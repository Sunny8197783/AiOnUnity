using System.Collections.Generic;
using System.Linq;
using AiOn.Data;

namespace AiOn.Game
{
    /// <summary>
    /// The bag screen's working copy (original ui/bag.js): how many of each item are at home and in the bag while
    /// the player packs. A unit goes into the bag only while the 10x5 grid still has room for it (BagPacker.CanAdd);
    /// nothing touches the game state until the screen closes and the result is applied.
    /// </summary>
    public sealed class BagPackingModel
    {
        public readonly List<BagItemView> Items;
        readonly ItemDatabase _db;
        readonly Dictionary<string, (int home, int bag)> _st = new();

        public BagPackingModel(IEnumerable<BagItemView> items, ItemDatabase db)
        {
            Items = items.ToList();
            _db = db;
            foreach (var i in Items) _st[i.Id] = (System.Math.Max(0, i.Home), System.Math.Max(0, i.Bag));
        }

        public int HomeOf(string id) => _st.TryGetValue(id, out var s) ? s.home : 0;
        public int BagOf(string id) => _st.TryGetValue(id, out var s) ? s.bag : 0;

        public IEnumerable<BagItemView> AtHome => Items.Where(i => HomeOf(i.Id) > 0);
        public IEnumerable<BagItemView> InBag => Items.Where(i => BagOf(i.Id) > 0);

        public (double weightKg, double volumeL) Load() =>
            (Items.Sum(i => i.WeightKg * BagOf(i.Id)), Items.Sum(i => i.VolumeL * BagOf(i.Id)));

        public List<BagLine> Packed() => Items.Where(i => _db.Get(i.Id) != null && BagOf(i.Id) > 0).Select(i => new BagLine(i.Id, BagOf(i.Id))).ToList();

        /// <summary>
        /// Moves up to n units of id out of 'home' (into the bag) or out of 'bag' (back home). Returns how many moved:
        /// 0 into the bag means it is full for that item (taking something out makes room).
        /// </summary>
        public int Move(string id, bool fromHome, int n = 1)
        {
            if (!_st.ContainsKey(id)) return 0;
            int k = 0;
            while (k < n)
            {
                var (home, bag) = _st[id];
                if (fromHome)
                {
                    if (home <= 0 || (_db.Get(id) != null && !BagPacker.CanAdd(Packed(), id, _db))) break;
                    _st[id] = (home - 1, bag + 1);
                }
                else
                {
                    if (bag <= 0) break;
                    _st[id] = (home + 1, bag - 1);
                }
                k++;
            }
            return k;
        }

        /// <summary>Everything back home ('가방 비우기').</summary>
        public void Empty()
        {
            foreach (var id in _st.Keys.ToList()) { var (h, b) = _st[id]; _st[id] = (h + b, 0); }
        }

        /// <summary>The screen's result: the wanted bag count per item.</summary>
        public Dictionary<string, int> Result() => Items.ToDictionary(i => i.Id, i => BagOf(i.Id));

        /// <summary>Where the packed things sit (tidy, biggest first), for the grid preview.</summary>
        public PackResult Layout() => BagPacker.Pack(Packed(), _db);
    }
}
