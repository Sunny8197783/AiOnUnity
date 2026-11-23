using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// 1장 소화기 점검 ("extgauge", competition upgrade): the corridor extinguisher's pressure gauge up close. Read where the
    /// needle sits (green band = normal, fact ext_gauge; red or yellow = replace, ext_gauge_bad), then decide: tell the
    /// management office (ext_manage) or leave it. Keys: 1-3 pick, Esc leaves without checking ({}).
    /// Options { needle, profile, reduceMotion, facts { id: { text, source } } } -> { answer, action, needle }.
    /// Rules: <see cref="ExtinguisherCheck"/>.
    /// </summary>
    public class ExtGaugeScreen : UiScreen
    {
        JObject _o;
        double _needle;
        string _answer;
        RectTransform _right;
        readonly List<Button> _choices = new();
        AiOn.Settings.HostSettings _speech;
        bool _lowVision;
        AiOn.UI.Studio.GaugeStudio _studio;

        public static async Task<JToken> Open(JObject options)
        {
            var r = await Show<ExtGaugeScreen>(s => s._o = options ?? new JObject(), 1400, 820);
            return r as JToken ?? new JObject();
        }

        public static void Register(UnityStoryHost host) => host.Screens[ExtinguisherCheck.Screen] = Open;

        protected override object EscapeResult() => new JObject();

        string Fact(string id) => EasyKorean.Of(_o?["facts"]?[id]?["text"]?.ToString()); // 쉬운 한국어
        string Source(string id) => _o?["facts"]?[id]?["source"]?.ToString();

        protected override void Build()
        {
            _needle = _o["needle"] != null && (_o["needle"].Type == JTokenType.Float || _o["needle"].Type == JTokenType.Integer) ? (double)_o["needle"] : ExtinguisherCheck.CorridorNeedle;
            _lowVision = _o["profile"]?.ToString() == "lowvision";
            Title("소화기 점검 · 3층 복도 옥내소화전 옆", "압력계를 봅니다");

            var dial = UiKit.Box("Dial", Window, new Color(0.1f, 0.11f, 0.12f)).rectTransform;
            UiKit.At(dial, 40, 140, 600, 560);
            // the extinguisher itself, close up at its gauge (UI/Studio/GaugeStudio); the flat dial if the model is missing
            _studio = AiOn.UI.Studio.GaugeStudio.Create(this, AiOn.UI.Studio.MiniStudio.PixelsFor(Root.GetComponent<Canvas>(), new Vector2(600, 560)), _needle, _lowVision);
            if (_studio)
            {
                dial.gameObject.AddComponent<RectMask2D>();
                _studio.Studio.Show(dial);
                var top = UiKit.Box("CaptionPlate", dial, new Color(0.02f, 0.03f, 0.04f, 0.7f)).rectTransform;
                UiKit.At(top, 0, 0, 600, 58);
                var bottom = UiKit.Box("LegendPlate", dial, new Color(0.02f, 0.03f, 0.04f, 0.7f)).rectTransform;
                UiKit.At(bottom, 0, 494, 600, 66);
                UiKit.At(UiKit.Label("Caption", dial, "압력계 (소화기 손잡이 옆)", 20, UiKit.Ink, TextAnchor.UpperCenter).rectTransform, 0, 16, 600, 30);
                UiKit.At(UiKit.Label("Legend", dial, "<color=#F2B33D>■</color> 낮음   <color=#4CC38A>■</color> 정상   <color=#E64849>■</color> 높음", 20, UiKit.Ink, TextAnchor.UpperCenter).rectTransform, 0, 513, 600, 30);
                _right = UiKit.Rect("Right", Window);
                UiKit.At(_right, 680, 140, 680, 640);
                Read();
                return;
            }
            var face = UiKit.Box("Face", dial, Color.white);
            face.sprite = DialSprite();
            UiKit.At(face.rectTransform, 90, 60, 420, 420);
            var needle = UiKit.Box("Needle", face.rectTransform, new Color(0.08f, 0.08f, 0.09f)).rectTransform;
            needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0f);
            needle.sizeDelta = new Vector2(8, 165);
            needle.anchoredPosition = Vector2.zero;
            needle.localRotation = Quaternion.Euler(0, 0, NeedleAngle(_needle));
            var hub = UiKit.Box("Hub", face.rectTransform, new Color(0.15f, 0.15f, 0.16f));
            hub.sprite = HudDraw.DiscSprite;
            var hr = hub.rectTransform; hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0.5f, 0.5f); hr.sizeDelta = new Vector2(34, 34);
            UiKit.At(UiKit.Label("Caption", dial, "압력계 (소화기 손잡이 옆)", 20, UiKit.Dim, TextAnchor.UpperCenter).rectTransform, 0, 18, 600, 30);
            UiKit.At(UiKit.Label("Legend", dial, "<color=#F2B33D>■</color> 낮음   <color=#4CC38A>■</color> 정상   <color=#E64849>■</color> 높음", 20, UiKit.Ink, TextAnchor.UpperCenter).rectTransform, 0, 505, 600, 30);

            _right = UiKit.Rect("Right", Window);
            UiKit.At(_right, 680, 140, 680, 640);
            Read();
        }

        /// <summary>Needle 0..1 across a 240° dial (0 at the lower left, 1 at the lower right).</summary>
        static float NeedleAngle(double v) => 120f - 240f * Mathf.Clamp01((float)v);

        static Sprite _dial;

        static Sprite DialSprite()
        {
            if (_dial) return _dial;
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "GaugeDial" };
            var px = new Color32[n * n];
            Color32 rim = new(60, 64, 68, 255), paper = new(238, 236, 228, 255), clear = new(0, 0, 0, 0);
            Color32 yellow = new(242, 179, 61, 255), green = new(76, 195, 138, 255), red = new(230, 72, 73, 255);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f, r = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                    Color32 c = clear;
                    if (r <= 1f) c = r > 0.93f ? rim : paper;
                    if (r > 0.62f && r < 0.86f)
                    {
                        // the angle from straight up, clockwise, mapped to the dial's 0..1 (−120°..+120°)
                        float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // 0 up, + to the right
                        float v = (ang + 120f) / 240f;
                        if (v >= 0 && v <= 1)
                            c = v < ExtinguisherCheck.GreenFrom ? yellow : v <= ExtinguisherCheck.GreenTo ? green : red; // as the legend: left low, right high
                    }
                    px[y * n + x] = c;
                }
            t.SetPixels32(px); t.Apply();
            return _dial = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        Text Para(string rich, int size, Color color, float y, float h, FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.Label("P", _right, rich, size, color, TextAnchor.UpperLeft, style);
            UiKit.At(t.rectTransform, 0, y, 680, h);
            return t;
        }

        Button Choice(string label, float y, System.Action onClick)
        {
            var b = UiKit.Button("Choice", _right, $"{_choices.Count + 1}. {label}", 24, ScreenDecor.Over(UiKit.Panel, Color.white, 0.08f), UiKit.Ink);
            b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            UiKit.At((RectTransform)b.transform, 0, y, 680, 64);
            b.onClick.AddListener(() => onClick());
            _choices.Add(b);
            return b;
        }

        void Say(string text)
        {
            if (!_lowVision || string.IsNullOrEmpty(text)) return;
            if (!_speech) _speech = Object.FindFirstObjectByType<AiOn.Settings.HostSettings>();
            if (_speech) _speech.Speak(text);
        }

        void Read()
        {
            Clear(_right); _choices.Clear();
            Para("압력계 바늘이 어디에 있나요?", 28, UiKit.Ink, 0, 40, FontStyle.Bold);
            Para("오래된 소화기는 겉보기에 멀쩡해도 압력이 빠져 있을 수 있습니다.", 21, UiKit.Dim, 46, 60);
            float y = 120;
            foreach (var (id, label) in ExtinguisherCheck.Readings)
            {
                var pick = id;
                Choice(label, y, () => Answer(pick));
                y += 76;
            }
            Say("압력계 바늘이 어디에 있나요? 1, 녹색 구간 안. 2, 녹색보다 왼쪽. 3, 녹색보다 오른쪽. " + (_lowVision ? "바늘은 녹색보다 조금 왼쪽에 있습니다." : ""));
        }

        void Answer(string id)
        {
            _answer = id;
            Clear(_right); _choices.Clear();
            bool ok = id == ExtinguisherCheck.Zone(_needle);
            var fb = ExtinguisherCheck.ReadingFeedback(id, _needle);
            Para((ok ? "<color=#4CC38A>✓</color> " : "<color=#F2B33D>△</color> ") + fb, 24, UiKit.Ink, 0, 64, FontStyle.Bold);
            float y = 72;
            foreach (var f in new[] { "ext_gauge", "ext_gauge_bad" })
            {
                var text = Fact(f);
                if (string.IsNullOrEmpty(text)) continue;
                Para($"{text}\n<size=17><color=#9aa6ad>— {Source(f)}</color></size>", 20, UiKit.Ink, y, 96);
                y += 104;
            }
            Para("어떻게 할까?", 26, UiKit.Ink, y + 4, 36, FontStyle.Bold);
            var manage = Fact("ext_manage");
            if (!string.IsNullOrEmpty(manage)) Para($"<size=18><color=#9aa6ad>{manage}\n— {Source("ext_manage")}</color></size>", 18, UiKit.Dim, y + 42, 76);
            y += 128;
            foreach (var (aid, label) in ExtinguisherCheck.Actions)
            {
                var pick = aid;
                Choice(label, y, () => Finish(new JObject { ["answer"] = _answer, ["action"] = pick, ["needle"] = _needle }));
                y += 76;
            }
            Say(fb + " 어떻게 할까? 1, 관리사무소에 알려 교체를 부탁한다. 2, 일단 그대로 둔다.");
        }

        static readonly Key[] NumberKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };

        protected override void Update()
        {
            base.Update();
            var kb = Keyboard.current;
            if (kb == null || Time.unscaledTime - OpenedAt < 0.25f) return;
            for (int i = 0; i < NumberKeys.Length && i < _choices.Count; i++)
                if (kb[NumberKeys[i]].wasPressedThisFrame) { _choices[i].onClick.Invoke(); return; }
        }
    }
}
