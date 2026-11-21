// 루머 해부 (RumorAnatomy, PrebunkScene), 휴대폰 배터리 자원 (PhoneBattery) and the low-vision overlay speech
// (OverlaySpeech): the rules, the facts they cite, the archive and report, and the scenes that call them.
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PrebunkTests
    {
        static readonly Facts Merged = FakeStoryHost.LoadFacts();

        [SetUp] public void Live() { PrebunkScene.Enabled = true; PhoneBattery.Live = true; }
        [TearDown] public void Restore() { PrebunkScene.Enabled = true; PhoneBattery.Live = true; } // a filtered run must not leave the seams off

        static GameState Game(string chapter = "endure")
        {
            var g = TestSupport.Rules.CreateGame(7);
            g.Chapter = chapter;
            return g;
        }

        // ---------------------------------------------------------------- 루머 해부: the rules

        [Test]
        public void EveryPost_ShowsEachOfItsMarks_AndAllFiveTechniquesAppear()
        {
            var g = Game();
            foreach (var a in RumorAnatomy.All)
            {
                var post = a.Post(g);
                Assert.That(post, Is.Not.Empty, a.Id);
                Assert.That(RumorAnatomy.Found(post, a.Marks).Count, Is.EqualTo(a.Marks.Length), a.Id + ": every phrase is in the post");
                foreach (var m in a.Marks) Assert.That(RumorAnatomy.Technique(m.Technique), Is.Not.Null, a.Id + " " + m.Technique);
                Assert.That(a.Where, Is.Not.Empty);
            }
            CollectionAssert.AreEquivalent(RumorAnatomy.Techniques.Select(t => t.Id),
                RumorAnatomy.All.SelectMany(a => a.Marks).Select(m => m.Technique).Distinct(), "each technique is shown by some post");
            Assert.That(RumorAnatomy.Techniques.Select(t => t.Name), Is.EqualTo(new[] { "출처 없음", "공포 자극", "숫자 과장", "퍼뜨리기 압박", "사진 재활용" }));
            foreach (var id in Rumors.All.Concat(Rumors.PortAdded).Select(r => r.Id))
                Assert.That(RumorAnatomy.Of(id), Is.Not.Null, "every rumour set can be taken apart: " + id);
        }

        [Test]
        public void TheHighlight_NumbersTheMarksInTheOrderTheyAppear_WithoutOverlap()
        {
            var a = RumorAnatomy.Of("mountain_raid");
            var post = a.Post(Game());
            Assert.That(RumorAnatomy.Highlight(post, a.Marks, plain: true), Is.EqualTo("북한군 수십 명①이 아파트 뒷산에 숨어 있대요②!!③ 다들 짐 싸서 빨리 나가래요④"));
            var rich = RumorAnatomy.Highlight(post, a.Marks);
            Assert.That(rich, Does.Contain("<color=#F2B33D><b>수십 명</b></color>"));
            var lines = RumorAnatomy.MarkLines(post, a.Marks);
            Assert.That(lines[0], Does.StartWith("① 숫자 과장 — "));
            Assert.That(lines[3], Does.StartWith("④ 퍼뜨리기 압박 — "));
            // a phrase twice or overlapping another is taken once; a missing phrase is left out
            var marks = new[] { new AnatomyMark("fear", "!!", "a"), new AnatomyMark("pressure", "!", "b"), new AnatomyMark("numbers", "없는 말", "c") };
            var found = RumorAnatomy.Found("아!! 그래!", marks);
            Assert.That(found.Select(f => f.at), Is.EqualTo(new[] { 1, 6 }));
            Assert.That(RumorAnatomy.Num(0) + RumorAnatomy.Num(4), Is.EqualTo("①⑤"));
        }

        [Test]
        public void TheCard_CitesOnlyRealFacts_IncludingTheVerifiedBadNewsStudy()
        {
            var g = Game();
            var lines = RumorAnatomy.CardLines(g, RumorAnatomy.PhotoPost, Merged);
            Assert.That(lines[0], Does.Contain("[사진]"));
            Assert.That(lines.Any(l => l.Contains("사진 재활용")), Is.True);
            Assert.That(lines.Any(l => l.Contains(RumorAnatomy.Habit)), Is.True);
            foreach (var id in RumorAnatomy.HabitFacts.Append(RumorAnatomy.ResearchFact))
            {
                Assert.That(Merged.Text(id), Is.Not.Empty, id);
                Assert.That(lines.Any(l => l.Contains(Merged.Text(id))), Is.True, id + " is on the card");
            }
            var study = Merged.Cite(RumorAnatomy.ResearchFact);
            Assert.That(study.Text, Does.Contain("21%").And.Contain("진짜 뉴스"));
            Assert.That(study.Source, Does.Contain("보조 자료"));
            Assert.That(study.Url, Does.StartWith("https://www.cam.ac.uk/"));
            Assert.That(Merged.IsExtension(RumorAnatomy.ResearchFact), Is.True, "a new fact lives in facts-ext.json");
            Assert.That(RumorAnatomy.Spoken(g, "mountain_raid"), Does.Not.Contain("<"));
        }

        [Test]
        public void Recording_IsOncePerPost_AndSeenCollectsTheTechniquesInTableOrder()
        {
            var g = Game();
            Assert.That(RumorAnatomy.Record(g, "shelter_closed"), Is.EqualTo(new[] { "nosource", "pressure" }));
            Assert.That(RumorAnatomy.Record(g, "shelter_closed"), Is.Empty, "once");
            Assert.That(RumorAnatomy.Record(g, "mountain_raid"), Is.EqualTo(new[] { "numbers", "fear" }), "only the techniques new to this run");
            Assert.That(g.Events.Count(e => e.Type == RumorAnatomy.Event), Is.EqualTo(2));
            Assert.That(RumorAnatomy.Seen(g), Is.EqualTo(new[] { "nosource", "fear", "numbers", "pressure" }));
            Assert.That(RumorAnatomy.Record(g, "nope"), Is.Empty);
            var row = LearningReport.Build(g).Rows.Single(r => r.Id == "prebunk");
            Assert.That(row.Ok && row.Axis == "info" && row.FactId == "info_rumor", Is.True);
            Assert.That(row.Label, Is.EqualTo("소문이 쓰는 수법 4가지를 짚어 보았다 (출처 없음 · 공포 자극 · 숫자 과장 · 퍼뜨리기 압박)"));
            Assert.That(LearningReport.Build(Game()).Rows.Any(r => r.Id == "prebunk"), Is.False, "no card, no row");
        }

        [Test]
        public void TheArchive_KeepsTheTechniques_AddsThemUp_AndRevealsAllFive()
        {
            var g = Game();
            RumorAnatomy.Record(g, RumorAnatomy.PhotoPost);
            var run = Archive.Scan(g);
            Assert.That(run.Techniques, Is.EqualTo(new[] { "nosource", "fear", "pressure", "photo" }));
            var stored = Archive.Read("{\"techniques\":[\"numbers\",\"zzz\",\"numbers\"]}");
            Assert.That(stored.Techniques, Is.EqualTo(new[] { "numbers" }), "unknown ids dropped");
            var m = Archive.Merge(stored, run);
            Assert.That(m.Techniques, Is.EqualTo(RumorAnatomy.Techniques.Select(t => t.Id)));
            Assert.That(Archive.Read(m.ToJson()).Techniques, Is.EqualTo(m.Techniques), "round trip");
            Assert.That(Archive.RevealAll(new ArchiveProgress()).Techniques.Count, Is.EqualTo(5));
            foreach (var t in RumorAnatomy.Techniques) Assert.That(RumorAnatomy.Example(t.Id).post, Is.Not.Null, t.Id + " has an example");
        }

        // ---------------------------------------------------------------- 루머 해부: the scenes

        [Test]
        public void TheCard_ShowsOnce_WithThePostHighlighted_AndANoticeForNewTechniques()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            bool first = false, second = true;
            host.Run(async () => { first = await PrebunkScene.Show(ctx, "mountain_raid"); second = await PrebunkScene.Show(ctx, "mountain_raid"); });
            Assert.That(first, Is.True); Assert.That(second, Is.False);
            var card = host.Cards.Single();
            Assert.That(card.Kind, Is.EqualTo(RumorAnatomy.Kind));
            Assert.That(card.Title, Is.EqualTo("3장 뒷산 소문 (103동 단톡방)"));
            Assert.That(card.Meta, Does.StartWith("이 글이 쓴 수법 4가지"));
            Assert.That(card.Body[0], Does.Contain("수십 명"));
            Assert.That(host.Toasts, Has.Member(PrebunkScene.Notice));
            Assert.That(PrebunkScene.Notice, Does.Contain(Archive.Path), "one wording for where the hub is");
            Assert.That(Archive.Notice(new[] { Archive.Cards[0].Id }), Does.Contain(Archive.Path));
            PrebunkScene.Enabled = false;
            var h2 = new FakeStoryHost();
            var c2 = h2.CreateContext(TestSupport.Rules, Game());
            h2.Run(() => PrebunkScene.Show(c2, "camp_food"));
            Assert.That(h2.Cards, Is.Empty, "the seam the original's tests use");
            Assert.That(h2.G.Events.Any(e => e.Type == RumorAnatomy.Event), Is.False);
        }

        [Test]
        public void TheShelterRumour_EndsWithItsAnatomy_BeforeTheFacts()
        {
            var g = Game();
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules, g);
            host.Answer("rumor", "check", "tell");
            host.AnswerCard("ok");
            host.Run(() => ctx.Goto("store", "entry"));
            host.Run(() => Neighbors.StoreRumor(ctx));
            Assert.That(host.Cards.Single().Kind, Is.EqualTo(RumorAnatomy.Kind));
            Assert.That(RumorAnatomy.Shown(g, "shelter_closed"), Is.True);
        }

        [Test]
        public void The22MinuteMorning_ShowsThePhotoPost_ThenTakesItApartAfterTheCorrection()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game("prep"));
            host.G.Day = -2; host.G.Minute = 21 * 60;
            TwentyTwoTests.AnswerCarefully(host);
            host.Run(() => TwentyTwoScene.Run(ctx));
            var chat = host.Talks.Where(t => t.Speaker == "103동 단톡방").ToList();
            Assert.That(chat[0].Lines.Any(l => l.Contains(RumorAnatomy.PhotoPostText)), Is.True);
            var fix = chat.Last().Lines.Single(l => l.Contains("작년 겨울 축제"));
            Assert.That(fix, Is.EqualTo(RumorAnatomy.PhotoCorrection));
            Assert.That(fix, Does.StartWith("사진 올린 분").And.Not.Contain("래요"), "corrected by the poster who checked, not by another hearsay line");
            // the anatomy comes after the review card (the elder's thanks and the order of response land first)
            Assert.That(host.Cards.Select(c => c.Kind).ToList(), Is.EqualTo(new[] { "record", RumorAnatomy.Kind }));
            var ev = host.G.Events.Single(e => e.Type == RumorAnatomy.Event);
            Assert.That((string)ev.Details["id"], Is.EqualTo(RumorAnatomy.PhotoPost));
            Assert.That(ev.Details["techniques"].Select(t => (string)t), Is.EqualTo(new[] { "photo", "fear", "nosource", "pressure" }));
            Assert.That(host.G.Phone.Battery, Is.EqualTo(TestSupport.Rules.CreateGame(7).Phone.Battery), "power is on in the morning: no battery cost");
        }

        [Test]
        public void TheHillRumour_InTheBlackout_CostsTheChatsBattery_AndEndsWithItsAnatomy()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            host.G.Day = 2; host.G.Minute = 8 * 60;
            ctx.Power = false;
            host.G.Phone.Battery = 50;
            MountainSceneTests.AnswerCarefully(host);
            ClaimHearing heard = null;
            host.Run(async () => heard = await RumorScene.HearClaim(ctx, "mountain_raid"));
            Assert.That(host.G.Phone.Battery, Is.EqualTo(47));
            var use = host.G.Events.Single(e => e.Type == PhoneBattery.Event);
            Assert.That((string)use.Details["what"], Is.EqualTo("chat"));
            Assert.That(host.Toasts, Has.Member(PhoneBattery.UseLine("chat", 3, 47)));
            // with the power on (or the phone dead) it is free, as before
            var h2 = new FakeStoryHost();
            var c2 = h2.CreateContext(TestSupport.Rules, Game());
            h2.G.Phone.Battery = 50;
            MountainSceneTests.AnswerCarefully(h2);
            h2.Run(() => RumorScene.HearClaim(c2, "mountain_raid"));
            Assert.That(h2.G.Phone.Battery, Is.EqualTo(50));
        }

        // ---------------------------------------------------------------- 휴대폰 배터리

        [Test]
        public void Costs_TheChatMost_ATextLeast_SaverTakesFortyPercentOff()
        {
            var g = Game();
            Assert.That(new[] { "sns", "chat", "fm", "map", "call", "text" }.Select(w => PhoneBattery.Cost(g, w)), Is.EqualTo(new double[] { 5, 3, 3, 3, 2, 1 }));
            Assert.That(PhoneBattery.Cost(g, "sns"), Is.GreaterThan(PhoneBattery.Cost(g, "text")));
            PhoneBattery.SetSaver(g, true);
            Assert.That(new[] { "sns", "chat", "fm", "text" }.Select(w => PhoneBattery.Cost(g, w)), Is.EqualTo(new double[] { 3, 2, 2, 1 }));
            Assert.That(PhoneBattery.Standby(g, false), Is.EqualTo(6)); Assert.That(PhoneBattery.Standby(g, true), Is.EqualTo(3));
            PhoneBattery.SetSaver(g, false);
            Assert.That(PhoneBattery.Saver(g), Is.False);
            Assert.That(PhoneBattery.Standby(g, false), Is.EqualTo(12), "the original's standby without 절전");
            Assert.That(PhoneBattery.Cost(g, "unknown"), Is.Zero);
        }

        [Test]
        public void TheBlackoutWindow_LowBattery_AndNoHarshEnd()
        {
            Assert.That(PhoneBattery.Blackout("dday", false) && PhoneBattery.Blackout("endure", false), Is.True);
            Assert.That(PhoneBattery.Blackout("dday", true) || PhoneBattery.Blackout("prep", false) || PhoneBattery.Blackout("evac", false), Is.False);
            Assert.That(PhoneBattery.Extras(16) && !PhoneBattery.Extras(15), Is.True);
            Assert.That(PhoneBattery.IsLow(15) && !PhoneBattery.IsLow(0) && PhoneBattery.IsOff(0), Is.True);
            Assert.That(PhoneBattery.CrossedLow(16, 15) && !PhoneBattery.CrossedLow(15, 14) && !PhoneBattery.CrossedLow(double.NaN, 10) && !PhoneBattery.CrossedLow(20, 0), Is.True);
            var g = Game();
            g.Phone.Battery = 2;
            Assert.That(PhoneBattery.Use(g, "sns"), Is.EqualTo(5));
            Assert.That(g.Phone.Battery, Is.Zero, "never below 0");
            Assert.That(PhoneBattery.Charge(g, "shelter", 25), Is.EqualTo(25));
            g.Phone.Battery = 90;
            Assert.That(PhoneBattery.Charge(g, "shelter", 25), Is.EqualTo(10), "never above 100");
            Assert.That(PhoneBattery.Use(g, "text"), Is.EqualTo(1));
            Assert.That(g.Events.Count(e => e.Type == PhoneBattery.Event && (string)e.Details["what"] == "text"), Is.Zero, "texts are not counted");
        }

        [Test]
        public void ACarefulBlackout_NeverRunsTheBatteryOut()
        {
            // 2장 from an uncharged 64 %: the texts, the map app, one chat thread, the shelter's power strip, two texts to 서연;
            // then three days of the ledger's standby without the power hour, with 절전 모드. Game numbers, the point is the margin.
            var g = Game("dday");
            g.Phone.Battery = 64;
            PhoneBattery.Use(g, "text"); PhoneBattery.Use(g, "map"); PhoneBattery.Use(g, "chat");
            PhoneBattery.Charge(g, "shelter", PhoneBattery.ShelterCharge);
            PhoneBattery.Use(g, "text"); PhoneBattery.Use(g, "text");
            PhoneBattery.SetSaver(g, true);
            for (int day = 0; day < 3; day++) { g.Phone.Battery -= PhoneBattery.Standby(g, false); PhoneBattery.Use(g, "chat"); PhoneBattery.Use(g, "text"); }
            Assert.That(g.Phone.Battery, Is.GreaterThan(PhoneBattery.Low));
        }

        [Test]
        public void TheReportRow_PraisesSavingAndCharging_AndNamesTooMuchScrolling()
        {
            var g = Game();
            Assert.That(PhoneBattery.Judge(g).Show, Is.False);
            PhoneBattery.Use(g, "chat");
            Assert.That(PhoneBattery.Judge(g).Show, Is.False, "a chat that came in is not a habit");
            PhoneBattery.Charge(g, "shelter", 25);
            var ok = LearningReport.Build(g).Rows.Single(r => r.Id == "battery");
            Assert.That(ok.Ok && ok.Axis == "endure" && ok.FactId == PhoneBattery.FactId, Is.True);
            Assert.That(Merged.Text(PhoneBattery.FactId), Does.Contain("저전력 모드"));
            for (int i = 0; i < 3; i++) PhoneBattery.Use(g, "sns");
            var no = LearningReport.Build(g).Rows.Single(r => r.Id == "battery");
            Assert.That(no.Ok, Is.False);
            Assert.That(no.Label, Is.EqualTo("단톡방을 여러 번 훑느라 배터리를 많이 썼다"));
            var h = Game();
            PhoneBattery.Use(h, "sns");
            Assert.That(LearningReport.Build(h).Rows.Single(r => r.Id == "battery").Label, Does.Contain("절전 모드·라디오·대피소 충전"));
        }

        [Test]
        public void TheReportRow_CountsLettingSomeoneChargeFirst_AndTheRadio_AsGoodHabits()
        {
            var y = Game();
            PhoneBattery.Yield(y);
            PhoneBattery.Use(y, "sns");
            var yielded = PhoneBattery.Judge(y);
            Assert.That(yielded.Show && yielded.Ok, Is.True, "letting the next person go first is fair, not a miss");
            Assert.That(yielded.Yes, Does.Contain("양보"));
            var r = Game();
            r.SetKnow(PhoneBattery.RadioKnow, true);
            Assert.That(PhoneBattery.Judge(r).Ok, Is.True, "listening on the radio's own batteries saves the phone");
            Assert.That(PhoneBattery.Judge(r).Yes, Does.Contain("라디오"));
            for (int i = 0; i < 3; i++) PhoneBattery.Use(r, "sns");
            Assert.That(PhoneBattery.Judge(r).Ok, Is.False, "only heavy scrolling fails the row");
        }

        [Test]
        public void TheShelterDesk_Yield_IsRecorded()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game("dday"));
            host.Answer("yield");
            var m = typeof(DDay).GetMethod("ShelterCharge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            host.Run(() => (System.Threading.Tasks.Task)m.Invoke(null, new object[] { ctx }));
            Assert.That(host.G.Events.Any(e => e.Type == PhoneBattery.Event && (string)e.Details["what"] == "yield"), Is.True);
        }

        [Test]
        public void AtLowBattery_AnIncomingChat_IsAFreePreview_AndNeverEmptiesThePhone()
        {
            foreach (var start in new double[] { 15, 3, 1 })
            {
                var host = new FakeStoryHost();
                var ctx = host.CreateContext(TestSupport.Rules, Game());
                ctx.Power = false;
                host.G.Phone.Battery = start;
                MountainSceneTests.AnswerCarefully(host);
                host.Run(() => RumorScene.HearClaim(ctx, "mountain_raid"));
                Assert.That(host.G.Phone.Battery, Is.EqualTo(start), start + "%: no cost at low battery");
                Assert.That(host.G.Events.Any(e => e.Type == PhoneBattery.Event), Is.False);
            }
            Assert.That(PhoneBattery.Sip(2, 1), Is.EqualTo(1));
            Assert.That(PhoneBattery.Sip(1, 1), Is.Zero, "never the last percent");
            Assert.That(PhoneBattery.Sip(1.5, 1), Is.Zero);
            Assert.That(PhoneBattery.Sip(40, 1), Is.EqualTo(1));
        }

        [Test]
        public void ANewRun_DoesNotCompareItsBatteryWithTheLastRuns()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            ctx.Power = false;
            host.G.Phone.Battery = 60; ctx.Refresh();
            host.G.Phone.Battery = 12; // a checkpoint or prepared save at low battery
            ctx.BatterySync();
            ctx.Refresh();
            Assert.That(host.Toasts.Any(t => t.StartsWith("휴대폰 배터리")), Is.False);
        }

        [Test]
        public void TheCardsSizeTags_FollowTheTextSizeSetting()
        {
            var g = Game();
            var normal = RumorAnatomy.CardLines(g, "mountain_raid", Merged);
            var big = RumorAnatomy.CardLines(g, "mountain_raid", Merged, 1.3f);
            Assert.That(normal[0], Does.Contain("<size=21>"));
            Assert.That(big[0], Does.Contain("<size=27>").And.Not.Contain("<size=21>"));
            Assert.That(normal.Last(), Does.StartWith("<size=16>"));
            Assert.That(big.Last(), Does.StartWith("<size=21>"), "the footnote grows with the body (16 x 1.3)");
            Assert.That(big.Any(l => l.StartsWith("<size=23>")), Is.True, "the habit citation (18 x 1.3)");
            Assert.That(RumorAnatomy.Px(16, 0), Is.EqualTo(16), "no scale reads as 1");
        }

        [Test]
        public void TheNotes_OnlySayWhatThePostShows_OnEveryBranch()
        {
            // the card follows the check, the correction and the set-aside branches alike: no note may lean on a scene
            // (the manager's visit, the official notice) that only one branch shows
            foreach (var a in RumorAnatomy.All)
                foreach (var m in a.Marks)
                    Assert.That(m.Note, Does.Not.Contain("박 소장").And.Not.Contain("공식 안내는"), a.Id + " " + m.Phrase);
            Assert.That(RumorAnatomy.PhotoSeen, Does.Not.Contain("한낮"), "the room is lit: the clue is the season, not the hour");
            Assert.That(RumorAnatomy.Technique("fear").Ask, Is.EqualTo("겁이 나서 확인도 안 하고 믿으려는 건 아닐까?"));
        }

        [Test]
        public void TheArchiveExample_PrefersThePlayersOwnPost_AndWordsOverPunctuation()
        {
            Assert.That(RumorAnatomy.Example("fear").mark.Phrase, Is.EqualTo("완전 난리!!"), "words, not bare punctuation");
            Assert.That(RumorAnatomy.Example("fear", new[] { "mountain_raid" }).mark.Phrase, Is.EqualTo("완전 난리!!"), "the hill post's '!!' alone teaches little");
            Assert.That(RumorAnatomy.Example("fear", new[] { "camp_food" }).post.Id, Is.EqualTo("camp_food"));
            Assert.That(RumorAnatomy.Example("nosource").post.Id, Is.EqualTo(RumorAnatomy.PhotoPost), "story order when nothing was taken apart");
            Assert.That(RumorAnatomy.Example("nosource", new[] { "evac_shortcut" }).post.Id, Is.EqualTo("evac_shortcut"));
            var g = Game();
            RumorAnatomy.Record(g, "camp_food");
            var run = Archive.Scan(g);
            Assert.That(run.Posts, Is.EqualTo(new[] { "camp_food" }));
            var m = Archive.Merge(Archive.Read("{\"posts\":[\"mountain_raid\",\"zzz\"]}"), run);
            Assert.That(m.Posts, Is.EqualTo(new[] { "mountain_raid", "camp_food" }), "union in table order, unknown ids dropped");
            Assert.That(Archive.Read(m.ToJson()).Posts, Is.EqualTo(m.Posts));
            Assert.That(RumorAnatomy.Example("nosource", m.Posts).post.Id, Is.EqualTo("mountain_raid"));
        }

        static System.Threading.Tasks.Task HillRumour(StoryContext ctx) =>
            (System.Threading.Tasks.Task)typeof(MountainScene).GetMethod("Rumour", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { ctx });

        [Test]
        public void TheHillRumour_TheFactNotice_DoesNotRepeatWhatTheCardShowed()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            host.G.Day = 2; host.G.Minute = 8 * 60;
            MountainSceneTests.AnswerCarefully(host);
            host.Run(() => HillRumour(ctx));
            Assert.That(host.Cards.Count(c => c.Kind == RumorAnatomy.Kind), Is.EqualTo(1));
            Assert.That(host.Toasts.Any(t => t.StartsWith("소문을 만났을 때")), Is.False, "info_rumor and radio_listen were on the card");
            Assert.That(host.G.Info.Any(i => i.Text == Merged.Text("info_rumor")), Is.True, "still noted for the phone and the notebook");
            PrebunkScene.Enabled = false;
            var h2 = new FakeStoryHost();
            var c2 = h2.CreateContext(TestSupport.Rules, Game());
            h2.G.Day = 2; h2.G.Minute = 8 * 60;
            MountainSceneTests.AnswerCarefully(h2);
            h2.Run(() => HillRumour(c2));
            Assert.That(h2.Toasts.Any(t => t.StartsWith("소문을 만났을 때")), Is.True, "without the card the notice is as before");
        }

        [Test]
        public void TheLedger_WithSaverMode_LosesHalfTheStandby_AndSaysSo()
        {
            var g = Game();
            g.Phone.Battery = 80;
            g.Items["radio"] = new ItemEntry { Home = 1, Checked = true, Charge = 100 }; // the power hour goes to the radio, not the phone
            var plain = Ledger.SimulateDays(g, new LedgerPlan(2, 2, "radio"), 2, "home");
            PhoneBattery.SetSaver(g, true);
            var saved = Ledger.SimulateDays(g, new LedgerPlan(2, 2, "radio"), 2, "home");
            Assert.That(plain.Phone, Is.EqualTo(56), "the original's 12 a day");
            Assert.That(saved.Phone - plain.Phone, Is.EqualTo(12), "6 a day instead of 12");
        }

        [Test]
        public void EnteringLowBatteryInTheBlackout_SaysSoOnce()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            ctx.Power = false;
            host.G.Phone.Battery = 20; ctx.Refresh();
            host.G.Phone.Battery = 14; ctx.Refresh();
            host.G.Phone.Battery = 12; ctx.Refresh();
            Assert.That(host.Toasts.Count(t => t == PhoneBattery.LowLine(14)), Is.EqualTo(1));
            Assert.That(host.Toasts.Count(t => t.StartsWith("휴대폰 배터리")), Is.EqualTo(1));
            ctx.Power = true;
            host.G.Phone.Battery = 30; ctx.Refresh(); host.G.Phone.Battery = 10; ctx.Refresh();
            Assert.That(host.Toasts.Count(t => t.StartsWith("휴대폰 배터리")), Is.EqualTo(1), "not with the power on");
        }

        [Test]
        public void WithoutARadio_ThePhonesFmAppWaitsAtLowBattery_InTheBlackout()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            ctx.Power = false;
            host.G.Phone.Battery = 12;
            host.Answer("no");
            host.Run(() => ctx.Modal(() => StoryCommon.RadioScene(ctx, "water")));
            var ask = host.Talks.Single();
            var fm = ask.Choices.Single(c => c.Id == "phone");
            Assert.That(fm.Disabled, Is.True);
            Assert.That(fm.Why, Is.EqualTo(PhoneBattery.LowWhy));
            // above low it is offered as before, with no note under it
            var h2 = new FakeStoryHost();
            var c2 = h2.CreateContext(TestSupport.Rules, Game());
            c2.Power = false;
            h2.G.Phone.Battery = 40;
            h2.Answer("no");
            h2.Run(() => c2.Modal(() => StoryCommon.RadioScene(c2, "water")));
            var fm2 = h2.Talks.Single().Choices.Single(c => c.Id == "phone");
            Assert.That(fm2.Disabled || fm2.Why != null, Is.False);
        }

        [Test]
        public void TheRadio_RunsOnItsOwnBatteries_AndSaysSoOnceInTheBlackout()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game());
            ctx.Power = false;
            host.G.Items["radio"] = new ItemEntry { Home = 1, Checked = true, Charge = 80 };
            host.G.Phone.Battery = 40;
            host.AnswerOpen("radio", new JObject { ["official"] = true, ["noted"] = new JArray() });
            host.AnswerOpen("radio", new JObject { ["official"] = true, ["noted"] = new JArray() });
            host.Run(async () => { await ctx.Modal(() => StoryCommon.RadioScene(ctx, "water")); await ctx.Modal(() => StoryCommon.RadioScene(ctx, "water")); });
            Assert.That(host.G.Phone.Battery, Is.EqualTo(40));
            Assert.That(host.Toasts.Count(t => t == PhoneBattery.RadioHabit), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- low vision: the overlay reads talks and cards

        [Test]
        public void TheOverlay_ReadsTheSpeakerFirst_TheNumberedChoicesLast_AndCardsWhole()
        {
            var o = new TalkOptions
            {
                Speaker = "박영수 소장", Role = "관리사무소", Lines = { "<b>첫 줄</b>", "둘째 줄" },
                Choices = new List<TalkChoice> { new("a", "확인한다"), new("b", "그냥 간다", true, "시간이 없다") },
            };
            Assert.That(OverlaySpeech.TalkLine(o, o.Lines[0], true, false), Is.EqualTo("박영수 소장, 관리사무소. 첫 줄"));
            Assert.That(OverlaySpeech.TalkLine(o, o.Lines[1], false, true), Is.EqualTo("둘째 줄 고르세요. 1번, 확인한다. 2번, 그냥 간다 (고를 수 없음)."));
            var card = new CardOptions { Kind = "anatomy", Title = "제목", Meta = "메타", Body = { "<color=#F2B33D>강조</color> 본문." } };
            Assert.That(OverlaySpeech.Card("루머 해부", card, new List<CardAction> { new("ok", "확인", true) }), Is.EqualTo("루머 해부. 제목. 메타. 강조 본문. 버튼: 확인."));
            Assert.That(OverlaySpeech.Skip("D+1 09:00", "하루가 지났다.", true), Is.EqualTo("D+1 09:00. 하루가 지났다."));
            Assert.That(OverlaySpeech.Skip("D+1 09:00", "둘째", false), Is.EqualTo("둘째"));
        }

        [Test]
        public void WhenTheOverlayReads_ScenesDoNotReadTheirTalksAgain_ButStillReadToasts()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, Game("prep"), settings: new StorySettings { Profile = "lowvision" });
            host.G.Day = -2; host.G.Minute = 21 * 60;
            var heard = new List<string>();
            ctx.Speak = heard.Add;
            ctx.UiReadsAloud = true;
            TwentyTwoTests.AnswerCarefully(host);
            host.Run(() => TwentyTwoScene.Run(ctx));
            Assert.That(heard.Any(s => s.StartsWith("103동 단톡방.")), Is.False, "the talk box reads the chat itself");
            Assert.That(heard.Any(s => s.StartsWith("위급재난문자.")), Is.True, "the alert screen is not the talk box: the scene still reads it");
        }
    }
}
