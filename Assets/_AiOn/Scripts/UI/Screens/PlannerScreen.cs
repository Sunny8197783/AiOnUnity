using System;
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
    /// The wall-calendar planner (original ui/planner.js), kept as a paper board: red calendar strip, the day, the time
    /// slots as tiles (지남 / 사용 preview), an optional hint, action cards on the left and the detail panel on the right
    /// (cost, slots left, what no longer fits, then '이 일을 한다' / '다시 고르기' once a card is chosen).
    /// Keys as the original: arrows move between cards, Enter/Space choose, Enter confirms (Tab or ←→ switch between the
    /// two confirm buttons), Esc goes back from a choice or closes with null. Rules live in <see cref="PlannerLogic"/>.
    /// </summary>
    public class PlannerScreen : UiScreen
    {
        // style.css planner colours
        static readonly Color Paper = Hex("#f4f0e7"), Paper2 = Hex("#fffdf8"), PInk = Hex("#1c2326"), PDim = Hex("#5b666b");
        static readonly Color Strip = Hex("#b8312b"), UsedTile = Hex("#ddd7cb"), UsedText = Hex("#8d9396");
        static readonly Color PreviewBg = Hex("#ffe2a3"), PreviewLine = Hex("#c27a00"), HintBg = Hex("#fff4d6"), HintText = Hex("#5a4300");
        static readonly Color SelectedBg = Hex("#fff6df"), BlockedBg = Hex("#ece8df"), BlockedText = Hex("#6a7276"), FocusLine = Hex("#0f7d72");
        static readonly Color LostBg = Hex("#fde8e6"), LostText = Hex("#7b1c18"), NopeBg = Hex("#f6d2cf");
        const string OkHex = "#0f7d72", NoHex = "#a3221d", DimHex = "#5b666b";

        const float W = 1720, H = 940, CardW = 352, CardH = 180, Gap = 12;

        PlannerLogic _p;
        RectTransform _tiles, _detail;
        readonly List<(Image bg, Outline line, Text text)> _cards = new();
        int _shownDetail = -2; bool _shownConfirming;
        int _confirmFocus; // 0: 이 일을 한다, 1: 다시 고르기
        Button _confirmBtn, _backBtn;
        int _nope = -1; float _nopeUntil;

        static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }

        /// <summary>planner({ day, slots: { total, used }, actions: [{ id, label, desc, cost, done, disabled, reason, icon }], hint }) -> actionId | null</summary>
        public static async Task<JToken> Open(JObject options)
        {
            var logic = PlannerLogic.Parse(options);
            var r = await Show<PlannerScreen>(s => s._p = logic, W, H);
            return r as JToken;
        }

        /// <summary>Adds the planner screen to the story host.</summary>
        public static void Register(AiOn.Story.UnityStoryHost host)
        {
            host.Screens["planner"] = Open;
        }

        // ---------- layout ----------

        protected override void Build()
        {
            var board = UiKit.Box("Board", Window, Paper).rectTransform;
            UiKit.Fill(board);
            UiKit.At(UiKit.Box("Strip", board, Strip).rectTransform, 0, 0, W, 16);

            UiKit.At(UiKit.Label("Eyebrow", board, "준비 계획표", 22, Hex("#a4610a"), style: FontStyle.Bold).rectTransform, 40, 38, 900, 30);
            var close = UiKit.Button("Close", board, "닫기", 22, new Color(0, 0, 0, 0.06f), PInk);
            UiKit.At((RectTransform)close.transform, W - 40 - 140, 34, 140, 50);
            close.onClick.AddListener(() => Finish(null));
            UiKit.At(UiKit.Label("Day", board, _p.Day, 40, PInk, style: FontStyle.Bold).rectTransform, 40, 70, 1400, 54);
            UiKit.At(UiKit.Label("Sub", board,
                $"남은 칸 <color=#b8312b><b><size=27>{PlannerLogic.JsNum(_p.Left)}</size></b></color> / {PlannerLogic.JsNum(_p.Total)} · 모든 일을 다 할 수는 없습니다.",
                22, PDim).rectTransform, 40, 126, 1400, 34);

            _tiles = UiKit.Rect("Slots", board);
            UiKit.At(_tiles, 40, 170, W - 80, 74);

            float y = 262;
            if (_p.Hint != "")
            {
                var hint = UiKit.Box("Hint", board, HintBg).rectTransform;
                UiKit.At(hint, 40, y, W - 80, 54);
                hint.gameObject.AddComponent<Outline>().effectColor = Hex("#ecd08a");
                UiKit.Fill(UiKit.Label("Text", hint, "※  " + _p.Hint, 20, HintText, TextAnchor.MiddleLeft).rectTransform, 16, 4);
                y += 70;
            }

            // Cards (left): three per row in a scrolling list.
            float gridW = CardW * 3 + Gap * 2, bodyH = H - y - 30;
            var gridBox = UiKit.Rect("Grid", board);
            UiKit.At(gridBox, 40, y, gridW + 12, bodyH);
            var content = UiKit.ScrollList("Cards", gridBox, Gap);
            var view = content.parent.GetComponent<Image>();
            view.color = new Color(0, 0, 0, 0.0f);
            Hover.On(view.gameObject, null, () => { _p.Leave(); Render(); });
            RectTransform row = null;
            for (int i = 0; i < _p.Actions.Count; i++)
            {
                if (i % 3 == 0) { row = UiKit.Rect("Row", content); UiKit.Height(row, CardH); }
                _cards.Add(Card(row, i));
            }

            // Detail (right)
            var detail = UiKit.Box("Detail", board, Paper2).rectTransform;
            UiKit.At(detail, 40 + gridW + 30, y, W - (40 + gridW + 30) - 40, bodyH);
            detail.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.12f);
            _detail = UiKit.Rect("Body", detail);
            UiKit.Fill(_detail, 22, 18);

            Render();
        }

        (Image, Outline, Text) Card(RectTransform row, int i)
        {
            var a = _p.Actions[i];
            var bg = UiKit.Box("Card_" + i, row, Paper2, raycast: true);
            UiKit.At(bg.rectTransform, (i % 3) * (CardW + Gap), 0, CardW, CardH);
            var line = bg.gameObject.AddComponent<Outline>();
            line.effectDistance = new Vector2(2, -2);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Choose(i));
            Hover.On(bg.gameObject, () => { _p.Hover(i); Render(); }, null);

            UiKit.At(UiKit.Icon("Icon", bg.transform, a.Icon != "" ? a.Icon : "•", 30, PInk, TextAnchor.UpperCenter).rectTransform, 14, 14, 38, 38);
            var cost = UiKit.Box("Cost", bg.transform, PInk).rectTransform;
            UiKit.At(cost, CardW - 14 - 64, 14, 64, 30);
            UiKit.Fill(UiKit.Label("Text", cost, PlannerLogic.CostText(a), 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
            var text = UiKit.Label("Text", bg.transform, "", 21, PInk);
            UiKit.At(text.rectTransform, 62, 12, CardW - 62 - 86, CardH - 20);
            return (bg, line, text);
        }

        // ---------- render ----------

        void Render()
        {
            // slot tiles
            Clear(_tiles);
            var slots = _p.Slots();
            for (int n = 0; n < slots.Count; n++)
            {
                var s = slots[n];
                var tile = UiKit.Box("Slot", _tiles, s.Used ? UsedTile : s.Preview ? PreviewBg : Paper2);
                UiKit.At(tile.rectTransform, n * 94, 0, 84, 72);
                var o = tile.gameObject.AddComponent<Outline>();
                o.effectColor = s.Preview ? PreviewLine : new Color(0, 0, 0, s.Used ? 0.1f : 0.22f);
                o.effectDistance = new Vector2(2, -2);
                var fg = s.Used ? UsedText : PInk;
                UiKit.At(UiKit.Label("No", tile.transform, $"<b>{s.No}</b>", 24, fg, TextAnchor.MiddleCenter).rectTransform, 0, 6, 84, 34);
                UiKit.At(UiKit.Label("State", tile.transform, s.State, 15, s.Used ? UsedText : PDim, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 0, 42, 84, 22);
            }

            // cards (built once: only their look changes, so the pointer is never lost)
            for (int i = 0; i < _cards.Count; i++)
            {
                var a = _p.Actions[i];
                var (bg, line, text) = _cards[i];
                bool sel = i == _p.Selected, focus = i == _p.Focus && _p.Selected < 0;
                bool nope = i == _nope && Time.unscaledTime < _nopeUntil;
                bg.color = nope ? NopeBg : sel ? SelectedBg : a.Blocked ? BlockedBg : Paper2;
                line.effectColor = sel ? PreviewLine : focus ? FocusLine : new Color(0, 0, 0, 0.12f);
                line.effectDistance = sel || focus ? new Vector2(3, -3) : new Vector2(1, -1);
                var status = PlannerLogic.CardStatus(a);
                string ink = a.Blocked ? "#6a7276" : "#1c2326";
                text.text = $"<color={ink}><b>{a.Label}</b></color>\n<size=16><color={DimHex}>{a.Desc}</color></size>" +
                    (status == null ? "" : $"\n<size=16><b><color={(status.Value.ok ? OkHex : NoHex)}>{(status.Value.ok ? "✓" : "✕")} {status.Value.text}</color></b></size>");
            }

            if (_p.DetailIndex != _shownDetail || _p.Confirming != _shownConfirming) Detail();
            else FocusConfirm();
        }

        void Detail()
        {
            _shownDetail = _p.DetailIndex; _shownConfirming = _p.Confirming;
            _confirmBtn = _backBtn = null;
            Clear(_detail);
            var col = UiKit.Column(UiKit.Rect("Col", _detail), 12);
            col.anchorMin = new Vector2(0, 1); col.anchorMax = new Vector2(1, 1); col.pivot = new Vector2(0.5f, 1);
            col.offsetMin = col.offsetMax = Vector2.zero;
            if (_p.DetailIndex < 0) { Para(col, PlannerLogic.Placeholder, 20, PDim); return; }

            var a = _p.Actions[_p.DetailIndex];
            Para(col, $"<b>{(a.Icon != "" && !UiKit.Emoji(a.Icon) ? a.Icon + " " : "")}{a.Label}</b>", 28, PInk);
            Para(col, a.Desc, 19, Hex("#3a4448"));
            if (a.Blocked)
            {
                Para(col, $"<b><color={NoHex}>✕ {PlannerLogic.BlockedText(a)}</color></b>", 19, PInk);
                return;
            }
            Para(col, $"<color={DimHex}>드는 시간</color>    <b>{PlannerLogic.CostText(a)}</b>\n<color={DimHex}>남는 칸</color>       <b>{_p.AfterText(a)}</b>", 21, PInk);
            var lost = _p.Lost(a);
            if (lost.Count > 0)
            {
                var box = UiKit.Box("Lost", col, LostBg);
                var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(16, 16, 10, 12);
                v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
                var lines = "<b>이걸 하면 못 하게 되는 일</b>";
                foreach (var b in lost) lines += "\n  · " + b.Label;
                UiKit.Label("Text", box.transform, lines, 18, LostText);
            }
            if (_p.Confirming)
            {
                var row = UiKit.Rect("Confirm", col);
                UiKit.Height(row, 64);
                _confirmBtn = Action(row, "✓ 이 일을 한다", true, () => Finish(_p.Confirm()), 0, 4, 220, 56);
                _backBtn = UiKit.Button("Back", row, "다시 고르기", 22, new Color(0, 0, 0, 0.06f), PInk);
                UiKit.At((RectTransform)_backBtn.transform, 236, 4, 180, 56);
                _backBtn.onClick.AddListener(Back);
                _confirmFocus = 0;
                FocusConfirm();
            }
        }

        void FocusConfirm()
        {
            if (_confirmBtn == null || _backBtn == null) return;
            SetLine(_confirmBtn.gameObject, _confirmFocus == 0);
            SetLine(_backBtn.gameObject, _confirmFocus == 1);
        }

        static void SetLine(GameObject go, bool on)
        {
            var o = go.GetComponent<Outline>();
            if (o == null) o = go.AddComponent<Outline>();
            o.effectColor = FocusLine; o.effectDistance = new Vector2(3, -3); o.enabled = on;
        }

        static void Para(RectTransform col, string text, int size, Color color) => UiKit.Label("P", col, text, size, color);

        // ---------- actions ----------

        void Choose(int i)
        {
            if (!_p.Choose(i)) { _nope = i; _nopeUntil = Time.unscaledTime + 0.35f; }
            else _confirmFocus = 0; // the original re-renders the detail and focuses 이 일을 한다 on every choice
            Render();
        }

        void Back() { _p.Back(); Render(); }

        protected override void Update()
        {
            base.Update(); // EscapeResult is null: Esc is handled here (back first, then close with null)
            // Keys are handled here only: no uGUI selection, so Enter/Space/arrows never also 'submit' or move a Button.
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject) EventSystem.current.SetSelectedGameObject(null);
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            if (_nope >= 0 && Time.unscaledTime >= _nopeUntil) { _nope = -1; Render(); }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_p.Escape()) Finish(null); else Render();
                return;
            }
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            if (_p.Selected >= 0)
            {
                if (kb.tabKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                { _confirmFocus = 1 - _confirmFocus; FocusConfirm(); }
                else if (enter) { if (_confirmFocus == 0) Finish(_p.Confirm()); else Back(); }
                return;
            }
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            // Tab / Shift+Tab walk the cards as browser focus does (the 닫기 button is not a stop here: mouse or Esc).
            int step = kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && !shift) ? 1
                : kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && shift) ? -1 : 0;
            if (step != 0) { _p.Step(step); Render(); }
            else if (enter && _p.Focus >= 0) Choose(_p.Focus);
        }

        /// <summary>Pointer enter/exit callbacks for a UI object (mouseenter / mouseleave of the original).</summary>
        sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            Action _enter, _exit;
            public static void On(GameObject go, Action enter, Action exit)
            {
                var h = go.AddComponent<Hover>();
                h._enter = enter; h._exit = exit;
            }
            public void OnPointerEnter(PointerEventData e) => _enter?.Invoke();
            public void OnPointerExit(PointerEventData e) => _exit?.Invoke();
        }
    }
}
