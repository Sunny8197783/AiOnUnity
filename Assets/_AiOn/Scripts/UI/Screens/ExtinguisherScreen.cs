using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The extinguisher encounter (original ui/extinguisher.js, layout kept): left the live scene (corridor, parcel boxes on
    /// fire, the extinguisher and its powder) with the danger meter under it; right the help box, the choice / step buttons,
    /// the feedback line and the retreat button. Decide first (끈다 / 물러나 알린다), then press the steps in order:
    /// the handle (손잡이) is held 0.7 s, the sweep is mouse swings left and right over the scene (or ←/→).
    /// Keys: Tab / Shift+Tab choose, Enter/Space press (hold on the handle), ←/→ sweep, Esc retreat (closes after the fire is out
    /// or in practice). The rules live in <see cref="ExtinguisherLogic"/>; this class only draws and forwards input.
    /// Resolves { sequence, result } like the original.
    /// </summary>
    public class ExtinguisherScreen : UiScreen
    {
        ExtinguisherLogic L;

        RectTransform _stage, _side, _help, _actions;
        Text _feedback, _dangerWord;
        Image _dangerFill;
        Button _retreat;
        Text _retreatText;
        readonly Dictionary<string, (Button b, Text t, Image fill)> _steps = new();
        Button _focused;
        RectTransform _ring;
        bool _keyHold, _mouseInside;
        Vector2 _lastMouse;
        bool _actBuilt;
        SceneView _scene;

        static readonly Color ButtonBg = new(1, 1, 1, 0.08f);
        static readonly Color CardBg = new(1, 1, 1, 0.05f);
        static readonly Color DoneInk = new(0.72f, 0.93f, 0.85f); // #b7ecd9
        static readonly Color Green = new(0.3f, 0.76f, 0.54f); // #4cc38a
        const string DimHex = "#9aa6ad";

        // ---------- entry ----------

        /// <summary>ui.extinguisher({ mode, fire, steps, choices, practice }) -> { sequence, result }.</summary>
        public static async Task<JToken> Open(JObject options)
        {
            var logic = ExtinguisherLogic.FromOptions(options);
            var r = await Show<ExtinguisherScreen>(s => s.L = logic);
            return r as JToken ?? logic.Result();
        }

        public static void Register(UnityStoryHost host)
        {
            host.Screens["extinguisher"] = Open;
        }

        protected override object EscapeResult() => L?.Result();

        static string Fact(string id) => GameSession.Current != null ? GameSession.Current.Fact(id) : null;
        static List<string> Facts(DrillStep s) => ExtinguisherLogic.FactTexts(s?.Facts, Fact);

        // ---------- layout ----------

        protected override void Build()
        {
            L.Changed += OnChanged;
            L.EndedWith += RenderEnd;
            L.Squeezed += () => { if (_steps.TryGetValue("sweep", out var s)) Focus(s.b); };
            L.CloseRequested += () => Finish(L.Result());

            Title(L.Eyebrow, L.Title);
            var chip = L.ModeChip;
            if (chip != null)
            {
                var c = UiKit.Box("Mode", Window, new Color(0.24f, 0.78f, 0.71f, 0.18f)).rectTransform;
                UiKit.At(c, 1520, 34, 160, 40);
                UiKit.Fill(UiKit.Label("Text", c, chip, 22, UiKit.Teal, TextAnchor.MiddleCenter).rectTransform);
            }
            if (L.Practice) UiKit.At(UiKit.Label("Why", Window, ExtinguisherLogic.PracticeNote, 22, UiKit.Dim).rectTransform, 40, 110, 1000, 30);

            // Stage: the scene (640 x 360 in the original, drawn here at 1.575x) and the danger meter.
            _stage = UiKit.Box("Stage", Window, new Color(0.1f, 0.11f, 0.12f)).rectTransform;
            UiKit.At(_stage, 40, 150, 1008, 567);
            _stage.gameObject.AddComponent<RectMask2D>();
            _scene = new SceneView(_stage, L, 1008f / 640f);

            if (!L.Practice)
            {
                var meter = UiKit.Rect("Danger", Window);
                UiKit.At(meter, 40, 734, 1008, 60);
                UiKit.At(UiKit.Label("Label", meter, $"<color=#f2b33d>▲ </color>{L.MeterLabel}", 20, UiKit.Dim).rectTransform, 0, 0, 400, 28);
                _dangerWord = UiKit.Label("Word", meter, "", 22, UiKit.Ink, TextAnchor.UpperRight, FontStyle.Bold);
                UiKit.At(_dangerWord.rectTransform, 600, 0, 408, 28);
                var track = UiKit.Box("Track", meter, new Color(1, 1, 1, 0.1f)).rectTransform;
                UiKit.At(track, 0, 36, 1008, 10);
                _dangerFill = UiKit.Box("Fill", track, Green);
                var f = _dangerFill.rectTransform;
                f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0, 1); f.offsetMin = f.offsetMax = Vector2.zero;
            }

            // Side: help, actions, feedback, retreat (top to bottom, growing with their content).
            _side = UiKit.Rect("Side", Window);
            UiKit.At(_side, 1080, 150, 600, 700);
            var col = _side.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 14; col.childAlignment = TextAnchor.UpperLeft;
            col.childControlWidth = true; col.childForceExpandWidth = true;
            col.childControlHeight = true; col.childForceExpandHeight = false;
            _help = Stack("Help", _side, 8);
            _actions = Stack("Actions", _side, 10);
            _feedback = UiKit.Label("Feedback", _side, "", 22, UiKit.Teal);
            (_retreat, _retreatText, _) = TextButton("Retreat", _side, "", 22, ButtonBg, UiKit.Ink, 60);
            SetRetreatText($"⤺ {L.RetreatLabel}");
            _retreat.onClick.AddListener(() => { Focus(_retreat); L.RetreatButton(); });

            UiKit.At(UiKit.Label("Keys", Window, ExtinguisherLogic.Keys, 18, UiKit.Dim).rectTransform, 40, 880, 1640, 30);

            if (L.Phase == "decide") RenderDecide(); else RenderAct();
        }

        static RectTransform Stack(string name, Transform parent, float spacing)
        {
            var rt = UiKit.Rect(name, parent);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>A button that grows with its (rich) text; an Outline marks the guided "next" one.</summary>
        (Button, Text, Image) TextButton(string name, Transform parent, string text, int size, Color bg, Color fg, float minH, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var img = UiKit.Box(name, parent, bg, raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // keys are handled here, not by the event system
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            b.colors = colors;
            var lay = img.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(18, 18, 12, 12);
            lay.childAlignment = align;
            lay.childControlWidth = true; lay.childForceExpandWidth = true;
            lay.childControlHeight = true; lay.childForceExpandHeight = false;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minH;
            var t = UiKit.Label("Text", img.transform, text, size, fg, align);
            var o = img.gameObject.AddComponent<Outline>();
            o.effectColor = UiKit.Amber; o.effectDistance = new Vector2(3, 3); o.enabled = false;
            return (b, t, img);
        }

        /// <summary>Clear() that first rescues the focus ring if it sits on a button about to go.</summary>
        void ClearPanel(Transform t)
        {
            if (_ring && _ring.IsChildOf(t)) { _ring.SetParent(Window, false); _ring.gameObject.SetActive(false); }
            Clear(t);
        }

        static void Next(Button b, bool on) { var o = b ? b.GetComponent<Outline>() : null; if (o) { o.effectColor = UiKit.Amber; o.enabled = on; } }

        void SetRetreatText(string rich) => _retreatText.text = rich;

        Text Para(Transform parent, string rich, int size, Color color, FontStyle style = FontStyle.Normal) =>
            UiKit.Label("P", parent, rich, size, color, TextAnchor.UpperLeft, style);

        static string Small(string s) => $"<size=18><color={DimHex}>{s}</color></size>";

        // ---------- decision ----------

        void RenderDecide()
        {
            var rec = L.Recommended;
            bool guided = L.Mode == "guided";
            ClearPanel(_actions);
            Para(_actions, ExtinguisherLogic.DecideQuestion, 24, UiKit.Ink, FontStyle.Bold);
            Button first = null;
            foreach (var c in L.Choices.Where(c => c.Id == "fight"))
            {
                var text = $"<b>{c.Label}</b>" + (guided ? string.Concat(Facts(c).Select(t => "\n" + Small(t))) : "");
                var (b, _, _) = TextButton("Choice_" + c.Id, _actions, text, 24, ButtonBg, UiKit.Ink, 64);
                Next(b, guided && c.Id == rec);
                b.onClick.AddListener(() => L.Fight());
                first ??= b;
            }
            var r = L.Choice("retreat");
            if (guided && r != null) SetRetreatText($"⤺ <b>{r.Label}</b>" + string.Concat(Facts(r).Select(t => "\n" + Small(t))));
            Next(_retreat, guided && rec == "retreat");

            // Guided puts the reasons on the buttons themselves; hint keeps them behind a button; free shows nothing.
            ClearPanel(_help);
            if (L.Mode == "hint")
            {
                var lines = L.Choices.Select(c => $"<b>{c.Label}: </b>{string.Join(" ", Facts(c))}").ToList();
                RectTransform body = null;
                Text btnText = null;
                Button hb = null;
                (hb, btnText, _) = TextButton("HintToggle", _help, "힌트 보기", 20, ButtonBg, UiKit.Ink, 48);
                hb.onClick.AddListener(() =>
                {
                    Focus(hb);
                    bool open = body == null || !body.gameObject.activeSelf;
                    if (body == null)
                    {
                        body = HelpBox(_help);
                        foreach (var l in lines) Para(body, l, 20, UiKit.Ink);
                    }
                    body.gameObject.SetActive(open);
                    btnText.text = open ? "힌트 닫기" : "힌트 보기";
                });
            }
            Focus(first);
        }

        RectTransform HelpBox(Transform parent)
        {
            var box = UiKit.Box("HelpBody", parent, new Color(0.24f, 0.78f, 0.71f, 0.08f)).rectTransform;
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 12, 12); v.spacing = 6;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            return box;
        }

        // ---------- acting ----------

        void OnChanged()
        {
            if (L.Ended) return; // the end card replaces the side panel
            if (!_actBuilt && L.Phase == "act") RenderAct();
            else if (_actBuilt) RefreshSteps();
        }

        void RenderAct()
        {
            _actBuilt = true;
            Next(_retreat, false);
            if (!L.Practice) SetRetreatText($"⤺ {L.RetreatLabel}");
            ClearPanel(_actions);
            _steps.Clear();
            Para(_actions, ExtinguisherLogic.ActQuestion, 24, UiKit.Ink, FontStyle.Bold);
            var list = Stack("Steps", _actions, 8);
            foreach (var s in L.Order)
            {
                var id = s.Id;
                var (b, t, img) = TextButton("Step_" + id, list, "", 23, ButtonBg, UiKit.Ink, 62);
                // Hold progress (the original's --p): a bar filling the button from the left.
                var fill = UiKit.Box("Hold", img.transform, new Color(0.24f, 0.78f, 0.71f, 0.28f));
                fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                var fr = fill.rectTransform;
                fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0, 1); fr.offsetMin = fr.offsetMax = Vector2.zero;
                fill.transform.SetAsFirstSibling();
                _steps[id] = (b, t, fill);
                if (id == "squeeze")
                {
                    var h = b.gameObject.AddComponent<ExtinguisherHoldButton>();
                    h.Down = () => { Focus(b); L.StartHold(); };
                    h.Up = () => L.StopHold();
                }
                else b.onClick.AddListener(() => { Focus(b); L.Press(id); });
            }
            if (L.Order.Count > 0) Focus(_steps[L.Order[0].Id].b);
            RefreshSteps();
        }

        void RefreshSteps()
        {
            bool guided = L.Mode == "guided";
            var expected = L.Expected();
            foreach (var kv in _steps)
            {
                var id = kv.Key;
                bool done = L.IsDone(id);
                var label = L.Step(id)?.Label ?? id;
                var tip = id == "squeeze" ? ExtinguisherLogic.HoldTip : id == "sweep" ? ExtinguisherLogic.SweepTip : null;
                var mark = done ? "<color=#4cc38a><b>✓</b></color>" : "○";
                kv.Value.t.text = $"{mark}  {label}" + (tip != null ? $"   {Small(tip)}" : "");
                kv.Value.t.color = done ? DoneInk : UiKit.Ink;
                Next(kv.Value.b, guided && !done && id == expected);
            }

            ClearPanel(_help);
            var nx = L.Step(expected);
            void Make(Transform into)
            {
                if (nx == null) return;
                Para(into, $"<b>다음: </b>{nx.Label}", 22, UiKit.Ink);
                foreach (var t in Facts(nx)) Para(into, t, 19, UiKit.Dim);
            }
            if (L.Mode == "guided") Make(HelpBox(_help));
            else if (L.Mode == "hint")
            {
                Button hb = null;
                (hb, _, _) = TextButton("Hint", _help, "힌트: 다음 순서", 20, ButtonBg, UiKit.Ink, 48);
                hb.onClick.AddListener(() =>
                {
                    bool wasFocused = _focused == hb;
                    var box = HelpBox(_help);
                    Make(box);
                    if (_ring && _ring.IsChildOf(hb.transform)) { _ring.SetParent(Window, false); _ring.gameObject.SetActive(false); }
                    hb.gameObject.SetActive(false);
                    Destroy(hb.gameObject);
                    if (wasFocused) Focus(FirstFocusable());
                });
            }
        }

        // ---------- end ----------

        void RenderEnd(string kind)
        {
            _keyHold = false;
            ClearPanel(_actions);
            _steps.Clear();
            var card = UiKit.Box("End_" + kind, _actions, CardBg).rectTransform;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(20, 20, 16, 16); v.spacing = 10;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            var edge = card.gameObject.AddComponent<Outline>();
            edge.effectColor = kind == "out" ? new Color(0.3f, 0.76f, 0.54f, 0.5f) : new Color(0.9f, 0.28f, 0.3f, 0.55f);
            edge.effectDistance = new Vector2(2, 2);

            Para(card, ExtinguisherLogic.EndTitle(kind, L.Practice), 28, UiKit.Ink, FontStyle.Bold);
            if (kind == "out")
            {
                int n = 0;
                foreach (var s in L.Steps)
                {
                    n++;
                    bool done = L.IsDone(s.Id);
                    Para(card, $"{n}. {(done ? "<color=#4cc38a>✓</color> " : "○ ")}{s.Label}", 21, done ? DoneInk : UiKit.Dim);
                }
                if (!L.ShoutFirst) Para(card, Fact("fire_shout") ?? "", 19, UiKit.Dim);
                if (L.Practice) foreach (var t in ExtinguisherLogic.FactTexts(new[] { "ext_1", "ext_2", "ext_3" }, Fact)) Para(card, t, 19, UiKit.Dim);
            }
            else Para(card, ExtinguisherLogic.EndBody(kind), 22, UiKit.Ink);

            var (ok, okText, _) = TextButton("Ok", card, ExtinguisherLogic.EndOk(kind), 24, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f), 60, TextAnchor.MiddleCenter);
            okText.fontStyle = FontStyle.Bold;
            ok.onClick.AddListener(() => L.Close());

            ClearPanel(_help);
            _retreat.gameObject.SetActive(L.RetreatVisible);
            Focus(ok);
        }

        // ---------- focus (the original's Tab order: help, actions, retreat) ----------

        List<Button> Focusables()
        {
            var list = new List<Button>();
            list.AddRange(_help.GetComponentsInChildren<Button>());
            list.AddRange(_actions.GetComponentsInChildren<Button>());
            if (_retreat && _retreat.gameObject.activeInHierarchy) list.Add(_retreat);
            return list;
        }

        Button FirstFocusable() => Focusables().FirstOrDefault();

        void Focus(Button b)
        {
            _focused = b;
            if (!_ring)
            {
                _ring = UiKit.Rect("FocusRing", Window);
                _ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                var white = new Color(1, 1, 1, 0.85f);
                void Edge(Vector2 min, Vector2 max, Vector2 oMin, Vector2 oMax)
                {
                    var e = UiKit.Box("Edge", _ring, white).rectTransform;
                    e.anchorMin = min; e.anchorMax = max; e.offsetMin = oMin; e.offsetMax = oMax;
                }
                Edge(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), Vector2.zero);
                Edge(new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 2));
                Edge(new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(2, 0));
                Edge(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), Vector2.zero);
            }
            if (b)
            {
                _ring.SetParent(b.transform, false);
                UiKit.Fill(_ring, -5, -5);
                _ring.SetAsLastSibling();
                _ring.gameObject.SetActive(true);
            }
            else
            {
                _ring.SetParent(Window, false);
                _ring.gameObject.SetActive(false);
            }
        }

        void MoveFocus(int step)
        {
            var list = Focusables();
            if (list.Count == 0) return;
            int i = _focused ? list.IndexOf(_focused) : -1;
            i = i < 0 ? (step > 0 ? 0 : list.Count - 1) : (i + step + list.Count) % list.Count;
            Focus(list[i]);
        }

        bool IsSqueeze(Button b) => b && _steps.TryGetValue("squeeze", out var s) && s.b == b;

        // ---------- frame ----------

        protected override void Update()
        {
            if (L == null || L.Closed) return;
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject) EventSystem.current.SetSelectedGameObject(null);

            var kb = Keyboard.current;
            if (kb != null && Time.unscaledTime - OpenedAt >= 0.2f)
            {
                if (kb.escapeKey.wasPressedThisFrame) { L.Escape(); return; }
                if (kb.leftArrowKey.wasPressedThisFrame) L.Arrow(-1);
                else if (kb.rightArrowKey.wasPressedThisFrame) L.Arrow(1);
                if (kb.tabKey.wasPressedThisFrame) MoveFocus(kb.shiftKey.isPressed ? -1 : 1);
                bool press = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                if (!_focused) _focused = null;
                if (press && _focused && _focused.gameObject.activeInHierarchy)
                {
                    if (IsSqueeze(_focused)) { _keyHold = true; L.StartHold(); }
                    else _focused.onClick.Invoke();
                }
                if (_keyHold && (kb.enterKey.wasReleasedThisFrame || kb.numpadEnterKey.wasReleasedThisFrame || kb.spaceKey.wasReleasedThisFrame))
                {
                    _keyHold = false; L.StopHold();
                }
                if (L.Closed) return;
            }

            // Mouse sweep over the scene: x across the stage, 0..1 (only when the pointer moved, like pointermove).
            var m = Mouse.current;
            if (m != null)
            {
                var pos = m.position.ReadValue();
                bool inside = RectTransformUtility.RectangleContainsScreenPoint(_stage, pos, null);
                if (inside && (!_mouseInside || pos != _lastMouse)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, pos, null, out var lp))
                {
                    var r = _stage.rect;
                    L.PointerMove((lp.x - r.xMin) / r.width);
                }
                else if (!inside && _mouseInside) L.PointerLeave();
                _mouseInside = inside; _lastMouse = pos;
            }

            float dt = Time.unscaledDeltaTime;
            L.Tick(dt);
            if (L.Closed) return;

            if (_dangerFill)
            {
                _dangerFill.rectTransform.anchorMax = new Vector2((float)L.Danger, 1);
                var st = ExtinguisherLogic.DangerState(L.Danger);
                _dangerFill.color = st == "over" ? UiKit.Red : st == "warn" ? UiKit.Amber : Green;
                _dangerWord.text = ExtinguisherLogic.DangerWord(L.Danger);
            }
            if (_steps.TryGetValue("squeeze", out var sq))
                sq.fill.rectTransform.anchorMax = new Vector2(L.IsDone("squeeze") ? 0 : (float)L.HoldProgress, 1);
            _feedback.text = L.Feedback;
            _scene.Draw(Time.unscaledTime, Mathf.Clamp(dt, 0, 0.1f));
        }

        // ---------- the scene (the original's canvas drawing, rebuilt from rectangles and a few generated sprites) ----------

        sealed class SceneView
        {
            const float W = 640, H = 360, FloorY = H * 0.74f, Fx = W * 0.76f, Fy = FloorY - 52, ExtX = 150, ExtY = H - 20;
            const int MaxParticles = 280;
            readonly float k;
            readonly RectTransform root;
            readonly ExtinguisherLogic L;
            readonly Image glow, smoke, residue, residueBar, leverTop, pinRing, pinRod, nozzle;
            readonly Image[] flames = new Image[18], embers = new Image[10], hose = new Image[12];
            readonly List<(Image img, float x, float y, float vx, float vy, float life, float max, float r)> parts = new();
            readonly Stack<Image> pool = new();
            readonly System.Random rng = new();

            static Sprite _radial, _disc, _ring, _vgrad, _flameOuter, _flameInner;

            public SceneView(RectTransform stage, ExtinguisherLogic logic, float scale)
            {
                k = scale; L = logic;
                root = UiKit.Rect("Scene", stage);
                UiKit.Fill(root);
                MakeSprites();

                // Corridor wall with faint panel seams.
                Put(Img("Wall", C(0x23262a)), 0, 0, W, FloorY);
                Put(Img("WallLight", C(0x303438), _vgrad), 0, 0, W, FloorY);
                for (float x = 120; x < W; x += 170) Put(Img("Seam", new Color(1, 1, 1, 0.045f)), x - 1, 0, 2, FloorY);
                // The way out is behind you: the door and its exit sign.
                Put(Img("DoorFrame", C(0x463c33)), 18, FloorY - 236, 82, 236);
                Put(Img("Door", C(0x5a4e42)), 25, FloorY - 229, 68, 229);
                Put(Img("Handle", C(0xc9c2b4)), 80, FloorY - 120, 8, 4);
                var sign = Img("Exit", C(0x2e7d4f));
                Put(sign, 34, FloorY - 262, 50, 18);
                UiKit.Fill(UiKit.Label("Text", sign.transform, "비상구", Mathf.RoundToInt(11 * k), C(0xe9fff1), TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
                // Floor with perspective lines.
                Put(Img("Floor", C(0x1b1814)), 0, FloorY, W, H - FloorY);
                Put(Img("FloorLight", C(0x3b342d), _vgrad), 0, FloorY, W, H - FloorY);
                for (int i = 1; i < 6; i++) Put(Img("FloorLine", new Color(0, 0, 0, 0.35f)), 0, FloorY + (H - FloorY) * Mathf.Pow(i / 6f, 1.5f), W, 1);
                for (int i = -6; i <= 6; i++) Line(Img("FloorRay", new Color(0, 0, 0, 0.35f)), W / 2 + i * 40, FloorY, W / 2 + i * 150, H, 1);
                Put(Img("Baseboard", C(0x18191a)), 0, FloorY - 9, W, 9);
                glow = Img("Glow", new Color(1, 0.59f, 0.24f, 0), _radial);

                // Parcel boxes.
                foreach (var (x, w, h, c, y0) in new[] { (Fx - 78, 88f, 58f, 0xa3774a, 0f), (Fx + 6, 72f, 74f, 0xb2834f, 0f), (Fx - 46, 66f, 44f, 0x986d43, 58f) })
                {
                    float y = FloorY - y0 - h;
                    Put(Img("Box", C(c)), x, y, w, h);
                    Put(Img("Shade", new Color(0, 0, 0, 0.18f)), x + w * 0.7f, y, w * 0.3f, h);
                    Put(Img("Tape", new Color(0.82f, 0.75f, 0.59f, 0.55f)), x + w / 2 - 5, y, 10, h);
                    Put(Img("Label", C(0xf2efe6)), x + 8, y + h * 0.45f, 22, 13);
                }
                residue = Img("Residue", new Color(0.93f, 0.93f, 0.9f, 0.75f), _disc);
                Rot(residue, Fx, FloorY + 6, 240, 28, 0);
                residueBar = Img("ResidueBar", new Color(0.93f, 0.93f, 0.9f, 0.75f));
                Put(residueBar, Fx - 80, Fy - 30, 150, 8);

                for (int i = 0; i < flames.Length; i++)
                {
                    flames[i] = Img("Flame", Color.white, i < 9 ? _flameOuter : _flameInner);
                    var rt = flames[i].rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0);
                }
                for (int i = 0; i < embers.Length; i++) embers[i] = Img("Ember", new Color(1, 0.75f, 0.35f, 0));
                smoke = Img("Smoke", new Color(0.07f, 0.07f, 0.08f, 0), _vgrad);

                // The extinguisher.
                float x0 = ExtX, y1 = ExtY, bw = 46, bh = 122, top = y1 - bh;
                Rot(Img("Shadow", new Color(0, 0, 0, 0.4f), _disc), x0, y1 + 2, 68, 12, 0);
                Put(Img("Body", C(0xe23a2c)), x0 - bw / 2, top, bw, bh);
                Put(Img("BodyDark", new Color(0.48f, 0.07f, 0.04f, 0.8f)), x0 - bw / 2, top, 13, bh);
                Put(Img("BodyDark", new Color(0.4f, 0.05f, 0.04f, 0.8f)), x0 + bw / 2 - 10, top, 10, bh);
                Put(Img("BodyLight", new Color(1, 0.48f, 0.4f, 0.6f)), x0 - 2, top, 6, bh);
                var plate = Img("Plate", C(0xf3f0e8));
                Put(plate, x0 - 17, top + 36, 34, 46);
                UiKit.Fill(UiKit.Label("Text", plate.transform, "분말\n소화기", Mathf.RoundToInt(10 * k), C(0xc42a1f), TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
                Put(Img("Head", C(0x2a2c2f)), x0 - 9, top - 16, 18, 18);
                Rot(Img("Gauge", C(0xe8e8e4), _disc), x0 + 14, top - 6, 14, 14, 0);
                Rot(Img("GaugeGreen", C(0x2fa35a), _ring), x0 + 14, top - 6, 11, 11, 0);
                Line(Img("Needle", C(0x222222)), x0 + 14, top - 6, x0 + 11, top - 10, 1.2f);
                Line(Img("Lever", C(0x141516)), x0 - 6, top - 14, x0 - 38, top - 18, 6);
                leverTop = Img("LeverTop", C(0x141516));
                pinRing = Img("Pin", C(0xe9c23b), _ring);
                Rot(pinRing, x0 - 24, top - 31, 15, 15, 0);
                pinRod = Img("PinRod", C(0xc9c9c9));
                Line(pinRod, x0 - 18, top - 30, x0 - 4, top - 25, 2);
                for (int i = 0; i < hose.Length; i++) hose[i] = Img("Hose", C(0x111214));
                nozzle = Img("Nozzle", C(0x1c1d20));
            }

            public void Draw(float t, float dt)
            {
                float lv = (float)L.FireLv, ft = t, flick = 0.82f + 0.18f * Nz(ft * 9);
                root.anchoredPosition = new Vector2(L.Shake > 0 ? Mathf.Sin(t * 90) * (float)L.Shake * 6 * k : 0, 0);

                glow.gameObject.SetActive(lv > 0);
                if (lv > 0)
                {
                    float r = 150 + lv * 300;
                    Rot(glow, Fx, Fy, r * 2, r * 2, 0);
                    glow.color = new Color(1, 0.59f, 0.24f, 0.55f * lv * flick);
                }
                bool swept = L.IsDone("sweep");
                residue.gameObject.SetActive(swept); residueBar.gameObject.SetActive(swept);

                // Flames: two layers of nine tongues, height and width from the fire level.
                float h = 46 + lv * (L.Large ? 290 : 220), w = 34 + lv * 130;
                for (int layer = 0; layer < 2; layer++)
                    for (int i = 0; i < 9; i++)
                    {
                        var img = flames[layer * 9 + i];
                        img.gameObject.SetActive(lv > 0);
                        if (lv <= 0) continue;
                        float u = i / 8f - 0.5f, kk = 1 - Mathf.Abs(u) * 1.4f;
                        float hh = h * kk * (0.62f + 0.38f * Nz(ft * 6 + i * 1.9f)) * (layer == 1 ? 0.55f : 1), ww = (w / 9) * (layer == 1 ? 0.9f : 1.6f);
                        float x = Fx + u * w, sway = Mathf.Sin(ft * 4 + i) * ww * 0.6f;
                        var rt = img.rectTransform;
                        rt.anchoredPosition = new Vector2(x * k, -Fy * k);
                        rt.sizeDelta = new Vector2(ww * 2 * k, Mathf.Max(0, hh) * k);
                        rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(sway, Mathf.Max(1, hh)) * Mathf.Rad2Deg);
                        img.color = new Color(1, 1, 1, flick);
                    }
                for (int i = 0; i < embers.Length; i++)
                {
                    bool on = lv > 0 && i < 10 * lv;
                    embers[i].gameObject.SetActive(on);
                    if (!on) continue;
                    float s = (ft * 0.7f + i * 0.137f) % 1, ex = Fx + Mathf.Sin(i * 7.3f + ft) * w * 0.5f, ey = Fy - s * h * 1.2f;
                    Put(embers[i], ex, ey, 2.5f, 2.5f);
                    embers[i].color = new Color(1, 0.75f, 0.35f, 0.8f * (1 - s));
                }
                smoke.gameObject.SetActive(lv > 0.04f);
                if (lv > 0.04f)
                {
                    Put(smoke, 0, 0, W, 70 + lv * (L.Large ? 210 : 140));
                    smoke.color = new Color(0.07f, 0.07f, 0.08f, 0.3f + lv * 0.5f);
                }

                // Extinguisher: lever closes with the hold, pin until pulled, hose and nozzle aimed at the fire.
                float x0 = ExtX, top = ExtY - 122, sq = (float)L.HoldProgress;
                Line(leverTop, x0 - 6, top - 22, x0 - 38, top - 38 + sq * 15, 6);
                pinRing.gameObject.SetActive(!L.PinOut); pinRod.gameObject.SetActive(!L.PinOut);
                float tipX, tipY, ang;
                if (L.Aimed) { tipX = x0 + 128; tipY = top + 4; ang = Mathf.Atan2(Fy - tipY, Fx - tipX) + (float)L.SweepDir * 0.26f + (L.Spraying ? Mathf.Sin(ft * 3) * 0.03f : 0); }
                else { tipX = x0 + 34; tipY = ExtY - 16; ang = Mathf.PI * 0.45f; }
                Vector2 p0 = new(x0 + 8, top - 8), c1 = new(x0 + 50, top + 6), c2 = new(tipX - 70, tipY + 44), p3 = new(tipX - 22 * Mathf.Cos(ang), tipY - 22 * Mathf.Sin(ang));
                var prev = p0;
                for (int i = 1; i <= hose.Length; i++)
                {
                    float s = i / (float)hose.Length, a = 1 - s;
                    var p = a * a * a * p0 + 3 * a * a * s * c1 + 3 * a * s * s * c2 + s * s * s * p3;
                    Line(hose[i - 1], prev.x, prev.y, p.x, p.y, 7);
                    prev = p;
                }
                Rot(nozzle, tipX - 12 * Mathf.Cos(ang), tipY - 12 * Mathf.Sin(ang), 24, 14, ang);

                // Powder.
                if (L.Spraying)
                    for (int i = 0; i < 6; i++)
                    {
                        float a = ang + ((float)rng.NextDouble() - 0.5f) * 0.35f, v = 260 + (float)rng.NextDouble() * 150;
                        parts.Add((Take(), tipX, tipY, Mathf.Cos(a) * v, Mathf.Sin(a) * v, 0, 0.55f + (float)rng.NextDouble() * 0.5f, 3));
                    }
                while (parts.Count > MaxParticles) { Give(parts[0].img); parts.RemoveAt(0); }
                for (int i = parts.Count - 1; i >= 0; i--)
                {
                    var p = parts[i];
                    p.x += p.vx * dt; p.y += p.vy * dt; p.vx *= 0.965f; p.vy = p.vy * 0.965f + 25 * dt; p.life += dt; p.r += 46 * dt;
                    if (p.life >= p.max) { Give(p.img); parts.RemoveAt(i); continue; }
                    parts[i] = p;
                    Rot(p.img, p.x, p.y, p.r * 2, p.r * 2, 0);
                    p.img.color = new Color(0.95f, 0.95f, 0.93f, Mathf.Max(0, 0.32f * (1 - p.life / p.max)));
                }
            }

            Image Take()
            {
                var img = pool.Count > 0 ? pool.Pop() : Img("Powder", Color.white, _disc);
                img.gameObject.SetActive(true);
                img.transform.SetAsLastSibling();
                return img;
            }

            void Give(Image img) { img.gameObject.SetActive(false); pool.Push(img); }

            static float Nz(float a) => 0.5f + 0.5f * Mathf.Sin(a) * Mathf.Sin(a * 1.37f + 1.1f);

            static Color C(int hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

            Image Img(string name, Color c, Sprite sprite = null)
            {
                var img = UiKit.Box(name, root, c);
                if (sprite) img.sprite = sprite;
                return img;
            }

            /// <summary>Top-left rectangle in the original's 640 x 360 canvas units.</summary>
            void Put(Image img, float x, float y, float w, float h) => UiKit.At(img.rectTransform, x * k, y * k, w * k, h * k);

            /// <summary>Centred, rotated rectangle (canvas angle, radians, clockwise with y down).</summary>
            void Rot(Image img, float cx, float cy, float w, float h, float ang)
            {
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(cx * k, -cy * k);
                rt.sizeDelta = new Vector2(w * k, h * k);
                rt.localRotation = Quaternion.Euler(0, 0, -ang * Mathf.Rad2Deg);
            }

            void Line(Image img, float x1, float y1, float x2, float y2, float width)
            {
                float dx = x2 - x1, dy = y2 - y1;
                Rot(img, (x1 + x2) / 2, (y1 + y2) / 2, Mathf.Sqrt(dx * dx + dy * dy), width, Mathf.Atan2(dy, dx));
            }

            static void MakeSprites()
            {
                if (_radial) return;
                _radial = Bake(64, 64, (u, v) => { float d = Mathf.Min(1, Mathf.Sqrt(u * u + v * v)); return new Color(1, 1, 1, Mathf.Pow(1 - d, 1.6f)); });
                _disc = Bake(64, 64, (u, v) => new Color(1, 1, 1, Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 16)));
                _ring = Bake(64, 64, (u, v) => { float d = Mathf.Sqrt(u * u + v * v); return new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 16) * Mathf.Clamp01((d - 0.62f) * 16)); });
                _vgrad = Bake(4, 64, (u, v) => new Color(1, 1, 1, (v + 1) / 2)); // opaque at the top, clear at the bottom
                _flameOuter = Flame(new Color(1, 0.67f, 0.24f, 0.85f), new Color(1, 0.38f, 0.11f, 0.55f), new Color(0.75f, 0.16f, 0.04f, 0));
                _flameInner = Flame(new Color(1, 0.98f, 0.84f, 0.9f), new Color(1, 0.82f, 0.37f, 0.6f), new Color(0.75f, 0.16f, 0.04f, 0));
            }

            /// <summary>A flame tongue: wide at the base, tapering to a point, coloured bottom -> middle -> top.</summary>
            static Sprite Flame(Color bottom, Color mid, Color topC)
            {
                return Bake(32, 64, (u, v) =>
                {
                    float y = (v + 1) / 2; // 0 bottom, 1 top
                    float half = Mathf.Pow(1 - y, 0.75f) * Mathf.Clamp01(0.55f + y * 3);
                    float inside = Mathf.Clamp01((half - Mathf.Abs(u)) * 6);
                    var c = y < 0.5f ? Color.Lerp(bottom, mid, y * 2) : Color.Lerp(mid, topC, (y - 0.5f) * 2);
                    c.a *= inside;
                    return c;
                }, new Vector2(0.5f, 0));
            }

            /// <summary>A generated sprite; f gets u, v in -1..1 (v up).</summary>
            static Sprite Bake(int w, int h, Func<float, float, Color> f, Vector2? pivot = null)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[y * w + x] = f((x + 0.5f) / w * 2 - 1, (y + 0.5f) / h * 2 - 1);
                t.SetPixels(px);
                t.Apply();
                return Sprite.Create(t, new Rect(0, 0, w, h), pivot ?? new Vector2(0.5f, 0.5f));
            }
        }
    }

    /// <summary>Press-and-hold for the handle button: pointer down starts, pointer up (anywhere) or disabling stops.</summary>
    public class ExtinguisherHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action Down, Up;
        bool _down;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _down = true; Down?.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !_down) return;
            _down = false; Up?.Invoke();
        }

        void OnDisable()
        {
            if (!_down) return;
            _down = false; Up?.Invoke();
        }
    }
}
