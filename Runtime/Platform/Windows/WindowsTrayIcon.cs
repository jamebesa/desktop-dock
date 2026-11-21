#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using AOT;
using UnityEngine;

namespace DesktopDock.Platform.Windows
{
    /// <summary>
    /// A notification-area (tray) icon. Owns a hidden window that receives the icon's mouse messages; Unity's own
    /// message loop dispatches them on the main thread. Menu commands are queued and run from <see cref="ProcessPending"/>
    /// so game code never runs inside the window procedure. Only one instance can exist at a time.
    /// </summary>
    internal sealed class WindowsTrayIcon : ITrayIcon
    {
        private const string ClassName = "DesktopDockTrayWindow";
        private const uint IconId = 1;
        private const uint CallbackMessage = Win32.WM_USER + 0x44;

        private static WindowsTrayIcon s_instance;
        private static readonly Win32.WndProc s_wndProc = WndProc;
        private static bool s_classRegistered;
        private static uint s_taskbarCreated;

        private readonly Func<List<TrayMenuItem>> _buildMenu;
        private readonly Action _onLeftClick;
        private readonly Queue<Action> _pending = new Queue<Action>();
        private IntPtr _hwnd;
        private IntPtr _icon;
        private bool _ownsIcon;
        private bool _added;
        private string _tooltip;

        private WindowsTrayIcon(string tooltip, Func<List<TrayMenuItem>> buildMenu, Action onLeftClick)
        {
            _tooltip = tooltip;
            _buildMenu = buildMenu;
            _onLeftClick = onLeftClick;
        }

        /// <summary>Creates the icon, or returns null if one already exists or Windows refuses.</summary>
        public static WindowsTrayIcon Create(string tooltip, Func<List<TrayMenuItem>> buildMenu, Action onLeftClick)
        {
            if (s_instance != null) return null;
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
                    Debug.LogWarning("[DesktopDock] Could not register the tray icon's window class.");
                    return null;
                }
                s_taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
            }

            var tray = new WindowsTrayIcon(tooltip, buildMenu, onLeftClick);
            s_instance = tray;
            // A hidden top-level window (not message-only) so it also gets "TaskbarCreated" when Explorer restarts.
            tray._hwnd = Win32.CreateWindowEx(0, ClassName, "DesktopDock Tray", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
            if (tray._hwnd == IntPtr.Zero)
            {
                s_instance = null;
                Debug.LogWarning("[DesktopDock] Could not create the tray icon's window.");
                return null;
            }

            tray.LoadIcon();
            if (!tray.Add())
            {
                tray.Dispose();
                Debug.LogWarning("[DesktopDock] Windows refused to add the tray icon.");
                return null;
            }
            return tray;
        }

        public void SetTooltip(string tooltip)
        {
            _tooltip = tooltip;
            if (!_added) return;
            var data = NewData();
            data.uFlags = Win32.NIF_TIP;
            Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
        }

        /// <summary>Run commands picked from the menu or the icon. Call once per frame from the main thread.</summary>
        public void ProcessPending()
        {
            while (_pending.Count > 0)
            {
                var action = _pending.Dequeue();
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public void Dispose()
        {
            if (_added)
            {
                var data = NewData();
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
                _added = false;
            }
            if (_hwnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_ownsIcon && _icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            if (s_instance == this) s_instance = null;
        }

        // ------------------------------------------------------------ internals

        private void LoadIcon()
        {
            // The game's own icon (set in Player Settings), taken from the executable.
            string exe = Win32.ExecutablePath();
            if (exe != null && Win32.ExtractIconEx(exe, 0, out IntPtr large, out IntPtr small, 1) > 0)
            {
                if (large != IntPtr.Zero && small != IntPtr.Zero) Win32.DestroyIcon(large);
                _icon = small != IntPtr.Zero ? small : large;
                _ownsIcon = _icon != IntPtr.Zero;
            }
            if (_icon == IntPtr.Zero)
            {
                _icon = Win32.LoadIcon(IntPtr.Zero, Win32.IDI_APPLICATION);
                _ownsIcon = false;
            }
        }

        private bool Add()
        {
            var data = NewData();
            data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
            _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
            return _added;
        }

        private Win32.NOTIFYICONDATA NewData() => new Win32.NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = Truncate(_tooltip, 127),
            szInfo = "",
            szInfoTitle = "",
        };

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);

        private void ShowMenu()
        {
            List<TrayMenuItem> items;
            try { items = _buildMenu(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (items == null || items.Count == 0) return;

            var actions = new List<Action>();
            IntPtr menu = BuildMenu(items, actions);
            Win32.GetCursorPos(out var p);
            // Needed so the menu closes when the player clicks elsewhere.
            Win32.SetForegroundWindow(_hwnd);
            int command = Win32.TrackPopupMenuEx(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_NONOTIFY,
                p.X, p.Y, _hwnd, IntPtr.Zero);
            Win32.PostMessage(_hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            Win32.DestroyMenu(menu); // also destroys submenus

            if (command > 0 && command <= actions.Count) _pending.Enqueue(actions[command - 1]);
        }

        private static IntPtr BuildMenu(List<TrayMenuItem> items, List<Action> actions)
        {
            IntPtr menu = Win32.CreatePopupMenu();
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.IsSeparator)
                {
                    Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                uint flags = Win32.MF_STRING | (item.Enabled ? 0 : Win32.MF_GRAYED);
                if (item.Children != null && item.Children.Count > 0)
                {
                    IntPtr sub = BuildMenu(item.Children, actions);
                    Win32.AppendMenu(menu, flags | Win32.MF_POPUP, new UIntPtr((ulong)sub.ToInt64()), item.Label);
                    continue;
                }

                actions.Add(item.OnClick);
                uint id = (uint)actions.Count;
                Win32.AppendMenu(menu, flags | (item.Checked ? Win32.MF_CHECKED : 0), new UIntPtr(id), item.Label);
                if (item.IsDefault) Win32.SetMenuDefaultItem(menu, id, 0);
            }
            return menu;
        }

        [MonoPInvokeCallback(typeof(Win32.WndProc))]
        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                var self = s_instance;
                if (self != null && hWnd == self._hwnd)
                {
                    if (msg == CallbackMessage)
                    {
                        uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                        if (mouse == Win32.WM_LBUTTONUP)
                        {
                            if (self._onLeftClick != null) self._pending.Enqueue(self._onLeftClick);
                        }
                        else if (mouse == Win32.WM_RBUTTONUP || mouse == Win32.WM_CONTEXTMENU)
                        {
                            self.ShowMenu();
                        }
                        return IntPtr.Zero;
                    }
                    if (s_taskbarCreated != 0 && msg == s_taskbarCreated)
                    {
                        // Explorer restarted and forgot every tray icon.
                        self._added = false;
                        self.Add();
                        return IntPtr.Zero;
                    }
                }
            }
            catch (Exception e)
            {
                // Never let an exception cross back into native code.
                Debug.LogException(e);
            }
            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
#endif
