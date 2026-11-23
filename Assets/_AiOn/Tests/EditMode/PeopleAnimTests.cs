using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.People;
using AiOn.Story;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AiOn.Tests
{
    /// <summary>
    /// The Rocketbox people's animation: the clip table (PersonClips), face timing (FaceMotion), prop grips (PersonRig),
    /// the echo ghost's view rules, and the built assets (animation FBX sizes, controllers with the game's state names,
    /// prefabs on the cast avatars).
    /// </summary>
    public class PeopleAnimTests
    {
        /// <summary>Every animation name the game asks a person for (StoryContract.IStoryPerson, PlayerBody, crowds).</summary>
        static readonly string[] GameAnims =
        {
            "idle", "carry", "walker", "sit", "sitChair", "talk", "phone", "wave", "point", "crouch", "stairsUp", "stairsDown",
            "hurt", "down", "air",
        };

        static readonly PersonClips.Set[] Sets = { PersonClips.Set.Male, PersonClips.Set.Female, PersonClips.Set.Elder };

        // ---------------------------------------------------------------- clip table

        [Test]
        public void EverySetPlaysEveryGameState()
        {
            foreach (var s in Sets)
            {
                var states = PersonClips.States(s).Select(c => c.State).ToList();
                foreach (var a in GameAnims) CollectionAssert.Contains(states, a, $"{s}: {a}");
                foreach (var o in PersonClips.OneShots) CollectionAssert.Contains(states, "clip_" + o, $"{s}: one-shot {o}");
                Assert.AreEqual(states.Count, states.Distinct().Count(), $"{s}: duplicate states");
                CollectionAssert.DoesNotContain(states, "walk", "gaits live in the Locomotion blend");
            }
        }

        [Test]
        public void ClipSourcesFollowTheReport()
        {
            // Rocketbox motion capture where it looks better; the game's own clips where Rocketbox has none
            foreach (var s in Sets)
            {
                var by = PersonClips.States(s).ToDictionary(c => c.State);
                foreach (var rb in new[] { "idle", "talk", "phone", "wave", "point", "sitChair", "crouch", "knock", "carry" })
                    StringAssert.StartsWith("rb_", by[rb].File, $"{s} {rb}");
                foreach (var g in new[] { "sit", "stairsUp", "stairsDown", "down", "air", "walker", "hurt" })
                    StringAssert.StartsWith("game_", by[g].File, $"{s} {g}");
                Assert.IsTrue(by["carry"].Travels, "carry is a walk");
                Assert.IsFalse(by["down"].Loop, "take cover plays once");
                Assert.IsFalse(by["clip_pickup"].Loop);
            }
            Assert.AreEqual("rb_male_loco", PersonClips.RbFile(PersonClips.Set.Male, true));
            Assert.AreEqual("rb_female_pose", PersonClips.RbFile(PersonClips.Set.Elder, false), "the elder uses the female takes");
            Assert.AreEqual("game_elder", PersonClips.GameFile(PersonClips.Set.Elder));
            Assert.IsTrue(PersonClips.Travels("run"));
            Assert.IsFalse(PersonClips.Travels("idle"));
            Assert.IsFalse(PersonClips.Travels("nothing"));
        }

        [Test]
        public void SetOfCastRows()
        {
            Assert.AreEqual(PersonClips.Set.Elder, PersonClips.SetOf(RocketboxCast.ForPreset("elder")));
            Assert.AreEqual(PersonClips.Set.Female, PersonClips.SetOf(RocketboxCast.ForPreset("seoyeon")));
            Assert.AreEqual(PersonClips.Set.Male, PersonClips.SetOf(RocketboxCast.ForPreset("jeongmin")));
            Assert.AreEqual(PersonClips.Set.Male, PersonClips.SetOf(null));
            foreach (var e in RocketboxCast.All.Where(e => e.Preset != null && e.Preset != "elder"))
                Assert.AreEqual(e.Female ? PersonClips.Set.Female : PersonClips.Set.Male, PersonClips.SetOf(e), e.Preset);
        }

        [Test]
        public void LocomotionChildrenMatchTheGaitSpeeds()
        {
            const float walk = 1.35f, brisk = 2.0f, run = 3.07f, slow = 0.82f;
            var m = PersonClips.Locomotion(PersonClips.Set.Male, walk, brisk, run, slow);
            // thresholds rise; the game's gait speeds are on the tree; every child plays at speed / clip speed (no sliding)
            for (int i = 1; i < m.Count; i++) Assert.Greater(m[i].Threshold, m[i - 1].Threshold);
            var at = m.ToDictionary(c => c.Threshold);
            Assert.AreEqual(PersonAnimator.WalkSpeed / walk, at[PersonAnimator.WalkSpeed].TimeScale, 1e-4);
            Assert.AreEqual(PersonAnimator.BriskSpeed / brisk, at[PersonAnimator.BriskSpeed].TimeScale, 1e-4);
            Assert.AreEqual(PersonAnimator.RunSpeed / run, at[PersonAnimator.RunSpeed].TimeScale, 1e-4);
            Assert.AreEqual("walk", m[0].Take);
            Assert.AreEqual(m[0].Threshold / walk, m[0].TimeScale, 1e-4, "the creeping child is the walk slowed to its speed");
            Assert.GreaterOrEqual(m[0].Threshold, PersonClips.CreepSpeed);

            var e = PersonClips.Locomotion(PersonClips.Set.Elder, walk, brisk, run, slow);
            Assert.AreEqual("slow", e[0].Take);
            Assert.AreEqual("slow", e[1].Take);
            Assert.AreEqual(slow, e[1].Threshold, 1e-4, "her own pace at its natural speed");
            Assert.AreEqual(1f, e[1].TimeScale, 1e-4);
            Assert.IsTrue(e.Any(c => c.Take == "walk" && Mathf.Approximately(c.Threshold, PersonAnimator.WalkSpeed)));

            var u = PersonClips.Locomotion(PersonClips.Set.Female, 0, 0, 0, 0);
            Assert.IsTrue(u.All(c => c.TimeScale == 1f), "unmeasured clips play at their own speed");
        }

        [Test]
        public void CarryBlendKeepsTheFeetOnTheGroundUpToThePlayersPace()
        {
            const float clip = 1.36f;
            var c = PersonClips.Carry(clip);
            for (int i = 1; i < c.Count; i++) Assert.Greater(c[i].Threshold, c[i - 1].Threshold);
            Assert.IsTrue(c.All(x => x.Take == "carry"));
            foreach (var x in c) Assert.AreEqual(x.Threshold / clip, x.TimeScale, 1e-4, $"carry at {x.Threshold} m/s");
            Assert.AreEqual(3.0f, c[c.Count - 1].Threshold, 1e-4, "the player walks at 3 m/s with the water");
            Assert.IsTrue(c.Any(x => Mathf.Approximately(x.Threshold, PersonAnimator.WalkSpeed)));
            Assert.IsTrue(PersonClips.Carry(0).All(x => x.TimeScale == 1f));
            Assert.IsTrue(PersonAnimator.TakesSpeed("carry") && PersonAnimator.TakesSpeed("walk") && !PersonAnimator.TakesSpeed("sit"));
        }

        [Test]
        public void ArmPosesFollowTheStateAndWhatTheHandsHold()
        {
            Assert.AreEqual(PersonClips.ArmsCart, PersonClips.ArmsFor("Locomotion", 0.2f, PersonClips.ArmsCart));
            Assert.AreEqual(PersonClips.ArmsCart, PersonClips.ArmsFor("idle", 0.9f, PersonClips.ArmsCart));
            Assert.AreEqual(PersonClips.ArmsNone, PersonClips.ArmsFor("talk", 0.2f, PersonClips.ArmsCart), "talking lets go of the handle");
            Assert.AreEqual(PersonClips.ArmsNone, PersonClips.ArmsFor("Locomotion", 0.2f, null));
            Assert.AreEqual(PersonClips.ArmsSitKnees, PersonClips.ArmsFor("sit", 0.2f, null), "hands resting on the thighs");
            Assert.AreEqual(PersonClips.ArmsSitBack, PersonClips.ArmsFor("sit", 0.7f, null), "the other half leans back on the hands");
            Assert.AreEqual(PersonClips.ArmsNone, PersonClips.ArmsFor("sitChair", 0.2f, null));
            CollectionAssert.AreEquivalent(new[] { PersonClips.ArmsCart, PersonClips.ArmsSitKnees, PersonClips.ArmsSitBack }, PersonClips.ArmPoses);
        }

        [Test]
        public void TalkingMouthLastsAboutAsLongAsTheLine()
        {
            Assert.AreEqual(0f, TalkFacing.SpeakSecondsFor(null));
            Assert.AreEqual(0f, TalkFacing.SpeakSecondsFor("…"), "nothing spoken");
            Assert.AreEqual(1f, TalkFacing.SpeakSecondsFor("네."), "short lines still move the mouth a moment");
            float a = TalkFacing.SpeakSecondsFor("정민 씨, 물은 챙기셨어요?");
            float b = TalkFacing.SpeakSecondsFor("정민 씨, 물은 챙기셨어요? 관리사무소에서 생수를 나눠 준대요. 가실 때 같이 가요.");
            Assert.That(a, Is.InRange(1.5f, 3.5f));
            Assert.Greater(b, a, "a longer page talks longer");
            Assert.LessOrEqual(TalkFacing.SpeakSecondsFor(new string('가', 200)), 7f);
        }

        [Test]
        public void PersonPromptSitsBelowTheFace()
        {
            Assert.AreEqual(-220f, AiOn.UI.HudMotion.NpcPromptY(null));
            Assert.AreEqual(-150f, AiOn.UI.HudMotion.NpcPromptY(100f), "feet high on screen: still well under the crosshair");
            Assert.AreEqual(-330f, AiOn.UI.HudMotion.NpcPromptY(-900f), "feet off the bottom: kept on screen");
            Assert.AreEqual(-234f, AiOn.UI.HudMotion.NpcPromptY(-200f), 1e-3f);
        }

        [Test]
        public void GameRigMapCoversTheRequiredHumanBones()
        {
            var humans = PersonClips.GameRigMap.Select(b => b.human).ToList();
            foreach (var req in new[] { "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
                                        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" })
                CollectionAssert.Contains(humans, req);
            Assert.AreEqual(humans.Count, humans.Distinct().Count());
            Assert.IsFalse(PersonClips.GameRigMap.Any(b => b.bone == "root"), "the root bone stays unmapped");
        }

        // ---------------------------------------------------------------- face

        [Test]
        public void BlinkClosesAndOpens()
        {
            Assert.AreEqual(0f, FaceMotion.Blink(0f));
            Assert.AreEqual(0f, FaceMotion.Blink(-1f));
            Assert.AreEqual(0f, FaceMotion.Blink(FaceMotion.BlinkLength + 0.01f));
            Assert.AreEqual(1f, FaceMotion.Blink(FaceMotion.BlinkClose + FaceMotion.BlinkHold * 0.5f));
            Assert.Less(FaceMotion.Blink(FaceMotion.BlinkClose * 0.3f), FaceMotion.Blink(FaceMotion.BlinkClose * 0.8f), "closing");
            float o1 = FaceMotion.Blink(FaceMotion.BlinkClose + FaceMotion.BlinkHold + 0.02f), o2 = FaceMotion.Blink(FaceMotion.BlinkLength - 0.01f);
            Assert.Greater(o1, o2, "opening");
            Assert.Less(FaceMotion.BlinkLength, 0.25f, "a blink is quick");
            Assert.AreEqual(FaceMotion.BlinkMin, FaceMotion.NextBlink(0f));
            Assert.AreEqual(FaceMotion.BlinkMax, FaceMotion.NextBlink(1f));
            Assert.AreEqual(FaceMotion.BlinkMax, FaceMotion.NextBlink(7f), "clamped");
        }

        [Test]
        public void MouthStaysInRangeAndMoves()
        {
            float min = 1, max = 0;
            var shapes = new HashSet<int>();
            for (float t = 0; t < 4f; t += 0.01f)
            {
                float v = FaceMotion.Mouth(t, 0.37f);
                min = Mathf.Min(min, v); max = Mathf.Max(max, v);
                int s = FaceMotion.Shape(t, 0.37f, 5);
                Assert.That(s, Is.InRange(0, 4));
                shapes.Add(s);
            }
            Assert.GreaterOrEqual(min, 0.08f - 1e-5f);
            Assert.LessOrEqual(max, 0.85f + 1e-5f);
            Assert.Greater(max - min, 0.4f, "the jaw really moves");
            Assert.Greater(shapes.Count, 2, "several mouth shapes");
            Assert.AreEqual(-1, FaceMotion.Shape(1f, 0f, 0));
        }

        [Test]
        public void LookWeightFadesBehind()
        {
            Assert.AreEqual(1f, FaceMotion.LookWeight(Vector3.forward, new Vector3(0.3f, 0.5f, 1f)));
            Assert.AreEqual(0f, FaceMotion.LookWeight(Vector3.forward, Vector3.back));
            float side = FaceMotion.LookWeight(Vector3.forward, Quaternion.Euler(0, 92, 0) * Vector3.forward);
            Assert.That(side, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(0f, FaceMotion.LookWeight(Vector3.forward, Vector3.zero));
        }

        // ---------------------------------------------------------------- grips

        [Test]
        public void GripOfAHangingHandIsUprightAndFacesForward()
        {
            // right hand hanging at the side, palm toward the body (-x): fingers down, index knuckle forward of the little one
            var (pos, rot) = PersonRig.GripFrame(new Vector3(0.2f, 0.85f, 0), new Vector3(0.2f, 0.75f, 0), new Vector3(0.2f, 0.75f, 0.03f), new Vector3(0.2f, 0.76f, -0.03f), true);
            Assert.Less(Quaternion.Angle(rot, Quaternion.identity), 8f);
            Assert.Less(pos.y, 0.85f); Assert.Greater(pos.y, 0.75f);
            Assert.Less(pos.x, 0.2f, "the palm surface is on the inner side of the right hand");
            Assert.AreEqual(Vector3.left, PersonRig.PalmNormal(true));
            Assert.AreEqual(Vector3.right, PersonRig.PalmNormal(false));
            var (posL, _) = PersonRig.GripFrame(new Vector3(-0.2f, 0.85f, 0), new Vector3(-0.2f, 0.75f, 0), new Vector3(-0.2f, 0.75f, 0.03f), new Vector3(-0.2f, 0.76f, -0.03f), false);
            Assert.Greater(posL.x, -0.2f, "left palm faces +x");
        }

        // ---------------------------------------------------------------- echo ghost

        [Test]
        public void GhostStepsOutOfTheLineOfSight()
        {
            var cam = new Vector3(0, 1.6f, 0); var fwd = Vector3.forward; var player = new Vector3(0, 0, 4f);
            Assert.AreEqual(0f, EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(0.1f, 0, 2f)), "between camera and player");
            Assert.AreEqual(1f, EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(3f, 0, 2f)), "well to the side");
            Assert.AreEqual(1f, EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(0, 0, -2f)), "behind the camera");
            Assert.AreEqual(1f, EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(0, 0, 9f)), "beyond the player");
            float half = EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(1f, 0, 2f));
            Assert.That(half, Is.GreaterThan(0f).And.LessThan(1f));
            // first person: the player stands at the camera; the line runs ahead along the view
            Assert.AreEqual(0f, EchoMarksView.GhostClearOfView(cam, fwd, new Vector3(0, 0, 0.1f), new Vector3(0.2f, 0, 2.5f)));
            Assert.AreEqual(1f, EchoMarksView.GhostClearOfView(cam, Vector3.right, new Vector3(0, 0, 0.1f), new Vector3(0.2f, 0, 2.5f)));
            // ...but a figure walking ahead down a corridor (past about 3 m) stays a faint silhouette that leads the way
            Assert.AreEqual(EchoMarksView.GhostLeadFloor, EchoMarksView.GhostClearOfView(cam, fwd, new Vector3(0, 0, 0.1f), new Vector3(0.1f, 0, 4f)), 1e-4f);
            Assert.That(EchoMarksView.GhostLeadFloor, Is.InRange(0.2f, 0.5f));
            Assert.AreEqual(0f, EchoMarksView.GhostClearOfView(cam, fwd, player, new Vector3(0.1f, 0, 3.6f)), "third person: no floor on the line to the player");
        }

        // ---------------------------------------------------------------- assets

        static string AnimPath(string file) => $"{PersonClips.AnimFolder}/{file}.fbx";

        [Test]
        public void ThePatientCanShutHisEyes()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_AiOn/Prefabs/People/{RocketboxCast.Patient}.prefab");
            Assert.IsNotNull(prefab);
            Assert.AreEqual(RocketboxCast.Patient, CprScene.Model);
            var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.GreaterOrEqual(smr.sharedMesh.GetBlendShapeIndex("AK_09_EyeBlinkLeft"), 0);
            Assert.GreaterOrEqual(smr.sharedMesh.GetBlendShapeIndex("AK_10_EyeBlinkRight"), 0);
            Assert.LessOrEqual(smr.sharedMesh.blendShapeCount, 2, "only the blinks: lean");
            Assert.IsNotNull(prefab.GetComponentInChildren<PersonFace>(true));
        }

        [Test]
        public void ArmPoseClipsAreSmallStaticPoses()
        {
            foreach (var pose in PersonClips.ArmPoses)
            {
                var p = $"{PersonClips.AnimFolder}/arms_{pose}.anim";
                Assert.IsTrue(File.Exists(p), p);
                Assert.Less(new FileInfo(p).Length, 120_000, p);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.AreEqual(58, bindings.Length, $"{pose}: 18 arm and 40 finger muscles");
                Assert.IsFalse(bindings.Any(b => b.propertyName.Contains("Leg") || b.propertyName.StartsWith("Root")), $"{pose}: arms only");
            }
        }

        [Test]
        public void AnimationFilesStayLean()
        {
            long total = 0;
            foreach (var f in Sets.SelectMany(PersonClips.Files).Distinct())
            {
                var p = AnimPath(f);
                Assert.IsTrue(File.Exists(p), p);
                long n = new FileInfo(p).Length;
                Assert.Less(n, 3_500_000, $"{f} is {n} bytes");
                total += n;
            }
            Assert.Less(total, 15_000_000, $"animation files {total} bytes in all (no LFS: keep them lean)");
        }

        [Test]
        public void EveryAnimationFileIsAValidHumanoidWithItsTakes()
        {
            foreach (var s in Sets)
                foreach (var c in PersonClips.States(s))
                {
                    var clips = AssetDatabase.LoadAllAssetsAtPath(AnimPath(c.File)).OfType<AnimationClip>().ToList();
                    var clip = clips.FirstOrDefault(x => x.name == c.State);
                    Assert.IsNotNull(clip, $"{c.File}: {c.State}");
                    Assert.IsTrue(clip.isHumanMotion, $"{c.File}: {c.State} is Humanoid");
                    var av = AssetDatabase.LoadAllAssetsAtPath(AnimPath(c.File)).OfType<UnityEngine.Avatar>().FirstOrDefault();
                    Assert.IsTrue(av && av.isValid && av.isHuman, c.File);
                }
            foreach (var t in new[] { "walk", "brisk", "run", "slow" })
                foreach (var f in new[] { "rb_male_loco", "rb_female_loco" })
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(AnimPath(f)).OfType<AnimationClip>().First(x => x.name == t);
                    Assert.Greater(new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude, 0.5f, $"{f} {t} keeps its ground speed (root motion, dropped in game)");
                    Assert.IsTrue(clip.isLooping, $"{f} {t} loops");
                }
        }

        [Test]
        public void EveryPresetPrefabIsItsCastAvatarWithTheGameStates()
        {
            foreach (var e in RocketboxCast.All.Where(x => x.Preset != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_AiOn/Prefabs/People/{e.Preset}.prefab");
                Assert.IsNotNull(prefab, e.Preset);
                var animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(animator, e.Preset);
                Assert.IsTrue(animator.avatar && animator.avatar.isHuman, $"{e.Preset}: humanoid avatar");
                StringAssert.Contains(e.Avatar, AssetDatabase.GetAssetPath(animator.avatar), $"{e.Preset}: cast avatar");
                Assert.AreEqual(e.KoreanScale, animator.transform.localScale.y, 1e-4, $"{e.Preset}: Korean-average scale");
                Assert.Less(Quaternion.Angle(animator.transform.localRotation, Quaternion.identity), 0.1f, $"{e.Preset}: no half turn (the FBX faces +z)");
                Assert.IsFalse(animator.applyRootMotion, e.Preset);
                var pa = animator.GetComponent<PersonAnimator>();
                Assert.IsNotNull(pa, e.Preset);
                Assert.AreEqual(e.Preset, pa.preset);
                Assert.AreEqual(e.Tier == RocketboxCast.Tier.Hero, animator.GetComponent<PersonFace>() != null, $"{e.Preset}: face only on heroes");
                foreach (var a in new[] { PersonRig.GripR, PersonRig.GripL, PersonRig.Back })
                    Assert.IsNotNull(PersonRig.Deep(prefab.transform, a), $"{e.Preset}: {a}");

                var ac = animator.runtimeAnimatorController as AnimatorController;
                Assert.IsNotNull(ac, e.Preset);
                var baseStates = ac.layers[0].stateMachine.states.Select(s => s.state.name).ToList();
                foreach (var g in GameAnims.Append("Locomotion")) CollectionAssert.Contains(baseStates, g, $"{e.Preset}: {g}");
                bool crowd = e.Tier == RocketboxCast.Tier.Crowd;
                Assert.AreEqual(!crowd, ac.layers[0].iKPass, $"{e.Preset}: look IK on heroes only");
                Assert.AreEqual(crowd, ac.name.EndsWith("_crowd"), $"{e.Preset}: crowd controller");
                foreach (var st in ac.layers[0].stateMachine.states)
                    if (crowd) Assert.IsFalse(st.state.iKOnFeet, $"{e.Preset}: {st.state.name} no foot IK in the crowd");
                var locoState = ac.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
                var loco = locoState.motion as BlendTree;
                Assert.IsNotNull(loco);
                Assert.AreEqual("Speed", loco.blendParameter);
                Assert.IsTrue(loco.children.All(c => c.motion), $"{e.Preset}: locomotion clips");
                Assert.IsTrue(locoState.speedParameterActive && locoState.speedParameter == PersonClips.GaitScaleParam, $"{e.Preset}: gait correction");
                var carry = ac.layers[0].stateMachine.states.First(s => s.state.name == "carry").state;
                Assert.IsTrue(carry.motion is BlendTree ct && ct.blendParameter == "Speed" && ct.children.Length >= 3, $"{e.Preset}: carry follows the speed");
                Assert.IsTrue(carry.speedParameterActive && carry.speedParameter == PersonClips.GaitScaleParam);
                Assert.AreEqual(1f, ac.parameters.First(p => p.name == PersonClips.GaitScaleParam).defaultFloat);
                // the walk on this body at this scale covers the ground it is asked to (measured by the builder): men 0.95
                // and women 0.93 of the clip actor's size need about 5 to 11 % faster cycles
                Assert.That(pa.gaitScale, Is.InRange(1.0f, 1.2f), $"{e.Preset}: gait scale {pa.gaitScale}");
                Assert.AreEqual(1f / animator.transform.localScale.y, pa.gaitScale, 0.04f, $"{e.Preset}: about 1 / scale");
                Assert.AreEqual("Action", ac.layers[1].name);
                var act = ac.layers[1].stateMachine.states.Select(s => s.state.name).ToList();
                foreach (var o in PersonClips.OneShots) CollectionAssert.Contains(act, "clip_" + o);
                var arms = ac.layers[PersonClips.ArmsLayer];
                Assert.AreEqual(PersonClips.ArmsLayerName, arms.name);
                Assert.IsNotNull(arms.avatarMask, "arms mask");
                Assert.IsTrue(arms.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm) && !arms.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg));
                var armStates = arms.stateMachine.states.Select(s => s.state).ToList();
                Assert.AreEqual(PersonClips.ArmsNone, arms.stateMachine.defaultState.name);
                Assert.IsNull(arms.stateMachine.defaultState.motion, "no pose: the clip's own arms");
                foreach (var pose in PersonClips.ArmPoses)
                    Assert.IsTrue(armStates.Any(s => s.name == pose && s.motion is AnimationClip c && c.isHumanMotion), $"{e.Preset}: arm pose {pose}");
            }
        }
    }
}
