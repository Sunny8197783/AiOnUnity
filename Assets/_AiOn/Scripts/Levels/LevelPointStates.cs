using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// Interaction points that a level state enables, disables or relabels (recorded from the original level builders by
    /// the exporter, &lt;level&gt;-states.json "points"): the water point with waterPoint, a closure's guide with closures,
    /// the 304 door with hallFire / elder304, the window and radio labels... Apply(key, value) returns the points whose
    /// availability or label is now different, for the host to update its story points.
    /// Combination: each key's matching rule is kept; any active "disabled" wins, else any active "enabled", else the
    /// default; the label of the most recently applied key that sets one wins.
    /// </summary>
    public class LevelPointStates : MonoBehaviour
    {
        [System.Serializable]
        public class Rule
        {
            public string key;
            [Tooltip("The state value as JSON.")] public string value;
            public int enabled = -1; // -1 unchanged, 0 off, 1 on
            public string label;     // null/empty = unchanged
            public bool hasLabel;
        }

        [System.Serializable]
        public class PointRules
        {
            public string id;
            public bool defaultEnabled = true;
            public string defaultLabel = "";
            public List<Rule> rules = new();
        }

        public List<PointRules> points = new();

        // point id -> key -> the active rule for that key (null: none matches)
        readonly Dictionary<string, Dictionary<string, Rule>> _active = new();
        readonly List<string> _order = new();

        public bool Handles(string key) => points.Any(p => p.rules.Any(r => r.key == key));

        /// <summary>Applies a state; returns (id, enabled, label) for every point that has rules for this key.</summary>
        public List<(string Id, bool Enabled, string Label)> Apply(string key, JToken value)
        {
            _order.Remove(key); _order.Add(key);
            var changed = new List<(string, bool, string)>();
            foreach (var p in points)
            {
                var mine = p.rules.Where(r => r.key == key).ToList();
                if (mine.Count == 0) continue;
                if (!_active.TryGetValue(p.id, out var act)) _active[p.id] = act = new Dictionary<string, Rule>();
                act[key] = Select(mine, value);
                var (en, label) = Resolve(p, act, _order);
                changed.Add((p.id, en, label));
            }
            return changed;
        }

        /// <summary>The rule matching a value: exact JSON first, then an array element ([e]), then an object field ({f: v}).</summary>
        public static Rule Select(IReadOnlyList<Rule> rules, JToken value)
        {
            string exact = Json(value);
            var hit = rules.FirstOrDefault(r => r.value == exact);
            if (hit != null) return hit;
            if (value is JArray arr)
                foreach (var e in arr)
                {
                    var r = rules.FirstOrDefault(x => x.value == Json(new JArray(e)));
                    if (r != null) return r;
                }
            if (value is JObject obj)
                foreach (var f in obj.Properties())
                {
                    var r = rules.FirstOrDefault(x => x.value == Json(new JObject { [f.Name] = f.Value }));
                    if (r != null) return r;
                }
            return null;
        }

        public static (bool Enabled, string Label) Resolve(PointRules p, IReadOnlyDictionary<string, Rule> active, IReadOnlyList<string> order)
        {
            bool anyOff = false, anyOn = false;
            foreach (var r in active.Values) { if (r == null) continue; if (r.enabled == 0) anyOff = true; else if (r.enabled == 1) anyOn = true; }
            bool en = anyOff ? false : anyOn ? true : p.defaultEnabled;
            string label = p.defaultLabel;
            foreach (var k in order)
                if (active.TryGetValue(k, out var r) && r != null && r.hasLabel) label = r.label;
            return (en, label);
        }

        static string Json(JToken t) => t == null ? "null" : t.ToString(Formatting.None);
    }
}
