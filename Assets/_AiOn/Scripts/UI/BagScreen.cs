using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The bag screen's pure checks: why a goal with its item packed is still open (the item is not ready: a torch
    /// whose batteries were never checked), and the one soft confirm before leaving without a critical goal.
    /// </summary>
    public static class BagCheck
    {
        /// <summary>The port's tag for a packed item that does not count yet (BagItemView.Ready false).</summary>
        public const string NotReady = "작동 확인 안 함";

        /// <summary>Goals worth one "are you sure" before leaving (BagDrill goal ids): ID / documents, then water.</summary>
        public static readonly string[] Critical = { "docs", "water" };

        static bool Counts(string id, Func<string, int> bagOf, IReadOnlyList<BagItemView> items) =>
            bagOf(id) > 0 && items.FirstOrDefault(i => i.Id == id)?.Ready != false;

        public static bool Met(BagGoal g, Func<string, int> bagOf, IReadOnlyList<BagItemView> items) =>
            BagDrill.GoalMet(g, bagOf, id => Counts(id, bagOf, items));

        /// <summary>NotReady when the goal is open although one of its items is packed (it is not ready); else null.</summary>
        public static string GoalNote(BagGoal g, Func<string, int> bagOf, IReadOnlyList<BagItemView> items) =>
            !Met(g, bagOf, items) && (g.Items ?? Array.Empty<string>()).Any(id => bagOf(id) > 0 && items.FirstOrDefault(i => i.Id == id)?.Ready == false)
                ? NotReady : null;

        /// <summary>The first critical goal still open, or null (none, or the free stage: no help there).</summary>
        public static BagGoal CriticalOpen(IEnumerable<BagGoal> goals, string mode, Func<string, int> bagOf, IReadOnlyList<BagItemView> items) =>
            CriticalOpenAll(goals, mode, bagOf, items).FirstOrDefault();

        /// <summary>Every critical goal still open, in <see cref="Critical"/> order (empty in the free stage).</summary>
        public static List<BagGoal> CriticalOpenAll(IEnumerable<BagGoal> goals, string mode, Func<string, int> bagOf, IReadOnlyList<BagItemView> items)
        {
            var open = new List<BagGoal>();
            if (mode == "free" || goals == null) return open;
            var list = goals.ToList();
            foreach (var id in Critical)
            {
                var g = list.FirstOrDefault(x => x.Id == id);
                if (g != null && !Met(g, bagOf, items)) open.Add(g);
            }
            return open;
        }

        public static string Question(BagGoal g) => $"{Noun(g)} 없이 나갈까요?";

        /// <summary>The question naming every critical gap: "신분증과 마실 물 없이 나갈까요?" when both are open.</summary>
        public static string Question(IReadOnlyList<BagGoal> open)
        {
            if (open == null || open.Count == 0) return "";
            if (open.Count == 1) return Question(open[0]);
            string a = Noun(open[0]);
            return $"{a}{(HasBatchim(a) ? "과" : "와")} {Noun(open[1])} 없이 나갈까요?";
        }

        static string Noun(BagGoal g) => g.Id == "docs" ? "신분증" : g.Id == "water" ? "마실 물" : g.Label;
        static bool HasBatchim(string w)
        {
            char c = string.IsNullOrEmpty(w) ? ' ' : w[w.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0;
        }

        /// <summary>
        /// "아직 ○ : a, b 외 N개", the line under the question: at most <paramref name="max"/> names and no more than
        /// <paramref name="maxChars"/> characters of names, so the line never wraps with '외 N개' left on its own.
        /// </summary>
        public static string OpenSummary(IReadOnlyList<string> open, int max = 2, int maxChars = 18)
        {
            if (open == null || open.Count == 0) return "";
            var names = new List<string>();
            int chars = 0;
            foreach (var n in open)
            {
                if (names.Count >= max || (names.Count > 0 && chars + n.Length + 2 > maxChars)) break;
                names.Add(n); chars += n.Length + 2;
            }
            return "아직 ○ : " + string.Join(", ", names) + (open.Count > names.Count ? $" 외 {open.Count - names.Count}개" : "");
        }

        /// <summary>The other goals still open (labels), for the line under the question.</summary>
        public static List<string> OpenLabels(IEnumerable<BagGoal> goals, Func<string, int> bagOf, IReadOnlyList<BagItemView> items) =>
            (goals ?? Enumerable.Empty<BagGoal>()).Where(g => !Met(g, bagOf, items)).Select(g => g.Label).ToList();
    }


    /// <summary>What the bag screen is opened with (the story's options object, or the free-roam bag point's).</summary>
    public sealed class BagScreenOptions
    {
        public string Context = "alert", Mode = "guided";
        public double LimitKg = 8, LimitL = 25;
        public IReadOnlyList<BagGoal> Goals = Array.Empty<BagGoal>();
        public IReadOnlyList<string> Tips = Array.Empty<string>();
        /// <summary>What '정리' costs in game time ("1분"), or null when it is free.</summary>
        public string TidyCost;
        /// <summary>&gt; 0: a countdown in real seconds (only the re-pack right after the siren). The screen closes itself at 0.</summary>
        public double TimerSeconds;
        /// <summary>What one '정리' takes off the countdown.</summary>
        public double TidySeconds = 10;
        public string Profile = "";
        public bool ReduceMotion;
        /// <summary>A fact's text and "publisher · title" for the score's sourced lines (null: lines show no source).</summary>
        public Func<string, FactCitation> Cite;
    }

    /// <summary>What the screen hands back when it closes.</summary>
    public sealed class BagScreenResult
    {
        public Dictionary<string, int> Bag;
        public string Layout;
        public List<string> Aids;
        public BagScore Score;
        public int Tidied;
        public double Elapsed;
        public bool TimedOut;

        /// <summary>The story's { bag, layout, aids, score, tidied, elapsed, timedOut } (the original's { bag } plus the new keys).</summary>
        public Newtonsoft.Json.Linq.JObject ToJson()
        {
            var bag = new Newtonsoft.Json.Linq.JObject();
            foreach (var kv in Bag ?? new Dictionary<string, int>()) bag[kv.Key] = kv.Value;
            return new Newtonsoft.Json.Linq.JObject
            {
                ["bag"] = bag, ["layout"] = Layout ?? "", ["aids"] = new Newtonsoft.Json.Linq.JArray(Aids ?? new List<string>()),
                ["score"] = Score?.ToJson(), ["tidied"] = Tidied, ["elapsed"] = Math.Round(Elapsed, 1), ["timedOut"] = TimedOut,
            };
        }
    }

    /// <summary>
    /// Packing one bag (original ui/bag.js two panes, 집 비축 / 가방, kept) with the competition upgrade's 직접 놓기:
    /// the 10x5 grid is the bag itself. A unit goes where it is put and stays there; drag it (or click to pick it up and
    /// click again to put it down: no holding needed), R or right-click turns it, dropping it on the home list takes it
    /// out. A click on a home row puts one in at the first free spot; when things lie badly there is no spot, and '정리'
    /// (biggest first, costs time) makes one. Keyboard: Tab home list / grid / bag list, arrows, Enter put in or pick up
    /// and put down, Space carry a home item to the grid, R turn, Delete take out, T 정리, Esc. Pad: d-pad, A, B, Y turn,
    /// X 정리, LB/RB lists, Start 다 챙겼다. The side panel scores the bag so it teaches: the official list (sourced),
    /// how things lie (game hints), the weight limit and the experience mode's own things. Nothing is applied to the
    /// game until it closes.
    /// </summary>
    public class BagScreen : MonoBehaviour
    {
        public static BagScreen Current { get; private set; }

        const float Cell = 64;
        const int Cols = 10, GridRows = 5;

        ManualBagModel _model;
        BagScreenOptions _o;
        string _title;
        Action<BagScreenResult> _onDone;

        RectTransform _win, _homePane, _homeList, _bagList, _goalList, _help, _gridArea, _pieces, _scoreBox, _confirm, _goalsHead;
        ScrollRect _homeScroll;
        Text _homeCount, _bagCount, _wText, _vText, _status, _why, _scoreHead, _timerText, _keys;
        Image _wBar, _vBar, _ghost, _cursor, _timerChip;
        Text _ghostText;
        Button _tidyBtn;

        // keyboard focus: 0 home list, 1 grid, 2 bag list, 3 the score (평가: why each line counts)
        int _focus, _sel, _cx, _cy, _scoreSel;
        BagScore _score;
        float _goalsTop = 510;
        int _shownSec = -1, _spokenMark = int.MaxValue;
        bool _keyWhy; // the keyboard chose what the why line shows (the mouse takes over when it moves)
        bool _helpOpen, _asked;
        float _openedAt;
        int _tidied;
        double _remaining, _elapsed;

        // what the pointer or keyboard is carrying
        BagPiece _held;            // a unit lifted from the grid
        string _heldNew;           // or a unit carried from home
        bool _heldRot;
        int _grabX, _grabY;        // which of the unit's cells the pointer holds
        bool _sticky;              // picked up by a click / key: put down by the next one
        string _pressId; Vector2 _pressAt; bool _pressHome, _pressBag, _dragging;
        BagPiece _pressPiece;

        readonly List<(RectTransform rt, string id)> _homeRows = new(), _bagRows = new();
        readonly List<(RectTransform rt, string text)> _hoverLines = new(); // also the score's rows for the keyboard (focus 3), in order
        readonly List<(RectTransform rt, BagPiece p)> _pieceRects = new();
        string _hoverShown;

        public bool IsOpen => _model != null;
        public string StatusText => _status ? _status.text : null;
        public ManualBagModel Model => _model;

        /// <summary>Opens the screen; onDone gets what was packed when it closes.</summary>
        public static BagScreen Open(ManualBagModel model, BagScreenOptions options, Action<BagScreenResult> onDone)
        {
            if (Current) Current.Close();
            var canvas = UiKit.ScreenCanvas("BagScreen", 200);
            var s = canvas.gameObject.AddComponent<BagScreen>();
            s._model = model; s._o = options ?? new BagScreenOptions(); s._onDone = onDone;
            s._o.Goals ??= Array.Empty<BagGoal>(); s._o.Tips ??= Array.Empty<string>();
            s._title = BagDrill.Titles.TryGetValue(s._o.Context ?? "", out var t) ? t : "가방 꾸리기";
            if (s._o.TimerSeconds > 0) s._title = "급히 다시 싸기";
            s._helpOpen = s._o.Mode == "guided";
            s._remaining = s._o.TimerSeconds;
            Current = s;
            InputGate.Hold();
            UiScreen.PushModal(); // counts as a modal like the UiScreens: the HUD fades under it and toasts wait
            s._counted = true;
            PlayerInputReader.HoldPointer();
            s._pointer = true;
            s.Build();
            s.Render();
            s._openedAt = Time.unscaledTime;
            if (s._o.TimerSeconds > 0) s.Say("사이렌이 울린다. 시간 안에 챙긴 것만 메고 나간다.");
            return s;
        }

        /// <summary>Closes and hands the result over ('다 챙겼다' and Esc both keep what was packed, like the original).</summary>
        public void Close() => Close(false);

        void Close(bool timedOut)
        {
            if (_model == null) return;
            CancelHeld();
            var result = new BagScreenResult
            {
                Bag = _model.Result(), Layout = _model.Grid.Encode(), Aids = _model.AidsPacked(), Score = _model.Score(_o.LimitKg),
                Tidied = _tidied, Elapsed = _elapsed, TimedOut = timedOut,
            };
            _model = null;
            if (Current == this) Current = null;
            InputGate.Release();
            Uncount();
            Unpoint();
            Destroy(gameObject);
            _onDone?.Invoke(result);
        }

        void OnDestroy()
        {
            if (_model != null) { _model = null; InputGate.Release(); }
            Uncount();
            Unpoint();
            if (Current == this) Current = null;
        }

        bool _counted, _pointer;
        void Unpoint() { if (!_pointer) return; _pointer = false; PlayerInputReader.ReleasePointer(); }
        void Uncount() { if (!_counted) return; _counted = false; UiScreen.PopModal(); }

        // ---------- layout ----------

        void Build()
        {
            var root = (RectTransform)transform;
            UiKit.Fill(UiKit.Box("Shade", root, UiKit.ModalShade, raycast: true).rectTransform);
            var win = UiKit.Box("Window", root, UiKit.Panel).rectTransform;
            win.anchorMin = win.anchorMax = win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = new Vector2(1840, 1000);
            _win = win;

            UiKit.At(UiKit.Label("Kicker", win, "가방", 22, UiKit.Dim).rectTransform, 40, 22, 300, 30);
            UiKit.At(UiKit.Label("Title", win, _title, 40, UiKit.Ink, style: FontStyle.Bold).rectTransform, 40, 48, 900, 56);
            var stage = _o.Mode == "guided" ? "안내" : _o.Mode == "hint" ? "힌트" : "자유";
            // a plain label, not a chip: it is not a button
            UiKit.At(UiKit.Label("Stage", win, $"단계 · {stage}", 20, UiKit.Dim, TextAnchor.MiddleRight).rectTransform, 1670, 38, 130, 34);
            if (_o.TimerSeconds > 0)
            {
                _timerChip = UiKit.Box("Timer", win, new Color(0.95f, 0.7f, 0.24f, 0.2f));
                UiKit.At(_timerChip.rectTransform, 1380, 28, 280, 52);
                _timerText = UiKit.Label("Text", _timerChip.transform, "", 28, UiKit.Amber, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Fill(_timerText.rectTransform);
            }

            // Home pane
            _homePane = UiKit.Rect("HomePane", win); UiKit.At(_homePane, 40, 120, 460, 740);
            _homeCount = UiKit.Label("Head", _homePane, "", 26, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_homeCount.rectTransform, 0, 0, 460, 40);
            var homeBody = UiKit.Rect("Body", _homePane); UiKit.At(homeBody, 0, 46, 460, 694);
            _homeList = UiKit.ScrollList("List", homeBody);
            _homeScroll = _homeList.GetComponentInParent<ScrollRect>();

            // Bag pane: the grid, then what is in it as a list
            var bag = UiKit.Rect("BagPane", win); UiKit.At(bag, 520, 120, 640, 740);
            _bagCount = UiKit.Label("Head", bag, "", 26, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_bagCount.rectTransform, 0, 0, 640, 40);
            var frame = UiKit.Box("GridFrame", bag, new Color(0, 0, 0, 0.3f)).rectTransform;
            UiKit.At(frame, 0, 46, 640, GridRows * Cell);
            _gridArea = UiKit.Rect("Grid", frame); UiKit.At(_gridArea, 0, 0, Cols * Cell, GridRows * Cell);
            for (int y = 0; y < GridRows; y++)
                for (int x = 0; x < Cols; x++)
                    UiKit.At(UiKit.Box("Cell", _gridArea, new Color(1, 1, 1, 0.05f)).rectTransform, x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2);
            UiKit.At(UiKit.Label("Top", frame, "위", 14, new Color(0.6f, 0.65f, 0.68f, 0.7f), TextAnchor.UpperRight).rectTransform, 560, -20, 76, 18);
            _pieces = UiKit.Rect("Pieces", _gridArea); UiKit.Fill(_pieces);
            _cursor = UiKit.Box("Cursor", _gridArea, new Color(1, 1, 1, 0));
            var ol = _cursor.gameObject.AddComponent<Outline>(); ol.effectColor = UiKit.Amber; ol.effectDistance = new Vector2(3, -3); ol.useGraphicAlpha = false;
            _ghost = UiKit.Box("Ghost", _gridArea, new Color(0.24f, 0.78f, 0.71f, 0.45f));
            _ghostText = UiKit.Label("Name", _ghost.transform, "", 15, new Color(0.05f, 0.07f, 0.08f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Fill(_ghostText.rectTransform, 2, 2);
            _ghost.gameObject.SetActive(false);
            _cursor.gameObject.SetActive(false);
            var gridKeys = UiKit.Label("GridKeys", bag, "끌어서 옮기기 · 클릭으로 들고 놓기 · R·우클릭 돌리기 · 집 목록으로 끌면 꺼내기", 17, UiKit.Dim);
            FitOneLine(gridKeys);
            UiKit.At(gridKeys.rectTransform, 0, 46 + GridRows * Cell + 6, 640, 26);
            var bagBody = UiKit.Rect("Body", bag); UiKit.At(bagBody, 0, 46 + GridRows * Cell + 38, 640, 740 - (46 + GridRows * Cell + 38));
            _bagList = UiKit.ScrollList("List", bagBody, 4);

            // Side: meters, the score, goals, help
            var side = UiKit.Rect("Side", win); UiKit.At(side, 1180, 120, 620, 740);
            (_wText, _wBar) = Meter(side, 0, "무게");
            (_vText, _vBar) = Meter(side, 76, "부피");
            _scoreHead = UiKit.Label("ScoreHead", side, "", 24, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_scoreHead.rectTransform, 0, 152, 620, 34);
            _scoreBox = UiKit.Rect("Score", side); UiKit.At(_scoreBox, 0, 190, 620, 230);
            _why = UiKit.Label("Why", side, "", 16, new Color(0.73f, 0.78f, 0.8f));
            // long reasons shrink to fit instead of running into the goals (the box is three lines at any text size)
            _why.resizeTextForBestFit = true; _why.resizeTextMinSize = 11; _why.resizeTextMaxSize = _why.fontSize;
            _why.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.At(_why.rectTransform, 0, 424, 620, 48);
            if (_o.Goals.Count > 0)
            {
                _goalsHead = UiKit.Label("GoalsHead", side, "챙길 것", 22, UiKit.Ink, style: FontStyle.Bold).rectTransform;
                UiKit.At(_goalsHead, 0, 478, 620, 30);
            }
            _goalList = UiKit.Rect("Goals", side); UiKit.At(_goalList, 0, 510, 620, 160);
            _help = UiKit.Rect("Help", side); UiKit.At(_help, 0, 676, 620, 64);

            // Footer
            _status = UiKit.Label("Status", win, "", 22, UiKit.Teal);
            UiKit.At(_status.rectTransform, 40, 876, 1120, 32);
            var keys = "클릭: 한 개 넣기·꺼내기 (Shift 전부) · Space 들고 가서 놓기 · Enter 넣기/들기/놓기 · R 돌리기 · Del 꺼내기 · Tab 목록·가방·평가 · T 정리 · Esc 닫기";
            if (Gamepad.current != null) keys += "\n게임패드  십자키 이동 · A 넣기/들기/놓기 · Y 돌리기 · X 정리 · LB/RB 목록·가방·평가 · B 들기 취소 · Start 다 챙겼다";
            _keys = UiKit.Label("Keys", win, keys, 17, UiKit.Dim);
            UiKit.At(_keys.rectTransform, 40, 914, 1120, 60);
            _tidyBtn = UiKit.Button("Tidy", win, TidyLabel(), 22, new Color(0.24f, 0.78f, 0.71f, 0.22f), UiKit.Ink);
            UiKit.At((RectTransform)_tidyBtn.transform, 1180, 880, 200, 64);
            _tidyBtn.onClick.AddListener(Tidy);
            var empty = UiKit.Button("Empty", win, "가방 비우기", 22, new Color(1, 1, 1, 0.08f), UiKit.Ink);
            UiKit.At((RectTransform)empty.transform, 1394, 880, 170, 64);
            empty.onClick.AddListener(() => { CancelHeld(); _model.Empty(); Say("가방을 비웠다."); Render(); });
            var done = UiKit.Button("Done", win, "다 챙겼다", 26, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            UiKit.At((RectTransform)done.transform, 1578, 880, 222, 64);
            done.onClick.AddListener(TryClose);
            // a larger 글자 크기 shrinks these to one line instead of spilling out of their boxes
            foreach (var b in new[] { _tidyBtn, empty, done }) FitOneLine(b.GetComponentInChildren<Text>());
        }

        static void FitOneLine(Text t)
        {
            if (!t) return;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = t.fontSize;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        string TidyLabel() =>
            _o.TimerSeconds > 0 ? $"정리 (T) −{_o.TidySeconds:0}초" : string.IsNullOrEmpty(_o.TidyCost) ? "정리 (T)" : $"정리 (T) · {_o.TidyCost}";

        /// <summary>다 챙겼다 / Esc: once per screen, ask before leaving with a critical goal (ID, water) still open.</summary>
        void TryClose()
        {
            if (_confirm) return;
            CancelHeld();
            var open = _asked ? null : BagCheck.CriticalOpenAll(_o.Goals, _o.Mode, _model.BagOf, _model.Items);
            if (open == null || open.Count == 0) { Close(); return; }
            _asked = true;
            ShowConfirm(open);
        }

        void ShowConfirm(List<BagGoal> critical)
        {
            _confirm = UiKit.Box("Confirm", _win, new Color(0, 0, 0, ScreenDecor.A(0.6f)), raycast: true).rectTransform;
            UiKit.Fill(_confirm);
            var box = ScreenDecor.Rounded("Box", _confirm, new Color(0.09f, 0.13f, 0.14f, 0.99f)).rectTransform;
            var panel = new Color(0.09f, 0.13f, 0.14f, 1);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(600, 226);
            var q = UiKit.Label("Q", box, BagCheck.Question(critical), 30, UiKit.Ink, style: FontStyle.Bold);
            q.resizeTextForBestFit = true; q.resizeTextMinSize = 24; q.resizeTextMaxSize = 30;
            UiKit.At(q.rectTransform, 36, 32, 528, 42);
            // the other gaps (the ones the question names are not repeated)
            var named = new HashSet<string>(critical.Select(c => c.Label));
            var open = BagCheck.OpenLabels(_o.Goals, _model.BagOf, _model.Items).Where(l => !named.Contains(l)).ToList();
            UiKit.At(UiKit.Label("Open", box, BagCheck.OpenSummary(open), 20, UiKit.Dim).rectTransform, 36, 86, 528, 30);
            var back = UiKit.Button("Back", box, "다시 보기", 24, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            UiKit.At((RectTransform)back.transform, 36, 138, 256, 56);
            back.onClick.AddListener(HideConfirm);
            var go = UiKit.Button("Go", box, "그대로 나가기", 24, ScreenDecor.Over(panel, Color.white, 0.08f), UiKit.Ink);
            UiKit.At((RectTransform)go.transform, 308, 138, 256, 56);
            go.onClick.AddListener(Close);
        }

        void HideConfirm()
        {
            if (!_confirm) return;
            var c = _confirm.gameObject; _confirm = null;
            c.transform.SetParent(null, false); // detached first: Destroy is deferred
            Destroy(c);
        }

        (Text, Image) Meter(RectTransform side, float y, string label)
        {
            UiKit.At(UiKit.Label(label, side, label, 20, UiKit.Dim).rectTransform, 0, y, 120, 28);
            var text = UiKit.Label(label + "Text", side, "", 22, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(text.rectTransform, 80, y - 2, 540, 30);
            var track = UiKit.Box(label + "Track", side, new Color(1, 1, 1, 0.08f)).rectTransform;
            UiKit.At(track, 0, y + 36, 620, 16);
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
            _bagCount.text = $"가방  <color=#9aa6ad>{bag.Sum(i => _model.BagOf(i.Id))}개 · 칸 {_model.Grid.Used} / {Cols * GridRows}</color>";
            if (_focus == 0 && home.Count == 0 && _model.Grid.Pieces.Count > 0) _focus = 1;
            var list = _focus == 2 ? bag : home;
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, list.Count - 1));

            Rows(_homeList, home, false, "집에 남은 물건이 없다.", _homeRows);
            Rows(_bagList, bag, true, "아직 비어 있다.", _bagRows);
            Grid();

            var (w, v) = _model.Load();
            SetMeter(_wText, _wBar, w, _o.LimitKg, BagDrill.FormatKg(w), BagDrill.FormatKg(_o.LimitKg), "걸음이 느려짐");
            SetMeter(_vText, _vBar, v, _o.LimitL, BagDrill.FormatL(v), $"{_o.LimitL:0}L", null);
            Score();

            Clear(_goalList);
            var goals = BagDrill.GoalStatus(_o.Goals, _o.Mode, _model.BagOf, _model.Items).ToList();
            // more than five goals (the evacuation bag has ten) go in two columns, so the help keeps its room
            bool two = goals.Count > 5;
            const float rowH = 26;
            int perCol = two ? (goals.Count + 1) / 2 : goals.Count;
            float colW = two ? 310 : 620;
            for (int gi = 0; gi < goals.Count; gi++)
            {
                var (label, ok) = goals[gi];
                string mark = ok == null ? "·" : ok.Value ? "<color=#3cc6b5>✓</color>" : "<color=#9aa6ad>○</color>";
                var why = ok == false && gi < _o.Goals.Count ? BagCheck.GoalNote(_o.Goals[gi], _model.BagOf, _model.Items) : null;
                var tag = why != null ? $" <size=14><color=#f2b33d>({why})</color></size>" : "";
                var t = UiKit.Label("Goal", _goalList, $"{mark}  {label}{tag}", 19, ok == true ? UiKit.Ink : UiKit.Dim);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 19;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.At(t.rectTransform, gi / perCol * colW, gi % perCol * rowH, colW - 8, rowH - 2);
            }
            float listH = Mathf.Max(60, perCol * rowH);
            UiKit.At(_goalList, 0, _goalsTop, 620, listH);
            float helpY = _goalsTop + listH + 6;
            UiKit.At(_help, 0, helpY, 620, Mathf.Max(44, 740 - helpY));
            Help();
        }

        void SetMeter(Text text, Image bar, double val, double max, string valText, string maxText, string overWord)
        {
            var state = BagDrill.MeterState(val, max);
            text.text = $"{valText} / {maxText} · {(state == "over" && overWord != null ? $"초과 — {overWord}" : BagDrill.MeterWord(state))}";
            bar.rectTransform.anchorMax = new Vector2((float)Math.Min(1, val / max), 1);
            bar.color = state == "over" ? UiKit.Red : state == "warn" ? UiKit.Amber : UiKit.Teal;
        }

        void Rows(RectTransform list, List<BagItemView> items, bool inBag, string emptyText, List<(RectTransform, string)> refs)
        {
            Clear(list);
            refs.Clear();
            if (items.Count == 0)
            {
                UiKit.Height(UiKit.Label("Empty", list, emptyText, 20, UiKit.Dim, TextAnchor.MiddleCenter), 52);
                return;
            }
            bool free = _o.Mode == "free";
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool selected = (inBag ? _focus == 2 : _focus == 0) && i == _sel;
                int n = inBag ? _model.BagOf(it.Id) : _model.HomeOf(it.Id);
                var row = UiKit.Box("Row_" + it.Id, list, selected ? UiKit.RowSelected : UiKit.Row, raycast: true);
                refs.Add((row.rectTransform, it.Id));

                var note = BagDrill.ItemNote(it);
                var size = _model.SizeOf(it.Id);
                var spec = $"{BagDrill.FormatKg(it.WeightKg)}" + (size.HasValue ? $" · {size.Value.w}×{size.Value.h}칸" : "") + (note != "" && note != "기한 지남" ? $" · ({note})" : "");
                var badges = string.Join(" ", BagDrill.ShownTags(it).Select(t => BagDrill.WarnTags.Contains(t)
                    ? $"<color=#f2b33d>! {BagDrill.TagName(t)}</color>" : $"<color=#7fb8c4>{BagDrill.TagName(t)}</color>"));
                var pri = BagRules.PriorityOf(it.Id);
                if (pri != null) badges = $"<color=#3cc6b5>★ {pri.Label}</color> " + badges;
                if (BagAids.IsAid(it.Id)) badges = "<color=#b9a3e8>◆ 체험 칸</color> " + badges;
                if (!it.Ready && !free) badges += $" <color=#f2b33d>! {BagCheck.NotReady}</color>";
                bool showTip = !inBag && !string.IsNullOrEmpty(it.Tip) && !free;
                var text = UiKit.Label("Text", row.transform,
                    $"{(inBag ? "←  " : "")}<b>{it.Name}</b> <color=#9aa6ad>×{n}</color>{(inBag ? "" : "  →")}\n<size=16><color=#9aa6ad>{spec}</color>   {badges}</size>" +
                    (showTip ? $"\n<size=15><color=#b9c6cc>{it.Tip}</color></size>" : ""), inBag ? 19 : 21, UiKit.Ink);
                var fit = row.gameObject.AddComponent<VerticalLayoutGroup>();
                fit.padding = inBag ? new RectOffset(12, 12, 4, 6) : new RectOffset(14, 14, 8, 10);
                var rowIcon = BagIcons.For(it.Id);
                if (rowIcon)
                {
                    // the item's picture at the row's left, the text moved over for it
                    var ic = UiKit.Box("Icon", row.transform, Color.white);
                    ic.sprite = rowIcon; ic.preserveAspect = true;
                    ic.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    var irt = ic.rectTransform;
                    irt.anchorMin = irt.anchorMax = new Vector2(0, 1); irt.pivot = new Vector2(0, 1);
                    float sz = inBag ? 30 : 38;
                    irt.sizeDelta = new Vector2(sz, sz); irt.anchoredPosition = new Vector2(10, inBag ? -6 : -10);
                    fit.padding.left += (int)sz + 10;
                }
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
            ["aid"] = new Color(0.72f, 0.62f, 0.92f),
        };

        Color ColorOf(string id)
        {
            var it = _model.View(id);
            return it != null && it.Tags.Count > 0 && CategoryColor.TryGetValue(it.Tags[0], out var c) ? c : UiKit.Dim;
        }

        /// <summary>The label on a piece: short enough for one 64 px cell.</summary>
        static readonly Dictionary<string, string> Short = new()
        {
            ["bars"] = "건빵", ["old_bars"] = "옛 건빵", ["batteries_aa"] = "건전지", ["batteries_old"] = "헌 건전지", ["meds"] = "복용약",
            ["first_aid"] = "구급함", ["doc_copies"] = "서류 사본", ["contact_card"] = "연락처 카드", ["id_card"] = "신분증", ["cash"] = "현금",
            ["powerbank"] = "보조 배터리", ["water_500"] = "생수 500", ["canned"] = "통조림", ["clothes"] = "여벌 옷", ["fridge_food"] = "반찬",
            ["cooked_meat"] = "익힌 고기", ["gas_stove"] = "가스레인지", ["butane"] = "부탄가스", ["aid_hearing_batt"] = "보청기 배터리",
            ["aid_chair_batt"] = "휠체어 배터리", ["aid_memo"] = "필담 메모장", ["aid_glasses"] = "여분 안경", ["water_jug"] = "물통",
        };

        static string ShortName(string id, string name)
        {
            if (id != null && Short.TryGetValue(id, out var s0)) return s0;
            var s = name ?? id ?? "";
            int cut = s.IndexOf('(');
            if (cut > 0) s = s.Substring(0, cut);
            return s.Trim();
        }

        /// <summary>"생수 2L을", "라디오를": the name without its bracket, with 을/를 by its last syllable.</summary>
        static string Obj(string name)
        {
            var s = ShortName(null, name);
            char c = s.Length > 0 ? s[s.Length - 1] : ' ';
            bool batchim = c >= 0xAC00 && c <= 0xD7A3 ? (c - 0xAC00) % 28 != 0 : char.IsDigit(c) || "LlMmNn".IndexOf(c) >= 0;
            return s + (batchim ? "을" : "를");
        }

        void Grid()
        {
            Clear(_pieces);
            _pieceRects.Clear();
            var waters = _model.Grid.Pieces.Where(p => BagRules.Water.Contains(p.Id)).ToList();
            var radios = _model.Ready("radio") ? _model.Grid.Pieces.Where(p => p.Id == "radio").ToList() : new List<BagPiece>();
            foreach (var p in _model.Grid.Pieces)
            {
                var col = ColorOf(p.Id);
                bool lifted = p == _held;
                var piece = UiKit.Box("Piece_" + p.Id, _pieces, new Color(col.r, col.g, col.b, lifted ? 0.25f : 0.88f));
                piece.sprite = HudDraw.RoundedSprite; piece.type = Image.Type.Sliced;
                UiKit.At(piece.rectTransform, p.X * Cell + 3, p.Y * Cell + 3, p.W * Cell - 6, p.H * Cell - 6);
                _pieceRects.Add((piece.rectTransform, p));
                bool wet = BagRules.Papers.Contains(p.Id) && waters.Any(w => BagGrid.RightAbove(w, p));
                bool linked = (radios.Contains(p) && _model.Grid.Pieces.Any(b => BagRules.Batteries.Contains(b.Id) && BagGrid.Touching(p, b)))
                              || (BagRules.Batteries.Contains(p.Id) && radios.Any(r => BagGrid.Touching(r, p)));
                if (wet || linked)
                {
                    var o = piece.gameObject.AddComponent<Outline>();
                    o.effectColor = wet ? UiKit.Amber : UiKit.Teal; o.effectDistance = new Vector2(3, -3);
                }
                var label = ShortName(p.Id, _model.View(p.Id)?.Name) + (wet ? "\n<color=#7a3d00>젖음!</color>" : "");
                var t = UiKit.Label("Name", piece.transform, label, 15, new Color(0.05f, 0.07f, 0.08f), TextAnchor.MiddleCenter, FontStyle.Bold);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 16;
                UiKit.Fill(t.rectTransform, 3, 2);
                var pic = BagIcons.For(p.Id);
                if (pic)
                {
                    // the item's picture: at the left of a wide piece, above the name in a tall one; a one-cell piece shows
                    // it whole with the name on a solid strip along its bottom (dark text over the picture was unreadable)
                    float pw = p.W * Cell - 6, ph = p.H * Cell - 6;
                    bool small = p.W == 1 && p.H == 1;
                    var ic = UiKit.Box("Icon", piece.transform, new Color(1, 1, 1, lifted ? 0.2f : 1f));
                    ic.sprite = pic; ic.preserveAspect = true;
                    ic.rectTransform.SetAsFirstSibling();
                    if (small)
                    {
                        const float strip = 17;
                        float sz = Mathf.Min(pw - 8, ph - strip - 4);
                        UiKit.At(ic.rectTransform, (pw - sz) / 2, 3, sz, sz);
                        var bar = UiKit.Box("NameStrip", piece.transform, new Color(0.04f, 0.06f, 0.07f, lifted ? 0.3f : 0.82f));
                        UiKit.At(bar.rectTransform, 0, ph - strip, pw, strip);
                        t.transform.SetParent(bar.transform, false);
                        UiKit.Fill(t.rectTransform, 2, 0);
                        t.color = new Color(0.95f, 0.96f, 0.94f);
                        t.text = ShortName(p.Id, _model.View(p.Id)?.Name) + (wet ? " <color=#f2b33d>젖음</color>" : "");
                        t.resizeTextMinSize = 8; t.resizeTextMaxSize = 12;
                    }
                    else if (p.H == 1 && p.W >= 2)
                    {
                        float sz = ph - 12;
                        UiKit.At(ic.rectTransform, 6, 6, sz, sz);
                        UiKit.At(t.rectTransform, sz + 8, 2, pw - sz - 11, ph - 4);
                    }
                    else
                    {
                        float sz = p.H == 1 ? ph * 0.5f : Mathf.Min(pw - 12, ph * 0.62f - 8);
                        UiKit.At(ic.rectTransform, (pw - sz) / 2, 4, sz, sz);
                        UiKit.At(t.rectTransform, 2, sz + 4, pw - 4, ph - sz - 6);
                        if (p.H == 1) { t.resizeTextMinSize = 9; t.resizeTextMaxSize = 13; }
                    }
                }
                if (BagRules.PriorityOf(p.Id) != null)
                    UiKit.At(UiKit.Label("Star", piece.transform, "★", 12, new Color(0.05f, 0.07f, 0.08f, 0.8f), TextAnchor.UpperLeft).rectTransform, 3, 1, 16, 16);
            }
        }

        void Score()
        {
            Clear(_scoreBox);
            _hoverLines.Clear();
            var s = _score = _model.Score(_o.LimitKg);
            bool free = _o.Mode == "free";
            _scoreHead.text = $"꾸림 평가  <color=#3cc6b5>{s.Total}</color><color=#9aa6ad> / {s.Max}점</color>";
            _scoreSel = Mathf.Clamp(_scoreSel, 0, Mathf.Max(0, s.Lines.Count - 1));
            int index = 0;
            // the official list: one chip per tag, sized to its text (the 글자 크기 setting can make it 1.5x)
            var pri = s.Lines.Where(l => l.Kind == "priority").ToList();
            float x = 0, y = 0, chipH = 32;
            foreach (var l in pri)
            {
                bool sel = _focus == 3 && index == _scoreSel;
                var tag = BagRules.Priorities.FirstOrDefault(p => p.Id == l.Id);
                bool unready = !l.Ok && tag != null && tag.Items.Concat(tag.Also ?? Array.Empty<string>()).Any(id => _model.BagOf(id) > 0 && !_model.Ready(id));
                var chip = UiKit.Box("Chip_" + l.Id, _scoreBox, sel ? UiKit.RowSelected : l.Ok ? new Color(0.24f, 0.78f, 0.71f, 0.22f) : new Color(1, 1, 1, 0.06f));
                var t = UiKit.Label("T", chip.transform, (l.Ok ? "✓ " : "○ ") + l.Label + (unready ? " <color=#f2b33d>!</color>" : ""), 17, l.Ok ? UiKit.Ink : UiKit.Dim, TextAnchor.MiddleCenter);
                float w = Mathf.Ceil(t.preferredWidth) + 24;
                chipH = Mathf.Max(32, Mathf.Ceil(t.fontSize * 1.4f) + 8);
                if (x > 0 && x + w > 620) { x = 0; y += chipH + 6; }
                UiKit.At(chip.rectTransform, x, y, w, chipH);
                UiKit.Fill(t.rectTransform);
                if (sel) AddSelOutline(chip.gameObject);
                _hoverLines.Add((chip.rectTransform, (unready ? $"<color=#f2b33d>({BagCheck.NotReady})</color> " : "") + Explain(l)));
                x += w + 6;
                index++;
            }
            y += chipH + 10;
            foreach (var l in s.Lines.Where(l => l.Kind != "priority"))
            {
                bool sel = _focus == 3 && index == _scoreSel;
                string mark = l.Points < 0 ? "<color=#f2b33d>!</color>" : l.Ok ? "<color=#3cc6b5>✓</color>" : "<color=#9aa6ad>○</color>";
                string pts = l.Points > 0 ? $"+{l.Points}" : l.Points < 0 ? $"{l.Points}" : "";
                string tag = l.Hint ? $"  <size=13><color=#9aa6ad>[{(l.Kind == "weight" ? "게임 규칙" : "게임 힌트")}]</color></size>" : l.Kind == "aid" ? "  <size=13><color=#b9a3e8>[체험 칸]</color></size>" : "";
                string detail = !free && !string.IsNullOrEmpty(l.Detail) ? $"  <size=15><color=#9aa6ad>{l.Detail}</color></size>" : "";
                Image back = sel ? UiKit.Box("Sel", _scoreBox, UiKit.RowSelected) : null;
                var t = UiKit.Label("Line_" + l.Id, _scoreBox, $"{mark}  {l.Label}{detail}{tag}  <color=#3cc6b5>{pts}</color>", 18,
                    l.Ok ? UiKit.Ink : l.Points < 0 ? UiKit.Amber : UiKit.Dim);
                UiKit.At(t.rectTransform, 0, y, 620, 26);
                float h = Mathf.Max(26, Mathf.Ceil(t.preferredHeight)); // wraps at a larger text size
                UiKit.At(t.rectTransform, 0, y, 620, h);
                if (back) { UiKit.At(back.rectTransform, -6, y - 1, 632, h + 2); AddSelOutline(back.gameObject); }
                _hoverLines.Add((t.rectTransform, Explain(l)));
                y += h + 1;
                index++;
            }
            UiKit.At(_scoreBox, 0, 190, 620, y);
            // the why line under the score, three lines tall at any text size; the goals follow it
            float whyY = 190 + y + 4, whyH = Mathf.Ceil(_why.fontSize * 1.25f * 3) + 4;
            UiKit.At(_why.rectTransform, 0, whyY, 620, whyH);
            float headY = whyY + whyH + 6;
            if (_goalsHead)
            {
                int headSize = _goalsHead.GetComponent<Text>().fontSize;
                UiKit.At(_goalsHead, 0, headY, 620, Mathf.Max(30, headSize + 8));
                _goalsTop = headY + Mathf.Max(32, headSize + 10);
            }
            else _goalsTop = headY;
            _hoverShown = null;
            ShowWhy(KeyWhyText());
        }

        static void AddSelOutline(GameObject go)
        {
            var o = go.AddComponent<Outline>(); o.effectColor = UiKit.Amber; o.effectDistance = new Vector2(2, -2); o.useGraphicAlpha = false;
        }

        string Explain(BagScoreLine l)
        {
            if (l.Fact == null)
                return l.Kind == "weight"
                    ? $"{l.Label}: 공식 지침에 없는 게임 규칙입니다. 무게 한도 {BagRules.KgShort(_o.LimitKg)}은 이 게임의 수치이고, 넘으면 걸음이 느려지고 남을 돕기 어렵습니다."
                    : $"{l.Label}: 공식 지침에 없는 게임 힌트입니다. " + (l.Kind == "wet" ? "물병이 새면 바로 아래 서류부터 젖습니다." : "라디오 건전지가 다 되면 바로 갈아 끼울 수 있게 붙여 둡니다.");
            var c = _o.Cite?.Invoke(l.Fact);
            return c == null ? l.Label : $"{l.Label} — {c.Text}  <color=#9aa6ad>({c.Source})</color>";
        }

        const string DefaultWhy = "★ 공식 목록(서울특별시·국민안전24 안내)과 체험 칸은 출처가 있는 안내이고, <color=#9aa6ad>[게임 힌트]·[게임 규칙]</color>은 이 게임이 정한 것입니다. 항목에 마우스를 올리거나 Tab으로 고르면 근거가 보입니다.";

        void ShowWhy(string text)
        {
            if (text == _hoverShown && text != null) return;
            _hoverShown = text;
            _why.text = text ?? DefaultWhy;
        }

        /// <summary>An item's name, weight and size, then why it counts (its official-list or 체험 칸 line) or its tip.</summary>
        string ItemWhy(string id, BagPiece p = null)
        {
            var v = _model.View(id);
            var size = p != null ? (p.W, p.H) : _model.SizeOf(id) ?? (0, 0);
            var head = $"{v?.Name ?? id} · {BagDrill.FormatKg(v?.WeightKg ?? 0)}" + (size.Item1 > 0 ? $" · {size.Item1}×{size.Item2}칸" : "")
                       + (v != null && !v.Ready ? $" · <color=#f2b33d>{BagCheck.NotReady}</color>" : "");
            var pri = BagRules.PriorityOf(id);
            var line = pri != null ? _score?.Line(pri.Id) : BagAids.IsAid(id) ? _score?.Line(id) : null;
            var why = line != null ? Explain(line) : _o.Mode != "free" && !string.IsNullOrEmpty(v?.Tip) ? v.Tip : "";
            return why == "" ? head : head + "\n" + why;
        }

        /// <summary>What the keyboard's choice explains (focused row, cursor piece or score line), or null.</summary>
        string KeyWhyText()
        {
            if (!_keyWhy || _model == null) return null;
            switch (_focus)
            {
                case 1:
                    var p = _model.Grid.At(_cx, _cy);
                    return p != null ? ItemWhy(p.Id, p) : null;
                case 3:
                    return _scoreSel < _hoverLines.Count ? _hoverLines[_scoreSel].text : null;
                default:
                    var it = (_focus == 2 ? _model.InBag : _model.AtHome).Skip(_sel).FirstOrDefault();
                    return it != null ? ItemWhy(it.Id) : null;
            }
        }

        static readonly System.Text.RegularExpressions.Regex Tags = new("<[^>]+>");

        /// <summary>저시력: reads out what the keyboard just chose.</summary>
        void SpeakWhy()
        {
            if (_o.Profile != "lowvision") return;
            var t = KeyWhyText();
            if (!string.IsNullOrEmpty(t)) Speak(Tags.Replace(t, "").Replace("\n", ". "));
        }

        void Help()
        {
            Clear(_help);
            if (_o.Mode == "free") return;
            float y = 0;
            if (_o.Mode == "hint")
            {
                var b = UiKit.Button("HintToggle", _help, _helpOpen ? "힌트 닫기" : "힌트 보기", 18, new Color(1, 1, 1, 0.08f), UiKit.Ink);
                UiKit.At((RectTransform)b.transform, 0, 0, 150, 36);
                b.onClick.AddListener(() => { _helpOpen = !_helpOpen; Help(); });
                y = 42;
            }
            if (!_helpOpen) return;
            var lines = new List<string> { "★ 공식 목록부터 채우고, 무게 막대가 넘치지 않게. 자리가 없으면 옮기거나 '정리'." };
            lines.AddRange(_o.Tips.Select(t => "· " + t));
            var box = UiKit.Rect("Scroll", _help);
            UiKit.At(box, 0, y, 620, Mathf.Max(36, _help.rect.height - y));
            var content = UiKit.ScrollList("Tips", box, 2);
            UiKit.Label("Text", content, string.Join("\n", lines), 16, new Color(0.73f, 0.78f, 0.8f));
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
            t.DetachChildren();
        }

        // ---------- actions ----------

        /// <summary>Moves units of id: fromHome puts them in at the first free spot, else takes the last ones out.</summary>
        public int Move(string id, bool fromHome, int n = 1)
        {
            var it = _model.View(id);
            if (it == null) return 0;
            int k = fromHome ? _model.PutIn(id, n) : _model.TakeOut(id, n);
            if (k == 0)
            {
                if (fromHome) SayFail(it.Name, _model.LastFail);
                return 0;
            }
            var (w, _) = _model.Load();
            Say($"{it.Name} {k}개를 {(fromHome ? "가방에 넣었다" : "집에 두었다")}. 가방 {BagDrill.FormatKg(w)}");
            UiSound.Play(fromHome ? "click" : "tick");
            Render();
            return k;
        }

        void SayFail(string name, string why) => Say(why switch
        {
            "room" => $"{name}: 빈자리가 없다. 다른 것을 옮기거나 '정리'하면 들어간다.",
            "full" => $"{name}: 가방에 더 들어가지 않는다. 다른 것을 꺼내면 자리가 생긴다.",
            "spot" => $"{name}: 거기엔 들어가지 않는다.",
            _ => $"{name}: 더 넣을 것이 없다.",
        });

        public void Tidy()
        {
            if (_model == null) return;
            CancelHeld();
            // only a tidy that moved things costs time (an empty bag, or a hand packing tighter than the rule, costs nothing)
            if (_model.Grid.Pieces.Count == 0) { Say("가방이 비어 있다. 정리할 것이 없다."); return; }
            if (!_model.Tidy()) { Say("다시 넣어 보니 지금처럼은 들어가지 않는다. 그대로 두었다."); UiSound.Play("close"); return; }
            string cost = "";
            if (_o.TimerSeconds > 0)
            {
                // the timed screen's one cost: 10 s off the countdown, which is also time spent (the rush minutes)
                _remaining = Math.Max(0, _remaining - _o.TidySeconds);
                _elapsed += _o.TidySeconds;
                cost = $" (−{_o.TidySeconds:0}초)";
            }
            else if (!string.IsNullOrEmpty(_o.TidyCost)) cost = $" ({_o.TidyCost})";
            _tidied++;
            Say("큰 것부터 차곡차곡 다시 넣었다." + cost);
            UiSound.Play("tick");
            Render();
        }

        void Say(string text)
        {
            if (_status) _status.text = text;
            if (_o?.Profile == "lowvision") Speak(text);
        }

        AiOn.Settings.HostSettings _host;
        bool _hostLooked;
        void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_hostLooked) { _hostLooked = true; _host = FindFirstObjectByType<AiOn.Settings.HostSettings>(); }
            if (_host) _host.Speak(text);
        }

        static bool ShiftHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        // ---------- carrying ----------

        (int w, int h) HeldSize()
        {
            if (_held != null) return BagGrid.Turned(_held.BaseW, _held.BaseH, _heldRot);
            var s = _model.SizeOf(_heldNew) ?? (1, 1);
            return BagGrid.Turned(s.w, s.h, _heldRot);
        }

        bool Carrying => _held != null || _heldNew != null;

        void PickUp(BagPiece p, int cellX, int cellY, bool sticky)
        {
            _held = p; _heldNew = null; _heldRot = p.Rotated; _sticky = sticky;
            _grabX = Mathf.Clamp(cellX - p.X, 0, p.W - 1); _grabY = Mathf.Clamp(cellY - p.Y, 0, p.H - 1);
            Say($"{_model.View(p.Id)?.Name}: 놓을 곳을 고르세요. R 돌리기 · 집 목록에 놓으면 꺼내기");
            UiSound.Play("click");
            Grid();
        }

        void CarryNew(string id, bool sticky)
        {
            if (_model.HomeOf(id) <= 0) return;
            _held = null; _heldNew = id; _heldRot = false; _sticky = sticky; _grabX = _grabY = 0;
            Say($"{_model.View(id)?.Name}: 가방 칸에 놓으세요. R 돌리기 · Esc 취소");
            UiSound.Play("click");
        }

        void Rotate()
        {
            if (!Carrying) return;
            _heldRot = !_heldRot;
            var (w, h) = HeldSize();
            (_grabX, _grabY) = (Mathf.Clamp(_grabY, 0, w - 1), Mathf.Clamp(_grabX, 0, h - 1));
            UiSound.Play("click");
        }

        /// <summary>Puts what is carried down with its grabbed cell on (cx, cy). True if it went in.</summary>
        bool Drop(int cx, int cy)
        {
            var (w, h) = HeldSize();
            int x = Mathf.Clamp(cx - _grabX, 0, Cols - w), y = Mathf.Clamp(cy - _grabY, 0, GridRows - h);
            if (_held != null)
            {
                var p = _held;
                if (!_model.Move(p, x, y, _heldRot)) { Say($"{_model.View(p.Id)?.Name}: 거기엔 들어가지 않는다."); UiSound.Play("close"); return false; }
                _held = null;
                Say($"{Obj(_model.View(p.Id)?.Name)} 옮겼다.");
            }
            else
            {
                var id = _heldNew;
                if (_model.PutAt(id, x, y, _heldRot) == null) { SayFail(_model.View(id)?.Name, _model.LastFail); UiSound.Play("close"); return false; }
                _heldNew = null;
                var (kg, _) = _model.Load();
                Say($"{Obj(_model.View(id)?.Name)} 가방에 넣었다. 가방 {BagDrill.FormatKg(kg)}");
            }
            UiSound.Play("tick");
            _sticky = false;
            Render();
            return true;
        }

        void DropHome()
        {
            if (_held != null)
            {
                var p = _held; _held = null;
                _model.TakeOut(p);
                Say($"{Obj(_model.View(p.Id)?.Name)} 집에 두었다.");
                UiSound.Play("tick");
            }
            _heldNew = null; _sticky = false;
            Render();
        }

        void CancelHeld()
        {
            bool had = _held != null;
            _held = null; _heldNew = null; _sticky = false; _dragging = false; _pressId = null; _pressPiece = null;
            if (_homeScroll) _homeScroll.enabled = true;
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (had && _pieces) Grid();
        }

        bool CellUnder(Vector2 screen, out int cx, out int cy)
        {
            cx = cy = -1;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_gridArea, screen, null, out var local)) return false;
            float lx = local.x - _gridArea.rect.xMin, ly = _gridArea.rect.yMax - local.y;
            if (lx < 0 || ly < 0 || lx >= Cols * Cell || ly >= GridRows * Cell) return false;
            cx = (int)(lx / Cell); cy = (int)(ly / Cell);
            return true;
        }

        static string RowAt(List<(RectTransform rt, string id)> rows, RectTransform view, Vector2 screen)
        {
            if (view && !RectTransformUtility.RectangleContainsScreenPoint(view, screen, null)) return null;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].rt && RectTransformUtility.RectangleContainsScreenPoint(rows[i].rt, screen, null)) return rows[i].id;
            return null;
        }

        void ShowGhost(int cx, int cy, bool onGrid)
        {
            if (!Carrying || !onGrid) { _ghost.gameObject.SetActive(false); return; }
            var (w, h) = HeldSize();
            int x = Mathf.Clamp(cx - _grabX, 0, Cols - w), y = Mathf.Clamp(cy - _grabY, 0, GridRows - h);
            bool fits = _model.Grid.Fits(x, y, w, h, _held);
            _ghost.gameObject.SetActive(true);
            _ghost.color = fits ? new Color(0.24f, 0.78f, 0.71f, 0.55f) : new Color(0.9f, 0.28f, 0.3f, 0.5f);
            UiKit.At(_ghost.rectTransform, x * Cell + 2, y * Cell + 2, w * Cell - 4, h * Cell - 4);
            var id = _held != null ? _held.Id : _heldNew;
            if (_ghostText.text.Length == 0 || _ghostId != id) { _ghostId = id; _ghostText.text = ShortName(id, _model.View(id)?.Name); }
            _ghost.transform.SetAsLastSibling();
        }
        string _ghostId;

        // ---------- input ----------

        void Update()
        {
            if (_model == null) return;
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject) EventSystem.current.SetSelectedGameObject(null); // Enter / Space never press a focused button
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            if (_o.TimerSeconds > 0)
            {
                _remaining -= dt;
                int sec = Mathf.Max(0, Mathf.CeilToInt((float)_remaining));
                if (sec != _shownSec) { _shownSec = sec; _timerText.text = $"남은 시간 {sec / 60}:{sec % 60:00}"; } // a new string once a second
                if (_o.Profile == "lowvision")
                {
                    int mark = _remaining <= 10 ? 10 : _remaining <= 30 ? 30 : 0;
                    if (mark != 0 && mark < _spokenMark) { _spokenMark = mark; Speak($"남은 시간 {mark}초"); }
                }
                bool low = _remaining < 15;
                _timerText.color = low ? UiKit.Red : UiKit.Amber;
                if (low && !_o.ReduceMotion) _timerChip.color = new Color(0.9f, 0.28f, 0.3f, 0.15f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3)));
                if (_remaining <= 0) { HideConfirm(); Close(true); return; }
            }
            if (Time.unscaledTime - _openedAt < 0.2f) return; // the E that opened it
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (_confirm)
            {
                // the confirm is modal: Enter / Esc / A / B = 다시 보기 (the safe answer)
                if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) HideConfirm();
                else if (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame)) HideConfirm();
                return;
            }
            PointerInput();
            if (_model == null) return;
            KeyInput(kb, gp);
        }

        void PointerInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool onGrid = CellUnder(pos, out int cx, out int cy);
            bool onHome = RectTransformUtility.RectangleContainsScreenPoint(_homePane, pos, null);

            bool moved = (pos - _lastMouse).sqrMagnitude > 4;
            if (moved) { _keyCarry = false; _keyWhy = false; } // the mouse moved: it leads again
            _lastMouse = pos;
            if (Carrying && mouse.rightButton.wasPressedThisFrame) Rotate();
            if (Carrying) { if (!_keyCarry) ShowGhost(cx, cy, onGrid); }
            else if (_ghost.gameObject.activeSelf && _focus != 1) _ghost.gameObject.SetActive(false);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (Carrying && _sticky)
                {
                    if (onGrid) Drop(cx, cy);
                    else if (onHome) DropHome();
                    else CancelHeld();
                    return;
                }
                _pressAt = pos; _dragging = false; _pressId = null; _pressPiece = null; _pressHome = _pressBag = false;
                if (onGrid) _pressPiece = _model.Grid.At(cx, cy);
                else if ((_pressId = RowAt(_homeRows, _homeList.parent as RectTransform, pos)) != null) { _pressHome = true; if (_homeScroll) _homeScroll.enabled = false; }
                else if ((_pressId = RowAt(_bagRows, _bagList.parent as RectTransform, pos)) != null) _pressBag = true;
            }
            if (mouse.leftButton.isPressed && !_dragging && (_pressPiece != null || _pressHome) && (pos - _pressAt).sqrMagnitude > 14 * 14)
            {
                _dragging = true;
                if (_pressPiece != null)
                {
                    CellUnder(_pressAt, out int px, out int py);
                    PickUp(_pressPiece, px, py, false);
                }
                else CarryNew(_pressId, false);
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_homeScroll) _homeScroll.enabled = true;
                if (_dragging && Carrying)
                {
                    if (onGrid) { if (!Drop(cx, cy)) CancelHeld(); }
                    else if (onHome) DropHome();
                    else CancelHeld();
                }
                else if (!_dragging)
                {
                    // a click
                    if (_pressPiece != null && onGrid && _model.Grid.At(cx, cy) == _pressPiece) PickUp(_pressPiece, cx, cy, true);
                    else if (_pressHome && _pressId != null && RowAt(_homeRows, _homeList.parent as RectTransform, pos) == _pressId) Move(_pressId, true, ShiftHeld() ? int.MaxValue : 1);
                    else if (_pressBag && _pressId != null && RowAt(_bagRows, _bagList.parent as RectTransform, pos) == _pressId) Move(_pressId, false, ShiftHeld() ? int.MaxValue : 1);
                }
                _dragging = false; _pressId = null; _pressPiece = null; _pressHome = _pressBag = false;
            }

            // hover: why a score line counts, or what a piece is
            if (!Carrying && !_keyWhy)
            {
                string why = null;
                for (int i = 0; i < _hoverLines.Count; i++)
                    if (_hoverLines[i].rt && RectTransformUtility.RectangleContainsScreenPoint(_hoverLines[i].rt, pos, null)) { why = _hoverLines[i].text; break; }
                if (why == null && onGrid && _model.Grid.At(cx, cy) is { } hp)
                {
                    if (_hoverPiece != hp || _hoverScore != _score) { _hoverPiece = hp; _hoverScore = _score; _hoverPieceText = ItemWhy(hp.Id, hp); }
                    why = _hoverPieceText;
                }
                ShowWhy(why);
            }
        }
        BagPiece _hoverPiece;
        BagScore _hoverScore;
        string _hoverPieceText;
        bool _keyCarry;
        Vector2 _lastMouse;

        void KeyInput(Keyboard kb, Gamepad gp)
        {
            bool Pressed(Func<Keyboard, KeyControl> k) => kb != null && k(kb).wasPressedThisFrame;
            bool up = Pressed(k => k.upArrowKey) || (gp != null && gp.dpad.up.wasPressedThisFrame);
            bool down = Pressed(k => k.downArrowKey) || (gp != null && gp.dpad.down.wasPressedThisFrame);
            bool left = Pressed(k => k.leftArrowKey) || (gp != null && gp.dpad.left.wasPressedThisFrame);
            bool right = Pressed(k => k.rightArrowKey) || (gp != null && gp.dpad.right.wasPressedThisFrame);
            bool enter = Pressed(k => k.enterKey) || Pressed(k => k.numpadEnterKey) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            bool space = Pressed(k => k.spaceKey);
            bool esc = Pressed(k => k.escapeKey);
            bool padB = gp != null && gp.buttonEast.wasPressedThisFrame; // B only drops what is carried; Start closes
            bool rot = Pressed(k => k.rKey) || (gp != null && gp.buttonNorth.wasPressedThisFrame);
            bool tidy = Pressed(k => k.tKey) || (gp != null && gp.buttonWest.wasPressedThisFrame);
            bool del = Pressed(k => k.deleteKey) || Pressed(k => k.backspaceKey);
            bool tab = Pressed(k => k.tabKey) || (gp != null && (gp.rightShoulder.wasPressedThisFrame || gp.leftShoulder.wasPressedThisFrame));
            bool done = gp != null && gp.startButton.wasPressedThisFrame;
            // nothing pressed: nothing to do (and nothing allocated this frame)
            if (!(up || down || left || right || enter || space || esc || padB || rot || tidy || del || tab || done)) return;
            _keyWhy = true;
            KeyAct(up, down, left, right, enter, space, esc, padB, rot, tidy, del, tab, done);
            if (_model != null) ShowWhy(KeyWhyText());
        }

        void KeyAct(bool up, bool down, bool left, bool right, bool enter, bool space, bool esc, bool padB, bool rot, bool tidy, bool del, bool tab, bool done)
        {
            if (done) { TryClose(); return; }
            if (padB)
            {
                if (Carrying) { CancelHeld(); Say("내려놓기를 그만뒀다."); Render(); }
                return;
            }
            if (esc)
            {
                if (Carrying) { CancelHeld(); Say("내려놓기를 그만뒀다."); Render(); }
                else TryClose();
                return;
            }
            if (rot) { Rotate(); if (_focus == 1 && _keyCarry) KeyGhost(); return; }
            if (tidy) { Tidy(); return; }
            if (tab)
            {
                if (!Carrying)
                {
                    _focus = (_focus + 1) % 4; _sel = 0; Render(); KeyGhost();
                    if (_o.Profile == "lowvision") Speak(FocusNames[_focus]);
                }
                return;
            }

            if (_focus == 1)
            {
                // the grid: a cursor cell
                int dx = (right ? 1 : 0) - (left ? 1 : 0), dy = (down ? 1 : 0) - (up ? 1 : 0);
                if (dx != 0 || dy != 0)
                {
                    _cx = Mathf.Clamp(_cx + dx, 0, Cols - 1); _cy = Mathf.Clamp(_cy + dy, 0, GridRows - 1);
                    if (Carrying) _keyCarry = true;
                    if (!Carrying && _model.Grid.At(_cx, _cy) != null) SpeakWhy();
                    KeyGhost();
                    return;
                }
                if (enter || space)
                {
                    if (Carrying) Drop(_cx, _cy);
                    else if (_model.Grid.At(_cx, _cy) is { } p) { PickUp(p, _cx, _cy, true); _keyCarry = true; }
                    KeyGhost();
                    return;
                }
                if (del)
                {
                    if (_held != null) DropHome();
                    else if (_heldNew != null) { CancelHeld(); Say("내려놓기를 그만뒀다."); Render(); } // the carried one never went in
                    else if (_model.Grid.At(_cx, _cy) is { } p) { _model.TakeOut(p); Say($"{Obj(_model.View(p.Id)?.Name)} 집에 두었다."); Render(); }
                    KeyGhost();
                }
                return;
            }

            if (_focus == 3)
            {
                // the score: each line's reason (and source), read out in 저시력
                if (down || right) { _scoreSel = Mathf.Min(_scoreSel + 1, Mathf.Max(0, _hoverLines.Count - 1)); Render(); SpeakWhy(); }
                else if (up || left) { _scoreSel = Mathf.Max(0, _scoreSel - 1); Render(); SpeakWhy(); }
                return;
            }

            var list = (_focus == 2 ? _model.InBag : _model.AtHome).ToList();
            if (down) { _sel = Mathf.Min(_sel + 1, Mathf.Max(0, list.Count - 1)); Render(); SpeakWhy(); return; }
            if (up) { _sel = Mathf.Max(0, _sel - 1); Render(); SpeakWhy(); return; }
            if (list.Count == 0) return;
            var id = list[Mathf.Clamp(_sel, 0, list.Count - 1)].Id;
            int n = ShiftHeld() ? int.MaxValue : 1;
            if (_focus == 0)
            {
                if (space) { CarryNew(id, true); _keyCarry = true; _focus = 1; Render(); KeyGhost(); }
                else if (right || enter) Move(id, true, n);
            }
            else if (left || enter || del) Move(id, false, n);
        }

        static readonly string[] FocusNames = { "집 비축 목록", "가방 칸", "가방 목록", "꾸림 평가" };

        /// <summary>The keyboard's cursor (and what it carries) on the grid.</summary>
        void KeyGhost()
        {
            bool on = _focus == 1;
            _cursor.gameObject.SetActive(on);
            if (!on) { if (_ghost) _ghost.gameObject.SetActive(false); return; }
            UiKit.At(_cursor.rectTransform, _cx * Cell, _cy * Cell, Cell, Cell);
            _cursor.transform.SetAsLastSibling();
            if (Carrying) { _grabX = 0; _grabY = 0; ShowGhost(_cx, _cy, true); }
        }
    }
}
