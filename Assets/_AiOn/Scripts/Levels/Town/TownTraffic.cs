using System;
using System.Collections.Generic;
using AiOn.Game;
using AiOn.Levels.Crowd;

namespace AiOn.Levels.Town
{
    /// <summary>A traffic lane (levels/town-plan.js LANES): Korea drives on the right; u runs in the travel direction.</summary>
    public sealed class TrafficLane
    {
        public readonly bool AxisX;
        public readonly int Dir;
        public readonly double C, Vmax;
        /// <summary>Which signal phase lets the lane go: EW | NS.</summary>
        public readonly string Group;
        /// <summary>World coordinates along the axis where cars wait at red.</summary>
        public readonly double[] Stops;
        /// <summary>The stops as lane coordinates u (stop * dir), ascending.</summary>
        public readonly double[] StopsU;

        public TrafficLane(char axis, int dir, double c, string group, double vmax, params double[] stops)
        {
            AxisX = axis == 'x'; Dir = dir; C = c; Group = group; Vmax = vmax; Stops = stops;
            StopsU = new double[stops.Length];
            for (int i = 0; i < stops.Length; i++) StopsU[i] = stops[i] * dir;
            Array.Sort(StopsU);
        }
    }

    /// <summary>One of the three procedural vehicles (levels/vehicle-geo.js TYPES): length, width, share and speed factor.</summary>
    public sealed class VehicleType
    {
        public readonly string Id;
        public readonly double L, W, Weight, V;
        public VehicleType(string id, double l, double w, double weight, double v) { Id = id; L = l; W = w; Weight = weight; V = v; }
    }

    /// <summary>The signal heads at time t: EW and NS each red | amber | green, and the seconds left in the current step.</summary>
    public readonly struct SignalState
    {
        public readonly string EW, NS;
        public readonly double Left;
        public SignalState(string ew, string ns, double left) { EW = ew; NS = ns; Left = left; }
        public string Of(string group) => group == "NS" ? NS : EW;
    }

    /// <summary>Lamp levels (emissive intensities) of the signal heads and pedestrian signals of one group.</summary>
    public struct SignalLamps
    {
        public float Red, Amber, Green, WalkRed, WalkGreen;
    }

    /// <summary>
    /// The town's traffic data and signal clock, verbatim from levels/town-plan.js (LANES, LANE_SPAN, CYCLE, signalAt),
    /// levels/vehicle-geo.js (TYPES) and levels/town-traffic.js (PALETTE, modes, setLamps). Original frame (x east, z south).
    /// </summary>
    public static class TownTraffic
    {
        public static readonly TrafficLane[] Lanes =
        {
            new('x', 1, 1.6, "EW", 12, -107.5, -63.5, -3.5, 44.5),
            new('x', 1, 4.4, "EW", 10.5, -107.5, -63.5, -3.5, 44.5),
            new('x', -1, -1.6, "EW", 12, 63.5, 3.5, -44.5, -100.5),
            new('x', -1, -4.4, "EW", 10.5, 63.5, 3.5, -44.5, -100.5),
            new('z', -1, -48, "NS", 9, 17.5),
            new('z', 1, -52, "NS", 9, -17.5),
            new('z', -1, 52, "NS", 9, 17.5),
            new('z', 1, 48, "NS", 9, -17.5),
        };

        /// <summary>Lanes run -340..340 so cars wrap far out in the city ring.</summary>
        public const double LaneSpan = 340;
        /// <summary>A person this close to a lane's centre line (on or stepping onto it) stops the cars coming at them.</summary>
        public const double Yield = 2.4;

        public static readonly VehicleType[] Types =
        {
            new("sedan", 4.7, 1.82, 0.5, 1), new("suv", 4.6, 1.88, 0.3, 0.95), new("truck", 5.1, 1.76, 0.2, 0.85),
        };

        public static VehicleType Type(string id)
        {
            foreach (var t in Types) if (t.Id == id) return t;
            return null;
        }

        /// <summary>Paint of the moving cars: sRGB hex and weight.</summary>
        public static readonly (string hex, double w)[] Palette =
        {
            ("#f2f2ef", 35), ("#8d9196", 20), ("#16181b", 15), ("#c4c7ca", 12), ("#1f2f4f", 8), ("#8e1f22", 4), ("#c9b89a", 3), ("#2f4a3a", 3),
        };

        public static readonly string[] Modes = { "normal", "light", "none" };

        /// <summary>Cars active per lane in a mode: x lanes (연무로) and z lanes (문화로 / 한빛로).</summary>
        public static (int x, int z) Count(string mode) => mode switch
        {
            "normal" => (4, 2), "light" => (2, 1), _ => (0, 0),
        };

        static double _cycle = -1;
        public static double CycleSeconds
        {
            get
            {
                if (_cycle < 0) { double s = 0; foreach (var c in TownCrowdPlan.Cycle) s += c.d; _cycle = s; }
                return _cycle;
            }
        }

        /// <summary>town-plan.js signalAt: EW green 20 s, amber 3, all red 1.5, NS green 13, amber 3, all red 1.5.</summary>
        public static SignalState SignalAt(double t)
        {
            double total = CycleSeconds;
            double u = ((t % total) + total) % total;
            foreach (var (g, c, d) in TownCrowdPlan.Cycle)
            {
                if (u < d) return new SignalState(g == "EW" ? c : "red", g == "NS" ? c : "red", d - u);
                u -= d;
            }
            return new SignalState("red", "red", 0);
        }

        /// <summary>
        /// town-traffic.js setLamps for one group: the lit lamp at 5, the walk figure at 4 while the group is green
        /// (blinking at 1 Hz over the last 5 s), the stop figure at 4 otherwise; all dark without power.
        /// </summary>
        public static SignalLamps Lamps(SignalState s, string group, double t, bool power)
        {
            string st = s.Of(group);
            bool walk = st == "green" && !(s.Left < 5 && ((long)Math.Floor(t * 2)) % 2 != 0);
            return new SignalLamps
            {
                Red = power && st == "red" ? 5 : 0,
                Amber = power && st == "amber" ? 5 : 0,
                Green = power && st == "green" ? 5 : 0,
                WalkGreen = power && walk ? 4 : 0,
                WalkRed = power && st != "green" ? 4 : 0,
            };
        }

        /// <summary>The lane a car stands in: the nearest centre line of its axis (null when none is within 1.5 m).</summary>
        public static int LaneOf(bool alongX, double x, double z)
        {
            int best = -1; double bd = 1.5;
            for (int i = 0; i < Lanes.Length; i++)
            {
                var l = Lanes[i];
                if (l.AxisX != alongX) continue;
                double d = Math.Abs((alongX ? z : x) - l.C);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>The road a lane runs on (연무로 for the x lanes, 문화로 / 한빛로 by the lane's centre line).</summary>
        public static TownRoad RoadOf(TrafficLane lane)
        {
            if (lane.AxisX) return TownMap.Roads[0];
            foreach (var r in TownMap.Roads)
                if (r.Rect.Z1 - r.Rect.Z0 > r.Rect.X1 - r.Rect.X0 && lane.C > r.Rect.X0 && lane.C < r.Rect.X1) return r;
            return null;
        }

        /// <summary>town-geo.js zebraZones: every zebra's carriageway, 0.6 m past each curb.</summary>
        public static Box2[] ZebraZones()
        {
            var zones = new Box2[TownMap.Crosswalks.Count];
            for (int k = 0; k < zones.Length; k++)
            {
                var c = TownMap.Crosswalks[k];
                var r = TownMap.Road(c.Road).Rect;
                bool ew = r.Z1 - r.Z0 < r.X1 - r.X0;
                zones[k] = ew ? new Box2(c.At - 2.6, c.At + 2.6, r.Z0 - 0.6, r.Z1 + 0.6) : new Box2(r.X0 - 0.6, r.X1 + 0.6, c.At - 2.6, c.At + 2.6);
            }
            return zones;
        }
    }

    /// <summary>A car of the moving pool (town-traffic.js pool entry), with its pose and live solid in the original frame.</summary>
    public sealed class TrafficCar
    {
        public int Lane, Rank, Slot;
        public string Type;
        public double U, V, V0, Len, Wid;
        public bool Active = true;
        public string Color = "#f2f2ef";
        /// <summary>Where it stands (original frame) and whether its solid is on (active and inside the town).</summary>
        public double X, Z;
        public bool SolidOn;
        public Box2 Solid;
    }

    /// <summary>
    /// The moving traffic of levels/town-traffic.js update(): cars on LANES follow each other (IDM), stop at red and at an
    /// amber they can stop for, wait for anyone in their lane ahead (the player only while down on the road) and short of
    /// a zebra someone is on, and wrap at ±LANE_SPAN. Pure: the view poses the meshes and colliders from the cars.
    /// </summary>
    public sealed class TrafficSim
    {
        public readonly List<TrafficCar> Pool;
        readonly List<TrafficCar>[] _byLane;
        readonly TrafficCar[] _cars = new TrafficCar[16];
        readonly double[] _inLane = new double[128];
        readonly double[] _zebra = new double[16];
        readonly Box2[] _zones = TownTraffic.ZebraZones();
        readonly int[][] _zebraOfLane;
        readonly bool[] _busy;
        string _mode = "normal";

        public string Mode => _mode;

        public TrafficSim(List<TrafficCar> pool)
        {
            Pool = pool ?? new List<TrafficCar>();
            var lanes = TownTraffic.Lanes;
            _byLane = new List<TrafficCar>[lanes.Length];
            for (int i = 0; i < lanes.Length; i++) _byLane[i] = new List<TrafficCar>();
            foreach (var c in Pool) if (c.Lane >= 0 && c.Lane < lanes.Length) _byLane[c.Lane].Add(c);
            _busy = new bool[_zones.Length];
            _zebraOfLane = new int[lanes.Length][];
            for (int li = 0; li < lanes.Length; li++)
            {
                var road = TownTraffic.RoadOf(lanes[li]);
                var list = new List<int>();
                for (int k = 0; k < TownMap.Crosswalks.Count; k++) if (road != null && TownMap.Crosswalks[k].Road == road.Id) list.Add(k);
                _zebraOfLane[li] = list.ToArray();
            }
            SetMode("normal");
            Pose();
        }

        /// <summary>
        /// The original's build: per lane 4 (x) or 2 (z) cars of a weighted random type, spread over the lane, at 80 % of
        /// the lane's top speed, each with its own cruise speed and paint.
        /// </summary>
        public static List<TrafficCar> DefaultPool(int seed = 31337)
        {
            var rnd = new Random(seed);
            var pool = new List<TrafficCar>();
            for (int li = 0; li < TownTraffic.Lanes.Length; li++)
            {
                var lane = TownTraffic.Lanes[li];
                int n = lane.AxisX ? 4 : 2;
                for (int i = 0; i < n; i++)
                {
                    var T = PickType(rnd);
                    pool.Add(Make(li, T, i, -TownTraffic.LaneSpan + (i + 0.2 + rnd.NextDouble() * 0.6) * (2 * TownTraffic.LaneSpan / n), rnd));
                }
            }
            return pool;
        }

        /// <summary>
        /// The pool rebuilt from where the export put each moving car (type, centre, along x or z): its lane by the nearest
        /// centre line, its rank by order along the lane; cruise speed and paint as the original draws them.
        /// </summary>
        public static List<TrafficCar> PoolFrom(IEnumerable<(string type, double x, double z, bool alongX)> cars, int seed = 31337)
        {
            var rnd = new Random(seed);
            var pool = new List<TrafficCar>();
            foreach (var (type, x, z, alongX) in cars)
            {
                int li = TownTraffic.LaneOf(alongX, x, z);
                var T = TownTraffic.Type(type);
                if (li < 0 || T == null) continue;
                var lane = TownTraffic.Lanes[li];
                pool.Add(Make(li, T, 0, (alongX ? x : z) * lane.Dir, rnd));
            }
            for (int li = 0; li < TownTraffic.Lanes.Length; li++)
            {
                var mine = pool.FindAll(c => c.Lane == li);
                mine.Sort((a, b) => a.U.CompareTo(b.U));
                for (int i = 0; i < mine.Count; i++) mine[i].Rank = i;
            }
            return pool;
        }

        static VehicleType PickType(Random rnd)
        {
            double sum = 0; foreach (var t in TownTraffic.Types) sum += t.Weight;
            double r = rnd.NextDouble() * sum;
            foreach (var t in TownTraffic.Types) if ((r -= t.Weight) <= 0) return t;
            return TownTraffic.Types[0];
        }

        static string PickColor(Random rnd)
        {
            double sum = 0; foreach (var p in TownTraffic.Palette) sum += p.w;
            double r = rnd.NextDouble() * sum;
            foreach (var p in TownTraffic.Palette) if ((r -= p.w) <= 0) return p.hex;
            return TownTraffic.Palette[0].hex;
        }

        static TrafficCar Make(int li, VehicleType T, int rank, double u, Random rnd)
        {
            var lane = TownTraffic.Lanes[li];
            return new TrafficCar
            {
                Lane = li, Type = T.Id, Rank = rank, U = u, V = lane.Vmax * 0.8, V0 = lane.Vmax * T.V * (0.9 + rnd.NextDouble() * 0.2),
                Len = T.L, Wid = T.W, Color = PickColor(rnd),
            };
        }

        /// <summary>town-traffic.js setMode: false (mode unchanged) for an unknown mode.</summary>
        public bool SetMode(string m)
        {
            if (Array.IndexOf(TownTraffic.Modes, m) < 0) return false;
            _mode = m;
            var (nx, nz) = TownTraffic.Count(m);
            foreach (var c in Pool) c.Active = c.Rank < (TownTraffic.Lanes[c.Lane].AxisX ? nx : nz);
            return true;
        }

        /// <summary>
        /// One frame. power: the signals run (else every lane goes as on green). The player counts only on the road
        /// (feet under 0.6 m); peds are the crowd members shown, in the original frame.
        /// </summary>
        public void Step(double dt, double t, bool power, bool hasPlayer, double px, double py, double pz, IReadOnlyList<Pt> peds, int pedCount)
        {
            var s = TownTraffic.SignalAt(t);
            bool plOnRoad = hasPlayer && py < 0.6;
            // busy zebras: someone stands on them now
            for (int k = 0; k < _zones.Length; k++)
            {
                var z = _zones[k];
                bool b = plOnRoad && z.Contains(px, pz);
                for (int i = 0; !b && i < pedCount; i++) b = z.Contains(peds[i].X, peds[i].Z);
                _busy[k] = b;
            }
            var lanes = TownTraffic.Lanes;
            for (int li = 0; li < lanes.Length; li++)
            {
                var lane = lanes[li];
                int n = 0;
                foreach (var c in _byLane[li]) if (c.Active && n < _cars.Length) _cars[n++] = c;
                for (int i = 1; i < n; i++) { var c = _cars[i]; int j = i - 1; while (j >= 0 && _cars[j].U > c.U) { _cars[j + 1] = _cars[j]; j--; } _cars[j + 1] = c; }
                string light = power ? s.Of(lane.Group) : "green";
                int np = 0;
                if (plOnRoad && Math.Abs((lane.AxisX ? pz : px) - lane.C) < TownTraffic.Yield) _inLane[np++] = (lane.AxisX ? px : pz) * lane.Dir;
                for (int i = 0; i < pedCount && np < _inLane.Length; i++)
                {
                    var p = peds[i];
                    if (Math.Abs((lane.AxisX ? p.Z : p.X) - lane.C) < TownTraffic.Yield) _inLane[np++] = (lane.AxisX ? p.X : p.Z) * lane.Dir;
                }
                int nz = 0;
                foreach (var k in _zebraOfLane[li]) if (_busy[k] && nz < _zebra.Length) _zebra[nz++] = TownMap.Crosswalks[k].At * lane.Dir - 3.2;
                for (int i = 0; i < n; i++)
                {
                    var c = _cars[i];
                    bool hasLead = i + 1 < n || n > 1;
                    double leadU = 0, leadV = 0, leadLen = 0;
                    if (i + 1 < n) { leadU = _cars[i + 1].U; leadV = _cars[i + 1].V; leadLen = _cars[i + 1].Len; }
                    else if (n > 1) { leadU = _cars[0].U + 2 * TownTraffic.LaneSpan; leadV = _cars[0].V; leadLen = _cars[0].Len; }
                    double front = c.U + c.Len / 2;
                    double gap = hasLead ? leadU - c.U - (c.Len + leadLen) / 2 : 1e9, vl = hasLead ? leadV : c.V0;
                    if (light != "green")
                        foreach (var su in lane.StopsU)
                        {
                            double d = su - front; if (d < -0.5) continue;
                            if (d < 80 && (light == "red" || d > c.V * c.V / 8 + 2) && d < gap) { gap = Math.Max(0.05, d); vl = 0; }
                            break;
                        }
                    for (int k = 0; k < np; k++) { double d = _inLane[k] - front - 1.4; if (d > -2.5 && d < 30 && d < gap) { gap = Math.Max(0.05, d); vl = 0; } }
                    for (int k = 0; k < nz; k++) { double d = _zebra[k] - front; if (d > -0.5 && d < 40 && d < gap) { gap = Math.Max(0.05, d); vl = 0; } }
                    c.V = Accel(c, gap, vl, dt);
                    c.U += c.V * dt;
                    if (c.U > TownTraffic.LaneSpan) c.U -= 2 * TownTraffic.LaneSpan;
                }
            }
            Pose();
        }

        /// <summary>The IDM step: a 2.2 m/s² accelerator, 3.2 m/s² comfortable brake, 1.6 m jam gap, 1.2 s headway, -9 m/s² at most.</summary>
        public static double Accel(TrafficCar c, double gap, double vl, double dt)
        {
            double dv = c.V - vl, sStar = 1.6 + Math.Max(0, c.V * 1.2 + c.V * dv / (2 * Math.Sqrt(2.2 * 3.2)));
            double r = c.V / c.V0, g = sStar / Math.Max(gap, 0.05);
            double acc = Math.Max(-9, 2.2 * (1 - r * r * r * r - g * g));
            return Math.Max(0, c.V + acc * dt);
        }

        void Pose()
        {
            foreach (var c in Pool)
            {
                var lane = TownTraffic.Lanes[c.Lane];
                double s0 = c.U * lane.Dir;
                c.X = lane.AxisX ? s0 : lane.C; c.Z = lane.AxisX ? lane.C : s0;
                c.SolidOn = c.Active && Math.Abs(c.X) <= 135 && Math.Abs(c.Z) <= 115;
                double hl = c.Len / 2 + 0.05, hw = c.Wid / 2 + 0.05;
                c.Solid = lane.AxisX ? new Box2(c.X - hl, c.X + hl, c.Z - hw, c.Z + hw) : new Box2(c.X - hw, c.X + hw, c.Z - hl, c.Z + hl);
            }
        }
    }

    /// <summary>
    /// The closures' guides, lamps and light towers (levels/town-events.js, town-plan.js CLOSURE_GUIDES). Original frame.
    /// </summary>
    public static class TownClosures
    {
        /// <summary>Where the guide of each closure stands (the side a player from 103동 usually arrives on) and faces.</summary>
        public static readonly IReadOnlyDictionary<string, (double x, double z, double fx, double fz)> Guides =
            new Dictionary<string, (double, double, double, double)>
            {
                ["yeonmuCenter"] = (30.4, 8.4, 1, 0),
                ["danji"] = (2.4, 54, 1, 0),
                ["sijang"] = (2.4, -54, 1, 0),
                ["munhwaSouth"] = (-56, 47.4, 0, 1),
                ["munhwaNorth"] = (-56, -21.4, 0, -1),
            };

        /// <summary>The portable light tower beside the guide (1.6 m to the guide's side) and where its spot points (6 m back).</summary>
        public static (double x, double z, double tx, double tz) Tower(string id)
        {
            var g = Guides[id];
            double x = g.x + g.fz * 1.6, z = g.z - g.fx * 1.6;
            return (x, z, x - g.fx * 6, z - g.fz * 6);
        }

        /// <summary>The flashing amber lamp: 4 on the lit half of a 1.6 Hz blink while any closure is up; steady with reduced motion.</summary>
        public static float Flash(double t, bool anyActive, bool reduced)
        {
            bool blink = reduced || ((long)Math.Floor(t * 1.6)) % 2 == 0;
            return anyActive && blink ? 4f : 0f;
        }

        /// <summary>
        /// The guide's animation (town-events.js update): a 9 s cycle offset 2.3 s per guide; near the player (8 m) point
        /// for 3 s then talk, else wave for 3 s then idle.
        /// </summary>
        public static string GuideAnim(double gt, int k, bool near)
        {
            double phase = ((gt + k * 2.3) % 9 + 9) % 9;
            return near ? (phase < 3 ? "point" : "talk") : phase < 3 ? "wave" : "idle";
        }

        /// <summary>The ids of the active closures, in the story's order, that are known (the guides take them in turn).</summary>
        public static List<string> Known(IEnumerable<string> ids)
        {
            var l = new List<string>();
            if (ids != null) foreach (var id in ids) if (id != null && Guides.ContainsKey(id) && !l.Contains(id)) l.Add(id);
            return l;
        }
    }
}
