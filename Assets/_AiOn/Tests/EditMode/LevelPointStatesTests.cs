using System.Collections.Generic;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class LevelPointStatesTests
    {
        static LevelPointStates Make()
        {
            var go = new GameObject("lps");
            var c = go.AddComponent<LevelPointStates>();
            c.points.Add(new LevelPointStates.PointRules
            {
                id = "elderDoor", defaultEnabled = true, defaultLabel = "304호",
                rules =
                {
                    new LevelPointStates.Rule { key = "hallFire", value = "\"small\"", enabled = 0 },
                    new LevelPointStates.Rule { key = "hallFire", value = "\"large\"", enabled = 0 },
                    new LevelPointStates.Rule { key = "elder304", value = "\"on\"", enabled = 0 },
                },
            });
            c.points.Add(new LevelPointStates.PointRules
            {
                id = "closure_danji", defaultEnabled = false, defaultLabel = "안내 요원 · 단지길 통제",
                rules = { new LevelPointStates.Rule { key = "closures", value = "[\"danji\"]", enabled = 1 } },
            });
            c.points.Add(new LevelPointStates.PointRules
            {
                id = "homeBag", defaultEnabled = true, defaultLabel = "비상가방 (비어 있음)",
                rules = { new LevelPointStates.Rule { key = "bag", value = "\"packed\"", hasLabel = true, label = "비상가방 (꾸림)" } },
            });
            return c;
        }

        static (bool, string) Of(List<(string Id, bool Enabled, string Label)> r, string id)
        {
            foreach (var x in r) if (x.Id == id) return (x.Enabled, x.Label);
            Assert.Fail("no " + id); return default;
        }

        [Test]
        public void DisablingKeysCombineAndResetToDefault()
        {
            var c = Make();
            Assert.AreEqual((false, "304호"), Of(c.Apply("hallFire", "small"), "elderDoor"));
            Assert.AreEqual((false, "304호"), Of(c.Apply("elder304", "off"), "elderDoor")); // the fire still disables it
            Assert.AreEqual((true, "304호"), Of(c.Apply("hallFire", "out"), "elderDoor"));
            Assert.AreEqual((false, "304호"), Of(c.Apply("elder304", "on"), "elderDoor"));
            Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void ListStatesMatchElementWise()
        {
            var c = Make();
            Assert.AreEqual((true, "안내 요원 · 단지길 통제"), Of(c.Apply("closures", new JArray("sijang", "danji")), "closure_danji"));
            Assert.AreEqual((false, "안내 요원 · 단지길 통제"), Of(c.Apply("closures", new JArray()), "closure_danji"));
            Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void LabelsFollowTheirState()
        {
            var c = Make();
            Assert.AreEqual((true, "비상가방 (꾸림)"), Of(c.Apply("bag", "packed"), "homeBag"));
            Assert.AreEqual((true, "비상가방 (비어 있음)"), Of(c.Apply("bag", "empty"), "homeBag"));
            Assert.IsTrue(c.Handles("closures")); Assert.IsFalse(c.Handles("power"));
            Object.DestroyImmediate(c.gameObject);
        }
    }
}
