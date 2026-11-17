using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AiOn.Levels
{
    /// <summary>One time-of-day look (original engine/presets.js): sun, fill, fog, exposure, bloom, grade and lit groups.</summary>
    public sealed class EnvPreset
    {
        public string Name;
        public bool Outdoor;
        public Vector2 Sun, Key;          // [azimuth from north clockwise, elevation] in degrees
        public Color KeyColor = Color.white;
        public float KeyIntensity = 1;
        public Color SkyFill = Color.white, GroundFill = Color.gray;
        public float FillIntensity = 0.5f;
        public Color FogColor = Color.gray;
        public float FogDensity;
        public float Exposure = 1;
        public float BloomStrength, BloomRadius, BloomThreshold = 1;
        public float Saturation = 1, Contrast = 1, GradeExposure, Vignette, Grain, TintAmount;
        public Color Tint = Color.white;
        public float NightAmount;
        public Color Zenith = Color.black, Horizon = Color.black;
        // Sky rig (engine/sky.js, presets.js envState). Colours are sRGB as parsed; SkyRig converts to linear.
        public bool NoSky;
        public float Turbidity = 2, Rayleigh = 1, Mie = 0.005f, MieG = 0.8f, SkyGain = 1;
        public float Glow, Stars, Moon;
        public Color GlowColor = new Color(1f, 0.604f, 0.333f);
        public float CloudCover = 0.3f, CloudOpacity, CloudGain = 1;
        public Color CloudLit = Color.white, CloudShade = Color.gray;
        public Color Hills = Color.gray;
        public Dictionary<string, float> Lit = new();

        public static Dictionary<string, EnvPreset> ParseAll(string json)
        {
            var all = new Dictionary<string, EnvPreset>();
            foreach (var p in JObject.Parse(json).Properties()) all[p.Name] = Parse(p.Name, (JObject)p.Value);
            return all;
        }

        static EnvPreset Parse(string name, JObject o)
        {
            var e = new EnvPreset { Name = name, Outdoor = (bool?)o["outdoor"] ?? false };
            e.Sun = V2(o["sun"]); e.Key = V2(o["key"]);
            e.KeyColor = Hex((string)o["keyColor"]); e.KeyIntensity = (float?)o["keyI"] ?? 1;
            if (o["hemi"] is JArray h) { e.SkyFill = Hex((string)h[0]); e.GroundFill = Hex((string)h[1]); e.FillIntensity = (float)h[2]; }
            if (o["fog"] is JArray f) { e.FogColor = Hex((string)f[0]); e.FogDensity = (float)f[1]; }
            e.Exposure = (float?)o["exposure"] ?? 1;
            if (o["bloom"] is JArray b) { e.BloomStrength = (float)b[0]; e.BloomRadius = (float)b[1]; e.BloomThreshold = (float)b[2]; }
            if (o["grade"] is JObject g)
            {
                e.Saturation = (float?)g["saturation"] ?? 1; e.Contrast = (float?)g["contrast"] ?? 1; e.GradeExposure = (float?)g["exposure"] ?? 0;
                e.Vignette = (float?)g["vignette"] ?? 0; e.Grain = (float?)g["grain"] ?? 0; e.TintAmount = (float?)g["tintAmount"] ?? 0;
                if (g["tint"] is JArray t) e.Tint = new Color((float)t[0], (float)t[1], (float)t[2]);
            }
            if (o["night"] is JObject n)
            {
                e.NightAmount = (float?)n["amount"] ?? 0; e.Zenith = Hex((string)n["zenith"]); e.Horizon = Hex((string)n["horizon"]);
                e.Glow = (float?)n["glow"] ?? 0; e.Stars = (float?)n["stars"] ?? 0; e.Moon = (float?)n["moon"] ?? 0;
                if (n["glowColor"] != null) e.GlowColor = Hex((string)n["glowColor"]);
            }
            e.NoSky = (bool?)o["noSky"] ?? false;
            if (o["sky"] is JObject sk)
            {
                e.Turbidity = (float?)sk["turbidity"] ?? 2; e.Rayleigh = (float?)sk["rayleigh"] ?? 1; e.Mie = (float?)sk["mie"] ?? 0.005f;
                e.MieG = (float?)sk["mieG"] ?? 0.8f; e.SkyGain = (float?)sk["gain"] ?? 1;
            }
            if (o["clouds"] is JObject cl)
            {
                e.CloudCover = (float?)cl["cover"] ?? 0.3f; e.CloudOpacity = (float?)cl["opacity"] ?? 0; e.CloudGain = (float?)cl["gain"] ?? 1;
                if (cl["lit"] != null) e.CloudLit = Hex((string)cl["lit"]);
                if (cl["shade"] != null) e.CloudShade = Hex((string)cl["shade"]);
            }
            if (o["hills"] != null) e.Hills = Hex((string)o["hills"]);
            if (o["lit"] is JObject l) foreach (var p in l.Properties()) e.Lit[p.Name] = (float)p.Value;
            return e;
        }

        static Vector2 V2(JToken t) => t is JArray a ? new Vector2((float)a[0], (float)a[1]) : Vector2.zero;
        static Color Hex(string s) => s != null && ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.white;

        /// <summary>Unity direction towards the sun/key light for [azimuth, elevation] (north = +z, east = +x).</summary>
        public static Vector3 DirectionTo(Vector2 azEl)
        {
            float az = azEl.x * Mathf.Deg2Rad, el = azEl.y * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }
    }

    /// <summary>
    /// Applies an environment preset to the scene: the key (sun/moon) light, ambient fill, fog, sky, the indoor lamps
    /// and URP post-processing (ACES tone mapping, exposure, bloom, colour grade, vignette, grain). The presets are the
    /// original's (Data/Source/env-presets.json); the look is tuned here for URP. SetPower(false) is the blackout.
    /// </summary>
    public class EnvironmentController : MonoBehaviour
    {
        public TextAsset presetsJson;
        public string preset = "interior";
        public Light keyLight;
        public Material skyMaterial;
        [Tooltip("Lamps that follow the 'indoor' lit group and the grid power.")]
        public List<Light> indoorLamps = new();
        public bool power = true;

        [Header("URP tuning (original values are multiplied by these)")]
        public float keyScale = 0.42f;
        public float fillScale = 1.6f;
        public float exposureOffset = 0.6f;
        public float lampIntensity = 1f;

        Dictionary<string, EnvPreset> _all;
        SkyRig _skyRig;
        Volume _volume;
        Bloom _bloom; ColorAdjustments _color; Vignette _vignette; FilmGrain _grain; Tonemapping _tone;
        Volume _smokeVolume;
        float _smokeFogBase = -1;

        /// <summary>The smoke escape's breathed-in smoke, 0..1 (original #world.sim-smoke --smoke); the HUD shows the crouch hint while it is up.</summary>
        public static float SmokeLevel { get; private set; }
        /// <summary>True from the smoke escape's first grade until it clears the grade (the original's sim-smoke class).</summary>
        public static bool SmokeActive { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SmokeLevel = 0; SmokeActive = false; }
        readonly List<float> _lampBase = new();
        Dictionary<string, float> _lit = new();

        /// <summary>Current lit level per group (core.js litNow): the preset's lit table with grid power applied.</summary>
        public IReadOnlyDictionary<string, float> LitLevels => _lit;

        public EnvPreset Current { get; private set; }
        public static EnvironmentController Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            // SmokeLevel/SmokeActive carry over a scene load (a trip home for a towel reloads the apartment mid-escape):
            // Apply below puts the smoke back on the new level's volume and fog.
            _all = presetsJson ? EnvPreset.ParseAll(presetsJson.text) : new Dictionary<string, EnvPreset>();
            foreach (var l in indoorLamps) _lampBase.Add(l ? l.intensity : 0);
            BuildVolume();
            Apply(preset);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void BuildVolume()
        {
            var go = new GameObject("PostProcessing");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.profile = profile;
            _tone = profile.Add<Tonemapping>(true); _tone.mode.Override(TonemappingMode.ACES);
            _bloom = profile.Add<Bloom>(true);
            _color = profile.Add<ColorAdjustments>(true);
            _vignette = profile.Add<Vignette>(true);
            _grain = profile.Add<FilmGrain>(true);

            // The smoke escape's look at full smoke, blended in by the volume's weight (SetSmoke). The original was a CSS
            // filter: blur(5px) brightness(0.6) sepia(0.35); here a Gaussian depth-of-field blur that eats the far view
            // first, the darker, warmer, greyer grade, and a narrowing vignette (plus thicker fog, see SetSmoke).
            var sgo = new GameObject("SmokeGrade");
            sgo.transform.SetParent(transform, false);
            _smokeVolume = sgo.AddComponent<Volume>();
            _smokeVolume.isGlobal = true;
            _smokeVolume.priority = 11;
            _smokeVolume.weight = 0;
            var sp = ScriptableObject.CreateInstance<VolumeProfile>();
            _smokeVolume.profile = sp;
            var dof = sp.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0.4f);
            dof.gaussianEnd.Override(3.5f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(true);
            var sc = sp.Add<ColorAdjustments>(true);
            sc.postExposure.Override(-0.9f);
            sc.saturation.Override(-35f);
            sc.contrast.Override(-12f);
            sc.colorFilter.Override(new Color(1f, 0.86f, 0.68f));
            var sv = sp.Add<Vignette>(true);
            sv.intensity.Override(0.5f);
            sv.smoothness.Override(0.6f);
            sv.color.Override(new Color(0.08f, 0.06f, 0.04f));
        }

        /// <summary>
        /// The smoke escape's view, 0 (clear) .. 1 (Strain smoke level at its worst): blur, darker sepia grade, vignette and a
        /// thick grey-brown fog over the preset's own. 0 takes it all off and puts the preset's fog back.
        /// </summary>
        public void SetSmoke(float level)
        {
            level = Mathf.Clamp01(level);
            SmokeLevel = level;
            if (_smokeVolume) _smokeVolume.weight = level;
            if (level <= 0)
            {
                if (_smokeFogBase >= 0 && Current != null)
                {
                    RenderSettings.fog = Current.FogDensity > 0;
                    RenderSettings.fogColor = Current.FogColor;
                    RenderSettings.fogDensity = _smokeFogBase;
                }
                _smokeFogBase = -1;
                return;
            }
            if (_smokeFogBase < 0) _smokeFogBase = RenderSettings.fog ? RenderSettings.fogDensity : 0;
            var baseColor = Current != null ? Current.FogColor : Color.gray;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(baseColor, new Color(0.16f, 0.14f, 0.12f), Mathf.Clamp01(level * 1.5f));
            // Even a little smoke is visible down a corridor; at full smoke the far end of it (~12 m) is gone.
            RenderSettings.fogDensity = Mathf.Max(_smokeFogBase, Mathf.Lerp(0.03f, 0.16f, level));
        }

        public bool Has(string name) => _all != null && _all.ContainsKey(name);

        public void Apply(string name)
        {
            if (_all == null || !_all.TryGetValue(name, out var e)) { Debug.LogWarning($"[Env] unknown preset '{name}'"); return; }
            preset = name; Current = e;

            if (keyLight)
            {
                keyLight.transform.rotation = Quaternion.LookRotation(-EnvPreset.DirectionTo(e.Key), Vector3.up);
                keyLight.color = e.KeyColor;
                keyLight.intensity = e.KeyIntensity * keyScale;
                keyLight.shadows = LightShadows.Soft;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = e.SkyFill * (e.FillIntensity * fillScale);
            RenderSettings.ambientGroundColor = e.GroundFill * (e.FillIntensity * fillScale);
            RenderSettings.ambientEquatorColor = Color.Lerp(e.SkyFill, e.GroundFill, 0.5f) * (e.FillIntensity * fillScale);

            RenderSettings.fog = e.FogDensity > 0;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = e.FogColor;
            RenderSettings.fogDensity = e.FogDensity * (e.Outdoor ? 1f : 0.5f);

            // The sky rig (AiOn/Sky skybox + distant city and hills); the procedural skyMaterial is only the fallback.
            if (!_skyRig) { _skyRig = GetComponent<SkyRig>(); if (!_skyRig) _skyRig = gameObject.AddComponent<SkyRig>(); }
            bool rigSky = _skyRig.Apply(e, power);
            if (rigSky) RenderSettings.sun = keyLight;
            else if (skyMaterial)
            {
                RenderSettings.skybox = skyMaterial;
                RenderSettings.sun = keyLight;
                float night = e.NightAmount;
                skyMaterial.SetFloat("_Exposure", Mathf.Lerp(1.15f, 0.12f, night));
                skyMaterial.SetColor("_SkyTint", Color.Lerp(new Color(0.42f, 0.52f, 0.68f), e.Zenith * 4f, night));
                skyMaterial.SetColor("_GroundColor", Color.Lerp(new Color(0.42f, 0.42f, 0.4f), e.Horizon, night));
                skyMaterial.SetFloat("_AtmosphereThickness", Mathf.Lerp(e.Sun.y < 12 ? 1.6f : 1.0f, 0.6f, night));
                skyMaterial.SetFloat("_SunSize", night > 0.5f ? 0.02f : 0.04f);
            }

            _color.postExposure.Override(Mathf.Log(Mathf.Max(0.05f, e.Exposure), 2f) + e.GradeExposure + exposureOffset);
            _color.saturation.Override((e.Saturation - 1f) * 100f);
            _color.contrast.Override((e.Contrast - 1f) * 100f);
            _color.colorFilter.Override(Color.Lerp(Color.white, e.Tint, Mathf.Clamp01(e.TintAmount * 1.5f)));
            _bloom.intensity.Override(e.BloomStrength * 1.6f);
            _bloom.threshold.Override(Mathf.Max(0.6f, e.BloomThreshold));
            _bloom.scatter.Override(Mathf.Clamp01(0.45f + e.BloomRadius * 0.5f));
            _vignette.intensity.Override(e.Vignette * 0.8f);
            _vignette.smoothness.Override(0.45f);
            _grain.type.Override(FilmGrainLookup.Thin1);
            _grain.intensity.Override(Mathf.Clamp01(e.Grain * 6f));

            ApplyLamps();
            UpdateLit();
            if (SmokeLevel > 0) { _smokeFogBase = -1; SetSmoke(SmokeLevel); } // a preset change mid-escape keeps the smoke
        }

        /// <summary>Recomputes the lit levels from the current preset and power and hands them to the level's materials.</summary>
        void UpdateLit()
        {
            if (Current == null) return;
            _lit = LevelMaterials.MergeLit(_lit, Current.Lit, power);
            PushLit();
        }

        /// <summary>Hands the current lit levels to the level's materials again (after a scene load).</summary>
        public void PushLit()
        {
            var mats = LevelMaterials.Instance;
            if (mats != null) mats.SetLit(_lit);
        }

        /// <summary>
        /// A temporary grade over the preset (original engine.post.setGrade): saturation (1 = unchanged), exposure (stops),
        /// tint [r, g, b] with tintAmount, vignette. null puts the preset's own grade back.
        /// </summary>
        public void SetGradeOverride(JObject grade)
        {
            if (grade == null) { SmokeActive = false; SetSmoke(0); if (Current != null) Apply(Current.Name); return; }
            if (grade["smoke"] != null) { SmokeActive = true; SetSmoke((float)grade["smoke"]); }
            if (grade["saturation"] != null) _color.saturation.Override(((float)grade["saturation"] - 1f) * 100f);
            if (grade["exposure"] != null) _color.postExposure.Override(Mathf.Log(Mathf.Max(0.05f, Current?.Exposure ?? 1f), 2f) + (float)grade["exposure"] + exposureOffset);
            if (grade["vignette"] != null) _vignette.intensity.Override((float)grade["vignette"] * 0.8f);
            if (grade["tint"] is JArray t)
                _color.colorFilter.Override(Color.Lerp(Color.white, new Color((float)t[0], (float)t[1], (float)t[2]), Mathf.Clamp01(((float?)grade["tintAmount"] ?? 0.3f) * 1.5f)));
        }

        /// <summary>Grid power: off is the blackout (lamps out; emergency lighting is a later pass).</summary>
        public void SetPower(bool on)
        {
            power = on;
            ApplyLamps();
            UpdateLit(); // grid groups (street, windows, shops, signs) of the level's materials
            if (_skyRig && Current != null) _skyRig.Apply(Current, power); // grid groups: distant city windows + beacons
        }

        void ApplyLamps()
        {
            float indoor = Current != null && Current.Lit.TryGetValue("indoor", out var v) ? v : 1f;
            float k = power ? indoor * lampIntensity : 0f;
            for (int i = 0; i < indoorLamps.Count; i++)
                if (indoorLamps[i]) { indoorLamps[i].intensity = _lampBase[i] * k; indoorLamps[i].enabled = k > 0.001f; }
        }
    }
}
