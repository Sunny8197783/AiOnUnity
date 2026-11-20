// Danger (story/danger.js): port of tests/r6-fix2-danger.test.mjs (shellAim; the stranger's give rules, which the
// original checked by reading its source, are checked here by playing them on FakeStoryHost) plus the layer's rules.
// Competition upgrade: the stranger asking for food is a non-violent encounter (no punch, no chase, nobody hurt).
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DangerTests
    {
        [Test]
        public void ACloseShellNeverLandsWithin4mOfAPlayerWhoStandsWalksOrRunsStraightOn()
        {
            var rnd = new Random(42);
            Func<double> r = rnd.NextDouble;
            foreach (var v in new double[] { 0, 3, 6 })
                foreach (var yaw in new[] { 0, 1.3, -2.2 })
                {
                    int near9 = 0;
                    for (int i = 0; i < 4000; i++)
                    {
                        double px0 = 10, pz0 = -20;
                        var (x, z) = Danger.ShellAim(px0, pz0, yaw, true, r);
                        // 2.3 s later the walker has moved v * 2.3 m along the facing (yaw 0 looks toward -z)
                        double px = px0 - Math.Sin(yaw) * v * 2.3, pz = pz0 - Math.Cos(yaw) * v * 2.3, d = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                        Assert.That(d, Is.GreaterThanOrEqualTo(4), $"v {v} yaw {yaw}: {d:0.00} m");
                        if (d < 9) near9++;
                    }
                    if (v == 0) Assert.That(near9, Is.EqualTo(4000), "standing still a close shell still hurts (inside 9 m)");
                }
            for (int i = 0; i < 2000; i++)
            {
                var (x, z) = Danger.ShellAim(0, 0, 0, false, r);
                double d = Math.Sqrt(x * x + z * z);
                Assert.That(d, Is.InRange(9 - 1e-9, 32 + 1e-9));
            }
        }

        static (FakeStoryHost host, StoryContext ctx, DangerLayer layer, List<(int n, string why)> hurts) Rig(string chapter = "endure")
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(9));
            host.G.Chapter = chapter;
            var hurts = new List<(int, string)>();
            var layer = Danger.Create(ctx, (n, why) => { hurts.Add((n, why)); ctx.Stat("health", -n); });
            return (host, ctx, layer, hurts);
        }

        static ItemEntry Item(int bag, int home = 0) => new ItemEntry { Home = home, Bag = bag };

        static Newtonsoft.Json.Linq.JToken EndOnLooterEvent(GameState g) => g.Events.Any(e => e.Type == "looter") ? "end" : null;

        [Test]
        public void Looter_GiveGoesThroughConsumeBag_OnlyWhileTheBagIsOn()
        {
            var (host, ctx, layer, hurts) = Rig();
            var g = host.G;
            g.Items["water_500"] = Item(2); g.Items["bars"] = Item(1, 3); g.Items["canned"] = Item(0, 2);
            g.SetKnow("bagOn", true);
            host.AnswerSpeaker("낯선 사람", "give");
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                var roam = ctx.Roam(new RoamOptions { Doors = false, Tick = _ => EndOnLooterEvent(g) });
                layer.Trigger("looter");
                Assert.That(layer.CurrentLooter, Is.Not.Null);
                await roam;
            });
            var talk = host.Talks.Last();
            Assert.That(talk.Choices.Select(c => c.Id), Is.EqualTo(new[] { "give", "back", "neighbors", "report", "argue" }));
            Assert.That(g.Items["water_500"].Bag, Is.EqualTo(1)); Assert.That(g.Items["water_500"].Used, Is.EqualTo(1));
            Assert.That(g.Items["bars"].Bag, Is.EqualTo(0)); Assert.That(g.Items["bars"].Home, Is.EqualTo(3), "the home stock is not handed over");
            Assert.That((string)g.Events.Last(e => e.Type == "looter").Details["result"], Is.EqualTo("gave"));
            Assert.That((double)g.Extra["karma"], Is.EqualTo(1));
            Assert.That(host.Toasts, Does.Contain("먹을 것을 건넸다. 그는 고개를 숙이고 골목으로 사라졌다."));
            Assert.That(hurts, Is.Empty);

            // no bag on the back: nothing to hand over; stepping back is the calm way out
            var (h2, c2, l2, hurts2) = Rig();
            h2.G.Items["water_500"] = Item(2); h2.G.SetKnow("bagOn", false);
            h2.AnswerSpeaker("낯선 사람", "back");
            h2.Run(async () =>
            {
                await c2.Goto("town", "park");
                var roam = c2.Roam(new RoamOptions { Doors = false, Tick = _ => EndOnLooterEvent(h2.G) });
                l2.Trigger("looter");
                await roam;
            });
            Assert.That(h2.Talks.Last().Choices.Select(c => c.Id), Is.EqualTo(new[] { "back", "neighbors", "report", "argue" }));
            Assert.That(h2.G.Items["water_500"].Bag, Is.EqualTo(2));
            Assert.That((string)h2.G.Events.Last(e => e.Type == "looter").Details["result"], Is.EqualTo("escaped"));
            Assert.That(hurts2, Is.Empty);
        }

        // Competition upgrade (non-violent): every answer to the stranger is words. Nobody is hit, nobody chases, nothing
        // punches; the bad answer (argue) costs calm and, with food in the bag, one item handed over in the end.
        [TestCase("back", "escaped", 0, 4, 0)]
        [TestCase("neighbors", "escaped", 0, -2, 0)]
        [TestCase("report", "escaped", 0, -2, 0)]
        [TestCase("argue", "argued", -1, 12, 1)]
        public void Looter_EveryAnswerIsWords_NobodyIsHurt_AndTheStrangerWalksAway(string pick, string result, double karma, int tension, int lost)
        {
            var (host, ctx, layer, hurts) = Rig("prep"); // a chapter with no shelling: only the stranger, forced in
            var g = host.G;
            g.Items.Clear();
            g.Items["bars"] = Item(2); g.SetKnow("bagOn", true);
            g.Stats.Tension = 20;
            double battery = g.Phone.Battery;
            host.AnswerSpeaker("낯선 사람", pick);
            double closest = double.MaxValue;
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                var roam = ctx.Roam(new RoamOptions
                {
                    Doors = false,
                    Tick = _ =>
                    {
                        var p = layer.CurrentLooter;
                        if (p != null) closest = Math.Min(closest, Math.Sqrt((p.X - host.PlayerX) * (p.X - host.PlayerX) + (p.Z - host.PlayerZ) * (p.Z - host.PlayerZ)));
                        return host.FrameCount > 2400 ? "end" : null;
                    },
                });
                layer.Trigger("looter");
                await roam;
            });
            var ev = g.Events.Single(e => e.Type == "looter");
            Assert.That((string)ev.Details["result"], Is.EqualTo(result));
            Assert.That((string)ev.Details["how"], Is.EqualTo(pick));
            Assert.That(hurts, Is.Empty, "nobody is hurt");
            Assert.That(host.Cues, Has.No.Member("hit"), "no punch, no hit sound");
            Assert.That(host.Calls("hurt"), Is.Empty);
            Assert.That(g.Extra.ContainsKey("karma") ? (double)g.Extra["karma"] : 0, Is.EqualTo(karma));
            Assert.That(g.Stats.Tension, Is.EqualTo(20 + tension).Within(0.01));
            Assert.That(g.Items["bars"].Bag, Is.EqualTo(2 - lost));
            Assert.That(g.Phone.Battery, Is.EqualTo(pick == "report" ? battery - 2 : battery));
            if (pick == "report") Assert.That(host.Toasts, Does.Contain(ctx.Facts.Text("comms_112")), "the sourced 112 line");
            Assert.That(closest, Is.GreaterThanOrEqualTo(Danger.TalkDistance - 0.2), "he stops at a talking distance");
            Assert.That(layer.CurrentLooter, Is.Null, "he walked away and is gone");
            Assert.That(g.Events.Any(e => e.Type == "violence"), Is.False);
            var said = host.Talks.Last();
            int variant = Array.FindIndex(Danger.LooterLines, ls => ls[0] == said.Lines[0]);
            Assert.That(variant, Is.GreaterThanOrEqualTo(0));
            Assert.That(said.Role, Is.EqualTo(Danger.LooterCues[variant]), "the cue fits the line he said");
        }

        // Review fix: the empty-handed version of 'argue' never showed (it compared How with "argued"), so a player with
        // nothing in the bag was told they handed food over.
        [TestCase(false)]
        [TestCase(true)]
        public void Looter_ArgueWithNothingToHandOver_SaysSo_AndTakesNothing(bool bagOn)
        {
            var (host, ctx, layer, hurts) = Rig("prep");
            var g = host.G;
            g.Items.Clear();
            g.Items["water_500"] = Item(bagOn ? 0 : 2, 3); g.SetKnow("bagOn", bagOn);
            host.AnswerSpeaker("낯선 사람", "argue");
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                var roam = ctx.Roam(new RoamOptions { Doors = false, Tick = _ => EndOnLooterEvent(g) });
                layer.Trigger("looter");
                await roam;
            });
            Assert.That(host.Toasts, Does.Contain(Danger.ArguedEmptyLine));
            Assert.That(host.Toasts, Has.No.Member(Danger.LooterOutcome("argue").Line));
            Assert.That(g.Items["water_500"].Bag, Is.EqualTo(bagOn ? 0 : 2));
            Assert.That(g.Items["water_500"].Home, Is.EqualTo(3));
            Assert.That((string)g.Events.Single(e => e.Type == "looter").Details["result"], Is.EqualTo("argued"));
            Assert.That(hurts, Is.Empty);
        }

        // Review fix: a player who walks on is not followed across town; he gives up and walks off, no talk opens.
        [Test]
        public void Looter_GivesUpWhenThePlayerWalksOn()
        {
            var (host, ctx, layer, hurts) = Rig("prep");
            var g = host.G;
            bool talked = false;
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                double x0 = host.PlayerX, z0 = host.PlayerZ;
                var roam = ctx.Roam(new RoamOptions
                {
                    Doors = false,
                    Tick = _ =>
                    {
                        var p = layer.CurrentLooter;
                        if (p != null)
                        {
                            // walk straight away from him, a little faster than he walks
                            double dx = host.PlayerX - p.X, dz = host.PlayerZ - p.Z, d = Math.Sqrt(dx * dx + dz * dz);
                            host.PlayerX += dx / d * 1.6 / 30; host.PlayerZ += dz / d * 1.6 / 30;
                        }
                        talked |= host.Talks.Count > 0;
                        return g.Events.Any(e => e.Type == "looter") || host.FrameCount > 30 * 40 ? "end" : null;
                    },
                });
                layer.Trigger("looter");
                await roam;
            });
            Assert.That(talked, Is.False, "no talk opens");
            var ev = g.Events.Single(e => e.Type == "looter");
            Assert.That((string)ev.Details["result"], Is.EqualTo("left"));
            Assert.That(host.Toasts, Does.Contain(Danger.GaveUpLine));
            Assert.That(hurts, Is.Empty);
        }

        [Test]
        public void LooterCues_FitEachVariant()
        {
            Assert.That(Danger.LooterCues.Length, Is.EqualTo(Danger.LooterLines.Length));
            Assert.That(Danger.LooterCues[1], Is.Not.EqualTo("목소리가 날카롭다"), "the pleading variant is not sharp");
        }

        [Test]
        public void LooterChoices_AreCalm_AndReportNeedsACharge()
        {
            var with = Danger.LooterChoices(true, true);
            Assert.That(with.Select(c => c.Id), Is.EqualTo(new[] { "give", "back", "neighbors", "report", "argue" }));
            Assert.That(with.All(c => !c.Disabled && c.Why == null), Is.True, "an open choice carries no reason line");
            var flat = Danger.LooterChoices(false, false);
            Assert.That(flat.Select(c => c.Id), Is.EqualTo(new[] { "back", "neighbors", "report", "argue" }));
            Assert.That(flat.Single(c => c.Id == "report").Disabled, Is.True);
            Assert.That(flat.Single(c => c.Id == "report").Why, Is.EqualTo("휴대폰 배터리가 없다"));
            foreach (var c in with) Assert.That(c.Label, Does.Not.Contain("주먹").And.Not.Contain("맞서").And.Not.Contain("때리"), c.Id);
            foreach (var p in new[] { "give", "back", "neighbors", "report", "argue", null, "fight" })
            {
                var o = Danger.LooterOutcome(p);
                Assert.That(new[] { "gave", "escaped", "argued" }, Does.Contain(o.Result), p);
                Assert.That(o.Line, Does.Not.Contain("주먹").And.Not.Contain("쓰러").And.Not.Contain("피가"), p);
            }
            Assert.That(Danger.LooterOutcome("fight").How, Is.EqualTo("back"), "an old or unknown answer is stepping back");
            foreach (var lines in Danger.LooterLines) foreach (var l in lines) Assert.That(l, Does.Not.Contain("주먹"));
        }

        // Polish fix: the looter's body dies with the level, but the layer kept it while the player was indoors (Active() is
        // town-only) and a bodiless '낯선 사람' fought on the player's return.
        [Test]
        public void Looter_IsDroppedWhenThePlayerLeavesTheLevel_AndNeverFightsAfterTheReturn()
        {
            var (host, ctx, layer, hurts) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                layer.Trigger("looter");
                Assert.That(layer.CurrentLooter, Is.Not.Null);
                await ctx.Goto("apartment", "lobby");
                host.RunFrames(5);
                Assert.That(layer.CurrentLooter, Is.Null, "dropped while indoors");
                await ctx.Goto("town", "park");
                var roam = ctx.Roam(new RoamOptions { Doors = false, Tick = _ => host.FrameCount > 900 ? "done" : null });
                await roam;
            });
            Assert.That(layer.CurrentLooter, Is.Null);
            Assert.That(hurts.Where(h => h.why == "fight"), Is.Empty);
            Assert.That(host.Talks.Where(t => t.Speaker == "낯선 사람"), Is.Empty);
        }

        [Test]
        public void LooterBearing_IsAheadOrToTheSide_NeverBehind()
        {
            foreach (var r in new[] { 0.0, 0.25, 0.5, 0.75, 0.999 })
                Assert.That(Math.Abs(Danger.LooterBearing(1.0, r) - 1.0), Is.LessThanOrEqualTo(0.7 + 1e-9));
        }

        [Test]
        public void Blast_DamageByDistance_CrouchCutsIt_AndItIsRecorded()
        {
            var (host, ctx, layer, hurts) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                host.MoveTo(0, 0);
                layer.Trigger("blast", 3, 0);   // inside 4 m: 85
                host.Crouching = true;
                layer.Trigger("blast", 6, 0);   // inside 9 m: 40 x 0.4 = 16
                host.Crouching = false;
                layer.Trigger("blast", 30, 0);  // past 16 m: nothing
                layer.Trigger("boom", 1, 0);    // sight and sound only
            });
            Assert.That(hurts, Is.EqualTo(new[] { (85, "blast"), (16, "blast") }));
            var blasts = host.G.Events.Where(e => e.Type == "blast").Select(e => ((int)e.Details["near"], (int)e.Details["dmg"])).ToList();
            Assert.That(blasts, Is.EqualTo(new[] { (3, 85), (6, 16), (30, 0) }));
            Assert.That(host.Toasts, Does.Contain("폭풍에 밀려 바닥에 주저앉았다. 귀가 먹먹하다."));
            Assert.That(host.Toasts, Does.Contain("가까운 폭발에 몸이 휘청였다. 몸을 낮추자."));
            Assert.That(host.Toasts.Any(t => t.Contains("피가")), Is.False, "no blood on screen");
        }

        [Test]
        public void Intensity_PeaceBeforeTheStrike_AndCalmDropsWhatIsPending()
        {
            var g = TestSupport.Rules.CreateGame(1);
            g.Chapter = "dday";
            Assert.That(Danger.Intensity["dday"](g), Is.Null);
            g.SetKnow("strikeSeen", true);
            var i = Danger.Intensity["dday"](g);
            Assert.That((i.Shell, i.Collapse, i.Looter), Is.EqualTo((0.8, 0.5, 0.0)));

            // a shell whistles, then would land 2.3 s later; Calm drops what is pending
            var (host, ctx, layer, _) = Rig("evac");
            host.Run(async () =>
            {
                await ctx.Goto("town", "park");
                double t = 0;
                var roam = ctx.Roam(new RoamOptions { Doors = false, Tick = dt => (t += dt) > 1 ? "end" : null });
                layer.Trigger("shell");
                await roam;
                Assert.That(host.Toasts.Count(x => x.StartsWith("휘이")), Is.EqualTo(1));
                ctx.Calm = true;
                host.RunFrames(2);
                ctx.Calm = false;
                t = 0;
                await ctx.Roam(new RoamOptions { Doors = false, Tick = dt => (t += dt) > 3 ? "end" : null });
                Assert.That(host.G.Events.Any(e => e.Type == "blast"), Is.False, "the pending blast was dropped by Calm");
            });

            // without Calm the same shell lands
            var (h2, c2, l2, _) = Rig("evac");
            h2.Run(async () =>
            {
                await c2.Goto("town", "park");
                l2.Trigger("shell");
                double t = 0;
                await c2.Roam(new RoamOptions { Doors = false, Tick = dt => (t += dt) > 3 ? "end" : null });
            });
            Assert.That(h2.G.Events.Count(e => e.Type == "blast"), Is.EqualTo(1));
            layer.Reset(); layer.Dispose(); l2.Dispose();
        }
    }
}
