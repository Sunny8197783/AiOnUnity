using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class RoutemapLogicTests
    {
        static JObject PrepOptions()
        {
            // As story/prep-actions.js doShelterCheck calls it.
            var opts = Routes.RouteOptions(null, null, "home", "shelter", new string[0],
                new Dictionary<string, bool> { ["east"] = true, ["stairs"] = true, ["ramp"] = true, ["access"] = false }, "walk", 0);
            return new JObject
            {
                ["mode"] = "guided", ["from"] = "home", ["to"] = "shelter", ["options"] = new JArray(opts.Select(o => o.ToJObject())),
                ["closures"] = new JArray(), ["gates"] = new JObject { ["east"] = "open", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "closed" },
                ["known"] = new JObject { ["closures"] = new JArray() }, ["playerAt"] = new JObject { ["x"] = 78, ["z"] = 26 },
            };
        }

        [Test]
        public void Parse_defaults_view_when_no_options()
        {
            var r = RoutemapLogic.Parse(new JObject());
            Assert.AreEqual("guided", r.Mode);
            Assert.IsTrue(r.View);
            Assert.AreEqual(78, r.Player.Value.X); // NODES.home
            Assert.AreEqual(-90, r.Dest.Value.X);  // NODES.shelter
            Assert.AreEqual(-1, RoutemapLogic.InitialSelection(r.View));
        }

        [Test]
        public void Parse_explicit_null_keeps_null_like_destructuring()
        {
            // JS defaults apply only to undefined: { mode: null, from: null } keeps null (no chip/help, NODES[null] = no player).
            var r = RoutemapLogic.Parse(new JObject { ["mode"] = null, ["from"] = null, ["to"] = null });
            Assert.IsNull(r.Mode);
            Assert.IsNull(r.Player);
            Assert.IsNull(r.Dest);
            Assert.IsTrue(r.View);
        }

        [Test]
        public void Parse_view_mode_from_app_M_key()
        {
            var r = RoutemapLogic.Parse(new JObject
            {
                ["mode"] = "view", ["from"] = "M", ["to"] = "shelter", ["options"] = new JArray(), ["closures"] = new JArray("danji", "nope"),
                ["gates"] = new JObject(), ["known"] = new JObject { ["closures"] = new JArray("danji", "sijang") }, ["playerAt"] = null,
            });
            Assert.IsTrue(r.View);
            CollectionAssert.AreEqual(new[] { "danji", "sijang" }, r.Shown);
            Assert.AreEqual(0, r.Player.Value.X); Assert.AreEqual(0, r.Player.Value.Z); // falls back to NODES.M
        }

        [Test]
        public void Parse_route_options_round_trip()
        {
            var r = RoutemapLogic.Parse(PrepOptions());
            Assert.IsFalse(r.View);
            Assert.GreaterOrEqual(r.Options.Count, 2);
            Assert.AreEqual("shortest", r.Options[0].Kind);
            Assert.AreEqual("home", r.Options[0].Nodes[0]);
            Assert.AreEqual(78, r.Player.Value.X); Assert.AreEqual(26, r.Player.Value.Z);
            // explain() from the parsed JSON equals explain() of the original object
            var direct = Routes.RouteOptions(null, null, "home", "shelter", new string[0],
                new Dictionary<string, bool> { ["access"] = false }, "walk", 0);
            Assert.AreEqual(Routes.Explain(direct[0]), RoutemapLogic.RouteExplain(r.Options[0]));
        }

        [Test]
        public void Gate_state_reads_strings_and_booleans()
        {
            Assert.AreEqual("open", RoutemapLogic.GateState("open"));
            Assert.AreEqual("open", RoutemapLogic.GateState(true));
            Assert.AreEqual("closed", RoutemapLogic.GateState("closed"));
            Assert.AreEqual("closed", RoutemapLogic.GateState(false));
            Assert.AreEqual("unknown", RoutemapLogic.GateState(null));
            Assert.AreEqual("unknown", RoutemapLogic.GateState("maybe"));
            var lines = RoutemapLogic.GateLines(new JObject { ["east"] = "open", ["access"] = "closed" });
            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual(("open", "✓", "동쪽 출입구 · 열림"), lines[0]);
            Assert.AreEqual(("unknown", "?", "출입구 A · 계단 · 확인 안 함"), lines[1]);
            Assert.AreEqual(("closed", "✕", "출입구 C · 경사로 · 닫힘"), lines[3]);
        }

        [Test]
        public void Route_texts()
        {
            var o = new RouteOption { Kind = "stepFree", Entrance = "stairs" };
            Assert.AreEqual("경로 2 · 계단 없는 길", RoutemapLogic.RouteTitle(1, o));
            Assert.AreEqual("경로 1 · 다른 길", RoutemapLogic.RouteTitle(0, new RouteOption { Kind = "weird" }));
            Assert.AreEqual(" (짧은 점선)", RoutemapLogic.RouteSpec(2));
            Assert.AreEqual("A 계단로 들어감", RoutemapLogic.EntranceBadge(o));
            Assert.AreEqual("동쪽 출입구로 들어감", RoutemapLogic.EntranceBadge(new RouteOption { Entrance = "east" }));
            Assert.IsNull(RoutemapLogic.EntranceBadge(new RouteOption()));
            CollectionAssert.AreEqual(new[] { "단지길 통제" }, RoutemapLogic.ClosureLines(new[] { "danji" }));
            Assert.AreEqual("이 길로 정한다", RoutemapLogic.Primary(false));
            Assert.AreEqual("Esc 또는 M으로 닫기", RoutemapLogic.Keys(true));
        }

        [Test]
        public void Step_wraps_and_result_shape()
        {
            Assert.AreEqual(1, RoutemapLogic.Step(0, 1, 3));
            Assert.AreEqual(0, RoutemapLogic.Step(2, 1, 3));
            Assert.AreEqual(2, RoutemapLogic.Step(0, -1, 3));
            Assert.AreEqual(0, RoutemapLogic.Step(2, 1, 5)); // only 3 listed
            Assert.AreEqual(-1, RoutemapLogic.Step(0, 1, 0));
            Assert.AreEqual(JTokenType.Null, RoutemapLogic.Result(true, -1)["choice"].Type);
            Assert.AreEqual(2, (int)RoutemapLogic.Result(false, 2)["choice"]);
        }

        [Test]
        public void Projector_fits_the_town()
        {
            var p = MapProjector.Fit(260 * 3, 220 * 3);
            Assert.AreEqual(3, p.S, 1e-9);
            Assert.AreEqual((0f, 0f), p.P(-130, -110));
            Assert.AreEqual((780f, 660f), p.P(130, 110));
        }

        [Test]
        public void Dash_pattern_carries_across_corners()
        {
            var pts = new List<(float, float)> { (0, 0), (5, 0), (5, 10) };
            var d = RoutemapLogic.Stroke(pts, new float[] { 4, 2 }, 1, new Rgba(1, 1, 1));
            // 0-4 on, 4-6 off (corner at 5), 6-10 on (y 1..5), 10-12 off, 12-15 on (y 7..10)
            Assert.AreEqual(3, d.Count);
            Assert.AreEqual(4, d[0].X2, 1e-4);
            Assert.AreEqual(5, d[1].X, 1e-4); Assert.AreEqual(1, d[1].Y, 1e-4); Assert.AreEqual(5, d[1].Y2, 1e-4);
            Assert.AreEqual(7, d[2].Y, 1e-4); Assert.AreEqual(10, d[2].Y2, 1e-4);
            var solid = RoutemapLogic.Stroke(pts, new float[0], 2, new Rgba(1, 1, 1), roundJoins: true);
            Assert.AreEqual(2 + 3, solid.Count);
            Assert.AreEqual(0, RoutemapLogic.Stroke(new List<(float, float)> { (0, 0) }, null, 1, new Rgba(1, 1, 1)).Count);
        }

        [Test]
        public void Base_layer_has_labels_and_only_shown_closures()
        {
            var p = MapProjector.Fit(780, 660);
            var none = RoutemapLogic.BaseLayer(p, new string[0], 1);
            var texts = none.Where(s => s.Kind == MapShapeKind.Text).Select(s => s.Text).ToList();
            CollectionAssert.Contains(texts, "연무로");
            CollectionAssert.Contains(texts, "103동");          // '한빛아파트 ' dropped
            CollectionAssert.Contains(texts, "대피소(지하)");
            CollectionAssert.Contains(texts, "연무공원");
            Assert.IsFalse(texts.Any(t => t.StartsWith("통제")));
            var withClosure = RoutemapLogic.BaseLayer(p, new[] { "yeonmuCenter" }, 1);
            Assert.AreEqual(1, withClosure.Count(s => s.Kind == MapShapeKind.Text && s.Text == "통제 · 연무로 중앙 구간 통제")); // label on the first barricade only
            Assert.AreEqual(none.Count(s => s.Kind == MapShapeKind.Circle) + 2, withClosure.Count(s => s.Kind == MapShapeKind.Circle));
            Assert.IsFalse(RoutemapLogic.BaseLayer(p, new string[0], 1, labels: false).Any(s => s.Kind == MapShapeKind.Text));
            // background first, filling the map
            Assert.AreEqual(780, none[0].W); Assert.AreEqual(660, none[0].H);
        }

        [Test]
        public void Top_layer_routes_selected_last_with_numbers()
        {
            var r = RoutemapLogic.Parse(PrepOptions());
            var p = MapProjector.Fit(780, 660);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, RoutemapLogic.DrawOrder(3, 0));
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, RoutemapLogic.DrawOrder(3, 1));
            var top = RoutemapLogic.TopLayer(p, r.Options, 0, r.Gates, r.Player, r.Dest, 1);
            var texts = top.Where(s => s.Kind == MapShapeKind.Text).Select(s => s.Text).ToList();
            for (int i = 1; i <= r.Options.Count; i++) CollectionAssert.Contains(texts, i.ToString());
            CollectionAssert.Contains(texts, "나");
            CollectionAssert.Contains(texts, "북");
            CollectionAssert.Contains(texts, "50m");
            CollectionAssert.Contains(texts, "✕"); // access closed
            CollectionAssert.Contains(texts, "동쪽 출입구");
            Assert.AreEqual(1, top.Count(s => s.Kind == MapShapeKind.Diamond));
            // the selected route (amber) is drawn after the others
            int lastAmber = top.FindLastIndex(s => s.Kind == MapShapeKind.Line && s.Color.R > 0.9f && s.Color.G > 0.6f && s.Color.B < 0.3f);
            int lastTeal = top.FindLastIndex(s => s.Kind == MapShapeKind.Line && s.Color.R < 0.3f && s.Color.G > 0.7f);
            if (lastTeal >= 0) Assert.Greater(lastAmber, lastTeal);
            // a closed gate is a square, the open ones discs
            Assert.IsTrue(top.Any(s => s.Kind == MapShapeKind.Rect && System.Math.Abs(s.Color.R - 0.898f) < 0.01f));
        }

        [Test]
        public void Single_route_has_no_number()
        {
            var r = RoutemapLogic.Parse(PrepOptions());
            var top = RoutemapLogic.TopLayer(MapProjector.Fit(780, 660), r.Options.Take(1).ToList(), 0, r.Gates, null, null, 1);
            Assert.IsFalse(top.Any(s => s.Kind == MapShapeKind.Text && s.Text == "1"));
            Assert.IsFalse(top.Any(s => s.Kind == MapShapeKind.Text && s.Text == "나"));
        }
    }
}
