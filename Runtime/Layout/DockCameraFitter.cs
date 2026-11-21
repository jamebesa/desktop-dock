using UnityEngine;

namespace DesktopDock.Layout
{
    /// <summary>How <see cref="DockCameraFitter"/> zooms when the dock changes size.</summary>
    public enum DockCameraFitMode
    {
        /// <summary>
        /// A fixed amount of world fits across the dock's thickness (height for top/bottom docks), so content grows as the
        /// dock gets thicker: a taller dock zooms in on the same map.
        /// </summary>
        Thickness = 0,
        /// <summary>A fixed amount of world fits along the dock's length, whatever its thickness.</summary>
        Length = 1,
        /// <summary>A fixed zoom (screen pixels per world unit): a thicker dock shows more of the world at the same size.</summary>
        FixedScale = 2,
    }

    /// <summary>
    /// Sizes an orthographic camera to the dock. By default a fixed amount of world space fits across the dock's
    /// thickness, so making the dock taller zooms in; <see cref="fitMode"/> can instead fit the length, or keep a fixed
    /// zoom so a taller dock shows more. Optionally snaps to whole-number pixel scales for crisp pixel art. Updates
    /// whenever the camera's pixel size changes, including while the player drags the resize grip.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [AddComponentMenu("Desktop Dock/Dock Camera Fitter")]
    public sealed class DockCameraFitter : MonoBehaviour
    {
        [Tooltip("Thickness: fixed world units across the dock, so a taller dock zooms in. Length: fixed world units along " +
                 "the dock. FixedScale: fixed zoom, so a taller dock shows more of the world.")]
        public DockCameraFitMode fitMode = DockCameraFitMode.Thickness;

        [Tooltip("Thickness mode: world units visible across the dock (height for top/bottom docks, width for left/right).")]
        [Min(0.01f)] public float worldUnitsAcross = 5f;

        [Tooltip("Length mode: world units visible along the dock (width for top/bottom docks, height for left/right).")]
        [Min(0.01f)] public float worldUnitsAlong = 20f;

        [Tooltip("FixedScale mode: screen pixels per world unit.")]
        [Min(0.01f)] public float screenPixelsPerUnit = 50f;

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
            _camera.orthographicSize = OrthographicSize(w, h, orientation);
        }

        /// <summary>The orthographic size for a camera of <paramref name="width"/>×<paramref name="height"/> pixels.</summary>
        public float OrthographicSize(int width, int height, DockOrientation orientation)
        {
            bool horizontal = orientation == DockOrientation.Horizontal;
            float pixelsPerWorldUnit = fitMode switch
            {
                DockCameraFitMode.Length => (horizontal ? width : height) / worldUnitsAlong,
                DockCameraFitMode.FixedScale => screenPixelsPerUnit,
                _ => (horizontal ? height : width) / worldUnitsAcross,
            };
            if (pixelPerfect)
            {
                int scale = Mathf.Max(1, Mathf.FloorToInt(pixelsPerWorldUnit / pixelsPerUnit));
                pixelsPerWorldUnit = scale * pixelsPerUnit;
            }
            // orthographicSize is half the visible height in world units.
            return height / (2f * pixelsPerWorldUnit);
        }
    }
}
