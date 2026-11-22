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

    /// <summary>What the show/hide shortcut does to a dock, and what it asks the other docks to do.</summary>
    public enum DockShowHide
    {
        Show = 0,
        /// <summary>Bring a dock tucked away by auto-hide back for a few seconds (show it if it's hidden).</summary>
        Reveal = 1,
        Hide = 2,
    }

    /// <summary>
    /// A system-wide keyboard shortcut that works while another app has focus, for example Ctrl+Alt+D to show or hide the
    /// dock. Add one component per shortcut. Windows builds only: elsewhere <see cref="IsRegistered"/> stays false. If
    /// another app already uses the combination, Windows refuses it and a warning is logged; offer players a way to
    /// pick another one with <see cref="SetShortcut"/>.
    /// <para>
    /// With several Desktop Dock games running, only the first to start gets a given shortcut. With
    /// <see cref="affectAllDocks"/> on (the default) its show/hide applies to every running dock, so one shortcut works
    /// for all of them, and the others keep trying so one takes over if that game quits.
    /// </para>
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

        [Tooltip("Show/hide applies to every running Desktop Dock window, from any game, not just this one.")]
        public bool affectAllDocks = true;

        private const float RetrySeconds = 3f;

        private int _id;
        private bool _warned;
        private float _nextRetry;

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
            // Another game may hold the shortcut; take it over when that game quits.
            if (_id == 0 && Time.unscaledTime >= _nextRetry)
            {
                _nextRetry = Time.unscaledTime + RetrySeconds;
                Register();
            }
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
            if (_id == 0 && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[DesktopDock] Windows refused the hotkey " + shortcut + "; another app (perhaps another " +
                                 "dock game, whose shortcut then works for this dock too) probably uses it. Trying again " +
                                 "every few seconds.");
            }
            if (_id != 0) _warned = false;
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
            if (action == DockHotkeyAction.ToggleShowHide)
            {
                var done = ToggleShowHide();
                if (affectAllDocks && done.HasValue) DesktopDockController.Instance.SendToOtherDocks(done.Value);
            }
            onPressed?.Invoke();
        }

        /// <summary>Shows a hidden dock, reveals a tucked-away one, or else hides it. Returns what it did.</summary>
        internal static DockShowHide? ToggleShowHide()
        {
            var dock = DesktopDockController.Instance;
            if (dock == null) return null;
            var what = dock.IsHidden ? DockShowHide.Show : dock.IsTuckedAway ? DockShowHide.Reveal : DockShowHide.Hide;
            Apply(what);
            return what;
        }

        /// <summary>Shows, reveals or hides this game's dock.</summary>
        internal static void Apply(DockShowHide what)
        {
            var dock = DesktopDockController.Instance;
            if (dock == null) return;
            switch (what)
            {
                case DockShowHide.Show:
                    dock.SetHidden(false);
                    break;
                case DockShowHide.Reveal:
                    if (dock.IsHidden) dock.SetHidden(false);
                    else dock.RevealAutoHide();
                    break;
                default:
                    dock.SetHidden(true);
                    break;
            }
        }
    }
}
