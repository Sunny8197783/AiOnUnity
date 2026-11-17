using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>
    /// The whole run as one JSON-safe object (the original game/state.js G). Pure data: the rules that
    /// change it live in <see cref="GameRules"/>, saving in <see cref="SaveCodec"/>.
    /// JSON names match the original web build, so its v2 saves load here and the other way round.
    /// </summary>
    public class GameState
    {
        public const int Version = 2;

        [JsonProperty("v")] public int V = Version;
        [JsonProperty("seed")] public uint Seed;
        [JsonProperty("chapter")] public string Chapter = "prologue";
        /// <summary>Days from the alert: -3 = D-3, 0 = D-day, 12 = D+12.</summary>
        [JsonProperty("day")] public int Day = -3;
        /// <summary>Minute of the day, 0..1439.</summary>
        [JsonProperty("minute")] public int Minute = 17 * 60 + 30;
        [JsonProperty("slots")] public Slots Slots = new();
        [JsonProperty("money")] public long Money = 150000;
        [JsonProperty("stats")] public Stats Stats = new();
        [JsonProperty("phone")] public Phone Phone = new();
        /// <summary>Item id -> where the player's units are. Insertion order matters (bag layout listing).</summary>
        [JsonProperty("items")] public Dictionary<string, ItemEntry> Items = new();
        /// <summary>What the player has learned or done (shelterLocated, bagOn, meetPoint...). Values are JSON.</summary>
        [JsonProperty("know")] public Dictionary<string, JToken> Know = new();
        [JsonProperty("rel")] public Dictionary<string, Relation> Rel = new();
        /// <summary>Per tool ('radio', 'bag', ...), the ladder of practice records.</summary>
        [JsonProperty("skill")] public Dictionary<string, List<JToken>> Skill = new();
        [JsonProperty("info")] public List<InfoNote> Info = new();
        [JsonProperty("cond")] public GameConditions Cond = new();
        [JsonProperty("events")] public List<GameEvent> Events = new();
        /// <summary>Day-ledger rows (original game/ledger.js, not ported yet).</summary>
        [JsonProperty("ledger")] public List<JObject> Ledger = new();
        [JsonProperty("checkpoint", NullValueHandling = NullValueHandling.Include)] public Checkpoint Checkpoint;
        /// <summary>Lasting injuries ('leg'); null until the first one.</summary>
        [JsonProperty("injury", NullValueHandling = NullValueHandling.Ignore)] public List<string> Injury;

        /// <summary>Fields owned by systems not ported yet (relay, karma, handover...); kept on save.</summary>
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();

        /// <summary>JS truthiness of a know flag: missing, null, false, 0 and "" are false.</summary>
        public bool Knows(string key)
        {
            if (!Know.TryGetValue(key, out var v) || v == null) return false;
            return v.Type switch
            {
                JTokenType.Null or JTokenType.Undefined => false,
                JTokenType.Boolean => (bool)v,
                JTokenType.Integer or JTokenType.Float => (double)v != 0,
                JTokenType.String => ((string)v).Length > 0,
                _ => true,
            };
        }

        public void SetKnow(string key, JToken value) => Know[key] = value ?? JValue.CreateNull();

        public bool HasInjury(string part) => Injury != null && Injury.Contains(part);
    }

    public class Slots
    {
        [JsonProperty("total")] public int Total = 6;
        [JsonProperty("used")] public int Used;
        /// <summary>Action ids in the order they were planned.</summary>
        [JsonProperty("done")] public List<string> Done = new();
    }

    public class Stats
    {
        [JsonProperty("health")] public double Health = 100;
        [JsonProperty("thirst")] public double Thirst = 10;
        [JsonProperty("fatigue")] public double Fatigue = 20;
        [JsonProperty("tension")] public double Tension = 25;
    }

    public enum Stat { Health, Thirst, Fatigue, Tension }

    public class Phone
    {
        [JsonProperty("battery")] public double Battery = 64;
        [JsonProperty("signal")] public string Signal = "ok";
    }

    public class ItemEntry
    {
        [JsonProperty("home")] public int Home;
        [JsonProperty("bag")] public int Bag;
        [JsonProperty("bought")] public int Bought;
        /// <summary>Usable: batteries in / charged. Only items with a needsCheck care.</summary>
        [JsonProperty("checked")] public bool Checked;
        [JsonProperty("used")] public int Used;
        /// <summary>Battery devices: 0..100 once checked. Null = checked before charges existed (counts as full).</summary>
        [JsonProperty("charge", NullValueHandling = NullValueHandling.Ignore)] public double? Charge;
        /// <summary>Units of the packed ones in the front pocket. Null instead of 0, like the original.</summary>
        [JsonProperty("pocket", NullValueHandling = NullValueHandling.Ignore)] public int? Pocket;

        public ItemEntry() { }
        public ItemEntry(int home) => Home = home;

        [JsonIgnore] public int Owned => Home + Bag;
    }

    public class Relation
    {
        [JsonProperty("trust")] public double Trust;
        [JsonProperty("contact", NullValueHandling = NullValueHandling.Include)] public JToken Contact = JValue.CreateNull();
    }

    public class InfoNote
    {
        [JsonProperty("chapter")] public string Chapter;
        [JsonProperty("day")] public int Day;
        [JsonProperty("minute")] public int Minute;
        [JsonProperty("text")] public string Text;
        /// <summary>official | site | neighbor | rumor | self</summary>
        [JsonProperty("label")] public string Label;
        [JsonProperty("place", NullValueHandling = NullValueHandling.Include)] public string Place;
    }

    public class GameEvent
    {
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("type")] public string Type;
        [JsonProperty("chapter")] public string Chapter;
        [JsonProperty("day")] public int Day;
        [JsonProperty("minute")] public int Minute;
        [JsonProperty("details")] public JObject Details = new();
    }

    public class Checkpoint
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("state")] public GameState State;
    }
}
