using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// Keyboard focus for the menu/notebook screens (the original's browser focus: Tab / Shift+Tab move, Enter and Space
    /// press, arrows step a radio group or a range). The focused control gets the original's teal focus outline. The
    /// event system's own selection is cleared every frame so Enter is never handled twice.
    /// </summary>
    public sealed class MenuFocusRing
    {
        public sealed class Stop
        {
            public RectTransform Rt;
            /// <summary>Space (and Enter, unless Enter is set).</summary>
            public Action Activate;
            /// <summary>Enter, when it differs from Space (a form control submits the form).</summary>
            public Action Enter;
            /// <summary>Arrow keys on a value control (radio group, range): -1 / +1.</summary>
            public Action<int> Adjust;
        }

        readonly List<Stop> _stops = new();
        int _i = -1;
        RectTransform _frame;

        /// <summary>Left/Right move focus on stops without Adjust (rows of buttons).</summary>
        public bool ArrowsMove = true;

        public Stop Current => _i >= 0 && _i < _stops.Count ? _stops[_i] : null;
        public int Count => _stops.Count;

        public Stop Add(RectTransform rt, Action activate, Action<int> adjust = null, Action enter = null)
        {
            var s = new Stop { Rt = rt, Activate = activate, Adjust = adjust, Enter = enter };
            _stops.Add(s);
            return s;
        }

        public Stop Add(Button b, Action enter = null)
        {
            var rt = (RectTransform)b.transform;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            return Add(rt, () => { if (b && b.interactable && b.gameObject.activeInHierarchy) b.onClick.Invoke(); }, null, enter);
        }

        public void Clear()
        {
            _stops.Clear();
            _i = -1;
            if (_frame) UnityEngine.Object.Destroy(_frame.gameObject);
            _frame = null;
        }

        public void Focus(int i)
        {
            if (_stops.Count == 0) return;
            _i = ((i % _stops.Count) + _stops.Count) % _stops.Count;
            var rt = _stops[_i].Rt;
            if (!rt) return;
            if (_frame) UnityEngine.Object.Destroy(_frame.gameObject);
            _frame = Outline(rt);
            EnsureVisible(rt);
        }

        public void Focus(RectTransform rt)
        {
            int i = _stops.FindIndex(s => s.Rt == rt);
            if (i >= 0) Focus(i);
        }

        public void Move(int d)
        {
            if (_stops.Count == 0) return;
            int start = _i < 0 ? (d > 0 ? -1 : 0) : _i;
            for (int n = 1; n <= _stops.Count; n++)
            {
                int j = (((start + d * n) % _stops.Count) + _stops.Count) % _stops.Count;
                if (_stops[j].Rt && _stops[j].Rt.gameObject.activeInHierarchy) { Focus(j); return; }
            }
        }

        /// <summary>Handles this frame's keys; true when one was used.</summary>
        public bool Tick(Keyboard kb)
        {
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (kb == null || _stops.Count == 0) return false;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            var cur = Current;
            if (kb.tabKey.wasPressedThisFrame) { Move(shift ? -1 : 1); return true; }
            if (kb.downArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
            {
                int d = kb.downArrowKey.wasPressedThisFrame ? 1 : -1;
                if (cur?.Adjust != null) cur.Adjust(d); else Move(d);
                return true;
            }
            if (kb.rightArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
            {
                int d = kb.rightArrowKey.wasPressedThisFrame ? 1 : -1;
                if (cur?.Adjust != null) { cur.Adjust(d); return true; }
                if (ArrowsMove) { Move(d); return true; }
                return false;
            }
            if (cur == null) return false;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { (cur.Enter ?? cur.Activate)?.Invoke(); return true; }
            if (kb.spaceKey.wasPressedThisFrame) { cur.Activate?.Invoke(); return true; }
            return false;
        }

        /// <summary>A 3px teal outline 2px outside rt (drawn as four edges so it never covers the control).</summary>
        static RectTransform Outline(RectTransform rt)
        {
            var root = UiKit.Rect("FocusOutline", rt);
            UiKit.Fill(root);
            var le = root.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            const float o = 2, w = 3;
            Edge(root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-o - w, o), new Vector2(o + w, o + w));   // top
            Edge(root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-o - w, -o - w), new Vector2(o + w, -o)); // bottom
            Edge(root, new Vector2(0, 0), new Vector2(0, 1), new Vector2(-o - w, -o), new Vector2(-o, o));         // left
            Edge(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(o, -o), new Vector2(o + w, o));           // right
            return root;
        }

        static void Edge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var e = UiKit.Box("Edge", parent, UiKit.Teal).rectTransform;
            e.anchorMin = aMin; e.anchorMax = aMax; e.offsetMin = oMin; e.offsetMax = oMax;
        }

        /// <summary>Scrolls the ScrollRect around rt (if any) so rt is in view.</summary>
        public static void EnsureVisible(RectTransform rt)
        {
            var sr = rt.GetComponentInParent<ScrollRect>();
            if (!sr || !sr.content || !sr.viewport || !rt.IsChildOf(sr.content)) return;
            Canvas.ForceUpdateCanvases();
            var content = sr.content;
            var r = rt.rect;
            float top = -content.InverseTransformPoint(rt.TransformPoint(new Vector3(r.xMin, r.yMax, 0))).y;
            float bottom = -content.InverseTransformPoint(rt.TransformPoint(new Vector3(r.xMin, r.yMin, 0))).y;
            float view = sr.viewport.rect.height, scroll = content.anchoredPosition.y;
            if (top - 8 < scroll) scroll = top - 8;
            else if (bottom + 8 > scroll + view) scroll = bottom + 8 - view;
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, content.rect.height - view));
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, scroll);
        }

        /// <summary>Scrolls a list by dy pixels (arrow keys on a scrolling page).</summary>
        public static void ScrollBy(RectTransform content, float dy)
        {
            var sr = content ? content.GetComponentInParent<ScrollRect>() : null;
            if (!sr || !sr.viewport) return;
            float max = Mathf.Max(0, content.rect.height - sr.viewport.rect.height);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(content.anchoredPosition.y + dy, 0, max));
        }
    }

    /// <summary>Small drawing helpers shared by the menu and notebook screens.</summary>
    public static class MenuBits
    {
        public static readonly Color Blue = Hex("#5aa6ff");
        public static readonly Color Ghost = new(1, 1, 1, 0.08f);
        public static readonly Color DarkInk = new(0.04f, 0.08f, 0.08f);
        public static readonly Color Translucent = new(12 / 255f, 18 / 255f, 21 / 255f, 0.97f); // opaque enough that the HUD and scene do not read through

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        public static Color Rgb((int r, int g, int b) c, float a = 1) => new(c.r / 255f, c.g / 255f, c.b / 255f, a);

        /// <summary>Text colour of an info label tag (style.css .tag-*).</summary>
        public static string TagHex(string label) => label switch
        {
            "official" => "#6fe0d2", "site" => "#8cc2ff", "neighbor" => "#c6b3ff", "rumor" => "#c4cbce", _ => "#ffd27f",
        };

        /// <summary>A button in the original's three looks: primary (teal), plain, ghost.</summary>
        public static Button Btn(RectTransform parent, string label, bool primary, bool ghost, Action onClick, int size = 24)
        {
            // faint light fills pre-mixed over the panel (translucent ones read two to three times as strong in linear blending)
            var bg = primary ? UiKit.Teal : ScreenDecor.Over(UiKit.Panel, Color.white, ghost ? 0.04f : 0.08f);
            var b = UiKit.Button(label, parent, label, size, bg, primary ? DarkInk : UiKit.Ink);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            return b;
        }

        public static Text Label(RectTransform parent, string name, string text, int size, Color color, float x, float y, float w, float h,
            TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.Label(name, parent, text, size, color, align, style);
            UiKit.At(t.rectTransform, x, y, w, h);
            return t;
        }

        /// <summary>A label row in a scrolling column whose height follows its wrapped text.</summary>
        public static Text Para(RectTransform column, string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.Label("Para", column, text, size, color, TextAnchor.UpperLeft, style);
            t.lineSpacing = 1.1f;
            return t;
        }

        /// <summary>A fixed-height empty row (spacing) in a column.</summary>
        public static void Gap(RectTransform column, float h) => UiKit.Height(UiKit.Rect("Gap", column), h);

        /// <summary>A row in a column that sizes to its children with padding (for wrapped text on a background).</summary>
        public static RectTransform Card(RectTransform column, Color bg, int padX = 16, int padY = 10, float spacing = 4)
        {
            var img = UiKit.Box("Card", column, bg);
            var v = img.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padX, padX, padY, padY);
            v.spacing = spacing;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            return img.rectTransform;
        }

        public static void Cue()
        {
            var h = AiOn.Story.UnityStoryHost.Instance;
            if (h) h.Cue("click");
        }
    }
}
