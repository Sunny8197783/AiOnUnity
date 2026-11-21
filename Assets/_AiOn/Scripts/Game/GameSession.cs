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
        [Tooltip("Data/Source/facts-ext.json: sourced facts added after the port, merged after the original's (may be empty).")]
        public TextAsset factsExtJson;
        [Tooltip("0 = a random seed.")]
        public long seed;
        [Tooltip("Grid power in this level (the blackout turns it off).")]
        public bool power = true;

        public GameRules Rules { get; private set; }
        public GameState State { get; private set; }

        readonly Dictionary<string, string> _facts = new();
        Facts _factsData;
        TextAsset _factsFrom, _extFrom;

        /// <summary>
        /// facts.json with facts-ext.json merged in (Facts.Load(json, extJson)): what the story, the guide and the report
        /// read. Rebuilt when either asset is swapped.
        /// </summary>
        public Facts FactsData
        {
            get
            {
                if (_factsData != null && _factsFrom == factsJson && _extFrom == factsExtJson) return _factsData;
                _factsFrom = factsJson; _extFrom = factsExtJson;
                return _factsData = factsJson ? Facts.Load(factsJson.text, factsExtJson ? factsExtJson.text : null) : Facts.Empty;
            }
        }

        /// <summary>
        /// The session the game runs on: the first one alive (Boot's, kept across level loads). A level scene brings its own
        /// GameSystems for standalone testing; that one wakes during the load and is destroyed by UnityStoryHost.EnterLevel,
        /// so it never takes Current from a session already running, and when Current goes the oldest survivor takes over.
        /// </summary>
        public static GameSession Current { get; private set; }

        static readonly List<GameSession> s_live = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_live.Clear(); Current = null; } // play mode without a domain reload

        void Awake()
        {
            s_live.Add(this);
            if (!Current) Current = this;
            Rules = new GameRules(items);
            State = Rules.CreateGame(seed != 0 ? seed : (long?)null);
            LoadFacts(factsJson ? factsJson.text : null, factsExtJson ? factsExtJson.text : null, _facts);
            if (Current == this) EasyKorean.UseFacts(FactsData); // 쉬운 한국어 finds the facts' plain wordings by their text
        }

        void OnDestroy()
        {
            s_live.Remove(this);
            if (Current != this) return;
            Current = null;
            for (int i = 0; i < s_live.Count && !Current; i++) if (s_live[i]) Current = s_live[i];
        }

        /// <summary>Text of a verified fact, or null.</summary>
        public string Fact(string id) => id != null && _facts.TryGetValue(id, out var t) ? t : null;

        public static void LoadFacts(string json, Dictionary<string, string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(json)) return;
            foreach (var f in JObject.Parse(json)["facts"] ?? new JArray())
                into[(string)f["id"]] = (string)f["text"];
        }

        /// <summary>The original facts, then the extension's (an id the original already has keeps the original text).</summary>
        public static void LoadFacts(string json, string extJson, Dictionary<string, string> into)
        {
            LoadFacts(json, into);
            if (string.IsNullOrWhiteSpace(extJson)) return;
            foreach (var f in JObject.Parse(extJson)["facts"] ?? new JArray())
            {
                var id = (string)f["id"];
                if (id != null && !into.ContainsKey(id)) into[id] = (string)f["text"];
            }
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
