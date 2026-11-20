using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>What a challenge's test may look at: the deeds done, the promises at the end, the run's times, endings seen.</summary>
    public sealed class ChallengeContext
    {
        /// <summary>Was this deed done?</summary>
        public Func<string, bool> Has;
        /// <summary>Promises at the end of the run (open ones count as broken).</summary>
        public List<DeedPromise> Ps;
        public RunStats St;
        /// <summary>Different endings this player has seen, this run's included.</summary>
        public int Seen;

        /// <summary>How many of these deeds were done.</summary>
        public int Count(params string[] ids) => ids.Count(Has);
    }

    /// <summary>One challenge (도전 과제).</summary>
    public sealed class ChallengeDef
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("hint")] public string Hint;
        [JsonIgnore] public Func<ChallengeContext, bool> Test;
    }

    /// <summary>Chapter times of a run, from its chapter_time events.</summary>
    public sealed class RunStats
    {
        /// <summary>Chapter id -> the last valid chapter_time details { id, ms, retries, partial? }.</summary>
        [JsonProperty("by")] public Dictionary<string, JObject> By = new();
        [JsonProperty("ms")] public long Ms;
        [JsonProperty("retries")] public long Retries;
        /// <summary>Every chapter was timed from its start (none continued mid-way).</summary>
        [JsonProperty("complete")] public bool Complete;
    }

    /// <summary>One finished run on the top-5 board.</summary>
    public sealed class RunRecord
    {
        [JsonProperty("at")] public string At;
        [JsonProperty("ending")] public string Ending;
        /// <summary>Challenges earned in that run.</summary>
        [JsonProperty("n")] public long N;
        [JsonProperty("ms")] public long Ms;
        [JsonProperty("retries")] public long Retries;
    }

    /// <summary>The stored records: { done: { [challengeId]: day }, best: { [chapter]: ms }, runs: [...] }.</summary>
    public sealed class Records
    {
        [JsonProperty("done")] public Dictionary<string, string> Done = new();
        [JsonProperty("best")] public Dictionary<string, long> Best = new();
        [JsonProperty("runs")] public List<RunRecord> Runs = new();
    }

    /// <summary>What a finished run added to the records.</summary>
    public sealed class RunNews
    {
        /// <summary>Challenges this run earned.</summary>
        [JsonProperty("done")] public List<string> Done = new();
        /// <summary>The ones earned for the first time ever.</summary>
        [JsonProperty("fresh")] public List<string> Fresh = new();
        /// <summary>Chapters whose earlier best time was beaten.</summary>
        [JsonProperty("bests")] public List<string> Bests = new();
        /// <summary>1..5 on the board, or null.</summary>
        [JsonProperty("rank", NullValueHandling = NullValueHandling.Include)] public int? Rank;
    }

    public sealed class RunResult
    {
        [JsonProperty("rec")] public Records Rec;
        [JsonProperty("news")] public RunNews News;
    }

    /// <summary>A finished run, as stored: the ending gallery and the records with it, plus what this run added.</summary>
    public sealed class FinishedRun
    {
        [JsonProperty("gal")] public Gallery Gal;
        [JsonProperty("rec")] public Records Rec;
        [JsonProperty("news")] public RunNews News;
    }

    /// <summary>
    /// Challenges (도전 과제) and run records for coming back (original game/challenges.js): missions read from the
    /// deed ledger as it stood at the ending, chapter times from the chapter_time events the app writes, best times
    /// and a top-5 run board kept per player beside the ending gallery. Runs rank by challenges first, retries
    /// next, time last. CHALLENGES is <see cref="All"/> (a member cannot have its class's name).
    /// </summary>
    public static class Challenges
    {
        public const string PublicUrl = "https://sunny8197783.github.io/AiOn/";
        public static readonly string[] Timed = { "prologue", "prep", "dday", "endure", "crossroads", "evac" };
        const int Top = 5;

        static ChallengeDef C(string id, string title, string hint, Func<ChallengeContext, bool> test) => new() { Id = id, Title = title, Hint = hint, Test = test };

        // test(c): c.Ps = promises at the end, c.St = RunStats, c.Seen = endings this player has seen.
        static readonly ChallengeDef[] Table =
        {
            C("info_first", "공식 안내부터", "정전되면 라디오부터 켜고, 대피소 소문은 공식 안내로 확인하기", c => c.Has("blackout_radio") && c.Has("rumor_checked")),
            C("rumor_stop", "소문은 나에게서 멈춘다", "확인한 내용을 소문을 들은 이웃에게 알려 주기", c => c.Has("rumor_corrected") && !c.Has("rumor_spread")),
            C("precise", "본 것만 정확히", "서연에게 내가 본 시각과 자리만 정확히 전하기", c => c.Has("handover_precise")),
            C("network", "연락망", "서연·박 소장·수아와 비상 연락 방법을 모두 정하기", c => c.Count("seoyeon_contact", "manager_contact", "clerk_contact") == 3),
            C("ask_first", "먼저 묻고 돕기", "도와드리기 전에 먼저 여쭙기, 세 번", c => c.Count("stairs_asked", "register_helped", "queue_carry_elder", "bus_helped") >= 3),
            C("share", "나누며 버티기", "가진 물과 먹을 것을 두 번 이상 이웃과 나누기", c => c.Count("mart_share", "shelter_water_given", "stock_to_manager") >= 2),
            C("elder", "304호를 잊지 않았다", "304호 어르신을 세 번 이상 챙기기", c => c.Count("elder_met", "elder_candle_tip", "elder_alert_knock", "elder_alert_told", "queue_carry_elder", "elder_visit", "fire_warned") >= 3),
            C("promises", "지킨 약속", "이웃과 약속을 두 개 이상 하고, 하나도 어기지 않기", c => c.Ps.Count >= 2 && c.Ps.All(p => p.State == "kept")),
            C("fire", "불 앞의 판단", "복도에 불이 나면 \"불이야!\" 외치고, 끌지 물러날지 판단하기", c => c.Has("fire_warned") && c.Has("fire_handled")),
            C("no_hoard", "사재기 없이", "사재기도, 값을 남기지 않고 가져온 물건도 없이 끝까지", c => !c.Has("hoard") && !c.Has("store_took")),
            // Port change (competition upgrade): nobody can be hit any more, so the challenge is about the voice
            C("hands_off", "끝까지 차분하게", "낯선 사람이 먹을 것을 달라고 해도 언성을 높이지 않기", c => !c.Has("violence") && !c.Has("looter_fought") && !c.Has("looter_argued")),
            C("clean", "한 번에", "한 번도 다시 하지 않고 처음부터 엔딩까지", c => c.St.Complete && c.St.Retries == 0),
            C("endings3", "세 갈래 길", "서로 다른 엔딩 세 개 보기", c => c.Seen >= 3),
        };

        /// <summary>The challenges in order (the original CHALLENGES).</summary>
        public static IReadOnlyList<ChallengeDef> All => Table;

        static bool OkMs(JToken v) => JsSem.IsInt(v) && JsSem.Number(v) > 0 && JsSem.Number(v) < 1e9;

        /// <summary>Challenges first, then fewer retries, then less time. Stable: an equal earlier run stays ahead.</summary>
        static List<RunRecord> Ranked(IEnumerable<RunRecord> runs) =>
            runs.OrderByDescending(r => r.N).ThenBy(r => r.Retries).ThenBy(r => r.Ms).Take(Top).ToList();

        /// <summary>"1분 05초", or "1시간 2분" from an hour up.</summary>
        public static string FmtMs(double ms)
        {
            long s = (long)Num.JsRound(ms / 1000), h = s / 3600, m = s / 60 % 60;
            return h != 0
                ? $"{h.ToString(CultureInfo.InvariantCulture)}시간 {m.ToString(CultureInfo.InvariantCulture)}분"
                : $"{m.ToString(CultureInfo.InvariantCulture)}분 {(s % 60).ToString("00", CultureInfo.InvariantCulture)}초";
        }

        /// <summary>Chapter times of the run. Complete = every chapter timed from its start.</summary>
        public static RunStats RunStats(GameState g)
        {
            var st = new RunStats();
            foreach (var e in g.Events)
            {
                if (e.Type != "chapter_time") continue;
                var d = Deeds.Det(e);
                var id = JsSem.Str(d["id"]);
                if (id != null && Array.IndexOf(Timed, id) >= 0 && OkMs(d["ms"])) st.By[id] = d;
            }
            double retries = 0;
            foreach (var t in st.By.Values)
            {
                st.Ms += (long)JsSem.Number(t["ms"]);
                if (JsSem.IsNumber(t["retries"])) retries += JsSem.Number(t["retries"]);
            }
            st.Retries = (long)retries; // a count the app writes: whole numbers
            st.Complete = Timed.All(id => st.By.TryGetValue(id, out var t) && !JsSem.Truthy(t["partial"]));
            return st;
        }

        /// <summary>Ids of the challenges this run earned. seen: different endings seen so far, this one included.</summary>
        public static List<string> RunChallenges(GameState g, int seen = 0)
        {
            var done = new HashSet<string>(Deeds.Of(g).Select(d => d.Id));
            var c = new ChallengeContext { Has = done.Contains, Ps = Deeds.Promises(g, final: true), St = RunStats(g), Seen = seen };
            return Table.Where(ch => ch.Test(c)).Select(ch => ch.Id).ToList();
        }

        // ---------- stored records ----------

        /// <summary>Sanitises the stored records text; anything unexpected is dropped.</summary>
        public static Records ReadRecords(string raw) => Sanitise(JsSem.Parse(raw));

        /// <summary>Sanitises parsed records (a JSON string value is read as the stored text).</summary>
        public static Records ReadRecords(JToken raw) => Sanitise(raw != null && raw.Type == JTokenType.String ? JsSem.Parse((string)raw) : raw);

        /// <summary>A sanitised copy of live records.</summary>
        public static Records ReadRecords(Records rec) => Sanitise(rec == null ? null : JToken.FromObject(rec));

        static Records Sanitise(JToken raw)
        {
            var r = raw as JObject;
            var rec = new Records();
            if (r?["done"] is JObject done)
                foreach (var p in done.Properties())
                    if (Table.Any(c => c.Id == p.Name) && JsSem.Str(p.Value) is string at && Endings.DayText.IsMatch(at)) rec.Done[p.Name] = at;
            if (r?["best"] is JObject best)
                foreach (var p in best.Properties())
                    if (Array.IndexOf(Timed, p.Name) >= 0 && OkMs(p.Value)) rec.Best[p.Name] = (long)JsSem.Number(p.Value);
            if (r?["runs"] is JArray runs)
                rec.Runs = Ranked(runs.OfType<JObject>()
                    .Where(x => JsSem.Str(x["at"]) is string at && Endings.DayText.IsMatch(at) && Endings.ById(JsSem.Str(x["ending"])) != null
                        && JsSem.IsInt(x["n"]) && JsSem.Number(x["n"]) >= 0 && JsSem.Number(x["n"]) <= Table.Length && OkMs(x["ms"])
                        && JsSem.IsInt(x["retries"]) && JsSem.Number(x["retries"]) >= 0 && JsSem.Number(x["retries"]) < 9e15)
                    .Select(x => new RunRecord
                    {
                        At = (string)x["at"], Ending = (string)x["ending"], N = (long)JsSem.Number(x["n"]), Ms = (long)JsSem.Number(x["ms"]), Retries = (long)JsSem.Number(x["retries"]),
                    }));
            return rec;
        }

        /// <summary>
        /// Adds a finished run to the records. News: done, fresh (first time ever), bests (beat an earlier best),
        /// rank (1..5 or null). seen: different endings seen so far; at: today, yyyy-MM-dd.
        /// </summary>
        public static RunResult AddRun(Records raw, GameState g, string ending, int seen, string at)
        {
            var rec = ReadRecords(raw);
            var st = RunStats(g);
            var done = RunChallenges(g, seen);
            var fresh = done.Where(id => !rec.Done.ContainsKey(id)).ToList();
            foreach (var id in fresh) rec.Done[id] = at;
            var bests = new List<string>();
            foreach (var (id, t) in st.By)
            {
                long ms = (long)JsSem.Number(t["ms"]);
                bool had = rec.Best.TryGetValue(id, out var best);
                if (JsSem.Truthy(t["partial"]) || (had && best <= ms)) continue;
                if (had) bests.Add(id);
                rec.Best[id] = ms;
            }
            int? rank = null;
            if (st.Complete && Endings.ById(ending) != null && at != null && Endings.DayText.IsMatch(at))
            {
                var mine = new RunRecord { At = at, Ending = ending, N = done.Count, Ms = st.Ms, Retries = st.Retries };
                rec.Runs = Ranked(rec.Runs.Append(mine));
                int i = rec.Runs.IndexOf(mine);
                rank = i >= 0 ? i + 1 : (int?)null;
            }
            return new RunResult { Rec = rec, News = new RunNews { Done = done, Fresh = fresh, Bests = bests, Rank = rank } };
        }

        public static RunResult AddRun(string raw, GameState g, string ending, int seen, string at) => AddRun(ReadRecords(raw), g, ending, seen, at);

        /// <summary>
        /// A finished run, as stored: the ending goes into the gallery and the run into the records, together.
        /// galleryRaw / recordsRaw: the stored JSON texts (null on a first run). factSource: fact id -> source id.
        /// </summary>
        public static FinishedRun FinishRun(GameState g, string galleryRaw, string recordsRaw, string at, Func<string, string> factSource) =>
            FinishRun(g, Endings.ReadGallery(galleryRaw), ReadRecords(recordsRaw), at, factSource);

        public static FinishedRun FinishRun(GameState g, Gallery gallery, Records records, string at, Func<string, string> factSource)
        {
            var fate = Endings.PickEnding(g, factSource);
            var gal = Endings.AddToGallery(Endings.ReadGallery(gallery), fate.Id, fate.Trace.Select(t => t.Id), at);
            var r = AddRun(records, g, fate.Id, gal.Seen.Count, at);
            return new FinishedRun { Gal = gal, Rec = r.Rec, News = r.News };
        }

        // ---------- share ----------

        /// <summary>
        /// Wordle-style result to paste to friends: the same squares for the same challenges, so runs compare at a
        /// glance. done: ids of the challenges this run earned (null = none known).
        /// </summary>
        public static string ShareText(Fate fate, IEnumerable<string> done, RunStats st)
        {
            var set = new HashSet<string>(done ?? Enumerable.Empty<string>());
            var lines = new List<string>
            {
                $"〈경보 이전〉 엔딩 {fate.No.ToString(CultureInfo.InvariantCulture)} · {fate.Title}",
                $"도전 과제 {set.Count.ToString(CultureInfo.InvariantCulture)}/{Table.Length.ToString(CultureInfo.InvariantCulture)} {string.Concat(Table.Select(c => set.Contains(c.Id) ? "🟩" : "⬜"))}",
            };
            if (st.Complete) lines.Add($"기록 {FmtMs(st.Ms)} · 다시 하기 {st.Retries.ToString(CultureInfo.InvariantCulture)}번");
            lines.Add(PublicUrl);
            return string.Join("\n", lines);
        }

        public static string ShareText(Fate fate, RunNews news, RunStats st) => ShareText(fate, news?.Done, st);

        /// <summary>news: the details of a 'run_recorded' event ({ done: [...] , ... }), or null.</summary>
        public static string ShareText(Fate fate, JObject news, RunStats st) =>
            ShareText(fate, (news?["done"] as JArray)?.Select(JsSem.Text), st);
    }
}
