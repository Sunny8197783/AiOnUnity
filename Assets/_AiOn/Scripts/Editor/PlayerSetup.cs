using AiOn.Data;
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
    /// <summary>Puts the player (body, input, controller) and its camera rig into the open scene.</summary>
    public static class PlayerSetup
    {
        [MenuItem("AiOn/Create Player In Open Scene")]
        public static void CreatePlayer()
        {
            foreach (var name in new[] { "Player", "PlayerCamera", "GameSystems" })
            {
                var old = GameObject.Find(name);
                if (old) Object.DestroyImmediate(old);
            }
            // The template's camera would fight ours for the screen.
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.CompareTag("MainCamera")) Object.DestroyImmediate(c.gameObject);

            var player = new GameObject("Player");
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerInputReader>();
            var pc = player.AddComponent<PlayerController>();
            var levelSpawns = AiOn.Levels.LevelSpawnPoints.Current;
            pc.startSpawn = levelSpawns == null || levelSpawns.TryGet("home", out _) || levelSpawns.points.Count == 0 ? "home" : levelSpawns.points[0].name;

            // The body seen in third person: the original's 정민 model, or a capsule until AiOn → Build People has run.
            GameObject body;
            var personPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PeopleBuilder.PrefabPath("jeongmin"));
            if (personPrefab != null)
            {
                body = (GameObject)PrefabUtility.InstantiatePrefab(personPrefab, player.transform);
                body.name = "Body";
                var pb = player.AddComponent<PlayerBody>();
                pb.player = pc;
                pb.person = body.GetComponentInChildren<AiOn.People.PersonAnimator>();
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(player.transform, false);
                body.transform.localPosition = new Vector3(0, PlayerController.Height / 2, 0);
                body.transform.localScale = new Vector3(PlayerController.Radius * 2, PlayerController.Height / 2, PlayerController.Radius * 2);
            }

            var camGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1400f; // the sky rig's distant city (330-630 m) and hills (760 / 1100 m)
            cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true; // the environment's tone mapping, bloom and grade
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            var rig = camGo.AddComponent<PlayerCameraRig>();
            rig.player = pc;
            rig.cam = cam;
            rig.body = body;
            rig.thirdPerson = true; // the original's default view

            // Start the camera at the spawn so the Scene view and first frame look right.
            var spawns = AiOn.Levels.LevelSpawnPoints.Current;
            if (spawns != null && spawns.TryGet(pc.startSpawn, out var p))
            {
                player.transform.SetPositionAndRotation(p.position, Quaternion.Euler(0, p.yaw, 0));
                camGo.transform.SetPositionAndRotation(p.position + Vector3.up * PlayerController.Eye, Quaternion.Euler(3, p.yaw, 0));
            }

            player.AddComponent<PlayerInteractor>().cameraRig = rig;

            // Game systems: the session (rules, state, facts), the HUD and the level's interactions.
            var systems = new GameObject("GameSystems");
            var session = systems.AddComponent<GameSession>();
            session.items = AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/_AiOn/Data/ItemDatabase.asset");
            session.factsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_AiOn/Data/Source/facts.json");
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(systems.transform, false);
            var hud = hudGo.AddComponent<GameHud>();
            hud.interactor = player.GetComponent<PlayerInteractor>();
            var world = systems.AddComponent<WorldInteractions>();
            world.interactor = hud.interactor;
            world.hud = hud;
            world.session = session;

            EditorSceneManager.MarkSceneDirty(player.scene);
            Selection.activeGameObject = player;
            Debug.Log("[AiOn] Player, PlayerCamera and GameSystems created");
        }
    }
}
