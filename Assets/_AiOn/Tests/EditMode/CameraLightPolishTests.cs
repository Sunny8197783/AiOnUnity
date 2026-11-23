// The pure parts of the camera, lighting and player-feel polish: the talk shot's framing, the indoor shoulder camera,
// timed env blends and the preset tuning, ground light pools, the flashlight's near dimming and the footsteps.
using System.Collections.Generic;
using AiOn.Levels;
using AiOn.Player;
using AiOn.Story;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class CameraLightPolishTests
    {
        // ---------- talk shot

        [Test]
        public void TalkFraming_TriesRightShoulderThenLeftThenShorter()
        {
            var o = TalkFraming.Order;
            Assert.That(o[0], Is.EqualTo((1f, 1f)));
            Assert.That(o[1], Is.EqualTo((-1f, 1f)));
            for (int i = 2; i < o.Length; i++) Assert.That(o[i].scale, Is.LessThan(1f), "after both full shoulders only shorter booms");
            for (int i = 1; i < o.Length; i++) Assert.That(o[i].scale, Is.LessThanOrEqualTo(o[i - 1].scale));
        }

        [Test]
        public void TalkFraming_CandidateSitsBehindAndBesideTheHead()
        {
            var head = new Vector3(0, 1.6f, 0);
            var c = TalkFraming.Candidate(head, Vector3.forward, 1f, 1f);
            Assert.That(c.z, Is.EqualTo(-1.6f).Within(1e-4f), "1.6 m back");
            Assert.That(c.x, Is.EqualTo(0.8f).Within(1e-4f), "0.8 m to the right");
            Assert.That(c.y, Is.EqualTo(1.75f).Within(1e-4f), "0.15 m up");
            var l = TalkFraming.Candidate(head, Vector3.forward, -1f, 1f);
            Assert.That(l.x, Is.EqualTo(-0.8f).Within(1e-4f), "the other shoulder");
            var s = TalkFraming.Candidate(head, Vector3.forward, 1f, 0.4f);
            Assert.That(-s.z, Is.LessThan(-c.z), "a shorter boom is closer");
            // a direction with height is flattened
            var t = TalkFraming.Candidate(head, new Vector3(0, 5, 1), 1f, 1f);
            Assert.That(t.y, Is.EqualTo(1.75f).Within(1e-4f));
        }

        [Test]
        public void TalkFraming_AimPutsTheEyesThirtyPercentFromTheTop()
        {
            var cam = new Vector3(0.8f, 1.75f, -1.6f);
            var eyes = new Vector3(0, 1.62f, 1.5f);
            var target = TalkFraming.AimAt(cam, eyes, TalkFraming.Fov, TalkFraming.EyesFromTop);
            Assert.That(TalkFraming.ScreenFromTop(cam, target, eyes, TalkFraming.Fov), Is.EqualTo(0.30f).Within(0.01f));
            Assert.That(TalkFraming.ScreenFromTop(cam, target, eyes, TalkFraming.Fov), Is.LessThan(0.45f), "in the upper 45 %");
            var thing = TalkFraming.AimAt(cam, eyes, 50f, 0.38f);
            Assert.That(TalkFraming.ScreenFromTop(cam, thing, eyes, 50f), Is.EqualTo(0.38f).Within(0.01f));
        }

        [Test]
        public void TalkFraming_BodyCoverGrowsAsTheCameraClosesIn()
        {
            var feet = Vector3.zero;
            var look = Quaternion.LookRotation(Vector3.forward);
            float far = TalkFraming.BodyCover(new Vector3(0.8f, 1.75f, -1.6f), look, 36f, 16f / 9f, feet, 0);
            float near = TalkFraming.BodyCover(new Vector3(0.3f, 1.6f, -0.6f), look, 36f, 16f / 9f, feet, 0);
            Assert.That(near, Is.GreaterThan(far));
            Assert.That(near, Is.GreaterThan(TalkFraming.MaxBodyCover));
            // the body behind the camera: nothing covered
            Assert.That(TalkFraming.BodyCover(new Vector3(0, 1.6f, 3f), look, 36f, 16f / 9f, feet, 0), Is.EqualTo(0f));
        }

        [Test]
        public void TalkFraming_ThingsAreFramedWhereTheyAreSeen()
        {
            var board = TalkFacing.SubjectOf(new Vector3(1, 0, 2), "notice", 0f);
            Assert.That(board.y, Is.EqualTo(1.45f).Within(1e-4f), "a notice board at eye level, not its floor point");
            Assert.That(TalkFacing.SubjectOf(new Vector3(1, 5.75f, 2), "fire", 5.75f).y, Is.EqualTo(6.45f).Within(1e-4f), "on the 3rd floor");
            Assert.That(TalkFacing.SubjectOf(new Vector3(1, 2f, 2), "notice", 0f).y, Is.EqualTo(2f), "already up high");
        }

        [Test]
        public void Env_DaylightVariantIsNotNight()
        {
            var e = P("interiorDark", 0.5f, Color.blue, 1f, outdoor: false);
            EnvironmentController.DaylightVariant(e);
            Assert.That(e.NightAmount, Is.EqualTo(0f));
            Assert.That(e.KeyIntensity, Is.GreaterThan(1.5f), "a morning sun through the windows (graphics realism pass; was a 0.6 window key)");
            Color.RGBToHSV(e.KeyColor, out _, out float s, out _);
            Assert.That(s, Is.LessThan(0.25f), "a warm-neutral morning sun, not blue moonlight");
            Assert.That(e.KeyColor.r, Is.GreaterThanOrEqualTo(e.KeyColor.b), "not blue");
            Assert.That(e.FillIntensity, Is.GreaterThanOrEqualTo(0.3f), "window daylight fills the room: the morning's objectives read without a flashlight");
        }

        // ---------- shoulder camera

        [Test]
        public void Rig_FramingBlendsToTheIndoorNumbers()
        {
            var o = PlayerCameraRig.Framing(0, 3.2f, 1.95f, 0.7f, 1.8f, 1.7f, 0.45f);
            Assert.That(o, Is.EqualTo((3.2f, 1.95f, 0.7f)));
            var i = PlayerCameraRig.Framing(1, 3.2f, 1.95f, 0.7f, 1.8f, 1.7f, 0.45f);
            Assert.That(i, Is.EqualTo((1.8f, 1.7f, 0.45f)));
        }

        [Test]
        public void Rig_PivotSinksToTheEyesAsTheBoomShortens()
        {
            Assert.That(PlayerCameraRig.PivotHeight(1.95f, 1.62f, 3.2f, 3.2f, 0.9f), Is.EqualTo(1.95f).Within(1e-4f));
            Assert.That(PlayerCameraRig.PivotHeight(1.95f, 1.62f, 0.9f, 3.2f, 0.9f), Is.EqualTo(1.62f).Within(1e-4f));
            float mid = PlayerCameraRig.PivotHeight(1.95f, 1.62f, 2f, 3.2f, 0.9f);
            Assert.That(mid, Is.InRange(1.62f, 1.95f));
            Assert.That(PlayerCameraRig.PivotHeight(1.95f, 1.62f, 0.3f, 3.2f, 0.9f), Is.LessThanOrEqualTo(1.62f), "never above the eyes in a tight spot");
        }

        [Test]
        public void Rig_FirstPersonOnlyWhenThereIsNoRoom()
        {
            Assert.That(PlayerCameraRig.FirstPersonShare(2f, 0.9f), Is.EqualTo(0f));
            Assert.That(PlayerCameraRig.FirstPersonShare(0.5f, 0.9f), Is.EqualTo(1f));
            Assert.That(PlayerCameraRig.FirstPersonShare(0.9f, 0.9f), Is.InRange(0.01f, 0.99f));
        }

        // ---------- env blend and tuning

        static EnvPreset P(string name, float key, Color fill, float night, bool outdoor = true)
        {
            var e = new EnvPreset { Name = name, Outdoor = outdoor, KeyIntensity = key, SkyFill = fill, NightAmount = night, Exposure = 1, Sun = new Vector2(350, 10) };
            e.Lit["street"] = night;
            return e;
        }

        [Test]
        public void Env_LerpIsTheEndsAtZeroAndOneAndMixesBetween()
        {
            var a = P("day", 4f, Color.white, 0); var b = P("night", 0.2f, Color.blue, 1);
            b.Sun = new Vector2(10, -20);
            b.Lit["windows"] = 1;
            var d = new EnvPreset();
            EnvPreset.LerpInto(d, a, b, 0);
            Assert.That(d.KeyIntensity, Is.EqualTo(4f)); Assert.That(d.Name, Is.EqualTo("day"));
            EnvPreset.LerpInto(d, a, b, 1);
            Assert.That(d.KeyIntensity, Is.EqualTo(0.2f).Within(1e-5f)); Assert.That(d.Name, Is.EqualTo("night"));
            EnvPreset.LerpInto(d, a, b, 0.5f);
            Assert.That(d.KeyIntensity, Is.EqualTo(2.1f).Within(1e-5f));
            Assert.That(d.Lit["street"], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(d.Lit["windows"], Is.EqualTo(1f), "a group only the target names takes its value");
            Assert.That(d.Sun.x, Is.EqualTo(0f).Within(1e-3f).Or.EqualTo(360f).Within(1e-3f), "the azimuth turns the short way (350 -> 10)");
            Assert.That(EnvPreset.Ease(0.5f), Is.EqualTo(0.5f)); Assert.That(EnvPreset.Ease(1.2f), Is.EqualTo(1f));
        }

        [Test]
        public void Env_BlendEndsWithoutAJump()
        {
            var a = P("dusk", 0.7f, Color.red, 0.35f); var b = P("night", 0.28f, Color.blue, 1f);
            var d = new EnvPreset();
            EnvPreset.LerpInto(d, a, b, EnvPreset.Ease(0.999f));
            var end = new EnvPreset(); end.CopyFrom(b);
            Assert.That(Mathf.Abs(d.KeyIntensity - end.KeyIntensity), Is.LessThan(1e-3f));
            Assert.That(Mathf.Abs(d.NightAmount - end.NightAmount), Is.LessThan(1e-3f));
        }

        [Test]
        public void Env_TuneDarkensNightFillAndTamesDusk()
        {
            var night = P("night", 0.28f, Color.white, 1f); night.FillIntensity = 1.8f;
            EnvPreset.Tune(night);
            Assert.That(night.FillIntensity, Is.EqualTo(1.08f).Within(1e-4f), "-40 %");
            var day = P("day", 4.6f, Color.white, 0f); day.FillIntensity = 0.5f;
            EnvPreset.Tune(day);
            Assert.That(day.FillIntensity, Is.EqualTo(0.5f), "day untouched");
            var dusk = P("dusk", 0.7f, new Color(0.42f, 0.44f, 0.66f), 0.35f); dusk.Tint = new Color(1f, 0.6f, 0.95f);
            EnvPreset.Tune(dusk);
            Color.RGBToHSV(dusk.Tint, out _, out float s, out _);
            Assert.That(s, Is.LessThanOrEqualTo(0.0601f));
            var blackout = P("blackout", 0.6f, Color.white, 1f); blackout.FillIntensity = 0.5f; // env-presets.json: blackout hemi 0.5, night 1.8
            EnvPreset.Tune(blackout);
            Assert.That(blackout.FillIntensity, Is.LessThan(night.FillIntensity * 0.75f), "darker than a lit night");
            Assert.That(blackout.FillIntensity, Is.GreaterThan(0.5f * 0.6f), "but not a black frame under the trees");
            var room = P("interior", 2.2f, Color.white, 0f, outdoor: false); room.BloomThreshold = 0.9f;
            EnvPreset.Tune(room);
            Assert.That(room.Exposure, Is.EqualTo(0.71f).Within(1e-4f)); Assert.That(room.BloomThreshold, Is.GreaterThanOrEqualTo(1.1f));
        }

        [Test]
        public void Env_CapSaturationKeepsHueAndValue()
        {
            var c = EnvPreset.CapSaturation(new Color(1f, 0f, 1f), 0.2f);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Assert.That(s, Is.EqualTo(0.2f).Within(1e-3f)); Assert.That(v, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(h, Is.EqualTo(5f / 6f).Within(1e-3f));
            var grey = new Color(0.5f, 0.5f, 0.5f);
            Assert.That(EnvPreset.CapSaturation(grey, 0.2f), Is.EqualTo(grey));
        }

        // ---------- light pools, lit merge

        [Test]
        public void Pools_AreTheBlackTransparentEmissiveMaterials()
        {
            Assert.That(LevelMaterials.IsGlowPool(3000, Color.black, new Color(1f, 0.7f, 0.4f)), Is.True);
            Assert.That(LevelMaterials.IsGlowPool(2000, Color.black, new Color(1f, 0.7f, 0.4f)), Is.False, "opaque");
            Assert.That(LevelMaterials.IsGlowPool(3000, Color.white, new Color(1f, 0.7f, 0.4f)), Is.False, "a lit pane");
            Assert.That(LevelMaterials.IsGlowPool(3000, Color.black, Color.black), Is.False);
        }

        [Test]
        public void Pools_FollowTheStreetLevelAndTheDark()
        {
            Assert.That(LevelMaterials.PoolGlow(0f, 0f), Is.EqualTo(0f), "day: off");
            Assert.That(LevelMaterials.PoolGlow(0f, 1f), Is.EqualTo(0f), "blackout (no grid): off");
            Assert.That(LevelMaterials.PoolGlow(1f, 1f), Is.EqualTo(1f), "night: full");
            float dusk = LevelMaterials.PoolGlow(1f, 0.35f);
            Assert.That(dusk, Is.InRange(0.3f, 0.7f), "dusk: partial");
            Assert.That(LevelMaterials.PoolFalloff(0f), Is.EqualTo(1f));
            Assert.That(LevelMaterials.PoolFalloff(1f), Is.EqualTo(0f), "no rim");
            Assert.That(LevelMaterials.PoolFalloff(0.5f), Is.LessThan(LevelMaterials.PoolFalloff(0.2f)));
        }

        [Test]
        public void MergeLitInto_MatchesMergeLit()
        {
            var prev = new Dictionary<string, float> { ["indoor"] = 0.3f, ["street"] = 1f };
            var preset = new Dictionary<string, float> { ["street"] = 1f, ["windows"] = 0.7f, ["emergency"] = 1f };
            var a = LevelMaterials.MergeLit(prev, preset, false);
            var b = new Dictionary<string, float>(prev);
            LevelMaterials.MergeLitInto(b, preset, false);
            Assert.That(b, Is.EquivalentTo(a));
            Assert.That(b["street"], Is.EqualTo(0f)); Assert.That(b["indoor"], Is.EqualTo(0.3f)); Assert.That(b["emergency"], Is.EqualTo(1f));
        }

        // ---------- flashlight

        [Test]
        public void Flashlight_DimsCloseToAWall()
        {
            Assert.That(PlayerFlashlight.NearDim(10f), Is.EqualTo(1f));
            Assert.That(PlayerFlashlight.NearDim(0.2f), Is.EqualTo(PlayerFlashlight.MinLevel));
            Assert.That(PlayerFlashlight.NearDim(1.2f), Is.InRange(PlayerFlashlight.MinLevel, 0.9f));
            Assert.That(PlayerFlashlight.NearDim(2f), Is.GreaterThan(PlayerFlashlight.NearDim(1f)));
        }

        // ---------- footsteps

        [Test]
        public void Footsteps_OneStrikePerStride()
        {
            float m = 0; int steps = 0;
            for (int i = 0; i < 100; i++) if (PlayerBody.StepDue(ref m, 0.07f, false)) steps++; // 7 m walking
            Assert.That(steps, Is.EqualTo(10));
            m = 0; steps = 0;
            for (int i = 0; i < 100; i++) if (PlayerBody.StepDue(ref m, 0.09f, true)) steps++; // 9 m running
            Assert.That(steps, Is.EqualTo(10));
            m = 0;
            Assert.That(PlayerBody.StepDue(ref m, 5f, false), Is.True);
            Assert.That(m, Is.LessThan(PlayerBody.WalkStride), "a jump in position is one step, not a burst");
        }

        [Test]
        public void Footsteps_SurfaceByPlace()
        {
            Assert.That(PlayerBody.Surface("town", false), Is.EqualTo("concrete"));
            Assert.That(PlayerBody.Surface("shelter", false), Is.EqualTo("concrete"), "app.js STEP_FLOOR: the shelter is concrete");
            Assert.That(PlayerBody.Surface("apartment", false), Is.EqualTo("tile"));
            Assert.That(PlayerBody.Surface("store", false), Is.EqualTo("tile"));
            Assert.That(PlayerBody.Surface(null, false), Is.EqualTo("concrete"));
            Assert.That(PlayerBody.Surface("office", true), Is.EqualTo("stairs"));
            Assert.That(PlayerBody.StepVoice("tile").pitch, Is.GreaterThan(PlayerBody.StepVoice("concrete").pitch));
            Assert.That(PlayerBody.StepVoice("stairs").pitch, Is.LessThan(PlayerBody.StepVoice("concrete").pitch));
        }

        [Test]
        public void TalkFraming_BroadcastsAndRadiosAreNotFaceToFace()
        {
            Assert.That(TalkFraming.NotFaceToFace("방송"), Is.True, "Evac: 박영수 소장's PA after the fire");
            Assert.That(TalkFraming.NotFaceToFace("문자"), Is.True);
            Assert.That(TalkFraming.NotFaceToFace("문 너머"), Is.True);
            Assert.That(TalkFraming.NotFaceToFace("무전"), Is.True);
            Assert.That(TalkFraming.NotFaceToFace(null), Is.False);
            Assert.That(TalkFraming.NotFaceToFace("관리사무소장"), Is.False);
        }

        [Test]
        public void TalkFraming_BoardShotSitsSquareInFrontAtEyeHeight()
        {
            var board = new Vector3(2f, 1.45f, 5f);
            var normal = new Vector3(0, 0.1f, -1f);
            var c = TalkFraming.BoardCamera(board, normal, 1.6f, 1.8f);
            Assert.That(c.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(c.z, Is.EqualTo(5f - 1.8f).Within(1e-4f));
            Assert.That(c.y, Is.EqualTo(1.6f));
            var target = TalkFraming.AimAt(c, board, TalkFraming.BoardFov, TalkFraming.BoardFromTop);
            float at = TalkFraming.ScreenFromTop(c, target, board, TalkFraming.BoardFov);
            Assert.That(at, Is.LessThan(0.55f), "the board sits in the upper part, above the dialog");
        }

        [Test]
        public void Env_ReflectionsFadeWithTheDark()
        {
            Assert.That(EnvironmentController.ReflectionFor(0f, true, true), Is.EqualTo(1f));
            Assert.That(EnvironmentController.ReflectionFor(1f, true, true), Is.EqualTo(0.15f).Within(1e-4f));
            Assert.That(EnvironmentController.ReflectionFor(1f, false, true), Is.LessThan(EnvironmentController.ReflectionFor(1f, true, true)));
            Assert.That(EnvironmentController.ReflectionFor(0.35f, true, true), Is.InRange(0.6f, 0.8f));
        }

        [Test]
        public void Rig_OnlyTheInteriorPresetsGetTheTightFraming()
        {
            Assert.That(PlayerCameraRig.IsInteriorPreset("interior"), Is.True);
            Assert.That(PlayerCameraRig.IsInteriorPreset("interiorDark"), Is.True);
            Assert.That(PlayerCameraRig.IsInteriorPreset("underground"), Is.False, "the shelter car park keeps the full boom");
            Assert.That(PlayerCameraRig.IsInteriorPreset(null), Is.False);
        }

        [Test]
        public void Flashlight_OutdoorsAimsAtTheRoadAhead()
        {
            var feet = new Vector3(1f, 0.2f, 1f);
            var g = PlayerFlashlight.GroundAim(feet, new Vector3(0, -0.05f, 1f), PlayerFlashlight.GroundReach);
            Assert.That(g.y, Is.EqualTo(0.2f));
            Assert.That(Vector3.Distance(new Vector3(g.x, 0, g.z), new Vector3(feet.x, 0, feet.z)), Is.EqualTo(PlayerFlashlight.GroundReach).Within(1e-4f));
            Assert.That(g.z, Is.GreaterThan(feet.z));
        }
    }
}
