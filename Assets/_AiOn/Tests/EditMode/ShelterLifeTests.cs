// 4장 「임시 거처의 하루」 (competition upgrade): Game/ShelterLifeLogic.cs (the choices, the 마음 words, the breathing
// rules, the review card) and Story/ShelterLifeScene.cs played on FakeStoryHost through Evac.Crossroads (move).
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
    public class ShelterLifeTests
    {
        // ---------------------------------------------------------------- the rules

        [Test]
        public void MindWords_FollowTheTensionBands_AndBreathsCalmALittle()
        {
            Assert.That(ShelterLifeLogic.MindWord(0), Is.EqualTo("차분"));
            Assert.That(ShelterLifeLogic.MindWord(33), Is.EqualTo("차분"));
            Assert.That(ShelterLifeLogic.MindWord(34), Is.EqualTo("조금 불안"));
            Assert.That(ShelterLifeLogic.MindWord(66), Is.EqualTo("조금 불안"));
            Assert.That(ShelterLifeLogic.MindWord(67), Is.EqualTo("불안"));
            // the same bands as the HUD's 긴장 word (GameRules.StatWord)
            foreach (var t in new[] { 0, 33, 34, 66, 67, 100 })
                Assert.That(ShelterLifeLogic.MindWord(t), Is.EqualTo(GameRules.StatWord("tension", t) switch { "좋음" => "차분", "보통" => "조금 불안", _ => "불안" }), t.ToString());
            Assert.That(ShelterLifeLogic.MindLine(70, 60), Is.EqualTo("마음이 조금 가라앉았다 · 불안 → 조금 불안"));
            // the direction shows even when the word stays the same
            Assert.That(ShelterLifeLogic.MindLine(50, 47), Is.EqualTo("마음이 조금 가라앉았다 · 조금 불안"));
            Assert.That(ShelterLifeLogic.MindLine(20, 25), Is.EqualTo("마음이 조금 술렁인다 · 차분"));
            Assert.That(ShelterLifeLogic.MindLine(40, 40), Is.EqualTo("마음: 조금 불안"));
            Assert.That(ShelterLifeLogic.BreathDrop(0), Is.EqualTo(0));
            Assert.That(ShelterLifeLogic.BreathDrop(2), Is.EqualTo(8));
            Assert.That(ShelterLifeLogic.BreathDrop(9), Is.EqualTo(ShelterLifeLogic.BreathDropMax));
            Assert.That(ShelterLifeLogic.BreathDrop(-3), Is.EqualTo(0));
        }

        static void Run(BreathLogic b, double seconds, bool held, double dt = 0.05)
        {
            for (double t = 0; t < seconds - 1e-9; t += dt) b.Tick(dt, held);
        }

        [Test]
        public void Breath_Hold_InThenOut_ThreeBreathsFinish()
        {
            var b = new BreathLogic();
            Assert.That(b.Phase, Is.EqualTo(BreathLogic.Ready));
            Assert.That(b.Fill, Is.EqualTo(0));
            for (int i = 0; i < 3; i++)
            {
                Run(b, 4, true);
                Assert.That(b.Phase, Is.EqualTo(BreathLogic.In), $"breath {i}: holding is breathing in");
                Assert.That(b.Fill, Is.EqualTo(1).Within(0.02));
                Run(b, 0.1, false);
                Assert.That(b.Phase, Is.EqualTo(BreathLogic.Out));
                Run(b, 4.1, false);
            }
            Assert.That(b.Finished, Is.True);
            Assert.That(b.Breaths, Is.EqualTo(3));
            Assert.That(b.Short, Is.EqualTo(0));
            Assert.That(b.Cue, Is.EqualTo("잘했어요"));
            b.Tick(1, true);
            Assert.That(b.Breaths, Is.EqualTo(3), "done stays done");
        }

        [Test]
        public void Breath_LettingGoTooSoon_IsAHintNotAPenalty_AndTheExperienceModesWidenIt()
        {
            var b = new BreathLogic();
            Run(b, 1.5, true);
            Run(b, 0.1, false);
            Assert.That(b.Phase, Is.EqualTo(BreathLogic.Ready));
            Assert.That(b.Short, Is.EqualTo(1));
            Assert.That(b.Hint, Does.Contain("길게"));
            Assert.That(b.Breaths, Is.EqualTo(0));
            // wide: 2 s is enough (the guide still counts 4: the pace never slows)
            var w = new BreathLogic { Wide = true };
            Run(w, 2.2, true);
            Run(w, 0.1, false);
            Assert.That(w.Phase, Is.EqualTo(BreathLogic.Out));
            Assert.That(BreathLogic.InSeconds, Is.EqualTo(4));
            Assert.That(w.Min, Is.LessThan(new BreathLogic().Min));
            // pressing again too early while breathing out: a hint, the breath out goes on
            Run(w, 0.5, true);
            Assert.That(w.Phase, Is.EqualTo(BreathLogic.Out));
            Assert.That(w.Hint, Does.Contain("내쉬어요"));
            // past the minimum, the next press starts the next breath at once
            Run(w, 1.6, false);
            Run(w, 0.1, true);
            Assert.That(w.Breaths, Is.EqualTo(1));
            Assert.That(w.Phase, Is.EqualTo(BreathLogic.In));
        }

        [Test]
        public void Breath_Toggle_OnePressRunsTheGuidedRhythm_AndSwitchingKeepsTheCount()
        {
            var b = new BreathLogic { Toggle = true };
            Run(b, 3, false);
            Assert.That(b.Phase, Is.EqualTo(BreathLogic.Ready), "waits for the one press");
            b.Start();
            Run(b, 8.1, false);
            Assert.That(b.Breaths, Is.EqualTo(1), "no holding needed");
            Assert.That(b.Phase, Is.EqualTo(BreathLogic.In));
            b.SetMode(false);
            Assert.That(b.Breaths, Is.EqualTo(1));
            Assert.That(b.Phase, Is.EqualTo(BreathLogic.Ready));
            Run(b, 3.5, true); Run(b, 4.1, false);
            Run(b, 3.5, true); Run(b, 4.1, false);
            Assert.That(b.Finished, Is.True);
            b.SetMode(true);
            Assert.That(b.Finished, Is.True, "a finished breath stays finished");
        }

        [Test]
        public void Choices_AreShortAndKind_EveryAnswerHasFeedback_AndTheWordingIsCalm()
        {
            foreach (var set in new[] { ShelterLifeLogic.Privacy, ShelterLifeLogic.Sleep, ShelterLifeLogic.SleepDeaf, ShelterLifeLogic.Outlet, ShelterLifeLogic.Water, ShelterLifeLogic.Child, ShelterLifeLogic.Play })
            {
                Assert.That(set.Select(c => c.Id), Is.Unique);
                Assert.That(set.Any(c => c.Kind == "good"), "every choice has a good answer");
                foreach (var c in set)
                {
                    Assert.That(c.Label, Is.Not.Empty); Assert.That(c.Feedback, Is.Not.Empty); Assert.That(c.Short, Is.Not.Empty);
                    Assert.That(new[] { "good", "ok", "rethink" }, Does.Contain(c.Kind), c.Id);
                    Assert.That(c.Tension, Is.InRange(-8, 6), c.Id + ": a light touch, no punishment loop");
                }
            }
            foreach (var t in ShelterLifeLogic.Texts)
            {
                Assert.That(GoldenRuns.HasHangul(t), t);
                foreach (var bad in new[] { "다쳤", "피가", "죽", "사망", "부상", "때리", "멍청", "탓" }) Assert.That(t, Does.Not.Contain(bad), t);
            }
            // the deaf night has no sounds in it
            foreach (var c in ShelterLifeLogic.SleepDeaf) Assert.That(c.Label + c.Feedback, Does.Not.Contain("소리"));
            // the board's notices are this scene's own (marked 가상) and say so on the review card
            Assert.That(ShelterLifeLogic.NightNoticeTitle, Does.Contain("가상"));
            Assert.That(ShelterLifeLogic.MorningNoticeTitle, Does.Contain("가상"));
            Assert.That(ShelterLifeLogic.MorningNotice.Count(l => l.StartsWith("[바뀜]")), Is.GreaterThanOrEqualTo(1), "one rule changes each day");
            Assert.That(ShelterLifeLogic.Charge("turn"), Is.GreaterThan(ShelterLifeLogic.Charge("yield")));
            Assert.That(ShelterLifeLogic.Charge("nope"), Is.EqualTo(0));
        }

        [Test]
        public void Result_RoundTrips_AndTheReviewCardNamesWhatWasDone_WithSourcedFacts()
        {
            var r = new ShelterLifeResult { Played = true, Privacy = "ask", Sleep = "breathe", Outlet = "yield", Notice = "staff", Water = "carry", Child = "watch", Play = "fold", Breaths = 3, MindStart = 70, MindEnd = 30 };
            var back = ShelterLifeResult.Read(r.ToJson());
            Assert.That(JToken.DeepEquals(back.ToJson(), r.ToJson()));
            Assert.That(ShelterLifeResult.Read(null).Played, Is.False);
            var facts = FakeStoryHost.LoadFacts();
            var lines = ShelterLifeLogic.ReviewLines(r, facts.Text, id => facts.Cite(id)?.Source);
            Assert.That(lines, Has.Some.Contains("옆 가족에게 먼저 물어"));
            Assert.That(lines, Has.Some.Contains("할머니께 먼저 양보"));
            Assert.That(lines, Has.Some.Contains("접수대에서 듣고서야"), "the changed rule heard from the staff");
            Assert.That(lines, Has.Some.Contains("종이비행기"));
            Assert.That(lines, Has.Some.Contains("마음이 조금 가라앉았다 · 불안 → 차분 · 숨 고르기 3번"));
            foreach (var id in ShelterLifeLogic.FactIds)
            {
                Assert.That(facts.Text(id), Is.Not.Null.And.Not.Empty, id + " is a sourced fact");
                Assert.That(lines, Has.Some.Contains(facts.Text(id)));
            }
            Assert.That(lines.Last(), Does.Contain("지어낸 것"));
            var read = ShelterLifeLogic.ReviewLines(new ShelterLifeResult { Played = true, Notice = "read", Water = "own" }, facts.Text, null);
            Assert.That(read, Has.Some.Contains("게시판에서 먼저 읽고"));
            // deaf: the night and the play as that profile was told them
            var deaf = ShelterLifeLogic.ReviewLines(new ShelterLifeResult { Played = true, Sleep = "ask", Child = "watch", Play = "radio" }, facts.Text, null, deaf: true);
            Assert.That(deaf, Has.Some.Contains("손짓으로"));
            Assert.That(deaf, Has.Some.Contains("틀어 주었다"));
        }

        [Test]
        public void TheChangedRule_IsTheOneTheMorningsErrandNeeds()
        {
            // the first change on the morning board is where the water is handed out, and it names the point the scene uses
            Assert.That(ShelterLifeLogic.MorningNotice[0], Does.StartWith("[바뀜]").And.Contain("식수").And.Contain("출입구 B"));
            Assert.That(ShelterLifeLogic.NightNotice, Has.Some.Contains("접수대 앞 배급대"), "the night's board says where it was");
            Assert.That(ShelterLifeLogic.RuleFromStaff, Does.Contain("물").And.Contain("출입구 B"), "the staff answers what the player came for");
            Assert.That(ShelterLifeLogic.RuleFromStaff, Does.Not.Contain("충전"));
            Assert.That(ShelterLifeLogic.WaterPoint, Is.EqualTo("fromRamp"));
        }

        [Test]
        public void CarryingTheGrandfathersWater_AsksHimFirst_AndEveryRecordSaysTheSame()
        {
            var carry = ShelterLifeLogic.Find(ShelterLifeLogic.Water, "carry");
            Assert.That(carry.Label, Does.StartWith("할아버지께").And.Contain("여쭙"), "asked the person, not only the manager");
            Assert.That(carry.Feedback, Does.StartWith("할아버지:"), "he answers in his own words");
            Assert.That(carry.Feedback, Does.Not.Contain("고개를 숙"));
            Assert.That(carry.Short, Does.Contain("여쭙고"));
            Assert.That(Deeds.Get("shelter_water_carried").Line, Does.Contain("여쭌 뒤"));
        }

        // ---------------------------------------------------------------- the scene, through 4장 (move)

        static (FakeStoryHost host, StoryContext ctx) Fresh(string profile = "")
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(7), settings: new StorySettings { Profile = profile });
            var g = host.G;
            g.Chapter = "endure"; g.Day = 3; g.Minute = 18 * 60;
            g.Items["water_2l"] = new ItemEntry(6);
            g.Items["canned"] = new ItemEntry(4);
            g.Items["rice_instant"] = new ItemEntry(2);
            g.Stats.Tension = 45;
            return (host, ctx);
        }

        /// <summary>The move: the rumour as neighbour talk, the card's note, then move; the shelter fire stepped back from.</summary>
        static void Move(FakeStoryHost host)
        {
            host.AnswerWhen(o => o.Choices != null && o.Choices.Any(c => c.Id == "neighbor") && o.Choices.Any(c => c.Id == "official"), "neighbor").AnswerSpeaker("정민", "go");
            host.AnswerCard("add", "move");
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("shout", "retreat") });
        }

        /// <summary>The day's presses: the board, the outlet and the mat at night; the board, the water and the mat in the morning.</summary>
        internal static readonly string[] Presses = { "shelter:shelterBoard", "shelter:reception", "shelter:spot", "shelter:shelterBoard", "shelter:" + ShelterLifeLogic.WaterPoint, "shelter:spot" };

        /// <summary>
        /// The day as the scripted playthroughs play it (the scene-select start, the prepared state's capture): the presses,
        /// the first answer everywhere, and stepping back from the shelter's fire after it.
        /// </summary>
        internal static void Day(StoryPlaythroughTests.Rig r)
        {
            r.Press(Presses);
            r.Screens["extinguisher"] = _ => new JObject { ["sequence"] = new JArray("shout", "retreat") };
        }

        static void QueueDay(FakeStoryHost host, IEnumerable<string> presses = null)
        {
            foreach (var p in presses ?? Presses) host.QueuePress(p);
        }

        static List<string> DeedIds(GameState g) => Deeds.Of(g).Select(d => d.Id).ToList();

        [Test]
        public void Move_PlaysTheNightAndTheMorning_BeforeTheFiresCheckpoint_AndRecordsTheDay()
        {
            var (host, ctx) = Fresh();
            Move(host);
            QueueDay(host);
            host.AnswerSpeaker("내 돗자리", "ask");     // privacy: ask the family first
            host.AnswerSpeaker("내 돗자리", "sleep");
            host.AnswerSpeaker("접수대 옆 멀티탭", "yield");
            host.AnswerSpeaker("한밤", "towel");
            host.AnswerSpeaker("급수차", "carry");
            host.AnswerSpeaker("옆 돗자리 지우 엄마", "watch");
            host.AnswerSpeaker("지우", "fold");
            host.Run(() => Evac.Crossroads(ctx));
            var g = host.G;
            Assert.That(host.PendingSteps, Is.EqualTo(0));
            var ev = g.Events.Single(e => e.Type == ShelterLifeLogic.Event);
            var r = ShelterLifeResult.Read(ev.Details);
            Assert.That((r.Privacy, r.Outlet, r.Sleep, r.Notice, r.Water, r.Child, r.Play), Is.EqualTo(("ask", "yield", "towel", "read", "carry", "watch", "fold")));
            // the day sits between the decision and the fire's checkpoint
            var types = host.EventTypes();
            Assert.That(types.IndexOf("decision"), Is.LessThan(types.IndexOf(ShelterLifeLogic.Event)));
            int life = host.Log.FindIndex(c => c.Kind == "card" && c.Text == ShelterLifeLogic.ReviewTitle);
            int cp = host.Log.FindIndex(c => c.Kind == "checkpoint" && c.Text == "fire4");
            Assert.That(life, Is.GreaterThan(0).And.LessThan(cp));
            Assert.That(host.TimeSkips.Select(s => s.to), Is.EqualTo(new[] { "D+6 목 10:00", "D+6 목 18:40", "D+7 금 06:50", "D+7 금 저녁" }));
            Assert.That(host.TimeSkips.Last().from, Is.EqualTo("D+7 금 아침"), "the original's '하루가 지났다' is told by the day itself");
            // the deeds and their endings' axes
            Assert.That(DeedIds(g), Is.SupersetOf(new[] { "shelter_outlet_shared", "shelter_water_carried", "shelter_child_care", "shelter_notice_read" }));
            var a = Deeds.AllAxes(g, true);
            Assert.That(a["care"], Is.GreaterThanOrEqualTo(4));
            Assert.That(a["report"], Is.GreaterThanOrEqualTo(1));
            // the board was read at night and in the morning; the review card shows the sourced lines
            Assert.That(host.Talks.Count(t => t.Speaker == "대피소 게시판"), Is.EqualTo(2));
            Assert.That(host.Talks.First(t => t.Speaker == "대피소 게시판" && t.Role == ShelterLifeLogic.MorningNoticeTitle).Lines, Is.EqualTo(ShelterLifeLogic.MorningNotice));
            var card = host.Cards.Single(c => c.Title == ShelterLifeLogic.ReviewTitle);
            Assert.That(card.Kind, Is.EqualTo("record"));
            Assert.That(card.Body, Has.Some.Contains(ctx.Facts.Text("mind_breath")));
            // the clock: D+7 morning before the evening's fire; the battery went up, the night slept off some fatigue
            Assert.That(g.Day, Is.EqualTo(7));
            Assert.That(host.Toasts, Has.Some.Contains("마음이 조금 가라앉았다"), "each answer says how the 마음 moved");
            // 서연 and 304호 어르신 are not at the temporary stay
            Assert.That((string)ctx.LevelStates["shelter"]["seoyeon"], Is.EqualTo("hidden"));
            Assert.That(host.Calls("state").Select(c => c.Text), Has.Some.EqualTo("shelter.elder=\"off\""));
            // the fire scene after it (the original part, unchanged)
            Assert.That(host.Opens.Count(o => o.screen == "extinguisher"), Is.EqualTo(1));
            Assert.That(host.Warnings.Where(w => w.StartsWith("shelter")), Is.Empty);
        }

        [Test]
        public void TheMindMeterIsOnForTheDayOnly_AndBreathingCalms()
        {
            var (host, ctx) = Fresh();
            var meter = new List<bool>();
            ctx.MindMeter = on => meter.Add(on);
            Move(host);
            QueueDay(host);
            host.AnswerSpeaker("한밤", "breathe");
            host.AnswerOpen(ShelterLifeLogic.Screen, new JObject { ["played"] = true, ["breaths"] = 3, ["mode"] = "hold" });
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(meter, Is.EqualTo(new[] { true, false }));
            var r = ShelterLifeResult.Read(host.G.Events.Single(e => e.Type == ShelterLifeLogic.Event).Details);
            Assert.That(r.Sleep, Is.EqualTo("breathe"));
            Assert.That(r.Breaths, Is.EqualTo(3));
            var opened = host.Opens.Single(o => o.screen == ShelterLifeLogic.Screen).options;
            Assert.That((string)opened["mind"], Is.Not.Empty);
            Assert.That((double)opened["tension"], Is.GreaterThan(0), "the screen settles the 마음 line as breaths finish");
            Assert.That((bool)opened["toggle"], Is.False);
            Assert.That(opened["facts"]["mind_breath"]["source"], Is.Not.Null);
            Assert.That(host.Toasts, Has.Some.Contains("어깨의 힘이"));
        }

        [Test]
        public void GoingForWaterBeforeReadingTheMorningBoard_TheStaffPointsTheWay_NoPenalty()
        {
            var (host, ctx) = Fresh();
            Move(host);
            // the mat (privacy, then 'more'), the mat again (sleep); morning: the old place first, then where the staff said
            QueueDay(host, new[] { "shelter:spot", "shelter:spot", "shelter:reception", "shelter:" + ShelterLifeLogic.WaterPoint, "shelter:spot" });
            host.AnswerSpeaker("내 돗자리", "none").AnswerSpeaker("내 돗자리", "more").AnswerSpeaker("내 돗자리", "sleep");
            host.AnswerSpeaker("한밤", "phone");
            host.AnswerSpeaker("옆 돗자리 지우 엄마", "no");
            double battery = host.G.Phone.Battery;
            host.Run(() => Evac.Crossroads(ctx));
            var r = ShelterLifeResult.Read(host.G.Events.Single(e => e.Type == ShelterLifeLogic.Event).Details);
            Assert.That(r.Outlet, Is.Null, "never went to charge: fine");
            Assert.That(r.Notice, Is.EqualTo("staff"));
            Assert.That(r.Child, Is.EqualTo("no"));
            Assert.That(host.Talks.Any(t => t.Speaker == "대피소 관리자" && t.Lines.Contains(ShelterLifeLogic.RuleFromStaff)));
            Assert.That(r.Water, Is.EqualTo("own"), "the water came from the truck at 출입구 B after all");
            Assert.That(host.Talks.Where(t => t.Speaker == "대피소 관리자" && t.Role == "접수대").SelectMany(t => t.Choices ?? new List<TalkChoice>()), Is.Empty, "the reception hands out no water today");
            Assert.That(DeedIds(host.G).Intersect(new[] { "shelter_outlet_shared", "shelter_water_carried", "shelter_child_care", "shelter_child_referred", "shelter_notice_read" }), Is.Empty,
                "nothing to remember, nothing taken away");
            var card = host.Cards.Single(c => c.Title == ShelterLifeLogic.ReviewTitle);
            Assert.That(card.Body, Has.Some.Contains("○"), "a thing to think about next time");
        }

        [Test]
        public void WithoutARadio_TheChildsRadioChoiceIsOff_AndTheDeafNightHasNoSounds()
        {
            var (host, ctx) = Fresh("deaf");
            host.G.Items.Remove("radio");
            Move(host);
            QueueDay(host);
            host.Run(() => Evac.Crossroads(ctx));
            var night = host.Talks.Single(t => t.Speaker == "한밤");
            Assert.That(string.Join(" ", night.Lines), Does.Not.Contain("소리"));
            Assert.That(night.Choices.Select(c => c.Label), Has.Some.Contains("손짓"));
            var play = host.Talks.Single(t => t.Speaker == "지우");
            Assert.That(play.Choices.Single(c => c.Id == "radio").Disabled, Is.True);
            Assert.That(play.Choices.Single(c => c.Id == "radio").Label, Is.EqualTo(ShelterLifeLogic.PlayDeaf[0].Label), "the deaf play: the radio is for 지우");
            foreach (var c in ShelterLifeLogic.PlayDeaf) Assert.That(c.Label + c.Feedback, Does.Not.Contain("노래").And.Not.Contain("같이 듣"));
            Assert.That(ShelterLifeResult.Read(host.G.Events.Single(e => e.Type == ShelterLifeLogic.Event).Details).Play, Is.EqualTo("fold"));
        }

        [Test]
        public void BreathingWithTheChild_StoppedAtOnce_CountsAsSittingBesideHer()
        {
            var (host, ctx) = Fresh();
            Move(host);
            QueueDay(host);
            host.AnswerSpeaker("지우", "breathe"); // the screen hands back {} (no breath finished)
            host.Run(() => Evac.Crossroads(ctx));
            var r = ShelterLifeResult.Read(host.G.Events.Single(e => e.Type == ShelterLifeLogic.Event).Details);
            Assert.That(r.Play, Is.EqualTo(ShelterLifeLogic.PlayStay.Id));
            Assert.That(r.Breaths, Is.EqualTo(0));
            Assert.That((string)host.Opens.Single(o => o.screen == ShelterLifeLogic.Screen).options["with"], Is.EqualTo("child"));
            Assert.That(host.Cards.Single(c => c.Title == ShelterLifeLogic.ReviewTitle).Body, Has.Some.Contains("곁에 앉아"));
            Assert.That(host.Cards.Single(c => c.Title == ShelterLifeLogic.ReviewTitle).Body, Has.None.Contains("함께 숨을 골랐다"));
        }

        [Test]
        public void ARetryAtTheFire_DoesNotPlayTheDayAgain_AndStayingNeverPlaysIt()
        {
            var (host, ctx) = Fresh();
            host.G.SetKnow("stayMove", "move");
            ctx.Resume = "fire4";
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("shout", "retreat") });
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.G.Events.Any(e => e.Type == ShelterLifeLogic.Event), Is.False);
            var (h2, c2) = Fresh();
            h2.AnswerWhen(o => o.Choices != null && o.Choices.Any(c => c.Id == "neighbor"), "neighbor").AnswerSpeaker("정민", "go");
            h2.AnswerCard("add", "stay");
            h2.AnswerSpeaker("판단", "stay");
            h2.Run(() => Evac.Crossroads(c2));
            Assert.That(h2.G.Events.Any(e => e.Type == ShelterLifeLogic.Event), Is.False);
            Assert.That(h2.Calls("enter").Select(c => c.Text), Has.None.StartsWith("shelter"));
        }

        [Test]
        public void TheTeacherRoute_KeepsToItsTime_AMovingClassGetsTheOriginalSkip()
        {
            var (host, ctx) = Fresh();
            ctx.TeacherRun = true;
            Move(host);
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.G.Events.Any(e => e.Type == ShelterLifeLogic.Event), Is.False);
            Assert.That(host.Cards.Any(c => c.Title == ShelterLifeLogic.ReviewTitle), Is.False);
            Assert.That(host.TimeSkips.Last().from, Is.EqualTo("D+6 목"), "the original's '하루가 지났다'");
            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "fire4" }));
            Assert.That(host.Opens.Count(o => o.screen == "extinguisher"), Is.EqualTo(1), "the shelter fire still plays");
        }

        [Test]
        public void TheDay_IsLeftOutOfTheChapterTime()
        {
            var (host, ctx) = Fresh();
            int begun = 0, ended = 0;
            ctx.BeginUntimed = () => { begun++; return () => ended++; };
            Move(host);
            QueueDay(host);
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That((begun, ended), Is.EqualTo((1, 1)));
        }

        [Test]
        public void TheSceneSelectStart_PlaysTheDayFromD6_ThenTheFire()
        {
            var (host, ctx) = Fresh();
            var g = host.G;
            g.Chapter = "crossroads"; g.Day = 6; g.Minute = 10 * 60; g.SetKnow("stayMove", "move");
            ctx.Resume = Evac.ShelterLifeResume;
            QueueDay(host);
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("shout", "retreat") });
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.Cards.Any(c => c.Title == "지금 내 상황"), Is.False, "the decision is not asked again");
            Assert.That(host.G.Events.Count(e => e.Type == ShelterLifeLogic.Event), Is.EqualTo(1));
            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "fire4" }));
            Assert.That(host.Calls("enter").First().Text, Is.EqualTo("shelter:spot"));
        }
    }
}
