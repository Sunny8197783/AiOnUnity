using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Registers the debrief screens (original ui/debrief.js failCard / chapterCard / ending, ui/gallery.js,
    /// ui/records.js, ui/lounge.js) under the names app.js uses.
    /// </summary>
    public static class DebriefScreens
    {
        public static void Register(UnityStoryHost host)
        {
            host.Screens["failCard"] = FailCardScreen.Open;
            host.Screens["chapterCard"] = ChapterCardScreen.Open;
            host.Screens["ending"] = EndingScreen.Open;
            host.Screens["gallery"] = GalleryScreen.Open;
            host.Screens["records"] = RecordsScreen.Open;
            host.Screens["lounge"] = LoungeScreen.Open;
        }
    }

    /// <summary>Colours and builders shared by the debrief screens (style.css values).</summary>
    internal static class DebriefUi
    {
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        public static string Rgb(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static readonly Color Accent = Hex("#f2b33d");
        public static readonly Color TextDim = Hex("#b8c5c9");
        public static readonly Color Muted = Hex("#8ea0a6");
        // Faint light fills over the dark panel are pre-mixed opaque (ScreenDecor.Over): translucent, they blend in linear
        // space and read two to three times as strong (the .10 amber payoff box turned a mustard bar).
        public static readonly Color Line = ScreenDecor.Over(UiKit.Panel, Color.white, 0.04f);
        public static readonly Color PayoffBg = ScreenDecor.Over(UiKit.Panel, new Color(0.95f, 0.70f, 0.24f), 0.10f);
        public static readonly Color Paper = Hex("#f4f0e7");
        public static readonly Color PaperInk = Hex("#1c2326");
        public static readonly Color PaperDim = Hex("#5b666b");
        public static readonly Color PaperHead = Hex("#8a4f00");
        public static readonly Color Blue = Hex("#5aa6ff");
        public static readonly Color Ghost = ScreenDecor.Over(UiKit.Panel, Color.white, 0.08f);

        /// <summary>A scrolling column in the given rectangle of parent (top-left layout); returns its content and ScrollRect.</summary>
        public static RectTransform Scroll(RectTransform parent, float x, float y, float w, float h, out ScrollRect scroll, float spacing = 10)
        {
            var area = UiKit.Rect("Body", parent);
            UiKit.At(area, x, y, w, h);
            var content = UiKit.ScrollList("Scroll", area, spacing);
            scroll = content.parent.GetComponent<ScrollRect>();
            var bg = content.parent.GetComponent<Image>();
            if (bg) bg.color = new Color(0, 0, 0, 0); // the original panels scroll without a frame
            return content;
        }

        public static Text Para(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal)
            => UiKit.Label("P", parent, text ?? "", size, color, style: style);

        /// <summary>A padded box in a column that grows with its children; returns it for adding labels.</summary>
        public static RectTransform Card(Transform parent, Color bg, int padX = 18, int padY = 12, float spacing = 4)
        {
            var box = UiKit.Box("Row", parent, bg);
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padX, padX, padY, padY); v.spacing = spacing;
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return box.rectTransform;
        }

        /// <summary>Makes a row open its link when clicked.</summary>
        public static void Link(RectTransform row, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            var img = row.GetComponent<Image>();
            if (img) img.raycastTarget = true;
            var b = row.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => Application.OpenURL(url));
        }

        /// <summary>"&lt;b&gt;bold&lt;/b&gt;  text" plus the small line under it.</summary>
        public static string RowText(DebriefLine l, Color bold, Color small, int smallSize)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(l.Bold)) sb.Append($"<b><color={Rgb(bold)}>{l.Bold}</color></b>   ");
            sb.Append(l.Text ?? "");
            if (!string.IsNullOrEmpty(l.Small)) sb.Append($"\n<size={smallSize}><color={Rgb(small)}>{l.Small}</color></size>");
            return sb.ToString();
        }

        public static float ButtonWidth(string label) => Mathf.Max(200, 56 + label.Length * 25);
    }

    /// <summary>
    /// Keyboard for the debrief screens (the original's focus behaviour): focus starts on the primary button,
    /// Tab / Shift+Tab / ← → move it, Enter or Space presses it, ↑ ↓ PageUp PageDown Home End scroll the document.
    /// </summary>
    internal sealed class DebriefKeys
    {
        readonly List<Button> _buttons = new();
        int _sel = -1;
        public ScrollRect Scroll;

        public void Add(Button b, bool focus = false)
        {
            _buttons.Add(b);
            var o = b.gameObject.AddComponent<Outline>();
            o.effectColor = DebriefUi.Accent; o.effectDistance = new Vector2(3, -3); o.enabled = false;
            if (focus) Focus(_buttons.Count - 1);
        }

        public void Focus(int i)
        {
            if (_buttons.Count == 0) return;
            _sel = ((i % _buttons.Count) + _buttons.Count) % _buttons.Count;
            for (int k = 0; k < _buttons.Count; k++)
                if (_buttons[k]) _buttons[k].GetComponent<Outline>().enabled = k == _sel;
        }

        public void Tick()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (_buttons.Count > 0)
            {
                if (kb.tabKey.wasPressedThisFrame) Focus(_sel + (kb.shiftKey.isPressed ? -1 : 1));
                else if (kb.rightArrowKey.wasPressedThisFrame) Focus(_sel + 1);
                else if (kb.leftArrowKey.wasPressedThisFrame) Focus(_sel - 1);
                else if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) && _sel >= 0)
                {
                    var b = _buttons[_sel];
                    if (b && b.interactable && b.gameObject.activeInHierarchy) b.onClick.Invoke();
                }
            }
            if (!Scroll || !Scroll.content || !Scroll.viewport) return;
            float view = Scroll.viewport.rect.height, range = Mathf.Max(1, Scroll.content.rect.height - view);
            float px = 0;
            if (kb.downArrowKey.isPressed) px += 900 * Time.unscaledDeltaTime;
            if (kb.upArrowKey.isPressed) px -= 900 * Time.unscaledDeltaTime;
            if (kb.pageDownKey.wasPressedThisFrame) px += view * 0.9f;
            if (kb.pageUpKey.wasPressedThisFrame) px -= view * 0.9f;
            if (kb.homeKey.wasPressedThisFrame) { Scroll.verticalNormalizedPosition = 1; return; }
            if (kb.endKey.wasPressedThisFrame) { Scroll.verticalNormalizedPosition = 0; return; }
            if (px != 0) Scroll.verticalNormalizedPosition = Mathf.Clamp01(Scroll.verticalNormalizedPosition - px / range);
        }
    }

    /// <summary>Base for the debrief screens: a footer of buttons (right-aligned) wired to DebriefKeys.</summary>
    public abstract class DebriefScreenBase : UiScreen
    {
        internal readonly DebriefKeys Keys = new();
        /// <summary>The original ignores presses in the first 400 ms (the key that opened the card).</summary>
        protected bool Ready => Time.unscaledTime - OpenedAt > 0.4f;
        /// <summary>Esc closes with null (gallery, records, lounge).</summary>
        protected virtual bool EscCloses => false;

        protected float WindowW => Window.rect.width;
        protected float WindowH => Window.rect.height;

        /// <summary>Buttons laid right to left from the bottom-right corner; the last given is the rightmost (primary).</summary>
        protected List<Button> Footer(params (string label, bool primary, bool focus, Action click)[] buttons)
        {
            var made = new List<Button>();
            float x = WindowW - 40, y = WindowH - 92;
            var widths = buttons.Select(b => DebriefUi.ButtonWidth(b.label)).ToArray();
            float total = widths.Sum() + 12 * (buttons.Length - 1);
            x -= total;
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, primary, focus, click) = buttons[i];
                var b = Action(Window, label, primary, click, x, y, widths[i], 60);
                made.Add(b);
                x += widths[i] + 12;
            }
            for (int i = 0; i < made.Count; i++) Keys.Add(made[i], buttons[i].focus);
            return made;
        }

        /// <summary>
        /// Sizes the window to its scrolling content (between min and max, centred on screen): a short card is not a tall
        /// box with half of it empty. top / bottom are the space above the list and below it (the footer).
        /// </summary>
        protected void FitHeight(RectTransform content, float top, float bottom, float min = 420, float max = 940)
        {
            var area = (RectTransform)content.parent.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            // 4 px of slack when it fits: a view exactly the content's height shows a full-length scrollbar on any
            // sub-pixel excess (ScrollRect's AutoHide)
            float want = top + LayoutUtility.GetPreferredHeight(content) + bottom;
            float h = Mathf.Clamp(want < max ? want + 4 : want, min, max);
            Window.sizeDelta = new Vector2(Window.sizeDelta.x, h);
            area.sizeDelta = new Vector2(area.sizeDelta.x, h - top - bottom);
        }

        protected Text Eyebrow(RectTransform parent, string text, float x, float y, float w)
        {
            var t = UiKit.Label("Eyebrow", parent, text ?? "", 21, DebriefUi.Accent, style: FontStyle.Bold);
            UiKit.At(t.rectTransform, x, y, w, 30);
            return t;
        }

        protected Text Heading(RectTransform parent, string text, float x, float y, float w, int size = 38)
        {
            var t = UiKit.Label("Heading", parent, text ?? "", size, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(t.rectTransform, x, y, w, size + 16);
            return t;
        }

        protected override void Update()
        {
            base.Update();
            if (!this) return;
            // Not on the frame that opened the screen: the Enter that picked 엔딩 기록 / 라운지 in the menu must not press 닫기.
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            Keys.Tick();
            if (EscCloses && Time.unscaledTime - OpenedAt > 0.2f && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Finish(null);
        }

        protected static JToken Result(object r) => r is string s ? new JValue(s) : null;
    }

    /// <summary>
    /// failCard({ title, seen, missed, next, sources }) -> 'retry' | 'menu'. The soft-fail card: what you knew, what
    /// was missed, what to try next, the official sources. Enter = 체크포인트에서 다시 (focused); Esc does nothing.
    /// </summary>
    public class FailCardScreen : DebriefScreenBase
    {
        FailCardView _v;

        public static async Task<JToken> Open(JObject options)
        {
            var view = DebriefLogic.FailCard(options);
            return Result(await Show<FailCardScreen>(s => s._v = view, 1120, 940));
        }

        protected override void Build()
        {
            var top = UiKit.Box("Accent", Window, DebriefUi.Accent).rectTransform;
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(0, -6); top.offsetMax = Vector2.zero;

            var col = DebriefUi.Scroll(Window, 48, 30, WindowW - 96, WindowH - 30 - 112, out var scroll, 10);
            Keys.Scroll = scroll;
            ScrollCue.Attach(scroll, UiKit.Panel);
            DebriefUi.Para(col, _v.Eyebrow, 21, DebriefUi.Accent, FontStyle.Bold);
            DebriefUi.Para(col, _v.Title, 38, UiKit.Ink, FontStyle.Bold);
            DebriefUi.Para(col, _v.Lead, 23, DebriefUi.TextDim);
            foreach (var (_, label, lines) in _v.Rows)
            {
                var row = DebriefUi.Card(col, DebriefUi.Line, 22, 14, 6);
                UiKit.Label("Label", row, label, 20, DebriefUi.Accent, style: FontStyle.Bold);
                foreach (var t in lines) UiKit.Label("Line", row, t, 23, UiKit.Ink);
            }
            if (_v.Sources.Count > 0)
            {
                var h = DebriefUi.Para(col, _v.SourcesTitle, 24, UiKit.Ink, FontStyle.Bold);
                UiKit.Height(h, 44);
                foreach (var s in _v.Sources)
                {
                    var row = DebriefUi.Card(col, new Color(0, 0, 0, 0), 4, 2);
                    UiKit.Label("Source", row, "· " + s.Text, 21, s.Url != null ? DebriefUi.Blue : DebriefUi.TextDim);
                    DebriefUi.Link(row, s.Url);
                }
            }
            FitHeight(col, 30, 112);
            Footer(("메인 메뉴", false, false, () => Go("menu")), ("체크포인트에서 다시", true, true, () => Go("retry")));
        }

        void Go(string id) { if (Ready) Finish(id); }
    }

    /// <summary>
    /// chapterCard({ chapter, title, lines, payoffs, ripples? }) -> null. The chapter summary: the moments a
    /// preparation paid off (with the earlier moment that set it up) and the chapter's log. Enter = 다음으로.
    /// </summary>
    public class ChapterCardScreen : DebriefScreenBase
    {
        ChapterCardView _v;

        public static async Task<JToken> Open(JObject options)
        {
            var view = DebriefLogic.ChapterCard(options);
            await Show<ChapterCardScreen>(s => s._v = view, 1120, 940);
            return null;
        }

        protected override void Build()
        {
            var col = DebriefUi.Scroll(Window, 48, 30, WindowW - 96, WindowH - 30 - 112, out var scroll, 8);
            Keys.Scroll = scroll;
            ScrollCue.Attach(scroll, UiKit.Panel);
            DebriefUi.Para(col, _v.Eyebrow, 21, DebriefUi.Accent, FontStyle.Bold);
            DebriefUi.Para(col, _v.Title, 38, UiKit.Ink, FontStyle.Bold);
            if (_v.Payoffs.Count > 0)
            {
                UiKit.Height(DebriefUi.Para(col, _v.PayoffsTitle, 24, UiKit.Ink, FontStyle.Bold), 48);
                // one heading per preparation, its moments under it (not "비상가방" repeated on every row)
                foreach (var (head, items) in _v.PayoffGroups())
                {
                    var row = DebriefUi.Card(col, DebriefUi.PayoffBg, 20, 12, 4);
                    var edge = UiKit.Box("Edge", row, DebriefUi.Accent).rectTransform;
                    edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    edge.anchorMin = Vector2.zero; edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f);
                    edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(4, 0);
                    UiKit.Label("Prep", row, head, 22, DebriefUi.Accent, style: FontStyle.Bold);
                    for (int i = 0; i < items.Count; i++)
                    {
                        var p = items[i];
                        var lead = items.Count > 1 ? $"<color={DebriefUi.Rgb(DebriefUi.Accent)}>·</color>  " : "";
                        UiKit.Label("Moment", row, lead + p.Text, 22, UiKit.Ink);
                        if (p.Small != null) UiKit.Label("Cause", row, $"{(items.Count > 1 ? "    " : "")}<color={DebriefUi.Rgb(DebriefUi.Accent)}>← </color>{p.Small}", 19, DebriefUi.TextDim);
                    }
                }
            }
            UiKit.Height(DebriefUi.Para(col, _v.LinesTitle, 24, UiKit.Ink, FontStyle.Bold), 48);
            if (_v.Lines.Count > 0)
                foreach (var t in _v.Lines)
                    DebriefUi.Para(col, $"<color={DebriefUi.Rgb(DebriefUi.Accent)}><b>·</b></color>  {t}", 22, DebriefUi.TextDim);
            else DebriefUi.Para(col, _v.Empty, 22, DebriefUi.Muted);
            FitHeight(col, 30, 112);
            Footer(("다음으로", true, true, () => { if (Ready) Finish("next"); }));
        }
    }

    /// <summary>
    /// ending(record, sources) -> 'restart' | 'menu'. Options: { record, sources } or the record itself with a
    /// sources array beside its fields. The printable 생존 기록 on paper, then 결과 복사 (when there is a share text),
    /// 인쇄하기 (saves the document as a text file and opens it for printing), 메인 메뉴, 처음부터 다시 (focused).
    /// </summary>
    public class EndingScreen : DebriefScreenBase
    {
        EndingDocView _v;

        public static async Task<JToken> Open(JObject options)
        {
            var (record, sources) = DebriefLogic.EndingArgs(options);
            Func<string, string> fact = id => GameSession.Current ? GameSession.Current.Fact(id) : null;
            var view = DebriefLogic.Ending(record, sources, fact, w => $"<b><color={DebriefUi.Rgb(DebriefUi.PaperHead)}>{w}</color></b>");
            return Result(await Show<EndingScreen>(s => s._v = view, 1320, 1010));
        }

        protected override void Build()
        {
            var paper = UiKit.Box("Paper", Window, DebriefUi.Paper).rectTransform;
            UiKit.At(paper, 0, 0, WindowW, WindowH - 120);
            var col = DebriefUi.Scroll(paper, 56, 28, WindowW - 112, WindowH - 120 - 40, out var scroll, 8);
            Keys.Scroll = scroll;
            ScrollCue.Attach(scroll, DebriefUi.Paper);
            scroll.verticalScrollbar.GetComponent<Image>().color = new Color(0, 0, 0, 0.06f); // dark on the paper
            scroll.verticalScrollbar.handleRect.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);

            DebriefUi.Para(col, _v.Kicker, 19, DebriefUi.PaperDim, FontStyle.Bold);
            DebriefUi.Para(col, _v.Title, 42, DebriefUi.PaperInk, FontStyle.Bold);
            if (_v.Place != null) DebriefUi.Para(col, _v.Place, 19, DebriefUi.PaperDim, FontStyle.Bold);
            DebriefUi.Para(col, _v.Sub, 21, DebriefUi.Hex("#3a4448"));
            var rule = UiKit.Box("Rule", col, new Color(0, 0, 0, 0.3f)); UiKit.Height(rule, 3);

            foreach (var s in _v.Sections) Section(col, s);
            DebriefUi.Para(col, _v.Fiction, 18, DebriefUi.PaperDim);

            var buttons = new List<(string, bool, bool, Action)>();
            Button copy = null;
            if (!string.IsNullOrEmpty(_v.Share)) buttons.Add(("결과 복사", false, false, () => Copy(copy)));
            buttons.Add(("인쇄하기", false, false, Print));
            buttons.Add(("메인 메뉴", false, false, () => Go("menu")));
            buttons.Add(("처음부터 다시", true, true, () => Go("restart")));
            var made = Footer(buttons.ToArray());
            if (!string.IsNullOrEmpty(_v.Share)) copy = made[0];
            // a silent hold on the record before anything can be pressed: the climax lands, then the buttons fade in
            _buttons = made;
            foreach (var b in made)
            {
                b.interactable = false;
                b.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            }
        }

        const float Hold = 1.5f, FadeIn = 0.4f;
        List<Button> _buttons;
        bool _armed;

        protected override void Update()
        {
            if (!_armed && _buttons != null)
            {
                float t = Time.unscaledTime - OpenedAt;
                float a = Mathf.Clamp01((t - Hold) / FadeIn);
                foreach (var b in _buttons) if (b) b.GetComponent<CanvasGroup>().alpha = a;
                if (t >= Hold) foreach (var b in _buttons) if (b) b.interactable = true;
                if (a >= 1) _armed = true;
            }
            base.Update();
        }

        void Section(RectTransform col, DebriefSection s)
        {
            bool practice = s.Class == "doc-practice";
            RectTransform box = practice ? DebriefUi.Card(col, Color.white, 22, 16, 6) : DebriefUi.Card(col, new Color(0, 0, 0, 0), 0, 8, 6);
            if (practice) box.gameObject.AddComponent<Outline>().effectColor = DebriefUi.Hex("#c27a00");
            if (s.Title != null) UiKit.Label("H3", box, s.Title, 24, DebriefUi.PaperHead, style: FontStyle.Bold);
            foreach (var p in s.Paras)
            {
                var color = p.Class == "practice-src" ? (p.Url != null ? DebriefUi.Hex("#1d5fa8") : DebriefUi.PaperDim) : DebriefUi.PaperInk;
                var t = UiKit.Label("P", box, p.Text, p.Class == "practice-src" ? 19 : 22, color, style: p.Class == "practice-text" ? FontStyle.Bold : FontStyle.Normal);
                if (p.Url != null) { var r = DebriefUi.Card(box, new Color(0, 0, 0, 0), 0, 0); t.transform.SetParent(r, false); DebriefUi.Link(r, p.Url); }
            }
            foreach (var i in s.Items)
            {
                var row = DebriefUi.Card(box, new Color(0, 0, 0, 0.035f), 10, 7, 2);
                Color ink = i.Class == "away" || i.Class == "stranger" || i.Class == "next" ? DebriefUi.PaperDim : DebriefUi.PaperInk;
                Color bold = i.Class == "fresh" ? DebriefUi.Hex("#a4610a") : ink;
                UiKit.Label("Li", row, DebriefUi.RowText(i, bold, i.Url != null ? DebriefUi.Hex("#1d5fa8") : DebriefUi.PaperDim, 18), 21, ink);
                DebriefUi.Link(row, i.Url);
            }
            if (s.Items.Count == 0 && s.Paras.Count == 0 && !string.IsNullOrEmpty(s.Empty)) UiKit.Label("Muted", box, s.Empty, 21, DebriefUi.PaperDim);
            if (s.Tail != null) UiKit.Label("Tail", box, s.Tail.Text, 21, s.TailMuted ? DebriefUi.PaperDim : DebriefUi.PaperInk);
        }

        void Go(string id) { if (Ready && Time.unscaledTime - OpenedAt >= Hold) Finish(id); }

        void Copy(Button b)
        {
            GUIUtility.systemCopyBuffer = _v.Share;
            var label = b ? b.GetComponentInChildren<Text>() : null;
            if (label) label.text = "복사했습니다";
        }

        void Print()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "survival-record.txt");
                File.WriteAllText(path, DebriefLogic.PlainText(_v), new UTF8Encoding(true));
                Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            catch (Exception e) { Debug.LogWarning("[ending] print: " + e.Message); }
        }
    }
}
