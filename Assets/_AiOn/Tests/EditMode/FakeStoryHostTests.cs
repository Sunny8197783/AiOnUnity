// FakeStoryHost itself: level loading, answers and strict mode, player steps, stuck detection.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class FakeStoryHostTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            return (host, host.CreateContext(rules, rules.CreateGame(7)));
        }

        static List<TalkChoice> AB => new() { new("a", "A"), new("b", "B", disabled: true, why: "no"), new("c", "C") };

        [Test]
        public void EnterLoadsTheLevelJson_PointsSpawnsNpcs()
        {
            var (host, ctx) = Rig();
            host.Run(() => ctx.Goto("apartment", "lobby"));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
            Assert.That((host.PlayerX, host.PlayerY, host.PlayerZ), Is.EqualTo((4.0, 0.0, -5.4)));
            var exit = host.Point("lobbyExit");
            Assert.That(exit.Kind, Is.EqualTo("door"));
            Assert.That(host.Point("elder304").Enabled, Is.False, "json enabled:false");
            Assert.That(host.Point("homeWallet").Enabled, Is.True);
            Assert.That(host.Point("stairsWet").Desc, Is.Not.Empty);
            Assert.That(host.Person("manager").Visible, Is.True);
            Assert.That(host.Person("seoyeon").Visible, Is.False);
            Assert.That(host.TryGetSpawn("lobbyView", out _, out _, out _, out _), Is.True, "anchors count as spots");
            Assert.That(host.Npc("nobody"), Is.Null);
            Assert.Throws<FakeHostException>(() => host.Point("nope"));
            Assert.That(ReferenceEquals(host.Points, host.Level("apartment").Points), Is.True, "same objects for the life of the level");
            Assert.That(host.Calls("enter").Single().Text, Is.EqualTo("apartment:lobby"));
        }

        [Test]
        public void UnknownLevelsAreEmpty_AndStateWarningsAreKept()
        {
            var (host, ctx) = Rig();
            host.Run(() => ctx.Goto("nowhere", "x"));
            Assert.That(host.Points, Is.Empty);
            host.Run(() => ctx.Goto("town", "home"));
            ctx.SetLevelState("town", "buses", "flying");
            ctx.SetLevelState("town", "mystery", 1);
            Assert.That(host.Warnings.Count, Is.EqualTo(2), string.Join("\n", host.Warnings));
        }

        [Test]
        public void Answers_RulesBeforeQueueBeforePolicy_AndBadAnswersThrow()
        {
            var (host, ctx) = Rig();
            host.AnswerSpeaker("서연", "c").Answer("a");
            host.Run(async () =>
            {
                Assert.That(await ctx.Say("서연", "?", AB), Is.EqualTo("c"));
                Assert.That(await ctx.Say("소장", "?", AB), Is.EqualTo("a"));
                Assert.That(await ctx.Say("소장", "?", AB), Is.EqualTo("a"), "policy: first enabled");
                Assert.That(await ctx.Say("소장", "no choices"), Is.Null);
                host.TalkPolicy = o => o.Choices.Last().Id;
                Assert.That(await ctx.Say("소장", "?", AB), Is.EqualTo("c"));
                host.Answer((string)null);
                Assert.That(await ctx.Say("소장", "?", AB), Is.Null, "Answer(null) is the original's undefined");
            });
            host.Answer("b");
            var e = Assert.Throws<FakeHostException>(() => host.Run(() => ctx.Say("소장", "?", AB)));
            StringAssert.Contains("disabled", e.Message);
            host.Answer("zzz");
            e = Assert.Throws<FakeHostException>(() => host.Run(() => ctx.Say("소장", "?", AB)));
            StringAssert.Contains("not offered", e.Message);
        }

        [Test]
        public void Cards_AndOpens()
        {
            var (host, ctx) = Rig();
            var card = new CardOptions { Title = "실패", Actions = new List<CardAction> { new("retry", "다시"), new("quit", "그만", primary: true) } };
            host.Run(async () =>
            {
                Assert.That(await ctx.Card(card), Is.EqualTo("quit"), "primary by default");
                host.AnswerCard("retry");
                Assert.That(await ctx.Card(card), Is.EqualTo("retry"));
                Assert.That(await ctx.Card(new CardOptions { Title = "글" }), Is.Null);
                Assert.That(await host.Open("bag", new JObject()), Is.InstanceOf<JObject>());
                host.AnswerOpen("bag", new JObject { ["done"] = true });
                Assert.That((bool)(await ctx.Ui.Open("bag", new JObject()))["done"], Is.True);
            });
            Assert.That(host.Cards.Count, Is.EqualTo(3));
            Assert.That(host.Opens.Count, Is.EqualTo(2));
        }

        [Test]
        public void Strict_ThrowsOnEveryUnscriptedDecision()
        {
            var (host, ctx) = Rig();
            host.Strict = true;
            Assert.Throws<FakeHostException>(() => host.Run(() => ctx.Say("x", "?", AB)));
            Assert.Throws<FakeHostException>(() => host.Run(() => ctx.Card(new CardOptions { Actions = new List<CardAction> { new("ok", "확인") } })));
            Assert.Throws<FakeHostException>(() => host.Run(() => ctx.Ui.Open("radio", new JObject())));
            Assert.That(host.Run(() => ctx.Say("x", "no choices")), Is.Null, "a line with no choices is no decision");
            host.Answer("a");
            Assert.That(host.Run(() => ctx.Say("x", "?", AB)), Is.EqualTo("a"));
            Assert.That(host.ControlsEnabled, Is.True, "modal restored the controls after the throw");
        }

        [Test]
        public void QueuedStepsRunWhenTheStoryWaitsOnThePlayer()
        {
            var (host, ctx) = Rig();
            host.QueuePress("town:aptNotice").QueueWalk("apartment:homeBed").QueuePress("apartment:homeBag");
            var seen = new List<string>();
            var r = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                await ctx.Roam(new RoamOptions { On = { ["aptNotice"] = p => { seen.Add(p.Id); return Task.FromResult<JToken>("next"); } } });
                await ctx.Goto("apartment", "home");
                return await ctx.Roam(new RoamOptions
                {
                    Tick = _ => null,
                    On = { ["homeBag"] = p => { seen.Add(p.Id + "@" + host.PlayerX.ToString(System.Globalization.CultureInfo.InvariantCulture)); return Task.FromResult<JToken>("bag"); } },
                });
            });
            Assert.That((string)r, Is.EqualTo("bag"));
            Assert.That(seen, Is.EqualTo(new[] { "aptNotice", "homeBag@-6" }), "walked to the bed (x -6) before pressing the bag");
            Assert.That(host.PendingSteps, Is.EqualTo(0));
            Assert.That(host.Calls("step").Select(c => c.Text), Is.EqualTo(new[] { "press town:aptNotice", "walk apartment:homeBed", "press apartment:homeBag" }));
        }

        [Test]
        public void AStoryWaitingForeverIsReportedAsStuck()
        {
            var (host, ctx) = Rig();
            host.MaxIdleFrames = 50;
            var e = Assert.Throws<FakeHostException>(() => host.Run(async () => { await ctx.Goto("town", "home"); await ctx.Roam(); }));
            StringAssert.Contains("stuck", e.Message);
            StringAssert.Contains("roaming=True", e.Message);
            var (h2, c2) = Rig();
            h2.AutoFrame = false;
            h2.QueuePress("apartment:homeBag"); // never ready in town
            e = Assert.Throws<FakeHostException>(() => h2.Run(async () => { await c2.Goto("town", "home"); await c2.Roam(); }));
            StringAssert.Contains("press apartment:homeBag", e.Message);
        }

        [Test]
        public void RunRethrowsWhatTheStoryThrew_AndSettleOnlyWorksInsideRun()
        {
            var (host, ctx) = Rig();
            Assert.Throws<StoryFail>(() => host.Run(() => { ctx.Fail("x"); return Task.CompletedTask; }));
            Assert.Throws<InvalidOperationException>(() => host.Run(async () => { await Task.Yield(); throw new InvalidOperationException(); }));
            Assert.Throws<FakeHostException>(() => host.Settle());
            Assert.That(host.RunExpectFail(async () => { await Task.Yield(); ctx.Fail("late"); }).Type, Is.EqualTo("late"));
            Assert.Throws<FakeHostException>(() => host.RunExpectFail(() => Task.CompletedTask));
        }

        [Test]
        public void LogKeepsEveryCallInOrder()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                ctx.Toast("하나");
                await ctx.Caption("둘", "나");
                ctx.Objective("셋");
                ctx.Audio.Cue("door");
                ctx.Audio.Loop("siren", true);
                ctx.Hud.SetChapter("1장", "D-3");
            });
            var kinds = host.Log.Select(c => c.Kind).Where(k => k is "toast" or "caption" or "objective" or "cue" or "loop" or "chapter").ToList();
            Assert.That(kinds, Is.EqualTo(new[] { "toast", "caption", "objective", "cue", "loop", "chapter" }));
            Assert.That(host.Loops, Does.Contain("siren"));
            Assert.That(host.Log.Select(c => c.Seq), Is.Ordered.Ascending);
            StringAssert.Contains("toast: 하나", host.LogText());
        }
    }
}
