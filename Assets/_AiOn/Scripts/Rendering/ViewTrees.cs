using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Rendering
{
    /// <summary>
    /// Leafy crowns for the street trees seen from 302호's windows (visual QA). The apartment's window view (the level
    /// model's "apt-view") draws its trees as faceted icospheres in flat vertex colours, which read as plastic balls through
    /// the veranda glass in every 1장 and 3장 beat at home. Each lobe of a crown keeps a shrunken, darker core and gets a
    /// shell of crossed leaf cards around it, tinted with the lobe's own colour (autumn brown, yellow, green), so the trees
    /// read as foliage. The view is unlit and its state copy is dimmed by LevelMaterials at night, so the cards use an unlit
    /// cut-out material whose colour follows the original crown material's. Play mode only; the original mesh is only hidden.
    /// </summary>
    public sealed class ViewTrees : MonoBehaviour
    {
        /// <summary>Cards per lobe (crossed in pairs).</summary>
        public const int CardsPerLobe = 12;
        /// <summary>The lobe's core: this much of its radius, this much of its colour.</summary>
        public const float CoreScale = 0.72f, CoreShade = 0.55f;

        static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer _source;
        Material _sourceMat, _cards;
        readonly List<Object> _made = new();
        float _next;

        /// <summary>Adds the crowns to a level whose model carries a window view with icosphere trees (else nothing).</summary>
        public static void Attach(GameObject env)
        {
            var src = FindCrowns(env.scene);
            if (src == null) return;
            env.AddComponent<ViewTrees>().Build(src);
        }

        /// <summary>The crowns: an unlit vertex-coloured mesh of many small lobes under the level's "*-view" node.</summary>
        static MeshFilter FindCrowns(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var p = mf.transform.parent;
                    if (!p || !p.name.Contains("-view")) continue;
                    var m = mf.sharedMesh;
                    var r = mf.GetComponent<Renderer>();
                    if (!m || !r || !m.isReadable || m.vertexCount < 3000) continue;
                    var b = r.bounds;
                    if (b.size.y > 12f || b.size.x < 20f) continue; // a belt of crowns, not a building or the ground
                    if (m.colors.Length != m.vertexCount) continue;
                    var mat = r.sharedMaterial;
                    if (!mat || mat.shader.name.IndexOf("unlit", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    return mf;
                }
            return null;
        }

        void Build(MeshFilter src)
        {
            var mesh = src.sharedMesh;
            var verts = mesh.vertices;
            var cols = mesh.colors;
            var tris = mesh.triangles;
            var lobes = Lobes(verts, tris, 0.01f);
            if (lobes.Count < 4) return;

            var leafMat = Resources.Load<Material>("Surfaces/ViewLeaves");
            if (!leafMat) { Debug.LogWarning("[ViewTrees] Resources/Surfaces/ViewLeaves missing; the view keeps its round trees"); return; }

            _source = src.GetComponent<Renderer>();
            _sourceMat = _source.sharedMaterial;
            _cards = new Material(leafMat) { name = "ViewLeaves (runtime)" };
            _cards.SetTexture("_BaseMap", LeafTexture());
            _made.Add(_cards);

            var coreV = new List<Vector3>(); var coreC = new List<Color>(); var coreT = new List<int>();
            var cardV = new List<Vector3>(); var cardC = new List<Color>(); var cardUv = new List<Vector2>(); var cardT = new List<int>();
            int seed = 7;
            foreach (var lobe in lobes)
            {
                // centre, radius and colour of the lobe
                var c = Vector3.zero; var col = Color.black;
                foreach (int i in lobe.Verts) { c += verts[i]; col += cols[i]; }
                c /= lobe.Verts.Count; col /= lobe.Verts.Count; col.a = 1f;
                float rad = 0f;
                foreach (int i in lobe.Verts) rad = Mathf.Max(rad, (verts[i] - c).magnitude);

                // the core: the lobe's own triangles pulled in and shaded
                var map = new Dictionary<int, int>();
                foreach (int t in lobe.Tris)
                {
                    if (!map.TryGetValue(t, out int ni))
                    {
                        ni = coreV.Count; map[t] = ni;
                        coreV.Add(c + (verts[t] - c) * CoreScale);
                        var k = cols[t] * CoreShade; k.a = 1f; coreC.Add(k);
                    }
                    coreT.Add(ni);
                }

                // the shell: crossed cards through points in the lobe, facing every way
                for (int n = 0; n < CardsPerLobe; n += 2)
                {
                    var dir = Random01(ref seed) * 2f - Vector3.one;
                    var at = c + Vector3.ClampMagnitude(dir, 1f) * rad * 0.45f;
                    float size = rad * (0.95f + 0.4f * Hash(ref seed));
                    var rot = Quaternion.Euler(Hash(ref seed) * 50f - 25f, Hash(ref seed) * 360f, Hash(ref seed) * 360f);
                    var tint = col * (0.85f + 0.3f * Hash(ref seed)); tint.a = 1f;
                    Card(cardV, cardC, cardUv, cardT, at, rot, size, tint);
                    Card(cardV, cardC, cardUv, cardT, at, rot * Quaternion.Euler(0, 90, 0), size, tint);
                }
            }

            var parent = src.transform;
            _core = AddMesh("ViewTreeCores", parent, coreV, coreC, null, coreT, _sourceMat);
            AddMesh("ViewTreeLeaves", parent, cardV, cardC, cardUv, cardT, _cards);
            _source.enabled = false;
            Sync();
        }

        Renderer _core;

        Renderer AddMesh(string name, Transform parent, List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> t, Material mat)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetColors(c); if (uv != null) m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            _made.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static void Card(List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> t, Vector3 at, Quaternion rot, float size, Color tint)
        {
            int b = v.Count;
            float h = size * 0.5f;
            v.Add(at + rot * new Vector3(-h, -h, 0)); v.Add(at + rot * new Vector3(h, -h, 0));
            v.Add(at + rot * new Vector3(h, h, 0)); v.Add(at + rot * new Vector3(-h, h, 0));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            for (int i = 0; i < 4; i++) c.Add(tint);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); // double-sided by the material (cull off)
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Sync();
        }

        /// <summary>The cards follow the crown material's colour (LevelMaterials dims the view at night).</summary>
        void Sync()
        {
            // LevelMaterials may swap in its state copy after this was built: follow whatever the hidden crowns carry
            if (_source && _source.sharedMaterial != _sourceMat) { _sourceMat = _source.sharedMaterial; if (_core) _core.sharedMaterial = _sourceMat; }
            if (!_cards || !_sourceMat || !_sourceMat.HasProperty(BaseColorFactor)) return;
            _cards.SetVector(BaseColorId, _sourceMat.GetVector(BaseColorFactor));
        }

        void OnDestroy()
        {
            // not the shared leaf texture: it lives for the app (64 KB). Reloading 302호 from 302호 builds the new
            // instance's cards on it before this OnDestroy runs, so destroying it here left them as solid squares.
            foreach (var o in _made) if (o) Destroy(o);
        }

        // ---------- pure helpers (tested) ----------

        public sealed class Lobe { public readonly List<int> Verts = new(); public readonly List<int> Tris = new(); }

        /// <summary>
        /// The connected pieces of a triangle soup: triangles sharing a corner (positions welded to <paramref name="weld"/> m,
        /// as the export repeats each corner per face) belong to one lobe.
        /// </summary>
        public static List<Lobe> Lobes(Vector3[] verts, int[] tris, float weld)
        {
            var parent = new int[verts.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            var byPos = new Dictionary<Vector3Int, int>();
            float inv = 1f / Mathf.Max(1e-5f, weld);
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(verts[i].x * inv), Mathf.RoundToInt(verts[i].y * inv), Mathf.RoundToInt(verts[i].z * inv));
                if (byPos.TryGetValue(key, out int j)) Union(j, i); else byPos[key] = i;
            }
            for (int t = 0; t + 2 < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }
            var groups = new Dictionary<int, Lobe>();
            var seen = new HashSet<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int g = Find(tris[t]);
                if (!groups.TryGetValue(g, out var lobe)) groups[g] = lobe = new Lobe();
                for (int k = 0; k < 3; k++)
                {
                    lobe.Tris.Add(tris[t + k]);
                    if (seen.Add(tris[t + k])) lobe.Verts.Add(tris[t + k]);
                }
            }
            return new List<Lobe>(groups.Values);
        }

        static float Hash(ref int s)
        {
            unchecked { s = s * 1103515245 + 12345; }
            return ((s >> 8) & 0xFFFF) / 65535f;
        }

        static Vector3 Random01(ref int s) => new(Hash(ref s), Hash(ref s), Hash(ref s));

        static Texture2D _leaf;

        /// <summary>A clump of small leaves in light greys (the card's colour tints it), cut out by alpha; code-made, 128 px.
        /// Made once and shared by every instance for the app's lifetime (never destroyed with one).</summary>
        public static Texture2D LeafTexture()
        {
            if (_leaf) return _leaf;
            const int n = 128;
            var px = new Color32[n * n];
            int s = 91;
            for (int k = 0; k < 70; k++)
            {
                float cx = 0.1f + 0.8f * Hash(ref s), cy = 0.1f + 0.8f * Hash(ref s);
                float dx0 = cx - 0.5f, dy0 = cy - 0.5f;
                if (dx0 * dx0 + dy0 * dy0 > 0.2f) { cx = 0.5f + dx0 * 0.7f; cy = 0.5f + dy0 * 0.7f; } // a round clump
                float ang = Hash(ref s) * Mathf.PI * 2f;
                float len = (7f + 6f * Hash(ref s)) / n, wid = len * 0.5f;
                float tone = 0.62f + 0.38f * Hash(ref s);
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int x0 = Mathf.FloorToInt((cx - len) * n), x1 = Mathf.CeilToInt((cx + len) * n), y0 = Mathf.FloorToInt((cy - len) * n), y1 = Mathf.CeilToInt((cy + len) * n);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(n, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(n, x1); x++)
                    {
                        float dx = (x + 0.5f) / n - cx, dy = (y + 0.5f) / n - cy;
                        float a = (dx * ca + dy * sa) / len, b = (-dx * sa + dy * ca) / wid;
                        if (a * a + b * b > 1f) continue;
                        float v = tone * (Mathf.Abs(b) < 0.15f ? 0.88f : 1f) * (0.85f + 0.15f * (1f - a * a));
                        byte g = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(v));
                        px[y * n + x] = new Color32(g, g, g, 255);
                    }
            }
            _leaf = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = "ViewLeaves", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            _leaf.SetPixels32(px);
            _leaf.Apply(true, true);
            return _leaf;
        }
    }
}
