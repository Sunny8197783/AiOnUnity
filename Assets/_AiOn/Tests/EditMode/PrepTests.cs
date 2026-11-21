// Prep (story/prep.js) played through on FakeStoryHost: two different weeks of preparation, the planner's rules,
// and the town tour that must never stop the chapter. PrepGoldenTests compares the same kind of runs with the original.
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PrepTests
    {
        // 「22분」 (Prep.TwentyTwoPart) is a port addition: these runs compare the chapter with the original, so without it.
        System.Func<StoryContext, System.Threading.Tasks.Task> _twentyTwo;
        [SetUp] public void WithoutTheInterlude() { _twentyTwo = Prep.TwentyTwoPart; Prep.TwentyTwoPart = null; }
        [TearDown] public void PutTheInterludeBack() => Prep.TwentyTwoPart = _twentyTwo;

        static (FakeStoryHost host, StoryContext ctx) Rig(bool toured = true)
        {
            var host = new FakeStoryHost();
            var g = TestSupport.Rules.CreateGame(7);
            g.Day = -2; g.Minute = 18 * 60 + 30;
            if (toured) g.SetKnow("toured", true);
            g.Cond.SoldOut.Early = new List<string>(); g.Cond.SoldOut.Late = new List<string> { "water" };
            return (host, host.CreateContext(TestSupport.Rules, g));
        }

        static IEnumerable<JObject> Planners(FakeStoryHost h) => h.Opens.Where(o => o.screen == "planner").Select(o => o.options);
        static JObject Row(JObject planner, string id) => (JObject)((JArray)planner["actions"]).First(a => (string)a["id"] == id);

        [Test]
        public void WeekA_EquipTonight_ThenMartShelterNeighborsBag_ThenWaterAndTheAlbum()
        {
            var (host, ctx) = Rig();
            var g = ctx.G;
            foreach (var p in new[] { "equip", "mart", "shelter", "neighbors", "bag" }) host.AnswerOpen("planner", p);
            host.AnswerOpen("shop", JObject.Parse("{ cart: { batteries_aa: 2, radio: 1 }, total: 33000 }"));
            host.AnswerOpen("shop", JObject.Parse("{ cart: { water_2l: 4, bars: 3 }, total: 8900 }"));
            host.AnswerOpen("routemap", JObject.Parse("{ choice: 1 }"));
            host.AnswerOpen("bag", JObject.Parse("{ bag: { water_2l: 1, bars: 3, flashlight: 1, radio: 1, meds: 1, clothes: 1 }, pocket: { meds: 1 } }"));
            host.AnswerSpeaker("현관", "lift", once: false);
            host.QueuePresses("homeStock", "homeGas", "homeBreaker", "homePlanner",                       // slot 0: equipment (no batteries yet)
                "homeDesk", "homePlanner", "homeExitDoor", "town:mart", "mart:shelfElec", "mart:shelfWater", "mart:checkout", // slots 1-2
                "homePlanner", "homeExitDoor", "town:shelterEast", "town:shelterA", "town:shelterB", "town:shelterNotice",   // slot 3
                "homePlanner", "homeExitDoor", "seoyeonDoor", "elevator", "managerWindow", "apartment:lobbyExit", "town:store",
                "store:storeCounter", "store:storeExit", "town:home", "apartment:elevator", "homeDoor",                       // slot 4
                "homePlanner", "homeBag",                                                                                   // slot 5
                "homeSink", "homeBed");                                                                                     // the evening
            host.Run(() => Prep.Run(ctx));

            Assert.That(g.Chapter, Is.EqualTo("prep"));
            Assert.That((g.Day, g.Minute), Is.EqualTo((0, 19 * 60 + 30)), "D-day 19:30");
            Assert.That(g.Slots.Used, Is.EqualTo(6));
            Assert.That(g.Slots.Done, Is.EqualTo(new[] { "equip", "mart", "shelter", "neighbors", "bag" }));
            Assert.That(g.Events.Where(e => e.Type == "prep_action").Select(e => ((string)e.Details["id"], (int)e.Details["slot"])),
                Is.EqualTo(new[] { ("equip", 0), ("mart", 1), ("shelter", 3), ("neighbors", 4), ("bag", 5) }));
            var types = host.EventTypes();
            foreach (var t in new[] { "light_blocked", "gas_checked", "equip_done", "shelter_located", "bought", "shelter_checked", "neighbors_done", "bag_packed", "tub_filled", "album" })
                Assert.That(types, Does.Contain(t));
            Assert.That(types.IndexOf("equip_done"), Is.LessThan(types.IndexOf("bought")));
            Assert.That(g.Events.Where(e => e.Type == "deed").Select(e => (string)e.Details["id"]), Is.EqualTo(new[] { "mart_share", "promise_store" }));
            Assert.That(g.Knows("gasValve") && g.Knows("breaker") && g.Knows("shelterLocated") && g.Knows("routeChecked") && g.Knows("entrancesHeard") && g.Knows("bagPacked") && g.Knows("tubFilled") && g.Knows("albumSeen"));
            Assert.That(g.Rel.Values.All(r => r.Trust == 1));
            Assert.That(g.Items["radio"].Bag, Is.EqualTo(1));
            Assert.That(g.Items["meds"].Pocket, Is.EqualTo(1));
            Assert.That(g.Money, Is.EqualTo(150000 - 33000 - 8900));

            // One time skip after tonight's slot, one before D-day; the planner first as a modal, then from the calendar.
            Assert.That(host.TimeSkips.Select(s => s.to), Is.EqualTo(new[] { "D-1 목 09:00", "D-day 금 19:30" }));
            var plans = Planners(host).ToList();
            Assert.That(plans.Count, Is.EqualTo(5));
            Assert.That(plans.Select(p => (string)p["day"]), Is.EqualTo(new[] { "D-2 저녁", "D-1 오전", "D-1 오후", "D-1 오후", "D-1 저녁" }));
            Assert.That((bool)Row(plans[0], "mart")["disabled"]); Assert.That((string)Row(plans[0], "mart")["reason"], Is.EqualTo("마트는 곧 문을 닫습니다. 내일 하세요."));
            Assert.That((string)plans[0]["hint"], Is.EqualTo("오늘 저녁은 한 칸뿐입니다. 마트는 두 칸이 드는 데다 곧 문을 닫습니다."));
            Assert.That((string)plans[1]["hint"], Is.EqualTo("손전등은 있지만 건전지가 없습니다. 있다는 것과 쓸 수 있다는 것은 다릅니다."));
            Assert.That((bool)Row(plans[1], "equip")["done"] && (bool)Row(plans[1], "equip")["disabled"]);
            Assert.That((string)Row(plans[1], "equip")["reason"], Is.EqualTo("이미 했습니다."));
            Assert.That((string)plans[2]["hint"], Is.EqualTo(""), "batteries at home now");
            Assert.That((string)Row(plans[4], "mart")["reason"], Is.EqualTo("남은 칸이 1칸이라 할 수 없습니다."));
            Assert.That((string)host.Level("apartment").State["windowView"], Is.EqualTo("night"));
            // Each slot sets the view (a Goto home re-applies it too): night tonight, day tomorrow, dusk for the last slot.
            Assert.That(host.Calls("state").Where(c => c.Text.StartsWith("apartment.windowView=")).Select(c => c.Text.Substring(21)).Distinct(),
                Is.EqualTo(new[] { "\"night\"", "\"day\"", "\"dusk\"" }));
            Assert.That((string)host.Level("apartment").State["bag"], Is.EqualTo("packed"));
            Assert.That(host.Cards.Select(c => c.Title), Is.EqualTo(new[] { "103동 302호", "대피소", "서연의 재난 대비 카드", "저녁 뉴스 (가상)" }));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
            Assert.That(host.Chapter, Is.EqualTo(("1장", "준비 — 남은 시간 여섯 칸")));
        }

        [Test]
        public void WeekB_DocsTonight_PlannerClosedOnce_HomeRestSeoyeonRestEmptyBag()
        {
            var (host, ctx) = Rig();
            var g = ctx.G;
            host.Strict = true;
            foreach (var p in new JToken[] { "docs", JValue.CreateNull(), "home", "rest", "neighbors", "rest", "bag" }) host.AnswerOpen("planner", p);
            host.AnswerOpen("docs", JObject.Parse("{ card: { family: '부모님', meet: '대피소', idcopy: true }, where: 'bag' }"));
            host.AnswerOpen("bag", JObject.Parse("{ bag: {}, pocket: {} }"));
            host.AnswerCard("ok", "ok", "ok", "ok", "ok"); // 302호, rest, 서연's card, rest, the news
            host.AnswerSpeaker("베란다 수납장", "toss").AnswerSpeaker("주방", "yes");
            host.AnswerSpeaker("오서연", "both").AnswerWhen(o => o.Speaker == "오서연" && o.Choices.Any(c => c.Id == "yes"), "yes");
            host.AnswerSpeaker("이웃", "yes").AnswerSpeaker("침대", "sleep");
            host.QueuePresses("homeDesk",
                "homePlanner", "homePlanner", "homeWindow", "homeStock", "homeSink",
                "homePlanner",
                "homePlanner", "homeExitDoor", "seoyeonDoor", "apartment:homeDoor", "homePlanner",
                "homePlanner",
                "homePlanner", "homeBag",
                "homeSink", "homeBed");
            host.Run(() => Prep.Run(ctx));

            Assert.That((g.Day, g.Minute), Is.EqualTo((0, 19 * 60 + 30)));
            Assert.That(g.Slots.Done, Is.EqualTo(new[] { "docs", "home", "rest", "neighbors", "rest", "bag" }));
            Assert.That(host.EventTypes(), Is.EqualTo(new[]
            {
                "slot", "prep_action", "encounter", "docs_done",
                "slot", "prep_action", "expiry_checked", "old_bars_tossed", "tub_filled", "home_done",
                "slot", "prep_action",
                "slot", "prep_action", "deed", "gift", "neighbors_done",
                "slot", "prep_action",
                "slot", "prep_action", "encounter", "bag_packed",
            }));
            // The card went into the bag's front pocket, but the bag screen came back empty: everything is unpacked again.
            Assert.That((g.Items["contact_card"].Home, g.Items["contact_card"].Bag), Is.EqualTo((1, 0)));
            Assert.That((g.Items["doc_copies"].Home, g.Items["doc_copies"].Bag), Is.EqualTo((1, 0)));
            Assert.That((string)g.Know["meetPoint"], Is.EqualTo("생활문화센터 대피소"));
            Assert.That(g.Items["old_bars"].Owned, Is.EqualTo(0));
            Assert.That(g.Items["batteries_aa"].Home, Is.EqualTo(1));
            Assert.That((string)g.Rel["seoyeon"].Contact, Is.EqualTo("both"));
            Assert.That(g.Knows("bagPacked"), Is.False);
            Assert.That(g.Knows("albumSeen"), Is.False, "went straight to sleep");
            Assert.That(host.Toasts, Does.Contain("큰 통과 냄비에 물을 받아 두었다."), "the water was already put by");
            // Two rests: fatigue 20 +6 (the first night) -25 -25 (floored at 0) -10 = 0; tension 25 +6 -12 -12 = 7.
            Assert.That(g.Stats.Fatigue, Is.EqualTo(0)); Assert.That(g.Stats.Tension, Is.EqualTo(7));
            var plans = Planners(host).ToList();
            Assert.That(plans.Count, Is.EqualTo(7));
            Assert.That((string)plans[1]["day"], Is.EqualTo((string)plans[2]["day"]), "closing the planner keeps the slot");
            Assert.That((bool)Row(plans[5], "rest")["done"], Is.False, "rest can be picked again");
            Assert.That((string)plans[5]["hint"], Is.EqualTo(""), "서연's spare pack is at home");
            Assert.That(host.Objectives.Count(o => o.title == "D-1 오전 · 무엇을 할까"), Is.EqualTo(3), "slot 1 twice (closed planner) and slot 2");
            Assert.That(host.Cards.Select(c => c.Title), Is.EqualTo(new[] { "103동 302호", "휴식", "서연의 재난 대비 카드", "휴식", "저녁 뉴스 (가상)" }));
        }

        [Test]
        public void TheTownTourNeverStopsTheChapter()
        {
            var (host, ctx) = Rig(toured: false);
            var g = ctx.G;
            foreach (var p in new[] { "rest", "rest", "rest", "rest", "rest", "rest" }) host.AnswerOpen("planner", p);
            for (int i = 0; i < 5; i++) host.QueuePress("apartment:homePlanner");
            host.QueuePress("apartment:homeBed");
            host.AnswerSpeaker("침대", "sleep");
            host.Run(() => Prep.Run(ctx)); // ctx.Cine is not set here: whatever the tour does, the chapter goes on
            Assert.That(g.Knows("toured"));
            Assert.That(g.Slots.Done.Count, Is.EqualTo(6));
            Assert.That((g.Day, g.Minute), Is.EqualTo((0, 19 * 60 + 30)));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
        }
    }
}
