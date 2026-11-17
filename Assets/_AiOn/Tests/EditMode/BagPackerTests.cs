using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>Port of the original tests/pack.test.mjs.</summary>
    public class BagPackerTests
    {
        [Test]
        public void Every_packable_item_has_cells_that_hold_its_volume_and_the_grid_is_the_bag()
        {
            Assert.AreEqual(Db.bagLimitL, Db.bagGrid.x * Db.bagGrid.y * Db.cellL);
            foreach (var c in Db.items)
            {
                if (c.fixture) { Assert.IsFalse(c.HasCells, c.id); continue; }
                Assert.That(c.cells.x >= 1 && c.cells.y >= 1, c.id + " has cells");
                Assert.That(c.cells.x * c.cells.y * Db.cellL, Is.GreaterThanOrEqualTo(c.volumeL), $"{c.id}: {c.cells} for {c.volumeL} L");
                if (c.pocket) Assert.That(c.volumeL, Is.LessThanOrEqualTo(0.2), c.id + " is small enough for the pocket");
            }
        }

        [Test]
        public void First_fit_decreasing_is_deterministic_and_biggest_first()
        {
            var list = new List<BagLine> { new("bars", 3), new("water_2l", 2), new("gas_stove", 1), new("radio", 1) };
            var a = BagPacker.Pack(list, Db);
            var b = BagPacker.Pack(list, Db);
            CollectionAssert.AreEqual(a.Placed, b.Placed);
            Assert.IsTrue(a.Fits);
            Assert.AreEqual(new Placement("gas_stove", 0, 0, 4, 3, false), a.Placed[0]);
            Assert.AreEqual(new Placement("water_2l", 4, 0, 3, 2, false), a.Placed[1]);
            Assert.AreEqual(12 + 6 + 6 + 2 + 3, a.Used);
            Assert.AreEqual(50 - a.Used, a.Free);
            var seen = new HashSet<(int, int)>();
            foreach (var p in a.Placed)
                for (int y = p.Y; y < p.Y + p.H; y++)
                    for (int x = p.X; x < p.X + p.W; x++)
                    {
                        Assert.That(x < 10 && y < 5);
                        Assert.IsTrue(seen.Add((x, y)), $"overlap at {x},{y}");
                    }
        }

        [Test]
        public void Rotation_is_used_only_when_needed()
        {
            var r = BagPacker.Pack(new[] { new BagLine("gas_stove") }, Db, cols: 3, rows: 4);
            CollectionAssert.AreEqual(new[] { new Placement("gas_stove", 0, 0, 3, 4, true) }, r.Placed);
            Assert.IsFalse(BagPacker.Pack(new[] { new BagLine("gas_stove") }, Db, cols: 3, rows: 3).Fits);
            var full = BagPacker.Pack(new[] { new BagLine("gas_stove", 3) }, Db);
            CollectionAssert.AreEqual(new[] { new ItemCount("gas_stove", 1) }, full.Overflow, "a third stove does not fit");
            Assert.IsFalse(BagPacker.CanAdd(new[] { new BagLine("gas_stove", 2) }, "gas_stove", Db));
            Assert.IsTrue(BagPacker.CanAdd(new[] { new BagLine("gas_stove", 2) }, "bars", Db));
        }

        [Test]
        public void The_front_pocket_takes_pocket_items_up_to_its_limit_the_rest_goes_in_the_main_grid()
        {
            var r = BagPacker.Pack(new[] { new BagLine("powerbank", 4, 4), new BagLine("contact_card", 1, 1), new BagLine("water_500", 1, 1) }, Db);
            Assert.That(r.PocketL, Is.LessThanOrEqualTo(Db.pocketL));
            CollectionAssert.AreEqual(new[] { new ItemCount("powerbank", 3), new ItemCount("contact_card", 1) }, r.Pocket);
            Assert.AreEqual(1, r.Placed.Count(p => p.Id == "powerbank"));
            Assert.AreEqual(1, r.Placed.Count(p => p.Id == "water_500"), "not a pocket item");
            Assert.Catch(() => BagPacker.Pack(new[] { new BagLine("nope") }, Db));
            Assert.Catch(() => BagPacker.Pack(new[] { new BagLine("bars", 1, 2) }, Db));
            CollectionAssert.AreEqual(new[] { new ItemCount("blackout_curtain", 1) }, BagPacker.Pack(new[] { new BagLine("blackout_curtain") }, Db).Overflow);
        }

        [Test]
        public void BagList_reads_the_bag_and_pocket_from_the_state()
        {
            var R = Rules;
            var g = R.CreateGame(1);
            R.Pack(g, "meds"); R.Pack(g, "water_2l", 2); R.SetPocket(g, "meds", 1);
            var list = BagPacker.BagList(g, Db);
            Assert.AreEqual(new[] { ("water_2l", 2, 0), ("meds", 1, 1) }, list.Select(l => (l.Id, l.N, l.Pocket)).ToArray());
            var r = BagPacker.Pack(list, Db);
            CollectionAssert.AreEqual(new[] { new ItemCount("meds", 1) }, r.Pocket);
            Assert.IsTrue(r.Fits);
        }
    }
}
