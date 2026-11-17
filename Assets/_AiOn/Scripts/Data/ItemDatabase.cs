using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Data
{
    [Serializable]
    public struct ItemStack
    {
        public ItemDefinition item;
        [Min(0)] public int count;
    }

    [Serializable]
    public struct PricedItem
    {
        public ItemDefinition item;
        [Min(0)] public int price;
    }

    [Serializable]
    public class ShopSection
    {
        public string id;
        public string label;
        public List<ItemDefinition> items = new();
    }

    /// <summary>
    /// The whole item catalogue plus the shop and bag rules from the original game/items.js.
    /// </summary>
    [CreateAssetMenu(menuName = "AiOn/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabase : ScriptableObject
    {
        public List<ItemDefinition> items = new();

        [Header("Home")]
        [Tooltip("What the player owns at home before preparing (START_HOME).")]
        public List<ItemStack> startHome = new();

        [Header("Backpack")]
        [Tooltip("Above this weight walking slows down.")]
        public double bagLimitKg = 8;
        public double bagLimitL = 25;
        public Vector2Int bagGrid = new(10, 5);
        public double cellL = 0.5;
        [Tooltip("Front pocket capacity in litres.")]
        public double pocketL = 0.5;

        [Header("Shops")]
        public List<ShopSection> martSections = new();
        [Tooltip("Convenience store: fewer items, a little dearer.")]
        public List<PricedItem> storeItems = new();

        Dictionary<string, ItemDefinition> _byId;

        public ItemDefinition Get(string id)
        {
            if (_byId == null || _byId.Count != items.Count)
            {
                _byId = new Dictionary<string, ItemDefinition>(items.Count);
                foreach (var it in items)
                    if (it != null && !string.IsNullOrEmpty(it.id)) _byId[it.id] = it;
            }
            return id != null && _byId.TryGetValue(id, out var def) ? def : null;
        }

        public string NameOf(string id) => Get(id)?.displayName ?? id;

        void OnValidate() => _byId = null;
    }
}
