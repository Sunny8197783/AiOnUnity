using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>
    /// The extinguisher encounter's state machine (original ui/extinguisher.js minus the drawing): decide (끈다 /
    /// 물러나 알린다), then press the steps in order (shout optional, pin -> aim -> squeeze (held 0.7 s) -> sweep
    /// (alternating strokes)) while the danger meter fills. Results are scored by <see cref="Drills.EvalExtinguisher"/>.
    /// No Unity types: the screen feeds it time, presses, holds, strokes and pointer x, and draws what it reads back.
    /// </summary>
    public sealed class ExtinguisherLogic
    {
        public const double HoldS = 0.7;
        public static readonly string[] Ops = { "pin", "aim", "squeeze", "sweep" };
        /// <summary>Seconds until a small fire spreads, per ladder stage (others: 40).</summary>
        public static readonly IReadOnlyDictionary<string, double> GrowS = new Dictionary<string, double> { ["guided"] = 70, ["hint"] = 45, ["free"] = 32 };
        public static readonly IReadOnlyDictionary<string, string> ModeText = new Dictionary<string, string> { ["guided"] = "안내 있음", ["hint"] = "힌트 가능", ["free"] = "혼자 판단", ["view"] = "보기" };

        static readonly Dictionary<string, Dictionary<string, string>> Mistakes = new()
        {
            ["squeeze"] = new() { ["pin"] = "안전핀이 꽂혀 있어 손잡이가 눌리지 않는다.", ["aim"] = "노즐이 불을 향하지 않았다. 분말이 엉뚱한 곳으로 간다." },
            ["sweep"] = new() { ["pin"] = "안전핀이 그대로다. 아무것도 나오지 않는다.", ["aim"] = "노즐을 먼저 불 쪽으로 향해야 한다.", ["squeeze"] = "손잡이를 쥐지 않아 분말이 나오지 않는다." },
            ["aim"] = new() { ["pin"] = "안전핀을 먼저 뽑아야 한다." },
        };
        const string WrongOrder = "순서가 맞지 않는다.";

        // ---------- options ----------
        public string Mode { get; }
        public bool Large { get; }
        public bool Practice { get; }
        /// <summary>
        /// Competition upgrade: the extinguisher lost its pressure (the 1장 gauge was never read, ExtinguisherCheck). Each
        /// stroke does <see cref="LowPower"/> as much and the powder stops after <see cref="LowStrokes"/> strokes (end "empty"):
        /// alternating strokes still just put a small fire out, one-sided ones do not.
        /// </summary>
        public bool Low { get; }
        public const int LowStrokes = 7;
        public const double LowPower = 0.8;
        int _strokes;
        public IReadOnlyList<DrillStep> Steps { get; }
        public IReadOnlyList<DrillStep> Choices { get; }
        /// <summary>The step buttons' order on screen (shuffled by a fixed hash, like the original).</summary>
        public IReadOnlyList<DrillStep> Order { get; }
        public double GrowPerS { get; }

        // ---------- state (the original's S) ----------
        /// <summary>decide | act</summary>
        public string Phase { get; private set; }
        public double FireLv { get; private set; }
        public double Danger { get; private set; }
        public double Hold { get; private set; }
        public bool Holding { get; private set; }
        public bool PinOut { get; private set; }
        public bool Aimed { get; private set; }
        public bool Spraying { get; private set; }
        public int LastDir { get; private set; }
        /// <summary>Nozzle swing for the drawing, -1..1, decays every tick.</summary>
        public double SweepDir { get; private set; }
        /// <summary>Seconds of camera shake left (a mistake).</summary>
        public double Shake { get; private set; }
        public bool Ended { get; private set; }
        /// <summary>null | out | spread | overwhelmed</summary>
        public string EndKind { get; private set; }
        public bool Closed { get; private set; }
        public string Feedback { get; private set; } = "";
        /// <summary>Reduced motion: mistakes do not shake.</summary>
        public bool Reduced { get; set; }

        readonly HashSet<string> _done = new();
        readonly List<string> _seq = new();

        /// <summary>Something the side panel shows changed (the original's refreshSteps / render calls).</summary>
        public event Action Changed;
        /// <summary>The encounter ended (kind).</summary>
        public event Action<string> EndedWith;
        /// <summary>The screen should close with <see cref="Result"/>.</summary>
        public event Action CloseRequested;
        /// <summary>The squeeze hold completed: focus moves to the sweep button.</summary>
        public event Action Squeezed;

        public ExtinguisherLogic(string mode = "guided", string fire = "small", IReadOnlyList<DrillStep> steps = null, IReadOnlyList<DrillStep> choices = null, bool practice = false, bool low = false)
        {
            Mode = mode;
            Low = low && !practice;
            Large = fire == "large";
            Practice = practice;
            Steps = steps ?? Drills.ExtSteps;
            Choices = choices ?? Drills.ExtChoices;
            Order = StepOrder(Steps);
            Phase = practice ? "act" : "decide";
            FireLv = practice ? 0.3 : Large ? 0.88 : 0.36;
            if (practice) _seq.Add("fight");
            double grow = mode != null && GrowS.TryGetValue(mode, out var g) ? g : 40;
            GrowPerS = practice ? 0 : (1 / grow) * (Large ? 2.4 : 1);
        }

        public IReadOnlyList<string> Sequence => _seq;
        public bool IsDone(string id) => _done.Contains(id);
        public int DoneCount => _done.Count;
        public double HoldProgress => _done.Contains("squeeze") ? 1 : Math.Min(1, Hold / HoldS);

        // ---------- the original's helpers ----------

        public static int Hash(string s)
        {
            int a = 7;
            foreach (var c in s ?? "") a = (a * 31 + c) % 1009;
            return a;
        }

        /// <summary>[...steps].sort((a, b) => hash(a.id) - hash(b.id)) (stable).</summary>
        public static IReadOnlyList<DrillStep> StepOrder(IEnumerable<DrillStep> steps) => steps.OrderBy(s => Hash(s.Id)).ToList();

        public string NextOp() => Ops.FirstOrDefault(id => !_done.Contains(id));
        /// <summary>The step the guide points at: the shout until anything was done, then the next operation.</summary>
        public string Expected() => !_done.Contains("shout") && _done.Count == 0 ? "shout" : NextOp();
        /// <summary>The recommended choice in the decide phase.</summary>
        public string Recommended => Large ? "retreat" : "fight";

        public DrillStep Step(string id) => Steps.FirstOrDefault(s => s.Id == id);
        public DrillStep Choice(string id) => Choices.FirstOrDefault(c => c.Id == id);

        public static List<string> FactTexts(IEnumerable<string> ids, Func<string, string> fact) =>
            (ids ?? Enumerable.Empty<string>()).Select(id => fact?.Invoke(id)).Where(t => !string.IsNullOrEmpty(t)).ToList();

        // ---------- texts ----------
        public string Eyebrow => Practice ? "소화기 · 연습" : "소화기";
        public string Title => Practice ? "사용 순서 익히기" : Large ? "천장까지 닿는 불길" : "작은 불";
        public string ModeChip => Mode != null && ModeText.TryGetValue(Mode, out var t) ? t : null;
        public string MeterLabel => Large ? "버틸 수 있는 시간" : "불이 번지기까지";
        public string RetreatLabel => Practice ? "연습 그만하기" : "물러나 알린다 (계단 · 119)";
        public const string PracticeNote = "연습입니다. 실제 불은 없습니다.";
        public const string Keys = "Tab으로 고르기 · Enter/Space 누르기 · 손잡이는 길게 누르기 · 쓸 때는 불 위에서 마우스를 좌우로 (←/→도 됨) · Esc 물러나기";
        public const string DecideQuestion = "어떻게 할까?";
        public const string ActQuestion = "순서대로 누르세요";
        public const string HoldTip = "길게 누르기";
        public const string SweepTip = "마우스로 좌우로 쓸기";

        /// <summary>'ok' | 'warn' | 'over' for the danger bar.</summary>
        public static string DangerState(double d) => d > 0.75 ? "over" : d > 0.45 ? "warn" : "ok";
        public static string DangerWord(double d) => d > 0.75 ? "곧 번짐" : d > 0.45 ? "서둘러야 함" : "여유 있음";
        public static string FireWord(double lv) => lv > 0.66 ? "큼" : lv > 0.25 ? "중간" : lv > 0 ? "작음" : "꺼짐";

        public static string EndTitle(string kind, bool practice) => kind switch
        {
            "out" => practice ? "연습 완료" : "불이 꺼졌다",
            "spread" => "불이 번졌다",
            "overwhelmed" => "더는 버틸 수 없다",
            "empty" => "분말이 다 떨어졌다",
            _ => "",
        };
        public static string EndBody(string kind) => kind switch
        {
            "spread" => "상자 더미로 옮겨 붙었다. 이제는 작은 불이 아니다.",
            "overwhelmed" => "연기와 열기가 덮쳐 온다. 소화기 한 대로는 감당할 수 없는 불이다.",
            "empty" => "압력이 빠진 소화기라 분말이 금방 끊겼다. 불은 그대로다. 물러나 알리자.",
            _ => null,
        };
        public static string EndOk(string kind) => kind == "out" ? "확인" : kind == "spread" || kind == "empty" ? "계속" : "그래도 버틴다";

        /// <summary>On a fire put out: was the alarm raised before anything else?</summary>
        public bool ShoutFirst => _seq.FirstOrDefault(id => id != "fight") == "shout";
        /// <summary>The retreat button stays after the end except when the fire is out or in practice.</summary>
        public bool RetreatVisible => !(Ended && (EndKind == "out" || Practice));

        // ---------- actions ----------

        public void Say(string text) { Feedback = text ?? ""; }

        /// <summary>The 끈다 choice.</summary>
        public void Fight()
        {
            if (Phase != "decide" || Closed) return;
            _seq.Add("fight"); Phase = "act";
            Say("소화기를 들고 불 앞에 섰다.");
            Changed?.Invoke();
        }

        /// <summary>A step button click (squeeze is held instead: <see cref="StartHold"/>).</summary>
        public void Press(string id)
        {
            if (Phase != "act" || Ended) return;
            if (id == "shout")
            {
                if (_done.Contains("shout")) { Say("이미 알렸다."); return; }
                _done.Add("shout"); _seq.Add("shout"); Changed?.Invoke();
                Say("\"불이야!\" 소리에 이웃들이 문을 열고 내다본다.");
                return;
            }
            if (_done.Contains(id) && id != "sweep") { Say("이미 했다."); return; }
            if (id == "sweep" && _done.Contains("squeeze")) { Say("불 그림 위에서 마우스를 좌우로 크게 움직여 쓸어 주세요. (키보드: ←/→)"); return; }
            var next = NextOp();
            if (id != next) { Mistake(MistakeText(id, next)); return; }
            _done.Add(id); _seq.Add(id);
            if (id == "pin") { PinOut = true; Say("안전핀을 뽑았다."); }
            if (id == "aim") { Aimed = true; Say(Large ? "노즐을 불 쪽으로 향했다. 열기가 얼굴까지 닿는다." : "문을 등지고 노즐을 불 쪽으로 향했다."); }
            Changed?.Invoke();
        }

        public static string MistakeText(string id, string next) =>
            id != null && next != null && Mistakes.TryGetValue(id, out var m) && m.TryGetValue(next, out var t) ? t : WrongOrder;

        public void StartHold()
        {
            if (Phase != "act" || Ended || _done.Contains("squeeze")) return;
            var next = NextOp();
            if (next != "squeeze") { Mistake(MistakeText("squeeze", next)); return; }
            Holding = true;
        }

        public void StopHold()
        {
            if (!Holding) return;
            Holding = false;
            if (!_done.Contains("squeeze")) { Hold = 0; Say("손잡이를 끝까지 힘껏 쥐어야 한다. 길게 누르세요."); }
        }

        void DoSqueeze()
        {
            Holding = false; _done.Add("squeeze"); _seq.Add("squeeze"); Spraying = true;
            Say("손잡이를 움켜쥐자 하얀 분말이 뿜어져 나온다. 불 위에서 마우스를 좌우로 움직여 쓸듯이!");
            Changed?.Invoke();
            Squeezed?.Invoke();
        }

        /// <summary>One sweep stroke (dir -1 left, 1 right). Alternating strokes put a small fire out; a large one barely shrinks.</summary>
        public void Stroke(int dir)
        {
            if (!_done.Contains("squeeze") || Ended) return;
            bool alt = dir != LastDir;
            LastDir = dir; SweepDir = dir;
            FireLv = Math.Max(0, FireLv - (Large ? 0.012 : alt ? 0.085 : 0.03) * (Low ? LowPower : 1));
            if (!alt && !Large) Say("한쪽으로만 뿌리고 있다. 좌우로 쓸듯이.");
            if (!Large && FireLv <= 0.02) { FireLv = 0; _seq.Add("sweep"); _done.Add("sweep"); End("out"); return; }
            if (Low && ++_strokes >= LowStrokes) { Spraying = false; End("empty"); }
        }

        // Mouse sweep: every swing of ~1/6 of the scene's width that turns back counts as one stroke.
        double _swing; int _swingDir; double? _lastX;

        /// <summary>The pointer moved over the scene; x is 0..1 across it.</summary>
        public void PointerMove(double x)
        {
            if (!_done.Contains("squeeze") || Ended) { _lastX = null; return; }
            SweepDir = Math.Max(-1, Math.Min(1, (x - 0.76) * 4));
            if (_lastX != null)
            {
                double dx = x - _lastX.Value;
                int dir = Math.Sign(dx);
                if (dir != 0 && dir != _swingDir) { _swingDir = dir; _swing = 0; }
                _swing += Math.Abs(dx);
                if (_swing > 0.16) { _swing = -10; Stroke(_swingDir); } // one stroke per swing until it turns back
            }
            _lastX = x;
        }

        public void PointerLeave() { _lastX = null; }

        void Mistake(string text)
        {
            Danger = Math.Min(1, Danger + (Practice ? 0 : 0.12)); Shake = Reduced ? 0 : 0.35;
            Say(text);
        }

        /// <summary>The retreat button (in practice: stop practising). Works from the end screen too, except after the fire is out.</summary>
        public void RetreatButton()
        {
            if (Practice) Close(); else Retreat();
        }

        public void Retreat()
        {
            if (Closed || EndKind == "out") return;
            if (!_seq.Contains("retreat")) _seq.Add("retreat");
            Close();
        }

        /// <summary>Esc: in practice or after the fire is out it closes, otherwise it retreats.</summary>
        public void Escape()
        {
            if (Practice || EndKind == "out") Close(); else Retreat();
        }

        /// <summary>The end card's button.</summary>
        public void Close()
        {
            if (Closed) return;
            Closed = true;
            CloseRequested?.Invoke();
        }

        /// <summary>←/→ while sweeping. Returns true when handled.</summary>
        public bool Arrow(int dir)
        {
            if (Phase == "act" && !Ended && _done.Contains("squeeze")) { Stroke(dir); return true; }
            return false;
        }

        void End(string kind)
        {
            Ended = true; EndKind = kind;
            if (kind == "out") Spraying = false;
            Holding = false;
            Changed?.Invoke();
            EndedWith?.Invoke(kind);
        }

        /// <summary>One frame (seconds; clamped to 0..0.1 like the original).</summary>
        public void Tick(double dt)
        {
            dt = Math.Max(0, Math.Min(0.1, dt));
            if (Closed) return;
            if (Phase == "act" && !Ended)
            {
                Danger = Math.Min(1, Danger + GrowPerS * dt);
                FireLv = Math.Min(1, FireLv + GrowPerS * dt * 0.4);
                if (Holding)
                {
                    Hold += dt;
                    if (Hold >= HoldS) DoSqueeze();
                }
                if (Danger >= 1) End(Large ? "overwhelmed" : "spread");
            }
            SweepDir *= Math.Pow(0.02, dt);
            Shake = Math.Max(0, Shake - dt);
        }

        // ---------- result ----------

        /// <summary>evalExtinguisher(seq).result, or 'incomplete' when the sequence cannot be scored (unknown custom steps).</summary>
        public string ResultKind()
        {
            try { return Drills.EvalExtinguisher(_seq, Large ? "large" : "small").Result; }
            catch (Exception) { return "incomplete"; }
        }

        /// <summary>The original's resolved value: { sequence, result }.</summary>
        public JObject Result() => new JObject { ["sequence"] = new JArray(_seq.Cast<object>().ToArray()), ["result"] = ResultKind() };

        // ---------- options ----------

        /// <summary>Builds the logic from the original's option object ({ mode, fire, steps, choices, practice }).</summary>
        public static ExtinguisherLogic FromOptions(JObject o)
        {
            o ??= new JObject();
            string mode = o.TryGetValue("mode", out var m) ? (m.Type == JTokenType.String ? (string)m : null) : "guided";
            if (m != null && m.Type == JTokenType.Undefined) mode = "guided";
            string fire = o["fire"]?.Type == JTokenType.String ? (string)o["fire"] : "small";
            var steps = o["steps"] is JArray sa ? sa.ToObject<List<DrillStep>>() : null;
            var choices = o["choices"] is JArray ca ? ca.ToObject<List<DrillStep>>() : null;
            return new ExtinguisherLogic(mode, fire, steps, choices, Truthy(o["practice"]), Truthy(o["low"]));
        }

        /// <summary>JS truthiness of an option value.</summary>
        public static bool Truthy(JToken v)
        {
            if (v == null) return false;
            switch (v.Type)
            {
                case JTokenType.Null: case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return (bool)v;
                case JTokenType.Integer: return (long)v != 0;
                case JTokenType.Float: { var d = (double)v; return d != 0 && !double.IsNaN(d); }
                case JTokenType.String: return ((string)v).Length > 0;
                default: return true;
            }
        }
    }
}
