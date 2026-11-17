// Pure logic of the handover message composer (original ui/compose.js): the chip rows with their labels, what the
// chips and the free text do to each other, the send/reset state and the result object; plus the marked note's
// spans (showNote 'marked') and the re-read caption's options. The sentences come from Relay.ComposeNote (C# port
// of game/relay.js): never re-implemented here. No Unity types, so it is tested outside Unity.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>One choice of a chip row; Label is the exact fragment that goes into the message (or 시각 없이 / 없음...).</summary>
    public sealed class ComposeOption
    {
        public string Value, Label;
        public ComposeOption(string value, string label) { Value = value; Label = label; }
    }

    /// <summary>A chip row (fieldset): radios, or a single checkbox (unknowns).</summary>
    public sealed class ComposeRow
    {
        public string Name, Legend;
        public bool Checkbox;
        public List<ComposeOption> Options = new();
    }

    /// <summary>The composer's state machine. Radios start unchecked, like the original form.</summary>
    public sealed class ComposeState
    {
        public string Route, ObservedVia, GateSeenAt, SentAt;
        public readonly NoteChips Chips = new();
        public bool Edited { get; private set; }
        /// <summary>The message in the text box.</summary>
        public string Text { get; private set; } = "";
        public readonly List<ComposeRow> Rows;
        /// <summary>Checked radio value per row (absent: none checked); the checkbox row keeps "true" when ticked.</summary>
        readonly Dictionary<string, string> _checked = new();

        public ComposeState(string route, string observedVia, string gateSeenAt, string sentAt)
        {
            Route = route; ObservedVia = observedVia; GateSeenAt = gateSeenAt; SentAt = sentAt ?? "";
            Rows = BuildRows();
            Refresh();
        }

        /// <summary>compose({ route, observedVia, gateSeenAt, sentAt }).</summary>
        public static ComposeState FromOptions(JObject o)
        {
            o ??= new JObject();
            string S(string k) { var t = o[k]; return t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined ? null : t.Type == JTokenType.String ? t.Value<string>() : t.ToString(); }
            return new ComposeState(S("route"), S("observedVia"), S("gateSeenAt"), S("sentAt"));
        }

        // composeNote's own defaults apply when the context leaves a value out.
        string R => Route ?? "stairs";
        string Via => ObservedVia ?? "sign";
        string Gate => GateSeenAt ?? "20:09";

        string Note(NoteChips c) => Relay.ComposeNote(c, R, Via, Gate);

        List<ComposeRow> BuildRows() => new()
        {
            new ComposeRow { Name = "what", Legend = "무엇을 봤나", Options = {
                new("gate_closed", Note(new NoteChips { What = "gate_closed" })), new("all_closed", Note(new NoteChips { What = "all_closed" })) } },
            new ComposeRow { Name = "time", Legend = "언제", Options = { new("true", $"{Gate}에"), new("false", "시각 없이") } },
            new ComposeRow { Name = "route", Legend = "내가 쓴 길", Options = { new("true", Note(new NoteChips { Route = true })), new("false", "쓰지 않기") } },
            new ComposeRow { Name = "unknowns", Legend = "모르는 것", Checkbox = true, Options = { new("true", Note(new NoteChips { Unknowns = true })) } },
            new ComposeRow { Name = "advice", Legend = "한마디", Options = {
                new("follow_me", Note(new NoteChips { Advice = "follow_me" })), new("direction", Note(new NoteChips { Advice = "direction" })),
                new("check_notice", Note(new NoteChips { Advice = "check_notice" })), new("", "없음") } },
        };

        /// <summary>The eyebrow above the title.</summary>
        public string Eyebrow => $"서연(303호)에게 문자 · {SentAt}";
        public string SendLabel => $"보내기 · {SentAt}";
        /// <summary>The entrance notice quote, shown only when 정민 learned of the closure from it.</summary>
        public string Quote => ObservedVia == "notice_after" ? "입구 안내문: “동쪽 출입구 폐쇄 · A 계단 / B 진출입로 이용”" : null;
        public string Help => $"최대 {Relay.MaxNote}자 · 직접 고칠 수 있어요";

        public bool CanSend => Edited ? Text.Trim().Length > 0 : !string.IsNullOrEmpty(Chips.What);
        public bool ShowReset => Edited;

        public bool IsChecked(string row, string value) => _checked.TryGetValue(row, out var v) && v == value;
        public string CheckedValue(string row) => _checked.TryGetValue(row, out var v) ? v : null;

        void Refresh()
        {
            if (!Edited) Text = Note(Chips);
        }

        /// <summary>A radio is picked (or the checkbox set): the chip changes, and the text follows unless edited.</summary>
        public void Pick(string row, string value)
        {
            switch (row)
            {
                case "unknowns":
                    Chips.Unknowns = value == "true";
                    if (Chips.Unknowns) _checked[row] = "true"; else _checked.Remove(row);
                    break;
                case "time": case "route":
                    _checked[row] = value;
                    if (row == "time") Chips.Time = value == "true"; else Chips.Route = value == "true";
                    break;
                case "what": case "advice":
                    _checked[row] = value ?? "";
                    var v = string.IsNullOrEmpty(value) ? null : value;
                    if (row == "what") Chips.What = v; else Chips.Advice = v;
                    break;
                default: return;
            }
            Refresh();
        }

        /// <summary>Space on the checkbox.</summary>
        public void Toggle(string row) { if (row == "unknowns") Pick(row, Chips.Unknowns ? "false" : "true"); }

        /// <summary>The player typed: the text is theirs now (cut to the box's maximum).</summary>
        public void Type(string text)
        {
            text ??= "";
            if (text.Length > Relay.MaxNote) text = text.Substring(0, Relay.MaxNote);
            Edited = true;
            Text = text;
        }

        /// <summary>칩으로 다시 쓰기: back to the chips' sentence.</summary>
        public void Reset() { Edited = false; Refresh(); }

        static JToken Nullable(string s) => s == null ? JValue.CreateNull() : new JValue(s);

        /// <summary>What compose() resolves with: { chips, text, edited }.</summary>
        public JObject Result() => new()
        {
            ["chips"] = new JObject
            {
                ["what"] = Nullable(Chips.What), ["time"] = Chips.Time, ["route"] = Chips.Route, ["unknowns"] = Chips.Unknowns, ["advice"] = Nullable(Chips.Advice),
            },
            ["text"] = Text,
            ["edited"] = Edited,
        };
    }

    public static class ComposeLogic
    {
        /// <summary>
        /// showNote 'marked': the message split into plain and claimed pieces. Each distinct quote is located by its first
        /// occurrence; overlapping quotes after an earlier one are skipped (original showNote).
        /// </summary>
        public static List<(string text, bool claim)> MarkedSpans(string text, IEnumerable<string> quotes)
        {
            text ??= "";
            var spans = (quotes ?? Enumerable.Empty<string>()).Where(q => q != null).Distinct()
                .Select(q => (i: text.IndexOf(q, StringComparison.Ordinal), q)).Where(s => s.i >= 0)
                .OrderBy(s => s.i).ToList();
            var parts = new List<(string, bool)>();
            int at = 0;
            foreach (var (i, q) in spans)
            {
                if (i < at) continue;
                parts.Add((text.Substring(at, i - at), false));
                parts.Add((q, true));
                at = i + q.Length;
            }
            parts.Add((text.Substring(at), false));
            return parts.Where(p => p.Item1.Length > 0).ToList();
        }

        /// <summary>The marked note's quotes, from hand.claims.quotes (insertion order of the original object).</summary>
        public static List<string> Quotes(JObject hand) =>
            (hand?["claims"]?["quotes"] as JObject)?.Properties().Select(p => p.Value.Type == JTokenType.String ? p.Value.Value<string>() : null).Where(s => s != null).ToList()
            ?? new List<string>();

        /// <summary>The note panel's small header line.</summary>
        public static string NoteHeader(JObject hand) => $"정민 · 302호 이웃 · 보냄 {hand?["sentAt"]} · 받음 {hand?["receivedAt"]}";

        /// <summary>The re-read caption's button: last step continues to 서연.</summary>
        public static string CaptionButton(bool last) => last ? "서연으로 계속 ▸" : "다음 ▸";

        public const string CaptionHint = "E · Enter · Space로 넘기기 · Esc로 끝까지";

        /// <summary>The pin lines under a caption: was, now, and also.now when present (null pin: none).</summary>
        public static List<(string text, bool was)> PinLines(JObject pin)
        {
            var lines = new List<(string, bool)>();
            if (pin == null) return lines;
            lines.Add((pin["was"]?.ToString() ?? "", true));
            lines.Add((pin["now"]?.ToString() ?? "", false));
            if (pin["also"] is JObject also) lines.Add((also["now"]?.ToString() ?? "", false));
            return lines;
        }

        /// <summary>Esc gives 'skip'; E, Enter and Space give 'next'. Anything else: null (key ignored).</summary>
        public static string CaptionKey(string key) => key switch
        {
            "Escape" => "skip",
            "KeyE" or "Enter" or "Space" => "next",
            _ => null,
        };
    }
}
