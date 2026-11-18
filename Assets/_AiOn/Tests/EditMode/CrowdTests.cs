using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Levels;
using AiOn.Levels.Crowd;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AiOn.Tests
{
    /// <summary>The crowd port (engine/crowd.js, levels/town-crowd.js, shelter-people.js, mart.js shoppers): pure logic.</summary>
    public class CrowdTests
    {
        const double E = 1e-9;

        [TearDown]
        public void TearDown() => CrowdTier.Set("high");

        static LevelLayout Layout(string id) => AssetDatabase.LoadAssetAtPath<LevelLayout>($"Assets/_AiOn/Data/Levels/{id}Layout.asset");

        delegate void Upd(double dt, in CrowdFrame f);

        static void Run(Upd update, double seconds, double dt = 0.05, Func<bool> each = null)
        {
            for (double t = 0; t < seconds; t += dt) { update(dt, CrowdFrame.Empty(t)); if (each != null && each()) return; }
        }

        // ---------- engine ----------

        [TestCase(900, 1624652753u, 0.708232630)]
        [TestCase(917, 3882290832u, 0.417619836)]
        [TestCase(934, 1706299987u, 0.608512345)]
        [TestCase(951, 2613377298u, 0.728147040)]
        public void SeedHash_MatchesTheOriginal(int seed, uint hash, double first)
        {
            Assert.AreEqual(hash, CrowdGroup.SeedOf(seed));
            Assert.AreEqual(first, new Mulberry32(hash).Next(), 1e-9);
        }

        [Test]
        public void CrowdPath_LengthPointAndProjection()
        {
            var P = CrowdPath.Prep(new[] { new Pt(0, 0), new Pt(10, 0), new Pt(10, 5) });
            Assert.AreEqual(15, P.Len, E);
            P.At(12, out var x, out var z, out var tx, out var tz);
            Assert.AreEqual(10, x, E); Assert.AreEqual(2, z, E); Assert.AreEqual(0, tx, E); Assert.AreEqual(1, tz, E);
            var (pi, s) = CrowdGeom.Project(new[] { P }, 4, 1);
            Assert.AreEqual(0, pi); Assert.AreEqual(4, s, E);
            Assert.IsNull(CrowdPath.Prep(new[] { new Pt(0, 0), new Pt(0.4, 0) }));
            Assert.IsNull(CrowdPath.Prep(new[] { new Pt(0, 0) }));
        }

        [Test]
        public void ClipPaths_MatchTheOriginal()
        {
            var cut = CrowdGeom.ClipPaths(new List<Pt[]> { new[] { new Pt(-44, 8), new Pt(44, 8) } }, TownCrowdPlan.Barricades["yeonmuCenter"]);
            Assert.AreEqual(2, cut.Count);
            Assert.AreEqual(new[] { -41.0, 8, 26, 8 }, new[] { cut[0][0].X, cut[0][0].Z, cut[0][1].X, cut[0][1].Z });
            Assert.AreEqual(new[] { 30.0, 8, 44, 8 }, new[] { cut[1][0].X, cut[1][0].Z, cut[1][1].X, cut[1][1].Z });
            Assert.AreEqual(29, TownCrowdPlan.StrollFor(null).Count);
            Assert.AreEqual(39, TownCrowdPlan.StrollFor(TownCrowdPlan.Barricades.Keys).Count);
        }

        [Test]
        public void QueueSlots_WaterAndAssembly()
        {
            var w = new CrowdGroup(14, 917);
            w.SetMode("queue", new CrowdGroup.ModeOpts { At = TownCrowdPlan.WaterQueue, Dir = new Pt(1, 0), Spacing = 0.9, Leave = new Pt(-1, 0) });
            Assert.AreEqual(-78.1, w.Slot(0).X, 1e-9); Assert.AreEqual(-18.4, w.Slot(0).Z, 1e-9);
            Assert.AreEqual(-66.4, w.Slot(13).X, 1e-9); Assert.AreEqual(-18.4, w.Slot(13).Z, 1e-9);
            Assert.AreEqual(Math.PI / 2, w.LineFace, 1e-9);
            Assert.AreEqual(-96.6, w.LeaveGoal.X, 1e-9); Assert.AreEqual(-18.4, w.LeaveGoal.Z, 1e-9);
            var a = new CrowdGroup(14, 917);
            a.SetMode("queue", new CrowdGroup.ModeOpts { At = TownCrowdPlan.AssemblyQueue, Dir = new Pt(0, 1), Spacing = 0.85, Leave = new Pt(1, 0) });
            Assert.AreEqual(-62.6, a.Slot(0).X, 1e-9); Assert.AreEqual(-44.1, a.Slot(0).Z, 1e-9);
            Assert.AreEqual(-62.6, a.Slot(13).X, 1e-9); Assert.AreEqual(-33.05, a.Slot(13).Z, 1e-9);
            Assert.AreEqual(0, a.LineFace, 1e-9);
            Assert.AreEqual(-44.6, a.LeaveGoal.X, 1e-9); Assert.AreEqual(-44.6, a.LeaveGoal.Z, 1e-9);
        }

        // ---------- town ----------

        static int Shown(CrowdGroup g) => g == null || !g.Visible ? 0 : g.Bodies.Count(b => b.Visible);

        [Test]
        public void TownModes_GroupsAndCounts()
        {
            var t = new TownCrowd(null, null);
            Assert.AreEqual("normal", t.Mode);
            Assert.AreEqual(26, Shown(t.Group("main"))); Assert.AreEqual("stroll", t.Group("main").Mode);
            t.Apply("crowd", "busy");
            Assert.AreEqual(26, Shown(t.Group("main"))); Assert.AreEqual(14, Shown(t.Group("extra")));
            t.Apply("crowd", "sparse");
            Assert.AreEqual(0, Shown(t.Group("main"))); Assert.AreEqual(8, Shown(t.Group("few")));
            t.Apply("crowd", "evacuating");
            Assert.AreEqual("brisk", t.Group("main").Mode); Assert.AreEqual("brisk", t.Group("extra").Mode);
            Assert.AreEqual(40, Shown(t.Group("main")) + Shown(t.Group("extra")));
            t.Apply("crowd", "queue");
            Assert.AreEqual(14, Shown(t.Group("line"))); Assert.AreEqual("queue", t.Group("line").Mode);
            Assert.AreEqual(8, Shown(t.Group("few"))); Assert.AreEqual("stroll", t.Group("few").Mode);
            Assert.AreEqual(0, Shown(t.Group("main")));
            t.Apply("crowd", "assembly");
            Assert.AreEqual("idle", t.Group("main").Mode); Assert.AreEqual(26, Shown(t.Group("main")));
            Assert.AreEqual(14, Shown(t.Group("line")));
            foreach (var b in t.Group("main").Bodies) Assert.IsTrue(TownCrowdPlan.AssemblyWaitArea.Contains(b.X, b.Z) && TownCrowdPlan.AssemblyArea.Contains(b.X, b.Z), $"{b.X},{b.Z}"); // polish: clear of the two lines
            t.Apply("crowd", "none");
            Assert.AreEqual(0, t.Bodies.Count(b => b.Visible));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unknown"));
            t.Apply("crowd", "riot");
            Assert.AreEqual("none", t.Mode);
        }

        [Test]
        public void TownGroups_LazySeedsAndTier()
        {
            var t = new TownCrowd(null, null);
            t.Apply("crowd", "queue");
            Assert.AreEqual(900 * 1000 + 1, t.Group("main").Bodies[0].Seed);
            Assert.AreEqual(917 * 1000 + 1, t.Group("line").Bodies[0].Seed);
            Assert.AreEqual(934 * 1000 + 1, t.Group("few").Bodies[0].Seed);
            CrowdTier.Set("low");
            var low = new TownCrowd(null, null);
            Assert.AreEqual(12, low.Group("main").Agents.Count);
        }

        [Test]
        public void TownNpcs_FollowCrowdAndGates()
        {
            var t = new TownCrowd(null, null);
            Assert.IsTrue(t.ClerkOutsideVisible); Assert.IsFalse(t.GuideCVisible); Assert.IsFalse(t.GuideEastVisible);
            t.Apply("gates", JObject.Parse("{\"east\":\"closed\",\"access\":\"open\"}"));
            Assert.IsTrue(t.GuideEastVisible); Assert.IsTrue(t.GuideCVisible);
            t.Apply("crowd", "assembly");
            Assert.IsFalse(t.ClerkOutsideVisible);
        }

        [Test]
        public void EvacRoutes_ByGatesAndClosures()
        {
            var (p, tg) = TownCrowdPlan.EvacFor(TownCrowdPlan.DefaultGates, null);
            Assert.AreEqual(19, p.Count); Assert.AreEqual(3, tg.Count);
            var closed = new Dictionary<string, string> { ["east"] = "closed", ["stairs"] = "closed", ["ramp"] = "closed", ["access"] = "closed" };
            (p, tg) = TownCrowdPlan.EvacFor(closed, null);
            Assert.AreEqual(12, p.Count); Assert.AreEqual(1, tg.Count);
            var all = new Dictionary<string, string>(TownCrowdPlan.DefaultGates) { ["access"] = "open" };
            (p, tg) = TownCrowdPlan.EvacFor(all, null);
            Assert.AreEqual(21, p.Count); Assert.AreEqual(4, tg.Count);
            (p, _) = TownCrowdPlan.EvacFor(TownCrowdPlan.DefaultGates, new[] { "munhwaNorth" });
            var r = TownCrowdPlan.Barricades["munhwaNorth"][0].Grow(1 - 1e-6);
            foreach (var path in p)
                for (int i = 1; i < path.Length; i++)
                    for (double k = 0; k <= 1; k += 0.05)
                        Assert.IsFalse(r.Contains(path[i - 1].X + (path[i].X - path[i - 1].X) * k, path[i - 1].Z + (path[i].Z - path[i - 1].Z) * k));
        }

        [Test]
        public void Signals_CycleAndHolds()
        {
            Assert.AreEqual(20, TownCrowdPlan.GreenLeft(0, "EW"), E);
            Assert.AreEqual(0, TownCrowdPlan.GreenLeft(0, "NS"), E);
            Assert.AreEqual(13, TownCrowdPlan.GreenLeft(24.5, "NS"), E);
            Assert.AreEqual(0, TownCrowdPlan.GreenLeft(42 + 20, "EW"), E);
            var holds = TownCrowdPlan.SignalHolds();
            Assert.AreEqual(8, holds.Count); // yeonmu 4 + munhwa 2 + hanbit 2
            TownCrowdPlan.SetSignals(holds, 0, true);
            foreach (var h in holds) Assert.AreEqual(h.Group == "EW", h.Open, h.Group);
            TownCrowdPlan.SetSignals(holds, 0, false);
            Assert.IsTrue(holds.All(h => h.Open));
        }

        [Test]
        public void WaterQueue_ServesCarriesAndRejoins()
        {
            var g = new CrowdGroup(14, 917);
            g.SetMode("queue", new CrowdGroup.ModeOpts { At = TownCrowdPlan.WaterQueue, Dir = new Pt(1, 0), Spacing = 0.9, Leave = new Pt(-1, 0), Carry = "waterJug" });
            var order = g.Line.ToList();
            CrowdAgent leaver = null; bool sawJug = false, rejoined = false, wasTail = false;
            Run(g.Update, 60, 0.05, () =>
            {
                if (leaver == null) leaver = g.Agents.FirstOrDefault(a => a.State == CrowdGroup.StLeave);
                if (leaver != null && leaver.State == CrowdGroup.StLeave && (leaver.Props & PropMask.WaterJug) != 0 && leaver.MoveAnim == "carry") sawJug = true;
                if (leaver != null && !rejoined && leaver.State == CrowdGroup.StLine)
                {
                    rejoined = true; wasTail = g.Line[g.Line.Count - 1] == leaver && (leaver.Props & PropMask.WaterJug) == 0;
                }
                return rejoined;
            });
            Assert.IsNotNull(leaver, "nobody was served");
            Assert.AreSame(order[0], leaver, "the head is served first");
            Assert.IsTrue(sawJug, "the leaver carries a water jug");
            Assert.IsTrue(rejoined && wasTail, "the leaver rejoins at the tail without the jug");
            var rest = g.Line.Where(a => a != leaver).ToList();
            CollectionAssert.AreEqual(order.Where(a => a != leaver && rest.Contains(a)).ToList(), rest, "line order kept (others may have been served meanwhile)");
            Run(g.Update, 3);
            for (int i = 0; i < g.Line.Count; i++)
                for (int j = i + 1; j < g.Line.Count; j++)
                    Assert.Greater(CrowdGeom.Hypot(g.Line[i].X - g.Line[j].X, g.Line[i].Z - g.Line[j].Z), 0.4);
        }

        [Test]
        public void Holds_StopStrollersUntilOpen()
        {
            var hold = new CrowdGroup.Hold { Zone = new Box2(-2.6, 2.6, -6.6, 6.6), Group = "NS", Open = false };
            var g = new CrowdGroup(1, 5, holds: new List<CrowdGroup.Hold> { hold });
            g.SetPaths(new List<Pt[]> { new[] { new Pt(0, -8), new Pt(0, 8) } });
            g.SetMode("stroll");
            var a = g.Agents[0];
            a.S = 0.5; a.Dir = 1; a.X = -a.Lane; a.Z = -7.5; a.State = CrowdGroup.StMove; a.StopT = 99;
            Run(g.Update, 5, 0.05, () => { Assert.Less(a.Z, -6.6); return false; });
            hold.Open = true;
            double maxZ = a.Z;
            Run(g.Update, 15, 0.05, () => { maxZ = Math.Max(maxZ, a.Z); return maxZ > 6.6; });
            Assert.Greater(maxZ, 6.6);
        }

        // ---------- shelter ----------

        [Test]
        public void Shelter_SeatsArrivingLeavingFire()
        {
            Assert.AreEqual(53, ShelterPeoplePlan.Seats.Length);
            var s = new ShelterPeople(null);
            Assert.AreEqual(-20.426, s.HomeOf(0).X, E); Assert.AreEqual(-3.606, s.HomeOf(0).Z, E);
            Assert.AreEqual(-11.38, s.HomeOf(25).X, E); Assert.AreEqual(14.382, s.HomeOf(25).Z, E);
            Assert.AreEqual(26, s.Seated); Assert.IsTrue(s.Queue.All(b => !b.Visible));
            Assert.AreEqual("idle", s.ManagerAnim);

            s.Apply("crowd", "arriving");
            Assert.AreEqual(12, s.Hidden); Assert.AreEqual(14, s.Seated); Assert.IsTrue(s.Queue.All(b => b.Visible));
            Assert.AreEqual("talk", s.ManagerAnim);
            Run(s.Update, 240);
            Assert.AreEqual(26, s.Seated, "every arrival reaches its seat");

            s.Apply("crowd", "leaving");
            Assert.AreEqual(1, s.Listeners.Count(b => b.Visible));
            Run(s.Update, 400);
            Assert.AreEqual(7, s.Seated);

            s.Apply("crowd", "settled");
            s.Apply("fire", "large");
            Assert.IsFalse(s.Evacuee(0).Visible, "the seat 5.9 m from the fire is left");
        }

        [Test]
        public void Shelter_SeatPathWithDesk()
        {
            var seat = ShelterPeoplePlan.Seats[0];
            var p = ShelterPeoplePlan.SeatPath("stairs", seat, true);
            Assert.AreEqual(2.55, p[6].X, E); Assert.AreEqual(-9.5, p[6].Z, E); Assert.AreEqual(2.6, p[6].Wait, E); Assert.AreEqual("talk", p[6].Anim);
            int n = p.Count;
            Assert.AreEqual(-19.3, p[n - 3].X, E); Assert.AreEqual(ShelterPeoplePlan.AN, p[n - 3].Z, E);
            Assert.AreEqual(-19.3, p[n - 2].X, E); Assert.AreEqual(seat.Z, p[n - 2].Z, E);
            Assert.AreEqual(seat.X, p[n - 1].X, E); Assert.AreEqual(seat.Z, p[n - 1].Z, E);
            Assert.AreEqual(8 + 1 + 3, n);
        }

        // ---------- mart ----------

        [Test]
        public void Mart_NormalAndBusy()
        {
            var m = new MartShoppers(null);
            Assert.AreEqual(6, m.VisibleShoppers); Assert.AreEqual(0, m.VisibleQueue);
            Assert.AreEqual(-14.26, m.ShopperBody(0).X, E); Assert.AreEqual(-7.4, m.ShopperBody(0).Z, E);
            Assert.AreEqual(PropMask.Cart, m.ShopperBody(0).Props); Assert.AreEqual(PropMask.Basket, m.ShopperBody(1).Props);
            m.Apply("crowd", "busy");
            Assert.AreEqual(12, m.VisibleShoppers); Assert.AreEqual(6, m.VisibleQueue);
            Run(m.Update, 20); // wanders without throwing
            m.Apply("crowd", "normal");
            Assert.AreEqual(6, m.VisibleShoppers); Assert.AreEqual(0, m.VisibleQueue);
        }

        // ---------- heights and solids ----------

        [Test]
        public void WalkHeight_TownHighestShelterLowest()
        {
            var town = WalkHeight.FromLayout(Layout("Town"), WalkHeight.Pick.Highest);
            Assert.AreEqual(0.12, town.At(0, 8), 1e-5);
            Assert.AreEqual(0, town.At(0, 0), 1e-5);
            var zones = new[]
            {
                new WalkHeight.Zone { X0 = 0, X1 = 2, Z0 = 0, Z1 = 2, Y = 1.785 },
                new WalkHeight.Zone { X0 = 0, X1 = 2, Z0 = 0, Z1 = 4, Ramp = true, Y0 = 0, Y1 = 1.785 },
            };
            Assert.AreEqual(1.785 / 4, new WalkHeight(zones, WalkHeight.Pick.Lowest).At(1, 1), 1e-9);
            Assert.AreEqual(1.785, new WalkHeight(zones, WalkHeight.Pick.Highest).At(1, 1), 1e-9);
            Assert.AreEqual(0, new WalkHeight(zones, WalkHeight.Pick.Lowest).At(5, 5), 1e-9);
        }

        [Test]
        public void SolidGrid_CollideAndSwitch()
        {
            var box = new SolidGrid.Solid { X0 = 0, X1 = 1, Y0 = 0, Y1 = 2, Z0 = 0, Z1 = 1 };
            var (x, z) = SolidGrid.Collide(1.1, 0.5, 0, new List<SolidGrid.Solid> { box }, 0.24);
            Assert.AreEqual(1.24, x, 1e-9); Assert.AreEqual(0.5, z, 1e-9);

            var grid = SolidGrid.FromLayout(Layout("Town"));
            var danji = grid.All.First(s => s.Id == "closure:danji");
            double cx = (danji.X0 + danji.X1) / 2, cz = (danji.Z0 + danji.Z1) / 2;
            var near = new List<SolidGrid.Solid>();
            grid.Query(cx - 0.6, cx + 0.6, cz - 0.6, cz + 0.6, near);
            Assert.IsTrue(near.Contains(danji));
            var (ox, oz) = SolidGrid.Collide(cx, cz, danji.Y0, near, 0.24, 1.7);
            Assert.AreEqual(cx, ox, 1e-9, "a closure barricade is off by default"); Assert.AreEqual(cz, oz, 1e-9);
            grid.SetOff("closure:danji", false);
            (ox, oz) = SolidGrid.Collide(cx, cz, danji.Y0, near, 0.24, 1.7);
            Assert.IsTrue(Math.Abs(ox - cx) > 0.01 || Math.Abs(oz - cz) > 0.01, "switched on, it pushes out");
        }

        [Test]
        public void SeedPhases_AreSpreadLikePeopleJs()
        {
            // people.js: o = (seed * 0.618034) % 1
            Assert.AreEqual((900 * 0.618034) % 1, CrowdView.PhaseOf(900), 1e-5);
            var ph = Enumerable.Range(0, 14).Select(k => CrowdView.PhaseOf(917 + k)).ToList();
            Assert.IsTrue(ph.All(p => p >= 0 && p < 1));
            Assert.Greater(ph.Max() - ph.Min(), 0.8f, "a queue's members start their cycles spread out, not in lockstep");
        }
    }
}
