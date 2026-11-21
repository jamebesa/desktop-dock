using System;
using UnityEngine;

namespace DesktopDock
{
    /// <summary>Which edge of the monitor the game window is docked to.</summary>
    public enum DockEdge
    {
        Bottom = 0,
        Top = 1,
        Left = 2,
        Right = 3,
    }

    /// <summary>Horizontal docks (top/bottom) are wide and short; vertical docks (left/right) are tall and narrow.</summary>
    public enum DockOrientation
    {
        Horizontal = 0,
        Vertical = 1,
    }

    /// <summary>Where a dock shorter than its edge sits along that edge.</summary>
    public enum DockAlignment
    {
        /// <summary>Left for top/bottom docks, top for left/right docks.</summary>
        Start = 0,
        Center = 1,
        /// <summary>Right for top/bottom docks, bottom for left/right docks.</summary>
        End = 2,
    }

    /// <summary>What the dock does while another app runs fullscreen on its monitor (a game or a fullscreen video).</summary>
    public enum DockFullscreenBehavior
    {
        /// <summary>Do nothing: a pinned dock stays on top of the fullscreen app.</summary>
        Ignore = 0,
        /// <summary>Drop behind the fullscreen app, and come back on top when it closes or loses focus.</summary>
        DropBehind = 1,
        /// <summary>Hide the window until the fullscreen app closes or loses focus.</summary>
        Hide = 2,
    }

    /// <summary>How the transparent background is made (when <see cref="DockConfig.transparentBackground"/> is on).</summary>
    public enum DockTransparencyMode
    {
        /// <summary>
        /// Pure-black pixels are see-through, every other pixel is opaque (a layered window with a black color key).
        /// Free, but no partial transparency. Needs Direct3D11 without the flip-model swapchain.
        /// </summary>
        ColorKey = 0,
        /// <summary>
        /// The backbuffer's alpha channel drives transparency, so soft shadows, glows and anti-aliased edges blend
        /// with the desktop. Each frame is read back from the GPU and handed to the window with
        /// <c>UpdateLayeredWindow</c>. Works with Direct3D11, Direct3D12 and Vulkan, and with or without the
        /// flip-model swapchain. Costs a GPU readback and a copy per frame, and the window shows each frame one or
        /// two frames late.
        /// </summary>
        PerPixelAlpha = 1,
    }

    public enum DockSizeUnit
    {
        /// <summary>Physical screen pixels.</summary>
        Pixels = 0,
        /// <summary>Fraction of the monitor's height (horizontal docks) or width (vertical docks).</summary>
        PercentOfScreen = 1,
    }

    /// <summary>
    /// What counts as "content" under the mouse. Where none of the enabled sources report a hit,
    /// the window lets clicks fall through to whatever is behind it.
    /// </summary>
    [Flags]
    public enum DockHitTestSources
    {
        None = 0,
        /// <summary>uGUI graphics with Raycast Target enabled (via the EventSystem).</summary>
        UI = 1 << 0,
        /// <summary>2D colliders under the cursor.</summary>
        Physics2D = 1 << 1,
        /// <summary>3D colliders under the cursor.</summary>
        Physics3D = 1 << 2,
        /// <summary>
        /// Any pixel that is visible on screen. In <see cref="DockTransparencyMode.ColorKey"/> that is any pixel that
        /// isn't pure black (one 1x1 GPU readback per frame); it doesn't depend on the alpha channel. In
        /// <see cref="DockTransparencyMode.PerPixelAlpha"/> it is any pixel whose alpha is above
        /// <see cref="DockConfig.visibleAlphaThreshold"/>, read from the frame already shown (no extra readback).
        /// </summary>
        VisiblePixel = 1 << 3,
        Everything = UI | Physics2D | Physics3D | VisiblePixel,
    }

    public static class DockEdgeExtensions
    {
        public static DockOrientation Orientation(this DockEdge edge) =>
            edge == DockEdge.Left || edge == DockEdge.Right ? DockOrientation.Vertical : DockOrientation.Horizontal;
    }

    /// <summary>A monitor as the OS reports it. Rects use desktop coordinates: origin top-left, y grows downward.</summary>
    [Serializable]
    public struct DockMonitor
    {
        public RectInt bounds;
        /// <summary>Bounds minus the taskbar and any other reserved app bars.</summary>
        public RectInt workArea;
        public bool isPrimary;

        public override string ToString() =>
            $"{bounds.width}x{bounds.height} @ ({bounds.x},{bounds.y}){(isPrimary ? " [primary]" : "")}";
    }

    /// <summary>Where the window ended up after the last dock pass.</summary>
    public readonly struct DockState
    {
        public readonly DockEdge Edge;
        /// <summary>Window rect in desktop coordinates (origin top-left, y down).</summary>
        public readonly RectInt WindowRect;
        public readonly int MonitorIndex;
        /// <summary>True when no real window could be docked (Editor, unsupported platform): only the edge/orientation are meaningful.</summary>
        public readonly bool IsSimulated;

        public DockState(DockEdge edge, RectInt windowRect, int monitorIndex, bool isSimulated)
        {
            Edge = edge;
            WindowRect = windowRect;
            MonitorIndex = monitorIndex;
            IsSimulated = isSimulated;
        }

        public DockOrientation Orientation => Edge.Orientation();
        /// <summary>Size across the dock (height for horizontal docks, width for vertical ones).</summary>
        public int Thickness => Orientation == DockOrientation.Horizontal ? WindowRect.height : WindowRect.width;
        /// <summary>Size along the docked edge.</summary>
        public int Length => Orientation == DockOrientation.Horizontal ? WindowRect.width : WindowRect.height;

        public override string ToString() =>
            $"{Edge} dock, {WindowRect.width}x{WindowRect.height} @ ({WindowRect.x},{WindowRect.y}), monitor {MonitorIndex}{(IsSimulated ? " (simulated)" : "")}";
    }

    /// <summary>
    /// Implement on any object and register with <see cref="DesktopDockController.AddHitTestProvider"/> to mark
    /// extra regions as interactive (for example an invisible drag handle).
    /// </summary>
    public interface IDockHitTestProvider
    {
        /// <param name="screenPosition">Unity screen coordinates (origin bottom-left, pixels).</param>
        bool IsContentAt(Vector2 screenPosition);
    }
}
