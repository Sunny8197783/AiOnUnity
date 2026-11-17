using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// The people and town tests of the original tests/endings.test.mjs (fates.js has no test file of its own), plus
    /// parity with the original game/fates.js (Fixtures/golden-fates.json over the games of golden-runs.json;
    /// regenerate with Tools/golden/make-golden-fates.mjs).
    /// </summary>
    public class FatesTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-fates.json");

        static PersonLine Line(GameState g, string who) =>
            Fates.PeopleLines(g, Endings.PickEnding(g, GoldenRuns.FactSource).Id).First(x => x.Who == who);

        [Test]
        public void Paths_either_leave_or_stay_in_town_and_an_unknown_path_is_the_bus()
        {
            CollectionAssert.AreEqual(new[] { "bus", "walk", "seoyeon", "missed", "stay", "volunteer" }, Fates.Paths);
            Assert.That(Fates.Paths.All(p => Fates.Leave.Contains(p) != Fates.Town.Contains(p)));
            var g = Rules.CreateGame(3);
            Assert.AreEqual("bus", Fates.PathOf(g), "no path yet");
            g.SetKnow("path", "teleport");
            Assert.AreEqual("bus", Fates.PathOf(g));
            g.SetKnow("path", 7);
            Assert.AreEqual("bus", Fates.PathOf(g));
            g.SetKnow("path", "stay");
            Assert.AreEqual("stay", Fates.PathOf(g));
        }

        [Test]
        public void People_four_neighbours_for_every_ending_then_strangers_and_lines_follow_what_was_done()
        {
            foreach (var f in GoldenRuns.Fix)
            {
                var p = Fates.PeopleLines(GoldenRuns.Build(f.id), f.id);
                CollectionAssert.AreEqual(new[] { "seoyeon", "manager", "clerk", "elder" }, p.Take(4).Select(x => x.Who).ToList(), f.id);
                Assert.That(p.Count <= 7 && p.Skip(4).All(x => x.Who == "stranger" && x.Name == "" && !x.Present), f.id);
                Assert.That(p.All(x => !string.IsNullOrEmpty(x.Line)), f.id);
                Assert.IsFalse(p[0].Present, f.id);
            }
            StringAssert.Contains("소식을 받지 못해", Line(GoldenRuns.Run("bus", new[] { "seoyeon_contact", "promise_note" }), "seoyeon").Line);
            StringAssert.DoesNotContain("소식을 받지 못해", Line(GoldenRuns.Run("bus", new[] { "seoyeon_contact", "promise_note" }, new[] { GoldenRuns.Ev("note_left") }), "seoyeon").Line);
            StringAssert.Contains("촛불을 꺼 두었다", Line(GoldenRuns.Run("bus", new[] { "manager_contact", "candle_left" }), "manager").Line);
            StringAssert.Contains("빈 칸", Line(GoldenRuns.Run("bus", new[] { "clerk_contact", "store_took" }), "clerk").Line);
            PersonLine stay = Line(GoldenRuns.Run("stay", new[] { "elder_alert_knock" }), "elder"), bus = Line(GoldenRuns.Run("bus", new[] { "elder_alert_knock" }), "elder");
            StringAssert.Contains("302호 몫", stay.Line); Assert.IsTrue(stay.Present);
            StringAssert.Contains("안부", bus.Line); Assert.IsFalse(bus.Present);
            Assert.IsFalse(Line(GoldenRuns.Run("stay"), "elder").Present, "never met: not on the roam");
            Assert.IsTrue(Line(GoldenRuns.Build("reopen"), "clerk").Present);
            var many = Fates.PeopleLines(GoldenRuns.Run("bus", new[] { "driver_text", "looter_gave", "mart_share", "shelter_water_given", "rumor_corrected" }), "bus");
            Assert.AreEqual(7, many.Count);
            var rumor = GoldenRuns.Run("stay", new[] { "rumor_spread" }); // told once, in the town lines
            Assert.That(!Fates.PeopleLines(rumor, "stay").Any(p => p.Line.Contains("폐쇄")) && Fates.TownLines(rumor, "stay").Any(l => l.Contains("한참 동안")));
        }

        [Test]
        public void Town_lines_are_two_or_three_plain_strings()
        {
            foreach (var f in GoldenRuns.Fix)
            {
                var t = Fates.TownLines(GoldenRuns.Build(f.id), f.id);
                Assert.That(t.Count >= 2 && t.Count <= 3 && t.All(x => !string.IsNullOrEmpty(x)), f.id);
            }
            Assert.AreEqual("연무동 소식은 박 소장의 문자로 들었다.", Fates.TownLines(GoldenRuns.Run("bus"), "bus")[0]);
        }

        [Test]
        public void A_letter_needs_a_bond_of_three_and_a_way_to_reach_you()
        {
            var a = Deeds.AxisNames.ToDictionary(k => k, k => 0);
            a["seoyeon"] = 3; a["clerk"] = 2; a["elder"] = 5;
            Assert.IsNotNull(Fates.LetterOf("seoyeon", a, id => id == "seoyeon_contact"));
            Assert.IsNull(Fates.LetterOf("seoyeon", a, id => false), "no way to reach you");
            Assert.IsNull(Fates.LetterOf("clerk", a, id => true), "a bond of two");
            Assert.IsNotNull(Fates.LetterOf("elder", a, id => id == "elder_met"));
            Assert.IsNull(Fates.LetterOf("town", a, id => true), "only neighbours write");
            Assert.IsNull(Fates.LetterOf("nobody", a, id => true));
        }

        // ---------- parity with the original ----------

        [Test]
        public void Every_golden_run_gets_the_people_and_town_lines_of_the_original()
        {
            CollectionAssert.AreEqual(Golden["paths"].Select(x => (string)x), Fates.Paths);
            CollectionAssert.AreEquivalent(Golden["leave"].Select(x => (string)x), Fates.Leave);
            CollectionAssert.AreEquivalent(Golden["town"].Select(x => (string)x), Fates.Town);
            var cases = (JArray)Golden["cases"];
            Assert.That(cases.Count, Is.GreaterThan(150));
            int asked = 0;
            foreach (var c in cases)
            {
                var name = (string)c["name"];
                var g = GoldenRuns.Load(name);
                Assert.AreEqual((string)c["path"], Fates.PathOf(g), $"{name}: path");
                foreach (var l in c["lines"])
                {
                    var id = (string)l["id"];
                    GoldenRuns.AssertGolden(Fates.PeopleLines(g, id), l["people"], $"{name}: people at ending {id}");
                    GoldenRuns.AssertGolden(Fates.TownLines(g, id), l["town"], $"{name}: town at ending {id}");
                    asked++;
                }
            }
            Assert.That(asked, Is.GreaterThan(300));
        }

        [Test]
        public void Letters_match_the_original()
        {
            foreach (var c in (JArray)Golden["letters"])
            {
                var g = GoldenRuns.Load((string)c["name"]);
                var a = Deeds.Axes(g, final: true);
                var done = new HashSet<string>(Deeds.Of(g).Select(d => d.Id));
                foreach (var p in ((JObject)c["letters"]).Properties())
                    Assert.AreEqual((string)p.Value, Fates.LetterOf(p.Name, a, done.Contains), $"{c["name"]}: {p.Name}");
            }
        }
    }
}
