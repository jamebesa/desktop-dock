using System;
using System.Collections.Generic;

namespace DesktopDock.Platform
{
    /// <summary>One entry in the tray icon's menu, rebuilt each time the menu opens.</summary>
    internal sealed class TrayMenuItem
    {
        public string Label;
        public Action OnClick;
        public bool Checked;
        public bool Enabled = true;
        /// <summary>Shown in bold; what a left click would do.</summary>
        public bool IsDefault;
        public bool IsSeparator;
        public List<TrayMenuItem> Children;

        public static TrayMenuItem Separator() => new TrayMenuItem { IsSeparator = true };
    }
}
