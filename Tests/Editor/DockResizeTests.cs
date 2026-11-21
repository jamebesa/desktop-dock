using DesktopDock.Layout;
using DesktopDock.Platform;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockResizeTests
    {
        [Test]
        public void ThicknessLimits_FromConfig()
        {
            var c = new DockConfig { minThicknessPixels = 100, maxThicknessPercent = 0.5f };
            Assert.AreEqual(new Vector2Int(100, 540), DockGeometry.ThicknessLimits(c, 1080));
        }

        [Test]
        public void ThicknessLimits_NeverInverted_OnSmallScreens()
        {
            var c = new DockConfig { minThicknessPixels = 300, maxThicknessPercent = 0.2f };
            Assert.AreEqual(new Vector2Int(300, 300), DockGeometry.ThicknessLimits(c, 1080), "max is raised to the min");
            Assert.AreEqual(new Vector2Int(200, 200), DockGeometry.ThicknessLimits(c, 200), "both fit the monitor");
        }

        [TestCase(1f, 1)]
        [TestCase(-1f, -1)]
        [TestCase(3f, 3)]
        [TestCase(120f, 1)]
        [TestCase(-240f, -2)]
        [TestCase(0.2f, 1)]
        [TestCase(0f, 0)]
        public void WheelNotches_LegacyAndInputSystemScales(float delta, int expected)
        {
            Assert.AreEqual(expected, DockGeometry.WheelNotches(delta));
        }
    }

    public class DockCameraFitterTests
    {
        private GameObject _go;
        private DockCameraFitter _fitter;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("camera", typeof(Camera));
            _fitter = _go.AddComponent<DockCameraFitter>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Thickness_TallerDockZoomsIn()
        {
            _fitter.fitMode = DockCameraFitMode.Thickness;
            _fitter.worldUnitsAcross = 4f;
            // A fixed 4 world units tall at any height: the same world, bigger on screen.
            Assert.AreEqual(2f, _fitter.OrthographicSize(1920, 220, DockOrientation.Horizontal), 1e-4f);
            Assert.AreEqual(2f, _fitter.OrthographicSize(1920, 440, DockOrientation.Horizontal), 1e-4f);
        }

        [Test]
        public void FixedScale_TallerDockShowsMore()
        {
            _fitter.fitMode = DockCameraFitMode.FixedScale;
            _fitter.screenPixelsPerUnit = 50f;
            Assert.AreEqual(2.2f, _fitter.OrthographicSize(1920, 220, DockOrientation.Horizontal), 1e-4f);
            Assert.AreEqual(4.4f, _fitter.OrthographicSize(1920, 440, DockOrientation.Horizontal), 1e-4f);
        }

        [Test]
        public void Length_FitsAlongTheDock()
        {
            _fitter.fitMode = DockCameraFitMode.Length;
            _fitter.worldUnitsAlong = 20f;
            // 1920 px across 20 units = 96 px per unit; 220 px tall = 2.29 units.
            Assert.AreEqual(220f / (2f * 96f), _fitter.OrthographicSize(1920, 220, DockOrientation.Horizontal), 1e-4f);
            // Vertical dock: the length is its height.
            Assert.AreEqual(1080f / (2f * 54f), _fitter.OrthographicSize(220, 1080, DockOrientation.Vertical), 1e-4f);
        }
    }
}
