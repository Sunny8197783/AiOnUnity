using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Data/Source/facts-ext.json (facts added after the port) and its merge after the original facts.json. The original
    /// stays as FactsTests pins it (95 facts, 54 sources); the extension only appends.
    /// </summary>
    public class FactsExtTests
    {
        const string BaseFile = "../../../Data/Source/facts.json";
        const string ExtFile = "../../../Data/Source/facts-ext.json";

        static Facts Base => Facts.Load(Fixture(BaseFile));
        static Facts Ext => Facts.Load(Fixture(ExtFile));
        static Facts Merged => Facts.Load(Fixture(BaseFile), Fixture(ExtFile));

        [Test]
        public void Merge_keeps_the_original_first_in_order_and_appends_the_extension()
        {
            Facts b = Base, e = Ext, m = Merged;
            Assert.AreEqual(b.All.Count + e.All.Count, m.All.Count);
            Assert.AreEqual(b.Sources.Count + e.Sources.Count, m.Sources.Count);
            CollectionAssert.AreEqual(b.All.Select(f => f.Id), m.All.Take(b.All.Count).Select(f => f.Id));
            CollectionAssert.AreEqual(b.All.Select(f => f.Text), m.All.Take(b.All.Count).Select(f => f.Text));
            CollectionAssert.AreEqual(e.All.Select(f => f.Id), m.All.Skip(b.All.Count).Select(f => f.Id));
            Assert.AreEqual(b.All.Count, m.ExtensionFrom);
            Assert.IsEmpty(m.Skipped);
            Assert.IsTrue(m.IsExtension("cpr_depth_rate"));
            Assert.IsFalse(m.IsExtension("bag_days"));
            Assert.IsFalse(b.IsExtension("bag_days"), "an unmerged Facts has no extension");
        }

        [Test]
        public void Merged_lookup_finds_original_and_extension_facts_with_their_sources()
        {
            var m = Merged;
            Assert.AreEqual(Base.Text("bag_days"), m.Text("bag_days"));
            Assert.That(m.Text("cpr_depth_rate"), Does.Contain("약 5cm").And.Contain("분당 100~120회"));
            var cite = m.Cite("report_object");
            Assert.That(cite.Text, Does.Contain("1338"));
            Assert.That(cite.Source, Does.Contain("행정안전부"));
            Assert.That(cite.Url, Does.StartWith("https://"));
        }

        [Test]
        public void Extension_ids_already_in_the_original_are_dropped_and_the_original_wording_wins()
        {
            const string ext = "{\"facts\":[{\"id\":\"bag_days\",\"topic\":\"bag\",\"sourceId\":\"x\",\"text\":\"바뀐 문장\"},"
                + "{\"id\":\"new_one\",\"topic\":\"info\",\"sourceId\":\"ps_emergency\",\"text\":\"새 문장\"}],"
                + "\"sources\":[{\"id\":\"ps_emergency\",\"publisher\":\"p\",\"title\":\"t\",\"url\":\"u\",\"checkedAt\":\"2026-11-20\"}]}";
            var m = Facts.Load(Fixture(BaseFile), ext);
            Assert.AreEqual(Base.Text("bag_days"), m.Text("bag_days"));
            Assert.AreEqual("새 문장", m.Text("new_one"));
            Assert.AreEqual(Base.Source("ps_emergency").Title, m.Source("ps_emergency").Title);
            CollectionAssert.AreEquivalent(new[] { "fact:bag_days", "source:ps_emergency" }, m.Skipped);
            Assert.AreEqual(Base.All.Count + 1, m.All.Count);
        }

        [Test]
        public void No_extension_gives_the_original_unchanged()
        {
            var b = Base;
            foreach (var ext in new[] { null, "", "  " })
            {
                var m = Facts.Load(Fixture(BaseFile), ext);
                Assert.AreEqual(b.All.Count, m.All.Count);
                Assert.AreEqual(b.Sources.Count, m.Sources.Count);
            }
            Assert.AreSame(b, Facts.Merge(b, Facts.Empty));
            Assert.Catch<System.ArgumentException>(() => Facts.Load(Fixture(BaseFile), "not json"), "a broken extension is a wiring mistake");
        }

        [Test]
        public void Every_extension_fact_has_a_new_id_a_known_source_and_a_dated_https_source()
        {
            Facts b = Base, e = Ext, m = Merged;
            Assert.IsNotEmpty(e.All);
            Assert.AreEqual(e.All.Count, e.All.Select(f => f.Id).Distinct().Count(), "ids are unique");
            Assert.AreEqual(e.Sources.Count, e.Sources.Select(s => s.Id).Distinct().Count());
            foreach (var f in e.All)
            {
                Assert.IsNull(b.Fact(f.Id), f.Id + " must not reuse an original id");
                Assert.IsFalse(string.IsNullOrWhiteSpace(f.Text), f.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(f.Topic), f.Id);
                Assert.IsNotNull(m.Source(f.SourceId), f.Id + " -> " + f.SourceId);
            }
            foreach (var s in e.Sources)
            {
                Assert.IsNull(b.Source(s.Id), s.Id + " must not reuse an original source id");
                Assert.That(s.Url, Does.StartWith("https://"), s.Id);
                Assert.That(s.CheckedAt, Does.Match(@"^\d{4}-\d{2}-\d{2}$"), s.Id + " needs 확인일 yyyy-MM-dd");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Publisher), s.Id);
                Assert.IsTrue(e.All.Any(f => f.SourceId == s.Id), s.Id + " is cited by some fact");
            }
        }

        [Test]
        public void Extension_text_avoids_the_unverified_wording()
        {
            // The brief's 사실 확인 표: no "2분 교대", no 1337 / 111 / 113, no extinguisher discharge seconds.
            foreach (var f in Ext.All)
            {
                Assert.That(f.Text, Does.Not.Contain("2분"), f.Id);
                Assert.IsFalse(Regex.IsMatch(f.Text, @"(?<!\d)(1337|111|113)(?!\d)"), f.Id + ": unverified number");
                if (f.Id.StartsWith("cpr")) Assert.That(f.Text, Does.Not.Contain("교대하").And.Not.Contain("분마다"), f.Id);
            }
            Assert.That(Ext.Text("cpr_fatigue"), Does.Contain("지치기 전에 교대를 요청"));
        }

        [Test]
        public void Extension_wording_keeps_the_sourced_caveats()
        {
            var e = Ext;
            // A lay rescuer who cannot give rescue breaths keeps compressing (국가건강정보포털): 30:2 is never unconditional.
            Assert.That(e.Text("cpr_ratio"), Does.Contain("30:2").And.Contain("가슴압박만 계속"));
            // The action line stays short for cards and HUDs; the research reason is its own line, in plain words.
            Assert.That(e.Text("cpr_fatigue").Length, Is.LessThanOrEqualTo(30));
            Assert.That(e.Text("cpr_fatigue_why"), Does.Not.Contain("중앙값"));
            // 소방청's guide names 대피공간, 경량칸막이, 하향식피난구 and no other refuge; no sizes.
            foreach (var id in new[] { "fire_own_home", "fire_own_trapped", "fire_seal" })
                Assert.That(e.Text(id), Does.Not.Contain("욕실").And.Not.Match(@"\d+\s*(cm|m|㎝)"), id);
            // 경계경보 has no siren, 공습경보 a 1-minute one (행정안전부 민방위 제도 운영).
            Assert.That(e.Text("alert_levels"), Does.Contain("사이렌 없이").And.Contain("1분"));
            // Foreign or academic material is marked as supporting, like the original's "(미국, 보조 자료)".
            Assert.That(e.Source("ext_ochoa1998").Publisher, Does.Contain("보조 자료"));
        }

        [Test]
        public void The_session_dictionary_and_the_story_host_read_the_merged_facts()
        {
            var dict = new Dictionary<string, string>();
            GameSession.LoadFacts(Fixture(BaseFile), Fixture(ExtFile), dict);
            Assert.AreEqual(Merged.All.Count, dict.Count);
            Assert.AreEqual(Base.Text("fire_stay"), dict["fire_stay"]);
            Assert.IsTrue(dict.ContainsKey("fire_seal"));

            var only = new Dictionary<string, string>();
            GameSession.LoadFacts(Fixture(BaseFile), only);
            Assert.AreEqual(Base.All.Count, only.Count, "the one-argument form stays the original alone");

            var host = FakeStoryHost.LoadFacts();
            Assert.IsNotNull(host.Text("cpr_ratio"), "story tests see what the game sees");
            Assert.AreEqual(Base.All.Count, host.ExtensionFrom);
        }

        [Test]
        public void The_extension_file_is_shaped_like_facts_json()
        {
            var o = JObject.Parse(Fixture(ExtFile));
            Assert.IsInstanceOf<JArray>(o["facts"]);
            Assert.IsInstanceOf<JArray>(o["sources"]);
            foreach (var f in (JArray)o["facts"])
                CollectionAssert.AreEquivalent(new[] { "id", "topic", "sourceId", "text" }, ((JObject)f).Properties().Select(p => p.Name));
            foreach (var s in (JArray)o["sources"])
                CollectionAssert.AreEquivalent(new[] { "id", "publisher", "title", "url", "checkedAt" }, ((JObject)s).Properties().Select(p => p.Name));
        }
    }
}
