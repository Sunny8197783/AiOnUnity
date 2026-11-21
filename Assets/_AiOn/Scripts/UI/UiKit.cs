using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Small helpers for screens built in code: the Korean OS font, panels, labels, buttons, layout.</summary>
    public static class UiKit
    {
        static Font _font;
        static readonly string[] FontNames = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial" };

        /// <summary>Pretendard (the original's UI font, SIL OFL, in Resources/Fonts), else a Korean OS font.</summary>
        public static Font Font => _font ? _font : _font = Resources.Load<Font>("Fonts/Pretendard-Medium") ?? Font.CreateDynamicFontFromOSFont(FontNames, 32);

        public static readonly Color Ink = new(0.91f, 0.93f, 0.93f);
        public static readonly Color Dim = new(0.6f, 0.65f, 0.68f);
        public static readonly Color Teal = new(0.24f, 0.78f, 0.71f);
        public static readonly Color Amber = new(0.95f, 0.7f, 0.24f);
        public static readonly Color Red = new(0.9f, 0.28f, 0.3f);
        public static readonly Color Panel = new(0.07f, 0.1f, 0.11f, 0.97f);
        /// <summary>The dim behind every modal: UiScreen windows and the overlay's story cards alike.</summary>
        public static readonly Color ModalShade = new(0, 0, 0, 0.72f);
        public static readonly Color Row = new(0.13f, 0.17f, 0.19f, 1f);
        public static readonly Color RowSelected = new(0.16f, 0.32f, 0.31f, 1f);

        /// <summary>A screen-space canvas that takes clicks (adds an Input System event system if the scene has none).</summary>
        public static Canvas ScreenCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (!Object.FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Object.DontDestroyOnLoad(es);
            }
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Box(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        static readonly System.Collections.Generic.Dictionary<string, Sprite> _emoji = new();

        /// <summary>
        /// The Noto Emoji picture for a text that is one emoji (Resources/Emoji/emoji_u&lt;codepoint&gt;.png), or null.
        /// uGUI Text cannot draw emoji outside the Basic Multilingual Plane, so icons like 🧭 are drawn as images.
        /// </summary>
        public static Sprite Emoji(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Trim().Replace("️", ""); // drop the emoji variation selector
            if (text.Length == 0 || !char.IsSurrogate(text, 0) && text[0] < 0x2190) return null;
            if (_emoji.TryGetValue(text, out var cached)) return cached;
            int cp = char.ConvertToUtf32(text, 0);
            var tex = Resources.Load<Texture2D>($"Emoji/emoji_u{cp:x}");
            Sprite sprite = tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100) : null;
            _emoji[text] = sprite;
            return sprite;
        }

        /// <summary>An icon: the emoji picture when there is one, else the text as a label.</summary>
        public static Graphic Icon(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var sprite = Emoji(text);
            if (!sprite) return Label(name, parent, text, size, color, align);
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.preserveAspect = true; img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = TextScaler.Register(t, size); // the 글자 크기 setting
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, int size, Color bg, Color fg)
        {
            var img = Box(name, parent, bg, raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            b.colors = colors;
            var t = Label("Text", img.transform, text, size, fg, TextAnchor.MiddleCenter);
            Fill(t.rectTransform, 12, 4);
            return b;
        }

        /// <summary>Anchors to the parent's top-left with a fixed position and size.</summary>
        public static RectTransform At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Fill(RectTransform rt, float padX = 0, float padY = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
            return rt;
        }

        /// <summary>A vertical list that grows with its rows (for use inside a ScrollRect).</summary>
        public static RectTransform Column(RectTransform rt, float spacing)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        /// <summary>A scrolling viewport filling <paramref name="parent"/>; returns its content column.</summary>
        public static RectTransform ScrollList(string name, RectTransform parent, float spacing = 6)
        {
            var view = Box(name, parent, new Color(0, 0, 0, 0.18f), raycast: true).rectTransform;
            Fill(view);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = new Vector2(6, 0); content.offsetMax = new Vector2(-6, 0);
            Column(content, spacing).GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 6, 6);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view; scroll.horizontal = false; scroll.scrollSensitivity = 30;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        public static void Height(Component c, float h)
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>(); // not ??: Unity's fake null
            le.preferredHeight = h; le.minHeight = h;
        }
    }

    /// <summary>
    /// The one key table (original hud.js hud-keys, app.js controls card, menu.js key list). The bottom-right strip, the
    /// pause menu's key list and the H card are all built from it, so a key added here shows in all three.
    /// Strip: the short word on the HUD strip (null: not on the strip). Pause: the pause list's key and word. Card: the
    /// H card's key and line (the original's wording, verbatim).
    /// </summary>
    public static class UiKeys
    {
        /// <summary>A scene with its own key panel (the 3D extinguisher) hides the general strip meanwhile.</summary>
        public static bool StripSuppressed;
        /// <summary>Someone else is the player (StoryDirector.OtherPov): 정민's own keys leave the strip.</summary>
        public static bool OtherPov;
        /// <summary>Someone else's slow steps (StoryContext.WalkOnly): no running or jumping either.</summary>
        public static bool WalkOnly;

        /// <summary>The strip keys that are 정민's own (hands, light, bag, phone, notebook, first aid, chalk, terminal).</summary>
        static readonly string[] OwnKeys = { "G", "Q", "C", "X", "B", "F", "P", "Tab", "I", "K" };

        /// <summary>Whether the strip shows this key now (OtherPov, WalkOnly).</summary>
        public static bool Shown(string stripKey, bool otherPov, bool walkOnly) =>
            stripKey != null && !(otherPov && System.Array.IndexOf(OwnKeys, stripKey) >= 0) && !(walkOnly && (stripKey == "Shift" || stripKey == "Space"));

        public readonly struct Entry
        {
            public readonly string StripKey, Strip, PauseKey, Pause, CardKey, Card;
            public Entry(string stripKey, string strip, string pauseKey, string pause, string cardKey, string card)
            { StripKey = stripKey; Strip = strip; PauseKey = pauseKey; Pause = pause; CardKey = cardKey; Card = card; }
        }

        public static readonly Entry[] All =
        {
            new("WASD", "이동", "WASD · 방향키", "이동", "W A S D · 방향키", "이동"),
            new(null, null, "마우스 (클릭 후) · Alt+방향키", "둘러보기", "마우스", "둘러보기 — 화면을 한 번 클릭하면 마우스만 움직여도 시점이 돕니다 (Esc로 커서 되찾기)"),
            new("Shift", "달리기", "Shift", "달리기 (기력)", "Shift", "달리기 (기력이 줄어듭니다. 가방이 무거우면 더 빨리 지칩니다)"),
            new("Space", "점프·넘기", "Space", "점프", "Space", "점프 · 낮은 담/차/난간 앞에서 누르면 넘거나 올라감"),
            new("E", "살피기", "E · Enter", "살피기 · 대화", "E · Enter", "살피기 · 대화 · 사용"),
            new("G", "줍기·놓기", "G", "줍기 · 내려놓기", "G", "바닥의 물건 줍기 / 내려놓기"),
            new("Q", "내려놓기", "Q", "든 물건 내려놓기", "Q", "손에 든 물건 내려놓기"),
            new("C", "엎드리기", "C", "엎드리기 · 웅크리기", "C", "엎드리기/웅크리기 (폭발·파편 피해가 줄어듭니다. Space로 일어서기)"),
            new("X", "구급함", "X", "구급함", "X", "구급함 쓰기"),
            new("B", "분필", "B", "분필로 적기", "B", "분필로 바닥에 적기 — 다음 회차의 내가 보게 됩니다"),
            new("F", "불빛", "F", "손전등 · 휴대폰 불빛", "F", "손전등 켜기/끄기 (정전 후)"),
            new("V", "시점", "V", "1인칭 / 3인칭", "V", "1인칭 / 3인칭 전환"),
            new("P", "휴대폰", "P", "휴대폰 · FM 라디오", "P", "휴대폰 (문자 · FM 라디오)"),
            new("Tab", "수첩", "Tab", "수첩", "Tab", "수첩"),
            new("I", "소지품", "I", "소지품", "I", "소지품"),
            new("M", "지도", "M", "지도", "M", "지도"),
            new("K", "행동요령", "K", "행동 요령 (재난 단말기)", "K", "재난 단말기 — 상황별 행동 요령"),
            new("H", "조작법", "H", "조작법", "H", "이 조작법 다시 보기"),
            new("Esc", "메뉴", "Esc", "이 메뉴", "Esc", "일시정지 · 설정"),
        };

        /// <summary>The pause menu's rows (key, word), in table order.</summary>
        public static string[][] PauseRows()
        {
            var rows = new string[All.Length][];
            for (int i = 0; i < All.Length; i++) rows[i] = new[] { All[i].PauseKey, All[i].Pause };
            return rows;
        }

        /// <summary>The H card's body lines ("key — text").</summary>
        public static System.Collections.Generic.List<string> CardLines()
        {
            var list = new System.Collections.Generic.List<string>(All.Length);
            foreach (var e in All) list.Add(e.CardKey + " — " + e.Card);
            return list;
        }

        /// <summary>The bottom-right strip as rich text, perRow keys a line.</summary>
        public static string StripText(int perRow) => StripText(perRow, false, false);

        /// <summary>The strip for someone else's turn (OtherPov) and slow steps (WalkOnly).</summary>
        public static string StripText(int perRow, bool otherPov, bool walkOnly)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var e in All)
            {
                if (!Shown(e.StripKey, otherPov, walkOnly)) continue;
                sb.Append(n == 0 ? "" : n % perRow == 0 ? "\n" : "    ").Append("<b><color=#e8eef0>").Append(e.StripKey).Append("</color></b> ").Append(e.Strip);
                n++;
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Interface blips (original audio.js UI: open, close, click, tick, confirm), synthesized like the original's
    /// oscillator tones and played on the UI bus ('UI 효과음'); the master volume, mute and the deaf setting are
    /// AudioListener.volume, which every source follows.
    /// </summary>
    public static class UiSound
    {
        static AudioSource _src;
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> _clips = new();
        static float _lastTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _src = null; _lastTick = 0; }

        public static void Play(string name)
        {
            if (!Application.isPlaying) return;
            if (name == "tick") { if (Time.unscaledTime - _lastTick < 0.05f) return; _lastTick = Time.unscaledTime; }
            var clip = Clip(name);
            if (!clip) return;
            if (!_src)
            {
                var go = new GameObject("UiSound");
                Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>();
                _src.playOnAwake = false; _src.spatialBlend = 0; _src.ignoreListenerPause = true;
            }
            _src.PlayOneShot(clip, AiOn.Settings.AudioBus.Ui);
        }

        static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = name switch
            {
                "open" => Make(name, (660, 880, 0.09f, 0.035f, 0f)),
                "close" => Make(name, (820, 600, 0.08f, 0.025f, 0f)),
                "click" => Make(name, (1400, 1400, 0.035f, 0.03f, 0f)),
                "tick" => Make(name, (1250, 1250, 0.03f, 0.022f, 0f)),
                "confirm" => Make(name, (784, 784, 0.08f, 0.045f, 0f), (1175, 1175, 0.12f, 0.045f, 0.07f)),
                // a radio / terminal keying up: a hard click, then a short high blip and a lower one
                "radio" => Make(name, (2400, 900, 0.018f, 0.05f, 0f), (1760, 1760, 0.045f, 0.03f, 0.03f), (1320, 1320, 0.06f, 0.03f, 0.09f)),
                // the report arriving: three rising tones
                "report" => Make(name, (523, 523, 0.09f, 0.04f, 0f), (659, 659, 0.09f, 0.04f, 0.09f), (784, 784, 0.16f, 0.045f, 0.18f)),
                _ => null,
            };
            _clips[name] = c;
            return c;
        }

        // Each tone: frequency from -> to (exponential), duration, peak (the original's gain before its 0.45 UI bus), start.
        static AudioClip Make(string name, params (float f0, float f1, float dur, float vol, float at)[] tones)
        {
            const int rate = 44100;
            float len = 0;
            foreach (var t in tones) len = Mathf.Max(len, t.at + t.dur + 0.02f);
            int n = Mathf.CeilToInt(len * rate);
            var data = new float[n];
            foreach (var t in tones)
            {
                int s0 = Mathf.RoundToInt(t.at * rate), cnt = Mathf.CeilToInt(t.dur * rate);
                double phase = 0;
                for (int i = 0; i < cnt && s0 + i < n; i++)
                {
                    float u = i / (float)rate, k = u / t.dur;
                    float f = t.f0 * Mathf.Pow(t.f1 / t.f0, k);
                    phase += 2 * Mathf.PI * f / rate;
                    // 8 ms attack, then an exponential decay to silence at dur (linearRamp + exponentialRamp in the original)
                    float env = u < 0.008f ? u / 0.008f : Mathf.Pow(0.0001f, (u - 0.008f) / Mathf.Max(0.001f, t.dur - 0.008f));
                    data[s0 + i] += (float)System.Math.Sin(phase) * env * t.vol * 2.5f;
                }
            }
            var clip = AudioClip.Create("ui_" + name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
