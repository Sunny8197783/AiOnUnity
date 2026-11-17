// The original's window.prompt (story/echo.js: the chalk text "바닥에 적을 말 (24자까지)") as a small modal:
// prompt({ text, max }) -> the typed string, or null when cancelled. The field takes focus at once (Korean IME works through
// uGUI InputField), Enter submits, Esc cancels, and the field never holds more than max characters.
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    public class PromptScreen : UiScreen
    {
        const float W = 900, Pad = 40;

        string _question;
        int _max;
        InputField _field;
        Text _counter;
        bool _refocus, _done, _collapseOnFocus;

        public static void Register(UnityStoryHost host) => host.Screens["prompt"] = Open;

        public static async Task<JToken> Open(JObject options)
        {
            string text = options?["text"]?.ToString() ?? "";
            int max = MaxOf(options?["max"]);
            var r = await Show<PromptScreen>(s => { s._question = text; s._max = max; }, W, 320);
            return r is string str ? new JValue(str) : null;
        }

        protected override void Build()
        {
            float y = 32, inner = W - Pad * 2;
            var q = UiKit.Label("Question", Window, _question, 28, UiKit.Ink, style: FontStyle.Bold);
            q.supportRichText = false;
            UiKit.At(q.rectTransform, Pad, y, inner, 40);
            float qh = Mathf.Max(40, q.preferredHeight);
            q.rectTransform.sizeDelta = new Vector2(inner, qh);
            y += qh + 18;

            var bg = UiKit.Box("Field", Window, new Color(0, 0, 0, 0.35f), raycast: true);
            UiKit.At(bg.rectTransform, Pad, y, inner, 64);
            var ring = bg.gameObject.AddComponent<Outline>();
            ring.effectColor = UiKit.Teal; ring.effectDistance = new Vector2(2, -2);
            var text = UiKit.Label("Text", bg.transform, "", 26, UiKit.Ink, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(text.rectTransform, 16, 6);
            _field = bg.gameObject.AddComponent<InputField>();
            _field.textComponent = text;
            _field.targetGraphic = bg;
            _field.lineType = InputField.LineType.SingleLine;
            _field.characterLimit = _max; // 0 = unlimited
            _field.caretColor = UiKit.Ink;
            _field.caretWidth = 2;
            _field.selectionColor = new Color(0.24f, 0.78f, 0.71f, 0.35f);
            var nav = _field.navigation; nav.mode = Navigation.Mode.None; _field.navigation = nav;
            _field.onValidateInput = (s, i, c) => c == '\t' || c == '\n' || c == '\r' ? '\0' : c;
            _field.onValueChanged.AddListener(_ => UpdateCounter());
            _field.onEndEdit.AddListener(OnEndEdit);
            y += 64 + 8;

            _counter = UiKit.Label("Counter", Window, "", 18, UiKit.Dim, TextAnchor.MiddleRight);
            UiKit.At(_counter.rectTransform, Pad, y, inner, 26);
            UiKit.At(UiKit.Label("Hint", Window, "Enter 확인 · Esc 취소", 18, UiKit.Dim, TextAnchor.MiddleLeft).rectTransform, Pad, y, inner, 26);
            y += 26 + 18;

            var ok = Action(Window, "확인", true, Submit, W - Pad - 200, y, 200, 58);
            var cancel = Action(Window, "취소", false, Cancel, W - Pad - 200 - 16 - 180, y, 180, 58);
            foreach (var b in new[] { ok, cancel }) { var n = b.navigation; n.mode = Navigation.Mode.None; b.navigation = n; }
            y += 58 + 32;

            Window.sizeDelta = new Vector2(W, y);
            UpdateCounter();
            Focus();
        }

        void Focus()
        {
            if (!_field || _done) return;
            EventSystem.current?.SetSelectedGameObject(_field.gameObject);
            _field.Select();
            _field.ActivateInputField();
        }

        void UpdateCounter()
        {
            if (_counter) _counter.text = Counter(_field ? _field.text : "", _max);
        }

        void OnEndEdit(string _)
        {
            if (_done) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Submit();
            else if (kb != null && kb.escapeKey.wasPressedThisFrame) Cancel();
            else _refocus = true; // focus left the field (a click elsewhere): bring it back next frame
        }

        void Submit()
        {
            if (_done) return;
            _done = true;
            Finish(Clamp(_field ? _field.text : "", _max));
        }

        void Cancel()
        {
            if (_done) return;
            _done = true;
            Finish(null);
        }

        // Esc is handled here (Finish(null)); the base's Esc needs a non-null result.
        protected override object EscapeResult() => null;

        protected override void Update()
        {
            if (_done) return;
            var kb = Keyboard.current;
            bool ready = Time.unscaledTime - OpenedAt >= 0.2f;
            if (kb != null && ready)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Cancel(); return; }
                // Enter while the field is not focused (e.g. right after a click elsewhere) still submits.
                if (!_field.isFocused && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { Submit(); return; }
            }
            if (_collapseOnFocus && _field.isFocused)
            {
                // ActivateInputField selects everything on refocus; put the caret at the end so the next keystroke
                // (or IME syllable) appends instead of replacing the whole answer.
                _collapseOnFocus = false;
                int end = _field.text.Length;
                _field.caretPosition = end;
                _field.selectionAnchorPosition = end;
                _field.selectionFocusPosition = end;
            }
            if (_refocus || !_field.isFocused && EventSystem.current && EventSystem.current.currentSelectedGameObject == null)
            {
                _refocus = false;
                _collapseOnFocus = true;
                Focus();
            }
        }

        // ---------- pure logic (EditMode tests) ----------

        /// <summary>max from the options: a positive whole number, else 0 (no limit).</summary>
        public static int MaxOf(JToken t)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return 0;
            double d = (double)t;
            return d >= 1 ? (int)System.Math.Min(d, 10000) : 0;
        }

        /// <summary>The submitted text, cut to max characters (UTF-16 units, like the original's slice).</summary>
        public static string Clamp(string text, int max)
        {
            text ??= "";
            return max > 0 && text.Length > max ? text.Substring(0, max) : text;
        }

        /// <summary>"n / max", or "" when there is no limit.</summary>
        public static string Counter(string text, int max) => max > 0 ? $"{(text ?? "").Length} / {max}" : "";
    }
}
