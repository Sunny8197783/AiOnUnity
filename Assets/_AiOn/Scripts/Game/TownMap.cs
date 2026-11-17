using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AiOn.Game
{
    // ---------- shapes ----------

    /// <summary>A point on the town's ground plane, in metres: X = east, Z = south (north is -Z).</summary>
    public readonly struct TownPoint
    {
        public readonly double X, Z;
        public TownPoint(double x, double z) { X = x; Z = z; }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Z);
    }

    /// <summary>An axis-aligned ground rectangle: X0..X1 west to east, Z0..Z1 north to south (metres).</summary>
    public readonly struct TownRect
    {
        public readonly double X0, X1, Z0, Z1;
        public TownRect(double x0, double x1, double z0, double z1) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; }

        /// <summary>East-west size.</summary>
        public double Width => X1 - X0;
        /// <summary>North-south size.</summary>
        public double Depth => Z1 - Z0;
        public TownPoint Center => new TownPoint((X0 + X1) / 2, (Z0 + Z1) / 2);
        public bool Contains(double x, double z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "x {0}..{1}, z {2}..{3}", X0, X1, Z0, Z1);
    }

    // ---------- rows of the tables ----------

    public sealed class TownRoad
    {
        public readonly string Id;
        /// <summary>Korean street name, drawn on the map.</summary>
        public readonly string Name;
        /// <summary>The carriageway; the sidewalk (<see cref="TownMap.Sidewalk"/>) lies outside it.</summary>
        public readonly TownRect Rect;
        public readonly int Lanes;

        public TownRoad(string id, string name, double x0, double x1, double z0, double z1, int lanes)
        { Id = id; Name = name; Rect = new TownRect(x0, x1, z0, z1); Lanes = lanes; }

        /// <summary>Runs east-west: its crosswalks are placed by x. Otherwise north-south, placed by z.</summary>
        public bool EastWest => Rect.Width > Rect.Depth;
    }

    /// <summary>A zebra crossing: the road it crosses and its centre along that road (x for E-W roads, z for N-S roads).</summary>
    public sealed class TownCrosswalk
    {
        public readonly string Road;
        public readonly double At;
        public TownCrosswalk(string road, double at) { Road = road; At = at; }
    }

    public sealed class TownBuilding
    {
        public readonly string Id;
        /// <summary>Korean name; null for background blocks (not enterable, not labelled).</summary>
        public readonly string Name;
        /// <summary>apartment | civic | shop | mart | office | warehouse | villa</summary>
        public readonly string Kind;
        public readonly TownRect Rect;
        public readonly int Floors;
        /// <summary>Height in metres when it is not floors x 3 (the warehouse); else null.</summary>
        public readonly double? H;
        /// <summary>Big number painted on the apartment blocks ('103'); else null.</summary>
        public readonly string Label;
        /// <summary>Which face has the main door: 'n' | 's' | 'e' | 'w'; null when it has none in the game.</summary>
        public readonly string Door;
        /// <summary>Text of the shop sign; null when there is none.</summary>
        public readonly string Sign;

        public TownBuilding(string id, string name, string kind, double x0, double x1, double z0, double z1, int floors,
            double? h = null, string label = null, string door = null, string sign = null)
        { Id = id; Name = name; Kind = kind; Rect = new TownRect(x0, x1, z0, z1); Floors = floors; H = h; Label = label; Door = door; Sign = sign; }

        /// <summary>Metres: floors x 3 unless H is given.</summary>
        public double Height => H ?? Floors * 3;
    }

    /// <summary>An open area the town builder fills (park, lots, yards).</summary>
    public sealed class TownArea
    {
        public readonly string Id, Name;
        /// <summary>park | parking | plaza | complex</summary>
        public readonly string Kind;
        public readonly TownRect Rect;

        public TownArea(string id, string name, string kind, double x0, double x1, double z0, double z1)
        { Id = id; Name = name; Kind = kind; Rect = new TownRect(x0, x1, z0, z1); }
    }

    /// <summary>An interaction point in the town.</summary>
    public sealed class TownPlace
    {
        public readonly string Id;
        public readonly TownPoint Pos;
        public readonly string Label;
        /// <summary>door (to another level) | shelter | notice | place</summary>
        public readonly string Kind;
        /// <summary>The level a door or shelter entrance leads to; null for the others.</summary>
        public readonly string To;

        public TownPlace(string id, double x, double z, string label, string kind, string to = null)
        { Id = id; Pos = new TownPoint(x, z); Label = label; Kind = kind; To = to; }

        public double X => Pos.X;
        public double Z => Pos.Z;
    }

    /// <summary>Where the player appears in the town. Yaw is in radians, 0 faces north (-Z).</summary>
    public sealed class TownSpawn
    {
        public readonly string Id;
        public readonly TownPoint Pos;
        public readonly double Yaw;

        public TownSpawn(string id, double x, double z, double yaw) { Id = id; Pos = new TownPoint(x, z); Yaw = yaw; }

        public double X => Pos.X;
        public double Z => Pos.Z;
    }

    /// <summary>A node of the pedestrian network (2D map and route planning).</summary>
    public sealed class TownNode
    {
        public readonly string Id;
        public readonly TownPoint Pos;
        /// <summary>Korean name used when a route is told in words; null for plain junctions (I1..I6).</summary>
        public readonly string Label;

        public TownNode(string id, double x, double z, string label = null) { Id = id; Pos = new TownPoint(x, z); Label = label; }

        public double X => Pos.X;
        public double Z => Pos.Z;
    }

    /// <summary>A walkable way between two nodes. Its segment id is what closures and shelter gates switch off.</summary>
    public sealed class TownEdge
    {
        public readonly string From, To, Segment;
        /// <summary>What the way itself is like: crossing | level | stairs | steep | ramp (see <see cref="TownMap.Tag"/>).</summary>
        public readonly IReadOnlyList<string> Tags;
        /// <summary>Metres between the two nodes.</summary>
        public readonly double Length;

        public TownEdge(string from, string to, string segment, IReadOnlyList<string> tags, double length)
        { From = from; To = to; Segment = segment; Tags = tags ?? Array.Empty<string>(); Length = length; }

        public bool Touches(string node) => From == node || To == node;
        /// <summary>The other end, seen from <paramref name="node"/> (null when the edge does not touch it).</summary>
        public string Other(string node) => node == From ? To : node == To ? From : null;
    }

    /// <summary>One way into the shelter. Which ones are open is game state, not a closure.</summary>
    public sealed class TownShelterEntrance
    {
        /// <summary>east | stairs | ramp | access: the key game state and route options use.</summary>
        public readonly string Id;
        /// <summary>The network node in front of it.</summary>
        public readonly string Node;
        /// <summary>Its interaction point in <see cref="TownMap.Places"/>.</summary>
        public readonly string Place;
        /// <summary>The edge into the shelter that is cut while it is closed.</summary>
        public readonly string Segment;
        public readonly string Label;
        /// <summary>Short map label ('A 계단'); null for the east entrance.</summary>
        public readonly string Short;
        /// <summary>level | stairs | steep | ramp: the tag of its segment.</summary>
        public readonly string Way;

        public TownShelterEntrance(string id, string node, string place, string segment, string label, string way, string @short = null)
        { Id = id; Node = node; Place = place; Segment = segment; Label = label; Way = way; Short = @short; }
    }

    /// <summary>A road closure: the segments it cuts, and where the barricades stand (rects the player cannot cross).</summary>
    public sealed class TownClosure
    {
        public readonly string Id, Label;
        public readonly IReadOnlyList<string> Segments;
        public readonly IReadOnlyList<TownRect> Barricades;

        public TownClosure(string id, string label, string[] segments, TownRect[] barricades)
        { Id = id; Label = label; Segments = segments; Barricades = barricades; }
    }

    /// <summary>One direction of an open edge out of a node (the original graph() entry { to, seg, tags, len }).</summary>
    public sealed class TownLink
    {
        public readonly string To, Seg;
        public readonly IReadOnlyList<string> Tags;
        public readonly double Len;

        public TownLink(string to, string seg, IReadOnlyList<string> tags, double len)
        { To = to; Seg = seg; Tags = tags ?? Array.Empty<string>(); Len = len; }
    }

    // ---------- tables ----------

    /// <summary>
    /// A read-only table keyed by id that keeps its declaration order (the original's object literals:
    /// their key order decides ties, so it must not be lost). table[id] is null for an unknown id, like NODES[id].
    /// </summary>
    public class TownTable<T> : IReadOnlyDictionary<string, T> where T : class
    {
        readonly List<string> _keys = new();
        readonly List<T> _values = new();
        readonly Dictionary<string, T> _byId = new();

        public TownTable(IEnumerable<KeyValuePair<string, T>> rows)
        {
            foreach (var kv in rows)
            {
                _byId.Add(kv.Key, kv.Value); // throws on a duplicate id
                _keys.Add(kv.Key);
                _values.Add(kv.Value);
            }
        }

        public int Count => _keys.Count;
        /// <summary>Ids in declaration order.</summary>
        public IReadOnlyList<string> Keys => _keys;
        /// <summary>Rows in declaration order.</summary>
        public IReadOnlyList<T> Values => _values;
        /// <summary>The row, or null when there is none (never throws).</summary>
        public T this[string id] => id != null && _byId.TryGetValue(id, out var v) ? v : null;
        public bool ContainsKey(string id) => id != null && _byId.ContainsKey(id);

        public bool TryGetValue(string id, out T value)
        {
            if (id != null) return _byId.TryGetValue(id, out value);
            value = null;
            return false;
        }

        IEnumerable<string> IReadOnlyDictionary<string, T>.Keys => _keys;
        IEnumerable<T> IReadOnlyDictionary<string, T>.Values => _values;

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
        {
            for (int i = 0; i < _keys.Count; i++) yield return new KeyValuePair<string, T>(_keys[i], _values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// The pedestrian network with closed segments and closed shelter gates left out (the original graph() result):
    /// node id -> the links out of it. Every node is a key, also one with no open link.
    /// </summary>
    public sealed class TownGraph : TownTable<IReadOnlyList<TownLink>>
    {
        public TownGraph(IEnumerable<KeyValuePair<string, IReadOnlyList<TownLink>>> rows) : base(rows) { }

        /// <summary>The same graph without the given segments (routes use it to look for a detour).</summary>
        public TownGraph Without(IReadOnlyCollection<string> segments)
        {
            if (segments == null || segments.Count == 0) return this;
            return new TownGraph(this.Select(kv => new KeyValuePair<string, IReadOnlyList<TownLink>>(
                kv.Key, kv.Value.Where(e => !segments.Contains(e.Seg)).ToList())));
        }

        /// <summary>Segment ids that are open, each once, in first-seen order (for drawing the network).</summary>
        public List<string> Segments()
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (var links in Values)
                foreach (var e in links)
                    if (seen.Add(e.Seg)) list.Add(e.Seg);
            return list;
        }
    }

    /// <summary>
    /// The fictional 연무동 town layout (original game/townmap.js): one source of truth for the 3D town, the 2D map
    /// and route logic. Frame: x = east, z = south (north is -z), metres, ground y = 0. Pure data, no scene objects.
    /// This is the original web build's frame, the one LevelLayout data is in: a scene or map that needs Unity world
    /// space converts with LevelLayout.ToUnity / ToUnityYaw (z flips, yaw turns the other way).
    /// </summary>
    public static class TownMap
    {
        /// <summary>Edge tags (what the way itself is like).</summary>
        public static class Tag
        {
            public const string Crossing = "crossing", Level = "level", Stairs = "stairs", Steep = "steep", Ramp = "ramp";
        }

        public static readonly TownRect Bounds = new TownRect(-130, 130, -110, 110);

        /// <summary>Sidewalk width along every road edge, curb height 0.12.</summary>
        public const double Sidewalk = 4;

        public static readonly IReadOnlyList<TownRoad> Roads = new[]
        {
            new TownRoad("yeonmu", "연무로", -130, 130, -6, 6, 4),
            new TownRoad("munhwa", "문화로", -54, -46, -110, 110, 2),
            new TownRoad("hanbit", "한빛로", 46, 54, -110, 110, 2),
            new TownRoad("sijang", "시장길", -46, 46, -64, -56, 2),
            new TownRoad("danji", "단지길", -46, 46, 56, 64, 2),
        };

        // Zebra crossings: road id + centre coordinate along that road (x for E-W roads, z for N-S roads).
        public static readonly IReadOnlyList<TownCrosswalk> Crosswalks = new[]
        {
            new TownCrosswalk("yeonmu", -104), new TownCrosswalk("yeonmu", -60), new TownCrosswalk("yeonmu", 0), new TownCrosswalk("yeonmu", 60),
            new TownCrosswalk("munhwa", -14), new TownCrosswalk("munhwa", 14), new TownCrosswalk("hanbit", -14), new TownCrosswalk("hanbit", 14),
            new TownCrosswalk("sijang", 0), new TownCrosswalk("danji", 0),
        };

        // Building footprints. floors x 3 m unless h is given. door: which face has the main door.
        public static readonly IReadOnlyList<TownBuilding> Buildings = new[]
        {
            new TownBuilding("apt103", "한빛아파트 103동", "apartment", 64, 112, 30, 42, 15, label: "103", door: "n"),
            new TownBuilding("apt102", "한빛아파트 102동", "apartment", 64, 112, 58, 70, 15, label: "102"),
            new TownBuilding("apt101", "한빛아파트 101동", "apartment", 64, 112, 86, 98, 15, label: "101"),
            new TownBuilding("aptOffice", "한빛아파트 관리동", "civic", 116, 126, 30, 40, 1),
            new TownBuilding("store", "바로편의점", "shop", 30, 42, 12, 22, 1, door: "n", sign: "바로편의점"),
            new TownBuilding("shopsS", "상가", "shop", 16, 29, 12, 22, 2, sign: "연무약국"),
            new TownBuilding("mart", "연무마트", "mart", -36, 4, -50, -24, 2, door: "s", sign: "연무마트"),
            new TownBuilding("office", "한결물류 연무센터", "office", 70, 104, -48, -22, 4, door: "s", sign: "한결물류"),
            new TownBuilding("warehouse", "한결물류 창고", "warehouse", 64, 124, -104, -60, 1, h: 10),
            new TownBuilding("center", "연무동 행정복지센터", "civic", -104, -74, -50, -28, 3, door: "s", sign: "연무동 행정복지센터"),
            new TownBuilding("cafe", "카페 쉼표", "shop", -126, -110, -34, -16, 2, door: "s", sign: "카페 쉼표"),
            new TownBuilding("culture", "연무 생활문화센터", "civic", -112, -70, 16, 46, 3, door: "e", sign: "연무 생활문화센터"),
            // Background blocks (not enterable)
            new TownBuilding("n1", null, "shop", -40, -8, -104, -70, 4),
            new TownBuilding("n2", null, "villa", 0, 40, -104, -70, 4),
            new TownBuilding("nw1", null, "villa", -126, -66, -104, -62, 4),
            new TownBuilding("s1", null, "villa", -40, -4, 70, 104, 4),
            new TownBuilding("s2", null, "villa", 4, 40, 70, 104, 5),
            new TownBuilding("sw1", null, "villa", -126, -62, 60, 104, 4),
        };

        // Open areas the town builder fills (park, lots, yards).
        public static readonly IReadOnlyList<TownArea> Areas = new[]
        {
            new TownArea("park", "연무공원", "park", -42, 12, 12, 52),
            new TownArea("martLot", "마트 주차장", "parking", -36, 4, -22, -12),
            new TownArea("centerYard", "행정복지센터 마당", "plaza", -104, -60, -26, -12),
            new TownArea("aptGrounds", "한빛아파트 단지", "complex", 58, 128, 12, 104),
            new TownArea("officeLot", "물류센터 마당", "parking", 58, 128, -56, -12),
        };

        // Interaction points in the town. kind: door (to another level), shelter, notice, npc, place.
        public static readonly TownTable<TownPlace> Places = Table(p => p.Id,
            new TownPlace("home", 78, 28.5, "103동 출입구", "door", "apartment"),
            new TownPlace("store", 36, 10.6, "바로편의점", "door", "store"),
            new TownPlace("mart", -16, -22.8, "연무마트 입구", "door", "mart"),
            new TownPlace("office", 87, -20.8, "한결물류 연무센터", "door", "office"),
            new TownPlace("center", -89, -26.8, "행정복지센터", "place"),
            new TownPlace("water", -80, -17, "비상급수시설", "place"),
            new TownPlace("cafe", -118, -14.8, "카페 쉼표", "place"),
            new TownPlace("assembly", -60, -36, "임시 집결지", "place"),
            new TownPlace("shelterEast", -68.6, 31, "동쪽 출입구", "shelter", "shelter"),
            new TownPlace("shelterA", -86, 13.4, "출입구 A · 계단", "shelter", "shelter"),
            new TownPlace("shelterB", -80, 50, "출입구 B · 지하주차장 진출입로", "shelter", "shelter"),
            new TownPlace("shelterC", -104, 13.4, "출입구 C · 경사로", "shelter", "shelter"),
            new TownPlace("shelterNotice", -94, 12, "현장 안내판", "notice"),
            new TownPlace("aptNotice", 72, 28.5, "단지 게시판", "notice"),
            new TownPlace("centerNotice", -96, -20, "행정복지센터 게시판", "notice"));

        // Spawn points in the town (yaw 0 faces north / -z).
        public static readonly TownTable<TownSpawn> Spawns = Table(s => s.Id,
            new TownSpawn("home", 78, 26, 0), new TownSpawn("store", 36, 8.6, 0), new TownSpawn("mart", -16, -21, Math.PI),
            new TownSpawn("office", 87, -18.5, Math.PI), new TownSpawn("cafe", -118, -12.5, Math.PI), new TownSpawn("shelter", -86, 10.8, Math.PI));

        // Pedestrian network for the 2D map and route planning. Node ids are short.
        public static readonly TownTable<TownNode> Nodes = Table(n => n.Id,
            new TownNode("home", 78, 26, "103동"), new TownNode("gate", 57, 20, "단지 정문"),
            new TownNode("I1", -50, -60), new TownNode("I2", 50, -60), new TownNode("I3", -50, 0), new TownNode("I4", 50, 0), new TownNode("I5", -50, 60), new TownNode("I6", 50, 60),
            new TownNode("store", 36, 9, "편의점"), new TownNode("M", 0, 0, "연무로 횡단보도"), new TownNode("mart", -16, -10, "연무마트"),
            new TownNode("office", 87, -18, "한결물류"), new TownNode("yard", -66, -9, "행정복지센터 앞"), new TownNode("water", -80, -16, "비상급수시설"),
            new TownNode("assembly", -58, -36, "임시 집결지"), new TownNode("W", -104, 0, "서쪽 횡단보도"), new TownNode("cafe", -118, -12, "카페"),
            new TownNode("S1", -57, 31, "문화로"), new TownNode("east", -68.6, 31, "동쪽 출입구"),
            new TownNode("A", -86, 11, "A 계단"), new TownNode("C", -104, 11, "C 경사로"), new TownNode("B", -80, 52, "B 진출입로"),
            new TownNode("shelter", -90, 31, "대피소(지하)"));

        // [from, to, segment id, tags]. Segments are what closures switch off. Tags describe the way itself.
        public static readonly IReadOnlyList<TownEdge> Edges = new[]
        {
            Edge("home", "gate", "aptPath"), Edge("gate", "I4", "hanbitMid"), Edge("gate", "I6", "hanbitS"),
            Edge("I4", "store", "yeonmuE"), Edge("store", "M", "yeonmuC1"), Edge("M", "I3", "yeonmuC2"), Edge("M", "mart", "martLot"),
            Edge("I4", "office", "officePath"), Edge("I2", "I4", "hanbitN"), Edge("I1", "I2", "sijang"), Edge("I1", "assembly", "munhwaN2"),
            Edge("assembly", "I3", "munhwaN1"), Edge("I3", "yard", "yeonmuW1"), Edge("yard", "water", "yardPath"), Edge("yard", "W", "yeonmuW2"),
            Edge("W", "cafe", "cafePath"), Edge("W", "C", "crossW", Tag.Crossing), Edge("I3", "A", "yeonmuW3", Tag.Crossing), Edge("A", "C", "cultureN"),
            Edge("I3", "S1", "munhwaS1"), Edge("S1", "east", "cultureE"), Edge("S1", "I5", "munhwaS2"), Edge("I5", "B", "cultureS"),
            Edge("I5", "I6", "danji"),
            // Shelter entrances. Which ones are open is game state (see ShelterEntrances), not a closure.
            Edge("east", "shelter", "gateEast", Tag.Level), Edge("A", "shelter", "gateA", Tag.Stairs), Edge("B", "shelter", "gateB", Tag.Steep), Edge("C", "shelter", "gateC", Tag.Ramp),
        };

        public static readonly TownTable<TownShelterEntrance> ShelterEntrances = Table(e => e.Id,
            new TownShelterEntrance("east", "east", "shelterEast", "gateEast", "동쪽 출입구", Tag.Level),
            new TownShelterEntrance("stairs", "A", "shelterA", "gateA", "출입구 A · 계단", Tag.Stairs, "A 계단"),
            new TownShelterEntrance("ramp", "B", "shelterB", "gateB", "출입구 B · 지하주차장 진출입로", Tag.Steep, "B 진출입로"),
            new TownShelterEntrance("access", "C", "shelterC", "gateC", "출입구 C · 경사로", Tag.Ramp, "C 경사로"));

        // Road closures: which segments they cut, and where the 3D barricades stand (rects the player cannot cross).
        public static readonly TownTable<TownClosure> Closures = Table(c => c.Id,
            new TownClosure("yeonmuCenter", "연무로 중앙 구간 통제", new[] { "yeonmuC1", "yeonmuC2" }, new[] { new TownRect(27, 29, -10, 10), new TownRect(-45, -43, -10, 10) }),
            new TownClosure("danji", "단지길 통제", new[] { "danji" }, new[] { new TownRect(-1, 1, 52, 68) }),
            new TownClosure("sijang", "시장길 통제", new[] { "sijang" }, new[] { new TownRect(-1, 1, -68, -52) }),
            new TownClosure("munhwaSouth", "문화로 남쪽 통제", new[] { "munhwaS2" }, new[] { new TownRect(-58, -42, 44, 46) }),
            new TownClosure("munhwaNorth", "문화로 북쪽 통제", new[] { "munhwaN1" }, new[] { new TownRect(-58, -42, -20, -18) }));

        static TownTable<T> Table<T>(Func<T, string> id, params T[] rows) where T : class =>
            new TownTable<T>(rows.Select(r => new KeyValuePair<string, T>(id(r), r)));

        static TownEdge Edge(string from, string to, string segment, params string[] tags) =>
            new TownEdge(from, to, segment, tags, Dist(Nodes[from].Pos, Nodes[to].Pos));

        // ---------- lookups ----------

        public static TownRoad Road(string id) => Roads.FirstOrDefault(r => r.Id == id);
        public static TownBuilding Building(string id) => Buildings.FirstOrDefault(b => b.Id == id);
        public static TownArea Area(string id) => Areas.FirstOrDefault(a => a.Id == id);
        /// <summary>The edge with this segment id (segment ids are unique), or null.</summary>
        public static TownEdge EdgeOf(string segment) => Edges.FirstOrDefault(e => e.Segment == segment);

        // ---------- geometry ----------

        /// <summary>Straight-line metres between two ground points.</summary>
        public static double Dist(TownPoint a, TownPoint b) => Hypot(a.X - b.X, a.Z - b.Z);

        public static double Dist(double ax, double az, double bx, double bz) => Hypot(ax - bx, az - bz);

        // The original's Math.hypot, operation for operation: V8 divides by the larger side and adds the squares
        // with Kahan compensation. A plain Sqrt(x*x + z*z) differs in the last bit for about a third of the town's
        // distances; with the same bits, lengths and any tie between two ways come out exactly as in the original.
        static double Hypot(double x, double z)
        {
            if (double.IsInfinity(x) || double.IsInfinity(z)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(z)) return double.NaN;
            x = Math.Abs(x); z = Math.Abs(z);
            double max = Math.Max(x, z);
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            Add(x / max, ref sum, ref compensation);
            Add(z / max, ref sum, ref compensation);
            return Math.Sqrt(sum) * max;
        }

        static void Add(double n, ref double sum, ref double compensation)
        {
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
        }

        // ---------- the network ----------

        /// <summary>
        /// Segment ids that are switched off: those of the given closures (unknown ids are ignored) and of every
        /// shelter gate that is not open. <paramref name="gates"/> null means all four gates are open; when it is
        /// given, an entrance it does not mention counts as closed (as in the original graph()).
        /// </summary>
        public static HashSet<string> CutSegments(IEnumerable<string> closures = null, IReadOnlyDictionary<string, bool> gates = null)
        {
            var cut = new HashSet<string>();
            if (closures != null)
                foreach (var id in closures)
                {
                    var c = Closures[id];
                    if (c != null) foreach (var s in c.Segments) cut.Add(s);
                }
            if (gates != null)
                foreach (var e in ShelterEntrances.Values)
                    if (!(gates.TryGetValue(e.Id, out var open) && open)) cut.Add(e.Segment);
            return cut;
        }

        /// <summary>Edge list with lengths, skipping closed segments and closed shelter gates (see <see cref="CutSegments"/>).</summary>
        public static TownGraph Graph(IEnumerable<string> closures = null, IReadOnlyDictionary<string, bool> gates = null)
        {
            var cut = CutSegments(closures, gates);
            var adj = new Dictionary<string, List<TownLink>>();
            foreach (var id in Nodes.Keys) adj[id] = new List<TownLink>();
            foreach (var e in Edges)
            {
                if (cut.Contains(e.Segment)) continue;
                adj[e.From].Add(new TownLink(e.To, e.Segment, e.Tags, e.Length));
                adj[e.To].Add(new TownLink(e.From, e.Segment, e.Tags, e.Length));
            }
            return new TownGraph(Nodes.Keys.Select(id => new KeyValuePair<string, IReadOnlyList<TownLink>>(id, adj[id])));
        }
    }
}
