// 3D 소화기 (competition upgrade): the rules of the first-person extinguisher, pure (no Unity types) so the EditMode tests
// read them. The scene (Story/HeldExtinguisher.cs) feeds one AimSample per frame: where the nozzle points relative to
// the fire (its base, the flames above it, or neither), how far the fire is, whether the exit is behind 정민, and what
// the hands do (drag the pin, squeeze the handle, shout, retreat). The story (Story/ExtinguisherScene.cs) ticks it.
//
// The official order (행정안전부 안전배움터, facts ext_1..ext_3; 소방청 ext_door, ext_cover): carry it to the fire, pull
// the pin, stand with the door behind you and point the hose at the fire, squeeze the handle and sweep like a broom so
// the powder covers the fire. The sequence it records is the one Drills.EvalExtinguisher already scores (fight, shout,
// pin, aim, squeeze, sweep, retreat), so the ladder, the debrief and the deeds read a 3D try exactly like a 2D one.
//
// Game numbers, not safety facts: the powder lasts FullCharge seconds ("약 10초 남짓, 제품 표시를 확인" is the only
// wording the screens use), a pressure-lost extinguisher (1장 gauge never checked) LowCharge seconds at LowPower: a good
// sweep on the base still just puts a small fire out with it, a careless one does not.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>What the 3D scene needs to know before it starts (the story's option object, as JSON for the 2D fallback too).</summary>
    public sealed class ExtinguisherSetup
    {
        /// <summary>guided | hint | free (Drills.LadderStage).</summary>
        public string Mode = "guided";
        /// <summary>small | large: the fire 정민 saw when choosing to fight (what the try is judged on).</summary>
        public string Fire = "small";
        public bool Practice;
        /// <summary>The extinguisher lost its pressure (1장 gauge never checked or left as it was).</summary>
        public bool Low;
        /// <summary>"불이야!" was already called (the 304 knock or the corridor talk): the sequence starts with it.</summary>
        public bool Shouted;
        /// <summary>Hold inputs are toggles (mobility, wheelchair, guardian).</summary>
        public bool Toggle;
        /// <summary>Metres from the fire beyond which walking away (after having come close) counts as retreating.</summary>
        public double RetreatDistance = 7;
        /// <summary>The way out the lines name (계단 in the apartment, 출입구 in the shelter).</summary>
        public string ExitWord = "계단";

        public JObject ToJson() => new JObject
        {
            ["mode"] = Mode, ["fire"] = Fire, ["practice"] = Practice, ["low"] = Low, ["shouted"] = Shouted, ["toggle"] = Toggle, ["retreatDistance"] = RetreatDistance, ["exitWord"] = ExitWord,
        };

        public static ExtinguisherSetup Read(JObject o)
        {
            var s = new ExtinguisherSetup();
            if (o == null) return s;
            if (o["mode"]?.Type == JTokenType.String) s.Mode = (string)o["mode"];
            if (o["fire"]?.Type == JTokenType.String && (string)o["fire"] == "large") s.Fire = "large";
            s.Practice = o["practice"]?.Type == JTokenType.Boolean && (bool)o["practice"];
            s.Low = o["low"]?.Type == JTokenType.Boolean && (bool)o["low"];
            s.Shouted = o["shouted"]?.Type == JTokenType.Boolean && (bool)o["shouted"];
            s.Toggle = o["toggle"]?.Type == JTokenType.Boolean && (bool)o["toggle"];
            if (o["exitWord"]?.Type == JTokenType.String) s.ExitWord = (string)o["exitWord"];
            if (o["retreatDistance"] != null && (o["retreatDistance"].Type == JTokenType.Float || o["retreatDistance"].Type == JTokenType.Integer)) s.RetreatDistance = (double)o["retreatDistance"];
            return s;
        }
    }

    /// <summary>One frame of what the scene sees and what the hands do.</summary>
    public struct AimSample
    {
        /// <summary>The scene has measured this frame (the logic waits for the first measured frame).</summary>
        public bool Seen;
        /// <summary>The nozzle points at the burning base (the boxes, or the floor just in front of them).</summary>
        public bool OnBase;
        /// <summary>The nozzle points at the flames above the base.</summary>
        public bool OnFlame;
        /// <summary>Metres across the fire (signed, left negative) where the nozzle points: the sweep reads its turns.</summary>
        public double Lateral;
        /// <summary>Metres from 정민 to the fire (horizontal).</summary>
        public double Distance;
        /// <summary>The exit (stairs, the door) is behind 정민.</summary>
        public bool ExitBehind;
        /// <summary>The handle is squeezed this frame (held, or toggled on).</summary>
        public bool Squeeze;
        /// <summary>The player left the floor of the fire (the stairs): retreating.</summary>
        public bool Away;
    }

    /// <summary>A geometric aim test, original or Unity space alike (y up).</summary>
    public readonly struct AimHit
    {
        public readonly bool OnBase, OnFlame;
        public readonly double Lateral, Height;
        public AimHit(bool onBase, bool onFlame, double lateral, double height) { OnBase = onBase; OnFlame = onFlame; Lateral = lateral; Height = height; }
    }

    public sealed class HeldExtinguisherLogic
    {
        // ---------- tuning (game numbers) ----------
        /// <summary>Seconds of powder in a full extinguisher (the screens say only "약 10초 남짓 · 제품 표시 확인").</summary>
        public const double FullCharge = 10.5;
        /// <summary>Seconds of powder, and the spray's strength, of one that lost its pressure.</summary>
        public const double LowCharge = 7.5, LowPower = 0.8;
        /// <summary>How far the powder reaches (m), full and low.</summary>
        public const double Reach = 4.2, LowReach = 2.6;
        /// <summary>Closer than this the heat is on 정민's face.</summary>
        public const double TooClose = 1.0;
        /// <summary>Fire level per second a sweeping spray on the base takes off a small fire (power 1); a large one barely moves.</summary>
        public const double Knock = 0.085, LargeKnock = 0.012;
        /// <summary>A spray held on one spot does this share of a sweep; on the flames above the base, this share.</summary>
        public const double StillShare = 0.35, FlameShare = 0.2;
        /// <summary>Metres of travel across the base before a turn counts as a stroke; a stroke keeps the sweep "on" this long.</summary>
        public const double StrokeSwing = 0.25, StrokeKeep = 1.1;
        /// <summary>Seconds aimed at the fire (pin out, in reach) before the aim step counts.</summary>
        public const double AimHold = 0.3;
        /// <summary>Seconds near a fire that is clearly lost (grown, or the powder gone) before it counts as fixating.</summary>
        public const double FixateAfter = 5;
        /// <summary>Seconds next to a large fire before its heat and smoke drive 정민 back (overwhelmed).</summary>
        public const double HeatS = 9;
        /// <summary>Smoke added (0..1) per second while spraying with the exit in front or to the side; its cap.</summary>
        public const double SmokeRate = 0.06, SmokeCap = 0.35;
        /// <summary>Seconds a hint line stays quiet after it was said.</summary>
        public const double SayEvery = 3.5;
        /// <summary>Seconds within which a second Q confirms stepping back (one press only asks).</summary>
        public const double ConfirmS = 1.6;

        public ExtinguisherSetup Setup { get; }
        public string Mode => Setup.Mode;
        public bool Practice => Setup.Practice;
        public bool Low => Setup.Low && !Setup.Practice;
        /// <summary>The fire as it was when 정민 chose to fight it (EvalExtinguisher's fire).</summary>
        public string Judged { get; }
        /// <summary>The fire now: large from the start, or grown.</summary>
        public bool Large { get; private set; }
        public bool Grown { get; private set; }
        public double GrowPerS { get; }

        // ---------- state ----------
        public double FireLv { get; private set; }
        public double Danger { get; private set; }
        public double Heat { get; private set; }
        public double Capacity { get; }
        public double Powder { get; private set; }
        /// <summary>Powder left as a share of a full extinguisher's (a low one starts short of full).</summary>
        public double PowderShare => Math.Max(0, Powder / FullCharge);
        public double PinProgress { get; private set; }
        public bool PinOut { get; private set; }
        public bool Spraying { get; private set; }
        /// <summary>The spray reaches the fire's base this frame (the scene puffs the powder there).</summary>
        public bool Hitting { get; private set; }
        public bool Approached { get; private set; }
        public double SmokeExtra { get; private set; }
        public bool Ended { get; private set; }
        /// <summary>out | retreat | overwhelmed | stopped (practice)</summary>
        public string EndKind { get; private set; }
        public bool Fixated { get; private set; }
        public AimSample Input;

        // ---------- what the debrief reads ----------
        public double SprayTime { get; private set; }
        public double BaseTime { get; private set; }
        public double FlameTime { get; private set; }
        public double MissTime { get; private set; }
        public double BehindTime { get; private set; }
        public double FacingTime { get; private set; }
        public int Strokes { get; private set; }
        public double LostTime { get; private set; }

        readonly List<string> _seq = new();
        readonly List<string> _said = new();
        readonly Dictionary<string, double> _quiet = new();
        double _aimFor, _swing, _sinceStroke = 99, _lastLateral = double.NaN, _clock, _askAt = -99;
        int _swingDir;

        public IReadOnlyList<string> Sequence => _seq;

        public HeldExtinguisherLogic(ExtinguisherSetup setup)
        {
            Setup = setup ?? new ExtinguisherSetup();
            Judged = Setup.Fire == "large" ? "large" : "small";
            Large = Judged == "large" && !Setup.Practice;
            FireLv = Setup.Practice ? 0.3 : Large ? 0.88 : 0.36;
            double grow = Setup.Mode != null && ExtGrow.TryGetValue(Setup.Mode, out var gs) ? gs : 40;
            GrowPerS = Setup.Practice ? 0 : 1 / grow;
            Capacity = Low ? LowCharge : FullCharge;
            Powder = Capacity;
            _seq.Add("fight");
            if (Setup.Shouted && !Setup.Practice) _seq.Add("shout");
        }

        /// <summary>Seconds until a small fire spreads, per ladder stage (the 2D screen's own table).</summary>
        static readonly IReadOnlyDictionary<string, double> ExtGrow = new Dictionary<string, double> { ["guided"] = 70, ["hint"] = 45, ["free"] = 32 };

        public bool Done(string step) => _seq.Contains(step);
        public double Power => Low ? LowPower : 1;
        public double ReachNow => Low ? LowReach : Reach;

        // ---------- the hands ----------

        /// <summary>Drag on the pin (0..1 of a full pull per call). True on the frame it comes out.</summary>
        public bool PullPin(double amount)
        {
            if (Ended || PinOut || amount <= 0) return false;
            PinProgress = Math.Min(1, PinProgress + amount);
            if (PinProgress < 1) return false;
            PinOut = true;
            _seq.Add("pin");
            Say("pin", "안전핀을 뽑았다. 이제 노즐을 불이 난 곳으로.", force: true);
            return true;
        }

        /// <summary>The pull stopped before the pin came out: it slides back.</summary>
        public void ReleasePin()
        {
            if (PinOut || PinProgress <= 0) return;
            if (PinProgress > 0.15) Say("pinHalf", "핀이 덜 빠졌다. 끝까지 당겨 뽑아야 한다.");
            PinProgress = 0;
        }

        /// <summary>"불이야!" (once; it raises the alarm for the floor).</summary>
        public bool Shout()
        {
            if (Ended || Practice || _seq.Contains("shout")) return false;
            _seq.Add("shout");
            Say("shout", "\"불이야!\" 소리에 이웃들이 문을 열고 내다본다.", force: true);
            return true;
        }

        /// <summary>물러나 알린다 (in practice: stop practising).</summary>
        public void Retreat()
        {
            if (Ended) return;
            if (Practice) { End("stopped"); return; }
            if (!_seq.Contains("retreat")) _seq.Add("retreat");
            End("retreat");
        }

        /// <summary>Q: the first press asks, a second within <see cref="ConfirmS"/> steps back. True when it stepped back.</summary>
        public bool RetreatPress()
        {
            if (Ended) return false;
            if (_clock - _askAt <= ConfirmS) { Retreat(); return true; }
            _askAt = _clock;
            Say("ask", Practice ? "연습을 그만할까요? Q 한 번 더" : "물러나 알릴까요? Q 한 번 더", force: true);
            return false;
        }

        /// <summary>The controls are off (a menu, the pause): the hands let go of the handle until the next tick.</summary>
        public void Hold() { Spraying = false; Hitting = false; }

        void End(string kind)
        {
            if (Ended) return;
            Ended = true; EndKind = kind; Spraying = false; Hitting = false;
        }

        // ---------- lines ----------

        /// <summary>Lines said since the last call (the story shows them as toasts).</summary>
        public List<string> TakeSaid()
        {
            if (_said.Count == 0) return null;
            var l = new List<string>(_said);
            _said.Clear();
            return l;
        }

        void Say(string key, string text, bool force = false)
        {
            if (!force && _quiet.TryGetValue(key, out var until) && until > _clock) return;
            _quiet[key] = _clock + SayEvery;
            _said.Add(text);
        }

        // ---------- the frame ----------

        public void Tick(double dt)
        {
            dt = Math.Max(0, Math.Min(0.1, dt));
            _clock += dt;
            if (Ended) return;
            var a = Input;
            if (!a.Seen) return; // nothing measured yet (the first frame after the scene built)
            bool inReach = a.Distance <= ReachNow;
            if (a.Distance <= ReachNow + 0.6) Approached = true;

            // walking off (the stairs, or well away after having come close) is retreating
            if (!Practice && (a.Away || (Approached && a.Distance > Setup.RetreatDistance))) { Retreat(); return; }

            // the fire keeps growing while it burns (the 2D screen's danger meter)
            if (!Large && GrowPerS > 0)
            {
                Danger = Math.Min(1, Danger + GrowPerS * dt * (Spraying && !a.ExitBehind ? 1.3 : 1));
                FireLv = Math.Min(1, FireLv + GrowPerS * dt * 0.4);
                if (Danger >= 1) Grow();
            }

            // the hands on the handle
            bool wantSpray = a.Squeeze;
            if (wantSpray && !PinOut) { Say("pinIn", "안전핀이 꽂혀 있어 손잡이가 눌리지 않는다."); wantSpray = false; }
            if (wantSpray && Powder <= 0) wantSpray = false;
            Spraying = wantSpray;
            Hitting = false;

            bool onFire = a.OnBase || a.OnFlame;
            if (PinOut && onFire && a.Distance <= ReachNow + 0.6)
            {
                _aimFor += dt;
                if (_aimFor >= AimHold && !_seq.Contains("aim"))
                {
                    _seq.Add("aim");
                    Say("aim", a.ExitBehind ? "출구를 등지고 노즐을 불 쪽으로 향했다." : "노즐을 불 쪽으로 향했다. 출구가 등 뒤에 있는지 확인하자.", force: true);
                }
            }
            else _aimFor = 0;

            // sweep strokes: the aim's travel across the base, counted at each turn
            if (Spraying && a.OnBase && inReach)
            {
                if (!double.IsNaN(_lastLateral))
                {
                    double dx = a.Lateral - _lastLateral;
                    int dir = Math.Abs(dx) < 1e-4 ? 0 : Math.Sign(dx);
                    if (dir != 0 && dir != _swingDir)
                    {
                        if (_swing >= StrokeSwing) { Strokes++; _sinceStroke = 0; }
                        _swingDir = dir; _swing = 0;
                    }
                    _swing += Math.Abs(dx);
                    if (_swing >= StrokeSwing * 2.2) { Strokes++; _sinceStroke = 0; _swing = 0; } // a long, slow sweep counts too
                }
                _lastLateral = a.Lateral;
            }
            else { _lastLateral = double.NaN; _swing = 0; }
            _sinceStroke += dt;

            if (Spraying)
            {
                if (!Practice) Powder = Math.Max(0, Powder - dt);
                SprayTime += dt;
                if (a.ExitBehind) BehindTime += dt; else FacingTime += dt;
                if (!a.ExitBehind && !Practice)
                {
                    SmokeExtra = Math.Min(SmokeCap, SmokeExtra + SmokeRate * dt);
                    Say("exit", "출구가 등 뒤에 있지 않다. 연기가 나갈 길 쪽으로 번진다.");
                }
                if (!_seq.Contains("squeeze") && _seq.Contains("aim") && onFire && inReach)
                {
                    _seq.Add("squeeze");
                    Say("squeeze", Low ? "손잡이를 움켜쥐었다. 분말이 힘없이 나온다." : "손잡이를 움켜쥐자 하얀 분말이 뿜어져 나온다. 빗자루로 쓸듯이 좌우로!", force: true);
                }
                double knock = (Large ? LargeKnock : Knock) * Power;
                if (!onFire) { MissTime += dt; Say("miss", "노즐이 불을 향하지 않았다. 분말이 엉뚱한 곳으로 간다."); }
                else if (!inReach) { MissTime += dt; Say("far", "분말이 불까지 닿지 않는다. 조금 더 가까이."); }
                else if (a.OnBase)
                {
                    BaseTime += dt; Hitting = true;
                    bool sweeping = _sinceStroke < StrokeKeep;
                    FireLv = Math.Max(0, FireLv - knock * (sweeping ? 1 : StillShare) * dt);
                    if (!sweeping && BaseTime > 1.2) Say("still", "한곳에만 뿌리고 있다. 빗자루로 쓸듯이 좌우로.");
                }
                else
                {
                    FlameTime += dt;
                    FireLv = Math.Max(0, FireLv - knock * FlameShare * dt);
                    Say("flame", "분말이 불길 위로 흩어진다. 불이 난 곳(아래)을 덮도록 겨누자.");
                }
                if (!Large && FireLv <= 0.02)
                {
                    FireLv = 0;
                    if (!_seq.Contains("squeeze")) _seq.Add("squeeze");
                    _seq.Add("sweep");
                    Say("out", Practice ? "가상 불이 꺼졌다. 순서를 익혔다." : "불이 꺼졌다.", force: true);
                    End("out");
                    return;
                }
                if (Powder <= 0) Say("empty", Low ? "분말이 금방 끊겼다. 압력이 빠진 소화기였다. 물러나 알리자." : "분말이 다 떨어졌다. 불이 남았다면 물러나 알리자.", force: true);
            }
            else if (!Practice) SmokeExtra = Math.Max(0, SmokeExtra - 0.02 * dt);

            if (a.Distance < TooClose && FireLv > 0.05) Say("close", "너무 가깝다. 열기가 얼굴에 닿는다. 조금 물러서자.");

            // a fire that is clearly lost: heat and smoke build next to it; staying on is fixating
            bool lost = Large || (Powder <= 0 && FireLv > 0.02);
            double near = a.Distance < 4.5 ? 1 : 0;
            if (lost && !Practice)
            {
                if (near > 0) LostTime += dt;
                if (LostTime > FixateAfter) Fixated = true;
                Heat = Math.Min(1, Math.Max(0, Heat + (near > 0 ? dt / HeatS * (a.Distance < 2.5 ? 1.4 : 1) : -dt / HeatS)));
                if (LostTime > 2.5) Say("lost", Large ? "불길이 천장까지 닿았다. 소화기 한 대로는 어렵다. 물러나 알리자 (Q 두 번)." : "끄기 어렵다. 물러나 알리자 (Q 두 번).");
                if (Heat >= 1)
                {
                    // driven back: a small fire chosen and lost is a retreat after all (fixating cost the learning point);
                    // a fire that was large when chosen stays the escalation the original fails
                    Fixated = true;
                    if (Judged == "small") _seq.Add("retreat");
                    Say("heat", "열기와 연기에 밀려났다. 끄기 어려운 불 앞에 너무 오래 있었다.", force: true);
                    End("overwhelmed");
                }
            }
        }

        void Grow()
        {
            if (Large) return;
            Large = Grown = true;
            FireLv = Math.Max(FireLv, 0.85); // the story says it (the chapter's own grow line)
        }

        // ---------- the steps the HUD lists ----------

        public sealed class StepLine
        {
            public string Id, Text;
            public bool Done;
        }

        /// <summary>
        /// What the step list shows, as bits (approached, pin, aim, squeeze, sweep, shout, the fire lost): the story
        /// rebuilds the list only when this changes, so nothing is allocated per frame.
        /// </summary>
        public int StepMask =>
            (Approached ? 1 : 0) | (PinOut ? 2 : 0) | (Done("aim") ? 4 : 0) | (Done("squeeze") ? 8 : 0) | (Done("sweep") ? 16 : 0)
            | (Done("shout") ? 32 : 0) | (Lost ? 64 : 0);

        /// <summary>The fire is beyond one extinguisher: large (chosen or grown), or the powder gone with it burning.</summary>
        public bool Lost => !Practice && (Large || (Powder <= 0 && FireLv > 0.02));

        /// <summary>
        /// The steps in order, with the keys for this setup (hold or toggle). "불이야!" first (Drills' order), not in
        /// practice. A lost fire lists the retreat instead (the shelter's fire is lost from the start).
        /// </summary>
        public List<StepLine> Steps()
        {
            string exit = string.IsNullOrEmpty(Setup.ExitWord) ? "계단" : Setup.ExitWord;
            var list = new List<StepLine>();
            if (!Practice) list.Add(new StepLine { Id = "shout", Text = "\"불이야!\" 외쳐 알리기 · T", Done = Done("shout") });
            if (Lost)
            {
                list.Add(new StepLine { Id = "stop", Text = "끄기를 멈추고 물러나기 · Q 두 번", Done = false });
                list.Add(new StepLine { Id = "leave", Text = exit + "로 대피하며 119 신고", Done = false });
                return list;
            }
            string hold = Setup.Toggle ? "누르기" : "길게";
            list.Add(new StepLine { Id = "carry", Text = "소화기를 들고 불 쪽으로 가까이 다가가기", Done = Approached });
            list.Add(new StepLine { Id = "pin", Text = Setup.Toggle ? "안전핀 뽑기 · R 또는 클릭" : "안전핀 뽑기 · 마우스를 누른 채 아래로 끌기 (R 길게)", Done = PinOut });
            list.Add(new StepLine { Id = "aim", Text = "출구를 등지고 노즐을 불이 난 곳(아래)으로", Done = Done("aim") });
            list.Add(new StepLine { Id = "squeeze", Text = "손잡이 움켜쥐기 · 마우스 왼쪽 " + hold + " (Space)", Done = Done("squeeze") });
            list.Add(new StepLine { Id = "sweep", Text = "빗자루로 쓸듯이 좌우로", Done = Done("sweep") });
            return list;
        }

        int _hintMask = -1;
        string _hint = "";

        /// <summary>The next step's line (the hint button), or the retreat when the fire is lost. Cached per <see cref="StepMask"/>.</summary>
        public string Hint()
        {
            int m = StepMask;
            if (m == _hintMask) return _hint;
            _hintMask = m;
            string exit = string.IsNullOrEmpty(Setup.ExitWord) ? "계단" : Setup.ExitWord;
            if (Lost) return _hint = "불이 커졌거나 분말이 떨어졌다면 끄기를 멈추고 물러나 알립니다 (Q 두 번 · " + exit + " · 119).";
            var next = Steps().FirstOrDefault(s => !s.Done);
            return _hint = next == null ? "" : "다음: " + next.Text;
        }

        // ---------- the recap card after a try ----------

        /// <summary>
        /// The card after a 3D try (the 2D screen's end card): each step of the official order ticked, the shout fact when
        /// it was not first, then how the powder went (the base, the exit behind), and the retreat when the fire was lost.
        /// fact(id) gives a fact's text (null when missing). (null, null) when there is nothing to recap: practice
        /// stopped, or a retreat before any powder flew (the chapter's own card follows).
        /// </summary>
        public (string title, List<string> lines) Recap(Func<string, string> fact)
        {
            if (!Ended || EndKind == "stopped") return (null, null);
            if (EndKind == "retreat" && SprayTime < 1) return (null, null);
            fact ??= _ => null;
            string title = EndKind == "out" ? (Practice ? "가상 불을 껐다" : "불을 껐다") : EndKind == "overwhelmed" ? "열기에 밀려났다" : "물러나 알렸다";
            var lines = new List<string>();
            int n = 0;
            foreach (var st in Drills.ExtSteps)
            {
                if (Practice && st.Id == "shout") continue;
                n++;
                lines.Add(n + ". " + (Done(st.Id) ? "✓ " : "○ ") + st.Label);
            }
            if (!Practice && _seq.FirstOrDefault(id => id != "fight") != "shout") Add(lines, fact("fire_shout"));
            var r = Result();
            var b = BaseOk(r);
            if (b == true) lines.Add("✓ 불이 난 곳(아래)을 겨눠 뿌렸다.");
            else if (b == false) Add(lines, "○ 분말이 불길 위나 다른 곳으로 많이 갔다.", fact("ext_cover"));
            var e = BehindOk(r);
            if (e == true) lines.Add("✓ 출구를 등지고 뿌렸다.");
            else if (e == false) Add(lines, "○ 출구를 등지지 않고 뿌렸다.", fact("ext_door"));
            var ret = Practice ? null : RetreatOk(r);
            if (ret == true) lines.Add("✓ 끄기 어려워지자 멈추고 물러났다.");
            else if (ret == false) Add(lines, "○ 끄기 어려운 불 앞에 너무 오래 머물렀다.", fact("fire_big"));
            if (Practice) foreach (var id in new[] { "ext_1", "ext_2", "ext_3" }) Add(lines, fact(id));
            return (title, lines);
        }

        static void Add(List<string> lines, string text, string fact = null)
        {
            if (text != null && fact != null) { lines.Add(text + " " + fact); return; }
            var one = text ?? fact;
            if (!string.IsNullOrEmpty(one)) lines.Add(one);
        }

        public static string FireWord(double lv) => lv > 0.66 ? "큼" : lv > 0.25 ? "중간" : lv > 0.02 ? "작음" : "꺼짐";

        // ---------- geometry (pure, so it is tested) ----------

        /// <summary>
        /// Where a ray from the eye (o, unit d) meets the fire whose base centre is c: on the vertical plane through c facing
        /// the eye, the base runs from 0.25 m below c to baseTop above it and halfWidth either side; the flames run from
        /// baseTop to flameTop. The floor in front of the base (within halfWidth + 0.4 of c) counts as the base too: powder
        /// aimed low rolls into it.
        /// </summary>
        public static AimHit Aim(double ox, double oy, double oz, double dx, double dy, double dz, double cx, double cy, double cz, double halfWidth, double baseTop, double flameTop)
        {
            double nx = cx - ox, nz = cz - oz;
            double nl = Math.Sqrt(nx * nx + nz * nz);
            if (nl < 1e-6) return new AimHit(false, false, 0, 0);
            nx /= nl; nz /= nl;
            double rx = -nz, rz = nx; // right of the facing (y up)
            double denom = dx * nx + dz * nz;
            bool onBase = false, onFlame = false;
            double lateral = 0, height = 0;
            if (denom > 1e-4)
            {
                double t = ((cx - ox) * nx + (cz - oz) * nz) / denom;
                double px = ox + dx * t, py = oy + dy * t, pz = oz + dz * t;
                lateral = (px - cx) * rx + (pz - cz) * rz;
                height = py - cy;
                if (Math.Abs(lateral) <= halfWidth)
                {
                    if (height >= -0.25 && height <= baseTop) onBase = true;
                    else if (height > baseTop && height <= flameTop) onFlame = true;
                }
            }
            if (!onBase && dy < -1e-4)
            {
                double tf = (cy - oy) / dy;
                if (tf > 0)
                {
                    double fx = ox + dx * tf, fz = oz + dz * tf;
                    double ddx = fx - cx, ddz = fz - cz;
                    if (Math.Sqrt(ddx * ddx + ddz * ddz) <= halfWidth + 0.4)
                    {
                        onBase = true; onFlame = false;
                        lateral = ddx * rx + ddz * rz;
                        height = 0;
                    }
                }
            }
            return new AimHit(onBase, onFlame, lateral, height);
        }

        /// <summary>The exit is behind someone facing (fx, fz) when the way to it (ex, ez) points back (horizontal vectors).</summary>
        public static bool Behind(double fx, double fz, double ex, double ez)
        {
            double fl = Math.Sqrt(fx * fx + fz * fz), el = Math.Sqrt(ex * ex + ez * ez);
            if (fl < 1e-6 || el < 1e-6) return false;
            return (fx * ex + fz * ez) / (fl * el) < -0.3;
        }

        // ---------- result ----------

        /// <summary>Drills.EvalExtinguisher on the recorded sequence and the fire as chosen.</summary>
        public ExtinguisherEval Eval() => Drills.EvalExtinguisher(_seq, Judged);

        /// <summary>
        /// The story's result: { sequence, result } like the 2D screen, plus what the 3D try shows (view "3d", end, low,
        /// share of spray on the base, share with the exit behind, strokes, fixated, powder left).
        /// </summary>
        public JObject Result()
        {
            var ev = Practice ? null : Eval();
            double spray = Math.Max(1e-6, SprayTime);
            return new JObject
            {
                ["sequence"] = new JArray(_seq.Cast<object>().ToArray()),
                ["result"] = Practice ? (EndKind == "out" ? "practice" : "stopped") : ev.Result,
                ["view"] = "3d", ["end"] = EndKind, ["fire"] = Judged, ["grown"] = Grown, ["low"] = Low,
                ["sprayed"] = Math.Round(SprayTime, 1),
                ["base"] = SprayTime > 0 ? Math.Round(BaseTime / spray, 2) : 0,
                ["behind"] = SprayTime > 0 ? Math.Round(BehindTime / spray, 2) : 0,
                ["strokes"] = Strokes, ["fixated"] = Fixated, ["powderLeft"] = Math.Round(Powder, 1),
            };
        }

        /// <summary>The learning report's judgement of a 3D try's details (null when that row does not apply).</summary>
        public static bool? BaseOk(JObject d) => d?["sprayed"] != null && (double)d["sprayed"] >= 1 ? (double)d["base"] >= 0.5 : (bool?)null;
        public static bool? BehindOk(JObject d) => d?["sprayed"] != null && (double)d["sprayed"] >= 1 ? (double)d["behind"] >= 0.6 : (bool?)null;
        /// <summary>When the fire was lost (grown or chosen large, or the powder gone): did 정민 stop and retreat in time?</summary>
        public static bool? RetreatOk(JObject d)
        {
            if (d == null) return null;
            bool lost = d["fire"]?.ToString() == "large" || (d["grown"]?.Type == JTokenType.Boolean && (bool)d["grown"])
                        || (d["end"]?.ToString() != "out" && d["powderLeft"] != null && (double)d["powderLeft"] <= 0);
            if (!lost) return null;
            bool fixated = d["fixated"]?.Type == JTokenType.Boolean && (bool)d["fixated"];
            return d["end"]?.ToString() == "retreat" && !fixated;
        }
    }
}
