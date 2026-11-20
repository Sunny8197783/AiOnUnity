// 1장 소화기 점검 (competition upgrade): at the 3층 복도 소화기 (집 장비 점검), 정민 reads its pressure gauge and decides
// what to do about it. The needle sits just left of the green band: pressure has leaked over the years (fact ext_gauge;
// red or yellow means it must be replaced, ext_gauge_bad), and the fix for a shared one in an apartment is to tell the
// management office (ext_manage). Telling them gets it replaced before 3장; never checking it, or leaving it, means the
// extinguisher 정민 grabs in the corridor fire is the low one (HeldExtinguisherLogic Low: short and weak).
// Pure: the "extgauge" screen draws the dial and these rules judge it.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public static class ExtinguisherCheck
    {
        /// <summary>The know flag: "reported" (told the office; replaced) or "left" (checked, left as it was).</summary>
        public const string Know = "extGauge";
        public const string Reported = "reported", Left = "left";
        /// <summary>The event recorded at the check ({ reading, answer, ok, action }).</summary>
        public const string Event = "ext_gauge";
        public const string Screen = "extgauge";
        /// <summary>The preparation id of the payoff (Ripples.Prep: "소화기 압력계 점검").</summary>
        public const string Prep = "extgauge";
        public static readonly string[] FactIds = { "ext_gauge", "ext_gauge_bad", "ext_manage" };

        /// <summary>The green band on the dial (0 = the left stop, 1 = the right stop).</summary>
        public const double GreenFrom = 0.38, GreenTo = 0.62;
        /// <summary>Where this extinguisher's needle sits: a little left of green.</summary>
        public const double CorridorNeedle = 0.29;

        /// <summary>low (left of green) | ok (green) | over (right of green).</summary>
        public static string Zone(double needle) => needle < GreenFrom ? "low" : needle > GreenTo ? "over" : "ok";

        public static readonly IReadOnlyList<(string id, string label)> Readings = new[]
        {
            ("ok", "녹색 구간 안이다 — 정상"),
            ("low", "녹색보다 왼쪽이다 — 압력이 빠졌다"),
            ("over", "녹색보다 오른쪽이다 — 압력이 너무 높다"),
        };

        public static readonly IReadOnlyList<(string id, string label)> Actions = new[]
        {
            ("report", "관리사무소에 알려 교체를 부탁한다"),
            ("leave", "일단 그대로 둔다"),
        };

        public static string ReadingFeedback(string answer, double needle)
        {
            var z = Zone(needle);
            if (answer == z) return z == "ok" ? "맞다. 바늘이 녹색 구간 안에 있다." : "맞다. 바늘이 녹색 구간을 벗어나 있다.";
            return z switch
            {
                "low" => "다시 보니 바늘이 녹색 구간보다 왼쪽에 있다. 압력이 빠진 것이다.",
                "over" => "다시 보니 바늘이 녹색 구간보다 오른쪽에 있다. 압력이 너무 높은 것이다.",
                _ => "다시 보니 바늘은 녹색 구간 안에 있다.",
            };
        }

        /// <summary>
        /// The 3장 corridor extinguisher is the low one unless the office was told in 1장 (never checked, or checked and left).
        /// Low is weak and short, not hopeless: a good sweep on the base still just puts a small fire out.
        /// </summary>
        public static bool IsLow(GameState g) => g == null || !(g.Know.TryGetValue(Know, out var v) && v?.Type == JTokenType.String && (string)v == Reported);

        /// <summary>The check was done (either way).</summary>
        public static bool Checked(GameState g) => g != null && g.Know.TryGetValue(Know, out var v) && v?.Type == JTokenType.String;

        /// <summary>The screen's result read back: played, the reading picked and the action.</summary>
        public sealed class Outcome
        {
            public bool Played;
            public string Answer, Action;
            public double Needle = CorridorNeedle;
            public bool ReadOk => Answer == Zone(Needle);
            public bool Report => Action == "report";

            public static Outcome Read(JToken res)
            {
                var o = new Outcome();
                if (res is not JObject j) return o;
                o.Answer = j["answer"]?.Type == JTokenType.String ? (string)j["answer"] : null;
                o.Action = j["action"]?.Type == JTokenType.String ? (string)j["action"] : null;
                if (j["needle"] != null && (j["needle"].Type == JTokenType.Float || j["needle"].Type == JTokenType.Integer)) o.Needle = (double)j["needle"];
                o.Played = o.Answer != null && o.Action != null;
                return o;
            }

            public JObject ToJson() => new JObject
            {
                ["reading"] = Zone(Needle), ["answer"] = Answer, ["ok"] = ReadOk, ["action"] = Action,
            };
        }
    }
}
