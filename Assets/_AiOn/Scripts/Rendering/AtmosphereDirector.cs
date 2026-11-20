using System;
using AiOn.Levels;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Rendering
{
    /// <summary>Pure numbers behind <see cref="AtmosphereDirector"/> (EditMode-tested).</summary>
    public static class AtmosphereMath
    {
        /// <summary>
        /// How strongly the cold base look sits over a preset: the warm peacetime looks (a lit home, daytime streets) keep
        /// most of their warmth, evening and the dark ones take it whole.
        /// </summary>
        public static float BaseWeightFor(string preset) => preset switch
        {
            "interior" => 0.35f,
            "day" => 0.5f,
            "afternoon" => 0.45f,
            "interiorNight" => 0.7f,
            "dusk" => 0.8f,
            _ => 1f,
        };

        /// <summary>Peak of the emergency grade for a blast this far away (metres): full within 25 m, nothing past 220 m.</summary>
        public static float BlastPeak(float distance) => Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(25f, 160f, distance)) * (distance > 220f ? 0f : 1f);

        /// <summary>The emergency grade while the siren sounds: a low red that swells with the siren's wail (period s).</summary>
        public static float SirenLevel(float t, float period, bool reduceMotion)
        {
            if (reduceMotion) return 0.3f;
            float wave = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f / Mathf.Max(0.1f, period));
            return 0.22f + 0.2f * wave;
        }

        /// <summary>A pulse: quick rise (attack s), then an exponential fall with the given half-life (s).</summary>
        public static float Pulse(float age, float attack, float halfLife)
        {
            if (age < 0) return 0;
            if (age < attack) return age / attack;
            return Mathf.Pow(0.5f, (age - attack) / Mathf.Max(0.01f, halfLife));
        }

        /// <summary>A small cold desaturation for the whole game (ColorAdjustments saturation points), by base weight.</summary>
        public static float SaturationBias(float baseWeight) => -10f * Mathf.Clamp01(baseWeight);

        /// <summary>
        /// Fog density for a preset on top of the original's (URP exp² fog): outdoors it thickens with the night so street
        /// lamps and guide lights glow through it (0.003 read as no fog at all); interiors keep half, the dark interior and
        /// the underground shelter three quarters so their haze shows.
        /// </summary>
        public static float FogDensity(float original, bool outdoor, float night, string preset)
        {
            if (outdoor) return original * (1.3f + 2.4f * Mathf.Clamp01(night));
            return original * (preset == "underground" || preset == "interiorDark" ? 0.75f : 0.5f);
        }
    }

    /// <summary>
    /// The game's mood on top of the level's environment preset, from four volume profiles under Resources/Look:
    /// <list type="bullet">
    /// <item>BaseLook: cool slate shadows, a hint of warm highlights, a weak far blur (always on, weight by preset).</item>
    /// <item>Emergency: red grade, hard vignette, grain and lens fringe for air raids, blasts and smoke.</item>
    /// <item>RadioSignal: <see cref="AnalogSignal"/> while a broadcast is tuned in, with random dropouts.</item>
    /// <item>MaskView: <see cref="MaskView"/> while something is worn over the face.</item>
    /// </list>
    /// Persistent across level loads. All entry points are static and do nothing outside play mode, so story code
    /// (run headless by the EditMode tests) can call them freely.
    /// </summary>
    public sealed class AtmosphereDirector : MonoBehaviour
    {
        public const float SirenPeriod = 4.2f;

        static AtmosphereDirector s_inst;

        /// <summary>Set by the settings (reduce motion): no jitter, roll or shake and no throbbing red.</summary>
        public static bool ReduceMotion { get; set; }

        Volume _base, _emergency, _signal, _mask;
        MaskView _maskFx;
        AnalogSignal _signalFx;
        float _blastAt = -999f, _blastPeak;
        float _sirenUntil;
        int _radio;
        float _burstUntil, _nextBurst;
        float _maskTarget, _respirator;
        float _manualEmergency;
        float _lastBias = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_inst = null; ReduceMotion = false; }

        static AtmosphereDirector Get()
        {
            if (!Application.isPlaying) return null;
            if (s_inst) return s_inst;
            var go = new GameObject("AtmosphereDirector");
            DontDestroyOnLoad(go);
            s_inst = go.AddComponent<AtmosphereDirector>();
            return s_inst;
        }

        /// <summary>Makes sure the base look is up (EnvironmentController calls it when a level starts).</summary>
        public static void Ensure() => Get();

        /// <summary>A blast at this distance from the player: a red flash-and-fade sized by the distance.</summary>
        public static void Blast(float distance)
        {
            var d = Get(); if (!d) return;
            float peak = AtmosphereMath.BlastPeak(distance) * (ReduceMotion ? 0.6f : 1f);
            if (peak <= 0) return;
            float now = d.CurrentPulse();
            d._blastPeak = Mathf.Max(peak, now);
            d._blastAt = Time.unscaledTime;
            d.Burst(0.25f + 0.4f * peak); // the picture tears for a moment
        }

        /// <summary>The air-raid siren: on for up to seconds (the synthesized siren ends by itself after a minute).</summary>
        public static void Siren(bool on, float seconds = 60f)
        {
            var d = Get(); if (!d) return;
            d._sirenUntil = on ? Time.unscaledTime + seconds : 0f;
        }

        /// <summary>A held emergency level 0..1 set by script (e.g. a scene of its own); 0 releases it.</summary>
        public static void SetEmergency(float level)
        {
            var d = Get(); if (!d) return;
            d._manualEmergency = Mathf.Clamp01(level);
        }

        /// <summary>A broadcast is being received (nests). Dispose the result to stop.</summary>
        public static IDisposable RadioScope()
        {
            var d = Get(); if (!d) return Nothing.Instance;
            d._radio++;
            d._nextBurst = Time.unscaledTime + 0.6f;
            return new Scope(() => { if (d) d._radio = Mathf.Max(0, d._radio - 1); });
        }

        /// <summary>A short signal tear (an emergency alert arriving, a blast).</summary>
        public static void Glitch(float seconds) { var d = Get(); if (d) d.Burst(seconds); }

        /// <summary>Something worn over the face: "cloth" (wet towel), "respirator" (gas mask); null takes it off.</summary>
        public static void Mask(string kind)
        {
            var d = Get(); if (!d) return;
            d._maskTarget = kind == null ? 0f : 1f;
            if (kind != null) d._respirator = kind == "respirator" ? 1f : 0f;
        }

        /// <summary>A decision moment: the world falls out of focus and a little dim behind the choices (counted).</summary>
        public static void Focus(bool on)
        {
            var d = Get(); if (!d) return;
            d._focusCount = Mathf.Max(0, d._focusCount + (on ? 1 : -1));
        }

        Volume _focus;
        int _focusCount;

        void BuildFocus()
        {
            var go = new GameObject("DecisionFocus");
            go.transform.SetParent(transform, false);
            _focus = go.AddComponent<Volume>();
            _focus.isGlobal = true; _focus.priority = 33; _focus.weight = 0;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            _focus.profile = p;
            var dof = p.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
            dof.mode.Override(UnityEngine.Rendering.Universal.DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0.3f);
            dof.gaussianEnd.Override(3f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(true);
            var ca = p.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca.postExposure.Override(-0.45f);
            ca.saturation.Override(-25f);
        }

        void Burst(float seconds)
        {
            if (ReduceMotion) return;
            _burstUntil = Mathf.Max(_burstUntil, Time.unscaledTime + seconds);
        }

        float CurrentPulse() => _blastPeak * AtmosphereMath.Pulse(Time.unscaledTime - _blastAt, 0.08f, 0.9f);

        void Awake()
        {
            _base = MakeVolume("Look/AiOn_BaseLook", 5, 1f, false);
            _emergency = MakeVolume("Look/AiOn_Emergency", 30, 0f, false);
            _signal = MakeVolume("Look/AiOn_RadioSignal", 31, 0f, true);
            _mask = MakeVolume("Look/AiOn_MaskView", 32, 0f, true);
            if (_signal && _signal.profile) _signal.profile.TryGet(out _signalFx);
            if (_mask && _mask.profile) _mask.profile.TryGet(out _maskFx);
        }

        Volume MakeVolume(string resource, float priority, float weight, bool copy)
        {
            var profile = Resources.Load<VolumeProfile>(resource);
            if (!profile) { Debug.LogWarning($"[Look] missing Resources/{resource} (run AiOn → Graphics → Build Look Assets)"); return null; }
            var go = new GameObject(System.IO.Path.GetFileName(resource));
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true; v.priority = priority; v.weight = weight;
            // profiles whose values change at run time are copied, so play mode never writes into the asset
            v.profile = copy ? Instantiate(profile) : profile;
            return v;
        }

        void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            var env = EnvironmentController.Instance;
            string preset = env && env.Current != null ? env.preset : "interior";

            bool siren = now < _sirenUntil;
            float emergency = Mathf.Max(CurrentPulse(), _manualEmergency);
            if (siren) emergency = Mathf.Max(emergency, AtmosphereMath.SirenLevel(now, SirenPeriod, ReduceMotion));
            if (EnvironmentController.SmokeActive) emergency = Mathf.Max(emergency, EnvironmentController.SmokeLevel * 0.35f);
            if (_emergency) _emergency.weight = Mathf.Clamp01(emergency);

            float baseW = AtmosphereMath.BaseWeightFor(preset);
            if (siren || emergency > 0.2f) baseW = 1f;
            if (_base) _base.weight = Mathf.MoveTowards(_base.weight, baseW, dt * 0.5f);
            float bias = AtmosphereMath.SaturationBias(_base ? _base.weight : baseW);
            if (env && !Mathf.Approximately(bias, _lastBias)) { _lastBias = bias; env.SetSaturationBias(bias); }

            // radio: a steady weak signal with a dropout every few seconds
            if (_radio > 0 && now >= _nextBurst)
            {
                Burst(UnityEngine.Random.Range(0.12f, 0.45f));
                _nextBurst = now + UnityEngine.Random.Range(1.8f, 5.5f);
            }
            bool burst = now < _burstUntil;
            float signal = (_radio > 0 ? 0.5f : 0f) + (burst ? 0.5f : 0f);
            if (ReduceMotion) signal *= 0.5f;
            if (_signal) _signal.weight = Mathf.MoveTowards(_signal.weight, signal, dt * (burst ? 12f : 3f));
            if (_signalFx)
            {
                // the tear is jitter and roll; the steady signal only scan lines, drift and snow
                _signalFx.jitter.value = ReduceMotion ? 0f : burst ? 0.75f : 0.15f;
                _signalFx.verticalJump.value = ReduceMotion ? 0f : burst ? 0.12f : 0f;
                _signalFx.horizontalShake.value = ReduceMotion ? 0f : burst ? 0.25f : 0f;
            }

            if (_mask) _mask.weight = Mathf.MoveTowards(_mask.weight, _maskTarget, dt / 0.6f);
            if (_focusCount > 0 && !_focus) BuildFocus();
            if (_focus) _focus.weight = Mathf.MoveTowards(_focus.weight, _focusCount > 0 ? 1f : 0f, dt / 0.3f);
            if (_maskFx) _maskFx.respirator.value = _respirator;
        }

        void OnDestroy() { if (s_inst == this) s_inst = null; }

        sealed class Scope : IDisposable
        {
            Action _end;
            public Scope(Action end) { _end = end; }
            public void Dispose() { _end?.Invoke(); _end = null; }
        }

        sealed class Nothing : IDisposable
        {
            public static readonly Nothing Instance = new();
            public void Dispose() { }
        }
    }
}
