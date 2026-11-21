using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One of the 11 endings (original ENDINGS entry).</summary>
    public sealed class EndingDef
    {
        [JsonProperty("no")] public int No;
        [JsonProperty("id")] public string Id;
        /// <summary>leave | base | world | shadow</summary>
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("title")] public string Title;
        [JsonProperty("place")] public string Place;
        /// <summary>World endings: every axis must reach its threshold.</summary>
        [JsonIgnore] public (string Axis, int Threshold)[] Need = Array.Empty<(string, int)>();
        /// <summary>Leave / base endings: the axes the trace scores deeds on.</summary>
        [JsonProperty("axes")] public string[] Axes = Array.Empty<string>();
        /// <summary>What to do to see it (shown in the gallery while it is unseen).</summary>
        [JsonProperty("hint")] public string Hint;
        /// <summary>FACTS ids the ending cites.</summary>
        [JsonProperty("facts")] public string[] Facts = Array.Empty<string>();

        /// <summary>(G, axes, has) -> the ending's own lines; null entries are left out.</summary>
        [JsonIgnore] internal Func<GameState, IReadOnlyDictionary<string, int>, Func<string, bool>, string[]> Lines;
        /// <summary>
        /// Port endings only (competition upgrade): the ending is a candidate (chosen or shown as a near miss) only for a
        /// run that holds one of these deeds, i.e. one that played the moments it is about. Runs of the original game hold
        /// none, so they reach and miss exactly what the original's did (갈라짐 기록).
        /// </summary>
        [JsonIgnore] public string[] Gate;
    }

    /// <summary>One step that led to the ending: the last-day path (id 'path') or a deed.</summary>
    public sealed class TraceItem
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text")] public string Text;
        /// <summary>"3장 · D+2 14:05"</summary>
        [JsonProperty("when")] public string When;
        /// <summary>The source of the one fact shown with the deed (none for the path).</summary>
        [JsonProperty("sourceIds")] public List<string> SourceIds = new();
    }

    /// <summary>A world ending this run did not reach, with the first undone deed on its weakest axis.</summary>
    public sealed class NearMiss
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("hint")] public string Hint;
        /// <summary>"3장 — 편의점 셔터를 내려 두었다", or "".</summary>
        [JsonProperty("tip")] public string Tip;
    }

    /// <summary>The ending a run reached and everything the ending screen says (original pickEnding result).</summary>
    public sealed class Fate
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("no")] public int No;
        [JsonProperty("title")] public string Title;
        [JsonProperty("place")] public string Place;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("path")] public string Path;
        [JsonProperty("lines")] public List<string> Lines;
        [JsonProperty("people")] public List<PersonLine> People;
        [JsonProperty("town")] public List<string> Town;
        [JsonProperty("trace")] public List<TraceItem> Trace;
        [JsonProperty("missed")] public List<NearMiss> Missed;
        [JsonProperty("sourceIds")] public List<string> SourceIds;
    }

    /// <summary>One seen ending in the stored gallery.</summary>
    public sealed class GallerySeen
    {
        /// <summary>The day it was first seen, yyyy-MM-dd.</summary>
        [JsonProperty("at")] public string At;
        [JsonProperty("runs")] public long Runs = 1;
        /// <summary>Up to five deed ids of the latest run's trace.</summary>
        [JsonProperty("trace")] public List<string> Trace = new();
    }

    /// <summary>The stored ending gallery: { seen: { [endingId]: { at, runs, trace } } }.</summary>
    public sealed class Gallery
    {
        [JsonProperty("seen")] public Dictionary<string, GallerySeen> Seen = new();
    }

    /// <summary>One card of the gallery screen; an unseen ending shows only its number and hint.</summary>
    public sealed class GalleryCard
    {
        [JsonProperty("no")] public int No;
        [JsonProperty("id")] public string Id;
        [JsonProperty("seen")] public bool Seen;
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)] public string Title;
        [JsonProperty("place", NullValueHandling = NullValueHandling.Ignore)] public string Place;
        [JsonProperty("at", NullValueHandling = NullValueHandling.Ignore)] public string At;
        [JsonProperty("shorts")] public List<string> Shorts = new();
        [JsonProperty("hint")] public string Hint;
    }

    /// <summary>
    /// Which of the 11 endings a run reaches, and why (original game/endings.js). The last-day path (G.know.path, set
    /// by the evacuation chapter) says how you left; on in-town paths the deed ledger can open a world ending.
    /// Deterministic, no G.karma. The day ledger adds how the rationed days went; stock is judged in litres, never
    /// in bottles. ENDINGS is <see cref="All"/> (a member cannot have its class's name).
    /// factSource: fact id -> its source id, or null (<c>facts.SourceId</c>).
    /// </summary>
    public static class Endings
    {
        /// <summary>
        /// An old save's leg injury (new runs no longer break a leg). Port change (competition upgrade, no injury scenes):
        /// the original said "부러진 다리로…" and "부러진 다리는 제대로 붙지 않았다…".
        /// </summary>
        public const string LegWalkLine = "지친 몸으로 걷는 길은 끝이 없었다. 지나가던 구호 트럭이 태워 주지 않았다면 그 길 위에서 멈췄을 것이다.",
            LegAfterLine = "그날 걸었던 먼 길이 지금도 가끔 꿈에 나온다.";

        public static string[] Paths => Fates.Paths;
        public static HashSet<string> Leave => Fates.Leave;
        public static HashSet<string> Town => Fates.Town;

        /// <summary>World endings in tie-break order.</summary>
        public static readonly string[] Order = { "commons", "reopen", "relief", "board" };

        /// <summary>The port's world endings in tie-break order, after the original's Order (competition upgrade).</summary>
        public static readonly string[] PortOrder = { "report", "care" };

        /// <summary>Ids of the endings the port added after the original 11 (EndingsTests checks the original part against its golden).</summary>
        public static IReadOnlyList<string> PortAdded => PortTable.Select(e => e.Id).ToArray();

        static int Rank(string id)
        {
            int i = Array.IndexOf(Order, id);
            return i >= 0 ? i : Order.Length + Array.IndexOf(PortOrder, id);
        }

        /// <summary>The world endings this run may reach: the original four, and a port ending once its gate is met.</summary>
        static IEnumerable<EndingDef> Worlds(Func<string, bool> has) =>
            AllEndings.Where(e => e.Kind == "world" && (e.Gate == null || e.Gate.Any(has)));

        // How you left: lines[0] of every ending on that path.
        static readonly Dictionary<string, string> Opening = new()
        {
            ["bus"] = "버스는 남쪽의 임시 주거단지로 향했다. 좁은 컨테이너 방이었지만, 밤새 포성이 들리지 않았다.",
            ["missed"] = "버스는 이미 떠난 뒤였다. 다음 차량은 사흘 뒤라고 했다.", // missed can happen anywhere, not only at the assembly point
            ["stay"] = "박 소장과 함께 남았다. 빈 집들의 문을 단속하고, 남은 주민들과 물탱크를 나눠 썼다.",
            ["seoyeon"] = "서연의 지원 차량을 함께 기다렸다. 차량의 경사판(리프트)이 고장 나 둘이서 한참을 씨름한 끝에, 해 질 무렵 같은 차로 떠났다.",
            ["volunteer"] = "행정복지센터 자원봉사 명단에 이름을 적었다. 급수 줄을 정리하고, 거동이 불편한 집에 물을 날랐다.",
            ["walk"] = "버스를 기다리지 않고 걸어서 도시를 빠져나왔다. 통제선 밖 국도에는 같은 선택을 한 사람들이 길게 늘어서 있었다.",
        };

        static readonly Dictionary<string, string> PathTrace = new()
        {
            ["bus"] = "집결지에서 남쪽으로 가는 버스를 탔다.", ["walk"] = "버스를 기다리지 않고 걸어서 도시를 떠났다.", ["seoyeon"] = "서연과 함께 지원 차량을 기다렸다.",
            ["missed"] = "집결지에 늦어 버스를 놓쳤다.", ["stay"] = "박 소장과 함께 단지에 남았다.", ["volunteer"] = "행정복지센터 자원봉사에 남았다.",
        };

        static int StockOf(GameState g, params string[] ids) =>
            ids.Sum(id => g.Items.TryGetValue(id, out var e) && e != null ? e.Home + e.Bag : 0);

        /// <summary>Drinking water in litres (home + bag), as the day ledger counts it: never in bottles.</summary>
        public static double WaterLitres(GameState g) => 2 * StockOf(g, "water_2l") + 0.5 * StockOf(g, "water_500");

        /// <summary>Enough laid in to see out the days the taps were off.</summary>
        public static class StockOk
        {
            public const double Litres = 10;
            public const int Food = 4;
        }

        static bool Stocked(GameState g) => WaterLitres(g) >= StockOk.Litres && StockOf(g, "rice_instant", "canned", "bars", "ramen") >= StockOk.Food;

        static string StockLine(GameState g) => Stocked(g)
            ? "미리 쌓아 둔 물과 식량 덕분에, 급수가 끊긴 날들을 버틸 수 있었다."
            : "물과 먹을 것이 금세 바닥났다. 급수차가 오는 날만 손꼽아 기다렸다.";

        /// <summary>How the rationed days went; a body that gave out is never told it held out to the end.</summary>
        static string LedgerLine(GameState g, string path)
        {
            var led = Ledger.LedgerSummary(g);
            if (led.Days == 0) return null;
            bool weak = (path == "walk" || path == "volunteer") && g.Stats.Health < 40;
            if (led.Skipped != 0) return weak
                ? "끼니를 거른 날도 있었다. 하루치를 나눠 쓴 것만으로는 몸이 버티지 못했다."
                : "끼니를 거른 날도 있었지만, 하루치를 나눠 쓴 덕분에 마지막까지 버틸 수 있었다.";
            if (led.Trips != 0) return $"급수소에 다녀온 날이 {JsSem.NumText(led.Trips)}일이었다. 하루치를 정해 두니 물이 언제 떨어질지 미리 알 수 있었다.";
            return Town.Contains(path) && !Stocked(g)
                ? "하루치를 정해 두니 언제 떨어질지 미리 알 수 있었다."
                : "물과 먹을 것을 하루치씩 나눠 둔 덕분에, 바닥이 보이기 전에 다음 일을 정할 수 있었다.";
        }

        static (string, int)[] Need(params (string, int)[] need) => need;
        static string[] L(params string[] lines) => lines;

        // need: (axis, threshold) for world endings; axes: what the trace scores on leave/base endings.
        static readonly EndingDef[] Table =
        {
            new EndingDef
            {
                No = 1, Id = "bus", Kind = "leave", Title = "남쪽에서 온 편지", Place = "남부 임시 주거단지 · D+14", Axes = new[] { "seoyeon", "manager", "clerk", "elder" },
                Hint = "집결지에서 버스를 타고 떠났다면.",
                Lines = (g, a, has) => L(
                    "단지 입구 게시판에 '연무동 주민 우편함'이 생겼다는 소식을 박 소장의 문자로 들었다.",
                    Deeds.NeighbourIds.Any(k => Fates.LetterOf(k, a, has) != null) ? "우편함에 내 앞으로 온 짧은 편지들이 있었다." : "우편함은 비어 있었다. 연무동에 연락처를 남겨 둔 사람이 없었다.",
                    a["logistics"] >= 6 ? "윤 팀장의 연락으로 단지 구호물자 창고에서 분류 일을 돕기 시작했다." : null,
                    has("bus_helped") ? "리프트 앞에서 짐을 들어 드린 어르신과 같은 동에 배정됐다. 아침마다 먼저 인사를 건네 오신다." : null),
            },
            new EndingDef
            {
                No = 2, Id = "walk", Kind = "leave", Title = "국도의 끝", Place = "국도 · 대구 방향 · D+14", Axes = new[] { "logistics", "info" },
                Hint = "버스를 기다리지 않고 걸어서 떠났다면.",
                Lines = (g, a, has) => L(
                    has("driver_text") ? "국도에서 낯익은 화물차가 섰다. 문자를 받았던 김 기사님이었다. 남은 길은 조수석에서 갔다."
                        : g.HasInjury("leg") ? LegWalkLine
                            : g.Stats.Health < 40 ? "몸이 버티지 못해 길가에 주저앉았다가 구호소에 실려 갔다. 멈춘 건 실패가 아니었다. 도움을 받을 수 있는 곳까지 간 것이었다."
                                : "사흘을 걸어 친척이 있는 남쪽 도시에 닿았다.",
                    has("card_family") ? "종이 카드에 적어 둔 번호로 대구 부모님께 전화를 걸었다. 휴대폰이 꺼져 있어도 번호는 남아 있었다." : null),
            },
            new EndingDef
            {
                No = 3, Id = "seoyeon", Kind = "leave", Title = "같은 속도로", Place = "연무 생활문화센터 · C 경사로 · D+14", Axes = new[] { "seoyeon" },
                Hint = "서연의 지원 차량을 함께 기다렸다면.",
                Lines = (g, a, has) => L(
                    "D+14, 서연과 함께 생활문화센터로 돌아왔다. 이번에는 대피하러 온 게 아니라 출입구를 점검하러 왔다.",
                    has("stairs_asked") ? "서연이 만든 점검표 첫 줄에는 이렇게 적혀 있었다. \"돕기 전에 먼저 물어볼 것.\"" : "경사로의 폭과 손잡이 높이를 하나씩 재어 적었다."),
            },
            new EndingDef
            {
                No = 4, Id = "missed", Kind = "base", Title = "두 번째 버스", Place = "103동 302호 · D+15", Axes = new[] { "manager", "town" },
                Hint = "집결지에 늦어 버스를 놓쳤다면.",
                Lines = (g, a, has) => L(
                    a["manager"] >= 4 ? "사흘째 되던 날, 박 소장이 문을 두드렸다. \"302호, 있어요? 다음 버스 명단에 올려 뒀어요.\""
                        : has("door_note") ? "남긴 쪽지를 보고 대피 담당 공무원이 찾아왔다. 다음 버스 명단에 이름이 올랐다."
                            : has("card_meet") ? "카드에 적어 둔 두 번째 만남의 장소, 행정복지센터에 가서 이름을 남겼다. 다음 버스 명단에 이름이 올랐다."
                                : "사흘 동안 아무도 문을 두드리지 않았다. 두 번째 버스는 하루 늦게 왔다.",
                    StockLine(g)),
            },
            new EndingDef
            {
                No = 5, Id = "stay", Kind = "base", Title = "지킨 사람", Place = "한빛아파트 관리사무소 · D+14", Axes = new[] { "manager", "town" },
                Hint = "박 소장과 함께 단지에 남았다면.", Lines = (g, a, has) => L(StockLine(g)),
            },
            new EndingDef
            {
                No = 6, Id = "volunteer", Kind = "base", Title = "남은 손", Place = "행정복지센터 비상급수시설 · D+14", Axes = new[] { "town", "logistics" },
                Hint = "행정복지센터 자원봉사에 남았다면.",
                Lines = (g, a, has) => L(g.Stats.Health < 40 ? "지친 몸으로 무리한 뒤 한동안 앓아누웠지만, 그 일을 후회하지는 않았다." : null),
            },
            new EndingDef
            {
                No = 7, Id = "commons", Kind = "world", Title = "불 꺼진 단지의 공동 부엌", Place = "관리사무소 앞 · D+14", Need = Need(("manager", 6), ("elder", 4), ("town", 5)),
                Hint = "박 소장, 304호 어르신과 자주 얼굴을 마주하고 이웃 일을 함께 나눴다면.",
                Lines = (g, a, has) => L(
                    "관리사무소 앞에 천막이 섰다. 남은 주민들이 쌀과 버너를 모아 공동 부엌을 꾸렸다.",
                    "박 소장은 남은 집 명단 옆에 끼니 명부를 붙였다. 누가 먹었는지가 아니라, 누가 아직 못 왔는지를 보는 명부였다.",
                    has("stock_to_manager") ? "첫 상자에는 '302호'라고 적혀 있었다." : null),
            },
            new EndingDef
            {
                No = 8, Id = "reopen", Kind = "world", Title = "다시 문을 연 가게", Place = "바로편의점 · D+13", Need = Need(("clerk", 5)),
                Hint = "수아와 한 약속을 지키고, 비어 있는 가게를 대신 돌봤다면.",
                Lines = (g, a, has) => L(
                    "D+13, 바로편의점 셔터가 다시 올라갔다. 수아가 돌아왔다.",
                    "계산대 옆에 나눔 선반이 생겼다. 필요한 만큼 가져가고, 남는 것은 두고 가는 선반이었다.",
                    has("store_shuttered") ? "\"셔터 내려 주신 거, 쪽지 보고 알았어요. 덕분에 가게가 그대로였어요.\" 수아가 말했다."
                        : has("store_iou") ? "\"쪽지 봤어요. 금액은 천천히 주셔도 돼요. 적어 두신 게 고마웠어요.\" 수아가 말했다." : null),
            },
            new EndingDef
            {
                No = 9, Id = "relief", Kind = "world", Title = "물길을 잇는 사람", Place = "한결물류 연무센터 · D+14", Need = Need(("logistics", 6), ("town", 3)),
                Hint = "물류 일을 하던 손으로 윤 팀장과 이웃들의 일을 도왔다면.",
                Lines = (g, a, has) => L(
                    "D+10, 윤 팀장에게서 다시 전화가 왔다. 분류 일손이 모자라니 나와 줄 수 있느냐고 했다.",
                    "확인된 수량만 적고, 모르는 것은 모른다고 적는 일이었다."),
            },
            new EndingDef
            {
                No = 10, Id = "board", Kind = "world", Title = "확인된 것만 적는 사람", Place = "행정복지센터 게시판 · D+14", Need = Need(("info", 8)), Facts = new[] { "info_rumor", "info_scope" },
                Hint = "들은 말을 옮기기 전에 확인하고, 본 것만 정확히 전했다면.",
                Lines = (g, a, has) => L(
                    "행정복지센터 앞 주민 게시판 정리를 자원봉사로 맡은 지 엿새째다.",
                    "게시판을 세 칸으로 나눴다. 공식 안내, 현장에서 확인한 것, 확인 중."),
            },
            new EndingDef
            {
                No = 11, Id = "shut", Kind = "shadow", Title = "닫힌 문", Place = "103동 3층 복도 · D+14", Axes = new[] { "harm" },
                Hint = "연무동에 남아, 이웃에게 먼저 문을 닫았다면.",
                Lines = (g, a, has) => L(
                    "D+14. 복도의 문들은 대부분 닫혀 있다. 급수 소식은 쪽지로 돌았지만, 302호 문에는 붙지 않았다.",
                    "누구도 탓하지 않았다. 다만 아무도 먼저 문을 두드리지 않았다.",
                    "그래도 급수차는 매일 왔고, 줄에 서면 물을 받을 수 있었다.",
                    "몇 주 뒤, 급수대 줄에서 먼저 인사를 건넨 건 정민이었다. 대답은 짧았지만, 대답은 있었다."),
            },
        };

        // ---- added by the port (competition upgrade): two in-town world endings on the port's axes (Deeds.PortAxisNames) ----
        static readonly EndingDef[] PortTable =
        {
            new EndingDef
            {
                No = 12, Id = "report", Kind = "world", Title = "짧고 정확한 한 줄", Place = "연무파출소 앞 주민 안전 교실 · D+14", Need = Need(("report", 14)),
                // ending 10 (board) is about what is written on the board; this one is about the call: 112 · 1338 · 119, said short
                Facts = new[] { "report_112_text", "report_object" },
                Hint = "수상한 것을 보면 다가가지 않고, 112·1338·119에 본 것만 짧게 알렸다면.",
                Gate = new[] { "sighting_reported", "sighting_honest", "object_reported", "tt_order_good", "fire_call_clear", "intercom_checked", "intercom_police", "looter_reported", "mountain_rumor_checked", "shelter_notice_read" },
                Lines = (g, a, has) => L(
                    "D+14, 연무파출소 앞 공터에서 주민 안전 교실이 열렸다. 주제는 '전화 한 통, 문자 한 줄로 알리기'. 순경이 그날의 신고를 예로 들어 달라고 정민에게 부탁했다.",
                    has("sighting_reported") ? "정민은 칠판에 그날의 신고 카드를 옮겨 적었다. 언제, 어디서, 몇 명, 무엇을 보았는지. 짐작한 칸은 하나도 없었다."
                        : has("sighting_honest") ? "정민은 칠판에 그날의 신고 카드를 옮겨 적었다. 모르는 칸에는 '모름'이라고 썼다. 순경은 그 칸이 가장 쓸모 있었다고 했다." : null,
                    has("object_reported") ? "잔디밭 낙하물 이야기에서는 아이들이 먼저 손을 들었다. \"만지지 않고, 물러나서, 군부대(1338)나 경찰서!\"" : null,
                    has("fire_call_clear") ? "복도 불을 119에 알릴 때 무엇부터 말했느냐는 질문에, 정민은 동·호수부터였다고 답했다." : null,
                    has("tt_order_good") ? "22분 동안 무엇부터 했느냐는 질문에는, 방송부터 들었다고 답했다." : null,
                    "교실이 끝나고 아이들이 신고 연습 카드에 한 줄씩 적었다. \"어디서, 무엇을, 본 것만. 짧고 정확하게.\""),
            },
            new EndingDef
            {
                No = 13, Id = "care", Kind = "world", Title = "돌봄 순번표", Place = "103동 1층 현관 · D+14", Need = Need(("care", 16)),
                Facts = new[] { "people_ask", "shelter_share" },
                Hint = "어르신과 아이, 쓰러진 이웃 곁을 지키고 가진 것을 나눴다면.",
                Gate = new[] { "cpr_helped", "cpr_relay", "elder_pov_walked", "shelter_child_care", "shelter_child_referred", "shelter_outlet_shared", "shelter_water_carried" },
                Lines = (g, a, has) => L(
                    "103동 현관에 종이 한 장이 붙었다. 이름 옆에 칸이 셋이었다. 안부 묻기, 물 나르기, 아이 봐 주기.",
                    has("elder_alert_knock") || has("queue_carry_elder") ? "304호 칸에는 날마다 두 사람의 이름이 적혔다. 첫 줄은 302호였다." : null,
                    has("cpr_relay") ? "대피소에서 함께 교대로 가슴을 눌렀던 이웃이 주민센터 심폐소생술 교육 신청서를 두 장 들고 왔다."
                        : has("cpr_helped") ? "대피소에서 할아버지 곁을 지켰던 일을 듣고, 이웃들이 심폐소생술 교육을 함께 신청했다." : null,
                    has("shelter_child_care") ? "임시 거처에서 봐 주었던 지우는 순번표의 '아이 봐 주기' 칸에 스티커를 붙이는 일을 맡았다."
                        : has("shelter_child_referred") ? "임시 거처의 아이 돌봄 자원봉사 자리를 본떠, 순번표에 '아이 봐 주기' 칸이 생겼다. 지우 엄마가 첫 줄에 이름을 적었다." : null,
                    "칸이 비면 누구든 먼저 이름을 적었다. 혼자 남는 집이 없도록."),
            },
        };

        static readonly EndingDef[] AllEndings = Table.Concat(PortTable).ToArray();

        static readonly Dictionary<string, EndingDef> ByIdMap = AllEndings.ToDictionary(e => e.Id);

        /// <summary>Every ending in order: the original 11 (ENDINGS), then the port's (report, care). The gallery shows them all.</summary>
        public static IReadOnlyList<EndingDef> All => AllEndings;

        /// <summary>"열세": the number of endings as the gallery's title says it.</summary>
        public static string CountWord(int n) => n switch { 11 => "열한", 12 => "열두", 13 => "열세", 14 => "열네", 15 => "열다섯", _ => JsSem.NumText(n) };

        /// <summary>The ending with this id, or null.</summary>
        public static EndingDef ById(string id) => id != null && ByIdMap.TryGetValue(id, out var e) ? e : null;

        /// <summary>How far the axes are towards a world ending: 1 = every threshold met.</summary>
        static double Fit(EndingDef e, IReadOnlyDictionary<string, int> a) =>
            e.Need.Length == 0 ? double.PositiveInfinity : e.Need.Min(n => (double)a[n.Axis] / n.Threshold);

        static string Choose(IReadOnlyDictionary<string, int> a, string path, Func<string, bool> has)
        {
            if (Town.Contains(path) && a["harm"] >= 4) return "shut";
            if (!Town.Contains(path)) return path;
            var best = Worlds(has).Select(e => (e, fit: Fit(e, a))).Where(x => x.fit >= 1)
                .OrderByDescending(x => x.fit).ThenBy(x => Rank(x.e.Id)).Select(x => x.e).FirstOrDefault();
            return best != null ? best.Id : path;
        }

        static string Clock(int day, int minute)
        {
            string d = day == 0 ? "D-day" : day < 0 ? $"D{JsSem.NumText(day)}" : $"D+{JsSem.NumText(day)}";
            return $"{d} {GameRules.Hhmm(minute)}";
        }

        static string WhenOf(string chapter, int day, int minute) =>
            $"{(chapter != null && Deeds.Ch.TryGetValue(chapter, out var name) ? name : Deeds.Ch["evac"])} · {Clock(day, minute)}";

        /// <summary>3–5 deeds that led here (fewer only when the run holds fewer), shown in the order they happened.</summary>
        public static List<TraceItem> Trace(GameState g, string id, Func<string, string> factSource)
        {
            var end = ById(id) ?? ByIdMap["bus"];
            var path = Fates.PathOf(g);
            var pe = g.Events.LastOrDefault(e => e.Type == "path");
            // The path item stands for the last-day choice, so a deed read from that same event (stayed, …) is left out.
            var ds = Deeds.Of(g).Where(d => id == "shut" || pe == null || d.Seq != pe.Seq).ToList();

            double Score(Deed d)
            {
                int Weight(string k) => d.Id == "violence"
                    ? (k == "harm" ? d.N + 2 * (d.Ko ?? 0) : 0)
                    : Deeds.Weight(d.Id, k); // the table's weight; on the port's axes (report, care) the port's
                if (end.Kind == "world") return end.Need.Aggregate(0.0, (s, n) => s + (double)Math.Max(0, Weight(n.Axis)) / n.Threshold);
                return end.Axes.Aggregate(0.0, (s, k) => s + Math.Max(0, Weight(k)));
            }

            var items = new List<(double seq, TraceItem item)>();
            if (id != "shut")
                items.Add((pe != null ? pe.Seq : double.PositiveInfinity, new TraceItem
                {
                    Id = "path", Text = PathTrace[path], When = pe != null ? WhenOf(pe.Chapter, pe.Day, pe.Minute) : Deeds.Ch["evac"],
                }));
            var picked = ds.Select(d => (d, score: Score(d))).Where(x => x.score > 0)
                .OrderByDescending(x => x.score).ThenBy(x => x.d.Seq).Select(x => x.d).ToList();
            var weighted = ds.Where(d => Deeds.Get(d.Id).W.Count > 0 && !picked.Contains(d)).Reverse().ToList(); // most recent first
            foreach (var d in picked.Concat(weighted))
            {
                if (items.Count >= 5 || (items.Count >= 3 && !picked.Contains(d))) break;
                var def = Deeds.Get(d.Id);
                var item = new TraceItem { Id = d.Id, Text = def.Line, When = WhenOf(d.Chapter, d.Day, d.Minute) };
                // the source of the one fact shown
                var source = def.Facts.Length > 0 ? factSource?.Invoke(def.Facts[0]) : null;
                if (!string.IsNullOrEmpty(source)) item.SourceIds.Add(source);
                items.Add((d.Seq, item));
            }
            return items.OrderBy(x => x.seq).Select(x => x.item).ToList();
        }

        /// <summary>World endings this run did not reach, closest first, with the first undone deed on the weakest axis.</summary>
        public static List<NearMiss> NearMisses(GameState g, string id)
        {
            var a = Deeds.AllAxes(g, final: true);
            var done = new HashSet<string>(Deeds.Of(g).Select(d => d.Id));
            string pre = Leave.Contains(Fates.PathOf(g)) ? "연무동에 남았다면 — " : "";
            return Worlds(done.Contains).Where(e => e.Id != id).Select(e => (e, fit: Fit(e, a)))
                .OrderByDescending(x => x.fit).ThenBy(x => Rank(x.e.Id)).Take(2)
                .Select(x =>
                {
                    var e = x.e;
                    string weak = e.Need.OrderBy(n => (double)a[n.Axis] / n.Threshold).First().Axis;
                    var tip = Deeds.Table.FirstOrDefault(d => Deeds.Weight(d.Id, weak) > 0 && !done.Contains(d.Id));
                    return new NearMiss { Id = e.Id, Title = e.Title, Hint = pre + e.Hint, Tip = tip != null ? $"{Deeds.Ch[tip.Ch]} — {tip.Short}" : "" };
                }).ToList();
        }

        /// <summary>The ending this run reaches, with everything the ending screen says.</summary>
        public static Fate PickEnding(GameState g, Func<string, string> factSource)
        {
            var path = Fates.PathOf(g);
            var a = Deeds.AllAxes(g, final: true);
            var done = new HashSet<string>(Deeds.Of(g).Select(d => d.Id));
            Func<string, bool> has = done.Contains;
            var id = Choose(a, path, has);
            var e = ByIdMap[id];
            var lines = new List<string> { Opening[path] };
            lines.AddRange(e.Lines(g, a, has));
            lines.Add(LedgerLine(g, path));
            lines.RemoveAll(string.IsNullOrEmpty);
            // The leg is told once: the walk's own line may already have said it. (Only old saves carry a leg injury now.)
            if (g.HasInjury("leg") && !lines.Contains(LegWalkLine)) lines.Add(LegAfterLine);
            if (a["harm"] >= 2 && id != "shut") lines.Add("몇몇 이웃은 끝내 눈을 마주치지 않았다."); // on town paths harm 4+ is already 'shut'
            return new Fate
            {
                Id = id, No = e.No, Title = e.Title, Place = e.Place, Kind = e.Kind, Path = path, Lines = lines,
                People = Fates.PeopleLines(g, id), Town = Fates.TownLines(g, id),
                Trace = Trace(g, id, factSource), Missed = NearMisses(g, id),
                SourceIds = e.Facts.Select(f => factSource?.Invoke(f)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList(),
            };
        }

        // ---------- the ending gallery ----------

        /// <summary>yyyy-MM-dd, ASCII digits, nothing after it.</summary>
        internal static readonly Regex DayText = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z");

        /// <summary>Sanitises the stored gallery text; anything unexpected is dropped.</summary>
        public static Gallery ReadGallery(string raw) => Sanitise(JsSem.Parse(raw));

        /// <summary>Sanitises a parsed gallery (a JSON string value is read as the stored text).</summary>
        public static Gallery ReadGallery(JToken raw) => Sanitise(raw != null && raw.Type == JTokenType.String ? JsSem.Parse((string)raw) : raw);

        static Gallery Sanitise(JToken raw)
        {
            var gal = new Gallery();
            if (raw is JObject g && g["seen"] is JObject src)
                foreach (var p in src.Properties())
                {
                    if (ById(p.Name) == null || p.Value is not JObject v || JsSem.Str(v["at"]) is not string at || !DayText.IsMatch(at)) continue;
                    gal.Seen[p.Name] = new GallerySeen
                    {
                        At = at,
                        Runs = JsSem.IsInt(v["runs"]) && JsSem.Number(v["runs"]) >= 1 && JsSem.Number(v["runs"]) < 9e15 ? (long)JsSem.Number(v["runs"]) : 1,
                        Trace = v["trace"] is JArray t ? t.Select(JsSem.Str).Where(x => Deeds.Get(x) != null).Take(5).ToList() : new List<string>(),
                    };
                }
            return gal;
        }

        /// <summary>A sanitised copy of a live gallery.</summary>
        public static Gallery ReadGallery(Gallery gal) => ReadGallery(gal == null ? null : JToken.FromObject(gal));

        /// <summary>Adds a seen ending: the first day stays, runs count up, the trace is the latest run's.</summary>
        public static Gallery AddToGallery(Gallery gal, string id, IEnumerable<string> traceIds, string at)
        {
            var o = ReadGallery(gal);
            if (ById(id) == null || at == null || !DayText.IsMatch(at)) return o;
            o.Seen.TryGetValue(id, out var old);
            o.Seen[id] = new GallerySeen
            {
                At = old?.At ?? at, Runs = (old?.Runs ?? 0) + 1,
                Trace = (traceIds ?? Enumerable.Empty<string>()).Where(t => Deeds.Get(t) != null).Take(5).ToList(),
            };
            return o;
        }

        public static Gallery AddToGallery(string raw, string id, IEnumerable<string> traceIds, string at) => AddToGallery(ReadGallery(raw), id, traceIds, at);

        /// <summary>All the cards in order (the original 11, then the port's); unseen endings keep their title and place hidden.</summary>
        public static List<GalleryCard> GalleryView(Gallery gal)
        {
            var seen = ReadGallery(gal).Seen;
            return AllEndings.Select(e =>
            {
                seen.TryGetValue(e.Id, out var s);
                return new GalleryCard
                {
                    No = e.No, Id = e.Id, Seen = s != null, Title = s != null ? e.Title : null, Place = s != null ? e.Place : null, At = s?.At,
                    Shorts = s != null ? s.Trace.Take(3).Select(t => Deeds.Get(t).Short).ToList() : new List<string>(), Hint = e.Hint,
                };
            }).ToList();
        }

        public static List<GalleryCard> GalleryView(string raw) => GalleryView(ReadGallery(raw));
    }
}
