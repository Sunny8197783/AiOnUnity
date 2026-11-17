using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds a level's LevelMaterials from its model (Art/Levels/&lt;id&gt;.glb) and states table
    /// (Data/Source/&lt;id&gt;-states.json 'materials'): every model material named '&lt;tag&gt;~m&lt;n&gt;' that is in a lit group,
    /// has state rules, any emission (LevelMaterials writes emissiveFactor linear-correct, which glTFast does not) or a
    /// KHR_materials_emissive_strength (which glTFast does not import) becomes an entry.
    /// Materials are matched by their '~m&lt;n&gt;' index, which the exporter made unique per level.
    /// State pictures were saved straight from the three.js canvas, while GLTFExporter flips canvas textures before
    /// embedding them; the builder therefore assigns vertically flipped copies (StateTextures/Flipped) and drops pictures
    /// that equal the GLB's own default picture (they redraw nothing and would only cover other keys' pictures, e.g. the
    /// town 'gates' probes over the 'notices' boards).
    /// </summary>
    public static class LevelMaterialsBuilder
    {
        public const string TextureDir = "Assets/_AiOn/Art/Levels/StateTextures";
        public const string FlippedDir = TextureDir + "/Flipped";
        /// <summary>Mean abs channel difference (0..255) under which a state picture counts as the default picture.</summary>
        const float SameAsDefault = 0.5f;

        [MenuItem("AiOn/Rebuild Level Materials In Open Scene")]
        public static void BuildOpenScene()
        {
            var id = EditorSceneManager.GetActiveScene().name.ToLowerInvariant();
            var root = GameObject.Find($"Level_{id}");
            if (root == null) { Debug.LogError($"[AiOn] No Level_{id} root in the open scene: run AiOn/Build Level In Open Scene"); return; }
            Build(root, id);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        public static LevelMaterials Build(GameObject root, string levelId)
        {
            foreach (var old in root.GetComponents<LevelMaterials>()) UnityEngine.Object.DestroyImmediate(old);

            // The model's materials by '~m<n>' index (door leaves were moved out of Visual, so the whole root is scanned).
            var byIndex = new Dictionary<int, List<Material>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !LevelMaterials.ParseName(m.name, out _, out _, out int n)) continue;
                    if (!byIndex.TryGetValue(n, out var list)) byIndex[n] = list = new List<Material>();
                    if (!list.Contains(m)) list.Add(m);
                }

            var glb = Glb.Read(LevelBuilder.ModelPath(levelId));
            var strength = glb.EmissiveStrengths();

            // The states table's materials by index.
            var table = new Dictionary<int, (string name, JObject body)>();
            var statesPath = $"Assets/_AiOn/Data/Source/{levelId}-states.json";
            if (File.Exists(statesPath) && JObject.Parse(File.ReadAllText(statesPath))["materials"] is JObject mats)
                foreach (var p in mats.Properties())
                    if (LevelMaterials.ParseName(p.Name, out _, out _, out int n)) table[n] = (p.Name, (JObject)p.Value);

            var comp = root.AddComponent<LevelMaterials>();
            var perGroup = new SortedDictionary<string, int>();
            int withRules = 0, ruleCount = 0, strong = 0, dims = 0, nameMismatch = 0, emissiveOnly = 0, droppedPics = 0;
            var missingTex = new List<string>();
            var notInModel = table.Where(kv => !byIndex.ContainsKey(kv.Key)).Select(kv => kv.Value.name).ToList();

            foreach (var kv in byIndex.OrderBy(k => k.Key))
                foreach (var mat in kv.Value)
                {
                    LevelMaterials.ParseName(mat.name, out var group, out var dim, out _);
                    // A strength of 0 matters too (emissive colour with emissiveIntensity 0): glTFast would light it fully.
                    float s = strength.TryGetValue(kv.Key, out var sv) ? Mathf.Max(0f, sv) : 1f;
                    table.TryGetValue(kv.Key, out var t);
                    bool hasRules = t.body?["rules"] is JArray ra && ra.Count > 0;
                    var e = new LevelMaterials.Entry { source = mat, group = group ?? "", dim = string.IsNullOrEmpty(group) ? -1 : dim, emissiveStrength = s };
                    bool emissive = ReadImported(mat, e);
                    bool plain = string.IsNullOrEmpty(group) && !hasRules && Mathf.Approximately(s, 1f);
                    // Emissive-only materials are kept too, so every emission gets the same (linear-correct) encoding.
                    if (plain && !emissive) continue;
                    if (plain) emissiveOnly++;
                    e.importedEmission = Scale(e.importedEmission, s);
                    if (!Mathf.Approximately(s, 1f)) strong++;

                    if (t.body != null)
                    {
                        if (!SameName(t.name, mat.name)) nameMismatch++;
                        if (t.body["def"] is JObject def)
                        {
                            e.hasDef = true;
                            e.hasDefColor = TryColor(def["c"], out e.defColor);
                            e.hasDefEmission = TryColor(def["e"], out e.defEmission);
                            e.defOpacity = (float?)def["o"] ?? 1f;
                        }
                        if (t.body["rules"] is JArray rules)
                            foreach (var rj in rules.OfType<JObject>())
                            {
                                var r = new LevelMaterials.Rule
                                {
                                    key = (string)rj["key"],
                                    value = LevelMaterials.CanonicalJson(rj["value"]),
                                };
                                r.hasColor = TryColor(rj["c"], out r.color);
                                r.hasEmission = TryColor(rj["e"], out r.emission);
                                if (rj["o"] != null && rj["o"].Type != JTokenType.Null) { r.hasOpacity = true; r.opacity = (float)rj["o"]; }
                                r.map = LoadStateTexture((string)rj["map"], missingTex, glb.DefaultPicture(kv.Key, "map"), ref droppedPics);
                                r.emissiveMap = LoadStateTexture((string)rj["emap"], missingTex, glb.DefaultPicture(kv.Key, "emap"), ref droppedPics);
                                e.rules.Add(r);
                                ruleCount++;
                            }
                        if (e.rules.Count > 0) withRules++;
                    }

                    if (!string.IsNullOrEmpty(group)) { perGroup[group] = perGroup.TryGetValue(group, out var c) ? c + 1 : 1; if (e.dim >= 0) dims++; }
                    comp.entries.Add(e);
                }

            var sb = new StringBuilder($"[AiOn] {levelId}: {comp.entries.Count} runtime materials; lit ");
            sb.Append(perGroup.Count == 0 ? "none" : string.Join(", ", perGroup.Select(p => $"{p.Key} {p.Value}")));
            if (dims > 0) sb.Append($" ({dims} backlit/dim)");
            sb.Append($"; {withRules} with state rules ({ruleCount} rules, keys {string.Join(",", comp.entries.SelectMany(x => x.rules).Select(x => x.key).Distinct())})");
            if (strong > 0) sb.Append($"; {strong} with emissive strength");
            if (emissiveOnly > 0) sb.Append($"; {emissiveOnly} emissive only");
            if (droppedPics > 0) sb.Append($"; {droppedPics} state pictures equal to the default dropped");
            if (notInModel.Count > 0) sb.Append($"; {notInModel.Count} states-table materials not in the model: {string.Join(", ", notInModel.Take(12))}");
            if (nameMismatch > 0) sb.Append($"; {nameMismatch} matched by index but named differently");
            if (missingTex.Count > 0) sb.Append($"; {missingTex.Count} missing textures: {string.Join(", ", missingTex.Distinct().Take(12))}");
            if (notInModel.Count > 0 || missingTex.Count > 0) Debug.LogWarning(sb.ToString()); else Debug.Log(sb.ToString());
            EditorUtility.SetDirty(comp);
            return comp;
        }

        static bool SameName(string a, string b) => b == a || b.StartsWith(a, StringComparison.Ordinal);

        static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static bool TryColor(JToken t, out Color c)
        {
            c = Color.black;
            if (t is not JArray a || a.Count < 3) return false;
            c = new Color((float)a[0], (float)a[1], (float)a[2], 1f);
            return true;
        }

        /// <summary>
        /// The imported material's values in linear glTF terms. glTFast (ShaderGraphMaterialGenerator) writes
        /// baseColorFactor = linear.gamma via SetVector and emissiveFactor = the raw glTF linear value via SetColor (only
        /// when non-black, with the _EMISSIVE keyword); GetColor returns what was set, so the raw emissive factor is the
        /// glTF value. LevelMaterials writes both as linear.gamma. True when the material emits (factor or map).
        /// </summary>
        static bool ReadImported(Material m, LevelMaterials.Entry e)
        {
            if (m.HasProperty("baseColorFactor"))
            {
                var v = m.GetVector("baseColorFactor");
                e.importedColor = new Color(v.x, v.y, v.z, 1f).linear;
                e.importedOpacity = v.w;
            }
            else if (m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor");
                e.importedColor = new Color(c.r, c.g, c.b, 1f).linear;
                e.importedOpacity = c.a;
            }

            bool hasEmissiveMap = (m.HasProperty("emissiveTexture") && m.GetTexture("emissiveTexture")) || (m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap"));
            if (m.HasProperty("emissiveFactor"))
            {
                bool on = m.IsKeywordEnabled("_EMISSIVE") || hasEmissiveMap;
                var c = m.GetColor("emissiveFactor");
                e.importedEmission = on ? new Color(c.r, c.g, c.b, 1f) : Color.black;
            }
            else if (m.HasProperty("_EmissionColor"))
            {
                bool on = m.IsKeywordEnabled("_EMISSION") || hasEmissiveMap;
                var c = m.GetColor("_EmissionColor");
                e.importedEmission = on ? new Color(c.r, c.g, c.b, 1f).linear : Color.black;
            }
            return hasEmissiveMap || e.importedEmission.maxColorComponent > 0f;
        }

        /// <summary>The GLB's JSON and binary chunks, with the '~m&lt;n&gt;' material lookups the builder needs.</summary>
        sealed class Glb
        {
            JObject _json;
            byte[] _bytes;
            int _binStart = -1, _binLength;
            string _path;
            readonly Dictionary<int, JObject> _byIndex = new();
            readonly Dictionary<int, (Color32[] px, int w, int h)?> _images = new(); // per glTF image index, rows bottom to top

            public static Glb Read(string path)
            {
                var g = new Glb { _path = path };
                try
                {
                    if (!File.Exists(path)) return g;
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67) return g; // 'glTF'
                    int len = (int)BitConverter.ToUInt32(bytes, 12);
                    if (BitConverter.ToUInt32(bytes, 16) != 0x4E4F534A || 20 + len > bytes.Length) return g; // 'JSON'
                    g._json = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, len));
                    g._bytes = bytes;
                    int bin = 20 + len;
                    if (bin + 8 <= bytes.Length && BitConverter.ToUInt32(bytes, bin + 4) == 0x004E4942) // 'BIN\0'
                    {
                        g._binStart = bin + 8;
                        g._binLength = (int)Math.Min(BitConverter.ToUInt32(bytes, bin), (uint)(bytes.Length - g._binStart));
                    }
                    if (g._json["materials"] is JArray mats)
                        foreach (var m in mats.OfType<JObject>())
                            if (LevelMaterials.ParseName((string)m["name"], out _, out _, out int n)) g._byIndex[n] = m;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AiOn] {path}: could not read the GLB ({ex.Message})");
                }
                return g;
            }

            /// <summary>KHR_materials_emissive_strength per material '~m&lt;n&gt;' index.</summary>
            public Dictionary<int, float> EmissiveStrengths()
            {
                var result = new Dictionary<int, float>();
                foreach (var kv in _byIndex)
                {
                    var s = kv.Value["extensions"]?["KHR_materials_emissive_strength"]?["emissiveStrength"];
                    if (s != null && s.Type != JTokenType.Null) result[kv.Key] = (float)s;
                }
                return result;
            }

            /// <summary>The material's embedded base colour ("map") or emissive ("emap") picture, decoded; null if none.</summary>
            public (Color32[] px, int w, int h)? DefaultPicture(int index, string slot)
            {
                try
                {
                    if (_binStart < 0 || !_byIndex.TryGetValue(index, out var m)) return null;
                    var ti = (slot == "map" ? m["pbrMetallicRoughness"]?["baseColorTexture"] : m["emissiveTexture"])?["index"];
                    if (ti == null || ti.Type == JTokenType.Null) return null;
                    var src = _json["textures"]?[(int)ti]?["source"];
                    if (src == null || src.Type == JTokenType.Null) return null;
                    int img = (int)src;
                    if (_images.TryGetValue(img, out var cached)) return cached;
                    _images[img] = null;
                    var bvi = _json["images"]?[img]?["bufferView"];
                    if (bvi == null || bvi.Type == JTokenType.Null || _json["bufferViews"]?[(int)bvi] is not JObject bv) return null;
                    if (((int?)bv["buffer"] ?? 0) != 0) return null;
                    int off = (int?)bv["byteOffset"] ?? 0, len = (int)bv["byteLength"];
                    if (off < 0 || off + len > _binLength) return null;
                    var data = new byte[len];
                    Array.Copy(_bytes, _binStart + off, data, 0, len);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        if (tex.LoadImage(data, false)) _images[img] = (tex.GetPixels32(), tex.width, tex.height);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(tex); }
                    return _images[img];
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AiOn] {_path}: could not read the default picture of ~m{index} {slot} ({ex.Message})");
                    return null;
                }
            }
        }

        /// <summary>
        /// A state picture, as a vertically flipped copy in StateTextures/Flipped: GLTFExporter flips canvas textures
        /// (flipY) when embedding them, while the state PNGs were saved straight from the canvas, so only the copy samples
        /// like the GLB's own picture on the same UVs. sRGB colour texture, clamped, with mipmaps, not readable.
        /// Null (counted in 'dropped') when the picture equals the GLB's default picture for that slot.
        /// </summary>
        static Texture2D LoadStateTexture(string file, List<string> missing, (Color32[] px, int w, int h)? def, ref int dropped)
        {
            if (string.IsNullOrEmpty(file)) return null;
            var src = $"{TextureDir}/{file}";
            if (!File.Exists(src)) { missing.Add(file); return null; }

            var path = $"{FlippedDir}/{file}";
            bool needWrite = !File.Exists(path) || File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(src);
            if (needWrite || def != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!tex.LoadImage(File.ReadAllBytes(src), false)) { missing.Add(file); return null; }
                    var flipped = LevelMaterials.FlipRows(tex.GetPixels32(), tex.width, tex.height);
                    if (def is { } d && d.w == tex.width && d.h == tex.height)
                    {
                        float diff = LevelMaterials.MeanAbsDiff(flipped, d.px);
                        if (diff >= 0f && diff < SameAsDefault) { dropped++; return null; }
                    }
                    if (needWrite)
                    {
                        tex.SetPixels32(flipped);
                        tex.Apply(false);
                        Directory.CreateDirectory(FlippedDir);
                        File.WriteAllBytes(path, tex.EncodeToPNG());
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(tex); }
            }

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                ti = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (ti != null)
            {
                bool dirty = false;
                void Set<T>(T now, T want, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
                Set(ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.sRGBTexture, true, v => ti.sRGBTexture = v);
                Set(ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
                Set(ti.isReadable, false, v => ti.isReadable = v);
                Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
                if (dirty) ti.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset == null) missing.Add(file);
            return asset;
        }
    }
}
