using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>One line the player can write on the card (original docs() fields[] item).</summary>
    public sealed class DocsField
    {
        public string Id = "", Label = "", Value = "", Why = "";
        /// <summary>The original f.value, put on the card as is.</summary>
        public JToken RawValue;
        public bool Recommended;
    }

    /// <summary>A Tab stop of the docs screen, in the original's DOM order.</summary>
    public enum DocsStop { Hint, Field, Where, Done }

    /// <summary>
    /// Pure part of the emergency contact card (original ui/docs.js): options, which fields are written, where the card
    /// is kept, the paper and dead-phone lists, the result object and keyboard focus. The screen only draws it.
    /// </summary>
    public sealed class DocsLogic
    {
        /// <summary>Where the card can be kept: name and what it means (original WHERE).</summary>
        public static readonly IReadOnlyDictionary<string, (string name, string desc)> WhereText = new Dictionary<string, (string, string)>
        {
            ["bag"] = ("배낭 앞주머니", "대피할 때 배낭과 함께 나갑니다."),
            ["home"] = ("책상 서랍", "집 안에 있을 때만 꺼내 볼 수 있습니다."),
        };
        /// <summary>The mode chip (original mini-dom MODE_TEXT).</summary>
        public static readonly IReadOnlyDictionary<string, string> ModeText = new Dictionary<string, string>
        {
            ["guided"] = "안내 있음", ["hint"] = "힌트 가능", ["free"] = "혼자 판단", ["view"] = "보기",
        };

        public const string HelpText = "휴대폰이 꺼져도 볼 수 있는 것은 종이에 적은 것뿐입니다. 꼭 필요한 연락처와 장소부터 적어 보세요.";
        public const string HelpHintText = "각 항목 아래에 왜 필요한지가 함께 보입니다.";
        public const string Keys = "Tab 이동 · Space 적기/지우기 · ↑↓ 보관 장소 · Esc 닫기";
        public const string EmptyPaper = "아직 아무것도 적지 않았다.";
        public const string AllOnPaper = "모두 종이에 적었다.";

        public string Mode = "guided";
        public List<DocsField> Fields = new();
        public List<string> Carry = new() { "bag", "home" };
        public string Where;
        public bool HelpOpen;

        readonly HashSet<string> _on = new();
        public int Focus { get; private set; }

        public static DocsLogic Parse(JObject o)
        {
            o ??= new JObject();
            var d = new DocsLogic();
            var mode = o["mode"];
            // JS default applies only to undefined: an explicit null mode is no ladder mode (no chip, no help, why shown).
            if (mode != null && mode.Type != JTokenType.Undefined) d.Mode = PlannerLogic.Str(mode);
            if (o["fields"] is JArray fa)
                foreach (var f in fa.OfType<JObject>())
                    d.Fields.Add(new DocsField
                    {
                        Id = PlannerLogic.Str(f["id"]), Label = PlannerLogic.Str(f["label"]), Value = PlannerLogic.Str(f["value"]),
                        RawValue = f["value"]?.DeepClone(), Why = PlannerLogic.Str(f["why"]), Recommended = PlannerLogic.Truthy(f["recommended"]),
                    });
            if (o["carry"] is JArray ca) d.Carry = ca.Select(PlannerLogic.Str).ToList();
            d.Where = d.Carry.Contains("bag") ? "bag" : d.Carry.Count > 0 && d.Carry[0] != "" ? d.Carry[0] : "home";
            d.HelpOpen = d.Mode == "guided";
            return d;
        }

        // ---------- what is shown ----------

        public string ModeChip => ModeText.TryGetValue(Mode, out var t) ? t : null;
        /// <summary>guided: help shown; hint: behind a 힌트 보기 button; free/view: none.</summary>
        public bool HasHelp => Mode == "guided" || Mode == "hint";
        public bool HasHintButton => Mode == "hint";
        public string HintButtonText => HelpOpen ? "힌트 닫기" : "힌트 보기";
        public bool ShowBadge(DocsField f) => Mode == "guided" && f.Recommended;
        public bool ShowWhy(DocsField f) => f.Why != "" && Mode != "free";
        public bool ShowWhereDesc => Mode != "free";

        /// <summary>The radio options: carry keys that have a WHERE text, in carry order.</summary>
        public List<string> WhereOptions => Carry.Where(k => WhereText.ContainsKey(k)).ToList();

        public bool IsOn(DocsField f) => _on.Contains(f.Id);
        public int OnCount => _on.Count;
        public List<DocsField> Written => Fields.Where(IsOn).ToList();
        public List<DocsField> PhoneOnly => Fields.Where(f => !IsOn(f)).ToList();
        public string PhoneCount => $"{_on.Count}/{Fields.Count}개 항목";

        // ---------- changes ----------

        /// <summary>Ticks or unticks field i; returns the announcement ('...을(를) 적었다.' / '...을(를) 지웠다.').</summary>
        public string Toggle(int i)
        {
            if (i < 0 || i >= Fields.Count) return null;
            var f = Fields[i];
            bool now = !_on.Contains(f.Id);
            if (now) _on.Add(f.Id); else _on.Remove(f.Id);
            return now ? $"{f.Label}을(를) 적었다." : $"{f.Label}을(를) 지웠다.";
        }

        public void SetWhere(string k) { if (WhereOptions.Contains(k)) Where = k; }

        /// <summary>↑/↓ on the radio group: the next option, wrapping (from no checked option: the first / last).</summary>
        public void CycleWhere(int step)
        {
            var opts = WhereOptions;
            if (opts.Count == 0) return;
            int at = opts.IndexOf(Where);
            int next = at < 0 ? (step > 0 ? 0 : opts.Count - 1) : ((at + step) % opts.Count + opts.Count) % opts.Count;
            Where = opts[next];
        }

        public void ToggleHelp() { if (HasHintButton) HelpOpen = !HelpOpen; }

        /// <summary>{ card: { id: value } for the written fields in field order, where }.</summary>
        public JObject Result()
        {
            var card = new JObject();
            foreach (var f in Fields.Where(IsOn)) card[f.Id] = f.RawValue?.DeepClone() ?? JValue.CreateNull(); // JS keeps the key (value undefined)
            return new JObject { ["card"] = card, ["where"] = Where };
        }

        // ---------- keyboard ----------

        /// <summary>Tab stops in DOM order: hint button, each field, the radio group, the done button.</summary>
        public List<(DocsStop kind, int index)> Stops()
        {
            var s = new List<(DocsStop, int)>();
            if (HasHintButton) s.Add((DocsStop.Hint, 0));
            for (int i = 0; i < Fields.Count; i++) s.Add((DocsStop.Field, i));
            if (WhereOptions.Count > 0) s.Add((DocsStop.Where, 0));
            s.Add((DocsStop.Done, 0));
            return s;
        }

        /// <summary>The first field has data-autofocus; otherwise the first stop.</summary>
        public void InitFocus()
        {
            var s = Stops();
            int f = s.FindIndex(x => x.kind == DocsStop.Field);
            Focus = f >= 0 ? f : 0;
        }

        public (DocsStop kind, int index) Focused { get { var s = Stops(); return s[System.Math.Min(Focus, s.Count - 1)]; } }

        public void Tab(bool back)
        {
            int n = Stops().Count;
            Focus = ((Focus + (back ? -1 : 1)) % n + n) % n;
        }

        public void FocusOn(DocsStop kind, int index)
        {
            int i = Stops().FindIndex(x => x.kind == kind && x.index == index);
            if (i >= 0) Focus = i;
        }

        /// <summary>Space on the focused stop. Returns the announcement, '' for other effects, null when nothing happened;
        /// done = true when the screen should close with Result().</summary>
        public string Space(out bool done)
        {
            done = false;
            var (kind, index) = Focused;
            switch (kind)
            {
                case DocsStop.Field: return Toggle(index);
                case DocsStop.Hint: ToggleHelp(); return "";
                case DocsStop.Done: done = true; return "";
                case DocsStop.Where:
                    {
                        // Tab lands on the checked radio (Space: nothing new); with none checked it lands on the first, and Space checks it.
                        var opts = WhereOptions;
                        if (opts.Count > 0 && !opts.Contains(Where)) { Where = opts[0]; return ""; }
                        return null;
                    }
                default: return null;
            }
        }

        /// <summary>↑/↓: change the place (the radio group takes focus).</summary>
        public void Arrow(int step)
        {
            if (WhereOptions.Count == 0) return;
            FocusOn(DocsStop.Where, 0);
            CycleWhere(step);
        }
    }
}
