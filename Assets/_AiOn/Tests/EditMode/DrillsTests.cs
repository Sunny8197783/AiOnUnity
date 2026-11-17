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
    /// Port of the radio, extinguisher and contact-card cases of the original tests/drills.test.mjs (the bag and
    /// ladder cases are in BagDrillTests), plus parity with the original (Fixtures/golden-drills.json, regenerate
    /// with Tools/golden/make-golden-drills.mjs).
    /// </summary>
    public class DrillsTests
    {
        GameRules R;
        Dictionary<string, int> prices;

        static Facts _facts;
        static Facts F => _facts ??= Facts.Load(Fixture("../../../Data/Source/facts.json"));
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-drills.json"));

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            prices = Db.items.ToDictionary(i => i.id, i => i.price);
        }

        static void AssertFacts(IEnumerable<string> ids) { foreach (var id in ids) Assert.IsNotNull(F.Fact(id), $"fact {id} exists"); }
        static List<string> Strings(JToken list) => list.Select(t => (string)t).ToList();
        static ExtinguisherEval Ext(string fire, params string[] sequence) => Drills.EvalExtinguisher(sequence, fire);

        [Test]
        public void Every_step_choice_and_field_points_at_real_facts()
        {
            foreach (var s in Drills.ExtSteps.Concat(Drills.ExtChoices))
            {
                Assert.That(s.Facts.Length, Is.GreaterThan(0), s.Id);
                AssertFacts(s.Facts);
            }
            foreach (var f in Drills.DocFields)
            {
                Assert.That(f.Facts.Length, Is.GreaterThan(0), f.Id);
                AssertFacts(f.Facts);
            }
        }

        [Test]
        public void The_original_drill_names_reach_the_bag_drill()
        {
            var g = R.CreateGame(1);
            Assert.AreEqual("guided", Drills.LadderStage(g, "radio"));
            AssertJson(Drills.RecordEncounter(g, "radio", "tuned"), "{day:-3,minute:1050,stage:'guided',result:'tuned'}");
            Assert.AreEqual("hint", BagDrill.LadderStage(g, "radio"));
            Assert.AreSame(BagDrill.Goals, Drills.BagGoals);
            Assert.IsFalse(Drills.EvalBag(R, g, "alert").Ok);
        }

        // ---------- radio ----------

        [Test]
        public void RadioSignal_falls_off_within_the_band_and_picks_the_nearest_station()
        {
            var a = new RadioStation { Freq = 89.1, Name = "A" };
            var b = new RadioStation { Freq = 90.0, Name = "B" };
            Assert.AreEqual(0.6, Drills.RadioWidth);
            var r = Drills.RadioSignal(89.1, new[] { a, b });
            Assert.AreSame(a, r.Station);
            Assert.AreEqual(1, r.Clarity);
            r = Drills.RadioSignal(89.4, new[] { a });
            Assert.AreSame(a, r.Station);
            Assert.AreEqual(0.5, r.Clarity);
            r = Drills.RadioSignal(89.7, new[] { a });
            Assert.IsNull(r.Station, "edge of the band is silent");
            Assert.AreEqual(0, r.Clarity);
            r = Drills.RadioSignal(95.0, new[] { a, b });
            Assert.IsNull(r.Station);
            Assert.AreEqual(0, r.Clarity);
            Assert.AreSame(b, Drills.RadioSignal(89.7, new[] { a, b }).Station);
            Assert.AreSame(a, Drills.RadioSignal(89.5, new[] { a, b }).Station);
            foreach (var none in new IEnumerable<RadioStation>[] { new RadioStation[0], null })
            {
                r = Drills.RadioSignal(89.1, none);
                Assert.IsNull(r.Station);
                Assert.AreEqual(0, r.Clarity);
            }
            Assert.IsNull(Drills.RadioSignal(89.1).Station, "no stations given");
            foreach (var f in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Catch<ArgumentException>(() => Drills.RadioSignal(f, new[] { a }));
        }

        [Test]
        public void RadioNeeds_no_radio_no_batteries_unchecked_slow_checked()
        {
            var g = R.CreateGame(4);
            g.Money = 10_000_000;
            AssertJson(Drills.RadioNeeds(g), "{ok:false,blocks:['noRadio'],extraSeconds:0,tips:['power_prep']}");
            R.Buy(g, Cart(("radio", 1)), prices);
            CollectionAssert.AreEqual(new[] { "noBatteries" }, Drills.RadioNeeds(g).Blocks);
            Assert.IsFalse(Drills.RadioNeeds(g).Ok);
            R.Buy(g, Cart(("batteries_aa", 1)), prices);
            R.Pack(g, "radio");
            AssertJson(Drills.RadioNeeds(g), "{ok:true,blocks:['notChecked'],extraSeconds:40,tips:['radio_batt']}", "radio in the bag counts");
            R.Check(g, "radio");
            AssertJson(Drills.RadioNeeds(g), "{ok:true,blocks:[],extraSeconds:0,tips:[]}");
        }

        [Test]
        public void Every_golden_dial_position_hears_what_the_original_hears()
        {
            var sets = Golden["stationSets"].Select(set => set.Select(s => new RadioStation
            { Freq = (double)s["freq"], Name = (string)s["name"], Kind = (string)s["kind"] }).ToList()).ToList();
            var cases = (JArray)Golden["signals"];
            Assert.That(cases.Count, Is.GreaterThan(1000));
            Assert.AreEqual((double)Golden["radioWidth"], Drills.RadioWidth);
            int heard = 0;
            foreach (var c in cases)
            {
                var set = sets[(int)c["set"]];
                // The fixture keeps the two short decimals apart: their sum here is the same double the original tuned to.
                double freq = (double)c["freq"] + (double)c["plus"];
                var r = Drills.RadioSignal(freq, set);
                var want = c["station"].Type == JTokenType.Null ? null : set[(int)c["station"]];
                string at = $"set {c["set"]} at {c["freq"]} + {c["plus"]}";
                Assert.AreSame(want, r.Station, at);
                Assert.AreEqual((double)c["clarity"], r.Clarity, at);
                if (r.Station != null) heard++;
            }
            Assert.That(heard, Is.GreaterThan(150));
        }

        [Test]
        public void Every_golden_radio_state_needs_what_the_original_says()
        {
            var cases = (JArray)Golden["needs"];
            Assert.AreEqual(11, cases.Count);
            foreach (var c in cases)
            {
                var g = SaveCodec.Load(c["save"].ToString(), Db);
                AssertSameJson(JToken.FromObject(Drills.RadioNeeds(g)), c["needs"], (string)c["label"]);
            }
        }

        // ---------- extinguisher ----------

        [Test]
        public void ExtSteps_keep_the_official_order_and_choices_are_fight_or_retreat()
        {
            CollectionAssert.AreEqual(new[] { "shout", "pin", "aim", "squeeze", "sweep" }, Drills.ExtSteps.Select(s => s.Id));
            CollectionAssert.AreEqual(new[] { "fight", "retreat" }, Drills.ExtChoices.Select(c => c.Id));
            foreach (var s in Drills.ExtSteps.Concat(Drills.ExtChoices)) Assert.That(s.Label, Does.Match("[가-힣]"));
            AssertSameJson(JArray.FromObject(Drills.ExtSteps), Golden["extSteps"], "EXT_STEPS");
            AssertSameJson(JArray.FromObject(Drills.ExtChoices), Golden["extChoices"], "EXT_CHOICES");
        }

        [Test]
        public void EvalExtinguisher_small_fire_right_and_wrong_orders()
        {
            AssertJson(Ext("small", "fight", "shout", "pin", "aim", "squeeze", "sweep"), "{result:'out',correct:true,firstError:null}");
            AssertJson(Ext("small", "fight", "pin", "aim", "squeeze", "sweep"), "{result:'out',correct:false,firstError:'shout'}", "out, but nobody was alerted");
            AssertJson(Ext("small", "shout", "aim", "pin", "squeeze", "sweep"), "{result:'incomplete',correct:false,firstError:'pin'}");
            AssertJson(Ext("small", "shout", "pin", "aim", "squeeze"), "{result:'incomplete',correct:false,firstError:'sweep'}");
            Assert.AreEqual("out", Ext("small", "shout", "pin", "pin", "aim", "squeeze", "sweep").Result, "repeated presses are ignored");
            AssertJson(Ext("small", "fight"), "{result:'incomplete',correct:false,firstError:'shout'}");
            AssertJson(Ext("small"), "{result:'incomplete',correct:false,firstError:'shout'}");
        }

        [Test]
        public void EvalExtinguisher_retreating_is_always_a_success_fighting_a_large_fire_is_not()
        {
            foreach (var fire in new[] { "small", "large" })
                AssertJson(Ext(fire, "retreat"), "{result:'retreat-ok',correct:true,firstError:null}");
            Assert.AreEqual("retreat-ok", Ext("small", "shout", "retreat").Result);
            AssertJson(Ext("large", "fight", "shout", "pin", "aim", "squeeze", "sweep"), "{result:'escalated',correct:false,firstError:'retreat'}");
            AssertJson(Ext("large", "fight", "pin", "retreat"), "{result:'retreat-ok',correct:false,firstError:'retreat'}", "late retreat still gets out");
            AssertJson(Ext("large"), "{result:'incomplete',correct:false,firstError:'retreat'}");
        }

        [Test]
        public void EvalExtinguisher_rejects_bad_input()
        {
            Assert.Catch<ArgumentException>(() => Drills.EvalExtinguisher(new string[0], null));
            Assert.Catch<ArgumentException>(() => Drills.EvalExtinguisher(new string[0], "medium"));
            Assert.Catch<ArgumentException>(() => Drills.EvalExtinguisher(null, "small"));
            Assert.Catch<ArgumentException>(() => Ext("small", "pin", "water"));
        }

        [Test]
        public void Every_golden_extinguisher_run_is_judged_like_the_original()
        {
            var cases = (JArray)Golden["extinguisher"];
            Assert.AreEqual(1800, cases.Count);
            var results = new Dictionary<string, int>();
            foreach (var c in cases)
            {
                var seq = Strings(c["sequence"]);
                var r = Drills.EvalExtinguisher(seq, (string)c["fire"]);
                string at = $"{c["fire"]}: {string.Join(" ", seq)}";
                Assert.AreEqual((string)c["result"], r.Result, at);
                Assert.AreEqual((bool)c["correct"], r.Correct, at);
                Assert.AreEqual((string)c["firstError"], r.FirstError, at);
                results[r.Result] = (results.TryGetValue(r.Result, out var n) ? n : 0) + 1;
            }
            CollectionAssert.AreEquivalent(new[] { "out", "incomplete", "escalated", "retreat-ok" }, results.Keys, "every result is reached");
        }

        // ---------- emergency contact card ----------

        [Test]
        public void DocFields_six_recommended_fields_each_explained_by_its_fact_text()
        {
            CollectionAssert.AreEqual(new[] { "contacts", "meds", "shelter", "meet", "idCopy", "cash" }, Drills.DocFields.Select(f => f.Id));
            var golden = (JArray)Golden["docFields"];
            Assert.AreEqual(golden.Count, Drills.DocFields.Length);
            for (int i = 0; i < golden.Count; i++)
            {
                var f = Drills.DocFields[i];
                Assert.IsTrue(f.Recommended);
                Assert.AreEqual(F.Fact(f.Facts[0]).Text, f.Why(F.Text));
                Assert.That(f.Label, Does.Match("[가-힣]"));
                var row = JObject.FromObject(f);
                row["why"] = f.Why(F.Text);
                AssertSameJson(row, golden[i], f.Id);
            }
        }

        [Test]
        public void EvalDocs_full_card_in_the_bag_card_left_at_home_empty_and_blank_values()
        {
            var ids = Drills.DocFields.Select(f => f.Id).ToArray();
            var full = new JObject();
            foreach (var id in ids) full[id] = "적음";

            var r = Drills.EvalDocs(full, "bag");
            Assert.IsTrue(r.Ok);
            CollectionAssert.AreEqual(ids, r.Covered);
            Assert.IsEmpty(r.Missing);
            Assert.IsEmpty(r.Tips);
            Assert.IsTrue(Drills.EvalDocs(full).Ok, "where is optional");

            r = Drills.EvalDocs(full, "home");
            Assert.IsFalse(r.Ok);
            CollectionAssert.AreEqual(ids, r.Covered);
            Assert.IsEmpty(r.Missing);
            CollectionAssert.AreEqual(new[] { "contact_card" }, r.Tips);

            var blank = new JObject { ["contacts"] = "   ", ["meds"] = false, ["shelter"] = JValue.CreateNull(), ["meet"] = "" };
            foreach (var card in new[] { null, new JObject(), blank })
            {
                r = Drills.EvalDocs(card, "bag");
                Assert.IsFalse(r.Ok);
                Assert.IsEmpty(r.Covered);
                CollectionAssert.AreEqual(ids, r.Missing);
                AssertFacts(r.Tips);
            }

            r = Drills.EvalDocs(new JObject { ["contacts"] = "엄마 010", ["meds"] = 0 }, "bag");
            CollectionAssert.AreEqual(new[] { "contacts", "meds" }, r.Covered);
            CollectionAssert.DoesNotContain(r.Missing, "contacts");
        }

        [Test]
        public void Every_golden_card_is_judged_like_the_original()
        {
            var cases = (JArray)Golden["docs"];
            Assert.AreEqual(48, cases.Count);
            int ok = 0;
            foreach (var c in cases)
            {
                var r = Drills.EvalDocs(c["card"] as JObject, (string)c["where"]);
                string at = $"{c["card"].ToString(Newtonsoft.Json.Formatting.None)} kept at {c["where"]}";
                Assert.AreEqual((bool)c["ok"], r.Ok, at);
                CollectionAssert.AreEqual(Strings(c["covered"]), r.Covered, at);
                CollectionAssert.AreEqual(Strings(c["missing"]), r.Missing, at);
                CollectionAssert.AreEqual(Strings(c["tips"]), r.Tips, at);
                if (r.Ok) ok++;
            }
            Assert.AreEqual(4, ok, "a full card, in the bag or with no place asked");
        }
    }
}
