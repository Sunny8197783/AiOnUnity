// The interiors' live dressing (original levels/apartment.js stepLights, apartment-stock.js setStock, apartment-props.js
// candle, store.js clerk, office.js update and the prologue dashboard, shelter.js emergency supply) and the level point
// descriptions the exported layouts dropped (story/world.js + point.desc).
using System.Linq;
using AiOn.Levels.Interiors;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using L = AiOn.Levels.Interiors.InteriorLogic;

namespace AiOn.Tests
{
    public class InteriorLogicTests
    {
        static L.StockKind Kind(string id) => L.Kinds.First(k => k.Id == id);

        [Test]
        public void StockKindsHaveTheOriginalSlotCounts()
        {
            var want = new (string, int)[]
            {
                ("water_2l", 40), ("water_500", 15), ("ramen", 24), ("rice_instant", 36), ("canned", 45), ("batteries_aa", 12), ("candles", 12),
                ("gas_stove", 3), ("butane", 20), ("first_aid", 4), ("tissue", 18), ("blanket", 6), ("wipes", 3), ("water_jug", 2),
            };
            Assert.AreEqual(want.Length, L.Kinds.Length);
            foreach (var (id, n) in want) Assert.AreEqual(n, L.Slots(Kind(id)).Count, id);
        }

        [Test]
        public void StockFillsTheLowestLayerFirstAndOverflowsTwoLitreBottlesOntoTheFloor()
        {
            var ramen = L.Slots(Kind("ramen"));
            Assert.IsTrue(ramen.Take(6).All(s => Mathf.Approximately(s.Pos.y, L.Shelf[1])));
            Assert.AreEqual(L.Shelf[1] + 0.056f, ramen[6].Pos.y, 1e-5);
            var water = L.Slots(Kind("water_2l"));
            Assert.IsTrue(water.Take(16).All(s => Mathf.Approximately(s.Pos.y, L.Shelf[0]) && s.Pos.x > L.StockFront && s.Pos.x < L.StockBack));
            Assert.AreEqual(-0.32f, water[16].Pos.x, 1e-5);
            Assert.AreEqual(L.StockFloor, water[16].Pos.y, 1e-5);
            Assert.AreEqual(9.25f, water[16].Pos.z, 1e-5);
            Assert.AreEqual(new Vector3(0.64f, 0.66f, 0.64f), L.Slots(Kind("water_500"))[0].Scale);
            // the same shelf every time
            Assert.AreEqual(L.Slots(Kind("canned"))[5].Pos, L.Slots(Kind("canned"))[5].Pos);
        }

        [Test]
        public void ShownUnitsAreCountTimesPerCappedBySlots()
        {
            Assert.AreEqual(12, L.Shown(new JValue(3), 4, 20)); // butane: 3 boxes of 4
            Assert.AreEqual(20, L.Shown(new JValue(9), 4, 20));
            Assert.AreEqual(0, L.Shown(null, 1, 40));
            Assert.AreEqual(0, L.Shown(new JValue(-3), 1, 40));
            Assert.AreEqual(2, L.Shown(new JValue("2"), 1, 40));
            Assert.AreEqual(0, L.Shown(new JValue("many"), 1, 40));
            Assert.AreEqual(40, L.Shown(new JValue(40), 1, 40));
        }

        [Test]
        public void FloorRowBodyFollowsTheOverflowBottles()
        {
            Assert.IsFalse(L.FloorRow(16, out _, out _, out _, out _));
            Assert.IsTrue(L.FloorRow(17, out var x0, out var x1, out var z0, out var z1));
            Assert.AreEqual(-0.38f, x0, 1e-4); Assert.AreEqual(-0.26f, x1, 1e-5);
            Assert.AreEqual(9.19f, z0, 1e-4); Assert.AreEqual(9.31f, z1, 1e-5);
            Assert.IsTrue(L.FloorRow(30, out x0, out _, out z0, out _)); // 14 on the floor: a full first row and a second
            Assert.AreEqual(-0.32f - 11 * 0.108f - 0.06f, x0, 1e-4);
            Assert.AreEqual(9.082f, z0, 1e-4);
        }

        [Test]
        public void SensorLampHoldsNineSecondsInItsZoneWhilePowered()
        {
            float hold = 0;
            Assert.IsTrue(L.SensorStep(ref hold, 0.1f, true, true, 2f, 0.5f));
            Assert.AreEqual(8.9f, hold, 1e-4);
            for (int i = 0; i < 88; i++) L.SensorStep(ref hold, 0.1f, true, true, 10f, 0.5f); // walked away
            Assert.IsTrue(hold > 0);
            Assert.IsFalse(L.SensorStep(ref hold, 0.2f, true, true, 10f, 0.5f));
            hold = 0;
            Assert.IsFalse(L.SensorStep(ref hold, 0.1f, true, false, 1f, 0.5f), "another zone");
            Assert.IsFalse(L.SensorStep(ref hold, 0.1f, true, true, 1f, 2.5f), "a floor away");
            Assert.IsFalse(L.SensorStep(ref hold, 0.1f, false, true, 1f, 0.5f), "no power");
            Assert.AreEqual(0, hold);
        }

        [Test]
        public void ApartmentZonesAndTheLampsTheSensorsReplace()
        {
            Assert.AreEqual("stairs", L.AptZone(5, 3, 2));
            Assert.AreEqual("home", L.AptZone(-3, 6, 4));
            Assert.AreEqual("corridor", L.AptZone(-3, 6, -1));
            Assert.AreEqual("lobby", L.AptZone(4, 0.5f, -3));
            // the scene's grid lamps (original frame): stairwell and corridor strip go, rooms stay
            Assert.IsTrue(L.AptSensorArea(5, 1.9f, 0.75f));
            Assert.IsTrue(L.AptSensorArea(5, 3.3f, 4.3f));
            Assert.IsTrue(L.AptSensorArea(-13.63f, 7.3f, -1f));
            Assert.IsFalse(L.AptSensorArea(1.5f, 1.9f, -3.25f), "lobby");
            Assert.IsFalse(L.AptSensorArea(9.5f, 7.2f, 0.85f), "303 entry");
            Assert.IsFalse(L.AptSensorArea(-5.63f, 7.35f, 2.35f), "302호");
            Assert.AreEqual(17, L.AptSensors.Length);
            Assert.AreEqual(8, L.AptBackups.Length);
            Assert.AreEqual(9, L.AptSensors.Count(s => L.AptZone(s.x, s.y - 0.25f, s.z) == "corridor"));
            Assert.AreEqual(7, L.AptSensors.Count(s => L.AptZone(s.x, s.y - 0.25f, s.z) == "stairs"));
            Assert.AreEqual("home", L.AptZone(L.AptSensors[16].x, L.AptSensors[16].y - 0.25f, L.AptSensors[16].z));
        }

        [Test]
        public void FixturesLightOnlyTheirZoneOrOneLinkedByAnOpenDoor()
        {
            Assert.IsTrue(L.AptLinked("home", "corridor", 0.5f, 0, 0));
            Assert.IsTrue(L.AptLinked("corridor", "home", 0.5f, 0, 0));
            Assert.IsFalse(L.AptLinked("home", "corridor", 0.05f, 1, 1), "302 shut");
            Assert.IsTrue(L.AptLinked("stairs", "corridor", 0, 0.2f, 0));
            Assert.IsTrue(L.AptLinked("lobby", "stairs", 0, 0, 1));
            Assert.IsFalse(L.AptLinked("lobby", "corridor", 1, 1, 1), "not neighbours");
            Assert.AreEqual(5f, L.FixtureDistance(true, true, false, 3, 0, 4), 1e-4);
            Assert.AreEqual(14f, L.FixtureDistance(true, false, true, 3, 0, 4), 1e-4);
            Assert.AreEqual(1.6f, L.FixtureDistance(true, true, false, 0, 1, 0), 1e-4);
            Assert.IsTrue(float.IsPositiveInfinity(L.FixtureDistance(true, false, false, 1, 0, 0)), "a closed wall");
            Assert.IsTrue(float.IsPositiveInfinity(L.FixtureDistance(false, true, false, 1, 0, 0)), "off");
            // the 3F landing's backup lamp from the dark corridor behind the shut stair door: no light
            var b = L.AptBackups[3];
            Assert.AreEqual("stairs", L.AptZone(b.x, b.y, b.z));
            Assert.AreEqual("corridor", L.AptZone(3.71f, 5.6f + 0.5f, -0.95f));
        }

        [Test]
        public void SeatedPeopleAreNotTurnedByATalk()
        {
            Assert.IsTrue(TalkFacing.IsSeated("sitChair"));
            Assert.IsTrue(TalkFacing.IsSeated("sit"));
            Assert.IsFalse(TalkFacing.IsSeated("idle"));
            Assert.IsFalse(TalkFacing.IsSeated(null));
        }

        [Test]
        public void CandleFlickersAroundItsLevel()
        {
            float lo = 9, hi = 0;
            for (float t = 0; t < 20; t += 0.013f) { float k = L.CandleFlicker(t); lo = Mathf.Min(lo, k); hi = Mathf.Max(hi, k); }
            Assert.That(lo, Is.GreaterThanOrEqualTo(0.85f));
            Assert.That(hi, Is.LessThanOrEqualTo(1.15f));
            Assert.That(hi - lo, Is.GreaterThan(0.15f));
        }

        [Test]
        public void StoreCounterLabelFollowsTheClerk()
        {
            Assert.AreEqual("계산대 (아무도 없다)", L.CounterLabel("out"));
            Assert.AreEqual("계산대 · 이수아", L.CounterLabel("in"));
        }

        [Test]
        public void YardWorkerPacesBetweenThePallets()
        {
            float x = 5.9f, pace = 1;
            L.WorkerPace(ref x, ref pace, 0.2f);
            Assert.AreEqual(-1, pace); Assert.AreEqual(6f, x, 1e-5);
            L.WorkerPace(ref x, ref pace, 1f);
            Assert.AreEqual(4.9f, x, 1e-4);
            x = -7.95f; L.WorkerPace(ref x, ref pace, 0.1f);
            Assert.AreEqual(1, pace); Assert.AreEqual(-8f, x, 1e-5);
        }

        [Test]
        public void DashboardStateParsesThePrologueRows()
        {
            var rows = new System.Collections.Generic.List<L.DashRow>();
            Assert.IsTrue(L.ParseDashboard(Prologue.Dashboard(1), out var title, out var updated, rows));
            Assert.AreEqual("한결물류 · 연무센터 운영 대시보드", title);
            Assert.AreEqual("D-3 (화) 17:25 기준", updated);
            Assert.AreEqual(6, rows.Count);
            Assert.AreEqual(new[] { true, true, false, false, false, false }, rows.Select(r => r.Hot).ToArray());
            L.ParseDashboard(Prologue.Dashboard(3), out _, out _, rows);
            Assert.AreEqual(new[] { true, true, false, true, false, true }, rows.Select(r => r.Hot).ToArray());
            Assert.AreEqual("서해안 노선", rows[1].Label); Assert.AreEqual("운송 지연 공지", rows[1].Value); Assert.AreEqual("내일부터", rows[1].Delta);
            Assert.IsFalse(L.ParseDashboard(new JValue("(canvas)"), out _, out _, rows));
        }

        [Test]
        public void ShelterEmergencySupplyDropsCircuitBAndDimsA()
        {
            // the original's six area lights: A B A along z -8, B A B along z 8
            Assert.AreEqual("ABA", new string(new[] { -14f, -2f, 10f }.Select(x => L.ShelterCircuit(x, 2.5f, -8)).ToArray()));
            Assert.AreEqual("BAB", new string(new[] { -14f, -2f, 10f }.Select(x => L.ShelterCircuit(x, 2.5f, 8)).ToArray()));
            // outside the main hall every lamp is circuit A: stair A, ramp C, the lift hall
            Assert.AreEqual('A', L.ShelterCircuit(5, 5.2f, -18.6f));
            Assert.AreEqual('A', L.ShelterCircuit(-13, 2.9f, -27));
            Assert.AreEqual('A', L.ShelterCircuit(23, 2.3f, -1));
            Assert.AreEqual('A', L.ShelterCircuit(24.6f, 2.3f, 0));
            Assert.AreEqual(1f, L.ShelterLampFactor(true, 'B'));
            Assert.AreEqual(0.55f, L.ShelterLampFactor(false, 'A'));
            Assert.AreEqual(0f, L.ShelterLampFactor(false, 'B'));
        }

        [Test]
        public void StockUnitMeshesCombineOnePerColour()
        {
            var bottle = InteriorMeshes.StockUnit(Kind("water_2l"));
            Assert.AreEqual(3, bottle.Colors.Count);
            var units = Enumerable.Range(0, 5).Select(i => (new Vector3(i, 0, 0), 30f * i, Vector3.one)).ToList();
            var mesh = bottle.Combine(null, units, 3);
            Assert.AreEqual(bottle.V.Count * 3, mesh.vertexCount);
            Assert.AreEqual(3, mesh.subMeshCount);
            Assert.That(mesh.bounds.size.y, Is.EqualTo(0.315f).Within(1e-3));
            var aid = InteriorMeshes.StockUnit(Kind("first_aid"));
            Assert.AreEqual(2, aid.Colors.Count);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void LevelPointDescriptionsAreKnown()
        {
            foreach (var key in new[] { "apartment:stairsWet", "store:storeCoffee", "store:storeSteamer", "store:storeRack", "store:storeRamen", "store:storeIce", "store:storeWindowBar", "shelter:fromStairs", "shelter:fromRamp", "shelter:fromAccess" })
                Assert.IsTrue(World.Desc.ContainsKey(key), key);
        }
    }
}
