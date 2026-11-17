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
        static readonly Color PanelBg = new(0.06f, 0.09f, 0.1f, 0.86f);
        static readonly Color Red = Hex("#e5484d");

        Canvas _canvas;
        RectTransform _root;
        Text _chapterNo, _chapterTitle, _clock, _objTitle, _objBody, _objSteps, _battery, _bag, _captionSpeaker, _captionText;
        GameObject _objective, _batteryChip, _bagChip, _caption, _stats, _letterTop, _letterBottom;
        Image _bagFill, _vignette, _hurt, _fade;
        GameObject _smokeHint, _topLeft;
        readonly Dictionary<GameObject, bool> _cineHidden = new();
        readonly Dictionary<string, (Image fill, Text word)> _statBars = new();
        RectTransform _toasts;
        float _hurtLevel, _tension;
        int _captionToken;
        bool _hidden;

        void Awake()
        {
            Instance = this;
            _canvas = UiKit.ScreenCanvas("StoryOverlay", 150);
            _canvas.transform.SetParent(transform, false);
            _root = (RectTransform)_canvas.transform;
            Build();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------- layout ----------

        void Build()
        {
            // Effects under everything else.
            _vignette = UiKit.Box("Vignette", _root, new Color(0.35f, 0, 0, 0));
            UiKit.Fill(_vignette.rectTransform);
            _vignette.sprite = RadialSprite();
            _hurt = UiKit.Box("Hurt", _root, new Color(0.8f, 0.05f, 0.05f, 0));
            UiKit.Fill(_hurt.rectTransform);
            _hurt.sprite = _vignette.sprite;

            _letterTop = UiKit.Box("LetterTop", _root, Color.black).gameObject;
            Anchor((RectTransform)_letterTop.transform, new Vector2(0, 1), new Vector2(1, 1), 0, 110);
            _letterBottom = UiKit.Box("LetterBottom", _root, Color.black).gameObject;
            Anchor((RectTransform)_letterBottom.transform, new Vector2(0, 0), new Vector2(1, 0), 0, 110);
            _letterTop.SetActive(false); _letterBottom.SetActive(false);

            // Top-left: chapter, clock, objective.
            var tl = UiKit.Rect("TopLeft", _root); UiKit.At(tl, 28, 24, 560, 520);
            _topLeft = tl.gameObject;
            _chapterNo = UiKit.Label("ChapterNo", tl, "", 20, UiKit.Amber, style: FontStyle.Bold);
            UiKit.At(_chapterNo.rectTransform, 0, 0, 560, 28);
            _chapterTitle = UiKit.Label("ChapterTitle", tl, "", 26, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_chapterTitle.rectTransform, 0, 28, 560, 36);
            foreach (var t in new[] { _chapterNo, _chapterTitle }) // readable over a bright wall or sky
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            }
            var clockBg = UiKit.Box("Clock", tl, PanelBg).rectTransform; UiKit.At(clockBg, 0, 72, 230, 40);
            _clock = UiKit.Label("Text", clockBg, "", 22, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.Fill(_clock.rectTransform);
            var obj = UiKit.Box("Objective", tl, PanelBg).rectTransform; UiKit.At(obj, 0, 124, 460, 230);
            _objective = obj.gameObject;
            UiKit.At(UiKit.Label("Eyebrow", obj, "목표", 18, UiKit.Teal, style: FontStyle.Bold).rectTransform, 18, 12, 200, 24);
            _objTitle = UiKit.Label("Title", obj, "", 24, UiKit.Ink, style: FontStyle.Bold); UiKit.At(_objTitle.rectTransform, 18, 38, 424, 34);
            _objBody = UiKit.Label("Body", obj, "", 19, UiKit.Dim); UiKit.At(_objBody.rectTransform, 18, 74, 424, 56);
            _objSteps = UiKit.Label("Steps", obj, "", 19, UiKit.Ink); UiKit.At(_objSteps.rectTransform, 18, 130, 424, 96);
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

            _toasts = UiKit.Rect("Toasts", _root);
            _toasts.anchorMin = _toasts.anchorMax = _toasts.pivot = new Vector2(1, 1);
            _toasts.anchoredPosition = new Vector2(-28, -84); _toasts.sizeDelta = new Vector2(560, 600);
            var v = _toasts.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.childAlignment = TextAnchor.UpperRight; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;

            // Bottom-left: condition bars.
            var st = UiKit.Box("Stats", _root, PanelBg).rectTransform;
            st.anchorMin = st.anchorMax = st.pivot = new Vector2(0, 0); st.anchoredPosition = new Vector2(28, 28); st.sizeDelta = new Vector2(300, 150);
            _stats = st.gameObject;
            for (int i = 0; i < StatRows.Length; i++)
            {
                var (key, label, _) = StatRows[i];
                float y = 14 + i * 33;
                UiKit.At(UiKit.Label(label, st, label, 18, UiKit.Dim).rectTransform, 16, y, 60, 26);
                var track = UiKit.Box(key + "Track", st, new Color(1, 1, 1, 0.1f)).rectTransform; UiKit.At(track, 76, y + 8, 140, 10);
                var fill = UiKit.Box("Fill", track, UiKit.Teal);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(1, 1); fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var word = UiKit.Label(key + "Word", st, "", 17, UiKit.Ink); UiKit.At(word.rectTransform, 226, y, 64, 26);
                _statBars[key] = (fill, word);
            }

            // Caption: bottom centre, above the prompt.
            var cap = UiKit.Box("Caption", _root, new Color(0.04f, 0.06f, 0.07f, 0.82f)).rectTransform;
            cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0.5f, 0); cap.anchoredPosition = new Vector2(0, 300); cap.sizeDelta = new Vector2(1100, 86);
            _caption = cap.gameObject;
            _captionSpeaker = UiKit.Label("Speaker", cap, "", 20, UiKit.Amber, style: FontStyle.Bold); UiKit.At(_captionSpeaker.rectTransform, 24, 10, 1052, 26);
            _captionText = UiKit.Label("Text", cap, "", 24, UiKit.Ink); UiKit.At(_captionText.rectTransform, 24, 38, 1052, 44);
            _caption.SetActive(false);

            // The smoke escape's key hint (original hud.js smokeHint): why to get low, while the smoke grade is up.
            var hint = UiKit.Box("SmokeHint", _root, new Color(0.04f, 0.06f, 0.07f, 0.82f)).rectTransform;
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

        GameObject Chip(string name, float x, float y, float w, out Text text)
        {
            var c = UiKit.Box(name, _root, PanelBg).rectTransform;
            c.anchorMin = c.anchorMax = c.pivot = new Vector2(1, 1); c.anchoredPosition = new Vector2(x, -y); c.sizeDelta = new Vector2(w, 44);
            text = UiKit.Label("Text", c, "", 20, UiKit.Ink, TextAnchor.MiddleCenter);
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
            foreach (var (key, _, worse) in StatRows)
            {
                double v = key switch { "health" => s.Health, "thirst" => s.Thirst, "fatigue" => s.Fatigue, _ => s.Tension };
                double good = worse ? 100 - v : v;
                var (fill, word) = _statBars[key];
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)good / 100f), 1);
                (string w, Color c) = good >= 67 ? ("좋음", UiKit.Teal) : good >= 34 ? ("보통", UiKit.Amber) : ("나쁨", Red);
                fill.color = c; word.text = w; word.color = c;
            }
        }

        public void SetBattery(double percent)
        {
            ShowPart(_batteryChip, !_hidden);
            int p = Mathf.RoundToInt((float)percent);
            _battery.text = $"휴대폰 {p}%";
            _battery.color = p <= 15 ? Red : p <= 30 ? UiKit.Amber : UiKit.Ink;
        }

        public void SetBag(double? weightKg, double limitKg)
        {
            ShowPart(_bagChip, weightKg.HasValue && !_hidden);
            if (!weightKg.HasValue) return;
            _bag.text = $"가방 {BagDrill.FormatKg(weightKg.Value)} / {BagDrill.FormatKg(limitKg)}";
            float k = (float)(weightKg.Value / Math.Max(0.01, limitKg));
            _bagFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
            _bagFill.color = k > 1 ? Red : k > 0.8f ? UiKit.Amber : UiKit.Teal;
        }

        public void SetTension(double amount01) => _tension = Mathf.Clamp01((float)amount01);
        public void SetStamina(double amount01) { }
        public void Prompt(string label) { } // the interact prompt is GameHud's

        public void Toast(string text, string label, int ms)
        {
            if (string.IsNullOrEmpty(text)) return;
            var (tagText, tagColor) = Labels.TryGetValue(label ?? "self", out var l) ? l : Labels["self"];
            var bg = UiKit.Box("Toast", _toasts, new Color(0.05f, 0.07f, 0.08f, 0.92f));
            var fit = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            fit.padding = new RectOffset(16, 16, 10, 12); fit.spacing = 2; fit.childControlHeight = true; fit.childControlWidth = true; fit.childForceExpandHeight = false;
            UiKit.Label("Tag", bg.transform, tagText, 16, tagColor, style: FontStyle.Bold);
            UiKit.Label("Text", bg.transform, text, 20, UiKit.Ink);
            bg.transform.SetAsLastSibling();
            // Destroy is deferred to the end of the frame: detach first, or childCount never drops and this never ends
            // (a fifth toast while four are up froze the game in the smoke escape).
            while (_toasts.childCount > 4)
            {
                var old = _toasts.GetChild(0);
                old.SetParent(null, false);
                Destroy(old.gameObject);
            }
            Destroy(bg.gameObject, Mathf.Max(1.2f, ms / 1000f));
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
            var sb = new System.Text.StringBuilder();
            if (steps != null)
                foreach (var s in steps)
                    sb.Append(s.Done ? "<color=#3cc6b5>✓</color>  <color=#9aa6ad>" + s.Text + "</color>\n" : "○  " + s.Text + "\n");
            _objSteps.text = sb.ToString().TrimEnd();
            // The panel fits its text: no empty box under a one-line goal, no steps spilling out under a long one.
            float bodyH = string.IsNullOrEmpty(_objBody.text) ? 0 : Mathf.Ceil(_objBody.preferredHeight);
            float stepsH = string.IsNullOrEmpty(_objSteps.text) ? 0 : Mathf.Ceil(_objSteps.preferredHeight);
            float y = 74 + bodyH + (bodyH > 0 ? 8 : 0);
            UiKit.At(_objBody.rectTransform, 18, 74, 424, Mathf.Max(1, bodyH));
            UiKit.At(_objSteps.rectTransform, 18, y, 424, Mathf.Max(1, stepsH));
            UiKit.At((RectTransform)_objective.transform, 0, 124, 460, y + stepsH + 16);
        }

        public void SetChapter(string title, string subtitle) { _chapterNo.text = title ?? ""; _chapterTitle.text = subtitle ?? ""; }

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
            _letterTop.SetActive(on); _letterBottom.SetActive(on);
            var parts = new[] { _topLeft, _toasts.gameObject, _stats, _batteryChip, _bagChip };
            if (on)
            {
                foreach (var go in parts)
                {
                    if (!_cineHidden.ContainsKey(go)) _cineHidden[go] = go.activeSelf;
                    go.SetActive(false);
                }
                return;
            }
            foreach (var kv in _cineHidden) if (kv.Key) kv.Key.SetActive(kv.Value);
            _cineHidden.Clear();
            _topLeft.SetActive(true); _toasts.gameObject.SetActive(true);
            _stats.SetActive(!_hidden);
        }

        public void Hurt(double amount01) => _hurtLevel = Mathf.Clamp01(_hurtLevel + (float)amount01);
        public void Show() { _hidden = false; _stats.SetActive(true); }
        public void Hide() { _hidden = true; foreach (var go in new[] { _objective, _stats, _batteryChip, _bagChip }) go.SetActive(false); }
        public void SetMap(Newtonsoft.Json.Linq.JObject map) { }
        public void SetCompass(double? yaw) { }

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
            _hurtLevel = Mathf.MoveTowards(_hurtLevel, 0, Time.unscaledDeltaTime * 0.8f);
            _hurt.color = new Color(0.8f, 0.05f, 0.05f, _hurtLevel * 0.6f);
            _vignette.color = new Color(0.3f, 0.02f, 0.02f, _tension * 0.45f);
            bool smoke = AiOn.Levels.EnvironmentController.SmokeActive && !_hidden;
            if (_smokeHint.activeSelf != smoke) _smokeHint.SetActive(smoke);
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

        public async Task<string> Talk(TalkOptions o)
        {
            try { TalkStarted?.Invoke(o.Speaker, o.Role); } catch (System.Exception e) { Debug.LogException(e); }
            var lines = new List<string>();
            foreach (var l in o.Lines ?? new List<string>()) if (!string.IsNullOrEmpty(l)) lines.Add(l);
            if (lines.Count == 0) lines.Add("…");
            using var modal = new ModalScope(o.Choices != null && o.Choices.Count > 0);
            var box = UiKit.Box("Talk", _root, new Color(0.04f, 0.06f, 0.07f, 0.94f)).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0); box.anchoredPosition = new Vector2(0, 50); box.sizeDelta = new Vector2(1240, 260);
            try
            {
                var speaker = UiKit.Label("Speaker", box, o.Speaker ?? "", 30, UiKit.Teal, style: FontStyle.Bold); UiKit.At(speaker.rectTransform, 34, 20, 800, 40);
                var role = UiKit.Label("Role", box, o.Role ?? "", 21, UiKit.Dim); UiKit.At(role.rectTransform, 34, 60, 800, 30);
                var count = UiKit.Label("Count", box, "", 20, UiKit.Dim, TextAnchor.UpperRight); UiKit.At(count.rectTransform, 1000, 22, 210, 30);
                var line = UiKit.Label("Line", box, "", 28, UiKit.Ink); UiKit.At(line.rectTransform, 34, 100, 1172, 140);
                for (int i = 0; i < lines.Count; i++)
                {
                    line.text = lines[i];
                    count.text = lines.Count > 1 ? $"{i + 1} / {lines.Count}" : "";
                    bool last = i == lines.Count - 1;
                    if (last && o.Choices != null && o.Choices.Count > 0) break;
                    await Advance();
                }
                if (o.Choices == null || o.Choices.Count == 0) return null;
                return await Choose(box, o.Choices);
            }
            finally { Destroy(box.gameObject); }
        }

        async Task<string> Choose(RectTransform box, List<TalkChoice> choices)
        {
            var list = UiKit.Rect("Choices", _root);
            list.anchorMin = list.anchorMax = list.pivot = new Vector2(0.5f, 0); list.anchoredPosition = new Vector2(0, 330); list.sizeDelta = new Vector2(1000, 10);
            UiKit.Column(list, 8);
            var tcs = new TaskCompletionSource<string>();
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var label = $"<color=#9aa6ad>{i + 1}</color>   {c.Label}" + (c.Disabled && !string.IsNullOrEmpty(c.Why) ? $"\n<size=18><color=#9aa6ad>{c.Why}</color></size>" : "");
                var b = UiKit.Button("Choice" + i, list, label, 24, c.Disabled ? new Color(1, 1, 1, 0.05f) : new Color(0.12f, 0.17f, 0.19f, 0.96f), c.Disabled ? UiKit.Dim : UiKit.Ink);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                UiKit.Height(b, c.Disabled && !string.IsNullOrEmpty(c.Why) ? 84 : 60);
                b.interactable = !c.Disabled;
                var id = c.Id;
                b.onClick.AddListener(() => tcs.TrySetResult(id));
            }
            try
            {
                await Task.Yield();
                while (!tcs.Task.IsCompleted)
                {
                    var kb = Keyboard.current;
                    if (kb != null)
                        for (int i = 0; i < choices.Count && i < 9; i++)
                            if (!choices[i].Disabled && (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame)) tcs.TrySetResult(choices[i].Id);
                    await Task.Yield();
                }
                return await tcs.Task;
            }
            finally { Destroy(list.gameObject); }
        }

        /// <summary>A story / notice card: kind chip, title, meta, body paragraphs, action buttons. Returns the action id.</summary>
        public async Task<string> Card(CardOptions o)
        {
            var actions = o.Actions != null && o.Actions.Count > 0 ? o.Actions : new List<CardAction> { new("ok", "확인", true) };
            using var modal = new ModalScope(true);
            var shade = UiKit.Box("CardShade", _root, new Color(0, 0, 0, 0.6f), raycast: true).rectTransform; UiKit.Fill(shade);
            var box = UiKit.Box("Card", shade, new Color(0.07f, 0.1f, 0.11f, 0.98f)).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(900, 10);
            var col = box.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(40, 40, 32, 32); col.spacing = 14; col.childControlHeight = true; col.childControlWidth = true; col.childForceExpandHeight = false;
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (!string.IsNullOrEmpty(o.Kind)) UiKit.Label("Kind", box, KindLabel(o.Kind), 18, UiKit.Amber, style: FontStyle.Bold);
            UiKit.Label("Title", box, o.Title ?? "", 34, UiKit.Ink, style: FontStyle.Bold);
            if (!string.IsNullOrEmpty(o.Meta)) UiKit.Label("Meta", box, o.Meta, 20, UiKit.Dim);
            foreach (var p in o.Body ?? new List<string>()) if (!string.IsNullOrEmpty(p)) UiKit.Label("Body", box, p, 23, UiKit.Ink);
            // Action buttons in rows that wrap inside the card (six experience modes do not fit on one line).
            const float rowWidth = 820, gap = 12;
            RectTransform row = null; float used = 0;
            var tcs = new TaskCompletionSource<string>();
            foreach (var a in actions)
            {
                float w = Mathf.Min(rowWidth, Mathf.Max(150, 40 + a.Label.Length * 26));
                if (row == null || used + gap + w > rowWidth)
                {
                    row = UiKit.Rect("Actions", box);
                    var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = gap; h.childAlignment = TextAnchor.MiddleRight; h.childControlWidth = false; h.childControlHeight = false;
                    UiKit.Height(row, 64);
                    used = -gap;
                }
                used += gap + w;
                var b = UiKit.Button("Action", row, a.Label, 24, a.Primary ? UiKit.Teal : new Color(1, 1, 1, 0.08f), a.Primary ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Ink);
                ((RectTransform)b.transform).sizeDelta = new Vector2(w, 58);
                var id = a.Id;
                b.onClick.AddListener(() => tcs.TrySetResult(id));
            }
            try
            {
                await Task.Yield();
                var primary = actions.Find(a => a.Primary) ?? actions[0];
                while (!tcs.Task.IsCompleted)
                {
                    var kb = Keyboard.current;
                    if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) tcs.TrySetResult(primary.Id);
                    await Task.Yield();
                }
                return await tcs.Task;
            }
            finally { Destroy(shade.gameObject); }
        }

        static string KindLabel(string kind) => kind switch
        {
            // the original ui/dialog.js KIND table; anything else shows as info
            "alert" => "경보", "news" => "뉴스", "notice" => "안내문", "sms" => "문자", _ => "알아 둘 것",
        };

        /// <summary>A time skip: from -> to over a black screen, then the lines one at a time.</summary>
        public async Task TimeSkip(string from, string to, IReadOnlyList<string> text)
        {
            using var modal = new ModalScope(false);
            var shade = UiKit.Box("TimeSkip", _root, new Color(0, 0, 0, 0.96f), raycast: true).rectTransform; UiKit.Fill(shade);
            try
            {
                var when = UiKit.Label("When", shade, $"<color=#9aa6ad>{from}</color>   →   <b>{to}</b>", 32, UiKit.Ink, TextAnchor.MiddleCenter);
                when.rectTransform.anchorMin = when.rectTransform.anchorMax = when.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                when.rectTransform.anchoredPosition = new Vector2(0, 160); when.rectTransform.sizeDelta = new Vector2(1400, 60);
                var body = UiKit.Label("Text", shade, "", 27, UiKit.Ink, TextAnchor.UpperCenter);
                body.rectTransform.anchorMin = body.rectTransform.anchorMax = body.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                body.rectTransform.anchoredPosition = new Vector2(0, -20); body.rectTransform.sizeDelta = new Vector2(1200, 300);
                var hint = UiKit.Label("Hint", shade, "Enter · Space · 클릭", 18, UiKit.Dim, TextAnchor.MiddleCenter);
                hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = hint.rectTransform.pivot = new Vector2(0.5f, 0);
                hint.rectTransform.anchoredPosition = new Vector2(0, 60); hint.rectTransform.sizeDelta = new Vector2(600, 30);
                if (text == null || text.Count == 0) { await Advance(); return; }
                foreach (var t in text) { body.text = t; await Advance(); }
            }
            finally { Destroy(shade.gameObject); }
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
            readonly bool _cursor;
            readonly bool _wasLocked;
            public ModalScope(bool freeCursor)
            {
                _cursor = freeCursor;
                InputGate.Hold();
                _wasLocked = Cursor.lockState == CursorLockMode.Locked;
                if (_cursor) PlayerInputReader.SetCursorLocked(false);
            }
            public void Dispose()
            {
                InputGate.Release();
                if (_cursor && _wasLocked) PlayerInputReader.SetCursorLocked(true);
            }
        }
    }
}
