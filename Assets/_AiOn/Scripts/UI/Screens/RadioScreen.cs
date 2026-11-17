using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// Portable radio or the phone's FM app (original ui/radio.js): switch it on, pull out the antenna (the phone needs
    /// earphones), then turn the tuning knob (drag it round, scroll on it or on the dial, ←/→ with Shift for fine steps,
    /// PgUp/PgDn, Home/End) while the needle crawls along the dial. Signal bars, waveform and the static loop follow the
    /// clarity; tuned in, the broadcast arrives line by line as captions and '메모' keeps a line.
    /// Options { mode, stations: [{ freq, name, kind, lines }], start, battery, extraSeconds, device, onSignal? } ->
    /// { station, official, heard, noted: [{ text, kind }] }. The state machine is RadioTuner (RadioLogic.cs).
    /// </summary>
    public class RadioScreen : UiScreen
    {
        const float W = 1160, H = 1010;
        const float FaceW = 914;                // radio face width (dial, wave)
        const float DialScale = FaceW / 720f;   // the original's 720x112 dial canvas, scaled to the face
        const float WaveW = FaceW, WaveH = 70;

        static readonly Color AmberLcd = Hex("#ffc861");
        static readonly Color LcdBg = Hex("#120e06");
        static readonly Color BodyBg = Hex("#2a2c2f");
        static readonly Color Needle = Hex("#ff4a35");
        static readonly Color PrimaryBg = Hex("#f2b33d");
        static readonly Color PrimaryFg = Hex("#1b1406");
        static readonly Color Ghost = new(1, 1, 1, 0.08f);

        RadioTuner _t;
        System.Action<double?> _onSignal;

        // faceplate
        Image _led, _dialBg, _glow, _needle, _knob;
        RectTransform _dialArea, _wave, _knobRt;
        readonly List<Graphic> _tickInk = new();
        readonly List<Image> _bars = new();
        readonly List<Image> _waveDots = new();
        Text _readout, _sigText, _status, _capWho, _capText, _powerText, _antText;
        Image _loadTrack, _loadFill;
        Button _powerBtn;
        RectTransform _transcript;
        ScrollRect _transcriptScroll;
        bool _helpOpen;
        RectTransform _help;
        float _loadStart;
        bool _scrollDown;
        readonly System.Random _noise = new();

        /// <summary>Opens the radio (original ui.radio). onSignal gets the clarity while on and null when off/closed.</summary>
        public static async Task<JToken> Open(JObject options, System.Action<double?> onSignal)
        {
            var opts = RadioOptions.Parse(options);
            var r = await Show<RadioScreen>(s => s.Setup(opts, onSignal), W, H);
            return r as JToken ?? JValue.CreateNull();
        }

        public static Task<JToken> Open(JObject options) => Open(options, null);

        /// <summary>Registers "radio". The static loop ("radio" cue) follows the signal unless options.onSignal is false.</summary>
        public static void Register(UnityStoryHost host)
        {
            host.Screens["radio"] = o =>
            {
                bool sound = o == null || o["onSignal"] == null || RadioLogic.Truthy(o["onSignal"]);
                System.Action<double?> hook = null;
                if (sound)
                {
                    bool? last = null;
                    hook = c =>
                    {
                        bool on = c.HasValue;
                        if (last == on && !on) return;
                        last = on;
                        host.Loop("radio", on, on ? new JObject { ["clarity"] = c.Value } : null);
                    };
                }
                return Open(o, hook);
            };
        }

        void Setup(RadioOptions opts, System.Action<double?> onSignal)
        {
            _t = new RadioTuner(opts);
            _onSignal = onSignal;
            _helpOpen = opts.Mode == "guided";
        }

        static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }

        // ---------- layout ----------

        protected override void Build()
        {
            var win = Window;
            UiKit.At(UiKit.Label("Eyebrow", win, _t.Eyebrow, 20, UiKit.Amber, style: FontStyle.Bold).rectTransform, 40, 24, 800, 28);
            UiKit.At(UiKit.Label("Title", win, _t.Title, 36, UiKit.Ink, style: FontStyle.Bold).rectTransform, 40, 50, 900, 48);
            if (RadioLogic.ModeText.TryGetValue(_t.Mode, out var modeText))
            {
                var col = _t.Mode == "guided" ? UiKit.Teal : _t.Mode == "hint" ? UiKit.Amber : UiKit.Dim;
                var chip = UiKit.Box("Mode", win, new Color(col.r, col.g, col.b, 0.14f)).rectTransform;
                UiKit.At(chip, W - 40 - 150, 36, 150, 40);
                UiKit.Fill(UiKit.Label("Text", chip, modeText, 20, col, TextAnchor.MiddleCenter).rectTransform);
            }
            _help = UiKit.Rect("Help", win); UiKit.At(_help, 40, 106, W - 80, 100);
            Help();

            // Radio body: speaker grille | face
            var body = UiKit.Box("Body", win, BodyBg).rectTransform;
            UiKit.At(body, 40, 214, W - 80, 404);
            var spk = UiKit.Box("Speaker", body, Hex("#1d1e20")).rectTransform;
            UiKit.At(spk, 18, 18, 150, 368);
            for (int y = 0; y < 30; y++)
                for (int x = 0; x < 12; x++)
                    UiKit.At(UiKit.Box("Hole", spk, Hex("#0c0d0e")).rectTransform, 12 + x * 11.5f, 12 + y * 11.8f, 4, 4);

            var face = UiKit.Rect("Face", body); UiKit.At(face, 186, 18, FaceW, 368);

            // LCD: led, frequency, MHz, signal bars + word
            var lcd = UiKit.Box("Lcd", face, LcdBg).rectTransform; UiKit.At(lcd, 0, 0, FaceW, 64);
            _led = UiKit.Box("Led", lcd, Hex("#3b1a14")); UiKit.At(_led.rectTransform, 14, 27, 10, 10);
            _readout = UiKit.Label("Freq", lcd, "", 40, AmberLcd, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(_readout.rectTransform, 34, 4, 150, 56);
            UiKit.At(UiKit.Label("Unit", lcd, "MHz", 20, AmberLcd, TextAnchor.MiddleLeft).rectTransform, 186, 18, 60, 36);
            for (int i = 0; i < 5; i++)
            {
                float bh = 22 * (i + 1) / 5f;
                var b = UiKit.Box("Bar" + i, lcd, AmberLcd);
                UiKit.At(b.rectTransform, FaceW - 330 + i * 7, 20 + (22 - bh), 5, bh);
                _bars.Add(b);
            }
            _sigText = UiKit.Label("Signal", lcd, "", 20, Hex("#e9c27a"), TextAnchor.MiddleLeft);
            UiKit.At(_sigText.rectTransform, FaceW - 288, 14, 280, 36);

            // Waveform
            _wave = UiKit.Box("Wave", face, LcdBg).rectTransform; UiKit.At(_wave, 0, 74, WaveW, WaveH);
            _wave.gameObject.AddComponent<RectMask2D>();
            for (float x = 0; x <= WaveW; x += 6)
            {
                var d = UiKit.Box("W", _wave, AmberLcd);
                UiKit.At(d.rectTransform, x, WaveH / 2, 3, 2);
                _waveDots.Add(d);
            }

            // Dial (the original 720x112 canvas, scaled up)
            _dialArea = UiKit.Rect("Dial", face); UiKit.At(_dialArea, 0, 154, 720, 112);
            _dialArea.localScale = new Vector3(DialScale, DialScale, 1);
            BuildDial();
            var dialInput = _dialArea.gameObject.AddComponent<RadioKnobInput>();
            dialInput.Scroll = up => { _t.Wheel(up); Render(); };

            // Knob row: power, antenna, label, knob
            var row = UiKit.Rect("KnobRow", face); UiKit.At(row, 0, 300, FaceW, 68);
            _powerBtn = Action(row, _t.PowerLabel, false, () => { _t.TogglePower(); Render(); }, 0, 6, 260, 56);
            _powerText = _powerBtn.GetComponentInChildren<Text>();
            var ant = Action(row, _t.AntennaLabel, false, () => { _t.ToggleAntenna(); Render(); }, 274, 6, 220, 56);
            _antText = ant.GetComponentInChildren<Text>();
            UiKit.At(UiKit.Label("TuningLabel", row, "TUNING · 돌리기", 18, Hex("#8d9296"), TextAnchor.MiddleRight).rectTransform, FaceW - 330, 14, 250, 40);
            _knob = UiKit.Box("Knob", row, Hex("#45494d"), raycast: true);
            _knobRt = _knob.rectTransform;
            _knobRt.anchorMin = _knobRt.anchorMax = new Vector2(0, 1);
            _knobRt.pivot = new Vector2(0.5f, 0.5f);
            _knobRt.anchoredPosition = new Vector2(FaceW - 36, -34);
            _knobRt.sizeDelta = new Vector2(64, 64);
            var rim = UiKit.Box("Cap", _knobRt, Hex("#2d3033")).rectTransform; UiKit.Fill(rim, 7, 7);
            var mark = UiKit.Box("Mark", _knobRt, UiKit.Amber).rectTransform;
            mark.anchorMin = mark.anchorMax = new Vector2(0.5f, 1); mark.pivot = new Vector2(0.5f, 1);
            mark.anchoredPosition = new Vector2(0, -5); mark.sizeDelta = new Vector2(4, 16);
            var knobInput = _knob.gameObject.AddComponent<RadioKnobInput>();
            knobInput.Scroll = up => { _t.Wheel(up); Render(); };
            knobInput.Down = _ => _t.KnobDown();
            knobInput.Turn = d => { _t.KnobTurn(d); Render(); };
            knobInput.Up = () => { _t.KnobUp(); Render(); };

            // Status (with the battery-loading bar), caption, transcript
            _status = UiKit.Label("Status", win, "", 22, Hex("#ffd58a"), TextAnchor.MiddleLeft);
            UiKit.At(_status.rectTransform, 40, 626, 700, 34);
            _loadTrack = UiKit.Box("LoadTrack", win, Ghost);
            UiKit.At(_loadTrack.rectTransform, 420, 638, 180, 10);
            _loadFill = UiKit.Box("Fill", _loadTrack.transform, UiKit.Amber);
            _loadFill.rectTransform.anchorMin = Vector2.zero; _loadFill.rectTransform.anchorMax = new Vector2(0, 1);
            _loadFill.rectTransform.offsetMin = _loadFill.rectTransform.offsetMax = Vector2.zero;

            var cap = UiKit.Box("Caption", win, new Color(0, 0, 0, 0.45f)).rectTransform;
            UiKit.At(cap, 40, 664, W - 80, 100);
            _capWho = UiKit.Label("Who", cap, "", 19, UiKit.Dim);
            UiKit.At(_capWho.rectTransform, 16, 10, W - 112, 28);
            _capText = UiKit.Label("Text", cap, "", 25, UiKit.Ink);
            UiKit.At(_capText.rectTransform, 16, 40, W - 112, 58);

            UiKit.At(UiKit.Label("SubHead", win, "들은 내용", 22, UiKit.Ink, style: FontStyle.Bold).rectTransform, 40, 772, 400, 30);
            var trBox = UiKit.Rect("TranscriptBox", win); UiKit.At(trBox, 40, 806, W - 80, 116);
            _transcript = UiKit.ScrollList("Transcript", trBox, 6);
            _transcriptScroll = trBox.GetComponentInChildren<ScrollRect>();

            // Footer
            UiKit.At(UiKit.Label("Keys", win, RadioLogic.KeysHint, 17, UiKit.Dim, TextAnchor.MiddleLeft).rectTransform, 40, 938, W - 340, 52);
            var off = UiKit.Button("Off", win, _t.OffLabel, 24, PrimaryBg, PrimaryFg);
            UiKit.At((RectTransform)off.transform, W - 40 - 240, 934, 240, 58);
            off.onClick.AddListener(Close);

            // Buttons: no arrow-key navigation (arrows tune the dial), Enter/Space still press the selected one.
            foreach (var b in Window.GetComponentsInChildren<Button>(true)) b.navigation = new Navigation { mode = Navigation.Mode.None };

            if (_t.Begin() == 0) SelectPower();
            _loadStart = Time.unscaledTime;
            Render();
        }

        void BuildDial()
        {
            const float dw = 720, dh = 112;
            _dialBg = UiKit.Box("Bg", _dialArea, Hex("#2a2927"), raycast: true); // takes the wheel
            UiKit.At(_dialBg.rectTransform, 0, 0, dw, dh);
            _glow = UiKit.Box("Glow", _dialArea, new Color(1f, 0.78f, 0.43f, 0.16f));
            UiKit.At(_glow.rectTransform, 0, 8, 160, dh - 16);
            for (int i = 0; i <= 40; i++)
            {
                double f = 88 + i * 0.5;
                float x = (float)RadioLogic.DialX(f, dw);
                bool major = i % 4 == 0, mid = i % 2 == 0;
                var tick = UiKit.Box("Tick", _dialArea, Color.white);
                UiKit.At(tick.rectTransform, x - 1, 16, 2, major ? 24 : mid ? 15 : 8);
                _tickInk.Add(tick);
                if (major)
                {
                    var l = UiKit.Label("Num", _dialArea, RadioLogic.FreqText(f), 19, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiKit.At(l.rectTransform, x - 40, 44, 80, 26);
                    _tickInk.Add(l);
                }
            }
            var fm = UiKit.Label("FM", _dialArea, "FM", 15, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(fm.rectTransform, 8, 84, 60, 22); _tickInk.Add(fm);
            var mhz = UiKit.Label("MHz", _dialArea, "MHz", 15, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.At(mhz.rectTransform, dw - 68, 84, 60, 22); _tickInk.Add(mhz);
            UiKit.At(UiKit.Box("Base", _dialArea, new Color(1, 1, 1, 0.12f)).rectTransform, 30, 81, dw - 60, 2);
            _needle = UiKit.Box("Needle", _dialArea, Needle);
            UiKit.At(_needle.rectTransform, 0, 8, 3, dh - 16);
            UiKit.At(UiKit.Box("Glass", _dialArea, new Color(1, 1, 1, 0.07f)).rectTransform, 0, 0, dw, dh * 0.45f);
        }

        void Help()
        {
            Clear(_help);
            if (_t.Mode != "guided" && _t.Mode != "hint") return;
            float y = 0;
            if (_t.Mode == "hint")
            {
                var b = UiKit.Button("HintToggle", _help, _helpOpen ? "힌트 닫기" : "힌트 보기", 20, Ghost, UiKit.Ink);
                UiKit.At((RectTransform)b.transform, 0, 0, 150, 40);
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(() => { _helpOpen = !_helpOpen; Help(); });
                y = 46;
            }
            if (!_helpOpen) return;
            var lines = _t.HelpLines();
            var box = UiKit.Box("Note", _help, new Color(0.24f, 0.78f, 0.71f, 0.08f)).rectTransform;
            UiKit.At(box, 0, y, W - 80, 100 - y);
            UiKit.At(UiKit.Box("Edge", box, UiKit.Teal).rectTransform, 0, 0, 3, 100 - y);
            var text = UiKit.Label("Text", box, $"{lines[0]}\n<size=17><color=#9aa6ad>{lines[1]}</color></size>", y > 0 ? 17 : 20, UiKit.Ink);
            UiKit.Fill(text.rectTransform, 14, 6);
        }

        void SelectPower()
        {
            if (_powerBtn && EventSystem.current) EventSystem.current.SetSelectedGameObject(_powerBtn.gameObject);
        }

        // ---------- render ----------

        void Render()
        {
            if (_t == null || _t.Closed) return;
            bool on = _t.Power;
            _readout.text = _t.Readout;
            _led.color = on ? Hex("#ff5a3c") : Hex("#3b1a14");
            for (int i = 0; i < _bars.Count; i++) _bars[i].color = _t.BarOn(i) ? AmberLcd : new Color(1f, 0.78f, 0.38f, 0.2f);
            _sigText.text = _t.SignalText;
            _powerText.text = _t.PowerLabel;
            _antText.text = _t.AntennaLabel;
            _knobRt.localEulerAngles = new Vector3(0, 0, -(float)_t.KnobDegrees); // clockwise as in the original

            // dial
            _dialBg.color = on ? Hex("#5a4113") : Hex("#2a2927");
            var ink = on ? Hex("#ffd79a") : Hex("#77736c");
            foreach (var g in _tickInk) g.color = ink;
            float nx = (float)RadioLogic.DialX(_t.Freq, 720);
            _needle.rectTransform.anchoredPosition = new Vector2(nx - 1.5f, -8);
            _needle.color = on ? Hex("#ff5a3c") : Needle;
            _glow.gameObject.SetActive(on);
            _glow.rectTransform.anchoredPosition = new Vector2(nx - 80, -8);

            // status + caption
            _status.text = _t.Status;
            _loadTrack.gameObject.SetActive(_t.LoadingSeconds > 0);
            var who = _t.CaptionName == "" ? "" : $"<b>{_t.CaptionName}</b>";
            if (_t.CaptionPill != null)
            {
                var (icon, text) = RadioLogic.Pill(_t.CaptionPill);
                var col = _t.CaptionPill == "official" ? "#3cc6b5" : "#b9c0c3";
                who += $"   <color={col}>{icon} {text}</color>";
            }
            _capWho.text = who;
            _capText.text = _t.CaptionText;
            NotifySignal();
        }

        void NotifySignal()
        {
            try { _onSignal?.Invoke(_t.SignalValue); } catch (Exception e) { Debug.LogWarning($"[Radio] onSignal: {e.Message}"); } // audio is optional
        }

        void AddTranscriptRow(RadioHeard h)
        {
            var row = UiKit.Box("Line", _transcript, UiKit.Row);
            var fit = row.gameObject.AddComponent<VerticalLayoutGroup>();
            fit.padding = new RectOffset(12, 150, 8, 8);
            fit.childControlWidth = true; fit.childForceExpandWidth = true;
            fit.childControlHeight = true; fit.childForceExpandHeight = false;
            UiKit.Label("Text", row.transform, $"<size=17><color=#ffc861>{RadioLogic.FreqText(h.Station.Freq)}MHz</color></size>   {h.Text}", 21, UiKit.Ink);
            var memo = UiKit.Button("Memo", row.transform, "✎ 메모", 18, Ghost, UiKit.Ink);
            memo.navigation = new Navigation { mode = Navigation.Mode.None };
            var mrt = (RectTransform)memo.transform;
            memo.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1, 0.5f);
            mrt.anchoredPosition = new Vector2(-10, 0);
            mrt.sizeDelta = new Vector2(126, 38);
            var label = memo.GetComponentInChildren<Text>();
            var bg = memo.GetComponent<Image>();
            memo.onClick.AddListener(() =>
            {
                if (_t.Closed) return;
                bool kept = _t.ToggleNote(h);
                label.text = kept ? "✓ 메모함" : "✎ 메모";
                bg.color = kept ? new Color(0.95f, 0.7f, 0.24f, 0.22f) : Ghost;
            });
            _scrollDown = true;
        }

        void DrawWave()
        {
            bool on = _t.Power;
            float t = Time.unscaledTime;
            for (int i = 0; i < _waveDots.Count; i++)
            {
                var d = _waveDots[i];
                if (!on) { if (d.enabled) d.enabled = false; continue; }
                d.enabled = true;
                double x = i * 6 * (360.0 / WaveW); // the original's 360px wave, stretched
                double off = RadioLogic.WaveOffset(x, t, _t.Clarity, _t.Current != null, WaveH, _noise.NextDouble() * 2 - 1);
                var rt = d.rectTransform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -(WaveH / 2 + (float)off) + 1);
            }
        }

        // ---------- frame ----------

        protected override void Update()
        {
            base.Update();
            if (_t == null || _t.Closed) return;
            bool wasReady = _t.CanPower;
            var heard = _t.Tick(Time.unscaledDeltaTime);
            if (heard != null) AddTranscriptRow(heard);
            if (!wasReady && _t.CanPower) SelectPower();
            if (_t.LoadingSeconds > 0)
                _loadFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((Time.unscaledTime - _loadStart) / (float)_t.LoadingSeconds), 1);
            _status.text = _t.Status;
            _loadTrack.gameObject.SetActive(_t.LoadingSeconds > 0);
            _capText.text = _t.CaptionText;
            DrawWave();
            if (_scrollDown && _transcriptScroll)
            {
                Canvas.ForceUpdateCanvases();
                _transcriptScroll.verticalNormalizedPosition = 0;
                _scrollDown = false;
            }
            Keys();
        }

        void Keys()
        {
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb == null || !_t.Power) return;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            double fine = RadioTuner.ArrowStep(shift);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) _t.Nudge(fine);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) _t.Nudge(-fine);
            else if (kb.pageUpKey.wasPressedThisFrame) _t.Nudge(1);
            else if (kb.pageDownKey.wasPressedThisFrame) _t.Nudge(-1);
            else if (kb.homeKey.wasPressedThisFrame) _t.SetFreq(RadioLogic.Min);
            else if (kb.endKey.wasPressedThisFrame) _t.SetFreq(RadioLogic.Max);
            else return;
            Render();
        }

        void Close()
        {
            if (_t.Closed) return;
            var result = _t.Result();
            _t.Close();
            try { _onSignal?.Invoke(null); } catch (Exception e) { Debug.LogWarning($"[Radio] onSignal: {e.Message}"); }
            Finish(result);
        }

        /// <summary>Esc switches it off and returns what was heard, like '라디오 끄기'.</summary>
        protected override object EscapeResult()
        {
            if (_t == null) return null;
            var result = _t.Result();
            if (!_t.Closed)
            {
                _t.Close();
                try { _onSignal?.Invoke(null); } catch (Exception e) { Debug.LogWarning($"[Radio] onSignal: {e.Message}"); }
            }
            return result;
        }
    }

    /// <summary>Pointer input for the tuning knob and the dial: drag round the centre (clockwise positive radians) and scroll.</summary>
    public class RadioKnobInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
    {
        public Func<PointerEventData, bool> Down;
        public Action<double> Turn;
        public Action Up;
        public Action<bool> Scroll;
        float? _angle;

        float AngleOf(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
            var rt = (RectTransform)transform;
            var c = rt.rect.center;
            return Mathf.Atan2(-(p.y - c.y), p.x - c.x); // screen-down positive, like the browser's clientY
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Down == null || !Down(e)) return;
            // Measure against the parent so the knob's own rotation does not feed back into the angle.
            _angle = ParentAngle(e);
        }

        float ParentAngle(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (!parent) return AngleOf(e);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var p);
            Vector2 c = rt.localPosition;
            return Mathf.Atan2(-(p.y - c.y), p.x - c.x);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_angle == null) return;
            float a = ParentAngle(e);
            double d = a - _angle.Value;
            _angle = a;
            Turn?.Invoke(d);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_angle == null) return;
            _angle = null;
            Up?.Invoke();
        }

        public void OnScroll(PointerEventData e)
        {
            if (Scroll == null || e.scrollDelta.y == 0) return;
            Scroll(e.scrollDelta.y > 0);
        }
    }
}
