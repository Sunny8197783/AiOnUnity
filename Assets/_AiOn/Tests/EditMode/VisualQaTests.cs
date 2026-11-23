using System.IO;
using AiOn.Levels;
using AiOn.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>Final visual QA pass: the fixes' pure parts (hill haze, emergency lamp stand-off, view-tree lobes, persistent services).</summary>
    public class VisualQaTests
    {
        [Test]
        public void Hills_HazeFollowsTheFog_ButTheRidgeStays()
        {
            // no fog: the original's fixed haze
            Assert.That(SkyRig.HillHaze(0.3f, SkyRig.HillsNearCap, SkyRig.HillsNearR, 0f), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(SkyRig.HillHaze(0.62f, SkyRig.HillsFarCap, SkyRig.HillsFarR, 0f), Is.EqualTo(0.62f).Within(1e-5f));
            // thin fog: more haze
            float thin = SkyRig.HillHaze(0.3f, SkyRig.HillsNearCap, SkyRig.HillsNearR, 0.0012f);
            Assert.That(thin, Is.GreaterThan(0.3f).And.LessThanOrEqualTo(SkyRig.HillsNearCap));
            // the game's day / afternoon fog (0.003 x 1.3): capped, so the ridge still shows, the far ring paler than the near
            float near = SkyRig.HillHaze(0.3f, SkyRig.HillsNearCap, SkyRig.HillsNearR, 0.0039f);
            float far = SkyRig.HillHaze(0.62f, SkyRig.HillsFarCap, SkyRig.HillsFarR, 0.0039f);
            Assert.That(near, Is.EqualTo(SkyRig.HillsNearCap).Within(1e-5f), "capped");
            Assert.That(far, Is.GreaterThan(near));
            Assert.That(SkyRig.HillHaze(0.3f, 0.9f, SkyRig.HillsFarR, 0.001f), Is.GreaterThan(SkyRig.HillHaze(0.3f, 0.9f, SkyRig.HillsNearR, 0.001f)), "farther is hazier");
        }

        [Test]
        public void Sky_HorizonHazeFollowsTheFogAtTheSkyline()
        {
            Assert.That(SkyRig.SkyHorizonHaze(0f), Is.EqualTo(0f), "no fog: the original's sky");
            Assert.That(SkyRig.SkyHorizonHaze(0f, 0.3f), Is.EqualTo(0.3f).Within(1e-5f), "the inspector floor");
            float day = SkyRig.SkyHorizonHaze(0.0039f);
            Assert.That(day, Is.GreaterThan(0.9f).And.LessThanOrEqualTo(0.95f), "the fogged skyline meets a fog-coloured horizon");
            Assert.That(SkyRig.SkyHorizonHaze(0.001f), Is.LessThan(day));
            Assert.That(SkyRig.SkyHorizonHaze(1f), Is.EqualTo(0.95f).Within(1e-5f), "never a flat fog-coloured sky");
        }

        [Test]
        public void EmergencyLamp_StandsOffTheWallTowardTheDoorPoint()
        {
            var sign = new Vector3(2f, 2.4f, 0f);
            var off = GuidingLights.LampOffset(sign, new Vector3(2f, 0f, 1.2f));
            Assert.That(off.y, Is.EqualTo(0f));
            Assert.That(off.magnitude, Is.EqualTo(GuidingLights.LampStandOff).Within(1e-5f));
            Assert.That(off.z, Is.GreaterThan(0f), "toward the walk-up point");
            Assert.That(GuidingLights.LampOffset(sign, new Vector3(2.2f, 0f, 0f)).magnitude, Is.EqualTo(0.2f).Within(1e-5f), "never past the point");
            Assert.That(GuidingLights.LampOffset(sign, new Vector3(2f, 0f, 0.01f)), Is.EqualTo(Vector3.zero), "right under the sign");
        }

        [Test]
        public void ViewTrees_SplitsATriangleSoupIntoLobes()
        {
            // two separate tetrahedra exported as a soup (each corner repeated per face)
            Vector3[] Tet(Vector3 o) => new[] { o, o + Vector3.right, o + Vector3.up, o + Vector3.forward };
            int[] faces = { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            foreach (var o in new[] { Vector3.zero, new Vector3(5, 0, 0) })
            {
                var c = Tet(o);
                foreach (int f in faces) { tris.Add(verts.Count); verts.Add(c[f]); }
            }
            var lobes = ViewTrees.Lobes(verts.ToArray(), tris.ToArray(), 0.01f);
            Assert.That(lobes.Count, Is.EqualTo(2));
            Assert.That(lobes[0].Tris.Count, Is.EqualTo(12));
            Assert.That(lobes[1].Verts.Count, Is.EqualTo(12));
        }

        [Test]
        public void ViewTrees_LeafTextureIsACutOutClump()
        {
            var t = ViewTrees.LeafTexture();
            Assert.That(t.width, Is.EqualTo(128));
            Assert.That(ViewTrees.LeafTexture(), Is.SameAs(t), "one shared texture");
            Assert.That(Resources.Load<Material>("Surfaces/ViewLeaves"), Is.Not.Null, "the cut-out leaf material ships in Resources");
        }

        [Test]
        public void ViewTrees_InstanceTeardownKeepsTheSharedLeafTexture()
        {
            // apartment -> apartment reloads build the new instance before the old one's OnDestroy runs
            var src = File.ReadAllText(Path.Combine(Application.dataPath, "_AiOn/Scripts/Rendering/ViewTrees.cs"));
            int at = src.IndexOf("void OnDestroy()");
            Assert.That(at, Is.GreaterThan(0));
            string body = src.Substring(at, src.IndexOf('}', at) - at);
            Assert.That(body, Does.Not.Contain("_leaf"));
        }

        [Test]
        public void PersistentServices_ComeBackAfterTheTitleReturn()
        {
            // TitleReturn destroys every persistent root; the boot-once services must make themselves again on the next load
            foreach (var file in new[] { "Rendering/PeopleFill.cs", "Story/TalkFacing.cs" })
            {
                var src = File.ReadAllText(Path.Combine(Application.dataPath, "_AiOn/Scripts", file));
                Assert.That(src, Does.Contain("SceneManager.sceneLoaded +="), file);
                Assert.That(src, Does.Contain("SceneManager.sceneLoaded -="), file + " (no double subscription)");
            }
        }
    }
}
