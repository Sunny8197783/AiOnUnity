using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>Port of the bag cases of the original tests/drills.test.mjs, plus parity with its evalBag.</summary>
    public class BagDrillTests
    {
        GameRules R;
        Dictionary<string, string> facts;
        Dictionary<string, int> prices;

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            facts = new Dictionary<string, string>();
            GameSession.LoadFacts(Fixture("../../../Data/Source/facts.json"), facts);
            prices = Db.items.Where(i => !i.fixture).ToDictionary(i => i.id, i => i.price);
        }

        GameState Game(params (string id, int n)[] cart)
        {
            var g = R.CreateGame(3);
            g.Money = 10_000_000;
            R.Buy(g, Cart(cart), prices);
            return g;
        }

        void PackAll(GameState g, params (string id, int n)[] list) { foreach (var (id, n) in list) R.Pack(g, id, n); }

        void AssertFacts(IEnumerable<string> ids) { foreach (var id in ids) Assert.That(facts.ContainsKey(id), $"fact {id} exists"); }

        /// <summary>A bag that covers every 'alert' goal with usable (checked) items.</summary>
        GameState AlertBag(params (string id, int n)[] extra)
        {
            var g = Game(new[] { ("water_500", 2), ("bars", 2), ("radio", 1), ("batteries_aa", 2), ("blanket", 1), ("contact_card", 1) }.Concat(extra).ToArray());
            Assert.IsTrue(R.Check(g, "flashlight").Ok);
            Assert.IsTrue(R.Check(g, "radio").Ok);
            PackAll(g, ("water_2l", 1), ("bars", 1), ("flashlight", 1), ("radio", 1), ("meds", 1), ("contact_card", 1), ("blanket", 1));
            return g;
        }

        [Test]
        public void Every_goal_points_at_real_items_and_facts()
        {
            foreach (var (ctx, goals) in BagDrill.Goals)
            {
                Assert.That(goals.Length, Is.GreaterThan(0), ctx);
                Assert.AreEqual(goals.Length, goals.Select(g => g.Id).Distinct().Count(), $"{ctx} goal ids unique");
                foreach (var g in goals)
                {
                    Assert.That(!string.IsNullOrEmpty(g.Label) && g.Items.Length > 0 && g.Facts.Length > 0, $"{ctx}.{g.Id}");
                    foreach (var id in g.Items) Assert.IsNotNull(Db.Get(id), $"{ctx}.{g.Id}: {id}");
                    AssertFacts(g.Facts);
                }
            }
            AssertFacts(BagDrill.ExtraTips.Values);
            AssertFacts(BagDrill.AlertTips);
            foreach (var c in Db.items) AssertFacts(c.facts);
        }

        [Test]
        public void Ladder_climbs_guided_hint_free_per_tool_and_records_encounters()
        {
            var g = R.CreateGame(1);
            Assert.AreEqual("guided", BagDrill.LadderStage(g, "radio"));
            var r = BagDrill.RecordEncounter(g, "radio", "tuned");
            AssertJson(r, "{day:-3,minute:1050,stage:'guided',result:'tuned'}");
            AssertJson(g.Events.Last().Details, "{tool:'radio',result:'tuned',stage:'guided'}");
            Assert.AreEqual("encounter", g.Events.Last().Type);
            Assert.AreEqual("hint", BagDrill.LadderStage(g, "radio"));
            Assert.AreEqual("hint", (string)BagDrill.RecordEncounter(g, "radio", "missed")["stage"]);
            Assert.AreEqual("free", BagDrill.LadderStage(g, "radio"));
            BagDrill.RecordEncounter(g, "radio", "tuned");
            Assert.AreEqual("free", BagDrill.LadderStage(g, "radio"), "stays free");
            Assert.AreEqual(3, g.Skill["radio"].Count);
            foreach (var tool in new[] { "bag", "route", "extinguisher", "docs" }) Assert.AreEqual("guided", BagDrill.LadderStage(g, tool));
            int n = g.Events.Count;
            Assert.Catch(() => BagDrill.LadderStage(g, "nope"));
            Assert.Catch(() => BagDrill.RecordEncounter(g, "nope", "x"));
            Assert.AreEqual(n, g.Events.Count, "a bad tool records nothing");
            Assert.Catch(() => BagDrill.LadderStage(null, "radio"));
        }

        [Test]
        public void An_empty_bag_misses_every_goal()
        {
            var g = R.CreateGame(2);
            foreach (var (ctx, goals) in BagDrill.Goals)
            {
                var r = BagDrill.EvalBag(R, g, ctx);
                Assert.IsFalse(r.Ok);
                Assert.IsEmpty(r.Covered);
                CollectionAssert.AreEqual(goals.Select(x => x.Id), r.Missing);
                Assert.IsEmpty(r.Extra);
                Assert.AreEqual(0, r.WeightKg);
                Assert.AreEqual(0, r.VolumeL);
                Assert.IsNotEmpty(r.Tips);
                AssertFacts(r.Tips);
            }
            Assert.Catch(() => BagDrill.EvalBag(R, g, "picnic"));
            Assert.Catch(() => BagDrill.EvalBag(R, g, null));
        }

        [Test]
        public void A_full_alert_bag_with_checked_gear_is_ok()
        {
            var r = BagDrill.EvalBag(R, AlertBag(), "alert");
            Assert.IsTrue(r.Ok);
            CollectionAssert.AreEqual(BagDrill.Goals["alert"].Select(g => g.Id), r.Covered);
            Assert.IsEmpty(r.Missing); Assert.IsEmpty(r.Extra); Assert.IsEmpty(r.Unready); Assert.IsEmpty(r.Tips);
            Assert.AreEqual(2.82, r.WeightKg); // 1.29 kg of gear + one 2 L bottle instead of 500 mL
        }

        [Test]
        public void Bought_is_not_usable_unchecked_gear_covers_no_goal()
        {
            var g = Game(("radio", 1));
            PackAll(g, ("radio", 1), ("flashlight", 1));
            var r = BagDrill.EvalBag(R, g, "alert");
            Assert.That(r.Missing, Does.Contain("info").And.Contain("light"));
            CollectionAssert.AreEquivalent(new[] { "flashlight", "radio" }, r.Unready);
            CollectionAssert.Contains(r.Tips, "radio_batt");
            AssertFacts(r.Tips);
        }

        [Test]
        public void Extras_get_a_fact_tip_and_overweight_fails_even_when_covered()
        {
            var g = AlertBag(("gas_stove", 1), ("candles", 1), ("water_2l", 2));
            PackAll(g, ("gas_stove", 1), ("candles", 1), ("ramen", 1));
            var r = BagDrill.EvalBag(R, g, "alert");
            CollectionAssert.AreEquivalent(new[] { "candles", "gas_stove", "ramen" }, r.Extra);
            foreach (var t in new[] { "shelter_rules", "candle_no", "bag_nocook" }) CollectionAssert.Contains(r.Tips, t);
            Assert.IsTrue(r.Ok, "extras alone do not fail the bag");
            R.Pack(g, "water_2l", 3);
            r = BagDrill.EvalBag(R, g, "alert");
            Assert.That(r.WeightKg, Is.GreaterThan(8));
            Assert.IsEmpty(r.Missing);
            Assert.IsFalse(r.Ok);
            CollectionAssert.Contains(r.Tips, "bag_foil");
            AssertFacts(r.Tips);
        }

        [Test]
        public void The_water_trip_wants_an_empty_jug_and_a_light_bag()
        {
            var g = Game(("water_jug", 1));
            R.Pack(g, "water_jug");
            var r = BagDrill.EvalBag(R, g, "water");
            Assert.IsTrue(r.Ok);
            CollectionAssert.AreEqual(new[] { "container" }, r.Covered);
            Assert.IsEmpty(r.Tips);
            R.Pack(g, "water_2l");
            r = BagDrill.EvalBag(R, g, "water");
            Assert.IsFalse(r.Ok, "over 2 kg before the walk back");
            CollectionAssert.AreEqual(new[] { "water_2l" }, r.Extra);
            CollectionAssert.Contains(r.Tips, "water_point");
        }

        [Test]
        public void Evac_counts_water_in_litres_and_food_in_units()
        {
            var g = Game(("water_500", 4), ("bars", 3));
            PackAll(g, ("water_500", 1), ("bars", 2));
            var r = BagDrill.EvalBag(R, g, "evac");
            Assert.That(r.Missing, Does.Contain("water").And.Contain("food"), "half a litre is not enough water");
            PackAll(g, ("water_500", 3), ("canned", 1));
            r = BagDrill.EvalBag(R, g, "evac");
            Assert.That(r.Covered, Does.Contain("water").And.Contain("food"));
            AssertFacts(r.Tips);
        }

        [Test]
        public void Every_golden_bag_is_judged_like_the_original()
        {
            var cases = (JArray)JObject.Parse(Fixture("golden-bag.json"))["cases"];
            Assert.That(cases.Count, Is.EqualTo(42));
            int ok = 0;
            foreach (var c in cases)
            {
                var g = SaveCodec.Load(c["save"].ToString(), Db);
                foreach (var ctx in BagDrill.Goals.Keys)
                {
                    var e = c["evals"][ctx];
                    var r = BagDrill.EvalBag(R, g, ctx);
                    var actual = new JObject
                    {
                        ["ok"] = r.Ok, ["weightKg"] = r.WeightKg, ["volumeL"] = r.VolumeL, ["covered"] = new JArray(r.Covered), ["missing"] = new JArray(r.Missing),
                        ["extra"] = new JArray(r.Extra), ["unready"] = new JArray(r.Unready), ["tips"] = new JArray(r.Tips),
                    };
                    AssertSameJson(actual, e, $"seed {g.Seed} {ctx}");
                    if (r.Ok) ok++;
                }
            }
            Assert.AreEqual(2, ok, "the two passing bags of the original tests");
        }

        [Test]
        public void Finishing_the_alert_bag_applies_it_and_says_what_is_missing()
        {
            var g = Game(("water_500", 2));
            var target = new Dictionary<string, int> { ["water_500"] = 2, ["water_2l"] = 1, ["meds"] = 1, ["photo_album"] = 1 };
            var (ev, toast) = BagDrill.FinishAlertBag(R, g, target);
            Assert.AreEqual(2, g.Items["water_500"].Bag);
            Assert.AreEqual(1, g.Items["water_2l"].Bag);
            Assert.IsTrue(g.Knows("bagPacked"));
            Assert.AreEqual(1, g.Skill["bag"].Count, "one encounter: the next bag is a hint stage");
            Assert.AreEqual("partial", (string)g.Skill["bag"][0]["result"]);
            CollectionAssert.AreEqual(new[] { "food", "light", "info", "contacts", "warmth" }, ev.Missing);
            Assert.AreEqual("배낭을 현관에 두었다. 빠진 것: 조리 없이 먹을 음식, 켜지는 손전등, 라디오 또는 충전된 보조배터리, 연락처 카드 또는 서류 사본, 보온(은박담요·여벌 옷·우의)", toast);
            var bp = g.Events.Last();
            Assert.AreEqual("bag_packed", bp.Type);
            AssertJson(bp.Details["missing"], "['food','light','info','contacts','warmth']");
            var json = SaveCodec.Save(g);
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)), "the save stays valid");

            BagDrill.FinishAlertBag(R, g, new Dictionary<string, int>());
            Assert.IsFalse(g.Knows("bagPacked"), "an emptied bag is not packed");
            Assert.AreEqual(0, g.Items.Values.Sum(e => e.Bag));
        }
    }

    public class BagScreenModelTests
    {
        [Test]
        public void Rows_show_battery_charge_dates_and_tags_like_the_original()
        {
            var R = Rules;
            var g = R.CreateGame(1);
            g.Money = 1_000_000;
            R.Buy(g, Cart(("batteries_aa", 1), ("gas_stove", 1)), R.MartPrices());
            R.Trade(g, Cart(("ramen", 1)), Cart(("batteries_old", 1)), "x");
            R.Check(g, "flashlight");
            R.DrainCharge(g, "flashlight", 12.5);
            var rows = BagDrill.BagItems(g, Db, _ => "tip");
            var byId = rows.ToDictionary(r => r.Id);
            Assert.AreEqual("건전지 88%", BagDrill.ItemNote(byId["flashlight"]), "87.5 rounds up like Math.round");
            Assert.AreEqual("건전지 30%", BagDrill.ItemNote(byId["batteries_old"]), "a half-used pack");
            Assert.AreEqual("", BagDrill.ItemNote(byId["water_2l"]));
            Assert.AreEqual("기한 확인 필요", BagDrill.ItemNote(byId["old_bars"]));
            CollectionAssert.AreEqual(new[] { "food" }, BagDrill.ShownTags(byId["old_bars"]), "the expired badge only once the date was read");
            g.SetKnow("expiryChecked", true);
            byId = BagDrill.BagItems(g, Db, _ => "tip").ToDictionary(r => r.Id);
            Assert.AreEqual("기한 지남", BagDrill.ItemNote(byId["old_bars"]));
            CollectionAssert.AreEqual(new[] { "food", "expired" }, BagDrill.ShownTags(byId["old_bars"]));
            CollectionAssert.AreEqual(new[] { "fire", "fire" }.Distinct(), BagDrill.ShownTags(byId["gas_stove"]), "category and risk are both 'fire': one badge");
            Assert.IsFalse(rows.Any(r => r.Id == "blackout_curtain"), "fixtures are not listed");
            Assert.AreEqual("8.0kg", BagDrill.FormatKg(8));
            Assert.AreEqual("warn", BagDrill.MeterState(7, 8));
            Assert.AreEqual("over", BagDrill.MeterState(8.1, 8));
        }

        [Test]
        public void A_unit_goes_in_only_while_the_grid_has_room_and_empty_brings_all_home()
        {
            var R = Rules;
            var g = R.CreateGame(1);
            g.Money = 1_000_000;
            R.Buy(g, Cart(("gas_stove", 3), ("bars", 2)), R.MartPrices());
            var m = new BagPackingModel(BagDrill.BagItems(g, Db, _ => ""), Db);
            Assert.AreEqual(2, m.Move("gas_stove", true, int.MaxValue), "two 4x3 stoves fill the 10x5 grid; a third does not fit");
            Assert.AreEqual(1, m.HomeOf("gas_stove"));
            Assert.AreEqual(2, m.Move("bars", true, 2), "small things still fit around them");
            Assert.AreEqual(0, m.Move("gas_stove", true));
            Assert.AreEqual(1, m.Move("gas_stove", false));
            Assert.AreEqual(1, m.Move("gas_stove", true), "taking one out made room");
            var (w, _) = m.Load();
            Assert.AreEqual(2 * 1.8 + 2 * 0.1, w, 1e-9);
            m.Empty();
            Assert.AreEqual(0, m.Result().Values.Sum());
            Assert.AreEqual(3, m.HomeOf("gas_stove"));
            Assert.AreEqual(0, g.Items["gas_stove"].Bag, "nothing is applied to the game until the screen closes");
        }

        [Test]
        public void Goal_ticks_follow_the_bag_and_the_free_stage_shows_none()
        {
            var R = Rules;
            var g = R.CreateGame(1);
            var items = BagDrill.BagItems(g, Db, _ => "");
            var m = new BagPackingModel(items, Db);
            m.Move("water_2l", true);
            var guided = BagDrill.GoalStatus(BagDrill.Goals["alert"], "guided", m.BagOf, m.Items);
            Assert.AreEqual(true, guided[0].ok, "one 2 L bottle is 2 L of water");
            Assert.AreEqual(false, guided[2].ok);
            m.Move("flashlight", true);
            Assert.AreEqual(false, BagDrill.GoalStatus(BagDrill.Goals["alert"], "guided", m.BagOf, m.Items)[2].ok, "a flashlight without batteries is not a light");
            Assert.IsTrue(BagDrill.GoalStatus(BagDrill.Goals["alert"], "free", m.BagOf, m.Items).All(x => x.ok == null));
        }
    }
}
