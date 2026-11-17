using System;
using System.Collections.Generic;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using static AiOn.Levels.Crowd.CrowdGeom;
using static AiOn.Levels.Crowd.ShelterPeoplePlan;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// People in the shelter (port of the original levels/shelter-people.js buildPeople). Every evacuee owns a seat on a
    /// mat; arriving, leaving and toilet trips move that same person between an entrance and the seat. Plus the reception
    /// queue and two radio listeners. The manager, volunteer, 서연 and 304호 어르신 are the level scene's own NPCs
    /// (the manager's talk / idle comes from <see cref="ManagerAnim"/>).
    /// States: crowd arriving | settled | leaving, phase alert | calm, fire none | large, ways [entrances].
    /// </summary>
    public sealed class ShelterPeople : ICrowdLogic
    {
        enum Done { None, Seat, Hide, Away }

        sealed class Ev
        {
            public CrowdBody B;
            public Seat Home;
            public string Mode = "seat";
            public List<Wp> Path;
            public int I;
            public double Pause;
            public double? Face;
            public Done Done;
            public double Speed;
            public bool Brisk;
            public double Away;
            public int Rank;
            public double Qx, Qz, OffX, OffZ, DirX, DirZ;
            public Ev Wait;
        }

        readonly Mulberry32 _r = new(733);
        readonly WalkHeight _walk;
        readonly List<Ev> _ev = new();
        readonly List<Ev> _order = new();
        readonly List<CrowdBody> _listeners = new(), _queue = new();
        readonly List<CrowdBody> _bodies = new();
        readonly List<Ev> _pickList = new();
        readonly int _walkCap;
        string _mode = "settled", _phase = "calm";
        bool _fire;
        double _timer = 1;
        string[] _ways = Entrances;
        bool _hasLast;
        double _lastX, _lastZ;
        CrowdFrame _f;

        public ShelterPeople(WalkHeight walk, CrowdTier.Profile? tier = null)
        {
            var Q = tier ?? CrowdTier.Current;
            _walk = walk;
            _walkCap = Q.ShelterWalk;
            int n = Math.Min(Q.ShelterSeats, Seats.Length);
            for (int i = 0; i < n; i++)
            {
                var s = Seats[(int)Math.Floor((double)i * Seats.Length / n)];
                var e = new Ev { B = new CrowdBody(600 + i), Home = s, Speed = 0.95 + R() * 0.4, Qx = s.X, Qz = s.Z };
                Place(e.B, s.X, 0.012, s.Z, s.Yaw, "sit");
                _ev.Add(e); _bodies.Add(e.B);
            }
            _order.AddRange(_ev);
            for (int i = _order.Count - 1; i > 0; i--) { int j = (int)(R() * (i + 1)); (_order[i], _order[j]) = (_order[j], _order[i]); }
            for (int k = 0; k < _order.Count; k++) _order[k].Rank = k;

            // radio listeners on chairs RADIO + [-0.9, 0.2, -π/2] and [0.9, 0.25, π/2]
            var l0 = new CrowdBody(43); Place(l0, Radio.X - 0.9, 0, Radio.Z + 0.2, -Math.PI / 2, "sitChair");
            var l1 = new CrowdBody(76); Place(l1, Radio.X + 0.9, 0, Radio.Z + 0.25, Math.PI / 2, "sitChair");
            _listeners.Add(l0); _listeners.Add(l1);
            // the reception queue, clear of the reception point
            var qs = new[] { (1.25, -9.6), (1.45, -10.4), (1.6, -11.2) };
            int nq = Q.ShelterWalk > 2 ? 3 : 2;
            for (int i = 0; i < nq; i++)
            {
                var b = new CrowdBody(50 + i);
                Place(b, qs[i].Item1, 0, qs[i].Item2, Math.PI + (i > 0 ? (R() - 0.5) * 0.4 : 0), "idle");
                _queue.Add(b);
            }
            _bodies.AddRange(_listeners); _bodies.AddRange(_queue);
            Reset();
        }

        public IReadOnlyList<CrowdBody> Bodies => _bodies;
        public string Mode => _mode;
        public string Phase => _phase;
        /// <summary>The manager talks while arrivals come in or the phase is alert (applyPhase).</summary>
        public string ManagerAnim => _phase == "alert" || _mode == "arriving" ? "talk" : "idle";
        public int Seated { get { int k = 0; foreach (var e in _ev) if (e.Mode == "seat") k++; return k; } }
        public int Hidden { get { int k = 0; foreach (var e in _ev) if (!e.B.Visible) k++; return k; } }
        public int Walking { get { int k = 0; foreach (var e in _ev) if (e.Mode == "walk" || e.Mode == "away") k++; return k; } }
        public IReadOnlyList<CrowdBody> Queue => _queue;
        public IReadOnlyList<CrowdBody> Listeners => _listeners;
        public CrowdBody Evacuee(int i) => _ev[i].B;
        public Seat HomeOf(int i) => _ev[i].Home;

        double R() => _r.Next();
        T Pick<T>(IReadOnlyList<T> l) => l[(int)(R() * l.Count)];

        static void Place(CrowdBody b, double x, double y, double z, double yaw, string anim)
        {
            b.X = x; b.Y = y; b.Z = z; b.Yaw = yaw; b.Anim = anim; b.Speed = 0; b.Visible = true; b.Snap = true;
        }

        bool NearFire(Seat h) => _fire && Hypot(h.X - Fire.X, h.Z - Fire.Z) < FireClear;
        bool NearPlayer(Ev e) => _hasLast && Hypot(e.Home.X - _lastX, e.Home.Z - _lastZ) < 3;
        int Busy() { int k = 0; foreach (var e in _ev) if (e.Mode == "walk" || e.Mode == "away") k++; return k; }

        void SeatDown(Ev e, bool snap = false)
        {
            e.Mode = "seat"; e.Path = null; e.B.Visible = true;
            e.B.X = e.Home.X; e.B.Y = 0.012; e.B.Z = e.Home.Z;
            if (snap) e.B.Yaw = e.Home.Yaw;
            e.B.Anim = "sit"; e.B.Speed = 0; if (snap) e.B.Snap = true;
            e.B.Props &= ~PropMask.Backpack;
        }

        void Hide(Ev e) { e.Mode = "gone"; e.Path = null; e.B.Visible = false; }

        void Go(Ev e, List<Wp> path, Done done, bool fromSeat = false)
        {
            var b = e.B;
            e.Mode = "walk"; e.Path = path; e.Done = done; e.Face = null; b.Visible = true; e.OffX = e.OffZ = 0; e.Wait = null; e.DirX = e.DirZ = 0;
            e.Brisk = _phase == "alert" && _mode == "arriving";
            b.Speed = e.Speed * (e.Brisk ? 1.3 : 1);
            if (fromSeat) { e.I = 0; e.Pause = 1.0; b.Anim = "idle"; e.Qx = b.X; e.Qz = b.Z; } // stand up first
            else
            {
                var p0 = path[0];
                b.X = p0.X; b.Z = p0.Z; b.Y = H(p0.X, p0.Z); b.Yaw = YawTo(path[1].X - p0.X, path[1].Z - p0.Z); b.Snap = true;
                e.I = 1; e.Pause = 0; e.Qx = p0.X; e.Qz = p0.Z;
            }
        }

        double H(double x, double z) => _walk != null ? _walk.At(x, z) : 0;

        void Reset()
        {
            _timer = 1;
            foreach (var e in _ev)
            {
                if (NearFire(e.Home) || (_mode == "arriving" && e.Rank >= CrowdTier.Round(_ev.Count * 0.55))) Hide(e);
                else SeatDown(e, true);
            }
            foreach (var q in _queue) q.Visible = _mode == "arriving";
            for (int i = 0; i < _listeners.Count; i++) _listeners[i].Visible = _mode != "leaving" || i == 0;
            ApplyPhase();
        }

        void ApplyPhase()
        {
            bool alert = _phase == "alert";
            for (int i = 0; i < _queue.Count; i++) _queue[i].Anim = alert && i == 1 ? "phone" : "idle";
        }

        // Ramp B and ramp C ends are in plain view from the hall: use them only while the camera does not see that end
        // (more than 18 m away or over 60° off the view); otherwise another open way, or nobody this time.
        bool Seen(Pt p)
        {
            if (!_f.HasCamera) return false;
            double dx = p.X - _f.Cx, dz = p.Z - _f.Cz, d = Hypot(dx, dz);
            if (d > 18) return false;
            double f = Hypot(_f.Fx, _f.Fz); if (f == 0) f = 1;
            return (dx * _f.Fx + dz * _f.Fz) / (d * f) > 0.5;
        }

        readonly List<string> _wl = new(), _alt = new();
        string WayFor(string[] list)
        {
            _wl.Clear();
            foreach (var w in list) if (Array.IndexOf(_ways, w) >= 0) _wl.Add(w);
            if (_wl.Count == 0) _wl.AddRange(_ways);
            string pick = Pick(_wl);
            if ((pick != "ramp" && pick != "access") || !Seen(Ways[pick][0])) return pick;
            _alt.Clear();
            foreach (var k in _wl) if (k != "ramp" && k != "access") _alt.Add(k);
            return _alt.Count > 0 ? Pick(_alt) : null;
        }

        void Launch()
        {
            if (Busy() >= (_mode == "settled" ? Math.Min(2, _walkCap) : _walkCap)) return;
            if (_mode == "arriving")
            {
                Ev e = null;
                foreach (var x in _order) if (x.Mode == "gone" && !NearFire(x.Home)) { e = x; break; }
                string w = e != null ? WayFor(In) : null;
                if (w != null)
                {
                    if (R() < 0.5) e.B.Props |= PropMask.Backpack; else e.B.Props &= ~PropMask.Backpack;
                    Go(e, SeatPath(w, e.Home, true), Done.Seat);
                }
            }
            else if (_mode == "leaving")
            {
                if (Seated <= CrowdTier.Round(_ev.Count * 0.25)) return;
                Ev e = null;
                foreach (var x in _order) if (x.Mode == "seat" && !NearPlayer(x)) { e = x; break; }
                string w = e != null ? WayFor(Out) : null;
                if (w != null)
                {
                    e.B.Props |= PropMask.Backpack;
                    var p = SeatPath(w, e.Home); p.Reverse();
                    Go(e, p, Done.Hide, true);
                }
            }
            else if (!_fire && R() < 0.6) // settled: now and then someone goes to the toilets by the lift hall
            {
                _pickList.Clear();
                foreach (var x in _ev) if (x.Mode == "seat" && !NearPlayer(x)) _pickList.Add(x);
                if (_pickList.Count == 0) { R(); return; } // pick() of an empty list still draws
                var e = Pick(_pickList);
                var p = SeatPath("toilet", e.Home); p.Reverse();
                Go(e, p, Done.Away, true);
            }
        }

        static bool RingLeader(Ev e, Ev x)
        {
            int min = e.Rank;
            int k = 0;
            for (var c = x; c != null && k < 12; c = c.Wait, k++) { if (c == e) return e.Rank == min; min = Math.Min(min, c.Rank); }
            return false;
        }

        void Finish(Ev e)
        {
            var d = e.Done; e.Done = Done.None;
            switch (d)
            {
                case Done.Seat: SeatDown(e); break;
                case Done.Hide: Hide(e); break;
                case Done.Away: e.Mode = "away"; e.B.Visible = false; e.Away = 10 + R() * 12; break;
            }
        }

        void Step(Ev e, double dt)
        {
            var b = e.B;
            if (e.Pause > 0)
            {
                e.Pause -= dt;
                if (e.Face.HasValue) b.Yaw += Turn(b.Yaw, e.Face.Value) * Math.Min(1, dt * 5);
                if (e.Pause > 0) return;
                e.Face = null;
            }
            if (e.I >= e.Path.Count) { Finish(e); return; }
            var t = e.Path[e.I];
            double dx = t.X - e.Qx, dz = t.Z - e.Qz, d = Hypot(dx, dz);
            if (d < 0.06)
            {
                e.I++;
                if (t.Wait > 0) { e.Pause = t.Wait; e.Face = t.Face; b.Anim = t.Anim ?? "idle"; }
                return;
            }
            double ux = dx / d, uz = dz / d; e.DirX = ux; e.DirZ = uz;
            if (_f.HasPlayer) // wait for the player instead of walking through them
            {
                double px = _f.Px - b.X, pz = _f.Pz - b.Z;
                if (px * px + pz * pz < 1.2 && px * dx + pz * dz > 0) { b.Anim = "idle"; return; }
            }
            // Others ahead within PASS.near: hold behind someone going my way, crossing my way or stopped on my line
            // (a ring of holds: its lowest rank goes); step aside past anyone coming at me.
            Ev hold = null; int aside = 0;
            foreach (var x in _ev)
            {
                if (x == e || x.Mode != "walk") continue;
                double ox = x.B.X - b.X, oz = x.B.Z - b.Z, o = Hypot(ox, oz);
                if (ox * ux + oz * uz <= 0 || o > PassNear) continue;
                double lat = oz * ux - ox * uz, same = x.DirX * ux + x.DirZ * uz;
                bool stopped = x.Pause > 0 || x.Wait != null;
                if (same < -0.3 || stopped) { if (Math.Abs(lat) < PassSide * 2) aside = lat > 0.02 ? -1 : 1; if (same < -0.3) continue; }
                if (o < Gap && !(x.Pause <= 0 && RingLeader(e, x))) { hold = x; break; }
            }
            e.Wait = hold;
            if (hold != null) { b.Anim = "idle"; return; }
            // step aside (a world-space offset eased at PASS.rate), back onto the line otherwise and on the last leg
            double want = e.I < e.Path.Count - 1 ? aside * PassSide : 0, wx = -uz * want - e.OffX, wz = ux * want - e.OffZ, wd = Hypot(wx, wz);
            if (wd > 1e-4) { double m = Math.Min(1, PassRate * dt / wd); e.OffX += wx * m; e.OffZ += wz * m; }
            double k = Math.Min(d, e.Speed * (e.Brisk ? 1.3 : 1) * dt) / d;
            e.Qx += dx * k; e.Qz += dz * k;
            b.X = e.Qx + e.OffX; b.Z = e.Qz + e.OffZ; b.Y = H(b.X, b.Z);
            b.Yaw += Turn(b.Yaw, YawTo(dx, dz)) * Math.Min(1, dt * 7);
            double x0 = b.X, z0 = b.Z;
            bool flight = z0 < -17.45 && z0 > -19.97; // flight 1 rises north, flight 2 south
            bool f1 = flight && x0 > 3.6 && x0 < 4.95, f2 = flight && x0 > 5.05 && x0 < 6.4;
            b.Anim = f1 ? (dz < 0 ? "stairsUp" : "stairsDown") : f2 ? (dz > 0 ? "stairsUp" : "stairsDown") : e.Brisk ? "brisk" : "walk";
            b.Speed = e.Speed * (e.Brisk ? 1.3 : 1);
        }

        public bool Apply(string key, JToken v)
        {
            string s = v != null && v.Type == JTokenType.String ? (string)v : null;
            switch (key)
            {
                case "crowd":
                    if (s != "arriving" && s != "settled" && s != "leaving") { UnityEngine.Debug.LogError($"[shelter] crowd must be arriving|settled|leaving: {v}"); return true; }
                    _mode = s; Reset(); return true;
                case "phase":
                    if (s != "alert" && s != "calm") { UnityEngine.Debug.LogError($"[shelter] phase must be alert|calm: {v}"); return true; }
                    _phase = s; ApplyPhase(); return true;
                case "fire":
                    if (s != "none" && s != "large") return true;
                    _fire = s == "large"; Reset(); return true;
                case "ways":
                    {
                        var w = new List<string>();
                        if (v is JArray arr) { foreach (var t in arr) if (t.Type == JTokenType.String && !w.Contains((string)t)) w.Add((string)t); }
                        else if (s != null) w.Add(s);
                        if (w.Count == 0 || !w.TrueForAll(x => Array.IndexOf(Entrances, x) >= 0))
                        { UnityEngine.Debug.LogError($"[shelter] ways must be a non-empty array of {string.Join("|", Entrances)}: {v}"); return true; }
                        _ways = w.ToArray(); return true;
                    }
            }
            return false;
        }

        public void Update(double dt, in CrowdFrame f)
        {
            dt = Math.Min(Math.Max(0, dt), 0.1);
            _f = f;
            if (f.HasPlayer) { _hasLast = true; _lastX = f.Px; _lastZ = f.Pz; }
            _timer -= dt;
            if (_timer <= 0) { _timer = _mode == "arriving" ? 2.2 + R() * 1.5 : _mode == "leaving" ? 2.8 + R() * 2 : 7 + R() * 6; Launch(); }
            foreach (var e in _ev)
            {
                if (e.Mode == "walk") Step(e, dt);
                else if (e.Mode == "away") { e.Away -= dt; if (e.Away <= 0) Go(e, SeatPath("toilet", e.Home), Done.Seat); }
                else if (e.Mode == "seat") // turn back to the seat's facing after sitting down
                {
                    double a = Turn(e.B.Yaw, e.Home.Yaw);
                    if (Math.Abs(a) > 0.01) e.B.Yaw += a * Math.Min(1, dt * 4);
                }
            }
        }
    }
}
