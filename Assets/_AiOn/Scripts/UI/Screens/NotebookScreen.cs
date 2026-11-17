// The original ui/notebook.js screens: the notebook (what 정민 learned, by day, with where it came from) and the
// inventory (home stock vs bag). Sorting, filtering, rows, tags and meters are in NotebookLogic.cs.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Registers notebook and inventory on the story host.</summary>
    public static class NotebookScreens
    {
        public static void Register(UnityStoryHost host)
        {
            host.Screens["notebook"] = NotebookScreen.Open;
            host.Screens["inventory"] = InventoryScreen.Open;
        }
    }

    // =====================================================================================================
    // notebook({ entries: [{ day, minute, text, label, place }] }) -> null   (also accepts { info: [...] })
    // =====================================================================================================
    public class NotebookScreen : UiScreen
    {
        List<NotebookLogic.Entry> _list;
        string _filter = "all";
        readonly MenuFocusRing _ring = new();
        readonly List<(string key, Button b)> _filters = new();
        RectTransform _bodyView, _body, _legend;
        Button _legendBtn;
        bool _legendOpen;

        public static async Task<JToken> Open(JObject o)
        {
            var list = NotebookLogic.Prepare(NotebookLogic.EntriesOf(o));
            await Show<NotebookScreen>(s => s._list = list, 1400, 940);
            return null;
        }

        protected override void Build()
        {
            Title(NotebookLogic.Eyebrow, NotebookLogic.Title);
            var close = MenuBits.Btn(Window, NotebookLogic.Close, false, true, () => Finish(null), 22);
            UiKit.At((RectTransform)close.transform, 1220, 34, 140, 52);
            _ring.Add(close); // DOM order: the close button sits in the header, before the filters

            // filters: 전체 + one per label, each with its count
            var counts = NotebookLogic.Counts(_list);
            var keys = new List<string> { "all" };
            keys.AddRange(NotebookLogic.Labels.Select(l => l.key));
            float fw = (1320 - (keys.Count - 1) * 10f) / keys.Count;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                var name = k == "all" ? NotebookLogic.AllFilter : NotebookLogic.LabelText(k);
                var hex = k == "all" ? "#e8eded" : MenuBits.TagHex(k);
                var n = k == "all" ? _list.Count : counts[k];
                var b = UiKit.Button("Filter_" + k, Window, $"<color={hex}><b>{name}</b></color>   <color=#9aa6ad>{n}</color>", 21, UiKit.Row, UiKit.Ink);
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                UiKit.At((RectTransform)b.transform, 40 + i * (fw + 10), 128, fw, 52);
                b.onClick.AddListener(() => { _filter = k; Render(); _ring.Focus((RectTransform)b.transform); });
                _filters.Add((k, b));
                _ring.Add(b);
            }

            _bodyView = UiKit.Rect("Pages", Window);
            _body = UiKit.ScrollList("List", _bodyView, 8);

            // legend (original <details>)
            _legendBtn = MenuBits.Btn(Window, $"▶ {NotebookLogic.LegendSummary}", false, true, ToggleLegend, 20);
            var lt = _legendBtn.GetComponentInChildren<Text>(); lt.alignment = TextAnchor.MiddleLeft;
            _legend = UiKit.Box("Legend", Window, new Color(0, 0, 0, 0.18f)).rectTransform;
            for (int i = 0; i < NotebookLogic.Labels.Length; i++)
            {
                var (k, text) = NotebookLogic.Labels[i];
                MenuBits.Label(_legend, "Tag", $"<color={MenuBits.TagHex(k)}><b>{text}</b></color>", 20, UiKit.Ink, 20, 12 + i * 32, 200, 30);
                MenuBits.Label(_legend, "Why", NotebookLogic.LabelWhy[k], 20, UiKit.Dim, 220, 12 + i * 32, 1060, 30);
            }
            _ring.Add(_legendBtn);
            Layout();
            Render();
            _ring.Focus((RectTransform)_filters[0].b.transform); // filterBtns[0].focus()
        }

        void ToggleLegend()
        {
            _legendOpen = !_legendOpen;
            _legendBtn.GetComponentInChildren<Text>().text = $"{(_legendOpen ? "▼" : "▶")} {NotebookLogic.LegendSummary}";
            Layout();
        }

        void Layout()
        {
            float bodyH = _legendOpen ? 600 - 186 : 600;
            UiKit.At(_bodyView, 40, 196, 1320, bodyH);
            UiKit.At(_legend, 40, 196 + bodyH + 10, 1320, 176);
            _legend.gameObject.SetActive(_legendOpen);
            UiKit.At((RectTransform)_legendBtn.transform, 40, 820, 640, 48);
        }

        void Render()
        {
            foreach (var (k, b) in _filters) b.GetComponent<Image>().color = k == _filter ? UiKit.RowSelected : UiKit.Row; // aria-pressed
            Clear(_body);
            var empty = NotebookLogic.EmptyText(_list, _filter);
            if (empty != null)
            {
                UiKit.Height(UiKit.Label("Empty", _body, empty, 22, UiKit.Dim, TextAnchor.MiddleCenter), 120);
                return;
            }
            foreach (var (day, entries) in NotebookLogic.ByDay(NotebookLogic.Filter(_list, _filter)))
            {
                MenuBits.Gap(_body, 4);
                UiKit.Height(UiKit.Label("Day", _body, NotebookLogic.DayLabel(day), 28, UiKit.Ink, style: FontStyle.Bold), 40);
                foreach (var e in entries)
                {
                    var card = MenuBits.Card(_body, UiKit.Row, 18, 12, 4);
                    var place = e.PlaceText != null ? $"   <size=17><color=#9aa6ad>{e.PlaceText}</color></size>" : "";
                    MenuBits.Para(card, $"<color=#9aa6ad>{e.Time}</color>   <color={MenuBits.TagHex(e.Kind)}><b>{NotebookLogic.LabelText(e.Kind)}</b></color>{place}", 19, UiKit.Ink);
                    MenuBits.Para(card, e.Text, 22, UiKit.Ink);
                }
            }
            // body.scrollTop = body.scrollHeight: newest at the bottom, in view
            Canvas.ForceUpdateCanvases();
            var sr = _body.GetComponentInParent<ScrollRect>();
            if (sr) sr.verticalNormalizedPosition = 0;
        }

        protected override void Update()
        {
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Finish(null); return; }
            if (kb != null)
            {
                // the pages scroll with the arrow / Page keys; Tab and ←→ move between the filters and buttons
                if (kb.downArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_body, 80); return; }
                if (kb.upArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_body, -80); return; }
                if (kb.pageDownKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_body, 400); return; }
                if (kb.pageUpKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_body, -400); return; }
            }
            _ring.Tick(kb);
        }
    }

    // =====================================================================================================
    // inventory({ home, bag, catalog, load: { weightKg, volumeL, over }, limit: { weightKg, volumeL }, cells: { used, total } }) -> null
    // =====================================================================================================
    public class InventoryScreen : UiScreen
    {
        JObject _home, _bag, _catalog;
        JToken _load, _limit, _cells;
        readonly MenuFocusRing _ring = new();
        RectTransform _homeList, _bagList;

        public static async Task<JToken> Open(JObject o)
        {
            o ??= new JObject();
            await Show<InventoryScreen>(s =>
            {
                s._home = o["home"] as JObject ?? new JObject();
                s._bag = o["bag"] as JObject ?? new JObject();
                s._catalog = o["catalog"] as JObject ?? new JObject();
                s._load = o["load"]; s._limit = o["limit"]; s._cells = o["cells"];
            }, 1600, 900);
            return null;
        }

        protected override void Build()
        {
            Title(InventoryLogic.Eyebrow, InventoryLogic.Title);
            var close = MenuBits.Btn(Window, NotebookLogic.Close, false, true, () => Finish(null), 22);
            UiKit.At((RectTransform)close.transform, 1420, 34, 140, 52);
            _ring.Add(close);

            // home pane
            var home = UiKit.Rect("HomePane", Window); UiKit.At(home, 40, 130, 740, 660);
            MenuBits.Label(home, "Head", $"<color=#3cc6b5>■</color>  {InventoryLogic.HomeHead}", 28, UiKit.Ink, 0, 0, 740, 40, style: FontStyle.Bold);
            var homeBody = UiKit.Rect("Body", home); UiKit.At(homeBody, 0, 50, 740, 610);
            _homeList = UiKit.ScrollList("List", homeBody, 6);
            Rows(_homeList, "home");

            // bag pane: meters, then the list
            var bag = UiKit.Rect("BagPane", Window); UiKit.At(bag, 820, 130, 740, 660);
            MenuBits.Label(bag, "Head", $"<color=#f2b33d>■</color>  {InventoryLogic.BagHead}", 28, UiKit.Ink, 0, 0, 740, 40, style: FontStyle.Bold);
            float y = 50;
            var meters = InventoryLogic.Meters(_load, _limit, _cells);
            if (meters != null)
                foreach (var m in meters) { Meter(bag, y, m); y += 62; }
            var bagBody = UiKit.Rect("Body", bag); UiKit.At(bagBody, 0, y + 6, 740, 660 - y - 6);
            _bagList = UiKit.ScrollList("List", bagBody, 6);
            Rows(_bagList, "bag");

            MenuBits.Label(Window, "Note", InventoryLogic.Note, 20, UiKit.Dim, 40, 812, 1520, 56);
            _ring.Focus(0); // the close button
        }

        void Meter(RectTransform pane, float y, InventoryLogic.Meter m)
        {
            var color = m.State == "over" ? UiKit.Red : m.State == "near" ? UiKit.Amber : UiKit.Teal;
            MenuBits.Label(pane, "MeterLabel", m.Label, 20, UiKit.Dim, 0, y + 4, 80, 28);
            var track = UiKit.Box("Track", pane, new Color(1, 1, 1, 0.08f)).rectTransform;
            UiKit.At(track, 80, y + 10, 380, 16);
            var fill = UiKit.Box("Fill", track, color).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)(m.Pct / 100.0)), 1); // a negative CSS width is ignored
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            MenuBits.Label(pane, "MeterValue", m.Value, 20, m.State == "ok" ? UiKit.Ink : color, 476, y + 2, 264, 30, style: FontStyle.Bold);
        }

        void Rows(RectTransform list, string where)
        {
            var rows = InventoryLogic.Rows(where, _home, _bag, _catalog);
            if (rows.Count == 0)
            {
                UiKit.Height(UiKit.Label("Empty", list, InventoryLogic.EmptyText(where), 22, UiKit.Dim, TextAnchor.MiddleCenter), 80);
                return;
            }
            foreach (var r in rows)
            {
                var row = UiKit.Box("Row_" + r.Id, list, UiKit.Row).rectTransform;
                UiKit.Height(row, r.Tags.Count > 0 ? 88 : 56);
                MenuBits.Label(row, "Cat", r.CategoryText, 18, UiKit.Dim, 16, 14, 110, 28);
                MenuBits.Label(row, "Name", r.Name, 23, UiKit.Ink, 128, 11, 420, 32, style: FontStyle.Bold);
                MenuBits.Label(row, "Qty", r.Qty, 23, UiKit.Ink, 548, 11, 160, 32, TextAnchor.UpperRight);
                if (r.Tags.Count > 0)
                {
                    var tags = string.Join("    ", r.Tags.Select(t =>
                        $"<color={(t.Kind == "ok" ? "#3cc6b5" : t.Kind == "warn" ? "#f2b33d" : "#e5484d")}>{(t.Kind == "ok" ? "✓ " : "")}{t.Text}</color>"));
                    MenuBits.Label(row, "Tags", tags, 18, UiKit.Ink, 128, 50, 580, 30);
                }
            }
        }

        protected override void Update()
        {
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame)) { Finish(null); return; }
            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_homeList, 80); MenuFocusRing.ScrollBy(_bagList, 80); return; }
                if (kb.upArrowKey.wasPressedThisFrame) { MenuFocusRing.ScrollBy(_homeList, -80); MenuFocusRing.ScrollBy(_bagList, -80); return; }
            }
            _ring.Tick(kb);
        }
    }
}
