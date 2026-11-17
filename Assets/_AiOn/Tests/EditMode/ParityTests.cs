using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Compares the C# port with the original JavaScript. Fixtures/golden-state.json was produced by running
    /// the original game/state.js and game/pack.js (regenerate with Tools/golden/make-golden.mjs).
    /// </summary>
    public class ParityTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-state.json"));

        [Test]
        public void Conditions_match_the_original_for_every_golden_seed()
        {
            var rows = (JArray)Golden["conditions"];
            Assert.That(rows.Count, Is.GreaterThan(200));
            foreach (var row in rows)
            {
                uint seed = GameRules.ToSeed((long)row["seed"]);
                AssertSameJson(JToken.FromObject(GameConditions.For(seed)), row["cond"], $"seed {seed}");
            }
        }

        [Test]
        public void A_scripted_run_ends_in_the_same_state_as_the_original()
        {
            var R = Rules;
            var mart = R.MartPrices();
            var exp = (JObject)Golden["scenario"];
            var steps = (JArray)exp["steps"];

            var g = R.CreateGame(11);
            R.Buy(g, Cart(("radio", 1), ("batteries_aa", 2), ("powerbank", 2), ("bars", 3)), mart);
            GameRules.Advance(g, 95);
            GameRules.SpendSlot(g, "mart", 2);
            R.Pack(g, "water_2l", 2); R.Pack(g, "bars", 3); R.Pack(g, "powerbank", 2); R.Pack(g, "meds", 1);
            R.SetPocket(g, "powerbank", 2);

            var packed = steps.First(s => (string)s["label"] == "packed");
            var load = R.BagLoad(g);
            Assert.AreEqual((double)packed["bagLoad"]["weightKg"], load.WeightKg);
            Assert.AreEqual((double)packed["bagLoad"]["volumeL"], load.VolumeL);
            Assert.AreEqual((bool)packed["bagLoad"]["over"]["weight"], load.OverWeight);
            Assert.AreEqual((bool)packed["bagLoad"]["over"]["volume"], load.OverVolume);
            var cells = R.BagCells(g);
            Assert.AreEqual((int)packed["bagCells"]["used"], cells.Used);
            Assert.AreEqual((bool)packed["bagCells"]["fits"], cells.Fits);
            Assert.AreEqual((double)packed["pocketLoad"], R.PocketLoad(g));
            AssertLayout(packed["layout"], BagPacker.Pack(BagPacker.BagList(g, Db), Db));

            R.Check(g, "radio"); R.Check(g, "flashlight");
            Assert.AreEqual((double)exp["drained"], R.DrainCharge(g, "flashlight", 12.5));
            R.Trade(g, Cart(("ramen", 2)), Cart(("batteries_old", 1)), "clerk");
            R.Consume(g, "bars", 1, "bag");
            GameRules.Note(g, "  급수차 9시  ", "official", "centerNotice");
            GameRules.Adjust(g, "tension", 10);
            GameRules.Advance(g, 600, awake: false);
            g.SetKnow("bagOn", true);
            Assert.AreEqual((double)exp["speed"], R.SpeedMul(g));
            SaveCodec.MakeCheckpoint(g, "dday");
            R.Buy(g, Cart(("water_500", 3)), mart);
            GameRules.Advance(g, 30.4);
            Assert.AreEqual((string)exp["clock"], GameRules.FormatClock(g));

            AssertSameJson(SaveCodec.ToJObject(g), exp["final"], "final save");
        }

        [Test]
        public void The_original_final_save_loads_and_resaves_unchanged()
        {
            var original = Golden["scenario"]["final"].ToString();
            var g = SaveCodec.Load(original, Db);
            AssertSameJson(SaveCodec.ToJObject(g), JToken.Parse(original));
        }

        [Test]
        public void An_overfull_bag_packs_and_overflows_like_the_original()
        {
            var lines = new[]
            {
                new BagLine("gas_stove", 2), new BagLine("water_2l", 3), new BagLine("first_aid", 2), new BagLine("radio", 2),
                new BagLine("bars", 9, 9), new BagLine("blackout_curtain", 1), new BagLine("clothes", 1),
            };
            AssertLayout(Golden["bigPack"], BagPacker.Pack(lines, Db));
        }

        static void AssertLayout(JToken expected, PackResult actual)
        {
            var placed = new JArray(actual.Placed.Select(p => new JObject
            { ["id"] = p.Id, ["x"] = p.X, ["y"] = p.Y, ["w"] = p.W, ["h"] = p.H, ["rotated"] = p.Rotated }));
            JArray Counts(System.Collections.Generic.IEnumerable<ItemCount> l) => new(l.Select(c => new JObject { ["id"] = c.Id, ["n"] = c.N }));
            AssertSameJson(placed, expected["placed"], "placed");
            AssertSameJson(Counts(actual.Pocket), expected["pocket"], "pocket");
            AssertSameJson(Counts(actual.Overflow), expected["overflow"], "overflow");
            Assert.AreEqual((int)expected["used"], actual.Used);
            Assert.AreEqual((int)expected["free"], actual.Free);
            Assert.AreEqual((double)expected["pocketL"], actual.PocketL);
            Assert.AreEqual((bool)expected["fits"], actual.Fits);
        }
    }
}
