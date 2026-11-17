// Port of story/prep.js: chapter 1 준비 · D-2 수요일 18:30 ~ D-1 목요일 밤. Six time slots, more things to do than fit.
// The planner screen is ctx.Ui.Open("planner", { day, slots: { total, used }, actions: [...], hint }) and returns the
// picked action id (a JSON string) or null (closed).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>One entry of the planner (prep.js ACTIONS).</summary>
    public sealed class PrepAction
    {
        public string Id, Label, Icon, Desc;
        public int Cost;
        public PrepAction(string id, string label, int cost, string icon, string desc) { Id = id; Label = label; Cost = cost; Icon = icon; Desc = desc; }
    }

    public static class Prep
    {
        /// <summary>Slot start times: (day, minute). Slot 0 is tonight; slots 1-5 are tomorrow.</summary>
        public static readonly IReadOnlyList<(int day, int minute)> SlotAt = new[]
        {
            (-2, 18 * 60 + 30), (-1, 9 * 60), (-1, 11 * 60), (-1, 13 * 60 + 30), (-1, 15 * 60 + 30), (-1, 17 * 60 + 30), (-1, 19 * 60 + 30),
        };
        public static readonly IReadOnlyList<string> SlotLabel = new[] { "D-2 저녁", "D-1 오전", "D-1 오전", "D-1 오후", "D-1 오후", "D-1 저녁" };

        public static readonly IReadOnlyList<PrepAction> Actions = new[]
        {
            new PrepAction("mart", "연무마트 장보기", 2, "🛒", "물, 식량, 건전지, 라디오 같은 물품을 산다. 사람이 몰려 시간이 걸린다."),
            new PrepAction("shelter", "대피소·출입구 확인", 1, "🧭", "지정 대피소까지 직접 걸어가 출입구와 경로를 확인한다."),
            new PrepAction("equip", "집 장비 점검", 1, "🔦", "라디오·손전등에 건전지를 넣어 보고, 보조배터리를 충전하고, 소화기와 가스 밸브 위치를 확인한다."),
            new PrepAction("docs", "서류·연락처 정리", 1, "🗂", "비상연락카드를 쓰고 중요 서류 사본을 만든다."),
            new PrepAction("neighbors", "이웃과 비상 연락 정하기", 1, "🤝", "303호 서연, 관리사무소 박 소장, 편의점 수아와 이야기한다."),
            new PrepAction("bag", "비상가방 꾸리기", 1, "🎒", "집에 있는 물품 가운데 들고 나갈 것을 배낭에 담아 현관에 둔다."),
            new PrepAction("home", "집 안 대비", 1, "🏠", "창에 암막 커튼을 달고(사 두었다면) 가장자리를 테이프로 막는다. 물을 미리 받아 두고, 비상식량 기한을 확인한다."),
            new PrepAction("rest", "휴식", 1, "🛏", "쉬면서 피로와 긴장을 덜어 낸다."),
        };

        static Func<StoryPoint, Task<JToken>> H(Func<Task<JToken>> f) => _ => f();
        static (int day, int minute) Slot(int i) => SlotAt[Math.Min(i, SlotAt.Count - 1)];

        static void SetClockToSlot(GameState g, int i)
        {
            var (d, m) = Slot(i);
            if (d * 1440 + m > g.Day * 1440 + g.Minute) { g.Day = d; g.Minute = m; }
        }

        static string EnvForSlot(int i) => i == 0 ? "night" : i >= 5 ? "dusk" : "day";

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            g.Chapter = "prep";
            ctx.IndoorEnv = "interiorNight"; ctx.Power = true; ctx.OutdoorEnv = "night";
            ctx.Hud.SetChapter("1장", "준비 — 남은 시간 여섯 칸");
            ctx.Objective(null);
            PrepActions.ApplyHome(ctx);
            if (!g.Knows("toured")) // C0: where the shop, the mart, the 행정복지센터 and the shelter are, before the first plan
            {
                try { await Cutscenes.TownTour(ctx); }
                catch (Exception) { /* the original logs '[cine] town tour failed; the story goes on' */ }
                g.SetKnow("toured", true);
            }
            await ctx.Goto("apartment", "home");
            ctx.Refresh();
            await ctx.Card(new CardOptions
            {
                Kind = "info", Title = "103동 302호", Meta = "D-2 수요일 18:30",
                Body = { "오늘 저녁 한 칸, 내일 다섯 칸. 모든 것을 다 할 수는 없다.", "거실 벽의 달력에서 할 일을 고르자. 물건을 많이 사는 것보다, 실제로 쓸 수 있게 해 두는 것이 중요하다." },
                Actions = new List<CardAction> { new("ok", "달력 보기", true) },
            });

            bool opened = false;
            while (g.Slots.Used < g.Slots.Total)
            {
                int slot = g.Slots.Used;
                SetClockToSlot(g, slot);
                ctx.OutdoorEnv = EnvForSlot(slot);
                ctx.IndoorEnv = slot == 0 ? "interiorNight" : "interior";
                ctx.Engine.SetEnv(ctx.IndoorEnv, 0.5);
                ctx.SetLevelState("apartment", "windowView", slot == 0 ? "night" : slot >= 5 ? "dusk" : "day");
                ctx.Refresh();
                ctx.Objective($"{SlotLabel[slot]} · 무엇을 할까", "거실 벽의 달력(계획판)에서 고릅니다. 집 안을 둘러볼 수도 있습니다.",
                    new List<ObjectiveStep> { new($"남은 칸 {g.Slots.Total - g.Slots.Used}", false) });
                JToken pickTok;
                if (opened)
                {
                    var on = HomeFree(ctx);
                    on["homePlanner"] = H(() => OpenPlanner(ctx, slot));
                    pickTok = await ctx.Roam(new RoamOptions { Doors = false, On = on });
                }
                else pickTok = await ctx.Modal(() => OpenPlanner(ctx, slot));
                opened = true;
                var pick = pickTok is JValue pv && pv.Type == JTokenType.String ? (string)pv : null;
                if (string.IsNullOrEmpty(pick)) continue;
                var action = Actions.First(a => a.Id == pick);
                GameRules.SpendSlot(g, action.Id, action.Cost);
                ctx.Event("prep_action", new JObject { ["id"] = action.Id, ["slot"] = slot });
                await RunAction(ctx, action, slot);
                if (slot == 0 && g.Slots.Used < g.Slots.Total)
                {
                    await ctx.Skip("D-2 수 밤", "D-1 목 09:00", new[] { "밤새 뉴스 속보 알림이 몇 번 울렸다. 정민은 잠을 설쳤다." });
                    GameRules.Adjust(g, "fatigue", 6); GameRules.Adjust(g, "tension", 6);
                }
            }
            await EveningBeforeAlert(ctx);
        }

        /// <summary>The planner's action rows as the screen gets them (done, disabled and why).</summary>
        public static JArray PlannerActions(GameState g, int slot)
        {
            int left = g.Slots.Total - g.Slots.Used;
            var done = new HashSet<string>(g.Slots.Done);
            var a = new JArray();
            foreach (var x in Actions)
            {
                bool did = done.Contains(x.Id) && x.Id != "rest";
                a.Add(new JObject
                {
                    ["id"] = x.Id, ["label"] = x.Label, ["cost"] = x.Cost, ["icon"] = x.Icon, ["desc"] = x.Desc,
                    ["done"] = did,
                    ["disabled"] = x.Cost > left || did || (slot == 0 && x.Cost > 1),
                    ["reason"] = slot == 0 && x.Cost > 1 ? "마트는 곧 문을 닫습니다. 내일 하세요." : x.Cost > left ? $"남은 칸이 {left}칸이라 할 수 없습니다." : done.Contains(x.Id) ? "이미 했습니다." : "",
                });
            }
            return a;
        }

        static async Task<JToken> OpenPlanner(StoryContext ctx, int slot)
        {
            var g = ctx.G;
            var hint = slot == 0 ? "오늘 저녁은 한 칸뿐입니다. 마트는 두 칸이 드는 데다 곧 문을 닫습니다."
                : !ctx.Rules.Usable(g, "flashlight") && !(g.Items.TryGetValue("batteries_aa", out var b) && b.Home > 0) ? "손전등은 있지만 건전지가 없습니다. 있다는 것과 쓸 수 있다는 것은 다릅니다." : "";
            var res = await ctx.Ui.Open("planner", new JObject
            {
                ["day"] = SlotLabel[slot], ["slots"] = new JObject { ["total"] = g.Slots.Total, ["used"] = g.Slots.Used }, ["actions"] = PlannerActions(g, slot), ["hint"] = hint,
            });
            // The planner resolves an action id or null (closed); null ends a roam too, like the original.
            return res is JValue v && v.Type == JTokenType.String ? v : JValue.CreateNull();
        }

        static async Task RunAction(StoryContext ctx, PrepAction action, int slot)
        {
            var g = ctx.G;
            var end = Slot(slot + action.Cost);
            switch (action.Id)
            {
                case "mart": await PrepActions.DoMart(ctx, slot); break;
                case "shelter": await PrepActions.DoShelterCheck(ctx); break;
                case "equip": await PrepActions.DoEquip(ctx); break;
                case "docs": await PrepActions.DoDocs(ctx); break;
                case "neighbors": await PrepActions.DoNeighbors(ctx); break;
                case "bag": await PrepActions.DoBag(ctx); break;
                case "home": await PrepActions.DoHome(ctx); break;
                case "rest":
                    await ctx.Card(new CardOptions { Kind = "info", Title = "휴식", Meta = SlotLabel[slot], Body = { "휴대폰을 내려놓고 잠깐 눈을 붙였다. 몸이 조금 가벼워졌다." }, Actions = new List<CardAction> { new("ok", "계속", true) } });
                    GameRules.Adjust(g, "fatigue", -25); GameRules.Adjust(g, "tension", -12);
                    break;
            }
            long t = Math.Max((long)g.Day * 1440 + g.Minute, (long)end.day * 1440 + end.minute); // absolute minutes, so a day change never rolls back
            g.Day = (int)Num.FloorDiv(t, 1440); g.Minute = (int)(t - (long)g.Day * 1440);
            if (ctx.Engine.LevelId != "apartment") await PrepActions.GoHome(ctx);
            PrepActions.ApplyHome(ctx);
        }

        /// <summary>Things the player can do at home without spending a slot.</summary>
        static Dictionary<string, Func<StoryPoint, Task<JToken>>> HomeFree(StoryContext ctx)
        {
            var g = ctx.G;
            return new Dictionary<string, Func<StoryPoint, Task<JToken>>>
            {
                ["homeWindow"] = H(async () =>
                {
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "창밖", Role = "연무동", Lines = { ctx.OutdoorEnv == "day" ? "평소처럼 버스가 다닌다. 마트 주차장 쪽에 차가 많다." : "건너편 동의 불빛이 하나둘 켜진다. 평소와 다를 것 없어 보인다." } });
                    return null;
                }),
                ["homeFridge"] = H(async () =>
                {
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "냉장고", Role = "주방", Lines = { "반찬 통 네 개, 어제 남은 익힌 고기 한 통. 전기가 끊기면 오래 버티지 못하는 것들이다." } });
                    return null;
                }),
                ["homeStock"] = H(async () => { await StoryCommon.StockLook(ctx); return null; }),
                ["homeBed"] = H(() => { ctx.Toast("아직 잘 시간이 아니다."); return Task.FromResult<JToken>(null); }),
                ["homeDesk"] = H(async () =>
                {
                    if (g.Knows("shelterLocated")) { ctx.Toast("지도앱에 생활문화센터 대피소를 저장해 두었다."); return null; }
                    var pick = await ctx.Ui.Talk(new TalkOptions
                    {
                        Speaker = "휴대폰", Role = "지도앱", Lines = { "지도앱에서 \"민방위 대피소\"를 검색해 볼까?" },
                        Choices = new List<TalkChoice> { new("yes", "검색한다"), new("no", "나중에") },
                    });
                    if (pick != "yes") return null;
                    g.SetKnow("shelterLocated", true);
                    ctx.Event("shelter_located", new JObject { ["via"] = "map_app" });
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "지도앱", Role = "검색 결과", Lines = { "가장 가까운 민방위 대피소: 연무 생활문화센터 지하주차장 (도보 약 4분).", "이름과 주소만 나온다. 출입구가 어디인지, 지금 열려 있는지는 지도만으로 알 수 없다." } });
                    ctx.Note("가장 가까운 대피소: 연무 생활문화센터 지하주차장", "official");
                    return null;
                }),
            };
        }

        /// <summary>D-1 evening: news, the bathtub, then sleep. Leaves the clock at D-day 19:30.</summary>
        static async Task EveningBeforeAlert(StoryContext ctx)
        {
            var g = ctx.G;
            g.Day = -1; g.Minute = 20 * 60;
            ctx.OutdoorEnv = "night"; ctx.IndoorEnv = "interiorNight";
            ctx.Engine.SetEnv("interiorNight", 1);
            ctx.SetLevelState("apartment", "windowView", "night");
            if (ctx.Engine.LevelId != "apartment") await PrepActions.GoHome(ctx);
            ctx.Refresh();
            await ctx.Card(new CardOptions
            {
                Kind = "news", Title = "저녁 뉴스 (가상)", Meta = "D-1 목 20:00",
                Body = { "정부는 국민들에게 비상시 국민행동요령을 미리 확인해 달라고 당부했습니다.", "일부 지역에서 생수와 건전지를 대량으로 사 가는 일이 이어지자, 사재기를 자제하고 필요한 만큼만 구입해 달라고 요청했습니다." },
                Actions = new List<CardAction> { new("ok", "TV를 끈다", true) },
            });
            void Show() => ctx.Objective("잠들기 전에", "할 수 있는 일이 남아 있다면 해 두고, 침대에서 잠듭니다.",
                new List<ObjectiveStep> { new("주방에서 큰 통·냄비에 물 받아 두기 — 선택", g.Knows("tubFilled")), new("침대에서 잠들기", false) });
            Show();
            var on = HomeFree(ctx);
            on["homeSink"] = H(async () => { if (await PrepActions.FillWater(ctx)) Show(); return null; });
            on["homeBed"] = H(async () =>
            {
                // A quiet night at home: the album on the shelf, once, before sleep.
                if (g.Items.TryGetValue("photo_album", out var album) && album.Home > 0 && !g.Knows("albumSeen"))
                {
                    var pick = await ctx.Ui.Talk(new TalkOptions
                    {
                        Speaker = "침대", Role = "잠들기 전에", Lines = { "책장에 가족사진 앨범이 꽂혀 있다." },
                        Choices = new List<TalkChoice> { new("look", "앨범을 넘겨 보고 잔다"), new("sleep", "그냥 잔다") },
                    });
                    if (pick == "look") await StoryCommon.LookAlbum(ctx);
                }
                return "sleep";
            });
            await ctx.Roam(new RoamOptions { Doors = false, On = on });
            await ctx.Skip("D-1 목 밤", "D-day 금 19:30", new[] { "다음 날, 회사는 오후 4시에 문을 닫았다. 정민은 일찍 집에 들어와 저녁을 먹었다." });
            g.Day = 0; g.Minute = 19 * 60 + 30;
            GameRules.Adjust(g, "fatigue", -10);
        }
    }
}
