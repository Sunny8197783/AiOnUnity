using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The mart shelf (original ui/grab3d.js shop3d, contract of ui/shop.js), as a 2D screen for now: the products
    /// stand in a row of cards on shelf planks (rows like grab-layout.js: over 5 items two rows, over 10 three), each
    /// with its price tag ('1,200원 · 적음' / '품절'), the copies still standing (up to four) and how many are in the
    /// basket. Below: the basket chips (click one to put it back), 합계 / 가진 돈, the weight, 그만두기 and
    /// 계산대로 가져가기. Keys: ←/→ choose, Enter/Space take, Backspace/Delete put back, Esc leave (null).
    /// Mouse: click a card to take, right-click to put back, hover selects. The 3D grab room is a later upgrade.
    /// options { title, money, items: [{ id, name, price, unit, weightKg, volumeL, stock: ok|low|out, note, label }], info: [{ text, label }] }
    /// -> { cart: { id: n }, total } | null
    /// </summary>
    public class ShopScreen : UiScreen
    {
        public static async Task<JToken> Open(JObject options)
        {
            var state = new ShopState(ShopOptions.Parse(options));
            var r = await Show<ShopScreen>(s => s._s = state);
            return r as JToken;
        }

        public static void Register(UnityStoryHost host)
        {
            host.Screens["shop"] = Open;
        }

        // Product colours (grab3d.js LOOK), so a card reads like the thing on the shelf.
        static readonly Dictionary<string, int> Look = new()
        {
            ["water_2l"] = 0x8cc8e6, ["water_500"] = 0xa9d8ef, ["rice_instant"] = 0xf2ede3, ["canned"] = 0xc8a13c, ["ramen"] = 0xcf3b2c,
            ["bars"] = 0xe2ad38, ["radio"] = 0x3b4046, ["batteries_aa"] = 0xd6a21e, ["flashlight"] = 0x2e3338, ["powerbank"] = 0xe7ebee,
            ["candles"] = 0xf3eee2, ["first_aid"] = 0xf2f4f3, ["masks"] = 0xdbe7f1, ["wipes"] = 0x58b29e, ["tissue"] = 0xf6f6f2,
            ["trash_bags"] = 0x3f6c4e, ["tape"] = 0x8a8e93, ["raincoat"] = 0xe5d04a, ["blanket"] = 0xc3c9d0, ["water_jug"] = 0x4a8fd4,
            ["gas_stove"] = 0xc3352d, ["butane"] = 0xebe5db, ["whistle"] = 0xef8a25, ["blackout_curtain"] = 0x262a33,
        };
        static Color LookColor(string id)
        {
            int c = Look.TryGetValue(id ?? "", out var v) ? v : 0xb9a27f;
            return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
        }
        static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        static readonly Color TagAmber = Hex("#ffc04d");
        static readonly Color ToastBg = new(140 / 255f, 30 / 255f, 20 / 255f, 0.9f);
        static readonly Color FootBg = new(16 / 255f, 18 / 255f, 22 / 255f, 0.84f);
        static readonly Color Plank = new(0.36f, 0.29f, 0.2f, 1f);

        const float W = 1720, ShelfBottom = 616, FootTop = 636, CardW = 300, Gap = 22;

        ShopState _s;
        RectTransform _shelf, _chips, _toast;
        Text _toastText, _sum, _weight, _status;
        float _toastUntil;
        float _shelfTop;

        // ---------- layout ----------

        protected override void Build()
        {
            Title("", _s.Header);
            float y = 116;
            foreach (var line in _s.Options.Info)
            {
                UiKit.At(UiKit.Label("Info", Window, line, 22, new Color(0.77f, 0.8f, 0.81f)).rectTransform, 40, y, 1640, 30);
                y += 32;
            }
            _shelfTop = y + 14;
            var board = UiKit.Box("Shelf", Window, new Color(0, 0, 0, 0.22f)).rectTransform;
            UiKit.At(board, 40, _shelfTop, W - 80, ShelfBottom - _shelfTop);
            _shelf = UiKit.Rect("Cards", Window);
            UiKit.At(_shelf, 40, _shelfTop, W - 80, ShelfBottom - _shelfTop);

            var foot = UiKit.Box("Foot", Window, FootBg).rectTransform;
            UiKit.At(foot, 40, FootTop, W - 80, 940 - FootTop - 24);
            _chips = UiKit.Rect("Held", foot);
            UiKit.At(_chips, 24, 16, W - 128, 96);
            _sum = UiKit.Label("Sum", foot, "", 24, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_sum.rectTransform, 24, 122, 760, 34);
            _weight = UiKit.Label("Weight", foot, "", 24, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_weight.rectTransform, 800, 122, 500, 34);
            _status = UiKit.Label("Status", foot, "", 20, UiKit.Teal);
            UiKit.At(_status.rectTransform, 24, 170, 1000, 30);
            UiKit.At(UiKit.Label("Keys", foot, ShopState.Words.Keys, 18, UiKit.Dim).rectTransform, 24, 246, 1040, 26);
            var quit = Action(foot, ShopState.Words.Quit, false, () => Finish(null), W - 80 - 24 - 320 - 16 - 200, 180, 200, 60);
            var take = Action(foot, ShopState.Words.Checkout, true, () => Finish(_s.Result()), W - 80 - 24 - 320, 180, 320, 60);
            NoNav(quit); NoNav(take);

            _toast = UiKit.Box("Toast", Window, ToastBg).rectTransform;
            _toastText = UiKit.Label("Text", _toast, "", 24, Color.white, TextAnchor.MiddleCenter);
            UiKit.Fill(_toastText.rectTransform, 16, 4);
            _toast.gameObject.SetActive(false);

            Render();
        }

        static void NoNav(Button b) => b.navigation = new Navigation { mode = Navigation.Mode.None }; // Enter takes; it never presses a button

        // ---------- render ----------

        void Render()
        {
            Clear(_shelf);
            int n = _s.Items.Count;
            var (rows, _) = ShopState.Shelf(n);
            float h = ShelfBottom - _shelfTop, rowH = (h - 16) / rows;
            for (int r = 0; r < rows; r++) // the planks the cards stand on
                UiKit.At(UiKit.Box("Plank", _shelf, Plank).rectTransform, 12, 8 + (r + 1) * rowH - 10, W - 104, 8);
            for (int i = 0; i < n; i++)
            {
                var (row, col, cols) = ShopState.Slot(i, n);
                float x = (W - 80) / 2 + (col - (cols - 1) / 2f) * (CardW + Gap) - CardW / 2;
                Card(i, x, 8 + row * rowH + 6, rowH - 22);
            }

            Clear(_chips);
            var held = _s.Held();
            if (held.Count == 0)
                UiKit.At(UiKit.Label("Empty", _chips, ShopState.Words.Empty, 20, UiKit.Dim).rectTransform, 0, 6, 600, 30);
            float cx = 0, cy = 0;
            foreach (var (index, text) in held)
            {
                var b = UiKit.Button("Chip", _chips, text, 20, new Color(1, 1, 1, 0.08f), UiKit.Ink);
                var t = b.GetComponentInChildren<Text>();
                float w = Mathf.Ceil(t.preferredWidth) + 40;
                if (cx > 0 && cx + w > W - 128) { cx = 0; cy += 48; }
                UiKit.At((RectTransform)b.transform, cx, cy, w, 40);
                cx += w + 8;
                NoNav(b);
                int k = index;
                b.onClick.AddListener(() => PutBack(k));
            }

            _sum.text = _s.SumText;
            _weight.text = _s.WeightText;
        }

        void Card(int i, float x, float y, float h)
        {
            var it = _s.Items[i];
            bool sel = i == _s.Sel;
            var bg = UiKit.Box("Card_" + it.Id, _shelf, sel ? UiKit.RowSelected : UiKit.Row, raycast: true);
            var rt = bg.rectTransform;
            UiKit.At(rt, x, y, CardW, h);
            if (_s.Faded(i)) rt.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f; // the shelf emptied by hand
            if (sel) // amber frame like the original's glow on the chosen product
            {
                UiKit.At(UiKit.Box("Top", rt, TagAmber).rectTransform, 0, 0, CardW, 3);
                UiKit.At(UiKit.Box("Bottom", rt, TagAmber).rectTransform, 0, h - 3, CardW, 3);
                UiKit.At(UiKit.Box("Left", rt, TagAmber).rectTransform, 0, 0, 3, h);
                UiKit.At(UiKit.Box("Right", rt, TagAmber).rectTransform, CardW - 3, 0, 3, h);
            }

            // The copies still standing, one behind the other in the 3D room: here up to four small blocks.
            var look = LookColor(it.Id);
            int shown = _s.Shown(i);
            for (int k = 0; k < 4; k++)
            {
                var c = k < shown ? look : new Color(1, 1, 1, 0.06f);
                UiKit.At(UiKit.Box("Copy", rt, c).rectTransform, CardW - 20 - (4 - k) * 20, 14, 14, 36);
            }
            int dst = _s.Dst(it.Id);
            if (dst > 0)
                UiKit.At(UiKit.Label("Held", rt, $"×{dst}", 24, UiKit.Teal, TextAnchor.UpperRight, FontStyle.Bold).rectTransform, CardW - 110, 56, 94, 32);

            var name = UiKit.Label("Name", rt, it.Name, 24, UiKit.Ink, style: FontStyle.Bold);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 16; name.resizeTextMaxSize = 24;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.At(name.rectTransform, 16, 12, CardW - 120, 34);
            UiKit.At(UiKit.Label("Sub", rt, ShopState.Sub(it), 22, it.Out ? UiKit.Red : TagAmber, style: FontStyle.Bold).rectTransform, 16, 50, CardW - 120, 30);
            if (!string.IsNullOrEmpty(it.Note) && h >= 104)
                UiKit.At(UiKit.Label("Note", rt, it.Note, 18, UiKit.Dim).rectTransform, 16, 82, CardW - 32, 24);

            var click = rt.gameObject.AddComponent<ShopCardInput>();
            int idx = i;
            click.OnEnter = () => { if (_s.Pick(idx)) Render(); };
            click.OnClick = right => { _s.Pick(idx); if (right) PutBack(idx); else Take(idx); };
        }

        // ---------- actions ----------

        void Take(int i)
        {
            var (ok, say) = _s.Take(i);
            if (!ok) { if (say != null) { Toast(say); Say(say); } Render(); return; }
            Say(say);
            Render();
        }

        void PutBack(int i)
        {
            var (ok, say) = _s.PutBack(i);
            if (!ok) return;
            Say(say);
            Render();
        }

        void Say(string text) { if (_status) _status.text = text ?? ""; } // the original's live-region announcement, shown

        void Toast(string text)
        {
            _toastText.text = text;
            float w = Mathf.Ceil(_toastText.preferredWidth) + 48;
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 1);
            _toast.pivot = new Vector2(0.5f, 0.5f);
            _toast.anchoredPosition = new Vector2(0, -940 * 0.42f);
            _toast.sizeDelta = new Vector2(w, 52);
            _toast.SetAsLastSibling();
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 1.6f;
        }

        // ---------- keys ----------

        protected override void Update()
        {
            if (_s == null) return;
            if (_toast && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.gameObject.SetActive(false);
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject) EventSystem.current.SetSelectedGameObject(null);
            if (Time.unscaledTime - OpenedAt < 0.2f) return; // the key that opened the shelf
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) { Finish(null); return; }
            if (kb.rightArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
            {
                var say = _s.Step(kb.rightArrowKey.wasPressedThisFrame ? 1 : -1);
                if (say != null) Say(say);
                Render();
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Take(_s.Sel);
            else if (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame) PutBack(_s.Sel);
        }
    }

    /// <summary>Hover selects a card; left click takes, right click puts back (grab3d.js pointer handling).</summary>
    public class ShopCardInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
    {
        public Action OnEnter;
        public Action<bool> OnClick;

        public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) OnClick?.Invoke(false);
            else if (e.button == PointerEventData.InputButton.Right) OnClick?.Invoke(true);
        }
    }
}
