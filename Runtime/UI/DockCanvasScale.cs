#if DESKTOPDOCK_UGUI
using UnityEngine;
using UnityEngine.UI;

namespace DesktopDock.UI
{
    /// <summary>
    /// Keeps a canvas's UI the same size in points on high-density screens. On a Retina Mac the game renders two pixels
    /// per point, so a Constant Pixel Size canvas shows buttons and text at half size. This multiplies the canvas's
    /// scale factor by <see cref="DesktopDockController.PixelsPerPoint"/>, and follows it when the dock moves to a
    /// screen with another scale. Put it on the canvas, next to its CanvasScaler. On Windows and in the Editor the
    /// factor stays at <see cref="baseScaleFactor"/>.
    /// <para>It sets the scaler to Constant Pixel Size. A canvas at Scale With Screen Size already follows the window's
    /// size in pixels, so it doesn't need this component.</para>
    /// </summary>
    [AddComponentMenu("Desktop Dock/Dock Canvas Scale")]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class DockCanvasScale : MonoBehaviour
    {
        [Tooltip("The canvas's scale factor at one pixel per point (Windows, the Editor, a Mac without Retina).")]
        [Min(0.01f)] public float baseScaleFactor = 1f;

        private CanvasScaler _scaler;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            var dock = DesktopDockController.Instance;
            float factor = baseScaleFactor * (dock != null ? dock.PixelsPerPoint : 1f);
            if (!Mathf.Approximately(_scaler.scaleFactor, factor)) _scaler.scaleFactor = factor;
        }
    }
}
#endif
