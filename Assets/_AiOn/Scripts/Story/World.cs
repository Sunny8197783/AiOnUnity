// Port of story/world.js. Things the player can use or look at in any chapter, when the chapter itself has no
// handler for the point: ATMs, vending machines, the bus stop, the wallet with the ID card, and a short description
// for everything else that has one (point.Desc on the level, or Desc below). StoryContext.Roam calls this after a
// chapter's '*' handler has declined the point.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class World
    {
        static bool Has(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e.Home + e.Bag > 0;

        static Func<StoryContext, IEnumerable<string>> Lines(params string[] lines) => _ => lines;

        /// <summary>level:point -> lines for the context (DESC). Chapters that handle a point override these.</summary>
        public static readonly IReadOnlyDictionary<string, Func<StoryContext, IEnumerable<string>>> Desc = new Dictionary<string, Func<StoryContext, IEnumerable<string>>>
        {
            ["apartment:homeSink"] = c => c.G.Day >= 1 ? new[] { "수도꼭지를 돌려도 물이 나오지 않는다. 단수다." } : new[] { "설거지가 조금 남아 있다. 물은 잘 나온다." },
            ["apartment:homeWindow"] = c => c.G.Knows("curtain")
                ? new[] { c.G.Knows("curtainSealed") ? "암막 커튼 가장자리를 테이프로 막아 두었다. 틈으로 새는 불빛이 없다." : "암막 커튼이 쳐져 있다. 가장자리 틈으로 빛이 조금 샌다." }
                : c.Power ? new[] { "건너편 동 창문마다 불이 켜져 있다. 평소와 같은 저녁 풍경." } : new[] { "건너편 동이 캄캄하다. 창문 몇 곳에서 손전등 불빛이 흔들린다." },
            ["apartment:homeFridge"] = c => c.Power ? new[] { "반찬 통 몇 개와 남은 고기. 냉장고는 잘 돌아간다." } : new[] { "냉장고가 조용하다. 문을 자주 열면 안의 냉기가 빨리 빠진다.", c.Facts.Text("food_door") },
            ["apartment:homeGas"] = Lines("레인지 옆 가스 중간밸브. 손잡이가 배관과 나란하면 열림, 직각이면 잠김이다."),
            ["apartment:homeBreaker"] = Lines("현관 위 분전반. 누전차단기와 개폐기가 줄지어 있다."),
            ["apartment:homeDesk"] = c => c.Power ? new[] { "노트북과 스탠드. 회사 메일은 확인할 게 없다." } : new[] { "노트북 화면이 꺼져 있다. 배터리를 아끼려면 켜지 않는 게 낫다." },
            ["apartment:homeBed"] = Lines("아직 잘 시간은 아니다."),
            ["apartment:homeStock"] = Lines("베란다 수납장. 생수와 비상용품을 넣어 두는 곳이다."),
            ["apartment:homePlanner"] = Lines("이번 주 계획판. 관리비 25일까지, 분리수거 화·금."),
            ["apartment:homeRadio"] = c => Has(c.G, "radio") ? new[] { "거실 탁자 위 휴대용 라디오." } : new[] { "라디오가 없다. 휴대폰 FM 라디오 앱은 쓸 수 있다 (P)." },
            ["apartment:homeBag"] = Lines("현관 옆 배낭. 대피할 때 들고 나갈 것을 담아 두는 곳이다."),
            ["apartment:lobbyNotice"] = Lines("단지 게시판: 관리비 안내, 분리수거 요일, 승강기 정기 점검 일정."),
            ["apartment:managerWindow"] = Lines("관리사무소 창구. 박 소장이 전화를 받고 있다."),
            ["apartment:elevator"] = c => c.Power ? new[] { "엘리베이터. 정전되면 멈추니 비상시에는 계단을 쓴다." } : new[] { "엘리베이터가 멈춰 있다. 버튼에 불이 들어오지 않는다.", c.Facts.Text("alert_stairs") },
            ["apartment:stairs1F"] = Lines("계단실. 층마다 비상조명등이 있다."),
            ["apartment:stairs3F"] = Lines("계단실. 4층 쪽은 물청소 중이라 막혀 있다."),
            ["apartment:extinguisher"] = Lines("복도 옥내소화전 옆 분말소화기. 안전핀 → 노즐 → 손잡이 → 쓸듯이."),
            ["apartment:seoyeonDoor"] = Lines("303호. 오서연 씨 집이다."),
            ["apartment:elderDoor"] = Lines("304호. 문 옆에 신발장이 있다."),
            ["apartment:homeDoor"] = Lines("302호, 우리 집."),
            // point.desc on the levels (apartment-core.js, store.js, shelter.js): the exported layouts do not carry them.
            ["apartment:stairsWet"] = Lines("위층으로 가는 계단에 출입 금지 줄이 쳐 있다. \"계단 물청소 중 · 미끄럼 주의\".", "젖은 계단은 미끄럽다. 오늘은 위층으로 올라가지 않는다."),
            ["store:storeCoffee"] = c => new[] { !c.Power ? "전원이 꺼져 있다. 버튼을 눌러도 아무 반응이 없다." : "원두커피 셀프 머신. 컵은 계산대에서 받는다." },
            ["store:storeSteamer"] = c => new[] { !c.Power ? "찜기가 식어 있다. 안의 찐빵도 차갑다." : "김이 서린 유리 너머로 찐빵이 따뜻하게 데워지고 있다." },
            ["store:storeRack"] = Lines("AA·AAA 건전지와 휴대폰 충전 케이블이 걸려 있다.", "라디오와 손전등은 건전지가 들어 있어야 쓸 수 있다."),
            ["store:storeRamen"] = c => new[] { !c.Power ? "온수기가 꺼져 있다. 정전에는 컵라면도 끓는 물이 없으면 못 먹는다." : "온수기, 전자레인지, 분리수거함. 컵라면 국물은 여기 버린다." },
            ["store:storeIce"] = c => new[] { !c.Power ? "냉동고가 멈췄다. 뚜껑 안쪽에 물방울이 맺혀 있다. 열지 않는 게 낫겠다." : "하드와 콘이 가득하다. 가을인데도 잘 팔린다고 한다." },
            ["store:storeWindowBar"] = Lines("창밖으로 단지 앞 도로가 보인다. 플라스틱 의자가 몇 개 놓여 있다."),
            ["shelter:fromStairs"] = Lines("출입구 A 계단. 지상으로 나가는 문은 계단을 올라간 계단참에 있다."),
            ["shelter:fromRamp"] = Lines("출입구 B 차량 진출입로. 경사로를 올라가면 차단기 너머가 도로다."),
            ["shelter:fromAccess"] = Lines("출입구 C 경사로. 계단 없이 오르내릴 수 있는 완만한 길이다."),
            ["town:center"] = Lines("연무동 행정복지센터. 민원실 유리문 안쪽으로 번호표 기계가 보인다."),
            ["town:cafe"] = c => c.Power ? new[] { "카페 쉼표. 커피 냄새가 난다." } : new[] { "카페 쉼표. 문이 잠겨 있고 \"정전으로 임시 휴업\" 종이가 붙어 있다." },
            ["town:aptNotice"] = Lines("한빛아파트 단지 게시판."),
            ["town:centerNotice"] = Lines("행정복지센터 게시판. 민방위 대피소 안내도가 붙어 있다."),
            ["town:shelterNotice"] = Lines("연무 생활문화센터 지하주차장 · 민방위 대피소 안내판."),
        };

        /// <summary>Shows or hides a point of the current level (no allocation; the host syncs Enabled to the scene). False when absent.</summary>
        public static bool SetPointEnabled(IStoryEngine engine, string id, bool on)
        {
            var pts = engine?.Points;
            if (pts == null) return false;
            for (int i = 0; i < pts.Count; i++)
                if (pts[i].Id == id) { pts[i].Enabled = on; return true; }
            return false;
        }

        public static bool IsCommon(StoryContext ctx, StoryPoint point)
        {
            var lv = ctx.Engine.LevelId;
            return point.Kind == "atm" || point.Kind == "vending" || point.Kind == "busStop" || point.Id == "homeWallet"
                || !string.IsNullOrEmpty(point.Desc) || Desc.ContainsKey($"{lv}:{point.Id}");
        }

        /// <summary>Talks through ctx.Ui directly: call it inside ctx.Modal (StoryContext.Roam does).</summary>
        public static async Task Common(StoryContext ctx, StoryPoint point)
        {
            var lv = ctx.Engine.LevelId;
            if (point.Kind == "atm") { await Atm(ctx); return; }
            if (point.Kind == "vending") { await Vending(ctx); return; }
            if (point.Kind == "busStop") { await BusStop(ctx); return; }
            if (point.Id == "homeWallet") { Wallet(ctx); return; }
            // point.Desc: the level's own description; several lines are separated by '\n'.
            IEnumerable<string> d = !string.IsNullOrEmpty(point.Desc) ? point.Desc.Split('\n')
                : Desc.TryGetValue($"{lv}:{point.Id}", out var f) ? f(ctx) : null;
            var lines = (d ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            if (lines.Count > 0) await ctx.Ui.Talk(new TalkOptions { Speaker = point.Label, Role = "살펴보기", Lines = lines });
        }

        static async Task Atm(StoryContext ctx)
        {
            var g = ctx.G;
            if (!ctx.Power)
            {
                await ctx.Ui.Talk(new TalkOptions { Speaker = "ATM", Role = "화면 꺼짐", Lines = { "화면이 까맣다. 정전으로 자동화기기가 멈췄다.", "전기가 끊기면 카드도 ATM도 쓸 수 없다. 현금은 미리 찾아 둬야 한다." } });
                return;
            }
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "ATM", Role = "현금 인출",
                Lines = { "카드를 넣고 비밀번호를 눌렀다.", $"계좌 잔액 {g.Money.ToString("N0", CultureInfo.InvariantCulture)}원. 얼마를 찾을까?" },
                Choices = new List<TalkChoice>
                {
                    new("1", "5만 원", g.Money < 50000), new("2", "10만 원", g.Money < 100000), new("3", "15만 원", g.Money < 150000), new("no", "취소"),
                },
            });
            // Number(pick): 'no' / null -> NaN / 0 -> nothing.
            if (!int.TryParse(pick, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0) return;
            ctx.Rules.Buy(g, new[] { new KeyValuePair<string, int>("cash", n) }, new Dictionary<string, int> { ["cash"] = 50000 });
            ctx.Event("cash", new JObject { ["n"] = n });
            ctx.Toast($"현금 {n * 5}만 원을 찾아 지갑에 넣었다.");
        }

        static async Task Vending(StoryContext ctx)
        {
            var g = ctx.G;
            if (!ctx.Power)
            {
                await ctx.Ui.Talk(new TalkOptions { Speaker = "음료 자판기", Role = "전원 꺼짐", Lines = { "불이 꺼진 자판기 안이 어둡다. 버튼을 눌러도, 동전을 넣어도 반응이 없다." } });
                return;
            }
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "음료 자판기", Role = "카드·현금", Lines = { "생수 500mL 1,000원." },
                Choices = new List<TalkChoice> { new("buy", "생수 한 병 산다", g.Money < 1000), new("no", "그만둔다") },
            });
            if (pick != "buy") return;
            ctx.Rules.Buy(g, new[] { new KeyValuePair<string, int>("water_500", 1) }, new Dictionary<string, int> { ["water_500"] = 1000 });
            ctx.Toast("생수 한 병을 샀다.");
        }

        static Task BusStop(StoryContext ctx)
        {
            var lines = ctx.Power
                ? new List<string> { "도착 안내 전광판: \"마을 3 곧 도착 · 720 6분\"", "오늘은 버스를 탈 일이 없다." }
                : new List<string> { "도착 안내 전광판이 꺼져 있다. 기둥에 종이가 붙어 있다.", "\"정전·교통 통제로 시내버스 운행을 중단합니다. 주민 이동 지원 차량은 행정복지센터 앞에서 출발합니다. (가상 안내)\"" };
            return ctx.Ui.Talk(new TalkOptions { Speaker = "버스 정류장", Role = "연무동 행정복지센터 방면", Lines = lines });
        }

        static void Wallet(StoryContext ctx)
        {
            var g = ctx.G;
            if (!g.Items.TryGetValue("id_card", out var e)) g.Items["id_card"] = e = new ItemEntry { Home = 0, Bag = 0, Bought = 0, Checked = true, Used = 0 };
            if (e.Home + e.Bag > 0) { ctx.Toast("신분증은 이미 챙겼다."); return; }
            e.Bag = 1;
            g.SetKnow("idTaken", true);
            ctx.SetLevelState("apartment", "wallet", "taken");
            GameRules.RecordEvent(g, "id_taken", new JObject());
            ctx.Toast($"지갑과 신분증을 챙겼다. {ctx.Facts.Text("docs_take") ?? ""}", "self", 5000);
        }
    }
}
