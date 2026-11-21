#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using AOT;
using UnityEngine;

namespace DesktopDock.Platform.Windows
{
    /// <summary>
    /// System-wide hotkeys through <c>RegisterHotKey</c>. A hidden message-only window receives <c>WM_HOTKEY</c>; Unity's
    /// message loop dispatches it on the main thread, and presses are queued until <see cref="DispatchPressed"/> runs
    /// the callbacks from a normal Update, never inside the window procedure.
    /// </summary>
    internal static class WindowsHotkeys
    {
        private const string ClassName = "DesktopDockHotkeyWindow";

        private static readonly Win32.WndProc s_wndProc = WndProc;
        private static readonly Dictionary<int, Action> s_callbacks = new Dictionary<int, Action>();
        private static readonly Queue<int> s_pressed = new Queue<int>();
        private static bool s_classRegistered;
        private static IntPtr s_hwnd;
        private static int s_nextId = 1;

        /// <summary>Registers a hotkey. Returns its id, or 0 if Windows refused (usually: another app already uses it).</summary>
        public static int Register(uint modifiers, uint virtualKey, Action callback)
        {
            if (!EnsureWindow()) return 0;
            int id = s_nextId++;
            if (!Win32.RegisterHotKey(s_hwnd, id, modifiers | Win32.MOD_NOREPEAT, virtualKey)) return 0;
            s_callbacks[id] = callback;
            return id;
        }

        public static void Unregister(int id)
        {
            if (id == 0 || !s_callbacks.Remove(id)) return;
            Win32.UnregisterHotKey(s_hwnd, id);
        }

        /// <summary>Run the callbacks of hotkeys pressed since the last call. Main thread only.</summary>
        public static void DispatchPressed()
        {
            while (s_pressed.Count > 0)
            {
                int id = s_pressed.Dequeue();
                if (!s_callbacks.TryGetValue(id, out var callback)) continue;
                try { callback?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static bool EnsureWindow()
        {
            if (s_hwnd != IntPtr.Zero) return true;
            IntPtr module = Win32.GetModuleHandle(null);
            if (!s_classRegistered)
            {
                var wc = new Win32.WNDCLASSEX
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.WNDCLASSEX>(),
                    lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(s_wndProc),
                    hInstance = module,
                    lpszClassName = ClassName,
                };
                s_classRegistered = Win32.RegisterClassEx(ref wc) != 0;
                if (!s_classRegistered)
                {
                    Debug.LogWarning("[DesktopDock] Could not register the hotkey window class.");
                    return false;
                }
            }
            // Message-only: never shown, only receives the WM_HOTKEY messages posted to it.
            s_hwnd = Win32.CreateWindowEx(0, ClassName, "DesktopDock Hotkeys", 0, 0, 0, 0, 0,
                Win32.HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);
            if (s_hwnd == IntPtr.Zero) Debug.LogWarning("[DesktopDock] Could not create the hotkey window.");
            return s_hwnd != IntPtr.Zero;
        }

        [MonoPInvokeCallback(typeof(Win32.WndProc))]
        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == Win32.WM_HOTKEY && hWnd == s_hwnd)
                {
                    s_pressed.Enqueue((int)wParam.ToInt64());
                    return IntPtr.Zero;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
#endif
