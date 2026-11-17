// The story's cutscenes (port of story/cutscenes.js; StoryCine shots): the opening over 연무동, the tour of the places
// that matter (C0), time passing over the 한빛아파트 complex, the D-day strike, the blackout, the air-raid siren, 304호
// 어르신 at the rail (C1) and the closing montage of what the early choices led to (C2). Each stands in for a card that
// only told it. Coordinates: town x = east, z = south, ground 0 (game/townmap.js); the 302호 floor is at y 5.75, the
// 3F corridor at 5.6. Every cutscene plays through ctx.Cine and completes with "done", "skipped" or "busy".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Cutscenes
    {
        /// <summary>levels/apartment-parts.js: 3rd floor (corridor) and 302호 main floor heights.</summary>
        public const double F3 = 5.6, FL = 5.75;

        static readonly HashSet<string> Night = new() { "night", "blackout" };

        static double[] V(double x, double y, double z) => new[] { x, y, z };
        static List<double[]> Pts(params double[][] p) => p.ToList();

        /// <summary>Reduced motion: each shot holds its last framing (no dolly, no zoom, no sway). Function cams are left alone.</summary>
        public static List<CineShot> Still(StoryContext ctx, IEnumerable<CineShot> list) => list.Select(s =>
        {
            if (!ctx.Engine.ReduceMotion || s.Cam == null || s.CamFn != null) return s;
            var c = s.Cam.Clone();
            c.P = new List<double[]> { c.P[^1] };
            c.T = new List<double[]> { c.T[^1] };
            c.Fov = c.Fov != null ? new[] { c.Fov[1], c.Fov[1] } : null;
            c.Hand = 0;
            var r = s.Copy(); r.Cam = c;
            return r;
        }).ToList();

        // Where the player was, so a cutscene shot elsewhere can put them back.
        sealed class Here { public string Level; public double X, Y, Z, Yaw; }
        static Here HereOf(StoryContext ctx)
        {
            var E = ctx.Engine;
            return E.LevelId != null ? new Here { Level = E.LevelId, X = E.PlayerX, Y = E.PlayerY, Z = E.PlayerZ, Yaw = E.PlayerYaw } : null;
        }
        static async Task Back(StoryContext ctx, Here h)
        {
            if (h == null || ctx.Engine.LevelId == h.Level) return;
            await ctx.Goto(h.Level);
            ctx.Engine.TeleportTo(h.X, h.Y, h.Z, h.Yaw); // the original's goto(level, { x, y, z, yaw })
        }

        // ---- opening: the town on an ordinary Tuesday, then 정민 on his way back into the office ----
        public static async Task<string> Opening(StoryContext ctx)
        {
            var cine = ctx.Cine;
            ctx.OutdoorEnv = "afternoon";
            ctx.SetLevelState("town", "crowd", "normal");
            await ctx.Goto("town", "office", "afternoon");
            IStoryPerson jm = null;
            (double x, double y, double z) JmAt() => jm != null ? (jm.X, cine.ActorY(jm), jm.Z) : (70, 0, -10); // y: his ground (0.12 on the sidewalk)
            return await cine.Play(new List<CineShot>
            {
                new CineShot { Dur = 9, Fade = true, Cam = new CineCam { P = Pts(V(175, 95, 150), V(150, 78, 110), V(128, 66, 72)), T = Pts(V(40, 8, 10), V(58, 4, -4), V(78, 2, -18)), Fov = new[] { 55.0, 50 }, Hand = 0.3 },
                    Lines = { new CineLine(1.4, "전쟁이 나기 사흘 전. 경기도 연무동의 평범한 화요일 오후였다.", null, 6500) } },
                new CineShot { Dur = 6.5, Cam = new CineCam { P = Pts(V(8, 1.7, 9), V(24, 1.7, 9.5)), T = Pts(V(60, 2.2, 3), V(64, 2, 1)), Fov = new[] { 48.0, 48 }, Hand = 0.05 },
                    Lines = { new CineLine(0.6, "퇴근길 사람들, 버스를 기다리는 학생들. 아무도 서두르지 않았다.", null, 5200) } },
                new CineShot { Dur = 8.5,
                    Setup = () => { jm = cine.Actor("jeongmin", new ActorOptions { Path = new (double, double)[] { (64, -8.5), (76, -13), (86, -18.8), (87, -20.4) }, Speed = 1.45 }); return null; },
                    CamFn = (k, s) => { var a = JmAt(); return new CinePose(V(a.x - 4.2 + k * 2.5, 1.75 + k * 0.2, a.z + 3.2 - k * 1.2), V(a.x + 0.4, a.y + 1.45, a.z - 0.6), 42); },
                    Lines = { new CineLine(0.4, "정민, 31세. 한결물류 연무센터에서 배송 운영을 맡고 있다.", null, 4000), new CineLine(4.4, "오늘 오후부터, 배송 취소가 이상하게 많다.", null, 3800) } },
            }, new[] { "연무동 · 경기도", "D-3 화요일 17:30" });
        }

        // ---- time passing (stands in for the time-skip card): the sky turns over the apartment complex ----
        /// <summary>The environment preset for a clock label ("D-1 21:30", "밤", "아침"...); night without power is blackout.</summary>
        public static string EnvAt(string label, bool power)
        {
            var m = Regex.Match(label ?? "", @"(\d{1,2}):(\d{2})");
            double h = m.Success ? int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) / 60.0
                : Regex.IsMatch(label ?? "", "밤") ? 22 : Regex.IsMatch(label ?? "", "아침") ? 8 : 13;
            var e = h < 5.5 || h >= 20 ? "night" : h < 7 ? "dusk" : h < 16 ? "day" : h < 18.5 ? "afternoon" : h < 20 ? "dusk" : "night";
            return e == "night" && !power ? "blackout" : e;
        }

        /// <summary>text: one caption or several, shown one after another while the sky turns. Fits ctx.SkipOverride.</summary>
        public static async Task<string> Timelapse(StoryContext ctx, string from, string to, IReadOnlyList<string> text)
        {
            var cine = ctx.Cine;
            var h = HereOf(ctx);
            string a = EnvAt(from, ctx.Power), b = EnvAt(to, ctx.Power);
            await ctx.Goto("town", "home", a);
            var mid = Night.Contains(a) || Night.Contains(b) ? (a == b ? a : "dusk") : ctx.Power ? "night" : "blackout";
            var texts = (text ?? Array.Empty<string>()).ToList();
            double dur = 4 + texts.Count * 4.2, step = (dur - 1.2) / texts.Count;
            var res = await cine.Play(new List<CineShot>
            {
                new CineShot { Dur = dur, Fade = true, Cam = new CineCam { P = Pts(V(26, 30, -2), V(34, 33, 2)), T = Pts(V(88, 18, 44), V(92, 22, 40)), Fov = new[] { 50.0, 46 }, Hand = 0.15 },
                    At = { (dur * 0.15, () => ctx.Engine.SetEnv(mid, dur * 0.3)), (dur * 0.52, () => ctx.Engine.SetEnv(b, dur * 0.32)), (dur * 0.52, () => cine.Title(new[] { to })) },
                    Lines = texts.Select((t, i) => new CineLine(0.8 + i * step, t, null, (int)Math.Round(step * 1000 - 300))).ToList() },
            }, new[] { from });
            await Back(ctx, h);
            return res;
        }

        // ---- D-day 20:1x: a missile comes down on the villa south of the park ----
        public static readonly (double x, double y, double z) Villa = (12, 9, 78);

        /// <summary>onImpact runs at the moment of the strike (dday.js: the ruin, fire and smoke stay in the town).</summary>
        public static Task<string> StrikeScene(StoryContext ctx, Action onImpact)
        {
            var cine = ctx.Cine;
            var engine = ctx.Engine;
            double px = engine.PlayerX, py = engine.PlayerY, pz = engine.PlayerZ;
            var VL = Villa;
            return cine.Play(new List<CineShot>
            {
                new CineShot { Dur = 2.6, Cam = new CineCam { P = Pts(V(54, 1.75, 21), V(53.6, 1.7, 21.4)), T = Pts(V(VL.x + 30, 70, VL.z - 40), V(VL.x + 4, 14, VL.z - 4)), Fov = new[] { 60.0, 50 }, Hand = 0.12 }, Exposure = 0.5,
                    Setup = () => { cine.Missile(V(VL.x + 120, 240, VL.z - 220), V(VL.x, VL.y, VL.z), 2.5); ctx.Audio.Cue("hit", new JObject { ["name"] = "whistle", ["seconds"] = 2.4 }); return null; } },
                new CineShot { Dur = 4.4, Slow = 0.4, Exposure = 0.6, Cam = new CineCam { P = Pts(V(2, 3.4, 54), V(3, 3.1, 55.5)), T = Pts(V(VL.x, 8, VL.z), V(VL.x, 12, VL.z)), Fov = new[] { 46.0, 40 }, Hand = 0.08 },
                    At = { (0.05, () => { onImpact?.Invoke(); engine.Shake(0.6, 1.4); ctx.Boom?.Invoke(VL.x, VL.z - 9, 2.6); }),
                        (0.45, () => ctx.Boom?.Invoke(VL.x - 6, VL.z - 9, 1.4)), (0.8, () => ctx.Boom?.Invoke(VL.x + 6, VL.z - 8.5, 1.2)) },
                    Lines = { new CineLine(0.4, "쾅— 땅이 울렸다. 공원 너머 남쪽 빌라 한 동이 무너져 내린다.", null, 4200) } },
                new CineShot { Dur = 3.4, Exposure = 0.4, Cam = new CineCam { P = Pts(V(px + 2.5, py + 1.4, pz - 2.2), V(px + 2.2, py + 1.5, pz - 1.9)), T = Pts(V(px, py + 1.0, pz), V(px - 3, py + 3, pz + 3)), Fov = new[] { 50.0, 54 }, Hand = 0.06 },
                    Setup = () => { var jm = cine.Actor("jeongmin", new ActorOptions { At = (px, pz), Yaw = Math.Atan2(-(VL.x - px), -(VL.z - pz)) }); jm.SetAnim("crouch"); return null; },
                    Lines = { new CineLine(0.3, "불길과 검은 연기가 치솟는다. 멈추지 말고 대피소로.", null, 3200) } },
            }, new[] { "연무동", "D-day 금요일 20:12" });
        }

        // ---- D-day 19:40: the lights go out at home, then across the complex ----
        /// <summary>cut runs when the power goes for good (dday.js).</summary>
        public static Task<string> BlackoutScene(StoryContext ctx, Action cut)
        {
            var cine = ctx.Cine;
            const double y = FL;
            void Flick(bool on) { ctx.SetLevelState("apartment", "power", on ? "on" : "off"); ctx.Engine.SetPower(on); } // the lights stutter first
            return cine.Play(new List<CineShot>
            {
                new CineShot { Dur = 3.2, Cam = new CineCam { P = Pts(V(-1.0, y + 1.75, 3.4), V(-1.15, y + 1.72, 3.8)), T = Pts(V(-2.3, y + 1.1, 8.6), V(-2.4, y + 1.05, 8.6)), Fov = new[] { 60.0, 57 }, Hand = 0.03 },
                    At = { (1.4, () => Flick(false)), (1.55, () => Flick(true)), (1.8, () => Flick(false)), (1.9, () => Flick(true)), (2.3, () => cut?.Invoke()) } },
                new CineShot { Dur = 3.6, Cam = new CineCam { P = Pts(V(-1.6, y + 1.6, 5.2), V(-2.0, y + 1.55, 7.0)), T = Pts(V(-2.4, y + 1.4, 12), V(-2.4, y + 1.6, 14)), Fov = new[] { 52.0, 48 }, Hand = 0.04 },
                    Lines = { new CineLine(0.2, "정전 — 집 안의 불이 한꺼번에 꺼졌다. 창밖 건너편 동도 캄캄하다.", null, 3400) } },
            }, new[] { "한빛아파트 103동 302호", "D-day 금요일 19:40" });
        }

        // ---- D-day 20:05: the siren over the dark complex ----
        public static async Task<string> SirenScene(StoryContext ctx)
        {
            var cine = ctx.Cine;
            var h = HereOf(ctx);
            IStoryPerson lead = null;
            await ctx.Goto("town", "home");
            var res = await cine.Play(new List<CineShot>
            {
                new CineShot { Dur = 6.5, Fade = true, Exposure = 1.2, Cam = new CineCam { P = Pts(V(50, 1.5, 12.5), V(49, 1.55, 11.5)), T = Pts(V(64, 1.5, 15), V(38, 1.4, 9)), Fov = new[] { 50.0, 50 }, Hand = 0.12 },
                    Setup = () =>
                    { // residents of the complex hurry out of the gate toward the shelter, phone torches on
                        var who = new (string preset, double delay, double speed, string[] props)[]
                        {
                            ("crowd", 0, 1.9, new[] { "flashlight" }), ("crowd", 0.5, 2.4, new string[0]), ("elder", 1.1, 1.2, new string[0]),
                            ("crowd", 1.6, 2.6, new[] { "backpack", "flashlight" }), ("crowd", 2.4, 2.1, new[] { "backpack" }),
                        };
                        lead = null;
                        for (int i = 0; i < who.Length; i++)
                        {
                            var (preset, delay, speed, props) = who[i];
                            var a = cine.Actor(preset, new ActorOptions { Path = new (double, double)[] { (66 + i * 0.6, 16 + (i % 2) * 1.2), (56, 11 + (i % 3) * 0.7), (30, 9 + (i % 2)) }, Speed = speed, Delay = delay, Props = props });
                            lead ??= a;
                        }
                        return null;
                    },
                    Fill = new CineFill { At = () => lead != null ? V(lead.X - 1.5, 2.6, lead.Z + 1.5) : null, I = 9, Color = "#e8ecff" }, // phone torches
                    Lines = { new CineLine(0.5, "20:05 — 공습경보. 사이렌이 오르내리며 1분 동안 울린다.", null, 5200) } },
                new CineShot { Dur = 5, Exposure = 1.1, Cam = new CineCam { P = Pts(V(40, 2, -8), V(46, 2.2, -6)), T = Pts(V(-20, 6, 30), V(-30, 10, 34)), Fov = new[] { 46.0, 44 }, Hand = 0.08 },
                    Lines = { new CineLine(0.4, "어두운 거리로 사람들이 쏟아져 나온다. 다들 지하로 향한다.", null, 4200) } },
            }, new[] { "연무동", "D-day 금요일 20:05" });
            await Back(ctx, h);
            return res;
        }

        // ---- C0, prep start: before the first plan, the places that matter in 연무동 (once: know.toured) ----
        // Night of D-2: the exposure lift keeps the buildings readable. Labels name places, they do not judge them.
        // Ends in town: the caller (Prep) enters 302호 next, so there is no extra trip back.
        sealed class TourStop { public CineCam Cam; public string[] Title; }
        static readonly TourStop[] Tour =
        {
            new TourStop { Cam = new CineCam { P = Pts(V(54, 13, -3), V(49, 12, -5)), T = Pts(V(36, 2, 15), V(35, 2, 15)), Fov = new[] { 50.0, 46 } }, Title = new[] { "바로편의점", "단지 앞 · 작은 가게" } },
            new TourStop { Cam = new CineCam { P = Pts(V(2, 15, 3), V(-8, 14, 1)), T = Pts(V(-16, 3, -30), V(-17, 3, -30)), Fov = new[] { 52.0, 48 } }, Title = new[] { "연무마트", "생필품 · 장보기는 두 칸이 든다" } },
            new TourStop { Cam = new CineCam { P = Pts(V(-58, 15, -3), V(-64, 14, -5)), T = Pts(V(-84, 3, -26), V(-85, 3, -25)), Fov = new[] { 52.0, 48 } }, Title = new[] { "연무동 행정복지센터", "게시판 · 마당의 비상급수시설" } },
            new TourStop { Cam = new CineCam { P = Pts(V(-50, 20, 6), V(-56, 18, 2)), T = Pts(V(-90, 2, 26), V(-91, 2, 27)), Fov = new[] { 52.0, 46 } }, Title = new[] { "연무 생활문화센터", "지하에 대피소가 있다고 들었다" } },
        };

        public static async Task<string> TownTour(StoryContext ctx)
        {
            var cine = ctx.Cine;
            var env = string.IsNullOrEmpty(ctx.OutdoorEnv) ? "night" : ctx.OutdoorEnv;
            if (ctx.Engine.LevelId != "town") await ctx.Goto("town", "home", env); else ctx.Engine.SetEnv(env, 0);
            var res = await cine.Play(Still(ctx, Tour.Select((t, i) =>
            {
                var cam = t.Cam.Clone(); cam.Hand = 0.1;
                var title = t.Title;
                return new CineShot
                {
                    Dur = i == Tour.Length - 1 ? 5.2 : 4.3, Fade = i == 0, Exposure = 1.0, Cam = cam,
                    At = { (0.2, () => cine.Title(title)) },
                    Lines = i == 0 ? new List<CineLine> { new CineLine(0.8, "달력을 보기 전에, 정민은 동네를 한 바퀴 떠올렸다.", null, 3400) }
                        : i == Tour.Length - 1 ? new List<CineLine> { new CineLine(1.4, "어디에 무엇이 있는지 알면, 움직일 때 덜 헤맨다.", null, 3600) } : new List<CineLine>(),
                };
            })));
            ctx.G.SetKnow("toured", true);
            return res;
        }

        // ---- C1, the alert on 3F: 304호 어르신 holding the rail by her door (Elder asks right after) ----
        public static async Task<string> ElderScene(StoryContext ctx)
        {
            var cine = ctx.Cine;
            var engine = ctx.Engine;
            bool was = engine.ControlsEnabled;
            var n = engine.LevelId == "apartment" ? engine.Npc("elder") : null;
            // her spot, levels/apartment-corridor.js ELDER (IStoryPerson has no Y: she stands on the 3F corridor)
            double ex = n != null && n.Visible ? n.X : 15.45, ez = n != null && n.Visible ? n.Z : -0.4, y = F3;
            // A phone torch's worth of light in the dark corridor; the cameras stand past 303's door, which opens outward.
            var fill = new CineFill { At = () => V(ex - 2.4, y + 2.2, -1.7), I = 1.6, Color = "#e8ecff" };
            try
            {
                return await cine.Play(Still(ctx, new[]
                {
                    new CineShot { Dur = 3.4, Fill = fill, Cam = new CineCam { P = Pts(V(ex - 4.4, y + 1.6, -1.55), V(ex - 3.5, y + 1.56, -1.5)), T = Pts(V(ex, y + 1.15, ez - 0.05)), Fov = new[] { 50.0, 46 }, Hand = 0.04 },
                        Lines = { new CineLine(0.3, "304호 어르신이 계단 손잡이를 붙들고 서 있다.", null, 3800) } },
                    new CineShot { Dur = 2.8, Fill = fill, Cam = new CineCam { P = Pts(V(ex - 1.9, y + 1.3, -1.55), V(ex - 1.65, y + 1.25, -1.5)), T = Pts(V(ex - 0.1, y + 1.0, ez + 0.1), V(ex - 0.05, y + 1.05, ez + 0.1)), Fov = new[] { 44.0, 40 }, Hand = 0.03 } },
                }));
            }
            finally { if (!was) engine.SetControls(false); } // asked from inside a modal: keep the world still for the talk
        }

        // ---- C2, the ending: a crane over the town at dusk with up to three ripples, then one shot of where 정민 is ----
        // rows: Ripples.Of(G); path: G.know.path. The level states it sets stay: the run is over after this.
        public sealed class Close
        {
            public string Level, Spawn, Env;
            public Dictionary<string, string> States = new();
            public CineCam Cam;
            public string[] Title;
        }

        public static readonly Dictionary<string, Close> Closes = new()
        {
            ["bus"] = new Close { States = { ["buses"] = "on" }, Cam = new CineCam { P = Pts(V(-47, 6, -29), V(-51, 5, -31)), T = Pts(V(-67, 1.6, -41)) }, Title = new[] { "행정복지센터 앞 임시 집결지", "버스가 출발한다" } },
            ["missed"] = new Close { States = { ["buses"] = "off", ["crowd"] = "sparse" }, Cam = new CineCam { P = Pts(V(-47, 6, -29), V(-50, 5.5, -30)), T = Pts(V(-62, 1, -44)) }, Title = new[] { "임시 집결지", "버스가 떠난 뒤" } },
            ["volunteer"] = new Close { States = { ["waterPoint"] = "on", ["crowd"] = "queue" }, Cam = new CineCam { P = Pts(V(-67, 5, -7), V(-70, 4.5, -9)), T = Pts(V(-80, 1.2, -17)) }, Title = new[] { "비상급수시설", "자원봉사 조끼를 입고" } },
            ["stay"] = new Close { Cam = new CineCam { P = Pts(V(80, 2.5, 12), V(79, 3.5, 14)), T = Pts(V(82, 9, 30), V(82, 11, 30)) }, Title = new[] { "한빛아파트 103동", "남기로 한 집" } },
            ["walk"] = new Close { Cam = new CineCam { P = Pts(V(121, 3, -6), V(122, 3.4, -7)), T = Pts(V(60, 8, 0), V(58, 9, 1)) }, Title = new[] { "연무동 동쪽 끝", "도시를 빠져나가는 길" } },
            // 303's door hangs on its east jamb and opens into the corridor, so the camera looks in past its west side.
            ["seoyeon"] = new Close { Level = "apartment", Spawn = "corridor3F", Env = "interiorDark", States = { ["seoyeonDoor"] = "open" },
                Cam = new CineCam { P = Pts(V(7.95, F3 + 1.52, -1.75), V(8.3, F3 + 1.5, -1.65)), T = Pts(V(9.5, F3 + 1.2, 0.5)) }, Title = new[] { "103동 3층 · 303호 앞", "지원 차량을 함께 기다린다" } },
        };

        public static async Task<string> RippleMontage(StoryContext ctx, IEnumerable<RippleRow> rows = null, string path = "bus")
        {
            var cine = ctx.Cine;
            var end = path != null && Closes.TryGetValue(path, out var c) ? c : Closes["bus"];
            var picks = (rows ?? Enumerable.Empty<RippleRow>()).Take(3).ToList();
            var level = end.Level ?? "town";
            var res = "done";
            if (picks.Count > 0)
            {
                await ctx.Goto("town", "home", "dusk");
                double dur = 2 + picks.Count * 4.6;
                res = await cine.Play(Still(ctx, new[]
                {
                    new CineShot { Dur = dur, Fade = true, Exposure = 0.4, Cam = new CineCam { P = Pts(V(60, 70, 80), V(20, 82, 50)), T = Pts(V(-10, 0, 0), V(-30, 0, -10)), Fov = new[] { 50.0, 46 }, Hand = 0.2 },
                        Lines = picks.Select((r, i) => new CineLine(1 + i * 4.6, $"{r.Cause?.When} · {r.Cause?.Text}  →  {r.Effect?.When} · {r.Effect?.Text}", r.Label, 4400)).ToList() },
                }), new[] { "그날의 선택들", "연무동, 돌아보며" });
            }
            if (res != "done") return res;
            if (ctx.Engine.LevelId != level) await ctx.Goto(level, end.Spawn ?? "home", end.Env ?? "dusk");
            else if (level == "town") ctx.Engine.SetEnv("dusk", 0);
            foreach (var kv in end.States) ctx.SetLevelState(level, kv.Key, kv.Value);
            var cam = end.Cam.Clone(); cam.Fov = new[] { 46.0, 44 }; cam.Hand = 0.06;
            var title = end.Title;
            return await cine.Play(Still(ctx, new[]
            {
                new CineShot { Dur = 4.6, Fade = picks.Count == 0, Exposure = level == "town" ? 0.4 : 0.6, Cam = cam, At = { (0.3, () => cine.Title(title)) } },
            }));
        }
    }
}
