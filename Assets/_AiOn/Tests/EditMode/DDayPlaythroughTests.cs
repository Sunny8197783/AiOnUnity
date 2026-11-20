// Scripted playthroughs of 2장 (DDay.Run) on FakeStoryHost: a prepared run, an unprepared run, the two fail ends of the
// alert roam (lostWay, late) and a retry from the 'alert' checkpoint. 서연's side (SeoyeonRelay, scripted in its own
// tests) is swapped through DDay.SeoyeonPart for StandInSeoyeon: the relay clock, no message, one marker event. The
// rest (Cutscenes with a StoryCine, Elder, PrepActions) runs as it is in the build.
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DDayPlaythroughTests
    {
        [SetUp]
        public void Pin()
        {
            var r = new System.Random(5);
            DDay.Random = r.NextDouble;
        }

        /// <summary>서연's part as the golden's stub plays it: the relay clock, no handover, a 'stub_seoyeon_relay' marker.</summary>
        public static System.Threading.Tasks.Task StandInSeoyeon(StoryContext ctx)
        {
            var g = ctx.G;
            var gate = g.Events.FirstOrDefault(e => e.Type == "gate_observed" && (string)e.Details["who"] == "jeongmin");
            Relay.StoreTimes(g, Relay.RelayTimes(g.Minute + 2, gate != null ? (int?)(int)gate.Details["minute"] : g.Minute - 3));
            Relay.ClearHandover(g);
            GameRules.RecordEvent(g, "stub_seoyeon_relay", new JObject());
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>Runs DDay.Run with StandInSeoyeon (put back afterwards, so other tests get the real SeoyeonRelay).</summary>
        /// <summary>
        /// Runs DDay.Run with StandInSeoyeon and without the port's shelter CPR (these tests and the golden compare the
        /// chapter with the original; CprSceneTests plays the CPR scene), both put back afterwards.
        /// </summary>
        public static void Play(FakeStoryHost host, StoryContext ctx)
        {
            var keep = DDay.SeoyeonPart; var cpr = DDay.CprPart;
            DDay.SeoyeonPart = StandInSeoyeon; DDay.CprPart = _ => System.Threading.Tasks.Task.CompletedTask;
            try { host.Run(() => DDay.Run(ctx)); } finally { DDay.SeoyeonPart = keep; DDay.CprPart = cpr; }
        }

        public static StoryFail PlayExpectFail(FakeStoryHost host, StoryContext ctx)
        {
            var keep = DDay.SeoyeonPart; var cpr = DDay.CprPart;
            DDay.SeoyeonPart = StandInSeoyeon; DDay.CprPart = _ => System.Threading.Tasks.Task.CompletedTask;
            try { return host.RunExpectFail(() => DDay.Run(ctx)); } finally { DDay.SeoyeonPart = keep; DDay.CprPart = cpr; }
        }

        static (FakeStoryHost host, StoryContext ctx) Rig(System.Action<GameRules, GameState> setup = null)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var g = rules.CreateGame(7);
            g.Chapter = "prep"; g.Day = -1;
            setup?.Invoke(rules, g);
            var ctx = host.CreateContext(rules, g);
            ctx.Cine = new StoryCine(ctx);
            return (host, ctx);
        }

        static List<string> Sub(IEnumerable<string> types, params string[] keep) => types.Where(keep.Contains).ToList();
        static List<string> Payoffs(GameState g) => g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]).ToList();
        static List<string> Deeds(GameState g) => g.Events.Where(e => e.Type == "deed").Select(e => (string)e.Details["id"]).ToList();

        static bool Ready(FakeStoryHost h, StoryContext c, string level) => c.Roaming && h.ControlsEnabled && h.LevelId == level;

        /// <summary>Holds the player's next steps until the alert roam runs (the evening roam ends on any E press).</summary>
        static void AwaitAlert(FakeStoryHost h, StoryContext c) =>
            h.Queue(_ => { }, () => c.Roaming && h.G.Events.Any(e => e.Type == "alert"), "wait for the alert");

        /// <summary>Down the stairs to 1F (the fake has no stairs: the player is put at the foot of them).</summary>
        static void Downstairs(FakeStoryHost h, StoryContext c) =>
            h.Queue(x => { x.MoveTo(5, 0.9, 0); x.RunFrame(); }, () => Ready(h, c, "apartment") && h.PlayerY > 4, "downstairs");

        static void PrepareWell(GameRules rules, GameState g)
        {
            g.Rel["seoyeon"].Contact = true; g.Rel["manager"].Contact = true;
            g.Items["flashlight"].Checked = true;
            g.Items["radio"] = new ItemEntry(1) { Checked = true, Charge = 100 };
            g.SetKnow("radioChecked", true); g.SetKnow("routeChecked", true); g.SetKnow("idTaken", true);
            g.Items["water_500"] = new ItemEntry(0);
            g.Items["contact_card"] = new ItemEntry(0);
            g.Items["blanket"] = new ItemEntry(0);
            g.Items["water_500"].Home = 3; rules.Pack(g, "water_500", 2);
            g.Items["contact_card"].Home = 1; rules.Pack(g, "contact_card", 1);
            g.Items["blanket"].Home = 1; rules.Pack(g, "blanket", 1);
            g.SetKnow("bagPacked", true);
        }

        [Test]
        public void Prepared_RadioFirst_PackedBagOut_ManagerDetour_StairsGate_HelpsAtReception_AsksSeoyeon()
        {
            var (host, ctx) = Rig(PrepareWell);
            var g = host.G;
            host.Strict = true;
            host.AnswerCard("ok");                                  // 서연's 19:32 text
            host.Answer("hall", "tip", "radio");                    // the blackout: hall (and the candle tip), then the radio
            host.AnswerOpen("radio", JObject.Parse("{ official: true, noted: [] }"));
            host.AnswerOpen("emergencyAlert", new JObject()); host.AnswerOpen("emergencyAlert", new JObject()); host.AnswerOpen("emergencyAlert", new JObject());
            host.Answer("ask", "give", "ask");                      // reception: write for the grandfather; water for the child; ask 서연
            host.AnswerOpen("chapterCard", new JObject());
            AwaitAlert(host, ctx);
            host.QueuePress("apartment:homeExitDoor");
            Downstairs(host, ctx);
            host.QueuePresses("apartment:managerWindow", "apartment:lobbyExit");
            host.Queue(h => h.Press("shelterA"), () => Ready(host, ctx, "town") && g.Knows("strikeSeen"), "shelterA after the strike");
            host.QueuePresses("shelter:reception", "shelter:spot");

            Play(host, ctx);

            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "alert" }));
            Assert.That(host.Chapter, Is.EqualTo(("2장", "정전과 경보")));
            Assert.That(Sub(host.EventTypes(), "blackout", "alert", "departure", "strike", "gate_observed", "route_attempt", "arrived", "stub_seoyeon_relay", "help"),
                Is.EqualTo(new[] { "blackout", "alert", "departure", "strike", "gate_observed", "route_attempt", "arrived", "stub_seoyeon_relay", "help" }));
            Assert.That((string)g.Events.First(e => e.Type == "departure").Details["bag"], Is.EqualTo("packed"));
            Assert.That((string)g.Events.First(e => e.Type == "gate_observed").Details["via"], Is.EqualTo("notice_after"));
            Assert.That((string)g.Events.First(e => e.Type == "route_attempt").Details["route"], Is.EqualTo("stairs"));
            Assert.That((bool)g.Events.First(e => e.Type == "arrived").Details["late"], Is.False);
            Assert.That((string)g.Events.Last(e => e.Type == "help").Details["kind"], Is.EqualTo("ask"));
            Assert.That(Payoffs(g), Is.EqualTo(new[] { "flashlight", "radio", "bag", "flashlight", "route", "docs", "bag" }));
            Assert.That(Deeds(g), Is.SupersetOf(new[] { "hall_checked", "elder_candle_tip", "manager_detour", "register_helped", "shelter_water_given" }));
            Assert.That(Deeds(g), Does.Not.Contain("blackout_radio"), "the radio was not the first thing tried");
            Assert.That(g.Knows("closureHeard"), Is.True);
            Assert.That(g.Knows("bagOn"), Is.False, "the bag comes off at the door");
            Assert.That(g.Items["water_500"].Bag, Is.EqualTo(0), "one drunk, one given");
            Assert.That(g.Rel["seoyeon"].Trust, Is.EqualTo(3));
            Assert.That(g.Minute, Is.EqualTo(21 * 60 + 20));
            Assert.That(host.LevelId, Is.EqualTo("apartment"));
            Assert.That(host.Level("town").State["strike"].ToString(), Is.EqualTo("hit"));
            Assert.That(host.Loops, Does.Not.Contain("siren"));
            Assert.That(host.Opens.Select(o => o.screen), Is.EqualTo(new[] { "radio", "emergencyAlert", "emergencyAlert", "emergencyAlert", "chapterCard" }));
            var card = host.Opens.Last().options;
            Assert.That((string)card["title"], Is.EqualTo("정전과 경보"));
            Assert.That(card["payoffs"].Count(), Is.GreaterThan(0));
            Assert.That(host.Toasts, Does.Contain("배낭을 현관에 내려놓았다."));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("로 돌아가요!")), Is.True);
            Assert.That(g.Stats.Health, Is.GreaterThan(0));
        }

        [Test]
        public void Unprepared_CandleThenPhone_PacksInAHurry_LeavesTheCandle_EastGateFirst_NoCardAtReception()
        {
            var (host, ctx) = Rig((rules, g) =>
            {
                g.Items["candles"] = new ItemEntry(1);
                g.Items["water_500"] = new ItemEntry(1);
                g.Items.Remove("radio");
            });
            var g = host.G;
            host.Strict = true;
            host.AnswerSpeaker("정전", "candle"); host.AnswerSpeaker("정전", "phone");
            host.AnswerOpen("phone", new JObject());
            host.AnswerOpen("emergencyAlert", new JObject()); host.AnswerOpen("emergencyAlert", new JObject()); host.AnswerOpen("emergencyAlert", new JObject());
            host.AnswerSpeaker("현관", "pack");
            host.AnswerOpen("bag", JObject.Parse("{ bag: { water_500: 1, water_2l: 1, flashlight: 1 }, pocket: {}, tidied: 1 }"));
            host.AnswerSpeaker("거실", "leave");
            host.AnswerCard("ok");                                  // the east gate notice
            host.AnswerSpeaker("접수대", "go");
            host.AnswerSpeaker("옆자리", "keep");
            host.AnswerSpeaker("오서연", "go");
            host.AnswerOpen("chapterCard", new JObject());
            AwaitAlert(host, ctx);
            host.QueuePresses("apartment:homeBag", "apartment:homeExitDoor");
            Downstairs(host, ctx);
            host.QueuePresses("apartment:elevator", "apartment:managerWindow", "apartment:lobbyExit", "town:closure_yeonmuCenter", "town:aptNotice", "town:shelterEast", "town:shelterC");
            host.Queue(h => h.Press("shelterB"), () => Ready(host, ctx, "town") && g.Knows("strikeSeen"), "shelterB after the strike");
            host.QueuePresses("shelter:spot", "shelter:reception", "shelter:spot");

            Play(host, ctx);

            Assert.That(Sub(host.EventTypes(), "blackout", "candle_lit", "alert", "candle_left", "departure", "elevator_tried", "strike", "gate_observed", "route_attempt", "arrived"),
                Is.EqualTo(new[] { "blackout", "candle_lit", "alert", "departure", "candle_left", "elevator_tried", "strike", "gate_observed", "route_attempt", "arrived" }).Or
                .EqualTo(new[] { "blackout", "candle_lit", "alert", "departure", "candle_left", "elevator_tried", "gate_observed", "strike", "route_attempt", "arrived" }));
            var dep = g.Events.First(e => e.Type == "departure").Details;
            Assert.That((string)dep["bag"], Is.EqualTo("late")); Assert.That((int)dep["delay"], Is.EqualTo(4));
            Assert.That((string)g.Events.First(e => e.Type == "gate_observed").Details["via"], Is.EqualTo("sign"));
            Assert.That((string)g.Events.First(e => e.Type == "route_attempt").Details["route"], Is.EqualTo("ramp"));
            Assert.That(g.Events.Last(e => e.Type == "encounter" && (string)e.Details["tool"] == "bag").Details["result"].ToString(), Is.EqualTo("partial"));
            Assert.That(g.Items["flashlight"].Bag, Is.EqualTo(1));
            Assert.That(g.Knows("closureSeen"), Is.True); Assert.That(g.Knows("closureHeard"), Is.False);
            Assert.That(g.Knows("gateClosedSeen"), Is.True);
            Assert.That(host.Level("apartment").State["candles"].ToString(), Is.EqualTo("lit"), "left burning");
            Assert.That(Payoffs(g), Is.EqualTo(new[] { "bag" }), "only the water drunk at the shelter");
            Assert.That(Deeds(g), Does.Not.Contain("register_helped"));
            Assert.That(Deeds(g), Does.Not.Contain("shelter_water_given"));
            Assert.That(g.Events.Any(e => e.Type == "help"), Is.False, "went up first");
            Assert.That(host.Toasts, Does.Contain("먼저 접수대에서 명부를 써 주세요."));
            Assert.That(host.Toasts, Does.Contain("물건을 고르느라 4분이 지났다."));
            Assert.That(host.Toasts, Does.Contain("셔터가 내려져 있다: \"준비 중\""));
            Assert.That(host.Toasts.Any(t => t.StartsWith("정전으로 멈춰 있다. ")), Is.True);
            Assert.That(host.Talks.Any(t => t.Speaker == "관리사무소장"), Is.True);
            Assert.That(host.Talks.Any(t => t.Speaker == "aptNotice" || t.Lines.Contains("대피소: 연무 생활문화센터 지하주차장. 유도표지판의 화살표를 따라가세요.")), Is.True);
            Assert.That(host.Talks.Any(t => t.Speaker == "안내 요원" && t.Role == "연무로 중앙 구간 통제"), Is.True);
            Assert.That(host.Talks.First(t => t.Speaker == "대피소 관리자").Lines[1], Is.EqualTo("(배낭에서 비상연락카드를 꺼내 보고 적었다.)").Or.EqualTo("(휴대폰을 보며 기억나는 대로 적었다. 배터리가 아깝다.)"));
            Assert.That(host.Talks.First(t => t.Speaker == "대피소 관리자").Lines[1], Does.Contain("휴대폰"));
            var bag = host.Opens.First(o => o.screen == "bag").options;
            Assert.That((string)bag["context"], Is.EqualTo("alert"));
            Assert.That((double)bag["limit"]["weightKg"], Is.EqualTo(8));
            Assert.That(bag["goals"].Select(x => (string)x["id"]).First(), Is.EqualTo("water"));
            Assert.That(g.Minute, Is.EqualTo(21 * 60 + 20));
        }

        [Test]
        public void LostWay_SawAClosureWithoutTheManagersWord_AndTheClockRanOut()
        {
            var (host, ctx) = Rig();
            host.AnswerSpeaker("현관", "go");
            AwaitAlert(host, ctx);
            host.QueuePresses("apartment:homeExitDoor");
            Downstairs(host, ctx);
            host.QueuePresses("apartment:lobbyExit", "town:closure_danji");
            var fail = PlayExpectFail(host, ctx);
            Assert.That(fail.Type, Is.EqualTo("lostWay"));
            Assert.That(host.G.Minute, Is.GreaterThanOrEqualTo(DDay.Late));
            Assert.That((string)host.G.Events.First(e => e.Type == "departure").Details["bag"], Is.EqualTo("none"));
            Assert.That(ctx.Trail.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Late_StillAtHomeWhenTheClockRanOut_ThenTheRetryResumesAtTheAlert()
        {
            var (host, ctx) = Rig();
            var fail = PlayExpectFail(host, ctx);
            Assert.That(fail.Type, Is.EqualTo("late"));
            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "alert" }));

            // the retry: Resume = 'alert' skips the evening and starts dark at home
            var (h2, c2) = Rig();
            c2.Resume = "alert"; h2.G.Day = 0; // the 'alert' checkpoint was taken on D-day
            h2.AnswerSpeaker("현관", "go");
            h2.QueuePress("apartment:homeExitDoor");
            Downstairs(h2, c2);
            h2.QueuePresses("apartment:lobbyExit");
            h2.Queue(h => h.Press("shelterA"), () => Ready(h2, c2, "town") && h2.G.Knows("strikeSeen"), "shelterA");
            h2.QueuePresses("shelter:reception", "shelter:spot");
            Play(h2, c2);
            Assert.That(h2.G.Events.Any(e => e.Type == "blackout"), Is.False, "the evening is not played again");
            Assert.That(h2.Checkpoints, Is.Empty);
            Assert.That(h2.Level("apartment").State["power"].ToString(), Is.EqualTo("off"));
            Assert.That(h2.PowerOn, Is.False);
            Assert.That(Deeds(h2.G), Does.Contain("shelter_water_received"), "out with nothing: a neighbour shares water");
            Assert.That(h2.G.Events.Any(e => e.Type == "arrived"), Is.True);
        }
    }
}
