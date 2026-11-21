using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One choice of the day ledger: its id (1 | 2 | 3, or 'phone' | 'powerbank' | 'radio') and its button label.</summary>
    public readonly struct LedgerPick<T>
    {
        public readonly T Id;
        public readonly string Label;
        public LedgerPick(T id, string label) { Id = id; Label = label; }
    }

    /// <summary>The three picks before a time skip.</summary>
    public sealed class LedgerPlan
    {
        /// <summary>Litres of water a day: 1, 2 or 3.</summary>
        [JsonProperty("water")] public int Water;
        /// <summary>Meals a day: 1, 2 or 3.</summary>
        [JsonProperty("meals")] public int Meals;
        /// <summary>The daily power hour: phone | powerbank | radio.</summary>
        [JsonProperty("power")] public string Power;

        public LedgerPlan() { }
        public LedgerPlan(int water, int meals, string power) { Water = water; Meals = meals; Power = power; }
    }

    /// <summary>Units of one item the days ate or drank, by where they came from.</summary>
    public sealed class LedgerUse
    {
        [JsonProperty("home")] public int Home;
        [JsonProperty("bag")] public int Bag;
    }

    /// <summary>Totals of one simulated stretch of days.</summary>
    public sealed class LedgerRunSummary
    {
        [JsonProperty("days")] public int Days;
        /// <summary>Walks to the water point.</summary>
        [JsonProperty("trips")] public int Trips;
        /// <summary>Ramen meals cooked on the gas stove.</summary>
        [JsonProperty("cooked")] public int Cooked;
        [JsonProperty("skipped")] public int Skipped;
        /// <summary>Meals of last year's bars (past their date).</summary>
        [JsonProperty("oldBars")] public int OldBars;
    }

    /// <summary>What <see cref="Ledger.SimulateDays"/> worked out; <see cref="Ledger.ApplyLedger"/> applies it.</summary>
    public sealed class LedgerResult
    {
        [JsonProperty("plan")] public LedgerPlan Plan;
        /// <summary>home | shelter</summary>
        [JsonProperty("place")] public string Place;
        /// <summary>One { day, water, meals, power, place, drank, ate, cooked, skipped, trips, loose, gasLeft } per day (saved in G.ledger).</summary>
        [JsonProperty("rows")] public List<JObject> Rows = new();
        /// <summary>1..5 calm Korean sentences for the timelapse.</summary>
        [JsonProperty("lines")] public List<string> Lines = new();
        /// <summary>Item id -> units eaten or drunk, in the order first taken.</summary>
        [JsonProperty("use")] public Dictionary<string, LedgerUse> Use = new();
        /// <summary>Stat -> delta, already capped (thirst, fatigue, tension, health).</summary>
        [JsonProperty("stats")] public Dictionary<string, double> Stats = new();
        /// <summary>The phone's battery after the days.</summary>
        [JsonProperty("phone")] public double Phone;
        [JsonProperty("radioDrain")] public double RadioDrain;
        [JsonProperty("radioDaily")] public bool RadioDaily;
        [JsonProperty("powerbankCharged")] public bool PowerbankCharged;
        // Durable tools the days put to work (ApplyLedger marks them used).
        [JsonProperty("usedBank")] public bool UsedBank;
        [JsonProperty("usedStove")] public bool UsedStove;
        [JsonProperty("usedJug")] public bool UsedJug;
        [JsonProperty("summary")] public LedgerRunSummary Summary;
    }

    /// <summary>Totals over every ledger row so far (endings, debrief).</summary>
    public sealed class LedgerTotals
    {
        [JsonProperty("days")] public int Days;
        [JsonProperty("trips")] public int Trips;
        [JsonProperty("cooked")] public int Cooked;
        [JsonProperty("skipped")] public int Skipped;
        /// <summary>The least water a day was planned for; 0 when no day was played out.</summary>
        [JsonProperty("minWater")] public int MinWater;
    }

    /// <summary>
    /// Day ledger (하루 장부, original game/ledger.js): the three picks before a time skip (water L/day, meals/day,
    /// the daily power hour) played out day by day. Pure and deterministic; game numbers, not advice
    /// (water_sphere: 2.5~3 L to drink).
    ///
    /// Rules: stock never goes negative. At home water comes from bottles (home, then bag) and, when they run out,
    /// a trip to the water point (+10 L with water_jug, +4 L without; fatigue +6 each). Ramen needs gas_stove +
    /// butane AND place 'home' (home_stove, shelter_rules); one butane bundle cooks 12 meals. Other food: canned,
    /// rice_instant, bars, then last year's old_bars (health -4, tension +4 each). At the shelter, water and two
    /// meals a day are provided; a third meal comes from the bag. Daily radio listening drains the radio 8 %/day;
    /// the 'powerbank' hour drains the phone 6 %/day and the bank tops it up once (+60) when it falls to 20 %.
    /// The ledger alone never pushes thirst past 80 or fatigue past 85, so it cannot cause the 'exhausted' fail.
    /// </summary>
    public static class Ledger
    {
        /// <summary>The choices of each pick, in the order shown (original PICKS.water / .meals / .power).</summary>
        public static class Picks
        {
            public static readonly LedgerPick<int>[] Water = { new(1, "하루 1L"), new(2, "하루 2L"), new(3, "하루 3L") };
            public static readonly LedgerPick<int>[] Meals = { new(1, "한 끼"), new(2, "두 끼"), new(3, "세 끼") };
            public static readonly LedgerPick<string>[] Power = { new("phone", "휴대폰 충전"), new("powerbank", "보조배터리"), new("radio", "라디오 매일 듣기") };
        }

        static string WaterLabel(int id) => Picks.Water.Where(p => p.Id == id).Select(p => p.Label).FirstOrDefault();
        static string MealsLabel(int id) => Picks.Meals.Where(p => p.Id == id).Select(p => p.Label).FirstOrDefault();
        static string PowerLabel(string id) => Picks.Power.Where(p => p.Id == id).Select(p => p.Label).FirstOrDefault();

        static double Litres(string id) => id == "water_2l" ? 2 : 0.5;
        static readonly string[] Food = { "canned", "rice_instant", "bars" };
        const int RadioPerDay = 8, GasMeals = 12, TripFatigue = 6, BankLow = 20, BankTop = 60;
        /// <summary>Thirst and tiredness a day by litres drunk / meals eaten (index 1..3).</summary>
        static readonly int[] Thirst = { 0, 8, 2, -4 }, Tired = { 0, 4, 1, 0 };
        const double ThirstCap = 80, FatigueCap = 85, HealthFloor = 20;

        /// <summary>으로 after a final consonant except ㄹ (충전으로, 라디오 매일 듣기로, 보조배터리로).</summary>
        static string Ro(string w)
        {
            int c = w[w.Length - 1] - 0xAC00, t = c % 28;
            return c >= 0 && c < 11172 && t != 0 && t != 8 ? "으로" : "로";
        }

        /// <summary>한 번, 두 끼, 12번.</summary>
        static string Times(int n, string unit) =>
            n >= 1 && n <= 4 ? Counters[n - 1] + " " + unit : n.ToString(CultureInfo.InvariantCulture) + unit;
        static readonly string[] Counters = { "한", "두", "세", "네" };

        /// <summary>
        /// A number as the original prints it in a sentence (JavaScript's shortest text that reads back to the same
        /// double). Whole numbers print plain; a radio charge like 41.3 run down by 8 a day prints its float noise
        /// ("25.299999999999997"), exactly as the original does.
        /// </summary>
        static string JsNumber(double x)
        {
            if (x == Math.Floor(x) && Math.Abs(x) < 1e15) return ((long)x).ToString(CultureInfo.InvariantCulture);
            foreach (var format in new[] { "G15", "G16", "G17" })
            {
                var s = x.ToString(format, CultureInfo.InvariantCulture);
                if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) == x) return s;
            }
            return x.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>'하루 배분: 물 2L · 두 끼 · 보조배터리' (chapter summary).</summary>
        public static string PlanLine(LedgerPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return $"하루 배분: 물 {plan.Water.ToString(CultureInfo.InvariantCulture)}L · {MealsLabel(plan.Meals) ?? "undefined"} · {PowerLabel(plan.Power) ?? "undefined"}";
        }

        /// <summary>The plan as a 'ledger' event keeps it: { water, meals, power }.</summary>
        public static string PlanLine(JToken plan)
        {
            if (plan is not JObject o) throw new ArgumentException("a ledger plan is { water, meals, power }", nameof(plan));
            return PlanLine(o.ToObject<LedgerPlan>());
        }

        static void CheckArgs(LedgerPlan plan, int days, string place)
        {
            if (plan == null || WaterLabel(plan.Water) == null || MealsLabel(plan.Meals) == null || PowerLabel(plan.Power) == null)
                throw new ArgumentException($"bad ledger plan: {(plan == null ? "null" : JsonConvert.SerializeObject(plan))}", nameof(plan));
            if (days < 1 || days > 30) throw new ArgumentOutOfRangeException(nameof(days), $"days must be 1..30, got {days}");
            if (place != "home" && place != "shelter") throw new ArgumentException($"place must be 'home' or 'shelter', got {place}", nameof(place));
        }

        static double NumberOr0(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : 0;

        /// <summary>
        /// Plays the plan out for <paramref name="days"/> whole days from g.Day on (rows are g.Day .. g.Day + days - 1),
        /// at 'home' or at the 'shelter'. Does NOT change the game: <see cref="ApplyLedger"/> does.
        /// </summary>
        public static LedgerResult SimulateDays(GameState g, LedgerPlan plan, int days, string place = "home")
        {
            CheckArgs(plan, days, place);
            // Where the days may eat from: home first, then the bag; at the shelter only the bag.
            string[] places = place == "home" ? new[] { "home", "bag" } : new[] { "bag" };
            var left = new Dictionary<string, int>();
            var use = new Dictionary<string, LedgerUse>();
            int At(string id, string k) => g.Items.TryGetValue(id, out var e) && e != null ? (k == "home" ? e.Home : e.Bag) : 0;
            int Used(string id, string k) => use.TryGetValue(id, out var u) ? (k == "home" ? u.Home : u.Bag) : 0;
            int Count(string id) => places.Sum(k => At(id, k)) - (left.TryGetValue(id, out var n) ? n : 0);
            bool TakeOne(string id)
            {
                var k = places.FirstOrDefault(x => At(id, x) - Used(id, x) > 0);
                if (k == null) return false;
                if (!use.TryGetValue(id, out var u)) use[id] = u = new LedgerUse();
                if (k == "home") u.Home += 1; else u.Bag += 1;
                left[id] = (left.TryGetValue(id, out var n) ? n : 0) + 1;
                return true;
            }
            bool Owns(string id) => At(id, "home") + At(id, "bag") > 0;

            // Open water and gas carry over from the last day already played out.
            var prev = g.Ledger != null && g.Ledger.Count > 0 ? g.Ledger[g.Ledger.Count - 1] : null;
            double loose = NumberOr0(prev?["loose"]);
            int gasLeft = (int)NumberOr0(prev?["gasLeft"]);
            bool jug = Owns("water_jug"), stove = place == "home" && Owns("gas_stove");
            g.Items.TryGetValue("radio", out var radio);
            bool radioOn = plan.Power == "radio" && radio != null && radio.Checked;
            bool bank = plan.Power == "powerbank" && Owns("powerbank");
            double startCharge = radioOn ? radio.Charge ?? 100 : 0, radioCharge = startCharge;
            int radioDays = 0;
            double thirst = 0, fatigue = 0, tension = 0, health = 0;
            double phone = g.Phone.Battery;
            bool topped = false;
            var rows = new List<JObject>();
            int totTrips = 0, totCooked = 0, totSkipped = 0, totOldBars = 0;

            for (int i = 0; i < days; i++)
            {
                // Water: what is open first, then a bottle that fits the need, then the water point.
                double need = place == "shelter" ? 0 : plan.Water;
                int trips = 0;
                void Sip() { double d = Math.Min(loose, need); loose = Num.Round1(loose - d); need = Num.Round1(need - d); }
                Sip();
                while (need > 0)
                {
                    var bottle = (need >= 2 ? new[] { "water_2l", "water_500" } : new[] { "water_500", "water_2l" }).FirstOrDefault(x => Count(x) > 0);
                    if (bottle == null) break;
                    TakeOne(bottle); loose = Num.Round1(loose + Litres(bottle)); Sip();
                }
                if (need > 0) { trips++; loose = Num.Round1(loose + (jug ? 10 : 4)); fatigue += TripFatigue; Sip(); }
                thirst += Thirst[place == "shelter" ? 2 : plan.Water]; // the shelter hands out water

                // Meals: provided at the shelter (two a day), otherwise cooked ramen first, then no-cook food.
                int ate = 0, cooked = 0, skipped = 0;
                for (int m = place == "shelter" ? Math.Min(2, plan.Meals) : 0; m < plan.Meals; m++)
                {
                    if (stove && Count("ramen") > 0 && (gasLeft > 0 || Count("butane") > 0))
                    {
                        if (gasLeft == 0) { TakeOne("butane"); gasLeft = GasMeals; }
                        TakeOne("ramen"); gasLeft--; cooked++; ate++;
                        continue;
                    }
                    var food = Food.FirstOrDefault(x => Count(x) > 0);
                    if (food != null) { TakeOne(food); ate++; continue; }
                    if (TakeOne("old_bars")) { ate++; totOldBars++; health -= 4; tension += 4; continue; }
                    skipped++; fatigue += 3; tension += 2;
                }
                if (place == "shelter") ate += Math.Min(2, plan.Meals);
                fatigue += Tired[plan.Meals];

                // The power hour.
                if (plan.Power == "phone" || (!bank && !radioOn)) phone = 100;
                else phone = Math.Max(0, phone - PhoneBattery.Standby(g, bank)); // 절전 모드 halves the standby (competition upgrade)
                // The charged bank tops the phone up once when it runs low (and is charged again in the power hour).
                if (bank && !topped && phone <= BankLow) { phone = Math.Min(100, phone + BankTop); topped = true; }
                if (radioOn && radioCharge > 0) { radioCharge = Math.Max(0, radioCharge - RadioPerDay); radioDays++; tension -= 2; }

                rows.Add(new JObject
                {
                    ["day"] = g.Day + i, ["water"] = plan.Water, ["meals"] = plan.Meals, ["power"] = plan.Power, ["place"] = place,
                    ["drank"] = plan.Water, ["ate"] = ate, ["cooked"] = cooked, ["skipped"] = skipped, ["trips"] = trips,
                    ["loose"] = loose, ["gasLeft"] = gasLeft,
                });
                totTrips += trips; totCooked += cooked; totSkipped += skipped;
            }

            // The ledger alone never pushes a stat past its cap (or health under its floor); one already past it stays.
            var stats = new Dictionary<string, double>();
            void Delta(string stat, double cur, double d, double? cap, double? floor)
            {
                double hi = cap.HasValue ? Math.Max(cur, cap.Value) : 100, lo = floor.HasValue ? Math.Min(cur, floor.Value) : 0;
                stats[stat] = Math.Min(hi, Math.Max(lo, cur + d)) - cur;
            }
            Delta("thirst", g.Stats.Thirst, thirst, ThirstCap, null);
            Delta("fatigue", g.Stats.Fatigue, fatigue, FatigueCap, null);
            Delta("tension", g.Stats.Tension, tension, null, null);
            Delta("health", g.Stats.Health, health, null, HealthFloor);

            string picked = PlanLine(plan).Replace("하루 배분: ", "하루 ");
            var lines = new List<string> { $"{picked}{Ro(PowerLabel(plan.Power))} {days.ToString(CultureInfo.InvariantCulture)}일을 보냈다." };
            if (place == "shelter") lines.Add("임시 거처에서 물을 받고, 하루 두 번 배식을 받았다.");
            if (totTrips > 0) lines.Add($"급수소에 {Times(totTrips, "번")} 다녀왔다. {(jug ? "접이식 물통 덕분에 한 번에 10L씩 받아 왔다." : "물통이 없어 한 번에 4L만 들고 올 수 있었다.")}");
            if (totCooked > 0) lines.Add($"휴대용 가스레인지로 라면을 {Times(totCooked, "번")} 끓였다.");
            if (totOldBars > 0) lines.Add("작년 가방에서 나온 건빵을 먹었다. 기한이 지나 속이 편하지 않았다.");
            if (totSkipped > 0) lines.Add($"먹을 것이 모자라 {Times(totSkipped, "끼")}를 걸렀다.");
            if (plan.Water == 1 && place == "home") lines.Add("물을 아끼느라 목이 자주 말랐다.");
            if (plan.Power == "radio" && !radioOn) lines.Add("라디오를 켤 수 없어 그 시간에 휴대폰을 충전했다.");
            else if (radioOn) lines.Add(radioCharge == 0 ? "매일 라디오를 듣다 보니 건전지가 다 닳았다." : $"매일 라디오로 안내를 들었다. 건전지가 {JsNumber(radioCharge)}% 남았다.");
            if (plan.Power == "powerbank" && !bank) lines.Add("보조배터리가 없어 그 시간에 휴대폰을 충전했다.");
            else if (topped) lines.Add("휴대폰이 꺼져 갈 때 보조배터리로 한 번 채웠다.");
            if (PhoneBattery.Saver(g) && plan.Power != "phone") lines.Add("절전 모드 덕분에 휴대폰 배터리가 덜 닳았다.");

            return new LedgerResult
            {
                Plan = new LedgerPlan(plan.Water, plan.Meals, plan.Power), Place = place, Rows = rows, Lines = lines.Take(5).ToList(), Use = use, Stats = stats, Phone = phone,
                RadioDrain = radioOn ? startCharge - radioCharge : 0, RadioDaily = radioDays > 0, PowerbankCharged = bank, UsedBank = topped,
                UsedStove = totCooked > 0, UsedJug = jug && totTrips > 0,
                Summary = new LedgerRunSummary { Days = days, Trips = totTrips, Cooked = totCooked, Skipped = totSkipped, OldBars = totOldBars },
            };
        }

        /// <summary>
        /// Applies a <see cref="SimulateDays"/> result: consumes the items, applies the stats, the phone and the radio
        /// charge, charges the powerbank, sets know.radioDaily, appends the rows to g.Ledger and records
        /// 'ledger' { plan, place, days, trips, cooked, skipped, oldBars }. Returns the rows.
        /// </summary>
        public static List<JObject> ApplyLedger(GameRules rules, GameState g, LedgerResult result)
        {
            if (result?.Rows == null || result.Rows.Count == 0) throw new ArgumentException("applyLedger needs a simulateDays result", nameof(result));
            foreach (var (id, n) in result.Use)
            {
                if (rules.Db.Get(id) == null) throw new ArgumentException($"unknown item: {id}");
                if (n.Home != 0) rules.Consume(g, id, n.Home, "home");
                if (n.Bag != 0) rules.Consume(g, id, n.Bag, "bag");
            }
            foreach (var (stat, d) in result.Stats) if (d != 0 && !double.IsNaN(d)) GameRules.Adjust(g, stat, d);
            g.Phone.Battery = result.Phone;
            if (result.RadioDrain != 0) rules.DrainCharge(g, "radio", result.RadioDrain);
            if (result.RadioDaily) g.SetKnow("radioDaily", true);
            if (result.PowerbankCharged) rules.Check(g, "powerbank");
            if (result.UsedBank) rules.Use(g, "powerbank");
            // Durable tools the days put to work count as used (debrief: not 'bought but never needed').
            if (result.UsedStove) rules.Use(g, "gas_stove");
            if (result.UsedJug) rules.Use(g, "water_jug");
            g.Ledger ??= new List<JObject>();
            foreach (var row in result.Rows) g.Ledger.Add((JObject)row.DeepClone());
            var s = result.Summary ?? new LedgerRunSummary();
            GameRules.RecordEvent(g, "ledger", new JObject
            {
                ["plan"] = result.Plan != null ? JObject.FromObject(result.Plan) : null, ["place"] = result.Place,
                ["days"] = s.Days, ["trips"] = s.Trips, ["cooked"] = s.Cooked, ["skipped"] = s.Skipped, ["oldBars"] = s.OldBars,
            });
            return result.Rows;
        }

        /// <summary>Totals over every ledger row so far (endings, debrief).</summary>
        public static LedgerTotals LedgerSummary(GameState g)
        {
            var rows = g?.Ledger ?? new List<JObject>();
            int Sum(string k) => rows.Sum(r => (int)NumberOr0(r?[k]));
            return new LedgerTotals
            {
                Days = rows.Count, Trips = Sum("trips"), Cooked = Sum("cooked"), Skipped = Sum("skipped"),
                MinWater = rows.Count > 0 ? rows.Min(r => (int)NumberOr0(r?["water"])) : 0,
            };
        }
    }
}
