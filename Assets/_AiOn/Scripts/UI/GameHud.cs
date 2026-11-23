using System;
using System.Collections.Generic;
using AiOn.Interaction;
using AiOn.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The in-game overlay, built in code: a centre dot, the "E  label" prompt for the point in reach, a dialog box
    /// (speaker, role, one line at a time; Enter / Space / E / click advances) and short toasts. While a dialog is
    /// open the player is held (InputGate). Korean text uses the OS font (Malgun Gothic / Apple SD Gothic Neo / Noto).
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        public PlayerInteractor interactor;
        public int referenceHeight = 1080;

        public static GameHud Current { get; private set; }

        public bool DialogOpen => _dialog != null && _dialog.activeSelf;
        /// <summary>The hands' prompt for a loose thing in reach (null when none; the point prompt wins), as actions.js words it
        /// ("G · 빈 병 줍기"); shown styled like the E prompt, formatted once per change.</summary>
        public string ItemPrompt
        {
            get => _itemPrompt;
            set
            {
                if (_itemPrompt == value) return;
                _itemPrompt = value;
                (_itemKey, _itemPromptText) = SplitKey(value);
            }
        }
        string _itemPrompt, _itemPromptText, _itemKey, _promptLabel;

        /// <summary>The loose thing the hands' prompt is about (outlined and bracketed like a point); null when none.</summary>
        public Transform ItemTarget { get; set; }

        /// <summary>"G · 빈 병 줍기" -> ("G", "빈 병 줍기"); no key part -> ("", text); empty -> (null, null).</summary>
        public static (string key, string label) SplitKey(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return (null, null);
            int i = prompt.IndexOf(" · ", StringComparison.Ordinal);
            return i > 0 && i <= 3 ? (prompt.Substring(0, i), prompt.Substring(i + 3)) : ("", prompt);
        }
        AiOn.Interaction.InteractionPoint _promptFor;
        public string PromptText => _prompt && _prompt.gameObject.activeSelf ? _prompt.text : null;
        public string DialogLine => DialogOpen ? _line.text : null;
        public string ToastText => _toast && _toast.gameObject.activeSelf ? _toast.text : null;

        Font _font;
        CanvasGroup _group;
        RectTransform _promptBg;
        /// <summary>A cutscene runs (StoryOverlay.SetCinematic): no dot or prompt on any GameHud, the persistent one or a level's own.</summary>
        public static bool Cinematic { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cinematic = false; }
        string _promptSized;
        Text _prompt, _promptKey, _speaker, _role, _line, _counter, _toast;
        HudCrosshair _cross;
        HudTargetMarker _marker;
        CanvasGroup _promptGroup;
        RectTransform _keyCap;
        float _appear;          // 0..1 since the current target came into reach
        Vector2 _promptPos;
        object _shownFor;
        readonly List<MeshFilter> _itemMeshes = new();
        Transform _itemMeshesFor;
        GameObject _dialog, _dot;
        readonly List<string> _lines = new();
        int _index;
        Action _onDone;
        float _armedAt, _toastUntil;

        static readonly string[] FontNames = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial" };

        void Awake()
        {
            Current = this;
            _font = UiKit.Font;
            Build();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (DialogOpen) InputGate.Release();
        }

        void Start()
        {
            if (!interactor) interactor = FindFirstObjectByType<PlayerInteractor>();
        }

        // ---------- layout ----------

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(referenceHeight * 16f / 9f, referenceHeight);
            scaler.matchWidthOrHeight = 1f;
            _group = gameObject.AddComponent<CanvasGroup>(); // fades out under modal windows
            _group.blocksRaycasts = false; _group.interactable = false;

            // Reticle: a dot that opens into a ring over something usable; brackets close in on the thing itself.
            _marker = Widget<HudTargetMarker>("TargetMarker", 160);
            _marker.gameObject.SetActive(false);
            _cross = Widget<HudCrosshair>("Crosshair", 64);
            _dot = _cross.gameObject;

            // The prompt: a slate pill with a key cap, by the thing it is about (first person) or just clear of the
            // character's head (third person). Sized to its words.
            var promptBg = Panel("Prompt", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, PromptYThird), new Vector2(PromptMinWidth, 48), HudTheme.Slate);
            HudTheme.Round(promptBg.GetComponent<Image>());
            _promptBg = promptBg;
            _promptGroup = promptBg.gameObject.AddComponent<CanvasGroup>();
            _promptGroup.blocksRaycasts = false; _promptGroup.interactable = false;
            _prompt = HudTheme.Shadowed(Label("Text", promptBg, 22, TextAnchor.MiddleLeft, HudTheme.Text));
            _prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
            _prompt.rectTransform.anchorMin = Vector2.zero; _prompt.rectTransform.anchorMax = Vector2.one;
            _prompt.rectTransform.offsetMin = new Vector2(PromptTextX, 0); _prompt.rectTransform.offsetMax = new Vector2(-PromptPad, 0);
            _keyCap = Panel("Key", promptBg, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(30, 30), HudTheme.Accent);
            HudTheme.Round(_keyCap.GetComponent<Image>());
            _promptKey = Label("K", _keyCap, 18, TextAnchor.MiddleCenter, new Color(0.03f, 0.07f, 0.1f));
            _promptKey.fontStyle = FontStyle.Bold;
            Stretch(_promptKey.rectTransform, 0, 0);
            promptBg.gameObject.SetActive(false);

            var toastBg = Panel("Toast", transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(1100, 90), new Color(0.05f, 0.08f, 0.09f, 0.82f));
            _toast = Label("Text", toastBg, 26, TextAnchor.MiddleCenter, new Color(0.93f, 0.95f, 0.96f));
            Stretch(_toast.rectTransform, 24, 8);
            toastBg.gameObject.SetActive(false);
            _toast = toastBg.GetComponentInChildren<Text>(true);

            var box = Panel("Dialog", transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(1240, 250), new Color(0.05f, 0.08f, 0.09f, 0.9f));
            _dialog = box.gameObject;
            _speaker = Label("Speaker", box, 32, TextAnchor.UpperLeft, new Color(0.24f, 0.78f, 0.71f));
            Place(_speaker.rectTransform, new Vector2(36, -22), new Vector2(700, 44));
            _speaker.fontStyle = FontStyle.Bold;
            _role = Label("Role", box, 24, TextAnchor.UpperLeft, new Color(0.6f, 0.65f, 0.68f));
            Place(_role.rectTransform, new Vector2(36, -64), new Vector2(700, 34));
            _counter = Label("Counter", box, 22, TextAnchor.UpperRight, new Color(0.6f, 0.65f, 0.68f));
            _counter.rectTransform.anchorMin = _counter.rectTransform.anchorMax = new Vector2(1, 1);
            _counter.rectTransform.pivot = new Vector2(1, 1);
            _counter.rectTransform.anchoredPosition = new Vector2(-30, -24);
            _counter.rectTransform.sizeDelta = new Vector2(200, 32);
            _line = Label("Line", box, 30, TextAnchor.UpperLeft, Color.white);
            _line.rectTransform.anchorMin = new Vector2(0, 0);
            _line.rectTransform.anchorMax = new Vector2(1, 1);
            _line.rectTransform.offsetMin = new Vector2(36, 50);
            _line.rectTransform.offsetMax = new Vector2(-36, -108);
            var hint = Label("Hint", box, 20, TextAnchor.LowerRight, new Color(0.6f, 0.65f, 0.68f));
            hint.text = "E · Enter · Space";
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = hint.rectTransform.pivot = new Vector2(1, 0);
            hint.rectTransform.anchoredPosition = new Vector2(-30, 18);
            hint.rectTransform.sizeDelta = new Vector2(400, 30);
            _dialog.SetActive(false);
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        Text Label(string name, Transform parent, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        T Widget<T>(string name, float size) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var g = go.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        static void Stretch(RectTransform rt, float padX, float padY)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
        }

        static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        const float PromptMinWidth = 140, PromptMaxWidth = 900, PromptPad = 18, PromptTextX = 52, PromptYThird = -44, PromptYFirst = -305;

        /// <summary>Cutscene on / off: the dot and the prompt stay off while it runs (the same static flag for every HUD).</summary>
        public void SetCinematic(bool on) => Cinematic = on;

        void SizePrompt()
        {
            if (ReferenceEquals(_promptSized, _prompt.text)) return;
            _promptSized = _prompt.text;
            bool key = !string.IsNullOrEmpty(_promptKey.text);
            if (_keyCap.gameObject.activeSelf != key) _keyCap.gameObject.SetActive(key);
            float x = key ? PromptTextX : PromptPad;
            _prompt.rectTransform.offsetMin = new Vector2(x, 0);
            float w = Mathf.Clamp(Mathf.Ceil(_prompt.preferredWidth) + x + PromptPad + 2, PromptMinWidth, PromptMaxWidth);
            _promptBg.sizeDelta = new Vector2(w, _promptBg.sizeDelta.y);
        }

        // ---------- behaviour ----------

        /// <summary>Shows a conversation or description; onDone runs after the last line.</summary>
        public void Talk(string speaker, string role, IEnumerable<string> lines, Action onDone = null)
        {
            _lines.Clear();
            foreach (var l in lines) if (!string.IsNullOrEmpty(l)) _lines.Add(l);
            if (_lines.Count == 0) _lines.Add("…");
            if (!DialogOpen) InputGate.Hold();
            _speaker.text = speaker ?? "";
            _role.text = role ?? "";
            _onDone = onDone;
            _index = 0;
            _dialog.SetActive(true);
            ShowLine();
        }

        /// <summary>Next line, or closes the dialog after the last one.</summary>
        public void Advance()
        {
            if (!DialogOpen) return;
            if (++_index < _lines.Count) { ShowLine(); return; }
            _dialog.SetActive(false);
            InputGate.Release();
            var done = _onDone; _onDone = null;
            done?.Invoke();
        }

        void ShowLine()
        {
            _line.text = _lines[_index];
            _counter.text = _lines.Count > 1 ? $"{_index + 1} / {_lines.Count}" : "";
            _armedAt = Time.unscaledTime + 0.22f; // the key that opened it does not skip the first line
        }

        public void Toast(string text, float seconds = 3.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toast.text = text;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (!Current) Current = this; // a level scene's own HUD took Current on load and dropped it when it was removed
            if (DialogOpen && Time.unscaledTime >= _armedAt && AdvancePressed()) Advance();

            // The HUD goes under a modal window (the window's own shade is enough); a dialog of this HUD keeps it up.
            float target = DialogOpen ? 1f : HudVisibility.LayerAlpha(UiScreen.OpenCount);
            if (_group.alpha != target) _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / 0.15f);

            // The dot and the prompt only in free play: not in a cutscene, under a window or over a dialog.
            bool pointer = HudVisibility.Pointer(Cinematic, UiScreen.OpenCount, DialogOpen || StoryOverlay.TalkOpen);
            var p = interactor ? interactor.Current : null;
            bool showPrompt = pointer && p != null;
            // app.js: a loose thing in reach ("G · 벽돌 줍기") shows when no point is in reach
            bool showItem = pointer && !showPrompt && !string.IsNullOrEmpty(ItemPrompt) && !AiOn.Player.InputGate.Blocked;
            var plate = _promptBg.gameObject;
            if (plate.activeSelf != (showPrompt || showItem)) plate.SetActive(showPrompt || showItem);
            if (showPrompt)
            {
                if (!ReferenceEquals(p, _promptFor) || p.label != _promptLabel) { _promptFor = p; _promptLabel = p.label; _prompt.text = p.label; _promptKey.text = "E"; }
            }
            else if (showItem && (_prompt.text != _itemPromptText || _promptFor != null)) { _prompt.text = _itemPromptText; _promptKey.text = _itemKey ?? ""; _promptFor = null; }
            if (_dot.activeSelf != pointer) _dot.SetActive(pointer);

            // ---- the reticle, the brackets and the prompt's place and pop ----
            float dt = Time.unscaledDeltaTime;
            var host = AiOn.Story.UnityStoryHost.Instance;
            bool reduce = host && host.ReduceMotion;
            bool first = host && host.View == "first";
            object aim = showPrompt ? p : showItem ? (object)(ItemTarget ? ItemTarget : this) : null;
            if (!ReferenceEquals(aim, _shownFor)) { _shownFor = aim; _appear = 0; }
            _appear = aim == null ? 0 : reduce ? 1 : Mathf.MoveTowards(_appear, 1, dt / 0.16f);
            _cross.Target = aim != null;
            if (showPrompt && interactor && interactor.UsedFrame == Time.frameCount) _cross.Press();
            if (pointer) _cross.Tick(dt, reduce);

            bool marked = false;
            if (aim != null)
            {
                SizePrompt();
                Vector2 want = new Vector2(0, first ? PromptYFirst : PromptYThird);
                var cam = interactor && interactor.cameraRig && interactor.cameraRig.cam ? interactor.cameraRig.cam : Camera.main;
                // A point sits on the floor in front of its fixture: the brackets go at waist height. A thing with an outline
                // of its own (a door) needs no brackets.
                var outlined = showPrompt ? AiOn.Rendering.OutlineHighlight.MeshesOf(p) : null;
                Vector3? world = showPrompt ? (outlined.Length > 0 ? (Vector3?)null : p.Position + Vector3.up * 1.0f) : ItemTarget ? ItemTarget.position : (Vector3?)null;
                if (cam && world.HasValue && ToCanvas(cam, world.Value, out var at, out float dist))
                {
                    float size = HudMotion.ProjectedSize(0.5f, dist, cam.fieldOfView, referenceHeight, 44f, 150f);
                    marked = true;
                    _marker.rectTransform.anchoredPosition = at;
                    _marker.Set(size, _appear);
                    // first person: the prompt hangs under the thing, inside the middle of the view
                    if (first) want = new Vector2(Mathf.Clamp(at.x, -620f, 620f), Mathf.Clamp(at.y - size * 0.5f - 46f, -290f, 330f));
                }
                // a person (third person): the prompt goes down by their feet, off the face and chest the player looks at
                if (!first && showPrompt && p.kind == "npc")
                    want = new Vector2(0, cam && ToCanvas(cam, p.Position, out var feet, out _) ? HudMotion.NpcPromptY(feet.y) : HudMotion.NpcPromptY(null));
                _promptPos = _appear <= 0.02f || reduce ? want : Vector2.Lerp(_promptPos, want, 1f - Mathf.Exp(-14f * dt));
                _promptBg.anchoredPosition = _promptPos;
                _promptGroup.alpha = HudMotion.OutCubic(_appear);
                float sc = reduce ? 1f : Mathf.Lerp(0.92f, 1f, HudMotion.OutBack(_appear));
                _promptBg.localScale = new Vector3(sc, sc, 1);

                // the outline: the point's door or prop, or the loose thing
                float pulse = reduce ? 1f : 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f);
                if (showPrompt) AiOn.Rendering.OutlineHighlight.Draw(outlined, HudTheme.Accent, _appear * pulse);
                else if (ItemTarget)
                {
                    if (_itemMeshesFor != ItemTarget) { _itemMeshesFor = ItemTarget; AiOn.Rendering.OutlineHighlight.MeshesOf(ItemTarget, _itemMeshes); }
                    AiOn.Rendering.OutlineHighlight.Draw(_itemMeshes, HudTheme.Accent, _appear * pulse);
                }
            }
            if (_marker.gameObject.activeSelf != marked) _marker.gameObject.SetActive(marked);

            if (_toast.transform.parent.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>
        /// A world position in this canvas' centre-anchored units. False when it is behind the camera or well off screen.
        /// </summary>
        bool ToCanvas(Camera cam, Vector3 world, out Vector2 at, out float distance)
        {
            var sp = cam.WorldToScreenPoint(world);
            distance = sp.z;
            at = default;
            if (sp.z <= 0.05f) return false;
            float k = referenceHeight / (float)Mathf.Max(1, cam.pixelHeight);
            at = new Vector2((sp.x - cam.pixelWidth * 0.5f) * k, (sp.y - cam.pixelHeight * 0.5f) * k);
            float halfW = cam.pixelWidth * 0.5f * k, halfH = referenceHeight * 0.5f;
            return Mathf.Abs(at.x) < halfW * 0.95f && Mathf.Abs(at.y) < halfH * 0.95f;
        }

        static bool AdvancePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) return true;
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame);
        }
    }
}
