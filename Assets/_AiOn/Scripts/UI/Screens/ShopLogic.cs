using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>One product on the shelf, as the story's shopItems() hands it over (ui/shop.js item shape).</summary>
    public sealed class ShopItem
    {
        public string Id, Name, Unit, Stock = "ok", Note = "", Label = "";
        public double Price, WeightKg, VolumeL;
        public bool Out => Stock == "out";
        public bool Low => Stock == "low";
    }

    /// <summary>The option object of ui.shop: { title, money, items, info } (budgetNote is ignored, as shop3d does).</summary>
    public sealed class ShopOptions
    {
        public string Title = "진열대";
        public double Money;
        public List<ShopItem> Items = new();
        public List<string> Info = new();

        public static ShopOptions Parse(JObject o)
        {
            var r = new ShopOptions();
            if (o == null) return r;
            var title = o["title"];
            if (title != null && title.Type != JTokenType.Undefined) r.Title = title.Type == JTokenType.Null ? "null" : title.ToString(); // JS default applies only to undefined
            r.Money = NumOf(o["money"]);
            if (o["items"] is JArray items)
                foreach (var t in items.OfType<JObject>())
                    r.Items.Add(new ShopItem
                    {
                        Id = Str(t["id"]) ?? "", Name = Str(t["name"]) ?? Str(t["id"]) ?? "", Unit = Str(t["unit"]),
                        Price = NumOf(t["price"]), WeightKg = NumOf(t["weightKg"]), VolumeL = NumOf(t["volumeL"]),
                        Stock = Str(t["stock"]) ?? "ok", Note = Str(t["note"]) ?? "", Label = Str(t["label"]) ?? "",
                    });
            if (o["info"] is JArray info)
                foreach (var t in info)
                {
                    var text = t is JObject j ? Str(j["text"]) : Str(t); // shop3d shows info.map(i => i.text)
                    if (text != null) r.Info.Add(text);
                }
            return r;
        }

        static string Str(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined ? null : t.ToString();
        static double NumOf(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : 0; // `x || 0`
    }

    /// <summary>
    /// What ui/grab3d.js shop3d() keeps while the player stands at a shelf: per item how many are still on the shelf
    /// (src, 20 unless sold out) and how many are in the basket (dst), the selection, the totals, the rules for taking
    /// and putting back, and every text it shows. Pure; ShopScreen only draws it.
    /// </summary>
    public sealed class ShopState
    {
        public const int MaxQty = 20; // grab3d.js MAX_QTY
        public static class Words
        {
            public const string Keys = "클릭: 집기 · 우클릭: 내려놓기 · ←/→ 고르기 · Enter 집기 · Backspace 내려놓기 · Esc 나가기";
            public const string Empty = "장바구니가 비어 있다.";
            public const string Quit = "그만두기";
            public const string Checkout = "계산대로 가져가기";
            public const string SoldOut = "품절입니다.";
            public const string NoMore = "더 없습니다.";
            public const string NoMoney = "가진 돈이 모자란다.";
            public const string HeldLabel = "담은 물건";
            public const string ChipTitle = "하나 내려놓기";
        }

        public readonly ShopOptions Options;
        public IReadOnlyList<ShopItem> Items => Options.Items;
        readonly Dictionary<string, (int src, int dst)> _st = new();
        public int Sel { get; private set; }

        public ShopState(ShopOptions options)
        {
            Options = options ?? new ShopOptions();
            foreach (var i in Items) _st[i.Id] = (i.Out ? 0 : MaxQty, 0); // a repeated id shares one count, like the JS object
        }

        public string Header => $"장보기 · {Options.Title}";
        public int Src(string id) => _st.TryGetValue(id, out var v) ? v.src : 0;
        public int Dst(string id) => _st.TryGetValue(id, out var v) ? v.dst : 0;

        public double Total() => Items.Sum(i => i.Price * Dst(i.Id));
        public double Kg() => Items.Sum(i => i.WeightKg * Dst(i.Id));

        /// <summary>The price tag's second line: '품절' or '1,200원' (+ ' · 적음' when low).</summary>
        public static string Sub(ShopItem i) => i.Out ? "품절" : $"{Won(i.Price)}{(i.Low ? " · 적음" : "")}";
        public string SumText => $"합계 {Won(Total())} / 가진 돈 {Won(Options.Money)}";
        public string WeightText => $"들고 갈 무게 약 {BagDrill.FormatKg(Kg())}";

        /// <summary>Copies standing on the shelf (the 3D room showed up to four); 0 for sold out.</summary>
        public int Shown(int i) => Items[i].Out ? 0 : Math.Min(4, Src(Items[i].Id));
        /// <summary>The tag fades when the shelf is emptied by hand (not when it was sold out to begin with).</summary>
        public bool Faded(int i) => Shown(i) == 0 && !Items[i].Out;

        /// <summary>true, or why not (shown as a toast): sold out, none left, or the money would not cover it.</summary>
        public string WhyNot(int i)
        {
            var it = Items[i];
            if (it.Out) return Words.SoldOut;
            if (Src(it.Id) <= 0) return Words.NoMore;
            return Total() + it.Price > Options.Money ? Words.NoMoney : null;
        }

        /// <summary>Takes one of item i. ok=false: say is the toast; ok=true: say is the announcement.</summary>
        public (bool ok, string say) Take(int i)
        {
            if (i < 0 || i >= Items.Count) return (false, null);
            var why = WhyNot(i);
            if (why != null) return (false, why);
            var it = Items[i];
            var v = _st[it.Id]; _st[it.Id] = (v.src - 1, v.dst + 1);
            return (true, $"{it.Name} 하나 담음");
        }

        /// <summary>Puts one of item i back on the shelf (nothing when none is held).</summary>
        public (bool ok, string say) PutBack(int i)
        {
            if (i < 0 || i >= Items.Count) return (false, null);
            var it = Items[i];
            var v = _st[it.Id];
            if (v.dst <= 0) return (false, null);
            _st[it.Id] = (v.src + 1, v.dst - 1);
            return (true, $"{it.Name} 하나 내려놓음");
        }

        /// <summary>Selects item i (hover or click); false when it already was.</summary>
        public bool Pick(int i)
        {
            if (i < 0 || i >= Items.Count || i == Sel) return false;
            Sel = i; return true;
        }

        /// <summary>←/→: moves the selection with wrap-around; returns the announcement '이름 · 가격'.</summary>
        public string Step(int dir)
        {
            if (Items.Count == 0) return null;
            Sel = ((Sel + (dir > 0 ? 1 : Items.Count - 1)) % Items.Count);
            return $"{Items[Sel].Name} · {Sub(Items[Sel])}";
        }

        /// <summary>The basket chips: items held, in shelf order, as '이름 ×n'.</summary>
        public List<(int index, string text)> Held()
        {
            var r = new List<(int, string)>();
            var seen = new HashSet<string>();
            for (int i = 0; i < Items.Count; i++)
                if (seen.Add(Items[i].Id) && Dst(Items[i].Id) > 0) r.Add((i, $"{Items[i].Name} ×{Dst(Items[i].Id)}"));
            return r;
        }

        /// <summary>'계산대로 가져가기': { cart: { id: n } (held only), total }.</summary>
        public JObject Result()
        {
            var cart = new JObject();
            foreach (var kv in _st) if (kv.Value.dst > 0) cart[kv.Key] = kv.Value.dst;
            return new JObject { ["cart"] = cart, ["total"] = NumToken(Total()) };
        }

        public static JToken NumToken(double v) => v == Math.Floor(v) && Math.Abs(v) < 9e15 ? new JValue((long)v) : new JValue(v);

        /// <summary>mini-dom.js won(): Math.round, ko-KR grouping, '원'.</summary>
        public static string Won(double n) => Num.JsRound(n).ToString("#,0", CultureInfo.InvariantCulture) + "원";

        // ---- the shelf rows (ui/grab-layout.js shelfLayout / slotX, kind 'shop') ----

        /// <summary>Rows in use and items per row: over 10 items three rows, over 5 two, else one.</summary>
        public static (int rows, int perRow) Shelf(int n)
        {
            int count = Math.Max(1, n), nRows = count > 10 ? 3 : count > 5 ? 2 : 1, perRow = (count + nRows - 1) / nRows;
            return ((count + perRow - 1) / perRow, perRow);
        }

        /// <summary>Row, column and the number of items in that row (rows are centred; the last may hold fewer).</summary>
        public static (int row, int col, int cols) Slot(int i, int n)
        {
            var (_, perRow) = Shelf(n);
            int r = i / perRow;
            return (r, i % perRow, Math.Min(perRow, n - r * perRow));
        }
    }
}
