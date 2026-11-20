using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A moment of the run as a ripple shows it: when ("D-1 오전") and what happened.</summary>
    public sealed class RippleMoment
    {
        [JsonProperty("day")] public int Day;
        [JsonProperty("minute")] public int Minute;
        [JsonProperty("when")] public string When;
        [JsonProperty("text")] public string Text;
    }

    /// <summary>One "D-1 cause -> D+n effect" pair: a preparation that paid off and the earlier moment that set it up.</summary>
    public sealed class RippleRow
    {
        /// <summary>Preparation id (a key of <see cref="Ripples.Prep"/>).</summary>
        [JsonProperty("prep")] public string Prep;
        [JsonProperty("label")] public string Label;
        [JsonProperty("cause")] public RippleMoment Cause;
        [JsonProperty("effect")] public RippleMoment Effect;
        /// <summary>Minutes between cause and effect.</summary>
        [JsonProperty("gap")] public int Gap;
    }

    /// <summary>An event type that counts as the cause of a payoff.</summary>
    public sealed class CauseRule
    {
        public string Type;
        /// <summary>Extra test on the event's details; null = any event of the type.</summary>
        public Func<JObject, bool> When;
        /// <summary>What the player did, in the player's words.</summary>
        public string Text;
    }

    /// <summary>
    /// 나비효과 기록 (original game/ripples.js): pairs each preparation that paid off (a 'payoff' event) with the
    /// earlier moment that set it up, so the player sees "D-1 cause -> D+n effect". Pure, built from g.Events only.
    /// The original's ripples(G) is <see cref="Of"/>: a C# class cannot have a member with its own name.
    /// </summary>
    public static class Ripples
    {
        /// <summary>A payoff passes a preparation id; the player sees these Korean names (the debrief shows them too).</summary>
        public static readonly IReadOnlyDictionary<string, string> Prep = new Dictionary<string, string>
        {
            ["bag"] = "비상가방", ["cash"] = "현금", ["docs"] = "비상연락처 카드·서류 사본", ["entrances"] = "대피소 출입구 확인", ["extinguisher"] = "소화기 위치·사용 순서",
            ["flashlight"] = "손전등 건전지", ["judgement"] = "화재 상황 판단", ["powerbank"] = "보조배터리 충전", ["radio"] = "라디오 주파수·건전지",
            ["route"] = "경로 확인", ["tub"] = "받아 둔 물", ["water_jug"] = "접이식 물통", ["curtain"] = "암막 커튼", ["towel"] = "젖은 수건", ["elder"] = "304호 어르신과 함께",
            ["gas"] = "가스 밸브 위치",
            ["partition"] = "경량칸막이 앞 비우기", // port addition (PortAdded)
            ["extgauge"] = "소화기 압력계 점검", // port addition (PortAdded)
        };

        /// <summary>Prep ids this port added after the original's (the competition upgrade): the golden table test skips them.</summary>
        public static readonly IReadOnlyList<string> PortAdded = new[] { "partition", "extgauge" };

        /// <summary>Label for a prep id that has a cause but no name (the original's fallback).</summary>
        const string UnnamedPrep = "준비";

        static string Str(JObject d, string key) => d[key] is JValue { Type: JTokenType.String } v ? (string)v : null;
        static bool Is(JObject d, string key, string value) => Str(d, key) == value;
        static bool Says(JObject d, string key, string part) => Str(d, key)?.Contains(part) == true;
        /// <summary>The bought cart holds at least one of the item.</summary>
        static Func<JObject, bool> Has(string item) => d =>
            d["cart"] is JObject cart && cart[item] is JValue { Type: JTokenType.Integer or JTokenType.Float } n && (double)n > 0;
        static CauseRule Rule(string type, string text, Func<JObject, bool> when = null) => new() { Type = type, When = when, Text = text };

        /// <summary>
        /// Per preparation, the event types that count as its cause, for every <see cref="Prep"/> id but 'judgement'
        /// (no single cause).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, CauseRule[]> Causes = new Dictionary<string, CauseRule[]>
        {
            ["bag"] = new[] { Rule("bag_packed", "비상가방을 꾸려 현관에 두었다") },
            ["docs"] = new[] { Rule("docs_done", "비상연락처 카드와 서류 사본을 정리했다") },
            ["entrances"] = new[] { Rule("shelter_checked", "대피소 출입구를 직접 둘러봤다") },
            ["route"] = new[]
            {
                Rule("shelter_checked", "대피소까지 가는 길을 미리 걸어 봤다"),
                Rule("encounter", "돌아가는 경로를 지도에 정해 두었다", d => Is(d, "tool", "route")),
            },
            ["radio"] = new[] { Rule("check", "라디오에 건전지를 넣어 두었다", d => Is(d, "id", "radio")) },
            ["flashlight"] = new[] { Rule("check", "손전등에 건전지를 넣어 두었다", d => Is(d, "id", "flashlight")) },
            ["powerbank"] = new[]
            {
                Rule("check", "보조배터리를 충전해 두었다", d => Is(d, "id", "powerbank")),
                Rule("help", "서연과 보조배터리를 나눴다", d => Is(d, "who", "seoyeon") && Says(d, "what", "보조배터리")),
            },
            ["tub"] = new[] { Rule("tub_filled", "큰 통과 냄비에 물을 받아 두었다") },
            ["water_jug"] = new[] { Rule("buy", "접이식 물통을 사 두었다", Has("water_jug")) },
            ["cash"] = new[] { Rule("cash", "현금을 찾아 두었다"), Rule("buy", "현금을 찾아 두었다", Has("cash")) },
            ["extinguisher"] = new[] { Rule("encounter", "복도 소화기 사용 순서를 익혀 두었다", d => Is(d, "tool", "extinguisher") && Is(d, "result", "practice")) },
            ["curtain"] = new[] { Rule("home_ready", "창문에 암막 커튼을 달아 두었다") },
            ["elder"] = new[]
            {
                Rule("help", "304호 어르신과 함께 계단을 내려갔다", d => Is(d, "who", "elder")),
                Rule("help", "박 소장에게 304호 어르신을 알렸다", d => Is(d, "who", "manager") && Says(d, "what", "304호")),
            },
            ["towel"] = new[] { Rule("tub_filled", "큰 통에 물을 받아 두었다") },
            ["gas"] = new[] { Rule("gas_checked", "주방 가스 중간 밸브 위치를 확인해 두었다") },
            ["partition"] = new[] { Rule("partition_cleared", "베란다 경량칸막이 앞 상자를 치워 두었다") },
            ["extgauge"] = new[] { Rule("ext_gauge", "복도 소화기 압력계를 보고 관리사무소에 알렸다", d => Is(d, "action", "report")) },
        };

        static string DayText(int d) => d == 0 ? "D-day" : d < 0 ? "D" + d.ToString(CultureInfo.InvariantCulture) : "D+" + d.ToString(CultureInfo.InvariantCulture);
        static string PartText(int m) => m < 360 ? "새벽" : m < 720 ? "오전" : m < 1080 ? "오후" : m < 1260 ? "저녁" : "밤";

        /// <summary>'D-1 오전' style label.</summary>
        public static string WhenText(int day, int minute) => $"{DayText(day)} {PartText(minute)}";

        static int Abs(GameEvent e) => e.Day * 1440 + e.Minute;
        static RippleMoment Moment(GameEvent e, string text) => new() { Day = e.Day, Minute = e.Minute, When = WhenText(e.Day, e.Minute), Text = text };

        static readonly JObject NoDetails = new();

        /// <summary>The latest event before <paramref name="beforeSeq"/> that one of the prep's rules accepts.</summary>
        static bool FindCause(GameState g, string prep, int? beforeSeq, out GameEvent cause, out string text)
        {
            cause = null; text = null;
            if (g?.Events == null || prep == null || !Causes.TryGetValue(prep, out var rules)) return false;
            for (int i = g.Events.Count - 1; i >= 0; i--)
            {
                var e = g.Events[i];
                if (e == null || (beforeSeq.HasValue && e.Seq >= beforeSeq.Value)) continue;
                var details = e.Details ?? NoDetails;
                var rule = rules.FirstOrDefault(r => r.Type == e.Type && (r.When == null || r.When(details)));
                if (rule != null) { cause = e; text = rule.Text; return true; }
            }
            return false;
        }

        /// <summary>
        /// The moment that set a preparation up, or null: the latest cause event (before event number
        /// <paramref name="beforeSeq"/>, when given). The payoff toast stamps it as "(when · text)".
        /// </summary>
        public static RippleMoment CauseOf(GameState g, string prep, int? beforeSeq = null) =>
            FindCause(g, prep, beforeSeq, out var e, out var text) ? Moment(e, text) : null;

        /// <summary>
        /// The original ripples(G): one row per prep id, for its FIRST payoff, using the latest cause event before it;
        /// sorted by gap (minutes between cause and effect), largest first. Payoffs without a recorded cause are left out.
        /// </summary>
        public static List<RippleRow> Of(GameState g)
        {
            var first = new List<KeyValuePair<string, GameEvent>>();
            var seen = new HashSet<string>();
            foreach (var e in g.Events)
            {
                if (e == null || e.Type != "payoff" || e.Details == null) continue;
                var prep = Str(e.Details, "prep");
                if (!string.IsNullOrEmpty(prep) && seen.Add(prep)) first.Add(new KeyValuePair<string, GameEvent>(prep, e));
            }
            var rows = new List<RippleRow>();
            foreach (var (prep, eff) in first)
            {
                if (!FindCause(g, prep, eff.Seq, out var cause, out var text)) continue;
                rows.Add(new RippleRow
                {
                    Prep = prep,
                    Label = Prep.TryGetValue(prep, out var label) ? label : UnnamedPrep,
                    Cause = Moment(cause, text),
                    Effect = Moment(eff, Str(eff.Details, "moment") ?? ""),
                    Gap = Abs(eff) - Abs(cause),
                });
            }
            // Prep ids are unique here, so gap then id (code-unit order, like the original '<') is a total order.
            return rows.OrderByDescending(r => r.Gap).ThenBy(r => r.Prep, StringComparer.Ordinal).ToList();
        }
    }
}
