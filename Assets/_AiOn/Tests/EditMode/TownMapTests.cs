using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>Helpers shared by the town map and route parity tests (twins of the golden generators' helpers).</summary>
    static class TownGolden
    {
        /// <summary>FNV-1a over UTF-16 code units, as 8 hex digits (Tools/golden/make-golden-*.mjs fnv()).</summary>
        public static string Fnv(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char c in s) { h ^= c; h = unchecked(h * 0x01000193u); }
            return h.ToString("x8");
        }

        /// <summary>The 64 bits of a double as hex: an exact comparison that JSON number text cannot blur.</summary>
        public static string Bits(double x) => BitConverter.DoubleToInt64Bits(x).ToString("x16");

        public static List<string> Strings(JToken list) => list == null || list.Type == JTokenType.Null ? null : list.Select(t => (string)t).ToList();

        /// <summary>A fixture's gates object for TownMap.Graph: null stays null (all open), booleans as they are.</summary>
        public static Dictionary<string, bool> BoolGates(JToken gates) =>
            gates == null || gates.Type == JTokenType.Null ? null : ((JObject)gates).Properties().ToDictionary(p => p.Name, p => (bool)p.Value);

        /// <summary>One line per node, 'node:to/seg/len-bits/tags ...', in the order the graph lists them.</summary>
        public static string Line(TownGraph adj) => string.Join("\n", adj.Select(kv =>
            kv.Key + ":" + string.Join(" ", kv.Value.Select(e => $"{e.To}/{e.Seg}/{Bits(e.Len)}/{string.Join("+", e.Tags)}"))));

        public static bool Reachable(TownGraph adj, string from, string to) => Reach(adj, from).Contains(to);

        public static HashSet<string> Reach(TownGraph adj, string from)
        {
            var seen = new HashSet<string> { from };
            var todo = new Stack<string>();
            todo.Push(from);
            while (todo.Count > 0)
                foreach (var e in adj[todo.Pop()])
                    if (seen.Add(e.To)) todo.Push(e.To);
            return seen;
        }
    }

    /// <summary>
    /// Port of the original tests/townmap.test.mjs, case for case, plus parity with game/townmap.js
    /// (Fixtures/golden-townmap.json, regenerate with Tools/golden/make-golden-townmap.mjs).
    /// </summary>
    public class TownMapTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-townmap.json"));

        // Worst case on any night: east gate closed, entrance C still shut (opens 20:16).
        static readonly Dictionary<string, bool> WorstGates = new() { ["east"] = false, ["stairs"] = true, ["ramp"] = true, ["access"] = false };

        static IEnumerable<string[]> AllClosureSets => GameConditions.DDayClosureSets.Concat(GameConditions.EvacClosureSets);

        // ---------- the original's tests ----------

        [Test]
        public void Every_edge_endpoint_is_a_node_and_segment_ids_are_unique()
        {
            foreach (var e in TownMap.Edges)
            {
                Assert.IsNotNull(TownMap.Nodes[e.From], $"edge {e.Segment}: unknown node {e.From}");
                Assert.IsNotNull(TownMap.Nodes[e.To], $"edge {e.Segment}: unknown node {e.To}");
                Assert.IsNotNull(e.Tags);
            }
            Assert.AreEqual(TownMap.Edges.Count, TownMap.Edges.Select(e => e.Segment).Distinct().Count());
        }

        [Test]
        public void Every_closure_segment_exists_and_every_shelter_entrance_is_wired()
        {
            var segs = new HashSet<string>(TownMap.Edges.Select(e => e.Segment));
            foreach (var (id, c) in TownMap.Closures)
            {
                Assert.That(c.Segments.Count, Is.GreaterThan(0), id);
                foreach (var s in c.Segments) Assert.IsTrue(segs.Contains(s), $"closure {id}: unknown segment {s}");
            }
            foreach (var (k, e) in TownMap.ShelterEntrances)
            {
                Assert.IsNotNull(TownMap.Nodes[e.Node], k);
                Assert.IsNotNull(TownMap.Places[e.Place], k);
                var edge = TownMap.EdgeOf(e.Segment);
                Assert.IsTrue(edge != null && edge.Touches("shelter"), $"{k} segment must lead into the shelter");
            }
            foreach (var id in new[] { "home", "shelter" }) Assert.IsNotNull(TownMap.Spawns[id], id);
        }

        [Test]
        public void Graph_is_symmetric_and_drops_closed_segments_and_gates()
        {
            var open = TownMap.Graph();
            foreach (var (a, es) in open)
                foreach (var e in es)
                    Assert.IsTrue(open[e.To].Any(x => x.To == a && x.Seg == e.Seg), $"{a} -> {e.To} has no way back");
            var g = TownMap.Graph(new[] { "yeonmuCenter" }, WorstGates);
            var all = g.Values.SelectMany(es => es).Select(e => e.Seg).ToList();
            foreach (var s in new[] { "yeonmuC1", "yeonmuC2", "gateEast", "gateC" }) Assert.IsFalse(all.Contains(s), s);
            Assert.IsTrue(all.Contains("gateA"));
        }

        [Test]
        public void Conditions_only_ever_produce_closure_sets_from_the_closure_set_tables()
        {
            static string Key(IEnumerable<string> l) => string.Join("+", l.OrderBy(x => x, StringComparer.Ordinal));
            for (uint seed = 1; seed <= 500; seed++)
            {
                var c = GameConditions.For(seed).Closures;
                Assert.IsTrue(GameConditions.DDayClosureSets.Any(l => Key(l) == Key(c.DDay)), $"seed {seed} dday");
                Assert.IsTrue(GameConditions.EvacClosureSets.Any(l => Key(l) == Key(c.Evac)), $"seed {seed} evac");
            }
        }

        [Test]
        public void Home_to_shelter_stays_reachable_on_foot_for_every_closure_set()
        {
            for (uint seed = 1; seed <= 500; seed++)
            {
                var c = GameConditions.For(seed).Closures;
                foreach (var set in new[] { c.DDay, c.Evac })
                    Assert.IsTrue(TownGolden.Reachable(TownMap.Graph(set, WorstGates), "home", "shelter"), $"seed {seed}: {string.Join(",", set)}");
            }
            foreach (var set in AllClosureSets)
                Assert.IsTrue(TownGolden.Reachable(TownMap.Graph(set, WorstGates), "home", "shelter"), string.Join(",", set));
        }

        [Test]
        public void Home_to_assembly_point_stays_reachable_under_every_evac_closure_set()
        {
            foreach (var set in GameConditions.EvacClosureSets)
                Assert.IsTrue(TownGolden.Reachable(TownMap.Graph(set), "home", "assembly"), string.Join(",", set));
        }

        [Test]
        public void The_reachability_check_can_fail()
        {
            var g = TownMap.Graph(new[] { "yeonmuCenter", "danji", "sijang" }, WorstGates);
            Assert.IsFalse(TownGolden.Reachable(g, "home", "shelter"));
        }

        // ---------- parity with the original data ----------

        static JObject WithRect(JObject o, TownRect r) { o["x0"] = r.X0; o["x1"] = r.X1; o["z0"] = r.Z0; o["z1"] = r.Z1; return o; }

        /// <summary>Adds a key only when the original has it (JSON.stringify leaves undefined fields out).</summary>
        static JObject Opt(JObject o, string key, string value) { if (value != null) o[key] = value; return o; }

        static JArray Rows<T>(IEnumerable<T> list, Func<T, JObject> row) => new JArray(list.Select(row));

        [Test]
        public void Every_table_is_the_original_row_for_row()
        {
            var t = Golden["tables"];
            AssertSameJson(WithRect(new JObject(), TownMap.Bounds), t["bounds"], "bounds");
            Assert.AreEqual((double)t["sidewalk"], TownMap.Sidewalk);

            AssertSameJson(Rows(TownMap.Roads, r => { var o = WithRect(new JObject { ["id"] = r.Id, ["name"] = r.Name }, r.Rect); o["lanes"] = r.Lanes; return o; }), t["roads"], "roads");
            AssertSameJson(Rows(TownMap.Crosswalks, c => new JObject { ["road"] = c.Road, ["at"] = c.At }), t["crosswalks"], "crosswalks");
            AssertSameJson(Rows(TownMap.Buildings, b =>
            {
                var o = Opt(new JObject { ["id"] = b.Id }, "name", b.Name);
                o["kind"] = b.Kind;
                WithRect(o, b.Rect);
                o["floors"] = b.Floors;
                if (b.H.HasValue) o["h"] = b.H.Value;
                return Opt(Opt(Opt(o, "label", b.Label), "door", b.Door), "sign", b.Sign);
            }), t["buildings"], "buildings");
            AssertSameJson(Rows(TownMap.Areas, a => WithRect(new JObject { ["id"] = a.Id, ["name"] = a.Name, ["kind"] = a.Kind }, a.Rect)), t["areas"], "areas");
            AssertSameJson(Rows(TownMap.Places.Values, p => Opt(new JObject { ["id"] = p.Id, ["x"] = p.X, ["z"] = p.Z, ["label"] = p.Label, ["kind"] = p.Kind }, "to", p.To)), t["places"], "places");
            AssertSameJson(Rows(TownMap.Spawns.Values, s => new JObject { ["id"] = s.Id, ["x"] = s.X, ["z"] = s.Z, ["yaw"] = s.Yaw }), t["spawns"], "spawns");
            AssertSameJson(Rows(TownMap.Nodes.Values, n => Opt(new JObject { ["id"] = n.Id, ["x"] = n.X, ["z"] = n.Z }, "label", n.Label)), t["nodes"], "nodes");
            AssertSameJson(Rows(TownMap.Edges, e => new JObject { ["from"] = e.From, ["to"] = e.To, ["seg"] = e.Segment, ["tags"] = new JArray(e.Tags) }), t["edges"], "edges");
            AssertSameJson(Rows(TownMap.ShelterEntrances.Values, e =>
            {
                var o = Opt(new JObject { ["id"] = e.Id, ["node"] = e.Node, ["place"] = e.Place, ["segment"] = e.Segment, ["label"] = e.Label }, "short", e.Short);
                o["way"] = e.Way;
                return o;
            }), t["shelterEntrances"], "shelterEntrances");
            AssertSameJson(Rows(TownMap.Closures.Values, c => new JObject
            {
                ["id"] = c.Id, ["label"] = c.Label, ["segments"] = new JArray(c.Segments),
                ["barricades"] = new JArray(c.Barricades.Select(r => WithRect(new JObject(), r))),
            }), t["closures"], "closures");
        }

        [Test]
        public void Tables_keep_their_order_and_answer_like_the_original_objects()
        {
            CollectionAssert.AreEqual(TownMap.Nodes.Values.Select(n => n.Id), TownMap.Nodes.Keys);
            CollectionAssert.AreEqual(new[] { "east", "stairs", "ramp", "access" }, TownMap.ShelterEntrances.Keys);
            CollectionAssert.AreEqual(new[] { "yeonmuCenter", "danji", "sijang", "munhwaSouth", "munhwaNorth" }, TownMap.Closures.Keys);
            Assert.AreEqual(TownMap.Nodes.Count, TownMap.Nodes.Select(kv => kv.Key).Count());
            Assert.IsNull(TownMap.Nodes["moon"], "an unknown id is null, not an exception");
            Assert.IsNull(TownMap.Nodes[null]);
            Assert.IsFalse(TownMap.Closures.ContainsKey("nope"));
            Assert.IsTrue(TownMap.Closures.TryGetValue("danji", out var danji));
            Assert.AreEqual("단지길 통제", danji.Label);
            Assert.IsFalse(TownMap.Closures.TryGetValue(null, out _));

            foreach (var e in TownMap.ShelterEntrances.Values)
            {
                CollectionAssert.AreEqual(new[] { e.Way }, TownMap.EdgeOf(e.Segment).Tags, $"{e.Id}: way is the tag of its segment");
                Assert.AreEqual("shelter", TownMap.EdgeOf(e.Segment).Other(e.Node));
                Assert.AreEqual("shelter", TownMap.Places[e.Place].Kind);
            }
            foreach (var c in TownMap.Crosswalks) Assert.IsNotNull(TownMap.Road(c.Road), c.Road);
            Assert.IsTrue(TownMap.Road("yeonmu").EastWest);
            Assert.IsFalse(TownMap.Road("munhwa").EastWest);
            Assert.AreEqual(10, TownMap.Building("warehouse").Height, "h wins over floors x 3");
            Assert.AreEqual(45, TownMap.Building("apt103").Height);
            Assert.AreEqual("park", TownMap.Area("park").Kind);
            Assert.IsNull(TownMap.Building("nope"));
            var points = TownMap.Places.Values.Select(p => p.Pos).Concat(TownMap.Nodes.Values.Select(n => n.Pos)).Concat(TownMap.Spawns.Values.Select(s => s.Pos));
            foreach (var at in points) Assert.IsTrue(TownMap.Bounds.Contains(at.X, at.Z), $"{at} is inside the town");
        }

        [Test]
        public void Distances_are_the_original_Math_hypot_bit_for_bit()
        {
            var points = TownMap.Nodes.Values.Select(n => n.Pos)
                .Concat(TownMap.Places.Values.Select(p => p.Pos))
                .Concat(TownMap.Spawns.Values.Select(s => s.Pos)).ToList();
            Assert.AreEqual((int)Golden["points"], points.Count);
            var want = (JArray)Golden["distBits"];
            int k = 0;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++, k++)
                {
                    Assert.AreEqual((string)want[k], TownGolden.Bits(TownMap.Dist(points[i], points[j])), $"dist {points[i]} {points[j]}");
                    Assert.AreEqual(TownMap.Dist(points[i], points[j]), TownMap.Dist(points[j], points[i]));
                }
            Assert.AreEqual(want.Count, k);

            foreach (var row in Golden["edgeLengths"])
            {
                var e = TownMap.EdgeOf((string)row["seg"]);
                Assert.AreEqual((string)row["bits"], TownGolden.Bits(e.Length), e.Segment);
                Assert.AreEqual((double)row["len"], e.Length, 1e-9, e.Segment);
            }
            Assert.AreEqual(0, TownMap.Dist(points[0], points[0]));
            Assert.AreEqual(5, TownMap.Dist(0, 0, 3, -4));
        }

        [Test]
        public void The_open_graph_lists_every_link_in_the_original_order()
        {
            var full = TownMap.Graph();
            Assert.AreEqual((string)Golden["fullLine"], TownGolden.Line(full));
            var actual = new JArray(full.Select(kv => new JObject
            {
                ["node"] = kv.Key,
                ["links"] = new JArray(kv.Value.Select(e => new JObject { ["to"] = e.To, ["seg"] = e.Seg, ["tags"] = new JArray(e.Tags), ["len"] = e.Len })),
            }));
            AssertSameJson(actual, Golden["fullAdj"], "graph()");
            CollectionAssert.AreEqual(TownMap.Nodes.Keys, full.Keys, "every node is a key, in node order");
        }

        [Test]
        public void Graph_matches_the_original_under_every_closure_set_and_gate_state()
        {
            var rows = (JArray)Golden["graphs"];
            Assert.That(rows.Count, Is.GreaterThan(90));
            var covered = new HashSet<string>(rows.Select(r => string.Join(",", TownGolden.Strings(r["closures"]))));
            foreach (var set in AllClosureSets) Assert.IsTrue(covered.Contains(string.Join(",", set)), $"closure set {string.Join(",", set)} is in the fixture");

            var segs = TownMap.Edges.Select(e => e.Segment).ToList();
            foreach (var row in rows)
            {
                var closures = TownGolden.Strings(row["closures"]);
                var gates = TownGolden.BoolGates(row["gates"]);
                string what = $"closures [{string.Join(",", closures)}] gates {row["gates"].ToString(Newtonsoft.Json.Formatting.None)}";
                var adj = TownMap.Graph(closures, gates);
                var open = adj.Segments();
                Assert.AreEqual((string)row["open"], string.Join(" ", open), what);
                var cut = segs.Where(s => !open.Contains(s)).ToList();
                Assert.AreEqual((string)row["cut"], string.Join(" ", cut), what);
                CollectionAssert.AreEquivalent(cut, TownMap.CutSegments(closures, gates), what);
                var seen = TownGolden.Reach(adj, "home");
                Assert.AreEqual((string)row["unreachable"], string.Join(" ", TownMap.Nodes.Keys.Where(n => !seen.Contains(n))), what);
                Assert.AreEqual((string)row["digest"], TownGolden.Fnv(TownGolden.Line(adj)), what);
            }
        }

        [Test]
        public void Without_leaves_segments_out_and_the_graph_itself_alone()
        {
            var adj = TownMap.Graph();
            Assert.AreSame(adj, adj.Without(new string[0]));
            Assert.AreSame(adj, adj.Without(null));
            var cut = adj.Without(new[] { "danji", "gateA" });
            Assert.IsFalse(cut.Segments().Contains("danji") || cut.Segments().Contains("gateA"));
            Assert.AreEqual(adj.Segments().Count - 2, cut.Segments().Count);
            CollectionAssert.AreEqual(adj.Keys, cut.Keys);
            Assert.IsTrue(adj.Segments().Contains("danji"), "the original graph is not changed");
            Assert.AreEqual(TownGolden.Line(TownMap.Graph(new[] { "danji" }, new Dictionary<string, bool> { ["east"] = true, ["stairs"] = false, ["ramp"] = true, ["access"] = true })),
                TownGolden.Line(cut), "cutting segments by hand gives the graph a closure and a closed gate give");
        }
    }
}
