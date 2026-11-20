// PrepActions (story/prep-actions.js) on FakeStoryHost: the ported prep parts of evac-story.test.mjs ('집 안 대비')
// and r6-fix3-systems.test.mjs (empty contact card, gas valve), plus each action played once with scripted answers.
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PrepActionsTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig(long seed = 11)
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(seed));
            return (host, ctx);
        }

        static ItemEntry Item(int home, int bought = 0) => new ItemEntry { Home = home, Bought = bought };
        static List<string> Types(FakeStoryHost h) => h.EventTypes();

        // ---------------------------------------------------------------- evac-story.test.mjs (M4a)

        [Test]
        public void HomeAction_CostsOneSlot_AndTheCurtainNeverReachesTheBagList()
        {
            Assert.That(Prep.Actions.Any(a => a.Id == "home" && a.Cost == 1));
            var (_, ctx) = Rig();
            ctx.G.Items["blackout_curtain"] = Item(1, 1);
            Assert.That(PrepActions.BagItems(ctx).Any(i => i.Id == "blackout_curtain"), Is.False);
            Assert.That(PrepActions.BagItems(ctx).Any(i => i.Id == "water_2l"), Is.True);
        }

        [Test]
        public void DoHome_CurtainHungAndTaped_WaterPutBy_DatesRead()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            g.Items["blackout_curtain"] = Item(1, 1);
            g.Items["tape"] = Item(1, 1);
            host.QueuePresses("homeWindow", "homeSink", "homeStock"); // the first choice each time: tape, fill, toss
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoHome(ctx); });
            Assert.That(g.Knows("curtain") && g.Knows("curtainSealed"));
            Assert.That((string)ctx.LevelStates["apartment"]["curtain"], Is.EqualTo("sealed"));
            Assert.That((string)host.Level("apartment").State["curtain"], Is.EqualTo("sealed"));
            Assert.That(g.Events.Any(e => e.Type == "home_ready" && (bool)e.Details["sealed"]));
            Assert.That(g.Knows("tubFilled") && g.Knows("expiryChecked"));
            Assert.That(g.Items["old_bars"].Home, Is.EqualTo(0), "the first choice tosses the expired snack");
            Assert.That(Types(host), Is.EqualTo(new[] { "home_ready", "tub_filled", "expiry_checked", "old_bars_tossed", "home_done" }));
            var done = g.Events.Last().Details;
            Assert.That((bool)done["window"] && (bool)done["water"] && (bool)done["stock"] && (bool)done["curtain"] && (bool)done["sealed"]);
            Assert.That(g.Items["blackout_curtain"].Used, Is.EqualTo(1)); Assert.That(g.Items["tape"].Used, Is.EqualTo(1));
            Assert.That(host.Toasts, Does.Contain(ctx.FactLine("home_tub")));
            Assert.That(host.PendingSteps, Is.EqualTo(0));
        }

        [Test]
        public void DoHome_NoCurtain_JustLooks_KeepsTheBars_EndsAtThePlanner()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            host.AnswerSpeaker("베란다 수납장", "keep");
            host.QueuePresses("homeWindow", "homeStock", "homeWindow", "homePlanner");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoHome(ctx); });
            Assert.That(g.Knows("curtain"), Is.False);
            Assert.That(g.Items["old_bars"].Home, Is.EqualTo(2));
            Assert.That(host.Toasts, Does.Contain("창은 살펴보았다."));
            Assert.That(Types(host), Is.EqualTo(new[] { "expiry_checked", "home_done" }));
            Assert.That(host.Objectives[0].steps[0].Text, Is.EqualTo("거실 창: 불빛이 새는지 살피기"));
            Assert.That(host.Talks.Any(t => t.Lines[0] == "안쪽에서 작년 비상가방에 넣어 두었던 건빵 2봉이 나왔다. 날짜를 보니 기한이 한참 지났다."));
        }

        // ---------------------------------------------------------------- r6-fix3-systems.test.mjs

        [Test]
        public void EmptyContactCard_NothingTickedOrEsc_IsNotAWrittenCard()
        {
            foreach (var res in new JToken[] { null, JObject.Parse("{ card: {}, where: 'bag' }") })
            {
                var (host, ctx) = Rig(3);
                host.AnswerOpen("docs", res).QueuePress("homeDesk");
                host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoDocs(ctx); });
                var g = ctx.G;
                Assert.That(g.Knows("contactsCard"), Is.False);
                Assert.That(!g.Items.ContainsKey("contact_card") || g.Items["contact_card"].Owned == 0, "no card item");
                Assert.That(g.Events.Any(e => e.Type == "docs_done"), Is.False);
                Assert.That(host.Toasts, Does.Contain("아무것도 적지 않았다."));
                Assert.That((string)g.Skill["docs"].Last()["result"], Is.EqualTo("skipped"));
            }
            {
                var (host, ctx) = Rig(3);
                host.AnswerOpen("docs", JObject.Parse("{ card: { family: '부모님' }, where: 'bag' }")).QueuePress("homeDesk");
                host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoDocs(ctx); });
                var g = ctx.G;
                Assert.That(g.Knows("contactsCard"));
                Assert.That(g.Items["contact_card"].Bag, Is.EqualTo(1));
                Assert.That(g.Items["contact_card"].Checked);
                Assert.That(g.Knows("docsCopied"), Is.False);
                Assert.That(host.Toasts.Last(), Is.EqualTo("비상연락카드를 배낭 앞주머니에 넣었다."));
            }
        }

        [Test]
        public void DoDocs_IdCopyAndMeetingPoint_HomeDrawer()
        {
            var (host, ctx) = Rig(3);
            host.AnswerOpen("docs", JObject.Parse("{ card: { me: 1, idcopy: 1, meet: 1 }, where: 'home' }")).QueuePress("homeDesk");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoDocs(ctx); });
            var g = ctx.G;
            Assert.That(g.Items["contact_card"].Home, Is.EqualTo(1));
            Assert.That(g.Items["doc_copies"].Home, Is.EqualTo(1));
            Assert.That(g.Knows("docsCopied"));
            Assert.That((string)g.Know["meetPoint"], Is.EqualTo("생활문화센터 대피소"));
            Assert.That(g.Know["cardFields"].ToObject<string[]>(), Is.EqualTo(new[] { "me", "idcopy", "meet" }));
            var opts = host.Opens.Single(o => o.screen == "docs").options;
            Assert.That(((JArray)opts["fields"]).Select(f => (string)f["id"]), Is.EqualTo(new[] { "me", "blood", "meds", "family", "work", "neighbors", "agencies", "meet", "idcopy" }));
            Assert.That((string)opts["fields"][0]["why"], Is.EqualTo(ctx.Facts.Text("contact_fields")));
            var ev = g.Events.Single(e => e.Type == "docs_done").Details;
            Assert.That((string)ev["where"], Is.EqualTo("home"));
            Assert.That(host.Toasts.Last(), Is.EqualTo("비상연락카드를 책상 서랍에 넣었다."));
        }

        [Test]
        public void GasValveCheck_IsRecordedOnce()
        {
            var (host, ctx) = Rig(3);
            host.QueuePresses("homeGas", "homeGas", "homePlanner"); // the planner's first choice: 마친다
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoEquip(ctx); });
            var g = ctx.G;
            Assert.That(g.Knows("gasValve"));
            Assert.That(Types(host), Is.EqualTo(new[] { "gas_checked", "equip_done" }));
            var done = g.Events.Last().Details;
            Assert.That((bool)done["gas"]); Assert.That((bool)done["radio"], Is.False);
            Assert.That(host.Talks.First(t => t.Speaker == "가스 중간 밸브").Lines[1], Is.EqualTo(ctx.Facts.Text("alert_caution")));
        }

        // ---------------------------------------------------------------- each action once

        [Test]
        public void DoEquip_EverythingChecked_WithBatteriesAndARadio()
        {
            var (host, ctx) = Rig(5);
            var g = ctx.G;
            g.Items["radio"] = Item(1, 1);
            g.Items["batteries_aa"] = Item(2, 2);
            host.AnswerOpen("radio", JObject.Parse("{ official: true, noted: [] }"));
            host.QueuePresses("homeRadio", "homeRadio", "homeStock", "homeDesk", "extinguisher", "homeGas", "homeBreaker", "homePlanner");
            // the original's equip list (the competition gauge check before the practice is HeldExtinguisherTests')
            var gauge = ExtinguisherScene.GaugePart;
            ExtinguisherScene.GaugePart = null;
            try { host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoEquip(ctx); }); }
            finally { ExtinguisherScene.GaugePart = gauge; }
            Assert.That(g.Items["radio"].Checked && g.Items["flashlight"].Checked);
            Assert.That(g.Items["batteries_aa"].Home, Is.EqualTo(0));
            Assert.That((double)g.Know["radioFreq"], Is.EqualTo(102.1));
            Assert.That(g.Knows("radioChecked") && g.Knows("extinguisherLocated") && g.Knows("breaker"));
            Assert.That(g.Phone.Battery, Is.EqualTo(100));
            Assert.That((string)g.Skill["radio"][0]["result"], Is.EqualTo("tuned"));
            Assert.That((string)g.Skill["extinguisher"][0]["result"], Is.EqualTo("practice"));
            Assert.That(Types(host), Is.EqualTo(new[] { "check", "encounter", "check", "encounter", "gas_checked", "equip_done" }));
            var done = (JObject)g.Events.Last().Details;
            Assert.That(done.Properties().Select(p => p.Name), Is.EqualTo(new[] { "radio", "light", "power", "ext", "gas", "breaker" }));
            Assert.That(done.Properties().All(p => (bool)p.Value));
            var radio = host.Opens.Single(o => o.screen == "radio").options;
            Assert.That((string)radio["stations"][1]["name"], Is.EqualTo("재난방송 주관방송사 라디오 (게임 속 주파수 102.1MHz)"));
            Assert.That(host.Opens.Single(o => o.screen == "extinguisher").options.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"mode\":\"guided\",\"fire\":\"small\",\"practice\":true}"));
            Assert.That(host.Toasts, Does.Contain("라디오는 점검을 마쳤다."));
            Assert.That(host.Toasts, Does.Contain("메모: 라디오 점검 완료. 재난방송 주파수 102.1MHz에 맞춰 두었다."));
            Assert.That(host.Toasts, Does.Contain("보조배터리가 없다. 휴대폰을 완충해 두는 수밖에 없다."));
            Assert.That(host.Objectives.Last().steps.All(s => s.Done));
        }

        [Test]
        public void DoEquip_NoBatteries_RadioMissingAndLightBlocked()
        {
            var (host, ctx) = Rig(5);
            host.QueuePresses("homeRadio", "homeStock", "homePlanner");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoEquip(ctx); });
            Assert.That(Types(host), Is.EqualTo(new[] { "radio_missing", "light_blocked", "equip_done" }));
            var done = host.G.Events.Last().Details;
            Assert.That((bool)done["radio"]); Assert.That((bool)done["light"], Is.False);
        }

        [Test]
        public void DoMart_SoldOutSection_WaterCap_Hoard_Share_HeavyWalkHome()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            g.Cond.SoldOut.Early = new List<string> { "elec" };
            g.Rel["clerk"].Contact = "store";
            host.AnswerOpen("shop", JObject.Parse("{ cart: { radio: 1 }, total: 24000 }"));          // elec: sold out
            host.AnswerOpen("shop", JObject.Parse("{ cart: { water_2l: 9 }, total: 9900 }"));        // capped at 6
            host.AnswerOpen("shop", JObject.Parse("{ cart: { ramen: 10, canned: 0 }, total: 9000 }")); // more than 8: hoarding
            host.AnswerSpeaker("ATM", "1");
            host.QueuePresses("homeExitDoor", "town:mart", "mart:checkout", "mart:shelfElec", "mart:shelfWater", "mart:shelfFood", "mart:atm", "mart:martExit", "mart:checkout");
            int min0 = 0;
            host.Run(async () => { await ctx.Goto("apartment", "home"); min0 = g.Day * 1440 + g.Minute; await PrepActions.DoMart(ctx, 1); });
            Assert.That((string)host.Level("mart").State["crowd"], Is.EqualTo("busy"));
            Assert.That(host.Level("mart").State["soldOut"].ToObject<string[]>(), Is.EqualTo(new[] { "elec" }));
            var elec = host.Opens.First(o => o.screen == "shop").options;
            Assert.That((string)elec["title"], Is.EqualTo("전지·전기용품"));
            Assert.That(((JArray)elec["items"]).All(i => (string)i["stock"] == "out"));
            Assert.That((string)elec["items"][0]["note"], Is.EqualTo("건전지 별매"));
            Assert.That((string)elec["budgetNote"], Is.EqualTo("남은 돈 150,000원"));
            Assert.That(((JArray)elec["info"]).Count, Is.EqualTo(2), "수아's word about batteries once she is a contact");
            Assert.That(host.Toasts, Does.Contain("아직 고른 물건이 없습니다."));
            Assert.That(host.Toasts, Does.Contain("담을 수 있는 물건이 없습니다."));
            Assert.That(host.Toasts, Does.Contain("계산대에서 계산을 마치세요."));
            Assert.That(host.Toasts, Does.Contain("현금 5만 원을 찾았다."));
            Assert.That(host.Toasts, Does.Contain(ctx.Facts.Text("home_nohoard")));
            Assert.That(host.Toasts, Does.Contain("줄 선 이웃들이 빈 진열대와 내 바구니를 번갈아 본다. 사재기하지 않기."));
            Assert.That(g.Rel["clerk"].Trust, Is.EqualTo(-1), "사재기 lowers the clerk's trust");
            Assert.That(host.Toasts, Does.Contain("무거워서 두 번에 나눠 옮겼다."));
            Assert.That(g.Items["water_2l"].Bought, Is.EqualTo(6));
            Assert.That(g.Items["water_2l"].Home, Is.EqualTo(2 + 6 - 1), "one shared at the till");
            Assert.That(g.Items["ramen"].Home, Is.EqualTo(15));
            Assert.That(g.Items["cash"].Home, Is.EqualTo(1));
            Assert.That(g.Money, Is.EqualTo(150000 - 6600 - 9000 - 50000));
            Assert.That(Types(host), Is.EqualTo(new[] { "buy", "bought", "buy", "bought", "buy", "cash", "hoard", "deed" }));
            Assert.That(g.Events.First(e => e.Type == "bought").Details.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"cart\":{\"water_2l\":6},\"slot\":1}"));
            var paid = host.Talks.Single(t => t.Speaker == "계산 완료");
            Assert.That(paid.Role, Is.EqualTo("남은 돈 84,400원"));
            Assert.That(paid.Lines[0], Is.EqualTo("산 물건 무게 약 13.5kg. 한 번에 들고 가기 버겁다."));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
            Assert.That(g.Day * 1440 + g.Minute - min0, Is.EqualTo(6 + 11), "the walk home: 6 min + round((13.5 - 6) * 1.5)");
        }

        [Test]
        public void DoMart_LeaveWithoutBuying()
        {
            var (host, ctx) = Rig(11);
            host.AnswerSpeaker("현관", "hall");
            host.QueuePresses("homeExitDoor", "elevator", "apartment:lobbyExit");
            host.Queue(h => h.RunFrame(), label: "a frame in town: goOut's tick ends its roam");
            host.QueuePresses("town:mart", "mart:martExit");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoMart(ctx, 0); });
            Assert.That(Types(host), Is.Empty);
            Assert.That(host.Level("mart").State["soldOut"].ToObject<string[]>(), Is.Empty, "nothing sold out the first evening");
            Assert.That(host.Talks.Any(t => t.Speaker == "연무마트" && t.Lines[0] == "아무것도 사지 않고 나갈까?"));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
        }

        [Test]
        public void DoShelterCheck_ThreeEntrancesAndTheBoard_PlanARoute()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            host.AnswerOpen("routemap", JObject.Parse("{ choice: 1 }"));
            host.QueuePresses("homeExitDoor", "town:shelterNotice", "town:shelterEast", "town:shelterC", "town:shelterC", "town:shelterA");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoShelterCheck(ctx); });
            Assert.That(g.Knows("shelterLocated") && g.Knows("entrancesChecked") && g.Knows("routeChecked"));
            var map = host.Opens.Single(o => o.screen == "routemap").options;
            Assert.That((string)map["mode"], Is.EqualTo("guided"));
            var options = (JArray)map["options"];
            Assert.That(g.Knows("altRoute"), Is.EqualTo(options.Count > 1));
            Assert.That(JToken.DeepEquals(g.Know["plannedRoute"], options[1]["nodes"]));
            var ev = g.Events.Single(e => e.Type == "shelter_checked").Details;
            Assert.That(ev["seen"].ToObject<string[]>(), Is.EqualTo(new[] { "shelterEast", "shelterC", "shelterA" }));
            Assert.That((bool)ev["board"]);
            Assert.That(g.Info.Count(i => i.Place == "culture" && i.Label == "site"), Is.EqualTo(4));
            Assert.That(g.Info.Last().Text, Is.EqualTo("집 → 대피소 경로를 확인했다. 막히면 돌아갈 길도 지도에서 봐 두었다."));
            Assert.That((string)g.Skill["route"][0]["result"], Is.EqualTo("planned"));
            Assert.That(host.Objectives.Last().steps[0].Text, Is.EqualTo("출입구 살펴보기 (3/4)"));
        }

        [Test]
        public void DoNeighbors_AllThreeAndTheElder_ThroughTheLevels()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            host.AnswerSpeaker("오서연", "both").AnswerWhen(o => o.Speaker == "오서연" && o.Choices?.Any(c => c.Id == "yes") == true, "yes");
            host.AnswerWhen(o => o.Speaker == "이수아" && o.Choices != null, "watch");
            host.QueuePresses("homeExitDoor", "seoyeonDoor", "seoyeonDoor", "elderDoor", "elevator", "managerWindow", "apartment:lobbyExit",
                "town:store", "store:storeCounter", "store:storeExit", "town:home", "apartment:elevator", "homeDoor");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoNeighbors(ctx); });
            Assert.That((string)g.Rel["seoyeon"].Contact, Is.EqualTo("both"));
            Assert.That((string)g.Rel["manager"].Contact, Is.EqualTo("office"));
            Assert.That((string)g.Rel["clerk"].Contact, Is.EqualTo("store"));
            Assert.That(g.Rel.Values.All(r => r.Trust == 1));
            Assert.That(g.Knows("entrancesHeard"));
            Assert.That(g.Items["batteries_aa"].Home, Is.EqualTo(1), "서연's spare pack");
            Assert.That(Types(host), Is.EqualTo(new[] { "deed", "gift", "deed", "deed", "neighbors_done" }));
            Assert.That(g.Events.Where(e => e.Type == "deed").Select(e => (string)e.Details["id"]), Is.EqualTo(new[] { "promise_note", "elder_met", "promise_store" }));
            Assert.That(g.Events.Last().Details.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"seoyeon\":true,\"manager\":true,\"clerk\":true}"));
            Assert.That(host.Toasts, Does.Contain("\"내일 봬요.\""));
            Assert.That(host.Cards.Single().Body, Is.EqualTo(PrepActions.SeoyeonCard.Concat(new[] { ctx.Facts.Text("people_card") }).ToList()));
            Assert.That((string)host.Level("apartment").State["seoyeonDoor"], Is.EqualTo("closed"));
            Assert.That(g.Info.Select(i => i.Place), Is.EqualTo(new[] { "apartment", "apartment", "store" }));
            Assert.That(host.Objectives.Last().steps.All(s => s.Done));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
        }

        [Test]
        public void DoNeighbors_StopAtThePlanner_WithBatteriesAtHome()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            g.Items["batteries_aa"] = Item(1, 1);
            host.QueuePresses("homeExitDoor", "seoyeonDoor", "apartment:homeDoor", "homePlanner");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoNeighbors(ctx); });
            Assert.That((string)g.Rel["seoyeon"].Contact, Is.EqualTo("sms"));
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.StartsWith("건전지 있으세요?"))), Is.False);
            Assert.That(Types(host), Is.EqualTo(new[] { "neighbors_done" }));
            Assert.That(g.Events.Last().Details.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"seoyeon\":true,\"manager\":false,\"clerk\":false}"));
        }

        [Test]
        public void DoBag_AppliesTheScreen_PocketAndGoals()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            g.Items["blackout_curtain"] = Item(1, 1);
            host.AnswerOpen("bag", JObject.Parse("{ bag: { water_2l: 1, canned: 1, meds: 1, clothes: 1, flashlight: 1 }, pocket: { meds: 1, canned: 1 } }"));
            host.QueuePress("homeBag");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoBag(ctx); });
            var opts = host.Opens.Single(o => o.screen == "bag").options;
            Assert.That((string)opts["mode"], Is.EqualTo("guided"));
            Assert.That((string)opts["context"], Is.EqualTo("alert"));
            Assert.That(((JArray)opts["items"]).Any(i => (string)i["id"] == "blackout_curtain"), Is.False);
            Assert.That((string)((JArray)opts["items"]).First(i => (string)i["id"] == "old_bars")["expiry"], Is.EqualTo("check"));
            Assert.That(((JArray)opts["goals"]).Select(x => (string)x["id"]), Is.EqualTo(new[] { "water", "food", "light", "info", "meds", "contacts", "warmth" }));
            Assert.That(((JArray)opts["tips"]).Count, Is.EqualTo(5));
            Assert.That(g.Items["water_2l"].Bag, Is.EqualTo(1)); Assert.That(g.Items["water_2l"].Home, Is.EqualTo(1));
            Assert.That(g.Items["meds"].Pocket, Is.EqualTo(1));
            Assert.That(g.Items["canned"].Pocket, Is.Null, "canned food is no pocket item");
            Assert.That(g.Knows("bagPacked"));
            Assert.That((string)host.Level("apartment").State["bag"], Is.EqualTo("packed"));
            var ev = g.Events.Single(e => e.Type == "bag_packed").Details;
            Assert.That(ev["missing"].ToObject<string[]>(), Is.EqualTo(new[] { "light", "info", "contacts" }));
            Assert.That((string)g.Skill["bag"][0]["result"], Is.EqualTo("partial"));
            Assert.That(host.Toasts.Last(), Is.EqualTo("배낭을 현관에 두었다. 빠진 것: 켜지는 손전등, 라디오 또는 충전된 보조배터리, 연락처 카드 또는 서류 사본"));
        }

        [Test]
        public void ApplyHome_Stock_Bag_Radio_Power()
        {
            var (host, ctx) = Rig(11);
            var g = ctx.G;
            g.Items["radio"] = Item(0); g.Items["radio"].Bag = 1;
            g.SetKnow("radioOn", true); g.SetKnow("bagPacked", true);
            host.Run(async () => { await ctx.Goto("apartment", "home"); PrepActions.ApplyHome(ctx); ctx.Power = false; PrepActions.ApplyHome(ctx); await host.Settle(); });
            var st = host.Level("apartment").State;
            Assert.That((int)st["stock"]["water_2l"], Is.EqualTo(2));
            Assert.That((int)st["stock"]["radio"], Is.EqualTo(0));
            Assert.That((string)st["bag"], Is.EqualTo("packed"));
            Assert.That((string)st["radio"], Is.EqualTo("on"));
            Assert.That((string)st["power"], Is.EqualTo("off"));
        }

        [Test]
        public void FillWater_Once()
        {
            var (host, ctx) = Rig(11);
            bool a = false, b = true;
            host.Run(async () => { a = await PrepActions.FillWater(ctx); b = await PrepActions.FillWater(ctx); });
            Assert.That(a); Assert.That(b, Is.False);
            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "tub_filled" }));
            Assert.That(host.Toasts.Last(), Is.EqualTo("큰 통과 냄비에 물을 받아 두었다."));
            Assert.That(PrepActions.RadioFreq, Is.EqualTo(StoryCommon.RadioFreq));
        }
    }
}
