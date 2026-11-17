using System;
using System.Collections.Generic;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// A level's solids on a uniform 4 m grid (engine/space.js: CELL 4, BIG 24) so a crowd member only tests the few
    /// boxes near it, plus the circle-vs-box push-out of engine/geom.js collide. Solids can be switched off by id
    /// (closures, the water point, the buses) without rebuilding the grid.
    /// </summary>
    public sealed class SolidGrid
    {
        public sealed class Solid
        {
            public double X0, X1, Y0, Y1, Z0, Z1;
            public string Id;
            public bool Off;
            internal int Stamp;
        }

        const double CELL = 4;
        const int BIG = 24;
        readonly Dictionary<long, List<Solid>> _buckets = new();
        readonly List<Solid> _big = new();
        readonly List<Solid> _all = new();
        int _q;

        public SolidGrid(IEnumerable<Solid> solids)
        {
            if (solids == null) return;
            foreach (var s in solids)
            {
                if (s == null) continue;
                _all.Add(s);
                int a = Cell(s.X0), b = Cell(s.X1), c = Cell(s.Z0), d = Cell(s.Z1);
                if (!(b - a < BIG && d - c < BIG)) { _big.Add(s); continue; }
                for (int x = a; x <= b; x++)
                    for (int z = c; z <= d; z++)
                    {
                        long k = Key(x, z);
                        if (!_buckets.TryGetValue(k, out var l)) _buckets[k] = l = new List<Solid>();
                        l.Add(s);
                    }
            }
        }

        /// <summary>The solids of a level layout (original frame).</summary>
        public static SolidGrid FromLayout(LevelLayout layout)
        {
            var list = new List<Solid>();
            if (layout != null)
                foreach (var s in layout.solids)
                    if (s != null && !s.IsEmpty)
                        list.Add(new Solid { X0 = s.x0, X1 = s.x1, Y0 = s.y0, Y1 = s.y1, Z0 = s.z0, Z1 = s.z1, Id = s.id, Off = s.off });
            return new SolidGrid(list);
        }

        public IReadOnlyList<Solid> All => _all;

        static int Cell(double v) => (int)Math.Floor(v / CELL);
        static long Key(int x, int z) => ((long)(x + 32768) << 20) ^ (z + 32768);

        /// <summary>Every solid whose cells meet the rect (switched-off ones included; Collide skips them).</summary>
        public void Query(double x0, double x1, double z0, double z1, List<Solid> into)
        {
            into.Clear();
            int q = ++_q;
            int a = Cell(x0), b = Cell(x1), c = Cell(z0), d = Cell(z1);
            for (int x = a; x <= b; x++)
                for (int z = c; z <= d; z++)
                    if (_buckets.TryGetValue(Key(x, z), out var l))
                        foreach (var s in l)
                        {
                            if (s.Stamp == q) continue;
                            s.Stamp = q;
                            if (s.X1 >= x0 && s.X0 <= x1 && s.Z1 >= z0 && s.Z0 <= z1) into.Add(s);
                        }
            foreach (var s in _big)
                if (s.X1 >= x0 && s.X0 <= x1 && s.Z1 >= z0 && s.Z0 <= z1) into.Add(s);
        }

        /// <summary>Switches every solid with this id off (true) or on (false).</summary>
        public void SetOff(string id, bool off)
        {
            foreach (var s in _all) if (s.Id == id) s.Off = off;
        }

        /// <summary>geom.js collide: pushes a circle out of every solid overlapping [feetY, feetY + h] (3 passes).</summary>
        public static (double x, double z) Collide(double px, double pz, double feetY, List<Solid> solids, double r = 0.32, double h = 1.72)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                foreach (var s in solids)
                {
                    if (s.Off || s.Y1 <= feetY + 0.05 || s.Y0 >= feetY + h) continue;
                    double cx = CrowdGeom.Clamp(px, s.X0, s.X1), cz = CrowdGeom.Clamp(pz, s.Z0, s.Z1), dx = px - cx, dz = pz - cz, d2 = dx * dx + dz * dz;
                    if (d2 >= r * r) continue;
                    if (d2 > 1e-10) { double d = Math.Sqrt(d2); px += dx / d * (r - d); pz += dz / d * (r - d); }
                    else
                    {
                        double l = px - s.X0, rr = s.X1 - px, f = pz - s.Z0, b = s.Z1 - pz, m = Math.Min(Math.Min(l, rr), Math.Min(f, b));
                        if (m == l) px = s.X0 - r; else if (m == rr) px = s.X1 + r; else if (m == f) pz = s.Z0 - r; else pz = s.Z1 + r;
                    }
                    moved = true;
                }
                if (!moved) break;
            }
            return (px, pz);
        }
    }
}
