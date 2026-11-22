#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Text;
using AOT;
using UnityEngine;

namespace DesktopDock.Platform.Windows
{
    /// <summary>
    /// Windows implementation. ColorKey transparency uses the well-known Unity recipe: a layered popup window with a
    /// black color key plus DwmExtendFrameIntoClientArea. Measured result: exactly the pure-black pixels show the
    /// desktop and every other pixel is opaque; the backbuffer's alpha channel is not used by the compositor.
    /// PerPixelAlpha shows read-back frames in a separate layered window (see <see cref="WindowsAlphaOverlay"/>).
    /// Click-through toggles WS_EX_TRANSPARENT depending on what is under the cursor.
    /// </summary>
    internal sealed class WindowsDockPlatform : IDockPlatform
    {
        private const string UnityWindowClass = "UnityWndClass";

        private IntPtr _hwnd;
        private bool _clickThrough;
        private bool _layered;
        private bool? _showInTaskbar;
        private bool _appBarRegistered;
        private uint _appBarCallbackMessage;

        public bool IsSupported => true;

        public bool BlendsAlphaNatively => false;
        public float PixelsPerPoint => 1f;

        private bool? _supportsColorKey;

        /// <summary>
        /// The color key only works on Direct3D11 with the bitblt-model swapchain. Direct3D12 and Vulkan always use the
        /// flip model, which DWM composes opaque (measured: the see-through area renders black). A build made with
        /// "Use DXGI flip model swapchain" off writes <c>force-d3d11-bitblt-model</c> into boot.config; the same switch
        /// can also be passed on the command line.
        /// </summary>
        public bool SupportsColorKey => _supportsColorKey ??= DetectColorKeySupport();

        private static bool DetectColorKeySupport()
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11) return false;
            const string BitbltSwitch = "force-d3d11-bitblt-model";
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.TrimStart('-').Equals(BitbltSwitch, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string bootConfig = System.IO.Path.Combine(Application.dataPath, "boot.config");
                if (!System.IO.File.Exists(bootConfig)) return false;
                foreach (var line in System.IO.File.ReadAllLines(bootConfig))
                    if (line.TrimStart().StartsWith(BitbltSwitch, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DesktopDock] Couldn't read boot.config to check the swapchain model: " + e.Message);
            }
            return false;
        }

        public bool Initialize()
        {
            _hwnd = FindUnityWindow();
            if (_hwnd == IntPtr.Zero) _hwnd = Win32.GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogWarning("[DesktopDock] Could not find the game window; docking is disabled.");
                return false;
            }
            _appBarCallbackMessage = Win32.RegisterWindowMessage("DesktopDock.AppBarNotify");
            return true;
        }

        // ---------------------------------------------------------------- window lookup

        private static uint s_searchPid;
        private static IntPtr s_searchResult;
        private static IntPtr s_searchFallback;
        private static readonly Win32.EnumWindowsProc s_enumWindows = OnEnumWindow;

        private static IntPtr FindUnityWindow()
        {
            s_searchPid = Win32.GetCurrentProcessId();
            s_searchResult = IntPtr.Zero;
            s_searchFallback = IntPtr.Zero;
            Win32.EnumWindows(s_enumWindows, IntPtr.Zero);
            return s_searchResult != IntPtr.Zero ? s_searchResult : s_searchFallback;
        }

        [MonoPInvokeCallback(typeof(Win32.EnumWindowsProc))]
        private static bool OnEnumWindow(IntPtr hWnd, IntPtr lParam)
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != s_searchPid) return true;

            var sb = new StringBuilder(64);
            Win32.GetClassName(hWnd, sb, sb.Capacity);
            if (sb.ToString() != UnityWindowClass) return true;

            if (Win32.IsWindowVisible(hWnd))
            {
                s_searchResult = hWnd;
                return false; // stop
            }
            if (s_searchFallback == IntPtr.Zero) s_searchFallback = hWnd;
            return true;
        }

        // ---------------------------------------------------------------- monitors

        private static readonly List<IntPtr> s_monitorHandles = new List<IntPtr>();
        private static readonly Win32.MonitorEnumProc s_enumMonitors = OnEnumMonitor;

        [MonoPInvokeCallback(typeof(Win32.MonitorEnumProc))]
        private static bool OnEnumMonitor(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            s_monitorHandles.Add(hMonitor);
            return true;
        }

        public IReadOnlyList<DockMonitor> GetMonitors()
        {
            s_monitorHandles.Clear();
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, s_enumMonitors, IntPtr.Zero);

            var result = new List<DockMonitor>(s_monitorHandles.Count);
            foreach (var h in s_monitorHandles)
            {
                var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
                if (!Win32.GetMonitorInfo(h, ref info)) continue;
                result.Add(new DockMonitor
                {
                    bounds = ToRectInt(info.rcMonitor),
                    workArea = ToRectInt(info.rcWork),
                    isPrimary = (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
                });
            }
            return result;
        }

        // ---------------------------------------------------------------- style

        public void ApplyWindowStyle(DockConfig config)
        {
            if (_hwnd == IntPtr.Zero) return;

            // Keep the current visibility: the dock may be hidden (tray, fullscreen app) while being restyled.
            bool visible = (Win32.GetWindowLong(_hwnd, Win32.GWL_STYLE) & Win32.WS_VISIBLE) != 0;
            Win32.SetWindowLong(_hwnd, Win32.GWL_STYLE,
                Win32.WS_POPUP | Win32.WS_CLIPSIBLINGS | (visible ? Win32.WS_VISIBLE : 0));

            uint ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            ex &= ~Win32.WS_EX_TRANSPARENT;
            _clickThrough = false;

            if (config.showInTaskbar) ex = (ex | Win32.WS_EX_APPWINDOW) & ~Win32.WS_EX_TOOLWINDOW;
            else ex = (ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW;

            var layering = !config.transparentBackground ? Layering.None
                : config.transparencyMode == DockTransparencyMode.PerPixelAlpha ? Layering.PerPixelAlpha
                : Layering.ColorKey;
            _layered = layering != Layering.None;
            if (_layered) ex |= Win32.WS_EX_LAYERED;
            else ex &= ~Win32.WS_EX_LAYERED;

            bool taskbarChanged = visible && _showInTaskbar.HasValue && _showInTaskbar.Value != config.showInTaskbar;
            _showInTaskbar = config.showInTaskbar;

            // The taskbar only notices WS_EX_TOOLWINDOW/APPWINDOW changes when the window is re-shown.
            if (taskbarChanged) Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex);
            if (taskbarChanged) Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);

            // Color key: DWM shows the swapchain minus its pure-black pixels. Per-pixel alpha: the game window is kept
            // at alpha 1/255 (invisible, but it still gets the mouse) and the overlay above it shows the frames.
            var margins = layering == Layering.ColorKey
                ? new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 }
                : new Win32.MARGINS();
            if (layering == Layering.ColorKey)
                Win32.SetLayeredWindowAttributes(_hwnd, 0x000000, 0, Win32.LWA_COLORKEY);
            else if (layering == Layering.PerPixelAlpha)
                Win32.SetLayeredWindowAttributes(_hwnd, 0, 1, Win32.LWA_ALPHA);
            Win32.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

            if (layering == Layering.PerPixelAlpha)
            {
                _overlay ??= new WindowsAlphaOverlay(_hwnd);
                _overlay.EnsureCreated();
            }
            else
            {
                _overlay?.Dispose();
                _overlay = null;
            }
        }

        private enum Layering
        {
            None,
            ColorKey,
            PerPixelAlpha,
        }

        // ---------------------------------------------------------------- per-pixel alpha

        private WindowsAlphaOverlay _overlay;

        public bool PresentAlphaFrame(IntPtr bgraPixels, int width, int height, bool topRowFirst)
        {
            if (_hwnd == IntPtr.Zero || _overlay == null || width <= 0 || height <= 0) return false;
            // A frame rendered before the last resize would show at the wrong size; wait for one that fits.
            if (!Win32.GetWindowRect(_hwnd, out var wr) || wr.Width != width || wr.Height != height) return false;
            return _overlay.Present(bgraPixels, width, height, topRowFirst, new Win32.POINT { X = wr.Left, Y = wr.Top });
        }

        // ---------------------------------------------------------------- placement

        public RectInt PlaceDock(DockConfig config, DockMonitor monitor)
        {
            if (!config.reserveScreenSpace)
            {
                ReleaseReservedSpace();
                return DockGeometry.Compute(config, config.respectTaskbar ? monitor.workArea : monitor.bounds);
            }

            // App bar: start from the whole monitor, let the shell push us off the taskbar / other bars,
            // then reserve exactly our thickness on that edge.
            if (!_appBarRegistered)
            {
                var reg = NewAppBarData();
                reg.uCallbackMessage = _appBarCallbackMessage;
                _appBarRegistered = Win32.SHAppBarMessage(Win32.ABM_NEW, ref reg) != UIntPtr.Zero;
                if (!_appBarRegistered)
                {
                    Debug.LogWarning("[DesktopDock] Could not register as an app bar; docking without reserving space.");
                    return DockGeometry.Compute(config, config.respectTaskbar ? monitor.workArea : monitor.bounds);
                }
            }

            int thickness = DockGeometry.Thickness(config, monitor.bounds);
            var abd = NewAppBarData();
            abd.uEdge = ToAppBarEdge(config.edge);
            abd.rc = ToRECT(monitor.bounds);
            ApplyThickness(ref abd.rc, config.edge, thickness);
            Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref abd);
            ApplyThickness(ref abd.rc, config.edge, thickness);
            Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref abd);

            // The reserved strip spans the whole edge; the window itself honours length/alignment within it.
            return DockGeometry.Compute(config, ToRectInt(abd.rc), thickness);
        }

        public void ReleaseReservedSpace()
        {
            if (!_appBarRegistered) return;
            var abd = NewAppBarData();
            Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref abd);
            _appBarRegistered = false;
        }

        public void SetWindowRect(RectInt rect, bool alwaysOnTop)
        {
            if (_hwnd == IntPtr.Zero) return;
            IntPtr order = alwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST;
            Win32.SetWindowPos(_hwnd, order, rect.x, rect.y, rect.width, rect.height,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOACTIVATE);
            _overlay?.Stack(order);
        }

        public bool TryGetWindowRect(out RectInt rect)
        {
            rect = default;
            if (_hwnd == IntPtr.Zero || !Win32.GetWindowRect(_hwnd, out var r)) return false;
            rect = ToRectInt(r);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (_hwnd == IntPtr.Zero) return;
            Win32.ShowWindow(_hwnd, visible ? Win32.SW_SHOWNOACTIVATE : Win32.SW_HIDE);
            _overlay?.SetVisible(visible);
        }

        public void SendToBack()
        {
            if (_hwnd == IntPtr.Zero) return;
            Win32.SetWindowPos(_hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            _overlay?.Stack(Win32.HWND_BOTTOM);
        }

        public void MoveWindow(Vector2Int topLeft)
        {
            if (_hwnd == IntPtr.Zero) return;
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, topLeft.x, topLeft.y, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }

        public void PlaceBelowTaskbar()
        {
            if (_hwnd == IntPtr.Zero) return;
            IntPtr taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;
            // Inserting after a top-most window keeps ours top-most, just under it.
            Win32.SetWindowPos(_hwnd, taskbar, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            _overlay?.Stack(taskbar);
        }

        public bool IsFullscreenAppActive(RectInt monitorBounds)
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;

            Win32.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == Win32.GetCurrentProcessId()) return false;

            // The desktop and the taskbar cover the monitor too but aren't apps.
            var sb = new StringBuilder(64);
            Win32.GetClassName(fg, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                return false;

            if (!Win32.GetWindowRect(fg, out var r)) return false;
            uint style = Win32.GetWindowLong(fg, Win32.GWL_STYLE);
            bool maximizedWithTitleBar = (style & Win32.WS_MAXIMIZE) != 0 && (style & Win32.WS_CAPTION) == Win32.WS_CAPTION;
            return DockGeometry.IsFullscreenWindow(ToRectInt(r), monitorBounds, maximizedWithTitleBar);
        }

        public bool TryGetCursorDesktopPosition(out Vector2Int position)
        {
            position = default;
            if (!Win32.GetCursorPos(out var p)) return false;
            position = new Vector2Int(p.X, p.Y);
            return true;
        }

        // ---------------------------------------------------------------- input

        public void SetClickThrough(bool clickThrough)
        {
            if (_hwnd == IntPtr.Zero || clickThrough == _clickThrough) return;
            // WS_EX_TRANSPARENT only makes a window click-through when it is also layered.
            if (clickThrough && !_layered) return;

            uint ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            ex = clickThrough ? ex | Win32.WS_EX_TRANSPARENT : ex & ~Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex);
            _clickThrough = clickThrough;

            // While click-through, the cursor's moves went to the window behind, so Unity's mouse position is stale.
            // Without a fresh one, a click that comes before the next move would land wherever the cursor last was.
            if (!clickThrough && Win32.GetCursorPos(out var p) && Win32.ScreenToClient(_hwnd, ref p))
            {
                long lParam = ((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF);
                Win32.PostMessage(_hwnd, Win32.WM_MOUSEMOVE, IntPtr.Zero, new IntPtr(lParam));
            }
        }

        public bool TryGetCursorPosition(out Vector2 screenPosition, out bool insideWindow)
        {
            screenPosition = default;
            insideWindow = false;
            if (_hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var p)) return false;
            if (!Win32.ScreenToClient(_hwnd, ref p) || !Win32.GetClientRect(_hwnd, out var client)) return false;

            int w = client.Width, h = client.Height;
            if (w <= 0 || h <= 0) return false;

            insideWindow = p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h;
            // Client space is y-down; Unity screen space is y-up. Scale in case the backbuffer lags a resize.
            float sx = (float)Screen.width / w, sy = (float)Screen.height / h;
            screenPosition = new Vector2((p.X + 0.5f) * sx, (h - p.Y - 0.5f) * sy);
            return true;
        }

        public bool IsAnyMouseButtonDown() =>
            (Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0 ||
            (Win32.GetAsyncKeyState(Win32.VK_RBUTTON) & 0x8000) != 0 ||
            (Win32.GetAsyncKeyState(Win32.VK_MBUTTON) & 0x8000) != 0;

        public void Shutdown()
        {
            ReleaseReservedSpace();
            _overlay?.Dispose();
            _overlay = null;
        }

        // ---------------------------------------------------------------- helpers

        private Win32.APPBARDATA NewAppBarData() => new Win32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.APPBARDATA>(),
            hWnd = _hwnd,
        };

        private static void ApplyThickness(ref Win32.RECT rc, DockEdge edge, int thickness)
        {
            switch (edge)
            {
                case DockEdge.Top: rc.Bottom = rc.Top + thickness; break;
                case DockEdge.Left: rc.Right = rc.Left + thickness; break;
                case DockEdge.Right: rc.Left = rc.Right - thickness; break;
                default: rc.Top = rc.Bottom - thickness; break;
            }
        }

        private static uint ToAppBarEdge(DockEdge edge) => edge switch
        {
            DockEdge.Top => Win32.ABE_TOP,
            DockEdge.Left => Win32.ABE_LEFT,
            DockEdge.Right => Win32.ABE_RIGHT,
            _ => Win32.ABE_BOTTOM,
        };

        private static RectInt ToRectInt(Win32.RECT r) => new RectInt(r.Left, r.Top, r.Width, r.Height);

        private static Win32.RECT ToRECT(RectInt r) => new Win32.RECT
        {
            Left = r.x, Top = r.y, Right = r.xMax, Bottom = r.yMax,
        };
    }
}
#endif
