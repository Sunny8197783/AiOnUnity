using System.Linq;
using AiOn.UI;
using AiOn.UI.Studio;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>The minigame art (UI/Studio): the arm reach, the swing-twist split, the kneeling layout, the tiling
    /// noise and surface maps, and the bag screen's item pictures.</summary>
    public class MinigameArtTests
    {
        [Test]
        public void ElbowKeepsBothBoneLengthsAndBendsTowardThePole()
        {
            var shoulder = new Vector3(0, 1, 0);
            var target = new Vector3(0, 0.55f, 0.3f);
            var pole = new Vector3(1, 1, 0);
            var elbow = StudioMath.Elbow(shoulder, target, 0.3f, 0.28f, pole, out float reach);
            Assert.AreEqual(0.3f, Vector3.Distance(shoulder, elbow), 1e-4f, "upper arm length");
            Assert.AreEqual(Vector3.Distance(shoulder, target), reach, 1e-4f, "a target in reach is met");
            Assert.AreEqual(0.28f, Vector3.Distance(elbow, shoulder + (target - shoulder).normalized * reach), 1e-3f, "forearm length to the wrist");
            Assert.Greater(elbow.x, 0f, "the elbow goes out toward the pole");
        }

        [Test]
        public void ElbowAtFullStretchForAFarTarget()
        {
            var elbow = StudioMath.Elbow(Vector3.zero, new Vector3(0, -2, 0), 0.3f, 0.28f, Vector3.right, out float reach);
            Assert.AreEqual(0.58f, reach, 1e-3f);
            Assert.AreEqual(0f, elbow.x, 1e-2f, "straight along the line");
            Assert.AreEqual(-0.3f, elbow.y, 1e-2f);
        }

        [Test]
        public void TwistIsThePartAboutTheAxis()
        {
            var axis = Vector3.forward;
            var turn = Quaternion.AngleAxis(50f, axis) * Quaternion.AngleAxis(30f, Vector3.right);
            var tw = StudioMath.Twist(Quaternion.AngleAxis(50f, axis), axis);
            Assert.AreEqual(50f, Quaternion.Angle(Quaternion.identity, tw), 0.01f);
            var only = StudioMath.Twist(Quaternion.AngleAxis(30f, Vector3.right), axis);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, only), 0.01f, "a swing has no twist");
            var mixed = StudioMath.Twist(turn, axis);
            var swing = turn * Quaternion.Inverse(mixed);
            // what is left after the twist moves the axis but does not turn about it
            var t2 = StudioMath.Twist(swing, axis);
            Assert.Less(Quaternion.Angle(Quaternion.identity, t2), 0.5f);
        }

        [Test]
        public void KneelingPutsTheShouldersOverTheHandsAndTheKneesOnTheFloor()
        {
            // a Rocketbox adult: trunk 0.49, arm 0.55 to the heel of the hand, shoulders 0.4 apart, thigh 0.38
            float trunk = 0.49f, arm = 0.55f, half = 0.2f, thigh = 0.38f, chest = 0.27f, gap = 0.21f, knee = 0.055f;
            var k = StudioMath.Kneel(trunk, arm, half, thigh, chest, gap, knee);
            float r = k.Lean * Mathf.Deg2Rad;
            Assert.AreEqual(trunk * Mathf.Sin(r), k.Back, 1e-4f, "the shoulders come forward by as much as the hips sit back");
            Assert.AreEqual(k.ShoulderHeight, k.HipHeight + trunk * Mathf.Cos(r), 1e-4f, "the shoulders straight over the hands");
            // each arm reaches in to the middle from its own shoulder: it must still reach, almost straight
            float reach = Mathf.Sqrt((k.ShoulderHeight - chest) * (k.ShoulderHeight - chest) + half * half);
            Assert.Less(reach, arm, "never past full stretch (both hands reach the one spot)");
            Assert.Greater(reach, arm * 0.95f, "the arms all but locked");
            // the legs hold the hips there with the knee joint at kneeling height
            float legs = knee + Mathf.Sqrt(thigh * thigh - k.KneeForward * k.KneeForward);
            Assert.AreEqual(k.HipHeight, legs, 2e-3f, "the thighs reach from the hips down to the knees on the floor");
            Assert.LessOrEqual(k.Back - k.KneeForward, gap + 1e-4f, "the knees come no closer than his side");
            Assert.That(k.Lean, Is.InRange(20f, 60f), "a lean from the hips, not lying over him");
            Assert.Greater(k.HipHeight, 0.3f, "kneeling up, not sitting on the heels");
        }

        [Test]
        public void SurfaceMapsTileAcrossBothEdges()
        {
            foreach (StudioTextures.Surface s in System.Enum.GetValues(typeof(StudioTextures.Surface)))
                for (int i = 0; i <= 40; i++)
                {
                    float t = i / 40f;
                    // just inside each edge against just inside the opposite one: the same as two neighbouring texels
                    const float e = 0.5f / 256f;
                    float hu0 = StudioTextures.HeightOf(s, e, t), hu1 = StudioTextures.HeightOf(s, 1f + e, t);
                    float hv0 = StudioTextures.HeightOf(s, t, e), hv1 = StudioTextures.HeightOf(s, t, 1f + e);
                    Assert.AreEqual(hu0, hu1, 1e-3f, s + " height repeats in u at v=" + t);
                    Assert.AreEqual(hv0, hv1, 1e-3f, s + " height repeats in v at u=" + t);
                    var cu0 = StudioTextures.ColourOf(s, e, t); var cu1 = StudioTextures.ColourOf(s, 1f + e, t);
                    var cv0 = StudioTextures.ColourOf(s, t, e); var cv1 = StudioTextures.ColourOf(s, t, 1f + e);
                    Assert.Less(Mathf.Abs(cu0.r - cu1.r) + Mathf.Abs(cu0.g - cu1.g) + Mathf.Abs(cu0.b - cu1.b), 3e-3f, s + " colour repeats in u at v=" + t);
                    Assert.Less(Mathf.Abs(cv0.r - cv1.r) + Mathf.Abs(cv0.g - cv1.g) + Mathf.Abs(cv0.b - cv1.b), 3e-3f, s + " colour repeats in v at u=" + t);
                }
        }

        [Test]
        public void RoundedBoxStaysInItsSizeAndFacesOut()
        {
            var size = new Vector3(0.3f, 0.14f, 0.42f);
            var m = StudioMath.RoundedBox(size, 0.05f, 6);
            try
            {
                var b = m.bounds;
                Assert.AreEqual(size.x, b.size.x, 1e-4f); Assert.AreEqual(size.y, b.size.y, 1e-4f); Assert.AreEqual(size.z, b.size.z, 1e-4f);
                var v = m.vertices; var n = m.normals; var t = m.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (face.sqrMagnitude < 1e-14f) continue; // a corner triangle squeezed to nothing
                    var centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Assert.Greater(Vector3.Dot(face, centre), 0f, "triangle " + i / 3 + " winds outward (Unity's front face)");
                }
                for (int i = 0; i < v.Length; i++) Assert.Greater(Vector3.Dot(n[i], v[i]), 0f, "normal " + i + " points out");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheTowelShutsOutTheGapsLight()
        {
            Assert.AreEqual(1f, LookStudio.Leak(false, "none"));
            Assert.Less(LookStudio.Leak(true, "dry"), 0.5f, "a dry towel lets a little through");
            Assert.Less(LookStudio.Leak(true, "tub"), LookStudio.Leak(true, "dry"), "a wet one less");
            Assert.Less(LookStudio.Leak(true, "bottle"), 0.1f);
        }

        [Test]
        public void NoiseTilesPerAxis()
        {
            for (int i = 0; i < 30; i++)
            {
                float x = i * 0.53f, y = i * 0.29f;
                float a = StudioMath.Noise(x, y, 220, 6, 52);
                Assert.AreEqual(a, StudioMath.Noise(x + 220, y, 220, 6, 52), 1e-5f);
                Assert.AreEqual(a, StudioMath.Noise(x, y + 6, 220, 6, 52), 1e-5f);
                float f = StudioMath.Fbm(x, y, 3, 1, 4, 51);
                Assert.AreEqual(f, StudioMath.Fbm(x + 3, y, 3, 1, 4, 51), 1e-5f);
                Assert.AreEqual(f, StudioMath.Fbm(x, y + 1, 3, 1, 4, 51), 1e-5f);
            }
        }

        [Test]
        public void NoiseTilesAndStaysInRange()
        {
            for (int i = 0; i < 50; i++)
            {
                float x = i * 0.37f, y = i * 0.61f;
                float a = StudioMath.Noise(x, y, 8, 3);
                Assert.That(a, Is.InRange(0f, 1f));
                Assert.AreEqual(a, StudioMath.Noise(x + 8, y, 8, 3), 1e-5f, "tiles in x");
                Assert.AreEqual(a, StudioMath.Noise(x, y + 8, 8, 3), 1e-5f, "tiles in y");
                Assert.That(StudioMath.Fbm(x, y, 4, 4, 1), Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void EveryBagItemHasAPictureThatExists()
        {
            var json = JObject.Parse(System.IO.File.ReadAllText("Assets/_AiOn/Data/Source/items.json"));
            var ids = json["catalog"].Select(t => (string)t["id"]).Where(id => (int?)t_cells(json, id) != 0).ToList();
            Assert.Greater(ids.Count, 20);
            foreach (var id in ids.Concat(AiOn.Game.BagAids.All.Select(a => a.Id)))
            {
                var path = BagIcons.PathOf(id);
                Assert.IsNotNull(path, id + " has no picture");
                Assert.IsNotNull(Resources.Load<Texture2D>(path), id + ": " + path + " is missing");
            }
        }

        /// <summary>The grid cells an item takes (0 for fixtures that never go in the bag).</summary>
        static int t_cells(JObject json, string id)
        {
            var it = json["catalog"].First(t => (string)t["id"] == id);
            var cells = it["cells"] as JArray;
            return cells == null ? 1 : (int)cells[0] * (int)cells[1];
        }
    }
}
