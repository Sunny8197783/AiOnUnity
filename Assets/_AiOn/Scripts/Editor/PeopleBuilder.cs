using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.People;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Turns each baked person model (Art/People/person-&lt;name&gt;.glb: a skinned mesh and 27 clips sampled from the
    /// original's procedural animation) into a ready prefab: looping copies of the clips, an Animator controller with a
    /// state per clip plus the walk-brisk-run blend tree, and Prefabs/People/&lt;name&gt;.prefab (root faces +z).
    /// </summary>
    public static class PeopleBuilder
    {
        const string SrcDir = "Assets/_AiOn/Art/People";
        const string AnimDir = "Assets/_AiOn/Art/People/Anim";
        const string PrefabDir = "Assets/_AiOn/Prefabs/People";
        static readonly string[] OneShots = { "punch", "throw", "pickup", "vault", "climb", "land", "hurt" };

        public static string PrefabPath(string name) => $"{PrefabDir}/{name}.prefab";

        [MenuItem("AiOn/Build People")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(AnimDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("person- t:GameObject", new[] { SrcDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".glb")) continue;
                Build(path);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Built {n} people prefabs in {PrefabDir}");
        }

        static void Build(string glbPath)
        {
            var name = Path.GetFileNameWithoutExtension(glbPath).Substring("person-".Length);
            var dir = $"{AnimDir}/{name}";
            Directory.CreateDirectory(dir);

            // Looping copies of the imported clips (imported clips are read-only and do not loop).
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>())
            {
                var clipPath = $"{dir}/{src.name}.anim";
                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (copy == null) { copy = new AnimationClip(); AssetDatabase.CreateAsset(copy, clipPath); }
                EditorUtility.CopySerialized(src, copy);
                copy.name = src.name;
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = !src.name.StartsWith("clip_") && src.name != "down";
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                EditorUtility.SetDirty(copy);
                clips[src.name] = copy;
            }

            var controllerPath = $"{dir}/{name}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            if (clips.TryGetValue("idle", out var idle)) sm.defaultState = sm.AddState("idle").WithMotion(idle);
            foreach (var (clipName, clip) in clips)
            {
                if (clipName == "idle" || clipName.StartsWith("clip_") || clipName == "walk" || clipName == "brisk" || clipName == "run") continue;
                sm.AddState(clipName).motion = clip;
            }
            if (clips.ContainsKey("walk") && clips.ContainsKey("brisk") && clips.ContainsKey("run"))
            {
                var loco = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = "Speed";
                tree.useAutomaticThresholds = false;
                tree.AddChild(clips["walk"], PersonAnimator.WalkSpeed);
                tree.AddChild(clips["brisk"], PersonAnimator.BriskSpeed);
                tree.AddChild(clips["run"], PersonAnimator.RunSpeed);
            }

            // One-shot clips on an override layer: an empty state, each clip returns to it when done.
            controller.AddLayer("Action");
            var layers = controller.layers;
            layers[1].defaultWeight = 1f;
            controller.layers = layers;
            var act = controller.layers[1].stateMachine;
            var none = act.AddState("none");
            act.defaultState = none;
            foreach (var shot in OneShots)
            {
                if (!clips.TryGetValue("clip_" + shot, out var clip)) continue;
                var st = act.AddState("clip_" + shot);
                st.motion = clip;
                var back = st.AddTransition(none);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
            }
            EditorUtility.SetDirty(controller);

            // Prefab: the root faces +z (Unity forward); the imported model faces -z, so it is turned half round inside.
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(glbPath), root.transform);
            model.name = "Model";
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var pa = model.AddComponent<PersonAnimator>();
            pa.preset = name;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = false;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
            Object.DestroyImmediate(root);
        }

        static AnimatorState WithMotion(this AnimatorState s, Motion m) { s.motion = m; return s; }
    }
}
