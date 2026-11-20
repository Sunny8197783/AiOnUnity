using AiOn.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// AiOn → Data → Wire Facts Extension: points every GameSession in the Build Settings scenes at
    /// Data/Source/facts-ext.json (GameSession.factsExtJson), so the story, the guide and the report read the merged
    /// facts. Safe to run again; the open scenes are restored afterwards. New scenes get it from PlayerSetup.
    /// </summary>
    public static class FactsExtWiring
    {
        public const string ExtPath = "Assets/_AiOn/Data/Source/facts-ext.json";

        [MenuItem("AiOn/Data/Wire Facts Extension")]
        public static void WireAll() => Debug.Log("[AiOn] facts-ext wired: " + Wire());

        /// <summary>Wires every enabled Build Settings scene; returns "scene:sessions" per changed scene.</summary>
        public static string Wire()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "skipped (play mode)";
            var ext = AssetDatabase.LoadAssetAtPath<TextAsset>(ExtPath);
            if (!ext) return "missing " + ExtPath;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return "skipped: an open scene has unsaved changes (save it first)";
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var report = new System.Text.StringBuilder();
            try
            {
                foreach (var entry in EditorBuildSettings.scenes)
                {
                    if (!entry.enabled) continue;
                    var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                    int changed = 0;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var s in root.GetComponentsInChildren<GameSession>(true))
                        {
                            if (s.factsExtJson == ext) continue;
                            Undo.RecordObject(s, "Wire facts-ext");
                            s.factsExtJson = ext;
                            EditorUtility.SetDirty(s);
                            changed++;
                        }
                    if (changed > 0) { EditorSceneManager.SaveScene(scene); report.Append(scene.name).Append(':').Append(changed).Append(' '); }
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            return report.Length > 0 ? report.ToString().Trim() : "already wired";
        }
    }
}
