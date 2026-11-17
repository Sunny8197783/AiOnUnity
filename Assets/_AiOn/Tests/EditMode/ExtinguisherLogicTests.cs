using System.Linq;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class ExtinguisherLogicTests
    {
        static void HoldFor(ExtinguisherLogic l, double seconds)
        {
            l.StartHold();
            for (double t = 0; t < seconds; t += 0.05) l.Tick(0.05);
        }

        static void SweepOut(ExtinguisherLogic l)
        {
            int dir = 1;
            for (int i = 0; i < 40 && !l.Ended; i++) { l.Stroke(dir); dir = -dir; }
        }

        [Test]
        public void StepOrderIsTheOriginalHashSort()
        {
            // hash(s) = reduce((a, c) => (a * 31 + c) % 1009, 7)
            Assert.AreEqual((7 * 31 + 'a') % 1009, ExtinguisherLogic.Hash("a"));
            var order = ExtinguisherLogic.StepOrder(Drills.ExtSteps).Select(s => s.Id).ToArray();
            var expected = Drills.ExtSteps.Select(s => s.Id).OrderBy(ExtinguisherLogic.Hash).ToArray();
            CollectionAssert.AreEqual(expected, order);
            CollectionAssert.AreEquivalent(new[] { "shout", "pin", "aim", "squeeze", "sweep" }, order);
        }

        [Test]
        public void FullCorrectRunPutsTheFireOut()
        {
            var l = new ExtinguisherLogic("guided", "small");
            Assert.AreEqual("decide", l.Phase);
            Assert.AreEqual("fight", l.Recommended);
            l.Fight();
            Assert.AreEqual("act", l.Phase);
            Assert.AreEqual("소화기를 들고 불 앞에 섰다.", l.Feedback);
            Assert.AreEqual("shout", l.Expected());
            l.Press("shout");
            Assert.AreEqual("pin", l.Expected());
            l.Press("pin"); Assert.IsTrue(l.PinOut);
            l.Press("aim"); Assert.IsTrue(l.Aimed);
            Assert.AreEqual("문을 등지고 노즐을 불 쪽으로 향했다.", l.Feedback);
            bool squeezed = false; l.Squeezed += () => squeezed = true;
            HoldFor(l, 0.8);
            Assert.IsTrue(squeezed); Assert.IsTrue(l.Spraying); Assert.IsTrue(l.IsDone("squeeze"));
            string ended = null; l.EndedWith += k => ended = k;
            SweepOut(l);
            Assert.AreEqual("out", ended);
            Assert.AreEqual(0, l.FireLv);
            Assert.IsFalse(l.Spraying);
            Assert.IsTrue(l.ShoutFirst);
            Assert.IsFalse(l.RetreatVisible);
            var r = l.Result();
            CollectionAssert.AreEqual(new[] { "fight", "shout", "pin", "aim", "squeeze", "sweep" }, r["sequence"].ToObject<string[]>());
            Assert.AreEqual("out", (string)r["result"]);
        }

        [Test]
        public void WrongOrderGivesTheOriginalMistakeTextAndRaisesDanger()
        {
            var l = new ExtinguisherLogic("free", "small");
            l.Fight();
            l.Press("aim");
            Assert.AreEqual("안전핀을 먼저 뽑아야 한다.", l.Feedback);
            Assert.AreEqual(0.12, l.Danger, 1e-9);
            l.Press("sweep");
            Assert.AreEqual("안전핀이 그대로다. 아무것도 나오지 않는다.", l.Feedback);
            l.StartHold();
            Assert.IsFalse(l.Holding);
            Assert.AreEqual("안전핀이 꽂혀 있어 손잡이가 눌리지 않는다.", l.Feedback);
            l.Press("pin");
            l.Press("pin");
            Assert.AreEqual("이미 했다.", l.Feedback);
            l.Press("sweep");
            Assert.AreEqual("노즐을 먼저 불 쪽으로 향해야 한다.", l.Feedback);
            l.Press("aim");
            l.Press("sweep");
            Assert.AreEqual("손잡이를 쥐지 않아 분말이 나오지 않는다.", l.Feedback);
            Assert.AreEqual("순서가 맞지 않는다.", ExtinguisherLogic.MistakeText("aim", "squeeze"));
            CollectionAssert.AreEqual(new[] { "fight", "pin", "aim" }, l.Sequence.ToArray()); // mistakes are not recorded
        }

        [Test]
        public void ShoutTwiceAndSweepAfterSqueezeHint()
        {
            var l = new ExtinguisherLogic("guided", "small");
            l.Fight();
            l.Press("shout"); l.Press("shout");
            Assert.AreEqual("이미 알렸다.", l.Feedback);
            l.Press("pin"); l.Press("aim"); HoldFor(l, 1);
            l.Press("sweep");
            Assert.AreEqual("불 그림 위에서 마우스를 좌우로 크게 움직여 쓸어 주세요. (키보드: ←/→)", l.Feedback);
            Assert.AreEqual(1, l.Sequence.Count(x => x == "shout"));
        }

        [Test]
        public void ReleasingTheHandleEarlyResetsTheHold()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            Assert.AreEqual("act", l.Phase);
            l.Press("pin"); l.Press("aim");
            l.StartHold(); l.Tick(0.1); l.Tick(0.1);
            Assert.AreEqual(0.2, l.Hold, 1e-9);
            Assert.AreEqual(0.2 / 0.7, l.HoldProgress, 1e-9);
            l.StopHold();
            Assert.AreEqual(0, l.Hold);
            Assert.AreEqual("손잡이를 끝까지 힘껏 쥐어야 한다. 길게 누르세요.", l.Feedback);
            Assert.IsFalse(l.IsDone("squeeze"));
        }

        [Test]
        public void OneSidedStrokesAreSlowAndScolded()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            l.Press("pin"); l.Press("aim"); HoldFor(l, 1);
            double before = l.FireLv;
            l.Stroke(1); // first stroke counts as alternating
            Assert.AreEqual(before - 0.085, l.FireLv, 1e-9);
            l.Stroke(1);
            Assert.AreEqual(before - 0.085 - 0.03, l.FireLv, 1e-9);
            Assert.AreEqual("한쪽으로만 뿌리고 있다. 좌우로 쓸듯이.", l.Feedback);
        }

        [Test]
        public void ArrowsOnlyStrokeAfterSqueeze()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            Assert.IsFalse(l.Arrow(-1));
            l.Press("pin"); l.Press("aim"); HoldFor(l, 1);
            Assert.IsTrue(l.Arrow(-1));
            Assert.AreEqual(-1, l.LastDir);
        }

        [Test]
        public void MouseSwingsCountOneStrokePerTurn()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            l.Press("pin"); l.Press("aim"); HoldFor(l, 1);
            double lv = l.FireLv;
            l.PointerMove(0.5);
            l.PointerMove(0.6);
            Assert.AreEqual(lv, l.FireLv, 1e-9); // 0.1 of the width: not yet a stroke
            l.PointerMove(0.7);
            Assert.AreEqual(lv - 0.085, l.FireLv, 1e-9);
            l.PointerMove(0.9); // same direction: locked until it turns back
            Assert.AreEqual(lv - 0.085, l.FireLv, 1e-9);
            l.PointerMove(0.7); // turns back: 0.2 > 0.16
            Assert.AreEqual(lv - 0.17, l.FireLv, 1e-9);
            Assert.AreEqual(-1, l.LastDir);
            l.PointerLeave();
            l.PointerMove(0.2); // no previous x after leaving
            Assert.AreEqual(lv - 0.17, l.FireLv, 1e-9);
        }

        [Test]
        public void SmallFireSpreadsWhenTheMeterFills()
        {
            var l = new ExtinguisherLogic("free", "small");
            Assert.AreEqual(1.0 / 32, l.GrowPerS, 1e-12);
            l.Fight();
            string ended = null; l.EndedWith += k => ended = k;
            for (int i = 0; i < 400 && !l.Ended; i++) l.Tick(0.1);
            Assert.AreEqual("spread", ended);
            Assert.AreEqual("불이 번졌다", ExtinguisherLogic.EndTitle(ended, false));
            Assert.AreEqual("계속", ExtinguisherLogic.EndOk(ended));
            Assert.IsTrue(l.RetreatVisible);
            l.Close();
            Assert.AreEqual("incomplete", (string)l.Result()["result"]);
        }

        [Test]
        public void DecideDoesNotGrowTheDanger()
        {
            var l = new ExtinguisherLogic("guided", "small");
            for (int i = 0; i < 50; i++) l.Tick(0.1);
            Assert.AreEqual(0, l.Danger);
            Assert.AreEqual(0.36, l.FireLv, 1e-9);
        }

        [Test]
        public void LargeFireIsOverwhelmingAndRetreatIsRight()
        {
            var l = new ExtinguisherLogic("guided", "large");
            Assert.AreEqual("retreat", l.Recommended);
            Assert.AreEqual(0.88, l.FireLv, 1e-9);
            Assert.AreEqual(2.4 / 70, l.GrowPerS, 1e-12);
            Assert.AreEqual("천장까지 닿는 불길", l.Title);
            Assert.AreEqual("버틸 수 있는 시간", l.MeterLabel);
            l.Fight();
            l.Press("pin"); l.Press("aim");
            Assert.AreEqual("노즐을 불 쪽으로 향했다. 열기가 얼굴까지 닿는다.", l.Feedback);
            HoldFor(l, 1);
            for (int i = 0; i < 100; i++) l.Stroke(i % 2 == 0 ? 1 : -1);
            Assert.IsFalse(l.Ended); // sweeping never puts a large fire out
            string ended = null; l.EndedWith += k => ended = k;
            for (int i = 0; i < 400 && !l.Ended; i++) l.Tick(0.1);
            Assert.AreEqual("overwhelmed", ended);
            Assert.AreEqual("그래도 버틴다", ExtinguisherLogic.EndOk(ended));
            // Holding on gives 'escalated'; retreating from the end screen gives 'retreat-ok'.
            var stay = l.Result();
            Assert.AreEqual("escalated", (string)stay["result"]);
            l.Retreat();
            Assert.IsTrue(l.Closed);
            Assert.AreEqual("retreat-ok", (string)l.Result()["result"]);
            Assert.AreEqual("retreat", l.Sequence.Last());
        }

        [Test]
        public void RetreatFromTheDecisionAndEsc()
        {
            var l = new ExtinguisherLogic("hint", "large");
            bool closed = false; l.CloseRequested += () => closed = true;
            l.Escape();
            Assert.IsTrue(closed);
            var r = l.Result();
            CollectionAssert.AreEqual(new[] { "retreat" }, r["sequence"].ToObject<string[]>());
            Assert.AreEqual("retreat-ok", (string)r["result"]);
            l.Retreat(); // closed: nothing more
            Assert.AreEqual(1, l.Sequence.Count);
        }

        [Test]
        public void EscAfterTheFireIsOutClosesWithoutRetreat()
        {
            var l = new ExtinguisherLogic("guided", "small");
            l.Fight(); l.Press("pin"); l.Press("aim"); HoldFor(l, 1); SweepOut(l);
            l.Retreat(); // ignored once out
            Assert.IsFalse(l.Closed);
            l.Escape();
            Assert.IsTrue(l.Closed);
            Assert.AreEqual("out", (string)l.Result()["result"]);
            Assert.IsFalse(l.ShoutFirst);
            Assert.IsFalse(l.Sequence.Contains("retreat"));
        }

        [Test]
        public void PracticeNeverGrowsAndStopsWithoutRetreat()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            Assert.AreEqual(0, l.GrowPerS);
            Assert.AreEqual("소화기 · 연습", l.Eyebrow);
            Assert.AreEqual("사용 순서 익히기", l.Title);
            Assert.AreEqual("연습 그만하기", l.RetreatLabel);
            l.Press("sweep");
            Assert.AreEqual(0, l.Danger); // practice mistakes cost nothing
            for (int i = 0; i < 100; i++) l.Tick(0.1);
            Assert.IsFalse(l.Ended);
            l.RetreatButton();
            Assert.IsTrue(l.Closed);
            var r = l.Result();
            CollectionAssert.AreEqual(new[] { "fight" }, r["sequence"].ToObject<string[]>());
            Assert.AreEqual("incomplete", (string)r["result"]);
        }

        [Test]
        public void PracticeEndKeepsRetreatHiddenAndSaysPracticeDone()
        {
            var l = new ExtinguisherLogic("guided", "small", practice: true);
            l.Press("pin"); l.Press("aim"); HoldFor(l, 1); SweepOut(l);
            Assert.AreEqual("out", l.EndKind);
            Assert.AreEqual("연습 완료", ExtinguisherLogic.EndTitle("out", true));
            Assert.IsFalse(l.RetreatVisible);
        }

        [Test]
        public void UnknownCustomStepsScoreIncomplete()
        {
            var steps = new[] { new DrillStep { Id = "dance", Label = "춤", Facts = new string[0] }, new DrillStep { Id = "pin", Label = "핀", Facts = new string[0] } };
            var l = new ExtinguisherLogic("guided", "small", steps);
            l.Fight();
            l.Press("dance"); // not an op: a mistake, not recorded
            Assert.AreEqual("incomplete", l.ResultKind());
        }

        [Test]
        public void DangerAndFireWords()
        {
            Assert.AreEqual("ok", ExtinguisherLogic.DangerState(0.45));
            Assert.AreEqual("warn", ExtinguisherLogic.DangerState(0.46));
            Assert.AreEqual("over", ExtinguisherLogic.DangerState(0.76));
            Assert.AreEqual("여유 있음", ExtinguisherLogic.DangerWord(0.1));
            Assert.AreEqual("서둘러야 함", ExtinguisherLogic.DangerWord(0.5));
            Assert.AreEqual("곧 번짐", ExtinguisherLogic.DangerWord(0.9));
            Assert.AreEqual("꺼짐", ExtinguisherLogic.FireWord(0));
            Assert.AreEqual("큼", ExtinguisherLogic.FireWord(0.7));
        }

        [Test]
        public void OptionsParseLikeTheOriginal()
        {
            var d = ExtinguisherLogic.FromOptions(new JObject());
            Assert.AreEqual("guided", d.Mode); Assert.IsFalse(d.Large); Assert.IsFalse(d.Practice);
            Assert.AreEqual(5, d.Steps.Count); Assert.AreEqual(2, d.Choices.Count);
            var o = ExtinguisherLogic.FromOptions(JObject.Parse("{\"mode\":\"hint\",\"fire\":\"large\",\"practice\":1}"));
            Assert.AreEqual("hint", o.Mode); Assert.IsTrue(o.Large); Assert.IsTrue(o.Practice);
            Assert.AreEqual("힌트 가능", o.ModeChip);
            var c = ExtinguisherLogic.FromOptions(JObject.Parse("{\"steps\":[{\"id\":\"pin\",\"label\":\"x\",\"facts\":[\"ext_1\"]}]}"));
            Assert.AreEqual(1, c.Steps.Count); Assert.AreEqual("x", c.Steps[0].Label);
            Assert.IsFalse(ExtinguisherLogic.Truthy(JToken.Parse("\"\"")));
            Assert.IsFalse(ExtinguisherLogic.Truthy(JToken.Parse("0")));
            Assert.IsTrue(ExtinguisherLogic.Truthy(JToken.Parse("\"no\"")));
        }

        [Test]
        public void FactTextsDropsMissingFacts()
        {
            var t = ExtinguisherLogic.FactTexts(new[] { "a", "b", "c" }, id => id == "b" ? null : id.ToUpper());
            CollectionAssert.AreEqual(new[] { "A", "C" }, t);
        }
    }
}
