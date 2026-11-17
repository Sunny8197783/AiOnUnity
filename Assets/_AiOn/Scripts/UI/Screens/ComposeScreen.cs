// 정민's message to 서연 (original ui/compose.js): chip rows whose labels are the exact fragments of the message, a
// text box the player may rewrite, 칩으로 다시 쓰기 and 보내기. Also the two small pieces the same file gives the
// re-read: the note as 서연 sees it (showNote / hideNote) and the caption steps (rereadCaption).
// State and texts live in ComposeState / ComposeLogic (tested outside Unity); sentences come from Relay.ComposeNote.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Player;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// compose({ route, observedVia, gateSeenAt, sentAt }) -> { chips: { what, time, route, unknowns, advice }, text, edited }.
    /// Keyboard like the original form: Tab / Shift+Tab move between the chip rows, the text box and the buttons
    /// (trapped inside); in a radio row ←→↑↓ move and pick; Space picks / ticks; Enter or Space presses a button.
    /// No Esc: the message must be sent.
    /// </summary>
    public class ComposeScreen : UiScreen
    {
        const float W = 1240, Pad = 48, LegendW = 170;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        ComposeState _s;
        readonly List<List<(Button btn, Text label, Outline ring)>> _pills = new();
        readonly int[] _cursor = new int[5];
        InputField _input;
        Outline _inputRing;
        Button _send, _reset;
        Outline _sendRing, _resetRing;
        bool _muting;

        // Focus: 0..4 chip rows, 5 text box, 6 reset, 7 send.
        const int FText = 5, FReset = 6, FSend = 7;
        int _focus;

        public static async Task<JToken> Open(JObject options)
        {
            var state = ComposeState.FromOptions(options);
            var r = await Show<ComposeScreen>(s => s._s = state, W, 980);
            return r as JToken;
        }

        /// <summary>Registers "compose" (the story calls it directly), and "showNote", "hideNote", "rereadCaption".</summary>
        public static void Register(UnityStoryHost host)
        {
            host.Screens["compose"] = Open;
            host.Screens["showNote"] = RelayNotePanel.Open;
            host.Screens["hideNote"] = RelayNotePanel.OpenHide;
            // Each step first applies its note change and 3D ops (RereadScreen / RereadFx), then shows the caption.
            host.Screens["rereadCaption"] = o => RereadScreen.Caption(host, o);
        }

        static void NoNav(Button b) { var n = b.navigation; n.mode = Navigation.Mode.None; b.navigation = n; }

        protected override void Build()
        {
            float y = 40, inner = W - Pad * 2;
            var eyebrow = UiKit.Label("Eyebrow", Window, $"<b>{_s.Eyebrow}</b>", 20, UiKit.Teal);
            UiKit.At(eyebrow.rectTransform, Pad, y, inner, 28); y += 32;
            UiKit.At(UiKit.Label("Title", Window, "무엇을 남길까요?", 38, UiKit.Ink, style: FontStyle.Bold).rectTransform, Pad, y, inner, 54); y += 62;
            var lead = UiKit.Label("Lead", Window, "정민은 대피소에 들어왔습니다. 서연에게 보낼 문자를 고르거나 직접 고쳐 쓰세요.", 22, UiKit.Ink);
            UiKit.At(lead.rectTransform, Pad, y, inner, 32);
            y += Mathf.Max(32, lead.preferredHeight) + 14;
            if (_s.Quote != null)
            {
                var q = UiKit.Box("Quote", Window, new Color(1, 1, 1, 0.05f)).rectTransform;
                UiKit.At(q, Pad, y, inner, 50);
                UiKit.At(UiKit.Box("Bar", q, UiKit.Amber).rectTransform, 0, 0, 4, 50);
                UiKit.Fill(UiKit.Label("Text", q, _s.Quote, 21, Hex("#d9e0e2"), TextAnchor.MiddleLeft).rectTransform, 18, 0);
                y += 64;
            }

            // Chip rows: legend on the left, pills flowing to the right.
            for (int r = 0; r < _s.Rows.Count; r++)
            {
                var row = _s.Rows[r];
                UiKit.At(UiKit.Label("Legend", Window, $"<b>{row.Legend}</b>", 20, UiKit.Dim).rectTransform, Pad, y + 12, LegendW, 30);
                float x = Pad + LegendW, rowY = y, maxX = W - Pad;
                var pills = new List<(Button, Text, Outline)>();
                for (int o = 0; o < row.Options.Count; o++)
                {
                    var opt = row.Options[o];
                    var b = UiKit.Button($"Pill_{row.Name}_{o}", Window, opt.Label, 20, UiKit.Row, UiKit.Ink);
                    NoNav(b);
                    var label = b.GetComponentInChildren<Text>();
                    label.supportRichText = false;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    float pw = Mathf.Min(label.preferredWidth + 34 + 24, maxX - (Pad + LegendW));
                    if (x + pw > maxX && x > Pad + LegendW) { x = Pad + LegendW; rowY += 58; }
                    UiKit.At((RectTransform)b.transform, x, rowY, pw, 48);
                    x += pw + 10;
                    int ri = r, oi = o;
                    b.onClick.AddListener(() => { ClickPill(ri, oi); EventSystem.current?.SetSelectedGameObject(null); });
                    var ring = b.gameObject.AddComponent<Outline>();
                    ring.effectDistance = new Vector2(3, -3);
                    pills.Add((b, label, ring));
                }
                _pills.Add(pills);
                y = rowY + 58 + 8;
            }

            y += 6;
            UiKit.At(UiKit.Label("NoteLabel", Window, $"<b>보낼 문장</b> <size=18><color=#9aa6ad>{_s.Help}</color></size>", 22, UiKit.Ink).rectTransform, Pad, y, inner, 32);
            y += 40;
            _input = TextBox(Window, Pad, y, inner, 132);
            y += 132 + 22;

            _send = UiKit.Button("Send", Window, _s.SendLabel, 24, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            UiKit.At((RectTransform)_send.transform, W - Pad - 260, y, 260, 60);
            NoNav(_send);
            _send.onClick.AddListener(Send);
            _sendRing = _send.gameObject.AddComponent<Outline>(); _sendRing.effectDistance = new Vector2(3, -3);
            _reset = UiKit.Button("Reset", Window, "칩으로 다시 쓰기", 22, new Color(1, 1, 1, 0.08f), UiKit.Ink);
            UiKit.At((RectTransform)_reset.transform, W - Pad - 260 - 16 - 240, y, 240, 60);
            NoNav(_reset);
            _reset.onClick.AddListener(() => { _s.Reset(); Render(); EventSystem.current?.SetSelectedGameObject(null); });
            _resetRing = _reset.gameObject.AddComponent<Outline>(); _resetRing.effectDistance = new Vector2(3, -3);
            y += 60 + 36;

            Window.sizeDelta = new Vector2(W, y);
            _focus = 0;
            _cursor[0] = 0;
            Render();
        }

        InputField TextBox(RectTransform parent, float x, float y, float w, float h)
        {
            var bg = UiKit.Box("NoteText", parent, new Color(0, 0, 0, 0.35f), raycast: true);
            UiKit.At(bg.rectTransform, x, y, w, h);
            var text = UiKit.Label("Text", bg.transform, "", 22, UiKit.Ink);
            text.supportRichText = false;
            UiKit.Fill(text.rectTransform, 16, 12);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.targetGraphic = bg;
            field.lineType = InputField.LineType.MultiLineNewline;
            field.characterLimit = AiOn.Game.Relay.MaxNote;
            field.caretColor = UiKit.Ink;
            field.selectionColor = new Color(0.24f, 0.78f, 0.71f, 0.35f);
            var nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
            // Tab leaves the box (as in the browser), it never types a tab.
            field.onValidateInput = (s, i, c) => c == '\t' ? '\0' : c;
            field.onValueChanged.AddListener(v => { if (_muting) return; _s.Type(v); Render(); });
            _inputRing = bg.gameObject.AddComponent<Outline>(); _inputRing.effectDistance = new Vector2(3, -3);
            return field;
        }

        void ClickPill(int row, int opt)
        {
            _cursor[row] = opt;
            SetFocus(row);
            var r = _s.Rows[row];
            if (r.Checkbox) _s.Toggle(r.Name); else _s.Pick(r.Name, r.Options[opt].Value);
            Render();
        }

        void Send()
        {
            if (!_s.CanSend) return;
            Finish(_s.Result());
        }

        // ---------- render ----------

        void Render()
        {
            for (int r = 0; r < _pills.Count; r++)
            {
                var row = _s.Rows[r];
                for (int o = 0; o < _pills[r].Count; o++)
                {
                    var (btn, label, ring) = _pills[r][o];
                    bool on = _s.IsChecked(row.Name, row.Options[o].Value);
                    btn.GetComponent<Image>().color = on ? new Color(0.24f, 0.78f, 0.71f, 0.22f) : UiKit.Row;
                    label.color = on ? UiKit.Teal : UiKit.Ink;
                    label.text = (row.Checkbox ? (on ? "✓ " : "□ ") : (on ? "● " : "○ ")) + row.Options[o].Label;
                    ring.effectColor = _focus == r && _cursor[r] == o ? UiKit.Teal : new Color(0, 0, 0, 0);
                }
            }
            if (!_s.Edited && _input.text != _s.Text) { _muting = true; _input.text = _s.Text; _muting = false; }
            _reset.gameObject.SetActive(_s.ShowReset);
            _send.interactable = _s.CanSend;
            _send.GetComponent<Image>().color = _s.CanSend ? UiKit.Teal : new Color(1, 1, 1, 0.1f);
            _send.GetComponentInChildren<Text>().color = _s.CanSend ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Dim;
            // A focused control that went away hands focus to the text box (the original pulls focus back in).
            if ((_focus == FReset && !_s.ShowReset) || (_focus == FSend && !_s.CanSend)) SetFocus(FText, false);
            _inputRing.effectColor = _focus == FText ? UiKit.Teal : new Color(0, 0, 0, 0);
            _resetRing.effectColor = _focus == FReset ? UiKit.Teal : new Color(0, 0, 0, 0);
            _sendRing.effectColor = _focus == FSend ? UiKit.Teal : new Color(0, 0, 0, 0);
        }

        bool Focusable(int f) => f < FText || f == FText || (f == FReset && _s.ShowReset) || (f == FSend && _s.CanSend);

        void SetFocus(int f, bool render = true)
        {
            if (f < FText && _focus != f)
            {
                // Entering a radio row lands on its checked radio, else its first.
                var row = _s.Rows[f];
                var v = _s.CheckedValue(row.Name);
                int idx = v == null ? 0 : row.Options.FindIndex(o => o.Value == v);
                if (!row.Checkbox) _cursor[f] = Math.Max(0, idx);
            }
            bool wasText = _focus == FText;
            _focus = f;
            if (f == FText && !_input.isFocused) { _input.Select(); _input.ActivateInputField(); }
            else if (f != FText && wasText && _input.isFocused)
            {
                _input.DeactivateInputField();
                EventSystem.current?.SetSelectedGameObject(null);
            }
            if (render) Render();
        }

        void MoveFocus(int dir)
        {
            int f = _focus;
            for (int i = 0; i < 8; i++)
            {
                f = (f + dir + 8) % 8;
                if (Focusable(f)) { SetFocus(f); return; }
            }
        }

        protected override void Update()
        {
            base.Update();
            if (_s == null) return;
            if (_input.isFocused && _focus != FText) { _focus = FText; Render(); }
            var kb = Keyboard.current;
            if (kb == null || Time.unscaledTime - OpenedAt < 0.2f) return;
            bool shift = kb.shiftKey.isPressed;
            if (kb.tabKey.wasPressedThisFrame) { MoveFocus(shift ? -1 : 1); return; }
            if (_focus == FText) return; // the text box has the keys

            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame, space = kb.spaceKey.wasPressedThisFrame;
            if (_focus == FSend) { if (enter || space) Send(); return; }
            if (_focus == FReset) { if (enter || space) { _s.Reset(); SetFocus(FText); } return; }

            var row = _s.Rows[_focus];
            if (row.Checkbox) { if (space) { _s.Toggle(row.Name); Render(); } return; }
            int n = row.Options.Count, step = 0;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) step = 1;
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) step = -1;
            if (step != 0)
            {
                _cursor[_focus] = (_cursor[_focus] + step + n) % n;
                _s.Pick(row.Name, row.Options[_cursor[_focus]].Value);
                Render();
            }
            else if (space)
            {
                _s.Pick(row.Name, row.Options[_cursor[_focus]].Value);
                Render();
            }
        }
    }

    /// <summary>
    /// The message as 서연 sees it (original showNote(hand, mode) / hideNote()): a small phone-style panel at the right
    /// that stays up without stopping play. Options { hand, mode: 'phone' | 'marked' }; 'marked' highlights each claim.
    /// Resolves null at once.
    /// </summary>
    public class RelayNotePanel : MonoBehaviour
    {
        static RelayNotePanel _current;

        public static Task<JToken> Open(JObject options)
        {
            Show(options?["hand"] as JObject, options?["mode"]?.ToString());
            return Task.FromResult<JToken>(null);
        }

        public static Task<JToken> OpenHide(JObject options) { Hide(); return Task.FromResult<JToken>(null); }

        public static void Show(JObject hand, string mode)
        {
            Hide();
            if (hand == null) return;
            var canvas = UiKit.ScreenCanvas("RelayNote", 215);
            _current = canvas.gameObject.AddComponent<RelayNotePanel>();
            var root = (RectTransform)canvas.transform;
            var panel = UiKit.Box("Note", root, new Color(0.05f, 0.08f, 0.09f, 0.94f)).rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0.5f);
            var head = UiKit.Label("Head", panel, ComposeLogic.NoteHeader(hand), 18, UiKit.Dim);
            UiKit.At(head.rectTransform, 24, 20, 512, 26);
            string text = hand["text"]?.ToString() ?? "";
            var body = UiKit.Label("Body", panel, "", 26, UiKit.Ink);
            if (mode == "marked")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (part, claim) in ComposeLogic.MarkedSpans(text, ComposeLogic.Quotes(hand)))
                    sb.Append(claim ? $"<color=#f2b33d><b>{Escape(part)}</b></color>" : Escape(part));
                body.text = sb.ToString();
            }
            else { body.supportRichText = false; body.text = text; }
            UiKit.At(body.rectTransform, 24, 58, 512, 40);
            float bh = Mathf.Max(40, body.preferredHeight);
            body.rectTransform.sizeDelta = new Vector2(512, bh);
            panel.sizeDelta = new Vector2(560, 58 + bh + 26);
            panel.anchoredPosition = new Vector2(-48, 0);
        }

        // Rich text has no escape for '<'; a lookalike keeps a typed tag from being read as one.
        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        public static void Hide()
        {
            if (_current) Destroy(_current.gameObject);
            _current = null;
        }
    }

    /// <summary>
    /// One step of the re-read (original rereadCaption(text, pin, last)): a caption at the bottom with optional pin
    /// lines and one button. Options { text, pin: { was, now, also: { now } }, last }. Resolves 'next' (button, E,
    /// Enter, Space) or 'skip' (Esc). The scene stays visible: there is no shade.
    /// </summary>
    public class RereadCaption : MonoBehaviour
    {
        static RereadCaption _current;
        TaskCompletionSource<JToken> _done;
        float _openedAt;
        bool _wasLocked;

        public static Task<JToken> Open(JObject options)
        {
            options ??= new JObject();
            return Show(options["text"]?.ToString() ?? "", options["pin"] as JObject, options["last"]?.Type == JTokenType.Boolean && (bool)options["last"]);
        }

        public static Task<JToken> Show(string text, JObject pin, bool last)
        {
            if (_current) _current.Done(null);
            var canvas = UiKit.ScreenCanvas("RereadCaption", 225);
            var c = canvas.gameObject.AddComponent<RereadCaption>();
            _current = c;
            c._done = new TaskCompletionSource<JToken>();
            InputGate.Hold();
            c._wasLocked = Cursor.lockState == CursorLockMode.Locked;
            PlayerInputReader.SetCursorLocked(false);
            c.Build(text, pin, last);
            c._openedAt = Time.unscaledTime;
            return c._done.Task;
        }

        void Build(string text, JObject pin, bool last)
        {
            var root = (RectTransform)transform;
            var box = UiKit.Box("Caption", root, new Color(0.05f, 0.08f, 0.09f, 0.92f)).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0);
            const float w = 1100;
            float y = 24;
            var cap = UiKit.Label("Text", box, text, 26, UiKit.Ink);
            cap.supportRichText = false;
            UiKit.At(cap.rectTransform, 32, y, w - 64, 40);
            float ch = Mathf.Max(36, cap.preferredHeight);
            cap.rectTransform.sizeDelta = new Vector2(w - 64, ch);
            y += ch + 12;
            foreach (var (line, was) in ComposeLogic.PinLines(pin))
            {
                var l = UiKit.Label(was ? "Was" : "Now", box, line, 22, was ? UiKit.Dim : UiKit.Amber);
                l.supportRichText = false;
                UiKit.At(l.rectTransform, 32, y, w - 64, 30);
                float lh = Mathf.Max(30, l.preferredHeight);
                l.rectTransform.sizeDelta = new Vector2(w - 64, lh);
                if (was)
                {
                    // Crossed out: what the message said, which no longer holds for 서연.
                    l.horizontalOverflow = HorizontalWrapMode.Overflow;
                    var strike = UiKit.Box("Strike", l.transform, UiKit.Dim).rectTransform;
                    UiKit.At(strike, 0, 15, Mathf.Min(w - 64, l.preferredWidth), 2);
                }
                y += lh + 4;
            }
            y += 10;
            var btn = UiKit.Button("Next", box, ComposeLogic.CaptionButton(last), 24, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            UiKit.At((RectTransform)btn.transform, w - 32 - 240, y, 240, 58);
            btn.onClick.AddListener(() => Done("next"));
            UiKit.At(UiKit.Label("Hint", box, ComposeLogic.CaptionHint, 18, UiKit.Dim, TextAnchor.MiddleLeft).rectTransform, 32, y, 600, 58);
            y += 58 + 24;
            box.sizeDelta = new Vector2(w, y);
            box.anchoredPosition = new Vector2(0, 48);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (_done == null || kb == null || Time.unscaledTime - _openedAt < 0.15f) return;
            // wasPressedThisFrame is a fresh press, so a held key (e.repeat) never fires.
            if (kb.escapeKey.wasPressedThisFrame) Done("skip");
            else if (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Done("next");
        }

        void Done(string result)
        {
            if (_done == null) return;
            var tcs = _done;
            _done = null;
            InputGate.Release();
            if (_wasLocked) PlayerInputReader.SetCursorLocked(true);
            if (_current == this) _current = null;
            Destroy(gameObject);
            tcs.TrySetResult(result == null ? null : new JValue(result));
        }

        void OnDestroy()
        {
            if (_done == null) return;
            var tcs = _done;
            _done = null;
            InputGate.Release();
            if (_current == this) _current = null;
            tcs.TrySetResult(new JValue("skip"));
        }
    }
}
