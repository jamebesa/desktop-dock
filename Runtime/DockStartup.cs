using System;
using System.Text;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using DesktopDock.Platform.Windows;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
using DesktopDock.Platform.MacOS;
#endif

namespace DesktopDock
{
    /// <summary>
    /// Start the game when the player signs in to Windows. Uses the per-user Run key
    /// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>), so it needs no admin rights and shows up in
    /// Task Manager's Startup apps list, where the player can also turn it off.
    /// On macOS (untested) it registers the app as a login item with <c>SMAppService</c> instead: macOS 13 or newer,
    /// shown in System Settings > General > Login Items, and the app must be a signed .app bundle.
    /// Only works in Windows and macOS builds: in the Editor <see cref="IsSupported"/> is false and nothing is written.
    /// Make it opt-in: ask the player before turning it on.
    /// </summary>
    public static class DockStartup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private static string s_entryName;

        /// <summary>Name of the startup entry. Defaults to the product name from Player Settings.</summary>
        public static string EntryName
        {
            get => string.IsNullOrEmpty(s_entryName) ? Application.productName : s_entryName;
            set => s_entryName = value;
        }

        /// <summary>What to call the setting in menus: "Start with Windows", or "Open at Login" on macOS.</summary>
        public static string DisplayName =>
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            "Open at Login";
#else
            "Start with Windows";
#endif

        /// <summary>True in Windows builds, and in macOS 13+ builds that have the native plugin and run as a signed
        /// .app bundle (an unsigned or loose build reports "not found" and can't register).</summary>
        public static bool IsSupported =>
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            true;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            IsMacStatusSupported(MacLoginItemStatus());
#else
            false;
#endif

        /// <summary>
        /// Whether Windows will start this executable at sign-in: the entry exists, points at this exe, and wasn't
        /// turned off in Task Manager. On macOS: registered as a login item, including while it waits for the player's
        /// approval in System Settings, so a toggle bound to this can always turn it back off.
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                string exe = Win32.ExecutablePath();
                if (exe == null) return false;
                byte[] command = ReadValue(RunKey, EntryName);
                if (command == null || !CommandTargets(DecodeString(command), exe)) return false;
                return !IsDisabledInTaskManager(ReadValue(ApprovedKey, EntryName));
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
                return IsMacStatusEnabled(MacLoginItemStatus());
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Turn starting with Windows on or off. Turning it on points the entry at the running executable, so call it
        /// again if the game moves, and clears a "disabled" mark left by Task Manager. Returns false on failure.
        /// </summary>
        /// <param name="arguments">Optional command-line arguments, for example a flag that starts the game minimized.</param>
        public static bool SetEnabled(bool enabled, string arguments = null)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            string name = EntryName;
            if (!enabled)
            {
                DeleteValue(ApprovedKey, name);
                return DeleteValue(RunKey, name);
            }

            string exe = Win32.ExecutablePath();
            if (exe == null) return false;
            if (!WriteString(RunKey, name, BuildCommand(exe, arguments))) return false;
            DeleteValue(ApprovedKey, name);
            return true;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(arguments))
                Debug.Log("[DesktopDock] Login items can't pass command-line arguments on macOS; ignoring them.");
            try
            {
                if (MacNative.DD_LoginItemSetEnabled(enabled ? 1 : 0) != 1)
                {
                    int status = MacLoginItemStatus();
                    Debug.LogWarning("[DesktopDock] Could not " + (enabled ? "turn on" : "turn off") + " Open at Login" +
                                     (status == MacStatusNotFound
                                         ? ": macOS can't find the app. It must run as a signed .app bundle."
                                         : " (status " + status + ")."));
                    return false;
                }
                if (enabled && MacLoginItemStatus() == MacStatusRequiresApproval)
                    Debug.Log("[DesktopDock] Open at Login needs approval in System Settings > General > Login Items.");
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return false;
            }
#else
            Debug.Log("[DesktopDock] Starting at sign-in only works in Windows and macOS builds.");
            return false;
#endif
        }

        // ------------------------------------------------------------ helpers (pure ones are unit-tested)

        // SMAppService.Status values; -1 = before macOS 13 or no native plugin.
        internal const int MacStatusNotRegistered = 0;
        internal const int MacStatusEnabled = 1;
        internal const int MacStatusRequiresApproval = 2;
        internal const int MacStatusNotFound = 3;

        /// <summary>Whether registering can work. "Not found" (unsigned or loose builds) and pre-13 can't.</summary>
        internal static bool IsMacStatusSupported(int status) =>
            status == MacStatusNotRegistered || status == MacStatusEnabled || status == MacStatusRequiresApproval;

        /// <summary>Registered, whether or not the player has approved it yet.</summary>
        internal static bool IsMacStatusEnabled(int status) =>
            status == MacStatusEnabled || status == MacStatusRequiresApproval;

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        /// <summary>SMAppService status, or -1 before macOS 13 or without the native plugin.</summary>
        private static int MacLoginItemStatus()
        {
            try { return MacNative.DD_LoginItemStatus(); }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return -1; }
        }
#endif

        internal static string BuildCommand(string exePath, string arguments) =>
            "\"" + exePath + "\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);

        /// <summary>Whether a Run-key command line starts <paramref name="exePath"/>.</summary>
        internal static bool CommandTargets(string command, string exePath)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(exePath)) return false;
            command = command.Trim();
            string target;
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                target = end > 0 ? command.Substring(1, end - 1) : command.Substring(1);
            }
            else
            {
                int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                target = exeEnd > 0 ? command.Substring(0, exeEnd + 4) : command;
            }
            return string.Equals(Normalize(target), Normalize(exePath), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Task Manager stores a binary value whose first byte is odd when the player disabled the entry.</summary>
        internal static bool IsDisabledInTaskManager(byte[] approvedValue) =>
            approvedValue != null && approvedValue.Length > 0 && (approvedValue[0] & 1) != 0;

        private static string Normalize(string path) => path.Trim().Replace('/', '\\');

        private static string DecodeString(byte[] data)
        {
            string s = Encoding.Unicode.GetString(data);
            int nul = s.IndexOf('\0');
            return nul >= 0 ? s.Substring(0, nul) : s;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static byte[] ReadValue(string subKey, string name)
        {
            if (Win32.RegOpenKeyEx(Win32.HKEY_CURRENT_USER, subKey, 0, Win32.KEY_QUERY_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
                return null;
            try
            {
                uint size = 0;
                if (Win32.RegQueryValueEx(key, name, IntPtr.Zero, out _, null, ref size) != Win32.ERROR_SUCCESS || size == 0)
                    return null;
                var data = new byte[size];
                return Win32.RegQueryValueEx(key, name, IntPtr.Zero, out _, data, ref size) == Win32.ERROR_SUCCESS ? data : null;
            }
            finally
            {
                Win32.RegCloseKey(key);
            }
        }

        private static bool WriteString(string subKey, string name, string value)
        {
            if (Win32.RegOpenKeyEx(Win32.HKEY_CURRENT_USER, subKey, 0, Win32.KEY_SET_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
                return false;
            try
            {
                byte[] data = Encoding.Unicode.GetBytes(value + "\0");
                return Win32.RegSetValueEx(key, name, 0, Win32.REG_SZ, data, (uint)data.Length) == Win32.ERROR_SUCCESS;
            }
            finally
            {
                Win32.RegCloseKey(key);
            }
        }

        private static bool DeleteValue(string subKey, string name)
        {
            if (Win32.RegOpenKeyEx(Win32.HKEY_CURRENT_USER, subKey, 0, Win32.KEY_SET_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
                return true; // no key, so no value
            try
            {
                int result = Win32.RegDeleteValue(key, name);
                return result == Win32.ERROR_SUCCESS || result == 2; // 2 = ERROR_FILE_NOT_FOUND: already gone
            }
            finally
            {
                Win32.RegCloseKey(key);
            }
        }
#endif
    }
}
