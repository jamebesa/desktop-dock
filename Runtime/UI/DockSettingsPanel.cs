#if DESKTOPDOCK_UGUI
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopDock.UI
{
    /// <summary>
    /// A ready-made settings panel for the dock: edge, size, length and alignment, monitor, always on top, taskbar
    /// button, reserved screen space, stepping aside for fullscreen apps, start with Windows, and reset. It covers the
    /// dock while open and lays its controls out to fit a short horizontal strip or a narrow vertical one, scrolling
    /// when they don't fit. Create one with <see cref="Create"/> and open it from your own button or the tray menu.
    /// Changes apply immediately and are saved when the panel closes.
    /// </summary>
    [AddComponentMenu("Desktop Dock/Dock Settings Panel")]
    public sealed class DockSettingsPanel : MonoBehaviour
    {
        // The Size slider's range is the dock's ThicknessLimits (DockConfig.minThicknessPixels / maxThicknessPercent).
        private static readonly Vector2 CellSize = new Vector2(200, 28);
        private const float Spacing = 4f;
        private const float Padding = 8f;

        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private GridLayoutGroup _grid;
        private ContentSizeFitter _fitter;

        private readonly Dictionary<DockEdge, Image> _edgeButtons = new Dictionary<DockEdge, Image>();
        private readonly Dictionary<DockAlignment, Image> _alignButtons = new Dictionary<DockAlignment, Image>();
        private Slider _size, _length;
        private Text _sizeLabel, _lengthLabel, _monitorLabel;
        private Button _monitorButton;
        private Toggle _onTop, _taskbar, _autoHide, _reserve, _fullscreen, _softAlpha, _startup;
        private bool _built;
        private bool _refreshing;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Creates a closed panel that covers <paramref name="canvas"/> when opened.</summary>
        public static DockSettingsPanel Create(Canvas canvas)
        {
            var rt = DockUI.Rect("Dock Settings", canvas.transform);
            DockUI.Stretch(rt);
            var panel = rt.gameObject.AddComponent<DockSettingsPanel>();
            rt.gameObject.SetActive(false);
            return panel;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        public void Close()
        {
            var dock = DesktopDockController.Instance;
            if (dock != null && dock.HasUnsavedChanges) dock.SaveSettings();
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void Awake() => Build();

        private void OnEnable()
        {
            DesktopDockController.Docked += OnDocked;
            // An open panel keeps an auto-hiding dock on screen.
            DesktopDockController.Instance?.HoldAutoHide(this);
            Refresh();
        }

        private void OnDisable()
        {
            DesktopDockController.Docked -= OnDocked;
            var dock = DesktopDockController.Instance;
            if (dock == null) return;
            dock.ReleaseAutoHide(this);
            if (dock.HasUnsavedChanges) dock.SaveSettings();
        }

        private void OnDocked(DockState _) => Refresh();

        private void OnRectTransformDimensionsChange()
        {
            if (_built) Relayout();
        }

        // ------------------------------------------------------------ build

        private void Build()
        {
            if (_built) return;
            _built = true;

            var root = (RectTransform)transform;
            var bg = gameObject.AddComponent<Image>();
            bg.color = DockUI.PanelColor; // raycast target: the open panel catches every click on the dock

            _viewport = DockUI.Rect("Viewport", root);
            DockUI.Stretch(_viewport, Padding, Padding, Padding, Padding);
            _viewport.gameObject.AddComponent<RectMask2D>();

            _content = DockUI.Rect("Content", _viewport);
            _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0, 1);
            _grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            _grid.cellSize = CellSize;
            _grid.spacing = new Vector2(Spacing, Spacing);
            _fitter = _content.gameObject.AddComponent<ContentSizeFitter>();

            _scroll = gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            var title = DockUI.Text(_content, "Dock settings", 14, TextAnchor.MiddleLeft, DockUI.MutedTextColor);
            title.fontStyle = FontStyle.Bold;

            var edgeRow = Row("Edge");
            foreach (DockEdge edge in System.Enum.GetValues(typeof(DockEdge)))
            {
                var e = edge;
                var b = DockUI.Button(edgeRow, edge.ToString(), () => Dock?.SetEdge(e), out _, 11);
                _edgeButtons[edge] = (Image)b.targetGraphic;
            }

            _size = DockUI.Slider(_content, "Size", 74, out _sizeLabel);
            _size.wholeNumbers = true;
            _size.onValueChanged.AddListener(v =>
            {
                if (_refreshing) return;
                int px = Mathf.RoundToInt(v);
                _sizeLabel.text = "Size " + px;
                Dock?.SetThicknessPixels(px, save: false);
            });

            _length = DockUI.Slider(_content, "Length", 74, out _lengthLabel);
            _length.minValue = 10;
            _length.maxValue = 100;
            _length.wholeNumbers = true;
            _length.onValueChanged.AddListener(v =>
            {
                if (_refreshing) return;
                _lengthLabel.text = "Length " + Mathf.RoundToInt(v) + "%";
                Dock?.Modify(c => c.lengthPercent = v / 100f, save: false);
            });

            var alignRow = Row("Align");
            foreach (DockAlignment alignment in System.Enum.GetValues(typeof(DockAlignment)))
            {
                var a = alignment;
                var b = DockUI.Button(alignRow, alignment.ToString(), () => Dock?.Modify(c => c.alignment = a), out _, 11);
                _alignButtons[alignment] = (Image)b.targetGraphic;
            }

            _monitorButton = DockUI.Button(_content, "Monitor", NextMonitor, out _monitorLabel);

            _onTop = DockUI.Toggle(_content, "Always on top", v => Set(c => c.alwaysOnTop = v), out _);
            _taskbar = DockUI.Toggle(_content, "Show in taskbar", v => Set(c => c.showInTaskbar = v), out _);
            _autoHide = DockUI.Toggle(_content, "Auto-hide", v => Set(c => c.autoHide = v), out _);
            _reserve = DockUI.Toggle(_content, "Reserve screen space", v => Set(c => c.reserveScreenSpace = v), out _);
            _fullscreen = DockUI.Toggle(_content, "Step aside for fullscreen", v => Set(c =>
                c.fullscreenBehavior = v ? DockFullscreenBehavior.DropBehind : DockFullscreenBehavior.Ignore), out _);
            _softAlpha = DockUI.Toggle(_content, "Soft transparency", v => Set(c =>
                c.transparencyMode = v ? DockTransparencyMode.PerPixelAlpha : DockTransparencyMode.ColorKey), out _);
            _startup = DockUI.Toggle(_content, DockStartup.IsSupported ? DockStartup.DisplayName : DockStartup.DisplayName + " (builds only)",
                v =>
                {
                    if (_refreshing) return;
                    DockStartup.SetEnabled(v);
                    Refresh();
                }, out _);
            _startup.interactable = DockStartup.IsSupported;

            DockUI.Button(_content, "Reset to defaults", () => Dock?.ResetToDefaults(), out _);
            var done = DockUI.Button(_content, "Done", Close, out _);
            ((Image)done.targetGraphic).color = DockUI.AccentColor;

            Relayout();
        }

        private RectTransform Row(string label)
        {
            var cell = DockUI.Rect(label, _content);
            var layout = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 2;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return cell;
        }

        private static DesktopDockController Dock => DesktopDockController.Instance;

        private void Set(System.Action<DockConfig> change)
        {
            if (!_refreshing) Dock?.Modify(change);
        }

        private void NextMonitor()
        {
            var dock = Dock;
            if (dock == null) return;
            int count = dock.Monitors.Count;
            if (count < 2) return;
            int current = DesktopDockController.CurrentState?.MonitorIndex ?? 0;
            dock.SetMonitor((current + 1) % count);
        }

        // ------------------------------------------------------------ state

        /// <summary>Show the dock's current settings. Called on open and after every dock pass.</summary>
        public void Refresh()
        {
            var dock = Dock;
            if (!_built || dock == null) return;
            _refreshing = true;
            try
            {
                var cfg = dock.Config;
                var state = DesktopDockController.CurrentState;

                foreach (var pair in _edgeButtons)
                    pair.Value.color = pair.Key == cfg.edge ? DockUI.AccentColor : DockUI.ControlColor;
                foreach (var pair in _alignButtons)
                    pair.Value.color = pair.Key == cfg.alignment ? DockUI.AccentColor : DockUI.ControlColor;

                var monitors = dock.Monitors;
                int monitorIndex = Mathf.Clamp(state?.MonitorIndex ?? 0, 0, Mathf.Max(0, monitors.Count - 1));
                var limits = dock.ThicknessLimits;
                _size.minValue = limits.x;
                _size.maxValue = Mathf.Max(limits.x + 1, limits.y);
                // From the config, not the window, so a slider drag isn't fought by a dock pass that lags behind it.
                int thickness = cfg.thicknessUnit == DockSizeUnit.Pixels || !state.HasValue || state.Value.IsSimulated
                    ? cfg.thicknessPixels
                    : state.Value.Thickness;
                _size.value = thickness;
                _sizeLabel.text = "Size " + thickness;

                _length.value = Mathf.Round(cfg.lengthPercent * 100f);
                _lengthLabel.text = "Length " + Mathf.RoundToInt(cfg.lengthPercent * 100f) + "%";

                _monitorLabel.text = monitors.Count > 1 ? $"Monitor {monitorIndex + 1} of {monitors.Count}" : "Monitor 1 of 1";
                _monitorButton.interactable = monitors.Count > 1;

                _onTop.isOn = cfg.alwaysOnTop;
                _taskbar.isOn = cfg.showInTaskbar;
                _reserve.isOn = cfg.reserveScreenSpace;
                _fullscreen.isOn = cfg.fullscreenBehavior != DockFullscreenBehavior.Ignore;
                _autoHide.isOn = cfg.autoHide;
                // An auto-hiding dock never reserves space, so the reserve toggle has no effect while it's on.
                _reserve.interactable = !cfg.autoHide;
                // Where ColorKey can't work (Direct3D12, Vulkan, flip model) soft transparency is the only option, so the
                // toggle shows it on and can't be turned off.
                _softAlpha.isOn = dock.ActiveTransparencyMode == DockTransparencyMode.PerPixelAlpha;
                _softAlpha.interactable = cfg.transparentBackground && dock.IsColorKeySupported;
                _startup.isOn = DockStartup.IsEnabled;
            }
            finally
            {
                _refreshing = false;
            }
            Relayout();
        }

        /// <summary>Fill columns in a short horizontal dock and rows in a narrow vertical one; scroll the rest.</summary>
        private void Relayout()
        {
            if (_grid == null) return;
            var rect = _viewport.rect;
            var edge = DesktopDockController.CurrentState?.Edge ?? Dock?.Config.edge ?? DockEdge.Bottom;
            bool horizontal = edge.Orientation() == DockOrientation.Horizontal;

            if (horizontal)
            {
                _grid.startAxis = GridLayoutGroup.Axis.Vertical;
                _grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                _grid.constraintCount = Mathf.Max(1, Mathf.FloorToInt((rect.height + Spacing) / (CellSize.y + Spacing)));
                _fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                _fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                _grid.constraintCount = Mathf.Max(1, Mathf.FloorToInt((rect.width + Spacing) / (CellSize.x + Spacing)));
                _fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                _fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            _scroll.horizontal = horizontal;
            _scroll.vertical = !horizontal;
            LayoutRebuilder.MarkLayoutForRebuild(_content);
        }
    }
}
#endif
