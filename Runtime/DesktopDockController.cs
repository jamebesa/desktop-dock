using System;
using System.Collections;
using System.Collections.Generic;
using DesktopDock.Platform;
using UnityEngine;
#if DESKTOPDOCK_UGUI
using UnityEngine.EventSystems;
#endif

namespace DesktopDock
{
    /// <summary>
    /// Put one of these in your first scene. In a Windows build (or a macOS build, untested) it turns the player window into a borderless,
    /// optionally transparent and click-through strip docked to an edge of a monitor. In the Editor it only
    /// simulates docking (publishes <see cref="Docked"/> with the configured edge) so layout code can be tested.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("Desktop Dock/Desktop Dock Controller")]
    public sealed class DesktopDockController : MonoBehaviour
    {
        [SerializeField, Tooltip("Project defaults. The live copy is in Config and can be changed at runtime.")]
        private DockConfig defaults = new DockConfig();

        [SerializeField, Tooltip("Save changes made through the runtime API (edge, size, monitor…) to PlayerPrefs and restore them next launch.")]
        private bool rememberUserChanges = true;

        [SerializeField] private string preferencesKey = "DesktopDock.Config";

        [SerializeField, Tooltip("Keep this object (and the dock) alive across scene loads.")]
        private bool persistAcrossScenes = true;

        [SerializeField, Tooltip("Camera for 2D/3D physics hit tests. Leave empty to test every enabled camera.")]
        private Camera hitTestCamera;

        [SerializeField, Tooltip("Seconds between checks for monitor, resolution or taskbar changes.")]
        [Min(0.25f)] private float displayPollInterval = 2f;

        /// <summary>The active controller, or null.</summary>
        public static DesktopDockController Instance { get; private set; }

        /// <summary>Raised after every dock pass (startup, config change, monitor change). Also raised in the Editor with a simulated state.</summary>
        public static event Action<DockState> Docked;

        /// <summary>The most recent dock result, or null before the first pass. Late subscribers should read this.</summary>
        public static DockState? CurrentState { get; private set; }

        /// <summary>Raised when <see cref="SetHidden"/> changes whether the dock is hidden.</summary>
        public static event Action<bool> HiddenChanged;

        /// <summary>Raised when a fullscreen app starts or stops covering the dock's monitor. Only checked while
        /// <see cref="DockConfig.fullscreenBehavior"/> isn't Ignore.</summary>
        public static event Action<bool> FullscreenAppChanged;

        private const float FullscreenCheckInterval = 0.5f;

        private IDockPlatform _platform;
        private DockConfig _config;
        private bool _initialized;
        private bool _initAttempted;
        private bool _applying;
        private bool _dirty;
        private string _displaySignature;
        private float _nextPoll;
        private RectInt _placedRect;
        private bool _hasPlaced;
        private float _guardUntil;
        private float _nextGuardCheck;
        private int _placementCorrections;
        private DockMonitor _placedMonitor;
        private bool _userHidden;
        private bool _fullscreenActive;
        private float _nextFullscreenCheck;
        private bool _windowVisible = true;
        private bool _droppedBehind;
        private bool _unsaved;

        private readonly List<IDockHitTestProvider> _hitTestProviders = new List<IDockHitTestProvider>();
        private Camera[] _cameraBuffer = new Camera[8];
        private Vector2 _cursor;
        private bool _cursorInside;
        private bool _pixelHit;
        private Texture2D _pixel;
        private DockAlphaPresenter _alphaPresenter;

#if DESKTOPDOCK_UGUI
        private PointerEventData _pointerData;
        private readonly List<RaycastResult> _uiResults = new List<RaycastResult>();
#endif

        /// <summary>A copy of the live configuration. Change it through the Set* methods, <see cref="Modify"/> or <see cref="ApplyConfig"/>.</summary>
        public DockConfig Config => _config.Clone();

        /// <summary>A copy of the inspector defaults.</summary>
        public DockConfig Defaults => defaults.Clone();

        /// <summary>True when a real window is being docked (Windows or macOS player), false in the Editor / unsupported platforms.</summary>
        public bool IsDockingActive => _initialized;

        /// <summary>Whether the cursor is over something the hit test considers content.</summary>
        public bool IsPointerOverContent { get; private set; }

        /// <summary>Whether clicks are currently passing through the window.</summary>
        public bool IsClickThrough { get; private set; }

        public IReadOnlyList<DockMonitor> Monitors => _platform.GetMonitors();

        /// <summary>Whether the dock was hidden with <see cref="SetHidden"/>, for example from the tray icon.</summary>
        public bool IsHidden => _userHidden;

        /// <summary>Whether a fullscreen app currently covers the dock's monitor.</summary>
        public bool IsFullscreenAppActive => _fullscreenActive;

        /// <summary>The <see cref="DockTransparencyMode.PerPixelAlpha"/> frame pump (null in ColorKey mode). For diagnostics.</summary>
        internal DockAlphaPresenter AlphaPresenter => _alphaPresenter;

        /// <summary>Whether some settings were changed with <c>save: false</c> and not saved yet.</summary>
        public bool HasUnsavedChanges => _unsaved;

        internal IDockPlatform Platform => _platform;

        /// <summary>
        /// Whether <see cref="DockTransparencyMode.ColorKey"/> works with this build's graphics setup (Direct3D11 without
        /// the flip-model swapchain). Always true in the Editor, where nothing is docked.
        /// </summary>
        public bool IsColorKeySupported => _platform == null || _platform.SupportsColorKey;

        /// <summary>
        /// The transparency mode actually in use. Usually <see cref="DockConfig.transparencyMode"/>; PerPixelAlpha when
        /// ColorKey is configured but <see cref="IsColorKeySupported"/> is false (the configured mode isn't changed or saved).
        /// </summary>
        public DockTransparencyMode ActiveTransparencyMode => EffectiveMode(_config);

        private DockTransparencyMode EffectiveMode(DockConfig c) =>
            c.transparencyMode == DockTransparencyMode.ColorKey && !IsColorKeySupported
                ? DockTransparencyMode.PerPixelAlpha
                : c.transparencyMode;

        private bool _loggedColorKeyFallback;

        /// <summary>The config the window is styled with: <paramref name="c"/> with the transparency mode it can actually use.</summary>
        private DockConfig StyleConfig(DockConfig c)
        {
            var mode = EffectiveMode(c);
            if (mode == c.transparencyMode) return c;
            // Where the OS blends alpha itself (macOS) that's simply how it works, so there's nothing to report.
            if (c.transparentBackground && !_loggedColorKeyFallback && !_platform.BlendsAlphaNatively)
            {
                _loggedColorKeyFallback = true;
                Debug.Log("[DesktopDock] ColorKey transparency needs Direct3D11 without the flip-model swapchain " +
                          $"(running {SystemInfo.graphicsDeviceType}); using PerPixelAlpha instead.");
            }
            var styled = c.Clone();
            styled.transparencyMode = mode;
            return styled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Docked = null;
            CurrentState = null;
            HiddenChanged = null;
            FullscreenAppChanged = null;
        }

        // ================================================================ lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[DesktopDock] A DesktopDockController already exists; removing the duplicate on " + name, this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (persistAcrossScenes)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }

            _config = LoadConfig();
            _platform = DockPlatformFactory.Create();
            Application.runInBackground = true;
            ApplyFrameRate(Application.isFocused);
        }

        private IEnumerator Start()
        {
            // Let the player finish creating its window before we restyle it.
            yield return null;
            _initialized = _platform.IsSupported && _platform.Initialize();
            _initAttempted = true;
            if (_initialized)
            {
                _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                StartCoroutine(SamplePixelRoutine());
            }
            RequestApply();
        }

        private void Update()
        {
            if (!_initialized) return;

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + displayPollInterval;
                if (!_applying && DisplaySignature(_platform.GetMonitors(), _config) != _displaySignature)
                    RequestApply();
            }

            UpdateFullscreenApp();
            GuardPlacement();
            UpdateClickThrough();
        }

        private void UpdateFullscreenApp()
        {
            if (Time.unscaledTime < _nextFullscreenCheck || !_hasPlaced) return;
            _nextFullscreenCheck = Time.unscaledTime + FullscreenCheckInterval;

            bool active = _config.fullscreenBehavior != DockFullscreenBehavior.Ignore
                          && _platform.IsFullscreenAppActive(_placedMonitor.bounds);
            if (active == _fullscreenActive) return;
            _fullscreenActive = active;
            ApplyVisibilityAndOrder();
            FullscreenAppChanged?.Invoke(active);
        }

        /// <summary>Show or hide the window and set its z-order from the hidden flag, the fullscreen state and the config.</summary>
        private void ApplyVisibilityAndOrder()
        {
            if (!_initialized || !_hasPlaced) return;

            bool stepAside = _fullscreenActive;
            bool visible = !_userHidden && !(stepAside && _config.fullscreenBehavior == DockFullscreenBehavior.Hide);
            bool dropBehind = stepAside && _config.fullscreenBehavior == DockFullscreenBehavior.DropBehind;

            if (visible != _windowVisible)
            {
                _windowVisible = visible;
                _platform.SetVisible(visible);
            }
            PlaceWindow(_placedRect, dropBehind);
        }

        private void PlaceWindow(RectInt rect, bool dropBehind)
        {
            _platform.SetWindowRect(rect, _config.alwaysOnTop);
            if (dropBehind) _platform.SendToBack();
            _droppedBehind = dropBehind;
        }

        /// <summary>
        /// Unity (or another app) can resize/move the window after we place it, e.g. when a resolution change lands
        /// late. Check every frame for a few seconds after each dock pass, then once a second, and put it back.
        /// </summary>
        private void GuardPlacement()
        {
            if (!_hasPlaced || _applying) return;
            float now = Time.unscaledTime;
            if (now >= _guardUntil && now < _nextGuardCheck) return;
            _nextGuardCheck = now + 1f;

            if (!_platform.TryGetWindowRect(out var actual) || actual.Equals(_placedRect)) return;

            _platform.ApplyWindowStyle(StyleConfig(_config));
            IsClickThrough = false;
            PlaceWindow(_placedRect, _droppedBehind);
            if (++_placementCorrections == 20)
                Debug.LogWarning("[DesktopDock] Something keeps moving or resizing the dock window; it is being put back each time.");
        }

        private void OnApplicationFocus(bool focused) => ApplyFrameRate(focused);

        private void OnApplicationQuit()
        {
            if (_unsaved) SaveConfig();
            _platform?.Shutdown();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            _platform?.Shutdown();
            _alphaPresenter?.Dispose();
            if (_pixel != null) Destroy(_pixel);
            Instance = null;
        }

        // ================================================================ public API

        public void SetEdge(DockEdge edge) => Modify(c => c.edge = edge);

        /// <param name="monitorIndex">-1 for the primary monitor, otherwise an index into <see cref="Monitors"/>.</param>
        public void SetMonitor(int monitorIndex) => Modify(c => c.monitorIndex = monitorIndex);

        public void SetThicknessPixels(int pixels) => Modify(c =>
        {
            c.thicknessUnit = DockSizeUnit.Pixels;
            c.thicknessPixels = Mathf.Max(16, pixels);
        });

        public void SetThicknessPercent(float fraction) => Modify(c =>
        {
            c.thicknessUnit = DockSizeUnit.PercentOfScreen;
            c.thicknessPercent = Mathf.Clamp(fraction, 0.02f, 0.9f);
        });

        public void SetLength(float fraction, DockAlignment alignment) => Modify(c =>
        {
            c.lengthPercent = Mathf.Clamp(fraction, 0.1f, 1f);
            c.alignment = alignment;
        });

        public void SetAlwaysOnTop(bool value) => Modify(c => c.alwaysOnTop = value);
        public void SetRespectTaskbar(bool value) => Modify(c => c.respectTaskbar = value);
        public void SetReserveScreenSpace(bool value) => Modify(c => c.reserveScreenSpace = value);
        public void SetClickThroughEnabled(bool value) => Modify(c => c.clickThroughTransparentAreas = value);
        public void SetShowInTaskbar(bool value) => Modify(c => c.showInTaskbar = value);

        /// <summary>Change any number of settings in one go, then re-dock once.</summary>
        public void Modify(Action<DockConfig> change) => Modify(change, true);

        /// <summary>
        /// Like <see cref="Modify(Action{DockConfig})"/>. Pass <paramref name="save"/> false for changes that happen
        /// many times a second (a slider or a drag), then call <see cref="SaveSettings"/> once at the end. Unsaved
        /// changes are also saved when the game quits.
        /// </summary>
        public void Modify(Action<DockConfig> change, bool save)
        {
            change(_config);
            if (save) SaveConfig();
            else _unsaved = true;
            ApplyFrameRate(Application.isFocused);
            if (_config.fullscreenBehavior == DockFullscreenBehavior.Ignore && _fullscreenActive)
            {
                _fullscreenActive = false;
                FullscreenAppChanged?.Invoke(false);
            }
            RequestApply();
        }

        /// <summary>Save the current settings to PlayerPrefs now (if remembering changes is on).</summary>
        public void SaveSettings() => SaveConfig();

        /// <summary>
        /// Hide the dock window completely, or show it again. Use it for a "hide" command in a tray menu or a hotkey:
        /// a hidden dock has no window to click, so the game must offer a way back (see <c>DockTrayIcon</c>).
        /// Reserved screen space is released while hidden.
        /// </summary>
        public void SetHidden(bool hidden)
        {
            if (hidden == _userHidden) return;
            _userHidden = hidden;
            if (_initialized && _config.reserveScreenSpace)
            {
                if (hidden) _platform.ReleaseReservedSpace();
                else RequestApply();
            }
            ApplyVisibilityAndOrder();
            HiddenChanged?.Invoke(hidden);
        }

        public void ApplyConfig(DockConfig config)
        {
            _config = config.Clone();
            SaveConfig();
            ApplyFrameRate(Application.isFocused);
            RequestApply();
        }

        /// <summary>Forget saved changes and go back to the inspector defaults.</summary>
        [ContextMenu("Reset To Defaults")]
        public void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(preferencesKey);
            _unsaved = false;
            _config = defaults.Clone();
            ApplyFrameRate(Application.isFocused);
            RequestApply();
        }

        /// <summary>Run the dock pass again with the current settings.</summary>
        [ContextMenu("Redock")]
        public void Redock() => RequestApply();

        public void AddHitTestProvider(IDockHitTestProvider provider)
        {
            if (provider != null && !_hitTestProviders.Contains(provider)) _hitTestProviders.Add(provider);
        }

        public void RemoveHitTestProvider(IDockHitTestProvider provider) => _hitTestProviders.Remove(provider);

        // ================================================================ docking

        private void RequestApply()
        {
            if (_platform == null || !_initAttempted) return; // Start() runs the first pass with whatever was set by then
            if (_applying)
            {
                _dirty = true;
                return;
            }
            StartCoroutine(ApplyRoutine());
        }

        private IEnumerator ApplyRoutine()
        {
            _applying = true;
            do
            {
                _dirty = false;
                var cfg = _config.Clone();

                if (!_initialized)
                {
                    Publish(new DockState(cfg.edge, new RectInt(0, 0, Screen.width, Screen.height), 0, isSimulated: true));
                    continue;
                }

                var monitors = _platform.GetMonitors();
                int monitorIndex = ResolveMonitor(monitors, cfg.monitorIndex);
                var monitor = monitors.Count > 0 ? monitors[monitorIndex] : default;

                // A hidden dock doesn't hold on to reserved screen space.
                var placeCfg = cfg;
                if (_userHidden && cfg.reserveScreenSpace)
                {
                    placeCfg = cfg.Clone();
                    placeCfg.reserveScreenSpace = false;
                }
                RectInt rect = _platform.PlaceDock(placeCfg, monitor);

                if (Screen.width != rect.width || Screen.height != rect.height)
                {
                    // Resize Unity's backbuffer to match. The player applies this a few frames later and sizes
                    // the window as if it still had a title bar, so wait for it to land before styling/placing.
                    Screen.SetResolution(rect.width, rect.height, FullScreenMode.Windowed);
                    for (int i = 0; i < 60 && (Screen.width != rect.width || Screen.height != rect.height); i++)
                        yield return null;
                    yield return null;
                }

                _platform.ApplyWindowStyle(StyleConfig(cfg));
                IsClickThrough = false;
                _placedRect = rect;
                _placedMonitor = monitor;
                _hasPlaced = true;
                ApplyVisibilityAndOrder();
                _guardUntil = Time.unscaledTime + 3f;
                _displaySignature = DisplaySignature(monitors, cfg);
                Publish(new DockState(cfg.edge, rect, monitorIndex, isSimulated: false));
            } while (_dirty);
            _applying = false;
        }

        private static int ResolveMonitor(IReadOnlyList<DockMonitor> monitors, int requested)
        {
            if (requested >= 0 && requested < monitors.Count) return requested;
            for (int i = 0; i < monitors.Count; i++)
                if (monitors[i].isPrimary) return i;
            return 0;
        }

        private static string DisplaySignature(IReadOnlyList<DockMonitor> monitors, DockConfig cfg)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in monitors)
            {
                sb.Append(m.bounds).Append(m.isPrimary);
                // As an app bar we change the work area ourselves, so don't let it trigger a redock loop.
                if (!cfg.reserveScreenSpace) sb.Append(m.workArea);
                sb.Append('|');
            }
            return sb.ToString();
        }

        private static void Publish(DockState state)
        {
            CurrentState = state;
            Docked?.Invoke(state);
        }

        // ================================================================ click-through

        private void UpdateClickThrough()
        {
            if (!_config.transparentBackground || !_config.clickThroughTransparentAreas)
            {
                SetClickThrough(false);
                IsPointerOverContent = true;
                return;
            }

            if (!_platform.TryGetCursorPosition(out _cursor, out _cursorInside)) return;

            bool hit = _cursorInside && HitTest(_cursor);
            IsPointerOverContent = hit;

            // Never flip while a button is held: that would cut a drag in half (ours or another app's).
            if (_platform.IsAnyMouseButtonDown()) return;
            SetClickThrough(!hit);
        }

        private void SetClickThrough(bool value)
        {
            if (IsClickThrough == value) return;
            _platform.SetClickThrough(value);
            IsClickThrough = value;
        }

        private bool HitTest(Vector2 pos)
        {
            for (int i = 0; i < _hitTestProviders.Count; i++)
                if (_hitTestProviders[i] != null && _hitTestProviders[i].IsContentAt(pos))
                    return true;

            var sources = _config.hitTestSources;

            if ((sources & DockHitTestSources.VisiblePixel) != 0 && _pixelHit) return true;

#if DESKTOPDOCK_UGUI
            if ((sources & DockHitTestSources.UI) != 0 && EventSystem.current != null)
            {
                _pointerData ??= new PointerEventData(EventSystem.current);
                _pointerData.Reset();
                _pointerData.position = pos;
                _uiResults.Clear();
                EventSystem.current.RaycastAll(_pointerData, _uiResults);
                if (_uiResults.Count > 0) return true;
            }
#endif

            bool test2D = (sources & DockHitTestSources.Physics2D) != 0;
            bool test3D = (sources & DockHitTestSources.Physics3D) != 0;
            if (!test2D && !test3D) return false;

            int count = CollectCameras();
            for (int i = 0; i < count; i++)
            {
                var cam = _cameraBuffer[i];
                if (cam == null || !cam.pixelRect.Contains(pos)) continue;
                var ray = cam.ScreenPointToRay(pos);
#if DESKTOPDOCK_PHYSICS2D
                if (test2D && Physics2D.GetRayIntersection(ray, cam.farClipPlane).collider != null) return true;
#endif
#if DESKTOPDOCK_PHYSICS
                if (test3D && Physics.Raycast(ray, cam.farClipPlane)) return true;
#endif
            }
            return false;
        }

        private int CollectCameras()
        {
            if (hitTestCamera != null)
            {
                _cameraBuffer[0] = hitTestCamera;
                return 1;
            }
            if (_cameraBuffer.Length < Camera.allCamerasCount)
                _cameraBuffer = new Camera[Camera.allCamerasCount];
            return Camera.GetAllCameras(_cameraBuffer);
        }

        // Copy frames to the window ourselves. Not needed where the OS blends the window's alpha itself (macOS): there
        // the one-pixel readback below reads alpha instead.
        private bool UsesPerPixelAlpha =>
            _config.transparentBackground && ActiveTransparencyMode == DockTransparencyMode.PerPixelAlpha
            && !_platform.BlendsAlphaNatively;

        /// <summary>
        /// Runs after rendering. In PerPixelAlpha mode it captures the frame for the window, and the hit test reads the
        /// alpha under the cursor from that frame. In ColorKey mode it reads back the one pixel under the cursor so the
        /// see-through areas can be detected.
        /// </summary>
        private IEnumerator SamplePixelRoutine()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;

                bool wanted = _config.transparentBackground
                              && _config.clickThroughTransparentAreas
                              && (_config.hitTestSources & DockHitTestSources.VisiblePixel) != 0
                              && _cursorInside;

                if (UsesPerPixelAlpha)
                {
                    _alphaPresenter ??= new DockAlphaPresenter(_platform);
                    _alphaPresenter.SamplePoint = new Vector2Int((int)_cursor.x, (int)_cursor.y);
                    // A hidden window shows nothing, so skip the readback.
                    if (_windowVisible) _alphaPresenter.Capture();
                    _pixelHit = wanted && DockAlphaPresenter.IsContent(_alphaPresenter.SampledAlpha, _config.visibleAlphaThreshold);
                    continue;
                }
                if (_alphaPresenter != null)
                {
                    _alphaPresenter.Dispose();
                    _alphaPresenter = null;
                }

                if (!wanted)
                {
                    _pixelHit = false;
                    continue;
                }

                int x = Mathf.Clamp((int)_cursor.x, 0, Screen.width - 1);
                int y = Mathf.Clamp((int)_cursor.y, 0, Screen.height - 1);
                _pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
                Color32 c = _pixel.GetPixel(0, 0);
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                // macOS composites the window with its alpha (premultiplied): a pixel is see-through only
                // when it is fully transparent black.
                _pixelHit = (c.r | c.g | c.b | c.a) != 0;
#else
                // Pure black is the color key: exactly those pixels are see-through on screen, so only
                // they let clicks through. Alpha is deliberately ignored (the OS doesn't use it, and URP
                // discards it whenever post-processing or HDR is on).
                _pixelHit = (c.r | c.g | c.b) != 0;
#endif
            }
        }

        // ================================================================ config persistence & frame rate

        private DockConfig LoadConfig()
        {
            var cfg = defaults.Clone();
            if (!rememberUserChanges || !PlayerPrefs.HasKey(preferencesKey)) return cfg;
            try
            {
                JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(preferencesKey), cfg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DesktopDock] Ignoring unreadable saved dock settings: " + e.Message);
                cfg = defaults.Clone();
            }
            return cfg;
        }

        private void SaveConfig()
        {
            _unsaved = false;
            if (!rememberUserChanges) return;
            PlayerPrefs.SetString(preferencesKey, JsonUtility.ToJson(_config));
            PlayerPrefs.Save();
        }

        private void ApplyFrameRate(bool focused)
        {
            if (_config == null) return;
            int rate = focused ? _config.focusedFrameRate : _config.backgroundFrameRate;
            if (rate > 0) QualitySettings.vSyncCount = 0; // targetFrameRate is ignored while vSync is on
            Application.targetFrameRate = rate;
        }
    }
}
