using System;
using System.Collections.Generic;
using DesktopDock;
using DesktopDock.Layout;
using DesktopDock.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DesktopDock.Samples
{
    /// <summary>
    /// Self-building demo: a tiny idle game. Add this to an empty GameObject in an empty scene, apply the recommended
    /// Player Settings (Tools > Desktop Dock), and build for Windows. Critters wander a strip of ground on the docked
    /// edge and earn coins over time, also while the game is closed (up to a limit). Click one for a bonus, and spend
    /// coins on more critters. Progress is saved in PlayerPrefs.
    /// It also shows the package's ready-made pieces: a settings panel for edge, size, always on top, auto-hide and the
    /// rest; a Move button to drag the dock to another edge; a grip on the inner edge to resize it; a tray icon; and
    /// Ctrl+Alt+D to show or hide the dock from any app.
    /// </summary>
    public sealed class DockDemo : MonoBehaviour
    {
        [Header("Idle game")]
        [Min(1)] public int startingCritters = 3;
        [Tooltip("Coins each critter earns per second.")]
        public float coinsPerCritterPerSecond = 0.25f;
        [Tooltip("Coins for clicking a critter.")]
        public int boopCoins = 5;
        public int firstCritterCost = 10;
        [Tooltip("Each critter costs this much more than the last.")]
        public float costGrowth = 1.5f;
        [Tooltip("Earnings while the game is closed are capped at this many hours.")]
        public float maxOfflineHours = 8f;
        [Min(1)] public int maxCritters = 40;

        [Header("Look")]
        [Tooltip("World units visible across the dock.")]
        public float worldUnitsAcross = 4f;
        [Tooltip("Ground thickness in world units.")]
        public float groundThickness = 0.6f;

        private const string SaveKey = "DockDemo.Save";
        private const float AutosaveSeconds = 10f;

        [Serializable]
        private sealed class SaveData
        {
            public double coins;
            public int critters;
            public long savedUtcTicks;
        }

        private Camera _camera;
        private Transform _ground;
        private Text _coinText;
        private Text _buyLabel;
        private Text _toast;
        private float _toastUntil;
        private DockSettingsPanel _settings;
        private double _coins;
        private int _owned;
        private float _nextAutosave;
        private static Sprite s_square;
        private static Sprite s_shadow;

        private readonly List<DockDemoCritter> _critters = new List<DockDemoCritter>();

        public static Sprite Square
        {
            get
            {
                if (s_square != null) return s_square;
                var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                s_square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                return s_square;
            }
        }

        /// <summary>
        /// A soft, pure-black blob. With the default ColorKey transparency it disappears where it overlaps the
        /// desktop (black is the color key); turn on "Soft transparency" in Settings (PerPixelAlpha) to see it blend.
        /// </summary>
        public static Sprite Shadow
        {
            get
            {
                if (s_shadow != null) return s_shadow;
                const int w = 64, h = 32;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                    float falloff = Mathf.Clamp01(1f - (dx * dx + dy * dy));
                    pixels[y * w + x] = new Color32(0, 0, 0, (byte)(255 * 0.6f * falloff * falloff));
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                s_shadow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
                return s_shadow;
            }
        }

        private void Awake()
        {
            if (DesktopDockController.Instance == null)
                new GameObject("Desktop Dock").AddComponent<DesktopDockController>();

            SetupCamera();
            SetupGround();
            SetupUI();
            SetupTrayAndHotkey();
            LoadGame();
        }

        private void SetupTrayAndHotkey()
        {
            // On the controller's object, which survives scene loads.
            var host = DesktopDockController.Instance.gameObject;
            if (!host.TryGetComponent<DockTrayIcon>(out var tray)) tray = host.AddComponent<DockTrayIcon>();
            tray.AddMenuItem("Settings...", () =>
            {
                DesktopDockController.Instance.SetHidden(false);
                _settings.Open();
            });
            // Ctrl+Alt+D shows or hides the dock from any app (DockHotkeyAction.ToggleShowHide by default).
            if (!host.TryGetComponent<DockHotkey>(out _)) host.AddComponent<DockHotkey>();
        }

        // ------------------------------------------------------------ idle game

        private double CoinsPerSecond => _owned * coinsPerCritterPerSecond;

        private int NextCost => Mathf.RoundToInt(firstCritterCost * Mathf.Pow(costGrowth, _owned - startingCritters));

        private void LoadGame()
        {
            var save = new SaveData { critters = startingCritters };
            if (PlayerPrefs.HasKey(SaveKey))
            {
                try { JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(SaveKey), save); }
                catch (Exception e) { Debug.LogWarning("Ignoring an unreadable save: " + e.Message); }
            }

            _coins = Math.Max(0, save.coins);
            int critters = Mathf.Clamp(save.critters, startingCritters, maxCritters);
            for (int i = 0; i < critters; i++) SpawnCritter();

            // What the critters earned while the game was closed.
            if (save.savedUtcTicks > 0)
            {
                double away = (DateTime.UtcNow - new DateTime(save.savedUtcTicks, DateTimeKind.Utc)).TotalSeconds;
                away = Math.Min(Math.Max(0, away), maxOfflineHours * 3600.0);
                double earned = away * CoinsPerSecond;
                if (earned >= 1)
                {
                    _coins += earned;
                    ShowToast("While you were away: +" + Format(earned) + " coins");
                }
            }
            RefreshLabels();
            SaveGame();
        }

        private void SaveGame()
        {
            var save = new SaveData { coins = _coins, critters = _owned, savedUtcTicks = DateTime.UtcNow.Ticks };
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
            _nextAutosave = Time.unscaledTime + AutosaveSeconds;
        }

        private void OnApplicationQuit() => SaveGame();

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveGame();
        }

        /// <summary>A critter was clicked.</summary>
        public void Boop()
        {
            _coins += boopCoins;
            RefreshLabels();
        }

        private void Buy()
        {
            if (_owned >= maxCritters || _coins < NextCost) return;
            _coins -= NextCost;
            SpawnCritter().Hop(10f);
            RefreshLabels();
            SaveGame();
        }

        private void Update()
        {
            // Update keeps running while the dock is unfocused, rendering less often, or hidden, so income keeps flowing.
            _coins += CoinsPerSecond * Time.unscaledDeltaTime;
            RefreshLabels();
            if (Time.unscaledTime >= _nextAutosave) SaveGame();

            if (_toast.enabled && Time.unscaledTime >= _toastUntil) _toast.enabled = false;

            UpdateScrollResize();
        }

        // ------------------------------------------------------------ resizing from game code

        private float _saveDockAt = -1f;
        private DockEdge? _lastEdge;

        /// <summary>
        /// Hold Ctrl and turn the mouse wheel over anything in the dock to make it taller or shorter: an example of
        /// resizing from your own input code with ResizeBy. (The resize grip also takes the wheel, without Ctrl.)
        /// The wheel only reaches the game over visible content: elsewhere the dock is click-through.
        /// </summary>
        private void UpdateScrollResize()
        {
            var dock = DesktopDockController.Instance;
            if (dock == null) return;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            bool ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
#else
            float wheel = Input.mouseScrollDelta.y;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
            if (ctrl && !Mathf.Approximately(wheel, 0f))
            {
                dock.ResizeBy(wheel > 0 ? 20 : -20, save: false);
                _saveDockAt = Time.unscaledTime + 0.5f;
            }
            // Save once the wheel has been still for a moment, not on every notch.
            if (_saveDockAt >= 0f && Time.unscaledTime >= _saveDockAt)
            {
                _saveDockAt = -1f;
                dock.SaveSettings();
            }
        }

        private void OnEnable() => DesktopDockController.Resized += OnDockResized;
        private void OnDisable() => DesktopDockController.Resized -= OnDockResized;

        /// <summary>
        /// Raised as the dock changes size, including on every step of a drag on the resize grip. The camera's
        /// DockCameraFitter already zooms the world to the new height (Thickness mode); this shows the new size.
        /// </summary>
        private void OnDockResized(DockState state)
        {
            bool sameEdge = _lastEdge == state.Edge;
            _lastEdge = state.Edge;
            if (sameEdge && _toast != null) ShowToast(state.Thickness + " px", 1f);
        }

        private void RefreshLabels()
        {
            if (_coinText == null) return;
            _coinText.text = Format(_coins) + " coins  (+" + CoinsPerSecond.ToString("0.##") + "/s)";
            _buyLabel.text = _owned >= maxCritters ? "Full" : "Buy " + NextCost;
            _buyLabel.color = _owned < maxCritters && _coins >= NextCost ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        private void ShowToast(string message, float seconds = 6f)
        {
            _toast.text = message;
            _toast.enabled = true;
            _toastUntil = Time.unscaledTime + seconds;
        }

        private static string Format(double value) => Math.Floor(value).ToString("N0");

        // ------------------------------------------------------------ scene

        private void SetupCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.transform.position = new Vector3(0, 0, -10);
            _camera.orthographic = true;
            if (!_camera.TryGetComponent<DockTransparentCamera>(out _)) _camera.gameObject.AddComponent<DockTransparentCamera>();
            if (!_camera.TryGetComponent<DockCameraFitter>(out var fitter)) fitter = _camera.gameObject.AddComponent<DockCameraFitter>();
            fitter.worldUnitsAcross = worldUnitsAcross;
            if (!_camera.TryGetComponent<Physics2DRaycaster>(out _)) _camera.gameObject.AddComponent<Physics2DRaycaster>();
        }

        private void SetupGround()
        {
            var go = new GameObject("Ground");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = new Color(0.36f, 0.55f, 0.27f, 1f);
            sr.sortingOrder = -2; // below the critters' shadows
            _ground = go.transform;
        }

        private DockDemoCritter SpawnCritter()
        {
            var go = new GameObject("Critter " + _owned);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = Color.HSVToRGB(UnityEngine.Random.value, 0.55f, 0.95f);
            go.AddComponent<BoxCollider2D>();
            go.transform.localScale = Vector3.one * 0.6f;
            var critter = go.AddComponent<DockDemoCritter>();
            critter.Init(this, UnityEngine.Random.Range(-1f, 1f));
            _critters.Add(critter);
            _owned++;
            return critter;
        }

        private void LateUpdate()
        {
            ViewExtents(out float halfW, out float halfH);
            float g = groundThickness;
            switch (Edge)
            {
                case DockEdge.Top:
                    _ground.position = new Vector3(0, halfH - g / 2);
                    _ground.localScale = new Vector3(halfW * 2, g, 1);
                    break;
                case DockEdge.Left:
                    _ground.position = new Vector3(-halfW + g / 2, 0);
                    _ground.localScale = new Vector3(g, halfH * 2, 1);
                    break;
                case DockEdge.Right:
                    _ground.position = new Vector3(halfW - g / 2, 0);
                    _ground.localScale = new Vector3(g, halfH * 2, 1);
                    break;
                default:
                    _ground.position = new Vector3(0, -halfH + g / 2);
                    _ground.localScale = new Vector3(halfW * 2, g, 1);
                    break;
            }
        }

        public DockEdge Edge => DesktopDockController.CurrentState?.Edge ?? DockEdge.Bottom;

        private void ViewExtents(out float halfW, out float halfH)
        {
            halfH = _camera.orthographicSize;
            halfW = halfH * _camera.aspect;
        }

        /// <summary>Half the usable length of the ground, in world units.</summary>
        public float HalfLength
        {
            get
            {
                ViewExtents(out float halfW, out float halfH);
                return Edge.Orientation() == DockOrientation.Horizontal ? halfW : halfH;
            }
        }

        /// <summary>World pose for something standing on the ground: <paramref name="along"/> runs along the edge,
        /// <paramref name="height"/> is distance above the ground surface.</summary>
        public void PlaceOnGround(Transform t, float along, float height)
        {
            ViewExtents(out float halfW, out float halfH);
            float g = groundThickness;
            switch (Edge)
            {
                case DockEdge.Top:
                    t.SetPositionAndRotation(new Vector3(along, halfH - g - height), Quaternion.Euler(0, 0, 180));
                    break;
                case DockEdge.Left:
                    t.SetPositionAndRotation(new Vector3(-halfW + g + height, along), Quaternion.Euler(0, 0, -90));
                    break;
                case DockEdge.Right:
                    t.SetPositionAndRotation(new Vector3(halfW - g - height, along), Quaternion.Euler(0, 0, 90));
                    break;
                default:
                    t.SetPositionAndRotation(new Vector3(along, -halfH + g + height), Quaternion.identity);
                    break;
            }
        }

        // ------------------------------------------------------------ UI

        private void SetupUI()
        {
            if (FindExisting<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                // Projects set to "Input System Package (New)" only, the default in many Unity 6 templates.
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            var canvasGo = new GameObject("Dock UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Same size in points on a Retina Mac as on Windows, instead of half size.
            canvasGo.AddComponent<DockCanvasScale>();

            var panel = new GameObject("Buttons", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(canvasGo.transform, false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 1);
            prt.anchoredPosition = new Vector2(8, -8);
            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(64, 26);
            grid.spacing = new Vector2(4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            var fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Edge and always-on-top live in the settings panel, so only what it doesn't offer is here.
            // Press and drag to move the dock to another edge or monitor.
            AddButton(prt, "Move", null).transform.parent.gameObject.AddComponent<DockMoveHandle>();
            _buyLabel = AddButton(prt, "Buy", Buy);
            AddButton(prt, "Settings", () => _settings.Toggle());
            AddButton(prt, "Quit", Application.Quit);

            // Bottom-right keeps it clear of the button grid in narrow side docks too.
            _coinText = MakeText(canvasGo.transform, "", 16, TextAnchor.LowerRight);
            var srt = _coinText.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 0);
            srt.anchoredPosition = new Vector2(-8, 8);
            srt.sizeDelta = new Vector2(260, 24);

            _toast = MakeText(canvasGo.transform, "", 15, TextAnchor.MiddleCenter);
            _toast.horizontalOverflow = HorizontalWrapMode.Wrap;
            var trt = _toast.rectTransform;
            trt.anchorMin = new Vector2(0, 0.5f);
            trt.anchorMax = new Vector2(1, 0.5f);
            trt.sizeDelta = new Vector2(-16, 48);
            _toast.enabled = false;

            // Drag the grip on the inner edge to resize the dock.
            DockResizeHandle.Create(canvasGo.transform);
            // Created last so it covers everything when open.
            _settings = DockSettingsPanel.Create(canvas);
        }

        private static T FindExisting<T>() where T : UnityEngine.Object =>
#if UNITY_2022_2_OR_NEWER
            FindAnyObjectByType<T>();
#else
            FindObjectOfType<T>();
#endif

        private static Text AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.92f);
            if (onClick != null) go.GetComponent<Button>().onClick.AddListener(onClick);
            var text = MakeText(go.transform, label, 13, TextAnchor.MiddleCenter);
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return text;
        }

        private static Text MakeText(Transform parent, string value, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
#if UNITY_2022_2_OR_NEWER
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            return text;
        }
    }
}
