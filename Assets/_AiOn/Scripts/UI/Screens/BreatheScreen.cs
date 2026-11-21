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
    /// 숨 고르기 ("breathe", competition upgrade; 4장 「임시 거처의 하루」). Hold Space, the mouse on the circle or the pad's
    /// A while breathing in, let go to breathe out; three breaths. Toggle mode (T, or the button; the default for the
    /// mobility, wheelchair and guardian profiles): one press starts a guided rhythm that runs by itself. The circle grows
    /// and shrinks with the breath; under reduce-motion it stays the same size and fills instead. Low vision: the cues are
    /// read aloud. The pace never slows; the experience modes widen how short a breath may be (BreathLogic.Wide).
    /// Options { profile, reduceMotion, with ("child"|""), mind, tension, toggle, wide, facts { id: { text, source } } }
    /// (tension: the 마음 line settles as each breath finishes, by ShelterLifeLogic.BreathDrop.)
    /// -> { played, breaths, mode ("hold"|"toggle"), short }. Esc stops with the breaths so far.
    /// </summary>
    public class BreatheScreen : UiScreen
    {
        JObject _o;
        BreathLogic _b;
        bool _reduce, _lowVision, _mouseHold, _touchHold;
        string _lastPhase;
        RectTransform _stage, _disc;
        Image _discImg;
        Text _count, _cue, _progress, _hint, _howTo, _toggleLabel, _mind;
        Button _start, _stop, _done;
        AiOn.Settings.HostSettings _speech;
        double _tension = -1;
        int _shownBreaths = -1;
        /// <summary>The count inside the ring (no string built per frame).</summary>
        static readonly string[] Digits = { "", "1", "2", "3", "4" };

        public static async Task<JToken> Open(JObject options)
        {
            var r = await Show<BreatheScreen>(s => s._o = options ?? new JObject(), 1240, 780);
            return r as JToken ?? new JObject();
        }

        public static void Register(UnityStoryHost host) => host.Screens[ShelterLifeLogic.Screen] = Open;

        protected override object EscapeResult() => Result();

        JObject Result() => new JObject
        {
            ["played"] = _b != null && _b.Breaths > 0, ["breaths"] = _b?.Breaths ?? 0, ["mode"] = _b != null && _b.Toggle ? "toggle" : "hold", ["short"] = _b?.Short ?? 0,
        };

        string Fact(string id) => EasyKorean.Of(_o?["facts"]?[id]?["text"]?.ToString()); // 쉬운 한국어
        string Source(string id) => _o?["facts"]?[id]?["source"]?.ToString();
        bool B(string k) => _o[k]?.Type == JTokenType.Boolean && (bool)_o[k];

        protected override void Build()
        {
            _reduce = B("reduceMotion");
            _lowVision = _o["profile"]?.ToString() == "lowvision";
            _b = new BreathLogic { Toggle = B("toggle"), Wide = B("wide") };
            var tn = _o["tension"];
            if (tn != null && (tn.Type == JTokenType.Integer || tn.Type == JTokenType.Float)) _tension = (double)tn;
            bool child = _o["with"]?.ToString() == "child";
            Title("숨 고르기 · 게임 속 쉬어 가기", child ? "지우와 함께 천천히 숨을 고릅니다" : "천천히 숨을 고릅니다");

            // the circle: a guide ring at full size, and the breath inside it
            _stage = UiKit.Rect("Stage", Window);
            UiKit.At(_stage, 40, 130, 560, 560);
            // the circle's area takes the mouse and touch; an invisible hit area (a Graphic that draws nothing)
            var hit = UiKit.Rect("Hit", _stage).gameObject.AddComponent<BreatheHitArea>();
            UiKit.Fill(hit.rectTransform);
            var guide = UiKit.Box("Guide", _stage, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.55f));
            guide.sprite = HudDraw.RingSprite; guide.preserveAspect = true;
            Centre(guide.rectTransform, 440);
            _discImg = UiKit.Box("Breath", _stage, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.32f));
            _discImg.sprite = HudDraw.DiscSprite; _discImg.preserveAspect = true;
            _disc = Centre(_discImg.rectTransform, 420);
            if (_reduce) { _discImg.type = Image.Type.Filled; _discImg.fillMethod = Image.FillMethod.Vertical; _discImg.fillOrigin = 0; _discImg.fillAmount = 0; }
            _count = UiKit.Label("Count", _stage, "", 120, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Centre(_count.rectTransform, 300);
            _cue = UiKit.Label("Cue", Window, "", 34, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.At(_cue.rectTransform, 40, 700, 560, 50);

            // the right column
            var right = UiKit.Rect("Right", Window);
            UiKit.At(right, 640, 130, 560, 620);
            _mind = Para(right, $"마음: {_o["mind"]?.ToString() ?? "—"}", 26, UiKit.Ink, 0, 36, FontStyle.Bold);
            _mind.horizontalOverflow = HorizontalWrapMode.Overflow;
            _progress = Para(right, "", 22, UiKit.Dim, 40, 30);
            _howTo = Para(right, "", 21, UiKit.Ink, 82, 96);
            _hint = Para(right, "", 21, UiKit.Amber, 180, 32);
            var tog = UiKit.Button("Toggle", right, "", 20, ScreenDecor.Over(UiKit.Panel, Color.white, 0.08f), UiKit.Ink);
            UiKit.At((RectTransform)tog.transform, 0, 222, 560, 52);
            _toggleLabel = tog.GetComponentInChildren<Text>();
            tog.onClick.AddListener(SwitchMode);
            _start = Action(right, "시작 (Space)", true, () => _b.Start(), 0, 286, 270, 56);
            _stop = Action(right, "그만하기 (Esc)", false, () => Finish(Result()), 290, 286, 270, 56);
            _done = Action(right, "돌아가기 (Enter)", true, () => Finish(Result()), 0, 286, 560, 56);
            _done.gameObject.SetActive(false);

            float y = 360;
            var breath = Fact("mind_breath");
            if (!string.IsNullOrEmpty(breath)) { Para(right, $"{breath}\n<size=16><color=#9aa6ad>— {Source("mind_breath")}</color></size>", 19, UiKit.Ink, y, 110); y += 118; }
            var call = Fact("mind_call");
            if (!string.IsNullOrEmpty(call)) Para(right, $"<color=#9aa6ad>{call} — {Source("mind_call")}</color>", 17, UiKit.Dim, y, 70);
            Para(right, "<color=#9aa6ad>[게임 동작] 들이쉬고 내쉬는 4초는 이 장면의 속도이며, 진단이나 치료가 아닙니다.</color>", 16, UiKit.Dim, 584, 40);
            Refresh();
            Say(_b.Toggle ? "숨 고르기. 시작을 누르면 함께 숫자를 셉니다. 들이쉬기 넷, 내쉬기 넷." : "숨 고르기. 스페이스를 누르고 있는 동안 들이쉬고, 떼면 내쉽니다. 세 번.");
        }

        RectTransform Centre(RectTransform rt, float size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        static Text Para(RectTransform parent, string rich, int size, Color color, float y, float h, FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.Label("P", parent, rich, size, color, TextAnchor.UpperLeft, style);
            UiKit.At(t.rectTransform, 0, y, 560, h);
            return t;
        }

        void SwitchMode()
        {
            if (_b.Finished) return;
            _b.SetMode(!_b.Toggle); // the breaths so far are kept; the breath under way starts again
            _lastPhase = null;
            Refresh();
            Say(_b.Toggle ? "켜 두기. 시작을 누르면 함께 셉니다." : "누르고 있기. 누르고 있는 동안 들이쉽니다.");
        }

        void Say(string text)
        {
            if (!_lowVision || string.IsNullOrEmpty(text)) return;
            if (!_speech) _speech = Object.FindFirstObjectByType<AiOn.Settings.HostSettings>();
            if (_speech) _speech.Speak(text);
        }

        bool InStage(Vector2 p) => RectTransformUtility.RectangleContainsScreenPoint(_stage, p, null);

        bool Held(out bool pressed)
        {
            pressed = false;
            var kb = Keyboard.current;
            bool key = kb != null && kb.spaceKey.isPressed;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) pressed = true;
            var gp = Gamepad.current;
            bool pad = gp != null && gp.buttonSouth.isPressed;
            if (gp != null && gp.buttonSouth.wasPressedThisFrame) pressed = true;
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame && InStage(m.position.ReadValue())) { _mouseHold = true; pressed = true; }
                if (!m.leftButton.isPressed) _mouseHold = false;
            }
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var pt = ts.primaryTouch;
                if (pt.press.wasPressedThisFrame && InStage(pt.position.ReadValue())) { _touchHold = true; pressed = true; }
                if (!pt.press.isPressed) _touchHold = false;
            }
            return key || pad || _mouseHold || _touchHold;
        }

        void Refresh()
        {
            bool toggle = _b.Toggle;
            _toggleLabel.text = toggle ? "■ 길게 누르기 대신 켜 두기 (T) — 누르지 않아도 함께 셉니다" : "□ 길게 누르기 대신 켜 두기 (T)";
            _howTo.text = toggle
                ? "시작을 누르면 들이쉬기 넷, 내쉬기 넷을 함께 셉니다. 숫자에 맞춰 천천히."
                : "Space나 게임패드 A, 또는 화면의 원을 마우스·손가락으로 누르고 있는 동안 들이쉬고, 떼면 내쉽니다.";
            _progress.text = $"숨 {_b.Breaths} / {BreathLogic.Target}";
            bool start = toggle && _b.Phase == BreathLogic.Ready;
            _start.gameObject.SetActive(start);
            // with no 시작 button beside it, 그만하기 takes the whole row (no empty slot)
            _stop.gameObject.SetActive(!_b.Finished);
            UiKit.At((RectTransform)_stop.transform, start ? 290 : 0, 286, start ? 270 : 560, 56);
            _done.gameObject.SetActive(_b.Finished);
            if (_shownBreaths != _b.Breaths)
            {
                _shownBreaths = _b.Breaths;
                if (_tension >= 0 && _b.Breaths > 0)
                    _mind.text = ShelterLifeLogic.MindLine(_tension, System.Math.Max(0, _tension - ShelterLifeLogic.BreathDrop(_b.Breaths)));
            }
        }

        protected override void Update()
        {
            base.Update();
            if (_b == null) return;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null); // Space / Enter never also "submit" a button
            var kb = Keyboard.current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f); // a hitch is not a held breath
            if (Time.unscaledTime - OpenedAt < 0.3f) { Held(out _); return; }

            if (_b.Finished)
            {
                var gp0 = Gamepad.current;
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (gp0 != null && gp0.buttonSouth.wasPressedThisFrame)) Finish(Result());
                return;
            }
            if (kb != null && kb.tKey.wasPressedThisFrame) { SwitchMode(); return; }
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame) { SwitchMode(); return; }

            bool held = Held(out bool pressed);
            if (_b.Toggle && pressed && _b.Phase == BreathLogic.Ready) _b.Start();
            _b.Tick(dt, held);

            // draw
            float fill = (float)_b.Fill;
            if (_reduce) _discImg.fillAmount = fill;
            else { float k = 0.3f + 0.7f * HudMotion.OutCubic(fill); _disc.localScale = new Vector3(k, k, 1); }
            var count = Digits[Mathf.Clamp(_b.Count4, 0, 4)];
            if (!ReferenceEquals(_count.text, count)) _count.text = count;
            var cue = _b.Cue;
            if (_cue.text != cue) _cue.text = cue;
            var hint = _b.Hint ?? "";
            if (_hint.text != hint) _hint.text = hint;
            if (_lastPhase != _b.Phase)
            {
                _lastPhase = _b.Phase;
                Refresh();
                Say(_b.Phase == BreathLogic.In ? "들이쉬세요" : _b.Phase == BreathLogic.Out ? "내쉬세요" : _b.Phase == BreathLogic.Done ? "잘했어요. 엔터로 돌아갑니다." : null);
            }
        }
    }

    /// <summary>The breathing circle's hit area: takes the mouse and touch, draws nothing.</summary>
    public sealed class BreatheHitArea : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
