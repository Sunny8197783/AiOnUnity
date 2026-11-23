using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// The level's materials that change at run time, like the original's kit.setLit groups and level states:
    /// <list type="bullet">
    /// <item>lit groups (street, windows, shops, signs, indoor, emergency, yard): the emission follows the preset's lit
    /// level and the grid power (SetLit); backlit panels ('dim') also darken their colour;</item>
    /// <item>state rules (Data/Source/&lt;id&gt;-states.json 'materials'): colour, emission, opacity and redrawn pictures
    /// per state value (power, windowView, alerts, gates...).</item>
    /// </list>
    /// At Awake every listed material gets a runtime copy that replaces it on the scene's renderers (the imported asset is
    /// never touched). All colours here are linear (three.js / glTF terms) and are written as linear.gamma: Unity
    /// linearises Color properties on upload in a Linear project (SetVector on a Color property goes the same way), so
    /// the shader receives the linear value. glTFast does this for baseColorFactor but writes emissiveFactor raw, so its
    /// own import renders emission as roughly e^2.2; runtime copies correct that, which keeps lit levels and state
    /// ratios linear (emissiveIntensity = base * level, as kit.js). State colours are scaled by their ratio to the
    /// exported default where that is non-zero.
    /// </summary>
    public class LevelMaterials : MonoBehaviour
    {
        [Serializable]
        public class Rule
        {
            public string key;
            [Tooltip("The state value as JSON (\"on\", [\"danji\"], {\"east\":\"closed\"}, 2).")]
            public string value;
            public bool hasColor;
            [Tooltip("Linear base colour (three.js).")]
            public Color color = Color.white;
            public bool hasEmission;
            [Tooltip("Linear emission (three.js emissive * emissiveIntensity).")]
            public Color emission = Color.black;
            public bool hasOpacity;
            public float opacity = 1;
            public Texture2D map;
            public Texture2D emissiveMap;
        }

        [Serializable]
        public class Entry
        {
            public Material source;
            [Tooltip("Lit group (street, windows, shops, signs, indoor, emergency, yard); empty when none.")]
            public string group = "";
            [Tooltip("Backlit panel: unlit colour level (userData.unlitColor); < 0 when none.")]
            public float dim = -1;
            [Header("Default as imported (linear glTF values)")]
            public Color importedColor = Color.white;
            public Color importedEmission = Color.black;
            public float importedOpacity = 1;
            [Tooltip("KHR_materials_emissive_strength of the GLB material (glTFast ignores it; it is folded into importedEmission).")]
            public float emissiveStrength = 1;
            [Header("Default in the states table (linear three.js values)")]
            public bool hasDef;
            public bool hasDefColor;
            public Color defColor = Color.white;
            public bool hasDefEmission;
            public Color defEmission = Color.black;
            public float defOpacity = 1;
            public List<Rule> rules = new();

            [NonSerialized] public Material instance;
            [NonSerialized] public Texture defaultMap, defaultEmissiveMap;
            [NonSerialized] public Dictionary<string, List<Rule>> byKey;
            [NonSerialized] public Dictionary<string, List<string>> valuesByKey;
            /// <summary>A light pool on the ground (transparent, black base, emissive): drawn additively, see IsGlowPool.</summary>
            [NonSerialized] public bool glow;
            [NonSerialized] public List<Renderer> glowRenderers;
            [NonSerialized] Dictionary<string, Rule> _active;
            /// <summary>The rule in force per state key (none: the default for that key).</summary>
            public Dictionary<string, Rule> Active => _active ??= new Dictionary<string, Rule>();
        }

        /// <summary>Material values in the imported material's terms (linear colours) before lit levels.</summary>
        public struct MatState
        {
            public Color Color, Emission;
            public float Opacity;
            public Texture Map, EmissiveMap;
        }

        public static readonly string[] GridGroups = { "street", "windows", "shops", "signs" };
        public const float LitEpsilon = 0.0005f;

        /// <summary>glTFast shader graph properties (Runtime/Scripts/Material/MaterialProperty.cs) and the URP Lit fallbacks.</summary>
        static class Prop
        {
            public static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
            public static readonly int BaseColorTexture = Shader.PropertyToID("baseColorTexture");
            public static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor");
            public static readonly int EmissiveTexture = Shader.PropertyToID("emissiveTexture");
            public static readonly int UrpBaseColor = Shader.PropertyToID("_BaseColor");
            public static readonly int UrpBaseMap = Shader.PropertyToID("_BaseMap");
            public static readonly int UrpEmissionColor = Shader.PropertyToID("_EmissionColor");
            public static readonly int UrpEmissionMap = Shader.PropertyToID("_EmissionMap");
            public static readonly int RoughnessFactor = Shader.PropertyToID("roughnessFactor");
            public static readonly int UrpSmoothness = Shader.PropertyToID("_Smoothness");
        }

        public List<Entry> entries = new();
        [Tooltip("Tuning: every final emission is multiplied by this.")]
        public float emissionGain = 1f;

        public static LevelMaterials Instance { get; private set; }

        /// <summary>How dark the sky is (the env's night amount, 0..1): ground light pools glow fully only at night.</summary>
        public float nightAmount = 1f;
        readonly List<Material> _extra = new();

        readonly Dictionary<string, float> _levels = new();
        readonly List<string> _order = new();
        readonly HashSet<string> _keys = new();
        bool _ready;

        void Awake()
        {
            Instance = this;
            Init();
            var env = EnvironmentController.Instance;
            if (env != null && env.LitLevels != null) SetLit(env.LitLevels);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var e in entries) if (e.instance) Destroy(e.instance);
            foreach (var m in _extra) if (m) Destroy(m);
            _extra.Clear();
        }

        void Init()
        {
            if (_ready) return;
            _ready = true;
            var map = new Dictionary<Material, Material>();
            foreach (var e in entries)
            {
                if (!e.source) continue;
                e.byKey = new Dictionary<string, List<Rule>>();
                e.valuesByKey = new Dictionary<string, List<string>>();
                foreach (var r in e.rules)
                {
                    if (string.IsNullOrEmpty(r.key)) continue;
                    _keys.Add(r.key);
                    if (!e.byKey.TryGetValue(r.key, out var list)) { e.byKey[r.key] = list = new List<Rule>(); e.valuesByKey[r.key] = new List<string>(); }
                    list.Add(r);
                    e.valuesByKey[r.key].Add(CanonicalJson(r.value));
                }
                e.glow = !string.IsNullOrEmpty(e.group) && IsGlowPool(e.source.renderQueue, e.importedColor, e.importedEmission);
                if (map.TryGetValue(e.source, out var shared)) { e.instance = shared; }
                else
                {
                    e.instance = e.glow ? GlowPoolMaterial(e.source.name) : null;
                    if (!e.instance) { e.glow = false; e.instance = new Material(e.source) { name = e.source.name + " (state)" }; }
                    map[e.source] = e.instance;
                }
                e.defaultMap = GetTex(e.source, Prop.BaseColorTexture, Prop.UrpBaseMap);
                e.defaultEmissiveMap = GetTex(e.source, Prop.EmissiveTexture, Prop.UrpEmissionMap);
            }
            // Swap the copies into every renderer of this scene that uses a listed material; window glass gets a rougher
            // copy (a flashlight or lamp left a hot specular orb on the smooth glTF glass).
            var glowOf = new Dictionary<Material, Entry>();
            foreach (var e in entries) if (e.glow && e.instance && !glowOf.ContainsKey(e.instance)) { e.glowRenderers = new List<Renderer>(); glowOf[e.instance] = e; }
            var copies = new HashSet<Material>(map.Values);
            var glass = new Dictionary<Material, Material>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    if (map.TryGetValue(mats[i], out var inst)) { mats[i] = inst; changed = true; }
                    if (glowOf.TryGetValue(mats[i], out var ge))
                    {
                        ge.glowRenderers.Add(r);
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                        continue;
                    }
                    if (IsGlass(mats[i]))
                    {
                        if (copies.Contains(mats[i])) { Roughen(mats[i]); continue; } // a state copy: ours to change
                        if (!glass.TryGetValue(mats[i], out var g)) { g = new Material(mats[i]) { name = mats[i].name + " (glass)" }; Roughen(g); glass[mats[i]] = g; _extra.Add(g); }
                        mats[i] = g; changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
            GlassCount = glass.Count;
            foreach (var e in entries) if (e.instance) Write(e);
        }

        // ---------------------------------------------------------------- light pools and glass

        /// <summary>How many glass materials were roughened (for checks).</summary>
        public int GlassCount { get; private set; }

        /// <summary>The exported light pools (town-props.js poolMat: black, emissive, transparent, additive in three.js).</summary>
        public static bool IsGlowPool(int renderQueue, Color baseColor, Color emission) =>
            renderQueue >= 2500 && baseColor.maxColorComponent < 0.02f && emission.maxColorComponent > 0.01f;

        /// <summary>A pool's strength: the lit level, at full only once the sky is dark (about half at dusk).</summary>
        public static float PoolGlow(float level, float night) => EffectiveLevel(level) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(night));

        static Texture2D _poolTex;
        static Shader _glowShader;

        /// <summary>A soft radial falloff (town-props.js glowTex: a gaussian in linear light, no rim) in the alpha.</summary>
        public static Texture2D PoolTexture()
        {
            if (_poolTex) return _poolTex;
            const int W = 128;
            var px = new Color32[W * W];
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2 - 1, v = (y + 0.5f) / W * 2 - 1;
                    px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * PoolFalloff(u * u + v * v)));
                }
            _poolTex = new Texture2D(W, W, TextureFormat.RGBA32, true, true) { name = "PoolGlow", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            _poolTex.SetPixels32(px);
            _poolTex.Apply(true, true);
            return _poolTex;
        }

        /// <summary>The pool's falloff at squared radius r2 (0 centre, 1 edge): exp(-r2 / 0.18) (1 - r2), 0 outside.</summary>
        public static float PoolFalloff(float r2) => r2 < 1f ? Mathf.Exp(-r2 / 0.18f) * (1f - r2) : 0f;

        static Material GlowPoolMaterial(string name)
        {
            if (!_glowShader) _glowShader = SkyRig.LoadShader("AiOnAdditiveGlow", "AiOn/AdditiveGlow");
            if (!_glowShader) return null;
            var m = new Material(_glowShader) { name = name + " (pool)", renderQueue = 3000 };
            m.SetTexture(Prop.UrpBaseMap, PoolTexture());
            m.SetColor(Prop.UrpBaseColor, Color.black);
            return m;
        }

        /// <summary>See-through glass (transparent, mostly clear) that is shinier than a matte pane.</summary>
        static bool IsGlass(Material m)
        {
            if (m.renderQueue < 2500 || !m.HasProperty(Prop.BaseColorFactor) || !m.HasProperty(Prop.RoughnessFactor)) return false;
            var c = m.GetColor(Prop.BaseColorFactor);
            return c.a < 0.9f && c.maxColorComponent > 0.02f && m.GetFloat(Prop.RoughnessFactor) < GlassRoughness;
        }

        /// <summary>Window glass roughness (smoothness 0.65): a soft sheen instead of a hot specular orb. At 0.7 the rough,
        /// lit pane read as a milky veil over the view (302호's veranda doors, glowing almost white at night).</summary>
        public const float GlassRoughness = 0.35f;

        /// <summary>Most opacity a clear pane keeps (the glTF glass is 0.16: with the room light on it, a grey film).</summary>
        public const float GlassAlpha = 0.08f;

        static void Roughen(Material m)
        {
            if (m.HasProperty(Prop.RoughnessFactor)) m.SetFloat(Prop.RoughnessFactor, GlassRoughness);
            if (m.HasProperty(Prop.UrpSmoothness)) m.SetFloat(Prop.UrpSmoothness, 1f - GlassRoughness);
            if (m.HasProperty(Prop.BaseColorFactor))
            {
                var c = m.GetColor(Prop.BaseColorFactor);
                if (c.a > GlassAlpha) { c.a = GlassAlpha; m.SetColor(Prop.BaseColorFactor, c); }
            }
        }

        static Texture GetTex(Material m, int gltf, int urp) =>
            m.HasProperty(gltf) ? m.GetTexture(gltf) : m.HasProperty(urp) ? m.GetTexture(urp) : null;

        /// <summary>True when some material has rules for this state key.</summary>
        public bool Handles(string key)
        {
            if (!_ready) Init();
            return _keys.Contains(key);
        }

        /// <summary>A level state changed: each material with rules for the key takes the matching rule (or its default).</summary>
        readonly Dictionary<string, string> _state = new();

        /// <summary>The last value a state key was set to here (JSON text without quotes for a plain string; null if never).</summary>
        public string StateOf(string key) => _state.TryGetValue(key, out var v) ? v : null;

        public void Apply(string key, JToken value)
        {
            _state[key] = value == null ? null : value.Type == JTokenType.String ? (string)value : CanonicalJson(value);
            if (key == "windowView") EnvironmentController.Instance?.OutsideChanged();
            if (!Handles(key)) return;
            _order.Remove(key);
            _order.Add(key);
            foreach (var e in entries)
            {
                if (!e.instance || !e.byKey.TryGetValue(key, out var rules)) continue;
                int i = SelectRule(e.valuesByKey[key], value);
                if (i >= 0) e.Active[key] = rules[i]; else e.Active.Remove(key);
                Write(e);
            }
        }

        /// <summary>Lit levels per group (a group left out keeps its last level; a group never set is at 1, as exported).</summary>
        public void SetLit(IReadOnlyDictionary<string, float> levels)
        {
            if (levels == null) return;
            _changed.Clear();
            foreach (var kv in levels) Put(kv.Key, kv.Value);
            WriteChanged();
        }

        /// <summary>SetLit without boxing an enumerator (an env blend calls it every frame); only groups whose level moved are rewritten.</summary>
        public void SetLit(Dictionary<string, float> levels)
        {
            if (levels == null) return;
            _changed.Clear();
            foreach (var kv in levels) Put(kv.Key, kv.Value);
            WriteChanged();
        }

        readonly HashSet<string> _changed = new();

        void Put(string group, float level)
        {
            if (_levels.TryGetValue(group, out var was) && Mathf.Abs(was - level) < 1e-5f) return;
            _levels[group] = level;
            _changed.Add(group);
        }

        void WriteChanged()
        {
            if (!_ready || _changed.Count == 0) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.instance && !string.IsNullOrEmpty(e.group) && _changed.Contains(e.group)) Write(e);
            }
        }

        public float LevelOf(string group) =>
            string.IsNullOrEmpty(group) ? 1f : _levels.TryGetValue(group, out var v) ? v : 1f;

        /// <summary>The env's night amount changed: the ground pools follow.</summary>
        public void SetNight(float night)
        {
            if (Mathf.Abs(night - nightAmount) < 1e-4f) return;
            nightAmount = night;
            if (_ready) for (int i = 0; i < entries.Count; i++) { var e = entries[i]; if (e.glow && e.instance) Write(e); }
        }

        void Write(Entry e)
        {
            var s = Compose(e, Defaults(e), _order, e.Active);
            if (e.glow) { WriteGlow(e, s); return; }
            var (color, emission) = Finish(s.Color, s.Emission, string.IsNullOrEmpty(e.group) ? 1f : LevelOf(e.group), string.IsNullOrEmpty(e.group) ? -1f : e.dim);
            emission *= emissionGain;
            var m = e.instance;
            var g = color.gamma;
            if (m.HasProperty(Prop.BaseColorFactor)) m.SetVector(Prop.BaseColorFactor, new Vector4(g.r, g.g, g.b, s.Opacity)); // as glTFast: linear.gamma via SetVector
            else if (m.HasProperty(Prop.UrpBaseColor)) m.SetColor(Prop.UrpBaseColor, new Color(g.r, g.g, g.b, s.Opacity));

            emission.a = 1;
            bool glow = emission.maxColorComponent > 0f || s.EmissiveMap != null;
            // Encoded once: Unity linearises Color properties on upload, so the shader gets the linear emission.
            if (m.HasProperty(Prop.EmissiveFactor)) m.SetColor(Prop.EmissiveFactor, emission.gamma);
            else if (m.HasProperty(Prop.UrpEmissionColor)) m.SetColor(Prop.UrpEmissionColor, emission.gamma);
            if (glow) { m.EnableKeyword("_EMISSIVE"); m.EnableKeyword("_EMISSION"); }

            if (m.HasProperty(Prop.BaseColorTexture)) { if (m.GetTexture(Prop.BaseColorTexture) != s.Map) m.SetTexture(Prop.BaseColorTexture, s.Map); }
            else if (m.HasProperty(Prop.UrpBaseMap) && m.GetTexture(Prop.UrpBaseMap) != s.Map) m.SetTexture(Prop.UrpBaseMap, s.Map);
            if (m.HasProperty(Prop.EmissiveTexture)) { if (m.GetTexture(Prop.EmissiveTexture) != s.EmissiveMap) m.SetTexture(Prop.EmissiveTexture, s.EmissiveMap); }
            else if (m.HasProperty(Prop.UrpEmissionMap) && m.GetTexture(Prop.UrpEmissionMap) != s.EmissiveMap) m.SetTexture(Prop.UrpEmissionMap, s.EmissiveMap);
        }

        /// <summary>Tuning: the pool colour is the exported emission (emissive x its intensity) times this.</summary>
        public float poolGain = 1f;

        void WriteGlow(Entry e, MatState s)
        {
            float k = PoolGlow(LevelOf(e.group), nightAmount) * poolGain;
            var c = s.Emission * k;
            c.a = 1;
            e.instance.SetColor(Prop.UrpBaseColor, c.gamma); // linearised on upload
            bool on = k > LitEpsilon;
            if (e.glowRenderers != null) foreach (var r in e.glowRenderers) if (r && r.enabled != on) r.enabled = on;
        }

        static MatState Defaults(Entry e) => new MatState
        {
            Color = e.importedColor, Emission = e.importedEmission, Opacity = e.importedOpacity,
            Map = e.defaultMap, EmissiveMap = e.defaultEmissiveMap,
        };

        // ---------------------------------------------------------------- pure helpers (unit-tested)

        /// <summary>
        /// The rule for a state value, like LevelStateVariants: the exact JSON value wins; a list value takes the first
        /// element that has a rule ([element]); an object value takes the first field that has a rule ({field: v}).
        /// -1: no rule (the material goes back to its default for this key).
        /// </summary>
        public static int SelectRule(IReadOnlyList<string> ruleValues, JToken value)
        {
            if (ruleValues == null || ruleValues.Count == 0) return -1;
            int i = IndexOf(ruleValues, CanonicalJson(value));
            if (i >= 0) return i;
            if (value is JArray arr)
                foreach (var el in arr)
                {
                    i = IndexOf(ruleValues, CanonicalJson(new JArray(el.DeepClone())));
                    if (i >= 0) return i;
                }
            if (value is JObject obj)
                foreach (var p in obj.Properties())
                {
                    i = IndexOf(ruleValues, CanonicalJson(new JObject { [p.Name] = p.Value.DeepClone() }));
                    if (i >= 0) return i;
                }
            return -1;
        }

        static int IndexOf(IReadOnlyList<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return i;
            return -1;
        }

        /// <summary>Compact JSON with whole floats written as integers (2.0 -> 2), so 2 and 2.0 match.</summary>
        public static string CanonicalJson(JToken t) => t == null ? "null" : Canonical(t).ToString(Formatting.None);

        public static string CanonicalJson(string json)
        {
            if (json == null) return "null";
            try { return CanonicalJson(JToken.Parse(json)); }
            catch (JsonReaderException) { return json; }
        }

        static JToken Canonical(JToken t)
        {
            switch (t)
            {
                case JArray a:
                    { var r = new JArray(); foreach (var x in a) r.Add(Canonical(x)); return r; }
                case JObject o:
                    { var r = new JObject(); foreach (var p in o.Properties()) r[p.Name] = Canonical(p.Value); return r; }
                case JValue v when v.Type == JTokenType.Float:
                    {
                        double d = Convert.ToDouble(v.Value, CultureInfo.InvariantCulture);
                        if (Math.Abs(d) < 9e15 && Math.Floor(d) == d) return new JValue((long)d);
                        return new JValue(d);
                    }
                default:
                    return t.DeepClone();
            }
        }

        /// <summary>
        /// The lit levels after a preset (core.js litTarget/setEnv): groups the preset names take its value, except the
        /// grid groups (street, windows, shops, signs), which are 0 while the power is off; other groups keep theirs.
        /// </summary>
        public static Dictionary<string, float> MergeLit(IReadOnlyDictionary<string, float> previous, IReadOnlyDictionary<string, float> preset, bool power)
        {
            var next = new Dictionary<string, float>();
            if (previous != null) foreach (var kv in previous) next[kv.Key] = kv.Value;
            MergeLitInto(next, preset, power);
            return next;
        }

        /// <summary>MergeLit in place (no allocation once the groups are known): the preset's groups over levels.</summary>
        public static void MergeLitInto(Dictionary<string, float> levels, IReadOnlyDictionary<string, float> preset, bool power)
        {
            if (preset == null) return;
            foreach (var kv in preset)
                levels[kv.Key] = !power && Array.IndexOf(GridGroups, kv.Key) >= 0 ? 0f : kv.Value;
        }

        /// <summary>MergeLitInto over a concrete dictionary (its struct enumerator: no allocation per env-blend frame).</summary>
        public static void MergeLitInto(Dictionary<string, float> levels, Dictionary<string, float> preset, bool power)
        {
            if (preset == null) return;
            foreach (var kv in preset)
                levels[kv.Key] = !power && Array.IndexOf(GridGroups, kv.Key) >= 0 ? 0f : kv.Value;
        }

        /// <summary>kit.js put(): a group at or below 0.0005 is off (0).</summary>
        public static float EffectiveLevel(float level) => level > LitEpsilon ? level : 0f;

        /// <summary>kit.js put() for userData.unlitColor: the colour factor dim + (1 - dim) * min(1, level).</summary>
        public static float DimFactor(float dim, float level) => dim + (1f - dim) * Mathf.Min(1f, Mathf.Max(0f, level));

        /// <summary>Final colour and emission for a lit level: emission * level; a dim panel's colour * DimFactor (dim &lt; 0: none).</summary>
        public static (Color color, Color emission) Finish(Color color, Color emission, float level, float dim)
        {
            float lv = EffectiveLevel(level);
            var e = new Color(emission.r * lv, emission.g * lv, emission.b * lv, emission.a);
            if (dim >= 0)
            {
                float k = DimFactor(dim, lv);
                color = new Color(color.r * k, color.g * k, color.b * k, color.a);
            }
            return (color, e);
        }

        /// <summary>
        /// A state's linear value in the imported material's terms: per channel, imported * value / def where the
        /// exported default is non-zero (so the import's own encoding carries over), else the value itself.
        /// </summary>
        public static Color Retarget(Color imported, bool hasDef, Color def, Color value)
        {
            return new Color(
                Channel(imported.r, hasDef, def.r, value.r),
                Channel(imported.g, hasDef, def.g, value.g),
                Channel(imported.b, hasDef, def.b, value.b),
                value.a);
        }

        static float Channel(float imported, bool hasDef, float def, float value) =>
            hasDef && Mathf.Abs(def) > 1e-4f ? imported * (value / def) : value;

        /// <summary>One rule over a state (only what the rule lists).</summary>
        public static MatState Overlay(MatState s, Entry e, Rule r)
        {
            if (r.hasColor) s.Color = Retarget(e.importedColor, e.hasDef && e.hasDefColor, e.defColor, r.color);
            if (r.hasEmission) s.Emission = Retarget(e.importedEmission, e.hasDef && e.hasDefEmission, e.defEmission, r.emission);
            if (r.hasOpacity) s.Opacity = r.opacity;
            if (r.map) s.Map = r.map;
            if (r.emissiveMap) s.EmissiveMap = r.emissiveMap;
            return s;
        }

        /// <summary>The default overlaid by the active rules in the order their keys were last applied.</summary>
        public static MatState Compose(Entry e, MatState defaults, IReadOnlyList<string> order, IReadOnlyDictionary<string, Rule> active)
        {
            var s = defaults;
            if (order == null || active == null) return s;
            for (int i = 0; i < order.Count; i++) // by index: no boxed enumerator per write
                if (active.TryGetValue(order[i], out var r) && r != null) s = Overlay(s, e, r);
            return s;
        }

        /// <summary>Pixels (rows bottom to top, as Texture2D) mirrored vertically.</summary>
        public static Color32[] FlipRows(Color32[] px, int width, int height)
        {
            var o = new Color32[px.Length];
            for (int y = 0; y < height; y++) Array.Copy(px, y * width, o, (height - 1 - y) * width, width);
            return o;
        }

        /// <summary>Mean absolute channel difference (0..255, RGBA) of two same-sized pictures; -1 when sizes differ.</summary>
        public static float MeanAbsDiff(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return -1f;
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
            return (float)(sum / (a.Length * 4.0));
        }

        /// <summary>
        /// Parses an exported material name '&lt;tag&gt;~m&lt;n&gt;' (tag 'lit:&lt;group&gt;' or 'lit:&lt;group&gt;:dim=&lt;x&gt;', else any
        /// name). Trailing text after the digits (" (Instance)") is ignored. False when there is no '~m&lt;n&gt;'.
        /// </summary>
        public static bool ParseName(string name, out string group, out float dim, out int index)
        {
            group = ""; dim = -1; index = -1;
            if (string.IsNullOrEmpty(name)) return false;
            int at = name.LastIndexOf("~m", StringComparison.Ordinal);
            if (at < 0) return false;
            int j = at + 2, n = 0, digits = 0;
            while (j < name.Length && char.IsDigit(name[j]) && digits < 9) { n = n * 10 + (name[j] - '0'); j++; digits++; }
            if (digits == 0) return false;
            index = n;
            var tag = name.Substring(0, at);
            if (tag.StartsWith("lit:", StringComparison.Ordinal))
            {
                var parts = tag.Substring(4).Split(':');
                group = parts[0];
                for (int k = 1; k < parts.Length; k++)
                    if (parts[k].StartsWith("dim=", StringComparison.Ordinal) &&
                        float.TryParse(parts[k].Substring(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        dim = d;
            }
            return true;
        }
    }
}
