// Pure content builders for the debrief screens (original ui/debrief.js, ui/gallery.js, ui/records.js): they read the
// original option objects (loose JSON, JS rules) and return plain text blocks the screens draw. No Unity here, so the
// .NET harness tests them. Every player-facing string is copied from the original files.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>One list row: a bold label, the text, a smaller line under it, a style class and an optional link.</summary>
    public sealed class DebriefLine
    {
        public string Bold, Text, Small, Class, Url;
        public DebriefLine() { }
        public DebriefLine(string bold, string text, string small = null, string cls = null, string url = null)
        { Bold = bold; Text = text; Small = small; Class = cls; Url = url; }
    }

    /// <summary>A section: heading (icon name as in the original), paragraphs, list rows, or the muted empty line.</summary>
    public sealed class DebriefSection
    {
        public string Icon, Title, Class;
        /// <summary>Paragraphs before the list (fate lines, practice text...).</summary>
        public List<DebriefLine> Paras = new();
        public List<DebriefLine> Items = new();
        /// <summary>Shown muted when there are no items (null = nothing).</summary>
        public string Empty;
        /// <summary>A paragraph after the list (the run line).</summary>
        public DebriefLine Tail;
        public bool TailMuted;
    }

    /// <summary>failCard content.</summary>
    public sealed class FailCardView
    {
        public string Eyebrow = "다시 해 볼 수 있습니다", Title, Lead = "실패는 벌이 아니라 정보입니다. 무엇을 알고 있었고 무엇이 비어 있었는지 확인해 보세요.";
        /// <summary>(icon, label, paragraphs) x3.</summary>
        public List<(string icon, string label, List<string> lines)> Rows = new();
        public List<DebriefLine> Sources = new();
        public string SourcesTitle = "참고한 공식 자료";
        public string Label;
    }

    /// <summary>chapterCard content.</summary>
    public sealed class ChapterCardView
    {
        public string Eyebrow, Title, Label;
        public List<DebriefLine> Payoffs = new();
        public string PayoffsTitle = "준비가 도움이 된 순간", LinesTitle = "이번 장의 기록";
        public List<string> Lines = new();
        public string Empty = "기록할 것이 많지 않았습니다.";

        /// <summary>
        /// The payoffs grouped under one heading per preparation (Bold), in first-seen order, so a bag that helped three
        /// times shows "비상가방" once with its three moments under it.
        /// </summary>
        public List<(string head, List<DebriefLine> items)> PayoffGroups()
        {
            var groups = new List<(string head, List<DebriefLine> items)>();
            foreach (var p in Payoffs)
            {
                var head = p.Bold ?? "";
                int at = groups.FindIndex(g => g.head == head);
                if (at < 0) groups.Add((head, new List<DebriefLine> { p }));
                else groups[at].items.Add(p);
            }
            return groups;
        }
    }

    /// <summary>The 생존 기록 document.</summary>
    public sealed class EndingDocView
    {
        public string Kicker = "연무동 · 한빛아파트 103동 302호 · 정민", Title, Place, Sub = "전쟁 직전의 준비가 전쟁 이후의 선택지를 어떻게 바꾸었는지 돌아봅니다.";
        public List<DebriefSection> Sections = new();
        public string Fiction = "이 기록은 가상의 이야기에서 나온 것입니다. 실제 상황에서는 재난방송·재난문자·지자체 안내를 따르세요.";
        public string Share = "";
    }

    /// <summary>A gallery or records list row (number badge, title, small line, sub-list).</summary>
    public sealed class DebriefListRow
    {
        public string No, Title, Small;
        public bool Seen;
        public List<string> Sub = new();
    }

    public sealed class GalleryViewModel
    {
        public string Eyebrow = "엔딩 기록", Title;
        public List<DebriefListRow> Rows = new();
    }

    public sealed class RecordsViewModel
    {
        public string Eyebrow = "도전 과제와 기록", Title;
        public List<DebriefListRow> Challenges = new();
        public string RankTitle = "내 기록 순위", RankEmpty = "처음부터 엔딩까지 한 번 마치면 순위가 남습니다.";
        public List<DebriefLine> Ranks = new();
        public string BestTitle = "장별 최고 기록";
        public List<DebriefLine> Bests = new();
        public string Note = "순위는 도전 과제가 많은 순, 다시 하기가 적은 순, 시간이 짧은 순입니다. 빠르기보다 안전이 먼저입니다. 기록은 이 기기에만 저장됩니다.";
    }

    public static class DebriefLogic
    {
        /// <summary>나비효과 rows shown at most in the ending document.</summary>
        public const int MaxRipples = 5;

        /// <summary>The chapters of ui/menu.js CHAPTERS ([no, title]) in TIMED order.</summary>
        public static readonly string[,] Chapters =
        {
            { "프롤로그", "평소와 다른 화요일" }, { "1장", "준비" }, { "2장", "정전과 경보" }, { "3장", "버티기" },
            { "4장", "남을까, 떠날까" }, { "5장", "공식 대피" }, { "엔딩", "생존 기록" },
        };

        // ---------- JS helpers ----------

        /// <summary>[].concat(v ?? []).filter((x) => x != null &amp;&amp; x !== '').</summary>
        public static List<JToken> Lines(JToken v)
        {
            var list = new List<JToken>();
            if (JsSem.Nullish(v)) return list;
            if (v is JArray a) list.AddRange(a); else list.Add(v);
            return list.Where(x => !JsSem.Nullish(x) && !(x.Type == JTokenType.String && (string)x == "")).ToList();
        }

        /// <summary>A value as a template literal prints it, with undefined/null as "" (for `x || ''` / `x ?? ''`).</summary>
        static string Or(JToken t, string fallback = "") => JsSem.Truthy(t) ? JsSem.Text(t) : fallback;
        static string Field(JToken o, string key) => o is JObject j ? JsSem.Text(j[key]) : "undefined";
        static JToken Get(JToken o, string key) => o is JObject j ? j[key] : null;

        /// <summary>
        /// A value placed as an element child by dom.js el(): null, undefined and booleans print nothing, arrays are
        /// flattened, anything else is String(v). (Template literals use JsSem.Text, which prints "undefined"/"null".)
        /// </summary>
        public static string Kid(JToken t)
        {
            if (JsSem.Nullish(t) || t.Type == JTokenType.Boolean) return "";
            if (t is JArray a) return string.Concat(a.Select(Kid));
            return JsSem.Text(t);
        }

        /// <summary>A destructuring default: the fallback only when the value is undefined (missing), else String(v).</summary>
        static string Def(JToken t, string fallback = "") => t == null || t.Type == JTokenType.Undefined ? fallback : JsSem.Text(t);

        /// <summary>PREP[id] || id || '준비'.</summary>
        public static string PrepName(JToken id)
        {
            var s = JsSem.Str(id);
            if (s != null && Ripples.Prep.TryGetValue(s, out var name) && !string.IsNullOrEmpty(name)) return name;
            return JsSem.Truthy(id) ? JsSem.Text(id) : "준비";
        }

        /// <summary>r.prep === (p.id ?? p.prep) &amp;&amp; r.effect.text === p.moment.</summary>
        public static bool SameMoment(JToken r, JToken p)
        {
            var pid = Get(p, "id");
            var key = JsSem.Nullish(pid) ? Get(p, "prep") : pid;
            return StrictEq(Get(r, "prep"), key) && StrictEq(Get(Get(r, "effect"), "text"), Get(p, "moment"));
        }

        static bool StrictEq(JToken a, JToken b)
        {
            bool an = a == null || a.Type == JTokenType.Undefined, bn = b == null || b.Type == JTokenType.Undefined;
            if (an || bn) return an && bn;
            if (JsSem.Nullish(a) || JsSem.Nullish(b)) return JsSem.Nullish(a) && JsSem.Nullish(b);
            if (JsSem.IsNumber(a) && JsSem.IsNumber(b)) return JsSem.Number(a) == JsSem.Number(b);
            if (a.Type != b.Type) return false;
            if (a.Type == JTokenType.String || a.Type == JTokenType.Boolean) return JToken.DeepEquals(a, b);
            return ReferenceEquals(a, b);
        }

        /// <summary>"D-1 오전 · cause → D-day 저녁 · effect" with the two "when" parts passed through <paramref name="when"/>.</summary>
        public static string RippleText(JToken r, Func<string, string> when = null)
        {
            when ??= s => s;
            var c = Get(r, "cause"); var e = Get(r, "effect");
            return $"{when(Kid(Get(c, "when")))} {Field(c, "text")} → {when(Kid(Get(e, "when")))} {Field(e, "text")}";
        }

        /// <summary>`${s.publisher ? `${s.publisher} · ` : ''}${s.title}` (failCard source list).</summary>
        public static DebriefLine SourceLine(JToken s)
        {
            var pub = Get(s, "publisher");
            var head = JsSem.Truthy(pub) ? $"{JsSem.Text(pub)} · " : "";
            var url = Get(s, "url");
            var title = Get(s, "title");
            var text = head + (JsSem.Truthy(url) ? JsSem.Text(title) : (JsSem.Nullish(title) ? "" : JsSem.Text(title)));
            return new DebriefLine(null, text, url: JsSem.Truthy(url) ? JsSem.Text(url) : null);
        }

        static List<string> TextLines(JToken v) => Lines(v).Select(Kid).ToList();

        // ---------- failCard ----------

        public static FailCardView FailCard(JObject o)
        {
            o ??= new JObject();
            var title = Or(o["title"]);
            var v = new FailCardView { Title = title != "" ? title : "이번에는 여기까지였습니다", Label = title != "" ? title : "다시 시도" };
            v.Rows.Add(("site", "당시 확인한 정보", TextLines(o["seen"])));
            v.Rows.Add(("eye", "판단에서 놓친 부분", TextLines(o["missed"])));
            v.Rows.Add(("spark", "다음에 시도할 준비", TextLines(o["next"])));
            v.Sources = Lines(o["sources"]).Select(SourceLine).ToList();
            return v;
        }

        // ---------- chapterCard ----------

        public static ChapterCardView ChapterCard(JObject o)
        {
            o ??= new JObject();
            string chapter = Def(o["chapter"]);
            var rows = Lines(o["ripples"]);
            var v = new ChapterCardView { Eyebrow = $"{chapter} 끝", Title = Kid(o["title"]), Label = $"{chapter} 정리 · {Def(o["title"])}" };
            foreach (var p in Lines(o["payoffs"]))
            {
                var r = rows.FirstOrDefault(x => SameMoment(x, p));
                v.Payoffs.Add(new DebriefLine(PrepName(Get(p, "prep")), Or(Get(p, "moment")),
                    r != null ? $"{Field(Get(r, "cause"), "when")} · {Field(Get(r, "cause"), "text")}" : null));
            }
            v.Lines = TextLines(o["lines"]);
            return v;
        }

        // ---------- ending ----------

        /// <summary>
        /// The options of the "ending" screen: the original passes (record, sources); here either { record, sources }
        /// or the record object itself with an optional sources array beside its fields.
        /// </summary>
        public static (JObject record, List<JToken> sources) EndingArgs(JObject o)
        {
            o ??= new JObject();
            if (o["record"] is JObject rec) return (rec, Lines(o["sources"]));
            return (o, Lines(o["sources"]));
        }

        static string SrcText(JToken s) => $"{Field(s, "publisher")} · {Field(s, "title")}";

        /// <summary>Builds the document. factText: fact id -> its text (the original fact(id).text).</summary>
        /// <param name="when">Wraps the two "when" parts of a 나비효과 row (the screen colours them); null keeps them plain.</param>
        public static EndingDocView Ending(JObject record, IList<JToken> sources, Func<string, string> factText, Func<string, string> when = null)
        {
            record ??= new JObject();
            sources ??= new List<JToken>();
            var fate = JsSem.Truthy(record["fate"]) ? record["fate"] : null;
            var practice = record["practice"];
            var run = record["run"];
            var chain = Lines(record["ripples"]).Take(MaxRipples).ToList();
            var rest = Lines(record["helped"]).Where(h => !chain.Any(r => SameMoment(r, h))).ToList();
            JToken src = null;
            if (JsSem.Truthy(Get(practice, "sourceId")))
                src = sources.FirstOrDefault(s => StrictEq(Get(s, "id"), Get(practice, "sourceId")));

            var v = new EndingDocView
            {
                Title = fate != null ? $"엔딩 {(JsSem.Nullish(Get(fate, "no")) ? "" : JsSem.Text(Get(fate, "no")))} · {Field(fate, "title")}" : "생존 기록",
                Place = fate != null && JsSem.Truthy(Get(fate, "place")) ? JsSem.Text(Get(fate, "place")) : null,
                Share = JsSem.Truthy(record["share"]) ? JsSem.Text(record["share"]) : "",
            };

            DebriefSection Section(string icon, string title, List<JToken> items, Func<JToken, DebriefLine> render, string empty, string cls = null)
            {
                var s = new DebriefSection { Icon = icon, Title = title, Class = cls, Empty = empty };
                s.Items.AddRange(items.Select(render));
                return s;
            }
            void FateSection(string cls, string icon, string title, JToken items, Func<JToken, DebriefLine> render)
            {
                var list = Lines(items);
                if (list.Count > 0) v.Sections.Add(Section(icon, title, list, render, null, cls));
            }

            if (fate != null)
                v.Sections.Add(new DebriefSection { Class = "doc-fate", Paras = TextLines(Get(fate, "lines")).Select(t => new DebriefLine(null, t)).ToList() });

            FateSection("trace", "map", "이 결말로 이끈 선택", Get(fate, "trace"), t => TraceLine(t, sources, factText));
            FateSection("fate-people", "people", "그 뒤의 사람들", Get(fate, "people"), p => new DebriefLine(
                JsSem.Truthy(Get(p, "name")) ? Kid(Get(p, "name")) : null, Kid(Get(p, "line")), null,
                JsSem.Is(Get(p, "who"), "stranger") ? "stranger" : JsSem.Truthy(Get(p, "present")) ? "" : "away"));
            var runSec = RunSection(run);
            if (runSec != null) v.Sections.Add(runSec);
            FateSection("", "site", "연무동", Get(fate, "town"), t => new DebriefLine(null, Kid(t)));
            if (chain.Count > 0)
                v.Sections.Add(Section("clock", "나비효과 · 작은 선택이 남긴 것", chain, r => new DebriefLine(Kid(Get(r, "label")), RippleText(r, when), cls: "ripple"), ""));
            if (!(chain.Count > 0 && rest.Count == 0))
                v.Sections.Add(Section("spark", chain.Count > 0 ? "그 밖에 도움이 된 준비" : "도움이 된 준비", rest,
                    h => new DebriefLine(PrepName(Get(h, "prep")), Or(Get(h, "moment"))), "이번에는 미리 해 둔 준비가 큰 역할을 하지 못했습니다. 다음에는 하나만 먼저 해 보세요."));
            v.Sections.Add(Section("box", "쓰지 못한 물건과 그 이유", Lines(record["unused"]),
                u => new DebriefLine(Kid(Get(u, "item")), Or(Get(u, "why"))), "가진 물건을 모두 쓸 수 있었습니다."));
            v.Sections.Add(Section("rumor", "확인하지 못한 정보", Lines(record["unchecked"]),
                t => new DebriefLine(null, Kid(t)), "필요한 정보를 대부분 확인했습니다."));
            if (!(Get(fate, "people") is JArray fp && fp.Count > 0))
                v.Sections.Add(Section("people", "함께 버틴 사람들", Lines(record["people"]),
                    p => new DebriefLine(Kid(Get(p, "who")), Or(Get(p, "what"))), "이번에는 혼자 버텼습니다. 이웃과의 연락 한 가지가 선택지를 넓힙니다."));
            FateSection("", "arrow", "다른 길이 있었다면", Get(fate, "missed"), x => new DebriefLine(Kid(Get(x, "title")), Kid(Get(x, "hint")),
                JsSem.Truthy(Get(x, "tip")) ? $" ({JsSem.Text(Get(x, "tip"))})" : null));
            if (JsSem.Truthy(Get(practice, "text")))
            {
                var s = new DebriefSection { Icon = "official", Title = "실제 생활에서 확인해 볼 한 가지", Class = "doc-practice" };
                s.Paras.Add(new DebriefLine(null, JsSem.Text(Get(practice, "text")), cls: "practice-text"));
                if (src != null)
                    s.Paras.Add(new DebriefLine(null, "출처: " + SrcText(src) + (JsSem.Truthy(Get(src, "checkedAt")) ? $" (확인 {JsSem.Text(Get(src, "checkedAt"))})" : ""),
                        cls: "practice-src", url: JsSem.Truthy(Get(src, "url")) ? JsSem.Text(Get(src, "url")) : null));
                v.Sections.Add(s);
            }
            return v;
        }

        /// <summary>Trace step: when, what you did, and the official line behind it with its sources (first link kept as Url).</summary>
        public static DebriefLine TraceLine(JToken t, IList<JToken> sources, Func<string, string> factText)
        {
            var line = new DebriefLine(Or(Get(t, "when")), Kid(Get(t, "text")));
            var deed = Deeds.Get(JsSem.Str(Get(t, "id")));
            var f = deed?.Facts != null && deed.Facts.Length > 0 ? deed.Facts[0] : null;
            if (!string.IsNullOrEmpty(f))
            {
                var found = Lines(Get(t, "sourceIds"))
                    .Select(id => sources.FirstOrDefault(x => StrictEq(Get(x, "id"), id)))
                    .Where(s => s != null).ToList();
                line.Small = (factText?.Invoke(f) ?? "") + " — " + string.Join(" · ", found.Select(SrcText));
                line.Url = found.Select(s => Get(s, "url")).Where(JsSem.Truthy).Select(u => JsSem.Text(u)).FirstOrDefault();
            }
            return line;
        }

        /// <summary>This run's challenges (new ones marked), two to try next, time and rank; null when the records were not saved.</summary>
        public static DebriefSection RunSection(JToken run)
        {
            var news = Get(run, "news");
            if (!JsSem.Truthy(news)) return null;
            var stats = Get(run, "stats");
            var done = new HashSet<string>(Lines(Get(news, "done")).Select(x => JsSem.Text(x)));
            var fresh = new HashSet<string>(Lines(Get(news, "fresh")).Select(x => JsSem.Text(x)));
            var all = Challenges.All;
            var s = new DebriefSection { Icon = "check", Title = $"도전 과제 {done.Count}/{all.Count}", Class = "doc-run" };
            foreach (var c in all.Where(c => done.Contains(c.Id)))
                s.Items.Add(new DebriefLine(c.Title, fresh.Contains(c.Id) ? "새로 달성" : "달성", cls: fresh.Contains(c.Id) ? "fresh" : ""));
            foreach (var c in all.Where(c => !done.Contains(c.Id)).Take(2))
                s.Items.Add(new DebriefLine($"다음 도전 · {c.Title}", c.Hint, cls: "next"));
            if (JsSem.Truthy(Get(stats, "complete")))
            {
                var rank = Get(news, "rank");
                var bests = Get(news, "bests") as JArray ?? new JArray();
                string BestName(JToken id) => JsSem.Str(id) is string k && Deeds.Ch.TryGetValue(k, out var n) ? n : "";
                var ms = Get(stats, "ms");
                s.Tail = new DebriefLine(null, $"기록 {Challenges.FmtMs(JsSem.IsNumber(ms) ? JsSem.Number(ms) : double.NaN)} · 다시 하기 {Field(stats, "retries")}번"
                    + (JsSem.Truthy(rank) ? $" · 내 기록 {JsSem.Text(rank)}위" : "")
                    + (bests.Count > 0 ? $" · 최고 기록 경신: {string.Join(", ", bests.Select(BestName))}" : ""));
            }
            else { s.Tail = new DebriefLine(null, "이어하기로 마친 판은 전체 시간과 순위가 남지 않습니다."); s.TailMuted = true; }
            return s;
        }

        /// <summary>The document as plain text (what 인쇄하기 saves for printing).</summary>
        public static string PlainText(EndingDocView v)
        {
            var sb = new StringBuilder();
            sb.AppendLine(v.Kicker).AppendLine(v.Title);
            if (v.Place != null) sb.AppendLine(v.Place);
            sb.AppendLine(v.Sub).AppendLine();
            foreach (var s in v.Sections)
            {
                if (s.Title != null) sb.AppendLine($"■ {s.Title}");
                foreach (var p in s.Paras) sb.AppendLine(p.Text + (p.Url != null ? $" ({p.Url})" : ""));
                foreach (var i in s.Items)
                {
                    sb.Append("- ");
                    if (!string.IsNullOrEmpty(i.Bold)) sb.Append(i.Bold).Append("  ");
                    sb.Append(i.Text);
                    if (!string.IsNullOrEmpty(i.Small)) sb.Append($"\n    {i.Small}");
                    if (i.Url != null) sb.Append($" ({i.Url})");
                    sb.AppendLine();
                }
                if (s.Items.Count == 0 && s.Paras.Count == 0 && !string.IsNullOrEmpty(s.Empty)) sb.AppendLine(s.Empty);
                if (s.Tail != null) sb.AppendLine(s.Tail.Text);
                sb.AppendLine();
            }
            sb.AppendLine(v.Fiction);
            return sb.ToString();
        }

        // ---------- gallery ----------

        /// <summary>
        /// gallery(view): options { view: galleryView(...) }; if there is no view, { gallery: stored gallery JSON } is
        /// turned into the view with Endings.GalleryView.
        /// </summary>
        public static JArray GalleryArg(JObject o)
        {
            o ??= new JObject();
            if (o["view"] is JArray view) return view;
            var gal = Endings.GalleryView(Endings.ReadGallery(o["gallery"]));
            return JArray.FromObject(gal);
        }

        public static GalleryViewModel Gallery(JArray view)
        {
            view ??= new JArray();
            var v = new GalleryViewModel();
            int seen = view.Count(e => JsSem.Truthy(Get(e, "seen")));
            int total = view.Count; // the original 11 and the port's endings (Endings.All)
            v.Title = $"연무동의 {Endings.CountWord(total)} 가지 끝 · {seen.ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)}";
            foreach (var e in view)
            {
                bool isSeen = JsSem.Truthy(Get(e, "seen"));
                var row = new DebriefListRow { No = Field(e, "no"), Seen = isSeen };
                if (isSeen)
                {
                    row.Title = Kid(Get(e, "title"));
                    var at = Get(e, "at");
                    row.Small = Field(e, "place") + (JsSem.Truthy(at) ? $" · 처음 본 날 {JsSem.Text(at)}" : "");
                    row.Sub = (Get(e, "shorts") as JArray ?? new JArray()).Select(Kid).ToList();
                }
                else { row.Title = "???"; row.Small = Kid(Get(e, "hint")); }
                v.Rows.Add(row);
            }
            return v;
        }

        // ---------- records ----------

        public static RecordsViewModel Records(Records rec)
        {
            rec ??= new Records();
            var all = Challenges.All;
            string len = all.Count.ToString(CultureInfo.InvariantCulture);
            bool Done(string id) => rec.Done != null && rec.Done.TryGetValue(id, out var d) && !string.IsNullOrEmpty(d);
            var v = new RecordsViewModel { Title = $"도전 과제 {all.Count(c => Done(c.Id)).ToString(CultureInfo.InvariantCulture)}/{len}" };
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                bool d = Done(c.Id);
                v.Challenges.Add(new DebriefListRow
                {
                    No = d ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture), Seen = d, Title = c.Title,
                    Small = d ? $"처음 달성 {rec.Done[c.Id]} · {c.Hint}" : c.Hint,
                });
            }
            var runs = rec.Runs ?? new List<RunRecord>();
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                var e = Endings.ById(r.Ending);
                v.Ranks.Add(new DebriefLine($"{(i + 1).ToString(CultureInfo.InvariantCulture)}위",
                    $"엔딩 {(e != null ? e.No.ToString(CultureInfo.InvariantCulture) : "undefined")} · {e?.Title ?? "undefined"}",
                    $"과제 {r.N.ToString(CultureInfo.InvariantCulture)}/{len} · 다시 하기 {r.Retries.ToString(CultureInfo.InvariantCulture)}번 · {Challenges.FmtMs(r.Ms)} · {r.At}"));
            }
            for (int i = 0; i < Challenges.Timed.Length; i++)
            {
                var id = Challenges.Timed[i];
                bool has = rec.Best != null && rec.Best.TryGetValue(id, out var ms) && ms != 0;
                v.Bests.Add(new DebriefLine(Chapters[i, 0], Chapters[i, 1], has ? Challenges.FmtMs(rec.Best[id]) : "—"));
            }
            return v;
        }
    }
}
