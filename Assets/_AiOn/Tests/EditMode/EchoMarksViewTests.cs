// Pure helpers of EchoMarksView (the drawing of earlier runs' marks): parsing the published value, trail pieces,
// smoothing, fades, the death label's spot, note jitter and the ghost's walk.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class EchoMarksViewTests
    {
        [Test]
        public void TryApplyIgnoresOtherKeys()
        {
            Assert.That(EchoMarksView.TryApply("power", new JValue("off")), Is.False);
            Assert.That(EchoMarksView.TryApply("haze", null), Is.False);
        }

        [Test]
        public void ParseReadsEchoMarksToJson()
        {
            var m = new EchoMarks { Level = "town" };
            m.Crosses.Add(new EchoMarks.Cross { X = 1, Z = 2, Yaw = 0.5, Age = 2, Label = "two" });
            m.Notes.Add(new EchoNote { Lv = "town", X = 3, Z = 4, Text = "hi", Yaw = 1 });
            m.Ghost = Enumerable.Range(0, 6).Select(i => ((double)i, 0.0)).ToList();
            var v = EchoMarksView.Parse(m.ToJson());
            Assert.That(v.Level, Is.EqualTo("town"));
            Assert.That(v.Crosses.Single().Age, Is.EqualTo(2));
            Assert.That(v.Crosses.Single().Label, Is.EqualTo("two"));
            Assert.That(v.Crosses.Single().Yaw, Is.EqualTo(0.5));
            Assert.That(v.Notes.Single().Text, Is.EqualTo("hi"));
            Assert.That(v.Notes.Single().X, Is.EqualTo(3));
            Assert.That(v.Ghost.Count, Is.EqualTo(6));
            Assert.That(v.Trails.Single().Age, Is.EqualTo(1));
            Assert.That(v.Empty, Is.False);
        }

        [Test]
        public void ParseDropsMalformedAndTreatsNullAsClear()
        {
            Assert.That(EchoMarksView.Parse(null), Is.Null);
            Assert.That(EchoMarksView.Parse(JValue.CreateNull()), Is.Null);
            Assert.That(EchoMarksView.Parse(new JArray()), Is.Null);
            var v = EchoMarksView.Parse(JObject.Parse(@"{
                ""level"": ""mart"",
                ""crosses"": [ { ""x"": 1 }, { ""x"": ""a"", ""z"": 1 }, { ""x"": 1, ""z"": 1, ""age"": 0 } ],
                ""notes"": [ { ""x"": 1, ""z"": 1, ""text"": ""  "" }, { ""x"": 1, ""z"": 1 }, { ""x"": 1, ""z"": 2, ""text"": ""ok"" } ],
                ""ghost"": [ [0, 0], [""x"", 1], [1] ]
            }"));
            Assert.That(v.Crosses.Count, Is.EqualTo(1));
            Assert.That(v.Crosses[0].Age, Is.EqualTo(1));
            Assert.That(v.Crosses[0].Label, Is.EqualTo(""));
            Assert.That(v.Notes.Single().Text, Is.EqualTo("ok"));
            Assert.That(v.Ghost, Is.Empty); // one good point is no line
            Assert.That(v.Trails, Is.Empty);
            Assert.That(EchoMarksView.Parse(new JObject { ["level"] = "x" }).Empty, Is.True);
        }

        [Test]
        public void ParseReadsOptionalOlderTrails()
        {
            var v = EchoMarksView.Parse(JObject.Parse(@"{ ""trails"": [ [[0,0],[1,0]], { ""age"": 3, ""points"": [[0,0],[0,1]] }, [[5,5]] ] }"));
            Assert.That(v.Trails.Select(t => t.Age), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(v.Ghost, Is.Empty);
        }

        [Test]
        public void SplitTrailDropsNearPointsAndBreaksAtJumps()
        {
            var pts = new List<(double X, double Z)> { (0, 0), (0.1, 0), (1, 0), (2, 0), (20, 0), (21, 0), (40, 0) };
            var pieces = EchoMarksView.SplitTrail(pts);
            Assert.That(pieces.Count, Is.EqualTo(2));
            Assert.That(pieces[0], Is.EqualTo(new List<(double, double)> { (0, 0), (1, 0), (2, 0) }));
            Assert.That(pieces[1], Is.EqualTo(new List<(double, double)> { (20, 0), (21, 0) }));
            Assert.That(EchoMarksView.SplitTrail(null), Is.Empty);
            Assert.That(EchoMarksView.SplitTrail(new List<(double, double)> { (0, 0), (0.1, 0.1) }), Is.Empty);
        }

        [Test]
        public void SmoothKeepsEndsAndCutsCorners()
        {
            var s = EchoMarksView.Smooth(new List<(double, double)> { (0, 0), (4, 0), (4, 4) });
            Assert.That(s.First(), Is.EqualTo((0.0, 0.0)));
            Assert.That(s.Last(), Is.EqualTo((4.0, 4.0)));
            Assert.That(s.Count, Is.EqualTo(6));
            Assert.That(s.Contains((4.0, 0.0)), Is.False); // the corner is cut
            Assert.That(EchoMarksView.Smooth(new List<(double, double)> { (0, 0), (1, 1) }).Count, Is.EqualTo(2));
        }

        [Test]
        public void OlderTrailsAreFainter()
        {
            Assert.That(EchoMarksView.TrailAlpha(1), Is.GreaterThan(EchoMarksView.TrailAlpha(2)));
            Assert.That(EchoMarksView.TrailAlpha(2), Is.GreaterThan(EchoMarksView.TrailAlpha(3)));
            Assert.That(EchoMarksView.TrailAlpha(0), Is.EqualTo(EchoMarksView.TrailAlpha(1)));
            Assert.That(EchoMarksView.TrailAlpha(1), Is.LessThan(0.4f)); // faint
        }

        [Test]
        public void DeathLabelSitsBehindTheWayYouWereGoing()
        {
            // yaw 0 faces north (-z): the near side is south (+z)
            var p = EchoMarksView.DeathLabelPos(1, 2, 0);
            Assert.That(p.X, Is.EqualTo(1).Within(1e-9));
            Assert.That(p.Z, Is.EqualTo(2.9).Within(1e-9));
            // yaw +pi/2 faces west (-x): the near side is east
            p = EchoMarksView.DeathLabelPos(0, 0, Math.PI / 2);
            Assert.That(p.X, Is.EqualTo(0.9).Within(1e-9));
            Assert.That(p.Z, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void NoteJitterIsStableAndSmall()
        {
            double a = EchoMarksView.NoteJitter(1.23, 4.56, "abc");
            Assert.That(EchoMarksView.NoteJitter(1.23, 4.56, "abc"), Is.EqualTo(a));
            var all = Enumerable.Range(0, 200).Select(i => EchoMarksView.NoteJitter(i * 0.7, -i, "n" + i)).ToList();
            Assert.That(all.All(j => j >= -0.15 && j <= 0.15), Is.True);
            Assert.That(all.Distinct().Count(), Is.GreaterThan(50));
            Assert.That(EchoMarksView.NoteJitter(0, 0, null), Is.InRange(-0.15, 0.15));
        }

        [Test]
        public void LabelsAreReadableNearAndFaintFar()
        {
            Assert.That(EchoMarksView.NearAlpha(0), Is.EqualTo(1f));
            Assert.That(EchoMarksView.NearAlpha(5), Is.EqualTo(1f));
            Assert.That(EchoMarksView.NearAlpha(30), Is.EqualTo(0.22f).Within(1e-6));
            float mid = EchoMarksView.NearAlpha(10);
            Assert.That(mid, Is.LessThan(1f).And.GreaterThan(0.22f));
            Assert.That(EchoMarksView.NearAlpha(12), Is.LessThan(mid));
        }

        [Test]
        public void GhostWalksThePathAndStartsOver()
        {
            var pts = new List<(double X, double Z)> { (0, 0), (0, -3), (0, -6), (0, -9), (0, -12) };
            // walking north at 1.5 m/s
            var s = EchoMarksView.GhostStep(0, 0, 1, pts, 1.0);
            Assert.That(s.Z, Is.EqualTo(-1.5).Within(1e-9));
            Assert.That(s.I, Is.EqualTo(1));
            Assert.That(s.Yaw.Value, Is.EqualTo(0).Within(1e-9)); // facing north
            // near the point: on to the next
            s = EchoMarksView.GhostStep(0, -2.9, 1, pts, 0.016);
            Assert.That(s.I, Is.EqualTo(2));
            Assert.That(s.Yaw, Is.Null);
            // at the end: wraps to the start, far away, so it snaps back
            s = EchoMarksView.GhostStep(0, -12, 4, pts, 0.016);
            Assert.That(s.I, Is.EqualTo(0));
            s = EchoMarksView.GhostStep(s.X, s.Z, s.I, pts, 0.016);
            Assert.That((s.X, s.Z), Is.EqualTo((0.0, 0.0)));
            Assert.That(EchoMarksView.GhostStep(1, 2, 0, new List<(double, double)>(), 1).I, Is.EqualTo(0));
        }

        [Test]
        public void TrailHeightsChainOutwardFromThePlayer()
        {
            var pieces = new List<List<(double X, double Z)>>
            {
                new() { (0, 0), (1, 0), (2, 0) },
                new() { (10, 0), (11, 0), (12, 0), (13, 0) },
                new() { (20, 0), (21, 0) },
            };
            var order = EchoMarksView.ChainOrder(pieces, 11.2, 0.5);
            Assert.That(order.Count, Is.EqualTo(9)); // every point once
            Assert.That(order.Select(o => (o.Piece, o.Index)).Distinct().Count(), Is.EqualTo(9));
            Assert.That(order[0], Is.EqualTo((1, 1, -1, -1))); // the seed: nearest the player, from the player's height
            var placed = new HashSet<(int, int)>();
            foreach (var o in order)
            {
                if (o.FromPiece >= 0) Assert.That(placed.Contains((o.FromPiece, o.FromIndex)), $"{o} chained from an unplaced point");
                placed.Add((o.Piece, o.Index));
            }
            Assert.That(order.Contains((1, 2, 1, 1)) && order.Contains((1, 0, 1, 1)), "outward both ways along the seed's piece");
            Assert.That(order.Contains((2, 0, 1, 3)), "later pieces go forward from the seed piece's end");
            Assert.That(order.Contains((0, 2, 1, 0)), "earlier pieces go backward from the seed piece's start");
            Assert.That(EchoMarksView.ChainOrder(new List<List<(double X, double Z)>>(), 0, 0), Is.Empty);
        }

        [Test]
        public void CachedMarksRedrawAfterASameLevelReload()
        {
            Assert.That(EchoMarksView.NeedsRedraw("apartment", "apartment", false, 5, 7), Is.True);  // unloaded
            Assert.That(EchoMarksView.NeedsRedraw("apartment", "apartment", true, 5, 7), Is.True);   // stale scene
            Assert.That(EchoMarksView.NeedsRedraw("apartment", "apartment", true, 7, 7), Is.False);  // still drawn
            Assert.That(EchoMarksView.NeedsRedraw("apartment", "town", false, 5, 7), Is.False);      // another level
            Assert.That(EchoMarksView.NeedsRedraw(null, "town", false, 5, 7), Is.False);             // nothing cached
        }

        [Test]
        public void MarkKeysTellMarksApart()
        {
            Assert.That(EchoMarksView.MarkKey("note", 1, 2, "a"), Is.EqualTo(EchoMarksView.MarkKey("note", 1.0001, 2, "a")));
            Assert.That(EchoMarksView.MarkKey("note", 1, 2, "a"), Is.Not.EqualTo(EchoMarksView.MarkKey("note", 1, 2, "b")));
            Assert.That(EchoMarksView.MarkKey("note", 1, 2, "a"), Is.Not.EqualTo(EchoMarksView.MarkKey("cross", 1, 2, "a")));
        }
    }
}
