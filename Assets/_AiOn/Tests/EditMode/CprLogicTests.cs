// The CPR rhythm minigame's rules (Game/CprLogic.cs): the fixed 110 BPM beat and its judging, hold = depth and
// release = recoil, the hidden fatigue and the gauge that only shows what is felt (Ochoa 1998), the named swap, the AED
// steps, and that every run ends with the paramedics taking over. Driven with a scripted player on a fake clock.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class CprLogicTests
    {
        const double Start = 3; // the breathing check answered at t = 3: compressing starts here

        static CprLogic Ready(bool wide = false, bool oneButton = false, string stage = "free", bool demo = false)
        {
            var L = new CprLogic(wide, oneButton, stage, demo);
            Assert.That(L.Check("tap"), Is.EqualTo("checked"));
            Assert.That(L.Call("named", 1), Is.EqualTo("call_named"));
            Assert.That(L.Breath("none", Start), Is.EqualTo("breath_ok"));
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Compress));
            return L;
        }

        /// <summary>
        /// Plays beats k = from.. while the beat is before `until` (compression time) and the phase is still Compress with the
        /// player compressing: press at beat + offset, release after hold. Ticks every 1/60 s in between, like the screen.
        /// </summary>
        static void Play(CprLogic L, int from, double until, double offset = 0, double hold = 0.17, Func<int, bool> skip = null)
        {
            for (int k = from; ; k++)
            {
                double bt = L.BeatTime(k);
                if (L.CompressTime(bt) >= until) break;
                TickTo(L, bt + offset);
                if (L.Phase != CprPhase.Compress) break;
                if (skip != null && skip(k)) continue;
                L.Press(bt + offset);
                L.Release(bt + offset + hold);
            }
        }

        static void TickTo(CprLogic L, double t)
        {
            for (double x = L.Now + 1 / 60.0; x < t; x += 1 / 60.0) L.Tick(x);
            L.Tick(t);
        }

        static int BeatAt(CprLogic L, double compressTime) => (int)Math.Ceiling(compressTime / CprLogic.Interval);

        // ------------------------------------------------------------------ the beat

        [Test]
        public void Tempo_IsFixedAt110_InsideTheGuidelinesRange_AndWideningNeverSlowsIt()
        {
            Assert.That(CprLogic.Bpm, Is.EqualTo(110));
            Assert.That(CprLogic.Bpm, Is.InRange(100, 120), "분당 100~120회 (cpr_depth_rate)");
            Assert.That(CprLogic.Interval, Is.EqualTo(60.0 / 110).Within(1e-12));
            var narrow = Ready(); var wide = Ready(wide: true);
            Assert.That(wide.BeatTime(10) - wide.BeatTime(9), Is.EqualTo(narrow.BeatTime(10) - narrow.BeatTime(9)).Within(1e-12), "same tempo");
            Assert.That(CprLogic.GoodWindowFor(true, 0), Is.GreaterThan(CprLogic.GoodWindowFor(false, 0)));
            Assert.That(CprLogic.PerfectWindowFor(true, 0), Is.GreaterThan(CprLogic.PerfectWindowFor(false, 0)));
            Assert.That(CprLogic.GoodWindowFor(true, 0), Is.LessThan(CprLogic.Interval / 2), "a wide window still belongs to one beat");
        }

        [TestCase(0.0, false, CprHit.Perfect)]
        [TestCase(-0.05, false, CprHit.Perfect)]
        [TestCase(0.1, false, CprHit.Good)]
        [TestCase(-0.12, false, CprHit.Good)]
        [TestCase(0.2, false, CprHit.Off)]
        [TestCase(0.2, true, CprHit.Good)]
        [TestCase(0.1, true, CprHit.Perfect)]
        public void APress_IsJudgedAgainstTheNearestBeat(double offset, bool wide, CprHit want)
        {
            var L = Ready(wide);
            double bt = L.BeatTime(0);
            TickTo(L, bt + offset);
            L.Press(bt + offset);
            L.Release(bt + offset + 0.17);
            Assert.That(L.Last.Hit, Is.EqualTo(want));
            Assert.That(L.Last.Beat, Is.EqualTo(0));
            Assert.That(L.Compressions, Is.EqualTo(1));
        }

        [Test]
        public void DuringTheCountIn_APressWaits_AndTwoPressesInOneBeatAreRushed()
        {
            var L = Ready();
            L.Press(Start + 0.5); L.Release(Start + 0.6);
            Assert.That(L.Feedback, Is.EqualTo("wait"));
            Assert.That(L.Compressions, Is.EqualTo(0));
            double bt = L.BeatTime(2);
            TickTo(L, bt);
            L.Press(bt); L.Release(bt + 0.1);
            L.Press(bt + 0.15); L.Release(bt + 0.2);
            Assert.That(L.Rushed, Is.EqualTo(1));
            Assert.That(L.Compressions, Is.EqualTo(1));
        }

        [Test]
        public void UnpressedBeats_AreMisses_AndPullTheQualityDown()
        {
            var L = Ready(stage: "guided");
            Play(L, 0, 10);
            double q = L.RollingQuality;
            Assert.That(q, Is.GreaterThan(0.8));
            TickTo(L, L.BeatTime(0) + 16);
            Assert.That(L.Misses, Is.GreaterThanOrEqualTo(10));
            Assert.That(L.RollingQuality, Is.EqualTo(0));
            Assert.That(L.Gauge, Is.LessThan(0.3), "a miss is noticed: the gauge drops too");
            Assert.That(L.Hint, Is.EqualTo("press"), "guided: a reminder to keep pressing");
        }

        // ------------------------------------------------------------------ hold = depth, release = recoil

        [Test]
        public void TheHoldIsTheDepth_AndLettingGoBeforeTheNextBeatIsTheRecoil()
        {
            var L = Ready();
            double bt = L.BeatTime(0);
            TickTo(L, bt); L.Press(bt); L.Release(bt + 0.06);
            Assert.That(L.Last.Depth, Is.EqualTo(0.06 / CprLogic.BaseHold).Within(1e-9));
            Assert.That(L.Feedback, Is.EqualTo("deeper"));
            bt = L.BeatTime(1);
            TickTo(L, bt); L.Press(bt); L.Release(bt + 0.2);
            Assert.That(L.Last.Depth, Is.EqualTo(1).Within(1e-9), "full depth (약 5cm) and no deeper");
            Assert.That(L.Last.Recoil, Is.True);
            Assert.That(L.Feedback, Is.EqualTo("good"));
            // leaning: still pressing when the chest should come back up
            bt = L.BeatTime(2);
            TickTo(L, bt); L.Press(bt);
            TickTo(L, L.BeatTime(3) - CprLogic.RecoilMin + 0.02);
            Assert.That(L.Leans, Is.EqualTo(1));
            Assert.That(L.Last.Recoil, Is.False);
            Assert.That(L.Feedback, Is.EqualTo("lean"));
            Assert.That(L.Last.Score, Is.EqualTo(CprLogic.LeanScore).Within(1e-9));
            L.Release(L.BeatTime(3) - 0.05);
            Assert.That(L.Compressions, Is.EqualTo(3), "the late release does not count twice");
        }

        // ------------------------------------------------------------------ fatigue and the felt gauge

        [Test]
        public void Fatigue_StartsAfterAMinute_ThenNarrowsTheWindows_NeedsLongerHolds_AndCapsTheDepth()
        {
            Assert.That(CprLogic.FatigueOf(0), Is.EqualTo(0));
            Assert.That(CprLogic.FatigueOf(59.9), Is.EqualTo(0));
            Assert.That(CprLogic.FatigueOf(60 + CprLogic.FatigueRamp / 2), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(CprLogic.FatigueOf(60 + CprLogic.FatigueRamp), Is.EqualTo(1));
            Assert.That(CprLogic.FatigueOf(1000), Is.EqualTo(1));
            Assert.That(CprLogic.GoodWindowFor(false, 1), Is.LessThan(CprLogic.GoodWindowFor(false, 0)));
            Assert.That(CprLogic.NeedHold(1), Is.GreaterThan(CprLogic.NeedHold(0)));
            Assert.That(CprLogic.DepthOf(10, 1), Is.EqualTo(1 - CprLogic.FatigueDepthCap).Within(1e-9), "no hold reaches full depth when spent");
            Assert.That(CprLogic.DepthOf(0.17, 0.5), Is.LessThan(CprLogic.DepthOf(0.17, 0)));
        }

        [Test]
        public void TheFatigueIsFelt_OnlyAfter186Seconds()
        {
            Assert.That(CprLogic.FeltAfter, Is.EqualTo(186), "Ochoa 1998: median time to feel fatigued");
            Assert.That(CprLogic.FeltFatigueOf(120), Is.EqualTo(0), "already tiring (quality dropping) but not felt");
            Assert.That(CprLogic.FatigueOf(120), Is.GreaterThan(0.5));
            Assert.That(CprLogic.FeltFatigueOf(190), Is.EqualTo(CprLogic.FatigueOf(190)));
        }

        [Test]
        public void WithoutASwap_ThePressesGetShallowAfterAMinute_WhileTheGaugeKeepsShowingWhatIsFelt()
        {
            var L = Ready();
            Play(L, 0, 98, hold: 0.168);
            Assert.That(L.Work, Is.GreaterThan(90));
            Assert.That(L.Fatigue, Is.GreaterThan(0.3));
            Assert.That(L.RollingDepth, Is.LessThan(CprLogic.ShallowBelow), "really shallow (under about 4 cm)");
            Assert.That(L.RollingFeltDepth, Is.EqualTo(1).Within(1e-9), "it feels like the same press");
            Assert.That(L.Gauge, Is.GreaterThan(0.85), "the gauge still looks fine");
            Assert.That(L.RollingQuality, Is.LessThan(L.Gauge - 0.1));
            var shallow = CprLogic.ShallowAt(L.Curve);
            Assert.That(shallow, Is.InRange(70, 98), "the drop starts after the first minute");
            var at = L.Curve.First(s => s.T == shallow);
            Assert.That(at.Gauge, Is.GreaterThan(0.8), "and the gauge did not show it then");
            Assert.That(at.Depth, Is.LessThan(CprLogic.ShallowBelow));
            // the felt feedback never says "deeper" for a press that only got shallow from fatigue
            Assert.That(L.Feedback, Is.EqualTo("good"));
        }

        // ------------------------------------------------------------------ the swap

        [Test]
        public void ANamedSwap_HandsOver_RestoresTheRescuer_AndAsksBackAfterAStint()
        {
            var L = Ready();
            Play(L, 0, 50);
            double workBefore = L.Work;
            double t = L.BeatTime(BeatAt(L, 50)) + 0.2;
            TickTo(L, t);
            Assert.That(L.RequestSwap(t, true), Is.EqualTo("swap"));
            Assert.That(L.Swaps, Is.EqualTo(1));
            Assert.That(L.FirstSwapAt, Is.EqualTo(L.CompressTime(t)).Within(1e-9));
            Assert.That(L.Swap, Is.EqualTo(CprSwap.Pending));
            Assert.That(L.Compressor, Is.EqualTo("none"), "hands changing");
            TickTo(L, t + CprLogic.SwapGap + 0.1);
            Assert.That(L.Swap, Is.EqualTo(CprSwap.On));
            Assert.That(L.Compressor, Is.EqualTo("neighbour"));
            L.Press(t + 3); L.Release(t + 3.1);
            Assert.That(L.Feedback, Is.EqualTo("neighbour"), "the player's presses do nothing while the neighbour compresses");
            TickTo(L, t + CprLogic.SwapGap + 10);
            Assert.That(L.RollingQuality, Is.EqualTo(CprLogic.NeighbourScore).Within(1e-9));
            Assert.That(L.Work, Is.LessThan(workBefore - 15), "resting recovers");
            TickTo(L, t + CprLogic.SwapGap + CprLogic.SwapStint + 0.05);
            Assert.That(L.Swap, Is.EqualTo(CprSwap.None));
            Assert.That(L.Feedback, Is.EqualTo("take_back"));
            Assert.That(L.Compressor, Is.EqualTo("me"));
            Assert.That(L.Work, Is.LessThan(10), "a 30-second stint is nearly a full rest");
            int k = BeatAt(L, L.CompressTime(L.Now) + 0.6);
            TickTo(L, L.BeatTime(k)); L.Press(L.BeatTime(k)); L.Release(L.BeatTime(k) + 0.17);
            Assert.That(L.TakeBackGap, Is.InRange(0.5, 2.0));
            Assert.That(L.Curve.Any(s => s.Who == "swap") && L.Curve.Any(s => s.Who == "gap"));
        }

        [Test]
        public void AVagueCall_MovesNobody_ForTheSwapAsForThe119Call()
        {
            var L = new CprLogic();
            L.Check("lift");
            Assert.That(L.CheckMistakes, Is.EqualTo(1));
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Check));
            L.Check("tap");
            Assert.That(L.Call("vague", 1), Is.EqualTo("call_vague"));
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Call));
            Assert.That(L.Call("self", 2), Is.EqualTo("call_self"));
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Call));
            Assert.That(L.Call("named", 3), Is.EqualTo("call_named"));
            Assert.That(L.Named && L.VagueCalls == 1 && L.SelfCalls == 1);
            L.Breath("none", 6);
            Play(L, 0, 20);
            double t = L.Now + 0.1;
            Assert.That(L.RequestSwap(t, false), Is.EqualTo("swap_vague"));
            Assert.That(L.Swap, Is.EqualTo(CprSwap.None));
            Assert.That(L.VagueSwaps, Is.EqualTo(1));
            Assert.That(L.Swaps, Is.EqualTo(0));
        }

        [Test]
        public void TheBreathingCheck_TakesUpTo10Seconds_ThenMovesOnByItself()
        {
            var late = new CprLogic(); late.Check("tap"); late.Call("named", 0);
            late.Breath("none", 12);
            Assert.That(late.BreathLate, Is.True);
            Assert.That(late.Feedback, Is.EqualTo("breath_late"));

            var wrong = new CprLogic(); wrong.Check("tap"); wrong.Call("named", 0);
            wrong.Breath("breathing", 4);
            Assert.That(wrong.BreathWrong && !wrong.BreathLate);
            Assert.That(wrong.Phase, Is.EqualTo(CprPhase.Compress), "an agonal gasp is not breathing: compressions start anyway");

            var idle = new CprLogic(); idle.Check("tap"); idle.Call("named", 0);
            idle.Tick(9); Assert.That(idle.Phase, Is.EqualTo(CprPhase.Breath)); Assert.That(idle.BreathLeft, Is.EqualTo(1).Within(1e-9));
            idle.Tick(CprLogic.BreathAuto);
            Assert.That(idle.Phase, Is.EqualTo(CprPhase.Compress));
            Assert.That(idle.BreathLate, Is.True);
            Assert.That(idle.Feedback, Is.EqualTo("breath_timeout"));
        }

        // ------------------------------------------------------------------ the AED and the handover

        [Test]
        public void TheAed_PowerPadsHandsOffShock_ThenCompressAtOnce_ThenTheParamedicsTakeOver()
        {
            var L = Ready();
            Play(L, 0, CprLogic.AedAt + 1);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPower));
            Assert.That(L.Compressor, Is.EqualTo("volunteer"), "someone keeps compressing while the AED is readied");
            double t = L.Now + 1.2;
            TickTo(L, t); L.Press(t); L.Release(t + 0.1); Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPads));
            TickTo(L, t + 1.2); L.Press(t + 1.2); L.Release(t + 1.3); Assert.That(L.Phase, Is.EqualTo(CprPhase.AedAnalyze));
            Assert.That(L.Compressor, Is.EqualTo("none"), "everyone off the patient");
            Assert.That(L.AedTouches, Is.EqualTo(0), "a short press for the pads is not a touch");
            TickTo(L, t + 3);
            L.Press(t + 3);
            Assert.That(L.AedTouches, Is.EqualTo(1));
            Assert.That(L.Feedback, Is.EqualTo("aed_touch"));
            Assert.That(L.AnalyzeLeft, Is.EqualTo(CprLogic.AnalyzeSeconds).Within(1e-6), "the analysis starts over");
            L.Release(t + 3.1);
            TickTo(L, t + 3 + CprLogic.AnalyzeSeconds + 0.05);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedShock));
            double shock = L.Now + CprLogic.StepLock + 0.1;
            TickTo(L, shock); L.Press(shock); L.Release(shock + 0.1);
            Assert.That(L.AedDone, Is.True);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Compress));
            Assert.That(L.Feedback, Is.EqualTo("aed_resume"));
            int k = BeatAt(L, L.CompressTime(shock) + 0.5);
            Play(L, k, CprLogic.HandoverAt + 30);
            Assert.That(L.ResumeDelay, Is.InRange(0.4, 1.6));
            TickTo(L, L.Now + 2);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover));
            Assert.That(L.CompressTime(L.EndAt), Is.GreaterThanOrEqualTo(CprLogic.HandoverAt));
            Assert.That(L.Curve.Any(s => s.Who == "aed"));
            var ev = CprLogic.Evaluate(L.Result());
            Assert.That(ev.Rows.Single(r => r.Id == "cprAed").Ok, Is.False, "a hand touched during the analysis");
            Assert.That(ev.Rows.Single(r => r.Id == "cprAed").Fact, Is.EqualTo("cpr_aed_clear"));
        }

        [Test]
        public void WithNoInputAtAll_TheVolunteerTakesTheAedSteps_AndItStillEndsWithTheHandover()
        {
            var L = Ready();
            TickTo(L, Start + 260);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover), "no soft lock and no other ending");
            Assert.That(L.AedAuto, Is.EqualTo(3), "power, pads and shock by the volunteer");
            Assert.That(L.Compressions, Is.EqualTo(0));
            var r = L.Result();
            Assert.That(r.Played, Is.True);
            Assert.That(CprLogic.Evaluate(r).Result, Is.EqualTo("solo"));
        }

        // ------------------------------------------------------------------ accessibility

        [Test]
        public void OneButton_JudgesOnlyTheBeat_FatigueStillTakesTheDepth_AndALongHoldAsksForTheSwap()
        {
            var L = Ready(oneButton: true);
            double bt = L.BeatTime(0);
            TickTo(L, bt); L.Press(bt);
            Assert.That(L.Compressions, Is.EqualTo(1), "counted on the press");
            Assert.That(L.Last.Depth, Is.EqualTo(1).Within(1e-9));
            L.Release(bt + 0.02);
            Assert.That(L.Last.Recoil, Is.True);
            Play(L, 1, 100 - 1, hold: 0.02);
            Assert.That(L.Last.Depth, Is.LessThan(0.9), "the hidden fatigue applies in every mode");
            Assert.That(L.Last.FeltDepth, Is.EqualTo(1).Within(1e-9));

            var M = Ready(oneButton: true);
            Play(M, 0, 30, hold: 0.02);
            double t = M.BeatTime(BeatAt(M, 31));
            TickTo(M, t); M.Press(t);
            TickTo(M, t + CprLogic.LongHold + 0.05);
            Assert.That(M.Swaps, Is.EqualTo(1));
            Assert.That(M.Swap, Is.Not.EqualTo(CprSwap.None));
        }

        [Test]
        public void TheSwapReminder_ComesInTheGuidedAndHintStagesOnly()
        {
            foreach (var (stage, at) in new[] { ("guided", CprLogic.HintGuided), ("hint", CprLogic.HintHint) })
            {
                var L = Ready(stage: stage);
                Play(L, 0, at - 2);
                Assert.That(L.Hint, Is.Null, stage);
                Play(L, BeatAt(L, at - 2), at + 1);
                Assert.That(L.Hint, Is.EqualTo("swap"), stage);
                L.HintShown();
                Assert.That(L.Hint, Is.Null);
            }
            var free = Ready(stage: "free");
            Play(free, 0, 99);
            Assert.That(free.Hint, Is.Null);
        }

        // ------------------------------------------------------------------ results

        [Test]
        public void Evaluate_ReadsTheRun_AndTheResultRoundTripsThroughJson()
        {
            var L = new CprLogic(stage: "hint");
            L.Check("tap"); L.Call("named", 0); L.Breath("none", 4);
            Play(L, 0, 50);
            double t = L.Now + 0.2;
            L.RequestSwap(t, true);
            TickTo(L, t + 40);
            Play(L, BeatAt(L, L.CompressTime(L.Now) + 0.5), CprLogic.AedAt + 1);
            TickTo(L, L.Now + 0.1);
            for (int i = 0; i < 3; i++) { double x = L.Now + (i == 2 ? CprLogic.AnalyzeSeconds + CprLogic.StepLock + 0.1 : 1.2); TickTo(L, x); L.Press(x); L.Release(x + 0.05); }
            Assert.That(L.AedDone, Is.True);
            Play(L, BeatAt(L, L.CompressTime(L.Now) + 0.3), CprLogic.HandoverAt + 25);
            TickTo(L, L.Now + 2);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover));
            var r = L.Result();
            var ev = CprLogic.Evaluate(r);
            Assert.That(ev.Result, Is.EqualTo("relay"));
            Assert.That(ev.Rows.Select(x => x.Id), Is.EqualTo(new[] { "cprPoint", "cprBreath", "cprRhythm", "cprSwap", "cprAed" }));
            Assert.That(ev.Rows.All(x => x.Ok), Is.True, string.Join(" / ", ev.Rows.Where(x => !x.Ok).Select(x => x.Label)));
            Assert.That(ev.Rows.Select(x => x.Fact), Is.EqualTo(new[] { "cpr_point", "cpr_breath", "cpr_depth_rate", "cpr_fatigue", "cpr_aed_clear" }));

            var back = CprResult.Read(r.ToJson(true));
            Assert.That(back.Played && back.Named);
            Assert.That(back.Swaps, Is.EqualTo(1));
            Assert.That(back.FirstSwapAt, Is.EqualTo(r.FirstSwapAt).Within(1e-3));
            Assert.That(back.ResumeDelay, Is.EqualTo(r.ResumeDelay).Within(1e-3));
            Assert.That(back.Curve.Count, Is.EqualTo(r.Curve.Count));
            Assert.That(back.Curve.Select(s => s.Who), Is.EqualTo(r.Curve.Select(s => s.Who)));
            var summary = r.ToJson();
            Assert.That(summary["curve"], Is.Null, "the save keeps the summary only");
            Assert.That(CprLogic.Evaluate(CprResult.Read(summary)).Rows.Select(x => x.Ok), Is.EqualTo(ev.Rows.Select(x => x.Ok)));

            var none = CprResult.Read(new Newtonsoft.Json.Linq.JObject());
            Assert.That(none.Played, Is.False);
            Assert.That(CprLogic.Evaluate(none).Result, Is.EqualTo("skipped"));
            Assert.That(CprLogic.Evaluate(none).Rows, Is.Empty);
        }

        [Test]
        public void TheDebrief_SaysWhenItGotShallow_AndWhatTheGaugeShowedThen()
        {
            var L = Ready();
            Play(L, 0, CprLogic.AedAt - 1, hold: 0.168);
            var r = L.Result();
            Assert.That(r.ShallowAt, Is.GreaterThan(60));
            Assert.That(r.GaugeAtShallow, Is.GreaterThan(0.7));
            var lines = CprLogic.DebriefLines(r);
            Assert.That(lines[0], Does.Contain(CprLogic.Clock(r.ShallowAt)).And.Contain("게이지는").And.Contain("괜찮다고 느꼈지만"));
            Assert.That(lines[1], Does.Contain("교대를 요청하지 않았습니다"));
            Assert.That(CprLogic.Clock(72), Is.EqualTo("1분 12초"));
            Assert.That(CprLogic.Clock(45), Is.EqualTo("45초"));
            var ev = CprLogic.Evaluate(r);
            Assert.That(ev.Rows.Single(x => x.Id == "cprSwap").Ok, Is.False);
        }

        [Test]
        public void ShallowAt_NeedsARunOfOwnShallowSeconds()
        {
            CprSample S(double t, double d, string who = "me") => new CprSample { T = t, Depth = d, Who = who };
            Assert.That(CprLogic.ShallowAt(new[] { S(0, 1), S(1, 0.7), S(2, 0.7), S(3, 0.7), S(4, 1) }), Is.EqualTo(-1), "three seconds is not a run");
            Assert.That(CprLogic.ShallowAt(new[] { S(0, 1), S(1, 0.7), S(2, 0.7), S(3, 0.7), S(4, 0.7) }), Is.EqualTo(1));
            Assert.That(CprLogic.ShallowAt(new[] { S(0, 0.7, "swap"), S(1, 0.7, "swap"), S(2, 0.7, "swap"), S(3, 0.7, "swap") }), Is.EqualTo(-1), "the neighbour's stretch is not the player's");
            Assert.That(CprLogic.ShallowAt(new[] { S(0, -1), S(1, -1), S(2, -1), S(3, -1) }), Is.EqualTo(-1), "no press yet");
        }

        // ------------------------------------------------------------------ sound

        [Test]
        public void TheMetronomeClick_IsShortAndAudible_AndTheAedTonesAreDistinct()
        {
            foreach (var accent in new[] { false, true })
            {
                var c = AiOn.Story.ProceduralAudio.ClickSamples(accent);
                Assert.That(c.Length / (double)AiOn.Story.ProceduralAudio.Rate, Is.LessThan(CprLogic.Interval / 4), "a click never smears into the next beat");
                Assert.That(c.Max(Math.Abs), Is.GreaterThan(0.1f));
            }
            Assert.That(AiOn.Story.ProceduralAudio.ClickSamples(true), Is.Not.EqualTo(AiOn.Story.ProceduralAudio.ClickSamples(false)), "the count's first beat is accented");
            foreach (var kind in new[] { "power", "analyze", "charge", "shock" })
                Assert.That(AiOn.Story.ProceduralAudio.AedSamples(kind).Max(Math.Abs), Is.GreaterThan(0.05f), kind);
        }

        // ------------------------------------------------------------------ words

        [Test]
        public void TheWords_AvoidTheUnverifiedWording_AndCoverEveryFeedback()
        {
            foreach (var s in CprText.AllLines())
            {
                Assert.That(s, Does.Not.Contain("2분").And.Not.Contain("분마다"), s);
                Assert.That(Regex.IsMatch(s, @"(?<!\d)(1337|111|113)(?!\d)"), Is.False, s);
                Assert.That(s, Does.Not.Contain("사망").And.Not.Contain("죽"), s + ": no death");
            }
            foreach (var id in new[] { "checked", "check_wrong", "call_named", "call_vague", "call_self", "breath_ok", "breath_late", "breath_timeout", "breath_wrong",
                         "wait", "good", "bit_early", "bit_late", "early", "late", "deeper", "lean", "miss", "rush", "swap", "swap_vague", "neighbour", "take_back",
                         "aed_arrive", "aed_pads", "aed_auto_power", "aed_auto_pads", "aed_analyze", "aed_touch", "aed_pulled", "step_wait", "shock_wait",
                         "aed_shock_ready", "aed_resume", "aed_auto_shock", "handover" })
                Assert.That(CprText.Feedback(id), Is.Not.Empty, id);
            Assert.That(CprText.CallChoices.Single(c => c.id == "named").label, Does.Contain(CprText.Caller).And.Contain(CprText.AedPerson));
            Assert.That(CprText.HintSwap, Does.Contain("지치기 전에 교대를 요청"));
            Assert.That(CprText.Title(CprPhase.Handover), Is.EqualTo("구급대 인계"));
            Assert.That(CprText.CollapseLine, Is.Not.EqualTo(CprText.CheckBody), "the collapse is not said twice");
            Assert.That(CprText.BreathBody, Does.Contain("헐떡"), "the gasp is shown before the choice, not only after it");
            Assert.That(CprText.Feedback("check_wrong"), Does.Not.Contain("흔들"), "no line the sources do not carry");
        }

        // ------------------------------------------------------------------ review fixes: the AED's input, the lesson, the rows

        /// <summary>Presses on every beat from before the AED's arrival until `until` (absolute time), whatever the phase.</summary>
        static void PressOnTheBeat(CprLogic L, int from, double until)
        {
            for (int k = from; L.BeatTime(k) < until; k++)
            {
                double bt = L.BeatTime(k);
                TickTo(L, bt); L.Press(bt); L.Release(bt + 0.17);
            }
        }

        [Test]
        public void PressingOnTheBeat_ThroughTheAedsArrival_NeverTurnsItOnOrTouchesThePatient()
        {
            var L = Ready();
            Play(L, 0, CprLogic.AedAt - 2);
            double arrive = L.BeatTime(0) + CprLogic.AedAt;
            PressOnTheBeat(L, BeatAt(L, CprLogic.AedAt - 2), arrive + 6);
            Assert.That(L.AedArrived, Is.True);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPower), "power and pads are not skipped by the compression rhythm");
            Assert.That(L.AedTouches, Is.EqualTo(0));
            Assert.That(L.Feedback, Is.EqualTo("step_wait"), "told to stop and read the AED's step");
            // stop, then press: the power goes on
            double t = L.Now + CprLogic.StepPause + 0.05;
            TickTo(L, t); L.Press(t); L.Release(t + 0.1);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPads));
            // a press straight after a step does nothing either (the step's lock)
            TickTo(L, t + 0.3); L.Press(t + 0.3); L.Release(t + 0.4);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPads));
            Assert.That(L.AedButton(t + 0.5), Is.False, "the on-screen button waits out the lock too");
            Assert.That(L.AedButton(t + CprLogic.StepLock + 0.05), Is.True);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedAnalyze));
        }

        [Test]
        public void HoldingTheButtonThroughTheAnalysis_IsAHandOnThePatient()
        {
            var L = Ready();
            Play(L, 0, CprLogic.AedAt + 1);
            double t = L.Now + 1.2;
            TickTo(L, t); L.Press(t); L.Release(t + 0.1);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPads));
            double pads = t + 1.2;
            TickTo(L, pads); L.Press(pads); // the pads ... and the finger stays down
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedAnalyze));
            TickTo(L, pads + CprLogic.AnalyzeSeconds);
            Assert.That(L.AedTouches, Is.EqualTo(1), "counted once for the hold");
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedAnalyze), "and the analysis started over");
            L.Release(pads + CprLogic.AnalyzeSeconds + 0.1);
            TickTo(L, L.Now + CprLogic.AnalyzeSeconds + 0.1);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedShock));
            Assert.That(CprLogic.Evaluate(L.Result()).Rows.Single(r => r.Id == "cprAed").Ok, Is.False);
        }

        [Test]
        public void TouchingAgainAndAgain_TheVolunteerPullsTheHandsAway_AndTheAnalysisEnds()
        {
            var L = Ready(oneButton: true);
            Play(L, 0, CprLogic.AedAt + 1, hold: 0.02);
            TickTo(L, L.Now + CprLogic.AutoStepAfter * 2 + 0.5); // the volunteer: power, then pads
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedAnalyze));
            double t = L.Now;
            for (int i = 0; i < 40; i++) { t += 0.5; TickTo(L, t); L.Press(t); L.Release(t + 0.05); }
            Assert.That(L.AedTouches, Is.GreaterThan(CprLogic.MaxRestarts), "every touch is still counted");
            Assert.That(L.Phase, Is.Not.EqualTo(CprPhase.AedAnalyze), "no soft lock in the analysis");
        }

        [Test]
        public void AFirmPresserAlone_GetsShallowBeforeTheAed_WhileTheGaugeStaysHigh()
        {
            Assert.That(CprLogic.SoloShallowAt, Is.LessThan(CprLogic.AedAt - 5), "the lesson fits before the AED");
            var L = Ready();
            Play(L, 0, CprLogic.AedAt - 0.5, hold: 0.33); // as long as the recoil allows
            var shallow = CprLogic.ShallowAt(L.Curve);
            Assert.That(shallow, Is.InRange(CprLogic.SoloShallowAt - 5, CprLogic.AedAt - 3));
            Assert.That(L.Curve.First(s => s.T == shallow).Gauge, Is.GreaterThan(0.85));
            Assert.That(L.Leans, Is.EqualTo(0), "a 0.33 s hold still lets the chest come back up");
        }

        [Test]
        public void WithoutAShallowStretch_TheDebriefStillTellsWhatAloneWouldHaveDone()
        {
            var L = Ready();
            Play(L, 0, 50);
            double t = L.Now + 0.2;
            L.RequestSwap(t, true);
            TickTo(L, t + 40);
            var r = L.Result();
            Assert.That(r.ShallowAt, Is.EqualTo(-1));
            var lines = CprLogic.DebriefLines(r);
            Assert.That(lines[0], Does.Contain("유지됐습니다"));
            Assert.That(lines[1], Does.Contain("혼자 계속 눌렀다면").And.Contain(CprLogic.Clock(CprLogic.SoloShallowAt)).And.Contain("게이지"));
        }

        [Test]
        public void TheRhythmRow_JudgesTheOwnCompressions_AndAnEarlySwapIsNotBeforeTiring()
        {
            // a player who swapped at 5 s and then pressed almost nothing
            var L = Ready();
            Play(L, 0, 5);
            double t = L.Now + 0.2;
            L.RequestSwap(t, true);
            TickTo(L, L.BeatTime(0) + 60);
            var r = L.Result();
            var ev = CprLogic.Evaluate(r);
            Assert.That(ev.Rows.Single(x => x.Id == "cprRhythm").Ok, Is.False);
            Assert.That(ev.Rows.Single(x => x.Id == "cprRhythm").Label, Does.Contain("적었다").Or.Contain("놓쳐"));
            Assert.That(ev.Rows.Single(x => x.Id == "cprSwap").Ok, Is.False);
            Assert.That(ev.Rows.Single(x => x.Id == "cprSwap").Label, Does.Contain("바로 교대"));
            Assert.That(CprLogic.DebriefLines(r)[0], Does.Not.Contain("유지됐습니다"), "never 'depth kept' for a few presses");

            // missing most beats
            var M = Ready();
            Play(M, 0, 60, skip: k => k % 3 != 0);
            Assert.That(CprLogic.Evaluate(M.Result()).Rows.Single(x => x.Id == "cprRhythm").Label, Does.Contain("놓쳐"));

            // on the beat
            var N = Ready();
            Play(N, 0, 60);
            Assert.That(CprLogic.Evaluate(N.Result()).Rows.Single(x => x.Id == "cprRhythm").Ok, Is.True);
        }

        [Test]
        public void LowVision_TheBreathClockStartsAfterTheReading()
        {
            var L = new CprLogic(); L.Check("tap"); L.Call("named", 0);
            L.DelayBreath(12);
            L.Tick(11);
            Assert.That(L.BreathLeft, Is.EqualTo(CprLogic.BreathLimit), "still being read");
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Breath));
            L.Breath("none", 20);
            Assert.That(L.BreathLate, Is.False);
            Assert.That(L.BreathSeconds, Is.EqualTo(8).Within(1e-9));
        }

        [Test]
        public void TheDemoSkip_AfterTheShock_EndsWithTheHandover()
        {
            var L = Ready();
            Play(L, 0, 30);
            Assert.That(L.SkipToHandover(L.Now + 0.1), Is.False, "not before the AED");
            TickTo(L, L.BeatTime(0) + CprLogic.AedAt + 3 * CprLogic.AutoStepAfter + CprLogic.AnalyzeSeconds + 1);
            Assert.That(L.AedDone, Is.True);
            Assert.That(L.SkipToHandover(L.Now + 0.1), Is.True);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover));
            Assert.That(L.Result().Seconds, Is.LessThan(CprLogic.HandoverAt));
        }

        // ------------------------------------------------------------------ feedback 11-25: B1 demo AED, B4 facts after the pick, B6 rows

        /// <summary>The AED's three steps the way the 3분 시연 경로 takes them (the on-screen button, each once it unlocks).</summary>
        static double TakeTheAedSteps(CprLogic L)
        {
            TickTo(L, L.Now + 0.1);
            for (int i = 0; i < 3; i++)
            {
                double x = L.Now + (i == 2 ? CprLogic.AnalyzeSeconds + CprLogic.StepLock + 0.1 : 1.2);
                TickTo(L, x);
                Assert.That(L.AedButton(x), Is.True, "AED step " + i);
            }
            Assert.That(L.AedDone, Is.True);
            return L.Now;
        }

        [Test]
        public void Demo_TheAedComesAfterAbout25Seconds_TheStoryKeeps100()
        {
            Assert.That(CprLogic.DemoAedAt, Is.EqualTo(25));
            Assert.That(new CprLogic().Demo, Is.False, "the story's default");
            Assert.That(new CprLogic().AedTime, Is.EqualTo(CprLogic.AedAt));
            Assert.That(new CprLogic().HandoverTime, Is.EqualTo(CprLogic.HandoverAt));
            Assert.That(CprLogic.AedAt, Is.EqualTo(100), "the main story's AED time is unchanged");
            Assert.That(CprLogic.HandoverAt, Is.EqualTo(150));

            var story = Ready();
            Play(story, 0, CprLogic.DemoAedAt + 2);
            Assert.That(story.Phase, Is.EqualTo(CprPhase.Compress), "no AED yet in the story");
            Assert.That(story.AedArrived, Is.False);

            var demo = Ready(demo: true);
            Assert.That(demo.AedTime, Is.EqualTo(CprLogic.DemoAedAt));
            Play(demo, 0, CprLogic.DemoAedAt - 1);
            Assert.That(demo.AedArrived, Is.False);
            Play(demo, BeatAt(demo, CprLogic.DemoAedAt - 1), CprLogic.DemoAedAt + 1);
            Assert.That(demo.AedArrived, Is.True);
            Assert.That(demo.Phase, Is.EqualTo(CprPhase.AedPower));
            Assert.That(demo.Feedback, Is.EqualTo("aed_arrive"));
            Assert.That(demo.CompressTime(demo.Now), Is.InRange(CprLogic.DemoAedAt, CprLogic.DemoAedAt + 1));
        }

        [Test]
        public void Demo_The3MinutePath_SwapAt20s_AedThenSkip_FitsTheShortVisit()
        {
            // COMPETITION_PLAN 3분 시연 경로: press about 20 s, ask the named neighbour, the AED's steps, then 건너뛰기
            var L = Ready(stage: "guided", demo: true);
            Play(L, 0, 21);
            double t = L.BeatTime(BeatAt(L, 21)) + 0.1;
            TickTo(L, t);
            Assert.That(L.RequestSwap(t, true), Is.EqualTo("swap"));
            TickTo(L, L.BeatTime(0) + CprLogic.DemoAedAt + 0.2);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.AedPower), "the AED comes while the neighbour has the chest");
            Assert.That(L.Swap, Is.EqualTo(CprSwap.None));
            TakeTheAedSteps(L);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Compress));
            Assert.That(L.SkipToHandover(L.Now + 1), Is.True, "the skip works once the shock is given");
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover));
            var r = L.Result();
            Assert.That(r.Seconds, Is.LessThan(40), "compressing to the skip: well under a minute (the count-in and pages come on top)");
            Assert.That(r.Swaps, Is.EqualTo(1));
            var ev = CprLogic.Evaluate(r);
            Assert.That(ev.Rows.Single(x => x.Id == "cprSwap").Ok, Is.True, string.Join(" / ", ev.Rows.Select(x => x.Label)));
        }

        [Test]
        public void Demo_WithoutTheSkip_TheParamedicsStillComeSoon_AndWithNoInputToo()
        {
            var L = Ready(demo: true);
            Play(L, 0, CprLogic.DemoAedAt + 1);
            TakeTheAedSteps(L);
            double shock = L.CompressTime(L.Now);
            Play(L, BeatAt(L, shock + 0.5), CprLogic.HandoverAt);
            TickTo(L, L.Now + 2);
            Assert.That(L.Phase, Is.EqualTo(CprPhase.Handover));
            double end = L.CompressTime(L.EndAt);
            Assert.That(end, Is.GreaterThanOrEqualTo(shock + CprLogic.AfterShockMin - 0.1), "compressions go on a while after the shock");
            Assert.That(end, Is.LessThan(60), "not the story's 150 s");

            var idle = Ready(demo: true);
            TickTo(idle, Start + 120);
            Assert.That(idle.Phase, Is.EqualTo(CprPhase.Handover), "no soft lock");
            Assert.That(idle.AedAuto, Is.EqualTo(3));
            Assert.That(idle.CompressTime(idle.EndAt), Is.LessThan(CprLogic.HandoverAt));
        }

        [Test]
        public void Demo_TheNoteIsPlainAndHonest()
        {
            Assert.That(CprText.DemoAedNote, Does.StartWith("시연용").And.Contain("AED"));
            Assert.That(CprText.AllLines(), Has.Member(CprText.DemoAedNote), "checked with the other words");
        }

        [Test]
        public void TheStepFacts_ComeWithTheFeedback_NeverBeforeThePick()
        {
            // nothing picked yet: no answer on the page (the breathing check keeps only its 10 seconds)
            Assert.That(CprText.StepFacts(CprPhase.Check, null), Is.Empty);
            Assert.That(CprText.StepFacts(CprPhase.Call, null), Is.Empty);
            Assert.That(CprText.StepFacts(CprPhase.Breath, null), Is.EqualTo(new[] { "cpr_breath" }));
            // each step's right answer is not on its page before the pick
            foreach (var (p, right) in new[] { (CprPhase.Check, "checked"), (CprPhase.Call, "call_named"), (CprPhase.Breath, "breath_ok") })
            {
                var answer = CprText.FactFor(right);
                Assert.That(answer, Is.Not.Null, right);
                Assert.That(CprText.BeforeFact(p), Is.Not.EqualTo(answer), p + ": the answer is not shown before the pick");
            }
            Assert.That(CprText.FactFor("breath_wrong"), Is.EqualTo("cpr_gasp"));
            Assert.That(CprText.FactFor("breath_late"), Is.EqualTo("cpr_breath"));
            Assert.That(CprText.FactFor("breath_timeout"), Is.EqualTo("cpr_breath"));
            // after a pick: the fact comes with the feedback
            Assert.That(CprText.StepFacts(CprPhase.Check, "check_wrong"), Is.EqualTo(new[] { "cpr_check" }));
            Assert.That(CprText.StepFacts(CprPhase.Call, "checked"), Is.EqualTo(new[] { "cpr_check" }), "the check's fact with its feedback, not the call's answer");
            Assert.That(CprText.StepFacts(CprPhase.Call, "call_vague"), Is.EqualTo(new[] { "cpr_point" }));
            Assert.That(CprText.StepFacts(CprPhase.Call, "call_self"), Is.EqualTo(new[] { "cpr_point" }));
            Assert.That(CprText.StepFacts(CprPhase.Breath, "call_named"), Is.EqualTo(new[] { "cpr_point", "cpr_breath" }));
            Assert.That(CprText.StepFacts(CprPhase.Breath, "call_named"), Has.None.EqualTo("cpr_gasp"));
            Assert.That(CprText.FactFor(null), Is.Null);
            Assert.That(CprText.FactFor("good"), Is.Null, "the compression feedback has no fact box");
            // every fact named is one the scene hands the screen, with its source
            foreach (var id in new[] { "checked", "check_wrong", "call_named", "call_vague", "call_self", "breath_ok", "breath_late", "breath_timeout", "breath_wrong" })
                Assert.That(AiOn.Story.CprScene.FactIds, Has.Member(CprText.FactFor(id)), id);
        }

        [Test]
        public void FewOwnPresses_WithoutASwap_TheRowsDoNotAlsoSayPressedOnAlone()
        {
            CprResult R(int presses, double firstSwapAt) => new CprResult
            {
                Played = true, Named = true, BreathSeconds = 5, Compressions = presses, Misses = 2, Quality = 0.8,
                FirstSwapAt = firstSwapAt, Swaps = firstSwapAt >= 0 ? 1 : 0, ResumeDelay = 1,
            };
            var few = CprLogic.Evaluate(R(CprLogic.MinOwnPresses - 1, -1));
            Assert.That(few.Rows.Select(x => x.Id), Is.EqualTo(new[] { "cprPoint", "cprBreath", "cprRhythm", "cprAed" }));
            Assert.That(few.Rows.Single(x => x.Id == "cprRhythm").Label, Is.EqualTo("직접 누른 압박이 적었다"));
            Assert.That(few.Rows.Select(x => x.Label), Has.None.Contains("혼자 계속 눌렀다"));
            Assert.That(few.Result, Is.EqualTo("solo"), "the ladder result is unchanged");

            var many = CprLogic.Evaluate(R(CprLogic.MinOwnPresses, -1));
            Assert.That(many.Rows.Single(x => x.Id == "cprSwap").Label, Is.EqualTo("교대를 요청하지 않고 혼자 계속 눌렀다"));

            var fewSwapped = CprLogic.Evaluate(R(5, 3));
            Assert.That(fewSwapped.Rows.Single(x => x.Id == "cprSwap").Label, Does.Contain("바로 교대"), "an early swap is still told");

            // the run in the screenshot: nobody pressed, the volunteer took the AED steps
            var idle = Ready();
            TickTo(idle, Start + 260);
            Assert.That(CprLogic.Evaluate(idle.Result()).Rows.Select(x => x.Id), Has.None.EqualTo("cprSwap"));

            // the debrief text says the same: few presses, and not also "혼자 계속 눌렀다면" or "교대를 요청하지 않았습니다"
            Assert.That(CprLogic.FewAlone(R(CprLogic.MinOwnPresses - 1, -1)), Is.True);
            Assert.That(CprLogic.FewAlone(R(CprLogic.MinOwnPresses, -1)), Is.False);
            Assert.That(CprLogic.FewAlone(R(5, 3)), Is.False, "a swap was asked for");
            var fewLines = CprLogic.DebriefLines(R(CprLogic.MinOwnPresses - 1, -1));
            Assert.That(fewLines, Has.Member("직접 누른 압박이 적었습니다."));
            Assert.That(fewLines, Has.None.Contains("혼자 계속 눌렀다면"));
            Assert.That(fewLines, Has.None.Contains("교대를 요청하지 않았습니다"));
            var manyLines = CprLogic.DebriefLines(R(CprLogic.MinOwnPresses, -1));
            Assert.That(manyLines, Has.Some.Contains("혼자 계속 눌렀다면"), "pressed enough: the lesson the run did not show is still told");
            Assert.That(manyLines, Has.Some.StartsWith("교대를 요청하지 않았습니다"));
            Assert.That(CprLogic.DebriefLines(idle.Result()), Has.None.Contains("교대를 요청하지 않았습니다"), "the idle run's text");
        }

        // ------------------------------------------------------------------ review 11-26: the demo fatigue clock

        [Test]
        public void Demo_AFirmPresserAlone_GetsShallowBeforeTheSoonerAed_WhileTheGaugeStaysHigh()
        {
            Assert.That(CprLogic.DemoSoloShallowAt, Is.LessThan(CprLogic.DemoAedAt - 5), "the lesson fits before the demo AED");
            Assert.That(CprLogic.SoloShallowAtFor(true), Is.EqualTo(CprLogic.DemoSoloShallowAt));
            Assert.That(CprLogic.SoloShallowAtFor(false), Is.EqualTo(CprLogic.SoloShallowAt));
            var L = Ready(demo: true);
            Play(L, 0, CprLogic.DemoAedAt - 0.5, hold: 0.33);
            var r = L.Result();
            Assert.That(r.Demo, Is.True);
            Assert.That(r.ShallowAt, Is.GreaterThanOrEqualTo(0), "the demo shows the presses getting shallow");
            Assert.That(r.ShallowAt, Is.InRange(CprLogic.DemoSoloShallowAt - 5, CprLogic.DemoAedAt - 3));
            Assert.That(r.GaugeAtShallow, Is.GreaterThan(0.85), "while the gauge still looks fine (the felt fatigue is unchanged)");
            Assert.That(L.FeltFatigue, Is.EqualTo(0));
            Assert.That(CprLogic.DebriefLines(r)[0], Does.Contain("실제로는 얕아지고 있었습니다"), "the line the feedback calls the strongest moment");

            // the same presses in the story: still full depth at that time
            var story = Ready();
            Play(story, 0, CprLogic.DemoAedAt - 0.5, hold: 0.33);
            Assert.That(story.Result().ShallowAt, Is.EqualTo(-1), "the story keeps its fatigue clock");
            Assert.That(story.Result().Demo, Is.False);
            Assert.That(CprLogic.FatigueStart, Is.EqualTo(60), "the story's numbers are unchanged");
            Assert.That(CprLogic.FatigueRamp, Is.EqualTo(60));
            Assert.That(CprLogic.FatigueOf(30), Is.EqualTo(0));
            Assert.That(CprLogic.FatigueOf(30, true), Is.GreaterThan(0.5));
        }

        [Test]
        public void Demo_ThePlansSwapAfter21Seconds_IsStillBeforeTiring_AndTheDebriefTellsTheDemoTime()
        {
            var L = Ready(stage: "guided", demo: true);
            Play(L, 0, 21.5, hold: 0.2);
            double t = L.BeatTime(BeatAt(L, 21.5)) + 0.1;
            TickTo(L, t);
            Assert.That(L.RequestSwap(t, true), Is.EqualTo("swap"));
            TickTo(L, L.BeatTime(0) + CprLogic.DemoAedAt + 0.2);
            TakeTheAedSteps(L);
            double shock = L.CompressTime(L.Now);
            Play(L, BeatAt(L, shock + 0.5), shock + 3, hold: 0.2); // back on the chest at once
            Assert.That(L.SkipToHandover(L.Now + 0.1), Is.True);
            var r = L.Result();
            var ev = CprLogic.Evaluate(r);
            Assert.That(ev.Rows.Select(x => x.Ok), Has.All.True, string.Join(" / ", ev.Rows.Select(x => x.Label)));
            // the swap comes after the presses got shallow: the graph and the first line show the hidden fatigue on this route too
            Assert.That(r.ShallowAt, Is.InRange(0, 21.5));
            Assert.That(CprLogic.DebriefLines(r)[0], Does.Contain("실제로는 얕아지고 있었습니다"));
            Assert.That(CprLogic.DebriefLines(r).Last(), Is.EqualTo(CprText.DemoDebriefNote), "the debrief says the times were shortened");

            // a demo run without a shallow stretch says the demo's "alone" time, the story its own
            var kept = new CprResult { Played = true, Compressions = 60, FirstSwapAt = 21, Swaps = 1, Demo = true };
            Assert.That(CprLogic.DebriefLines(kept), Has.Some.Contains(CprLogic.Clock(CprLogic.DemoSoloShallowAt) + "쯤부터"));
            kept.Demo = false;
            Assert.That(CprLogic.DebriefLines(kept), Has.Some.Contains(CprLogic.Clock(CprLogic.SoloShallowAt) + "쯤부터"));
            Assert.That(CprLogic.DebriefLines(kept), Has.No.Member(CprText.DemoDebriefNote), "the story's debrief has no demo line");
            // the flag goes into a prepared run's event only, and comes back
            Assert.That(CprResult.Read(r.ToJson()).Demo, Is.True);
            Assert.That(new CprResult { Played = true }.ToJson().ContainsKey("demo"), Is.False, "the story's event is as before");
            Assert.That(CprText.DemoAedNote, Does.Contain("지치는 시간"), "the page says the fatigue is shortened too");
        }
    }
}
