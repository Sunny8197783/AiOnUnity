using System.Collections.Generic;
using System.Linq;
using AiOn.Levels.Crowd;
using AiOn.Levels.Town;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>The town's live street (levels/town-traffic.js, town-plan.js signalAt, town-events.js): pure logic.</summary>
    public class TownStreetTests
    {
        static readonly Pt[] NoPeds = new Pt[0];

        static TrafficCar Car(int lane, double u, string type = "sedan", int rank = 0)
        {
            var T = TownTraffic.Type(type);
            var l = TownTraffic.Lanes[lane];
            return new TrafficCar { Lane = lane, Type = type, Rank = rank, U = u, V = l.Vmax * 0.8, V0 = l.Vmax * T.V, Len = T.L, Wid = T.W };
        }

        static void Run(TrafficSim sim, double t0, double seconds, double py = 9, double px = 0, double pz = 0, bool player = false, Pt[] peds = null, bool power = true, bool freeze = false)
        {
            const double dt = 0.05;
            peds ??= NoPeds;
            for (double e = 0; e < seconds; e += dt) sim.Step(dt, freeze ? t0 : t0 + e, power, player, px, py, pz, peds, peds.Length);
        }

        // ---------- signals ----------

        [TestCase(0, "green", "red", 20)]
        [TestCase(21, "amber", "red", 2)]
        [TestCase(23.5, "red", "red", 1)]
        [TestCase(25, "red", "green", 12.5)]
        [TestCase(38, "red", "amber", 2.5)]
        [TestCase(41, "red", "red", 1)]
        [TestCase(42, "green", "red", 20)]
        [TestCase(-1, "red", "red", 1)]
        public void SignalAt_RunsTheCycle(double t, string ew, string ns, double left)
        {
            var s = TownTraffic.SignalAt(t);
            Assert.AreEqual(ew, s.EW); Assert.AreEqual(ns, s.NS); Assert.AreEqual(left, s.Left, 1e-9);
            Assert.AreEqual(42, TownTraffic.CycleSeconds, 1e-9);
        }

        [Test]
        public void Lamps_LightOneHeadAndBlinkTheWalkFigure()
        {
            var a = TownTraffic.Lamps(TownTraffic.SignalAt(5), "EW", 5, true);
            Assert.AreEqual(5, a.Green); Assert.AreEqual(0, a.Red); Assert.AreEqual(0, a.Amber);
            Assert.AreEqual(4, a.WalkGreen); Assert.AreEqual(0, a.WalkRed);
            var ns = TownTraffic.Lamps(TownTraffic.SignalAt(5), "NS", 5, true);
            Assert.AreEqual(5, ns.Red); Assert.AreEqual(4, ns.WalkRed); Assert.AreEqual(0, ns.WalkGreen);
            // the last 5 s of green: the walk figure blinks at 1 Hz and the stop figure stays dark
            Assert.AreEqual(4, TownTraffic.Lamps(TownTraffic.SignalAt(16), "EW", 16, true).WalkGreen);
            var off = TownTraffic.Lamps(TownTraffic.SignalAt(16.5), "EW", 16.5, true);
            Assert.AreEqual(0, off.WalkGreen); Assert.AreEqual(0, off.WalkRed); Assert.AreEqual(5, off.Green);
            Assert.AreEqual(5, TownTraffic.Lamps(TownTraffic.SignalAt(21), "EW", 21, true).Amber);
            var dark = TownTraffic.Lamps(TownTraffic.SignalAt(5), "EW", 5, false);
            Assert.AreEqual(0, dark.Green + dark.Red + dark.Amber + dark.WalkGreen + dark.WalkRed);
        }

        [Test]
        public void Signals_ShareTheCrowdClock()
        {
            for (double t = 0; t < 84; t += 0.25)
            {
                var s = TownTraffic.SignalAt(t);
                double ew = s.EW == "green" ? s.Left : 0, ns = s.NS == "green" ? s.Left : 0;
                Assert.AreEqual(TownCrowdPlan.GreenLeft(t, "EW"), ew, 1e-9, $"t {t}");
                Assert.AreEqual(TownCrowdPlan.GreenLeft(t, "NS"), ns, 1e-9, $"t {t}");
            }
        }

        // ---------- pool and modes ----------

        [Test]
        public void DefaultPool_FillsEveryLaneAndTheModesThinIt()
        {
            var sim = new TrafficSim(TrafficSim.DefaultPool());
            Assert.AreEqual(24, sim.Pool.Count);
            Assert.AreEqual(24, sim.Pool.Count(c => c.Active));
            Assert.IsTrue(sim.SetMode("light"));
            Assert.AreEqual(4 * 2 + 4 * 1, sim.Pool.Count(c => c.Active));
            Assert.IsTrue(sim.SetMode("none"));
            Assert.AreEqual(0, sim.Pool.Count(c => c.Active));
            Assert.IsFalse(sim.SetMode("rush"));
            Assert.AreEqual("none", sim.Mode);
            Assert.IsTrue(sim.Pool.All(c => TownTraffic.Palette.Any(p => p.hex == c.Color)));
        }

        [Test]
        public void PoolFrom_FindsLanesAndRanksFromTheExport()
        {
            var pool = TrafficSim.PoolFrom(new[] { ("sedan", 99.5, 1.6, true), ("suv", -120.4, 1.6, true), ("truck", 87.0, -1.6, true), ("sedan", 52.0, 150.3, false), ("truck", 118.8, -56.2, false) });
            Assert.AreEqual(4, pool.Count, "the idling truck in the lot is not on a lane");
            var a = pool.Single(c => c.Type == "sedan" && c.Lane == 0);
            var b = pool.Single(c => c.Type == "suv");
            Assert.AreEqual(0, b.Lane); Assert.AreEqual(0, b.Rank); Assert.AreEqual(1, a.Rank);
            var t = pool.Single(c => c.Type == "truck");
            Assert.AreEqual(2, t.Lane); Assert.AreEqual(-87.0, t.U, 1e-9);
            Assert.AreEqual(6, pool.Single(c => c.Lane == 6).Lane);
            Assert.AreEqual(-150.3, pool.Single(c => c.Lane == 6).U, 1e-9);
        }

        // ---------- driving ----------

        [Test]
        public void Cars_StopAtRedAndGoOnGreen()
        {
            var car = Car(0, -50);
            var sim = new TrafficSim(new List<TrafficCar> { car });
            Run(sim, 25, 11); // NS green: EW waits
            Assert.LessOrEqual(car.U + car.Len / 2, -3.5 + 0.05);
            Assert.Greater(car.U + car.Len / 2, -3.5 - 3, "it waits at the line, not far short of it");
            Assert.Less(car.V, 0.2);
            Run(sim, 42, 8); // EW green again
            Assert.Greater(car.U, 20);
        }

        [Test]
        public void Cars_RunAnAmberTheyCannotStopFor()
        {
            var car = Car(0, -10);
            car.V = 12;
            var sim = new TrafficSim(new List<TrafficCar> { car });
            Run(sim, 20.01, 2);
            Assert.Greater(car.U, 0, "6 m short at 12 m/s: it goes through");
        }

        [Test]
        public void Signals_DarkWithoutPower_CarsDriveOn()
        {
            var car = Car(0, -50);
            var sim = new TrafficSim(new List<TrafficCar> { car });
            Run(sim, 25, 11, power: false);
            Assert.Greater(car.U, 20);
        }

        [Test]
        public void Cars_YieldToThePlayerOnTheRoad_NotOnTheCurbAbove()
        {
            var car = Car(0, -30);
            var sim = new TrafficSim(new List<TrafficCar> { car });
            Run(sim, 0, 10, py: 0, px: 10, pz: 1.0, player: true, freeze: true);
            Assert.Less(car.U + car.Len / 2, 10 - 1.4 + 0.1);
            Assert.Less(car.V, 0.2);
            var b = Car(0, -30);
            var sim2 = new TrafficSim(new List<TrafficCar> { b });
            Run(sim2, 0, 10, py: 0.8, px: 10, pz: 1.0, player: true, freeze: true);
            Assert.Greater(b.U, 30);
        }

        [Test]
        public void Cars_WaitShortOfABusyZebra()
        {
            var car = Car(2, -60); // westbound: u = -x, the zebra at x 0 is u 0
            var sim = new TrafficSim(new List<TrafficCar> { car });
            var peds = new[] { new Pt(0, -5.5) }; // on the zebra, outside the lane's 2.4 m
            Run(sim, 0, 12, peds: peds, freeze: true);
            Assert.LessOrEqual(car.U + car.Len / 2, -3.2 + 0.05);
            Assert.Less(car.V, 0.2);
        }

        [Test]
        public void Cars_FollowWithoutOverlap_AndWrap()
        {
            var sim = new TrafficSim(TrafficSim.DefaultPool());
            var peds = new[] { new Pt(30, 1.6), new Pt(0, 0) };
            for (double t = 0; t < 240; t += 0.05)
            {
                sim.Step(0.05, t, true, false, 0, 0, 0, t < 60 ? peds : NoPeds, t < 60 ? peds.Length : 0);
                if (((int)(t * 20)) % 40 != 0) continue;
                for (int li = 0; li < TownTraffic.Lanes.Length; li++)
                {
                    var cars = sim.Pool.Where(c => c.Lane == li && c.Active).OrderBy(c => c.U).ToList();
                    for (int i = 1; i < cars.Count; i++)
                        Assert.Greater(cars[i].U - cars[i - 1].U - (cars[i].Len + cars[i - 1].Len) / 2, -0.01, $"lane {li} at t {t:0.0}");
                    foreach (var c in cars) Assert.LessOrEqual(c.U, TownTraffic.LaneSpan);
                }
            }
            Assert.IsTrue(sim.Pool.All(c => c.V >= 0));
        }

        [Test]
        public void Solids_FollowTheCarAndGoOffOutsideTheTown()
        {
            var a = Car(4, 0); // southbound 문화로 lane at x -48, u = -z
            var b = Car(0, -200);
            var sim = new TrafficSim(new List<TrafficCar> { a, b });
            Assert.IsTrue(a.SolidOn); Assert.AreEqual(-48, a.X, 1e-9);
            Assert.AreEqual(-48 - a.Wid / 2 - 0.05, a.Solid.X0, 1e-9);
            Assert.AreEqual(a.Len / 2 + 0.05, a.Solid.Z1, 1e-9);
            Assert.IsFalse(b.SolidOn, "x -200 is out in the city ring");
            sim.SetMode("none");
            sim.Step(0.05, 0, true, false, 0, 0, 0, NoPeds, 0);
            Assert.IsFalse(a.SolidOn);
        }

        // ---------- closures ----------

        [Test]
        public void Closures_FlashGuidesAndTowers()
        {
            Assert.AreEqual(4f, TownClosures.Flash(0, true, false));
            Assert.AreEqual(0f, TownClosures.Flash(0.7, true, false));
            Assert.AreEqual(4f, TownClosures.Flash(1.3, true, false));
            Assert.AreEqual(4f, TownClosures.Flash(0.7, true, true), "steady with reduced motion");
            Assert.AreEqual(0f, TownClosures.Flash(0, false, false));
            Assert.AreEqual("wave", TownClosures.GuideAnim(1, 0, false));
            Assert.AreEqual("idle", TownClosures.GuideAnim(4, 0, false));
            Assert.AreEqual("idle", TownClosures.GuideAnim(1, 1, false), "the second guide runs 2.3 s ahead");
            Assert.AreEqual("point", TownClosures.GuideAnim(1, 0, true));
            Assert.AreEqual("talk", TownClosures.GuideAnim(5, 0, true));
            var (x, z, tx, tz) = TownClosures.Tower("danji");
            Assert.AreEqual(2.4, x, 1e-9); Assert.AreEqual(52.4, z, 1e-9); Assert.AreEqual(-3.6, tx, 1e-9); Assert.AreEqual(52.4, tz, 1e-9);
            var (mx, mz, _, mtz) = TownClosures.Tower("munhwaNorth");
            Assert.AreEqual(-57.6, mx, 1e-9); Assert.AreEqual(-21.4, mz, 1e-9); Assert.AreEqual(-15.4, mtz, 1e-9);
            CollectionAssert.AreEqual(new[] { "danji", "sijang" }, TownClosures.Known(new[] { "danji", "nowhere", "sijang", "danji" }));
        }

        [Test]
        public void Towers_StandOnTheirSolids()
        {
            var layout = UnityEditor.AssetDatabase.LoadAssetAtPath<AiOn.Levels.LevelLayout>("Assets/_AiOn/Data/Levels/TownLayout.asset");
            Assume.That(layout != null);
            foreach (var id in TownClosures.Guides.Keys)
            {
                var (x, z, _, _) = TownClosures.Tower(id);
                Assert.IsTrue(layout.solids.Any(s => s.id == "closure:" + id && s.off && x > s.x0 && x < s.x1 && z > s.z0 && z < s.z1), id);
            }
        }

        // ---------- baked meshes ----------

        [Test]
        public void InstanceCount_SplitsBakedInstances()
        {
            var v = new List<Vector3>();
            foreach (var cx in new[] { -100f, 0f, 120f })
                for (int i = 0; i < 12; i++) v.Add(new Vector3(cx + (i % 4) * 1.5f - 2.25f, i * 0.1f, (i % 3) - 1));
            Assert.AreEqual(3, TownStreet.InstanceCount(v));
            Assert.AreEqual(1, TownStreet.InstanceCount(v.Take(12).ToList()));
            Assert.AreEqual(0, TownStreet.InstanceCount(new List<Vector3> { new(0, 0, 0), new(50, 0, 0), new(1, 0, 0) }, 6f, 2), "no split up to maxK");
            Assert.AreEqual(0, TownStreet.InstanceCount(new List<Vector3>()));
        }

        // ---------- the 'traffic' state ----------

        [Test]
        public void TrafficState_KeepsTheOldModeOnAnUnknownOne()
        {
            var t = new TownCrowd(null, null);
            Assert.AreEqual("normal", t.Traffic);
            Assert.IsTrue(t.Apply("traffic", "light"));
            Assert.AreEqual("light", t.Traffic);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("traffic \"jam\" unknown"));
            Assert.IsTrue(t.Apply("traffic", "jam"), "the key is still the crowd's");
            Assert.AreEqual("light", t.Traffic, "town.js: state.traffic = traffic.mode");
            Assert.IsTrue(t.Apply("traffic", "none"));
            Assert.AreEqual("none", t.Traffic);
        }
    }
}
