using System;
using AiOn.Levels.Crowd;
using AiOn.Story;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AiOn.Settings
{
    /// <summary>
    /// The settings the host owns (app.js applySettings): master volume and mute (AudioListener), the sound buses, the
    /// mouse sensitivity, the graphics tier (URP asset, camera anti-aliasing, flashlight shadows, crowd rows) with the
    /// frame-rate governor, the text scale, reduced motion on the camera, and the experience profile's body (run, jump,
    /// eye height, stairs) and senses (청각 장애: no sound; 저시력: a blurred, dimmer, greyer world and spoken prompts).
    /// Lives on the story host (Boot scene, persistent).
    /// </summary>
    [RequireComponent(typeof(UnityStoryHost))]
    public class HostSettings : MonoBehaviour
    {
        UnityStoryHost _host;
        readonly FrameGovernor _gov = new();
        WindowsSpeech _speech;
        Volume _lowVision;
        VolumeProfile _lowVisionProfile;
        string _profile = "none";

        /// <summary>The tier in force (never 'auto').</summary>
        public string Tier { get; private set; }
        /// <summary>The governor stepped the tier down: (tier, Korean label). The Director keeps it and says so.</summary>
        public Action<string, string> Stepped;
        /// <summary>The Editor's frame rate (scene view, inspectors, tools) is not the game's: there the governor only runs
        /// when this is set (to try it). A build always runs it.</summary>
        public static bool GovernInEditor;

        void Awake()
        {
            _host = GetComponent<UnityStoryHost>();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // A return to the title destroys this host after the next one has applied its settings: the shared state (the
        // URP asset, the listener volume) is put back only when the game ends, never by an instance going away.
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _speech?.Dispose(); _speech = null;
            if (_lowVision) Destroy(_lowVision.gameObject);
            if (_lowVisionProfile)
            {
                foreach (var c in _lowVisionProfile.components) if (c) Destroy(c);
                Destroy(_lowVisionProfile);
                _lowVisionProfile = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_asset = null;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        static void OnQuit() { RestoreAsset(); AudioListener.volume = 1f; }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => _gov.Hold(4); // a level swap is not the game running slow

        // ------------------------------------------------------------------ apply

        /// <summary>The first-run tier (quality.js autoTier) from this machine.</summary>
        public static string DetectTier() => QualityTiers.AutoTier(SystemInfo.graphicsDeviceName, SystemInfo.processorCount,
            SystemInfo.systemMemorySize / 1024.0, (double)Screen.currentResolution.width * Screen.currentResolution.height, SystemInfo.maxTextureSize);

        /// <summary>StoryDirector.ApplyHostSettings: the settings object and the body of the profile in force.</summary>
        public void Apply(JObject s, ExperienceProfile body)
        {
            if (s == null) return;
            _profile = Str(s["profile"]) ?? "none";
            bool deaf = _profile == "deaf";

            AudioListener.volume = AudioBus.Master(Num(s["volume"], 0.7), Truthy(s["mute"]), deaf);
            AudioBus.Set(Num(s["volAmbience"], 1), Num(s["volEffects"], 1), Num(s["volUi"], 1));

            var p = _host ? _host.player : null;
            if (p)
            {
                p.sensitivity = SettingsMath.Sensitivity(Num(s["sensitivity"], 1));
                if (body != null) { p.canRun = body.Run; p.canJump = body.Jump; p.canStairs = body.Stairs; p.eye = (float)body.Eye; }
            }
            if (_host && _host.cameraRig) _host.cameraRig.reduceMotion = Truthy(s["reduceMotion"]);
            TextScaler.Apply((float)Num(s["textScale"], 1));

            var tier = QualityTiers.Norm(Str(s["quality"]), null) ?? Tier ?? DetectTier();
            if (tier != Tier) SetTier(tier);

            LowVision(_profile == "lowvision");
            if (_profile != "lowvision") _speech?.Cancel();
        }

        /// <summary>Low-vision mode: read this aloud (the Director calls it only in that mode).</summary>
        public void Speak(string text)
        {
            if (_profile != "lowvision") return;
            (_speech ??= new WindowsSpeech()).Speak(text);
        }

        // ------------------------------------------------------------------ graphics tier

        static UniversalRenderPipelineAsset s_asset;
        static float s_rs0, s_sd0; static int s_cc0, s_ms0, s_ls0, s_po0;

        /// <summary>Applies a tier at once: crowd rows, the URP asset knobs, the camera's anti-aliasing, flashlight shadows.</summary>
        public void SetTier(string tier)
        {
            Tier = QualityTiers.Norm(tier, "high");
            CrowdTier.Set(Tier);
            var k = QualityTiers.KnobsOf(Tier);
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset)
            {
                if (asset != s_asset)
                {
                    RestoreAsset();
                    s_asset = asset; // the authored values, put back when the game ends (in the Editor the asset is the project's)
                    s_rs0 = asset.renderScale; s_sd0 = asset.shadowDistance; s_cc0 = asset.shadowCascadeCount;
                    s_ms0 = asset.mainLightShadowmapResolution; s_ls0 = asset.additionalLightsShadowmapResolution; s_po0 = asset.maxAdditionalLightsCount;
                }
                asset.renderScale = k.RenderScale;
                asset.shadowDistance = k.ShadowDistance;
                asset.shadowCascadeCount = k.Cascades;
                asset.mainLightShadowmapResolution = k.MainShadowMap;
                asset.additionalLightsShadowmapResolution = k.LightShadowMap;
                asset.maxAdditionalLightsCount = k.PerObjectLights;
            }
            var cam = _host && _host.cameraRig ? _host.cameraRig.cam : null;
            if (cam && cam.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                data.antialiasing = k.Smaa > 0 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = k.Smaa > 1 ? AntialiasingQuality.High : AntialiasingQuality.Medium;
            }
            if (_host) _host.FlashShadows = k.FlashShadows;
            _gov.Hold(4);
        }

        static void RestoreAsset()
        {
            if (!s_asset) return;
            s_asset.renderScale = s_rs0; s_asset.shadowDistance = s_sd0; s_asset.shadowCascadeCount = s_cc0;
            s_asset.mainLightShadowmapResolution = s_ms0; s_asset.additionalLightsShadowmapResolution = s_ls0; s_asset.maxAdditionalLightsCount = s_po0;
            s_asset = null;
        }

        // Frame-rate governor: below 30 fps for 4 s of real time steps down one tier, then waits 6 s; never below the
        // lowest tier, never while no level is up, the window is in the background or a cutscene camera plays.
        void Update()
        {
            if (Tier == null || (Application.isEditor && !GovernInEditor)) return;
            var rig = _host ? _host.cameraRig : null;
            bool busy = !Application.isFocused || (_host && (_host.LevelId == null || _host.Loading)) || (rig && rig.overrideActive);
            if (!_gov.Sample(Time.unscaledDeltaTime, busy, QualityTiers.Rank(Tier) == 0)) return;
            string from = Tier, q = QualityTiers.Lower(Tier);
            Debug.LogWarning($"[Settings] fps {_gov.Fps:0.0} < 30 for 4 s; quality {from} -> {q}");
            SetTier(q);
            try { Stepped?.Invoke(q, QualityTiers.Label(q)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ low vision

        // The original: #world { filter: blur(6px) brightness(.6) contrast(.85) saturate(.6) } on the 3D view only (the
        // HUD stays sharp). Here a Gaussian depth-of-field blur from the near view on, and a channel mix that keeps 60 %
        // of the colour and 60 % of the light, on a volume over the preset's grade (the HUD is not post-processed).
        void LowVision(bool on)
        {
            if (!on) { if (_lowVision) _lowVision.weight = 0; return; }
            if (!_lowVision)
            {
                var go = new GameObject("LowVisionGrade");
                go.transform.SetParent(transform, false);
                _lowVision = go.AddComponent<Volume>();
                _lowVision.isGlobal = true;
                _lowVision.priority = 20;
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                _lowVisionProfile = prof;
                _lowVision.profile = prof;
                var dof = prof.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(0.2f);
                dof.gaussianEnd.Override(2.5f);
                dof.gaussianMaxRadius.Override(1.5f);
                dof.highQualitySampling.Override(true);
                var mix = prof.Add<ChannelMixer>(true);
                const float keep = 0.6f, light = 0.6f, grey = 1f - keep;
                float lr = 21.26f * grey, lg = 71.52f * grey, lb = 7.22f * grey; // the luminance share, in percent
                mix.redOutRedIn.Override((100f * keep + lr) * light); mix.redOutGreenIn.Override(lg * light); mix.redOutBlueIn.Override(lb * light);
                mix.greenOutRedIn.Override(lr * light); mix.greenOutGreenIn.Override((100f * keep + lg) * light); mix.greenOutBlueIn.Override(lb * light);
                mix.blueOutRedIn.Override(lr * light); mix.blueOutGreenIn.Override(lg * light); mix.blueOutBlueIn.Override((100f * keep + lb) * light);
            }
            _lowVision.weight = 1;
        }

        // ------------------------------------------------------------------ JSON helpers

        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static double Num(JToken t, double d) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t
            : t != null && t.Type == JTokenType.String && double.TryParse((string)t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Boolean => (bool)t, JTokenType.Integer => (long)t != 0, JTokenType.Float => (double)t != 0,
            JTokenType.String => ((string)t).Length > 0, JTokenType.Null => false, JTokenType.Undefined => false, _ => true,
        };
    }
}
