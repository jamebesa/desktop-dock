using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockConfigTests
    {
        [Test]
        public void Clone_IsIndependent()
        {
            var a = new DockConfig { edge = DockEdge.Left };
            var b = a.Clone();
            b.edge = DockEdge.Top;
            Assert.AreEqual(DockEdge.Left, a.edge);
        }

        [Test]
        public void Json_RoundTrips()
        {
            var a = new DockConfig
            {
                edge = DockEdge.Right,
                thicknessPixels = 333,
                lengthPercent = 0.4f,
                alignment = DockAlignment.End,
                alwaysOnTop = false,
                fullscreenBehavior = DockFullscreenBehavior.Hide,
                hitTestSources = DockHitTestSources.UI | DockHitTestSources.Physics2D,
            };
            var b = new DockConfig();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(a), b);
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
        }

        [Test]
        public void Json_FromOlderVersion_KeepsDefaultsForNewFields()
        {
            // Saved by 0.1.0, before fullscreenBehavior existed.
            const string old = "{\"edge\":2,\"thicknessPixels\":300,\"alwaysOnTop\":false}";
            var cfg = new DockConfig();
            JsonUtility.FromJsonOverwrite(old, cfg);
            Assert.AreEqual(DockEdge.Left, cfg.edge);
            Assert.AreEqual(300, cfg.thicknessPixels);
            Assert.IsFalse(cfg.alwaysOnTop);
            Assert.AreEqual(DockFullscreenBehavior.DropBehind, cfg.fullscreenBehavior);
        }

        [Test]
        public void Transparency_DefaultsToColorKey()
        {
            var cfg = new DockConfig();
            Assert.AreEqual(DockTransparencyMode.ColorKey, cfg.transparencyMode);
            Assert.AreEqual(0.5f, cfg.visibleAlphaThreshold);
        }

        [Test]
        public void Json_FromBeforePerPixelAlpha_KeepsColorKey()
        {
            // Saved before PerPixelAlpha, by a build that still had the old alphaHitThreshold field.
            const string old = "{\"edge\":0,\"transparentBackground\":true,\"alphaHitThreshold\":0.1,\"fullscreenBehavior\":2}";
            var cfg = new DockConfig();
            JsonUtility.FromJsonOverwrite(old, cfg);
            Assert.AreEqual(DockTransparencyMode.ColorKey, cfg.transparencyMode);
            Assert.AreEqual(0.5f, cfg.visibleAlphaThreshold);
        }

        [Test]
        public void Json_RoundTripsTransparencyMode()
        {
            var a = new DockConfig { transparencyMode = DockTransparencyMode.PerPixelAlpha, visibleAlphaThreshold = 0.25f };
            var b = new DockConfig();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(a), b);
            Assert.AreEqual(DockTransparencyMode.PerPixelAlpha, b.transparencyMode);
            Assert.AreEqual(0.25f, b.visibleAlphaThreshold);
        }

        [Test]
        public void Orientation_FollowsEdge()
        {
            Assert.AreEqual(DockOrientation.Horizontal, DockEdge.Bottom.Orientation());
            Assert.AreEqual(DockOrientation.Horizontal, DockEdge.Top.Orientation());
            Assert.AreEqual(DockOrientation.Vertical, DockEdge.Left.Orientation());
            Assert.AreEqual(DockOrientation.Vertical, DockEdge.Right.Orientation());
        }
    }

    public class DockStartupTests
    {
        private const string Exe = @"C:\Games\My Dock Game\MyDockGame.exe";

        [Test]
        public void BuildCommand_QuotesPath()
        {
            Assert.AreEqual("\"" + Exe + "\"", DockStartup.BuildCommand(Exe, null));
            Assert.AreEqual("\"" + Exe + "\" -minimized", DockStartup.BuildCommand(Exe, "-minimized"));
        }

        [TestCase("\"C:\\Games\\My Dock Game\\MyDockGame.exe\"", true)]
        [TestCase("\"C:\\Games\\My Dock Game\\MyDockGame.exe\" -minimized", true)]
        [TestCase("\"c:/games/my dock game/mydockgame.exe\"", true)]
        [TestCase("C:\\Games\\My Dock Game\\MyDockGame.exe -x", true)]
        [TestCase("\"C:\\Old Place\\MyDockGame.exe\"", false)]
        [TestCase("", false)]
        public void CommandTargets_MatchesOnlyThisExe(string command, bool expected)
        {
            Assert.AreEqual(expected, DockStartup.CommandTargets(command, Exe));
        }

        [Test]
        public void TaskManagerFlag_OddFirstByteMeansDisabled()
        {
            Assert.IsFalse(DockStartup.IsDisabledInTaskManager(null));
            Assert.IsFalse(DockStartup.IsDisabledInTaskManager(new byte[] { 2, 0, 0 }));
            Assert.IsTrue(DockStartup.IsDisabledInTaskManager(new byte[] { 3, 0, 0 }));
        }

        [Test]
        public void NotSupportedInEditor()
        {
            Assert.IsFalse(DockStartup.IsSupported);
            Assert.IsFalse(DockStartup.IsEnabled);
        }
    }

    public class DockTrayIconTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Menu_WithoutController_HasCustomItemsAndQuit()
        {
            _go = new GameObject("tray");
            var tray = _go.AddComponent<DockTrayIcon>();
            tray.AddMenuItem("Settings...", () => { });
            var labels = tray.BuildMenu().Select(i => i.IsSeparator ? "-" : i.Label).ToArray();
            CollectionAssert.AreEqual(new[] { "Settings...", "-", "Quit" }, labels);
        }

        [Test]
        public void Menu_CustomItemCheckedState_IsAskedEachTime()
        {
            _go = new GameObject("tray");
            var tray = _go.AddComponent<DockTrayIcon>();
            bool on = false;
            tray.AddMenuItem("Sound", () => on = !on, () => on);
            Assert.IsFalse(tray.BuildMenu().First(i => i.Label == "Sound").Checked);
            on = true;
            Assert.IsTrue(tray.BuildMenu().First(i => i.Label == "Sound").Checked);
            Assert.IsTrue(tray.RemoveMenuItem("Sound"));
            Assert.IsFalse(tray.BuildMenu().Any(i => i.Label == "Sound"));
        }
    }
}
