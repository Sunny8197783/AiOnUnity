using System.Collections.Generic;
using AiOn.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.Game
{
    /// <summary>
    /// The running game: the rules, the current GameState, the verified facts and the level's world state
    /// (grid power). One per scene for now; the chapter director will own it later.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameSession : MonoBehaviour
    {
        public ItemDatabase items;
        [Tooltip("Data/Source/facts.json: the original's verified safety facts.")]
        public TextAsset factsJson;
        [Tooltip("0 = a random seed.")]
        public long seed;
        [Tooltip("Grid power in this level (the blackout turns it off).")]
        public bool power = true;

        public GameRules Rules { get; private set; }
        public GameState State { get; private set; }

        readonly Dictionary<string, string> _facts = new();

        public static GameSession Current { get; private set; }

        void Awake()
        {
            Current = this;
            Rules = new GameRules(items);
            State = Rules.CreateGame(seed != 0 ? seed : (long?)null);
            LoadFacts(factsJson ? factsJson.text : null, _facts);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        /// <summary>Text of a verified fact, or null.</summary>
        public string Fact(string id) => id != null && _facts.TryGetValue(id, out var t) ? t : null;

        public static void LoadFacts(string json, Dictionary<string, string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(json)) return;
            foreach (var f in JObject.Parse(json)["facts"] ?? new JArray())
                into[(string)f["id"]] = (string)f["text"];
        }

        /// <summary>For tests and tools: a session without a scene object.</summary>
        public static (GameRules rules, GameState state, Dictionary<string, string> facts) CreateDetached(ItemDatabase db, string factsJsonText, long seed)
        {
            var rules = new GameRules(db);
            var facts = new Dictionary<string, string>();
            LoadFacts(factsJsonText, facts);
            return (rules, rules.CreateGame(seed), facts);
        }
    }
}
