// 3D 소화기 (competition upgrade): the pure rules (Game/HeldExtinguisherLogic.cs), the 1장 pressure-gauge check
// (Game/ExtinguisherCheck.cs), the 2D screen's low-pressure variant (ExtinguisherLogic Low), and the story glue
// (Story/ExtinguisherScene.cs) on FakeStoryHost with a stand-in view that aims like a player would.
using System;
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
    public class HeldExtinguisherTests
    {
        const double Dt = 1 / 30.0;

        static AimSample At(double distance = 2.5, bool onBase = true, bool onFlame = false, double lateral = 0, bool behind = true, bool squeeze = false) =>
            new AimSample { Seen = true, Distance = distance, OnBase = onBase, OnFlame = onFlame, Lateral = lateral, ExitBehind = behind, Squeeze = squeeze };

        /// <summary>Runs the logic for seconds with a sample per frame (t = seconds since the start of this call).</summary>
        static void Run(HeldExtinguisherLogic l, double seconds, Func<double, AimSample> sample)
        {
            for (double t = 0; t < seconds && !l.Ended; t += Dt) { l.Input = sample(t); l.Tick(Dt); }
        }

        /// <summary>A good sweep: on the base, swinging 0.4 m either side about twice a second.</summary>
        static AimSample Sweep(double t, bool behind = true) => At(lateral: 0.4 * Math.Sin(t * 6), squeeze: true, behind: behind);

        static HeldExtinguisherLogic Ready(ExtinguisherSetup s = null)
        {
            var l = new HeldExtinguisherLogic(s ?? new ExtinguisherSetup());
            l.Input = At(); l.Tick(Dt);
            Assert.That(l.PullPin(1), Is.True);
            Run(l, 0.5, _ => At()); // aim held on the base
            return l;
        }

        // ---------------------------------------------------------------- geometry

        [Test]
        public void Aim_TheBase_TheFlames_TheFloorInFront_AndBesideIt()
        {
            // eye at 1.6 m, 3 m back from the fire's base centre (0, 0, 0), looking along +z
            double Down(double h) => (h - 1.6) / 3.0;
            AimHit Look(double dx, double dy) { var len = Math.Sqrt(dx * dx + dy * dy + 1); return HeldExtinguisherLogic.Aim(0, 1.6, -3, dx / len, dy / len, 1 / len, 0, 0, 0, 0.5, 0.45, 1.6); }
            var b = Look(0, Down(0.2));
            Assert.That(b.OnBase, Is.True); Assert.That(b.OnFlame, Is.False);
            var f = Look(0, Down(1.2));
            Assert.That(f.OnFlame, Is.True); Assert.That(f.OnBase, Is.False);
            Assert.That(Look(0, Down(2.4)).OnFlame, Is.False, "over the flames");
            Assert.That(Look(0.4, Down(0.2)).OnBase, Is.False, "1.2 m to the side");
            var floor = Look(0, -0.7); // hits the floor ~0.7 m in front of the boxes
            Assert.That(floor.OnBase, Is.True, "powder aimed low rolls into the base");
            var right = Look(0.1, Down(0.2));
            Assert.That(Math.Abs(right.Lateral), Is.GreaterThan(0.2).And.LessThan(0.4), "the sweep reads turns, so only the size matters");
            Assert.That(HeldExtinguisherLogic.Aim(0, 1.6, -3, 0, 0, -1, 0, 0, 0, 0.5, 0.45, 1.6).OnBase, Is.False, "facing away");
        }

        [Test]
        public void TheExit_IsBehind_OnlyWhenTheWayToItPointsBack()
        {
            Assert.That(HeldExtinguisherLogic.Behind(1, 0, -10, 0), Is.True);
            Assert.That(HeldExtinguisherLogic.Behind(1, 0, -5, 3), Is.True);
            Assert.That(HeldExtinguisherLogic.Behind(1, 0, 0, 5), Is.False, "to the side");
            Assert.That(HeldExtinguisherLogic.Behind(1, 0, 4, 0), Is.False, "in front");
            Assert.That(HeldExtinguisherLogic.Behind(0, 0, -4, 0), Is.False);
        }

        // ---------------------------------------------------------------- the official order

        [Test]
        public void TheOfficialOrder_PutsASmallFireOut_AndScoresAsThe2DScreenWould()
        {
            var l = Ready(new ExtinguisherSetup { Shouted = true });
            Run(l, 12, t => Sweep(t));
            Assert.That(l.EndKind, Is.EqualTo("out"));
            Assert.That(l.Sequence, Is.EqualTo(new[] { "fight", "shout", "pin", "aim", "squeeze", "sweep" }));
            var ev = l.Eval();
            Assert.That((ev.Result, ev.Correct), Is.EqualTo(("out", true)));
            Assert.That(l.Powder, Is.GreaterThan(0), "a full extinguisher is enough when swept on the base");
            Assert.That(l.SprayTime, Is.LessThan(HeldExtinguisherLogic.FullCharge));
            var r = l.Result();
            Assert.That((string)r["result"], Is.EqualTo("out"));
            Assert.That((string)r["view"], Is.EqualTo("3d"));
            Assert.That(HeldExtinguisherLogic.BaseOk(r), Is.True);
            Assert.That(HeldExtinguisherLogic.BehindOk(r), Is.True);
            Assert.That(HeldExtinguisherLogic.RetreatOk(r), Is.Null, "nothing to retreat from");
            Assert.That(l.Strokes, Is.GreaterThan(3));
            // without the shout first it is still out, but not the whole order
            var quiet = Ready();
            Run(quiet, 12, t => Sweep(t));
            Assert.That((quiet.Eval().Result, quiet.Eval().Correct, quiet.Eval().FirstError), Is.EqualTo(("out", false, "shout")));
        }

        [Test]
        public void HoldingTheSprayOnOneSpot_RunsThePowderOutBeforeTheFireIsOut()
        {
            var l = Ready();
            Run(l, 12, _ => At(squeeze: true));
            Assert.That(l.Ended, Is.False);
            Assert.That(l.Powder, Is.EqualTo(0));
            Assert.That(l.FireLv, Is.GreaterThan(0.02));
            Assert.That(l.Done("sweep"), Is.False);
            Assert.That(l.TakeSaid(), Has.Some.Contains("쓸듯이"));
            // the fire is lost now: stepping back is the right call
            l.Retreat();
            var ev = l.Eval();
            Assert.That((ev.Result, ev.Correct), Is.EqualTo(("retreat-ok", true)));
            Assert.That(HeldExtinguisherLogic.RetreatOk(l.Result()), Is.True);
        }

        [Test]
        public void SprayingTheFlames_DoesAFractionOfSprayingTheBase()
        {
            var onBase = Ready(); var onFlame = Ready();
            double b0 = onBase.FireLv, f0 = onFlame.FireLv;
            Run(onBase, 2, t => Sweep(t));
            Run(onFlame, 2, t => At(onBase: false, onFlame: true, lateral: 0.4 * Math.Sin(t * 6), squeeze: true));
            double baseDrop = b0 - onBase.FireLv, flameDrop = f0 - onFlame.FireLv;
            Assert.That(flameDrop, Is.LessThan(baseDrop * 0.3));
            Assert.That(onFlame.TakeSaid(), Has.Some.Contains("불길 위"));
            Run(onFlame, 3, t => At(onBase: false, onFlame: true, lateral: 0.4 * Math.Sin(t * 6), squeeze: true));
            Assert.That(HeldExtinguisherLogic.BaseOk(onFlame.Result()), Is.False);
        }

        [Test]
        public void TheExitInFront_ThickensTheSmoke_AndIsReported()
        {
            var l = Ready();
            Run(l, 3, t => Sweep(t, behind: false));
            Assert.That(l.SmokeExtra, Is.GreaterThan(0.1));
            Assert.That(l.TakeSaid(), Has.Some.Contains("출구가 등 뒤에 있지 않다"));
            Run(l, 12, t => Sweep(t, behind: false));
            Assert.That(HeldExtinguisherLogic.BehindOk(l.Result()), Is.False);
        }

        [Test]
        public void ThePinFirst_TheHandleDoesNothingWhileItIsIn_AndAHalfPullSlidesBack()
        {
            var l = new HeldExtinguisherLogic(new ExtinguisherSetup());
            Run(l, 1, _ => At(squeeze: true));
            Assert.That(l.Spraying, Is.False);
            Assert.That(l.Powder, Is.EqualTo(HeldExtinguisherLogic.FullCharge));
            Assert.That(l.TakeSaid(), Has.Some.Contains("안전핀이 꽂혀"));
            Assert.That(l.PullPin(0.5), Is.False);
            l.ReleasePin();
            Assert.That(l.PinProgress, Is.EqualTo(0));
            Assert.That(l.PullPin(0.6) || l.PullPin(0.6), Is.True);
            Assert.That(l.Sequence, Is.EqualTo(new[] { "fight", "pin" }));
        }

        [Test]
        public void SprayingBeforeAiming_WastesPowder_ButTheOrderStaysRight()
        {
            var l = new HeldExtinguisherLogic(new ExtinguisherSetup());
            l.PullPin(1);
            Run(l, 1, _ => At(onBase: false, squeeze: true));
            Assert.That(l.MissTime, Is.GreaterThan(0.9));
            Assert.That(l.Sequence, Is.EqualTo(new[] { "fight", "pin" }), "a squeeze into the air is not the squeeze step");
            Run(l, 12, t => Sweep(t));
            Assert.That(l.Sequence, Is.EqualTo(new[] { "fight", "pin", "aim", "squeeze", "sweep" }));
        }

        [Test]
        public void TooFar_ThePowderDoesNotReach()
        {
            var l = Ready();
            double f0 = l.FireLv;
            Run(l, 2, t => At(distance: 5.5, lateral: 0.4 * Math.Sin(t * 6), squeeze: true));
            Assert.That(l.FireLv, Is.GreaterThanOrEqualTo(f0));
            Assert.That(l.TakeSaid(), Has.Some.Contains("닿지 않는다"));
        }

        // ---------------------------------------------------------------- a low extinguisher (1장 gauge)

        [Test]
        public void ALowExtinguisher_IsShortAndWeak_ButAGoodSweepStillJustPutsASmallFireOut()
        {
            var l = Ready(new ExtinguisherSetup { Low = true });
            Assert.That(l.Capacity, Is.EqualTo(HeldExtinguisherLogic.LowCharge));
            Assert.That(l.PowderShare, Is.LessThan(0.8), "the meter shows it short of a full one from the start");
            Run(l, 15, t => Sweep(t));
            Assert.That(l.EndKind, Is.EqualTo("out"), "winnable with a good sweep on the base");
            var full = Ready();
            Run(full, 15, t => Sweep(t));
            Assert.That(l.SprayTime, Is.GreaterThan(full.SprayTime), "but it takes longer");
            Assert.That(l.Powder, Is.LessThan(HeldExtinguisherLogic.LowCharge * 0.3), "and most of its powder");
            var still = Ready(new ExtinguisherSetup { Low = true });
            Run(still, 15, _ => At(squeeze: true));
            Assert.That(still.EndKind, Is.Not.EqualTo("out"), "held on one spot it runs dry");
            Assert.That(still.Powder, Is.EqualTo(0));
            Assert.That(still.FireLv, Is.GreaterThan(0.1));
            Assert.That((bool)still.Result()["low"], Is.True);
        }

        [Test]
        public void TheGauge_ZonesFeedback_AndWhoGetsTheLowOne()
        {
            Assert.That(ExtinguisherCheck.Zone(ExtinguisherCheck.CorridorNeedle), Is.EqualTo("low"));
            Assert.That(ExtinguisherCheck.Zone(0.5), Is.EqualTo("ok"));
            Assert.That(ExtinguisherCheck.Zone(0.8), Is.EqualTo("over"));
            Assert.That(ExtinguisherCheck.ReadingFeedback("ok", 0.29), Does.Contain("왼쪽"));
            Assert.That(ExtinguisherCheck.ReadingFeedback("low", 0.29), Does.StartWith("맞다"));
            foreach (var ans in new[] { "ok", "over" }) Assert.That(ExtinguisherCheck.ReadingFeedback(ans, 0.29), Does.Not.Contain("노란").And.Not.Contain("빨간"), "no unsourced colour layout");
            var g = TestSupport.Rules.CreateGame(7);
            Assert.That(ExtinguisherCheck.IsLow(g), Is.True, "never checked");
            Assert.That(ExtinguisherCheck.Checked(g), Is.False);
            g.SetKnow(ExtinguisherCheck.Know, ExtinguisherCheck.Left);
            Assert.That(ExtinguisherCheck.IsLow(g), Is.True, "checked and left");
            g.SetKnow(ExtinguisherCheck.Know, ExtinguisherCheck.Reported);
            Assert.That(ExtinguisherCheck.IsLow(g), Is.False, "the office replaced it");
            var o = ExtinguisherCheck.Outcome.Read(JObject.Parse("{answer:'ok',action:'report',needle:0.29}"));
            Assert.That((o.Played, o.ReadOk, o.Report), Is.EqualTo((true, false, true)));
            Assert.That(ExtinguisherCheck.Outcome.Read(new JObject()).Played, Is.False, "Esc: not played");
            var facts = FakeStoryHost.LoadFacts();
            foreach (var id in ExtinguisherCheck.FactIds.Concat(new[] { "ext_cover" }))
                Assert.That(facts.Source(facts.SourceId(id)), Is.Not.Null, id);
        }

        [Test]
        public void The2DScreen_ALowExtinguisher_AlternatingStillWins_OneSidedRunsDry()
        {
            var l = new ExtinguisherLogic("guided", "small", low: true);
            l.Fight(); l.Press("pin"); l.Press("aim"); l.StartHold(); for (int i = 0; i < 30; i++) l.Tick(0.05);
            int n = 0;
            for (; n < 20 && !l.Ended; n++) l.Stroke(n % 2 == 0 ? -1 : 1);
            Assert.That(l.EndKind, Is.EqualTo("out"));
            var full = new ExtinguisherLogic("guided", "small");
            full.Fight(); full.Press("pin"); full.Press("aim"); full.StartHold(); for (int i = 0; i < 30; i++) full.Tick(0.05);
            int m = 0;
            for (; m < 20 && !full.Ended; m++) full.Stroke(m % 2 == 0 ? -1 : 1);
            Assert.That(full.EndKind, Is.EqualTo("out"), "the full one is unchanged");
            Assert.That(n, Is.GreaterThan(m), "the low one needs more strokes");
            var one = new ExtinguisherLogic("guided", "small", low: true);
            one.Fight(); one.Press("pin"); one.Press("aim"); one.StartHold(); for (int i = 0; i < 30; i++) one.Tick(0.05);
            for (int i = 0; i < 20 && !one.Ended; i++) one.Stroke(1);
            Assert.That(one.EndKind, Is.EqualTo("empty"));
            Assert.That(one.ResultKind(), Is.EqualTo("incomplete"));
            Assert.That(ExtinguisherLogic.EndTitle("empty", false), Is.EqualTo("분말이 다 떨어졌다"));
            Assert.That(ExtinguisherLogic.FromOptions(JObject.Parse("{low:true}")).Low, Is.True);
            Assert.That(ExtinguisherLogic.FromOptions(JObject.Parse("{low:true,practice:true}")).Low, Is.False);
        }

        // ---------------------------------------------------------------- retreat is right; fixating is not

        [Test]
        public void ALargeFire_StayingOnIt_IsOverwhelmed_AndEscalates()
        {
            var l = Ready(new ExtinguisherSetup { Fire = "large" });
            Run(l, 30, t => Sweep(t));
            Assert.That(l.EndKind, Is.EqualTo("overwhelmed"));
            Assert.That(l.Fixated, Is.True);
            Assert.That(l.Eval().Result, Is.EqualTo("escalated"));
            Assert.That(HeldExtinguisherLogic.RetreatOk(l.Result()), Is.False);
        }

        [Test]
        public void ALargeFire_StoppingAndWalkingBack_IsARetreat()
        {
            var l = Ready(new ExtinguisherSetup { Fire = "large", RetreatDistance = 7 });
            Run(l, 2, t => Sweep(t));
            Run(l, 10, t => At(distance: 2.5 + t * 2, onBase: false));
            Assert.That(l.EndKind, Is.EqualTo("retreat"));
            var ev = l.Eval();
            Assert.That((ev.Result, ev.Correct), Is.EqualTo(("retreat-ok", false)), "tried first: right in the end, not at first");
            Assert.That(HeldExtinguisherLogic.RetreatOk(l.Result()), Is.True);
        }

        [Test]
        public void ASmallFireThatGrows_KeepingAtIt_DrivesYouBack_AsAFixatedRetreat()
        {
            var l = Ready(new ExtinguisherSetup { Mode = "free" }); // spreads after 32 s
            Run(l, 40, _ => At(onBase: false, onFlame: true));
            Assert.That(l.Grown, Is.True);
            Run(l, 30, _ => At());
            Assert.That(l.EndKind, Is.EqualTo("overwhelmed"));
            Assert.That(l.Sequence.Last(), Is.EqualTo("retreat"));
            Assert.That(l.Eval().Result, Is.EqualTo("retreat-ok"));
            Assert.That(HeldExtinguisherLogic.RetreatOk(l.Result()), Is.False, "fixating loses the point");
        }

        [Test]
        public void TheStairs_OrQ_AreARetreat_AndWalkingOffBeforeComingCloseIsNot()
        {
            var a = new HeldExtinguisherLogic(new ExtinguisherSetup());
            a.Tick(Dt); // the first frame, before the scene measured anything
            Assert.That((a.Approached, a.Ended), Is.EqualTo((false, false)), "an unmeasured frame is not 'at the fire'");
            Run(a, 2, _ => At(distance: 12, onBase: false));
            Assert.That(a.Ended, Is.False, "never came close: still deciding");
            a.Input = new AimSample { Seen = true, Distance = 12, Away = true }; a.Tick(Dt);
            Assert.That(a.EndKind, Is.EqualTo("retreat"));
            var q = new HeldExtinguisherLogic(new ExtinguisherSetup());
            q.Retreat();
            Assert.That(q.Sequence, Is.EqualTo(new[] { "fight", "retreat" }));
        }

        [Test]
        public void Practice_DrainsNothing_EndsAsPractice_AndQStops()
        {
            var l = Ready(new ExtinguisherSetup { Practice = true });
            Run(l, 15, t => Sweep(t, behind: false));
            Assert.That(l.EndKind, Is.EqualTo("out"));
            Assert.That(l.Powder, Is.EqualTo(l.Capacity));
            Assert.That(l.SmokeExtra, Is.EqualTo(0));
            Assert.That((string)l.Result()["result"], Is.EqualTo("practice"));
            var s = new HeldExtinguisherLogic(new ExtinguisherSetup { Practice = true });
            s.Retreat();
            Assert.That((s.EndKind, (string)s.Result()["result"]), Is.EqualTo(("stopped", "stopped")));
            Assert.That(s.Shout(), Is.False, "no alarm in practice");
        }

        [Test]
        public void Setup_RoundTrips_AndTheStepsNameTheKeys()
        {
            var s = new ExtinguisherSetup { Mode = "hint", Fire = "large", Low = true, Shouted = true, Toggle = true, RetreatDistance = 9 };
            var r = ExtinguisherSetup.Read(s.ToJson());
            Assert.That((r.Mode, r.Fire, r.Low, r.Shouted, r.Toggle, r.RetreatDistance), Is.EqualTo(("hint", "large", true, true, true, 9.0)));
            Assert.That(ExtinguisherSetup.Read(null).Fire, Is.EqualTo("small"));
            var steps = new HeldExtinguisherLogic(new ExtinguisherSetup { Toggle = true }).Steps();
            Assert.That(steps.Select(x => x.Id), Is.EqualTo(new[] { "shout", "carry", "pin", "aim", "squeeze", "sweep" }), "\"불이야!\" first, as Drills scores it");
            Assert.That(steps[4].Text, Does.Contain("누르기"), "toggle wording");
            Assert.That(new HeldExtinguisherLogic(new ExtinguisherSetup()).Steps()[4].Text, Does.Contain("길게"));
            Assert.That(new HeldExtinguisherLogic(new ExtinguisherSetup { Practice = true }).Steps().Select(x => x.Id), Has.No.Member("shout"), "no alarm in practice");
            Assert.That(new HeldExtinguisherLogic(new ExtinguisherSetup { Shouted = true }).Steps()[0].Done, Is.True, "already called at 304's door");
            foreach (var st in new HeldExtinguisherLogic(new ExtinguisherSetup()).Steps()) Assert.That(st.Text, Does.Not.Contain("걸음"), "no unsourced distances");
            // a lost fire (the shelter's, large from the start) lists the retreat, naming the place's way out
            var large = new HeldExtinguisherLogic(s);
            Assert.That(large.Steps().Select(x => x.Id), Is.EqualTo(new[] { "shout", "stop", "leave" }));
            Assert.That(large.Hint(), Does.Contain("물러나").And.Contain("계단"));
            var shelter = new HeldExtinguisherLogic(new ExtinguisherSetup { Fire = "large", ExitWord = "출입구" });
            Assert.That(shelter.Hint(), Does.Contain("출입구").And.Not.Contain("계단"));
            Assert.That(shelter.Steps().Last().Text, Does.Contain("출입구"));
            Assert.That(ReferenceEquals(shelter.Hint(), shelter.Hint()), Is.True, "the hint is cached while nothing changes");
        }

        [Test]
        public void Q_AsksFirst_ASecondPressSoonAfterStepsBack()
        {
            var l = Ready();
            Assert.That(l.RetreatPress(), Is.False);
            Assert.That(l.Ended, Is.False);
            Assert.That(l.TakeSaid(), Has.Some.Contains("Q 한 번 더"));
            Run(l, HeldExtinguisherLogic.ConfirmS + 0.5, _ => At());
            Assert.That(l.RetreatPress(), Is.False, "too late: it asks again");
            Run(l, 0.5, _ => At());
            Assert.That(l.RetreatPress(), Is.True);
            Assert.That(l.EndKind, Is.EqualTo("retreat"));
            var p = Ready(new ExtinguisherSetup { Practice = true });
            p.RetreatPress();
            Assert.That(p.TakeSaid(), Has.Some.Contains("연습을 그만"));
            p.RetreatPress();
            Assert.That(p.EndKind, Is.EqualTo("stopped"));
        }

        [Test]
        public void Hold_LetsGoOfTheHandle_WhileTheControlsAreOff()
        {
            var l = Ready();
            l.Input = Sweep(0); l.Tick(Dt);
            Assert.That((l.Spraying, l.Hitting), Is.EqualTo((true, true)));
            l.Hold();
            Assert.That((l.Spraying, l.Hitting), Is.EqualTo((false, false)));
        }

        [Test]
        public void TheStepMask_ChangesOnlyWithTheSteps()
        {
            var l = new HeldExtinguisherLogic(new ExtinguisherSetup());
            int m0 = l.StepMask;
            l.Input = At(distance: 12, onBase: false); l.Tick(Dt);
            Assert.That(l.StepMask, Is.EqualTo(m0));
            l.Input = At(); l.Tick(Dt);
            Assert.That(l.StepMask, Is.Not.EqualTo(m0), "came close");
            int m1 = l.StepMask;
            l.PullPin(1);
            Assert.That(l.StepMask, Is.Not.EqualTo(m1));
        }

        [Test]
        public void TheRecap_TicksTheOfficialOrder_AndSaysWhatWentWrong()
        {
            Func<string, string> fact = id => "[" + id + "]";
            var good = Ready(new ExtinguisherSetup { Shouted = true });
            Run(good, 12, t => Sweep(t));
            var (title, lines) = good.Recap(fact);
            Assert.That(title, Is.EqualTo("불을 껐다"));
            Assert.That(lines.Take(5).All(x => x.Contains("✓")), "all five steps in order");
            Assert.That(lines, Has.Some.Contains("불이 난 곳(아래)"));
            Assert.That(lines, Has.Some.Contains("출구를 등지고"));
            Assert.That(lines, Has.None.Contains("[fire_shout]"));

            var quiet = Ready();
            Run(quiet, 12, t => Sweep(t, behind: false));
            var (_, ql) = quiet.Recap(fact);
            Assert.That(ql[0], Does.Contain("○"), "no shout");
            Assert.That(ql, Has.Some.Contains("[fire_shout]"));
            Assert.That(ql, Has.Some.Contains("[ext_door]"));

            var lost = Ready(new ExtinguisherSetup { Fire = "large" });
            Run(lost, 30, t => Sweep(t));
            var (lt, ll) = lost.Recap(fact);
            Assert.That(lt, Is.EqualTo("열기에 밀려났다"));
            Assert.That(ll, Has.Some.Contains("[fire_big]"));
            Assert.That(lost.TakeSaid(), Has.Some.Contains("열기와 연기에 밀려났다"), "driven back is said, not silent");

            var gone = new HeldExtinguisherLogic(new ExtinguisherSetup());
            gone.Retreat();
            Assert.That(gone.Recap(fact).title, Is.Null, "a retreat before any powder: the chapter's card says it");
            var practice = Ready(new ExtinguisherSetup { Practice = true });
            Run(practice, 15, t => Sweep(t));
            var (pt, pl) = practice.Recap(fact);
            Assert.That(pt, Is.EqualTo("가상 불을 껐다"));
            Assert.That(pl, Has.Some.Contains("[ext_1]"));
            Assert.That(pl, Has.None.Contains("불이야"), "no alarm step in practice");
        }

        // ---------------------------------------------------------------- the story

        sealed class Disposer : IDisposable
        {
            readonly Action _a; public bool Disposed;
            public Disposer(Action a) { _a = a; }
            public void Dispose() { Disposed = true; _a(); }
        }

        Func<StoryContext, HeldExtinguisherLogic, ExtinguisherScene.Place, IDisposable> _view;
        Func<StoryContext, bool> _available;
        Func<StoryContext, System.Threading.Tasks.Task<bool>> _gauge;
        Func<StoryContext, bool, System.Threading.Tasks.Task<LookFirstLogic.Outcome>> _look;

        [SetUp] public void Keep() { _view = ExtinguisherScene.View; _available = ExtinguisherScene.Available; _gauge = ExtinguisherScene.GaugePart; _look = Endure.LookPart; }
        [TearDown] public void PutBack() { ExtinguisherScene.View = _view; ExtinguisherScene.Available = _available; ExtinguisherScene.GaugePart = _gauge; Endure.LookPart = _look; }

        /// <summary>A stand-in for the Unity view: each frame it aims as the script says (pin pulled at once).</summary>
        static Disposer StandIn(FakeStoryHost host, Func<HeldExtinguisherLogic, double, AimSample> script, List<Disposer> made = null)
        {
            Disposer d = null;
            ExtinguisherScene.Available = _ => true;
            ExtinguisherScene.View = (ctx, logic, place) =>
            {
                double t = 0;
                logic.PullPin(1);
                Action<double> f = dt => { t += dt; logic.Input = script(logic, t); };
                host.Frame += f;
                d = new Disposer(() => host.Frame -= f);
                made?.Add(d);
                return d;
            };
            return d;
        }

        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(7));
            host.G.Chapter = "endure";
            return (host, ctx);
        }

        [Test]
        public void Use3D_OnlyWithAView_AndNotForTheScreenProfilesOrReducedMotion()
        {
            var (host, ctx) = Rig();
            ExtinguisherScene.View = null;
            Assert.That(ExtinguisherScene.Use3D(ctx), Is.False, "no view (the fake host)");
            StandIn(host, (l, t) => At());
            Assert.That(ExtinguisherScene.Use3D(ctx), Is.True);
            foreach (var p in new[] { "lowvision", "mobility" }) { ctx.Settings.Profile = p; Assert.That(ExtinguisherScene.Use3D(ctx), Is.False, p); }
            foreach (var p in new[] { "", "deaf", "wheelchair", "guardian" }) { ctx.Settings.Profile = p; Assert.That(ExtinguisherScene.Use3D(ctx), Is.True, p); }
            ctx.Settings.Profile = ""; ctx.Settings.ReduceMotion = true;
            Assert.That(ExtinguisherScene.Use3D(ctx), Is.False, "reduce motion");
            ctx.Settings.ReduceMotion = false; ctx.Settings.Profile = "wheelchair";
            Assert.That(ExtinguisherScene.Setup(ctx, "guided", "small", ExtinguisherScene.Corridor).Toggle, Is.True, "hold inputs become toggles");
            ExtinguisherScene.Available = _available;
            Assert.That(ExtinguisherScene.Use3D(ctx), Is.False, "a fake engine is not the Unity host");
        }

        [Test]
        public void Run3D_TurnsTheControlsOn_ShowsTheSteps_RecordsTheTry_AndPutsEverythingBack()
        {
            var (host, ctx) = Rig();
            var made = new List<Disposer>();
            StandIn(host, (l, t) => t < 0.5 ? At() : Sweep(t), made);
            host.SetControls(false);
            host.SetView("third");
            var res = host.Run(() => ExtinguisherScene.Run3D(ctx, ExtinguisherScene.Setup(ctx, "guided", "small", ExtinguisherScene.Corridor), ExtinguisherScene.Corridor));
            Assert.That((string)res["result"], Is.EqualTo("out"));
            Assert.That(made.Single().Disposed, Is.True);
            Assert.That(host.ControlsEnabled, Is.False, "controls as they were");
            Assert.That(host.View, Is.EqualTo("third"), "the view as it was");
            Assert.That(host.Log.Any(c => c.Kind == "view" && c.Text == "first"), "first person while holding it");
            Assert.That(host.Objectives.Any(o => o.title == "소화기로 끄기" && o.steps.Count == 6), "guided: the steps on the objective panel");
            Assert.That(host.Objectives.Last().title, Is.Null, "the objective is cleared afterwards");
            Assert.That(ctx.HandsFull, Is.False);
            var ev = host.G.Events.Single(e => e.Type == ExtinguisherScene.UseEvent);
            Assert.That((string)ev.Details["result"], Is.EqualTo("out"));
            Assert.That(host.Toasts.Any(t => t.Contains("불이 꺼졌다")));
            Assert.That(host.Card(c => c.Title == "불을 껐다").Body, Has.Some.Contains("빗자루"), "the recap ticks the official order");
        }

        [Test]
        public void TheCorridorFire_In3D_PutOut_WithTheReportedGauge_PaysOff()
        {
            var (host, ctx) = Rig();
            ctx.Resume = Endure.ExtResume;
            host.G.SetKnow(ExtinguisherCheck.Know, ExtinguisherCheck.Reported);
            GameRules.RecordEvent(host.G, ExtinguisherCheck.Event, new JObject { ["action"] = "report" });
            StandIn(host, (l, t) => t < 0.5 ? At() : Sweep(t));
            host.QueuePress("apartment:extinguisher");
            host.Run(() => Endure.Run(ctx));
            var g = host.G;
            Assert.That(host.Opens.Select(o => o.screen), Has.No.Member("extinguisher").And.No.Member(LookFirstScene.Screen), "3D, and the scene-select start skips the look step");
            Assert.That(g.Skill["extinguisher"].Last().Value<string>("result"), Is.EqualTo("out"));
            Assert.That(host.Level("apartment").State["hallFire"].ToString(), Is.EqualTo("out"));
            Assert.That(g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Does.Contain(ExtinguisherCheck.Prep));
            Assert.That(Ripples.CauseOf(g, ExtinguisherCheck.Prep), Is.Not.Null);
            var report = LearningReport.Build(g);
            Assert.That(report.Rows.Single(r => r.Id == "extBase").Ok, Is.True);
            Assert.That(report.Rows.Single(r => r.Id == "extExit").Ok, Is.True);
            Assert.That(report.Rows.Single(r => r.Id == "extGauge").Ok, Is.True);
        }

        [Test]
        public void TheCorridorFire_2DPath_TellsTheScreenTheExtinguisherIsLow_WhenTheGaugeWasNeverRead()
        {
            var (host, ctx) = Rig();
            ctx.Resume = Endure.ExtResume;
            ExtinguisherScene.View = null;
            host.AnswerOpen("extinguisher", new JObject { ["sequence"] = new JArray("fight", "pin", "aim", "squeeze") });
            host.QueuePresses("apartment:extinguisher", "apartment:hallFire");
            host.TalkPolicy = o => o.Choices.Any(c => c.Id == "retreat") ? "retreat" : o.Choices.FirstOrDefault(c => !c.Disabled)?.Id;
            host.Queue(h => h.MoveTo(5, 0.9, 0), label: "down the stairs");
            host.Run(() => Endure.Run(ctx));
            var opts = host.Opens.First(o => o.screen == "extinguisher").options;
            Assert.That((bool)opts["low"], Is.True);
            Assert.That(host.Toasts.Any(t => t.Contains("압력이 빠진 소화기")), "why it ran dry");
        }

        [Test]
        public void TheGaugeCheck_ReportingGetsItReplaced_LeavingKeepsItLow_EscRecordsNothing()
        {
            var (host, ctx) = Rig();
            host.AnswerOpen(ExtinguisherCheck.Screen, new JObject { ["answer"] = "low", ["action"] = "report", ["needle"] = ExtinguisherCheck.CorridorNeedle });
            host.Run(() => ExtinguisherScene.CheckGauge(ctx));
            var g = host.G;
            Assert.That(ExtinguisherCheck.IsLow(g), Is.False);
            var o = host.Opens.Single().options;
            Assert.That((double)o["needle"], Is.EqualTo(ExtinguisherCheck.CorridorNeedle));
            var facts = FakeStoryHost.LoadFacts();
            foreach (var id in ExtinguisherCheck.FactIds) Assert.That((string)o["facts"][id]["text"], Is.EqualTo(facts.Text(id)), id);
            var ev = g.Events.Single(e => e.Type == ExtinguisherCheck.Event);
            Assert.That(((string)ev.Details["reading"], (bool)ev.Details["ok"], (string)ev.Details["action"]), Is.EqualTo(("low", true, "report")));
            Assert.That(Ripples.CauseOf(g, ExtinguisherCheck.Prep)?.Text, Does.Contain("관리사무소"));

            var (h2, c2) = Rig();
            h2.AnswerOpen(ExtinguisherCheck.Screen, new JObject { ["answer"] = "ok", ["action"] = "leave" });
            h2.Run(() => ExtinguisherScene.CheckGauge(c2));
            Assert.That(ExtinguisherCheck.IsLow(h2.G), Is.True);
            Assert.That(LearningReport.Build(h2.G).Rows.Single(r => r.Id == "extGauge").Ok, Is.False);

            var (h3, c3) = Rig();
            h3.Run(() => ExtinguisherScene.CheckGauge(c3)); // {} = Esc
            Assert.That(h3.G.Events.Any(e => e.Type == ExtinguisherCheck.Event), Is.False);
            Assert.That(ExtinguisherCheck.Checked(h3.G), Is.False);
        }

        [Test]
        public void TheGaugeCheck_Escaped_LeavesThePointOpen_AndAnsweredGoesOnToThePractice()
        {
            var (host, ctx) = Rig();
            ExtinguisherScene.View = null;
            host.G.Chapter = "prep";
            host.QueuePresses("extinguisher", "homePlanner");
            host.Run(async () => { await ctx.Goto("apartment", "home"); await PrepActions.DoEquip(ctx); });
            Assert.That(host.G.Knows("extinguisherLocated"), Is.False, "Esc: nothing decided");
            Assert.That(host.Opens.Select(o => o.screen), Has.No.Member("extinguisher"), "no practice yet");
            Assert.That(host.Toasts, Has.Some.Contains("다시 와서"));

            var (h2, c2) = Rig();
            ExtinguisherScene.View = null;
            h2.G.Chapter = "prep";
            h2.AnswerOpen(ExtinguisherCheck.Screen, new JObject { ["answer"] = "low", ["action"] = "report" });
            h2.QueuePresses("extinguisher", "homePlanner");
            h2.Run(async () => { await c2.Goto("apartment", "home"); await PrepActions.DoEquip(c2); });
            Assert.That(h2.G.Knows("extinguisherLocated"), Is.True);
            Assert.That(ExtinguisherCheck.IsLow(h2.G), Is.False);
            Assert.That(h2.Opens.Select(o => o.screen), Has.Member("extinguisher"), "then the practice (2D here)");
        }

        [Test]
        public void TheShelterFire_In3D_RetreatingAtOnceIsRight_TryingThenRetreatingIsRightOnlyInTheEnd()
        {
            static bool IsLabels(TalkOptions o) => o.Choices != null && o.Choices.Any(c => c.Id == "neighbor") && o.Choices.Any(c => c.Id == "official");
            (FakeStoryHost, StoryContext) Fresh()
            {
                var (h, c) = Rig();
                var g = h.G;
                g.Day = 3; g.Minute = 18 * 60;
                g.Items["water_2l"] = new ItemEntry(6); g.Items["canned"] = new ItemEntry(4); g.Items["rice_instant"] = new ItemEntry(2);
                h.AnswerWhen(IsLabels, "neighbor").AnswerSpeaker("정민", "go");
                h.AnswerSpeaker("대피소 관리자", "precise").AnswerSpeaker("대피소 관리자", "help");
                h.AnswerCard("add", "move");
                return (h, c);
            }
            var (host, ctx) = Fresh();
            StandIn(host, (l, t) => At(distance: 20, onBase: false));
            host.AnswerWhen(o => o.Speaker == "정민" && o.Choices != null && o.Choices.Any(c => c.Id == "fight"), "retreat");
            host.Run(() => Evac.Crossroads(ctx));
            Assert.That(host.Opens.Select(o => o.screen), Has.No.Member("extinguisher"), "3D: no 2D screen");
            Assert.That(host.G.Skill["extinguisher"].Last().Value<string>("result"), Is.EqualTo("retreat-ok"));
            Assert.That(host.G.Events.Any(e => e.Type == ExtinguisherScene.UseEvent), Is.False, "never picked it up");
            Assert.That(host.G.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Does.Contain("judgement"), "retreating at once pays off");

            // picked it up, sprayed a little at the ceiling-high fire, then walked back: retreat-ok, not correct at first
            var (h2, c2) = Fresh();
            StandIn(h2, (l, t) => t < 3 ? At(distance: 3, lateral: Math.Sin(t * 6) * 0.4, squeeze: true) : At(distance: 3 + (t - 3) * 3, onBase: false));
            h2.AnswerWhen(o => o.Speaker == "정민" && o.Choices != null && o.Choices.Any(c => c.Id == "fight"), "fight");
            h2.Run(() => Evac.Crossroads(c2));
            var use = h2.G.Events.Single(e => e.Type == ExtinguisherScene.UseEvent);
            Assert.That(((string)use.Details["end"], (string)use.Details["result"]), Is.EqualTo(("retreat", "retreat-ok")));
            Assert.That(HeldExtinguisherLogic.RetreatOk(use.Details), Is.True);
            Assert.That((string)h2.Level("shelter").State["fire"], Is.EqualTo("none"));
            Assert.That(h2.G.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Has.No.Member("judgement"), "trying to fight it first: not the judgement payoff");
            Assert.That(h2.Card(cd => cd.Title == "물러나 알렸다"), Is.Not.Null, "the recap after a try with powder");
        }
    }
}
