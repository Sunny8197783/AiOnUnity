// The shelter's bystander CPR in the story (Story/CprScene.cs): what the "cpr" screen is given (the ladder stage, the
// experience mode, the sourced facts) and what the story keeps from its result (the "cpr" event, the Drills encounter,
// the deeds on the town axis, the learning report's 응급 처치 rows, the Fates line, the clock, the paramedics' toast).
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class CprSceneTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.G.Chapter = "dday";
            host.G.Minute = DDay.Alert + 20;
            return (host, ctx);
        }

        static JObject Played(int swaps, double firstSwapAt, int touches = 0) => new JObject
        {
            ["played"] = true, ["stage"] = "guided", ["named"] = true, ["vague"] = 0, ["self"] = 0, ["breathSec"] = 6.5,
            ["compressions"] = 240, ["misses"] = 9, ["quality"] = 0.84, ["shallowAt"] = swaps > 0 ? -1 : 88, ["gaugeAtShallow"] = swaps > 0 ? -1 : 0.93,
            ["firstSwapAt"] = firstSwapAt, ["swaps"] = swaps, ["aedTouches"] = touches, ["resumeDelay"] = 1.2, ["seconds"] = 168,
            ["curve"] = new JObject { ["t"] = new JArray(0, 1), ["q"] = new JArray(0.5, 0.9), ["g"] = new JArray(0.4, 0.8), ["d"] = new JArray(1, 1), ["fd"] = new JArray(1, 1), ["who"] = new JArray("me", "me") },
        };

        static void Play(FakeStoryHost host, StoryContext ctx) => host.Run(async () =>
        {
            await ctx.Goto("shelter", "spot");
            await CprScene.Run(ctx);
        });

        [Test]
        public void TheScreen_IsGivenTheStage_TheMode_AndTheSourcedFacts()
        {
            var (host, ctx) = Rig();
            ctx.Settings.Profile = "deaf"; ctx.Settings.ReduceMotion = true;
            host.AnswerOpen(CprScene.Screen, Played(1, 50));
            Play(host, ctx);
            var o = host.Opens.Single(x => x.screen == CprScene.Screen).options;
            Assert.That((string)o["stage"], Is.EqualTo("guided"), "the first time is the guided stage");
            Assert.That((string)o["profile"], Is.EqualTo("deaf"));
            Assert.That((bool)o["reduceMotion"], Is.True);
            var facts = FakeStoryHost.LoadFacts();
            foreach (var id in CprScene.FactIds)
            {
                Assert.That((string)o["facts"][id]["text"], Is.EqualTo(facts.Text(id)), id);
                Assert.That((string)o["facts"][id]["source"], Is.Not.Empty, id);
            }
            Assert.That((string)o["facts"]["cpr_point"]["text"], Does.Contain("지목"));
            Assert.That(host.Talks.First().Lines[0], Does.Contain("쓰러졌다"));
            Assert.That((bool)o["demo"], Is.False, "the story is not a 장면 바로가기 start");
        }

        [Test]
        public void APreparedRun_TellsTheScreenItIsADemo_TheStoryNever()
        {
            var (_, ctx) = Rig();
            Assert.That((bool)CprScene.Options(ctx, "guided")["demo"], Is.False, "the story keeps the AED at 100 s");
            ctx.Resume = "alert"; // the story's own checkpoint, which the 공습경보 shortcut also starts from
            Assert.That((bool)CprScene.Options(ctx, "guided")["demo"], Is.False, "the key alone is not a prepared run");
            ctx.DemoRun = true; // StoryDirector.PlayPrepared: 장면 바로가기 or a 교사용 모드 step, whichever moment
            Assert.That((bool)CprScene.Options(ctx, "guided")["demo"], Is.True, "the short AED and the skip");
            ctx.Resume = DDay.CprResume;
            Assert.That((bool)CprScene.Options(ctx, "guided")["demo"], Is.True);
            Assert.That(new CprLogic(demo: true).AedTime, Is.EqualTo(CprLogic.DemoAedAt));
        }

        [Test]
        public void ARelayRun_IsRemembered_AsTheEvent_TheEncounter_AndTwoTownDeeds()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            int minute = g.Minute;
            host.AnswerOpen(CprScene.Screen, Played(2, 48));
            Play(host, ctx);
            var ev = g.Events.Single(e => e.Type == "cpr");
            Assert.That((bool)ev.Details["played"], Is.True);
            Assert.That((int)ev.Details["swaps"], Is.EqualTo(2));
            Assert.That(ev.Details["curve"], Is.Null, "the save keeps the summary, not the curve");
            var enc = g.Events.Single(e => e.Type == "encounter");
            Assert.That((string)enc.Details["tool"], Is.EqualTo(CprScene.Tool));
            Assert.That((string)enc.Details["result"], Is.EqualTo("relay"));
            Assert.That(g.Skill[CprScene.Tool], Has.Count.EqualTo(1));
            Assert.That(Drills.LadderStage(g, CprScene.Tool), Is.EqualTo("hint"), "the next time is the hint stage");
            var deeds = Deeds.Of(g).Select(d => d.Id).ToList();
            Assert.That(deeds, Is.SupersetOf(new[] { "cpr_helped", "cpr_relay" }));
            Assert.That(Deeds.Get("cpr_helped").W["town"], Is.GreaterThan(0));
            Assert.That(g.Minute, Is.EqualTo(minute + CprScene.Minutes));
            Assert.That(g.Knows("cprDone"), Is.True);
            Assert.That(host.Toasts, Does.Contain("구급대가 할아버지를 넘겨받아 구급차로 옮겼다."));
            Assert.That(host.Toasts, Has.None.Contains("지치기 전에"), "no reminder after a swap");
            Assert.That(host.Calls("removePerson").Count(), Is.EqualTo(1));
            Assert.That(host.Level("shelter").Spawned, Is.Empty, "the grandfather is taken away");
            var report = LearningReport.Build(g);
            Assert.That(report.Axes.Single(a => a.Id == "care").Total, Is.EqualTo(5));
            Assert.That(report.Rows.Where(r => r.Axis == "care").All(r => r.Ok), Is.True);
            // the 2장 closing card names it in words, never the raw tool id or the docs card's "skipped"
            var lines = Debrief.ChapterSummary(TestSupport.Rules, g, "dday").Lines;
            Assert.That(lines, Has.Some.EqualTo("심폐소생술: 이웃과 교대하며 구급대에 넘겼습니다"));
            Assert.That(lines, Has.None.StartsWith("cpr"));
            Assert.That(lines, Has.None.Contains("아무것도 적지 않았습니다"));
        }

        [Test]
        public void ASoloRun_GetsTheHelpDeedOnly_AndTheSwapReminder()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.AnswerOpen(CprScene.Screen, Played(0, -1, touches: 1));
            Play(host, ctx);
            var deeds = Deeds.Of(g).Select(d => d.Id).ToList();
            Assert.That(deeds, Does.Contain("cpr_helped"));
            Assert.That(deeds, Does.Not.Contain("cpr_relay"));
            Assert.That((string)g.Events.Single(e => e.Type == "encounter").Details["result"], Is.EqualTo("solo"));
            Assert.That(host.Toasts, Does.Contain(FakeStoryHost.LoadFacts().Text("cpr_fatigue")));
            var rows = LearningReport.Build(g).Rows.Where(r => r.Axis == "care").ToDictionary(r => r.Id);
            Assert.That(rows["cprSwap"].Ok, Is.False);
            Assert.That(rows["cprAed"].Ok, Is.False);
            Assert.That(rows["cprPoint"].Ok, Is.True);
        }

        [Test]
        public void AnEmptyResult_RecordsNothingToBeProudOrAshamedOf()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            Play(host, ctx); // the fake host's default answer: {}
            Assert.That((bool)g.Events.Single(e => e.Type == "cpr").Details["played"], Is.False);
            Assert.That((string)g.Events.Single(e => e.Type == "encounter").Details["result"], Is.EqualTo("skipped"));
            Assert.That(Deeds.Of(g).Select(d => d.Id), Has.None.StartsWith("cpr_"));
            Assert.That(LearningReport.Build(g).Rows.Where(r => r.Axis == "care"), Is.Empty);
            Assert.That(Debrief.ChapterSummary(TestSupport.Rules, g, "dday").Lines, Has.None.Contains("심폐소생술").And.None.StartsWith("cpr"), "not played: no line");
            Assert.That(host.Toasts, Does.Contain("구급대가 할아버지를 넘겨받아 구급차로 옮겼다."), "it always ends with the paramedics");
        }

        [Test]
        public void TheSceneHasAFateLine_AndOldSavesWithoutTheToolStillLoad()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            Assert.That(g.Skill.ContainsKey(CprScene.Tool), Is.False, "not in the original's tool list (GameStateTests pins it)");
            host.AnswerOpen(CprScene.Screen, Played(1, 40));
            Play(host, ctx);
            var back = SaveCodec.Load(SaveCodec.Save(g), TestSupport.Rules.Db);
            Assert.That(back.Skill[CprScene.Tool], Has.Count.EqualTo(1), "the extra tool survives a save");
            Assert.That(Deeds.Of(back).Select(d => d.Id), Does.Contain("cpr_helped"));
            Assert.That(Fates.PeopleLines(back, "bus").Select(p => p.Line), Has.Some.Contains("할아버지의 가족"));
            var fresh = SaveCodec.Load(SaveCodec.Save(TestSupport.Rules.CreateGame(3)), TestSupport.Rules.Db);
            CprScene.EnsureTool(fresh);
            Assert.That(Drills.LadderStage(fresh, CprScene.Tool), Is.EqualTo("guided"));
        }
    }
}
