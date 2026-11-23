using System.IO;
using System.Linq;
using AiOn.People;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>Rocketbox casting table (People/RocketboxCast.cs) and the imported avatar assets.</summary>
    public class RocketboxCastTests
    {
        /// <summary>The presets the game actually builds: one per Art/People/person-&lt;preset&gt;.glb (what PeopleBuilder reads).</summary>
        static string[] GamePresets() =>
            Directory.GetFiles("Assets/_AiOn/Art/People", "person-*.glb", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring("person-".Length)).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        [Test]
        public void EveryGamePresetIsCastOnce()
        {
            var game = GamePresets();
            Assert.AreEqual(13, game.Length, "person-*.glb count");
            // the original's 13 people and the port's own (the CPR patient, no baked original)
            CollectionAssert.AreEquivalent(game.Concat(RocketboxCast.PortAdded), RocketboxCast.Presets.ToArray(), "cast table vs person-*.glb + port presets");
            Assert.AreEqual(game.Length + RocketboxCast.PortAdded.Length, RocketboxCast.Presets.Distinct().Count());
            foreach (var p in RocketboxCast.PortAdded) Assert.AreEqual(0f, RocketboxCast.ForPreset(p).OldHeight, p);
            foreach (var p in game) Assert.IsNotNull(RocketboxCast.ForPreset(p), p);
            Assert.IsNull(RocketboxCast.ForPreset("nobody"));
        }

        [Test]
        public void NullOrEmptyPresetFindsNothing()
        {
            // spares have no preset: a person without a cast entry must not silently become the first spare (the child)
            Assert.IsNull(RocketboxCast.ForPreset(null));
            Assert.IsNull(RocketboxCast.ForPreset(""));
            Assert.IsNull(RocketboxCast.ForAvatar(null));
            Assert.AreEqual(1f, RocketboxCast.ScaleFor(null));
            Assert.AreEqual(1f, RocketboxCast.ScaleFor("staff1"));
        }

        [Test]
        public void FourteenDistinctAvatarsAndNoUnusedOnes()
        {
            // no LFS: only avatars a preset uses are shipped (the unused spares were left out of the repository)
            Assert.AreEqual(14, RocketboxCast.All.Count);
            Assert.AreEqual(14, RocketboxCast.All.Select(e => e.Avatar).Distinct().Count());
            Assert.AreEqual(14, RocketboxCast.All.Select(e => e.Prefix).Distinct().Count());
            Assert.AreEqual(8, RocketboxCast.All.Count(e => e.Tier == RocketboxCast.Tier.Hero));
            Assert.AreEqual(6, RocketboxCast.All.Count(e => e.Tier == RocketboxCast.Tier.Crowd));
            Assert.AreEqual(0, RocketboxCast.All.Count(e => e.Tier == RocketboxCast.Tier.Spare || e.Preset == null));
            var folders = Directory.GetDirectories("Assets/_AiOn/Art/People/Rocketbox").Select(Path.GetFileName).Where(n => n != "Anim");
            CollectionAssert.AreEquivalent(RocketboxCast.All.Select(e => e.Avatar), folders, "every imported avatar folder is cast");
            Assert.AreEqual("Male_Adult_03", RocketboxCast.ForPreset(RocketboxCast.Patient).Avatar, "the grey-haired grandfather");
        }

        [Test]
        public void KeyRolesUseTheChosenAvatars()
        {
            Assert.AreEqual("Male_Adult_10", RocketboxCast.ForPreset("jeongmin").Avatar);
            Assert.AreEqual("Female_Adult_12", RocketboxCast.ForPreset("seoyeon").Avatar);
            Assert.AreEqual("Business_Female_03", RocketboxCast.ForPreset("clerk").Avatar);
            Assert.AreEqual("Business_Male_02", RocketboxCast.ForPreset("coworker").Avatar); // the office team lead in a suit
            Assert.AreEqual("Male_Adult_09", RocketboxCast.ForPreset("manager").Avatar);
            Assert.AreEqual(RocketboxCast.Tier.Crowd, RocketboxCast.ForAvatar("Business_Male_04").Tier);
            // the elder: an East Asian avatar aged in her maps; the two East Asian crowd women that replaced the Western ones
            Assert.AreEqual("Medical_Female_01", RocketboxCast.ForPreset("elder").Avatar);
            Assert.AreEqual("f152", RocketboxCast.ForAvatar("Medical_Female_01").Prefix);
            Assert.AreEqual(RocketboxCast.Tier.Hero, RocketboxCast.ForPreset("elder").Tier);
            Assert.AreEqual("Sports_Female_02", RocketboxCast.ForPreset("crowd4").Avatar);
            Assert.AreEqual("Female_Adult_03", RocketboxCast.ForPreset("crowd6").Avatar);
            Assert.AreEqual(RocketboxCast.Tier.Crowd, RocketboxCast.ForAvatar("Female_Adult_03").Tier);
            Assert.AreEqual(RocketboxCast.Tier.Crowd, RocketboxCast.ForAvatar("Sports_Female_02").Tier);
            Assert.AreSame(RocketboxCast.ForPreset("guide"), RocketboxCast.ForAvatar("Security_Male_01"));
        }

        [Test]
        public void OnlyHeroesKeepBlendshapes()
        {
            foreach (var e in RocketboxCast.All)
                Assert.AreEqual(e.Tier == RocketboxCast.Tier.Hero, e.Blendshapes, e.Avatar);
        }

        [Test]
        public void ScalesGiveSensibleHeights()
        {
            foreach (var e in RocketboxCast.All)
            {
                Assert.That(e.Height, Is.InRange(1.4f, 1.85f), e.Avatar);
                if (e.Preset == null) continue;
                float h = e.KoreanHeight;
                if (e.Preset == "elder") Assert.That(h, Is.InRange(1.50f, 1.60f), "elder is the shortest adult");
                else if (e.Female) Assert.That(h, Is.InRange(1.58f, 1.64f), e.Avatar);
                else Assert.That(h, Is.InRange(1.68f, 1.76f), e.Avatar);
            }
            Assert.AreEqual(1f, RocketboxCast.ForPreset(RocketboxCast.Patient).OldScale, "no original person to match");
        }

        [Test]
        public void OldScaleMatchesTheOriginalPeople()
        {
            // original baked person height / avatar height (prep report casting table)
            Assert.AreEqual(0.957f, RocketboxCast.ForPreset("jeongmin").OldScale, 0.001f);
            Assert.AreEqual(0.906f, RocketboxCast.ForPreset("coworker").OldScale, 0.001f);
            Assert.AreEqual(0.928f, RocketboxCast.ForPreset("manager").OldScale, 0.001f);
            Assert.AreEqual(0.945f, RocketboxCast.ForPreset("seoyeon").OldScale, 0.001f);
            Assert.AreEqual(0.941f, RocketboxCast.ForPreset("clerk").OldScale, 0.001f);
            Assert.AreEqual(0.895f, RocketboxCast.ForPreset("elder").OldScale, 0.001f);
            Assert.AreEqual(0.886f, RocketboxCast.ForPreset("crowd1").OldScale, 0.001f);
            Assert.AreEqual(1.032f, RocketboxCast.ForPreset("crowd4").OldScale, 0.001f);
            Assert.AreEqual(0.944f, RocketboxCast.ForPreset("crowd6").OldScale, 0.001f);
        }

        [Test]
        public void ScaleForTargetsAHeight()
        {
            Assert.AreEqual(RocketboxCast.ElderScale, RocketboxCast.ScaleFor("elder"), 1e-6f);
            Assert.AreEqual(1.70f, RocketboxCast.ScaleFor("jeongmin", 1.70f) * RocketboxCast.ForPreset("jeongmin").Height, 1e-4f);
            Assert.AreEqual(1f, RocketboxCast.ScaleFor("nobody"));
        }

        [Test]
        public void ImportedAvatarsAreHumanoidWithUrpMaterials()
        {
            foreach (var e in RocketboxCast.All)
            {
                var path = RocketboxCast.ModelPath(e.Avatar);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.Avatar>().FirstOrDefault();
                Assert.IsNotNull(avatar, e.Avatar + " avatar");
                Assert.IsTrue(avatar.isValid && avatar.isHuman, e.Avatar + " humanoid");
                var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(smr, e.Avatar);
                Assert.AreEqual(e.Blendshapes, smr.sharedMesh.blendShapeCount > 0, e.Avatar + " blendshapes");
                foreach (var m in smr.sharedMaterials)
                {
                    Assert.IsNotNull(m, e.Avatar + " material");
                    Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, m.name);
                    Assert.IsTrue(AssetDatabase.GetAssetPath(m).EndsWith(".mat"), m.name + " remapped to an external material");
                    Assert.AreEqual(0f, m.GetFloat("_Surface"), m.name + " opaque");
                    Assert.IsNotNull(m.GetTexture("_BaseMap"), m.name + " base map");
                    bool cutout = m.name.EndsWith("_opacity") || m.name.EndsWith("_glasses");
                    Assert.AreEqual(cutout, m.IsKeywordEnabled("_ALPHATEST_ON"), m.name + " alpha clip");
                    Assert.AreEqual(2f, m.GetFloat("_Cull"), m.name + " front faces only");
                }
            }
        }

        [Test]
        public void TextureBudgetPerTier()
        {
            // repo budget (no LFS): metallic/smoothness 512; crowd and spares 1024; heroes 2048 only for head colour
            // (and the body colour of the two people seen closest)
            foreach (var e in RocketboxCast.All)
            {
                var dir = RocketboxCast.Folder(e.Avatar) + "/Textures";
                var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
                Assert.IsNotEmpty(guids, dir);
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var name = Path.GetFileNameWithoutExtension(path);
                    // the source file's own size: the importer caps the imported texture at 2048, so an oversized
                    // source would otherwise pass
                    var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                    imp.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
                    int max = name.EndsWith("_metallicsmooth") ? 512
                        : e.Tier != RocketboxCast.Tier.Hero ? 1024
                        : name.EndsWith("_head_color") ? 2048
                        : name.EndsWith("_body_color") && (e.Preset == "jeongmin" || e.Preset == "seoyeon") ? 2048
                        : 1024;
                    Assert.LessOrEqual(Mathf.Max(srcW, srcH), max, path);
                }
            }
        }

        [Test]
        public void HairIsCutoutMatteWithoutEnvironmentReflections()
        {
            foreach (var e in RocketboxCast.All)
            {
                var hair = AssetDatabase.LoadAssetAtPath<Material>($"{RocketboxCast.Folder(e.Avatar)}/Materials/{e.Prefix}_opacity.mat");
                if (hair == null) continue; // the guard has a cap, no hair cards
                Assert.IsTrue(hair.IsKeywordEnabled("_ALPHATEST_ON"), hair.name);
                Assert.AreEqual(0.5f, hair.GetFloat("_Cutoff"), 0.11f, hair.name);
                Assert.LessOrEqual(hair.GetFloat("_Smoothness"), e.Preset == "elder" ? 0.26f : 0.31f, hair.name);
                Assert.IsTrue(hair.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), hair.name + " no sky sheen on the cards");
            }
        }
    }
}
