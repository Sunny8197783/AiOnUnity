// "불나면 살펴서 대피" (competition upgrade, 3장 복도 화재 앞): the judgement step before a fire in the apartment.
// 정민 first LOOKS (the back of the hand on the door handle, the gap under the door, the corridor's sound, the rest of the
// home, the management office's broadcast, the phone), then says WHERE the fire is, then chooses what to DO: leave low by
// the stairs, stay in (door shut, wet towels in the gap, windows shut, away from the door), the balcony's 경량칸막이 to
// the next home, or the elevator (a neighbour stops you: a correction, never a punishment). Staying and the partition end
// with a 119 call put together from cards (where, what is burning, who is there, keep the line open).
//
// The verdicts follow the 소방청 guide (facts-ext fire_own_home, fire_own_trapped, fire_seal, fire_other_smoke and the
// original fire_stay, fire_lift, fire_door): Judge(situation, act) is scenario-free, so other fires reuse it; a scenario
// supplies the clue texts and the truth. Feedback always says why and names the facts it rests on (shown with sources).
// Pure: no UnityEngine; the screen (UI/Screens/LookFirstScreen.cs) only draws and forwards input.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public enum LookPhase { Look, Where, Act, Seal, Partition, Report, Done }

    /// <summary>good: right for this fire; caution: allowed but not the first choice here (the reason says what is);
    /// wrong: the guide says otherwise; corrected: someone stopped you (the elevator); info: a clue; step: progress on a
    /// task (a box moved, a kick at the partition).</summary>
    public enum LookVerdict { Info, Good, Caution, Wrong, Corrected, Step }

    public sealed class LookFeedback
    {
        public LookVerdict Verdict;
        public string Title, Text;
        /// <summary>Who said it (the elevator correction's neighbour), or null.</summary>
        public string Speaker;
        /// <summary>The screen's tag when the verdict's own does not fit (a wrong guess is not "against the guide").</summary>
        public string Tag;
        public string[] Facts = Array.Empty<string>();
    }

    /// <summary>What the fire really is (the clues tell it; the verdicts read it).</summary>
    public sealed class LookSituation
    {
        /// <summary>"home" (our own home is burning) or "other" (another home or the shared corridor).</summary>
        public string FireAt = "other";
        /// <summary>Smoke (or flame) comes into our home.</summary>
        public bool SmokeIn;
        /// <summary>The corridor and the stairs are free of flame and smoke enough to go (복도·계단에 화염·연기가 없음).</summary>
        public bool WayClear;
    }

    public sealed class LookClue
    {
        public string Id, Label, Icon;
        /// <summary>The note's short name (문손잡이, 문틈...).</summary>
        public string Short;
        /// <summary>How long looking takes (the fire may grow meanwhile).</summary>
        public double Seconds;
        public string Small, Large;
        /// <summary>True when it tells where the fire is (the judgement counts these).</summary>
        public bool Locates;
        public string Fact;
    }

    public sealed class LookCard
    {
        public string Id, Text;
        /// <summary>fire, place, what, people, line (needed); vague, guess, noise, hangup (not).</summary>
        public string Kind;
        public LookCard(string id, string kind, string text) { Id = id; Kind = kind; Text = text; }
    }

    public sealed class LookReport
    {
        public bool Played, Fire, Place, What, People, Line, Guess, Hangup, Vague;
        public int Score => (Fire ? 1 : 0) + (Place ? 1 : 0) + (What ? 1 : 0) + (People ? 1 : 0) + (Line ? 1 : 0);
        public const int Full = 5;
        public JObject ToJson() => new JObject
        {
            ["played"] = Played, ["fire"] = Fire, ["place"] = Place, ["what"] = What, ["people"] = People, ["line"] = Line,
            ["guess"] = Guess, ["hangup"] = Hangup, ["vague"] = Vague, ["score"] = Score,
        };
        public static LookReport Read(JToken t)
        {
            var o = t as JObject;
            if (o == null) return new LookReport();
            bool B(string k) => o[k]?.Type == JTokenType.Boolean && (bool)o[k];
            return new LookReport { Played = B("played"), Fire = B("fire"), Place = B("place"), What = B("what"), People = B("people"), Line = B("line"), Guess = B("guess"), Hangup = B("hangup"), Vague = B("vague") };
        }
    }

    /// <summary>What the story gives the screen: the fire's start and what 정민 has at hand.</summary>
    public sealed class LookSetup
    {
        /// <summary>"corridor3" (3장: the parcel boxes in front of 304) — the only scenario so far.</summary>
        public string Scenario = "corridor3";
        /// <summary>The elder knocked as it started: the fire stays small while 정민 looks.</summary>
        public bool Knocked;
        /// <summary>1장: the boxes in front of the balcony partition were cleared (know partitionCleared).</summary>
        public bool PartitionCleared;
        public bool Towel, Tub, Bottle;
        public string Profile = "";
        /// <summary>"look" (the whole step) or "report" (only the 119 call, e.g. from the lobby after leaving).</summary>
        public string Mode = "look";
        /// <summary>Where the caller is for the report: home (302), next (301 after the partition), lobby.</summary>
        public string Caller = "home";
        /// <summary>The fire's size when the step opens on the call alone (report mode, from the corridor); look mode starts small.</summary>
        public string Size = "small";

        public static LookSetup FromOptions(JObject o)
        {
            o ??= new JObject();
            bool B(string k) => o[k]?.Type == JTokenType.Boolean && (bool)o[k];
            string S(string k, string d) => o[k]?.Type == JTokenType.String ? (string)o[k] : d;
            return new LookSetup
            {
                Scenario = S("scenario", "corridor3"), Knocked = B("knocked"), PartitionCleared = B("partitionCleared"),
                Towel = B("towel"), Tub = B("tub"), Bottle = B("bottle"), Profile = S("profile", ""), Mode = S("mode", "look"), Caller = S("caller", "home"),
                Size = S("size", "small") == "large" ? "large" : "small",
            };
        }

        public JObject ToJson() => new JObject
        {
            ["scenario"] = Scenario, ["knocked"] = Knocked, ["partitionCleared"] = PartitionCleared, ["towel"] = Towel, ["tub"] = Tub,
            ["bottle"] = Bottle, ["profile"] = Profile, ["mode"] = Mode, ["caller"] = Caller, ["size"] = Size,
        };
    }

    public sealed class LookFirstLogic
    {
        // ---------------------------------------------------------------- words

        public const string Kicker = "3장 · 불나면 살펴서 대피";
        public const string LookTitle = "문을 열기 전에 살핀다";
        public const string LookBody = "타는 냄새가 난다. 바로 뛰어나가기 전에, 불이 어디서 났는지부터 살핀다. 살피는 데도 시간이 든다.";
        public const string WhereTitle = "불은 어디에서 났을까?";
        public const string ActTitle = "어떻게 할까?";
        public const string SealTitle = "집 안에서 연기를 막는다";
        public const string PartitionTitle = "베란다 경량칸막이";
        public const string ReportTitle = "119 신고";
        public const string ReportBody = "\"119입니다. 무슨 일이세요?\" 전할 말을 고릅니다. 다 고르면 '말하기'.";
        public const string Neighbour = "303호 오서연";
        public const string Address = "연무동 한빛아파트 103동";
        public const int MaxCards = 6;
        /// <summary>Not knocked: after this many seconds of looking the fire reaches the box pile (the corridor's own 28 s, shorter: you are not watching it).</summary>
        public const double GrowAfter = 12;
        /// <summary>Boxes to move off the partition when it was not cleared in 1장.</summary>
        public const int Boxes = 3;
        /// <summary>Presses to break through (or seconds of holding at PushPerSecond).</summary>
        public const int Pushes = 5;
        public const double PushPerSecond = 0.45;

        public static readonly (string id, string label)[] WhereChoices =
        {
            ("home", "우리 집 안"),
            ("corridor", "현관 밖 복도 · 304호 쪽"),
            ("below", "아래층"),
        };

        public static string ActLabel(string id, string profile) => id switch
        {
            "leave" => profile == "wheelchair" ? "도움을 청해 이웃과 함께 계단으로 나간다" : "몸을 낮추고 계단으로 나간다 (나가며 문 닫기)",
            "stay" => "집 안에서 기다린다 (문틈 막기 · 창문 닫기 · 119)",
            "partition" => "베란다 경량칸막이를 넘어 옆집으로",
            "elevator" => "엘리베이터로 빨리 내려간다",
            _ => id,
        };

        public static readonly string[] ActIds = { "leave", "stay", "partition", "elevator" };

        public static readonly (string id, string label)[] SealSteps =
        {
            ("towel", "젖은 수건으로 현관 문틈 막기"),
            ("window", "창문 닫기"),
            ("away", "현관에서 먼 방으로 옮기고 방문 닫기"),
        };

        // ---------------------------------------------------------------- the 3장 scenario

        /// <summary>The clues of the 3장 corridor fire (the parcel boxes in front of 304, a candle knocked over).</summary>
        public static List<LookClue> Clues(LookSetup s)
        {
            bool deaf = s?.Profile == "deaf";
            return new List<LookClue>
            {
                new() { Id = "handle", Short = "문손잡이", Label = "손등으로 문손잡이 만져 보기", Icon = "✋", Seconds = 3, Locates = true, Fact = "fire_door",
                    Small = "손등을 대 본다. 미지근하다. 뜨겁지는 않다. 문 바로 바깥에서 타는 것은 아니다.",
                    Large = "손등을 대 본다. 따뜻하지만 손을 댈 수는 있다. 문 바로 바깥은 아니다." },
                new() { Id = "gap", Short = "문틈", Label = "문틈 살피기", Icon = "▤", Seconds = 3, Locates = true,
                    Small = "문틈으로 연기가 가늘게 스민다. 주황빛은 복도 끝 304호 쪽에서 흔들린다. 계단 쪽은 아직 연기가 옅다.",
                    Large = "문틈으로 회색 연기가 밀려든다. 복도 끝이 환하고, 계단 쪽까지 뿌옇다." },
                deaf
                    ? new() { Id = "sound", Short = "진동", Label = "현관문에 손 대고 진동 느끼기", Icon = "≋", Seconds = 4, Locates = true,
                        Small = "현관문에 손바닥을 대니 쿵쿵 울림이 전해진다. 복도 끝 쪽에서 문이 열리고 사람들이 오간다.",
                        Large = "울림이 커졌다. 복도 끝 쪽에서 무언가 무너지는 진동이 전해진다." }
                    : new() { Id = "sound", Short = "복도 소리", Label = "복도 소리 듣기", Icon = "≋", Seconds = 4, Locates = true,
                        Small = "타닥타닥 소리가 몇 집 건너에서 난다. 304호 쪽에서 \"불이야!\" 하는 목소리.",
                        Large = "불 소리가 커졌다. 복도 끝에서 무언가 무너지는 소리가 난다." },
                new() { Id = "home", Short = "집 안", Label = "집 안 둘러보기", Icon = "⌂", Seconds = 4, Locates = true,
                    Small = "부엌, 방, 베란다 어디에도 불빛이나 연기는 없다. 냄새는 현관 쪽에서만 난다.",
                    Large = "집 안은 괜찮다. 연기는 현관 쪽에서만 들어오고 있다." },
                new() { Id = "broadcast", Short = "방송", Label = deaf ? "인터폰 화면 보기 (관리사무소)" : "관리사무소 방송 기다리기", Icon = "◉", Seconds = 8, Locates = true,
                    Small = deaf ? DeafIntercom : "(비상 방송) \"3층 복도에서 불이 났다는 신고가 있습니다. 엘리베이터는 타지 마십시오.\"",
                    Large = deaf ? DeafIntercom : "(비상 방송) \"3층 복도에서 불이 났다는 신고가 있습니다. 엘리베이터는 타지 마십시오.\"" },
                new() { Id = "text", Short = "재난문자", Label = "휴대폰 재난문자 확인", Icon = "▣", Seconds = 2, Locates = false,
                    Small = "새 문자는 없다. 마지막 문자는 어제의 급수 안내다. 이 불은 문자로 오지 않는다. 직접 살펴야 한다.",
                    Large = "새 문자는 없다. 이 불은 문자로 오지 않는다. 직접 살펴야 한다." },
            };
        }

        const string DeafIntercom = "(인터폰 화면 글자) 3층 복도 화재 신고 · 엘리베이터 사용 금지";

        /// <summary>The 3장 truth: a fire outside our home, smoke at the gap; the way is clear while it is small.</summary>
        public static LookSituation Situation(string size) => new()
        {
            FireAt = "other", SmokeIn = true, WayClear = size != "large",
        };

        public const string TrueWhere = "corridor";

        // ---------------------------------------------------------------- judging (scenario-free)

        /// <summary>
        /// The 소방청 guide as a table: our home or another, smoke in or not, the corridor and stairs clear or not. The
        /// elevator is always corrected (fire_lift). Every verdict carries its reason and facts.
        /// </summary>
        public static LookFeedback Judge(LookSituation s, string act, bool partitionCleared = true, string profile = "")
        {
            s ??= new LookSituation();
            LookFeedback F(LookVerdict v, string title, string text, params string[] facts) => new() { Verdict = v, Title = title, Text = text, Facts = facts };
            if (act == "elevator")
                return new LookFeedback
                {
                    Verdict = LookVerdict.Corrected, Speaker = Neighbour, Title = "엘리베이터 말고 계단",
                    Text = ElevatorLine(profile) + " 다시 고른다.",
                    Facts = new[] { "fire_lift" },
                };
            string part = partitionCleared ? "" : " 다만 칸막이 앞 짐부터 옮겨야 한다.";
            if (s.FireAt == "home")
            {
                switch (act)
                {
                    case "leave":
                        return s.WayClear
                            ? F(LookVerdict.Good, "나갈 수 있으면 나간다", "우리 집에 불이 났고 현관으로 나갈 수 있다. 낮은 자세로 계단을 통해 내려가고, 나가면서 문을 닫는다.", "fire_own_home", "fire_lift")
                            : F(LookVerdict.Wrong, "현관 쪽이 막혔다", "불과 연기가 현관 쪽을 막고 있다. 그 길로 나가지 않고, 대피공간이나 경량칸막이로 피한다.", "fire_own_trapped");
                    case "partition":
                        return s.WayClear
                            ? F(LookVerdict.Caution, "나갈 수 있으면 계단이 먼저", "현관으로 나갈 수 있다면 계단으로 내려가는 것이 먼저다. 경량칸막이는 나갈 수 없을 때의 길이다." + part, "fire_own_home", "fire_own_trapped")
                            : F(LookVerdict.Good, "나갈 수 없을 때의 길", "현관으로 나갈 수 없다. 경량칸막이를 부수고 옆집으로 피한다." + part, "fire_own_trapped", "fire_partition");
                    default: // stay
                        return s.WayClear
                            ? F(LookVerdict.Wrong, "우리 집 불이면 버티지 않는다", "불이 난 곳이 우리 집이다. 나갈 수 있으면 낮은 자세로 계단을 통해 나간다.", "fire_own_home")
                            : F(LookVerdict.Caution, "피할 곳도 없을 때의 방법", "대피공간이나 경량칸막이가 없다면 불과 연기에서 먼 곳의 문을 닫고 틈을 막은 뒤 119에 알린다.", "fire_own_trapped", "fire_seal");
                }
            }
            if (!s.SmokeIn)
            {
                return act == "stay"
                    ? F(LookVerdict.Good, "연기가 없으면 집 안에서", "다른 집의 불이고 연기가 들어오지 않는다. 창문을 닫고 집 안에서 기다리며 119에 알린다.", "fire_stay")
                    : F(LookVerdict.Caution, "연기가 없으면 집 안이 더 안전할 수 있다", "다른 집의 불이고 우리 집에는 연기가 들어오지 않는다. 무조건 나가기보다 집 안에서 상황을 살피는 것이 먼저다." + (act == "partition" ? part : ""), "fire_stay", "fire_look");
            }
            if (s.WayClear)
            {
                switch (act)
                {
                    case "leave":
                        return F(LookVerdict.Good, "복도·계단이 비어 있다", "다른 집 불의 연기가 문틈으로 들어오지만, 복도와 계단 쪽은 아직 연기가 옅다. 몸을 낮추고 계단으로 나가며 문을 닫는다.", "fire_other_smoke", "fire_lift");
                    case "partition":
                        return F(LookVerdict.Caution, "지금은 계단이 열려 있다", "경량칸막이는 현관으로 나갈 수 없을 때의 길이다. 지금은 계단 쪽이 아직 비어 있다." + part, "fire_other_smoke", "fire_own_trapped");
                    default:
                        return F(LookVerdict.Caution, "나갈 수 있으면 나가는 것이 먼저", "연기가 들어오고 있고, 복도와 계단은 아직 지나갈 수 있다. 이럴 때는 대피가 먼저다. 집 안에서 버티는 것은 나갈 수 없을 때의 방법이다.", "fire_other_smoke", "fire_seal");
                }
            }
            switch (act)
            {
                case "leave":
                    return F(LookVerdict.Caution, "연기가 복도를 채웠다", "복도와 계단에 연기가 찼다. 이럴 때는 구조를 요청하는 것이 먼저다. 꼭 나가야 한다면 젖은 수건으로 코와 입을 막고 몸을 최대한 낮춘다.", "fire_other_smoke", "fire_big");
                case "partition":
                    return F(LookVerdict.Good, "현관 대신 베란다로", "현관 쪽으로는 나갈 수 없다. 경량칸막이를 넘어 연기에서 먼 옆집으로 피한다." + part, "fire_own_trapped", "fire_partition");
                default:
                    return F(LookVerdict.Good, "문을 닫고 틈을 막는다", "복도와 계단에 연기가 찼다. 문을 닫고 젖은 수건으로 틈을 막은 뒤, 119에 위치와 상황을 알리고 구조를 기다린다.", "fire_other_smoke", "fire_seal");
            }
        }

        /// <summary>The elevator correction: 서연 calls out (deaf mode: she runs out, takes 정민's arm and points at the stairs).</summary>
        public static string ElevatorLine(string profile) => profile == "deaf"
            ? "서연이 303호에서 뛰어나와 팔을 붙잡고 계단 쪽을 가리킨다. 휴대폰 화면에 \"엘리베이터 안 돼요! 불나면 계단으로!\""
            : "\"정민 씨, 엘리베이터는 안 돼요! 불이 나면 계단으로 가야 해요.\" 서연이 303호 문틈으로 외친다.";

        // ---------------------------------------------------------------- state

        public readonly LookSetup Setup;
        public LookPhase Phase { get; private set; }
        public string Size { get; private set; } = "small";
        public bool Grew { get; private set; }
        public double LookSeconds { get; private set; }
        public readonly List<string> Examined = new();
        /// <summary>What each clue showed when it was looked at (the fire as it was then).</summary>
        public readonly Dictionary<string, string> Notes = new();
        public string Where { get; private set; }
        public bool WhereRight => Where == TrueWhere;
        public string Act { get; private set; }
        public LookVerdict ActVerdict { get; private set; }
        public int Elevator { get; private set; }
        public readonly HashSet<string> Sealed = new();
        /// <summary>What the towel was wet with: tub, bottle, dry (no water), cloth (no towel; clothes wet with water), none.</summary>
        public string Wet { get; private set; } = "none";
        public int BoxesLeft { get; private set; }
        public double Push { get; private set; }
        public readonly List<string> Picked = new();
        public LookReport Report { get; private set; } = new();
        public LookFeedback Feedback { get; private set; }
        /// <summary>Every feedback in order (the screen's 돌아보기).</summary>
        public readonly List<LookFeedback> History = new();
        public readonly List<LookClue> AllClues;
        /// <summary>The report's cards: who is calling from where follows the choice (301 after the partition).</summary>
        public List<LookCard> Cards => _cards ??= ReportCards(Act == "partition" ? "next" : Setup.Caller, Size);
        List<LookCard> _cards;

        public event Action Changed;

        public LookFirstLogic(LookSetup setup)
        {
            Setup = setup ?? new LookSetup();
            AllClues = Clues(Setup);
            BoxesLeft = Setup.PartitionCleared ? 0 : Boxes;
            Phase = Setup.Mode == "report" ? LookPhase.Report : LookPhase.Look;
            if (Setup.Mode == "report") Size = Setup.Size == "large" ? "large" : "small";
        }

        public LookSituation Now => Situation(Size);
        public bool Played => Examined.Count > 0 || Where != null || Act != null || Report.Played;
        public LookClue Clue(string id) => AllClues.FirstOrDefault(c => c.Id == id);
        public int Located => Examined.Count(id => Clue(id)?.Locates == true);

        void Say(LookFeedback f)
        {
            Feedback = f;
            if (f != null) History.Add(f);
            Changed?.Invoke();
        }

        /// <summary>Look at one clue (again is free: it only repeats what was seen, with the fire as it is now).</summary>
        public LookFeedback Examine(string id)
        {
            var c = Clue(id);
            if (c == null || Phase != LookPhase.Look) return Feedback;
            bool again = Examined.Contains(id);
            string grewLine = null;
            if (!again)
            {
                Examined.Add(id);
                LookSeconds += c.Seconds;
                if (!Setup.Knocked && Size == "small" && LookSeconds >= GrowAfter)
                {
                    Size = "large"; Grew = true;
                    grewLine = "그사이 불이 상자 더미로 번졌다. 살피는 것도 짧게.";
                }
            }
            var text = Size == "large" ? c.Large : c.Small;
            if (!again) Notes[id] = text;
            if (grewLine != null) text += " " + grewLine;
            var f = new LookFeedback { Verdict = LookVerdict.Info, Title = c.Label, Text = text, Facts = c.Fact != null ? new[] { c.Fact } : Array.Empty<string>() };
            Say(f);
            return f;
        }

        /// <summary>Done looking: the judgement.</summary>
        public void ToWhere()
        {
            if (Phase != LookPhase.Look) return;
            Phase = LookPhase.Where;
            Changed?.Invoke();
        }

        public LookFeedback JudgeWhere(string answer)
        {
            if (Phase != LookPhase.Where || WhereChoices.All(w => w.id != answer)) return Feedback;
            Where = answer;
            Phase = LookPhase.Act;
            LookFeedback f;
            if (answer == TrueWhere)
                f = Located >= 2
                    ? new LookFeedback { Verdict = LookVerdict.Good, Title = "불은 현관 밖 복도", Text = $"단서 {Located}개가 같은 곳을 가리킨다. {SeenSentences()} 우리 집이 아니라 복도의 불이다.", Facts = new[] { "fire_look", "fire_door" } }
                    : new LookFeedback
                    {
                        Verdict = LookVerdict.Caution, Title = "맞았지만 단서가 적다",
                        Text = "복도의 불이 맞다. " + (Located == 0 ? "다만 아무 단서 없이 짐작했다." : "다만 단서 하나로는 짐작에 가깝다.") + " 손잡이와 문틈을 함께 보면 더 확실하다.",
                        Facts = new[] { "fire_look", "fire_door" },
                    };
            else
                f = new LookFeedback
                {
                    Verdict = LookVerdict.Wrong, Tag = "✕ 다시 보기", Title = "불은 현관 밖 복도였다",
                    Text = (answer == "home" ? "집 안에는 불빛도 연기도 없었다. " : "소리와 불빛은 같은 층, 304호 쪽에서 왔다. ") + "불은 현관 밖 복도, 304호 앞이다. 어디서 났는지에 따라 할 일이 달라진다.",
                    Facts = new[] { "fire_look" },
                };
            Say(f);
            return f;
        }

        /// <summary>What the clues actually looked at said about the place, one short sentence each (in the order looked).</summary>
        public string SeenSentences()
        {
            bool deaf = Setup.Profile == "deaf";
            var parts = new List<string>();
            foreach (var id in Examined)
            {
                switch (id)
                {
                    case "handle": parts.Add(Notes.TryGetValue(id, out var n) && n == Clue(id)?.Large ? "손잡이는 따뜻하지만 뜨겁지는 않다." : "손잡이는 뜨겁지 않다."); break;
                    case "gap": parts.Add("문틈의 연기와 불빛은 304호 쪽에서 온다."); break;
                    case "sound": parts.Add(deaf ? "진동도 복도 끝 쪽에서 온다." : "소리도 304호 쪽에서 난다."); break;
                    case "home": parts.Add("집 안에는 불빛도 연기도 없다."); break;
                    case "broadcast": parts.Add(deaf ? "인터폰 화면도 3층 복도 화재라고 한다." : "방송도 3층 복도의 불이라고 한다."); break;
                }
            }
            return string.Join(" ", parts);
        }

        public LookFeedback Choose(string act)
        {
            if (Phase != LookPhase.Act || !ActIds.Contains(act)) return Feedback;
            var f = Judge(Now, act, Setup.PartitionCleared, Setup.Profile);
            if (act == "elevator") { Elevator++; Say(f); return f; }
            Act = act; ActVerdict = f.Verdict; _cards = null;
            Phase = act == "leave" ? LookPhase.Done : act == "stay" ? LookPhase.Seal : LookPhase.Partition;
            Say(f);
            return f;
        }

        public bool SealDone => SealSteps.All(s => Sealed.Contains(s.id));

        public LookFeedback DoSeal(string step)
        {
            if (Phase != LookPhase.Seal || SealSteps.All(s => s.id != step) || Sealed.Contains(step)) return Feedback;
            Sealed.Add(step);
            LookFeedback f;
            switch (step)
            {
                case "towel":
                    if (Setup.Towel && Setup.Tub) { Wet = "tub"; f = Good("문틈을 막았다", "받아 둔 물에 수건을 적셔 현관 문틈에 꼭꼭 밀어 넣었다. 들어오던 연기가 줄었다.", "fire_seal"); }
                    else if (Setup.Towel && Setup.Bottle) { Wet = "bottle"; f = Good("문틈을 막았다", "생수 한 병으로 수건을 적셔 현관 문틈을 막았다. 들어오던 연기가 줄었다.", "fire_seal"); }
                    else if (Setup.Towel) { Wet = "dry"; f = new LookFeedback { Verdict = LookVerdict.Caution, Title = "마른 수건뿐", Text = "적실 물이 없어 마른 수건으로 틈을 막았다. 젖은 것보다 연기가 더 샌다.", Facts = new[] { "fire_seal" } }; }
                    else if (Setup.Tub || Setup.Bottle) { Wet = "cloth"; f = Good("옷가지로 막았다", "수건이 없어 두꺼운 옷을 물에 적셔 문틈을 막았다.", "fire_seal"); }
                    else { Wet = "dry"; f = new LookFeedback { Verdict = LookVerdict.Caution, Title = "마른 천뿐", Text = "수건도 물도 없어 마른 옷가지로 틈을 막았다. 연기가 조금씩 샌다.", Facts = new[] { "fire_seal" } }; }
                    break;
                case "window":
                    f = Good("창문을 닫았다", "거실과 방의 창문을 모두 닫았다. 밖의 연기가 들어오지 않게 한다.", "fire_stay");
                    break;
                default:
                    f = Good("현관에서 멀리", "현관에서 가장 먼 방으로 옮기고 방문도 닫았다.", "fire_seal");
                    break;
            }
            if (SealDone) { Phase = LookPhase.Report; f.Text += " 이제 119에 알린다."; }
            Say(f);
            return f;
        }

        static LookFeedback Good(string title, string text, params string[] facts) => new() { Verdict = LookVerdict.Good, Title = title, Text = text, Facts = facts };

        /// <summary>Move one box off the partition (only when it was not cleared in 1장).</summary>
        public LookFeedback MoveBox()
        {
            if (Phase != LookPhase.Partition || BoxesLeft <= 0) return Feedback;
            BoxesLeft--;
            var f = new LookFeedback
            {
                Verdict = BoxesLeft == 0 ? LookVerdict.Step : LookVerdict.Caution, Title = $"상자 옮기기 {Boxes - BoxesLeft}/{Boxes}",
                Text = BoxesLeft == 0 ? "칸막이 앞이 비었다. 이제 칸막이를 부순다." : "이사 때 상자가 칸막이를 막고 있다. 하나씩 옮긴다. 미리 치워 두었다면 바로 넘어갈 수 있었다.",
                Facts = new[] { "fire_partition_clear" },
            };
            Say(f);
            return f;
        }

        public bool PartitionOpen => Push >= 1;

        /// <summary>The partition's verb, after the fact's own words (발로 차서 부수고); wheelchair mode strikes it instead.</summary>
        public static string KickLabel(string profile) => profile == "wheelchair" ? "칸막이를 힘껏 쳐서 부순다" : "칸막이를 발로 차서 부순다";

        /// <summary>
        /// One press at the partition (1/Pushes), or a held press for dt seconds (PushPerSecond). A held press raises no
        /// Changed until the wall gives: the screen moves its own gauge each frame (Changed rebuilds the page).
        /// </summary>
        public LookFeedback PushPartition(double heldSeconds = -1)
        {
            if (Phase != LookPhase.Partition || BoxesLeft > 0 || PartitionOpen) return Feedback;
            Push = Math.Min(1, Push + (heldSeconds < 0 ? 1.0 / Pushes : heldSeconds * PushPerSecond));
            if (!PartitionOpen)
            {
                if (heldSeconds >= 0) return Feedback; // holding: no line and no rebuild every frame
                var f = new LookFeedback
                {
                    Verdict = LookVerdict.Step, Title = "칸막이 부수기",
                    Text = (Setup.Profile == "wheelchair" ? "힘껏 친다." : "발로 찬다.") + " 얇은 벽이 쿵 울리며 금이 간다.", Facts = new[] { "fire_partition" },
                };
                Say(f);
                return f;
            }
            Phase = LookPhase.Report;
            var done = new LookFeedback
            {
                Verdict = LookVerdict.Good, Title = "301호 베란다로",
                Text = "칸막이가 무너졌다. 301호 베란다로 건너가자 301호 아주머니가 놀란 얼굴로 창을 연다. \"이리 들어와요!\" 이제 119에 알린다.",
                Facts = new[] { "fire_partition" },
            };
            Say(done);
            return done;
        }

        // ---------------------------------------------------------------- the 119 call

        /// <summary>The 'what is burning' card says only what can be seen (no cause), and how big the fire is now.</summary>
        public static string WhatCard(string size) => size == "large"
            ? "304호 앞 복도의 상자 더미가 타고, 불길이 천장까지 닿았어요."
            : "304호 앞 복도의 택배 상자 더미가 타고 있어요.";

        public static List<LookCard> ReportCards(string caller, string size = "small") => new()
        {
            new LookCard("fire", "fire", "불이 났어요."),
            new LookCard("hurry", "noise", "빨리요, 빨리 와 주세요!"),
            new LookCard("place", "place", $"{Address} 3층 복도, 304호 앞이에요."),
            new LookCard("apt", "vague", "아파트예요."),
            new LookCard("what", "what", WhatCard(size)),
            new LookCard("guess", "guess", "전기 합선 같아요."),
            new LookCard("me", "people", caller == "next" ? "저는 옆집 301호로 피했어요. 3층에 사람이 남아 있을 수 있어요."
                : caller == "lobby" ? "저는 1층으로 나왔어요. 3층에 사람이 남아 있을 수 있어요."
                : "저는 302호 안에서 문틈을 막고 있어요. 3층에 사람이 남아 있을 수 있어요."),
            new LookCard("line", "line", "전화를 끊지 않고 안내를 들을게요."),
            new LookCard("hangup", "hangup", "다 말했으니 끊고 짐을 챙길게요."),
        };

        public LookCard Card(string id) => Cards.FirstOrDefault(c => c.Id == id);

        /// <summary>Adds a card to the message (or takes it back if it is already in).</summary>
        public void Toggle(string id)
        {
            if (Phase != LookPhase.Report || Card(id) == null) return;
            if (Picked.Contains(id)) Picked.Remove(id);
            else if (Picked.Count < MaxCards) Picked.Add(id);
            Changed?.Invoke();
        }

        public LookFeedback Submit()
        {
            if (Phase != LookPhase.Report || Picked.Count == 0) return Feedback;
            var kinds = Picked.Select(id => Card(id)?.Kind).ToList();
            var r = new LookReport
            {
                Played = true, Fire = kinds.Contains("fire"), Place = kinds.Contains("place"), What = kinds.Contains("what"), People = kinds.Contains("people"),
                Line = kinds.Contains("line") && !kinds.Contains("hangup"), Guess = kinds.Contains("guess"), Hangup = kinds.Contains("hangup"),
                Vague = kinds.Contains("vague") && !kinds.Contains("place"),
            };
            Report = r;
            Phase = LookPhase.Done;
            var lines = new List<string>();
            lines.Add(r.Place ? "✓ 장소: 동·층·호수까지 정확히 말했다." : r.Vague ? "✕ 장소: '아파트'만으로는 어디인지 모른다. 동과 층, 호수까지 말한다." : "✕ 장소를 말하지 않았다. 어디인지 알아야 출동할 수 있다.");
            lines.Add(r.What ? "✓ 무엇이 타는지, 불이 얼마나 큰지 말했다." : "✕ 무엇이 타는지(불의 규모와 상황)를 말하지 않았다.");
            lines.Add(r.People ? "✓ 내가 어디 있는지, 남은 사람이 있을 수 있는지 말했다." : "✕ 사람이 있는지, 내가 어디 있는지 말하지 않았다.");
            lines.Add(r.Line ? "✓ 전화를 끊지 않고 안내를 들었다." : r.Hangup ? "✕ 먼저 끊었다. 상황요원이 더 물을 수 있으니 전화는 끊지 않는다." : "✕ 전화를 끊지 않겠다는 말이 없었다.");
            if (!r.Fire) lines.Add("· '불이 났다'는 말부터 하면 더 빨리 전해진다.");
            if (r.Guess) lines.Add("· 원인 짐작은 말하지 않아도 된다. 본 것(불의 규모와 상황)만 말한다.");
            string op = r.Score >= LookReport.Full
                ? "\"103동 3층 복도, 확인했습니다. 소방차가 출동했습니다. 전화 끊지 마시고 기다리세요.\""
                : r.Place ? "\"확인했습니다. 소방차가 출동했습니다. 몇 가지만 더 여쭤볼게요.\"" : "\"주소가 어떻게 되세요? 동하고 호수요.\"";
            var f = new LookFeedback
            {
                Verdict = r.Score >= LookReport.Full ? LookVerdict.Good : r.Score >= 3 ? LookVerdict.Caution : LookVerdict.Wrong,
                Tag = r.Score >= 3 ? null : "✕ 다시 보기",
                Title = r.Score >= LookReport.Full ? "빠짐없이 전했다" : "빠진 말이 있다",
                Text = op + "\n" + string.Join("\n", lines),
                Facts = new[] { "fire_report", "fire_119" },
            };
            Say(f);
            return f;
        }

        // ---------------------------------------------------------------- result

        /// <summary>The verdict a whole run gets: the choice's verdict, unless the elevator had to be corrected first.</summary>
        public string Verdict => Act == null ? null : (ActVerdict == LookVerdict.Good ? "good" : ActVerdict == LookVerdict.Caution ? "caution" : "wrong");

        public JObject Result() => new JObject
        {
            ["played"] = Played, ["mode"] = Setup.Mode, ["clues"] = new JArray(Examined), ["seconds"] = LookSeconds, ["size"] = Size, ["grew"] = Grew,
            ["where"] = Where, ["whereOk"] = Where != null && WhereRight, ["located"] = Located,
            ["act"] = Act, ["verdict"] = Verdict, ["elevator"] = Elevator,
            ["seal"] = Act == "stay" ? new JObject { ["towel"] = Sealed.Contains("towel"), ["window"] = Sealed.Contains("window"), ["away"] = Sealed.Contains("away"), ["wet"] = Wet } : null,
            ["partition"] = Act == "partition" ? new JObject { ["cleared"] = Setup.PartitionCleared, ["boxes"] = Setup.PartitionCleared ? 0 : Boxes - BoxesLeft, ["open"] = PartitionOpen } : null,
            ["report"] = Report.Played ? Report.ToJson() : null,
        };

        /// <summary>What the story reads back (JSON from the screen, or {} when nothing was played).</summary>
        public sealed class Outcome
        {
            public bool Played;
            public string Act, Size = "small", Verdict, Where, Wet;
            public bool WhereOk, Grew, PartitionOpen;
            public int Elevator, Clues, Located;
            public LookReport Report = new();

            public static Outcome Read(JToken t)
            {
                var o = t as JObject ?? new JObject();
                string S(JToken x) => x?.Type == JTokenType.String ? (string)x : null;
                bool B(JToken x) => x?.Type == JTokenType.Boolean && (bool)x;
                int I(JToken x) => x != null && (x.Type == JTokenType.Integer || x.Type == JTokenType.Float) ? (int)(double)x : 0;
                var act = S(o["act"]);
                return new Outcome
                {
                    Played = B(o["played"]), Act = act == "leave" || act == "stay" || act == "partition" ? act : null,
                    Size = S(o["size"]) == "large" ? "large" : "small", Verdict = S(o["verdict"]), Where = S(o["where"]),
                    WhereOk = B(o["whereOk"]), Grew = B(o["grew"]), Elevator = I(o["elevator"]),
                    Clues = o["clues"] is JArray a ? a.Count : 0, Located = I(o["located"]),
                    Wet = S((o["seal"] as JObject)?["wet"]), PartitionOpen = B((o["partition"] as JObject)?["open"]),
                    Report = LookReport.Read(o["report"]),
                };
            }
        }
    }
}
