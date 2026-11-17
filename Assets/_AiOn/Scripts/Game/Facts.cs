using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One verified safety fact (original FACTS entry). The game states safety advice only from these.</summary>
    public sealed class FactEntry
    {
        [JsonProperty("id")] public string Id;
        /// <summary>bag | home | contacts | power | comms | alert | shelter | fire | water | food | evac | info | people | mind | myth</summary>
        [JsonProperty("topic")] public string Topic;
        [JsonProperty("sourceId")] public string SourceId;
        [JsonProperty("text")] public string Text;
    }

    /// <summary>Where a fact comes from (original SOURCES entry).</summary>
    public sealed class FactSource
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("publisher")] public string Publisher;
        [JsonProperty("title")] public string Title;
        [JsonProperty("url")] public string Url;
        /// <summary>The date the source page was opened and the line checked against it (yyyy-MM-dd).</summary>
        [JsonProperty("checkedAt")] public string CheckedAt;
    }

    /// <summary>"text - publisher · title" for cards and captions (original cite()).</summary>
    public sealed class FactCitation
    {
        [JsonProperty("text")] public string Text;
        /// <summary>"publisher · title", or "" when the fact's source is unknown.</summary>
        [JsonProperty("source")] public string Source;
        [JsonProperty("url")] public string Url;
    }

    /// <summary>
    /// The verified facts and their sources (original game/facts.js). Every line was checked against the opened
    /// source page on the date in checkedAt; where official pages disagree (candles) both sides are kept.
    /// The text is data, not code: it is loaded from Data/Source/facts.json
    /// ({ facts: [{ id, topic, sourceId, text }], sources: [{ id, publisher, title, url, checkedAt }] }),
    /// so a safety line is never retyped here.
    /// </summary>
    public sealed class Facts
    {
        /// <summary>No facts at all (a scene or test that shows none).</summary>
        public static readonly Facts Empty = new(new List<FactEntry>(), new List<FactSource>());

        /// <summary>Every fact, in the original order (FACTS).</summary>
        public IReadOnlyList<FactEntry> All { get; }
        /// <summary>Every source, in the original order (SOURCES).</summary>
        public IReadOnlyList<FactSource> Sources { get; }

        readonly Dictionary<string, FactEntry> _byId = new();
        readonly Dictionary<string, FactSource> _srcById = new();

        Facts(List<FactEntry> facts, List<FactSource> sources)
        {
            All = facts;
            Sources = sources;
            foreach (var f in facts) if (f?.Id != null) _byId[f.Id] = f;
            foreach (var s in sources) if (s?.Id != null) _srcById[s.Id] = s;
        }

        /// <summary>Reads facts.json. Throws when it is not the facts file (a wiring mistake, not a player error).</summary>
        public static Facts Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("facts json is empty", nameof(json));
            JToken root;
            try
            {
                // No date parsing: checkedAt and the texts stay the strings they are.
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
                root = JToken.ReadFrom(reader);
            }
            catch (JsonException e) { throw new ArgumentException($"facts json is not JSON: {e.Message}", nameof(json)); }
            if (root is not JObject o || o["facts"] is not JArray facts) throw new ArgumentException("facts json needs a 'facts' array", nameof(json));
            var sources = o["sources"] as JArray ?? new JArray();
            return new Facts(facts.ToObject<List<FactEntry>>(), sources.ToObject<List<FactSource>>());
        }

        /// <summary>The fact, or null.</summary>
        public FactEntry Fact(string id) => id != null && _byId.TryGetValue(id, out var f) ? f : null;

        /// <summary>The source, or null.</summary>
        public FactSource Source(string id) => id != null && _srcById.TryGetValue(id, out var s) ? s : null;

        /// <summary>Text, "publisher · title" and link of a fact, or null.</summary>
        public FactCitation Cite(string id)
        {
            var f = Fact(id);
            if (f == null) return null;
            var s = Source(f.SourceId);
            return new FactCitation { Text = f.Text, Source = s != null ? $"{s.Publisher} · {s.Title}" : "", Url = s?.Url ?? "" };
        }

        /// <summary>The fact's text, or null: the <c>Func&lt;string, string&gt; fact</c> the rules take (the original fact(id)?.text).</summary>
        public string Text(string id) => Fact(id)?.Text;

        /// <summary>The id of the fact's source, or null (the original fact(id)?.sourceId).</summary>
        public string SourceId(string id) => Fact(id)?.SourceId;
    }
}
