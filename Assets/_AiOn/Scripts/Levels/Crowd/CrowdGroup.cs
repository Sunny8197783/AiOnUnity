using System;
using System.Collections.Generic;
using AiOn.Game;
using static AiOn.Levels.Crowd.CrowdGeom;

namespace AiOn.Levels.Crowd
{
    /// <summary>One member of a crowd group (crowd.js agent record).</summary>
    public sealed class CrowdAgent
    {
        public int I;
        public CrowdGroup G;
        public CrowdBody Body;
        public double X, Z, Vx, Vz, Yaw;
        public double? Face;
        public bool On, Placed, Shown;
        public string State = CrowdGroup.StStand;
        public int Path = -1;
        public double S;
        public int Dir = 1;
        public double Lane, Speed = 1.2;
        public bool HasGoal;
        public double GoalX, GoalZ;
        public double Timer, StopT;
        public string Anim = "", StandAnim = "idle", MoveAnim = "walk";
        public bool Moving;
        public double Dy;
        public PropMask Props;
        public double Jit, Slow = 1;
        public double HurtT, FleeT, DownT, Fx, Fz, Detour, Side = 1, Nx, Nz, Calm = 9, Stuck;
        public bool Wait;
    }

    /// <summary>
    /// Line-for-line port of the original engine/crowd.js createCrowd: seeded people moving along polylines with
    /// separation steering, a keep-right lane, player avoidance, signal holds, queues and solid push-out.
    /// Pure logic in the original frame; the members are drawn from <see cref="Bodies"/>.
    /// Modes: stroll | brisk | queue | sit | idle | none (see crowd.js header for their options).
    /// </summary>
    public sealed class CrowdGroup
    {
        public const string StStand = "stand", StMove = "move", StLine = "line", StLeave = "leave", StGone = "gone";
        public static readonly string[] Modes = { "stroll", "brisk", "queue", "sit", "idle", "none" };
        static readonly string[] STAND = { "idle", "idle", "phone", "talk" };
        const double SEP = 0.62, AVOID = 1.05, RESPAWN_HIDE = 20, BODY = 0.24;

        public sealed class Hold
        {
            public Box2 Zone;
            public string Group;
            public bool Open = true;
        }

        public sealed class ModeOpts
        {
            public int? Count;
            public List<Pt[]> Paths;
            public Box2? Area;
            public bool? Instant;
            public Pt? At, Dir, Leave;
            public double? Spacing;
            public double Serve = 10;
            public string Carry;
            public List<Pt> Targets;
            /// <summary>brisk: respawn (default) | despawn | idle.</summary>
            public string Then = "respawn";
            /// <summary>brisk + respawn: seconds (plus up to as much again, random) before an arrived member walks in again; null 0.5-1.5 s.</summary>
            public double? RespawnDelay;
            public List<(PropMask prop, double prob)> Props;
            public List<(double x, double z, double? yaw)> Spots;
            public double? Standing, Y;
        }

        readonly Mulberry32 _r;
        readonly Func<double, double, double> _h;
        readonly SolidGrid _grid;
        readonly List<Hold> _holds;
        readonly List<CrowdAgent> _agents = new();
        readonly List<CrowdAgent> _sep;
        readonly List<CrowdBody> _bodies = new();
        readonly List<CrowdAgent> _line = new();
        readonly List<CrowdAgent> _list = new();
        readonly List<SolidGrid.Solid> _qa = new();
        readonly List<(int qi, double s, int d)> _next = new();
        readonly List<CrowdPath> _paths = new();
        Box2? _area;
        string _mode = "none";
        ModeOpts M = new();
        int _active, _capNow;
        double _serveT;
        Pt _dirN, _leaveN, _at;
        bool _visible = true, _warned;
        // the viewer (camera) and the player of the current frame
        bool _hasViewer, _hasPlayer;
        double _vx, _vz, _plx, _plz;
        // crowd.js tmp
        double _tx0, _tz0, _ttx, _ttz = 1;

        public CrowdGroup(int count, int seed, Func<double, double, double> heightAt = null, SolidGrid solids = null, List<Hold> holds = null,
            List<CrowdAgent> peers = null, CrowdTier.Profile? tier = null)
        {
            var P0 = tier ?? CrowdTier.Current;
            _r = new Mulberry32(SeedOf(seed));
            _h = heightAt ?? ((x, z) => 0);
            _grid = solids;
            _holds = holds;
            _capNow = CrowdTier.Current.Cap;
            int n = Math.Max(0, Math.Min(CrowdTier.Share(Math.Max(0, count), P0.Scale), P0.Cap));
            for (int i = 0; i < n; i++)
            {
                var a = new CrowdAgent { I = i, G = this, Body = new CrowdBody(seed * 1000 + i + 1) };
                _agents.Add(a);
                _bodies.Add(a.Body);
            }
            if (peers != null) peers.AddRange(_agents);
            _sep = peers ?? _agents;
        }

        /// <summary>The group's rng seed: ((seed * 2654435761) ^ 0x5bd1e995) &gt;&gt;&gt; 0.</summary>
        public static uint SeedOf(int seed) => unchecked((uint)((ulong)(uint)seed * 2654435761UL)) ^ 0x5bd1e995u;

        public string Mode => _mode;
        public int Active => _active;
        public IReadOnlyList<CrowdAgent> Agents => _agents;
        public IReadOnlyList<CrowdBody> Bodies => _bodies;
        public IReadOnlyList<CrowdAgent> Line => _line;
        public IReadOnlyList<CrowdPath> Paths => _paths;
        public double ServeT => _serveT;
        public string CarryProp => M.Carry;

        /// <summary>A hidden group draws nothing, does not update, and its members are skipped by peers' separation.</summary>
        public bool Visible
        {
            get => _visible;
            set { _visible = value; foreach (var a in _agents) a.Body.Visible = a.Shown && value; }
        }

        double R() => _r.Next();
        T Pick<T>(IReadOnlyList<T> arr) => arr[(int)(R() * arr.Count)];

        void Show(CrowdAgent a, bool on) { a.Shown = on; a.Body.Visible = on && _visible; }
        void SetProps(CrowdAgent a, PropMask want) { a.Props = want; a.Body.Props = want; }

        void RandomPathPoint(CrowdAgent a)
        {
            double total = 0; foreach (var P in _paths) total += P.Len;
            double u = R() * total; int pi = 0;
            while (pi < _paths.Count - 1 && u > _paths[pi].Len) u -= _paths[pi++].Len;
            a.Path = pi; a.S = Clamp(u, 0, _paths[pi].Len);
        }

        Pt AreaPoint()
        {
            if (_area is Box2 b) { double x = b.X0 + R() * (b.X1 - b.X0); return new Pt(x, b.Z0 + R() * (b.Z1 - b.Z0)); }
            double px = (R() - 0.5) * 10; return new Pt(px, (R() - 0.5) * 10);
        }

        void AtTmp(CrowdPath P, double s) => P.At(s, out _tx0, out _tz0, out _ttx, out _ttz);

        /// <summary>Carrot on the path, offset to the walker's right (keep right on sidewalks).</summary>
        (double x, double z) LaneGoal(CrowdAgent a)
        {
            AtTmp(_paths[a.Path], a.S);
            double dx = _ttx * a.Dir, dz = _ttz * a.Dir;
            return (_tx0 - dz * a.Lane, _tz0 + dx * a.Lane);
        }

        static void PlaceAt(CrowdAgent a, double x, double z, double? yaw = null)
        {
            a.X = x; a.Z = z; a.Vx = a.Vz = 0; if (yaw.HasValue) a.Yaw = yaw.Value; a.Placed = true;
        }

        // ---------- per-mode setup ----------

        void SetupPath(CrowdAgent a, bool keep, bool brisk)
        {
            if (keep) { var pr = Project(_paths, a.X, a.Z); a.Path = pr.pi; a.S = pr.s; } else RandomPathPoint(a);
            AtTmp(_paths[a.Path], a.S);
            double ahead = -Math.Sin(a.Yaw) * _ttx - Math.Cos(a.Yaw) * _ttz; // keep walking the way they face
            a.Dir = brisk ? 1 : keep ? (ahead >= 0 ? 1 : -1) : (R() < 0.5 ? 1 : -1);
            a.Lane = brisk ? (R() - 0.5) * 1.4 : 0.2 + R() * 0.4;
            if (!keep) { var (gx, gz) = LaneGoal(a); PlaceAt(a, gx, gz, Math.Atan2(-_ttx * a.Dir, -_ttz * a.Dir)); }
        }

        /// <summary>Queue slot k: at + dir * (0.5 + k * spacing).</summary>
        public Pt Slot(int k)
        {
            double s = M.Spacing ?? 0.75;
            return new Pt(_at.X + _dirN.X * (0.5 + k * s), _at.Z + _dirN.Z * (0.5 + k * s));
        }

        /// <summary>Where a served member walks to (at + leave * 18).</summary>
        public Pt LeaveGoal => new(_at.X + _leaveN.X * 18, _at.Z + _leaveN.Z * 18);
        /// <summary>Facing of the line members (toward the head).</summary>
        public double LineFace => Math.Atan2(_dirN.X, _dirN.Z);

        void SetupSit(List<CrowdAgent> list)
        {
            var spots = new List<(double x, double z, double? yaw, double k)>();
            if (M.Spots != null && M.Spots.Count > 0) foreach (var s in M.Spots) spots.Add((s.x, s.z, s.yaw, 0));
            else if (_area is Box2 ar)
            {
                double sp = M.Spacing ?? 1.5;
                for (double z = ar.Z0 + sp / 2; z < ar.Z1; z += sp)
                    for (double x = ar.X0 + sp / 2; x < ar.X1; x += sp) { double jx = x + (R() - 0.5) * 0.3; spots.Add((jx, z + (R() - 0.5) * 0.3, null, 0)); }
            }
            else { UnityEngine.Debug.LogError("[crowd] sit mode needs opts.spots or an area (setArea / opts.area)"); spots.Add((0, 0, null, 0)); }
            for (int i = 0; i < spots.Count; i++) { var s = spots[i]; s.k = R(); spots[i] = s; }
            spots.Sort((p, q) => p.k.CompareTo(q.k));
            for (int k = 0; k < list.Count; k++)
            {
                var a = list[k];
                if (k >= spots.Count) { a.State = StGone; Show(a, false); continue; }
                var s = spots[k];
                double yaw = s.yaw.HasValue ? s.yaw.Value + (R() - 0.5) * 0.4 : Pick(new[] { 0, 1, 2, 3 }) * Math.PI / 2 + (R() - 0.5) * 0.6;
                bool standing = R() < (M.Standing ?? 0.12);
                a.State = StStand; a.HasGoal = false;
                if (standing) { a.StandAnim = Pick(new[] { "idle", "phone", "talk" }); a.Dy = 0; PlaceAt(a, s.x + Math.Cos(yaw) * 0.65, s.z - Math.Sin(yaw) * 0.65, yaw); }
                else { a.StandAnim = "sit"; a.Dy = M.Y ?? 0.03; PlaceAt(a, s.x, s.z, yaw); }
                a.Face = a.Yaw;
            }
        }

        double FaceNeighbour(CrowdAgent a)
        {
            CrowdAgent best = null; double bd = 9;
            foreach (var b in _agents)
                if (b != a && b.On && b.State != StGone)
                {
                    double d = (b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z);
                    if (d < bd) { bd = d; best = b; }
                }
            return best != null ? Math.Atan2(-(best.X - a.X), -(best.Z - a.Z)) : a.Yaw;
        }

        void ApplyMode()
        {
            _capNow = CrowdTier.Current.Cap; // a lower tier at run time shows fewer of the members built
            int want = _mode == "none" ? 0 : Math.Max(0, Math.Min(Math.Min(M.Count ?? _agents.Count, _agents.Count), _capNow));
            _active = want;
            bool instant = M.Instant ?? (_mode == "queue" || _mode == "sit");
            _list.Clear();
            for (int k = 0; k < _agents.Count; k++)
            {
                var a = _agents[k];
                if (k >= want) { a.On = false; Show(a, false); SetProps(a, PropMask.None); continue; }
                bool keep = !instant && a.On && a.Placed && a.State != StGone;
                a.On = true; a.Dy = 0; a.Face = null; a.HasGoal = false; a.StopT = 4 + R() * 16; a.Timer = 0;
                var props = PropMask.None;
                if (M.Props != null) foreach (var (prop, prob) in M.Props) if (R() < prob) props |= prop;
                SetProps(a, props);
                _list.Add(a);
                if (_mode == "stroll" || _mode == "brisk")
                {
                    bool brisk = _mode == "brisk";
                    a.Speed = (brisk ? 1.55 + R() * 0.45 : 1.0 + R() * 0.4) * a.Slow;
                    a.MoveAnim = brisk ? "brisk" : "walk"; a.StandAnim = brisk ? "idle" : Pick(STAND); a.State = StMove;
                    if (_paths.Count > 0) SetupPath(a, keep, brisk);
                    else
                    {
                        a.Path = -1;
                        if (!keep) { var p = AreaPoint(); PlaceAt(a, p.X, p.Z, R() * TAU); }
                        if (brisk && M.Targets.Count == 0 && !_warned) { _warned = true; UnityEngine.Debug.LogWarning("[crowd] brisk mode without paths or targets: members stroll in the area"); }
                    }
                }
                else if (_mode == "idle")
                {
                    if (!keep) { if (_paths.Count > 0) SetupPath(a, false, false); else { var p = AreaPoint(); PlaceAt(a, p.X, p.Z, R() * TAU); } }
                    a.State = StStand; a.StandAnim = Pick(STAND);
                }
                else if (_mode == "queue")
                {
                    a.Speed = 0.9 * a.Slow; a.MoveAnim = "walk"; a.StandAnim = R() < 0.3 ? "phone" : "idle"; a.State = StLine; a.Jit = (R() - 0.5) * 0.18;
                    if (!a.Placed) { var p = AreaPoint(); PlaceAt(a, p.X, p.Z); }
                }
                if (!keep) { a.Anim = a.StandAnim; a.Moving = false; a.Body.Snap = true; }
                Show(a, true);
            }
            if (_mode == "sit") SetupSit(_list);
            if (_mode == "idle") foreach (var a in _list) a.Face = a.StandAnim == "talk" ? FaceNeighbour(a) : a.Yaw;
            if (_mode == "queue")
            {
                if (M.At == null)
                {
                    UnityEngine.Debug.LogError("[crowd] queue mode needs opts.at {x,z}");
                    M.At = _area is Box2 b ? new Pt((b.X0 + b.X1) / 2, (b.Z0 + b.Z1) / 2) : new Pt(0, 0);
                }
                _at = M.At.Value;
                var d = M.Dir ?? new Pt(0, 1); double l = Hypot(d.X, d.Z); if (l == 0) l = 1;
                _dirN = new Pt(d.X / l, d.Z / l);
                var lv = M.Leave ?? new Pt(-_dirN.Z, _dirN.X); double ll = Hypot(lv.X, lv.Z); if (ll == 0) ll = 1;
                _leaveN = new Pt(lv.X / ll, lv.Z / ll);
                _line.Clear();
                _line.AddRange(_list);
                var at = _at;
                _line.Sort((p, q) => // stable, like Array.prototype.sort
                {
                    int c = Hypot(p.X - at.X, p.Z - at.Z).CompareTo(Hypot(q.X - at.X, q.Z - at.Z));
                    return c != 0 ? c : p.I.CompareTo(q.I);
                });
                if (instant)
                    for (int k = 0; k < _line.Count; k++)
                    {
                        var a = _line[k]; var s = Slot(k);
                        PlaceAt(a, s.X + _dirN.Z * a.Jit, s.Z - _dirN.X * a.Jit, Math.Atan2(_dirN.X, _dirN.Z));
                    }
                _serveT = M.Serve * (0.5 + R());
            }
            foreach (var a in _list) if (a.State != StGone) Pose(a, 0);
        }

        // ---------- behaviour ----------

        void EndOfPath(CrowdAgent a)
        {
            var P = _paths[a.Path];
            Pt end = a.Dir > 0 ? P.Last : P.First;
            if (_mode == "brisk") { Arrive(a, end); return; }
            _next.Clear();
            for (int qi = 0; qi < _paths.Count; qi++)
            {
                if (qi == a.Path) continue;
                var Q = _paths[qi];
                if (Hypot(Q.First.X - end.X, Q.First.Z - end.Z) < 4) _next.Add((qi, 0, 1));
                if (Hypot(Q.Last.X - end.X, Q.Last.Z - end.Z) < 4) _next.Add((qi, Q.Len, -1));
            }
            if (_next.Count > 0 && R() > 0.15) { var (qi, s, d) = Pick(_next); a.Path = qi; a.S = s; a.Dir = d; }
            else { a.Dir = -a.Dir; a.S = Clamp(a.S, 0, P.Len); if (R() < 0.5) Stop(a, 2 + R() * 4); }
        }

        void Arrive(CrowdAgent a, Pt end)
        {
            if (M.Then == "idle")
            {
                a.State = StStand; a.HasGoal = false; a.StandAnim = Pick(new[] { "idle", "idle", "phone" });
                a.Face = Math.Atan2(-(end.X - a.X), -(end.Z - a.Z)); return;
            }
            a.State = StGone; a.Timer = M.RespawnDelay is double rd ? rd + R() * rd : 0.5 + R(); Show(a, false);
        }

        void Stop(CrowdAgent a, double s)
        {
            a.State = StStand; a.Timer = s; a.StandAnim = Pick(STAND); a.Face = a.StandAnim == "talk" ? FaceNeighbour(a) : (double?)null;
        }

        void Respawn(CrowdAgent a, double dt)
        {
            if (M.Then != "respawn" || _paths.Count == 0) return;
            if ((a.Timer -= dt) > 0) return;
            a.Timer = 0.6;
            int pi = (int)(R() * _paths.Count); var st = _paths[pi].First;
            if (_hasViewer && Hypot(st.X - _vx, st.Z - _vz) < RESPAWN_HIDE) return;
            a.Path = pi; a.S = 0; a.Dir = 1; a.Lane = (R() - 0.5) * 1.4;
            var (gx, gz) = LaneGoal(a);
            PlaceAt(a, gx, gz, Math.Atan2(-_ttx, -_ttz)); a.State = StMove; Show(a, true);
        }

        void QueueTick(double dt)
        {
            _serveT -= dt;
            if (_serveT <= 0 && _line.Count > 1)
            {
                _serveT = M.Serve * (0.7 + 0.6 * R());
                var a = _line[0]; _line.RemoveAt(0);
                a.State = StLeave; a.HasGoal = true; var g = LeaveGoal; a.GoalX = g.X; a.GoalZ = g.Z;
                if (!string.IsNullOrEmpty(M.Carry)) { SetProps(a, a.Props | Props.Of(M.Carry)); if (M.Carry == "waterJug") a.MoveAnim = "carry"; }
            }
        }

        // Out of the level's solids: push the body circle out, drop the velocity into the obstacle (slide along it), and
        // when it was met head-on, walk round it for a moment to the side the goal lies on.
        void Resolve(CrowdAgent a, double gx, double gz, double v)
        {
            if (_grid == null) return;
            _grid.Query(a.X - 0.6, a.X + 0.6, a.Z - 0.6, a.Z + 0.6, _qa);
            if (_qa.Count == 0) return;
            var (cx, cz) = SolidGrid.Collide(a.X, a.Z, _h(a.X, a.Z), _qa, BODY, 1.7);
            double nx = cx - a.X, nz = cz - a.Z, l = Hypot(nx, nz);
            if (l < 1e-6) return;
            a.X = cx; a.Z = cz;
            double ux = nx / l, uz = nz / l, vn = a.Vx * ux + a.Vz * uz;
            if (vn < 0) { a.Vx -= vn * ux; a.Vz -= vn * uz; }
            if (v > 0.3 && a.Detour <= 0 && Hypot(a.Vx, a.Vz) < 0.25 * v)
            {
                bool again = a.Calm < 1.5; // blocked again right after a side-step: the other side, a little longer
                a.Nx = ux; a.Nz = uz; a.Detour = again ? 1.4 : 0.9; a.Calm = 0;
                a.Side = again ? -a.Side : (gx - a.X) * -uz + (gz - a.Z) * ux >= 0 ? 1 : -1;
                if (again && a.Path >= 0) a.Lane = Clamp(a.Lane + (R() < 0.5 ? -0.5 : 0.5), -1.2, 1.2);
                if (again && (a.Stuck += 2) > 6 && _mode == "stroll" && a.Path >= 0) { a.Dir = -a.Dir; a.Stuck = 0; } // the way is shut: turn back
            }
        }

        // At a hold that is not open: true while the next stretch of the path enters the zone and the member is not in it yet.
        bool Held(CrowdAgent a)
        {
            if (_holds == null || _holds.Count == 0) return false;
            var P = _paths[a.Path]; double s1 = Clamp(a.S + a.Dir * 1.6, 0, P.Len);
            AtTmp(P, s1);
            double dx = _ttx * a.Dir, dz = _ttz * a.Dir, nx = _tx0 - dz * a.Lane, nz = _tz0 + dx * a.Lane;
            foreach (var h in _holds)
                if (!h.Open && h.Zone.Contains(nx, nz) && !h.Zone.Contains(a.X, a.Z)) { AtTmp(P, a.S); return true; }
            AtTmp(P, a.S);
            return false;
        }

        /// <summary>Steer toward (gx, gz) at speed v with separation and player avoidance.</summary>
        double Steer(CrowdAgent a, double gx, double gz, double v, double dt)
        {
            double dx = gx - a.X, dz = gz - a.Z, d = Hypot(dx, dz);
            double wx = 0, wz = 0;
            if (d > 0.04) { double k0 = Math.Min(v, d * 1.8) / d; wx = dx * k0; wz = dz * k0; }
            if (a.Detour > 0) { a.Detour -= dt; wx += -a.Nz * a.Side * v + a.Nx * 0.3 * v; wz += a.Nx * a.Side * v + a.Nz * 0.3 * v; }
            else { a.Calm += dt; if (a.Calm > 3) a.Stuck = 0; }
            for (int i = 0; i < _sep.Count; i++)
            {
                var b = _sep[i];
                if (b == a || !b.On || b.State == StGone || !b.G._visible) continue;
                double ex = a.X - b.X, ez = a.Z - b.Z, e2 = ex * ex + ez * ez;
                if (e2 < SEP * SEP && e2 > 1e-6) { double e = Math.Sqrt(e2), f = (SEP - e) / SEP * 1.5 / e; wx += ex * f; wz += ez * f; }
            }
            if (_hasPlayer)
            {
                double ex = a.X - _plx, ez = a.Z - _plz, e = Hypot(ex, ez);
                if (e < AVOID * 2.4 && e > 1e-3 && d > 0.04 && (dx * -ex + dz * -ez) / d > 0.2 * e) // player ahead: step to the right
                {
                    double k1 = 0.6 * (1 - e / (AVOID * 2.4)) * v; wx += -dz / d * k1; wz += dx / d * k1;
                }
                if (e < AVOID && e > 1e-3) { double f = (AVOID - e) / AVOID * 2.2 / e; wx += ex * f; wz += ez * f; }
            }
            double k = Math.Min(1, dt * 5);
            a.Vx += (wx - a.Vx) * k; a.Vz += (wz - a.Vz) * k;
            a.X += a.Vx * dt; a.Z += a.Vz * dt;
            Resolve(a, gx, gz, v);
            return d;
        }

        void Tick(CrowdAgent a, double dt)
        {
            if (a.State == StGone) { Respawn(a, dt); return; }
            if (a.HurtT > 0 || a.DownT > 0 || a.FleeT > 0) // flees in a straight line, sliding along whatever is in the way
            {
                a.HurtT = Math.Max(0, a.HurtT - dt);
                if (a.DownT > 0) a.DownT -= dt;
                if (a.HurtT > 0 || a.DownT > 0) { Steer(a, a.X, a.Z, 0, dt); return; }
                a.FleeT -= dt;
                double fdx = a.X - a.Fx, fdz = a.Z - a.Fz, fd = Hypot(fdx, fdz); if (fd == 0) fd = 1;
                Steer(a, a.X + fdx / fd * 4, a.Z + fdz / fd * 4, 4.2, dt);
                return;
            }
            bool hasGoal = false; double gx = 0, gz = 0, v = a.Speed;
            if (_mode == "stroll" || _mode == "brisk")
            {
                if (a.State == StStand)
                {
                    a.Timer -= dt;
                    if (a.Timer <= 0 && (_mode == "stroll" || (a.Path < 0 && M.Targets.Count == 0))) a.State = StMove;
                }
                else if (a.Path >= 0)
                {
                    if (_mode == "stroll" && (a.StopT -= dt) <= 0) { a.StopT = 10 + R() * 20; if (R() < 0.3) Stop(a, 3 + R() * 6); }
                    var (lx, lz) = LaneGoal(a);
                    a.Wait = _mode == "stroll" && Held(a);
                    if (!a.Wait && Hypot(lx - a.X, lz - a.Z) < 1.4) a.S += a.Dir * a.Speed * dt * 1.15;
                    a.Face = a.Wait ? Math.Atan2(-_ttx * a.Dir, -_ttz * a.Dir) : a.Face;
                    if (a.Dir > 0 ? a.S >= _paths[a.Path].Len : a.S <= 0)
                    {
                        bool near = Hypot(lx - a.X, lz - a.Z) < (_mode == "brisk" ? 1.0 : 1.6);
                        if (near) EndOfPath(a);
                    }
                    if (a.State == StGone) return;
                    if (a.State == StMove) { (gx, gz) = LaneGoal(a); hasGoal = true; }
                }
                else if (_mode == "brisk" && M.Targets.Count > 0) // straight to the nearest target
                {
                    var t = M.Targets[0];
                    foreach (var q in M.Targets) if (Hypot(q.X - a.X, q.Z - a.Z) < Hypot(t.X - a.X, t.Z - a.Z)) t = q;
                    if (Hypot(t.X - a.X, t.Z - a.Z) < 1.0) Arrive(a, t); else { gx = t.X; gz = t.Z; hasGoal = true; }
                }
                else // wander in the area
                {
                    if (!a.HasGoal || Hypot(a.GoalX - a.X, a.GoalZ - a.Z) < 0.6)
                    {
                        if (a.HasGoal) Stop(a, 2 + R() * 4);
                        var p = AreaPoint(); a.HasGoal = true; a.GoalX = p.X; a.GoalZ = p.Z;
                    }
                    if (a.State == StMove) { gx = a.GoalX; gz = a.GoalZ; hasGoal = true; }
                }
            }
            else if (_mode == "queue")
            {
                if (a.State == StLine)
                {
                    int k = _line.IndexOf(a); var s = Slot(k);
                    gx = s.X + _dirN.Z * a.Jit; gz = s.Z - _dirN.X * a.Jit; hasGoal = true; v = 0.9 * a.Slow;
                    a.Face = Math.Atan2(_dirN.X, _dirN.Z);
                }
                else if (a.State == StLeave)
                {
                    gx = a.GoalX; gz = a.GoalZ; hasGoal = true; v = 1.1 * a.Slow;
                    if (Hypot(a.GoalX - a.X, a.GoalZ - a.Z) < 0.8) // out of the scene: rejoin at the visible tail
                    {
                        if (!string.IsNullOrEmpty(M.Carry)) SetProps(a, a.Props & ~Props.Of(M.Carry));
                        a.MoveAnim = "walk";
                        var s = Slot(_line.Count); PlaceAt(a, s.X, s.Z); a.State = StLine; _line.Add(a); hasGoal = false;
                    }
                }
            }
            if (hasGoal) Steer(a, gx, gz, v, dt);
            else Steer(a, a.X, a.Z, 0, dt); // standing: only step aside for neighbours and the player
        }

        /// <summary>Animation, facing and placement of one member (into its body).</summary>
        void Pose(CrowdAgent a, double dt)
        {
            double sp = Hypot(a.Vx, a.Vz);
            if (sp > 0.25) a.Moving = true; else if (sp < 0.12) a.Moving = false;
            string anim = a.DownT > 0 ? "down" : a.HurtT > 0 ? "hurt" : a.Moving ? (a.FleeT > 0 ? "run" : a.MoveAnim) : a.StandAnim;
            a.Anim = anim;
            double target = a.Moving ? Math.Atan2(-a.Vx, -a.Vz) : a.Face ?? a.Yaw;
            a.Yaw = dt > 0 ? LerpAngle(a.Yaw, target, Math.Min(1, dt * 5)) : target;
            var b = a.Body;
            b.X = a.X; b.Z = a.Z; b.Y = _h(a.X, a.Z) + a.Dy; b.Yaw = a.Yaw; b.Anim = anim;
            b.Speed = a.Moving ? sp : 0;
        }

        /// <summary>One frame (dt clamped to 0..0.1 s). The original's distance-based animation LOD is left to Unity's culling.</summary>
        public void Update(double dt, in CrowdFrame f)
        {
            dt = Math.Min(Math.Max(0, double.IsNaN(dt) ? 0 : dt), 0.1);
            if (f.HasCamera) { _hasViewer = true; _vx = f.Cx; _vz = f.Cz; }
            _hasPlayer = f.HasPlayer || _hasViewer;
            _plx = f.HasPlayer ? f.Px : _vx; _plz = f.HasPlayer ? f.Pz : _vz;
            if (CrowdTier.Current.Cap != _capNow && _mode != "none") ApplyMode();
            if (_active == 0 || dt == 0) return;
            if (_mode == "queue") QueueTick(dt);
            foreach (var a in _agents)
            {
                if (!a.On) continue;
                if (_mode != "sit") Tick(a, dt);
                if (a.State == StGone) continue;
                Pose(a, dt);
            }
        }

        /// <summary>Sets a mode; false (and an error) for an unknown one, the mode unchanged.</summary>
        public bool SetMode(string mode, ModeOpts o = null)
        {
            if (Array.IndexOf(Modes, mode) < 0) { UnityEngine.Debug.LogError($"[crowd] unknown mode \"{mode}\"; known: {string.Join(", ", Modes)}"); return false; }
            o ??= new ModeOpts();
            if (o.Paths != null) SetPaths(o.Paths, false);
            if (o.Area.HasValue) SetArea(o.Area.Value, false);
            _mode = mode; M = o; M.Targets ??= new List<Pt>();
            ApplyMode();
            return true;
        }

        public void SetPaths(IReadOnlyList<Pt[]> list, bool reapply = true)
        {
            _paths.Clear();
            foreach (var p in list) { var P = CrowdPath.Prep(p); if (P != null) _paths.Add(P); }
            if (reapply && _mode != "none") ApplyMode();
        }

        public void SetArea(Box2 r, bool reapply = true)
        {
            if (!(r.X1 > r.X0 && r.Z1 > r.Z0)) { UnityEngine.Debug.LogError("[crowd] setArea expects x0<x1, z0<z1"); return; }
            _area = r;
            if (reapply && (_mode == "sit" || ((_mode == "stroll" || _mode == "idle") && _paths.Count == 0))) ApplyMode();
        }

        /// <summary>Hit by the player: flinch, then run from (x, z); ko = knocked down for a while (PlayerHands).</summary>
        public void Hit(int agent, double x, double z, bool ko)
        {
            if (agent < 0 || agent >= _agents.Count) return;
            var a = _agents[agent];
            a.HurtT = 0.45; a.Fx = x; a.Fz = z; if (ko) a.DownT = 14; else a.FleeT = 7;
        }
    }
}
