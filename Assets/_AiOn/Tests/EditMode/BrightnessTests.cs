using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AiOn.Levels;
using AiOn.Rendering;
using AiOn.Settings;
using AiOn.Story;
using AiOn.Story.Testing;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Feedback B3: the 20:05 siren cutscene was a black frame (mean luma 5 of 255). A cutscene outdoors at night lifts the
    /// exposure, the ambient fill and the moon (AtmosphereMath.CineNightLift), and the settings have a 화면 밝기 slider
    /// (stops over every look, 0 .. 1.5, saved with the other settings). The presets' own numbers stay as they are.
    /// </summary>
    public class BrightnessTests
    {
        static Dictionary<string, EnvPreset> s_presets;
        static Dictionary<string, EnvPreset> Presets => s_presets ??= EnvPreset.ParseAll(File.ReadAllText("Assets/_AiOn/Data/Source/env-presets.json"));

        // ---------- the night cutscene lift

        [Test]
        public void Lift_FullInTheBlackout_HalfOnALitNight_NoneElsewhere()
        {
            Assert.That(AtmosphereMath.CineNightLift(true, Presets["blackout"]), Is.EqualTo(1f).Within(1e-5f), "the siren scene's dark street");
            Assert.That(AtmosphereMath.CineNightLift(true, Presets["night"]), Is.EqualTo(0.5f).Within(1e-5f), "street lamps on: half");
            foreach (var name in new[] { "day", "afternoon", "dusk", "interior", "interiorNight", "interiorDark", "underground" })
                Assert.That(AtmosphereMath.CineNightLift(true, Presets[name]), Is.EqualTo(0f), name);
            Assert.That(AtmosphereMath.CineNightLift(false, Presets["blackout"]), Is.EqualTo(0f), "only while a cutscene runs");
            Assert.That(AtmosphereMath.CineNightLift(true, null), Is.EqualTo(0f));
        }

        [Test]
        public void Lift_FollowsABlendIntoTheBlackout()
        {
            var mid = new EnvPreset();
            EnvPreset.LerpInto(mid, Presets["dusk"], Presets["blackout"], 0.75f);
            float k = AtmosphereMath.CineNightLift(true, mid);
            Assert.That(k, Is.GreaterThan(0f).And.LessThan(1f), "a time-lapse eases into it");
        }

        [Test]
        public void Lift_ExposureFillAndKey()
        {
            Assert.That(AtmosphereMath.ExposureBias(0f, 0f), Is.EqualTo(0f));
            Assert.That(AtmosphereMath.ExposureBias(0f, 1f), Is.EqualTo(AtmosphereMath.CineNightStops));
            Assert.That(AtmosphereMath.ExposureBias(0.5f, 1f), Is.EqualTo(0.5f + AtmosphereMath.CineNightStops).Within(1e-5f), "the setting adds on top");
            Assert.That(AtmosphereMath.ExposureBias(0f, 3f), Is.EqualTo(AtmosphereMath.CineNightStops), "the lift is clamped");
            Assert.That(AtmosphereMath.FillGain(0f), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.KeyGain(0f), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.FillGain(1f), Is.EqualTo(AtmosphereMath.CineNightFill).Within(1e-5f));
            Assert.That(AtmosphereMath.KeyGain(1f), Is.EqualTo(AtmosphereMath.CineNightKey).Within(1e-5f));
            Assert.That(AtmosphereMath.FillGain(0.5f), Is.InRange(1f, AtmosphereMath.CineNightFill));
            // still night: about 7.5x on the shaded street (the fill is a gamma colour), not a day-for-night wash
            float shaded = Mathf.Pow(2f, AtmosphereMath.CineNightStops) * Mathf.Pow(AtmosphereMath.CineNightFill, 2.2f);
            Assert.That(shaded, Is.InRange(5f, 10f));
            Assert.That(AtmosphereMath.CineNightStops + (float)SettingsMath.BrightnessMax, Is.LessThanOrEqualTo(3f), "the most it can ever add");
        }

        [Test]
        public void PostExposure_KeepsThePresetNumbersAndAddsTheBias()
        {
            var b = Presets["blackout"];
            float tuned = Mathf.Log(1.35f, 2f) + 0.1f + 0.6f; // env-presets.json blackout exposure 1.35, grade exposure 0.1; URP offset 0.6
            Assert.That(EnvironmentController.PostExposure(b, null, 0.6f, 0f), Is.EqualTo(tuned).Within(1e-4f), "unchanged without a bias");
            Assert.That(EnvironmentController.PostExposure(b, 1.55f, 0.6f, 0f), Is.EqualTo(tuned + 1.55f).Within(1e-4f), "a shot's own stops");
            Assert.That(EnvironmentController.PostExposure(b, 1.55f, 0.6f, 1.25f), Is.EqualTo(tuned + 2.8f).Within(1e-4f), "plus the lift");
            Assert.That(EnvironmentController.SmokeExposure, Is.EqualTo(-0.9f));
            Assert.That(AtmosphereDirector.FocusExposure, Is.EqualTo(-0.45f));
        }

        // ---------- the 화면 밝기 setting

        [Test]
        public void Brightness_ClampsToZeroToOneAndAHalfStops()
        {
            Assert.That(SettingsMath.Brightness(double.NaN), Is.EqualTo(0f));
            Assert.That(SettingsMath.Brightness(-1), Is.EqualTo(0f));
            Assert.That(SettingsMath.Brightness(0), Is.EqualTo(0f));
            Assert.That(SettingsMath.Brightness(0.5), Is.EqualTo(0.5f));
            Assert.That(SettingsMath.Brightness(1.5), Is.EqualTo(1.5f));
            Assert.That(SettingsMath.Brightness(3), Is.EqualTo(1.5f));
            Assert.That(SettingsMath.Brightness(double.PositiveInfinity), Is.EqualTo(1.5f));
            Assert.That(SettingsMath.Brightness(double.NegativeInfinity), Is.EqualTo(0f));
        }

        [Test]
        public void Brightness_FormDefaultsToZeroAndStepsByAQuarterStop()
        {
            var f = new SettingsForm(new JObject());
            Assert.That((double)SettingsForm.Defaults()[SettingsForm.BrightnessKey], Is.EqualTo(0));
            Assert.That(f.Range[SettingsForm.BrightnessKey], Is.EqualTo(0));
            Assert.That(f.Brightness.Label, Is.EqualTo("화면 밝기"));
            Assert.That(f.RangeText("brightness"), Is.EqualTo("기본"));
            Assert.That((double)f.Submit()["brightness"], Is.EqualTo(0));
            f.StepRange("brightness", +1);
            Assert.That(f.RangeText("brightness"), Is.EqualTo("+1"));
            Assert.That(f.BrightnessStops, Is.EqualTo(0.25));
            Assert.That((double)f.Submit()["brightness"], Is.EqualTo(0.25));
            f.SetRange("brightness", 999);
            Assert.That(f.RangeText("brightness"), Is.EqualTo("+6"));
            Assert.That((double)f.Submit()["brightness"], Is.EqualTo(1.5));
            f.StepRange("brightness", +1);
            Assert.That(f.Range["brightness"], Is.EqualTo(150), "stays at the top");
            f.SetRange("brightness", -40);
            Assert.That(f.Range["brightness"], Is.EqualTo(0), "and at the bottom");
            f.SetRange("brightness", 60);
            Assert.That(f.Range["brightness"], Is.EqualTo(50), "snaps to the nearest quarter stop");
        }

        [Test]
        public void Brightness_ReadsSavedValues_ClampedAndSnapped()
        {
            int Read(string json) => new SettingsForm(JObject.Parse(json)).Range["brightness"];
            Assert.That(Read("{brightness:0.75}"), Is.EqualTo(75));
            Assert.That(Read("{brightness:0.8}"), Is.EqualTo(75), "0.8 snaps to 0.75");
            Assert.That(Read("{brightness:5}"), Is.EqualTo(150));
            Assert.That(Read("{brightness:-2}"), Is.EqualTo(0));
            Assert.That(Read("{brightness:'x'}"), Is.EqualTo(0), "not a number: 0, not the midpoint the other ranges take");
            Assert.That(Read("{brightness:'1'}"), Is.EqualTo(100));
            Assert.That(Read("{}"), Is.EqualTo(0), "an old save without the key");
        }

        [Test]
        public void Brightness_Cancel_KeepsTheSavedValue()
        {
            var f = new SettingsForm(JObject.Parse("{brightness:0.5}"));
            f.SetRange("brightness", 150);
            Assert.That((double)f.Cancel()["brightness"], Is.EqualTo(0.5), "취소: the screen's preview is put back from this");
        }

        [Test]
        public void Brightness_IsSavedWithTheSettings_AndHandedToTheHost()
        {
            var host = new FakeStoryHost();
            var store = new MemoryStoryStore();
            var parts = new DirectorParts { Chapters = new List<DirectorChapter> { new("prologue", c => Task.FromResult<JToken>(null)) } };
            var d = new StoryDirector(Rules, FakeStoryHost.LoadFacts(), host, host, host, host, store, parts, host);
            JObject applied = null;
            d.ApplyHostSettings = (s, body) => applied = s;

            var form = new SettingsForm((JObject)d.Settings.DeepClone());
            form.SetRange("brightness", 75);
            d.UpdateSettings(form.Submit());
            Assert.That((double)applied["brightness"], Is.EqualTo(0.75), "HostSettings.Apply gets it");
            Assert.That((double)JObject.Parse(store.Get(StoryDirector.SettingsKey))["brightness"], Is.EqualTo(0.75), "saved with the other settings");

            // the next launch reads it back
            var again = new StoryDirector(Rules, FakeStoryHost.LoadFacts(), host, host, host, host, store, parts, host);
            Assert.That((double)again.Settings["brightness"], Is.EqualTo(0.75));
            Assert.That(new SettingsForm((JObject)again.Settings.DeepClone()).Range["brightness"], Is.EqualTo(75));
            Assert.That(SettingsMath.Brightness((double)again.Settings["brightness"]), Is.EqualTo(0.75f));
        }
    }
}
