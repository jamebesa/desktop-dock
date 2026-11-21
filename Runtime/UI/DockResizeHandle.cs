#if DESKTOPDOCK_UGUI
using DesktopDock.Platform;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DesktopDock.UI
{
    /// <summary>
    /// A grip on the dock's inner edge (the edge facing the desktop) that the player drags to make the dock thicker or
    /// thinner. The new size is saved when the drag ends. Put it on a UI Graphic with Raycast Target on, under a
    /// Screen Space Overlay canvas, or call <see cref="Create"/>. Only resizes in Windows and macOS builds.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Desktop Dock/Dock Resize Handle")]
    public sealed class DockResizeHandle : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Smallest thickness the player can drag to, in pixels.")]
        [Min(16)] public int minThickness = 100;

        [Tooltip("Largest thickness, as a fraction of the monitor's height (top/bottom docks) or width (left/right docks).")]
        [Range(0.05f, 0.9f)] public float maxFraction = 0.5f;

        [Tooltip("Move the grip to the inner edge whenever the dock changes edge.")]
        public bool autoPosition = true;

        [Tooltip("Grip size along the edge, in pixels. 0 = the whole edge.")]
        [Min(0)] public float gripLength = 120f;

        [Tooltip("Grip size across the edge, in pixels.")]
        [Min(2)] public float gripThickness = 8f;

        [Tooltip("Gap between the grip and the inner edge, in pixels.")]
        public float inset = 2f;

        [Tooltip("Graphic tinted on hover and while dragging. Defaults to the Graphic on this object.")]
        public Graphic tintTarget;

        public Color normalColor = new Color(1f, 1f, 1f, 0.28f);
        public Color activeColor = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>Whether the player is dragging the grip right now.</summary>
        public bool IsDragging { get; private set; }

        private bool _hover;
        private DockEdge _edge;
        private RectInt _startRect;
        private int _grabOffset;
        private int _maxThickness;
        private int _lastSent;

        /// <summary>Creates a grip under <paramref name="parent"/> (a RectTransform in a Screen Space Overlay canvas).</summary>
        public static DockResizeHandle Create(Transform parent)
        {
            var img = DockUI.Image("Dock Resize Handle", parent, Color.white);
            var handle = img.gameObject.AddComponent<DockResizeHandle>();
            handle.tintTarget = img;
            handle.UpdateTint();
            return handle;
        }

        private void Awake()
        {
            if (tintTarget == null) tintTarget = GetComponent<Graphic>();
            UpdateTint();
        }

        private void OnEnable()
        {
            DesktopDockController.Docked += OnDocked;
            if (DesktopDockController.CurrentState.HasValue) OnDocked(DesktopDockController.CurrentState.Value);
        }

        private void OnDisable()
        {
            DesktopDockController.Docked -= OnDocked;
            EndDrag();
        }

        private void OnDocked(DockState state)
        {
            if (!autoPosition) return;
            var rt = (RectTransform)transform;
            bool full = gripLength <= 0;
            switch (state.Edge)
            {
                case DockEdge.Top: Place(rt, new Vector2(0.5f, 0), new Vector2(0, inset), true, full); break;
                case DockEdge.Left: Place(rt, new Vector2(1, 0.5f), new Vector2(-inset, 0), false, full); break;
                case DockEdge.Right: Place(rt, new Vector2(0, 0.5f), new Vector2(inset, 0), false, full); break;
                default: Place(rt, new Vector2(0.5f, 1), new Vector2(0, -inset), true, full); break;
            }
        }

        private void Place(RectTransform rt, Vector2 anchor, Vector2 offset, bool horizontal, bool full)
        {
            Vector2 min = anchor, max = anchor;
            if (full)
            {
                if (horizontal) { min.x = 0; max.x = 1; }
                else { min.y = 0; max.y = 1; }
            }
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = anchor;
            float along = full ? 0 : gripLength;
            rt.sizeDelta = horizontal ? new Vector2(along, gripThickness) : new Vector2(gripThickness, along);
            rt.anchoredPosition = offset;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            var dock = DesktopDockController.Instance;
            var state = DesktopDockController.CurrentState;
            if (dock == null || !dock.IsDockingActive || !state.HasValue || state.Value.IsSimulated) return;
            if (!dock.Platform.TryGetCursorDesktopPosition(out var cursor)) return;

            var monitors = dock.Monitors;
            if (state.Value.MonitorIndex < 0 || state.Value.MonitorIndex >= monitors.Count) return;
            var bounds = monitors[state.Value.MonitorIndex].bounds;

            _edge = state.Value.Edge;
            _startRect = state.Value.WindowRect;
            // Keep the grip under the cursor instead of jumping by however far into the grip the player clicked.
            _grabOffset = state.Value.Thickness - DockGeometry.ThicknessFromCursor(_edge, _startRect, cursor);
            int across = _edge.Orientation() == DockOrientation.Horizontal ? bounds.height : bounds.width;
            _maxThickness = Mathf.Max(minThickness, Mathf.RoundToInt(across * maxFraction));
            _lastSent = state.Value.Thickness;
            IsDragging = true;
            UpdateTint();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            UpdateTint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            UpdateTint();
        }

        private void Update()
        {
            if (!IsDragging) return;
            var dock = DesktopDockController.Instance;
            if (dock == null || !dock.IsDockingActive)
            {
                EndDrag();
                return;
            }

            // Read the OS cursor and buttons: the window resizes under the cursor, and the cursor may leave it.
            var platform = dock.Platform;
            if (!platform.IsAnyMouseButtonDown())
            {
                EndDrag();
                return;
            }
            if (!platform.TryGetCursorDesktopPosition(out var cursor)) return;

            int thickness = Mathf.Clamp(DockGeometry.ThicknessFromCursor(_edge, _startRect, cursor) + _grabOffset,
                minThickness, _maxThickness);
            if (thickness == _lastSent) return;
            _lastSent = thickness;
            dock.Modify(c =>
            {
                c.thicknessUnit = DockSizeUnit.Pixels;
                c.thicknessPixels = thickness;
            }, save: false);
        }

        private void EndDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            var dock = DesktopDockController.Instance;
            if (dock != null && dock.HasUnsavedChanges) dock.SaveSettings();
            UpdateTint();
        }

        private void UpdateTint()
        {
            if (tintTarget != null) tintTarget.color = _hover || IsDragging ? activeColor : normalColor;
        }
    }
}
#endif
