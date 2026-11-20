using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The emergency terminal look shared by the survival guide and the learning report: a hand-held device frame on a
    /// deep navy screen (#0B0F19), amber (#F59E0B) for warnings, signal red (#EF4444) for "do not", emerald (#10B981) for
    /// safe actions. Colour never carries a meaning alone: every line also has a ✓ / ! / ✕ mark.
    /// </summary>
    public static class Terminal
    {
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        public static readonly Color Bg = Hex("#0B0F19");
        public static readonly Color Bezel = Hex("#05070C");
        public static readonly Color Plate = Hex("#131A29");
        public static readonly Color PlateHi = Hex("#1B2436");
        public static readonly Color Rule = new(1, 1, 1, 0.08f);
        public static readonly Color Ink = Hex("#E8EDF5");
        public static readonly Color Muted = Hex("#94A3B8");
        public static readonly Color Amber = Hex("#F59E0B");
        public static readonly Color Red = Hex("#EF4444");
        public static readonly Color Emerald = Hex("#10B981");
        public static readonly Color Sky = Hex("#60A5FA");

        public static Color ToneColor(string tone) => tone switch { "alert" => Red, "warn" => Amber, "safe" => Emerald, _ => Sky };
        public static Color KindColor(string kind) => kind switch { "never" => Red, "warn" => Amber, _ => Emerald };
        public static string KindIcon(string kind) => kind switch { "never" => "cross", "warn" => "warn", _ => "check" };
        public static string Rgb(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static Color Tint(Color c, float a) => new(c.r, c.g, c.b, a);

        public const float HeaderH = 68, Inset = 14;

        /// <summary>
        /// Turns a UiScreen window into the device: bezel, screen, a header (status dot, device name, title, signal and
        /// battery marks). Returns the body area under the header.
        /// </summary>
        public static RectTransform Frame(RectTransform window, string title, string status, Color statusColor)
        {
            var bezel = window.GetComponent<Image>();
            if (bezel) { bezel.color = Bezel; HudTheme.Round(bezel); }
            var screen = UiKit.Box("Screen", window, Bg);
            HudTheme.Round(screen);
            UiKit.Fill(screen.rectTransform, Inset, Inset);
            float w = window.sizeDelta.x - Inset * 2, h = window.sizeDelta.y - Inset * 2;

            var head = UiKit.Rect("Header", screen.transform); UiKit.At(head, 0, 0, w, HeaderH);
            var dot = UiKit.Box("Dot", head, statusColor); dot.sprite = HudDraw.DiscSprite; UiKit.At(dot.rectTransform, 28, 29, 10, 10);
            UiKit.At(UiKit.Label("Status", head, status, 16, statusColor, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 48, 8, 420, 22);
            UiKit.At(UiKit.Label("Title", head, title, 28, Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 48, 28, 900, 36);
            // signal bars and a battery, top right: it is a device in the hand
            for (int i = 0; i < 4; i++)
            {
                var bar = UiKit.Box("Sig" + i, head, i < 3 ? Muted : Tint(Muted, 0.3f)).rectTransform;
                UiKit.At(bar, w - 150 + i * 9, 40 - i * 5, 5, 8 + i * 5);
            }
            var batt = UiKit.Box("Batt", head, Tint(Muted, 0.35f)).rectTransform; UiKit.At(batt, w - 100, 30, 34, 16);
            UiKit.At(UiKit.Box("Charge", batt, Emerald).rectTransform, 2, 2, 20, 12);
            UiKit.At(UiKit.Label("Dev", head, "가상 훈련 단말", 15, Muted, TextAnchor.MiddleRight).rectTransform, w - 420, 28, 250, 20);
            UiKit.At(UiKit.Box("HeadRule", screen.transform, Tint(statusColor, 0.55f)).rectTransform, 0, HeaderH, w, 2);

            var body = UiKit.Rect("Body", screen.transform); UiKit.At(body, 0, HeaderH + 2, w, h - HeaderH - 2);
            return body;
        }

        public static TerminalIcon Icon(Transform parent, string kind, Color color, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var ic = go.AddComponent<TerminalIcon>();
            ic.Kind = kind; ic.color = color; ic.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            return ic;
        }

        /// <summary>A terminal button: plate fill, tone edge, a click on press and a tick on hover.</summary>
        public static Button Button(Transform parent, string label, Color tone, bool primary)
        {
            var b = UiKit.Button("Btn", parent, label, 22, primary ? tone : PlateHi, primary ? Bg : Ink);
            HudTheme.Round(b.GetComponent<Image>());
            var t = b.GetComponentInChildren<Text>(); if (t) t.fontStyle = FontStyle.Bold;
            var trig = b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => UiSound.Play("tick"));
            trig.triggers.Add(enter);
            b.onClick.AddListener(() => UiSound.Play("click"));
            return b;
        }
    }

    /// <summary>Flat line icons drawn as a mesh inside the rect (no font, no texture): the guide's and the report's marks.</summary>
    public sealed class TerminalIcon : MaskableGraphic
    {
        string _kind = "check";
        public string Kind { get => _kind; set { _kind = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float s = Mathf.Min(r.width, r.height) * 0.5f;
            Vector2 c = r.center;
            Color32 col = color, hole = Terminal.Bg;
            Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
            void Line(float x0, float y0, float x1, float y1, float w, Color32 k)
            {
                Vector2 a = P(x0, y0), b = P(x1, y1), n = (b - a).normalized; n = new Vector2(-n.y, n.x) * (w * s * 0.5f);
                int i = vh.currentVertCount;
                vh.AddVert(a - n, k, Vector4.zero); vh.AddVert(a + n, k, Vector4.zero); vh.AddVert(b + n, k, Vector4.zero); vh.AddVert(b - n, k, Vector4.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            }
            void Tri(float x0, float y0, float x1, float y1, float x2, float y2, Color32 k) => HudDraw.Tri(vh, P(x0, y0), P(x1, y1), P(x2, y2), k);
            void Box(float x0, float y0, float x1, float y1, Color32 k) { var a = P(x0, y0); var b = P(x1, y1); HudDraw.Quad(vh, a.x, a.y, b.x, b.y, k); }
            void Disc(float x, float y, float rad, Color32 k) { var p = P(x, y); HudDraw.Disc(vh, p.x, p.y, rad * s, k, 20); }

            switch (_kind)
            {
                case "check": Line(-0.7f, 0f, -0.2f, -0.5f, 0.28f, col); Line(-0.25f, -0.5f, 0.75f, 0.55f, 0.28f, col); break;
                case "cross": Line(-0.6f, -0.6f, 0.6f, 0.6f, 0.28f, col); Line(-0.6f, 0.6f, 0.6f, -0.6f, 0.28f, col); break;
                case "warn":
                case "siren":
                    Tri(-0.9f, -0.75f, 0f, 0.85f, 0.9f, -0.75f, col);
                    Box(-0.09f, -0.2f, 0.09f, 0.4f, hole); Box(-0.09f, -0.5f, 0.09f, -0.33f, hole);
                    break;
                case "power":
                    Tri(0.15f, 0.9f, -0.55f, -0.1f, 0.05f, -0.1f, col); Tri(-0.15f, -0.9f, 0.55f, 0.1f, -0.05f, 0.1f, col);
                    break;
                case "shelter":
                    Tri(-0.95f, 0.05f, 0f, 0.9f, 0.95f, 0.05f, col); Box(-0.65f, -0.8f, 0.65f, 0.05f, col);
                    Box(-0.18f, -0.8f, 0.18f, -0.2f, hole);
                    break;
                case "fire":
                    Disc(0f, -0.3f, 0.6f, col); Tri(-0.52f, 0f, 0.05f, 0.95f, 0.52f, 0f, col); Tri(0.1f, 0.1f, 0.55f, 0.6f, 0.58f, -0.2f, col);
                    Disc(0f, -0.42f, 0.26f, hole);
                    break;
                case "hazard":
                    Disc(0f, 0.45f, 0.36f, col); Disc(-0.42f, -0.3f, 0.36f, col); Disc(0.42f, -0.3f, 0.36f, col);
                    Disc(0f, -0.03f, 0.2f, hole); Disc(0f, -0.03f, 0.1f, col);
                    break;
                case "radio":
                    Box(-0.75f, -0.75f, 0.75f, 0.3f, col); Line(0.45f, 0.3f, 0.15f, 0.95f, 0.14f, col);
                    Disc(-0.3f, -0.22f, 0.28f, hole); Box(0.15f, -0.1f, 0.55f, 0.05f, hole); Box(0.15f, -0.4f, 0.55f, -0.25f, hole);
                    break;
                case "heart": // a heart with a pulse line through it (CPR)
                    Disc(-0.4f, 0.3f, 0.45f, col); Disc(0.4f, 0.3f, 0.45f, col); Tri(-0.83f, 0.17f, 0.83f, 0.17f, 0f, -0.85f, col);
                    Line(-0.75f, 0.05f, -0.3f, 0.05f, 0.12f, hole); Line(-0.3f, 0.05f, -0.12f, 0.4f, 0.12f, hole);
                    Line(-0.12f, 0.4f, 0.1f, -0.3f, 0.12f, hole); Line(0.1f, -0.3f, 0.28f, 0.05f, 0.12f, hole); Line(0.28f, 0.05f, 0.75f, 0.05f, 0.12f, hole);
                    break;
                case "clock":
                    Disc(0, 0, 0.9f, col); Disc(0, 0, 0.68f, hole); Line(0, 0, 0, 0.5f, 0.16f, col); Line(0, 0, 0.36f, -0.12f, 0.16f, col);
                    break;
                default: Disc(0, 0, 0.5f, col); break;
            }
        }
    }

    /// <summary>A ring gauge: a dim full ring and a tone arc from 12 o'clock clockwise for Value 0..1.</summary>
    public sealed class TerminalGauge : MaskableGraphic
    {
        float _value;
        public float Thickness = 16f;
        public Color TrackColor = new(1, 1, 1, 0.08f);
        public float Value { get => _value; set { value = Mathf.Clamp01(value); if (Mathf.Abs(value - _value) > 0.0005f) { _value = value; SetVerticesDirty(); } } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - Thickness * 0.5f;
            Arc(vh, r.center, rad, 0, 1, TrackColor);
            if (_value > 0.001f) Arc(vh, r.center, rad, 0, _value, color);
        }

        void Arc(VertexHelper vh, Vector2 c, float rad, float from, float to, Color32 col)
        {
            int seg = Mathf.Max(3, Mathf.CeilToInt(72 * (to - from)));
            int i0 = vh.currentVertCount;
            float r0 = rad - Thickness * 0.5f, r1 = rad + Thickness * 0.5f;
            for (int s = 0; s <= seg; s++)
            {
                float a = Mathf.PI * 0.5f - Mathf.Lerp(from, to, s / (float)seg) * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(c + d * r0, col, Vector4.zero); vh.AddVert(c + d * r1, col, Vector4.zero);
                if (s == 0) continue;
                int i = i0 + s * 2;
                vh.AddTriangle(i - 2, i - 1, i + 1); vh.AddTriangle(i - 2, i + 1, i);
            }
        }
    }

    /// <summary>A radar chart: rings and spokes for N axes, and the scores' polygon filled and outlined. Grow 0..1 animates it out from the centre.</summary>
    public sealed class TerminalRadar : MaskableGraphic
    {
        readonly List<float> _values = new();
        float _grow = 1f;
        public Color GridColor = new(1, 1, 1, 0.12f);
        public float Grow { get => _grow; set { value = Mathf.Clamp01(value); if (Mathf.Abs(value - _grow) > 0.0005f) { _grow = value; SetVerticesDirty(); } } }

        public void SetValues(IEnumerable<float> values) { _values.Clear(); _values.AddRange(values); SetVerticesDirty(); }

        /// <summary>Direction of axis i of n: the first points up, the rest go clockwise (pure; tested).</summary>
        public static Vector2 AxisDir(int i, int n)
        {
            float a = Mathf.PI * 0.5f - i * Mathf.PI * 2f / Mathf.Max(1, n);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = _values.Count;
            if (n < 3) return;
            var r = rectTransform.rect;
            float rad = Mathf.Min(r.width, r.height) * 0.5f;
            Vector2 c = r.center;
            void Seg(Vector2 a, Vector2 b, float w, Color32 k)
            {
                Vector2 d = (b - a).normalized, nn = new Vector2(-d.y, d.x) * (w * 0.5f);
                int i = vh.currentVertCount;
                vh.AddVert(a - nn, k, Vector4.zero); vh.AddVert(a + nn, k, Vector4.zero); vh.AddVert(b + nn, k, Vector4.zero); vh.AddVert(b - nn, k, Vector4.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            }
            for (int ring = 1; ring <= 4; ring++)
                for (int i = 0; i < n; i++)
                    Seg(c + AxisDir(i, n) * rad * ring / 4f, c + AxisDir((i + 1) % n, n) * rad * ring / 4f, ring == 4 ? 2f : 1f, GridColor);
            for (int i = 0; i < n; i++) Seg(c, c + AxisDir(i, n) * rad, 1f, GridColor);

            // the scores: a floor of 6 % so a zero axis still shows a corner
            Color32 fill = new Color(color.r, color.g, color.b, color.a * 0.28f);
            int c0 = vh.currentVertCount;
            vh.AddVert(c, fill, Vector4.zero);
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                pts[i] = c + AxisDir(i, n) * rad * Mathf.Max(0.06f, _values[i]) * _grow;
                vh.AddVert(pts[i], fill, Vector4.zero);
            }
            for (int i = 0; i < n; i++) vh.AddTriangle(c0, c0 + 1 + i, c0 + 1 + (i + 1) % n);
            for (int i = 0; i < n; i++) Seg(pts[i], pts[(i + 1) % n], 3f, color);
            for (int i = 0; i < n; i++) HudDraw.Disc(vh, pts[i].x, pts[i].y, 5f, color, 12);
        }
    }

    /// <summary>
    /// Fade and slide in, once, on unscaled time: the transition every terminal screen, card and choice uses. Under
    /// reduce-motion it only fades, quickly.
    /// </summary>
    public sealed class UiAppear : MonoBehaviour
    {
        public float delay, duration = 0.24f;
        public Vector2 from = new(0, -22);
        CanvasGroup _group;
        RectTransform _rt;
        Vector2 _home;
        float _t;
        bool _homeSet, _slide;

        /// <summary>Adds the transition to go. slide: move as well as fade (false for children of a layout group).</summary>
        public static UiAppear On(GameObject go, float delay = 0f, bool slide = true, float duration = 0.24f)
        {
            var a = go.AddComponent<UiAppear>();
            a.delay = delay; a._slide = slide; a.duration = duration;
            a._group = go.GetComponent<CanvasGroup>();
            if (!a._group) a._group = go.AddComponent<CanvasGroup>();
            if (Application.isPlaying) a._group.alpha = 0;
            return a;
        }

        /// <summary>Progress 0..1 at time t for a delay and a duration (pure; tested).</summary>
        public static float Progress(float t, float delay, float duration) => Mathf.Clamp01((t - delay) / Mathf.Max(0.001f, duration));

        void Update()
        {
            if (!_group) { enabled = false; return; }
            bool reduce = AiOn.Rendering.AtmosphereDirector.ReduceMotion;
            _t += Time.unscaledDeltaTime;
            float k = Progress(_t, reduce ? 0 : delay, reduce ? 0.1f : duration);
            float e = HudMotion.OutCubic(k);
            _group.alpha = e;
            if (_slide && !reduce)
            {
                if (!_rt) _rt = (RectTransform)transform;
                if (!_homeSet && k > 0) { _homeSet = true; _home = _rt.anchoredPosition; }
                if (_homeSet) _rt.anchoredPosition = _home + from * (1f - e);
            }
            if (k >= 1f) enabled = false;
        }
    }
}
