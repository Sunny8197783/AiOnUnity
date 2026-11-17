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
