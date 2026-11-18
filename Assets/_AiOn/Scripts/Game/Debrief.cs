using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A fail card: 당시 확인한 정보 / 판단에서 놓친 부분 / 다음에 시도할 준비.</summary>
    public sealed class FailCard
    {
        [JsonProperty("title")] public string Title;
        [JsonProperty("seen")] public string Seen;
        [JsonProperty("missed")] public string Missed;
        [JsonProperty("next")] public string Next;
        /// <summary>Sources of the facts the card rests on, each once.</summary>
        [JsonProperty("sourceIds")] public List<string> SourceIds = new();
    }

    /// <summary>A preparation that paid off: its Korean name, the moment, and the raw prep id.</summary>
    public sealed class Payoff
    {
        [JsonProperty("prep", NullValueHandling = NullValueHandling.Ignore)] public string Prep;
        [JsonProperty("moment", NullValueHandling = NullValueHandling.Ignore)] public string Moment;
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)] public string Id;
    }

    public sealed class ChapterSummary
    {
        [JsonProperty("lines")] public List<string> Lines = new();
        [JsonProperty("payoffs")] public List<Payoff> Payoffs = new();
    }

    public sealed class UnusedItem
    {
        [JsonProperty("item")] public string Item;
        [JsonProperty("why")] public string Why;
    }

    public sealed class PersonRow
    {
        [JsonProperty("who")] public string Who;
        [JsonProperty("what")] public string What;
    }

    /// <summary>The real-life practice line: a fact's text and its source.</summary>
    public sealed class PracticeLine
    {
        [JsonProperty("text")] public string Text;
        [JsonProperty("sourceId")] public string SourceId;
    }

    /// <summary>What the app recorded for this run (a 'run_recorded' event) with the run's times.</summary>
    public sealed class RecordedRun
    {
        [JsonProperty("stats")] public RunStats Stats;
        /// <summary>{ done, fresh, bests, rank } as the event stored it.</summary>
        [JsonProperty("news")] public JObject News;
    }

    /// <summary>The ending's 생존 기록.</summary>
    public sealed class EndingRecord
    {
        [JsonProperty("fate")] public Fate Fate;
        [JsonProperty("helped")] public List<Payoff> Helped;
        [JsonProperty("unused")] public List<UnusedItem> Unused;
        /// <summary>What was never checked, in the player's words.</summary>
        [JsonProperty("unchecked")] public List<string> Unchecked;
        [JsonProperty("people")] public List<PersonRow> People;
        [JsonProperty("ripples")] public List<RippleRow> Ripples;
        [JsonProperty("practice")] public PracticeLine Practice;
        /// <summary>Null until the app has recorded the run.</summary>
        [JsonProperty("run", NullValueHandling = NullValueHandling.Ignore)] public RecordedRun Run;
        [JsonProperty("share")] public string Share;
    }

    /// <summary>
    /// Fail cards, chapter summaries and the ending's 생존 기록 (original game/debrief.js). Lines describe the
    /// situation, never blame the player or anyone else; safety statements are FACTS texts, game advice names
    /// planner actions. fact: fact id -> text (<c>facts.Text</c>); factSource: fact id -> source id (<c>facts.SourceId</c>).
    /// </summary>
    public static class Debrief
    {
        /// <summary>Preparation id -> Korean name (game/ripples.js PREP).</summary>
        public static IReadOnlyDictionary<string, string> Prep => Ripples.Prep;

        /// <summary>'help' / 'trade' who ids. elder and stranger have no G.rel entry; trader is the barter.</summary>
        public static readonly IReadOnlyDictionary<string, string> People = new Dictionary<string, string>
        {
            ["manager"] = "박영수 소장", ["clerk"] = "이수아", ["seoyeon"] = "오서연", ["elder"] = "304호 어르신", ["stranger"] = "문을 두드린 이웃", ["trader"] = "102동 주민",
        };

        static readonly Dictionary<string, string> Contact = new()
        {
            ["sms"] = "문자", ["door"] = "문 두드리기", ["both"] = "문자·문에 쪽지", ["office"] = "관리사무소 창구", ["store"] = "가게로 찾아가기",
        };

        static readonly Dictionary<string, string> Tool = new()
        {
            ["radio"] = "라디오", ["bag"] = "비상가방", ["route"] = "경로 정하기", ["extinguisher"] = "소화기", ["docs"] = "비상연락처 카드", ["info"] = "정보 가려 듣기",
        };

        // Every result string the story passes to recordEncounter, so no raw id reaches the player.
        static readonly Dictionary<string, string> Result = new()
        {
            ["out"] = "불을 껐습니다", ["retreat-ok"] = "대피하며 알렸습니다", ["escalated"] = "불이 커져 물러났습니다", ["incomplete"] = "끝까지 마치지 못했습니다",
            ["risky"] = "연기 속으로 바로 나섰습니다", ["practice"] = "사용 순서를 익혔습니다", ["tuned"] = "공식 방송을 찾아 들었습니다", ["opened"] = "라디오를 켜 보았습니다",
            ["missed"] = "공식 방송은 찾지 못했습니다", ["complete"] = "필요한 것을 다 챙겼습니다", ["partial"] = "몇 가지가 빠졌습니다", ["planned"] = "경로를 정해 두었습니다",
            ["written"] = "카드를 적어 두었습니다", ["skipped"] = "아무것도 적지 않았습니다", ["sorted"] = "출처를 가려 들었습니다", ["misled"] = "소문을 따라갔습니다",
            ["doubted"] = "확인된 소식도 한 번 더 의심했습니다", ["checked"] = "한 번 더 확인했습니다",
        };

        static readonly (string stat, string label)[] StatNames = { ("health", "체력"), ("thirst", "갈증"), ("fatigue", "피로"), ("tension", "긴장") };

        static readonly (string label, string name)[] LabelNames =
        {
            ("official", "공식 안내"), ("site", "현장 확인"), ("neighbor", "이웃에게 들음"), ("rumor", "소문·미확인"), ("self", "내 기록"),
        };

        const string Tried = "해 봤습니다";

        static JToken KnowOf(GameState g, string key) => key != null && g.Know != null && g.Know.TryGetValue(key, out var v) ? v : null;

        static Payoff PayoffOf(GameEvent e)
        {
            var prep = Deeds.Det(e)["prep"];
            var id = JsSem.Nullish(prep) ? null : JsSem.Text(prep);
            return new Payoff { Prep = id != null && Prep.TryGetValue(id, out var name) ? name : id, Moment = JsSem.Str(Deeds.Det(e)["moment"]), Id = id };
        }

        static List<string> SourcesOf(IEnumerable<string> ids, Func<string, string> factSource) =>
            ids.Select(id => factSource?.Invoke(id)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

        static GameEvent LastEncounter(GameState g, string tool) =>
            g.Events.LastOrDefault(e => e.Type == "encounter" && JsSem.Is(Deeds.Det(e)["tool"], tool));

        /// <summary>HUD value words; health is better high, the other stats are better low.</summary>
        static string Word(string stat, double v) => GameRules.StatWord(stat, v); // the HUD's word, so the card and the HUD agree

        static double StatOf(GameState g, string stat) => stat switch
        {
            "health" => g.Stats.Health, "thirst" => g.Stats.Thirst, "fatigue" => g.Stats.Fatigue, "tension" => g.Stats.Tension,
            _ => throw new ArgumentException($"unknown stat: {stat}"),
        };

        /// <summary>recordEncounter takes a string or an eval object ({ result } or { ok }).</summary>
        static string ResultWord(JToken r)
        {
            if (JsSem.Str(r) is string text) return Result.TryGetValue(text, out var word) ? word : Tried;
            var o = r as JObject;
            if (JsSem.Truthy(o?["result"])) return JsSem.Str(o["result"]) is string id && Result.TryGetValue(id, out var w) ? w : Tried;
            if (o?["ok"] is JToken { Type: JTokenType.Boolean } ok) return (bool)ok ? "잘 해냈습니다" : "아쉬움이 남았습니다";
            return Tried;
        }

        // ---------- fail cards ----------

        sealed class Card
        {
            public string Title, Seen, Missed, Next;
            public string[] Facts;
        }

        static string[] F(params string[] ids) => ids;

        /// <summary>Health ran out: a fall, a fight, a blast, debris, outdoor flames or indoor smoke. detail.why says which.</summary>
        static Card Dead(GameState g, JObject detail, Func<string, string> fact)
        {
            // why defaults to 'hurt' only when it is not given at all
            string why = detail["why"] == null ? "hurt" : JsSem.Str(detail["why"]);
            string[] w = why switch
            {
                "fall" => new[] { "높은 곳에서 떨어졌습니다", "떨어지기 전, 아래까지의 높이를 가늠할 시간이 있었습니다.", "내려갈 때는 계단·사다리처럼 몸을 붙일 수 있는 길을 먼저 찾기" },
                "fight" => new[] { "몸싸움 끝에 쓰러졌습니다", "상대가 먼저 물러설 여지가 있었습니다.", "시비가 붙으면 물건을 내주거나 자리를 피하는 것도 선택지로 두기" },
                "blast" => new[] { "폭발에 휘말렸습니다", "휘파람 같은 낙하음이 들린 뒤 몇 초의 여유가 있었습니다.", "낙하음이 들리면 바로 엎드리고, 건물 안쪽·지하로 몸을 숨기기" },
                "debris" => new[] { "무너진 잔해에 다쳤습니다", "금이 가고 먼지가 떨어지는 건물은 무너지기 전에 신호를 보냅니다.", "흔들리거나 금 간 건물 곁에서는 멀리 떨어져 걷기" },
                "fire" => new[] { "불길 속에서 쓰러졌습니다", "연기가 짙어지기 전에 빠져나갈 길이 있었습니다.", "불길에서 멀리 떨어지고, 연기가 오지 않는 쪽으로 피하기" },
                "smoke" => new[] { "연기를 너무 많이 마셨습니다", "복도 가운데 계단과 비상구 표시등까지는 몇 걸음이었습니다.", fact("fire_big") },
                "hurt" => new[] { "더 이상 버틸 수 없을 만큼 다쳤습니다", "쉬면서 상처를 돌볼 수 있는 순간이 있었습니다.", "구급함을 챙기고, 다쳤을 때는 안전한 곳에서 먼저 처치하기" },
                _ => new[] { "더 이상 버틸 수 없을 만큼 다쳤습니다", "쉬면서 상처를 돌볼 수 있는 순간이 있었습니다.", "다친 뒤에는 먼저 안전한 곳에서 처치하기" },
            };
            string[] facts = why switch
            {
                "blast" => F("alert_airraid", "shelter_near"), "debris" => F("alert_airraid", "shelter_near"), "fire" => F("fire_big"), "smoke" => F("fire_big", "fire_lift"),
                "fall" => F("alert_stairs"), "fight" => F("people_order"), "hurt" => F("home_stock"),
                _ => F("home_stock"),
            };
            string missed = why == "smoke"
                ? (g.Events.Any(e => e.Type == "towel_wet") ? "연기 속에서 몸을 낮추지 않고(C) 오래 머물렀습니다." : "젖은 수건으로 코와 입을 막지 못한 채 연기 속에 있었습니다.")
                : g.Injury != null && g.Injury.Count > 0 ? "이미 다친 몸으로 움직이고 있었습니다." : "몸 상태를 살필 여유가 없었습니다.";
            return new Card { Title = w[0], Seen = w[1], Missed = missed, Next = w[2], Facts = facts };
        }

        static Card Late(GameState g, JObject detail, Func<string, string> fact)
        {
            var dep = g.Events.LastOrDefault(e => e.Type == "departure")?.Details?["bag"]; // dday: packed / none / late
            bool left = JsSem.Truthy(dep);
            bool prep = !g.Knows("shelterLocated") || !g.Knows("entrancesChecked");
            return new Card
            {
                Title = "대피소에 제때 닿지 못했습니다",
                Seen = g.Knows("gateClosedSeen") ? "동쪽 출입구가 닫혀 있다는 것은 현장에서 확인했습니다."
                    : g.Knows("shelterLocated") ? "대피소가 생활문화센터 지하라는 것은 알고 있었습니다."
                        : "경보는 들었지만, 가까운 대피소 위치는 아직 확인 전이었습니다.",
                Missed = !g.Knows("entrancesChecked") ? "출입구 A·B·C 가운데 어디가 열려 있는지 미리 알 방법이 없었습니다."
                    : !g.Knows("routeChecked") ? "통제된 길을 돌아가는 경로는 아직 정해 두지 않은 상태였습니다."
                        : !left ? "경보가 울린 뒤에도 집을 나서지 않았습니다." : "경보가 울린 뒤 출발하기까지 시간이 걸렸습니다.",
                Next = prep ? "준비 단계에서 '대피소·경로 확인'으로 출입구 A·B·C를 둘러보기"
                    : JsSem.Is(dep, "late") || !g.Knows("bagPacked") ? "'비상가방 꾸리기'로 가방을 현관에 두고 바로 나설 수 있게 하기"
                        : !left ? fact("bag_take") : "경보가 울리면 도착할 시각을 정해 두고, 가는 길에 멈추지 않기",
                Facts = F("shelter_know", "shelter_near", "bag_take"),
            };
        }

        /// <summary>detail.why: 'thirst' (default) or 'carry' (walking water home from the water point without a rest).</summary>
        static Card Exhausted(GameRules rules, GameState g, JObject detail)
        {
            var s = g.Stats;
            var load = rules.BagLoad(g);
            bool water = new[] { "water_500", "water_2l" }.Any(id => g.Items.TryGetValue(id, out var e) && e != null && e.Bag > 0);
            if (JsSem.Is(detail["why"], "carry")) return new Card
            {
                Title = "더 걷기 어려울 만큼 지쳤습니다",
                Seen = $"그때 피로는 {Word("fatigue", s.Fatigue)}, 갈증은 {Word("thirst", s.Thirst)} 상태였습니다.",
                Missed = "무거운 물통을 들고 쉬지 않고 걸었습니다. 물 1리터는 1kg입니다.",
                Next = "급수소에서 돌아올 때는 중간중간 멈춰 서서 숨을 고르고, 접이식 물통으로 나눠 들기",
                Facts = F("water_point", "water_sphere"),
            };
            return new Card
            {
                Title = "더 걷기 어려울 만큼 지쳤습니다",
                Seen = $"그때 갈증은 {Word("thirst", s.Thirst)}, 피로는 {Word("fatigue", s.Fatigue)} 상태였습니다.",
                Missed = load.OverWeight ? $"가방이 {JsSem.NumText(load.WeightKg)}kg으로, 들고 걷기 편한 {JsSem.NumText(rules.Db.bagLimitKg)}kg을 넘었습니다."
                    : !water ? "가방 안에 마실 물이 없었습니다." : "중간에 쉴 시간이 따로 없었습니다.",
                Next = load.OverWeight ? "'비상가방 꾸리기'에서 무게 막대를 보며 꼭 필요한 것부터 담기"
                    : !water ? "'비상가방 꾸리기'에서 작은 생수병을 먼저 담기" : "준비 칸 하나를 '휴식'에 남겨 두기",
                Facts = load.OverWeight ? F("bag_foil", "bag_days") : F("bag_days"),
            };
        }

        /// <summary>detail.scene: 'corridor' (3장, default), 'upstairs' (4장 stay: smoke from another flat), 'shelter' (4장 move).</summary>
        static Card Fire(GameState g, JObject detail, Func<string, string> fact)
        {
            if (JsSem.Is(detail["scene"], "upstairs")) return new Card
            {
                Title = "연기 속으로 불을 끄러 나갔습니다",
                Seen = "우리 집 안으로는 연기나 불길이 들어오지 않은 상태였습니다.",
                Missed = fact("fire_stay"),
                Next = "문손잡이를 손등으로 먼저 만져 보고, 연기가 들어오지 않으면 집 안에서 창문을 닫고 119에 신고하기",
                Facts = F("fire_stay", "fire_judge", "fire_door"),
            };
            if (JsSem.Is(detail["scene"], "shelter")) return new Card
            {
                Title = "대피소의 큰 불 앞에서 버텼습니다",
                Seen = "불길이 이미 천장 배관에 닿아 있었습니다.",
                Missed = fact("fire_big"),
                Next = "천장까지 닿은 불은 끄려 하지 말고, \"불이야!\" 외치며 물러나 119에 알리기",
                Facts = F("fire_big", "fire_shout", "fire_119"),
            };
            var r = LastEncounter(g, "extinguisher")?.Details?["result"];
            string res = r is JObject eval ? JsSem.Str(eval["result"]) : JsSem.Str(r);
            string firstError = JsSem.Str((r as JObject)?["firstError"]);
            string stepFact = firstError == null ? null : Drills.ExtSteps.FirstOrDefault(s => s.Id == firstError)?.Facts[0];
            string factId = res == "escalated" ? "fire_big" : stepFact ?? "fire_shout";
            bool located = g.Knows("extinguisherLocated");
            return new Card
            {
                Title = "복도의 불이 커졌습니다",
                Seen = located ? "복도 소화기가 어디 있는지는 알고 있었습니다." : "복도 소화기 위치는 확인하기 전이었습니다.",
                Missed = fact(factId),
                Next = located ? "불길이 천장에 닿았는지 먼저 보고, 끌지 대피할지 정하기"
                    : "'집 장비 점검' 때 복도 소화기 위치와 사용 순서를 확인하기",
                Facts = F(factId, "fire_small", "ext_1"),
            };
        }

        /// <summary>detail.via: 'rumor' when the player acted on a false post (game/rumors.js); otherwise lost on the way.</summary>
        static Card LostWay(GameState g, JObject detail, Func<string, string> fact)
        {
            if (JsSem.Is(detail["via"], "rumor")) return new Card
            {
                Title = "소문을 따라가느라 길을 잃었습니다",
                Seen = "SNS 글에는 누가 언제 확인했는지가 없었습니다.",
                Missed = fact("info_rumor"),
                Next = "재난문자·라디오·게시판으로 한 번 더 확인하고 움직이기",
                Facts = F("info_rumor", "info_harm", "evac_note"),
            };
            bool route = g.Knows("routeChecked");
            return new Card
            {
                Title = "길을 찾느라 시간이 걸렸습니다",
                Seen = route ? "미리 본 경로가 현장 통제로 막혀 있었습니다." : "정전 뒤부터 휴대폰 지도가 잘 열리지 않았습니다.",
                Missed = fact("shelter_signs"),
                Next = route ? "돌아가는 두 번째 경로까지 정해 두기" : "'대피소·경로 확인'으로 경로를 미리 걸어 보고 수첩에 적어 두기",
                Facts = F("shelter_signs", "evac_note"),
            };
        }

        public static readonly string[] FailTypes = { "dead", "late", "exhausted", "fire", "lostWay" };

        /// <summary>
        /// The card for a failed scene. type: one of <see cref="FailTypes"/>; detail: { why } (dead, exhausted),
        /// { scene } (fire), { via } (lostWay), or null.
        /// </summary>
        public static FailCard FailDebrief(GameRules rules, GameState g, string type, JObject detail, Func<string, string> fact, Func<string, string> factSource)
        {
            detail ??= new JObject();
            var card = type switch
            {
                "dead" => Dead(g, detail, fact),
                "late" => Late(g, detail, fact),
                "exhausted" => Exhausted(rules, g, detail),
                "fire" => Fire(g, detail, fact),
                "lostWay" => LostWay(g, detail, fact),
                _ => throw new ArgumentException($"unknown fail type: {type}"),
            };
            return new FailCard { Title = card.Title, Seen = card.Seen, Missed = card.Missed, Next = card.Next, SourceIds = SourcesOf(card.Facts, factSource) };
        }

        // ---------- chapter summary ----------

        static string CartText(GameRules rules, JToken cart) => cart is JObject o
            ? string.Join(", ", o.Properties().Select(p => $"{rules.Db.NameOf(p.Name)} {JsSem.Text(p.Value)}{rules.Db.Get(p.Name)?.unit ?? "개"}"))
            : "";

        static string PersonName(JToken who) => JsSem.Str(who) is string id && People.TryGetValue(id, out var name) ? name : JsSem.Text(who);

        /// <summary>What happened in one chapter, as lines for its closing card, and the preparations that paid off in it.</summary>
        public static ChapterSummary ChapterSummary(GameRules rules, GameState g, string chapter)
        {
            var db = rules.Db;
            var evs = g.Events.Where(e => e.Chapter == chapter).ToList();
            List<GameEvent> Of(string type) => evs.Where(e => e.Type == type).ToList();
            var r = new ChapterSummary();
            var lines = r.Lines;

            var slots = Of("slot");
            if (slots.Count > 0) lines.Add($"준비에 쓴 시간: {JsSem.NumText(slots.Sum(e => JsSem.IsNumber(Deeds.Det(e)["cost"]) ? JsSem.Number(Deeds.Det(e)["cost"]) : 0))}칸");

            var order = new List<string>();
            var bought = new Dictionary<string, double>();
            foreach (var e in Of("buy"))
                if (Deeds.Det(e)["cart"] is JObject cart)
                    foreach (var p in cart.Properties())
                    {
                        if (!bought.ContainsKey(p.Name)) { order.Add(p.Name); bought[p.Name] = 0; }
                        if (JsSem.IsNumber(p.Value)) bought[p.Name] += JsSem.Number(p.Value);
                    }
            if (order.Count > 0) lines.Add($"산 물건: {string.Join(", ", order.Select(id => $"{db.NameOf(id)} {JsSem.NumText(bought[id])}{db.Get(id)?.unit ?? "개"}"))}");

            var checkedItems = Of("check").Select(e => db.NameOf(JsSem.Str(Deeds.Det(e)["id"]))).ToList();
            if (checkedItems.Count > 0) lines.Add($"점검해서 쓸 수 있게 된 것: {string.Join(", ", checkedItems)}");

            // 4장 stay branch files its smoke judgement under 'extinguisher' for the ladder; no extinguisher is used.
            bool smoke = Of("smoke_choice").Count > 0;
            foreach (var e in Of("encounter"))
            {
                var t = Deeds.Det(e)["tool"];
                var res = Deeds.Det(e)["result"];
                if (smoke && JsSem.Is(t, "extinguisher"))
                    lines.Add($"화재 판단: {(JsSem.Is(res, "risky") ? "연기 속으로 바로 나섰습니다" : JsSem.Is(res, "retreat-ok") ? "문을 닫고 신고한 뒤 안내를 기다렸습니다" : ResultWord(res))}");
                else
                    lines.Add($"{(JsSem.Str(t) is string tool && Tool.TryGetValue(tool, out var toolName) ? toolName : JsSem.Text(t))}: {ResultWord(res)}");
            }

            var notes = g.Info.Where(i => i.Chapter == chapter).ToList();
            if (notes.Count > 0)
            {
                var byLabel = LabelNames.Select(l => (l.name, n: notes.Count(i => i.Label == l.label))).Where(x => x.n > 0);
                lines.Add($"수첩에 적은 정보 {JsSem.NumText(notes.Count)}개 ({string.Join(", ", byLabel.Select(x => $"{x.name} {JsSem.NumText(x.n)}"))})");
            }

            foreach (var t in Of("help").Select(e => $"{PersonName(Deeds.Det(e)["who"])}: {JsSem.Text(Deeds.Det(e)["what"])}").Distinct()) lines.Add(t);
            foreach (var e in Of("trade"))
                lines.Add($"물물교환({PersonName(Deeds.Det(e)["from"])}): {CartText(rules, Deeds.Det(e)["give"])} → {CartText(rules, Deeds.Det(e)["get"])}");

            // Up to three deeds someone will remember, as "who — short" (the cue subject without its 은/는; just the short when it names them).
            var kept = Deeds.Of(g).Where(d => d.Chapter == chapter && !string.IsNullOrEmpty(Deeds.Get(d.Id).Cue)).Take(3).ToList();
            string Memo(DeedDef d)
            {
                string who = d.Cue.EndsWith("은", StringComparison.Ordinal) || d.Cue.EndsWith("는", StringComparison.Ordinal) ? d.Cue.Substring(0, d.Cue.Length - 1) : d.Cue;
                return d.Short.Contains(who) ? d.Short : $"{who} — {d.Short}";
            }
            if (kept.Count > 0) lines.Add($"기억된 일: {string.Join(" · ", kept.Select(d => Memo(Deeds.Get(d.Id))))}");

            // The day ledger's plan, as its 'ledger' event keeps it: { water, meals, power }.
            foreach (var e in Of("ledger"))
                if (Deeds.Det(e)["plan"] is JObject plan) lines.Add(Ledger.PlanLine(plan));

            var dead = Of("battery_dead").Select(e => db.NameOf(JsSem.Str(Deeds.Det(e)["id"]))).ToList();
            if (dead.Count > 0) lines.Add($"건전지가 다 닳은 것: {string.Join(", ", dead.Distinct())}");

            lines.Add($"지금 상태: {string.Join(", ", StatNames.Select(s => $"{s.label} {Word(s.stat, StatOf(g, s.stat))}"))}");
            r.Payoffs = Of("payoff").Select(PayoffOf).ToList();
            return r;
        }

        // ---------- the ending's record ----------

        /// <summary>A fixture counts as used once its know flag is set.</summary>
        static readonly Dictionary<string, string> FixtureDone = new() { ["blackout_curtain"] = "curtain" };

        static readonly (string know, string text)[] KnowText =
        {
            ("shelterLocated", "가까운 대피소 위치"),
            ("entrancesChecked", "대피소 출입구 A·B·C와 경사로"),
            ("routeChecked", "통제 구간을 돌아가는 경로"),
            ("extinguisherLocated", "복도 소화기 위치"),
            ("radioChecked", "라디오 주파수와 건전지"),
            ("contactsCard", "종이에 적은 비상연락처"),
            ("docsCopied", "중요 서류 사본"),
            ("meetPoint", "만남의 장소"),
            ("waterPoint", "비상급수시설 위치"),
        };

        // First unchecked know flag picks the real-life practice line.
        static readonly (string know, string fact)[] Practice =
        {
            ("shelterLocated", "shelter_find"), ("entrancesChecked", "shelter_signs"), ("contactsCard", "contact_card"), ("meetPoint", "contact_meet"),
            ("radioChecked", "radio_batt"), ("extinguisherLocated", "ext_1"), ("docsCopied", "bag_copies"), ("waterPoint", "water_point"),
        };

        /// <summary>One row per person (the same help pressed twice is one row): their last two different moments, oldest first.</summary>
        static List<PersonRow> HelpRows(GameState g)
        {
            var order = new List<string>();
            var by = new Dictionary<string, List<string>>();
            foreach (var e in g.Events)
            {
                if (e.Type != "help" || !JsSem.Truthy(Deeds.Det(e)["who"]) || !JsSem.Truthy(Deeds.Det(e)["what"])) continue;
                string who = JsSem.Text(Deeds.Det(e)["who"]), what = JsSem.Text(Deeds.Det(e)["what"]);
                if (!by.TryGetValue(who, out var list)) { by[who] = list = new List<string>(); order.Add(who); }
                if (!list.Contains(what)) list.Add(what);
            }
            return order.Select(who => new PersonRow
            {
                Who = People.TryGetValue(who, out var name) ? name : who,
                What = string.Join(" · ", by[who].Skip(Math.Max(0, by[who].Count - 2))),
            }).ToList();
        }

        /// <summary>
        /// The ending's 생존 기록: the fate, what helped, what went unused or unchecked, the people, the ripples, a
        /// practice line for real life, and the share text. Durable items count as used when items[id].used > 0.
        /// </summary>
        public static EndingRecord EndingRecord(GameRules rules, GameState g, Func<string, string> fact, Func<string, string> factSource)
        {
            var db = rules.Db;
            var unused = new List<UnusedItem>();
            foreach (var (id, e) in g.Items)
            {
                var c = db.Get(id);
                if (c == null) continue;
                if (c.needsCheck != ItemCheck.None && !e.Checked && e.Home + e.Bag > 0)
                    unused.Add(new UnusedItem
                    {
                        Item = c.displayName,
                        Why = e.Charge == 0 ? "건전지가 다 닳아 더 켤 수 없었습니다."
                            : c.needsCheck == ItemCheck.Batteries ? "건전지를 넣지 않아 켜 볼 수 없었습니다." : "충전해 두지 않아 쓸 수 없었습니다.",
                    });
                else if (e.Bought > 0 && e.Used == 0 && !JsSem.Truthy(KnowOf(g, FixtureDone.TryGetValue(id, out var flag) ? flag : null)))
                    unused.Add(new UnusedItem { Item = c.displayName, Why = "사 두었지만 쓸 일이 없었습니다." });
            }

            var people = HelpRows(g);
            foreach (var (k, rel) in g.Rel)
            {
                if (rel == null || !JsSem.Truthy(rel.Contact)) continue; // merged into that person's help row
                string who = People.TryGetValue(k, out var name) ? name : k;
                string how = JsSem.Str(rel.Contact) is string c && Contact.TryGetValue(c, out var label) ? label : JsSem.Text(rel.Contact);
                string what = $"비상 연락 방법을 정했습니다 ({how})";
                var row = people.FirstOrDefault(p => p.Who == who);
                if (row != null) row.What += $" · {what}"; else people.Add(new PersonRow { Who = who, What = what });
            }

            string practice = Practice.Where(p => !g.Knows(p.know)).Select(p => p.fact).FirstOrDefault() ?? "shelter_know";
            // run_recorded: what the app added to this player's records (challenges, bests, rank) before the document.
            var fate = Endings.PickEnding(g, factSource);
            var stats = Challenges.RunStats(g);
            var news = g.Events.LastOrDefault(e => e.Type == "run_recorded")?.Details;
            return new EndingRecord
            {
                Fate = fate,
                Helped = g.Events.Where(e => e.Type == "payoff").Select(PayoffOf).ToList(),
                Unused = unused,
                Unchecked = KnowText.Where(k => !g.Knows(k.know)).Select(k => k.text).ToList(),
                People = people,
                Ripples = Ripples.Of(g),
                Practice = new PracticeLine { Text = fact(practice), SourceId = factSource?.Invoke(practice) },
                Run = news != null ? new RecordedRun { Stats = stats, News = news } : null,
                Share = Challenges.ShareText(fate, news, stats),
            };
        }
    }
}
