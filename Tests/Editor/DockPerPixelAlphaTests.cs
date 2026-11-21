using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockPerPixelAlphaTests
    {
        [Test]
        public void HitTest_UsesStrictThreshold()
        {
            Assert.IsTrue(DockAlphaPresenter.IsContent(0.6f, 0.5f));
            Assert.IsFalse(DockAlphaPresenter.IsContent(0.5f, 0.5f));
            Assert.IsFalse(DockAlphaPresenter.IsContent(0f, 0f));
            Assert.IsTrue(DockAlphaPresenter.IsContent(1f, 0.99f));
        }

        [Test]
        public void HitTest_UnknownAlphaCountsAsContent()
        {
            Assert.IsTrue(DockAlphaPresenter.IsContent(-1f, 0.5f));
        }

        [Test]
        public void PlayerSettingsCheck_PerPixelAlphaSkipsGraphicsApiAndSwapchain()
        {
            var perPixel = DesktopDock.Editor.DockPlayerSettings.FindIssues(colorKey: false);
            var colorKey = DesktopDock.Editor.DockPlayerSettings.FindIssues(colorKey: true);
            Assert.IsTrue(perPixel.All(colorKey.Contains));
            Assert.IsFalse(perPixel.Any(i => i.Contains("Direct3D11") || i.Contains("flip model")));
        }

        // The prep pass, run on the GPU: pixels in, pixels out (RGBA bytes).
        private static Color32[] Prepare(bool linear, params Color32[] input)
        {
            var material = DockAlphaPresenter.CreatePrepareMaterial(linear);
            if (material == null) Assert.Ignore("AlphaPrepare shader not supported here (no GPU?).");

            var src = new Texture2D(input.Length, 1, TextureFormat.RGBA32, false, linear: true) { filterMode = FilterMode.Point };
            src.SetPixels32(input);
            src.Apply();
            var rt = new RenderTexture(input.Length, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var dst = new Texture2D(input.Length, 1, TextureFormat.RGBA32, false, linear: true);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt, material);
                RenderTexture.active = rt;
                dst.ReadPixels(new Rect(0, 0, input.Length, 1), 0, 0, false);
                dst.Apply();
                return dst.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
                Object.DestroyImmediate(material);
            }
        }

        private static void AssertNear(Color32 expected, Color32 actual, int tolerance = 2)
        {
            bool ok = Mathf.Abs(expected.r - actual.r) <= tolerance && Mathf.Abs(expected.g - actual.g) <= tolerance
                      && Mathf.Abs(expected.b - actual.b) <= tolerance && Mathf.Abs(expected.a - actual.a) <= tolerance;
            Assert.IsTrue(ok, $"expected {expected}, got {actual}");
        }

        [Test]
        public void Prepare_Gamma_KeepsValidPremultipliedPixels()
        {
            var output = Prepare(false,
                new Color32(0, 0, 0, 0),
                new Color32(128, 0, 0, 128),
                new Color32(10, 200, 30, 255),
                new Color32(0, 0, 0, 150));
            AssertNear(new Color32(0, 0, 0, 0), output[0]);
            AssertNear(new Color32(128, 0, 0, 128), output[1]);
            AssertNear(new Color32(10, 200, 30, 255), output[2]);
            AssertNear(new Color32(0, 0, 0, 150), output[3]);
        }

        [Test]
        public void Prepare_RaisesAlphaToTheBrightestChannel()
        {
            // Color brighter than alpha would add light on the desktop instead of blending.
            var output = Prepare(false, new Color32(200, 40, 0, 100));
            AssertNear(new Color32(200, 40, 0, 200), output[0]);
        }

        [Test]
        public void Prepare_Linear_ConvertsToDisplaySpacePremultiplied()
        {
            // Linear color space: half-transparent red over a clear background is stored as enc(0.5) = 188.
            // The desktop compositor needs 0.5 * enc(1) = 128.
            var output = Prepare(true,
                new Color32(188, 0, 0, 128),
                new Color32(10, 200, 30, 255),
                new Color32(0, 0, 0, 0));
            AssertNear(new Color32(128, 0, 0, 128), output[0]);
            AssertNear(new Color32(10, 200, 30, 255), output[1]);
            AssertNear(new Color32(0, 0, 0, 0), output[2]);
        }
    }
}
