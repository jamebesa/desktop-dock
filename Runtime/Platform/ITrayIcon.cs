using System;

namespace DesktopDock.Platform
{
    /// <summary>A platform's tray / menu bar icon, as used by <see cref="DockTrayIcon"/>.</summary>
    internal interface ITrayIcon : IDisposable
    {
        void SetTooltip(string tooltip);

        /// <summary>Run commands picked from the menu or the icon. Call once per frame from the main thread.</summary>
        void ProcessPending();
    }
}
