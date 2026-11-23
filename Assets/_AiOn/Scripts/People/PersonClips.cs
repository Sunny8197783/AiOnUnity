using System.Collections.Generic;

namespace AiOn.People
{
    /// <summary>
    /// Which clip plays each of the game's animation states on the Rocketbox people (pure data; the people builder and the
    /// tests read it). Two sources, both retargeted through Humanoid:
    /// <list type="bullet">
    /// <item>Rocketbox motion capture (MIT), merged offline into Art/People/Rocketbox/Anim/rb_{male,female}_loco.fbx (30 fps:
    /// walk, brisk, run, slow, and carry = the walk with the right arm of the hold-bag idle) and rb_{male,female}_pose.fbx
    /// (15 fps, long takes cut at a clean loop point: idle, talk, phone, wave, point, sitChair, crouch, knock); armature
    /// only, body bones.</item>
    /// <item>The game's own baked clips (Art/People/person-*.glb), exported offline as game_male / game_female / game_elder.fbx:
    /// the floor sit, the stair flights, take cover (down), air, the walker gait, hurt and the one-shots, which Rocketbox
    /// does not have.</item>
    /// </list>
    /// The elder walks slowly with the slow Rocketbox gait. Locomotion is one 1D blend on Speed (m/s) whose children sit at the
    /// game's gait speeds; each child's time scale is that speed over the clip's own ground speed so the feet do not slide.
    /// </summary>
    public static class PersonClips
    {
        public enum Set { Male, Female, Elder }

        /// <summary>One clip of a set: the state it plays, the FBX file (in <see cref="AnimFolder"/>) and take, and how it loops.</summary>
        public readonly struct Clip
        {
            public readonly string State, File, Take;
            public readonly bool Loop;
            /// <summary>The take moves over the ground (Rocketbox "xy" clips): root XZ stays root motion, which the game
            /// drops (the scripts move people), so the clip plays in place.</summary>
            public readonly bool Travels;

            public Clip(string state, string file, string take, bool loop, bool travels)
            {
                State = state; File = file; Take = take; Loop = loop; Travels = travels;
            }
        }

        public const string AnimFolder = "Assets/_AiOn/Art/People/Rocketbox/Anim";
        public const string Loco = "_loco", Pose = "_pose";
        public const string GameMale = "game_male", GameFemale = "game_female", GameElder = "game_elder";

        /// <summary>Rocketbox takes: take name → (male source file, female source file, travels = in the _loco file).</summary>
        public static readonly (string take, string male, string female, bool travels)[] RocketboxTakes =
        {
            ("idle",     "m_idle_neutral_01",              "f_idle_neutral_01",              false),
            ("walk",     "m_walk_neutral_02",              "f_walk_neutral_02",              true),
            ("brisk",    "m_walk_fast_02",                 "f_walk_fast_02",                 true),
            ("run",      "m_run_neutral",                  "f_run_neutral",                  true),
            ("slow",     "m_walk_slow_01",                 "f_walk_slow_01",                 true),
            ("talk",     "m_gestic_talk_neutral_01",       "f_gestic_talk_neutral_01",       false),
            ("phone",    "m_cell_phone_talk_01",           "f_cell_phone_textmessage",       false),
            ("wave",     "m_wave_01",                      "f_wave_01",                      false),
            ("point",    "m_gestic_presentation_right_01", "f_gestic_presentation_right_01", false),
            ("sitChair", "m_sit_chair_idle_neutral_01",    "f_sit_chair_idle_neutral_01",    false),
            ("crouch",   "m_crouch_idle",                  "f_crouch_idle",                  false),
            ("knock",    "m_knock_door",                   "f_knock_door",                   false),
            ("carry",    "m_walk_neutral_02",              "f_walk_neutral_02",              true),
        };

        /// <summary>The right arm of this idle replaces the walk's in the carry take (a hand at the side, holding).</summary>
        public static readonly (string male, string female) CarryArm = ("m_hold_bag_idle", "f_hold_bag_idle_01");

        /// <summary>Takes kept from the game's own clips (the glb action names).</summary>
        public static readonly string[] GameTakes =
        {
            "sit", "stairsUp", "stairsDown", "down", "air", "hurt", "walker",
            "clip_pickup", "clip_vault", "clip_climb", "clip_land", "clip_throw", "clip_hurt",
        };

        /// <summary>One-shot clips on the Action layer (PersonAnimator.Play "pickup" → state "clip_pickup").</summary>
        public static readonly string[] OneShots = { "pickup", "throw", "vault", "climb", "land", "hurt" };

        /// <summary>The game person whose baked clips a set retargets.</summary>
        public static string GameSource(Set s) => s switch { Set.Female => "seoyeon", Set.Elder => "elder", _ => "jeongmin" };

        /// <summary>The Rocketbox file of a set: rb_male_loco, rb_female_pose...</summary>
        public static string RbFile(Set s, bool travels) => (s == Set.Male ? "rb_male" : "rb_female") + (travels ? Loco : Pose);
        /// <summary>Whether a Rocketbox take travels (lives in the _loco file); false for unknown takes.</summary>
        public static bool Travels(string take)
        {
            foreach (var t in RocketboxTakes) if (t.take == take) return t.travels;
            return false;
        }
        /// <summary>Every FBX of a set (the two Rocketbox files and the game's clips).</summary>
        public static IEnumerable<string> Files(Set s) { yield return RbFile(s, true); yield return RbFile(s, false); yield return GameFile(s); }
        public static string GameFile(Set s) => s switch { Set.Female => GameFemale, Set.Elder => GameElder, _ => GameMale };

        /// <summary>The clip set of a cast row: the elder has her own, otherwise by the avatar's sex.</summary>
        public static Set SetOf(RocketboxCast.Entry e) =>
            e == null ? Set.Male : e.Preset == "elder" ? Set.Elder : e.Female ? Set.Female : Set.Male;

        /// <summary>Every state of a set and its clip (locomotion children are separate: <see cref="Locomotion"/>).</summary>
        public static IEnumerable<Clip> States(Set s)
        {
            string pose = RbFile(s, false), loco = RbFile(s, true), game = GameFile(s);
            foreach (var t in new[] { "idle", "talk", "phone", "wave", "point", "sitChair", "crouch" })
                yield return new Clip(t, pose, t, true, false);
            yield return new Clip("knock", pose, "knock", false, false);
            yield return new Clip("carry", loco, "carry", true, true);
            foreach (var t in new[] { "sit", "stairsUp", "stairsDown", "air", "hurt", "walker" })
                yield return new Clip(t, game, t, true, false);
            yield return new Clip("down", game, "down", false, false);
            foreach (var o in OneShots) yield return new Clip("clip_" + o, game, "clip_" + o, false, false);
        }

        /// <summary>A child of the locomotion blend: the take, the Speed it sits at, and its time scale = speed / clip speed.</summary>
        public readonly struct Child
        {
            public readonly string Take;
            public readonly float Threshold, TimeScale;
            public Child(string take, float threshold, float timeScale) { Take = take; Threshold = threshold; TimeScale = timeScale; }
        }

        /// <summary>Speed below which the slowest child would be played slower than half its speed: the low end of the tree.</summary>
        public const float CreepSpeed = 0.5f;

        /// <summary>
        /// The locomotion children for measured clip ground speeds (m/s, at the avatar's own size). The game asks for walk
        /// 1.3, brisk 2.1 and run 3.9 (<see cref="PersonAnimator"/>); each gets the clip of that gait played at speed/clipSpeed.
        /// A creeping child (the walk at half speed or the creep speed) keeps slow steps from sliding. The elder's slow pace is
        /// the slow gait at its own speed (and half of it below); from walk speed up she uses the common gaits. Unmeasured (0)
        /// speeds play at time scale 1.
        /// </summary>
        public static List<Child> Locomotion(Set s, float walkSpeed, float briskSpeed, float runSpeed, float slowSpeed)
        {
            var list = new List<Child>();
            static float Ts(float want, float have) => have > 0.05f ? want / have : 1f;
            if (s == Set.Elder && slowSpeed > 0.05f)
            {
                // the elder's own pace is the slow gait at its natural speed; asked to keep up, she walks normally
                float c = slowSpeed * 0.5f;
                list.Add(new Child("slow", c, 0.5f));
                list.Add(new Child("slow", slowSpeed, 1f));
            }
            else
            {
                float creep = walkSpeed > 0.05f ? System.Math.Max(CreepSpeed, walkSpeed * 0.5f) : CreepSpeed;
                list.Add(new Child("walk", creep, Ts(creep, walkSpeed)));
            }
            list.Add(new Child("walk", PersonAnimator.WalkSpeed, Ts(PersonAnimator.WalkSpeed, walkSpeed)));
            list.Add(new Child("brisk", PersonAnimator.BriskSpeed, Ts(PersonAnimator.BriskSpeed, briskSpeed)));
            list.Add(new Child("run", PersonAnimator.RunSpeed, Ts(PersonAnimator.RunSpeed, runSpeed)));
            return list;
        }

        /// <summary>Float parameter every controller has (default 1): the Locomotion and carry states play this much faster.
        /// The builder measures each prefab's real foot speed (its avatar at its Korean scale) and stores the correction
        /// in <see cref="PersonAnimator.gaitScale"/>, since one controller serves people of different sizes.</summary>
        public const string GaitScaleParam = "GaitScale";

        /// <summary>The carry state's blend children (the walk with the hold-bag arm): slow, walk, brisk and the player's
        /// 3 m/s walking pace, each at speed / clip speed so the feet keep their contact (0: unmeasured, time scale 1).</summary>
        public static List<Child> Carry(float carrySpeed)
        {
            static float Ts(float want, float have) => have > 0.05f ? want / have : 1f;
            var list = new List<Child>();
            foreach (var v in new[] { CreepSpeed, PersonAnimator.WalkSpeed, PersonAnimator.BriskSpeed, CarryFast })
                list.Add(new Child("carry", v, Ts(v, carrySpeed)));
            return list;
        }

        /// <summary>The fastest carry child: the player's walking speed (PlayerController.walkSpeed).</summary>
        public const float CarryFast = 3.0f;

        // ---------------------------------------------------------------- arm poses

        /// <summary>The override layer (index 2, after "Action") that holds an arm pose: both arms and the fingers, under an
        /// avatar mask, so the legs and the body keep their clip. Its states are <see cref="ArmsNone"/> (empty: nothing
        /// changes) and one static pose per entry of <see cref="ArmPoses"/>.</summary>
        public const string ArmsLayerName = "Arms";
        public const int ArmsLayer = 2;
        public const string ArmsNone = "none";
        /// <summary>Hands on a cart handle; resting on the thighs while sitting on the floor; leaning back on the hands.</summary>
        public const string ArmsCart = "cart", ArmsSitKnees = "sitKnees", ArmsSitBack = "sitBack";
        public static readonly string[] ArmPoses = { ArmsCart, ArmsSitKnees, ArmsSitBack };

        /// <summary>
        /// The arm pose for a base state: a person pushing a cart (<paramref name="hold"/> "cart") has both hands on its
        /// handle while standing or walking; on the floor ("sit") the hands rest on the thighs, or (the other half of a
        /// crowd, by cycle phase) the person leans back on them, so a hall of seated people is not one repeated pose.
        /// Anything else leaves the clip's own arms (<see cref="ArmsNone"/>).
        /// </summary>
        public static string ArmsFor(string state, float phase, string hold)
        {
            if (hold == ArmsCart && (state == "Locomotion" || state == "idle")) return ArmsCart;
            if (state == "sit") return phase < 0.5f ? ArmsSitKnees : ArmsSitBack;
            return ArmsNone;
        }

        /// <summary>Humanoid map of the game's 18-bone rig (root stays unmapped: the hips carry the motion).</summary>
        public static readonly (string bone, string human)[] GameRigMap =
        {
            ("pelvis", "Hips"), ("spine", "Spine"), ("chest", "Chest"), ("neck", "Neck"), ("head", "Head"),
            ("lArm", "LeftUpperArm"), ("lFore", "LeftLowerArm"), ("lHand", "LeftHand"),
            ("rArm", "RightUpperArm"), ("rFore", "RightLowerArm"), ("rHand", "RightHand"),
            ("lThigh", "LeftUpperLeg"), ("lShin", "LeftLowerLeg"), ("lFoot", "LeftFoot"),
            ("rThigh", "RightUpperLeg"), ("rShin", "RightLowerLeg"), ("rFoot", "RightFoot"),
        };
    }
}
