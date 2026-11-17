using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Data;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public readonly struct BagLoad
    {
        public readonly double WeightKg, VolumeL;
        public readonly bool OverWeight, OverVolume;

        public BagLoad(double weightKg, double volumeL, bool overWeight, bool overVolume)
        { WeightKg = weightKg; VolumeL = volumeL; OverWeight = overWeight; OverVolume = overVolume; }
    }

    public readonly struct BagCells
    {
        public readonly int Used, Total;
        public readonly bool Fits;
        public BagCells(int used, int total, bool fits) { Used = used; Total = total; Fits = fits; }
    }

    public readonly struct CheckResult
    {
        public readonly bool Ok;
        /// <summary>Player-facing Korean reason when not ok.</summary>
        public readonly string Reason;
        public CheckResult(bool ok, string reason = null) { Ok = ok; Reason = reason; }
    }

    /// <summary>
    /// The rules that change a <see cref="GameState"/> (original game/state.js). Pure logic: no scene, no UI.
    /// Exceptions are for developers (a bug in the caller); player-facing text lives in the UI and story.
    /// </summary>
    public sealed class GameRules
    {
        public static readonly string[] Labels = { "official", "site", "neighbor", "rumor", "self" };
        public static readonly string[] Tools = { "radio", "bag", "route", "extinguisher", "docs", "info" };
        /// <summary>People with a relationship. 'elder' and 'stranger' only appear in 'help' events.</summary>
        public static readonly string[] People = { "manager", "clerk", "seoyeon" };
        /// <summary>Battery packs, best first.</summary>
        public static readonly string[] BatteryPacks = { "batteries_aa", "batteries_old" };

        static readonly string[] InitialKnowFlags =
        {
            "shelterLocated", "entrancesChecked", "routeChecked", "extinguisherLocated", "radioChecked",
            "contactsCard", "docsCopied",
        };

        public ItemDatabase Db { get; }

        public GameRules(ItemDatabase db) => Db = db ? db : throw new ArgumentNullException(nameof(db));

        ItemDefinition Def(string id) => Db.Get(id) ?? throw new ArgumentException($"unknown item: {id}");

        // ---------- creation ----------

        public GameState CreateGame(long? seed = null)
        {
            uint s = seed.HasValue ? ToSeed(seed.Value) : (uint)new Random().Next();
            var g = new GameState { Seed = s };
            foreach (var st in Db.startHome) g.Items[st.item.id] = new ItemEntry(st.count);
            foreach (var k in InitialKnowFlags) g.Know[k] = false;
            g.Know["meetPoint"] = JValue.CreateNull();
            g.Know["waterPoint"] = false;
            g.Know["gateClosedSeen"] = false;
            foreach (var p in People) g.Rel[p] = new Relation();
            foreach (var t in Tools) g.Skill[t] = new List<JToken>();
            g.Cond = GameConditions.For(s);
            return g;
        }

        /// <summary>Like the original Math.trunc(seed) >>> 0.</summary>
        public static uint ToSeed(long seed) => unchecked((uint)seed);

        // ---------- clock and stats ----------

        public static GameEvent RecordEvent(GameState g, string type, object details = null)
        {
            if (string.IsNullOrEmpty(type)) throw new ArgumentException("event type must be a non-empty string");
            var e = new GameEvent
            {
                Seq = (g.Events.Count > 0 ? g.Events[^1].Seq : 0) + 1,
                Type = type,
                Chapter = g.Chapter,
                Day = g.Day,
                Minute = g.Minute,
                Details = details switch
                {
                    null => new JObject(),
                    JObject o => o,
                    _ => JObject.FromObject(details),
                },
            };
            g.Events.Add(e);
            return e;
        }

        public static string Hhmm(int minute) => $"{minute / 60:00}:{minute % 60:00}";

        public static string FormatClock(GameState g)
        {
            string d = g.Day == 0 ? "D-day" : g.Day < 0 ? $"D{g.Day}" : $"D+{g.Day}";
            return $"{d} {Hhmm(g.Minute)}";
        }

        public static double Adjust(GameState g, Stat stat, double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta)) throw new ArgumentException($"delta must be a number, got {delta}");
            double Clamp(double v) => Math.Min(100, Math.Max(0, v + delta));
            switch (stat)
            {
                case Stat.Health: return g.Stats.Health = Clamp(g.Stats.Health);
                case Stat.Thirst: return g.Stats.Thirst = Clamp(g.Stats.Thirst);
                case Stat.Fatigue: return g.Stats.Fatigue = Clamp(g.Stats.Fatigue);
                case Stat.Tension: return g.Stats.Tension = Clamp(g.Stats.Tension);
                default: throw new ArgumentException($"unknown stat: {stat}");
            }
        }

        /// <summary>Stat by its original name ('health', 'thirst', 'fatigue', 'tension').</summary>
        public static double Adjust(GameState g, string stat, double delta) => stat switch
        {
            "health" => Adjust(g, Stat.Health, delta),
            "thirst" => Adjust(g, Stat.Thirst, delta),
            "fatigue" => Adjust(g, Stat.Fatigue, delta),
            "tension" => Adjust(g, Stat.Tension, delta),
            _ => throw new ArgumentException($"unknown stat: {stat}"),
        };

        /// <summary>
        /// Moves the clock. Drift counts boundaries crossed on an absolute clock, so ten 2-minute steps add
        /// the same thirst (+1 per 20 min) and fatigue (+1 per 30 min awake) as one 20-minute step.
        /// </summary>
        public static GameState Advance(GameState g, double minutes, bool awake = true)
        {
            if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0)
                throw new ArgumentOutOfRangeException(nameof(minutes), $"minutes must be >= 0, got {minutes}");
            long t0 = (long)g.Day * 1440 + g.Minute, t1 = t0 + (long)Num.JsRound(minutes);
            long Crossed(long step) => Num.FloorDiv(t1, step) - Num.FloorDiv(t0, step);
            Adjust(g, Stat.Thirst, Crossed(20));
            if (awake) Adjust(g, Stat.Fatigue, Crossed(30));
            g.Day = (int)Num.FloorDiv(t1, 1440);
            g.Minute = (int)(t1 - (long)g.Day * 1440);
            return g;
        }

        /// <summary>Plans an action into the day's slots. Returns the slots left.</summary>
        public static int SpendSlot(GameState g, string actionId, int cost = 1)
        {
            if (cost < 1) throw new ArgumentOutOfRangeException(nameof(cost), $"slot cost must be a positive integer, got {cost}");
            int left = g.Slots.Total - g.Slots.Used;
            if (cost > left) throw new InvalidOperationException($"not enough slots for {actionId}: {left} left, {cost} needed");
            g.Slots.Used += cost;
            g.Slots.Done.Add(actionId);
            RecordEvent(g, "slot", new JObject { ["actionId"] = actionId, ["cost"] = cost });
            return left - cost;
        }

        // ---------- items ----------

        static int Qty(int n) => n >= 1 ? n : throw new ArgumentOutOfRangeException(nameof(n), $"quantity must be a positive integer, got {n}");

        ItemEntry Entry(GameState g, string id)
        {
            Def(id);
            if (!g.Items.TryGetValue(id, out var e)) g.Items[id] = e = new ItemEntry();
            return e;
        }

        /// <summary>Mart prices: everything sold, at catalogue price (sold-out sections are the caller's to drop).</summary>
        public Dictionary<string, int> MartPrices() =>
            Db.items.Where(i => !i.notSold && !i.atmOnly).ToDictionary(i => i.id, i => i.price);

        public Dictionary<string, int> StorePrices() =>
            Db.storeItems.ToDictionary(p => p.item.id, p => p.price);

        /// <summary>
        /// Buys a cart from a shop whose stock is <paramref name="prices"/>. All or nothing. 'cash' at an ATM is
        /// a purchase like any other. Returns the total paid.
        /// </summary>
        public long Buy(GameState g, IEnumerable<KeyValuePair<string, int>> cart, IReadOnlyDictionary<string, int> prices)
        {
            var lines = (cart ?? Enumerable.Empty<KeyValuePair<string, int>>()).Where(kv => kv.Value != 0).ToList();
            long total = 0;
            foreach (var (id, n) in lines)
            {
                Def(id);
                Qty(n);
                if (prices == null || !prices.TryGetValue(id, out var p) || p < 0)
                    throw new InvalidOperationException($"{id} is not in stock here");
                total += (long)p * n;
            }
            if (total > g.Money) throw new InvalidOperationException($"not enough money: {total} > {g.Money}");
            g.Money -= total;
            foreach (var (id, n) in lines) { var e = Entry(g, id); e.Home += n; e.Bought += n; }
            if (lines.Count > 0)
            {
                var c = new JObject();
                foreach (var (id, n) in lines) c[id] = n;
                RecordEvent(g, "buy", new JObject { ["cart"] = c, ["total"] = total });
            }
            return total;
        }

        /// <summary>The front pocket is part of the bag: when the bag count drops below it, the pocket empties first.</summary>
        static void FitPocket(ItemEntry e)
        {
            if (e.Pocket > e.Bag) e.Pocket = e.Bag;
            if (e.Pocket == 0) e.Pocket = null;
        }

        ItemEntry Move(GameState g, string id, int n, bool homeToBag)
        {
            Qty(n);
            var e = Entry(g, id);
            int have = homeToBag ? e.Home : e.Bag;
            if (have < n) throw new InvalidOperationException($"only {have} {id} at {(homeToBag ? "home" : "bag")}, asked to move {n}");
            if (homeToBag) { e.Home -= n; e.Bag += n; }
            else { e.Bag -= n; e.Home += n; }
            FitPocket(e);
            return e;
        }

        public ItemEntry Pack(GameState g, string id, int n = 1)
        {
            if (Db.Get(id) is { fixture: true }) throw new InvalidOperationException($"{id} is a fixture and stays at home");
            return Move(g, id, n, homeToBag: true);
        }

        public ItemEntry Unpack(GameState g, string id, int n = 1) => Move(g, id, n, homeToBag: false);

        public BagLoad BagLoad(GameState g)
        {
            double w = 0, v = 0;
            foreach (var (id, e) in g.Items)
            {
                var d = Def(id);
                w += e.Bag * d.weightKg;
                v += e.Bag * d.volumeL;
            }
            double weightKg = Num.Round2(w), volumeL = Num.Round2(v);
            // The real room limit is the cell grid: litres alone never reach 25 L before the grid is full.
            return new BagLoad(weightKg, volumeL, weightKg > Db.bagLimitKg, volumeL > Db.bagLimitL || !BagCells(g).Fits);
        }

        public BagCells BagCells(GameState g)
        {
            var r = BagPacker.Pack(BagPacker.BagList(g, Db), Db);
            return new BagCells(r.Used, Db.bagGrid.x * Db.bagGrid.y, r.Fits);
        }

        /// <summary>Litres in the front pocket.</summary>
        public double PocketLoad(GameState g)
        {
            double v = 0;
            foreach (var (id, e) in g.Items) v += (e.Pocket ?? 0) * (Db.Get(id)?.volumeL ?? 0);
            return Num.Round2(v);
        }

        /// <summary>Puts n of the packed <paramref name="id"/> in the front pocket (0 takes them out).</summary>
        public ItemEntry SetPocket(GameState g, string id, int n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), $"pocket count must be an integer >= 0, got {n}");
            var e = Entry(g, id);
            var d = Def(id);
            if (n > 0 && !d.pocket) throw new InvalidOperationException($"{id} does not fit the front pocket");
            if (n > e.Bag) throw new InvalidOperationException($"only {e.Bag} {id} in the bag, asked to pocket {n}");
            double after = Num.Round2(PocketLoad(g) + (n - (e.Pocket ?? 0)) * d.volumeL);
            if (after > Db.pocketL) throw new InvalidOperationException($"front pocket full: {after} L > {Db.pocketL} L");
            e.Pocket = n > 0 ? n : null;
            return e;
        }

        public static bool InPocket(GameState g, string id) => g.Items.TryGetValue(id, out var e) && (e.Pocket ?? 0) > 0;

        static bool Have(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e.Owned > 0;

        /// <summary>Owned is not usable: a flashlight without batteries does not count.</summary>
        public bool Usable(GameState g, string id)
        {
            if (!Have(g, id)) return false;
            return Db.Get(id)?.needsCheck is null or ItemCheck.None || g.Items[id].Checked;
        }

        /// <summary>
        /// Turns an owned item into a usable one. 'batteries' uses up one battery pack (a new AA pack unless
        /// <paramref name="usingPack"/> names one) and sets the device's charge from it; 'charge' needs grid
        /// power, which the story gates (before the blackout).
        /// </summary>
        public CheckResult Check(GameState g, string id, string usingPack = null)
        {
            var d = Def(id);
            if (usingPack != null && Array.IndexOf(BatteryPacks, usingPack) < 0) throw new ArgumentException($"not a battery pack: {usingPack}");
            if (!Have(g, id)) return new CheckResult(false, "가지고 있지 않습니다.");
            var e = g.Items[id];
            if (e.Checked) return new CheckResult(true);
            if (d.needsCheck == ItemCheck.Batteries)
            {
                var pack = usingPack ?? BatteryPacks.FirstOrDefault(p => Have(g, p));
                if (pack == null || !Have(g, pack)) return new CheckResult(false, "넣을 건전지가 없습니다.");
                Consume(g, pack, 1, g.Items[pack].Home > 0 ? "home" : "bag");
                e.Charge = Def(pack).charge;
            }
            e.Checked = true;
            RecordEvent(g, "check", d.needsCheck == ItemCheck.Batteries
                ? new JObject { ["id"] = id, ["charge"] = e.Charge }
                : new JObject { ["id"] = id });
            return new CheckResult(true);
        }

        /// <summary>
        /// Runs a battery device down by pct. At 0 it is no longer usable and 'battery_dead' is recorded.
        /// A device checked before charges existed counts as full. Returns the charge left (0 if not running).
        /// </summary>
        public double DrainCharge(GameState g, string id, double pct)
        {
            if (Db.Get(id)?.needsCheck != ItemCheck.Batteries) throw new InvalidOperationException($"{id} does not run on batteries");
            if (double.IsNaN(pct) || double.IsInfinity(pct) || pct < 0) throw new ArgumentOutOfRangeException(nameof(pct), $"pct must be >= 0, got {pct}");
            if (!g.Items.TryGetValue(id, out var e) || !e.Checked) return 0;
            e.Charge = Math.Max(0, Num.Round1((e.Charge ?? 100) - pct));
            if (e.Charge == 0) { e.Checked = false; RecordEvent(g, "battery_dead", new JObject { ["id"] = id }); }
            return e.Charge.Value;
        }

        /// <summary>Uses up n units from 'home' or 'bag'.</summary>
        public ItemEntry Consume(GameState g, string id, int n = 1, string from = "home")
        {
            if (from != "home" && from != "bag") throw new ArgumentException($"consume from must be 'home' or 'bag', got {from}");
            Qty(n);
            var e = Entry(g, id);
            int have = from == "home" ? e.Home : e.Bag;
            if (have < n) throw new InvalidOperationException($"only {have} {id} at {from}, asked to consume {n}");
            if (from == "home") e.Home -= n; else e.Bag -= n;
            e.Used += n;
            FitPocket(e);
            return e;
        }

        /// <summary>
        /// Barter: give for get with <paramref name="from"/>. All or nothing. Given items leave home first, then
        /// the bag, and count as used; received items go home (not 'bought').
        /// </summary>
        public GameEvent Trade(GameState g, IEnumerable<KeyValuePair<string, int>> give, IEnumerable<KeyValuePair<string, int>> get, string from)
        {
            if (string.IsNullOrWhiteSpace(from)) throw new ArgumentException("trade needs who it is with");
            var outs = (give ?? Enumerable.Empty<KeyValuePair<string, int>>()).ToList();
            var ins = (get ?? Enumerable.Empty<KeyValuePair<string, int>>()).ToList();
            if (outs.Count == 0 || ins.Count == 0) throw new ArgumentException("a trade gives and gets at least one item");
            foreach (var (id, n) in outs.Concat(ins)) { Def(id); Qty(n); }
            foreach (var (id, n) in outs)
            {
                int has = g.Items.TryGetValue(id, out var e) ? e.Owned : 0;
                if (has < n) throw new InvalidOperationException($"only {has} {id} to give, asked {n}");
            }
            foreach (var (id, n) in outs)
            {
                var e = g.Items[id];
                int home = Math.Min(e.Home, n);
                e.Home -= home; e.Bag -= n - home; e.Used += n;
                FitPocket(e);
            }
            foreach (var (id, n) in ins) Entry(g, id).Home += n;

            JObject ToObj(List<KeyValuePair<string, int>> l) { var o = new JObject(); foreach (var (id, n) in l) o[id] = n; return o; }
            return RecordEvent(g, "trade", new JObject { ["give"] = ToObj(outs), ["get"] = ToObj(ins), ["from"] = from });
        }

        /// <summary>Durable items (radio, flashlight, water jug...) are used without being used up.</summary>
        public ItemEntry Use(GameState g, string id)
        {
            var e = Entry(g, id);
            if (!Usable(g, id)) throw new InvalidOperationException($"{id} is not usable");
            e.Used += 1;
            return e;
        }

        // ---------- info and movement ----------

        /// <summary>Records a piece of labelled info once (same text and label returns the earlier note).</summary>
        public static InfoNote Note(GameState g, string text, string label, string place = null)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("note text must be a non-empty string");
            if (Array.IndexOf(Labels, label) < 0) throw new ArgumentException($"unknown info label: {label}");
            var t = text.Trim();
            var dup = g.Info.FirstOrDefault(i => i.Text == t && i.Label == label);
            if (dup != null) return dup;
            var n = new InfoNote { Chapter = g.Chapter, Day = g.Day, Minute = g.Minute, Text = t, Label = label, Place = place };
            g.Info.Add(n);
            return n;
        }

        /// <summary>
        /// Walking and running speed multiplier 0.6..1 from fatigue (above 50) and, once the bag is on the back,
        /// its weight against the bag limit (up to -12 % at the limit, steeply more above it). A broken leg x0.7.
        /// </summary>
        public double SpeedMul(GameState g)
        {
            double weightKg = g.Knows("bagOn") ? BagLoad(g).WeightKg : 0, lim = Db.bagLimitKg;
            double tired = Math.Max(0, g.Stats.Fatigue - 50) / 50 * 0.25;
            double load = weightKg <= lim ? 0.12 * weightKg / lim : 0.12 + (weightKg - lim) / lim * 0.4;
            double leg = g.HasInjury("leg") ? 0.7 : 1;
            return Num.Round2(Math.Min(1, Math.Max(0.6, 1 - tired - load)) * leg);
        }
    }
}
