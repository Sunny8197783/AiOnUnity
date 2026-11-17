using System;
using System.Collections.Generic;

namespace AiOn.Player
{
    /// <summary>
    /// Vault / climb planning for Space in front of a low solid (engine/vault.js planVault), pure and in the original
    /// frame (x east, y up, z south). Null = nothing to get over here: the caller jumps instead. The rules, so a wall is
    /// never a way through:
    ///  - the first solid in front at knee height decides; a solid taller than 2.3 m (a wall) stops it all;
    ///  - thickness is the box's size across the move axis (the face you meet), and the move goes straight across it;
    ///  - over: up to 1.5 m tall and 1.0 m thick (low walls, railings, cars, low fences), landing on a walk zone at about
    ///    the same level, with a free body there, a clear line to the landing at the height the body passes, and
    ///    headroom over the arc;
    ///  - onto: a top at least 0.4 m deep, and 0.6 m if it is more than 1.2 m up (no standing on a structural wall), with
    ///    1.8 m of headroom over the landing (ceilings; indoors without ceiling data the room is 2.3 m high) and a clear
    ///    line to it;
    ///  - solids marked noVault / noClimb / npc are never used for that.
    /// The geometry helpers are engine/geom.js (groundAt, groundNear, collide, segBox) and space.js headroomAt.
    /// </summary>
    public static class VaultPlanner
    {
        public const double Over = 1.5, OverThick = 1.0, Onto = 2.3, Thin = 0.4, Wall = 0.6, WallH = 1.2, Head = 1.8, Drop = 1.2, Reach = 1.0;
        public const double RoomH = 2.3;

        public sealed class Box
        {
            public double X0, X1, Y0, Y1, Z0, Z1;
            public bool NoVault, NoClimb, Npc;
            public Box() { }
            public Box(double x0, double x1, double y0, double y1, double z0, double z1) { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z0 = z0; Z1 = z1; }
        }

        public struct Walk
        {
            public double X0, X1, Z0, Z1, Y;
            public bool Ramp, AxisX;
            public double Y0, Y1;
            public static Walk Flat(double x0, double x1, double z0, double z1, double y) => new Walk { X0 = x0, X1 = x1, Z0 = z0, Z1 = z1, Y = y };
        }

        public struct Ceiling { public double X0, X1, Z0, Z1, Y; }

        /// <summary>What the planner reads of a level: its solids (switched-off ones left out), walk zones and ceilings.</summary>
        public sealed class World
        {
            public readonly List<Box> Solids = new();
            public readonly List<Walk> Walk = new();
            public readonly List<Ceiling> Ceilings = new();
            /// <summary>space.js indoor(L): every level but the town. Without ceiling data the room is 2.3 m high.</summary>
            public bool Indoor;
        }

        public struct Plan
        {
            /// <summary>"over" (to the far side) or "onto" (up on its top).</summary>
            public string Kind;
            public double X1, Z1, Y1, H, Arc;
            public Box Solid;
            public bool IsOver => Kind == "over";
            /// <summary>controls.js: over 0.5 + h * 0.15 s, onto 0.35 + h * 0.3 s.</summary>
            public double Duration => IsOver ? 0.5 + H * 0.15 : 0.35 + H * 0.3;
            /// <summary>The body clip: climb for a top more than 1 m up, else vault.</summary>
            public string Clip => !IsOver && H > 1 ? "climb" : "vault";
            /// <summary>Stamina it costs: over 0.08, onto 0.05 + h * 0.05.</summary>
            public double Stamina => IsOver ? 0.08 : 0.05 + H * 0.05;
        }

        // ---------------------------------------------------------------- from a LevelLayout

        public static Walk WalkOf(AiOn.Levels.LevelLayout.WalkZone w) => new Walk
        {
            X0 = w.x0, X1 = w.x1, Z0 = w.z0, Z1 = w.z1, Y = w.y, Ramp = w.isRamp, AxisX = w.rampAxis == "x", Y0 = w.rampY0, Y1 = w.rampY1,
        };

        public static Box BoxOf(AiOn.Levels.LevelLayout.SolidBox s) =>
            new Box(s.x0, s.x1, s.y0, s.y1, s.z0, s.z1) { NoVault = s.noVault, NoClimb = s.noClimb };

        /// <summary>A level's walk zones and ceilings (solids are added by the caller: only the ones on now).</summary>
        public static World WorldOf(AiOn.Levels.LevelLayout layout)
        {
            var w = new World { Indoor = layout == null || layout.levelId != "town" };
            if (layout == null) return w;
            foreach (var z in layout.walk) if (z != null) w.Walk.Add(WalkOf(z));
            foreach (var c in layout.ceilings) if (c != null) w.Ceilings.Add(new Ceiling { X0 = c.x0, X1 = c.x1, Z0 = c.z0, Z1 = c.z1, Y = c.y });
            return w;
        }

        // ---------------------------------------------------------------- geom.js

        static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        /// <summary>groundAt: the highest walk surface under (x, z) within `below` m below / 0.45 m above feetY, or null.</summary>
        public static double? GroundAt(List<Walk> walk, double x, double z, double feetY, bool any = false, double below = 0.6)
        {
            double? best = null;
            if (walk == null) return null;
            for (int i = 0; i < walk.Count; i++)
            {
                var w = walk[i];
                if (x < w.X0 - 0.02 || x > w.X1 + 0.02 || z < w.Z0 - 0.02 || z > w.Z1 + 0.02) continue;
                double y = w.Y;
                if (w.Ramp)
                {
                    double span = w.AxisX ? w.X1 - w.X0 : w.Z1 - w.Z0;
                    double k = Clamp((w.AxisX ? x - w.X0 : z - w.Z0) / (span == 0 ? 1 : span), 0, 1);
                    y = w.Y0 + (w.Y1 - w.Y0) * k;
                }
                if (!any && (y > feetY + 0.45 || y < feetY - below)) continue;
                if (best == null || y > best) best = y;
            }
            return best;
        }

        static readonly double[] NearX = { 0, 0.25, -0.25, 0, 0 }, NearZ = { 0, 0, 0, 0.25, -0.25 };

        /// <summary>groundNear: ground with a little footprint (a seam narrower than the body is not a hole).</summary>
        public static double? GroundNear(List<Walk> walk, double x, double z, double feetY)
        {
            for (int i = 0; i < NearX.Length; i++) { var g = GroundAt(walk, x + NearX[i], z + NearZ[i], feetY); if (g != null) return g; }
            return null;
        }

        /// <summary>collide: pushes a circle (radius r) out of every box overlapping [feetY, feetY + h].</summary>
        public static (double x, double z) Collide(double px, double pz, double feetY, List<Box> solids, double r = 0.32, double h = 1.72)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                foreach (var s in solids)
                {
                    if (s.Y1 <= feetY + 0.05 || s.Y0 >= feetY + h) continue;
                    double cx = Clamp(px, s.X0, s.X1), cz = Clamp(pz, s.Z0, s.Z1), dx = px - cx, dz = pz - cz, d2 = dx * dx + dz * dz;
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

        /// <summary>segBox: segment o -> o + d (t in 0..1) against the box grown by pad; the entry t, or +inf (also from inside).</summary>
        public static double SegBox(double ox, double oy, double oz, double dx, double dy, double dz, Box s, double pad)
        {
            double x0 = s.X0 - pad, x1 = s.X1 + pad, y0 = s.Y0 - pad, y1 = s.Y1 + pad, z0 = s.Z0 - pad, z1 = s.Z1 + pad;
            if (ox > x0 && ox < x1 && oy > y0 && oy < y1 && oz > z0 && oz < z1) return double.PositiveInfinity;
            double t0 = 0, t1 = 1;
            if (!Slab(ox, dx, x0, x1, ref t0, ref t1) || !Slab(oy, dy, y0, y1, ref t0, ref t1) || !Slab(oz, dz, z0, z1, ref t0, ref t1)) return double.PositiveInfinity;
            return t0;
        }

        static bool Slab(double o, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-9) return !(o < lo || o > hi);
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { var k = a; a = b; b = k; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }

        /// <summary>space.js headroomAt: the lowest ceiling over (x, z) above feet + 0.5, else 2.3 m indoors, else +inf.</summary>
        public static double Headroom(World w, double x, double z, double feetY)
        {
            double c = double.PositiveInfinity, above = feetY + 0.5;
            foreach (var k in w.Ceilings)
                if (x >= k.X0 && x <= k.X1 && z >= k.Z0 && z <= k.Z1 && k.Y > above && k.Y < c) c = k.Y;
            if (!double.IsPositiveInfinity(c)) return c;
            return w.Indoor ? feetY + RoomH : double.PositiveInfinity;
        }

        // ---------------------------------------------------------------- vault.js

        static readonly List<Box> NearList = new();

        static Box FirstHit(double x, double z, double feet, double dx, double dz, List<Box> list, double r)
        {
            Box best = null; double bt = double.PositiveInfinity;
            foreach (var s in list)
            {
                if (s.Npc || s.Y1 <= feet + 0.3 || s.Y0 > feet + 0.3) continue;
                double t = SegBox(x, feet + 0.25, z, dx * Reach, 0, dz * Reach, s, r * 0.6);
                if (t < bt) { bt = t; best = s; }
            }
            return best;
        }

        static bool Clear(List<Box> list, Box skip, double ax, double ay, double az, double bx, double by, double bz)
        {
            foreach (var s in list)
                if (s != skip && !s.Npc && SegBox(ax, ay, az, bx - ax, by - ay, bz - az, s, 0.05) <= 1) return false;
            return true;
        }

        /// <summary>planVault for a body at (x, feet, z) heading (dx, dz) (a unit vector), radius r and height h.</summary>
        public static Plan? PlanVault(World world, double x, double z, double feet, double dx, double dz, double r = 0.32, double h = 1.72)
        {
            if (world == null) return null;
            var list = NearList; list.Clear();
            foreach (var s in world.Solids)
                if (s.X0 <= x + 3 && s.X1 >= x - 3 && s.Z0 <= z + 3 && s.Z1 >= z - 3) list.Add(s);
            var best = FirstHit(x, z, feet, dx, dz, list, r);
            if (best == null) return null;
            double ht = best.Y1 - feet;
            if (ht > Onto) return null; // a wall: just a jump
            // The face met: the axis the player is outside the box on (the larger gap at a corner), crossed straight.
            double gx = x < best.X0 ? best.X0 - x : x > best.X1 ? x - best.X1 : -1, gz = z < best.Z0 ? best.Z0 - z : z > best.Z1 ? z - best.Z1 : -1;
            if (gx < 0 && gz < 0) return null; // already over it
            bool ax = gx >= gz;
            int sgn = ax ? (x < best.X0 ? 1 : -1) : (z < best.Z0 ? 1 : -1);
            double lo = ax ? best.X0 : best.Z0, hi = ax ? best.X1 : best.Z1, thick = hi - lo;
            double width = ax ? best.Z1 - best.Z0 : best.X1 - best.X0;
            double near0 = sgn > 0 ? lo : hi, far = sgn > 0 ? hi : lo;
            // Along the face the move keeps the player's line, kept on the box so the body really goes across it.
            double cross = ax ? z : x, cLo = ax ? best.Z0 : best.X0, cHi = ax ? best.Z1 : best.X1;
            double along = Math.Min(Math.Max(cross, cLo + Math.Min(0.2, width / 2)), cHi - Math.Min(0.2, width / 2));
            (double, double) At(double u) => ax ? (u, along) : (along, u);
            bool Free(double px, double pz, double py) { var (cx, cz) = Collide(px, pz, py, list, r, Head); return Math.Sqrt((cx - px) * (cx - px) + (cz - pz) * (cz - pz)) < 0.05; }
            double chest = feet + 1.4;

            if (ht <= Over && thick <= OverThick && !best.NoVault) // over it
            {
                var (lx, lz) = At(far + sgn * (r + 0.18));
                var gl = GroundAt(world.Walk, lx, lz, feet, false, Drop) ?? GroundNear(world.Walk, lx, lz, feet);
                double body = Math.Max(feet + 0.45, best.Y1 + 0.25);
                if (gl != null && Math.Abs(gl.Value - feet) <= Drop && Free(lx, lz, gl.Value)
                    && Headroom(world, (x + lx) / 2, (z + lz) / 2, feet) - best.Y1 >= 1.0 && Headroom(world, lx, lz, gl.Value) - gl.Value >= Head
                    && Clear(list, best, x, body, z, lx, Math.Max(gl.Value + 0.45, best.Y1 + 0.25), lz)
                    && Clear(list, best, x, Math.Max(chest, best.Y1 + 0.6), z, lx, gl.Value + 1.4, lz))
                {
                    double y1 = Math.Max(gl.Value, feet - 0.4);
                    return new Plan { Kind = "over", X1 = lx, Z1 = lz, Y1 = y1, H = ht, Arc = best.Y1 + 0.2 - (feet + y1) / 2, Solid = best };
                }
            }
            double minW = Math.Min(thick, width);
            if (best.NoClimb || minW < Thin || (ht > WallH && minW < Wall)) return null; // onto it
            var (ox, oz) = At(near0 + sgn * Math.Min(0.45, thick / 2)); // at least 0.2 m in from both faces
            if (!Free(ox, oz, best.Y1) || Headroom(world, ox, oz, feet) - best.Y1 < Head) return null; // the room's ceiling
            if (!Clear(list, best, x, Math.Max(chest, best.Y1 + 0.3), z, ox, best.Y1 + 1.0, oz)) return null;
            return new Plan { Kind = "onto", X1 = ox, Z1 = oz, Y1 = best.Y1, H = ht, Arc = 0.2, Solid = best };
        }

        /// <summary>
        /// Where the body is k (0..1) of the way through the move (controls.js: smoothstep along the line, plus a
        /// sin(pi k) arc over the top). Original frame.
        /// </summary>
        public static (double x, double y, double z) MoveAt(double x0, double y0, double z0, in Plan p, double k)
        {
            k = Clamp(k, 0, 1);
            double e = k * k * (3 - 2 * k);
            return (x0 + (p.X1 - x0) * e, y0 + (p.Y1 - y0) * e + Math.Sin(Math.PI * k) * p.Arc, z0 + (p.Z1 - z0) * e);
        }
    }
}
