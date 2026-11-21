using UnityEngine;
using UnityEngine.Events;

namespace DesktopDock
{
    /// <summary>What a <see cref="DockHotkey"/> does besides raising <see cref="DockHotkey.onPressed"/>.</summary>
    public enum DockHotkeyAction
    {
        /// <summary>Only raise the event.</summary>
        None = 0,
        /// <summary>
        /// Show a hidden dock; bring a dock tucked away by auto-hide back for a few seconds; otherwise hide the dock.
        /// </summary>
        ToggleShowHide = 1,
    }

    /// <summary>
    /// A system-wide keyboard shortcut that works while another app has focus, for example Ctrl+Alt+D to show or hide the
    /// dock. Add one component per shortcut. Windows builds only: elsewhere <see cref="IsRegistered"/> stays false. If
    /// another app already uses the combination, Windows refuses it and a warning is logged; offer players a way to
    /// pick another one with <see cref="SetShortcut"/>.
    /// </summary>
    [AddComponentMenu("Desktop Dock/Dock Hotkey")]
    public sealed class DockHotkey : MonoBehaviour
    {
        [Tooltip("Key combination, such as Ctrl+Alt+D or Ctrl+Shift+F12. Needs Ctrl, Alt, Shift or Win, except for F13–F24.")]
        [SerializeField] private string shortcut = "Ctrl+Alt+D";

        [Tooltip("What pressing the shortcut does.")]
        public DockHotkeyAction action = DockHotkeyAction.ToggleShowHide;

        [Tooltip("Raised on every press, after the action.")]
        public UnityEvent onPressed = new UnityEvent();

        private int _id;

        public string Shortcut => shortcut;

        /// <summary>Whether Windows accepted the shortcut and it's active now.</summary>
        public bool IsRegistered => _id != 0;

        /// <summary>
        /// Change the shortcut. Returns false, keeping the old one, if the text can't be parsed or Windows refuses it.
        /// </summary>
        public bool SetShortcut(string value)
        {
            if (!DockShortcut.TryParse(value, out _, out _, out string error))
            {
                Debug.LogWarning("[DesktopDock] " + error);
                return false;
            }
            string previous = shortcut;
            shortcut = value;
            if (!isActiveAndEnabled) return true;
            Unregister();
            if (Register()) return true;
            shortcut = previous;
            Register();
            return false;
        }

        private void OnEnable() => Register();
        private void OnDisable() => Unregister();

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Platform.Windows.WindowsHotkeys.DispatchPressed();
#endif
        }

        private bool Register()
        {
            if (!DockShortcut.TryParse(shortcut, out uint modifiers, out uint vk, out string error))
            {
                Debug.LogWarning("[DesktopDock] Hotkey not set: " + error);
                return false;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _id = Platform.Windows.WindowsHotkeys.Register(modifiers, vk, OnPressed);
            if (_id == 0)
                Debug.LogWarning("[DesktopDock] Windows refused the hotkey " + shortcut + "; another app probably uses it.");
            return _id != 0;
#else
            // Hotkeys are system-wide, so the Editor never takes them; other platforms have no implementation yet.
            return false;
#endif
        }

        private void Unregister()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Platform.Windows.WindowsHotkeys.Unregister(_id);
#endif
            _id = 0;
        }

        private void OnPressed()
        {
            if (action == DockHotkeyAction.ToggleShowHide) ToggleShowHide();
            onPressed?.Invoke();
        }

        internal static void ToggleShowHide()
        {
            var dock = DesktopDockController.Instance;
            if (dock == null) return;
            if (dock.IsHidden) dock.SetHidden(false);
            else if (dock.IsTuckedAway) dock.RevealAutoHide();
            else dock.SetHidden(true);
        }
    }
}
