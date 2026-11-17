using System.IO;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Turns Data/Source/*-level.json (walk, solids, ceilings, spawns read from the original level builders) into
    /// LevelLayout assets in Data/Levels. Re-running updates them in place.
    /// </summary>
    public static class LevelLayoutImporter
    {
        const string SourceDir = "Assets/_AiOn/Data/Source";
        const string OutDir = "Assets/_AiOn/Data/Levels";

        [MenuItem("AiOn/Import Level Layouts")]
        public static void ImportAll()
        {
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/_AiOn/Data", "Levels");
            int n = 0;
            foreach (var file in Directory.GetFiles(SourceDir, "*-level.json"))
            {
                var layout = Import(file.Replace('\\', '/'));
                Debug.Log($"[AiOn] Level layout '{layout.levelId}': {layout.walk.Count} walk zones, {layout.solids.Count} solids, {layout.ceilings.Count} ceilings, {layout.spawns.Count} spawns, {layout.points.Count} points, {layout.doors.Count} doors");
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Imported {n} level layout(s) into {OutDir}");
        }

        public static LevelLayout Import(string jsonPath)
        {
            var o = JObject.Parse(File.ReadAllText(jsonPath));
            var id = (string)o["id"];
            var path = $"{OutDir}/{char.ToUpperInvariant(id[0])}{id.Substring(1)}Layout.asset";
            var layout = AssetDatabase.LoadAssetAtPath<LevelLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<LevelLayout>();
                AssetDatabase.CreateAsset(layout, path);
            }
            layout.levelId = id;
            layout.env = (string)o["env"] ?? "interior";

            layout.walk.Clear();
            foreach (var w in o["walk"])
            {
                var z = new LevelLayout.WalkZone { x0 = F(w["x0"]), x1 = F(w["x1"]), z0 = F(w["z0"]), z1 = F(w["z1"]) };
                if (w["ramp"] is JObject r)
                {
                    z.rampAxis = (string)r["axis"] ?? "z";
                    if (z.rampAxis != "z" && z.rampAxis != "x") throw new InvalidDataException($"{jsonPath}: unknown ramp axis {r["axis"]}");
                    z.isRamp = true; z.rampY0 = F(r["y0"]); z.rampY1 = F(r["y1"]);
                }
                else z.y = F(w["y"]);
                layout.walk.Add(z);
            }

            layout.solids.Clear();
            foreach (var s in o["solids"])
                layout.solids.Add(new LevelLayout.SolidBox
                {
                    x0 = F(s["x0"]), x1 = F(s["x1"]), y0 = F(s["y0"]), y1 = F(s["y1"]), z0 = F(s["z0"]), z1 = F(s["z1"]),
                    id = (string)s["id"] ?? "", off = (bool?)s["off"] ?? false,
                    noClimb = (bool?)s["noClimb"] ?? false, noVault = (bool?)s["noVault"] ?? false,
                });

            layout.ceilings.Clear();
            foreach (var c in o["ceilings"])
                layout.ceilings.Add(new LevelLayout.Ceiling { x0 = F(c["x0"]), x1 = F(c["x1"]), z0 = F(c["z0"]), z1 = F(c["z1"]), y = F(c["y"]) });

            layout.spawns.Clear();
            foreach (var p in ((JObject)o["spawns"]).Properties())
                layout.spawns.Add(new LevelLayout.Spawn { name = p.Name, x = F(p.Value["x"]), y = F(p.Value["y"]), z = F(p.Value["z"]), yaw = F(p.Value["yaw"]) });

            layout.points.Clear();
            foreach (var p in o["points"] ?? new JArray())
                layout.points.Add(new LevelLayout.Point
                {
                    id = (string)p["id"], label = (string)p["label"], kind = (string)p["kind"],
                    x = F(p["x"]), y = F(p["y"]), z = F(p["z"]), r = F(p["r"]), reach = F(p["reach"]),
                    host = p["host"] is JArray hosts ? string.Join(",", hosts) : (string)p["host"] ?? "",
                    enabled = (bool?)p["enabled"] ?? true,
                });

            layout.doors.Clear();
            foreach (var d in o["doors"] ?? new JArray())
                layout.doors.Add(new LevelLayout.DoorDef
                {
                    id = (string)d["id"], axis = (string)d["axis"] ?? "z", c = F(d["c"]), a = F(d["a"]), b = F(d["b"]), y = F(d["y"]),
                    h = d["h"] != null ? F(d["h"]) : 2.1f, hinge = (string)d["hinge"] ?? "a", dir = (int?)d["dir"] ?? 1, open = F(d["open"]),
                    solid = (bool?)d["solid"] ?? true, locked = (bool?)d["lock"] ?? false, auto = F(d["auto"]),
                });

            layout.npcs.Clear();
            if (o["npcs"] is JObject npcs)
                foreach (var n in npcs.Properties())
                    layout.npcs.Add(new LevelLayout.Npc
                    {
                        name = n.Name, preset = (string)n.Value["preset"] ?? "crowd", x = F(n.Value["x"]), y = F(n.Value["y"]), z = F(n.Value["z"]),
                        yaw = F(n.Value["yaw"]), visible = (bool?)n.Value["visible"] ?? true,
                    });

            EditorUtility.SetDirty(layout);
            return layout;
        }

        static float F(JToken t) => t == null ? 0f : (float)t;
    }
}
