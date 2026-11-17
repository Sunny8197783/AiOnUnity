// Cutscenes (port of story/cine.js createCine): letterboxed camera moves over the live world, with people walking
// through them, timed events (explosions, lights going out, sounds) and captions. Space / Enter / Esc skips the rest.
//   new StoryCine(ctx[, fx]) -> Play(shots, title), Actor(preset, opts), Missile(from, to, seconds), Title(lines), Busy
// A shot (CineShot): Dur, Cam | CamFn, Lines, At, Env, Slow, Fade, Setup, Exposure, Fill
//   Cam: P / T positions and look-at targets eased along a Catmull-Rom curve (three.js CatmullRomCurve3, centripetal)
//        over the shot, Fov [a, b], Ease 'inout' | 'linear' | 'out' | 'in', Hand = handheld sway in metres.
//        Or CamFn (k, s) -> CinePose for anything else (s = seconds into the shot).
//   Lines: (at, text, speaker, ms) captions; At: (seconds, action) events; Env: (preset, seconds) at the start;
//   Slow: world time scale for the shot; Fade: fade in from black; Setup(): returns an optional cleanup;
//   Exposure: stops added to the grade for the shot; Fill: a soft light that stays on the subject.
// Time runs on IStoryEngine.Frame (real seconds: dt / TimeScale, a hitch counts as at most 0.25 s), so a shot plays
// out the same on the Unity host and on FakeStoryHost. The overlay (title lines, letterbox), the fill light and the
// missile have no member in the contract: they go to an optional ICineFx (the host may implement it on its engine).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>A camera placement for one moment of a shot: position, look-at target, field of view (null keeps it).</summary>
    public sealed class CinePose
    {
        public double[] P, T;
        public double? Fov;
        public CinePose() { }
        public CinePose(double[] p, double[] t, double? fov = null) { P = p; T = t; Fov = fov; }
    }

    /// <summary>shot.cam as data: points eased over the shot.</summary>
    public sealed class CineCam
    {
        public List<double[]> P = new(), T = new();
        /// <summary>[from, to] or null (keep the camera's fov).</summary>
        public double[] Fov;
        public string Ease = "inout";
        /// <summary>Handheld sway in metres; null = the default 0.04.</summary>
        public double? Hand;
        public CineCam Clone() => new CineCam
        {
            P = P.Select(a => (double[])a.Clone()).ToList(), T = T.Select(a => (double[])a.Clone()).ToList(),
            Fov = (double[])Fov?.Clone(), Ease = Ease, Hand = Hand,
        };
    }

    /// <summary>shot.fill: a soft light kept on the subject (At is evaluated every frame; null = none this frame).</summary>
    public sealed class CineFill
    {
        public Func<double[]> At;
        public double? I;
        public string Color;
    }

    /// <summary>One caption of a shot: [at, text, speaker?, ms?].</summary>
    public sealed class CineLine
    {
        public double At;
        public string Text, Speaker;
        public int? Ms;
        public CineLine(double at, string text, string speaker = null, int? ms = null) { At = at; Text = text; Speaker = speaker; Ms = ms; }
    }

    public sealed class CineShot
    {
        public double Dur;
        public CineCam Cam;
        public Func<double, double, CinePose> CamFn;
        public List<CineLine> Lines = new();
        public List<(double at, Action fn)> At = new();
        public (string preset, double seconds)? Env;
        public double? Slow;
        public bool Fade;
        public Func<Action> Setup;
        public double? Exposure;
        public CineFill Fill;

        /// <summary>Shallow copy (the original's { ...s }).</summary>
        public CineShot Copy() => (CineShot)MemberwiseClone();
    }

    /// <summary>Options of StoryCine.Actor (the original's { at, path, speed, anim, yaw, delay, props }).</summary>
    public sealed class ActorOptions
    {
        public (double x, double z)? At;
        public IReadOnlyList<(double x, double z)> Path;
        public double Speed = 1.3;
        public string Anim = "idle";
        public double Yaw;
        public double Delay;
        public IReadOnlyList<string> Props = Array.Empty<string>();
    }

    /// <summary>
    /// What a cutscene draws that the contract has no member for. The host implements it (e.g. on its engine object);
    /// without one these are skipped and the cutscene still plays (camera, captions, people, events).
    /// </summary>
    public interface ICineFx
    {
        /// <summary>The letterbox overlay with its "Space · 건너뛰기" hint on or off.</summary>
        void Overlay(bool on);
        /// <summary>Typed-out title lines in the corner (place, time); null or empty clears; Out() fades them away.</summary>
        void Title(IReadOnlyList<string> lines);
        void TitleOut();
        /// <summary>The fill light: intensity 0 turns it off. Position in the original frame.</summary>
        void Fill(double x, double y, double z, double intensity, string color);
        /// <summary>A missile from -> to in seconds (white-hot head, long smoke trail); dispose removes it.</summary>
        IDisposable Missile(double[] from, double[] to, double seconds);
    }

    public class StoryCine
    {
        /// <summary>Keys that skip the rest of a cutscene (KeyboardEvent.code names, as IStoryEngine.Key reports them).</summary>
        public static readonly HashSet<string> SkipKeys = new() { "Space", "Enter", "NumpadEnter", "Escape" };

        public static readonly Dictionary<string, Func<double, double>> Eases = new()
        {
            ["inout"] = k => k * k * (3 - 2 * k),
            ["linear"] = k => k,
            ["out"] = k => 1 - (1 - k) * (1 - k),
            ["in"] = k => k * k,
        };

        protected readonly StoryContext Ctx;
        readonly ICineFx _fx;
        readonly List<IDisposable> _live = new(); // actors and effects to remove when the cutscene ends
        bool _busy, _skipped;
        double? _titleLeft; // real seconds until the title fades out (the original's setTimeout, wall clock)
        double _fov = 50;
        readonly Dictionary<IStoryPerson, double> _actorY = new(); // the ground height each actor stands on

        /// <summary>Errors from shot events and cleanups (the original logs them and goes on).</summary>
        public readonly List<Exception> Errors = new();

        public StoryCine(StoryContext ctx, ICineFx fx = null)
        {
            Ctx = ctx;
            _fx = fx ?? ctx?.Engine as ICineFx;
            if (ctx != null) { ctx.Engine.Key += OnKey; ctx.Engine.Frame += OnTitleFrame; }
        }

        // The title's timer counts real seconds like setTimeout: Frame's dt is scaled by TimeScale, so a slow shot
        // (StrikeScene's 0.4) does not hold the title up longer.
        void OnTitleFrame(double dt)
        {
            if (_titleLeft == null) return;
            double scale = Ctx.Engine.TimeScale > 0 ? Ctx.Engine.TimeScale : 1;
            _titleLeft -= dt / scale;
            if (_titleLeft <= 0) { _titleLeft = null; _fx?.TitleOut(); }
        }

        public bool Busy => _busy;

        void OnKey(string code) { if (_busy && code != null && SkipKeys.Contains(code)) _skipped = true; }

        /// <summary>Skips the rest of the cutscene that is playing (what Space / Enter / Esc do).</summary>
        public void Skip() { if (_busy) _skipped = true; }

        // ------------------------------------------------------------------ title

        /// <summary>Typed-out title lines in the corner (place, time), the way a film opens on a location.</summary>
        public virtual void Title(IReadOnlyList<string> lines)
        {
            _titleLeft = null; // clearTimeout(titleT)
            if (lines == null || lines.Count == 0) { _fx?.Title(null); return; }
            _fx?.Title(lines);
            _titleLeft = 6.5; // read, then out of the way
        }

        // ------------------------------------------------------------------ camera pose

        static double[] Lerp(double[] a, double[] b, double e) => new[] { a[0] + (b[0] - a[0]) * e, a[1] + (b[1] - a[1]) * e, a[2] + (b[2] - a[2]) * e };

        /// <summary>THREE.CatmullRomCurve3(points).getPoint(t): centripetal, tension 0.5, open curve.</summary>
        public static double[] CatmullRom(IReadOnlyList<double[]> pts, double t)
        {
            int l = pts.Count;
            double p = (l - 1) * t;
            int ip = (int)Math.Floor(p);
            double w = p - ip;
            if (w == 0 && ip == l - 1) { ip = l - 2; w = 1; }
            double[] p0 = ip > 0 ? pts[ip - 1] : Sub2(pts[0], pts[1]);
            double[] p1 = pts[ip % l], p2 = pts[(ip + 1) % l];
            double[] p3 = ip + 2 < l ? pts[(ip + 2) % l] : Sub2(pts[l - 1], pts[l - 2]);
            double dt0 = Math.Pow(D2(p0, p1), 0.25), dt1 = Math.Pow(D2(p1, p2), 0.25), dt2 = Math.Pow(D2(p2, p3), 0.25);
            if (dt1 < 1e-4) dt1 = 1.0;
            if (dt0 < 1e-4) dt0 = dt1;
            if (dt2 < 1e-4) dt2 = dt1;
            var r = new double[3];
            for (int i = 0; i < 3; i++)
            {
                double x0 = p0[i], x1 = p1[i], x2 = p2[i], x3 = p3[i];
                double t1 = ((x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1) * dt1;
                double t2 = ((x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2) * dt1;
                double c0 = x1, c1 = t1, c2 = -3 * x1 + 3 * x2 - 2 * t1 - t2, c3 = 2 * x1 - 2 * x2 + t1 + t2;
                r[i] = c0 + c1 * w + c2 * w * w + c3 * w * w * w;
            }
            return r;
        }
        static double[] Sub2(double[] a, double[] b) => new[] { 2 * a[0] - b[0], 2 * a[1] - b[1], 2 * a[2] - b[2] }; // (a - b) + a
        static double D2(double[] a, double[] b) { double x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2]; return x * x + y * y + z * z; }

        /// <summary>The camera at k (0..1 of the shot) and s seconds into it.</summary>
        public static CinePose Pose(CineShot shot, double k, double s)
        {
            if (shot.CamFn != null) return shot.CamFn(k, s);
            var cam = shot.Cam;
            var e = (cam.Ease != null && Eases.TryGetValue(cam.Ease, out var f) ? f : Eases["inout"])(k);
            double[] At(List<double[]> pts) => pts.Count == 1 ? (double[])pts[0].Clone() : pts.Count == 2 ? Lerp(pts[0], pts[1], e) : CatmullRom(pts, e);
            return new CinePose(At(cam.P), At(cam.T), cam.Fov != null ? cam.Fov[0] + (cam.Fov[1] - cam.Fov[0]) * e : (double?)null);
        }

        // ------------------------------------------------------------------ shots

        Task RunShot(CineShot shot)
        {
            var E = Ctx.Engine;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action cleanup = shot.Setup?.Invoke();
            if (shot.Env is (string preset, double secs)) E.SetEnv(preset, secs);
            E.TimeScale = shot.Slow ?? 1;
            E.SetGrade(new JObject { ["exposure"] = shot.Exposure ?? 0, ["seconds"] = shot.Exposure.HasValue && shot.Exposure != 0 ? 0.4 : 0.2 });
            if (shot.Fade) { _ = E.FadeTo(1, 0); _ = E.FadeTo(0, 0.6); }
            var events = new List<(double at, Action fn)>(shot.At ?? new List<(double, Action)>());
            foreach (var ln in shot.Lines ?? new List<CineLine>()) { var l = ln; events.Add((l.At, () => { _ = Ctx.Caption(l.Text, l.Speaker, l.Ms); })); }
            events = events.OrderBy(x => x.at).ToList(); // stable, like Array.prototype.sort
            double hand = shot.Cam?.Hand ?? 0.04;
            bool rm = E.ReduceMotion;
            double s = 0;
            bool done = false;
            Action<double> onFrame = null;

            void Finish()
            {
                if (done) return;
                done = true;
                E.Frame -= onFrame;
                _fx?.Fill(0, -500, 0, 0, null);
                try { cleanup?.Invoke(); } catch (Exception err) { Errors.Add(err); } // '[cine] shot cleanup failed'
                tcs.TrySetResult(true);
            }

            onFrame = dt =>
            {
                if (done) return;
                // Real seconds whatever the world's time scale; a hitch counts as at most a quarter second.
                double scale = E.TimeScale > 0 ? E.TimeScale : 1;
                s += Math.Min(0.25, dt / scale);
                while (events.Count > 0 && events[0].at <= s)
                {
                    var fn = events[0].fn; events.RemoveAt(0);
                    try { fn(); } catch (Exception err) { Errors.Add(err); } // '[cine] event failed'
                }
                var P = Pose(shot, Math.Min(1, s / shot.Dur), s);
                double tx = P.T[0], ty = P.T[1], tz = P.T[2];
                if (!rm && hand != 0)
                {
                    tx += hand * (Math.Sin(s * 1.3) + 0.5 * Math.Sin(s * 3.1 + 1));
                    ty += hand * 0.7 * Math.Sin(s * 1.7 + 2);
                    tz += hand * Math.Sin(s * 0.9 + 4);
                }
                if (P.Fov.HasValue) _fov = P.Fov.Value;
                E.SetCameraOverride(new CameraShot { X = P.P[0], Y = P.P[1], Z = P.P[2], TargetX = tx, TargetY = ty, TargetZ = tz, Fov = _fov });
                if (shot.Fill != null)
                {
                    var at = shot.Fill.At?.Invoke();
                    if (at != null) _fx?.Fill(at[0], at[1], at[2], shot.Fill.I ?? 6, shot.Fill.Color);
                }
                if (s >= shot.Dur || _skipped) Finish();
            };
            E.Frame += onFrame;
            return tcs.Task;
        }

        /// <summary>
        /// Play shots in order; completes with "done" or "skipped" ("busy" if one is already playing). Controls are off
        /// and the HUD is cinematic meanwhile; the controls come back as they were (a cutscene inside a modal leaves them off).
        /// </summary>
        public virtual async Task<string> Play(IReadOnlyList<CineShot> shots, IReadOnlyList<string> title = null)
        {
            if (_busy) return "busy";
            _busy = true;
            _skipped = false;
            var E = Ctx.Engine;
            bool wasOn = E.ControlsEnabled;
            E.SetControls(false); Ctx.Hud.SetCinematic(true); _fx?.Overlay(true);
            E.SetModelHidden(true);
            Title(title);
            bool skipped;
            try { foreach (var shot in shots) { if (_skipped) break; await RunShot(shot); } }
            finally
            {
                skipped = _skipped;
                E.SetCameraOverride(null); E.TimeScale = 1; _ = E.FadeTo(0, 0); E.SetGrade(new JObject { ["exposure"] = 0, ["seconds"] = 0.6 });
                foreach (var o in _live) { try { o.Dispose(); } catch (Exception err) { Errors.Add(err); } }
                _live.Clear();
                _fx?.Overlay(false);
                Title(null);
                if (skipped) Ctx.Hud.ClearCaption();
                Ctx.Hud.SetCinematic(false); E.SetModelHidden(false); E.SetControls(wasOn);
                _busy = false;
                _skipped = false;
            }
            return skipped ? "skipped" : "done";
        }

        // ------------------------------------------------------------------ actors and effects

        sealed class Disposer : IDisposable
        {
            Action _a;
            public Disposer(Action a) { _a = a; }
            public void Dispose() { var a = _a; _a = null; a?.Invoke(); }
        }

        /// <summary>A walk zone of a level (Data/Source/&lt;level&gt;-level.json "walk"): flat at Y, or a ramp from Y0 to Y1 along Axis.</summary>
        public readonly struct WalkZone
        {
            public readonly double X0, X1, Z0, Z1, Y, Y0, Y1;
            public readonly string Axis;
            public WalkZone(double x0, double x1, double z0, double z1, double y, string axis = null, double y0 = 0, double y1 = 0)
            { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; Y = y; Axis = axis; Y0 = y0; Y1 = y1; }
        }

        /// <summary>The town's walk zones (town-level.json): roads at 0, the blocks' sidewalks at 0.12, ramps and steps.</summary>
        public static readonly WalkZone[] TownWalk =
        {
            new WalkZone(-130, 130, -6, 6, 0),
            new WalkZone(-54, -46, -110, -6, 0),
            new WalkZone(-54, -46, 6, 110, 0),
            new WalkZone(46, 54, -110, -6, 0),
            new WalkZone(46, 54, 6, 110, 0),
            new WalkZone(-46, 46, -64, -56, 0),
            new WalkZone(-46, 46, 56, 64, 0),
            new WalkZone(-130, -54, -110, -6, 0.12),
            new WalkZone(-130, -54, 6, 13.6, 0.12),
            new WalkZone(-130, -54, 16, 46, 0.12),
            new WalkZone(-130, -54, 54, 110, 0.12),
            new WalkZone(-130, -84.6, 46, 54, 0.12),
            new WalkZone(-75.4, -54, 46, 54, 0.12),
            new WalkZone(-130, -112, 13.6, 16, 0.12),
            new WalkZone(-101.8, -87.3, 13.6, 16, 0.12),
            new WalkZone(-84.7, -54, 13.6, 16, 0.12),
            new WalkZone(54, 130, -110, -6, 0.12),
            new WalkZone(54, 130, 6, 110, 0.12),
            new WalkZone(-46, 46, -110, -64, 0.12),
            new WalkZone(-46, 46, -56, -6, 0.12),
            new WalkZone(-46, 46, 6, 56, 0.12),
            new WalkZone(-46, 46, 64, 110, 0.12),
            new WalkZone(-84.3, -75.7, 46, 54, 0, "z", -0.9, 0.12),
            new WalkZone(-109.6, -101.8, 13.75, 16, 0, "x", -0.5, 0.12),
            new WalkZone(-112, -109.6, 13.75, 16, -0.5),
            new WalkZone(-125.5, -110.5, -16, -13.4, 0.2),
            new WalkZone(-3.4, 1.4, 35.6, 40.4, 0.52),
            new WalkZone(-4, -3.4, 36.3, 37.5, 0.32),
        };

        /// <summary>engine/geom.js groundAt(walk, x, z, feetY, any = true): the highest walk surface over (x, z), or null.</summary>
        public static double? GroundOf(IReadOnlyList<WalkZone> walk, double x, double z)
        {
            double? best = null;
            foreach (var w in walk)
            {
                if (x < w.X0 - 0.02 || x > w.X1 + 0.02 || z < w.Z0 - 0.02 || z > w.Z1 + 0.02) continue;
                double y = w.Y;
                if (w.Axis != null)
                {
                    double span = w.Axis == "x" ? w.X1 - w.X0 : w.Z1 - w.Z0;
                    if (span == 0) span = 1;
                    double k = Math.Min(1, Math.Max(0, (w.Axis == "x" ? x - w.X0 : z - w.Z0) / span));
                    y = w.Y0 + (w.Y1 - w.Y0) * k;
                }
                if (best == null || y > best) best = y;
            }
            return best;
        }

        /// <summary>Ground height for an actor (the original's groundAt(L.walk, x, z, 0.5, true) ?? 0). The town's walk
        /// zones are known here; elsewhere the actor stands at the player's height (the host may override).</summary>
        protected virtual double GroundAt(double x, double z) => Ctx.Engine.LevelId == "town" ? GroundOf(TownWalk, x, z) ?? 0 : Ctx.Engine.PlayerY;

        /// <summary>The height an actor of this cutscene stands at (IStoryPerson has no Y; the original read group.position.y).</summary>
        public double ActorY(IStoryPerson p) => p != null && _actorY.TryGetValue(p, out var y) ? y : 0;

        /// <summary>Someone in the shot: stands at At or walks Path at Speed, in the current level; removed when the cutscene ends.</summary>
        public virtual IStoryPerson Actor(string preset, ActorOptions o = null)
        {
            o ??= new ActorOptions();
            var E = Ctx.Engine;
            var path = o.Path;
            var start = path != null && path.Count > 0 ? path[0] : o.At ?? (0, 0);
            var p = E.SpawnPerson(preset, start.x, GroundAt(start.x, start.z), start.z, o.Yaw);
            _actorY[p] = GroundAt(start.x, start.z);
            foreach (var n in o.Props ?? Array.Empty<string>()) p.SetProp(n, true);
            p.SetAnim(o.Anim);
            double x = start.x, z = start.z, yaw = o.Yaw, wait = o.Delay;
            int i = 1;
            bool walking = false;
            Action<double> onFrame = dt =>
            {
                if (path == null || i >= path.Count || (wait -= dt) > 0) return;
                var (tx, tz) = path[i];
                double dx = tx - x, dz = tz - z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < 0.2)
                {
                    if (++i >= path.Count) { p.SetAnim("idle", 0); walking = false; }
                }
                else
                {
                    double st = Math.Min(d, o.Speed * dt);
                    x += dx / d * st; z += dz / d * st;
                    double want = Math.Atan2(-dx, -dz);
                    yaw += Math.Atan2(Math.Sin(want - yaw), Math.Cos(want - yaw)) * Math.Min(1, dt * 8);
                    double gy = GroundAt(x, z);
                    _actorY[p] = gy;
                    p.SetPosition(x, gy, z, yaw);
                    if (!walking) { p.SetAnim("walk", o.Speed); walking = true; }
                }
            };
            E.Frame += onFrame;
            _live.Add(new Disposer(() => { E.Frame -= onFrame; E.RemovePerson(p); _actorY.Remove(p); }));
            return p;
        }

        /// <summary>A missile coming down: from -> to in seconds (drawn by ICineFx; removed when the cutscene ends).</summary>
        public virtual IDisposable Missile(double[] from, double[] to, double seconds = 1)
        {
            var m = _fx?.Missile(from, to, seconds);
            var o = new Disposer(() => m?.Dispose());
            _live.Add(o);
            return o;
        }
    }
}
