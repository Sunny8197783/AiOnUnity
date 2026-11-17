// The numbers and rules of the interior levels' live dressing (original levels/apartment.js stepLights,
// apartment-stock.js, apartment-props.js candle, store.js applyPower / clerk, office.js update, shelter.js update),
// kept free of scene objects so they can be tested. Positions are the original's frame (x, y, z).
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.Levels.Interiors
{
    public static class InteriorLogic
    {
        // ------------------------------------------------------------------ apartment: 베란다 수납장 (apartment-stock.js)

        /// <summary>HOME.bf: the balcony floor (FL - 0.08).</summary>
        public const float StockFloor = 5.75f - 0.08f;
        public const float StockFront = -0.13f, StockBack = 0.26f;
        public static readonly float[] Shelf = { StockFloor + 0.08f, StockFloor + 0.48f, StockFloor + 0.88f, StockFloor + 1.28f, StockFloor + 1.66f };
        /// <summary>2 L bottles that fit on the bottom shelf before the rest go on the floor.</summary>
        public const int ShelfWater2L = 16;

        public enum StockShape { Bottle, Can, Butane, Roll, Box }

        /// <summary>A slot block: nx across the depth, nz along the shelf, ny layers h apart, from y.</summary>
        public struct Block
        {
            public float Z0, Z1, Y, H; public int Nx, Nz, Ny; public bool Floor; public Vector3 Scale;
        }

        public sealed class StockKind
        {
            public string Id; public StockShape Shape; public int Per; public Block[] Blocks;
            /// <summary>Box size (depth x, height y, length z) and its print colours (background, foreground).</summary>
            public Vector3 Size; public string Bg, Fg;
            public bool Cross; // the first-aid box's red cross
        }

        static Block B(float z0, float z1, int nx, int nz, int ny, int shelf, float h = 0, Vector3? s = null)
            => new Block { Z0 = z0, Z1 = z1, Nx = nx, Nz = nz, Ny = ny, Y = Shelf[shelf], H = h, Scale = s ?? Vector3.one };

        static StockKind K(string id, StockShape shape, int per, Block[] blocks, Vector3 size = default, string bg = null, string fg = null, bool cross = false)
            => new StockKind { Id = id, Shape = shape, Per = per, Blocks = blocks, Size = size, Bg = bg, Fg = fg, Cross = cross };

        /// <summary>The kinds on the shelf, in the original's order (KINDS).</summary>
        public static readonly StockKind[] Kinds =
        {
            K("water_2l", StockShape.Bottle, 1, new[] { B(8.14f, 9.0f, 2, 8, 1, 0), new Block { Floor = true, Scale = Vector3.one } }),
            K("water_500", StockShape.Bottle, 1, new[] { B(9.02f, 9.26f, 5, 3, 1, 0, 0, new Vector3(0.64f, 0.66f, 0.64f)) }),
            K("ramen", StockShape.Box, 1, new[] { B(8.14f, 8.74f, 2, 3, 4, 1, 0.056f) }, new Vector3(0.14f, 0.055f, 0.19f), "#d8312a", "#fff3c4"),
            K("rice_instant", StockShape.Box, 1, new[] { B(8.78f, 9.26f, 3, 3, 4, 1, 0.041f) }, new Vector3(0.115f, 0.04f, 0.13f), "#f7f6f2", "#c4161c"),
            K("canned", StockShape.Can, 1, new[] { B(8.14f, 8.6f, 3, 5, 3, 2, 0.047f) }),
            K("batteries_aa", StockShape.Box, 1, new[] { B(8.62f, 8.84f, 6, 2, 1, 2) }, new Vector3(0.02f, 0.16f, 0.11f), "#1d1d1f", "#f2c230"),
            K("candles", StockShape.Box, 1, new[] { B(8.86f, 9.26f, 3, 2, 2, 2, 0.112f) }, new Vector3(0.07f, 0.11f, 0.2f), "#f6f0dc", "#8a5a1c"),
            K("gas_stove", StockShape.Box, 1, new[] { B(8.14f, 8.5f, 1, 1, 3, 3, 0.102f) }, new Vector3(0.27f, 0.1f, 0.34f), "#2a2c30", "#f4f4f1"),
            K("butane", StockShape.Butane, 4, new[] { B(8.52f, 8.92f, 4, 5, 1, 3) }),
            K("first_aid", StockShape.Box, 1, new[] { B(8.95f, 9.26f, 2, 1, 2, 3, 0.102f) }, new Vector3(0.14f, 0.1f, 0.2f), "#f8f8f6", "#d7262d", true),
            K("tissue", StockShape.Roll, 1, new[] { B(8.14f, 8.5f, 3, 3, 2, 4, 0.1f) }),
            K("blanket", StockShape.Box, 1, new[] { B(8.52f, 8.76f, 1, 1, 6, 4, 0.031f) }, new Vector3(0.1f, 0.03f, 0.16f), "#d7b65a", "#5a4410"),
            K("wipes", StockShape.Box, 1, new[] { B(8.78f, 9.0f, 1, 1, 3, 4, 0.071f) }, new Vector3(0.11f, 0.07f, 0.2f), "#e9f5ec", "#2f8a4a"),
            K("water_jug", StockShape.Box, 1, new[] { B(9.02f, 9.26f, 1, 1, 2, 4, 0.061f) }, new Vector3(0.18f, 0.06f, 0.22f), "#2f6fb2", "#ffffff"),
        };

        public struct Slot { public Vector3 Pos; public float Yaw; public Vector3 Scale; }

        /// <summary>
        /// The slots of a kind in fill order (lower layers first; the 2 L floor row after the shelf). Yaw is the original's
        /// ry in radians; jitter comes from a fixed seed, so the shelf looks the same every time.
        /// </summary>
        public static List<Slot> Slots(StockKind k, int seed = 302)
        {
            int h = seed;
            foreach (char c in k.Id) h = h * 31 + c;
            var r = new System.Random(h & 0x7fffffff);
            float R() => (float)r.NextDouble();
            bool round = k.Shape != StockShape.Box;
            var slots = new List<Slot>();
            foreach (var b in k.Blocks)
            {
                if (b.Floor)
                {
                    for (int row = 0; row < 2; row++)
                        for (int i = 0; i < 12; i++)
                            slots.Add(new Slot { Pos = new Vector3(-0.32f - i * 0.108f, StockFloor, 9.25f - row * 0.108f), Yaw = R() * 6.28f, Scale = Vector3.one });
                    continue;
                }
                float dx = (StockBack - StockFront) / b.Nx, dz = (b.Z1 - b.Z0) / b.Nz;
                for (int y = 0; y < b.Ny; y++)
                    for (int i = 0; i < b.Nx; i++)
                        for (int j = 0; j < b.Nz; j++)
                            slots.Add(new Slot
                            {
                                Pos = new Vector3(StockFront + (i + 0.5f) * dx + (R() - 0.5f) * 0.008f, b.Y + y * b.H, b.Z0 + (j + 0.5f) * dz + (R() - 0.5f) * 0.008f),
                                Yaw = (R() - 0.5f) * (round ? 6f : 0.12f), Scale = b.Scale,
                            });
            }
            return slots;
        }

        /// <summary>Units shown for a count: count x per, capped by the slots (missing or bad counts are 0).</summary>
        public static int Shown(JToken count, int per, int cap)
        {
            double n = 0;
            if (count != null && (count.Type == JTokenType.Integer || count.Type == JTokenType.Float)) n = (double)count;
            else if (count != null && count.Type == JTokenType.String) double.TryParse((string)count, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n);
            if (double.IsNaN(n)) n = 0;
            return Math.Max(0, Math.Min(cap, (int)Math.Round(n * per, MidpointRounding.AwayFromZero)));
        }

        /// <summary>
        /// The body of the 2 L bottles on the floor (original frame): off when none overflow, else the row(s) shown,
        /// y from the floor to 0.5 m above it (taller than a step).
        /// </summary>
        public static bool FloorRow(int water2LShown, out float x0, out float x1, out float z0, out float z1)
        {
            int n = water2LShown - ShelfWater2L, cols = Math.Min(12, n);
            x0 = x1 = z0 = z1 = 0;
            if (n <= 0) return false;
            x0 = -0.32f - (cols - 1) * 0.108f - 0.06f; x1 = -0.26f;
            z0 = (n > 12 ? 9.142f : 9.25f) - 0.06f; z1 = 9.31f;
            return true;
        }

        // ------------------------------------------------------------------ apartment: lights (apartment.js)

        public const float SensorHold = 9f, SensorReach = 3.4f, SensorHeadBand = 1.8f;

        /// <summary>The light zone of a point (apartment.js zoneOf): stairs, home (302호), corridor or lobby.</summary>
        public static string AptZone(float x, float y, float z)
            => x > 3.3f && x < 6.7f && z > 0 ? "stairs" : y > 4 ? (z > 0.05f && x < 0.35f ? "home" : "corridor") : "lobby";

        /// <summary>
        /// One motion-sensor lamp's step: a player in its zone within 3.4 m (flat) and with the head within 1.8 m of the
        /// lamp's height re-arms the 9 s hold; the lamp is lit while powered and held.
        /// </summary>
        public static bool SensorStep(ref float hold, float dt, bool power, bool sameZone, float flatDistance, float headToLamp)
        {
            if (power && sameZone && flatDistance < SensorReach && Mathf.Abs(headToLamp) < SensorHeadBand) hold = SensorHold;
            hold = Mathf.Max(0, hold - dt);
            return power && hold > 0;
        }

        /// <summary>
        /// Zones keep the fixtures' light from shining through closed walls (apartment.js LINKS / linked): home and the
        /// corridor meet at 302's door, the corridor and the stairs at the 3F stair door, the stairs and the lobby at the
        /// ground-floor stair door; a link counts while its door is open more than 0.1.
        /// </summary>
        public static bool AptLinked(string a, string b, float door302, float stairDoor3, float stairDoor1)
        {
            bool Pair(string u, string v) => (a == u && b == v) || (a == v && b == u);
            return (Pair("home", "corridor") && door302 > 0.1f) || (Pair("corridor", "stairs") && stairDoor3 > 0.1f)
                || (Pair("stairs", "lobby") && stairDoor1 > 0.1f);
        }

        /// <summary>A fixture farther than this (by FixtureDistance) gets no light.</summary>
        public const float PoolReach = 20f;
        /// <summary>The original's light pool (5 on the default tier) less one: the nearest this many lit fixtures shine.</summary>
        public const int PoolLit = 4;

        /// <summary>
        /// A fixture's pool distance (apartment.js stepLights f.d): off = infinite; in the player's zone the distance (the
        /// height counting 1.6x); in a zone linked by an open door twice that plus 4; any other zone infinite.
        /// </summary>
        public static float FixtureDistance(bool on, bool sameZone, bool linked, float dx, float headToFixture, float dz)
        {
            if (!on) return float.PositiveInfinity;
            float d = Mathf.Sqrt(dx * dx + headToFixture * 1.6f * headToFixture * 1.6f + dz * dz);
            return sameZone ? d : linked ? d * 2 + 4 : float.PositiveInfinity;
        }

        /// <summary>
        /// The building's grid lamps the motion sensors replace: the stairwell and the 3rd-floor corridor strip (their
        /// only lamps in the original are the sensor lamps). Rooms (lobby, 302호, the 303 entry) keep theirs.
        /// </summary>
        public static bool AptSensorArea(float x, float y, float z)
            => (x > 3.3f && x < 6.7f && z > 0) || (y > 4 && z < 0.1f && z > -2.1f);

        /// <summary>The sensor lamps (x, ceiling y, z): 4 stair landings, 3 mid landings, 9 in the corridor, 302's entry.</summary>
        public static readonly Vector3[] AptSensors = BuildSensors();

        static Vector3[] BuildSensors()
        {
            var l = new List<Vector3>();
            const float fh = 2.8f;
            for (int n = 1; n <= 4; n++) l.Add(new Vector3(5f, fh * (n - 1) + 2.58f, 0.75f));
            for (int n = 1; n <= 3; n++) l.Add(new Vector3(5f, fh * (n - 1) + 3.98f, 4.3f));
            for (float x = -14; x <= 18; x += 4) l.Add(new Vector3(x, 5.6f + 2.4f - 0.02f, -0.95f));
            l.Add(new Vector3(-1.25f, 5.75f + 2.3f - 0.02f, 0.65f));
            return l.ToArray();
        }

        /// <summary>The battery 비상조명등 (fixture points): lobby by the stair door, every stair landing and mid landing.</summary>
        public static readonly Vector3[] AptBackups = BuildBackups();

        static Vector3[] BuildBackups()
        {
            var l = new List<Vector3> { new Vector3(3.2f, 2.2f, -0.45f) };
            const float fh = 2.8f;
            for (int n = 1; n <= 4; n++) l.Add(new Vector3(3.71f, fh * (n - 1) + 2.2f, 0.75f));
            for (int n = 1; n <= 3; n++) l.Add(new Vector3(3.71f, fh * (n - 1) + 3.6f, 4.3f));
            return l.ToArray();
        }

        // ------------------------------------------------------------------ candle (apartment-props.js)

        /// <summary>The candle light's flicker factor at time t (about 0.85..1.15).</summary>
        public static float CandleFlicker(float t)
            => 1 + 0.07f * Mathf.Sin(t * 13.1f) + 0.05f * Mathf.Sin(t * 7.3f + 1.3f) + 0.03f * Mathf.Sin(t * 23.7f);

        // ------------------------------------------------------------------ store (store.js)

        public static string CounterLabel(string clerk) => clerk == "out" ? "계산대 (아무도 없다)" : "계산대 · 이수아";

        // ------------------------------------------------------------------ office (office.js update)

        /// <summary>The yard worker paces between x -8 and 6 at 1.1 m/s; pace flips at the ends.</summary>
        public static void WorkerPace(ref float x, ref float pace, float dt)
        {
            x += pace * 1.1f * dt;
            if (x > 6 || x < -8) { pace = -pace; x = Mathf.Clamp(x, -8, 6); }
        }

        /// <summary>A dashboard row: label, value, delta, hot.</summary>
        public struct DashRow { public string Label, Value, Delta; public bool Hot; }

        /// <summary>The 'dashboard' state ({ kind, title, updated, rows: [[a, b, d, hot]] }); false when it is not one.</summary>
        public static bool ParseDashboard(JToken v, out string title, out string updated, List<DashRow> rows)
        {
            title = updated = null;
            rows?.Clear();
            if (!(v is JObject o) || !(o["rows"] is JArray a)) return false;
            title = (string)o["title"] ?? ""; updated = (string)o["updated"] ?? "";
            foreach (var r in a)
            {
                if (!(r is JArray ra) || ra.Count < 3) continue;
                rows?.Add(new DashRow { Label = (string)ra[0], Value = (string)ra[1], Delta = (string)ra[2], Hot = ra.Count > 3 && ra[3].Type == JTokenType.Boolean && (bool)ra[3] });
            }
            return true;
        }

        // ------------------------------------------------------------------ shelter (shelter.js update)

        /// <summary>
        /// The circuit of an area lamp (original frame): in the main hall A / B alternate along x (12 m) and between the
        /// two halves (z sign); the lamps outside it (stair A, the ramps, the lift hall) are all on circuit A
        /// (shelter-shell.js lights).
        /// </summary>
        public static char ShelterCircuit(float x, float y, float z)
        {
            if (Mathf.Abs(x) > 22.25f || Mathf.Abs(z) > 16.25f || y >= 3.1f) return 'A';
            return ((Mathf.FloorToInt((x + 20f) / 12f) + (z < 0 ? 0 : 1)) & 1) == 0 ? 'A' : 'B';
        }

        /// <summary>An area lamp's level: full on the grid; on the emergency supply circuit A runs at 55 %, B is off.</summary>
        public static float ShelterLampFactor(bool grid, char circuit) => grid ? 1f : circuit == 'A' ? 0.55f : 0f;
    }
}
