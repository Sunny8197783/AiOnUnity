using System.Collections.Generic;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Rendering
{
    /// <summary>One kind of real-world surface: a packed detail set and how strongly it shows (Resources/Surfaces/surfaces.json).</summary>
    public sealed class SurfaceKind
    {
        public string Name, Set;
        public float Scale = 0.5f, Normal = 0.5f, Albedo = 0.3f, Rough = 2f, Mean = 0.7f, Offset, Macro = 0.2f;
    }

    /// <summary>The surfaces table (pure; EditMode-tested): kinds, and per level which glTF material gets which kind.</summary>
    public sealed class SurfaceTable
    {
        public readonly Dictionary<string, SurfaceKind> Kinds = new();
        public readonly Dictionary<string, Dictionary<string, string>> Levels = new();

        public static SurfaceTable Parse(string json)
        {
            var t = new SurfaceTable();
            if (string.IsNullOrEmpty(json)) return t;
            var o = JObject.Parse(json);
            if (o["kinds"] is JObject kinds)
                foreach (var p in kinds.Properties())
                {
                    var k = (JObject)p.Value;
                    t.Kinds[p.Name] = new SurfaceKind
                    {
                        Name = p.Name, Set = (string)k["set"],
                        Scale = (float?)k["scale"] ?? 0.5f, Normal = (float?)k["normal"] ?? 0.5f, Albedo = (float?)k["albedo"] ?? 0.3f,
                        Rough = (float?)k["rough"] ?? 2f, Mean = (float?)k["mean"] ?? 0.7f, Offset = (float?)k["offset"] ?? 0f,
                        Macro = (float?)k["macro"] ?? 0.2f,
                    };
                }
            if (o["levels"] is JObject levels)
                foreach (var p in levels.Properties())
                {
                    var map = new Dictionary<string, string>();
                    foreach (var m in ((JObject)p.Value).Properties()) map[m.Name] = (string)m.Value;
                    t.Levels[p.Name] = map;
                }
            return t;
        }

        /// <summary>
        /// The glTF name of a material as the scene shows it: Unity's " (Instance)" and the runtime copies' " (state)" or
        /// " (glass)" suffixes dropped ("m~m22 (state)" -> "m~m22").
        /// </summary>
        public static string KeyOf(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return materialName;
            int cut = materialName.IndexOf(" (", System.StringComparison.Ordinal);
            return cut > 0 ? materialName.Substring(0, cut) : materialName;
        }

        /// <summary>The kind for a material of a level, or null when it keeps its look.</summary>
        public SurfaceKind KindFor(string levelId, string materialName)
        {
            if (levelId == null || !Levels.TryGetValue(levelId, out var map)) return null;
            return map.TryGetValue(KeyOf(materialName), out var kind) && Kinds.TryGetValue(kind, out var k) ? k : null;
        }
    }

    /// <summary>
    /// Real-world surfaces for the level models ("그래픽 사실화"): the listed glTF materials (walls, ceilings, floors, roads,
    /// pavements, concrete, wood) switch to AiOn/Surface, which keeps their colour and picture and adds a world-space CC0
    /// normal, roughness and albedo/AO detail. The level's runtime state copies (LevelMaterials) are switched in place, so
    /// their lit levels, emission and state pictures keep working; other materials get one shared copy. Materials with
    /// their own occlusion or metal-rough maps, texture rotation, transparency, vertex colours or a non-glTF
    /// shader are left as they are. Off on the low quality tiers. Added by EnvironmentController to every level.
    /// </summary>
    public sealed class LevelSurfaces : MonoBehaviour
    {
        /// <summary>Set by the quality tier: medium and up.</summary>
        public static bool Allowed { get; set; } = true;

        static SurfaceTable s_table;
        static Shader s_shader;
        static readonly Dictionary<string, (Texture2D n, Texture2D a)> s_sets = new();

        public const string GltfShader = "Shader Graphs/glTF-pbrMetallicRoughness";

        static readonly int DetailN = Shader.PropertyToID("_DetailN"), DetailA = Shader.PropertyToID("_DetailA"),
            DetailScale = Shader.PropertyToID("_DetailScale"), DetailNormal = Shader.PropertyToID("_DetailNormal"),
            DetailAlbedo = Shader.PropertyToID("_DetailAlbedo"), DetailRough = Shader.PropertyToID("_DetailRough"),
            DetailRoughMean = Shader.PropertyToID("_DetailRoughMean"), RoughOffset = Shader.PropertyToID("_RoughOffset"),
            Macro = Shader.PropertyToID("_Macro"), HasNormalMap = Shader.PropertyToID("_HasNormalMap");
        static readonly int NormalTex = Shader.PropertyToID("normalTexture"), OcclusionTex = Shader.PropertyToID("occlusionTexture"),
            MrTex = Shader.PropertyToID("metallicRoughnessTexture"), Rotation = Shader.PropertyToID("baseColorTexture_Rotation"),
            TexCoord = Shader.PropertyToID("baseColorTexture_texCoord");

        /// <summary>Materials switched in this level (for checks).</summary>
        public int Converted { get; private set; }
        public string LevelId { get; private set; }
        readonly List<Material> _owned = new();

        public static SurfaceTable Table
        {
            get
            {
                if (s_table == null)
                {
                    var ta = Resources.Load<TextAsset>("Surfaces/surfaces");
                    s_table = SurfaceTable.Parse(ta ? ta.text : null);
                }
                return s_table;
            }
        }

        /// <summary>
        /// Single materials of the level models whose finish was wrong (pure table; tested): (level, glTF material) ->
        /// (least roughness, most normal strength). The office whiteboard was glossy (roughness 0.25) and the ceiling
        /// lamps' glare covered a column header the story wants read; the shelter's foil blankets (one merged mesh) were
        /// chrome plates with full-strength embossing instead of soft crinkled film. Applied on every tier (a copy of the
        /// material, two numbers).
        /// </summary>
        public static bool FinishFor(string levelId, string materialName, out float minRoughness, out float maxNormal)
        {
            switch ((levelId, KeyOfName(materialName)))
            {
                case ("office", "m~m61"): minRoughness = 0.7f; maxNormal = 1f; return true;  // whiteboard: matte board
                case ("shelter", "m~m77"): minRoughness = 0.55f; maxNormal = 0.25f; return true; // foil blankets: soft film
            }
            minRoughness = 0f; maxNormal = 1f;
            return false;
        }

        static string KeyOfName(string n) => SurfaceTable.KeyOf(n);

        static readonly int RoughnessFactor = Shader.PropertyToID("roughnessFactor"), NormalScale = Shader.PropertyToID("normalTexture_scale");

        void Finishes()
        {
            var mats = new List<Material>();
            var copies = new Dictionary<Material, Material>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene) continue;
                r.GetSharedMaterials(mats);
                bool changed = false;
                for (int i = 0; i < mats.Count; i++)
                {
                    var m = mats[i];
                    if (!m || !FinishFor(LevelId, m.name, out float minR, out float maxN)) continue;
                    if (!copies.TryGetValue(m, out var c))
                    {
                        c = new Material(m) { name = m.name + " (finish)" };
                        _owned.Add(c);
                        if (c.HasProperty(RoughnessFactor)) c.SetFloat(RoughnessFactor, Mathf.Max(minR, c.GetFloat(RoughnessFactor)));
                        if (c.HasProperty(NormalScale)) c.SetFloat(NormalScale, Mathf.Min(maxN, c.GetFloat(NormalScale)));
                        copies[m] = c;
                    }
                    mats[i] = c; changed = true;
                }
                if (changed) r.sharedMaterials = mats.ToArray();
            }
        }

        void Start()
        {
            LevelId = gameObject.scene.name.ToLowerInvariant();
            NoBackdropShadows();
            Finishes();
            if (!Allowed) return;
            if (!s_shader) s_shader = SkyRig.LoadShader("AiOnSurface", "AiOn/Surface");
            if (!s_shader || !s_shader.isSupported) return;
            var table = Table;
            if (!table.Levels.ContainsKey(LevelId)) return;

            var copies = new Dictionary<Material, Material>();
            var mats = new List<Material>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh && mf.sharedMesh.HasVertexAttribute(VertexAttribute.Color)) continue; // vertex-coloured sets keep glTFast's shader
                r.GetSharedMaterials(mats);
                bool changed = false;
                for (int i = 0; i < mats.Count; i++)
                {
                    var m = mats[i];
                    if (!m) continue;
                    if (copies.TryGetValue(m, out var done)) { if (done != m) { mats[i] = done; changed = true; } continue; }
                    var kind = m.shader && m.shader.name == GltfShader ? table.KindFor(LevelId, m.name) : null;
                    if (kind == null || !Convertible(m)) { copies[m] = m; continue; }
                    // LevelMaterials' state copies are runtime materials it keeps writing to: switch those in place
                    bool runtimeCopy = m.name.EndsWith(" (state)") || m.name.EndsWith(" (glass)");
                    var target = runtimeCopy ? m : new Material(m) { name = m.name + " (surface)" };
                    if (!runtimeCopy) _owned.Add(target);
                    Apply(target, kind);
                    copies[m] = target;
                    if (target != m) { mats[i] = target; changed = true; }
                    Converted++;
                }
                if (changed) r.sharedMaterials = mats.ToArray();
            }
        }

        /// <summary>
        /// The levels' painted backdrops (the sky-and-city cylinder round the apartment, ~150 m out and 118 m high) cast
        /// shadows like walls: a sun lower than about 38 degrees never reached the apartment's windows. Anything that big
        /// stops casting (pure test: IsBackdrop), and so does window glass (AllSeeThrough).
        /// </summary>
        void NoBackdropShadows()
        {
            var mats = new List<Material>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                r.GetSharedMaterials(mats);
                if (IsBackdrop(r.bounds.size) || AllSeeThrough(mats)) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Window glass (every material transparent) casts no shadow: glTFast's transparent glass wrote the shadow map, so
        /// no sun and no window light ever came through a closed window.
        /// </summary>
        public static bool AllSeeThrough(List<Material> mats)
        {
            if (mats == null || mats.Count == 0) return false;
            foreach (var m in mats) if (!m || m.renderQueue < 2500) return false;
            return true;
        }

        /// <summary>A backdrop: wider than 200 m and taller than 90 m (the sky picture; merged town blocks stay under 65 m).</summary>
        public static bool IsBackdrop(Vector3 size) => Mathf.Max(size.x, size.z) > 200f && size.y > 90f;

        static bool Convertible(Material m)
        {
            if (m.renderQueue >= 2450) return false; // transparent or cut out
            if (m.HasProperty(OcclusionTex) && m.GetTexture(OcclusionTex)) return false;
            if (m.HasProperty(MrTex) && m.GetTexture(MrTex)) return false;
            if (Number(m, Rotation, "baseColorTexture_Rotation") > 1e-4f) return false; // glTFast keeps it as a vector (sin, ...)
            if (Number(m, TexCoord, "baseColorTexture_texCoord") > 0.5f) return false;
            return true;
        }

        /// <summary>A material number whatever its declared type (glTFast declares some transform values as vectors).</summary>
        static float Number(Material m, int id, string name)
        {
            if (!m.HasProperty(id)) return 0f;
            int i = m.shader.FindPropertyIndex(name);
            if (i < 0) return 0f;
            return m.shader.GetPropertyType(i) switch
            {
                UnityEngine.Rendering.ShaderPropertyType.Vector => Mathf.Abs(m.GetVector(id).x),
                UnityEngine.Rendering.ShaderPropertyType.Color => Mathf.Abs(m.GetColor(id).r),
                UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range => Mathf.Abs(m.GetFloat(id)),
                UnityEngine.Rendering.ShaderPropertyType.Int => Mathf.Abs(m.GetInteger(id)),
                _ => 0f,
            };
        }

        static void Apply(Material m, SurfaceKind k)
        {
            var set = Set(k.Set);
            bool hasNormal = m.HasProperty(NormalTex) && m.GetTexture(NormalTex);
            m.shader = s_shader; // same property names: colour, picture, normal map, roughness, emission and cull carry over
            m.SetFloat(HasNormalMap, hasNormal ? 1f : 0f);
            m.renderQueue = (int)RenderQueue.Geometry;
            m.SetTexture(DetailN, set.n);
            m.SetTexture(DetailA, set.a);
            m.SetFloat(DetailScale, k.Scale);
            m.SetFloat(DetailNormal, k.Normal);
            m.SetFloat(DetailAlbedo, k.Albedo);
            m.SetFloat(DetailRough, k.Rough);
            m.SetFloat(DetailRoughMean, k.Mean);
            m.SetFloat(RoughOffset, k.Offset);
            m.SetFloat(Macro, k.Macro);
        }

        static (Texture2D n, Texture2D a) Set(string name)
        {
            if (name == null) name = "plaster";
            if (s_sets.TryGetValue(name, out var s) && s.n && s.a) return s;
            s = (Resources.Load<Texture2D>("Surfaces/surf_" + name + "_n"), Resources.Load<Texture2D>("Surfaces/surf_" + name + "_a"));
            s_sets[name] = s;
            return s;
        }

        void OnDestroy()
        {
            foreach (var m in _owned) if (m) Destroy(m);
            _owned.Clear();
        }
    }
}
