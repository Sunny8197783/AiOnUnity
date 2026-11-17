using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Facts.cs and Data/Source/facts.json against the original game/facts.js (Fixtures/golden-facts.json, regenerate
    /// with Tools/golden/make-golden-facts.mjs), plus the facts case of the original tests/debrief.test.mjs.
    /// </summary>
    public class FactsTests
    {
        const string DataFile = "../../../Data/Source/facts.json"; // from the fixture folder: Assets/_AiOn/Data/Source

        static Facts _facts;
        static Facts F => _facts ??= Facts.Load(Fixture(DataFile));
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-facts.json"));

        [Test]
        public void The_data_file_has_every_field_of_every_original_fact_and_source()
        {
            var data = JObject.Parse(Fixture(DataFile));
            foreach (var list in new[] { "facts", "sources" })
            {
                var have = (JArray)data[list];
                var want = (JArray)Golden[list];
                Assert.AreEqual(want.Count, have.Count, list);
                for (int i = 0; i < want.Count; i++) AssertSameJson(have[i], want[i], $"{list}[{i}] {want[i]["id"]}");
            }
            Assert.IsNotNull(data["sources"][0]["checkedAt"], "sources carry the date they were checked");
        }

        [Test]
        public void Facts_and_sources_load_like_the_original_in_the_same_order()
        {
            var facts = (JArray)Golden["facts"];
            var sources = (JArray)Golden["sources"];
            Assert.AreEqual(95, facts.Count);
            Assert.AreEqual(54, sources.Count);
            Assert.AreEqual(facts.Count, F.All.Count);
            Assert.AreEqual(sources.Count, F.Sources.Count);
            for (int i = 0; i < facts.Count; i++)
            {
                AssertSameJson(JObject.FromObject(F.All[i]), facts[i], $"fact {facts[i]["id"]}");
                Assert.AreSame(F.All[i], F.Fact((string)facts[i]["id"]));
            }
            for (int i = 0; i < sources.Count; i++)
            {
                AssertSameJson(JObject.FromObject(F.Sources[i]), sources[i], $"source {sources[i]["id"]}");
                Assert.AreSame(F.Sources[i], F.Source((string)sources[i]["id"]));
            }
            CollectionAssert.AreEqual(Golden["topics"].Select(t => (string)t), F.All.Select(f => f.Topic).Distinct());
        }

        [Test]
        public void Cite_gives_the_text_the_source_line_and_the_link()
        {
            var cites = (JArray)Golden["cites"];
            Assert.AreEqual(F.All.Count, cites.Count);
            foreach (var c in cites) AssertSameJson(JObject.FromObject(F.Cite((string)c["id"])), c["cite"], (string)c["id"]);
            var one = F.Cite("bag_days");
            Assert.AreEqual(F.Fact("bag_days").Text, one.Text);
            Assert.AreEqual($"{F.Source("ps_emergency").Publisher} · {F.Source("ps_emergency").Title}", one.Source);
            Assert.That(one.Url, Does.StartWith("https://"));
        }

        [Test]
        public void An_unknown_id_is_null_not_an_error()
        {
            Assert.AreEqual(JTokenType.Null, Golden["unknown"]["fact"].Type);
            Assert.IsNull(F.Fact("nope"));
            Assert.IsNull(F.Source("nope"));
            Assert.IsNull(F.Cite("nope"));
            Assert.IsNull(F.Fact(null));
            Assert.IsNull(F.Source(null));
            Assert.IsNull(F.Cite(null));
            Assert.IsNull(F.Text("nope"));
            Assert.IsNull(F.SourceId("nope"));
        }

        // tests/debrief.test.mjs: 'every FACTS sourceId exists; SOURCES are unique https links'
        [Test]
        public void Every_fact_has_a_source_and_sources_are_unique_https_links()
        {
            var srcIds = new HashSet<string>(F.Sources.Select(s => s.Id));
            Assert.AreEqual(F.Sources.Count, srcIds.Count);
            Assert.AreEqual(F.All.Count, F.All.Select(f => f.Id).Distinct().Count());
            foreach (var f in F.All)
            {
                Assert.That(srcIds.Contains(f.SourceId), $"{f.Id} -> {f.SourceId}");
                Assert.That(f.Text.Trim(), Is.Not.Empty, f.Id);
            }
            foreach (var s in F.Sources)
            {
                Assert.AreEqual("https", new Uri(s.Url).Scheme, s.Id);
                Assert.That(s.CheckedAt, Does.Match(@"^\d{4}-\d{2}-\d{2}$"), s.Id);
            }
        }

        [Test]
        public void Text_is_the_lookup_the_rules_take()
        {
            Func<string, string> fact = F.Text;
            Assert.AreEqual(F.Fact("fire_big").Text, fact("fire_big"));
            Assert.IsNull(fact("nope"));
            Assert.IsNull(fact(null));
            Assert.AreEqual("ps_fire", F.SourceId("fire_big"));
            foreach (var c in Db.items) foreach (var id in c.facts) Assert.IsNotNull(fact(id), $"{c.id} cites {id}");
        }

        [Test]
        public void Load_rejects_what_is_not_the_facts_file()
        {
            Assert.Catch<ArgumentException>(() => Facts.Load(null));
            Assert.Catch<ArgumentException>(() => Facts.Load("  "));
            Assert.Catch<ArgumentException>(() => Facts.Load("not json"));
            Assert.Catch<ArgumentException>(() => Facts.Load("[]"));
            Assert.Catch<ArgumentException>(() => Facts.Load("{\"sources\":[]}"));

            // A file without sources still loads; a fact whose source is unknown cites nothing.
            var lone = Facts.Load("{\"facts\":[{\"id\":\"a\",\"topic\":\"t\",\"sourceId\":\"s\",\"text\":\"x\"},{\"id\":\"a\",\"topic\":\"t\",\"sourceId\":\"s\",\"text\":\"y\"}]}");
            AssertJson(lone.Cite("a"), "{text:'y',source:'',url:''}", "the later of two entries with one id wins, like the original Map");
            Assert.AreEqual(2, lone.All.Count);
            Assert.IsEmpty(lone.Sources);
            Assert.IsNull(Facts.Empty.Fact("bag_days"));
            Assert.IsEmpty(Facts.Empty.All);
        }
    }
}
