// The settings the host applies (app.js applySettings, engine/quality.js, audio.js, controls.js stairs): tier names,
// the first-run guess, the frame-rate governor, the sound gains, the wheelchair's stair rule and the small helpers.
using System.Collections.Generic;
using AiOn.Levels;
using AiOn.Settings;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class SettingsTests
    {
        // ------------------------------------------------------------------ tiers

        [Test]
        public void TierNamesAliasesAndSteps()
        {
            Assert.AreEqual("ultralow", QualityTiers.Norm("very-low"));
            Assert.AreEqual("ultra", QualityTiers.Norm("max"));
            Assert.AreEqual("medium", QualityTiers.Norm("normal"));
            Assert.AreEqual("high", QualityTiers.Norm("high"));
            Assert.AreEqual("medium", QualityTiers.Norm("auto"));
            Assert.IsNull(QualityTiers.Norm("auto", null));
            Assert.IsNull(QualityTiers.Norm(null, null));
            Assert.AreEqual("medium", QualityTiers.Lower("high"));
            Assert.AreEqual("ultralow", QualityTiers.Lower("ultralow"), "never below the lowest");
            Assert.AreEqual(4, QualityTiers.Rank("ultra"));
            Assert.AreEqual("높음", QualityTiers.Label("high"));
            Assert.AreEqual("완전 낮음", QualityTiers.Label("ultralow"));
        }

        [Test]
        public void SavedQualityKeepsATierAndDetectsTheRest()
        {
            Assert.AreEqual(("low", false), QualityTiers.Saved("low", false));
            Assert.AreEqual(("medium", true), QualityTiers.Saved("medium", true), "a detected tier the governor lowered is kept");
            Assert.AreEqual(((string)null, true), QualityTiers.Saved("auto", false));
            Assert.AreEqual(((string)null, true), QualityTiers.Saved(null, false));
        }

        [Test]
        public void FirstRunGuessFollowsTheMachine()
        {
            Assert.AreEqual("high", QualityTiers.AutoTier("NVIDIA GeForce RTX 3060", 8, 16, 2e6));
            Assert.AreEqual("low", QualityTiers.AutoTier("Intel(R) UHD Graphics 620", 8, 16, 2e6));
            Assert.AreEqual("medium", QualityTiers.AutoTier("Some Unknown GPU", 8, 16, 2e6));
            Assert.AreEqual("ultralow", QualityTiers.AutoTier("Microsoft Basic Render Driver"));
            Assert.AreEqual("medium", QualityTiers.AutoTier("NVIDIA GeForce RTX 3060", 4, 16, 2e6), "four cores: one down");
            Assert.AreEqual("low", QualityTiers.AutoTier("NVIDIA GeForce RTX 3060", 2, 16, 2e6), "two cores: two down");
            Assert.AreEqual("medium", QualityTiers.AutoTier("NVIDIA GeForce RTX 3060", 8, 16, 8.3e6), "a 4K screen: one down");
            Assert.AreEqual("ultralow", QualityTiers.AutoTier("Intel HD Graphics", 2, 2, 2e6));
            foreach (var g in new[] { "rtx 4090", "radeon rx 7900", "quadro p4000" })
                Assert.AreNotEqual("ultra", QualityTiers.AutoTier(g, 32, 64, 2e6), "ultra is never guessed");
        }

        [Test]
        public void TierKnobsGiveUpInOrder()
        {
            var hi = QualityTiers.KnobsOf("high");
            Assert.AreEqual(50f, hi.ShadowDistance); Assert.AreEqual(2048, hi.MainShadowMap); Assert.AreEqual(4, hi.Cascades); Assert.AreEqual(2, hi.Smaa);
            Assert.IsTrue(hi.FlashShadows);
            var ul = QualityTiers.KnobsOf("ultralow");
            Assert.AreEqual(0f, ul.ShadowDistance, "no sun shadow"); Assert.AreEqual(0.75f, ul.RenderScale); Assert.AreEqual(0, ul.Smaa); Assert.IsFalse(ul.FlashShadows);
            Assert.AreEqual(hi.ShadowDistance, QualityTiers.KnobsOf("whatever").ShadowDistance, "unknown is high");
            float last = -1;
            foreach (var t in QualityTiers.Tiers)
            {
                var k = QualityTiers.KnobsOf(t);
                Assert.GreaterOrEqual(k.ShadowDistance, last, t);
                last = k.ShadowDistance;
            }
        }

        // ------------------------------------------------------------------ governor

        static int Run(FrameGovernor g, double fps, double seconds, bool busy = false, bool floor = false)
        {
            int steps = 0;
            for (double t = 0; t < seconds; t += 1 / fps) if (g.Sample(1 / fps, busy, floor)) steps++;
            return steps;
        }

        [Test]
        public void GovernorStepsAfterFourSlowSecondsOnce()
        {
            var g = new FrameGovernor();
            Assert.AreEqual(0, Run(g, 60, 5), "fast is fine");
            Assert.AreEqual(0, Run(g, 20, 3.9), "not yet");
            Assert.AreEqual(1, Run(g, 20, 1));
            Assert.AreEqual(0, Run(g, 20, 5), "it waits 6 s after a step");
            Assert.AreEqual(1, Run(g, 20, 8), "then judges again");
        }

        [Test]
        public void GovernorSettlesHoldsAndIgnoresBusyAndTheFloor()
        {
            var g = new FrameGovernor();
            Assert.AreEqual(0, Run(g, 20, 6.9), "3 s settle at the start, then 4 s slow are needed");
            g = new FrameGovernor(settle: 0);
            Assert.AreEqual(0, Run(g, 20, 20, busy: true));
            Assert.AreEqual(0, Run(g, 20, 20, floor: true));
            Run(g, 20, 3); g.Hold(4);
            Assert.AreEqual(0, Run(g, 20, 3.5), "a hold resets the count");
            Assert.AreEqual(0, Run(g, 34, 20), "between the lines it drains, never steps");
            Assert.AreEqual(0, Run(g, 20, 3)); Assert.AreEqual(0, Run(g, 45, 1)); Assert.AreEqual(0, Run(g, 20, 3), "fast resets the count");
            Assert.IsFalse(g.Sample(3), "a long gap (hidden window) resets the window");
        }

        // ------------------------------------------------------------------ sound

        [Test]
        public void MasterIsTheVolumeUnlessMutedOrDeaf()
        {
            Assert.AreEqual(0.7f, AudioBus.Master(0.7, false, false), 1e-6);
            Assert.AreEqual(0f, AudioBus.Master(0.7, true, false));
            Assert.AreEqual(0f, AudioBus.Master(0.7, false, true), "청각 장애: no sound");
            Assert.AreEqual(1f, AudioBus.Master(3, false, false));
            Assert.AreEqual(0f, AudioBus.Master(double.NaN, false, false));
        }

        [Test]
        public void BusesClampAndTellTheirSources()
        {
            int n = 0;
            System.Action f = () => n++;
            AudioBus.Changed += f;
            try
            {
                AudioBus.Set(0.5, 2, -1);
                Assert.AreEqual(0.5f, AudioBus.Ambience); Assert.AreEqual(1f, AudioBus.Effects); Assert.AreEqual(0f, AudioBus.Ui);
                AudioBus.Set(0.5, 1, 0);
                Assert.AreEqual(1, n, "no change, no call");
            }
            finally { AudioBus.Changed -= f; AudioBus.Set(1, 1, 1); }
        }

        // ------------------------------------------------------------------ the wheelchair's stairs

        static List<LevelLayout.WalkZone> Walk() => new()
        {
            new LevelLayout.WalkZone { x0 = -10, x1 = 0, z0 = -1, z1 = 1, y = 0 },                                                       // floor
            new LevelLayout.WalkZone { x0 = 0, x1 = 4, z0 = -1, z1 = 1, isRamp = true, rampAxis = "x", rampY0 = 0, rampY1 = 3 },         // a flight
            new LevelLayout.WalkZone { x0 = 4, x1 = 8, z0 = -1, z1 = 1, y = 3 },                                                         // landing
            new LevelLayout.WalkZone { x0 = -10, x1 = 0, z0 = 2, z1 = 12, isRamp = true, rampAxis = "z", rampY0 = 0, rampY1 = 1 },       // a gentle ramp
        };

        [Test]
        public void GroundAtFollowsRampsAndReach()
        {
            var w = Walk();
            Assert.IsTrue(StairRule.GroundAt(w, 2, 0, 1.5, out var y, out var s, out var i));
            Assert.AreEqual(1.5, y, 1e-9); Assert.AreEqual(0.75, s, 1e-9); Assert.AreEqual(1, i);
            Assert.IsFalse(StairRule.GroundAt(w, 2, 0, 5, out _, out _, out _), "too far below the feet");
            Assert.IsTrue(StairRule.GroundAt(w, -5, 7, 0.5, out _, out s, out _)); Assert.AreEqual(0.1, s, 1e-9);
        }

        [Test]
        public void AFlightIsAWallFromTheFloorAndFromTheLanding()
        {
            var w = Walk();
            Assert.AreEqual(1, StairRule.Blocking(w, -0.3, 0, 0.1, 0, 0), "from the foot");
            Assert.AreEqual(1, StairRule.Blocking(w, 4.3, 0, 3.9, 0, 3), "from the top");
            Assert.AreEqual(-1, StairRule.Blocking(w, -3, 0, -2.5, 0, 0), "flat floor");
            Assert.AreEqual(-1, StairRule.Blocking(w, -5, 1.9, -5, 2.3, 0), "a gentle ramp is not stairs");
            Assert.AreEqual(-1, StairRule.Blocking(w, 2, 0, 2.2, 0, 1.5), "already on the flight (carried): may leave it");
        }

        [Test]
        public void StandingStillAtTheFootIsNeverAStop()
        {
            // The controller asks every grounded frame; standing still asks with ahead == feet, which re-arms the host's
            // contact latch (controls.js: blocked = stop with px == position), so a second push offers help again.
            var w = Walk();
            Assert.AreEqual(-1, StairRule.Blocking(w, -0.3, 0, -0.3, 0, 0), "still at the foot");
            Assert.AreEqual(-1, StairRule.Blocking(w, 4.3, 0, 4.3, 0, 3), "still at the top");
            Assert.AreEqual(1, StairRule.Blocking(w, -0.3, 0, 0.1, 0, 0), "pushing again stops again");
        }

        [Test]
        public void MenuBlipsGoThroughTheUiBus()
        {
            Assert.IsTrue(AiOn.Story.UnityStoryHost.IsUiCue("click"));
            Assert.IsTrue(AiOn.Story.UnityStoryHost.IsUiCue("confirm"));
            Assert.IsFalse(AiOn.Story.UnityStoryHost.IsUiCue("boom"));
            Assert.IsFalse(AiOn.Story.UnityStoryHost.IsUiCue("hit"));
        }

        [Test]
        public void ZoneJsonIsWhatTheStairHelpReads()
        {
            var z = StairRule.ZoneJson(Walk()[1]);
            Assert.AreEqual("x", (string)z["ramp"]["axis"]);
            Assert.AreEqual(3.0, (double)z["ramp"]["y1"], 1e-9);
            Assert.AreEqual(4.0, (double)z["x1"], 1e-9);
            Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, StairRule.ZoneJson(Walk()[0])["ramp"].Type);
        }

        // ------------------------------------------------------------------ helpers

        [Test]
        public void SensitivityTextScaleAndSpeechLines()
        {
            Assert.AreEqual(1f, SettingsMath.Sensitivity(0));
            Assert.AreEqual(0.3f, SettingsMath.Sensitivity(0.1), 1e-6);
            Assert.AreEqual(3f, SettingsMath.Sensitivity(9));
            Assert.AreEqual(1.15f, SettingsMath.Sensitivity(1.15), 1e-6);
            Assert.AreEqual(1.3f, SettingsMath.TextScale(1.3), 1e-6);
            Assert.AreEqual(1f, SettingsMath.TextScale(double.NaN));
            Assert.AreEqual(34, SettingsMath.Scaled(26, 1.3f));
            Assert.AreEqual(23, SettingsMath.Scaled(26, 0.9f));
            var line = SettingsMath.SpeechLine("계단 앞이다.\n휠체어");
            Assert.IsFalse(line.Contains("\n"));
            Assert.AreEqual("계단 앞이다. 휠체어", System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(line)));
        }
    }
}
