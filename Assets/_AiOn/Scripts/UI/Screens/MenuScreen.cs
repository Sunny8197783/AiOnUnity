// The original ui/menu.js screens: landing (start screen with the animated skyline), pause, settings, about.
// Texts, button lists, the settings form and its result are in MenuLogic.cs; this file only draws and handles input.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Registers landing, pause, settings and about on the story host.</summary>
    public static class MenuScreens
    {
        public static void Register(UnityStoryHost host)
        {
            host.Screens["landing"] = LandingScreen.Open;
            host.Screens["pause"] = PauseScreen.Open;
            host.Screens["settings"] = SettingsScreen.Open;
            host.Screens["about"] = AboutScreen.Open;
            SceneSelectScreen.Register(host); // the judge entry's 장면 바로가기
        }
    }

    // =====================================================================================================
    // landing({ hasSave, endings, challenges }) -> 'new'|'continue'|'settings'|'about'|'gallery'|'records'|'lounge'
    // =====================================================================================================
    public class LandingScreen : UiScreen
    {
        bool _hasSave; int _endings, _challenges;
        const int ArtOrder = 221; // the screen canvas is 220: its window fill, then the skyline (221), then the front (222)
        readonly MenuFocusRing _ring = new();

        // skyline
        Skyline _sky;
        Image[] _skyBands;
        Image _glow;
        readonly List<(Skyline.Window w, Image img, int layer)> _wins = new();
        readonly List<Text> _numbers = new();
        CanvasGroup _stars, _exits;
        int[] _state;
        bool _still;

        public static async Task<JToken> Open(JObject o)
        {
            var (hasSave, endings, challenges) = MenuLogic.LandingOptions(o);
            var r = await Show<LandingScreen>(s => { s._hasSave = hasSave; s._endings = endings; s._challenges = challenges; }, 1920, 1080);
            return r == null ? null : new JValue((string)r);
        }

        protected override void Build()
        {
            Window.GetComponent<Image>().color = MenuBits.Hex("#05070f");
            var host = UnityStoryHost.Instance;
            _still = host && host.ReduceMotion;
            BuildSkyline();

            // Everything in front of the skyline sits on its own canvas after the skyline's: a nested canvas draws over
            // its parent's graphics, so without this the lit windows would draw across the text, buttons and panel.
            var front = UiKit.Rect("Front", Window); UiKit.Fill(front);
            var fc = front.gameObject.AddComponent<Canvas>();
            fc.overrideSorting = true; fc.sortingOrder = ArtOrder + 1; // explicitly after the skyline canvas (equal orders draw in no fixed order)
            front.gameObject.AddComponent<GraphicRaycaster>();

            // shade for legibility (original .landing-shade): a soft left-to-right gradient over the whole screen and
            // a fade up from the bottom, so the hero copy reads and nothing ends on a hard edge
            var tint = new Color(0.02f, 0.03f, 0.05f, 1);
            UiKit.Fill(ScreenDecor.Raw("Shade", front, ScreenDecor.LandingShade, tint).rectTransform);
            var foot = ScreenDecor.Raw("ShadeBottom", front, ScreenDecor.FadeUp, new Color(tint.r, tint.g, tint.b, ScreenDecor.A(0.9f))).rectTransform;
            foot.anchorMin = Vector2.zero; foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0);
            foot.offsetMin = Vector2.zero; foot.offsetMax = new Vector2(0, 1080 * 0.3f);
            BuildFront(front);
        }

        void BuildFront(RectTransform Window)
        {

            // hero
            MenuBits.Label(Window, "Eyebrow", $"<color=#3cc6b5>●</color>  {MenuLogic.LandingEyebrow}", 22, UiKit.Dim, 120, 132, 1000, 32);
            MenuBits.Label(Window, "Title", $"<color=#9aa6ad>〈</color>{MenuLogic.LandingTitleKo}<color=#9aa6ad>〉</color>", 104, UiKit.Ink, 112, 170, 1000, 130, style: FontStyle.Bold);
            MenuBits.Label(Window, "TitleEn", MenuLogic.LandingTitleEn, 30, UiKit.Amber, 124, 300, 600, 40);
            MenuBits.Label(Window, "Pitch", MenuLogic.LandingPitch, 34, UiKit.Ink, 120, 362, 1000, 46, style: FontStyle.Bold);
            MenuBits.Label(Window, "Sub", MenuLogic.LandingSub, 24, UiKit.Dim, 120, 418, 960, 70);
            for (int i = 0; i < MenuLogic.LandingLoop.Length; i++)
            {
                var chip = ScreenDecor.Rounded("Loop" + i, Window, new Color(0.05f, 0.08f, 0.1f, ScreenDecor.A(0.8f))).rectTransform;
                UiKit.At(chip, 120 + i * 170, 488, 150, 44);
                UiKit.Fill(UiKit.Label("Text", chip, $"<color=#3cc6b5><b>{i + 1}</b></color>  {MenuLogic.LandingLoop[i]}", 22, UiKit.Ink, TextAnchor.MiddleCenter).rectTransform);
            }

            // actions: the big ones in a row, the ghost ones below, three per row
            var buttons = MenuLogic.LandingButtons(_hasSave, _endings, _challenges, Challenges.All.Count);
            int big = 0, ghost = 0;
            Button focus = null;
            foreach (var mb in buttons)
            {
                var id = mb.Id;
                var b = MenuBits.Btn(Window, mb.Label, mb.Primary, mb.Ghost, () => { MenuBits.Cue(); Finish(id); }, mb.Ghost ? 22 : 28);
                var rt = (RectTransform)b.transform;
                if (!mb.Primary) b.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.07f, ScreenDecor.A(mb.Ghost ? 0.75f : 0.85f)); // dark fill: the skyline never reads through
                if (id == "scenes" || id == "teacher" || id == FamilyCard.Screen || id == Archive.Screen)
                {
                    // the judge / classroom / learning entries read as their own row: a teal-tinted fill and teal text
                    b.GetComponent<Image>().color = new Color(0.05f, 0.13f, 0.13f, ScreenDecor.A(0.85f));
                    var t = b.GetComponentInChildren<Text>(); if (t) t.color = MenuBits.Hex("#6fe0d2");
                }
                if (mb.Ghost) { UiKit.At(rt, 120 + (ghost % 3) * 300, 664 + (ghost / 3) * 70, 284, 58); ghost++; }
                else { UiKit.At(rt, 120 + big * 320, 568, 300, 76); big++; }
                _ring.Add(b);
                if (mb.Autofocus) focus = b;
            }

            // chapters (original .landing-chapters)
            var ch = UiKit.Box("Chapters", Window, new Color(0.047f, 0.07f, 0.082f, ScreenDecor.A(0.93f))).rectTransform;
            UiKit.At(ch, 1250, 132, 560, 760);
            MenuBits.Label(ch, "Head", MenuLogic.LandingChaptersHead, 26, UiKit.Ink, 28, 22, 500, 36, style: FontStyle.Bold);
            for (int i = 0; i < MenuLogic.Chapters.Length; i++)
            {
                var c = MenuLogic.Chapters[i];
                float y = 76 + i * 96;
                MenuBits.Label(ch, "No", c[0], 20, UiKit.Teal, 28, y + 2, 100, 30, style: FontStyle.Bold);
                var when = string.IsNullOrEmpty(c[2]) ? "" : $"  <size=17><color=#9aa6ad>{c[2]}</color></size>";
                MenuBits.Label(ch, "Main", $"<b>{c[1]}</b>{when}", 23, UiKit.Ink, 128, y, 410, 34);
                MenuBits.Label(ch, "Line", c[3], 19, UiKit.Dim, 128, y + 38, 410, 50);
            }

            // footer
            MenuBits.Label(Window, "Fiction", MenuLogic.FictionNote, 18, UiKit.Dim, 120, 930, 1100, 60);
            var keys = string.Join("     ", MenuLogic.LandingKeys.Select(k => $"<color=#e8eded><b>{k[0]}</b></color>  {k[1]}"));
            MenuBits.Label(Window, "Keys", keys, 20, UiKit.Dim, 1250, 930, 560, 30);

            if (focus) _ring.Focus((RectTransform)focus.transform); else _ring.Focus(0);
        }

        void BuildSkyline()
        {
            var art = UiKit.Rect("Art", Window);
            UiKit.Fill(art);
            var ac = art.gameObject.AddComponent<Canvas>(); // its own canvas: the per-frame window changes do not rebuild the menu
            ac.overrideSorting = true; ac.sortingOrder = ArtOrder;
            _sky = new Skyline(1920, 1080);
            float ground = (float)_sky.Ground;

            const int bands = 24;
            _skyBands = new Image[bands];
            for (int i = 0; i < bands; i++)
            {
                var b = UiKit.Box("Sky" + i, art, Color.black);
                UiKit.At(b.rectTransform, 0, ground * i / bands, 1920, ground / bands + 1);
                _skyBands[i] = b;
            }
            var starRoot = UiKit.Rect("Stars", art); UiKit.Fill(starRoot);
            _stars = starRoot.gameObject.AddComponent<CanvasGroup>();
            foreach (var s in _sky.Stars)
                UiKit.At(UiKit.Box("Star", starRoot, MenuBits.Hex("#e8eefc")).rectTransform, (float)s.X, (float)s.Y, (float)Math.Max(1, s.R), (float)Math.Max(1, s.R));
            // moon and halo: soft round sprites, top right above the chapter panel (panel top is y 132; halo ends at y 122)
            const float moonX = 1690, moonY = 66;
            var halo = UiKit.Box("Halo", art, new Color(0.94f, 0.93f, 0.86f, 0.16f)); halo.sprite = ScreenDecor.Glow;
            UiKit.At(halo.rectTransform, moonX - 56, moonY - 56, 112, 112);
            var moon = UiKit.Box("Moon", art, MenuBits.Hex("#f1ecdd")); moon.sprite = ScreenDecor.Moon;
            UiKit.At(moon.rectTransform, moonX - 14, moonY - 14, 28, 28);
            _glow = UiKit.Box("Glow", art, new Color(1, 0.67f, 0.35f, 0.2f));
            UiKit.At(_glow.rectTransform, 0, ground - 1080 * 0.12f, 1920, 1080 * 0.12f);

            for (int li = 0; li < 3; li++)
            {
                var layer = UiKit.Rect("Layer" + li, art); UiKit.Fill(layer);
                var col = MenuBits.Hex(Skyline.LayerColors[li]);
                foreach (var b in _sky.Layers[li])
                {
                    UiKit.At(UiKit.Box("Slab", layer, col).rectTransform, (float)b.X, (float)b.Y, (float)b.W, (float)b.H);
                    if (li == 1)
                    {
                        var roof = MenuBits.Hex("#161c2a");
                        UiKit.At(UiKit.Box("Roof", layer, roof).rectTransform, (float)(b.X + b.W * 0.42), (float)b.Y - 10, (float)(b.W * 0.16), 10);
                        UiKit.At(UiKit.Box("Parapet", layer, roof).rectTransform, (float)b.X, (float)b.Y - 3, (float)b.W, 3);
                    }
                    foreach (var w in b.Win)
                    {
                        var img = UiKit.Box("Win", layer, MenuBits.Hex(w.Col));
                        UiKit.At(img.rectTransform, (float)w.X, (float)w.Y, (float)Math.Max(1, w.W), (float)Math.Max(1, w.H));
                        _wins.Add((w, img, li));
                    }
                    if (b.Label != null)
                    {
                        int size = Mathf.RoundToInt((float)(b.W * 0.16));
                        var t = MenuBits.Label(layer, "No", b.Label, size, new Color(0.86f, 0.89f, 0.93f, 0.55f), (float)b.X, (float)(b.Y + b.W * 0.2 - size), (float)b.W, size * 1.3f, TextAnchor.UpperCenter, FontStyle.Bold);
                        _numbers.Add(t);
                    }
                }
            }
            var exitRoot = UiKit.Rect("ExitLights", art); UiKit.Fill(exitRoot);
            _exits = exitRoot.gameObject.AddComponent<CanvasGroup>();
            var green = new Color(60 / 255f, 230 / 255f, 140 / 255f);
            foreach (var b in _sky.Layers[1])
                for (int f = 1; f < 15; f += 2)
                    UiKit.At(UiKit.Box("Exit", exitRoot, green).rectTransform, (float)(b.X + b.W - 7), (float)(b.Y + b.H / 15 * f + 4), 3, 2);

            UiKit.At(UiKit.Box("Ground", art, MenuBits.Hex("#07090e")).rectTransform, 0, ground, 1920, 1080 - ground);
            foreach (var x in _sky.Poles)
            {
                var pole = MenuBits.Hex("#05070b");
                UiKit.At(UiKit.Box("Pole", art, pole).rectTransform, (float)x - 2.5f, ground - 1080 * 0.3f, 5, 1080 * 0.3f);
                UiKit.At(UiKit.Box("Arm", art, pole).rectTransform, (float)x - 18, ground - 1080 * 0.28f, 36, 3);
            }
            _state = new int[_wins.Count];
            for (int i = 0; i < _state.Length; i++) _state[i] = -1;
            Animate(0);
        }

        void Animate(double now)
        {
            double p = Skyline.Phase(now, _still), dark = Skyline.Dark(p);
            var stops = Skyline.Sky(dark);
            for (int i = 0; i < _skyBands.Length; i++)
            {
                float k = (i + 0.5f) / _skyBands.Length;
                Color a = MenuBits.Rgb(stops[0]), m = MenuBits.Rgb(stops[1]), h = MenuBits.Rgb(stops[2]);
                _skyBands[i].color = k < 0.55f ? Color.Lerp(a, m, k / 0.55f) : Color.Lerp(m, h, (k - 0.55f) / 0.45f);
            }
            _stars.alpha = (float)(0.15 + dark * 0.85) * 0.8f;
            _glow.color = new Color(1, 0.67f, 0.35f, (float)(0.22 * (1 - dark)) * 0.6f);
            _exits.alpha = dark > 0.2 ? (float)dark : 0;
            // building numbers: quieter on the left half, where they would sit under the hero copy and buttons
            for (int i = 0; i < _numbers.Count; i++)
            {
                var t = _numbers[i];
                float a = (float)(0.55 - dark * 0.35);
                var rt = t.rectTransform;
                float cx = rt.anchoredPosition.x + rt.sizeDelta.x / 2, ty = -rt.anchoredPosition.y;
                // none behind the hero copy and buttons (light text at a low alpha still reads about .4 in linear blending)
                if (cx < 1000 && ty > 100 && ty < 820) a = 0;
                else if (cx < 960) a = Mathf.Min(a, 0.25f) * 0.6f;
                t.color = new Color(0.86f, 0.89f, 0.93f, a);
            }
            for (int i = 0; i < _wins.Count; i++)
            {
                var (w, img, li) = _wins[i];
                int st = _sky.WindowOn(w, p, now) ? 1 : Skyline.EmergencyLight(w, dark) ? 2 : 0;
                if (st == 2)
                {
                    var c = MenuBits.Hex("#f3f6ff"); c.a = (float)(0.5 + 0.5 * Math.Sin(now / 400 + w.K * 99)); img.color = c;
                    img.enabled = true;
                }
                else if (st != _state[i])
                {
                    img.enabled = st == 1;
                    if (st == 1) { var c = MenuBits.Hex(w.Col); c.a = li == 0 ? 0.55f : 0.92f; img.color = c; }
                }
                _state[i] = st;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!_still && _sky != null) Animate(Time.unscaledTime * 1000.0);
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            _ring.Tick(Keyboard.current);
        }
    }

    // =====================================================================================================
    // pause() -> 'resume'|'settings'|'save'|'lounge'|'menu'
    // =====================================================================================================
    public class PauseScreen : UiScreen
    {
        readonly MenuFocusRing _ring = new();
        RectTransform _confirm;
        Button _menuBtn, _exitBtn;
        bool _confirmOpen;

        public static async Task<JToken> Open(JObject o)
        {
            var r = await Show<PauseScreen>(null, 1140, 780);
            return r == null ? null : new JValue((string)r);
        }

        protected override object EscapeResult() => "resume";

        protected override void Build()
        {
            Title(MenuLogic.PauseEyebrow, MenuLogic.PauseTitle);
            var list = MenuLogic.PauseButtons();
            for (int i = 0; i < list.Count; i++)
            {
                var mb = list[i];
                var id = mb.Id;
                Action click = id == "menu?" ? ShowConfirm : () => { MenuBits.Cue(); Finish(id); };
                var b = MenuBits.Btn(Window, mb.Label, mb.Primary, mb.Ghost, click, 26);
                UiKit.At((RectTransform)b.transform, 40, 140 + i * 78, 420, 64);
                if (id == "menu?") _menuBtn = b;
            }

            _confirm = UiKit.Box("Confirm", Window, new Color(0.95f, 0.7f, 0.24f, 0.1f)).rectTransform;
            UiKit.At(_confirm, 40, 540, 420, 200);
            MenuBits.Label(_confirm, "Text", MenuLogic.PauseConfirmText, 21, UiKit.Ink, 20, 16, 380, 90);
            _exitBtn = MenuBits.Btn(_confirm, "나가기", true, false, () => { MenuBits.Cue(); Finish("menu"); });
            UiKit.At((RectTransform)_exitBtn.transform, 20, 120, 180, 60);
            var cancel = MenuBits.Btn(_confirm, "취소", false, true, HideConfirm);
            UiKit.At((RectTransform)cancel.transform, 220, 120, 180, 60);
            _confirm.gameObject.SetActive(false);

            // key list (original dl.key-list)
            var keys = UiKit.Box("Keys", Window, new Color(0, 0, 0, 0.18f)).rectTransform;
            UiKit.At(keys, 500, 140, 600, 600);
            // the whole key table (UiKeys), Q/G/C/X/B included: rows shrink so every key fits the 600 px box
            int nk = MenuLogic.PauseKeys.Length;
            float rowH = Mathf.Min(44, (600 - 24) / (float)nk);
            int ks = rowH >= 40 ? 20 : 18;
            for (int i = 0; i < nk; i++)
            {
                var k = MenuLogic.PauseKeys[i];
                MenuBits.Label(keys, "Key", k[0], ks, UiKit.Ink, 24, 12 + i * rowH, 300, rowH, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuBits.Label(keys, "What", k[1], ks, UiKit.Dim, 330, 12 + i * rowH, 260, rowH, TextAnchor.MiddleLeft);
            }
            Ring();
            _ring.Focus(0);
        }

        void Ring()
        {
            // the modal traps focus over every visible button in DOM order (the confirm's two follow the menu's five)
            _ring.Clear();
            foreach (var b in Window.GetComponentsInChildren<Button>()) _ring.Add(b);
        }

        void ShowConfirm()
        {
            _confirmOpen = true;
            _confirm.gameObject.SetActive(true);
            Ring();
            _ring.Focus((RectTransform)_exitBtn.transform); // confirmBox.querySelector('.btn').focus()
        }

        void HideConfirm()
        {
            _confirmOpen = false;
            _confirm.gameObject.SetActive(false);
            Ring();
            _ring.Focus((RectTransform)_menuBtn.transform);
        }

        protected override void Update()
        {
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                bool open = _confirmOpen;
                var r = MenuLogic.PauseEscape(ref open);
                if (r == null) HideConfirm(); else Finish(r);
                return;
            }
            _ring.Tick(kb);
        }
    }

    // =====================================================================================================
    // settings(current) -> { quality, qualityAuto, volume, mute, captions, reduceMotion, textScale, view, sensitivity,
    //   minimap, volAmbience, volEffects, volUi, profile, memoryCue, ...the rest of current }
    // =====================================================================================================
    public class SettingsScreen : UiScreen
    {
        SettingsForm _form;
        const float WinH = 1000; // with the compact rows below, both columns fit their 770 px views at the default text size
        readonly MenuFocusRing _ring = new() { ArrowsMove = false };
        static readonly Color ListBg = new(0.057f, 0.082f, 0.09f);
        readonly Dictionary<string, Action> _refresh = new();

        public static async Task<JToken> Open(JObject current)
        {
            var form = new SettingsForm(current);
            var r = await Show<SettingsScreen>(s => s._form = form, 1720, WinH);
            return r as JObject ?? form.Cancel();
        }

        /// <summary>Copies what the story reads (captions, profile, memory cue, reduced motion) into the host's settings.</summary>
        public static void ApplyTo(StorySettings s, JObject result)
        {
            if (s == null || result == null) return;
            var (captions, profile, memoryCue, reduceMotion) = MenuLogic.StoryFields(result);
            s.Captions = captions; s.Profile = profile; s.MemoryCue = memoryCue; s.ReduceMotion = reduceMotion;
        }

        protected override object EscapeResult() => _form.Cancel();

        protected override void Build()
        {
            Title(SettingsForm.Eyebrow, SettingsForm.Title);
            // the two columns run down to 20 px above the 취소 / 적용 row; a column longer than its view gets a thin
            // scrollbar and a fade at the bottom so it is clear that more follows
            const float top = 122, btnY = WinH - 88, viewH = btnY - 20 - top;
            var leftView = UiKit.Rect("Left", Window); UiKit.At(leftView, 40, top, 800, viewH);
            var rightView = UiKit.Rect("Right", Window); UiKit.At(rightView, 880, top, 800, viewH);
            var left = UiKit.ScrollList("List", leftView, 6);
            var right = UiKit.ScrollList("List", rightView, 6);
            ScrollCue.Attach(left.parent.GetComponent<ScrollRect>(), ListBg, 64, -12);  // the bar just outside each column
            ScrollCue.Attach(right.parent.GetComponent<ScrollRect>(), ListBg, 64, -12);
            const float W = 788;

            // Left: the four choices (그래픽 품질, 시점, 글자 크기, 체험 모드). Right: 조작 (one slider and one switch), 소리,
            // 접근성. Measured at the default text size: left about 645 px, right about 730 px, both inside the 770 px views,
            // so nothing starts cut off under the fade; a larger text size still scrolls with the cue.
            foreach (var g in _form.LeftRadios) RadioGroupUi(left, g, W);
            // 쉬운 한국어 · 도우미 모드 (competition upgrade): one row of two switches, so the left column still fits its view
            Legend(left, SettingsForm.TogetherLegend);
            var together = UiKit.Rect("Together", left);
            UiKit.Height(together, TogetherH);
            float half = (W - 8) / 2;
            for (int i = 0; i < _form.TogetherToggles.Length; i++) ToggleUi(left, _form.TogetherToggles[i], half, together, i * (half + 8));

            Legend(right, SettingsForm.ControlsLegend);
            RangeUi(right, _form.Sensitivity, W);
            ToggleUi(right, _form.Minimap, W);
            Legend(right, SettingsForm.SoundLegend);
            RangeUi(right, _form.Volume, W);
            foreach (var r in _form.BusRanges) RangeUi(right, r, W);
            ToggleUi(right, _form.Mute, W);
            Legend(right, SettingsForm.AccessLegend);
            foreach (var t in _form.AccessToggles) ToggleUi(right, t, W);

            var cancel = MenuBits.Btn(Window, "취소", false, true, () => Finish(_form.Cancel()));
            UiKit.At((RectTransform)cancel.transform, 1180, btnY, 240, 60);
            var apply = MenuBits.Btn(Window, "✓ 적용", true, false, Submit);
            UiKit.At((RectTransform)apply.transform, 1440, btnY, 240, 60);
            _ring.Add(cancel);
            _ring.Add(apply);

            // the original focuses the first checked input
            int first = 0;
            for (int i = 0; i < _form.LeftRadios.Count; i++) if (_form.Radio[_form.LeftRadios[i].Name] != null) { first = i; break; }
            _ring.Focus(first);
        }

        void Submit()
        {
            MenuBits.Cue();
            Finish(_form.Submit());
        }

        static void Legend(RectTransform col, string text)
        {
            MenuBits.Gap(col, 6);
            UiKit.Height(UiKit.Label("Legend", col, text, 24, UiKit.Ink, style: FontStyle.Bold), 32);
        }

        void RadioGroupUi(RectTransform col, RadioGroup g, float width)
        {
            Legend(col, g.Legend);
            int n = g.Options.Length;
            int cols = g.Cols > 0 ? g.Cols : n > 4 ? 3 : n;
            bool subs = g.Options.Any(o => o.Sub != null);
            float h = subs ? 60 : 46, gap = 6, cw = (width - (cols - 1) * gap) / cols;
            int rows = (n + cols - 1) / cols;
            var box = UiKit.Rect("Seg_" + g.Name, col);
            UiKit.Height(box, rows * h + (rows - 1) * gap);
            var buttons = new Button[n];
            for (int i = 0; i < n; i++)
            {
                var o = g.Options[i];
                var text = o.Sub != null ? $"<b>{o.Label}</b>\n<size=17><color=#9aa6ad>{o.Sub}</color></size>" : $"<b>{o.Label}</b>";
                var b = UiKit.Button("Opt_" + o.Value, box, text, 22, UiKit.Row, UiKit.Ink);
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                UiKit.At((RectTransform)b.transform, (i % cols) * (cw + gap), (i / cols) * (h + gap), cw, h);
                var v = o.Value;
                b.onClick.AddListener(() => { _form.Radio[g.Name] = v; _refresh[g.Name](); _ring.Focus(box); });
                buttons[i] = b;
            }
            _refresh[g.Name] = () =>
            {
                for (int i = 0; i < n; i++)
                {
                    bool on = _form.Radio[g.Name] == g.Options[i].Value;
                    buttons[i].GetComponent<Image>().color = on ? UiKit.RowSelected : UiKit.Row;
                    var t = buttons[i].GetComponentInChildren<Text>();
                    t.color = on ? MenuBits.Hex("#6fe0d2") : UiKit.Ink;
                }
            };
            _refresh[g.Name]();
            _ring.Add(box, null, d =>
            {
                int cur = Array.FindIndex(g.Options, o => o.Value == _form.Radio[g.Name]);
                int next = cur < 0 ? (d > 0 ? 0 : n - 1) : ((cur + d) % n + n) % n;
                _form.Radio[g.Name] = g.Options[next].Value;
                _refresh[g.Name]();
            }, Submit);
            if (g.Note != null) MenuBits.Para(col, g.Note, 18, UiKit.Dim);
        }

        void RangeUi(RectTransform col, RangeDef r, float width)
        {
            // one line: label, slider, value (the label-over-slider rows made both columns too tall to fit)
            var row = UiKit.Rect("Range_" + r.Name, col);
            UiKit.Height(row, 46);
            const float labelW = 300, outW = 96;
            MenuBits.Label(row, "Label", r.Label, 21, UiKit.Ink, 0, 9, labelW, 30, TextAnchor.UpperLeft);
            var output = MenuBits.Label(row, "Out", _form.RangeText(r.Name), 21, UiKit.Teal, width - outW, 9, outW, 30, TextAnchor.UpperRight, FontStyle.Bold);

            var bg = UiKit.Box("Slider", row, ScreenDecor.Over(ListBg, Color.white, 0.08f), raycast: true);
            UiKit.At(bg.rectTransform, labelW + 12, 12, width - labelW - outW - 32, 22);
            var slider = bg.gameObject.AddComponent<Slider>();
            var fillArea = UiKit.Rect("Fill Area", bg.transform); UiKit.Fill(fillArea);
            var fill = UiKit.Box("Fill", fillArea, UiKit.Teal);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = UiKit.Rect("Handle Slide Area", bg.transform); UiKit.Fill(handleArea, 10, 0);
            var handle = UiKit.Box("Handle", handleArea, UiKit.Ink, raycast: true);
            handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.sizeDelta = new Vector2(20, 8);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            var nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.minValue = r.Min / (float)r.Step;
            slider.maxValue = r.Max / (float)r.Step;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(_form.Range[r.Name] / (float)r.Step);
            slider.onValueChanged.AddListener(x => { _form.SetRange(r.Name, x * r.Step); output.text = _form.RangeText(r.Name); });
            _ring.Add(row, null, d =>
            {
                // the browser: Right/Up raise a range, Left/Down lower it
                _form.StepRange(r.Name, d);
                slider.SetValueWithoutNotify(_form.Range[r.Name] / (float)r.Step);
                output.text = _form.RangeText(r.Name);
            }, Submit);
        }

        const float TogetherH = 56;

        /// <summary>A switch row in a column, or (row given) one of several side by side in that row at x.</summary>
        void ToggleUi(RectTransform col, ToggleDef t, float width, RectTransform row = null, float x = 0)
        {
            var b = UiKit.Button("Toggle_" + t.Name, row ? row : col, "", 22, ScreenDecor.Over(ListBg, Color.white, 0.04f), UiKit.Ink);
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            if (row) UiKit.At((RectTransform)b.transform, x, 0, width, TogetherH);
            else UiKit.Height(b, 60);
            var rt = (RectTransform)b.transform;
            var label = b.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.text = t.Sub != null ? $"<b>{t.Label}</b>\n<size=17><color=#9aa6ad>{t.Sub}</color></size>" : $"<b>{t.Label}</b>";
            label.rectTransform.offsetMin = new Vector2(96, 4);
            var track = UiKit.Box("Switch", rt, Color.white).rectTransform;
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0, 0.5f);
            track.anchoredPosition = new Vector2(16, 0); track.sizeDelta = new Vector2(60, 30);
            var knob = UiKit.Box("Knob", track, UiKit.Ink).rectTransform;
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0, 0.5f);
            knob.sizeDelta = new Vector2(24, 24);
            void Draw()
            {
                bool on = _form.Toggle[t.Name];
                track.GetComponent<Image>().color = on ? UiKit.Teal : ScreenDecor.Over(ListBg, Color.white, 0.16f);
                knob.anchoredPosition = new Vector2(on ? 33 : 3, 0);
            }
            Draw();
            b.onClick.AddListener(() => { _form.Toggle[t.Name] = !_form.Toggle[t.Name]; Draw(); _ring.Focus(rt); });
            _ring.Add(rt, () => { _form.Toggle[t.Name] = !_form.Toggle[t.Name]; Draw(); }, null, Submit);
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            // the browser raises a range with Up and lowers it with Down; MenuFocusRing passes Down as +1
            if (kb != null && _ring.Current?.Adjust != null && _ring.Current.Rt.name.StartsWith("Range_")
                && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame))
            {
                _ring.Current.Adjust(kb.upArrowKey.wasPressedThisFrame ? 1 : -1);
                return;
            }
            // without Adjust, Up/Down move between controls (Tab does too)
            _ring.Tick(kb);
        }
    }

    // =====================================================================================================
    // about({ sources: [{ publisher, title, url, checkedAt }] }) -> null
    // =====================================================================================================
    public class AboutScreen : UiScreen
    {
        JToken _sources;
        readonly MenuFocusRing _ring = new() { ArrowsMove = false };
        static readonly Color ListBg = new(0.057f, 0.082f, 0.09f);
        RectTransform _list;

        public static async Task<JToken> Open(JObject o)
        {
            var src = MenuLogic.AboutSources(o);
            await Show<AboutScreen>(s => s._sources = src, 1320, 940);
            return null;
        }

        protected override void Build()
        {
            Title(MenuLogic.AboutEyebrow, MenuLogic.AboutTitle);
            var view = UiKit.Rect("Scroll", Window); UiKit.At(view, 40, 130, 1240, 690);
            _list = UiKit.ScrollList("List", view, 8);

            var fiction = MenuBits.Card(_list, new Color(0.95f, 0.7f, 0.24f, 0.08f), 22, 16, 8);
            MenuBits.Para(fiction, MenuLogic.AboutFictionHead, 26, UiKit.Amber, FontStyle.Bold);
            foreach (var p in MenuLogic.AboutFiction) MenuBits.Para(fiction, p, 21, UiKit.Ink);

            MenuBits.Gap(_list, 8);
            var groups = MenuLogic.GroupSources(_sources, out int total);
            MenuBits.Para(_list, MenuLogic.AboutSourcesHead(total), 26, UiKit.Ink, FontStyle.Bold);
            foreach (var (pub, items) in groups)
            {
                MenuBits.Gap(_list, 4);
                MenuBits.Para(_list, pub, 22, UiKit.Teal, FontStyle.Bold);
                foreach (var s in items)
                {
                    var small = s.CheckedText != null ? $"<size=17><color=#9aa6ad>{s.CheckedText}</color></size>" : "";
                    if (s.Url != null)
                    {
                        var b = UiKit.Button("Link", _list, $"• <color=#8cc2ff>{s.Title}</color>{small}", 20, new Color(1, 1, 1, 0.03f), UiKit.Ink);
                        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                        var t = b.GetComponentInChildren<Text>(); t.alignment = TextAnchor.MiddleLeft;
                        UiKit.Height(b, 40);
                        var url = s.Url;
                        b.onClick.AddListener(() => Application.OpenURL(url));
                        _ring.Add(b);
                    }
                    else MenuBits.Para(_list, $"• {s.Title}{small}", 20, UiKit.Ink);
                }
            }

            MenuBits.Gap(_list, 8);
            MenuBits.Para(_list, MenuLogic.AboutCreditsHead, 26, UiKit.Ink, FontStyle.Bold);
            foreach (var c in MenuLogic.AboutCredits) MenuBits.Para(_list, $"• {c}", 21, UiKit.Ink);

            var close = MenuBits.Btn(Window, "닫기", true, false, () => Finish(null));
            UiKit.At((RectTransform)close.transform, 1040, 852, 240, 60);
            _ring.Add(close);
            _ring.Focus((RectTransform)close.transform); // data-autofocus
        }

        protected override void Update()
        {
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Finish(null); return; }
            // the scroll area: arrows / Page keys scroll it (on a focused link too, like the browser; Tab moves focus)
            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_list, 80); return; }
                if (kb.upArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_list, -80); return; }
            }
            if (kb != null && kb.pageDownKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_list, 500); return; }
            if (kb != null && kb.pageUpKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_list, -500); return; }
            _ring.Tick(kb);
        }
    }
}
