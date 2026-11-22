using System;
using System.Collections.Generic;
using System.IO;
using DesktopDock.Platform;
using UnityEngine;

namespace DesktopDock
{
    /// <summary>
    /// Finds the other Desktop Dock windows running on this computer, from any game, through small files in a shared
    /// folder. Docks on the same edge of the same monitor tile side by side, and one hotkey can show or hide them all.
    /// Each dock rewrites its heartbeat file every second; one that stops (closed or crashed) drops out after a few
    /// seconds. A row's order is kept by game, so the same games line up the same way next time. Every file operation
    /// is best-effort: a dock that can't reach the folder simply docks alone.
    /// </summary>
    internal sealed class DockPeers : IDisposable
    {
        [Serializable]
        private sealed class Beat
        {
            public int pid;
            public string id;
            public string row;
            public float weight;
            public long started;
            public long time;
        }

        [Serializable]
        private sealed class RowOrder
        {
            public string row;
            public List<string> ids = new List<string>();
        }

        [Serializable]
        private sealed class Orders
        {
            public List<RowOrder> rows = new List<RowOrder>();
        }

        [Serializable]
        private sealed class Message
        {
            public long seq;
            public int from;
            public int action;
        }

        private const float PollSeconds = 0.5f;
        private const float BeatSeconds = 1f;
        private static readonly long StaleTicks = TimeSpan.FromSeconds(4).Ticks;
        private static readonly long DeleteTicks = TimeSpan.FromMinutes(1).Ticks;

        private readonly string _dir;
        private readonly Beat _me;
        private readonly Func<long> _clock;
        private float _nextPoll, _nextBeat;
        private long _lastMessage;
        private readonly List<Beat> _members = new List<Beat>();
        private readonly Dictionary<string, Beat> _lastGood = new Dictionary<string, Beat>();
        private bool _disposed;

        /// <summary>The row this dock is in (see <see cref="SetRow"/>), or null.</summary>
        public string Row => _me.row;

        /// <summary>This dock's share of the edge, (start, length) as fractions, while it shares the edge with others.</summary>
        public Vector2? Span { get; private set; }

        /// <summary>The docks in this dock's row, this one included (1 when alone or not tiling).</summary>
        public int Count => Mathf.Max(1, _members.Count);

        /// <summary>This dock's place in its row, from the start of the edge.</summary>
        public int Index { get; private set; }

        /// <summary>Spans of every dock in the row, in order. Empty when alone.</summary>
        public IReadOnlyList<Vector2> Spans { get; private set; } = Array.Empty<Vector2>();

        /// <summary>A message another dock sent with <see cref="Send"/> (for example, the shared show/hide hotkey).</summary>
        public event Action<int> MessageReceived;

        /// <summary>The shared folder, under the user's local application data.</summary>
        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDock", "Peers");

        public DockPeers(string folder, string id, int pid, Func<long> clock = null)
        {
            _dir = folder;
            _clock = clock ?? (() => DateTime.UtcNow.Ticks);
            _me = new Beat { pid = pid, id = id, weight = 1f, started = _clock() };
            try
            {
                Directory.CreateDirectory(_dir);
                // Messages sent before this dock started aren't for it.
                _lastMessage = ReadJson<Message>(MessagePath)?.seq ?? 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DesktopDock] Can't use the folder for tiling with other docks (" + _dir + "): " + e.Message);
            }
        }

        private string BeatPath(int pid) => Path.Combine(_dir, pid + ".beat");
        private string OrdersPath => Path.Combine(_dir, "order.json");
        private string MessagePath => Path.Combine(_dir, "message.json");

        /// <summary>
        /// Which row this dock is in, such as "the bottom edge of the monitor at 0,0 3840×2160", or null when it isn't
        /// tiling. <paramref name="weight"/> is its Length setting: its share of the edge relative to the others.
        /// </summary>
        public void SetRow(string row, float weight)
        {
            if (row == _me.row && Mathf.Approximately(weight, _me.weight)) return;
            _me.row = row;
            _me.weight = weight;
            // Tell the others and look again right away.
            _nextBeat = _nextPoll = 0f;
        }

        /// <summary>Call every frame. Returns true when this dock's share of the edge changed and it should re-dock.</summary>
        public bool Tick(float now)
        {
            if (now >= _nextBeat)
            {
                _nextBeat = now + BeatSeconds;
                WriteBeat();
            }
            if (now < _nextPoll) return false;
            _nextPoll = now + PollSeconds;
            ReadMessages();
            return Refresh();
        }

        /// <summary>Moves this dock to place <paramref name="index"/> in its row (0 = the start of the edge).</summary>
        public void MoveTo(int index)
        {
            if (_me.row == null || _members.Count < 2) return;
            var orders = ReadJson<Orders>(OrdersPath) ?? new Orders();
            var order = RowFor(orders, _me.row);
            // Start from the order as it is on screen, so docks the list doesn't know yet keep their places.
            order.ids.Clear();
            foreach (var m in _members) if (!order.ids.Contains(m.id)) order.ids.Add(m.id);
            order.ids.Remove(_me.id);
            order.ids.Insert(Mathf.Clamp(index, 0, order.ids.Count), _me.id);
            WriteJson(OrdersPath, orders);
            _nextPoll = 0f;
        }

        /// <summary>Sends <paramref name="action"/> to every other running dock, which raises <see cref="MessageReceived"/>.</summary>
        public void Send(int action)
        {
            long seq = Math.Max(_clock(), _lastMessage + 1);
            _lastMessage = seq;
            WriteJson(MessagePath, new Message { seq = seq, from = _me.pid, action = action });
        }

        public void Dispose()
        {
            _disposed = true;
            try { File.Delete(BeatPath(_me.pid)); }
            catch (Exception) { /* best effort */ }
        }

        // ------------------------------------------------------------ internals

        private void WriteBeat()
        {
            if (_disposed) return;
            try
            {
                if (_me.row == null)
                {
                    File.Delete(BeatPath(_me.pid));
                    return;
                }
                _me.time = _clock();
                // Swapped in whole, so a dock reading it never sees half a file.
                WriteJson(BeatPath(_me.pid), _me);
            }
            catch (Exception) { /* best effort: try again next beat */ }
        }

        private void ReadMessages()
        {
            var m = ReadJson<Message>(MessagePath);
            if (m == null || m.seq <= _lastMessage) return;
            _lastMessage = m.seq;
            if (m.from != _me.pid) MessageReceived?.Invoke(m.action);
        }

        /// <summary>Re-reads the row and recomputes this dock's share. True if the share changed.</summary>
        private bool Refresh()
        {
            var before = Span;
            _members.Clear();
            if (_me.row != null)
            {
                long now = _clock();
                string[] files;
                try { files = Directory.GetFiles(_dir, "*.beat"); }
                catch (Exception) { files = Array.Empty<string>(); }
                var seen = new HashSet<string>();
                foreach (var file in files)
                {
                    seen.Add(file);
                    // A read that fails (the file is being swapped right now) uses that dock's last good beat, so
                    // the row doesn't flicker.
                    var beat = ReadJson<Beat>(file);
                    if (beat != null) _lastGood[file] = beat;
                    else _lastGood.TryGetValue(file, out beat);
                    if (beat == null || beat.pid == _me.pid) continue;
                    if (now - beat.time > DeleteTicks) TryDelete(file);
                    if (now - beat.time > StaleTicks || beat.row != _me.row) continue;
                    _members.Add(beat);
                }
                // Forget docks whose files are gone.
                var gone = new List<string>();
                foreach (var key in _lastGood.Keys) if (!seen.Contains(key)) gone.Add(key);
                foreach (var key in gone) _lastGood.Remove(key);
                _members.Add(_me);
            }

            if (_members.Count < 2)
            {
                _members.Clear();
                Span = null;
                Spans = Array.Empty<Vector2>();
                Index = 0;
                return before.HasValue;
            }

            // Order: the saved order for this row, then newcomers by when they started.
            var orders = ReadJson<Orders>(OrdersPath) ?? new Orders();
            var order = RowFor(orders, _me.row);
            _members.Sort((a, b) =>
            {
                int ia = order.ids.IndexOf(a.id), ib = order.ids.IndexOf(b.id);
                if (ia < 0) ia = int.MaxValue;
                if (ib < 0) ib = int.MaxValue;
                if (ia != ib) return ia.CompareTo(ib);
                if (a.started != b.started) return a.started.CompareTo(b.started);
                return a.pid.CompareTo(b.pid);
            });
            // Remember newcomers' places, so the row comes back the same next time.
            bool added = false;
            foreach (var m in _members)
                if (!order.ids.Contains(m.id)) { order.ids.Add(m.id); added = true; }
            if (added) WriteJson(OrdersPath, orders);

            var weights = new float[_members.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = _members[i].weight;
            var spans = DockGeometry.TileSpans(weights);
            Spans = spans;
            Index = _members.IndexOf(_me);
            Span = spans[Index];
            return !before.HasValue || (before.Value - Span.Value).sqrMagnitude > 1e-8f;
        }

        private static RowOrder RowFor(Orders orders, string row)
        {
            foreach (var r in orders.rows) if (r.row == row) return r;
            var added = new RowOrder { row = row };
            orders.rows.Add(added);
            return added;
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null; // half-written by another dock: read it again next time
            }
        }

        private static void WriteJson<T>(string path, T value)
        {
            try
            {
                string tmp = path + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(value));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception) { /* best effort */ }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception) { /* another dock got there first */ }
        }
    }
}
