using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One judged action of the run: what happened, whether it matched the guidance, and the fact that says why.</summary>
    public sealed class ReportRow
    {
        public string Id, Axis, Label, FactId;
        public bool Ok;
    }

    public sealed class ReportAxis
    {
        public string Id, Label;
        public int Ok, Total;
        public float Score => Total == 0 ? 0f : (float)Ok / Total;
    }

    public sealed class LearningReportData
    {
        public List<ReportRow> Rows = new();
        public List<ReportAxis> Axes = new();
        public int Ok, Total;
        /// <summary>Share of the judged actions that matched the guidance, 0..1 (0 when nothing was judged).</summary>
        public float Rate => Total == 0 ? 0f : (float)Ok / Total;
        /// <summary>Game minutes from the air-raid alert to arriving at the shelter; null when the run never got there.</summary>
        public int? GoldenMinutes;
        public bool? GoldenOk;
        public double Health, Thirst, Fatigue, Tension;
    }

    /// <summary>
    /// The 생존 학습 리포트: the run's recorded events (GameState.Events) read against the guidance the game teaches.
    /// Nothing is estimated: a row appears only when the run holds the event that decides it, the headline figure is the
    /// share of those rows that matched (not a survival probability, which the game does not model), and every row
    /// points at a FACTS id for its explanation. Wording describes what happened, without blame.
    /// </summary>
    public static class LearningReport
    {
        /// <summary>
        /// Minutes from the alert within which arriving counts as in time: the story's own lateness rule (DDay: late = minute >
        /// Alert + 15), NOT an official standard. No checked source sets a shelter-arrival time, so screens call it "이야기 속
        /// 제한" and never "기준" or "골든타임".
        /// </summary>
        public const int GoldenLimit = 15;

        public static readonly (string id, string label)[] AxisDefs =
        {
            ("prep", "사전 대비"), ("alert", "경보 대응"), ("fire", "화재·연기"), ("info", "정보 판단"), ("endure", "버티기"),
            ("care", "응급 처치"), // competition upgrade: the shelter's bystander CPR
        };

        public static LearningReportData Build(GameState g)
        {
            var r = new LearningReportData();
            if (g == null) return r;
            var ev = g.Events ?? new List<GameEvent>();
            bool Has(string type) => ev.Any(e => e.Type == type);
            GameEvent First(string type) => ev.FirstOrDefault(e => e.Type == type);
            bool Any(string type, Func<JObject, bool> pred) => ev.Any(e => e.Type == type && e.Details != null && pred(e.Details));
            void Row(string axis, string id, bool ok, string yes, string no, string fact) =>
                r.Rows.Add(new ReportRow { Axis = axis, Id = id, Ok = ok, Label = ok ? yes : no, FactId = fact });
            static bool True(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;
            static bool False(JToken t) => t != null && t.Type == JTokenType.Boolean && !(bool)t;
            static string JStr(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

            bool tested = Has("blackout") || Has("alert"); // the preparations were put to the test

            // ---- 사전 대비 ----
            if (tested)
            {
                Row("prep", "bag", Has("bag_packed"), "생존배낭을 미리 꾸려 두었다", "생존배낭을 미리 꾸리지 않았다", "bag_days");
                Row("prep", "tub", Has("tub_filled"), "단수에 대비해 욕조에 물을 받아 두었다", "물을 미리 받아 두지 않았다", "home_tub");
                Row("prep", "shelter", Has("shelter_checked"), "대피소 위치를 미리 확인했다", "대피소 위치를 미리 확인하지 않았다", "shelter_know");
            }
            if (Has("radio_blocked") || Has("battery_dead")) Row("prep", "batteries", false, null, "필요한 순간에 건전지가 없었다", "radio_batt");
            else if (Any("check", d => d["charge"] != null)) Row("prep", "batteries", true, "라디오·손전등 건전지를 확인해 두었다", null, "radio_batt");
            if (Has("expiry_checked")) Row("prep", "expiry", true, "비상식량의 유통기한을 확인했다", null, "bag_nocook");
            if (Has("partition_cleared")) Row("prep", "partition", true, "베란다 경량칸막이 앞 상자를 치워 두었다", null, "fire_partition_clear");

            // ---- 경보 대응 ----
            var alert = First("alert");
            var arrived = First("arrived");
            if (alert != null && arrived != null)
            {
                int a = (int?)alert.Details?["minute"] ?? alert.Minute, b = (int?)arrived.Details?["minute"] ?? arrived.Minute;
                int minutes = Math.Max(0, b - a);
                bool late = arrived.Details?["late"] != null ? True(arrived.Details["late"]) : minutes > GoldenLimit;
                r.GoldenMinutes = minutes; r.GoldenOk = !late;
                // GoldenLimit is this story's own time limit, not an official standard: the label says so, and the fact explains why
                // speed matters (공습경보: 즉시 대피).
                Row("alert", "golden", !late, $"경보 {minutes}분 만에 대피소에 도착했다 (이야기 속 제한 {GoldenLimit}분)", $"대피소까지 {minutes}분이 걸렸다 (이야기 속 제한 {GoldenLimit}분)", "alert_airraid");
                bool lift = Any("elevator_tried", d => !True(d["fire"]));
                Row("alert", "stairs", !lift, "엘리베이터 대신 계단으로 내려갔다", "경보 중에 엘리베이터를 타려 했다", "alert_stairs");
            }
            var dep = First("departure");
            string bag = dep?.Details?["bag"]?.Type == JTokenType.String ? (string)dep.Details["bag"] : null;
            if (bag == "packed") Row("alert", "takeBag", true, "생존배낭을 들고 나섰다", null, "bag_take");
            else if (bag == "none") Row("alert", "takeBag", false, null, "생존배낭 없이 나섰다", "bag_take");
            else if (bag == "late") Row("alert", "takeBag", false, null, "경보가 울린 뒤에 배낭을 꾸리느라 출발이 늦었다", "bag_take");
            if (Has("candle_lit")) Row("alert", "candle", Has("candle_out"), "촛불을 끄고 집을 나섰다", "촛불을 켜 둔 채 집을 나섰다", "candle_rules");
            // 「22분」 (D-1 morning, competition upgrade): the order of the response, never whether the alert was real
            var tt = TwentyTwoLogic.PicksOf(g);
            if (tt != null)
            {
                var ttRows = TwentyTwoLogic.Rows(tt);
                bool TtOk(string id) => ttRows.Any(x => x.Id == id && x.Ok);
                // good needs the elder row (TwentyTwoLogic.Verdict), so the label claims only what was done
                Row("alert", "ttOrder", TwentyTwoLogic.Verdict(tt) == "good", "오발령 22분: 확인하고, 준비하고, 노약자부터 챙기는 순서로 대응했다",
                    "오발령 22분: 대응 순서가 흐트러졌다", "alert_caution");
                Row("info", "ttOfficial", TtOk("official"), "이유도 장소도 없는 경보 문자를 공식 방송으로 먼저 확인했다", "이유도 장소도 없는 경보 문자를 공식 방송으로 먼저 확인하지 않았다", "radio_listen");
                if (tt.Contains("chat")) Row("info", "ttRumor", false, null, "오발령 아침, 확인되지 않은 말을 단톡방에 올렸다", "info_rumor");
                if (tt.Contains("redial")) Row("info", "ttCalls", false, null, "오발령 아침, 안 되는 전화를 계속 걸었다", "comms_calls");
            }

            // ---- 화재·연기 ----
            var smoke = First("smoke_escape");
            if (smoke?.Details != null)
            {
                var d = smoke.Details;
                if (d["towel"] != null) Row("fire", "towel", True(d["towel"]), "젖은 수건으로 코와 입을 막았다", "코와 입을 막지 않고 연기 속을 지났다", "fire_big");
                if (d["crouch"] != null) Row("fire", "crouch", True(d["crouch"]), "몸을 낮춰 연기 아래로 이동했다", "몸을 낮추지 않고 연기 속을 지났다", "fire_big");
                bool lift = Any("elevator_tried", x => True(x["fire"]));
                Row("fire", "fireStairs", !lift, "계단으로 대피했다", "화재 대피 중에 엘리베이터를 타려 했다", "fire_lift");
            }
            if (Has("fire_shout")) Row("fire", "shout", true, "\"불이야!\" 하고 주변에 알렸다", null, "fire_shout");
            // competition upgrade: 불나면 살펴서 대피 (LookFirstScene) and the 119 call put together from cards
            var look = ev.FirstOrDefault(e => e.Type == "lookfirst" && e.Details != null && True(e.Details["played"]));
            if (look != null)
            {
                var d = look.Details;
                int located = d["located"] != null && (d["located"].Type == JTokenType.Integer || d["located"].Type == JTokenType.Float) ? (int)(double)d["located"] : 0;
                if (d["where"]?.Type == JTokenType.String)
                    Row("fire", "lookWhere", True(d["whereOk"]) && located >= 2, "문을 열기 전에 단서를 살펴 불이 난 곳을 짚었다", "단서를 충분히 살피지 않고 불이 난 곳을 짐작했다", "fire_look");
                var act = d["act"]?.Type == JTokenType.String ? (string)d["act"] : null;
                if (act != null)
                    Row("fire", "lookAct", d["verdict"]?.Type == JTokenType.String && (string)d["verdict"] == "good", "불이 난 곳과 연기에 맞는 방법을 골랐다", "그 순간의 불과 연기에 덜 맞는 방법을 골랐다",
                        act == "leave" ? "fire_other_smoke" : act == "stay" ? "fire_seal" : "fire_own_trapped");
                if (d["elevator"] != null && d["elevator"].Type == JTokenType.Integer && (int)d["elevator"] > 0)
                    Row("fire", "lookLift", false, null, "불이 났는데 엘리베이터로 가려 했다", "fire_lift");
            }
            var call = look?.Details?["report"] as JObject ?? ev.FirstOrDefault(e => e.Type == "fire_report")?.Details;
            if (call != null && True(call["played"]))
            {
                int score = call["score"] != null && call["score"].Type == JTokenType.Integer ? (int)call["score"] : 0;
                Row("fire", "report119", score >= LookReport.Full, "119에 장소·상황·사람을 빠짐없이 알리고 전화를 끊지 않았다", "119 신고에서 빠진 말이 있었다", "fire_report");
            }

            // competition upgrade: the 3D extinguisher (ExtinguisherScene "ext_use") and the 1장 pressure-gauge check
            var use = ev.LastOrDefault(e => e.Type == "ext_use" && e.Details != null);
            if (use != null)
            {
                var d = use.Details;
                var aimed = HeldExtinguisherLogic.BaseOk(d);
                if (aimed != null) Row("fire", "extBase", aimed.Value, "분말이 불을 덮도록 불이 난 곳을 겨눴다", "분말이 불길 위나 엉뚱한 곳으로 흩어졌다", "ext_cover");
                var behind = HeldExtinguisherLogic.BehindOk(d);
                if (behind != null) Row("fire", "extExit", behind.Value, "출구를 등지고 소화기를 썼다", "출구를 등지지 않고 소화기를 썼다", "ext_door");
                var back = HeldExtinguisherLogic.RetreatOk(d);
                if (back != null) Row("fire", "extRetreat", back.Value, "끄기 어려운 불 앞에서 멈추고 물러나 알렸다", "끄기 어려운 불 앞에서 끄기를 계속했다", "fire_big");
            }
            var gauge = First(ExtinguisherCheck.Event);
            if (gauge?.Details != null)
                Row("prep", "extGauge", gauge.Details["action"]?.Type == JTokenType.String && (string)gauge.Details["action"] == "report",
                    "소화기 압력계를 보고 관리사무소에 알렸다", "압력이 빠진 소화기를 그대로 두었다", "ext_gauge");
            else if (use != null && True(use.Details["low"]))
                Row("prep", "extGauge", false, null, "소화기 압력계를 미리 보지 않아 압력이 빠진 줄 몰랐다", "ext_gauge");

            // ---- 정보 판단 ----
            if (Has("rumor_checked")) Row("info", "rumorChecked", true, "소문을 공식 안내로 확인했다", null, "info_rumor");
            if (Any("rumor_believed", d => False(d["truth"]))) Row("info", "rumorBelieved", false, null, "확인하지 않은 소문을 사실로 받아들였다", "info_rumor");
            if (Has("rumor_followed")) Row("info", "rumorFollowed", false, null, "소문만 듣고 권고되지 않은 길로 갔다", "evac_note");
            if (Has("rumor_spread")) Row("info", "rumorSpread", false, null, "확인하지 않은 말을 이웃에게 옮겼다", "shelter_rumor");
            if (Has("rumor_corrected")) Row("info", "rumorCorrected", true, "잘못 알고 있던 내용을 공식 안내로 바로잡았다", null, "radio_listen");
            if (Has("note_left")) Row("info", "note", true, "떠나면서 행선지를 쪽지로 남겼다", null, "evac_note");

            // ---- 「산에서 내려온 사람들」 (3장 하위 장면, MountainScene): the report card, the intercom, the object ----
            var sight = ev.LastOrDefault(e => e.Type == "sighting");
            var sr = sight != null ? SightResult.Read(sight.Details) : null;
            if (sr != null && sr.Played)
            {
                Row("info", "sightSafe", sr.HowOk, "흔적을 확인하러 가지 않고 안전한 집에서 신고했다", "흔적을 확인하러 다시 가려 했다", "defense_report");
                Row("info", "sightReport", sr.Accurate, $"본 것만 빠짐없이 신고했다 (정확도 {sr.Score}/{sr.Full})",
                    sr.Guesses > 0 ? $"신고 카드에 보지 않은 것을 짐작으로 적었다 (정확도 {sr.Score}/{sr.Full})" : $"신고 카드에 빈 곳이 많았다 (정확도 {sr.Score}/{sr.Full})", "report_5w");
            }
            var obj = First("unknown_object");
            if (obj?.Details != null)
            {
                bool hands = MountainLogic.ObjectFirstOk(JStr(obj.Details["first"])), told = MountainLogic.ObjectReportOk(JStr(obj.Details["reported"]));
                Row("info", "objectHandsOff", hands && told, "낙하물을 만지지 않고 1338·경찰서에 위치를 알렸다",
                    !hands ? "모르는 낙하물에 다가가려 했다" : "낙하물을 1338이나 경찰서에 직접 알리지 않았다", hands ? "report_object" : "object_no_touch");
            }

            // ---- 응급 처치 (대피소 심폐소생술; CprLogic.Evaluate on the "cpr" event's summary) ----
            var cpr = First("cpr");
            if (cpr?.Details != null)
                foreach (var row in CprLogic.Evaluate(CprResult.Read(cpr.Details)).Rows)
                    r.Rows.Add(new ReportRow { Axis = "care", Id = row.Id, Ok = row.Ok, Label = row.Label, FactId = row.Fact });

            // ---- 버티기 ----
            if (Has("ate_spoiled")) Row("endure", "spoiled", false, null, "정전 뒤 상했을 수 있는 음식을 먹었다", "food_4h");
            if (Has("tossed_food")) Row("endure", "tossed", true, "의심스러운 음식은 먹지 않고 버렸다", null, "food_doubt");
            if (Has("water_fetched")) Row("endure", "water", true, "비상급수시설에서 물을 받아 왔다", null, "water_point");
            var door = First("intercom");
            if (door?.Details != null)
                Row("endure", "doorTalk", !True(door.Details["opened"]), "낯선 방문자에게 문을 열지 않고 인터폰으로 먼저 확인했다", "누구인지 확인하기 전에 문을 열었다", "door_talk");

            foreach (var (id, label) in AxisDefs)
            {
                var rows = r.Rows.Where(x => x.Axis == id).ToList();
                if (rows.Count == 0) continue;
                r.Axes.Add(new ReportAxis { Id = id, Label = label, Ok = rows.Count(x => x.Ok), Total = rows.Count });
            }
            r.Total = r.Rows.Count; r.Ok = r.Rows.Count(x => x.Ok);
            if (g.Stats != null) { r.Health = g.Stats.Health; r.Thirst = g.Stats.Thirst; r.Fatigue = g.Stats.Fatigue; r.Tension = g.Stats.Tension; }
            return r;
        }
    }

    /// <summary>One line of a guide card: a FACTS sentence, how to read it, and who published it.</summary>
    public sealed class GuideLine
    {
        public string FactId, Text, Source;
        /// <summary>"do" (safe action), "warn" (take care) or "never" (do not).</summary>
        public string Kind;
    }

    public sealed class GuideCard
    {
        public string Id, Title, Tag, Icon;
        /// <summary>"alert" (red), "warn" (amber), "safe" (emerald) or "info".</summary>
        public string Tone;
        public List<GuideLine> Lines = new();
    }

    /// <summary>
    /// The 생존 가이드 cards: one per situation, at most three lines each, every line a FACTS sentence word for word
    /// (the project's rule: safety wording comes only from the checked facts, facts.json plus facts-ext.json). A card with no
    /// fixed lines is filled from the facts of its topic, and a card whose facts are missing says so rather than inventing.
    /// </summary>
    public static class GuideLogic
    {
        public const int MaxLines = 3;

        static readonly (string id, string title, string tag, string icon, string tone, (string fact, string kind)[] lines)[] Defs =
        {
            // The 2023 false-alarm lesson first: which alert has a siren (facts-ext alert_levels), then what to do.
            ("airraid", "공습경보가 울리면", "경보", "siren", "alert", new[] { ("alert_levels", "warn"), ("alert_airraid", "do"), ("alert_stairs", "never") }),
            // 화생방 had no checked source, so its slot is the bystander CPR the game teaches (facts-ext, 질병관리청).
            ("cpr", "누군가 쓰러지면", "심폐소생술", "heart", "safe", new[] { ("cpr_point", "do"), ("cpr_depth_rate", "do"), ("cpr_ratio", "do") }),
            ("outage", "정전·단수가 되면", "정전·단수", "power", "warn", new[] { ("home_tub", "do"), ("water_cut", "never"), ("power_lift", "warn") }),
            ("shelter", "대피소로 이동할 때", "대피", "shelter", "safe", new[] { ("shelter_near", "do"), ("bag_take", "do"), ("shelter_rules", "warn") }),
            // 소방청 "불나면 살펴서 대피": look first, check the door, then low and wet-towelled down the stairs (the
            // elevator warning stays on the airraid and outage cards).
            ("fire", "불이 나고 연기가 차면", "화재", "fire", "alert", new[] { ("fire_look", "do"), ("fire_door", "warn"), ("fire_big", "do") }),
            ("info", "소식을 확인하고 알릴 때", "정보·신고", "radio", "info", new[] { ("radio_listen", "do"), ("info_rumor", "warn"), ("report_object", "do") }),
        };

        public static List<GuideCard> Cards(Facts facts)
        {
            var list = new List<GuideCard>();
            facts ??= Facts.Empty;
            foreach (var d in Defs)
            {
                var card = new GuideCard { Id = d.id, Title = d.title, Tag = d.tag, Icon = d.icon, Tone = d.tone };
                var picks = d.lines.Length > 0
                    ? d.lines.Select(l => (facts.Fact(l.fact), l.kind))
                    : facts.All.Where(f => f.Topic == d.id).Select(f => (f, "do")); // a topic added to the facts later
                foreach (var (f, kind) in picks)
                {
                    if (f == null || card.Lines.Count >= MaxLines) continue;
                    card.Lines.Add(new GuideLine { FactId = f.Id, Text = f.Text, Kind = kind, Source = facts.Source(f.SourceId)?.Publisher });
                }
                list.Add(card);
            }
            return list;
        }
    }
}
