using System.Collections.Generic;
using System.IO;
using AiOn.Data;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace AiOn.Tests
{
    static class TestSupport
    {
        public const string FixtureDir = "Assets/_AiOn/Tests/EditMode/Fixtures";

        static ItemDatabase _db;

        public static ItemDatabase Db
        {
            get
            {
                if (_db == null) _db = AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/_AiOn/Data/ItemDatabase.asset");
                Assert.That(_db, Is.Not.Null, "ItemDatabase.asset missing: run AiOn/Import Item Catalog");
                return _db;
            }
        }

        public static GameRules Rules => new(Db);

        public static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

        /// <summary>An ordered cart / give / get list: Cart(("radio", 1), ("bars", 2)).</summary>
        public static List<KeyValuePair<string, int>> Cart(params (string id, int n)[] lines)
        {
            var l = new List<KeyValuePair<string, int>>();
            foreach (var (id, n) in lines) l.Add(new KeyValuePair<string, int>(id, n));
            return l;
        }

        /// <summary>Deep JSON equality, numbers compared by value (100 == 100.0), key order ignored.</summary>
        public static void AssertJson(object actual, string expectedJson, string message = null)
        {
            var a = actual as JToken ?? JToken.FromObject(actual);
            var e = JToken.Parse(expectedJson);
            Assert.That(JToken.DeepEquals(Normalize(a), Normalize(e)), Is.True,
                $"{message}\nexpected: {e.ToString(Newtonsoft.Json.Formatting.None)}\n  actual: {a.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        public static void AssertSameJson(JToken actual, JToken expected, string message = null) =>
            Assert.That(JToken.DeepEquals(Normalize(actual), Normalize(expected)), Is.True,
                $"{message}\nexpected: {expected.ToString(Newtonsoft.Json.Formatting.None)}\n  actual: {actual.ToString(Newtonsoft.Json.Formatting.None)}");

        /// <summary>Every number as a double, so integer and float tokens with the same value compare equal.</summary>
        static JToken Normalize(JToken t) => t switch
        {
            JObject o => new JObject(SortedProps(o)),
            JArray a => new JArray(System.Linq.Enumerable.Select(a, Normalize)),
            JValue v when v.Type is JTokenType.Integer or JTokenType.Float => new JValue((double)v),
            _ => t.DeepClone(),
        };

        static IEnumerable<JProperty> SortedProps(JObject o)
        {
            var props = new List<JProperty>(o.Properties());
            props.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            foreach (var p in props) yield return new JProperty(p.Name, Normalize(p.Value));
        }
    }
}
