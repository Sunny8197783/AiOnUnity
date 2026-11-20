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

        /// <summary>Light intensity at full visibility: a hint of colour on the nearby surfaces, never a room light.</summary>
        public static float IntensityOf(Kind k) => k switch { Kind.Shelter => 0.9f, Kind.Exit => 0.35f, Kind.Safety => 0.3f, _ => 0.25f };

        /// <summary>
        /// Fade by distance from the eye (m): a guide you are standing at has done its job (and would flood the corridor
        /// with its colour), so it fades in from 1.5 m to 4 m.
        /// </summary>
        public static float NearFade(float distance) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 4f, distance));

        /// <summary>How strongly guides show: 0.3 by day up to 1 in the dark (night, no power, an unlit room).</summary>
        public static float Visibility(float night, bool power, bool outdoor, string preset)
        {
            float dark = Mathf.Clamp01(night);
            if (!power) dark = Mathf.Max(dark, outdoor ? 0.85f : 0.9f);
            if (preset == "interiorDark" || preset == "underground") dark = Mathf.Max(dark, 0.8f);
            return Mathf.Lerp(0.3f, 1f, dark);
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
            foreach (var p in InteractionPoint.All)
                if (p && p.id != null && Table.TryGetValue(p.id, out var kind)) _guides.Add(Build(p, kind));
        }

        Guide Build(InteractionPoint p, Kind kind)
        {
            var root = new GameObject("Guide_" + p.id).transform;
            root.SetParent(transform, false);
            // above the point: over a door, the glow sits where an exit sign would be
            float up = kind == Kind.Exit || kind == Kind.Shelter ? 1.1f : 0.45f;
            root.position = p.Position + Vector3.up * up;

            var lgo = new GameObject("Light");
            lgo.transform.SetParent(root, false);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
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
            float vis = e != null ? Visibility(e.NightAmount, env.power, e.Outdoor, env.preset) : 0.5f;
            var eye = cam.transform.position;
            if (Time.unscaledTime >= _nextPick) { _nextPick = Time.unscaledTime + 0.25f; PickLit(eye); }

            _mpb ??= new MaterialPropertyBlock();
            float t = Time.time;
            foreach (var g in _guides)
            {
                if (!g.Halo.enabled) continue;
                // a slow breathing pulse; exits a touch brighter
                float pulse = AtmosphereDirector.ReduceMotion ? 1f : 0.85f + 0.15f * Mathf.Sin(t * 1.6f + g.Phase);
                float near = NearFade(Vector3.Distance(eye, g.Root.position));
                float k = vis * pulse * near;
                var c = ColorOf(g.Kind) * k * (g.Kind == Kind.Exit ? 1.2f : 1f); c.a = 1f;
                _mpb.SetColor(ColorId, c);
                _mpb.SetFloat(SizeId, g.Kind == Kind.Shelter ? 0.9f : g.Kind == Kind.Exit ? 0.5f : 0.35f);
                g.Halo.SetPropertyBlock(_mpb);
                g.Light.intensity = IntensityOf(g.Kind) * vis * near;
            }
        }

        void PickLit(Vector3 eye)
        {
            // nearest MaxLit enabled guides within 30 m carry a light
            _guides.Sort((a, b) => (a.Root.position - eye).sqrMagnitude.CompareTo((b.Root.position - eye).sqrMagnitude));
            int lit = 0;
            foreach (var g in _guides)
            {
                bool on = g.Point && g.Point.Enabled;
                g.Halo.enabled = on;
                bool light = on && lit < MaxLit && (g.Root.position - eye).sqrMagnitude < 900f;
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
