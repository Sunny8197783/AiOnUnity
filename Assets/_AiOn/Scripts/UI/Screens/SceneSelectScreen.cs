// The judge entry's "장면 바로가기" screen (competition upgrade): the signature moments (SceneSelect.Moments) as rows;
// a ready one starts from its prepared state, a slot a later package fills shows "준비 중". Returns the moment id, or
// null (닫기 / Esc). Texts and the list are in Story/SceneSelect.cs; this file only draws and handles input.
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    public class SceneSelectScreen : UiScreen
    {
        JArray _moments;
        readonly MenuFocusRing _ring = new() { ArrowsMove = false };
        const float W = 1500, H = 1010, RowH = 80, RowGap = 6, Top = 144; // eight moments (소화기 쓰기 added) above the close button

        public static void Register(UnityStoryHost host) => host.Screens["sceneSelect"] = Open;

        public static async Task<JToken> Open(JObject o)
        {
            var list = o?["moments"] as JArray ?? new JArray();
            var r = await Show<SceneSelectScreen>(s => s._moments = list, W, H);
            return r == null ? null : new JValue((string)r);
        }

        protected override object EscapeResult() => "";

        protected override void Build()
        {
            Title(SceneSelect.Eyebrow, SceneSelect.Title);
            MenuBits.Label(Window, "Note", SceneSelect.Note, 21, UiKit.Dim, 40, 108, W - 80, 30);

            Button first = null;
            for (int i = 0; i < _moments.Count; i++)
            {
                var m = (JObject)_moments[i];
                bool ready = m["ready"]?.Type == JTokenType.Boolean && (bool)m["ready"];
                string id = (string)m["id"];
                float y = Top + i * (RowH + RowGap);
                var bg = ready ? new Color(0.06f, 0.09f, 0.1f, 1) : new Color(0.05f, 0.065f, 0.07f, 1);
                var b = UiKit.Button("Moment_" + id, Window, "", 22, bg, UiKit.Ink);
                var rt = (RectTransform)b.transform;
                UiKit.At(rt, 40, y, W - 80, RowH);
                b.interactable = ready;
                var label = b.GetComponentInChildren<Text>();
                if (label) label.text = "";
                var dim = ready ? 1f : 0.45f;
                Color Fade(Color c) => new(c.r, c.g, c.b, c.a * dim);
                // left: chapter tag and when
                MenuBits.Label(rt, "No", (string)m["no"], 24, Fade(UiKit.Teal), 28, 14, 110, 32, style: FontStyle.Bold);
                MenuBits.Label(rt, "When", (string)m["when"] ?? "", 17, Fade(UiKit.Dim), 28, 50, 190, 26);
                // middle: title and line
                MenuBits.Label(rt, "Title", (string)m["title"], 27, Fade(UiKit.Ink), 230, 12, 900, 36, style: FontStyle.Bold);
                MenuBits.Label(rt, "Line", (string)m["line"], 20, Fade(UiKit.Dim), 230, 50, 1000, 30);
                // right: start, or the slot's "준비 중"
                var chipBg = ready ? UiKit.Teal : new Color(1, 1, 1, 0.06f);
                var chip = UiKit.Box("Chip", rt, chipBg).rectTransform;
                UiKit.At(chip, W - 80 - 28 - 150, (RowH - 44) / 2, 150, 44);
                UiKit.Fill(UiKit.Label("Text", chip, ready ? "시작 ▶" : SceneSelect.SoonLabel, 21, ready ? MenuBits.DarkInk : UiKit.Dim, TextAnchor.MiddleCenter, ready ? FontStyle.Bold : FontStyle.Normal).rectTransform);
                if (!ready) continue;
                b.onClick.AddListener(() => { MenuBits.Cue(); Finish(id); });
                _ring.Add(b);
                if (first == null) first = b;
            }

            var close = MenuBits.Btn(Window, "닫기", false, true, () => Finish(""), 24);
            UiKit.At((RectTransform)close.transform, W - 40 - 240, H - 40 - 60, 240, 60);
            _ring.Add(close);
            if (first) _ring.Focus((RectTransform)first.transform); else _ring.Focus((RectTransform)close.transform);
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime - OpenedAt < 0.2f) return;
            _ring.Tick(Keyboard.current);
        }
    }
}
