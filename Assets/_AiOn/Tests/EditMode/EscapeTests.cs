// Escape (story/escape.js): the smoke-escape part of tests/r6-fix3-systems.test.mjs, played on FakeStoryHost, plus
// the smoke itself (a standing player breathes in and is hurt; the fail is 'dead' { why: 'smoke' }).
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class EscapeTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            return (host, host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(3)));
        }

        static System.Collections.Generic.List<string> Payoffs(GameState g) =>
            g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]).ToList();

        [Test]
        public void GasValveCheckAndTheWetTowelPayOffInTheSmokeEscape()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            // what doEquip's homeGas does (PrepActions is another agent's): the event, then the flag
            ctx.Event("gas_checked"); g.SetKnow("gasValve", true);
            g.SetKnow("tubFilled", true); ctx.Event("tub_filled");
            if (!g.Items.TryGetValue("towel", out var tw) || tw.Home + tw.Bag == 0) g.Items["towel"] = new ItemEntry(1);
            host.QueuePress("homeSink");
            host.Queue(h => h.MoveTo(h.PlayerX, h.PlayerZ, 0.5), () => ctx.Roaming && host.ControlsEnabled && host.Toasts.Any(t => t == ctx.Facts.Text("fire_big")), "down to 1F");
            var res = host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                return await Escape.SmokeEscape(ctx);
            });
            Assert.That((bool)res["towel"], Is.True);
            Assert.That(Payoffs(g), Is.EqualTo(new[] { "gas", "tub", "towel" }));
            var rows = Ripples.Of(g).Select(r => r.Prep).ToList();
            Assert.That(rows, Does.Contain("gas").And.Contain("towel"), string.Join(",", rows));
            Assert.That((string)g.Events.Last(e => e.Type == "towel_wet").Details["with"], Is.EqualTo("tub"));
            var ev = g.Events.Last(e => e.Type == "smoke_escape");
            Assert.That(JToken.DeepEquals(ev.Details, res), Is.True);
            Assert.That(host.Calls("grade").Last().Data, Is.Null, "the smoke grade is cleared at the end");
        }

        [Test]
        public void TowelNeedsWater_ABottleIsUsedWhenTheTubIsDry()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Items["towel"] = new ItemEntry(1);
            g.Items["water_500"] = new ItemEntry(2);
            g.Items.Remove("water_2l");
            host.QueuePresses("homeSink", "homeSink");
            host.Queue(h => h.MoveTo(h.PlayerX, h.PlayerZ, 0.5), () => ctx.Roaming && host.ControlsEnabled && host.Toasts.Contains("젖은 수건으로 코와 입을 막고 있다."), "down");
            var res = host.Run(async () => { await ctx.Goto("apartment", "corridor3F"); return await Escape.SmokeEscape(ctx, "small"); });
            Assert.That(host.Toasts, Does.Contain("생수 한 병으로 수건을 적셨다."));
            Assert.That(g.Items["water_500"].Home, Is.EqualTo(1));
            Assert.That((string)g.Events.Last(e => e.Type == "towel_wet").Details["with"], Is.EqualTo("water_500"));
            Assert.That((bool)res["towel"], Is.True);
        }

        [Test]
        public void StayingInTheSmokeHurts_AndHealthOutIsDeadBySmoke()
        {
            var (host, ctx) = Rig();
            host.G.Stats.Health = 3;
            int grew = 0;
            var fail = host.RunExpectFail(async () => { await ctx.Goto("apartment", "corridor3F"); await Escape.SmokeEscape(ctx, "small", () => grew++); });
            Assert.That(fail.Type, Is.EqualTo("dead"));
            Assert.That((string)fail.Detail["why"], Is.EqualTo("smoke"));
            Assert.That(grew, Is.EqualTo(1), "a small fire grows after 20 s");
            Assert.That(host.Toasts, Does.Contain("연기가 목을 찌른다. 몸을 낮추자 (C)."));
            Assert.That(host.Calls("hurt").Any(), Is.True);
        }

        [Test]
        public void CrouchingAndATowelCutTheExposure()
        {
            double Breathe(bool crouch)
            {
                var (host, ctx) = Rig();
                host.Crouching = crouch;
                double t = 0;
                host.Queue(h => h.MoveTo(h.PlayerX, h.PlayerZ, 0.5), () => ctx.Roaming && (t = host.Seconds) > 10, "down after 10 s");
                var res = host.Run(async () => { await ctx.Goto("apartment", "corridor3F"); return await Escape.SmokeEscape(ctx); });
                Assert.That((bool)res["crouch"], Is.EqualTo(crouch));
                return (double)res["exposure"];
            }
            double stand = Breathe(false), low = Breathe(true);
            Assert.That(low, Is.LessThan(stand * 0.5));
        }
    }
}
