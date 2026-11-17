using System;
using System.Collections.Generic;

namespace AiOn.Levels.Crowd
{
    // All crowd logic runs in the original frame (x east, z south, yaw 0 faces -z, positive yaw turns left).

    public struct Pt
    {
        public double X, Z;
        public Pt(double x, double z) { X = x; Z = z; }
        public override string ToString() => $"({X:0.###}, {Z:0.###})";
    }

    /// <summary>Axis-aligned ground rect. Contains is strict, like crowd.js inZone.</summary>
    public struct Box2
    {
        public double X0, X1, Z0, Z1;
        public Box2(double x0, double x1, double z0, double z1) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; }
        public bool Contains(double x, double z) => x > X0 && x < X1 && z > Z0 && z < Z1;
        public Box2 Grow(double m) => new(X0 - m, X1 + m, Z0 - m, Z1 + m);
    }

    /// <summary>A polyline with its arc lengths (crowd.js prepPath / at).</summary>
    public sealed class CrowdPath
    {
        public readonly Pt[] P;
        public readonly double[] Cum;
        public readonly double Len;

        CrowdPath(Pt[] p, double[] cum) { P = p; Cum = cum; Len = cum[cum.Length - 1]; }

        /// <summary>Null for fewer than 2 finite points or a length of 0.5 m or less.</summary>
        public static CrowdPath Prep(IReadOnlyList<Pt> pts)
        {
            if (pts == null) return null;
            var p = new List<Pt>(pts.Count);
            foreach (var q in pts) if (!double.IsNaN(q.X) && !double.IsInfinity(q.X) && !double.IsNaN(q.Z) && !double.IsInfinity(q.Z)) p.Add(q);
            if (p.Count < 2) return null;
            var cum = new double[p.Count];
            for (int i = 1; i < p.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt((p[i].X - p[i - 1].X) * (p[i].X - p[i - 1].X) + (p[i].Z - p[i - 1].Z) * (p[i].Z - p[i - 1].Z));
            return cum[cum.Length - 1] > 0.5 ? new CrowdPath(p.ToArray(), cum) : null;
        }

        public Pt First => P[0];
        public Pt Last => P[P.Length - 1];

        /// <summary>Point and unit tangent at arc length s.</summary>
        public void At(double s, out double x, out double z, out double tx, out double tz)
        {
            s = CrowdGeom.Clamp(s, 0, Len);
            int i = 1;
            while (i < P.Length - 1 && Cum[i] < s) i++;
            Pt a = P[i - 1], b = P[i];
            double L = Cum[i] - Cum[i - 1]; if (L == 0) L = 1;
            double t = (s - Cum[i - 1]) / L;
            x = a.X + (b.X - a.X) * t; z = a.Z + (b.Z - a.Z) * t; tx = (b.X - a.X) / L; tz = (b.Z - a.Z) / L;
        }
    }

    public static class CrowdGeom
    {
        public const double TAU = Math.PI * 2;

        public static double Clamp(double x, double a, double b) => Math.Min(b, Math.Max(a, x));
        public static double Hypot(double x, double z) => Math.Sqrt(x * x + z * z);

        /// <summary>crowd.js lerpAngle.</summary>
        public static double LerpAngle(double a, double b, double k) => a + ((((b - a + Math.PI) % TAU) + TAU) % TAU - Math.PI) * k;

        /// <summary>Shortest signed turn from a to b.</summary>
        public static double Turn(double a, double b) => Math.Atan2(Math.Sin(b - a), Math.Cos(b - a));

        /// <summary>Yaw (0 = facing -z) of direction (dx, dz).</summary>
        public static double YawTo(double dx, double dz) => Math.Atan2(-dx, -dz);

        /// <summary>Nearest path and arc length to (x, z) (crowd.js project). pi = -1 when there are no paths.</summary>
        public static (int pi, double s) Project(IReadOnlyList<CrowdPath> paths, double x, double z)
        {
            int best = -1; double bs = 0, bd = double.PositiveInfinity;
            for (int pi = 0; pi < paths.Count; pi++)
            {
                var P = paths[pi];
                for (int i = 1; i < P.P.Length; i++)
                {
                    Pt a = P.P[i - 1], b = P.P[i];
                    double dx = b.X - a.X, dz = b.Z - a.Z, L2 = dx * dx + dz * dz; if (L2 == 0) L2 = 1;
                    double t = Clamp(((x - a.X) * dx + (z - a.Z) * dz) / L2, 0, 1);
                    double ex = a.X + dx * t - x, ez = a.Z + dz * t - z, d = ex * ex + ez * ez;
                    if (d < bd) { bd = d; best = pi; bs = P.Cum[i - 1] + Math.Sqrt(L2) * t; }
                }
            }
            return (best, bs);
        }

        /// <summary>
        /// town-plan.js clipPaths: removes the parts of axis-aligned polylines inside the rects grown by 1 m, and
        /// drops pieces whose ends are 3 m apart or less.
        /// </summary>
        public static List<Pt[]> ClipPaths(IReadOnlyList<Pt[]> paths, IReadOnlyList<Box2> rects)
        {
            var output = new List<Pt[]>();
            List<Pt> cur = null;
            void Flush() { if (cur != null && cur.Count > 1) output.Add(cur.ToArray()); }
            var pieces = new List<(double u, double v)>();
            var next = new List<(double u, double v)>();
            foreach (var path in paths)
            {
                cur = null;
                for (int i = 1; i < path.Length; i++)
                {
                    Pt a = path[i - 1], b = path[i];
                    pieces.Clear(); pieces.Add((0, 1));
                    foreach (var r in rects)
                    {
                        var t = SegRect(a, b, r.Grow(1));
                        if (t == null) continue;
                        next.Clear();
                        foreach (var (u, v) in pieces)
                        {
                            double p0 = u, q0 = Math.Min(v, t.Value.t0), p1 = Math.Max(u, t.Value.t1), q1 = v;
                            if (q0 - p0 > 1e-3) next.Add((p0, q0));
                            if (q1 - p1 > 1e-3) next.Add((p1, q1));
                        }
                        pieces.Clear(); pieces.AddRange(next);
                    }
                    Pt At(double t) => new(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
                    if (pieces.Count == 0) { Flush(); cur = null; }
                    foreach (var (u, v) in pieces)
                    {
                        if (u > 0 || cur == null) { Flush(); cur = new List<Pt> { At(u) }; }
                        cur.Add(At(v));
                        if (v < 1) { Flush(); cur = null; }
                    }
                }
                Flush();
            }
            output.RemoveAll(p => Hypot(p[p.Length - 1].X - p[0].X, p[p.Length - 1].Z - p[0].Z) <= 3);
            return output;
        }

        /// <summary>Parametric [t0, t1] where segment a-b is inside r, or null (slab method).</summary>
        public static (double t0, double t1)? SegRect(Pt a, Pt b, Box2 r)
        {
            double t0 = 0, t1 = 1;
            for (int axis = 0; axis < 2; axis++)
            {
                double p = axis == 0 ? a.X : a.Z, d = axis == 0 ? b.X - a.X : b.Z - a.Z, lo = axis == 0 ? r.X0 : r.Z0, hi = axis == 0 ? r.X1 : r.Z1;
                if (Math.Abs(d) < 1e-9) { if (p < lo || p > hi) return null; continue; }
                double u = (lo - p) / d, v = (hi - p) / d; if (u > v) (u, v) = (v, u);
                t0 = Math.Max(t0, u); t1 = Math.Min(t1, v); if (t0 >= t1) return null;
            }
            return (t0, t1);
        }
    }
}
