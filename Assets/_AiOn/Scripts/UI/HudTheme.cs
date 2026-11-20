using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The in-game HUD's look in one place: translucent dark slate plates (#0F172A at 75 %) with rounded corners at the
    /// screen edges, a three-step type scale (objective title > values > labels) and the status colours. The world stays
    /// visible through the plates; text carries a soft shadow so it holds on a pale wall.
    /// </summary>
    public static class HudTheme
    {
        // #0F172A. The design value is 75 %; uGUI blends in linear space, where 75 % over a lit ceiling comes out a milky
        // mid-grey, so the plate is drawn at 86 %, which reads as the 75 % of a design tool.
        public static readonly Color Slate = new Color32(0x0F, 0x17, 0x2A, 219);
        public static readonly Color SlateDense = new Color32(0x0F, 0x17, 0x2A, 224); // captions and toasts: text first
        public static readonly Color Hairline = new(1f, 1f, 1f, 0.10f);
        public static readonly Color Track = new(1f, 1f, 1f, 0.14f);
        public static readonly Color Text = new Color32(0xF1, 0xF5, 0xF9, 255);
        public static readonly Color Muted = new Color32(0xA3, 0xB1, 0xC2, 255);
        public static readonly Color Accent = UiKit.Teal;
        public static readonly Color Warn = UiKit.Amber;
        public static readonly Color Danger = new Color32(0xF8, 0x71, 0x71, 255);

        // type scale (reference height 1080)
        public const int Eyebrow = 15, Label = 16, Value = 18, Body = 18, Title = 26;
        /// <summary>Distance of every plate from the screen edge.</summary>
        public const float Edge = 28f;

        /// <summary>A rounded slate plate.</summary>
        public static Image Plate(string name, Transform parent, bool dense = false)
        {
            var img = UiKit.Box(name, parent, dense ? SlateDense : Slate);
            Round(img);
            return img;
        }

        public static void Round(Image img)
        {
            img.sprite = HudDraw.RoundedSprite;
            img.type = Image.Type.Sliced;
        }

        /// <summary>A soft drop shadow so light text holds over a bright view behind a translucent plate.</summary>
        public static T Shadowed<T>(T g) where T : Graphic
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.6f);
            sh.effectDistance = new Vector2(1f, -1f);
            return g;
        }

        /// <summary>Good / middling / bad by the word the rules give a stat.</summary>
        public static Color StatColor(string word) => word == "좋음" ? Accent : word == "보통" ? Warn : Danger;

        static Sprite _gradient;

        /// <summary>A white bar that is a little brighter towards its right end (a meter's leading edge).</summary>
        public static Sprite GradientSprite
        {
            get
            {
                if (_gradient) return _gradient;
                const int n = 64;
                var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HudGradient" };
                for (int x = 0; x < n; x++)
                {
                    float k = x / (n - 1f);
                    float v = Mathf.Lerp(0.72f, 1f, k * k);
                    t.SetPixel(x, 0, new Color(v, v, v, 1));
                }
                t.Apply();
                return _gradient = Sprite.Create(t, new Rect(0, 0, n, 1), new Vector2(0.5f, 0.5f));
            }
        }
    }

    /// <summary>Easing and timing numbers behind the HUD's motion (EditMode-tested).</summary>
    public static class HudMotion
    {
        /// <summary>Ease-out-back: overshoots a little, then settles (a pop).</summary>
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float OutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }

        /// <summary>Exponential approach of current to target (frame-rate independent).</summary>
        public static float Approach(float current, float target, float rate, float dt) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        /// <summary>
        /// On-screen size (canvas units at reference height) of something metres wide seen from distance metres with a
        /// vertical field of view of fovDeg, clamped to [min, max].
        /// </summary>
        public static float ProjectedSize(float metres, float distance, float fovDeg, float referenceHeight, float min, float max)
        {
            float half = Mathf.Tan(Mathf.Clamp(fovDeg, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.05f, distance);
            return Mathf.Clamp(metres / half * referenceHeight * 0.5f, min, max);
        }

        /// <summary>The trailing "damage" bar: holds for a moment after a loss, then drains towards the real value.</summary>
        public static float Trail(float trail, float value, float sinceLoss, float dt)
        {
            if (trail <= value) return value;
            return sinceLoss < 0.35f ? trail : Mathf.MoveTowards(trail, value, dt * 0.45f);
        }
    }

    /// <summary>
    /// The centre reticle: a small dot at rest that opens into a ring when something can be used, and dips for a moment
    /// when it is used. Drawn as one mesh.
    /// </summary>
    public sealed class HudCrosshair : MaskableGraphic
    {
        float _open, _press;
        bool _target;

        /// <summary>Something is in reach.</summary>
        public bool Target { get => _target; set => _target = value; }

        /// <summary>The use key went down: the ring dips and springs back.</summary>
        public void Press() { _press = 1f; }

        /// <summary>Advances the animation; reduce: snap, no spring.</summary>
        public void Tick(float dt, bool reduce)
        {
            float want = _target ? 1f : 0f;
            float open = reduce ? want : HudMotion.Approach(_open, want, _target ? 16f : 22f, dt); // out faster than in
            float press = reduce ? 0f : Mathf.MoveTowards(_press, 0f, dt / 0.18f);
            if (Mathf.Abs(open - _open) > 0.0005f || press != _press) { _open = open; _press = press; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 shadow = new Color(0, 0, 0, 0.45f * color.a);
            Color dotC = Color.Lerp(new Color(1, 1, 1, 0.75f), HudTheme.Accent, _open); dotC.a *= color.a;
            float dot = Mathf.Lerp(2.6f, 2.2f, _open);
            HudDraw.Disc(vh, 0, 0, dot + 1.2f, shadow, 16);
            HudDraw.Disc(vh, 0, 0, dot, dotC, 16);
            if (_open <= 0.01f) return;
            // ring: grows from the dot, dips when pressed
            float r = Mathf.Lerp(4f, 13f, HudMotion.OutBack(_open)) * (1f - 0.22f * _press);
            Color ringC = HudTheme.Accent; ringC.a = _open * 0.95f * color.a;
            Ring(vh, r + 1.2f, 3.6f, new Color(0, 0, 0, 0.35f * _open * color.a));
            Ring(vh, r, 1.8f, ringC);
        }

        static void Ring(VertexHelper vh, float r, float w, Color32 c)
        {
            const int seg = 40;
            int i0 = vh.currentVertCount;
            float r0 = r - w * 0.5f, r1 = r + w * 0.5f;
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2 / seg, cx = Mathf.Cos(a), sy = Mathf.Sin(a);
                vh.AddVert(new Vector3(cx * r0, sy * r0), c, Vector4.zero);
                vh.AddVert(new Vector3(cx * r1, sy * r1), c, Vector4.zero);
                if (s == 0) continue;
                int i = i0 + s * 2;
                vh.AddTriangle(i - 2, i - 1, i + 1);
                vh.AddTriangle(i - 2, i + 1, i);
            }
        }
    }

    /// <summary>Four corner brackets around the thing in reach, sized to it on screen.</summary>
    public sealed class HudTargetMarker : MaskableGraphic
    {
        float _size = 60, _k;

        /// <summary>size: the box side in canvas units; k: 0 (just appeared, wide) .. 1 (settled).</summary>
        public void Set(float size, float k)
        {
            if (Mathf.Abs(size - _size) < 0.25f && Mathf.Abs(k - _k) < 0.002f) return;
            _size = size; _k = k;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float half = _size * 0.5f * Mathf.Lerp(1.35f, 1f, HudMotion.OutCubic(_k)); // closes in on the target
            float arm = Mathf.Clamp(_size * 0.22f, 8f, 20f);
            const float w = 2f;
            Color c = HudTheme.Accent; c.a = 0.9f * _k * color.a;
            Color32 s = new Color(0, 0, 0, 0.4f * _k * color.a);
            for (int pass = 0; pass < 2; pass++)
            {
                Color32 col = pass == 0 ? s : (Color32)c;
                float o = pass == 0 ? 1f : 0f; // the shadow is 1 px fatter
                foreach (var (sx, sy) in new[] { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) })
                {
                    float x = sx * half, y = sy * half;
                    HudDraw.Quad(vh, Mathf.Min(x, x - sx * arm) - o, y - w * 0.5f - o, Mathf.Max(x, x - sx * arm) + o, y + w * 0.5f + o, col);
                    HudDraw.Quad(vh, x - w * 0.5f - o, Mathf.Min(y, y - sy * arm) - o, x + w * 0.5f + o, Mathf.Max(y, y - sy * arm) + o, col);
                }
            }
        }
    }
}
