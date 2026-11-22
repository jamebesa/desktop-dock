using System;
using System.IO;
using DesktopDock.Platform;
using NUnit.Framework;
using UnityEngine;

namespace DesktopDock.Tests
{
    public class DockTilingTests
    {
        // A 1920x1080 monitor with a 40 px taskbar at the bottom.
        private static readonly RectInt Work = new RectInt(0, 0, 1920, 1040);

        private static DockConfig Tiled(DockEdge edge, float start, float length) => new DockConfig
        {
            edge = edge,
            thicknessUnit = DockSizeUnit.Pixels,
            thicknessPixels = 220,
            spanStart = start,
            spanLength = length,
        };

        // ------------------------------------------------------------ geometry

        [Test]
        public void TileSpans_EqualWeights_SplitEvenly()
        {
            var spans = DockGeometry.TileSpans(new[] { 1f, 1f });
            Assert.AreEqual(new Vector2(0f, 0.5f), spans[0]);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), spans[1]);
        }

        [Test]
        public void TileSpans_FollowWeights_AndFillTheEdge()
        {
            var spans = DockGeometry.TileSpans(new[] { 1f, 0.5f, 0.5f });
            Assert.AreEqual(0.5f, spans[0].y, 1e-5f);
            Assert.AreEqual(0.25f, spans[1].y, 1e-5f);
            Assert.AreEqual(0.75f, spans[2].x, 1e-5f);
            Assert.AreEqual(1f, spans[2].x + spans[2].y, 1e-6f);
        }

        [Test]
        public void Compute_Span_PlacesTheShare()
        {
            Assert.AreEqual(new RectInt(960, 820, 960, 220), DockGeometry.Compute(Tiled(DockEdge.Bottom, 0.5f, 0.5f), Work));
            Assert.AreEqual(new RectInt(0, 520, 220, 520), DockGeometry.Compute(Tiled(DockEdge.Left, 0.5f, 0.5f), Work));
        }

        [Test]
        public void Compute_Spans_MeetWithoutGapsOrOverlap()
        {
            var spans = DockGeometry.TileSpans(new[] { 1f, 1f, 1f });
            int end = Work.x;
            foreach (var s in spans)
            {
                var r = DockGeometry.Compute(Tiled(DockEdge.Bottom, s.x, s.y), Work);
                Assert.AreEqual(end, r.x);
                end = r.xMax;
            }
            Assert.AreEqual(Work.xMax, end);
        }

        [Test]
        public void Compute_WithoutSpan_StillUsesLengthAndAlignment()
        {
            var c = new DockConfig { edge = DockEdge.Bottom, thicknessUnit = DockSizeUnit.Pixels, thicknessPixels = 220,
                lengthPercent = 0.5f, alignment = DockAlignment.End };
            Assert.AreEqual(new RectInt(960, 820, 960, 220), DockGeometry.Compute(c, Work));
        }

        [Test]
        public void TileInsertIndex_CountsOtherMiddlesBeforeThePoint()
        {
            var spans = DockGeometry.TileSpans(new[] { 1f, 1f, 1f });
            Assert.AreEqual(0, DockGeometry.TileInsertIndex(spans, 2, 0.1f));
            Assert.AreEqual(1, DockGeometry.TileInsertIndex(spans, 0, 0.55f)); // past B's middle (0.5), before C's
            Assert.AreEqual(2, DockGeometry.TileInsertIndex(spans, 0, 0.95f));
        }

        [Test]
        public void TileInsertIndex_IsStableAfterTheMove_WhateverTheSizes()
        {
            float[][] weightSets = { new[] { 1f, 1f, 0.2f }, new[] { 0.3f, 1f, 0.6f, 1f }, new[] { 1f, 0.1f } };
            foreach (var weights in weightSets)
            for (int self = 0; self < weights.Length; self++)
            for (float f = 0.01f; f < 1f; f += 0.07f)
            {
                var spans = DockGeometry.TileSpans(weights);
                int target = DockGeometry.TileInsertIndex(spans, self, f);
                // Move self to target and lay out again.
                var order = new System.Collections.Generic.List<float>(weights);
                float w = order[self];
                order.RemoveAt(self);
                order.Insert(target, w);
                var after = DockGeometry.TileSpans(order);
                Assert.AreEqual(target, DockGeometry.TileInsertIndex(after, target, f),
                    "weights " + string.Join(",", weights) + ", tile " + self + ", at " + f);
            }
        }

        [Test]
        public void FractionAlong_IsClampedAlongTheEdge()
        {
            Assert.AreEqual(0.5f, DockGeometry.FractionAlong(DockEdge.Bottom, Work, new Vector2Int(960, 1000)), 1e-5f);
            Assert.AreEqual(0.25f, DockGeometry.FractionAlong(DockEdge.Left, Work, new Vector2Int(10, 260)), 1e-5f);
            Assert.AreEqual(1f, DockGeometry.FractionAlong(DockEdge.Top, Work, new Vector2Int(5000, 0)));
        }

        // ------------------------------------------------------------ peers

        private string _dir;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "DesktopDockPeersTest-" + Guid.NewGuid().ToString("N"));
            _now = DateTime.UtcNow.Ticks;
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); }
            catch (Exception) { /* best effort */ }
        }

        private DockPeers Peer(string id, int pid) => new DockPeers(_dir, id, pid, () => _now);

        /// <summary>Lets every peer write its beat and look at the others, a few times over.</summary>
        private void Settle(float startTime, params DockPeers[] peers)
        {
            for (int round = 0; round < 3; round++)
                foreach (var p in peers) p.Tick(startTime + round * 2f);
        }

        [Test]
        public void Peers_OnTheSameRow_SplitTheEdge()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("monitor 0 Bottom", 1f);
            b.SetRow("monitor 0 Bottom", 1f);
            Settle(0f, a, b);
            Assert.AreEqual(2, a.Count);
            Assert.AreEqual(2, b.Count);
            Assert.AreNotEqual(a.Index, b.Index);
            Assert.AreEqual(0.5f, a.Span.Value.y, 1e-5f);
            Assert.AreEqual(1f, a.Span.Value.y + b.Span.Value.y, 1e-5f);
        }

        [Test]
        public void Peers_OnOtherRows_StayAlone()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("monitor 0 Bottom", 1f);
            b.SetRow("monitor 0 Top", 1f);
            Settle(0f, a, b);
            Assert.AreEqual(1, a.Count);
            Assert.IsFalse(a.Span.HasValue);
        }

        [Test]
        public void Peers_NotTiling_AreLeftOut()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("monitor 0 Bottom", 1f);
            b.SetRow(null, 1f);
            Settle(0f, a, b);
            Assert.AreEqual(1, a.Count);
        }

        [Test]
        public void Peers_Weights_FollowLength()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("row", 1f);
            b.SetRow("row", 0.5f);
            Settle(0f, a, b);
            Assert.AreEqual(2f / 3f, a.Span.Value.y, 1e-4f);
            Assert.AreEqual(1f / 3f, b.Span.Value.y, 1e-4f);
        }

        [Test]
        public void Peers_MoveTo_SwapsPlaces_AndIsRemembered()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("row", 1f);
            b.SetRow("row", 1f);
            Settle(0f, a, b);
            int aWas = a.Index;
            var mover = aWas == 0 ? b : a;
            mover.MoveTo(0);
            Settle(10f, a, b);
            Assert.AreEqual(0, mover.Index);
            Assert.AreEqual(1 - aWas, a.Index);

            // Next time the same games start, they line up the same way.
            a.Dispose();
            b.Dispose();
            var a2 = Peer("Game A", 201);
            var b2 = Peer("Game B", 202);
            a2.SetRow("row", 1f);
            b2.SetRow("row", 1f);
            Settle(0f, a2, b2);
            Assert.AreEqual(1 - aWas, a2.Index);
        }

        [Test]
        public void Peers_ThatQuit_LeaveTheRow()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("row", 1f);
            b.SetRow("row", 1f);
            Settle(0f, a, b);
            b.Dispose();
            Settle(10f, a);
            Assert.AreEqual(1, a.Count);
            Assert.IsFalse(a.Span.HasValue);
        }

        [Test]
        public void Peers_ThatStopBeating_DropOut()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            a.SetRow("row", 1f);
            b.SetRow("row", 1f);
            Settle(0f, a, b);
            Assert.AreEqual(2, a.Count);
            // b crashes: its file stays, but it stops updating it.
            _now += TimeSpan.FromSeconds(10).Ticks;
            Settle(10f, a);
            Assert.AreEqual(1, a.Count);
        }

        [Test]
        public void Peers_Messages_ReachTheOthers_NotTheSender()
        {
            var a = Peer("Game A", 101);
            var b = Peer("Game B", 102);
            int aGot = -1, bGot = -1;
            a.MessageReceived += m => aGot = m;
            b.MessageReceived += m => bGot = m;
            a.Send((int)DockShowHide.Hide);
            Settle(0f, a, b);
            Assert.AreEqual((int)DockShowHide.Hide, bGot);
            Assert.AreEqual(-1, aGot);
        }

        [Test]
        public void Peers_IgnoreMessagesFromBeforeTheyStarted()
        {
            var a = Peer("Game A", 101);
            a.Send((int)DockShowHide.Hide);
            var b = Peer("Game B", 102);
            int bGot = -1;
            b.MessageReceived += m => bGot = m;
            Settle(0f, b);
            Assert.AreEqual(-1, bGot);
        }
    }
}
