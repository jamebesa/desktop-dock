using UnityEngine;

namespace DesktopDock.Layout
{
    /// <summary>
    /// Makes a camera clear to transparent black (0,0,0,0), so the desktop shows through wherever nothing is drawn:
    /// black is the window's color key in ColorKey mode, and alpha 0 is see-through in PerPixelAlpha mode. Put it on
    /// the camera that renders first (lowest depth). Works with the built-in pipeline and URP (Universal and 2D
    /// renderers, with or without post-processing). In URP it also turns off camera dithering. In ColorKey mode, avoid
    /// post effects that lift pure black above zero, such as bloom: those pixels stop being see-through.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Desktop Dock/Dock Transparent Camera")]
    public sealed class DockTransparentCamera : MonoBehaviour
    {
        [Tooltip("Color shown instead when the dock's transparent background is turned off.")]
        public Color opaqueBackground = new Color(0.16f, 0.18f, 0.22f, 1f);

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            Apply(DesktopDockController.Instance == null || DesktopDockController.Instance.Config.transparentBackground);
        }

        private void OnEnable() => DesktopDockController.Docked += OnDocked;
        private void OnDisable() => DesktopDockController.Docked -= OnDocked;

        private void OnDocked(DockState _)
        {
            if (DesktopDockController.Instance != null)
                Apply(DesktopDockController.Instance.Config.transparentBackground);
        }

        private void Apply(bool transparent)
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // Exactly black: that's the color key the window makes see-through. Alpha 0 as well, which is what
            // PerPixelAlpha mode makes see-through.
            _camera.backgroundColor = transparent ? new Color(0f, 0f, 0f, 0f) : opaqueBackground;
#if DESKTOPDOCK_URP
            // URP dithering adds noise to every pixel, speckling the see-through area with dark dots.
            if (transparent && _camera.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var urp))
                urp.dithering = false;
#endif
        }
    }
}
