using System.Linq;
using AiOn.Levels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AiOn.Tests
{
    public class LevelLayoutTests
    {
        static LevelLayout Apartment
        {
            get
            {
                var l = AssetDatabase.LoadAssetAtPath<LevelLayout>("Assets/_AiOn/Data/Levels/ApartmentLayout.asset");
                Assert.That(l, Is.Not.Null, "run AiOn/Import Level Layouts");
                return l;
            }
        }

        [Test]
        public void Apartment_layout_has_the_original_counts()
        {
            var l = Apartment;
            Assert.AreEqual("apartment", l.levelId);
            Assert.AreEqual(15, l.walk.Count);
            Assert.AreEqual(107, l.solids.Count);
            Assert.AreEqual(9, l.ceilings.Count);
            CollectionAssert.AreEquivalent(new[] { "home", "homeEntry", "lobby", "corridor3F", "stairs3F", "stairs1F" }, l.spawns.Select(s => s.name));
            Assert.AreEqual(4, l.walk.Count(w => w.isRamp), "two flights between each pair of floors, two floors of stairs");
        }

        [Test]
        public void Every_spawn_stands_on_a_walk_zone_at_its_floor_height()
        {
            foreach (var s in Apartment.spawns)
            {
                var zone = Apartment.walk.FirstOrDefault(w => !w.isRamp && s.x >= w.x0 && s.x <= w.x1 && s.z >= w.z0 && s.z <= w.z1 && Mathf.Abs(w.y - s.y) < 0.01f);
                Assert.That(zone, Is.Not.Null, $"spawn {s.name} at ({s.x}, {s.y}, {s.z})");
            }
        }

        [Test]
        public void Frame_conversion_keeps_east_and_turns_north_into_plus_z()
        {
            Assert.AreEqual(new Vector3(2, 1, -3), LevelLayout.ToUnity(2, 1, 3), "three.js z points south");
            Assert.AreEqual(0f, LevelLayout.ToUnityYaw(0f), 1e-4, "yaw 0 faces north in both");
            Assert.AreEqual(90f, LevelLayout.ToUnityYaw(-Mathf.PI / 2), 1e-3, "a right turn to the east");
            Assert.AreEqual(-90f, LevelLayout.ToUnityYaw(Mathf.PI / 2), 1e-3, "a left turn to the west");
            var (c, size) = LevelLayout.BoxToUnity(0, 2, 0, 1, 4, 6);
            Assert.AreEqual(new Vector3(1, 0.5f, -5), c);
            Assert.AreEqual(new Vector3(2, 1, 2), size);
        }
    }
}
