using System;
using UnityEngine;

namespace DesktopDock
{
    /// <summary>
    /// Everything that decides how the window is docked. Serialized on <see cref="DesktopDockController"/> as the
    /// project defaults; a copy is what changes at runtime (and what gets saved to PlayerPrefs if enabled).
    /// </summary>
    [Serializable]
    public sealed class DockConfig
    {
        [Header("Placement")]
        [Tooltip("Edge of the monitor to dock against.")]
        public DockEdge edge = DockEdge.Bottom;

        [Tooltip("Monitor to dock on. -1 = primary monitor; otherwise an index into DesktopDockController.Monitors.")]
        public int monitorIndex = -1;

        [Tooltip("Stay inside the work area (above/beside the taskbar) instead of overlapping it.")]
        public bool respectTaskbar = true;

        [Tooltip("Register as a Windows app bar so maximized windows stop at the dock instead of covering it (like the taskbar does).")]
        public bool reserveScreenSpace = false;

        [Header("Size")]
        public DockSizeUnit thicknessUnit = DockSizeUnit.Pixels;

        [Tooltip("Dock thickness in physical pixels (height for top/bottom, width for left/right).")]
        [Min(16)] public int thicknessPixels = 220;

        [Tooltip("Dock thickness as a fraction of the monitor's height (top/bottom) or width (left/right).")]
        [Range(0.02f, 0.9f)] public float thicknessPercent = 0.2f;

        [Tooltip("Smallest thickness, in pixels, that resizing (the resize grip, the settings panel, the scroll wheel, " +
                 "SetThicknessPixels and ResizeBy) can set.")]
        [Min(16)] public int minThicknessPixels = 100;

        [Tooltip("Largest thickness resizing can set, as a fraction of the monitor's height (top/bottom) or width (left/right).")]
        [Range(0.05f, 0.9f)] public float maxThicknessPercent = 0.5f;

        [Tooltip("How much of the edge the dock spans. 1 = the full width/height of the monitor.")]
        [Range(0.1f, 1f)] public float lengthPercent = 1f;

        [Tooltip("Where a dock shorter than its edge sits along that edge.")]
        public DockAlignment alignment = DockAlignment.Center;

        [Header("Window")]
        [Tooltip("Keep the dock above other windows.")]
        public bool alwaysOnTop = true;

        [Tooltip("What to do while another app runs fullscreen on the dock's monitor, such as a game or a fullscreen video.")]
        public DockFullscreenBehavior fullscreenBehavior = DockFullscreenBehavior.DropBehind;

        [Tooltip("Show the desktop through the empty parts of the dock, such as a camera background of (0,0,0,0). How depends on Transparency Mode.")]
        public bool transparentBackground = true;

        [Tooltip("ColorKey: pure-black pixels are see-through and all others opaque; free, but needs Direct3D11 without the flip-model swapchain. " +
                 "PerPixelAlpha: the backbuffer's alpha blends with the desktop (soft shadows, glows, smooth edges); works with any graphics API but reads every frame back from the GPU.")]
        public DockTransparencyMode transparencyMode = DockTransparencyMode.ColorKey;

        [Tooltip("Let mouse clicks pass through transparent areas to the windows behind.")]
        public bool clickThroughTransparentAreas = true;

        [Tooltip("What counts as content under the cursor when deciding whether to let a click through.")]
        public DockHitTestSources hitTestSources = DockHitTestSources.Everything;

        [Tooltip("PerPixelAlpha only: a pixel counts as content for the VisiblePixel hit test when its alpha is above this. " +
                 "At 0.5, soft shadows, glows and the outer half of anti-aliased edges let clicks through; lower it to catch them.")]
        [Range(0f, 0.99f)] public float visibleAlphaThreshold = 0.5f;

        [Tooltip("Show the game's button on the taskbar and in Alt+Tab.")]
        public bool showInTaskbar = true;

        [Header("Auto-hide")]
        [Tooltip("Slide the dock almost out of sight when the cursor leaves it, like an auto-hiding taskbar. Touching the " +
                 "sliver left at the screen edge brings it back. Reserve Screen Space is ignored while this is on.")]
        public bool autoHide = false;

        [Tooltip("Seconds after the cursor leaves the dock before it slides away.")]
        [Min(0f)] public float autoHideDelay = 1f;

        [Tooltip("Pixels of the dock left on screen while it's tucked away, for the cursor to touch.")]
        [Min(1)] public int autoHidePeekPixels = 4;

        [Header("Performance")]
        [Tooltip("Target frame rate while the dock has keyboard focus. -1 = platform default.")]
        public int focusedFrameRate = 60;

        [Tooltip("Target frame rate while another app has focus (the usual state for a desktop idler). -1 = platform default.")]
        public int backgroundFrameRate = 30;

        [Tooltip("Render only every Nth frame while another app has focus and the cursor isn't over the dock. Scripts " +
                 "still run every frame, so game logic keeps its pace. 1 = render every frame. 2–4 suits most idle games.")]
        [Range(1, 10)] public int backgroundRenderInterval = 1;

        [Tooltip("Target frame rate while the dock can't be seen: hidden, tucked away by auto-hide, or behind a " +
                 "fullscreen app. 0 = same as the background frame rate.")]
        [Min(0)] public int hiddenFrameRate = 10;

        public DockConfig Clone() => (DockConfig)MemberwiseClone();

        public DockOrientation Orientation => edge.Orientation();
    }
}
