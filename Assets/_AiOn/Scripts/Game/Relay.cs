using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A way into the shelter, and whether it fits 서연's card.</summary>
    public sealed class RelayEntrance
    {
        [JsonProperty("label")] public string Label;
        [JsonProperty("short")] public string Short;
        [JsonProperty("seoyeonOk")] public bool SeoyeonOk;
        /// <summary>The line of 서연's card this entrance goes against; null when it fits her card.</summary>
        [JsonProperty("card", NullValueHandling = NullValueHandling.Ignore)] public string Card;
    }

    /// <summary>The four clock times of the relay ("HH:MM"), kept in the save as G.relay.</summary>
    public sealed class RelayClock
    {
        [JsonProperty("gateSeenAt")] public string GateSeenAt;
        [JsonProperty("sentAt")] public string SentAt;
        [JsonProperty("accessOpenAt")] public string AccessOpenAt;
        [JsonProperty("receivedAt")] public string ReceivedAt;
    }

    /// <summary>What 정민 actually did, which the message may or may not say.</summary>
    public sealed class RelayContext
    {
        /// <summary>The entrance 정민 used: stairs | ramp.</summary>
        [JsonProperty("route")] public string Route = "stairs";
        /// <summary>How the closed east gate was learned: sign | notice_after.</summary>
        [JsonProperty("observedVia")] public string ObservedVia = "sign";
    }

    /// <summary>The compose screen's chips: what the message will say.</summary>
    public sealed class NoteChips
    {
        /// <summary>gate_closed | all_closed | null</summary>
        [JsonProperty("what")] public string What;
        [JsonProperty("time")] public bool Time;
        [JsonProperty("route")] public bool Route;
        [JsonProperty("unknowns")] public bool Unknowns;
        /// <summary>follow_me | direction | check_notice | null</summary>
        [JsonProperty("advice")] public string Advice;
    }

    /// <summary>What a message says, read by rule. Each quote is a whole clause of the message.</summary>
    public sealed class NoteClaims
    {
        /// <summary>gate_closed | all_closed | null</summary>
        [JsonProperty("what")] public string What;
        /// <summary>The time as written ("20:09", "2시 10분"), or null.</summary>
        [JsonProperty("time")] public string Time;
        /// <summary>stairs | ramp | access | null</summary>
        [JsonProperty("route")] public string Route;
        [JsonProperty("unknowns")] public bool Unknowns;
        /// <summary>follow_me | direction | check_notice | null</summary>
        [JsonProperty("advice")] public string Advice;
        /// <summary>Field (what, route, advice, time, unknowns) -> the clause it was read from, in the order found.</summary>
        [JsonProperty("quotes")] public Dictionary<string, string> Quotes = new();
        /// <summary>'rules' for the player's own words, 'chips' for a composed note; only on a handover.</summary>
        [JsonProperty("parsedBy", NullValueHandling = NullValueHandling.Ignore)] public string ParsedBy;
    }

    /// <summary>The message 정민 sent, as kept in the save (G.handover).</summary>
    public sealed class Handover
    {
        [JsonProperty("text")] public string Text;
        /// <summary>A copy of the chips as they were sent.</summary>
        [JsonProperty("chips")] public JObject Chips = new();
        /// <summary>The player wrote the text (false: it was composed from the chips).</summary>
        [JsonProperty("edited")] public bool Edited;
        [JsonProperty("claims")] public NoteClaims Claims;
        /// <summary>precise | partial | generalized | unparsed</summary>
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("actualRoute")] public string ActualRoute;
        [JsonProperty("observedVia")] public string ObservedVia;
        [JsonProperty("gateSeenAt")] public string GateSeenAt;
        [JsonProperty("sentAt")] public string SentAt;
        [JsonProperty("accessOpenAt")] public string AccessOpenAt;
        [JsonProperty("receivedAt")] public string ReceivedAt;
    }

    public sealed class RelayPinAlso
    {
        [JsonProperty("anchor")] public string Anchor;
        [JsonProperty("now")] public string Now;
    }

    /// <summary>One reason the message cannot be applied to 서연 as-is, pinned on a place.</summary>
    public sealed class RelayPin
    {
        /// <summary>body | time | scope | advice | direction | place</summary>
        [JsonProperty("factor")] public string Factor;
        /// <summary>stairs | ramp | access | notice | gate | start1</summary>
        [JsonProperty("anchor")] public string Anchor;
        [JsonProperty("was")] public string Was;
        [JsonProperty("now")] public string Now;
        /// <summary>The body pin also crosses out the other entrance.</summary>
        [JsonProperty("also", NullValueHandling = NullValueHandling.Ignore)] public RelayPinAlso Also;
        /// <summary>Anchor to draw a line to (start0), when the pin compares two places.</summary>
        [JsonProperty("line", NullValueHandling = NullValueHandling.Ignore)] public string Line;
    }

    /// <summary>How one part of the message read from 서연's side.</summary>
    public sealed class ClaimRow
    {
        [JsonProperty("label")] public string Label;
        /// <summary>The clause it was read from; null when the message did not say it.</summary>
        [JsonProperty("quote")] public string Quote;
        /// <summary>helped | recheck | mismatch | missing</summary>
        [JsonProperty("status")] public string Status;
        [JsonProperty("why")] public string Why;
        /// <summary>SOURCES id.</summary>
        [JsonProperty("sourceId")] public string SourceId;
    }

    /// <summary>
    /// The message 정민 sends 서연 from the shelter, and why it cannot be applied to her as-is (original
    /// game/relay.js). Pure logic. Times come from G.relay, set by the D-day chapter from the real clock.
    /// G.relay and G.handover have no typed field: they are kept in <see cref="GameState.Extra"/> under their
    /// original names (<see cref="StoredTimes"/>, <see cref="StoredHandover"/>).
    /// </summary>
    public static class Relay
    {
        /// <summary>"HH:MM" of a minute of the day (the original re-exports state.js hhmm).</summary>
        public static string Hhmm(int minute) => GameRules.Hhmm(minute);

        public static readonly IReadOnlyDictionary<string, RelayEntrance> Entrances = new Dictionary<string, RelayEntrance>
        {
            ["stairs"] = new RelayEntrance { Label = "출입구 A · 계단", Short = "A 계단", SeoyeonOk = false, Card = "계단은 혼자 내려가지 않음" },
            ["ramp"] = new RelayEntrance { Label = "출입구 B · 지하주차장 진출입로", Short = "B 진출입로", SeoyeonOk = false, Card = "가파른 내리막은 혼자 내려가지 않음" },
            ["access"] = new RelayEntrance { Label = "출입구 C · 경사로", Short = "C 경사로", SeoyeonOk = true },
        };

        /// <summary>Longest message the player may write (UTF-16 units, as the original counts).</summary>
        public const int MaxNote = 120;

        /// <summary>The times used when the D-day chapter set none (sent at 20:13).</summary>
        const int DefaultSent = 20 * 60 + 13;

        // ---------- state: G.relay and G.handover ----------

        static T Stored<T>(GameState g, string key) where T : class =>
            g?.Extra != null && g.Extra.TryGetValue(key, out var t) && t is JObject o ? o.ToObject<T>() : null;

        /// <summary>G.relay, or null when the chapter has not set it.</summary>
        public static RelayClock StoredTimes(GameState g) => Stored<RelayClock>(g, "relay");

        /// <summary>Sets G.relay (the D-day chapter: StoreTimes(g, RelayTimes(g.Minute + 2, gateMinute))).</summary>
        public static RelayClock StoreTimes(GameState g, RelayClock times)
        {
            if (times != null) g.Extra["relay"] = JObject.FromObject(times);
            else g.Extra["relay"] = JValue.CreateNull();
            return times;
        }

        /// <summary>G.handover, or null when no message was written.</summary>
        public static Handover StoredHandover(GameState g) => Stored<Handover>(g, "handover");

        /// <summary>G.handover = null: no message (yet), as the chapter sets it before the compose screen.</summary>
        public static void ClearHandover(GameState g) => g.Extra["handover"] = JValue.CreateNull();

        // ---------- times ----------

        /// <summary>
        /// C opens after the message is sent and before 서연 reads it, so the message can never mention it from sight.
        /// gateMinute defaults to three minutes before sending.
        /// </summary>
        public static RelayClock RelayTimes(int sentMinute, int? gateMinute = null)
        {
            int gate = gateMinute ?? sentMinute - 3;
            int sent = Math.Max(sentMinute, gate);
            int open = Math.Max(20 * 60 + 16, sent + 3);
            return new RelayClock { GateSeenAt = Hhmm(gate), SentAt = Hhmm(sent), AccessOpenAt = Hhmm(open), ReceivedAt = Hhmm(open + 1) };
        }

        // ---------- composing ----------

        static string Str(JObject d, string key) => d != null && d[key] is JValue { Type: JTokenType.String } v ? (string)v : null;

        /// <summary>What 정민 actually did: the last of A or B he entered by, and how he learned the east gate was closed.</summary>
        public static RelayContext NoteContext(GameState g)
        {
            var events = g?.Events ?? new List<GameEvent>();
            var entered = events.LastOrDefault(e => e != null && e.Type == "route_attempt" && Str(e.Details, "who") == "jeongmin"
                && (Str(e.Details, "route") == "stairs" || Str(e.Details, "route") == "ramp"));
            var gate = events.FirstOrDefault(e => e != null && e.Type == "gate_observed" && Str(e.Details, "who") == "jeongmin");
            return new RelayContext
            {
                Route = entered != null ? Str(entered.Details, "route") : "stairs",
                ObservedVia = gate != null && Str(gate.Details, "via") == "notice_after" ? "notice_after" : "sign",
            };
        }

        static string Entered(string route) => route == "ramp" ? "나는 지하주차장 진출입로(B)로 들어왔어." : "나는 계단(A)으로 들어왔어.";

        /// <summary>The message the chips make. Never mentions entrance C: it was not open when 정민 wrote.</summary>
        public static string ComposeNote(NoteChips chips = null, string route = "stairs", string observedVia = "sign", string gateSeenAt = "20:09")
        {
            chips ??= new NoteChips();
            gateSeenAt ??= "20:09";
            string when = chips.Time ? $"{gateSeenAt}에 " : "";
            var parts = new List<string>();
            if (chips.What == "gate_closed") parts.Add(observedVia == "notice_after" ? $"{when}도착해서 안내문을 보니 동쪽 출입구는 폐쇄래." : $"{when}동쪽 출입구가 닫혀 있었어.");
            if (chips.What == "all_closed") parts.Add($"{when}대피소가 닫혀 있었어.");
            if (chips.Route) parts.Add(Entered(route));
            if (chips.Unknowns) parts.Add("다른 입구 상태는 확인 못 했어.");
            if (chips.Advice == "follow_me") parts.Add("나처럼 오면 돼.");
            if (chips.Advice == "direction") parts.Add(route == "ramp" ? "연무로 따라 오다가 문화로에서 왼쪽으로 꺾어." : "연무로 따라 서쪽으로 쭉 오면 왼쪽이야.");
            if (chips.Advice == "check_notice") parts.Add("현장 안내를 꼭 다시 확인해.");
            return string.Join(" ", parts);
        }

        // ---------- reading ----------

        // The original's patterns are JavaScript regular expressions without the unicode flag. There \s is the
        // ECMAScript white space set and \w, \d, \b know ASCII only; .NET's own \s \w \d \b follow Unicode (a Hangul
        // syllable would be a word character, so "A로" would hide the A). They are spelled out here instead.
        /// <summary>JS \s as a character class. Built from code points so that no invisible character sits in this file.</summary>
        static readonly string Sp = JsSpaceClass();
        static string JsSpaceClass()
        {
            var sb = new StringBuilder("[");
            sb.Append((char)0x2000).Append('-').Append((char)0x200A);
            foreach (int c in new[] { 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x20, 0xA0, 0x1680, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF }) sb.Append((char)c);
            return sb.Append(']').ToString();
        }
        const string NoWordBefore = "(?<![A-Za-z0-9_])"; // \b in front of a word character
        const string NoWordAfter = "(?![A-Za-z0-9_])";   // \b behind a word character

        static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant);

        /// <summary>'막' alone is often the adverb "just", so only its closed forms count.</summary>
        const string Shut = "폐쇄|닫|막[혀힌힘혔아았]";
        static readonly Regex Unknown = Rx("모르|몰라|몰랐|미확인|확인" + Sp + "*(못|안)|확인하지");
        static readonly Regex East = Rx("동쪽|동문");
        static readonly Regex NotThisWay = Rx("말고|빼고|피해|안" + Sp + "*돼|쓰지" + Sp + "*마|가지" + Sp + "*마");
        static readonly Regex ClockTime = Rx(NoWordBefore + "([01]?[0-9]|2[0-3]):[0-5][0-9]" + NoWordAfter);
        static readonly Regex SpokenTime = Rx("[0-9]{1,2}" + Sp + "*시(?!간)(" + Sp + "*[0-9]{1,2}" + Sp + "*분)?");

        /// <summary>Field -> (value, pattern) in the order tried; the first value with a matching clause wins.</summary>
        static readonly (string Field, (string Value, Regex Pattern)[] Values)[] Rules =
        {
            ("what", new[]
            {
                ("all_closed", Rx("(전체|모두|전부|다)" + Sp + "*(" + Shut + ")|대피(소|시설)[^.,]{0,8}(" + Shut + ")")),
                ("gate_closed", Rx("(동쪽|동문)[^.,]{0,8}(" + Shut + "|통제)")),
            }),
            ("route", new[]
            {
                ("stairs", Rx("계단|" + NoWordBefore + "[Aa]" + NoWordAfter)),
                ("ramp", Rx("진출입로|" + NoWordBefore + "[Bb]" + NoWordAfter)),
                ("access", Rx(NoWordBefore + "[Cc]" + NoWordAfter + "|경사로")),
            }),
            ("advice", new[]
            {
                ("follow_me", Rx("나처럼|따라" + Sp + "*와|똑같이|같은" + Sp + "*길|여기로")),
                ("direction", Rx("오른쪽|왼쪽|쭉|건너|꺾")),
                ("check_notice", Rx("안내[^.]{0,8}확인|확인[^.]{0,8}안내")),
            }),
        };

        /// <summary>Rule-based reading of the message. Each quote is a whole clause, so it is always an exact substring.</summary>
        public static NoteClaims ParseNote(string text)
        {
            var clauses = (text ?? "").Split('.', '!', '?', '\n').Select(JsTrim).Where(c => c.Length > 0).ToList();
            var claims = new NoteClaims();
            foreach (var (field, values) in Rules)
            {
                foreach (var (value, pattern) in values)
                {
                    var clause = clauses.FirstOrDefault(c => pattern.IsMatch(c)
                        && !(value == "all_closed" && (East.IsMatch(c) || Unknown.IsMatch(c)))
                        && !(field == "route" && NotThisWay.IsMatch(c)));
                    if (clause == null) continue;
                    if (field == "what") claims.What = value; else if (field == "route") claims.Route = value; else claims.Advice = value;
                    claims.Quotes[field] = clause;
                    break;
                }
            }
            foreach (var clause in clauses)
            {
                var m = ClockTime.Match(clause);
                if (!m.Success) m = SpokenTime.Match(clause);
                if (!m.Success) continue;
                claims.Time = m.Value;
                claims.Quotes["time"] = clause;
                break;
            }
            var unknown = clauses.FirstOrDefault(c => Unknown.IsMatch(c));
            if (unknown != null) { claims.Unknowns = true; claims.Quotes["unknowns"] = unknown; }
            return claims;
        }

        /// <summary>
        /// generalized (said more than was seen), precise (the gate, when, and what is not known), partial, or
        /// unparsed (nothing could be read).
        /// </summary>
        public static string NoteKind(NoteClaims claims)
        {
            if (claims.What == "all_closed") return "generalized";
            bool timed = !string.IsNullOrEmpty(claims.Time);
            if (claims.What == "gate_closed" && timed && claims.Unknowns) return "precise";
            return !string.IsNullOrEmpty(claims.What) || timed || !string.IsNullOrEmpty(claims.Route) || claims.Unknowns || !string.IsNullOrEmpty(claims.Advice)
                ? "partial" : "unparsed";
        }

        /// <summary>
        /// Writes the message: the player's own text when <paramref name="edited"/> and not blank, else the note the
        /// chips compose. Sets G.handover and records 'handover_written' { kind, edited, length }.
        /// </summary>
        public static Handover MakeHandover(GameState g, NoteChips chips = null, string text = null, bool edited = false)
        {
            if (g?.Events == null) throw new ArgumentException("올바른 게임 기록이 필요합니다.", nameof(g));
            var times = StoredTimes(g) ?? RelayTimes(DefaultSent);
            var c = NoteContext(g);
            string composed = ComposeNote(chips, c.Route, c.ObservedVia, times.GateSeenAt);
            // Player text is untrusted: plain string, no control characters, bounded length.
            string cleaned = "";
            if (edited)
            {
                var sb = new StringBuilder(text ?? "");
                for (int i = 0; i < sb.Length; i++) if (sb[i] <= 0x1F || sb[i] == 0x7F) sb[i] = ' ';
                cleaned = JsTrim(sb.ToString());
                if (cleaned.Length > MaxNote) cleaned = cleaned.Substring(0, MaxNote);
            }
            string finalText = cleaned.Length > 0 ? cleaned : composed;
            var claims = ParseNote(finalText);
            claims.ParsedBy = cleaned.Length > 0 ? "rules" : "chips";
            var handover = new Handover
            {
                Text = finalText,
                Chips = chips != null ? JObject.FromObject(chips) : new JObject(),
                Edited = cleaned.Length > 0,
                Claims = claims,
                Kind = NoteKind(claims),
                ActualRoute = c.Route,
                ObservedVia = c.ObservedVia,
                GateSeenAt = times.GateSeenAt,
                SentAt = times.SentAt,
                AccessOpenAt = times.AccessOpenAt,
                ReceivedAt = times.ReceivedAt,
            };
            GameRules.RecordEvent(g, "handover_written", new JObject { ["kind"] = handover.Kind, ["edited"] = handover.Edited, ["length"] = finalText.Length });
            g.Extra["handover"] = JObject.FromObject(handover);
            return handover;
        }

        // ---------- 서연's side ----------

        static string Quote(NoteClaims claims, string field) => claims.Quotes != null && claims.Quotes.TryGetValue(field, out var q) ? q : "";

        static string GateKnown(Handover h) => h?.ObservedVia == "notice_after"
            ? "도착 후 안내문으로 안 동쪽 출입구 한 곳"
            : $"직접 본 동쪽 출입구 한 곳 ({h?.GateSeenAt ?? ""})";

        /// <summary>Three reasons the message cannot be applied as-is: body, time, and one more (scope/advice/place).</summary>
        public static List<RelayPin> BuildPins(GameState g)
        {
            var h = StoredHandover(g);
            var claims = h?.Claims ?? ParseNote("");
            string actual = !string.IsNullOrEmpty(h?.ActualRoute) ? h.ActualRoute : NoteContext(g).Route;
            // The handover carries its own times; without one, the chapter's (or the default ones).
            var t = h != null
                ? new RelayClock { GateSeenAt = h.GateSeenAt, SentAt = h.SentAt, AccessOpenAt = h.AccessOpenAt, ReceivedAt = h.ReceivedAt }
                : StoredTimes(g) ?? RelayTimes(DefaultSent);
            string named = claims.Route == "stairs" || claims.Route == "ramp" ? claims.Route : null;
            string bodyAt = named ?? actual;
            string other = bodyAt == "stairs" ? "ramp" : "stairs";
            var body = new RelayPin
            {
                Factor = "body", Anchor = bodyAt,
                Was = named != null ? $"“{Quote(claims, "route")}”" : $"메시지에 입구 없음 · 정민이 들어간 입구: {Entrances[actual].Short}",
                Now = $"서연의 카드: {Entrances[bodyAt].Card}",
                Also = new RelayPinAlso { Anchor = other, Now = $"✕ {Entrances[other].Short} · {Entrances[other].Card}" },
            };
            var time = claims.Route == "access"
                ? new RelayPin { Factor = "time", Anchor = "access", Was = $"“{Quote(claims, "route")}”", Now = $"{t.SentAt}의 C · 지금 상태는 안내판에서" }
                : new RelayPin { Factor = "time", Anchor = "notice", Was = $"메시지: {claims.Time ?? "시각 없음"} · 보냄 {t.SentAt}", Now = $"지금 {t.ReceivedAt} · 안내판이 {t.AccessOpenAt}에 바뀜 — 메시지 뒤의 일" };
            RelayPin third;
            if (claims.What == "all_closed") third = new RelayPin { Factor = "scope", Anchor = "gate", Was = $"“{Quote(claims, "what")}”", Now = $"정민이 확인한 것: {GateKnown(h)}" };
            else if (claims.Advice == "follow_me") third = new RelayPin { Factor = "advice", Anchor = "start1", Was = $"“{Quote(claims, "advice")}”", Now = "정민의 다리, 정민의 출발점 기준의 길" };
            else if (claims.Advice == "direction") third = new RelayPin { Factor = "direction", Anchor = "start1", Was = $"“{Quote(claims, "advice")}”", Now = "정민의 출발점(동쪽 아파트) 기준 방향 · 서연은 서쪽 카페에서 출발", Line = "start0" };
            else third = new RelayPin { Factor = "place", Anchor = "start1", Was = "정민: 동쪽 한빛아파트에서 출발", Now = "서연: 서쪽 카페 쉼표에서 출발", Line = "start0" };
            return new List<RelayPin> { body, time, third };
        }

        /// <summary>Two calm closing lines for the re-read, by the kind of message.</summary>
        public static string[] ClosingLines(string kind)
        {
            if (kind == "generalized") return new[] { "정민이 본 것보다 넓은 말이었습니다. 그리고 정민의 시각, 정민의 몸, 정민의 자리에서 쓴 말이었습니다.", "서연은 지금의 안내로 다시 판단합니다." };
            if (kind == "unparsed") return new[] { "서연에게 닿은 것은 이 문장뿐이었습니다.", "서연은 지금의 안내에서 다시 시작합니다." };
            return new[] { "정민의 시각, 정민의 몸, 정민의 자리에서 쓴 말이었습니다.", "서연은 지금의 안내로 다시 판단합니다." };
        }

        /// <summary>How each part of the message read from 서연's side. Status: helped | recheck | mismatch | missing.</summary>
        public static List<ClaimRow> ReadClaims(GameState g)
        {
            var rows = new List<ClaimRow>();
            var h = StoredHandover(g);
            var c = h?.Claims;
            if (c == null) return rows;
            void Add(string label, string quote, string status, string why, string sourceId) =>
                rows.Add(new ClaimRow { Label = label, Quote = quote, Status = status, Why = why, SourceId = sourceId });
            string Said(string field) => c.Quotes != null && c.Quotes.TryGetValue(field, out var q) ? q : null;
            bool timed = !string.IsNullOrEmpty(c.Time);

            if (c.What == "gate_closed") Add("본 것", Said("what"), timed ? "helped" : "recheck", timed ? "본 범위와 시각이 함께 있어 지금 안내와 비교할 수 있었습니다." : "언제 본 것인지 없어 지금도 그런지 판단하기 어려웠습니다.", timed ? "press_code" : "sms_list");
            if (c.What == "all_closed") Add("본 것", Said("what"), "mismatch", $"정민이 확인한 것은 {GateKnown(h)}뿐이었습니다.", "press_code");
            if (c.Route == "stairs" || c.Route == "ramp") Add("들어간 길", Said("route"), "mismatch", "서연의 카드 기준과 다른 입구였습니다.", "koddi_card");
            if (c.Unknowns) Add("모르는 것", Said("unknowns"), "helped", "모르는 부분을 밝혀 서연이 직접 확인할 곳을 알 수 있었습니다.", "press_code");
            if (c.Advice == "follow_me" || c.Advice == "direction") Add("조언", Said("advice"), "mismatch", "쓴 사람의 다리와 출발점 기준의 말이었습니다.", "sms_area");
            if (c.Advice == "check_notice") Add("조언", Said("advice"), "helped", "지금의 현장 안내를 다시 보게 했습니다.", "ps_airraid");
            if (!timed) Add("시각", null, "missing", "언제의 정보인지가 없었습니다.", "sms_list");
            if (!c.Unknowns) Add("모르는 것", null, "missing", "확인하지 못한 부분이 적혀 있지 않았습니다.", "press_code");
            Add("C 경사로", c.Route == "access" ? Said("route") : null, c.Route == "access" ? "recheck" : "missing", $"{h.AccessOpenAt}에 열렸습니다 — 메시지를 보낸 뒤의 일이라 지금의 안내에서만 알 수 있었습니다.", "ps_airraid");
            return rows;
        }

        // ---------- JavaScript string helpers ----------

        /// <summary>JS String.prototype.trim: its white space set is not char.IsWhiteSpace (BOM yes, NEL no).</summary>
        static string JsTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsJsSpace(s[a])) a++;
            while (b > a && IsJsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        static bool IsJsSpace(char c) =>
            (c >= 0x09 && c <= 0x0D) || c == 0x20 || c == 0xA0 || c == 0x1680 || (c >= 0x2000 && c <= 0x200A)
            || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000 || c == 0xFEFF;
    }
}
