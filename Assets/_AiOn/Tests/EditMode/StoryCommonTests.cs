// StoryCommon (story/common.js) and World (story/world.js) on FakeStoryHost: ports of the runtime/common parts of
// tests/evac-story.test.mjs (day ledger, old bars, album, radio batteries) plus tests of our own.
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StoryCommonTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig(long seed = 11)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            return (host, host.CreateContext(rules, rules.CreateGame(seed)));
        }

        // ------------------------------------------------------------------ evac-story.test.mjs

        [Test]
        public void DayLedger_ThreePicksPlayTheDaysOutAndGiveTheTimeSkipLines_M4b()
        {
            var (host, ctx) = Rig();
            host.TalkPolicy = o => o.Choices[1].Id; // 2 L, two meals, powerbank
            host.G.Day = 3;
            var lines = host.Run(() => StoryCommon.DayLedger(ctx, 3, "home"));
            Assert.That(host.Talks.Count, Is.EqualTo(3));
            Assert.That(lines.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(lines.All(l => Regex.IsMatch(l, "[가-힣]")), Is.True);
            Assert.That(host.G.Ledger.Count, Is.EqualTo(3));
            Assert.That(host.G.Ledger.Select(r => (int)r["day"]), Is.EqualTo(new[] { 3, 4, 5 }));
            Assert.That(host.G.Items.Values.All(e => e.Home >= 0 && e.Bag >= 0), Is.True);
            var ev = host.G.Events.Last(e => e.Type == "ledger");
            Assert.That((string)ev.Details["plan"]["power"], Is.EqualTo("powerbank"));
            Assert.That((int)ev.Details["plan"]["water"], Is.EqualTo(2));
            Assert.That(host.Talks[2].Choices.Select(c => c.Id), Is.EqualTo(new[] { "phone", "powerbank", "radio" }));
            Assert.That(host.Talks[2].Choices[0].Why, Does.StartWith("지금 "), "the choice note rides in Why");
        }

        [Test]
        public void OldBars_TheStockLookWarns_EatingOneCostsALittleHealthAndCalm_NeverAFail_M3()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.Run(() => StoryCommon.StockLook(ctx));
            StringAssert.Contains("작년 가방에서 나온 것 · 기한 확인 필요", host.Talks[0].Lines[0]);
            Assert.That(g.Items["old_bars"].Home, Is.EqualTo(1), "the first choice eats one");
            Assert.That(g.Stats.Health, Is.EqualTo(96));
            StringAssert.Contains("기한", host.Toasts.Last());
            g.Items["old_bars"].Home = 0;
            Assert.That(StoryCommon.EatOldBars(ctx), Is.False);
            double t0 = g.Stats.Tension;
            host.Run(async () => { await StoryCommon.LookAlbum(ctx); await StoryCommon.LookAlbum(ctx); });
            Assert.That(g.Stats.Tension, Is.EqualTo(System.Math.Max(0, t0 - 5)), "the album calms once");
            Assert.That(g.Events.Count(e => e.Type == "album"), Is.EqualTo(1));
        }

        [Test]
        public void EachRadioListenRunsTheBatteriesDown_AFlatRadioTakesASparePack_M3()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.OpenPolicy = (screen, o) => new JObject { ["official"] = true, ["noted"] = new JArray() };
            g.Items["radio"] = new ItemEntry { Home = 1, Bought = 1 };
            g.Items["batteries_aa"] = new ItemEntry { Home = 1, Bought = 1 };
            ctx.Rules.Check(g, "radio");
            host.Run(() => StoryCommon.RadioScene(ctx, "normal"));
            Assert.That(g.Items["radio"].Charge, Is.EqualTo(100 - StoryCommon.RadioDrain));
            g.Items["radio"].Charge = StoryCommon.RadioDrain;
            host.Run(() => StoryCommon.RadioScene(ctx, "normal"));
            Assert.That(g.Items["radio"].Checked, Is.False);
            Assert.That(g.Events.Any(e => e.Type == "battery_dead"), Is.True);
            Assert.That(host.Toasts.Last(), Is.EqualTo("라디오 소리가 점점 작아지더니 꺼졌다. 건전지가 다 닳았다."));
            g.Items["batteries_old"] = new ItemEntry { Home = 1 };
            var heard = host.Run(() => StoryCommon.RadioScene(ctx, "normal"));
            Assert.That(heard, Is.Not.Null, "a used pack brings it back");
            Assert.That(g.Items["radio"].Charge, Is.EqualTo(30 - StoryCommon.RadioDrain));
            Assert.That(host.Toasts.Contains("다 닳은 건전지를 빼고 새 건전지로 갈아 끼웠다."), Is.True);
        }

        // ------------------------------------------------------------------ ours

        [Test]
        public void RadioScene_WithoutARadioOffersThePhone_NotesWhatWasHeard()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Items.Remove("radio");
            g.Phone.Battery = 50;
            host.Answer("phone");
            host.AnswerOpen("radio", new JObject { ["official"] = true, ["noted"] = new JArray(new JObject { ["text"] = "공식 안내", ["kind"] = "official" }, new JObject { ["text"] = "소문", ["kind"] = "rumor" }) });
            var heard = host.Run(() => StoryCommon.RadioScene(ctx, "blackout"));
            Assert.That(heard[0], Is.EqualTo("연무구청이 알려 드립니다. (가상 방송)"));
            Assert.That(g.Phone.Battery, Is.EqualTo(47));
            Assert.That(host.Talks[0].Lines[1], Is.EqualTo("(배터리가 닳는다 · 지금 50%)"));
            var (screen, opts) = host.Opens.Single();
            Assert.That(screen, Is.EqualTo("radio"));
            Assert.That((string)opts["device"], Is.EqualTo("phone"));
            Assert.That((double)opts["stations"][2]["freq"], Is.EqualTo(102.1));
            Assert.That((string)opts["stations"][2]["name"], Is.EqualTo("재난방송 주관방송사 라디오 (게임 속 주파수 102.1MHz)"));
            Assert.That((double)g.Know["radioFreq"], Is.EqualTo(102.1));
            Assert.That(g.Info.Select(i => (i.Text, i.Label)), Is.EquivalentTo(new[] { ("공식 안내", "official"), ("소문", "rumor") }));
            Assert.That(g.Skill["radio"].Last()["result"].ToString(), Is.EqualTo("tuned"));

            host.AnswerOpen("radio", new JObject { ["official"] = false });
            host.Answer("phone");
            Assert.That(host.Run(() => StoryCommon.RadioScene(ctx, "blackout")), Is.Null, "missed the station");
            Assert.That(g.Skill["radio"].Last()["result"].ToString(), Is.EqualTo("missed"));

            host.Answer("no");
            Assert.That(host.Run(() => StoryCommon.RadioScene(ctx, "blackout")), Is.Null);
            g.Phone.Battery = 0;
            Assert.That(host.Run(() => StoryCommon.RadioScene(ctx, "blackout")), Is.Null);
            Assert.That(host.Talks.Last().Lines[0], Is.EqualTo("라디오가 없고, 휴대폰도 꺼져 있다."));
        }

        [Test]
        public void RadioScene_WithoutBatteriesIsBlocked()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Items["radio"] = new ItemEntry { Home = 1 };
            foreach (var p in GameRules.BatteryPacks) g.Items.Remove(p);
            g.Phone.Battery = 0;
            Assert.That(host.Run(() => StoryCommon.RadioScene(ctx, "normal")), Is.Null);
            Assert.That(host.Talks[0].Lines[0], Is.EqualTo("건전지가 없어 켜지지 않는다."));
            Assert.That(g.Events.Any(e => e.Type == "radio_blocked" && (string)e.Details["script"] == "normal"), Is.True);
        }

        [Test]
        public void CurrentBroadcast_FollowsTheChapter_AndEvacNamesTodaysClosures()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Chapter = "dday"; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("normal"));
            GameRules.RecordEvent(g, "blackout"); Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("blackout"));
            GameRules.RecordEvent(g, "alert"); Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("shelter"));
            g.Chapter = "endure"; g.Day = 1; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("water"));
            g.Day = 2; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("rumor"));
            g.Chapter = "crossroads"; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("crossroads"));
            g.Chapter = "ending"; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("evac"));
            g.Chapter = "prep"; Assert.That(StoryCommon.CurrentBroadcast(g), Is.EqualTo("normal"));
            var evac = StoryCommon.BroadcastLines(ctx, "evac");
            Assert.That(g.Cond.Closures.Evac.Count, Is.GreaterThan(0));
            foreach (var id in g.Cond.Closures.Evac) StringAssert.Contains(TownMap.Closures[id].Label.Replace(" 통제", ""), evac[2]);
            StringAssert.DoesNotContain(" 통제,", evac[2]);
            Assert.That(StoryCommon.BroadcastLines(ctx, "shelter")[2], Is.EqualTo(ctx.Facts.Text("radio_listen")));
        }

        [Test]
        public void LightSource_FlashlightThenPhoneThenNothing()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Items["flashlight"] = new ItemEntry { Home = 1, Checked = true };
            Assert.That(StoryCommon.LightSource(ctx), Is.EqualTo("flashlight"));
            g.Items["flashlight"].Checked = false;
            g.Phone.Battery = 10;
            Assert.That(StoryCommon.LightSource(ctx), Is.EqualTo("phone"));
            g.Phone.Battery = 0;
            Assert.That(StoryCommon.LightSource(ctx), Is.Null);
        }

        [Test]
        public void EmergencyText_ShowsTheAlert_OrASiteToastWhenThePhoneIsDead()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.Run(() => StoryCommon.EmergencyText(ctx, "대피하십시오.", "20:06"));
            Assert.That(host.Cues, Is.EqualTo(new[] { "phoneAlert" }));
            Assert.That((string)((JObject)host.Calls("cue").Single().Data)["kind"], Is.EqualTo("emergency"));
            Assert.That(host.Opens.Single().screen, Is.EqualTo("emergencyAlert"));
            Assert.That((string)host.Opens.Single().options["text"], Is.EqualTo("대피하십시오."));
            Assert.That(g.Info.Last().Label, Is.EqualTo("official"));
            Assert.That(host.ControlsEnabled, Is.True);
            host.Run(() => StoryCommon.EmergencyText(ctx, "안전 안내", null, "안전안내문자"));
            Assert.That((string)((JObject)host.Calls("cue").Last().Data)["kind"], Is.EqualTo("safety"));
            g.Phone.Battery = 0;
            host.Run(() => StoryCommon.EmergencyText(ctx, "못 봄"));
            Assert.That(host.Toasts.Last(), Is.EqualTo("휴대폰이 꺼져 있어 문자를 볼 수 없다. 주변 안내를 따른다."));
            Assert.That(host.ToastLabels.Last(), Is.EqualTo("site"));
            Assert.That(g.Events.Count(e => e.Type == "sms"), Is.EqualTo(3));
        }

        [Test]
        public void FactCard_NotesEveryFact_AndToastsThemOneAfterAnother()
        {
            var (host, ctx) = Rig();
            string a = ctx.Facts.Text("radio_batt"), b = ctx.Facts.Text("water_point");
            host.Run(() => StoryCommon.FactCard(ctx, "안내", new[] { "radio_batt", "nope", "water_point" }));
            Assert.That(host.Toasts, Is.EqualTo(new[] { $"안내 — {a}" }));
            host.RunFrames(80);
            Assert.That(host.Toasts, Is.EqualTo(new[] { $"안내 — {a}", b }));
            Assert.That(host.G.Info.Select(i => i.Text), Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void World_DescriptionsFollowPowerAndDay()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                var p = ctx.Roam(new StoryRoamHelper().Until("homeBed"));
                await host.PressAsync("homeSink");
                Assert.That(host.Talks.Last().Lines, Is.EqualTo(new[] { "설거지가 조금 남아 있다. 물은 잘 나온다." }));
                host.G.Day = 1;
                await host.PressAsync("homeSink");
                Assert.That(host.Talks.Last().Lines, Is.EqualTo(new[] { "수도꼭지를 돌려도 물이 나오지 않는다. 단수다." }));
                ctx.Power = false;
                await host.PressAsync("homeFridge");
                Assert.That(host.Talks.Last().Lines, Is.EqualTo(new[] { "냉장고가 조용하다. 문을 자주 열면 안의 냉기가 빨리 빠진다.", ctx.Facts.Text("food_door") }));
                await host.PressAsync("homeBed");
                await p;
            });
            Assert.That(host.Talks.All(t => t.Role == "살펴보기"), Is.True);
        }

        [Test]
        public void World_VendingNeedsPowerAndMoney()
        {
            var (host, ctx) = Rig();
            host.Answer("buy");
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var p = ctx.Roam(new StoryRoamHelper().Until("aptNotice"));
                long m = host.G.Money;
                await host.PressAsync("vending1");
                Assert.That(host.G.Money, Is.EqualTo(m - 1000));
                Assert.That(host.Toasts.Last(), Is.EqualTo("생수 한 병을 샀다."));
                ctx.Power = false;
                await host.PressAsync("vending1");
                Assert.That(host.Talks.Last().Role, Is.EqualTo("전원 꺼짐"));
                await host.PressAsync(new StoryPoint { Id = "atmX", Kind = "atm", Label = "ATM" });
                Assert.That(host.Talks.Last().Role, Is.EqualTo("화면 꺼짐"));
                await host.PressAsync("busStop");
                Assert.That(host.Talks.Last().Lines[0], Is.EqualTo("도착 안내 전광판이 꺼져 있다. 기둥에 종이가 붙어 있다."));
                await host.PressAsync("aptNotice");
                await p;
            });
            Assert.That(host.G.Items["water_500"].Bought, Is.GreaterThanOrEqualTo(1));
        }
    }

    /// <summary>Tiny helper: a roam that ends when a given point is pressed.</summary>
    sealed class StoryRoamHelper
    {
        public RoamOptions Until(string pointId) =>
            new RoamOptions { On = { [pointId] = _ => System.Threading.Tasks.Task.FromResult<JToken>("end") } };
    }
}
