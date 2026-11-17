using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>Port of the original tests/state.test.mjs, case for case.</summary>
    public class GameStateTests
    {
        GameRules R;
        Dictionary<string, int> mart;

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            mart = R.MartPrices();
        }

        static Exception Throws(TestDelegate act, string messagePart = null)
        {
            var ex = Assert.Catch(act);
            if (messagePart != null) StringAssert.Contains(messagePart, ex.Message);
            return ex;
        }

        [Test]
        public void CreateGame_starts_from_the_spec_state_and_START_HOME()
        {
            var g = R.CreateGame(7);
            Assert.AreEqual(2, g.V);
            Assert.AreEqual(7u, g.Seed);
            Assert.AreEqual("D-3 17:30", GameRules.FormatClock(g));
            AssertJson(g.Slots, "{total:6,used:0,done:[]}");
            Assert.AreEqual(150000, g.Money);
            AssertJson(g.Stats, "{health:100,thirst:10,fatigue:20,tension:25}");
            CollectionAssert.AreEquivalent(Db.startHome.Select(s => s.item.id), g.Items.Keys);
            foreach (var s in Db.startHome)
                AssertJson(g.Items[s.item.id], $"{{home:{s.count},bag:0,bought:0,checked:false,used:0}}", s.item.id);
            Assert.IsFalse(R.Usable(g, "flashlight"), "owned flashlight has no batteries");
            Assert.IsTrue(R.Usable(g, "water_2l"));
            Assert.IsFalse(R.Usable(g, "radio"));
            AssertSameJson(JToken.FromObject(g.Cond), JToken.FromObject(GameConditions.For(7)));
            Assert.AreEqual(JTokenType.Null, g.Know["meetPoint"].Type);
            AssertJson(g.Rel["seoyeon"], "{trust:0,contact:null}");
            CollectionAssert.AreEqual(new[] { "radio", "bag", "route", "extinguisher", "docs", "info" }, g.Skill.Keys);
            Assert.IsEmpty(g.Ledger);
            Assert.IsNull(g.Checkpoint);
            Assert.DoesNotThrow(() => R.CreateGame(), "random seed when none is given");
        }

        [Test]
        public void Conditions_are_deterministic_and_keep_the_safe_facts_fixed()
        {
            var seen = new HashSet<string>();
            for (uint s = 1; s <= 500; s++)
            {
                var a = GameConditions.For(s);
                var json = JToken.FromObject(a).ToString();
                Assert.AreEqual(json, JToken.FromObject(GameConditions.For(s)).ToString());
                CollectionAssert.Contains(a.Closures.DDay, "yeonmuCenter");
                Assert.IsTrue(a.EastGateClosed);
                Assert.IsFalse(a.RumorTrue);
                Assert.AreEqual("20:16", a.AccessOpensAt);
                Assert.AreEqual("cafe", a.SeoyeonAt);
                Assert.That(a.CommsWeakAt, Is.GreaterThan(19 * 60 + 40).And.LessThan(20 * 60 + 5));
                CollectionAssert.Contains(a.SoldOut.Late, "water");
                seen.Add(json);
            }
            Assert.That(seen.Count, Is.GreaterThan(10), "seeds vary the situation");
        }

        [Test]
        public void RecordEvent_numbers_events_and_stamps_time_and_chapter()
        {
            var g = R.CreateGame(1);
            var a = GameRules.RecordEvent(g, "payoff", new JObject { ["prep"] = "x", ["moment"] = "y" });
            var b = GameRules.RecordEvent(g, "help");
            Assert.AreEqual(1, a.Seq);
            Assert.AreEqual(2, b.Seq);
            Assert.AreEqual("prologue", a.Chapter);
            Assert.AreEqual(-3, a.Day);
            AssertJson(b.Details, "{}");
            Throws(() => GameRules.RecordEvent(g, ""));
        }

        [Test]
        public void FormatClock_and_Hhmm()
        {
            var g = R.CreateGame(1);
            g.Day = 0; g.Minute = 20 * 60 + 5;
            Assert.AreEqual("D-day 20:05", GameRules.FormatClock(g));
            g.Day = 12; g.Minute = 7;
            Assert.AreEqual("D+12 00:07", GameRules.FormatClock(g));
            Assert.AreEqual("15:20", GameRules.Hhmm(15 * 60 + 20));
        }

        [Test]
        public void Advance_moves_the_clock_across_days_and_drifts_stats()
        {
            var g = R.CreateGame(1);
            GameRules.Advance(g, 60);
            Assert.AreEqual("D-3 18:30", GameRules.FormatClock(g));
            Assert.AreEqual(13, g.Stats.Thirst);
            Assert.AreEqual(22, g.Stats.Fatigue);
            for (int i = 0; i < 10; i++) GameRules.Advance(g, 2); // small steps add up like one long step
            Assert.AreEqual(14, g.Stats.Thirst);
            GameRules.Advance(g, 6 * 60);
            Assert.AreEqual("D-2 00:50", GameRules.FormatClock(g));
            var f = g.Stats.Fatigue;
            GameRules.Advance(g, 120, awake: false);
            Assert.AreEqual(f, g.Stats.Fatigue);
            GameRules.Advance(g, 0);
            Throws(() => GameRules.Advance(g, -5));
            Throws(() => GameRules.Advance(g, double.NaN));
        }

        [Test]
        public void Adjust_clamps_to_0_100_and_rejects_unknown_stats()
        {
            var g = R.CreateGame(1);
            Assert.AreEqual(100, GameRules.Adjust(g, "health", 50));
            Assert.AreEqual(0, GameRules.Adjust(g, "thirst", -99));
            Assert.AreEqual(35, GameRules.Adjust(g, Stat.Tension, 10));
            Throws(() => GameRules.Adjust(g, "mood", 1));
            Throws(() => GameRules.Adjust(g, "health", double.NaN));
        }

        [Test]
        public void SpendSlot_spends_until_the_planner_is_full()
        {
            var g = R.CreateGame(1);
            Assert.AreEqual(4, GameRules.SpendSlot(g, "mart", 2));
            Assert.AreEqual(3, GameRules.SpendSlot(g, "bag"));
            CollectionAssert.AreEqual(new[] { "mart", "bag" }, g.Slots.Done);
            Throws(() => GameRules.SpendSlot(g, "x", 4), "not enough slots");
            Throws(() => GameRules.SpendSlot(g, "x", 0));
            Assert.AreEqual(3, g.Slots.Used);
            Assert.AreEqual(2, g.Events.Count(e => e.Type == "slot"));
        }

        [Test]
        public void Buy_checks_stock_and_money_adds_to_home_and_bought()
        {
            var g = R.CreateGame(1);
            Assert.AreEqual(24000 + 9000, R.Buy(g, Cart(("radio", 1), ("batteries_aa", 2), ("water_2l", 0)), mart));
            Assert.AreEqual(150000 - 33000, g.Money);
            AssertJson(g.Items["radio"], "{home:1,bag:0,bought:1,checked:false,used:0}");
            Assert.AreEqual(2, g.Items["water_2l"].Home);
            Throws(() => R.Buy(g, Cart(("radio", 1)), R.StorePrices()), "not in stock");
            Throws(() => R.Buy(g, Cart(("nope", 1)), mart), "unknown item");
            Throws(() => R.Buy(g, Cart(("water_2l", -1)), mart));
            Throws(() => R.Buy(g, Cart(("gas_stove", 10)), mart), "not enough money");
            Assert.AreEqual(117000, g.Money, "failed purchases change nothing");
            Assert.AreEqual(0, R.Buy(g, Cart(), mart));
            Assert.AreEqual(0, R.Buy(g, null, mart));
        }

        [Test]
        public void Cash_comes_from_an_ATM_and_lives_in_items()
        {
            var g = R.CreateGame(1);
            R.Buy(g, Cart(("cash", 1)), new Dictionary<string, int> { ["cash"] = Db.Get("cash").price });
            Assert.AreEqual(100000, g.Money);
            Assert.AreEqual(1, g.Items["cash"].Home);
            Assert.IsTrue(R.Usable(g, "cash"));
        }

        [Test]
        public void Pack_unpack_and_BagLoad()
        {
            var g = R.CreateGame(1);
            var empty = R.BagLoad(g);
            Assert.AreEqual((0.0, 0.0, false, false), (empty.WeightKg, empty.VolumeL, empty.OverWeight, empty.OverVolume));
            R.Pack(g, "water_2l", 2);
            R.Pack(g, "clothes");
            Assert.AreEqual(0, g.Items["water_2l"].Home);
            Assert.AreEqual(2, g.Items["water_2l"].Bag);
            var l = R.BagLoad(g);
            Assert.AreEqual((4.7, 7.4, false, false), (l.WeightKg, l.VolumeL, l.OverWeight, l.OverVolume));
            Throws(() => R.Pack(g, "water_2l", 1), "only 0");
            Throws(() => R.Pack(g, "radio", 1), "only 0");
            Throws(() => R.Pack(g, "nope", 1), "unknown item");
            R.Unpack(g, "water_2l");
            Assert.AreEqual(1, g.Items["water_2l"].Home);
            R.Buy(g, Cart(("water_2l", 4)), mart);
            R.Pack(g, "water_2l", 5);
            Assert.IsTrue(R.BagLoad(g).OverWeight);
            Assert.That(R.BagLoad(g).WeightKg, Is.GreaterThan(Db.bagLimitKg));
        }

        [Test]
        public void Check_turns_owned_into_usable_batteries_are_used_up()
        {
            var g = R.CreateGame(1);
            var none = R.Check(g, "radio");
            Assert.AreEqual((false, "가지고 있지 않습니다."), (none.Ok, none.Reason));
            Assert.IsFalse(R.Check(g, "flashlight").Ok, "no batteries yet");
            R.Buy(g, Cart(("batteries_aa", 1), ("radio", 1), ("powerbank", 1)), mart);
            Assert.IsTrue(R.Check(g, "flashlight").Ok);
            Assert.IsTrue(R.Usable(g, "flashlight"));
            Assert.AreEqual(0, g.Items["batteries_aa"].Home);
            Assert.AreEqual(1, g.Items["batteries_aa"].Used);
            Assert.IsFalse(R.Check(g, "radio").Ok, "the one pack is gone");
            Assert.IsTrue(R.Check(g, "flashlight").Ok, "checking twice is free");
            Assert.IsTrue(R.Check(g, "powerbank").Ok);
            Assert.IsTrue(R.Usable(g, "powerbank"));
            Assert.IsTrue(R.Check(g, "water_2l").Ok);
            Throws(() => R.Check(g, "nope"));
        }

        [Test]
        public void Consume_and_Use()
        {
            var g = R.CreateGame(1);
            R.Pack(g, "ramen", 2);
            R.Consume(g, "ramen", 1, "bag");
            R.Consume(g, "ramen", 3);
            AssertJson(g.Items["ramen"], "{home:0,bag:1,bought:0,checked:false,used:4}");
            Throws(() => R.Consume(g, "ramen", 1), "only 0");
            Throws(() => R.Consume(g, "ramen", 1, "car"));
            Throws(() => R.Consume(g, "ramen", 0, "bag"));
            Throws(() => R.Use(g, "flashlight"), "not usable");
            R.Buy(g, Cart(("batteries_aa", 1)), mart);
            R.Check(g, "flashlight");
            R.Use(g, "flashlight");
            Assert.AreEqual(1, g.Items["flashlight"].Used);
            Assert.AreEqual(1, g.Items["flashlight"].Home);
        }

        [Test]
        public void Note_records_labelled_info_once()
        {
            var g = R.CreateGame(1);
            var n = GameRules.Note(g, "  급수차 9시  ", "official", "centerNotice");
            AssertJson(n, "{chapter:'prologue',day:-3,minute:1050,text:'급수차 9시',label:'official',place:'centerNotice'}");
            Assert.AreSame(n, GameRules.Note(g, "급수차 9시", "official"));
            GameRules.Note(g, "급수차 9시", "rumor");
            Assert.AreEqual(2, g.Info.Count);
            Throws(() => GameRules.Note(g, "", "self"));
            Throws(() => GameRules.Note(g, "x", "gossip"));
        }

        [Test]
        public void SpeedMul_stays_within_0_6_to_1_and_falls_with_fatigue_and_load()
        {
            var g = R.CreateGame(1);
            var fresh = R.SpeedMul(g);
            Assert.AreEqual(1, fresh);
            GameRules.Adjust(g, "fatigue", 80);
            var tired = R.SpeedMul(g);
            Assert.That(tired, Is.LessThan(fresh).And.GreaterThanOrEqualTo(0.6));
            R.Buy(g, Cart(("water_2l", 20)), mart);
            R.Pack(g, "water_2l", 22);
            Assert.AreEqual(tired, R.SpeedMul(g), "a bag left by the door slows nobody");
            g.SetKnow("bagOn", true);
            Assert.AreEqual(0.6, R.SpeedMul(g));
        }

        [Test]
        public void Save_load_round_trip_and_validation()
        {
            var g = R.CreateGame(3);
            R.Buy(g, Cart(("radio", 1)), mart);
            GameRules.Note(g, "라디오 샀음", "self");
            SaveCodec.MakeCheckpoint(g, "ch1");
            var json = SaveCodec.Save(g);
            var back = SaveCodec.Load(json, Db);
            Assert.AreEqual(json, SaveCodec.Save(back));
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(g, Db)), "accepts a state too");
            Throws(() => SaveCodec.Load("{oops", Db), "not JSON");
            Throws(() => SaveCodec.Load("null", Db), "invalid save");

            string Bad(Action<JObject> f) { var o = JObject.Parse(json); f(o); return o.ToString(); }
            Throws(() => SaveCodec.Load(Bad(o => o["v"] = 1), Db), "invalid save");
            Throws(() => SaveCodec.Load(Bad(o => o["stats"]["health"] = 300), Db), "stats");
            Throws(() => SaveCodec.Load(Bad(o => o["items"] = JObject.Parse("{radio:{home:-1,bag:0,bought:0,used:0,checked:false}}")), Db), "item radio");
            Throws(() => SaveCodec.Load(Bad(o => o["info"] = JArray.Parse("[{text:1,label:'self'}]")), Db), "info");
            var old = SaveCodec.Load(Bad(o => o["items"]["retired_item"] = JObject.Parse("{home:1}")), Db);
            Assert.IsFalse(old.Items.ContainsKey("retired_item"), "items removed from the catalogue are dropped");
        }

        [Test]
        public void Checkpoint_and_Restore_roll_back_in_place()
        {
            var g = R.CreateGame(5);
            var reference = g;
            SaveCodec.MakeCheckpoint(g, "dday");
            R.Buy(g, Cart(("radio", 1)), mart);
            GameRules.Advance(g, 90);
            GameRules.RecordEvent(g, "payoff");
            SaveCodec.Restore(g);
            Assert.AreSame(reference, g);
            Assert.AreEqual(150000, g.Money);
            Assert.IsFalse(g.Items.ContainsKey("radio"));
            Assert.AreEqual(0, g.Events.Count);
            Assert.AreEqual("dday", g.Checkpoint.Key);
            g.Money = 1;
            SaveCodec.Restore(g);
            Assert.AreEqual(150000, g.Money, "can retry twice from the same checkpoint");
            Throws(() => SaveCodec.Restore(R.CreateGame(1)), "no checkpoint");
            Throws(() => SaveCodec.MakeCheckpoint(g, ""));
        }

        [Test]
        public void A_broken_leg_slows_the_run_for_good()
        {
            var g = R.CreateGame(4);
            var baseSpeed = R.SpeedMul(g);
            g.Injury = new List<string> { "leg" };
            Assert.AreEqual(Num.Round2(baseSpeed * 0.7), R.SpeedMul(g));
            CollectionAssert.AreEqual(new[] { "leg" }, SaveCodec.Load(SaveCodec.Save(g), Db).Injury);
        }

        [Test]
        public void Migrate_a_pre_ledger_save_and_its_nested_checkpoint_still_load()
        {
            var raw = JObject.Parse(Fixture("save-v2-pre-ledger.json"));
            Assert.IsNull(raw["skill"]["info"], "fixture really is the old shape");
            Assert.IsNull(raw["ledger"]);
            Assert.IsNull(raw["checkpoint"]["state"]["skill"]["info"]);
            var g = SaveCodec.Load(raw.ToString(), Db);
            Assert.IsEmpty(g.Skill["info"]);
            Assert.IsEmpty(g.Ledger);
            Assert.IsEmpty(g.Checkpoint.State.Skill["info"]);
            Assert.IsEmpty(g.Checkpoint.State.Ledger);
            Assert.AreEqual(1, g.Skill["bag"].Count, "old ladder kept");
            Assert.IsTrue(R.Usable(g, "radio"), "checked before charges existed: still usable");
            Assert.AreEqual(90, R.DrainCharge(g, "radio", 10), "and counts as full");
            SaveCodec.Restore(g);
            var json = SaveCodec.Save(g);
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)));
            Assert.IsNull(SaveCodec.Migrate(null));

            var partial = (JObject)raw.DeepClone();
            partial["skill"] = JObject.Parse("{radio:[]}");
            partial["checkpoint"] = JValue.CreateNull();
            Assert.IsEmpty(SaveCodec.Load(partial.ToString(), Db).Skill["info"], "any missing ladder is filled");
            var noSkill = (JObject)raw.DeepClone();
            noSkill["skill"] = JValue.CreateNull();
            Throws(() => SaveCodec.Load(noSkill.ToString(), Db), "skill");
        }

        [Test]
        public void Validate_charge_0_100_pocket_le_bag_ledger_rows()
        {
            var g = R.CreateGame(2);
            string Bad(Action<JObject> f) { var o = JObject.Parse(SaveCodec.Save(g)); f(o); return o.ToString(); }
            Throws(() => SaveCodec.Load(Bad(o => o["items"]["flashlight"]["charge"] = 150), Db), "charge");
            Throws(() => SaveCodec.Load(Bad(o => o["items"]["meds"]["pocket"] = 1), Db), "pocket");
            Throws(() => SaveCodec.Load(Bad(o => o["ledger"] = JArray.Parse("[{day:'x'}]")), Db), "ledger");
            Throws(() => SaveCodec.Load(Bad(o => o["ledger"] = new JObject()), Db), "ledger");
            Assert.IsNotNull(SaveCodec.Load(Bad(o =>
            {
                o["items"]["meds"]["bag"] = 1; o["items"]["meds"]["pocket"] = 1; o["items"]["flashlight"]["charge"] = 30;
            }), Db));
        }

        [Test]
        public void Check_sets_charge_from_the_pack_DrainCharge_runs_it_down_to_0()
        {
            var g = R.CreateGame(1);
            g.Money = 1_000_000;
            R.Buy(g, Cart(("radio", 1), ("batteries_aa", 1)), mart);
            R.Trade(g, Cart(("ramen", 1)), Cart(("batteries_old", 1)), "trader");
            R.Check(g, "radio");
            Assert.AreEqual(100, g.Items["radio"].Charge, "a new pack first");
            Assert.AreEqual(0, g.Items["batteries_aa"].Home);
            R.Check(g, "flashlight");
            Assert.AreEqual(30, g.Items["flashlight"].Charge, "then the half-used one");
            Assert.AreEqual(1, g.Items["batteries_old"].Used);
            Assert.IsTrue(R.Check(g, "flashlight").Ok);
            Assert.AreEqual(17.5, R.DrainCharge(g, "flashlight", 12.5));
            Assert.AreEqual(0, R.DrainCharge(g, "flashlight", 50));
            Assert.IsFalse(R.Usable(g, "flashlight"), "dead batteries: not usable");
            Assert.AreEqual(1, g.Events.Count(e => e.Type == "battery_dead"));
            Assert.AreEqual(0, R.DrainCharge(g, "flashlight", 5), "a dead device stays at 0");
            Assert.IsFalse(R.Check(g, "flashlight").Ok, "no packs left");
            R.Buy(g, Cart(("batteries_aa", 1)), mart);
            R.Check(g, "flashlight");
            Assert.AreEqual(100, g.Items["flashlight"].Charge, "a spare pack revives it");
            Throws(() => R.DrainCharge(g, "water_2l", 1), "batteries");
            Throws(() => R.DrainCharge(g, "radio", -1));
            Throws(() => R.Check(g, "radio", "water_2l"), "battery pack");
            AssertJson(new JArray(g.Events.Where(e => e.Type == "check").Select(e => e.Details)),
                "[{id:'radio',charge:100},{id:'flashlight',charge:30},{id:'flashlight',charge:100}]");
        }

        [Test]
        public void Trade_is_all_or_nothing_and_records_the_barter()
        {
            var g = R.CreateGame(1);
            g.Money = 1_000_000;
            R.Buy(g, Cart(("bars", 3)), mart);
            R.Pack(g, "bars", 2);
            Throws(() => R.Trade(g, Cart(("bars", 4)), Cart(("batteries_aa", 1)), "trader"), "only 3");
            Throws(() => R.Trade(g, Cart(("bars", 1)), Cart(("nope", 1)), "trader"), "unknown item");
            Throws(() => R.Trade(g, Cart(("bars", 1)), Cart(), "trader"));
            Throws(() => R.Trade(g, Cart(("bars", 1)), Cart(("batteries_aa", 1)), ""));
            AssertJson(g.Items["bars"], "{home:1,bag:2,bought:3,checked:false,used:0}", "failed trades change nothing");
            var e = R.Trade(g, Cart(("bars", 3)), Cart(("batteries_old", 1)), "trader");
            AssertJson(g.Items["bars"], "{home:0,bag:0,bought:3,checked:false,used:3}", "home first, then the bag");
            AssertJson(g.Items["batteries_old"], "{home:1,bag:0,bought:0,checked:false,used:0}");
            AssertJson(e.Details, "{give:{bars:3},get:{batteries_old:1},from:'trader'}");
            Assert.AreEqual("trade", e.Type);
        }

        [Test]
        public void Front_pocket_pocket_items_only_up_to_the_limit_emptied_first_when_the_bag_shrinks()
        {
            var g = R.CreateGame(1);
            g.Money = 1_000_000;
            R.Buy(g, Cart(("powerbank", 4)), mart);
            R.Pack(g, "powerbank", 4); R.Pack(g, "meds"); R.Pack(g, "water_2l");
            R.SetPocket(g, "powerbank", 3);
            Assert.AreEqual(0.45, R.PocketLoad(g));
            Throws(() => R.SetPocket(g, "powerbank", 4), "pocket full");
            Throws(() => R.SetPocket(g, "meds", 1), "pocket full");
            Throws(() => R.SetPocket(g, "water_2l", 1), "does not fit");
            Throws(() => R.SetPocket(g, "bars", 1), "only 0");
            R.SetPocket(g, "powerbank", 2);
            R.SetPocket(g, "meds", 1);
            Assert.IsTrue(GameRules.InPocket(g, "meds"));
            R.Unpack(g, "powerbank", 3);
            Assert.AreEqual(1, g.Items["powerbank"].Pocket, "pocket never holds more than the bag");
            R.Consume(g, "meds", 1, "bag");
            Assert.IsFalse(GameRules.InPocket(g, "meds"));
            Assert.IsNull(g.Items["meds"].Pocket);
            Assert.IsFalse(JObject.Parse(SaveCodec.Save(g))["items"]["meds"].Value<JObject>().ContainsKey("pocket"));
            var json = SaveCodec.Save(g);
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)));
        }

        [Test]
        public void Fixtures_stay_at_home()
        {
            var g = R.CreateGame(1);
            R.Buy(g, Cart(("blackout_curtain", 1)), mart);
            Assert.IsFalse(Db.Get("blackout_curtain").Packable);
            Assert.IsTrue(Db.Get("water_2l").Packable);
            Throws(() => R.Pack(g, "blackout_curtain"), "fixture");
        }

        [Test]
        public void A_looter_given_a_pocketed_item_through_consume_bag_keeps_a_save_that_loads()
        {
            var g = R.CreateGame(3);
            g.Items["bars"] = new ItemEntry { Bag = 1 };
            R.SetPocket(g, "bars", 1);
            var raw = R.CreateGame(3);
            raw.Items["bars"] = new ItemEntry { Bag = 1 };
            R.SetPocket(raw, "bars", 1);
            raw.Items["bars"].Bag -= 1; // the old danger.js give: pocket > bag
            Throws(() => SaveCodec.Load(SaveCodec.Save(raw), Db), "pocket");
            R.Consume(g, "bars", 1, "bag");
            Assert.That(g.Items["bars"].Pocket ?? 0, Is.LessThanOrEqualTo(g.Items["bars"].Bag));
            Assert.AreEqual(1, g.Items["bars"].Used);
            var json = SaveCodec.Save(g);
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)));
        }
    }
}
