using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AiOn.People;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Import rules and URP materials for the Microsoft Rocketbox avatars (MIT) in Art/People/Rocketbox/&lt;Avatar&gt;/.
    /// Layout: Model/&lt;Avatar&gt;.fbx (metres, faces +Z, Biped bones re-parented for Humanoid, hair cards already
    /// double-sided, card normals bent toward a head capsule) and Textures/&lt;prefix&gt;_&lt;slot&gt;_{color.jpg, normal.jpg, metallicsmooth.png} plus
    /// &lt;prefix&gt;_opacity_color.png (hair and lash cards, RGBA). Casting and heights: <see cref="RocketboxCast"/>.
    /// The FBX files are already turned to face +Z offline (the downloaded export faced -Z): a builder must not add a 180°
    /// turn for these models. Offline staging and its rules: Docs/COMPETITION_PLAN.md, 그래픽 사실화 "인물 가져오기".
    /// AiOn → People → Import Rocketbox applies the import settings, maps the Humanoid avatar by hand (Biped names,
    /// jaw left unmapped) with the arms and legs straightened into a T-pose, builds one URP Lit material per slot and
    /// remaps the FBX onto them.
    /// </summary>
    public class RocketboxImport : AssetPostprocessor
    {
        public const string Root = "Assets/_AiOn/Art/People/Rocketbox";
        const string MatFolder = "Materials";

        public override uint GetVersion() => 3;

        static string AvatarOf(string assetPath)
        {
            if (!assetPath.StartsWith(Root + "/")) return null;
            var rest = assetPath.Substring(Root.Length + 1);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : null;
        }

        void OnPreprocessModel()
        {
            var avatar = AvatarOf(assetPath);
            if (avatar == null || !assetPath.Contains("/Model/")) return;
            ApplyModelSettings((ModelImporter)assetImporter, RocketboxCast.ForAvatar(avatar));
        }

        void OnPreprocessTexture()
        {
            var avatar = AvatarOf(assetPath);
            if (avatar == null || !assetPath.Contains("/Textures/")) return;
            ApplyTextureSettings((TextureImporter)assetImporter, assetPath, RocketboxCast.ForAvatar(avatar));
        }

        // ---------------------------------------------------------------- import settings

        static void ApplyModelSettings(ModelImporter mi, RocketboxCast.Entry cast)
        {
            bool hero = cast != null && cast.Blendshapes;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.useFileUnits = true;          // "Convert Units"
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = hero;
            mi.importBlendShapeNormals = ModelImporterNormals.Calculate;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.skinWeights = ModelImporterSkinWeights.Standard; // 4 bones; the meshes never use more
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;  // props are parented to hand and chest bones
        }

        /// <summary>Map kind of a texture file by its name.</summary>
        public enum TexKind { Color, Normal, MetallicSmooth, Cutout }

        public static TexKind KindOf(string fileName)
        {
            var n = Path.GetFileNameWithoutExtension(fileName);
            if (n.EndsWith("_normal")) return TexKind.Normal;
            if (n.EndsWith("_metallicsmooth")) return TexKind.MetallicSmooth;
            if (n.Contains("opacity")) return TexKind.Cutout;
            return TexKind.Color;
        }

        static void ApplyTextureSettings(TextureImporter ti, string path, RocketboxCast.Entry cast)
        {
            bool hero = cast != null && cast.Tier == RocketboxCast.Tier.Hero;
            var kind = KindOf(path);
            ti.mipmapEnabled = true;
            ti.isReadable = false;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipMapsPreserveCoverage = false;
            switch (kind)
            {
                case TexKind.Normal:
                    ti.textureType = TextureImporterType.NormalMap; // OpenGL (+Y): no green flip
                    ti.flipGreenChannel = false;
                    break;
                case TexKind.MetallicSmooth:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = false;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = false;
                    break;
                case TexKind.Cutout:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = true;
                    ti.mipMapsPreserveCoverage = true; // hair and lashes keep their coverage in the distance
                    ti.alphaTestReferenceValue = 0.5f;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    break;
                default:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.None;
                    ti.alphaIsTransparency = false;
                    if (hero && path.Contains("_head_")) ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    break;
            }
        }

        // ---------------------------------------------------------------- menu

        [MenuItem("AiOn/People/Import Rocketbox")]
        public static void ImportAll()
        {
            var log = new StringBuilder();
            int ok = 0, n = 0;
            AssetDatabase.Refresh();
            foreach (var cast in RocketboxCast.All)
            {
                var fbx = RocketboxCast.ModelPath(cast.Avatar);
                if (AssetImporter.GetAtPath(fbx) is not ModelImporter) { log.AppendLine($"{cast.Avatar}: missing {fbx}"); continue; }
                n++;
                try
                {
                    var msg = ImportOne(cast);
                    log.AppendLine($"{cast.Avatar}: {msg}");
                    if (msg.StartsWith("ok")) ok++;
                }
                catch (Exception e) { log.AppendLine($"{cast.Avatar}: FAILED {e.Message}"); Debug.LogException(e); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Rocketbox import: {ok}/{n} avatars ok\n{log}");
        }

        /// <summary>Imports one avatar: settings, textures, Humanoid avatar in T-pose, materials. Returns "ok ..." or a problem.</summary>
        public static string ImportOne(RocketboxCast.Entry cast)
        {
            var fbx = RocketboxCast.ModelPath(cast.Avatar);
            var dir = RocketboxCast.Folder(cast.Avatar);
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir + "/Textures" }))
            {
                var tp = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
                ApplyTextureSettings(ti, tp, cast);
                ti.SaveAndReimport();
            }

            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            ApplyModelSettings(mi, cast);
            mi.SaveAndReimport();

            // Humanoid: explicit Biped map and a T-pose skeleton taken from a straightened instance of the model.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)Object.Instantiate(model);
            inst.name = model.name;
            string poseNote;
            try
            {
                poseNote = StraightenToTPose(inst.transform);
                var hd = mi.humanDescription;
                hd.human = HumanMap(inst.transform).ToArray();
                hd.skeleton = inst.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                }).ToArray();
                hd.hasTranslationDoF = false;
                mi.humanDescription = hd;
            }
            finally { Object.DestroyImmediate(inst); }
            mi.SaveAndReimport();

            int mats = BuildMaterials(cast, mi, fbx);
            mi.SaveAndReimport();

            var av = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<UnityEngine.Avatar>().FirstOrDefault();
            if (av == null) return "no avatar";
            if (!av.isValid || !av.isHuman) return $"avatar invalid (valid {av.isValid}, human {av.isHuman})";
            return $"ok humanoid, {mats} materials, {poseNote}";
        }

        // ---------------------------------------------------------------- humanoid

        /// <summary>Biped bone → Humanoid bone (Jaw deliberately unmapped: the face rig drives it).</summary>
        public static readonly (string bip, string human)[] BipedMap =
        {
            ("Bip01 Pelvis", "Hips"), ("Bip01 Spine", "Spine"), ("Bip01 Spine1", "Chest"), ("Bip01 Spine2", "UpperChest"),
            ("Bip01 Neck", "Neck"), ("Bip01 Head", "Head"), ("Bip01 LEye", "LeftEye"), ("Bip01 REye", "RightEye"),
            ("Bip01 L Clavicle", "LeftShoulder"), ("Bip01 L UpperArm", "LeftUpperArm"), ("Bip01 L Forearm", "LeftLowerArm"), ("Bip01 L Hand", "LeftHand"),
            ("Bip01 R Clavicle", "RightShoulder"), ("Bip01 R UpperArm", "RightUpperArm"), ("Bip01 R Forearm", "RightLowerArm"), ("Bip01 R Hand", "RightHand"),
            ("Bip01 L Thigh", "LeftUpperLeg"), ("Bip01 L Calf", "LeftLowerLeg"), ("Bip01 L Foot", "LeftFoot"), ("Bip01 L Toe0", "LeftToes"),
            ("Bip01 R Thigh", "RightUpperLeg"), ("Bip01 R Calf", "RightLowerLeg"), ("Bip01 R Foot", "RightFoot"), ("Bip01 R Toe0", "RightToes"),
        };

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] PhalanxNames = { "Proximal", "Intermediate", "Distal" };

        internal static IEnumerable<HumanBone> HumanMap(Transform root)
        {
            var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            foreach (var (bip, human) in BipedMap)
                if (names.Contains(bip)) yield return Bone(bip, human);
            foreach (var side in new[] { "L", "R" })
            {
                var sideName = side == "L" ? "Left" : "Right";
                for (int f = 0; f < 5; f++)
                    for (int p = 0; p < 3; p++)
                    {
                        var bip = p == 0 ? $"Bip01 {side} Finger{f}" : $"Bip01 {side} Finger{f}{p}";
                        if (names.Contains(bip)) yield return Bone(bip, $"{sideName} {FingerNames[f]} {PhalanxNames[p]}");
                    }
            }
        }

        internal static HumanBone Bone(string bone, string human)
        {
            var hb = new HumanBone { boneName = bone, humanName = human };
            hb.limit.useDefaultValues = true;
            return hb;
        }

        internal static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        internal static float Aim(Transform bone, Transform child, Vector3 dir)
        {
            if (!bone || !child) return 0f;
            var cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-8f) return 0f;
            float angle = Vector3.Angle(cur, dir);
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
            return angle;
        }

        /// <summary>
        /// "Enforce T-Pose" done by hand on an instance: arms out along ±X (the model faces +Z, so the character's left is
        /// -X), hands in line, legs straight down. Returns the largest correction in degrees.
        /// </summary>
        internal static string StraightenToTPose(Transform root)
        {
            float maxArm = 0f, maxLeg = 0f;
            foreach (var side in new[] { "L", "R" })
            {
                var outDir = root.TransformDirection(side == "L" ? Vector3.left : Vector3.right);
                var up = Find(root, $"Bip01 {side} UpperArm");
                var fore = Find(root, $"Bip01 {side} Forearm");
                var hand = Find(root, $"Bip01 {side} Hand");
                var mid = Find(root, $"Bip01 {side} Finger2");
                maxArm = Mathf.Max(maxArm, Aim(up, fore, outDir));
                maxArm = Mathf.Max(maxArm, Aim(fore, hand, outDir));
                maxArm = Mathf.Max(maxArm, Aim(hand, mid, outDir));
                var down = root.TransformDirection(Vector3.down);
                var thigh = Find(root, $"Bip01 {side} Thigh");
                var calf = Find(root, $"Bip01 {side} Calf");
                var foot = Find(root, $"Bip01 {side} Foot");
                maxLeg = Mathf.Max(maxLeg, Aim(thigh, calf, down));
                maxLeg = Mathf.Max(maxLeg, Aim(calf, foot, down));
            }
            return $"T-pose arms {maxArm:0}°, legs {maxLeg:0}°";
        }

        // ---------------------------------------------------------------- materials

        static int BuildMaterials(RocketboxCast.Entry cast, ModelImporter mi, string fbx)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var dir = RocketboxCast.Folder(cast.Avatar);
            var matDir = dir + "/" + MatFolder;
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, MatFolder);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var slots = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Select(m => m.name).Distinct().ToList();
            // names already remapped show up as the external material's name, which is the same slot name
            foreach (var slot in slots)
            {
                var path = $"{matDir}/{slot}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(lit) { name = slot }; AssetDatabase.CreateAsset(m, path); }
                Setup(m, lit, slot, dir + "/Textures/", cast.Preset == "elder");
                EditorUtility.SetDirty(m);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), m);
            }
            return slots.Count;
        }

        static Texture2D Tex(string texDir, string stem)
        {
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + stem + ext);
                if (t) return t;
            }
            return null;
        }

        /// <summary>Material slot kind by its name: hair/lash cards ("_opacity") and glasses clip at 0.5, never blend.</summary>
        public static bool IsCutoutSlot(string slot) => slot.EndsWith("_opacity") || slot.EndsWith("_glasses");

        /// <summary>
        /// Hair: smoothness 0.3 (the elder's grey hair 0.25 with a faint warm tint: it read as a blue-silver wig) and no
        /// environment reflections, which at grazing angles turned the large, flat Rocketbox cards into pale grey-blue
        /// plates; the lights' own highlights stay.
        /// </summary>
        public const float HairSmoothness = 0.3f, GreyHairSmoothness = 0.25f;

        public static void Setup(Material m, Shader lit, string slot, string texDir, bool greyHair = false)
        {
            bool cutout = IsCutoutSlot(slot);
            bool hair = slot.EndsWith("_opacity");
            var baseMap = hair ? Tex(texDir, slot + "_color") : Tex(texDir, slot + "_color") ?? Tex(texDir, slot + "_opacity_color");
            var normal = Tex(texDir, slot + "_normal");
            var ms = hair ? null : Tex(texDir, slot + "_metallicsmooth");

            m.shader = lit;
            m.SetTexture("_BaseMap", baseMap);
            m.SetTexture("_MainTex", baseMap);
            m.SetColor("_BaseColor", hair && greyHair ? new Color(1f, 0.97f, 0.93f) : Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", hair ? 0f : 1f);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MetallicGlossMap", ms);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness from the metallic map's alpha
            m.SetFloat("_Smoothness", ms ? 1f : hair ? (greyHair ? GreyHairSmoothness : HairSmoothness) : 0.3f);
            m.SetFloat("_WorkflowMode", 1f);             // metallic
            m.SetFloat("_Surface", 0f);                  // opaque, never transparent
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", (float)CullMode.Back);   // Render Face = Front; hair cards are double-sided geometry
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            SetKeyword(m, "_NORMALMAP", normal);
            SetKeyword(m, "_METALLICSPECGLOSSMAP", ms);
            SetKeyword(m, "_ALPHATEST_ON", cutout);
            SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", false);
            SetKeyword(m, "_ALPHAPREMULTIPLY_ON", false);
            SetKeyword(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", false);
            SetKeyword(m, "_ENVIRONMENTREFLECTIONS_OFF", hair);
            m.SetShaderPassEnabled("ShadowCaster", true);
            m.SetShaderPassEnabled("DepthOnly", true);
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }
    }
}
