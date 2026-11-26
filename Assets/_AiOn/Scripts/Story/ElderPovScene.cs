// 2장 「304호의 계단」 (competition upgrade): the air-raid stairs seen by the person being helped. After 정민 signs in
// and sits down in the shelter, the game offers (once, skippable) the same minutes from 304호 어르신's side: the same
// siren, the same dark stairwell, the same exchange at her door ("같은 문장, 다른 사람"), walked at her pace with a rest
// on every landing. Who is with her and what she has in hand come from this run (Game/ElderPovLogic.cs): 정민's answer
// at the stairs (help / manager / go), the 「22분」 morning knock (a bag and a flashlight by her door since then) and the
// blackout's candle tip. Nobody is hurt and nothing goes wrong that someone does not set right; her inner lines are
// polite and her own ("천천히 가면 돼요"), never pitiful.
//
// Structure as 서연's part (SeoyeonRelay): a point-of-view card, SetPlayer("elder") and a slow SpeedOverride, a roam in
// the apartment with the clock held (Rate 0) and the war layer calm, then 정민 again in the shelter. The clock reads
// 20:05 on and is put back afterwards; the scene records one event (elder_pov) once 정민's clock is his again.
// The wheelchair profile cannot walk stairs in the engine: there the flights are taken one choice at a time (guided).
// Her body walks only (StoryContext.WalkOnly: no running, jumping or vaulting, whatever the profile), and 정민's own
// keys and panels (bag, phone, notebook, first aid, chalk, stats) wait while she is the player (StoryDirector.OtherPov).
// The optional scene is left out of the 2장 chapter time (StoryContext.BeginUntimed).
// The low-vision profile hears every line, choice and toast (StoryContext.Speak); sounds come as captions for the deaf profile.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class ElderPovScene
    {
        /// <summary>The story mark at the offer (the scene-select state is taken here).</summary>
        public const string StartMark = "elderpov";
        public const string Speaker = "304호 어르신", Inner = "어르신 (속마음)";
        const string Manager = "박영수 소장", Neighbour = "203호 이웃";

        /// <summary>Where she stands: a step out of 304's door, facing west down the corridor toward the stairs.</summary>
        public static readonly (double X, double Y, double Z, double Yaw) Door = (15.0, Cutscenes.F3, -0.95, Math.PI / 2);
        /// <summary>Her candle, just inside 304's door (the glow the cine fill light draws until she puts it out).</summary>
        static readonly (double X, double Y, double Z) CandleAt = (15.9, Cutscenes.F3 + 1.0, -0.45);
        /// <summary>Seconds of the fade to black before the point-of-view card (the shelter goes, her side comes).</summary>
        public const double PovFade = 0.45;

        /// <summary>Guided descent (wheelchair profile): the 3F stair top, then the landings 1..4 (ElderPovLogic.LandingAt).</summary>
        public static readonly (double X, double Y, double Z, double Yaw)[] Steps =
        {
            (4.15, 5.6, 0.8, Math.PI), (4.15, 4.2, 4.2, Math.PI), (5.85, 2.8, 0.9, 0), (4.15, 1.4, 4.2, Math.PI), (5.85, 0, 0.9, 0),
        };

        // ---------------------------------------------------------------- read aloud (low vision)

        static readonly Regex Tags = new Regex("<[^>]+>");

        static void Read(StoryContext ctx, string text)
        {
            if (ctx.Settings.Profile != "lowvision" || string.IsNullOrEmpty(text)) return;
            try { ctx.Speak?.Invoke(Tags.Replace(text, "")); } catch { /* no voice */ }
        }

        static Task<string> Say(StoryContext ctx, string speaker, IEnumerable<string> lines, List<TalkChoice> choices = null, string role = null)
        {
            var l = lines.ToList();
            var said = speaker + ". " + string.Join(" ", l);
            if (choices != null && choices.Count > 0) said += " 고르세요. " + string.Join(" ", choices.Select((c, i) => $"{i + 1}번, {c.Label}."));
            if (!ctx.UiReadsAloud) Read(ctx, said); // the host's talk box reads itself (StoryOverlay)
            return ctx.Say(speaker, l, choices, role);
        }

        static Task<string> Say(StoryContext ctx, string speaker, string line, List<TalkChoice> choices = null, string role = null) =>
            Say(ctx, speaker, new[] { line }, choices, role);

        static Task<string> Card(StoryContext ctx, CardOptions o)
        {
            if (!ctx.UiReadsAloud) Read(ctx, o.Title + ". " + string.Join(" ", o.Body ?? new List<string>()));
            return ctx.Card(o);
        }

        /// <summary>A toast that the low-vision profile also hears.</summary>
        static void Toast(StoryContext ctx, string text, int ms)
        {
            Read(ctx, text);
            ctx.Toast(text, "self", ms);
        }

        static async void Forget(Func<Task> run)
        {
            try { await run(); }
            catch (Exception) { /* a rest that could not play: the walk goes on */ }
        }

        // ---------------------------------------------------------------- the scene

        /// <summary>The offer (unless started from its prepared state), the point of view, and 정민 again in the shelter.</summary>
        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            if (!ElderPovLogic.Available(g)) return;
            ctx.Reached(StartMark);
            if (ctx.Resume != DDay.ElderResume)
            {
                var want = await Card(ctx, new CardOptions
                {
                    Kind = "decision", Title = ElderPovLogic.OfferTitle, Meta = "선택 · 약 3분",
                    Body =
                    {
                        "맞은편 벽 쪽에 304호 어르신이 박 소장님과 함께 자리를 잡으셨다.",
                        "같은 사이렌, 같은 계단을 어르신은 어떻게 내려오셨을까요? 어르신의 걸음으로 그 계단을 다시 내려가 봅니다.",
                    },
                    Actions = new List<CardAction> { new("view", "어르신의 걸음으로", true), new("skip", "건너뛰기") },
                });
                if (want == "skip")
                {
                    GameRules.RecordEvent(g, ElderPovLogic.Event, new JObject { ["played"] = false });
                    return;
                }
            }

            var s = ElderPovLogic.Read(g, ctx.LightSource);
            var r = new ElderPovResult { Pick = s.Pick, Way = s.Way, Helper = ElderPovLogic.Helper(s.Way), Prepared = s.Prepared, Guided = ctx.Settings.Profile == "wheelchair" };
            int day0 = g.Day, minute0 = g.Minute;
            string light0 = ctx.LightSource;
            bool calm0 = ctx.Calm;
            JToken elder0 = ctx.LevelStates.TryGetValue("apartment", out var apt) && apt.TryGetValue("elder304", out var ev) ? ev?.DeepClone() : null;
            var walk = new Walk(ctx, s, r);
            var untimed = ctx.BeginUntimed?.Invoke(); // optional: not part of the 2장 chapter time
            try
            {
                await Play(ctx, s, r, walk);
            }
            finally
            {
                walk.Dispose();
                ctx.WalkOnly?.Invoke(false);
                ctx.Audio.Loop("siren", false);
                ctx.Engine.SetFlashlight(false);
                ctx.Cine?.FillAt(0, -500, 0, 0);
                ctx.Engine.SetView(SeoyeonRelay.ViewSetting?.Invoke(ctx) ?? walk.View0 ?? "third");
                ctx.SpeedOverride = null;
                ctx.SetPlayer?.Invoke("jeongmin");
                ctx.LightSource = light0;
                ctx.Calm = calm0;
                ctx.SetLevelState("apartment", "elder304", elder0 ?? "off");
                g = ctx.G;
                g.Day = day0; g.Minute = minute0;
                ctx.Hud.SetCinematic(false);
                untimed?.Invoke();
                await walk.Unblack(0.35); // left before the apartment (a fail, the title): no black screen left behind
            }
            await ctx.Goto("shelter", "spot");
            ctx.Refresh(); // 정민's clock, bag and phone again
            ctx.Hud.SetChapter("2장", "정전과 경보");
            ctx.Objective("대피소 안에서", "정민은 대피소에 자리를 잡았습니다.", new[] { new ObjectiveStep("명부 쓰기 (접수대)", true), new ObjectiveStep("자리 잡기", true) });
            var o = r.ToJson(); o["played"] = true;
            GameRules.RecordEvent(ctx.G, ElderPovLogic.Event, o);
        }

        static async Task Play(StoryContext ctx, ElderPovSetup s, ElderPovResult r, Walk walk)
        {
            var g = ctx.G;
            bool deaf = ctx.Settings.Profile == "deaf";
            var body = new List<string>
            {
                "이제 304호 어르신입니다. 103동에 혼자 사시고, 귀가 조금 어두우시고, 계단은 난간을 잡고 천천히 내려가십니다.",
                "같은 사이렌, 같은 계단, 같은 대화입니다. 이번에는 어르신 쪽에서 듣고, 어르신의 걸음으로 내려갑니다.",
                "· 달리지 않고 천천히 걷습니다", "· 층계참마다 숨을 고릅니다",
            };
            if (r.Guided) body.Add("· 휠체어 체험 모드에서는 계단을 반 층씩 골라 내려갑니다");
            // The card says 20:05 at her door: it comes over black, not over the shelter (and 정민's later clock) behind it.
            // The black lifts with the apartment's own fade in (Goto below).
            walk.Black = true;
            await ctx.Engine.FadeTo(1, PovFade);
            await Card(ctx, new CardOptions
            {
                Kind = "info", Title = "시점 전환 · 304호 어르신", Meta = "20:05 · 103동 304호 앞", Body = body,
                Actions = new List<CardAction> { new("ok", "어르신으로", true) },
            });

            // Her side. The clock reads the siren's minute; 정민's own is put back afterwards.
            g.Minute = DDay.Alert;
            ctx.Hud.SetChapter("2장", "같은 문장, 다른 사람 · 304호");
            ctx.LightSource = null; // the director's F key and battery belong to 정민
            ctx.Calm = true;
            ctx.SetLevelState("apartment", "elder304", "off"); // she is the player now
            await ctx.Goto("apartment", "corridor3F");
            await walk.Unblack(0); // the level's fade has lifted it already; a host whose Enter does not fade gets the picture back here
            ctx.Engine.TeleportTo(Door.X, Door.Y, Door.Z, Door.Yaw);
            ctx.SetPlayer?.Invoke("elder");
            ctx.WalkOnly?.Invoke(true); // her own slow steps: no running, no jumping
            walk.View0 = ctx.Engine.View;
            ctx.Engine.SetView("third"); // her body on screen: who is walking (the player's own view comes back after)
            ctx.Cine?.FillAt(CandleAt.X, CandleAt.Y, CandleAt.Z, 1.3, "#ffb565", 4.5); // her candle's glow through the open door
            walk.SetPace(false);
            ctx.Refresh(); // the HUD drops 정민's bag and phone (StoryDirector.Pov)
            ctx.Audio.Loop("siren", true);
            walk.ShowGoal();

            await Say(ctx, Inner, deaf
                ? new[] { "창밖이 길게 웅웅 울려요. 휴대폰에 빨간 글씨가 번쩍여요. 사이렌이에요.", "정전에 사이렌까지… 계단이 캄캄하겠지요." }
                : new[] { "사이렌 소리… 귀가 예전 같지 않아도 이 소리는 들려요.", "정전에 사이렌까지… 계단이 캄캄하겠지요." }, null, "20:05 · 304호 현관");
            if (s.Met) await Say(ctx, Inner, "302호 정민 씨가 이사 와서 인사하러 왔을 때, 초인종 말고 문을 두드려 달라고 했지요.", null, "304호 현관");

            // the candle on the shoe cabinet
            if (s.CandleTip)
            {
                await Say(ctx, Inner, "신발장 위 촛불부터 껐어요. 아까 정민 씨가 자리 비울 땐 꼭 끄라고 했지요.", null, "304호 현관");
                r.Candle = "tip";
                walk.CandleOut();
            }
            else
            {
                var c = await Say(ctx, Inner, "신발장 위에 켜 둔 초가 흔들려요.", new List<TalkChoice>
                {
                    new("out", "촛불을 끄고 나선다"), new("leave", "금방 올 텐데, 그대로 둔다"),
                }, "304호 현관");
                r.Candle = c == "leave" ? "left" : "out";
                if (r.Candle == "out") { walk.CandleOut(); await Say(ctx, Inner, "후 — 불은 꺼 두는 게 마음이 편해요.", null, "304호 현관"); }
            }

            // what she takes
            if (s.Prepared)
            {
                await Say(ctx, Inner, new[]
                {
                    s.MorningDown ? "그날 아침 정민 씨랑 계단을 내려가 본 뒤로, 현관에 신발이랑 작은 가방, 손전등을 뒀어요." : "그날 아침 정민 씨가 문을 두드려 준 뒤로, 현관에 신발이랑 작은 가방, 손전등을 뒀어요.",
                    "가방을 메고 손전등을 켰어요. 약봉지도 신분증도 물 한 병도 다 들어 있어요.",
                }, null, "304호 현관");
                walk.LightOn();
            }
            else
            {
                var b = await Say(ctx, Inner, "무얼 챙길까요?", new List<TalkChoice>
                {
                    new("light", "약봉지·신분증·휴대폰만 챙긴다"), new("bundle", "장롱 속 사진첩과 보따리까지 챙긴다 (시간이 든다)"),
                }, "304호 현관");
                r.Bundle = b == "bundle";
                await Say(ctx, Inner, r.Bundle ? "약봉지랑 신분증에, 장롱 속 사진첩과 보따리까지 꺼내느라 한참 걸렸어요. 손이 무거워요." : "약봉지랑 신분증, 휴대폰만 주머니에 넣었어요. 손전등은… 어디 뒀더라. 찾을 새가 없어요.", null, "304호 현관");
            }
            walk.SetPace(false);

            // 정민 passes her door: the same exchange the player had, heard from her side
            var jm = walk.Spawn("jeongmin", Door.X - 2.3, Door.Z - 0.1, -Math.PI / 2);
            if (jm != null && g.Knows("bagOn")) jm.SetProp("backpack", true);
            jm?.LookAt(Door.X, Door.Z);
            await Say(ctx, Speaker, "\"302호 정민 씨예요? 사이렌이 울리는데 계단이 캄캄해서… 혼자 내려가기가 겁나네요.\"", null, "경보 중 · 3층 복도");
            string said = s.Pick == "help" ? "\"어르신, 부축해 드릴게요. 같이 천천히 내려가요.\""
                        : s.Pick == "manager" ? "\"1층 관리사무소에 알리고 먼저 갈게요. 소장님이 올라오실 거예요.\""
                        : "\"저 먼저 내려갈게요.\"";
            await Say(ctx, "정민", said, null, "3층 복도");
            await Say(ctx, Speaker, s.Pick == "help" ? "\"고마워요. 신발만 신을게요. 천천히 가요. 손잡이 쪽에 서 줘요.\""
                                  : s.Pick == "manager" ? "\"그래요, 소장님한테 말 좀 해 줘요. 나는 여기 손잡이 잡고 있을게요.\""
                                  : "\"그래요, 어서 가요. 나는 소장님 올라오면 같이 갈게요.\"", null, "304호 앞");

            if (s.Pick == "help")
            {
                await Say(ctx, Inner, "손잡이 쪽에 서 달라고 했어요. 한 손은 난간, 한 손은 정민 씨 팔. 그러면 무섭지 않아요.", null, "304호 앞");
                await Reminder(ctx, r, walk, "정민", "\"어르신, 현관 촛불은 끄고 가요. 제가 기다릴게요.\"");
                walk.Join(jm, "door");
            }
            else
            {
                walk.Leave(jm); // 정민 goes on to the stairs
                if (s.Way == "manager")
                {
                    var w = await Say(ctx, Inner, "소장님이 곧 오시겠지요. 그래도 가만히 서 있으니 마음이 더 급해요.", new List<TalkChoice>
                    {
                        new("wait", "손잡이를 잡고 소장님을 기다린다"), new("go", "난간을 잡고 한 층이라도 먼저 내려간다"),
                    }, "304호 앞");
                    r.Waited = w == "wait";
                    if (r.Waited)
                    {
                        await ctx.Modal(() => ctx.Caption("손잡이를 잡고 기다려요. 계단 쪽에서 손전등 불빛이 올라와요.", Inner, 2800));
                        var mg = walk.Spawn("manager", Door.X - 2.2, Door.Z - 0.1, -Math.PI / 2);
                        mg?.SetProp("flashlight", true);
                        await Say(ctx, Manager, "\"어르신! 정민 씨한테 들었어요. 같이 내려가요. 제가 손잡이 쪽에 설게요.\"", null, "304호 앞");
                        await Say(ctx, Speaker, "\"아이고, 소장님. 바쁘실 텐데 고마워요.\"", null, "304호 앞");
                        await Reminder(ctx, r, walk, Manager, "\"어르신, 현관 촛불은 끄고 나가요. 천천히 하세요.\"");
                        walk.Join(mg, "door");
                    }
                }
                else if (s.Way == "untold")
                {
                    // 정민 meant to tell 박 소장 and walked out past the window: nobody comes up. She decides for herself.
                    await Say(ctx, Inner, "소장님이 곧 오시겠지요. 손잡이를 잡고 기다려 볼게요.", null, "304호 앞");
                    await ctx.Modal(() => ctx.Caption("손잡이를 잡고 기다려요. 계단 쪽은 아직 조용해요.", Inner, 2800));
                    await Say(ctx, Inner, new[]
                    {
                        "소장님도 이 밤에 바쁘시겠지요. 기다리기만 할 게 아니라, 내 걸음으로 가 봐요.",
                        "난간만 놓지 않으면 돼요.",
                    }, null, "304호 앞");
                }
                else
                {
                    await Say(ctx, Inner, new[]
                    {
                        "젊은 사람은 먼저 가야지요.",
                        "소장님은 내가 여기 있는 줄 모르실 거예요. 기다리기만 할 게 아니라, 내 걸음으로 가 봐요. 난간만 놓지 않으면 돼요.",
                    }, null, "304호 앞");
                }
            }
            r.MeetAt = ElderPovLogic.MeetAt(s.Way, r.Waited);
            r.Light = walk.Light;
            g.Minute = DDay.Alert + (s.Prepared ? 1 : r.Bundle ? 4 : 2) + (r.Waited ? 3 : s.Way == "untold" ? ElderPovLogic.UntoldWait : 0);
            ctx.Refresh();

            if (r.Guided) await walk.Guided();
            else await walk.Roam();

            // 1F
            r.Rests = walk.Rests;
            r.Minutes = ElderPovLogic.Minutes(s, r.Waited, r.Bundle, r.Rests);
            g.Minute = Math.Max(g.Minute, DDay.Alert + r.Minutes);
            ctx.Refresh();
            walk.ShowGoal(true);
            walk.FaceAtFoot();
            if (r.Helper != "manager") walk.ManagerAtFoot(); // 박 소장 waits at the stair foot with his torch
            switch (r.Helper)
            {
                case "jeongmin":
                    await Say(ctx, Speaker, "\"다 왔네. 여기서부터는 소장님이랑 천천히 갈게요. 고마워요. 어서 가요.\"", null, "1층 계단 앞");
                    await Say(ctx, Inner, "정민 씨가 대피소 쪽으로 서둘러 가요. 고맙다는 말을 한 번 더 할 걸 그랬어요.", null, "1층");
                    break;
                case "manager":
                    await Say(ctx, Manager, "\"다 오셨어요. 대피소까지 제가 모실게요. 천천히 가요.\"", null, "1층 계단 앞");
                    await Say(ctx, Inner, "정민 씨가 소장님께 알려 줘서, 혼자가 아니었어요.", null, "1층");
                    break;
                default:
                    await Say(ctx, Manager, "\"어르신! 층마다 돌아보려고 올라가려던 참이었어요. 203호도 고마워요.\"", null, "1층 계단 앞");
                    await Say(ctx, Inner, "2층까지는 내 걸음으로 왔어요. 그다음은 이웃이 함께였고요.", null, "1층");
                    break;
            }
            if (r.Candle == "left")
            {
                // As 정민's own candle at his door (DDay homeExitDoor): nobody goes back up during the alert; the rule is said.
                await Say(ctx, Manager, "\"어르신, 304호 촛불은 끄고 나오셨어요?\"", null, "1층");
                await Say(ctx, Speaker, "\"아이고… 켜 두고 왔네요.\"", null, "1층");
                await Say(ctx, Manager, "\"지금은 다시 올라가지 말고 대피부터 해요. 경보가 풀리면 제가 304호부터 가 볼게요.\"", null, "1층");
                var rule = ctx.Facts.Text("candle_rules");
                if (!string.IsNullOrEmpty(rule)) { Read(ctx, rule); ctx.Toast(rule, "official", 6000); }
            }
            ctx.Audio.Loop("siren", false);
            await Card(ctx, new CardOptions
            {
                Kind = "record", Title = ElderPovLogic.ReviewTitle, Meta = "D-day 금 20:05 · 304호 어르신의 시점",
                Body = ElderPovLogic.ReviewLines(s, r, id => ctx.Facts.Text(id), id => ctx.Facts.Cite(id)?.Source),
                Actions = new List<CardAction> { new("ok", "정민에게로", true) },
            });
        }

        /// <summary>A candle still lit when someone walks with her from the door: they say so, and she puts it out.</summary>
        static async Task Reminder(StoryContext ctx, ElderPovResult r, Walk walk, string who, string line)
        {
            if (r.Candle != "left") return;
            await Say(ctx, who, line, null, "304호 앞");
            await Say(ctx, Speaker, "\"아이고, 그렇지요. 금방 끄고 올게요.\"", null, "304호 현관");
            walk.CandleOut();
            r.Candle = "reminded";
        }

        // ---------------------------------------------------------------- the walk down

        /// <summary>Her walk: the pace, the light, the companion who follows her trail, the rests on the landings.</summary>
        sealed class Walk : IDisposable
        {
            readonly StoryContext _ctx;
            readonly ElderPovSetup _s;
            readonly ElderPovResult _r;
            readonly HashSet<int> _rested = new();
            readonly List<IStoryPerson> _spawned = new();
            Follower _with;
            double _pace = -1;
            bool _busy, _darkSaid;

            public bool Light { get; private set; }
            /// <summary>The camera view before her part (put back after it when the director does not say).</summary>
            public string View0;
            /// <summary>The screen went black for the point-of-view card and has not been given back yet.</summary>
            public bool Black;

            /// <summary>Gives the picture back (over seconds) if the point-of-view card's black is still up.</summary>
            public async Task Unblack(double seconds)
            {
                if (!Black) return;
                Black = false;
                await _ctx.Engine.FadeTo(0, seconds);
            }
            bool _candle = true;
            double _fillX = double.NaN, _fillY, _fillZ;
            public int Rests => _rested.Count(n => n >= 1 && n <= ElderPovLogic.Landings);

            public Walk(StoryContext ctx, ElderPovSetup s, ElderPovResult r) { _ctx = ctx; _s = s; _r = r; }

            IStoryEngine E => _ctx.Engine;

            public IStoryPerson Spawn(string preset, double x, double z, double yaw)
            {
                IStoryPerson p = null;
                try { p = E.SpawnPerson(preset, x, E.PlayerY, z, yaw); } catch { p = null; }
                if (p != null) { p.SetAnim("idle"); _spawned.Add(p); }
                return p;
            }

            public void LightOn() { Light = true; E.SetFlashlight(true); }

            /// <summary>The candle is out: the cine fill becomes a faint light that keeps her readable in the dark (FollowFill).</summary>
            public void CandleOut() { _candle = false; _fillX = double.NaN; _ctx.Cine?.FillAt(0, -500, 0, 0); }

            /// <summary>
            /// A faint cool light above her (the stairwell is near black in the blackout): moved only when she has walked
            /// half a metre, so no work most frames. Left alone while her candle still glows at the door.
            /// </summary>
            void FollowFill()
            {
                if (_candle && E.PlayerY > 5) return;
                double x = E.PlayerX, y = E.PlayerY, z = E.PlayerZ;
                if (!double.IsNaN(_fillX) && Math.Abs(x - _fillX) + Math.Abs(z - _fillZ) + Math.Abs(y - _fillY) < 0.5) return;
                bool first = double.IsNaN(_fillX);
                _fillX = x; _fillY = y; _fillZ = z;
                _ctx.Cine?.FillAt(x, y + 2.1, z, 0.6, first ? "#aebfd0" : null, 4.5);
            }

            /// <summary>1F: 박 소장 at the stair foot, a little to the side, looking at her.</summary>
            /// <summary>
            /// Where someone stands to talk to her at the end: inside the stair foot (z 0.1..1.6, walls all round but the
            /// lobby door) beside her, or the lobby just past the stair door; out in the lobby, ahead of her.
            /// </summary>
            (double x, double z, double yaw) FootSpot(bool manager)
            {
                double px = E.PlayerX, pz = E.PlayerZ;
                double x, z;
                if (pz > 0) { x = manager ? 5.0 : px < 5 ? px + 1.3 : px - 1.3; z = manager ? -0.4 : 0.55; } // 박 소장 framed in the stair door
                else
                {
                    double yaw0 = E.PlayerYaw, fx = -Math.Sin(yaw0), fz = -Math.Cos(yaw0);
                    x = px + fx * (manager ? 2.1 : 1.4) + (manager ? fz * 0.7 : 0); z = pz + fz * (manager ? 2.1 : 1.4) - (manager ? fx * 0.7 : 0);
                }
                return (x, z, Math.Atan2(-(px - x), -(pz - z))); // facing her
            }

            public void ManagerAtFoot()
            {
                var (x, z, yaw) = FootSpot(true);
                var m = LevelManager();
                if (m != null) { _managerMoved = true; m.Visible = true; m.SetPosition(x, E.PlayerY, z, yaw); }
                else m = Spawn("manager", x, z, yaw);
                m?.SetProp("flashlight", true);
                m?.LookAt(E.PlayerX, E.PlayerZ);
            }

            // The level's own 박 소장 (at the 1F window): hidden while a 박 소장 walks with her, moved to the stair foot at the end.
            IStoryPerson _levelManager;
            (double X, double Z) _managerHome;
            bool _managerMoved, _managerHidden;
            IStoryPerson LevelManager()
            {
                if (_levelManager == null && E.LevelId == "apartment")
                {
                    _levelManager = E.Npc("manager");
                    if (_levelManager != null) _managerHome = (_levelManager.X, _levelManager.Z);
                }
                return _levelManager;
            }

            /// <summary>
            /// 1F: in the stair foot she turns to the lobby door (the steps she came down rise behind her and would pull
            /// the shoulder camera into her head), and the helper steps round to her side for the last words.
            /// </summary>
            public void FaceAtFoot()
            {
                if (E.LevelId == "apartment" && E.PlayerZ > 0 && Math.Abs(E.PlayerY) < 0.3) E.TeleportTo(E.PlayerX, E.PlayerY, E.PlayerZ, 0);
                var p = _with?.Person;
                if (p == null) return;
                var (x, z, yaw) = FootSpot(false);
                p.SetPosition(x, E.PlayerY, z, yaw);
                p.SetAnim("idle");
                p.LookAt(E.PlayerX, E.PlayerZ);
            }

            /// <summary>The helper walks with her from here (at her side, level with her); their light lights her steps.</summary>
            public void Join(IStoryPerson p, string where)
            {
                if (p == null) { _with = null; }
                else { _with = new Follower(E, p); p.LookAt(null, null); _with.Place(); } // at her side, arm in arm
                if (p != null && _r.Helper == "manager") { var lm = LevelManager(); if (lm != null) { lm.Visible = false; _managerHidden = true; } }
                if (ElderPovLogic.HelperLight(_r.Helper, _s.JeongminLight)) LightOn();
                SetPace(false);
            }

            /// <summary>정민 goes on ahead to the stairs (and out of sight).</summary>
            public void Leave(IStoryPerson p)
            {
                if (p == null) return;
                p.LookAt(null, null);
                p.SetAnim("walk", 1);
                _ = p.WalkTo(new List<(double x, double z)> { (6.2, -1.0), (5.0, -0.5), (5.0, 0.8) }, 1.8);
                _ctx.After(7, () => { p.Visible = false; });
            }

            public void SetPace(bool stairs)
            {
                double pace = ElderPovLogic.Pace(stairs, Light, _with != null, _r.Bundle);
                if (Math.Abs(pace - _pace) < 1e-6) return;
                _pace = pace;
                _ctx.SpeedOverride = pace / (_ctx.ProfileSpeed is double ps && ps > 0 ? ps : 1); // her pace, not the player's mode on top
                E.SpeedMul = pace;
            }

            const string Goal = "난간을 잡고 1층까지 천천히 내려갑니다. 층계참에서는 숨을 고릅니다.";
            const string DarkLine = "발끝으로 계단 끝을 더듬어요. 비상구 유도등 초록 불빛이 희미하게 비쳐요.";
            bool _goalRead;

            public void ShowGoal(bool down = false)
            {
                _ctx.Objective("304호 어르신 · 계단", Goal, new[]
                {
                    new ObjectiveStep("3층에서 계단으로", down || _rested.Count > 0), new ObjectiveStep("2층 층계참", down || _rested.Contains(2)), new ObjectiveStep("1층까지", down),
                });
                if (!_goalRead) { _goalRead = true; Read(_ctx, Goal); } // once, when her walk is first set out
            }

            static bool OnStairs(double x, double y, double z)
            {
                int i = Elder.ZoneAt(Elder.ApartmentWalk, x, z, y);
                return i >= 0 && Elder.ApartmentWalk[i].Axis != null;
            }

            /// <summary>The roam: down the corridor and the four flights; a rest on each landing; done on 1F.</summary>
            public Task<JToken> Roam() => _ctx.Roam(new RoamOptions
            {
                Rate = 0, Doors = false,
                On =
                {
                    ["elevator"] = _ => { Toast(_ctx, "정전이라 엘리베이터는 멈췄을 거예요. 계단으로 가야지요.", 3600); return Task.FromResult<JToken>(StoryContext.Handled); },
                    ["*"] = _ => { Toast(_ctx, "지금은 내려가는 게 먼저예요. 난간을 잡고 계단으로 가요.", 3200); return Task.FromResult<JToken>(StoryContext.Handled); },
                },
                Tick = dt =>
                {
                    _with?.Tick(dt);
                    FollowFill();
                    if (E.LevelId != "apartment" || _busy) return null;
                    double x = E.PlayerX, y = E.PlayerY, z = E.PlayerZ;
                    int n = ElderPovLogic.LandingAt(x, y, z);
                    if (n == 4) return "down";
                    bool stairs = OnStairs(x, y, z);
                    SetPace(stairs);
                    if (stairs && !Light && !_darkSaid)
                    {
                        _darkSaid = true;
                        Toast(_ctx, DarkLine, 4200);
                    }
                    if (n >= 1 && n <= ElderPovLogic.Landings && !_rested.Contains(n))
                    {
                        _rested.Add(n); _busy = true;
                        Forget(async () => { try { await _ctx.Modal(() => Landing(n)); } finally { _busy = false; } });
                    }
                    return null;
                },
            });

            /// <summary>The wheelchair profile: the corridor and each flight as a choice; the same landings and rests.</summary>
            public async Task Guided()
            {
                await Say(_ctx, Inner, "난간을 짚으며 계단 앞까지 걸어가요.", new List<TalkChoice> { new("go", "계단 앞으로") }, "3층 복도");
                Place(0);
                string[] flights =
                {
                    "3층에서 반 층 아래 층계참까지예요. 첫 계단이 제일 무서워요.", "2층까지 남은 반 층이에요.", "2층에서 아래로 가요. 다리에 힘을 줘요.", "마지막 반 층이에요. 1층 불빛이 보여요.",
                };
                for (int n = 1; n <= 4; n++)
                {
                    SetPace(true);
                    await Say(_ctx, Inner, flights[n - 1], new List<TalkChoice> { new("step", "난간을 잡고 한 칸씩 내려간다") }, "계단");
                    Place(n);
                    if (n == 1 && !Light) Toast(_ctx, DarkLine, 4200);
                    if (n <= ElderPovLogic.Landings) { _rested.Add(n); await _ctx.Modal(() => Landing(n)); }
                }
            }

            void Place(int i)
            {
                var p = Steps[i];
                E.TeleportTo(p.X, p.Y, p.Z, p.Yaw);
                _with?.Place();
                FollowFill();
            }

            /// <summary>A landing: she stops to catch her breath (the rest), 2F is where a later helper meets her.</summary>
            async Task Landing(int n)
            {
                var ctx = _ctx;
                bool with = _with != null;
                ShowGoal();
                ctx.G.Minute += 2; ctx.Refresh();
                int ms = (int)(ElderPovLogic.RestSeconds(Light, with) * 1000);
                if (n == 2 && _r.MeetAt == "2F" && _with == null)
                {
                    await ctx.Caption("2층이에요. 다리가 떨려도, 절반은 왔어요.", Inner, ms);
                    await Meet();
                    return;
                }
                string line = n switch
                {
                    1 => with ? "\"잠깐만요, 여기서 숨 좀 고르고 가요.\""
                       : Light ? "반 층 내려왔어요. 손전등 불빛 안에서 다음 계단 끝이 또렷해요. 숨부터 고를게요."
                       : "반 층 내려왔어요. 발끝으로 더듬어 내려오느라 다리가 떨려요. 서두르다 넘어지는 게 더 큰일이에요.",
                    2 => "2층이에요. 다리가 떨려도, 절반은 왔어요.",
                    _ => "조금만 더요. 아래층 불빛이 보여요.",
                };
                await ctx.Caption(line, with && n == 1 ? Speaker : Inner, ms);
                if (n == 1 && with)
                {
                    string reply = _r.Helper == "jeongmin" ? "\"네, 천천히 하세요. 기다릴게요.\"" : _r.Helper == "manager" ? "\"천천히요, 어르신. 시간 충분해요.\"" : "\"괜찮으세요? 천천히 가요.\"";
                    await ctx.Caption(reply, ElderPovLogic.HelperName(_r.Helper), 2000);
                    if (_r.Helper == "jeongmin" && _s.HeavyBag) await ctx.Caption("정민 씨 배낭이 무거워 보여요. 그래도 내 팔을 놓지 않네요.", Inner, 3000);
                }
            }

            /// <summary>On the 2F landing: 박 소장 coming up (manager, not waited) or the neighbour from 203호 (go).</summary>
            async Task Meet()
            {
                var ctx = _ctx;
                if (_r.Helper == "manager")
                {
                    var mg = Spawn("manager", 4.15, 0.7, -Math.PI / 2);
                    mg?.SetProp("flashlight", true);
                    mg?.LookAt(E.PlayerX, E.PlayerZ);
                    await Say(ctx, Manager, "\"어르신! 정민 씨한테 들었어요. 벌써 여기까지 오셨어요? 이제 같이 가요.\"", null, "2층 층계참"); // only when told (Way manager)
                    await Say(ctx, Speaker, "\"소장님 오시는 길 마중 나왔지요. 고마워요.\"", null, "2층 층계참");
                    Join(mg, "2F");
                }
                else
                {
                    var nb = Spawn("crowd3", 5.0, 0.35, Math.PI);
                    nb?.SetProp("phone", true);
                    nb?.LookAt(E.PlayerX, E.PlayerZ);
                    await Say(ctx, Neighbour, Light ? "\"어르신, 저 203호예요. 같이 내려가요.\"" : "\"어르신, 저 203호예요. 휴대폰 불빛으로 비춰 드릴게요. 같이 내려가요.\"", null, "2층 층계참");
                    await Say(ctx, Speaker, "\"고마워요. 내 걸음이 느려도 괜찮겠어요?\"", null, "2층 층계참");
                    await Say(ctx, Neighbour, "\"그럼요. 천천히 가요.\"", null, "2층 층계참");
                    Join(nb, "2F");
                }
            }

            public void Dispose()
            {
                if (_levelManager != null && (_managerMoved || _managerHidden))
                {
                    try
                    {
                        _levelManager.SetPosition(_managerHome.X, 0, _managerHome.Z, Math.PI / 2); // back at the 1F window (apartment-level.json)
                        _levelManager.SetProp("flashlight", false);
                        _levelManager.LookAt(null, null);
                        _levelManager.Visible = true;
                    }
                    catch { /* gone with the level */ }
                }
                foreach (var p in _spawned) { try { E.RemovePerson(p); } catch { /* gone with the level */ } }
                _spawned.Clear();
                _with = null;
            }
        }

        /// <summary>
        /// A helper walking with her: level with her and about 0.6 m to her left (her right where the left is off her walk
        /// zone), so the shoulder camera sees them both. Only where neither side fits (a doorway) do they
        /// fall in on her trail a step behind (as 어르신 follows 정민 in ElderDilemma, the other way round).
        /// </summary>
        sealed class Follower
        {
            readonly IStoryEngine _e;
            readonly IStoryPerson _p;
            List<Crumb> _crumbs = new();
            double _ny;
            // The side tried first: the shoulder camera sits over her right shoulder (as seen on screen), so the helper
            // walks on the other side, clear of the lens.
            const int Prefer = -1;

            /// <summary>How far to her side the helper walks.</summary>
            public const double Side = 0.6;

            public Follower(IStoryEngine e, IStoryPerson p) { _e = e; _p = p; _ny = e.PlayerY; }

            public IStoryPerson Person => _p;

            static double Sq(double x) => x * x;

            /// <summary>
            /// Pure: the spot at her side, or null when neither side is on the walk mesh at her height. side: the side to
            /// try first (+1 right, -1 left); the one used comes back. yaw 0 looks toward -z, so her right is (cos, -sin).
            /// </summary>
            public static (double x, double z, int side)? SideSpot(IReadOnlyList<StoryCine.WalkZone> walk, double px, double py, double pz, double yaw, int side)
            {
                int here = Elder.ZoneAt(walk, px, pz, py);
                double rx = Math.Cos(yaw), rz = -Math.Sin(yaw);
                for (int pass = 0; pass < 2; pass++)
                    foreach (var sd in new[] { side, -side })
                    {
                        double x = px + rx * Side * sd, z = pz + rz * Side * sd;
                        int zone = Elder.ZoneAt(walk, x, z, py);
                        if (zone < 0) continue;
                        if (pass == 0 && here >= 0 && zone != here) continue; // the same floor or flight first
                        return (x, z, sd);
                    }
                return null;
            }

            public void Place()
            {
                double py = _e.PlayerY;
                var spot = SideSpot(Elder.ApartmentWalk, _e.PlayerX, py, _e.PlayerZ, _e.PlayerYaw, Prefer);
                double bx, bz;
                if (spot.HasValue) { bx = spot.Value.x; bz = spot.Value.z; }
                else (bx, bz) = Elder.FollowSpot(Elder.ApartmentWalk, _e.PlayerX, py, _e.PlayerZ, _e.PlayerYaw);
                _ny = py; _p.SetPosition(bx, py, bz, _e.PlayerYaw);
                _crumbs = new List<Crumb>();
                _p.SetAnim("idle");
            }

            public void Tick(double dt)
            {
                if (_e.LevelId != "apartment") return;
                double px = _e.PlayerX, py = _e.PlayerY, pz = _e.PlayerZ, pyaw = _e.PlayerYaw;
                Crumb? last = _crumbs.Count > 0 ? _crumbs[^1] : (Crumb?)null;
                if (last is Crumb l0 && Math.Sqrt(Sq(l0.X - px) + Sq(l0.Y - py) + Sq(l0.Z - pz)) > 3) { _crumbs = new List<Crumb>(); last = null; }
                if (!(last is Crumb l) || Math.Sqrt(Sq(l.X - px) + Sq(l.Z - pz)) > 0.3 || Math.Abs(l.Y - py) > 0.3) _crumbs.Add(new Crumb(px, py, pz));
                if (_crumbs.Count > 400) _crumbs.RemoveRange(0, 100);

                // at her side when it fits, else her trail
                double tx, tz, ty = py;
                bool beside;
                var spot = SideSpot(Elder.ApartmentWalk, px, py, pz, pyaw, Prefer);
                if (spot.HasValue) { tx = spot.Value.x; tz = spot.Value.z; beside = true; }
                else if (Elder.FollowTarget(_crumbs) is Crumb t) { tx = t.X; tz = t.Z; ty = t.Y; beside = false; }
                else return;

                double gx = _p.X, gz = _p.Z;
                double dx = tx - gx, dz = tz - gz, d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 8 || Math.Abs(_ny - py) > 3) { Place(); return; }
                bool moving = false;
                double? yaw = null;
                if (d > 0.12)
                {
                    var v = Math.Min(d * 2.4, 2.2) * dt;
                    double nx = gx + dx / d * Math.Min(v, d), nz = gz + dz / d * Math.Min(v, d);
                    double nearNew = Math.Sqrt(Sq(nx - px) + Sq(nz - pz)), nearOld = Math.Sqrt(Sq(gx - px) + Sq(gz - pz));
                    // beside her the gap is the side step itself; on her trail keep the old minimum gap
                    if (beside || nearNew >= Elder.MinGap || nearNew > nearOld)
                    {
                        gx = nx; gz = nz; moving = d > 0.25;
                        yaw = beside ? pyaw : Math.Atan2(-dx, -dz); // walking with her, facing where she faces
                    }
                }
                else if (beside) yaw = pyaw;
                var ground = Elder.GroundAt(Elder.ApartmentWalk, gx, gz, _ny + 0.4, 1.2);
                _ny += ((ground ?? ty) - _ny) * Math.Min(1, dt * 10);
                _p.SetPosition(gx, _ny, gz, yaw);
                _p.SetAnim(moving ? "walk" : "idle", moving ? Math.Min(d * 2, 1.2) : 0);
            }
        }
    }
}
