using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The emergency contact card (original ui/docs.js): on the left the ladder help, the fields to tick ('카드에 적을 것')
    /// and where the card is kept ('카드를 둘 곳'); on the right a desk with the paper card (what is written) and the dead
    /// phone (what is lost when the battery is gone). Keys as the original footer: Tab move · Space 적기/지우기 · ↑↓ 보관
    /// 장소 · Esc 닫기 (Enter also presses the focused 카드 다 썼다). Esc and the button both return the card.
    /// State lives in <see cref="DocsLogic"/>.
    /// </summary>
    public class DocsScreen : UiScreen
    {
        static readonly Color Card = new(1, 1, 1, 0.04f), Muted = Hex("#9fb0b5"), Desk = Hex("#5a3f28"), PaperBg = Hex("#fbf8f0");
        static readonly Color PaperLine = Hex("#dfe6ee"), FocusLine = Hex("#f2b33d"), RecLine = new(0.24f, 0.78f, 0.71f, 0.25f);

        const float W = 1720, H = 940, LeftW = 800;

        DocsLogic _d;
        Button _hintBtn;
        RectTransform _helpBody, _fieldContent, _fieldView, _paperRows;
        readonly List<(Image bg, Outline line, Text box)> _fields = new();
        readonly List<(Image bg, Outline line, Text dot, string key)> _wheres = new();
        Outline _doneLine;
        Text _phoneText, _phoneOnly, _status;

        static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }

        /// <summary>docs({ mode, fields: [{ id, label, value, why, recommended }], carry }) -> { card: { id: value }, where }</summary>
        public static async Task<JToken> Open(JObject options)
        {
            var logic = DocsLogic.Parse(options);
            var r = await Show<DocsScreen>(s => s._d = logic, W, H);
            return r as JToken ?? logic.Result();
        }

        /// <summary>Adds the docs screen to the story host.</summary>
        public static void Register(AiOn.Story.UnityStoryHost host)
        {
            host.Screens["docs"] = Open;
        }

        protected override object EscapeResult() => _d?.Result();

        // ---------- layout ----------

        protected override void Build()
        {
            Title("서류·연락처 정리", "비상연락카드");
            var chip = _d.ModeChip;
            if (chip != null)
            {
                var c = _d.Mode == "guided" ? UiKit.Teal : _d.Mode == "hint" ? UiKit.Amber : Muted;
                var box = UiKit.Box("Mode", Window, new Color(c.r, c.g, c.b, 0.14f)).rectTransform;
                UiKit.At(box, W - 40 - 150, 34, 150, 40);
                UiKit.Fill(UiKit.Label("Text", box, chip, 20, c, TextAnchor.MiddleCenter).rectTransform);
            }

            // ----- left: help, fields, where -----
            float y = 130;
            if (_d.HasHintButton)
            {
                _hintBtn = UiKit.Button("Hint", Window, _d.HintButtonText, 20, new Color(1, 1, 1, 0.08f), UiKit.Ink);
                UiKit.At((RectTransform)_hintBtn.transform, 40, y, 160, 42);
                _hintBtn.onClick.AddListener(() => { _d.FocusOn(DocsStop.Hint, 0); _d.ToggleHelp(); Render(); });
                y += 50;
            }
            if (_d.HasHelp)
            {
                _helpBody = UiKit.Box("Help", Window, new Color(0.24f, 0.78f, 0.71f, 0.08f)).rectTransform;
                UiKit.At(_helpBody, 40, y, LeftW, 78);
                var lines = DocsLogic.HelpText + (_d.Mode == "hint" ? "\n<color=#9fb0b5>" + DocsLogic.HelpHintText + "</color>" : "");
                UiKit.Fill(UiKit.Label("Text", _helpBody, lines, 18, UiKit.Ink).rectTransform, 14, 8);
                y += 92;
            }

            var opts = _d.WhereOptions;
            float rowH = _d.ShowWhereDesc ? 64 : 46;
            float whereTop = opts.Count > 0 ? 830 - 38 - opts.Count * (rowH + 6) : 830;

            UiKit.At(UiKit.Label("FieldsHead", Window, "<b>카드에 적을 것</b>", 22, UiKit.Ink).rectTransform, 40, y, LeftW, 32);
            var list = UiKit.Rect("Fields", Window);
            UiKit.At(list, 40, y + 38, LeftW, whereTop - (y + 38) - 14);
            _fieldContent = UiKit.ScrollList("List", list, 6);
            _fieldView = (RectTransform)_fieldContent.parent;
            for (int i = 0; i < _d.Fields.Count; i++) _fields.Add(FieldRow(i));

            if (opts.Count > 0)
            {
                UiKit.At(UiKit.Label("WhereHead", Window, "<b>카드를 둘 곳</b>", 22, UiKit.Ink).rectTransform, 40, whereTop, LeftW, 32);
                for (int i = 0; i < opts.Count; i++) _wheres.Add(WhereRow(opts[i], 40, whereTop + 38 + i * (rowH + 6), rowH));
            }

            // ----- right: the desk with the paper and the dead phone -----
            float dx = 40 + LeftW + 40, dw = W - dx - 40;
            var desk = UiKit.Box("Desk", Window, Desk).rectTransform;
            UiKit.At(desk, dx, 130, dw, 700);
            for (int i = 0; i < 24; i++) // wood grain stripes
                UiKit.At(UiKit.Box("Grain", desk, new Color(0, 0, 0, i % 3 == 0 ? 0.08f : 0.03f)).rectTransform, i * dw / 24f, 0, 6, 700);

            var paper = UiKit.Box("Paper", desk, PaperBg).rectTransform;
            UiKit.At(paper, 24, 24, dw - 48, 450);
            paper.localRotation = Quaternion.Euler(0, 0, 1.2f); // CSS rotate(-1.2deg): y is flipped
            var shadow = paper.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.4f); shadow.effectDistance = new Vector2(0, -10);
            for (float ly = 110; ly < 450; ly += 28)
                UiKit.At(UiKit.Box("Line", paper, PaperLine).rectTransform, 0, ly, dw - 48, 1);
            UiKit.At(UiKit.Label("Head", paper, "<b>비상연락카드</b>", 26, Hex("#b3261e")).rectTransform, 20, 16, dw - 88, 36);
            UiKit.At(UiKit.Label("Sub", paper, "지갑 크기 종이 한 장 · 볼펜으로", 16, Hex("#6c6f73")).rectTransform, 20, 56, dw - 88, 24);
            var rowsBox = UiKit.Rect("RowsBox", paper);
            UiKit.At(rowsBox, 20, 112, dw - 88, 330);
            _paperRows = UiKit.Column(UiKit.Rect("Rows", rowsBox), 4);
            _paperRows.anchorMin = new Vector2(0, 1); _paperRows.anchorMax = new Vector2(1, 1); _paperRows.pivot = new Vector2(0.5f, 1);
            _paperRows.offsetMin = _paperRows.offsetMax = Vector2.zero;

            var phone = UiKit.Box("DeadPhone", desk, new Color(0.04f, 0.05f, 0.055f, 0.72f)).rectTransform;
            UiKit.At(phone, 24, 500, dw - 48, 176);
            var ico = UiKit.Box("Icon", phone, Hex("#4a4f55")).rectTransform;
            UiKit.At(ico, 16, 16, 40, 68);
            var screen = UiKit.Box("Screen", ico, Hex("#050607")).rectTransform;
            UiKit.Fill(screen, 3, 3);
            UiKit.Fill(UiKit.Label("Pct", screen, "<b>0%</b>", 13, UiKit.Red, TextAnchor.MiddleCenter).rectTransform);
            _phoneText = UiKit.Label("Count", phone, "", 19, UiKit.Ink);
            UiKit.At(_phoneText.rectTransform, 72, 12, dw - 48 - 88, 56);
            _phoneOnly = UiKit.Label("Only", phone, "", 17, Hex("#ffb0b2"));
            UiKit.At(_phoneOnly.rectTransform, 72, 70, dw - 48 - 88, 100);

            // ----- footer -----
            _status = UiKit.Label("Status", Window, "", 20, UiKit.Teal);
            UiKit.At(_status.rectTransform, 40, 846, 1100, 30);
            UiKit.At(UiKit.Label("Keys", Window, DocsLogic.Keys, 18, UiKit.Dim).rectTransform, 40, 880, 1100, 30);
            var done = Action(Window, "카드 다 썼다", true, () => Finish(_d.Result()), W - 40 - 250, 852, 250, 60);
            _doneLine = done.gameObject.AddComponent<Outline>();
            _doneLine.effectColor = FocusLine; _doneLine.effectDistance = new Vector2(3, -3);

            _d.InitFocus();
            Render();
        }

        (Image, Outline, Text) FieldRow(int i)
        {
            var f = _d.Fields[i];
            var bg = UiKit.Box("Field_" + f.Id, _fieldContent, Card, raycast: true);
            var line = bg.gameObject.AddComponent<Outline>();
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => { _d.FocusOn(DocsStop.Field, i); Say(_d.Toggle(i)); Render(); });
            var v = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(56, 14, 10, 10);
            v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;

            var text = $"<b>{f.Label}</b>" + (_d.ShowBadge(f) ? "  <size=16><color=#8fe6da>★ 권장</color></size>" : "") +
                $"\n<size=17><color=#9fb0b5>{f.Value}</color></size>" + (_d.ShowWhy(f) ? $"\n<size=16><color=#b9c6cc>{f.Why}</color></size>" : "");
            UiKit.Label("Text", bg.transform, text, 21, UiKit.Ink);

            var check = UiKit.Box("Check", bg.transform, new Color(1, 1, 1, 0.1f));
            check.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiKit.At(check.rectTransform, 16, 12, 26, 26);
            var mark = UiKit.Label("Mark", check.transform, "", 20, new Color(0.05f, 0.07f, 0.08f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Fill(mark.rectTransform);
            return (bg, line, mark);
        }

        (Image, Outline, Text, string) WhereRow(string key, float x, float y, float h)
        {
            var bg = UiKit.Box("Where_" + key, Window, Card, raycast: true);
            UiKit.At(bg.rectTransform, x, y, LeftW, h);
            var line = bg.gameObject.AddComponent<Outline>();
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => { _d.FocusOn(DocsStop.Where, 0); _d.SetWhere(key); Render(); });
            var dot = UiKit.Label("Dot", bg.transform, "", 24, UiKit.Amber, TextAnchor.MiddleCenter);
            UiKit.At(dot.rectTransform, 12, (h - 30) / 2, 32, 30);
            var (name, desc) = DocsLogic.WhereText[key];
            var t = UiKit.Label("Text", bg.transform, $"<b>{name}</b>" + (_d.ShowWhereDesc ? $"\n<size=17><color=#9fb0b5>{desc}</color></size>" : ""), 21, UiKit.Ink, TextAnchor.MiddleLeft);
            UiKit.At(t.rectTransform, 56, 4, LeftW - 70, h - 8);
            return (bg, line, dot, key);
        }

        // ---------- render ----------

        void Render()
        {
            var (kind, index) = _d.Focused;

            if (_hintBtn) _hintBtn.GetComponentInChildren<Text>().text = _d.HintButtonText;
            if (_hintBtn) SetFocus(_hintBtn.gameObject, kind == DocsStop.Hint);
            if (_helpBody) _helpBody.gameObject.SetActive(_d.HelpOpen);

            for (int i = 0; i < _fields.Count; i++)
            {
                var f = _d.Fields[i];
                var (bg, line, mark) = _fields[i];
                bool on = _d.IsOn(f), focus = kind == DocsStop.Field && index == i;
                mark.text = on ? "✓" : "";
                mark.transform.parent.GetComponent<Image>().color = on ? UiKit.Amber : new Color(1, 1, 1, 0.1f);
                bg.color = on ? new Color(0.95f, 0.7f, 0.24f, 0.08f) : Card;
                line.effectColor = focus ? FocusLine : _d.Mode == "guided" && f.Recommended ? RecLine : new Color(0, 0, 0, 0);
                line.effectDistance = focus ? new Vector2(3, -3) : new Vector2(1, -1);
            }
            foreach (var (bg, line, dot, key) in _wheres)
            {
                bool on = _d.Where == key;
                dot.text = on ? "●" : "○";
                bg.color = on ? new Color(0.95f, 0.7f, 0.24f, 0.08f) : Card;
                line.effectColor = kind == DocsStop.Where && on ? FocusLine : new Color(0, 0, 0, 0);
                line.effectDistance = new Vector2(3, -3);
            }
            // a group with no checked option still shows that it has focus
            if (kind == DocsStop.Where && !_wheres.Exists(w => w.key == _d.Where) && _wheres.Count > 0) _wheres[0].line.effectColor = FocusLine;
            _doneLine.enabled = kind == DocsStop.Done;

            // paper
            Clear(_paperRows);
            var written = _d.Written;
            foreach (var f in written) // one handwritten line per field: label, then the value (wraps when long)
                UiKit.Label("Row", _paperRows, $"<size=15><color=#5b6066>{f.Label}</color></size>   <color=#1f3d7a>{f.Value}</color>", 20, Hex("#1f3d7a"));
            if (written.Count == 0) UiKit.Label("Empty", _paperRows, DocsLogic.EmptyPaper, 18, Hex("#6c6f73"));

            // dead phone
            _phoneText.text = $"휴대폰이 꺼져도 카드로 볼 수 있음: <b>{_d.PhoneCount}</b>\n<size=16><color=#9fb0b5>휴대폰 속에만 있어 꺼지면 볼 수 없는 것:</color></size>";
            var left = _d.PhoneOnly;
            if (left.Count == 0) _phoneOnly.text = $"<color=#b7ecd9>✓ {DocsLogic.AllOnPaper}</color>";
            else
            {
                var parts = new List<string>();
                foreach (var f in left) parts.Add("✕ " + f.Label);
                _phoneOnly.text = string.Join("    ", parts);
            }
        }

        static void SetFocus(GameObject go, bool on)
        {
            var o = go.GetComponent<Outline>();
            if (o == null) o = go.AddComponent<Outline>();
            o.effectColor = FocusLine; o.effectDistance = new Vector2(3, -3); o.enabled = on;
        }

        void Say(string text) { if (!string.IsNullOrEmpty(text) && _status) _status.text = text; }

        /// <summary>Scrolls the field list so the focused field is in view.</summary>
        void Reveal()
        {
            var (kind, index) = _d.Focused;
            if (kind != DocsStop.Field || index >= _fields.Count) return;
            Canvas.ForceUpdateCanvases();
            var row = _fields[index].bg.rectTransform;
            float top = -row.anchoredPosition.y - row.rect.height * (1 - row.pivot.y), bottom = top + row.rect.height;
            float off = _fieldContent.anchoredPosition.y, viewH = _fieldView.rect.height;
            if (top < off) off = top - 6;
            else if (bottom > off + viewH) off = bottom - viewH + 6;
            _fieldContent.anchoredPosition = new Vector2(_fieldContent.anchoredPosition.x, Mathf.Max(0, off));
        }

        protected override void Update()
        {
            base.Update(); // Esc: closes with the card (EscapeResult)
            // Keys are handled here only: no uGUI selection, so Space/Enter/arrows never also 'submit' or move a Button.
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject) EventSystem.current.SetSelectedGameObject(null);
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.tabKey.wasPressedThisFrame) { _d.Tab(shift); Render(); Reveal(); }
            else if (kb.upArrowKey.wasPressedThisFrame) { _d.Arrow(-1); Render(); }
            else if (kb.downArrowKey.wasPressedThisFrame) { _d.Arrow(1); Render(); }
            else if (kb.spaceKey.wasPressedThisFrame)
            {
                var said = _d.Space(out var done);
                if (done) { Finish(_d.Result()); return; }
                Say(said); Render();
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                // Enter presses a focused button (done, hint), as in the browser; on a checkbox or radio it does nothing.
                if (_d.Focused.kind == DocsStop.Done) Finish(_d.Result());
                else if (_d.Focused.kind == DocsStop.Hint) { _d.ToggleHelp(); Render(); }
            }
        }
    }
}
