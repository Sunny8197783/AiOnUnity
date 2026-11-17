using System;
using System.Collections.Generic;
using System.IO;
using AiOn.Data;
using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds ItemDefinition assets and the ItemDatabase from Data/Source/items.json
    /// (exported from the original safe-route game/items.js). Re-running updates assets in place.
    /// </summary>
    public static class ItemCatalogImporter
    {
        const string SourcePath = "Assets/_AiOn/Data/Source/items.json";
        const string ItemsFolder = "Assets/_AiOn/Data/Items";
        const string DatabasePath = "Assets/_AiOn/Data/ItemDatabase.asset";

        [Serializable] class JItem
        {
            public string id, name, unit, category, needsCheck, risk;
            public int price;
            public double weightKg, volumeL;
            public int[] cells;
            public bool pocket, notSold, atmOnly, fixture;
            public int charge = -1;
            public string[] needs, facts;
        }
        [Serializable] class JCount { public string id; public int count; }
        [Serializable] class JPrice { public string id; public int price; }
        [Serializable] class JLimit { public double weightKg, volumeL; }
        [Serializable] class JGrid { public int cols, rows; public double cellL; }
        [Serializable] class JSection { public string id, label; public string[] items; }
        [Serializable] class JRoot
        {
            public JItem[] catalog;
            public JCount[] startHome;
            public JLimit bagLimit;
            public JGrid bagGrid;
            public double pocketL;
            public JSection[] martSections;
            public JPrice[] storeItems;
        }

        [MenuItem("AiOn/Import Item Catalog")]
        public static void Import()
        {
            var json = File.ReadAllText(SourcePath);
            var root = JsonUtility.FromJson<JRoot>(json);
            if (root?.catalog == null || root.catalog.Length == 0)
                throw new InvalidDataException($"No items found in {SourcePath}");

            var byId = new Dictionary<string, ItemDefinition>();
            foreach (var j in root.catalog)
            {
                var path = $"{ItemsFolder}/{j.id}.asset";
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                }
                def.id = j.id;
                def.displayName = j.name;
                def.unit = j.unit;
                def.price = j.price;
                def.weightKg = j.weightKg;
                def.volumeL = j.volumeL;
                def.category = ParseEnum<ItemCategory>(j.category, j.id);
                def.cells = j.cells is { Length: 2 } ? new Vector2Int(j.cells[0], j.cells[1]) : Vector2Int.zero;
                def.pocket = j.pocket;
                def.notSold = j.notSold;
                def.atmOnly = j.atmOnly;
                def.fixture = j.fixture;
                def.needsCheck = string.IsNullOrEmpty(j.needsCheck) ? ItemCheck.None : ParseEnum<ItemCheck>(j.needsCheck, j.id);
                def.risk = string.IsNullOrEmpty(j.risk) ? ItemRisk.None : ParseEnum<ItemRisk>(j.risk, j.id);
                def.charge = j.charge;
                def.needs = j.needs ?? Array.Empty<string>();
                def.facts = j.facts ?? Array.Empty<string>();
                EditorUtility.SetDirty(def);
                byId[j.id] = def;
            }

            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<ItemDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            db.items = new List<ItemDefinition>(byId.Values);

            db.startHome = new List<ItemStack>();
            foreach (var s in root.startHome ?? Array.Empty<JCount>())
                db.startHome.Add(new ItemStack { item = Require(byId, s.id), count = s.count });

            if (root.bagLimit != null) { db.bagLimitKg = root.bagLimit.weightKg; db.bagLimitL = root.bagLimit.volumeL; }
            if (root.bagGrid != null) { db.bagGrid = new Vector2Int(root.bagGrid.cols, root.bagGrid.rows); db.cellL = root.bagGrid.cellL; }
            db.pocketL = root.pocketL;

            db.martSections = new List<ShopSection>();
            foreach (var s in root.martSections ?? Array.Empty<JSection>())
            {
                var sec = new ShopSection { id = s.id, label = s.label };
                foreach (var id in s.items ?? Array.Empty<string>()) sec.items.Add(Require(byId, id));
                db.martSections.Add(sec);
            }

            db.storeItems = new List<PricedItem>();
            foreach (var p in root.storeItems ?? Array.Empty<JPrice>())
                db.storeItems.Add(new PricedItem { item = Require(byId, p.id), price = p.price });

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Imported {byId.Count} items, {db.martSections.Count} mart sections, {db.storeItems.Count} store items into {DatabasePath}");
        }

        static ItemDefinition Require(Dictionary<string, ItemDefinition> byId, string id) =>
            byId.TryGetValue(id, out var d) ? d : throw new InvalidDataException($"Unknown item id '{id}' in {SourcePath}");

        static T ParseEnum<T>(string value, string itemId) where T : struct =>
            Enum.TryParse<T>(value, true, out var e) ? e : throw new InvalidDataException($"Item '{itemId}': unknown {typeof(T).Name} '{value}'");
    }
}
