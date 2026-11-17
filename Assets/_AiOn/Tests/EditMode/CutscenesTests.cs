// StoryCine (story/cine.js) and Cutscenes (story/cutscenes.js): the port of tests/cutscenes.test.mjs (a recording cine
// stands in for cine.play, as the original's fake ctx did) plus each cutscene played to the end on FakeStoryHost with
// the real StoryCine: captions, titles, people, events, the skip key and what is restored afterwards.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class CutscenesTests
    {
        /// <summary>cine.play records the shots and answers with the queue (the last answer repeats).</summary>
        sealed class RecordingCine : StoryCine
        {
            public readonly List<(IReadOnlyList<CineShot> shots, IReadOnlyList<string> title)> Plays = new();
            readonly Queue<string> _answers;
            public RecordingCine(StoryContext ctx, params string[] answers) : base(ctx) { _answers = new Queue<string>(answers.Length > 0 ? answers : new[] { "done" }); }
            public override Task<string> Play(IReadOnlyList<CineShot> shots, IReadOnlyList<string> title = null)
            {
                Plays.Add((shots, title));
                return Task.FromResult(_answers.Count > 1 ? _answers.Dequeue() : _answers.Peek());
            }
        }

        /// <summary>The overlay, fill light and missile the host would draw.</summary>
        sealed class RecordingFx : ICineFx
        {
            public readonly List<string> Log = new();
            public readonly List<IReadOnlyList<string>> Titles = new();
            public int Missiles, MissilesDisposed;
            public double LastFill;
            public void Overlay(bool on) => Log.Add("overlay " + (on ? "on" : "off"));
            public void Title(IReadOnlyList<string> lines) { if (lines != null) Titles.Add(lines.ToList()); Log.Add("title " + (lines == null ? "-" : string.Join("|", lines))); }
            public Action OnTitleOut;
            public void TitleOut() { Log.Add("titleOut"); OnTitleOut?.Invoke(); }
            public void Fill(double x, double y, double z, double intensity, string color) { LastFill = Math.Max(LastFill, intensity); if (intensity > 0) Log.Add($"fill {intensity}"); }
            public IDisposable Missile(double[] from, double[] to, double seconds) { Missiles++; Log.Add("missile"); return new D(() => MissilesDisposed++); }
            sealed class D : IDisposable { readonly Action a; public D(Action a) { this.a = a; } public void Dispose() => a(); }
        }

        static (FakeStoryHost host, StoryContext ctx) Rig(long seed = 7)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var ctx = host.CreateContext(rules, rules.CreateGame(seed));
            return (host, ctx);
        }

        static (FakeStoryHost host, StoryContext ctx, RecordingFx fx) Live(long seed = 7)
        {
            var (host, ctx) = Rig(seed);
            var fx = new RecordingFx();
            ctx.Cine = new StoryCine(ctx, fx);
            return (host, ctx, fx);
        }

        static List<RippleRow> Rows(int n) => Enumerable.Range(0, n).Select(i => new RippleRow
        {
            Prep = $"p{i}", Label = $"준비{i}", Cause = new RippleMoment { When = "D-1 오전", Text = $"원인{i}" }, Effect = new RippleMoment { When = "D+3 오전", Text = $"결과{i}" },
        }).ToList();

        static List<string> Enters(FakeStoryHost host) => host.Calls("enter").Select(c => c.Text).ToList();

        static void AssertRestored(FakeStoryHost host, StoryContext ctx, bool controls = true)
        {
            Assert.That(host.Camera, Is.Null, "camera override cleared");
            Assert.That(host.Cinematic, Is.False);
            Assert.That(host.ModelHidden, Is.False);
            Assert.That(host.TimeScale, Is.EqualTo(1));
            Assert.That(host.ControlsEnabled, Is.EqualTo(controls));
            Assert.That(host.Black, Is.EqualTo(0));
            Assert.That(ctx.Cine.Busy, Is.False);
            Assert.That(ctx.Cine.Errors, Is.Empty, () => string.Join("\n", ctx.Cine.Errors));
            if (host.LevelId != null) Assert.That(host.Level(host.LevelId).Spawned, Is.Empty, "cutscene extras removed");
        }

        // ================================================================== tests/cutscenes.test.mjs

        [Test]
        public void C0Tour_FourLabelledPlacesInTown_Once()
        {
            var (host, ctx) = Rig();
            var cine = new RecordingCine(ctx); ctx.Cine = cine;
            ctx.OutdoorEnv = "night";
            var res = host.Run(async () => { await ctx.Goto("apartment", "home"); return await Cutscenes.TownTour(ctx); });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(Enters(host), Is.EqualTo(new[] { "apartment:home", "town:home" }));
            Assert.That(host.Env, Is.EqualTo("night"));
            var shots = cine.Plays[0].shots;
            Assert.That(shots.Count, Is.EqualTo(4));
            Assert.That(shots.All(s => s.At.Count > 0 && s.Cam.P.Count == 2), Is.True, "each shot names its place and moves");
            Assert.That(host.G.Knows("toured"), Is.True);
        }

        [Test]
        public void ReducedMotion_EveryShotHoldsItsLastFraming_NoSway()
        {
            var (host, ctx) = Rig();
            var cine = new RecordingCine(ctx); ctx.Cine = cine;
            host.ReduceMotion = true;
            host.Run(() => Cutscenes.TownTour(ctx));
            foreach (var s in cine.Plays[0].shots)
            {
                Assert.That(s.Cam.P.Count, Is.EqualTo(1)); Assert.That(s.Cam.T.Count, Is.EqualTo(1));
                Assert.That(s.Cam.Hand, Is.EqualTo(0)); Assert.That(s.Cam.Fov[0], Is.EqualTo(s.Cam.Fov[1]));
            }
        }

        [Test]
        public void C1_KeepsTheWorldStillWhenAskedFromInsideAModal()
        {
            var (host, ctx) = Rig();
            var cine = new RecordingCine(ctx); ctx.Cine = cine;
            host.SetControls(false);
            Assert.That(host.Run(() => Cutscenes.ElderScene(ctx)), Is.EqualTo("done"));
            Assert.That(cine.Plays[0].shots.Count, Is.EqualTo(2));
            StringAssert.Contains("304호 어르신이 계단 손잡이를 붙들고 서 있다", cine.Plays[0].shots[0].Lines[0].Text);
            Assert.That(host.ControlsEnabled, Is.False);
        }

        [Test]
        public void C2Montage_AtMostThreeRipples_ThenTheClosingShotOfThePath()
        {
            var (host, ctx) = Rig();
            var cine = new RecordingCine(ctx); ctx.Cine = cine;
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                Assert.That(await Cutscenes.RippleMontage(ctx, Rows(5), "bus"), Is.EqualTo("done"));
            });
            Assert.That(cine.Plays.Count, Is.EqualTo(2));
            Assert.That(cine.Plays[0].shots[0].Lines.Count, Is.EqualTo(3));
            Assert.That(cine.Plays[0].shots[0].Lines[0].Text, Is.EqualTo("D-1 오전 · 원인0  →  D+3 오전 · 결과0"));
            Assert.That(cine.Plays[0].shots[0].Lines[0].Speaker, Is.EqualTo("준비0"));
            Assert.That(ctx.LevelStates["town"]["buses"].ToString(), Is.EqualTo("on"));
            Assert.That(host.Level("town").State["buses"].ToString(), Is.EqualTo("on"));
            Assert.That(ctx.LevelStates["town"].Keys, Is.EqualTo(new[] { "buses" }));

            // No ripples: straight to the closing shot (it fades in itself); an unknown path falls back to the bus.
            var (qh, quiet) = Rig();
            var qc = new RecordingCine(quiet); quiet.Cine = qc;
            qh.Run(async () => { await quiet.Goto("town", "home"); await Cutscenes.RippleMontage(quiet, new List<RippleRow>(), "nowhere"); });
            Assert.That(qc.Plays.Count, Is.EqualTo(1));
            Assert.That(qc.Plays[0].shots[0].Fade, Is.True);
            Assert.That(quiet.LevelStates["town"]["buses"].ToString(), Is.EqualTo("on"));

            // 303호 앞 with 서연: the closing shot moves to the apartment corridor.
            var (sh, sy) = Rig();
            var sc = new RecordingCine(sy); sy.Cine = sc;
            sh.Run(async () => { await sy.Goto("town", "home"); await Cutscenes.RippleMontage(sy, Rows(1), "seoyeon"); });
            Assert.That(Enters(sh).Last(), Is.EqualTo("apartment:corridor3F"));
            Assert.That(sh.Env, Is.EqualTo("interiorDark"));
            Assert.That(sy.LevelStates.ContainsKey("town") ? sy.LevelStates["town"].Count : 0, Is.EqualTo(0));
            Assert.That(sy.LevelStates["apartment"].Keys, Is.EqualTo(new[] { "seoyeonDoor" }), "서연 stands at her open door");
            Assert.That(sy.LevelStates["apartment"]["seoyeonDoor"].ToString(), Is.EqualTo("open"));
        }

        [Test]
        public void C2Montage_SkippingTheCraneSkipsTheRest()
        {
            var (host, ctx) = Rig();
            var cine = new RecordingCine(ctx, "skipped", "done"); ctx.Cine = cine;
            var res = host.Run(async () => { await ctx.Goto("town", "home"); return await Cutscenes.RippleMontage(ctx, Rows(2), "stay"); });
            Assert.That(res, Is.EqualTo("skipped"));
            Assert.That(cine.Plays.Count, Is.EqualTo(1));
            Assert.That(ctx.LevelStates.ContainsKey("town") ? ctx.LevelStates["town"].Count : 0, Is.EqualTo(0));
        }

        // ================================================================== StoryCine pieces

        [Test]
        public void CatmullRom_MatchesThreeAtEndsAndMidpoint()
        {
            var pts = new List<double[]> { new double[] { 175, 95, 150 }, new double[] { 150, 78, 110 }, new double[] { 128, 66, 72 } };
            Assert.That(StoryCine.CatmullRom(pts, 0), Is.EqualTo(pts[0]).Within(1e-9));
            Assert.That(StoryCine.CatmullRom(pts, 0.5), Is.EqualTo(pts[1]).Within(1e-9));
            Assert.That(StoryCine.CatmullRom(pts, 1), Is.EqualTo(pts[2]).Within(1e-9));
            var q = StoryCine.CatmullRom(pts, 0.25);
            Assert.That(q[0], Is.InRange(150.0, 175.0)); Assert.That(q[2], Is.InRange(110.0, 150.0));
        }

        [Test]
        public void EnvAt_ClockLabelsToPresets()
        {
            Assert.That(Cutscenes.EnvAt("D-2 21:30", true), Is.EqualTo("night"));
            Assert.That(Cutscenes.EnvAt("D-2 21:30", false), Is.EqualTo("blackout"));
            Assert.That(Cutscenes.EnvAt("D-1 06:10", true), Is.EqualTo("dusk"));
            Assert.That(Cutscenes.EnvAt("D-1 09:00", true), Is.EqualTo("day"));
            Assert.That(Cutscenes.EnvAt("D-1 17:00", true), Is.EqualTo("afternoon"));
            Assert.That(Cutscenes.EnvAt("D-1 19:00", true), Is.EqualTo("dusk"));
            Assert.That(Cutscenes.EnvAt("그날 밤", true), Is.EqualTo("night"));
            Assert.That(Cutscenes.EnvAt("다음날 아침", false), Is.EqualTo("day"));
            Assert.That(Cutscenes.EnvAt("오후", true), Is.EqualTo("day"));
        }

        [Test]
        public void Play_WhileBusyAnswersBusy_AndAModalCutsceneLeavesControlsOff()
        {
            var (host, ctx, fx) = Live();
            var shot = new CineShot { Dur = 1, Cam = new CineCam { P = { new double[] { 0, 1, 0 } }, T = { new double[] { 0, 1, -5 } } } };
            var (a, b) = host.Run(async () =>
            {
                var first = ctx.Cine.Play(new[] { shot });
                var second = await ctx.Cine.Play(new[] { shot });
                return (await first, second);
            });
            Assert.That(a, Is.EqualTo("done")); Assert.That(b, Is.EqualTo("busy"));
            AssertRestored(host, ctx);
            Assert.That(host.Seconds, Is.EqualTo(1).Within(0.1), "a 1 s shot takes 1 s of frames");

            host.Run(() => ctx.Modal(async () => { Assert.That(await ctx.Cine.Play(new[] { shot }), Is.EqualTo("done")); Assert.That(host.ControlsEnabled, Is.False); }));
            Assert.That(host.ControlsEnabled, Is.True);
        }

        [Test]
        public void Play_SlowShotStillTakesItsRealSeconds()
        {
            var (host, ctx, fx) = Live();
            var shot = new CineShot { Dur = 2, Slow = 0.4, Cam = new CineCam { P = { new double[] { 0, 1, 0 } }, T = { new double[] { 0, 1, -5 } } } };
            int frames = host.FrameCount;
            double scale = 0;
            shot.At.Add((1.0, () => scale = host.TimeScale));
            host.Run(() => ctx.Cine.Play(new[] { shot }));
            Assert.That(scale, Is.EqualTo(0.4));
            Assert.That(host.FrameCount - frames, Is.InRange(59, 62), "2 real seconds at 30 fps");
            Assert.That(host.TimeScale, Is.EqualTo(1));
        }

        [Test]
        public void Title_FadesOutAfter6_5RealSeconds_EvenDuringASlowShot()
        {
            // setTimeout(6500) counts wall-clock time; Frame's dt is scaled by TimeScale (StrikeScene: 2.6 s, then 4.4 s at 0.4).
            var (host, ctx, fx) = Live();
            int outFrame = -1, frames = host.FrameCount;
            fx.OnTitleOut = () => outFrame = host.FrameCount - frames;
            CineShot Shot(double dur, double? slow) => new CineShot { Dur = dur, Slow = slow, Cam = new CineCam { P = { new double[] { 0, 1, 0 } }, T = { new double[] { 0, 1, -5 } } } };
            host.Run(() => ctx.Cine.Play(new[] { Shot(2.6, null), Shot(8, 0.4) }, new[] { "연무동", "D-day 금요일 20:12" }));
            Assert.That(outFrame, Is.InRange(194, 197), "6.5 real seconds at 30 fps, during the slow shot");
            Assert.That(fx.Log.Count(l => l == "titleOut"), Is.EqualTo(1));
        }

        [Test]
        public void TownGround_MatchesTheWalkMesh()
        {
            Assert.That(StoryCine.GroundOf(StoryCine.TownWalk, 0, 0), Is.EqualTo(0), "the main road");
            Assert.That(StoryCine.GroundOf(StoryCine.TownWalk, 70, -10), Is.EqualTo(0.12), "the office block's sidewalk");
            Assert.That(StoryCine.GroundOf(StoryCine.TownWalk, -80, 50), Is.EqualTo(0.12 - 1.02 * (4.0 / 8)).Within(1e-9), "a ramp up from the road");
            Assert.That(StoryCine.GroundOf(StoryCine.TownWalk, 0, 38), Is.EqualTo(0.52));
            Assert.That(StoryCine.GroundOf(StoryCine.TownWalk, 200, 0), Is.Null);
        }

        // ================================================================== each cutscene on FakeStoryHost

        [Test]
        public void Opening_PlaysToTheEnd_JeongminWalksToTheOffice()
        {
            var (host, ctx, fx) = Live();
            Assert.That(host.Run(() => Cutscenes.Opening(ctx)), Is.EqualTo("done"));
            Assert.That(host.Captions, Is.EqualTo(new[]
            {
                "전쟁이 나기 사흘 전. 경기도 연무동의 평범한 화요일 오후였다.",
                "퇴근길 사람들, 버스를 기다리는 학생들. 아무도 서두르지 않았다.",
                "정민, 31세. 한결물류 연무센터에서 배송 운영을 맡고 있다.",
                "오늘 오후부터, 배송 취소가 이상하게 많다.",
            }));
            Assert.That(Enters(host), Is.EqualTo(new[] { "town:office" }));
            Assert.That(host.Warnings, Is.Empty, string.Join("; ", host.Warnings));
            Assert.That(ctx.OutdoorEnv, Is.EqualTo("afternoon"));
            Assert.That(host.Env, Is.EqualTo("afternoon"));
            Assert.That(host.State("crowd").ToString(), Is.EqualTo("normal"));
            Assert.That(fx.Titles[0], Is.EqualTo(new[] { "연무동 · 경기도", "D-3 화요일 17:30" }));
            Assert.That(fx.Log.First(), Is.EqualTo("overlay on")); Assert.That(fx.Log, Does.Contain("overlay off"));
            // 정민 walks his path toward the office door (8.5 s at 1.45 m/s: about 12.3 m of its 26 m, as in the original)
            // and is removed with the cutscene.
            var jm = (FakePerson)host.Calls("spawnPerson").Single().Data;
            Assert.That(jm.Preset, Is.EqualTo("jeongmin"));
            double walked = Math.Sqrt((jm.X - 64) * (jm.X - 64) + (jm.Z + 8.5) * (jm.Z + 8.5));
            Assert.That(walked, Is.EqualTo(8.5 * 1.45).Within(0.3));
            Assert.That(jm.Anim, Is.EqualTo("walk")); Assert.That(jm.AnimSpeed, Is.EqualTo(1.45));
            Assert.That(jm.Yaw, Is.EqualTo(Math.Atan2(-12, 4.5)).Within(0.05), "faces along the first leg");
            Assert.That(host.Calls("removePerson").Count(), Is.EqualTo(1));
            // He walks the office block's sidewalk (walk y 0.12), so the camera looks at his ground + 1.45, as jm.group.position.y did.
            Assert.That(jm.Y, Is.EqualTo(0.12));
            var last = host.Calls("camera").Select(c => c.Data).OfType<CameraShot>().Last();
            Assert.That(last.TargetY, Is.EqualTo(0.12 + 1.45).Within(0.03), "look-at height from his ground (hand sway 0.028)");
            Assert.That(host.Seconds, Is.EqualTo(9 + 6.5 + 8.5).Within(0.3));
            AssertRestored(host, ctx);
            Assert.That(host.Calls("clearCaption"), Is.Empty);
        }

        [Test]
        public void Opening_SpaceSkipsTheRest()
        {
            var (host, ctx, fx) = Live();
            host.Queue(h => h.PressKey("Space"), () => host.Seconds > 3, "skip");
            Assert.That(host.Run(() => Cutscenes.Opening(ctx)), Is.EqualTo("skipped"));
            Assert.That(host.Captions, Is.EqualTo(new[] { "전쟁이 나기 사흘 전. 경기도 연무동의 평범한 화요일 오후였다." }));
            Assert.That(host.Calls("clearCaption").Count(), Is.EqualTo(1));
            Assert.That(host.Calls("spawnPerson"), Is.Empty, "the third shot never set up");
            Assert.That(host.Seconds, Is.LessThan(4));
            AssertRestored(host, ctx);
        }

        [Test]
        public void Timelapse_SkyTurnsOverTheComplex_ThenBackWhereThePlayerWas()
        {
            var (host, ctx, fx) = Live();
            ctx.SkipOverride = (from, to, text) => Cutscenes.Timelapse(ctx, from, to, text);
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                host.TeleportTo(1, 5.75, 2, 0.5);
                await ctx.Skip("D-2 21:30", "D-1 07:30", new[] { "밤이 지나간다.", "아침이 왔다." });
            });
            Assert.That(host.TimeSkips, Is.Empty, "the time-lapse replaced the card");
            Assert.That(Enters(host), Is.EqualTo(new[] { "apartment:home", "town:home", "apartment" }));
            var envs = host.Calls("env").Select(c => c.Text).ToList();
            int t = envs.IndexOf("night");
            Assert.That(t, Is.GreaterThanOrEqualTo(0), "starts at night (21:30)");
            Assert.That(envs.Skip(t + 1).Take(2), Is.EqualTo(new[] { "dusk", "day" }), "night -> dusk -> day (07:30)");
            Assert.That(host.Captions, Is.EqualTo(new[] { "밤이 지나간다.", "아침이 왔다." }));
            Assert.That(fx.Titles.Select(x => x[0]), Is.EqualTo(new[] { "D-2 21:30", "D-1 07:30" }));
            Assert.That((host.PlayerX, host.PlayerY, host.PlayerZ, host.PlayerYaw), Is.EqualTo((1.0, 5.75, 2.0, 0.5)));
            Assert.That(host.Seconds, Is.EqualTo(4 + 2 * 4.2).Within(0.2));
            AssertRestored(host, ctx);
        }

        [Test]
        public void Timelapse_WithoutPowerTurnsToBlackout_AndAFailingOneFallsBackToTheCard()
        {
            var (host, ctx, fx) = Live();
            ctx.Power = false;
            var res = host.Run(() => Cutscenes.Timelapse(ctx, "D-day 18:00", "D-day 22:00", new[] { "어두워진다." }));
            Assert.That(res, Is.EqualTo("done"));
            var envs = host.Calls("env").Select(c => c.Text).ToList();
            Assert.That(envs.Skip(envs.IndexOf("afternoon") + 1), Is.EqualTo(new[] { "dusk", "blackout" }));
            Assert.That(Enters(host), Is.EqualTo(new[] { "town:home" }), "no level before: nowhere to go back to");

            var (h2, c2) = Rig(); // no cine set: the override throws and ctx.Skip shows the card
            c2.SkipOverride = (from, to, text) => Cutscenes.Timelapse(c2, from, to, text);
            h2.Run(() => c2.Skip("D-1 09:00", "D-1 12:00", new[] { "x" }));
            Assert.That(h2.TimeSkips.Count, Is.EqualTo(1));
        }

        [Test]
        public void StrikeScene_MissileImpactBoomsAndJeongminCrouches()
        {
            var (host, ctx, fx) = Live();
            int impacts = 0;
            var booms = new List<(double x, double z, double size)>();
            ctx.Boom = (x, z, s) => booms.Add((x, z, s));
            double slowAtImpact = 0;
            var res = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                return await Cutscenes.StrikeScene(ctx, () => { impacts++; slowAtImpact = host.TimeScale; });
            });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(impacts, Is.EqualTo(1));
            Assert.That(slowAtImpact, Is.EqualTo(0.4));
            Assert.That(booms, Is.EqualTo(new[] { (12.0, 69.0, 2.6), (6.0, 69.0, 1.4), (18.0, 69.5, 1.2) }));
            Assert.That(host.Calls("shake").Single().Text, Is.EqualTo("0.6"));
            var cue = host.Calls("cue").Single();
            Assert.That(cue.Text, Is.EqualTo("hit")); Assert.That((string)((JObject)cue.Data)["name"], Is.EqualTo("whistle"));
            Assert.That(fx.Missiles, Is.EqualTo(1)); Assert.That(fx.MissilesDisposed, Is.EqualTo(1));
            var jm = (FakePerson)host.Calls("spawnPerson").Single().Data;
            Assert.That(jm.Anim, Is.EqualTo("crouch"));
            Assert.That((jm.X, jm.Z), Is.EqualTo((host.PlayerX, host.PlayerZ)));
            Assert.That(host.Captions, Is.EqualTo(new[] { "쾅— 땅이 울렸다. 공원 너머 남쪽 빌라 한 동이 무너져 내린다.", "불길과 검은 연기가 치솟는다. 멈추지 말고 대피소로." }));
            Assert.That(fx.Titles[0], Is.EqualTo(new[] { "연무동", "D-day 금요일 20:12" }));
            var grades = host.Calls("grade").Select(c => (double)((JObject)c.Data)["exposure"]).ToList();
            Assert.That(grades, Is.EqualTo(new[] { 0.5, 0.6, 0.4, 0.0 }));
            AssertRestored(host, ctx);
        }

        [Test]
        public void StrikeScene_SkippedBeforeImpact_NeverStrikes()
        {
            var (host, ctx, fx) = Live();
            int impacts = 0;
            host.Queue(h => h.PressKey("Escape"), () => host.Seconds > 1, "skip");
            var res = host.Run(async () => { await ctx.Goto("town", "home"); return await Cutscenes.StrikeScene(ctx, () => impacts++); });
            Assert.That(res, Is.EqualTo("skipped"));
            Assert.That(impacts, Is.EqualTo(0), "the caller (dday) strikes on its own when the scene was skipped");
            Assert.That(fx.MissilesDisposed, Is.EqualTo(1));
            AssertRestored(host, ctx);
        }

        [Test]
        public void BlackoutScene_LightsStutterThenCut()
        {
            var (host, ctx, fx) = Live();
            int cuts = 0;
            var res = host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                return await Cutscenes.BlackoutScene(ctx, () => { cuts++; ctx.Power = false; ctx.SetLevelState("apartment", "power", "off"); });
            });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(cuts, Is.EqualTo(1));
            var power = host.Calls("power").Select(c => c.Text).ToList();
            Assert.That(power.Skip(power.Count - 5), Is.EqualTo(new[] { "off", "on", "off", "on", "off" }));
            Assert.That(host.State("power").ToString(), Is.EqualTo("off"));
            Assert.That(host.PowerOn, Is.False);
            Assert.That(host.Captions, Is.EqualTo(new[] { "정전 — 집 안의 불이 한꺼번에 꺼졌다. 창밖 건너편 동도 캄캄하다." }));
            Assert.That(fx.Titles[0], Is.EqualTo(new[] { "한빛아파트 103동 302호", "D-day 금요일 19:40" }));
            Assert.That(host.Warnings, Is.Empty, string.Join("; ", host.Warnings));
            AssertRestored(host, ctx);
        }

        [Test]
        public void SirenScene_ResidentsHurryOut_ThenBackToTheApartment()
        {
            var (host, ctx, fx) = Live();
            ctx.OutdoorEnv = "blackout";
            var res = host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                host.TeleportTo(-2, 5.75, 6, 1);
                return await Cutscenes.SirenScene(ctx);
            });
            Assert.That(res, Is.EqualTo("done"));
            var people = host.Calls("spawnPerson").Select(c => (FakePerson)c.Data).ToList();
            Assert.That(people.Select(p => p.Preset), Is.EqualTo(new[] { "crowd", "crowd", "elder", "crowd", "crowd" }));
            Assert.That(people[3].Props, Is.EquivalentTo(new[] { "backpack", "flashlight" }));
            Assert.That(people.All(p => p.X < 56), Is.True, "everyone got past the gate toward the shelter");
            Assert.That(host.Calls("removePerson").Count(), Is.EqualTo(5));
            Assert.That(fx.LastFill, Is.EqualTo(9), "phone torches");
            Assert.That(host.Captions, Is.EqualTo(new[] { "20:05 — 공습경보. 사이렌이 오르내리며 1분 동안 울린다.", "어두운 거리로 사람들이 쏟아져 나온다. 다들 지하로 향한다." }));
            Assert.That(Enters(host), Is.EqualTo(new[] { "apartment:home", "town:home", "apartment" }));
            Assert.That((host.PlayerX, host.PlayerZ), Is.EqualTo((-2.0, 6.0)));
            Assert.That(host.Calls("env").Select(c => c.Text), Does.Contain("blackout"));
            AssertRestored(host, ctx);
        }

        [Test]
        public void TownTour_FourPlacesTitled_AlreadyInTownStaysPut()
        {
            var (host, ctx, fx) = Live();
            ctx.OutdoorEnv = "night";
            var res = host.Run(async () => { await ctx.Goto("town", "home"); return await Cutscenes.TownTour(ctx); });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(Enters(host), Is.EqualTo(new[] { "town:home" }), "no second trip into town");
            Assert.That(fx.Titles.Select(x => x[0]), Is.EqualTo(new[] { "바로편의점", "연무마트", "연무동 행정복지센터", "연무 생활문화센터" }));
            Assert.That(host.Captions, Is.EqualTo(new[] { "달력을 보기 전에, 정민은 동네를 한 바퀴 떠올렸다.", "어디에 무엇이 있는지 알면, 움직일 때 덜 헤맨다." }));
            Assert.That(host.Seconds, Is.EqualTo(4.3 * 3 + 5.2).Within(0.3));
            Assert.That(host.G.Knows("toured"), Is.True);
            AssertRestored(host, ctx);
        }

        [Test]
        public void TownTour_ReducedMotion_CameraHoldsStill()
        {
            var (host, ctx, fx) = Live();
            host.ReduceMotion = true;
            host.Run(() => Cutscenes.TownTour(ctx));
            var cams = host.Calls("camera").Where(c => c.Data != null).Select(c => (CameraShot)c.Data).ToList();
            var first = cams.TakeWhile(c => c.X == cams[0].X).ToList();
            Assert.That(first.Count, Is.GreaterThan(100), "the first stop's framing does not move");
            Assert.That(first.All(c => c.TargetX == first[0].TargetX && c.Fov == first[0].Fov), Is.True);
            Assert.That(cams.Select(c => c.X).Distinct().Count(), Is.EqualTo(4));
            AssertRestored(host, ctx);
        }

        [Test]
        public void ElderScene_InTheCorridor_FramesHerAndKeepsTheModalStill()
        {
            var (host, ctx, fx) = Live();
            var res = host.Run(async () =>
            {
                await ctx.Goto("apartment", "corridor3F");
                return await ctx.Modal(() => Cutscenes.ElderScene(ctx));
            });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(host.Captions, Is.EqualTo(new[] { "304호 어르신이 계단 손잡이를 붙들고 서 있다." }));
            Assert.That(fx.LastFill, Is.EqualTo(1.6));
            var last = host.Calls("camera").Where(c => c.Data != null).Select(c => (CameraShot)c.Data).Last();
            var elder = host.Person("elder");
            double ex = elder != null && elder.Visible ? elder.X : 15.45;
            Assert.That(last.X, Is.EqualTo(ex - 1.65).Within(0.01));
            Assert.That(last.Y, Is.EqualTo(Cutscenes.F3 + 1.25).Within(0.01));
            AssertRestored(host, ctx);
            Assert.That(host.Warnings, Is.Empty, string.Join("; ", host.Warnings));
        }

        [Test]
        public void RippleMontage_BusWithRealRipples_CraneThenAssembly()
        {
            var (host, ctx, fx) = Live();
            var rows = Rows(2);
            var res = host.Run(async () => { await ctx.Goto("apartment", "home"); return await Cutscenes.RippleMontage(ctx, rows, "bus"); });
            Assert.That(res, Is.EqualTo("done"));
            Assert.That(host.Captions, Is.EqualTo(new[] { "D-1 오전 · 원인0  →  D+3 오전 · 결과0", "D-1 오전 · 원인1  →  D+3 오전 · 결과1" }));
            Assert.That(host.Calls("caption").Select(c => (string)c.Data), Is.EqualTo(new[] { "준비0", "준비1" }));
            Assert.That(Enters(host), Is.EqualTo(new[] { "apartment:home", "town:home" }));
            Assert.That(host.State("buses").ToString(), Is.EqualTo("on"));
            Assert.That(fx.Titles.Select(x => x[0]), Is.EqualTo(new[] { "그날의 선택들", "행정복지센터 앞 임시 집결지" }));
            Assert.That(host.Seconds, Is.EqualTo(2 + 2 * 4.6 + 4.6).Within(0.3));
            AssertRestored(host, ctx);
        }

        [Test]
        public void RippleMontage_FromGameRipples_MissedAndSeoyeon()
        {
            // Rows from the game's own Ripples.Of on a game with a few preps, as evac.js passes them.
            var (host, ctx, fx) = Live();
            var rows = Ripples.Of(host.G);
            host.Run(() => Cutscenes.RippleMontage(ctx, rows, "missed"));
            Assert.That(host.Level("town").State["buses"].ToString(), Is.EqualTo("off"));
            Assert.That(host.Level("town").State["crowd"].ToString(), Is.EqualTo("sparse"));
            Assert.That(host.Captions.Count, Is.EqualTo(Math.Min(3, rows.Count)));
            AssertRestored(host, ctx);

            var (h2, c2, f2) = Live();
            h2.Run(() => Cutscenes.RippleMontage(c2, null, "seoyeon"));
            Assert.That(Enters(h2), Is.EqualTo(new[] { "apartment:corridor3F" }));
            Assert.That(h2.State("seoyeonDoor").ToString(), Is.EqualTo("open"));
            Assert.That(f2.Titles.Single(), Is.EqualTo(new[] { "103동 3층 · 303호 앞", "지원 차량을 함께 기다린다" }));
            Assert.That(h2.Warnings, Is.Empty, string.Join("; ", h2.Warnings));
            AssertRestored(h2, c2);
        }

        [Test]
        public void RippleMontage_SkippedCrane_NoClosingShotNoStates()
        {
            var (host, ctx, fx) = Live();
            host.Queue(h => h.PressKey("Enter"), () => host.Seconds > 2, "skip");
            var res = host.Run(() => Cutscenes.RippleMontage(ctx, Rows(3), "volunteer"));
            Assert.That(res, Is.EqualTo("skipped"));
            Assert.That(host.Level("town").State.ContainsKey("waterPoint"), Is.False);
            Assert.That(fx.Titles.Count, Is.EqualTo(1));
            AssertRestored(host, ctx);
        }
    }
}
