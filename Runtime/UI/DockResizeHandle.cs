#if DESKTOPDOCK_UGUI
using DesktopDock.Platform;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DesktopDock.UI
{
    /// <summary>
    /// A grip on the dock's inner edge (the edge facing the desktop) that the player drags to make the dock's visible
    /// area thicker or thinner, or scrolls the mouse wheel over. <see cref="DesktopDockController.Resized"/> is raised
    /// as the size changes, and the new size is saved when the drag ends. The size stays within
    /// <see cref="DockConfig.minThicknessPixels"/> and <see cref="DockConfig.maxThicknessPercent"/>. Put it on a UI
    /// Graphic with Raycast Target on, under a Screen Space Overlay canvas, or call <see cref="Create"/>. Only resizes in
    /// Windows and macOS builds.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Desktop Dock/Dock Resize Handle")]
    public sealed class DockResizeHandle : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        [Tooltip("Pixels the dock grows or shrinks per notch of the mouse wheel over the grip. 0 = the wheel does nothing.")]
        [Min(0)] public int scrollStep = 20;

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

            _edge = state.Value.Edge;
            _startRect = state.Value.WindowRect;
            // Keep the grip under the cursor instead of jumping by however far into the grip the player clicked.
            _grabOffset = state.Value.Thickness - DockGeometry.ThicknessFromCursor(_edge, _startRect, cursor);
            _lastSent = state.Value.Thickness;
            IsDragging = true;
            dock.HoldAutoHide(this);
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

        /// <summary>The mouse wheel over the grip: up makes the dock thicker, down thinner.</summary>
        public void OnScroll(PointerEventData eventData)
        {
            var dock = DesktopDockController.Instance;
            if (scrollStep <= 0 || dock == null || !dock.IsDockingActive || IsDragging) return;
            int notches = DockGeometry.WheelNotches(eventData.scrollDelta.y);
            if (notches == 0) return;
            dock.ResizeBy(notches * scrollStep, save: false);
            _saveAt = Time.unscaledTime + 0.5f; // save once the wheel has been still for a moment
            // The grip moves with the dock's edge, out from under a cursor that stays put. Catch the wheel anywhere on
            // the dock until it has been still for a moment, so the next notches still resize instead of falling
            // through the click-through areas to the window behind.
            SetScrollCatcher(true);
        }

        private float _saveAt = -1f;
        private RectTransform _scrollCatcher;

        private void SetScrollCatcher(bool on)
        {
            if (on && _scrollCatcher == null)
            {
                // An invisible child, so its events bubble up to this grip.
                var img = DockUI.Image("Scroll Catcher", transform, new Color(0f, 0f, 0f, 0f));
                _scrollCatcher = img.rectTransform;
            }
            if (_scrollCatcher == null) return;
            if (on)
            {
                // Cover the whole canvas (Screen Space Overlay, so world units are screen pixels).
                var canvas = GetComponentInParent<Canvas>();
                var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : (RectTransform)transform;
                _scrollCatcher.anchorMin = _scrollCatcher.anchorMax = _scrollCatcher.pivot = new Vector2(0.5f, 0.5f);
                _scrollCatcher.sizeDelta = root.rect.size * root.lossyScale.x / Mathf.Max(0.0001f, transform.lossyScale.x);
                _scrollCatcher.position = root.position;
            }
            _scrollCatcher.gameObject.SetActive(on);
        }

        private void Update()
        {
            if (_saveAt >= 0f && Time.unscaledTime >= _saveAt)
            {
                _saveAt = -1f;
                SetScrollCatcher(false);
                var d = DesktopDockController.Instance;
                if (d != null && d.HasUnsavedChanges) d.SaveSettings();
            }

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

            int thickness = DockGeometry.ThicknessFromCursor(_edge, _startRect, cursor) + _grabOffset;
            if (thickness == _lastSent) return;
            _lastSent = thickness;
            dock.SetThicknessPixels(thickness, save: false); // clamps to the dock's thickness limits
        }

        private void EndDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            var dock = DesktopDockController.Instance;
            if (dock != null)
            {
                dock.ReleaseAutoHide(this);
                if (dock.HasUnsavedChanges) dock.SaveSettings();
            }
            UpdateTint();
        }

        private void UpdateTint()
        {
            if (tintTarget != null) tintTarget.color = _hover || IsDragging ? activeColor : normalColor;
        }
    }
}
#endif
