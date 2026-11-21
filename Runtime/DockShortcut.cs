using System;
using System.Collections.Generic;

namespace DesktopDock
{
    /// <summary>
    /// Parses key combinations such as "Ctrl+Alt+D" or "Shift+Win+F12" into Windows hotkey modifiers and a virtual-key
    /// code. Modifiers: Ctrl (or Control), Alt, Shift, Win. Keys: A–Z, 0–9, F1–F24, NumPad0–NumPad9, Space, Enter, Tab,
    /// Esc, Backspace, Insert, Delete, Home, End, PageUp, PageDown, Left, Right, Up, Down, Pause, PrintScreen, and the
    /// punctuation keys ` - = [ ] \ ; ' , . /. Case and spaces don't matter.
    /// </summary>
    public static class DockShortcut
    {
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        private static readonly Dictionary<string, uint> s_named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "Space", 0x20 }, { "Enter", 0x0D }, { "Return", 0x0D }, { "Tab", 0x09 }, { "Esc", 0x1B }, { "Escape", 0x1B },
            { "Backspace", 0x08 }, { "Insert", 0x2D }, { "Ins", 0x2D }, { "Delete", 0x2E }, { "Del", 0x2E },
            { "Home", 0x24 }, { "End", 0x23 }, { "PageUp", 0x21 }, { "PgUp", 0x21 }, { "PageDown", 0x22 }, { "PgDn", 0x22 },
            { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 }, { "Pause", 0x13 },
            { "PrintScreen", 0x2C }, { "PrtSc", 0x2C },
            { "`", 0xC0 }, { "Backquote", 0xC0 }, { "-", 0xBD }, { "Minus", 0xBD }, { "=", 0xBB }, { "Equals", 0xBB },
            { "[", 0xDB }, { "]", 0xDD }, { "\\", 0xDC }, { ";", 0xBA }, { "'", 0xDE }, { ",", 0xBC }, { "Comma", 0xBC },
            { ".", 0xBE }, { "Period", 0xBE }, { "/", 0xBF }, { "Slash", 0xBF },
        };

        /// <summary>
        /// Parses <paramref name="text"/>. Needs exactly one key and, except for F13–F24 (which no app uses on its own),
        /// at least one modifier, so a plain letter can't take over typing everywhere.
        /// </summary>
        public static bool TryParse(string text, out uint modifiers, out uint virtualKey, out string error)
        {
            modifiers = 0;
            virtualKey = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The shortcut is empty.";
                return false;
            }

            // '+' separates the parts, so the '+' key itself can't be named; use "=" (the same key) instead.
            string[] parts = text.Split('+');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0)
                {
                    error = "\"" + text + "\" has an empty part.";
                    return false;
                }
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= ModControl; continue;
                    case "alt": modifiers |= ModAlt; continue;
                    case "shift": modifiers |= ModShift; continue;
                    case "win":
                    case "windows":
                    case "super": modifiers |= ModWin; continue;
                }

                if (virtualKey != 0)
                {
                    error = "\"" + text + "\" has more than one key.";
                    return false;
                }
                if (!TryKey(part, out virtualKey))
                {
                    error = "\"" + part + "\" isn't a key this shortcut format knows.";
                    return false;
                }
            }

            if (virtualKey == 0)
            {
                error = "\"" + text + "\" has no key, only modifiers.";
                return false;
            }
            bool highFunctionKey = virtualKey >= 0x7C && virtualKey <= 0x87; // F13–F24
            if (modifiers == 0 && !highFunctionKey)
            {
                error = "\"" + text + "\" needs Ctrl, Alt, Shift or Win, or else it would take over that key everywhere.";
                virtualKey = 0;
                return false;
            }
            return true;
        }

        private static bool TryKey(string key, out uint vk)
        {
            vk = 0;
            if (key.Length == 1)
            {
                char ch = char.ToUpperInvariant(key[0]);
                if (ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9')
                {
                    vk = ch;
                    return true;
                }
            }
            if (s_named.TryGetValue(key, out vk)) return true;

            if ((key[0] == 'F' || key[0] == 'f') && int.TryParse(key.Substring(1), out int f) && f >= 1 && f <= 24)
            {
                vk = (uint)(0x70 + f - 1);
                return true;
            }
            if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsDigit(key[6]))
            {
                vk = (uint)(0x60 + (key[6] - '0'));
                return true;
            }
            return false;
        }
    }
}
