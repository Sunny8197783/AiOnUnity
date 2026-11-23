using System.Collections.Generic;
using AiOn.Interaction;
using AiOn.Levels;
using UnityEngine;

namespace AiOn.Rendering
{
    /// <summary>
    /// Guiding light: a soft coloured glow and a small light at the places a player should find without being told:
    /// exits and stairs (green, like Korean emergency exit signs), shelter entrances (amber), supplies (cool white) and
    /// the extinguisher (red). It grows with the dark (night, blackout, an unlit room), so by day it is barely there, and
    /// only the nearest few carry a real light (URP's per-object light budget belongs to the level's lamps).
    /// Added by EnvironmentController to every level; follows each point's enabled state.
    /// </summary>
    public sealed class GuidingLights : MonoBehaviour
    {
        public enum Kind { Exit, Shelter, Supply, Safety }

        /// <summary>Which interaction points get a guide, by the original's point id.</summary>
        public static readonly Dictionary<string, Kind> Table = new()
        {
            // exits and stairs
            ["lobbyExit"] = Kind.Exit, ["homeExitDoor"] = Kind.Exit, ["stairs1F"] = Kind.Exit, ["stairs3F"] = Kind.Exit,
            ["martExit"] = Kind.Exit, ["storeExit"] = Kind.Exit, ["officeExit"] = Kind.Exit, ["shelterExit"] = Kind.Exit,
            // shelters
            ["shelterEast"] = Kind.Shelter, ["shelterA"] = Kind.Shelter, ["shelterB"] = Kind.Shelter, ["shelterC"] = Kind.Shelter,
            ["reception"] = Kind.Shelter, ["assembly"] = Kind.Shelter,
            // supplies
            ["homeBag"] = Kind.Supply, ["homeStock"] = Kind.Supply, ["shelfWater"] = Kind.Supply, ["shelfFood"] = Kind.Supply,
            ["shelfHealth"] = Kind.Supply, ["storeShelf"] = Kind.Supply, ["water"] = Kind.Supply,
            // safety equipment
            ["extinguisher"] = Kind.Safety,
        };

        public static Color ColorOf(Kind k) => k switch
        {
            Kind.Exit => new Color(0.25f, 1f, 0.45f),
            Kind.Shelter => new Color(1f, 0.7f, 0.25f),
            Kind.Safety => new Color(1f, 0.28f, 0.22f),
            _ => new Color(0.7f, 0.88f, 1f),
        };

        public static float RangeOf(Kind k) => k switch { Kind.Shelter => 6f, Kind.Exit => 3f, _ => 1.8f };

        /// <summary>
        /// Light intensity at full visibility: a hint of colour on the nearby surfaces, never a room light. Only exits (their
        /// sign) and shelter entrances carry a light; a supply or the extinguisher has no lamp of its own (its light sat at
        /// the foot of the veranda shelf as an unexplained hot spot), so they keep only the glow.
        /// </summary>
        public static float IntensityOf(Kind k) => k switch { Kind.Shelter => 0.9f, Kind.Exit => 0.35f, _ => 0f };

        /// <summary>How much of the light shows at a visibility (pure; tested): none by day (a guide light in daylight only
        /// tinted the walls), full in the dark.</summary>
        public static float LightShare(float visibility) => Mathf.Clamp01((visibility - 0.3f) / 0.4f);

        /// <summary>
        /// Fade by distance from the eye (m): a guide you are standing at has done its job (and would flood the corridor
        /// with its colour), so it fades in from 1.5 m to 4 m.
        /// </summary>
        public static float NearFade(float distance) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 4f, distance));

        /// <summary>
        /// How strongly guides show: 0.3 by day up to 1 in the dark (night, no power, an unlit room). A powerless room by
        /// day (interiorDark with daylight at the windows: the 3장 morning) is daylight, not dark: the green washed the
        /// front door and wall in the grey morning.
        /// </summary>
        public static float Visibility(float night, bool power, bool outdoor, string preset, bool daylightInside = false)
        {
            if (daylightInside) return 0.3f;
            float dark = Mathf.Clamp01(night);
            if (!power) dark = Mathf.Max(dark, outdoor ? 0.85f : 0.9f);
            if (preset == "interiorDark" || preset == "underground") dark = Mathf.Max(dark, 0.8f);
            return Mathf.Lerp(0.3f, 1f, dark);
        }

        /// <summary>The glow's brightness and size per kind: an exit's is a sign-sized soft glow at the sign, not a ball of
        /// light (it was the brightest thing in the dark corridor).
        /// A supply's is a faint glint (in the blackout living room the bag's and the stock's cool-white glow at the foot of
        /// the veranda shelf was the brightest thing in the frame; the outline already marks what can be picked up).</summary>
        public static float HaloGain(Kind k) => k switch { Kind.Exit => 0.55f, Kind.Shelter => 1f, Kind.Safety => 0.6f, _ => 0.16f };
        public static float HaloSize(Kind k) => k switch { Kind.Shelter => 0.9f, Kind.Exit => 0.3f, _ => 0.2f };

        /// <summary>Height of a guide above its point (on the floor): an exit's glow and light sit at the sign over the
        /// door (2.2 m, where the levels' green signs hang), a shelter's over its entrance, a supply's at hand height.</summary>
        public static float HeightOf(Kind k) => k switch { Kind.Exit => 2.2f, Kind.Shelter => 1.1f, _ => 0.45f };

        /// <summary>
        /// A dark interior with the power out: the nearest exits' lights become the building's battery emergency lamps
        /// (비상조명등 by the stair doors), a dim neutral light that lets the corridor read; otherwise the green sign glow.
        /// (pure; tested) Returns (colour, intensity, range).
        /// </summary>
        public static (Color color, float intensity, float range) ExitLight(bool emergency)
            => emergency ? (new Color(0.92f, 0.95f, 1f), EmergencyIntensity, 9f) : (ColorOf(Kind.Exit), IntensityOf(Kind.Exit), RangeOf(Kind.Exit));

        public const float EmergencyIntensity = 5f;

        /// <summary>How far an exit's light sits out from its sign, toward where one walks up to the door (horizontal, at
        /// most <see cref="LampStandOff"/>; zero when the point is right under the sign) (pure; tested).</summary>
        public static Vector3 LampOffset(Vector3 sign, Vector3 point)
        {
            var d = point - sign; d.y = 0f;
            float m = d.magnitude;
            return m < 0.05f ? Vector3.zero : d / m * Mathf.Min(LampStandOff, m);
        }

        public const float LampStandOff = 0.9f;

        /// <summary>Exits of a building's common spaces (stairs, lobby, shop and shelter doors) have emergency lamps; a
        /// flat's own front door does not.</summary>
        public static bool HasEmergencyLamp(string pointId) => pointId != "homeExitDoor";

        /// <summary>
        /// The exit sign an exit guide belongs to (pure; tested): the nearest sign-sized box within reach of the guide's spot
        /// over the door. The guide then sits on the sign's face, a few cm out towards the point; the stair door's glow hung
        /// half a metre out in the corridor beside the hydrant cabinet, a green ball that no lamp explained.
        /// </summary>
        public static bool SnapToSign(Vector3 guide, Vector3 point, IReadOnlyList<Bounds> signs, out Vector3 at)
        {
            at = guide; float best = SignReach;
            bool found = false;
            foreach (var b in signs)
            {
                float d = Vector3.Distance(b.center, guide);
                if (d >= best) continue;
                best = d; found = true;
                var outDir = point - b.center; outDir.y = 0f;
                at = b.center + (outDir.sqrMagnitude > 1e-4f ? outDir.normalized * 0.06f : Vector3.zero);
            }
            return found;
        }

        public const float SignReach = 1.3f;

        /// <summary>Whether a renderer is an exit sign (the levels' green signs use the "lit:emergency" materials).</summary>
        public static bool IsSign(string materialName, Vector3 size)
        {
            if (materialName == null || materialName.IndexOf("lit:emergency", System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return big >= 0.25f && big <= 0.9f;
        }

        /// <summary>Lights that may be on at once (the rest keep only their glow).</summary>
        public const int MaxLit = 3;

        sealed class Guide
        {
            public InteractionPoint Point;
            public Kind Kind;
            public Transform Root;
            public Light Light;
            public Renderer Halo;
            public float Phase;
        }

        readonly List<Guide> _guides = new();
        readonly List<Bounds> _signs = new();
        MaterialPropertyBlock _mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color"), SizeId = Shader.PropertyToID("_Size");
        Material _mat;
        float _nextPick;
        static Mesh s_quad;

        void Start()
        {
            var template = Resources.Load<Material>("Look/GuideHalo");
            if (!template) { Debug.LogWarning("[Look] missing Resources/Look/GuideHalo (run AiOn → Graphics → Build Look Assets)"); enabled = false; return; }
            _mat = new Material(template) { name = "GuideHalo (runtime)" };
            // the level's exit signs, once (exit guides sit on them)
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m && IsSign(m.name, r.bounds.size)) _signs.Add(r.bounds);
            }
            foreach (var p in InteractionPoint.All)
                if (p && p.id != null && Table.TryGetValue(p.id, out var kind)) _guides.Add(Build(p, kind));
        }

        Guide Build(InteractionPoint p, Kind kind)
        {
            var root = new GameObject("Guide_" + p.id).transform;
            root.SetParent(transform, false);
            // above the point: over a door, the glow sits where an exit sign would be
            root.position = p.Position + Vector3.up * HeightOf(kind);
            if (kind == Kind.Exit && SnapToSign(root.position, p.Position, _signs, out var onSign)) root.position = onSign;

            var lgo = new GameObject("Light");
            lgo.transform.SetParent(root, false);
            var l = lgo.AddComponent<Light>();
            // an exit's sign (and the emergency lamp beside it) lights the floor and the door below, not the ceiling
            // it hangs under (a point light there burnt a white patch on the ceiling)
            l.type = kind == Kind.Exit ? LightType.Spot : LightType.Point;
            if (kind == Kind.Exit)
            {
                lgo.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward); l.spotAngle = 150f; l.innerSpotAngle = 70f;
                // Off the wall toward the door's walk-up point (visual QA): flush on the sign, the emergency lamp's wide cone
                // grazed the wall a few centimetres away and burnt it white for anyone standing at the stair door.
                lgo.transform.position = root.position + LampOffset(root.position, p.Position);
            }
            l.color = ColorOf(kind);
            l.range = RangeOf(kind);
            l.shadows = LightShadows.None;
            l.enabled = false;

            var hgo = new GameObject("Halo");
            hgo.transform.SetParent(root, false);
            hgo.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = hgo.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            return new Guide { Point = p, Kind = kind, Root = root, Light = l, Halo = r, Phase = Random.value * 6f };
        }

        static Mesh Quad()
        {
            if (s_quad) return s_quad;
            s_quad = new Mesh { name = "GuideQuad" };
            s_quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            s_quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // the shader billboards it; bounds big enough that culling never drops a visible glow
            s_quad.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return s_quad;
        }

        void Update()
        {
            var cam = Camera.main;
            if (!cam) return;
            var env = EnvironmentController.Instance;
            var e = env ? env.Shown : null;
            bool dayIn = e != null && !e.Outdoor && env.preset == "interiorDark" && EnvironmentController.DaylightOutside;
            float vis = e != null ? Visibility(e.NightAmount, env.power, e.Outdoor, env.preset, dayIn) : 0.5f;
            bool emergency = e != null && !e.Outdoor && !env.power && !dayIn;
            var eye = cam.transform.position;
            if (Time.unscaledTime >= _nextPick) { _nextPick = Time.unscaledTime + 0.25f; PickLit(eye); }

            _mpb ??= new MaterialPropertyBlock();
            float t = Time.time;
            if (AiOn.UI.GameHud.Cinematic != _cine) { _cine = AiOn.UI.GameHud.Cinematic; PickLit(eye); } // at once, not up to 0.25 s late
            foreach (var g in _guides)
            {
                if (!g.Halo.enabled) continue;
                // a slow breathing pulse; exits a touch brighter
                float pulse = AtmosphereDirector.ReduceMotion ? 1f : 0.85f + 0.15f * Mathf.Sin(t * 1.6f + g.Phase);
                float near = NearFade(Vector3.Distance(eye, g.Root.position));
                float k = vis * pulse * near;
                var c = ColorOf(g.Kind) * k * HaloGain(g.Kind); c.a = 1f;
                _mpb.SetColor(ColorId, c);
                _mpb.SetFloat(SizeId, HaloSize(g.Kind));
                g.Halo.SetPropertyBlock(_mpb);
                if (g.Kind == Kind.Exit)
                {
                    bool lamp = emergency && HasEmergencyLamp(g.Point ? g.Point.id : null);
                    var (lc, li, lr) = ExitLight(lamp);
                    g.Light.color = lc; g.Light.range = lr;
                    // the emergency lamp does not fade as you reach it (it is the corridor's light, not a hint)
                    g.Light.intensity = lamp ? li : li * LightShare(vis) * near;
                }
                else g.Light.intensity = IntensityOf(g.Kind) * LightShare(vis) * near;
            }
        }

        bool _cine;

        void PickLit(Vector3 eye)
        {
            // nearest MaxLit enabled guides within 30 m carry a light
            _guides.Sort((a, b) => (a.Root.position - eye).sqrMagnitude.CompareTo((b.Root.position - eye).sqrMagnitude));
            int lit = 0;
            foreach (var g in _guides)
            {
                // wayfinding glows are for play: in a cutscene they hung as unexplained orbs beside people (the ending's
                // assembly point). Exit signs stay: the blackout emergency lamp is the corridor's own light.
                bool on = g.Point && g.Point.Enabled && !(g.Kind != Kind.Exit && AiOn.UI.GameHud.Cinematic);
                g.Halo.enabled = on;
                bool light = on && lit < MaxLit && IntensityOf(g.Kind) > 0f && (g.Root.position - eye).sqrMagnitude < 900f;
                if (light) lit++;
                g.Light.enabled = light;
            }
        }

        void OnDestroy()
        {
            if (_mat) Destroy(_mat);
        }
    }
}
