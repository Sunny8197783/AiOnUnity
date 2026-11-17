// 정민's phone (original ui/phone.js): status bar (time, signal, battery %), the map app's state, the message list,
// the neighbourhood chat / SNS posts, and the action buttons; plus the full-width emergency alert sheet.
// Option parsing, texts and rules live in PhoneLogic (tested outside Unity); this file only draws.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// phone({ battery, signal, messages, mapApp, actions }) -> the picked action id.
    /// Keys: ←→ / Tab move between the buttons (the first is focused), Enter or Space presses, ↑↓ scroll,
    /// Esc picks 'close' / 'back' (or the only action). Clicks within 0.2 s of opening are ignored, like the original.
    /// </summary>
    public class PhoneScreen : UiScreen
    {
        const float W = 460, H = 920;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        PhoneView _v;
        readonly List<Button> _buttons = new();
        int _focus;
        RectTransform _content, _lastMessage, _spinner;
        ScrollRect _scroll;
        bool _scrolled;

        public static async Task<JToken> Open(JObject options)
        {
            var v = PhoneLogic.Parse(options);
            var r = await Show<PhoneScreen>(s => s._v = v, W, H);
            // m.close(undefined) for an action without an id: resolve null, not a JSON-null token.
            return r is JToken t && t.Type != JTokenType.Null ? t : null;
        }

        /// <summary>Registers "phone" and "emergencyAlert" (the names of app.js's ui object).</summary>
        public static void Register(UnityStoryHost host)
        {
            host.Screens["phone"] = Open;
            host.Screens["emergencyAlert"] = EmergencyAlertScreen.Open;
        }

        protected override object EscapeResult() => _v != null && _v.HasEscape ? (object)(_v.EscapeId ?? JValue.CreateNull()) : null;

        protected override void Build()
        {
            // The bezel is the window; the screen sits inside it.
            Window.GetComponent<Image>().color = Hex("#1b2327");
            var frame = UiKit.Box("Frame", Window, Hex("#0a0e10")).rectTransform;
            UiKit.Fill(frame, 12, 12);
            const float fw = W - 24, fh = H - 24;

            var notch = UiKit.Box("Notch", frame, Color.black).rectTransform;
            UiKit.At(notch, fw / 2 - 62, 10, 124, 32);

            StatusBar(frame, fw);

            // Actions and the home bar along the bottom.
            const float homeH = 26, actH = 74;
            float actY = fh - homeH - actH;
            var acts = UiKit.Box("Actions", frame, Hex("#0c1214")).rectTransform;
            UiKit.At(acts, 0, actY, fw, actH);
            float gap = 10, bw = (fw - 36 - gap * (_v.Actions.Count - 1)) / _v.Actions.Count;
            for (int i = 0; i < _v.Actions.Count; i++)
            {
                var a = _v.Actions[i];
                var b = UiKit.Button("Act_" + i, acts, a.Label, 22, a.Primary ? UiKit.Teal : new Color(1, 1, 1, 0.08f),
                    a.Primary ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Ink);
                UiKit.At((RectTransform)b.transform, 18 + i * (bw + gap), 10, bw, 56);
                var id = a.Id;
                b.onClick.AddListener(() => Press(id));
                b.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0);
                _buttons.Add(b);
            }
            var home = UiKit.Box("Home", frame, new Color(1, 1, 1, 0.5f)).rectTransform;
            UiKit.At(home, fw / 2 - 70, fh - 18, 140, 6);

            // The screen: one scrolling column.
            var screen = UiKit.Box("Screen", frame, Hex("#101719")).rectTransform;
            UiKit.At(screen, 0, 58, fw, actY - 58);
            _content = UiKit.ScrollList("Apps", screen, 14);
            _scroll = _content.parent.GetComponent<ScrollRect>();
            _content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 10, 14);

            if (PhoneLogic.LowBattery(_v.Battery))
            {
                var banner = Bubble(_content, Hex("#e5484d") * new Color(1, 1, 1, 0.18f));
                UiKit.Label("Text", banner, $"<b>{PhoneLogic.BannerText(_v.Battery)}</b>", 19, Hex("#ffb4b6"));
            }
            if (_v.MapApp != null) MapApp(_content, fw - 32, _v.MapApp);

            Heading(_content, "메시지");
            if (_v.Messages.Count == 0) UiKit.Height(UiKit.Label("Empty", _content, "새 메시지가 없습니다.", 19, UiKit.Dim), 34);
            else foreach (var m in _v.Messages) _lastMessage = Message(_content, m);

            if (_v.Posts.Count > 0)
            {
                Heading(_content, "동네 단체방 · SNS");
                foreach (var m in _v.Posts) Post(_content, m);
            }
            SetFocus(0);
        }

        void StatusBar(RectTransform frame, float fw)
        {
            var (sigText, bars) = PhoneLogic.Signal(_v.Signal);
            UiKit.At(UiKit.Label("Time", frame, $"<b>{PhoneLogic.StatusTime(_v.Messages)}</b>", 19, UiKit.Ink).rectTransform, 30, 16, 120, 28);

            bool low = PhoneLogic.BatteryLowColour(_v.Battery);
            var battCol = low ? Hex("#ff8f92") : UiKit.Ink;
            float x = fw - 30;
            var pct = UiKit.Label("Batt", frame, $"<b>{PhoneLogic.BatteryText(_v.Battery)}</b>", 18, battCol, TextAnchor.UpperRight);
            UiKit.At(pct.rectTransform, x - 56, 17, 56, 26);
            x -= 62;
            var shell = UiKit.Box("BattShell", frame, new Color(1, 1, 1, 0.35f)).rectTransform;
            UiKit.At(shell, x - 26, 22, 26, 13);
            var inner = UiKit.Box("BattIn", shell, Hex("#0a0e10")).rectTransform;
            UiKit.Fill(inner, 1.5f, 1.5f);
            var fill = UiKit.Box("BattFill", inner, battCol).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2((float)(PhoneLogic.ClampBattery(_v.Battery) / 100.0), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            x -= 34;

            var note = PhoneLogic.SignalNote(bars);
            if (note != null)
            {
                var n = UiKit.Label("SigNote", frame, note, 15, Hex("#ffd27f"), TextAnchor.UpperRight);
                UiKit.At(n.rectTransform, x - 90, 20, 90, 24);
                x -= 96;
            }
            // Four bars, lit up to the signal; the spoken text is the object's name (aria-label in the original).
            var sig = UiKit.Rect("Signal " + sigText, frame);
            UiKit.At(sig, x - 21, 20, 21, 16);
            float[] hs = { 5, 9, 13, 16 };
            for (int i = 0; i < 4; i++)
                UiKit.At(UiKit.Box("Bar", sig, i < bars ? Color.white : new Color(1, 1, 1, 0.25f)).rectTransform, i * 5.5f, 16 - hs[i], 4, hs[i]);
        }

        static RectTransform Bubble(RectTransform parent, Color bg, int padL = 14, int padR = 14)
        {
            var box = UiKit.Box("Bubble", parent, bg).rectTransform;
            var lay = box.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(padL, padR, 10, 12);
            lay.spacing = 4;
            lay.childControlWidth = true; lay.childForceExpandWidth = true;
            lay.childControlHeight = true; lay.childForceExpandHeight = false;
            return box;
        }

        static void Heading(RectTransform parent, string text) =>
            UiKit.Height(UiKit.Label("Heading", parent, $"<b>{text}</b>", 21, UiKit.Ink), 32);

        // Head line: left part, and the time pinned to the right of the same line.
        static void HeadLine(RectTransform bubble, string left, string time)
        {
            var head = UiKit.Label("Head", bubble, left, 16, Hex("#b9c6cc"));
            var t = UiKit.Label("Time", head.transform, time ?? "", 16, Hex("#7d8a90"), TextAnchor.UpperRight);
            UiKit.Fill(t.rectTransform);
        }

        RectTransform Message(RectTransform parent, PhoneMessage m)
        {
            var head = PhoneLogic.Head(m);
            var kind = PhoneLogic.BubbleKind(m);
            Color bg = kind switch
            {
                "emergency" => Hex("#3a1416"),
                "safety" => Hex("#33280f"),
                "me" => Hex("#155e56"),
                _ => Hex("#232d32"),
            };
            RectTransform holder = parent;
            if (kind == "me")
            {
                // Right-aligned and at most 85 % wide.
                holder = UiKit.Rect("Me", parent);
                var row = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(60, 0, 0, 0);
                row.childControlWidth = true; row.childForceExpandWidth = true;
                row.childControlHeight = true; row.childForceExpandHeight = false;
            }
            var b = Bubble(holder, bg);
            if (kind == "emergency" || kind == "safety")
                UiKit.Box("Edge", b, kind == "emergency" ? new Color(0.9f, 0.28f, 0.3f, 0.6f) : new Color(0.95f, 0.7f, 0.24f, 0.45f))
                    .gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            string left;
            if (head.Official)
            {
                var col = kind == "emergency" ? "#ff9ea1" : "#ffd27f";
                var mark = kind == "emergency" ? "!" : "i";
                left = $"<color={col}><b>({mark}) {head.KindLabel}</b></color>" + (head.From != null ? $"  <color=#b9c6cc>{head.From}</color>" : "");
            }
            else left = $"<b>{head.From}</b>";
            HeadLine(b, left, head.Time);
            UiKit.Label("Text", b, m.Text ?? "", 20, UiKit.Ink).supportRichText = false;
            // A thin coloured edge along the left for official texts (the original's border).
            var edge = b.Find("Edge") as RectTransform;
            if (edge) { edge.anchorMin = Vector2.zero; edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f); edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(4, 0); }
            return kind == "me" ? holder : b;
        }

        // An SNS post someone passed on: handle, what it says, the share count (no author, no time... beyond the handle).
        void Post(RectTransform parent, PhoneMessage m)
        {
            var b = Bubble(parent, Hex("#1b2226"));
            HeadLine(b, $"<color=#b9c0c3>(#)</color> <b><color=#e8eded>{(string.IsNullOrEmpty(m.From) ? "SNS" : m.From)}</color></b>", m.Time);
            UiKit.Label("Text", b, m.Text ?? "", 20, UiKit.Ink).supportRichText = false;
            UiKit.Label("Foot", b, $"공유 {PhoneLogic.ShareCount(m.ShareKey ?? m.Text)}", 15, Hex("#7d8a90"));
        }

        void MapApp(RectTransform parent, float width, string state)
        {
            var map = UiKit.Box("MapApp", parent, Hex("#dfe6e2")).rectTransform;
            UiKit.Height(map.GetComponent<Image>(), 192);
            map.gameObject.AddComponent<RectMask2D>();
            bool dim = state != "ok";
            Color road = dim ? new Color(0.7f, 0.72f, 0.72f, 0.5f) : Color.white;
            Color park = dim ? new Color(0.62f, 0.66f, 0.64f, 0.5f) : Hex("#bfdcc4");
            Color block = dim ? new Color(0.78f, 0.8f, 0.8f, 0.5f) : Hex("#cdd6d1");
            // A few tiles: blocks, a park, roads.
            UiKit.At(UiKit.Box("Block", map, block).rectTransform, 20, 18, 120, 70);
            UiKit.At(UiKit.Box("Block", map, block).rectTransform, 170, 112, 140, 60);
            UiKit.At(UiKit.Box("Park", map, park).rectTransform, 250, 14, 150, 80);
            UiKit.At(UiKit.Box("Road", map, road).rectTransform, 0, 96, 600, 12);
            UiKit.At(UiKit.Box("Road", map, road).rectTransform, 150, 0, 12, 200);
            UiKit.At(UiKit.Box("Road", map, road).rectTransform, 330, 0, 8, 200);

            var (title, sub) = PhoneLogic.MapState(state);
            if (title == null) return;
            if (state == "ok")
            {
                UiKit.At(UiKit.Box("Pin", map, Hex("#5aa6ff")).rectTransform, 150, 94, 14, 14);
                Chip(map, $"<b>{title}</b>", 12, 110);
                Chip(map, sub, 12, 148);
            }
            else
            {
                var col = state == "fail" ? Hex("#8b1d21") : Hex("#2a3236");
                if (state == "loading")
                {
                    _spinner = UiKit.Box("Spinner", map, new Color(0.16f, 0.2f, 0.22f, 0.8f)).rectTransform;
                    _spinner.anchorMin = _spinner.anchorMax = new Vector2(0.5f, 1);
                    _spinner.pivot = new Vector2(0.5f, 0.5f);
                    _spinner.sizeDelta = new Vector2(22, 22);
                    _spinner.anchoredPosition = new Vector2(0, -48);
                }
                else
                {
                    var ic = UiKit.Label("Icon", map, "<b>!</b>", 30, col, TextAnchor.UpperCenter);
                    UiKit.At(ic.rectTransform, 0, 26, width, 40);
                }
                UiKit.At(UiKit.Label("Title", map, $"<b>{title}</b>", 21, col, TextAnchor.UpperCenter).rectTransform, 0, 76, width, 30);
                UiKit.At(UiKit.Label("Sub", map, sub, 17, col, TextAnchor.UpperCenter).rectTransform, 0, 110, width, 26);
            }
        }

        static void Chip(RectTransform map, string text, float x, float y)
        {
            var t = UiKit.Label("ChipText", map, text, 18, Hex("#121619"));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = t.preferredWidth + 16;
            var bg = UiKit.Box("Chip", map, new Color(1, 1, 1, 0.9f)).rectTransform;
            UiKit.At(bg, x, y, w, 30);
            t.transform.SetParent(bg, false);
            UiKit.Fill(t.rectTransform, 8, 2);
        }

        void SetFocus(int i)
        {
            if (_buttons.Count == 0) return;
            _focus = ((i % _buttons.Count) + _buttons.Count) % _buttons.Count;
            for (int k = 0; k < _buttons.Count; k++)
            {
                var o = _buttons[k].GetComponent<Outline>();
                o.effectColor = k == _focus ? UiKit.Teal : new Color(0, 0, 0, 0);
                o.effectDistance = new Vector2(3, -3);
            }
        }

        void Press(JToken id)
        {
            if (Time.unscaledTime - OpenedAt <= 0.2f) return;
            Finish(id ?? JValue.CreateNull());
        }

        protected override void Update()
        {
            base.Update();
            if (!_scrolled && _content)
            {
                // Newest message in view (scrollIntoView block: nearest).
                _scrolled = true;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                if (_lastMessage)
                {
                    var view = (RectTransform)_content.parent;
                    var corners = new Vector3[4];
                    _lastMessage.GetWorldCorners(corners);
                    float bottom = -_content.InverseTransformPoint(corners[0]).y + 14;
                    float viewH = view.rect.height;
                    if (bottom > viewH) _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, bottom - viewH);
                }
            }
            if (_spinner) _spinner.Rotate(0, 0, -360 * Time.unscaledDeltaTime);

            var kb = Keyboard.current;
            if (kb == null || Time.unscaledTime - OpenedAt < 0.2f) return;
            bool shift = kb.shiftKey.isPressed;
            if (kb.rightArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && !shift)) SetFocus(_focus + 1);
            else if (kb.leftArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && shift)) SetFocus(_focus - 1);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (_focus < _v.Actions.Count) Press(_v.Actions[_focus].Id);
            }
            else if (_scroll && kb.upArrowKey.wasPressedThisFrame) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + 0.15f);
            else if (_scroll && kb.downArrowKey.wasPressedThisFrame) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition - 0.15f);
        }
    }

    /// <summary>
    /// emergencyAlert({ kind: '위급재난문자'|'긴급재난문자'|'안전안내문자', text, time }) -> null.
    /// A white sheet dropping from the top over a red-tinted shade; 위급 shakes and the shade pulses. Keys: Enter or
    /// Space (확인 is focused) and Esc close; clicks within 0.3 s of opening are ignored, like the original.
    /// </summary>
    public class EmergencyAlertScreen : UiScreen
    {
        const float W = 860;
        string _kind, _text, _time, _level;
        Image _shade;
        float _targetY = 64;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static async Task<JToken> Open(JObject options)
        {
            var (kind, text, time, level) = PhoneLogic.ParseAlert(options);
            await Show<EmergencyAlertScreen>(s => { s._kind = kind; s._text = text; s._time = time; s._level = level; }, W, 400);
            return null;
        }

        protected override void Build()
        {
            _shade = Root.Find("Shade")?.GetComponent<Image>();
            if (_shade) _shade.color = new Color(30 / 255f, 2 / 255f, 4 / 255f, 0.55f);
            Window.GetComponent<Image>().color = Hex("#fbfbfc");
            Window.anchorMin = Window.anchorMax = Window.pivot = new Vector2(0.5f, 1);

            var headCol = _level == "critical" ? Hex("#e5484d") : _level == "urgent" ? Hex("#d97a1f") : Hex("#2f3b41");
            var head = UiKit.Box("Head", Window, headCol).rectTransform;
            UiKit.At(head, 0, 0, W, 76);
            var icon = UiKit.Label("Icon", head, $"<b>{(_level == "safety" ? "i" : "!")}</b>", 30, headCol, TextAnchor.MiddleCenter);
            var iconBg = UiKit.Box("IconBg", head, Color.white).rectTransform;
            UiKit.At(iconBg, 26, 20, 36, 36);
            icon.transform.SetParent(iconBg, false);
            UiKit.Fill(icon.rectTransform);
            UiKit.At(UiKit.Label("Kind", head, $"<b>{_kind}</b>", 30, Color.white, TextAnchor.MiddleLeft).rectTransform, 78, 0, 560, 76);
            if (!string.IsNullOrEmpty(_time))
                UiKit.At(UiKit.Label("Time", head, $"<b>{_time}</b>", 24, Color.white, TextAnchor.MiddleRight).rectTransform, W - 230, 0, 200, 76);

            var text = UiKit.Label("Text", Window, _text, 32, Hex("#121619"), style: FontStyle.Bold);
            text.supportRichText = false;
            text.lineSpacing = 1.15f;
            UiKit.At(text.rectTransform, 30, 76 + 28, W - 60, 40);
            float th = Mathf.Max(40, text.preferredHeight);
            text.rectTransform.sizeDelta = new Vector2(W - 60, th);

            float fy = 76 + 28 + th + 22;
            UiKit.At(UiKit.Label("Note", Window, "게임 속 가상 문자입니다", 19, Hex("#5c676d"), TextAnchor.MiddleLeft).rectTransform, 28, fy, 500, 60);
            var ok = UiKit.Button("Ok", Window, "확인", 24, Hex("#121619"), Color.white);
            UiKit.At((RectTransform)ok.transform, W - 28 - 170, fy, 170, 60);
            ok.onClick.AddListener(() => { if (Time.unscaledTime - OpenedAt > 0.3f) Finish(null); });
            var ring = ok.gameObject.AddComponent<Outline>(); // focused (data-autofocus)
            ring.effectColor = UiKit.Teal; ring.effectDistance = new Vector2(3, -3);

            Window.sizeDelta = new Vector2(W, fy + 60 + 26);
            Window.anchoredPosition = new Vector2(0, -_targetY + 60);
        }

        protected override void Update()
        {
            float t = Time.unscaledTime - OpenedAt;
            // Drop in (0.4 s), then 위급 shakes twice (0.5 s each); its shade pulses three times.
            float drop = Mathf.Clamp01(t / 0.4f);
            float y = -_targetY + 60 * (1 - drop) * (1 - drop);
            float x = 0;
            if (_level == "critical" && t > 0.4f && t < 1.4f) x = Mathf.Sin((t - 0.4f) * Mathf.PI * 2 / 0.125f) * 8 * (1 - (t - 0.4f));
            Window.anchoredPosition = new Vector2(x, y);
            if (_shade && _level == "critical" && t < 3f)
                _shade.color = new Color(30 / 255f + 0.25f * Mathf.Abs(Mathf.Sin(t * Mathf.PI)), 2 / 255f, 4 / 255f, 0.55f);

            var kb = Keyboard.current;
            if (kb == null || t < 0.3f) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                Finish(null);
        }
    }
}
