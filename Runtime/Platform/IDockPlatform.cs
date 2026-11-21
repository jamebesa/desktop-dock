using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopDock.Platform
{
    /// <summary>
    /// The OS-specific half of the dock. The controller decides *what* should happen; a platform makes the
    /// window do it. Add a new implementation (e.g. macOS via a native plugin) and return it from
    /// <see cref="DockPlatformFactory"/>.
    /// </summary>
    internal interface IDockPlatform
    {
        /// <summary>False for the Editor and unsupported platforms; the controller then only simulates docking.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Whether <see cref="DockTransparencyMode.ColorKey"/> can work with the running graphics setup. When it can't,
        /// the controller uses <see cref="DockTransparencyMode.PerPixelAlpha"/> instead.
        /// </summary>
        bool SupportsColorKey { get; }

        /// <summary>
        /// True where the OS already blends the window with its own alpha (macOS). Transparency is then always per
        /// pixel and the controller doesn't copy frames with <see cref="PresentAlphaFrame"/>.
        /// </summary>
        bool BlendsAlphaNatively { get; }

        /// <summary>Find and take hold of the game window. Returns false if it can't be found.</summary>
        bool Initialize();

        IReadOnlyList<DockMonitor> GetMonitors();

        /// <summary>Borderless popup style, transparency and taskbar presence.</summary>
        void ApplyWindowStyle(DockConfig config);

        /// <summary>Compute the window rect for <paramref name="config"/> and, if requested, reserve that edge as an app bar.</summary>
        RectInt PlaceDock(DockConfig config, DockMonitor monitor);

        void ReleaseReservedSpace();

        /// <summary>Move and size the window and set whether it is top-most. Never shows a hidden window.</summary>
        void SetWindowRect(RectInt rect, bool alwaysOnTop);

        /// <summary>The window's current outer rect in desktop coordinates.</summary>
        bool TryGetWindowRect(out RectInt rect);

        /// <summary>Show (without taking focus) or hide the window.</summary>
        void SetVisible(bool visible);

        /// <summary>Drop to the bottom of the z-order, losing top-most status until the next <see cref="SetWindowRect"/>.</summary>
        void SendToBack();

        /// <summary>Whether another app's window has focus and covers all of <paramref name="monitorBounds"/>
        /// (a fullscreen game or video).</summary>
        bool IsFullscreenAppActive(RectInt monitorBounds);

        /// <summary>Cursor position in desktop coordinates (origin top-left of the primary monitor, y down).</summary>
        bool TryGetCursorDesktopPosition(out Vector2Int position);

        /// <summary>When true, mouse input passes through the whole window to whatever is behind it.</summary>
        void SetClickThrough(bool clickThrough);

        /// <summary>Cursor position in Unity screen coordinates (origin bottom-left), read from the OS so it stays
        /// correct while the window is click-through and receiving no mouse messages.</summary>
        bool TryGetCursorPosition(out Vector2 screenPosition, out bool insideWindow);

        bool IsAnyMouseButtonDown();

        /// <summary>
        /// <see cref="DockTransparencyMode.PerPixelAlpha"/>: show one frame of premultiplied BGRA pixels (4 bytes per
        /// pixel, rows packed) as the window's content. Returns false if the frame wasn't shown, for example because
        /// its size doesn't match the window (yet).
        /// </summary>
        bool PresentAlphaFrame(IntPtr bgraPixels, int width, int height, bool topRowFirst);

        void Shutdown();
    }

    internal static class DockPlatformFactory
    {
        public static IDockPlatform Create()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return new Windows.WindowsDockPlatform();
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return new MacOS.MacDockPlatform();
#else
            return new NullDockPlatform();
#endif
        }
    }

    /// <summary>Used in the Editor (never restyle the Editor's own window) and on platforms without an implementation.</summary>
    internal sealed class NullDockPlatform : IDockPlatform
    {
        public bool IsSupported => false;
        public bool SupportsColorKey => true;
        public bool BlendsAlphaNatively => false;
        public bool Initialize() => false;

        public IReadOnlyList<DockMonitor> GetMonitors()
        {
            var r = new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            return new[] { new DockMonitor { bounds = r, workArea = r, isPrimary = true } };
        }

        public void ApplyWindowStyle(DockConfig config) { }
        public RectInt PlaceDock(DockConfig config, DockMonitor monitor) => DockGeometry.Compute(config, monitor.workArea);
        public void ReleaseReservedSpace() { }
        public void SetWindowRect(RectInt rect, bool alwaysOnTop) { }

        public bool TryGetWindowRect(out RectInt rect)
        {
            rect = default;
            return false;
        }

        public void SetVisible(bool visible) { }
        public void SendToBack() { }
        public bool IsFullscreenAppActive(RectInt monitorBounds) => false;

        public bool TryGetCursorDesktopPosition(out Vector2Int position)
        {
            position = default;
            return false;
        }

        public void SetClickThrough(bool clickThrough) { }

        public bool TryGetCursorPosition(out Vector2 screenPosition, out bool insideWindow)
        {
            screenPosition = default;
            insideWindow = false;
            return false;
        }

        public bool IsAnyMouseButtonDown() => false;
        public bool PresentAlphaFrame(IntPtr bgraPixels, int width, int height, bool topRowFirst) => false;
        public void Shutdown() { }
    }

    internal static class DockGeometry
    {
        public static int Thickness(DockConfig c, RectInt area)
        {
            int across = c.Orientation == DockOrientation.Horizontal ? area.height : area.width;
            int t = c.thicknessUnit == DockSizeUnit.Pixels
                ? c.thicknessPixels
                : Mathf.RoundToInt(c.thicknessPercent * across);
            return Mathf.Clamp(t, 16, Mathf.Max(16, across));
        }

        /// <summary>Dock rect inside <paramref name="area"/> (desktop coordinates, y down).</summary>
        public static RectInt Compute(DockConfig c, RectInt area) => Compute(c, area, Thickness(c, area));

        public static RectInt Compute(DockConfig c, RectInt area, int thickness)
        {
            bool horizontal = c.Orientation == DockOrientation.Horizontal;
            int full = horizontal ? area.width : area.height;
            int length = Mathf.Clamp(Mathf.RoundToInt(full * c.lengthPercent), 1, full);
            int offset = c.alignment switch
            {
                DockAlignment.Start => 0,
                DockAlignment.End => full - length,
                _ => (full - length) / 2,
            };

            switch (c.edge)
            {
                case DockEdge.Top: return new RectInt(area.x + offset, area.y, length, thickness);
                case DockEdge.Left: return new RectInt(area.x, area.y + offset, thickness, length);
                case DockEdge.Right: return new RectInt(area.xMax - thickness, area.y + offset, thickness, length);
                default: return new RectInt(area.x + offset, area.yMax - thickness, length, thickness);
            }
        }

        /// <summary>
        /// Thickness that puts the dock's inner edge at <paramref name="cursor"/>, keeping the outer edge of
        /// <paramref name="dockRect"/> where it is. Desktop coordinates. Can be negative when the cursor is past the outer edge.
        /// </summary>
        public static int ThicknessFromCursor(DockEdge edge, RectInt dockRect, Vector2Int cursor) => edge switch
        {
            DockEdge.Top => cursor.y - dockRect.y,
            DockEdge.Left => cursor.x - dockRect.x,
            DockEdge.Right => dockRect.xMax - cursor.x,
            _ => dockRect.yMax - cursor.y,
        };

        /// <summary>
        /// Whether a focused window with rect <paramref name="window"/> counts as fullscreen on a monitor. A maximized
        /// window with a title bar doesn't, even when it covers the monitor because the taskbar auto-hides.
        /// </summary>
        public static bool IsFullscreenWindow(RectInt window, RectInt monitor, bool maximizedWithTitleBar) =>
            !maximizedWithTitleBar
            && window.xMin <= monitor.xMin && window.yMin <= monitor.yMin
            && window.xMax >= monitor.xMax && window.yMax >= monitor.yMax;
    }
}
