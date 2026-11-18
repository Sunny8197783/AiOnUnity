// Port of the evac parts of tests/evac-story.test.mjs (depart: the rumour shortcut, lostWay / missed, 303호 and the
// manager; the missed caption), plus scripted playthroughs of 4장 (crossroads), 5장 (evacuation) and the ending on
// FakeStoryHost.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class EvacTests
    {
        static bool IsLabels(TalkOptions o) => o.Choices != null && o.Choices.Any(c => c.Id == "neighbor") && o.Choices.Any(c => c.Id == "official");
        static StoryPoint P(string id, string kind = "npc", string label = null) => new() { Id = id, Kind = kind, Label = label ?? id };

        /// <summary>Asserts that want appears in order inside got (other events may sit between).</summary>
        static void InOrder(IReadOnlyList<string> got, params string[] want)
        {
            int i = 0;
            foreach (var t in got) if (i < want.Length && t == want[i]) i++;
            Assert.That(i, Is.EqualTo(want.Length), $"events {string.Join(",", want)} not in order in: {string.Join(",", got)}");
        }

        static GameEvent Ev(GameState g, string type) => g.Events.LastOrDefault(e => e.Type == type);
        static List<string> Deeds(GameState g) => g.Events.Where(e => e.Type == "deed").Select(e => (string)e.Details["id"]).ToList();

        // ---------------------------------------------------------------- depart (original rig: seed 11, D+12 13:15)

        sealed class Rig
        {
            public FakeStoryHost Host;
            public StoryContext Ctx;
            public GameState G => Host.G;
            public string Road;
        }

        /// <summary>answer(o): the pick for each talk with choices (the original's rig answer).</summary>
        static Rig MakeRig(Func<TalkOptions, string> answer)
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.TalkPolicy = answer;
            var g = host.G;
            g.Chapter = "evac"; g.Day = 12; g.Minute = 13 * 60 + 15;
            return new Rig { Host = host, Ctx = ctx, Road = Rumors.Rumor("evac_shortcut").Road(g) };
        }

        static string GoOr(TalkOptions o, string label) =>
            IsLabels(o) ? label : o.Choices.Any(c => c.Id == "go") ? "go" : o.Choices.Last().Id;

        [Test]
        public void BelieveTheShortcutWalkToItsClosureLet1400Pass_LostWayViaRumour()
        {
            var r = MakeRig(o => GoOr(o, "neighbor"));
            var fail = r.Host.RunExpectFail(async () =>
            {
                var done = Evac.Depart(r.Ctx);
                await r.Host.Settle();
                Assert.That(r.Host.Objectives.Last().body, Does.Contain("지름길"), "the objective names the road the post points to");
                await r.Host.PressAsync(P($"closure_{r.Road}", "npc", "guide"));
                Assert.That(r.G.Events.Any(e => e.Type == "rumor_followed" && (string)e.Details["id"] == "evac_shortcut"));
                r.G.Minute = 14 * 60;
                r.Host.RunFrame();
                await done;
            });
            Assert.That(fail.Type, Is.EqualTo("lostWay"));
            Assert.That((string)fail.Detail["via"], Is.EqualTo("rumor"));
        }

        [Test]
        public void BelievedButNeverFollowed_OrCorrectedByTheManager_1400IsThePlainMissedBus()
        {
            var a = MakeRig(o => GoOr(o, "official"));
            a.Host.Run(async () =>
            {
                var done = Evac.Depart(a.Ctx);
                await a.Host.Settle();
                a.G.Minute = 14 * 60; a.Host.RunFrame();
                await done;
            });
            Assert.That((string)a.G.Know["path"], Is.EqualTo("missed"));

            var b = MakeRig(o => GoOr(o, "official"));
            b.Host.Run(async () =>
            {
                var done = Evac.Depart(b.Ctx);
                await b.Host.Settle();
                await b.Host.PressAsync(P("managerWindow", "npc", "관리사무소"));
                Assert.That(Rumors.Misled(b.G), Is.Null, "the site information from the manager corrects the post");
                Assert.That(b.G.Events.Any(e => e.Type == "help" && (string)e.Details["who"] == "manager"), Is.False, "talking is not helping (F7)");
                await b.Host.PressAsync(P($"closure_{b.Road}", "npc", "guide"));
                b.G.Minute = 14 * 60; b.Host.RunFrame();
                await done;
            });
            Assert.That((string)b.G.Know["path"], Is.EqualTo("missed"));
        }

        [Test]
        public void Room303CanBeAskedAgain_AndStayingWithTheManagerIsTheOneHelpLogged()
        {
            int n = 0;
            var a = MakeRig(o => IsLabels(o) ? "rumor" : o.Speaker == "오서연" ? (n++ > 0 ? "wait" : "go") : o.Choices[0].Id);
            a.Host.Run(async () =>
            {
                var done = Evac.Depart(a.Ctx);
                await a.Host.Settle();
                await a.Host.PressAsync(P("seoyeonDoor", "door", "303호"));
                await a.Host.PressAsync(P("seoyeonDoor", "door", "303호"));
                await done;
            });
            Assert.That((string)a.G.Know["path"], Is.EqualTo("seoyeon"));
            Assert.That(a.Host.Talks.Count(t => t.Speaker == "오서연"), Is.EqualTo(2));
            Assert.That(a.Host.Talks.Last(t => t.Speaker == "오서연").Lines[0], Does.Contain("아직 안 가셨어요"));

            var b = MakeRig(o => IsLabels(o) ? "rumor" : o.Speaker == "박영수 소장" ? "stay" : "go");
            b.Host.Run(async () =>
            {
                var done = Evac.Depart(b.Ctx);
                await b.Host.Settle();
                await b.Host.PressAsync(P("managerWindow", "npc", "관리사무소"));
                await done;
            });
            Assert.That((string)b.G.Know["path"], Is.EqualTo("stay"));
            Assert.That(b.G.Events.Count(e => e.Type == "help" && (string)e.Details["who"] == "manager"), Is.EqualTo(1));
            Assert.That(b.Host.Captions.Last(), Is.EqualTo("가방을 내려놓았다. 오늘부터 이 건물이 내 자리다."));
        }

        [Test]
        public void MissedCaption_AnEmptyBayOnlyAtTheAssemblyPoint()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var a = host.Point("assembly");
                Assert.That((a.X, a.Z), Is.EqualTo((-60.0, -36.0)).Using<(double, double)>((p, q) => Math.Abs(p.Item1 - q.Item1) < 1 && Math.Abs(p.Item2 - q.Item2) < 1 ? 0 : 1));
                host.MoveTo(-58, -30);
                Assert.That(Evac.MissedCaption(ctx), Does.Contain("집결지"));
                host.MoveTo(40, 60);
                Assert.That(Evac.MissedCaption(ctx), Does.Not.Contain("집결지"));
                await ctx.Goto("apartment", "home");
                Assert.That(Evac.MissedCaption(ctx), Does.Contain("2시가 넘었다"));
            });
        }

        [Test]
        public void TheManagerTakesSpareStockOnceFromHomeOnly()
        {
            var r = MakeRig(o => IsLabels(o) ? "rumor" : o.Choices.Any(c => c.Id == "give") ? "give" : "go");
            r.G.Items["water_2l"] = new ItemEntry(3);
            r.G.Items["canned"] = new ItemEntry(1);
            r.G.Items["bars"] = new ItemEntry(2);
            int FoodHome() => new[] { "canned", "rice_instant", "bars", "ramen" }.Sum(id => r.G.Items.TryGetValue(id, out var e) ? e.Home : 0);
            int food0 = FoodHome();
            r.Host.Run(async () =>
            {
                var done = Evac.Depart(r.Ctx);
                await r.Host.Settle();
                await r.Host.PressAsync(P("managerWindow"));
                await r.Host.PressAsync(P("managerWindow"));
                r.G.Minute = 14 * 60; r.Host.RunFrame();
                await done;
            });
            Assert.That(r.G.Items["water_2l"].Home, Is.EqualTo(1));
            Assert.That(FoodHome(), Is.EqualTo(food0 - 2), "two units of food");
            Assert.That(r.G.Items["canned"].Home, Is.EqualTo(0), "canned first, in list order");
            Assert.That(Deeds(r.G).Count(d => d == "stock_to_manager"), Is.EqualTo(1));
            Assert.That(r.Host.Talks.Count(t => t.Lines.Any(l => l.Contains("맡겨 줘도 돼요"))), Is.EqualTo(1), "offered once");
        }

        // ---------------------------------------------------------------- 4장 playthroughs

        static (FakeStoryHost host, StoryContext ctx) Fresh(int seed = 7)
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(seed));
            var g = host.G;
            g.Chapter = "endure"; g.Day = 3; g.Minute = 18 * 60;
            g.Items["water_2l"] = new ItemEntry(6);
            g.Items["canned"] = new ItemEntry(4);
            g.Items["rice_instant"] = new ItemEntry(2);
            return (host, ctx);
        }

        [Test]
        public void Crossroads_StayAtHome_FeelTheHandleThenWait()
        {
            var (host, ctx) = Fresh();
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go");
            host.AnswerSpeaker("판단", "handle").AnswerSpeaker("판단", "stay");
            host.AnswerSpeaker("윤 팀장", "yes").AnswerSpeaker("박영수 소장", "go");
            host.AnswerCard("add", "stay");
            host.Run(() => Evac.Crossroads(ctx));
            var g = host.G;
            Assert.That(g.Chapter, Is.EqualTo("crossroads"));
            Assert.That(host.Chapter, Is.EqualTo(("4장", "남을까, 떠날까")));
            Assert.That(host.TimeSkips[0].from, Is.EqualTo("D+3 월"));
            Assert.That(host.TimeSkips[0].text[0], Does.StartWith("하루 한두 시간씩"));
            Assert.That(host.Talks.Any(t => t.Lines.Contains("현관문 틈에 서연이 끼워 둔 메모가 있다.")), "acted on the judgement: 서연's memo");
            InOrder(host.EventTypes(), "rumor_judged", "decision", "smoke_choice", "smoke_choice", "payoff");
            Assert.That((string)Ev(g, "decision").Details["stayMove"], Is.EqualTo("stay"));
            Assert.That(g.Events.Where(e => e.Type == "smoke_choice").Select(e => (string)e.Details["pick"]), Is.EqualTo(new[] { "handle", "stay" }));
            Assert.That((string)Ev(g, "payoff").Details["prep"], Is.EqualTo("judgement"));
            Assert.That(Deeds(g), Is.SupersetOf(new[] { "seoyeon_note_reply", "yoon_reply", "rounds_with_manager" }));
            Assert.That(host.Checkpoints, Does.Contain("fire4"));
            Assert.That((g.Day, g.Minute >= 22 * 60 + 10 + 40), Is.EqualTo((7, true)), "the rounds took 40 minutes");
            // the handle is felt once: the second ask has it disabled
            var asks = host.Talks.Where(t => t.Speaker == "판단").ToList();
            Assert.That(asks.Select(t => t.Choices[0].Disabled), Is.EqualTo(new[] { false, true }));
            Assert.That(host.Talks.Any(t => t.Speaker == "현관 문손잡이" && t.Lines[1] == ctx.Facts.Text("fire_door")));
            // the smoke states, cleared after
            Assert.That(host.Level("apartment").State["smoke"].Value<double>(), Is.EqualTo(0));
            Assert.That((string)host.Level("apartment").State["haze"], Is.EqualTo("follow"));
            Assert.That((string)host.Level("apartment").State["windowView"], Is.EqualTo("blackout"));
            Assert.That(ctx.OutdoorEnv, Is.EqualTo("blackout"));
            Assert.That(host.Opens.Last().screen, Is.EqualTo("chapterCard"));
            Assert.That((string)host.Opens.Last().options["chapter"], Is.EqualTo("4장"));
            Assert.That(g.Skill["extinguisher"].Last().Value<string>("result"), Is.EqualTo("retreat-ok"));
        }

        [Test]
        public void Crossroads_StayAtHome_RunningOutHurts_FightingFails()
        {
            var (host, ctx) = Fresh();
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("판단", "run").AnswerSpeaker("박영수 소장", "rest");
            host.AnswerCard("read", "stay");
            double h0 = host.G.Stats.Health;
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.G.Stats.Health, Is.EqualTo(h0 - 15));
            Assert.That(host.G.Events.Any(e => e.Type == "payoff"), Is.False);
            Assert.That(Deeds(host.G), Does.Not.Contain("seoyeon_note_reply"));
            Assert.That(host.Talks.Last(t => t.Speaker == "박영수 소장").Lines[0], Is.EqualTo("그래요, 푹 쉬어요. 놀랐을 텐데."));

            var (host2, ctx2) = Fresh();
            host2.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("판단", "fight");
            host2.AnswerCard("add", "stay");
            var fail = host2.RunExpectFail(() => Evac.Crossroads(ctx2));
            Assert.That((fail.Type, (string)fail.Detail["scene"]), Is.EqualTo(("fire", "upstairs")));
            Assert.That(host2.G.Skill["extinguisher"].Last().Value<string>("result"), Is.EqualTo("escalated"));
        }

        [Test]
        public void Crossroads_MoveToTheShelter_BelievedTheFoodRumour_StepBackFromTheFire()
        {
            var (host, ctx) = Fresh();
            host.AnswerWhen(IsLabels, "neighbor").AnswerSpeaker("정민", "go");
            host.AnswerSpeaker("대피소 관리자", "precise").AnswerSpeaker("대피소 관리자", "help");
            host.AnswerCard("add", "move");
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("shout", "retreat") });
            host.Run(() => Evac.Crossroads(ctx));
            var g = host.G;
            Assert.That(host.LevelId, Is.EqualTo("shelter"));
            Assert.That(host.Card(c => c.Title == "지금 내 상황").Body, Has.Some.Contains("임시 거처 급식이 끊겼다"), "the claim is on the judgement card");
            InOrder(host.EventTypes(), "rumor_judged", "rumor_believed", "decision", "rumor_corrected", "payoff");
            Assert.That(Rumors.Misled(g), Is.Null);
            Assert.That(host.Captions[0], Does.Contain("배식 줄이 섰다"));
            var ext = host.Opens.Single(o => o.screen == "extinguisher").options;
            Assert.That((string)ext["fire"], Is.EqualTo("large"));
            Assert.That((string)host.Level("shelter").State["fire"], Is.EqualTo("none"));
            Assert.That((string)host.Level("shelter").State["power"], Is.EqualTo("emergency"));
            Assert.That(Deeds(g), Is.SupersetOf(new[] { "fire_report", "sort_supplies" }));
            Assert.That(host.Talks.Any(t => t.Lines.Contains("시간이랑 자리를 정확히 말해 주시니 확인이 빠르네요. 고맙습니다.")));
            Assert.That(host.PlayerYaw, Is.Not.EqualTo(0), "faces the burning corner");
            Assert.That(host.Warnings.Where(w => w.StartsWith("shelter")), Is.Empty);
        }

        [Test]
        public void Crossroads_Move_FightingTheCeilingFireFails_CheckingOnTheRadioShowsTheNotice()
        {
            var (host, ctx) = Fresh();
            host.G.Items["radio"] = new ItemEntry(1); host.G.Items["batteries_aa"] = new ItemEntry(1);
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "check");
            host.AnswerOpen("radio", new JObject { ["official"] = true, ["noted"] = new JArray() });
            host.AnswerCard("ok", "add", "move");
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("fight", "pin", "aim") });
            var fail = host.RunExpectFail(() => Evac.Crossroads(ctx));
            Assert.That((fail.Type, (string)fail.Detail["scene"]), Is.EqualTo(("fire", "shelter")));
            Assert.That(host.Cards[0].Title, Is.EqualTo("연무구 안내 (가상)"));
            Assert.That(host.Cards[0].Body.Last(), Is.EqualTo("(라디오에서 같은 내용을 들었다.)"));
            Assert.That(host.G.Events.Any(e => e.Type == "rumor_checked" && (string)e.Details["id"] == "camp_food"));
            Assert.That(host.Card(c => c.Title == "지금 내 상황").Body, Has.Some.Contains("확인한 것: 임시 거처는 급수·급식을 지원한다"));
            Assert.That((string)host.Level("shelter").State["fire"], Is.EqualTo("none"));
        }

        [Test]
        public void Crossroads_CheckAtTheBoard_WalksOutAndBack()
        {
            var (host, ctx) = Fresh();
            host.G.Phone.Battery = 50;
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "check").AnswerSpeaker("라디오", "no").AnswerSpeaker("정민", "board");
            host.AnswerCard("ok", "add", "stay");
            host.AnswerSpeaker("판단", "stay");
            host.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").QueuePress("town:aptNotice");
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.Cards[0].Body.Last(), Is.EqualTo("(단지 게시판에서 직접 읽었다.)"));
            Assert.That(host.Objectives.Any(o => o.title == "게시판에서 확인하기"));
            Assert.That(host.G.Events.Count(e => e.Type == "rumor_checked"), Is.EqualTo(1));
            Assert.That(host.Calls("enter").Select(c => c.Text), Has.Some.EqualTo("apartment:home").And.Some.EqualTo("town:home"));
        }

        [Test]
        public void Crossroads_ResumeAtFire4_SkipsStraightToTheChosenNight()
        {
            var (host, ctx) = Fresh();
            host.G.SetKnow("stayMove", "move");
            ctx.Resume = "fire4";
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.TimeSkips.Single().to, Is.EqualTo("D+7 금 저녁"));
            Assert.That(host.G.Events.Any(e => e.Type == "decision"), Is.False);
            Assert.That(host.Checkpoints, Is.Empty);
        }

        // ---------------------------------------------------------------- 5장 playthroughs

        static void Kit(GameState g)
        {
            g.Chapter = "crossroads"; g.Day = 7; g.Minute = 23 * 60;
            g.SetKnow("stayMove", "stay");
            g.SetKnow("bagPacked", true);
            foreach (var id in new[] { "id_card", "contact_card", "doc_copies", "water_500", "bars", "flashlight", "meds" })
                g.Items[id] = new ItemEntry(2);
        }

        static JObject FullBag => new()
        {
            ["bag"] = new JObject { ["id_card"] = 1, ["contact_card"] = 1, ["doc_copies"] = 1, ["water_500"] = 2, ["bars"] = 2, ["flashlight"] = 1, ["meds"] = 1 },
            ["pocket"] = new JObject { ["contact_card"] = 1 },
        };

        [Test]
        public void Evacuation_ByBus_NoteOnTheDoor_HelpAtTheLift_PapersFromThePocket()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("라디오", "no");
            host.AnswerSpeaker("현관", "note").AnswerSpeaker("집결지", "ask");
            host.AnswerOpen("bag", FullBag);
            host.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").QueuePress("town:assembly");
            host.Run(() => Evac.Evacuation(ctx));
            var g = host.G;
            Assert.That(g.Chapter, Is.EqualTo("evac"));
            Assert.That(host.Chapter, Is.EqualTo(("5장", "공식 대피")));
            Assert.That(host.TimeSkips[0].text[0], Is.EqualTo("닷새가 더 지났다. 이웃 몇 집은 이미 떠났다."));
            var alert = host.Opens.First(o => o.screen == "emergencyAlert").options;
            Assert.That((string)alert["kind"], Is.EqualTo("안전안내문자"));
            Assert.That((string)alert["text"], Does.StartWith("[안전안내문자] 연무동 주민 이동 지원.").And.EndWith(StoryCommon.Area));
            Assert.That((string)alert["time"], Is.EqualTo(GameRules.Hhmm(g.Cond.EvacNoticeAt)));
            var bag = host.Opens.First(o => o.screen == "bag").options;
            Assert.That((string)bag["context"], Is.EqualTo("evac"));
            Assert.That((double)bag["limit"]["weightKg"], Is.EqualTo(8));
            Assert.That(((JArray)bag["items"]).Select(i => (string)i["id"]), Does.Contain("id_card"));
            Assert.That(((JArray)bag["items"]).All(i => (string)i["tip"] == ""), "no tips on the third encounter");
            Assert.That(g.Items["contact_card"].Pocket, Is.EqualTo(1));
            Assert.That(g.Knows("bagOn"));
            Assert.That(host.Checkpoints, Does.Contain("depart"));
            InOrder(host.EventTypes(), "sms", "rumor_judged", "note_left", "path", "arrived_assembly");
            Assert.That((string)g.Know["path"], Is.EqualTo("bus"));
            Assert.That(Deeds(g), Does.Contain("bus_helped"));
            var payoffs = g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]).ToList();
            Assert.That(payoffs, Does.Contain("docs"));
            Assert.That(g.Events.Last(e => e.Type == "payoff" && (string)e.Details["prep"] == "docs").Details.Value<string>("moment"), Does.Contain("바로 "));
            var desk = host.Talks.Last(t => t.Speaker == "집결지 접수");
            Assert.That(desk.Lines, Is.EqualTo(new[] { "이름, 연락처, 복용약을 적어 주세요. 신분증 있으세요?", "(지갑에서 신분증을 꺼내 보여 주었다.)", "(가방 앞주머니의 연락처 카드와 서류 사본도 함께 건넸다.)" }));
            Assert.That(host.Captions.Last(), Is.EqualTo("버스 창밖으로 연무동이 멀어진다."));
            var town = host.Level("town").State;
            Assert.That((string)town["buses"], Is.EqualTo("departed")); // polish: they left (the departure scene, or its caption)
            Assert.That(town["closures"].Select(t => (string)t), Is.EqualTo(g.Cond.Closures.Evac));
            Assert.That(host.Warnings.Where(w => w.StartsWith("town")), Is.Empty);
        }

        [Test]
        public void Evacuation_IdLeftAtHome_PapersDeepInTheBag()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("라디오", "no").AnswerSpeaker("현관", "no").AnswerSpeaker("집결지", "line");
            host.AnswerOpen("bag", new JObject { ["bag"] = new JObject { ["contact_card"] = 1, ["water_500"] = 2 } });
            host.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").QueuePress("town:assembly");
            host.Run(() => Evac.Evacuation(ctx));
            var desk = host.Talks.Last(t => t.Speaker == "집결지 접수");
            Assert.That(desk.Lines[1], Does.StartWith("(신분증을 집에 두고 나왔다."));
            Assert.That(desk.Lines[2], Does.StartWith("(연락처 카드와 서류 사본은 가방 깊숙이 있었다."));
            Assert.That(host.Toasts, Does.Contain(ctx.Facts.Text("docs_take")));
            Assert.That(host.G.Events.Last(e => e.Type == "payoff").Details.Value<string>("moment"), Does.Contain("조금 늦게나마"));
            Assert.That(host.G.Events.Any(e => e.Type == "note_left"), Is.False);
            Assert.That(Deeds(host.G), Does.Not.Contain("bus_helped"));
            Assert.That(host.G.Skill["bag"].Last().Value<string>("result"), Is.EqualTo("partial"));
        }

        [Test]
        public void Evacuation_OtherPaths_Volunteer_WalkOut_Seoyeon()
        {
            foreach (var (path, steps, caption) in new (string, Action<FakeStoryHost>, string)[]
            {
                ("volunteer", h => h.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").QueuePress("town:center"), "조끼를 받아 입었다. 급수대 앞 줄이 길다."),
                ("walk", h => h.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").Queue(x => x.MoveTo(130, 0), () => x0(h), "walk out"), "도시의 끝, 통제선을 지나 국도로 들어섰다. 뒤에서 또 포성이 울렸다."),
                ("seoyeon", h => h.QueuePress("apartment:homeExitDoor").QueuePress("apartment:seoyeonDoor"), "서연의 문 앞에 가방을 내려놓고 앉았다. 지원 차량은 늦어지고 있었다."),
            })
            {
                var (host, ctx) = Fresh();
                Kit(host.G);
                host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("라디오", "no");
                host.AnswerSpeaker("행정복지센터", "yes").AnswerSpeaker("오서연", "wait");
                host.AnswerOpen("bag", FullBag);
                steps(host);
                host.Run(() => Evac.Evacuation(ctx));
                Assert.That((string)host.G.Know["path"], Is.EqualTo(path), path);
                Assert.That(host.Captions.Last(), Is.EqualTo(caption), path);
                Assert.That(host.G.Events.Any(e => e.Type == "arrived_assembly"), Is.False, path);
            }
        }

        static bool x0(FakeStoryHost h) => h.LevelId == "town" && h.Ctx.Roaming && h.ControlsEnabled;

        [Test]
        public void Evacuation_ShelterStay_ResumeAtDepart()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.G.SetKnow("stayMove", "move");
            host.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("라디오", "no");
            host.AnswerOpen("bag", FullBag);
            host.QueuePress("apartment:managerWindow");
            host.AnswerSpeaker("박영수 소장", "stay");
            host.Run(() => Evac.Evacuation(ctx));
            Assert.That(host.TimeSkips[0].text[0], Does.StartWith("닷새가 더 지났다. 임시 거처에서"));
            Assert.That((string)host.G.Know["path"], Is.EqualTo("stay"));

            // a retry from the 'depart' checkpoint asks the shortcut post again and skips the morning
            var (h2, c2) = Fresh();
            Kit(h2.G); h2.G.Day = 12; h2.G.Minute = 13 * 60 + 15;
            c2.Resume = "depart";
            h2.AnswerWhen(IsLabels, "rumor").AnswerSpeaker("정민", "go").AnswerSpeaker("박영수 소장", "stay");
            h2.QueuePress("apartment:managerWindow");
            h2.Run(() => Evac.Evacuation(c2));
            Assert.That(h2.TimeSkips, Is.Empty);
            Assert.That(h2.Opens.Any(o => o.screen == "bag"), Is.False);
            Assert.That(h2.G.Events.Count(e => e.Type == "rumor_judged"), Is.EqualTo(1));
        }

        [Test]
        public void Evacuation_CheckingTheShortcutOnTheRadio_TicksTheStepAndNamesTheClosures()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.G.Items["radio"] = new ItemEntry(1); host.G.Items["batteries_aa"] = new ItemEntry(2);
            host.AnswerWhen(IsLabels, "official").AnswerSpeaker("정민", "check");
            host.AnswerOpen("radio", new JObject { ["official"] = false }); // the morning listen misses the station
            host.AnswerOpen("bag", FullBag);
            host.AnswerOpen("radio", new JObject { ["official"] = true });
            host.Queue(h => { h.G.Minute = 14 * 60; h.RunFrame(); }, () => host.Ctx.Roaming && host.ControlsEnabled, "let 14:00 pass");
            host.Run(() => Evac.Evacuation(ctx));
            Assert.That(host.Toasts.Any(t => t.StartsWith("라디오: 오늘 통제 구간에")));
            var obj = host.Objectives.Last(o => o.title == "오늘 어떻게 할까");
            Assert.That(obj.steps[0].Text, Does.StartWith("한 번 더 확인하기"));
            Assert.That(obj.steps[0].Done, Is.True);
            Assert.That(obj.body, Does.Contain("오늘 통제: "));
            Assert.That((string)host.G.Know["path"], Is.EqualTo("missed"));
            Assert.That(host.Captions.Last(), Is.EqualTo("시계를 보니 2시가 넘었다. 버스는 이미 떠났다."));
        }

        // ---------------------------------------------------------------- ending

        [Test]
        public void Ending_RecordsTheLoadRecordsTheRunAndOpensTheDocument_TheMontageNeverBlocks()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.G.SetKnow("path", "volunteer");
            host.G.SetKnow("bagOn", true);
            host.G.Items["water_500"].Bag = 2;
            int recorded = 0;
            ctx.RecordRun = () => { recorded++; return System.Threading.Tasks.Task.CompletedTask; };
            host.AnswerOpen("ending", "again");
            var r = host.Run(() => Evac.Ending(ctx));
            Assert.That((string)r, Is.EqualTo("again"));
            Assert.That(host.G.Chapter, Is.EqualTo("ending"));
            Assert.That(Ev(host.G, "ending").Details.Value<double>("load"), Is.EqualTo(TestSupport.Rules.BagLoad(host.G).WeightKg));
            Assert.That(recorded, Is.EqualTo(1));
            var doc = host.Opens.Single(o => o.screen == "ending").options;
            Assert.That(doc["record"]["fate"], Is.Not.Null);
            Assert.That(((JArray)doc["sources"]).Count, Is.EqualTo(ctx.Facts.Sources.Count));
        }

        [Test]
        public void FullRun_CrossroadsEvacuationEnding()
        {
            var (host, ctx) = Fresh();
            Kit(host.G);
            host.G.Day = 3; host.G.Chapter = "endure";
            host.AnswerWhen(IsLabels, "rumor", once: false).AnswerWhen(o => o.Speaker == "정민", "go", once: false);
            host.AnswerSpeaker("판단", "stay").AnswerSpeaker("라디오", "no");
            host.AnswerCard("add", "stay");
            host.AnswerOpen("bag", FullBag);
            host.QueuePress("apartment:homeExitDoor").QueuePress("apartment:lobbyExit").QueuePress("town:assembly");
            host.Run(async () =>
            {
                await Evac.Crossroads(ctx);
                await Evac.Evacuation(ctx);
                await Evac.Ending(ctx);
            });
            InOrder(host.EventTypes(), "decision", "smoke_choice", "path", "arrived_assembly", "ending");
            Assert.That(host.Opens.Select(o => o.screen), Is.SupersetOf(new[] { "chapterCard", "bag", "ending" }));
        }
    }

    static class HostExt
    {
        public static CardOptions Card(this FakeStoryHost h, Func<CardOptions, bool> f) => h.Cards.Single(f);
    }
}
