// Pure logic of the original ui/notebook.js: the notebook (entries sorted, filtered by info label, grouped by day) and
// the inventory (home stock vs bag rows, check/risk tags, the weight and room meters). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    public static class NotebookLogic
    {
        /// <summary>Info labels in the original order: key -> shown text (original dom.js LABELS).</summary>
        public static readonly (string key, string text)[] Labels =
        {
            ("official", "공식 안내"), ("site", "현장 확인"), ("neighbor", "이웃에게 들음"), ("rumor", "소문·미확인"), ("self", "내 기록"),
        };
        public static readonly Dictionary<string, string> LabelWhy = new()
        {
            ["official"] = "정부·지자체·재난방송·재난문자",
            ["site"] = "내 눈으로 직접 본 것",
            ["neighbor"] = "아는 사람에게 들은 것",
            ["rumor"] = "누가 처음 말했는지 모르는 것",
            ["self"] = "내가 정리하거나 한 일",
        };
        public static readonly Dictionary<string, string> Place = new()
        {
            ["office"] = "사무실", ["apartment"] = "집", ["town"] = "동네", ["mart"] = "연무마트", ["store"] = "바로편의점", ["shelter"] = "대피소", ["culture"] = "생활문화센터",
        };

        public const string Eyebrow = "정민의 수첩";
        public const string Title = "확인한 것과 들은 것";
        public const string AllFilter = "전체";
        public const string EmptyFiltered = "이 분류의 기록이 없습니다.";
        public const string EmptyAll = "아직 적은 것이 없습니다. 확인한 정보는 여기에 쌓입니다.";
        public const string LegendSummary = "출처 표시는 무엇을 뜻하나요?";
        public const string Close = "닫기";

        public static bool IsLabel(string k) => k != null && Labels.Any(l => l.key == k);
        /// <summary>The label an entry is shown and filtered under (unknown -> self).</summary>
        public static string LabelOf(string label) => IsLabel(label) ? label : "self";
        public static string LabelText(string label) => Labels.First(l => l.key == LabelOf(label)).text;

        public static string DayLabel(double d) => d < 0 ? $"D{UiJs.NumStr(d)}" : d == 0 ? "D-day" : $"D+{UiJs.NumStr(d)}";

        /// <summary>HH:MM of a minute count (null -> 00:00).</summary>
        public static string Hhmm(double? minute)
        {
            double m = minute ?? 0;
            string Pad(double v) { var s = UiJs.NumStr(v); return s.Length < 2 ? s.PadLeft(2, '0') : s; }
            return $"{Pad(JsMod(Math.Floor(m / 60), 24))}:{Pad(JsMod(m, 60))}";
        }

        static double JsMod(double a, double b) => a % b; // C# % keeps the sign like JS

        public sealed class Entry
        {
            public double? Day, Minute;
            public string Text, Label, PlaceId;
            /// <summary>Shown label key (unknown -> self).</summary>
            public string Kind => LabelOf(Label);
            public string PlaceText => PlaceId != null && Place.TryGetValue(PlaceId, out var p) ? p : null;
            public string Time => Hhmm(Minute);
        }

        /// <summary>notebook()'s entries: { entries: [...] } (also { info: [...] }, the G.info app.js passed).</summary>
        public static JArray EntriesOf(JObject o) => o?["entries"] as JArray ?? o?["info"] as JArray ?? new JArray();

        /// <summary>Entries with text, sorted by day then minute (stable, like Array.prototype.sort).</summary>
        public static List<Entry> Prepare(JToken entries)
        {
            var list = new List<Entry>();
            if (entries is not JArray arr) return list;
            foreach (var t in arr)
            {
                if (t is not JObject e || !UiJs.Truthy(e["text"])) continue;
                list.Add(new Entry
                {
                    Day = NumOrNull(e["day"]), Minute = NumOrNull(e["minute"]),
                    Text = UiJs.Str(e["text"]),
                    Label = e["label"]?.Type == JTokenType.String ? (string)e["label"] : null,
                    PlaceId = e["place"]?.Type == JTokenType.String ? (string)e["place"] : null,
                });
            }
            // (a.day - b.day) || (a.minute - b.minute); a missing value makes the difference NaN = equal.
            int Cmp(Entry a, Entry b)
            {
                double d = Diff(a.Day, b.Day);
                if (d != 0 && !double.IsNaN(d)) return d < 0 ? -1 : 1;
                double m = Diff(a.Minute, b.Minute);
                if (m != 0 && !double.IsNaN(m)) return m < 0 ? -1 : 1;
                return 0;
            }
            // Stable insertion sort (lists are short).
            for (int i = 1; i < list.Count; i++)
            {
                var x = list[i]; int j = i - 1;
                while (j >= 0 && Cmp(list[j], x) > 0) { list[j + 1] = list[j]; j--; }
                list[j + 1] = x;
            }
            return list;
        }

        // JS: undefined - n = NaN, null - n = -n
        static double Diff(double? a, double? b) => (a ?? double.NaN) - (b ?? double.NaN);

        static double? NumOrNull(JToken t)
        {
            if (t == null || t.Type == JTokenType.Undefined) return null;
            if (t.Type == JTokenType.Null) return 0; // null behaves as 0 in arithmetic
            var d = UiJs.Num(t);
            return double.IsNaN(d) ? (double?)null : d;
        }

        /// <summary>Count per label (all labels present, zero included).</summary>
        public static Dictionary<string, int> Counts(List<Entry> list) =>
            Labels.ToDictionary(l => l.key, l => list.Count(e => e.Kind == l.key));

        public static List<Entry> Filter(List<Entry> list, string filter) =>
            list.Where(e => filter == "all" || e.Kind == filter).ToList();

        /// <summary>Shown entries by day (first-seen order; a missing day is day 0).</summary>
        public static List<(double day, List<Entry> entries)> ByDay(List<Entry> shown)
        {
            var days = new List<(double, List<Entry>)>();
            foreach (var e in shown)
            {
                double k = e.Day ?? 0;
                int i = days.FindIndex(d => d.Item1 == k);
                if (i < 0) { days.Add((k, new List<Entry>())); i = days.Count - 1; }
                days[i].Item2.Add(e);
            }
            return days;
        }

        /// <summary>The empty-page line for a filter, or null when there is something to show.</summary>
        public static string EmptyText(List<Entry> list, string filter) =>
            Filter(list, filter).Count > 0 ? null : list.Count > 0 ? EmptyFiltered : EmptyAll;
    }

    // ---------- inventory ----------

    public static class InventoryLogic
    {
        public static readonly (string key, string text)[] Category =
        {
            ("water", "물"), ("food", "먹을 것"), ("info", "정보"), ("power", "전원"), ("light", "불빛"), ("health", "건강"),
            ("hygiene", "위생"), ("warmth", "보온"), ("tool", "도구"), ("fire", "화기"), ("docs", "서류·현금"),
        };
        public static readonly Dictionary<string, string> Check = new()
        {
            ["batteries"] = "건전지를 넣어 봐야 쓸 수 있음", ["charge"] = "충전해 둬야 쓸 수 있음", ["tune"] = "주파수를 맞춰 봐야 함",
        };

        public const string Eyebrow = "소지품";
        public const string Title = "집 비축과 가방";
        public const string HomeHead = "집 비축";
        public const string BagHead = "가방";
        public const string EmptyBag = "가방이 비어 있습니다.";
        public const string EmptyHome = "집에 남은 것이 없습니다.";
        public const string Note = "산 것과 쓸 수 있는 것은 다릅니다. \"점검 전\" 물건은 미리 확인해 두어야 필요할 때 씁니다.";

        public static int Order(string category) => Array.FindIndex(Category, c => c.key == category);
        public static string CategoryText(string category) { var i = Order(category); return i >= 0 ? Category[i].text : "기타"; }

        /// <summary>count(v, key): a number as is, else Number(v[key]) || 0.</summary>
        public static double Count(JToken v, string key)
        {
            if (v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) return (double)v;
            var d = v is JObject o ? UiJs.Num(o[key]) : double.NaN;
            return double.IsNaN(d) ? 0 : d;
        }

        public sealed class Tag { public string Kind, Text; public Tag(string k, string t) { Kind = k; Text = t; } } // ok, warn, risk
        public sealed class Row { public string Id, CategoryText, Name, Qty; public double N; public List<Tag> Tags = new(); }

        /// <summary>Ids from home then bag, first-seen order.</summary>
        public static List<string> Ids(JObject home, JObject bag)
        {
            var ids = new List<string>();
            foreach (var src in new[] { home, bag })
                if (src != null) foreach (var p in src.Properties()) if (!ids.Contains(p.Name)) ids.Add(p.Name);
            return ids;
        }

        /// <summary>The rows of one pane ('home' or 'bag'), sorted by category order then Korean name.</summary>
        public static List<Row> Rows(string where, JObject home, JObject bag, JObject catalog)
        {
            var src = where == "bag" ? bag : home;
            var items = new List<(string id, double n, JObject c, JToken entry)>();
            foreach (var id in Ids(home, bag))
            {
                var c = catalog?[id] as JObject ?? new JObject { ["name"] = id, ["unit"] = "개", ["category"] = "tool" };
                JToken entry = home?[id];
                if (entry == null || entry.Type == JTokenType.Null) entry = bag?[id];
                items.Add((id, Count(src?[id], where), c, entry));
            }
            var shown = items.Where(x => x.n > 0).ToList();
            // stable sort: ORDER.indexOf(category) (unknown = -1, first), then name.localeCompare(ko)
            var sorted = shown.Select((x, i) => (x, i)).OrderBy(t => t, Comparer<((string id, double n, JObject c, JToken entry) x, int i)>.Create((a, b) =>
            {
                int d = Order(Str(a.x.c["category"])) - Order(Str(b.x.c["category"]));
                if (d != 0) return d;
                int s = UiJs.KoCompare(UiJs.Str(a.x.c["name"]), UiJs.Str(b.x.c["name"]));
                return s != 0 ? s : a.i - b.i;
            })).Select(t => t.x);

            var rows = new List<Row>();
            foreach (var (id, n, c, entry) in sorted)
            {
                var unit = UiJs.Truthy(c["unit"]) ? UiJs.Str(c["unit"]) : "개";
                var row = new Row { Id = id, N = n, CategoryText = CategoryText(Str(c["category"])), Name = UiJs.Str(c["name"]), Qty = $"{UiJs.NumStr(n)}{unit}" };
                if (UiJs.Truthy(c["needsCheck"]))
                {
                    var nc = UiJs.Str(c["needsCheck"]);
                    row.Tags.Add(entry is JObject eo && UiJs.Truthy(eo["checked"])
                        ? new Tag("ok", "점검 완료")
                        : new Tag("warn", $"점검 전 · {(Check.TryGetValue(nc, out var why) ? why : "확인 필요")}"));
                }
                var risk = Str(c["risk"]);
                if (risk == "fire") row.Tags.Add(new Tag("risk", "화기 주의"));
                if (risk == "spoil") row.Tags.Add(new Tag("risk", "정전 땐 오래 못 둠"));
                rows.Add(row);
            }
            return rows;
        }

        static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : UiJs.Str(t);

        public static string EmptyText(string where) => where == "bag" ? EmptyBag : EmptyHome;

        public sealed class Meter
        {
            public string Label, State, Value; // state: ok | near | over
            public double Pct;                  // 0..100
        }

        public static Meter MakeMeter(string label, double v, JToken max, string unit, bool over, int digits = 1)
        {
            double mx = UiJs.Num(max);
            double pct = UiJs.Truthy(max) ? Math.Min(100, v / mx * 100) : 0;
            if (double.IsNaN(pct)) pct = 0; // width: NaN% is ignored by the browser
            var state = over ? "over" : pct > 80 ? "near" : "ok";
            return new Meter
            {
                Label = label, State = state, Pct = pct,
                Value = $"{UiJs.Fixed(v, digits)} / {UiJs.Str(max)}{unit}" + (over ? " · 초과" : pct > 80 ? " · 거의 참" : ""),
            };
        }

        /// <summary>The bag meters (weight, then cells or litres), or null when load or limit is missing.</summary>
        public static List<Meter> Meters(JToken load, JToken limit, JToken cells)
        {
            if (!UiJs.Truthy(load) || !UiJs.Truthy(limit)) return null;
            double N(JToken t) { var d = UiJs.Num(t); return double.IsNaN(d) ? 0 : d; }
            var lo = load as JObject ?? new JObject();
            var li = limit as JObject ?? new JObject();
            var ce = cells as JObject ?? new JObject(); // cells ? ... : any truthy cells picks the room meter
            var over = lo["over"];
            bool Over(string k) => over is JObject ov && UiJs.Truthy(ov[k]);
            var list = new List<Meter> { MakeMeter("무게", N(lo["weightKg"]), li["weightKg"], "kg", Over("weight")) };
            if (UiJs.Truthy(cells)) list.Add(MakeMeter("공간", N(ce["used"]), ce["total"], "칸", Over("volume"), 0));
            else list.Add(MakeMeter("부피", N(lo["volumeL"]), li["volumeL"], "L", Over("volume")));
            return list;
        }
    }
}
