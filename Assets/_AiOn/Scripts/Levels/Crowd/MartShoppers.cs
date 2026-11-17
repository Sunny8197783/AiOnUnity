using System;
using System.Collections.Generic;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using static AiOn.Levels.Crowd.CrowdGeom;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// The mart's shoppers (port of the original levels/mart.js 218-291): they wander the aisles with a cart or a basket,
    /// stop at a shelf, give way to each other and to the player; on 'busy' twice as many, plus a checkout queue.
    /// The original's rng(77) is shared with the mart build, so the draws here start from a fresh rng(77): the same
    /// layout and behaviour, not the same sequence. States: crowd normal | busy.
    /// </summary>
    public sealed class MartShoppers : ICrowdLogic
    {
        public static readonly double[] Aisles = { -14.26, -10.9, -7.5, -4.1, -0.7, 2.7, 6.2 };
        public const double Front = -7.4, Back = -21.3;

        sealed class Wp { public double X, Z, Wait; public double? Face; }

        sealed class Shopper
        {
            public CrowdBody B;
            public bool Cart;
            public double Speed, Off, Wait;
            public double? Face;
            public readonly List<Wp> Path = new();
            public bool Blocked, Held;
            public double Push, Stuck;
        }

        readonly Mulberry32 _r = new(77);
        readonly List<Shopper> _shoppers = new();
        readonly List<CrowdBody> _queue = new();
        readonly List<CrowdBody> _bodies = new();
        readonly List<SolidGrid.Solid> _low = new();
        readonly int _nNormal, _nBusy;
        string _mode = "normal";
        CrowdFrame _f;

        public MartShoppers(SolidGrid solids, CrowdTier.Profile? tier = null)
        {
            double q = (tier ?? CrowdTier.Current).Shop;
            _nNormal = (int)CrowdTier.Round(6 * q); _nBusy = (int)CrowdTier.Round(12 * q);
            if (solids != null) foreach (var s in solids.All) if (s.Y0 < 1 && !s.Off) _low.Add(s);
            for (int i = 0; i < _nBusy; i++)
            {
                // distinct starts (one per aisle end); the two draws stay so the rest of the seeded sequence is unchanged
                R(); R();
                bool cart = i % 3 == 0;
                double x = Aisles[i % Aisles.Length], z = i < Aisles.Length ? Front : Back;
                var b = new CrowdBody(300 + i) { X = x, Z = z, Yaw = R() * 6, Anim = "idle", Props = cart ? PropMask.Cart : PropMask.Basket, Snap = true };
                var s = new Shopper { B = b, Cart = cart };
                s.Speed = (cart ? 0.75 : 0.85) + R() * 0.35; s.Off = (R() - 0.5) * 0.6; s.Wait = R() * 3;
                b.Speed = s.Speed;
                _shoppers.Add(s); _bodies.Add(b);
            }
            var qd = new[] { (4.25, -3.8, -Math.PI / 2), (4.25, -5.4, Math.PI), (4.3, -6.0, Math.PI), (4.2, -6.6, Math.PI), (0.85, -2.2, Math.PI), (0.75, -5.8, Math.PI) };
            int nq = (int)CrowdTier.Round(6 * q);
            for (int i = 0; i < nq && i < qd.Length; i++)
            {
                var b = new CrowdBody(400 + i) { X = qd[i].Item1, Z = qd[i].Item2, Yaw = qd[i].Item3, Anim = i == 2 ? "phone" : "idle", Props = PropMask.Basket, Snap = true };
                _queue.Add(b); _bodies.Add(b);
            }
            ApplyCrowd();
        }

        public IReadOnlyList<CrowdBody> Bodies => _bodies;
        public string Mode => _mode;
        public int VisibleShoppers { get { int k = 0; foreach (var s in _shoppers) if (s.B.Visible) k++; return k; } }
        public int VisibleQueue { get { int k = 0; foreach (var b in _queue) if (b.Visible) k++; return k; } }
        public CrowdBody ShopperBody(int i) => _shoppers[i].B;

        double R() => _r.Next();

        void ApplyCrowd()
        {
            bool busy = _mode == "busy";
            for (int i = 0; i < _shoppers.Count; i++)
            {
                var s = _shoppers[i];
                s.B.Visible = i < (busy ? _nBusy : _nNormal);
                if (s.B.Visible && s.Wait == 0 && s.Path.Count == 0) s.B.Anim = "walk";
            }
            foreach (var b in _queue) b.Visible = busy;
        }

        void Plan(Shopper s)
        {
            var g = s.B;
            bool atFront = Math.Abs(g.Z - Front) < Math.Abs(g.Z - Back);
            double ax = Aisles[(int)(R() * Aisles.Length)] + s.Off, z0 = atFront ? Front : Back, z1 = atFront ? Back : Front;
            double zb = -9.5 - R() * 9;
            int side = ax > Aisles[6] - 0.5 ? 1 : R() < 0.5 ? -1 : 1;
            s.Path.Clear();
            s.Path.Add(new Wp { X = ax, Z = z0 });
            s.Path.Add(new Wp { X = ax, Z = zb, Wait = 2 + R() * 5, Face = YawTo(side, 0) });
            s.Path.Add(new Wp { X = ax, Z = z1 });
        }

        bool Open(double x, double z)
        {
            foreach (var o in _low) if (x > o.X0 - 0.3 && x < o.X1 + 0.3 && z > o.Z0 - 0.3 && z < o.Z1 + 0.3) return false;
            return true;
        }

        // Other people in the way: follow a slower one going the same way (stop 0.75 m behind), step aside (right-hand
        // rule, about 0.6 m clear) for someone oncoming or standing; a crossing walker yields to one on its right.
        // After 2.5 s of yielding a shopper only steps aside for a while, so nobody waits on anybody forever.
        bool Traffic(Shopper s, double ux, double uz, double dt)
        {
            var g = s.B; bool stop = false; double side = 0;
            int n = _shoppers.Count + _queue.Count;
            for (int k = 0; k < n; k++)
            {
                Shopper o = k < _shoppers.Count ? _shoppers[k] : null;
                var q = o != null ? o.B : _queue[k - _shoppers.Count];
                if (o == s || !q.Visible) continue;
                double rx = q.X - g.X, rz = q.Z - g.Z, dd = Hypot(rx, rz), ahead = rx * ux + rz * uz;
                if (dd > 1.2 || ahead <= 0) continue;
                double lat = rx * -uz + rz * ux; // + : on my right
                Wp t = o != null && o.Wait <= 0 && o.Path.Count > 0 ? o.Path[0] : null;
                double od = t != null ? Hypot(t.X - q.X, t.Z - q.Z) : 0;
                double? dot = od > 0.01 ? ((t.X - q.X) * ux + (t.Z - q.Z) * uz) / od : (double?)null; // null: standing still
                if (dot.HasValue && dot.Value > 0.5) { if (dd < 0.75 && Math.Abs(lat) < 0.45) stop = true; continue; }
                if (dot.HasValue && dot.Value > -0.5 && lat > 0 && dd < 0.9) { stop = true; continue; }
                if (Math.Abs(lat) < 0.6) side += (lat > 0.05 ? -1 : 1) * (0.6 - Math.Abs(lat)); // away from it, right if dead ahead
            }
            if (stop && s.Push <= 0) { s.Stuck += dt; if (s.Stuck > 2.5) { s.Stuck = 0; s.Push = 1.2; } else return true; }
            s.Push = Math.Max(0, s.Push - dt); if (!stop) s.Stuck = 0;
            if (side != 0)
            {
                double k = Math.Sign(side) * Math.Min(Math.Abs(side), s.Speed * dt * 0.9), nx = g.X - uz * k, nz = g.Z + ux * k;
                if (Open(nx, nz)) { g.X = nx; g.Z = nz; }
            }
            return false;
        }

        void StepShopper(Shopper s, double dt)
        {
            var g = s.B;
            if (s.Wait > 0)
            {
                s.Wait -= dt;
                if (s.Wait <= 0) g.Anim = "walk";
                else if (s.Face.HasValue) g.Yaw += Turn(g.Yaw, s.Face.Value) * Math.Min(1, dt * 4);
                return;
            }
            if (s.Path.Count == 0) Plan(s);
            var t = s.Path[0];
            double dx = t.X - g.X, dz = t.Z - g.Z, d = Hypot(dx, dz);
            if (d < 0.05)
            {
                s.Path.RemoveAt(0);
                if (t.Wait > 0) { s.Wait = t.Wait; s.Face = t.Face; g.Anim = "idle"; } else s.Face = null;
                return;
            }
            if (_f.HasPlayer)
            {
                double px = _f.Px - g.X, pz = _f.Pz - g.Z;
                bool block = Hypot(px, pz) < 1.15 && px * dx + pz * dz > 0;
                if (block != s.Blocked) { s.Blocked = block; g.Anim = block ? "idle" : "walk"; }
                if (block) return;
            }
            // the stop point is taken by someone already standing there: stop short, a step before them
            if (t.Wait > 0 && d < 1.6)
                foreach (var o in _shoppers)
                    if (o != s && o.B.Visible && o.Wait > 0 && Hypot(o.B.X - t.X, o.B.Z - t.Z) < 0.7)
                    { t.Z = Math.Min(-8.8, Math.Max(-19.2, t.Z + (g.Z > t.Z ? 0.9 : -0.9))); break; }
            bool held = Traffic(s, dx / d, dz / d, dt);
            if (held != s.Held) { s.Held = held; if (!s.Blocked) g.Anim = held ? "idle" : "walk"; }
            if (held) return;
            double k = Math.Min(d, s.Speed * dt) / d; g.X += dx * k; g.Z += dz * k;
            g.Yaw += Turn(g.Yaw, YawTo(dx, dz)) * Math.Min(1, dt * 7);
        }

        public bool Apply(string key, JToken v)
        {
            if (key != "crowd") return false;
            string m = v != null && v.Type == JTokenType.String ? (string)v : null;
            if (m != "normal" && m != "busy") { UnityEngine.Debug.LogError($"[mart] crowd must be normal|busy: {v}"); return true; }
            _mode = m; ApplyCrowd();
            return true;
        }

        public void Update(double dt, in CrowdFrame f)
        {
            dt = Math.Min(Math.Max(0, dt), 0.1);
            _f = f;
            foreach (var s in _shoppers) if (s.B.Visible) StepShopper(s, dt);
        }
    }
}
