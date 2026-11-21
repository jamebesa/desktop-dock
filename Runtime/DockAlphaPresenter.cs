using System;
using DesktopDock.Platform;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace DesktopDock
{
    /// <summary>
    /// <see cref="DockTransparencyMode.PerPixelAlpha"/>: copies each finished frame (including screen-space overlay UI)
    /// into a render texture, reads it back asynchronously and hands the pixels to the platform, which makes them the
    /// window's content. The same pixels answer the VisiblePixel hit test, so clicks follow exactly what is on screen.
    /// </summary>
    internal sealed class DockAlphaPresenter : IDisposable
    {
        // More than this many readbacks in flight means the GPU is behind; skip frames rather than queue them up.
        private const int MaxInFlight = 3;
        // Frames shown, none with a transparent pixel, before we warn that the alpha channel seems to be missing.
        private const int OpaqueFramesBeforeWarning = 60;

        private readonly IDockPlatform _platform;
        private readonly Action<AsyncGPUReadbackRequest> _onReadback;
        private RenderTexture _capture;
        private RenderTexture _prepared;
        private int _inFlight;
        private int _opaqueFrames;
        private bool _warnedOpaque;
        private bool _sawTransparency;
        private bool _warnedUnsupported;
        private bool _disposed;

        /// <summary>Where to sample alpha for the hit test, in Unity screen pixels (origin bottom-left).</summary>
        public Vector2Int SamplePoint;

        /// <summary>Alpha (0–1) at <see cref="SamplePoint"/> in the most recent frame shown, or -1 if unknown.</summary>
        public float SampledAlpha { get; private set; } = -1f;

        /// <summary>Frames shown so far.</summary>
        public int FramesPresented { get; private set; }

        /// <summary>Milliseconds the last readback callback spent copying and presenting on the main thread.</summary>
        public float LastPresentMilliseconds { get; private set; }

        public DockAlphaPresenter(IDockPlatform platform)
        {
            _platform = platform;
            _onReadback = OnReadback;
        }

        /// <summary>Call once per frame from a <c>WaitForEndOfFrame</c> coroutine, after everything has rendered.</summary>
        public void Capture()
        {
            if (_disposed) return;
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!_warnedUnsupported)
                {
                    _warnedUnsupported = true;
                    Debug.LogWarning("[DesktopDock] PerPixelAlpha needs AsyncGPUReadback, which this graphics API doesn't support. Use ColorKey.");
                }
                return;
            }
            if (_inFlight >= MaxInFlight) return;

            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return;
            if (_capture == null || _capture.width != w || _capture.height != h)
            {
                ReleaseCapture();
                _capture = NewTarget(w, h, "DesktopDock Capture");
            }

            ScreenCapture.CaptureScreenshotIntoRenderTexture(_capture);
            RenderTexture source = _capture;
            if (PrepareMaterial != null)
            {
                if (_prepared == null || _prepared.width != w || _prepared.height != h)
                {
                    if (_prepared != null) DestroyTarget(_prepared);
                    _prepared = NewTarget(w, h, "DesktopDock Prepared");
                }
                Graphics.Blit(_capture, _prepared, PrepareMaterial);
                source = _prepared;
            }
            // The request copies the texture on the GPU timeline right away, so the texture can be reused next frame.
            AsyncGPUReadback.Request(source, 0, TextureFormat.BGRA32, _onReadback);
            _inFlight++;
        }

        // No sRGB conversion on either texture: the bytes stay exactly as the backbuffer holds them.
        private static RenderTexture NewTarget(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = name };
            rt.Create();
            return rt;
        }

        private static void DestroyTarget(RenderTexture rt)
        {
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        private static Material s_prepareMaterial;
        private static bool s_prepareLoaded;

        /// <summary>
        /// Converts the capture to display-space premultiplied alpha (needed in Linear color space) and keeps color no
        /// brighter than alpha. Null if the shader is missing, in which case frames are shown as captured.
        /// </summary>
        private static Material PrepareMaterial
        {
            get
            {
                if (s_prepareLoaded) return s_prepareMaterial;
                s_prepareLoaded = true;
                s_prepareMaterial = CreatePrepareMaterial(QualitySettings.activeColorSpace == ColorSpace.Linear);
                if (s_prepareMaterial == null)
                    Debug.LogWarning("[DesktopDock] The AlphaPrepare shader is missing or unsupported; PerPixelAlpha shows frames unconverted " +
                                     "(wrong in Linear color space).");
                return s_prepareMaterial;
            }
        }

        internal static Material CreatePrepareMaterial(bool linearColorSpace)
        {
            var shader = Resources.Load<Shader>("DesktopDock/AlphaPrepare");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (linearColorSpace) material.EnableKeyword("DESKTOPDOCK_LINEAR");
            return material;
        }

        /// <summary>
        /// The VisiblePixel hit test in PerPixelAlpha mode. An unknown alpha (no frame shown yet) counts as content so
        /// a click isn't sent to the window behind by mistake.
        /// </summary>
        internal static bool IsContent(float sampledAlpha, float threshold) => sampledAlpha < 0f || sampledAlpha > threshold;

        private unsafe void OnReadback(AsyncGPUReadbackRequest request)
        {
            _inFlight = Math.Max(0, _inFlight - 1);
            if (_disposed || request.hasError) return;

            int w = request.width, h = request.height;
            if (w != Screen.width || h != Screen.height) return; // from before a resize

            float start = Time.realtimeSinceStartup;
            NativeArray<byte> data = request.GetData<byte>();
            if (data.Length < w * h * 4) return;
            var ptr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);

            // CaptureScreenshotIntoRenderTexture leaves the image upside down where texture rows start at the top.
            bool topRowFirst = SystemInfo.graphicsUVStartsAtTop;
            if (!_platform.PresentAlphaFrame(ptr, w, h, topRowFirst)) return;
            FramesPresented++;

            byte* px = (byte*)ptr;
            int sx = Mathf.Clamp(SamplePoint.x, 0, w - 1);
            int sy = Mathf.Clamp(SamplePoint.y, 0, h - 1);
            int row = topRowFirst ? h - 1 - sy : sy;
            SampledAlpha = px[((long)row * w + sx) * 4 + 3] / 255f;

            CheckForMissingAlpha(px, w, h);
            LastPresentMilliseconds = (Time.realtimeSinceStartup - start) * 1000f;
        }

        /// <summary>
        /// A backbuffer whose alpha is 1 everywhere shows as a solid block. That happens when the render pipeline drops
        /// alpha (URP with HDR or post-processing) or the camera doesn't clear to transparent. Warn once, and only if no
        /// frame has had a see-through pixel yet: a full-size opaque panel later on is the game's choice.
        /// </summary>
        private unsafe void CheckForMissingAlpha(byte* px, int w, int h)
        {
            if (_warnedOpaque || _sawTransparency) return;
            const int grid = 8;
            for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
            {
                int x = (int)((gx + 0.5f) * w / grid), y = (int)((gy + 0.5f) * h / grid);
                if (px[((long)y * w + x) * 4 + 3] < 255)
                {
                    _sawTransparency = true;
                    return;
                }
            }
            if (++_opaqueFrames < OpaqueFramesBeforeWarning) return;
            _warnedOpaque = true;
            Debug.LogWarning("[DesktopDock] PerPixelAlpha: every frame so far is opaque everywhere, so the dock shows as a solid block. " +
                             "If it should have see-through areas, clear the camera to (0,0,0,0) (DockTransparentCamera does) and make sure the " +
                             "render pipeline keeps alpha: in URP turn on Post-processing > Alpha Processing and use HDR Precision 64 Bits or turn HDR off.");
        }

        private void ReleaseCapture()
        {
            if (_capture != null) DestroyTarget(_capture);
            if (_prepared != null) DestroyTarget(_prepared);
            _capture = _prepared = null;
        }

        public void Dispose()
        {
            _disposed = true;
            ReleaseCapture();
            SampledAlpha = -1f;
        }
    }
}
