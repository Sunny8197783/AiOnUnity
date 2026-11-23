using AiOn.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class PeopleFillTests
    {
        [Test]
        public void FillIsFullInTheDarkAndOffInDaylight()
        {
            Assert.AreEqual(1f, PeopleFill.Strength(0f), 1e-5f);
            Assert.AreEqual(1f, PeopleFill.Strength(PeopleFill.DarkBelow), 1e-5f);
            Assert.AreEqual(0f, PeopleFill.Strength(PeopleFill.BrightAbove), 1e-5f);
            Assert.AreEqual(0f, PeopleFill.Strength(2f), 1e-5f);
            float mid = PeopleFill.Strength((PeopleFill.DarkBelow + PeopleFill.BrightAbove) / 2f);
            Assert.AreEqual(0.5f, mid, 1e-4f);
            float prev = 2f;
            for (float x = 0; x <= 0.5f; x += 0.01f) { float v = PeopleFill.Strength(x); Assert.LessOrEqual(v, prev + 1e-6f); prev = v; }
        }

        [Test]
        public void SceneLightReadsTheAmbientAndTheKey()
        {
            var dim = new Color(0.02f, 0.02f, 0.03f);
            float dark = PeopleFill.SceneLight(dim, dim, 0f, Color.white);
            Assert.Less(dark, PeopleFill.DarkBelow);
            // a bright outdoor day: a strong sky fill and the sun get no fill (or next to none)
            var sky = new Color(0.75f, 0.8f, 0.9f);
            float day = PeopleFill.SceneLight(sky, sky * 0.8f, 1.6f, new Color(1f, 0.96f, 0.9f));
            Assert.Less(PeopleFill.Strength(day), 0.05f);
            // the shelter at night (measured: sky fill about 0.42, no key) gets some, less than the dark
            var shelter = new Color(0.39f, 0.427f, 0.44f);
            float sh = PeopleFill.Strength(PeopleFill.SceneLight(shelter, shelter * 0.64f, 0f, Color.white));
            Assert.Greater(sh, 0.4f); Assert.Less(sh, 0.9f);
            // the key light adds, the brighter of sky and equator counts
            Assert.Greater(PeopleFill.SceneLight(dim, dim, 1f, Color.white), dark);
            Assert.AreEqual(PeopleFill.SceneLight(dim, sky, 0f, Color.white), PeopleFill.SceneLight(sky, dim, 0f, Color.white), 1e-6f);
        }

        [Test]
        public void PeopleSitOnTheirOwnLayer()
        {
            Assert.AreEqual(1u << PeopleFill.LayerBit, PeopleFill.LayerMask);
            Assert.AreNotEqual(1u, PeopleFill.LayerMask, "not the default layer every level renderer is on");
            var go = new GameObject("p");
            try
            {
                var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                child.transform.SetParent(go.transform);
                PeopleFill.Mark(go);
                var r = child.GetComponent<Renderer>();
                Assert.AreEqual(PeopleFill.LayerMask, r.renderingLayerMask & PeopleFill.LayerMask);
                Assert.AreEqual(1u, r.renderingLayerMask & 1u, "keeps the default layer: the level's lights still light people");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
