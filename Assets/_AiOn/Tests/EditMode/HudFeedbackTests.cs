using AiOn.Player;
using AiOn.Rendering;
using AiOn.Story;
using AiOn.UI;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class HudFeedbackTests
    {
        [Test]
        public void Danger_ComesFromTensionLowHealthOrSmoke()
        {
            Assert.That(DangerMath.Level(0f, 100f, 0f), Is.EqualTo(0f));
            Assert.That(DangerMath.Level(0.8f, 100f, 0f), Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(DangerMath.Level(0f, 0f, 0f), Is.EqualTo(1f));
            Assert.That(DangerMath.Level(0f, 60f, 0f), Is.EqualTo(0f), "health above 45 does not count");
            Assert.That(DangerMath.Level(0f, 100f, 1f), Is.EqualTo(0.9f).Within(1e-5f));
        }

        [Test]
        public void Heart_QuickensButStaysUnderThreePulsesASecond()
        {
            Assert.That(DangerMath.Bpm(DangerMath.HeartFrom), Is.EqualTo(64f).Within(1e-3f));
            Assert.That(DangerMath.Bpm(1f), Is.LessThanOrEqualTo(180f * 0.99f));
            Assert.That(DangerMath.Bpm(1f) / 60f, Is.LessThan(3f));
            Assert.That(DangerMath.HeartVolume(1f), Is.GreaterThan(DangerMath.HeartVolume(0.4f)));
        }

        [Test]
        public void Beat_IsLubThenASofterDub()
        {
            Assert.That(DangerMath.Beat(0.03f), Is.EqualTo(1f).Within(1e-4f));
            float between = DangerMath.Beat(DangerMath.DubAfter - 0.01f), dub = DangerMath.Beat(DangerMath.DubAfter + 0.03f);
            Assert.That(dub, Is.GreaterThan(between));
            Assert.That(dub, Is.EqualTo(0.6f).Within(0.05f));
            Assert.That(DangerMath.Beat(1f), Is.LessThan(0.01f));
        }

        [Test]
        public void Edge_PulsesOnlyWithoutReduceMotion_AndStaysCapped()
        {
            float rest = DangerMath.EdgeAlpha(1f, 1f, 0f, false), peak = DangerMath.EdgeAlpha(1f, 1f, 1f, false);
            Assert.That(peak, Is.GreaterThan(rest));
            Assert.That(peak, Is.LessThanOrEqualTo(0.4f));
            Assert.That(DangerMath.EdgeAlpha(1f, 1f, 0f, true), Is.EqualTo(DangerMath.EdgeAlpha(1f, 1f, 1f, true)));
        }

        [Test]
        public void Breath_WindedIsFull_RestedAndClearIsNone()
        {
            Assert.That(PlayerCameraRig.BreathAmount(1f, false, 0f), Is.EqualTo(0f));
            Assert.That(PlayerCameraRig.BreathAmount(0.2f, true, 0f), Is.EqualTo(1f));
            Assert.That(PlayerCameraRig.BreathAmount(0f, false, 0f), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(PlayerCameraRig.BreathAmount(1f, false, 1f), Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Motion_EasesAndTrail()
        {
            Assert.That(HudMotion.OutBack(0f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(HudMotion.OutBack(1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(HudMotion.OutBack(0.7f), Is.GreaterThan(1f), "it overshoots before it settles");
            Assert.That(HudMotion.OutCubic(0.5f), Is.EqualTo(0.875f).Within(1e-5f));
            // the trail holds for a moment after a loss, then drains, and never sits under the value
            Assert.That(HudMotion.Trail(0.8f, 0.5f, 0.1f, 0.1f), Is.EqualTo(0.8f));
            Assert.That(HudMotion.Trail(0.8f, 0.5f, 1f, 0.1f), Is.EqualTo(0.755f).Within(1e-4f));
            Assert.That(HudMotion.Trail(0.4f, 0.5f, 1f, 0.1f), Is.EqualTo(0.5f));
        }

        [Test]
        public void Marker_ShrinksWithDistanceWithinItsLimits()
        {
            float near = HudMotion.ProjectedSize(0.5f, 1f, 70f, 1080f, 44f, 150f), far = HudMotion.ProjectedSize(0.5f, 4f, 70f, 1080f, 44f, 150f);
            Assert.That(near, Is.EqualTo(150f));
            Assert.That(far, Is.InRange(44f, 150f));
            Assert.That(HudMotion.ProjectedSize(0.5f, 40f, 70f, 1080f, 44f, 150f), Is.EqualTo(44f));
        }

        [Test]
        public void Prompt_SplitsTheKeyFromTheLabel()
        {
            Assert.That(GameHud.SplitKey("G · 빈 병 줍기"), Is.EqualTo(("G", "빈 병 줍기")));
            Assert.That(GameHud.SplitKey("그냥 문장"), Is.EqualTo(("", "그냥 문장")));
            Assert.That(GameHud.SplitKey(null), Is.EqualTo(((string)null, (string)null)));
        }

        [Test]
        public void Outline_TakesSmallPropsAtThePointOnly()
        {
            var at = new Vector3(1, 1, 1);
            Assert.That(OutlineHighlight.IsPropAt(new Bounds(at, Vector3.one * 0.4f), at), Is.True);
            Assert.That(OutlineHighlight.IsPropAt(new Bounds(at, Vector3.one * 5f), at), Is.False, "a wall or floor is not the thing");
            Assert.That(OutlineHighlight.IsPropAt(new Bounds(at + Vector3.right * 3f, Vector3.one * 0.4f), at), Is.False);
        }

        [Test]
        public void Heartbeat_HasTwoThumps()
        {
            var s = ProceduralAudio.HeartbeatSamples();
            int rate = ProceduralAudio.Rate;
            float Peak(float from, float to) { float m = 0; for (int i = (int)(from * rate); i < (int)(to * rate) && i < s.Length; i++) m = Mathf.Max(m, Mathf.Abs(s[i])); return m; }
            Assert.That(Peak(0f, 0.08f), Is.GreaterThan(0.5f));
            Assert.That(Peak(DangerMath.DubAfter, DangerMath.DubAfter + 0.08f), Is.GreaterThan(0.3f));
            Assert.That(Peak(0.4f, 0.42f), Is.LessThan(0.05f), "it ends quiet, so beats do not click");
        }
    }
}
