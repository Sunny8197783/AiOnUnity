// Generated textures and small decorations shared by the menu / full-screen ports: soft gradients, a radial glow,
// a rounded pill, and the scroll cue (thin scrollbar + bottom fade) for lists that run past their view.
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Pure numbers behind the generated textures (EditMode-testable).</summary>
    public static class DecorMath
    {
        /// <summary>Piecewise-linear alpha through (t, a) stops sorted by t; clamps outside the first and last.</summary>
        public static float Ramp(float t, float[] ts, float[] alphas)
        {
            if (t <= ts[0]) return alphas[0];
            for (int i = 1; i < ts.Length; i++)
                if (t <= ts[i]) return Mathf.Lerp(alphas[i - 1], alphas[i], (t - ts[i - 1]) / Mathf.Max(1e-6f, ts[i] - ts[i - 1]));
            return alphas[alphas.Length - 1];
        }

        /// <summary>
        /// The alpha that darkens as much as CSS alpha <paramref name="a"/> does: uGUI blends in linear space in a
        /// linear-colour project, where a .55 black veil leaves a bright window at about two thirds of its look.
        /// </summary>
        public static float Perceptual(float a, bool linear) => linear ? 1 - Mathf.Pow(1 - Mathf.Clamp01(a), 2.2f) : a;

        /// <summary>Opaque CSS-style mix of <paramref name="tint"/> over <paramref name="under"/> (sRGB values, alpha 1).</summary>
        public static Color Over(Color under, Color tint, float a)
        {
            var c = Color.Lerp(under, tint, Mathf.Clamp01(a)); c.a = 1; return c;
        }

        /// <summary>A soft disc: 1 inside <paramref name="core"/> (0..1 of the radius), smoothstep down to 0 at the rim.</summary>
        public static float Radial(float r, float core)
        {
            if (r >= 1) return 0;
            if (r <= core) return 1;
            float k = 1 - (r - core) / (1 - core);
            return k * k * (3 - 2 * k);
        }
    }

    /// <summary>Textures and sprites made once in code and cached for the session.</summary>
    public static class ScreenDecor
    {
        static Texture2D _landingH, _fadeUp;
        static Sprite _glowSprite, _moonSprite, _pill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // DontSave textures outlive play mode; with domain reload off the old ones would leak each session
            Kill(_landingH); Kill(_fadeUp);
            foreach (var s in new[] { _glowSprite, _moonSprite, _pill }) if (s) { Kill(s.texture); Kill(s); }
            _landingH = _fadeUp = null; _glowSprite = _moonSprite = _pill = null;
        }
        static void Kill(Object o) { if (o) Object.DestroyImmediate(o); }

        static Texture2D Strip(int w, int h, System.Func<float, float> alpha, bool horizontal)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            int n = horizontal ? w : h;
            var px = new Color32[w * h];
            for (int i = 0; i < n; i++)
            {
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha((i + 0.5f) / n)) * 255);
                if (horizontal) for (int y = 0; y < h; y++) px[y * w + i] = new Color32(255, 255, 255, a);
                else for (int x = 0; x < w; x++) px[i * w + x] = new Color32(255, 255, 255, a);
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>The landing shade, left to right: .92 at the left edge, .62 at 46%, .12 at the right edge.</summary>
        public static Texture2D LandingShade => _landingH ? _landingH : _landingH = Strip(256, 1, t => A(DecorMath.Ramp(t, new[] { 0f, 0.46f, 1f }, new[] { 0.92f, 0.62f, 0.12f })), true);

        /// <summary>CSS-like alpha for this project's colour space (see DecorMath.Perceptual).</summary>
        public static float A(float cssAlpha) => DecorMath.Perceptual(cssAlpha, QualitySettings.activeColorSpace == ColorSpace.Linear);

        /// <summary>
        /// A light-on-dark tint as an opaque colour: <paramref name="under"/> mixed toward <paramref name="tint"/> by the
        /// CSS alpha, the way the browser shows it. A translucent light fill blended in linear space reads two to three
        /// times as strong (an amber .10 box turns mustard), so faint fills over a known panel are pre-mixed instead.
        /// </summary>
        public static Color Over(Color under, Color tint, float cssAlpha) => DecorMath.Over(under, tint, cssAlpha);

        /// <summary>Bottom-up fade: alpha 1 at the bottom row (v = 0) to 0 at the top, eased.</summary>
        public static Texture2D FadeUp => _fadeUp ? _fadeUp : _fadeUp = Strip(1, 128, v => { float k = 1 - v; return k * k * (3 - 2 * k); }, false);

        static Sprite Disc(float core)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(DecorMath.Radial(Mathf.Sqrt(dx * dx + dy * dy), core) * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>A soft glow (falls off from the centre).</summary>
        public static Sprite Glow => _glowSprite ? _glowSprite : _glowSprite = Disc(0f);
        /// <summary>A round disc with a soft one-pixel rim (the moon).</summary>
        public static Sprite Moon => _moonSprite ? _moonSprite : _moonSprite = Disc(0.86f);

        /// <summary>A rounded pill / rounded rectangle (radius 12 px), 9-sliced.</summary>
        public static Sprite Pill
        {
            get
            {
                if (_pill) return _pill;
                const int n = 32, r = 12;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(r - d + 0.5f) * 255));
                    }
                t.SetPixels32(px);
                t.Apply(false, true);
                return _pill = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            }
        }

        /// <summary>A rounded box (Pill sprite, sliced).</summary>
        public static Image Rounded(string name, Transform parent, Color color, bool raycast = false)
        {
            var img = UiKit.Box(name, parent, color, raycast);
            img.sprite = Pill; img.type = Image.Type.Sliced;
            return img;
        }

        /// <summary>A full-rect RawImage with a texture, tinted.</summary>
        public static RawImage Raw(string name, Transform parent, Texture tex, Color tint)
        {
            var rt = UiKit.Rect(name, parent);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex; img.color = tint; img.raycastTarget = false;
            return img;
        }
    }

    /// <summary>
    /// The "more below" cue on a ScrollRect: a thin scrollbar on the right that shows only while the content runs past
    /// the view, and a fade at the bottom edge (in the panel's colour) that goes away once the end is reached.
    /// </summary>
    public sealed class ScrollCue : MonoBehaviour
    {
        ScrollRect _sr;
        RawImage _fade;
        Color _tint;
        float _lastAlpha = -1;

        /// <summary>
        /// Adds the cue to <paramref name="sr"/>; the bar and fade are drawn on the viewport's parent (unmasked). A
        /// negative barInset puts the bar outside the view's right edge (clear of full-width rows such as sliders).
        /// </summary>
        public static ScrollCue Attach(ScrollRect sr, Color panel, float fadeHeight = 64, float barInset = 2)
        {
            var host = (RectTransform)sr.transform.parent;
            var cue = sr.gameObject.AddComponent<ScrollCue>();
            cue._sr = sr;
            cue._tint = new Color(panel.r, panel.g, panel.b, 1);

            cue._fade = ScreenDecor.Raw("MoreFade", host, ScreenDecor.FadeUp, cue._tint);
            var f = cue._fade.rectTransform;
            f.anchorMin = new Vector2(0, 0); f.anchorMax = new Vector2(1, 0); f.pivot = new Vector2(0.5f, 0);
            f.offsetMin = new Vector2(0, 0); f.offsetMax = new Vector2(barInset >= 0 ? -10 : 0, fadeHeight);

            var track = UiKit.Box("Scrollbar", host, new Color(1, 1, 1, 0.06f), raycast: true);
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(1, 0); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(1, 0.5f);
            tr.offsetMin = new Vector2(-6 - barInset, 4); tr.offsetMax = new Vector2(-barInset, -4);
            var area = UiKit.Rect("Sliding Area", tr); UiKit.Fill(area);
            var handle = UiKit.Box("Handle", area, new Color(1, 1, 1, 0.34f), raycast: true);
            handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
            var sb = track.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle.rectTransform; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;
            var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide; // the ScrollRect shows it only when the content runs past the view
            cue.LateUpdate();
            return cue;
        }

        void LateUpdate()
        {
            if (!_sr || !_sr.content || !_sr.viewport) return;
            float view = _sr.viewport.rect.height, content = _sr.content.rect.height;
            float rest = content - view - _sr.content.anchoredPosition.y; // pixels still below the view
            bool over = content > view + 1;
            float a = over ? Mathf.Clamp01(rest / 48f) : 0;
            if (!Mathf.Approximately(a, _lastAlpha))
            {
                _lastAlpha = a;
                _fade.color = new Color(_tint.r, _tint.g, _tint.b, a);
                _fade.enabled = a > 0.001f;
            }
        }
    }
}
