using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Data;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One thing a bag should hold: any of <see cref="Items"/>, at least Min units (or litres, with Per).</summary>
    public sealed class BagGoal
    {
        public string Id, Label;
        public string[] Items;
        public double Min = 1;
        /// <summary>Counts these items in another unit (drinking water in litres), else 1 each.</summary>
        public Dictionary<string, double> Per;
        /// <summary>FACTS ids that explain the goal (shown as tips when it is missed).</summary>
        public string[] Facts;
    }

    public sealed class BagEval
    {
        public bool Ok;
        public double WeightKg, VolumeL;
        public List<string> Covered = new(), Missing = new(), Extra = new(), Unready = new(), Tips = new();
    }

    /// <summary>What the bag screen shows about one packable item (original prep-actions bagItems + ui/bag bagInfo).</summary>
    public sealed class BagItemView
    {
        public string Id, Name;
        public double WeightKg, VolumeL;
        public int Home, Bag, Pocket;
        public string Tip;
        public List<string> Tags = new();
        /// <summary>Usable: no check needed, or batteries in / charged. Unready gear ticks no goal.</summary>
        public bool Ready;
        /// <summary>Battery %, for a loaded device or a half-used pack; null otherwise.</summary>
        public double? Charge;
        /// <summary>The old bars' date: "check" until read, then "past"; null for everything else.</summary>
        public string Expiry;
    }

    /// <summary>
    /// The bag-packing drill (original game/drills.js + ui/bag.js rules): goals per situation, the guided -> hint -> free
    /// ladder, evaluating a packed bag and applying the bag screen's result to the game state. Pure logic.
    /// </summary>
    public static class BagDrill
    {
        static readonly string[] Water = { "water_500", "water_2l" };
        static readonly Dictionary<string, double> Litres = new() { ["water_500"] = 0.5, ["water_2l"] = 2 };
        static readonly string[] Food = { "bars", "canned", "rice_instant" };
        static BagGoal Light => new() { Id = "light", Label = "켜지는 손전등", Items = new[] { "flashlight" }, Facts = new[] { "bag_days", "power_prep" } };
        static BagGoal Info => new() { Id = "info", Label = "라디오 또는 충전된 보조배터리", Items = new[] { "radio", "powerbank" }, Facts = new[] { "bag_days", "radio_batt", "comms_battery" } };
        static BagGoal Meds => new() { Id = "meds", Label = "복용약·상비약", Items = new[] { "meds", "first_aid" }, Facts = new[] { "bag_days", "contact_fields" } };
        static BagGoal Warmth => new() { Id = "warmth", Label = "보온(은박담요·여벌 옷·우의)", Items = new[] { "blanket", "clothes", "raincoat" }, Facts = new[] { "bag_foil", "home_clothes" } };

        /// <summary>Goals per situation: 'alert' (D-3 emergency bag), 'water' (fetching water), 'evac' (the evacuation bag).</summary>
        public static readonly IReadOnlyDictionary<string, BagGoal[]> Goals = new Dictionary<string, BagGoal[]>
        {
            ["alert"] = new[]
            {
                new BagGoal { Id = "water", Label = "마실 물 2L 이상", Items = Water, Min = 2, Per = Litres, Facts = new[] { "bag_days", "water_sphere" } },
                new BagGoal { Id = "food", Label = "조리 없이 먹을 음식", Items = Food, Facts = new[] { "bag_nocook" } },
                Light, Info, Meds,
                new BagGoal { Id = "contacts", Label = "연락처 카드 또는 서류 사본", Items = new[] { "contact_card", "doc_copies" }, Facts = new[] { "contact_card", "bag_copies" } },
                Warmth,
            },
            ["water"] = new[]
            {
                new BagGoal { Id = "container", Label = "빈 물통", Items = new[] { "water_jug" }, Facts = new[] { "water_point" } },
            },
            ["evac"] = new[]
            {
                new BagGoal { Id = "water", Label = "마실 물 2L 이상", Items = Water, Min = 2, Per = Litres, Facts = new[] { "bag_days", "water_sphere" } },
                new BagGoal { Id = "food", Label = "조리 없이 먹을 음식 3개 이상", Items = Food, Min = 3, Facts = new[] { "bag_days", "bag_nocook" } },
                Light, Info, Meds,
                new BagGoal { Id = "contacts", Label = "비상연락처 카드", Items = new[] { "contact_card" }, Facts = new[] { "contact_card", "contact_fields" } },
                new BagGoal { Id = "docs", Label = "신분증·중요 서류 사본", Items = new[] { "id_card", "doc_copies" }, Facts = new[] { "bag_copies", "docs_take" } },
                new BagGoal { Id = "cash", Label = "현금", Items = new[] { "cash" }, Facts = new[] { "bag_cash" } },
                Warmth,
                new BagGoal { Id = "hygiene", Label = "위생용품", Items = new[] { "hygiene_kit", "wipes", "tissue", "masks" }, Facts = new[] { "bag_days" } },
            },
        };

        /// <summary>Screen titles per situation.</summary>
        public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
        {
            ["alert"] = "비상가방 꾸리기", ["water"] = "물 받으러 갈 준비", ["evac"] = "대피 가방 하나",
        };

        /// <summary>Why an item that fills no goal is worth a second look in a bag (FACTS ids).</summary>
        public static readonly IReadOnlyDictionary<string, string> ExtraTips = new Dictionary<string, string>
        {
            ["gas_stove"] = "shelter_rules", ["butane"] = "shelter_rules", ["candles"] = "candle_no", ["ramen"] = "bag_nocook",
            ["fridge_food"] = "food_4h", ["cooked_meat"] = "myth_meat", ["old_bars"] = "bag_nocook",
        };

        /// <summary>Game number: the way back carries up to 10 L of water, so the bag going out stays light.</summary>
        public const double WaterTripKg = 2;

        /// <summary>The 'alert' bag screen's tips (FACTS ids), as the original doBag passes them.</summary>
        public static readonly string[] AlertTips = { "bag_days", "bag_nocook", "bag_copies", "bag_foil", "bag_door" };

        // ---------- encounter ladder: guided -> hint -> free ----------

        public static string LadderStage(GameState g, string tool)
        {
            if (g?.Skill == null || !g.Skill.TryGetValue(tool, out var runs) || runs == null) throw new ArgumentException($"unknown drill tool: {tool}");
            return runs.Count == 0 ? "guided" : runs.Count == 1 ? "hint" : "free";
        }

        public static JObject RecordEncounter(GameState g, string tool, string result)
        {
            var stage = LadderStage(g, tool);
            var r = new JObject { ["day"] = g.Day, ["minute"] = g.Minute, ["stage"] = stage, ["result"] = result };
            g.Skill[tool].Add(r);
            GameRules.RecordEvent(g, "encounter", new JObject { ["tool"] = tool, ["result"] = result, ["stage"] = stage });
            return r;
        }

        // ---------- goals and evaluation ----------

        public static bool GoalMet(BagGoal goal, Func<string, int> count, Func<string, bool> ready) =>
            goal.Items.Where(ready).Sum(id => count(id) * (goal.Per != null && goal.Per.TryGetValue(id, out var p) ? p : 1)) >= goal.Min;

        public static BagEval EvalBag(GameRules rules, GameState g, string context)
        {
            if (context == null || !Goals.TryGetValue(context, out var goals)) throw new ArgumentException($"unknown bag context: {context}");
            var db = rules.Db;
            int InBag(string id) => g.Items.TryGetValue(id, out var e) ? e.Bag : 0;
            bool Ready(string id) => InBag(id) > 0 && (db.Get(id).needsCheck == ItemCheck.None || g.Items[id].Checked);
            var load = rules.BagLoad(g);
            var r = new BagEval { WeightKg = load.WeightKg, VolumeL = load.VolumeL };
            var tips = new List<string>();
            void Tip(string f) { if (!tips.Contains(f)) tips.Add(f); }

            foreach (var goal in goals)
            {
                if (GoalMet(goal, InBag, Ready)) r.Covered.Add(goal.Id);
                else { r.Missing.Add(goal.Id); foreach (var f in goal.Facts) Tip(f); }
            }
            var wanted = new HashSet<string>(goals.SelectMany(x => x.Items));
            var packed = g.Items.Keys.Where(id => InBag(id) > 0).ToList();
            r.Extra = packed.Where(id => !wanted.Contains(id)).ToList();
            foreach (var id in r.Extra) if (ExtraTips.TryGetValue(id, out var t)) Tip(t);
            r.Unready = packed.Where(id => db.Get(id).needsCheck != ItemCheck.None && !g.Items[id].Checked).ToList();
            bool heavy = context == "water" ? load.WeightKg > WaterTripKg : load.OverWeight || load.OverVolume;
            if (heavy) Tip(context == "water" ? "water_point" : "bag_foil");
            r.Tips = tips;
            r.Ok = r.Missing.Count == 0 && !heavy;
            return r;
        }

        /// <summary>Goal lines with a tick each (ok null = not judged: the free stage shows no ticks).</summary>
        public static List<(string label, bool? ok)> GoalStatus(IEnumerable<BagGoal> goals, string mode, Func<string, int> bagOf, IReadOnlyList<BagItemView> items)
        {
            bool InBag(string id) => bagOf(id) > 0 && items.FirstOrDefault(i => i.Id == id)?.Ready != false;
            return goals.Select(g => (g.Label, mode == "free" ? (bool?)null : GoalMet(g, bagOf, InBag))).ToList();
        }

        // ---------- the screen's rows ----------

        static readonly Dictionary<string, string> TagNames = new()
        {
            ["water"] = "물", ["food"] = "식품", ["info"] = "정보", ["power"] = "전원", ["light"] = "빛", ["health"] = "건강", ["hygiene"] = "위생",
            ["warmth"] = "보온", ["tool"] = "도구", ["docs"] = "서류", ["fire"] = "화기 주의", ["spoil"] = "상하기 쉬움", ["expired"] = "기한 지남",
            ["keepsake"] = "추억", ["home"] = "집에 두는 것",
        };

        public static readonly HashSet<string> WarnTags = new() { "fire", "spoil", "expired" };

        public static string TagName(string tag) => TagNames.TryGetValue(tag, out var n) ? n : null;

        /// <summary>Packable things owned (home or bag), in the state's order, with what the screen says about them.</summary>
        public static List<BagItemView> BagItems(GameState g, ItemDatabase db, Func<string, string> fact, bool withTips = true)
        {
            var list = new List<BagItemView>();
            foreach (var (id, e) in g.Items)
            {
                var c = db.Get(id);
                if (c == null || !c.Packable || e.Home + e.Bag <= 0) continue;
                var v = new BagItemView
                {
                    Id = id, Name = c.displayName, WeightKg = c.weightKg, VolumeL = c.volumeL, Home = e.Home, Bag = e.Bag, Pocket = e.Pocket ?? 0,
                    Tip = withTips && c.facts.Length > 0 ? fact(c.facts[0]) ?? "" : "",
                    Ready = c.needsCheck == ItemCheck.None || e.Checked,
                };
                v.Tags.Add(c.category.ToString().ToLowerInvariant());
                if (c.risk != ItemRisk.None) v.Tags.Add(c.risk.ToString().ToLowerInvariant());
                if (c.needsCheck == ItemCheck.Batteries && e.Checked) v.Charge = e.Charge ?? 100;
                else if (c.charge >= 0 && c.charge < 100) v.Charge = c.charge; // a half-used pack
                if (c.risk == ItemRisk.Expired) v.Expiry = g.Knows("expiryChecked") ? "past" : "check";
                list.Add(v);
            }
            return list;
        }

        /// <summary>One short status: battery left, or the old bars' date. "" when there is nothing to say.</summary>
        public static string ItemNote(BagItemView it) =>
            it.Charge.HasValue ? $"건전지 {Num.JsRound(it.Charge.Value)}%"
            : it.Expiry == "check" || (it.Expiry == null && it.Tags.Contains("expired")) ? "기한 확인 필요"
            : it.Expiry == "past" ? "기한 지남" : "";

        /// <summary>Badge tags: known ones once each; the expired badge only once the date was read.</summary>
        public static List<string> ShownTags(BagItemView it) =>
            it.Tags.Where((t, i) => TagNames.ContainsKey(t) && it.Tags.IndexOf(t) == i && (t != "expired" || it.Expiry == "past")).ToList();

        public static string MeterState(double v, double max) => v > max ? "over" : v > max * 0.8 ? "warn" : "ok";
        public static string MeterWord(string state) => state switch { "warn" => "거의 가득", "over" => "초과", _ => "여유" };
        public static string FormatKg(double kg) => (Num.JsRound(kg * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) + "kg";
        public static string FormatL(double l) => l.ToString("0.0", CultureInfo.InvariantCulture) + "L";

        // ---------- applying the screen's result ----------

        /// <summary>Packs or unpacks each item to the wanted bag count (clamped to what is owned).</summary>
        public static void ApplyBag(GameRules rules, GameState g, IReadOnlyDictionary<string, int> target)
        {
            foreach (var (id, e) in g.Items.ToList())
            {
                int want = Math.Max(0, Math.Min(target.TryGetValue(id, out var t) ? t : 0, e.Home + e.Bag));
                if (want > e.Bag) rules.Pack(g, id, want - e.Bag);
                else if (want < e.Bag) rules.Unpack(g, id, e.Bag - want);
            }
        }

        /// <summary>
        /// The end of the 'alert' bag scene (original doBag): apply the bag, judge it, climb the ladder, mark the bag
        /// packed and record 'bag_packed'. Returns the evaluation and the toast to show.
        /// </summary>
        public static (BagEval eval, string toast) FinishAlertBag(GameRules rules, GameState g, IReadOnlyDictionary<string, int> target)
        {
            ApplyBag(rules, g, target);
            var ev = EvalBag(rules, g, "alert");
            RecordEncounter(g, "bag", ev.Missing.Count > 0 ? "partial" : "complete");
            bool packed = g.Items.Values.Any(e => e.Bag > 0);
            g.SetKnow("bagPacked", packed);
            GameRules.RecordEvent(g, "bag_packed", new JObject { ["weightKg"] = ev.WeightKg, ["missing"] = new JArray(ev.Missing) });
            var labels = ev.Missing.Select(id => Goals["alert"].FirstOrDefault(x => x.Id == id)?.Label ?? id);
            var toast = ev.Missing.Count > 0 ? $"배낭을 현관에 두었다. 빠진 것: {string.Join(", ", labels)}" : "배낭을 현관에 두었다. 필요한 것을 고루 담았다.";
            return (ev, toast);
        }
    }
}
