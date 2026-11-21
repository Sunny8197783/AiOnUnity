// 3장 「산에서 내려온 사람들」 (competition upgrade): the report card's rules (SightingReport, MountainLogic), the facts it
// stands on, and the sub-scene on FakeStoryHost (MountainScene: the traces on D+1, the D+2 day indoors, the record card).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class MountainSceneTests
    {
        /// <summary>The D+2 day's presses (the playthrough rigs queue them after D+1's bed).</summary>
        public static readonly string[] DayPoints = { "apartment:homeDesk", "apartment:homeExitDoor", "apartment:homeWindow", "apartment:homePlanner" };
        /// <summary>The same as StoryPlaythroughTests.Rig.Steps take them.</summary>
        public static readonly string[] DaySteps = DayPoints.Select(p => "press " + p).ToArray();

        /// <summary>
        /// The D+2 beats as a careful player answers them (the playthrough rigs, so the prepared saves carry a careful
        /// run): the group-chat claim labelled a rumour and checked, the stranger asked through the intercom and checked
        /// with the office, the children warned and 1338 called. Everything else takes the first choice.
        /// </summary>
        public static void AnswerCarefully(FakeStoryHost x)
        {
            bool heard = false;
            x.AnswerWhen(o => { if (o.Speaker != "103동 단톡방 (퍼온 글)" && o.Speaker != "복도에서 들린 말") return false; heard = true; return true; }, "rumor");
            x.AnswerWhen(o => heard && o.Speaker == "정민" && o.Role == "판단" && o.Choices != null && o.Choices.Any(c => c.Id == "check"), "check");
            x.AnswerWhen(o => o.Speaker == "현관 인터폰" && o.Choices != null && o.Choices.Any(c => c.Id == "silent"), "ask");
            x.AnswerWhen(o => o.Speaker == "현관 인터폰" && o.Choices != null && o.Choices.Any(c => c.Id == "office"), "office");
            x.AnswerWhen(o => o.Speaker == "거실 창밖" && o.Choices != null && o.Choices.Any(c => c.Id == "shout"), "shout");
        }

        /// <summary>A player's report card: from home, every line as seen, then 112 (the screen's result shape).</summary>
        public static JObject Sighting(JObject o) => Play(SightSetup.FromOptions(o), f => f.Options.First(x => x.Kind == "exact").Id);

        static JObject Play(SightSetup setup, Func<SightField, string> pick, string how = "report", string channel = "call112")
        {
            var r = new SightingReport(setup);
            r.ChooseHow(how);
            if (how != "report") r.ChooseHow("report");
            while (r.Current != null) r.Pick(pick(r.Current));
            r.Submit();
            r.ChooseChannel(channel);
            if (!SightingReport.ReachesPolice(channel)) r.ChooseChannel(SightingReport.ChannelOptions(setup.Profile, setup.Phone).First(o => o.Kind == "exact").Id);
            return r.Result();
        }

        // ---------------------------------------------------------------- the rules

        [Test]
        public void EveryLineAsSeen_IsFullMarks_NoGuess_AndLiftsEarly()
        {
            var res = SightResult.Read(Sighting(new JObject()));
            Assert.That(res.Played && res.HowOk && res.ChannelOk, Is.True);
            Assert.That(res.Score, Is.EqualTo(12)); Assert.That(res.Full, Is.EqualTo(12));
            Assert.That(res.Guesses, Is.Zero);
            Assert.That(res.Accurate, Is.True);
            Assert.That(res.Lift, Is.EqualTo("early"));
            Assert.That(MountainLogic.LiftMinute("early"), Is.EqualTo(17 * 60 + 30));
        }

        [Test]
        public void SayingIDontKnow_IsHonest_NotAGuess_ButAnAllUnknownCardLiftsLate()
        {
            var o = Play(new SightSetup(), f => f.Options.FirstOrDefault(x => x.Kind == "honest")?.Id ?? f.Options.First(x => x.Kind == "exact").Id);
            var res = SightResult.Read(o);
            Assert.That(res.Guesses, Is.Zero);
            Assert.That(res.Unknowns, Is.EqualTo(5), "every field but 어디서 has a \"모른다\"");
            Assert.That(res.Score, Is.EqualTo(7));
            Assert.That(res.Lift, Is.EqualTo("evening"));
            Assert.That(res.Accurate, Is.False);
        }

        [Test]
        public void TheRumoursNumber_OrAnythingNotSeen_IsAGuess_AndKeepsTheControlOvernight()
        {
            var o = Play(new SightSetup(), f => f.Options.FirstOrDefault(x => x.Kind == "wrong")?.Id ?? f.Options[0].Id);
            var res = SightResult.Read(o);
            Assert.That(res.Guesses, Is.EqualTo(6));
            Assert.That(res.Score, Is.Zero);
            Assert.That(res.Lift, Is.EqualTo("late"));
            Assert.That(MountainLogic.LiftMinute("late"), Is.Null);
            Assert.That((string)o["fields"]["count"], Is.EqualTo("dozens"), "수십 명 is the group chat's number");
            // one guess and the rest as seen: not early, but the evening
            var one = SightResult.Read(Play(new SightSetup(), f => f.Id == "count" ? "dozens" : f.Options.First(x => x.Kind == "exact").Id));
            Assert.That(one.Guesses, Is.EqualTo(1));
            Assert.That(one.Lift, Is.EqualTo("evening"));
        }

        [Test]
        public void GoingBack_FollowingOrFilming_IsExplained_AndOnlyTheFirstPickIsJudged()
        {
            foreach (var wrong in new[] { "back", "photo", "follow" })
            {
                var r = new SightingReport(new SightSetup());
                r.ChooseHow(wrong);
                Assert.That(r.Phase, Is.EqualTo(SightPhase.How), wrong + ": the page stays");
                Assert.That(r.Feedback.Ok, Is.False);
                Assert.That(r.Feedback.Facts, Is.Not.Empty);
                r.ChooseHow("report");
                Assert.That(r.Phase, Is.EqualTo(SightPhase.Card));
                Assert.That(r.FirstHow, Is.EqualTo(wrong));
                Assert.That((bool)r.Result()["howOk"], Is.False);
            }
            var res = SightResult.Read(Play(new SightSetup(), f => f.Options.First(x => x.Kind == "exact").Id, how: "photo", channel: "chat"));
            Assert.That(res.HowOk, Is.False);
            Assert.That(res.ChannelOk, Is.False);
            Assert.That(res.Channel, Is.EqualTo("call112"));
        }

        [Test]
        public void TheCard_CanGoBackAField_AndIsNotSentHalfWritten()
        {
            var r = new SightingReport(new SightSetup());
            r.ChooseHow("report");
            r.Pick("seen"); r.Pick("hill");
            Assert.That(r.FieldIndex, Is.EqualTo(2));
            r.Back();
            Assert.That(r.Current.Id, Is.EqualTo("where"));
            r.Pick("path");
            r.Submit();
            Assert.That(r.Phase, Is.EqualTo(SightPhase.Card), "four lines are still empty");
            Assert.That((bool)r.Result()["played"], Is.False);
            r.Pick("nonsense");
            Assert.That(r.FieldIndex, Is.EqualTo(2), "an unknown answer is ignored");
        }

        [Test]
        public void TheMemo_GivesTheExactMinute_AndTheDeafProfileSeesTheTextReportFirst()
        {
            var noted = new SightingReport(new SightSetup { Noted = true, SeenMinute = 9 * 60 + 27 });
            Assert.That(noted.Fields[0].Options.Single(o => o.Kind == "exact").Text, Does.Contain("9시 27분").And.Contain("메모"));
            var blurry = new SightingReport(new SightSetup());
            Assert.That(blurry.Fields[0].Options.Single(o => o.Kind == "exact").Text, Does.Contain("9시 반쯤"));
            Assert.That(SightingReport.ChannelOptions("deaf")[0].Id, Is.EqualTo("text112"));
            Assert.That(SightingReport.ChannelOptions("")[0].Id, Is.EqualTo("call112"));
            Assert.That(SightingReport.ChannelOptions("").Select(o => o.Text), Has.None.Match(@"(?<!\d)(1337|111|113)(?!\d)"));
            var back = SightSetup.FromOptions(new SightSetup { Noted = true, SeenMinute = 600, Profile = "deaf" }.ToJson());
            Assert.That(back.Noted && back.SeenMinute == 600 && back.Profile == "deaf", Is.True);
        }

        [Test]
        public void TheCard_NeverNamesAnEnemy_AndTheWrongAnswersSayWhy()
        {
            var r = new SightingReport(new SightSetup());
            foreach (var f in r.Fields)
            {
                Assert.That(f.Options.Count(o => o.Kind == "exact"), Is.EqualTo(1), f.Id);
                foreach (var o in f.Options)
                {
                    Assert.That(o.Text, Does.Not.Contain("북한").And.Not.Contain("간첩").And.Not.Contain("공비"), o.Id);
                    if (o.Kind == "wrong" || o.Kind == "vague") Assert.That(o.Why, Is.Not.Null.And.Not.Empty, $"{f.Id}.{o.Id}");
                }
            }
            Assert.That(SightingReport.RuleLine, Does.StartWith("[게임 규칙]"), "the lift is a game rule, not a fact");
            Assert.That(MountainLogic.DeclareSms, Does.Contain("병종사태").And.Contain("외출").And.Contain("112").And.Contain("(가상)"));
            Assert.That(MountainLogic.Area, Is.EqualTo(StoryCommon.Area));
            Assert.That(MountainLogic.DeclareSms, Does.StartWith("[").And.Contain("1338").And.EndWith(StoryCommon.Area), "the alert format; objects go to 1338 or the police station");
            Assert.That(MountainLogic.DeclareSms, Does.Not.Contain("북한"), "the official text names no one");
        }

        [Test]
        public void TheIntercomAndTheObject_AreJudgedByTheFirstMoveAndTheRightNumber()
        {
            Assert.That(MountainLogic.IntercomOk("ask"), Is.True);
            Assert.That(MountainLogic.IntercomOk("open"), Is.False);
            Assert.That(MountainLogic.IntercomOk("silent"), Is.False);
            Assert.That(MountainLogic.ObjectFirstOk("shout"), Is.True);
            Assert.That(MountainLogic.ObjectFirstOk("go"), Is.False);
            Assert.That(MountainLogic.ObjectReportOk("1338") && MountainLogic.ObjectReportOk("police"), Is.True);
            Assert.That(MountainLogic.ObjectReportOk("office"), Is.False);
        }

        // ---------------------------------------------------------------- facts and the rumour

        [Test]
        public void EveryFactTheSubSceneShows_IsSourced_AndTheRecordNamesNoOne()
        {
            var facts = FakeStoryHost.LoadFacts();
            var ids = new HashSet<string>(MountainScene.FactIds.Concat(MountainLogic.RecordFacts).Append(MountainLogic.TodayFact)
                .Concat(new[] { "defense_stage_c", "defense_zone", "object_no_touch", "report_object", "door_talk", "info_appearance", "radio_listen", "info_scope" }));
            var r = new SightingReport(new SightSetup());
            foreach (var o in SightingReport.HowOptions) { r = new SightingReport(new SightSetup()); r.ChooseHow(o.Id); foreach (var f in r.Feedback.Facts) ids.Add(f); }
            foreach (var id in ids)
            {
                Assert.That(facts.Text(id), Is.Not.Null.And.Not.Empty, id);
                Assert.That(facts.Source(facts.SourceId(id)), Is.Not.Null, id);
            }
            Assert.That(facts.Text("defense_stage_c"), Does.Contain("병종사태").And.Contain("소규모"));
            Assert.That(facts.Source(facts.SourceId("defense_stage_c")).Url, Does.StartWith("https://www.law.go.kr/"));
            Assert.That(facts.Text("suspect_report"), Does.Contain("112").And.Not.Contain("113").And.Not.Contain("111"));
            Assert.That(facts.Text("report_object"), Does.Contain("1338"));
            Assert.That(facts.Text("record_1996_tips"), Does.Contain("실마리"), "the article's point: a small tip can count");
            Assert.That(facts.Text("record_1996_taxi"), Does.Not.Contain("다시 확인"), "the card does not hold up going back to check");
            foreach (var id in MountainLogic.RecordFacts)
            {
                var t = facts.Text(id);
                Assert.That(t, Does.Not.Contain("이진규").And.Not.Contain("홍사근").And.Not.Contain("정순자").And.Not.Contain("이인수"), id + ": no names");
                Assert.That(t, Does.Not.Contain("사살").And.Not.Contain("사망").And.Not.Contain("희생"), id + ": no victims or killing");
            }
        }

        [Test]
        public void TheRumour_IsAPortSet_AndTheOriginalSetsStayAsTheyWere()
        {
            Assert.That(Rumors.All.Select(x => x.Id), Is.EqualTo(new[] { "shelter_closed", "camp_food", "evac_shortcut" }));
            var set = Rumors.Rumor(MountainLogic.RumorId);
            Assert.That(set.Chapter, Is.EqualTo("endure"));
            var g = TestSupport.Rules.CreateGame(7);
            var posts = Rumors.Posts(g, MountainLogic.RumorId);
            Assert.That(posts[0].Truth, Is.False);
            Assert.That(posts[0].Text, Does.Contain("수십 명"));
            Assert.That(posts.Where(p => p.Truth).Select(p => p.Label), Is.EquivalentTo(new[] { "site", "official" }));
            Assert.That(posts.First(p => p.Key == "manager").Text, Does.Not.Contain("가 봤"), "the trusted adult does not walk to the closed hill");
            Assert.That(posts.First(p => p.Key == "official").Text, Does.Not.Contain("북한"), "the official line names no one");
            Assert.That(Rumors.Judge(g, MountainLogic.RumorId, "rumor").Ok, Is.True);
            Assert.That(Rumors.RumorFacts(MountainLogic.RumorId, FakeStoryHost.LoadFacts().Text).Count, Is.EqualTo(set.Facts.Count));
        }

        // ---------------------------------------------------------------- the story

        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(7));
            host.G.Chapter = "endure";
            host.G.Day = 1; host.G.Minute = 9 * 60 + 25;
            return (host, ctx);
        }

        [Test]
        public void TheTraces_AreNotedWithoutTouching_AndReachingForTheBagIsStoppedGently()
        {
            var (host, ctx) = Rig();
            host.Answer("bag", "note");
            host.Run(() => MountainScene.Traces(ctx));
            var ev = host.G.Events.Single(e => e.Type == MountainScene.TracesEvent);
            Assert.That((bool)ev.Details["reached"], Is.True);
            Assert.That((bool)ev.Details["noted"], Is.True);
            Assert.That((int)ev.Details["minute"], Is.EqualTo(9 * 60 + 25));
            Assert.That((string)host.G.Know[MountainScene.TracesKnow], Is.EqualTo("noted"));
            Assert.That(host.Toasts.Any(t => t.Contains(FakeStoryHost.LoadFacts().Text("info_suspect"))), "the fact behind not touching");
            Assert.That(host.Toasts.Any(t => t.StartsWith("메모: 09:25 뒷산 산책로 입구")), Is.True);
            host.Run(() => MountainScene.Traces(ctx));
            Assert.That(host.G.Events.Count(e => e.Type == MountainScene.TracesEvent), Is.EqualTo(1), "once");
        }

        static void PlayDay(FakeStoryHost host, StoryContext ctx, JToken sighting)
        {
            host.AnswerOpen(MountainScene.Screen, sighting);
            host.QueuePresses(DayPoints);
            host.Run(() => MountainScene.Run(ctx));
        }

        [Test]
        public void TheDay_TextRumourReportIntercomObject_ThenTheLiftAndTheNight()
        {
            var (host, ctx) = Rig();
            host.Answer("note");
            host.Run(() => MountainScene.Traces(ctx));
            // checks the rumour on the radio, asks through the intercom and the office, shouts and calls 1338
            host.Answer("rumor", "check", "tell", "ask", "office", "shout", "1338");
            PlayDay(host, ctx, Sighting(new JObject { ["noted"] = true, ["seenMinute"] = 9 * 60 + 25 }));
            var g = host.G;
            var types = g.Events.Select(e => e.Type).ToList();
            Assert.That(types.Count(t => t == "sms"), Is.EqualTo(2), "the 병종사태 text and the lift");
            Assert.That(types, Does.Contain("rumor_checked").And.Contain("sighting").And.Contain("intercom").And.Contain("unknown_object").And.Contain("defense_lift"));
            Assert.That(types.IndexOf("sighting"), Is.LessThan(types.IndexOf("intercom")));
            Assert.That(types.IndexOf("intercom"), Is.LessThan(types.IndexOf("unknown_object")));
            var opened = host.Opens.Select(o => o.screen).ToList();
            Assert.That(opened.First(), Is.EqualTo("emergencyAlert"));
            var sightOpts = host.Opens.Single(o => o.screen == MountainScene.Screen).options;
            Assert.That((bool)sightOpts["noted"], Is.True);
            Assert.That((string)sightOpts["facts"]["report_5w"]["source"], Does.Contain("국토안보부"));
            Assert.That((string)g.Know[MountainScene.LiftKnow], Is.EqualTo("early"));
            Assert.That(g.Day, Is.EqualTo(2));
            Assert.That(g.Minute, Is.EqualTo(21 * 60 + 40), "the skip card's time, before the fire checkpoint");
            Assert.That(host.Calls("timeSkip").Select(c => c.Text), Is.EqualTo(new[] { "D+1 토 밤 -> D+2 일 08:10", "D+2 일 저녁 -> D+2 일 21:40" }));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("담당이 바뀐")), "the stranger was a courier the office knows (new to the route)");
            Assert.That(host.Talks.Any(t => t.Speaker == "112 상황실" && t.Lines[0].Contains("산책로 입구와 방향")), "an accurate report's reply");
            // nobody is spawned: no enemy, no stranger on screen
            Assert.That(host.Calls("spawnPerson"), Is.Empty);
            // the learning report and the deeds read it
            var rows = LearningReport.Build(g).Rows.ToDictionary(r => r.Id);
            Assert.That(rows["sightSafe"].Ok && rows["sightReport"].Ok && rows["doorTalk"].Ok && rows["objectHandsOff"].Ok && rows["rumorChecked"].Ok, Is.True);
            var deeds = Deeds.Of(g).Select(d => d.Id).ToList();
            Assert.That(deeds, Does.Contain("mountain_rumor_checked").And.Contain("sighting_reported").And.Contain("intercom_checked").And.Contain("object_reported"));
        }

        [Test]
        public void BelievingTheRumour_OpeningTheDoor_AndGoingDown_AreCorrectedWithoutPunishment()
        {
            var (host, ctx) = Rig();
            host.Answer("pass");
            host.Run(() => MountainScene.Traces(ctx));
            ctx.Settings.Profile = "deaf";
            // labels the claim official and acts on it; opens the door; first wants to go down, then shouts; tells only the office
            host.Answer("official", "go", "open", "go", "shout", "office");
            PlayDay(host, ctx, Play(new SightSetup(), f => f.Options.FirstOrDefault(x => x.Kind == "wrong")?.Id ?? f.Options[0].Id, how: "back"));
            var g = host.G;
            Assert.That(g.Events.Any(e => e.Type == "rumor_believed"), Is.True);
            Assert.That(g.Events.Any(e => e.Type == "rumor_checked"), Is.False, "정민 did not check it himself");
            Assert.That((string)g.Events.Single(e => e.Type == "rumor_corrected").Details["after"], Is.EqualTo("believed"), "서연 brings the official word");
            Assert.That(Rumors.Misled(g), Is.Null, "the false claim is corrected the same morning");
            Assert.That(host.Talks.First(t => t.Speaker == "오서연").Lines[0], Does.StartWith("현관문 틈으로 쪽지"), "deaf: a note, not a voice");
            var door = g.Events.Single(e => e.Type == "intercom").Details;
            Assert.That((bool)door["opened"], Is.True);
            var obj = g.Events.Single(e => e.Type == "unknown_object").Details;
            Assert.That((string)obj["first"], Is.EqualTo("go"));
            Assert.That((string)obj["reported"], Is.EqualTo("office"));
            Assert.That((string)g.Know[MountainScene.LiftKnow], Is.EqualTo("late"));
            Assert.That(host.Talks.Any(t => t.Speaker == "112 상황실" && t.Lines[0].Contains("직접 보신 것과 들으신 것")), "a guessed report's reply");
            var rows = LearningReport.Build(g).Rows.ToDictionary(r => r.Id);
            Assert.That(rows["sightSafe"].Ok || rows["sightReport"].Ok || rows["doorTalk"].Ok || rows["objectHandsOff"].Ok, Is.False);
            Assert.That(rows.ContainsKey("rumorBelieved"), Is.True);
            Assert.That(rows.ContainsKey("rumorChecked"), Is.False, "believing is not checking first");
            Assert.That(rows["rumorCorrected"].Ok, Is.True);
            // a guessed card from someone who first wanted to go back earns no "본 것만" deed and no ending line about it
            Assert.That(Deeds.Of(g).Select(d => d.Id), Has.No.Member("object_reported").And.No.Member("intercom_checked").And.No.Member("mountain_rumor_checked")
                .And.No.Member("sighting_reported").And.No.Member("sighting_honest"));
            Assert.That(host.Talks.Any(t => t.Speaker == "정민" && t.Lines[0].Contains("택배 기사라는 말만")), Is.False, "opened without asking: the plain line");
        }

        [Test]
        public void TheDeeds_FollowTheCard_HonestGapsAreNotGuesses()
        {
            GameState G(JObject sighting)
            {
                var g = TestSupport.Rules.CreateGame(7);
                GameRules.RecordEvent(g, "sighting", sighting);
                return g;
            }
            List<string> Ids(GameState g) => Deeds.Of(g).Select(d => d.Id).ToList();
            var exact = Ids(G(Sighting(new JObject())));
            Assert.That(exact, Does.Contain("sighting_reported").And.No.Member("sighting_honest"));
            var honest = Ids(G(Play(new SightSetup(), f => f.Options.FirstOrDefault(x => x.Kind == "honest")?.Id ?? f.Options.First(x => x.Kind == "exact").Id)));
            Assert.That(honest, Does.Contain("sighting_honest").And.No.Member("sighting_reported"));
            var oneGuess = Ids(G(Play(new SightSetup(), f => f.Id == "count" ? "dozens" : f.Options.First(x => x.Kind == "exact").Id)));
            Assert.That(oneGuess, Has.No.Member("sighting_honest").And.No.Member("sighting_reported"));
            var wentBack = Ids(G(Play(new SightSetup(), f => f.Options.First(x => x.Kind == "exact").Id, how: "back")));
            Assert.That(wentBack, Does.Contain("sighting_honest").And.No.Member("sighting_reported"), "an exact card, but the first idea was to go back");
            // the review names the cause: a guess, or honest gaps
            Assert.That(MountainLogic.LiftLine("late", 0), Does.Contain("모르는 칸").And.Not.Contain("짐작이 섞여"));
            Assert.That(MountainLogic.LiftLine("late", 3), Does.Contain("짐작이 섞여"));
            Assert.That(MountainLogic.LiftLine("evening", 1), Does.Contain("짐작").And.Not.Contain("모르는 칸"));
            Assert.That(MountainLogic.LiftLine("evening", 0), Does.Contain("모르는 칸"));
        }

        [Test]
        public void WithTheDeadPhone_TheReportGoesThroughANeighbourOrTheOffice()
        {
            var (host, ctx) = Rig();
            host.G.Phone.Battery = 0;
            host.Answer("note");
            host.Run(() => MountainScene.Traces(ctx));
            Assert.That(host.Talks[0].Choices[0].Label, Does.Contain("종이에").And.Not.Contain("휴대폰"));
            // the text cannot be read; the rumour comes by word of mouth and is checked; no group-chat reply is offered
            host.Answer("rumor", "check", "ask", "office", "shout", "office1338");
            var o = Play(new SightSetup { Phone = false }, f => f.Options.First(x => x.Kind == "exact").Id, channel: "wait");
            Assert.That((string)o["channel"], Is.EqualTo("borrow112"), "after the explanation, through 서연's phone");
            Assert.That((bool)o["channelOk"], Is.False, "putting it off was the first pick");
            PlayDay(host, ctx, o);
            var g = host.G;
            Assert.That(host.Opens.Select(x => x.screen), Has.No.Member("emergencyAlert"));
            Assert.That((bool)host.Opens.Single(x => x.screen == MountainScene.Screen).options["phone"], Is.False);
            Assert.That(host.Talks.Any(t => t.Speaker == "103동 단톡방"), Is.False, "no group chat without the phone");
            var door = host.Talks.First(t => t.Speaker == "현관 인터폰" && t.Choices != null && t.Choices.Any(c => c.Id == "office"));
            Assert.That(door.Choices.Select(c => c.Id), Has.No.Member("police"));
            var where = host.Talks.Single(t => t.Lines.Count > 0 && t.Lines[0] == "어디에 알릴까?");
            Assert.That(where.Choices.Select(c => c.Id), Is.EqualTo(new[] { "office1338", "office" }));
            Assert.That(LearningReport.Build(g).Rows.Single(r => r.Id == "objectHandsOff").Ok, Is.True);
            Assert.That(host.Talks.Any(t => t.Role == "경찰 · 서연의 휴대폰"), "112 through 서연's phone");
            var chans = SightingReport.ChannelOptions("", false).Select(c => c.Id).ToList();
            Assert.That(chans, Is.EqualTo(new[] { "borrow112", "office112", "wait" }));
            Assert.That(SightingReport.ChannelOptions("deaf", false)[0].Text, Does.Contain("문자"));
            Assert.That(SightSetup.FromOptions(new JObject()).Phone, Is.True, "an old options object has the phone");
        }

        [Test]
        public void TheControlKeptOvernight_IsLiftedByTheMorningText()
        {
            var (host, ctx) = Rig();
            GameRules.RecordEvent(host.G, "defense_lift", new JObject { ["lift"] = "late" });
            host.G.Day = 3; host.G.Minute = 10 * 60;
            host.Run(() => MountainScene.MorningAfter(ctx));
            host.Run(() => MountainScene.MorningAfter(ctx));
            var texts = host.Opens.Where(o => o.screen == "emergencyAlert").ToList();
            Assert.That(texts, Has.Count.EqualTo(1), "once");
            Assert.That((string)texts[0].options["text"], Does.Contain("해제").And.EndWith(MountainLogic.Area));
            Assert.That((string)host.G.Know[MountainScene.LiftKnow], Is.EqualTo("morning"));
            // lifted the evening before: nothing the next morning
            var (h2, c2) = Rig();
            GameRules.RecordEvent(h2.G, "defense_lift", new JObject { ["lift"] = "early" });
            h2.Run(() => MountainScene.MorningAfter(c2));
            Assert.That(h2.Opens, Is.Empty);
            Assert.That(MountainLogic.LiftSms("late", "19:00"), Does.Contain("내일 아침").And.StartWith("[안전안내문자] 19:00"));
        }

        [Test]
        public void TheIntercom_AskedThenOpened_NamesTheRealSlip_AndThePoliceBranchHasNoManager()
        {
            var (host, ctx) = Rig();
            host.Answer("ask", "open");
            host.Run(() => MountainScene.Intercom(ctx));
            Assert.That(host.Talks.Last(t => t.Speaker == "정민").Lines[0], Does.Contain("택배 기사라는 말만 듣고 열었다"));
            Assert.That(host.Talks.First(t => t.Speaker == "현관 인터폰").Lines[0], Does.Contain("비상전원"), "the blackout: the intercom runs on emergency power");
            var (h2, c2) = Rig();
            h2.Answer("ask", "police");
            h2.Run(() => MountainScene.Intercom(c2));
            var deeds = Deeds.Of(h2.G).Select(d => d.Id).ToList();
            Assert.That(deeds, Does.Contain("intercom_police").And.No.Member("intercom_checked"), "박 소장 is not in this branch");
            Assert.That(h2.Talks.Any(t => t.Speaker == "박영수 소장"), Is.False);
        }

        [Test]
        public void WithoutTheTraces_ThereIsNoReport_AndTheDoorRingsFirst()
        {
            var (host, ctx) = Rig();
            host.Answer("rumor", "go", "ask", "office", "shout", "police");
            host.QueuePresses("apartment:homeExitDoor", "apartment:homeWindow", "apartment:homePlanner");
            host.Run(() => MountainScene.Run(ctx));
            var g = host.G;
            Assert.That(host.Opens.Select(o => o.screen), Has.No.Member(MountainScene.Screen));
            Assert.That(g.Events.Any(e => e.Type == "sighting"), Is.False);
            Assert.That(g.Know.ContainsKey(MountainScene.LiftKnow), Is.False, "no report: the 통제 stays overnight");
            Assert.That((string)g.Events.Single(e => e.Type == "defense_lift").Details["lift"], Is.EqualTo("late"));
            Assert.That(host.Captions.First(c => c.Contains("인터폰")), Does.Contain("띵동"));
            Assert.That(MountainScene.Played(g), Is.False, "no record card without the sub-scene's traces or report");
        }

        [Test]
        public void TheRecordCard_ThreeSourcedLinesAndTodaysLine_Once()
        {
            var (host, ctx) = Rig();
            host.Answer("note");
            host.Run(() => MountainScene.Traces(ctx));
            host.Run(() => MountainScene.RecordCard(ctx));
            host.Run(() => MountainScene.RecordCard(ctx));
            var cards = host.Cards.Where(c => c.Title == MountainLogic.RecordTitle).ToList();
            Assert.That(cards, Has.Count.EqualTo(1));
            var facts = FakeStoryHost.LoadFacts();
            var body = cards[0].Body;
            for (int i = 0; i < 3; i++) Assert.That(body[i], Is.EqualTo(facts.Text(MountainLogic.RecordFacts[i])));
            Assert.That(body[3], Does.Contain("서울신문"));
            Assert.That(body[4], Does.Contain(facts.Text(MountainLogic.TodayFact)));
            Assert.That(cards[0].Kind, Is.EqualTo("record"));
        }

        [Test]
        public void TheChapter_WithTheSubScene_KeepsItsCheckpointsAndEndsWithTheRecordCard()
        {
            var (host, ctx) = Rig();
            host.Answer("note");
            host.Run(() => MountainScene.Traces(ctx));
            ctx.Resume = Endure.MountainResume;
            host.Answer("rumor", "check", "quiet", "ask", "office", "shout", "1338");
            host.AnswerOpen(MountainScene.Screen, Sighting(new JObject()));
            host.AnswerOpen("extinguisher", JObject.Parse("{ sequence: ['fight', 'shout', 'pin', 'aim', 'squeeze', 'sweep'] }"));
            host.QueuePresses(DayPoints);
            host.QueuePress("apartment:extinguisher");
            host.Run(() => Endure.Run(ctx));
            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "fire" }), "no new checkpoint key");
            int chapterCard = host.Log.FindIndex(c => c.Kind == "open" && c.Text == "chapterCard");
            int record = host.Log.FindIndex(c => c.Kind == "card" && ((CardOptions)c.Data).Title == MountainLogic.RecordTitle);
            Assert.That(chapterCard, Is.GreaterThan(0));
            Assert.That(record, Is.GreaterThan(chapterCard), "the record card follows the chapter card");
            Assert.That(host.G.Events.Count(e => e.Type == "defense_lift"), Is.EqualTo(1));
            Assert.That(host.G.Day, Is.EqualTo(3));
        }
    }
}
