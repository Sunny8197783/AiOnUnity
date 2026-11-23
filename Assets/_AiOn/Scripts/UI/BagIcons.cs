// The bag screen's item pictures (competition upgrade, minigame art): each item id to a Noto Emoji picture
// (Resources/Emoji/emoji_u<codepoint>.png, Apache-2.0, see ThirdParty/THIRD_PARTY.md), shown on the bag's pieces and
// beside each row, so the grid reads as things rather than coloured blocks with names. Pure map; UiKit.Emoji loads.
using System.Collections.Generic;
using UnityEngine;

namespace AiOn.UI
{
    public static class BagIcons
    {
        /// <summary>Item id to the emoji codepoint drawn for it (catalog items and the experience-mode aids).</summary>
        public static readonly IReadOnlyDictionary<string, int> Codepoints = new Dictionary<string, int>
        {
            ["water_2l"] = 0x1f4a7, ["water_500"] = 0x1f4a7, ["water_jug"] = 0x1faa3,
            ["rice_instant"] = 0x1f35a, ["canned"] = 0x1f96b, ["ramen"] = 0x1f35c, ["bars"] = 0x1f36a, ["old_bars"] = 0x1f36a,
            ["fridge_food"] = 0x1f961, ["cooked_meat"] = 0x1f356,
            ["radio"] = 0x1f4fb, ["batteries_aa"] = 0x1f50b, ["batteries_old"] = 0x1faab, ["powerbank"] = 0x1f50c,
            ["flashlight"] = 0x1f526, ["candles"] = 0x1f56f,
            ["first_aid"] = 0x1fa79, ["meds"] = 0x1f48a, ["masks"] = 0x1f637,
            ["wipes"] = 0x1f9f4, ["tissue"] = 0x1f9fb, ["trash_bags"] = 0x1f5d1, ["hygiene_kit"] = 0x1faa5, ["towel"] = 0x1f9fa,
            ["blanket"] = 0x1f6cc, ["raincoat"] = 0x1f9e5, ["clothes"] = 0x1f9e6,
            ["whistle"] = 0x1f4e3, ["tape"] = 0x1f9f0,
            ["gas_stove"] = 0x1f373, ["butane"] = 0x1f525,
            ["cash"] = 0x1f45b, ["contact_card"] = 0x1f4c7, ["doc_copies"] = 0x1f4c4, ["id_card"] = 0x1faaa,
            ["photo_album"] = 0x1f5bc,
            ["aid_chair_batt"] = 0x1f50b, ["aid_hearing_batt"] = 0x1f50b, ["aid_memo"] = 0x1f4dd, ["aid_glasses"] = 0x1f453,
        };

        /// <summary>The Resources path of an item's picture, or null when it has none.</summary>
        public static string PathOf(string id) => id != null && Codepoints.TryGetValue(id, out var cp) ? $"Emoji/emoji_u{cp:x}" : null;

        /// <summary>The item's picture (cached by UiKit.Emoji), or null.</summary>
        public static Sprite For(string id) => id != null && Codepoints.TryGetValue(id, out var cp) ? UiKit.Emoji(char.ConvertFromUtf32(cp)) : null;
    }
}
