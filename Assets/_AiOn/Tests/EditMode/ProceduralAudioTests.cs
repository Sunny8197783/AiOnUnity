using AiOn.Story;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class ProceduralAudioTests
    {
        static void AssertSound(float[] s, float minSeconds)
        {
            Assert.That(s.Length, Is.GreaterThanOrEqualTo((int)(minSeconds * ProceduralAudio.Rate)));
            float peak = 0;
            foreach (var v in s) { Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v)); peak = Mathf.Max(peak, Mathf.Abs(v)); }
            Assert.That(peak, Is.GreaterThan(0.005f), "not silent");
        }

        [Test]
        public void SirenRisesForFiveSecondsAndFallsForThree()
        {
            Assert.That(ProceduralAudio.SirenFreq(0, 300, 640), Is.EqualTo(300).Within(0.5f));
            Assert.That(ProceduralAudio.SirenFreq(4.999f, 300, 640), Is.EqualTo(640).Within(1f));
            Assert.That(ProceduralAudio.SirenFreq(7.999f, 300, 640), Is.EqualTo(300).Within(1f));
            Assert.That(ProceduralAudio.SirenFreq(2.5f, 300, 640), Is.EqualTo(300 * Mathf.Sqrt(640f / 300f)).Within(1f)); // exponential ramp
            Assert.That(ProceduralAudio.SirenFreq(59.999f, 300, 640), Is.EqualTo(640).Within(1f));
            Assert.That(ProceduralAudio.SirenFreq(63.5f, 300, 640), Is.EqualTo(165).Within(0.5f));
        }

        [Test]
        public void SirenLastsAMinute() => AssertSound(ProceduralAudio.SirenSamples(), 63f);

        [Test]
        public void PhoneAlertsSound()
        {
            AssertSound(ProceduralAudio.PhoneSamples("emergency"), 3.7f);
            AssertSound(ProceduralAudio.PhoneSamples("safety"), 0.9f);
            AssertSound(ProceduralAudio.PhoneSamples("sms"), 0.3f);
        }

        [Test]
        public void BoomIsLouderNear()
        {
            var near = ProceduralAudio.BoomSamples(0.3f);
            var far = ProceduralAudio.BoomSamples(1f);
            AssertSound(near, 3.8f); AssertSound(far, 3.8f);
            float Rms(float[] s) { double a = 0; foreach (var v in s) a += v * v; return (float)System.Math.Sqrt(a / s.Length); }
            Assert.That(Rms(near), Is.GreaterThan(Rms(far)));
        }

        [TestCase("whistle")] [TestCase("punch")] [TestCase("thud")] [TestCase("land")]
        [TestCase("glass")] [TestCase("swing")] [TestCase("pick")] [TestCase("collapse")]
        public void HitsSound(string name) => AssertSound(ProceduralAudio.HitSamples(name, 2.2f), 0.05f);

        [Test]
        public void FireLoopIsSeamless()
        {
            var s = ProceduralAudio.FireSamples();
            AssertSound(s, 5.9f);
            float peak = 0; foreach (var v in s) peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.That(Mathf.Abs(s[0] - s[s.Length - 1]), Is.LessThan(peak * 0.5f), "no click at the loop point");
        }

        [Test]
        public void ClipsAreCached()
        {
            Assert.AreSame(ProceduralAudio.PhoneAlert("emergency"), ProceduralAudio.PhoneAlert("emergency"));
            Assert.AreSame(ProceduralAudio.Boom(0.52), ProceduralAudio.Boom(0.5)); // quantized to 0.1
        }
    }
}
