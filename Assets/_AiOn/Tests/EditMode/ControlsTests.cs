using System.Collections.Generic;
using AiOn.Player;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>Vault / climb planning (engine/vault.js), the hands' rules (engine/actions.js) and the player body's stair clip.
    /// Expected values were computed with the original modules (node, engine/vault.js + space.js + geom.js).</summary>
    public class ControlsTests
    {
        static VaultPlanner.World Town(params VaultPlanner.Box[] solids)
        {
            var w = new VaultPlanner.World { Indoor = false };
            w.Walk.Add(VaultPlanner.Walk.Flat(-10, 10, -10, 10, 0));
            w.Solids.AddRange(solids);
            return w;
        }

        [Test]
        public void Vault_OverALowWall_LandsOnTheFarSide()
        {
            var p = VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9)), 0.3, 0, 0, 0, -1);
            Assert.IsTrue(p.HasValue);
            Assert.AreEqual("over", p.Value.Kind);
            Assert.AreEqual(0.3, p.Value.X1, 1e-9);
            Assert.AreEqual(-1.6, p.Value.Z1, 1e-9);
            Assert.AreEqual(0, p.Value.Y1, 1e-9);
            Assert.AreEqual(1.2, p.Value.Arc, 1e-9);
            Assert.AreEqual("vault", p.Value.Clip);
            Assert.AreEqual(0.65, p.Value.Duration, 1e-9);
            Assert.AreEqual(0.08, p.Value.Stamina, 1e-9);
        }

        [Test]
        public void Vault_OverACar_AtAnAngle_GoesStraightAcrossTheFace()
        {
            var p = VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-2.2, 2.2, 0, 1.4, -1.9, -0.9)), 0.5, 0, 0, 0.1, -0.995);
            Assert.IsTrue(p.HasValue);
            Assert.AreEqual("over", p.Value.Kind);
            Assert.AreEqual(0.5, p.Value.X1, 1e-9);
            Assert.AreEqual(-2.4, p.Value.Z1, 1e-9);
            Assert.AreEqual(1.6, p.Value.Arc, 1e-9);
        }

        [Test]
        public void Climb_OntoATallCrate()
        {
            var p = VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-1, 1, 0, 1.6, -2.5, -0.5)), 0, 0, 0, 0, -1);
            Assert.IsTrue(p.HasValue);
            Assert.AreEqual("onto", p.Value.Kind);
            Assert.AreEqual(0, p.Value.X1, 1e-9);
            Assert.AreEqual(-0.95, p.Value.Z1, 1e-9);
            Assert.AreEqual(1.6, p.Value.Y1, 1e-9);
            Assert.AreEqual("climb", p.Value.Clip);
            Assert.AreEqual(0.35 + 1.6 * 0.3, p.Value.Duration, 1e-9);
        }

        [Test]
        public void Vault_NeverThroughAWall_NorWhereTheLevelSaysNo()
        {
            Assert.IsNull(VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-3, 3, 0, 3, -1.1, -0.9)), 0, 0, 0, 0, -1), "a 3 m wall");
            Assert.IsNull(VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9) { NoVault = true }), 0, 0, 0, 0, -1), "noVault (and too thin to climb)");
            Assert.IsNull(VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-1, 1, 0, 1.6, -2.5, -0.5) { NoClimb = true }), 0, 0, 0, 0, -1), "noClimb");
            Assert.IsNull(VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9) { Npc = true }), 0, 0, 0, 0, -1), "a person");
            Assert.IsNull(VaultPlanner.PlanVault(Town(), 0, 0, 0, 0, -1), "nothing in front: a jump");
            Assert.IsNull(VaultPlanner.PlanVault(Town(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9)), 0, 0, 0, 0, 1), "facing away");
        }

        [Test]
        public void Climb_IndoorsWithoutCeilingData_NotOntoATable()
        {
            var w = Town(new VaultPlanner.Box(-1, 1, 0, 0.8, -1.8, -0.6));
            Assert.IsNotNull(VaultPlanner.PlanVault(w, 0.2, 0, 0, 0, -1), "outdoors it can be stood on");
            w.Indoor = true;
            Assert.IsNull(VaultPlanner.PlanVault(w, 0.2, 0, 0, 0, -1), "a 2.3 m room leaves no headroom on top");
        }

        [Test]
        public void Vault_NeedsALandingWithGroundAndRoom()
        {
            var w = new VaultPlanner.World();
            w.Walk.Add(VaultPlanner.Walk.Flat(-10, 10, -1, 10, 0)); // nothing beyond the wall
            w.Solids.Add(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9));
            Assert.IsNull(VaultPlanner.PlanVault(w, 0.3, 0, 0, 0, -1), "no floor on the far side");
            var blocked = Town(new VaultPlanner.Box(-2, 2, 0, 1.0, -1.1, -0.9), new VaultPlanner.Box(-2, 2, 0, 3, -1.9, -1.7));
            Assert.IsNull(VaultPlanner.PlanVault(blocked, 0.3, 0, 0, 0, -1), "a wall just past it");
        }

        [Test]
        public void MoveAt_EasesAlongTheLineWithAnArc()
        {
            var p = new VaultPlanner.Plan { Kind = "over", X1 = 0, Z1 = -2, Y1 = 0, Arc = 1.2, H = 1 };
            var (x0, y0, z0) = VaultPlanner.MoveAt(0, 0, 0, p, 0);
            var (xm, ym, zm) = VaultPlanner.MoveAt(0, 0, 0, p, 0.5);
            var (x1, y1, z1) = VaultPlanner.MoveAt(0, 0, 0, p, 1);
            Assert.AreEqual(0, z0, 1e-9); Assert.AreEqual(-1, zm, 1e-9); Assert.AreEqual(-2, z1, 1e-9);
            Assert.AreEqual(1.2, ym, 1e-9); Assert.AreEqual(0, y1, 1e-9); Assert.AreEqual(0, x1 + x0 + xm, 1e-9); Assert.AreEqual(0, y0, 1e-9);
        }

        [Test]
        public void Geom_GroundAtRampsAndHeadroom()
        {
            var walk = new List<VaultPlanner.Walk> { new VaultPlanner.Walk { X0 = 0, X1 = 2, Z0 = 0, Z1 = 4, Ramp = true, Y0 = 0, Y1 = 2 } };
            Assert.AreEqual(1.0, VaultPlanner.GroundAt(walk, 1, 2, 1).Value, 1e-9);
            Assert.IsNull(VaultPlanner.GroundAt(walk, 1, 2, 3), "too far below the feet");
            Assert.AreEqual(1.0, VaultPlanner.GroundAt(walk, 1, 2, 3, any: true).Value, 1e-9);
            var w = new VaultPlanner.World { Indoor = true };
            Assert.AreEqual(2.3, VaultPlanner.Headroom(w, 0, 0, 0), 1e-9);
            w.Ceilings.Add(new VaultPlanner.Ceiling { X0 = -1, X1 = 1, Z0 = -1, Z1 = 1, Y = 3.1 });
            Assert.AreEqual(3.1, VaultPlanner.Headroom(w, 0, 0, 0), 1e-9);
            Assert.AreEqual(double.PositiveInfinity, VaultPlanner.Headroom(new VaultPlanner.World(), 0, 0, 0));
        }

        [Test]
        public void Rng_IsTheOriginalsMulberry32()
        {
            var r = new LooseThings.Rng(4 * 7919 + 13);
            Assert.AreEqual(0.02866826835088432, r.Next(), 1e-15);
            Assert.AreEqual(0.7347562236245722, r.Next(), 1e-15);
            Assert.AreEqual(0.2480041258968413, r.Next(), 1e-15);
            Assert.AreEqual(0.7674791167955846, r.Next(), 1e-15);
        }

        [Test]
        public void Scatter_MatchesTheOriginal_AndAvoidsSolidsAndRamps()
        {
            var walk = new List<VaultPlanner.Walk>
            {
                VaultPlanner.Walk.Flat(0, 6, 0, 5, 0), VaultPlanner.Walk.Flat(6, 7, 0, 1, 0),
                new VaultPlanner.Walk { X0 = 0, X1 = 3, Z0 = 5, Z1 = 9, Ramp = true, Y0 = 0, Y1 = 2 },
            };
            var solids = new List<VaultPlanner.Box> { new VaultPlanner.Box(2, 4, 0, 1, 1, 3) };
            var got = LooseThings.Scatter("store", walk, solids);
            var want = new[]
            {
                ("bottle", 1.651709, 3.348162, 5.869287), ("box", 4.764647, 0.678497, 1.596230), ("box", 4.673398, 4.075485, 5.380779),
                ("can", 4.344244, 2.919343, 0.950680), ("box", 5.082591, 0.724646, 3.015162), ("can", 1.693384, 4.045579, 5.214165),
                ("box", 4.856809, 1.046449, 2.663543), ("box", 4.814721, 2.359831, 5.185546),
            };
            Assert.AreEqual(want.Length, got.Count);
            for (int i = 0; i < want.Length; i++)
            {
                Assert.AreEqual(want[i].Item1, got[i].Kind, $"#{i}");
                Assert.AreEqual(want[i].Item2, got[i].X, 1e-6); Assert.AreEqual(want[i].Item3, got[i].Z, 1e-6); Assert.AreEqual(want[i].Item4, got[i].Yaw, 1e-6);
                Assert.AreEqual(LooseThings.Kinds[got[i].Kind].R, got[i].Y, 1e-9, "rests on the floor");
                Assert.IsTrue(got[i].X < 6, "never on the 1 m² zone");
            }
        }

        [Test]
        public void Scatter_TownHasFortyBricksRocksBottlesAndCans()
        {
            var walk = new List<VaultPlanner.Walk> { VaultPlanner.Walk.Flat(-50, 50, -50, 50, 0) };
            var got = LooseThings.Scatter("town", walk, new List<VaultPlanner.Box>());
            Assert.AreEqual(40, got.Count);
            foreach (var p in got) CollectionAssert.Contains(new[] { "brick", "rock", "bottle", "can" }, p.Kind);
            Assert.AreEqual(8, LooseThings.Scatter("office", walk, new List<VaultPlanner.Box>()).Count);
            Assert.AreEqual(0, LooseThings.Scatter("office", new List<VaultPlanner.Walk>(), null).Count);
            Assert.AreEqual("G · 벽돌 줍기", LooseThings.PickLabel("brick"));
            Assert.AreEqual("G · 빈 병 줍기", LooseThings.PickLabel("bottle"));
        }

        [Test]
        public void Bounce_FloorAndWall()
        {
            double vx = 4, vy = -6, vz = 0;
            var b = LooseThings.Hit(ref vx, ref vy, ref vz, 0, 1, 0, "bottle", out bool rest);
            Assert.AreEqual(LooseThings.Bounce.Glass, b);
            Assert.IsFalse(rest);
            Assert.AreEqual(1.8, vy, 1e-9); Assert.AreEqual(2.2, vx, 1e-9);
            vx = 0.3; vy = -0.5; vz = 0;
            Assert.AreEqual(LooseThings.Bounce.None, LooseThings.Hit(ref vx, ref vy, ref vz, 0, 1, 0, "brick", out rest));
            Assert.IsTrue(rest, "slow on the floor: it stays");
            vx = 10; vy = 0; vz = 0; // straight into a wall facing -x
            Assert.AreEqual(LooseThings.Bounce.Thud, LooseThings.Hit(ref vx, ref vy, ref vz, -1, 0, 0, "brick", out rest));
            Assert.AreEqual(-3.5 * 0.7, vx, 1e-9);
        }

        [Test]
        public void Throw_StrikesAPersonOnThePath()
        {
            Assert.IsTrue(LooseThings.Strikes(0, 1.2, 0, 0, 1.1, 1, 0.2, 0, 0.5));
            Assert.IsFalse(LooseThings.Strikes(0, 1.2, 0, 0, 1.1, 1, 0.6, 0, 0.5), "beside the line");
            Assert.IsFalse(LooseThings.Strikes(0, 2.2, 0, 0, 2.1, 1, 0.1, 0, 0.5), "over the head");
            Assert.IsFalse(LooseThings.Strikes(0, 1.2, 0, 0, 1.1, 1, 0.1, 0, 2.0), "beyond where it stopped");
        }

        [Test]
        public void Body_StairClipByTheSlopeAndTheWayItGoes()
        {
            var stair = new Vector3(0, 1, -0.7f).normalized; // rises toward +z (normal leans to -z, downhill)
            Assert.AreEqual("stairsUp", PlayerBody.StairAnim(stair, new Vector3(0, 0, 2), 2));
            Assert.AreEqual("stairsDown", PlayerBody.StairAnim(stair, new Vector3(0, 0, -2), 2));
            Assert.IsNull(PlayerBody.StairAnim(Vector3.up, new Vector3(0, 0, 2), 2), "flat floor");
            Assert.IsNull(PlayerBody.StairAnim(new Vector3(0, 1, -0.2f).normalized, new Vector3(0, 0, 2), 2), "a gentle ramp");
            Assert.IsNull(PlayerBody.StairAnim(stair, Vector3.zero, 0), "standing");
        }
    }
}
