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
            foreach (var p in JObject.Parse(json).Properties()) { var e = Parse(p.Name, (JObject)p.Value); Tune(e); all[p.Name] = e; }
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

        /// <summary>Copies every value of o into this preset (the lit table too, without reallocating it).</summary>
        public void CopyFrom(EnvPreset o)
        {
            Name = o.Name; Outdoor = o.Outdoor; Sun = o.Sun; Key = o.Key; KeyColor = o.KeyColor; KeyIntensity = o.KeyIntensity;
            SkyFill = o.SkyFill; GroundFill = o.GroundFill; FillIntensity = o.FillIntensity; FogColor = o.FogColor; FogDensity = o.FogDensity;
            Exposure = o.Exposure; BloomStrength = o.BloomStrength; BloomRadius = o.BloomRadius; BloomThreshold = o.BloomThreshold;
            Saturation = o.Saturation; Contrast = o.Contrast; GradeExposure = o.GradeExposure; Vignette = o.Vignette; Grain = o.Grain;
            TintAmount = o.TintAmount; Tint = o.Tint; NightAmount = o.NightAmount; Zenith = o.Zenith; Horizon = o.Horizon;
            NoSky = o.NoSky; Turbidity = o.Turbidity; Rayleigh = o.Rayleigh; Mie = o.Mie; MieG = o.MieG; SkyGain = o.SkyGain;
            Glow = o.Glow; Stars = o.Stars; Moon = o.Moon; GlowColor = o.GlowColor; CloudCover = o.CloudCover; CloudOpacity = o.CloudOpacity;
            CloudGain = o.CloudGain; CloudLit = o.CloudLit; CloudShade = o.CloudShade; Hills = o.Hills;
            if (!ReferenceEquals(Lit, o.Lit)) { Lit.Clear(); foreach (var kv in o.Lit) Lit[kv.Key] = kv.Value; }
        }

        /// <summary>core.js ease: smoothstep.</summary>
        public static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }

        /// <summary>
        /// core.js mixState: every number and colour of a and b mixed at t (already eased) into dst (no allocation once the
        /// lit table has its keys). Directions turn the short way round; the name, outdoor and sky flags switch at the half.
        /// A lit group only one side names keeps that side's value.
        /// </summary>
        public static void LerpInto(EnvPreset dst, EnvPreset a, EnvPreset b, float t)
        {
            t = Mathf.Clamp01(t);
            var near = t < 0.5f ? a : b;
            dst.Name = near.Name; dst.Outdoor = near.Outdoor; dst.NoSky = near.NoSky;
            dst.Sun = new Vector2(Mathf.LerpAngle(a.Sun.x, b.Sun.x, t), Mathf.Lerp(a.Sun.y, b.Sun.y, t));
            dst.Key = new Vector2(Mathf.LerpAngle(a.Key.x, b.Key.x, t), Mathf.Lerp(a.Key.y, b.Key.y, t));
            dst.KeyColor = Color.Lerp(a.KeyColor, b.KeyColor, t); dst.KeyIntensity = Mathf.Lerp(a.KeyIntensity, b.KeyIntensity, t);
            dst.SkyFill = Color.Lerp(a.SkyFill, b.SkyFill, t); dst.GroundFill = Color.Lerp(a.GroundFill, b.GroundFill, t);
            dst.FillIntensity = Mathf.Lerp(a.FillIntensity, b.FillIntensity, t);
            dst.FogColor = Color.Lerp(a.FogColor, b.FogColor, t); dst.FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t);
            dst.Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t);
            dst.BloomStrength = Mathf.Lerp(a.BloomStrength, b.BloomStrength, t); dst.BloomRadius = Mathf.Lerp(a.BloomRadius, b.BloomRadius, t);
            dst.BloomThreshold = Mathf.Lerp(a.BloomThreshold, b.BloomThreshold, t);
            dst.Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t); dst.Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t);
            dst.GradeExposure = Mathf.Lerp(a.GradeExposure, b.GradeExposure, t); dst.Vignette = Mathf.Lerp(a.Vignette, b.Vignette, t);
            dst.Grain = Mathf.Lerp(a.Grain, b.Grain, t);
            // the tint as the mixed-in colour (tint at tintAmount), so a tint fades in rather than jumping hue
            var ta = Color.Lerp(Color.white, a.Tint, Mathf.Clamp01(a.TintAmount)); var tb = Color.Lerp(Color.white, b.Tint, Mathf.Clamp01(b.TintAmount));
            dst.Tint = Color.Lerp(ta, tb, t); dst.TintAmount = 1f;
            dst.NightAmount = Mathf.Lerp(a.NightAmount, b.NightAmount, t);
            dst.Zenith = Color.Lerp(a.Zenith, b.Zenith, t); dst.Horizon = Color.Lerp(a.Horizon, b.Horizon, t);
            dst.Turbidity = Mathf.Lerp(a.Turbidity, b.Turbidity, t); dst.Rayleigh = Mathf.Lerp(a.Rayleigh, b.Rayleigh, t);
            dst.Mie = Mathf.Lerp(a.Mie, b.Mie, t); dst.MieG = Mathf.Lerp(a.MieG, b.MieG, t); dst.SkyGain = Mathf.Lerp(a.SkyGain, b.SkyGain, t);
            dst.Glow = Mathf.Lerp(a.Glow, b.Glow, t); dst.Stars = Mathf.Lerp(a.Stars, b.Stars, t); dst.Moon = Mathf.Lerp(a.Moon, b.Moon, t);
            dst.GlowColor = Color.Lerp(a.GlowColor, b.GlowColor, t);
            dst.CloudCover = Mathf.Lerp(a.CloudCover, b.CloudCover, t); dst.CloudOpacity = Mathf.Lerp(a.CloudOpacity, b.CloudOpacity, t);
            dst.CloudGain = Mathf.Lerp(a.CloudGain, b.CloudGain, t);
            dst.CloudLit = Color.Lerp(a.CloudLit, b.CloudLit, t); dst.CloudShade = Color.Lerp(a.CloudShade, b.CloudShade, t);
            dst.Hills = Color.Lerp(a.Hills, b.Hills, t);
            foreach (var kv in a.Lit) dst.Lit[kv.Key] = b.Lit.TryGetValue(kv.Key, out var bv) ? Mathf.Lerp(kv.Value, bv, t) : kv.Value;
            foreach (var kv in b.Lit) if (!a.Lit.ContainsKey(kv.Key)) dst.Lit[kv.Key] = kv.Value;
        }

        /// <summary>A colour with its HSV saturation capped at max (value and hue kept).</summary>
        public static Color CapSaturation(Color c, float max)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s <= max) return c;
            var o = Color.HSVToRGB(h, max, v);
            o.a = c.a;
            return o;
        }

        /// <summary>
        /// The URP look on top of the original numbers (applied once per preset at load): night and dusk ambient 40 % lower
        /// so lamps and windows carry the street (the three.js hemi lit the ground almost as evenly as day); a dusk without
        /// the magenta wash (tint, sky fill and fog capped in saturation); a darker, cooler blackout (less ambient and moon);
        /// interiors half a stop down with a higher bloom threshold (blown office ceilings).
        /// </summary>
        public static void Tune(EnvPreset e)
        {
            if (e.Outdoor && e.NightAmount > 0) e.FillIntensity *= 0.6f;
            if (e.Name == "dusk")
            {
                e.Tint = CapSaturation(e.Tint, 0.06f);
                e.SkyFill = CapSaturation(e.SkyFill, 0.22f);
                e.FogColor = CapSaturation(e.FogColor, 0.16f);
                e.Hills = CapSaturation(e.Hills, 0.2f);
            }
            if (e.Name == "blackout")
            {
                e.FillIntensity *= 0.6f;
                e.KeyIntensity *= 0.75f;
                e.Exposure *= 0.85f;
            }
            if (e.Name == "interiorDark") e.FillIntensity *= 0.7f;
            if (!e.Outdoor && e.Name.StartsWith("interior", System.StringComparison.Ordinal) && e.Name != "interiorDark")
            {
                if (e.Name == "interior") e.Exposure *= 0.71f; // -0.5 EV
                e.BloomThreshold = Mathf.Max(1.1f, e.BloomThreshold);
            }
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

        /// <summary>The preset asked for last (a blend is heading here).</summary>
        public EnvPreset Current { get; private set; }
        /// <summary>The look on screen now (mid-blend: the mix).</summary>
        public EnvPreset Shown => _shown;
        /// <summary>True while a timed blend runs.</summary>
        public bool Blending => _blendDur > 0 && _blendK < 1;

        readonly EnvPreset _shown = new EnvPreset(), _from = new EnvPreset();
        EnvPreset _to;
        float _blendK = 1, _blendDur;
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
            AiOn.Rendering.AtmosphereDirector.Ensure(); // the cold base look, emergency grade and screen effects over this
            if (!GetComponent<AiOn.Rendering.GuidingLights>()) gameObject.AddComponent<AiOn.Rendering.GuidingLights>(); // exits, shelters, supplies
            if (!TryGetComponent(out _reflections)) _reflections = gameObject.AddComponent<AiOn.Rendering.LevelReflections>(); // live reflections
        }

        float _satBias;
        AiOn.Rendering.LevelReflections _reflections;

        /// <summary>Saturation points added over every preset's grade (the base look's cold desaturation).</summary>
        public void SetSaturationBias(float points)
        {
            if (Mathf.Approximately(points, _satBias)) return;
            _satBias = points;
            if (Current != null && _color) { _color.saturation.Override((_shown.Saturation - 1f) * 100f + _satBias); ApplyGradeOverride(); }
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

        public void Apply(string name) => BlendTo(name, 0);

        /// <summary>
        /// Eases from what is shown now to the preset over seconds (core.js setEnv/stepEnv: smoothstep, from the mixed state
        /// when a blend is already under way); 0 snaps. Key and fill lights, fog, sky, grade and the lit groups all follow.
        /// </summary>
        public void BlendTo(string name, float seconds) => BlendTo(name, seconds, false);

        void BlendTo(string name, float seconds, bool keepGrade)
        {
            if (_all == null || !_all.TryGetValue(name, out var e)) { Debug.LogWarning($"[Env] unknown preset '{name}'"); return; }
            if (name == "interiorDark" && DaylightOutside) e = DarkByDay(e);
            bool first = Current == null;
            preset = name; Current = e;
            // core.js setEnv sets the preset's whole grade: a story grade made before it is dropped (one made after it,
            // like a cutscene shot's exposure, then holds over the whole blend)
            if (!keepGrade) { _gSat = _gExp = _gVig = null; _gTint = null; }
            if (first || seconds <= 0.01f)
            {
                _blendDur = 0; _blendK = 1; _to = e;
                _shown.CopyFrom(e);
                ApplyShown();
                return;
            }
            _from.CopyFrom(_shown);
            _to = e; _blendDur = seconds; _blendK = 0;
        }

        /// <summary>The window view shows daylight (the level state windowView is 'day').</summary>
        public static bool DaylightOutside
        {
            get
            {
                var lm = LevelMaterials.Instance;
                return lm != null && lm.StateOf("windowView") == "day";
            }
        }

        EnvPreset _darkDay;

        /// <summary>
        /// A blackout by day indoors: interiorDark lit from the windows instead of by the moon (a neutral daylight key at
        /// 0.6 and a cool sky fill), so a daytime room without power reads as grey daylight, not night. The key barely
        /// reaches past the walls, so the window daylight that spills into the rooms is the fill (DaylightSpillFill): bright
        /// enough that the morning's objectives (fridge, veranda shelf, radio) read without the flashlight.
        /// </summary>
        EnvPreset DarkByDay(EnvPreset dark)
        {
            if (_darkDay != null) return _darkDay;
            _darkDay = new EnvPreset();
            _darkDay.CopyFrom(dark);
            DaylightVariant(_darkDay);
            return _darkDay;
        }

        /// <summary>interiorDark turned into its daylight variant (pure; see DarkByDay).</summary>
        /// <summary>The ambient fill of a powerless room by day (window spill; was 0.16, which left the 3장 morning near black).</summary>
        public const float DaylightSpillFill = 0.36f;

        public static void DaylightVariant(EnvPreset e)
        {
            e.KeyColor = new Color(0.91f, 0.93f, 0.96f);
            e.KeyIntensity = 0.6f; // the window key
            e.SkyFill = new Color(0.55f, 0.62f, 0.72f); e.GroundFill = new Color(0.22f, 0.21f, 0.2f);
            e.FillIntensity = DaylightSpillFill;
            e.FogColor = new Color(0.28f, 0.3f, 0.33f);
            e.Tint = new Color(0.95f, 0.97f, 1.02f); e.TintAmount = 0.2f;
            e.Saturation = 0.8f; e.Exposure = 1.1f;
            e.NightAmount = 0f; e.Stars = 0f; e.Moon = 0f;
            e.Zenith = new Color(0.55f, 0.65f, 0.78f); e.Horizon = new Color(0.78f, 0.82f, 0.86f);
        }

        /// <summary>The window view changed (LevelMaterials): the dark interior follows day or night outside.</summary>
        public void OutsideChanged()
        {
            if (Current != null && preset == "interiorDark") BlendTo("interiorDark", 0.6f, true);
        }

        void Update()
        {
            if (!Blending || _to == null) return;
            _blendK = Mathf.Min(1f, _blendK + Time.unscaledDeltaTime / _blendDur);
            if (_blendK >= 1f) _shown.CopyFrom(_to);
            else EnvPreset.LerpInto(_shown, _from, _to, EnvPreset.Ease(_blendK));
            ApplyShown();
        }

        void ApplyShown()
        {
            var e = _shown;

            if (keyLight)
            {
                keyLight.transform.rotation = Quaternion.LookRotation(-EnvPreset.DirectionTo(e.Key), Vector3.up);
                keyLight.color = e.KeyColor;
                keyLight.intensity = e.KeyIntensity * keyScale * _roomDim;
                keyLight.shadows = LightShadows.Soft;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            float fill = e.FillIntensity * fillScale * _roomDim;
            RenderSettings.ambientSkyColor = e.SkyFill * fill;
            RenderSettings.ambientGroundColor = e.GroundFill * fill;
            RenderSettings.ambientEquatorColor = Color.Lerp(e.SkyFill, e.GroundFill, 0.5f) * fill;

            RenderSettings.fog = e.FogDensity > 0;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = e.FogColor;
            RenderSettings.fogDensity = AiOn.Rendering.AtmosphereMath.FogDensity(e.FogDensity, e.Outdoor, e.NightAmount, e.Name);

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
            _color.saturation.Override((e.Saturation - 1f) * 100f + _satBias);
            _color.contrast.Override((e.Contrast - 1f) * 100f);
            // The original mixes the tint in at tintAmount (post.js); 1.5x washed afternoon orange and dusk lavender.
            _color.colorFilter.Override(Color.Lerp(Color.white, e.Tint, Mathf.Clamp01(e.TintAmount)));
            _bloom.intensity.Override(e.BloomStrength * 1.6f);
            _bloom.threshold.Override(Mathf.Max(0.6f, e.BloomThreshold));
            _bloom.scatter.Override(Mathf.Clamp01(0.45f + e.BloomRadius * 0.5f));
            _vignette.intensity.Override(e.Vignette * 0.8f);
            _vignette.smoothness.Override(0.45f);
            _grain.type.Override(FilmGrainLookup.Thin1);
            _grain.intensity.Override(Mathf.Clamp01(e.Grain * 6f));

            ApplyGradeOverride(); // a story grade stays over the preset through a blend
            // The default reflection is the edit-time daytime sky: at night or in a blackout it would put a pale sheen
            // on every glossy surface (the player, parked cars). Reflections fade with the dark.
            RenderSettings.reflectionIntensity = ReflectionFor(e.NightAmount, power, e.Outdoor);

            ApplyLamps();
            UpdateLit();
            if (!Blending && _reflections) _reflections.Request(); // the live probe sees the room as it now is
            if (SmokeLevel > 0) { _smokeFogBase = -1; SetSmoke(SmokeLevel); } // a preset change mid-escape keeps the smoke
        }

        /// <summary>Environment reflection strength: 1 by day, down to 0.15 at full night, a little less again without power.</summary>
        public static float ReflectionFor(float nightAmount, bool power, bool outdoor)
        {
            float k = 1f - Mathf.Clamp01(nightAmount) * 0.85f;
            if (!power) k *= outdoor ? 0.7f : 0.6f;
            return k;
        }

        /// <summary>The preset's own grade again (after SetGradeOverride(null)): the shown look re-applied.</summary>
        void Reapply() => ApplyShown();

        // The story grade in force (null fields: the preset's own). Kept so every blend frame lays it over the preset.
        float? _gSat, _gExp, _gVig;
        Color? _gTint;

        void ApplyGradeOverride()
        {
            if (_gSat.HasValue) _color.saturation.Override((_gSat.Value - 1f) * 100f + _satBias);
            if (_gExp.HasValue) _color.postExposure.Override(Mathf.Log(Mathf.Max(0.05f, _shown.Exposure), 2f) + _shown.GradeExposure + _gExp.Value + exposureOffset);
            if (_gVig.HasValue) _vignette.intensity.Override(_gVig.Value * 0.8f);
            if (_gTint.HasValue) _color.colorFilter.Override(_gTint.Value);
        }

        /// <summary>Recomputes the lit levels from the current preset and power and hands them to the level's materials.</summary>
        void UpdateLit()
        {
            if (Current == null) return;
            LevelMaterials.MergeLitInto(_lit, _shown.Lit, power);
            PushLit();
        }

        /// <summary>Hands the current lit levels to the level's materials again (after a scene load).</summary>
        public void PushLit()
        {
            var mats = LevelMaterials.Instance;
            if (mats != null) { mats.SetNight(_shown.NightAmount); mats.SetLit(_lit); }
        }

        /// <summary>
        /// A temporary grade over the preset (original engine.post.setGrade): saturation (1 = unchanged), exposure (stops),
        /// tint [r, g, b] with tintAmount, vignette. null puts the preset's own grade back.
        /// </summary>
        public void SetGradeOverride(JObject grade)
        {
            if (grade == null)
            {
                _gSat = _gExp = _gVig = null; _gTint = null;
                SmokeActive = false; SetSmoke(0); if (Current != null) Reapply(); return;
            }
            if (grade["smoke"] != null) { SmokeActive = true; SetSmoke((float)grade["smoke"]); }
            if (grade["saturation"] != null) _gSat = (float)grade["saturation"];
            if (grade["exposure"] != null) _gExp = (float)grade["exposure"];
            if (grade["vignette"] != null) _gVig = (float)grade["vignette"];
            if (grade["tint"] is JArray t)
                _gTint = Color.Lerp(Color.white, new Color((float)t[0], (float)t[1], (float)t[2]), Mathf.Clamp01(((float?)grade["tintAmount"] ?? 0.3f) * 1.5f));
            ApplyGradeOverride();
        }

        /// <summary>Grid power: off is the blackout (lamps out; the interiors' battery lamps are InteriorDressing's).</summary>
        public void SetPower(bool on)
        {
            power = on;
            if (Current != null) RenderSettings.reflectionIntensity = ReflectionFor(_shown.NightAmount, power, _shown.Outdoor);
            ApplyLamps();
            UpdateLit(); // grid groups (street, windows, shops, signs) of the level's materials
            if (_reflections) _reflections.Request(0.6f); // after the lamps' own fade
            if (_skyRig && Current != null) _skyRig.Apply(_shown, power); // grid groups: distant city windows + beacons
        }

        float _roomDim = 1f;

        /// <summary>
        /// This level's own dimming of the env's key and fill (1 = as the preset): a shop with its power cut sits well
        /// below its battery lamps, so their pools read. Per scene (a new level starts at 1).
        /// </summary>
        public void SetRoomDim(float k)
        {
            k = Mathf.Clamp01(k);
            if (Mathf.Approximately(k, _roomDim)) return;
            _roomDim = k;
            if (Current != null) ApplyShown();
        }

        /// <summary>The current preset's 'indoor' lit level (1 when it has none).</summary>
        public float IndoorLit => Current != null && _shown.Lit.TryGetValue("indoor", out var v) ? v : 1f;

        System.Func<Vector3, float> _lampFactor;

        /// <summary>
        /// A per-lamp level on top of the preset and power (by the lamp's Unity position): the apartment's stairwell and
        /// corridor lamps give way to the motion-sensor lamps, the shelter's circuits on the emergency supply. null = all 1.
        /// </summary>
        public void SetLampFactor(System.Func<Vector3, float> factor) { _lampFactor = factor; ApplyLamps(); }

        void ApplyLamps()
        {
            float k = power ? IndoorLit * lampIntensity : 0f;
            for (int i = 0; i < indoorLamps.Count; i++)
            {
                var l = indoorLamps[i];
                if (!l) continue;
                float f = k * (_lampFactor != null ? _lampFactor(l.transform.position) : 1f);
                l.intensity = _lampBase[i] * f; l.enabled = f > 0.001f;
            }
        }
    }
}
