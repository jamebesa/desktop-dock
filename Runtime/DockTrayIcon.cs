using System;
using System.Collections.Generic;
using DesktopDock.Platform;
using UnityEngine;

namespace DesktopDock
{
    /// <summary>
    /// Adds an icon for the game to the Windows notification area (system tray), or to the menu bar on macOS
    /// (untested). Left-click shows or hides the dock; right-click opens a menu with show/hide, dock edge, always on
    /// top, start with Windows / open at login, your own items, and Quit. Useful with
    /// <see cref="DockConfig.showInTaskbar"/> off, since the tray is then the only way to get a hidden dock back.
    /// Only does anything in Windows and macOS builds. Uses the game's icon from Player Settings.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Desktop Dock/Dock Tray Icon")]
    public sealed class DockTrayIcon : MonoBehaviour
    {
        [Tooltip("Text shown when hovering the icon. Empty = the product name.")]
        public string tooltip = "";

        [Tooltip("Left-click the icon to show or hide the dock.")]
        public bool leftClickTogglesDock = true;

        [Header("Built-in menu items")]
        public bool showEdgeItems = true;
        public bool showAlwaysOnTopItem = true;
        [Tooltip("\"Start with Windows\" (\"Open at Login\" on macOS). Only appears in builds, where it is supported.")]
        public bool showStartWithWindowsItem = true;
        public bool showQuitItem = true;

        private sealed class CustomItem
        {
            public string Label;
            public Action OnClick;
            public Func<bool> IsChecked;
        }

        private readonly List<CustomItem> _customItems = new List<CustomItem>();

        private ITrayIcon _tray;

        /// <summary>Whether the icon is currently in the tray (menu bar on macOS).</summary>
        public bool IsShown => _tray != null;

        /// <summary>
        /// Add your own menu item, shown between the built-in settings and Quit. <paramref name="isChecked"/>, if given,
        /// is asked each time the menu opens.
        /// </summary>
        public void AddMenuItem(string label, Action onClick, Func<bool> isChecked = null)
        {
            if (string.IsNullOrEmpty(label) || onClick == null) return;
            _customItems.Add(new CustomItem { Label = label, OnClick = onClick, IsChecked = isChecked });
        }

        public bool RemoveMenuItem(string label) => _customItems.RemoveAll(i => i.Label == label) > 0;

        private void OnEnable()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _tray = Platform.Windows.WindowsTrayIcon.Create(TooltipText(), BuildMenu, OnLeftClick);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            // Without a left-click action, any click opens the menu, as macOS users expect.
            _tray = Platform.MacOS.MacTrayIcon.Create(TooltipText(), BuildMenu, leftClickTogglesDock ? OnLeftClick : (Action)null);
#endif
        }

        private void OnDisable()
        {
            _tray?.Dispose();
            _tray = null;
        }

        private void Update() => _tray?.ProcessPending();

        private void OnValidate() => _tray?.SetTooltip(TooltipText());

        private string TooltipText() => string.IsNullOrEmpty(tooltip) ? Application.productName : tooltip;

        private void OnLeftClick()
        {
            if (!leftClickTogglesDock) return;
            var dock = DesktopDockController.Instance;
            if (dock != null) dock.SetHidden(!dock.IsHidden);
        }

        internal List<TrayMenuItem> BuildMenu()
        {
            var items = new List<TrayMenuItem>();
            var dock = DesktopDockController.Instance;

            if (dock != null)
            {
                items.Add(new TrayMenuItem
                {
                    Label = dock.IsHidden ? "Show" : "Hide",
                    OnClick = () => dock.SetHidden(!dock.IsHidden),
                    IsDefault = leftClickTogglesDock,
                });
                items.Add(TrayMenuItem.Separator());

                var cfg = dock.Config;
                if (showEdgeItems)
                {
                    var edges = new List<TrayMenuItem>();
                    foreach (DockEdge edge in Enum.GetValues(typeof(DockEdge)))
                    {
                        var e = edge;
                        edges.Add(new TrayMenuItem { Label = e.ToString(), Checked = cfg.edge == e, OnClick = () => dock.SetEdge(e) });
                    }
                    items.Add(new TrayMenuItem { Label = "Dock to", Children = edges });
                }
                if (showAlwaysOnTopItem)
                {
                    items.Add(new TrayMenuItem
                    {
                        Label = "Always on top",
                        Checked = cfg.alwaysOnTop,
                        OnClick = () => dock.SetAlwaysOnTop(!dock.Config.alwaysOnTop),
                    });
                }
            }

            if (showStartWithWindowsItem && DockStartup.IsSupported)
            {
                bool on = DockStartup.IsEnabled;
                items.Add(new TrayMenuItem { Label = DockStartup.DisplayName, Checked = on, OnClick = () => DockStartup.SetEnabled(!on) });
            }

            if (_customItems.Count > 0)
            {
                items.Add(TrayMenuItem.Separator());
                foreach (var c in _customItems)
                {
                    bool isChecked = false;
                    try { isChecked = c.IsChecked != null && c.IsChecked(); }
                    catch (Exception e) { Debug.LogException(e); }
                    items.Add(new TrayMenuItem { Label = c.Label, OnClick = c.OnClick, Checked = isChecked });
                }
            }

            if (showQuitItem)
            {
                items.Add(TrayMenuItem.Separator());
                items.Add(new TrayMenuItem { Label = "Quit", OnClick = Application.Quit });
            }

            // Drop separators that ended up first, last or doubled.
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i].IsSeparator && (i == 0 || i == items.Count - 1 || items[i - 1].IsSeparator))
                    items.RemoveAt(i);
            return items;
        }
    }
}
