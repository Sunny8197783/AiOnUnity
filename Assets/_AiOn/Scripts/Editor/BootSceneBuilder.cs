using System.Linq;
using AiOn.Game;
using AiOn.Interaction;
using AiOn.Player;
using AiOn.Story;
using AiOn.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds Scenes/Boot.unity, the scene the game starts in: the persistent player, camera, HUD, story overlay and the
    /// UnityStoryHost that loads the level scenes on top (each level scene's own test player is removed on load).
    /// The story runner that drives the chapters is added here once the story is in the project.
    /// </summary>
    public static class BootSceneBuilder
    {
        const string BootPath = "Assets/_AiOn/Scenes/Boot.unity";

        [MenuItem("AiOn/Build Boot Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);

            PlayerSetup.CreatePlayer();
            var systems = GameObject.Find("GameSystems");
            var world = systems.GetComponent<WorldInteractions>();
            if (world) Object.DestroyImmediate(world); // the story answers E on every point

            var overlayGo = new GameObject("StoryOverlay");
            var overlay = overlayGo.AddComponent<StoryOverlay>();

            var hostGo = new GameObject("StoryHost");
            var host = hostGo.AddComponent<UnityStoryHost>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            host.player = player;
            host.cameraRig = Object.FindFirstObjectByType<PlayerCameraRig>();
            host.interactor = player.GetComponent<PlayerInteractor>();
            host.overlay = overlay;
            host.audioLibrary = AssetDatabase.LoadAssetAtPath<AudioLibrary>("Assets/_AiOn/Data/AudioLibrary.asset") ?? AudioLibraryBuilder.Build();
            host.peoplePrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_AiOn/Prefabs/People" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p).ToList();
            host.levelLayouts = Layouts();

            // The runner (chapters) is attached by StoryRunnerSetup when the story code is present.
            var runnerType = System.Type.GetType("AiOn.Story.StoryRunner, AiOn.Runtime");
            if (runnerType != null) hostGo.AddComponent(runnerType);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AiOn] Boot scene built: host, overlay, player, {host.peoplePrefabs.Count} people prefabs{(runnerType != null ? ", story runner" : " (no story runner yet)")}");
        }

        static System.Collections.Generic.List<AiOn.Levels.LevelLayout> Layouts() =>
            AssetDatabase.FindAssets("t:LevelLayout", new[] { "Assets/_AiOn/Data/Levels" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AiOn.Levels.LevelLayout>(AssetDatabase.GUIDToAssetPath(g))).Where(l => l).ToList();

        /// <summary>Sets only the story host's level layouts (the crowds walk on them) in the boot scene, without a rebuild.</summary>
        [MenuItem("AiOn/Wire Level Layouts")]
        public static void WireLayouts()
        {
            var scene = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<UnityStoryHost>();
            if (!host) { Debug.LogError("[AiOn] Boot scene has no UnityStoryHost; run AiOn/Build Boot Scene"); return; }
            host.levelLayouts = Layouts();
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AiOn] Boot scene: {host.levelLayouts.Count} level layouts wired to the story host");
        }
    }
}
