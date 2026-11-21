// 사전/사후 퀴즈 (competition upgrade, learning evidence): seven short questions asked before a run (or a class) and again
// after its ending (or the class), each concept asked twice in different words. The questions only restate the checked
// facts (facts.json + facts-ext.json): every right answer names the fact ids that say it, and the result page after the
// post quiz shows those facts word for word with who published them. Pure logic, no UnityEngine; the screen is
// UI/Screens/QuizScreen.cs, the story hooks are StoryDirector.OfferPreQuiz / OfferPostQuiz.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public sealed class QuizOption
    {
        public string Id, Text;
        public QuizOption(string id, string text) { Id = id; Text = text; }
    }

    /// <summary>One wording of a concept's question: the prompt, its options and which one is right.</summary>
    public sealed class QuizAsk
    {
        public string Prompt, Correct;
        public List<QuizOption> Options = new();
        public QuizOption Right => Options.FirstOrDefault(o => o.Id == Correct);
    }

    /// <summary>A concept asked twice: Pre before the run, Post (other words, same knowledge) after it.</summary>
    public sealed class QuizItem
    {
        /// <summary>Label: what the concept teaches (the result page and the report, after the post quiz). Topic: a neutral
        /// name that gives no answer away (the pre quiz's intro).</summary>
        public string Id, Label, Topic;
        public QuizAsk Pre, Post;
        /// <summary>The checked facts that make the right answer right (shown with their sources after the post quiz).</summary>
        public string[] Facts;
        public QuizAsk Ask(string phase) => phase == QuizLogic.Post ? Post : Pre;
    }

    /// <summary>What one sitting of the quiz answered (the "quiz" event's details, and the screen's result).</summary>
    public sealed class QuizAnswers
    {
        public string Phase;
        public uint Seed;
        /// <summary>concept id -> option id ("unsure" = 잘 모르겠어요).</summary>
        public Dictionary<string, string> Picks = new();
        /// <summary>The concept order the questions were asked in.</summary>
        public List<string> Order = new();

        public bool Answered => Picks.Count > 0;
        public string Pick(string concept) => concept != null && Picks.TryGetValue(concept, out var p) ? p : null;

        public JObject ToJson()
        {
            var answers = new JArray();
            foreach (var id in Order.Concat(Picks.Keys.Where(k => !Order.Contains(k))))
            {
                var item = QuizLogic.Item(id);
                var pick = Pick(id);
                if (pick == null) continue;
                answers.Add(new JObject { ["concept"] = id, ["option"] = pick, ["state"] = QuizLogic.State(item, Phase, pick) });
            }
            return new JObject
            {
                ["phase"] = Phase, ["seed"] = Seed, ["answers"] = answers,
                ["score"] = QuizLogic.Score(this), ["total"] = Picks.Count, // the questions asked (a class asks fewer)
            };
        }

        /// <summary>Reads a "quiz" event's details or the screen's result; null when it holds no answer (skipped, {}).</summary>
        public static QuizAnswers Read(JToken t)
        {
            if (t is not JObject o) return null;
            var a = new QuizAnswers { Phase = o["phase"]?.Type == JTokenType.String ? (string)o["phase"] : QuizLogic.Pre };
            if (o["seed"] != null && (o["seed"].Type == JTokenType.Integer)) a.Seed = unchecked((uint)(long)o["seed"]);
            if (o["answers"] is JArray arr)
                foreach (var x in arr.OfType<JObject>())
                {
                    var c = x["concept"]?.Type == JTokenType.String ? (string)x["concept"] : null;
                    var p = x["option"]?.Type == JTokenType.String ? (string)x["option"] : null;
                    if (c == null || p == null || QuizLogic.Item(c) == null || a.Picks.ContainsKey(c)) continue;
                    a.Picks[c] = p; a.Order.Add(c);
                }
            return a.Answered ? a : null;
        }
    }

    /// <summary>One concept read before and after: what the player knew, and how that changed.</summary>
    public sealed class QuizCompareRow
    {
        public string Concept, Label;
        /// <summary>"right", "wrong", "unsure" or null (not asked / that quiz skipped).</summary>
        public string Pre, Post;
        /// <summary>"learned" (몰랐음 → 앎), "kept" (앎 → 앎), "slipped" (앎 → 헷갈림), "missed" (몰랐음 → 다시 보기),
        /// "postRight" / "postWrong" (no pre quiz), "preOnly" (no post quiz yet).</summary>
        public string Change;
        public string ChangeText => QuizLogic.ChangeText(Change);
    }

    public sealed class QuizComparison
    {
        public List<QuizCompareRow> Rows = new();
        public int? PreScore, PostScore;
        public int Total;
        public int Learned => Rows.Count(r => r.Change == "learned");
        public bool HasPre => PreScore.HasValue;
        public bool HasPost => PostScore.HasValue;
    }

    public static class QuizLogic
    {
        public const string Screen = "quiz", Event = "quiz", Pre = "pre", Post = "post", Unsure = "unsure";
        public const string UnsureText = "잘 모르겠어요";

        static QuizAsk Ask(string prompt, string correct, params (string id, string text)[] options)
        {
            var a = new QuizAsk { Prompt = prompt, Correct = correct };
            foreach (var (id, text) in options) a.Options.Add(new QuizOption(id, text));
            return a;
        }

        /// <summary>
        /// The seven concepts, in the pre quiz's order. Every right answer restates its Facts; the wrong answers are the
        /// mistakes people really make, of about the same length, and each differs from the right one in the concept the
        /// question checks (never an absolute "무조건" or a "do nothing" strawman, never a number the project has not
        /// checked: no 1337, 111 or 113, no "2분 교대").
        /// </summary>
        public static readonly IReadOnlyList<QuizItem> Items = new List<QuizItem>
        {
            new QuizItem
            {
                Id = "alert", Label = "경계경보·공습경보 구별", Topic = "경보의 종류", Facts = new[] { "alert_levels" },
                Pre = Ask("사이렌이 1분 동안 울리고 음성방송이 함께 나온다. 두 경보 가운데 어느 쪽일까요?", "airraid",
                    ("airraid", "공습경보"), ("caution", "경계경보")),
                Post = Ask("경계경보와 공습경보의 차이로 맞는 것은?", "right",
                    ("right", "공습경보는 사이렌이 1분 울리고, 경계경보는 사이렌 없이 음성방송으로 알린다"),
                    ("swapped", "경계경보는 사이렌이 1분 울리고, 공습경보는 사이렌 없이 음성방송으로 알린다"),
                    ("same", "둘 다 사이렌이 1분 울리고, 음성방송의 내용으로만 구별한다")),
            },
            new QuizItem
            {
                Id = "lift", Label = "엘리베이터 대신 계단", Topic = "고층에서 내려가기", Facts = new[] { "alert_stairs", "fire_lift" },
                Pre = Ask("고층 아파트에서 공습경보가 울렸다. 아래층으로 어떻게 내려갈까요?", "stairs",
                    ("stairs", "엘리베이터를 쓰지 않고 계단으로 내려간다"), ("lift", "사람이 몰리기 전에 엘리베이터로 내려간다"),
                    ("liftIfPower", "전기가 들어와 있으면 엘리베이터로 내려간다")),
                Post = Ask("불이 나서 대피한다. 지상까지 어떻게 내려갈까요?", "stairs",
                    ("stairs", "엘리베이터를 쓰지 않고 계단으로 지상까지 내려간다"), ("lift", "고층이라 오래 걸리니 엘리베이터로 내려간다"),
                    ("liftIfClear", "엘리베이터 앞에 연기가 없으면 엘리베이터를 탄다")),
            },
            new QuizItem
            {
                Id = "look", Label = "불나면 살펴서 대피", Topic = "아파트 화재", Facts = new[] { "fire_look", "fire_stay" },
                Pre = Ask("다른 집에서 불이 났다. 우리 집에는 불길도 연기도 들어오지 않는다. 어떻게 할까요?", "stay",
                    ("stay", "창문을 닫고 집 안에서 기다리며 119에 신고한다"), ("run", "연기가 없을 때 서둘러 계단으로 나가 밖으로 대피한다"),
                    ("window", "창문을 열어 두고 바깥 상황을 지켜보며 기다린다")),
                Post = Ask("아파트에 불이 났다는 방송이 나온다. 먼저 무엇을 할까요?", "look",
                    ("look", "불이 난 곳과 불길, 연기를 살펴서 나갈지 머물지 정한다"), ("run", "어디서 났든 곧바로 현관문을 열고 계단으로 나간다"),
                    ("window", "창문을 열어 연기를 빼면서 집 안에서 소식을 기다린다")),
            },
            new QuizItem
            {
                Id = "cpr", Label = "심폐소생술: 지목·깊이·속도", Topic = "심폐소생술", Facts = new[] { "cpr_point", "cpr_depth_rate" },
                Pre = Ask("어른이 쓰러져 숨을 제대로 쉬지 않는다. 맞는 대응은?", "point",
                    ("point", "한 사람을 지목해 119와 AED를 부탁하고, 가슴을 약 5cm 깊이로 분당 100~120회 누른다"),
                    ("shout", "\"누가 119 좀 불러 주세요\" 하고 외치고, 가슴을 약 5cm 깊이로 분당 100~120회 누른다"),
                    ("soft", "한 사람을 지목해 119와 AED를 부탁하고, 다치지 않게 가슴을 얕고 천천히 누른다")),
                Post = Ask("심폐소생술을 할 때 맞는 것은?", "point",
                    ("point", "\"파란 옷 입은 분, 119에 신고해 주세요\"처럼 한 사람을 정해 부탁하고, 약 5cm 깊이·분당 100~120회로 누른다"),
                    ("shout", "모두에게 \"누가 신고 좀 해 주세요\" 하고 외치고, 약 5cm 깊이·분당 100~120회로 누른다"),
                    ("soft", "\"파란 옷 입은 분, 119에 신고해 주세요\"처럼 한 사람을 정해 부탁하고, 다치지 않게 얕고 느리게 누른다")),
            },
            new QuizItem
            {
                Id = "ext", Label = "소화기 쓰는 순서", Topic = "소화기", Facts = new[] { "ext_1", "ext_2", "ext_3" },
                Pre = Ask("소화기를 불이 난 곳으로 옮겼다. 그다음 순서로 맞는 것은?", "order",
                    ("order", "안전핀 뽑기 → 바람을 등지고 호스를 불 쪽으로 → 손잡이를 움켜쥐고 빗자루로 쓸듯이"),
                    ("facing", "안전핀 뽑기 → 바람을 마주 보고 호스를 불 쪽으로 → 손잡이를 움켜쥐고 빗자루로 쓸듯이"),
                    ("gripFirst", "호스를 불 쪽으로 → 손잡이를 움켜쥐고 → 안전핀을 뽑은 뒤 빗자루로 쓸듯이")),
                Post = Ask("소화기 쓰는 순서를 바르게 늘어놓은 것은?", "order",
                    ("order", "불난 곳으로 옮기기 → 안전핀 뽑기 → 바람을 등지고 호스를 불 쪽으로 → 움켜쥐고 쓸듯이 뿌리기"),
                    ("gripFirst", "불난 곳으로 옮기기 → 손잡이 움켜쥐기 → 안전핀 뽑기 → 바람을 등지고 쓸듯이 뿌리기"),
                    ("facing", "불난 곳으로 옮기기 → 안전핀 뽑기 → 바람을 마주 보고 호스를 불 쪽으로 → 움켜쥐고 쓸듯이 뿌리기")),
            },
            new QuizItem
            {
                Id = "report", Label = "1338·112·119 신고처", Topic = "신고할 곳", Facts = new[] { "report_object", "suspect_report", "fire_report" },
                Pre = Ask("상황과 알릴 곳이 바르게 이어진 것은?", "match",
                    ("match", "불은 119, 수상한 사람은 112, 정체 모를 물체는 1338(군부대)이나 가까운 경찰서"),
                    ("object119", "불은 119, 수상한 사람은 112, 정체 모를 물체는 위험할 수 있으니 119"),
                    ("office", "불은 119, 수상한 사람은 관리사무소, 정체 모를 물체는 1338(군부대)이나 가까운 경찰서")),
                Post = Ask("어디에 알릴지 바르게 짝지은 것은?", "match",
                    ("match", "정체 모를 물체·전단 → 1338이나 가까운 경찰서 · 수상한 사람 → 112 · 불 → 119"),
                    ("object119", "정체 모를 물체·전단 → 119 · 수상한 사람 → 112 · 불 → 119"),
                    ("person119", "정체 모를 물체·전단 → 1338이나 가까운 경찰서 · 수상한 사람 → 119 · 불 → 119")),
            },
            new QuizItem
            {
                Id = "info", Label = "공식 정보 확인", Topic = "정보 확인", Facts = new[] { "radio_listen", "info_rumor" },
                Pre = Ask("이유도 장소도 없는 경보 문자가 왔다. 단톡방에는 \"전쟁 났대\"라는 말이 돈다. 먼저 할 일은?", "official",
                    ("official", "TV·라디오의 민방위 방송으로 정부 안내를 확인한다"), ("forward", "가족이 대비하도록 단톡방의 말을 바로 전한다"),
                    ("chat", "단톡방에서 여러 사람의 말을 더 모아 보고 판단한다")),
                Post = Ask("대피소에서 \"큰길 다리가 끊겼다\"는 소문을 들었다. 어떻게 할까요?", "official",
                    ("official", "소문에 흔들리지 않고 공식 안내로 사실인지 확인한다"), ("forward", "혹시 모르니 가족 단톡방에 바로 전해 둔다"),
                    ("follow", "여러 사람이 같은 말을 하면 다른 길을 찾아 떠난다")),
            },
        };

        /// <summary>
        /// The concepts a class (교사용 모드, SceneSelect.TeacherRoute) actually plays: the alert and the stairs (alert), the
        /// corridor fire with its extinguisher (fire), the rumours (decision). CPR and the 1338·112 reports come in moments
        /// the class does not play, so a class is not asked them (tested against the route's lesson facts).
        /// </summary>
        public static readonly IReadOnlyList<string> ClassConcepts = new[] { "alert", "lift", "look", "ext", "info" };

        public static QuizItem Item(string id) => Items.FirstOrDefault(i => i.Id == id);

        /// <summary>"right", "wrong" or "unsure" for a pick of a concept in a phase (null when nothing was picked).</summary>
        public static string State(QuizItem item, string phase, string pick)
        {
            if (item == null || pick == null) return null;
            if (pick == Unsure) return "unsure";
            return pick == item.Ask(phase).Correct ? "right" : "wrong";
        }

        public static int Score(QuizAnswers a) =>
            a == null ? 0 : Items.Count(i => State(i, a.Phase, a.Pick(i.Id)) == "right");

        /// <summary>A seed from an unsigned run seed and the phase: the post quiz never repeats the pre quiz's order.</summary>
        public static uint SeedFor(uint baseSeed, string phase) => unchecked(baseSeed * 2654435761u + (phase == Post ? 0x9E3779B9u : 0x85EBCA6Bu));

        static void Shuffle<T>(IList<T> list, Mulberry32 rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.Next() * (i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>One question as asked: the concept, its prompt and options in order ("잘 모르겠어요" always last).</summary>
        public sealed class Question
        {
            public QuizItem Item;
            public string Prompt;
            public List<QuizOption> Options;
        }

        /// <summary>
        /// The questions of a sitting: the pre quiz keeps the concept order, the post quiz shuffles it; both shuffle the
        /// options (deterministic for a seed). only: the concepts to ask (null = all seven; a class asks ClassConcepts).
        /// </summary>
        public static List<Question> Questions(string phase, uint seed, IEnumerable<string> only = null)
        {
            var rng = new Mulberry32(seed);
            var keep = only != null ? new HashSet<string>(only) : null;
            var items = Items.Where(i => keep == null || keep.Contains(i.Id)).ToList();
            if (phase == Post) Shuffle(items, rng);
            var list = new List<Question>();
            foreach (var item in items)
            {
                var ask = item.Ask(phase);
                var opts = ask.Options.ToList();
                Shuffle(opts, rng);
                opts.Add(new QuizOption(Unsure, UnsureText));
                list.Add(new Question { Item = item, Prompt = ask.Prompt, Options = opts });
            }
            return list;
        }

        public static string Change(string pre, string post)
        {
            if (post == null) return pre == null ? null : "preOnly";
            bool now = post == "right";
            if (pre == null) return now ? "postRight" : "postWrong";
            bool before = pre == "right";
            return before ? (now ? "kept" : "slipped") : (now ? "learned" : "missed");
        }

        public static string ChangeText(string change) => change switch
        {
            "learned" => "몰랐음 → 앎",
            "kept" => "앎 → 앎",
            "slipped" => "앎 → 헷갈림",
            "missed" => "몰랐음 → 다시 보기",
            "postRight" => "사후 · 앎",
            "postWrong" => "사후 · 다시 보기",
            "preOnly" => "사전만 풀었음",
            _ => "",
        };

        /// <summary>The concept-by-concept comparison; null when neither quiz was answered.</summary>
        public static QuizComparison Compare(QuizAnswers pre, QuizAnswers post)
        {
            if (pre == null && post == null) return null;
            // the concepts asked (all seven, or a class's ClassConcepts), in the items' order
            var asked = Items.Where(i => pre?.Pick(i.Id) != null || post?.Pick(i.Id) != null).ToList();
            var c = new QuizComparison { Total = asked.Count, PreScore = pre != null ? Score(pre) : null, PostScore = post != null ? Score(post) : null };
            foreach (var item in asked)
            {
                string a = pre != null ? State(item, Pre, pre.Pick(item.Id)) : null, b = post != null ? State(item, Post, post.Pick(item.Id)) : null;
                c.Rows.Add(new QuizCompareRow { Concept = item.Id, Label = item.Label, Pre = a, Post = b, Change = Change(a, b) });
            }
            return c;
        }

        /// <summary>The run's latest answered sitting of a phase ("quiz" events), or null.</summary>
        public static QuizAnswers Find(GameState g, string phase)
        {
            if (g?.Events == null) return null;
            for (int i = g.Events.Count - 1; i >= 0; i--)
            {
                var e = g.Events[i];
                if (e.Type != Event || e.Details == null) continue;
                var a = QuizAnswers.Read(e.Details);
                if (a != null && a.Phase == phase) return a;
            }
            return null;
        }

        public static QuizComparison Compare(GameState g) => Compare(Find(g, Pre), Find(g, Post));

        /// <summary>
        /// The headline of a comparison: "사전 3/7 → 사후 6/7 · 새로 안 것 3개" (only the parts that exist). Without a post
        /// quiz: it was skipped at the ending, or (postLater, a class) it comes after the class.
        /// </summary>
        public static string Headline(QuizComparison c, bool postLater = false)
        {
            if (c == null) return "";
            if (c.HasPre && c.HasPost) return $"사전 {c.PreScore}/{c.Total} → 사후 {c.PostScore}/{c.Total}" + (c.Learned > 0 ? $" · 새로 안 것 {c.Learned}개" : "");
            if (c.HasPost) return $"사후 {c.PostScore}/{c.Total} (사전 퀴즈는 건너뜀)";
            return postLater ? "사전 퀴즈를 풀었습니다 · 사후 퀴즈는 수업 끝에" : "사전 퀴즈만 풀었습니다 (사후 퀴즈는 건너뜀)";
        }

        // ---------------------------------------------------------------- screen texts

        public static string IntroTitle(string phase) => phase == Post ? "사후 퀴즈" : "사전 퀴즈";

        /// <summary>The offer's text. count: the questions asked; teacher: a class (the answers come after the class).</summary>
        public static string IntroBody(string phase, bool hasPre, int count, bool teacher = false) => phase == Post
            ? (hasPre
                ? $"처음에 풀었던 내용을 다른 말로 다시 묻습니다. {count}문항, 1분쯤 걸립니다. 다 풀면 정답과 근거, 처음과 달라진 점을 함께 보여 드립니다."
                : $"이번 체험에서 다룬 행동요령을 {count}문항으로 확인합니다. 1분쯤 걸리고, 다 풀면 정답과 근거를 보여 드립니다.")
            : $"{count}문항, 1분쯤 걸리는 짧은 퀴즈입니다. 헷갈리면 '잘 모르겠어요'를 고르세요. 정답은 {(teacher ? "수업이" : "이야기가")} 끝난 뒤 사후 퀴즈와 함께 보여 드립니다.";

        public const string IntroNote = "점수는 이 컴퓨터 안에만 남고, 건너뛰어도 체험은 그대로입니다.";
        public const string PreDoneTitle = "사전 퀴즈를 마쳤습니다";
        public static string PreDoneBody(bool teacher = false) =>
            $"정답은 아직 알려 드리지 않습니다. {(teacher ? "수업이" : "이야기가")} 끝나면 같은 내용을 다른 말로 한 번 더 묻습니다.";
        public const string ResultNote = "같은 개념을 처음과 끝에 다른 문장으로 물었습니다. 한 사람의 한 번 기록이라 효과를 단정하지 않습니다.";
    }
}
