using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A way through the town network (the original shortestPath result).</summary>
    public class RoutePath
    {
        /// <summary>Node ids from start to goal (just the one node when start = goal).</summary>
        public List<string> Nodes = new();
        /// <summary>Metres, rounded to a whole number.</summary>
        public double Length;
        /// <summary>Segment ids walked, in order: one fewer than <see cref="Nodes"/>.</summary>
        public List<string> Segments = new();
        /// <summary>Tags of the ways used, each once, in the order met walking back from the goal.</summary>
        public List<string> Tags = new();

        /// <summary>The original's object, key for key (for events, saves and the parity tests).</summary>
        public virtual JObject ToJObject() => new JObject
        {
            ["nodes"] = new JArray(Nodes), ["length"] = Length, ["segments"] = new JArray(Segments), ["tags"] = new JArray(Tags),
        };
    }

    /// <summary>One choice on the route screen: a path, what kind it is, how long it takes and where it enters the shelter.</summary>
    public sealed class RouteOption : RoutePath
    {
        /// <summary>shortest | detour | stepFree</summary>
        public string Kind;
        /// <summary>Walking time for this mobility and load, rounded to a whole second.</summary>
        public double Seconds;
        /// <summary>The shelter entrance it goes in by (east | stairs | ramp | access), or null when it does not end in the shelter.</summary>
        public string Entrance;

        public override JObject ToJObject()
        {
            var o = base.ToJObject();
            o["kind"] = Kind;
            o["seconds"] = Seconds;
            o["entrance"] = Entrance != null ? new JValue(Entrance) : JValue.CreateNull();
            return o;
        }
    }

    /// <summary>Route planning over the <see cref="TownMap"/> pedestrian graph (original game/routes.js). Pure logic.</summary>
    public static class Routes
    {
        /// <summary>Walking speed in m/s per mobility: on foot, or with a rollator walker.</summary>
        public static readonly IReadOnlyDictionary<string, double> Speed = new Dictionary<string, double> { ["walk"] = 1.3, ["walker"] = 0.9 };
        /// <summary>Impassable with a rollator walker.</summary>
        public static readonly IReadOnlyList<string> StepTags = new[] { TownMap.Tag.Stairs, TownMap.Tag.Steep };
        /// <summary>Carrying more than this slows walking to 80 %.</summary>
        public const double HeavyKg = 8;

        static readonly string[] None = Array.Empty<string>();

        /// <summary>
        /// Dijkstra. <paramref name="adj"/> is <see cref="TownMap.Graph"/> output. <paramref name="tagCost"/> multiplies an
        /// edge's length per tag (Infinity = never use); edges with one of <paramref name="avoidTags"/> are not used at all.
        /// Null when a node is unknown or there is no way.
        /// </summary>
        public static RoutePath ShortestPath(TownGraph adj, string from, string to,
            IEnumerable<string> avoidTags = null, IReadOnlyDictionary<string, double> tagCost = null)
        {
            if (adj == null || adj[from] == null || adj[to] == null) return null;
            var avoid = avoidTags == null ? None : avoidTags as ICollection<string> ?? avoidTags.ToList();

            // 'reached' keeps the order nodes were first reached in (the original walks its cost object's keys in
            // that order): among equally near nodes the one reached first is settled first, so ties break the same way.
            var reached = new List<string> { from };
            var cost = new Dictionary<string, double> { [from] = 0 };
            var prev = new Dictionary<string, KeyValuePair<string, TownLink>>();
            var done = new HashSet<string>();
            for (;;)
            {
                string u = null;
                foreach (var n in reached)
                    if (!done.Contains(n) && (u == null || cost[n] < cost[u])) u = n;
                if (u == null) return null;
                if (u == to) break;
                done.Add(u);
                foreach (var e in adj[u])
                {
                    if (done.Contains(e.To) || e.Tags.Any(t => avoid.Contains(t))) continue;
                    double m = 1;
                    foreach (var t in e.Tags) m *= tagCost != null && tagCost.TryGetValue(t, out var k) ? k : 1;
                    double c = cost[u] + e.Len * m;
                    if (double.IsNaN(c) || double.IsInfinity(c)) continue;
                    if (cost.TryGetValue(e.To, out var old)) { if (c >= old) continue; }
                    else reached.Add(e.To);
                    cost[e.To] = c;
                    prev[e.To] = new KeyValuePair<string, TownLink>(u, e);
                }
            }

            var r = new RoutePath();
            r.Nodes.Add(to);
            double length = 0;
            for (var n = to; n != from;)
            {
                var step = prev[n];
                r.Nodes.Insert(0, step.Key);
                r.Segments.Insert(0, step.Value.Seg);
                foreach (var t in step.Value.Tags) if (!r.Tags.Contains(t)) r.Tags.Add(t);
                length += step.Value.Len;
                n = step.Key;
            }
            r.Length = Num.JsRound(length);
            return r;
        }

        /// <summary>The route's segments, longest first.</summary>
        static List<string> BySize(RoutePath r) => r.Segments
            .Select((seg, i) => new KeyValuePair<string, double>(seg, TownMap.Dist(TownMap.Nodes[r.Nodes[i]].Pos, TownMap.Nodes[r.Nodes[i + 1]].Pos)))
            .OrderByDescending(x => x.Value) // stable, like the original Array.sort
            .Select(x => x.Key).ToList();

        /// <summary>
        /// A gate value as the original reads it: booleans or the town level's 'open' | 'closed'.
        /// Only false and 'closed' close a gate; null or anything else (not checked yet) counts as open.
        /// </summary>
        public static bool GateOpen(JToken v) =>
            v == null || !((v.Type == JTokenType.Boolean && !(bool)v) || (v.Type == JTokenType.String && (string)v == "closed"));

        /// <summary>Gate states as JSON (booleans or 'open' | 'closed') -> the open flags <see cref="RouteOptions"/> takes.</summary>
        public static Dictionary<string, bool> Gates(IEnumerable<KeyValuePair<string, JToken>> states)
        {
            var open = new Dictionary<string, bool>();
            if (states != null) foreach (var kv in states) open[kv.Key] = GateOpen(kv.Value);
            return open;
        }

        /// <summary>
        /// Up to 3 distinct options: the shortest, a detour around the shortest's longest segment, and a
        /// step-free one (walk) or a second detour (walker). Each adds Kind, Seconds and Entrance.
        /// </summary>
        /// <param name="rules">Only used to weigh the bag when <paramref name="carryingKg"/> is not given; may be null otherwise.</param>
        /// <param name="g">The game, for the bag's weight; null = carrying nothing.</param>
        /// <param name="closures">Closure ids (<see cref="TownMap.Closures"/>) in force.</param>
        /// <param name="gates">Shelter entrance id -> open. A gate not mentioned is open (see <see cref="Gates"/> for 'open' | 'closed').</param>
        /// <param name="mobility">'walk' or 'walker' (never uses stairs or steep ways).</param>
        /// <param name="carryingKg">Load in kg; over <see cref="HeavyKg"/> walking is 20 % slower. Null = the bag's weight.</param>
        public static List<RouteOption> RouteOptions(GameRules rules, GameState g, string from, string to,
            IEnumerable<string> closures = null, IReadOnlyDictionary<string, bool> gates = null, string mobility = "walk", double? carryingKg = null)
        {
            if (mobility == null || !Speed.TryGetValue(mobility, out var pace)) throw new ArgumentException($"unknown mobility: {mobility}");
            var open = new Dictionary<string, bool>();
            foreach (var k in TownMap.ShelterEntrances.Keys) open[k] = gates == null || !gates.TryGetValue(k, out var isOpen) || isOpen;
            var adj = TownMap.Graph(closures, open);
            double kg = carryingKg ?? (g != null ? (rules ?? throw new ArgumentNullException(nameof(rules))).BagLoad(g).WeightKg : 0);
            double speed = pace * (kg > HeavyKg ? 0.8 : 1);
            IReadOnlyList<string> baseAvoid = mobility == "walker" ? StepTags : None;
            RoutePath Find(IReadOnlyCollection<string> cut, IEnumerable<string> avoidTags) => ShortestPath(adj.Without(cut), from, to, avoidTags);

            var opts = new List<RouteOption>();
            bool Add(RoutePath r, string kind)
            {
                if (r == null || opts.Any(o => o.Segments.SequenceEqual(r.Segments))) return false;
                var entrance = TownMap.ShelterEntrances.Values.FirstOrDefault(e => r.Segments.Contains(e.Segment))?.Id;
                opts.Add(new RouteOption
                {
                    Nodes = r.Nodes, Length = r.Length, Segments = r.Segments, Tags = r.Tags,
                    Kind = kind, Seconds = Num.JsRound(r.Length / speed), Entrance = entrance,
                });
                return true;
            }

            if (!Add(Find(None, baseAvoid), "shortest")) return opts;
            foreach (var seg in BySize(opts[0])) if (Add(Find(new[] { seg }, baseAvoid), "detour")) break;
            if (mobility == "walk") Add(Find(None, StepTags), "stepFree");
            if (opts.Count < 3 && opts.Count > 1)
                Add(Find(new[] { BySize(opts[0]).FirstOrDefault(), BySize(opts[1]).FirstOrDefault() }, baseAvoid), "detour");
            return opts.Take(3).ToList();
        }

        static readonly Dictionary<string, string> TagNote = new()
        {
            [TownMap.Tag.Stairs] = "계단", [TownMap.Tag.Steep] = "가파른 진출입로", [TownMap.Tag.Ramp] = "경사로", [TownMap.Tag.Crossing] = "횡단보도",
        };

        /// <summary>
        /// '103동 → 단지 정문 → … → 대피소(지하) · 410m, 약 5분 · 계단·횡단보도 있음'.
        /// A plain <see cref="RoutePath"/> (no time of its own) is timed at walking speed.
        /// </summary>
        public static string Explain(RoutePath option)
        {
            if (option?.Nodes == null || option.Nodes.Count == 0) return "갈 수 있는 길이 없습니다.";
            var names = option.Nodes.Select(n => TownMap.Nodes[n]?.Label).Where(l => !string.IsNullOrEmpty(l)).ToList();
            var via = string.Join(" → ", names.Where((n, i) => i == 0 || n != names[i - 1]));
            double secs = option is RouteOption o ? o.Seconds : option.Length / Speed["walk"];
            var notes = (option.Tags ?? new List<string>()).Select(t => TagNote.TryGetValue(t, out var note) ? note : null).Where(s => s != null).ToList();
            string minutes = Js(Math.Max(1, Num.JsRound(secs / 60)));
            string tail = notes.Count > 0 ? " · " + string.Join("·", notes) + " 있음" : "";
            return $"{via} · {Js(option.Length)}m, 약 {minutes}분{tail}";
        }

        /// <summary>A number as the original prints it in a template string (410, not 410.0).</summary>
        static string Js(double x) => x.ToString("R", CultureInfo.InvariantCulture);
    }
}
