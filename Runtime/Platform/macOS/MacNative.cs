#if UNITY_STANDALONE_OSX || UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopDock.Platform.MacOS
{
    /// <summary>
    /// P/Invoke declarations for <c>DesktopDockMac.bundle</c>, built from <c>Plugins~/macOS/DesktopDockMac.m</c>.
    /// Compiled in the Editor too (on every OS) so it stays compile-checked, but only called in macOS players.
    /// Rects are Cocoa global coordinates (points, origin bottom-left of the primary screen, y up); see
    /// <see cref="MacCoordinates"/>. Booleans are ints.
    /// </summary>
    internal static class MacNative
    {
        private const string Lib = "DesktopDockMac";

        [DllImport(Lib)] internal static extern int DD_Initialize();
        [DllImport(Lib)] internal static extern void DD_Shutdown();

        [DllImport(Lib)] internal static extern int DD_GetScreenCount();

        [DllImport(Lib)]
        internal static extern int DD_GetScreen(int index,
            out double x, out double y, out double width, out double height,
            out double visibleX, out double visibleY, out double visibleWidth, out double visibleHeight,
            out double scale);

        [DllImport(Lib)] internal static extern double DD_GetPrimaryScreenHeight();

        [DllImport(Lib)] internal static extern void DD_ApplyStyle(int transparent, int showInDock);
        [DllImport(Lib)] internal static extern void DD_SetFrame(double x, double y, double width, double height, int alwaysOnTop);
        [DllImport(Lib)] internal static extern int DD_GetFrame(out double x, out double y, out double width, out double height);
        [DllImport(Lib)] internal static extern int DD_GetContentRect(out double x, out double y, out double width, out double height);
        [DllImport(Lib)] internal static extern double DD_GetBackingScale();
        [DllImport(Lib)] internal static extern void DD_SetVisible(int visible);
        [DllImport(Lib)] internal static extern int DD_IsVisible();
        [DllImport(Lib)] internal static extern void DD_SendToBack();

        [DllImport(Lib)] internal static extern void DD_SetIgnoresMouseEvents(int ignore);
        [DllImport(Lib)] internal static extern void DD_GetMouseLocation(out double x, out double y);
        [DllImport(Lib)] internal static extern int DD_GetPressedMouseButtons();

        /// <summary>Takes a Quartz rect: points, origin top-left of the primary screen, y down.</summary>
        [DllImport(Lib)] internal static extern int DD_IsFullscreenAppActive(double x, double y, double width, double height);

        // ------------------------------------------------------------ status item (menu bar "tray" icon, see MacTrayIcon)

        internal const int MenuChecked = 1;
        internal const int MenuDisabled = 2;
        internal const int MenuSeparator = 4;
        internal const int MenuHasChildren = 8;

        /// <summary>Called when the menu is about to open; respond with <see cref="DD_TrayMenuAdd"/> calls.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void TrayPopulateCallback();

        /// <summary>A menu item's id, or -1 for a left click on the icon.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void TrayCommandCallback(int command);

        [DllImport(Lib)]
        internal static extern int DD_TrayCreate(byte[] tooltipUtf8, int leftClickIsCommand,
            TrayPopulateCallback populate, TrayCommandCallback command);

        [DllImport(Lib)] internal static extern void DD_TraySetTooltip(byte[] tooltipUtf8);
        [DllImport(Lib)] internal static extern void DD_TrayMenuAdd(int parentId, int itemId, byte[] labelUtf8, int flags);
        [DllImport(Lib)] internal static extern void DD_TrayRemove();

        // ------------------------------------------------------------ start at login (SMAppService, macOS 13+)

        /// <summary>0 not registered, 1 enabled, 2 requires approval, 3 not found, -1 before macOS 13.</summary>
        [DllImport(Lib)] internal static extern int DD_LoginItemStatus();

        /// <summary>1 on success, 0 on failure, -1 before macOS 13.</summary>
        [DllImport(Lib)] internal static extern int DD_LoginItemSetEnabled(int enabled);

        /// <summary>Null-terminated UTF-8, so strings marshal the same under Mono and IL2CPP.</summary>
        internal static byte[] Utf8(string s) => Encoding.UTF8.GetBytes((s ?? string.Empty) + "\0");
    }
}
#endif
