using System;
using System.Collections.Generic;

namespace AiOn.Story
{
    /// <summary>
    /// Pure: how the two assembly buses (town-events.js BUS, original frame) leave the bay (port addition). Each pulls
    /// forward, turns east through the bay's open side onto 문화로, then north up its far lane (the 'NS' lane at x -48,
    /// heading -z) and out of town. The second bus (x -65, nearer the road) goes first, the other follows. Positions are
    /// the bus's centre on the ground; yaw is the original's (0 faces -z, the way the parked buses face).
    /// </summary>
    public static class BusRoute
    {
        /// <summary>The buses' parked centres (town-events.js BUS).</summary>
        public static readonly (double x, double z)[] Parked = { (-69.5, -40.5), (-65.0, -40.5) };

        /// <summary>Seconds after the departure starts that each bus sets off (the one nearer the road first).</summary>
        public static readonly double[] Delay = { 2.4, 0 };

        public const double Accel = 1.6, Cruise = 6.5; // m/s², m/s: a heavy bus, a calm pull-out
        public const double LaneX = -48, TurnZ = -52, RoadEdgeX = -54;

        static readonly List<(double x, double z, double s)>[] Paths = { Build(Parked[0].x), Build(Parked[1].x) };

        static List<(double x, double z, double s)> Build(double bx)
        {
            var pts = new List<(double x, double z)>();
            void Line(double x0, double z0, double x1, double z1, int n) { for (int i = pts.Count == 0 ? 0 : 1; i <= n; i++) { double k = (double)i / n; pts.Add((x0 + (x1 - x0) * k, z0 + (z1 - z0) * k)); } }
            void Quad(double x0, double z0, double cx, double cz, double x1, double z1, int n)
            {
                for (int i = 1; i <= n; i++)
                {
                    double k = (double)i / n, a = (1 - k) * (1 - k), b = 2 * (1 - k) * k, c = k * k;
                    pts.Add((a * x0 + b * cx + c * x1, a * z0 + b * cz + c * z1));
                }
            }
            double z0 = Parked[0].z;
            Line(bx, z0, bx, -45, 6);                         // forward out of the bay
            Quad(bx, -45, bx, TurnZ, bx + 7, TurnZ, 16);       // right, toward the open east side
            Line(bx + 7, TurnZ, -55, TurnZ, 6);               // across the sidewalk
            Quad(-55, TurnZ, LaneX, TurnZ, LaneX, TurnZ - 7, 16); // left, up 문화로
            Line(LaneX, TurnZ - 7, LaneX, -130, 20);          // north, out of town
            var r = new List<(double x, double z, double s)>(pts.Count);
            double s = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) s += Math.Sqrt((pts[i].x - pts[i - 1].x) * (pts[i].x - pts[i - 1].x) + (pts[i].z - pts[i - 1].z) * (pts[i].z - pts[i - 1].z));
                r.Add((pts[i].x, pts[i].z, s));
            }
            return r;
        }

        /// <summary>Metres travelled t seconds after setting off: a slow start up to a calm cruise.</summary>
        public static double Distance(double t)
        {
            if (t <= 0) return 0;
            double ta = Cruise / Accel;
            return t <= ta ? 0.5 * Accel * t * t : 0.5 * Accel * ta * ta + Cruise * (t - ta);
        }

        public static double Length(int bus) => Paths[bus][Paths[bus].Count - 1].s;

        /// <summary>Bus <paramref name="bus"/> (0 = west, 1 = east) t seconds after the departure began: centre, ground drop, yaw.</summary>
        public static (double x, double z, double drop, double yaw) At(int bus, double t)
        {
            var p = Paths[bus];
            double d = Math.Min(Distance(t - Delay[bus]), p[p.Count - 1].s);
            int i = 1;
            while (i < p.Count - 1 && p[i].s < d) i++;
            var a = p[i - 1]; var b = p[i];
            double seg = b.s - a.s, k = seg > 1e-9 ? Math.Max(0, Math.Min(1, (d - a.s) / seg)) : 0;
            double x = a.x + (b.x - a.x) * k, z = a.z + (b.z - a.z) * k;
            double yaw = Math.Atan2(-(b.x - a.x), -(b.z - a.z)); // original yaw: 0 faces -z
            // off the bay's curb (0.12) onto the road (0) as it crosses the sidewalk's edge
            double e = Math.Max(0, Math.Min(1, (x - (RoadEdgeX - 1)) / 2));
            return (x, z, 0.12 * e * e * (3 - 2 * e), yaw);
        }

        /// <summary>True once both buses are past the end of the bay's wall and well up 문화로 (out of the assembly's view).</summary>
        public static bool Gone(double t) => At(0, t).z < -95 && At(1, t).z < -95;
    }
}
