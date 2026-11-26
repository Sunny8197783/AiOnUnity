// CPR rhythm minigame (competition upgrade, 2장 대피소): the pure rules. No UnityEngine, so EditMode tests drive it.
//
// The order is the official one (facts-ext: cpr_check -> cpr_point -> cpr_breath -> cpr_depth_rate; AED: cpr_aed_steps,
// cpr_aed_clear): tap the shoulders and call out, point at one named person for 119 and another for the AED (a vague
// "누가 좀" moves nobody), look at the face and chest for no more than 10 s, then compress on a fixed 110 BPM beat
// (inside 분당 100~120회; the beat never slows, accessibility widens the timing windows instead).
//
// Compression: a press is judged against the nearest beat; how long it is held is the depth (약 5cm = depth 1), and
// letting go well before the next beat is the recoil. Hidden fatigue (Ochoa 1998, cpr_fatigue_why): after a minute of
// one's own compressing the windows narrow, the same depth needs a longer hold and full depth is out of reach, while the
// gauge the player sees keeps using the depth they *feel*: the drop shows only once fatigue is felt (a median 186 s in the
// study). Asking a named neighbour to take over (교대 요청) lets the rescuer recover. The AED comes at a fixed time:
// power, pads, hands off while it analyses, shock, compress again at once. It always ends with the paramedics taking
// over (구급대 인계); there is no other ending.
//
// A prepared run (장면 바로가기, 교사용 모드: the screen's "demo" option; CprScene sets it from StoryContext.DemoRun)
// brings the AED sooner, DemoAedAt instead of AedAt, and the hidden fatigue sooner (DemoFatigueStart, DemoFatigueRamp), so
// a judge's short visit reaches both the AED and the moment the presses get shallow while the gauge stays high; the screen
// says so on the page. The story keeps AedAt, HandoverAt, FatigueStart and FatigueRamp.
//
// Times are seconds on the caller's clock (the screen passes AudioSettings.dspTime-based seconds, unaffected by
// Time.timeScale). Call Tick(t) every frame and Press(t)/Release(t) on the button's edges.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public enum CprPhase { Check, Call, Breath, Compress, AedPower, AedPads, AedAnalyze, AedShock, Handover }

    public enum CprHit { None, Perfect, Good, Off }

    /// <summary>The swap with a named neighbour: asked (hands off while hands change), the neighbour compressing, or none.</summary>
    public enum CprSwap { None, Pending, On }

    /// <summary>One judged compression of the player's (the last one is shown as feedback).</summary>
    public struct CprPress
    {
        public int Beat;
        public double At, Offset, Hold, Depth, FeltDepth, Score, FeltScore;
        public CprHit Hit;
        public bool Recoil;
    }

    /// <summary>One second of the compression phase: the true quality, what the gauge showed, and who was compressing.</summary>
    public sealed class CprSample
    {
        /// <summary>Seconds since the first compression beat.</summary>
        public double T;
        /// <summary>Rolling quality 0..1 (true) and the gauge as shown.</summary>
        public double Quality, Gauge;
        /// <summary>Rolling depth of the presses (1 = 약 5cm), true and as felt; -1 = no press yet.</summary>
        public double Depth, FeltDepth;
        /// <summary>me | swap (a neighbour compressing) | gap (hands changing) | aed</summary>
        public string Who;
    }

    public sealed class CprLogic
    {
        // ---------------------------------------------------------------- numbers (game numbers unless a fact says so)

        /// <summary>The beat: fixed, inside the guideline's 분당 100~120회 (cpr_depth_rate). Never slowed.</summary>
        public const double Bpm = 110;
        public const double Interval = 60.0 / Bpm;
        /// <summary>Beats of count-in before the first judged beat (the dispatcher counts them).</summary>
        public const int LeadInBeats = 8;

        public const double PerfectWindow = 0.07, GoodWindow = 0.14;
        /// <summary>Accessibility: windows grow by this much (never the tempo); capped below half a beat.</summary>
        public const double WideFactor = 1.8, MaxWindow = 0.25;

        /// <summary>Seconds of one's own compressing before the quality starts to drop (Ochoa: from about a minute).</summary>
        public const double FatigueStart = 60;
        /// <summary>Seconds past FatigueStart to full fatigue.</summary>
        public const double FatigueRamp = 60;
        /// <summary>
        /// Full fatigue narrows the windows by this share, caps the depth at 1 - this, and needs this much more hold. The cap
        /// alone (however long the hold) takes a solo rescuer under ShallowBelow at SoloShallowAt, before the AED comes.
        /// </summary>
        public const double FatigueNarrow = 0.3, FatigueDepthCap = 0.4, TiredHold = 0.9;
        /// <summary>Seconds of own compressing before the rescuer feels the fatigue (Ochoa 1998: median 186 s).</summary>
        public const double FeltAfter = 186;
        /// <summary>Rest recovers this many seconds of work per second.</summary>
        public const double Recover = 2;

        /// <summary>Hold (s) for full depth when fresh.</summary>
        public const double BaseHold = 0.16;
        /// <summary>The release must come at least this long before the next beat for the chest to come back up.</summary>
        public const double RecoilMin = 0.14;
        public const double LeanScore = 0.6;
        /// <summary>Depth counted as 약 5cm, and the depth below which the presses are called shallow (about 4 cm).</summary>
        public const double DepthOk = 0.9, ShallowBelow = 0.8;
        /// <summary>Consecutive seconds of shallow rolling depth before ShallowAt is set.</summary>
        public const int ShallowRun = 4;
        public const int Rolling = 8;

        /// <summary>A neighbour or the volunteer compressing (fresh hands).</summary>
        public const double NeighbourScore = 0.9, NeighbourDepth = 0.95;
        /// <summary>Hands off while the hands change, and how long the neighbour keeps going before handing back.</summary>
        public const double SwapGap = 2, SwapStint = 30;

        /// <summary>Seconds after the first beat when the AED arrives, and the earliest handover.</summary>
        public const double AedAt = 100, HandoverAt = 150, AfterShockMin = 20;
        /// <summary>
        /// 장면 바로가기 only (Demo): the AED this many seconds after the first beat, and the earliest handover (the
        /// AfterShockMin after the shock still applies, so the paramedics come about 20 s after it). Game numbers, not facts.
        /// </summary>
        public const double DemoAedAt = 25, DemoHandoverAt = DemoAedAt + AfterShockMin;
        /// <summary>
        /// Prepared runs only (Demo): the hidden fatigue starts after this many seconds of one's own compressing and is full
        /// this many seconds later, so a rescuer alone gets shallow (DemoSoloShallowAt) before the DemoAedAt AED. Game
        /// numbers for the short visit (the page says the time was shortened), not facts; the felt fatigue is unchanged.
        /// </summary>
        public const double DemoFatigueStart = 6, DemoFatigueRamp = 12;
        public const double AnalyzeSeconds = 5;
        /// <summary>An AED step nobody takes in this long is done by the volunteer (no soft lock).</summary>
        public const double AutoStepAfter = 12;
        /// <summary>
        /// The AED steps are not the compression button's next beat: a step press counts only StepLock seconds after the step
        /// began, and only after StepPause seconds without any press or release (pressing on the beat never turns the power on).
        /// </summary>
        public const double StepLock = 1.0, StepPause = 0.8;
        /// <summary>A press still held this long into the analysis is a hand on the patient.</summary>
        public const double AnalyzeGrace = 0.6;
        /// <summary>After this many restarts the volunteer pulls the hands away and the analysis runs to its end (no soft lock).</summary>
        public const int MaxRestarts = 3;
        /// <summary>Compressions back within this many seconds of the shock count as "at once".</summary>
        public const double ResumeGood = 3;
        /// <summary>The breathing check: 10초 이내 (cpr_breath); after BreathAuto the screen moves on by itself.</summary>
        public const double BreathLimit = 10, BreathAuto = 15;
        /// <summary>One-button mode: a press held this long asks the named neighbour to take over.</summary>
        public const double LongHold = 1.0;
        /// <summary>Own compressing before the guided / hint stages remind about the swap (free: never).</summary>
        public const double HintGuided = 60, HintHint = 90;
        public const int MissHintRun = 6;
        /// <summary>Own presses below this are "few" (the depth is not called kept); a swap before this many seconds is not "before tiring".</summary>
        public const int MinOwnPresses = 20;
        public const double SwapMinOwn = 20;
        /// <summary>The rhythm row: share of own beats pressed, and mean true quality while compressing.</summary>
        public const double RhythmCover = 0.75, RhythmQuality = 0.5;
        /// <summary>Compression time when a rescuer alone (no swap) can no longer reach ShallowBelow, however long the hold (a game number).</summary>
        public static double SoloShallowAt => FatigueStart + FatigueRamp * (1 - ShallowBelow) / FatigueDepthCap;
        /// <summary>SoloShallowAt with the prepared runs' fatigue clock (DemoFatigueStart, DemoFatigueRamp).</summary>
        public static double DemoSoloShallowAt => DemoFatigueStart + DemoFatigueRamp * (1 - ShallowBelow) / FatigueDepthCap;
        /// <summary>SoloShallowAt for a run (DemoSoloShallowAt in a prepared run).</summary>
        public static double SoloShallowAtFor(bool demo) => demo ? DemoSoloShallowAt : SoloShallowAt;
        /// <summary>
        /// Few presses of one's own and no swap (nobody pressing, or the breathing check that moved on by itself): the
        /// debrief says only that, not "혼자 계속 눌렀다" or "교대를 요청하지 않았습니다", which would contradict it.
        /// </summary>
        public static bool FewAlone(CprResult r) => r != null && r.FirstSwapAt < 0 && r.Compressions < MinOwnPresses;

        // ---------------------------------------------------------------- options

        public readonly bool Wide, OneButton;
        /// <summary>Drills ladder stage: guided | hint | free.</summary>
        public readonly string Stage;
        /// <summary>A prepared run (the screen's "demo" option): the AED at DemoAedAt, the fatigue on the demo clock. The story: false.</summary>
        public readonly bool Demo;

        public CprLogic(bool wide = false, bool oneButton = false, string stage = "guided", bool demo = false)
        {
            Wide = wide; OneButton = oneButton; Stage = stage ?? "guided"; Demo = demo;
        }

        /// <summary>Seconds after the first beat when the AED arrives in this run (AedAt; DemoAedAt in a demo).</summary>
        public double AedTime => Demo ? DemoAedAt : AedAt;
        /// <summary>The earliest handover in this run (HandoverAt; DemoHandoverAt in a demo).</summary>
        public double HandoverTime => Demo ? DemoHandoverAt : HandoverAt;

        // ---------------------------------------------------------------- state

        public CprPhase Phase { get; private set; } = CprPhase.Check;
        /// <summary>Id of the latest thing to tell the player (CprText.Feedback).</summary>
        public string Feedback { get; private set; }
        /// <summary>Increments with every Feedback change (the screen re-draws on it).</summary>
        public int FeedbackSeq { get; private set; }
        /// <summary>A one-time reminder: "swap" (guided/hint stages) or "press" (guided, after misses); null otherwise.</summary>
        public string Hint { get; private set; }
        public double Now { get; private set; }

        public int CheckMistakes, VagueCalls, SelfCalls;
        public bool Named;
        public double BreathStart = -1, BreathSeconds = -1;
        public bool BreathWrong, BreathLate;

        double _c0 = -1;
        int _next;
        public int Compressions, Misses, Rushed, Perfect, Good, Off, Leans;
        bool _held, _pending, _ignored, _longFired;
        double _pressAt;
        // the raw button in every phase: down, since when, its last edge (press or release), and whether this hold already touched
        bool _down, _touchCounted;
        double _edgeAt = -1, _downAt;
        int _restarts;
        CprPress _cur;
        public CprPress Last { get; private set; }
        public int PressSeq { get; private set; }

        public double Work { get; private set; }

        readonly double[] _q = new double[Rolling], _fq = new double[Rolling], _d = new double[Rolling], _fd = new double[Rolling];
        int _qn, _qi, _dn, _di;
        public double Gauge { get; private set; }

        public CprSwap Swap { get; private set; }
        double _swapFrom, _swapUntil, _backAt = -1;
        public int Swaps, VagueSwaps;
        public double FirstSwapAt = -1, TakeBackGap = -1;

        double _stepAt, _analyzeFrom, _shockAt = -1, _shockCt = -1;
        public bool AedArrived, AedDone;
        public int AedTouches, AedAuto;
        public double ResumeDelay = -1;
        public double EndAt = -1;

        public readonly List<CprSample> Curve = new();
        double _nextSample, _lastT = -1;
        bool _hintShown, _missHintShown;
        int _missRun;

        // ---------------------------------------------------------------- derived

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static double FatigueOf(double work) => FatigueOf(work, false);
        /// <summary>The fatigue after this much own compressing, on the story's clock or (demo) the prepared runs' one.</summary>
        public static double FatigueOf(double work, bool demo) =>
            demo ? Clamp01((work - DemoFatigueStart) / DemoFatigueRamp) : Clamp01((work - FatigueStart) / FatigueRamp);
        public double Fatigue => FatigueOf(Work, Demo);
        /// <summary>The fatigue the rescuer feels: none until FeltAfter seconds of work.</summary>
        public double FeltFatigue => Work >= FeltAfter ? Fatigue : 0;
        public static double FeltFatigueOf(double work) => work >= FeltAfter ? FatigueOf(work) : 0;

        public static double PerfectWindowFor(bool wide, double fatigue) => Math.Min(MaxWindow, PerfectWindow * (wide ? WideFactor : 1)) * (1 - FatigueNarrow * fatigue);
        public static double GoodWindowFor(bool wide, double fatigue) => Math.Min(MaxWindow, GoodWindow * (wide ? WideFactor : 1)) * (1 - FatigueNarrow * fatigue);
        public double PerfectW => PerfectWindowFor(Wide, Fatigue);
        public double GoodW => GoodWindowFor(Wide, Fatigue);

        public static double NeedHold(double fatigue) => BaseHold * (1 + TiredHold * fatigue);
        public static double MaxDepth(double fatigue) => 1 - FatigueDepthCap * fatigue;
        /// <summary>Depth (1 = 약 5cm) a hold of this length gives at this fatigue.</summary>
        public static double DepthOf(double hold, double fatigue) => Math.Max(0, Math.Min(hold / NeedHold(fatigue), MaxDepth(fatigue)));
        /// <summary>One-button mode: the depth is not the player's to set, but fatigue still takes it away.</summary>
        public static double AutoDepth(double fatigue) => MaxDepth(fatigue);
        public static double DepthScore(double depth) => Clamp01((depth - 0.3) / (DepthOk - 0.3));
        public static double TimingScore(CprHit h) => h == CprHit.Perfect ? 1 : h == CprHit.Good ? 0.8 : h == CprHit.Off ? 0.4 : 0;

        /// <summary>Time of judged beat k (k = 0 is the first compression).</summary>
        public double BeatTime(int k) => _c0 + (LeadInBeats + k) * Interval;
        /// <summary>Seconds since the first compression beat (negative during the count-in; 0 before compressions).</summary>
        public double CompressTime(double t) => _c0 < 0 ? 0 : t - BeatTime(0);
        public bool Started => _c0 >= 0;
        /// <summary>The index of the beat nearest to t (for the metronome and the visual beat).</summary>
        public int BeatIndexAt(double t) => _c0 < 0 ? 0 : (int)Math.Round((t - BeatTime(0)) / Interval);

        public double RollingQuality => Mean(_q, _qn);
        public double RollingFelt => Mean(_fq, _qn);
        /// <summary>Mean depth of the recent presses, -1 before any.</summary>
        public double RollingDepth => _dn == 0 ? -1 : Mean(_d, _dn);
        public double RollingFeltDepth => _dn == 0 ? -1 : Mean(_fd, _dn);

        static double Mean(double[] a, int n)
        {
            if (n == 0) return 0;
            double s = 0;
            for (int i = 0; i < n; i++) s += a[i];
            return s / n;
        }

        /// <summary>Who is compressing now: me | neighbour | volunteer | none.</summary>
        public string Compressor => Phase switch
        {
            CprPhase.Compress => Swap == CprSwap.On ? "neighbour" : Swap == CprSwap.Pending ? "none" : "me",
            CprPhase.AedPower or CprPhase.AedPads => "volunteer",
            _ => "none",
        };

        public double SwapLeft => Swap == CprSwap.On ? Math.Max(0, _swapUntil - Now) : Swap == CprSwap.Pending ? SwapStint : 0;
        public double AnalyzeLeft => Phase == CprPhase.AedAnalyze ? Math.Max(0, AnalyzeSeconds - (Now - _analyzeFrom)) : 0;
        public double BreathLeft => Phase == CprPhase.Breath ? Math.Min(BreathLimit, Math.Max(0, BreathLimit - (Now - BreathStart))) : 0;
        public bool Held => _held;

        void Say(string id) { Feedback = id; FeedbackSeq++; }

        // ---------------------------------------------------------------- the steps before compressing

        /// <summary>반응 확인: "tap" (어깨를 두드리며 부르기) is right; anything else is a mistake and stays on the step.</summary>
        public string Check(string choice)
        {
            if (Phase != CprPhase.Check) return null;
            if (choice == "tap") { Phase = CprPhase.Call; Say("checked"); }
            else { CheckMistakes++; Say("check_wrong"); }
            return Feedback;
        }

        /// <summary>도움 요청: "named" (one person for 119, one for the AED) moves on; "vague" and "self" do not.</summary>
        public string Call(string choice, double t)
        {
            if (Phase != CprPhase.Call) return null;
            Now = t;
            switch (choice)
            {
                case "named": Named = true; Phase = CprPhase.Breath; BreathStart = t; Say("call_named"); break;
                case "self": SelfCalls++; Say("call_self"); break;
                default: VagueCalls++; Say("call_vague"); break;
            }
            return Feedback;
        }

        /// <summary>
        /// Low-vision speech: the 10-second clock starts only after the step has been read aloud (about this many seconds), so
        /// screen-reader time is never "late". The 10 seconds stay the goal.
        /// </summary>
        public void DelayBreath(double seconds)
        {
            if (Phase == CprPhase.Breath && seconds > 0) BreathStart = Math.Max(BreathStart, Now + seconds);
        }

        /// <summary>호흡 확인: "none" (숨을 안 쉬거나 비정상) is right; "breathing" is recorded as a slip. Either way compressing starts.</summary>
        public string Breath(string choice, double t)
        {
            if (Phase != CprPhase.Breath) return null;
            BreathSeconds = Math.Max(0, t - BreathStart);
            BreathLate = BreathSeconds > BreathLimit;
            BreathWrong = choice == "breathing";
            Say(BreathWrong ? "breath_wrong" : BreathLate ? "breath_late" : "breath_ok");
            StartCompress(t);
            return Feedback;
        }

        void StartCompress(double t)
        {
            Phase = CprPhase.Compress;
            _c0 = t; _next = 0; _nextSample = 0; _lastT = t; Now = t;
        }

        // ---------------------------------------------------------------- input

        /// <summary>The button went down: a compression, an AED step, or (while it analyses) a touch.</summary>
        public void Press(double t)
        {
            Tick(t);
            bool paused = _edgeAt < 0 || t - _edgeAt >= StepPause;
            _down = true; _downAt = t; _edgeAt = t; _touchCounted = false;
            switch (Phase)
            {
                case CprPhase.AedPower: case CprPhase.AedPads: case CprPhase.AedShock:
                    if (paused && t - _stepAt >= StepLock) AedStep(t, false);
                    else Say(Phase == CprPhase.AedShock ? "shock_wait" : "step_wait");
                    return;
                case CprPhase.AedAnalyze: Touch(t); return;
                case CprPhase.Compress: break;
                default: return;
            }
            if (_held) return;
            _held = true; _pressAt = t; _longFired = false; _ignored = true; _pending = false;
            if (Swap != CprSwap.None) { Say(Swap == CprSwap.On ? "neighbour" : "swap"); return; }
            if (t < BeatTime(0) - GoodW) { Say("wait"); return; }
            int k = Math.Max(0, (int)Math.Round((t - BeatTime(0)) / Interval));
            if (k < _next) { Rushed++; Say("rush"); return; }
            while (_next < k) Resolve(_next++, t);
            _next = k + 1;
            double off = t - BeatTime(k), a = Math.Abs(off);
            var hit = a <= PerfectW ? CprHit.Perfect : a <= GoodW ? CprHit.Good : CprHit.Off;
            _cur = new CprPress { Beat = k, At = t, Offset = off, Hit = hit, Recoil = true };
            _ignored = false; _missRun = 0;
            if (_backAt >= 0) { TakeBackGap = Math.Max(TakeBackGap, t - _backAt); _backAt = -1; }
            if (_shockAt >= 0 && ResumeDelay < 0) ResumeDelay = t - _shockAt;
            if (OneButton) Finish(0, true);
            else _pending = true;
        }

        /// <summary>The button came up: the hold so far is the depth; letting go early enough is the recoil.</summary>
        public void Release(double t)
        {
            Tick(t);
            _down = false; _edgeAt = t; _touchCounted = false;
            if (!_held) return;
            _held = false;
            if (_ignored || !_pending) { _ignored = false; return; }
            Finish(t - _pressAt, t <= BeatTime(_cur.Beat + 1) - RecoilMin);
        }

        void Finish(double hold, bool recoil)
        {
            _pending = false;
            double f = Fatigue, ff = FeltFatigue;
            double d = OneButton ? AutoDepth(f) : DepthOf(hold, f);
            double fd = OneButton ? AutoDepth(ff) : DepthOf(hold, ff);
            double ts = TimingScore(_cur.Hit), rs = recoil ? 1 : LeanScore;
            _cur.Hold = hold; _cur.Recoil = recoil; _cur.Depth = d; _cur.FeltDepth = fd;
            _cur.Score = ts * DepthScore(d) * rs; _cur.FeltScore = ts * DepthScore(fd) * rs;
            PushQ(_cur.Score, _cur.FeltScore);
            PushD(d, fd);
            Compressions++;
            if (_cur.Hit == CprHit.Perfect) Perfect++; else if (_cur.Hit == CprHit.Good) Good++; else Off++;
            if (!recoil) Leans++;
            Last = _cur; PressSeq++;
            // What the player is told is what they can feel: the felt depth, never the hidden one.
            Say(!recoil ? "lean" : _cur.Hit == CprHit.Off ? (_cur.Offset < 0 ? "early" : "late")
                : fd < DepthOk - 0.05 ? "deeper" : _cur.Hit == CprHit.Good ? (_cur.Offset < 0 ? "bit_early" : "bit_late") : "good");
        }

        /// <summary>교대 요청. named = a person pointed at by name; a vague call moves nobody.</summary>
        public string RequestSwap(double t, bool named)
        {
            Tick(t);
            if (Phase != CprPhase.Compress || !Started) return null;
            if (!named) { VagueSwaps++; Say("swap_vague"); return Feedback; }
            if (Swap != CprSwap.None || t < BeatTime(0)) return null;
            if (_pending) Finish(t - _pressAt, true);
            _ignored = true;
            Swaps++;
            if (FirstSwapAt < 0) FirstSwapAt = CompressTime(t);
            Swap = CprSwap.Pending; _swapFrom = t + SwapGap; _swapUntil = _swapFrom + SwapStint; _backAt = -1;
            Say("swap");
            return Feedback;
        }

        /// <summary>Take the compressions back before the neighbour asks (allowed once they have started).</summary>
        public bool TakeBack(double t)
        {
            Tick(t);
            if (Phase != CprPhase.Compress || Swap != CprSwap.On) return false;
            Swap = CprSwap.None; _backAt = t;
            Say("take_back");
            return true;
        }

        /// <summary>
        /// 장면 바로가기 only (the screen offers it after the shock): the volunteer and then the paramedics take it from here, so
        /// the debrief comes sooner. The story always plays to the handover.
        /// </summary>
        public bool SkipToHandover(double t)
        {
            Tick(t);
            if (Phase != CprPhase.Compress || !AedDone) return false;
            if (_pending) Finish(t - _pressAt, true);
            Phase = CprPhase.Handover; EndAt = t; Swap = CprSwap.None;
            Say("handover");
            return true;
        }

        /// <summary>The on-screen AED button (or Enter): a deliberate press, so only the step's short lock applies.</summary>
        public bool AedButton(double t)
        {
            Tick(t);
            if (!(Phase is CprPhase.AedPower or CprPhase.AedPads or CprPhase.AedShock) || t - _stepAt < StepLock) return false;
            AedStep(t, false);
            return true;
        }

        // A hand on the patient while the AED analyses: the analysis starts over, until the volunteer pulls the hands away.
        void Touch(double t)
        {
            _touchCounted = true;
            AedTouches++;
            if (_restarts < MaxRestarts) { _restarts++; _analyzeFrom = t; Say("aed_touch"); }
            else Say("aed_pulled");
        }

        /// <summary>The next AED step (power -> pads -> [analysis] -> shock). auto: the volunteer did it.</summary>
        public void AedStep(double t, bool auto)
        {
            if (auto) AedAuto++;
            switch (Phase)
            {
                case CprPhase.AedPower: Phase = CprPhase.AedPads; _stepAt = t; Say(auto ? "aed_auto_power" : "aed_pads"); break;
                case CprPhase.AedPads: Phase = CprPhase.AedAnalyze; _analyzeFrom = t; _restarts = 0; Say(auto ? "aed_auto_pads" : "aed_analyze"); break;
                case CprPhase.AedShock:
                    Phase = CprPhase.Compress; _shockAt = t; _shockCt = CompressTime(t); AedDone = true;
                    Say(auto ? "aed_auto_shock" : "aed_resume");
                    break;
            }
        }

        // ---------------------------------------------------------------- time

        public void Tick(double t)
        {
            double dt = _lastT < 0 ? 0 : Math.Max(0, t - _lastT);
            if (t > _lastT) _lastT = t;
            Now = Math.Max(Now, t);
            if (Phase == CprPhase.Breath && t - BreathStart >= BreathAuto)
            {
                BreathSeconds = t - BreathStart; BreathLate = true;
                Say("breath_timeout");
                StartCompress(t);
                return;
            }
            if (!Started || Phase == CprPhase.Handover) return;

            bool me = Phase == CprPhase.Compress && Swap == CprSwap.None && t >= BeatTime(0);
            Work = me ? Work + dt : Math.Max(0, Work - dt * Recover);

            if (Swap == CprSwap.Pending && t >= _swapFrom) { Swap = CprSwap.On; Say("neighbour"); }
            if (Swap == CprSwap.On && t >= _swapUntil) { Swap = CprSwap.None; _backAt = t; Say("take_back"); }

            // Still pressing when the chest should be coming back up: leaning.
            if (_pending && _held && t > BeatTime(_cur.Beat + 1) - RecoilMin) { Finish(t - _pressAt, false); _ignored = true; }
            if (OneButton && _held && !_longFired && t - _pressAt >= LongHold)
            {
                _longFired = true;
                if (Phase == CprPhase.Compress && Swap == CprSwap.None) RequestSwap(t, true);
            }

            while (BeatTime(_next) + Interval * 0.5 < t) Resolve(_next++, t);

            if (Phase == CprPhase.Compress && !AedArrived && CompressTime(t) >= AedTime)
            {
                AedArrived = true;
                if (_pending) Finish(t - _pressAt, true);
                _ignored = _held;
                Swap = CprSwap.None; _backAt = -1;
                Phase = CprPhase.AedPower; _stepAt = t;
                Say("aed_arrive");
            }
            if ((Phase == CprPhase.AedPower || Phase == CprPhase.AedPads || Phase == CprPhase.AedShock) && t - _stepAt >= AutoStepAfter) AedStep(t, true);
            // still holding (the pads press, or a hold carried in): a hand on the patient, once per hold
            if (Phase == CprPhase.AedAnalyze && _down && !_touchCounted && t - _downAt >= AnalyzeGrace) Touch(t);
            if (Phase == CprPhase.AedAnalyze && t - _analyzeFrom >= AnalyzeSeconds) { Phase = CprPhase.AedShock; _stepAt = t; Say("aed_shock_ready"); }

            if (Phase == CprPhase.Compress && AedDone && CompressTime(t) >= Math.Max(HandoverTime, _shockCt + AfterShockMin))
            {
                if (_pending) Finish(t - _pressAt, true);
                Phase = CprPhase.Handover; EndAt = t; Swap = CprSwap.None;
                Say("handover");
            }

            if (Stage != "free" && !_hintShown && Phase == CprPhase.Compress && Swaps == 0 && Work >= (Stage == "guided" ? HintGuided : HintHint))
            {
                _hintShown = true; Hint = "swap";
            }
            if (Stage == "guided" && !_missHintShown && _missRun >= MissHintRun) { _missHintShown = true; Hint = "press"; }

            Gauge += (RollingFelt - Gauge) * (1 - Math.Exp(-dt * 2));
            double ct = CompressTime(t);
            while (ct >= _nextSample && Phase != CprPhase.Handover)
            {
                Curve.Add(new CprSample { T = _nextSample, Quality = RollingQuality, Gauge = Gauge, Depth = RollingDepth, FeltDepth = RollingFeltDepth, Who = WhoNow() });
                _nextSample += 1;
            }
        }

        /// <summary>Clears the hint once the screen has shown it.</summary>
        public void HintShown() => Hint = null;

        string WhoNow() => Phase switch
        {
            CprPhase.Compress => Swap == CprSwap.On ? "swap" : Swap == CprSwap.Pending ? "gap" : "me",
            _ => "aed",
        };

        // A beat nobody pressed: the neighbour's or volunteer's compression, hands changing, hands off for the AED, or a miss.
        void Resolve(int k, double t)
        {
            double bt = BeatTime(k);
            switch (Phase)
            {
                case CprPhase.Compress:
                    if (Swap == CprSwap.On && bt >= _swapFrom) PushQ(NeighbourScore, NeighbourScore);
                    else if (Swap != CprSwap.None) PushQ(0, 0); // hands changing: no compression this beat
                    else
                    {
                        Misses++; _missRun++;
                        PushQ(0, 0);
                        if (t - bt < Interval * 1.5) Say("miss");
                    }
                    break;
                case CprPhase.AedPower: case CprPhase.AedPads:
                    PushQ(NeighbourScore, NeighbourScore);
                    break;
                // analysing and shocking: hands off, as they should be (no quality change)
            }
        }

        void PushQ(double q, double fq)
        {
            _q[_qi] = q; _fq[_qi] = fq;
            _qi = (_qi + 1) % Rolling;
            if (_qn < Rolling) _qn++;
        }

        void PushD(double d, double fd)
        {
            _d[_di] = d; _fd[_di] = fd;
            _di = (_di + 1) % Rolling;
            if (_dn < Rolling) _dn++;
        }

        // ---------------------------------------------------------------- the outcome

        /// <summary>First second of ShallowRun consecutive own-compressing seconds whose depth was shallow; -1 if never.</summary>
        public static double ShallowAt(IReadOnlyList<CprSample> curve)
        {
            int run = 0;
            for (int i = 0; i < curve.Count; i++)
            {
                var s = curve[i];
                if (s.Who == "me" && s.Depth >= 0 && s.Depth < ShallowBelow) { if (++run >= ShallowRun) return curve[i - ShallowRun + 1].T; }
                else run = 0;
            }
            return -1;
        }

        public CprResult Result()
        {
            var shallow = ShallowAt(Curve);
            var mine = Curve.Where(s => s.Who == "me").ToList();
            return new CprResult
            {
                Played = true, Stage = Stage, Wide = Wide, OneButton = OneButton, Demo = Demo,
                CheckMistakes = CheckMistakes, VagueCalls = VagueCalls, SelfCalls = SelfCalls, Named = Named,
                BreathSeconds = BreathSeconds, BreathWrong = BreathWrong,
                Compressions = Compressions, Misses = Misses, Rushed = Rushed, Leans = Leans,
                Quality = mine.Count == 0 ? 0 : mine.Average(s => s.Quality),
                ShallowAt = shallow,
                GaugeAtShallow = shallow < 0 ? -1 : Curve.First(s => s.T == shallow).Gauge,
                FirstSwapAt = FirstSwapAt, Swaps = Swaps, VagueSwaps = VagueSwaps, TakeBackGap = TakeBackGap,
                AedTouches = AedTouches, AedAuto = AedAuto, ResumeDelay = ResumeDelay,
                Seconds = EndAt >= 0 ? CompressTime(EndAt) : CompressTime(Now),
                Curve = Curve.ToList(),
            };
        }

        // ---------------------------------------------------------------- evaluation

        /// <summary>The run's judged rows (each points at a facts-ext id) and the ladder result ("relay" | "solo" | "skipped").</summary>
        public static CprEval Evaluate(CprResult r)
        {
            var e = new CprEval();
            if (r == null || !r.Played) { e.Result = "skipped"; return e; }
            e.Result = r.Swaps > 0 ? "relay" : "solo";
            void Row(string id, bool ok, string yes, string no, string fact) => e.Rows.Add(new CprRow { Id = id, Ok = ok, Label = ok ? yes : no, Fact = fact });

            bool cleanCall = r.Named && r.VagueCalls == 0 && r.SelfCalls == 0;
            Row("cprPoint", cleanCall, "한 사람씩 지목해 119 신고와 AED를 부탁했다",
                r.VagueCalls > 0 ? "처음에 \"누가 좀\" 하고 막연하게 불러 아무도 움직이지 않았다" : "곁을 떠나 직접 신고하려 했다", "cpr_point");
            bool breathOk = r.BreathSeconds >= 0 && r.BreathSeconds <= BreathLimit && !r.BreathWrong;
            Row("cprBreath", breathOk, "10초 안에 호흡을 확인했다",
                r.BreathWrong ? "헐떡이는 숨을 정상 호흡으로 보았다" : "호흡 확인에 10초를 넘겼다", r.BreathWrong ? "cpr_gasp" : "cpr_breath");
            int own = r.Compressions + r.Misses;
            double cover = own == 0 ? 0 : r.Compressions / (double)own;
            bool rhythmOk = r.Compressions >= MinOwnPresses && cover >= RhythmCover && r.Quality >= RhythmQuality;
            Row("cprRhythm", rhythmOk, "박자에 맞춰 끊기지 않게 눌렀다",
                r.Compressions < MinOwnPresses ? "직접 누른 압박이 적었다" : cover < RhythmCover ? "박자를 자주 놓쳐 압박이 끊겼다" : "박자·깊이·이완이 고르지 않았다", "cpr_depth_rate");
            bool swapEarly = r.FirstSwapAt >= SwapMinOwn && (r.ShallowAt < 0 || r.FirstSwapAt <= r.ShallowAt + 10);
            // few own presses and no swap: "혼자 계속 눌렀다" would contradict "직접 누른 압박이 적었다", so the rhythm row says it alone
            if (!FewAlone(r)) Row("cprSwap", swapEarly, "지치기 전에 이웃을 지목해 교대를 요청했다",
                r.FirstSwapAt < 0 ? "교대를 요청하지 않고 혼자 계속 눌렀다" : r.FirstSwapAt < SwapMinOwn ? "거의 누르지 않고 바로 교대를 넘겼다" : "압박이 얕아지고 한참 뒤에야 교대를 요청했다", "cpr_fatigue");
            bool aedOk = r.AedTouches == 0 && r.ResumeDelay >= 0 && r.ResumeDelay <= ResumeGood;
            Row("cprAed", aedOk, "분석 중에는 손을 떼고, 충격 뒤 바로 압박을 다시 시작했다",
                r.AedTouches > 0 ? "AED가 분석하는 동안 환자에게 손이 닿았다" : "충격 뒤 압박을 다시 시작하기까지 오래 걸렸다", "cpr_aed_clear");
            return e;
        }

        /// <summary>"1분 12초" (or "45초").</summary>
        public static string Clock(double seconds)
        {
            int s = Math.Max(0, (int)Math.Round(seconds));
            return s >= 60 ? $"{s / 60}분 {s % 60}초" : $"{s}초";
        }

        /// <summary>The debrief's lines: when it got shallow and what the gauge showed then, the swap, the AED.</summary>
        public static List<string> DebriefLines(CprResult r)
        {
            var lines = new List<string>();
            if (r == null || !r.Played) return lines;
            if (r.ShallowAt >= 0)
            {
                int g = (int)Math.Round(Clamp01(r.GaugeAtShallow) * 100);
                lines.Add($"압박 {Clock(r.ShallowAt)}부터 깊이가 약 5cm에 못 미쳤습니다. 그때 게이지는 {g}%를 보여 주었습니다{(r.GaugeAtShallow >= 0.7 ? " — 괜찮다고 느꼈지만 실제로는 얕아지고 있었습니다." : ".")}");
            }
            else if (FewAlone(r))
            {
                // few presses and no swap (as the rows say): not "혼자 계속 눌렀다면" or "교대를 요청하지 않았습니다" next to it
                lines.Add("직접 누른 압박이 적었습니다.");
            }
            else
            {
                lines.Add(r.Compressions < MinOwnPresses ? "직접 누른 압박이 적었습니다." : "직접 누르는 동안 압박 깊이가 유지됐습니다.");
                // the lesson the run did not show: alone, it gets shallow long before it feels so
                lines.Add($"혼자 계속 눌렀다면 압박 {Clock(SoloShallowAtFor(r.Demo))}쯤부터 얕아졌겠지만, 게이지는 그대로 높게 보였을 것입니다.");
            }
            if (r.FirstSwapAt >= 0) lines.Add($"압박 {Clock(r.FirstSwapAt)}에 이웃을 지목해 교대를 요청했습니다{(r.Swaps > 1 ? $" (모두 {r.Swaps}번)" : "")}.");
            else if (!FewAlone(r)) lines.Add("교대를 요청하지 않았습니다. 지친 느낌은 질이 떨어지고 한참 뒤에야 옵니다.");
            if (r.AedTouches > 0) lines.Add($"AED 분석 중에 손이 {r.AedTouches}번 닿아 분석을 다시 했습니다.");
            if (r.ResumeDelay >= 0) lines.Add($"충격 뒤 {r.ResumeDelay.ToString("0.0", CultureInfo.InvariantCulture)}초 만에 압박을 다시 시작했습니다.");
            if (r.Demo) lines.Add(CprText.DemoDebriefNote); // the times above are the demo's, not the fact's beside them
            return lines;
        }
    }

    public sealed class CprRow
    {
        public string Id, Label, Fact;
        public bool Ok;
    }

    public sealed class CprEval
    {
        public readonly List<CprRow> Rows = new();
        /// <summary>The Drills ladder result: relay (asked for a swap) | solo | skipped.</summary>
        public string Result;
    }

    /// <summary>What the screen hands back (and, without the curve, what the story records in the "cpr" event).</summary>
    public sealed class CprResult
    {
        public bool Played, Wide, OneButton, Named, BreathWrong;
        /// <summary>A prepared run's CPR (the demo AED and fatigue clock): the debrief's "alone" time is the demo one.</summary>
        public bool Demo;
        public string Stage;
        public int CheckMistakes, VagueCalls, SelfCalls, Compressions, Misses, Rushed, Leans, Swaps, VagueSwaps, AedTouches, AedAuto;
        public double BreathSeconds = -1, Quality, ShallowAt = -1, GaugeAtShallow = -1, FirstSwapAt = -1, TakeBackGap = -1, ResumeDelay = -1, Seconds;
        public List<CprSample> Curve = new();

        static double R(double v) => Math.Round(v, 3);

        /// <summary>The summary (no curve unless asked): small enough for the save's event list.</summary>
        public JObject ToJson(bool curve = false)
        {
            var o = new JObject
            {
                ["played"] = Played, ["stage"] = Stage, ["wide"] = Wide, ["oneButton"] = OneButton,
                ["checkMistakes"] = CheckMistakes, ["vague"] = VagueCalls, ["self"] = SelfCalls, ["named"] = Named,
                ["breathSec"] = R(BreathSeconds), ["breathWrong"] = BreathWrong,
                ["compressions"] = Compressions, ["misses"] = Misses, ["rushed"] = Rushed, ["leans"] = Leans,
                ["quality"] = R(Quality), ["shallowAt"] = R(ShallowAt), ["gaugeAtShallow"] = R(GaugeAtShallow),
                ["firstSwapAt"] = R(FirstSwapAt), ["swaps"] = Swaps, ["vagueSwaps"] = VagueSwaps, ["takeBackGap"] = R(TakeBackGap),
                ["aedTouches"] = AedTouches, ["aedAuto"] = AedAuto, ["resumeDelay"] = R(ResumeDelay), ["seconds"] = R(Seconds),
            };
            if (Demo) o["demo"] = true; // only a prepared run's: the story's event is as before
            if (curve)
                o["curve"] = new JObject
                {
                    ["t"] = new JArray(Curve.Select(s => (object)R(s.T))), ["q"] = new JArray(Curve.Select(s => (object)R(s.Quality))),
                    ["g"] = new JArray(Curve.Select(s => (object)R(s.Gauge))), ["d"] = new JArray(Curve.Select(s => (object)R(s.Depth))),
                    ["fd"] = new JArray(Curve.Select(s => (object)R(s.FeltDepth))), ["who"] = new JArray(Curve.Select(s => (object)s.Who)),
                };
            return o;
        }

        /// <summary>Reads a result or an event's details; anything missing reads as "not played".</summary>
        public static CprResult Read(JToken t)
        {
            var r = new CprResult();
            if (t is not JObject o) return r;
            static bool B(JToken v) => v != null && v.Type == JTokenType.Boolean && (bool)v;
            static int I(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (int)(double)v : 0;
            static double D(JToken v, double def) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (double)v : def;
            r.Played = B(o["played"]); r.Demo = B(o["demo"]); r.Wide = B(o["wide"]); r.OneButton = B(o["oneButton"]); r.Named = B(o["named"]); r.BreathWrong = B(o["breathWrong"]);
            r.Stage = o["stage"]?.Type == JTokenType.String ? (string)o["stage"] : null;
            r.CheckMistakes = I(o["checkMistakes"]); r.VagueCalls = I(o["vague"]); r.SelfCalls = I(o["self"]);
            r.Compressions = I(o["compressions"]); r.Misses = I(o["misses"]); r.Rushed = I(o["rushed"]); r.Leans = I(o["leans"]);
            r.Swaps = I(o["swaps"]); r.VagueSwaps = I(o["vagueSwaps"]); r.AedTouches = I(o["aedTouches"]); r.AedAuto = I(o["aedAuto"]);
            r.BreathSeconds = D(o["breathSec"], -1); r.Quality = D(o["quality"], 0); r.ShallowAt = D(o["shallowAt"], -1);
            r.GaugeAtShallow = D(o["gaugeAtShallow"], -1); r.FirstSwapAt = D(o["firstSwapAt"], -1); r.TakeBackGap = D(o["takeBackGap"], -1);
            r.ResumeDelay = D(o["resumeDelay"], -1); r.Seconds = D(o["seconds"], 0);
            if (o["curve"] is JObject c && c["t"] is JArray ts)
            {
                JArray A(string k) => c[k] as JArray ?? new JArray();
                var q = A("q"); var g = A("g"); var d = A("d"); var fd = A("fd"); var who = A("who");
                for (int i = 0; i < ts.Count; i++)
                    r.Curve.Add(new CprSample
                    {
                        T = D(ts[i], 0), Quality = i < q.Count ? D(q[i], 0) : 0, Gauge = i < g.Count ? D(g[i], 0) : 0,
                        Depth = i < d.Count ? D(d[i], -1) : -1, FeltDepth = i < fd.Count ? D(fd[i], -1) : -1,
                        Who = i < who.Count && who[i].Type == JTokenType.String ? (string)who[i] : "me",
                    });
            }
            return r;
        }
    }
}
