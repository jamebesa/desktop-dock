using UnityEngine;

namespace DesktopDock.Platform.MacOS
{
    /// <summary>
    /// Converts between Cocoa global coordinates and the package's desktop coordinates. Pure math, compiled on
    /// every platform so it can be unit-tested anywhere.
    /// <list type="bullet">
    /// <item>Cocoa: points, origin at the bottom-left of the primary screen (the one with the menu bar), y up.</item>
    /// <item>Quartz (CGWindow): points, origin at the top-left of the primary screen, y down.</item>
    /// <item>Desktop (<see cref="DockMonitor"/>, <see cref="DockState.WindowRect"/>): Unity pixels, origin top-left of
    /// the primary screen, y down. One point is <c>scale</c> pixels, where scale is what Unity renders per point:
    /// the Retina backing scale (usually 2) with Retina support on, or 1 with it off.</item>
    /// </list>
    /// A single scale is used for the whole desktop. With monitors of different scales this keeps the layout
    /// consistent, and the dock re-docks when the window moves to a screen with another scale.
    /// </summary>
    internal static class MacCoordinates
    {
        /// <summary>Cocoa rect to desktop rect. Rounds the edges, not the size, so adjacent rects stay adjacent.</summary>
        public static RectInt CocoaToDesktop(double x, double y, double width, double height, double primaryHeight, float scale)
        {
            double top = primaryHeight - (y + height);
            int x0 = Round(x * scale), y0 = Round(top * scale);
            int x1 = Round((x + width) * scale), y1 = Round((top + height) * scale);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Desktop rect to Cocoa rect (the inverse of <see cref="CocoaToDesktop"/>).</summary>
        public static void DesktopToCocoa(RectInt rect, double primaryHeight, float scale,
            out double x, out double y, out double width, out double height)
        {
            x = rect.x / (double)scale;
            width = rect.width / (double)scale;
            height = rect.height / (double)scale;
            y = primaryHeight - rect.y / (double)scale - height;
        }

        /// <summary>Desktop rect to Quartz rect (points, y down).</summary>
        public static void DesktopToQuartz(RectInt rect, float scale,
            out double x, out double y, out double width, out double height)
        {
            x = rect.x / (double)scale;
            y = rect.y / (double)scale;
            width = rect.width / (double)scale;
            height = rect.height / (double)scale;
        }

        /// <summary>Cocoa point (for example <c>NSEvent.mouseLocation</c>) to a desktop pixel.</summary>
        public static Vector2Int CocoaPointToDesktop(double x, double y, double primaryHeight, float scale) =>
            new Vector2Int(Mathf.FloorToInt((float)(x * scale)), Mathf.FloorToInt((float)((primaryHeight - y) * scale)));

        /// <summary>
        /// Cocoa point to Unity screen coordinates (origin bottom-left of the window's content, y up, in backbuffer
        /// pixels). Both are y-up, so this is an offset and a scale to the backbuffer size.
        /// </summary>
        public static bool CocoaPointToScreen(double pointX, double pointY,
            double contentX, double contentY, double contentWidth, double contentHeight,
            int screenWidth, int screenHeight, out Vector2 screenPosition)
        {
            screenPosition = default;
            if (contentWidth <= 0 || contentHeight <= 0) return false;
            double lx = pointX - contentX, ly = pointY - contentY;
            screenPosition = new Vector2((float)(lx * screenWidth / contentWidth), (float)(ly * screenHeight / contentHeight));
            return lx >= 0 && ly >= 0 && lx < contentWidth && ly < contentHeight;
        }

        /// <summary>
        /// Unity pixels per point. <paramref name="measured"/> is the backbuffer width divided by the content width
        /// in points; it is noisy while a resize is in flight, so snap it to the backing scale or to 1, and keep
        /// <paramref name="previous"/> when it matches neither.
        /// </summary>
        public static float PickScale(float measured, float backing, float previous)
        {
            if (backing <= 0f) backing = 1f;
            if (measured > 0f)
            {
                if (Mathf.Abs(measured - backing) <= backing * 0.15f) return backing;
                if (Mathf.Abs(measured - 1f) <= 0.15f) return 1f;
            }
            return previous > 0f ? previous : backing;
        }

        /// <summary>
        /// Smooths <see cref="PickScale"/> over time. Right after a resize the window's width in points has changed
        /// but Unity's backbuffer may lag a few frames, so a single reading can look like scale 1 on a Retina screen.
        /// A different scale is only accepted once two readings at least <see cref="SettleFrames"/> frames apart agree
        /// on it, with no reading of the current scale in between. The first reading is accepted at once.
        /// </summary>
        public struct ScaleTracker
        {
            public const int SettleFrames = 10;

            private float _pending;
            private int _pendingFrame;

            /// <summary>Unity pixels per point; 0 before the first reading.</summary>
            public float Scale { get; private set; }

            public void Observe(float measured, float backing, int frame)
            {
                float candidate = PickScale(measured, backing, Scale);
                if (Scale <= 0f || candidate == Scale)
                {
                    Scale = candidate;
                    _pending = 0f;
                    return;
                }
                if (candidate != _pending)
                {
                    _pending = candidate;
                    _pendingFrame = frame;
                    return;
                }
                if (frame - _pendingFrame >= SettleFrames)
                {
                    Scale = candidate;
                    _pending = 0f;
                }
            }
        }

        /// <summary>
        /// The rect last set, if <paramref name="actual"/> is within a pixel of it on every side. AppKit can round a
        /// frame to the backing grid; without this the placement guard would keep "correcting" it.
        /// </summary>
        public static RectInt SnapToExpected(RectInt actual, RectInt expected) =>
            Mathf.Abs(actual.xMin - expected.xMin) <= 1 && Mathf.Abs(actual.yMin - expected.yMin) <= 1
            && Mathf.Abs(actual.xMax - expected.xMax) <= 1 && Mathf.Abs(actual.yMax - expected.yMax) <= 1
                ? expected
                : actual;

        private static int Round(double v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
