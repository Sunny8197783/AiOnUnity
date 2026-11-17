using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>An RGBA colour (0..1) the map shapes carry; the screen turns it into a UnityEngine.Color.</summary>
    public readonly struct Rgba
    {
        public readonly float R, G, B, A;
        public Rgba(float r, float g, float b, float a = 1) { R = r; G = g; B = b; A = a; }

        /// <summary>'#rrggbb' (optionally with an alpha 0..1 given separately).</summary>
        public static Rgba Hex(string hex, float a = 1)
        {
            var h = hex.TrimStart('#');
            int v = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Rgba(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
        }

        /// <summary>CSS rgba(r,g,b,a) with 0..255 channels.</summary>
        public static Rgba Of(int r, int g, int b, float a = 1) => new Rgba(r / 255f, g / 255f, b / 255f, a);

        public Rgba WithAlpha(float a) => new Rgba(R, G, B, a);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "rgba({0:0.###},{1:0.###},{2:0.###},{3:0.###})", R, G, B, A);
    }

    public enum MapShapeKind
    {
        /// <summary>Axis-aligned box: X, Y = top-left, W, H.</summary>
        Rect,
        /// <summary>Straight stroke from (X, Y) to (X2, Y2), Width thick.</summary>
        Line,
        /// <summary>Filled disc centred on (X, Y), radius R.</summary>
        Circle,
        /// <summary>Square rotated 45 degrees centred on (X, Y), R = centre to corner.</summary>
        Diamond,
        /// <summary>Upward triangle filling a W x H box centred on (X, Y) (the north arrow).</summary>
        Triangle,
        /// <summary>Text centred on (X, Y), Size px, Bold when Weight >= 700, Outline colour drawn round it.</summary>
        Text,
    }

    /// <summary>One primitive of the drawn map, in map pixels (origin top-left, y down).</summary>
    public sealed class MapShape
    {
        public MapShapeKind Kind;
        public float X, Y, X2, Y2, W, H, R, Width;
        public Rgba Color;
        public string Text;
        public float Size;
        public int Weight;
        public Rgba Outline;

        public override string ToString() => Kind == MapShapeKind.Text ? $"Text '{Text}' @({X:0.#},{Y:0.#})" : $"{Kind} @({X:0.#},{Y:0.#})";
    }

    /// <summary>World (x east, z south) -> map pixels, the original projector() without the turn (rot = 0).</summary>
    public readonly struct MapProjector
    {
        public readonly double Cx, Cz, S, W, H;
        public MapProjector(double cx, double cz, double s, double w, double h) { Cx = cx; Cz = cz; S = s; W = w; H = h; }

        public (float x, float y) P(double x, double z) => ((float)(W / 2 + (x - Cx) * S), (float)(H / 2 + (z - Cz) * S));

        /// <summary>The whole town (TownMap.Bounds) fitted into a w x h box.</summary>
        public static MapProjector Fit(double w, double h)
        {
            var b = TownMap.Bounds;
            double s = Math.Min(w / (b.X1 - b.X0), h / (b.Z1 - b.Z0));
            return new MapProjector((b.X0 + b.X1) / 2, (b.Z0 + b.Z1) / 2, s, w, h);
        }
    }

    /// <summary>What routemap() was asked to show (its option object, parsed).</summary>
    public sealed class RoutemapRequest
    {
        public string Mode = "guided", From = "home", To = "shelter";
        public List<RouteOption> Options = new();
        /// <summary>Closures shown: closures + known.closures, once each, only ids TownMap knows.</summary>
        public List<string> Shown = new();
        /// <summary>Raw gate values by entrance id ('open' | 'closed' | true | false | anything = unknown).</summary>
        public JObject Gates = new();
        public TownPoint? Player;
        public TownPoint? Dest;
        /// <summary>mode 'view' or no options: just the map, nothing to choose.</summary>
        public bool View;
    }

    /// <summary>
    /// The pure part of the route map screen (original ui/routemap.js): option parsing, the texts beside the map, the
    /// keyboard choice and the result, and drawMap() turned into a list of shapes (rects, strokes, discs, labels).
    /// </summary>
    public static class RoutemapLogic
    {
        // ---------- constants of the original ----------

        /// <summary>[colour, dash pattern (map px), dash name] per route index: told apart by colour AND dash.</summary>
        public static readonly IReadOnlyList<(string color, float[] dash, string name)> RouteStyle = new[]
        {
            ("#f2b33d", new float[0], "실선"), ("#3cc6b5", new float[] { 12, 7 }, "긴 점선"), ("#c49bff", new float[] { 3, 6 }, "짧은 점선"),
        };

        public static readonly IReadOnlyDictionary<string, string> Kind = new Dictionary<string, string>
        {
            ["shortest"] = "가장 짧은 길", ["detour"] = "돌아가는 길", ["stepFree"] = "계단 없는 길",
        };

        static readonly Dictionary<string, string> BuildingColor = new()
        {
            ["apartment"] = "#58656e", ["shop"] = "#6e5d4c", ["mart"] = "#715e45", ["office"] = "#4d5d75", ["civic"] = "#4f6d68", ["villa"] = "#5f5852", ["warehouse"] = "#4c5156",
        };
        static readonly Dictionary<string, string> AreaColor = new() { ["park"] = "#24462f", ["parking"] = "#283034", ["plaza"] = "#2c3336", ["complex"] = "#1f2b26" };

        /// <summary>Gate state -> (icon, word, colour).</summary>
        public static readonly IReadOnlyDictionary<string, (string icon, string text, string color)> Gate = new Dictionary<string, (string, string, string)>
        {
            ["open"] = ("✓", "열림", "#3cc6b5"), ["closed"] = ("✕", "닫힘", "#e5484d"), ["unknown"] = ("?", "확인 안 함", "#8b9599"),
        };

        public static readonly IReadOnlyDictionary<string, string> ModeText = new Dictionary<string, string>
        {
            ["guided"] = "안내 있음", ["hint"] = "힌트 가능", ["free"] = "혼자 판단", ["view"] = "보기",
        };

        public const string Legend = "● 나   ◆ 대피소   ✓ 열림   ✕ 닫힘   ? 확인 안 함   ⊘ 통제";
        public const string NoClosures = "알고 있는 도로 통제가 없다. (현장은 다를 수 있다)";
        public static readonly IReadOnlyList<string> HelpLines = new[]
        {
            "선을 비교해 보세요. 걸리는 시간, 계단이나 가파른 길, 그리고 지나는 출입구.",
            "첫 번째 길이 막히면 어느 길로 돌아갈지도 함께 봐 두세요.",
        };

        public static string Eyebrow(bool view) => view ? "지도" : "경로 계획";
        public static string Heading(bool view) => view ? "연무동 지도" : "집에서 대피소까지";
        public static string Keys(bool view) => view ? "Esc 또는 M으로 닫기" : "↑/↓ 경로 바꾸기 · Enter 정하기 · Esc 닫기";
        public static string Primary(bool view) => view ? "지도 닫기" : "이 길로 정한다";
        public const string RoutesLegend = "갈 길 고르기";
        public const string GatesHead = "대피소 출입구";
        public const string ClosuresHead = "알고 있는 통제";

        // ---------- parsing ----------

        /// <summary>gateState(): 'open' or true -> open, 'closed' or false -> closed, anything else -> unknown.</summary>
        public static string GateState(JToken v)
        {
            if (v == null) return "unknown";
            if (v.Type == JTokenType.Boolean) return (bool)v ? "open" : "closed";
            if (v.Type == JTokenType.String) { var s = (string)v; return s == "open" ? "open" : s == "closed" ? "closed" : "unknown"; }
            return "unknown";
        }

        /// <summary>A route option object (routeOptions() output, as JSON) -> RouteOption.</summary>
        public static RouteOption ToOption(JToken t)
        {
            var o = t as JObject ?? new JObject();
            List<string> Strings(string k) => (o[k] as JArray)?.Select(x => x.Type == JTokenType.Null ? null : (string)x).ToList() ?? new List<string>();
            double Num(string k) => o[k] != null && (o[k].Type == JTokenType.Integer || o[k].Type == JTokenType.Float) ? (double)o[k] : 0;
            return new RouteOption
            {
                Nodes = Strings("nodes"), Segments = Strings("segments"), Tags = Strings("tags"), Length = Num("length"),
                Kind = o["kind"]?.Type == JTokenType.String ? (string)o["kind"] : null, Seconds = Num("seconds"),
                Entrance = o["entrance"]?.Type == JTokenType.String ? (string)o["entrance"] : null,
            };
        }

        static TownPoint? Point(JToken t)
        {
            if (t is not JObject o) return null;
            var x = o["x"]; var z = o["z"];
            if (x == null || z == null || (x.Type != JTokenType.Integer && x.Type != JTokenType.Float) || (z.Type != JTokenType.Integer && z.Type != JTokenType.Float)) return null;
            return new TownPoint((double)x, (double)z);
        }

        // A destructuring default applies only when the key is missing (undefined); an explicit null or a non-string
        // value is kept by the original (mode: null -> no mode chip, no help; from: null -> NODES[null] = no player).
        static string Str(JObject o, string k, string def)
        {
            var t = o?[k];
            if (t == null || t.Type == JTokenType.Undefined) return def;
            return t.Type == JTokenType.String ? (string)t : null;
        }

        /// <summary>routemap({ mode, from, to, options, closures, gates, known, playerAt }) with the original defaults.</summary>
        public static RoutemapRequest Parse(JObject o)
        {
            o ??= new JObject();
            var r = new RoutemapRequest { Mode = Str(o, "mode", "guided"), From = Str(o, "from", "home"), To = Str(o, "to", "shelter") };
            if (o["options"] is JArray opts) r.Options = opts.Select(ToOption).ToList();
            IEnumerable<string> Ids(JToken t) => (t as JArray)?.Where(x => x.Type == JTokenType.String).Select(x => (string)x) ?? Enumerable.Empty<string>();
            r.Shown = Ids(o["closures"]).Concat(Ids((o["known"] as JObject)?["closures"])).Distinct().Where(id => TownMap.Closures.ContainsKey(id)).ToList();
            r.Gates = o["gates"] as JObject ?? new JObject();
            r.View = r.Mode == "view" || r.Options.Count == 0;
            var from = TownMap.Nodes[r.From];
            r.Player = Point(o["playerAt"]) ?? (from != null ? from.Pos : (TownPoint?)null);
            var to = TownMap.Nodes[r.To];
            r.Dest = to != null ? to.Pos : (TownPoint?)null;
            return r;
        }

        // ---------- choosing ----------

        /// <summary>Starting selection: none when viewing, else the first route.</summary>
        public static int InitialSelection(bool view) => view ? -1 : 0;

        /// <summary>↑/↓ in the radio group: moves and wraps, over the routes listed (at most 3).</summary>
        public static int Step(int sel, int delta, int optionCount)
        {
            int n = Math.Min(3, optionCount);
            if (n <= 0) return -1;
            return ((sel + delta) % n + n) % n;
        }

        /// <summary>The resolved value: { choice: index | null }.</summary>
        public static JObject Result(bool view, int sel) => new JObject { ["choice"] = view ? JValue.CreateNull() : new JValue(sel) };

        // ---------- side panel texts ----------

        public static string RouteTitle(int i, RouteOption o) => $"경로 {i + 1} · {(o?.Kind != null && Kind.TryGetValue(o.Kind, out var k) ? k : "다른 길")}";
        public static string RouteSpec(int i) => $" ({RouteStyle[Math.Min(i, RouteStyle.Count - 1)].name})";
        public static string RouteExplain(RouteOption o) => Routes.Explain(o);

        /// <summary>'A 계단로 들어감', or null when the route does not end at a known entrance.</summary>
        public static string EntranceBadge(RouteOption o)
        {
            var e = o?.Entrance != null ? TownMap.ShelterEntrances[o.Entrance] : null;
            return e == null ? null : $"{e.Short ?? e.Label}로 들어감";
        }

        /// <summary>One line per shelter entrance: (state, icon, '동쪽 출입구 · 열림').</summary>
        public static List<(string state, string icon, string text)> GateLines(JObject gates)
        {
            var list = new List<(string, string, string)>();
            foreach (var kv in TownMap.ShelterEntrances)
            {
                var st = GateState(gates?[kv.Key]);
                list.Add((st, Gate[st].icon, $"{kv.Value.Label} · {Gate[st].text}"));
            }
            return list;
        }

        /// <summary>Closure labels shown, or empty (the screen then shows <see cref="NoClosures"/>).</summary>
        public static List<string> ClosureLines(IEnumerable<string> shown) => shown.Select(id => TownMap.Closures[id].Label).ToList();

        // ---------- the drawing (drawMap with rot 0) ----------

        static MapShape Box(double x0, double z0, double x1, double z1, MapProjector p, Rgba c)
        {
            var (ax, ay) = p.P(x0, z0); var (bx, by) = p.P(x1, z1);
            return new MapShape { Kind = MapShapeKind.Rect, X = Math.Min(ax, bx), Y = Math.Min(ay, by), W = Math.Abs(bx - ax), H = Math.Abs(by - ay), Color = c };
        }
        static MapShape Box(TownRect r, double pad, MapProjector p, Rgba c) => Box(r.X0 - pad, r.Z0 - pad, r.X1 + pad, r.Z1 + pad, p, c);
        static MapShape Disc(float x, float y, float r, Rgba c) => new MapShape { Kind = MapShapeKind.Circle, X = x, Y = y, R = r, Color = c };
        static MapShape Line(float x1, float y1, float x2, float y2, float w, Rgba c) => new MapShape { Kind = MapShapeKind.Line, X = x1, Y = y1, X2 = x2, Y2 = y2, Width = w, Color = c };

        static readonly Rgba LabelStroke = Rgba.Of(10, 14, 16, 0.85f);
        static MapShape Label(string text, float x, float y, float size, string color = "#e9eef0", int weight = 600) =>
            new MapShape { Kind = MapShapeKind.Text, Text = text, X = x, Y = y, Size = size, Weight = weight, Color = Rgba.Hex(color), Outline = LabelStroke };

        /// <summary>
        /// A polyline stroked with a canvas dash pattern (empty = solid): the dash carries on across corners like
        /// setLineDash does. Solid lines add a disc at each inner corner for the round join.
        /// </summary>
        public static List<MapShape> Stroke(IReadOnlyList<(float x, float y)> pts, float[] dash, float width, Rgba color, bool roundJoins = false)
        {
            var list = new List<MapShape>();
            if (pts == null || pts.Count < 2) return list;
            bool solid = dash == null || dash.Length == 0 || dash.Sum() <= 0;
            if (solid)
            {
                for (int i = 1; i < pts.Count; i++) list.Add(Line(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y, width, color));
                if (roundJoins) for (int i = 0; i < pts.Count; i++) list.Add(Disc(pts[i].x, pts[i].y, width / 2, color));
                return list;
            }
            // A pattern with an odd count repeats twice over, as in canvas.
            var pat = dash.Length % 2 == 1 ? dash.Concat(dash).ToArray() : dash;
            int k = 0; float left = pat[0]; bool on = true;
            for (int i = 1; i < pts.Count; i++)
            {
                float ax = pts[i - 1].x, ay = pts[i - 1].y, bx = pts[i].x, by = pts[i].y;
                float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (len <= 0) continue;
                float t = 0;
                while (t < len - 1e-4f)
                {
                    float step = Math.Min(left, len - t);
                    if (on && step > 0)
                        list.Add(Line(ax + (bx - ax) * t / len, ay + (by - ay) * t / len, ax + (bx - ax) * (t + step) / len, ay + (by - ay) * (t + step) / len, width, color));
                    t += step; left -= step;
                    if (left <= 1e-4f) { k = (k + 1) % pat.Length; left = pat[k]; on = k % 2 == 0; }
                }
            }
            return list;
        }

        /// <summary>
        /// The fixed part of the map: ground, areas (parking stripes, park trees), roads, centre lines, crosswalks,
        /// buildings, labels and the closures shown. <paramref name="px"/> = the original's px() (map px per CSS px).
        /// </summary>
        public static List<MapShape> BaseLayer(MapProjector p, IReadOnlyList<string> closures, float px, bool labels = true)
        {
            double s = p.S;
            var g = new List<MapShape>
            {
                new MapShape { Kind = MapShapeKind.Rect, X = 0, Y = 0, W = (float)p.W, H = (float)p.H, Color = Rgba.Hex("#172023") },
            };
            foreach (var a in TownMap.Areas)
            {
                g.Add(Box(a.Rect, 0, p, Rgba.Hex(AreaColor.TryGetValue(a.Kind, out var c) ? c : "#222b2e")));
                if (a.Kind == "parking")
                    for (double x = a.Rect.X0 + 2.5; x < a.Rect.X1; x += 2.5)
                    {
                        var (ax, ay) = p.P(x, a.Rect.Z0 + 1); var (bx, by) = p.P(x, a.Rect.Z0 + 5);
                        g.Add(Line(ax, ay, bx, by, px, Rgba.Of(230, 230, 220, 0.18f)));
                    }
                if (a.Kind == "park")
                    for (int i = 0; i < 46; i++) // tree crowns, deterministic
                    {
                        double tx = a.Rect.X0 + 3 + ((i * 37) % 97) / 97.0 * (a.Rect.X1 - a.Rect.X0 - 6), tz = a.Rect.Z0 + 3 + ((i * 61) % 89) / 89.0 * (a.Rect.Z1 - a.Rect.Z0 - 6);
                        var (cx, cz) = p.P(tx, tz);
                        g.Add(Disc(cx, cz, (float)Math.Max(1.5 * px, 1.6 * s), i % 5 != 0 ? Rgba.Of(70, 120, 72, 0.75f) : Rgba.Of(176, 130, 48, 0.7f)));
                    }
            }
            foreach (var r in TownMap.Roads) g.Add(Box(r.Rect, TownMap.Sidewalk, p, Rgba.Hex("#4b5251")));
            foreach (var r in TownMap.Roads) g.Add(Box(r.Rect, 0, p, Rgba.Hex("#30383c")));
            foreach (var r in TownMap.Roads)
            {
                bool ew = r.EastWest; double mid = ew ? (r.Rect.Z0 + r.Rect.Z1) / 2 : (r.Rect.X0 + r.Rect.X1) / 2;
                void CentreLine(double off, Rgba color, float[] dash)
                {
                    var a = ew ? p.P(r.Rect.X0, mid + off) : p.P(mid + off, r.Rect.Z0);
                    var b = ew ? p.P(r.Rect.X1, mid + off) : p.P(mid + off, r.Rect.Z1);
                    g.AddRange(Stroke(new[] { a, b }, dash.Select(d => (float)(d * s)).ToArray(), (float)Math.Max(0.8 * px, 0.22 * s), color));
                }
                if (r.Id == "yeonmu") { CentreLine(-0.3, Rgba.Hex("#d6ae38"), new float[0]); CentreLine(0.3, Rgba.Hex("#d6ae38"), new float[0]); }
                else CentreLine(0, Rgba.Of(230, 230, 225, 0.5f), new float[] { 3, 3 });
            }
            foreach (var cw in TownMap.Crosswalks)
            {
                var r = TownMap.Road(cw.Road); if (r == null) continue;
                bool ew = r.EastWest; double k0 = ew ? r.Rect.Z0 : r.Rect.X0, k1 = ew ? r.Rect.Z1 : r.Rect.X1;
                for (double k = k0 + 0.4; k < k1 - 0.4; k += 1.2)
                    g.Add(ew ? Box(cw.At - 2, k, cw.At + 2, k + 0.6, p, Rgba.Of(236, 236, 230, 0.78f)) : Box(k, cw.At - 2, k + 0.6, cw.At + 2, p, Rgba.Of(236, 236, 230, 0.78f)));
            }
            foreach (var b in TownMap.Buildings)
            {
                var r = b.Rect;
                g.Add(Box(r.X0 + 1.2, r.Z0 + 1.2, r.X1 + 1.2, r.Z1 + 1.2, p, new Rgba(0, 0, 0, 0.35f)));
                bool culture = b.Id == "culture";
                var edge = Box(r, 0, p, culture ? Rgba.Hex("#3cc6b5") : new Rgba(0, 0, 0, 0.45f));
                float lw = (culture ? 2.5f : 1f) * px;
                // stroke straddles the edge: outer box in the stroke colour, then the fill inset by half the width
                g.Add(new MapShape { Kind = MapShapeKind.Rect, X = edge.X - lw / 2, Y = edge.Y - lw / 2, W = edge.W + lw, H = edge.H + lw, Color = edge.Color });
                g.Add(new MapShape { Kind = MapShapeKind.Rect, X = edge.X + lw / 2, Y = edge.Y + lw / 2, W = Math.Max(0, edge.W - lw), H = Math.Max(0, edge.H - lw), Color = Rgba.Hex(BuildingColor.TryGetValue(b.Kind, out var bc) ? bc : "#5a5a5a") });
            }
            if (labels)
            {
                foreach (var r in TownMap.Roads)
                {
                    double lx = r.Id == "yeonmu" ? 30 : (r.Rect.X0 + r.Rect.X1) / 2;
                    double lz = r.Id == "yeonmu" ? 0 : r.Id == "munhwa" || r.Id == "hanbit" ? -86 : (r.Rect.Z0 + r.Rect.Z1) / 2;
                    var (x, y) = p.P(lx, lz); g.Add(Label(r.Name, x, y, 10 * px, "#aeb8bb", 500));
                }
                foreach (var b in TownMap.Buildings)
                    if (!string.IsNullOrEmpty(b.Name))
                    {
                        var (x, y) = p.P((b.Rect.X0 + b.Rect.X1) / 2, (b.Rect.Z0 + b.Rect.Z1) / 2);
                        g.Add(Label(b.Name.Replace("한빛아파트 ", ""), x, y - (b.Id == "culture" ? 8 * px : 0), 11 * px));
                    }
                { var (x, y) = p.P(-90, 31); g.Add(Label("대피소(지하)", x, y + 8 * px, 11 * px, "#7fe0d3", 700)); }
                foreach (var a in TownMap.Areas)
                    if (a.Kind == "park") { var (x, y) = p.P((a.Rect.X0 + a.Rect.X1) / 2, (a.Rect.Z0 + a.Rect.Z1) / 2); g.Add(Label(a.Name, x, y, 11 * px, "#bfe3c2")); }
            }
            foreach (var id in closures ?? Array.Empty<string>())
            {
                var cl = TownMap.Closures[id]; if (cl == null) continue;
                for (int i = 0; i < cl.Barricades.Count; i++)
                {
                    var r = cl.Barricades[i];
                    var box = Box(r, 0, p, Rgba.Hex("#e5484d"));
                    g.Add(box);
                    var corners = new[] { (box.X, box.Y), (box.X + box.W, box.Y), (box.X + box.W, box.Y + box.H), (box.X, box.Y + box.H), (box.X, box.Y) };
                    g.AddRange(Stroke(corners, new[] { 4 * px, 4 * px }, 2 * px, new Rgba(1, 1, 1)));
                    var (x, y) = p.P((r.X0 + r.X1) / 2, (r.Z0 + r.Z1) / 2);
                    g.Add(Disc(x, y, 9 * px, Rgba.Hex("#e5484d")));
                    g.Add(new MapShape { Kind = MapShapeKind.Rect, X = x - 5 * px, Y = y - 1.6f * px, W = 10 * px, H = 3.2f * px, Color = new Rgba(1, 1, 1) });
                    if (labels && i == 0) g.Add(Label($"통제 · {cl.Label}", x, y - 16 * px, 10 * px, "#ffb3b5", 700));
                }
            }
            return g;
        }

        /// <summary>Order the routes are drawn in: the selected one last (on top).</summary>
        public static List<int> DrawOrder(int count, int sel) => Enumerable.Range(0, count).OrderBy(i => i == sel ? 1 : 0).ToList();

        /// <summary>The routes (dark casing, coloured dashed line, numbered disc when there is more than one), entrances, destination, player, north arrow and the 50 m bar.</summary>
        public static List<MapShape> TopLayer(MapProjector p, IReadOnlyList<RouteOption> options, int sel, JObject gates, TownPoint? player, TownPoint? dest, float px, bool labels = true)
        {
            var g = new List<MapShape>();
            options ??= Array.Empty<RouteOption>();
            foreach (int i in DrawOrder(options.Count, sel))
            {
                var o = options[i];
                var style = i < RouteStyle.Count ? RouteStyle[i] : RouteStyle[0];
                var pts = (o.Nodes ?? new List<string>()).Select(n => TownMap.Nodes[n]).Where(n => n != null).Select(n => p.P(n.X, n.Z)).ToList();
                if (pts.Count < 2) continue;
                g.AddRange(Stroke(pts, null, (i == sel ? 11 : 8) * px, Rgba.Of(8, 12, 14, 0.8f), roundJoins: true));
                float alpha = sel < 0 || i == sel ? 1 : 0.55f;
                g.AddRange(Stroke(pts, style.dash.Select(d => d * px).ToArray(), (i == sel ? 6 : 4) * px, Rgba.Hex(style.color, alpha), roundJoins: style.dash.Length == 0));
                if (labels && options.Count > 1)
                {
                    var (x, y) = pts[pts.Count / 2];
                    g.Add(Disc(x, y, 10 * px, Rgba.Hex(style.color)));
                    g.Add(Label((i + 1).ToString(CultureInfo.InvariantCulture), x, y, 12 * px, "#10161a", 800));
                }
            }
            foreach (var kv in TownMap.ShelterEntrances)
            {
                var e = kv.Value; var pl = TownMap.Places[e.Place]; if (pl == null) continue;
                var (x, y) = p.P(pl.X, pl.Z);
                var st = GateState(gates?[kv.Key]); var (icon, _, col) = Gate[st];
                float r = 8 * px, lw = 2 * px;
                if (st == "closed")
                {
                    g.Add(new MapShape { Kind = MapShapeKind.Rect, X = x - r - lw / 2, Y = y - r - lw / 2, W = 2 * r + lw, H = 2 * r + lw, Color = Rgba.Hex("#0c1214") });
                    g.Add(new MapShape { Kind = MapShapeKind.Rect, X = x - r + lw / 2, Y = y - r + lw / 2, W = 2 * r - lw, H = 2 * r - lw, Color = Rgba.Hex(col) });
                }
                else { g.Add(Disc(x, y, r + lw / 2, Rgba.Hex("#0c1214"))); g.Add(Disc(x, y, r - lw / 2, Rgba.Hex(col))); }
                g.Add(Label(icon, x, y + 0.5f * px, 11 * px, "#0c1214", 900));
                if (labels) g.Add(Label(e.Short ?? "동쪽 출입구", x, y + 17 * px, 10 * px, "#d8f3ef"));
            }
            if (dest is TownPoint d) { var (x, y) = p.P(d.X, d.Z); g.Add(new MapShape { Kind = MapShapeKind.Diamond, X = x, Y = y, R = 9 * px, Color = Rgba.Hex("#3cc6b5") }); }
            if (player is TownPoint pp)
            {
                var (x, y) = p.P(pp.X, pp.Z);
                g.Add(Disc(x, y, 16 * px, Rgba.Of(242, 179, 61, 0.25f)));
                g.Add(Disc(x, y, 8 * px, Rgba.Hex("#10161a")));
                g.Add(Disc(x, y, 6 * px, Rgba.Hex("#f2b33d")));
                if (labels) g.Add(Label("나", x, y - 20 * px, 12 * px, "#ffd58a", 800));
            }
            if (labels)
            {
                float nx = (float)p.W - 28 * px, ny = 30 * px;
                g.Add(new MapShape { Kind = MapShapeKind.Triangle, X = nx, Y = ny - 4 * px, R = 10 * px, W = 14 * px, H = 20 * px, Color = Rgba.Hex("#e9eef0") });
                g.Add(Label("북", nx, ny + 18 * px, 11 * px));
                float sx = 18 * px, sy = (float)p.H - 18 * px, bar = (float)(50 * p.S);
                g.Add(new MapShape { Kind = MapShapeKind.Rect, X = sx, Y = sy, W = bar, H = 3 * px, Color = Rgba.Hex("#e9eef0") });
                g.Add(Label("50m", sx + bar / 2, sy - 10 * px, 10 * px));
            }
            return g;
        }
    }
}
