using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>A radio station on the dial. Only <see cref="Freq"/> matters to the rules; the rest is for the radio screen.</summary>
    public sealed class RadioStation
    {
        /// <summary>MHz.</summary>
        public double Freq;
        public string Name;
        /// <summary>official | music | rumor</summary>
        public string Kind;
        /// <summary>What is heard once the station is locked.</summary>
        public string[] Lines = Array.Empty<string>();
    }

    /// <summary>What the dial hears: the nearest station inside the band (null = static) and how clear it is, 0..1.</summary>
    public readonly struct RadioSignalResult
    {
        public readonly RadioStation Station;
        public readonly double Clarity;
        public RadioSignalResult(RadioStation station, double clarity) { Station = station; Clarity = clarity; }
    }

    /// <summary>What stands between the player and the radio (original radioNeeds).</summary>
    public sealed class RadioNeedsResult
    {
        [JsonProperty("ok")] public bool Ok;
        /// <summary>noRadio | noBatteries | notChecked</summary>
        [JsonProperty("blocks")] public List<string> Blocks = new();
        /// <summary>Time lost loading batteries at the worst moment.</summary>
        [JsonProperty("extraSeconds")] public int ExtraSeconds;
        /// <summary>FACTS ids.</summary>
        [JsonProperty("tips")] public List<string> Tips = new();
    }

    /// <summary>One extinguisher step or fight/retreat choice. The label is the button text; the safety text is in FACTS.</summary>
    public sealed class DrillStep
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public string Label;
        [JsonProperty("facts")] public string[] Facts;
    }

    public sealed class ExtinguisherEval
    {
        /// <summary>out | incomplete | escalated | retreat-ok</summary>
        [JsonProperty("result")] public string Result;
        /// <summary>The whole official order was followed (or retreating was the right call).</summary>
        [JsonProperty("correct")] public bool Correct;
        /// <summary>The step or choice expected instead of the first slip; null when there was none.</summary>
        [JsonProperty("firstError")] public string FirstError;
    }

    /// <summary>One line of the emergency contact card.</summary>
    public sealed class DocField
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public string Label;
        [JsonProperty("facts")] public string[] Facts;
        [JsonProperty("recommended")] public bool Recommended = true;

        /// <summary>Why the field matters: the text of its first fact (the original DOC_FIELDS[].why).</summary>
        public string Why(Func<string, string> fact) => fact(Facts[0]);
    }

    public sealed class DocsEval
    {
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("covered")] public List<string> Covered = new();
        [JsonProperty("missing")] public List<string> Missing = new();
        /// <summary>FACTS ids.</summary>
        [JsonProperty("tips")] public List<string> Tips = new();
    }

    /// <summary>
    /// Minigame rules other than the bag (original game/drills.js): radio tuning, extinguisher order and the emergency
    /// contact card. The bag part and the encounter ladder are in <see cref="BagDrill"/>. Pure logic.
    /// Safety text is never written here: steps and fields point at FACTS ids.
    /// </summary>
    public static class Drills
    {
        // ---------- the rest of drills.js lives in BagDrill; these keep the original names reachable ----------

        public static string LadderStage(GameState g, string tool) => BagDrill.LadderStage(g, tool);
        public static JObject RecordEncounter(GameState g, string tool, string result) => BagDrill.RecordEncounter(g, tool, result);
        public static IReadOnlyDictionary<string, BagGoal[]> BagGoals => BagDrill.Goals;
        public static BagEval EvalBag(GameRules rules, GameState g, string context) => BagDrill.EvalBag(rules, g, context);

        // ---------- radio ----------

        /// <summary>MHz either side of a station where anything is heard.</summary>
        public const double RadioWidth = 0.6;

        public static RadioSignalResult RadioSignal(double freq, IEnumerable<RadioStation> stations = null)
        {
            if (double.IsNaN(freq) || double.IsInfinity(freq))
                throw new ArgumentException($"freq must be a number, got {freq.ToString(CultureInfo.InvariantCulture)}", nameof(freq));
            RadioStation station = null;
            double clarity = 0;
            foreach (var s in stations ?? Enumerable.Empty<RadioStation>())
            {
                double c = Math.Max(0, 1 - Math.Abs(freq - s.Freq) / RadioWidth);
                if (c > clarity) { clarity = c; station = s; }
            }
            return new RadioSignalResult(station, Num.JsRound(clarity * 1000) / 1000);
        }

        /// <summary>
        /// What stands between the player and the radio. An unchecked radio with spare batteries still works
        /// (a half-used pack too), but loading them costs time at the worst moment.
        /// </summary>
        public static RadioNeedsResult RadioNeeds(GameState g)
        {
            bool Has(string id) => g.Items.TryGetValue(id, out var e) && e != null && e.Home + e.Bag > 0;
            if (!Has("radio")) return new RadioNeedsResult { Ok = false, Blocks = { "noRadio" }, Tips = { "power_prep" } };
            if (g.Items["radio"].Checked) return new RadioNeedsResult { Ok = true };
            if (!Has("batteries_aa") && !Has("batteries_old")) return new RadioNeedsResult { Ok = false, Blocks = { "noBatteries" }, Tips = { "radio_batt" } };
            return new RadioNeedsResult { Ok = true, Blocks = { "notChecked" }, ExtraSeconds = 40, Tips = { "radio_batt" } };
        }

        // ---------- extinguisher ----------

        /// <summary>Official order (fire_shout, ext_1..ext_3, ext_door).</summary>
        public static readonly DrillStep[] ExtSteps =
        {
            new DrillStep { Id = "shout", Label = "\"불이야!\" 외쳐 알리기", Facts = new[] { "fire_shout" } },
            new DrillStep { Id = "pin", Label = "소화기를 옮겨 안전핀 뽑기", Facts = new[] { "ext_1" } },
            new DrillStep { Id = "aim", Label = "바람·문을 등지고 노즐을 불 쪽으로", Facts = new[] { "ext_2", "ext_door" } },
            new DrillStep { Id = "squeeze", Label = "손잡이 힘껏 움켜쥐기", Facts = new[] { "ext_3" } },
            new DrillStep { Id = "sweep", Label = "빗자루로 쓸듯이 뿌리기", Facts = new[] { "ext_3" } },
        };

        public static readonly DrillStep[] ExtChoices =
        {
            new DrillStep { Id = "fight", Label = "소화기로 끈다", Facts = new[] { "fire_small" } }, // candle-specific advice stays with the candle fire's own card
            new DrillStep { Id = "retreat", Label = "대피하며 알린다 (계단 · 119)", Facts = new[] { "fire_big", "fire_119" } },
        };

        static readonly string[] Order = { "shout", "pin", "aim", "squeeze", "sweep" };
        /// <summary>What physically puts the fire out.</summary>
        static readonly string[] Ops = { "pin", "aim", "squeeze", "sweep" };

        /// <summary>
        /// sequence: decision ('fight' | 'retreat') and step ids in the order pressed; fire: 'small' | 'large'.
        /// Retreating is always a success; on a large fire it is the only correct choice. Result 'out' needs
        /// pin -> aim -> squeeze -> sweep in order; Correct also needs the alert first.
        /// FirstError = the step or choice expected instead.
        /// </summary>
        public static ExtinguisherEval EvalExtinguisher(IEnumerable<string> sequence, string fire)
        {
            if (fire != "small" && fire != "large") throw new ArgumentException($"fire must be 'small' or 'large', got {fire}", nameof(fire));
            if (sequence == null) throw new ArgumentNullException(nameof(sequence), "sequence must be an array");
            var seq = sequence.ToList();
            var bad = seq.FirstOrDefault(id => id != null && Array.IndexOf(Order, id) < 0 && id != "fight" && id != "retreat");
            if (bad != null) throw new ArgumentException($"unknown extinguisher step: {bad}", nameof(sequence));

            var decision = seq.FirstOrDefault(id => id == "fight" || id == "retreat");
            if (seq.Contains("retreat"))
            {
                bool right = fire == "small" || decision == "retreat";
                return new ExtinguisherEval { Result = "retreat-ok", Correct = right, FirstError = right ? null : "retreat" };
            }
            if (seq.Count == 0) return new ExtinguisherEval { Result = "incomplete", Correct = false, FirstError = fire == "large" ? "retreat" : Order[0] };
            if (fire == "large") return new ExtinguisherEval { Result = "escalated", Correct = false, FirstError = "retreat" };

            // Steps in the order first pressed (repeated presses are ignored).
            var steps = new List<string>();
            foreach (var id in seq) if (id != null && Array.IndexOf(Order, id) >= 0 && !steps.Contains(id)) steps.Add(id);
            var ops = steps.Where(id => Array.IndexOf(Ops, id) >= 0).ToList();
            bool isOut = ops.Count == Ops.Length && ops.SequenceEqual(Ops);
            string firstError = null;
            for (int i = 0; i < Order.Length; i++)
                if (i >= steps.Count || steps[i] != Order[i]) { firstError = Order[i]; break; }
            return new ExtinguisherEval { Result = isOut ? "out" : "incomplete", Correct = firstError == null, FirstError = firstError };
        }

        // ---------- emergency contact card ----------

        /// <summary>The card's fields. Each one's "why" is the text of its first fact: <see cref="DocField.Why"/>.</summary>
        public static readonly DocField[] DocFields =
        {
            new DocField { Id = "contacts", Label = "가족·지인 연락처", Facts = new[] { "contact_card", "contact_fields" } },
            new DocField { Id = "meds", Label = "복용약·혈액형", Facts = new[] { "contact_fields" } },
            new DocField { Id = "shelter", Label = "대피소 주소", Facts = new[] { "shelter_know", "contact_meet" } },
            new DocField { Id = "meet", Label = "만남의 장소(1·2·3차)", Facts = new[] { "contact_meet" } },
            new DocField { Id = "idCopy", Label = "신분증 사본", Facts = new[] { "bag_copies", "docs_take" } },
            new DocField { Id = "cash", Label = "현금 챙기기 메모", Facts = new[] { "bag_cash" } },
        };

        /// <summary>
        /// card: { fieldId: value } from the docs screen (null = nothing written); where: 'bag' | 'home', or null when
        /// the screen did not ask. A field counts when its value is not null, false or blank. A card kept anywhere
        /// but the bag is not ok, however complete.
        /// </summary>
        public static DocsEval EvalDocs(JObject card, string where = null)
        {
            var r = new DocsEval();
            foreach (var f in DocFields) if (Filled(card?[f.Id])) r.Covered.Add(f.Id);
            foreach (var f in DocFields) if (f.Recommended && !r.Covered.Contains(f.Id)) r.Missing.Add(f.Id);
            foreach (var f in DocFields)
                if (r.Missing.Contains(f.Id))
                    foreach (var t in f.Facts) if (!r.Tips.Contains(t)) r.Tips.Add(t);
            bool away = where != null && where != "bag";
            if (away && !r.Tips.Contains("contact_card")) r.Tips.Add("contact_card");
            r.Ok = r.Missing.Count == 0 && !away;
            return r;
        }

        /// <summary>The original's filled(): not undefined, null or false, and String(v).trim() is not empty (so 0 counts).</summary>
        static bool Filled(JToken v)
        {
            if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.Undefined) return false;
            if (v.Type == JTokenType.Boolean && !(bool)v) return false;
            return JsTrim(JsString(v)).Length > 0;
        }

        /// <summary>JS String(v) as far as a card value needs it: arrays join their items with ',', objects are never blank.</summary>
        static string JsString(JToken v)
        {
            switch (v.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined: return "";
                case JTokenType.String: return (string)v;
                case JTokenType.Boolean: return (bool)v ? "true" : "false";
                case JTokenType.Array: return string.Join(",", ((JArray)v).Select(x => x == null ? "" : JsString(x)));
                case JTokenType.Object: return "[object Object]";
                default: return v.ToString(Formatting.None); // numbers and the rest: never blank
            }
        }

        /// <summary>JS String.prototype.trim (its white space set is not the same as char.IsWhiteSpace: BOM yes, NEL no).</summary>
        static string JsTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsJsSpace(s[a])) a++;
            while (b > a && IsJsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        static bool IsJsSpace(char c) =>
            (c >= 0x09 && c <= 0x0D) || c == 0x20 || c == 0xA0 || c == 0x1680 || (c >= 0x2000 && c <= 0x200A)
            || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000 || c == 0xFEFF;
    }
}
