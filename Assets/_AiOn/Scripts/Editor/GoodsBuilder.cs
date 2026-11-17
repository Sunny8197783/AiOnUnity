using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds a shop's shelf goods from Art/Goods/&lt;id&gt;-goods.json (exported from the original's instanced goods):
    /// every item is baked into combined static meshes per section, state (full / out) and surface (paper, film, PET,
    /// tin, tag), with its label picked from the shared atlas, under a ShopGoods component that switches sold-out
    /// sections. The original frame is mirrored on z into Unity's, so triangles are re-wound.
    /// </summary>
    public static class GoodsBuilder
    {
        const string Dir = "Assets/_AiOn/Art/Goods";

        public static bool HasGoods(string levelId) => File.Exists($"{Dir}/{levelId}-goods.json");

        public static ShopGoods Build(GameObject levelRoot, string levelId)
        {
            var json = JObject.Parse(File.ReadAllText($"{Dir}/{levelId}-goods.json"));
            float cellScale = (float)json["cellScale"];
            var shapes = (JObject)json["shapes"];
            var batches = (JArray)json["batches"];

            var atlas = Texture($"{Dir}/{levelId}-goods-atlas.png");
            var tagTex = Texture($"{Dir}/{levelId}-goods-tag.png");
            var meshDir = $"{Dir}/Meshes";
            if (!AssetDatabase.IsValidFolder(meshDir)) AssetDatabase.CreateFolder(Dir, "Meshes");

            var root = new GameObject("Goods");
            root.transform.SetParent(levelRoot.transform, false);
            var goods = root.AddComponent<ShopGoods>();

            // Group batches: section -> state -> material key -> batches.
            var groups = batches.Cast<JObject>()
                .GroupBy(b => ((string)b["section"], (string)b["state"]))
                .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2);
            int meshCount = 0, items = 0;
            foreach (var g in groups)
            {
                var (section, state) = g.Key;
                var sec = goods.sections.Find(s => s.name == section);
                if (sec == null) { sec = new ShopGoods.Section { name = section }; goods.sections.Add(sec); }
                var holder = new GameObject($"{section}_{state}");
                holder.transform.SetParent(root.transform, false);
                if (state == "full") sec.full = holder; else sec.@out = holder;

                foreach (var byMat in g.GroupBy(b => ((bool)b["tag"], (float)b["roughness"], (float)b["metalness"])))
                {
                    var (tag, rough, metal) = byMat.Key;
                    var mat = Material(levelId, tag ? tagTex : atlas, tag, rough, metal);
                    var mesh = Combine(byMat, shapes, tag ? 1f : cellScale, tag);
                    if (mesh.vertexCount == 0) continue;
                    var path = $"{meshDir}/{levelId}_{section}_{state}_{(tag ? "tag" : $"r{rough:0.00}m{metal:0.0}")}.asset";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject(Path.GetFileNameWithoutExtension(path));
                    go.transform.SetParent(holder.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    meshCount++;
                    items += byMat.Sum(b => ((JArray)b["instances"]).Count);
                }
            }
            goods.SetSoldOut(new string[0]);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Goods for {levelId}: {items} items in {meshCount} meshes, sections {string.Join(", ", goods.sections.Select(s => s.name))}");
            return goods;
        }

        static Mesh Combine(IEnumerable<JObject> batches, JObject shapes, float cellScale, bool tag)
        {
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var b in batches)
            {
                var shape = (JObject)shapes[(string)b["shape"]];
                var sp = ((JArray)shape["positions"]).Select(t => (float)t).ToArray();
                var sn = shape["normals"] is JArray na ? na.Select(t => (float)t).ToArray() : null;
                var su = shape["uvs"] is JArray ua ? ua.Select(t => (float)t).ToArray() : null;
                var si = shape["indices"] is JArray ia ? ia.Select(t => (int)t).ToArray() : Enumerable.Range(0, sp.Length / 3).ToArray();
                int vc = sp.Length / 3;
                foreach (JArray inst in (JArray)b["instances"])
                {
                    // three.js column-major 4x4 world matrix, then tint, cellU, cellV.
                    var e = inst.Take(16).Select(t => (float)t).ToArray();
                    var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m[r, c] = e[c * 4 + r];
                    var nm = m.inverse.transpose;
                    var cell = new Vector2((float)inst[17], (float)inst[18]);
                    int baseV = pos.Count;
                    for (int v = 0; v < vc; v++)
                    {
                        var p = m.MultiplyPoint3x4(new Vector3(sp[v * 3], sp[v * 3 + 1], sp[v * 3 + 2]));
                        pos.Add(new Vector3(p.x, p.y, -p.z));
                        var n = sn != null ? nm.MultiplyVector(new Vector3(sn[v * 3], sn[v * 3 + 1], sn[v * 3 + 2])).normalized : Vector3.up;
                        nor.Add(new Vector3(n.x, n.y, -n.z));
                        var t = su != null ? new Vector2(su[v * 2], su[v * 2 + 1]) : Vector2.zero;
                        uv.Add(tag ? t : t * cellScale + cell);
                    }
                    for (int k = 0; k + 2 < si.Length; k += 3) // mirrored frame: reverse the winding
                    { tri.Add(baseV + si[k]); tri.Add(baseV + si[k + 2]); tri.Add(baseV + si[k + 1]); }
                }
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(pos); mesh.SetNormals(nor); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        static Material Material(string levelId, Texture2D tex, bool tag, float rough, float metal)
        {
            var path = $"{Dir}/{levelId}_goods_{(tag ? "tag" : $"r{rough:0.00}m{metal:0.0}")}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 1f - rough);
            m.SetFloat("_Metallic", metal);
            if (tag) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.2f, 0f, 0f)); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Texture(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.anisoLevel < 4 || imp.maxTextureSize < 2048))
            {
                imp.anisoLevel = 4; imp.maxTextureSize = 2048; imp.mipmapEnabled = true; imp.sRGBTexture = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
