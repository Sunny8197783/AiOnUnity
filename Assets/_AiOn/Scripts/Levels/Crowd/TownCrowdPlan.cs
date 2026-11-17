using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// The town crowd's data, verbatim from the original levels/town-plan.js (SIDEWALKS, ENTRY, EVAC, BARRICADES, CYCLE),
    /// levels/town-events.js (WATER, ASSEMBLY) and levels/town-crowd.js (SIZES, CROWD_MODES, signal holds). Original frame.
    /// </summary>
    public static class TownCrowdPlan
    {
        public static readonly IReadOnlyDictionary<string, int> Sizes = new Dictionary<string, int> { ["main"] = 26, ["extra"] = 14, ["few"] = 8, ["line"] = 14 };
        public static readonly string[] Modes = { "none", "normal", "busy", "evacuating", "sparse", "queue", "assembly" };

        static Pt[] P(params double[] xz)
        {
            var p = new Pt[xz.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Pt(xz[2 * i], xz[2 * i + 1]);
            return p;
        }

        static Pt[] Cat(params Pt[][] parts) => parts.SelectMany(x => x).ToArray();

        /// <summary>Sidewalk centre lines (2 m from the curb) the crowd strolls on. Closures cut them.</summary>
        public static readonly Pt[][] Sidewalks =
        {
            P(-128, -8, -56, -8), P(-44, -8, 44, -8), P(56, -8, 128, -8),
            P(-128, 8, -56, 8), P(-44, 8, 44, 8), P(56, 8, 128, 8),
            P(-56, -108, -56, -8), P(-56, 8, -56, 108), P(-44, -54, -44, -8), P(-44, 8, -44, 54),
            P(44, -54, 44, -8), P(44, 8, 44, 54), P(56, -108, 56, -8), P(56, 8, 56, 108),
            P(-44, -54, 44, -54), P(-44, -66, 44, -66), P(-44, 54, 44, 54), P(-44, 66, 44, 66),
            // crossings (zebra) and inner walks
            P(0, -8, 0, 8), P(-104, -8, -104, 8), P(-56, -14, -44, -14), P(-56, 14, -44, 14),
            P(44, -14, 56, -14), P(44, 14, 56, 14), P(-60, -8, -60, 8), P(60, -8, 60, 8),
            P(0, 8, 0, 11, -15, 22, -15, 40, -30, 44, -38, 30.5, -44, 30.5),
            P(56, 20, 62, 20, 67.6, 25.6, 100, 25.6), P(-66, -11, -80, -14, -96, -18), // 103동 walk clear of the canopy posts
        };

        /// <summary>Where evacuees head for each shelter gate (just outside the entrance).</summary>
        public static readonly IReadOnlyDictionary<string, Pt> Entry = new Dictionary<string, Pt>
        {
            ["stairs"] = new(-86, 11.8), ["ramp"] = new(-80, 55.4), ["access"] = new(-102.4, 12.7), ["east"] = new(-66.4, 31),
        };

        static readonly Pt[] TO_A = P(44, 8, -44, 8, -44, 14, -56, 14, -56, 10, -75, 10, -86, 11.8);
        static readonly Pt[] TO_A1 = TO_A.Skip(1).ToArray();

        /// <summary>Evacuation paths by gate; they end at the gate's entry.</summary>
        public static readonly (string gate, Pt[] path)[] Evac =
        {
            ("stairs", TO_A),
            ("stairs", Cat(P(128, 8, 56, 8), TO_A)),
            ("stairs", Cat(P(128, -8, 60, -8, 60, 8, 56, 8), TO_A)), ("stairs", Cat(P(56, 104, 56, 8), TO_A)),
            ("stairs", P(56, -104, 56, -14, 44, -14, 44, -8, -44, -8, -44, -14, -56, -14, -56, -8, -60, -8, -60, 9.6, -86, 11.8)),
            ("stairs", Cat(P(-38, 30.5, -30, 44, -15, 40, -15, 22, 0, 14, 0, 8), TO_A1)),
            ("east", P(-15, 22, -15, 40, -30, 44, -38, 30.5, -44, 30.5, -44, 31, -56, 31, -66.4, 31)),
            ("stairs", P(-56, -100, -56, -8, -60, -8, -60, 9.6, -86, 11.8)), ("stairs", P(44, -54, -44, -54, -56, -54, -56, -8, -60, -8, -60, 9.6, -86, 11.8)),
            ("stairs", P(-128, -8, -104, -8, -104, 9.6, -86, 11.8)),
            ("access", P(-128, -8, -104, -8, -104, 9.6, -102.4, 12.7)),
            ("access", P(-118, -14, -104, -8, -104, 9.6, -102.4, 12.7)),
            ("ramp", P(44, 54, -42, 54, -44, 55.2, -56, 55.2, -62, 55.6, -80, 55.4)), // crosses 문화로 clear of the light poles at z 54
            // the z ±66 sidewalks cross 단지길 / 시장길 on the zebra (x 0), not between the parked cars
            ("ramp", P(44, 66, 1.2, 66, 1.2, 54, -42, 54, -44, 55.2, -56, 55.2, -62, 55.6, -80, 55.4)),
            ("ramp", P(-44, 66, -1.2, 66, -1.2, 54, -42, 54, -44, 55.2, -56, 55.2, -62, 55.6, -80, 55.4)),
            ("stairs", P(44, -66, 1.2, -66, 1.2, -54, -44, -54, -56, -54, -56, -8, -60, -8, -60, 9.6, -86, 11.8)),
            ("stairs", P(-44, -66, -1.2, -66, -1.2, -54, -44, -54, -56, -54, -56, -8, -60, -8, -60, 9.6, -86, 11.8)),
            ("ramp", P(-56, 104, -56, 57, -62, 55.6, -80, 55.4)),
            ("east", P(-56, 104, -56, 31, -66.4, 31)),
            ("east", P(-44, 54, -44, 31, -56, 31, -66.4, 31)),
            ("stairs", Cat(P(100, 25.6, 67.6, 25.6, 62, 20, 56, 20, 56, 8), TO_A)),
        };

        /// <summary>
        /// Barricade rects by closure id. yeonmuCenter is widened to the fence lines, as in town-plan.js (TownMap.Closures
        /// keeps the narrower ones the map draws).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Box2[]> Barricades = new Dictionary<string, Box2[]>
        {
            ["yeonmuCenter"] = new[] { new Box2(27, 29, -10, 12), new Box2(-46, -42, -10, 12) },
            ["danji"] = new[] { new Box2(-1, 1, 52, 68) },
            ["sijang"] = new[] { new Box2(-1, 1, -68, -52) },
            ["munhwaSouth"] = new[] { new Box2(-58, -42, 44, 46) },
            ["munhwaNorth"] = new[] { new Box2(-58, -42, -20, -18) },
        };

        public static readonly IReadOnlyDictionary<string, string> DefaultGates = new Dictionary<string, string>
        {
            ["east"] = "open", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "closed",
        };

        // town-events.js
        public static readonly Pt WaterQueue = new(-78.6, -18.4);
        public static readonly Pt AssemblyQueue = new(-62.6, -44.6);
        public static readonly Box2 AssemblyArea = new(-61.8, -58.6, -49, -40);

        /// <summary>Signal cycle (s): EW green, EW amber, all red, NS green, NS amber, all red.</summary>
        public static readonly (string g, string c, double d)[] Cycle =
        {
            ("EW", "green", 20), ("EW", "amber", 3), ("ALL", "red", 1.5), ("NS", "green", 13), ("NS", "amber", 3), ("ALL", "red", 1.5),
        };
        static readonly double CycleS = Cycle.Sum(c => c.d);

        /// <summary>Seconds of steady green left for group EW | NS at time t.</summary>
        public static double GreenLeft(double t, string grp)
        {
            double u = ((t % CycleS) + CycleS) % CycleS;
            foreach (var (g, c, d) in Cycle) { if (u < d) return g == grp && c == "green" ? d - u : 0; u -= d; }
            return 0;
        }

        static readonly string[] Signalled = { "yeonmu", "munhwa", "hanbit" };

        /// <summary>
        /// The carriageway of every zebra with pedestrian signals (town-geo zebraZones on the three signalled roads):
        /// crossing an east-west road waits for the NS green, and the other way round.
        /// </summary>
        public static List<CrowdGroup.Hold> SignalHolds()
        {
            var holds = new List<CrowdGroup.Hold>();
            foreach (var c in TownMap.Crosswalks)
            {
                var r = TownMap.Road(c.Road);
                if (r == null || Array.IndexOf(Signalled, r.Id) < 0) continue;
                bool ew = r.Rect.Depth < r.Rect.Width;
                var zone = ew ? new Box2(c.At - 2.6, c.At + 2.6, r.Rect.Z0 - 0.6, r.Rect.Z1 + 0.6) : new Box2(r.Rect.X0 - 0.6, r.Rect.X1 + 0.6, c.At - 2.6, c.At + 2.6);
                holds.Add(new CrowdGroup.Hold { Zone = zone, Group = ew ? "NS" : "EW", Open = true });
            }
            return holds;
        }

        /// <summary>Opens a hold while the walk signal is steady green (more than 5 s left) or the signals are dark.</summary>
        public static void SetSignals(List<CrowdGroup.Hold> holds, double t, bool on)
        {
            foreach (var h in holds) h.Open = !on || GreenLeft(t, h.Group) > 5;
        }

        public static List<Box2> Rects(IEnumerable<string> closures)
        {
            var l = new List<Box2>();
            if (closures != null) foreach (var id in closures) if (id != null && Barricades.TryGetValue(id, out var b)) l.AddRange(b);
            return l;
        }

        public static List<Pt[]> StrollFor(IEnumerable<string> closures) => CrowdGeom.ClipPaths(Sidewalks, Rects(closures));

        /// <summary>Evacuation paths through the open gates (else the stairs ones), cut by closures, and the gates' entries.</summary>
        public static (List<Pt[]> paths, List<Pt> targets) EvacFor(IReadOnlyDictionary<string, string> gates, IEnumerable<string> closures)
        {
            var open = Evac.Where(e => gates != null && gates.TryGetValue(e.gate, out var v) && v == "open").ToList();
            var use = open.Count > 0 ? open : Evac.Where(e => e.gate == "stairs").ToList();
            var paths = CrowdGeom.ClipPaths(use.Select(e => e.path).ToList(), Rects(closures));
            var targets = use.Select(e => e.gate).Distinct().Select(g => Entry[g]).ToList();
            return (paths, targets);
        }
    }
}
