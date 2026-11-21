// 4장 「임시 거처의 하루」 (competition upgrade). For whoever chose to move to the 생활문화센터 temporary stay (D+6), the
// original skipped straight to the next evening's fire ("하루가 지났다"). This is that day: the first night (the board's
// notice, the charging outlet, a boundary for the mat, lights-out with its noise and light) and the next morning (the
// board's changed rule, the water line, the neighbour's child). Every choice answers in one short line and moves on;
// nothing fails. The HUD's 긴장 row reads 마음 for the scene (ctx.MindMeter), and the breathing screen ("breathe")
// lowers it a little. Rules and wording: Game/ShelterLifeLogic.cs. Recorded as one "shelter_life" event (the deeds
// shelter_* and the care / report endings read it), then a review card (kind record).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class ShelterLifeScene
    {
        /// <summary>The story mark at the start (the scene-select state "shelterlife" is taken here).</summary>
        public const string StartMark = "shelter_life";

        /// <summary>Played this run (once: the 4장 checkpoint after it keeps it).</summary>
        public static bool Played(GameState g) => g.Events.Any(e => e.Type == ShelterLifeLogic.Event);

        static bool Deaf(StoryContext ctx) => ctx.Settings.Profile == "deaf";
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

        static Task<string> Card(StoryContext ctx, CardOptions o)
        {
            if (!ctx.UiReadsAloud) Read(ctx, o.Title + ". " + string.Join(" ", o.Body ?? new List<string>()));
            return ctx.Card(o);
        }

        static string Mind(StoryContext ctx) => $"마음: {ShelterLifeLogic.MindWord(ctx.G.Stats.Tension)}";

        /// <summary>The answer in one toast, with how the 마음 moved.</summary>
        static void Feedback(StoryContext ctx, string text, double before)
        {
            var line = $"{text} ({ShelterLifeLogic.MindLine(before, ctx.G.Stats.Tension)})";
            ctx.Toast(line, "self", 6200);
            Read(ctx, line);
        }

        /// <summary>A choice from one of the logic's tables: the tension moves by the answer's amount, the feedback follows.</summary>
        static async Task<LifeChoice> Choose(StoryContext ctx, string speaker, IEnumerable<string> lines, IReadOnlyList<LifeChoice> set, string role,
            Func<LifeChoice, string> disabledWhy = null)
        {
            var choices = set.Select(c => { var why = disabledWhy?.Invoke(c); return new TalkChoice(c.Id, c.Label, why != null, why); }).ToList();
            var id = await Say(ctx, speaker, lines, choices, role);
            var pick = ShelterLifeLogic.Find(set, id) ?? set.First(c => disabledWhy?.Invoke(c) == null);
            double before = ctx.G.Stats.Tension;
            if (pick.Tension != 0) ctx.Stat("tension", pick.Tension);
            if (pick.Id != "breathe") Feedback(ctx, pick.Feedback, before);
            return pick;
        }

        /// <summary>The breathing screen's options (UI/Screens/BreatheScreen.cs).</summary>
        public static JObject BreatheOptions(StoryContext ctx, string with)
        {
            var profile = ctx.Settings.Profile ?? "";
            var facts = new JObject();
            foreach (var id in new[] { "mind_breath", "mind_call" })
            {
                var c = ctx.Facts.Cite(id);
                if (c != null) facts[id] = new JObject { ["text"] = c.Text, ["source"] = c.Source };
            }
            return new JObject
            {
                ["profile"] = profile, ["reduceMotion"] = ctx.Settings.ReduceMotion, ["with"] = with ?? "",
                ["mind"] = ShelterLifeLogic.MindWord(ctx.G.Stats.Tension),
                // the screen shows the 마음 settling as each breath finishes (the drop itself is applied when it closes)
                ["tension"] = ctx.G.Stats.Tension,
                // no holding for the bodies that find a long press hard (as the CPR screen's one-button default); changeable on the screen
                ["toggle"] = profile == "mobility" || profile == "wheelchair" || profile == "guardian",
                // wider windows for every experience mode (the pace itself never slows)
                ["wide"] = profile.Length > 0,
                ["facts"] = facts,
            };
        }

        /// <summary>The breathing screen; returns the breaths finished (0 = stopped before one was done).</summary>
        static async Task<int> Breathe(StoryContext ctx, ShelterLifeResult r, string with)
        {
            double before = ctx.G.Stats.Tension;
            var res = await ctx.Modal(() => ctx.Ui.Open(ShelterLifeLogic.Screen, BreatheOptions(ctx, with)));
            var b = (res as JObject)?["breaths"];
            int n = b != null && (b.Type == JTokenType.Integer || b.Type == JTokenType.Float) ? Math.Max(0, (int)(double)b) : 0;
            r.Breaths += n;
            if (n > 0) ctx.Stat("tension", -ShelterLifeLogic.BreathDrop(n));
            var line = n == 0 ? (with == "child" ? ShelterLifeLogic.PlayStay.Feedback : "숨 고르기는 언제든 다시 할 수 있다.")
                : with == "child" ? ShelterLifeLogic.Find(ShelterLifeLogic.Play, "breathe").Feedback
                : ShelterLifeLogic.Find(ShelterLifeLogic.Sleep, "breathe").Feedback;
            Feedback(ctx, line, before);
            return n;
        }

        public static async Task Run(StoryContext ctx)
        {
            if (Played(ctx.G)) return;
            ctx.Reached(StartMark);
            var r = new ShelterLifeResult { Played = true };
            var untimed = ctx.BeginUntimed?.Invoke(); // optional (the move only): not part of the 4장 chapter time, as ElderPovScene
            try { await Day(ctx, r); }
            finally { untimed?.Invoke(); }
        }

        static async Task Day(StoryContext ctx, ShelterLifeResult r)
        {
            await ctx.Skip("D+6 목", "D+6 목 18:40", new[] { "짐을 꾸려 생활문화센터 임시 거처로 옮겼다. 돗자리 한 장과 담요 한 장이 한 사람 몫이다." });
            var g = ctx.G;
            g.Day = 6; g.Minute = ShelterLifeLogic.ArriveMinute;
            ctx.SetLevelState("shelter", "crowd", "settled");
            ctx.SetLevelState("shelter", "phase", "calm");
            ctx.SetLevelState("shelter", "power", "emergency");
            ctx.SetLevelState("shelter", "fire", "none");
            ctx.SetLevelState("shelter", "ways", new JArray("stairs", "ramp", "access"));
            // 서연 waits for the move support at home, and 304호 어르신 is not here either: neither stands by the mat this time
            ctx.SetLevelState("shelter", "seoyeon", "hidden");
            ctx.SetLevelState("shelter", "elder", "off");
            await ctx.Goto("shelter", "spot");
            ctx.Refresh();
            r.MindStart = ctx.G.Stats.Tension;
            ctx.MindMeter?.Invoke(true);
            try
            {
                await Card(ctx, new CardOptions
                {
                    Kind = "notice", Title = "임시 거처 첫날 밤", Meta = "D+6 목 18:40 · 연무 생활문화센터 지하주차장",
                    Body =
                    {
                        "주차장 바닥에 돗자리가 줄지어 깔렸다. 옆 돗자리와의 사이는 한 걸음이 채 안 된다.",
                        "왼쪽 아래 '마음'은 정민이 얼마나 불안한지 보여 줍니다. 돗자리에서 숨을 고를 수 있습니다.",
                        "이곳의 안내는 날마다 바뀝니다. 게시판을 먼저 보면 헤매지 않습니다.",
                    },
                    Actions = new List<CardAction> { new("ok", "둘러보기", true) },
                });
                await Night(ctx, r);
                await Morning(ctx, r);
                r.MindEnd = ctx.G.Stats.Tension;
                GameRules.RecordEvent(ctx.G, ShelterLifeLogic.Event, r.ToJson());
                ctx.Refresh(); // the deeds' "will remember" cue
                await Card(ctx, new CardOptions
                {
                    Kind = "record", Title = ShelterLifeLogic.ReviewTitle, Meta = ShelterLifeLogic.ReviewMeta,
                    Body = ShelterLifeLogic.ReviewLines(r, id => ctx.Facts.Text(id), id => ctx.Facts.Cite(id)?.Source, Deaf(ctx)),
                    Actions = new List<CardAction> { new("ok", "계속", true) },
                });
            }
            finally { ctx.MindMeter?.Invoke(false); }
        }

        static Task<JToken> Toast(StoryContext ctx, string text, string label = "site")
        {
            ctx.Toast(text, label, 3600);
            Read(ctx, text);
            return Task.FromResult<JToken>(null);
        }

        /// <summary>The radio and the way out answer the same both times.</summary>
        static void Common(StoryContext ctx, Dictionary<string, Func<StoryPoint, Task<JToken>>> on)
        {
            on["shelterRadio"] = async _ =>
            {
                await Say(ctx, "공용 라디오", new[] { "\"연무구청이 알려 드립니다. 임시 거처의 급수와 급식은 계속 지원합니다. (가상 방송)\"", ctx.Facts.Text("shelter_rumor") }, null, "재난방송");
                return null;
            };
            on["shelterExit"] = _ => Toast(ctx, "밤낮으로 드나들면 다른 사람의 잠을 깨운다. 지금은 안에 머문다.");
        }

        static async Task Night(StoryContext ctx, ShelterLifeResult r)
        {
            bool board = false, outlet = false;
            List<ObjectiveStep> Steps() => new()
            {
                new ObjectiveStep("게시판 안내 읽기", board), new ObjectiveStep("휴대폰 충전 (접수대 옆 멀티탭)", outlet), new ObjectiveStep("돗자리 정리하고 잠자리 들기", false),
            };
            void Show() => ctx.Objective("임시 거처 첫날 밤", "게시판을 읽고, 충전하고, 자리를 정리합니다. 다 하지 않아도 잠자리에 들 수 있습니다.", Steps());
            Show();
            var on = new Dictionary<string, Func<StoryPoint, Task<JToken>>>
            {
                ["shelterBoard"] = async _ =>
                {
                    await Say(ctx, "대피소 게시판", ShelterLifeLogic.NightNotice, null, ShelterLifeLogic.NightNoticeTitle);
                    board = true; Show();
                    return null;
                },
                ["reception"] = async _ =>
                {
                    if (outlet) return await Toast(ctx, "충전은 오늘 한 번 했다. 지금은 다른 사람 차례다.");
                    var g = ctx.G;
                    var lines = new List<string> { "구멍 네 개짜리 멀티탭 앞에 충전기를 든 사람들이 줄을 섰다.", "내 차례가 왔다. 바로 뒤의 할머니는 휴대폰이 꺼져 있다고 한다." };
                    if (!board) lines.Add("(충전 규칙은 게시판에 적혀 있다고 한다.)");
                    var pick = await Choose(ctx, "접수대 옆 멀티탭", lines, ShelterLifeLogic.Outlet, $"휴대폰 {Math.Round(g.Phone.Battery)}%");
                    r.Outlet = pick.Id; outlet = true;
                    g = ctx.G;
                    g.Phone.Battery = Math.Min(100, g.Phone.Battery + ShelterLifeLogic.Charge(pick.Id));
                    ctx.Pass(pick.Id == "yield" ? 40 : 30);
                    Show();
                    return null;
                },
                ["spot"] = async _ =>
                {
                    if (r.Privacy == null)
                    {
                        var p = await Choose(ctx, "내 돗자리", new[] { "옆 가족과의 사이에 아무것도 없다. 오가는 사람이 머리맡을 지나간다." }, ShelterLifeLogic.Privacy, "자리 정리");
                        r.Privacy = p.Id;
                        ctx.Pass(p.Id == "screen" ? 10 : 3);
                    }
                    var next = await Say(ctx, "내 돗자리", new[] { "곧 소등이다.", Mind(ctx) }, new List<TalkChoice>
                    {
                        new("sleep", "잠자리에 든다 (22:00 소등)"), new("breathe", "앉아서 숨을 고른다"), new("more", "조금 더 둘러본다"),
                    }, "임시 거처");
                    if (next == "breathe") { await Breathe(ctx, r, null); return null; }
                    return next == "sleep" ? "sleep" : null;
                },
            };
            Common(ctx, on);
            await ctx.Roam(new RoamOptions { Rate = 0, Doors = false, On = on });

            // 22:00 lights out; then the night's light and noise.
            var g0 = ctx.G;
            g0.Minute = Math.Max(g0.Minute, ShelterLifeLogic.LightsOut);
            ctx.Refresh();
            ctx.Objective("임시 거처 첫날 밤", "소등했습니다.", new List<ObjectiveStep>());
            ctx.Engine.SetEnv("interiorDark", 1.5);
            await ctx.Caption("22:00 소등. 통로의 유도등만 남았다.", null, 2600);
            g0 = ctx.G;
            g0.Minute = ShelterLifeLogic.NightMinute;
            ctx.Stat("tension", ShelterLifeLogic.NightTension);
            bool deaf = Deaf(ctx);
            var lines2 = deaf
                ? new List<string> { "23:40. 건너편 돗자리에서 휴대폰 화면이 환하다. 아기 엄마가 아기를 안고 통로를 오간다.", "잠이 오지 않는다.", Mind(ctx) }
                : new List<string> { "23:40. 소등했지만 건너편 돗자리에서 휴대폰 화면이 환하다.", "어디선가 코 고는 소리, 아기가 칭얼대는 소리. 잠이 오지 않는다.", Mind(ctx) };
            var s = await Choose(ctx, "한밤", lines2, deaf ? ShelterLifeLogic.SleepDeaf : ShelterLifeLogic.Sleep, "임시 거처");
            r.Sleep = s.Id;
            if (s.Id == "breathe") await Breathe(ctx, r, null);
            if (s.Id == "phone") { StoryCommon.DrainBattery(ctx.G, 4); ctx.Refresh(); }
            await ctx.Skip("D+6 목 밤", "D+7 금 06:50", new[] { s.Kind == "good" ? "몇 번 깼지만, 새벽에는 조금 잤다." : "자다 깨다를 되풀이하다 새벽이 왔다." });
            var g1 = ctx.G;
            g1.Day = 7; g1.Minute = ShelterLifeLogic.MorningMinute;
            ctx.Stat("fatigue", s.Kind == "good" ? -25 : -12);
            ctx.Engine.SetEnv("underground", 1.0);
        }

        static async Task Morning(StoryContext ctx, ShelterLifeResult r)
        {
            bool board = false, water = false;
            // where the water is handed out today: known once the board was read or the reception said so
            bool Known() => r.Notice != null;
            List<ObjectiveStep> Steps() => new()
            {
                new ObjectiveStep("게시판 다시 보기 (오늘 바뀐 것)", board),
                new ObjectiveStep(Known() ? "물 받기 (출입구 B 급수차)" : "물 받기 (받는 곳은 게시판에)", water),
                new ObjectiveStep("내 돗자리로 돌아가기", false),
            };
            void Show() => ctx.Objective("임시 거처 이튿날 아침", "아침 7시 식수 배급이 시작됩니다.", Steps());
            Show();
            _ = ctx.Caption(Deaf(ctx) ? "D+7 금 06:50. 사람들이 물병을 들고 오간다." : "D+7 금 06:50. 출입구 쪽이 벌써 웅성거린다.", null, 3000);
            var on = new Dictionary<string, Func<StoryPoint, Task<JToken>>>
            {
                ["shelterBoard"] = async _ =>
                {
                    await Say(ctx, "대피소 게시판", ShelterLifeLogic.MorningNotice, null, ShelterLifeLogic.MorningNoticeTitle);
                    if (!board && r.Notice == null) r.Notice = "read";
                    board = true; Show();
                    return null;
                },
                ["reception"] = async _ =>
                {
                    if (water) return await Toast(ctx, "오늘 몫의 물은 받았다.");
                    if (r.Notice == "read") return await Toast(ctx, "게시판에 오늘 물은 출입구 B 급수차에서 나눠 준다고 적혀 있었다. 접수대 배급대는 쉰다.", "self");
                    if (r.Notice == null)
                    {
                        // came for water without the board: the staff answers what the player came for (no penalty, just a longer walk)
                        await Say(ctx, "대피소 관리자", new[] { ShelterLifeLogic.RuleFromStaff }, null, "접수대");
                        r.Notice = "staff"; Show();
                        return null;
                    }
                    return await Toast(ctx, "오늘 물은 출입구 B 급수차에서 나눠 준다.", "self");
                },
                [ShelterLifeLogic.WaterPoint] = async _ =>
                {
                    if (water) return await Toast(ctx, "오늘 몫의 물은 받았다.");
                    var lines = new List<string> { "출입구 B 앞에 급수차가 섰다. 한 사람 두 병씩, 명부를 확인하고 나눠 준다.", "줄 옆 기둥에 기대 앉은 할아버지가 줄을 바라보고 있다." };
                    if (r.Notice == null) lines.Insert(1, "(오늘은 여기서 나눠 준다고 게시판에 붙어 있었다고 한다.)");
                    var pick = await Choose(ctx, "급수차", lines, ShelterLifeLogic.Water, "아침 7시 · 출입구 B");
                    r.Water = pick.Id; water = true;
                    GameRules.Adjust(ctx.G, "thirst", -20);
                    ctx.Pass(pick.Id == "carry" ? 15 : 10);
                    Show();
                    return null;
                },
                ["spot"] = async _ =>
                {
                    if (!water) return await Toast(ctx, Known() ? "먼저 출입구 B 급수차에서 오늘 몫의 물을 받아 오자." : "먼저 오늘 몫의 물을 받아 오자. 어디서 나눠 주는지는 게시판에 있다.", "self");
                    await Task.CompletedTask;
                    return "back";
                },
            };
            Common(ctx, on);
            await ctx.Roam(new RoamOptions { Rate = 0, Doors = false, On = on });

            var g = ctx.G;
            g.Minute = Math.Max(g.Minute, ShelterLifeLogic.ChildMinute);
            ctx.Refresh();
            ctx.Objective("임시 거처 이튿날 아침", "옆 돗자리 이웃이 말을 건넵니다.", new List<ObjectiveStep>());
            var c = await Choose(ctx, "옆 돗자리 지우 엄마", new[]
            {
                "\"저기, 혹시… 지우 아빠가 다른 대피소에 있어서요. 급수 줄 다녀올 동안 지우 좀 봐 주실 수 있을까요?\"",
                "일곱 살 지우가 엄마 옷자락을 잡고 정민을 올려다본다.",
            }, ShelterLifeLogic.Child, "임시 거처");
            r.Child = c.Id;
            if (c.Id == "watch")
            {
                bool hasRadio = StoryCommon.Has(ctx.G, "radio");
                var play = await Choose(ctx, "지우", new[] { "\"사이렌 또 울려요?\"", "지우가 담요 끝을 만지작거린다.", Mind(ctx) }, Deaf(ctx) ? ShelterLifeLogic.PlayDeaf : ShelterLifeLogic.Play, "옆 돗자리",
                    p => p.Id == "radio" && !hasRadio ? "라디오가 없다" : null);
                r.Play = play.Id;
                if (play.Id == "breathe" && await Breathe(ctx, r, "child") == 0) r.Play = ShelterLifeLogic.PlayStay.Id; // stopped at once: sat beside her
                ctx.Pass(20);
                await Say(ctx, "지우 엄마", new[] { "\"고맙습니다. 지우가 웃고 있네요.\"" }, null, "임시 거처");
            }
            else if (c.Id == "staff") ctx.Pass(3);
        }
    }
}
