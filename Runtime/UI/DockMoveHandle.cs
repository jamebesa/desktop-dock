#if DESKTOPDOCK_UGUI
using DesktopDock.Platform;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopDock.UI
{
    /// <summary>
    /// Press on this UI element and drag to move the dock: it jumps to whichever edge of whichever monitor the cursor is
    /// nearest, and the choice is saved when the button is released. Put it on a Graphic with Raycast Target on (a
    /// button labelled "Move", a grip icon...). Only moves the dock in builds where docking is active.
    /// </summary>
    [AddComponentMenu("Desktop Dock/Dock Move Handle")]
    public sealed class DockMoveHandle : MonoBehaviour, IPointerDownHandler
    {
        [Tooltip("How much closer, in pixels, another edge must be before the dock switches to it. Stops flickering near corners.")]
        [Min(0)] public int hysteresis = 48;

        /// <summary>Whether the player is dragging the dock right now.</summary>
        public bool IsDragging { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            var dock = DesktopDockController.Instance;
            if (dock == null || !dock.IsDockingActive) return;
            IsDragging = true;
            dock.HoldAutoHide(this);
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

            // Read the OS cursor and buttons: the window jumps away from under the cursor as it re-docks.
            var platform = dock.Platform;
            if (!platform.IsAnyMouseButtonDown())
            {
                EndDrag();
                return;
            }
            if (!platform.TryGetCursorDesktopPosition(out var cursor)) return;

            var monitors = dock.Monitors;
            int index = DockGeometry.MonitorAt(monitors, cursor);
            if (index < 0) return;

            var cfg = dock.Config;
            int current = DesktopDockController.CurrentState?.MonitorIndex ?? 0;
            // Keep the current edge's hysteresis only on the same monitor; on another monitor, any edge goes.
            DockEdge edge = DockGeometry.NearestEdge(monitors[index].bounds, cursor, cfg.edge, index == current ? hysteresis : 0);

            // -1 means "the primary monitor": keep it that way while the dock stays on the primary.
            int monitorSetting = monitors[index].isPrimary && cfg.monitorIndex < 0 ? -1 : index;
            if (edge == cfg.edge && monitorSetting == cfg.monitorIndex) return;
            dock.Modify(c =>
            {
                c.edge = edge;
                c.monitorIndex = monitorSetting;
            }, save: false);
        }

        private void OnDisable() => EndDrag();

        private void EndDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            var dock = DesktopDockController.Instance;
            if (dock == null) return;
            dock.ReleaseAutoHide(this);
            if (dock.HasUnsavedChanges) dock.SaveSettings();
        }
    }
}
#endif
