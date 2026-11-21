using System.Collections.Generic;
using DesktopDock.Platform;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockAutoHideTests
    {
        private static readonly RectInt Bottom = new RectInt(0, 820, 1920, 220);

        [Test]
        public void Progress0_IsTheDockedRect()
        {
            Assert.AreEqual(Bottom, DockGeometry.AutoHideRect(Bottom, DockEdge.Bottom, 4, 0f));
        }

        [TestCase(DockEdge.Bottom, 0, 216)]
        [TestCase(DockEdge.Top, 0, -216)]
        [TestCase(DockEdge.Left, -216, 0)]
        [TestCase(DockEdge.Right, 216, 0)]
        public void FullyTucked_LeavesPeekPixels_TowardTheOuterEdge(DockEdge edge, int dx, int dy)
        {
            var dock = edge.Orientation() == DockOrientation.Horizontal ? Bottom : new RectInt(0, 0, 220, 1040);
            var tucked = DockGeometry.AutoHideRect(dock, edge, 4, 1f);
            Assert.AreEqual(dock.size, tucked.size, "size never changes");
            Assert.AreEqual(new Vector2Int(dock.x + dx, dock.y + dy), tucked.position);
        }

        [Test]
        public void HalfWay_MovesHalfTheTravel()
        {
            Assert.AreEqual(820 + 108, DockGeometry.AutoHideRect(Bottom, DockEdge.Bottom, 4, 0.5f).y);
        }

        [Test]
        public void PeekLargerThanDock_DoesNotMoveBackward()
        {
            var tiny = new RectInt(0, 1000, 100, 20);
            Assert.AreEqual(tiny, DockGeometry.AutoHideRect(tiny, DockEdge.Bottom, 50, 1f));
        }
    }

    public class DockMoveTests
    {
        private static readonly RectInt Monitor = new RectInt(0, 0, 1920, 1080);

        [TestCase(960, 1070, DockEdge.Bottom)]
        [TestCase(960, 10, DockEdge.Top)]
        [TestCase(10, 540, DockEdge.Left)]
        [TestCase(1910, 540, DockEdge.Right)]
        public void NearestEdge_PicksClosest(int x, int y, DockEdge expected)
        {
            // Start from an edge far away so hysteresis doesn't matter.
            var start = expected == DockEdge.Top ? DockEdge.Bottom : DockEdge.Top;
            Assert.AreEqual(expected, DockGeometry.NearestEdge(Monitor, new Vector2Int(x, y), start, 0));
        }

        [Test]
        public void NearestEdge_Hysteresis_KeepsCurrentNearCorner()
        {
            // 30 px from the left edge, 50 px from the bottom: left is closer, but not by more than 48 px.
            var cursor = new Vector2Int(30, 1030);
            Assert.AreEqual(DockEdge.Bottom, DockGeometry.NearestEdge(Monitor, cursor, DockEdge.Bottom, 48));
            Assert.AreEqual(DockEdge.Left, DockGeometry.NearestEdge(Monitor, cursor, DockEdge.Bottom, 0));
        }

        [Test]
        public void MonitorAt_FindsContainingOrNearest()
        {
            var monitors = new List<DockMonitor>
            {
                new DockMonitor { bounds = new RectInt(0, 0, 1920, 1080), isPrimary = true },
                new DockMonitor { bounds = new RectInt(1920, 0, 2560, 1440) },
            };
            Assert.AreEqual(0, DockGeometry.MonitorAt(monitors, new Vector2Int(100, 100)));
            Assert.AreEqual(1, DockGeometry.MonitorAt(monitors, new Vector2Int(2000, 1300)));
            Assert.AreEqual(0, DockGeometry.MonitorAt(monitors, new Vector2Int(500, 1300)), "below the first, nearest to it");
            Assert.AreEqual(-1, DockGeometry.MonitorAt(new List<DockMonitor>(), Vector2Int.zero));
        }
    }

    public class DockPacingTests
    {
        private static DockConfig Config() => new DockConfig
        {
            focusedFrameRate = 60,
            backgroundFrameRate = 30,
            backgroundRenderInterval = 3,
            hiddenFrameRate = 10,
        };

        [Test]
        public void Focused_FullRateEveryFrame() =>
            Assert.AreEqual((60, 1), DockPacing.Choose(Config(), true, false, false));

        [Test]
        public void Background_UsesRenderInterval() =>
            Assert.AreEqual((30, 3), DockPacing.Choose(Config(), false, false, false));

        [Test]
        public void Background_PointerOver_RendersEveryFrame() =>
            Assert.AreEqual((30, 1), DockPacing.Choose(Config(), false, true, false));

        [Test]
        public void Unseen_UsesHiddenRate_EvenWhenFocused() =>
            Assert.AreEqual((10, 1), DockPacing.Choose(Config(), true, true, true));

        [Test]
        public void HiddenRateZero_FallsBackToBackgroundRate()
        {
            var c = Config();
            c.hiddenFrameRate = 0;
            Assert.AreEqual((30, 1), DockPacing.Choose(c, false, false, true));
        }
    }

    public class DockShortcutTests
    {
        [TestCase("Ctrl+Alt+D", DockShortcut.ModControl | DockShortcut.ModAlt, 0x44u)]
        [TestCase(" shift + win + f12 ", DockShortcut.ModShift | DockShortcut.ModWin, 0x7Bu)]
        [TestCase("Control+Space", DockShortcut.ModControl, 0x20u)]
        [TestCase("Alt+7", DockShortcut.ModAlt, 0x37u)]
        [TestCase("Ctrl+NumPad5", DockShortcut.ModControl, 0x65u)]
        [TestCase("Ctrl+`", DockShortcut.ModControl, 0xC0u)]
        [TestCase("F13", 0u, 0x7Cu)]
        public void Parses(string text, uint modifiers, uint vk)
        {
            Assert.IsTrue(DockShortcut.TryParse(text, out uint m, out uint k, out string error), error);
            Assert.AreEqual(modifiers, m);
            Assert.AreEqual(vk, k);
        }

        [TestCase("")]
        [TestCase("D")]                 // no modifier
        [TestCase("Ctrl+Alt")]          // no key
        [TestCase("Ctrl+A+B")]          // two keys
        [TestCase("Ctrl+Banana")]       // unknown key
        [TestCase("Ctrl++")]            // empty part
        public void Rejects(string text)
        {
            Assert.IsFalse(DockShortcut.TryParse(text, out _, out _, out string error));
            Assert.IsNotEmpty(error);
        }
    }

    public class DockConfigCompatibilityTests
    {
        [Test]
        public void NewFields_HaveSafeDefaults_WhenLoadingOlderSaves()
        {
            const string from020 = "{\"edge\":0,\"alwaysOnTop\":true,\"focusedFrameRate\":60,\"backgroundFrameRate\":30}";
            var cfg = new DockConfig();
            JsonUtility.FromJsonOverwrite(from020, cfg);
            Assert.IsFalse(cfg.autoHide, "auto-hide stays off unless chosen");
            Assert.AreEqual(1, cfg.backgroundRenderInterval, "renders every frame by default");
            Assert.AreEqual(4, cfg.autoHidePeekPixels);
        }
    }
}
