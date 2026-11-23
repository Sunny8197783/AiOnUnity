// Procedural, tiling surface maps for the minigame studios (no image files in the repository): concrete, a woven straw
// mat, a plain weave fabric, painted plaster, wood grain, a steel door's brushed paint, cardboard. Each is a colour map
// and a normal map made from the same height field, built on first use (StudioMath noise) and kept until StudioShared
// lets go of them. Every noise term repeats a whole number of times over the map in each direction, so the maps tile
// without a seam (EditMode tests compare the height fields and colours across both edges).
using System;
using UnityEngine;

namespace AiOn.UI.Studio
{
    public static class StudioTextures
    {
        const int N = 256;

        static Texture2D _concrete, _concreteN, _straw, _strawN, _fabric, _fabricN, _plaster, _plasterN, _wood, _woodN, _card, _cardN, _tile, _tileN, _smoke;

        public static Texture2D Concrete { get { Make(ref _concrete, ref _concreteN, ConcreteH, ConcreteC, 2.2f); return _concrete; } }
        public static Texture2D ConcreteNormal { get { Make(ref _concrete, ref _concreteN, ConcreteH, ConcreteC, 2.2f); return _concreteN; } }
        public static Texture2D StrawMat { get { Make(ref _straw, ref _strawN, StrawH, StrawC, 5f); return _straw; } }
        public static Texture2D StrawMatNormal { get { Make(ref _straw, ref _strawN, StrawH, StrawC, 5f); return _strawN; } }
        public static Texture2D Fabric { get { Make(ref _fabric, ref _fabricN, FabricH, FabricC, 3f); return _fabric; } }
        public static Texture2D FabricNormal { get { Make(ref _fabric, ref _fabricN, FabricH, FabricC, 3f); return _fabricN; } }
        public static Texture2D Plaster { get { Make(ref _plaster, ref _plasterN, PlasterH, PlasterC, 1.4f); return _plaster; } }
        public static Texture2D PlasterNormal { get { Make(ref _plaster, ref _plasterN, PlasterH, PlasterC, 1.4f); return _plasterN; } }
        public static Texture2D Wood { get { Make(ref _wood, ref _woodN, WoodH, WoodC, 1.2f); return _wood; } }
        public static Texture2D WoodNormal { get { Make(ref _wood, ref _woodN, WoodH, WoodC, 1.2f); return _woodN; } }
        public static Texture2D Cardboard { get { Make(ref _card, ref _cardN, CardH, CardC, 2f); return _card; } }
        public static Texture2D CardboardNormal { get { Make(ref _card, ref _cardN, CardH, CardC, 2f); return _cardN; } }
        /// <summary>Square floor tiles (현관 / balcony), 4 x 4 per tile of the texture.</summary>
        public static Texture2D Tile { get { Make(ref _tile, ref _tileN, TileH, TileC, 3f); return _tile; } }
        public static Texture2D TileNormal { get { Make(ref _tile, ref _tileN, TileH, TileC, 3f); return _tileN; } }

        static Texture2D _sole, _leaves;

        /// <summary>
        /// A clump of small leaves (cut out by alpha): forty-odd overlapping ovals in a few greens, darker toward the
        /// middle, on crossed cards for the hill's scrub.
        /// </summary>
        public static Texture2D Leaves
        {
            get
            {
                if (_leaves) return _leaves;
                const int n = 128;
                var t = StudioShared.Keep(new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp }, "Leaves");
                var px = new Color32[n * n];
                for (int k = 0; k < 46; k++)
                {
                    // a leaf: centre within a rough dome, a random tilt, 9-16 px long
                    float cx = 0.15f + 0.7f * StudioMath.Noise(k * 1.31f, 3.7f, 0, 0, 81) , cy = 0.08f + 0.8f * StudioMath.Noise(5.1f, k * 1.77f, 0, 0, 82);
                    if (Mathf.Abs(cx - 0.5f) > 0.48f - cy * 0.25f) cx = 0.5f + (cx - 0.5f) * 0.6f;
                    float ang = StudioMath.Noise(k * 2.3f, 1.1f, 0, 0, 83) * Mathf.PI * 2f;
                    float len = (9f + 7f * StudioMath.Noise(k * 0.7f, 9.3f, 0, 0, 84)) / n, wid = len * 0.42f;
                    float tone = StudioMath.Noise(k * 3.1f, 2.9f, 0, 0, 85);
                    var col = Color.Lerp(new Color(0.12f, 0.22f, 0.08f), new Color(0.3f, 0.38f, 0.14f), tone) * (0.75f + 0.35f * cy);
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    int x0 = Mathf.FloorToInt((cx - len) * n), x1 = Mathf.CeilToInt((cx + len) * n), y0 = Mathf.FloorToInt((cy - len) * n), y1 = Mathf.CeilToInt((cy + len) * n);
                    for (int y = Mathf.Max(0, y0); y < Mathf.Min(n, y1); y++)
                        for (int x = Mathf.Max(0, x0); x < Mathf.Min(n, x1); x++)
                        {
                            float dx = (x + 0.5f) / n - cx, dy = (y + 0.5f) / n - cy;
                            float a = (dx * ca + dy * sa) / len, b = (-dx * sa + dy * ca) / wid;
                            if (a * a + b * b > 1f) continue;
                            float vein = Mathf.Abs(b) < 0.12f ? 0.85f : 1f;
                            px[y * n + x] = (Color32)new Color(col.r * vein, col.g * vein, col.b * vein, 1f);
                        }
                }
                t.SetPixels32(px);
                t.Apply(true, true);
                return _leaves = t;
            }
        }

        /// <summary>A shoe print (white, alpha): the ball and the heel joined at the waist, with tread grooves across; toe up.</summary>
        public static Texture2D Sole
        {
            get
            {
                if (_sole) return _sole;
                const int w = 96, h = 224;
                var t = StudioShared.Keep(new Texture2D(w, h, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp }, "Sole");
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                        // the outline: wide at the ball (v 0.7), narrow at the waist (v 0.4), round at the heel (v 0.15)
                        float half = v > 0.55f ? Mathf.Lerp(0.3f, 0.46f, Mathf.Sin(Mathf.Clamp01((v - 0.55f) / 0.4f) * Mathf.PI * 0.62f))
                                   : v > 0.3f ? Mathf.Lerp(0.33f, 0.3f, (v - 0.3f) / 0.25f) : 0.33f;
                        float cx = 0.5f + (v - 0.5f) * 0.06f; // a slight curve
                        float side = Mathf.Abs(u - cx) / half;
                        float ends = v > 0.82f ? (v - 0.82f) / 0.16f : v < 0.17f ? (0.17f - v) / 0.15f : 0f;
                        float d = Mathf.Sqrt(side * side + ends * ends); // < 1 inside
                        float edge = Mathf.Clamp01((1f - d) * 18f);
                        // tread: grooves across, a gap at the waist, patchy where the mud did not take
                        float groove = Mathf.Sin(v * 46f) > 0.6f ? 0.6f : 1f;
                        float waist = v > 0.34f && v < 0.5f ? 0.55f : 1f;
                        float patch = 0.65f + 0.35f * StudioMath.Fbm(u * 6, v * 12, 0, 3, 91);
                        byte a = (byte)(255 * edge * groove * waist * patch);
                        px[y * w + x] = new Color32(255, 255, 255, a);
                    }
                t.SetPixels32(px);
                t.Apply(true, true);
                return _sole = t;
            }
        }

        /// <summary>A soft round puff with a noisy edge (white, alpha falls off), for smoke cards and particles.</summary>
        public static Texture2D Smoke
        {
            get
            {
                if (_smoke) return _smoke;
                const int n = 128;
                var t = StudioShared.Keep(new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp }, "Smoke");
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float wob = StudioMath.Fbm(x / 16f, y / 16f, 8, 4, 7) - 0.5f;
                        float a = Mathf.Clamp01(1f - (r + wob * 0.45f));
                        a = a * a * (3 - 2 * a);
                        float shade = 0.82f + 0.18f * StudioMath.Fbm(x / 10f, y / 10f, 0, 3, 11);
                        byte c = (byte)(255 * shade);
                        px[y * n + x] = new Color32(c, c, c, (byte)(255 * a));
                    }
                t.SetPixels32(px);
                t.Apply(true, true);
                return _smoke = t;
            }
        }

        // ------------------------------------------------------------------ height fields (u, v in 0..1, tiling) and colours

        static float ConcreteH(float u, float v)
        {
            float big = StudioMath.Fbm(u * 4, v * 4, 4, 5, 1);
            float pits = StudioMath.Noise(u * 48, v * 48, 48, 5);
            return big * 0.7f + (pits > 0.86f ? -0.12f : 0f) + StudioMath.Noise(u * 128, v * 128, 128, 9) * 0.12f;
        }
        static Color ConcreteC(float u, float v, float h)
        {
            float stain = StudioMath.Fbm(u * 2, v * 2, 2, 4, 21);
            float g = 0.6f + (h - 0.45f) * 0.18f - stain * 0.08f;
            return new Color(g, g * 1.0f, g * 0.98f);
        }

        // woven straw: strips 1/16 wide in an over-under weave, each strip with fibres along it
        static float StrawH(float u, float v)
        {
            int cells = 16;
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            bool horiz = ((cx + cy) & 1) == 0;
            float along = horiz ? x : y, across = horiz ? y - cy : x - cx;
            float rib = Mathf.Sin(across * Mathf.PI);           // each strip rounded
            // along the strip: 32 cells over the map (so it repeats with it); across: inside one strip, never at an edge
            float fibre = (horiz ? StudioMath.Noise(along * 2, across * 40, 32, 40, 3) : StudioMath.Noise(across * 40, along * 2, 40, 32, 3)) * 0.25f;
            float bend = Mathf.Sin((horiz ? x - cx : y - cy) * Mathf.PI) * 0.3f; // rises over the crossing
            return rib * 0.55f + bend + fibre;
        }
        static Color StrawC(float u, float v, float h)
        {
            int cells = 16;
            int cx = Mathf.FloorToInt(u * cells), cy = Mathf.FloorToInt(v * cells);
            float tone = StudioMath.Noise(cx, cy, cells, cells, 13); // one tone per strip, repeating with the map
            var a = new Color(0.86f, 0.78f, 0.55f);
            var b = new Color(0.74f, 0.64f, 0.42f);
            var c = Color.Lerp(a, b, tone * 0.45f);
            // a woven stripe band once a tile, as on the usual mats
            float band = Mathf.Repeat(v, 1f);
            if (band > 0.90f && band < 0.96f) c = Color.Lerp(c, new Color(0.45f, 0.2f, 0.16f), 0.6f);
            return c * (0.8f + 0.25f * Mathf.Clamp01(h));
        }

        static float FabricH(float u, float v)
        {
            float x = u * 96, y = v * 96;
            float w = Mathf.Sin(x * Mathf.PI) * Mathf.Sin(y * Mathf.PI);
            return 0.5f + 0.25f * w + StudioMath.Fbm(u * 6, v * 6, 6, 3, 4) * 0.4f;
        }
        static Color FabricC(float u, float v, float h)
        {
            float g = 0.82f + 0.25f * (h - 0.5f) + (StudioMath.Noise(u * 300, v * 7, 300, 7, 2) - 0.5f) * 0.08f;
            return new Color(g, g, g);
        }

        static float PlasterH(float u, float v) => StudioMath.Fbm(u * 8, v * 8, 8, 5, 31) * 0.8f + StudioMath.Noise(u * 160, v * 160, 160, 3) * 0.2f;
        static Color PlasterC(float u, float v, float h)
        {
            float g = 0.9f + (h - 0.5f) * 0.12f - StudioMath.Fbm(u * 2, v * 2, 2, 3, 41) * 0.06f;
            return new Color(g, g * 0.985f, g * 0.95f);
        }

        // wood grain running along v (a door leaf's veneer): the warp's cells are long along the grain (3 across, 1 along)
        static float WoodH(float u, float v)
        {
            float warp = StudioMath.Fbm(u * 3, v, 3, 1, 4, 51) * 6f;
            float rings = Mathf.Repeat(u * 22 + warp, 1f);
            return Mathf.Pow(rings, 3f) * 0.5f + StudioMath.Noise(u * 220, v * 6, 220, 6, 52) * 0.25f;
        }
        static Color WoodC(float u, float v, float h)
        {
            var light = new Color(0.62f, 0.45f, 0.3f);
            var dark = new Color(0.38f, 0.25f, 0.16f);
            return Color.Lerp(light, dark, Mathf.Clamp01(h * 1.4f));
        }

        static float CardH(float u, float v)
        {
            float flute = Mathf.Sin(u * 60 * Mathf.PI * 2) * 0.08f;
            return 0.5f + flute + StudioMath.Fbm(u * 5, v * 5, 5, 4, 61) * 0.3f;
        }
        static Color CardC(float u, float v, float h)
        {
            float t = StudioMath.Fbm(u * 3, v * 3, 3, 4, 63);
            return Color.Lerp(new Color(0.72f, 0.55f, 0.36f), new Color(0.6f, 0.44f, 0.27f), t) * (0.9f + 0.2f * h);
        }

        static float TileH(float u, float v)
        {
            float x = Mathf.Repeat(u * 4, 1f), y = Mathf.Repeat(v * 4, 1f);
            float g = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
            float grout = Mathf.Clamp01(g / 0.025f);
            return grout * (0.75f + StudioMath.Fbm(u * 10, v * 10, 10, 3, 71) * 0.25f);
        }
        static Color TileC(float u, float v, float h)
        {
            int cx = Mathf.FloorToInt(u * 4), cy = Mathf.FloorToInt(v * 4);
            float tone = StudioMath.Noise(cx, cy, 4, 4, 72) * 0.08f; // one tone per tile, repeating with the map
            var tile = new Color(0.5f - tone, 0.48f - tone, 0.45f - tone);
            var grout = new Color(0.28f, 0.27f, 0.25f);
            return h < 0.3f ? grout : tile * (0.9f + 0.15f * StudioMath.Fbm(u * 16, v * 16, 16, 3, 73));
        }

        // ------------------------------------------------------------------ building

        /// <summary>The surfaces (for tests: each height field and colour must tile).</summary>
        public enum Surface { Concrete, Straw, Fabric, Plaster, Wood, Cardboard, Tile }

        public static float HeightOf(Surface s, float u, float v) => s switch
        {
            Surface.Concrete => ConcreteH(u, v), Surface.Straw => StrawH(u, v), Surface.Fabric => FabricH(u, v),
            Surface.Plaster => PlasterH(u, v), Surface.Wood => WoodH(u, v), Surface.Cardboard => CardH(u, v), _ => TileH(u, v),
        };

        public static Color ColourOf(Surface s, float u, float v)
        {
            float h = HeightOf(s, u, v);
            return s switch
            {
                Surface.Concrete => ConcreteC(u, v, h), Surface.Straw => StrawC(u, v, h), Surface.Fabric => FabricC(u, v, h),
                Surface.Plaster => PlasterC(u, v, h), Surface.Wood => WoodC(u, v, h), Surface.Cardboard => CardC(u, v, h), _ => TileC(u, v, h),
            };
        }

        static void Make(ref Texture2D color, ref Texture2D normal, Func<float, float, float> height, Func<float, float, float, Color> paint, float strength)
        {
            if (color && normal) return;
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    h[y * N + x] = height((x + 0.5f) / N, (y + 0.5f) / N);
            var c = new Color32[N * N];
            var n = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float hv = h[y * N + x];
                    c[y * N + x] = paint((x + 0.5f) / N, (y + 0.5f) / N, hv);
                    float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                    float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                    var nv = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    n[y * N + x] = new Color32((byte)((nv.x * 0.5f + 0.5f) * 255), (byte)((nv.y * 0.5f + 0.5f) * 255), (byte)((nv.z * 0.5f + 0.5f) * 255), 255);
                }
            color = StudioShared.Keep(new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 }, "Color");
            color.SetPixels32(c);
            color.Apply(true, true);
            normal = StudioShared.Keep(new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 }, "Normal");
            normal.SetPixels32(n);
            normal.Apply(true, true);
        }
    }
}
