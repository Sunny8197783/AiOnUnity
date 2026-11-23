// The CPR rhythm minigame (competition upgrade, 2장 대피소). Left: the patient seen from where 정민 kneels, with the
// first-person hands drawn as UI (no body animation), the beat ring and the depth meter; right: the step, the choices,
// the quality gauge (what the rescuer *feels*), the swap and AED buttons. Pages: 조작 안내 -> 반응 확인 -> 도움 요청 ->
// 호흡 확인 -> 가슴압박 (with the AED inside) -> 구급대 인계 -> 돌아보기 (the true quality curve over the gauge).
//
// Timing: the beat grid and every press are on AudioSettings.dspTime (estimated between audio buffer updates with the
// unscaled real time), never Time.time, so DecisionMoment or a time scale cannot slow it; a hitch or a pause shifts the
// clock instead of counting as missed beats. The metronome is our own procedural click scheduled with PlayScheduled.
// The rules live in AiOn.Game.CprLogic; this class only draws and forwards input. Returns CprResult.ToJson(curve: true).
using System.Collections.Generic;
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
    public class CprScreen : UiScreen
    {
        public const string Name = "cpr";

        public static void Register(UnityStoryHost host) => host.Screens[Name] = Open;

        /// <summary>ui.cpr({ stage, profile, reduceMotion, demo, facts: { id: { text, source } } }) -> CprResult json.</summary>
        public static async Task<JToken> Open(JObject o)
        {
            CprScreen me = null;
            var r = await Show<CprScreen>(s => { s._o = o ?? new JObject(); me = s; });
            if (r is JToken t) return t;
            return me != null && me.L != null ? me.L.Result().ToJson(true) : new JObject();
        }

        enum Page { Intro, Steps, Compress, Handover, Debrief }

        JObject _o;
        CprLogic L;
        Page _page;
        string _profile;
        bool _reduce, _wide, _oneButton, _vibrate, _deaf, _lowVision, _demo;

        const float W = 1720, H = 940;
        static readonly Color Floor = new(0.085f, 0.1f, 0.11f, 1f);
        static readonly Color Mat = new(0.2f, 0.26f, 0.3f, 1f);
        static readonly Color Shirt = new(0.42f, 0.47f, 0.52f, 1f);
        static readonly Color Skin = new(0.86f, 0.7f, 0.58f, 1f);
        static readonly Color Sleeve = new(0.24f, 0.33f, 0.4f, 1f);
        static readonly Color Ghost = new(1, 1, 1, 0.07f);
        static readonly Color Dark = new(0.04f, 0.08f, 0.08f);

        RectTransform _stage, _side;
        Vector2 _handsBase;
        Image[] _sleeves;
        string _handsOf;
        static readonly Color NeighbourSleeve = new(0.2f, 0.36f, 0.72f, 1f);  // 파란 점퍼
        static readonly Color VolunteerSleeve = new(0.85f, 0.7f, 0.18f, 1f); // 노란 조끼 (the arms of a light top)
        RectTransform _hands, _ring, _point, _padA, _padB, _edge, _torso;
        Image _ringImg, _edgeImg, _depthFill, _gaugeFill, _beatDot;
        RectTransform _depthFillRt, _gaugeFillRt;
        Text _title, _kicker, _count, _feedback, _timer, _who, _hint, _aedLabel, _gaugeWord, _depthWord;
        Button _swap, _vague, _takeBack, _aedButton, _skip;
        Text _aedButtonText;
        readonly List<GameObject> _compressOnly = new();

        // clock
        double _origin, _paused, _lastDsp = -1, _dspAt, _realAt, _lastClock;
        // input
        bool _down, _mouseHold, _touchHold;
        // view state
        int _seenFeedback = -1, _seenPress = -1, _shownSecond = -1, _shownCount = -1;
        CprPhase _shownPhase = (CprPhase)(-1);
        float _press, _beatFlash;
        int _lastVisualBeat = int.MinValue, _schedBeat = int.MinValue;
        readonly AudioSource[] _src = new AudioSource[2]; // the metronome's two (scheduled clicks alternate)
        AudioSource _cueSrc;                               // the AED's tones: never stops a scheduled click
        int _flip;
        float _rumbleUntil;
        HostSettingsProxy _speech;

        // ---------------------------------------------------------------- build

        protected override object EscapeResult() => null; // it always ends with the paramedics taking over

        protected override void Build()
        {
            _profile = (string)_o["profile"] ?? "";
            _reduce = _o["reduceMotion"]?.Type == JTokenType.Boolean && (bool)_o["reduceMotion"];
            _deaf = _profile == "deaf";
            _lowVision = _profile == "lowvision";
            _demo = _o["demo"]?.Type == JTokenType.Boolean && (bool)_o["demo"];
            _wide = _profile.Length > 0 && _profile != "none";
            _oneButton = _profile == "mobility" || _profile == "wheelchair" || _profile == "guardian";
            _speech = new HostSettingsProxy();
            for (int i = 0; i < 2; i++)
            {
                _src[i] = gameObject.AddComponent<AudioSource>();
                _src[i].playOnAwake = false; _src[i].spatialBlend = 0; _src[i].ignoreListenerPause = true;
                _src[i].volume = 0.55f * AiOn.Settings.AudioBus.Ui;
            }
            _cueSrc = gameObject.AddComponent<AudioSource>();
            _cueSrc.playOnAwake = false; _cueSrc.spatialBlend = 0; _cueSrc.ignoreListenerPause = true;
            _cueSrc.volume = 0.55f * AiOn.Settings.AudioBus.Ui;
            _kicker = UiKit.Label("Kicker", Window, CprText.Kicker, 22, UiKit.Dim);
            UiKit.At(_kicker.rectTransform, 40, 26, 900, 30);
            _title = UiKit.Label("Title", Window, "", 40, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_title.rectTransform, 40, 52, 1300, 56);
            _stage = UiKit.Rect("Stage", Window);
            UiKit.At(_stage, 40, 124, 820, 776);
            _side = UiKit.Rect("Side", Window);
            UiKit.At(_side, 900, 124, 780, 776);
            BuildStage();
            ShowIntro();
            _origin = RawClock();
        }

        static Image Rounded(string name, Transform parent, Color c)
        {
            var img = UiKit.Box(name, parent, c);
            img.sprite = HudDraw.RoundedSprite; img.type = Image.Type.Sliced;
            return img;
        }

        static Image Disc(string name, Transform parent, Color c, Sprite s = null)
        {
            var img = UiKit.Box(name, parent, c);
            img.sprite = s ? s : HudDraw.DiscSprite; img.preserveAspect = true;
            return img;
        }

        /// <summary>Placed by its centre (x right, y down from the parent's top-left), pivot in the middle so it scales and turns in place.</summary>
        static RectTransform Centered(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Where the hands go in the flat drawing: the lower half of the breastbone (the torso runs 200-640, the collar is at 214).</summary>
        const float ChestY = 330;

        /// <summary>The rendered set (UI/Studio/CprStudio: the Rocketbox grandfather and real arms); null falls back to the flat drawing.</summary>
        AiOn.UI.Studio.CprStudio _studio;
        float _chestX = 380, _chestY = ChestY;

        void BuildStage()
        {
            _stage.gameObject.AddComponent<RectMask2D>(); // the forearms run off the bottom edge
            _studio = AiOn.UI.Studio.CprStudio.Create(this, AiOn.UI.Studio.MiniStudio.PixelsFor(Root.GetComponent<Canvas>(), new Vector2(820, 776)), _reduce);
            if (_studio) BuildStudioStage(); else BuildFlatStage();
            BuildStageOverlay();
        }

        /// <summary>The rendered picture: the set's view under the overlay, the beat ring over the hands' spot as the camera sees it.</summary>
        void BuildStudioStage()
        {
            _studio.Studio.Show(_stage);
            _studio.Frame(true); // the overlay sits where the pressing framing shows his chest
            var v = _studio.ChestOnView;
            _studio.Frame(false); // until someone presses: him alone, centred
            _chestX = v.x * 820; _chestY = (1 - v.y) * 776;
            _point = Centered(Disc("Point", _stage, new Color(1, 1, 1, 0.12f), HudDraw.RingSprite).rectTransform, _chestX, _chestY, 62, 62);
            _point.gameObject.SetActive(false); // placed for the pressing framing: shown with it
            _padA = Centered(UiKit.Rect("PadA", _stage), 0, 0, 1, 1); _padB = Centered(UiKit.Rect("PadB", _stage), 0, 0, 1, 1); // the pads are in the set
            _padA.gameObject.SetActive(false); _padB.gameObject.SetActive(false);
            _ring = Centered(Disc("BeatRing", _stage, UiKit.Teal, HudDraw.RingSprite).rectTransform, _chestX, _chestY, 200, 200);
            _ringImg = _ring.GetComponent<Image>();
            _hands = Centered(UiKit.Rect("Hands", _stage), _chestX, _chestY, 10, 10); // the arms are in the set
            _handsBase = _hands.anchoredPosition;
            _sleeves = new Image[0];
            _hands.gameObject.SetActive(false); _ring.gameObject.SetActive(false);
        }

        void BuildFlatStage()
        {
            UiKit.Fill(Rounded("Floor", _stage, Floor).rectTransform);
            Centered(Rounded("Mat", _stage, Mat).rectTransform, 380, 400, 560, 720);
            // the patient, head up the screen (his right side is on our left)
            Centered(Disc("Hair", _stage, new Color(0.72f, 0.72f, 0.7f, 1)).rectTransform, 380, 104, 158, 158);
            Centered(Disc("Head", _stage, Skin * 0.92f + new Color(0, 0, 0, 0.08f)).rectTransform, 380, 130, 140, 140);
            _torso = Centered(Rounded("Torso", _stage, Shirt).rectTransform, 380, 420, 430, 440);
            Centered(Rounded("Collar", _stage, Shirt * 0.8f + new Color(0, 0, 0, 0.2f)).rectTransform, 380, 214, 150, 28);
            Centered(Rounded("ArmL", _stage, Shirt * 0.9f + new Color(0, 0, 0, 0.1f)).rectTransform, 128, 430, 80, 400);
            Centered(Rounded("ArmR", _stage, Shirt * 0.9f + new Color(0, 0, 0, 0.1f)).rectTransform, 632, 430, 80, 400);
            // the centre of the chest (the lower half of the breastbone): about a third of the way down from the collar
            _point = Centered(Disc("Point", _stage, new Color(1, 1, 1, 0.16f), HudDraw.RingSprite).rectTransform, 380, ChestY, 70, 70);
            _padA = Centered(Rounded("PadA", _stage, new Color(0.93f, 0.93f, 0.9f, 1)).rectTransform, 262, 246, 96, 64);
            _padB = Centered(Rounded("PadB", _stage, new Color(0.93f, 0.93f, 0.9f, 1)).rectTransform, 520, 515, 64, 96);
            _padA.gameObject.SetActive(false); _padB.gameObject.SetActive(false);

            _ring = Centered(Disc("BeatRing", _stage, UiKit.Teal, HudDraw.RingSprite).rectTransform, 380, ChestY, 230, 230);
            _ringImg = _ring.GetComponent<Image>();
            _hands = Centered(UiKit.Rect("Hands", _stage), 380, ChestY, 10, 10);
            _handsBase = _hands.anchoredPosition;
            // first person, kneeling at his side: both arms straight and parallel, in from the lower left to the hands stacked on the chest
            var sl = Rounded("ForearmL", _hands, Sleeve); var fl = sl.rectTransform; Centered(fl, 5 - 178, 5 + 156, 76, 420); fl.localRotation = Quaternion.Euler(0, 0, -35);
            var sr = Rounded("ForearmR", _hands, Sleeve); var fr = sr.rectTransform; Centered(fr, 5 - 86, 5 + 220, 76, 420); fr.localRotation = Quaternion.Euler(0, 0, -35);
            _sleeves = new[] { sl, sr };
            Centered(Rounded("PalmUnder", _hands, Skin * 0.95f + new Color(0, 0, 0, 0.05f)).rectTransform, 5, 5 + 8, 128, 100);
            Centered(Rounded("PalmOver", _hands, Skin).rectTransform, 5, 5 - 6, 136, 96);
            for (int i = 0; i < 4; i++) Centered(Rounded("Finger" + i, _hands, Skin * 0.97f + new Color(0, 0, 0, 0.03f)).rectTransform, 5 - 48 + i * 32, 5 - 62, 26, 50);
            _hands.gameObject.SetActive(false); _ring.gameObject.SetActive(false);
        }

        void BuildStageOverlay()
        {
            // depth meter (what the press felt like); the target band is 약 5cm
            if (_studio)
            {
                // a dark plate under the meter, the count and the beat dot, so they read over the picture
                var plate = Rounded("MeterPlate", _stage, new Color(0.03f, 0.05f, 0.06f, 0.72f)).rectTransform;
                UiKit.At(plate, 704, 186, 110, 466);
                var countPlate = Rounded("CountPlate", _stage, new Color(0.03f, 0.05f, 0.06f, 0.62f)).rectTransform;
                UiKit.At(countPlate, 16, 16, 190, 84);
                _compressOnly.Add(plate.gameObject); _compressOnly.Add(countPlate.gameObject);
            }
            var meter = Rounded("DepthMeter", _stage, new Color(1, 1, 1, _studio ? 0.14f : 0.08f)).rectTransform;
            UiKit.At(meter, 742, 230, 34, 380);
            var band = UiKit.Box("Target", meter, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.28f)).rectTransform;
            band.anchorMin = new Vector2(0, 0.75f); band.anchorMax = new Vector2(1, 0.92f); band.offsetMin = band.offsetMax = Vector2.zero;
            _depthFill = Rounded("Fill", meter, UiKit.Teal);
            _depthFillRt = _depthFill.rectTransform;
            _depthFillRt.anchorMin = Vector2.zero; _depthFillRt.anchorMax = new Vector2(1, 0); _depthFillRt.pivot = new Vector2(0.5f, 0);
            _depthFillRt.offsetMin = Vector2.zero; _depthFillRt.offsetMax = Vector2.zero;
            var lab = UiKit.Label("DepthLabel", _stage, "깊이", 18, UiKit.Dim, TextAnchor.MiddleCenter);
            UiKit.At(lab.rectTransform, 712, 196, 94, 28);
            _depthWord = UiKit.Label("DepthWord", _stage, "약 5cm", 16, UiKit.Teal, TextAnchor.MiddleCenter);
            UiKit.At(_depthWord.rectTransform, 712, 616, 94, 26);
            _compressOnly.Add(meter.gameObject); _compressOnly.Add(lab.gameObject); _compressOnly.Add(_depthWord.gameObject);

            _count = UiKit.Label("Count", _stage, "", 64, UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(_count.rectTransform, 28, 18, 300, 80);
            _beatDot = Disc("BeatDot", _stage, UiKit.Teal);
            UiKit.At(_beatDot.rectTransform, 744, 40, 30, 30);
            _compressOnly.Add(_count.gameObject); _compressOnly.Add(_beatDot.gameObject);

            // the visual metronome along the stage edge (strong in deaf mode)
            _edgeImg = UiKit.Box("BeatEdge", _stage, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0));
            _edgeImg.sprite = HudDraw.RoundedSprite; _edgeImg.type = Image.Type.Sliced; _edgeImg.fillCenter = false;
            _edge = UiKit.Fill(_edgeImg.rectTransform, -6, -6);
            foreach (var g in _compressOnly) g.SetActive(false);
        }

        // ---------------------------------------------------------------- pages

        void ClearSide()
        {
            Clear(_side);
            _feedback = _timer = _who = _hint = _aedLabel = _gaugeWord = null;
            _swap = _vague = _takeBack = _aedButton = _skip = null;
            _nav.Clear(); _navColors.Clear(); _cursor = -1;
            _gaugeFill = null;
            _choiceKeys.Clear();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null); // Space must not press a focused button
        }

        Text SideLabel(string name, string text, int size, Color c, float y, float h, FontStyle st = FontStyle.Normal)
        {
            var t = UiKit.Label(name, _side, text, size, c, style: st);
            UiKit.At(t.rectTransform, 0, y, 780, h);
            return t;
        }

        Button SideButton(string label, bool primary, System.Action onClick, float y, float h = 64, float x = 0, float w = 780)
        {
            var b = UiKit.Button(label, _side, label, 22, primary ? UiKit.Teal : Ghost, primary ? Dark : UiKit.Ink);
            UiKit.At((RectTransform)b.transform, x, y, w, h);
            var t = b.GetComponentInChildren<Text>(); t.alignment = TextAnchor.MiddleLeft;
            UiKit.Fill(t.rectTransform, 22, 4);
            b.onClick.AddListener(() => { UiSound.Play("click"); onClick(); });
            _nav.Add(b); _navColors.Add(b.GetComponent<Image>().color);
            return b;
        }

        Text Toggle(string label, bool on, float y, System.Action<bool> set)
        {
            Text t = null;
            bool state = on;
            string Mark() => (state ? "■  " : "□  ") + label;
            var b = SideButton(Mark(), false, () => { state = !state; t.text = Mark(); set(state); }, y, 56);
            t = b.GetComponentInChildren<Text>();
            return t;
        }

        string FactText(string id)
        {
            var f = _o["facts"]?[id];
            return f?["text"]?.Type == JTokenType.String ? EasyKorean.Of((string)f["text"]) : null; // 쉬운 한국어: the fact's plain wording
        }

        string FactSource(string id)
        {
            var f = _o["facts"]?[id];
            return f?["source"]?.Type == JTokenType.String ? (string)f["source"] : null;
        }

        const float FactH = 108;

        void FactBox(string id, float y)
        {
            var text = FactText(id);
            if (string.IsNullOrEmpty(text)) return;
            var src = FactSource(id);
            var box = Rounded("Fact_" + id, _side, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.1f)).rectTransform;
            UiKit.At(box, 0, y, 780, FactH);
            var t = UiKit.Label("Text", box, text, 19, UiKit.Ink);
            UiKit.At(t.rectTransform, 18, 10, 744, 50);
            if (src != null) { var s = UiKit.Label("Source", box, "출처: " + src, 14, UiKit.Dim); UiKit.At(s.rectTransform, 18, 62, 744, 40); }
        }

        void ShowIntro()
        {
            _page = Page.Intro;
            ClearSide();
            _kicker.text = "심폐소생술 · 조작 안내";
            _title.text = "두 손의 리듬";
            SideLabel("Body", CprText.ControlsLine, 22, UiKit.Ink, 0, 110);
            SideLabel("Note", "압박 속도는 분당 110회로 고정입니다. 판정을 넓혀도 박자는 느려지지 않습니다.", 19, UiKit.Dim, 116, 60);
            Toggle(CprText.WideLabel, _wide, 190, v => _wide = v);
            Toggle(CprText.OneButtonLabel, _oneButton, 256, v => _oneButton = v);
            Toggle(CprText.VibrateLabel, _vibrate, 322, v => _vibrate = v);
            SideLabel("OneButton", CprText.OneButtonLine, 18, UiKit.Dim, 390, 50);
            SideLabel("Practice", CprText.PracticeNote, 18, UiKit.Amber, 450, 56);
            var start = SideButton("시작 ▶", true, Begin, 700, 70);
            start.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            _choiceKeys.Add(Begin);
        }

        void Begin()
        {
            L = new CprLogic(_wide, _oneButton, (string)_o["stage"] ?? "guided");
            _kicker.text = CprText.Kicker;
            ShowStep();
        }

        readonly List<System.Action> _choiceKeys = new();

        void ShowStep(string feedbackId = null)
        {
            _page = Page.Steps;
            if (_studio) { _studio.Frame(false); if (_point) _point.gameObject.SetActive(false); }
            ClearSide();
            var p = L.Phase;
            _title.text = CprText.Title(p);
            string body = p == CprPhase.Check ? CprText.CheckBody : p == CprPhase.Call ? CprText.CallBody : CprText.BreathBody;
            SideLabel("Body", body, 22, UiKit.Ink, 0, 120);
            var choices = p == CprPhase.Check ? CprText.CheckChoices : p == CprPhase.Call ? CprText.CallChoices : CprText.BreathChoices;
            float y = 132;
            for (int i = 0; i < choices.Length; i++)
            {
                var (id, label) = choices[i];
                System.Action act = () => Choose(id);
                var b = SideButton($"{i + 1}  {label}", false, act, y, 74);
                b.GetComponentInChildren<Text>().fontSize = 21;
                _choiceKeys.Add(act);
                y += 84;
            }
            _feedback = SideLabel("Feedback", CprText.Feedback(feedbackId), 21, UiKit.Amber, y + 10, 90);
            if (p == CprPhase.Breath)
            {
                FactBox("cpr_breath", 600 - FactH - 8);
                FactBox("cpr_gasp", 600);
                _timer = SideLabel("Timer", "", 26, UiKit.Teal, y + 100, 40, FontStyle.Bold);
                _shownSecond = -1;
            }
            else FactBox(p == CprPhase.Check ? "cpr_check" : "cpr_point", 600);
            // low vision: the whole step in one go (what happened, the title, the situation, the numbered choices), never cut
            var spoken = new System.Text.StringBuilder();
            var fb = CprText.Feedback(feedbackId);
            if (fb.Length > 0) spoken.Append(fb).Append(' ');
            spoken.Append(_title.text).Append(". ").Append(body);
            for (int i = 0; i < choices.Length; i++) spoken.Append(' ').Append(i + 1).Append("번, ").Append(choices[i].label).Append('.');
            _speech.Say(spoken.ToString());
            // ...and the 10-second clock starts once it has been read (about 0.13 s a character): screen-reader time is never late
            if (p == CprPhase.Breath && _lowVision) L.DelayBreath(Mathf.Clamp(spoken.Length * 0.13f, 3f, 30f));
        }

        void Choose(string id)
        {
            double t = Clock();
            switch (L.Phase)
            {
                case CprPhase.Check: L.Check(id); break;
                case CprPhase.Call: L.Call(id, t); break;
                case CprPhase.Breath: L.Breath(id, t); break;
                default: return;
            }
            AfterStep();
        }

        void AfterStep()
        {
            if (L.Phase == CprPhase.Compress) { ShowCompress(); return; }
            bool moved = L.Phase != _shownPhase;
            _shownPhase = L.Phase;
            if (moved && L.Feedback is "checked" or "call_named") ShowStep(L.Feedback); // speaks the feedback with the new step
            else if (_feedback) { _feedback.text = CprText.Feedback(L.Feedback); _speech.Say(_feedback.text); }
        }

        void ShowCompress()
        {
            _page = Page.Compress;
            if (_studio) { _studio.Frame(true); if (_point) _point.gameObject.SetActive(true); }
            ClearSide();
            _title.text = CprText.Title(CprPhase.Compress);
            _hands.gameObject.SetActive(true); _ring.gameObject.SetActive(true);
            foreach (var g in _compressOnly) g.SetActive(true);
            SideLabel("Body", CprText.CompressBody, 20, UiKit.Dim, 0, 64);
            _feedback = SideLabel("Feedback", CprText.Feedback(L.Feedback), 30, UiKit.Ink, 70, 84, FontStyle.Bold);
            _timer = SideLabel("Timer", "", 22, UiKit.Dim, 160, 32);
            SideLabel("GaugeLabel", CprText.GaugeLabel, 19, UiKit.Dim, 200, 28);
            var track = Rounded("GaugeTrack", _side, new Color(1, 1, 1, 0.1f)).rectTransform;
            UiKit.At(track, 0, 232, 640, 34);
            _gaugeFill = Rounded("GaugeFill", track, UiKit.Teal);
            _gaugeFillRt = _gaugeFill.rectTransform;
            _gaugeFillRt.anchorMin = Vector2.zero; _gaugeFillRt.anchorMax = new Vector2(0, 1); _gaugeFillRt.pivot = new Vector2(0, 0.5f);
            _gaugeFillRt.offsetMin = _gaugeFillRt.offsetMax = Vector2.zero;
            _gaugeWord = UiKit.Label("GaugeWord", _side, "", 22, UiKit.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.At(_gaugeWord.rectTransform, 650, 232, 130, 34);
            _who = SideLabel("Who", "", 20, UiKit.Ink, 282, 56);
            _hint = SideLabel("Hint", "", 20, UiKit.Amber, 340, 56);
            _aedLabel = SideLabel("Aed", "", 22, UiKit.Ink, 400, 96, FontStyle.Bold);
            _aedButton = SideButton("", true, () => L.AedButton(Clock()), 500, 70);
            _aedImage = _aedButton.GetComponent<Image>();
            _logicPhase = CprPhase.Compress;
            _aedButtonText = _aedButton.GetComponentInChildren<Text>();
            _aedButtonText.alignment = TextAnchor.MiddleCenter;
            _swap = SideButton(CprText.SwapButton, false, () => L.RequestSwap(Clock(), true), 586, 60);
            _takeBack = SideButton(CprText.TakeBackButton, false, () => L.TakeBack(Clock()), 586, 60);
            _vague = SideButton(CprText.VagueSwapButton, false, () => L.RequestSwap(Clock(), false), 652, 56);
            _skip = SideButton(CprText.SkipButton, false, () => L.SkipToHandover(Clock()), 500, 70);
            _skip.gameObject.SetActive(false);
            string keys = _oneButton ? "Space · 클릭 · 터치: 누르기   길게: 교대 요청   Enter: AED 버튼" : "Space · 클릭 · 터치: 누르기   E: 교대 요청   Enter: AED 버튼";
            if (Gamepad.current != null) keys += "\n게임패드 A: 누르기   Y: 교대 요청   X: \"누가 좀\"   RB: AED 버튼";
            SideLabel("Keys", keys, 17, UiKit.Dim, 716, 52);
            _shownPhase = (CprPhase)(-1); _shownSecond = -1; _shownCount = -1;
            _seenPress = L.PressSeq; _depthWord.text = "약 5cm"; _depthWord.color = UiKit.Dim; _depthFillRt.anchorMax = new Vector2(1, 0);
            _schedBeat = -CprLogic.LeadInBeats;
            _speech.Say(CprText.CompressBody);
        }

        CprPhase _logicPhase;
        Image _aedImage;

        // The AED's sounds follow its steps (the deaf profile gets none: the steps are on screen).
        void PhaseCue(CprPhase from, CprPhase to)
        {
            switch (to)
            {
                case CprPhase.AedPower: break; // it has just been put down: no tone until the power is on
                case CprPhase.AedPads: Cue("power"); break;
                case CprPhase.AedAnalyze: _padA.gameObject.SetActive(true); _padB.gameObject.SetActive(true); Cue("analyze"); break;
                case CprPhase.AedShock: Cue("charge"); break;
                case CprPhase.Compress: if (from == CprPhase.AedShock) Cue("shock"); break;
            }
        }

        void ShowHandover()
        {
            _page = Page.Handover;
            ClearSide();
            StopRumble();
            _title.text = CprText.Title(CprPhase.Handover);
            _hands.gameObject.SetActive(false); _ring.gameObject.SetActive(false);
            foreach (var g in _compressOnly) g.SetActive(false);
            _edgeImg.color = new Color(0, 0, 0, 0);
            if (_studio) _studio.Drive("none", 0f, Vector2.zero, 0f); // the paramedics have him now
            SideLabel("Body", CprText.HandoverBody, 24, UiKit.Ink, 0, 170);
            SideLabel("Done", $"가슴압박 {CprLogic.Clock(L.CompressTime(L.EndAt))} · 압박 {L.Compressions}회 · 교대 {L.Swaps}번", 20, UiKit.Dim, 190, 40);
            var b = SideButton("돌아보기 ▶", true, ShowDebrief, 700, 70);
            b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            _choiceKeys.Add(ShowDebrief);
            _speech.Say(CprText.HandoverBody);
        }

        void ShowDebrief()
        {
            _page = Page.Debrief;
            ClearSide();
            var r = L.Result();
            var ev = CprLogic.Evaluate(r);
            _title.text = CprText.DebriefTitle;
            Clear(_stage);
            if (_studio) { Destroy(_studio.gameObject); _studio = null; } // the chart takes the whole left
            // the chart over the whole left: the true quality and what the gauge showed
            var chartBox = Rounded("ChartBox", _stage, new Color(1, 1, 1, 0.04f)).rectTransform;
            UiKit.At(chartBox, 0, 0, 820, 470);
            var chartRt = UiKit.Rect("Chart", chartBox);
            chartRt.gameObject.AddComponent<CanvasRenderer>();
            var chart = chartRt.gameObject.AddComponent<CprChart>();
            UiKit.Fill(chart.rectTransform, 56, 40);
            chart.raycastTarget = false;
            chart.Set(r.Curve, r.Seconds, r.ShallowAt);
            for (int i = 0; i <= 2; i++)
            {
                var yl = UiKit.Label("Y" + i, chartBox, (i * 50) + "%", 15, UiKit.Dim, TextAnchor.MiddleRight);
                UiKit.At(yl.rectTransform, 0, 470 - 40 - i * (470 - 80) / 2f - 12, 50, 24);
            }
            var xl = UiKit.Label("X", chartBox, "0초", 15, UiKit.Dim); UiKit.At(xl.rectTransform, 56, 436, 100, 24);
            var xr = UiKit.Label("XEnd", chartBox, CprLogic.Clock(r.Seconds), 15, UiKit.Dim, TextAnchor.UpperRight); UiKit.At(xr.rectTransform, 664, 436, 100, 24);
            Legend(chartBox, CprChart.TrueColor, CprText.ChartTrue, 56, 8);
            Legend(chartBox, CprChart.GaugeColor, CprText.ChartGauge, 300, 8);
            Legend(chartBox, CprChart.SwapColor, "이웃이 누른 구간", 560, 8);
            Legend(chartBox, new Color(1, 1, 1, 0.25f), "AED", 730, 8);
            if (r.ShallowAt >= 0)
            {
                float mx = 56 + (float)(r.ShallowAt / System.Math.Max(1, r.Seconds)) * (820 - 112);
                var ml = UiKit.Label("ShallowMark", chartBox, "얕아지기 시작", 15, UiKit.Red, TextAnchor.UpperRight);
                UiKit.At(ml.rectTransform, mx - 206, 470 - 40 - 30, 200, 22); // above the time axis, clear of the 100% lines
            }
            float y = 490;
            foreach (var line in CprLogic.DebriefLines(r))
            {
                var t = UiKit.Label("Line", _stage, line, 19, UiKit.Ink);
                UiKit.At(t.rectTransform, 0, y, 820, 60);
                y += t.preferredHeight > 30 ? 60 : 34;
            }
            // right: the judged rows, then the facts behind them
            float sy = 0;
            foreach (var row in ev.Rows)
            {
                var mark = UiKit.Label("Mark", _side, row.Ok ? "✓" : "!", 24, row.Ok ? UiKit.Teal : UiKit.Amber, TextAnchor.UpperCenter, FontStyle.Bold);
                UiKit.At(mark.rectTransform, 0, sy, 32, 40);
                var t = UiKit.Label("Row", _side, row.Label, 20, UiKit.Ink);
                UiKit.At(t.rectTransform, 40, sy + 2, 740, 56);
                sy += 58;
            }
            FactBox("cpr_fatigue", sy + 8);
            FactBox("cpr_fatigue_why", sy + 8 + FactH + 8);
            FactBox("cpr_practice", sy + 8 + 2 * (FactH + 8));
            var b = SideButton("계속 ▶", true, () => Finish(r.ToJson(true)), 706, 70);
            b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            _choiceKeys.Add(() => Finish(r.ToJson(true)));
            _speech.Say(string.Join(" ", CprLogic.DebriefLines(r)));
        }

        static void Legend(RectTransform parent, Color c, string label, float x, float y)
        {
            UiKit.At(UiKit.Box("Swatch", parent, c).rectTransform, x, y + 9, 22, 6);
            UiKit.At(UiKit.Label("Legend", parent, label, 15, UiKit.Dim).rectTransform, x + 30, y, 260, 24);
        }

        // ---------------------------------------------------------------- clock and input

        double RawClock()
        {
            double dsp = AudioSettings.dspTime, real = Time.realtimeSinceStartupAsDouble;
            if (dsp != _lastDsp) { _lastDsp = dsp; _dspAt = dsp; _realAt = real; }
            return _dspAt + (real - _realAt);
        }

        /// <summary>Seconds since the screen opened on the dspTime clock, without pauses; never goes back.</summary>
        double Clock()
        {
            double c = RawClock() - _origin - _paused;
            if (c < _lastClock) c = _lastClock;
            return _lastClock = c;
        }

        bool PointerInStage(Vector2 screenPos) => RectTransformUtility.RectangleContainsScreenPoint(_stage, screenPos, null);

        bool ButtonDown()
        {
            var kb = Keyboard.current;
            bool key = kb != null && kb.spaceKey.isPressed;
            var gp = Gamepad.current;
            bool pad = gp != null && gp.buttonSouth.isPressed;
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame && PointerInStage(m.position.ReadValue())) _mouseHold = true;
                if (!m.leftButton.isPressed) _mouseHold = false;
            }
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var pt = ts.primaryTouch;
                if (pt.press.wasPressedThisFrame && PointerInStage(pt.position.ReadValue())) _touchHold = true;
                if (!pt.press.isPressed) _touchHold = false;
            }
            return key || pad || _mouseHold || _touchHold;
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledDeltaTime > 0.35f) _paused += Time.unscaledDeltaTime - 1 / 60.0; // a hitch or a pause is not missed beats
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            // the project rule (DocsScreen): no uGUI selection, so Space / Enter / the pad's A never also "submit" a button
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (Time.unscaledTime - OpenedAt < 0.25f) return;

            if (_page != Page.Compress)
            {
                if (gp != null && PadNav(gp)) return;
                if (_torso) _torso.localScale = GaspScale();
                if (_studio) _studio.Drive("none", 0f, Vector2.zero, Gasp01());
                if (kb != null)
                    for (int i = 0; i < _choiceKeys.Count && i < 3; i++)
                        if ((i == 0 && (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame || (_choiceKeys.Count == 1 && kb.enterKey.wasPressedThisFrame)))
                            || (i == 1 && (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame))
                            || (i == 2 && (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)))
                        { UiSound.Play("click"); _choiceKeys[i](); return; }
                if (_page == Page.Steps && L != null)
                {
                    L.Tick(Clock());
                    if (L.Phase == CprPhase.Compress) { ShowCompress(); return; }
                    if (_timer)
                    {
                        int left = Mathf.CeilToInt((float)L.BreathLeft);
                        if (left != _shownSecond) { _shownSecond = left; _timer.text = left > 0 ? $"남은 시간 {left}초" : "10초가 지났습니다"; }
                    }
                }
                return;
            }

            double t = Clock();
            bool down = ButtonDown();
            if (down && !_down) L.Press(t);
            else if (!down && _down) L.Release(t);
            _down = down;
            bool named = false, vague = false, aed = false;
            if (kb != null)
            {
                named = kb.eKey.wasPressedThisFrame; vague = kb.qKey.wasPressedThisFrame;
                aed = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                named |= gp.buttonNorth.wasPressedThisFrame; vague |= gp.buttonWest.wasPressedThisFrame; aed |= gp.rightShoulder.wasPressedThisFrame;
            }
            if (named) { if (L.Swap == CprSwap.On) L.TakeBack(t); else L.RequestSwap(t, true); }
            if (vague) L.RequestSwap(t, false);
            if (aed) L.AedButton(t);
            L.Tick(t);
            if (L.Phase != _logicPhase) { PhaseCue(_logicPhase, L.Phase); _logicPhase = L.Phase; }
            if (L.Phase == CprPhase.Handover) { ShowHandover(); return; }
            Metronome(t);
            Draw(t);
        }

        // ---------------------------------------------------------------- sound and haptics

        void Metronome(double t)
        {
            if (!L.Started) return;
            bool quiet = L.Phase == CprPhase.AedAnalyze || L.Phase == CprPhase.AedShock;
            // schedule the next click a little ahead on the dsp clock
            double bt = L.BeatTime(_schedBeat);
            while (bt < t - 0.02) { _schedBeat++; bt = L.BeatTime(_schedBeat); }
            if (bt - t < 0.2)
            {
                if (!quiet && !_deaf)
                {
                    var s = _src[_flip ^= 1];
                    s.clip = ProceduralAudio.MetronomeClick(Mod(_schedBeat, 30) == 0);
                    s.PlayScheduled(bt + _origin + _paused);
                }
                _schedBeat++;
            }
            if (_vibrate && Time.unscaledTime > _rumbleUntil) StopRumble();
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        void Cue(string kind)
        {
            if (_deaf) return;
            _cueSrc.PlayOneShot(ProceduralAudio.AedTone(kind));
        }

        void Rumble()
        {
            var gp = Gamepad.current;
            if (!_vibrate || gp == null) return;
            gp.SetMotorSpeeds(0.15f, 0.35f);
            _rumbleUntil = Time.unscaledTime + 0.06f;
        }

        void StopRumble()
        {
            if (_rumbleUntil <= 0) return;
            _rumbleUntil = 0;
            Gamepad.current?.SetMotorSpeeds(0, 0);
        }

        void OnDisable() => StopRumble();

        // ---------------------------------------------------------------- drawing

        void Draw(double t)
        {
            float dt = Time.unscaledDeltaTime;
            // the beat: the ring closes on the hands at each beat; a flash at the beat (visual metronome)
            int k = L.BeatIndexAt(t);
            double bt = L.BeatTime(k), phase = (t - bt) / CprLogic.Interval; // -0.5 .. 0.5 around the nearest beat
            bool counting = L.Phase == CprPhase.Compress && L.Swap == CprSwap.None;
            // analysing and shocking: everyone off the patient, so no beat at all (no flash, no ring, no vibration)
            bool handsOff = L.Phase == CprPhase.AedAnalyze || L.Phase == CprPhase.AedShock;
            if (k != _lastVisualBeat && t >= bt)
            {
                _lastVisualBeat = k;
                if (!handsOff) { _beatFlash = 1; Rumble(); }
            }
            if (handsOff) { _beatFlash = 0; phase = 0; }
            _beatFlash = Mathf.Max(0, _beatFlash - dt * 5f);
            float approach = phase < 0 ? (float)(-phase * 2) : 0f; // 1 half a beat before, 0 on the beat
            float ringScale = 1f + approach * (_reduce ? 0.25f : 0.7f);
            _ring.localScale = new Vector3(ringScale, ringScale, 1);
            var rc = L.Fatigue > 0.4f && L.FeltFatigue > 0 ? UiKit.Amber : UiKit.Teal;
            _ringImg.color = new Color(rc.r, rc.g, rc.b, counting ? 0.35f + 0.65f * _beatFlash : 0.15f);
            if (handsOff)
            {
                // a steady amber edge: hands off (no pulse to press to)
                _beatDot.color = new Color(UiKit.Amber.r, UiKit.Amber.g, UiKit.Amber.b, 0.3f);
                _edgeImg.color = new Color(UiKit.Amber.r, UiKit.Amber.g, UiKit.Amber.b, _deaf ? 0.8f : 0.45f);
            }
            else
            {
                _beatDot.color = new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.2f + 0.8f * _beatFlash);
                float edge = (_deaf ? 0.75f : 0.3f) * _beatFlash * (counting ? 1 : 0.4f);
                _edgeImg.color = new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, edge);
            }

            // the hands: down while pressed, back up on release; they tremble as fatigue sets in (gently with reduce motion).
            // The neighbour's or the volunteer's hands (their own sleeves) press on the beat by themselves.
            var who = L.Compressor;
            bool myHands = who == "me", anyHands = who != "none";
            if (who != _handsOf)
            {
                _handsOf = who;
                var c = who == "neighbour" ? NeighbourSleeve : who == "volunteer" ? VolunteerSleeve : Sleeve;
                foreach (var sv in _sleeves) sv.color = c;
            }
            float target = myHands ? (L.Held ? 1f : 0f) : anyHands && phase >= 0 && phase < 0.3 ? 1f : 0f;
            _press = Mathf.MoveTowards(_press, target, dt * (target > _press ? 14f : 9f));
            float f = (float)L.Fatigue;
            float amp = myHands ? (_reduce ? 1.2f : 6f) * f : 0f;
            float n = Time.unscaledTime * 23f;
            var shake = new Vector2((Mathf.PerlinNoise(n, 0.3f) - 0.5f) * 2 * amp, (Mathf.PerlinNoise(0.7f, n) - 0.5f) * 2 * amp);
            _hands.anchoredPosition = _handsBase + shake + new Vector2(0, -16 * _press);
            float s = 1f - 0.06f * _press;
            _hands.localScale = new Vector3(s, s, 1);
            if (_hands.gameObject.activeSelf != anyHands) _hands.gameObject.SetActive(anyHands);
            if (_studio)
            {
                // the same press and tremble on the real arms (a pixel of the drawing is about a millimetre there)
                _studio.Drive(anyHands ? who : "none", _press, shake * 0.001f, 0f);
                _studio.ShowAed(L.Phase != CprPhase.Compress || L.AedDone, L.Phase is CprPhase.AedAnalyze or CprPhase.AedShock || L.AedDone);
            }

            // the depth meter shows the last press as it felt
            if (L.PressSeq != _seenPress)
            {
                _seenPress = L.PressSeq;
                var lp = L.Last;
                float d = Mathf.Clamp01((float)lp.FeltDepth / 1.2f);
                _depthFillRt.anchorMax = new Vector2(1, d);
                bool ok = lp.FeltDepth >= CprLogic.DepthOk - 0.05;
                _depthFill.color = ok ? UiKit.Teal : UiKit.Amber;
                _depthWord.text = ok ? "약 5cm" : "얕음";
                _depthWord.color = ok ? UiKit.Teal : UiKit.Amber;
            }

            // the count the dispatcher gives (하나, 둘 ... 서른), and the count-in
            int count = k < 0 ? k : Mod(k, 30) + 1;
            if (L.Compressor == "none" && L.Phase != CprPhase.Compress) count = int.MinValue; // hands off: nobody counts
            if (count != _shownCount)
            {
                _shownCount = count;
                _count.text = !L.Started || count == int.MinValue ? "" : k < -4 ? "준비" : k < 0 ? (-k).ToString() : count.ToString();
            }

            // the gauge: what the rescuer feels
            float g = Mathf.Clamp01((float)L.Gauge);
            _gaugeFillRt.anchorMax = new Vector2(g, 1);
            _gaugeFill.color = g >= 0.7f ? UiKit.Teal : g >= 0.45f ? UiKit.Amber : UiKit.Red;
            int gp = Mathf.RoundToInt(g * 10) * 10;
            if (gp != _shownGauge) { _shownGauge = gp; _gaugeWord.text = gp + "%"; }

            int sec = Mathf.Max(0, (int)L.CompressTime(t));
            if (sec != _shownSecond || L.Compressions != _shownPresses)
            {
                _shownSecond = sec; _shownPresses = L.Compressions;
                _timer.text = $"압박 {CprLogic.Clock(sec)} · {L.Compressions}회";
            }

            if (L.FeedbackSeq != _seenFeedback)
            {
                _seenFeedback = L.FeedbackSeq;
                var line = CprText.Feedback(L.Feedback);
                bool step = L.Feedback != null && (L.Feedback.StartsWith("aed") || L.Feedback.StartsWith("swap") || L.Feedback == "neighbour" || L.Feedback == "take_back");
                if (step) { _aedLabel.text = line; _aedLabelAt = Time.unscaledTime; _speech.Say(line); }
                else _feedback.text = line;
                _feedback.color = L.Feedback == "good" ? UiKit.Teal : UiKit.Ink;
            }
            if (L.Hint != null)
            {
                _hint.text = L.Hint == "swap" ? CprText.HintSwap : CprText.HintPress;
                _hintAt = Time.unscaledTime;
                L.HintShown();
            }
            // a reminder stays 10 s; a step's line 6 s once compressing again (the AED's lines stay while it talks)
            if (_hint.text.Length > 0 && Time.unscaledTime - _hintAt > 10f) _hint.text = "";
            if (_aedLabel.text.Length > 0 && L.Phase == CprPhase.Compress && Time.unscaledTime - _aedLabelAt > 6f) _aedLabel.text = "";

            if (L.Phase != _shownPhase || L.Swap != _shownSwap)
            {
                bool aed = L.Phase != CprPhase.Compress;
                if (aed && _shownPhase == CprPhase.Compress) { _feedback.text = ""; _hint.text = ""; } // the AED talks now
                if (L.Swap != _shownSwap && L.Swap != CprSwap.None) _feedback.text = ""; // the neighbour's presses are not judged
                _shownPhase = L.Phase; _shownSwap = L.Swap;
                _title.text = CprText.Title(L.Phase);
                var label = CprText.AedButton(L.Phase);
                _aedButton.gameObject.SetActive(label != null);
                if (label != null) _aedButtonText.text = label;
                _swap.gameObject.SetActive(!aed && L.Swap == CprSwap.None);
                _skip.gameObject.SetActive(_demo && !aed && L.AedDone);
                _takeBack.gameObject.SetActive(!aed && L.Swap == CprSwap.On);
                _vague.gameObject.SetActive(!aed && L.Swap == CprSwap.None);
                _who.text = L.Compressor switch
                {
                    "me" => "지금 누르는 사람: 나",
                    "neighbour" => "지금 누르는 사람: " + CprText.SwapPerson,
                    "volunteer" => "지금 누르는 사람: " + CprText.AedPerson,
                    _ => L.Phase == CprPhase.AedAnalyze || L.Phase == CprPhase.AedShock ? "모두 환자에게서 떨어져 있습니다" : "손을 바꾸는 중",
                };
                if (L.Phase is CprPhase.AedAnalyze or CprPhase.AedShock || L.AedDone) { _padA.gameObject.SetActive(true); _padB.gameObject.SetActive(true); } // the pads stay on
            }
            if (L.Phase == CprPhase.AedShock && _aedButton.gameObject.activeSelf)
            {
                float blink = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f);
                _aedImage.color = Color.Lerp(UiKit.Amber, UiKit.Red, blink);
            }
            else if (_aedButton.gameObject.activeSelf) _aedImage.color = UiKit.Teal;
            if (L.Phase == CprPhase.AedAnalyze)
            {
                int left = Mathf.CeilToInt((float)L.AnalyzeLeft);
                if (left != _shownAnalyze) { _shownAnalyze = left; _who.text = $"분석 중 · 손을 떼고 기다리세요 ({left})"; }
            }
            else if (L.Swap == CprSwap.On)
            {
                int left = Mathf.CeilToInt((float)L.SwapLeft);
                if (left != _shownAnalyze) { _shownAnalyze = left; _who.text = $"지금 누르는 사람: {CprText.SwapPerson} · {left}초 뒤 다시 교대"; }
            }
            else _shownAnalyze = -1;
        }

        int _shownGauge = -1, _shownPresses = -1, _shownAnalyze = -1;

        // ---------------------------------------------------------------- gamepad on the pages (no uGUI selection)

        readonly List<Button> _nav = new();
        readonly List<Color> _navColors = new();
        int _cursor = -1;

        /// <summary>D-pad / stick up and down move a highlight over the page's buttons, A presses it. True when it acted.</summary>
        bool PadNav(Gamepad gp)
        {
            if (_nav.Count == 0) return false;
            int move = (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame ? 1 : 0)
                     - (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame ? 1 : 0);
            bool press = gp.buttonSouth.wasPressedThisFrame;
            if (move == 0 && !press) return false;
            if (_cursor < 0) { SetCursor(_page == Page.Intro ? _nav.Count - 1 : 0); if (!press) return true; }
            else if (move != 0) { SetCursor(Mathf.Clamp(_cursor + move, 0, _nav.Count - 1)); return true; }
            var b = _nav[_cursor];
            if (b) b.onClick.Invoke();
            return true;
        }

        void SetCursor(int i)
        {
            if (_cursor >= 0 && _cursor < _nav.Count && _nav[_cursor]) _nav[_cursor].GetComponent<Image>().color = _navColors[_cursor];
            _cursor = i;
            if (_nav[i]) _nav[i].GetComponent<Image>().color = Color.Lerp(_navColors[i], UiKit.Amber, 0.55f);
        }

        /// <summary>The breathing check: his chest stays still but gives a gasp every few seconds (still with reduce motion).</summary>
        Vector3 GaspScale()
        {
            float b = Gasp01();
            return new Vector3(1f + 0.015f * b, 1f + 0.035f * b, 1f);
        }

        /// <summary>The gasp as 0..1 (the rendered chest lifts by it, the flat torso scales by it); 0 with reduce motion.</summary>
        float Gasp01()
        {
            if (_reduce || _page != Page.Steps || L == null || L.Phase != CprPhase.Breath) return 0f;
            float ph = Time.unscaledTime % 3.4f;
            return ph < 0.3f ? Mathf.Sin(ph / 0.3f * Mathf.PI) : 0f;
        }
        float _hintAt, _aedLabelAt;
        CprSwap _shownSwap = (CprSwap)(-1);

        /// <summary>Low-vision speech through the host's settings (only speaks in that mode); quiet in tests and without a host.</summary>
        sealed class HostSettingsProxy
        {
            AiOn.Settings.HostSettings _h;
            bool _looked;
            string _last;

            public void Say(string text)
            {
                if (string.IsNullOrEmpty(text) || text == _last) return;
                _last = text;
                if (!_looked) { _looked = true; _h = Object.FindFirstObjectByType<AiOn.Settings.HostSettings>(); }
                if (_h) _h.Speak(text);
            }
        }
    }
}
