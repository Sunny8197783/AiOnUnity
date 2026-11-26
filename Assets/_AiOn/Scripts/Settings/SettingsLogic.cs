using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AiOn.Levels;

namespace AiOn.Settings
{
    /// <summary>
    /// The graphics tiers (engine/quality.js): ids and aliases, ranks, the step down, the first-run guess from the
    /// machine, and what each tier changes in Unity (URP asset knobs, camera anti-aliasing, flashlight shadows; the crowd
    /// rows are <see cref="AiOn.Levels.Crowd.CrowdTier"/>). '높음' (high) is the look the game was tuned at.
    /// </summary>
    public static class QualityTiers
    {
        public static readonly string[] Tiers = { "ultralow", "low", "medium", "high", "ultra" };

        static readonly Dictionary<string, string> Alias = new()
        {
            ["ultra-low"] = "ultralow", ["ultraLow"] = "ultralow", ["verylow"] = "ultralow", ["very-low"] = "ultralow", ["minimum"] = "ultralow",
            ["ultra-high"] = "ultra", ["ultrahigh"] = "ultra", ["veryhigh"] = "ultra", ["very-high"] = "ultra", ["max"] = "ultra", ["normal"] = "medium", ["mid"] = "medium",
        };

        /// <summary>normQ: a tier id (aliases accepted), or <paramref name="fallback"/> when q is not one.</summary>
        public static string Norm(string q, string fallback = "medium")
        {
            var k = q == null ? null : Alias.TryGetValue(q, out var a) ? a : q;
            return k != null && Array.IndexOf(Tiers, k) >= 0 ? k : fallback;
        }

        public static int Rank(string q) => Array.IndexOf(Tiers, Norm(q, "medium"));
        public static string Lower(string q) => Tiers[Math.Max(0, Rank(q) - 1)];

        /// <summary>The Korean name of a tier (TIER_LABELS[q][0]), or the id.</summary>
        public static string Label(string q) => AiOn.UI.SettingsForm.TierLabels.TryGetValue(q ?? "", out var l) ? l[0] : q;

        /// <summary>
        /// savedQuality: a saved tier keeps its meaning (and qualityAuto: the governor may have lowered a detected one);
        /// anything else (nothing, 'auto', unknown) is detected again: (null, true).
        /// </summary>
        public static (string quality, bool auto) Saved(string quality, bool qualityAuto)
        {
            var q = Norm(quality, null);
            return q != null ? (q, qualityAuto) : (null, true);
        }

        static readonly Regex Software = new("swiftshader|llvmpipe|softpipe|software|basic render|microsoft basic");
        static readonly Regex Strong = new(@"rtx|gtx ?(9[78]0|10[6-9]0|16[56]0)|radeon rx|rx ?[5-7]\d{3}|rx ?vega|arc a\d|apple m\d (pro|max|ultra)|quadro|radeon pro");
        static readonly Regex Weak = new(@"intel|uhd|iris|hd graphics|mali|adreno|powervr|videocore|radeon\(tm\) graphics|radeon graphics|vega \d\b");

        /// <summary>autoTier: the first-run guess from the GPU name, CPU cores, memory (GB), screen pixels and the largest
        /// texture. 'ultra' is never guessed.</summary>
        public static string AutoTier(string gpu, int cores = 4, double memoryGb = 8, double pixels = 2e6, int maxTexture = 8192)
        {
            var g = (gpu ?? "").ToLowerInvariant();
            if (Software.IsMatch(g)) return "ultralow";
            var t = Strong.IsMatch(g) ? "high" : Weak.IsMatch(g) ? "low" : "medium";
            if (cores <= 2 || memoryGb <= 2 || maxTexture < 4096) t = Lower(Lower(t));
            else if (cores <= 4 || memoryGb <= 4) t = Lower(t);
            if (pixels > 5e6) t = Lower(t); // 4K-class screens: the fill rate decides
            return t;
        }

        /// <summary>What a tier sets in Unity. High is the URP asset as authored (PC_RPAsset).</summary>
        public readonly struct Knobs
        {
            public readonly float RenderScale, ShadowDistance;
            public readonly int Cascades, MainShadowMap, LightShadowMap, PerObjectLights;
            /// <summary>0 none, 1 SMAA medium, 2 SMAA high (quality.js smaa).</summary>
            public readonly int Smaa;
            /// <summary>The flashlight casts shadows (quality.js flash &gt; 0).</summary>
            public readonly bool FlashShadows;

            public Knobs(float renderScale, float shadowDistance, int cascades, int mainShadowMap, int lightShadowMap, int perObjectLights, int smaa, bool flashShadows)
            {
                RenderScale = renderScale; ShadowDistance = shadowDistance; Cascades = cascades; MainShadowMap = mainShadowMap;
                LightShadowMap = lightShadowMap; PerObjectLights = perObjectLights; Smaa = smaa; FlashShadows = flashShadows;
            }
        }

        // pr scale, sun shadow (0 = none), shadow map sizes, smaa and flash, as quality.js PROFILES gives them up.
        public static Knobs KnobsOf(string tier) => Norm(tier, "high") switch
        {
            "ultralow" => new Knobs(0.75f, 0f, 1, 512, 256, 2, 0, false),
            "low" => new Knobs(1f, 30f, 1, 512, 512, 2, 0, false),
            "medium" => new Knobs(1f, 40f, 2, 1024, 1024, 4, 1, false),
            "ultra" => new Knobs(1f, 70f, 4, 4096, 2048, 6, 2, true),
            _ => new Knobs(1f, 50f, 4, 2048, 2048, 4, 2, true),
        };
    }

    /// <summary>
    /// The frame-rate governor (quality.js createGovernor). Sample(raw seconds of the last frame) returns true when the
    /// game has stayed below <c>low</c> fps for <c>need</c> seconds of real time (busy stretches and the settle time after
    /// a change do not count). Above <c>high</c> fps the count resets; between the two it drains slowly, so hovering near
    /// the line never steps. A frame longer than <c>gap</c> seconds resets the window.
    /// </summary>
    public sealed class FrameGovernor
    {
        readonly double _low, _high, _need, _after, _gap;
        double _acc, _slow, _rt, _until;
        int _frames;

        public double Fps { get; private set; } = 60;
        public double Slow => _slow;

        public FrameGovernor(double low = 30, double high = 38, double need = 4, double settle = 3, double after = 6, double gap = 2.5)
        {
            _low = low; _high = high; _need = need; _after = after; _gap = gap; _until = settle;
        }

        /// <summary>Do not judge for s seconds (after a tier change, a level swap).</summary>
        public void Hold(double s = 3) { _until = Math.Max(_until, _rt + s); _slow = 0; }

        public bool Sample(double raw, bool busy = false, bool floor = false)
        {
            if (!(raw >= 0) || raw > _gap) { _acc = 0; _frames = 0; return false; }
            _rt += raw; _acc += raw; _frames++;
            if (_acc < 0.5) return false;
            double win = _acc; Fps = _frames / _acc; _acc = 0; _frames = 0;
            if (busy || floor || _rt < _until) { _slow = 0; return false; }
            if (Fps < _low) _slow += win; else if (Fps > _high) _slow = 0; else _slow = Math.Max(0, _slow - win * 0.5);
            if (_slow < _need) return false;
            _slow = 0; _until = _rt + _after;
            return true;
        }
    }

    /// <summary>
    /// The sound settings as gains (audio.js): the master is the volume, or 0 when muted or in 청각 장애 mode; each bus
    /// (ambience, effects, ui) is 0..1 on top of the master. Sources multiply their own level by their bus.
    /// </summary>
    public static class AudioBus
    {
        public static float Ambience { get; private set; } = 1f;
        public static float Effects { get; private set; } = 1f;
        public static float Ui { get; private set; } = 1f;
        /// <summary>A bus changed: sources that hold a level re-apply it.</summary>
        public static event Action Changed;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Ambience = Effects = Ui = 1f; Changed = null; }

        public static float Clamp01(double v) => double.IsNaN(v) ? 0f : (float)Math.Max(0, Math.Min(1, v));

        /// <summary>audio.setVolume + setMute: the master gain.</summary>
        public static float Master(double volume, bool mute, bool deaf) => mute || deaf ? 0f : Clamp01(volume);

        public static void Set(double ambience, double effects, double ui)
        {
            float a = Clamp01(ambience), e = Clamp01(effects), u = Clamp01(ui);
            if (a == Ambience && e == Effects && u == Ui) return;
            Ambience = a; Effects = e; Ui = u;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Without stairs (the wheelchair) a stair flight is a wall (engine/controls.js, geom.js groundAt): stepping from
    /// ground that is not a flight onto a walk zone whose ramp is steeper than 0.2 stops the body. Original frame.
    /// </summary>
    public static class StairRule
    {
        public const double Slope = 0.2;

        /// <summary>geom.js groundAt: the highest walk height under (x, z) within reach of feetY (+0.45 / -below), its
        /// slope and zone index; false when there is none.</summary>
        public static bool GroundAt(IReadOnlyList<LevelLayout.WalkZone> walk, double x, double z, double feetY, out double y, out double slope, out int index, double below = 0.6)
        {
            y = 0; slope = 0; index = -1;
            bool found = false;
            if (walk == null) return false;
            for (int i = 0; i < walk.Count; i++)
            {
                var w = walk[i];
                if (w == null || x < w.x0 - 0.02 || x > w.x1 + 0.02 || z < w.z0 - 0.02 || z > w.z1 + 0.02) continue;
                double wy = w.y, s = 0;
                if (w.isRamp)
                {
                    bool ax = w.rampAxis == "x";
                    double span = ax ? w.x1 - w.x0 : w.z1 - w.z0;
                    if (span == 0) span = 1;
                    double k = Math.Max(0, Math.Min(1, ((ax ? x - w.x0 : z - w.z0) / span)));
                    wy = w.rampY0 + (w.rampY1 - w.rampY0) * k; s = Math.Abs(w.rampY1 - w.rampY0) / span;
                }
                if (wy > feetY + 0.45 || wy < feetY - below) continue;
                if (!found || wy > y) { y = wy; slope = s; index = i; found = true; }
            }
            return found;
        }

        /// <summary>The steep zone the body would step onto going from (x, z) to (toX, toZ), or -1 when it may go.</summary>
        public static int Blocking(IReadOnlyList<LevelLayout.WalkZone> walk, double x, double z, double toX, double toZ, double feetY)
        {
            if (!GroundAt(walk, toX, toZ, feetY, out _, out double next, out int i) || next <= Slope) return -1;
            if (GroundAt(walk, x, z, feetY, out _, out double here, out _) && here > Slope) return -1; // already on the flight
            return i;
        }

        /// <summary>A walk zone as the Director's stair help reads it: { x0, x1, z0, z1, y, ramp: { axis, y0, y1 } }.</summary>
        public static Newtonsoft.Json.Linq.JObject ZoneJson(LevelLayout.WalkZone w) => new()
        {
            ["x0"] = w.x0, ["x1"] = w.x1, ["z0"] = w.z0, ["z1"] = w.z1, ["y"] = w.y,
            ["ramp"] = w.isRamp ? new Newtonsoft.Json.Linq.JObject { ["axis"] = w.rampAxis, ["y0"] = w.rampY0, ["y1"] = w.rampY1 } : null,
        };
    }

    /// <summary>Small helpers the settings apply with.</summary>
    public static class SettingsMath
    {
        /// <summary>controls.js setSensitivity: clamp(Number(v) || 1, 0.3, 3).</summary>
        public static float Sensitivity(double v) => double.IsNaN(v) || v == 0 ? 1f : (float)Math.Max(0.3, Math.Min(3, v));

        /// <summary>The text scale the menu offers (0.9 .. 1.3); anything else is 1.</summary>
        public static float TextScale(double v) => double.IsNaN(v) || v <= 0 ? 1f : (float)Math.Max(0.8, Math.Min(1.5, v));

        /// <summary>The brightest the 화면 밝기 setting goes, in stops over every look.</summary>
        public const double BrightnessMax = 1.5;

        /// <summary>The 화면 밝기 setting: stops of exposure added over every look, clamp(v, 0, 1.5); NaN (nothing saved, not a number) is 0.</summary>
        public static float Brightness(double v) => double.IsNaN(v) ? 0f : (float)Math.Max(0, Math.Min(BrightnessMax, v));

        /// <summary>A text's size at a scale (rounded, at least 1).</summary>
        public static int Scaled(int baseSize, float scale) => Math.Max(1, (int)Math.Round(baseSize * scale, MidpointRounding.AwayFromZero));

        /// <summary>A line for the speech process: UTF-8 as base64 (no encoding trouble on the pipe, no line breaks).</summary>
        public static string SpeechLine(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes((text ?? "").Replace('\n', ' ')));
    }
}
