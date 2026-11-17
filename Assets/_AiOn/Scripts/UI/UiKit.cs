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
            t.fontSize = size;
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
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h;
        }
    }
}
