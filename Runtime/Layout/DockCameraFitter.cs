using UnityEngine;

namespace DesktopDock.Layout
{
    /// <summary>
    /// Sizes an orthographic camera so a fixed amount of world space always fits across the dock: the visible
    /// height for top/bottom docks, the visible width for left/right docks. Optionally snaps to whole-number
    /// pixel scales for crisp pixel art.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [AddComponentMenu("Desktop Dock/Dock Camera Fitter")]
    public sealed class DockCameraFitter : MonoBehaviour
    {
        [Tooltip("World units visible across the dock (height for top/bottom docks, width for left/right).")]
        [Min(0.01f)] public float worldUnitsAcross = 5f;

        [Tooltip("Snap the zoom so one world unit is a whole number of screen pixels.")]
        public bool pixelPerfect = false;

        [Tooltip("Sprite pixels per world unit (match your sprites' import setting).")]
        [Min(1)] public int pixelsPerUnit = 16;

        private Camera _camera;
        private int _lastW, _lastH;
        private DockOrientation _orientation = DockOrientation.Horizontal;
        private bool _dirty = true;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            DesktopDockController.Docked += OnDocked;
            if (DesktopDockController.CurrentState is DockState s) _orientation = s.Orientation;
            _dirty = true;
        }

        private void OnDisable() => DesktopDockController.Docked -= OnDocked;

        private void OnValidate() => _dirty = true;

        private void OnDocked(DockState state)
        {
            _orientation = state.Orientation;
            _dirty = true;
        }

        private void LateUpdate()
        {
            int w = _camera.pixelWidth, h = _camera.pixelHeight;
            if (!_dirty && w == _lastW && h == _lastH) return;
            _lastW = w;
            _lastH = h;
            _dirty = false;
            if (w <= 0 || h <= 0) return;

            // In the Editor the controller isn't docking, so infer orientation from the Game view's shape.
            var orientation = DesktopDockController.CurrentState.HasValue ? _orientation
                : (w >= h ? DockOrientation.Horizontal : DockOrientation.Vertical);

            bool horizontal = orientation == DockOrientation.Horizontal;
            int acrossPixels = horizontal ? h : w;

            float pixelsPerWorldUnit = acrossPixels / worldUnitsAcross;
            if (pixelPerfect)
            {
                int scale = Mathf.Max(1, Mathf.FloorToInt(pixelsPerWorldUnit / pixelsPerUnit));
                pixelsPerWorldUnit = scale * pixelsPerUnit;
            }

            // orthographicSize is half the visible height in world units.
            _camera.orthographicSize = h / (2f * pixelsPerWorldUnit);
        }
    }
}
