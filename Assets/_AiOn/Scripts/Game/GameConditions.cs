using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>
    /// Situations that vary per seed (original game/state.js conditions()). They change where and when,
    /// never what the safe action is. Field names match the original save format.
    /// </summary>
    public class GameConditions
    {
        public class ClosureSet
        {
            [JsonProperty("dday")] public List<string> DDay = new();
            [JsonProperty("evac")] public List<string> Evac = new();
        }

        public class SoldOutSet
        {
            /// <summary>MART_SECTIONS ids sold out on the early (D-3) and late visits.</summary>
            [JsonProperty("early")] public List<string> Early = new();
            [JsonProperty("late")] public List<string> Late = new();
        }

        [JsonProperty("closures")] public ClosureSet Closures = new();
        [JsonProperty("eastGateClosed")] public bool EastGateClosed;
        [JsonProperty("accessOpensAt")] public string AccessOpensAt;
        /// <summary>Minute of D-day when the phone signal turns weak (after the 19:40 blackout).</summary>
        [JsonProperty("commsWeakAt")] public int CommsWeakAt;
        [JsonProperty("soldOut")] public SoldOutSet SoldOut = new();
        [JsonProperty("rumorTrue")] public bool RumorTrue;
        /// <summary>Minute of D+1 when the water truck comes.</summary>
        [JsonProperty("waterAt")] public int WaterAt;
        /// <summary>Minute of D+12 when the evacuation notice goes up.</summary>
        [JsonProperty("evacNoticeAt")] public int EvacNoticeAt;
        [JsonProperty("seoyeonAt")] public string SeoyeonAt;

        /// <summary>Fields from newer or older saves that this build does not know; kept on save.</summary>
        [JsonExtensionData] public IDictionary<string, JToken> Extra;

        // Every closure set conditions may pick. Each keeps home -> shelter reachable on foot.
        public static readonly string[][] DDayClosureSets =
        {
            new[] { "yeonmuCenter" },
            new[] { "yeonmuCenter", "danji" },
        };

        public static readonly string[][] EvacClosureSets =
        {
            new[] { "yeonmuCenter", "sijang" },
            new[] { "yeonmuCenter", "munhwaNorth" },
            new[] { "danji", "munhwaNorth" },
            new[] { "sijang", "munhwaSouth" },
            new[] { "danji", "sijang" },
        };

        /// <summary>Same draws in the same order as the original, so a seed gives the same situation.</summary>
        public static GameConditions For(uint seed)
        {
            var r = new Mulberry32(seed);
            T Pick<T>(IReadOnlyList<T> list) => list[(int)Math.Floor(r.Next() * list.Count)];

            var c = new GameConditions();
            c.Closures.DDay = new List<string>(Pick(DDayClosureSets));
            c.Closures.Evac = new List<string>(Pick(EvacClosureSets));
            c.EastGateClosed = true;
            c.AccessOpensAt = "20:16";
            c.CommsWeakAt = 19 * 60 + 44 + (int)Math.Floor(r.Next() * 12);
            c.SoldOut.Early = r.Next() < 0.5 ? new List<string>() : new List<string> { "water" };
            c.SoldOut.Late = r.Next() < 0.5
                ? new List<string> { "water", "elec" }
                : new List<string> { "water", "elec", "food" };
            c.RumorTrue = false;
            c.WaterAt = 9 * 60 + 30 * (int)Math.Floor(r.Next() * 4);
            c.EvacNoticeAt = 7 * 60 + 15 * (int)Math.Floor(r.Next() * 5);
            c.SeoyeonAt = "cafe";
            return c;
        }
    }
}
