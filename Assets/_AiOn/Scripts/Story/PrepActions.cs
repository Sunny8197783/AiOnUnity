// Port of story/prep-actions.js: the six preparation actions of chapter 1, each played in 3D (walk there, look,
// decide), plus the home helpers other chapters reuse (ApplyHome, GoHome, GoOut, FillWater, BagItems, ApplyBag).
// Screens the original opened with ctx.ui.<screen>(opts) go through ctx.Ui.Open("<screen>", opts) with the same
// option object and result: shop, radio, extinguisher, docs, routemap, bag.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Data;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class PrepActions
    {
        public static readonly IReadOnlyList<string> SeoyeonCard = new[] { "보행 보조기를 씁니다", "평지와 완만한 경사로는 혼자 갑니다", "계단과 가파른 내리막은 혼자 내려가지 않습니다", "보조기와 떨어지지 않습니다", "도움이 필요하면 제가 말합니다" };
        /// <summary>Game frequency of the disaster-broadcast station (fictional). Same value as StoryCommon.RadioFreq.</summary>
        public const double RadioFreq = StoryCommon.RadioFreq;

        // ------------------------------------------------------------------ small helpers

        static bool Has(GameState g, string id, string where = "home") =>
            g.Items.TryGetValue(id, out var e) && e != null && (where == "bag" ? e.Bag : e.Home) > 0;
        static bool Owned(GameState g, string id) => Has(g, id) || Has(g, id, "bag");
        static List<string> Tips(StoryContext ctx, params string[] ids) => ids.Select(id => ctx.Facts.Text(id)).Where(t => !string.IsNullOrEmpty(t)).ToList();
        static string F(double x) => x.ToString(CultureInfo.InvariantCulture);
        /// <summary>JS Number.prototype.toLocaleString() for whole won amounts: 150,000.</summary>
        static string Won(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);
        static List<ObjectiveStep> Steps(params (string text, bool done)[] s) => s.Select(x => new ObjectiveStep(x.text, x.done)).ToList();
        static TalkOptions T(string speaker, string role, IEnumerable<string> lines, List<TalkChoice> choices = null) =>
            new TalkOptions { Speaker = speaker, Role = role, Lines = lines.ToList(), Choices = choices };
        static List<TalkChoice> C(params (string id, string label)[] c) => c.Select(x => new TalkChoice(x.id, x.label)).ToList();
        static Func<StoryPoint, Task<JToken>> H(Func<Task<JToken>> f) => _ => f();

        /// <summary>JS truthiness of a JSON value.</summary>
        internal static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => false,
            JTokenType.Boolean => (bool)t,
            JTokenType.Integer => (long)t != 0,
            JTokenType.Float => (double)t != 0 && !double.IsNaN((double)t),
            JTokenType.String => ((string)t).Length > 0,
            _ => true,
        };

        static ItemEntry EnsureItem(GameState g, string id, bool isChecked)
        {
            if (!g.Items.TryGetValue(id, out var e) || e == null) g.Items[id] = e = new ItemEntry { Checked = isChecked };
            return e;
        }

        // ------------------------------------------------------------------ home

        /// <summary>The apartment's look from the game: stock on the shelves, the bag by the door, the radio, the power.</summary>
        public static void ApplyHome(StoryContext ctx)
        {
            var g = ctx.G;
            var stock = new JObject();
            foreach (var kv in g.Items) stock[kv.Key] = kv.Value.Home;
            ctx.SetLevelState("apartment", "stock", stock);
            ctx.SetLevelState("apartment", "bag", g.Knows("bagPacked") ? "packed" : "empty");
            ctx.SetLevelState("apartment", "radio", Has(g, "radio") || Has(g, "radio", "bag") ? (g.Knows("radioOn") ? "on" : "off") : "none");
            ctx.SetLevelState("apartment", "power", ctx.Power ? "on" : "off");
        }

        /// <summary>Back home after an errand: the walk takes time, heavier with shopping.</summary>
        public static async Task GoHome(StoryContext ctx, double carryKg = 0)
        {
            var minutes = 6 + Num.JsRound(Math.Max(0, carryKg - 6) * 1.5);
            ctx.Pass(minutes);
            await ctx.Goto("apartment", "home");
        }

        /// <summary>Leave 302호 for the town. With power on the elevator takes you straight down.</summary>
        public static async Task GoOut(StoryContext ctx, string title, string body)
        {
            ctx.Objective(title, body, new List<ObjectiveStep>());
            if (ctx.Engine.LevelId == "town") return;
            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["homeExitDoor"] = H(async () =>
                    {
                        if (!ctx.Power) { await ctx.Goto("apartment", "corridor3F"); return null; }
                        var pick = await ctx.Ui.Talk(T("현관", "302호", new[] { "나가자." }, C(("lift", "엘리베이터로 1층까지"), ("hall", "복도로 나간다"))));
                        if (pick == "lift") { await ctx.Goto("town", "home"); return "out"; }
                        await ctx.Goto("apartment", "corridor3F");
                        return null;
                    }),
                    ["homeDoor"] = H(async () => { await ctx.Goto("apartment", "homeEntry"); return null; }),
                    ["elevator"] = H(async () =>
                    {
                        if (!ctx.Power) { ctx.Toast("엘리베이터가 멈춰 있다. 계단을 이용하자."); return null; }
                        await ctx.Goto("apartment", ctx.Engine.PlayerY > 1 ? "lobby" : "corridor3F");
                        return null;
                    }),
                },
                Tick = _ => ctx.Engine.LevelId == "town" ? "out" : null,
            });
        }

        /// <summary>
        /// What the bag UI lists: everything at home or already packed that can go in a bag (not the curtain).
        /// Ready = usable now (checked, or needs no check). BagDrill.BagItems also fills pocket/charge/expiry (bagInfo).
        /// </summary>
        public static List<BagItemView> BagItems(StoryContext ctx, bool withTips = false) =>
            BagDrill.BagItems(ctx.G, ctx.Rules.Db, id => ctx.Facts.Text(id), withTips);

        static JObject BagItemJson(BagItemView i)
        {
            var o = new JObject
            {
                ["id"] = i.Id, ["name"] = i.Name, ["weightKg"] = i.WeightKg, ["volumeL"] = i.VolumeL, ["home"] = i.Home, ["bag"] = i.Bag,
                ["tip"] = i.Tip ?? "", ["tags"] = new JArray(i.Tags), ["ready"] = i.Ready, ["pocket"] = i.Pocket,
            };
            if (i.Charge.HasValue) o["charge"] = i.Charge.Value;
            if (i.Expiry != null) o["expiry"] = i.Expiry;
            return o;
        }

        static JObject GoalJson(BagGoal g)
        {
            var o = new JObject { ["id"] = g.Id, ["label"] = g.Label, ["items"] = new JArray(g.Items), ["min"] = g.Min, ["facts"] = new JArray(g.Facts ?? Array.Empty<string>()) };
            if (g.Per != null) { var p = new JObject(); foreach (var kv in g.Per) p[kv.Key] = kv.Value; o["per"] = p; }
            return o;
        }

        // ------------------------------------------------------------------ mart

        static ShopSection Section(StoryContext ctx, string id) => ctx.Rules.Db.martSections.FirstOrDefault(s => s.id == id);

        static JArray ShopItems(StoryContext ctx, string section, List<string> soldOut)
        {
            var a = new JArray();
            foreach (var c in Section(ctx, section).items)
            {
                var note = c.needsCheck == ItemCheck.Batteries ? "건전지 별매" : c.needsCheck == ItemCheck.Charge ? "사용 전 충전 필요" : c.risk == ItemRisk.Fire ? "화기 주의"
                    : c.needs != null && c.needs.Contains("hot_water") ? "끓는 물 필요" : "";
                a.Add(new JObject
                {
                    ["id"] = c.id, ["name"] = c.displayName, ["price"] = c.price, ["unit"] = c.unit, ["weightKg"] = c.weightKg, ["volumeL"] = c.volumeL,
                    ["stock"] = soldOut.Contains(c.id) ? "out" : c.id == "water_2l" ? "low" : "ok", ["note"] = note, ["label"] = "",
                });
            }
            return a;
        }

        static List<string> SoldOutAt(StoryContext ctx, int slot)
        {
            var so = ctx.G.Cond?.SoldOut;
            var list = slot <= 0 || so == null ? new List<string>() : slot <= 2 ? (so.Early ?? new List<string>()) : (so.Late ?? so.Early ?? new List<string>());
            return list.SelectMany(x => Section(ctx, x) is { } s ? s.items.Select(i => i.id) : new[] { x }).ToList();
        }

        public static async Task DoMart(StoryContext ctx, int slot)
        {
            var g = ctx.G;
            var db = ctx.Rules.Db;
            var soldOut = SoldOutAt(ctx, slot);
            ctx.SetLevelState("mart", "crowd", "busy");
            ctx.SetLevelState("mart", "soldOut", new JArray(db.martSections.Where(s => s.items.All(i => soldOut.Contains(i.id))).Select(s => s.id)));
            await GoOut(ctx, "연무마트에 가기", "단지 정문으로 나가 연무로 건너편 연무마트로 갑니다.");
            ctx.Objective("장보기", "진열대에서 고르고 계산대에서 계산합니다. 필요한 만큼, 들고 올 수 있는 만큼.", Steps(("진열대 둘러보기", false), ("계산하기", false)));
            double carried = 0; bool boughtAny = false, hoard = false; int water = 0;
            var info = new JArray { new JObject { ["text"] = "생수 1인 6병 한정", ["label"] = "site" } };
            if (g.Rel.TryGetValue("clerk", out var clerk) && Truthy(clerk?.Contact))
                info.Add(new JObject { ["text"] = "수아: \"건전지는 오늘 오전에 더 들어온다고 했어요. 확실하진 않아요.\"", ["label"] = "neighbor" });

            Func<StoryPoint, Task<JToken>> ShopAt(string sec) => H(async () =>
            {
                var res = await ctx.Ui.Open("shop", new JObject
                {
                    ["title"] = Section(ctx, sec).label, ["money"] = g.Money, ["items"] = ShopItems(ctx, sec, soldOut), ["info"] = info.DeepClone(),
                    ["budgetNote"] = $"남은 돈 {Won(g.Money)}원",
                });
                if (res is not JObject r || !Truthy(r["total"])) return null;
                var cart = new List<KeyValuePair<string, int>>();
                if (r["cart"] is JObject cj) foreach (var p in cj.Properties()) cart.Add(new KeyValuePair<string, int>(p.Name, p.Value.Type is JTokenType.Integer or JTokenType.Float ? (int)(double)p.Value : 0));
                int wi = cart.FindIndex(kv => kv.Key == "water_2l");
                if (wi >= 0 && cart[wi].Value != 0)
                {
                    int already = g.Items.TryGetValue("water_2l", out var we) ? we.Bought : 0;
                    cart[wi] = new KeyValuePair<string, int>("water_2l", Math.Max(0, Math.Min(cart[wi].Value, 6 - already)));
                }
                cart = cart.Where(kv => kv.Value != 0 && !soldOut.Contains(kv.Key)).ToList();
                if (cart.Count == 0) { ctx.Toast("담을 수 있는 물건이 없습니다.", "site"); return null; }
                if (cart.Any(kv => kv.Value > 8)) hoard = true;
                var prices = cart.ToDictionary(kv => kv.Key, kv => db.Get(kv.Key)?.price ?? -1);
                try { ctx.Rules.Buy(g, cart, prices); }
                catch (Exception e) { ctx.Toast(string.IsNullOrEmpty(e.Message) ? "살 수 없습니다." : e.Message, "site"); return null; }
                boughtAny = true; water += cart.Where(kv => kv.Key == "water_2l").Sum(kv => kv.Value);
                carried += cart.Sum(kv => db.Get(kv.Key).weightKg * kv.Value);
                var cartJ = new JObject();
                foreach (var kv in cart) cartJ[kv.Key] = kv.Value;
                ctx.Event("bought", new JObject { ["cart"] = cartJ, ["slot"] = slot });
                return null;
            });

            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["shelfWater"] = ShopAt("water"), ["shelfFood"] = ShopAt("food"), ["shelfDaily"] = ShopAt("daily"), ["shelfElec"] = ShopAt("elec"), ["shelfHealth"] = ShopAt("health"),
                    ["atm"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(T("ATM", "연무마트 입구", new[] { $"잔액 {Won(g.Money)}원. 현금을 뽑을까?" },
                            new List<TalkChoice> { new("1", "5만 원", g.Money < 50000), new("2", "10만 원", g.Money < 100000), new("no", "그만") }));
                        if (pick == "1" || pick == "2")
                        {
                            int n = pick == "1" ? 1 : 2;
                            ctx.Rules.Buy(g, new[] { new KeyValuePair<string, int>("cash", n) }, new Dictionary<string, int> { ["cash"] = 50000 });
                            ctx.Event("cash", new JObject { ["n"] = n });
                            ctx.Toast($"현금 {n * 5}만 원을 찾았다.");
                        }
                        return null;
                    }),
                    ["checkout"] = H(async () =>
                    {
                        if (!boughtAny) { ctx.Toast("아직 고른 물건이 없습니다."); return null; }
                        if (hoard)
                        {
                            await ctx.Ui.Talk(T("계산원", "연무마트", new[] { "오늘 많이들 사 가셔서요… 다른 분들 몫도 남겨 주시면 감사하겠습니다." }));
                            ctx.Event("hoard", new JObject()); ctx.Toast(ctx.Facts.Text("home_nohoard"), "official", 6000);
                        }
                        if (water >= 4 && Has(g, "water_2l"))
                        {
                            var pick = await ctx.Ui.Talk(T("계산대", "연무마트", new[] { "뒤에 선 할머니 바구니에 생수가 없다." }, C(("share", "생수 한 병을 나눠 드린다"), ("no", "그냥 계산한다"))));
                            if (pick == "share") { ctx.Rules.Consume(g, "water_2l", 1); ctx.Remember("mart_share"); ctx.Toast("\"아이고, 고마워요. 오늘은 벌써 다 나갔다길래…\"", "neighbor"); }
                        }
                        await ctx.Ui.Talk(T("계산 완료", $"남은 돈 {Won(g.Money)}원", new[] { $"산 물건 무게 약 {carried.ToString("0.0", CultureInfo.InvariantCulture)}kg. {(carried > 12 ? "한 번에 들고 가기 버겁다." : "들고 갈 만하다.")}" }));
                        return "done";
                    }),
                    ["martExit"] = H(async () =>
                    {
                        if (boughtAny) { ctx.Toast("계산대에서 계산을 마치세요."); return null; }
                        var pick = await ctx.Ui.Talk(T("연무마트", "출입구", new[] { "아무것도 사지 않고 나갈까?" }, C(("yes", "나간다"), ("no", "더 둘러본다"))));
                        return pick == "yes" ? "done" : null;
                    }),
                },
            });
            if (carried > 12) ctx.Toast("무거워서 두 번에 나눠 옮겼다.");
            await GoHome(ctx, carried);
        }

        // ------------------------------------------------------------------ shelter check

        static readonly Dictionary<string, (string title, string text)> EntranceText = new()
        {
            ["shelterEast"] = ("동쪽 출입구 · 생활문화센터 정문", "유리문 안쪽 계단과 엘리베이터로 지하주차장에 내려간다. 평소 가장 많이 쓰는 문이다."),
            ["shelterA"] = ("출입구 A · 계단", "지하주차장으로 바로 내려가는 계단 출입구. 계단 양쪽에 손잡이가 있다."),
            ["shelterB"] = ("출입구 B · 지하주차장 진출입로", "차가 드나드는 경사로다. 경사가 가파르고, 평소에는 차가 다닌다."),
            ["shelterC"] = ("출입구 C · 경사로", "완만한 경사로와 양쪽 손잡이. 셔터가 내려져 있고 안내문이 붙어 있다: \"공사 중 · 10월 말 개방 예정. 비상시에는 현장 안내에 따라 열 수 있습니다.\""),
        };

        public static async Task DoShelterCheck(StoryContext ctx)
        {
            var g = ctx.G;
            await GoOut(ctx, "생활문화센터 대피소 확인하기", "연무로를 따라 서쪽, 연무 생활문화센터로 갑니다.");
            var seen = new List<string>(); bool board = false;
            List<ObjectiveStep> StepsNow() => Steps(($"출입구 살펴보기 ({seen.Count}/4)", seen.Count >= 3), ("현장 안내판 읽기", board));
            ctx.Objective("출입구와 안내판 확인", "각 출입구 앞에서 E로 살펴봅니다.", StepsNow());
            Func<StoryPoint, Task<JToken>> Look(string id) => H(async () =>
            {
                var (title, text) = EntranceText[id];
                await ctx.Ui.Talk(T(title, "현장 확인", new[] { text }));
                if (!seen.Contains(id)) { seen.Add(id); ctx.Note($"{title}: {text}", "site", "culture"); }
                ctx.Objective("출입구와 안내판 확인", "각 출입구 앞에서 E로 살펴봅니다.", StepsNow());
                return seen.Count >= 3 && board ? "done" : null;
            });
            await ctx.Roam(new RoamOptions
            {
                Doors = false,
                On =
                {
                    ["shelterEast"] = Look("shelterEast"), ["shelterA"] = Look("shelterA"), ["shelterB"] = Look("shelterB"), ["shelterC"] = Look("shelterC"),
                    ["shelterNotice"] = H(async () =>
                    {
                        await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "notice", Title = "대피소", Meta = "빨간 바탕의 대피소 표지판 · SHELTER",
                            Body = { "연무 생활문화센터 지하주차장 · 민방위 대피소", "출입구: 동쪽 정문, A 계단, B 지하주차장 진출입로 (C 경사로 공사 중)", ctx.Facts.Text("shelter_signs") },
                            Actions = new List<CardAction> { new("ok", "확인", true) },
                        });
                        board = true; ctx.Note("생활문화센터 대피소 출입구: 동쪽 정문, A 계단, B 진출입로 (C 공사 중)", "site", "culture");
                        ctx.Objective("출입구와 안내판 확인", "각 출입구 앞에서 E로 살펴봅니다.", StepsNow());
                        return seen.Count >= 3 ? "done" : null;
                    }),
                },
            });
            var options = Routes.RouteOptions(ctx.Rules, g, "home", "shelter", new string[0],
                new Dictionary<string, bool> { ["east"] = true, ["stairs"] = true, ["ramp"] = true, ["access"] = false }, "walk");
            var mode = Drills.LadderStage(g, "route");
            var res = await ctx.Modal(() => ctx.Ui.Open("routemap", new JObject
            {
                ["mode"] = mode, ["from"] = "home", ["to"] = "shelter", ["options"] = new JArray(options.Select(o => o.ToJObject())), ["closures"] = new JArray(),
                ["gates"] = new JObject { ["east"] = "open", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "closed" },
                ["known"] = new JObject { ["closures"] = new JArray() },
                ["playerAt"] = new JObject { ["x"] = ctx.Engine.PlayerX, ["z"] = ctx.Engine.PlayerZ },
            }));
            Drills.RecordEncounter(g, "route", "planned");
            g.SetKnow("shelterLocated", true); g.SetKnow("entrancesChecked", seen.Count >= 3); g.SetKnow("routeChecked", true); g.SetKnow("altRoute", options.Count > 1);
            int choice = res is JObject rj && rj["choice"] is JValue cv && cv.Type is JTokenType.Integer or JTokenType.Float ? (int)(double)cv : 0;
            g.SetKnow("plannedRoute", choice >= 0 && choice < options.Count ? new JArray(options[choice].Nodes) : JValue.CreateNull());
            GameRules.RecordEvent(g, "shelter_checked", new JObject { ["seen"] = new JArray(seen), ["board"] = board });
            ctx.Note("집 → 대피소 경로를 확인했다. 막히면 돌아갈 길도 지도에서 봐 두었다.", "self");
        }

        // ------------------------------------------------------------------ equipment

        public static async Task DoEquip(StoryContext ctx)
        {
            var g = ctx.G;
            bool radio = false, light = false, power = false, ext = false, gas = false, breaker = false;
            void Show() => ctx.Objective("집 장비 점검", "다 마치면 달력(계획판)에서 끝냅니다.", Steps(
                ("라디오 (거실 탁자)", radio), ("손전등 (베란다 수납장)", light), ("보조배터리 (책상)", power),
                ("소화기 위치 (3층 복도)", ext), ("가스 밸브 (주방)", gas), ("분전반 (현관)", breaker)));
            Show();
            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["homeRadio"] = H(async () =>
                    {
                        if (radio) { ctx.Toast("라디오는 점검을 마쳤다."); return null; }
                        if (!Owned(g, "radio"))
                        {
                            await ctx.Ui.Talk(T("거실 탁자", "라디오 자리", new[] { "라디오가 없다. 정전되면 방송을 들을 방법을 따로 찾아야 한다." }));
                            radio = true; ctx.Event("radio_missing", new JObject()); Show(); return null;
                        }
                        var ok = ctx.Rules.Check(g, "radio");
                        if (!ok.Ok)
                        {
                            await ctx.Ui.Talk(T("휴대용 라디오", "건전지식", new[] { "건전지가 없어 켜지지 않는다. 라디오는 있지만 지금은 쓸 수 없다." }));
                            ctx.Event("radio_blocked", new JObject { ["why"] = "batteries" }); return null;
                        }
                        var res = await ctx.Ui.Open("radio", new JObject
                        {
                            ["mode"] = "guided", ["start"] = 89.5, ["battery"] = true, ["extraSeconds"] = 0,
                            ["stations"] = new JArray
                            {
                                new JObject { ["freq"] = 95.1, ["name"] = "음악 방송", ["kind"] = "music", ["lines"] = new JArray("(잔잔한 음악이 흐른다)") },
                                new JObject { ["freq"] = RadioFreq, ["name"] = $"재난방송 주관방송사 라디오 (게임 속 주파수 {F(RadioFreq)}MHz)", ["kind"] = "official", ["lines"] = new JArray("지금은 평상시 프로그램입니다.", "비상시에는 이 방송에서 정부와 지자체 안내를 전합니다. (가상 방송)") },
                            },
                        });
                        bool official = res is JObject ro && Truthy(ro["official"]);
                        Drills.RecordEncounter(g, "radio", official ? "tuned" : "opened");
                        g.SetKnow("radioChecked", true); if (official) g.SetKnow("radioFreq", RadioFreq);
                        ctx.Note($"라디오 점검 완료. 재난방송 주파수 {F(RadioFreq)}MHz에 맞춰 두었다.", "self");
                        ctx.Toast(ctx.Facts.Text("radio_batt"), "official", 5000);
                        radio = true; Show(); return null;
                    }),
                    ["homeStock"] = H(async () =>
                    {
                        if (light) { ctx.Toast("손전등은 점검을 마쳤다."); return null; }
                        if (!ctx.Rules.Check(g, "flashlight").Ok)
                        {
                            await ctx.Ui.Talk(T("손전등", "베란다 수납장", new[] { "손전등은 있는데 건전지가 없다. 켜지지 않는다." }));
                            ctx.Event("light_blocked", new JObject()); return null;
                        }
                        light = true; ctx.Toast("손전등이 켜진다. 현관 신발장 위에 두었다."); Show(); return null;
                    }),
                    ["homeDesk"] = H(async () =>
                    {
                        await Task.CompletedTask;
                        if (power) { ctx.Toast("보조배터리는 충전해 두었다."); return null; }
                        if (!Owned(g, "powerbank")) { ctx.Toast("보조배터리가 없다. 휴대폰을 완충해 두는 수밖에 없다."); g.Phone.Battery = 100; power = true; Show(); return null; }
                        ctx.Rules.Check(g, "powerbank"); g.Phone.Battery = 100; power = true; ctx.Toast("보조배터리와 휴대폰을 완충했다."); Show(); return null;
                    }),
                    ["extinguisher"] = H(async () =>
                    {
                        if (ext) { ctx.Toast("소화기 위치를 확인했다."); return null; }
                        await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "notice", Title = "3층 복도 소화기", Meta = "옥내소화전 옆", Body = { ctx.Facts.Text("ext_1"), ctx.Facts.Text("ext_2"), ctx.Facts.Text("ext_3") },
                            Actions = new List<CardAction> { new("ok", "사용 순서 익혀 보기", true) },
                        });
                        await ctx.Ui.Open("extinguisher", new JObject { ["mode"] = "guided", ["fire"] = "small", ["practice"] = true });
                        Drills.RecordEncounter(g, "extinguisher", "practice");
                        g.SetKnow("extinguisherLocated", true); ext = true; Show(); return null;
                    }),
                    ["homeGas"] = H(async () =>
                    {
                        if (!g.Knows("gasValve")) ctx.Event("gas_checked", new JObject());
                        g.SetKnow("gasValve", true); gas = true;
                        await ctx.Ui.Talk(T("가스 중간 밸브", "주방", new[] { "레인지 옆 가스 중간 밸브 위치를 확인했다.", ctx.Facts.Text("alert_caution") }));
                        Show(); return null;
                    }),
                    ["homeBreaker"] = H(async () =>
                    {
                        g.SetKnow("breaker", true); breaker = true;
                        await ctx.Ui.Talk(T("분전반", "현관 위", new[] { "누전차단기와 개폐기가 있다.", ctx.Facts.Text("power_home") }));
                        Show(); return null;
                    }),
                    ["homePlanner"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(T("점검", "달력", new[] { "점검을 마칠까?" }, C(("yes", "마친다"), ("no", "조금 더 한다"))));
                        return pick == "yes" ? "done" : null;
                    }),
                },
            });
            ctx.Event("equip_done", new JObject { ["radio"] = radio, ["light"] = light, ["power"] = power, ["ext"] = ext, ["gas"] = gas, ["breaker"] = breaker });
        }

        // ------------------------------------------------------------------ documents

        public static async Task DoDocs(StoryContext ctx)
        {
            var g = ctx.G;
            ctx.Objective("서류·연락처 정리", "작은방 책상에서 비상연락카드를 씁니다.", new List<ObjectiveStep>());
            await ctx.Roam(new RoamOptions { On = { ["homeDesk"] = H(() => Task.FromResult<JToken>("go")) } });
            JObject Field(string id, string label, string value, string why) =>
                new JObject { ["id"] = id, ["label"] = label, ["value"] = value, ["why"] = why, ["recommended"] = true };
            var fields = new JArray
            {
                Field("me", "개인사항 (이름·주소·생년월일)", "정민 · 연무동 한빛아파트 103동 302호", ctx.Facts.Text("contact_fields")),
                Field("blood", "혈액형", "A형", "다쳤을 때 도움이 됩니다."),
                Field("meds", "복용약", "알레르기약 (1일 1회)", "약이 떨어지거나 잃어버렸을 때 필요합니다."),
                Field("family", "가족 연락처", "부모님(대구) 010-○○○○-○○○○", ctx.Facts.Text("contact_card")),
                Field("work", "직장 연락처", "한결물류 연무센터 운영팀", "집·직장 연락처를 함께 적습니다."),
                Field("neighbors", "이웃 연락처", "303호 오서연 · 관리사무소", "휴대폰이 꺼져도 연락할 사람을 찾을 수 있습니다."),
                Field("agencies", "신고·구호기관", "119 · 112 · 정전 123", ctx.Facts.Text("power_123")),
                Field("meet", "만남의 장소 1·2·3차", "① 생활문화센터 대피소 ② 행정복지센터 ③ 연무공원 정자", ctx.Facts.Text("contact_meet")),
                Field("idcopy", "신분증·통장·계약서 사본", "프린터로 사본 1부씩", ctx.Facts.Text("bag_copies")),
            };
            var res = await ctx.Modal(() => ctx.Ui.Open("docs", new JObject { ["mode"] = "guided", ["fields"] = fields, ["carry"] = new JArray("bag", "home") }));
            var card = res is JObject rj && rj["card"] is JObject cj && Truthy(cj) ? cj : new JObject();
            // Nothing ticked (or Esc): no card exists, so no bag goal, payoff or debrief credit.
            if (!card.Properties().Any()) { Drills.RecordEncounter(g, "docs", "skipped"); ctx.Toast("아무것도 적지 않았다."); return; }
            Drills.RecordEncounter(g, "docs", "written");
            var where = res is JObject r2 && (string)(r2["where"] as JValue) == "bag" ? "bag" : "home";
            var cc = EnsureItem(g, "contact_card", true);
            if (where == "bag") cc.Bag += 1; else cc.Home += 1;
            g.SetKnow("contactsCard", true);
            g.SetKnow("cardFields", new JArray(card.Properties().Select(p => p.Name)));
            if (Truthy(card["idcopy"]))
            {
                var dc = EnsureItem(g, "doc_copies", true);
                if (where == "bag") dc.Bag += 1; else dc.Home += 1;
                g.SetKnow("docsCopied", true);
            }
            if (Truthy(card["meet"])) g.SetKnow("meetPoint", "생활문화센터 대피소");
            ctx.Event("docs_done", new JObject { ["fields"] = new JArray(card.Properties().Select(p => p.Name)), ["where"] = where });
            ctx.Toast(where == "bag" ? "비상연락카드를 배낭 앞주머니에 넣었다." : "비상연락카드를 책상 서랍에 넣었다.");
        }

        // ------------------------------------------------------------------ neighbours

        public static async Task DoNeighbors(StoryContext ctx)
        {
            bool seoyeon = false, manager = false, clerk = false;
            bool elder = false; // optional: not part of met, so it never holds up finishing
            void Show() => ctx.Objective("이웃과 비상 연락 정하기", "세 사람을 만나 이야기합니다. 다 만나면 집으로 돌아옵니다.",
                Steps(("303호 서연", seoyeon), ("관리사무소 박 소장 (1층)", manager), ("바로편의점 수아", clerk), ("304호 어르신 (선택)", elder)));
            Show();
            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["homeExitDoor"] = H(async () => { await ctx.Goto("apartment", "corridor3F"); return null; }),
                    ["homeDoor"] = H(async () => { await ctx.Goto("apartment", "homeEntry"); return seoyeon && manager && clerk ? "done" : null; }),
                    ["elevator"] = H(async () => { await ctx.Goto("apartment", ctx.Engine.PlayerY > 1 ? "lobby" : "corridor3F"); return null; }),
                    ["seoyeonDoor"] = H(async () => { if (seoyeon) { ctx.Toast("\"내일 봬요.\"", "neighbor"); return null; } await TalkSeoyeon(ctx); seoyeon = true; Show(); return null; }),
                    ["elderDoor"] = H(async () =>
                    {
                        if (elder) { ctx.Toast("문 너머로 라디오 소리가 작게 들린다.", "site"); return null; }
                        await ctx.Ui.Talk(T("304호 어르신", "304호", new[] { "누구신가? …아, 옆옆집 302호 총각이구먼. 이사 오고 인사는 처음이네.", "나야 혼자 사니까 별일 없으면 그냥 집에 있어요. 귀가 좀 어두워서 초인종은 잘 못 들어. 문을 두드려 줘요." }));
                        elder = true; ctx.Remember("elder_met"); Show(); return null;
                    }),
                    ["managerWindow"] = H(async () => { if (manager) { ctx.Toast("박 소장은 전화 통화 중이다."); return null; } await TalkManager(ctx); manager = true; Show(); return null; }),
                    ["storeCounter"] = H(async () => { if (clerk) { ctx.Toast("\"또 오세요.\"", "neighbor"); return null; } await TalkClerk(ctx); clerk = true; Show(); return null; }),
                    ["homePlanner"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(T("이웃", "달력", new[] { "이 정도로 마칠까?" }, C(("yes", "마친다"), ("no", "더 만난다"))));
                        return pick == "yes" ? "done" : null;
                    }),
                },
            });
            ctx.Event("neighbors_done", new JObject { ["seoyeon"] = seoyeon, ["manager"] = manager, ["clerk"] = clerk });
        }

        static Relation Rel(GameState g, string who)
        {
            if (!g.Rel.TryGetValue(who, out var r) || r == null) g.Rel[who] = r = new Relation();
            return r;
        }

        static async Task TalkSeoyeon(StoryContext ctx)
        {
            var g = ctx.G;
            ctx.SetLevelState("apartment", "seoyeonDoor", "open");
            await ctx.Ui.Talk(T("오서연", "303호", new[] { "아, 302호 정민 씨죠? 오서연이에요.", "저는 보행 보조기를 써요. 이 카드에 제 상황을 적어 뒀어요." }));
            var body = SeoyeonCard.ToList(); body.Add(ctx.Facts.Text("people_card"));
            await ctx.Ui.Card(new CardOptions { Kind = "info", Title = "서연의 재난 대비 카드", Meta = "서연이 직접 쓴 카드", Body = body, Actions = new List<CardAction> { new("ok", "읽었다", true) } });
            var pick = await ctx.Ui.Talk(T("오서연", "303호", new[] { "비상시에 서로 어떻게 연락할까요? 문자가 제일 편해요.", "혹시 대피소 출입구 상황을 알게 되면, 확인한 것과 못 한 것을 나눠서 보내 줄래요? 저한테 맞는 입구는 제가 다시 볼게요." },
                C(("sms", "문자로 연락해요"), ("both", "문자도 하고, 문에 쪽지도 남길게요"))));
            var rel = Rel(g, "seoyeon");
            rel.Contact = pick == "both" ? "both" : "sms";
            if (pick == "both") ctx.Remember("promise_note");
            rel.Trust += 1;
            if (!Has(g, "batteries_aa"))
            {
                var b = await ctx.Ui.Talk(T("오서연", "303호", new[] { "건전지 있으세요? 저 두 팩 사 놨는데 한 팩 나눠 드릴까요?" }, C(("yes", "고마워요, 받을게요"), ("no", "괜찮아요"))));
                if (b == "yes")
                {
                    EnsureItem(g, "batteries_aa", false).Home += 1;
                    ctx.Event("gift", new JObject { ["from"] = "seoyeon", ["item"] = "batteries_aa" });
                }
            }
            await ctx.Ui.Talk(T("오서연", "303호", new[] { "금요일 저녁엔 병원 진료가 있어서 좀 늦게 들어와요. 무슨 일 있으면 문자 주세요." }));
            ctx.Note("서연(303호): 문자로 연락. 출입구 상황은 확인한 것과 못 한 것을 나눠서.", "neighbor", "apartment");
            ctx.SetLevelState("apartment", "seoyeonDoor", "closed");
        }

        static async Task TalkManager(StoryContext ctx)
        {
            var g = ctx.G;
            await ctx.Ui.Talk(T("박영수 소장", "관리사무소", new[] { "아이고, 302호. 오늘 민원이 밀려서 길게는 못 해요.", "정전되면 엘리베이터는 못 써요. 계단 비상등은 배터리로 켜지고요.", "우리 단지 대피소는 길 건너 생활문화센터 지하예요. 정문이 동쪽 문이고, 계단 A, 차 다니는 진출입로 B. C 경사로는 아직 공사 중이에요.", "소화기는 층마다 복도 소화전 옆에 있어요." }));
            var rel = Rel(g, "manager");
            rel.Contact = "office"; rel.Trust += 1;
            g.SetKnow("entrancesHeard", true);
            ctx.Note("박 소장: 대피소 출입구는 동쪽 정문·A 계단·B 진출입로 (C 공사 중). 정전 시 엘리베이터 사용 불가.", "neighbor", "apartment");
        }

        static async Task TalkClerk(StoryContext ctx)
        {
            var g = ctx.G;
            await ctx.Ui.Talk(T("이수아", "바로편의점", new[] { "어서 오세요. 건전지요? 오늘 거의 다 나갔어요. 내일 오전에 더 들어온다고는 했는데, 확실하진 않아요.", "생수는 마트가 더 싸요. 1인 6병 한정이래요.", "정전되면 카드 단말기가 안 될 수도 있대요. 현금 조금 있으면 좋을 거예요. 저도 들은 얘기지만요." }));
            var pick = await ctx.Ui.Talk(T("이수아", "바로편의점", new[] { "무슨 일 있으면 가게로 오세요. 근데 저도 언제까지 일할지는 몰라요. 본가가 지방이라서요." },
                C(("watch", "가게 비우시면 셔터라도 봐 드릴게요"), ("bye", "조심히 다녀오세요"))));
            if (pick == "watch") { ctx.Remember("promise_store"); await ctx.Ui.Talk(T("이수아", "바로편의점", new[] { "정말요? 그럼 마음이 좀 놓이네요. 고마워요." })); }
            var rel = Rel(g, "clerk");
            rel.Contact = "store"; rel.Trust += 1;
            ctx.Note("수아: 건전지 내일 오전 입고 예정(확실하지 않음). 정전 시 현금만 받을 수도 있음.", "neighbor", "store");
        }

        // ------------------------------------------------------------------ home readiness

        /// <summary>Water in big pots before the taps stop (home_tub). Shared by '집 안 대비' and the D-1 evening. Talks through ctx.Ui (call inside a modal or roam handler).</summary>
        public static async Task<bool> FillWater(StoryContext ctx)
        {
            var g = ctx.G;
            if (g.Knows("tubFilled")) { ctx.Toast("큰 통과 냄비에 물을 받아 두었다."); return false; }
            var pick = await ctx.Ui.Talk(T("주방", "단수 대비", new[] { "단수에 대비해 큰 통과 냄비에 물을 받아 둘까? 씻거나 변기 물을 내리는 데 쓸 수 있다." }, C(("yes", "받아 둔다"), ("no", "그만둔다"))));
            if (pick != "yes") return false;
            g.SetKnow("tubFilled", true); GameRules.RecordEvent(g, "tub_filled", new JObject()); ctx.Toast(ctx.FactLine("home_tub"), "official", 6000);
            return true;
        }

        /// <summary>
        /// 집 안 대비 (design M4a): the blackout curtain on the living-room glass with its edges taped (night_lights),
        /// water put by early (home_tub), and the dates on the stored food read (bag_nocook). Tape seals the curtain
        /// edges against light only; the game never teaches taping windows as protection.
        /// </summary>
        public static async Task DoHome(StoryContext ctx)
        {
            var g = ctx.G;
            bool window = false, water = g.Knows("tubFilled"), stock = false;
            bool Curtain() => Owned(g, "blackout_curtain");
            void Show() => ctx.Objective("집 안 대비", "집 안을 돌며 하나씩 해 둡니다. 다 마치면 달력(계획판)에서 끝냅니다.", Steps(
                (Curtain() ? "거실 창: 암막 커튼 달기" : "거실 창: 불빛이 새는지 살피기", window), ("주방: 큰 통·냄비에 물 받아 두기", water), ("베란다 수납장: 비상식량 기한 확인", stock)));
            JToken Finished() => window && water && stock ? "done" : null;
            Show();
            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["homeWindow"] = H(async () =>
                    {
                        if (window) { ctx.Toast(g.Knows("curtain") ? "암막 커튼을 달아 두었다." : "창은 살펴보았다."); return null; }
                        window = true;
                        if (!Curtain())
                        {
                            await ctx.Ui.Talk(T("거실 창", "집 안 대비", new[] { "지금 커튼은 얇아서 밤에 불을 켜면 밖에서 다 비친다.", "연무마트 생활용품 코너에 암막 커튼이 있었다.", ctx.Facts.Text("night_lights") }));
                            Show(); return Finished();
                        }
                        ctx.Rules.Use(g, "blackout_curtain");
                        g.SetKnow("curtain", true);
                        ctx.SetLevelState("apartment", "curtain", "hung");
                        bool sealedEdges = false;
                        if (Owned(g, "tape"))
                        {
                            var pick = await ctx.Ui.Talk(T("거실 창", "암막 커튼", new[] { "암막 커튼을 거실 창에 달았다. 가장자리 틈으로 불빛이 조금 샐 것 같다." },
                                C(("tape", "청테이프로 커튼 가장자리를 창틀에 붙인다"), ("no", "이대로 둔다"))));
                            if (pick == "tape") { ctx.Rules.Use(g, "tape"); sealedEdges = true; g.SetKnow("curtainSealed", true); ctx.SetLevelState("apartment", "curtain", "sealed"); }
                        }
                        else await ctx.Ui.Talk(T("거실 창", "암막 커튼", new[] { "암막 커튼을 거실 창에 달았다. 가장자리 틈으로 불빛이 조금 샌다. 테이프가 있으면 막을 수 있겠다." }));
                        GameRules.RecordEvent(g, "home_ready", new JObject { ["curtain"] = true, ["sealed"] = sealedEdges });
                        ctx.Toast(ctx.Facts.Text("night_lights"), "official", 6000);
                        Show(); return Finished();
                    }),
                    ["homeSink"] = H(async () =>
                    {
                        if (water) { ctx.Toast("큰 통과 냄비에 물을 받아 두었다."); return null; }
                        if (await FillWater(ctx)) { water = true; Show(); return Finished(); }
                        return null;
                    }),
                    ["homeStock"] = H(async () =>
                    {
                        if (stock) { ctx.Toast("비상식량 기한은 확인했다."); return null; }
                        stock = true; g.SetKnow("expiryChecked", true);
                        int old = g.Items.TryGetValue("old_bars", out var ob) ? ob.Home + ob.Bag : 0;
                        GameRules.RecordEvent(g, "expiry_checked", new JObject { ["old"] = old });
                        if (old == 0)
                        {
                            await ctx.Ui.Talk(T("베란다 수납장", "기한 확인", new[] { "통조림과 즉석밥의 날짜를 하나씩 확인했다. 기한이 지난 것은 없다.", ctx.Facts.Text("bag_nocook") }));
                            Show(); return Finished();
                        }
                        var pick = await ctx.Ui.Talk(T("베란다 수납장", "기한 확인", new[] { $"안쪽에서 작년 비상가방에 넣어 두었던 건빵 {old}봉이 나왔다. 날짜를 보니 기한이 한참 지났다.", ctx.Facts.Text("bag_nocook") },
                            C(("toss", "기한 지난 건빵은 버린다"), ("keep", "일단 둔다"))));
                        if (pick == "toss")
                        {
                            foreach (var where in new[] { "home", "bag" }) { int n = where == "home" ? ob.Home : ob.Bag; if (n > 0) ctx.Rules.Consume(g, "old_bars", n, where); }
                            GameRules.RecordEvent(g, "old_bars_tossed", new JObject { ["n"] = old });
                            ctx.Toast("기한 지난 건빵을 정리했다. 다음에 장을 보면 새것으로 채워 두자.", "self");
                        }
                        Show(); return Finished();
                    }),
                    ["homePlanner"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(T("집 안 대비", "달력", new[] { "이 정도로 마칠까?" }, C(("yes", "마친다"), ("no", "조금 더 한다"))));
                        return pick == "yes" ? "done" : null;
                    }),
                },
            });
            ctx.Event("home_done", new JObject { ["window"] = window, ["water"] = water, ["stock"] = stock, ["curtain"] = g.Knows("curtain"), ["sealed"] = g.Knows("curtainSealed") });
        }

        // ------------------------------------------------------------------ bag

        public static async Task DoBag(StoryContext ctx)
        {
            var g = ctx.G;
            var db = ctx.Rules.Db;
            ctx.Objective("비상가방 꾸리기", "현관의 배낭에 들고 나갈 물건을 담습니다.", new List<ObjectiveStep>());
            await ctx.Roam(new RoamOptions { On = { ["homeBag"] = H(() => Task.FromResult<JToken>("go")) } });
            var mode = Drills.LadderStage(g, "bag");
            var items = new JArray(BagItems(ctx, true).Select(BagItemJson));
            var goals = new JArray(Drills.BagGoals["alert"].Select(GoalJson));
            var res = await ctx.Modal(() => ctx.Ui.Open("bag", new JObject
            {
                ["mode"] = mode, ["context"] = "alert", ["limit"] = new JObject { ["weightKg"] = db.bagLimitKg, ["volumeL"] = db.bagLimitL },
                ["items"] = items, ["goals"] = goals, ["tips"] = new JArray(Tips(ctx, "bag_days", "bag_nocook", "bag_copies", "bag_foil", "bag_door")),
            }));
            var r = res as JObject;
            ApplyBag(ctx, r?["bag"] as JObject ?? new JObject());
            ApplyPocket(ctx, r?["pocket"] as JObject);
            var ev = Drills.EvalBag(ctx.Rules, g, "alert");
            Drills.RecordEncounter(g, "bag", ev.Missing.Count > 0 ? "partial" : "complete");
            g.SetKnow("bagPacked", g.Items.Values.Any(e => e.Bag > 0));
            ctx.SetLevelState("apartment", "bag", g.Knows("bagPacked") ? "packed" : "empty");
            ctx.Event("bag_packed", new JObject { ["weightKg"] = ev.WeightKg, ["missing"] = new JArray(ev.Missing) });
            ctx.Toast(ev.Missing.Count > 0
                ? $"배낭을 현관에 두었다. 빠진 것: {string.Join(", ", ev.Missing.Select(id => Drills.BagGoals["alert"].FirstOrDefault(x => x.Id == id)?.Label ?? id))}"
                : "배낭을 현관에 두었다. 필요한 것을 고루 담았다.");
        }

        /// <summary>The bag screen's { id: count } into the game (packs or unpacks each item to that count).</summary>
        public static void ApplyBag(StoryContext ctx, JObject target)
        {
            var t = new Dictionary<string, int>();
            if (target != null)
                foreach (var p in target.Properties())
                    if (p.Value.Type is JTokenType.Integer or JTokenType.Float) t[p.Name] = (int)Math.Floor((double)p.Value);
            BagDrill.ApplyBag(ctx.Rules, ctx.G, t);
        }

        /// <summary>
        /// ui/bag.js applyPocket: the bag UI's { pocket } back into the game after ApplyBag set the counts: empty the pocket,
        /// then fill it, so the 0.5 L check never trips over what was there before. Anything that no longer fits stays in
        /// the main part.
        /// </summary>
        public static void ApplyPocket(StoryContext ctx, JObject pocket)
        {
            if (pocket == null) return;
            var g = ctx.G;
            foreach (var kv in g.Items.ToList()) if ((kv.Value.Pocket ?? 0) > 0) ctx.Rules.SetPocket(g, kv.Key, 0);
            foreach (var p in pocket.Properties())
            {
                if (!g.Items.TryGetValue(p.Name, out var e) || e == null || ctx.Rules.Db.Get(p.Name)?.pocket != true) continue;
                if (p.Value.Type is not (JTokenType.Integer or JTokenType.Float) || !((double)p.Value > 0)) continue;
                try { ctx.Rules.SetPocket(g, p.Name, Math.Min((int)Math.Floor((double)p.Value), e.Bag)); }
                catch (InvalidOperationException) { /* over 0.5 L: stays in the main part */ }
            }
        }
    }
}
