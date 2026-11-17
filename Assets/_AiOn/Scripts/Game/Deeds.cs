using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One row of the deed table (original DEEDS[id]).</summary>
    public sealed class DeedDef
    {
        [JsonProperty("id")] public string Id;
        /// <summary>1-based place in the table.</summary>
        [JsonProperty("no")] public int No;
        /// <summary>Chapter id the deed belongs to (a key of <see cref="Deeds.Ch"/>).</summary>
        [JsonProperty("ch")] public string Ch;
        /// <summary>'event' = read from an existing event; 'new' = written by <see cref="Deeds.Remember"/>.</summary>
        [JsonProperty("src")] public string Src;
        /// <summary>Axis -> weight (see <see cref="Deeds.AxisNames"/>).</summary>
        [JsonProperty("w")] public IReadOnlyDictionary<string, int> W;
        /// <summary>Who will remember it ("서연은"), or null.</summary>
        [JsonProperty("cue")] public string Cue;
        [JsonProperty("short")] public string Short;
        [JsonProperty("line")] public string Line;
        /// <summary>FACTS ids.</summary>
        [JsonProperty("facts")] public string[] Facts;
        /// <summary>Every matching event counts (violence), not only the first.</summary>
        [JsonProperty("each")] public bool Each;

        [JsonIgnore] internal Func<GameEvent, GameState, bool> Match;
    }

    /// <summary>A deed this run holds: the event that made it (the 4th for signals_clear) and how many events matched.</summary>
    public sealed class Deed
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("chapter", NullValueHandling = NullValueHandling.Ignore)] public string Chapter;
        [JsonProperty("day")] public int Day;
        [JsonProperty("minute")] public int Minute;
        [JsonProperty("n")] public int N;
        [JsonProperty("details")] public JObject Details;
        /// <summary>Violence only: how many of the hits were knock-outs.</summary>
        [JsonProperty("ko", NullValueHandling = NullValueHandling.Ignore)] public int? Ko;
    }

    /// <summary>A promise that was made: 'kept', 'open', or 'broken' once the run is over.</summary>
    public sealed class DeedPromise
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("who")] public string Who;
        [JsonProperty("state")] public string State;
    }

    public sealed class DeedRef
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("seq")] public int Seq;
    }

    public sealed class Neighbour
    {
        [JsonProperty("name")] public string Name;
    }

    /// <summary>
    /// The ledger of deeds (original game/deeds.js): what the player did for whom, derived from G.events. Existing
    /// events are matched (src 'event'); new choices write a 'deed' event through Remember() (src 'new'). Bonds,
    /// promises and the endings are pure functions of this ledger, so old saves and retries need no schema change.
    /// Names: DEEDS is <see cref="Table"/> / <see cref="Get"/>, deeds(G) is <see cref="Of"/>, AXES is
    /// <see cref="AxisNames"/>, NEW_DEEDS is <see cref="NewIds"/> (C# cannot give a member its class's name, or a
    /// field and a method the same one).
    /// </summary>
    public static class Deeds
    {
        public static readonly string[] AxisNames = { "seoyeon", "manager", "clerk", "elder", "town", "info", "logistics", "harm" };

        /// <summary>Chapter id -> the name the player sees.</summary>
        public static readonly IReadOnlyDictionary<string, string> Ch = new Dictionary<string, string>
        {
            ["prologue"] = "프롤로그", ["prep"] = "1장", ["dday"] = "2장", ["endure"] = "3장", ["crossroads"] = "4장", ["evac"] = "5장",
        };

        /// <summary>The four neighbours, in the order they are always listed.</summary>
        public static readonly string[] NeighbourIds = { "seoyeon", "manager", "clerk", "elder" };

        public static readonly IReadOnlyDictionary<string, Neighbour> Neighbours = new Dictionary<string, Neighbour>
        {
            ["seoyeon"] = new Neighbour { Name = "오서연" }, ["manager"] = new Neighbour { Name = "박영수 소장" },
            ["clerk"] = new Neighbour { Name = "이수아" }, ["elder"] = new Neighbour { Name = "304호 어르신" },
        };

        // ---------- matchers for existing events. Untagged help events fall back to their old `what` text. ----------

        static readonly JObject NoDetails = new();
        internal static JObject Det(GameEvent e) => e.Details ?? NoDetails;

        static Func<GameEvent, GameState, bool> Ev(string type, Func<JObject, bool> pred = null) =>
            (e, g) => e.Type == type && (pred == null || pred(Det(e)));

        static Func<GameEvent, GameState, bool> Help(string kind, Func<string, bool> fb) =>
            Ev("help", d => JsSem.Is(d["who"], "seoyeon") && (JsSem.Truthy(d["kind"]) ? JsSem.Is(d["kind"], kind) : fb(JsSem.Str(d["what"]) ?? "")));

        static Func<GameEvent, GameState, bool> Fields(string f) =>
            Ev("docs_done", d => d["fields"] is JArray a && a.Any(t => JsSem.Is(t, f)));

        /// <summary>recordEncounter takes a string or an eval object.</summary>
        static string Res(JToken r) => r is JObject o ? JsSem.Str(o["result"]) : JsSem.Str(r);

        static bool Ext(GameEvent e) => e.Type == "encounter" && JsSem.Is(Det(e)["tool"], "extinguisher");

        /// <summary>game/rumors.js ids; old saves carry none.</summary>
        static bool ShelterRumor(JObject d) => JsSem.Nullish(d["id"]) || JsSem.Is(d["id"], "shelter_closed");

        static JToken StayMove(GameState g)
        {
            var last = g.Events.LastOrDefault(e => e.Type == "decision");
            var v = last?.Details?["stayMove"];
            if (!JsSem.Nullish(v)) return v;
            return g.Know != null && g.Know.TryGetValue("stayMove", out var k) ? k : null;
        }

        static (string, int)[] W(params (string, int)[] w) => w;
        static string[] F(params string[] f) => f;

        static DeedDef Row(string id, string ch, (string axis, int v)[] w, string cue, string[] facts, string @short, string line,
            Func<GameEvent, GameState, bool> match = null, bool each = false)
        {
            var weights = new Dictionary<string, int>();
            foreach (var (axis, v) in w) weights[axis] = v;
            return new DeedDef
            {
                Id = id, Ch = ch, W = weights, Cue = cue, Facts = facts, Short = @short, Line = line, Each = each,
                Src = match == null ? "new" : "event",
                // no match = src 'new' (written by Remember())
                Match = match ?? ((e, g) => e.Type == "deed" && JsSem.Is(Det(e)["id"], id)),
            };
        }

        // id, chapter, weights, cue, facts, short, line, match?
        static readonly DeedDef[] Rows =
        {
            Row("signals_clear", "prologue", W(("info", 2)), null, F("info_rumor"), "신호를 사실과 소문으로 나눴다", "쏟아지는 신호를 확인된 사실과 소문으로 나눠 보았다.", Ev("signal_sorted", d => JsSem.IsTrue(d["ok"]))),
            Row("lead_honest", "prologue", W(("info", 1), ("logistics", 1)), "윤 팀장은", F("info_scope"), "확인된 것만 안내하겠다고 했다", "윤 팀장에게 확인된 것만 안내하겠다고 말했다.", Ev("talk_lead", d => JsSem.Is(d["pick"], "ok"))),
            Row("driver_text", "prologue", W(("logistics", 2)), "김 기사님은", F(), "김 기사님께 안부 문자를 보냈다", "출차하지 못한 김 기사님께 무슨 일 있으면 연락 달라고 문자를 보냈다."),
            Row("seoyeon_contact", "prep", W(("seoyeon", 1)), null, F("people_card"), "서연과 연락 방법을 정했다", "303호 서연과 비상시 연락 방법을 정했다.", Ev("neighbors_done", d => JsSem.IsTrue(d["seoyeon"]))),
            Row("promise_note", "prep", W(("seoyeon", 1)), "서연은", F("comms_text"), "문자와 쪽지로 알리기로 했다", "무슨 일이 생기면 문자와 문 앞 쪽지로 알려 주겠다고 서연과 약속했다."),
            Row("took_batteries", "prep", W(("seoyeon", 1)), null, F(), "서연이 준 건전지를 받았다", "서연이 남는다며 건넨 건전지를 고맙게 받았다.", Ev("gift", d => JsSem.Is(d["from"], "seoyeon"))),
            Row("manager_contact", "prep", W(("manager", 1)), null, F(), "박 소장과 연락 방법을 정했다", "관리사무소 박 소장과 비상시 연락 방법을 정했다.", Ev("neighbors_done", d => JsSem.IsTrue(d["manager"]))),
            Row("clerk_contact", "prep", W(("clerk", 1)), null, F(), "수아와 연락 방법을 정했다", "바로편의점 수아와 비상시 연락 방법을 정했다.", Ev("neighbors_done", d => JsSem.IsTrue(d["clerk"]))),
            Row("promise_store", "prep", W(("clerk", 1)), "수아는", F(), "가게 셔터를 봐 주겠다고 했다", "본가에 가는 수아에게, 가게를 비우면 셔터라도 봐 주겠다고 했다."),
            Row("elder_met", "prep", W(("elder", 2)), "304호 어르신은", F(), "304호 어르신께 인사드렸다", "304호 어르신 댁에 들러 처음으로 인사를 드렸다."),
            Row("card_neighbors", "prep", W(("seoyeon", 1), ("manager", 1)), null, F("contact_card"), "카드에 이웃 연락처를 적었다", "비상연락카드에 303호와 관리사무소 연락처를 적었다.", Fields("neighbors")),
            Row("card_meet", "prep", W(("info", 1)), null, F("contact_meet"), "만남의 장소를 정해 적었다", "비상연락카드에 만남의 장소 세 곳을 적었다.", Fields("meet")),
            Row("card_family", "prep", W(), null, F("contact_card"), "부모님 연락처를 카드에 적었다", "대구 부모님 연락처를 종이 카드에 적어 두었다.", Fields("family")),
            Row("mart_share", "prep", W(("town", 2)), "그분은", F("people_order"), "할머니께 생수를 나눠 드렸다", "마트 계산대 뒤에 선 할머니께 생수 한 병을 나눠 드렸다."),
            Row("hoard", "prep", W(("town", -2), ("harm", 1)), null, F("home_nohoard"), "물건을 한꺼번에 많이 샀다", "불안한 마음에 같은 물건을 한꺼번에 많이 샀다.", Ev("hoard")),
            Row("blackout_radio", "dday", W(("info", 1)), null, F("radio_listen"), "정전 뒤 라디오부터 켰다", "정전이 되자 먼저 라디오를 켜 안내를 들었다."),
            Row("hall_checked", "dday", W(("elder", 1)), null, F(), "정전 뒤 복도를 살폈다", "정전된 저녁, 손전등을 들고 3층 복도를 살폈다."),
            Row("elder_candle_tip", "dday", W(("elder", 1)), "304호 어르신은", F("candle_rules"), "어르신께 촛불을 꼭 끄시라 했다", "304호 어르신께 자리를 비우실 땐 촛불을 꼭 꺼 달라고 말씀드렸다."),
            Row("candle_left", "dday", W(), null, F("candle_rules"), "촛불을 켜 둔 채 집을 나섰다", "경보가 울리던 밤, 촛불을 켜 둔 채 집을 나섰다.", Ev("candle_left")),
            Row("elder_alert_knock", "dday", W(("elder", 2), ("town", 1)), "304호 어르신은", F("people_ask", "alert_stairs"), "경보 때 함께 계단을 내려갔다", "공습경보가 울리던 캄캄한 계단에서 304호 어르신을 부축해 함께 내려갔다.",
                (e, g) => (e.Type == "elder_choice" && JsSem.Is(Det(e)["pick"], "help")) || (e.Type == "help" && JsSem.Is(Det(e)["who"], "elder"))),
            Row("elder_alert_told", "dday", W(("elder", 1), ("manager", 1)), "박 소장은", F("alert_stairs"), "어르신 일을 박 소장에게 알렸다", "경보 속 계단 앞에 계신 304호 어르신을 박 소장에게 알려 모시고 내려오게 했다.",
                Ev("help", d => JsSem.Is(d["who"], "manager") && (JsSem.Str(d["what"]) ?? "").Contains("304호"))),
            Row("manager_detour", "dday", W(("manager", 1), ("info", 1)), "박 소장은", F(), "관리사무소에 들러 연락을 정했다", "대피하는 길에 관리사무소 창구에 들러 연락 방법을 정했다."),
            Row("handover_precise", "dday", W(("seoyeon", 2), ("info", 2)), null, F("info_scope", "comms_text"), "서연에게 본 것만 정확히 전했다", "서연에게 내가 본 시각과 자리만 정확히 적어 보냈다.", Ev("handover_written", d => JsSem.Is(d["kind"], "precise"))),
            Row("handover_partial", "dday", W(("seoyeon", 1), ("info", 1)), null, F("info_scope"), "서연에게 본 것을 일부 전했다", "서연에게 내가 본 것 가운데 일부를 적어 보냈다.", Ev("handover_written", d => JsSem.Is(d["kind"], "partial"))),
            Row("handover_overgeneral", "dday", W(("info", -1)), null, F("info_scope"), "본 것보다 넓게 말해 전했다", "서연에게 내가 본 것보다 넓은 말로 적어 보냈다.", Ev("handover_written", d => JsSem.Is(d["kind"], "generalized"))),
            Row("shelter_water_given", "dday", W(("town", 2)), "그 가족은", F("people_order"), "옆자리 아이에게 물을 나눴다", "대피소 옆자리 아이에게 가방 속 물 한 병을 나눠 주었다."),
            Row("shelter_water_received", "dday", W(), null, F(), "대피소에서 물을 나눠 받았다", "빈손으로 온 대피소에서 옆 사람에게 물을 나눠 받았다."),
            Row("register_helped", "dday", W(("town", 1)), "그분은", F("people_order"), "명부 대신 적기를 먼저 여쭸다", "안경을 두고 온 할아버지께 먼저 여쭌 뒤 명부를 대신 적어 드렸다."),
            Row("stairs_asked", "dday", W(("seoyeon", 3)), "서연은", F("people_ask"), "계단에서 먼저 물었다", "대피소 계단에서 어떻게 도울지 서연에게 먼저 묻고 보행기를 들었다.", Help("ask", w => w.Contains("먼저 묻고"))),
            Row("stairs_carried", "dday", W(("seoyeon", 1)), null, F(), "계단에서 보행기를 들어 주었다", "대피소 계단에서 서연의 보행기를 옆에서 들어 주었다.", Help("carry", w => w.StartsWith("계단에서 보행기", StringComparison.Ordinal))),
            Row("looter_gave", "endure", W(("town", 2)), null, F("people_order"), "낯선 사람에게 먹을 것을 내줬다", "먹을 것을 달라는 낯선 사람에게 가진 것을 내주었다.", Ev("looter", d => JsSem.Is(d["result"], "gave"))),
            Row("looter_escaped", "endure", W(), null, F(), "시비를 피해 자리를 떴다", "시비가 붙자 맞서지 않고 자리를 피했다.", Ev("looter", d => JsSem.Is(d["result"], "escaped"))),
            Row("looter_fought", "endure", W(), null, F(), "덤벼든 사람에게 맞섰다", "덤벼드는 사람에게서 몸을 지키려 맞섰다.", Ev("looter", d => JsSem.Is(d["result"], "fought"))),
            Row("violence", "dday", W(("harm", 1)), null, F("people_order"), "먼저 사람에게 손을 댔다", "시비도 없던 사람에게 먼저 손을 댔다.", Ev("violence"), each: true),
            Row("water_for_seoyeon", "endure", W(("seoyeon", 3)), "서연은", F("water_point"), "서연 몫의 물을 날라 주었다", "급수소에서 물 한 통을 받아 3층 서연의 집까지 날라 주었다.", Ev("water_fetched", d => JsSem.IsTrue(d["forSeoyeon"]))),
            Row("water_declined", "endure", W(), null, F(), "오늘은 내 물만 들 수 있다고 했다", "내 몫만 들기도 벅차 서연의 물은 함께 받지 못했다."),
            Row("board_offer", "endure", W(("town", 2), ("manager", 1), ("logistics", 1)), "이웃들은", F("shelter_share"), "게시판에 급수 심부름을 적었다", "단지 게시판에 급수소에 다녀온다고, 필요한 분은 쪽지를 달라고 적었다."),
            Row("queue_carry_elder", "endure", W(("elder", 2), ("town", 1)), "304호 어르신은", F("people_order", "water_point"), "어르신께 여쭙고 물통을 들었다", "급수 줄에서 304호 어르신께 먼저 여쭌 뒤 물통을 들어 드렸다."),
            Row("store_paid", "endure", W(("clerk", 2)), "수아는", F("bag_cash"), "편의점 현금 통에 값을 넣었다", "불 꺼진 편의점에서 물건을 챙기고 현금 통에 값을 넣었다.", Ev("buy", d => JsSem.Is(d["paid"], "cash"))),
            Row("store_iou", "endure", W(("clerk", 2)), "수아는", F(), "편의점에 금액 쪽지를 남겼다", "현금이 없어 건전지 한 팩만 챙기고 쪽지에 302호와 금액을 적었다."),
            Row("store_took", "endure", W(("clerk", -2), ("harm", 1)), "수아는", F("people_order"), "값을 남기지 않고 물건을 챙겼다", "현금이 없어 편의점 건전지와 물을 값을 남기지 않고 가져왔다."),
            Row("store_shuttered", "endure", W(("clerk", 2)), "수아는", F(), "편의점 셔터를 내려 두었다", "반쯤 열린 편의점 셔터를 내리고 302호라고 쪽지를 남겼다."),
            Row("rumor_checked", "endure", W(("info", 2), ("town", 1)), null, F("info_rumor", "shelter_rumor"), "대피소 폐쇄 소문을 확인했다", "대피소가 폐쇄됐다는 소문을 듣고 먼저 공식 안내로 확인했다.", Ev("rumor_checked", ShelterRumor)),
            Row("rumor_spread", "endure", W(("info", -2), ("town", -1), ("harm", 1)), null, F("info_harm"), "대피소 폐쇄 소문을 전했다", "대피소가 폐쇄됐다는 확인되지 않은 말을 이웃에게 전했다.", Ev("rumor_spread", ShelterRumor)),
            Row("rumor_corrected", "endure", W(("info", 1), ("town", 1)), "그 주민은", F("info_harm"), "102동 주민에게 확인한 걸 알렸다", "102동 주민에게 대피소가 정상 운영 중이라고 확인한 내용을 알렸다."),
            Row("knock_helped", "endure", W(("town", 1)), "그 이웃은", F("shelter_share"), "밤에 찾아온 이웃을 도왔다", "밤늦게 문을 두드린 이웃에게 물이나 급수소 소식을 건넸다.",
                (e, g) => (e.Type == "knock" && (JsSem.Is(Det(e)["pick"], "give") || JsSem.Is(Det(e)["pick"], "info"))) || (e.Type == "help" && JsSem.Is(Det(e)["who"], "stranger"))),
            Row("fire_warned", "endure", W(("elder", 2), ("town", 1)), "304호 어르신은", F("fire_shout"), "\"불이야!\" 외쳐 이웃을 깨웠다", "복도에 불이 나자 \"불이야!\" 외쳐 이웃들을 깨웠다.", Ev("fire_shout")),
            Row("fire_handled", "endure", W(("manager", 1), ("town", 1)), null, F("fire_small", "fire_big"), "복도 불 앞에서 판단해 움직였다", "복도 불 앞에서 끌지 물러날지 판단해 움직였다.",
                (e, g) => e.Type == "fire_retreat" || (Ext(e) && e.Chapter == "endure" && (Res(Det(e)["result"]) == "out" || Res(Det(e)["result"]) == "retreat-ok"))),
            Row("elder_visit", "endure", W(("elder", 1)), "304호 어르신은", F(), "304호 어르신께 안부를 여쭸다", "불이 난 다음 날, 304호에 들러 어르신께 안부를 여쭸다."),
            Row("notes_exchange", "endure", W(("seoyeon", 2), ("info", 1)), null, F("shelter_share"), "서연과 라디오 메모를 나눴다", "서연과 라디오 메모와 급수소 소식을 서로 나누기로 했다.", Help("memo", w => w.StartsWith("라디오 메모", StringComparison.Ordinal))),
            Row("seoyeon_note_reply", "crossroads", W(("seoyeon", 1), ("info", 1)), "서연은", F("info_scope"), "서연 메모에 급수 시간을 적었다", "서연의 메모에 급수 시간과 그 말을 들은 곳을 덧붙여 적었다."),
            Row("yoon_reply", "crossroads", W(("logistics", 2)), "윤 팀장은", F(), "윤 팀장에게 돕겠다고 답했다", "구호물자 분류를 도와 달라는 윤 팀장의 문자에 가능한 날 나가겠다고 답했다."),
            Row("smoke_judged", "crossroads", W(("info", 1), ("manager", 1)), null, F("fire_stay", "fire_judge"), "연기 속에서 집 안에 머물렀다", "윗집 연기가 퍼질 때 문을 닫고 집 안에서 기다렸다.", Ev("smoke_choice", d => JsSem.Is(d["pick"], "stay"))),
            Row("shelter_fire_judged", "crossroads", W(("town", 1)), null, F("fire_big"), "대피소 불 앞에서 판단했다", "대피소 주차장의 큰 불 앞에서 무리하지 않고 판단했다.",
                (e, g) => Ext(e) && e.Chapter == "crossroads" && Res(Det(e)["result"]) != "escalated" && JsSem.Is(StayMove(g), "move")),
            // §2 rule "move = shelter_fire_judged +1 and fire_report +1" (equal to smoke_judged +2) wins over the table's info+1, town+1.
            Row("fire_report", "crossroads", W(("info", 1)), null, F("info_scope", "shelter_rules"), "본 대로 시각과 자리를 알렸다", "불이 난 시각과 자리, 다친 사람이 있었는지를 본 대로 알렸다."),
            Row("rounds_with_manager", "crossroads", W(("manager", 3), ("town", 1)), "박 소장은", F(), "박 소장과 빈 집 문단속을 돌았다", "박 소장과 함께 빈 집들의 문단속을 돌았다."),
            Row("sort_supplies", "crossroads", W(("logistics", 3), ("town", 1)), "이웃들은", F(), "대피소 구호품 정리를 도왔다", "물류 일 하던 솜씨로 대피소 구호품 상자 정리를 도왔다."),
            Row("door_note", "evac", W(("town", 1), ("seoyeon", 1), ("manager", 1)), null, F("evac_note"), "떠나며 문에 쪽지를 남겼다", "집을 나서며 언제 어디로 가는지 문에 쪽지를 남겼다.", Ev("note_left")),
            Row("seoyeon_card_photo", "evac", W(("seoyeon", 1)), null, F("contact_card"), "서연과 연락처를 다시 확인했다", "떠나기 전 서연과 서로의 연락처를 다시 확인했다.", Help("card", w => w.StartsWith("떠나기 전", StringComparison.Ordinal))),
            Row("seoyeon_waited", "evac", W(("seoyeon", 3)), null, F("people_unhcr"), "서연의 지원 차량을 함께 기다렸다", "지원 차량이 올 때까지 서연 곁을 지켰다.", Ev("path", d => JsSem.Is(d["path"], "seoyeon"))),
            Row("stock_to_manager", "evac", W(("manager", 2), ("town", 2), ("logistics", 1)), "박 소장은", F("people_order"), "남은 물과 통조림을 맡겼다", "남는 물과 통조림을 관리사무소에 맡기고 나섰다."),
            Row("bus_helped", "evac", W(("town", 1)), "그분은", F("people_order"), "리프트 앞 어르신 짐을 들어 드렸다", "버스 리프트 앞에서 어르신께 먼저 여쭌 뒤 짐을 들어 드렸다."),
            Row("volunteered", "evac", W(("town", 2)), null, F("water_point"), "급수 자원봉사에 이름을 적었다", "버스 대신 행정복지센터 급수 자원봉사에 이름을 적었다.", Ev("path", d => JsSem.Is(d["path"], "volunteer"))),
            Row("stayed", "evac", W(("manager", 1)), null, F(), "박 소장과 단지에 남았다", "버스 대신 박 소장과 함께 단지에 남기로 했다.", Ev("path", d => JsSem.Is(d["path"], "stay"))),
        };

        static readonly Dictionary<string, DeedDef> ById = Index();

        static Dictionary<string, DeedDef> Index()
        {
            var by = new Dictionary<string, DeedDef>();
            for (int i = 0; i < Rows.Length; i++) { Rows[i].No = i + 1; by.Add(Rows[i].Id, Rows[i]); }
            return by;
        }

        /// <summary>Every deed, in table order (the original DEEDS).</summary>
        public static IReadOnlyList<DeedDef> Table => Rows;

        /// <summary>The deed's row (DEEDS[id]), or null.</summary>
        public static DeedDef Get(string id) => id != null && ById.TryGetValue(id, out var d) ? d : null;

        /// <summary>Ids of the new-choice deeds (the original NEW_DEEDS): the story writes each with Remember().</summary>
        public static readonly IReadOnlyList<string> NewIds = Rows.Where(d => d.Src == "new").Select(d => d.Id).ToArray();

        /// <summary>The 4th correctly sorted signal is the deed.</summary>
        static readonly Dictionary<string, int> Min = new() { ["signals_clear"] = 4 };

        /// <summary>Writes a new-choice deed once. Unknown ids and event-derived ids are developer errors.</summary>
        public static GameEvent Remember(GameState g, string id, JObject details = null)
        {
            var d = Get(id) ?? throw new ArgumentException($"unknown deed: {id}");
            if (d.Src != "new") throw new InvalidOperationException($"deed {id} is derived from events, not remembered");
            if (!d.Each && g.Events.Any(e => d.Match(e, g))) return null;
            var o = details != null ? (JObject)details.DeepClone() : new JObject();
            o["id"] = id;
            return GameRules.RecordEvent(g, "deed", o);
        }

        /// <summary>The run's deeds (the original deeds(G)): unique by id, in the order they first happened.</summary>
        public static List<Deed> Of(GameState g)
        {
            var found = new List<Deed>();
            foreach (var def in Rows)
            {
                var ms = g.Events.Where(e => def.Match(e, g)).ToList();
                int k = Min.TryGetValue(def.Id, out var min) ? min : 1;
                if (ms.Count < k) continue;
                var e = ms[k - 1];
                found.Add(new Deed
                {
                    Id = def.Id, Seq = e.Seq, Chapter = e.Chapter, Day = e.Day, Minute = e.Minute, N = ms.Count, Details = Det(e),
                    Ko = def.Id == "violence" ? ms.Count(x => JsSem.Truthy(Det(x)["ko"])) : (int?)null,
                });
            }
            return found.OrderBy(d => d.Seq).ToList(); // stable, like the original sort: deeds of one event keep table order
        }

        static readonly (string id, string who, string made, string[] kept)[] PromiseTable =
        {
            ("note_for_seoyeon", "seoyeon", "promise_note", new[] { "seoyeon_note_reply", "door_note" }),
            ("store_watch", "clerk", "promise_store", new[] { "store_shuttered" }),
            ("news_for_seoyeon", "seoyeon", "notes_exchange", new[] { "seoyeon_note_reply" }),
        };

        /// <summary>Only promises that were made. Open ones count as broken once the run is over (final).</summary>
        public static List<DeedPromise> Promises(GameState g, bool final = false, IEnumerable<Deed> ds = null)
        {
            var has = new HashSet<string>((ds ?? Of(g)).Select(d => d.Id));
            return PromiseTable.Where(p => has.Contains(p.made))
                .Select(p => new DeedPromise { Id = p.id, Who = p.who, State = p.kept.Any(has.Contains) ? "kept" : final ? "broken" : "open" })
                .ToList();
        }

        /// <summary>The bonds and leanings of the run, one number per <see cref="AxisNames"/> entry.</summary>
        public static Dictionary<string, int> Axes(GameState g, bool final = false)
        {
            var a = new Dictionary<string, int>();
            foreach (var k in AxisNames) a[k] = 0;
            var ds = Of(g);
            foreach (var d in ds)
            {
                if (d.Id == "violence") { a["harm"] += d.N + 2 * (d.Ko ?? 0); continue; } // +1 a hit, +3 a knock-out
                foreach (var (k, v) in ById[d.Id].W) a[k] += v;
            }
            foreach (var p in Promises(g, final, ds)) if (p.State == "broken") a[p.Who] -= 1;
            return a;
        }

        /// <summary>Deeds made by events after <paramref name="afterSeq"/> (for the "will remember this" cue).</summary>
        public static List<DeedRef> NewDeeds(GameState g, int afterSeq) =>
            Of(g).Where(d => d.Seq > afterSeq).Select(d => new DeedRef { Id = d.Id, Seq = d.Seq }).ToList();

        public static string CueText(string id) => Get(id)?.Cue is { Length: > 0 } cue ? $"{cue} 이 일을 기억할 것이다." : null;
    }

    /// <summary>
    /// JavaScript value rules for event details and stored JSON (===, truthiness, Number.isInteger, String(v)),
    /// so the ports of deeds / endings / challenges / lounge / debrief read loose data exactly like the original.
    /// </summary>
    internal static class JsSem
    {
        /// <summary>undefined or null (a JValue made from a null string has type String and no value: null too).</summary>
        public static bool Nullish(JToken t) =>
            t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined || (t is JValue v && v.Value == null);

        public static bool Truthy(JToken t)
        {
            if (Nullish(t)) return false;
            switch (t.Type)
            {
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer: return (double)t != 0;
                case JTokenType.Float: { double v = (double)t; return v != 0 && !double.IsNaN(v); }
                case JTokenType.String: return ((string)t).Length > 0;
                default: return true;
            }
        }

        /// <summary>The string, or null when the value is not a string.</summary>
        public static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        /// <summary>t === s</summary>
        public static bool Is(JToken t, string s) => s != null && Str(t) == s;

        /// <summary>t === true</summary>
        public static bool IsTrue(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;

        public static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        public static double Number(JToken t) => (double)t;

        /// <summary>Number.isInteger(t)</summary>
        public static bool IsInt(JToken t)
        {
            if (!IsNumber(t)) return false;
            if (t.Type == JTokenType.Integer) return true;
            double v = (double)t;
            return !double.IsNaN(v) && !double.IsInfinity(v) && Math.Floor(v) == v;
        }

        /// <summary>String(number): whole numbers without a decimal point.</summary>
        public static string NumText(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
            if (Math.Floor(x) == x && Math.Abs(x) < 1e15) return ((long)x).ToString(CultureInfo.InvariantCulture);
            return x.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>String(v) as a template literal prints it (a missing value prints "undefined").</summary>
        public static string Text(JToken t)
        {
            if (t == null || t.Type == JTokenType.Undefined) return "undefined";
            if (Nullish(t)) return "null";
            switch (t.Type)
            {
                case JTokenType.String: return (string)t;
                case JTokenType.Boolean: return (bool)t ? "true" : "false";
                case JTokenType.Integer:
                case JTokenType.Float: return NumText((double)t);
                case JTokenType.Array: return string.Join(",", t.Select(x => Nullish(x) ? "" : Text(x)));
                case JTokenType.Object: return "[object Object]";
                default: return t.ToString();
            }
        }

        /// <summary>JSON.parse(text), or null when it is not JSON. Strings stay strings (no date parsing).</summary>
        public static JToken Parse(string text)
        {
            if (text == null) return null;
            try
            {
                using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
                var token = JToken.ReadFrom(reader);
                while (reader.Read()) if (reader.TokenType != JsonToken.Comment) return null; // trailing content
                return token;
            }
            catch (JsonException) { return null; }
        }
    }
}
