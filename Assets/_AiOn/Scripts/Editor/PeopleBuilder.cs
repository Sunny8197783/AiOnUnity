using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.People;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds the people prefabs (Prefabs/People/&lt;preset&gt;.prefab, root faces +z) from the Rocketbox avatars of
    /// <see cref="RocketboxCast"/> and the Humanoid clips of <see cref="PersonClips"/>: one Animator controller per clip set
    /// (male, female, elder) with the game's state names, the "Locomotion" blend tree on Speed and the "Action" layer of
    /// one-shots, so <see cref="PersonAnimator"/> drives them exactly as it drove the original baked people. Each prefab
    /// gets the avatar at its Korean-average scale (no half turn: the FBX files already face +z), grips at the palms and an
    /// anchor on the back for props (<see cref="PersonRig"/>), and heroes a <see cref="PersonFace"/> (blink, talking mouth,
    /// head and eye look). Existing prefabs are edited in place so scene instances and the boot scene's list keep them.
    /// The original glb people (Art/People/person-*.glb) are only the source of the game's own clips now.
    /// Crowd members get a lighter twin of their set's controller (no IK pass, no foot IK: they have no face to aim and are
    /// seen from further away). Every prefab's <see cref="PersonAnimator.gaitScale"/> is measured: the walk is played on a
    /// copy of that body at its scale and the ground it covers is compared with the speed asked for (Humanoid motion
    /// grows with the avatar's size and the transform's scale, which one shared controller cannot know).
    /// </summary>
    public static class PeopleBuilder
    {
        const string PrefabDir = "Assets/_AiOn/Prefabs/People";

        public static string PrefabPath(string name) => $"{PrefabDir}/{name}.prefab";
        public static string ControllerPath(PersonClips.Set s, bool crowd = false) =>
            $"{PersonClips.AnimFolder}/people_{s.ToString().ToLowerInvariant()}{(crowd ? "_crowd" : "")}.controller";

        /// <summary>Whether a cast row gets the crowd controller (no IK): crowd members.</summary>
        public static bool UsesCrowdController(RocketboxCast.Entry e) => e.Tier == RocketboxCast.Tier.Crowd && PersonClips.SetOf(e) != PersonClips.Set.Elder;

        [MenuItem("AiOn/Build People")]
        public static void BuildAll() => Debug.Log("[AiOn] " + Build());

        /// <summary>Controllers, then every preset's prefab, then the scenes' player body links. Returns a short report.</summary>
        public static string Build()
        {
            Directory.CreateDirectory(PrefabDir);
            var report = new System.Text.StringBuilder();
            report.Append(PeopleArmPoses.Build());
            var controllers = new Dictionary<(PersonClips.Set, bool), AnimatorController>();
            foreach (PersonClips.Set s in System.Enum.GetValues(typeof(PersonClips.Set)))
                foreach (var crowd in s == PersonClips.Set.Elder ? new[] { false } : new[] { false, true })
                {
                    controllers[(s, crowd)] = BuildController(s, crowd, out var note);
                    report.AppendLine($"{s}{(crowd ? " crowd" : "")}: {note}");
                }
            int n = 0;
            foreach (var e in RocketboxCast.All)
            {
                if (e.Preset == null) continue;
                var r = BuildPrefab(e, controllers[(PersonClips.SetOf(e), UsesCrowdController(e))]);
                report.AppendLine($"{e.Preset}: {r}");
                n++;
            }
            AssetDatabase.SaveAssets();
            report.Insert(0, $"Built {n} people prefabs\n");
            return report.ToString();
        }

        // ---------------------------------------------------------------- controller

        static AnimationClip Clip(string file, string state) => PeopleAnimImport.Clip(file, state);

        static float GroundSpeed(AnimationClip c) => c ? new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude : 0f;

        static AnimatorController BuildController(PersonClips.Set set, bool crowd, out string note)
        {
            var path = ControllerPath(set, crowd);
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = PersonClips.GaitScaleParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            bool ik = !crowd; // heroes: PersonFace looks with the head and eyes (OnAnimatorIK); feet held to the clip's contacts
            var layers = controller.layers;
            layers[0].iKPass = ik;
            controller.layers = layers;
            var sm = controller.layers[0].stateMachine;
            var missing = new List<string>();

            var idleClip = Clip(PersonClips.RbFile(set, false), "idle");
            var idle = sm.AddState("idle");
            idle.motion = idleClip; idle.iKOnFeet = ik;
            sm.defaultState = idle;
            if (!idleClip) missing.Add("idle");

            // the locomotion blend: children at the game's gait speeds, played at speed / clip speed
            var loco = PersonClips.RbFile(set, true);
            AnimationClip walk = Clip(loco, "walk"), brisk = Clip(loco, "brisk"), run = Clip(loco, "run"), slow = Clip(loco, "slow");
            var children = PersonClips.Locomotion(set, GroundSpeed(walk), GroundSpeed(brisk), GroundSpeed(run), GroundSpeed(slow));
            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.hideFlags = HideFlags.HideInHierarchy;
            var motions = new List<ChildMotion>();
            foreach (var ch in children)
            {
                var c = Clip(loco, ch.Take);
                if (!c) { missing.Add(ch.Take); continue; }
                motions.Add(new ChildMotion { motion = c, threshold = ch.Threshold, timeScale = ch.TimeScale, directBlendParameter = "Speed" });
            }
            tree.children = motions.ToArray();
            var locoState = sm.AddState("Locomotion");
            locoState.motion = tree; locoState.iKOnFeet = ik;
            locoState.speedParameter = PersonClips.GaitScaleParam; locoState.speedParameterActive = true;

            foreach (var c in PersonClips.States(set))
            {
                if (c.State == "idle" || c.State.StartsWith("clip_")) continue;
                var clip = Clip(c.File, c.State);
                if (!clip) { missing.Add(c.State); continue; }
                var st = sm.AddState(c.State);
                st.iKOnFeet = ik && c.State != "air";
                if (c.State == "carry")
                {
                    // the walk with the hold-bag arm, as a blend on Speed like the locomotion (the player carries at 3 m/s)
                    var ct = new BlendTree { name = "Carry", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
                    AssetDatabase.AddObjectToAsset(ct, controller);
                    ct.hideFlags = HideFlags.HideInHierarchy;
                    var cm = new List<ChildMotion>();
                    foreach (var ch in PersonClips.Carry(GroundSpeed(clip)))
                        cm.Add(new ChildMotion { motion = clip, threshold = ch.Threshold, timeScale = ch.TimeScale, directBlendParameter = "Speed" });
                    ct.children = cm.ToArray();
                    st.motion = ct;
                    st.speedParameter = PersonClips.GaitScaleParam; st.speedParameterActive = true;
                }
                else st.motion = clip;
            }

            // one-shots on an override layer: an empty state, each clip returns to it when done
            controller.AddLayer("Action");
            layers = controller.layers;
            layers[1].defaultWeight = 1f;
            controller.layers = layers;
            var act = controller.layers[1].stateMachine;
            var none = act.AddState("none");
            act.defaultState = none;
            foreach (var o in PersonClips.OneShots)
            {
                var clip = Clip(PersonClips.GameFile(set), "clip_" + o);
                if (!clip) { missing.Add("clip_" + o); continue; }
                var st = act.AddState("clip_" + o);
                st.motion = clip;
                var back = st.AddTransition(none);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
            }
            // arm poses over everything: both arms and the fingers (the cart handle, hands resting while sitting)
            var mask = PeopleArmPoses.Mask();
            controller.AddLayer(PersonClips.ArmsLayerName);
            layers = controller.layers;
            layers[PersonClips.ArmsLayer].defaultWeight = 1f;
            layers[PersonClips.ArmsLayer].avatarMask = mask;
            layers[PersonClips.ArmsLayer].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            var arms = controller.layers[PersonClips.ArmsLayer].stateMachine;
            arms.defaultState = arms.AddState(PersonClips.ArmsNone);
            foreach (var pose in PersonClips.ArmPoses)
            {
                var clip = PeopleArmPoses.Clip(pose);
                if (!clip || !mask) { missing.Add("arms_" + pose); continue; }
                arms.AddState(pose).motion = clip;
            }
            EditorUtility.SetDirty(controller);
            note = $"{(crowd ? "no IK; " : "")}locomotion {string.Join(" ", motions.Select(m => $"{m.motion.name}@{m.threshold:0.0}x{m.timeScale:0.00}"))}" +
                   (missing.Count > 0 ? $"; MISSING {string.Join(",", missing)}" : "");
            return controller;
        }

        // ---------------------------------------------------------------- prefab

        static string BuildPrefab(RocketboxCast.Entry e, AnimatorController controller)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RocketboxCast.ModelPath(e.Avatar));
            if (!modelAsset) return "no model " + e.Avatar;
            var path = PrefabPath(e.Preset);
            GameObject root;
            bool existing = File.Exists(path);
            root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(e.Preset);
            try
            {
                // the old body goes (children are rebuilt every time)
                var kids = new List<GameObject>();
                foreach (Transform t in root.transform) kids.Add(t.gameObject);
                foreach (var k in kids) Object.DestroyImmediate(k);
                foreach (var c in root.GetComponents<Component>())
                    if (!(c is Transform)) Object.DestroyImmediate(c);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity; // the FBX already faces +z
                model.transform.localScale = Vector3.one * e.KoreanScale;
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var pa = model.AddComponent<PersonAnimator>();
                pa.preset = e.Preset;
                float walked = MeasureWalk(modelAsset, controller, e.KoreanScale);
                pa.gaitScale = walked > 0.3f ? Mathf.Round(PersonAnimator.WalkSpeed / walked * 1000f) / 1000f : 1f;
                bool hero = e.Tier == RocketboxCast.Tier.Hero;
                if (hero) model.AddComponent<PersonFace>();
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.updateWhenOffscreen = false;
                    smr.quality = hero ? SkinQuality.Bone4 : SkinQuality.Bone2;
                    smr.skinnedMotionVectors = hero;
                    smr.allowOcclusionWhenDynamic = true;
                }
                var anchors = PersonRig.BuildAnchors(animator);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{e.Avatar} x{e.KoreanScale:0.00} ({e.KoreanHeight:0.00} m), {controller.name}, walk {walked:0.000} m/s -> gait x{pa.gaitScale:0.000}, {anchors}";
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Ground speed (m/s) of this body walking at the game's walk speed with the gait correction off: the model at its
        /// scale with the controller, root motion on, stepped for two seconds in a preview scene.
        /// </summary>
        public static float MeasureWalk(GameObject modelAsset, RuntimeAnimatorController controller, float scale)
        {
            using var s = new RocketboxPreview.Stage(false);
            var go = (GameObject)Object.Instantiate(modelAsset);
            s.Add(go);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            var a = go.GetComponent<Animator>();
            if (!a) return 0f;
            a.runtimeAnimatorController = controller;
            a.applyRootMotion = true;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.Rebind();
            a.SetFloat("Speed", PersonAnimator.WalkSpeed);
            a.SetFloat(PersonClips.GaitScaleParam, 1f);
            a.Play("Locomotion", 0, 0f);
            a.Update(0f);
            var p0 = go.transform.position;
            const float dt = 1f / 30f; const int steps = 60;
            for (int i = 0; i < steps; i++) a.Update(dt);
            var d = go.transform.position - p0; d.y = 0;
            return d.magnitude / (steps * dt);
        }

        // ---------------------------------------------------------------- scenes

        /// <summary>
        /// Points every scene's PlayerBody at the PersonAnimator inside its body again (a rebuilt prefab gives the
        /// component a new id). Opens and saves Boot and the level scenes.
        /// </summary>
        [MenuItem("AiOn/People/Relink Player Bodies")]
        public static void RelinkPlayerBodies() => Debug.Log("[AiOn] " + Relink());

        public static string Relink()
        {
            var log = new System.Text.StringBuilder();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scenes = new List<string> { "Assets/_AiOn/Scenes/Boot.unity" };
                scenes.AddRange(Directory.GetFiles("Assets/_AiOn/Scenes/Levels", "*.unity").Select(p => p.Replace('\\', '/')));
                foreach (var sp in scenes)
                {
                    var scene = EditorSceneManager.OpenScene(sp, OpenSceneMode.Single);
                    int fixedN = 0;
                    foreach (var pb in Object.FindObjectsByType<AiOn.Player.PlayerBody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var pa = pb.GetComponentInChildren<PersonAnimator>(true);
                        if (pb.person == pa) continue;
                        pb.person = pa;
                        EditorUtility.SetDirty(pb);
                        fixedN++;
                    }
                    if (fixedN > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
                    log.AppendLine($"{Path.GetFileNameWithoutExtension(sp)}: {fixedN} relinked");
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            return log.ToString();
        }
    }
}
