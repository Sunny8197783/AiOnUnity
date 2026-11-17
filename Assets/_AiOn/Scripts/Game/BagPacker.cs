using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Data;

namespace AiOn.Game
{
    /// <summary>n units of an item in the bag, <see cref="Pocket"/> of them wanted in the front pocket.</summary>
    public readonly struct BagLine
    {
        public readonly string Id;
        public readonly int N;
        public readonly int Pocket;

        public BagLine(string id, int n = 1, int pocket = 0) { Id = id; N = n; Pocket = pocket; }
    }

    public readonly struct ItemCount : IEquatable<ItemCount>
    {
        public readonly string Id;
        public readonly int N;
        public ItemCount(string id, int n) { Id = id; N = n; }
        public bool Equals(ItemCount o) => Id == o.Id && N == o.N;
        public override bool Equals(object o) => o is ItemCount c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(Id, N);
        public override string ToString() => $"{Id} x{N}";
    }

    /// <summary>One packed unit: cells from the top-left, x across, y down.</summary>
    public readonly struct Placement : IEquatable<Placement>
    {
        public readonly string Id;
        public readonly int X, Y, W, H;
        public readonly bool Rotated;

        public Placement(string id, int x, int y, int w, int h, bool rotated)
        { Id = id; X = x; Y = y; W = w; H = h; Rotated = rotated; }

        public bool Equals(Placement o) => Id == o.Id && X == o.X && Y == o.Y && W == o.W && H == o.H && Rotated == o.Rotated;
        public override bool Equals(object o) => o is Placement p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Id, X, Y, W, H, Rotated);
        public override string ToString() => $"{Id} @({X},{Y}) {W}x{H}{(Rotated ? " rotated" : "")}";
    }

    public sealed class PackResult
    {
        public List<Placement> Placed = new();
        public List<ItemCount> Pocket = new();
        /// <summary>Units that found no room (fixtures always land here).</summary>
        public List<ItemCount> Overflow = new();
        public int Used;
        public int Free;
        public double PocketL;
        public bool Fits => Overflow.Count == 0;
    }

    /// <summary>
    /// Where each packed thing sits in the 25 L backpack grid, plus the 0.5 L front pocket (original game/pack.js).
    /// Deterministic first-fit decreasing: biggest pieces first (area, then longest side, then id), each at the
    /// first free spot in reading order, upright before rotated. Same list, same layout.
    /// </summary>
    public static class BagPacker
    {
        public static PackResult Pack(IEnumerable<BagLine> list, ItemDatabase db, int? cols = null, int? rows = null)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            int c = cols ?? db.bagGrid.x, r = rows ?? db.bagGrid.y;
            if (c < 1 || r < 1) throw new ArgumentOutOfRangeException(nameof(cols), $"grid must be at least 1x1, got {c}x{r}");

            var grid = new List<(string id, int w, int h)>();
            var pocketIds = new List<string>();
            var overflow = new OrderedCounter();
            double pocketL = 0;

            foreach (var line in list)
            {
                var item = db.Get(line.Id) ?? throw new ArgumentException($"unknown item: {line.Id}");
                if (line.N < 0 || line.Pocket < 0 || line.Pocket > line.N)
                    throw new ArgumentOutOfRangeException(nameof(list), $"bad counts for {line.Id}: n {line.N}, pocket {line.Pocket}");
                for (int i = 0; i < line.N; i++)
                {
                    if (i < line.Pocket && item.pocket && Num.Round2(pocketL + item.volumeL) <= db.pocketL)
                    {
                        pocketL = Num.Round2(pocketL + item.volumeL);
                        pocketIds.Add(line.Id);
                        continue;
                    }
                    if (!item.HasCells) { overflow.Add(line.Id); continue; } // fixtures never go in a bag
                    grid.Add((line.Id, item.cells.x, item.cells.y));
                }
            }

            // OrderBy is stable, like the original Array.sort.
            var order = grid
                .OrderByDescending(u => u.w * u.h)
                .ThenByDescending(u => Math.Max(u.w, u.h))
                .ThenBy(u => u.id, StringComparer.Ordinal)
                .ToList();

            var taken = new bool[r, c];
            bool Free(int x, int y, int w, int h)
            {
                if (x + w > c || y + h > r) return false;
                for (int j = y; j < y + h; j++)
                    for (int i = x; i < x + w; i++)
                        if (taken[j, i]) return false;
                return true;
            }
            (int x, int y)? Spot(int w, int h)
            {
                for (int y = 0; y < r; y++)
                    for (int x = 0; x < c; x++)
                        if (Free(x, y, w, h)) return (x, y);
                return null;
            }

            var result = new PackResult { PocketL = pocketL };
            foreach (var u in order)
            {
                int w = u.w, h = u.h;
                bool rotated = false;
                var at = Spot(w, h);
                if (at == null && u.w != u.h && (at = Spot(u.h, u.w)) != null) { w = u.h; h = u.w; rotated = true; }
                if (at == null) { overflow.Add(u.id); continue; }
                var (ax, ay) = at.Value;
                for (int j = ay; j < ay + h; j++)
                    for (int i = ax; i < ax + w; i++)
                        taken[j, i] = true;
                result.Placed.Add(new Placement(u.id, ax, ay, w, h, rotated));
                result.Used += w * h;
            }

            var pocketCount = new OrderedCounter();
            foreach (var id in pocketIds) pocketCount.Add(id);
            result.Pocket = pocketCount.ToList();
            result.Overflow = overflow.ToList();
            result.Free = c * r - result.Used;
            return result;
        }

        /// <summary>The bag (and front pocket) as the packer reads it from the game state.</summary>
        public static List<BagLine> BagList(GameState g, ItemDatabase db) =>
            g.Items.Where(kv => db.Get(kv.Key) != null && kv.Value.Bag > 0)
                   .Select(kv => new BagLine(kv.Key, kv.Value.Bag, kv.Value.Pocket ?? 0))
                   .ToList();

        /// <summary>Would one more unit of <paramref name="id"/> still find room? (The bag UI's take rule.)</summary>
        public static bool CanAdd(IReadOnlyList<BagLine> list, string id, ItemDatabase db, int? cols = null, int? rows = null)
        {
            var next = list.Any(l => l.Id == id)
                ? list.Select(l => l.Id == id ? new BagLine(l.Id, l.N + 1, l.Pocket) : l).ToList()
                : list.Append(new BagLine(id)).ToList();
            return Pack(next, db, cols, rows).Fits;
        }

        /// <summary>Counts per id, in first-seen order (like a JS object's keys).</summary>
        sealed class OrderedCounter
        {
            readonly List<string> _order = new();
            readonly Dictionary<string, int> _n = new();

            public void Add(string id)
            {
                if (_n.TryGetValue(id, out var n)) _n[id] = n + 1;
                else { _order.Add(id); _n[id] = 1; }
            }

            public List<ItemCount> ToList() => _order.Select(id => new ItemCount(id, _n[id])).ToList();
        }
    }

    /// <summary>Number helpers that round exactly like the original's Math.round.</summary>
    public static class Num
    {
        /// <summary>JS Math.round: halves go up (towards +infinity).</summary>
        public static double JsRound(double x) => Math.Floor(x + 0.5);
        public static double Round2(double x) => JsRound(x * 100) / 100;
        public static double Round1(double x) => JsRound(x * 10) / 10;
        /// <summary>Floor division (C# '/' truncates towards zero; the clock runs into negative days).</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }
    }
}
