// 1장 「22분」 (competition upgrade): a short interlude on the D-1 morning, before the planner's next slot. An adaptation of
// the 2023.5.31 Seoul 경계경보 오발령, told in the fictional 연무동: at 06:41 a 위급재난문자 asks to get ready to evacuate
// but gives no reason and no place, the shelter app does not load, the neighbours' chat fills with guesses. 정민 does
// one thing at a time (Game/TwentyTwoLogic.cs: each takes game minutes); 22 minutes after the text, at 07:03, the
// correction "오발령" arrives whatever 정민 was doing. The review card judges the ORDER of the response, never whether
// the alert was real, and blames no one. After the 2장 chapter card, "그날 22분이 남긴 것" sets that morning beside the
// real air-raid alert (AfterCard).
//
// A sub-scene of 1장, not a chapter (Prep.TwentyTwoPart; the chapter list and checkpoint keys are unchanged). Talk
// choices only, no roam: no timer, keyboard/pad/number keys, captions for sounds. The low-vision profile hears the
// texts, every talk with its numbered choices and the cards (Read -> StoryContext.Speak -> the host's voice).
// Choices under the speaker "판단" slow the world (DecisionMoment, StoryOverlay).
//
// The in-game 경계경보 follows today's rule (alert_levels: no siren, voice broadcast); the review card says so, and
// that in 2023 people woke to sirens (misfire_siren). The 2장 card puts the siren difference on the alert TYPE.
//
// Events: twentytwo { played, picks[], elapsed, verdict, next }, twentytwo_after. Know: ttNext (check | doubt).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class TwentyTwoScene
    {
        /// <summary>The story mark at the start of the interlude (the scene-select state is taken here).</summary>
        public const string StartMark = "twentytwo";

        static bool Deaf(StoryContext ctx) => ctx.Settings.Profile == "deaf";
        static string Clock(int minute) => GameRules.Hhmm(minute);

        // ---------------------------------------------------------------- read aloud (low vision)

        static readonly Regex Tags = new Regex("<[^>]+>");

        /// <summary>Low-vision profile: read this aloud (rich-text tags stripped).</summary>
        static void Read(StoryContext ctx, string text)
        {
            if (ctx.Settings.Profile != "lowvision" || string.IsNullOrEmpty(text)) return;
            try { ctx.Speak?.Invoke(Tags.Replace(text, "")); } catch { /* no voice */ }
        }

        /// <summary>ctx.Say, read aloud first: the speaker, the lines, then the numbered choices.</summary>
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

        static Task Text(StoryContext ctx, string text, string time)
        {
            Read(ctx, "위급재난문자. " + text);
            return StoryCommon.EmergencyText(ctx, text, time);
        }

        /// <summary>The interlude: from the night skip to 07:03 and the review, then the skip to the planner's 09:00.</summary>
        public static async Task Run(StoryContext ctx)
        {
            ctx.Reached(StartMark);
            var g = ctx.G;
            bool deaf = Deaf(ctx);
            await ctx.Skip("D-2 수 밤", "D-1 목 06:41", new[]
            {
                "밤새 뉴스 속보 알림이 몇 번 울렸다. 정민은 잠을 설쳤다.",
                deaf ? "새벽, 머리맡의 휴대폰이 번쩍이며 길게 떨었다." : "새벽, 머리맡의 휴대폰이 요란하게 울렸다.",
            });
            g = ctx.G;
            GameRules.Adjust(g, "fatigue", 6); GameRules.Adjust(g, "tension", 6); // the original's restless night
            // forward only: Prep leaves the clock in tonight's slot (D-2 evening), so this never rewinds
            if ((long)g.Day * 1440 + g.Minute < -1440L + TwentyTwoLogic.SentAt) { g.Day = -1; g.Minute = TwentyTwoLogic.SentAt; }
            ctx.OutdoorEnv = "day"; ctx.IndoorEnv = "interior"; ctx.Power = true;
            ctx.SetLevelState("apartment", "windowView", "day");
            if (ctx.Engine.LevelId != "apartment") await ctx.Goto("apartment", "home", "interior");
            else ctx.Engine.SetEnv("interior", 0.5);
            ctx.Hud.SetChapter("1장", "준비 — 「22분」");
            ctx.Refresh();

            await Text(ctx, TwentyTwoLogic.AlertSms(StoryCommon.Area), "06:41");
            ctx.Stat("tension", 8);
            await Say(ctx, "정민", new[]
            {
                "대피할 준비를 하라는데… 왜? 어디로?",
                deaf ? "창밖 건너편 동에서 사람들이 창문을 열고 밖을 내다본다. 무슨 방송이 나오는 모양이다. 휴대폰 말고는 아무 신호도 없다."
                     : "창밖 멀리서 확성기 방송이 울린다. 말소리가 울려 무슨 말인지 들리지 않는다. 사이렌은 울리지 않는다.",
            }, null, "302호 · 06:41");
            await Say(ctx, "103동 단톡방", new[]
            {
                "\"이거 뭐예요? 어디로 대피하라는 거예요?\"",
                "\"애들 깨워서 옷은 입혔는데 어디로 가요?\"",
                "\"오발령 아닌가요? 그냥 자요.\"",
                $"\"{RumorAnatomy.PhotoPostText}\" {RumorAnatomy.PhotoSeen}", // taken apart after the correction (루머 해부)
            }, null, "06:42 · 알림 37개");

            ctx.Pass(1); // a minute of reading the chat
            var run = new TwentyTwoRun { Deaf = deaf, Minute = TwentyTwoLogic.SentAt + 1 };
            while (!run.Over)
            {
                var offered = run.Available();
                string meta = $"{Clock(run.Minute)} · 문자를 받은 지 {run.Elapsed}분";
                var pick = await Say(ctx, "판단", new[] { run.Picks.Count == 0 ? "무엇부터 할까?" : "그다음은?" },
                    offered.Select(a => new TalkChoice(a.Id, a.Label)).ToList(), meta) ?? offered[0].Id;
                if (offered.All(a => a.Id != pick)) pick = offered[0].Id;
                int before = run.Minute;
                run.Pick(pick);
                int spent = Math.Min(run.Minute, TwentyTwoLogic.CorrectionAt) - before;
                if (spent > 0) ctx.Pass(spent);
                await Act(ctx, run, pick);
            }
            if (run.Down && run.Minute < TwentyTwoLogic.CorrectionAt)
                await ctx.Modal(() => ctx.Caption(deaf ? "지하주차장 입구 앞에 이웃 몇이 먼저 와 있다. 다들 휴대폰만 들여다본다." : "지하주차장 입구 앞에 이웃 몇이 먼저 와 있다. 다들 휴대폰만 들여다보며 웅성거린다.", null, 3000));
            g = ctx.G;
            g.Minute = Math.Max(g.Minute, TwentyTwoLogic.CorrectionAt);
            ctx.Refresh();
            if (run.CorrectionDuringLast) ctx.Toast(run.Down ? "계단을 내려가는 사이, 휴대폰이 다시 울렸다." : "그사이, 휴대폰이 다시 울렸다.", "self", 3600);
            await Text(ctx, TwentyTwoLogic.CorrectionSms(StoryCommon.Area), "07:03");
            ctx.Stat("tension", -6);
            await After(ctx, run);
        }

        /// <summary>What one pick shows (the clock already moved).</summary>
        static async Task Act(StoryContext ctx, TwentyTwoRun run, string id)
        {
            var g = ctx.G;
            bool deaf = run.Deaf;
            string where = $"302호 · {Clock(Math.Min(run.Minute, TwentyTwoLogic.CorrectionAt))}";
            switch (id)
            {
                case "app":
                    await Say(ctx, "휴대폰", g.Knows("shelterLocated")
                        ? new[] { "대피소 앱: \"접속자가 많아 연결이 지연되고 있습니다.\" 포털 검색도 넘어가지 않는다.", "어젯밤 지도앱에 저장해 둔 곳은 열린다: 연무 생활문화센터 지하주차장 (도보 약 4분)." }
                        : new[] { "대피소 앱: \"접속자가 많아 연결이 지연되고 있습니다.\" 포털 검색도 넘어가지 않는다.", "가장 가까운 지하라면… 단지 지하주차장이 있다." }, null, where);
                    break;
                case "official":
                    await Say(ctx, "재난방송", new[]
                    {
                        (deaf ? "화면 아래 자막: " : "") + "\"연무동 일대에 경계경보가 발령됐습니다. 발령 이유는 확인하고 있습니다. 대피할 준비를 하시고, 방송을 계속 들어 주십시오.\"",
                        "왜인지, 어디로 가라는지는 아직 없다. 하지만 지금 할 일은 알겠다: 준비하고, 방송을 듣는다.",
                    }, null, "거실 TV · 공식 안내 (가상)");
                    ctx.Note("경계경보: 발령 이유 확인 중 · 대피 준비 · 방송 계속 듣기 (재난방송)", "official");
                    break;
                case "call":
                    await Say(ctx, "휴대폰", deaf ? "영상통화가 연결 중에서 멈췄다가 끊긴다." : "신호가 가다가 뚝 끊긴다. 다시 걸어도 \"연결이 되지 않아…\"", null, where);
                    break;
                case "text":
                    await Say(ctx, "정민", "\"경보 문자 받았어. 확인 중이고, 준비해서 가까운 지하로 갈게. 답은 문자로.\" 한 통을 보냈다.", null, where);
                    break;
                case "redial":
                    await Say(ctx, "정민", "몇 번을 더 걸었다. 연결은 되지 않고, 시간만 갔다.", null, where);
                    ctx.Stat("tension", 4);
                    break;
                case "ready":
                    await Say(ctx, "정민", g.Knows("bagPacked")
                        ? "옷을 갈아입고 신발 끈을 맸다. 현관의 배낭을 멨다."
                        : "옷을 갈아입고 신발 끈을 맸다. 배낭은 아직 꾸리지 못해, 물 한 병과 충전기만 챙겨 현관에 두었다.", null, where);
                    break;
                case "chat":
                    await Say(ctx, "103동 단톡방", new[]
                    {
                        "정민: \"전쟁 난 거 아니에요?\"",
                        "\"진짜요?? 어디서 들었어요?\" \"애들 데리고 어디로 가야 해요?\" 답이 줄줄이 달린다.",
                    }, null, "단톡방 · " + Clock(Math.Min(run.Minute, TwentyTwoLogic.CorrectionAt)));
                    ctx.Stat("tension", 4);
                    break;
                case "elder":
                    await AtHall(ctx, async () =>
                    {
                        await Say(ctx, "304호 어르신", new[]
                        {
                            deaf ? "문이 열리고, 어르신이 휴대폰 화면을 내민다. \"문자가 왔는데 글씨가 작아서… 뭐라는 거예요?\""
                                 : "\"누구세요? …아, 302호. 문자가 왔는데 글씨가 작아서… 뭐라는 거예요?\"",
                        }, null, "304호 앞");
                        await Say(ctx, "정민", "\"대피할 준비를 하래요. 어르신 같은 분들이 먼저 대피하시래요.\"", null, "304호 앞");
                        await Say(ctx, "304호 어르신", "\"아이고, 그럼 신발 신을게요. 혼자는 계단이 무서운데, 같이 가 줄 거죠?\"", null, "304호 앞");
                    });
                    break;
                case "down":
                    await Say(ctx, "정민", run.WithElder
                        ? new[] { "어르신 손을 잡고 계단으로 천천히 내려간다. 엘리베이터 앞에는 사람들이 줄을 서 있다." }
                        : new[] { "계단으로 단지 지하주차장까지 내려간다. 엘리베이터 앞에는 사람들이 줄을 서 있다." }, null, "103동 계단");
                    await ctx.Goto("apartment", "lobby", "interior"); // 1층, on the way to the car park's door
                    if (g.Knows("shelterLocated")) ctx.Toast("지정 대피소는 생활문화센터 지하주차장. 안내가 나오면 그리로 옮기자.", "self", 4200);
                    break;
                case "listen":
                    if (run.WithElder)
                        await AtHall(ctx, () => Say(ctx, "정민", deaf ? "어르신 댁 TV를 켜고, 신발을 신은 채 함께 재난방송 자막을 지켜봤다." : "어르신 댁 TV를 켜고, 신발을 신은 채 함께 재난방송을 들었다.", null, "304호 현관"));
                    else
                        await Say(ctx, "정민", deaf ? "신발을 신은 채 TV 앞에서 재난방송 자막을 계속 지켜봤다. 휴대폰도 손에 쥐었다." : "신발을 신은 채 TV 앞에서 재난방송을 계속 들었다. 휴대폰도 손에 쥐었다.", null, "302호 거실");
                    break;
                case "sleep":
                    await Say(ctx, "정민", "이불을 다시 덮었다. 잠은 오지 않았다. 휴대폰 화면만 몇 번이고 켰다 껐다.", null, "302호 침대");
                    break;
            }
        }

        /// <summary>The 3F corridor from 302's door toward 304, 어르신 at her door (as at the 2장 blackout's look out), for the body.</summary>
        static async Task AtHall(StoryContext ctx, Func<Task> body)
        {
            var hall = DDay.HallLook(ctx);
            if (hall != null)
            {
                ctx.SetLevelState("apartment", "elder304", "on");
                ctx.Hud.SetCinematic(true);
                ctx.Engine.SetCameraOverride(hall);
            }
            try { await body(); }
            finally
            {
                if (hall != null) { ctx.Engine.SetCameraOverride(null); ctx.Hud.SetCinematic(false); ctx.SetLevelState("apartment", "elder304", "off"); }
            }
        }

        /// <summary>After the correction: the neighbours, the decision for next time, the review card, the record and the skip to 09:00.</summary>
        static async Task After(StoryContext ctx, TwentyTwoRun run)
        {
            var g = ctx.G;
            bool deaf = run.Deaf;
            ctx.Pass(1); // the HUD clock keeps up with the chat's times
            await Say(ctx, "103동 단톡방", new[]
            {
                "\"오발령이래요. 왜 그랬는지는 안 나와 있네요.\"",
                "\"택시 타고 가던 중이었어요. 허탈하네.\"",
                "\"다음엔 문자 와도 그냥 잘래요.\"",
                RumorAnatomy.PhotoCorrection, // the poster checked the photo's original post (not another '~래요')
            }, null, Clock(ctx.G.Minute));
            if (run.WithElder)
            {
                // the thanks fits what 정민 did after knocking: went down with her, waited with her, or did not come back
                string hold = deaf ? "어르신이 정민의 손을 꼭 잡았다 놓는다. " : "";
                if (run.Down) await Say(ctx, "304호 어르신", hold + "\"그래도 같이 나와 줘서 고마워요. 다음에도 이렇게 해 줘요.\"", null, "1층 · 지하주차장 입구");
                else if (run.Listened) await AtHall(ctx, () => Say(ctx, "304호 어르신", hold + "\"그래도 같이 기다려 줘서 고마워요. 다음에도 이렇게 해 줘요.\"", null, "304호 현관"));
                else await AtHall(ctx, () => Say(ctx, "304호 어르신", new[] { "어르신은 신발을 신은 채 문 앞에서 기다리고 계셨다.", "\"그래도 와 줘서 고마워요. 다음엔 같이 내려가요.\"" }, null, "304호 앞"));
            }
            ctx.Pass(1);
            var next = await Say(ctx, "판단", new[] { "다음에 또 경보 문자가 오면?" }, new List<TalkChoice>
            {
                new("check", "그래도 먼저 확인하고, 준비하고, 어르신부터 챙긴다"),
                new("doubt", "또 오발령이겠지. 다음엔 그냥 잔다"),
            }, Clock(ctx.G.Minute)) ?? "check";
            if (next != "doubt") next = "check";
            if (run.Down)
            {
                await ctx.Goto("apartment", "home", "interior");
                ctx.Toast(run.WithElder ? "어르신을 304호까지 모셔다 드리고 집으로 돌아왔다." : "집으로 돌아왔다.", "self", 3600);
            }
            if (next == "doubt")
                await Say(ctx, "정민", "…그런데 다음 문자가 진짜라면? 오발령인지 아닌지는 받는 순간엔 알 수 없다.", null, "302호");
            g.SetKnow(TwentyTwoLogic.NextKnow, next);
            var result = run.Result(next);
            GameRules.RecordEvent(g, TwentyTwoLogic.Event, result);

            await Card(ctx, new CardOptions
            {
                Kind = "record", Title = TwentyTwoLogic.ReviewTitle, Meta = TwentyTwoLogic.ReviewMeta, Body = ReviewLines(ctx, run.Picks),
                Actions = new List<CardAction> { new("ok", "확인", true) },
            });
            // 루머 해부: the photo post, answered by its poster. After the review, so the elder's thanks and the order of
            // response land first; the side lesson comes last, before the day moves on.
            await ctx.Modal(() => PrebunkScene.Show(ctx, RumorAnatomy.PhotoPost));
            await ctx.Skip("D-1 목 아침", "D-1 목 09:00", new[] { "아침 뉴스는 오발령 이야기로 가득했다. 정민은 거실 벽의 달력 앞에 섰다." });
            ctx.Hud.SetChapter("1장", "준비 — 남은 시간 여섯 칸");
        }

        /// <summary>The review card's lines: the frame, the order played, a row per criterion with its sourced fact, the verdict.</summary>
        public static List<string> ReviewLines(StoryContext ctx, IReadOnlyList<string> picks)
        {
            var lines = new List<string>
            {
                "경보가 진짜였는지가 아니라, 22분 동안 무엇을 어떤 순서로 했는지 돌아봅니다.",
                "내 순서: " + (picks.Count == 0 ? "없음" : string.Join(" → ", picks.Select(TwentyTwoLogic.Short))),
                "권하는 순서(이 장면의 제안): " + TwentyTwoLogic.Recommended,
            };
            foreach (var r in TwentyTwoLogic.Rows(picks))
            {
                var f = ctx.Facts.Text(r.Fact);
                var mark = r.Ok ? "<color=#3DC7B5>✓</color> " : "<color=#F2B33D>○</color> ";
                lines.Add(mark + r.Label + (r.Ok || string.IsNullOrEmpty(f) ? "" : $" <size=18><color=#94a3b8>{f}</color></size>"));
            }
            lines.Add(TwentyTwoLogic.VerdictLine(TwentyTwoLogic.Verdict(picks)));
            // the sourced footer: that morning in 2023 (the times, the app, the sirens), and the rule this scene follows
            var real = new[] { "misfire_times", "misfire_app", "misfire_siren" }.Select(id => ctx.Facts.Cite(id)).Where(c => c != null).ToList();
            if (real.Count > 0)
                lines.Add($"<size=18><color=#8fd9cf>그날 실제로는 ▸</color> <color=#94a3b8>{string.Join(" ", real.Select(c => c.Text))} — {string.Join(" · ", real.Select(c => c.Source).Distinct())}</color></size>");
            var rule = ctx.Facts.Cite("alert_levels");
            if (rule != null) lines.Add($"<size=18><color=#8fd9cf>이 장면의 경보는 지금 규정을 따른다 ▸</color> <color=#94a3b8>{rule.Text} — {rule.Source}</color></size>");
            return lines;
        }

        // ---------------------------------------------------------------- 2장: the card after the chapter card

        /// <summary>Played the interlude this run (the 2장 card follows the chapter card only then).</summary>
        public static bool Played(GameState g) => TwentyTwoLogic.PicksOf(g) != null;

        /// <summary>"그날 22분이 남긴 것": that morning beside tonight's alert (once, after the 2장 chapter card).</summary>
        public static async Task AfterCard(StoryContext ctx)
        {
            var g = ctx.G;
            if (!Played(g) || g.Events.Any(e => e.Type == TwentyTwoLogic.AfterEvent)) return;
            var lines = AfterLines(g, id => ctx.Facts.Text(id), id => ctx.Facts.Cite(id)?.Source);
            await Card(ctx, new CardOptions
            {
                Kind = "record", Title = TwentyTwoLogic.AfterTitle, Meta = "D-1 목 아침과 D-day 금 밤", Body = lines,
                Actions = new List<CardAction> { new("ok", "계속", true) },
            });
            GameRules.RecordEvent(g, TwentyTwoLogic.AfterEvent, new JObject());
        }

        /// <summary>The card's lines (pure over the game and the facts, so the tests read them).</summary>
        public static List<string> AfterLines(GameState g, Func<string, string> fact, Func<string, string> source)
        {
            var picks = TwentyTwoLogic.PicksOf(g) ?? new List<string>();
            var lines = new List<string>
            {
                "그날 아침 문자에는 이유도, 어디로 가라는 말도 없었다. 오늘 밤 문자에는 무엇 때문에(미사일 발사), 어디로(지하시설 또는 건물 안)가 들어 있었다.",
                "그날 아침은 경계경보라 사이렌 없이 문자와 방송뿐이었고, 오늘 밤은 공습경보라 사이렌이 1분 동안 울렸다. 사이렌이 다른 것은 경보의 종류가 달라서다.",
            };
            var alert = g.Events.FirstOrDefault(e => e.Type == "alert");
            var arrived = g.Events.FirstOrDefault(e => e.Type == "arrived");
            string tonight = "";
            if (alert != null && arrived != null)
            {
                int a = (int?)alert.Details?["minute"] ?? alert.Minute, b = (int?)arrived.Details?["minute"] ?? arrived.Minute;
                tonight = $" 오늘 밤에는 경보 {Math.Max(0, b - a)}분 만에 대피소에 닿았다.";
            }
            lines.Add(TwentyTwoLogic.FirstLine(picks) + tonight);
            bool morning = picks.Contains("elder"), together = morning && picks.Contains("down");
            string pick = g.Know != null && g.Know.TryGetValue("elderPick", out var p) && p?.Type == JTokenType.String ? (string)p : null;
            string thatMorning = together ? "그날 아침에는 함께 내려갔고" : "그날 아침에는 먼저 문을 두드렸고";
            if (together && pick == "help") lines.Add("304호 어르신: 그날 아침에도, 오늘 밤에도 함께 내려갔다.");
            else if (morning && pick == "help") lines.Add($"304호 어르신: {thatMorning}, 오늘 밤에는 함께 내려갔다.");
            else if (morning && pick == "manager") lines.Add($"304호 어르신: {thatMorning}, 오늘 밤에는 박 소장님께 부탁했다.");
            else if (morning) lines.Add("304호 어르신: 그날 아침에는 먼저 챙겼다. 오늘 밤에는 먼저 내려왔다.");
            else if (pick == "help") lines.Add("304호 어르신: 그날 아침에는 지나쳤지만, 오늘 밤에는 함께 내려갔다.");
            string next = g.Know != null && g.Know.TryGetValue(TwentyTwoLogic.NextKnow, out var n) && n?.Type == JTokenType.String ? (string)n : null;
            if (next == "doubt") lines.Add("\"다음엔 그냥 자야지\" 했던 아침이 있었다. 받는 순간에는 진짜인지 알 수 없었다.");
            else if (next == "check") lines.Add("\"다음에도 먼저 확인하고 준비하자\" — 그 아침의 다짐이 있었다.");
            var fix = fact?.Invoke("alert_text_fix");
            if (!string.IsNullOrEmpty(fix)) lines.Add($"<color=#8fd9cf>그 뒤에 바뀐 것 ▸</color> {fix}");
            var clarify = fact?.Invoke("alert_clarify");
            if (!string.IsNullOrEmpty(clarify)) lines.Add($"<color=#8fd9cf>잘못 발령되면 ▸</color> {clarify}");
            var levels = fact?.Invoke("alert_levels");
            if (!string.IsNullOrEmpty(levels)) lines.Add($"<color=#8fd9cf>경보 소리 ▸</color> {levels}");
            var src = new[] { "alert_text_fix", "alert_clarify", "alert_levels" }.Select(id => source?.Invoke(id)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (src.Count > 0) lines.Add($"<size=18><color=#94a3b8>출처: {string.Join(" · ", src)}</color></size>");
            return lines;
        }
    }
}
