// Neighbors (story/neighbors.js): ports of the barter / knock / shelter-rumour tests of tests/story.test.mjs, plus the
// branches the original tests leave out. Each scene runs on FakeStoryHost in strict mode: every talk answer is scripted.
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class NeighborsTests
    {
        /// <summary>sceneCtx: a context for one scene, talks answered from a script (strict), in the store.</summary>
        static (FakeStoryHost host, StoryContext ctx) Scene(GameState g, params string[] answers)
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules, g);
            host.Answer(answers);
            host.Run(() => ctx.Goto("store", "entry"));
            return (host, ctx);
        }

        static int N(GameState g, string id) => g.Items.TryGetValue(id, out var e) ? e.Home + e.Bag : 0;
        static ItemEntry Bars(int n) => new ItemEntry { Home = n, Bought = n };

        [Test]
        public void Barter_TestingThePackFirstShowsItIsUsed_AndThePriceDropsToOneSnack()
        {
            var g = TestSupport.Rules.CreateGame(3); g.Items["bars"] = Bars(3);
            Assert.That(Neighbors.OfferFor(g), Is.EqualTo(new Dictionary<string, int> { ["bars"] = 3 }));
            var (host, ctx) = Scene(g, "test", "small");
            host.Run(() => Neighbors.Barter(ctx));
            Assert.That(N(g, "bars"), Is.EqualTo(2));
            Assert.That(N(g, "batteries_old"), Is.EqualTo(1));
            Assert.That(host.Talks.Any(o => o.Lines.Any(l => l.Contains("30%"))), Is.True);
            var tr = g.Events.First(e => e.Type == "trade");
            Assert.That((string)tr.Details["from"], Is.EqualTo("trader"));
            Assert.That(host.PendingTalkAnswers, Is.EqualTo(0));
            var p2 = host.Talks.Last(o => o.Choices != null);
            Assert.That(p2.Choices.Select(c => c.Id), Is.EqualTo(new[] { "small", "no" }));
            Assert.That(p2.Choices[0].Label, Is.EqualTo(Neighbors.Ro($"{TestSupport.Db.NameOf("bars")} 1{TestSupport.Db.Get("bars").unit}") + " 바꾼다"));
            Assert.That(host.Toasts.Last(), Is.EqualTo(ctx.Facts.Text("radio_batt")));
        }

        [Test]
        public void Barter_OldBarsAreReadAndDeclined_TakingWithoutATestGetsTheUsedPack()
        {
            var g = TestSupport.Rules.CreateGame(3); g.Items["bars"] = Bars(3);
            var (host, ctx) = Scene(g, "old", "take");
            host.Run(() => Neighbors.Barter(ctx));
            Assert.That(g.Events.Any(e => e.Type == "barter_old_bars"), Is.True);
            Assert.That(N(g, "old_bars"), Is.EqualTo(2), "the expired bars stay");
            Assert.That(N(g, "bars"), Is.EqualTo(0));
            Assert.That(N(g, "batteries_old"), Is.EqualTo(1));
            // the second offer no longer has the old-bars choice
            Assert.That(host.Talks.Where(o => o.Choices != null).Last().Choices.Select(c => c.Id), Is.EqualTo(new[] { "test", "take", "no" }));

            var h = TestSupport.Rules.CreateGame(3);
            var (host2, ctx2) = Scene(h, "no");
            host2.Run(() => Neighbors.Barter(ctx2));
            Assert.That(h.Events.Any(e => e.Type == "trade"), Is.False);
        }

        [Test]
        public void Barter_ChoicesAndParticles()
        {
            Assert.That(Neighbors.Ro("건빵 3봉"), Is.EqualTo("건빵 3봉으로"));
            Assert.That(Neighbors.Ro("라면 2개"), Is.EqualTo("라면 2개로"));
            Assert.That(Neighbors.Ro("물 1L"), Is.EqualTo("물 1L로"));
            Assert.That(Neighbors.Ro("통조림 1캔"), Is.EqualTo("통조림 1캔으로"));
            Assert.That(Neighbors.Ro("물 1일"), Is.EqualTo("물 1일로"), "ㄹ takes 로");
            var g = TestSupport.Rules.CreateGame(3);
            foreach (var k in new[] { "bars", "canned", "ramen", "rice_instant" }) g.Items.Remove(k);
            Assert.That(Neighbors.OfferFor(g), Is.Null);
            g.Items.Remove("flashlight");
            var (host, ctx) = Scene(g, "no");
            host.Run(() => Neighbors.Barter(ctx));
            var first = host.Talks.First(o => o.Choices != null);
            Assert.That(first.Choices.Single(c => c.Id == "test").Disabled, Is.True);
            Assert.That(first.Choices.Single(c => c.Id == "take").Disabled, Is.True);
            Assert.That(first.Choices.Single(c => c.Id == "take").Why, Is.EqualTo("바꿀 만한 먹을거리가 없다"));
            Assert.That(first.Lines[1], Is.EqualTo("먹을 거 조금이면 돼요."));
        }

        [TestCase("give", true)]
        [TestCase("info", true)]
        [TestCase("none", false)]
        public void Knock_WaterInformationOrNoAnswer_OnlyTheFirstTwoAreHelp(string pick, bool help)
        {
            var g = TestSupport.Rules.CreateGame(5);
            int before = N(g, "water_2l");
            g.Day = 1; g.Minute = 21 * 60;
            var (host, ctx) = Scene(g, pick);
            host.Run(() => Neighbors.Knock(ctx));
            Assert.That(g.Events.Any(e => e.Type == "help" && (string)e.Details["who"] == "stranger"), Is.EqualTo(help), pick);
            Assert.That(N(g, "water_2l"), Is.EqualTo(pick == "give" ? before - 1 : before));
            Assert.That(host.Talks[0].Role, Is.EqualTo("D+1 21:00"));
            Assert.That((string)g.Events.First(e => e.Type == "knock").Details["pick"], Is.EqualTo(pick));
            if (pick == "give") Assert.That((long)g.Extra["karma"], Is.EqualTo(1));
            if (pick == "none") Assert.That(host.Captions, Has.Member("잠시 뒤 발소리가 계단 쪽으로 멀어졌다."));
        }

        [Test]
        public void Knock_NoWaterDisablesGiving()
        {
            var g = TestSupport.Rules.CreateGame(5);
            g.Items.Remove("water_2l");
            var (host, ctx) = Scene(g, "info");
            host.Run(() => Neighbors.Knock(ctx));
            var give = host.Talks[0].Choices.Single(c => c.Id == "give");
            Assert.That(give.Disabled, Is.True);
            Assert.That(give.Why, Is.EqualTo("나눌 물이 없다"));
        }

        static List<(string id, string after)> DeedRows(GameState g) =>
            Deeds.Of(g).Select(d => (d.Id, d.Details?["after"]?.Type == JTokenType.String ? (string)d.Details["after"] : null)).ToList();

        [Test]
        public void StoreRumor_JudgedBySource_AndSortedIntoTheInfoLadder()
        {
            var g = TestSupport.Rules.CreateGame(9);
            g.Chapter = "endure";
            var (host, ctx) = Scene(g, "rumor", "check", "tell");
            host.Run(() => Neighbors.StoreRumor(ctx));
            Assert.That((string)g.Skill["info"].Last()["result"], Is.EqualTo("sorted"));
            Assert.That(g.Events.Any(e => e.Type == "rumor_checked"), Is.True);
            Assert.That(DeedRows(g), Is.EqualTo(new List<(string, string)> { ("rumor_checked", null), ("rumor_corrected", "check") }));
            Assert.That(host.Talks.Any(t => t.Speaker == "행정복지센터 게시판(주민의 휴대폰 사진)"), Is.True, "no radio: the board photo");

            var h = TestSupport.Rules.CreateGame(9);
            var (host2, ctx2) = Scene(h, "official", "spread", "fix");
            host2.Run(() => Neighbors.StoreRumor(ctx2));
            Assert.That((string)h.Skill["info"].Last()["result"], Is.EqualTo("misled"));
            Assert.That(h.Events.Any(e => e.Type == "rumor_spread") && h.Events.Any(e => e.Type == "rumor_believed"), Is.True);
            Assert.That(h.Knows("misled"), Is.False, "corrected the same evening");
            Assert.That(DeedRows(h), Is.EqualTo(new List<(string, string)> { ("rumor_spread", null), ("rumor_corrected", "spread") }), "told the neighbour again");
            Assert.That(host2.Toasts, Has.Member("다시 떠올려 보니, 누가 언제 확인한 말인지는 듣지 못했다."));
        }

        [Test]
        public void StoreRumor_WithARadioTheOfficialLineComesFromTheBroadcast()
        {
            var g = TestSupport.Rules.CreateGame(9);
            g.Chapter = "endure";
            g.Items["radio"] = new ItemEntry { Home = 1, Checked = true, Charge = 80 };
            var (host, ctx) = Scene(g, "rumor", "check", "no");
            host.AnswerOpen("radio", new JObject { ["official"] = true, ["noted"] = new JArray() });
            host.Run(() => Neighbors.StoreRumor(ctx));
            Assert.That(host.Opens.Single().screen, Is.EqualTo("radio"));
            Assert.That(host.Talks.Any(t => t.Speaker == "재난방송" && t.Lines[0] == "생활문화센터 대피소는 정상 운영 중입니다. 확인되지 않은 정보에 주의해 주십시오. (가상 안내)"), Is.True);
            Assert.That(Deeds.Of(g).Any(d => d.Id == "rumor_corrected"), Is.False);
            // the fact card at the end: one toast now, the other two on frames
            Assert.That(host.Toasts.Any(t => t.StartsWith("소문을 만났을 때 — ")), Is.True);
        }
    }
}
