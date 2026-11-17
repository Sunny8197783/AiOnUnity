using System;
using System.Collections.Generic;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Shared mesh helpers and sprites for the compass and minimap.</summary>
    static class HudDraw
    {
        public static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c) => Quad(vh, x0, y0, x1, y1, c, c, c, c);

        /// <summary>A quad with its own colour per corner (bottom-left, top-left, top-right, bottom-right).</summary>
        public static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 bl, Color32 tl, Color32 tr, Color32 br)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), bl, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), tl, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), tr, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), br, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, col, Vector4.zero); vh.AddVert(b, col, Vector4.zero); vh.AddVert(c, col, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>A filled disc as a fan (dots on the map).</summary>
        public static void Disc(VertexHelper vh, float cx, float cy, float r, Color32 col, int seg = 12)
        {
            int c0 = vh.currentVertCount;
            vh.AddVert(new Vector3(cx, cy), col, Vector4.zero);
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2 / seg;
                vh.AddVert(new Vector3(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r), col, Vector4.zero);
                if (s > 0) vh.AddTriangle(c0, c0 + s, c0 + s + 1);
            }
        }

        static Sprite _disc, _ring;

        /// <summary>An anti-aliased white disc (mask, badges).</summary>
        public static Sprite DiscSprite => _disc ? _disc : _disc = Make(r => Mathf.Clamp01(r));

        /// <summary>An anti-aliased white ring along the edge (the map's rim).</summary>
        public static Sprite RingSprite => _ring ? _ring : _ring = Make(r => Mathf.Clamp01(r) * Mathf.Clamp01(3.5f - r));

        // f(d): alpha from d = pixels inside the edge (negative outside).
        static Sprite Make(System.Func<float, float> f)
        {
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HudCircle" };
            var px = new Color32[n * n];
            float c = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float inside = c - Mathf.Sqrt(dx * dx + dy * dy); // pixels in from the edge
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(f(inside) * 255));
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>
    /// The compass strip (original ui/hud.js setCompass): a soft dark band, 15° ticks, 북 N / 북동 / 동 E ... names and an
    /// amber notch at the heading. Sized by its RectTransform (the original was 360 x 34 px).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class HudCompass : MaskableGraphic
    {
        static readonly Color32 North = new(255, 90, 74, 255), Cardinal = new(242, 179, 61, 255), Minor = new(201, 211, 214, 255);
        const int Pool = 5; // 160° shown, a name every 45°: never more than 4
        readonly Text[] _labels = new Text[Pool];
        double _heading;
        bool _built;

        float S => rectTransform.rect.width / 360f;

        void Build()
        {
            if (_built) return;
            _built = true;
            for (int i = 0; i < Pool; i++)
            {
                var t = UiKit.Label("Name" + i, transform, "", 18, Minor, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(80, 30);
                var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(1, -1);
                t.gameObject.SetActive(false);
                _labels[i] = t;
            }
        }

        /// <summary>deg: heading clockwise from north.</summary>
        public void SetHeading(double deg)
        {
            Build();
            _heading = deg;
            float s = S, h = rectTransform.rect.height, half = rectTransform.rect.width / 2;
            int used = 0;
            for (int d = -HudMapMath.CompassSpan; d <= HudMapMath.CompassSpan; d++)
            {
                if (HudMapMath.CompassMark(deg, d, out int a, out double x) != HudMapMath.MarkKind.Name || used >= Pool) continue;
                var t = _labels[used++];
                bool big = HudMapMath.IsCardinal(a);
                t.text = HudMapMath.CompassName(a);
                t.fontSize = Mathf.RoundToInt((big ? 14 : 11) * s * 1.05f);
                Color c = HudMapMath.Wrap360(a) == 0 ? North : big ? Cardinal : Minor;
                float px = (float)x * s;
                c.a = Mathf.Clamp01((half - Mathf.Abs(px)) / (half * 0.22f)); // the names fade out with the band
                t.color = c;
                t.rectTransform.anchoredPosition = new Vector2(px, -1 * s);
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
            for (int i = used; i < Pool; i++) if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float s = r.width / 360f;
            Color32 clear = new(10, 15, 18, 0), band = new(10, 15, 18, 184);
            float xa = r.xMin + r.width * 0.15f, xb = r.xMin + r.width * 0.85f;
            HudDraw.Quad(vh, r.xMin, r.yMin, xa, r.yMax, clear, clear, band, band);
            HudDraw.Quad(vh, xa, r.yMin, xb, r.yMax, band);
            HudDraw.Quad(vh, xb, r.yMin, r.xMax, r.yMax, band, band, clear, clear);
            Color32 tick = new(220, 230, 232, 140);
            float cx = r.center.x;
            for (int d = -HudMapMath.CompassSpan; d <= HudMapMath.CompassSpan; d++)
            {
                if (HudMapMath.CompassMark(_heading, d, out _, out double x) != HudMapMath.MarkKind.Tick) continue;
                float px = cx + (float)x * s;
                HudDraw.Quad(vh, px - 0.6f * s, r.yMin + 3 * s, px + 0.6f * s, r.yMin + 9 * s, tick);
            }
            Color32 amber = new(242, 179, 61, 255);
            HudDraw.Tri(vh, new Vector2(cx - 5 * s, r.yMax), new Vector2(cx + 5 * s, r.yMax), new Vector2(cx, r.yMax - 6 * s), amber);
        }
    }

    /// <summary>
    /// The north-up minimap (original ui/hud.js setMap): the level's walk zones on this floor, its solids, the story's
    /// points (shelter red, doors teal, the rest amber) and the player's arrow at the centre. Lives under a disc Mask.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class HudMinimap : MaskableGraphic
    {
        struct Box { public float x0, y0, x1, y1; public Color32 c; }
        struct Dot { public float x, y, r; public Color32 c; }

        static readonly Color32 WalkC = new(150, 165, 170, 60), SolidC = new(205, 190, 160, 158);
        static readonly Color32[] PointC = { new(242, 179, 61, 255), new(60, 198, 181, 255), new(255, 107, 90, 255) };
        readonly List<Box> _boxes = new();
        readonly List<Dot> _dots = new();
        float _yaw;

        /// <summary>Redraws from the Director's map frame (x, y, z, yaw, points, scale; original frame) and the level's layout.</summary>
        public void SetMap(JObject m, LevelLayout layout, Func<LevelLayout.SolidBox, bool> solidOff = null, List<LevelLayout.SolidBox> extra = null)
        {
            _boxes.Clear(); _dots.Clear();
            float size = rectTransform.rect.width, ui = size / 184f, half = size / 2;
            double px = (double?)m["x"] ?? 0, py = (double?)m["y"] ?? 0, pz = (double?)m["z"] ?? 0;
            double k = ((double?)m["scale"] ?? HudMapMath.MapScale((string)m["level"])) * ui, R = half / k;
            _yaw = (float)((double?)m["yaw"] ?? 0);
            if (layout)
            {
                foreach (var w in layout.walk)
                    if (HudMapMath.Near(w.x0, w.x1, w.z0, w.z1, px, pz, R) && HudMapMath.WalkOnFloor(w, py)) Add(w.x0, w.x1, w.z0, w.z1, px, pz, k, WalkC, 0);
                foreach (var s in layout.solids)
                    if (s != null && HudMapMath.Near(s.x0, s.x1, s.z0, s.z1, px, pz, R) && HudMapMath.SolidShown(s, solidOff != null ? solidOff(s) : s.off, py))
                        Add(s.x0, s.x1, s.z0, s.z1, px, pz, k, SolidC, ui);
            }
            if (extra != null)
                foreach (var s in extra)
                    if (HudMapMath.SolidShown(s, py) && HudMapMath.Near(s.x0, s.x1, s.z0, s.z1, px, pz, R)) Add(s.x0, s.x1, s.z0, s.z1, px, pz, k, SolidC, ui);
            if (m["points"] is JArray pts)
                foreach (var t in pts)
                {
                    if (t is not JObject p) continue;
                    bool enabled = p["enabled"]?.Type != JTokenType.Boolean || (bool)p["enabled"];
                    if (!HudMapMath.PointShown(enabled, (double?)p["y"], py)) continue;
                    HudMapMath.ToMap((double?)p["x"] ?? 0, (double?)p["z"] ?? 0, px, pz, k, out float ux, out float uy);
                    if (Mathf.Abs(ux) > half + 4 || Mathf.Abs(uy) > half + 4) continue;
                    int style = HudMapMath.PointStyle((string)p["kind"]);
                    _dots.Add(new Dot { x = ux, y = uy, r = (style > 0 ? 3.6f : 2.6f) * ui, c = PointC[style] });
                }
            SetVerticesDirty();
        }

        void Add(float x0, float x1, float z0, float z1, double px, double pz, double k, Color32 c, float minPx)
        {
            HudMapMath.ToMap(x0, z0, px, pz, k, out float ax, out float ay);
            HudMapMath.ToMap(x1, z1, px, pz, k, out float bx, out float by);
            if (bx - ax < minPx) bx = ax + minPx;
            if (ay - by < minPx) by = ay - minPx;
            _boxes.Add(new Box { x0 = ax, y0 = by, x1 = bx, y1 = ay, c = c });
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var ctr = rectTransform.rect.center;
            float ui = rectTransform.rect.width / 184f;
            foreach (var b in _boxes) HudDraw.Quad(vh, ctr.x + b.x0, ctr.y + b.y0, ctr.x + b.x1, ctr.y + b.y1, b.c);
            foreach (var d in _dots) HudDraw.Disc(vh, ctr.x + d.x, ctr.y + d.y, d.r, d.c);
            // The arrow points up (north) at yaw 0 and turns left with positive yaw; a dark rim first, then the white body.
            float cs = Mathf.Cos(_yaw), sn = Mathf.Sin(_yaw);
            Arrow(vh, ctr, cs, sn, ui * 1.3f, new Color32(10, 15, 18, 255));
            Arrow(vh, ctr, cs, sn, ui, new Color32(255, 255, 255, 255));
        }

        static void Arrow(VertexHelper vh, Vector2 ctr, float cs, float sn, float k, Color32 c)
        {
            Vector2 tip = ctr + Rot(0, 9.5f, cs, sn) * k, back = ctr + Rot(0, -3, cs, sn) * k;
            HudDraw.Tri(vh, tip, ctr + Rot(6, -7, cs, sn) * k, back, c);
            HudDraw.Tri(vh, tip, back, ctr + Rot(-6, -7, cs, sn) * k, c);
        }

        static Vector2 Rot(float x, float y, float cs, float sn) => new(x * cs - y * sn, x * sn + y * cs);
    }
}
