// "불나면 살펴서 대피" in the story (Story/LookFirstScene.cs, Endure.CorridorFire): what the screen is given (the fire's
// start, what is at hand, the 1장 partition, the sourced facts), what each choice does to the chapter (leave -> the corridor
// and the smoke escape, then the 119 call from the lobby; stay / partition -> no corridor, the firefighters come), the
// elevator correction, the 1장 partition interaction and how it pays off, the learning report rows and the ripple.
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class LookFirstSceneTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig(int seed = 7)
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(seed));
            host.G.Chapter = "endure";
            ctx.Resume = "fire";
            return (host, ctx);
        }

        static JObject Played(string act, string size = "small", JObject seal = null, JObject partition = null, bool report = true, int elevator = 0) => new JObject
        {
            ["played"] = true, ["mode"] = "look", ["clues"] = new JArray("handle", "gap", "sound"), ["seconds"] = 10, ["size"] = size, ["grew"] = size == "large",
            ["where"] = "corridor", ["whereOk"] = true, ["located"] = 3, ["act"] = act, ["verdict"] = act == "leave" ? "good" : "caution", ["elevator"] = elevator,
            ["seal"] = seal, ["partition"] = partition,
            ["report"] = report ? new JObject { ["played"] = true, ["fire"] = true, ["place"] = true, ["what"] = true, ["people"] = true, ["line"] = true, ["score"] = 5 } : null,
        };

        static void PlayChapter(FakeStoryHost host, StoryContext ctx) => host.Run(() => Endure.Run(ctx));

        [Test]
        public void TheScreen_IsGivenTheFire_WhatIsAtHand_ThePartition_AndTheFacts()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.SetKnow("tubFilled", true);
            g.SetKnow(LookFirstScene.ClearedKnow, true);
            ctx.Settings.Profile = "wheelchair";
            host.AnswerOpen(LookFirstScene.Screen, Played("stay", seal: new JObject { ["towel"] = true, ["window"] = true, ["away"] = true, ["wet"] = "tub" }));
            host.Answer("wait", "ok", "later");
            PlayChapter(host, ctx);
            var o = host.Opens.First(x => x.screen == LookFirstScene.Screen).options;
            Assert.That((string)o["mode"], Is.EqualTo("look"));
            Assert.That((bool)o["partitionCleared"], Is.True);
            Assert.That((bool)o["tub"], Is.True);
            Assert.That((string)o["profile"], Is.EqualTo("wheelchair"));
            Assert.That((bool)o["knocked"], Is.EqualTo(Elder.ElderSafeEarly(g.Know)));
            var facts = FakeStoryHost.LoadFacts();
            foreach (var id in LookFirstScene.FactIds)
            {
                Assert.That((string)o["facts"][id]["text"], Is.EqualTo(facts.Text(id)), id);
                Assert.That((string)o["facts"][id]["source"], Is.Not.Empty, id);
            }
        }

        [Test]
        public void Staying_SkipsTheCorridor_TheFirefightersPutItOut_AndTheTubPaysOff()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            GameRules.RecordEvent(g, "tub_filled", new JObject());
            g.SetKnow("tubFilled", true);
            host.AnswerOpen(LookFirstScene.Screen, Played("stay", seal: new JObject { ["towel"] = true, ["window"] = true, ["away"] = true, ["wet"] = "tub" }));
            host.Answer("shout", "ok", "later");
            PlayChapter(host, ctx);
            Assert.That(host.Opens.Select(x => x.screen), Has.No.Member("extinguisher"));
            Assert.That(g.Events.Any(e => e.Type == "smoke_escape"), Is.False, "no walk through the smoke");
            var ev = g.Events.Single(e => e.Type == "lookfirst");
            Assert.That((string)ev.Details["act"], Is.EqualTo("stay"));
            Assert.That(g.Events.Single(e => e.Type == "fire_waited").Details["act"].ToString(), Is.EqualTo("stay"));
            Assert.That(host.Talks.Any(t => t.Speaker == "소방대원" && t.Lines[0].StartsWith("302호 분")), Is.True);
            Assert.That(host.Level("apartment").State["hallFire"].ToString(), Is.EqualTo("out"));
            Assert.That(g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Does.Contain("tub"));
            Assert.That(g.Events.Any(e => e.Type == "chapter_done" || e.Type == "fire_retreat"), Is.False);
            Assert.That(g.Day, Is.EqualTo(3), "the chapter goes on to D+3");
            // alerting from inside and staying are the 3장 fire's judgement too (deeds fire_warned, fire_handled)
            Assert.That(g.Events.Single(e => e.Type == "fire_shout").Details["from"].ToString(), Is.EqualTo("stay"));
            var deeds = Deeds.Of(g).Select(d => d.Id).ToList();
            Assert.That(deeds, Does.Contain("fire_warned").And.Contain("fire_handled"));
        }

        [Test]
        public void ThePartition_WithoutAShout_StillCountsAsHandled_AndThePanelStaysBroken()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.AnswerOpen(LookFirstScene.Screen, Played("partition", "large", partition: new JObject { ["cleared"] = false, ["boxes"] = 3, ["open"] = true }));
            host.Answer("wait", "ok", "later");
            PlayChapter(host, ctx);
            Assert.That(g.Events.Any(e => e.Type == "fire_shout"), Is.False);
            var deeds = Deeds.Of(g).Select(d => d.Id).ToList();
            Assert.That(deeds, Does.Contain("fire_handled").And.Not.Contain("fire_warned"));
            Assert.That(LookFirstScene.Broken(g), Is.True);
            // back home in 5장 (ApplyHome): the hole stays, and the panel says so
            g.SetKnow(LookFirstScene.ClearedKnow, true);
            PrepActions.ApplyHome(ctx);
            Assert.That(host.Level("apartment").State[LookFirstScene.StateKey].ToString(), Is.EqualTo("broken"));
            Assert.That(World.Desc["apartment:homePartition"](ctx).First(), Does.Contain("부수고 301호로"));
        }

        [Test]
        public void WhenTheElderKnocked_ThereIsNoSecondShout()
        {
            var (host, ctx) = Rig();
            host.G.SetKnow("elderPick", "help");
            host.AnswerOpen(LookFirstScene.Screen, Played("stay", seal: new JObject { ["wet"] = "dry" }));
            host.Answer("ok", "later");
            PlayChapter(host, ctx);
            Assert.That(host.Talks.Any(t => t.Role == "알리기"), Is.False);
        }

        [Test]
        public void Leaving_TheStairsStartTheEscape_WithoutGoingToTheFire()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.AnswerOpen(LookFirstScene.Screen, Played("leave"));
            host.AnswerOpen(LookFirstScene.Screen, new JObject { ["played"] = true, ["mode"] = "report", ["report"] = new JObject { ["played"] = true, ["score"] = 5, ["place"] = true } });
            host.Answer("ok", "later");
            host.QueuePress("apartment:stairs3F");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            PlayChapter(host, ctx);
            Assert.That(g.Events.Any(e => e.Type == "fire_retreat"), Is.True);
            Assert.That(g.Events.Any(e => e.Type == "smoke_escape"), Is.True);
            Assert.That(host.Talks.Any(t => t.Speaker == "복도"), Is.False, "no walk up to the fire");
            Assert.That(host.Opens.Count(x => x.screen == "extinguisher"), Is.EqualTo(0));
        }

        [Test]
        public void Leaving_WalkingDownTheStairs_IsLeavingToo_AndTheCallKnowsTheFiresSize()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            host.AnswerOpen(LookFirstScene.Screen, Played("leave", "large"));
            host.AnswerOpen(LookFirstScene.Screen, new JObject { ["played"] = true, ["mode"] = "report", ["report"] = new JObject { ["played"] = true, ["score"] = 5, ["place"] = true } });
            host.Answer("ok", "later");
            host.Queue(h => { h.MoveTo(5, -0.7, 3.0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "복도의 불", "stairwell");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            PlayChapter(host, ctx);
            Assert.That((string)g.Events.Single(e => e.Type == "fire_retreat").Details["size"], Is.EqualTo("large"));
            Assert.That(g.Events.Any(e => e.Type == "smoke_escape"), Is.True);
            var call = host.Opens.Where(x => x.screen == LookFirstScene.Screen).Last().options;
            Assert.That((string)call["mode"], Is.EqualTo("report"));
            Assert.That((string)call["size"], Is.EqualTo("large"));
        }

        [Test]
        public void TheElevatorStop_IsOnlyOn3F_AndIsSeenInDeafMode()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "lobby");
                host.MoveTo(1.2, -0.6, 0);
                await LookFirstScene.ElevatorStop(ctx);
            });
            Assert.That(host.Talks, Is.Empty, "서연 is not in the 1F lobby");
            Assert.That(host.Toasts, Has.Member(FakeStoryHost.LoadFacts().Text("fire_lift")));

            var (deafHost, deafCtx) = Rig();
            deafCtx.Settings.Profile = "deaf";
            deafHost.Run(async () => { await deafCtx.Goto("apartment", "corridor3F"); await LookFirstScene.ElevatorStop(deafCtx); });
            Assert.That(deafHost.Talks.Single().Lines[0], Does.Contain("계단 쪽을 가리킨다").And.Not.Contain("외친다"));
        }

        [Test]
        public void In1장_TheHomeObjective_ListsThePartition_AsOptional()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.G.Chapter = "prep";
            host.Answer("clear");
            host.QueuePresses(LookFirstScene.Point, "homePlanner");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoHome(ctx); });
            var home = host.Objectives.Where(o => o.title == "집 안 대비").ToList();
            var first = home.First().steps.Last();
            Assert.That(first.Text, Does.Contain("경량칸막이").And.Contain("(선택)"));
            Assert.That(first.Done, Is.False);
            Assert.That(home.Last().steps.Last().Done, Is.True, "ticked once the boxes are moved");
        }

        [Test]
        public void ThePartition_ClearedIn1장_PaysOff_IsBroken_AndSeoyeonRemembersIt()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            GameRules.RecordEvent(g, "partition_cleared", new JObject());
            g.SetKnow(LookFirstScene.ClearedKnow, true);
            host.AnswerOpen(LookFirstScene.Screen, Played("partition", "large", partition: new JObject { ["cleared"] = true, ["boxes"] = 0, ["open"] = true }));
            host.Answer("wait", "ok", "later");
            PlayChapter(host, ctx);
            Assert.That(g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Does.Contain("partition"));
            Assert.That(host.Level("apartment").State[LookFirstScene.StateKey].ToString(), Is.EqualTo("broken"));
            Assert.That(host.Talks.Single(t => t.Speaker == "소방대원").Lines[0], Does.StartWith("301호로 피하신 분"));
            Assert.That(host.Talks.First(t => t.Speaker == "오서연").Lines[0], Does.Contain("301호로 피하셨다면서요"));
            var row = Ripples.Of(g).Single(r => r.Prep == "partition");
            Assert.That(row.Cause.Text, Is.EqualTo("베란다 경량칸막이 앞 상자를 치워 두었다"));
            Assert.That(row.Label, Is.EqualTo("경량칸막이 앞 비우기"));
        }

        [Test]
        public void Leaving_GoesToTheCorridorAsBefore_ThenThe119CallFromTheLobby()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.SetKnow("elderPick", "manager"); g.SetKnow("elderTold", true); // she knocks: the fire is caught small
            host.AnswerOpen(LookFirstScene.Screen, Played("leave"));
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("retreat") });
            host.AnswerOpen(LookFirstScene.Screen, new JObject { ["played"] = true, ["mode"] = "report", ["report"] = new JObject { ["played"] = true, ["place"] = true, ["what"] = true, ["people"] = true, ["line"] = true, ["fire"] = true, ["score"] = 5 } });
            host.Answer("ok", "later");
            host.QueuePress("apartment:extinguisher");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            PlayChapter(host, ctx);
            var opens = host.Opens.Where(x => x.screen == LookFirstScene.Screen).ToList();
            Assert.That(opens, Has.Count.EqualTo(2));
            Assert.That((string)opens[1].options["mode"], Is.EqualTo("report"));
            Assert.That((string)opens[1].options["caller"], Is.EqualTo("lobby"));
            Assert.That(g.Events.Any(e => e.Type == "smoke_escape"), Is.True, "the 3D smoke escape is the leave branch");
            Assert.That(g.Events.Single(e => e.Type == "fire_report").Details["score"].ToString(), Is.EqualTo("5"));
            Assert.That(host.Talks.Single(t => t.Speaker == "박영수 소장").Lines[0], Does.Contain("신고 잘했어요"));
            Assert.That(host.Objectives.Any(o => o.body != null && o.body.Contains("엘리베이터는 쓰지 않습니다")), Is.True);
        }

        [Test]
        public void NothingPlayed_LeavesTheChapterAsTheOriginal()
        {
            var (host, ctx) = Rig();
            host.Answer("retreat", "ok", "later");
            host.QueuePress("apartment:hallFire");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            PlayChapter(host, ctx);
            Assert.That(host.G.Events.Any(e => e.Type == "lookfirst" || e.Type == "fire_report"), Is.False);
            Assert.That(host.Opens.Count(x => x.screen == LookFirstScene.Screen), Is.EqualTo(1), "no call from the lobby when nothing was played");
            Assert.That(host.Talks.Single(t => t.Speaker == "박영수 소장").Lines[0], Does.Contain("119에 신고했어요"));
        }

        [Test]
        public void ALargeFireSeenWhileLooking_IsTheFireInTheCorridor()
        {
            var (host, ctx) = Rig();
            host.AnswerOpen(LookFirstScene.Screen, Played("leave", "large"));
            host.Answer("retreat", "ok", "later");
            host.QueuePress("apartment:hallFire");
            host.Queue(h => { h.MoveTo(5, 0.9, 0); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.Objectives.Last().title == "연기 속에서 1층까지", "down");
            PlayChapter(host, ctx);
            Assert.That(host.Talks.First(t => t.Speaker == "복도").Role, Is.EqualTo("천장까지 닿는 불길"));
            Assert.That((string)host.G.Events.Single(e => e.Type == "fire_retreat").Details["size"], Is.EqualTo("large"));
        }

        [Test]
        public void TheElevator_DuringTheFire_ANeighbourStopsYou()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                await LookFirstScene.ElevatorStop(ctx);
            });
            var t = host.Talks.Single();
            Assert.That(t.Speaker, Is.EqualTo(LookFirstLogic.Neighbour));
            Assert.That(t.Lines[0], Does.Contain("계단"));
            Assert.That(t.Lines[1], Is.EqualTo(FakeStoryHost.LoadFacts().Text("fire_lift")));
            Assert.That(host.G.Events.Single(e => e.Type == "elevator_tried").Details["fire"].ToString(), Is.EqualTo("True"));
        }

        // ---------------------------------------------------------------- 1장

        [Test]
        public void In1장_ClearingTheBoxes_SetsTheFlag_TheEvent_TheState_AndTakesTenMinutes()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(3));
            host.G.Chapter = "prep";
            int minute = host.G.Minute;
            host.Answer("clear");
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                await PrepActions.Partition(ctx);
                await PrepActions.Partition(ctx); // again: only a reminder
            });
            var g = host.G;
            Assert.That(g.Knows(LookFirstScene.ClearedKnow), Is.True);
            Assert.That(g.Events.Count(e => e.Type == "partition_cleared"), Is.EqualTo(1));
            Assert.That(host.Level("apartment").State[LookFirstScene.StateKey].ToString(), Is.EqualTo("clear"));
            Assert.That(g.Minute, Is.EqualTo(minute + 10));
            Assert.That(host.Talks.Single().Lines[1], Is.EqualTo(FakeStoryHost.LoadFacts().Text("fire_partition")));
            Assert.That(host.Toasts, Has.Member(FakeStoryHost.LoadFacts().Text("fire_partition_clear")));
            Assert.That(host.Level("apartment").Point(LookFirstScene.Point), Is.Not.Null, "the level has the point");
            var report = LearningReport.Build(g);
            Assert.That(report.Rows.Single(r => r.Id == "partition").Ok, Is.True);
        }

        [Test]
        public void In1장_Later_LeavesTheBoxes()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(3));
            host.Answer("later");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.Partition(ctx); });
            Assert.That(host.G.Knows(LookFirstScene.ClearedKnow), Is.False);
            Assert.That(host.Level("apartment").State.ContainsKey(LookFirstScene.StateKey), Is.False, "the box pile is the default");
        }

        // ---------------------------------------------------------------- the learning report

        [Test]
        public void TheLearningReport_ReadsTheStep_AndTheCall()
        {
            var g = TestSupport.Rules.CreateGame(5);
            GameRules.RecordEvent(g, "lookfirst", Played("stay", seal: new JObject { ["wet"] = "dry" }, elevator: 1));
            var r = LearningReport.Build(g);
            var fire = r.Rows.Where(x => x.Axis == "fire").ToDictionary(x => x.Id);
            Assert.That(fire["lookWhere"].Ok, Is.True);
            Assert.That(fire["lookWhere"].FactId, Is.EqualTo("fire_look"));
            Assert.That(fire["lookAct"].Ok, Is.False, "staying while the way was clear: caution");
            Assert.That(fire["lookAct"].FactId, Is.EqualTo("fire_seal"));
            Assert.That(fire["lookLift"].Ok, Is.False);
            Assert.That(fire["report119"].Ok, Is.True);
            Assert.That(fire["report119"].FactId, Is.EqualTo("fire_report"));

            var lobby = TestSupport.Rules.CreateGame(5);
            GameRules.RecordEvent(lobby, "fire_report", new JObject { ["played"] = true, ["score"] = 3 });
            Assert.That(LearningReport.Build(lobby).Rows.Single(x => x.Id == "report119").Ok, Is.False);

            var facts = FakeStoryHost.LoadFacts();
            foreach (var row in r.Rows) Assert.That(facts.Text(row.FactId), Is.Not.Empty, row.Id);
        }

        // ---------------------------------------------------------------- 장면 바로가기

        [Test]
        public void TheSceneSelectMoment_IsReady_AtItsOwnResumeKey()
        {
            var m = SceneSelect.Get("look");
            Assert.That(m.Ready, Is.True);
            Assert.That(m.Key, Is.EqualTo(Endure.LookResume));
            Assert.That(m.Chapter, Is.EqualTo("endure"));
            // a prepared start at "look" plays the fire's evening (no D+1 / D+2 days before it)
            var (host, ctx) = Rig();
            ctx.Resume = Endure.LookResume;
            host.AnswerOpen(LookFirstScene.Screen, Played("stay", seal: new JObject { ["wet"] = "dry" }));
            host.Answer("wait", "ok", "later");
            PlayChapter(host, ctx);
            Assert.That(host.TimeSkips.Select(t => t.from), Has.No.Member("D-day 금 밤"));
            Assert.That(host.Opens.First().screen, Is.EqualTo(LookFirstScene.Screen));
        }
    }
}
