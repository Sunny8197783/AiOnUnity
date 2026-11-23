using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AiOn.Rendering
{
    /// <summary>
    /// A soft fill light for people only (competition upgrade, "그래픽 사실화"): in the dark beats (blackout corridors, the
    /// shelter at night, the 304 POV) the Rocketbox faces sank to silhouettes. A soft directional light shines from just
    /// above and to the right of the camera and lights only renderers on the people's rendering layer (<see cref="LayerBit"/>,
    /// "Light Layer 3"; URP light layers are on in both pipeline assets, and the layer is set on the light's URP data,
    /// which is what URP reads), so the walls, floors and the mood of the level stay as they are. It is a directional
    /// light so a person across the room reads as well as one at arm's length. It grows as the scene darkens
    /// (<see cref="Strength"/>) and fades out in bright daylight. Every person joins the layer when its
    /// <c>PersonAnimator</c> wakes (<see cref="Mark"/>). Play mode only; nothing allocates per frame.
    /// </summary>
    public sealed class PeopleFill : MonoBehaviour
    {
        /// <summary>Rendering layer of people (URP light layer 3; the default layer is bit 0).</summary>
        public const int LayerBit = 3;
        public const uint LayerMask = 1u << LayerBit;

        /// <summary>Intensity at full strength (a directional light without shadows).</summary>
        public const float MaxIntensity = 0.6f;
        /// <summary>Scene brightness (<see cref="SceneLight"/>) at and above which the fill is off, and below which it is full.</summary>
        public const float DarkBelow = 0.10f, BrightAbove = 1.0f;

        /// <summary>
        /// How much fill a scene gets, 0..1, from its brightness: full at or below <see cref="DarkBelow"/>, none at or above
        /// <see cref="BrightAbove"/>, smooth between (pure; tested).
        /// </summary>
        public static float Strength(float sceneLight)
        {
            float t = Mathf.Clamp01((sceneLight - DarkBelow) / (BrightAbove - DarkBelow));
            return 1f - t * t * (3 - 2 * t);
        }

        /// <summary>
        /// A rough brightness of the scene from the ambient (the brighter of the trilight's sky and equator, as luminance)
        /// and the key light (its intensity times its colour's luminance, weighted down: a low sun still lights faces) (pure; tested).
        /// </summary>
        public static float SceneLight(Color ambientSky, Color ambientEquator, float keyIntensity, Color keyColor)
        {
            float amb = Mathf.Max(Lum(ambientSky), Lum(ambientEquator));
            return amb + 0.15f * Mathf.Max(0f, keyIntensity) * Lum(keyColor);
        }

        /// <summary>
        /// The most fill a scene of this brightness takes (pure; tested): only what is missing up to a readable face, so a lit
        /// night street (its own ambient near 0.35) adds almost nothing and the crowd under it no longer glows white once
        /// the night exposure lifts it; a blackout corridor still gets most of <see cref="MaxIntensity"/>.
        /// </summary>
        public static float Cap(float sceneLight) => Mathf.Clamp(ReadableLight - sceneLight, 0.04f, MaxIntensity);

        /// <summary>Scene brightness at which faces read without help (see Cap).</summary>
        public const float ReadableLight = 0.42f;

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Puts every renderer under a person on the people's layer (once, when the person wakes).</summary>
        public static void Mark(GameObject person)
        {
            if (!person) return;
            foreach (var r in person.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= LayerMask;
        }

        static PeopleFill _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // '메인 메뉴로' (TitleReturn) destroys every persistent root, this one too: make it again on the next scene
            // load, or every scene after the first return to the title had no fill (faces black in the dark beats).
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Reboot;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += Reboot;
            if (_instance) return;
            // Not DontSave: a DontSave object outlives play mode in the Editor, so every play session left one more fill
            // light behind, all of them still lighting the people's layer (dozens summed to a white glow on every person
            // at night). Clear any such leftovers (named PeopleFill, outside any scene) before making this session's one.
            foreach (var old in Resources.FindObjectsOfTypeAll<Light>())
                if (old && old.name == "PeopleFill" && !old.gameObject.scene.IsValid()) Destroy(old.gameObject);
            var go = new GameObject("PeopleFill") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PeopleFill>();
        }

        static void Reboot(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!_instance && Application.isPlaying) Boot();
        }

        Light _light;
        Camera _cam;
        float _next, _want, _shown;

        void Awake()
        {
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.color = new Color(1f, 0.95f, 0.88f);
            _light.shadows = LightShadows.None;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.renderingLayerMask = (int)LayerMask;
            _light.GetUniversalAdditionalLightData().renderingLayers = LayerMask; // URP's light layers read this
            _light.cullingMask = ~0;
            _light.intensity = 0f;
            _light.enabled = false;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.25f;
                if (!_cam || !_cam.isActiveAndEnabled) _cam = Camera.main;
                var sun = RenderSettings.sun;
                float key = sun && sun.isActiveAndEnabled ? sun.intensity : 0f;
                float light = SceneLight(RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, key, sun ? sun.color : Color.black);
                _want = Mathf.Min(MaxIntensity * Strength(light), Cap(light));
            }
            _shown = Mathf.MoveTowards(_shown, _want, Time.unscaledDeltaTime * MaxIntensity); // about a second to settle
            bool on = _cam && _shown > 0.01f;
            if (_light.enabled != on) _light.enabled = on;
            if (!on) return;
            _light.intensity = _shown;
            var ct = _cam.transform;
            // from a little above and to the right of the camera, along the view
            var f = ct.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) f = ct.up;
            f.Normalize();
            transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-20f, Vector3.up) * (f - Vector3.up * 0.35f));
        }
    }
}
