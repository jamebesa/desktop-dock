using DesktopDock.Platform;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockGeometryTests
    {
        // A 1920x1080 monitor with a 40 px taskbar at the bottom.
        private static readonly RectInt Work = new RectInt(0, 0, 1920, 1040);

        private static DockConfig Config(DockEdge edge, int thickness = 220, float length = 1f,
            DockAlignment alignment = DockAlignment.Center)
        {
            return new DockConfig
            {
                edge = edge,
                thicknessUnit = DockSizeUnit.Pixels,
                thicknessPixels = thickness,
                lengthPercent = length,
                alignment = alignment,
            };
        }

        [Test]
        public void Bottom_SpansWidth_AboveTaskbar()
        {
            Assert.AreEqual(new RectInt(0, 820, 1920, 220), DockGeometry.Compute(Config(DockEdge.Bottom), Work));
        }

        [Test]
        public void Top_SpansWidth_AtTop()
        {
            Assert.AreEqual(new RectInt(0, 0, 1920, 220), DockGeometry.Compute(Config(DockEdge.Top), Work));
        }

        [Test]
        public void Left_SpansHeight()
        {
            Assert.AreEqual(new RectInt(0, 0, 220, 1040), DockGeometry.Compute(Config(DockEdge.Left), Work));
        }

        [Test]
        public void Right_SpansHeight()
        {
            Assert.AreEqual(new RectInt(1700, 0, 220, 1040), DockGeometry.Compute(Config(DockEdge.Right), Work));
        }

        [Test]
        public void SecondMonitor_OffsetsRect()
        {
            var area = new RectInt(-1280, 100, 1280, 1024);
            Assert.AreEqual(new RectInt(-1280, 904, 1280, 220), DockGeometry.Compute(Config(DockEdge.Bottom), area));
        }

        [TestCase(DockAlignment.Start, 0)]
        [TestCase(DockAlignment.Center, 480)]
        [TestCase(DockAlignment.End, 960)]
        public void PartialLength_Alignment(DockAlignment alignment, int expectedX)
        {
            var rect = DockGeometry.Compute(Config(DockEdge.Bottom, length: 0.5f, alignment: alignment), Work);
            Assert.AreEqual(960, rect.width);
            Assert.AreEqual(expectedX, rect.x);
        }

        [Test]
        public void PartialLength_VerticalDock_AlignsAlongHeight()
        {
            var rect = DockGeometry.Compute(Config(DockEdge.Left, length: 0.5f, alignment: DockAlignment.End), Work);
            Assert.AreEqual(new RectInt(0, 520, 220, 520), rect);
        }

        [Test]
        public void Thickness_Percent_UsesHeightForHorizontal_WidthForVertical()
        {
            var c = new DockConfig { edge = DockEdge.Bottom, thicknessUnit = DockSizeUnit.PercentOfScreen, thicknessPercent = 0.25f };
            Assert.AreEqual(260, DockGeometry.Thickness(c, Work));
            c.edge = DockEdge.Right;
            Assert.AreEqual(480, DockGeometry.Thickness(c, Work));
        }

        [Test]
        public void Thickness_ClampedToAtLeast16_AndAtMostTheArea()
        {
            Assert.AreEqual(16, DockGeometry.Thickness(Config(DockEdge.Bottom, thickness: 2), Work));
            Assert.AreEqual(1040, DockGeometry.Thickness(Config(DockEdge.Bottom, thickness: 5000), Work));
        }

        [TestCase(DockEdge.Bottom, 960, 700, 340)]
        [TestCase(DockEdge.Top, 960, 300, 300)]
        [TestCase(DockEdge.Left, 250, 500, 250)]
        [TestCase(DockEdge.Right, 1600, 500, 320)]
        public void ThicknessFromCursor_KeepsOuterEdge(DockEdge edge, int x, int y, int expected)
        {
            var dockRect = DockGeometry.Compute(Config(edge), Work);
            Assert.AreEqual(expected, DockGeometry.ThicknessFromCursor(edge, dockRect, new Vector2Int(x, y)));
        }

        [Test]
        public void Fullscreen_WindowCoveringMonitor()
        {
            var monitor = new RectInt(0, 0, 1920, 1080);
            Assert.IsTrue(DockGeometry.IsFullscreenWindow(new RectInt(0, 0, 1920, 1080), monitor, false));
            Assert.IsTrue(DockGeometry.IsFullscreenWindow(new RectInt(-8, -8, 1936, 1096), monitor, false));
        }

        [Test]
        public void Fullscreen_NotForMaximizedOrSmallerWindows()
        {
            var monitor = new RectInt(0, 0, 1920, 1080);
            // Maximized with a visible taskbar: stops at the work area.
            Assert.IsFalse(DockGeometry.IsFullscreenWindow(new RectInt(-8, -8, 1936, 1056), monitor, false));
            // Maximized with an auto-hiding taskbar covers the monitor but has a title bar.
            Assert.IsFalse(DockGeometry.IsFullscreenWindow(new RectInt(-8, -8, 1936, 1096), monitor, true));
            // Fullscreen on another monitor.
            Assert.IsFalse(DockGeometry.IsFullscreenWindow(new RectInt(1920, 0, 1920, 1080), monitor, false));
        }
    }
}
