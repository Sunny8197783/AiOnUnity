// Port of story/dday.js — 2장 정전과 경보 · D-day 금요일. 19:40 blackout, 20:05 air-raid alert, the first evacuation.
// What was prepared shows up here as time saved, not as a score.
//
// Mappings: audio.siren(on) -> Audio.Loop("siren", on); audio.boom(d) -> Audio.Cue("boom", { distance: d });
// ui.phone / ui.bag / ui.chapterCard -> Ui.Open("phone" | "bag" | "chapterCard", the original's options).
// The bag screen's tidy callback ({ cost: '1분', run: () => ctx.pass(1) }) becomes { tidy: { cost: '1분' } } in the
// options and a number "tidied" in the result: the minutes the screen's tidy button was used (one each).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class DDay
    {
        public const int Alert = 20 * 60 + 5, Late = 20 * 60 + 30;
        /// <summary>The quiet evening before the lights go (the original's 7 s felt like a cut; port pacing).</summary>
        public const double EveningRoamSeconds = 15;
        /// <summary>Until this minute evacuees keep coming out of the complex (town state trickle on); after it the street empties.</summary>
        public const int TrickleEnd = Alert + 15;
        /// <summary>The resume key of a scene-select start inside the shelter (a prepared save, never a story checkpoint).</summary>
        public const string ShelterResume = "shelter";
        /// <summary>The story mark after the shelter's sign-in and seat (ctx.Reached): the teacher mode's alert moment ends here.</summary>
        public const string ShelteredMark = "sheltered";
        /// <summary>The resume key of the scene-select CPR start (seated in the shelter, just before the grandfather collapses).</summary>
        public const string CprResume = "cpr";

        /// <summary>Math.random stand-in (far blasts). Tests may pin it.</summary>
        public static Func<double> Random = new System.Random().NextDouble;

        /// <summary>
        /// 서연's side of the night (SeoyeonRelay.Run). A test seam: a scripted playthrough of this chapter may swap in a
        /// stand-in for the other chapter part (and must put it back); the game never changes it.
        /// </summary>
        public static Func<StoryContext, Task> SeoyeonPart = ctx => SeoyeonRelay.Run(ctx);

        /// <summary>
        /// The shelter's bystander CPR (CprScene.Run; a port addition, not in the original chapter). The same kind of test
        /// seam: the tests that compare this chapter with the original play it without the scene (and put it back).
        /// </summary>
        public static Func<StoryContext, Task> CprPart = ctx => CprScene.Run(ctx);

        static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => false,
            JTokenType.Boolean => (bool)t,
            JTokenType.Integer or JTokenType.Float => (double)t != 0,
            JTokenType.String => ((string)t).Length > 0,
            _ => true,
        };

        static int BagOf(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null ? e.Bag : 0;
        static string Hhmm(GameState g) => GameRules.Hhmm(g.Minute);

        // A few seconds after 정민 steps outside, a missile hits a villa south of the park (no story place):
        // the blast, the shake, then fire and a smoke column that stay for the rest of the war.
        // Shown as a cutscene (Cutscenes.StrikeScene); the town keeps the ruin, the fire and the smoke column after it.
        static void Strike(StoryContext ctx)
        {
            var g = ctx.G;
            g.SetKnow("strikeSeen", true);
            bool hit = false;
            void Impact()
            {
                if (hit) return;
                hit = true;
                ctx.Audio.Cue("boom", new JObject { ["distance"] = 0.55 });
                ctx.SetLevelState("town", "strike", "hit");
                GameRules.RecordEvent(g, "strike", new JObject { ["minute"] = g.Minute });
                ctx.Stat("tension", 18);
            }
            async Task Play()
            {
                try { await Cutscenes.StrikeScene(ctx, Impact); }
                catch (Exception) { /* '[cine] strike scene failed' */ }
                finally { Impact(); ctx.Toast("멈추지 말고 대피소로. 무너진 건물 쪽으로 가지 않는다.", "self", 5200); }
            }
            _ = Play();
        }

        // Later, more blasts far away: a dull boom and a slight tremor every half minute or so.
        static Func<double, JToken> FarBlasts(StoryContext ctx)
        {
            double t = 18 + Random() * 20;
            return dt =>
            {
                if ((t -= dt) > 0) return null;
                t = 25 + Random() * 35;
                ctx.Audio.Cue("boom", new JObject { ["distance"] = 1 });
                ctx.Engine.Shake(0.05, 0.8);
                return null;
            };
        }

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            g.Chapter = "dday";
            ctx.Hud.SetChapter("2장", "정전과 경보");
            if (ctx.Resume == ShelterResume)
            {
                // Scene select (SceneSelect "shelter"): a prepared state taken inside the shelter, right after arriving.
                // Not a story checkpoint (no ctx.Checkpoint here): only a prepared save carries this key.
                ctx.Audio.Loop("siren", false);
                await ctx.Goto("shelter", "stairs");
                ctx.Refresh();
            }
            else if (ctx.Resume == CprResume)
            {
                // Scene select (SceneSelect "cpr"): seated in the shelter after the sign-in; again a prepared save only.
                ctx.Audio.Loop("siren", false);
                ShelterState(ctx);
                await ctx.Goto("shelter", "spot");
                ctx.Refresh();
            }
            else
            {
                if (ctx.Resume != "alert")
                {
                    await Evening(ctx);
                    ctx.Checkpoint("alert");
                }
                else
                {
                    BlackoutState(ctx);
                    await ctx.Goto("apartment", "home");
                }
                await AlertScene(ctx);
            }
            if (ctx.Resume != CprResume)
            {
                await Shelter(ctx);
                ctx.Reached(ShelteredMark);
            }
            // Competition upgrade: the grandfather on the next mat collapses (bystander CPR). After the teacher mode's stop.
            await CprPart(ctx);
            await SeoyeonPart(ctx);
            await AfterAlert(ctx);
        }

        static void BlackoutState(StoryContext ctx)
        {
            ctx.Power = false; ctx.OutdoorEnv = "blackout"; ctx.IndoorEnv = "interior";
            ctx.SetLevelState("apartment", "power", "off");
            ctx.SetLevelState("apartment", "windowView", "blackout");
            ctx.SetLevelState("store", "power", "off");
            ctx.G.Phone.Signal = "weak";
            PrepActions.ApplyHome(ctx);
        }

        static async Task Evening(StoryContext ctx)
        {
            var g = ctx.G;
            g.Day = 0; g.Minute = 19 * 60 + 30;
            ctx.Power = true; ctx.IndoorEnv = "interiorNight"; ctx.OutdoorEnv = "night";
            ctx.SetLevelState("apartment", "windowView", "night");
            PrepActions.ApplyHome(ctx);
            await ctx.Goto("apartment", "home");
            ctx.Refresh();
            if (g.Rel.TryGetValue("seoyeon", out var sy) && Truthy(sy.Contact))
                await ctx.Card(new CardOptions
                {
                    Kind = "sms", Title = "오서연", Meta = "19:32 · 문자", Body = { "정민 씨, 병원 끝나고 카페 쉼표에서 잠깐 쉬고 있어요. 9시쯤 들어가요." },
                    Actions = new List<CardAction> { new("ok", "확인", true) },
                });
            ctx.Objective("금요일 저녁", "집에서 쉬는 중입니다.", new List<ObjectiveStep>());
            double t = 0;
            await ctx.Roam(new RoamOptions
            {
                Doors = false,
                Tick = dt => (t += dt) > EveningRoamSeconds ? "go" : null, // or the first thing looked at
                On = { ["*"] = _ => Task.FromResult<JToken>("go") },
            });

            // 19:40 the lights go out.
            g.Minute = 19 * 60 + 40;
            bool off = false;
            void Cut()
            {
                if (off) return;
                off = true;
                BlackoutState(ctx);
                ctx.Engine.SetPower(false);
                ctx.Engine.SetEnv("interiorDark", 0.4);
                ctx.Audio.Ambience("blackout", 0.3);
            }
            try { await Cutscenes.BlackoutScene(ctx, Cut); } catch (Exception) { /* '[cine] blackout scene failed' */ }
            Cut();
            GameRules.RecordEvent(g, "blackout", new JObject());
            ctx.Refresh();
            var src = StoryCommon.LightSource(ctx);
            ctx.LightSource = src;
            ctx.Toast(src == "flashlight" ? "F 키: 손전등 켜기/끄기" : src == "phone" ? "F 키: 휴대폰 손전등 (배터리가 빨리 닳습니다)" : "빛이 없다. 천천히 움직이자.", "self", 5000);
            if (src == "flashlight") { ctx.Rules.Use(g, "flashlight"); ctx.Payoff("flashlight", "정전 직후 손전등을 바로 켰다"); }
            if (g.Knows("curtain")) ctx.Payoff("curtain", g.Knows("curtainSealed") ? "테이프로 가장자리를 막은 암막 커튼 덕분에 불빛이 밖으로 새지 않았다" : "암막 커튼을 쳐서 집 안 불빛이 밖으로 덜 새어 나갔다");

            g.Minute = Math.Max(g.Minute, 19 * 60 + 41); ctx.Refresh(); // the clock reads what the choice says
            var done = new HashSet<string>();
            bool first = true;
            while (!done.Contains("radio") && !done.Contains("phone"))
            {
                var choices = new List<TalkChoice>
                {
                    new("radio", "라디오를 켠다", !StoryCommon.Has(g, "radio"), "라디오가 없다"),
                    new("phone", "휴대폰을 본다"),
                    new("window", "창밖을 본다", done.Contains("window")),
                    new("hall", "복도에 나가 본다", done.Contains("hall")),
                };
                if (StoryCommon.Has(g, "candles") && !g.Knows("candleLit")) choices.Add(new TalkChoice("candle", "양초를 켠다"));
                var pick = await ctx.Say("정전", new[] { "무엇부터 할까?" }, choices, "19:41");
                bool wasFirst = first; first = false;
                if (pick != null) done.Add(pick);
                if (pick == "radio")
                {
                    var heard = await ctx.Modal(() => StoryCommon.RadioScene(ctx, "blackout"));
                    if (heard != null && g.Knows("radioChecked")) ctx.Payoff("radio", "맞춰 둔 주파수로 정전 안내를 바로 들었다");
                    if (heard != null && wasFirst) ctx.Remember("blackout_radio");
                    if (heard == null) done.Remove("radio");
                }
                else if (pick == "phone")
                {
                    StoryCommon.DrainBattery(g, 3);
                    g.Minute = Math.Max(g.Minute, 19 * 60 + 46); // the text came in at 19:46
                    ctx.Refresh();
                    await ctx.Modal(() => ctx.Ui.Open("phone", new JObject
                    {
                        ["battery"] = g.Phone.Battery, ["signal"] = "weak", ["mapApp"] = "fail",
                        ["messages"] = new JArray(new JObject
                        {
                            ["from"] = "안전안내문자", ["time"] = "19:46", ["kind"] = "safety",
                            ["text"] = $"[안전안내문자] 19:46 연무동 일대 정전 발생. 복구 시간 미정. 추가 안내 예정 {StoryCommon.Area}",
                        }),
                        ["actions"] = new JArray(new JObject { ["id"] = "close", ["label"] = "닫기" }),
                    }));
                    ctx.Note("19:46 연무동 일대 정전, 복구 시간 미정 (안전안내문자)", "official");
                    ctx.Toast("지도앱이 열리지 않는다. 데이터가 거의 잡히지 않는다.", "site");
                }
                else if (pick == "window")
                {
                    await ctx.Say("창밖", new[] { "연무동 전체가 어둡다. 건너편 동 창문에 휴대폰 불빛이 몇 개 흔들린다." }, null, "정전");
                }
                else if (pick == "hall")
                {
                    ctx.Remember("hall_checked");
                    // A look out of the door while the lines play: 304호 어르신 at her door, lit only by the exit sign.
                    var hallCam = HallLook(ctx);
                    if (hallCam != null)
                    {
                        ctx.SetLevelState("apartment", "elder304", "on");
                        ctx.Hud.SetCinematic(true); // a cut, not a broken camera: no HUD panels or the living room's prompt over it
                        ctx.Engine.SetCameraOverride(hallCam);
                        // the exit sign's green and her candle: just enough to see her at the door
                        ctx.Cine?.FillAt(Elder.HomeX - 1.2, Cutscenes.F3 + 2.2, Elder.HomeZ - 0.9, 3, "#b8f0c4", 5);
                    }
                    string tip = null;
                    try
                    {
                        tip = await ctx.Say("3층 복도", new[] { "비상구 표시등만 초록색으로 켜져 있다. 304호 어르신이 문을 열고 내다본다.", "\"정전인가 봐요. 나는 초 하나 켜 놨어요.\"" },
                        new List<TalkChoice> { new("tip", "\"어르신, 자리 비우실 땐 촛불 꼭 꺼 주세요\""), new("in", "그냥 들어간다") }, "정전");
                        if (tip == "tip")
                        {
                            await ctx.Say("304호 어르신", new[] { "그래요, 그래요. 깜빡 잊을 수도 있으니 말해 주니 좋네." }, null, "3층 복도");
                            ctx.Remember("elder_candle_tip");
                        }
                    }
                    finally
                    {
                        if (hallCam != null) { ctx.Engine.SetCameraOverride(null); ctx.Hud.SetCinematic(false); ctx.Cine?.FillAt(0, -500, 0, 0); ctx.SetLevelState("apartment", "elder304", "off"); }
                    }
                    if (tip == "tip") ctx.Toast(ctx.Facts.Text("candle_rules"), "official", 6000); // after the cut: the cinematic view hides toasts
                }
                else if (pick == "candle")
                {
                    g.SetKnow("candleLit", true); ctx.Rules.Use(g, "candles");
                    ctx.SetLevelState("apartment", "candles", "lit");
                    GameRules.RecordEvent(g, "candle_lit", new JObject());
                    // The original passes a 4th argument ('판단이 필요한 정보') that factCard does not take.
                    await StoryCommon.FactCard(ctx, "양초에 대한 안내는 공식 자료끼리 다릅니다", new[] { "candle_prep", "candle_no", "candle_rules" });
                }
            }
            ctx.Pass(Alert - g.Minute > 0 ? Alert - g.Minute : 0);
        }

        /// <summary>The 3F corridor seen from 302's door toward 304 (levels/apartment-corridor.js ELDER), or null off the apartment.</summary>
        static CameraShot HallLook(StoryContext ctx)
        {
            var e = ctx.Engine;
            if (e.LevelId != "apartment" || !e.TryGetSpawn("corridor3F", out var x, out _, out var z, out _)) return null;
            const double ex = Elder.HomeX, ez = Elder.HomeZ, y = Cutscenes.F3;
            // from 302's doorway (the spawn there, kept inside the 2 m corridor) down the open corridor to 304, long lens
            return new CameraShot { X = x + 0.3, Y = y + 1.62, Z = Math.Max(-1.7, Math.Min(-0.3, z)), TargetX = ex, TargetY = y + 1.15, TargetZ = ez - 0.3, Fov = 30 };
        }

        static async Task AlertScene(StoryContext ctx)
        {
            var g = ctx.G;
            g.Minute = Alert;
            ctx.Refresh();
            ctx.Audio.Loop("siren", true);
            // Town for the evacuation (set first: the siren cutscene shows the street filling).
            var closures = g.Cond.Closures.DDay ?? new List<string>();
            ctx.SetLevelState("town", "traffic", "none");
            ctx.SetLevelState("town", "crowd", "evacuating");
            ctx.SetLevelState("town", "closures", new JArray(closures));
            ctx.SetLevelState("town", "gates", new JObject { ["east"] = "closed", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "closed" });
            ctx.SetLevelState("town", "notices", "dday");
            if (!g.Knows("elderPick")) ctx.SetLevelState("apartment", "elder304", "on"); // 304호 어르신 at her door (Elder)
            try { await Cutscenes.SirenScene(ctx); }
            catch (Exception) { _ = ctx.Caption("공습경보 사이렌 — 1분간 오르내리는 소리 (5초 상승 · 3초 하강 반복)", null, 6000); }
            await StoryCommon.EmergencyText(ctx, $"[위급재난문자] 20:05 미사일 발사, 연무동지역 공습경보, 지하시설 또는 건물 내로 대피. EVACUATION, MISSILES ATTACK {StoryCommon.Area}", "20:05", "위급재난문자");
            GameRules.RecordEvent(g, "alert", new JObject { ["minute"] = Alert });
            await StoryCommon.FactCard(ctx, "공습경보", new[] { "alert_airraid", "bag_take", "alert_stairs", "alert_night" });

            // What is still open depends on this run's closures; nobody names a road that is closed tonight.
            var detour = string.Join("이나 ", new[] { ("danji", "단지길"), ("sijang", "시장길") }.Where(r => !closures.Contains(r.Item1)).Select(r => r.Item2));
            bool left = false, inStairs = false, trickleOff = false;
            ctx.SetLevelState("town", "trickle", "on");
            double outT = 0;
            var blasts = FarBlasts(ctx);
            var trail = ctx.Trail = new List<JObject>();
            // A long wander, not a long route: the trail (a point per 1.5 m in town) is compared with tonight's shortest way.
            var direct = Routes.ShortestPath(TownMap.Graph(closures, new Dictionary<string, bool> { ["east"] = false, ["stairs"] = true, ["ramp"] = true, ["access"] = false }), "home", "shelter")?.Length ?? 205;
            bool Wandered() => trail.Count * 1.5 > direct * 1.4 + 30;
            var elder = Elder.CreateElderDilemma(ctx);
            List<ObjectiveStep> Steps() => new()
            {
                new ObjectiveStep("배낭 챙기기", g.Knows("bagOn") || left), new ObjectiveStep("계단으로 1층까지", inStairs), new ObjectiveStep("가까운 대피소로", false),
            };
            void Show() => ctx.Objective("대피하기", "지하시설이나 유리창 없는 건물 안으로. 엘리베이터는 쓰지 않습니다.", Steps());
            Show();
            if (!g.Knows("idTaken")) ctx.Toast("지갑과 신분증은 거실 탁자 위에 있다. 나가기 전에 챙기자. (E)", "self", 5000);

            async Task TakeBag()
            {
                if (g.Knows("bagOn")) return;
                if (g.Knows("bagPacked") && g.Items.Values.Any(e => e.Bag > 0))
                {
                    g.SetKnow("bagOn", true); ctx.SetLevelState("apartment", "bag", "none"); ctx.Payoff("bag", "현관에 둔 배낭을 메고 바로 나섰다");
                    GameRules.RecordEvent(g, "departure", new JObject { ["bag"] = "packed" });
                    return;
                }
                bool empty = !g.Items.Values.Any(e => e.Bag > 0); // something may already be in it (the contact card, say)
                var pick = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "현관", Role = empty ? "배낭이 비어 있다" : "배낭을 아직 꾸리지 않았다", Lines = { "지금 챙길까, 그냥 나갈까?" },
                    Choices = new List<TalkChoice> { new("pack", "급히 챙긴다 (시간이 든다)"), new("go", "휴대폰만 들고 나간다") },
                });
                if (pick != "pack") { GameRules.RecordEvent(g, "departure", new JObject { ["bag"] = "none" }); return; }
                var items = BagDrill.BagItems(g, ctx.Rules.Db, id => ctx.Facts.Text(id), withTips: false);
                var mode = Drills.LadderStage(g, "bag");
                // The one timed packing: right after the siren, for whoever did not pack on D-1 (a packed bag skips all of this).
                var profile = ctx.Settings?.Profile ?? "";
                var res = await ctx.Ui.Open("bag", BagHand.AddOptions(new JObject
                {
                    ["mode"] = mode, ["context"] = "alert",
                    ["limit"] = new JObject { ["weightKg"] = ctx.Rules.Db.bagLimitKg, ["volumeL"] = ctx.Rules.Db.bagLimitL },
                    ["items"] = new JArray(items.Select(BagItemJson)), ["goals"] = new JArray(Drills.BagGoals["alert"].Select(GoalJson)),
                    ["tips"] = new JArray(), ["tidy"] = new JObject { ["cost"] = "1분" }, ["timer"] = BagHand.TimerFor(profile),
                }, g, profile, ctx.Settings?.ReduceMotion ?? false));
                var r = res as JObject;
                BagHand.Remember(g, r, "alert");
                // The original screen charges a minute per tidy. The timed screen charges it once: 10 s off the countdown, which is
                // in 'elapsed' and so in the rush minutes below.
                for (int i = BagHand.AlertTidyMinutes(r); i > 0; i--) ctx.Pass(1);
                BagDrill.ApplyBag(ctx.Rules, g, BagTarget(r?["bag"]));
                ApplyPocket(ctx.Rules, g, r?["pocket"]);
                var ev = Drills.EvalBag(ctx.Rules, g, "alert");
                Drills.RecordEncounter(g, "bag", ev.Missing.Count > 0 ? "partial" : "complete");
                int rush = BagHand.RushMinutes(r); // the original's fixed 4 minutes when the screen does not say
                ctx.Pass(rush);
                g.SetKnow("bagOn", true); ctx.SetLevelState("apartment", "bag", "none");
                ctx.Toast(BagHand.TimedOut(r) ? $"사이렌이 계속 울린다. 챙긴 것만 메고 나섰다. ({rush}분)" : $"물건을 고르느라 {rush}분이 지났다.", "self");
                GameRules.RecordEvent(g, "departure", new JObject { ["bag"] = "late", ["delay"] = rush });
            }

            var on = new Dictionary<string, Func<StoryPoint, Task<JToken>>>
            {
                ["homeBag"] = async _ => { await TakeBag(); Show(); return null; },
                ["homeRadio"] = _ =>
                {
                    if (g.Knows("candleLit") && !g.Knows("candleOut"))
                    {
                        g.SetKnow("candleOut", true); ctx.SetLevelState("apartment", "candles", "none"); GameRules.RecordEvent(g, "candle_out", new JObject());
                        ctx.Toast("촛불을 껐다.");
                        return Task.FromResult<JToken>(null);
                    }
                    ctx.Toast("지금은 대피가 먼저다.");
                    return Task.FromResult<JToken>(null);
                },
                ["homeExitDoor"] = async _ =>
                {
                    if (g.Knows("candleLit") && !g.Knows("candleOut"))
                    {
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "거실", Role = "촛불", Lines = { "탁자 위 촛불이 아직 켜져 있다." },
                            Choices = new List<TalkChoice> { new("out", "끄고 나간다"), new("leave", "그대로 나간다") },
                        });
                        if (pick == "out") { g.SetKnow("candleOut", true); ctx.SetLevelState("apartment", "candles", "none"); }
                        GameRules.RecordEvent(g, pick == "out" ? "candle_out" : "candle_left", new JObject());
                        if (pick != "out") ctx.Toast(ctx.Facts.Text("candle_rules"), "official", 6000);
                    }
                    await TakeBag();
                    left = true;
                    await ctx.Goto("apartment", "corridor3F");
                    Show();
                    return null;
                },
                ["elevator"] = _ =>
                {
                    ctx.Toast($"정전으로 멈춰 있다. {ctx.Facts.Text("alert_stairs")}", "official", 5000);
                    GameRules.RecordEvent(g, "elevator_tried", new JObject());
                    return Task.FromResult<JToken>(null);
                },
                ["managerWindow"] = async _ =>
                {
                    await elder.ManagerLine(); // 어르신 first, then the detour in the same visit
                    if (g.Rel.TryGetValue("manager", out var mg) && Truthy(mg.Contact))
                    {
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "박영수 소장", Role = "1층 현관", Lines = { $"정민 씨! 생활문화센터로 가요. 연무로 가운데는 통제한대요. {detour}로 돌아가요!" } });
                        g.SetKnow("closureHeard", true); ctx.Note($"박 소장: 연무로 중앙 구간 통제, {detour}로 우회", "neighbor");
                        ctx.Remember("manager_detour");
                    }
                    else await ctx.Ui.Talk(new TalkOptions { Speaker = "관리사무소장", Role = "1층 현관", Lines = { "생활문화센터 대피소로 가세요! 길 막힌 데 있으니 안내 잘 보시고요!" } });
                    return null;
                },
                ["shelterNotice"] = async _ =>
                {
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "현장 안내판", Role = Hhmm(g), Lines = { "동쪽 출입구 폐쇄 · A 계단 운영 · B 진출입로 운영 (차량 통제) · C 경사로 준비 중" } });
                    return null;
                },
                ["elder304"] = _ =>
                {
                    var pick = g.Know.TryGetValue("elderPick", out var ep) ? ep : null;
                    ctx.Toast(!g.Knows("elderPick") ? "어르신이 손잡이를 붙들고 서 있다." : pick?.Type == JTokenType.String && (string)pick == "manager" && !g.Knows("elderTold") ? "\"소장님한테 말 좀 해 줘요.\"" : "\"나는 괜찮아요. 어서 가요.\"", "neighbor");
                    return Task.FromResult<JToken>(null);
                },
                // Every notice board tonight says one thing (the runtime runs '*' before the boards' everyday text).
                ["*"] = async point =>
                {
                    if (point.Kind != "notice") return null;
                    await ctx.Ui.Talk(new TalkOptions { Speaker = point.Label, Role = "안내", Lines = { "대피소: 연무 생활문화센터 지하주차장. 유도표지판의 화살표를 따라가세요." } });
                    return StoryContext.Handled;
                },
            };
            foreach (var c in TownMap.Closures.Values)
            {
                var label = c.Label;
                on[$"closure_{c.Id}"] = async _ =>
                {
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "안내 요원", Role = label, Lines = { $"이 구간은 통제 중입니다. 돌아가 주세요. 생활문화센터는 {detour}로 갈 수 있습니다." } });
                    g.SetKnow("closureSeen", true); ctx.Note($"{label} (현장 안내 요원)", "site");
                    return null;
                };
            }

            await ctx.Roam(new RoamOptions
            {
                // Out in town after meeting a closure or a long walk: lost the way; otherwise the time ran out before leaving.
                Rate = 3,
                Deadline = new RoamDeadline
                {
                    Day = 0, Minute = Late,
                    TypeOf = () => ctx.Engine.LevelId == "town" && (g.Knows("closureSeen") || Wandered()) && !g.Knows("closureHeard") ? "lostWay" : "late",
                },
                Tick = dt =>
                {
                    var e = ctx.Engine;
                    if (left) elder.Tick(dt);
                    // The bag is on the back: the spot by the door is no longer something to press E on.
                    if (g.Knows("bagOn") && e.LevelId == "apartment") World.SetPointEnabled(e, "homeBag", false);
                    // Evacuees keep trickling out of the complex for a quarter of an hour, then the street empties.
                    if (!trickleOff && g.Minute > TrickleEnd) { trickleOff = true; ctx.SetLevelState("town", "trickle", "off"); }
                    if (e.LevelId == "town")
                    {
                        var last = trail.Count > 0 ? trail[^1] : null;
                        if (last == null || Math.Sqrt(Math.Pow((double)last["x"] - e.PlayerX, 2) + Math.Pow((double)last["z"] - e.PlayerZ, 2)) > 1.5)
                            trail.Add(new JObject { ["x"] = e.PlayerX, ["z"] = e.PlayerZ });
                    }
                    if (e.LevelId == "town" && !g.Knows("strikeSeen") && (outT += dt) > 4) Strike(ctx);
                    else if (g.Knows("strikeSeen")) blasts(dt);
                    if (!inStairs && e.LevelId == "apartment" && e.PlayerY < 1 && left)
                    {
                        inStairs = true; Show();
                        if (ctx.LightSource == "flashlight") ctx.Payoff("flashlight", "손전등으로 어두운 계단을 내려갔다");
                    }
                    return null;
                },
                On = on,
                Shelter = point => EnterGate(ctx, point),
            });
        }

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

        static JObject GoalJson(BagGoal goal)
        {
            var o = new JObject { ["id"] = goal.Id, ["label"] = goal.Label, ["items"] = new JArray(goal.Items ?? Array.Empty<string>()), ["min"] = goal.Min };
            if (goal.Per != null) o["per"] = JObject.FromObject(goal.Per);
            if (goal.Facts != null) o["facts"] = new JArray(goal.Facts);
            return o;
        }

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
            if (pocket is not JObject p) return;
            foreach (var kv in g.Items.ToList()) if ((kv.Value.Pocket ?? 0) != 0) rules.SetPocket(g, kv.Key, 0);
            foreach (var prop in p.Properties())
            {
                if (!g.Items.TryGetValue(prop.Name, out var e) || e == null) continue;
                var def = rules.Db.Get(prop.Name);
                if (def == null || !def.pocket) continue;
                if (prop.Value.Type is not (JTokenType.Integer or JTokenType.Float)) continue;
                double n = (double)prop.Value;
                if (!(n > 0)) continue;
                try { rules.SetPocket(g, prop.Name, Math.Min((int)Math.Floor(n), e.Bag)); } catch (Exception) { /* over 0.5 L: stays in the main part */ }
            }
        }

        static async Task<JToken> EnterGate(StoryContext ctx, StoryPoint point)
        {
            var g = ctx.G;
            bool GateSeen() => g.Events.Any(e => e.Type == "gate_observed" && e.Details?["who"]?.Type == JTokenType.String && (string)e.Details["who"] == "jeongmin");
            if (point.Id == "shelterEast")
            {
                if (!GateSeen()) GameRules.RecordEvent(g, "gate_observed", new JObject { ["who"] = "jeongmin", ["via"] = "sign", ["minute"] = g.Minute });
                g.SetKnow("gateClosedSeen", true);
                await ctx.Ui.Card(new CardOptions
                {
                    Kind = "notice", Title = "동쪽 출입구 폐쇄", Meta = $"현장 안내 · {Hhmm(g)}",
                    Body = { "동쪽 출입구 폐쇄 · A 계단 / B 진출입로 이용", "확인한 사실: 이 출입구가 닫혀 있음. 이것만으로는 시설 전체의 운영 여부를 알 수 없다." },
                    Actions = new List<CardAction> { new("ok", "다른 출입구로", true) },
                });
                if (g.Knows("entrancesChecked")) ctx.Payoff("entrances", "닫힌 정문 앞에서 망설이지 않고 다른 출입구로 향했다");
                return null;
            }
            if (point.Id == "shelterC") { ctx.Toast("셔터가 내려져 있다: \"준비 중\"", "site"); return null; }
            var route = point.Id == "shelterA" ? "stairs" : "ramp";
            if (!GateSeen()) GameRules.RecordEvent(g, "gate_observed", new JObject { ["who"] = "jeongmin", ["via"] = "notice_after", ["minute"] = g.Minute });
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "jeongmin", ["route"] = route, ["minute"] = g.Minute });
            GameRules.RecordEvent(g, "arrived", new JObject { ["minute"] = g.Minute, ["late"] = g.Minute > Alert + 15 });
            if (g.Knows("routeChecked")) ctx.Payoff("route", "확인해 둔 경로 덕분에 막힌 길에서 돌아갈 길을 알았다");
            ctx.Audio.Loop("siren", false);
            await ctx.Goto("shelter", route == "stairs" ? "stairs" : "ramp");
            ctx.Refresh();
            _ = ctx.Caption($"대피소 도착 · {Hhmm(g)}", null, 2600);
            return "arrived";
        }

        /// <summary>
        /// Reception: only a bag on your back counts (one left by the door at home does not help), and the line says where
        /// the card was.
        /// </summary>
        public static (bool Card, string Line) ReceptionCard(GameState g)
        {
            bool card = g.Knows("bagOn") && (BagOf(g, "contact_card") > 0 || BagOf(g, "doc_copies") > 0);
            bool front = GameRules.InPocket(g, "contact_card") || GameRules.InPocket(g, "doc_copies");
            return (card, !card ? "(휴대폰을 보며 기억나는 대로 적었다. 배터리가 아깝다.)" : front ? "(배낭 앞주머니의 비상연락카드를 보고 바로 적었다.)" : "(배낭에서 비상연락카드를 꺼내 보고 적었다.)");
        }

        /// <summary>
        /// Settled in: drink what the bag holds (500 mL first) and wrap up in its blanket. Returns the payoff line naming only
        /// what was there, or null (no bag on, or nothing of the kind in it).
        /// </summary>
        public static string UnpackAtShelter(GameRules rules, GameState g)
        {
            if (!g.Knows("bagOn")) return null;
            var w = new[] { "water_500", "water_2l" }.FirstOrDefault(id => BagOf(g, id) > 0);
            bool b = BagOf(g, "blanket") > 0 && rules.Usable(g, "blanket");
            if (w != null) { rules.Consume(g, w, 1, "bag"); GameRules.Adjust(g, "thirst", w == "water_2l" ? -45 : -20); }
            if (b) rules.Use(g, "blanket");
            if (w == null && !b) return null;
            var what = string.Join("과 ", new[] { w != null ? "물" : null, b ? "담요" : null }.Where(s => s != null));
            return $"배낭에서 {what}{(b ? "를" : "을")} 꺼냈다";
        }

        static void ShelterState(StoryContext ctx)
        {
            ctx.SetLevelState("shelter", "crowd", "arriving");
            ctx.SetLevelState("shelter", "ways", new JArray("stairs", "ramp")); // east closed, C not open yet: nobody walks in there
            ctx.SetLevelState("shelter", "phase", "alert");
            ctx.SetLevelState("shelter", "power", "emergency");
            if (ctx.G.Knows("elderPick")) ctx.SetLevelState("shelter", "elder", "on"); // helped down, or brought by 박 소장
            ctx.Engine.SetEnv("underground", 0.8);
        }

        static async Task Shelter(StoryContext ctx)
        {
            var g = ctx.G;
            ShelterState(ctx);
            bool signed = false;
            ctx.Objective("대피소 안에서", "접수대에서 명부를 쓰고 자리를 잡습니다.", new List<ObjectiveStep> { new("명부 쓰기 (접수대)"), new("자리 잡기") });
            var thuds = FarBlasts(ctx);
            await ctx.Roam(new RoamOptions
            {
                Rate = 0, Doors = false, Tick = thuds,
                On =
                {
                    ["shelterExit"] = _ => { ctx.Toast("경보가 해제될 때까지 대피소 안에 머뭅니다.", "site"); return Task.FromResult<JToken>(null); },
                    ["reception"] = async _ =>
                    {
                        if (signed) { ctx.Toast("명부를 썼다."); return null; }
                        var (card, line) = ReceptionCard(g);
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "대피소 관리자", Role = "접수대", Lines = { "이름이랑 연락처, 복용하는 약 있으면 여기 적어 주세요.", line } });
                        if (card) ctx.Payoff("docs", "비상연락카드를 보고 명부를 바로 적었다"); else { StoryCommon.DrainBattery(g, 2); ctx.Refresh(); }
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "대피소 관리자", Role = "접수대", Lines = { ctx.Facts.Text("shelter_rules"), ctx.Facts.Text("shelter_rumor") } });
                        signed = true;
                        var help = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "접수대", Role = "뒤에 선 사람", Lines = { "뒤의 할아버지가 안경을 두고 와 명부를 못 쓰신다." },
                            Choices = new List<TalkChoice> { new("ask", "\"대신 적어 드릴까요?\" 먼저 여쭌다"), new("go", "그냥 간다") },
                        });
                        if (help == "ask")
                        {
                            await ctx.Ui.Talk(new TalkOptions { Speaker = "할아버지", Role = "접수대", Lines = { "아이고, 그래 주면 고맙지. 내가 불러 줄 테니 적어 줘요." } });
                            ctx.Pass(2); ctx.Remember("register_helped");
                        }
                        ctx.Objective("대피소 안에서", "자리를 잡습니다.", new List<ObjectiveStep> { new("명부 쓰기 (접수대)", true), new("자리 잡기") });
                        return null;
                    },
                    ["shelterRadio"] = async _ =>
                    {
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "공용 라디오", Role = "재난방송 (가상)", Lines = (StoryCommon.BroadcastLines(ctx, "shelter") ?? Array.Empty<string>()).ToList() });
                        return null;
                    },
                    ["shelterBoard"] = async _ =>
                    {
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "대피소 게시판", Role = "안내", Lines = { "연무 생활문화센터 지하주차장 · 민방위 대피소", ctx.Facts.Text("shelter_share") } });
                        return null;
                    },
                    ["spot"] = _ =>
                    {
                        if (signed) return Task.FromResult<JToken>("sit");
                        ctx.Toast("먼저 접수대에서 명부를 써 주세요.", "site");
                        return Task.FromResult<JToken>(null);
                    },
                },
            });
            var got = UnpackAtShelter(ctx.Rules, g);
            if (got != null) { ctx.Refresh(); ctx.Payoff("bag", got); }
            var water = g.Knows("bagOn") ? new[] { "water_500", "water_2l" }.FirstOrDefault(id => BagOf(g, id) > 0) : null;
            if (water != null)
            {
                var pick = await ctx.Say("옆자리", new[] { "옆자리 아이 입술이 말라 있다." }, new List<TalkChoice> { new("give", "물 한 병을 나눈다"), new("keep", "그냥 둔다") }, "대피소");
                if (pick == "give")
                {
                    ctx.Rules.Consume(g, water, 1, "bag"); ctx.Remember("shelter_water_given");
                    ctx.Toast("아이 엄마가 고개를 숙여 고맙다고 했다.", "neighbor");
                }
            }
            if (!g.Knows("bagOn"))
            {
                ctx.Stat("thirst", 6);
                ctx.Toast("맨손으로 나와 마실 물이 없다. 옆 사람이 물 한 모금을 나눠 주었다.", "neighbor");
                ctx.Remember("shelter_water_received");
            }
        }

        static async Task AfterAlert(StoryContext ctx)
        {
            var g = ctx.G;
            var h = Relay.StoredHandover(g);
            ctx.SetLevelState("shelter", "ways", new JArray("stairs", "ramp", "access"));
            await ctx.Goto("shelter", "spot");
            ctx.SetLevelState("shelter", "crowd", "settled");
            ctx.SetLevelState("shelter", "seoyeon", "shown"); // runtime state: a retry or a new run hides her again
            var lines = h == null ? new[] { "정민 씨! 다행이다. 저는 C로 들어왔어요. 안내판에 C가 열렸다고 해서요." }
                : h.Kind == "generalized" ? new[] { "메시지 고마워요. 대피소가 닫혔다길래 잠깐 놀랐는데, 안내판엔 C가 열렸다고 해서 다시 봤어요." }
                : h.Kind == "precise" ? new[] { "메시지 고마워요. 언제 뭘 봤는지, 뭘 모르는지 써 줘서 판단하기 쉬웠어요. 저는 C로 왔어요." }
                : new[] { "메시지 고마워요. 저는 안내판 보고 C로 왔어요. 계단은 저한테 안 맞아서요." };
            await ctx.Say("오서연", lines, null, "대피소");
            g.Rel["seoyeon"].Trust += 1;

            void Later(int m) { g.Minute = Math.Max(g.Minute, m); ctx.Refresh(); } // 서연's walk may already be past these
            // Stamped with the clock as it is: after a late arrival and 서연's walk it can already be past 20:35.
            Later(20 * 60 + 35);
            await StoryCommon.EmergencyText(ctx, $"[위급재난문자] {Hhmm(g)} 연무동지역 경계경보로 변경, 대피소에서 나와 경계 태세를 유지하며 이동 가능 {StoryCommon.Area}", Hhmm(g), "위급재난문자");
            await StoryCommon.FactCard(ctx, "경계경보로 바뀌었습니다", new[] { "alert_downgrade", "alert_caution" });
            Later(Math.Max(20 * 60 + 50, g.Minute + 10));
            await StoryCommon.EmergencyText(ctx, $"[안전안내문자] {Hhmm(g)} 연무동지역 경보해제. 정부 안내방송을 계속 들어 주십시오 {StoryCommon.Area}", Hhmm(g), "안전안내문자");

            // Home together. The building is still dark; 서연 lives on the same floor.
            ctx.OutdoorEnv = "blackout";
            ctx.SetLevelState("town", "crowd", "sparse");
            await ctx.Goto("apartment", "lobby");
            ctx.Objective("집으로", "서연과 함께 3층까지 계단으로 올라갑니다.", new List<ObjectiveStep>());
            // An upgrade over the original (where she was only a voice here): 서연 stands at the stairs with her walker,
            // facing 정민, for this conversation.
            IStoryPerson sy = null;
            {
                var e = ctx.Engine;
                double fx = -Math.Sin(e.PlayerYaw), fz = -Math.Cos(e.PlayerYaw);
                try { sy = e.SpawnPerson("seoyeon", e.PlayerX + fx * 1.6, e.PlayerY, e.PlayerZ + fz * 1.6, e.PlayerYaw + Math.PI); } catch { sy = null; }
            }
            var pick = await ctx.Say("오서연", new[] { "엘리베이터가 안 되네요. 3층이니까 계단으로 천천히 갈게요." },
                new List<TalkChoice> { new("ask", "\"어떻게 도와드릴까요?\""), new("carry", "보행기를 들어 드릴게요"), new("go", "먼저 올라갈게요") }, "1층 계단 앞");
            if (pick == "ask")
            {
                await ctx.Say("오서연", new[] { "보행기를 접어서 제 옆에서 들어 주세요. 저는 난간 잡고 천천히 갈게요. 떨어져서 먼저 가지만 않으면 돼요." }, null, "1층 계단 앞");
                ctx.Stat("fatigue", 6); g.Rel["seoyeon"].Trust += 2;
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "ask", ["what"] = "어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다" });
                ctx.Toast(ctx.Facts.Text("people_ask"), "official", 6000);
            }
            else if (pick == "carry")
            {
                await ctx.Say("오서연", new[] { "아, 잠깐만요. 보행기는 제 옆에 있어야 해서요. 접어서 바로 옆에서 들어 주시면 좋겠어요." }, null, "1층 계단 앞");
                ctx.Stat("fatigue", 6); g.Rel["seoyeon"].Trust += 1;
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "seoyeon", ["kind"] = "carry", ["what"] = "계단에서 보행기를 옆에서 들어 주었다" });
            }
            else
            {
                await ctx.Say("오서연", new[] { "네, 천천히 갈게요. 소장님이 계단 비춰 주신대요." }, null, "1층 계단 앞");
            }
            if (sy != null) ctx.Engine.RemovePerson(sy);
            Later(21 * 60 + 20);
            ctx.SetLevelState("apartment", "elder304", "off"); // back home in 304 too
            await ctx.Goto("apartment", "home");
            // The bag comes off at the door: its weight no longer slows or tires the walk (Evac puts it on again).
            if (g.Knows("bagOn")) { g.SetKnow("bagOn", false); PrepActions.ApplyHome(ctx); ctx.Refresh(); ctx.Toast("배낭을 현관에 내려놓았다.", "self", 2600); }
            var sum = Debrief.ChapterSummary(ctx.Rules, g, "dday");
            await ctx.Modal(() => ctx.Ui.Open("chapterCard", new JObject
            {
                ["chapter"] = "2장", ["title"] = "정전과 경보", ["lines"] = new JArray(sum.Lines), ["payoffs"] = JArray.FromObject(sum.Payoffs),
            }));
        }
    }
}
