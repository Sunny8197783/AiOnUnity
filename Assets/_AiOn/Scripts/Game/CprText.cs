// The CPR minigame's words (competition upgrade). Kept apart from the screen so tests can read them: no "2분마다 교대",
// no unverified numbers; the safety sentences themselves are facts-ext ids (shown with their source), these are the
// scene's lines and short feedback words.
using System.Collections.Generic;

namespace AiOn.Game
{
    public static class CprText
    {
        /// <summary>The people pointed at by name: the shelter manager (119), the volunteer (AED), the neighbour who takes over.</summary>
        public const string Caller = "관리자님", AedPerson = "노란 조끼 입은 분", SwapPerson = "파란 점퍼 입은 분";

        public const string Kicker = "대피소 · 응급 상황";

        public static readonly (string id, string label)[] CheckChoices =
        {
            ("tap", "어깨를 가볍게 두드리며 \"괜찮으세요?\" 하고 부른다"),
            ("lift", "몸을 흔들어 일으켜 앉힌다"),
        };

        public static readonly (string id, string label)[] CallChoices =
        {
            ("vague", "\"누가 119 좀 불러 주세요!\""),
            ("self", "직접 신고하러 접수대로 뛰어간다"),
            ("named", $"\"{Caller}, 119에 신고해 주세요! {AedPerson}은 AED 가져다주세요!\""),
        };

        public static readonly (string id, string label)[] BreathChoices =
        {
            ("none", "숨을 쉬지 않는다 · 숨이 이상하다"),
            ("breathing", "헐떡이는 소리가 나니 숨을 쉬는 것 같다"),
        };

        /// <summary>The story's line as he collapses (CprScene), then the first page's look at him (not the same sentence twice).</summary>
        public const string CollapseLine = "옆 돗자리의 할아버지가 말을 하다 말고 뒤로 스르르 쓰러졌다.";
        public const string CheckBody = "할아버지가 돗자리에 반듯이 누워 있다. 바닥은 평평하고 주변은 안전하다.";
        public const string CallBody = "대답이 없다. 가까이에 대피소 관리자, 노란 조끼의 자원봉사자, 파란 점퍼 입은 이웃이 있다.";
        public const string BreathBody = "관리자가 휴대폰을 스피커로 켰다. 119 상황요원: \"숨을 쉬는지 얼굴과 가슴을 봐 주세요.\" 가슴이 오르내리지 않는다. 몇 초에 한 번 '컥' 하고 헐떡일 뿐이다.";
        public const string CompressBody = "119 상황요원: \"가슴 한가운데를 세게, 빠르게 누르세요. 제가 박자를 세어 드릴게요.\"";
        public const string HandoverBody = "구급대원 두 명이 들어왔다. \"수고하셨습니다. 이제 저희가 이어서 하겠습니다.\" 할아버지는 들것에 실려 구급차로 향했다.";
        public const string ControlsLine = "Space · 클릭 · 화면 터치로 박자에 맞춰 누릅니다. 누르는 시간만큼 깊이 눌리고, 다음 박자 전에 손을 끝까지 떼야 가슴이 다시 올라옵니다.";
        public const string OneButtonLine = "원버튼: 박자만 맞추면 됩니다. 길게 누르면 교대를 요청합니다.";
        public const string WideLabel = "판정 넓게 (박자는 그대로 분당 110회)";
        public const string OneButtonLabel = "원버튼 (박자만 판정)";
        public const string VibrateLabel = "진동 메트로놈 (게임패드)";
        public const string GaugeLabel = "압박 품질 · 내가 느끼는 것";
        public const string HintSwap = "1분이 넘었습니다. 지치기 전에 교대를 요청할 수 있습니다.";
        public const string HintPress = "Space · 클릭 · 터치로 박자에 맞춰 계속 누르세요.";
        public const string SwapButton = "교대 요청 · " + SwapPerson + " (E)";
        public const string VagueSwapButton = "\"누가 좀 바꿔 주세요\" (Q)";
        public const string TakeBackButton = "다시 이어 누르기 (E)";
        /// <summary>장면 바로가기 only, after the shock: straight to the paramedics and the debrief.</summary>
        public const string SkipButton = "구급대 도착까지 건너뛰기 (장면 바로가기)";
        /// <summary>Prepared runs only (CprLogic.Demo): the AED and the fatigue come sooner than in the story, and the page says so.</summary>
        public const string DemoAedNote = "시연용: AED 도착과 지치는 시간을 줄였습니다.";
        /// <summary>The debrief's last line in a prepared run: the times it shows are the shortened ones, not the fact's.</summary>
        public const string DemoDebriefNote = "이 판은 시연용으로 AED 도착과 지치는 시간을 줄였습니다.";
        public const string DebriefTitle = "돌아보기 · 압박의 질";
        public const string ChartTrue = "실제 압박의 질";
        public const string ChartGauge = "그때 게이지가 보여 준 것";
        public const string PracticeNote = "이 게임은 실습을 대신하지 않습니다. 실제 손 위치와 깊이는 실습 교육에서 마네킹으로 익힙니다.";

        static readonly Dictionary<string, string> Lines = new()
        {
            ["checked"] = "대답이 없다. 몸이 축 늘어져 있다.",
            ["check_wrong"] = "먼저 어깨를 가볍게 두드리며 \"괜찮으세요?\" 하고 불러 봅니다.",
            ["call_named"] = $"관리자가 바로 119에 전화를 건다. {AedPerson}이 AED를 가지러 뛰어간다.",
            ["call_vague"] = "사람들이 서로 얼굴만 본다. 누가 할지 정해지지 않았다.",
            ["call_self"] = "그러면 할아버지 곁이 빈다. 주변 사람 가운데 한 명을 정확히 지목해 부탁합니다.",
            ["breath_ok"] = "숨을 쉬지 않는다. 바로 가슴압박을 시작한다.",
            ["breath_late"] = "10초를 넘겼다. 망설이지 말고 바로 압박을 시작한다.",
            ["breath_timeout"] = "10초가 지났다. 망설이지 말고 바로 압박을 시작한다.",
            ["breath_wrong"] = "헐떡이는 숨은 정상 호흡이 아니다. 바로 압박을 시작한다.",
            ["wait"] = "상황요원이 세는 박자에 맞춰 시작합니다.",
            ["good"] = "좋아요",
            ["bit_early"] = "조금 빨라요",
            ["bit_late"] = "조금 늦어요",
            ["early"] = "너무 빨라요 · 박자에 맞추세요",
            ["late"] = "늦었어요 · 박자에 맞추세요",
            ["deeper"] = "더 깊게 · 조금 더 길게 누르세요",
            ["lean"] = "끝까지 떼세요 · 가슴이 올라오게",
            ["miss"] = "멈추지 말고 계속 누르세요",
            ["rush"] = "한 박자에 한 번만",
            ["swap"] = $"\"{SwapPerson}, 교대해 주세요!\" 손을 바꾼다.",
            ["swap_vague"] = "\"누가 좀 바꿔 주세요\" · 다들 머뭇거린다. 한 사람을 지목해야 한다.",
            ["neighbour"] = $"{SwapPerson}이 이어서 누른다. 숨을 고른다.",
            ["take_back"] = "\"이제 바꿔 주세요!\" · 바로 이어서 누른다.",
            ["aed_arrive"] = $"{AedPerson}이 AED를 가져왔다. \"제가 누를게요, 전원을 켜 주세요!\"",
            ["aed_pads"] = "음성 안내: \"환자의 맨가슴에 패드를 붙이세요.\"",
            ["aed_auto_power"] = "자원봉사자가 대신 전원을 켰다. 음성 안내: \"패드를 붙이세요.\"",
            ["aed_auto_pads"] = "자원봉사자가 대신 패드를 붙였다. 음성 안내: \"심장 리듬을 분석 중입니다. 환자에게서 떨어지세요.\"",
            ["aed_pulled"] = "자원봉사자: \"손 떼세요!\" 손을 거두고 분석이 끝나기를 기다린다.",
            ["step_wait"] = "잠깐 손을 멈추고 AED 안내를 보세요. 압박은 자원봉사자가 이어 갑니다.",
            ["shock_wait"] = "모두 물러나 있습니다. 손을 멈췄다가 충격 버튼을 누르세요.",
            ["aed_analyze"] = "음성 안내: \"심장 리듬을 분석 중입니다. 환자에게서 떨어지세요.\"",
            ["aed_touch"] = "손이 닿았다. 분석을 처음부터 다시 한다. 손을 떼고 기다린다.",
            ["aed_shock_ready"] = "음성 안내: \"충격이 필요합니다. 모두 물러나세요. 깜빡이는 버튼을 누르세요.\"",
            ["aed_resume"] = "음성 안내: \"즉시 가슴압박을 다시 시작하세요.\"",
            ["aed_auto_shock"] = "자원봉사자가 충격 버튼을 눌렀다. 음성 안내: \"즉시 가슴압박을 다시 시작하세요.\"",
            ["handover"] = "구급대원이 도착했다.",
        };

        /// <summary>The line for a CprLogic.Feedback id ("" for none).</summary>
        public static string Feedback(string id) => id != null && Lines.TryGetValue(id, out var s) ? s : "";

        public static IEnumerable<string> AllLines()
        {
            foreach (var v in Lines.Values) yield return v;
            foreach (var c in CheckChoices) yield return c.label;
            foreach (var c in CallChoices) yield return c.label;
            foreach (var c in BreathChoices) yield return c.label;
            yield return CollapseLine; yield return CheckBody; yield return CallBody; yield return BreathBody; yield return CompressBody; yield return HandoverBody;
            yield return ControlsLine; yield return OneButtonLine; yield return HintSwap; yield return HintPress; yield return PracticeNote;
            yield return DemoAedNote; yield return DemoDebriefNote;
        }

        /// <summary>
        /// The fact (facts-ext id) behind a step's feedback, shown with it once the player has picked; null for none. A fact
        /// box beside the choices before the pick read as the right choice, so a step's answer comes only after it.
        /// </summary>
        public static string FactFor(string feedbackId) => feedbackId switch
        {
            "checked" or "check_wrong" => "cpr_check",
            "call_named" or "call_vague" or "call_self" => "cpr_point",
            "breath_late" or "breath_timeout" => "cpr_breath",
            "breath_ok" or "breath_wrong" => "cpr_gasp",
            _ => null,
        };

        /// <summary>
        /// A fact a step page shows before the pick: only how to do the step (the breathing check's 10 seconds), never which
        /// choice is right (cpr_gasp answers the breathing check; cpr_check and cpr_point answer theirs). Null for none.
        /// </summary>
        public static string BeforeFact(CprPhase p) => p == CprPhase.Breath ? "cpr_breath" : null;

        /// <summary>The facts on a step page, top to bottom: the one behind the feedback shown (if any), then the step's own.</summary>
        public static List<string> StepFacts(CprPhase p, string feedbackId)
        {
            var ids = new List<string>();
            var told = FactFor(feedbackId);
            if (told != null) ids.Add(told);
            var own = BeforeFact(p);
            if (own != null && !ids.Contains(own)) ids.Add(own);
            return ids;
        }

        /// <summary>The page title for a phase.</summary>
        public static string Title(CprPhase p) => p switch
        {
            CprPhase.Check => "쓰러진 사람",
            CprPhase.Call => "도움 요청",
            CprPhase.Breath => "호흡 확인 · 10초 이내",
            CprPhase.Compress => "가슴압박",
            CprPhase.AedPower or CprPhase.AedPads or CprPhase.AedAnalyze or CprPhase.AedShock => "자동심장충격기(AED)",
            _ => "구급대 인계",
        };

        /// <summary>The AED step button's label (null when the phase has none).</summary>
        public static string AedButton(CprPhase p) => p switch
        {
            CprPhase.AedPower => "전원 켜기 (Enter · 멈췄다가 Space)",
            CprPhase.AedPads => "패드 붙이기 (Enter · 멈췄다가 Space)",
            CprPhase.AedShock => "충격 버튼 (Enter · 멈췄다가 Space)",
            _ => null,
        };
    }
}
