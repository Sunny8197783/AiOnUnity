using System;
using System.Collections.Generic;
using AiOn.Story;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class DangerFxTests
    {
        [Test]
        public void PickGround_TakesTheHighestWalkZone_IgnoresRoofsAndCeilings_NullWhenNone()
        {
            Assert.AreEqual(3.2f, DangerFxMath.PickGround(new List<(float, bool)> { (9f, false), (3.2f, true), (0f, true) }));
            Assert.IsNull(DangerFxMath.PickGround(new List<(float, bool)> { (9f, false) }));
            Assert.IsNull(DangerFxMath.PickGround(new List<(float, bool)>()));
        }

        [Test]
        public void IsWalkable_OnlyUnderFloorsOrStairs()
        {
            var root = new GameObject("Collision");
            try
            {
                var floors = new GameObject("Floors"); floors.transform.SetParent(root.transform);
                var f = new GameObject("Floor_0"); f.transform.SetParent(floors.transform);
                var stairs = new GameObject("Stairs"); stairs.transform.SetParent(root.transform);
                var ramp = new GameObject("RampCollider"); ramp.transform.SetParent(stairs.transform);
                var wall = new GameObject("Solid_0"); wall.transform.SetParent(root.transform);
                Assert.IsTrue(DangerFxMath.IsWalkable(f.transform));
                Assert.IsTrue(DangerFxMath.IsWalkable(ramp.transform));
                Assert.IsFalse(DangerFxMath.IsWalkable(wall.transform));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void BlastShake_FallsWithDistance_NeverBelowAFloor()
        {
            Assert.AreEqual(0.6f, DangerFxMath.BlastShake(0), 1e-5);
            Assert.AreEqual(0.4f, DangerFxMath.BlastShake(10), 1e-5);
            Assert.AreEqual(0.04f, DangerFxMath.BlastShake(100), 1e-5);
        }

        [Test]
        public void Velocity_FlipsZOnly()
        {
            Assert.AreEqual(new Vector3(1, 2, -3), DangerFxMath.ToUnityVelocity(1, 2, 3));
        }

        [Test]
        public void BlastCurve_StartsBrightAndSmall_EndsDarkAndWide()
        {
            var a = DangerFxMath.BlastCurve(1, 1);
            var b = DangerFxMath.BlastCurve(0, 1);
            Assert.AreEqual(1f, a.light, 1e-5); Assert.AreEqual(0f, b.light, 1e-5);
            Assert.AreEqual(2f, a.ballScale, 1e-5); Assert.AreEqual(9f, b.ballScale, 1e-5);
            Assert.AreEqual(1f, a.ringScale, 1e-5); Assert.AreEqual(17f, b.ringScale, 1e-5);
            Assert.AreEqual(0.55f, a.ringAlpha, 1e-5); Assert.AreEqual(0f, b.ringAlpha, 1e-5);
            Assert.AreEqual(34f, DangerFxMath.BlastCurve(0, 2).ringScale, 1e-5);
        }

        [Test]
        public void FlashDecay_GoneInHalfASecond()
        {
            float k = 1;
            for (int i = 0; i < 30; i++) k = DangerFxMath.FlashDecay(k, 1 / 60f);
            Assert.AreEqual(0f, k, 1e-4);
            Assert.AreEqual(0f, DangerFxMath.FlashDecay(0.1f, 1));
        }

        [Test]
        public void Evict_KeepsUnderTheCap()
        {
            Assert.AreEqual(0, DangerFxMath.Evict(0, 20));
            Assert.AreEqual(0, DangerFxMath.Evict(19, 20));
            Assert.AreEqual(1, DangerFxMath.Evict(20, 20));
            Assert.AreEqual(6, DangerFxMath.Evict(25, 20));
        }

        [Test]
        public void RubbleChunks_StayInsideTheFootprint_HigherTowardTheMiddle()
        {
            var rng = new System.Random(3);
            var chunks = DangerFxMath.RubbleChunks(6, 4, 1.4f, rng.NextDouble);
            Assert.AreEqual(60, chunks.Count);
            foreach (var (pos, _, scale) in chunks)
            {
                Assert.LessOrEqual(Math.Abs(pos.x), 3f + 1e-4);
                Assert.LessOrEqual(Math.Abs(pos.z), 2f + 1e-4);
                Assert.GreaterOrEqual(pos.y, 0f);
                Assert.LessOrEqual(pos.y, 1.4f * (1 - Math.Max(Math.Abs(pos.x) / 3, Math.Abs(pos.z) / 2)) + 1e-4);
                Assert.Greater(scale.y, 0f);
            }
        }

        [TestCase(6f, 4f, 1.4f)]
        [TestCase(4f, 6f, 1.4f)]
        [TestCase(10f, 10f, 1.4f)]
        [TestCase(2f, 3f, 1.4f)] // too narrow for h: the top is lowered, not steepened
        public void RubbleHull_CoversTheFootprint_SidesClimbable(float w, float d, float h)
        {
            var v = DangerFxMath.RubbleHull(w, d, h);
            Assert.AreEqual(8, v.Length);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(-0.5f, v[i].y, 1e-5);
                Assert.AreEqual(w / 2, Math.Abs(v[i].x), 1e-5);
                Assert.AreEqual(d / 2, Math.Abs(v[i].z), 1e-5);
                Assert.LessOrEqual(v[i + 4].y, h + 1e-5);
                Assert.Greater(v[i + 4].y, 0f);
                Assert.LessOrEqual(Math.Abs(v[i + 4].x), w / 2); Assert.LessOrEqual(Math.Abs(v[i + 4].z), d / 2);
                // side slopes along x and z
                float rise = v[i + 4].y - v[i].y;
                float runX = Math.Abs(v[i].x) - Math.Abs(v[i + 4].x), runZ = Math.Abs(v[i].z) - Math.Abs(v[i + 4].z);
                Assert.LessOrEqual(Mathf.Atan2(rise, runX) * Mathf.Rad2Deg, DangerFxMath.RubbleMaxSlopeDeg + 1e-3f);
                Assert.LessOrEqual(Mathf.Atan2(rise, runZ) * Mathf.Rad2Deg, DangerFxMath.RubbleMaxSlopeDeg + 1e-3f);
            }
            if (Math.Min(w, d) >= 4) Assert.AreEqual(h, v[4].y, 1e-5); // the original's heap height where it fits
        }

        [Test]
        public void RubbleHullTris_FaceOutward()
        {
            var v = DangerFxMath.RubbleHull(6, 4, 1.4f);
            var t = DangerFxMath.RubbleHullTris;
            Assert.AreEqual(36, t.Length);
            var centre = Vector3.zero; foreach (var p in v) centre += p; centre /= v.Length;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a); // Unity's front-face normal (clockwise)
                Assert.Greater(Vector3.Dot(n, (a + b + c) / 3 - centre), 0f, $"triangle {i / 3}");
            }
        }

        [Test]
        public void IsCover_FalseForWalkZonesAndCeilings()
        {
            var root = new GameObject("Collision");
            try
            {
                GameObject Under(string parent, string name)
                {
                    var p = new GameObject(parent); p.transform.SetParent(root.transform);
                    var c = new GameObject(name); c.transform.SetParent(p.transform);
                    return c;
                }
                Assert.IsFalse(DangerFxMath.IsCover(Under("Floors", "Floor_0").transform));
                Assert.IsFalse(DangerFxMath.IsCover(Under("Stairs", "RampCollider").transform));
                Assert.IsFalse(DangerFxMath.IsCover(Under("Ceilings", "Ceiling_0").transform));
                Assert.IsTrue(DangerFxMath.IsCover(Under("Solids", "Solid_0").transform));
                Assert.IsTrue(DangerFxMath.IsCover(Under("DangerFx", "Rubble").transform));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
