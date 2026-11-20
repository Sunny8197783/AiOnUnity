using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Data;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One unit lying in the bag grid: cells from the top-left, x across, y down (the bag stands upright).</summary>
    public sealed class BagPiece
    {
        public int Uid;
        public string Id;
        public int X, Y, W, H;
        /// <summary>The unit's upright size (ItemDefinition.cells); W/H swap when it is turned.</summary>
        public int BaseW, BaseH;

        public bool Rotated => BaseW != BaseH && W == BaseH && H == BaseW;
        public bool Covers(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
        public override string ToString() => $"{Id}#{Uid} @({X},{Y}) {W}x{H}{(Rotated ? " r" : "")}";
    }

    /// <summary>
    /// The backpack as the player arranges it by hand (the competition upgrade's 직접 놓기): a cols x rows grid where
    /// each unit sits where it was put and keeps its turn. <see cref="Tidy"/> is the same biggest-first rule as
    /// <see cref="BagPacker"/> (which stays the original's packer, untouched), applied to what is in the bag.
    /// </summary>
    public sealed class BagGrid
    {
        public readonly int Cols, Rows;
        readonly List<BagPiece> _p = new();
        int _uid;

        public BagGrid(int cols, int rows)
        {
            if (cols < 1 || rows < 1) throw new ArgumentOutOfRangeException(nameof(cols), $"grid must be at least 1x1, got {cols}x{rows}");
            Cols = cols; Rows = rows;
        }

        public IReadOnlyList<BagPiece> Pieces => _p;
        public int Used => _p.Sum(p => p.W * p.H);
        public int Free => Cols * Rows - Used;
        public int Count(string id) => _p.Count(p => p.Id == id);
        public BagPiece At(int x, int y) => _p.FirstOrDefault(p => p.Covers(x, y));
        /// <summary>The unit of id put in last (the one '꺼내기' takes out).</summary>
        public BagPiece Last(string id) => _p.LastOrDefault(p => p.Id == id);

        public static (int w, int h) Turned(int w, int h, bool rotated) => rotated ? (h, w) : (w, h);

        /// <summary>Inside the grid and over no other unit (<paramref name="ignore"/>: the unit being moved).</summary>
        public bool Fits(int x, int y, int w, int h, BagPiece ignore = null)
        {
            if (w < 1 || h < 1 || x < 0 || y < 0 || x + w > Cols || y + h > Rows) return false;
            foreach (var p in _p)
                if (p != ignore && x < p.X + p.W && p.X < x + w && y < p.Y + p.H && p.Y < y + h) return false;
            return true;
        }

        /// <summary>The first free spot in reading order (top row first, left to right), or null.</summary>
        public (int x, int y)? FirstFree(int w, int h, BagPiece ignore = null)
        {
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    if (Fits(x, y, w, h, ignore)) return (x, y);
            return null;
        }

        /// <summary>Puts a unit at the first free spot, upright before turned; null when no spot is free as things lie.</summary>
        public BagPiece Add(string id, int w, int h)
        {
            var at = FirstFree(w, h);
            bool rot = false;
            if (at == null && w != h && (at = FirstFree(h, w)) != null) rot = true;
            return at == null ? null : Put(id, w, h, at.Value.x, at.Value.y, rot);
        }

        /// <summary>Puts a unit exactly there (top-left cell), turned or not; null when it does not fit there.</summary>
        public BagPiece AddAt(string id, int w, int h, int x, int y, bool rotated)
        {
            var (pw, ph) = Turned(w, h, rotated);
            return Fits(x, y, pw, ph) ? Put(id, w, h, x, y, rotated) : null;
        }

        BagPiece Put(string id, int w, int h, int x, int y, bool rotated)
        {
            var (pw, ph) = Turned(w, h, rotated);
            var p = new BagPiece { Uid = ++_uid, Id = id, BaseW = w, BaseH = h, X = x, Y = y, W = pw, H = ph };
            _p.Add(p);
            return p;
        }

        /// <summary>Moves (and maybe turns) a unit already in the bag; false and unchanged when it does not fit there.</summary>
        public bool MoveTo(BagPiece p, int x, int y, bool rotated)
        {
            if (p == null || !_p.Contains(p)) return false;
            var (w, h) = Turned(p.BaseW, p.BaseH, rotated);
            if (!Fits(x, y, w, h, p)) return false;
            p.X = x; p.Y = y; p.W = w; p.H = h;
            return true;
        }

        public bool Remove(BagPiece p) => p != null && _p.Remove(p);
        public void Clear() => _p.Clear();

        /// <summary>
        /// '정리': everything out and back in, biggest first (area, longest side, id, then the order it went in), each at
        /// the first free spot, upright before turned — BagPacker's rule. False (and nothing moved) if that order would
        /// leave something out, which can happen when a hand packing was tighter than the rule.
        /// </summary>
        public bool Tidy()
        {
            var order = _p.OrderByDescending(p => p.BaseW * p.BaseH).ThenByDescending(p => Math.Max(p.BaseW, p.BaseH))
                          .ThenBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.Uid).ToList();
            var keep = _p.Select(p => (p, p.X, p.Y, p.W, p.H)).ToList();
            foreach (var p in _p) p.X = -100; // off the grid while they go back in one by one
            var placed = new List<BagPiece>();
            foreach (var p in order)
            {
                var at = FreeAmong(placed, p.BaseW, p.BaseH);
                int w = p.BaseW, h = p.BaseH;
                if (at == null && w != h && (at = FreeAmong(placed, h, w)) != null) (w, h) = (h, w);
                if (at == null)
                {
                    foreach (var (q, x, y, qw, qh) in keep) { q.X = x; q.Y = y; q.W = qw; q.H = qh; }
                    return false;
                }
                p.X = at.Value.x; p.Y = at.Value.y; p.W = w; p.H = h;
                placed.Add(p);
            }
            // reading order, so the next '꺼내기' and the encoded layout follow what the eye sees
            _p.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            return true;
        }

        (int x, int y)? FreeAmong(List<BagPiece> placed, int w, int h)
        {
            for (int y = 0; y + h <= Rows; y++)
                for (int x = 0; x + w <= Cols; x++)
                    if (!placed.Any(p => x < p.X + p.W && p.X < x + w && y < p.Y + p.H && p.Y < y + h)) return (x, y);
            return null;
        }

        public BagGrid Clone()
        {
            var c = new BagGrid(Cols, Rows) { _uid = _uid };
            foreach (var p in _p) c._p.Add(new BagPiece { Uid = p.Uid, Id = p.Id, X = p.X, Y = p.Y, W = p.W, H = p.H, BaseW = p.BaseW, BaseH = p.BaseH });
            return c;
        }

        /// <summary>Two units share a side (not just a corner).</summary>
        public static bool Touching(BagPiece a, BagPiece b)
        {
            bool xOverlap = a.X < b.X + b.W && b.X < a.X + a.W, yOverlap = a.Y < b.Y + b.H && b.Y < a.Y + a.H;
            return (xOverlap && (a.Y + a.H == b.Y || b.Y + b.H == a.Y)) || (yOverlap && (a.X + a.W == b.X || b.X + b.W == a.X));
        }

        /// <summary><paramref name="top"/> lies right on top of <paramref name="below"/> in at least one column.</summary>
        public static bool RightAbove(BagPiece top, BagPiece below) =>
            top.Y + top.H == below.Y && top.X < below.X + below.W && below.X < top.X + top.W;

        /// <summary>"id:x,y" or "id:x,y,r" per unit, ';' between (kept in G.Know so the next opening shows the same bag).</summary>
        public string Encode() => string.Join(";", _p.Select(p => $"{p.Id}:{p.X},{p.Y}{(p.Rotated ? ",r" : "")}"));

        /// <summary>The units of an encoded layout, or null when the text is not one.</summary>
        public static List<(string id, int x, int y, bool rotated)> Decode(string text)
        {
            var list = new List<(string, int, int, bool)>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(';'))
            {
                int colon = part.LastIndexOf(':');
                if (colon <= 0) return null;
                var nums = part.Substring(colon + 1).Split(',');
                if (nums.Length < 2 || nums.Length > 3) return null;
                if (!int.TryParse(nums[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(nums[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return null;
                if (nums.Length == 3 && nums[2] != "r") return null;
                list.Add((part.Substring(0, colon), x, y, nums.Length == 3));
            }
            return list;
        }
    }

    /// <summary>
    /// A personal thing for one experience mode (체험 모드별 맞춤 칸). Not in the item catalogue or the save's items: the
    /// bag screen lists it while that mode is on and remembers it in G.Know ("bagAids"). Sizes and weights are game
    /// numbers; the reason to pack it is a sourced fact.
    /// </summary>
    public sealed class BagAid
    {
        public string Id, Name, Profile, Fact;
        public int W, H;
        public double Kg, L;
    }

    public static class BagAids
    {
        public const string Prefix = "aid_";

        public static readonly BagAid[] All =
        {
            // 인천광역시 장애인 재난안전가이드: 보조기기를 쓰면 여분 배터리를 갖춰 둔다.
            new BagAid { Id = "aid_chair_batt", Name = "휠체어 보조 배터리", Profile = "wheelchair", W = 2, H = 1, Kg = 1.2, L = 0.8, Fact = "aid_battery" },
            new BagAid { Id = "aid_hearing_batt", Name = "보청기 여분 배터리", Profile = "deaf", W = 1, H = 1, Kg = 0.02, L = 0.02, Fact = "aid_battery" },
            // 같은 안내: 청각·언어장애인은 의사소통 보조기기를 챙겨 둔다.
            new BagAid { Id = "aid_memo", Name = "필담 메모장", Profile = "deaf", W = 1, H = 1, Kg = 0.1, L = 0.2, Fact = "aid_comm" },
            // 강동구 생존배낭 목록: 안경 등 개인별 필수용품.
            new BagAid { Id = "aid_glasses", Name = "여분 안경", Profile = "lowvision", W = 1, H = 1, Kg = 0.1, L = 0.3, Fact = "bag_personal" },
        };

        public static List<BagAid> For(string profile) => All.Where(a => a.Profile == profile).ToList();
        public static BagAid Get(string id) => All.FirstOrDefault(a => a.Id == id);
        public static bool IsAid(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The screen row for an aid (always ready; at home unless it was packed before).</summary>
        public static BagItemView View(BagAid a, bool packed) => new()
        {
            Id = a.Id, Name = a.Name, WeightKg = a.Kg, VolumeL = a.L, Home = packed ? 0 : 1, Bag = packed ? 1 : 0, Ready = true, Tip = "",
            Tags = new List<string> { "aid" },
        };
    }

    public sealed class BagScoreLine
    {
        /// <summary>priority | nextTo | wet | weight | aid</summary>
        public string Kind;
        public string Id, Label, Detail;
        /// <summary>The fact behind the line, or null for a game rule (shown as '게임 힌트', never as a safety fact).</summary>
        public string Fact;
        public bool Ok;
        public int Points, Max;
        public bool Hint => Fact == null;
    }

    public sealed class BagScore
    {
        public readonly List<BagScoreLine> Lines = new();
        public int Total, Max;
        public double WeightKg;
        public bool Over;
        public int Wet;
        public BagScoreLine Line(string id) => Lines.FirstOrDefault(l => l.Id == id);

        public JObject ToJson() => new()
        {
            ["total"] = Total, ["max"] = Max, ["weightKg"] = Math.Round(WeightKg, 2), ["over"] = Over, ["wet"] = Wet,
            ["ok"] = new JArray(Lines.Where(l => l.Ok).Select(l => l.Id)),
            ["missed"] = new JArray(Lines.Where(l => !l.Ok).Select(l => l.Id)),
        };
    }

    /// <summary>
    /// How a hand-packed bag scores, so the score teaches: the official list first (서울특별시 생존배낭 목록: 생수, 간편식,
    /// 손전등, 상비약, 라디오와 건전지, 화장지와 물티슈 — and 신분증 from 국민안전24 docs_take), then how things lie
    /// (라디오 옆 건전지, 물병 바로 아래 서류 — game hints: no official source says where in a bag), the weight limit
    /// (a game number with a 5장 consequence) and the experience mode's own things (sourced).
    /// </summary>
    public static class BagRules
    {
        public sealed class Priority
        {
            public string Id, Label, Fact;
            public string[] Items;
            /// <summary>A second set that must be there too (spare batteries with the radio).</summary>
            public string[] Also;
        }

        public static readonly Priority[] Priorities =
        {
            new Priority { Id = "water", Label = "식수", Items = new[] { "water_500", "water_2l" }, Fact = "bag_list" },
            new Priority { Id = "food", Label = "식량", Items = new[] { "bars", "canned", "rice_instant" }, Fact = "bag_list" },
            new Priority { Id = "meds", Label = "상비약", Items = new[] { "meds", "first_aid" }, Fact = "bag_list" },
            new Priority { Id = "radio", Label = "라디오와 건전지", Items = new[] { "radio" }, Also = new[] { "batteries_aa", "batteries_old" }, Fact = "bag_list" },
            new Priority { Id = "light", Label = "손전등", Items = new[] { "flashlight" }, Fact = "bag_list" },
            // The list's 마스크 is a 화생방 방독면 마스크 (no such item here), so the tag is the list's 화장지와 물티슈.
            new Priority { Id = "tissue", Label = "화장지·물티슈", Items = new[] { "tissue", "wipes" }, Fact = "bag_list" },
            new Priority { Id = "id", Label = "신분증", Items = new[] { "id_card", "doc_copies" }, Fact = "docs_take" },
        };

        public static readonly HashSet<string> Water = new() { "water_500", "water_2l" };
        public static readonly HashSet<string> Papers = new() { "id_card", "doc_copies", "contact_card", "cash", "aid_memo" };
        public static readonly HashSet<string> Batteries = new() { "batteries_aa", "batteries_old" };

        public const int PriorityPts = 2, NextToPts = 1, DryPts = 1, WeightPts = 2, AidPts = 1;

        /// <summary>The official-list tag of an item (the screen's '우선' badge), or null.</summary>
        public static Priority PriorityOf(string id) => Priorities.FirstOrDefault(p => p.Items.Contains(id) || (p.Also?.Contains(id) ?? false));

        /// <param name="ready">Usable now (a torch with batteries in); unready gear does not count, like the goals.</param>
        public static BagScore Score(BagGrid grid, Func<string, bool> ready, Func<string, double> kgOf, double limitKg, IReadOnlyList<BagAid> aids)
        {
            var s = new BagScore();
            bool In(string id) => grid.Count(id) > 0 && (ready == null || ready(id));
            void Add(BagScoreLine l) { s.Lines.Add(l); s.Total += l.Points; s.Max += l.Max; }

            foreach (var p in Priorities)
            {
                bool ok = p.Items.Any(In) && (p.Also == null || p.Also.Any(In));
                Add(new BagScoreLine { Kind = "priority", Id = p.Id, Label = p.Label, Fact = p.Fact, Ok = ok, Points = ok ? PriorityPts : 0, Max = PriorityPts });
            }

            // an unready radio (작동 확인 안 함) earns nothing for where it lies, like its tag
            bool radioUnready = grid.Count("radio") > 0 && ready != null && !ready("radio");
            var radios = radioUnready ? new List<BagPiece>() : grid.Pieces.Where(p => p.Id == "radio").ToList();
            var batts = grid.Pieces.Where(p => Batteries.Contains(p.Id)).ToList();
            bool next = radios.Any(r => batts.Any(b => BagGrid.Touching(r, b)));
            Add(new BagScoreLine
            {
                Kind = "nextTo", Id = "nextTo", Label = "라디오 바로 옆에 건전지",
                Detail = radioUnready ? "라디오 작동 확인부터" : radios.Count == 0 || batts.Count == 0 ? "둘 다 넣었다면 붙여 두기" : next ? "바로 갈아 끼울 수 있다" : "붙여 두면 바로 갈아 끼울 수 있다",
                Ok = next, Points = next ? NextToPts : 0, Max = NextToPts,
            });

            var waters = grid.Pieces.Where(p => Water.Contains(p.Id)).ToList();
            var papers = grid.Pieces.Where(p => Papers.Contains(p.Id)).ToList();
            s.Wet = papers.Count(d => waters.Any(w => BagGrid.RightAbove(w, d)));
            bool dry = papers.Count > 0 && s.Wet == 0;
            Add(new BagScoreLine
            {
                Kind = "wet", Id = "dry", Label = s.Wet > 0 ? $"물병 바로 아래 서류 {s.Wet}개" : "서류는 물병 아래를 피하기",
                Detail = s.Wet > 0 ? "물이 새면 젖는다" : papers.Count == 0 ? "서류를 넣었다면 물병 위나 옆에" : "젖을 걱정이 적다",
                Ok = dry, Points = s.Wet > 0 ? -Math.Min(3, s.Wet) * DryPts : dry ? DryPts : 0, Max = DryPts,
            });

            s.WeightKg = grid.Pieces.Sum(p => kgOf(p.Id));
            s.Over = s.WeightKg > limitKg + 1e-9;
            bool light = grid.Pieces.Count > 0 && !s.Over;
            Add(new BagScoreLine
            {
                Kind = "weight", Id = "weight", Label = s.Over ? $"무게 {KgShort(limitKg)} 넘음" : $"무게 {KgShort(limitKg)} 이하",
                Detail = s.Over ? "걸음이 느려지고, 남의 짐을 들어 주기 어렵다" : "들고 걷기 편한 무게",
                Ok = light, Points = light ? WeightPts : 0, Max = WeightPts,
            });

            foreach (var a in aids ?? Array.Empty<BagAid>())
            {
                bool ok = grid.Count(a.Id) > 0;
                Add(new BagScoreLine { Kind = "aid", Id = a.Id, Label = a.Name, Fact = a.Fact, Ok = ok, Points = ok ? AidPts : 0, Max = AidPts });
            }
            s.Total = Math.Max(0, s.Total);
            return s;
        }

        /// <summary>"8kg", "7.5kg": a limit as people say it.</summary>
        public static string KgShort(double kg) => kg.ToString("0.#", CultureInfo.InvariantCulture) + "kg";

        // ---------- mart: 사재기 ----------

        /// <summary>Game number: more than this many of one thing in one trip is 사재기 (the original's per-cart 8).</summary>
        public const int HoardEach = 8;

        /// <summary>True when one item's units bought this trip (all carts together) pass <see cref="HoardEach"/>.</summary>
        public static bool Hoarding(IReadOnlyDictionary<string, int> boughtThisTrip) =>
            boughtThisTrip != null && boughtThisTrip.Any(kv => kv.Key != "cash" && kv.Value > HoardEach);
    }

    /// <summary>
    /// The bag screen's working copy for 직접 놓기 (a separate model from <see cref="BagPackingModel"/>, whose
    /// first-fit rule the original tests pin): home counts, the grid as the player left it, the experience mode's
    /// own things. Nothing touches the game until the screen closes.
    /// </summary>
    public sealed class ManualBagModel
    {
        public readonly List<BagItemView> Items;
        public readonly IReadOnlyList<BagAid> Aids;
        public readonly BagGrid Grid;
        readonly Dictionary<string, int> _home = new();
        readonly Dictionary<string, (int w, int h)> _size = new();

        /// <summary>Why the last PutIn failed: "none" (nothing left at home), "room" (no free spot as things lie; '정리'
        /// or moving things makes one), "full" (no room even tidied), "size" (no cells: never packed).</summary>
        public string LastFail { get; private set; }

        public ManualBagModel(IEnumerable<BagItemView> items, ItemDatabase db, IEnumerable<BagAid> aids = null,
            IEnumerable<string> aidsPacked = null, string layout = null)
            : this(items, id => db.Get(id) is { } d && d.HasCells ? (d.cells.x, d.cells.y) : ((int, int)?)null,
                   db.bagGrid.x, db.bagGrid.y, aids, aidsPacked, layout) { }

        public ManualBagModel(IEnumerable<BagItemView> items, Func<string, (int w, int h)?> cellsOf, int cols, int rows,
            IEnumerable<BagAid> aids = null, IEnumerable<string> aidsPacked = null, string layout = null)
        {
            Aids = (aids ?? Enumerable.Empty<BagAid>()).ToList();
            var packedAids = new HashSet<string>(aidsPacked ?? Enumerable.Empty<string>());
            Items = (items ?? Enumerable.Empty<BagItemView>()).Where(i => !BagAids.IsAid(i.Id)).ToList();
            // the mode's own things first, where the player sees them without scrolling
            Items.InsertRange(0, Aids.Select(a => BagAids.View(a, packedAids.Contains(a.Id))));
            Grid = new BagGrid(cols, rows);
            foreach (var i in Items)
            {
                var a = BagAids.Get(i.Id);
                var size = a != null ? (a.W, a.H) : cellsOf(i.Id);
                if (size.HasValue) _size[i.Id] = size.Value;
                _home[i.Id] = Math.Max(0, i.Home);
            }
            // The bag as it was: the remembered layout when it still matches, else tidied biggest-first.
            var want = Items.Where(i => _size.ContainsKey(i.Id)).ToDictionary(i => i.Id, i => Math.Max(0, i.Bag));
            if (!FromLayout(layout, want))
            {
                Grid.Clear();
                var units = want.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
                    .OrderByDescending(id => _size[id].w * _size[id].h).ThenByDescending(id => Math.Max(_size[id].w, _size[id].h))
                    .ThenBy(id => id, StringComparer.Ordinal).ToList();
                foreach (var id in units)
                    if (Grid.Add(id, _size[id].w, _size[id].h) == null) _home[id] = HomeOf(id) + 1; // did not fit: it stays home
            }
            // what has no cells (bad data) stays where it was and is never moved
            foreach (var i in Items.Where(i => !_size.ContainsKey(i.Id))) _home[i.Id] = Math.Max(0, i.Home);
        }

        bool FromLayout(string layout, Dictionary<string, int> want)
        {
            var units = BagGrid.Decode(layout);
            if (units == null || units.Count == 0) return false;
            var counts = units.GroupBy(u => u.id).ToDictionary(g => g.Key, g => g.Count());
            if (counts.Count != want.Count(kv => kv.Value > 0) || counts.Any(kv => !want.TryGetValue(kv.Key, out var n) || n != kv.Value)) return false;
            foreach (var u in units)
                if (Grid.AddAt(u.id, _size[u.id].w, _size[u.id].h, u.x, u.y, u.rotated) == null) return false;
            return true;
        }

        public (int w, int h)? SizeOf(string id) => _size.TryGetValue(id, out var s) ? s : ((int, int)?)null;
        public int HomeOf(string id) => _home.TryGetValue(id, out var n) ? n : 0;
        public int BagOf(string id) => _size.ContainsKey(id) ? Grid.Count(id) : Items.FirstOrDefault(i => i.Id == id)?.Bag ?? 0;
        public IEnumerable<BagItemView> AtHome => Items.Where(i => HomeOf(i.Id) > 0);
        public IEnumerable<BagItemView> InBag => Items.Where(i => BagOf(i.Id) > 0);
        public BagItemView View(string id) => Items.FirstOrDefault(i => i.Id == id);
        public bool Ready(string id) => View(id)?.Ready != false;

        public double KgOf(string id) => View(id)?.WeightKg ?? 0;
        public (double weightKg, double volumeL) Load() =>
            (Items.Sum(i => i.WeightKg * BagOf(i.Id)), Items.Sum(i => i.VolumeL * BagOf(i.Id)));

        /// <summary>One unit from home into the first free spot (as things lie). Null and <see cref="LastFail"/> if not.</summary>
        public BagPiece PutIn(string id)
        {
            LastFail = null;
            if (!_size.TryGetValue(id, out var s)) { LastFail = "size"; return null; }
            if (HomeOf(id) <= 0) { LastFail = "none"; return null; }
            var p = Grid.Add(id, s.w, s.h);
            if (p == null) { LastFail = CouldFitTidied(id) ? "room" : "full"; return null; }
            _home[id]--;
            return p;
        }

        /// <summary>One unit from home to that cell (its top-left), turned or not. Null if it does not fit there.</summary>
        public BagPiece PutAt(string id, int x, int y, bool rotated)
        {
            LastFail = null;
            if (!_size.TryGetValue(id, out var s)) { LastFail = "size"; return null; }
            if (HomeOf(id) <= 0) { LastFail = "none"; return null; }
            var p = Grid.AddAt(id, s.w, s.h, x, y, rotated);
            if (p == null) { LastFail = "spot"; return null; }
            _home[id]--;
            return p;
        }

        public bool Move(BagPiece p, int x, int y, bool rotated) => Grid.MoveTo(p, x, y, rotated);

        public bool TakeOut(BagPiece p)
        {
            if (p == null || !Grid.Remove(p)) return false;
            _home[p.Id] = HomeOf(p.Id) + 1;
            return true;
        }

        /// <summary>Takes out up to n units of id (the last ones put in first). Returns how many came out.</summary>
        public int TakeOut(string id, int n = 1)
        {
            int k = 0;
            while (k < n && TakeOut(Grid.Last(id))) k++;
            return k;
        }

        /// <summary>Puts up to n units in (first free spot each). Returns how many went in.</summary>
        public int PutIn(string id, int n)
        {
            int k = 0;
            while (k < n && PutIn(id) != null) k++;
            return k;
        }

        /// <summary>Would one more unit of id find room if the bag were tidied first?</summary>
        public bool CouldFitTidied(string id)
        {
            if (!_size.TryGetValue(id, out var s)) return false;
            // everything in the bag plus the newcomer, biggest first, into an empty grid
            var trial = new BagGrid(Grid.Cols, Grid.Rows);
            var all = Grid.Pieces.Select(p => (id: p.Id, w: p.BaseW, h: p.BaseH)).Append((id, s.w, s.h))
                .OrderByDescending(u => u.w * u.h).ThenByDescending(u => Math.Max(u.w, u.h)).ThenBy(u => u.id, StringComparer.Ordinal);
            foreach (var u in all) if (trial.Add(u.id, u.w, u.h) == null) return false;
            return true;
        }

        /// <summary>'정리'. False when biggest-first would not hold everything (the hand packing stays).</summary>
        public bool Tidy() => Grid.Tidy();

        /// <summary>Everything back home ('가방 비우기').</summary>
        public void Empty()
        {
            foreach (var p in Grid.Pieces.ToList()) TakeOut(p);
        }

        /// <summary>The screen's result: the wanted bag count per catalogue item (aids are not catalogue items).</summary>
        public Dictionary<string, int> Result() => Items.Where(i => !BagAids.IsAid(i.Id)).ToDictionary(i => i.Id, i => BagOf(i.Id));

        public List<string> AidsPacked() => Aids.Where(a => Grid.Count(a.Id) > 0).Select(a => a.Id).ToList();

        public BagScore Score(double limitKg) => BagRules.Score(Grid, Ready, KgOf, limitKg, Aids);
    }
    /// <summary>
    /// The story side of the hand-packed bag: what the bag screen is told (experience mode, the last layout, the aids
    /// packed before), what it hands back (kept in G.Know, scored as a 'bag_score' event), the re-pack countdown after
    /// the siren and how long that took in game minutes. A result without the new keys (the tests' fake screen, an old
    /// answer) changes nothing, so the original's runs stay as they were.
    /// </summary>
    public static class BagHand
    {
        public const string LayoutKey = "bagLayout", AidsKey = "bagAids";

        /// <summary>Game number: real seconds for the re-pack right after the siren (an experience mode gets half again).</summary>
        public const double AlertTimer = 90;
        /// <summary>Game number: one '정리' takes this off that countdown.</summary>
        public const double TidySeconds = 10;
        /// <summary>The original's fixed cost of packing after the siren, in game minutes (also the most it costs now).</summary>
        public const int RushMinutesMax = 4;

        public static double TimerFor(string profile) => string.IsNullOrEmpty(profile) || profile == "none" ? AlertTimer : AlertTimer * 1.5;

        /// <summary>Adds profile, reduceMotion and the remembered layout and aids to a bag screen's options.</summary>
        public static JObject AddOptions(JObject o, GameState g, string profile, bool reduceMotion)
        {
            o["profile"] = profile ?? "";
            o["reduceMotion"] = reduceMotion;
            if (g?.Know != null && g.Know.TryGetValue(LayoutKey, out var l) && l?.Type == JTokenType.String) o["layout"] = l.DeepClone();
            if (g?.Know != null && g.Know.TryGetValue(AidsKey, out var a) && a is JArray) o["aids"] = a.DeepClone();
            return o;
        }

        /// <summary>Keeps the layout and the aids and records the score. False (nothing done) for a result without a layout.</summary>
        public static bool Remember(GameState g, JObject r, string context)
        {
            if (g == null || r == null || r["layout"]?.Type != JTokenType.String) return false;
            g.SetKnow(LayoutKey, (string)r["layout"]);
            g.SetKnow(AidsKey, r["aids"] is JArray aids ? aids.DeepClone() : new JArray());
            if (r["score"] is JObject s)
                GameRules.RecordEvent(g, "bag_score", new JObject
                {
                    ["context"] = context, ["total"] = s["total"]?.DeepClone(), ["max"] = s["max"]?.DeepClone(), ["wet"] = s["wet"]?.DeepClone(),
                    ["over"] = s["over"]?.DeepClone(), ["missed"] = s["missed"]?.DeepClone() ?? new JArray(),
                    ["tidied"] = r["tidied"]?.DeepClone() ?? 0, ["timedOut"] = r["timedOut"]?.Type == JTokenType.Boolean && (bool)r["timedOut"],
                });
            return true;
        }

        /// <summary>
        /// Game minutes the re-pack after the siren took: a quarter of the countdown is a minute, at least 1, at most the
        /// original's 4. A result without 'elapsed' (the original screen) is the original's 4.
        /// </summary>
        public static int RushMinutes(JObject r)
        {
            var e = r?["elapsed"];
            if (e == null || !(e.Type is JTokenType.Integer or JTokenType.Float)) return RushMinutesMax;
            double sec = (double)e;
            return Math.Max(1, Math.Min(RushMinutesMax, (int)Math.Ceiling(sec / (AlertTimer / RushMinutesMax))));
        }

        /// <summary>
        /// Game minutes the '정리' presses add after the siren re-pack. The original screen (no 'elapsed') charged a minute
        /// each; the timed screen charges a tidy once, as 10 s off the countdown, which is already in 'elapsed' and so in
        /// <see cref="RushMinutes"/>: nothing more here.
        /// </summary>
        public static int AlertTidyMinutes(JObject r)
        {
            if (r == null || r["elapsed"] != null) return 0;
            return r["tidied"] is JValue tv && (tv.Type == JTokenType.Integer || tv.Type == JTokenType.Float) ? Math.Max(0, (int)(double)tv) : 0;
        }

        public static bool TimedOut(JObject r) => r?["timedOut"]?.Type == JTokenType.Boolean && (bool)r["timedOut"];

        /// <summary>The aids the screen should start with packed (G.Know "bagAids").</summary>
        public static List<string> AidsFrom(JToken t) => t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).ToList() : new List<string>();

        /// <summary>Weight and volume of the aids packed (G.Know "bagAids"): part of the bag the game carries (GameRules.BagLoad).</summary>
        public static (double kg, double l) AidLoad(GameState g)
        {
            double kg = 0, l = 0;
            if (g?.Know == null || !g.Know.TryGetValue(AidsKey, out var t)) return (0, 0);
            foreach (var id in AidsFrom(t))
                if (BagAids.Get(id) is { } a) { kg += a.Kg; l += a.L; }
            return (kg, l);
        }
    }
}

