using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/routes.test.mjs, case for case, plus parity with game/routes.js
    /// (Fixtures/golden-routes.json, regenerate with Tools/golden/make-golden-routes.mjs).
    /// </summary>
    public class RoutesTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-routes.json"));

        GameRules R;

        [SetUp]
        public void SetUp() => R = Rules;

        /// <summary>Gates as the original passes them: Gates("{east:false,access:'closed'}").</summary>
        static Dictionary<string, bool> Gates(string json) => Routes.Gates(JObject.Parse(json));

        static readonly string[] GateSets =
        {
            "{east:true,stairs:true,ramp:true,access:true}",
            "{east:false,stairs:true,ramp:true,access:false}",
            "{east:'closed',stairs:'open',ramp:'open',access:'open'}",
            "{east:false,stairs:false,ramp:false,access:true}",
        };

        const string AllShut = "{east:false,stairs:false,ramp:false,access:false}";

        // ---------- the original's tests ----------

        [Test]
        public void ShortestPath_finds_the_shortest_way_and_its_parts()
        {
            var adj = TownMap.Graph();
            var r = Routes.ShortestPath(adj, "home", "store");
            CollectionAssert.AreEqual(new[] { "home", "gate", "I4", "store" }, r.Nodes);
            CollectionAssert.AreEqual(new[] { "aptPath", "hanbitMid", "yeonmuE" }, r.Segments);
            Assert.IsEmpty(r.Tags);
            Assert.IsTrue(r.Length > 50 && r.Length < 70);
            AssertJson(Routes.ShortestPath(adj, "home", "home").ToJObject(), "{nodes:['home'],length:0,segments:[],tags:[]}");
        }

        [Test]
        public void ShortestPath_edge_cases_unknown_nodes_unreachable_avoidTags_tagCost()
        {
            var adj = TownMap.Graph();
            Assert.IsNull(Routes.ShortestPath(adj, "home", "moon"));
            Assert.IsNull(Routes.ShortestPath(null, "home", "store"));
            Assert.IsNull(Routes.ShortestPath(TownMap.Graph(gates: Gates(AllShut)), "home", "shelter"));
            var viaA = Routes.ShortestPath(adj, "I3", "shelter");
            CollectionAssert.Contains(viaA.Tags, "stairs");
            var noStairs = Routes.ShortestPath(adj, "I3", "shelter", avoidTags: new[] { "stairs" });
            CollectionAssert.DoesNotContain(noStairs.Tags, "stairs");
            var pricey = Routes.ShortestPath(adj, "I3", "shelter", tagCost: new Dictionary<string, double> { ["stairs"] = 100 });
            CollectionAssert.DoesNotContain(pricey.Tags, "stairs");
            const double never = double.PositiveInfinity;
            var blocked = Routes.ShortestPath(adj, "A", "shelter", tagCost: new Dictionary<string, double> { ["stairs"] = never, ["ramp"] = never, ["level"] = never });
            Assert.IsFalse(blocked != null && blocked.Tags.Contains("stairs"));
        }

        [Test]
        public void RouteOptions_gives_up_to_3_distinct_options_with_time()
        {
            var g = R.CreateGame(1);
            var opts = Routes.RouteOptions(R, g, "home", "shelter", new[] { "yeonmuCenter" }, Gates("{east:'closed',access:'closed'}"));
            Assert.IsTrue(opts.Count >= 2 && opts.Count <= 3);
            Assert.AreEqual(opts.Count, opts.Select(o => string.Join(",", o.Segments)).Distinct().Count());
            Assert.AreEqual("shortest", opts[0].Kind);
            foreach (var o in opts)
            {
                Assert.AreEqual(Num.JsRound(o.Length / 1.3), o.Seconds);
                Assert.IsTrue(o.Entrance == "stairs" || o.Entrance == "ramp", o.Entrance);
                Assert.IsFalse(o.Segments.Any(s => TownMap.Closures["yeonmuCenter"].Segments.Contains(s)));
                Assert.IsFalse(o.Segments.Contains("gateEast") || o.Segments.Contains("gateC"));
            }
            Assert.IsTrue(opts.All(o => o.Length >= opts[0].Length));
            Assert.IsEmpty(Routes.RouteOptions(R, g, "home", "shelter", gates: Gates(AllShut)));
            Assert.Catch(() => Routes.RouteOptions(R, g, "home", "shelter", mobility: "fly"));
        }

        [Test]
        public void Carrying_over_8_kg_slows_walking_and_the_bag_is_used_when_carryingKg_is_not_given()
        {
            var g = R.CreateGame(1);
            var light = Routes.RouteOptions(R, g, "home", "water")[0];
            var heavy = Routes.RouteOptions(R, g, "home", "water", carryingKg: 10)[0];
            Assert.AreEqual(Num.JsRound(light.Length / (1.3 * 0.8)), heavy.Seconds);
            R.Buy(g, Cart(("water_2l", 3)), new Dictionary<string, int> { ["water_2l"] = 1100 });
            R.Pack(g, "water_2l", 5);
            Assert.AreEqual(heavy.Seconds, Routes.RouteOptions(R, g, "home", "water")[0].Seconds);
            Assert.AreEqual(light.Seconds, Routes.RouteOptions(null, null, "home", "water")[0].Seconds);
        }

        [Test]
        public void Walker_routes_never_use_stairs_or_steep_ways()
        {
            var sets = new List<string[]> { new string[0] };
            sets.AddRange(GameConditions.DDayClosureSets);
            sets.AddRange(GameConditions.EvacClosureSets);
            sets.Add(TownMap.Closures.Keys.ToArray());
            var pairs = new[] { ("cafe", "shelter"), ("home", "shelter"), ("home", "assembly"), ("store", "water") };
            int found = 0;
            foreach (var closures in sets)
                foreach (var gates in GateSets)
                    foreach (var (from, to) in pairs)
                        foreach (var o in Routes.RouteOptions(null, null, from, to, closures, Gates(gates), "walker"))
                        {
                            found++;
                            Assert.IsFalse(o.Tags.Contains("stairs") || o.Tags.Contains("steep"), $"{from}->{to} {string.Join(",", o.Segments)}");
                            Assert.AreEqual(Num.JsRound(o.Length / 0.9), o.Seconds);
                        }
            Assert.That(found, Is.GreaterThan(50));
            var seoyeon = Routes.RouteOptions(null, null, "cafe", "shelter", gates: Gates("{east:false}"), mobility: "walker")[0];
            Assert.AreEqual("access", seoyeon.Entrance);
            Assert.IsEmpty(Routes.RouteOptions(null, null, "cafe", "shelter", gates: Gates("{access:false,east:false}"), mobility: "walker"));
        }

        [Test]
        public void Walking_options_include_a_step_free_one_when_it_differs()
        {
            var opts = Routes.RouteOptions(null, null, "home", "shelter", gates: Gates("{east:false}"));
            Assert.IsTrue(opts.Any(o => !o.Tags.Contains("stairs") && !o.Tags.Contains("steep")));
        }

        [Test]
        public void Explain_describes_the_route_in_Korean()
        {
            var o = Routes.RouteOptions(null, null, "home", "shelter", gates: Gates("{east:false,ramp:false,access:false}"))[0];
            var s = Routes.Explain(o);
            StringAssert.StartsWith("103동 → 단지 정문", s);
            StringAssert.Contains("대피소(지하)", s);
            StringAssert.Contains("계단", s);
            StringAssert.IsMatch(@"\d+m, 약 \d+분", s);
            Assert.AreEqual("갈 수 있는 길이 없습니다.", Routes.Explain(null));
            Assert.AreEqual("103동 → 단지 정문 · 22m, 약 1분", Routes.Explain(new RoutePath { Nodes = new List<string> { "home", "gate" }, Length = 22 }));
        }

        // ---------- parity with the original ----------

        /// <summary>A number as the original prints it in a template string.</summary>
        static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        static JToken Json(RoutePath p) => p != null ? p.ToJObject() : JValue.CreateNull();

        /// <summary>One path as the golden generator writes it for its sweeps: 'nodes|length|segments|tags', '-' for no way.</summary>
        static string PathLine(RoutePath p) =>
            p == null ? "-" : $"{string.Join(" ", p.Nodes)}|{N(p.Length)}|{string.Join(" ", p.Segments)}|{string.Join(" ", p.Tags)}";

        static JArray Said(IEnumerable<RouteOption> opts) => new JArray(opts.Select(o =>
        {
            var j = o.ToJObject();
            j["said"] = Routes.Explain(o);
            return j;
        }));

        static string Where(JToken row) => $"closures {row["closures"]?.ToString(Newtonsoft.Json.Formatting.None)} gates {row["gates"]?.ToString(Newtonsoft.Json.Formatting.None)}";

        /// <summary>shortestPath options from a fixture: { avoidTags?, tagCost? } with 'Infinity' written as a string.</summary>
        static RoutePath Shortest(TownGraph adj, string from, string to, JToken options)
        {
            var avoid = TownGolden.Strings(options["avoidTags"]);
            Dictionary<string, double> cost = null;
            if (options["tagCost"] is JObject tc)
                cost = tc.Properties().ToDictionary(p => p.Name, p => p.Value.Type == JTokenType.String ? double.PositiveInfinity : (double)p.Value);
            return Routes.ShortestPath(adj, from, to, avoid, cost);
        }

        /// <summary>routeOptions as a fixture row describes the call: { from, to, options: { closures?, gates?, mobility?, carryingKg? } }.</summary>
        static List<RouteOption> Call(GameRules rules, GameState g, JToken call)
        {
            var o = call["options"] as JObject ?? new JObject();
            return Routes.RouteOptions(rules, g, (string)call["from"], (string)call["to"],
                TownGolden.Strings(o["closures"]), o["gates"] is JObject gates ? Routes.Gates(gates) : null,
                (string)o["mobility"] ?? "walk", (double?)o["carryingKg"]);
        }

        [Test]
        public void The_fixture_covers_every_closure_set_conditions_can_pick()
        {
            var covered = new HashSet<string>(Golden["closureSets"].Select(l => string.Join(",", TownGolden.Strings(l))));
            foreach (var set in GameConditions.DDayClosureSets.Concat(GameConditions.EvacClosureSets))
                Assert.IsTrue(covered.Contains(string.Join(",", set)), string.Join(",", set));
            Assert.IsTrue(covered.Contains(""), "no closure");
            Assert.IsTrue(covered.Contains(string.Join(",", TownMap.Closures.Keys)), "every closure at once");
        }

        [Test]
        public void Shortest_paths_match_the_original_under_every_closure_set()
        {
            int n = 0;
            foreach (var group in Golden["paths"])
            {
                var adj = TownMap.Graph(TownGolden.Strings(group["closures"]), TownGolden.BoolGates(group["gates"]));
                foreach (var r in group["results"])
                {
                    var p = Routes.ShortestPath(adj, (string)r["from"], (string)r["to"]);
                    AssertSameJson(Json(p), r["path"], $"{r["from"]} -> {r["to"]}, {Where(group)}");
                    n++;
                }
            }
            Assert.That(n, Is.GreaterThan(150));
        }

        [Test]
        public void AvoidTags_and_tagCost_change_the_path_like_the_original()
        {
            var rows = (JArray)Golden["variants"];
            Assert.That(rows.Count, Is.GreaterThan(100));
            foreach (var r in rows)
            {
                var adj = TownMap.Graph(TownGolden.Strings(r["closures"]), TownGolden.BoolGates(r["gates"]));
                var p = Shortest(adj, (string)r["from"], (string)r["to"], r["options"]);
                AssertSameJson(Json(p), r["path"], $"{r["from"]} -> {r["to"]} {r["options"].ToString(Newtonsoft.Json.Formatting.None)}, {Where(r)}");
            }
        }

        [Test]
        public void Every_start_and_goal_pair_gives_the_original_path_on_every_graph()
        {
            var rows = (JArray)Golden["sweep"];
            Assert.That(rows.Count, Is.GreaterThan(150));
            var ids = TownMap.Nodes.Keys;
            foreach (var r in rows)
            {
                var adj = TownMap.Graph(TownGolden.Strings(r["closures"]), TownGolden.BoolGates(r["gates"]));
                int found = 0;
                var lines = new List<string>(ids.Count * ids.Count);
                foreach (var from in ids)
                    foreach (var to in ids)
                    {
                        var p = Shortest(adj, from, to, r["options"]);
                        if (p != null) found++;
                        lines.Add($"{from}>{to}={PathLine(p)}");
                    }
                string what = $"{r["options"].ToString(Newtonsoft.Json.Formatting.None)}, {Where(r)}";
                Assert.AreEqual((int)r["found"], found, what);
                Assert.AreEqual((string)r["digest"], TownGolden.Fnv(string.Join("\n", lines)), what);
            }
        }

        /// <summary>
        /// The town has no two ways of exactly the same cost, so hand-made graphs (a grid of 1 m edges, free edges)
        /// pin down which of several equal ways is taken: the original's choice, from the original's code.
        /// </summary>
        [Test]
        public void Equal_ways_are_chosen_like_the_original()
        {
            var cases = (JArray)Golden["ties"];
            Assert.That(cases.Count, Is.GreaterThan(10));
            foreach (var c in cases)
            {
                var adj = new TownGraph(c["adj"].Select(n => new KeyValuePair<string, IReadOnlyList<TownLink>>((string)n["node"],
                    n["links"].Select(l => new TownLink((string)l["to"], (string)l["seg"], TownGolden.Strings(l["tags"]), (double)l["len"])).ToList())));
                var lines = new List<string>();
                foreach (var from in adj.Keys)
                    foreach (var to in adj.Keys)
                        lines.Add($"{from}>{to}={PathLine(Shortest(adj, from, to, c["options"]))}");
                string what = $"{c["name"]} {c["options"].ToString(Newtonsoft.Json.Formatting.None)}";
                if (c["lines"] != null) CollectionAssert.AreEqual(c["lines"].Select(t => (string)t), lines, what); // small graphs: readable
                Assert.AreEqual((string)c["digest"], TownGolden.Fnv(string.Join("\n", lines)), what);
            }
        }

        [Test]
        public void Route_options_match_the_original_with_their_explanations()
        {
            var rows = (JArray)Golden["options"];
            Assert.That(rows.Count, Is.GreaterThan(100));
            var kinds = new HashSet<string>();
            foreach (var r in rows)
            {
                var opts = Routes.RouteOptions(null, null, (string)r["from"], (string)r["to"],
                    TownGolden.Strings(r["closures"]), Routes.Gates((JObject)r["gates"]), (string)r["mobility"]);
                AssertSameJson(Said(opts), r["result"], $"{r["mobility"]} {r["from"]} -> {r["to"]}, {Where(r)}");
                for (int i = 0; i < opts.Count; i++) kinds.Add(i + opts[i].Kind);
            }
            CollectionAssert.IsSubsetOf(new[] { "0shortest", "1detour", "2stepFree", "2detour" }, kinds, "every kind of option is covered");
        }

        [Test]
        public void Route_options_from_every_node_match_the_original_on_every_graph()
        {
            var rows = (JArray)Golden["optionSweep"];
            Assert.That(rows.Count, Is.GreaterThan(100));
            var targets = new[] { "shelter", "assembly", "water", "home", "cafe" };
            foreach (var r in rows)
            {
                var closures = TownGolden.Strings(r["closures"]);
                var gates = Routes.Gates((JObject)r["gates"]);
                int count = 0;
                var lines = new List<string>();
                foreach (var from in TownMap.Nodes.Keys)
                    foreach (var to in targets)
                    {
                        var opts = Routes.RouteOptions(null, null, from, to, closures, gates, (string)r["mobility"], (double?)r["carryingKg"]);
                        count += opts.Count;
                        lines.Add($"{from}>{to}=" + string.Join(";", opts.Select(o =>
                            $"{o.Kind}/{o.Entrance ?? "null"}/{N(o.Length)}/{N(o.Seconds)}/{string.Join(" ", o.Nodes)}/{string.Join(" ", o.Segments)}/{string.Join(" ", o.Tags)}/{Routes.Explain(o)}")));
                    }
                string what = $"{r["mobility"]} {r["carryingKg"]} kg, {Where(r)}";
                Assert.AreEqual((int)r["count"], count, what);
                Assert.AreEqual((string)r["digest"], TownGolden.Fnv(string.Join("\n", lines)), what);
            }
        }

        [Test]
        public void The_bag_weight_slows_the_walk_like_the_original()
        {
            var games = (JArray)Golden["games"];
            Assert.AreEqual(5, games.Count);
            foreach (var game in games)
            {
                var g = SaveCodec.Load(game["save"].ToString(), Db);
                Assert.AreEqual((double)game["bagKg"], R.BagLoad(g).WeightKg, (string)game["name"]);
                foreach (var call in game["calls"])
                    AssertSameJson(Said(Call(R, g, call)), call["result"], $"{game["name"]}: {call["from"]} -> {call["to"]} {call["options"].ToString(Newtonsoft.Json.Formatting.None)}");
            }
            foreach (var call in Golden["noGame"])
                AssertSameJson(Said(Call(null, null, call)), call["result"], $"no game: {call["from"]} -> {call["to"]} {call["options"].ToString(Newtonsoft.Json.Formatting.None)}");

            var heavy = SaveCodec.Load(games[3]["save"].ToString(), Db);
            Assert.Catch(() => Routes.RouteOptions(null, heavy, "home", "water"), "weighing the bag needs the rules");
            Assert.DoesNotThrow(() => Routes.RouteOptions(null, heavy, "home", "water", carryingKg: 3));
        }

        [Test]
        public void Explain_says_the_same_words_as_the_original()
        {
            var rows = (JArray)Golden["explain"];
            Assert.That(rows.Count, Is.GreaterThan(10));
            foreach (var r in rows)
            {
                var o = r["option"];
                RoutePath option = null;
                if (o != null && o.Type != JTokenType.Null)
                {
                    var nodes = TownGolden.Strings(o["nodes"]) ?? new List<string>();
                    var tags = TownGolden.Strings(o["tags"]);
                    double length = (double?)o["length"] ?? 0;
                    option = o["seconds"] != null
                        ? new RouteOption { Nodes = nodes, Length = length, Tags = tags, Seconds = (double)o["seconds"] }
                        : new RoutePath { Nodes = nodes, Length = length, Tags = tags };
                }
                Assert.AreEqual((string)r["said"], Routes.Explain(option), o?.ToString(Newtonsoft.Json.Formatting.None));
            }
            Assert.AreEqual("갈 수 있는 길이 없습니다.", Routes.Explain(new RoutePath { Nodes = null }));
        }

        [Test]
        public void Only_false_and_closed_close_a_gate()
        {
            foreach (var r in Golden["gateOpen"])
            {
                Assert.AreEqual((bool)r["open"], Routes.GateOpen(r["value"]), r["value"].ToString(Newtonsoft.Json.Formatting.None));
                var gates = Routes.Gates(new JObject { ["east"] = r["value"].DeepClone(), ["stairs"] = false, ["ramp"] = false, ["access"] = false });
                Assert.AreEqual((bool)r["open"], Routes.RouteOptions(null, null, "east", "shelter", gates: gates).Count > 0);
            }
            Assert.IsTrue(Routes.GateOpen(null), "a gate nobody mentioned is open");
            Assert.IsEmpty(Routes.Gates(null));
            Assert.IsTrue(Routes.RouteOptions(null, null, "east", "shelter")[0].Entrance == "east");
        }
    }
}
