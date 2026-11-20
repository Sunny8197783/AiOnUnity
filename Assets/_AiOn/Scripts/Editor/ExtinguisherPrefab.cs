using System.IO;
using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// 3D 소화기 (competition upgrade): the CC0 Poly Haven korean_fire_extinguisher_01 (glTF, imported by glTFast) as a
    /// Resources prefab, so the runtime (Story/HeldExtinguisher) can load it with Resources.Load. Safe to run again.
    /// </summary>
    public static class ExtinguisherPrefab
    {
        const string Source = "Assets/_AiOn/ThirdParty/Props/RobotNativeEngine/korean_fire_extinguisher_01/korean_fire_extinguisher_01_1k.gltf";
        const string Target = "Assets/_AiOn/Resources/Props/KoreanExtinguisher.prefab";

        [MenuItem("AiOn/Props/Build Extinguisher Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (!model) { Debug.LogError($"[AiOn] extinguisher model not found at {Source}"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Target));
            var go = (GameObject)Object.Instantiate(model);
            go.name = "KoreanExtinguisher";
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(go, Target);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] extinguisher prefab -> {Target}");
        }
    }
}
