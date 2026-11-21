#if UNITY_STANDALONE_OSX || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopDock.Platform.MacOS
{
    /// <summary>
    /// macOS implementation, backed by the native <c>DesktopDockMac.bundle</c> (source and build script in
    /// <c>Plugins~/macOS</c>). UNTESTED: it was written without a Mac; see the README.
    /// <para>
    /// The player's NSWindow is made borderless and non-opaque with a clear background, so unlike Windows the
    /// backbuffer's alpha channel decides what is see-through. Pinning uses the floating window level, monitors come
    /// from NSScreen (<c>visibleFrame</c> excludes the menu bar and the Dock), click-through toggles
    /// <c>ignoresMouseEvents</c>, and the cursor is read globally with <c>NSEvent.mouseLocation</c>.
    /// </para>
    /// Compiled in every Editor so it stays compile-checked; only created in macOS players.
    /// </summary>
    internal sealed class MacDockPlatform : IDockPlatform
    {
        private bool _ready;
        private bool _transparent;
        private bool _clickThrough;
        private bool _warnedReserve;
        private MacCoordinates.ScaleTracker _scaleTracker;
        private double _primaryHeight;  // points
        private RectInt _lastSetRect;
        private bool _hasSetRect;

        public bool IsSupported => true;

        // The NSWindow is non-opaque and AppKit blends it with the backbuffer's alpha, so there is no color key and
        // no frames to copy: transparency is always per pixel, whatever DockConfig.transparencyMode says.
        public bool SupportsColorKey => false;
        public bool BlendsAlphaNatively => true;
        public bool PresentAlphaFrame(IntPtr bgraPixels, int width, int height, bool topRowFirst) => false;

        public bool Initialize()
        {
            try
            {
                _ready = MacNative.DD_Initialize() != 0;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.LogWarning("[DesktopDock] The native plugin DesktopDockMac.bundle is missing or out of date, so " +
                                 "docking is disabled. Build it with DesktopDock/Plugins~/macOS/build.sh. (" + e.Message + ")");
                return false;
            }

            if (!_ready)
            {
                Debug.LogWarning("[DesktopDock] Could not find the game window; docking is disabled.");
                return false;
            }
            RefreshScale();
            return true;
        }

        /// <summary>Re-read the primary screen height and how many Unity pixels make one point.</summary>
        private void RefreshScale()
        {
            _primaryHeight = MacNative.DD_GetPrimaryScreenHeight();
            float backing = (float)MacNative.DD_GetBackingScale();
            float measured = 0f;
            if (MacNative.DD_GetContentRect(out _, out _, out double w, out _) != 0 && w > 0)
                measured = (float)(Screen.width / w);
            _scaleTracker.Observe(measured, backing, Time.frameCount);
        }

        // ---------------------------------------------------------------- monitors

        public IReadOnlyList<DockMonitor> GetMonitors()
        {
            var result = new List<DockMonitor>();
            if (!_ready) return result;
            RefreshScale();

            int count = MacNative.DD_GetScreenCount();
            for (int i = 0; i < count; i++)
            {
                if (MacNative.DD_GetScreen(i, out double x, out double y, out double w, out double h,
                        out double vx, out double vy, out double vw, out double vh, out _) == 0)
                    continue;
                result.Add(new DockMonitor
                {
                    bounds = MacCoordinates.CocoaToDesktop(x, y, w, h, _primaryHeight, _scaleTracker.Scale),
                    workArea = MacCoordinates.CocoaToDesktop(vx, vy, vw, vh, _primaryHeight, _scaleTracker.Scale),
                    // NSScreen.screens[0] holds the menu bar and the coordinate origin.
                    isPrimary = i == 0,
                });
            }
            return result;
        }

        // ---------------------------------------------------------------- style

        public void ApplyWindowStyle(DockConfig config)
        {
            if (!_ready) return;
            _transparent = config.transparentBackground;
            // "Taskbar button" maps to the Dock icon and the Cmd-Tab entry.
            MacNative.DD_ApplyStyle(_transparent ? 1 : 0, config.showInTaskbar ? 1 : 0);
            _clickThrough = false; // DD_ApplyStyle resets ignoresMouseEvents
        }

        // ---------------------------------------------------------------- placement

        public RectInt PlaceDock(DockConfig config, DockMonitor monitor)
        {
            if (config.reserveScreenSpace && !_warnedReserve)
            {
                _warnedReserve = true;
                Debug.Log("[DesktopDock] Reserve Screen Space has no macOS equivalent and is ignored.");
            }
            // workArea is NSScreen.visibleFrame: it already leaves out the menu bar and the Dock.
            return DockGeometry.Compute(config, config.respectTaskbar ? monitor.workArea : monitor.bounds);
        }

        public void ReleaseReservedSpace() { }

        public void SetWindowRect(RectInt rect, bool alwaysOnTop)
        {
            if (!_ready) return;
            MacCoordinates.DesktopToCocoa(rect, _primaryHeight, _scaleTracker.Scale, out double x, out double y, out double w, out double h);
            MacNative.DD_SetFrame(x, y, w, h, alwaysOnTop ? 1 : 0);
            _lastSetRect = rect;
            _hasSetRect = true;
        }

        public bool TryGetWindowRect(out RectInt rect)
        {
            rect = default;
            if (!_ready || MacNative.DD_GetFrame(out double x, out double y, out double w, out double h) == 0) return false;
            rect = MacCoordinates.CocoaToDesktop(x, y, w, h, _primaryHeight, _scaleTracker.Scale);
            if (_hasSetRect) rect = MacCoordinates.SnapToExpected(rect, _lastSetRect);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (_ready) MacNative.DD_SetVisible(visible ? 1 : 0);
        }

        public void SendToBack()
        {
            if (_ready) MacNative.DD_SendToBack();
        }

        public bool IsFullscreenAppActive(RectInt monitorBounds)
        {
            if (!_ready) return false;
            MacCoordinates.DesktopToQuartz(monitorBounds, _scaleTracker.Scale, out double x, out double y, out double w, out double h);
            return MacNative.DD_IsFullscreenAppActive(x, y, w, h) != 0;
        }

        public bool TryGetCursorDesktopPosition(out Vector2Int position)
        {
            position = default;
            if (!_ready) return false;
            MacNative.DD_GetMouseLocation(out double x, out double y);
            position = MacCoordinates.CocoaPointToDesktop(x, y, _primaryHeight, _scaleTracker.Scale);
            return true;
        }

        // ---------------------------------------------------------------- input

        public void SetClickThrough(bool clickThrough)
        {
            if (!_ready || clickThrough == _clickThrough) return;
            // Same rule as Windows: only a see-through window lets clicks through.
            if (clickThrough && !_transparent) return;
            MacNative.DD_SetIgnoresMouseEvents(clickThrough ? 1 : 0);
            _clickThrough = clickThrough;
        }

        public bool TryGetCursorPosition(out Vector2 screenPosition, out bool insideWindow)
        {
            screenPosition = default;
            insideWindow = false;
            if (!_ready || MacNative.DD_GetContentRect(out double cx, out double cy, out double cw, out double ch) == 0)
                return false;
            if (cw <= 0 || ch <= 0) return false;

            MacNative.DD_GetMouseLocation(out double mx, out double my);
            insideWindow = MacCoordinates.CocoaPointToScreen(mx, my, cx, cy, cw, ch, Screen.width, Screen.height, out screenPosition)
                           && MacNative.DD_IsVisible() != 0;
            return true;
        }

        public bool IsAnyMouseButtonDown() => _ready && (MacNative.DD_GetPressedMouseButtons() & 0x7) != 0;

        public void Shutdown()
        {
            if (!_ready) return;
            _ready = false;
            MacNative.DD_Shutdown();
        }
    }
}
#endif
