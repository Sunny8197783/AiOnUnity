// Danger (story/danger.js): port of tests/r6-fix2-danger.test.mjs (shellAim; the looter's give/loot rules, which the
// original checked by reading its source, are checked here by playing them on FakeStoryHost) plus the layer's rules.
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
            var (host, ctx, layer, _) = Rig();
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
                Assert.That(layer.IsHostile(layer.CurrentLooter), Is.True);
                await roam;
            });
            var talk = host.Talks.Last();
            Assert.That(talk.Choices.Select(c => c.Id), Is.EqualTo(new[] { "give", "refuse", "run" }));
            Assert.That(g.Items["water_500"].Bag, Is.EqualTo(1)); Assert.That(g.Items["water_500"].Used, Is.EqualTo(1));
            Assert.That(g.Items["bars"].Bag, Is.EqualTo(0)); Assert.That(g.Items["bars"].Home, Is.EqualTo(3), "the home stock is not handed over");
            Assert.That((string)g.Events.Last(e => e.Type == "looter").Details["result"], Is.EqualTo("gave"));
            Assert.That((double)g.Extra["karma"], Is.EqualTo(1));
            Assert.That(host.Toasts, Does.Contain("먹을 것을 건넸다. 그는 말없이 골목으로 사라졌다."));

            // no bag on the back: nothing to hand over
            var (h2, c2, l2, _) = Rig();
            h2.G.Items["water_500"] = Item(2); h2.G.SetKnow("bagOn", false);
            h2.AnswerSpeaker("낯선 사람", "run");
            h2.Run(async () =>
            {
                await c2.Goto("town", "park");
                var roam = c2.Roam(new RoamOptions { Doors = false, Tick = _ => EndOnLooterEvent(h2.G) });
                l2.Trigger("looter");
                // the chase gives up once the player is far enough away
                h2.Queue(h => h.MoveTo(h.PlayerX + 40, h.PlayerZ), () => c2.Roaming && h2.ControlsEnabled && h2.Talks.Count > 0, "run away");
                await roam;
            });
            Assert.That(h2.Talks.Last().Choices.Select(c => c.Id), Is.EqualTo(new[] { "refuse", "run" }));
            Assert.That(h2.G.Items["water_500"].Bag, Is.EqualTo(2));
            Assert.That((string)h2.G.Events.Last(e => e.Type == "looter").Details["result"], Is.EqualTo("escaped"));
        }

        [Test]
        public void Looter_KnockedDown_DropsLootWhereTheBagIs()
        {
            foreach (var bagOn in new[] { true, false })
            {
                var (host, ctx, layer, _) = Rig();
                var g = host.G;
                g.Items.Remove("canned"); g.SetKnow("bagOn", bagOn);
                host.AnswerSpeaker("낯선 사람", "refuse");
                host.Run(async () =>
                {
                    await ctx.Goto("town", "park");
                    var roam = ctx.Roam(new RoamOptions { Doors = false, Tick = _ => EndOnLooterEvent(g) });
                    layer.Trigger("looter");
                    host.Queue(h => { layer.HitLooter(layer.CurrentLooter, true); layer.HitLooter(layer.CurrentLooter, true); },
                        () => ctx.Roaming && host.ControlsEnabled && host.Talks.Count > 0, "fight back");
                    await roam;
                });
                Assert.That((string)g.Events.Last(e => e.Type == "looter").Details["result"], Is.EqualTo("fought"));
                Assert.That(bagOn ? g.Items["canned"].Bag : g.Items["canned"].Home, Is.EqualTo(1));
                Assert.That(bagOn ? g.Items["canned"].Home : g.Items["canned"].Bag, Is.EqualTo(0));
                Assert.That(host.Toasts.Any(t => t.StartsWith("“좋게 말할 때 내놨어야지.”")), Is.True);
            }
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
            Assert.That(host.Toasts, Does.Contain("폭풍에 날아가 바닥을 굴렀다. 귀가 먹먹하다."));
            Assert.That(host.Toasts, Does.Contain("파편이 스쳤다. 피가 흐른다."));
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
