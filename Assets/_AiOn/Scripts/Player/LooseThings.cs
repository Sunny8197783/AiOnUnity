using System;
using System.Collections.Generic;

namespace AiOn.Player
{
    /// <summary>
    /// The pure rules of the hands (engine/actions.js): the loose things a level is strewn with the first time it is
    /// entered (the town: 40 bricks, rocks, bottles and cans; every other level: 8 bottles, cans and empty boxes, on the
    /// flat walk zones larger than 4 m² and clear of solids, from a seeded generator), how a thrown thing bounces, who a
    /// punch reaches and who a flying thing hits. Original frame (x east, y up, z south) unless noted.
    /// </summary>
    public static class LooseThings
    {
        public sealed class Kind
        {
            public string Id, Name;
            /// <summary>Collision radius (m): the height it rests at above the floor, the sweep of its flight.</summary>
            public double R;
            /// <summary>How much of a person's 3 hit points one hit takes.</summary>
            public double Power;
            /// <summary>Base colour (sRGB hex) and roughness of the original's material.</summary>
            public int Color;
            public double Rough;
        }

        public static readonly IReadOnlyDictionary<string, Kind> Kinds = new Dictionary<string, Kind>
        {
            ["brick"] = new Kind { Id = "brick", Name = "벽돌", R = 0.06, Power = 2, Color = 0x9a4a32, Rough = 0.95 },
            ["rock"] = new Kind { Id = "rock", Name = "돌멩이", R = 0.07, Power = 2, Color = 0x77736c, Rough = 1 },
            ["bottle"] = new Kind { Id = "bottle", Name = "빈 병", R = 0.05, Power = 1, Color = 0x2f6b3a, Rough = 0.15 },
            ["can"] = new Kind { Id = "can", Name = "빈 캔", R = 0.04, Power = 0.5, Color = 0xb8c0c8, Rough = 0.35 },
            ["box"] = new Kind { Id = "box", Name = "빈 상자", R = 0.12, Power = 0.5, Color = 0xb08a5a, Rough = 0.9 },
        };

        public const double Gravity = 12, PickRadius = 1.5, PickDy = 1.4, Reach = 1.4;
        public const double ThrowSpeed = 14, ThrowLift = 2.2, PunchCooldown = 0.5, PunchLands = 0.19, SwingAt = 0.09;
        public const double PickBusy = 0.8, PickClose = 0.4, ThrowBusy = 0.6, ThrowRelease = 0.3;

        /// <summary>The G prompt for a loose thing in reach.</summary>
        public static string PickLabel(string kind) => Kinds.TryGetValue(kind ?? "", out var k) ? $"G · {k.Name} 줍기" : null;

        /// <summary>actions.js rng (mulberry32): the same seed always strews the same things in the same places.</summary>
        public sealed class Rng
        {
            uint _a;
            public Rng(uint seed) { _a = seed; }
            public double Next()
            {
                unchecked
                {
                    _a += 0x6D2B79F5;
                    uint t = (_a ^ (_a >> 15)) * (_a | 1);
                    t ^= t + (t ^ (t >> 7)) * (t | 61);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            }
        }

        public struct Placed { public string Kind; public double X, Y, Z, Yaw; }

        /// <summary>actions.js SCATTER: (count, kinds) for a level.</summary>
        public static (int n, string[] kinds) ScatterOf(string levelId) => levelId == "town"
            ? (40, new[] { "brick", "rock", "rock", "bottle", "can" })
            : (8, new[] { "bottle", "can", "box" });

        /// <summary>
        /// actions.js scatter(L): n things on the level's flat walk zones over 4 m², 0.5 m in from their edges, each
        /// clear of every solid (radius 0.3, 1 m tall); up to 6n tries. Solids that are off are ignored.
        /// </summary>
        public static List<Placed> Scatter(string levelId, IReadOnlyList<VaultPlanner.Walk> walk, List<VaultPlanner.Box> solids)
        {
            var (n, kinds) = ScatterOf(levelId);
            var r = new Rng((uint)((levelId ?? "").Length * 7919 + 13));
            var zones = new List<VaultPlanner.Walk>();
            if (walk != null) foreach (var w in walk) if (!w.Ramp && (w.X1 - w.X0) * (w.Z1 - w.Z0) > 4) zones.Add(w);
            var items = new List<Placed>();
            for (int i = 0, tries = 0; i < n && zones.Count > 0 && tries < n * 6; tries++)
            {
                var w = zones[(int)Math.Floor(r.Next() * zones.Count)];
                double x = w.X0 + 0.5 + r.Next() * (w.X1 - w.X0 - 1), z = w.Z0 + 0.5 + r.Next() * (w.Z1 - w.Z0 - 1);
                var (cx, cz) = VaultPlanner.Collide(x, z, w.Y, solids ?? new List<VaultPlanner.Box>(), 0.3, 1);
                if (Math.Sqrt((cx - x) * (cx - x) + (cz - z) * (cz - z)) > 0.01) continue;
                var kind = kinds[(int)Math.Floor(r.Next() * kinds.Length)];
                items.Add(new Placed { Kind = kind, X = x, Y = w.Y + Kinds[kind].R, Z = z, Yaw = r.Next() * 6.28 });
                i++;
            }
            return items;
        }

        public enum Bounce { None, Thud, Glass }

        /// <summary>
        /// A flying thing meets a surface with unit normal (nx, ny, nz) (any frame with y up). A floor (normal mostly up)
        /// takes 70 % of the fall and 45 % of the slide (it sounds when it came down faster than 2 m/s; a bottle breaks
        /// the silence with glass) and it comes to rest under 0.5 m/s; anything else sends the part along the normal
        /// back at 35 % and keeps 70 % of it all (a thud above 2 m/s).
        /// </summary>
        public static Bounce Hit(ref double vx, ref double vy, ref double vz, double nx, double ny, double nz, string kind, out bool rest)
        {
            rest = false;
            if (ny > 0.6)
            {
                var sound = Math.Abs(vy) > 2 ? (kind == "bottle" ? Bounce.Glass : Bounce.Thud) : Bounce.None;
                vy *= -0.3; vx *= 0.55; vz *= 0.55;
                if (Math.Sqrt(vx * vx + vy * vy + vz * vz) < 0.5) { rest = true; vx = vy = vz = 0; }
                return sound;
            }
            double dn = vx * nx + vy * ny + vz * nz;
            if (dn < 0) { vx -= 1.35 * dn * nx; vy -= 1.35 * dn * ny; vz -= 1.35 * dn * nz; }
            vx *= 0.7; vy *= 0.7; vz *= 0.7;
            return Math.Sqrt(vx * vx + vy * vy + vz * vz) > 2 ? Bounce.Thud : Bounce.None;
        }

        /// <summary>
        /// Does the path flown this frame (from -> to) pass through a person standing at p (feet): within 0.45 m of the
        /// line on the ground, between the feet and 1.8 m up.
        /// </summary>
        public static bool Strikes(double fx, double fy, double fz, double tx, double ty, double tz, double px, double py, double pz)
        {
            double sx = tx - fx, sz = tz - fz, s2 = sx * sx + sz * sz;
            double u = s2 > 1e-9 ? Math.Min(1, Math.Max(0, ((px - fx) * sx + (pz - fz) * sz) / s2)) : 1;
            double hx = fx + sx * u, hz = fz + sz * u, hy = fy + (ty - fy) * u;
            return Math.Sqrt((px - hx) * (px - hx) + (pz - hz) * (pz - hz)) < 0.45 && hy > py && hy < py + 1.8;
        }
    }
}
