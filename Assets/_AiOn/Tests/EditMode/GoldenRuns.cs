using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Shared by the deeds / fates / endings / challenges / debrief tests: the games of Fixtures/golden-runs.json
    /// (built with the ORIGINAL JavaScript by Tools/golden/make-golden-runs.mjs), the verified facts, the run
    /// builder of the original tests/deed-fixtures.mjs, and JSON comparison against what the original answered.
    /// </summary>
    static class GoldenRuns
    {
        /// <summary>Reads a fixture as JSON; strings stay strings (no date parsing).</summary>
        public static JObject ParseFixture(string name)
        {
            using var reader = new JsonTextReader(new StringReader(Fixture(name))) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
            return (JObject)JToken.ReadFrom(reader);
        }

        // ---------- the runs ----------

        static Dictionary<string, JObject> _saves;
        static List<string> _names;

        static Dictionary<string, JObject> Saves
        {
            get
            {
                if (_saves != null) return _saves;
                _names = new List<string>();
                var saves = new Dictionary<string, JObject>();
                foreach (var r in (JArray)ParseFixture("golden-runs.json")["runs"])
                {
                    _names.Add((string)r["name"]);
                    saves[(string)r["name"]] = (JObject)r["save"];
                }
                return _saves = saves;
            }
        }

        public static IReadOnlyList<string> Names { get { _ = Saves; return _names; } }

        /// <summary>A fresh copy of the named game, loaded from the original's save.</summary>
        public static GameState Load(string name)
        {
            Assert.That(Saves.ContainsKey(name), $"golden-runs.json has no run '{name}'");
            return SaveCodec.Load(Saves[name].ToString(Formatting.None), Db);
        }

        // ---------- facts ----------

        static Dictionary<string, string> _text, _source;
        static HashSet<string> _sourceIds;

        static void LoadFacts()
        {
            if (_text != null) return;
            var o = ParseFixture("../../../Data/Source/facts.json");
            _source = new Dictionary<string, string>();
            _sourceIds = new HashSet<string>(((JArray)o["sources"]).Select(s => (string)s["id"]));
            var text = new Dictionary<string, string>();
            foreach (var f in (JArray)o["facts"]) { text[(string)f["id"]] = (string)f["text"]; _source[(string)f["id"]] = (string)f["sourceId"]; }
            _text = text;
        }

        /// <summary>Fact id -> text, or null (the rules' <c>fact</c> parameter).</summary>
        public static string Fact(string id) { LoadFacts(); return id != null && _text.TryGetValue(id, out var t) ? t : null; }

        /// <summary>Fact id -> source id, or null (the rules' <c>factSource</c> parameter).</summary>
        public static string FactSource(string id) { LoadFacts(); return id != null && _source.TryGetValue(id, out var s) ? s : null; }

        public static bool IsSource(string id) { LoadFacts(); return _sourceIds.Contains(id); }

        // ---------- comparing with the original ----------

        /// <summary>A result as JSON under its original names. Members that are null are left out: C# has no 'undefined'.</summary>
        public static JToken Js(object value) => StripNulls(value == null ? JValue.CreateNull() : value as JToken ?? JToken.FromObject(value));

        public static JToken StripNulls(JToken t) => t switch
        {
            JObject o => new JObject(o.Properties().Where(p => !IsNull(p.Value)).Select(p => new JProperty(p.Name, StripNulls(p.Value)))),
            JArray a => new JArray(a.Select(StripNulls)),
            null => JValue.CreateNull(),
            _ => t.DeepClone(),
        };

        /// <summary>JSON null, however it was made (a JValue from a null string has type String and no value).</summary>
        static bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined || (t is JValue v && v.Value == null);

        /// <summary>Deep equality with what the original answered (numbers by value, key order ignored, null members = missing).</summary>
        public static void AssertGolden(object actual, JToken expected, string message = null) =>
            AssertSameJson(Js(actual), StripNulls(expected), message);

        // ---------- tests/deed-fixtures.mjs ----------

        /// <summary>The hand-built events that satisfy an event-derived deed: (type, details, chapter or null).</summary>
        public static List<(string type, JObject details, string chapter)> Raw(string id)
        {
            var list = new List<(string, JObject, string)>();
            // The 'raw:<id>' run holds exactly the original RAW[id] events; a chapter other than the first means RAW named one.
            foreach (var e in (JArray)Saves[$"raw:{id}"]["events"])
                list.Add(((string)e["type"], (JObject)e["details"].DeepClone(), (string)e["chapter"] == "prologue" ? null : (string)e["chapter"]));
            return list;
        }

        public static IEnumerable<string> RawIds => Names.Where(n => n.StartsWith("raw:", StringComparison.Ordinal)).Select(n => n.Substring(4));

        /// <summary>An extra event for <see cref="Run"/>: its type and details as (lenient) JSON.</summary>
        public static (string type, string details) Ev(string type, string details = "{}") => (type, details);

        /// <summary>A game that reached <paramref name="path"/> after doing <paramref name="ids"/> (event deeds from RAW, new-choice deeds via Remember).</summary>
        public static GameState Run(string path, IEnumerable<string> ids = null, IEnumerable<(string type, string details)> extra = null, long seed = 3)
        {
            var g = Rules.CreateGame(seed);
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                var def = Deeds.Get(id);
                Assert.IsNotNull(def, $"unknown deed {id}");
                g.Chapter = def.Ch;
                if (def.Src == "new") Deeds.Remember(g, id, id == "rumor_corrected" ? new JObject { ["after"] = "check" } : new JObject());
                else if (id == "signals_clear") for (int i = 0; i < 4; i++) GameRules.RecordEvent(g, "signal_sorted", new JObject { ["id"] = $"s{i}", ["ok"] = true });
                else foreach (var (type, d, ch) in Raw(id)) { if (ch != null) g.Chapter = ch; GameRules.RecordEvent(g, type, d); }
            }
            foreach (var (type, d) in extra ?? Enumerable.Empty<(string, string)>()) GameRules.RecordEvent(g, type, JObject.Parse(d));
            if (path != null)
            {
                g.Chapter = "evac"; g.Day = 12; g.Minute = 13 * 60;
                GameRules.RecordEvent(g, "path", new JObject { ["path"] = path });
                g.SetKnow("path", path);
            }
            return g;
        }

        public static readonly string[] Commons = { "manager_contact", "manager_detour", "rounds_with_manager", "elder_met", "elder_alert_knock", "board_offer", "mart_share" };

        /// <summary>tests/endings.test.mjs FIX: the minimal run that reaches each ending, in ending order.</summary>
        public static readonly (string id, string path, string[] ids, (string type, string details)[] extra)[] Fix =
        {
            ("bus", "bus", null, null), ("walk", "walk", null, null), ("seoyeon", "seoyeon", null, null),
            ("missed", "missed", null, null), ("stay", "stay", null, null), ("volunteer", "volunteer", null, null),
            ("commons", "stay", Commons, null),
            ("reopen", "missed", new[] { "clerk_contact", "promise_store", "store_paid", "store_shuttered" }, null),
            ("relief", "volunteer", new[] { "driver_text", "yoon_reply", "sort_supplies" }, null),
            ("board", "stay", new[] { "signals_clear", "rumor_checked", "handover_precise", "card_meet", "blackout_radio" }, null),
            ("shut", "stay", new[] { "hoard" }, new[] { ("violence", "{how:'punch',ko:true}") }),
        };

        public static GameState Build(string endingId, long seed = 3)
        {
            var f = Fix.First(x => x.id == endingId);
            return Run(f.path, f.ids, f.extra, seed);
        }

        /// <summary>
        /// The original drills.js recordEncounter with any result (a string or an eval object): one more rung of the
        /// tool's ladder and an 'encounter' event.
        /// </summary>
        public static JObject Encounter(GameState g, string tool, JToken result)
        {
            var stage = BagDrill.LadderStage(g, tool);
            var r = new JObject { ["day"] = g.Day, ["minute"] = g.Minute, ["stage"] = stage, ["result"] = result?.DeepClone() };
            g.Skill[tool].Add(r);
            GameRules.RecordEvent(g, "encounter", new JObject { ["tool"] = tool, ["result"] = result?.DeepClone(), ["stage"] = stage });
            return r;
        }

        public static bool HasHangul(string s) => s != null && s.Any(c => c >= '가' && c <= '힣');
        public static bool HasLatin(string s) => s != null && s.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    }
}
