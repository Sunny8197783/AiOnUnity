using UnityEngine;

namespace AiOn.Data
{
    public enum ItemCategory { Water, Food, Info, Power, Light, Health, Hygiene, Warmth, Tool, Fire, Docs, Keepsake, Home }

    /// <summary>Why an item can hurt (fire hazard, spoils without power, past its date).</summary>
    public enum ItemRisk { None, Fire, Spoil, Expired }

    /// <summary>What turns "bought" into "usable": batteries in, or a charged pack.</summary>
    public enum ItemCheck { None, Batteries, Charge }

    /// <summary>
    /// One entry of the original game's item catalogue (safe-route game/items.js CATALOG).
    /// Prices are fictional won; weights and volumes are game approximations, not advice.
    /// </summary>
    [CreateAssetMenu(menuName = "AiOn/Item Definition", fileName = "Item")]
    public class ItemDefinition : ScriptableObject
    {
        [Tooltip("Stable id from the original catalogue, e.g. water_2l. Save files refer to items by this.")]
        public string id;
        public string displayName;
        public string unit;
        [Min(0)] public int price;
        [Min(0)] public double weightKg;
        [Min(0)] public double volumeL;
        public ItemCategory category;

        [Tooltip("Size in the 10x5 bag grid, 0.5 L per cell. (0,0) for fixtures that are never packed.")]
        public Vector2Int cells;

        [Tooltip("Small and flat enough (<= 0.2 L) for the bag's front pocket.")]
        public bool pocket;
        [Tooltip("Found or made at home, never sold in shops.")]
        public bool notSold;
        [Tooltip("Only obtainable from the ATM (cash).")]
        public bool atmOnly;
        [Tooltip("Installed at home (e.g. blackout curtain), never packed.")]
        public bool fixture;

        public ItemCheck needsCheck;
        public ItemRisk risk;

        [Tooltip("Battery packs only: device charge after loading this pack, 0..100. -1 = not a battery.")]
        [Range(-1, 100)] public int charge = -1;

        [Tooltip("Other resources this item requires to be used, e.g. hot_water for ramen.")]
        public string[] needs = System.Array.Empty<string>();
        [Tooltip("Fact ids (original game/facts.js) the item's tip is based on.")]
        public string[] facts = System.Array.Empty<string>();

        public bool Packable => !fixture;
        /// <summary>Takes room in the bag grid (fixtures have no cells).</summary>
        public bool HasCells => cells.x > 0 && cells.y > 0;
    }
}
