#if UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DesktopDock.Platform.Windows
{
    /// <summary>
    /// <see cref="DockTransparencyMode.PerPixelAlpha"/> on Windows. A layered, click-through window owned by the game
    /// window shows each frame with <c>UpdateLayeredWindow</c>, so the desktop compositor blends it per pixel.
    ///
    /// Why a second window: with a flip-model swapchain (always the case on Direct3D12 and Vulkan) DWM composes the
    /// game window's swapchain directly and opaquely, on top of anything UpdateLayeredWindow gave it, and ignores
    /// its color key. A uniform layered alpha does apply, though, so the game window is kept at alpha 1/255: invisible,
    /// but still the window that gets mouse input and focus. This overlay sits directly above it and never takes input.
    /// </summary>
    internal sealed class WindowsAlphaOverlay : IDisposable
    {
        private const string ClassName = "DesktopDockAlphaOverlay";
        private static bool s_classRegistered;

        private readonly IntPtr _owner;
        private IntPtr _hwnd;
        private IntPtr _dc;
        private IntPtr _bitmap;
        private IntPtr _oldBitmap;
        private IntPtr _bits;
        private int _width, _height;
        private bool _topDown;
        private bool _loggedFailure;

        public IntPtr Handle => _hwnd;

        public WindowsAlphaOverlay(IntPtr owner)
        {
            _owner = owner;
        }

        public bool EnsureCreated()
        {
            if (_hwnd != IntPtr.Zero) return true;
            IntPtr instance = Win32.GetModuleHandle(null);
            if (!s_classRegistered)
            {
                var wc = new Win32.WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                    // The window never handles anything itself, so it can use DefWindowProc directly.
                    lpfnWndProc = Win32.GetProcAddress(Win32.GetModuleHandle("user32.dll"), "DefWindowProcW"),
                    hInstance = instance,
                    lpszClassName = ClassName,
                };
                s_classRegistered = Win32.RegisterClassEx(ref wc) != 0;
            }

            bool ownerVisible = (Win32.GetWindowLong(_owner, Win32.GWL_STYLE) & Win32.WS_VISIBLE) != 0;
            // Owned by the game window: always above it, never on the taskbar. Transparent + layered: clicks pass
            // through to the game window underneath.
            _hwnd = Win32.CreateWindowEx(
                Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
                ClassName, "Desktop Dock", Win32.WS_POPUP | (ownerVisible ? Win32.WS_VISIBLE : 0),
                0, 0, 1, 1, _owner, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                Debug.LogWarning("[DesktopDock] Could not create the per-pixel alpha window (error " + Marshal.GetLastWin32Error() + ").");
            return _hwnd != IntPtr.Zero;
        }

        /// <summary>Show premultiplied BGRA pixels at <paramref name="position"/> (desktop coordinates, top-left).</summary>
        public unsafe bool Present(IntPtr bgraPixels, int width, int height, bool topRowFirst, Win32.POINT position)
        {
            if (!EnsureCreated() || !EnsureSurface(width, height, topRowFirst)) return false;
            UnsafeUtility.MemCpy((void*)_bits, (void*)bgraPixels, (long)width * height * 4);
            Win32.GdiFlush();

            var size = new Win32.SIZE { cx = width, cy = height };
            var src = new Win32.POINT();
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Win32.AC_SRC_ALPHA,
            };
            bool ok = Win32.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref position, ref size, _dc, ref src, 0, ref blend, Win32.ULW_ALPHA);
            if (!ok && !_loggedFailure)
            {
                _loggedFailure = true;
                Debug.LogWarning("[DesktopDock] UpdateLayeredWindow failed (error " + Marshal.GetLastWin32Error() + "); per-pixel transparency is not showing.");
            }
            return ok;
        }

        public void SetVisible(bool visible)
        {
            if (_hwnd != IntPtr.Zero) Win32.ShowWindow(_hwnd, visible ? Win32.SW_SHOWNOACTIVATE : Win32.SW_HIDE);
        }

        /// <summary>Put the overlay at <paramref name="insertAfter"/>'s z-position, then the game window right below it.</summary>
        public void Stack(IntPtr insertAfter)
        {
            if (_hwnd == IntPtr.Zero) return;
            const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
            Win32.SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, flags);
            Win32.SetWindowPos(_owner, _hwnd, 0, 0, 0, 0, flags);
        }

        private bool EnsureSurface(int width, int height, bool topRowFirst)
        {
            if (_dc != IntPtr.Zero && _width == width && _height == height && _topDown == topRowFirst) return true;
            ReleaseSurface();

            IntPtr screen = Win32.GetDC(IntPtr.Zero);
            _dc = Win32.CreateCompatibleDC(screen);
            Win32.ReleaseDC(IntPtr.Zero, screen);
            if (_dc == IntPtr.Zero) return false;

            var bmi = new Win32.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = topRowFirst ? -height : height, // negative: rows run top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB,
            };
            _bitmap = Win32.CreateDIBSection(_dc, ref bmi, Win32.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
            {
                ReleaseSurface();
                return false;
            }
            _oldBitmap = Win32.SelectObject(_dc, _bitmap);
            _width = width;
            _height = height;
            _topDown = topRowFirst;
            return true;
        }

        private void ReleaseSurface()
        {
            if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) Win32.SelectObject(_dc, _oldBitmap);
            if (_bitmap != IntPtr.Zero) Win32.DeleteObject(_bitmap);
            if (_dc != IntPtr.Zero) Win32.DeleteDC(_dc);
            _dc = _bitmap = _oldBitmap = _bits = IntPtr.Zero;
            _width = _height = 0;
        }

        public void Dispose()
        {
            ReleaseSurface();
            if (_hwnd != IntPtr.Zero) Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
#endif
