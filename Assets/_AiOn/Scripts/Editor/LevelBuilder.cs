using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Interaction;
using AiOn.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds a level scene from its LevelLayout and the original's exported model (Art/Levels/&lt;id&gt;.glb):
    /// the model is what you see; invisible colliders from the original's walk zones, ramps, solids and ceilings are
    /// what you walk on and bump into, so movement matches the original exactly. Also places interaction points,
    /// spawns and hinged doors. Without a model it falls back to a gray-box (visible boxes).
    /// Rebuilding replaces the previous "Level_&lt;id&gt;" root.
    /// </summary>
    public static class LevelBuilder
    {
        const string MatDir = "Assets/_AiOn/Materials/Blockout";
        public static readonly string[] LevelIds = { "apartment", "mart", "store", "office", "town", "shelter" };

        public static string ScenePath(string id) => $"Assets/_AiOn/Scenes/Levels/{char.ToUpperInvariant(id[0])}{id.Substring(1)}.unity";
        public static string LayoutPath(string id) => $"Assets/_AiOn/Data/Levels/{char.ToUpperInvariant(id[0])}{id.Substring(1)}Layout.asset";
        public static string ModelPath(string id) => $"Assets/_AiOn/Art/Levels/{id}.glb";

        [MenuItem("AiOn/Build Level In Open Scene")]
        public static void BuildOpenScene()
        {
            var id = EditorSceneManager.GetActiveScene().name.ToLowerInvariant();
            var layout = AssetDatabase.LoadAssetAtPath<LevelLayout>(LayoutPath(id));
            if (layout == null) { Debug.LogError($"[AiOn] No layout for scene '{id}' ({LayoutPath(id)}): run AiOn/Import Level Layouts"); return; }
            Build(layout);
        }

        /// <summary>Opens each level scene, rebuilds the level and the player in it, and saves.</summary>
        [MenuItem("AiOn/Rebuild All Level Scenes")]
        public static void BuildAll()
        {
            LevelLayoutImporter.ImportAll();
            PeopleBuilder.BuildAll();
            foreach (var id in LevelIds)
            {
                var layout = AssetDatabase.LoadAssetAtPath<LevelLayout>(LayoutPath(id));
                if (layout == null) { Debug.LogWarning($"[AiOn] {id}: no layout, skipped"); continue; }
                var scene = EditorSceneManager.OpenScene(ScenePath(id), OpenSceneMode.Single);
                foreach (var go in scene.GetRootGameObjects())
                    if (go.name == "Ground" || go.name == "Main Camera") Object.DestroyImmediate(go); // template leftovers
                Build(layout);
                PlayerSetup.CreatePlayer();
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("[AiOn] Rebuilt all level scenes");
        }

        public static GameObject Build(LevelLayout layout)
        {
            var rootName = $"Level_{layout.levelId}";
            var old = GameObject.Find(rootName);
            if (old) Object.DestroyImmediate(old);

            var root = new GameObject(rootName);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(layout.levelId));
            bool visible = model == null; // gray-box when there is no model

            GameObject visual = null;
            if (model != null)
            {
                visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); // so door leaves can be re-hinged
                visual.name = "Visual";
                // glTF import mirrors x (right-handed -> left-handed); our frame mirrors z. Half a turn about y maps one to the other.
                visual.transform.localRotation = Quaternion.Euler(0, 180, 0);
                foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.gameObject.isStatic = false;
            }

            var collision = Child(root, "Collision");
            var floors = Child(collision, "Floors");
            var stairs = Child(collision, "Stairs");
            var solids = Child(collision, "Solids");
            var ceilings = Child(collision, "Ceilings");
            var doors = Child(root, "Doors");

            var mFloor = Mat("Floor", new Color(0.55f, 0.52f, 0.48f));
            var mStair = Mat("Stair", new Color(0.62f, 0.62f, 0.6f));
            var mWall = Mat("Wall", new Color(0.86f, 0.85f, 0.82f));
            var mDoor = Mat("Door", new Color(0.45f, 0.33f, 0.24f));
            var mCeiling = Mat("Ceiling", new Color(0.93f, 0.93f, 0.92f));

            int i = 0;
            foreach (var w in layout.walk.Where(w => !w.isRamp))
                Box(floors, $"Floor_{i++}", w.x0, w.x1, w.y - 0.15f, w.y, w.z0, w.z1, mFloor, visible);

            i = 0;
            foreach (var w in layout.walk.Where(w => w.isRamp))
            {
                var flight = Child(stairs, $"Ramp_{i++}");
                Ramp(flight, w);
                if (visible && w.rampAxis == "z") Treads(flight, w, 8, mStair);
            }

            // Collision boxes in their default state (an "off" box is a door in its other position). The boxes of
            // doors from the door table are left out: those doors are built as hinged leaves below.
            i = 0;
            foreach (var s in layout.solids)
            {
                if (s.off || s.IsEmpty || layout.doors.Any(d => LevelLayout.IsDoorSolid(d, s))) continue;
                var go = Box(solids, string.IsNullOrEmpty(s.id) ? $"Solid_{i}" : $"Solid_{s.id}_{i}", s.x0, s.x1, s.y0, s.y1, s.z0, s.z1, mWall, visible);
                i++;
            }

            foreach (var d in layout.doors) BuildDoor(doors, d, mDoor, visual);

            var points = Child(root, "Points");
            foreach (var p in layout.points)
            {
                var go = new GameObject($"Point_{p.id}");
                go.transform.SetParent(points.transform, false);
                go.transform.localPosition = LevelLayout.ToUnity(p.x, p.y, p.z);
                var ip = go.AddComponent<InteractionPoint>();
                ip.id = p.id; ip.label = p.label; ip.kind = p.kind; ip.radius = p.r; ip.reach = p.reach; ip.host = p.host; ip.interactable = p.enabled;
            }

            i = 0;
            foreach (var c in layout.ceilings)
                Box(ceilings, $"Ceiling_{i++}", c.x0, c.x1, c.y, c.y + 0.1f, c.z0, c.z1, mCeiling, visible);

            BuildEnvironment(root, layout);

            // Named characters at their opening spots (hidden ones wait for the story to show them).
            var people = Child(root, "People");
            foreach (var n in layout.npcs)
            {
                // 'crowd' is a family of looks: the same name always gets the same one.
                int variant = 1 + (int)((uint)Animator.StringToHash(layout.levelId + n.name) % 6);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PeopleBuilder.PrefabPath(n.preset == "crowd" ? "crowd" + variant : n.preset))
                    ?? AssetDatabase.LoadAssetAtPath<GameObject>(PeopleBuilder.PrefabPath("crowd1"));
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, people.transform);
                go.name = $"Npc_{n.name}";
                go.transform.localPosition = LevelLayout.ToUnity(n.x, n.y, n.z);
                go.transform.localRotation = Quaternion.Euler(0, LevelLayout.ToUnityYaw(n.yaw), 0);
                go.SetActive(n.visible);
            }

            if (visual != null) BuildStateVariants(root, visual, layout.levelId);

            // Shop shelves: the original's instanced goods, rebuilt as combined meshes per section.
            if (GoodsBuilder.HasGoods(layout.levelId)) GoodsBuilder.Build(root, layout.levelId);

            // Lit groups and state-driven colours/pictures of the model's materials (after the doors took their leaves).
            if (visual != null) LevelMaterialsBuilder.Build(root, layout.levelId);

            var sp = root.AddComponent<LevelSpawnPoints>();
            sp.points = layout.spawns.Select(s => new LevelSpawnPoints.Point
            {
                name = s.name, position = LevelLayout.ToUnity(s.x, s.y, s.z), yaw = LevelLayout.ToUnityYaw(s.yaw),
            }).ToList();
            sp.killY = layout.walk.Count > 0 ? layout.walk.Min(w => w.isRamp ? Mathf.Min(w.rampY0, w.rampY1) : w.y) - 15f : -20f;

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[AiOn] Built {rootName}: model={(model ? "yes" : "no (gray-box)")}, {floors.transform.childCount} floors, {stairs.transform.childCount} ramps, {solids.transform.childCount} solids, {doors.transform.childCount} doors, {points.transform.childCount} points, {ceilings.transform.childCount} ceilings, {sp.points.Count} spawns");
            return root;
        }

        /// <summary>
        /// The level's lighting: the key (sun) light, a grid of warm lamps under every ceiling (interiors) and the
        /// EnvironmentController that applies the level's preset (sky, fog, ambient, post-processing) at run time.
        /// </summary>
        static void BuildEnvironment(GameObject root, LevelLayout layout)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && !l.transform.IsChildOf(root.transform)) Object.DestroyImmediate(l.gameObject);
            var oldVolume = GameObject.Find("Global Volume");
            if (oldVolume) Object.DestroyImmediate(oldVolume); // the template's profile would fight the preset

            var envGo = Child(root, "Environment");
            var sunGo = Child(envGo, "KeyLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50, 195, 0);

            var lamps = Child(envGo, "IndoorLamps");
            var list = new List<Light>();
            bool underground = layout.env == "underground";
            foreach (var c in layout.ceilings)
            {
                float dx = c.x1 - c.x0, dz = c.z1 - c.z0;
                if (dx < 1.2f || dz < 1.2f) continue;
                float spacing = underground ? 9f : 4.5f;
                int nx = Mathf.Max(1, Mathf.RoundToInt(dx / spacing)), nz = Mathf.Max(1, Mathf.RoundToInt(dz / spacing));
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        var go = new GameObject($"Lamp_{list.Count}");
                        go.transform.SetParent(lamps.transform, false);
                        go.transform.localPosition = LevelLayout.ToUnity(c.x0 + dx * (ix + 0.5f) / nx, c.y - 0.7f, c.z0 + dz * (iz + 0.5f) / nz);
                        var l = go.AddComponent<Light>();
                        l.type = LightType.Point;
                        l.color = underground ? new Color(0.92f, 0.96f, 1f) : new Color(1f, 0.93f, 0.84f);
                        l.range = underground ? 11f : 7.5f;
                        l.intensity = underground ? 7f : 3f;
                        l.shadows = LightShadows.None;
                        list.Add(l);
                    }
            }

            const string skyPath = "Assets/_AiOn/Materials/Sky_Procedural.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }

            var env = envGo.AddComponent<EnvironmentController>();
            env.presetsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_AiOn/Data/Source/env-presets.json");
            env.preset = string.IsNullOrEmpty(layout.env) ? "interior" : layout.env;
            env.keyLight = sun;
            env.skyMaterial = sky;
            env.indoorLamps = list;
        }

        /// <summary>Objects of the model that show or hide with the level's states (Data/Source/&lt;id&gt;-states.json).</summary>
        static void BuildStateVariants(GameObject root, GameObject visual, string levelId)
        {
            var path = $"Assets/_AiOn/Data/Source/{levelId}-states.json";
            if (!System.IO.File.Exists(path)) return;
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            var byName = new Dictionary<string, GameObject>();
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) byName[t.name] = t.gameObject;
            var comp = root.AddComponent<LevelStateVariants>();
            int missing = 0;
            foreach (var p in ((Newtonsoft.Json.Linq.JObject)json["variants"]).Properties())
            {
                if (!byName.TryGetValue(p.Name, out var go)) { missing++; continue; }
                var v = new LevelStateVariants.Variant { target = go, defaultVisible = (bool)p.Value["def"] };
                foreach (var r in p.Value["rules"])
                    v.rules.Add(new LevelStateVariants.Rule { key = (string)r["key"], value = r["value"].ToString(Newtonsoft.Json.Formatting.None), visible = (bool)r["visible"] });
                comp.variants.Add(v);
                go.SetActive(v.defaultVisible);
            }
            Debug.Log($"[AiOn] {levelId}: {comp.variants.Count} state-variant objects{(missing > 0 ? $", {missing} not found in the model" : "")}");

            // Interaction points that states enable, disable or relabel.
            if (json["points"] is Newtonsoft.Json.Linq.JObject pts && pts.HasValues)
            {
                var ps = root.AddComponent<LevelPointStates>();
                foreach (var p in pts.Properties())
                {
                    var pr = new LevelPointStates.PointRules
                    {
                        id = p.Name, defaultEnabled = (bool?)p.Value["def"]?["enabled"] ?? true, defaultLabel = (string)p.Value["def"]?["label"] ?? "",
                    };
                    foreach (var r in p.Value["rules"])
                        pr.rules.Add(new LevelPointStates.Rule
                        {
                            key = (string)r["key"], value = r["value"].ToString(Newtonsoft.Json.Formatting.None),
                            enabled = r["enabled"] == null ? -1 : (bool)r["enabled"] ? 1 : 0,
                            hasLabel = r["label"] != null, label = (string)r["label"] ?? "",
                        });
                    ps.points.Add(pr);
                }
                Debug.Log($"[AiOn] {levelId}: {ps.points.Count} interaction points change with states");
            }
        }

        static void BuildDoor(GameObject parent, LevelLayout.DoorDef d, Material mat, GameObject visual)
        {
            var (lw, sgn, hinge) = Door.Geometry(d);
            var go = new GameObject($"Door_{d.id}");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = hinge;
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(go.transform, false);

            const float t = 0.05f;
            float h = d.h - 0.01f;
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(pivot, false);
            var dir = Door.ClosedLeafDir(d, sgn);
            leaf.transform.localPosition = dir * (lw / 2) + Vector3.up * (h / 2);
            leaf.transform.localScale = d.axis == "z" ? new Vector3(lw, h, t) : new Vector3(t, h, lw);
            leaf.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var body = leaf.AddComponent<Rigidbody>(); // kinematic: a moving collider the controller can push against
            body.isKinematic = true;

            var door = go.AddComponent<Door>();
            door.id = d.id;
            door.def = d;
            door.pivot = pivot;
            door.leafCollider = leaf.GetComponent<BoxCollider>();
            door.k = door.target = d.open;
            door.Apply(d.open, still: true);

            // The model's own leaf (a node pivoted at the same hinge) takes the place of the stand-in box.
            if (visual != null)
            {
                Transform best = null;
                float bestDist = 0.06f;
                foreach (var tr in visual.GetComponentsInChildren<Transform>())
                {
                    if (tr == visual.transform || tr.GetComponentInChildren<Renderer>() == null) continue;
                    float dist = Vector3.Distance(tr.position, hinge);
                    if (dist < bestDist) { bestDist = dist; best = tr; }
                }
                if (best != null)
                {
                    best.SetParent(pivot, true);
                    best.name = "LeafVisual";
                    Object.DestroyImmediate(leaf.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(leaf.GetComponent<MeshFilter>());
                }
            }
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static GameObject Box(GameObject parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Material mat, bool visible)
        {
            var (center, size) = LevelLayout.BoxToUnity(x0, x1, y0, y1, z0, z1);
            GameObject go;
            if (visible)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            else
            {
                go = new GameObject();
                go.AddComponent<BoxCollider>();
            }
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.isStatic = true;
            return go;
        }

        /// <summary>An invisible sloped collider through the ramp's low and high edges (stairs are climbed on these).</summary>
        static void Ramp(GameObject parent, LevelLayout.WalkZone w)
        {
            Vector3 a, b;
            float width;
            if (w.rampAxis == "x")
            {
                float cz = (w.z0 + w.z1) / 2;
                a = LevelLayout.ToUnity(w.x0, w.rampY0, cz);
                b = LevelLayout.ToUnity(w.x1, w.rampY1, cz);
                width = w.z1 - w.z0;
            }
            else
            {
                float cx = (w.x0 + w.x1) / 2;
                a = LevelLayout.ToUnity(cx, w.rampY0, w.z0);
                b = LevelLayout.ToUnity(cx, w.rampY1, w.z1);
                width = w.x1 - w.x0;
            }
            var along = b - a;
            var rot = Quaternion.LookRotation(along.normalized, Vector3.up);
            const float thick = 0.1f;
            var go = new GameObject("RampCollider");
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation((a + b) / 2 - rot * Vector3.up * (thick / 2), rot);
            go.AddComponent<BoxCollider>().size = new Vector3(width, thick, along.magnitude);
            go.isStatic = true;
        }

        /// <summary>Gray-box treads of a z flight: the ramp runs through the tread-top centres.</summary>
        static void Treads(GameObject parent, LevelLayout.WalkZone w, int steps, Material mat)
        {
            bool lowAtZ0 = w.rampY0 <= w.rampY1;
            float yLow = Mathf.Min(w.rampY0, w.rampY1), rise = Mathf.Abs(w.rampY1 - w.rampY0) / steps;
            float tread = (w.z1 - w.z0) / steps, zLow = lowAtZ0 ? w.z0 : w.z1, dir = lowAtZ0 ? 1f : -1f;
            for (int i = 0; i < steps; i++)
            {
                float d0 = tread / 2 + i * tread, d1 = d0 + tread;
                float za = zLow + dir * d0, zb = zLow + dir * d1;
                float top = yLow + (i + 1) * rise;
                var go = Box(parent, $"Tread_{i}", w.x0, w.x1, top - rise, top, Mathf.Min(za, zb), Mathf.Max(za, zb), mat, true);
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
        }

        static Material Mat(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_AiOn/Materials")) AssetDatabase.CreateFolder("Assets/_AiOn", "Materials");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_AiOn/Materials", "Blockout");
            var path = $"{MatDir}/Blockout_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
