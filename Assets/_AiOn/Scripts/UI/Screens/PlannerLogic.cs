using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>One card of the planner (original planner() actions[] item).</summary>
    public sealed class PlannerAction
    {
        /// <summary>The original a.id, returned as is when the action is confirmed (null when missing).</summary>
        public JToken Id;
        public string Label = "", Desc = "", Reason = "", Icon = "";
        public double Cost;
        public bool Done, Disabled;

        /// <summary>a.done || a.disabled: the card cannot be chosen.</summary>
        public bool Blocked => Done || Disabled;
    }

    /// <summary>One time slot tile: used (지남), in the preview (사용) or free.</summary>
    public readonly struct PlannerSlot
    {
        public readonly int No;
        public readonly bool Used, Preview;
        public readonly string State;
        public PlannerSlot(int no, bool used, bool preview, string state) { No = no; Used = used; Preview = preview; State = state; }
    }

    /// <summary>
    /// Pure part of the wall-calendar planner (original ui/planner.js): option parsing, slot tiles, the cost/after/lost
    /// detail and the select/confirm/back state machine. The screen only draws what this says.
    /// </summary>
    public sealed class PlannerLogic
    {
        public string Day = "";
        public double Total = 6, Used;
        public string Hint = "";
        public List<PlannerAction> Actions = new();

        public double Left => Total - Used;

        // ---------- state (indices into Actions, -1 = none) ----------
        /// <summary>The chosen card waiting for '이 일을 한다' (JS selected).</summary>
        public int Selected { get; private set; } = -1;
        /// <summary>The card with keyboard focus.</summary>
        public int Focus { get; private set; } = -1;
        /// <summary>The card whose cost the slot tiles preview (blocked cards preview 0).</summary>
        public int PreviewIndex { get; private set; } = -1;
        /// <summary>The card shown in the detail panel (-1: the '할 일을 고르면...' placeholder).</summary>
        public int DetailIndex { get; private set; } = -1;
        /// <summary>The detail panel shows the confirm buttons.</summary>
        public bool Confirming { get; private set; }

        public static PlannerLogic Parse(JObject o)
        {
            o ??= new JObject();
            var p = new PlannerLogic { Day = Str(o["day"]), Hint = Str(o["hint"]) };
            var slots = o["slots"] as JObject;
            double total = Num(slots?["total"], 6), used = Num(slots?["used"], 0);
            p.Total = Math.Max(0, total);
            p.Used = Math.Min(p.Total, Math.Max(0, used));
            if (o["actions"] is JArray arr)
                foreach (var t in arr.OfType<JObject>())
                    p.Actions.Add(new PlannerAction
                    {
                        Id = t["id"] == null || t["id"].Type == JTokenType.Null ? null : t["id"].DeepClone(),
                        Label = Str(t["label"]), Desc = Str(t["desc"]), Reason = Str(t["reason"]), Icon = Str(t["icon"]),
                        Cost = Num(t["cost"], 0), Done = Truthy(t["done"]), Disabled = Truthy(t["disabled"]),
                    });
            p.Init();
            return p;
        }

        /// <summary>preview(null), then focus on the first card that can be chosen (or the first card).</summary>
        public void Init()
        {
            Selected = -1;
            Hover(-1);
            int first = Actions.FindIndex(a => !a.Blocked);
            if (first < 0 && Actions.Count > 0) first = 0;
            if (first >= 0) SetFocus(first);
        }

        // ---------- texts ----------

        public string Sub => $"남은 칸 {JsNum(Left)} / {JsNum(Total)} · 모든 일을 다 할 수는 없습니다.";
        public string Label => $"준비 계획표 · {Day}";
        public const string Placeholder = "할 일을 고르면 드는 시간과 대신 못 하게 되는 일을 보여 줍니다.";

        /// <summary>The status line on a card: (ok, text) or null.</summary>
        public static (bool ok, string text)? CardStatus(PlannerAction a)
            => a.Done ? (true, "이미 했음") : a.Disabled ? (false, a.Reason != "" ? a.Reason : "지금은 할 수 없음") : ((bool, string)?)null;

        public static string CostText(PlannerAction a) => $"{JsNum(a.Cost)}칸";

        /// <summary>The detail panel's lock line for a blocked card.</summary>
        public static string BlockedText(PlannerAction a) => a.Done ? "이미 했습니다." : (a.Reason != "" ? a.Reason : "지금은 할 수 없습니다.");

        public double After(PlannerAction a) => Left - a.Cost;
        public string AfterText(PlannerAction a) { var after = After(a); return after > 0 ? $"{JsNum(after)}칸" : "없음 · 준비 시간이 끝납니다"; }

        /// <summary>The other open actions that no longer fit once a is done.</summary>
        public List<PlannerAction> Lost(PlannerAction a)
        {
            var after = After(a);
            return Actions.Where(b => !JToken.DeepEquals(b.Id, a.Id) &&!b.Blocked && b.Cost > after).ToList();
        }

        public List<PlannerSlot> Slots()
        {
            var a = PreviewIndex >= 0 ? Actions[PreviewIndex] : null;
            double cost = a != null && !a.Blocked ? a.Cost : 0;
            int n = (int)Math.Floor(Total);
            var list = new List<PlannerSlot>(n);
            for (int i = 0; i < n; i++)
            {
                bool used = i < Used, on = i >= Used && i < Used + cost;
                list.Add(new PlannerSlot(i + 1, used, on, used ? "지남" : on ? "사용" : ""));
            }
            return list;
        }

        public string SlotAria => $"준비 시간 {JsNum(Total)}칸 가운데 {JsNum(Used)}칸을 썼고 {JsNum(Left)}칸이 남았습니다";

        // ---------- events ----------

        /// <summary>preview(a): mouse enter or focus on card i (-1 = null).</summary>
        public void Hover(int i)
        {
            PreviewIndex = i;
            if (Selected < 0 || i == Selected)
            {
                DetailIndex = i;
                Confirming = i >= 0 && i == Selected;
            }
        }

        public void SetFocus(int i) { if (i < 0 || i >= Actions.Count) return; Focus = i; Hover(i); }

        /// <summary>Mouse leaving the card grid: preview(selected).</summary>
        public void Leave() => Hover(Selected);

        /// <summary>Click (or Enter/Space) on card i. Returns false for a blocked card (the 'nope' shake).</summary>
        public bool Choose(int i)
        {
            if (i < 0 || i >= Actions.Count) return false;
            Focus = i;
            if (Actions[i].Blocked)
            {
                Selected = -1;
                DetailIndex = i; Confirming = false;
                return false;
            }
            Selected = i;
            Hover(i);
            return true;
        }

        /// <summary>'다시 고르기' (or Esc while a card is chosen): drop the choice and focus that card again.</summary>
        public void Back()
        {
            int was = Selected;
            Selected = -1;
            Hover(-1);
            if (was >= 0) SetFocus(was);
        }

        /// <summary>Arrow keys on the cards (wrapping). Ignored while the confirm buttons hold focus.</summary>
        public void Step(int step)
        {
            if (Selected >= 0 || Focus < 0 || Actions.Count == 0) return;
            SetFocus(((Focus + step) % Actions.Count + Actions.Count) % Actions.Count);
        }

        /// <summary>Esc: true = close the planner with null; otherwise it went back from a choice.</summary>
        public bool Escape()
        {
            if (Selected >= 0) { Back(); return false; }
            return true;
        }

        /// <summary>The result of '이 일을 한다': the chosen action's id (null when none chosen).</summary>
        public JToken Confirm() => Selected >= 0 ? Actions[Selected].Id?.DeepClone() : null;

        // ---------- JS helpers ----------

        public static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null: case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.String: return ((string)t).Length > 0;
                case JTokenType.Integer: return (long)t != 0;
                case JTokenType.Float: { var d = (double)t; return d != 0 && !double.IsNaN(d); }
                default: return true;
            }
        }

        /// <summary>A string option ('' for null/missing).</summary>
        public static string Str(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined ? "" : t.Type == JTokenType.String ? (string)t : t.ToString();

        /// <summary>x ?? fallback for a number option.</summary>
        public static double Num(JToken t, double fallback)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return fallback;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (double)t;
            return double.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
        }

        /// <summary>A number as JS prints it in a template (2, not 2.0).</summary>
        public static string JsNum(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
    }
}
