#if UNITY_STANDALONE_OSX || UNITY_EDITOR
using System;
using System.Collections.Generic;
using AOT;
using UnityEngine;

namespace DesktopDock.Platform.MacOS
{
    /// <summary>
    /// The macOS counterpart of the tray icon: an NSStatusItem in the menu bar showing the app icon. UNTESTED.
    /// The menu is rebuilt from <see cref="TrayMenuItem"/>s each time it opens. With a left-click action, a left click
    /// runs it and a right (or Control-) click opens the menu; without one, any click opens the menu, the usual macOS
    /// behaviour. Commands are queued and run from <see cref="ProcessPending"/>, never inside AppKit's menu tracking.
    /// Only one instance can exist at a time.
    /// </summary>
    internal sealed class MacTrayIcon : ITrayIcon
    {
        private static MacTrayIcon s_instance;
        // Kept in static fields so the delegates outlive every native call that might use them.
        private static readonly MacNative.TrayPopulateCallback s_populate = OnPopulate;
        private static readonly MacNative.TrayCommandCallback s_command = OnCommand;

        private readonly Func<List<TrayMenuItem>> _buildMenu;
        private readonly Action _onLeftClick;
        private readonly Queue<Action> _pending = new Queue<Action>();
        private readonly List<Action> _menuActions = new List<Action>();
        private bool _created;

        private MacTrayIcon(Func<List<TrayMenuItem>> buildMenu, Action onLeftClick)
        {
            _buildMenu = buildMenu;
            _onLeftClick = onLeftClick;
        }

        /// <summary>Creates the status item, or returns null if one already exists or the native plugin is missing.</summary>
        public static MacTrayIcon Create(string tooltip, Func<List<TrayMenuItem>> buildMenu, Action onLeftClick)
        {
            if (s_instance != null) return null;
            var tray = new MacTrayIcon(buildMenu, onLeftClick);
            s_instance = tray;
            try
            {
                tray._created = MacNative.DD_TrayCreate(MacNative.Utf8(tooltip), onLeftClick != null ? 1 : 0,
                    s_populate, s_command) != 0;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.LogWarning("[DesktopDock] No menu bar icon: the native plugin DesktopDockMac.bundle is missing. (" + e.Message + ")");
            }
            if (!tray._created)
            {
                s_instance = null;
                return null;
            }
            return tray;
        }

        public void SetTooltip(string tooltip)
        {
            if (_created) MacNative.DD_TraySetTooltip(MacNative.Utf8(tooltip));
        }

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
            if (_created)
            {
                MacNative.DD_TrayRemove();
                _created = false;
            }
            if (s_instance == this) s_instance = null;
        }

        // ------------------------------------------------------------ native callbacks (main thread)

        [MonoPInvokeCallback(typeof(MacNative.TrayPopulateCallback))]
        private static void OnPopulate()
        {
            try
            {
                var self = s_instance;
                if (self == null) return;
                self._menuActions.Clear();
                var items = self._buildMenu?.Invoke();
                if (items != null) self.AddItems(items, -1);
            }
            catch (Exception e)
            {
                // Never let an exception cross back into native code.
                Debug.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(MacNative.TrayCommandCallback))]
        private static void OnCommand(int command)
        {
            try
            {
                var self = s_instance;
                if (self == null) return;
                if (command < 0)
                {
                    if (self._onLeftClick != null) self._pending.Enqueue(self._onLeftClick);
                }
                else if (command < self._menuActions.Count)
                {
                    self._pending.Enqueue(self._menuActions[command]);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Adds <paramref name="items"/> under <paramref name="parentId"/>. Ids are indexes into _menuActions;
        /// submenu headers get an id too (with a null action) so their children can name them as parent.</summary>
        private void AddItems(List<TrayMenuItem> items, int parentId)
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.IsSeparator)
                {
                    MacNative.DD_TrayMenuAdd(parentId, -1, null, MacNative.MenuSeparator);
                    continue;
                }

                int id = _menuActions.Count;
                bool hasChildren = item.Children != null && item.Children.Count > 0;
                _menuActions.Add(hasChildren ? null : item.OnClick);

                int flags = (item.Checked ? MacNative.MenuChecked : 0)
                            | (item.Enabled ? 0 : MacNative.MenuDisabled)
                            | (hasChildren ? MacNative.MenuHasChildren : 0);
                MacNative.DD_TrayMenuAdd(parentId, id, MacNative.Utf8(item.Label), flags);
                if (hasChildren) AddItems(item.Children, id);
            }
        }
    }
}
#endif
