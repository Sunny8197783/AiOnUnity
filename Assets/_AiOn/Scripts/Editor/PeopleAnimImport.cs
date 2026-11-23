using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AiOn.People;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Import rules for the people's animation files in Art/People/Rocketbox/Anim (see <see cref="PersonClips"/>): every FBX
    /// there is an armature with takes, imported as Humanoid with its own avatar. AiOn → People → Import Person Clips maps
    /// the bones by hand (Rocketbox Biped names, or the game's 18-bone rig), straightens the rest pose into a T-pose
    /// ("Enforce T-Pose" done on an instance, as for the avatars) and sets each take up as a clip named after its game
    /// state: loops with Loop Pose, rotation and height baked into the pose from the original, XZ baked too except on the
    /// travelling gaits, whose ground motion stays root motion that the game drops (people are moved by script).
    /// </summary>
    public class PeopleAnimImport : AssetPostprocessor
    {
        public override uint GetVersion() => 3;

        static bool Ours(string path) => path.StartsWith(PersonClips.AnimFolder + "/") && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.useFileUnits = true;
            mi.bakeAxisConversion = true; // as the avatars: Blender's -Y front comes in as +Z
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = true;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.animationRotationError = 0.3f;
            mi.animationPositionError = 0.3f;
            mi.animationScaleError = 0.5f;
        }

        public static string PathOf(string file) => $"{PersonClips.AnimFolder}/{file}.fbx";

        static bool IsGameRig(string file) => file.StartsWith("game_");

        /// <summary>The state a take plays (FBX take names come as "Armature|take").</summary>
        public static string TakeState(string takeName)
        {
            int bar = takeName.LastIndexOf('|');
            return bar >= 0 ? takeName.Substring(bar + 1) : takeName;
        }

        [MenuItem("AiOn/People/Import Person Clips")]
        public static void ImportAllMenu() => Debug.Log("[AiOn] Person clips\n" + ImportAll());

        public static string ImportAll()
        {
            var files = new[] { PersonClips.Set.Male, PersonClips.Set.Female, PersonClips.Set.Elder }
                .SelectMany(PersonClips.Files).Distinct().ToList();
            var log = new StringBuilder();
            foreach (var f in files) log.AppendLine(ImportOne(f));
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        public static string ImportOne(string file)
        {
            var path = PathOf(file);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return $"{file}: missing";
            mi.SaveAndReimport(); // the rules above

            // humanoid map and a T-pose skeleton from a straightened instance
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var inst = (GameObject)Object.Instantiate(model);
            inst.name = model.name;
            string pose;
            try
            {
                List<HumanBone> human;
                if (IsGameRig(file)) { pose = StraightenGameRig(inst.transform); human = GameMap(inst.transform).ToList(); }
                else { pose = RocketboxImport.StraightenToTPose(inst.transform); human = RocketboxImport.HumanMap(inst.transform).ToList(); }
                var hd = mi.humanDescription;
                hd.human = human.ToArray();
                // Rocketbox takes: the bones' T-pose rotations are the avatars' own (same Biped skeleton and bone axes), so a
                // take's local rotations map one to one; each file's rest differs (some stand with bent knees)
                var refRot = IsGameRig(file) ? null : ReferenceRotations(file.StartsWith("rb_female") ? "seoyeon" : "jeongmin");
                if (refRot != null) pose += $", {refRot.Count} bone rotations from the avatar";
                hd.skeleton = inst.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, scale = t.localScale,
                    rotation = refRot != null && t.name.StartsWith("Bip01 ") && refRot.TryGetValue(t.name, out var q) ? q : t.localRotation,
                }).ToArray();
                hd.hasTranslationDoF = false;
                mi.humanDescription = hd;
            }
            finally { Object.DestroyImmediate(inst); }

            // clips named after their states
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var take in mi.importedTakeInfos)
            {
                var state = TakeState(take.name);
                if (state.StartsWith("_")) continue; // "_rest": the rest pose the model is posed in at import (first take)
                bool loop, travels, feet = false;
                if (IsGameRig(file))
                {
                    loop = !state.StartsWith("clip_") && state != "down";
                    travels = false;
                    // the game rig's floor poses keep the pelvis at standing height (only the legs fold: the original
                    // lowered nothing), so their height comes from the feet; jumps and vaults keep the original height
                    feet = GameFeetBased(state);
                }
                else
                {
                    loop = state != "knock";
                    travels = PersonClips.Travels(state);
                }
                clips.Add(new ModelImporterClipAnimation
                {
                    name = state,
                    takeName = take.name,
                    firstFrame = take.startTime * take.sampleRate,
                    lastFrame = take.stopTime * take.sampleRate,
                    loopTime = loop,
                    loopPose = loop,
                    lockRootRotation = true, keepOriginalOrientation = true,
                    lockRootHeightY = true, keepOriginalPositionY = !feet, // "Original" wins over "Feet" when both are set
                    lockRootPositionXZ = !travels, keepOriginalPositionXZ = true,
                    heightFromFeet = feet,
                });
            }
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();

            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.Avatar>().FirstOrDefault();
            var got = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            var speeds = string.Join(", ", got.Select(c => $"{c.name} {c.length:0.0}s {(c.isHumanMotion ? "" : "NOT HUMAN ")}{new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude:0.00}m/s"));
            return $"{file}: avatar {(av && av.isValid && av.isHuman ? "ok" : "INVALID")}, {pose}, {got.Count} clips: {speeds}";
        }

        /// <summary>T-pose local rotations of a cast avatar's bones (its importer's human description), by bone name.</summary>
        static Dictionary<string, Quaternion> ReferenceRotations(string preset)
        {
            var e = RocketboxCast.ForPreset(preset);
            if (e == null || !(AssetImporter.GetAtPath(RocketboxCast.ModelPath(e.Avatar)) is ModelImporter ami)) return null;
            var d = new Dictionary<string, Quaternion>();
            foreach (var sb in ami.humanDescription.skeleton) d[sb.name] = sb.rotation;
            return d.Count > 0 ? d : null;
        }

        /// <summary>Game clips placed by their feet (Root Transform Position Y based upon Feet) rather than the original height.</summary>
        public static bool GameFeetBased(string state) =>
            state == "sit" || state == "down" || state == "hurt" || state == "walker" || state == "stairsUp" || state == "stairsDown" || state == "clip_pickup";

        static IEnumerable<HumanBone> GameMap(Transform root)
        {
            var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            foreach (var (bone, human) in PersonClips.GameRigMap)
                if (names.Contains(bone)) yield return RocketboxImport.Bone(bone, human);
        }

        /// <summary>The game rig's I-pose straightened: arms out along ±X (the character's left is -X), legs straight down.</summary>
        static string StraightenGameRig(Transform root)
        {
            float maxArm = 0f, maxLeg = 0f;
            foreach (var s in new[] { "l", "r" })
            {
                var outDir = root.TransformDirection(s == "l" ? Vector3.left : Vector3.right);
                maxArm = Mathf.Max(maxArm, RocketboxImport.Aim(RocketboxImport.Find(root, s + "Arm"), RocketboxImport.Find(root, s + "Fore"), outDir));
                maxArm = Mathf.Max(maxArm, RocketboxImport.Aim(RocketboxImport.Find(root, s + "Fore"), RocketboxImport.Find(root, s + "Hand"), outDir));
                var down = root.TransformDirection(Vector3.down);
                maxLeg = Mathf.Max(maxLeg, RocketboxImport.Aim(RocketboxImport.Find(root, s + "Thigh"), RocketboxImport.Find(root, s + "Shin"), down));
                maxLeg = Mathf.Max(maxLeg, RocketboxImport.Aim(RocketboxImport.Find(root, s + "Shin"), RocketboxImport.Find(root, s + "Foot"), down));
            }
            return $"T-pose arms {maxArm:0}°, legs {maxLeg:0}°";
        }

        /// <summary>A clip of a file by state name (null when missing).</summary>
        public static AnimationClip Clip(string file, string state) =>
            AssetDatabase.LoadAllAssetsAtPath(PathOf(file)).OfType<AnimationClip>().FirstOrDefault(c => c.name == state);
    }
}
