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
    /// Port of the original tests/ledger.test.mjs and tests/r6-fix4-ledger.test.mjs, plus parity with the original
    /// (Fixtures/golden-ledger.json, regenerate with Tools/golden/make-golden-ledger.mjs).
    /// </summary>
    public class LedgerTests
    {
        GameRules R;
        Dictionary<string, int> prices;

        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-ledger.json"));

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            prices = Db.items.ToDictionary(i => i.id, i => i.price);
        }

        GameState Game(params (string id, int n)[] cart)
        {
            var g = R.CreateGame(8);
            g.Money = 10_000_000; g.Day = 3; g.Minute = 10 * 60;
            R.Buy(g, Cart(cart), prices);
            return g;
        }

        static LedgerPlan Plan(int water = 2, int meals = 2, string power = "phone") => new(water, meals, power);
        static int Total(GameState g, string id) => g.Items.TryGetValue(id, out var e) ? e.Home + e.Bag : 0;
        static string Text(LedgerResult r) => string.Join(" ", r.Lines);

        // ---------- tests/ledger.test.mjs ----------

        [Test]
        public void Deterministic_and_pure()
        {
            var g = Game(("water_500", 4), ("canned", 2));
            var before = SaveCodec.Save(g);
            var a = Ledger.SimulateDays(g, Plan(), 3);
            var b = Ledger.SimulateDays(g, Plan(), 3);
            AssertSameJson(JObject.FromObject(a), JObject.FromObject(b));
            Assert.AreEqual(before, SaveCodec.Save(g), "simulateDays does not touch G");
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, a.Rows.Select(r => (int)r["day"]));
            Assert.That(a.Lines.Count, Is.InRange(1, 5));
            foreach (var l in a.Lines) Assert.That(l, Does.Match("[가-힣]"));
            Assert.That(Text(a), Does.Not.Match("undefined|NaN|null"));
            Assert.Catch<ArgumentException>(() => Ledger.SimulateDays(g, Plan(4), 3));
            Assert.Catch<ArgumentException>(() => Ledger.SimulateDays(g, Plan(2, 2, "tv"), 3));
            Assert.Catch<ArgumentException>(() => Ledger.SimulateDays(g, Plan(), 0));
            Assert.Catch<ArgumentException>(() => Ledger.SimulateDays(g, Plan(), 2, "car"));
            Assert.AreEqual(3, Ledger.Picks.Water.Length);
        }

        [Test]
        public void Stock_never_goes_negative_whatever_the_plan()
        {
            foreach (var w in new[] { 1, 2, 3 })
                foreach (var m in new[] { 1, 2, 3 })
                    foreach (var place in new[] { "home", "shelter" })
                    {
                        var g = Game(("water_500", 1), ("bars", 1), ("gas_stove", 1), ("butane", 1));
                        Ledger.ApplyLedger(R, g, Ledger.SimulateDays(g, Plan(w, m, "radio"), 6, place));
                        foreach (var (id, e) in g.Items) Assert.That(e.Home >= 0 && e.Bag >= 0, $"{id} {w}/{m}/{place}");
                        Assert.That(g.Stats.Thirst, Is.LessThanOrEqualTo(80), "the ledger alone never exhausts");
                        Assert.That(g.Stats.Fatigue, Is.LessThanOrEqualTo(85));
                        Assert.That(g.Stats.Health, Is.GreaterThanOrEqualTo(20));
                        var back = SaveCodec.Load(SaveCodec.Save(g), Db);
                        AssertSameJson(JArray.FromObject(back.Ledger), JArray.FromObject(g.Ledger), "the rows survive a save");
                    }
        }

        [Test]
        public void Ramen_only_with_stove_and_butane_and_only_at_home()
        {
            var plain = Game();
            var r0 = Ledger.SimulateDays(plain, Plan(2, 3), 2);
            Assert.IsFalse(r0.Use.ContainsKey("ramen"), "no stove: no ramen");
            var g = Game(("gas_stove", 1), ("butane", 1));
            var home = Ledger.SimulateDays(g, Plan(2, 3), 2);
            Assert.AreEqual(5, home.Use["ramen"].Home, "all five START_HOME ramen cooked");
            Assert.AreEqual(1, home.Use["butane"].Home);
            Assert.AreEqual(12 - 5, (int)home.Rows.Last()["gasLeft"]);
            Assert.That(Text(home), Does.Match("라면을 5번"));
            var shelter = Ledger.SimulateDays(g, Plan(2, 3), 2, "shelter");
            Assert.IsFalse(shelter.Use.ContainsKey("ramen"), "never at the shelter");
            Assert.IsFalse(shelter.Use.ContainsKey("butane"));
            var noGas = Game(("gas_stove", 1));
            Assert.IsFalse(Ledger.SimulateDays(noGas, Plan(2, 3), 2).Use.ContainsKey("ramen"), "stove without butane");
        }

        [Test]
        public void Water_point_10_L_with_the_folding_jug_4_L_without()
        {
            GameState Dry(params (string id, int n)[] cart) { var g = Game(cart); g.Items["water_2l"].Home = 0; return g; }
            var no = Ledger.SimulateDays(Dry(), Plan(2), 1);
            var jug = Ledger.SimulateDays(Dry(("water_jug", 1)), Plan(2), 1);
            Assert.AreEqual(1, (int)no.Rows[0]["trips"]);
            Assert.AreEqual(2, (double)no.Rows[0]["loose"]);
            Assert.AreEqual(1, (int)jug.Rows[0]["trips"]);
            Assert.AreEqual(8, (double)jug.Rows[0]["loose"]);
            Assert.That(Text(jug), Does.Match("10L"));
            Assert.That(Text(no), Does.Match("4L"));
            Assert.AreEqual(6 + 1, no.Stats["fatigue"], "one trip plus two meals a day");
            // The jug water lasts: three days at 2 L need one trip, not three.
            Assert.AreEqual(1, Ledger.SimulateDays(Dry(("water_jug", 1)), Plan(2), 3).Summary.Trips);
            Assert.AreEqual(2, Ledger.SimulateDays(Dry(), Plan(2), 3).Summary.Trips);
        }

        [Test]
        public void Daily_radio_listening_drains_8_per_day_and_can_run_it_flat()
        {
            var g = Game(("radio", 1), ("batteries_aa", 1));
            R.Check(g, "radio");
            var r = Ledger.SimulateDays(g, Plan(2, 2, "radio"), 3);
            Assert.AreEqual(24, r.RadioDrain);
            Ledger.ApplyLedger(R, g, r);
            Assert.AreEqual(76, g.Items["radio"].Charge);
            Assert.IsTrue(g.Knows("radioDaily"));
            g.Items["radio"].Charge = 10;
            Ledger.ApplyLedger(R, g, Ledger.SimulateDays(g, Plan(2, 2, "radio"), 3));
            Assert.IsFalse(g.Items["radio"].Checked, "flat: needs new batteries");
            Assert.That(g.Events.Any(e => e.Type == "battery_dead" && (string)e.Details["id"] == "radio"));
            var none = Game();
            var n = Ledger.SimulateDays(none, Plan(2, 2, "radio"), 2);
            Assert.AreEqual(0, n.RadioDrain);
            Assert.IsFalse(n.RadioDaily);
            Assert.AreEqual(100, n.Phone);
            Assert.That(Text(n), Does.Match("라디오를 켤 수 없어"));
        }

        [Test]
        public void ApplyLedger_consumes_records_rows_and_an_event_summary_and_plan_line()
        {
            var g = Game(("powerbank", 1), ("water_2l", 2));
            var r = Ledger.SimulateDays(g, Plan(3, 2, "powerbank"), 2);
            int w = Total(g, "water_2l");
            var rows = Ledger.ApplyLedger(R, g, r);
            Assert.AreSame(r.Rows, rows);
            Assert.AreEqual(w - 3, Total(g, "water_2l"), "six litres = three 2 L bottles");
            Assert.IsTrue(g.Items["powerbank"].Checked);
            Assert.AreEqual(2, g.Ledger.Count);
            CollectionAssert.AreEqual(new[] { "day", "water", "meals", "power", "place" }, g.Ledger[0].Properties().Select(p => p.Name).Take(5));
            var e = g.Events.Last();
            Assert.AreEqual("ledger", e.Type);
            AssertJson(e.Details["plan"], "{water:3,meals:2,power:'powerbank'}");
            Assert.AreEqual("하루 배분: 물 3L · 두 끼 · 보조배터리", Ledger.PlanLine(e.Details["plan"]));
            Assert.AreEqual("하루 배분: 물 3L · 두 끼 · 보조배터리", Ledger.PlanLine(Plan(3, 2, "powerbank")));
            AssertJson(Ledger.LedgerSummary(g), "{days:2,trips:0,cooked:0,skipped:0,minWater:3}");
            Assert.Catch<ArgumentException>(() => Ledger.ApplyLedger(R, g, new LedgerResult()));
            Assert.Catch<ArgumentException>(() => Ledger.ApplyLedger(R, g, null));
        }

        [Test]
        public void Old_bars_are_the_last_resort_and_hurt_a_little()
        {
            var g = Game();
            foreach (var id in new[] { "ramen", "rice_instant", "canned" }) g.Items[id].Home = 0;
            var r = Ledger.SimulateDays(g, Plan(2, 2), 2);
            Assert.AreEqual(2, r.Use["old_bars"].Home);
            Assert.AreEqual(2, r.Summary.Skipped);
            Assert.AreEqual(-8, r.Stats["health"]);
            Assert.That(Text(r), Does.Match("건빵"));
        }

        // ---------- tests/r6-fix4-ledger.test.mjs ----------

        // The original asks debrief's endingRecord for its 'bought but never used' list; that list reads
        // items[id].used, which is what the ledger has to raise.
        [Test]
        public void S3_a_ledger_that_cooked_and_fetched_marks_the_stove_and_jug_used()
        {
            var g = Game(("gas_stove", 1), ("butane", 1), ("water_jug", 1), ("ramen", 10));
            var r = Ledger.SimulateDays(g, Plan(2, 3, "phone"), 5);
            Assert.That(r.Summary.Cooked > 0 && r.Summary.Trips > 0);
            Assert.IsTrue(r.UsedStove && r.UsedJug);
            Ledger.ApplyLedger(R, g, r);
            Assert.That(g.Items["gas_stove"].Used, Is.GreaterThan(0));
            Assert.That(g.Items["water_jug"].Used, Is.GreaterThan(0));
        }

        [Test]
        public void S3_a_jug_with_no_trip_stays_unused()
        {
            var g = Game(("water_jug", 1), ("water_2l", 10));
            var r = Ledger.SimulateDays(g, Plan(2, 1, "phone"), 2);
            Assert.AreEqual(0, r.Summary.Trips);
            Assert.IsFalse(r.UsedJug);
        }

        [Test]
        public void S6_euro_after_a_final_consonant_ro_after_vowel_endings()
        {
            var g = Game();
            Assert.That(Ledger.SimulateDays(g, Plan(2, 3, "phone"), 2).Lines[0], Does.Match("휴대폰 충전으로 2일을"));
            Assert.That(Ledger.SimulateDays(g, Plan(2, 3, "radio"), 2).Lines[0], Does.Match("라디오 매일 듣기로 2일을"));
            Assert.That(Ledger.SimulateDays(g, Plan(2, 3, "powerbank"), 2).Lines[0], Does.Match("보조배터리로 2일을"));
        }

        [Test]
        public void S8_the_powerbank_tops_the_phone_up_once_and_counts_as_used()
        {
            var g = Game(("powerbank", 1));
            g.Phone.Battery = 40;
            var r = Ledger.SimulateDays(g, Plan(2, 2, "powerbank"), 5);
            Assert.IsTrue(r.UsedBank);
            Assert.That(r.Phone, Is.GreaterThanOrEqualTo(50), $"phone {r.Phone}");
            Ledger.ApplyLedger(R, g, r);
            Assert.That(g.Items["powerbank"].Checked && g.Items["powerbank"].Used > 0);
            var brief = Ledger.SimulateDays(Game(("powerbank", 1)), Plan(2, 2, "powerbank"), 2);
            Assert.IsFalse(brief.UsedBank, "full phone needs no top-up");
        }

        [Test]
        public void S13_no_thirst_penalty_or_dry_throat_line_at_the_shelter()
        {
            var g = Game();
            var r = Ledger.SimulateDays(g, Plan(1, 2, "phone"), 5, "shelter");
            var two = Ledger.SimulateDays(g, Plan(2, 2, "phone"), 5, "shelter");
            Assert.AreEqual(two.Stats["thirst"], r.Stats["thirst"], "shelter water does not depend on the pick");
            Assert.IsFalse(r.Lines.Any(l => l.Contains("물을 아끼느라")));
            var home = Ledger.SimulateDays(Game(("water_2l", 10)), Plan(1, 2, "phone"), 5);
            Assert.That(home.Stats["thirst"] > 0 && home.Lines.Any(l => l.Contains("물을 아끼느라")));
        }

        // ---------- parity with the original ----------

        /// <summary>What ApplyLedger may touch, as the golden generator keeps it.</summary>
        static JObject Touched(GameState g, int eventsBefore)
        {
            var o = SaveCodec.ToJObject(g);
            return new JObject
            {
                ["items"] = o["items"], ["stats"] = o["stats"], ["phone"] = o["phone"], ["know"] = o["know"], ["ledger"] = o["ledger"],
                ["events"] = new JArray(((JArray)o["events"]).Skip(eventsBefore)),
            };
        }

        [Test]
        public void Picks_and_plan_lines_are_the_originals()
        {
            static JArray Rows<T>(LedgerPick<T>[] picks) => new(picks.Select(p => new JObject { ["id"] = JToken.FromObject(p.Id), ["label"] = p.Label }));
            var picks = new JObject { ["water"] = Rows(Ledger.Picks.Water), ["meals"] = Rows(Ledger.Picks.Meals), ["power"] = Rows(Ledger.Picks.Power) };
            AssertSameJson(picks, Golden["picks"], "PICKS");
            var lines = (JArray)Golden["planLines"];
            Assert.AreEqual(27, lines.Count);
            foreach (var c in lines)
            {
                Assert.AreEqual((string)c["line"], Ledger.PlanLine(c["plan"]));
                Assert.AreEqual((string)c["line"], Ledger.PlanLine(c["plan"].ToObject<LedgerPlan>()));
            }
            AssertSameJson(JObject.FromObject(Ledger.LedgerSummary(R.CreateGame(8))), Golden["emptySummary"], "no ledger yet");
        }

        [Test]
        public void Every_plan_at_home_and_at_the_shelter_plays_out_like_the_original()
        {
            int n = 0;
            foreach (var m in Golden["matrices"])
            {
                var g = SaveCodec.Load(m["save"].ToString(), Db);
                var before = SaveCodec.Save(g);
                foreach (var c in m["cases"])
                {
                    var plan = c["plan"].ToObject<LedgerPlan>();
                    var r = c["place"].Type == JTokenType.Null
                        ? Ledger.SimulateDays(g, plan, (int)c["days"])
                        : Ledger.SimulateDays(g, plan, (int)c["days"], (string)c["place"]);
                    AssertSameJson(JObject.FromObject(r), c["result"], $"{m["label"]}: {c["plan"].ToString(Newtonsoft.Json.Formatting.None)} {c["days"]} days at {c["place"]}");
                    n++;
                }
                Assert.AreEqual(before, SaveCodec.Save(g), "simulating changes nothing");
            }
            Assert.AreEqual(109, n);
        }

        [Test]
        public void Every_golden_household_is_simulated_and_applied_like_the_original()
        {
            var cases = (JArray)Golden["cases"];
            Assert.AreEqual(96, cases.Count);
            int trips = 0, cooked = 0, skipped = 0, flat = 0;
            foreach (var c in cases)
            {
                var g = SaveCodec.Load(c["before"].ToString(), Db);
                string at = $"seed {g.Seed}: {c["plan"].ToString(Newtonsoft.Json.Formatting.None)} {c["days"]} days at {c["place"]}";
                int eventsBefore = g.Events.Count;
                var plan = c["plan"].ToObject<LedgerPlan>();
                var r = Ledger.SimulateDays(g, plan, (int)c["days"], (string)c["place"]);
                AssertSameJson(JObject.FromObject(r), c["result"], at);
                Ledger.ApplyLedger(R, g, r);
                AssertSameJson(Touched(g, eventsBefore), c["after"], at + " (applied)");
                AssertSameJson(JObject.FromObject(Ledger.LedgerSummary(g)), c["summary"], at + " (summary)");
                Assert.AreEqual((string)c["planLine"], Ledger.PlanLine(plan));
                var json = SaveCodec.Save(g);
                Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)), at + ": the save stays valid");
                trips += r.Summary.Trips; cooked += r.Summary.Cooked; skipped += r.Summary.Skipped;
                if (g.Events.Skip(eventsBefore).Any(e => e.Type == "battery_dead")) flat++;
            }
            Assert.That(trips > 50 && cooked > 50 && skipped > 50 && flat > 0, $"every branch is reached: trips {trips}, cooked {cooked}, skipped {skipped}, flat {flat}");
        }

        [Test]
        public void The_radio_charge_whole_or_fractional_runs_down_and_reads_like_the_original()
        {
            var radio = Golden["radio"];
            var runs = (JArray)radio["runs"];
            Assert.AreEqual(40, runs.Count);
            foreach (var c in runs)
            {
                var g = SaveCodec.Load(radio["save"].ToString(), Db);
                g.Items["radio"].Charge = (double?)c["charge"]; // null: checked before charges existed, counts as full
                string at = $"charge {c["charge"]} for {c["days"]} days";
                int eventsBefore = g.Events.Count;
                var r = Ledger.SimulateDays(g, Plan(2, 2, "radio"), (int)c["days"]);
                AssertSameJson(JObject.FromObject(r), c["result"], at);
                Ledger.ApplyLedger(R, g, r);
                AssertSameJson(Touched(g, eventsBefore), c["after"], at + " (applied)");
            }
        }

        [Test]
        public void Bad_plans_days_and_places_are_refused_like_the_original()
        {
            var g = Game();
            var bad = (JArray)Golden["bad"];
            Assert.AreEqual(11, bad.Count);
            foreach (var c in bad)
            {
                var plan = (c["plan"] as JObject)?.ToObject<LedgerPlan>();
                Assert.Catch<ArgumentException>(() => Ledger.SimulateDays(g, plan, (int)c["days"], (string)c["place"]), c.ToString(Newtonsoft.Json.Formatting.None));
            }
            Assert.Catch<ArgumentException>(() => Ledger.PlanLine((JToken)null));
            Assert.AreEqual(0, g.Ledger.Count);
            Assert.IsFalse(g.Events.Any(e => e.Type == "ledger"));
        }
    }
}
