// 루머 해부 (competition upgrade, 4단계 세계와 학습: prebunking). When a rumour has been checked or corrected, a short
// card takes the post apart: the words that carry each technique are highlighted in the post, each technique is named
// in one line, and the habit that answers all of them is the same: check the official channels. Technique-level
// inoculation (learning how a false post is made, not only that one post is false) is the idea behind the Cambridge
// "Bad News" game; that study is cited as a sourced fact (prebunk_badnews, 보조 자료). The technique names and the notes
// are game text about the fictional posts; the only safety sentences are facts (info_rumor, radio_listen).
// Pure logic; PrebunkScene (Story) shows the card, Archive collects the techniques seen, LearningReport has a row.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A technique false posts use (game names, kept short).</summary>
    public sealed class RumorTechnique
    {
        public string Id, Name;
        /// <summary>What it does, in one line.</summary>
        public string What;
        /// <summary>The question that answers it.</summary>
        public string Ask;
    }

    /// <summary>One technique found in one post: the words that carry it and a note on this post.</summary>
    public sealed class AnatomyMark
    {
        public string Technique, Phrase, Note;
        public AnatomyMark(string technique, string phrase, string note) { Technique = technique; Phrase = phrase; Note = note; }
    }

    /// <summary>A post taken apart.</summary>
    public sealed class RumorAnatomyDef
    {
        /// <summary>The rumour set id (Rumors), or a post that is not a set (tt_photo, 「22분」's group chat).</summary>
        public string Id;
        /// <summary>Where the post was met (the card's title and the archive's example line).</summary>
        public string Where;
        public Func<GameState, string> Post;
        public AnatomyMark[] Marks;
    }

    public static class RumorAnatomy
    {
        public const string Event = "prebunk";
        public const string Kind = "anatomy";
        public const string Title = "루머 해부";

        /// <summary>The five techniques, in the order the archive shows them.</summary>
        public static readonly IReadOnlyList<RumorTechnique> Techniques = new[]
        {
            new RumorTechnique { Id = "nosource", Name = "출처 없음", What = "누가, 언제, 어디서 확인했는지 없이 '~래요', '퍼옴'으로 말만 옮깁니다.", Ask = "누가 직접 봤나? 언제?" },
            new RumorTechnique { Id = "fear", Name = "공포 자극", What = "느낌표와 무서운 말로 확인하기 전에 마음부터 흔듭니다.", Ask = "겁이 나서 확인도 안 하고 믿으려는 건 아닐까?" },
            new RumorTechnique { Id = "numbers", Name = "숫자 과장", What = "아무도 세지 않은 큰 숫자를 사실처럼 씁니다.", Ask = "그 숫자는 누가 셌나?" },
            new RumorTechnique { Id = "pressure", Name = "퍼뜨리기 압박", What = "'다들', '빨리', '알려 주세요'로 확인할 틈 없이 움직이고 옮기게 합니다.", Ask = "왜 이렇게 서두르게 하지?" },
            new RumorTechnique { Id = "photo", Name = "사진 재활용", What = "다른 날, 다른 곳의 사진을 지금 여기 일처럼 붙입니다.", Ask = "사진 속 날씨·시간·간판이 지금과 맞나?" },
        };

        public static RumorTechnique Technique(string id) => Techniques.FirstOrDefault(t => t.Id == id);

        /// <summary>The 「22분」 group-chat post with a photo (not a Rumors set: nobody judges it, the correction text answers it).</summary>
        public const string PhotoPost = "tt_photo";
        public const string PhotoPostText = "[사진] 연무로 사거리 지금 완전 난리!! 누가 그러는데 다들 빨리 퍼뜨려 달래요";
        /// <summary>What the photo shows (the chat line after the post).</summary>
        public const string PhotoSeen = "(사진 속 사람들은 두꺼운 패딩 차림이고, 길가에 눈이 쌓여 있다.)";
        /// <summary>The correction after the 오발령 text: the poster found the photo's original post (named, checked, not '~래요').</summary>
        public const string PhotoCorrection = "사진 올린 분: \"원래 글을 찾아보니 작년 겨울 축제 때 사진이었어요. 죄송해요, 지울게요.\"";

        public const string Habit = "막는 습관 ▸ 옮기기 전에 공식 채널에서 확인: 재난문자 · 행정안전부·지자체 안내 · TV·라디오 재난방송";
        /// <summary>Where to check (the habit's channels, game text).</summary>
        public const string Channels = "공식 채널: 재난문자 · 행정안전부·지자체 안내 · TV·라디오 재난방송";
        /// <summary>The habit, as facts (info_rumor, radio_listen).</summary>
        public static readonly string[] HabitFacts = { "info_rumor", "radio_listen" };
        /// <summary>Why the card names techniques (the study behind prebunking).</summary>
        public const string ResearchFact = "prebunk_badnews";

        public static readonly IReadOnlyList<RumorAnatomyDef> All = new[]
        {
            new RumorAnatomyDef
            {
                Id = PhotoPost, Where = "「22분」 아침 단톡방의 사진 글", Post = _ => PhotoPostText,
                Marks = new[]
                {
                    new AnatomyMark("photo", "[사진]", "사진 속은 눈 쌓인 한겨울인데 지금은 가을이다. 다른 날 찍은 사진이다."),
                    new AnatomyMark("fear", "완전 난리!!", "무서운 말과 느낌표가 먼저 마음을 흔든다."),
                    new AnatomyMark("nosource", "누가 그러는데", "누가 그랬는지, 몇 시 일인지 없다."),
                    new AnatomyMark("pressure", "다들 빨리 퍼뜨려 달래요", "확인보다 옮기기를 먼저 시킨다."),
                },
            },
            new RumorAnatomyDef
            {
                Id = "shelter_closed", Where = "3장 편의점 앞에서 들은 SNS 글", Post = g => Claim(g, "shelter_closed"),
                Marks = new[]
                {
                    new AnatomyMark("nosource", "폐쇄됐대요", "작성자도 시각도 없다. 누가 가서 봤는지 말하지 않는다."),
                    new AnatomyMark("pressure", "다들 다른 데로 가래요", "'다들'이라며 확인할 틈 없이 움직이게 한다."),
                },
            },
            new RumorAnatomyDef
            {
                Id = "mountain_raid", Where = "3장 뒷산 소문 (103동 단톡방)", Post = g => Claim(g, "mountain_raid"),
                Marks = new[]
                {
                    new AnatomyMark("numbers", "수십 명", "누가, 언제 센 숫자인지 없다."),
                    new AnatomyMark("nosource", "숨어 있대요", "본 사람도, 시각도, 사진도 없다."),
                    new AnatomyMark("fear", "!!", "느낌표가 먼저 마음을 흔든다."),
                    new AnatomyMark("pressure", "다들 짐 싸서 빨리 나가래요", "확인할 틈도 없이 짐 싸서 나가라고 재촉한다."),
                },
            },
            new RumorAnatomyDef
            {
                Id = "camp_food", Where = "4장 임시 거처 급식 소문", Post = g => Claim(g, "camp_food"),
                Marks = new[]
                {
                    new AnatomyMark("nosource", "[퍼옴]", "어디서 퍼 왔는지, 처음 쓴 사람이 누구인지 없다."),
                    new AnatomyMark("fear", "가면 굶는다고 함", "확인보다 겁부터 준다. 배식 시간은 행정복지센터에 물으면 알 수 있었다."),
                },
            },
            new RumorAnatomyDef
            {
                Id = "evac_shortcut", Where = "5장 지름길 소문 (동네 단체방)", Post = g => Claim(g, "evac_shortcut"),
                Marks = new[]
                {
                    new AnatomyMark("pressure", "풀림!", "느낌표 한 마디로 '지금 그쪽으로 가라'고 재촉한다."),
                    new AnatomyMark("nosource", "지름길이래요", "누가 가 보고 확인했는지 없다."),
                },
            },
        };

        static string Claim(GameState g, string id)
        {
            try { return Rumors.Posts(g, id)[0].Text; }
            catch (ArgumentException) { return ""; }
        }

        public static RumorAnatomyDef Of(string id) => All.FirstOrDefault(a => a.Id == id);

        /// <summary>Circled numbers for the marks (①…⑤).</summary>
        public static string Num(int i) => i >= 0 && i < 9 ? ((char)('①' + i)).ToString() : $"({i + 1})";

        /// <summary>
        /// The marks found in the post, in the order they appear (each phrase once, no overlaps; a phrase not in the post is
        /// left out, so a post whose wording changed never shows a mark for words it lacks).
        /// </summary>
        public static List<(AnatomyMark mark, int at)> Found(string post, IEnumerable<AnatomyMark> marks)
        {
            var found = new List<(AnatomyMark, int)>();
            if (string.IsNullOrEmpty(post) || marks == null) return found;
            var taken = new List<(int from, int to)>();
            foreach (var m in marks)
            {
                if (string.IsNullOrEmpty(m.Phrase)) continue;
                int from = 0, at = -1;
                while ((at = post.IndexOf(m.Phrase, from, StringComparison.Ordinal)) >= 0)
                {
                    int a = at, b = at + m.Phrase.Length;
                    if (!taken.Any(t => a < t.to && t.from < b)) break;
                    from = at + 1;
                }
                if (at < 0) continue;
                taken.Add((at, at + m.Phrase.Length));
                found.Add((m, at));
            }
            return found.OrderBy(f => f.Item2).ToList();
        }

        /// <summary>
        /// The post with each mark's words in amber and its number after it (rich text). <paramref name="plain"/> gives
        /// the same without tags (read aloud, tests).
        /// </summary>
        public static string Highlight(string post, IEnumerable<AnatomyMark> marks, bool plain = false, float scale = 1f)
        {
            var found = Found(post, marks);
            var sb = new StringBuilder();
            int pos = 0;
            for (int i = 0; i < found.Count; i++)
            {
                var (m, at) = found[i];
                sb.Append(post, pos, at - pos);
                if (plain) sb.Append(m.Phrase).Append(Num(i));
                else sb.Append("<color=#F2B33D><b>").Append(m.Phrase).Append("</b></color><size=").Append(Px(21, scale)).Append("><color=#F2B33D>").Append(Num(i)).Append("</color></size>");
                pos = at + m.Phrase.Length;
            }
            sb.Append(post, pos, post.Length - pos);
            return sb.ToString();
        }

        /// <summary>
        /// A rich-text &lt;size&gt; in pixels at the 글자 크기 setting: a size tag is absolute and the text scaler only
        /// rescales a Text's own font size, so the card scales its tags itself (same rounding as SettingsMath.Scaled).
        /// </summary>
        public static int Px(int size, float scale) => Math.Max(1, (int)Math.Round(size * (scale > 0 ? scale : 1f), MidpointRounding.AwayFromZero));

        /// <summary>"① 숫자 과장 — note" for each mark found, in the post's order.</summary>
        public static List<string> MarkLines(string post, IEnumerable<AnatomyMark> marks)
        {
            var found = Found(post, marks);
            return found.Select((f, i) => $"{Num(i)} {Technique(f.mark.Technique)?.Name ?? f.mark.Technique} — {f.mark.Note}").ToList();
        }

        /// <summary>The technique ids a post shows (found marks, in order).</summary>
        public static List<string> TechniquesIn(GameState g, string id)
        {
            var a = Of(id);
            if (a == null) return new List<string>();
            return Found(a.Post(g), a.Marks).Select(f => f.mark.Technique).Distinct().ToList();
        }

        /// <summary>True once this run took this post apart.</summary>
        public static bool Shown(GameState g, string id) =>
            g?.Events != null && g.Events.Any(e => e.Type == Event && e.Details != null && JsSem.Is(e.Details["id"], id));

        /// <summary>Records 'prebunk' { id, techniques } (once per post per run). Returns the techniques new to this run.</summary>
        public static List<string> Record(GameState g, string id)
        {
            if (g == null || Of(id) == null || Shown(g, id)) return new List<string>();
            var before = Seen(g);
            var t = TechniquesIn(g, id);
            GameRules.RecordEvent(g, Event, new JObject { ["id"] = id, ["techniques"] = new JArray(t) });
            return t.Where(x => !before.Contains(x)).ToList();
        }

        /// <summary>Every technique this run has taken apart, in table order.</summary>
        public static List<string> Seen(GameState g)
        {
            var ids = new HashSet<string>();
            if (g?.Events != null)
                foreach (var e in g.Events)
                    if (e.Type == Event && e.Details?["techniques"] is JArray a)
                        foreach (var t in a) { var s = JsSem.Str(t); if (s != null) ids.Add(s); }
            return Techniques.Select(t => t.Id).Where(ids.Contains).ToList();
        }

        /// <summary>
        /// The archive's example of a technique: one whose words carry it (not punctuation alone) first, then a post the
        /// player has taken apart (<paramref name="taken"/>), then story order.
        /// </summary>
        public static (RumorAnatomyDef post, AnatomyMark mark) Example(string technique, IEnumerable<string> taken = null)
        {
            var mine = new HashSet<string>(taken ?? Enumerable.Empty<string>());
            var best = All.Select((a, i) => (a, i, m: a.Marks.FirstOrDefault(x => x.Technique == technique)))
                .Where(x => x.m != null)
                .OrderBy(x => x.m.Phrase.Any(char.IsLetterOrDigit) ? 0 : 1)
                .ThenBy(x => mine.Contains(x.a.Id) ? 0 : 1)
                .ThenBy(x => x.i)
                .FirstOrDefault();
            return best.a != null ? (best.a, best.m) : (null, null);
        }

        /// <summary>The posts this run has taken apart, in table order.</summary>
        public static List<string> PostsShown(GameState g) => All.Select(a => a.Id).Where(id => Shown(g, id)).ToList();

        /// <summary>The card's lines: the post highlighted, a line per technique, the habit, then the sourced footer.</summary>
        /// <param name="scale">The 글자 크기 setting (TextScaler.Scale): the size tags follow it.</param>
        public static List<string> CardLines(GameState g, string id, Facts facts, float scale = 1f)
        {
            var a = Of(id);
            if (a == null) return new List<string>();
            var post = a.Post(g);
            var lines = new List<string> { $"\"{Highlight(post, a.Marks, false, scale)}\"" };
            lines.AddRange(MarkLines(post, a.Marks));
            lines.Add($"<color=#8fd9cf>{Habit}</color>");
            if (facts != null)
            {
                var cites = HabitFacts.Select(facts.Cite).Where(c => c != null).ToList();
                if (cites.Count > 0)
                    lines.Add($"<size={Px(18, scale)}><color=#94a3b8>{string.Join(" ", cites.Select(c => c.Text))} — {string.Join(" · ", cites.Select(c => c.Source).Distinct())}</color></size>");
                var r = facts.Text(ResearchFact);
                var pub = facts.Source(facts.SourceId(ResearchFact))?.Publisher?.Replace(" (보조 자료)", ""); // the sentence already says (보조 자료)
                if (!string.IsNullOrEmpty(r)) lines.Add($"<size={Px(16, scale)}><color=#94a3b8>왜 수법을 미리 보나 ▸ {r}{(string.IsNullOrEmpty(pub) ? "" : " — " + pub)}</color></size>");
            }
            return lines;
        }

        /// <summary>The card's meta line.</summary>
        public static string Meta(GameState g, string id) => $"이 글이 쓴 수법 {TechniquesIn(g, id).Count}가지 · 수법을 알아 두면 다음 글에 덜 흔들립니다";

        /// <summary>The same card as one sentence to read aloud (no tags, numbers spoken as they are shown).</summary>
        public static string Spoken(GameState g, string id)
        {
            var a = Of(id);
            if (a == null) return "";
            var post = a.Post(g);
            return $"{Title}. {a.Where}. \"{Highlight(post, a.Marks, plain: true)}\" " + string.Join(" ", MarkLines(post, a.Marks)) + " " + Habit.Replace("▸", ":");
        }

        /// <summary>The report row's line: the techniques taken apart, by name.</summary>
        public static string ReportLine(IReadOnlyCollection<string> seen) =>
            $"소문이 쓰는 수법 {seen.Count}가지를 짚어 보았다 ({string.Join(" · ", seen.Select(id => Technique(id)?.Name ?? id))})";
    }
}
