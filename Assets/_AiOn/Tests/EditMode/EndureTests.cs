// Endure (story/endure.js, 3장 버티기): scripted playthroughs on FakeStoryHost compared with the ORIGINAL chapter run
// under node on the same scripts (Tools/golden/make-golden-story-endure.mjs -> Fixtures/story-endure-golden.json),
// plus key-state checks per branch and the soft fails.
//   helper        서연 asks, jug + cash, 어르신 helped at the queue, store visit (buy, check the rumour, take the barter,
//                 shut the shutter), knock -> info, the extinguisher puts the small fire out, thanks + visit.
//   alone         nobody asks, no jug or cash, spoiled food eaten, phone FM, take from the store, believe then fix the
//                 rumour, old bars then no, knock -> none, shout / water / retreat down through the smoke.
//   fireRetreat   resume at the 'fire' checkpoint, 박 소장 was told on D-day (어르신 knocks), the drill says retreat, a wet towel.
//   fireEscalated resume at 'fire', water makes it grow, fighting the large fire -> StoryFail('fire').
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
    public class EndureTests
    {
        // The chapter as the original had it: without the competition upgrade's 살펴서 대피 step (LookFirstSceneTests
        // plays it) and with the original fact toast at the elevator during the smoke escape.
        Func<StoryContext, bool, System.Threading.Tasks.Task<LookFirstLogic.Outcome>> _look;
        bool _stops;
        [SetUp] public void WithoutTheLookFirstStep() { _look = Endure.LookPart; _stops = Escape.NeighbourStops; Endure.LookPart = null; Escape.NeighbourStops = false; }
        [TearDown] public void PutTheStepBack() { Endure.LookPart = _look; Escape.NeighbourStops = _stops; }

        static JObject _golden;
        // Polish pass (port upgrades the original did not have): the wait in the water queue, the knock before 서연's
        // doorway talk, and the afternoon / night shown as time passing. The golden comparison leaves them out.
        static readonly HashSet<string> PortCaptions = new() { "줄을 서서 기다렸다.", "똑똑— 현관문을 두드리는 소리." };
        static readonly HashSet<string> PortSkips = new() { "D+1 토 오후", "D+2 밤" };

        static JObject Golden => _golden ??= JObject.Parse(TestSupport.Fixture("story-endure-golden.json"));

        sealed class Script
        {
            public long Seed;
            public string Resume;
            public Action<GameState> Setup;
            public string[] Talk = Array.Empty<string>(), Card = Array.Empty<string>();
            public (string screen, JToken result)[] Open = Array.Empty<(string, JToken)>();
            public string[] Steps = Array.Empty<string>();
        }

        static ItemEntry Own(int n) => new ItemEntry { Home = n, Bought = n };

        // The same scripts as make-golden-story-endure.mjs.
        static readonly Dictionary<string, Script> Scripts = new()
        {
            ["helper"] = new Script
            {
                Seed = 7,
                Setup = g => { g.Rel["seoyeon"].Trust = 1; g.Items["water_jug"] = Own(1); g.Items["cash"] = Own(1); g.SetKnow("elderPick", "help"); g.SetKnow("extinguisherLocated", true); },
                Talk = new[] { "toss", "toss", "yes", "add", "ask", "buy", "rumor", "check", "tell", "take", "shut", "info", "thanks", "visit" },
                Card = new[] { "ok", "ok" },
                Open = new (string, JToken)[] { ("extinguisher", new JObject { ["sequence"] = new JArray("fight", "shout", "pin", "aim", "squeeze", "sweep") }), ("chapterCard", new JObject()) },
                Steps = new[] { "press apartment:homeFridge", "press apartment:homeStock", "press apartment:seoyeonDoor", "press apartment:lobbyNotice", "press apartment:homePlanner",
                    "press apartment:homeExitDoor", "door apartment:lobbyExit", "press town:water", "door town:home", "y 5.6", "press apartment:seoyeonDoor",
                    "door apartment:lobbyExit", "door town:store", "press store:storeCounter", "door store:storeExit", "door town:home", "press apartment:homeBed",
                    "press apartment:extinguisher" },
            },
            ["alone"] = new Script
            {
                Seed = 11,
                Setup = g => { g.Rel["seoyeon"].Trust = 0; },
                Talk = new[] { "eat", "eat", "phone", "go", "take", "official", "spread", "fix", "old", "no", "go", "none", "shout", "water", "retreat", "ok", "later" },
                Card = new[] { "ok" },
                Open = new (string, JToken)[]
                {
                    ("radio", new JObject { ["official"] = true, ["noted"] = new JArray(new JObject { ["text"] = "급수소 09:30~17:00", ["kind"] = "official" }) }),
                    ("chapterCard", new JObject()),
                },
                Steps = new[] { "press apartment:homeFridge", "press apartment:homeRadio", "press apartment:seoyeonDoor", "press apartment:homePlanner",
                    "press apartment:homeExitDoor", "door apartment:lobbyExit", "press town:water", "door town:home", "y 5.6",
                    "door apartment:lobbyExit", "door town:store", "press store:storeCounter", "door store:storeExit", "door town:home", "press apartment:homeBed",
                    "press apartment:hallFire", "press apartment:hallFire", "press apartment:hallFire", "stairs1F apartment:stairs1F" },
            },
            ["fireRetreat"] = new Script
            {
                Seed = 7, Resume = "fire",
                Setup = g => { g.SetKnow("elderPick", "manager"); g.SetKnow("elderTold", true); g.Chapter = "endure"; },
                Talk = new[] { "ok", "later" },
                Open = new (string, JToken)[] { ("extinguisher", new JObject { ["sequence"] = new JArray("retreat") }), ("chapterCard", new JObject()) },
                Steps = new[] { "press apartment:extinguisher", "press apartment:homeSink", "stairs1F apartment:stairs1F" },
            },
            ["fireEscalated"] = new Script
            {
                Seed = 7, Resume = "fire",
                Setup = g => { g.Chapter = "endure"; },
                Talk = new[] { "water" },
                Open = new (string, JToken)[] { ("extinguisher", new JObject { ["sequence"] = new JArray("fight") }) },
                Steps = new[] { "press apartment:hallFire", "press apartment:extinguisher" },
            },
        };

        sealed class Played
        {
            public FakeStoryHost Host;
            public StoryContext Ctx;
            public StoryFail Fail;
            public GameState G => Host.G;
        }

        static Played Play(string name)
        {
            var s = Scripts[name];
            var host = new FakeStoryHost { Strict = true };
            var rules = TestSupport.Rules;
            var ctx = host.CreateContext(rules, rules.CreateGame(s.Seed));
            s.Setup(host.G);
            ctx.Resume = s.Resume;
            host.Answer(s.Talk);
            if (s.Card.Length > 0) host.AnswerCard(s.Card);
            foreach (var (screen, result) in s.Open) host.AnswerOpen(screen, result);
            foreach (var step in s.Steps)
            {
                var parts = step.Split(' ');
                string verb = parts[0], target = parts[1];
                string level = target.Contains(':') ? target.Split(':')[0] : null;
                bool Ready() => ctx.Roaming && host.ControlsEnabled && (level == null || host.LevelId == level);
                switch (verb)
                {
                    case "press": host.QueuePress(target); break;
                    case "door": host.QueueWalk(target); break; // walk onto it: the roam's walk-through door takes you
                    case "y": host.Queue(h => { h.PlayerY = double.Parse(target, System.Globalization.CultureInfo.InvariantCulture); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.LevelId == "apartment", "climb to 3F"); break;
                    case "stairs1F": host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, Ready, "down to 1F"); break;
                    default: throw new ArgumentException(step);
                }
            }
            var p = new Played { Host = host, Ctx = ctx };
            try { host.Run(() => Endure.Run(ctx)); }
            catch (StoryFail f) { p.Fail = f; }
            Assert.That(host.PendingSteps, Is.EqualTo(0), "every scripted step ran");
            Assert.That(host.PendingTalkAnswers, Is.EqualTo(0), "every talk answer was used");
            Assert.That(host.PendingCardAnswers, Is.EqualTo(0), "every card answer was used");
            return p;
        }

        // ------------------------------------------------------------------ JSON compare (numbers by value, like JS)

        static bool Same(JToken a, JToken b)
        {
            bool Nullish(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;
            if (Nullish(a) || Nullish(b)) return Nullish(a) && Nullish(b);
            bool Num(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
            if (Num(a) && Num(b)) return Math.Abs((double)a - (double)b) < 1e-9;
            if (a is JObject oa && b is JObject ob)
            {
                var keys = oa.Properties().Select(x => x.Name).Union(ob.Properties().Select(x => x.Name));
                return keys.All(k => Same(oa[k], ob[k]));
            }
            if (a is JArray aa && b is JArray ab) return aa.Count == ab.Count && aa.Zip(ab, Same).All(x => x);
            return JToken.DeepEquals(a, b);
        }

        static void AssertSame(JToken want, JToken got, string what) =>
            Assert.That(Same(want, got), Is.True, $"{what}\n  want: {want?.ToString(Newtonsoft.Json.Formatting.None)}\n  got:  {got?.ToString(Newtonsoft.Json.Formatting.None)}");

        static JObject Items(GameState g)
        {
            var o = new JObject();
            foreach (var (k, v) in g.Items) if (v.Home != 0 || v.Bag != 0 || v.Used != 0) o[k] = new JArray(v.Home, v.Bag, v.Used, v.Checked);
            return o;
        }

        [TestCase("helper")]
        [TestCase("alone")]
        [TestCase("fireRetreat")]
        [TestCase("fireEscalated")]
        public void MatchesTheOriginal(string name)
        {
            var w = (JObject)Golden[name];
            var p = Play(name);
            var g = p.G;
            var host = p.Host;
            Assert.That(p.Fail?.Type, Is.EqualTo((string)w["fail"]), "fail");
            // events: types in order, then each one's details
            Assert.That(host.EventTypes(), Is.EqualTo(w["events"].Select(e => (string)e[0]).ToList()), "event types");
            for (int i = 0; i < g.Events.Count; i++) AssertSame(w["events"][i][1], g.Events[i].Details, $"event #{i} {g.Events[i].Type}");
            AssertSame(w["items"], Items(g), "items");
            AssertSame(w["know"], JObject.FromObject(g.Know), "know");
            foreach (var who in new[] { "manager", "clerk", "seoyeon" }) Assert.That(g.Rel[who].Trust, Is.EqualTo((double)w["rel"][who]["trust"]), $"trust {who}");
            Assert.That(g.Day, Is.EqualTo((int)w["day"]), "day");
            Assert.That(g.Minute, Is.EqualTo((int)w["minute"]), "minute");
            Assert.That(g.Phone.Battery, Is.EqualTo((double)w["phone"]), "phone battery");
            Assert.That(g.Extra.TryGetValue("karma", out var k) ? (double)k : 0, Is.EqualTo((double)w["karma"]), "karma");
            // stats: the original and the fake pump a different number of frames while walking with the water (rest/strain)
            foreach (var st in new[] { "health", "thirst", "fatigue", "tension" })
            {
                double want = (double)w["stats"][st], got = st switch { "health" => g.Stats.Health, "thirst" => g.Stats.Thirst, "fatigue" => g.Stats.Fatigue, _ => g.Stats.Tension };
                Assert.That(got, Is.EqualTo(want).Within(1.5), st);
            }
            Assert.That(g.Info.Select(i => new[] { i.Text, i.Label }).ToList(), Is.EqualTo(w["info"].Select(i => new[] { (string)i[0], (string)i[1] }).ToList()), "info notes");
            Assert.That(host.Talks.Select(t => new[] { t.Speaker, t.Role ?? "", t.Lines.FirstOrDefault() ?? "" }).ToList(),
                Is.EqualTo(w["talks"].Select(t => new[] { (string)t[0], (string)t[1], (string)t[2] }).ToList()), "talks (speaker, role, first line)");
            Assert.That(host.Cards.Select(c => c.Title).ToList(), Is.EqualTo(w["cards"].Select(x => (string)x).ToList()), "cards");
            Assert.That(host.Opens.Select(o => o.screen).ToList(), Is.EqualTo(w["opens"].Select(x => (string)x).ToList()), "screens");
            Assert.That(host.Captions.Where(c => !PortCaptions.Contains(c)).ToList(), Is.EqualTo(w["captions"].Select(x => (string)x).ToList()), "captions");
            Assert.That(host.Checkpoints, Is.EqualTo(w["checkpoints"].Select(x => (string)x).ToList()), "checkpoints");
            Assert.That(host.TimeSkips.Where(t => !PortSkips.Contains(t.from)).Select(t => new[] { t.from, t.to }).ToList(), Is.EqualTo(w["timeSkips"].Select(x => new[] { (string)x[0], (string)x[1] }).ToList()), "time skips");
        }

        // ------------------------------------------------------------------ key state per branch

        [Test]
        public void Helper_WaterFor303_ThePowerbank_TheStoreAndTheExtinguisher()
        {
            var p = Play("helper");
            var g = p.G; var host = p.Host;
            Assert.That(p.Fail, Is.Null);
            Assert.That(g.Chapter, Is.EqualTo("endure"));
            Assert.That(p.Ctx.SeoyeonAsk, Is.EqualTo("yes"));
            Assert.That(g.Rel["seoyeon"].Trust, Is.EqualTo(4), "1 + 2 for the water + 1 for the memo");
            var fetched = g.Events.Single(e => e.Type == "water_fetched");
            Assert.That((int)fetched.Details["litres"], Is.EqualTo(20));
            Assert.That(g.Items["powerbank"].Home, Is.EqualTo(1)); Assert.That(g.Items["powerbank"].Checked, Is.True);
            Assert.That(g.Items["water_2l"].Home, Is.EqualTo(6), "2 - 1 drunk + (20 - 10) / 2");
            Assert.That(g.Knows("clerkLeft") && g.Knows("waterPoint"), Is.True);
            Assert.That(Deeds.Of(g).Select(d => d.Id), Is.SupersetOf(new[] { "board_offer", "queue_carry_elder", "rumor_corrected", "store_shuttered", "elder_visit" }));
            Assert.That(g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Is.EqualTo(new[] { "water_jug", "cash", "elder", "extinguisher" }));
            Assert.That(host.Level("apartment").State["hallFire"].ToString(), Is.EqualTo("out"));
            Assert.That(host.Level("store").State["clerk"].ToString(), Is.EqualTo("out"));
            Assert.That(host.Level("town").State["waterPoint"].ToString(), Is.EqualTo("off"), "closed at 17:00");
            Assert.That(host.Loops, Does.Not.Contain("fire"));
            Assert.That(host.Carrying, Is.Null);
            Assert.That(p.Ctx.SpeedOverride, Is.Null);
            var cc = host.Opens.Last();
            Assert.That(cc.screen, Is.EqualTo("chapterCard"));
            Assert.That((string)cc.options["chapter"], Is.EqualTo("3장")); Assert.That((string)cc.options["title"], Is.EqualTo("버티기"));
            Assert.That(cc.options["lines"], Is.InstanceOf<JArray>());
            Assert.That(host.Objectives.Select(o => o.title), Is.SupersetOf(new[] { "정전 이틀째 아침", "물 받기", "집으로", "303호에 물 전하기", "저녁 · 할 수 있는 일", "복도의 불" }));
            Assert.That(host.Objectives.Single(o => o.title == "집으로").body, Is.EqualTo("물 20리터를 들고 103동 3층까지 돌아갑니다. 무겁다. 지치면 멈춰 서서 숨을 고르세요."));
            Assert.That(host.Chapter, Is.EqualTo(("3장", "버티기")));
            // what the level json does not list: stock is an object state (the fake checks only plain values); smoke 0.1 is the
            // original's own after-fire haze; store.clerk is set by the original but missing from store-level.json's states.
            Assert.That(host.Warnings.Distinct(), Is.SubsetOf(new[] { "apartment: state 'stock' is not in the level json", "store: state 'clerk' is not in the level json", "apartment: smoke=0.1 is not one of [0,0.25,0.5,0.75,1]" }));
        }

        [Test]
        public void Alone_SpoiledFood_PhoneRadio_RumourSpreadThenFixed_RetreatThroughTheSmoke()
        {
            var p = Play("alone");
            var g = p.G; var host = p.Host;
            Assert.That(p.Fail, Is.Null);
            Assert.That(g.Stats.Health, Is.EqualTo(76), "two spoiled meals, -12 each");
            Assert.That(g.Items["fridge_food"].Home, Is.EqualTo(3), "eating takes one; the rest stays");
            Assert.That(g.Phone.Battery, Is.EqualTo(61), "phone FM costs 3%");
            Assert.That((int)g.Events.Single(e => e.Type == "water_fetched").Details["litres"], Is.EqualTo(4));
            Assert.That(host.Toasts, Has.Member("물통이 없어 빈 생수병 두 개에 4리터만 받았다."));
            Assert.That(g.Items.ContainsKey("powerbank") && g.Items["powerbank"].Home > 0, Is.False);
            Assert.That(g.Knows("misled"), Is.False);
            Assert.That(g.Events.Any(e => e.Type == "help" && (string)e.Details["who"] == "stranger"), Is.False, "not answering is no help");
            Assert.That((string)g.Events.Single(e => e.Type == "fire_retreat").Details["size"], Is.EqualTo("large"), "water made it grow");
            Assert.That(host.Level("apartment").State["hallFire"].ToString(), Is.EqualTo("out"));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Role == "1층"), Is.True);
            Assert.That(host.Talks.Single(t => t.Speaker == "304호 어르신" && t.Role == "304호 앞").Lines[0], Is.EqualTo("어젯밤엔 고마웠어요. 그 소리 듣고 바로 나왔지."), "she heard the shout");
            Assert.That(host.Captions, Has.Member("복도에서 타는 냄새가 난다. 문틈으로 주황빛이 흔들린다."));
        }

        [Test]
        public void FireRetreat_SheKnocks_TheDrillSaysLeave_AWetTowel()
        {
            var p = Play("fireRetreat");
            var g = p.G; var host = p.Host;
            Assert.That(p.Fail, Is.Null);
            Assert.That(host.TimeSkips.Where(t => !PortSkips.Contains(t.from)), Is.Empty, "resumed at the fire");
            Assert.That(host.Captions[0], Does.StartWith("쾅쾅— "));
            Assert.That((bool)g.Events.Single(e => e.Type == "smoke_escape").Details["towel"], Is.True);
            Assert.That(g.Day, Is.EqualTo(3)); Assert.That(g.Minute, Is.EqualTo(10 * 60));
            Assert.That(host.Talks.Single(t => t.Speaker == "304호 어르신").Lines[0], Is.EqualTo("어젯밤엔 다들 놀랐지요. 나는 괜찮아요."));
        }

        [Test]
        public void FireEscalated_FightingTheLargeFireFails()
        {
            var p = Play("fireEscalated");
            Assert.That(p.Fail?.Type, Is.EqualTo("fire"));
            Assert.That(p.Host.Level("apartment").State["hallFire"].ToString(), Is.EqualTo("large"));
            Assert.That((string)p.Host.Opens.Single().options["fire"], Is.EqualTo("large"), "judged on the fire the player saw");
            Assert.That(p.Host.Loops, Does.Not.Contain("fire"), "the sound stops on the fail");
        }

        [Test]
        public void TheFireGrowsByItselfWhileThePlayerHesitates()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.G.Chapter = "endure";
            ctx.Resume = "fire";
            host.Queue(h => h.RunFrames(30 * 29), () => ctx.Roaming && host.ControlsEnabled, "wait 29 s");
            host.Queue(h => h.Press("hallFire"), () => ctx.Roaming && host.ControlsEnabled, "look at the fire");
            host.Answer("retreat");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            host.Answer("ok", "later");
            host.AnswerOpen("chapterCard", new JObject());
            host.Run(() => Endure.Run(ctx));
            Assert.That(host.Toasts, Has.Member("불이 상자 더미로 번졌다. 천장까지 닿을 것 같다."));
            Assert.That(host.Talks.First(t => t.Speaker == "복도").Role, Is.EqualTo("천장까지 닿는 불길"));
            Assert.That((string)host.G.Events.Single(e => e.Type == "fire_retreat").Details["size"], Is.EqualTo("large"));
        }

        [Test]
        public void Thirst_GivesOut_AsASoftFail()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.G.Stats.Thirst = 96;
            var f = host.RunExpectFail(() => Endure.Run(ctx));
            Assert.That(f.Type, Is.EqualTo("exhausted"));
            Assert.That((string)f.Detail["why"], Is.EqualTo("thirst"));
        }

        [Test]
        public void Morning_ThePlannerWaitsForTheDaysNotice_AndTheDoorsWork()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.QueuePresses("apartment:homePlanner", "apartment:homeStock", "apartment:homeStock", "apartment:homeExitDoor");
            host.QueuePresses("apartment:seoyeonDoor", "apartment:lobbyNotice");
            host.Answer("no"); // the board
            host.AnswerCard("ok");
            host.QueuePress("apartment:homePlanner");
            // the water trip starts: stop the run there with a fail of our own
            host.Queue(h => { h.G.Stats.Thirst = 99; h.WalkTo("lobbyExit"); }, () => ctx.Roaming && host.Objectives.Last().title == "비상급수시설로", "out, thirsty");
            var f = host.RunExpectFail(() => Endure.Run(ctx));
            Assert.That(f.Type, Is.EqualTo("exhausted"));
            Assert.That(host.Toasts, Has.Member("먼저 오늘의 안내를 확인하자."));
            Assert.That(host.Toasts, Has.Member("아까 마셨다. 아껴 두자."));
            Assert.That(host.Talks.Any(t => t.Speaker == "303호" && t.Role == "문 너머"), Is.True, "trust 0: 303 is busy");
            Assert.That(host.G.Minute, Is.EqualTo(9 * 60 + 20), "the day starts at 09:20 at the earliest");
            var morning = host.Objectives.Where(o => o.title == "정전 이틀째 아침").Last();
            Assert.That(morning.steps.Select(s => s.Done), Is.EqualTo(new[] { false, true, true }));
            Assert.That(host.G.Events.Any(e => e.Type == "deed"), Is.False, "the board line was not added");
        }
    }
}
