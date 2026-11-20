// Elder (story/elder.js): the pure follower / safe-early test of tests/story.test.mjs, and the D-day dilemma played on
// FakeStoryHost for each of the three answers (the alert roam calls ElderDilemma.Tick every frame, as dday.js does).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class ElderTests
    {
        static Dictionary<string, JToken> Know(object pick = null, bool? told = null)
        {
            var k = new Dictionary<string, JToken>();
            if (pick != null) k["elderPick"] = JToken.FromObject(pick);
            if (told != null) k["elderTold"] = told.Value;
            return k;
        }

        [Test]
        public void FollowsTheTrailAStepBehind_HelpedOrToldCountsAsBroughtDownEarly()
        {
            var crumbs = new List<Crumb> { new(0, 0), new(0.5, 0), new(1, 0), new(1.5, 0), new(2, 0) };
            var t = Elder.FollowTarget(crumbs, 1.1);
            Assert.That(t.HasValue && t.Value.X == 0.5 && t.Value.Z == 0, Is.True, t?.ToString());
            var one = Elder.FollowTarget(crumbs.Take(1).ToList());
            Assert.That(one.HasValue && one.Value.X == 0 && one.Value.Z == 0, Is.True);
            Assert.That(Elder.FollowTarget(new List<Crumb>()), Is.Null);
            Assert.That(Elder.ElderSafeEarly(Know("help")) && Elder.ElderSafeEarly(Know("manager", true)), Is.True);
            Assert.That(!Elder.ElderSafeEarly(Know("manager")) && !Elder.ElderSafeEarly(Know("go")) && !Elder.ElderSafeEarly(Know()), Is.True);
            Assert.That(Elder.ElderSafeEarly((IDictionary<string, JToken>)null), Is.False);
        }

        /// <summary>D-day sets elder304 'on'; the real level then shows her and enables the point (the fake only records the state).</summary>
        static void ElderOn(FakeStoryHost host, StoryContext ctx)
        {
            ctx.SetLevelState("apartment", "elder304", "on");
            host.Point("elder304").Enabled = true;
            host.Person("elder").Visible = true;
        }

        /// <summary>The alert on 3F: walk past 304, answer, then go down to the 1F stairs.</summary>
        static (FakeStoryHost host, StoryContext ctx, ElderDilemma elder) Play(string pick, Action<GameState> setup = null)
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            var g = host.G;
            setup?.Invoke(g);
            host.Answer(pick);
            ElderDilemma elder = null;
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                ElderOn(host, ctx);
                elder = Elder.CreateElderDilemma(ctx);
                var p = host.Point("elder304");
                host.Queue(h => h.MoveTo(p.X - 3, p.Z, p.Y), label: "walk past 304");
                host.Queue(h => { h.MoveTo(4, -0.5, 5.6); h.RunFrame(); h.MoveTo(5, 0.9, 0); }, () => g.Knows("elderPick") && ctx.Engine.ControlsEnabled, "down the stairs");
                await ctx.Roam(new RoamOptions
                {
                    Tick = dt =>
                    {
                        elder.Tick(dt);
                        return g.Knows("elderPick") && !elder.Following && ctx.Engine.PlayerY < 1 && ctx.Engine.ControlsEnabled ? (JToken)"down" : null;
                    },
                });
            });
            return (host, ctx, elder);
        }

        [Test]
        public void Dilemma_Help_SlowsThePlayer_SheFollowsDownAndIsThanked()
        {
            var (host, ctx, elder) = Play("help");
            var g = host.G;
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "elder_choice", "help" }));
            Assert.That((string)g.Events[0].Details["pick"], Is.EqualTo("help"));
            Assert.That((string)g.Events[1].Details["who"], Is.EqualTo("elder"));
            Assert.That(Elder.ElderSafeEarly(g), Is.True);
            Assert.That(host.Captions, Has.Member("계단 옆 304호 앞에서 어르신이 벽 손잡이를 붙들고 서 있다."), "no cine: the plain caption");
            Assert.That(host.Talks.Select(t => t.Lines[0]).ToList(), Is.EqualTo(new[]
            {
                "\"302호 정민 씨예요? 사이렌이 울리는데 계단이 캄캄해서… 혼자 내려가기가 겁나네요.\"",
                "\"고마워요. 신발만 신을게요. 천천히 가요. 손잡이 쪽에 서 줘요.\"",
                "\"다 왔네. 여기서부터는 소장님이랑 천천히 갈게요. 고마워요. 어서 가요.\"",
            }));
            Assert.That(host.Talks.Last().Role, Is.EqualTo("1층 계단 앞"));
            Assert.That(host.Point("elder304").Enabled, Is.False);
            Assert.That(ctx.SpeedOverride, Is.Null, "her pace ends at 1F");
            Assert.That((string)ctx.LevelStates["apartment"]["elder304"], Is.EqualTo("shelter"));
            Assert.That((string)ctx.LevelStates["shelter"]["elder"], Is.EqualTo("on"));
            Assert.That(host.Toasts.Any(t => t.StartsWith("어르신 걸음에 맞춰 계단으로. ")), Is.True);
            Assert.That(g.Minute, Is.EqualTo(17 * 60 + 33), "3 minutes to get ready");
            var her = host.Person("elder");
            Assert.That(her.Y, Is.LessThan(1), "she came down");
        }

        [Test]
        public void Dilemma_Help_WithABagOverTheLimit_StillHelps_ButTheWeightCostsAMinuteAndFatigue()
        {
            var (host, ctx, elder) = Play("help", g =>
            {
                g.Items["water_2l"] = new ItemEntry(0) { Bag = 5 }; // 10.25 kg on the back
                g.SetKnow("bagOn", true);
            });
            var g = host.G;
            Assert.That(TestSupport.Rules.BagLoad(g).OverWeight, Is.True);
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "elder_choice", "bag_heavy", "help" }), "she is still helped down");
            Assert.That((string)g.Events[1].Details["where"], Is.EqualTo("stairs"));
            Assert.That(host.Captions.Any(c => c.StartsWith("배낭 ") && c.Contains("어깨가 짓눌린다")), Is.True);
            Assert.That(g.Minute, Is.EqualTo(17 * 60 + 34), "3 minutes to get ready and one more for the weight");
            Assert.That(Elder.ElderSafeEarly(g), Is.True);
        }

        [Test]
        public void Dilemma_Manager_ThenTheWindowLineOnce()
        {
            var (host, ctx, elder) = Play("manager");
            var g = host.G;
            Assert.That(Elder.ElderSafeEarly(g), Is.False);
            Assert.That(host.Toasts, Has.Member("1층 관리사무소 창구에서 박 소장에게 알리자."));
            bool said = false, again = true;
            host.Run(async () => { said = await elder.ManagerLine(); again = await elder.ManagerLine(); });
            Assert.That(said, Is.True); Assert.That(again, Is.False);
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "elder_choice", "help" }));
            Assert.That((string)g.Events[1].Details["who"], Is.EqualTo("manager"));
            Assert.That(Elder.ElderSafeEarly(g), Is.True);
            Assert.That((string)host.Level("apartment").State["elder304"], Is.EqualTo("shelter"));
            Assert.That((string)ctx.LevelStates["shelter"]["elder"], Is.EqualTo("on"));
        }

        [Test]
        public void Dilemma_Go_NothingElseHappens()
        {
            var (host, ctx, elder) = Play("go");
            var g = host.G;
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "elder_choice" }));
            Assert.That(Elder.ElderSafeEarly(g), Is.False);
            bool said = true;
            host.Run(async () => said = await elder.ManagerLine());
            Assert.That(said, Is.False);
            Assert.That(host.Talks.Last().Lines[0], Is.EqualTo("\"그래요, 어서 가요. 나는 소장님 올라오면 같이 갈게요.\""));
            Assert.That(ctx.SpeedOverride, Is.Null);
        }

        [Test]
        public void Dilemma_LeavingTheBuildingWhileFollowing_SheComesWithTheManager()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.Answer("help");
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                ElderOn(host, ctx);
                var elder = Elder.CreateElderDilemma(ctx);
                var p = host.Point("elder304");
                host.Queue(h => h.MoveTo(p.X - 3, p.Z, p.Y), label: "walk past 304");
                host.Queue(h => _ = ctx.Goto("town", "home"), () => elder.Following && ctx.Engine.ControlsEnabled, "out at once");
                await ctx.Roam(new RoamOptions { Tick = dt => { elder.Tick(dt); return ctx.Engine.LevelId == "town" && !elder.Following ? (JToken)"out" : null; } });
            });
            Assert.That(host.Toasts, Has.Member("어르신은 1층에서 박 소장과 함께 천천히 오기로 했다."));
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "elder_choice", "help" }));
        }
        [Test]
        public void ApartmentWalk_MatchesTheLevelJson_AndGroundAtFiltersByFeet()
        {
            var j = JObject.Parse(File.ReadAllText(Path.Combine(FakeStoryHost.ResolveDataDir(), "apartment-level.json")));
            var walk = (JArray)j["walk"];
            Assert.That(Elder.ApartmentWalk.Length, Is.EqualTo(walk.Count));
            for (int i = 0; i < walk.Count; i++)
            {
                var w = walk[i]; var z = Elder.ApartmentWalk[i];
                Assert.That(new[] { z.X0, z.X1, z.Z0, z.Z1 }, Is.EqualTo(new[] { (double)w["x0"], (double)w["x1"], (double)w["z0"], (double)w["z1"] }), $"zone {i}");
                if (w["ramp"] is JObject r) Assert.That((z.Axis, z.Y0, z.Y1), Is.EqualTo(((string)r["axis"], (double)r["y0"], (double)r["y1"])), $"ramp {i}");
                else { Assert.That(z.Axis, Is.Null); Assert.That(z.Y, Is.EqualTo((double)w["y"]), $"floor {i}"); }
            }
            // groundAt(L.walk, x, z, y + 0.4, false, 1.2): the stair ramp under her feet, not the floors above or below.
            Assert.That(Elder.GroundAt(Elder.ApartmentWalk, 5.85, 2.363, 0.7 + 0.4, 1.2), Is.EqualTo(0.7).Within(1e-9));
            Assert.That(Elder.GroundAt(Elder.ApartmentWalk, 5, 0.9, 0 + 0.4, 1.2), Is.EqualTo(0));
            Assert.That(Elder.GroundAt(Elder.ApartmentWalk, 5, 0.9, 5.6 + 0.4, 1.2), Is.EqualTo(5.6));
            Assert.That(Elder.GroundAt(Elder.ApartmentWalk, 30, 0, 5.6 + 0.4, 1.2), Is.Null, "off the mesh: the trail's height");
        }

        [Test]
        public void Follower_WalkSpeedFollowsHerDistance_AndSheStandsOnTheStairs()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            var g = host.G;
            host.Answer("help");
            var speeds = new List<double>();
            var onStairs = new List<string>();
            int idle = 0;
            // Down the 3F -> 2F flight (ramp x 3.4..4.9, z 1.537 -> 3.738, y 5.6 -> 4.2) in small steps; each waits for her to settle.
            var path = new List<(double x, double z)> { (4.15, 1.3) };
            for (double z = 1.6; z <= 3.61; z += 0.25) path.Add((4.15, z));
            path.Add((4.15, 3.65)); path.Add((4.15, 3.7));
            double Floor(double x, double z, double y) => Elder.GroundAt(Elder.ApartmentWalk, x, z, y + 0.4, 1.2) ?? y;
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                ElderOn(host, ctx);
                var elder = Elder.CreateElderDilemma(ctx);
                var her = host.Person("elder");
                var p = host.Point("elder304");
                host.Queue(h => h.MoveTo(p.X - 3, p.Z, p.Y), label: "walk past 304");
                host.Queue(h => { h.MoveTo(4, -0.5, 5.6); h.RunFrame(); h.MoveTo(5, 0.9, 5.6); }, () => g.Knows("elderPick") && ctx.Engine.ControlsEnabled, "to the stairs");
                double py = 5.6;
                foreach (var (x, z) in path)
                    host.Queue(h =>
                    {
                        if (her.Z > 1.6 && her.Z < 3.7)
                        {
                            var gy = Elder.GroundAt(Elder.ApartmentWalk, her.X, her.Z, her.Y + 0.4, 1.2);
                            onStairs.Add(gy == null || Math.Abs(her.Y - gy.Value) > 0.02 ? $"off ({her.X:0.00},{her.Y:0.000},{her.Z:0.00}) ground {gy}" : "ok");
                        }
                        py = Floor(x, z, py); h.MoveTo(x, z, py); idle = 0;
                    }, () => elder.Following && idle > 40, $"step to z={z:0.00}");
                await ctx.Roam(new RoamOptions
                {
                    Tick = dt =>
                    {
                        elder.Tick(dt);
                        if (her.Anim == "walk" && her.AnimSpeed is double sp) speeds.Add(sp);
                        idle = her.Anim == "idle" ? idle + 1 : 0;
                        return idle > 40 && ctx.Engine.PlayerZ > 3.69 ? (JToken)"on the stairs" : null;
                    },
                });
            });
            Assert.That(speeds.Distinct().Count(), Is.GreaterThan(1), "setSpeed every frame: her steps slow as she closes in");
            Assert.That(speeds.Last(), Is.LessThan(speeds.Max()));
            var her = host.Person("elder");
            Assert.That(her.Z, Is.InRange(1.7, 3.6), "she is on the flight");
            Assert.That(her.Y, Is.EqualTo(Elder.GroundAt(Elder.ApartmentWalk, her.X, her.Z, her.Y + 0.4, 1.2)).Within(0.02), "she stands on the ramp");
            Assert.That(onStairs.Count, Is.GreaterThanOrEqualTo(2), string.Join("|", onStairs));
            Assert.That(onStairs.Where(s => s != "ok"), Is.Empty, "on the stairs she stands on the steps");
        }

        [Test]
        public void Retry_PutsHerBackAtHerDoor_FacingWest()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                Elder.CreateElderDilemma(ctx);
                var her = host.Person("elder");
                her.SetPosition(5, 0, 0.9, -1.0); // the follower took her down and turned her
                Elder.CreateElderDilemma(ctx);   // a checkpoint retry
            });
            var p = host.Person("elder");
            Assert.That((p.X, p.Y, p.Z), Is.EqualTo((Elder.HomeX, Elder.HomeY, Elder.HomeZ)));
            Assert.That(p.Yaw, Is.EqualTo(Math.PI / 2).Within(1e-9), "SPOT keeps ry");
            Assert.That(p.Anim, Is.EqualTo("idle"));
        }
    }
}
