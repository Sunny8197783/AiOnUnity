// Port of story/endure.js — 3장 버티기 · D+1 ~ D+3. Blackout and water cut. Food safety, the water point, a rumour,
// a small fire in the corridor. Helping costs rest; refusing costs nothing but the help.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Endure
    {
        /// <summary>The story mark after the corridor fire (ctx.Reached): the teacher mode's fire moment ends here.</summary>
        public const string FireDoneMark = "fire_done";
        /// <summary>The resume key of the scene-select "살펴서 대피" start (the fire's evening, a prepared save only).</summary>
        public const string LookResume = "look";
        /// <summary>The resume key of the scene-select "소화기" start: the same evening, straight into the corridor (no look step).</summary>
        public const string ExtResume = "ext";
        /// <summary>The resume key of the scene-select 「산에서 내려온 사람들」 start (D+2 morning, a prepared save only).</summary>
        public const string MountainResume = "mountain";

        /// <summary>
        /// The "살펴서 대피" judgement before the corridor fire (LookFirstScene.Run; a port addition, not in the original
        /// chapter). A test seam like DDay.CprPart: the tests that compare this chapter with the original set it to null.
        /// </summary>
        public static Func<StoryContext, bool, Task<LookFirstLogic.Outcome>> LookPart = (ctx, knocked) => LookFirstScene.Run(ctx, knocked);

        /// <summary>
        /// 「산에서 내려온 사람들」 (MountainScene; a port addition): the traces on the way to the water point (D+1) and the
        /// D+2 day indoors in place of the original's one-line skip to the fire's night, then the record card after the
        /// chapter card. Test seams like LookPart: the tests that compare this chapter with the original set both to null.
        /// </summary>
        public static Func<StoryContext, Task> TracesPart = MountainScene.Traces;
        public static Func<StoryContext, Task> MountainPart = MountainScene.Run;

        static int Count(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null ? e.Home + e.Bag : 0;
        static string WaterId(GameState g) => new[] { "water_500", "water_2l" }.FirstOrDefault(id => Count(g, id) > 0);
        static ItemEntry Entry(GameState g, string id)
        {
            if (!g.Items.TryGetValue(id, out var e) || e == null) g.Items[id] = e = new ItemEntry();
            return e;
        }
        static string Name(StoryContext ctx, string id) => ctx.Rules.Db.Get(id)?.displayName ?? id;
        static string Unit(StoryContext ctx, string id) => ctx.Rules.Db.Get(id)?.unit;

        /// <summary>audio.fire(level): the fire loop at a level 0..1 (0 stops it).</summary>
        public static void FireSound(StoryContext ctx, double level) =>
            ctx.Audio.Loop("fire", level > 0, new JObject { ["level"] = Math.Max(0, Math.Min(1, level)) });

        static bool Drink(StoryContext ctx)
        {
            var g = ctx.G;
            var id = WaterId(g);
            if (id == null) { ctx.Toast("마실 물이 없다.", "self"); return false; }
            ctx.Rules.Consume(g, id, 1, g.Items[id].Home > 0 ? "home" : "bag");
            GameRules.Adjust(g, "thirst", id == "water_2l" ? -45 : -20);
            ctx.Refresh();
            return true;
        }

        /// <summary>
        /// The body giving out is a soft failure: a neighbour finds 정민, and the day starts over. Injuries are left to roam
        /// (dead { why }); exhaustion comes from thirst or from effort (carrying water home).
        /// </summary>
        static Action BodyCheck(StoryContext ctx, string why = "thirst") => () =>
        {
            var s = ctx.G.Stats;
            if (s.Health <= 0) return;
            if (s.Thirst >= 95) ctx.Fail("exhausted", new JObject { ["why"] = "thirst" });
            if (s.Fatigue >= 100) ctx.Fail("exhausted", new JObject { ["why"] = why });
        };

        static Func<double, JToken> Tick(Action check) => _ => { check(); return null; };

        static bool OnThird(StoryContext ctx) => ctx.Engine.LevelId == "apartment" && ctx.Engine.PlayerY > 4;

        static Func<StoryPoint, Task<JToken>> H(Func<Task<JToken>> f) => _ => f();
        static Func<StoryPoint, Task<JToken>> H(Func<Task> f) => async _ => { await f(); return null; };
        static readonly JToken Undefined = null;

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            g.Chapter = "endure";
            ctx.Hud.SetChapter("3장", "버티기");
            ctx.Power = false; ctx.OutdoorEnv = "day"; ctx.IndoorEnv = "interior";
            ctx.SetLevelState("apartment", "power", "off");
            ctx.SetLevelState("store", "power", "off");
            ctx.SetLevelState("store", "clerk", "out"); // 이수아 left town the morning after the alert
            ctx.SetLevelState("apartment", "elder304", "off");
            g.SetKnow("bagOn", false); // at home the bag sits by the door (older saves may still have it on)
            ctx.Refresh();
            if (ctx.Resume != "fire" && ctx.Resume != LookResume && ctx.Resume != ExtResume)
            {
                bool mountain = ctx.Resume == MountainResume && MountainPart != null;
                if (!mountain)
                {
                    await ctx.Skip("D-day 금 밤", "D+1 토 07:00", new[] { "전기는 밤새 돌아오지 않았다. 아침에는 수도꼭지에서도 물이 나오지 않았다." });
                    g = ctx.G;
                    g.Day = 1; g.Minute = 7 * 60;
                    ctx.SetLevelState("apartment", "windowView", "day");
                    PrepActions.ApplyHome(ctx);
                    await ctx.Goto("apartment", "home", "interiorDark");
                    ctx.Refresh();
                    await Morning(ctx);
                    await WaterTrip(ctx);
                    await Evening(ctx);
                }
                else PrepActions.ApplyHome(ctx);
                if (MountainPart != null) await MountainPart(ctx);
                else await ctx.Skip("D+1 토 밤", "D+2 일 21:40", new[] { "하루가 더 지났다. 낮에는 물을 아껴 마시며 집에서 버텼다." });
                ctx.Checkpoint("fire");
            }
            await CorridorFire(ctx, ctx.Resume == ExtResume);
            ctx.Reached(FireDoneMark);
            await DayThree(ctx);
            var sum = Debrief.ChapterSummary(ctx.Rules, ctx.G, "endure");
            await ctx.Modal(() => ctx.Ui.Open("chapterCard", new JObject
            {
                ["chapter"] = "3장", ["title"] = "버티기", ["lines"] = new JArray(sum.Lines), ["payoffs"] = JArray.FromObject(sum.Payoffs),
            }));
            if (MountainPart != null) await MountainScene.RecordCard(ctx); // 장 사이 기록 카드 "그날, 시민들은" (only after the sub-scene)
        }

        static async Task Morning(StoryContext ctx)
        {
            var g = ctx.G;
            bool fridge = false, drink = false, info = false, offered = false;
            void Show() => ctx.Objective("정전 이틀째 아침", "냉장고를 정리하고, 물을 마시고, 오늘의 안내를 확인합니다. 다 했으면 달력에서 하루를 시작합니다.", new[]
            {
                new ObjectiveStep("냉장고 정리", fridge), new ObjectiveStep("물 마시기 (베란다 수납장)", drink), new ObjectiveStep("안내 확인 (라디오·이웃·1층 게시판)", info),
            });
            Show();
            await ctx.Roam(new RoamOptions
            {
                Tick = Tick(BodyCheck(ctx)),
                On =
                {
                    ["homeFridge"] = H(async () =>
                    {
                        if (fridge) { ctx.Toast("냉장고는 정리했다."); return; }
                        await StoryCommon.FactCard(ctx, "정전 12시간째 냉장고", new[] { "food_fridge", "food_4h", "food_door" });
                        foreach (var id in new[] { "fridge_food", "cooked_meat" })
                        {
                            int n = g.Items.TryGetValue(id, out var e) && e != null ? e.Home : 0;
                            if (n == 0) continue;
                            var pick = await ctx.Ui.Talk(new TalkOptions
                            {
                                Speaker = Name(ctx, id), Role = $"냉장고 · {n}{Unit(ctx, id)}",
                                Lines = { id == "cooked_meat" ? "익힌 고기라 괜찮지 않을까? 냄새는 아직 잘 모르겠다." : "반찬 통이 미지근하다." },
                                Choices = new List<TalkChoice> { new("eat", "먹는다"), new("toss", "버린다") },
                            });
                            if (pick == "eat")
                            {
                                ctx.Rules.Consume(g, id, 1); GameRules.Adjust(g, "health", -12); GameRules.Adjust(g, "tension", 8);
                                GameRules.RecordEvent(g, "ate_spoiled", new JObject { ["id"] = id });
                                ctx.Toast("오후 내내 배가 아팠다.", "self");
                                if (id == "cooked_meat") await StoryCommon.FactCard(ctx, "익혀도 마찬가지입니다", new[] { "myth_meat", "food_doubt" });
                            }
                            else { ctx.Rules.Consume(g, id, n); GameRules.RecordEvent(g, "tossed_food", new JObject { ["id"] = id }); }
                        }
                        fridge = true; Show();
                    }),
                    ["homeStock"] = H(async () =>
                    {
                        await Task.CompletedTask;
                        if (drink) { ctx.Toast("아까 마셨다. 아껴 두자."); return; }
                        if (!Drink(ctx)) return;
                        drink = true; Show();
                        if (g.Knows("tubFilled")) ctx.Payoff("tub", "받아 둔 물로 세수하고 변기 물을 내렸다");
                    }),
                    ["homeRadio"] = H(async () =>
                    {
                        if (await StoryCommon.RadioScene(ctx, "water") != null) { g.SetKnow("waterPoint", true); info = true; Show(); }
                    }),
                    ["homeExitDoor"] = H(async () => { await ctx.Goto("apartment", "corridor3F"); }),
                    ["homeDoor"] = H(async () => { await ctx.Goto("apartment", "homeEntry"); }),
                    ["seoyeonDoor"] = H(async () =>
                    {
                        if (ctx.SeoyeonAsk != null) { ctx.Toast("\"이따 봬요.\"", "neighbor"); return; }
                        if (g.Rel["seoyeon"].Trust < 1) { await ctx.Ui.Talk(new TalkOptions { Speaker = "303호", Role = "문 너머", Lines = { "\"누구세요? …아, 302호요. 지금은 좀 바빠서요.\"" } }); return; }
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "오서연", Role = "303호",
                            Lines = { "아침 라디오에서 들었어요. 행정복지센터 마당에서 비상급수를 한대요. 9시 반부터요.", "저는 계단이 어려워서… 제 것도 한 통 부탁드려도 될까요? 물통은 제 걸 빌려 드릴게요. 대신 충전해 둔 보조배터리 하나 드릴게요." },
                            Choices = new List<TalkChoice> { new("yes", "같이 받아 올게요"), new("no", "오늘은 제 것만 겨우 들 것 같아요") },
                        });
                        ctx.SeoyeonAsk = pick;
                        g.SetKnow("waterPoint", true); info = true;
                        ctx.Note("서연: 행정복지센터 마당 비상급수 09:30부터", "neighbor");
                        if (pick == "no") { await ctx.Ui.Talk(new TalkOptions { Speaker = "오서연", Role = "303호", Lines = { "괜찮아요. 소장님께 여쭤볼게요." } }); ctx.Remember("water_declined"); }
                        Show();
                    }),
                    ["lobbyNotice"] = H(async () =>
                    {
                        await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "notice", Title = "관리사무소 안내", Meta = "D+1 07:30 · 박영수 소장",
                            Body = { "단수가 계속되고 있습니다.", "행정복지센터 마당 비상급수시설 운영: 09:30 ~ 17:00 (물통 지참)", "엘리베이터 운행 중지. 계단에서는 손전등을 쓰세요." },
                            Actions = new List<CardAction> { new("ok", "확인", true) },
                        });
                        g.SetKnow("waterPoint", true); info = true; ctx.Note("관리사무소: 비상급수 09:30~17:00, 물통 지참", "official"); Show();
                        if (offered) return;
                        offered = true;
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "1층 게시판", Role = "빈자리", Lines = { "안내문 아래에 빈자리가 있다." },
                            Choices = new List<TalkChoice> { new("add", "한 줄 덧붙인다: \"302호 · 급수소 다녀옵니다. 필요한 분은 문에 쪽지\""), new("no", "그냥 둔다") },
                        });
                        if (pick == "add") { ctx.Pass(3); ctx.Remember("board_offer"); ctx.Toast("펜으로 적고 테이프로 붙였다.", "self"); }
                    }),
                    ["homePlanner"] = H(async () =>
                    {
                        await Task.CompletedTask;
                        if (info) return (JToken)"done";
                        ctx.Toast("먼저 오늘의 안내를 확인하자.");
                        return Undefined;
                    }),
                },
            });
            g.Minute = Math.Max(g.Minute, 9 * 60 + 20);
            ctx.Refresh();
        }

        static async Task WaterTrip(StoryContext ctx)
        {
            var g = ctx.G;
            int jugs = Count(g, "water_jug");
            bool forSeoyeon = ctx.SeoyeonAsk == "yes";
            ctx.SetLevelState("town", "traffic", "none");
            ctx.SetLevelState("town", "crowd", "queue");
            ctx.SetLevelState("town", "waterPoint", "on");
            ctx.SetLevelState("town", "closures", new JArray());
            ctx.OutdoorEnv = "day";
            await PrepActions.GoOut(ctx, "비상급수시설로", "행정복지센터 마당의 비상급수시설로 갑니다.");
            if (TracesPart != null) await TracesPart(ctx); // 「산에서 내려온 사람들」: traces by the path up the hill (traces only)
            ctx.Objective("물 받기", "행정복지센터 마당 급수대에서 줄을 섭니다.", Array.Empty<ObjectiveStep>());
            await ctx.Roam(new RoamOptions { Rate = 2, Tick = Tick(BodyCheck(ctx)), On = { ["water"] = _ => Task.FromResult<JToken>("queue") } });
            await JoinQueue(ctx);
            await StoryCommon.FactCard(ctx, "비상급수시설", new[] { "water_point", "water_sphere", "water_hands" });
            ctx.Pass(25);
            var elder = await ctx.Say("급수대", new[] { "304호 어르신이 물통을 들기 힘들어 보인다." },
                new List<TalkChoice> { new("ask", "\"도와드릴까요?\" 먼저 묻는다"), new("go", "그냥 간다") }, "행정복지센터 마당");
            if (elder == "ask")
            {
                await ctx.Say("304호 어르신", new[] { "아이고, 그래 줄래요? 계단 앞까지만 들어 주면 돼요. 거기서부턴 내가 쉬엄쉬엄 갈게." }, null, "급수대");
                ctx.Stat("fatigue", 6); ctx.Remember("queue_carry_elder");
            }
            int carry;
            if (jugs > 0) { ctx.Rules.Use(g, "water_jug"); carry = 10; ctx.Payoff("water_jug", "접이식 물통으로 급수소 물을 받아 왔다"); }
            else { carry = 4; ctx.Toast("물통이 없어 빈 생수병 두 개에 4리터만 받았다.", "site"); }
            if (forSeoyeon) carry += 10;
            GameRules.RecordEvent(g, "water_fetched", new JObject { ["litres"] = carry, ["forSeoyeon"] = forSeoyeon });
            World.SetPointEnabled(ctx.Engine, "water", false); // filled: the tap is someone else's turn now
            ctx.SpeedOverride = carry > 12 ? 0.62 : 0.78;
            ctx.Engine.Carrying = "water";
            // never past the warning at pick-up
            GameRules.Adjust(g, "fatigue", Math.Min(carry > 12 ? 14 : 8, Math.Max(0, Strain.Carry.Warn + 5 - g.Stats.Fatigue)));
            ctx.Refresh();
            ctx.Objective("집으로", $"물 {carry}리터를 들고 103동 3층까지 돌아갑니다. 무겁다. 지치면 멈춰 서서 숨을 고르세요.", Array.Empty<ObjectiveStep>());
            // Effort: walking with the load tires (running more), standing still lets you catch your breath (Strain).
            var P = ctx.Engine;
            var check = BodyCheck(ctx, "carry");
            double lastX = P.PlayerX, lastZ = P.PlayerZ, hudT = 0;
            bool warned = false;
            void StrainStep(double dt)
            {
                double speed = dt > 0 ? Math.Sqrt((P.PlayerX - lastX) * (P.PlayerX - lastX) + (P.PlayerZ - lastZ) * (P.PlayerZ - lastZ)) / dt : 0;
                lastX = P.PlayerX; lastZ = P.PlayerZ;
                if (speed > 8) return; // a door or a teleport, not a step
                var d = Strain.CarryStep(dt, speed, carry);
                GameRules.Adjust(g, "fatigue", d.Fatigue); GameRules.Adjust(g, "thirst", d.Thirst);
                if ((hudT += dt) > 0.5) { hudT = 0; ctx.Refresh(); }
                if (!warned && g.Stats.Fatigue >= Strain.Carry.Warn) { warned = true; ctx.Toast("팔이 떨린다. 잠깐 물통을 내려놓고 숨을 고르자 (멈춰 서기).", "self", 4600); }
                else if (warned && g.Stats.Fatigue < Strain.Carry.Warn - 10) warned = false;
            }
            await ctx.Roam(new RoamOptions
            {
                Rate = 2,
                Tick = dt => { StrainStep(dt); check(); return OnThird(ctx) ? (JToken)"up" : null; },
                On = { ["elevator"] = H(async () => { await Task.CompletedTask; ctx.Toast("엘리베이터가 멈춰 있다. 계단으로."); }) },
            });
            ctx.SpeedOverride = null;
            ctx.Engine.Carrying = null;
            ctx.Refresh();
            Entry(g, "water_2l").Home += forSeoyeon ? (int)Math.Floor((carry - 10) / 2.0) : (int)Math.Floor(carry / 2.0);
            if (forSeoyeon)
            {
                ctx.Objective("303호에 물 전하기", "서연의 문을 두드립니다.", Array.Empty<ObjectiveStep>());
                await ctx.Roam(new RoamOptions { On = { ["seoyeonDoor"] = _ => Task.FromResult<JToken>("give") } });
                await ctx.Ui.Talk(new TalkOptions { Speaker = "오서연", Role = "303호", Lines = { "정말 고마워요. 덕분에 오늘은 버틸 수 있겠어요.", "약속한 보조배터리예요. 저는 하나 더 있어요." } });
                var pb = Entry(g, "powerbank");
                pb.Home += 1; pb.Checked = true;
                g.Rel["seoyeon"].Trust += 2;
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "water", ["what"] = "급수소에서 물 한 통을 받아 3층까지 날라 주었다" });
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "powerbank", ["what"] = "서연이 충전된 보조배터리를 나눠 주었다", ["by"] = "other" }); // received, not given
            }
            await ctx.Goto("apartment", "home");
            g.Minute = Math.Max(g.Minute, 13 * 60);
            ctx.Refresh();
        }

        /// <summary>
        /// Where the water queue ends (town-events.js WATER: 14 people at 0.9 m from the tap, toward +x), and where one is
        /// served: at the taps (x -81.2 .. -78.8 along z -19.8), west of the line's head (x -78.6), facing them (north), so
        /// after the wait the player is at the front and not beside the people still in line.
        /// </summary>
        public static readonly (double x, double z) QueueEnd = (-66.0, -18.4), TapSide = (-80.0, -19.05);

        /// <summary>
        /// E at the tap no longer cuts the line (port fix): a short fade to the end of the queue, the wait, then the turn
        /// at the tap.
        /// </summary>
        static async Task JoinQueue(StoryContext ctx)
        {
            var e = ctx.Engine;
            if (e.LevelId != "town") return;
            e.SetControls(false);
            try
            {
                await e.FadeTo(1, 0.3);
                e.TeleportTo(QueueEnd.x, e.PlayerY, QueueEnd.z, Math.PI / 2); // facing the tap (west)
                await e.FadeTo(0, 0.35);
                await ctx.Caption("줄을 서서 기다렸다.", null, 2400);
                await e.FadeTo(1, 0.3);
                e.TeleportTo(TapSide.x, e.PlayerY, TapSide.z, 0); // facing the taps (north)
                await e.FadeTo(0, 0.35);
            }
            finally { e.SetControls(true); }
        }

        static async Task Evening(StoryContext ctx)
        {
            var g = ctx.G;
            // Port: the afternoon passes on screen (it used to jump from the 3F landing at ~09:50 to 17:30).
            await ctx.Skip("D+1 토 오후", "D+1 토 17:30", new[] { "물을 아껴 쓰며 오후를 보냈다. 해가 기울자 집 안이 금세 어두워졌다." });
            g = ctx.G;
            g.Minute = 17 * 60 + 30;
            ctx.OutdoorEnv = "dusk";
            ctx.SetLevelState("town", "waterPoint", "off"); // closed at 17:00: tank, table and E all go
            ctx.SetLevelState("apartment", "windowView", "dusk");
            ctx.Refresh();
            bool visited = false, knocked = false;
            ctx.Objective("저녁 · 할 수 있는 일", "편의점에 들러 볼 수도 있고, 집에서 쉴 수도 있습니다. 침대에서 하루를 마칩니다.", new[] { new ObjectiveStep("바로편의점 (선택)", false), new ObjectiveStep("침대에서 쉬기", false) });
            await ctx.Roam(new RoamOptions
            {
                Tick = Tick(BodyCheck(ctx)),
                On =
                {
                    ["homeExitDoor"] = H(async () => { await ctx.Goto("apartment", "corridor3F"); }),
                    ["homeDoor"] = H(async () => { await ctx.Goto("apartment", "homeEntry"); }),
                    ["homeStock"] = H(async () => { await Task.CompletedTask; Drink(ctx); }),
                    ["storeCounter"] = H(async () =>
                    {
                        if (visited) { ctx.Toast("계산대에는 아무도 없다."); return; }
                        visited = true;
                        await StoreEvening(ctx);
                        ctx.Objective("저녁 · 할 수 있는 일", "집으로 돌아가 침대에서 하루를 마칩니다.", new[] { new ObjectiveStep("바로편의점 (선택)", true), new ObjectiveStep("침대에서 쉬기", false) });
                    }),
                    ["homeBed"] = H(async () =>
                    {
                        if (!knocked) { knocked = true; g.Minute = Math.Max(g.Minute, 21 * 60); ctx.Refresh(); await Neighbors.Knock(ctx); }
                        return (JToken)"sleep";
                    }),
                },
            });
            GameRules.Adjust(g, "fatigue", -30); GameRules.Adjust(g, "tension", -8);
            g.Day = 2; g.Minute = 21 * 60;
        }

        /// <summary>Nobody is behind the counter: 이수아 has gone to her family and left a note and a cash box.</summary>
        static async Task StoreEvening(StoryContext ctx)
        {
            var g = ctx.G;
            await ctx.Ui.Card(new CardOptions
            {
                Kind = "notice", Title = "계산대 위 쪽지", Meta = "바로편의점 · 비상등만 켜져 있다",
                Body = { "정전으로 영업을 멈춥니다. 본가에 다녀올게요. — 이수아", "급하신 분은 남은 건전지·생수를 가져가시고, 현금은 계산대 옆 통에 넣어 주세요. 카드 단말기는 안 됩니다." },
                Actions = new List<CardAction> { new("ok", "둘러본다", true) },
            });
            int cash = Count(g, "cash");
            if (cash > 0)
            {
                var pick = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "계산대 옆 현금 통", Role = "현금만", Lines = { "선반에 건전지 한 팩과 작은 생수 몇 병이 남아 있다." },
                    Choices = new List<TalkChoice> { new("buy", "건전지 한 팩과 생수 두 병을 챙기고 5만 원을 넣는다 (거스름돈 없음)"), new("no", "그냥 둔다") },
                });
                if (pick == "buy")
                {
                    ctx.Rules.Consume(g, "cash", 1, g.Items["cash"].Home > 0 ? "home" : "bag");
                    var cart = new JObject { ["batteries_aa"] = 1, ["water_500"] = 2 };
                    foreach (var kv in cart.Properties()) { var e = Entry(g, kv.Name); e.Home += (int)kv.Value; e.Bought += (int)kv.Value; }
                    // The cash is one 5만 원 bundle and nobody is there to give change: say so.
                    var prices = ctx.Rules.StorePrices();
                    GameRules.RecordEvent(g, "buy", new JObject { ["cart"] = cart, ["total"] = 50000, ["goods"] = prices["batteries_aa"] + prices["water_500"] * 2, ["paid"] = "cash", ["change"] = 0 });
                    ctx.Toast("6,900원어치를 챙기고 5만 원을 통에 넣었다. 거스름돈은 받을 수 없었다.", "self", 4200);
                    ctx.Payoff("cash", "카드가 안 되는 편의점에서 현금으로 건전지와 물을 샀다");
                }
            }
            else
            {
                var pick = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "정민", Role = "지갑", Lines = { "현금이 없다. 카드뿐이다. 카드로는 아무것도 살 수 없다.", "선반에 건전지 한 팩과 작은 생수 몇 병이 남아 있다." },
                    Choices = new List<TalkChoice> { new("iou", "건전지 한 팩만 챙기고 쪽지에 302호와 금액을 적는다"), new("take", "그냥 가져간다"), new("no", "그냥 둔다") },
                });
                void Add(string id, int n) => Entry(g, id).Home += n;
                if (pick == "iou") { Add("batteries_aa", 1); ctx.Remember("store_iou"); }
                if (pick == "take") { Add("batteries_aa", 1); Add("water_500", 2); ctx.Remember("store_took"); }
            }
            await Neighbors.StoreRumor(ctx); // judge the claim by its source, then spread or check
            await Neighbors.Barter(ctx);
            var shutter = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "바로편의점", Role = "나가는 길", Lines = { "가게 셔터가 반쯤 열린 채다." },
                Choices = new List<TalkChoice> { new("shut", "셔터를 내리고 쪽지를 남긴다: \"셔터 내려 뒀어요 — 302호\""), new("go", "그냥 간다") },
            });
            if (shutter == "shut") { ctx.Pass(5); ctx.Remember("store_shuttered"); }
            GameRules.RecordEvent(g, "help", new JObject { ["who"] = "clerk", ["what"] = "가게를 닫으며 쪽지와 현금 통을 남겨 이웃이 필요한 물건을 가져가게 했다", ["by"] = "other" }); // 이수아's kindness
            g.SetKnow("clerkLeft", true);
        }

        /// <summary>D+2 night: a candle left burning by 304 tipped onto parcel boxes. Encounter 2 for the extinguisher.</summary>
        /// <param name="inCorridor">The scene-select "소화기" start: 정민 is already out in the corridor (no look step).</param>
        static async Task CorridorFire(StoryContext ctx, bool inCorridor = false)
        {
            var g = ctx.G;
            g.Day = 2; g.Minute = 21 * 60 + 40;
            ctx.OutdoorEnv = "blackout";
            ctx.SetLevelState("apartment", "windowView", "blackout");
            ctx.SetLevelState("apartment", "hallFire", "small");
            ctx.SetLevelState("apartment", "smoke", 0.25);
            await ctx.Goto("apartment", inCorridor ? "corridor3F" : "home", "interiorDark");
            ctx.Refresh();
            FireSound(ctx, 0.4);
            // Butterfly (Elder): if 어르신 came down with someone on D-day, she knocks the moment it starts.
            bool knocked = Elder.ElderSafeEarly(g.Know);
            if (knocked)
            {
                await ctx.Caption("쾅쾅— \"302호! 302호! 내가 초를 넘어뜨렸어요! 불이야!\" 304호 어르신이 문을 두드린다.", "304호 어르신", 4200);
                ctx.Payoff("elder", "어르신이 먼저 문을 두드려, 불이 작을 때 알았다");
            }
            else await ctx.Caption("복도에서 타는 냄새가 난다. 문틈으로 주황빛이 흔들린다.", null, 3200);
            string size = "small", resolved = null;
            bool fighting = false;
            double t = 0;
            bool shouted = knocked;
            double growAt = knocked ? 45 : 28;
            void Grow()
            {
                if (size == "small" && resolved == null)
                {
                    size = "large"; ctx.SetLevelState("apartment", "hallFire", "large"); ctx.SetLevelState("apartment", "smoke", 0.7); FireSound(ctx, 0.9);
                    ctx.Toast("불이 상자 더미로 번졌다. 천장까지 닿을 것 같다.", "site");
                }
            }
            // Competition upgrade: look first (handle, gap, sound, broadcast), say where it is, then leave / stay / partition.
            bool looked = false;
            string lookAct = null;
            if (LookPart != null && !inCorridor)
            {
                var look = await LookPart(ctx, knocked);
                looked = look != null && look.Played;
                lookAct = looked ? look.Act : null;
                if (looked && look.Size == "large") Grow();
                if (looked && (look.Act == "stay" || look.Act == "partition"))
                {
                    resolved = look.Act;
                    await LookFirstScene.WaitForRescue(ctx, look, knocked);
                    FireSound(ctx, 0);
                    GameRules.Adjust(g, "tension", 10);
                    return;
                }
            }
            // Chose to leave: the stairs (or simply walking down them) start the smoke escape; the extinguisher stays on the way.
            bool leaving = looked && lookAct == "leave";
            void ShowObjective() => ctx.Objective("복도의 불", looked ? "몸을 낮추고 계단으로. 작은 불이면 소화기로, 커졌다면 알리고 대피합니다. 엘리베이터는 쓰지 않습니다." : "판단하세요. 작은 불이면 소화기로, 커졌다면 알리고 대피합니다.", Array.Empty<ObjectiveStep>());
            ShowObjective();

            // Leaving is a short walk down through the smoke (Escape), then 박 소장 in the lobby.
            async Task<JToken> Retreat()
            {
                resolved = "retreat";
                GameRules.RecordEvent(g, "fire_retreat", new JObject { ["size"] = size });
                await StoryCommon.FactCard(ctx, "대피하며 알리기", new[] { "fire_shout", "fire_119", "fire_lift", "fire_big" });
                ctx.Engine.SetControls(true); // the escape is its own roam, inside this handler's dialog pause
                await Escape.SmokeEscape(ctx, size, () => { ctx.SetLevelState("apartment", "hallFire", "large"); ctx.SetLevelState("apartment", "smoke", 0.75); FireSound(ctx, 0.9); }, neighbour: looked);
                ctx.Engine.SetControls(false);
                FireSound(ctx, 0);
                ctx.SetLevelState("apartment", "hallFire", "out"); ctx.SetLevelState("apartment", "smoke", 0.1);
                // after looking first, 정민 makes the 119 call from the lobby (신고는 하되, 대피가 먼저: fire_119)
                bool reported = looked && await LookFirstScene.Report(ctx, "lobby", size);
                await ctx.Ui.Talk(new TalkOptions { Speaker = "박영수 소장", Role = "1층", Lines = { reported
                    ? "다들 내려왔죠? 신고 잘했어요. 소방대가 금방 껐대요. 304호 어르신도 무사하시고요."
                    : "다들 내려왔죠? 119에 신고했어요. 소방대가 금방 껐대요. 304호 어르신도 무사하시고요." } });
                return "retreat";
            }

            async Task<JToken> Fight()
            {
                var mode = Drills.LadderStage(g, "extinguisher");
                var fire = size; // judged on the fire the player saw when they chose
                // competition upgrade: an extinguisher whose gauge nobody read in 1장 has lost its pressure (ExtinguisherCheck)
                bool low = ExtinguisherScene.GaugePart != null && ExtinguisherCheck.IsLow(g);
                JToken res;
                if (ExtinguisherScene.Use3D(ctx))
                {
                    // the 3D extinguisher in hand (the 2D screen stays the accessible alternative); its own roam has the
                    // controls on, so this roam's clock and stairs check stand aside meanwhile (fighting)
                    var setup = ExtinguisherScene.Setup(ctx, mode, fire, ExtinguisherScene.Corridor, low: low, shouted: shouted);
                    fighting = true;
                    try { res = await ExtinguisherScene.Run3D(ctx, setup, ExtinguisherScene.Corridor, Grow); }
                    finally { fighting = false; }
                }
                else
                {
                    var opts = new JObject { ["mode"] = mode, ["fire"] = fire };
                    if (low) opts["low"] = true;
                    res = await ctx.Ui.Open("extinguisher", opts);
                }
                var seq = (res as JObject)?["sequence"] is JArray a ? a.Select(x => x.Type == JTokenType.String ? (string)x : null).ToList() : new List<string>();
                if (seq.Contains("shout")) shouted = true;
                var ev = Drills.EvalExtinguisher(seq, fire);
                Drills.RecordEncounter(g, "extinguisher", ev.Result);
                if (ev.Result == "out")
                {
                    resolved = "out"; ctx.SetLevelState("apartment", "hallFire", "out"); ctx.SetLevelState("apartment", "smoke", 0.1); FireSound(ctx, 0);
                    if (g.Knows("extinguisherLocated")) ctx.Payoff("extinguisher", "소화기 위치와 순서를 알고 있어 작은 불을 바로 껐다");
                    if (ExtinguisherScene.GaugePart != null && !low && ExtinguisherCheck.Checked(g)) ctx.Payoff(ExtinguisherCheck.Prep, "1장에 압력계를 보고 알려 둔 덕에 소화기가 제대로 나왔다");
                    return "out";
                }
                if (low) ExtinguisherScene.LowNote(ctx);
                if (ev.Result == "retreat-ok") return await Retreat();
                if (ev.Result == "escalated") { FireSound(ctx, 0); ctx.Fail("fire"); }
                Grow();
                ShowObjective();
                return Undefined;
            }

            // The fire spreads only while the player is free to act; dialogs and the drill have their own pace.
            var roam = new RoamOptions
            {
                Rate = 0,
                Tick = dt =>
                {
                    if (fighting) return null; // the 3D extinguisher keeps its own pace
                    if (resolved == null && ctx.Engine.ControlsEnabled && (t += dt) > growAt) Grow();
                    // leaving after looking: walking down the stairs without a word is leaving too
                    if (leaving && resolved == null && ctx.Engine.LevelId == "apartment" && ctx.Engine.PlayerY < 4) return "down";
                    return null;
                },
                On =
                {
                    ["homeExitDoor"] = H(async () => { await ctx.Goto("apartment", "corridor3F"); }),
                    ["hallFire"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "복도", Role = size == "small" ? "택배 상자에 붙은 작은 불" : "천장까지 닿는 불길",
                            Lines = { size == "small" ? "304호 앞 택배 상자 위로 촛불이 넘어져 있다. 불길은 아직 작다." : "불길이 천장에 닿았다. 연기가 복도를 채운다." },
                            Choices = new List<TalkChoice> { new("shout", "\"불이야!\" 외친다", shouted) { Taken = shouted }, new("water", "물을 끼얹는다"), new("retreat", "대피하며 알린다 (계단 · 119)") },
                        });
                        if (pick == "shout")
                        {
                            shouted = true; GameRules.RecordEvent(g, "fire_shout", new JObject());
                            ctx.Toast("문이 열리고 이웃들이 나온다. 304호 어르신이 1층에서 올라오고 있다.", "site");
                            return Undefined;
                        }
                        // Once the fire reaches the ceiling the candle advice (extinguisher, blanket) no longer applies.
                        if (pick == "water")
                        {
                            if (size == "small") await StoryCommon.FactCard(ctx, "촛불 화재에 물은 위험할 수 있습니다", new[] { "fire_candle" });
                            else await StoryCommon.FactCard(ctx, "불길이 이미 커졌습니다", new[] { "fire_big", "fire_shout" });
                            Grow();
                            return Undefined;
                        }
                        return await Retreat();
                    }),
                    ["extinguisher"] = _ => Fight(),
                    // a neighbour stops 정민 at the elevator (a correction, not a punishment); not in the original chapter
                    ["elevator"] = H(async () => { if (looked) await LookFirstScene.ElevatorStop(ctx); else ctx.Toast(ctx.Facts.Text("fire_lift"), "official", 5000); }),
                },
            };
            if (leaving) roam.On["stairs3F"] = _ => Retreat();
            var how = await ctx.Roam(roam);
            if (how?.Type == JTokenType.String && (string)how == "down") await ctx.Modal(() => Retreat());
            FireSound(ctx, 0);
            GameRules.Adjust(g, "tension", 10);
        }

        static async Task DayThree(StoryContext ctx)
        {
            var g = ctx.G;
            // Port: the night after the fire passes on screen (it cut from the 21:40 lobby straight to D+3 10:00).
            // 서연 speaks from her own doorway (she was a voice from nowhere): the night passes, a knock ends it, and the
            // morning opens on the 3F corridor with 303 open. One level load: in the black before the skip the player is
            // put at 302's door, so the skip's way back lands there (it used to land in the lobby, then load 302, then the corridor).
            var en = ctx.Engine;
            if (en.LevelId == "apartment" && en.TryGetSpawn("corridor3F", out var cx, out var cy, out var cz, out var cyaw))
            {
                await en.FadeTo(1, 0.4);
                en.TeleportTo(cx, cy, cz, cyaw);
            }
            ctx.SetLevelState("apartment", "windowView", "day");
            ctx.SetLevelState("apartment", "smoke", 0);
            ctx.SetLevelState("apartment", "seoyeonDoor", "open");
            ctx.Objective(null);
            await ctx.Skip("D+2 밤", "D+3 일 10:00", new[] { "소방대가 돌아간 뒤에도 복도의 탄내는 밤새 빠지지 않았다. 아침이 되자 이웃들이 하나둘 문을 열었다.", "똑똑— 현관문을 두드리는 소리." });
            g = ctx.G;
            g.Day = 3; g.Minute = 10 * 60;
            ctx.OutdoorEnv = "day";
            if (en.LevelId != "apartment") await ctx.Goto("apartment", "corridor3F", "interiorDark");
            _ = en.FadeTo(0, 0.4); // the card skip (no time-lapse) leaves the black in place
            ctx.Refresh();
            if (MountainPart != null) await MountainScene.MorningAfter(ctx); // 「산에서 내려온 사람들」: the 통제 kept overnight is lifted
            if (g.Stats.Tension > 55) await StoryCommon.FactCard(ctx, "긴장이 높을 때", new[] { "mind_breath", "mind_call" });
            bool crossed = g.Events.Any(e => e.Type == "lookfirst" && e.Details?["act"]?.Type == JTokenType.String && (string)e.Details["act"] == "partition");
            var pick = await ctx.Say("오서연", new[] { (crossed ? "어젯밤에 베란다 벽을 넘어 301호로 피하셨다면서요? 다행이에요. " : "어젯밤 괜찮으셨어요? ") + "저 라디오 들은 거 메모해 뒀어요. 전기 복구 시점은 아직 정해지지 않았대요.", "앞으로 저녁마다 들은 걸 정리해서 문 앞에 붙여 둘게요. 필요하면 보세요." },
                new List<TalkChoice> { new("thanks", "고마워요. 저는 급수소 소식을 전해 드릴게요"), new("ok", "네, 볼게요") }, "303호 앞");
            if (pick == "thanks")
            {
                g.Rel["seoyeon"].Trust += 1;
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "memo", ["what"] = "라디오 메모와 급수소 소식을 서로 나누기로 했다" });
            }
            ctx.Note("서연 메모: 전기 복구 시점 미정 (라디오)", "neighbor");
            var visit = await ctx.Say("304호 어르신", new[] { g.Events.Any(e => e.Type == "fire_shout") ? "어젯밤엔 고마웠어요. 그 소리 듣고 바로 나왔지." : "어젯밤엔 다들 놀랐지요. 나는 괜찮아요." },
                new List<TalkChoice> { new("visit", "들러서 안부를 묻는다"), new("later", "다음에 들른다") }, "304호 앞");
            if (visit == "visit") { ctx.Pass(10); ctx.Remember("elder_visit"); }
            ctx.SetLevelState("apartment", "seoyeonDoor", "closed");
            GameRules.Adjust(g, "fatigue", -20); GameRules.Adjust(g, "tension", -10);
            ctx.Refresh();
        }
    }
}
