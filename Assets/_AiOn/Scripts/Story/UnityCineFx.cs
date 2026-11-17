// ICineFx on the Unity host (port of the overlay / fill light / missile parts of story/cine.js):
//   Overlay(on)   the "Space · 건너뛰기" hint over the letterbox (the bars themselves are StoryOverlay.SetCinematic,
//                 which StoryCine.Play already turns on through the HUD).
//   Title(lines)  typed-out place / time lines in the top-left corner under the top bar; TitleOut() fades them.
//   Fill(...)     a soft point light that stays on the subject (intensity 0 = off), position in the original frame.
//   Missile(...)  a white-hot head (unlit HDR, so bloom catches it) with a short fire trail and a long smoke trail,
//                 from -> to over seconds along a slight arc; the smoke lingers after impact; Dispose removes it all.
// The overlay canvas and the fill light live under the persistent host; a missile lives in the active (level) scene, so
// it dies with the level even if nobody disposes it.
using System;
using System.Collections.Generic;
using System.Globalization;
using AiOn.Levels;
using AiOn.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AiOn.Story
{
    public class UnityCineFx : ICineFx
    {
        // ------------------------------------------------------------------ pure logic (EditMode-tested)

        /// <summary>Seconds after Title() at which character j of line i appears (the original's 400 + i*700 + j*45 ms).</summary>
        public static double CharTime(int line, int index) => 0.4 + line * 0.7 + index * 0.045;

        /// <summary>How many characters of a line of `length` characters are typed `elapsed` seconds after Title().</summary>
        public static int TypedCount(int line, int length, double elapsed)
        {
            if (length <= 0) return 0;
            double t = elapsed - CharTime(line, 0);
            if (t < 0) return 0;
            return Math.Min(length, (int)Math.Floor(t / 0.045 + 1e-9) + 1);
        }

        /// <summary>The first `count` text elements of `text` (a surrogate pair or a combining sequence types as one).</summary>
        public static string TypedText(string text, int count)
        {
            if (string.IsNullOrEmpty(text) || count <= 0) return "";
            var si = new StringInfo(text);
            if (count >= si.LengthInTextElements) return text;
            return si.SubstringByTextElements(0, count);
        }

        public static int TextLength(string text) => string.IsNullOrEmpty(text) ? 0 : new StringInfo(text).LengthInTextElements;

        /// <summary>Unity light intensity for the original's three.js fill intensity (i ~ 1.6..9, decay 1.6, 14 m).</summary>
        public static float FillIntensity(double i) => i <= 0 ? 0f : (float)(i * 0.4);

        /// <summary>A '#rrggbb' colour; null or unreadable gives the fallback.</summary>
        public static Color ParseColor(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? c : fallback;

        /// <summary>
        /// The missile's position (original frame) at k = t / seconds in 0..1: accelerating like the original (k²) along
        /// the straight line, lifted by a slight arc (5% of the distance, at most 15 m) that is zero at both ends.
        /// </summary>
        public static double[] MissilePoint(double[] from, double[] to, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            double e = k * k;
            double dx = to[0] - from[0], dy = to[1] - from[1], dz = to[2] - from[2];
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double arc = Math.Min(15, dist * 0.05) * 4 * e * (1 - e);
            return new[] { from[0] + dx * e, from[1] + dy * e + arc, from[2] + dz * e };
        }

        // ------------------------------------------------------------------ host side

        public const float TitleSeconds = 1f; // .cine-title { transition: opacity 1s }

        readonly UnityStoryHost _host;
        CineOverlayUi _ui;
        Light _fill;
        static readonly Color FillDefault = new(0xdf / 255f, 0xe6 / 255f, 1f); // 0xdfe6ff

        // Parked where nothing sees it while off (the original adds its fill at (0,-500,0), intensity 0, up front).
        static Vector3 FillParked => LevelLayout.ToUnity(0, -500, 0);

        public UnityCineFx(UnityStoryHost host)
        {
            _host = host;
            var go = new GameObject("CineFill");
            if (Root) go.transform.SetParent(Root, false); else Object.DontDestroyOnLoad(go);
            go.transform.position = FillParked;
            _fill = go.AddComponent<Light>();
            _fill.type = LightType.Point;
            _fill.range = 14f;
            _fill.shadows = LightShadows.None;
            _fill.color = FillDefault;
            _fill.intensity = 0;
        }

        Transform Root => _host ? _host.transform : null;

        CineOverlayUi Ui
        {
            get
            {
                if (_ui) return _ui;
                var canvas = UiKit.ScreenCanvas("CineOverlay", 151); // just over StoryOverlay (150), under modal screens
                if (Root) canvas.transform.SetParent(Root, false); else Object.DontDestroyOnLoad(canvas.gameObject);
                Object.Destroy(canvas.GetComponent<GraphicRaycaster>()); // never takes clicks
                _ui = canvas.gameObject.AddComponent<CineOverlayUi>();
                _ui.Build();
                return _ui;
            }
        }

        public void Overlay(bool on) => Ui.SetOn(on);

        public void Title(IReadOnlyList<string> lines) => Ui.SetTitle(lines);

        public void TitleOut() { if (_ui) _ui.TitleOut(); }

        public void Fill(double x, double y, double z, double intensity, string color)
        {
            if (!_fill) return; // host torn down
            float i = FillIntensity(intensity);
            if (i <= 0) { _fill.intensity = 0; _fill.transform.position = FillParked; return; }
            _fill.transform.position = LevelLayout.ToUnity((float)x, (float)y, (float)z);
            _fill.intensity = i;
            if (!string.IsNullOrEmpty(color)) _fill.color = ParseColor(color, _fill.color);
        }

        public IDisposable Missile(double[] from, double[] to, double seconds)
        {
            if (from == null || to == null || from.Length < 3 || to.Length < 3) return new MissileHandle(null);
            var go = new GameObject("CineMissile"); // active scene: unloads with the level
            var m = go.AddComponent<CineMissile>();
            m.Launch(from, to, seconds > 0 ? seconds : 1);
            return new MissileHandle(m);
        }

        sealed class MissileHandle : IDisposable
        {
            CineMissile _m;
            public MissileHandle(CineMissile m) { _m = m; }
            public void Dispose()
            {
                if (_m) Object.Destroy(_m.gameObject);
                _m = null;
            }
        }
    }

    /// <summary>The cutscene overlay's skip hint and corner title (the letterbox bars are StoryOverlay's).</summary>
    public class CineOverlayUi : MonoBehaviour
    {
        // StoryOverlay's letterbox bars are 110 px on a 1080 p reference; the CSS puts the title 22 px under the top bar
        // at max(24px, 5vw) and the hint 34 px up inside the bottom bar at max(16px, 3vw).
        const float Bar = 110, Left = 96, Right = 58;
        static readonly Color TitleInk = new(0xf2 / 255f, 0xef / 255f, 0xe6 / 255f);
        static readonly Color SubInk = new(0xc9 / 255f, 0xc4 / 255f, 0xb6 / 255f);

        CanvasGroup _titleGroup, _skipGroup;
        RectTransform _titleBox;
        readonly List<Text> _lineTexts = new();
        string[] _lines = Array.Empty<string>();
        double _typedFor;          // seconds since Title()
        bool _on, _out;
        float _onFor;              // seconds since Overlay(true)

        public void Build()
        {
            var root = (RectTransform)transform;

            _titleBox = UiKit.Rect("CineTitle", root);
            UiKit.At(_titleBox, Left, Bar + 22, 1100, 200);
            _titleGroup = _titleBox.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.blocksRaycasts = false; _titleGroup.interactable = false;

            var skip = UiKit.Label("CineSkip", root, "Space · 건너뛰기", 13, new Color(1, 1, 1, 0.55f), TextAnchor.LowerRight);
            var srt = skip.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 0);
            srt.anchoredPosition = new Vector2(-Right, Bar - 34); // bottom: calc(12vh - 34px)
            srt.sizeDelta = new Vector2(400, 30);
            _skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            _skipGroup.alpha = 0; _skipGroup.blocksRaycasts = false;

            gameObject.SetActive(true);
        }

        public void SetOn(bool on)
        {
            _on = on;
            _onFor = 0;
            if (!on) _skipGroup.alpha = 0;
        }

        public void SetTitle(IReadOnlyList<string> lines)
        {
            _out = false;
            _titleGroup.alpha = 1;
            _typedFor = 0;
            foreach (var t in _lineTexts) if (t) Destroy(t.gameObject);
            _lineTexts.Clear();
            _lines = Array.Empty<string>();
            if (lines == null || lines.Count == 0) return;
            _lines = new string[lines.Count];
            float y = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                _lines[i] = lines[i] ?? "";
                bool sub = i > 0;
                int size = sub ? 16 : 22; // clamp(.82rem,1.6vw,1rem) / clamp(1rem,2.2vw,1.35rem) at 1920
                var t = UiKit.Label("Line" + i, _titleBox, "", size, sub ? SubInk : TitleInk, TextAnchor.UpperLeft, sub ? FontStyle.Normal : FontStyle.Bold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(1, -1.5f);
                UiKit.At(t.rectTransform, 0, y, 1100, size * 1.3f);
                y += size * 1.3f + size * 0.35f;
                _lineTexts.Add(t);
            }
        }

        public void TitleOut() => _out = true;

        void Update()
        {
            float dt = Time.unscaledDeltaTime; // CSS transitions and setTimeout run on wall-clock time

            if (_on)
            {
                _onFor += dt;
                // transition: opacity .6s .8s
                _skipGroup.alpha = Mathf.Clamp01((_onFor - 0.8f) / 0.6f);
            }

            if (_lines.Length > 0)
            {
                _typedFor += dt;
                for (int i = 0; i < _lines.Length && i < _lineTexts.Count; i++)
                {
                    int n = UnityCineFx.TypedCount(i, UnityCineFx.TextLength(_lines[i]), _typedFor);
                    var s = UnityCineFx.TypedText(_lines[i], n);
                    if (_lineTexts[i].text != s) _lineTexts[i].text = s;
                }
            }

            if (_out && _titleGroup.alpha > 0)
                _titleGroup.alpha = Mathf.MoveTowards(_titleGroup.alpha, 0, dt / UnityCineFx.TitleSeconds);
        }
    }

    /// <summary>One missile: head, fire trail, smoke trail and smoke puffs. Destroying the GameObject removes all of it.</summary>
    public class CineMissile : MonoBehaviour
    {
        const float Linger = 7f; // seconds the smoke stays after impact before the missile cleans itself up

        double[] _from, _to;
        float _seconds, _t, _after = -1;
        Transform _head;
        TrailRenderer _fire, _smoke;
        ParticleSystem _puffs;
        Light _glow;
        readonly List<Material> _mats = new();

        public void Launch(double[] from, double[] to, double seconds)
        {
            _from = (double[])from.Clone(); _to = (double[])to.Clone(); _seconds = (float)seconds;
            transform.position = Pos(0);

            // The white-hot head: unlit HDR so bloom blows it out (the original's colour (8, 6, 4)), unfogged (fog: false).
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(transform, false);
            head.transform.localScale = Vector3.one * 1.2f;
            var hr = head.GetComponent<MeshRenderer>();
            hr.sharedMaterial = Track(NoFog(new Color(8, 6, 4)));
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hr.receiveShadows = false;
            _head = head.transform;

            _glow = gameObject.AddComponent<Light>();
            _glow.type = LightType.Point; _glow.range = 30; _glow.intensity = 6; _glow.shadows = LightShadows.None;
            _glow.color = new Color(1f, 0.72f, 0.45f);

            // Fire: a short, hot tail right behind the head (the original's 40 m cone, 0.25 m radius at the head widening to
            // 0.9 m, opacity 0.55, fog: false).
            _fire = gameObject.AddComponent<TrailRenderer>();
            _fire.sharedMaterial = Track(NoFog(new Color(2.2f, 1.4f, 0.8f, 1f)));
            _fire.time = Mathf.Clamp(_seconds * 0.16f, 0.15f, 0.5f);
            _fire.minVertexDistance = 0.5f;
            _fire.widthCurve = new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.8f));
            _fire.colorGradient = Grad(new Color(1, 0.95f, 0.85f), new Color(1, 0.55f, 0.2f), 0.55f, 0f);
            Quiet(_fire);

            // Smoke: a long grey ribbon that widens toward the tail and fades out over seconds after it passes.
            var sgo = new GameObject("Smoke");
            sgo.transform.SetParent(transform, false);
            _smoke = sgo.AddComponent<TrailRenderer>();
            _smoke.sharedMaterial = Track(TrailMat(false, Color.white));
            _smoke.time = 5.5f;
            _smoke.minVertexDistance = 1.2f;
            _smoke.widthCurve = new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(0.15f, 2.4f), new Keyframe(1, 6f));
            _smoke.colorGradient = Grad(new Color(0.62f, 0.6f, 0.58f), new Color(0.42f, 0.41f, 0.4f), 0.75f, 0f);
            Quiet(_smoke);

            _puffs = BuildPuffs();
        }

        Material Track(Material m) { _mats.Add(m); return m; }

        static void Quiet(TrailRenderer tr)
        {
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.numCapVertices = 2;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.autodestruct = false;
            tr.Clear();
        }

        static Gradient Grad(Color a, Color b, float alphaA, float alphaB)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(alphaA, 0), new GradientAlphaKey(alphaA * 0.6f, 0.4f), new GradientAlphaKey(alphaB, 1) });
            return g;
        }

        // Unfogged unlit HDR colour: URP has no per-material fog switch (fog is a global multi_compile keyword), so this
        // uses Sprites/Default (no fog code, renders in URP, Always Included) whose output is texture x vertex colour x
        // _Color, premultiplied (Blend One OneMinusSrcAlpha). Vertex alpha 0.55 on the fire is then close to the
        // original's additive 0.55 on a dark sky; the head (alpha 1) is opaque.
        static Material NoFog(Color c)
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10; // over the (fogged) smoke ribbon
            return m;
        }

        // URP particle unlit, untextured; vertex colour (the trail gradient) times the base colour. Fogged on purpose:
        // the smoke should sit in the haze.
        static Material TrailMat(bool additive, Color baseColor)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword(additive ? "_BLENDMODE_ADD" : "_BLENDMODE_ALPHA");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        ParticleSystem BuildPuffs()
        {
            var go = new GameObject("Puffs");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.54f, 0.52f, 0.55f), new Color(0.4f, 0.39f, 0.38f, 0.45f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = -0.02f;
            main.maxParticles = 700;
            main.playOnAwake = false;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.rateOverDistance = 0.7f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.4f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(1, 2.6f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = StoryFx.SmokeMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        Vector3 Pos(double k)
        {
            var p = UnityCineFx.MissilePoint(_from, _to, k);
            return LevelLayout.ToUnity((float)p[0], (float)p[1], (float)p[2]);
        }

        void Update()
        {
            if (_from == null) return;
            if (_after >= 0)
            {
                _after += Time.deltaTime;
                if (_after >= Linger) Destroy(gameObject);
                return;
            }
            _t += Time.deltaTime; // world time, like the original's engine.onFrame dt (a slow shot slows it)
            float k = Mathf.Min(1, _t / _seconds);
            var p = Pos(k);
            var ahead = Pos(Mathf.Min(1, k + 0.02f)) - p;
            if (ahead.sqrMagnitude < 1e-6f) ahead = Pos(1) - Pos(Mathf.Max(0, k - 0.02f));
            transform.position = p;
            if (ahead.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(ahead.normalized);
            if (k >= 1) Impact();
        }

        void Impact()
        {
            _after = 0;
            if (_head) _head.gameObject.SetActive(false); // g.visible = k < 1
            if (_glow) _glow.enabled = false;
            if (_fire) _fire.emitting = false;
            if (_smoke) _smoke.emitting = false;
            if (_puffs) _puffs.Stop(false, ParticleSystemStopBehavior.StopEmitting); // what is in the air drifts and fades
        }

        void OnDestroy()
        {
            foreach (var m in _mats) if (m) Destroy(m);
            _mats.Clear();
        }
    }
}
