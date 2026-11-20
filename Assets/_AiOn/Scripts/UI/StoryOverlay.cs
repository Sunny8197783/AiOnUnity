using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Player;
using AiOn.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Keyboard focus on a card's actions (pure, EditMode-tested).</summary>
    public static class CardFocus
    {
        /// <summary>The original's armedAfter(220): a card ignores keys and clicks this long after it opens.</summary>
        public const float ArmSeconds = 0.22f;

        /// <summary>
        /// The next focused action. From none (-1) an arrow lands on the button on its side (left: the first, right:
        /// the last) and Tab on the first (Shift+Tab the last); after that arrows stop at the ends and Tab wraps.
        /// </summary>
        public static int Step(int focus, int count, int dir, bool wrap)
        {
            if (count <= 0) return -1;
            if (focus < 0) return wrap ? (dir < 0 ? count - 1 : 0) : (dir < 0 ? 0 : count - 1);
            int n = focus + dir;
            return wrap ? ((n % count) + count) % count : Mathf.Clamp(n, 0, count - 1);
        }
    }

    /// <summary>
    /// The story's overlay (original ui/hud.js + ui/dialog.js), built in code: chapter and clock, the objective card,
    /// battery and bag chips, the four condition bars, label-tagged toasts, captions, letterbox, tension vignette, hurt
    /// flash and fades; plus the modal dialog (lines then choices), story cards and time-skip interstitials.
    /// Implements IStoryHud and the typed part of IStoryUi.
    /// </summary>
    public class StoryOverlay : MonoBehaviour, IStoryHud
    {
        public static StoryOverlay Instance { get; private set; }

        // Info labels (original ui/dom.js LABELS and style.css .tag-*).
        static readonly Dictionary<string, (string text, Color color)> Labels = new()
        {
            ["official"] = ("공식 안내", Hex("#6fe0d2")),
            ["site"] = ("현장 확인", Hex("#8cc2ff")),
            ["neighbor"] = ("이웃에게 들음", Hex("#c6b3ff")),
            ["rumor"] = ("소문·미확인", Hex("#c4cbce")),
            ["self"] = ("내 기록", Hex("#ffd27f")),
        };
        static readonly (string key, string label, bool worse)[] StatRows =
        {
            ("health", "체력", false), ("thirst", "갈증", true), ("fatigue", "피로", true), ("tension", "긴장", true),
        };

        static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        // The HUD's plates: translucent dark slate (HudTheme), rounded; their text carries a shadow for pale walls.
        static readonly Color PanelBg = HudTheme.Slate;
        static readonly Color Red = Hex("#e5484d");

        Canvas _canvas;
        RectTransform _root;
        // The roaming HUD (chapter, clock, objective, chips, toasts, stats, compass, minimap, stamina, keys, banner): one
        // layer that fades out under a modal window. The talk box, captions, letterbox and fades stay on the root.
        RectTransform _hud;
        CanvasGroup _hudGroup;
        readonly ToastQueue _toastQ = new();
        readonly List<string> _toastLabels = new(8);
        GameObject _torchChip;
        bool _torchOn;
        // chapter banner and objective flash (original hud.js setChapter / setObjective)
        GameObject _banner;
        CanvasGroup _bannerGroup;
        Text _bannerNo, _bannerTitle;
        float _bannerAt = -100;
        bool _bannerPending;
        string _lastChapter, _lastObjective;
        CanvasGroup _objFlash;
        float _flashAt = -100;
        bool _flashPending;
        // key strip
        CanvasGroup _keysGroup;
        readonly KeyStripTimer _keyTimer = new();
        // minimap: the last frame (drawn when the map shows again) and the level it was drawn for
        Newtonsoft.Json.Linq.JObject _lastMap;
        string _hostLevel;
        bool _mapDirty;

        /// <summary>A story dialog (Talk) is on screen: GameHud keeps its dot and prompt off.</summary>
        public static bool TalkOpen { get; private set; }
        Text _chapterNo, _chapterTitle, _clock, _objTitle, _objBody, _objSteps, _battery, _bag, _captionSpeaker, _captionText;
        GameObject _objective, _batteryChip, _bagChip, _caption, _stats, _letterTop, _letterBottom;
        Image _bagFill, _vignette, _hurt, _fade;
        GameObject _smokeHint, _topLeft;
        RectTransform _chapterBg;
        readonly Dictionary<GameObject, bool> _cineHidden = new();
        /// <summary>One meter of the vitals plate: label, track, fill, word; the trail lags behind a loss.</summary>
        sealed class MeterRow
        {
            public RectTransform Root;
            public Image Fill, Trail;
            public Text Word;
            public float Shown = 1, TrailK = 1, LossAt = -10;
        }
        MeterRow _healthRow, _staminaRow, _exposureRow;
        readonly Dictionary<string, Text> _miniStats = new();
        RectTransform _statsRt, _statsRule;
        bool _exposureOn;
        float _health = 100, _danger, _sinceBeat = 10, _beatEvery;
        AudioSource _heart;
        RectTransform _toasts;
        float _hurtLevel, _tension;
        static readonly Color HurtTint = new Color(0.16f, 0.13f, 0.1f);
        int _captionToken;
        bool _hidden, _batteryWanted, _bagWanted;

        // Compass strip, minimap, stamina bar and key strip (original hud.js 59-68): each is wanted by the story or the
        // player's state, and shows only while the HUD is up and no cutscene runs.
        HudCompass _compass;
        RectTransform _mapRoot;
        HudMinimap _minimap;
        GameObject _keys;
        Image _staminaFill;
        bool _cinematic, _compassWanted, _mapWanted, _staminaWanted;
        float _staminaShown = -1;
        bool _windedShown;
        /// <summary>The level layout the minimap draws (walk zones, solids); the host sets it.</summary>
        public Func<string, AiOn.Levels.LevelLayout> MapLayout;
        /// <summary>A layout solid's live off state (doors, closures, gates; the host's level state); null: as authored.</summary>
        public Func<AiOn.Levels.LevelLayout.SolidBox, bool> MapSolidOff;
        /// <summary>Fills the solids the story added at runtime (rubble); the host sets it.</summary>
        public Action<List<AiOn.Levels.LevelLayout.SolidBox>> MapExtraSolids;
        readonly List<AiOn.Levels.LevelLayout.SolidBox> _mapExtra = new();

        const float MapSize = 240, MapTop = 84, ToastTop = 84, ChapterMinW = 230, ChapterPad = 14;
        static readonly Color StaminaOk = Hex("#7fe0a0"), StaminaWinded = Hex("#ff8a6a");

        void Awake()
        {
            Instance = this;
            _canvas = UiKit.ScreenCanvas("StoryOverlay", 150);
            _canvas.transform.SetParent(transform, false);
            _root = (RectTransform)_canvas.transform;
            Build();
            Hide(); // the whole HUD waits for the Director's Hud.Show() (original hud.js: .hud starts hidden)
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Torn down mid-card or mid-talk (title return, scene teardown): the awaits that would close them never resume,
            // so the counts they hold go now, or the next run starts with the HUD faded out and no dot or prompt.
            for (int i = _scopes.Count - 1; i >= 0; i--) _scopes[i].Dispose();
            _scopes.Clear();
            _talkDepth = 0; TalkOpen = false;
            GameHud.Cinematic = false;
            DecisionMoment.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _talkDepth = 0; TalkOpen = false; }

        /// <summary>The cards, time skips and talks open now (ModalScope): OnDestroy lets go of what they hold.</summary>
        readonly List<ModalScope> _scopes = new(4);

        static readonly Color ObjBody = Hex("#c9d2d6");
        const float ObjX = 22, ObjW = 420;

        // ---------- layout ----------

        void Build()
        {
            // Effects under everything else.
            _vignette = UiKit.Box("Vignette", _root, new Color(0.35f, 0, 0, 0));
            UiKit.Fill(_vignette.rectTransform);
            _vignette.sprite = RadialSprite();
            // a dim, dusty edge when health drops (not a red flash: the game shows no injuries)
            _hurt = UiKit.Box("Hurt", _root, new Color(HurtTint.r, HurtTint.g, HurtTint.b, 0));
            UiKit.Fill(_hurt.rectTransform);
            _hurt.sprite = _vignette.sprite;

            _letterTop = UiKit.Box("LetterTop", _root, Color.black).gameObject;
            Anchor((RectTransform)_letterTop.transform, new Vector2(0, 1), new Vector2(1, 1), 0, 110);
            _letterBottom = UiKit.Box("LetterBottom", _root, Color.black).gameObject;
            Anchor((RectTransform)_letterBottom.transform, new Vector2(0, 0), new Vector2(1, 0), 0, 110);
            _letterTop.SetActive(false); _letterBottom.SetActive(false);

            _hud = UiKit.Rect("Hud", _root); UiKit.Fill(_hud);
            _hudGroup = _hud.gameObject.AddComponent<CanvasGroup>();
            _hudGroup.blocksRaycasts = false; _hudGroup.interactable = false;

            // Top-left: chapter, clock, objective.
            var tl = UiKit.Rect("TopLeft", _hud); UiKit.At(tl, 28, 24, 560, 520);
            _topLeft = tl.gameObject;
            // A soft dark plate behind the chapter lines: the shadow alone left them ghostly over a pale ceiling.
            _chapterBg = HudTheme.Plate("ChapterBg", tl).rectTransform;
            UiKit.At(_chapterBg, 0, -4, ChapterMinW, 70); // one left edge with the clock and objective, never narrower than the clock
            _chapterNo = UiKit.Label("ChapterNo", tl, "", HudTheme.Eyebrow + 1, UiKit.Amber, style: FontStyle.Bold);
            UiKit.At(_chapterNo.rectTransform, ChapterPad, 5, 560 - ChapterPad, 24);
            _chapterTitle = UiKit.Label("ChapterTitle", tl, "", 22, HudTheme.Text, style: FontStyle.Bold);
            UiKit.At(_chapterTitle.rectTransform, ChapterPad, 28, 560 - ChapterPad, 34);
            foreach (var t in new[] { _chapterNo, _chapterTitle }) // readable over a bright wall or sky
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            }
            var clockBg = HudTheme.Plate("Clock", tl).rectTransform; UiKit.At(clockBg, 0, 72, ChapterMinW, 40);
            _clock = HudTheme.Shadowed(UiKit.Label("Text", clockBg, "", 20, HudTheme.Text, TextAnchor.MiddleCenter));
            UiKit.Fill(_clock.rectTransform);
            // The mission: the one thing on the HUD set in the title size. A teal bar down its left edge marks it out.
            var obj = HudTheme.Plate("Objective", tl).rectTransform; UiKit.At(obj, 0, 124, 460, 230);
            _objective = obj.gameObject;
            var accent = UiKit.Box("Accent", obj, HudTheme.Accent).rectTransform;
            accent.anchorMin = new Vector2(0, 0); accent.anchorMax = new Vector2(0, 1); accent.pivot = new Vector2(0, 0.5f);
            accent.offsetMin = new Vector2(0, 10); accent.offsetMax = new Vector2(4, -10);
            UiKit.At(UiKit.Label("Eyebrow", obj, "현재 목표", HudTheme.Eyebrow, HudTheme.Accent, style: FontStyle.Bold).rectTransform, ObjX, 12, 200, 22);
            _objTitle = HudTheme.Shadowed(UiKit.Label("Title", obj, "", HudTheme.Title, HudTheme.Text, style: FontStyle.Bold)); UiKit.At(_objTitle.rectTransform, ObjX, 34, ObjW, 38);
            _objBody = HudTheme.Shadowed(UiKit.Label("Body", obj, "", HudTheme.Body, ObjBody)); UiKit.At(_objBody.rectTransform, ObjX, 74, ObjW, 56);
            _objSteps = HudTheme.Shadowed(UiKit.Label("Steps", obj, "", HudTheme.Body, HudTheme.Text)); UiKit.At(_objSteps.rectTransform, ObjX, 130, ObjW, 96);
            // The amber border that flashes twice when the goal changes (style.css .hud-objective.flash).
            var fl = UiKit.Rect("Flash", obj); UiKit.Fill(fl, -3, -3);
            _objFlash = fl.gameObject.AddComponent<CanvasGroup>(); _objFlash.alpha = 0; _objFlash.blocksRaycasts = false;
            foreach (var (a0, a1, o0, o1) in new[]
            {
                (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), Vector2.zero), (new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 3)),
                (new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(3, 0)), (new Vector2(1, 0), new Vector2(1, 1), new Vector2(-3, 0), Vector2.zero),
            })
            {
                var bar = UiKit.Box("Edge", fl, UiKit.Amber).rectTransform;
                bar.anchorMin = a0; bar.anchorMax = a1; bar.offsetMin = o0; bar.offsetMax = o1;
            }
            _objective.SetActive(false);

            // Top-right: battery and bag chips, toasts under them.
            _batteryChip = Chip("Battery", -28, 24, 170, out _battery);
            _bagChip = Chip("Bag", -210, 24, 250, out _bag);
            var meter = UiKit.Box("Meter", _bagChip.transform, new Color(1, 1, 1, 0.12f)).rectTransform;
            meter.anchorMin = new Vector2(0, 0); meter.anchorMax = new Vector2(1, 0); meter.pivot = new Vector2(0.5f, 0);
            meter.offsetMin = new Vector2(14, 6); meter.offsetMax = new Vector2(-14, 12);
            _bagFill = UiKit.Box("Fill", meter, UiKit.Teal);
            _bagFill.rectTransform.anchorMin = Vector2.zero; _bagFill.rectTransform.anchorMax = new Vector2(0, 1);
            _bagFill.rectTransform.offsetMin = _bagFill.rectTransform.offsetMax = Vector2.zero;
            _batteryChip.SetActive(false); _bagChip.SetActive(false);
            // The torch glyph left of the battery chip while the phone light is on (SetTorch).
            var torch = HudTheme.Plate("Torch", _hud).rectTransform;
            torch.anchorMin = torch.anchorMax = torch.pivot = new Vector2(1, 1); torch.anchoredPosition = new Vector2(-206, -24); torch.sizeDelta = new Vector2(44, 44);
            var ti = UiKit.Icon("Glyph", torch, "\U0001F526", 22, UiKit.Amber); UiKit.Fill(ti.rectTransform, 8, 8);
            _torchChip = torch.gameObject;
            _torchChip.SetActive(false);

            _toasts = UiKit.Rect("Toasts", _hud);
            _toasts.anchorMin = _toasts.anchorMax = _toasts.pivot = new Vector2(1, 1);
            _toasts.anchoredPosition = new Vector2(-28, -84); _toasts.sizeDelta = new Vector2(560, 600);
            var v = _toasts.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.childAlignment = TextAnchor.UpperRight; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;

            // Bottom-left: vitals. Health and stamina as meters, smoke exposure only while there is smoke, then the slow
            // needs (thirst, fatigue, tension) as coloured words.
            var st = HudTheme.Plate("Stats", _hud).rectTransform;
            st.anchorMin = st.anchorMax = st.pivot = new Vector2(0, 0); st.anchoredPosition = new Vector2(HudTheme.Edge, HudTheme.Edge);
            _stats = st.gameObject; _statsRt = st;
            _healthRow = Meter(st, "Health", "체력");
            _staminaRow = Meter(st, "Stamina", "기력");
            _staminaRow.Fill.color = StaminaOk;
            _staminaFill = _staminaRow.Fill;
            _exposureRow = Meter(st, "Exposure", "연기 노출");
            _statsRule = UiKit.Box("Rule", st, HudTheme.Hairline).rectTransform;
            for (int i = 1; i < StatRows.Length; i++)
            {
                var t = UiKit.Label(StatRows[i].key, st, "", HudTheme.Eyebrow, HudTheme.Muted, TextAnchor.MiddleLeft);
                _miniStats[StatRows[i].key] = t;
            }
            LayoutVitals(false);

            BuildMapParts();
            BuildBanner();

            // Caption: bottom centre, above the prompt.
            var cap = HudTheme.Plate("Caption", _root, dense: true).rectTransform;
            cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0.5f, 0); cap.anchoredPosition = new Vector2(0, 300); cap.sizeDelta = new Vector2(1100, 86);
            _caption = cap.gameObject;
            _captionSpeaker = UiKit.Label("Speaker", cap, "", 20, UiKit.Amber, style: FontStyle.Bold); UiKit.At(_captionSpeaker.rectTransform, 24, 10, 1052, 26);
            _captionText = UiKit.Label("Text", cap, "", 24, UiKit.Ink); UiKit.At(_captionText.rectTransform, 24, 38, 1052, 44);
            foreach (var t in new[] { _captionSpeaker, _captionText }) { var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(1, -1); }
            _caption.SetActive(false);

            // The smoke escape's key hint (original hud.js smokeHint): why to get low, while the smoke grade is up.
            var hint = HudTheme.Plate("SmokeHint", _hud, dense: true).rectTransform;
            hint.anchorMin = hint.anchorMax = hint.pivot = new Vector2(0.5f, 0); hint.anchoredPosition = new Vector2(0, 164); hint.sizeDelta = new Vector2(400, 48);
            var hintKey = UiKit.Box("Key", hint, new Color(1, 1, 1, 0.14f)).rectTransform; UiKit.At(hintKey, 12, 8, 32, 32);
            UiKit.Fill(UiKit.Label("K", hintKey, "C", 20, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
            UiKit.At(UiKit.Label("Text", hint, "몸을 낮추면 연기를 덜 마신다", 20, UiKit.Ink, TextAnchor.MiddleLeft).rectTransform, 56, 4, 336, 40);
            _smokeHint = hint.gameObject;
            _smokeHint.SetActive(false);

            _fade = UiKit.Box("Fade", _root, new Color(0, 0, 0, 0));
            UiKit.Fill(_fade.rectTransform);
            _fade.raycastTarget = false;
        }

        void BuildMapParts()
        {
            // Compass strip: top centre (360 x 34 in the original, x1.3 here like the rest of the HUD).
            var cr = UiKit.Rect("Compass", _hud);
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 1); cr.anchoredPosition = new Vector2(0, -16); cr.sizeDelta = new Vector2(470, 44);
            cr.gameObject.AddComponent<CanvasRenderer>();
            _compass = cr.gameObject.AddComponent<HudCompass>();
            _compass.raycastTarget = false;
            cr.gameObject.SetActive(false);

            // Minimap: top right under the battery / bag chips, a dark disc with the map clipped to it.
            _mapRoot = UiKit.Rect("Minimap", _hud);
            _mapRoot.anchorMin = _mapRoot.anchorMax = _mapRoot.pivot = new Vector2(1, 1);
            _mapRoot.anchoredPosition = new Vector2(-28, -MapTop); _mapRoot.sizeDelta = new Vector2(MapSize, MapSize);
            var shadow = UiKit.Box("Shadow", _mapRoot, new Color(0, 0, 0, 0.35f));
            shadow.sprite = HudDraw.DiscSprite; UiKit.Fill(shadow.rectTransform, -6, -6); shadow.rectTransform.anchoredPosition = new Vector2(0, -4);
            var disc = UiKit.Box("Disc", _mapRoot, new Color32(8, 12, 15, 230));
            disc.sprite = HudDraw.DiscSprite; UiKit.Fill(disc.rectTransform);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var mr = UiKit.Rect("Map", disc.transform); UiKit.Fill(mr);
            mr.gameObject.AddComponent<CanvasRenderer>();
            _minimap = mr.gameObject.AddComponent<HudMinimap>();
            _minimap.raycastTarget = false;
            var ring = UiKit.Box("Ring", _mapRoot, new Color(1, 1, 1, 0.22f));
            ring.sprite = HudDraw.RingSprite; UiKit.Fill(ring.rectTransform);
            float e = MapSize / 2 - 14;
            foreach (var (t, x, y, north) in new[] { ("N", 0f, e, true), ("S", 0f, -e, false), ("E", e, 0f, false), ("W", -e, 0f, false) })
            {
                var b = UiKit.Box("Badge" + t, _mapRoot, new Color32(8, 12, 15, 217));
                b.sprite = HudDraw.DiscSprite;
                var rt = b.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(24, 24);
                UiKit.Fill(UiKit.Label("T", rt, t, 16, north ? Hex("#ff5a4a") : Hex("#e8eef0"), TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
            }
            _mapRoot.gameObject.SetActive(false);

            // The key strip (hud-keys): bottom right on a dark rounded plate, from the one key table (UiKeys). It fades
            // after 20 s of roaming and comes back for 8 s on H and on each level entry.
            var plate = UiKit.Box("Keys", _hud, HudTheme.Slate);
            plate.sprite = HudDraw.RoundedSprite; plate.type = Image.Type.Sliced;
            var pr = plate.rectTransform; pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1, 0); pr.anchoredPosition = new Vector2(-28, 24);
            var ph = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            ph.padding = new RectOffset(16, 16, 10, 10); ph.childControlWidth = true; ph.childControlHeight = true; ph.childForceExpandWidth = false; ph.childForceExpandHeight = false;
            var pf = plate.gameObject.AddComponent<ContentSizeFitter>();
            pf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; pf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var keys = UiKit.Label("Text", plate.transform, UiKeys.StripText(6), 16, new Color(0.72f, 0.77f, 0.8f), TextAnchor.LowerRight);
            keys.lineSpacing = 1.15f;
            keys.horizontalOverflow = HorizontalWrapMode.Overflow; // three fixed rows: a key never parts from its word
            _keysGroup = plate.gameObject.AddComponent<CanvasGroup>(); _keysGroup.alpha = 0; _keysGroup.blocksRaycasts = false;
            _keys = plate.gameObject;
            _keys.SetActive(false);
        }

        const float VitalsW = 304, VitalsPad = 14, MeterH = 26, MeterGap = 2;

        MeterRow Meter(RectTransform parent, string name, string label)
        {
            var row = UiKit.Rect(name, parent);
            float w = VitalsW - VitalsPad * 2;
            UiKit.At(HudTheme.Shadowed(UiKit.Label("Label", row, label, HudTheme.Label, HudTheme.Muted, TextAnchor.MiddleLeft)).rectTransform, 0, 0, 84, MeterH);
            var track = UiKit.Box("Track", row, HudTheme.Track).rectTransform; UiKit.At(track, 88, 10, w - 88 - 62, 6);
            var trail = UiKit.Box("Trail", track, new Color(1, 1, 1, 0.6f));
            var fill = UiKit.Box("Fill", track, HudTheme.Accent);
            fill.sprite = HudTheme.GradientSprite;
            foreach (var im in new[] { trail, fill })
            {
                im.rectTransform.anchorMin = Vector2.zero; im.rectTransform.anchorMax = Vector2.one;
                im.rectTransform.offsetMin = im.rectTransform.offsetMax = Vector2.zero;
            }
            var word = UiKit.Label("Word", row, "", HudTheme.Label, HudTheme.Text, TextAnchor.MiddleRight, FontStyle.Bold); // no shadow: it smears small bold text
            UiKit.At(word.rectTransform, w - 58, 0, 58, MeterH);
            return new MeterRow { Root = row, Fill = fill, Trail = trail, Word = word };
        }

        /// <summary>Stacks the vitals rows; the exposure meter takes a row only while there is smoke.</summary>
        void LayoutVitals(bool exposure)
        {
            float w = VitalsW - VitalsPad * 2, y = 12;
            foreach (var row in new[] { _healthRow, _staminaRow, _exposureRow })
            {
                bool on = row != _exposureRow || exposure;
                SetActive(row.Root.gameObject, on);
                if (!on) continue;
                UiKit.At(row.Root, VitalsPad, y, w, MeterH);
                y += MeterH + MeterGap;
            }
            y += 4;
            UiKit.At(_statsRule, VitalsPad, y, w, 1);
            y += 7;
            int i = 0;
            foreach (var t in _miniStats.Values) UiKit.At(t.rectTransform, VitalsPad + i++ * (w / 3f), y, w / 3f, 24);
            _statsRt.sizeDelta = new Vector2(VitalsW, y + 24 + 10);
        }

        static void SetMeter(MeterRow row, float k, Color color, string word)
        {
            k = Mathf.Clamp01(k);
            if (k < row.Shown - 0.004f) row.LossAt = Time.unscaledTime; // the trail holds, then drains
            if (k > row.TrailK) row.TrailK = k;
            row.Shown = k;
            row.Fill.rectTransform.anchorMax = new Vector2(k, 1);
            row.Fill.color = color;
            if (row.Word.text != word) row.Word.text = word;
            row.Word.color = color;
        }

        void PlayHeart(float volume)
        {
            if (!_heart)
            {
                _heart = gameObject.AddComponent<AudioSource>();
                _heart.playOnAwake = false; _heart.spatialBlend = 0; _heart.priority = 40;
            }
            float v = volume * AiOn.Settings.AudioBus.Effects;
            if (v > 0.001f) _heart.PlayOneShot(AiOn.Story.ProceduralAudio.Heartbeat(), v);
        }

        static void SetActive(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }

        void BuildBanner()
        {
            // The chapter banner (hud.js .hud-banner): centred near 30 % from the top, the number in amber over the title.
            var b = UiKit.Rect("Banner", _hud);
            b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 1); b.anchoredPosition = new Vector2(0, -290); b.sizeDelta = new Vector2(1400, 120);
            _bannerGroup = b.gameObject.AddComponent<CanvasGroup>(); _bannerGroup.alpha = 0; _bannerGroup.blocksRaycasts = false;
            // a soft dark pool behind the words: they read over a white wall or sky
            // A faint pool sized to the words (SizeBannerBacking): a stand-in for the original's wide blurred text-shadow,
            // not a visible plate.
            var pool = UiKit.Box("Backing", b, new Color(0.02f, 0.03f, 0.04f, 0.32f));
            pool.sprite = HudDraw.SoftSprite;
            var pr = pool.rectTransform; pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = new Vector2(0, -4); pr.sizeDelta = new Vector2(700, 200);
            _bannerBacking = pr;
            _bannerNo = UiKit.Label("No", b, "", 22, UiKit.Amber, TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.At(_bannerNo.rectTransform, 0, 0, 1400, 32);
            _bannerTitle = UiKit.Label("Title", b, "", 52, UiKit.Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.At(_bannerTitle.rectTransform, 0, 36, 1400, 72);
            foreach (var t in new[] { _bannerNo, _bannerTitle })
            {
                var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(2, -2);
                var wide = t.gameObject.AddComponent<Outline>(); wide.effectColor = new Color(0, 0, 0, 0.28f); wide.effectDistance = new Vector2(3, -3);
            }
            _banner = b.gameObject;
            _banner.SetActive(false);
        }

        RectTransform _bannerBacking;

        void SizeBannerBacking()
        {
            float w = Mathf.Max(_bannerNo.preferredWidth, _bannerTitle.preferredWidth);
            _bannerBacking.sizeDelta = new Vector2(Mathf.Clamp(w * 1.5f + 120, 360, 1100), 200);
        }

        GameObject Chip(string name, float x, float y, float w, out Text text)
        {
            var c = HudTheme.Plate(name, _hud).rectTransform;
            c.anchorMin = c.anchorMax = c.pivot = new Vector2(1, 1); c.anchoredPosition = new Vector2(x, -y); c.sizeDelta = new Vector2(w, 44);
            text = HudTheme.Shadowed(UiKit.Label("Text", c, "", HudTheme.Value, HudTheme.Text, TextAnchor.MiddleCenter));
            UiKit.Fill(text.rectTransform, 10, 4);
            return c.gameObject;
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, float x, float h)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, min.y);
            rt.offsetMin = new Vector2(x, 0); rt.offsetMax = new Vector2(-x, 0); rt.sizeDelta = new Vector2(0, h);
            rt.anchoredPosition = Vector2.zero;
        }

        static Sprite RadialSprite()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float a = Mathf.SmoothStep(0.35f, 1.15f, Mathf.Sqrt(dx * dx + dy * dy));
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        // ---------- IStoryHud ----------

        public void SetClock(string text) => _clock.text = text ?? "";

        public void SetStats(Stats s)
        {
            if (s == null) return;
            _health = (float)s.Health;
            string hw = GameRules.StatWord("health", s.Health); // the one word table, shared with the cards and the debrief
            SetMeter(_healthRow, _health / 100f, HudTheme.StatColor(hw), hw);
            foreach (var (key, label, _) in StatRows)
            {
                if (!_miniStats.TryGetValue(key, out var t)) continue;
                double v = key switch { "thirst" => s.Thirst, "fatigue" => s.Fatigue, _ => s.Tension };
                string w = GameRules.StatWord(key, v);
                string text = $"{label}  <b><color=#{ColorUtility.ToHtmlStringRGB(HudTheme.StatColor(w))}>{w}</color></b>";
                if (t.text != text) t.text = text;
            }
        }

        public void SetBattery(double percent)
        {
            _batteryWanted = true;
            ShowPart(_batteryChip, !_hidden);
            int p = (int)Math.Floor(PhoneLogic.ClampBattery(percent) + 0.5); // the phone screen's rounding (JS Math.round), so both read the same %
            _battery.text = $"휴대폰 {p}%";
            _battery.color = p <= 15 ? Red : p <= 30 ? UiKit.Amber : UiKit.Ink;
        }

        /// <summary>Hides the phone chip until the next SetBattery (someone else's POV: it is not their phone).</summary>
        public void HideBattery()
        {
            _batteryWanted = false;
            ShowPart(_batteryChip, false);
        }

        public void SetBag(double? weightKg, double limitKg)
        {
            _bagWanted = weightKg.HasValue;
            ShowPart(_bagChip, weightKg.HasValue && !_hidden);
            if (!weightKg.HasValue) return;
            _bag.text = $"가방 {BagDrill.FormatKg(weightKg.Value)} / {BagDrill.FormatKg(limitKg)}";
            float k = (float)(weightKg.Value / Math.Max(0.01, limitKg));
            _bagFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
            _bagFill.color = k > 1 ? Red : k > 0.8f ? UiKit.Amber : UiKit.Teal;
        }

        public void SetTension(double amount01) => _tension = Mathf.Clamp01((float)amount01);
        public void SetStamina(double amount01) => SetStamina(amount01, false);

        /// <summary>hud.setStamina(v, winded): the bar fills with what is left and turns orange while you are winded.</summary>
        public void SetStamina(double amount01, bool winded)
        {
            float k = Mathf.Clamp01((float)amount01);
            _staminaWanted = k <= 0.995f || winded;
            if (Mathf.Abs(k - _staminaShown) > 0.002f) { _staminaShown = k; _staminaFill.rectTransform.anchorMax = new Vector2(k, 1); _staminaRow.Trail.rectTransform.anchorMax = new Vector2(k, 1); }
            if (winded != _windedShown)
            {
                _windedShown = winded;
                _staminaFill.color = winded ? StaminaWinded : StaminaOk;
                _staminaRow.Word.text = winded ? "숨참" : ""; _staminaRow.Word.color = StaminaWinded;
            }
        }
        public void Prompt(string label) { } // the interact prompt is GameHud's

        /// <summary>
        /// A label-tagged toast. While a modal window is open it waits and comes out after it closes (0.6 s apart); the
        /// "메모:" echo of an alert the popup shows is dropped; at most two official toasts are up at once.
        /// </summary>
        public void Toast(string text, string label, int ms)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_toastQ.IsAlertEcho(text)) return;
            if (UiScreen.OpenCount > 0 || _toastQ.Pending > 0) { _toastQ.Enqueue(text, label, ms); return; }
            ShowToast(text, label, ms);
        }

        /// <summary>The alert popup's text (the phone's 안전안내문자 / 위급재난문자): a "메모:" toast repeating it is dropped.</summary>
        public void SetAlert(string text) => _toastQ.AlertText = text;

        void ShowToast(string text, string label, int ms)
        {
            var key = label != null && Labels.ContainsKey(label) ? label : "self";
            var (tagText, tagColor) = Labels[key];
            // At most two official notices on screen: the oldest one makes way.
            _toastLabels.Clear();
            for (int i = 0; i < _toasts.childCount; i++) _toastLabels.Add(_toasts.GetChild(i).name);
            int evict = ToastQueue.EvictIndex(_toastLabels, key);
            if (evict >= 0) Detach(_toasts.GetChild(evict));
            var bg = HudTheme.Plate(key, _toasts, dense: true);
            var fit = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            fit.padding = new RectOffset(16, 16, 10, 12); fit.spacing = 2; fit.childControlHeight = true; fit.childControlWidth = true; fit.childForceExpandHeight = false;
            UiKit.Label("Tag", bg.transform, tagText, 16, tagColor, style: FontStyle.Bold);
            UiKit.Label("Text", bg.transform, text, 20, UiKit.Ink);
            bg.transform.SetAsLastSibling();
            // Destroy is deferred to the end of the frame: detach first, or childCount never drops and this never ends
            // (a fifth toast while four are up froze the game in the smoke escape).
            while (_toasts.childCount > 4) Detach(_toasts.GetChild(0));
            Destroy(bg.gameObject, Mathf.Max(1.2f, ms / 1000f));
        }

        static void Detach(Transform t)
        {
            t.SetParent(null, false);
            Destroy(t.gameObject);
        }

        public async Task Caption(string text, string speaker = null, int? ms = null)
        {
            int token = ++_captionToken;
            _caption.SetActive(!string.IsNullOrEmpty(text));
            _captionSpeaker.text = speaker ?? "";
            _captionText.text = text ?? "";
            int wait = ms ?? Math.Min(7000, 1500 + (text?.Length ?? 0) * 60);
            await Wait(wait / 1000f);
            if (token == _captionToken) _caption.SetActive(false);
        }

        public void ClearCaption() { _captionToken++; _caption.SetActive(false); }

        public void SetObjective(string title, string body, IReadOnlyList<ObjectiveStep> steps)
        {
            _objective.SetActive(!string.IsNullOrEmpty(title) && !_hidden);
            _objTitle.text = title ?? "";
            _objBody.text = body ?? "";
            _objSteps.text = StepsText(steps);
            // The panel fits its text: no empty box under a one-line goal, no steps spilling out under a long one.
            float bodyH = string.IsNullOrEmpty(_objBody.text) ? 0 : Mathf.Ceil(_objBody.preferredHeight);
            float stepsH = string.IsNullOrEmpty(_objSteps.text) ? 0 : Mathf.Ceil(_objSteps.preferredHeight);
            float y = 74 + bodyH + (bodyH > 0 ? 8 : 0);
            UiKit.At(_objBody.rectTransform, ObjX, 74, ObjW, Mathf.Max(1, bodyH));
            UiKit.At(_objSteps.rectTransform, ObjX, y, ObjW, Mathf.Max(1, stepsH));
            UiKit.At((RectTransform)_objective.transform, 0, 124, 460, y + stepsH + 16);
            // A new goal or a step ticked: the plate's border flashes amber twice (hud.js .flash).
            var key = string.IsNullOrEmpty(title) ? null : title + "\n" + _objSteps.text;
            if (key != null && key != _lastObjective) { _flashPending = true; _flashAt = -100; } // Update starts it where it can be seen
            _lastObjective = key;
        }

        /// <summary>
        /// The objective's step lines: ✓ done, ○ to do; exclusive options (ObjectiveStep.Choice) are ◇ under one
        /// "다음 중 하나를 고르세요" line, so six endings do not read as six boxes to tick.
        /// </summary>
        public static string StepsText(IReadOnlyList<ObjectiveStep> steps)
        {
            if (steps == null || steps.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            bool choiceHead = false;
            foreach (var s in steps)
            {
                if (s == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                if (s.Choice)
                {
                    if (!choiceHead) { choiceHead = true; sb.Append("<color=#9aa6ad>다음 중 하나를 고르세요</color>\n"); }
                    sb.Append(s.Done ? "<color=#3cc6b5>◆</color>  <color=#9aa6ad>" + s.Text + "</color>" : "<color=#f2b33d>◇</color>  " + s.Text);
                }
                else sb.Append(s.Done ? "<color=#3cc6b5>✓</color>  <color=#9aa6ad>" + s.Text + "</color>" : "○  " + s.Text);
            }
            return sb.ToString();
        }

        public void SetChapter(string title, string subtitle)
        {
            _chapterNo.text = title ?? ""; _chapterTitle.text = subtitle ?? "";
            var w = Mathf.Max(_chapterNo.preferredWidth, _chapterTitle.preferredWidth);
            _chapterBg.gameObject.SetActive(w > 0);
            _chapterBg.sizeDelta = new Vector2(Mathf.Clamp(Mathf.Ceil(w) + ChapterPad * 2, ChapterMinW, 560), _chapterBg.sizeDelta.y);
            // A new chapter: the centred banner (hud.js setChapter). While the HUD is hidden it waits for Show().
            var key = (title ?? "") + "|" + (subtitle ?? "");
            if (key == _lastChapter || w <= 0) return;
            _lastChapter = key;
            _bannerNo.text = title ?? ""; _bannerTitle.text = subtitle ?? "";
            SizeBannerBacking();
            // It waits while the HUD is hidden or a window / card is up (the HUD layer is faded out then): Update starts it.
            _bannerPending = true; _bannerAt = -100;
        }

        /// <summary>The phone light on / off: a small torch glyph by the battery chip (StoryDirector.ToggleFlashlight).</summary>
        public void SetTorch(bool on)
        {
            if (!_torchChip) return;
            _torchOn = on;
            ShowPart(_torchChip, on && !_hidden);
            // the bag chip moves over to make room
            var bag = (RectTransform)_bagChip.transform;
            bag.anchoredPosition = new Vector2(on ? -262 : -210, bag.anchoredPosition.y);
        }

        /// <summary>A HUD part on/off; during a cinematic only remembered for when the letterbox lifts.</summary>
        void ShowPart(GameObject go, bool on)
        {
            if (_cineHidden.ContainsKey(go)) _cineHidden[go] = on;
            else go.SetActive(on);
        }

        /// <summary>
        /// Letterbox on/off. Like the original (.hud.cinematic > * { opacity: 0 }) everything but the bars goes: the chapter
        /// and clock would sit under the cine title, the toasts over the shot. Off puts back what was showing.
        /// </summary>
        public void SetCinematic(bool on)
        {
            if (!this || !_letterTop) return; // play mode tearing down while a run starts (the landing resolves on destroy)
            _letterTop.SetActive(on); _letterBottom.SetActive(on);
            _cinematic = on;
            GameHud.Cinematic = on; // every GameHud (the persistent one, a level's own) drops the dot and the prompt
            if (_cineParts == null) _cineParts = new[] { _topLeft, _toasts.gameObject, _stats, _batteryChip, _bagChip, _torchChip };
            if (on)
            {
                foreach (var go in _cineParts)
                {
                    if (!_cineHidden.ContainsKey(go)) _cineHidden[go] = go.activeSelf;
                    go.SetActive(false);
                }
                return;
            }
            foreach (var kv in _cineHidden) if (kv.Key) kv.Key.SetActive(kv.Value);
            _cineHidden.Clear();
            // a hidden HUD (after the run, the gallery, the records) stays hidden when the letterbox lifts
            _topLeft.SetActive(!_hidden); _toasts.gameObject.SetActive(true);
            _stats.SetActive(!_hidden);
            if (_mapWanted) _mapDirty = true;
        }

        GameObject[] _cineParts;

        public void Hurt(double amount01) => _hurtLevel = Mathf.Clamp01(_hurtLevel + (float)amount01);
        public void Show()
        {
            // puts back what the story set while hidden (the overlay starts hidden, so the first objective may come first)
            _hidden = false;
            ShowPart(_topLeft, true);
            ShowPart(_stats, true);
            ShowPart(_batteryChip, _batteryWanted);
            ShowPart(_bagChip, _bagWanted);
            ShowPart(_torchChip, _torchOn);
            _objective.SetActive(!string.IsNullOrEmpty(_objTitle.text));
            if (_mapWanted) _mapDirty = true;
        }

        /// <summary>The whole roaming HUD goes: chapter and clock, objective, chips, stats, compass, minimap, keys, banner.</summary>
        public void Hide()
        {
            _hidden = true;
            _objective.SetActive(false);
            ShowPart(_topLeft, false);
            ShowPart(_stats, false); ShowPart(_batteryChip, false); ShowPart(_bagChip, false); ShowPart(_torchChip, false);
            _bannerAt = -100;
        }

        public void SetMap(Newtonsoft.Json.Linq.JObject map)
        {
            _mapWanted = map != null;
            _lastMap = map;
            if (map == null) return;
            if (_hidden || _cinematic) { _mapDirty = true; return; }
            DrawMap(map);
        }

        void DrawMap(Newtonsoft.Json.Linq.JObject map)
        {
            _mapDirty = false;
            _mapExtra.Clear();
            MapExtraSolids?.Invoke(_mapExtra);
            _minimap.SetMap(map, MapLayout?.Invoke((string)map["level"]), MapSolidOff, _mapExtra);
        }

        /// <summary>Heading in degrees clockwise from north; null hides the strip.</summary>
        public void SetCompass(double? yaw)
        {
            _compassWanted = yaw.HasValue;
            if (yaw.HasValue && !_hidden && !_cinematic) _compass.SetHeading(yaw.Value);
        }

        /// <summary>Black over the whole screen (0..1) over seconds.</summary>
        public async Task FadeTo(double black, double seconds)
        {
            float from = _fade.color.a, to = Mathf.Clamp01((float)black), t = 0, dur = Mathf.Max(0.001f, (float)seconds);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t / dur));
                await Task.Yield();
            }
            _fade.color = new Color(0, 0, 0, to);
        }

        void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            _hurtLevel = Mathf.MoveTowards(_hurtLevel, 0, dt * 0.8f);
            _hurt.color = new Color(HurtTint.r, HurtTint.g, HurtTint.b, _hurtLevel * 0.55f);
            // Tension edge: a muted, capped red — at 0.45 a saturated maroon swallowed the dark blackout scenes.
            bool smoke = AiOn.Levels.EnvironmentController.SmokeActive && !_hidden;
            if (_smokeHint.activeSelf != smoke) _smokeHint.SetActive(smoke);

            // Danger (tension, low health, smoke): the red edge beats with a heartbeat that quickens as it rises. One
            // signal, two channels (sight and sound); under reduce-motion the edge holds steady instead of pulsing.
            bool reduceFx = AiOn.Rendering.AtmosphereDirector.ReduceMotion;
            bool quiet = _hidden || _cinematic || UiScreen.OpenCount > 0;
            _danger = HudMotion.Approach(_danger, quiet ? 0 : DangerMath.Level(_tension, _health, AiOn.Levels.EnvironmentController.SmokeLevel), 2f, dt);
            _sinceBeat += dt;
            if (_danger > DangerMath.HeartFrom && !quiet)
            {
                if (_sinceBeat >= _beatEvery)
                {
                    _sinceBeat = 0;
                    _beatEvery = 60f / DangerMath.Bpm(_danger);
                    PlayHeart(DangerMath.HeartVolume(_danger));
                }
            }
            else _beatEvery = 0; // the next one lands as soon as the danger is back
            _vignette.color = new Color(0.18f, 0.05f, 0.05f, DangerMath.EdgeAlpha(_tension, _danger, DangerMath.Beat(_sinceBeat), reduceFx));

            // Vitals: the exposure meter while there is smoke, the health trail draining after a loss.
            if (smoke != _exposureOn) { _exposureOn = smoke; LayoutVitals(smoke); }
            if (smoke)
            {
                float lv = AiOn.Levels.EnvironmentController.SmokeLevel;
                SetMeter(_exposureRow, lv, lv > 0.66f ? HudTheme.Danger : lv > 0.33f ? HudTheme.Warn : HudTheme.Muted, lv > 0.66f ? "위험" : lv > 0.33f ? "주의" : "낮음");
                _exposureRow.Trail.rectTransform.anchorMax = new Vector2(lv, 1);
            }
            float trail = HudMotion.Trail(_healthRow.TrailK, _healthRow.Shown, now - _healthRow.LossAt, dt);
            if (trail != _healthRow.TrailK || _healthRow.Trail.rectTransform.anchorMax.x != trail)
            {
                _healthRow.TrailK = trail;
                _healthRow.Trail.rectTransform.anchorMax = new Vector2(trail, 1);
            }

            // Under a modal window the roaming HUD fades out (0.15 s) and comes back after; the talk box and captions stay.
            float layer = HudVisibility.LayerAlpha(UiScreen.OpenCount);
            if (_hudGroup.alpha != layer) _hudGroup.alpha = Mathf.MoveTowards(_hudGroup.alpha, layer, dt / 0.15f);
            if (_toastQ.TryNext(now, UiScreen.OpenCount > 0, out var queued)) ShowToast(queued.Text, queued.Label, queued.Ms);

            // A new level: the minimap drops the last level's plan at once and redraws from the next frame for this one.
            var host = AiOn.Story.UnityStoryHost.Instance;
            var level = host ? host.LevelId : null;
            if (!ReferenceEquals(level, _hostLevel) && level != _hostLevel)
            {
                _hostLevel = level;
                _minimap.Clear();
                _mapDirty = true;
                _keyTimer.Show(KeyStripTimer.Again);
            }

            bool up = !_hidden && !_cinematic;
            SetActive(_compass.gameObject, HudVisibility.Part(_compassWanted, _hidden, _cinematic));
            bool map = HudVisibility.Part(_mapWanted, _hidden, _cinematic);
            if (map != _mapRoot.gameObject.activeSelf)
            {
                _mapRoot.gameObject.SetActive(map);
                _toasts.anchoredPosition = new Vector2(-28, -(map ? MapTop + MapSize + 18 : ToastTop)); // toasts go under the map
            }
            if (map && _mapDirty && _lastMap != null && (level == null || (string)_lastMap["level"] == level)) DrawMap(_lastMap);

            // The key strip: free roam only (a dialog or menu has its own keys), for a while after start, H and level entry.
            var kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) _keyTimer.Show(KeyStripTimer.Again);
            bool roaming = up && _compassWanted && !InputGate.Blocked;
            bool keys = HudVisibility.KeyStrip(up, roaming, _keyTimer.Tick(roaming, dt)) && !UiKeys.StripSuppressed;
            float ka = Mathf.MoveTowards(_keysGroup.alpha, keys ? 1 : 0, dt / (keys ? 0.25f : 0.8f));
            if (ka != _keysGroup.alpha) _keysGroup.alpha = ka;
            SetActive(_keys, keys || ka > 0);

            // chapter banner and the objective's flash: each starts once the HUD is up and no window covers it
            if (ChapterBanner.ShouldStart(_bannerPending, _hidden, UiScreen.OpenCount)) { _bannerPending = false; _bannerAt = now; }
            if (ChapterBanner.ShouldStart(_flashPending, _hidden || _cinematic, UiScreen.OpenCount)) { _flashPending = false; _flashAt = now; }
            bool reduce = host && host.ReduceMotion;
            float ba = up || _cinematic ? ChapterBanner.Alpha(now - _bannerAt, reduce) : 0;
            if (_hidden) ba = 0;
            SetActive(_banner, ba > 0);
            if (ba > 0) _bannerGroup.alpha = ba;
            float fa = reduce ? 0 : ChapterBanner.Flash(now - _flashAt);
            if (fa != _objFlash.alpha) _objFlash.alpha = fa;
        }

        static async Task Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) await Task.Yield();
        }

        // ---------- dialog, cards, time skips (IStoryUi typed part) ----------

        /// <summary>Lines one at a time (Enter / Space / E / click), then the choices (click or 1-9). Returns the choice id.</summary>
        /// <summary>Raised when a dialog opens: speaker and role (TalkFacing turns the player toward that person).</summary>
        public static event System.Action<string, string> TalkStarted;
        /// <summary>Raised when that dialog closes (TalkFacing hands the camera back).</summary>
        public static event System.Action TalkEnded;

        public async Task<string> Talk(TalkOptions o)
        {
            try { TalkStarted?.Invoke(o.Speaker, o.Role); } catch (System.Exception e) { Debug.LogException(e); }
            _talkDepth++; TalkOpen = true;
            try { return await TalkBody(o); }
            finally
            {
                _talkDepth = Mathf.Max(0, _talkDepth - 1); TalkOpen = _talkDepth > 0;
                try { TalkEnded?.Invoke(); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        static int _talkDepth;
        const float TalkWidth = 1240, TalkBottom = 50, TalkMinH = 140, TalkMaxH = 300, ChoiceMinW = 520, ChoiceMaxW = 1000;
        static readonly Color ChoiceBg = new(0.12f, 0.17f, 0.19f, 0.98f), ChoiceOffBg = new(0.07f, 0.09f, 0.1f, 0.98f);
        static readonly Color ChoiceTaken = new(0.55f, 0.6f, 0.62f), ChoiceLocked = new(0.45f, 0.49f, 0.51f);
        // a Button tint multiplier: ChoiceBg (0.12, 0.17, 0.19) becomes about (0.24, 0.2, 0.13), a warm amber-brown
        static readonly Color ChoiceHover = new(2.0f, 1.2f, 0.68f, 1f);

        /// <summary>The speaker name the story gives a choice the player makes alone, under pressure.</summary>
        public static bool IsJudgement(string speaker) => speaker == "판단";

        /// <summary>The talk box sized to its line (140 to 300 px), at the bottom: the centre stays free for the speaker.</summary>
        async Task<string> TalkBody(TalkOptions o)
        {
            var lines = new List<string>();
            foreach (var l in o.Lines ?? new List<string>()) if (!string.IsNullOrEmpty(l)) lines.Add(l);
            if (lines.Count == 0) lines.Add("…");
            bool hasChoices = o.Choices != null && o.Choices.Count > 0;
            using var modal = new ModalScope(this, hasChoices);
            // A judgement under pressure: time slows and the world blurs behind the choices (DecisionMoment).
            using var moment = hasChoices && IsJudgement(o.Speaker) ? DecisionMoment.Begin() : null;
            var box = UiKit.Box("Talk", _root, new Color(0.04f, 0.06f, 0.07f, 0.97f)).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0); box.anchoredPosition = new Vector2(0, TalkBottom); box.sizeDelta = new Vector2(TalkWidth, TalkMinH);
            try
            {
                // A miss (the explanation after a wrong pick) is amber with a △, not the usual teal name.
                bool miss = o.Tone == "miss";
                var speaker = UiKit.Label("Speaker", box, (miss ? "△ " : "") + (o.Speaker ?? ""), 30, miss ? UiKit.Amber : UiKit.Teal, style: FontStyle.Bold);
                UiKit.At(speaker.rectTransform, 34, 20, 940, 40);
                bool hasRole = !string.IsNullOrEmpty(o.Role);
                var role = UiKit.Label("Role", box, o.Role ?? "", 21, UiKit.Dim); UiKit.At(role.rectTransform, 34, 60, 940, 30);
                role.gameObject.SetActive(hasRole);
                var count = UiKit.Label("Count", box, "", 20, UiKit.Dim, TextAnchor.UpperRight); UiKit.At(count.rectTransform, TalkWidth - 34 - 210, 22, 210, 30);
                float lineTop = hasRole ? 100 : 68, lineW = TalkWidth - 68;
                var line = UiKit.Label("Line", box, "", 28, UiKit.Ink); UiKit.At(line.rectTransform, 34, lineTop, lineW, 40);
                for (int i = 0; i < lines.Count; i++)
                {
                    line.text = lines[i];
                    float lh = Mathf.Ceil(line.preferredHeight);
                    line.rectTransform.sizeDelta = new Vector2(lineW, lh);
                    box.sizeDelta = new Vector2(TalkWidth, Mathf.Clamp(lineTop + lh + 30, TalkMinH, TalkMaxH));
                    count.text = lines.Count > 1 ? $"{i + 1} / {lines.Count}" : o.Progress ?? "";
                    bool last = i == lines.Count - 1;
                    if (last && hasChoices) break;
                    await Advance();
                }
                if (!hasChoices) return null;
                return await Choose(box, o.Choices);
            }
            finally { Destroy(box.gameObject); }
        }

        /// <summary>
        /// The choices stacked right above the talk box (8 px gap), one column as wide as the longest (520 to 1000 px),
        /// opaque. Taken (TalkChoice.Taken) reads "✓" and dim; any other disabled choice is dimmer with a "⊘" and its
        /// reason, if it has one, on a smaller second line; an open choice's note sits on that line too.
        /// </summary>
        async Task<string> Choose(RectTransform box, List<TalkChoice> choices)
        {
            var list = UiKit.Rect("Choices", _root);
            list.anchorMin = list.anchorMax = list.pivot = new Vector2(0.5f, 0);
            list.anchoredPosition = new Vector2(0, TalkBottom + box.sizeDelta.y + 8);
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tcs = new TaskCompletionSource<string>();
            var texts = new List<Text>(choices.Count);
            var buttons = new List<Button>(choices.Count);
            float widest = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var (head, why) = ChoiceParts(c);
                bool taken = c.Disabled && c.Taken, locked = c.Disabled && !c.Taken;
                var b = UiKit.Button("Choice" + i, list, "", 24, c.Disabled ? ChoiceOffBg : ChoiceBg, taken ? ChoiceTaken : locked ? ChoiceLocked : UiKit.Ink);
                var t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft;
                UiKit.Fill(t.rectTransform, 20, 8);
                int small = Mathf.Max(12, Mathf.RoundToInt(t.fontSize * 0.75f));
                string num = $"<color={(c.Disabled ? "#5f6b71" : "#9aa6ad")}>{i + 1}</color>   ";
                // taken: "✓" and dim; locked: "⊘", dimmer, its reason (if any) under it; open: the original's note (if any) under it
                string note = why == null ? "" : $"\n<size={small}><color={(c.Disabled ? "#8a9499" : "#9aa6ad")}>{why}</color></size>";
                t.text = taken ? num + "✓ " + head
                    : locked ? num + "⊘ " + head + note
                    : num + head + note;
                b.interactable = !c.Disabled;
                // the dim look is ours (no see-through tint); hover warms toward amber like the original's .choice:hover
                var cb = b.colors; cb.disabledColor = Color.white; cb.highlightedColor = ChoiceHover; cb.selectedColor = Color.white;
                cb.pressedColor = ChoiceHover * 0.9f; cb.fadeDuration = 0.08f; b.colors = cb;
                var edge = UiKit.Box("HoverEdge", b.transform, UiKit.Amber).rectTransform; // a thin amber bar on the left while hovered
                edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f);
                edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(4, 0);
                var edgeGo = edge.gameObject; edgeGo.SetActive(false);
                var id = c.Id;
                b.onClick.AddListener(() => { if (tcs.TrySetResult(id)) UiSound.Play("confirm"); });
                var trig = b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
                var on = !c.Disabled;
                enter.callback.AddListener(_ => { if (!on) return; UiSound.Play("tick"); edgeGo.SetActive(true); });
                trig.triggers.Add(enter);
                var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { if (edgeGo) edgeGo.SetActive(false); });
                trig.triggers.Add(exit);
                widest = Mathf.Max(widest, t.preferredWidth + 40);
                texts.Add(t); buttons.Add(b);
                UiAppear.On(b.gameObject, 0.05f + i * 0.06f, slide: false, duration: 0.2f); // the choices come in one after another
            }
            float colW = Mathf.Clamp(Mathf.Ceil(widest) + 8, ChoiceMinW, ChoiceMaxW);
            list.sizeDelta = new Vector2(colW, 10);
            UiAppear.On(list.gameObject, 0f, slide: true, duration: 0.28f).from = new Vector2(0, -18);
            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                var gen = t.cachedTextGeneratorForLayout;
                float h = gen.GetPreferredHeight(t.text, t.GetGenerationSettings(new Vector2(colW - 40, 0))) / t.pixelsPerUnit;
                UiKit.Height(buttons[i], Mathf.Max(60, Mathf.Ceil(h) + 20));
            }
            try
            {
                await Task.Yield();
                while (!tcs.Task.IsCompleted)
                {
                    var kb = Keyboard.current;
                    if (kb != null)
                        for (int i = 0; i < choices.Count && i < 9; i++)
                            if (!choices[i].Disabled && (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) && tcs.TrySetResult(choices[i].Id))
                                UiSound.Play("confirm");
                    await Task.Yield();
                }
                return await tcs.Task;
            }
            finally { Destroy(list.gameObject); }
        }

        /// <summary>
        /// A choice's first line and its second (null: none). The second is TalkChoice.Why (an open choice's note, as the
        /// original's choice-note, or a locked one's reason), or for a disabled choice without one, a trailing "(…)" of
        /// its label ("라디오를 켠다 (라디오가 없다)"), kept verbatim. An open choice's label stays whole.
        /// </summary>
        public static (string head, string why) ChoiceParts(TalkChoice c)
        {
            var label = c?.Label ?? "";
            if (c == null) return (label, null);
            if (!string.IsNullOrEmpty(c.Why)) return (label, c.Why);
            if (!c.Disabled || c.Taken) return (label, null);
            int open = label.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && label.EndsWith(")", StringComparison.Ordinal)) return (label.Substring(0, open), label.Substring(open + 1));
            return (label, null);
        }

        /// <summary>
        /// A story / notice card: kind chip, title, meta, body paragraphs, action buttons. Returns the action id.
        /// A "decision" card (a two-way choice) gets a larger title, 24 px before the actions and equal-width primary
        /// buttons; an action with a Hint shows it as a second line under its label (a two-line chip).
        /// </summary>
        public async Task<string> Card(CardOptions o)
        {
            var actions = o.Actions != null && o.Actions.Count > 0 ? o.Actions : new List<CardAction> { new("ok", "확인", true) };
            bool decision = o.Kind == "decision";
            bool hints = actions.Exists(a => !string.IsNullOrEmpty(a.Hint));
            using var modal = new ModalScope(this, true, counts: true);
            using var moment = decision ? DecisionMoment.Begin() : null;
            var shade = UiKit.Box("CardShade", _root, UiKit.ModalShade, raycast: true).rectTransform; UiKit.Fill(shade);
            var box = UiKit.Box("Card", shade, new Color(0.07f, 0.1f, 0.11f, 0.98f)).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(900, 10);
            var col = box.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(40, 40, 32, 32); col.spacing = 14; col.childControlHeight = true; col.childControlWidth = true; col.childForceExpandHeight = false;
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiAppear.On(box.gameObject, 0f, slide: true, duration: decision ? 0.3f : 0.22f); // every card fades and slides up
            if (!string.IsNullOrEmpty(o.Kind)) UiKit.Label("Kind", box, KindLabel(o.Kind), 18, decision ? UiKit.Teal : UiKit.Amber, style: FontStyle.Bold);
            UiKit.Label("Title", box, o.Title ?? "", decision ? 42 : 34, UiKit.Ink, style: FontStyle.Bold);
            if (!string.IsNullOrEmpty(o.Meta)) UiKit.Label("Meta", box, o.Meta, 20, UiKit.Dim);
            foreach (var p in o.Body ?? new List<string>()) if (!string.IsNullOrEmpty(p)) UiKit.Label("Body", box, p, 23, UiKit.Ink);
            if (decision) UiKit.Height(UiKit.Rect("Gap", box), 10); // 14 + 10 = 24 px before the choice
            // Action buttons in rows that wrap inside the card (six experience modes do not fit on one line).
            // One line right-aligned when they fit; otherwise an even grid (six modes: 3 + 3 of equal width, centred) so a
            // short last row does not hang off one side. A decision is always one row of equal halves.
            const float rowWidth = 820, gap = 12;
            float Natural(CardAction a) => Mathf.Min(rowWidth, Mathf.Max(150, 40 + Mathf.Max(a.Label.Length, (a.Hint?.Length ?? 0) * 0.75f) * 26));
            float total = -gap; foreach (var a in actions) total += gap + Natural(a);
            bool grid = decision || total > rowWidth;
            int perRow = actions.Count;
            if (grid && !decision) { int rows = Mathf.CeilToInt(total / rowWidth); perRow = Mathf.CeilToInt(actions.Count / (float)rows); }
            float cellW = grid ? (rowWidth - gap * (perRow - 1)) / perRow : 0;
            float btnH = hints ? 84 : decision ? 66 : 58;
            RectTransform row = null; int inRow = 0;
            var tcs = new TaskCompletionSource<string>();
            // the original's armedAfter(): nothing counts for the first 220 ms, so a key still held from the dialogue
            // before cannot pick an action on a card the player has not seen
            float armAt = Time.unscaledTime + CardFocus.ArmSeconds;
            var cardBg = new Color(0.07f, 0.1f, 0.11f, 1);
            var ghost = ScreenDecor.Over(cardBg, Color.white, 0.08f);
            var decisionIdle = ScreenDecor.Over(cardBg, UiKit.Teal, 0.16f);
            var btns = new List<(Image bg, Text label)>();
            int focus = -1; // a decision starts with no button focused: Enter does nothing until one is chosen
            void PaintFocus()
            {
                if (!decision) return;
                for (int i = 0; i < btns.Count; i++)
                {
                    bool on = i == focus;
                    btns[i].bg.color = on ? UiKit.Teal : decisionIdle;
                    btns[i].label.color = on ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Ink;
                }
            }
            foreach (var a in actions)
            {
                float w = grid ? cellW : Natural(a);
                if (row == null || inRow >= perRow)
                {
                    row = UiKit.Rect("Actions", box);
                    var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = gap; h.childAlignment = grid ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight; h.childControlWidth = false; h.childControlHeight = false;
                    UiKit.Height(row, btnH + 6);
                    inRow = 0;
                }
                inRow++;
                bool primary = a.Primary && !decision;
                var ink = primary ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Ink;
                string text = string.IsNullOrEmpty(a.Hint) ? (hints ? $"<b>{a.Label}</b>" : a.Label)
                    : $"<b>{a.Label}</b>\n<size=18><color={(primary ? "#123a36" : "#9aa6ad")}>{a.Hint}</color></size>";
                var b = UiKit.Button("Action", row, text, 24, primary ? UiKit.Teal : decision ? decisionIdle : ghost, ink);
                var label = b.GetComponentInChildren<Text>();
                label.lineSpacing = 1.05f;
                ((RectTransform)b.transform).sizeDelta = new Vector2(w, btnH);
                var id = a.Id;
                int index = btns.Count;
                btns.Add((b.GetComponent<Image>(), label));
                b.onClick.AddListener(() => { if (Time.unscaledTime >= armAt) tcs.TrySetResult(id); });
                if (decision)
                {
                    // the pointer focuses too, so the button about to be clicked lights up like the keyboard's
                    var trig = b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
                    entry.callback.AddListener(ev => { focus = index; PaintFocus(); });
                    trig.triggers.Add(entry);
                }
            }
            PaintFocus();
            try
            {
                var primaryAction = actions.Find(a => a.Primary) ?? actions[0];
                while (!tcs.Task.IsCompleted)
                {
                    var kb = Keyboard.current;
                    if (kb != null && Time.unscaledTime >= armAt)
                    {
                        bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                        if (decision)
                        {
                            int dir = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame ? -1
                                : kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ? 1
                                : kb.tabKey.wasPressedThisFrame ? (kb.shiftKey.isPressed ? -1 : 1) : 0;
                            if (dir != 0) { focus = CardFocus.Step(focus, actions.Count, dir, kb.tabKey.wasPressedThisFrame); PaintFocus(); UiSound.Play("tick"); }
                            else if ((enter || kb.spaceKey.wasPressedThisFrame) && focus >= 0) tcs.TrySetResult(actions[focus].Id);
                        }
                        else if (enter) tcs.TrySetResult(primaryAction.Id);
                    }
                    await Task.Yield();
                }
                return await tcs.Task;
            }
            finally { Destroy(shade.gameObject); }
        }

        static string KindLabel(string kind) => kind switch
        {
            // the original ui/dialog.js KIND table; anything else shows as info. "decision" is the port's choice card.
            "alert" => "경보", "news" => "뉴스", "notice" => "안내문", "sms" => "문자", "decision" => "선택", _ => "알아 둘 것",
        };

        /// <summary>
        /// A time skip: from -> to over a black screen, then the lines one at a time. Like the original's timeSkip, each
        /// line continues by itself after a reading-time delay (StoryScreens.TimeSkipMs, shown as a thin progress bar),
        /// or earlier on any key or click once 0.4 s have passed.
        /// </summary>
        public async Task TimeSkip(string from, string to, IReadOnlyList<string> text)
        {
            using var modal = new ModalScope(this, false, counts: true);
            // as dark as the original's .96 veil (uGUI blends in linear space here)
            var shade = UiKit.Box("TimeSkip", _root, new Color(0, 0, 0, ScreenDecor.A(0.96f)), raycast: true).rectTransform; UiKit.Fill(shade);
            try
            {
                // one group centred on the screen, as the original's .skip-inner: when, the line, the 260 x 3 progress
                // track right under it and the ghost 계속 control; re-laid out per line so the bar follows the text
                RectTransform Top(Graphic gr, float w, float h)
                {
                    var rt = gr.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 1); rt.sizeDelta = new Vector2(w, h); return rt;
                }
                var when = UiKit.Label("When", shade, $"<color=#9aa6ad>{from}</color>   →   <b>{to}</b>", 32, UiKit.Ink, TextAnchor.MiddleCenter);
                var whenRt = Top(when, 1400, 48);
                var body = UiKit.Label("Text", shade, "", 27, UiKit.Ink, TextAnchor.UpperCenter);
                var bodyRt = Top(body, 1200, 40);
                // the original's .skip-progress: 260 x 3, a faint track the accent fills over the delay
                var track = UiKit.Box("Progress", shade, ScreenDecor.Over(Color.black, Color.white, 0.14f));
                var trackRt = Top(track, 260, 3);
                var fill = UiKit.Box("Fill", track.transform, UiKit.Teal).rectTransform;
                fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
                // dialog.js: btn('계속', { iconName: 'arrow', ghost: true }); any click on the screen continues, this included
                var cont = UiKit.Button("Continue", shade, "계속  →", 20, ScreenDecor.Over(Color.black, Color.white, 0.08f), UiKit.Dim);
                var contRt = (RectTransform)cont.transform;
                contRt.anchorMin = contRt.anchorMax = new Vector2(0.5f, 0.5f); contRt.pivot = new Vector2(0.5f, 1); contRt.sizeDelta = new Vector2(132, 42);
                var contImg = cont.GetComponent<Image>(); contImg.sprite = ScreenDecor.Pill; contImg.type = Image.Type.Sliced;
                void Layout()
                {
                    float bodyH = string.IsNullOrEmpty(body.text) ? 0 : Mathf.Ceil(body.preferredHeight);
                    var ys = StoryScreens.SkipLayout(48, bodyH, 3, 42);
                    whenRt.anchoredPosition = new Vector2(0, ys[0]);
                    bodyRt.anchoredPosition = new Vector2(0, ys[1]); bodyRt.sizeDelta = new Vector2(1200, Mathf.Max(1, bodyH));
                    trackRt.anchoredPosition = new Vector2(0, ys[2]);
                    contRt.anchoredPosition = new Vector2(0, ys[3]);
                }
                if (text == null || text.Count == 0) { Layout(); await SkipLine("", fill); return; }
                foreach (var t in text) { body.text = t; Layout(); await SkipLine(t, fill); }
            }
            finally { Destroy(shade.gameObject); }
        }

        /// <summary>One time-skip line: fills the bar over the line's reading time, ending early on any key or click.</summary>
        static async Task SkipLine(string line, RectTransform fill)
        {
            float secs = StoryScreens.TimeSkipMs(line) / 1000f, t0 = Time.unscaledTime;
            while (true)
            {
                float k = (Time.unscaledTime - t0) / secs;
                if (fill) fill.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
                if (k >= 1) return;
                if (AdvanceRequested) { AdvanceRequested = false; return; }
                if (Time.unscaledTime - t0 >= 0.4f && AnyPress()) return;
                await Task.Yield();
            }
        }

        /// <summary>Any key but Tab and the modifiers (the original's timeSkip onKey), a click, or the pad's south button.</summary>
        static bool AnyPress()
        {
            var kb = Keyboard.current; var m = Mouse.current; var g = Gamepad.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.tabKey.wasPressedThisFrame && !kb.shiftKey.wasPressedThisFrame
                && !kb.ctrlKey.wasPressedThisFrame && !kb.altKey.wasPressedThisFrame && !kb.leftMetaKey.wasPressedThisFrame && !kb.rightMetaKey.wasPressedThisFrame) return true;
            return (m != null && m.leftButton.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
        }

        /// <summary>Set to advance the current line as if a key was pressed (tests and automation drive the dialog this way).</summary>
        public static bool AdvanceRequested;

        /// <summary>Waits for a fresh Enter / Space / E / click (the press that opened the screen does not count).</summary>
        static async Task Advance()
        {
            await Wait(0.22f);
            while (true)
            {
                if (AdvanceRequested) { AdvanceRequested = false; return; }
                var kb = Keyboard.current; var m = Mouse.current; var g = Gamepad.current;
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                    || (m != null && m.leftButton.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame)) return;
                await Task.Yield();
            }
        }

        /// <summary>While a dialog or card is open the player is held and the cursor is free for clicking.</summary>
        sealed class ModalScope : IDisposable
        {
            readonly bool _cursor, _counts;
            readonly StoryOverlay _owner;
            bool _done;
            /// <summary>counts: a card or time skip, which the HUD fades under like a UiScreen (UiScreen.PushModal).</summary>
            public ModalScope(StoryOverlay owner, bool freeCursor, bool counts = false)
            {
                _owner = owner;
                _owner._scopes.Add(this);
                _cursor = freeCursor;
                _counts = counts;
                InputGate.Hold();
                if (_counts) UiScreen.PushModal();
                if (_cursor) PlayerInputReader.HoldPointer(); // choices and cards are clicked
            }
            /// <summary>Once only: the await's using, or the overlay's OnDestroy when that await never resumes.</summary>
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _owner._scopes.Remove(this);
                InputGate.Release();
                if (_counts) UiScreen.PopModal();
                if (_cursor) PlayerInputReader.ReleasePointer();
            }
        }
    }
}
