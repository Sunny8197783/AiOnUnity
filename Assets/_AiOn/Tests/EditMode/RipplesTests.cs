using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/ripples.test.mjs (and the ripples line of r6-fix3-systems.test.mjs), plus parity
    /// with the original (Fixtures/golden-ripples.json, regenerate with Tools/golden/make-golden-ripples.mjs).
    /// </summary>
    public class RipplesTests
    {
        GameRules R;

        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-ripples.json"));

        [SetUp]
        public void SetUp() => R = Rules;

        static void At(GameState g, int day, int minute) { g.Day = day; g.Minute = minute; }
        static void Payoff(GameState g, string prep, string moment) =>
            GameRules.RecordEvent(g, "payoff", new JObject { ["prep"] = prep, ["moment"] = moment });

        [Test]
        public void Every_prep_but_judgement_has_a_cause_and_labels_are_Korean()
        {
            foreach (var (id, label) in Ripples.Prep)
            {
                Assert.That(label, Does.Match("[가-힣]"), id);
                if (id == "judgement") { Assert.IsFalse(Ripples.Causes.ContainsKey(id)); continue; }
                Assert.That(Ripples.Causes.TryGetValue(id, out var rules) && rules.Length > 0, $"cause for {id}");
                foreach (var c in rules) Assert.That(c.Text, Does.Match("[가-힣]"));
            }
            foreach (var id in Ripples.Causes.Keys) Assert.That(Ripples.Prep.ContainsKey(id), $"{id} has a label");
            Assert.AreEqual("D-1 오전", Ripples.WhenText(-1, 9 * 60));
            Assert.AreEqual("D-day 저녁", Ripples.WhenText(0, 20 * 60 + 6));
            Assert.AreEqual("D+12 밤", Ripples.WhenText(12, 22 * 60));
        }

        [Test]
        public void An_empty_game_has_no_ripples()
        {
            Assert.IsEmpty(Ripples.Of(R.CreateGame(1)));
        }

        [Test]
        public void Bag_and_tub_two_rows_cause_before_effect_sorted_by_gap()
        {
            var g = R.CreateGame(1);
            At(g, -1, 9 * 60); GameRules.RecordEvent(g, "bag_packed");
            At(g, -1, 22 * 60); GameRules.RecordEvent(g, "tub_filled");
            At(g, 0, 20 * 60 + 6); Payoff(g, "bag", "바로 나섰다");
            At(g, 1, 8 * 60); Payoff(g, "tub", "받아 둔 물로 세수했다");
            At(g, 2, 8 * 60); Payoff(g, "bag", "두 번째");
            Payoff(g, "judgement", "기다렸다");
            Payoff(g, "radio", "원인 없음");
            var r = Ripples.Of(g);
            CollectionAssert.AreEqual(new[] { "bag", "tub" }, r.Select(x => x.Prep), "bag: 35 h, tub: 34 h");
            foreach (var x in r)
            {
                Assert.That(x.Cause.Day * 1440 + x.Cause.Minute, Is.LessThanOrEqualTo(x.Effect.Day * 1440 + x.Effect.Minute));
                Assert.That(x.Gap, Is.GreaterThan(0));
                foreach (var t in new[] { x.Label, x.Cause.Text, x.Cause.When, x.Effect.When }) Assert.That(t, Does.Match("[가-힣]|D"));
            }
            Assert.AreEqual("바로 나섰다", r[0].Effect.Text, "first payoff only");
            Assert.AreEqual("D-1 오전", r[0].Cause.When);
            Assert.AreEqual((0 - -1) * 1440 + (20 * 60 + 6) - 9 * 60, r[0].Gap);
            AssertJson(Ripples.CauseOf(g, "bag"), "{day:-1,minute:540,when:'D-1 오전',text:'비상가방을 꾸려 현관에 두었다'}");
            Assert.IsNull(Ripples.CauseOf(g, "radio"));
            Assert.IsNull(Ripples.CauseOf(g, "judgement"), "no single cause");
            Assert.IsNull(Ripples.CauseOf(g, "bag", 1), "nothing happened before the first event");
        }

        [Test]
        public void Causes_match_on_details_check_id_bought_cart_practice_drill_latest_cause_wins()
        {
            var g = R.CreateGame(2);
            g.Money = 1_000_000;
            At(g, -2, 10 * 60);
            R.Buy(g, Cart(("water_jug", 1), ("batteries_aa", 2), ("radio", 1)),
                new Dictionary<string, int> { ["water_jug"] = 7000, ["batteries_aa"] = 4500, ["radio"] = 24000 });
            R.Check(g, "radio");
            At(g, -1, 15 * 60); R.Check(g, "flashlight");
            BagDrill.RecordEncounter(g, "extinguisher", "risky");
            BagDrill.RecordEncounter(g, "extinguisher", "practice");
            At(g, -1, 19 * 60); GameRules.RecordEvent(g, "bag_packed");
            At(g, -1, 20 * 60); GameRules.RecordEvent(g, "bag_packed");
            At(g, 1, 9 * 60);
            foreach (var prep in new[] { "water_jug", "radio", "flashlight", "extinguisher", "bag" }) Payoff(g, prep, "순간");
            var by = Ripples.Of(g).ToDictionary(x => x.Prep);
            Assert.AreEqual("접이식 물통을 사 두었다", by["water_jug"].Cause.Text);
            Assert.AreEqual(-2, by["radio"].Cause.Day);
            Assert.AreEqual(-1, by["flashlight"].Cause.Day);
            Assert.AreEqual(15 * 60, by["extinguisher"].Cause.Minute);
            Assert.AreEqual(20 * 60, by["bag"].Cause.Minute, "the latest cause before the payoff");
        }

        // tests/r6-fix3-systems.test.mjs, 'gas valve check and the wet towel pay off in the smoke escape': the story
        // scenes that record these events are not ported yet, so the events are written here as those scenes write them.
        [Test]
        public void The_gas_valve_and_the_wet_towel_show_up_as_ripples()
        {
            var g = R.CreateGame(3);
            GameRules.RecordEvent(g, "gas_checked");
            GameRules.RecordEvent(g, "tub_filled");
            GameRules.Advance(g, 600);
            foreach (var prep in new[] { "gas", "tub", "towel" }) Payoff(g, prep, "순간");
            var rows = Ripples.Of(g);
            CollectionAssert.AreEqual(new[] { "gas", "towel", "tub" }, rows.Select(r => r.Prep), "equal gaps: by id");
            Assert.AreEqual("큰 통에 물을 받아 두었다", rows[1].Cause.Text);
            Assert.AreEqual("젖은 수건", rows[1].Label);
        }

        [Test]
        public void The_tables_are_the_original_ones_in_the_original_order()
        {
            var prep = (JObject)Golden["prep"];
            CollectionAssert.AreEqual(prep.Properties().Select(p => p.Name), Ripples.Prep.Keys.Except(Ripples.PortAdded));
            foreach (var p in prep.Properties()) Assert.AreEqual((string)p.Value, Ripples.Prep[p.Name], p.Name);

            var causes = (JObject)Golden["causes"];
            CollectionAssert.AreEqual(causes.Properties().Select(p => p.Name), Ripples.Causes.Keys.Except(Ripples.PortAdded));
            foreach (var p in causes.Properties())
            {
                var rules = Ripples.Causes[p.Name];
                var want = (JArray)p.Value;
                Assert.AreEqual(want.Count, rules.Length, p.Name);
                for (int i = 0; i < rules.Length; i++)
                {
                    Assert.AreEqual((string)want[i]["type"], rules[i].Type, p.Name);
                    Assert.AreEqual((string)want[i]["text"], rules[i].Text, p.Name);
                    Assert.AreEqual((bool)want[i]["when"], rules[i].When != null, $"{p.Name}[{i}] has a detail test");
                }
            }
        }

        [Test]
        public void WhenText_names_the_day_and_the_part_of_the_day_like_the_original()
        {
            var cases = (JArray)Golden["whenText"];
            Assert.That(cases.Count, Is.GreaterThan(80));
            foreach (var c in cases)
                Assert.AreEqual((string)c["text"], Ripples.WhenText((int)c["day"], (int)c["minute"]), $"day {c["day"]} minute {c["minute"]}");
        }

        [Test]
        public void Every_golden_history_ripples_like_the_original()
        {
            var scenarios = (JArray)Golden["scenarios"];
            Assert.AreEqual(37, scenarios.Count);
            var preps = Ripples.Prep.Keys.Append("mystery").ToList();
            int rows = 0, causes = 0;
            foreach (var s in scenarios)
            {
                var g = SaveCodec.Load(s["save"].ToString(), Db);
                string at = $"seed {g.Seed}";
                var actual = Ripples.Of(g);
                AssertSameJson(JArray.FromObject(actual), s["ripples"], at);
                rows += actual.Count;

                void AssertCause(string prep, int? beforeSeq, JToken want)
                {
                    var c = Ripples.CauseOf(g, prep, beforeSeq);
                    string where = $"{at} {prep} before {beforeSeq}";
                    if (want == null || want.Type == JTokenType.Null) Assert.IsNull(c, where);
                    else { Assert.IsNotNull(c, where); AssertSameJson(JObject.FromObject(c), want, where); causes++; }
                }
                foreach (var prep in preps) AssertCause(prep, null, s["causes"][prep]);
                foreach (var b in s["before"])
                    foreach (var prep in preps) AssertCause(prep, (int)b["beforeSeq"], b["causes"][prep]);
            }
            Assert.AreEqual(152, rows, "ripple rows over all histories");
            Assert.That(causes, Is.GreaterThan(500));
        }
    }
}
