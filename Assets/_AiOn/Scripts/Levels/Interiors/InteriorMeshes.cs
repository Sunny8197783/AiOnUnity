using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Levels.Interiors
{
    /// <summary>
    /// Small procedural meshes and materials for the interior dressing: goods on the 베란다 수납장 (lathed bottles and
    /// cans, printed boxes), sensor-lamp glow domes and soft glow textures. A part list keeps one submesh per colour, so
    /// a kind's units combine into one mesh with a few lit materials.
    /// </summary>
    public sealed class PartMesh
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector3> N = new();
        public readonly List<List<int>> Sub = new();
        public readonly List<Color> Colors = new();
        public readonly List<float> Smooth = new();

        public int Mat(Color c, float smoothness)
        {
            for (int i = 0; i < Colors.Count; i++) if (Colors[i] == c && Mathf.Approximately(Smooth[i], smoothness)) return i;
            Colors.Add(c); Smooth.Add(smoothness); Sub.Add(new List<int>());
            return Colors.Count - 1;
        }

        void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int k = 0; k < 4; k++) N.Add(n);
            // wound so that n is the front face (Unity: a front triangle's Cross(b - a, c - a) points along its normal);
            // tested on the diagonals, which stay right when a == b (a lathe's pole)
            if (Vector3.Dot(Vector3.Cross(c - a, d - b), n) >= 0) Sub[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else Sub[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        /// <summary>A box with its base centred at c (size: x depth, y height, z length).</summary>
        public void Box(int sub, Vector3 c, Vector3 s)
        {
            float x0 = c.x - s.x / 2, x1 = c.x + s.x / 2, y0 = c.y, y1 = c.y + s.y, z0 = c.z - s.z / 2, z1 = c.z + s.z / 2;
            Quad(sub, new(x0, y0, z0), new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z1), Vector3.left);
            Quad(sub, new(x1, y0, z0), new(x1, y0, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.right);
            Quad(sub, new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), Vector3.up);
            Quad(sub, new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0), Vector3.down);
            Quad(sub, new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), Vector3.back);
            Quad(sub, new(x0, y0, z1), new(x0, y1, z1), new(x1, y1, z1), new(x1, y0, z1), Vector3.forward);
        }

        /// <summary>A flat rectangle on the box's front (-x) face plane at x, centred (y, z), w along z, h along y.</summary>
        public void FrontPatch(int sub, float x, float y, float z, float w, float h)
            => Quad(sub, new(x, y - h / 2, z - w / 2), new(x, y + h / 2, z - w / 2), new(x, y + h / 2, z + w / 2), new(x, y - h / 2, z + w / 2), Vector3.left);

        /// <summary>A lathe around y from a (radius, y) profile; each profile segment's submesh comes from subOf(y0, y1).</summary>
        public void Lathe(IReadOnlyList<Vector2> profile, int segments, System.Func<float, float, int> subOf)
        {
            for (int p = 0; p < profile.Count - 1; p++)
            {
                Vector2 a = profile[p], b = profile[p + 1];
                int sub = subOf(a.y, b.y);
                var d = b - a;
                var n2 = new Vector2(d.y, -d.x).normalized; // outward for a profile going up / out
                for (int s = 0; s < segments; s++)
                {
                    float t0 = s * Mathf.PI * 2 / segments, t1 = (s + 1) * Mathf.PI * 2 / segments;
                    var c0 = new Vector3(Mathf.Cos(t0), 0, Mathf.Sin(t0)); var c1 = new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1));
                    var n = ((c0 + c1) * 0.5f).normalized * n2.x + Vector3.up * n2.y;
                    Quad(sub, c0 * a.x + Vector3.up * a.y, c1 * a.x + Vector3.up * a.y, c1 * b.x + Vector3.up * b.y, c0 * b.x + Vector3.up * b.y, n.normalized);
                }
            }
        }

        /// <summary>
        /// The units of a template combined into one mesh (one submesh per colour). Each unit is (position, yaw degrees,
        /// scale) in Unity space.
        /// </summary>
        public Mesh Combine(Mesh into, IReadOnlyList<(Vector3 pos, float yawDeg, Vector3 scale)> units, int count)
        {
            if (!into) into = new Mesh { name = "Units" };
            into.Clear();
            int nv = V.Count * count;
            if (nv > 65000) into.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new Vector3[nv]; var norms = new Vector3[nv];
            for (int u = 0; u < count; u++)
            {
                var (pos, yaw, sc) = units[u];
                var q = Quaternion.Euler(0, yaw, 0);
                for (int i = 0; i < V.Count; i++)
                {
                    verts[u * V.Count + i] = pos + q * Vector3.Scale(V[i], sc);
                    norms[u * V.Count + i] = q * N[i];
                }
            }
            into.vertices = verts; into.normals = norms;
            into.subMeshCount = Sub.Count;
            for (int s = 0; s < Sub.Count; s++)
            {
                var src = Sub[s];
                var tris = new int[src.Count * count];
                for (int u = 0; u < count; u++)
                    for (int i = 0; i < src.Count; i++) tris[u * src.Count + i] = src[i] + u * V.Count;
                into.SetTriangles(tris, s);
            }
            into.RecalculateBounds();
            return into;
        }
    }

    public static class InteriorMeshes
    {
        static readonly Dictionary<(Color, float), Material> Lit = new();
        static Material _glowWhite;
        static Texture2D _soft, _flame;
        static Mesh _dome, _quad;

        public static Color Hex(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.magenta;

        public static Material LitMat(Color c, float smoothness)
        {
            if (Lit.TryGetValue((c, smoothness), out var m) && m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Stock_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            Lit[(c, smoothness)] = m;
            return m;
        }

        static Shader GlowShader()
        {
            var s = Resources.Load<Shader>("Shaders/AiOnAdditiveGlow");
            return s ? s : Shader.Find("AiOn/AdditiveGlow");
        }

        /// <summary>An additive glow material (Resources/Shaders/AiOnAdditiveGlow): colour (HDR) x the texture's alpha.</summary>
        public static Material Glow(string name, Color color, Texture2D mask)
        {
            var m = new Material(GlowShader()) { name = name };
            m.SetTexture("_BaseMap", mask ? mask : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", color);
            return m;
        }

        /// <summary>A soft round falloff (the candle halo, lamp-head glows).</summary>
        public static Texture2D Soft()
        {
            if (_soft) return _soft;
            const int n = 64;
            _soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftGlow" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color(1, 1, 1, (1 - d) * (1 - d) * 0.9f);
                }
            _soft.SetPixels(px); _soft.Apply();
            return _soft;
        }

        /// <summary>The candle flame (apartment-props.js drawFlame): a teardrop, bright at its lower middle.</summary>
        public static Texture2D Flame()
        {
            if (_flame) return _flame;
            const int w = 32, h = 64;
            _flame = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CandleFlame" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2 - 1, v = (y + 0.5f) / h; // v 0 bottom .. 1 tip
                    float half = v < 0.32f ? Mathf.Sqrt(Mathf.Clamp01(1 - Mathf.Pow((0.32f - v) / 0.32f, 2))) : Mathf.Pow(Mathf.Clamp01((1 - v) / 0.68f), 0.8f);
                    float edge = half <= 0 ? 0 : Mathf.Clamp01(1 - Mathf.Abs(u) / (half * 0.95f));
                    float core = Mathf.Clamp01(1 - new Vector2(u * 1.6f, (v - 0.3f) * 2.2f).magnitude);
                    px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(edge * 0.75f + core * 0.6f) * edge);
                }
            _flame.SetPixels(px); _flame.Apply();
            return _flame;
        }

        /// <summary>A sensor lamp's diffuser: a half sphere r 0.12 flattened to 0.45, hanging down from y 0.</summary>
        public static Mesh Dome()
        {
            if (_dome) return _dome;
            var pm = new PartMesh(); int s = pm.Mat(Color.white, 0);
            var prof = new List<Vector2>();
            for (int i = 6; i >= 0; i--) { float a = i / 6f * Mathf.PI / 2; prof.Add(new Vector2(Mathf.Cos(a) * 0.12f, -Mathf.Sin(a) * 0.12f * 0.45f)); }
            // the profile runs from the bottom pole out to the rim (outward normals point down/out)
            pm.Lathe(prof, 16, (a, b) => s);
            _dome = pm.Combine(null, new[] { (Vector3.zero, 0f, Vector3.one) }, 1);
            _dome.name = "SensorDome";
            return _dome;
        }

        /// <summary>A unit quad in the xy plane facing -z (for camera-facing sprites).</summary>
        public static Mesh Quad()
        {
            if (_quad) return _quad;
            _quad = new Mesh { name = "GlowQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateNormals(); _quad.RecalculateBounds();
            return _quad;
        }

        /// <summary>The template of one unit of a stock kind (Unity space, base at y 0, front face -x).</summary>
        public static PartMesh StockUnit(InteriorLogic.StockKind k)
        {
            var pm = new PartMesh();
            switch (k.Shape)
            {
                case InteriorLogic.StockShape.Bottle:
                {
                    int cap = pm.Mat(Hex("#1f5fbf"), 0.5f), label = pm.Mat(Hex("#2c7fd1"), 0.45f), clear = pm.Mat(Hex("#d4ecf6"), 0.92f);
                    var prof = new[] { (0f, 0f), (0.044f, 0f), (0.051f, 0.008f), (0.051f, 0.109f), (0.0512f, 0.11f), (0.0512f, 0.19f), (0.051f, 0.191f), (0.051f, 0.2f), (0.049f, 0.215f), (0.03f, 0.258f), (0.016f, 0.283f), (0.016f, 0.296f), (0.0172f, 0.297f), (0.0172f, 0.315f), (0f, 0.315f) };
                    pm.Lathe(System.Array.ConvertAll(prof, p => new Vector2(p.Item1, p.Item2)), 14,
                        (y0, y1) => { float y = (y0 + y1) / 2; return y > 0.2965f ? cap : y > 0.1095f && y < 0.1905f ? label : clear; });
                    break;
                }
                case InteriorLogic.StockShape.Can:
                {
                    int lid = pm.Mat(Hex("#c9cdd1"), 0.6f), body = pm.Mat(Hex("#d9a62c"), 0.55f);
                    pm.Lathe(new[] { new Vector2(0, 0), new Vector2(0.043f, 0), new Vector2(0.043f, 0.046f), new Vector2(0, 0.046f) }, 18,
                        (y0, y1) => Mathf.Approximately(y0, y1) ? lid : body);
                    break;
                }
                case InteriorLogic.StockShape.Butane:
                {
                    int grey = pm.Mat(Hex("#c9cdd1"), 0.6f), band = pm.Mat(Hex("#f4f1ea"), 0.5f), orange = pm.Mat(Hex("#e0661a"), 0.5f);
                    pm.Lathe(new[] { new Vector2(0, 0), new Vector2(0.033f, 0), new Vector2(0.033f, 0.08f), new Vector2(0.033f, 0.125f), new Vector2(0.033f, 0.185f), new Vector2(0.033f, 0.2f), new Vector2(0, 0.2f) }, 14,
                        (y0, y1) => { if (Mathf.Approximately(y0, y1)) return grey; float y = (y0 + y1) / 2; return y > 0.185f ? grey : y > 0.08f && y < 0.125f ? band : orange; });
                    break;
                }
                case InteriorLogic.StockShape.Roll:
                {
                    int paper = pm.Mat(Hex("#f7f7f4"), 0.05f);
                    pm.Lathe(new[] { new Vector2(0, 0), new Vector2(0.055f, 0), new Vector2(0.055f, 0.1f), new Vector2(0, 0.1f) }, 16, (a, b) => paper);
                    break;
                }
                default:
                {
                    // A printed box (the original drew the brand on a canvas): the background colour all round, the print as a
                    // band of the foreground colour on the front face (a red cross on the first-aid box).
                    int bg = pm.Mat(Hex(k.Bg), 0.35f), fg = pm.Mat(Hex(k.Fg), 0.35f);
                    var s = k.Size;
                    pm.Box(bg, Vector3.zero, s);
                    float fx = -s.x / 2 - 0.0008f;
                    if (k.Cross)
                    {
                        float a = Mathf.Min(s.y, s.z) * 0.55f, t = a * 0.32f;
                        pm.FrontPatch(fg, fx, s.y / 2, 0, a, t); pm.FrontPatch(fg, fx - 0.0002f, s.y / 2, 0, t, a);
                    }
                    else if (s.y >= 0.05f) pm.FrontPatch(fg, fx, s.y * 0.55f, 0, s.z * 0.72f, s.y * 0.28f);
                    else pm.FrontPatch(fg, fx, s.y * 0.5f, 0, s.z * 0.6f, s.y * 0.5f);
                    break;
                }
            }
            return pm;
        }
    }
}
