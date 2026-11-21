using System;
using UnityEngine;
using UnityEngine.Events;

namespace DesktopDock.Layout
{
    /// <summary>
    /// Swaps between a horizontal layout (top/bottom docks) and a vertical one (left/right docks), and exposes
    /// UnityEvents so designers can hook up anything else that should react to the dock edge.
    /// </summary>
    [AddComponentMenu("Desktop Dock/Dock Layout Switcher")]
    public sealed class DockLayoutSwitcher : MonoBehaviour
    {
        [Serializable] public sealed class EdgeEvent : UnityEvent<DockEdge> { }
        [Serializable] public sealed class OrientationEvent : UnityEvent<DockOrientation> { }

        [Tooltip("Enabled for top/bottom docks, disabled otherwise.")]
        public GameObject[] horizontalOnly = Array.Empty<GameObject>();

        [Tooltip("Enabled for left/right docks, disabled otherwise.")]
        public GameObject[] verticalOnly = Array.Empty<GameObject>();

        public EdgeEvent onEdgeChanged = new EdgeEvent();
        public OrientationEvent onOrientationChanged = new OrientationEvent();

        private DockEdge? _lastEdge;
        private DockOrientation? _lastOrientation;

        private void OnEnable()
        {
            DesktopDockController.Docked += Apply;
            if (DesktopDockController.CurrentState is DockState s) Apply(s);
        }

        private void OnDisable() => DesktopDockController.Docked -= Apply;

        private void Apply(DockState state)
        {
            var orientation = state.Orientation;
            bool horizontal = orientation == DockOrientation.Horizontal;
            foreach (var go in horizontalOnly) if (go != null) go.SetActive(horizontal);
            foreach (var go in verticalOnly) if (go != null) go.SetActive(!horizontal);

            if (_lastEdge != state.Edge)
            {
                _lastEdge = state.Edge;
                onEdgeChanged.Invoke(state.Edge);
            }
            if (_lastOrientation != orientation)
            {
                _lastOrientation = orientation;
                onOrientationChanged.Invoke(orientation);
            }
        }
    }
}
