using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// The parts of a level that show or hide with its states (recorded from the original level builders: road closures
    /// and barricades, gates, the water point, buses, strike damage, candles, curtains, the hall fire, window views...).
    /// Apply(key, value) sets every object that has rules for that key: a rule for the exact value wins; list values
    /// (closures, ways) show an object when any element's rule shows it; object values (gates, stock) apply per field.
    /// </summary>
    public class LevelStateVariants : MonoBehaviour
    {
        [System.Serializable]
        public class Rule
        {
            public string key;
            [Tooltip("The state value as JSON (\"on\", [\"danji\"], {\"east\":\"closed\"}).")]
            public string value;
            public bool visible;
        }

        [System.Serializable]
        public class Variant
        {
            public GameObject target;
            public bool defaultVisible;
            public List<Rule> rules = new();
        }

        public List<Variant> variants = new();

        void Awake()
        {
            foreach (var v in variants) if (v.target) v.target.SetActive(v.defaultVisible);
        }

        public bool Handles(string key) => variants.Any(v => v.rules.Any(r => r.key == key));

        static string Json(JToken t) => t == null ? "null" : t.ToString(Formatting.None);

        public void Apply(string key, JToken value)
        {
            foreach (var v in variants)
            {
                if (!v.target) continue;
                var rules = v.rules.Where(r => r.key == key).ToList();
                if (rules.Count == 0) continue;
                v.target.SetActive(Visible(v, rules, value));
            }
        }

        static bool Visible(Variant v, List<Rule> rules, JToken value)
        {
            string exact = Json(value);
            var hit = rules.FirstOrDefault(r => r.value == exact);
            if (hit != null) return hit.visible;
            if (value is JArray arr) // a list state: each element on its own
            {
                bool? any = null;
                foreach (var e in arr)
                {
                    var r = rules.FirstOrDefault(x => x.value == Json(new JArray(e)));
                    if (r == null) continue;
                    if (r.visible) return true;
                    any = false;
                }
                return any ?? v.defaultVisible;
            }
            if (value is JObject obj) // an object state: field by field
            {
                foreach (var p in obj.Properties())
                {
                    var r = rules.FirstOrDefault(x => x.value == Json(new JObject { [p.Name] = p.Value }));
                    if (r != null) return r.visible;
                }
            }
            return v.defaultVisible;
        }
    }
}
