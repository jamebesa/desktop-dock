using DesktopDock.Platform.MacOS;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class MacCoordinatesTests
    {
        // A 1512x982 pt Retina laptop screen (scale 2): 37 pt menu bar, 70 pt Dock at the bottom.
        private const double PrimaryHeight = 982;
        private const float Retina = 2f;

        [Test]
        public void PrimaryScreen_MapsToOrigin_InPixels()
        {
            Assert.AreEqual(new RectInt(0, 0, 3024, 1964),
                MacCoordinates.CocoaToDesktop(0, 0, 1512, 982, PrimaryHeight, Retina));
        }

        [Test]
        public void VisibleFrame_ExcludesMenuBarAtTop_AndDockAtBottom()
        {
            // visibleFrame starts above the Dock (y = 70, y up) and stops below the menu bar.
            Assert.AreEqual(new RectInt(0, 74, 3024, 1750),
                MacCoordinates.CocoaToDesktop(0, 70, 1512, 875, PrimaryHeight, Retina));
        }

        [Test]
        public void ScreenAbovePrimary_HasNegativeDesktopY()
        {
            Assert.AreEqual(new RectInt(-3840, -2160, 3840, 2160),
                MacCoordinates.CocoaToDesktop(-1920, 982, 1920, 1080, PrimaryHeight, Retina));
        }

        [Test]
        public void ScreenBelowPrimaryTop_FlipsCorrectly_AtScale1()
        {
            // An external screen to the right whose bottom sits 98 pt below the primary's bottom.
            Assert.AreEqual(new RectInt(1512, 0, 1920, 1080),
                MacCoordinates.CocoaToDesktop(1512, -98, 1920, 1080, PrimaryHeight, 1f));
        }

        [Test]
        public void BottomDock_RoundTrips_AndSitsOnTheDock()
        {
            var dock = new RectInt(0, 1384, 3024, 440); // 440 px at the bottom of the work area above
            MacCoordinates.DesktopToCocoa(dock, PrimaryHeight, Retina, out double x, out double y, out double w, out double h);
            Assert.AreEqual(0, x);
            Assert.AreEqual(70, y, "bottom edge should be the top of the Dock");
            Assert.AreEqual(1512, w);
            Assert.AreEqual(220, h);
            Assert.AreEqual(dock, MacCoordinates.CocoaToDesktop(x, y, w, h, PrimaryHeight, Retina));
        }

        [Test]
        public void DesktopToQuartz_OnlyScales()
        {
            MacCoordinates.DesktopToQuartz(new RectInt(3024, -200, 3840, 2160), Retina,
                out double x, out double y, out double w, out double h);
            Assert.AreEqual(new[] { 1512.0, -100.0, 1920.0, 1080.0 }, new[] { x, y, w, h });
        }

        [Test]
        public void CursorPoint_FlipsToDesktopPixels()
        {
            Assert.AreEqual(new Vector2Int(20, 0), MacCoordinates.CocoaPointToDesktop(10.25, 981.75, PrimaryHeight, Retina));
            Assert.AreEqual(new Vector2Int(3023, 1963), MacCoordinates.CocoaPointToDesktop(1511.9, 0.1, PrimaryHeight, Retina));
        }

        [Test]
        public void CursorPoint_ToUnityScreen_IsYUpAndScaledToBackbuffer()
        {
            // Window content: the bottom dock above, 1512x220 pt, rendered at 3024x440.
            bool inside = MacCoordinates.CocoaPointToScreen(756, 180, 0, 70, 1512, 220, 3024, 440, out var pos);
            Assert.IsTrue(inside);
            Assert.AreEqual(new Vector2(1512, 220), pos);

            Assert.IsFalse(MacCoordinates.CocoaPointToScreen(756, 60, 0, 70, 1512, 220, 3024, 440, out _), "below the window");
            Assert.IsFalse(MacCoordinates.CocoaPointToScreen(1512, 100, 0, 70, 1512, 220, 3024, 440, out _), "right edge is exclusive");
        }

        [Test]
        public void PickScale_SnapsToBackingOrOne_ElseKeepsPrevious()
        {
            Assert.AreEqual(2f, MacCoordinates.PickScale(1.98f, 2f, 0f));
            Assert.AreEqual(1f, MacCoordinates.PickScale(1.0f, 2f, 2f), "Retina support off");
            Assert.AreEqual(2f, MacCoordinates.PickScale(1.5f, 2f, 2f), "mid-resize noise keeps the previous scale");
            Assert.AreEqual(2f, MacCoordinates.PickScale(0f, 2f, 0f), "nothing measured yet: use the backing scale");
            Assert.AreEqual(1f, MacCoordinates.PickScale(0f, 0f, 0f), "no data at all");
        }

        [Test]
        public void ScaleTracker_TakesFirstReadingAtOnce()
        {
            var t = new MacCoordinates.ScaleTracker();
            t.Observe(2f, 2f, frame: 5);
            Assert.AreEqual(2f, t.Scale);
        }

        [Test]
        public void ScaleTracker_IgnoresOneOffReadingDuringResize()
        {
            var t = new MacCoordinates.ScaleTracker();
            t.Observe(2f, 2f, 0);
            t.Observe(1f, 2f, 1);   // backbuffer lagging a widened window: looks like scale 1
            t.Observe(1f, 2f, 3);   // still lagging, but too soon to count
            t.Observe(2f, 2f, 20);  // caught up
            t.Observe(1f, 2f, 40);  // a fresh one-off restarts the wait
            Assert.AreEqual(2f, t.Scale);
        }

        [Test]
        public void ScaleTracker_AcceptsChangeOnceStable()
        {
            var t = new MacCoordinates.ScaleTracker();
            t.Observe(2f, 2f, 0);
            t.Observe(1f, 1f, 100); // moved to a non-Retina screen
            Assert.AreEqual(2f, t.Scale, "one reading isn't enough");
            t.Observe(1f, 1f, 100 + MacCoordinates.ScaleTracker.SettleFrames);
            Assert.AreEqual(1f, t.Scale);
        }

        [Test]
        public void LoginItemStatus_NotFoundIsUnsupported_PendingApprovalCountsAsOn()
        {
            // SMAppService: 0 not registered, 1 enabled, 2 requires approval, 3 not found; -1 = before macOS 13.
            Assert.IsTrue(DockStartup.IsMacStatusSupported(0));
            Assert.IsTrue(DockStartup.IsMacStatusSupported(2));
            Assert.IsFalse(DockStartup.IsMacStatusSupported(3), "unsigned or loose build");
            Assert.IsFalse(DockStartup.IsMacStatusSupported(-1));

            Assert.IsFalse(DockStartup.IsMacStatusEnabled(0));
            Assert.IsTrue(DockStartup.IsMacStatusEnabled(1));
            Assert.IsTrue(DockStartup.IsMacStatusEnabled(2), "so the toggle can turn it back off");
            Assert.IsFalse(DockStartup.IsMacStatusEnabled(3));
        }

        [Test]
        public void SnapToExpected_AbsorbsOnePixelRounding()
        {
            var expected = new RectInt(0, 1384, 3024, 440);
            Assert.AreEqual(expected, MacCoordinates.SnapToExpected(new RectInt(1, 1383, 3023, 441), expected));
            var moved = new RectInt(0, 1300, 3024, 440);
            Assert.AreEqual(moved, MacCoordinates.SnapToExpected(moved, expected));
        }

        [Test]
        public void SnapToPoints_MovesOddPixelEdgesToWholePoints_OnRetina()
        {
            // A bottom dock 319 px tall: the inner edge snaps, the edge on the screen's bottom stays.
            Assert.AreEqual(new RectInt(0, 2020, 3600, 318), MacCoordinates.SnapToPoints(new RectInt(0, 2019, 3600, 319), Retina));
            // A top dock: the outer edge stays at the top of the work area.
            Assert.AreEqual(new RectInt(0, 74, 3024, 222), MacCoordinates.SnapToPoints(new RectInt(0, 74, 3024, 221), Retina));
            // A screen left of the primary one.
            Assert.AreEqual(new RectInt(-1920, 0, 1920, 200), MacCoordinates.SnapToPoints(new RectInt(-1920, 0, 1920, 199), Retina));
        }

        [Test]
        public void SnapToPoints_KeepsRectsAlreadyOnTheGrid_AndScaleOne()
        {
            var even = new RectInt(0, 1744, 3024, 220);
            Assert.AreEqual(even, MacCoordinates.SnapToPoints(even, Retina));
            var odd = new RectInt(3, 5, 7, 9);
            Assert.AreEqual(odd, MacCoordinates.SnapToPoints(odd, 1f));
        }

        [Test]
        public void SnapToPoints_NeverCollapsesToZero()
        {
            Assert.AreEqual(new RectInt(0, 100, 100, 2), MacCoordinates.SnapToPoints(new RectInt(0, 100, 100, 1), Retina));
        }
    }
}
