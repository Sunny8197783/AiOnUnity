using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The town map (original ui/routemap.js routemap()): 연무동 drawn from TownMap (roads, crossings, buildings, park,
    /// labels, shelter entrances with status icons, closures the player knows, up to 3 route options told apart by colour
    /// AND dash) beside a side panel (help ladder, route choice, entrances, closures, legend).
    /// Keys: view — Esc or M closes; choosing — ↑/↓ change route, Enter decides, Esc closes (keeping the selection).
    /// Resolves { choice: index | null } (null when only viewing).
    /// </summary>
    public class RoutemapScreen : UiScreen
    {
        // Map box in the window, and the original's px() factor (map px per CSS px of the web canvas).
        const float MapX = 40, MapY = 124, MapW = 874, MapH = 740, Px = 1.3f;
        const float SideX = 944, SideW = 736;

        RoutemapRequest _req;
        int _sel;
        bool _helpOpen;
        MapProjector _proj;
        RectTransform _map, _top, _side;

        static readonly Color PrimaryBg = new(0.95f, 0.7f, 0.24f);
        static readonly Color PrimaryFg = new(0.106f, 0.078f, 0.024f);
        static readonly Color Muted = new(0.62f, 0.69f, 0.71f);

        // ---------- entry ----------

        public static async Task<JToken> Open(JObject options)
        {
            var req = RoutemapLogic.Parse(options);
            var result = await Show<RoutemapScreen>(s => s.Setup(req));
            return result as JToken ?? RoutemapLogic.Result(req.View, RoutemapLogic.InitialSelection(req.View));
        }

        public static void Register(AiOn.Story.UnityStoryHost host)
        {
            host.Screens["routemap"] = Open;
        }

        void Setup(RoutemapRequest req)
        {
            _req = req;
            _sel = RoutemapLogic.InitialSelection(req.View);
            _helpOpen = req.Mode == "guided";
        }

        protected override object EscapeResult() => RoutemapLogic.Result(_req.View, _sel);

        void Done() => Finish(RoutemapLogic.Result(_req.View, _sel));

        // ---------- layout ----------

        protected override void Build()
        {
            bool view = _req.View;
            Title(RoutemapLogic.Eyebrow(view), RoutemapLogic.Heading(view));
            if (_req.Mode != null && RoutemapLogic.ModeText.TryGetValue(_req.Mode, out var modeText))
            {
                var c = _req.Mode == "guided" ? UiKit.Teal : _req.Mode == "hint" ? UiKit.Amber : Muted;
                var chip = UiKit.Box("Mode", Window, new Color(c.r, c.g, c.b, 0.16f)).rectTransform;
                UiKit.At(chip, 1540, 34, 140, 40);
                UiKit.Fill(UiKit.Label("Text", chip, modeText, 22, c, TextAnchor.MiddleCenter).rectTransform);
            }
            var eyebrow = Window.Find("Kicker")?.GetComponent<Text>();
            if (eyebrow) eyebrow.color = UiKit.Amber; // .mg-eyebrow is amber

            // Map
            var frame = UiKit.Box("MapFrame", Window, new Color(1, 1, 1, 0.1f)).rectTransform;
            UiKit.At(frame, MapX - 1, MapY - 1, MapW + 2, MapH + 2);
            _map = UiKit.Box("Map", Window, new Color(0.09f, 0.125f, 0.137f)).rectTransform;
            UiKit.At(_map, MapX, MapY, MapW, MapH);
            _map.gameObject.AddComponent<RectMask2D>();
            _proj = MapProjector.Fit(MapW, MapH);
            var baseLayer = UiKit.Rect("Base", _map); UiKit.At(baseLayer, 0, 0, MapW, MapH);
            MapPainter.Paint(baseLayer, RoutemapLogic.BaseLayer(_proj, _req.Shown, Px));
            _top = UiKit.Rect("Top", _map); UiKit.At(_top, 0, 0, MapW, MapH);

            // Side panel
            var sideBox = UiKit.Rect("Side", Window); UiKit.At(sideBox, SideX, MapY, SideW, MapH);
            _side = UiKit.ScrollList("SideList", sideBox, 8);

            // Footer
            var line = UiKit.Box("FootLine", Window, new Color(1, 1, 1, 0.1f)).rectTransform;
            UiKit.At(line, 40, 878, 1640, 1);
            UiKit.At(UiKit.Label("Keys", Window, RoutemapLogic.Keys(view), 20, Muted).rectTransform, 40, 900, 1100, 30);
            var go = UiKit.Button("Primary", Window, RoutemapLogic.Primary(view), 24, PrimaryBg, PrimaryFg);
            go.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            UiKit.At((RectTransform)go.transform, 1410, 888, 270, 44);
            go.onClick.AddListener(Done);

            Render();
        }

        void Render()
        {
            Clear(_top);
            MapPainter.Paint(_top, RoutemapLogic.TopLayer(_proj, _req.Options, _sel, _req.Gates, _req.Player, _req.Dest, Px));
            Side();
        }

        void Side()
        {
            Clear(_side);
            bool view = _req.View;

            // Help ladder: guided shown, hint behind a button, free/view none.
            if (!view && (_req.Mode == "guided" || _req.Mode == "hint"))
            {
                if (_req.Mode == "hint")
                {
                    var hb = UiKit.Button("HintToggle", _side, _helpOpen ? "힌트 닫기" : "힌트 보기", 22, new Color(1, 1, 1, 0.06f), UiKit.Ink);
                    UiKit.Height(hb, 48);
                    hb.onClick.AddListener(() => { _helpOpen = !_helpOpen; Side(); });
                }
                if (_helpOpen)
                {
                    var help = UiKit.Box("Help", _side, new Color(0.24f, 0.78f, 0.71f, 0.08f));
                    var bar = UiKit.Box("Bar", help.transform, UiKit.Teal).rectTransform;
                    bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 0.5f);
                    bar.offsetMin = Vector2.zero; bar.offsetMax = new Vector2(3, 0);
                    bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    Fit(help.gameObject, 16, 12);
                    UiKit.Label("P1", help.transform, RoutemapLogic.HelpLines[0], 21, UiKit.Ink);
                    UiKit.Label("P2", help.transform, RoutemapLogic.HelpLines[1], 19, Muted);
                }
            }

            // Route choice
            if (!view)
            {
                UiKit.Height(UiKit.Label("RoutesHead", _side, $"<b>{RoutemapLogic.RoutesLegend}</b>", 23, UiKit.Ink), 34);
                int n = Math.Min(3, _req.Options.Count);
                for (int i = 0; i < n; i++) RouteRow(i);
            }

            // Shelter entrances
            UiKit.Height(UiKit.Label("GatesHead", _side, $"<b>{RoutemapLogic.GatesHead}</b>", 23, UiKit.Ink), 38);
            foreach (var (state, icon, text) in RoutemapLogic.GateLines(_req.Gates))
            {
                var col = Hex(RoutemapLogic.Gate[state].color);
                var row = UiKit.Rect("Gate", _side);
                UiKit.Height(row, 32);
                var ico = state == "closed" ? UiKit.Box("Ico", row, col) : MapPainter.Disc("Ico", row, col);
                UiKit.At(ico.rectTransform, 2, 4, 24, 24);
                UiKit.Fill(UiKit.Label("Icon", ico.transform, icon, 16, new Color(0.047f, 0.07f, 0.078f), TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
                UiKit.At(UiKit.Label("Text", row, text, 21, UiKit.Ink, TextAnchor.MiddleLeft).rectTransform, 36, 0, SideW - 60, 32);
            }

            // Known closures
            UiKit.Height(UiKit.Label("ClosuresHead", _side, $"<b>{RoutemapLogic.ClosuresHead}</b>", 23, UiKit.Ink), 38);
            var closures = RoutemapLogic.ClosureLines(_req.Shown);
            if (closures.Count == 0) UiKit.Label("None", _side, RoutemapLogic.NoClosures, 20, Muted);
            foreach (var c in closures) UiKit.Label("Closure", _side, "⊘ " + c, 21, new Color(1f, 0.69f, 0.7f));

            // Legend
            UiKit.Height(UiKit.Rect("Gap", _side), 4);
            UiKit.Label("Legend", _side, RoutemapLogic.Legend, 18, Muted);
        }

        void RouteRow(int i)
        {
            var o = _req.Options[i];
            var style = RoutemapLogic.RouteStyle[i];
            bool on = i == _sel;
            var row = UiKit.Box("Route" + i, _side, on ? new Color(0.95f, 0.7f, 0.24f, 0.12f) : new Color(1, 1, 1, 0.04f), raycast: true);
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row;
            int idx = i;
            btn.onClick.AddListener(() => { if (_sel != idx) { _sel = idx; Render(); } });
            Fit(row.gameObject, 14, 10, left: 52);

            // Radio dot and border accent (outside the layout)
            var radio = MapPainter.Disc("Radio", row.transform, on ? PrimaryBg : new Color(1, 1, 1, 0.25f));
            radio.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiKit.At(radio.rectTransform, 16, 14, 22, 22);
            if (!on) { var hole = MapPainter.Disc("Hole", radio.transform, new Color(0.09f, 0.12f, 0.13f)); UiKit.At(hole.rectTransform, 3, 3, 16, 16); }
            var edge = UiKit.Box("Edge", row.transform, on ? PrimaryBg : new Color(1, 1, 1, 0.1f)).rectTransform;
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f);
            edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(on ? 4 : 1, 0);

            // Title line with a swatch of the line style in front
            var head = UiKit.Label("Head", row.transform, $"          <b>{RoutemapLogic.RouteTitle(i, o)}</b><color=#9fb0b5><size=18>{RoutemapLogic.RouteSpec(i)}</size></color>", 22, UiKit.Ink);
            var sw = UiKit.Rect("Swatch", head.transform); UiKit.At(sw, 0, 6, 40, 16);
            var swatch = RoutemapLogic.Stroke(new List<(float, float)> { (3, 8), (37, 8) }, style.dash, 4, Rgba.Hex(style.color));
            MapPainter.Paint(sw, swatch);
            UiKit.Label("Explain", row.transform, RoutemapLogic.RouteExplain(o), 19, new Color(0.78f, 0.82f, 0.84f));
            var badge = RoutemapLogic.EntranceBadge(o);
            if (badge != null) UiKit.Label("Badge", row.transform, $"<color=#cfd8db>[{badge}]</color>", 18, UiKit.Ink);
        }

        static void Fit(GameObject go, int padX, int padY, int left = -1)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(left >= 0 ? left : padX, padX, padY, padY);
            v.spacing = 4;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
        }

        static Color Hex(string hex) { var c = Rgba.Hex(hex); return new Color(c.R, c.G, c.B, c.A); }

        // ---------- keys ----------

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime - OpenedAt < 0.2f || !this) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (_req.View)
            {
                // M closes; Enter/Space press the focused '지도 닫기' button (the only focusable, so it has focus in the original).
                if (kb.mKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Done();
                return;
            }
            int n = Math.Min(3, _req.Options.Count);
            if (kb.upArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) { _sel = RoutemapLogic.Step(_sel, -1, n); Render(); }
            else if (kb.downArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) { _sel = RoutemapLogic.Step(_sel, 1, n); Render(); }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Done();
        }
    }

    /// <summary>Turns <see cref="MapShape"/>s into uGUI Images and Texts under a top-left-anchored parent.</summary>
    public static class MapPainter
    {
        static Sprite _disc, _triangle;

        static Sprite Make(string name, Func<float, float, float> alpha)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / n, (y + 0.5f) / n));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>A white anti-aliased disc.</summary>
        public static Sprite DiscSprite => _disc ? _disc : _disc = Make("MapDisc", (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
            return (0.5f - d) * 64f + 0.5f;
        });

        /// <summary>A white upward triangle (apex at the top; texture v grows upwards).</summary>
        public static Sprite TriangleSprite => _triangle ? _triangle : _triangle = Make("MapTriangle", (u, v) =>
        {
            float half = 0.5f * (1 - v); // width at height v: full at the bottom, 0 at the top
            return (half - Mathf.Abs(u - 0.5f)) * 64f + 0.5f;
        });

        public static Image Disc(string name, Transform parent, Color color)
        {
            var img = UiKit.Box(name, parent, color);
            img.sprite = DiscSprite;
            return img;
        }

        static Color C(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        public static void Paint(RectTransform parent, IEnumerable<MapShape> shapes)
        {
            foreach (var s in shapes)
            {
                switch (s.Kind)
                {
                    case MapShapeKind.Rect:
                        UiKit.At(UiKit.Box("R", parent, C(s.Color)).rectTransform, s.X, s.Y, s.W, s.H);
                        break;
                    case MapShapeKind.Line:
                    {
                        float dx = s.X2 - s.X, dy = s.Y2 - s.Y, len = Mathf.Sqrt(dx * dx + dy * dy);
                        if (len <= 0.01f) break;
                        var rt = UiKit.Box("L", parent, C(s.Color)).rectTransform;
                        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                        rt.pivot = new Vector2(0, 0.5f);
                        rt.anchoredPosition = new Vector2(s.X, -s.Y);
                        rt.sizeDelta = new Vector2(len, Mathf.Max(0.5f, s.Width));
                        rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg);
                        break;
                    }
                    case MapShapeKind.Circle:
                        UiKit.At(Disc("C", parent, C(s.Color)).rectTransform, s.X - s.R, s.Y - s.R, s.R * 2, s.R * 2);
                        break;
                    case MapShapeKind.Diamond:
                    {
                        float side = s.R * 1.41421356f;
                        var rt = UiKit.Box("D", parent, C(s.Color)).rectTransform;
                        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.anchoredPosition = new Vector2(s.X, -s.Y);
                        rt.sizeDelta = new Vector2(side, side);
                        rt.localEulerAngles = new Vector3(0, 0, 45);
                        break;
                    }
                    case MapShapeKind.Triangle:
                    {
                        var img = UiKit.Box("T", parent, C(s.Color));
                        img.sprite = TriangleSprite;
                        UiKit.At(img.rectTransform, s.X - s.W / 2, s.Y - s.H / 2, s.W, s.H);
                        break;
                    }
                    case MapShapeKind.Text:
                    {
                        int size = Mathf.Max(8, Mathf.RoundToInt(s.Size));
                        var t = UiKit.Label("Tx", parent, s.Text, size, C(s.Color), TextAnchor.MiddleCenter, s.Weight >= 700 ? FontStyle.Bold : FontStyle.Normal);
                        t.horizontalOverflow = HorizontalWrapMode.Overflow;
                        t.supportRichText = false;
                        UiKit.At(t.rectTransform, s.X - 200, s.Y - size, 400, size * 2);
                        var outline = t.gameObject.AddComponent<Outline>();
                        outline.effectColor = C(s.Outline);
                        float d = Mathf.Max(1f, size * 0.12f);
                        outline.effectDistance = new Vector2(d, -d);
                        break;
                    }
                }
            }
        }
    }
}
