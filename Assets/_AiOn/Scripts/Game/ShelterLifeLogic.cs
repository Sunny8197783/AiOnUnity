// 4장 「임시 거처의 하루」 (competition upgrade; Story/ShelterLifeScene.cs). The first night and the next morning in the
// 생활문화센터 temporary stay, for whoever chose to move there: a boundary for the mat, a night of noise and light, the
// charging outlet, the morning's changed notice, the water line, a neighbour's child. Every choice gives a short
// answer and moves on (no punishment loop). The 마음 indicator is the existing tension stat read in calm words; the
// breathing screen ("breathe") lowers it a little. Pure: no UnityEngine, so the EditMode tests read it directly.
//
// The notices on the board are this scene's own (fictional, marked 가상); they are not facts. The sourced lines the
// scene shows are original facts: shelter_rules, shelter_share, people_order, mind_breath, mind_call.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One answer of a shelter-life choice: what it says back, how the 마음 moves, and whether it is worth keeping.</summary>
    public sealed class LifeChoice
    {
        public string Id, Label, Feedback, Short;
        /// <summary>Tension change (negative = calmer).</summary>
        public int Tension;
        /// <summary>"good" | "ok" | "rethink" (the review card's ✓ · ○).</summary>
        public string Kind;
        public LifeChoice(string id, string label, string feedback, string @short, int tension, string kind)
        {
            Id = id; Label = label; Feedback = feedback; Short = @short; Tension = tension; Kind = kind;
        }
    }

    /// <summary>What the shelter-life scene recorded (the "shelter_life" event's details).</summary>
    public sealed class ShelterLifeResult
    {
        public bool Played;
        public string Privacy, Sleep, Outlet, Notice, Water, Child, Play;
        public int Breaths;
        public double MindStart, MindEnd;

        public JObject ToJson()
        {
            var o = new JObject { ["played"] = Played, ["breaths"] = Breaths, ["mindStart"] = MindStart, ["mindEnd"] = MindEnd };
            void Put(string k, string v) { if (v != null) o[k] = v; }
            Put("privacy", Privacy); Put("sleep", Sleep); Put("outlet", Outlet); Put("notice", Notice); Put("water", Water); Put("child", Child); Put("play", Play);
            return o;
        }

        public static ShelterLifeResult Read(JToken t)
        {
            var o = t as JObject;
            if (o == null) return new ShelterLifeResult();
            string S(string k) => o[k]?.Type == JTokenType.String ? (string)o[k] : null;
            double D(string k) => o[k] != null && (o[k].Type == JTokenType.Integer || o[k].Type == JTokenType.Float) ? (double)o[k] : 0;
            return new ShelterLifeResult
            {
                Played = o["played"]?.Type == JTokenType.Boolean && (bool)o["played"],
                Privacy = S("privacy"), Sleep = S("sleep"), Outlet = S("outlet"), Notice = S("notice"), Water = S("water"), Child = S("child"), Play = S("play"),
                Breaths = (int)D("breaths"), MindStart = D("mindStart"), MindEnd = D("mindEnd"),
            };
        }
    }

    public static class ShelterLifeLogic
    {
        public const string Event = "shelter_life";
        /// <summary>The breathing screen (UI/Screens/BreatheScreen.cs).</summary>
        public const string Screen = "breathe";
        public const string ReviewTitle = "임시 거처의 하루";
        public const string ReviewMeta = "D+6 목 밤 ~ D+7 금 아침 · 생활문화센터 임시 거처";

        // The scene's clock (game minutes; the day is set by the scene).
        public const int ArriveMinute = 18 * 60 + 40, LightsOut = 22 * 60, NightMinute = 23 * 60 + 40, MorningMinute = 6 * 60 + 50, ChildMinute = 7 * 60 + 40;
        /// <summary>The night's sounds and lights after lights-out (a story beat, not a penalty).</summary>
        public const int NightTension = 8;

        // ---------------------------------------------------------------- 마음 (the tension stat in calm words)

        /// <summary>The HUD's 마음 word for a tension value, on the same bands as GameRules.StatWord("tension").</summary>
        public static string MindWord(double tension) => tension <= 33 ? "차분" : tension <= 66 ? "조금 불안" : "불안";

        /// <summary>
        /// Which way the 마음 moved, then the word: "마음이 조금 가라앉았다 · 조금 불안 → 차분", "마음이 조금 술렁인다 · 조금 불안",
        /// or "마음: 차분" when it did not move. The direction shows even when the band stays the same.
        /// </summary>
        public static string MindLine(double before, double after)
        {
            string a = MindWord(before), b = MindWord(after);
            string word = a == b ? b : $"{a} → {b}";
            return after < before - 0.01 ? $"마음이 조금 가라앉았다 · {word}" : after > before + 0.01 ? $"마음이 조금 술렁인다 · {word}" : $"마음: {word}";
        }

        public const int BreathDropPer = 4, BreathDropMax = 12;

        /// <summary>Tension taken off by the breaths finished on the breathing screen (each 4, at most 12).</summary>
        public static int BreathDrop(int breaths) => Math.Min(BreathDropMax, Math.Max(0, breaths) * BreathDropPer);

        // ---------------------------------------------------------------- the board (this scene's own notices, fictional)

        public static readonly string[] NightNotice =
        {
            "22:00 소등. 통로 유도등은 켜 둡니다.",
            "휴대폰 충전: 접수대 옆 멀티탭에서 한 사람 30분씩 차례로.",
            "식수: 아침 7시, 접수대 앞 배급대에서 한 사람 하루 두 병.",
            "통로와 출입구에는 짐을 두지 않습니다.",
        };

        /// <summary>The morning's board. The first change is the one the morning's errand needs: where the water is handed out.</summary>
        public static readonly string[] MorningNotice =
        {
            "[바뀜] 오늘 식수는 급수차가 출입구 B(차량 진출입로) 앞에서 아침 7시에 나눠 줍니다. 접수대 배급대는 쉽니다.",
            "[바뀜] 발전기 연료를 아끼려고 충전은 오전 9~11시, 오후 3~5시에만 합니다.",
            "[바뀜] 세면장은 구역별로 나눠 씁니다. 7시 1구역, 7시 반 2구역.",
        };

        public const string NightNoticeTitle = "임시 거처 안내 · D+6 목 (가상)";
        public const string MorningNoticeTitle = "임시 거처 안내 · D+7 금 (가상) — 오늘 바뀐 것";
        /// <summary>The point where the water is handed out in the morning (the shelter level's 출입구 B).</summary>
        public const string WaterPoint = "fromRamp";
        /// <summary>The manager's answer to someone who comes to the reception for water before reading the morning's board.</summary>
        public const string RuleFromStaff = "\"오늘 물은 급수차가 출입구 B 앞에서 나눠 드려요. 바뀐 안내는 게시판에 붙여 놨어요.\"";

        // ---------------------------------------------------------------- the choices

        public static readonly LifeChoice[] Privacy =
        {
            new("bags", "가방과 신발을 돗자리 끝에 두어 경계를 만든다", "옆 가족도 짐을 자기 쪽으로 당겨 놓았다. 말없이도 서로의 자리가 생겼다.", "가방과 신발로 돗자리 경계를 만들었다", -4, "good"),
            new("ask", "옆 가족에게 \"짐을 이쪽에 둬도 될까요?\" 먼저 묻는다", "\"그럼요. 저희는 아이 신발을 이쪽에 둘게요.\" 한마디로 경계가 정해졌다.", "옆 가족에게 먼저 물어 경계를 정했다", -5, "good"),
            new("screen", "접수대에서 종이 칸막이를 빌려 세운다", "허리 높이 칸막이 하나로 오가는 시선이 한결 덜 신경 쓰인다. 남은 칸막이가 마침 하나 있었다.", "종이 칸막이를 빌려 세웠다", -6, "good"),
            new("none", "신경 쓰지 않고 그냥 눕는다", "오가는 발소리가 머리맡을 스친다. 짐으로라도 경계를 만들어 두면 덜 신경 쓰일 것 같다.", "경계 없이 그냥 누웠다", 3, "rethink"),
        };

        public static readonly LifeChoice[] Sleep =
        {
            new("towel", "수건을 눈 위에 얹고 벽 쪽으로 돌아눕는다", "불빛이 한결 덜 보인다. 코 고는 소리 사이로 잠이 조금씩 온다.", "수건으로 불빛을 가리고 잠을 청했다", -4, "good"),
            new("ask", "\"화면 밝기 조금만 줄여 주실 수 있을까요?\" 작게 부탁한다", "\"아, 죄송해요.\" 화면이 어두워졌다. 건너편 사람도 잠을 못 이루던 참이었다.", "화면 밝기를 줄여 달라고 작게 부탁했다", -5, "good"),
            new("breathe", "누운 채로 천천히 숨을 고른다", "숨을 고르는 사이 어깨의 힘이 조금 빠졌다.", "누운 채로 숨을 고르며 잠을 청했다", 0, "good"),
            new("phone", "나도 휴대폰으로 새 소식을 찾아본다", "새 소식은 없고 배터리만 줄었다. 화면을 끄고 나니 눈이 더 말똥말똥하다.", "휴대폰을 보다 늦게 잠들었다", 4, "rethink"),
        };

        /// <summary>Deaf profile: the same night told without sounds.</summary>
        public static readonly LifeChoice[] SleepDeaf =
        {
            new("towel", "수건을 눈 위에 얹고 벽 쪽으로 돌아눕는다", "불빛이 한결 덜 보인다. 몇 번 뒤척이다 잠이 조금씩 온다.", "수건으로 불빛을 가리고 잠을 청했다", -4, "good"),
            new("ask", "건너편 사람에게 손짓으로 화면을 가리켜 밝기를 줄여 달라고 부탁한다", "고개를 끄덕이더니 화면이 어두워졌다. 그 사람도 잠을 못 이루던 참이었다.", "손짓으로 화면 밝기를 줄여 달라고 부탁했다", -5, "good"),
            Sleep[2], Sleep[3],
        };

        public static readonly LifeChoice[] Outlet =
        {
            new("turn", "내 차례에 30분만 꽂는다", "안내대로 30분을 채우고 다음 사람에게 자리를 넘겼다.", "충전은 내 차례에 30분만 했다", -2, "ok"),
            new("yield", "옆 할머니께 먼저 꽂으시라고 차례를 양보한다", "할머니가 \"아들한테 문자 한 통만 보내면 돼요\" 하신다. 내 차례는 그다음이다.", "충전 차례를 할머니께 먼저 양보했다", -3, "good"),
            new("double", "빈 구멍에 충전기를 하나 더 꽂는다", "관리자가 다가와 \"한 사람 하나씩이에요\" 하고 안내했다. 충전기를 뽑아 줄 뒤로 갔다.", "충전기를 하나 더 꽂았다가 안내를 받았다", 3, "rethink"),
        };

        /// <summary>Battery points each outlet answer gives back (game numbers).</summary>
        public static int Charge(string outlet) => outlet == "turn" ? 20 : outlet == "yield" ? 12 : outlet == "double" ? 8 : 0;

        public static readonly LifeChoice[] Water =
        {
            new("own", "내 몫 두 병을 받는다", "물 두 병. 오늘 하루치다.", "물은 내 몫 두 병을 받았다", 0, "ok"),
            new("carry", "할아버지께 \"물 같이 받아다 드릴까요?\" 하고 여쭙는다", "할아버지: \"그래 주면 고맙지. 나는 한 병이면 돼요.\" 급수차 직원이 명부에서 할아버지 이름을 확인하고 한 병을 내주었다.", "할아버지께 여쭙고 물을 받아다 드렸다", -2, "good"),
        };

        public static readonly LifeChoice[] Child =
        {
            new("watch", "\"그럼요, 다녀오세요.\" 지우를 봐 준다", "엄마가 \"20분이면 돼요\" 하고 급수 줄로 갔다.", "지우를 20분 동안 봐 주었다", -2, "good"),
            new("staff", "접수대 옆 아이 돌봄 자원봉사 자리를 알려 드린다", "\"아, 거기가 있었네요.\" 엄마는 지우 손을 잡고 접수대로 갔다.", "아이 돌봄 자원봉사 자리를 알려 드렸다", 0, "good"),
            new("no", "\"지금은 제가 좀 어려워요\" 하고 정중히 말한다", "\"괜찮아요, 물어본 거예요.\" 엄마는 다른 이웃에게 다시 부탁했다. 거절해도 괜찮다.", "아이 돌봄 부탁은 정중히 거절했다", 0, "ok"),
        };

        public static readonly LifeChoice[] Play =
        {
            new("radio", "라디오에서 어린이 방송을 찾아 같이 듣는다", "지우가 노래를 따라 흥얼거린다. 어깨가 조금 내려갔다.", "라디오로 어린이 방송을 같이 들었다", -3, "good"),
            new("fold", "종이를 접어 비행기를 만들어 준다", "종이비행기가 기둥까지 날아갔다. 지우가 처음으로 웃었다.", "종이비행기를 접어 주었다", -3, "good"),
            new("breathe", "\"같이 숨 쉬어 볼까?\" 지우와 함께 숨을 고른다", "지우가 숫자를 세며 따라 했다. \"이제 안 무서워요.\"", "지우와 함께 숨을 골랐다", 0, "good"),
        };

        /// <summary>Deaf profile: the radio is for 지우 to listen to; 정민 watches her (no listening, no song).</summary>
        public static readonly LifeChoice[] PlayDeaf =
        {
            new("radio", "라디오를 켜서 지우가 어린이 방송을 듣게 해 준다", "지우가 라디오를 귀에 대고 고개를 까딱인다. 어깨가 조금 내려갔다.", "지우에게 라디오로 어린이 방송을 틀어 주었다", -3, "good"),
            Play[1], Play[2],
        };

        /// <summary>Breathing with the child stopped before a breath was done: 정민 stayed beside her (no choice of its own).</summary>
        public static readonly LifeChoice PlayStay = new("stay", "지우 곁에 앉아 있어 준다", "지우가 담요를 끌어안고 정민 옆에 기대 앉았다.", "지우 곁에 앉아 있어 주었다", 0, "good");

        public static LifeChoice Find(IEnumerable<LifeChoice> set, string id) => id == null ? null : set.FirstOrDefault(c => c.Id == id);

        static IEnumerable<LifeChoice> All => Privacy.Concat(Sleep).Concat(SleepDeaf).Concat(Outlet).Concat(Water).Concat(Child).Concat(Play).Concat(PlayDeaf).Append(PlayStay);

        /// <summary>Every line the scene can show from these tables (the tests check the wording).</summary>
        public static IEnumerable<string> Texts => All.SelectMany(c => new[] { c.Label, c.Feedback, c.Short })
            .Concat(NightNotice).Concat(MorningNotice).Concat(new[] { NightNoticeTitle, MorningNoticeTitle, RuleFromStaff, ReviewTitle, ReviewMeta });

        // ---------------------------------------------------------------- the review card

        static string Mark(string kind) => kind == "good" ? "<color=#3DC7B5>✓</color> " : kind == "rethink" ? "<color=#F2B33D>○</color> " : "· ";

        /// <summary>The notice row: read the changed board first, heard it from the staff, or missed it.</summary>
        public static (string Text, string Kind) NoticeRow(string notice) => notice == "read"
            ? ("바뀐 안내를 게시판에서 먼저 읽고, 물 받는 곳(출입구 B)으로 곧장 갔다", "good")
            : notice == "staff" ? ("물 받는 곳이 바뀐 것을 접수대에서 듣고서야 알았다. 안내는 날마다 바뀐다", "rethink")
            : ("바뀐 안내는 보지 못했다. 안내는 날마다 바뀐다", "rethink");

        public static readonly string[] FactIds = { "shelter_share", "people_order", "mind_breath", "mind_call" };

        /// <summary>The review card's lines (pure over the result and the facts); deaf: the night and the play as that profile was told them.</summary>
        public static List<string> ReviewLines(ShelterLifeResult r, Func<string, string> fact, Func<string, string> source, bool deaf = false)
        {
            var lines = new List<string> { "임시 거처에서는 돗자리 한 장이 집이다. 하루 동안 무엇을 했는지 돌아봅니다." };
            void Row(LifeChoice c) { if (c != null) lines.Add(Mark(c.Kind) + c.Short); }
            Row(Find(Privacy, r.Privacy));
            Row(Find(Outlet, r.Outlet));
            Row(deaf ? Find(SleepDeaf, r.Sleep) : Find(Sleep, r.Sleep));
            if (r.Notice != null || r.Water != null) { var (t, k) = NoticeRow(r.Notice); lines.Add(Mark(k) + t); }
            Row(Find(Water, r.Water));
            Row(Find(Child, r.Child));
            Row(Find(deaf ? PlayDeaf : Play, r.Play) ?? (r.Play == PlayStay.Id ? PlayStay : null));
            lines.Add(MindLine(r.MindStart, r.MindEnd) + (r.Breaths > 0 ? $" · 숨 고르기 {r.Breaths}번" : ""));
            foreach (var id in FactIds)
            {
                var f = fact?.Invoke(id);
                if (string.IsNullOrEmpty(f)) continue;
                var s = source?.Invoke(id);
                lines.Add($"<size=18><color=#94a3b8>{f}{(string.IsNullOrEmpty(s) ? "" : " — " + s)}</color></size>");
            }
            lines.Add("<size=18><color=#94a3b8>[게임 속 안내문] 게시판의 시간과 규칙은 이 장면을 위해 지어낸 것입니다. 실제 대피소의 안내는 관리자에게 확인합니다.</color></size>");
            return lines;
        }
    }

    /// <summary>
    /// The breathing screen's rules (BreatheScreen). Hold (Space, the mouse, pad A) while breathing in and let go to
    /// breathe out; the guide counts 4 s each way. A breath counts once the hold lasted MinHold (wider in the experience
    /// modes); letting go sooner only shows a hint. The pace never slows for anyone: the windows widen instead. Toggle
    /// mode (no holding): one press starts a guided rhythm that runs by itself. Three breaths finish it.
    /// These are game numbers, not a sourced instruction.
    /// </summary>
    public sealed class BreathLogic
    {
        public const string Ready = "ready", In = "in", Out = "out", Done = "done";
        public const double InSeconds = 4, OutSeconds = 4, MinHold = 3, WideMinHold = 2;
        public const int Target = 3;

        public bool Toggle, Wide;
        public string Phase { get; private set; } = Ready;
        /// <summary>Seconds into the phase.</summary>
        public double T { get; private set; }
        public int Breaths { get; private set; }
        /// <summary>Holds let go before MinHold (a hint each time, never a penalty).</summary>
        public int Short { get; private set; }
        public string Hint { get; private set; }

        public double Min => Wide ? WideMinHold : MinHold;
        public bool Finished => Phase == Done;

        /// <summary>Toggle mode: the one press that starts the guided rhythm.</summary>
        public void Start()
        {
            if (Toggle && Phase == Ready) Go(In);
        }

        void Go(string phase) { Phase = phase; T = 0; }

        /// <summary>Switches between holding and the guided rhythm; the breaths so far stay, the one under way starts again.</summary>
        public void SetMode(bool toggle)
        {
            if (Phase == Done) return;
            Toggle = toggle;
            Hint = null;
            Go(Ready);
        }

        void Count(bool next)
        {
            Breaths++;
            Hint = null;
            if (Breaths >= Target) { Go(Done); return; }
            Go(next ? In : Ready);
        }

        public void Tick(double dt, bool held)
        {
            if (Phase == Done || dt < 0) return;
            if (Toggle)
            {
                if (Phase == Ready) return;
                T += dt;
                if (Phase == In && T >= InSeconds) Go(Out);
                else if (Phase == Out && T >= OutSeconds) Count(true);
                return;
            }
            switch (Phase)
            {
                case Ready:
                    if (held) { Hint = null; Go(In); }
                    break;
                case In:
                    T += dt;
                    if (!held)
                    {
                        if (T >= Min) Go(Out);
                        else { Short++; Hint = "조금 더 길게 들이쉬어 보세요."; Go(Ready); }
                    }
                    else if (T > InSeconds + 2) Hint = "이제 손을 떼고 천천히 내쉬어요.";
                    break;
                case Out:
                    T += dt;
                    if (held && T < Min) Hint = "천천히, 조금 더 내쉬어요.";
                    else if (held) Count(true);
                    else if (T >= OutSeconds) Count(false);
                    break;
            }
        }

        /// <summary>0..1: how full the breath is (the ring's size, or its fill under reduce-motion).</summary>
        public double Fill => Phase == In ? Math.Min(1, T / InSeconds) : Phase == Out ? Math.Max(0, 1 - T / OutSeconds) : 0;

        /// <summary>The count shown inside the ring: 1..4 during a breath, 0 otherwise.</summary>
        public int Count4 => Phase == In || Phase == Out ? Math.Min(4, (int)Math.Floor(T) + 1) : 0;

        /// <summary>The cue word.</summary>
        public string Cue => Phase switch
        {
            In => T >= InSeconds ? "이제 내쉬기" : "들이쉬기",
            Out => "내쉬기",
            Done => "잘했어요",
            _ => Toggle ? "시작을 누르면 함께 셉니다" : "누르고 있는 동안 들이쉬기",
        };
    }
}
