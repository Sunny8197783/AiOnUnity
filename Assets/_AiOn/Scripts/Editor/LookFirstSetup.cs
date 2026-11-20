using System.Linq;
using AiOn.Interaction;
using AiOn.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiOn.EditorTools
{
    /// <summary>
    /// "불나면 살펴서 대피" (competition upgrade): puts the balcony partition's interaction point (homePartition, from
    /// Data/Source/apartment-level.json) into the Apartment scene next to the other points, without rebuilding the level.
    /// Idempotent: an existing point is updated in place. The boxes in front of it are runtime dressing (InteriorDressing).
    /// </summary>
    public static class LookFirstSetup
    {
        const string ScenePath = "Assets/_AiOn/Scenes/Levels/Apartment.unity";
        const string PointId = "homePartition";

        [MenuItem("AiOn/Levels/Add Partition Point (Apartment)")]
        public static void AddPartitionPoint()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[AiOn] Leave play mode first."); return; }
            var layout = LevelLayoutImporter.Import("Assets/_AiOn/Data/Source/apartment-level.json");
            var p = layout.points.FirstOrDefault(x => x.id == PointId);
            if (p == null) { Debug.LogError("[AiOn] apartment-level.json has no " + PointId); return; }
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InteractionPoint>(true)).ToList();
                var stock = all.FirstOrDefault(x => x.id == "homeStock");
                if (stock == null) { Debug.LogError("[AiOn] Apartment has no homeStock point to sit beside"); return; }
                var ip = all.FirstOrDefault(x => x.id == PointId);
                if (ip == null)
                {
                    var go = new GameObject("Point_" + PointId);
                    go.transform.SetParent(stock.transform.parent, false);
                    go.transform.SetSiblingIndex(stock.transform.GetSiblingIndex() + 1);
                    ip = go.AddComponent<InteractionPoint>();
                }
                ip.transform.localPosition = LevelLayout.ToUnity(p.x, p.y, p.z);
                ip.id = p.id; ip.label = p.label; ip.kind = p.kind; ip.radius = p.r; ip.reach = p.reach; ip.host = p.host; ip.interactable = p.enabled;
                EditorUtility.SetDirty(ip);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[AiOn] Apartment: point {PointId} at {ip.transform.position}");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
