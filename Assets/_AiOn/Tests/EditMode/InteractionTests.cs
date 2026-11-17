using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Interaction;
using AiOn.Levels;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    public class InteractionPickerTests
    {
        sealed class P : IReachable
        {
            public Vector3 Position { get; set; }
            public float Radius { get; set; }
            public float ReachOverride { get; set; }
            public bool Enabled { get; set; } = true;
            public string Name;
            public override string ToString() => Name;
        }

        static readonly float[] North = { 0f, 0f }; // Unity yaw 0 faces +z

        [Test]
        public void Reach_is_never_under_1_6_m_unless_overridden()
        {
            Assert.AreEqual(1.6f, InteractionPicker.ReachOf(new P { Radius = 0 }));
            Assert.AreEqual(1.6f, InteractionPicker.ReachOf(new P { Radius = 1.3f }));
            Assert.AreEqual(1.9f, InteractionPicker.ReachOf(new P { Radius = 1.9f }));
            Assert.AreEqual(1.2f, InteractionPicker.ReachOf(new P { Radius = 1.4f, ReachOverride = 1.2f }), "stairsWet");
        }

        [Test]
        public void In_reach_in_view_and_within_the_height_band()
        {
            var feet = Vector3.zero;
            Assert.That(InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, 1.5f) }, feet, North), Is.EqualTo(1.5f).Within(1e-4));
            Assert.IsTrue(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, 1.7f) }, feet, North)), "too far");
            Assert.IsTrue(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, 2.3f, 1f) }, feet, North)), "too high");
            Assert.IsTrue(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, -1.9f, 1f) }, feet, North)), "too low");
            Assert.IsTrue(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, 1f), Enabled = false }, feet, North)), "disabled");
        }

        [Test]
        public void Behind_the_view_only_when_very_close()
        {
            var feet = Vector3.zero;
            Assert.IsTrue(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, -1.4f) }, feet, North)), "behind, 1.4 m");
            float close = InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, -1.1f) }, feet, North);
            Assert.That(close, Is.EqualTo(1.1f + Mathf.PI).Within(1e-3), "behind but under 1.2 m: distance + 180° off");
            float side = InteractionPicker.RawScore(new P { Position = new Vector3(1.5f, 0, 0) }, feet, North);
            Assert.That(side, Is.EqualTo(1.5f + Mathf.PI / 2).Within(1e-3), "90° to the right is inside the 100° cone");
            Assert.IsFalse(float.IsPositiveInfinity(InteractionPicker.RawScore(new P { Position = new Vector3(0, 0, -1.4f) }, feet, new[] { 0f, 180f })), "the body faces it");
        }

        [Test]
        public void Best_score_wins_and_the_current_point_is_kept_unless_clearly_beaten()
        {
            var picker = new InteractionPicker();
            var a = new P { Name = "a", Position = new Vector3(0, 0, 1.0f) };
            var b = new P { Name = "b", Position = new Vector3(0, 0, 1.2f) };
            Assert.AreSame(a, picker.Pick(new[] { a, b }, Vector3.zero, North));
            a.Position = new Vector3(0, 0, 1.4f); // now b is better by 0.2: under the 0.3 keep margin
            Assert.AreSame(a, picker.Pick(new[] { a, b }, Vector3.zero, North));
            a.Position = new Vector3(0, 0, 1.55f); // better by 0.35
            Assert.AreSame(b, picker.Pick(new[] { a, b }, Vector3.zero, North));
            Assert.AreSame(a, picker.Pick(new[] { a, b }, Vector3.zero, North, p => p == b), "a wall hides b");
            Assert.IsNull(picker.Pick(new IReachable[0], Vector3.zero, North));
        }
    }

    public class DoorTests
    {
        static LevelLayout Apartment => AssetDatabase.LoadAssetAtPath<LevelLayout>("Assets/_AiOn/Data/Levels/ApartmentLayout.asset");

        /// <summary>The original's leaf free edge (three.js frame), apartment-geo.js leafBoxes.</summary>
        static Vector2 OriginalFreeEdge(LevelLayout.DoorDef d, float k)
        {
            float lw = d.b - d.a - 0.03f;
            int sgn = d.hinge == "a" ? 1 : -1;
            float hu = d.hinge == "a" ? d.a + 0.015f : d.b - 0.015f;
            float px = d.axis == "z" ? hu : d.c, pz = d.axis == "z" ? d.c : hu;
            float lx = d.axis == "z" ? sgn * lw : 0, lz = d.axis == "z" ? 0 : sgn * lw;
            int ang = d.axis == "z" ? (d.hinge == "a" ? -d.dir : d.dir) : (d.hinge == "a" ? d.dir : -d.dir);
            float phi = ang * k * Mathf.PI / 2;
            return new Vector2(px + lx * Mathf.Cos(phi) + lz * Mathf.Sin(phi), pz - lx * Mathf.Sin(phi) + lz * Mathf.Cos(phi));
        }

        [Test]
        public void Every_door_swings_like_the_original([Values(0f, 0.3f, 0.72f, 1f)] float k)
        {
            Assert.That(Apartment.doors.Count, Is.EqualTo(10));
            foreach (var d in Apartment.doors)
            {
                var o = OriginalFreeEdge(d, k);
                var u = Door.FreeEdge(d, k);
                Assert.That(Vector3.Distance(u, LevelLayout.ToUnity(o.x, d.y, o.y)), Is.LessThan(1e-3f), $"{d.id} at k={k}");
            }
        }

        [Test]
        public void Leaves_lie_where_the_original_put_their_collision_boxes()
        {
            // The level data holds each leaf's boxes as the original built them: 302 shut, the bedroom door wide open.
            var l = Apartment;
            foreach (var (doorId, boxes) in new[]
            {
                ("door302", l.solids.Where(s => s.id == "door302").ToList()),
                ("homeRoom", l.solids.Where(s => string.IsNullOrEmpty(s.id) && !s.off && s.y0 == 5.75f && s.z0 > 3.9f && s.z1 < 4.1f && s.x1 - s.x0 < 0.35f).ToList()),
                ("door303", l.solids.Where(s => s.id == "door303").ToList()),
            })
            {
                Assert.That(boxes.Count, Is.EqualTo(3), doorId);
                var door = l.doors.First(d => d.id == doorId);
                var o = OriginalFreeEdge(door, door.open);
                float x0 = boxes.Min(b => b.x0), x1 = boxes.Max(b => b.x1), z0 = boxes.Min(b => b.z0), z1 = boxes.Max(b => b.z1);
                Assert.That(o.x >= x0 - 0.01f && o.x <= x1 + 0.01f && o.y >= z0 - 0.01f && o.y <= z1 + 0.01f, Is.True,
                    $"{doorId}: free edge ({o.x}, {o.y}) outside its boxes x {x0}..{x1} z {z0}..{z1}");
            }
        }
    }

    public class ApartmentWorldTests
    {
        static ApartmentWorld.Context Ctx(long seed = 1, bool power = true)
        {
            var (rules, g, facts) = GameSession.CreateDetached(Db, Fixture("../../../Data/Source/facts.json"), seed);
            return new ApartmentWorld.Context { Rules = rules, G = g, Power = power, Fact = id => facts.TryGetValue(id, out var t) ? t : null };
        }

        static readonly string[] PointIds =
        {
            "homeSink", "homeWindow", "homeFridge", "homeGas", "homeBreaker", "homeDesk", "homeBed", "homeStock", "homePlanner",
            "homeRadio", "homeBag", "lobbyNotice", "managerWindow", "elevator", "stairs1F", "stairs3F", "extinguisher",
            "seoyeonDoor", "elderDoor", "homeDoor",
        };

        [Test]
        public void Every_description_line_is_the_original_text_or_a_verified_fact()
        {
            var original = new HashSet<string>(JArray.Parse(Fixture("apartment-world-lines.json")).Select(t => (string)t));
            foreach (var power in new[] { true, false })
                foreach (var day in new[] { -3, 1 })
                    foreach (var curtain in new[] { false, true })
                    {
                        var c = Ctx(power: power);
                        c.G.Day = day;
                        if (curtain) c.G.SetKnow("curtain", true);
                        foreach (var id in PointIds)
                        {
                            var r = ApartmentWorld.Use(c, id, id);
                            Assert.That(r.Lines, Is.Not.Empty, id);
                            foreach (var line in r.Lines)
                                Assert.That(original.Contains(line) || c.Fact("food_door") == line || c.Fact("alert_stairs") == line, Is.True, $"{id}: '{line}' is not in the original");
                        }
                    }
        }

        [Test]
        public void Blackout_descriptions_add_the_verified_fact()
        {
            var c = Ctx(power: false);
            CollectionAssert.Contains(ApartmentWorld.Use(c, "homeFridge", "냉장고").Lines, c.Fact("food_door"));
            CollectionAssert.Contains(ApartmentWorld.Use(c, "elevator", "엘리베이터").Lines, c.Fact("alert_stairs"));
            Assert.That(c.Fact("food_door"), Does.Contain("냉장고 문"));
        }

        [Test]
        public void The_wallet_puts_the_ID_card_in_the_bag_once()
        {
            var c = Ctx();
            var r = ApartmentWorld.Use(c, "homeWallet", "지갑 · 신분증");
            Assert.That(r.Toast, Does.StartWith("지갑과 신분증을 챙겼다."));
            Assert.That(r.Toast, Does.Contain(c.Fact("docs_take")));
            AssertJson(c.G.Items["id_card"], "{home:0,bag:1,bought:0,checked:true,used:0}");
            Assert.IsTrue(c.G.Knows("idTaken"));
            Assert.AreEqual(1, c.G.Events.Count(e => e.Type == "id_taken"));
            Assert.AreEqual("신분증은 이미 챙겼다.", ApartmentWorld.Use(c, "homeWallet", "지갑 · 신분증").Toast);
            Assert.AreEqual(1, c.G.Events.Count(e => e.Type == "id_taken"));
            var json = SaveCodec.Save(c.G);
            Assert.AreEqual(json, SaveCodec.Save(SaveCodec.Load(json, Db)), "the save stays valid");
        }

        [Test]
        public void A_point_without_a_description_says_nothing_yet()
        {
            Assert.IsTrue(ApartmentWorld.Use(Ctx(), "homeExitDoor", "현관문 (복도로)").IsEmpty);
        }
    }
}
