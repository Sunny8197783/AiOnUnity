using System;
using System.Collections.Generic;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// The shelter people's data (original levels/shelter-people.js and shelter-camp.js), original frame.
    /// The seats are the camp's own (shelter-camp.js 47-60, rng 1987 after the 540 draws of the foil canvas): 53 seats
    /// on 36 used mats, embedded as data rather than re-running the camp's generator.
    /// </summary>
    public static class ShelterPeoplePlan
    {
        public readonly struct Seat
        {
            public readonly double X, Z, Yaw;
            public Seat(double x, double z, double yaw) { X = x; Z = z; Yaw = yaw; }
        }

        static Seat S(double x, double z, double yaw) => new(x, z, yaw);

        public static readonly Seat[] Seats =
        {
            S(-20.426, -3.606, 0.595), S(-20.527, -1.929, 3.077), S(-20.371, 1.814, -0.253), S(-18.289, -3.69, -0.519), S(-18.166, -1.823, 3.918),
            S(-18.62, 1.673, 0.789), S(-17.78, 1.67, -0.493), S(-18.62, 3.539, 2.639), S(-17.78, 3.614, 2.343), S(-16.42, 1.931, 0.113),
            S(-15.58, 1.888, -0.024), S(-16.015, 3.536, 2.45), S(-13.791, 1.765, 0.555), S(-11.553, -3.683, 0.737), S(-11.615, -1.803, 2.82),
            S(-9.376, -1.828, 2.894), S(-9.82, 3.539, 3.057), S(-8.98, 3.55, 2.717), S(-7.62, 1.715, 0.726), S(-6.78, 1.931, 0.793),
            S(-5.42, -3.523, 0.12), S(-4.58, -3.667, 0.356), S(-5.42, -1.814, 2.689), S(-4.58, -1.711, 2.482), S(-5.42, 1.827, -0.49),
            S(-4.58, 1.922, -0.033), S(-5.42, 3.549, 2.731), S(-4.58, 3.749, 3.339), S(-0.523, -3.691, 0.173), S(-0.539, -1.855, 3.234),
            S(-0.514, 1.906, 0.334), S(1.18, -3.524, 0.456), S(2.02, -3.662, 0.575), S(1.682, -1.707, 2.673), S(1.743, 1.864, 0.489),
            S(3.38, -3.608, 0.139), S(4.22, -3.502, 0.748), S(3.38, -1.719, 3.14), S(4.22, -1.801, 2.419), S(3.38, 3.593, 3.357),
            S(4.22, 3.642, 2.926), S(6.13, -3.735, 0.03), S(6.098, -1.82, 3.23), S(-16.099, 14.166, 3.037), S(-13.906, 12.031, 3.39),
            S(-14.42, 14.154, 3.361), S(-13.58, 14.306, 3.391), S(-12.22, 12.002, 3.397), S(-11.38, 12.024, 2.927), S(-12.22, 14.232, 2.786),
            S(-11.38, 14.382, 2.945), S(-10.02, 14.241, 2.66), S(-9.18, 14.163, 3.403),
        };

        public const double AN = -6.9, AS = 9.0; // north / south driving aisles (z)

        /// <summary>Entrance paths: they start out of sight (stair flight, ramp slopes, lobby and toilet doors) and end on an aisle.</summary>
        public static readonly IReadOnlyDictionary<string, Pt[]> Ways = new Dictionary<string, Pt[]>
        {
            ["stairs"] = new[] { new Pt(5.7, -18.0), new Pt(5.7, -20.6), new Pt(4.3, -20.6), new Pt(4.3, -17.3), new Pt(5, -16.0), new Pt(5, -10.4), new Pt(3.5, -9.2), new Pt(3.5, AN) },
            ["access"] = new[] { new Pt(-13, -23), new Pt(-13, -15.6), new Pt(-13, -10.0), new Pt(-12.4, AN) },
            ["east"] = new[] { new Pt(24.6, -3.4), new Pt(22.8, -0.5), new Pt(20.3, -1.2), new Pt(20.3, AN) },
            ["toilet"] = new[] { new Pt(24.6, 3.4), new Pt(22.8, 0.5), new Pt(20.3, -1.2), new Pt(20.3, AN) },
            ["ramp"] = new[] { new Pt(11, 23), new Pt(11, 15.4), new Pt(11, 10.6), new Pt(10.2, AS) },
        };

        public static readonly string[] In = { "stairs", "stairs", "stairs", "east", "ramp", "access" };
        public static readonly string[] Out = { "stairs", "stairs", "east", "access" };
        public static readonly string[] Entrances = { "stairs", "ramp", "access", "east" };
        /// <summary>Gaps between mats clear of pillars, radio table, bins.</summary>
        public static readonly double[] Cols = { -19.3, -17.1, -14.9, -12.7, -10.5, -3.9, -1.7, 2.7, 4.9 };
        public const double FireClear = 6.5;
        public static readonly Pt Fire = new(-18.6, -9.2);
        public const double Gap = 0.8, PassNear = 1.8, PassSide = 0.34, PassRate = 1.2;

        public static readonly Pt Reception = new(2.0, -8.6), Radio = new(-5.5, 7.4);

        /// <summary>A waypoint; the desk stop (arrivals by stair A write in the roster) waits there, talking, facing the desk.</summary>
        public struct Wp
        {
            public double X, Z;
            public double Wait;
            public string Anim;
            public double? Face;
            public Wp(double x, double z) { X = x; Z = z; Wait = 0; Anim = null; Face = null; }
        }

        public static readonly Wp Desk = new(2.55, -9.5) { Wait = 2.6, Anim = "talk", Face = Math.PI };

        /// <summary>Entrance -&gt; aisle -&gt; gap column -&gt; seat row -&gt; seat. Reverse it to leave.</summary>
        public static List<Wp> SeatPath(string way, Seat s, bool desk = false)
        {
            var pts = new List<Wp>();
            foreach (var p in Ways[way]) pts.Add(new Wp(p.X, p.Z));
            if (desk && way == "stairs") pts.Insert(6, Desk);
            double az = pts[pts.Count - 1].Z, gx = Cols[0];
            foreach (var c in Cols) if (Math.Abs(c - s.X) < Math.Abs(gx - s.X)) gx = c;
            pts.Add(new Wp(gx, az)); pts.Add(new Wp(gx, s.Z)); pts.Add(new Wp(s.X, s.Z));
            return pts;
        }
    }
}
