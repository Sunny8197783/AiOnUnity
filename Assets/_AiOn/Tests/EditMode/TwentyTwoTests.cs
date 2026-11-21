// 1장 「22분」 (competition upgrade): the rules of the D-1 morning interlude (Game/TwentyTwoLogic.cs: the clock, the
// choices, the order review), the facts it stands on, the scene on FakeStoryHost (Story/TwentyTwoScene.cs), its place
// in 1장 (Prep.TwentyTwoPart, no new checkpoint), the 2장 card "그날 22분이 남긴 것" and the learning-report rows.
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
    public class TwentyTwoTests
    {
        /// <summary>The order this scene recommends, as picks.</summary>
        public static readonly string[] Careful = { "official", "ready", "elder", "down" };

        /// <summary>
        /// The interlude as a careful player answers it (the playthrough rigs, so the prepared saves carry a careful run):
        /// the TV first, then ready, 304호, down; "check" for next time. Rules match in order and are used once.
        /// </summary>
        public static void AnswerCarefully(FakeStoryHost x)
        {
            foreach (var id in Careful) x.AnswerWhen(o => o.Speaker == "판단" && o.Choices != null && o.Choices.Any(c => c.Id == id) && o.Choices.All(c => c.Id != "check"), id);
            x.AnswerWhen(o => o.Speaker == "판단" && o.Choices != null && o.Choices.Any(c => c.Id == "doubt"), "check");
        }

        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(7));
            host.G.Chapter = "prep";
            host.G.Day = -2; host.G.Minute = 21 * 60; // as Prep leaves it after tonight's slot (the interlude moves it on)
            return (host, ctx);
        }

        static TwentyTwoRun Play(params string[] picks)
        {
            var run = new TwentyTwoRun();
            foreach (var p in picks) { if (run.Over) break; run.Pick(p); }
            return run;
        }

        // ---------------------------------------------------------------- the rules

        [Test]
        public void TheClock_IsTheRealTwentyTwoMinutes_AndTheCarefulOrderFitsInsideIt()
        {
            Assert.That(TwentyTwoLogic.SentAt, Is.EqualTo(6 * 60 + 41));
            Assert.That(TwentyTwoLogic.CorrectionAt, Is.EqualTo(7 * 60 + 3));
            Assert.That(TwentyTwoLogic.Span, Is.EqualTo(22));
            var run = Play(Careful);
            Assert.That(run.Over && run.Down && run.WithElder, Is.True);
            Assert.That(run.Elapsed, Is.EqualTo(3 + 4 + 5 + 6 + TwentyTwoLogic.ElderSlower));
            Assert.That(run.Minute, Is.LessThanOrEqualTo(TwentyTwoLogic.CorrectionAt), "down in the car park before the correction");
            Assert.That(run.CorrectionDuringLast, Is.False);
            Assert.That(TwentyTwoLogic.Verdict(run.Picks), Is.EqualTo("good"));
            Assert.That(TwentyTwoLogic.Rows(run.Picks).All(r => r.Ok), Is.True);
        }

        [Test]
        public void TheCall_IsReplacedByTextOrRedial_AndRedialingFloodsTheLine()
        {
            var run = new TwentyTwoRun();
            run.Pick("call");
            var ids = run.Available().Select(a => a.Id).ToList();
            Assert.That(ids, Does.Contain("text").And.Contain("redial").And.Not.Contain("call"));
            Assert.That(ids.IndexOf("text"), Is.EqualTo(2), "the follow-ups take the call's place (after app, official)");
            run.Pick("redial");
            Assert.That(run.Available().Select(a => a.Id), Has.No.Member("text").And.No.Member("redial"));
            var rows = TwentyTwoLogic.Rows(run.Picks);
            Assert.That(rows.Single(r => r.Id == "calls").Ok, Is.False);
            Assert.That(rows.Single(r => r.Id == "calls").Fact, Is.EqualTo("comms_calls"));
            Assert.That(TwentyTwoLogic.Rows(new[] { "official", "call", "text" }).Single(r => r.Id == "calls").Ok, Is.True, "one text instead is fine");
            Assert.That(TwentyTwoLogic.Rows(new[] { "official" }).Any(r => r.Id == "calls"), Is.False, "no call, no row");
            Assert.Throws<System.ArgumentException>(() => run.Pick("call"), "not offered twice");
        }

        [Test]
        public void TheOrder_NotTheTruth_IsJudged()
        {
            // the same deeds in a worse order: going down before the TV, the elder after going down
            var late = TwentyTwoLogic.Rows(new[] { "down" });
            Assert.That(late.Single(r => r.Id == "official").Ok, Is.False);
            Assert.That(late.Single(r => r.Id == "down").Ok, Is.True);
            var rows = TwentyTwoLogic.Rows(new[] { "app", "chat", "official", "ready" });
            Assert.That(rows.Single(r => r.Id == "official").Ok, Is.False, "the TV came after posting a guess");
            Assert.That(rows.Single(r => r.Id == "official").Label, Does.Contain("다른 일부터"));
            Assert.That(rows.Single(r => r.Id == "rumor").Ok, Is.False);
            Assert.That(TwentyTwoLogic.Rows(new[] { "app", "official", "ready", "elder" }).Single(r => r.Id == "official").Ok, Is.True, "the app first is no fault (it just fails)");
            // back to bed: the clock jumps to the correction, and the verdict is poor whatever came before
            var bed = Play("official", "sleep");
            Assert.That(bed.Minute, Is.EqualTo(TwentyTwoLogic.CorrectionAt));
            Assert.That(bed.Over && bed.Slept, Is.True);
            Assert.That(TwentyTwoLogic.Verdict(bed.Picks), Is.EqualTo("poor"));
            Assert.That(TwentyTwoLogic.Rows(bed.Picks).Single(r => r.Id == "bed").Ok, Is.False);
            Assert.That(TwentyTwoLogic.Verdict(new[] { "official", "ready", "elder", "chat", "down" }), Is.EqualTo("partial"), "a rumour spoils a good order");
            Assert.That(TwentyTwoLogic.VerdictLine("partial"), Does.Not.Contain("빨라"), "a skipped step is not a slow order");
            // the verdict never reads whether the alert was real: the result has no such field
            Assert.That(Play(Careful).Result("check").Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "played", "picks", "elapsed", "verdict", "next" }));
        }

        [Test]
        public void TheElder_CountsOnlyBeforeGoingDown_AndSlowsTheWalk()
        {
            Assert.That(TwentyTwoLogic.Rows(new[] { "down" }).Single(r => r.Id == "elder").Label, Does.Contain("챙기지 않았다"));
            var alone = Play("official", "ready", "down");
            Assert.That(alone.Elapsed, Is.EqualTo(3 + 4 + 6));
            var with = Play("official", "elder", "down");
            Assert.That(with.Elapsed, Is.EqualTo(3 + 5 + 6 + TwentyTwoLogic.ElderSlower));
        }

        [Test]
        public void TheCorrection_ComesAtSevenOThree_WhateverIsBeingDone()
        {
            var run = Play("app", "official", "call", "redial", "ready", "elder", "down");
            Assert.That(run.Over, Is.True, "07:03 passed");
            Assert.That(run.Minute, Is.GreaterThan(TwentyTwoLogic.CorrectionAt));
            Assert.That(run.CorrectionDuringLast, Is.True);
            Assert.That(run.Picks.Last(), Is.EqualTo("down"), "the walk down with 어르신 is cut short by the text");
        }

        [Test]
        public void TheTexts_HaveNoReasonAndNoPlace_AndNameNoOne()
        {
            var sms = TwentyTwoLogic.AlertSms(StoryCommon.Area);
            Assert.That(sms, Does.Contain("경계경보").And.Contain("대피할 준비").And.Contain("어린이와 노약자"));
            Assert.That(sms, Does.Not.Contain("미사일").And.Not.Contain("대피소").And.Not.Contain("지하"), "no reason, no place (as the 2023 text)");
            var fix = TwentyTwoLogic.CorrectionSms(StoryCommon.Area);
            Assert.That(fix, Does.Contain("오발령").And.Contain("06:41"));
            foreach (var t in new[] { sms, fix })
            {
                Assert.That(t, Does.Contain("(가상)"), "the story's area tag");
                Assert.That(t, Does.Not.Contain("행정안전부").And.Not.Contain("서울시"), "no real office named in the story texts");
            }
        }

        [Test]
        public void EveryFactTheInterludeUses_IsSourced_AndTheNewOnesAreExtensions()
        {
            var facts = FakeStoryHost.LoadFacts();
            var ids = new[] { "misfire_times", "misfire_app", "alert_text_fix", "alert_clarify", "alert_levels", "info_misfire" }
                .Concat(new[] { Careful, new[] { "official", "ready", "elder", "down", "call", "redial", "chat", "sleep" } }
                    .SelectMany(p => TwentyTwoLogic.Rows(p)).Select(r => r.Fact)).Distinct().ToList();
            foreach (var id in ids)
            {
                var c = facts.Cite(id);
                Assert.That(c, Is.Not.Null, id);
                Assert.That(c.Url, Does.StartWith("https://"), id);
            }
            foreach (var id in new[] { "misfire_times", "misfire_app", "alert_text_fix", "alert_clarify" }) Assert.That(facts.IsExtension(id), id);
            Assert.That(facts.Text("misfire_times"), Does.Contain("6시 41분").And.Contain("7시 3분"));
            Assert.That(facts.Text("alert_text_fix"), Does.Contain("발령 사유").And.Contain("대피 방법"));
            foreach (var id in ids) Assert.IsFalse(Regex.IsMatch(facts.Text(id), @"(?<!\d)(1337|111|113)(?!\d)"), id);
        }

        [Test]
        public void KeepingListeningReady_IsAsRightAsGoingDown_UnderThisAlert()
        {
            // offered only once the TV is on and 정민 is ready
            Assert.That(new TwentyTwoRun().Available().Select(a => a.Id), Has.No.Member("listen"));
            Assert.That(Play("official").Available().Select(a => a.Id), Has.No.Member("listen"));
            var run = Play("official", "ready", "elder", "listen");
            Assert.That(run.Over && run.Listened, Is.True);
            Assert.That(run.Minute, Is.EqualTo(TwentyTwoLogic.CorrectionAt), "waits for the next word");
            Assert.That(run.CorrectionDuringLast, Is.False);
            var rows = TwentyTwoLogic.Rows(run.Picks);
            Assert.That(rows.All(r => r.Ok), Is.True);
            Assert.That(rows.Single(r => r.Id == "down").Label, Does.Contain("방송을 들으며"));
            Assert.That(TwentyTwoLogic.Verdict(run.Picks), Is.EqualTo("good"));
            // "good" always includes the elder (the report's label says so)
            Assert.That(TwentyTwoLogic.Verdict(new[] { "official", "ready", "down" }), Is.EqualTo("partial"));
            Assert.That(TwentyTwoLogic.Verdict(new[] { "official", "ready", "listen" }), Is.EqualTo("partial"));
            // after knocking at 304 there is no going back to bed: she is waiting
            Assert.That(Play("official", "elder").Available().Select(a => a.Id), Has.No.Member("sleep"));
            Assert.That(TwentyTwoLogic.Actions(false).Single(a => a.Id == "ready").Label, Does.Not.Contain("배낭"));
        }

        [Test]
        public void TheElder_IsThankedForWhatWasDone()
        {
            var (host, ctx) = Rig();
            host.Answer("official", "ready", "elder", "listen", "check");
            host.Run(() => TwentyTwoScene.Run(ctx));
            var thanks = host.Talks.Single(t => t.Speaker == "304호 어르신" && t.Lines.Any(l => l.Contains("고마워요")));
            Assert.That(thanks.Lines[0], Does.Contain("같이 기다려 줘서"));
            Assert.That(host.Talks.SelectMany(t => t.Lines).Any(l => l.Contains("같이 나와 줘서")), Is.False, "she did not come out");
            Assert.That((string)host.G.Events.Single(e => e.Type == TwentyTwoLogic.Event).Details["verdict"], Is.EqualTo("good"));

            var (h2, c2) = Rig();
            h2.Answer("elder", "app", "call", "redial", "chat", "official", "ready", "check"); // 07:03 comes before going down
            h2.Run(() => TwentyTwoScene.Run(c2));
            var waited = h2.Talks.Single(t => t.Speaker == "304호 어르신" && t.Lines.Any(l => l.Contains("고마워요")));
            Assert.That(waited.Lines[0], Does.Contain("기다리고 계셨다"));
        }

        [Test]
        public void TheLowVisionProfile_HearsTheTextsTheChoicesAndTheCards()
        {
            var (host, ctx) = Rig();
            ctx.Settings.Profile = "lowvision";
            var heard = new List<string>();
            ctx.Speak = heard.Add;
            AnswerCarefully(host);
            host.Run(() => TwentyTwoScene.Run(ctx));
            Assert.That(heard.Any(s => s.Contains(TwentyTwoLogic.AlertSms(StoryCommon.Area))), "the alert text");
            Assert.That(heard.Any(s => s.StartsWith("판단.") && s.Contains("1번, 휴대폰으로 대피소 위치를 찾아본다.")), "the numbered choices");
            Assert.That(heard.Any(s => s.StartsWith(TwentyTwoLogic.ReviewTitle) && !s.Contains("<")), "the review card, tags stripped");
            var (h2, c2) = Rig();
            var none = new List<string>();
            c2.Speak = none.Add;
            AnswerCarefully(h2);
            h2.Run(() => TwentyTwoScene.Run(c2));
            Assert.That(none, Is.Empty, "other profiles hear nothing new");
        }

        // ---------------------------------------------------------------- the scene

        [Test]
        public void TheMorning_TextChoicesCorrectionReview_ThenNineOClock()
        {
            var (host, ctx) = Rig();
            AnswerCarefully(host);
            host.Run(() => TwentyTwoScene.Run(ctx));
            var g = host.G;
            Assert.That(host.Calls("timeSkip").Select(c => c.Text), Is.EqualTo(new[] { "D-2 수 밤 -> D-1 목 06:41", "D-1 목 아침 -> D-1 목 09:00" }));
            var alerts = host.Opens.Where(o => o.screen == "emergencyAlert").Select(o => (string)o.options["text"]).ToList();
            Assert.That(alerts, Is.EqualTo(new[] { TwentyTwoLogic.AlertSms(StoryCommon.Area), TwentyTwoLogic.CorrectionSms(StoryCommon.Area) }));
            var ev = g.Events.Single(e => e.Type == TwentyTwoLogic.Event);
            Assert.That(ev.Details["picks"].Select(t => (string)t), Is.EqualTo(Careful));
            Assert.That((string)ev.Details["verdict"], Is.EqualTo("good"));
            Assert.That((string)ev.Details["next"], Is.EqualTo("check"));
            Assert.That((string)g.Know[TwentyTwoLogic.NextKnow], Is.EqualTo("check"));
            Assert.That(ev.Minute, Is.EqualTo(TwentyTwoLogic.CorrectionAt + 2), "recorded at 07:05, the minute of the decision (the HUD keeps up with the talk)");
            var chat = host.Talks.Where(t => t.Speaker == "103동 단톡방").ToList();
            Assert.That(chat.Last().Role, Is.EqualTo("07:04"));
            Assert.That(host.Talks.Last(t => t.Speaker == "판단").Role, Is.EqualTo("07:05"));
            var stamps = g.Events.Select(e => (long)e.Day * 1440 + e.Minute).ToList();
            Assert.That(stamps, Is.Ordered, "the clock only moves forward");
            Assert.That(g.Day, Is.EqualTo(-1));
            // the decisions are judgements (DecisionMoment), with the clock in the meta line
            var judged = host.Talks.Where(t => t.Speaker == "판단").ToList();
            Assert.That(judged.Count, Is.EqualTo(Careful.Length + 1));
            Assert.That(judged[0].Role, Is.EqualTo("06:42 · 문자를 받은 지 1분"));
            Assert.That(judged[1].Role, Is.EqualTo("06:45 · 문자를 받은 지 4분"));
            // the people around react differently, and 304호 came along
            Assert.That(host.Talks.Count(t => t.Speaker == "103동 단톡방"), Is.EqualTo(2));
            Assert.That(host.Talks.Any(t => t.Speaker == "304호 어르신" && t.Lines[0].Contains("고마워요")), "the elder thanks 정민 after the correction");
            Assert.That(g.Info.Any(n => n.Text.Contains("재난방송")), "the official word is noted");
            // the review card: the frame, the order, a ✓ per row, the verdict; then the sourced 2023 facts as notices
            var review = host.Cards.Single(c => c.Title == TwentyTwoLogic.ReviewTitle);
            Assert.That(review.Body[0], Does.Contain("진짜였는지가 아니라"));
            Assert.That(review.Body[1], Is.EqualTo("내 순서: 재난방송 확인 → 대피 준비 → 304호 어르신 → 지하주차장으로"));
            Assert.That(review.Body.Count(l => l.Contains("✓")), Is.EqualTo(TwentyTwoLogic.Rows(Careful).Count));
            Assert.That(host.Calls("enter").Select(c => c.Text), Does.Contain("apartment:lobby").And.Contain("apartment:home"), "down to 1F, then home again");
            Assert.That(review.Meta, Does.Contain("2023년 5월 31일").And.Contain("'오발령' 정정 문자"), "quotes the texts, does not rule on the dispute");
            Assert.That(review.Kind, Is.EqualTo("record"), "the player's own review, not an official notice");
            Assert.That(review.Body[2], Does.Contain("이 장면의 제안"));
            var facts = FakeStoryHost.LoadFacts();
            var foot = review.Body[review.Body.Count - 2];
            Assert.That(foot, Does.Contain(facts.Text("misfire_times")).And.Contain(facts.Text("misfire_app")).And.Contain(facts.Text("misfire_siren")).And.Contain("서울신문"),
                "that morning in 2023, sourced, at the foot of the card (not a toast the skip hides)");
            Assert.That(review.Body.Last(), Does.Contain(facts.Text("alert_levels")).And.Contain("지금 규정"), "the no-siren morning is today's rule, said so");
            Assert.That(host.Calls("spawnPerson"), Is.Empty, "nobody is put on screen");
            Assert.That(host.Chapter, Is.EqualTo(("1장", "준비 — 남은 시간 여섯 칸")), "the chapter label is put back");
        }

        [Test]
        public void ARushedMorning_IsReviewedCalmly_WithTheFactBehindEachMiss()
        {
            var (host, ctx) = Rig();
            host.Answer("chat", "call", "redial", "sleep", "doubt");
            host.Run(() => TwentyTwoScene.Run(ctx));
            var g = host.G;
            var ev = g.Events.Single(e => e.Type == TwentyTwoLogic.Event);
            Assert.That(ev.Details["picks"].Select(t => (string)t), Is.EqualTo(new[] { "chat", "call", "redial", "sleep" }));
            Assert.That((string)ev.Details["verdict"], Is.EqualTo("poor"));
            Assert.That((string)g.Know[TwentyTwoLogic.NextKnow], Is.EqualTo("doubt"));
            Assert.That(host.Talks.Any(t => t.Speaker == "정민" && t.Lines[0].Contains("받는 순간엔 알 수 없다")), "doubt is answered, not punished");
            var review = host.Cards.Single(c => c.Title == TwentyTwoLogic.ReviewTitle);
            var facts = FakeStoryHost.LoadFacts();
            Assert.That(review.Body.Any(l => l.Contains("○") && l.Contains(facts.Text("info_rumor"))));
            Assert.That(review.Body.Any(l => l.Contains("○") && l.Contains(facts.Text("comms_calls"))));
            Assert.That(review.Body.Any(l => l.Contains("다시 누웠다")));
            Assert.That(host.Talks.Any(t => t.Speaker == "304호 어르신"), Is.False, "no elder this time");
            // no blame anywhere: the texts and the review name no real office
            foreach (var t in host.Talks.SelectMany(t => t.Lines).Concat(review.Body))
                Assert.That(t, Does.Not.Contain("서울시").And.Not.Contain("행안부").And.Not.Contain("탓"), t);
        }

        [Test]
        public void TheDeafProfile_SeesInsteadOfHears_AndCallsByVideo()
        {
            var (host, ctx) = Rig();
            ctx.Settings.Profile = "deaf";
            host.Answer("official", "call", "text", "ready", "down", "check");
            host.Run(() => TwentyTwoScene.Run(ctx));
            Assert.That(host.Talks.First(t => t.Speaker == "판단").Choices.Single(c => c.Id == "call").Label, Does.Contain("영상통화"));
            Assert.That(host.Talks.Single(t => t.Speaker == "재난방송").Lines[0], Does.StartWith("화면 아래 자막"));
            Assert.That(host.Talks.SelectMany(t => t.Lines).Any(l => l.Contains("들리지 않는다") || l.Contains("사이렌 같은")), Is.False);
            Assert.That((string)host.G.Events.Single(e => e.Type == TwentyTwoLogic.Event).Details["verdict"], Is.EqualTo("partial"), "official, ready, down: moved, but no elder first");
        }

        [Test]
        public void InTheChapter_AfterTonightsSlot_WithoutANewCheckpoint()
        {
            var host = new FakeStoryHost();
            var g = TestSupport.Rules.CreateGame(7);
            g.Day = -2; g.Minute = 18 * 60 + 30; g.SetKnow("toured", true);
            g.Cond.SoldOut.Early = new List<string>(); g.Cond.SoldOut.Late = new List<string> { "water" };
            var ctx = host.CreateContext(TestSupport.Rules, g);
            foreach (var p in new[] { "rest", "rest", "rest", "rest", "rest", "rest" }) host.AnswerOpen("planner", p);
            for (int i = 0; i < 5; i++) host.QueuePress("apartment:homePlanner");
            host.QueuePress("apartment:homeBed");
            host.AnswerSpeaker("침대", "sleep");
            AnswerCarefully(host);
            host.Run(() => Prep.Run(ctx));
            Assert.That(host.Checkpoints, Is.Empty, "Prep.Run makes no checkpoint (the director's 'prep' is before it)");
            Assert.That(host.TimeSkips.Select(s => s.to), Is.EqualTo(new[] { "D-1 목 06:41", "D-1 목 09:00", "D-day 금 19:30" }));
            var types = g.Events.Select(e => e.Type).ToList();
            Assert.That(types.IndexOf(TwentyTwoLogic.Event), Is.GreaterThan(types.IndexOf("prep_action")), "after tonight's slot");
            Assert.That(g.Events.Select(e => (long)e.Day * 1440 + e.Minute), Is.Ordered, "tonight's slot ends in the evening: the interlude never moves the clock back");
            var sms = g.Events.First(e => e.Type == "sms");
            Assert.That((sms.Day, sms.Minute), Is.EqualTo((-1, TwentyTwoLogic.SentAt)));
            Assert.That(g.Events.Where(e => e.Type == "prep_action").Select(e => (int)e.Details["slot"]), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }), "every slot is still played");
            Assert.That((g.Day, g.Minute), Is.EqualTo((0, 19 * 60 + 30)));
        }

        [Test]
        public void ThePreparedStart_PlaysTheInterlude_ThenThePlannerFromSlotOne()
        {
            var host = new FakeStoryHost();
            var g = TestSupport.Rules.CreateGame(7);
            g.Day = -2; g.Minute = 21 * 60; g.SetKnow("toured", true);
            g.Slots.Used = 1; g.Slots.Done.Add("rest");
            g.Cond.SoldOut.Early = new List<string>(); g.Cond.SoldOut.Late = new List<string> { "water" };
            var ctx = host.CreateContext(TestSupport.Rules, g);
            ctx.Resume = Prep.TwentyTwoResume;
            foreach (var p in new[] { "rest", "rest", "rest", "rest", "rest" }) host.AnswerOpen("planner", p);
            for (int i = 0; i < 5; i++) host.QueuePress("apartment:homePlanner");
            host.QueuePress("apartment:homeBed");
            host.AnswerSpeaker("침대", "sleep");
            AnswerCarefully(host);
            host.Run(() => Prep.Run(ctx));
            Assert.That(host.Cards.Any(c => c.Title == "103동 302호"), Is.False, "no first-evening card");
            Assert.That(host.Calls("timeSkip").First().Text, Is.EqualTo("D-2 수 밤 -> D-1 목 06:41"));
            Assert.That(host.Opens.Where(o => o.screen == "planner").Select(o => (string)o.options["day"]).First(), Is.EqualTo("D-1 오전"));
            Assert.That(g.Slots.Used, Is.EqualTo(6));
            Assert.That(g.Events.Count(e => e.Type == TwentyTwoLogic.Event), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- 2장 and the report

        static void FakeDDay(GameState g, string elderPick)
        {
            g.Chapter = "dday"; g.Day = 0; g.Minute = DDay.Alert;
            GameRules.RecordEvent(g, "alert", new JObject { ["minute"] = DDay.Alert });
            g.Minute = DDay.Alert + 11;
            GameRules.RecordEvent(g, "arrived", new JObject { ["minute"] = DDay.Alert + 11 });
            if (elderPick != null) g.SetKnow("elderPick", elderPick);
        }

        [Test]
        public void TheSecondChapterCard_SetsThatMorningBesideTonight_Once()
        {
            var (host, ctx) = Rig();
            AnswerCarefully(host);
            host.Run(() => TwentyTwoScene.Run(ctx));
            FakeDDay(host.G, "help");
            host.Run(() => TwentyTwoScene.AfterCard(ctx));
            host.Run(() => TwentyTwoScene.AfterCard(ctx));
            var cards = host.Cards.Where(c => c.Title == TwentyTwoLogic.AfterTitle).ToList();
            Assert.That(cards, Has.Count.EqualTo(1));
            var body = cards[0].Body;
            Assert.That(body[0], Does.Contain("이유도").And.Contain("미사일 발사"));
            Assert.That(body[1], Does.Contain("경계경보라 사이렌 없이").And.Contain("공습경보라 사이렌이 1분").And.Contain("경보의 종류"), "the siren follows the alert type, not whether it is real");
            Assert.That(body[2], Is.EqualTo("그날 아침 처음 한 일: 재난방송 확인. 오늘 밤에는 경보 11분 만에 대피소에 닿았다."));
            Assert.That(body, Does.Contain("304호 어르신: 그날 아침에도, 오늘 밤에도 함께 내려갔다."));
            var facts = FakeStoryHost.LoadFacts();
            Assert.That(body.Any(l => l.Contains(facts.Text("alert_text_fix"))));
            Assert.That(body.Any(l => l.Contains(facts.Text("alert_levels"))));
            Assert.That(body.Any(l => l.Contains(facts.Text("alert_clarify"))), "the correction rule behind the 07:03 text");
            Assert.That(body.Last(), Does.Contain("출처").And.Contain("서울특별시"));
            Assert.That(cards[0].Kind, Is.EqualTo("record"));
        }

        [Test]
        public void WithoutTheInterlude_ThereIsNoCard_AndNoReportRow()
        {
            var (host, ctx) = Rig();
            FakeDDay(host.G, "go");
            host.Run(() => TwentyTwoScene.AfterCard(ctx));
            Assert.That(host.Cards, Is.Empty);
            var ids = LearningReport.Build(host.G).Rows.Select(r => r.Id).ToList();
            Assert.That(ids, Has.No.Member("ttOrder").And.No.Member("ttOfficial"));
        }

        [Test]
        public void TheLearningReport_ReadsTheOrder()
        {
            var g = TestSupport.Rules.CreateGame(7);
            GameRules.RecordEvent(g, TwentyTwoLogic.Event, Play(Careful).Result("check"));
            var rows = LearningReport.Build(g).Rows.ToDictionary(r => r.Id);
            Assert.That(rows["ttOrder"].Ok && rows["ttOfficial"].Ok, Is.True);
            Assert.That(rows["ttOrder"].Axis, Is.EqualTo("alert"));
            Assert.That(rows.ContainsKey("ttRumor") || rows.ContainsKey("ttCalls"), Is.False);
            var bad = TestSupport.Rules.CreateGame(7);
            GameRules.RecordEvent(bad, TwentyTwoLogic.Event, Play("chat", "call", "redial", "sleep").Result("doubt"));
            var b = LearningReport.Build(bad).Rows.ToDictionary(r => r.Id);
            Assert.That(b["ttOrder"].Ok || b["ttOfficial"].Ok || b["ttRumor"].Ok || b["ttCalls"].Ok, Is.False);
            var facts = FakeStoryHost.LoadFacts();
            foreach (var r in b.Values.Where(r => r.Id.StartsWith("tt"))) Assert.That(facts.Text(r.FactId), Is.Not.Null.And.Not.Empty, r.Id);
        }
    }
}
