using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// Packing one bag (original ui/bag.js, design kept): two panes (집 비축 / 가방) where a click or a key moves one
    /// unit (Shift: all of it), weight and volume meters, the goals checklist, the ladder help (guided: shown, hint:
    /// behind a button, free: none and no ticks). New here: a 10x5 grid preview of where each piece sits in the bag.
    /// Keys: ↑↓ choose, → put in (home list) / ← take out (bag list), Tab switch list, Enter move one, Shift all,
    /// Esc or 다 챙겼다 closes. Nothing is applied to the game until it closes.
    /// </summary>
    public class BagScreen : MonoBehaviour
    {
        public static BagScreen Current { get; private set; }

        BagPackingModel _model;
        string _mode, _title;
        double _limitKg, _limitL;
        IReadOnlyList<BagGoal> _goals;
        IReadOnlyList<string> _tips;
        Action<Dictionary<string, int>> _onDone;

        RectTransform _homeList, _bagList, _goalList, _grid, _help;
        Text _homeCount, _bagCount, _wText, _vText, _status;
        Image _wBar, _vBar;
        bool _bagPane; // which list the keyboard is on
        int _sel;
        bool _helpOpen;
        float _openedAt;

        public bool IsOpen => _model != null;
        public string StatusText => _status ? _status.text : null;

        /// <summary>Opens the screen; onDone gets the wanted bag count per item when it closes.</summary>
        public static BagScreen Open(BagPackingModel model, string context, string mode, double limitKg, double limitL,
            IReadOnlyList<BagGoal> goals, IReadOnlyList<string> tips, Action<Dictionary<string, int>> onDone)
        {
            if (Current) Current.Close();
            var canvas = UiKit.ScreenCanvas("BagScreen", 200);
            var s = canvas.gameObject.AddComponent<BagScreen>();
            s._model = model; s._mode = mode; s._limitKg = limitKg; s._limitL = limitL;
            s._goals = goals; s._tips = tips ?? Array.Empty<string>(); s._onDone = onDone;
            s._title = BagDrill.Titles.TryGetValue(context, out var t) ? t : "가방 꾸리기";
            s._helpOpen = mode == "guided";
            Current = s;
            InputGate.Hold();
            PlayerInputReader.SetCursorLocked(false);
            s.Build();
            s.Render();
            s._openedAt = Time.unscaledTime;
            return s;
        }

        /// <summary>Closes and hands the result over ('다 챙겼다' and Esc both keep what was packed, like the original).</summary>
        public void Close()
        {
            if (_model == null) return;
            var result = _model.Result();
            _model = null;
            if (Current == this) Current = null;
            InputGate.Release();
            PlayerInputReader.SetCursorLocked(true);
            Destroy(gameObject);
            _onDone?.Invoke(result);
        }

        void OnDestroy()
        {
            if (_model != null) { _model = null; InputGate.Release(); }
            if (Current == this) Current = null;
        }

        // ---------- layout ----------

        void Build()
        {
            var root = (RectTransform)transform;
            UiKit.Fill(UiKit.Box("Shade", root, new Color(0, 0, 0, 0.55f), raycast: true).rectTransform);
            var win = UiKit.Box("Window", root, UiKit.Panel).rectTransform;
            win.anchorMin = win.anchorMax = win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = new Vector2(1720, 940);

            UiKit.At(UiKit.Label("Kicker", win, "가방", 22, UiKit.Dim).rectTransform, 40, 26, 300, 30);
            UiKit.At(UiKit.Label("Title", win, _title, 40, UiKit.Ink, style: FontStyle.Bold).rectTransform, 40, 52, 900, 56);
            var stage = _mode == "guided" ? "안내" : _mode == "hint" ? "힌트" : "자유";
            var chip = UiKit.Box("Stage", win, new Color(0.24f, 0.78f, 0.71f, 0.18f)).rectTransform;
            UiKit.At(chip, 1560, 34, 120, 40);
            UiKit.Fill(UiKit.Label("Text", chip, stage, 22, UiKit.Teal, TextAnchor.MiddleCenter).rectTransform);

            // Home pane
            var home = UiKit.Rect("HomePane", win); UiKit.At(home, 40, 130, 540, 690);
            _homeCount = UiKit.Label("Head", home, "", 26, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_homeCount.rectTransform, 0, 0, 540, 40);
            var homeBody = UiKit.Rect("Body", home); UiKit.At(homeBody, 0, 46, 540, 644);
            _homeList = UiKit.ScrollList("List", homeBody);

            // Bag pane: grid preview on top, list below
            var bag = UiKit.Rect("BagPane", win); UiKit.At(bag, 600, 130, 560, 690);
            _bagCount = UiKit.Label("Head", bag, "", 26, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_bagCount.rectTransform, 0, 0, 560, 40);
            _grid = UiKit.Box("Grid", bag, new Color(0, 0, 0, 0.3f)).rectTransform;
            UiKit.At(_grid, 0, 46, 560, 284);
            var bagBody = UiKit.Rect("Body", bag); UiKit.At(bagBody, 0, 340, 560, 350);
            _bagList = UiKit.ScrollList("List", bagBody);

            // Side: meters, goals, help
            var side = UiKit.Rect("Side", win); UiKit.At(side, 1190, 130, 490, 690);
            (_wText, _wBar) = Meter(side, 0, "무게");
            (_vText, _vBar) = Meter(side, 92, "부피");
            UiKit.At(UiKit.Label("Why", side, $"무게가 {BagDrill.FormatKg(_limitKg)}을 넘으면 걸음이 느려집니다.", 20, UiKit.Dim).rectTransform, 0, 186, 490, 30);
            if (_goals.Count > 0) UiKit.At(UiKit.Label("GoalsHead", side, "챙길 것", 26, UiKit.Ink, style: FontStyle.Bold).rectTransform, 0, 228, 490, 36);
            _goalList = UiKit.Rect("Goals", side); UiKit.At(_goalList, 0, 270, 490, 236);
            UiKit.Column(_goalList, 4);
            _help = UiKit.Rect("Help", side); UiKit.At(_help, 0, 512, 490, 178);

            // Footer
            _status = UiKit.Label("Status", win, "", 22, UiKit.Teal);
            UiKit.At(_status.rectTransform, 40, 832, 1100, 32);
            UiKit.At(UiKit.Label("Keys", win, "누르기(Enter) 한 개 · Shift 전부 · → 넣기 · ← 꺼내기 · ↑↓ 이동 · Tab 목록 바꾸기 · Esc 닫기", 18, UiKit.Dim).rectTransform, 40, 876, 1100, 30);
            var empty = UiKit.Button("Empty", win, "가방 비우기", 24, new Color(1, 1, 1, 0.08f), UiKit.Ink);
            UiKit.At((RectTransform)empty.transform, 1190, 852, 220, 60);
            empty.onClick.AddListener(() => { _model.Empty(); Say("가방을 비웠다."); Render(); });
            var done = UiKit.Button("Done", win, "다 챙겼다", 26, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            UiKit.At((RectTransform)done.transform, 1430, 852, 250, 60);
            done.onClick.AddListener(Close);
        }

        (Text, Image) Meter(RectTransform side, float y, string label)
        {
            UiKit.At(UiKit.Label(label, side, label, 20, UiKit.Dim).rectTransform, 0, y, 120, 28);
            var text = UiKit.Label(label + "Text", side, "", 24, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(text.rectTransform, 80, y - 2, 410, 32);
            var track = UiKit.Box(label + "Track", side, new Color(1, 1, 1, 0.08f)).rectTransform;
            UiKit.At(track, 0, y + 38, 490, 18);
            var fill = UiKit.Box("Fill", track, UiKit.Teal);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return (text, fill);
        }

        // ---------- render ----------

        void Render()
        {
            var home = _model.AtHome.ToList();
            var bag = _model.InBag.ToList();
            _homeCount.text = $"집 비축  <color=#9aa6ad>{home.Sum(i => _model.HomeOf(i.Id))}개</color>";
            _bagCount.text = $"가방  <color=#9aa6ad>{bag.Sum(i => _model.BagOf(i.Id))}개</color>";
            var list = _bagPane ? bag : home;
            if (list.Count == 0 && (_bagPane ? home : bag).Count > 0) { _bagPane = !_bagPane; list = _bagPane ? bag : home; }
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, list.Count - 1));

            Rows(_homeList, home, false, "집에 남은 물건이 없다.");
            Rows(_bagList, bag, true, "아직 비어 있다.");
            Grid();

            var (w, v) = _model.Load();
            SetMeter(_wText, _wBar, w, _limitKg, BagDrill.FormatKg(w), BagDrill.FormatKg(_limitKg));
            SetMeter(_vText, _vBar, v, _limitL, BagDrill.FormatL(v), $"{_limitL:0}L");

            Clear(_goalList);
            var goals = BagDrill.GoalStatus(_goals, _mode, _model.BagOf, _model.Items).ToList();
            // The list fits its goals (the evacuation bag has ten) and the help moves down under it instead of over it.
            bool tight = goals.Count > 6;
            float rowH = tight ? 28 : 32;
            foreach (var (label, ok) in goals)
            {
                string mark = ok == null ? "·" : ok.Value ? "<color=#3cc6b5>✓</color>" : "<color=#9aa6ad>○</color>";
                var t = UiKit.Label("Goal", _goalList, $"{mark}  {label}", tight ? 20 : 22, ok == true ? UiKit.Ink : UiKit.Dim);
                UiKit.Height(t, rowH);
            }
            float listH = Mathf.Max(236, goals.Count * rowH + Mathf.Max(0, goals.Count - 1) * 4);
            UiKit.At(_goalList, 0, 270, 490, listH);
            float helpY = 270 + listH + 6;
            UiKit.At(_help, 0, helpY, 490, Mathf.Max(48, 690 - helpY));
            Help();
        }

        void SetMeter(Text text, Image bar, double val, double max, string valText, string maxText)
        {
            var state = BagDrill.MeterState(val, max);
            text.text = $"{valText} / {maxText} · {BagDrill.MeterWord(state)}";
            bar.rectTransform.anchorMax = new Vector2((float)Math.Min(1, val / max), 1);
            bar.color = state == "over" ? UiKit.Red : state == "warn" ? UiKit.Amber : UiKit.Teal;
        }

        void Rows(RectTransform list, List<BagItemView> items, bool inBag, string emptyText)
        {
            Clear(list);
            if (items.Count == 0)
            {
                UiKit.Height(UiKit.Label("Empty", list, emptyText, 22, UiKit.Dim, TextAnchor.MiddleCenter), 60);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool selected = inBag == _bagPane && i == _sel;
                int n = inBag ? _model.BagOf(it.Id) : _model.HomeOf(it.Id);
                var row = UiKit.Box("Row_" + it.Id, list, selected ? UiKit.RowSelected : UiKit.Row, raycast: true);
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = row;
                var id = it.Id;
                btn.onClick.AddListener(() => Move(id, !inBag, ShiftHeld() ? int.MaxValue : 1));

                var note = BagDrill.ItemNote(it);
                var spec = $"{BagDrill.FormatKg(it.WeightKg)} · {it.VolumeL.ToString(System.Globalization.CultureInfo.InvariantCulture)}L" + (note != "" && note != "기한 지남" ? $" · ({note})" : "");
                var badges = string.Join(" ", BagDrill.ShownTags(it).Select(t => BagDrill.WarnTags.Contains(t)
                    ? $"<color=#f2b33d>! {BagDrill.TagName(t)}</color>" : $"<color=#7fb8c4>{BagDrill.TagName(t)}</color>"));
                bool showTip = !string.IsNullOrEmpty(it.Tip) && _mode != "free";
                var arrow = inBag ? "←  " : "";
                var tail = inBag ? "" : "  →";
                var text = UiKit.Label("Text", row.transform,
                    $"{arrow}<b>{it.Name}</b> <color=#9aa6ad>×{n}</color>{tail}\n<size=18><color=#9aa6ad>{spec}</color>   {badges}</size>" +
                    (showTip ? $"\n<size=17><color=#b9c6cc>{it.Tip}</color></size>" : ""), 23, UiKit.Ink);
                // The row grows with its text: a long tip wraps onto more lines instead of spilling over the next row.
                var fit = row.gameObject.AddComponent<VerticalLayoutGroup>();
                fit.padding = new RectOffset(14, 14, 8, 10);
                fit.childControlWidth = true; fit.childForceExpandWidth = true;
                fit.childControlHeight = true; fit.childForceExpandHeight = false;
            }
        }

        static readonly Dictionary<string, Color> CategoryColor = new()
        {
            ["water"] = new Color(0.25f, 0.55f, 0.85f), ["food"] = new Color(0.85f, 0.45f, 0.2f), ["info"] = new Color(0.55f, 0.45f, 0.8f),
            ["power"] = new Color(0.8f, 0.75f, 0.25f), ["light"] = new Color(0.95f, 0.85f, 0.4f), ["health"] = new Color(0.2f, 0.65f, 0.6f),
            ["hygiene"] = new Color(0.55f, 0.75f, 0.85f), ["warmth"] = new Color(0.8f, 0.4f, 0.45f), ["tool"] = new Color(0.55f, 0.58f, 0.6f),
            ["fire"] = new Color(0.9f, 0.3f, 0.25f), ["docs"] = new Color(0.85f, 0.85f, 0.8f), ["keepsake"] = new Color(0.7f, 0.55f, 0.4f),
        };

        void Grid()
        {
            Clear(_grid);
            var layout = _model.Layout();
            const float cell = 50, pad = 30;
            float ox = (560 - 10 * cell) / 2, oy = 22;
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 10; x++)
                    UiKit.At(UiKit.Box("Cell", _grid, new Color(1, 1, 1, 0.05f)).rectTransform, ox + x * cell + 1, oy + y * cell + 1, cell - 2, cell - 2);
            foreach (var p in layout.Placed)
            {
                var it = _model.Items.First(i => i.Id == p.Id);
                var col = CategoryColor.TryGetValue(it.Tags[0], out var c) ? c : UiKit.Dim;
                var piece = UiKit.Box("Piece_" + p.Id, _grid, new Color(col.r, col.g, col.b, 0.85f));
                UiKit.At(piece.rectTransform, ox + p.X * cell + 3, oy + p.Y * cell + 3, p.W * cell - 6, p.H * cell - 6);
                if (p.W * p.H >= 2)
                {
                    var name = it.Name.Split('(', ' ')[0];
                    UiKit.Fill(UiKit.Label("Name", piece.transform, name, 15, new Color(0.05f, 0.07f, 0.08f), TextAnchor.MiddleCenter).rectTransform, 2, 2);
                }
            }
            UiKit.At(UiKit.Label("Caption", _grid, $"칸 {layout.Used} / 50 사용 (0.5L 칸, 큰 것부터 차곡차곡 넣은 모습)", 17, UiKit.Dim).rectTransform, ox, oy + 5 * cell + 4, 500, pad);
        }

        void Help()
        {
            Clear(_help);
            if (_mode == "free") return;
            float y = 0;
            if (_mode == "hint")
            {
                var b = UiKit.Button("HintToggle", _help, _helpOpen ? "힌트 닫기" : "힌트 보기", 20, new Color(1, 1, 1, 0.08f), UiKit.Ink);
                UiKit.At((RectTransform)b.transform, 0, 0, 160, 40);
                b.onClick.AddListener(() => { _helpOpen = !_helpOpen; Help(); });
                y = 48;
            }
            if (!_helpOpen) return;
            var lines = new List<string> { "목표 목록에서 ○ 표시가 남은 것을 먼저 채워 보세요. 무게와 부피 막대가 넘치지 않게." };
            lines.AddRange(_tips.Select(t => "· " + t));
            if (_mode == "hint") lines.Add("각 물건 아래에 도움말이 함께 보입니다.");
            var box = UiKit.Rect("Scroll", _help);
            UiKit.At(box, 0, y, 490, Mathf.Max(40, _help.rect.height - y));
            var content = UiKit.ScrollList("Tips", box, 2); // all the tips, scrolling instead of cut off
            UiKit.Label("Text", content, string.Join("\n", lines), 17, new Color(0.73f, 0.78f, 0.8f));
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
            t.DetachChildren();
        }

        // ---------- actions ----------

        /// <summary>Moves units of id (fromHome: into the bag) and says what happened, like the original.</summary>
        public int Move(string id, bool fromHome, int n = 1)
        {
            var it = _model.Items.FirstOrDefault(i => i.Id == id);
            if (it == null) return 0;
            int k = _model.Move(id, fromHome, n);
            if (k == 0)
            {
                if (fromHome && _model.HomeOf(id) > 0) Say($"{it.Name}: 가방에 더 들어가지 않는다. 다른 것을 꺼내면 자리가 생긴다.");
                return 0;
            }
            var (w, _) = _model.Load();
            Say($"{it.Name} {k}개를 {(fromHome ? "가방에 넣었다" : "집에 두었다")}. 가방 {BagDrill.FormatKg(w)}");
            Render();
            return k;
        }

        void Say(string text) { if (_status) _status.text = text; }

        static bool ShiftHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        void Update()
        {
            if (_model == null || Time.unscaledTime - _openedAt < 0.2f) return; // the E that opened it
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            var list = (_bagPane ? _model.InBag : _model.AtHome).ToList();
            if (kb.downArrowKey.wasPressedThisFrame) { _sel = Mathf.Min(_sel + 1, Mathf.Max(0, list.Count - 1)); Render(); }
            else if (kb.upArrowKey.wasPressedThisFrame) { _sel = Mathf.Max(0, _sel - 1); Render(); }
            else if (kb.tabKey.wasPressedThisFrame) { _bagPane = !_bagPane; _sel = 0; Render(); }
            else if (list.Count > 0)
            {
                int n = ShiftHeld() ? int.MaxValue : 1;
                var id = list[_sel].Id;
                if (!_bagPane && kb.rightArrowKey.wasPressedThisFrame) Move(id, true, n);
                else if (_bagPane && (kb.leftArrowKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame)) Move(id, false, n);
                else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Move(id, !_bagPane, n);
                else if (!_bagPane && kb.leftArrowKey.wasPressedThisFrame) { _bagPane = true; _sel = 0; Render(); }
                else if (_bagPane && kb.rightArrowKey.wasPressedThisFrame) { _bagPane = false; _sel = 0; Render(); }
            }
        }
    }
}
