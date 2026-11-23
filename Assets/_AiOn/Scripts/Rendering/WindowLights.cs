using System.Collections.Generic;
using AiOn.Levels;
using UnityEngine;

namespace AiOn.Rendering
{
    /// <summary>One window opening of an interior level (Unity coordinates): its centre, the way into the room, its size,
    /// and how deep the room behind it is (from the glass, along Inward).</summary>
    public readonly struct WindowOpening
    {
        public readonly Vector3 Center, Inward;
        public readonly float Width, Height, Depth;
        public WindowOpening(Vector3 center, Vector3 inward, float width, float height, float depth = 10f)
        {
            Center = center; Inward = inward.normalized; Width = width; Height = height; Depth = depth;
        }
    }

    /// <summary>Pure numbers behind <see cref="WindowLights"/> (EditMode-tested).</summary>
    public static class WindowLightMath
    {
        /// <summary>
        /// The windows of each interior level (measured from the level models' glass): the apartment's corridor strip
        /// (north), the veranda glass of 302호 (south), the stairwell window and the lobby's glass porch; the shop fronts
        /// of the store and the mart; the office's yard windows. Levels without daylight (shelter) or outdoors: none.
        /// </summary>
        public static List<WindowOpening> For(string levelId)
        {
            var w = new List<WindowOpening>();
            switch (levelId)
            {
                case "apartment":
                    // corridor strip (the corridor is 2 m deep), 302's veranda glass (the flat runs 9.4 m to the corridor wall),
                    // the stairwell's landing windows (5.1 m to the corridor wall), the lobby's glass porch
                    for (float x = -13f; x <= 18f; x += 5f) w.Add(new WindowOpening(new Vector3(x, 7.2f, 2.0f), Vector3.back, 4.5f, 1.1f, 2.0f));
                    foreach (float x in new[] { -6.2f, -3.7f, -1.2f }) w.Add(new WindowOpening(new Vector3(x, 6.85f, -9.45f), Vector3.forward, 2.4f, 2.0f, 9.4f));
                    foreach (float y in new[] { 3.4f, 6.2f, 8.6f }) w.Add(new WindowOpening(new Vector3(5.0f, y, -5.1f), Vector3.forward, 1.3f, 1.8f, 5.1f));
                    foreach (float x in new[] { 1.6f, 6.4f }) w.Add(new WindowOpening(new Vector3(x, 1.4f, 6.5f), Vector3.back, 4.5f, 2.1f, 5f));
                    break;
                case "store":
                    foreach (float x in new[] { -3.6f, 0.6f, 3.1f }) w.Add(new WindowOpening(new Vector3(x, 1.4f, 0f), Vector3.forward, 3.6f, 2.2f, 9f));
                    break;
                case "mart":
                    foreach (float x in new[] { -12.1f, -9.0f, -5.9f }) w.Add(new WindowOpening(new Vector3(x, 1.3f, 0f), Vector3.forward, 3.7f, 2.6f, 14f));
                    break;
                case "office":
                    foreach (float x in new[] { -6f, 0f, 6f }) w.Add(new WindowOpening(new Vector3(x, 1.6f, 12.1f), Vector3.back, 6f, 1.65f, 12f));
                    break;
            }
            return w;
        }

        /// <summary>
        /// Light from outside through a window: full daylight by day, a dim city glow at night with the town's power on,
        /// moonlight only in a blackout night (pure; tested). Returns (intensity, colour) for a spot light of WindowLights.
        /// </summary>
        public static (float intensity, Color color) Outside(float nightAmount, bool cityPower)
        {
            float day = 1f - Mathf.Clamp01(nightAmount);
            float nightI = cityPower ? CityNight : MoonNight;
            // moonlight is a desaturated blue-grey (a saturated blue turned the red signs navy in the dark rooms)
            var nightC = cityPower ? new Color(0.74f, 0.75f, 0.84f) : MoonColor;
            var c = Color.Lerp(nightC, Daylight, day);
            // the sky outside is bright for most of the dusk: brightness follows day^0.6
            float i = Mathf.Lerp(nightI, DayIntensity, Mathf.Pow(day, 0.6f));
            return (i, c);
        }

        public const float DayIntensity = 26f, CityNight = 3.2f, MoonNight = 4.5f;

        /// <summary>
        /// Night light through a window into a deep room (pure; tested): moonlight and the city's glow are sky light, which a
        /// deep room gets through a big pane over a long way; the 302 living room (9.4 m behind the veranda glass) read as a
        /// black frame at the plain night intensity while the 2 m corridor was already lit. Day is unchanged.
        /// </summary>
        public static float DepthGain(WindowOpening w, float nightAmount)
            => Mathf.Lerp(1f, Mathf.Clamp(w.Depth / 4f, 0.8f, 2.2f), Mathf.Clamp01(nightAmount));
        public static readonly Color Daylight = new(0.96f, 0.97f, 1f);
        public static readonly Color MoonColor = new(0.7f, 0.75f, 0.85f);

        /// <summary>How far outside the pane the spot sits: a window is an area light, and a source set back from it lights
        /// the room evenly instead of burning the sill (inverse-square from 2 m out, not from the glass).</summary>
        public const float SetBack = 2.6f;

        /// <summary>The spot's range for a window of this size (a big pane reaches deeper into the room), set-back included.</summary>
        public static float RangeFor(float width, float height) => SetBack + Mathf.Clamp(5.5f + 1.2f * Mathf.Sqrt(Mathf.Max(0f, width * height)), 5.5f, 9f);

        /// <summary>
        /// The spot's range for this window: the size rule, but never much past the far wall of the room behind it (a
        /// light without a shadow goes through walls and floors, so it must stop where its room stops).
        /// </summary>
        public static float RangeFor(WindowOpening w) => Mathf.Min(RangeFor(w.Width, w.Height), SetBack + w.Depth + 1.0f);

        /// <summary>How far above or below a window's centre the eye may be and still be on its storey (m).</summary>
        public const float StoreyBand = 1.8f;

        /// <summary>
        /// Whether an eye at this position is in the room this window lights (pure; tested): on the inside of the glass,
        /// no deeper than the room, on the same storey and not far along the wall. Only those windows are lit: the nearest windows by plain
        /// distance were often in the next room or on the floor above, and their light came through the walls and slabs.
        /// </summary>
        public static bool Serves(WindowOpening w, Vector3 eye)
        {
            var d = eye - w.Center;
            float inward = Vector3.Dot(d, w.Inward);
            if (inward < -0.3f || inward > w.Depth + 0.5f) return false;
            if (Mathf.Abs(d.y) >= StoreyBand) return false;
            // and not off to the side in another room (the stairwell window is level with the living room, 9 m along)
            var side = Vector3.Cross(Vector3.up, w.Inward);
            return Mathf.Abs(Vector3.Dot(d, side)) < w.Width * 0.5f + SideReach;
        }

        /// <summary>How far past a window's edge, along the wall, its room may reach (m).</summary>
        public const float SideReach = 3f;

        /// <summary>
        /// The windows to light for an eye (pure; tested): the nearest of those that serve its room, at most max, nearest
        /// first. Fills into with indices into windows.
        /// </summary>
        public static void Pick(IReadOnlyList<WindowOpening> windows, Vector3 eye, int max, List<int> into)
        {
            into.Clear();
            for (int n = 0; n < max; n++)
            {
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < windows.Count; i++)
                {
                    if (into.Contains(i) || !Serves(windows[i], eye)) continue;
                    float d = (windows[i].Center - eye).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0 || bestD > 30f * 30f) break;
                into.Add(best);
            }
        }

        /// <summary>Where the spot sits: outside the pane and a little above it, so the wall the window is in stays unlit (as a
        /// real window wall) and the frame and the slab above shape the light.</summary>
        public static Vector3 LightPosition(WindowOpening w) => w.Center - w.Inward * SetBack + Vector3.up * (0.3f + 0.25f * w.Height);

        /// <summary>The cone (outer, inner, degrees): wide enough to fill the room, with a soft core so no hot spot sits on
        /// the floor just inside the glass.</summary>
        public const float SpotAngle = 100f, InnerSpotAngle = 20f;

        /// <summary>The spot's direction: into the room and a little down (sky light comes from above).</summary>
        public static Vector3 LightDirection(WindowOpening w) => (w.Inward - Vector3.up * 0.32f).normalized;
    }

    /// <summary>
    /// Motivated light through the windows of the interior levels ("그래픽 사실화"): each window gets a soft spot light just
    /// outside it, aimed into the room, whose colour and strength follow the light outside (daylight, the city's glow at
    /// night, moonlight in a blackout). The levels change time and power at run time, so nothing is baked; only the
    /// nearest few windows of the room the camera is in are on (WindowLightMath.Pick), each stops at its room's far wall,
    /// and on the high tiers the nearest two cast soft shadows (ShadowsAllowed) so the frames shape the light.
    /// Added by EnvironmentController to every level; no-op where WindowLightMath has no windows.
    /// </summary>
    public sealed class WindowLights : MonoBehaviour
    {
        public const int MaxLit = 5, MaxShadowed = 2;

        /// <summary>Whether the extra lights (windows, the fire) may cast shadows on this quality tier (HostSettings.SetTier:
        /// high and up, like the flashlight's).</summary>
        public static bool ShadowsAllowed { get; set; } = true;

        sealed class Win { public WindowOpening W; public Light Light; }

        readonly List<Win> _wins = new();
        readonly List<WindowOpening> _openings = new();
        readonly List<int> _pick = new();
        EnvironmentController _env;
        float _next;
        float _gain = 1f;
        public string LevelId { get; private set; }

        /// <summary>Tuning: every window light's intensity is multiplied by this.</summary>
        public float gain = 1f;

        public void Init(string levelId)
        {
            LevelId = levelId;
            foreach (var w in WindowLightMath.For(levelId))
            {
                var go = new GameObject("WindowLight");
                go.transform.SetParent(transform, false);
                go.transform.position = WindowLightMath.LightPosition(w);
                go.transform.rotation = Quaternion.LookRotation(WindowLightMath.LightDirection(w), Vector3.up);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = WindowLightMath.SpotAngle;
                l.innerSpotAngle = WindowLightMath.InnerSpotAngle;
                l.range = WindowLightMath.RangeFor(w);
                l.shadows = LightShadows.None;
                LightShadowBias.Set(l);
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                _wins.Add(new Win { W = w, Light = l });
                _openings.Add(w);
            }
            if (_wins.Count == 0) enabled = false;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;
            if (!_env) _env = EnvironmentController.Instance;
            var cam = Camera.main;
            if (!_env || _env.Current == null || !cam) return;
            var shown = _env.Shown;
            // the town's grid: a blackout darkens the city outside too (the level's own power is the grid's)
            var (intensity, color) = WindowLightMath.Outside(shown.NightAmount, _env.power);
            _gain = gain * (shown.Outdoor ? 0f : 1f);
            WindowLightMath.Pick(_openings, cam.transform.position, MaxLit, _pick);
            int maxShadowed = ShadowsAllowed ? MaxShadowed : 0;
            for (int i = 0; i < _wins.Count; i++)
            {
                int rank = _pick.IndexOf(i);
                var sh = rank >= 0 && rank < maxShadowed ? LightShadows.Soft : LightShadows.None;
                if (_wins[i].Light.shadows != sh) _wins[i].Light.shadows = sh;
            }
            for (int i = 0; i < _wins.Count; i++)
            {
                var w = _wins[i];
                bool on = _pick.Contains(i) && intensity * _gain > 0.01f;
                if (w.Light.enabled != on) w.Light.enabled = on;
                if (!on) continue;
                w.Light.color = color;
                w.Light.intensity = intensity * _gain * WindowLightMath.DepthGain(w.W, shown.NightAmount);
            }
        }
    }

    /// <summary>
    /// Shadow bias for the extra lights (fire, windows): URP's pipeline bias (PC 0.1 / 0.5) is tuned for the sun's
    /// cascades and drew contour bands ("shadow acne") on walls under a point light's few cube-face texels.
    /// </summary>
    public static class LightShadowBias
    {
        public const float Depth = 1.0f, Normal = 1.2f;

        public static void Set(Light l)
        {
            if (!l) return;
            l.shadowBias = Depth; l.shadowNormalBias = Normal;
            var d = UnityEngine.Rendering.Universal.LightExtensions.GetUniversalAdditionalLightData(l);
            if (d) d.usePipelineSettings = false;
        }
    }
}
