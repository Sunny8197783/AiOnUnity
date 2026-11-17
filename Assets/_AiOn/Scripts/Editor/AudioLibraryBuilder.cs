using System.Collections.Generic;
using System.Linq;
using AiOn.Story;
using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Builds Data/AudioLibrary.asset: the story's sound names mapped to the free clips in ThirdParty/Audio
    /// (Oculus Audio Pack 1 CC BY 4.0, sfx-kit CC0, Blanket CC0). Sounds the original synthesised (siren, fire, boom)
    /// have no recording here and stay silent until they are made.
    /// </summary>
    public static class AudioLibraryBuilder
    {
        const string Path = "Assets/_AiOn/Data/AudioLibrary.asset";

        static readonly Dictionary<string, string> Map = new()
        {
            ["ambience_indoor"] = "AMBHome_RoomTone_LOOP_01",
            ["ambience_city"] = "AMBUrbn_CityEuroDaytime_LOOP_01",
            ["ambience_night"] = "summer-night",
            ["ambience_blackout"] = "wind",
            ["ambience_office"] = "MACHHvac_HVAC_LOOP_01",
            ["ambience_mart"] = "CRWDConv_Restaurant_LOOP_01",
            ["ambience_shelter"] = "AMBInd_RoomToneMachineRoom_LOOP_01",
            ["rain"] = "RAIN_CityRain_LOOP_01",
            ["door"] = "DOORWood_FrontDoorOpen_OS_01",
            ["doorClose"] = "DOORWood_FrontDoorClose_OS_01",
            ["glassDoor"] = "DOORGlas_GlassDoorOpen_OS_01",
            ["knock"] = "DOORKnck_OfficeDoorKnock_OS_01",
            ["footstep"] = "FEETHmn_ShoeConcreteWalk_OS_01",
            ["land"] = "FEETHmn_ShoeConcreteLand_OS_01",
            ["phoneAlert"] = "notification-alert-clear",
            ["phoneMessage"] = "notification-message-in-clear",
            ["radio"] = "digital-static",
            ["radioTune"] = "digital-interference",
            ["click"] = "click-button-soft",
            ["hit"] = "notification-failure-thud",
            ["glass"] = "GLASBrk_WindowPaneSmallBreak_OS_01",
            ["powerDown"] = "digital-power-down",
            ["powerUp"] = "digital-power-up",
            ["page"] = "props-page-turn",
            ["zip"] = "props-zip",
            ["tape"] = "props-tape",
        };

        [MenuItem("AiOn/Build Audio Library")]
        public static AudioLibrary Build()
        {
            var lib = AssetDatabase.LoadAssetAtPath<AudioLibrary>(Path);
            if (lib == null) { lib = ScriptableObject.CreateInstance<AudioLibrary>(); AssetDatabase.CreateAsset(lib, Path); }
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_AiOn/ThirdParty/Audio" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .GroupBy(p => System.IO.Path.GetFileNameWithoutExtension(p))
                .ToDictionary(g => g.Key, g => g.First());
            lib.entries.Clear();
            var missing = new List<string>();
            foreach (var (name, file) in Map)
            {
                if (clips.TryGetValue(file, out var path)) lib.entries.Add(new AudioLibrary.Entry { name = name, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) });
                else missing.Add($"{name} ({file})");
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AiOn] Audio library: {lib.entries.Count} sounds{(missing.Count > 0 ? "; missing " + string.Join(", ", missing) : "")}");
            return lib;
        }
    }
}
