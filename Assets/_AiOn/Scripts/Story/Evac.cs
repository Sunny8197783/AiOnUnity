// Port of story/evac.js: 4장 남을까 떠날까 (D+6) and 5장 공식 대피 (D+12), then the ending. Both choices in 4장 are
// valid under different conditions; the fire in 4장 is the third encounter, where stepping back is the success.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Evac
    {
        static readonly string[] Water = { "water_2l", "water_500" }, Food = { "canned", "rice_instant", "bars", "ramen" };

        static int Count(GameState g, string id, bool bag) => g.Items.TryGetValue(id, out var e) && e != null ? (bag ? e.Bag : e.Home) : 0;
        static int StockOf(GameState g, IEnumerable<string> ids) => ids.Sum(id => Count(g, id, false) + Count(g, id, true));
        static int HomeOf(GameState g, IEnumerable<string> ids) => ids.Sum(id => Count(g, id, false));

        /// <summary>Takes up to n units out of home, in list order.</summary>
        static void MoveOut(StoryContext ctx, IEnumerable<string> ids, int n)
        {
            foreach (var id in ids)
            {
                int k = Math.Min(n, Count(ctx.G, id, false));
                if (k > 0) { ctx.Rules.Consume(ctx.G, id, k, "home"); n -= k; }
            }
        }

        /// <summary>String(number) as a JS template literal prints it.</summary>
        static string N(double x) =>
            Math.Floor(x) == x && Math.Abs(x) < 1e15 ? ((long)x).ToString(CultureInfo.InvariantCulture) : x.ToString("R", CultureInfo.InvariantCulture);

        static string KnowString(GameState g, string key) =>
            g.Know.TryGetValue(key, out var v) && v != null && v.Type == JTokenType.String ? (string)v : null;

        static List<TalkChoice> Choices(params (string id, string label)[] cs) => cs.Select(c => new TalkChoice(c.id, c.label)).ToList();

        static JObject ChapterCardOptions(StoryContext ctx, string id, string chapter, string title)
        {
            var g = ctx.G;
            var sum = Debrief.ChapterSummary(ctx.Rules, g, id);
            return new JObject
            {
                ["chapter"] = chapter, ["title"] = title,
                ["lines"] = new JArray(sum.Lines), ["payoffs"] = JArray.FromObject(sum.Payoffs), ["ripples"] = JArray.FromObject(Ripples.Of(g)),
            };
        }

        // ================================================================== 4장 남을까, 떠날까

        public static async Task Crossroads(StoryContext ctx)
        {
            var G = ctx.G;
            G.Chapter = "crossroads";
            ctx.Hud.SetChapter("4장", "남을까, 떠날까");
            ctx.Power = false; ctx.OutdoorEnv = "day";
            if (ctx.Resume != "fire4")
            {
                int days = Math.Max(1, 6 - G.Day);
                var ledger = await ctx.Modal(() => StoryCommon.DayLedger(ctx, days, "home"));
                await ctx.Skip("D+3 월", "D+6 목 10:00", new[] { "하루 한두 시간씩 전기가 들어왔다 나갔다. 급수소 줄은 날마다 길어졌다." }.Concat(ledger.Take(4)).ToList());
                G.Day = 6; G.Minute = 10 * 60;
                PrepActions.ApplyHome(ctx);
                await ctx.Goto("apartment", "home", "interiorDark");
                ctx.Refresh();
                var how = await CampFood(ctx);
                if (how == "radio") await NoticeCard(ctx, how);
                else if (how == null)
                {
                    var memo = Rumors.Posts(G, "camp_food").First(p => p.Key == "seoyeon");
                    await ctx.Say(memo.From, new[] { "현관문 틈에 서연이 끼워 둔 메모가 있다.", memo.Text, "희망하는 주민은 생활문화센터 임시 거처로 옮길 수 있다고 한다. 권고이고, 명령은 아니라고 했다." }, null, memo.Meta);
                }
                var reply = await ctx.Card(new CardOptions
                {
                    Kind = "notice", Title = "303호 문 앞 메모판", Meta = "D+6 아침 · 서연의 손글씨",
                    Body = { "라디오(아침 7시): 생활문화센터 임시 거처, 희망하면 옮길 수 있음 (권고)", "전기 복구 시점: 아직 미정", "급수소: ?" },
                    Actions = new List<CardAction> { new("add", "급수 시간과 들은 곳을 덧붙여 적는다", true), new("read", "읽기만 한다") },
                });
                if (reply == "add") { ctx.Pass(3); ctx.Remember("seoyeon_note_reply"); }
                var yoon = await ctx.Say("윤 팀장", new[] { "연무센터가 구호물자 분류 거점이 됐어. 손 보탤 수 있으면 연락 줘." },
                    Choices(("yes", "가능한 날 나가겠습니다"), ("no", "지금은 어렵습니다")), "문자");
                if (yoon == "yes") ctx.Remember("yoon_reply");
                double water = Endings.WaterLitres(G);
                int food = StockOf(G, new[] { "rice_instant", "canned", "bars", "ramen" });
                string heardLine = Rumors.Misled(G) == "camp_food" ? "마음에 걸리는 말: \"임시 거처 급식이 끊겼다\" (SNS 퍼온 글 · 확인하지 않음)"
                    : G.Events.Any(e => e.Type == "rumor_checked" && (string)e.Details?["id"] == "camp_food") ? "확인한 것: 임시 거처는 급수·급식을 지원한다 (공식 안내)" : null;
                var stayMove = await ctx.Card(new CardOptions
                {
                    Kind = "info", Title = "지금 내 상황", Meta = "판단 재료",
                    Body = new[]
                    {
                        $"마실 물 약 {N(water)}L · 먹을 것 {food}개", $"휴대폰 {N(G.Phone.Battery)}% · 몸 상태: 피로 {N(G.Stats.Fatigue)} · 긴장 {N(G.Stats.Tension)}", heardLine,
                        "박 소장: \"저는 남아서 건물 봐야죠.\" · 서연: \"저는 주민센터에 이동 지원을 신청해 뒀어요.\"",
                        G.Knows("clerkLeft") ? "바로편의점 문에 쪽지: \"본가에 다녀옵니다 — 이수아\"" : "바로편의점은 문을 닫았다.",
                        "어느 쪽도 정답이 정해져 있지 않습니다. 지금의 조건으로 고르세요.",
                    }.Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    Actions = new List<CardAction> { new("stay", "집에 남는다"), new("move", "생활문화센터 임시 거처로 옮긴다") },
                });
                G.SetKnow("stayMove", stayMove);
                GameRules.RecordEvent(G, "decision", new JObject { ["stayMove"] = stayMove, ["water"] = water, ["food"] = food });
                ctx.Checkpoint("fire4");
            }
            if (KnowString(ctx.G, "stayMove") == "move") await MoveAndFire(ctx); else await StayAndSmoke(ctx);
            await ctx.Modal(() => ctx.Ui.Open("chapterCard", ChapterCardOptions(ctx, "crossroads", "4장", "남을까, 떠날까")));
        }

        /// <summary>
        /// "임시 거처 급식 끊김" (Rumors camp_food) arrives before the stay/move choice. Returns how it was checked:
        /// "radio" | "board" | null (acted on the judgement as it was).
        /// </summary>
        static async Task<string> CampFood(StoryContext ctx)
        {
            var heard = await ctx.Modal(() => RumorScene.HearClaim(ctx, "camp_food", new[] { "휴대폰이 짧게 울린다. 동네 단톡방에 퍼온 글이 올라왔다." }));
            if (heard.Act != "check") return null;
            if (await ctx.Modal(() => StoryCommon.RadioScene(ctx, "crossroads")) != null) { Rumors.Verify(ctx.G, "camp_food"); return "radio"; }
            var go = await ctx.Say("정민", new[] { "라디오로는 확인하지 못했다. 아파트 앞 단지 게시판까지 나가 볼까? 정전이라 계단으로 내려가야 한다." },
                Choices(("board", "게시판까지 다녀온다"), ("no", "그만두고 집에 있는다")), "한 번 더 확인");
            if (go != "board") return null;
            await RumorScene.BoardWalk(ctx, "게시판에서 확인하기", "아파트 앞 단지 게시판이나 행정복지센터 게시판에서 공식 안내를 읽습니다. 엘리베이터는 멈춰 있어 계단으로 내려갑니다.",
                () => ctx.Ui.Card(Notice("board")));
            Rumors.Verify(ctx.G, "camp_food");
            ctx.Pass(15);
            await PrepActions.GoHome(ctx);
            ctx.Refresh();
            return "board";
        }

        /// <summary>The official notice, read on the radio or at a board.</summary>
        public static CardOptions Notice(string how) => new()
        {
            Kind = "notice", Title = "연무구 안내 (가상)", Meta = "D+6 · 행정복지센터 게시판·라디오",
            Body =
            {
                "희망하는 주민은 생활문화센터 임시 거처로 옮길 수 있습니다. (권고, 명령 아님)", "임시 거처: 급수·급식 지원, 공용 공간, 개인 공간 협소", "집에 머무는 주민도 비상급수시설을 계속 이용할 수 있습니다.",
                how == "radio" ? "(라디오에서 같은 내용을 들었다.)" : "(단지 게시판에서 직접 읽었다.)",
            },
            Actions = new List<CardAction> { new("ok", "지금 상황 보기", true) },
        };

        static Task<string> NoticeCard(StoryContext ctx, string how) => ctx.Card(Notice(how));

        /// <summary>Stay: smoke from another flat. The fact-based answer is to judge, not to run or to fight.</summary>
        static async Task StayAndSmoke(StoryContext ctx)
        {
            var G = ctx.G;
            await ctx.Skip("D+6 목", "D+7 금 밤", new[] { "집에 남기로 했다. 하루가 더 지났다. 물을 아껴 쓰며 라디오 안내를 기다렸다." });
            G.Day = 7; G.Minute = 22 * 60 + 10; ctx.OutdoorEnv = "blackout";
            ctx.SetLevelState("apartment", "windowView", "blackout");
            ctx.SetLevelState("apartment", "smoke", 0.35);
            ctx.SetLevelState("apartment", "haze", 0); // the smoke is in the corridor; none came into 302호 (the scene's premise)
            await ctx.Goto("apartment", "home", "interiorDark");
            ctx.Refresh();
            await ctx.Caption("밤 10시. 위층 어딘가에서 화재경보가 울리고, 복도 쪽에서 연기 냄새가 난다.", null, 3500);
            bool felt = false;
            for (;;)
            {
                var pick = await ctx.Say("판단", new[] { "우리 집 안으로 연기나 불길이 들어오지는 않았다. 어떻게 할까?" }, new List<TalkChoice>
                {
                    new("handle", "현관 문손잡이를 손등으로 만져 본다", felt),
                    new("fight", "복도로 나가 소화기로 끄러 간다"),
                    new("run", "바로 복도로 뛰어나가 계단으로 내려간다"),
                    new("stay", "창문을 닫고 119에 신고한 뒤 안내방송에 따른다"),
                }, "22:10");
                GameRules.RecordEvent(G, "smoke_choice", new JObject { ["pick"] = pick });
                if (pick == "handle")
                {
                    felt = true;
                    await ctx.Say("현관 문손잡이", new[] { "뜨겁다. 문 너머 복도에 연기가 차 있는 것 같다.", ctx.Facts.Text("fire_door") }, null, "손등으로");
                    continue;
                }
                if (pick == "fight") { Drills.RecordEncounter(G, "extinguisher", "escalated"); ctx.Fail("fire", new JObject { ["scene"] = "upstairs" }); }
                if (pick == "run")
                {
                    GameRules.Adjust(G, "health", -15); Drills.RecordEncounter(G, "extinguisher", "risky");
                    await StoryCommon.FactCard(ctx, "연기 속으로 나가기 전에", new[] { "fire_judge", "fire_door", "fire_stay" });
                    break;
                }
                Drills.RecordEncounter(G, "extinguisher", "retreat-ok");
                await StoryCommon.FactCard(ctx, "상황을 보고 판단했습니다", new[] { "fire_stay", "fire_judge", "fire_119" });
                ctx.Payoff("judgement", "불을 끄러 가지도, 연기 속으로 뛰어들지도 않고 상황을 보고 기다렸다");
                break;
            }
            ctx.SetLevelState("apartment", "smoke", 0);
            ctx.SetLevelState("apartment", "haze", "follow");
            await ctx.Say("박영수 소장", new[] { "5층 세대 주방에서 난 불이었대요. 소방대가 껐고 다친 사람은 없대요." }, null, "방송");
            var rounds = await ctx.Say("박영수 소장", new[] { "302호, 아직 안 잤으면… 빈 집 문단속 같이 돌아요? 떠난 집이 많아서요." },
                Choices(("go", "같이 돈다"), ("rest", "오늘은 쉬겠습니다")), "복도");
            if (rounds == "go") { ctx.Stat("fatigue", 8); ctx.Pass(40); ctx.Remember("rounds_with_manager"); }
            else await ctx.Say("박영수 소장", new[] { "그래요, 푹 쉬어요. 놀랐을 텐데." }, null, "복도");
        }

        /// <summary>Move: temporary stay in the shelter; someone cooks with a burner and the boxes catch.</summary>
        static async Task MoveAndFire(StoryContext ctx)
        {
            var G = ctx.G;
            await ctx.Skip("D+6 목", "D+7 금 저녁", new[] { "짐을 꾸려 생활문화센터 임시 거처로 옮겼다. 하루가 지났다." });
            G.Day = 7; G.Minute = 19 * 60 + 30;
            ctx.SetLevelState("shelter", "crowd", "settled");
            ctx.SetLevelState("shelter", "phase", "calm");
            ctx.SetLevelState("shelter", "power", "emergency");
            ctx.SetLevelState("shelter", "fire", "large");
            await ctx.Goto("shelter", "spot");
            // face the burning corner
            if (ctx.Engine.TryGetSpawn("fire", out var fx, out _, out var fz, out _))
            {
                var e = ctx.Engine;
                e.TeleportTo(e.PlayerX, e.PlayerY, e.PlayerZ, Math.Atan2(-(fx - e.PlayerX), -(fz - e.PlayerZ)));
            }
            ctx.Refresh();
            if (Rumors.Misled(G) == "camp_food") // the claim met what is really here
            {
                G.SetKnow("misled", null); GameRules.RecordEvent(G, "rumor_corrected", new JObject { ["id"] = "camp_food" });
                await ctx.Caption("점심과 저녁에 배식 줄이 섰다. 급식이 끊겼다던 SNS 글과는 달랐다.", null, 3800);
            }
            await ctx.Caption("저녁. 주차장 구석에서 누군가 휴대용 버너로 라면을 끓이다 쌓아 둔 상자에 불이 옮겨 붙었다. 불길이 천장 배관에 닿는다.", null, 4200);
            await StoryCommon.FactCard(ctx, "대피소에서는", new[] { "shelter_rules" });
            var res = await ctx.Modal(() => ctx.Ui.Open("extinguisher", new JObject { ["mode"] = Drills.LadderStage(G, "extinguisher"), ["fire"] = "large" }));
            var seq = (res as JObject)?["sequence"] is JArray a ? a.Select(x => (string)x).ToList() : new List<string>();
            var ev = Drills.EvalExtinguisher(seq, "large");
            Drills.RecordEncounter(G, "extinguisher", ev.Result);
            ctx.SetLevelState("shelter", "fire", "none");
            if (ev.Result == "escalated") ctx.Fail("fire", new JObject { ["scene"] = "shelter" });
            await StoryCommon.FactCard(ctx, "물러서는 것도 성공입니다", new[] { "fire_big", "fire_shout", "fire_119" });
            ctx.Payoff("judgement", "천장까지 닿은 불 앞에서 물러나 알리고 대피했다");
            var report = await ctx.Say("대피소 관리자", new[] { "소방대가 왔어요. 처음 본 분 계세요? 어떻게 된 건지 말씀해 주세요." },
                Choices(("precise", "\"19시 반쯤 주차장 구석, 버너에서 상자로 옮겨 붙었어요. 다친 사람은 못 봤어요.\""), ("vague", "\"저쪽에서 불났어요, 큰일 날 뻔했어요!\"")), "대피소");
            if (report == "precise") ctx.Remember("fire_report");
            await ctx.Say("대피소 관리자", new[] { report == "precise" ? "시간이랑 자리를 정확히 말해 주시니 확인이 빠르네요. 고맙습니다." : "네, 다들 놀라셨죠. 소방대가 확인하고 있어요." }, null, "대피소");
            var sort = await ctx.Say("대피소 관리자", new[] { "참, 물류 일 하셨다고요? 구호품 상자 정리 좀 도와주실 수 있어요? 어디에 뭐가 있는지 엉망이라서요." },
                Choices(("help", "돕는다"), ("rest", "오늘은 쉬겠습니다")), "대피소");
            if (sort == "help") { ctx.Stat("fatigue", 8); ctx.Pass(40); ctx.Remember("sort_supplies"); }
        }

        // ================================================================== 5장 공식 대피

        public static async Task Evacuation(StoryContext ctx)
        {
            var G = ctx.G;
            G.Chapter = "evac";
            ctx.Hud.SetChapter("5장", "공식 대피");
            ctx.OutdoorEnv = "day";
            if (ctx.Resume != "depart")
            {
                var place = KnowString(G, "stayMove") == "move" ? "shelter" : "home";
                var ledger = await ctx.Modal(() => StoryCommon.DayLedger(ctx, Math.Max(1, 12 - G.Day), place));
                await ctx.Skip("D+7", "D+12 수", new[] { place == "shelter" ? "닷새가 더 지났다. 임시 거처에서 지내다 오늘 아침 짐을 챙기러 집에 들렀다." : "닷새가 더 지났다. 이웃 몇 집은 이미 떠났다." }.Concat(ledger.Take(4)).ToList());
                G.Day = 12; G.Minute = G.Cond.EvacNoticeAt;
                PrepActions.ApplyHome(ctx);
                await ctx.Goto("apartment", "home", "interiorDark");
                ctx.Refresh();
                await StoryCommon.EmergencyText(ctx, $"[안전안내문자] 연무동 주민 이동 지원. 오늘 14:00까지 행정복지센터 앞 임시 집결지로 이동. 1인 1가방, 신분증 지참 {StoryCommon.Area}",
                    GameRules.Hhmm(G.Minute), "안전안내문자");
                await StoryCommon.FactCard(ctx, "대피 안내를 받았을 때", new[] { "evac_order", "evac_kit", "evac_note" });
                var heard = await ctx.Modal(() => StoryCommon.RadioScene(ctx, "evac"));
                G.SetKnow("evacClosuresKnown", heard != null);
                // One bag, third encounter: no tips unless the ladder says so.
                var db = ctx.Rules.Db;
                var items = BagDrill.BagItems(G, db, id => ctx.Facts.Text(id), withTips: false);
                var mode = Drills.LadderStage(G, "bag");
                var res = await ctx.Modal(() => ctx.Ui.Open("bag", new JObject
                {
                    ["mode"] = mode, ["context"] = "evac", ["limit"] = new JObject { ["weightKg"] = db.bagLimitKg, ["volumeL"] = db.bagLimitL },
                    ["items"] = new JArray(items.Select(BagItemJson)), ["goals"] = new JArray(Drills.BagGoals["evac"].Select(GoalJson)), ["tips"] = new JArray(),
                }));
                BagDrill.ApplyBag(ctx.Rules, G, BagTarget((res as JObject)?["bag"]));
                ApplyPocket(ctx.Rules, G, (res as JObject)?["pocket"]);
                var ev = Drills.EvalBag(ctx.Rules, G, "evac");
                Drills.RecordEncounter(G, "bag", ev.Missing.Count > 0 ? "partial" : "complete");
                G.SetKnow("bagOn", true);
                if (G.Knows("bagPacked") && ev.Missing.Count == 0) ctx.Payoff("bag", "미리 꾸려 본 경험으로 한 가방을 빠르게 챙겼다");
                if (G.Minute < 13 * 60 + 15) await ctx.Skip($"D+12 수 {GameRules.Hhmm(G.Minute)}", "D+12 수 13:15", new[] { "짐을 싸고 집을 정리하다 보니 오후가 되었다." });
                G.Minute = Math.Max(G.Minute, 13 * 60 + 15);
                ctx.Checkpoint("depart");
            }
            await Depart(ctx);
        }

        /// <summary>One item of the bag screen, as the original's bagItems + bagInfo object.</summary>
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
            var o = new JObject { ["id"] = g.Id, ["label"] = g.Label, ["items"] = new JArray(g.Items ?? Array.Empty<string>()), ["min"] = g.Min };
            if (g.Per != null) o["per"] = JObject.FromObject(g.Per);
            if (g.Facts != null) o["facts"] = new JArray(g.Facts);
            return o;
        }

        /// <summary>The bag screen's { bag: { id: n } } (anything else: an empty bag, as the original's `res?.bag || {}`).</summary>
        static Dictionary<string, int> BagTarget(JToken bag)
        {
            var d = new Dictionary<string, int>();
            if (bag is JObject o)
                foreach (var p in o.Properties())
                    if (p.Value.Type is JTokenType.Integer or JTokenType.Float) d[p.Name] = (int)Math.Floor((double)p.Value);
            return d;
        }

        /// <summary>
        /// ui/bag.js applyPocket: the bag screen's { pocket } back into G after ApplyBag set the counts. Empty the pocket,
        /// then fill it, so the 0.5 L check never trips over what was there before; what no longer fits stays in the main part.
        /// </summary>
        public static void ApplyPocket(GameRules rules, GameState g, JToken pocket)
        {
            if (!(pocket is JObject p)) return;
            foreach (var (id, e) in g.Items.ToList()) if ((e.Pocket ?? 0) != 0) rules.SetPocket(g, id, 0);
            foreach (var prop in p.Properties())
            {
                if (!g.Items.TryGetValue(prop.Name, out var e) || e == null) continue;
                var def = rules.Db.Get(prop.Name);
                if (def == null || !def.pocket) continue;
                if (!(prop.Value.Type is JTokenType.Integer or JTokenType.Float)) continue;
                double n = (double)prop.Value;
                if (!(n > 0)) continue;
                try { rules.SetPocket(g, prop.Name, (int)Math.Min(Math.Floor(n), e.Bag)); }
                catch (InvalidOperationException) { /* over 0.5 L: stays in the main part */ }
            }
        }

        static string RoadName(string id) =>
            id != null && TownMap.Closures.TryGetValue(id, out var c) ? Regex.Replace(c.Label, " 통제$", "") : id;

        static (double x, double z)? Centre(TownRect? b) => b.HasValue ? ((b.Value.X0 + b.Value.X1) / 2, (b.Value.Z0 + b.Value.Z1) / 2) : null;

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        public static async Task Depart(StoryContext ctx)
        {
            var G = ctx.G;
            var closures = G.Cond.Closures.Evac;
            ctx.SetLevelState("town", "closures", new JArray(closures));
            ctx.SetLevelState("town", "crowd", "assembly");
            ctx.SetLevelState("town", "buses", "on");
            ctx.SetLevelState("town", "waterPoint", "off");
            ctx.SetLevelState("town", "notices", "evac");
            await ctx.Goto("apartment", "home", "interiorDark");
            ctx.Refresh();
            // After the 'depart' checkpoint, so a retry asks again: "the closed road is open, a shortcut" (always false).
            string road = Rumors.Rumor("evac_shortcut").Road(G), shortcut = RoadName(road);
            var claim = await ctx.Modal(() => RumorScene.HearClaim(ctx, "evac_shortcut", new[] { "나가려는데 휴대폰이 울린다. 동네 단체방에 새 글이 올라왔다." }));
            bool checking = claim.Act == "check", @checked = false;
            bool followed = G.Events.Any(e => e.Type == "rumor_followed" && (string)e.Details?["id"] == "evac_shortcut");
            if (checking && await ctx.Modal(() => StoryCommon.RadioScene(ctx, "evac")) != null)
            {
                @checked = true; G.SetKnow("evacClosuresKnown", true); Rumors.Verify(G, "evac_shortcut");
                ctx.Toast($"라디오: 오늘 통제 구간에 {shortcut}도 들어 있다. 풀렸다는 글은 사실이 아니었다.", "official", 5200);
            }
            bool noted = false, said = false, handed = false;
            // The last choice is yours: the bus is one way out, not the answer. Each path ends the story differently.
            void Show()
            {
                var body = new[]
                {
                    Rumors.Misled(G) == "evac_shortcut" ? (followed ? $"{shortcut} 쪽은 막혀 있었다. 안내 요원이 있는 큰길로 돌아가자." : $"단체방 글대로라면 {shortcut} 쪽이 집결지까지 지름길이다.") : null,
                    $"14:00 버스는 행정복지센터 앞 집결지에서 떠납니다. {(G.Knows("evacClosuresKnown") ? $"오늘 통제: {string.Join(", ", closures.Select(c => TownMap.Closures[c].Label))}" : "어디가 통제인지는 아직 모릅니다.")}",
                }.Where(s => !string.IsNullOrEmpty(s));
                var steps = new List<ObjectiveStep>();
                if (checking) steps.Add(new ObjectiveStep("한 번 더 확인하기: 단지 게시판 · 행정복지센터 게시판 · 관리사무소", @checked));
                steps.Add(new ObjectiveStep("문에 쪽지 남기기 (선택)", noted));
                steps.Add(new ObjectiveStep("버스 타기: 14:00까지 집결지"));
                steps.Add(new ObjectiveStep("남기: 관리사무소 박 소장에게"));
                steps.Add(new ObjectiveStep("서연과 함께: 303호"));
                steps.Add(new ObjectiveStep("돕기: 행정복지센터 자원봉사"));
                steps.Add(new ObjectiveStep("걸어서 떠나기: 큰길 끝까지"));
                ctx.Objective("오늘 어떻게 할까", string.Join(" ", body), steps);
            }
            Show();
            // Reaching the closed road the post sent you down (by E on the guide, or walking up to the barricade).
            bool Follow()
            {
                if (followed || Rumors.Misled(G) != "evac_shortcut") return false;
                followed = true; GameRules.RecordEvent(G, "rumor_followed", new JObject { ["id"] = "evac_shortcut", ["road"] = road });
                G.SetKnow("evacClosuresKnown", true); // you have stood at one: the objective now lists today's closures
                Show(); return true;
            }
            Func<StoryPoint, Task<JToken>> Official(string where) => async _ =>
            {
                var o = Rumors.Posts(G, "evac_shortcut").First(p => p.Key == "official");
                await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = where, Role = $"{o.From} · 공식 안내",
                    Lines = { o.Text, $"오늘 통제 구간: {string.Join(", ", closures.Select(RoadName))}. 현장 안내 요원의 안내를 따르십시오. (가상 안내)" },
                });
                G.SetKnow("evacClosuresKnown", true);
                if (!@checked)
                {
                    @checked = true; Rumors.Verify(G, "evac_shortcut");
                    if (claim.Taken) ctx.Toast($"{shortcut}은 오늘도 통제 중이다. 단체방 글과 달랐다.", "self", 4800);
                }
                Show();
                return null;
            };
            const double BX0 = -124, BX1 = 124, BZ0 = -104, BZ1 = 104; // past these, on a road, you have walked out of town
            var on = new Dictionary<string, Func<StoryPoint, Task<JToken>>>
            {
                ["homeExitDoor"] = async _ =>
                {
                    if (!noted)
                    {
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "현관", Role = "나가기 전에", Lines = { "문에 쪽지를 남길까?" },
                            Choices = Choices(("note", $"\"{GameRules.Hhmm(G.Minute)} 행정복지센터 집결지로 떠납니다 — 302호 정민\""), ("no", "그냥 나간다")),
                        });
                        noted = true;
                        if (pick == "note") { GameRules.RecordEvent(G, "note_left", new JObject()); ctx.Toast(ctx.Facts.Text("evac_note"), "official", 5000); }
                        Show();
                    }
                    await ctx.Goto("apartment", "corridor3F");
                    return null;
                },
                ["homeDoor"] = async _ => { await ctx.Goto("apartment", "homeEntry"); return null; },
                ["homeStock"] = async _ => { await StoryCommon.StockLook(ctx); return null; },
                ["seoyeonDoor"] = async _ =>
                {
                    // A first answer never closes the door: 서연 can be asked again (F6).
                    var pick = said
                        ? await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "오서연", Role = "303호", Lines = { "정민 씨, 아직 안 가셨어요? 버스 시간 괜찮아요?" },
                            Choices = Choices(("go", "이제 갈게요. 집결지에서 만나요"), ("wait", "같이 기다렸다가 지원 차량으로 같이 가요")),
                        })
                        : await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "오서연", Role = "303호", Lines = { "저는 지원 차량이 3시에 와요. 집결지에서 다시 만나요.", "같이 기다려 줄 필요는 없어요. 정민 씨는 버스 시간 맞춰 가세요." },
                            Choices = Choices(("go", "집결지에서 만나요"), ("card", "제 연락처 카드 사진 찍어 두세요"), ("wait", "같이 기다렸다가 지원 차량으로 같이 가요")),
                        });
                    if (pick == "card" && !said) GameRules.RecordEvent(G, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "card", ["what"] = "떠나기 전 서로의 연락처를 다시 확인했다" });
                    said = true;
                    if (pick == "wait") { GameRules.RecordEvent(G, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "wait", ["what"] = "지원 차량이 올 때까지 곁을 지켰다" }); return "seoyeon"; }
                    return null;
                },
                ["managerWindow"] = async _ =>
                {
                    var site = Rumors.Posts(G, "evac_shortcut").First(p => p.Key == "manager");
                    var pick = await ctx.Ui.Talk(new TalkOptions
                    {
                        Speaker = "박영수 소장", Role = "관리사무소", Lines = { site.Text, "저는 남아요. 건물 지키는 사람도 있어야죠. 조심히 가요, 302호." },
                        Choices = Choices(("go", "소장님도 조심하세요"), ("stay", "저도 남겠습니다. 같이 지켜요")),
                    });
                    if (!@checked) { @checked = true; Rumors.Verify(G, "evac_shortcut"); Show(); }
                    // Only staying is something 정민 does for the building; the manager's own work is not 정민's help (F7).
                    if (pick == "stay") { GameRules.RecordEvent(G, "help", new JObject { ["who"] = "manager", ["what"] = "박 소장과 함께 남아 건물을 지키기로 했다" }); return "stay"; }
                    if (!handed && HomeOf(G, Water) >= 2 && HomeOf(G, Food) >= 2)
                    {
                        handed = true; // offered once, only from what stays at home (not the bag)
                        var give = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "박영수 소장", Role = "관리사무소", Lines = { "집에 남는 물이나 먹을 거 있으면, 남는 분들 몫으로 맡겨 줘도 돼요." },
                            Choices = Choices(("give", "남는 물과 통조림을 관리사무소에 맡길게요"), ("no", "다음에요")),
                        });
                        if (give == "give") { MoveOut(ctx, Water, 2); MoveOut(ctx, Food, 2); ctx.Pass(5); ctx.Remember("stock_to_manager"); }
                    }
                    return null;
                },
                ["center"] = async _ =>
                {
                    var pick = await ctx.Ui.Talk(new TalkOptions
                    {
                        Speaker = "행정복지센터", Role = "자원봉사 접수", Lines = { "급수 안내하고 거동 불편한 분들께 물 나를 사람이 모자라요.", "남아서 도와주실 수 있어요? 버스는 놓치게 될 거예요." },
                        Choices = Choices(("yes", "남아서 돕겠습니다"), ("no", "죄송해요, 버스를 타야 해요")),
                    });
                    return pick == "yes" ? "volunteer" : null;
                },
                ["aptNotice"] = Official("한빛아파트 단지 게시판"),
                ["centerNotice"] = Official("행정복지센터 게시판"),
                ["assembly"] = _ => Task.FromResult<JToken>("bus"),
            };
            foreach (var id in TownMap.Closures.Keys.ToList())
            {
                var cid = id;
                on[$"closure_{cid}"] = async _ =>
                {
                    if (cid == road) Follow();
                    var lines = new List<string> { "이 구간은 통제 중입니다. 다른 길로 돌아가 주세요." };
                    if (cid == road && Rumors.Misled(G) == "evac_shortcut") lines.Add("단체방에 여기가 풀렸다는 글이 돈다면서요. 아니에요. 안내 요원이 있는 큰길로 가세요.");
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "안내 요원", Role = TownMap.Closures[cid].Label, Lines = lines });
                    return null;
                };
            }
            var pathTok = await ctx.Roam(new RoamOptions
            {
                Rate = 4,
                Tick = _ =>
                {
                    var E = ctx.Engine;
                    double px = E.PlayerX, pz = E.PlayerZ;
                    if (E.LevelId == "town" && (px < BX0 || px > BX1 || pz < BZ0 || pz > BZ1)) return "walk";
                    if (E.LevelId == "town" && !followed && Rumors.Misled(G) == "evac_shortcut")
                    {
                        var pt = E.Points?.FirstOrDefault(q => q.Id == $"closure_{road}");
                        (double x, double z)? at = pt != null ? (pt.X, pt.Z)
                            : Centre(road != null && TownMap.Closures.TryGetValue(road, out var c) && c.Barricades.Count > 0 ? c.Barricades[0] : (TownRect?)null);
                        if (at.HasValue && Hypot(px - at.Value.x, pz - at.Value.z) < 7 && Follow())
                            ctx.Toast($"{shortcut}: 바리케이드가 그대로 있다. 풀렸다던 글과 다르다.", "site", 5200);
                    }
                    if (G.Day > 12 || G.Minute >= 14 * 60)
                    {
                        if (Rumors.LostByRumor(G) != null) ctx.Fail("lostWay", new JObject { ["via"] = "rumor" });
                        return "missed";
                    }
                    return null;
                },
                On = on,
            });
            var path = pathTok?.Type == JTokenType.String ? (string)pathTok : pathTok?.ToString();
            G.SetKnow("path", path);
            GameRules.RecordEvent(G, "path", new JObject { ["path"] = path });
            if (path != "bus")
            {
                var caption = path switch
                {
                    "missed" => MissedCaption(ctx),
                    "stay" => "가방을 내려놓았다. 오늘부터 이 건물이 내 자리다.",
                    "seoyeon" => "서연의 문 앞에 가방을 내려놓고 앉았다. 지원 차량은 늦어지고 있었다.",
                    "volunteer" => "조끼를 받아 입었다. 급수대 앞 줄이 길다.",
                    "walk" => "도시의 끝, 통제선을 지나 국도로 들어섰다. 뒤에서 또 포성이 울렸다.",
                    _ => null,
                };
                await ctx.Caption(caption, null, 4000);
                return;
            }
            GameRules.RecordEvent(G, "arrived_assembly", new JObject { ["minute"] = G.Minute });
            var lift = await ctx.Say("집결지", new[] { "리프트 앞 어르신 짐이 무겁다." }, Choices(("ask", "\"들어 드릴까요?\" 먼저 여쭌다"), ("line", "줄을 선다")), "임시 집결지");
            if (lift == "ask")
            {
                await ctx.Say("어르신", new[] { "고마워요. 그 가방만 버스 안까지 올려 줘요. 나머진 내가 들게." }, null, "버스 리프트 앞");
                ctx.Stat("fatigue", 4); ctx.Pass(3); ctx.Remember("bus_helped");
            }
            if (G.Knows("routeChecked") && !followed) ctx.Payoff("route", "동네 길을 미리 알아 둔 덕분에 통제 구간을 돌아 집결지에 닿았다");
            bool card = Count(G, "contact_card", true) + Count(G, "doc_copies", true) > 0;
            bool deep = card && !GameRules.InPocket(G, "contact_card") && !GameRules.InPocket(G, "doc_copies"); // game rule (M8): the pocket comes out at once
            bool idCard = Count(G, "id_card", true) > 0; // carried in the bag or its front pocket (the pocket is part of bag); left at 집 = not shown
            await ctx.Say("집결지 접수", new[]
            {
                "이름, 연락처, 복용약을 적어 주세요. 신분증 있으세요?",
                idCard ? "(지갑에서 신분증을 꺼내 보여 주었다.)" : "(신분증을 집에 두고 나왔다. 이름과 주소, 생년월일로 본인 확인을 하느라 한참 걸렸다.)",
                !card ? "(연락처는 기억나는 대로 적었다.)" : deep ? "(연락처 카드와 서류 사본은 가방 깊숙이 있었다. 꺼내느라 잠깐 걸렸다.)" : "(가방 앞주머니의 연락처 카드와 서류 사본도 함께 건넸다.)",
            }, null, "임시 집결지");
            if (deep) ctx.Pass(2);
            if (!idCard) { ctx.Pass(15); ctx.Toast(ctx.Facts.Text("docs_take"), "official", 5000); }
            // The docs payoff credits the D-1 card/papers (Ripples Causes.docs); the 신분증 is not a D-1 prep.
            if (card) ctx.Payoff("docs", $"가방에 챙긴 연락처 카드와 서류 사본으로 접수를 {(deep ? "조금 늦게나마 " : "바로 ")}마쳤다");
            await ctx.Caption("버스 창밖으로 연무동이 멀어진다.", null, 3500);
        }

        /// <summary>'missed' happens wherever the clock passes 14:00: only at the assembly point is it an empty bay (F11).</summary>
        public static string MissedCaption(StoryContext ctx)
        {
            var E = ctx.Engine;
            var a = E.LevelId == "town" ? E.Points?.FirstOrDefault(q => q.Id == "assembly") : null;
            return a != null && Hypot(E.PlayerX - a.X, E.PlayerZ - a.Z) < 25 ? "버스는 이미 떠났다. 텅 빈 집결지에 바람만 분다." : "시계를 보니 2시가 넘었다. 버스는 이미 떠났다.";
        }

        // ================================================================== ending

        /// <summary>The closing scene and the ending document. Returns what the ending screen returns.</summary>
        public static async Task<JToken> Ending(StoryContext ctx)
        {
            var G = ctx.G;
            G.Chapter = "ending";
            GameRules.RecordEvent(G, "ending", new JObject { ["load"] = ctx.Rules.BagLoad(G).WeightKg });
            // C2: what the early choices led to, over the town at dusk, then where 정민 is now. Never blocks the ending.
            try { await Cutscenes.RippleMontage(ctx, Ripples.Of(G), KnowString(G, "path")); }
            catch (Exception) { /* the original logs '[cine] ripple montage failed; the story goes on' */ }
            if (ctx.RecordRun != null) await ctx.RecordRun(); // the ending gallery (and the records) count the run once its closing scene has played
            var record = Debrief.EndingRecord(ctx.Rules, G, id => ctx.Facts.Text(id), id => ctx.Facts.SourceId(id));
            return await ctx.Modal(() => ctx.Ui.Open("ending", new JObject
            {
                ["record"] = JObject.FromObject(record), ["sources"] = JArray.FromObject(ctx.Facts.Sources),
            }));
        }
    }
}
