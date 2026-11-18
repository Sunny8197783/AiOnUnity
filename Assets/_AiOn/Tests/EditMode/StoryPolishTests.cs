// Polish pass (story flow, cutscenes, world logic): the pure rules behind the fixes.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Levels;
using AiOn.Levels.Crowd;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StoryPolishTests
    {
        static readonly Dictionary<string, string> DDayGates = new() { ["east"] = "closed", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "closed" };

        static double Dist(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        [Test]
        public void Trickle_WalksFromTheComplexGateToAnOpenGate_EvenWithClosures()
        {
            foreach (var closures in new[] { new string[0], new[] { "yeonmuCenter" }, new[] { "danji", "munhwaSouth" } })
            {
                var (paths, targets) = TownCrowdPlan.TricklePaths(DDayGates, closures);
                Assert.That(paths, Is.Not.Empty, string.Join(",", closures));
                foreach (var p in paths)
                {
                    Assert.That(p.Length, Is.GreaterThan(1));
                    Assert.That(Dist(p[0], TownCrowdPlan.ComplexGate), Is.LessThanOrEqualTo(TownCrowdPlan.TrickleReach));
                }
                Assert.That(targets, Is.Not.Empty);
                Assert.That(targets, Has.None.EqualTo(TownCrowdPlan.Entry["east"]), "the east doors are closed tonight");
            }
        }

        [Test]
        public void Trickle_IsThin_AndStopsAQuarterHourAfterTheAlert()
        {
            Assert.That(TownCrowdPlan.Sizes["trickle"], Is.LessThan(TownCrowdPlan.Sizes["main"] / 3));
            Assert.That(TownCrowdPlan.TrickleDelay, Is.GreaterThanOrEqualTo(5));
            Assert.That(DDay.TrickleEnd, Is.EqualTo(DDay.Alert + 15));
            // the port's groups have their own seeds, so the original groups keep 900 + 17 n
            Assert.That(TownCrowdPlan.PortSeeds.Keys, Is.EquivalentTo(new[] { "trickle", "torchA", "torchB", "line2" }));
            Assert.That(TownCrowdPlan.PortSeeds.Values.Distinct().Count(), Is.EqualTo(4));
            Assert.That(TownCrowdPlan.Modes, Does.Contain("boarded"));
        }

        [Test]
        public void TorchAreas_StandBesideEntrancesAAndB_NotInTheWayIn()
        {
            foreach (var (name, gate) in new[] { ("torchA", "stairs"), ("torchB", "ramp") })
            {
                var a = TownCrowdPlan.TorchAreas[name];
                var c = new Pt((a.X0 + a.X1) / 2, (a.Z0 + a.Z1) / 2);
                double d = Dist(c, TownCrowdPlan.Entry[gate]);
                Assert.That(d, Is.InRange(2.5, 10.0), name);
                Assert.That(TownCrowdPlan.Entry[gate].X >= a.X0 && TownCrowdPlan.Entry[gate].X <= a.X1 && TownCrowdPlan.Entry[gate].Z >= a.Z0 && TownCrowdPlan.Entry[gate].Z <= a.Z1, Is.False);
            }
        }

        [TestCase("D+1 토 07:00", "morning")]
        [TestCase("D+3 일 10:00", "morning")]
        [TestCase("D+12 수 13:15", "afternoon")]
        [TestCase("D+1 토 17:30", "dusk")]
        [TestCase("D-2 수 18:30", "dusk")]
        [TestCase("D+2 일 21:40", "night")]
        [TestCase("D+7 금 밤", "night")]
        [TestCase("D+1 토 오후", "afternoon")]
        public void Timelapse_PicksACameraPathByTheHour(string label, string kind)
        {
            Assert.That(Cutscenes.TimelapseKind(label), Is.EqualTo(kind));
            Assert.That(Cutscenes.TimelapseCams.ContainsKey(kind));
        }

        [Test]
        public void StrikeTitle_ReadsTheClock_AndTheMissileComesFromInFrontOfShotOne()
        {
            Assert.That(Cutscenes.StrikeTitle(20 * 60 + 19), Is.EqualTo(new[] { "연무동", "D-day 금요일 20:19" }));
            // shot 1 stands at (54, 21) and looks toward the villa: the missile starts beyond it (same side), not behind the camera
            double cx = 54, cz = 21;
            var f = Cutscenes.MissileFrom;
            double vx = Cutscenes.Villa.x - cx, vz = Cutscenes.Villa.z - cz, mx = f[0] - cx, mz = f[2] - cz;
            double cos = (vx * mx + vz * mz) / Math.Sqrt(vx * vx + vz * vz) / Math.Sqrt(mx * mx + mz * mz);
            Assert.That(cos, Is.GreaterThan(Math.Cos(25 * Math.PI / 180)), "within 25 degrees of the view");
        }

        [Test]
        public void BusRoute_LeavesTheBayEastThenUpMunhwaro_WithoutTouchingTheWallsOrEachOther()
        {
            for (int b = 0; b < 2; b++)
            {
                var start = BusRoute.At(b, -1);
                Assert.That(start.x, Is.EqualTo(BusRoute.Parked[b].x).Within(1e-9));
                Assert.That(start.z, Is.EqualTo(BusRoute.Parked[b].z).Within(1e-9));
                Assert.That(start.yaw, Is.EqualTo(0).Within(1e-9), "parked facing north (-z)");
                Assert.That(start.drop, Is.EqualTo(0));
                for (double t = 0; t <= 30; t += 0.1)
                {
                    var p = BusRoute.At(b, t);
                    double fx = p.x - Math.Sin(p.yaw) * 5.5, fz = p.z - Math.Cos(p.yaw) * 5.5; // the nose (yaw 0 faces -z)
                    if (fx < -58) Assert.That(fz, Is.GreaterThan(-56.2), $"bus {b}: the nose stays off the bay's north wall (t {t:0.0})");
                    Assert.That(p.x, Is.GreaterThan(-72), "never west into the centre building");
                }
                var end = BusRoute.At(b, 60);
                Assert.That(end.x, Is.EqualTo(BusRoute.LaneX).Within(1e-6), "up the lane of 문화로");
                Assert.That(end.z, Is.LessThan(-100), "and out of town");
                Assert.That(end.drop, Is.EqualTo(0.12).Within(1e-9), "down off the bay's curb onto the road");
            }
            Assert.That(BusRoute.At(1, 2).z, Is.LessThan(BusRoute.Parked[1].z), "the bus nearer the road goes first");
            Assert.That(BusRoute.At(0, 2).z, Is.EqualTo(BusRoute.Parked[0].z), "the other waits");
            for (double t = 0; t <= 40; t += 0.1)
            {
                var a = BusRoute.At(0, t); var c = BusRoute.At(1, t);
                if (a.z < -105 || c.z < -105) continue; // out of town: hidden
                Assert.That(Math.Sqrt((a.x - c.x) * (a.x - c.x) + (a.z - c.z) * (a.z - c.z)), Is.GreaterThan(4.4), $"t {t:0.0}");
            }
            Assert.That(BusRoute.Distance(1), Is.LessThan(1), "a slow start");
            Assert.That(BusRoute.Gone(30), Is.True);
            Assert.That(BusRoute.Gone(3), Is.False);
        }

        [Test]
        public void BusDeparture_CameraWatchesFromAcrossTheRoad_AndFadesBeforeTheEnd()
        {
            Assert.That(Cutscenes.BusFadeAt, Is.LessThan(Cutscenes.BusDepartSeconds - 1));
            for (double t = 0; t <= Cutscenes.BusDepartSeconds; t += 0.1)
            {
                var p = Cutscenes.BusDepartPose(t);
                Assert.That(p.T[2], Is.GreaterThanOrEqualTo(Cutscenes.BusPanZ - 1e-9));
                Assert.That(p.T[0], Is.LessThanOrEqualTo(Cutscenes.BusPanX + 1e-9));
                for (int b = 0; b < 2; b++)
                {
                    var bus = BusRoute.At(b, t);
                    Assert.That(Math.Sqrt((bus.x - p.P[0]) * (bus.x - p.P[0]) + (bus.z - p.P[2]) * (bus.z - p.P[2])), Is.GreaterThan(7), "the camera is never in a bus's way");
                }
            }
            Assert.That(Cutscenes.Closes["bus"].States["buses"], Is.EqualTo("departed"), "the closing shot does not replay the pull-out");
        }

        [Test]
        public void Boarded_FewSeeTheBusesOffByTheReception_ClearOfTheirWayOut()
        {
            var a = TownCrowdPlan.BoardedArea;
            Assert.That(a.Z0, Is.GreaterThan(-45 + 4), "south of where the buses turn out");
            Assert.That(a.X0, Is.GreaterThan(-63.75), "east of the east bus's side");
        }

        [Test]
        public void LitLevel_PutsTheTorchAway_OnlyWhereTheLightsWork()
        {
            var st = new Dictionary<string, Dictionary<string, JToken>>
            {
                ["shelter"] = new() { ["power"] = "emergency" }, ["apartment"] = new() { ["power"] = "off" }, ["store"] = new() { ["power"] = "on" },
                ["mart"] = new() { ["crowd"] = "busy" },
            };
            Assert.That(StoryDirector.LitLevel(st, "shelter"), Is.True);
            Assert.That(StoryDirector.LitLevel(st, "store"), Is.True);
            Assert.That(StoryDirector.LitLevel(st, "apartment"), Is.False);
            Assert.That(StoryDirector.LitLevel(st, "mart"), Is.False, "no power state and no grid: dark");
            Assert.That(StoryDirector.LitLevel(st, "mart", grid: true), Is.True, "no power state: the grid's lights");
            Assert.That(StoryDirector.LitLevel(st, "town", grid: true), Is.False, "street lights do not count");
            Assert.That(StoryDirector.LitLevel(st, null), Is.False);
            // the shelter arrives on its default 'grid' lights (D-day: the emergency lights come later)
            var none = new Dictionary<string, Dictionary<string, JToken>>();
            Assert.That(StoryDirector.LitLevel(none, "shelter"), Is.True);
            Assert.That(StoryDirector.LitLevel(new Dictionary<string, Dictionary<string, JToken>> { ["shelter"] = new() { ["power"] = "grid" } }, "shelter"), Is.True);
            Assert.That(StoryDirector.PowerOf(none, "store", false), Is.EqualTo("off"));
        }

        [Test]
        public void ElderFollower_ComesThroughOnThePlayersSideOfTheDoor()
        {
            // just out of 302 into the 3F corridor (z -2..0), facing north (-z): behind is the doorway of 302, so she comes in beside
            var (x, z) = Elder.FollowSpot(Elder.ApartmentWalk, -1.0, 5.6, -0.5, 0);
            Assert.That(z, Is.InRange(-2.0, 0.0), "in the corridor, not back in 302");
            Assert.That(Math.Sqrt((x + 1.0) * (x + 1.0) + (z + 0.5) * (z + 0.5)), Is.EqualTo(0.9).Within(1e-6));
            // the middle of the corridor: straight behind
            var (x2, z2) = Elder.FollowSpot(Elder.ApartmentWalk, 10, 5.6, -1.0, Math.PI / 2); // facing -x
            Assert.That(x2, Is.EqualTo(10.9).Within(1e-6)); Assert.That(z2, Is.EqualTo(-1.0).Within(1e-6));
            // nowhere on the mesh: the plain spot behind
            var (x3, z3) = Elder.FollowSpot(Elder.ApartmentWalk, 100, 0, 100, 0);
            var (bx, bz) = Elder.BehindOf(100, 100, 0);
            Assert.That(x3, Is.EqualTo(bx)); Assert.That(z3, Is.EqualTo(bz));
        }

        [Test]
        public void ShelterAlarm_TheSeatedStandAndFaceTheFire_AndStayUp()
        {
            var s = new ShelterPeople(null);
            s.Apply("crowd", "settled"); s.Apply("phase", "calm"); s.Apply("fire", "large");
            Assert.That(s.Alarm, Is.False, "fire, but no alarm yet");
            s.Apply("phase", "alert");
            Assert.That(s.Alarm, Is.True);
            var f = new CrowdFrame(0, false, 0, 0, false, 0, 0, 0, 0, true, "underground");
            for (int i = 0; i < 200; i++) s.Update(0.05, f);
            Assert.That(s.Seated, Is.GreaterThan(0), "they keep their places");
            Assert.That(s.Bodies.Count(b => b.Visible && b.Anim == "sit"), Is.EqualTo(0), "nobody sits back down while it burns");
            s.Apply("phase", "calm");
            Assert.That(s.Alarm, Is.False);
            Assert.That(s.Bodies.Count(b => b.Visible && b.Anim == "sit"), Is.GreaterThan(0), "calm sits them back down");
        }

        [Test]
        public void WaterQueue_ServedAtTheTaps_AheadOfTheLine()
        {
            Assert.That(Endure.TapSide.x, Is.InRange(-81.2, -78.8), "in front of a tap");
            Assert.That(Endure.TapSide.x, Is.LessThan(-78.6), "west of the line's head: nobody still waiting ahead");
            Assert.That(Endure.TapSide.z, Is.GreaterThan(-19.45), "clear of the tank");
        }

        [Test]
        public void Pack_ComesOffAtHome()
        {
            Assert.That(UnityStoryHost.AtHome("apartment", -3, 5.6, 4), Is.True);
            Assert.That(UnityStoryHost.AtHome("apartment", -3, 5.6, -1), Is.False, "the corridor");
            Assert.That(UnityStoryHost.AtHome("apartment", -3, 0, 4), Is.False, "another floor");
            Assert.That(UnityStoryHost.AtHome("town", -3, 5.6, 4), Is.False);
        }

        [Test]
        public void ElderFollower_ComesThroughAStepBehind()
        {
            var (x, z) = Elder.BehindOf(10, 5, 0); // facing -z: behind is +z
            Assert.That(x, Is.EqualTo(10).Within(1e-9));
            Assert.That(z - 5, Is.EqualTo(0.9).Within(1e-9));
            Assert.That(Elder.MinGap, Is.GreaterThanOrEqualTo(0.8));
            var (x2, z2) = Elder.BehindOf(0, 0, Math.PI / 2); // facing -x: behind is +x
            Assert.That(x2, Is.EqualTo(0.9).Within(1e-9));
            Assert.That(z2, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void BareStore_HidesAboutSevenInTen()
        {
            int hidden = Enumerable.Range(0, 100).Count(ShopGoods.SparseHides);
            Assert.That(hidden, Is.EqualTo(70));
            Assert.That(ShopGoods.SparseKeep, Is.SupersetOf(new[] { "batteries", "water" }));
        }

        [Test]
        public void LooterBearing_Spread()
        {
            Assert.That(Danger.LooterBearing(0, 0.5), Is.EqualTo(0).Within(1e-9));
            Assert.That(Danger.LooterBearing(0, 0), Is.EqualTo(-0.7).Within(1e-9));
        }
    }
}
