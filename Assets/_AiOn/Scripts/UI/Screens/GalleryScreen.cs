using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// gallery(view) -> null (original ui/gallery.js). Options: { view: galleryView(...) } or { gallery: stored gallery }.
    /// The 11 endings in order; seen ones show where you ended up and three deeds that led there, unseen ones only a
    /// hint. Keys: ↑ ↓ PageUp PageDown scroll, Enter = 닫기, Esc closes.
    /// </summary>
    public class GalleryScreen : DebriefScreenBase
    {
        GalleryViewModel _v;
        protected override bool EscCloses => true;

        public static async Task<JToken> Open(JObject options)
        {
            var view = DebriefLogic.Gallery(DebriefLogic.GalleryArg(options));
            await Show<GalleryScreen>(s => s._v = view, 1240, 960);
            return null;
        }

        protected override void Build()
        {
            Eyebrow(Window, _v.Eyebrow, 48, 30, 900);
            Heading(Window, _v.Title, 48, 62, WindowW - 96);
            var col = DebriefUi.Scroll(Window, 48, 132, WindowW - 96, WindowH - 132 - 112, out var scroll, 8);
            ScrollCue.Attach(scroll, UiKit.Panel); // a thin bar and a bottom fade while more follows
            Keys.Scroll = scroll;
            foreach (var r in _v.Rows) DebriefListRows.Add(col, r);
            Footer(("닫기", true, true, () => Finish(null)));
        }
    }

    /// <summary>A gallery / records list row: number badge on the left, title, small line, sub-list.</summary>
    internal static class DebriefListRows
    {
        public static void Add(RectTransform col, DebriefListRow r)
        {
            var row = UiKit.Box("Row", col, r.Seen ? UiKit.Row : new Color(1, 1, 1, 0.03f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 18, 12, 12); h.spacing = 18;
            h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandHeight = false; h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.UpperLeft;

            var badge = UiKit.Box("No", row.transform, r.Seen ? UiKit.Teal : new Color(1, 1, 1, 0.08f));
            var ble = badge.gameObject.AddComponent<LayoutElement>(); ble.preferredWidth = ble.minWidth = 52; ble.preferredHeight = ble.minHeight = 52;
            var no = UiKit.Label("Text", badge.transform, r.No ?? "", 22, r.Seen ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Fill(no.rectTransform);

            var body = DebriefUi.Card(row.transform, new Color(0, 0, 0, 0), 0, 0, 3);
            body.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            UiKit.Label("Title", body, r.Title ?? "", 24, r.Seen ? UiKit.Ink : UiKit.Dim, style: FontStyle.Bold);
            if (!string.IsNullOrEmpty(r.Small)) UiKit.Label("Small", body, r.Small, 19, UiKit.Dim);
            foreach (var s in r.Sub) UiKit.Label("Sub", body, $"<color={DebriefUi.Rgb(UiKit.Teal)}>·</color> {s}", 20, DebriefUi.TextDim);
        }
    }
}
