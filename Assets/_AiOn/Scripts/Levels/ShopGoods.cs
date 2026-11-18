using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// The shelf stock of a shop (original levels/commerce-goods.js): per section a "full" set of goods and an "out" set
    /// (a few leftovers and red 품절 tags). SetSoldOut swaps the listed sections to their out set, like the level state
    /// 'soldOut' of the original mart and store. Sections not listed (and 'misc', which never sells out) show full.
    /// </summary>
    public class ShopGoods : MonoBehaviour
    {
        [System.Serializable]
        public class Section
        {
            public string name;
            public GameObject full;
            public GameObject @out;
        }

        public List<Section> sections = new();

        readonly List<string> _soldOut = new();
        public IReadOnlyList<string> SoldOut => _soldOut;

        void Awake() => SetSoldOut(_soldOut);

        public static ShopGoods Current => FindFirstObjectByType<ShopGoods>();

        /// <summary>Sections a bare store still shows in full (what 이수아's note leaves: batteries and water; levels/store.js:302-305).</summary>
        public static readonly string[] SparseKeep = { "batteries", "water", "misc" };

        /// <summary>True for the goods a bare shelf hides: about 7 in 10, the same ones every time (by index).</summary>
        public static bool SparseHides(int index) => (index * 7 + 3) % 10 < 7;

        bool _sparse;

        /// <summary>
        /// Power off with nobody minding the shop: about 70% of the goods on the shelves are gone, except in the kept
        /// sections. Works on the full sets' direct children; off restores them.
        /// </summary>
        public void SetSparse(bool on, IReadOnlyCollection<string> keep = null)
        {
            if (on == _sparse) return;
            _sparse = on;
            foreach (var s in sections)
            {
                if (!s.full) continue;
                bool kept = keep != null && System.Linq.Enumerable.Contains(keep, s.name);
                var t = s.full.transform;
                for (int i = 0; i < t.childCount; i++)
                    t.GetChild(i).gameObject.SetActive(!(on && !kept && SparseHides(i)));
            }
        }

        public void SetSoldOut(IEnumerable<string> ids)
        {
            var list = new List<string>(ids ?? new string[0]);
            _soldOut.Clear();
            foreach (var s in sections)
            {
                bool @out = s.name != "misc" && list.Contains(s.name);
                if (@out) _soldOut.Add(s.name);
                if (s.full) s.full.SetActive(!@out);
                if (s.@out) s.@out.SetActive(@out);
            }
            foreach (var id in list)
                if (!sections.Exists(s => s.name == id)) Debug.LogWarning($"[ShopGoods] unknown soldOut section '{id}'");
        }
    }
}
