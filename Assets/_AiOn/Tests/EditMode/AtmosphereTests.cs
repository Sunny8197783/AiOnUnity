using AiOn.Rendering;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class AtmosphereTests
    {
        [Test]
        public void BaseLook_WarmPeacetimeKeepsMoreWarmth()
        {
            Assert.That(AtmosphereMath.BaseWeightFor("interior"), Is.LessThan(AtmosphereMath.BaseWeightFor("night")));
            Assert.That(AtmosphereMath.BaseWeightFor("blackout"), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.BaseWeightFor("underground"), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.SaturationBias(1f), Is.EqualTo(-10f));
            Assert.That(AtmosphereMath.SaturationBias(0f), Is.EqualTo(0f));
        }

        [Test]
        public void Blast_PeakFallsWithDistance()
        {
            Assert.That(AtmosphereMath.BlastPeak(10f), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.BlastPeak(100f), Is.InRange(0.25f, 1f));
            Assert.That(AtmosphereMath.BlastPeak(100f), Is.LessThan(AtmosphereMath.BlastPeak(40f)));
            Assert.That(AtmosphereMath.BlastPeak(300f), Is.EqualTo(0f));
        }

        [Test]
        public void Pulse_RisesThenHalves()
        {
            Assert.That(AtmosphereMath.Pulse(-1f, 0.1f, 1f), Is.EqualTo(0f));
            Assert.That(AtmosphereMath.Pulse(0.05f, 0.1f, 1f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(AtmosphereMath.Pulse(1.1f, 0.1f, 1f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Siren_SwellsUnlessReducedMotion()
        {
            float lo = AtmosphereMath.SirenLevel(0f, 4f, false), hi = AtmosphereMath.SirenLevel(2f, 4f, false);
            Assert.That(lo, Is.EqualTo(0.22f).Within(1e-5f));
            Assert.That(hi, Is.EqualTo(0.42f).Within(1e-5f));
            Assert.That(AtmosphereMath.SirenLevel(1f, 4f, true), Is.EqualTo(AtmosphereMath.SirenLevel(3f, 4f, true)));
        }

        [Test]
        public void Fog_ThickerOutdoorsAtNight_HazyUnderground()
        {
            float day = AtmosphereMath.FogDensity(0.003f, true, 0f, "day");
            float night = AtmosphereMath.FogDensity(0.003f, true, 1f, "night");
            Assert.That(night, Is.GreaterThan(day * 2.5f));
            Assert.That(AtmosphereMath.FogDensity(0.006f, false, 0f, "interior"), Is.EqualTo(0.003f).Within(1e-6f));
            Assert.That(AtmosphereMath.FogDensity(0.02f, false, 0f, "underground"), Is.EqualTo(0.015f).Within(1e-6f));
        }

        [Test]
        public void Guides_BrighterInTheDark_AndCoverEveryLevel()
        {
            Assert.That(GuidingLights.Visibility(0f, true, true, "day"), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(GuidingLights.Visibility(0f, false, false, "interior"), Is.GreaterThan(0.85f));
            Assert.That(GuidingLights.Visibility(1f, true, true, "night"), Is.EqualTo(1f));
            foreach (var exit in new[] { "lobbyExit", "martExit", "storeExit", "officeExit", "shelterExit" })
                Assert.That(GuidingLights.Table[exit], Is.EqualTo(GuidingLights.Kind.Exit), exit);
            Assert.That(GuidingLights.Table["shelterA"], Is.EqualTo(GuidingLights.Kind.Shelter));
            Assert.That(GuidingLights.NearFade(1f), Is.EqualTo(0f));
            Assert.That(GuidingLights.NearFade(10f), Is.EqualTo(1f));
        }

        [Test]
        public void Reflections_RefreshOnLookChangeOrAfterAWalk()
        {
            var a = new UnityEngine.Vector3(0, 1.6f, 0);
            Assert.That(LevelReflections.NeedsRefresh(new UnityEngine.Vector3(float.NaN, 0, 0), a, false), Is.True, "first render");
            Assert.That(LevelReflections.NeedsRefresh(a, a + UnityEngine.Vector3.forward * 2f, false), Is.False);
            Assert.That(LevelReflections.NeedsRefresh(a, a + UnityEngine.Vector3.forward * 7f, false), Is.True);
            Assert.That(LevelReflections.NeedsRefresh(a, a, true), Is.True);
        }
    }
}
