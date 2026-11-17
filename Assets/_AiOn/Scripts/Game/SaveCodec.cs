using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AiOn.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public class InvalidSaveException : Exception
    {
        public InvalidSaveException(string why) : base($"invalid save: {why}") { }
    }

    /// <summary>
    /// Save, load, checkpoint and restore (original game/state.js). The JSON is the original v2 format, so
    /// saves move between the web build and Unity. Loading migrates older v2 saves, then validates.
    /// </summary>
    public static class SaveCodec
    {
        static readonly JsonSerializerSettings Settings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            Formatting = Formatting.None,
        };

        static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Save(GameState g) => JsonConvert.SerializeObject(g, Settings);

        public static JObject ToJObject(GameState g) => JObject.FromObject(g, Serializer);

        /// <summary>Deep copy (the original structuredClone).</summary>
        public static GameState Clone(GameState g) => ToJObject(g).ToObject<GameState>(Serializer);

        public static GameState Load(string json, ItemDatabase db)
        {
            JToken token;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
                token = JToken.ReadFrom(reader);
                if (reader.Read()) throw new JsonReaderException("trailing content");
            }
            catch (JsonException) { throw new InvalidSaveException("not JSON"); }
            if (token is not JObject o) throw new InvalidSaveException("not a v2 game");
            Migrate(o);
            Validate(o, nested: false, db);
            return o.ToObject<GameState>(Serializer);
        }

        /// <summary>Accepts a live state too (validated through a copy).</summary>
        public static GameState Load(GameState g, ItemDatabase db) => Load(Save(g), db);

        /// <summary>
        /// Saves stay v2: fields added since the first v2 build (skill.info, ledger) are filled here, for the
        /// state and its checkpoint state. items.*.charge / pocket are optional.
        /// </summary>
        public static JObject Migrate(JObject g, bool nested = false)
        {
            if (g == null) return null;
            if (g["skill"] is JObject skill)
                foreach (var t in GameRules.Tools)
                    if (skill[t] == null || skill[t].Type == JTokenType.Null) skill[t] = new JArray();
            if (g["ledger"] == null || g["ledger"].Type == JTokenType.Null) g["ledger"] = new JArray();
            if (!nested && g["checkpoint"] is JObject cp && cp["state"] is JObject st) Migrate(st, true);
            return g;
        }

        // ---------- validation (mirrors the original validate()) ----------

        static void Fail(string why) => throw new InvalidSaveException(why);

        static bool IsNum(JToken t, double lo = double.NegativeInfinity, double hi = double.PositiveInfinity)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return false;
            double v = (double)t;
            return !double.IsNaN(v) && !double.IsInfinity(v) && v >= lo && v <= hi;
        }

        static bool IsInt(JToken t) => IsNum(t) && Math.Floor((double)t) == (double)t;

        static void Validate(JObject g, bool nested, ItemDatabase db)
        {
            if (g == null || !IsNum(g["v"]) || (double)g["v"] != GameState.Version) Fail("not a v2 game");
            if (!IsNum(g["seed"], 0) || g["chapter"]?.Type != JTokenType.String || !IsInt(g["day"]) || !IsInt(g["minute"]) || !IsNum(g["minute"], 0, 1439)) Fail("clock");

            if (g["slots"] is not JObject s || !IsInt(s["total"]) || !IsNum(s["used"], 0, (double)s["total"]) || s["done"] is not JArray) Fail("slots");
            if (!IsNum(g["money"], 0)) Fail("money");
            if (g["stats"] is not JObject stats || !new[] { "health", "thirst", "fatigue", "tension" }.All(k => IsNum(stats[k], 0, 100))) Fail("stats");
            if (g["phone"] is not JObject phone || !IsNum(phone["battery"], 0, 100)) Fail("phone");

            if (g["items"] is not JObject items) { Fail("items"); return; }
            foreach (var prop in items.Properties().ToList())
            {
                var id = prop.Name;
                var def = db.Get(id);
                if (def == null) { prop.Remove(); continue; } // removed from the catalogue since this save
                if (prop.Value is not JObject e
                    || !new[] { "home", "bag", "bought", "used" }.All(k => IsInt(e[k]) && (double)e[k] >= 0)
                    || e["checked"]?.Type != JTokenType.Boolean) Fail($"item {id}");
                var entry = (JObject)prop.Value;
                if (entry.ContainsKey("charge") && !IsNum(entry["charge"], 0, 100)) Fail($"item {id} charge");
                if (entry.ContainsKey("pocket"))
                {
                    var p = entry["pocket"];
                    if (!(IsInt(p) && (double)p >= 0 && (double)p <= (double)entry["bag"] && ((double)p == 0 || def.pocket))) Fail($"item {id} pocket");
                }
            }

            if (g["know"] is not JObject || g["cond"] is not JObject || g["rel"] is not JObject rel
                || !GameRules.People.All(p => rel[p] is JObject)) Fail("know/cond/rel");
            if (g["skill"] is not JObject skill || !GameRules.Tools.All(t => skill[t] is JArray)) Fail("skill");
            if (g["info"] is not JArray info || !info.All(i => i is JObject io && io["text"]?.Type == JTokenType.String
                    && io["label"]?.Type == JTokenType.String && Array.IndexOf(GameRules.Labels, (string)io["label"]) >= 0)) Fail("info");
            if (g["events"] is not JArray events || !events.All(e => e is JObject eo && IsInt(eo["seq"]) && eo["type"]?.Type == JTokenType.String)) Fail("events");
            var ledger = g["ledger"];
            if (ledger != null && !(ledger is JArray la && la.All(d => d is JObject dobj && IsInt(dobj["day"])))) Fail("ledger");

            var cp = g["checkpoint"];
            if (cp != null && cp.Type != JTokenType.Null)
            {
                if (nested || cp is not JObject cpo || cpo["key"]?.Type != JTokenType.String) { Fail("checkpoint"); return; }
                Validate(cpo["state"] as JObject, true, db);
            }
        }

        // ---------- checkpoint ----------

        /// <summary>Snapshots the state (without its own checkpoint) for a retry.</summary>
        public static Checkpoint MakeCheckpoint(GameState g, string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("checkpoint key must be a non-empty string");
            var snapshot = ToJObject(g);
            snapshot["checkpoint"] = JValue.CreateNull();
            g.Checkpoint = new Checkpoint { Key = key, State = snapshot.ToObject<GameState>(Serializer) };
            return g.Checkpoint;
        }

        /// <summary>Rolls the state back in place (callers keep their reference); the checkpoint stays for another retry.</summary>
        public static GameState Restore(GameState g)
        {
            var cp = g.Checkpoint ?? throw new InvalidOperationException("no checkpoint to restore");
            var copy = Clone(cp.State);
            foreach (var f in typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(g, f.GetValue(copy));
            g.Checkpoint = cp;
            return g;
        }
    }
}
