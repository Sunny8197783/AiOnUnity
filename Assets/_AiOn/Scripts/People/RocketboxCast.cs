using System;
using System.Collections.Generic;

namespace AiOn.People
{
    /// <summary>
    /// Casting of the Microsoft Rocketbox avatars (MIT, Art/People/Rocketbox/&lt;Avatar&gt;/) onto the game's 13 person
    /// presets and the port's own <see cref="Patient"/>. Heights are the avatars' measured standing heights (metres, rest
    /// pose); OldHeight is the height of the original baked person (Art/People/person-&lt;preset&gt;.glb, 0 for the
    /// patient). Pure data: the editor importer and the people builder both read it. (Tier.Spare is kept for avatars
    /// imported ahead of a use; none ships now, to keep the repository lean.)
    /// </summary>
    public static class RocketboxCast
    {
        public enum Tier { Hero, Crowd, Spare }

        /// <summary>One casting row. Read-only: the table is shared by the importer, the builder and the tests.</summary>
        public sealed class Entry
        {
            public readonly string Preset;      // game preset ("jeongmin"...), null for spares
            public readonly string Avatar;      // Rocketbox folder / FBX name
            public readonly string Prefix;      // texture and material prefix (m024_body ...)
            public readonly Tier Tier;
            public readonly bool Female;
            public readonly float Height;       // avatar height, metres
            public readonly float OldHeight;    // original baked person's height (0 for spares)
            public readonly float KoreanScale;  // scale that brings the avatar to a Korean adult average for the role

            public Entry(string preset, string avatar, string prefix, Tier tier, bool female, float height, float oldHeight, float koreanScale)
            {
                Preset = preset; Avatar = avatar; Prefix = prefix; Tier = tier; Female = female;
                Height = height; OldHeight = oldHeight; KoreanScale = koreanScale;
            }

            /// <summary>Uniform scale that matches the original person's height (1 for spares).</summary>
            public float OldScale => OldHeight > 0 ? OldHeight / Height : 1f;
            /// <summary>Height after the Korean-average scale.</summary>
            public float KoreanHeight => Height * KoreanScale;
            /// <summary>Only heroes import facial blendshapes (visemes + ARKit) and 2048 head maps.</summary>
            public bool Blendshapes => Tier == Tier.Hero;
        }

        /// <summary>Korean average scale by build: men 0.95 (about 1.73 m), women 0.93 (about 1.61 m), the elder 0.90 (about 1.57 m).</summary>
        public const float MaleScale = 0.95f, FemaleScale = 0.93f, ElderScale = 0.90f;

        static Entry E(string preset, string avatar, string prefix, Tier tier, bool female, float h, float old, float k) =>
            new Entry(preset, avatar, prefix, tier, female, h, old, k);

        /// <summary>
        /// Heroes are the people the player sees up close, so they get the East Asian-looking avatars of the downloaded
        /// set (Male_Adult_10, Business_Male_02, Male_Adult_09, Female_Adult_12, Business_Female_03, Medical_Female_01);
        /// every iris is recoloured dark brown. 서연 is Female_Adult_12 because her POV chapter shows her from behind and that
        /// hair (ponytail) holds up there; Business_Female_03's nape cards end in a hard edge, so she is the clerk, seen
        /// across the counter. The office team lead (윤 팀장, the prologue's first close-up) wears Business_Male_02's suit;
        /// the apartment's 관리소장 is Male_Adult_09 in a plain dark tee (the graphic and the forearm tattoo painted out of
        /// his body map). No older East Asian woman exists in the Rocketbox library, so the 304호 elder is Medical_Female_01
        /// aged in her maps (grey hair in a bun, soft wrinkles: Tools/people-anim/age_elder.py) and her white scrubs
        /// repainted as a flower-print blouse, dark trousers and plain shoes (dress_elder.py); her face shape is still the
        /// avatar's own. Female_Adult_03 aged the same way read younger (long loose hair, side bangs), so she is a crowd
        /// woman with Sports_Female_02. The Western-looking avatars stay in the crowd, seen from a distance.
        /// </summary>
        public static readonly IReadOnlyList<Entry> All = new[]
        {
            E("jeongmin", "Male_Adult_10",      "m024",  Tier.Hero,  false, 1.831f, 1.752f, MaleScale),
            E("coworker", "Business_Male_02",   "m008",  Tier.Hero,  false, 1.823f, 1.651f, MaleScale),
            E("manager",  "Male_Adult_09",      "m017",  Tier.Hero,  false, 1.822f, 1.691f, MaleScale),
            E("seoyeon",  "Female_Adult_12",    "f012",  Tier.Hero,  true,  1.727f, 1.632f, FemaleScale),
            E("clerk",    "Business_Female_03", "f016",  Tier.Hero,  true,  1.743f, 1.641f, FemaleScale),
            E("elder",    "Medical_Female_01",  "f152",  Tier.Hero,  true,  1.739f, 1.557f, ElderScale),
            E("guide",    "Security_Male_01",   "m171",  Tier.Hero,  false, 1.832f, 1.766f, MaleScale),
            E("crowd1",   "Business_Male_04",   "m015",  Tier.Crowd, false, 1.801f, 1.595f, MaleScale),
            E("crowd2",   "Female_Adult_08",    "f008",  Tier.Crowd, true,  1.735f, 1.759f, FemaleScale),
            E("crowd3",   "Male_Adult_01",      "m002",  Tier.Crowd, false, 1.824f, 1.621f, MaleScale),
            E("crowd4",   "Sports_Female_02",   "f013",  Tier.Crowd, true,  1.737f, 1.792f, FemaleScale),
            E("crowd5",   "Male_Adult_20",      "m027",  Tier.Crowd, false, 1.805f, 1.723f, MaleScale),
            E("crowd6",   "Female_Adult_03",    "f003",  Tier.Crowd, true,  1.739f, 1.641f, FemaleScale),
            E(Patient,    "Male_Adult_03",      "m004",  Tier.Hero,  false, 1.807f, 0f,     PatientScale),
        };

        /// <summary>
        /// Presets the port added (no original baked person): the grandfather who collapses in the shelter (CprScene). He
        /// is seen lying close to the camera with his eyes shut, so he is the grey-haired Male_Adult_03 with blendshapes
        /// (AK_09/AK_10 closed), a hero row without a 2048 head.
        /// </summary>
        public const string Patient = "patient";
        public static readonly string[] PortAdded = { Patient };
        /// <summary>The grandfather: a little shorter than the average man (about 1.70 m).</summary>
        public const float PatientScale = 0.94f;

        /// <summary>The 14 presets in table order (the original's 13 and the patient).</summary>
        public static IEnumerable<string> Presets
        {
            get { foreach (var e in All) if (e.Preset != null) yield return e.Preset; }
        }

        /// <summary>The row of a game preset; null for an unknown, empty or null preset (spares are never returned).</summary>
        public static Entry ForPreset(string preset)
        {
            if (string.IsNullOrEmpty(preset)) return null;
            foreach (var e in All) if (e.Preset == preset) return e;
            return null;
        }

        public static Entry ForAvatar(string avatar)
        {
            if (string.IsNullOrEmpty(avatar)) return null;
            foreach (var e in All) if (string.Equals(e.Avatar, avatar, StringComparison.Ordinal)) return e;
            return null;
        }

        /// <summary>Scale for a preset at a target height in metres (0 or less: the Korean-average scale); 1 for an unknown preset.</summary>
        public static float ScaleFor(string preset, float targetHeight = 0f)
        {
            var e = ForPreset(preset);
            if (e == null) return 1f;
            return targetHeight > 0f ? targetHeight / e.Height : e.KoreanScale;
        }

        /// <summary>Asset folder of an avatar (forward slashes, no trailing slash).</summary>
        public static string Folder(string avatar) => "Assets/_AiOn/Art/People/Rocketbox/" + avatar;
        public static string ModelPath(string avatar) => Folder(avatar) + "/Model/" + avatar + ".fbx";
    }
}
