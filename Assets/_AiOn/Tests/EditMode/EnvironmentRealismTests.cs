using System.Collections.Generic;
using System.IO;
using AiOn.Levels;
using AiOn.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>"그래픽 사실화" environment package: window light, surfaces, decals, sky photos, fire flicker.</summary>
    public class EnvironmentRealismTests
    {
        // ---------- window light

        [Test]
        public void Window_OutsideLightFollowsTheDayAndTheGrid()
        {
            var (day, dayC) = WindowLightMath.Outside(0f, true);
            var (city, _) = WindowLightMath.Outside(1f, true);
            var (moon, moonC) = WindowLightMath.Outside(1f, false);
            Assert.That(day, Is.EqualTo(WindowLightMath.DayIntensity).Within(1e-4f));
            Assert.That(city, Is.EqualTo(WindowLightMath.CityNight).Within(1e-4f));
            Assert.That(moon, Is.EqualTo(WindowLightMath.MoonNight).Within(1e-4f));
            Assert.That(day, Is.GreaterThan(city * 5f), "daylight far above the night");
            Assert.That(moonC.b, Is.GreaterThan(moonC.r), "moonlight is cool");
            Color.RGBToHSV(dayC, out _, out float s, out _);
            Assert.That(s, Is.LessThan(0.1f), "daylight is near neutral");
            var (dusk, _) = WindowLightMath.Outside(0.35f, true);
            Assert.That(dusk, Is.InRange(city, day));
        }

        [Test]
        public void Window_LightSitsOutsideAndAimsIntoTheRoom()
        {
            var w = new WindowOpening(new Vector3(0, 7, 2), Vector3.back, 4.5f, 1.1f);
            var at = WindowLightMath.LightPosition(w);
            Assert.That(at.z, Is.GreaterThan(2f), "outside the pane (inward is -z)");
            Assert.That(at.y, Is.GreaterThan(7f));
            var dir = WindowLightMath.LightDirection(w);
            Assert.That(dir.z, Is.LessThan(-0.8f)); Assert.That(dir.y, Is.LessThan(0f));
            Assert.That(WindowLightMath.RangeFor(4.5f, 1.1f), Is.GreaterThan(WindowLightMath.SetBack + 5f));
            Assert.That(WindowLightMath.RangeFor(10f, 10f), Is.LessThanOrEqualTo(WindowLightMath.SetBack + 9f));
        }

        [Test]
        public void Window_InteriorLevelsHaveWindowsAndTheShelterNone()
        {
            foreach (var id in new[] { "apartment", "store", "mart", "office" })
                Assert.That(WindowLightMath.For(id), Is.Not.Empty, id);
            Assert.That(WindowLightMath.For("shelter"), Is.Empty);
            Assert.That(WindowLightMath.For("town"), Is.Empty);
            foreach (var w in WindowLightMath.For("apartment")) Assert.That(w.Inward.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Env_DaylightVariantHasAMorningSun()
        {
            var e = new EnvPreset { Name = "interiorDark", NightAmount = 1f, KeyColor = Color.blue, KeyIntensity = 0.5f };
            EnvironmentController.DaylightVariant(e);
            Assert.That(e.Key.y, Is.InRange(10f, 35f), "a low sun that reaches through the south glass");
            Assert.That(e.Key.x, Is.InRange(90f, 180f), "from the south-east");
            Assert.That(e.KeyIntensity, Is.GreaterThan(1.5f));
            Assert.That(AtmosphereMath.BaseWeightFor(AtmosphereMath.DarkByDay), Is.LessThan(AtmosphereMath.BaseWeightFor("interiorDark")));
        }

        // ---------- surfaces

        static string SurfacesJson => File.ReadAllText(Path.Combine(Application.dataPath, "_AiOn/Resources/Surfaces/surfaces.json"));

        [Test]
        public void Surfaces_TableParsesAndEveryKindHasItsMaps()
        {
            var t = SurfaceTable.Parse(SurfacesJson);
            Assert.That(t.Kinds.Count, Is.GreaterThanOrEqualTo(8));
            foreach (var k in t.Kinds.Values)
            {
                Assert.That(File.Exists(Path.Combine(Application.dataPath, $"_AiOn/Resources/Surfaces/surf_{k.Set}_n.jpg")), k.Name);
                Assert.That(File.Exists(Path.Combine(Application.dataPath, $"_AiOn/Resources/Surfaces/surf_{k.Set}_a.jpg")), k.Name);
                Assert.That(k.Scale, Is.InRange(0.1f, 4f), k.Name);
                Assert.That(k.Normal, Is.InRange(0f, 2f), k.Name);
            }
            foreach (var lv in t.Levels)
                foreach (var m in lv.Value)
                    Assert.That(t.Kinds.ContainsKey(m.Value), $"{lv.Key}/{m.Key} -> {m.Value}");
            foreach (var id in new[] { "apartment", "town", "mart", "store", "office", "shelter" })
                Assert.That(t.Levels.ContainsKey(id), id);
        }

        [Test]
        public void Surfaces_KeyDropsRuntimeSuffixes()
        {
            Assert.That(SurfaceTable.KeyOf("m~m22 (state)"), Is.EqualTo("m~m22"));
            Assert.That(SurfaceTable.KeyOf("m~m22 (Instance)"), Is.EqualTo("m~m22"));
            Assert.That(SurfaceTable.KeyOf("lit:windows~m66"), Is.EqualTo("lit:windows~m66"));
            var t = SurfaceTable.Parse(SurfacesJson);
            Assert.That(t.KindFor("apartment", "m~m19 (state)")?.Name, Is.EqualTo("wood"));
            Assert.That(t.KindFor("apartment", "m~m99999"), Is.Null);
            Assert.That(t.KindFor("nowhere", "m~m19"), Is.Null);
        }

        [Test]
        public void Surfaces_BackdropsAndGlassCastNoShadow()
        {
            Assert.That(LevelSurfaces.IsBackdrop(new Vector3(300, 120, 300)), Is.True, "the apartment's sky cylinder");
            Assert.That(LevelSurfaces.IsBackdrop(new Vector3(600, 61, 600)), Is.False, "a merged town block keeps its shadow");
            Assert.That(LevelSurfaces.IsBackdrop(new Vector3(20, 30, 20)), Is.False);
            var see = new Material(Shader.Find("Universal Render Pipeline/Lit")) { renderQueue = 3000 };
            var solid = new Material(Shader.Find("Universal Render Pipeline/Lit")) { renderQueue = 2000 };
            try
            {
                Assert.That(LevelSurfaces.AllSeeThrough(new List<Material> { see }), Is.True);
                Assert.That(LevelSurfaces.AllSeeThrough(new List<Material> { see, solid }), Is.False);
                Assert.That(LevelSurfaces.AllSeeThrough(new List<Material>()), Is.False);
            }
            finally { Object.DestroyImmediate(see); Object.DestroyImmediate(solid); }
        }

        // ---------- decals

        [Test]
        public void Decals_RayBoxFindsTheFaceItEnters()
        {
            Assert.That(DecalMath.RayBox(new Vector3(0, 1, 5), Vector3.back, new Vector3(-1, 0, -1), new Vector3(1, 2, 1), out float t, out var n), Is.True);
            Assert.That(t, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(n, Is.EqualTo(Vector3.forward));
            Assert.That(DecalMath.RayBox(new Vector3(0, 5, 0), Vector3.down, new Vector3(-1, 0, -1), new Vector3(1, 2, 1), out t, out n), Is.True);
            Assert.That(n, Is.EqualTo(Vector3.up)); Assert.That(t, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(DecalMath.RayBox(new Vector3(5, 1, 5), Vector3.back, new Vector3(-1, 0, -1), new Vector3(1, 2, 1), out _, out _), Is.False, "misses beside it");
            Assert.That(DecalMath.RayBox(new Vector3(0, 1, 0), Vector3.back, new Vector3(-1, 0, -1), new Vector3(1, 2, 1), out _, out _), Is.False, "starts inside");
        }

        [Test]
        public void Decals_CastHitsWallsAndFloorsOfALayout()
        {
            var layout = ScriptableObject.CreateInstance<LevelLayout>();
            try
            {
                // a wall from x -2..2 at original z 0..0.2 (Unity z -0.2..0), 0..3 high; a floor at y 0 over x -5..5, z -5..5
                layout.solids.Add(new LevelLayout.SolidBox { x0 = -2, x1 = 2, y0 = 0, y1 = 3, z0 = 0, z1 = 0.2f });
                layout.walk.Add(new LevelLayout.WalkZone { x0 = -5, x1 = 5, z0 = -5, z1 = 5, y = 0 });
                Assert.That(DecalMath.Cast(layout, new Vector3(0, 1.5f, 2), Vector3.back, 8f, out var p, out var n), Is.True);
                Assert.That(p.z, Is.EqualTo(0f).Within(1e-4f)); Assert.That(n, Is.EqualTo(Vector3.forward));
                Assert.That(DecalMath.Cast(layout, new Vector3(3, 1, 3), Vector3.down, 8f, out p, out n), Is.True);
                Assert.That(p.y, Is.EqualTo(0f).Within(1e-4f)); Assert.That(n, Is.EqualTo(Vector3.up));
                Assert.That(DecalMath.Cast(layout, new Vector3(30, 1, 30), Vector3.down, 8f, out _, out _), Is.False, "off the floor");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void Decals_EveryPictureExistsAndNoticesAreFew()
        {
            foreach (var id in new[] { "apartment", "town", "shelter", "store", "mart", "office" })
            {
                int notices = 0;
                foreach (var s in DecalMath.For(id))
                {
                    if (s.Kind == "puddle") { Assert.That(File.Exists(Path.Combine(Application.dataPath, "_AiOn/Resources/Decals/puddle_mask.png"))); continue; }
                    Assert.That(File.Exists(Path.Combine(Application.dataPath, $"_AiOn/Resources/Decals/{s.Kind}.png")), $"{id}: {s.Kind}");
                    Assert.That(s.Opacity, Is.InRange(0.1f, 1f));
                    Assert.That(s.Dir.magnitude, Is.EqualTo(1f).Within(1e-4f));
                    if (s.Kind.StartsWith("notice_")) notices++;
                }
                Assert.That(notices, Is.LessThanOrEqualTo(4), $"{id}: restrained");
            }
        }

        // ---------- sky photos, people fill, fire

        [Test]
        public void Sky_PhotoPerTimeOfDayAndNoneAtNight()
        {
            Assert.That(SkyRig.PhotoFor("day", 0f), Is.EqualTo(0));
            Assert.That(SkyRig.PhotoFor("interior", 0f), Is.EqualTo(0));
            Assert.That(SkyRig.PhotoFor("afternoon", 0f), Is.EqualTo(1));
            Assert.That(SkyRig.PhotoFor("dusk", 0.35f), Is.EqualTo(2));
            Assert.That(SkyRig.PhotoFor("night", 1f), Is.EqualTo(-1));
            Assert.That(SkyRig.PhotoFor("blackout", 1f), Is.EqualTo(-1));
            Assert.That(SkyRig.PhotoFor("interiorDark", 0f), Is.EqualTo(0), "the powerless room by day still sees a day sky");
            foreach (var p in SkyRig.Photos)
                Assert.That(File.Exists(Path.Combine(Application.dataPath, "_AiOn/Resources/" + p.resource + ".jpg")), p.resource);
            // the photo's sun lands on the preset's sun azimuth
            float yaw = SkyRig.PhotoYaw(0.6f, 195f);
            Assert.That(Mathf.Repeat(195f / 360f + yaw, 1f), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(yaw, Is.InRange(0f, 1f));
        }

        [Test]
        public void PeopleFill_CapTakesOnlyWhatIsMissing()
        {
            Assert.That(PeopleFill.Cap(0f), Is.LessThanOrEqualTo(PeopleFill.MaxIntensity));
            Assert.That(PeopleFill.Cap(0.05f), Is.GreaterThan(0.3f), "a blackout corridor still lifts faces");
            Assert.That(PeopleFill.Cap(0.35f), Is.LessThan(0.1f), "a lit night street adds almost nothing");
            Assert.That(PeopleFill.Cap(2f), Is.GreaterThan(0f));
        }

        [Test]
        public void Fire_FlickerStaysInBandAndMoves()
        {
            float min = 9, max = -9;
            for (float t = 0; t < 20f; t += 0.01f) { float f = StoryFx.Flicker(t, 3f); min = Mathf.Min(min, f); max = Mathf.Max(max, f); }
            Assert.That(min, Is.GreaterThanOrEqualTo(0.6f)); Assert.That(max, Is.LessThanOrEqualTo(1.05f));
            Assert.That(max - min, Is.GreaterThan(0.15f), "it visibly flickers");
        }

        // ---------- review fixes: lit window set, shadows by tier, photo fades, decal depth, fire, guides, dark grade

        [Test]
        public void Window_OnlyTheCameraRoomsWindowsAreLit()
        {
            var wins = WindowLightMath.For("apartment");
            var pick = new List<int>();
            // 302's living room: the veranda glass only, not the corridor strip through the flat's wall or the stairwell
            WindowLightMath.Pick(wins, new Vector3(-3.7f, 7.35f, -5f), WindowLights.MaxLit, pick);
            Assert.That(pick, Is.Not.Empty);
            foreach (int i in pick) Assert.That(wins[i].Center.z, Is.EqualTo(-9.45f).Within(1e-3f), "veranda only, got " + wins[i].Center);
            // the 3F corridor: its own window strip, not the veranda or the stairwell landings behind its wall
            WindowLightMath.Pick(wins, new Vector3(2f, 7.3f, 1.0f), WindowLights.MaxLit, pick);
            Assert.That(pick, Is.Not.Empty);
            foreach (int i in pick) Assert.That(wins[i].Center.z, Is.EqualTo(2f).Within(1e-3f), "corridor strip only, got " + wins[i].Center);
            // the lobby (1F): nothing from upstairs
            WindowLightMath.Pick(wins, new Vector3(3.5f, 1.6f, 4f), WindowLights.MaxLit, pick);
            foreach (int i in pick) Assert.That(wins[i].Center.y, Is.LessThan(3.5f), "nothing from upstairs, got " + wins[i].Center);
            // no light reaches far past its room's far wall
            foreach (var w in wins) Assert.That(WindowLightMath.RangeFor(w), Is.LessThanOrEqualTo(WindowLightMath.SetBack + w.Depth + 1.01f));
        }

        [Test]
        public void Window_DeepRoomsGetMoreSkyLightAtNightOnly()
        {
            var wins = WindowLightMath.For("apartment");
            WindowOpening veranda = default, corridor = default;
            foreach (var w in wins) { if (w.Center.z < -9f) veranda = w; if (Mathf.Approximately(w.Center.z, 2f)) corridor = w; }
            Assert.That(WindowLightMath.DepthGain(veranda, 0f), Is.EqualTo(1f), "day unchanged");
            Assert.That(WindowLightMath.DepthGain(veranda, 1f), Is.GreaterThan(1.8f), "the living room behind the veranda");
            Assert.That(WindowLightMath.DepthGain(corridor, 1f), Is.LessThanOrEqualTo(1f), "the 2 m corridor was already lit");
            Assert.That(WindowLightMath.DepthGain(veranda, 1f), Is.LessThanOrEqualTo(2.2f));
        }

        [Test]
        public void Guides_ExitGlowSitsOnTheSign()
        {
            // 3F stair door: the guide over the point (5, 7.8, 0.7), the sign on the wall at (5, 7.85, 0.13)
            var signs = new List<Bounds> { new Bounds(new Vector3(5f, 7.85f, 0.13f), new Vector3(0.4f, 0.2f, 0f)), new Bounds(new Vector3(9f, 7.85f, 0.13f), new Vector3(0.4f, 0.2f, 0f)) };
            Assert.That(GuidingLights.SnapToSign(new Vector3(5f, 7.8f, 0.7f), new Vector3(5f, 5.6f, 0.7f), signs, out var at), Is.True);
            Assert.That(Vector3.Distance(at, signs[0].center), Is.LessThan(0.1f));
            Assert.That(at.z, Is.GreaterThan(0.13f), "on the corridor side of the sign");
            Assert.That(GuidingLights.SnapToSign(new Vector3(20f, 7.8f, 0.7f), Vector3.zero, signs, out var none), Is.False, "no sign near: stays");
            Assert.That(none, Is.EqualTo(new Vector3(20f, 7.8f, 0.7f)));
            Assert.That(GuidingLights.IsSign("lit:emergency~m10 (state)", new Vector3(0.4f, 0.2f, 0f)), Is.True);
            Assert.That(GuidingLights.IsSign("lit:emergency~m17 (state)", new Vector3(0.08f, 0.05f, 0.08f)), Is.False, "a pilot lamp, not a sign");
            Assert.That(GuidingLights.IsSign("m~m37", new Vector3(0.4f, 0.2f, 0f)), Is.False);
        }

        [Test]
        public void Fire_FlameTonguesAreFewAndColouredByTheirPicture()
        {
            Assert.That(FireFx.FlameRate, Is.LessThan(60f), "a few big tongues, not an additive pile of dots");
            Assert.That(GuidingLights.HaloGain(GuidingLights.Kind.Supply), Is.LessThan(GuidingLights.HaloGain(GuidingLights.Kind.Exit) * 0.5f), "a supply is a glint, not a lamp");
        }

        [Test]
        public void Shadows_ExtraLightsFollowTheTierAndTheMorningSunTheSunShadows()
        {
            Assert.That(StoryFx.FireShadows(1f, 1f, true), Is.True);
            Assert.That(StoryFx.FireShadows(1f, 1f, false), Is.False, "medium and below: no fire shadows");
            Assert.That(StoryFx.FireShadows(0.2f, 1f, true), Is.False, "a small fire casts none");
            foreach (var t in new[] { "ultralow", "low", "medium" }) Assert.That(AiOn.Settings.QualityTiers.KnobsOf(t).FlashShadows, Is.False, t);
            var lit = new EnvPreset { Name = "interiorDark" }; EnvironmentController.DaylightVariant(lit, true);
            var flat = new EnvPreset { Name = "interiorDark" }; EnvironmentController.DaylightVariant(flat, false);
            Assert.That(lit.KeyIntensity, Is.EqualTo(EnvironmentController.MorningSun));
            Assert.That(flat.KeyIntensity, Is.LessThanOrEqualTo(0.6f), "no sun shadows (ultralow): the sun would shine through every wall");
            Assert.That(flat.FillIntensity, Is.GreaterThan(lit.FillIntensity), "the windows' fill carries the room instead");
        }

        [Test]
        public void Sky_PhotoFadeTurnsBackAndHandsOver()
        {
            var b = PhotoBlend.None;
            b.Want(0); b.Snap();
            Assert.That(b.A, Is.EqualTo(0)); Assert.That(b.Weight, Is.EqualTo(1f));
            // day -> afternoon -> day before the fade ends: the fade turns round and ends on day
            b.Want(1); b.Step(0.4f);
            Assert.That(b.B, Is.EqualTo(1)); Assert.That(b.Mix, Is.EqualTo(0.4f).Within(1e-5f));
            b.Want(0);
            Assert.That(b.Mix, Is.EqualTo(0.6f).Within(1e-5f), "same picture on screen (no jump)");
            for (int i = 0; i < 10; i++) b.Step(0.2f);
            Assert.That(b.A, Is.EqualTo(0)); Assert.That(b.B, Is.EqualTo(-1)); Assert.That(b.Showing, Is.EqualTo(0));
            // day -> afternoon -> dusk mid-fade: dusk ends on screen
            b.Want(1); b.Step(0.7f); b.Want(2);
            Assert.That(b.A, Is.EqualTo(1), "from the photo that showed more"); Assert.That(b.B, Is.EqualTo(2));
            for (int i = 0; i < 10; i++) b.Step(0.2f);
            Assert.That(b.Showing, Is.EqualTo(2)); Assert.That(b.Target, Is.EqualTo(2));
            // night: the photo fades out; back to day straight from the dark: no cross-fade from dusk
            b.Want(-1); for (int i = 0; i < 10; i++) b.Step(0.2f);
            Assert.That(b.Showing, Is.EqualTo(-1));
            b.Want(0); Assert.That(b.A, Is.EqualTo(0)); Assert.That(b.B, Is.EqualTo(-1));
        }

        [Test]
        public void Decals_ThinOnFloorsSoPeopleStayClean()
        {
            Assert.That(LevelDecals.DepthFor(Vector3.up), Is.LessThanOrEqualTo(0.15f), "a seated person or a shoe stays out of a floor stain");
            Assert.That(LevelDecals.DepthFor(Vector3.forward), Is.LessThanOrEqualTo(0.3f));
            Assert.That(LevelDecals.DepthFor(Vector3.forward), Is.GreaterThan(LevelDecals.DepthFor(Vector3.up)), "walls keep a little tolerance");
        }

        [Test]
        public void Fire_LightSitsInTheFlamesAboveWhatBurns()
        {
            Assert.That(StoryFx.LightHeight(1.2f, 0.66f), Is.GreaterThan(0.66f + 0.5f), "well above the boxes' top face");
            Assert.That(StoryFx.LightHeight(1f, 0f), Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Guides_DayIsDayAndExitsCarryEmergencyLampsInTheDark()
        {
            Assert.That(GuidingLights.Visibility(0f, false, false, "interiorDark", daylightInside: true), Is.EqualTo(0.3f).Within(1e-5f), "the morning is daylight");
            Assert.That(GuidingLights.Visibility(1f, false, false, "interiorDark"), Is.EqualTo(1f));
            var (c, i, r) = GuidingLights.ExitLight(true);
            Color.RGBToHSV(c, out _, out float s, out _);
            Assert.That(s, Is.LessThan(0.15f), "an emergency lamp is near white, not green");
            Assert.That(i, Is.GreaterThan(GuidingLights.IntensityOf(GuidingLights.Kind.Exit) * 3f));
            Assert.That(r, Is.GreaterThan(GuidingLights.RangeOf(GuidingLights.Kind.Exit)));
            Assert.That(GuidingLights.HasEmergencyLamp("stairs3F"), Is.True);
            Assert.That(GuidingLights.HasEmergencyLamp("homeExitDoor"), Is.False, "not inside the flat");
            Assert.That(GuidingLights.HeightOf(GuidingLights.Kind.Exit), Is.GreaterThan(2f), "at the sign over the door");
            Assert.That(GuidingLights.HaloSize(GuidingLights.Kind.Exit), Is.LessThan(0.4f));
        }

        [Test]
        public void Env_DarkInteriorIsBlueGreyNotBlue()
        {
            var e = new EnvPreset { Name = "interiorDark", KeyColor = new Color(0.62f, 0.71f, 0.92f), Tint = new Color(0.85f, 0.92f, 1.1f), TintAmount = 0.3f, Saturation = 0.7f, FillIntensity = 0.12f, KeyIntensity = 0.5f };
            EnvPreset.Tune(e);
            Color.RGBToHSV(e.KeyColor, out _, out float ks, out _);
            Color.RGBToHSV(e.SkyFill, out _, out float fs, out _);
            Assert.That(ks, Is.LessThanOrEqualTo(0.2001f)); Assert.That(fs, Is.LessThan(0.3f));
            Assert.That(e.Saturation, Is.GreaterThanOrEqualTo(0.85f), "reds stay red in the dark");
            Color.RGBToHSV(WindowLightMath.MoonColor, out _, out float ms, out _);
            Assert.That(ms, Is.LessThan(0.2f));
        }
        [Test]
        public void Surfaces_WhiteboardMatteAndFoilSoft()
        {
            Assert.That(LevelSurfaces.FinishFor("office", "m~m61", out float r, out _), Is.True);
            Assert.That(r, Is.GreaterThanOrEqualTo(0.6f), "a matte board: no glare over the schedule");
            Assert.That(LevelSurfaces.FinishFor("shelter", "m~m77 (state)", out float fr, out float fn), Is.True);
            Assert.That(fr, Is.GreaterThanOrEqualTo(0.45f)); Assert.That(fn, Is.LessThanOrEqualTo(0.5f));
            Assert.That(LevelSurfaces.FinishFor("office", "m~m7", out _, out _), Is.False);
        }

        [Test]
        public void PeopleFill_BootLeavesNoLightBehindPlayMode()
        {
            // a DontSave fill outlived every play session in the Editor and they summed to a white glow on people
            var src = File.ReadAllText(Path.Combine(Application.dataPath, "_AiOn/Scripts/Rendering/PeopleFill.cs"));
            int boot = src.IndexOf("static void Boot()", System.StringComparison.Ordinal);
            Assert.That(boot, Is.GreaterThan(0));
            var body = src.Substring(boot, src.IndexOf("Awake()", boot, System.StringComparison.Ordinal) - boot);
            Assert.That(body, Does.Not.Contain("HideFlags.DontSave"));
        }
    }
}
