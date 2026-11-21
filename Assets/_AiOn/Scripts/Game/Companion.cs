// 도우미 모드 (competition upgrade, asymmetric companion): a second person at the same PC (a parent, a teacher, a judge
// at the booth) helps without taking the controls. One setting turns it on; F1 (or the small 도우미 button) shows a hint
// for what is on screen now, and at each moment of judgement (DecisionMoment) a "함께 생각해 보기" card stops the game
// with a "what would you do?" question and two prompts for the pair to talk about before the player chooses. The words
// here are open questions and control tips, never new safety claims: a hint's safety line is a checked fact (by id).
// Pure, no UnityEngine; the view is UI/CompanionPanel.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Story;

namespace AiOn.Game
{
    /// <summary>A "함께 생각해 보기" card: the question on screen and what to talk about.</summary>
    public sealed class CompanionTalk
    {
        public string Title = Companion.DiscussTitle, Question, Note = Companion.DiscussNote, Topic;
        public List<string> Prompts = new();
        /// <summary>The choices the player will see, for the helper to read out (not to pick).</summary>
        public List<string> Choices = new();
    }

    /// <summary>An F1 hint: a heading, a few lines and (maybe) one checked fact.</summary>
    public sealed class CompanionHint
    {
        public string Title, Topic, FactId;
        public List<string> Lines = new();
    }

    public static class Companion
    {
        public const string SettingKey = "companion";
        public const string Label = "도우미 모드";
        public const string Sub = "옆 사람과 F1 힌트 · 함께 생각하기";
        public const string HintKey = "F1";
        public const string ChipText = "도우미  <b>F1</b> 힌트";
        public const string DiscussTitle = "함께 생각해 보기";
        public const string DiscussNote = "도우미에게: 답을 먼저 알려 주기보다, 무엇을 고르려는지와 그 이유를 들어 주세요.";
        public const string FirstPrompt = "나라면 어떻게 할까요? 고르기 전에 먼저 말해 보세요.";
        public const string ContinueLabel = "이야기를 마쳤어요 · 고르기 (Enter)";
        public const string HintTitle = "도우미 힌트";
        public const string NothingToDo = "지금은 정해진 할 일이 없어요. 주변을 둘러보며 E로 살펴보세요.";

        /// <summary>The setting in force (StoryDirector.ApplySettings).</summary>
        public static bool On { get; set; }

        // ------------------------------------------------------------------ what is on screen (the views tell)

        static readonly List<string> s_screens = new();

        /// <summary>The story screen open now (the newest), or null (UnityStoryHost.Open pushes and pops).</summary>
        public static string Screen => s_screens.Count > 0 ? s_screens[s_screens.Count - 1] : null;
        public static void PushScreen(string name) { if (name != null) s_screens.Add(name); }
        public static void PopScreen(string name)
        {
            int i = s_screens.LastIndexOf(name);
            if (i >= 0) s_screens.RemoveAt(i);
        }

        /// <summary>The HUD's objective as the story set it (original words; the hint shows them plain in the mode).</summary>
        public static string ObjectiveTitle { get; private set; }
        public static string ObjectiveBody { get; private set; }
        public static IReadOnlyList<ObjectiveStep> ObjectiveSteps { get; private set; } = Array.Empty<ObjectiveStep>();

        public static void SetObjective(string title, string body, IReadOnlyList<ObjectiveStep> steps)
        {
            ObjectiveTitle = string.IsNullOrEmpty(title) ? null : title;
            ObjectiveBody = body;
            ObjectiveSteps = steps ?? Array.Empty<ObjectiveStep>();
        }

        /// <summary>
        /// A run is playing (StoryDirector.RunChapters / teacher mode set it; the title clears it through Reset): outside
        /// a run the chip is hidden and F1 does nothing, so the title and its menus are never offered a "지금 할 일" hint.
        /// </summary>
        public static bool RunActive { get; set; }

        /// <summary>The chip and F1 are offered: the mode is on and a run is playing.</summary>
        public static bool Offered => On && RunActive;

        /// <summary>The title (or a new run): nothing on screen is remembered, no run is playing, no talk was had.</summary>
        public static void Reset()
        {
            s_screens.Clear();
            SetObjective(null, null, null);
            RunActive = false;
            s_lastChoices.Clear();
        }

        // ------------------------------------------------------------------ when the card stops the game

        static readonly List<string> s_lastChoices = new();

        /// <summary>The choices a talk offers now, as labels: a disabled or already taken one is not offered.</summary>
        public static List<string> OpenChoices(IEnumerable<TalkChoice> choices) =>
            (choices ?? Enumerable.Empty<TalkChoice>()).Where(c => c != null && !c.Disabled && !c.Taken && !string.IsNullOrWhiteSpace(c.Label))
                .Select(c => c.Label).ToList();

        /// <summary>
        /// Whether this judgement gets the card: once per judgement, not on every pass of a loop that asks it again (the
        /// smoke question after feeling the door, each pick of 「22분」). A pass is a repeat when at least half of its open
        /// choices were on the card already shown; the chain grows with each repeat (a choice that appears later in the
        /// loop counts as part of it). A different judgement shares (almost) no choices and gets its own card.
        /// </summary>
        public static bool FirstPass(IEnumerable<string> choices)
        {
            var now = (choices ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (now.Count > 0 && s_lastChoices.Count > 0 && now.Count(s_lastChoices.Contains) * 2 >= now.Count)
            {
                foreach (var c in now) if (!s_lastChoices.Contains(c)) s_lastChoices.Add(c);
                return false;
            }
            s_lastChoices.Clear();
            s_lastChoices.AddRange(now);
            return true;
        }

        /// <summary>
        /// Whether a card is a judgement to talk over: a decision card, but not an offer to play or skip an optional scene
        /// (a "skip" action, or a Meta starting "선택 ·", such as the 304호 offer).
        /// </summary>
        public static bool CardDiscusses(CardOptions o) =>
            o != null && o.Kind == "decision"
            && !(o.Actions ?? new List<CardAction>()).Any(a => a?.Id == "skip")
            && !(o.Meta ?? "").StartsWith("선택 ·", StringComparison.Ordinal);

        // ------------------------------------------------------------------ topics

        static readonly (string topic, string[] words)[] Topics =
        {
            ("reflect", new[] { "다음에 또", "다음엔", "다음에는" }), // a look back after a scene, not a moment of danger
            ("cpr", new[] { "심폐소생", "가슴압박", "AED", "쓰러", "압박" }),
            ("fire", new[] { "화재", "연기", "소화기", "칸막이", "불길", "불이 났", "불이 난", "복도의 불", "불이 커", "작은 불", "불이야" }),
            ("report", new[] { "신고", "112", "1338", "수상", "낯선", "전단", "물체", "흔적" }),
            ("rumor", new[] { "소문", "단톡", "확인되지", "가짜", "믿고", "라디오", "방송" }),
            ("evac", new[] { "남을", "남는다", "떠날", "떠나", "옮긴다", "대피 안내", "집결", "대피명령" }),
            ("shelter", new[] { "대피소 안", "명부", "접수대", "자리를 잡", "돗자리", "임시 거처" }),
            ("alert", new[] { "경보", "사이렌", "공습", "대피소", "계단", "엘리베이터" }),
            ("bag", new[] { "배낭", "가방", "챙길", "챙겨" }),
            ("care", new[] { "할아버지", "어르신", "이웃", "아이", "지우", "도와", "돕" }),
        };

        /// <summary>The first topic whose words appear in the text, or null.</summary>
        public static string Topic(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var (topic, words) in Topics)
                if (words.Any(w => text.IndexOf(w, StringComparison.Ordinal) >= 0)) return topic;
            return null;
        }

        /// <summary>The topic of a story screen (UnityStoryHost screen names), or null.</summary>
        public static string ScreenTopic(string screen) => screen switch
        {
            "cpr" => "cpr",
            "lookfirst" or "extinguisher" or "extgauge" => "fire",
            "sighting" or "compose" => "report",
            "radio" => "rumor",
            "emergencyAlert" or "phone" => "alert",
            "bag" => "bag",
            "breathe" => "care",
            "quiz" => "quiz",
            _ => null,
        };

        static readonly Dictionary<string, string[]> TopicPrompts = new()
        {
            ["cpr"] = new[] { "누구에게, 무엇을 부탁해야 할까요?", "누르는 사람이 지치면 어떻게 할까요?" },
            ["fire"] = new[] { "불은 어디서 났을까요? 무엇을 보면 알 수 있을까요?", "나가는 것과 집 안에서 기다리는 것, 지금은 어느 쪽이 더 안전할까요? 왜요?" },
            ["report"] = new[] { "누구에게 알려야 할까요?", "본 것을 '언제, 어디서, 무엇을' 순서로 말해 볼까요? 짐작은 빼고요." },
            ["rumor"] = new[] { "이 말은 누가, 언제 확인한 걸까요?", "공식 안내는 어디서 확인할 수 있을까요?" },
            // any channel: a 경계경보 comes as voice and text with no siren, and the deaf profile reads it
            ["alert"] = new[] { "지금 받은 경보는 어떤 경보일까요? 사이렌·방송·문자 가운데 무엇으로 알았나요?", "어디로, 어떤 길로 가야 할까요?" },
            ["reflect"] = new[] { "이번에 잘한 것과 아쉬운 것은 무엇일까요?", "다음에는 무엇부터 확인할까요?" },
            ["shelter"] = new[] { "여럿이 함께 지내려면 무엇을 지켜야 할까요?", "주변에 도움이 필요한 사람은 없을까요?" },
            ["evac"] = new[] { "남으면 무엇이 필요하고, 떠나면 무엇이 필요할까요?", "지금 확실히 아는 것과 모르는 것은 무엇일까요?" },
            ["bag"] = new[] { "꼭 가져가야 할 것 세 가지는 무엇일까요?", "너무 무거우면 무엇을 뺄까요?" },
            ["care"] = new[] { "이 선택으로 누가 도움을 받을까요?", "그 사람에게 무엇이 필요한지 어떻게 알 수 있을까요?" },
        };

        static readonly string[] DefaultPrompts = { "지금 확실히 아는 것과 모르는 것은 무엇일까요?", "다른 것을 고르면 어떻게 될까요?" };

        /// <summary>The two talking points for a topic (open questions).</summary>
        public static IReadOnlyList<string> PromptsFor(string topic) => topic != null && TopicPrompts.TryGetValue(topic, out var p) ? p : DefaultPrompts;

        /// <summary>
        /// The "함께 생각해 보기" card for a judgement: the question on screen, the "what would you do?" prompt, then two
        /// talking points for the topic (from the question and the choices, or the topic given).
        /// </summary>
        public static CompanionTalk Discuss(string question, IEnumerable<string> choices, string topic = null)
        {
            var list = (choices ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            topic ??= Topic(question) ?? Topic(string.Join(" ", list));
            var t = new CompanionTalk { Question = string.IsNullOrWhiteSpace(question) ? "어떻게 할까?" : question, Topic = topic, Choices = list };
            t.Prompts.Add(FirstPrompt);
            t.Prompts.AddRange(PromptsFor(topic));
            return t;
        }

        // ------------------------------------------------------------------ hints

        /// <summary>The checked fact a topic's hint shows (ids in facts.json / facts-ext.json; tested).</summary>
        public static readonly IReadOnlyDictionary<string, string> TopicFact = new Dictionary<string, string>
        {
            ["cpr"] = "cpr_point", ["fire"] = "fire_look", ["report"] = "suspect_report", ["rumor"] = "info_rumor",
            ["alert"] = "alert_stairs", ["shelter"] = "shelter_rules", ["evac"] = "evac_order", ["bag"] = "bag_take", ["care"] = "people_ask",
        };

        /// <summary>How a screen is played, for the helper to explain (controls only, no safety claims).</summary>
        public static readonly IReadOnlyDictionary<string, string> ScreenTips = new Dictionary<string, string>
        {
            ["cpr"] = "Space(또는 클릭)를 박자에 맞춰 누릅니다. 화면의 박자 점을 함께 보며 세어 주세요.",
            ["lookfirst"] = "문을 열기 전에 단서를 하나씩 살펴보게 해 주세요. 손잡이, 문틈, 소리, 방송.",
            ["extinguisher"] = "순서대로 누릅니다. 불이 커지면 물러나도 괜찮아요.",
            ["extgauge"] = "압력계 바늘이 어느 색 칸에 있는지 함께 보세요.",
            ["bag"] = "물건을 끌어서 가방 칸에 넣습니다. ★ 표시는 공식 목록에 있는 물건이에요.",
            ["sighting"] = "본 것만 골라 신고 카드를 만듭니다. 짐작은 빼요.",
            ["radio"] = "주파수를 맞춰 공식 방송을 찾아보세요. 화면에 '힌트 보기' 버튼이 있으면 그것도 함께 보세요.",
            ["phone"] = "문자를 끝까지 읽고, 누가 보냈는지 함께 보세요.",
            ["emergencyAlert"] = "문자를 끝까지 읽고, 어떤 경보인지 함께 보세요.",
            ["breathe"] = "숫자에 맞춰 함께 천천히 숨을 쉬어 보세요.",
            ["quiz"] = "퀴즈는 플레이어가 혼자 풀게 해 주세요. 끝난 뒤 결과를 함께 보면 좋아요.",
            ["planner"] = "남은 시간 칸에 무엇을 먼저 할지 함께 정해 보세요.",
            ["guide"] = "상황별 행동요령입니다. 지금 장면과 맞는 카드를 함께 읽어 보세요.",
        };

        /// <summary>
        /// The F1 hint for what is on screen: an open screen's tip, else the objective (its goal and the next step), and the
        /// topic's checked fact (except in the quiz, where a fact would give answers away). show: how a line is shown
        /// (EasyKorean.Of in the game).
        /// </summary>
        public static CompanionHint Hint(string screen, string title, string body, IReadOnlyList<ObjectiveStep> steps, Func<string, string> show = null)
        {
            show ??= s => s;
            var h = new CompanionHint();
            string topic = ScreenTopic(screen);
            if (screen != null && ScreenTips.TryGetValue(screen, out var tip))
            {
                h.Title = HintTitle + " · 이 화면";
                h.Lines.Add(tip);
            }
            else if (!string.IsNullOrEmpty(title))
            {
                h.Title = HintTitle + " · " + show(title);
                if (!string.IsNullOrEmpty(body)) h.Lines.Add(show(body));
                var open = (steps ?? Array.Empty<ObjectiveStep>()).Where(s => s != null && !s.Done && !string.IsNullOrEmpty(s.Text)).ToList();
                if (open.Count > 0 && open.All(s => s.Choice)) h.Lines.Add("고를 수 있는 것: " + string.Join(" / ", open.Select(s => show(s.Text))));
                else if (open.Count > 0) h.Lines.Add("다음 할 일: " + show(open.First(s => !s.Choice).Text));
                topic ??= Topic(title + " " + body + " " + string.Join(" ", open.Select(s => s.Text)));
            }
            else
            {
                h.Title = HintTitle;
                h.Lines.Add(NothingToDo);
            }
            h.Topic = topic;
            if (topic != null && topic != "quiz" && TopicFact.TryGetValue(topic, out var fact)) h.FactId = fact;
            return h;
        }
    }
}
