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
    /// Self-building demo. Add this to an empty GameObject in an empty scene, apply the recommended Player Settings
    /// (Tools > Desktop Dock), and build for Windows. You get a transparent strip with a strip of ground on the
    /// docked edge, critters you can click, and buttons to move the dock between edges. It also shows the
    /// package's ready-made pieces: a grip on the inner edge to resize the dock, a settings panel, and a tray icon.
    /// </summary>
    public sealed class DockDemo : MonoBehaviour
    {
        [Min(1)] public int critterCount = 6;
        [Tooltip("World units visible across the dock.")]
        public float worldUnitsAcross = 4f;
        [Tooltip("Ground thickness in world units.")]
        public float groundThickness = 0.6f;

        private Camera _camera;
        private Transform _ground;
        private Text _scoreText;
        private Text _pinLabel;
        private DockSettingsPanel _settings;
        private int _score;
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
            for (int i = 0; i < critterCount; i++) SpawnCritter(i);
            SetupUI();
            SetupTray();
        }

        private void SetupTray()
        {
            // On the controller's object, which survives scene loads.
            var host = DesktopDockController.Instance.gameObject;
            if (!host.TryGetComponent<DockTrayIcon>(out var tray)) tray = host.AddComponent<DockTrayIcon>();
            tray.AddMenuItem("Settings...", () =>
            {
                DesktopDockController.Instance.SetHidden(false);
                _settings.Open();
            });
        }

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

        private void SpawnCritter(int i)
        {
            var go = new GameObject("Critter " + i);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = Color.HSVToRGB(Random.value, 0.55f, 0.95f);
            go.AddComponent<BoxCollider2D>();
            go.transform.localScale = Vector3.one * 0.6f;
            var critter = go.AddComponent<DockDemoCritter>();
            critter.Init(this, Random.Range(-1f, 1f));
            _critters.Add(critter);
        }

        private void LateUpdate()
        {
            // The tray menu and settings panel can change this too.
            string pin = PinText();
            if (_pinLabel.text != pin) _pinLabel.text = pin;

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

        public void AddScore()
        {
            _score++;
            _scoreText.text = "Boops: " + _score;
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

            var panel = new GameObject("Buttons", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(canvasGo.transform, false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 1);
            prt.anchoredPosition = new Vector2(8, -8);
            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(64, 26);
            grid.spacing = new Vector2(4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            var fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddButton(prt, "Bottom", () => DesktopDockController.Instance.SetEdge(DockEdge.Bottom));
            AddButton(prt, "Top", () => DesktopDockController.Instance.SetEdge(DockEdge.Top));
            AddButton(prt, "Left", () => DesktopDockController.Instance.SetEdge(DockEdge.Left));
            AddButton(prt, "Right", () => DesktopDockController.Instance.SetEdge(DockEdge.Right));
            _pinLabel = AddButton(prt, PinText(), () =>
            {
                var dock = DesktopDockController.Instance;
                dock.SetAlwaysOnTop(!dock.Config.alwaysOnTop);
                _pinLabel.text = PinText();
            });
            AddButton(prt, "Settings", () => _settings.Toggle());
            AddButton(prt, "Quit", Application.Quit);

            // Bottom-right keeps it clear of the button grid in narrow side docks too.
            _scoreText = MakeText(canvasGo.transform, "Boops: 0", 16, TextAnchor.LowerRight);
            var srt = _scoreText.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 0);
            srt.anchoredPosition = new Vector2(-8, 8);
            srt.sizeDelta = new Vector2(160, 24);

            // Drag the grip on the inner edge to resize the dock.
            DockResizeHandle.Create(canvasGo.transform);
            // Created last so it covers everything when open.
            _settings = DockSettingsPanel.Create(canvas);
        }

        private static T FindExisting<T>() where T : Object =>
#if UNITY_2022_2_OR_NEWER
            FindAnyObjectByType<T>();
#else
            FindObjectOfType<T>();
#endif

        private static string PinText() =>
            DesktopDockController.Instance != null && DesktopDockController.Instance.Config.alwaysOnTop ? "Pinned" : "Unpinned";

        private static Text AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.92f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
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
