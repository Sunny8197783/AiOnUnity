using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One relay message that passed every check: a fixed phrase, or a finished run.</summary>
    public sealed class LoungeMessage
    {
        [JsonProperty("id")] public string Id;
        /// <summary>Unix seconds, as the relay stamped it.</summary>
        [JsonProperty("time")] public long Time;
        [JsonProperty("who")] public string Who;
        /// <summary>Always one of our own sentences, never the sender's words.</summary>
        [JsonProperty("text")] public string Text;
        /// <summary>True for a posted run (ending number and challenge count).</summary>
        [JsonProperty("run")] public bool Run;
    }

    /// <summary>
    /// 대피소 라운지 (shelter lounge, original game/lounge.js): players talk in fixed phrases only, through a public
    /// relay (ntfy.sh, no account), and can post a finished run as its ending number and challenge count. Every
    /// received message is checked against these tables, so nothing another client sends can put its own words on
    /// screen. Pure: no UI, no network.
    /// </summary>
    public static class Lounge
    {
        public const string Relay = "https://ntfy.sh", Topic = "gyeongbo-ijeon-lounge-v1";

        public static readonly IReadOnlyDictionary<string, string> Phrases = new Dictionary<string, string>
        {
            ["hello"] = "안녕하세요, 연무동 이웃입니다.",
            ["cheer"] = "끝까지 함께 버텨요!",
            ["radio"] = "정전되면 라디오부터 켜 보세요.",
            ["rumor"] = "소문은 공식 안내로 먼저 확인해요.",
            ["ask"] = "돕기 전에 먼저 물어봐요.",
            ["elder"] = "304호 어르신을 잊지 마세요.",
            ["card"] = "비상연락카드에 만남의 장소를 적어 둬요.",
            ["retreat"] = "불 앞에서 물러나는 것도 좋은 판단이에요.",
            ["ending"] = "새 엔딩을 봤어요!",
            ["thanks"] = "알려 줘서 고마워요.",
        };

        /// <summary>ASCII digits only, nothing after the name.</summary>
        static readonly Regex Who = new(@"^10[1-6]동 이웃 [0-9]{4}\z");

        /// <summary>A neighbour name, e.g. '103동 이웃 4821'.</summary>
        public static bool IsName(string s) => s != null && Who.IsMatch(s);

        /// <summary>A neighbour name for this player. rnd: () => [0, 1); two draws.</summary>
        public static string NewName(Func<double> rnd = null)
        {
            if (rnd == null) { var random = new Random(); rnd = random.NextDouble; }
            long dong = 101 + (long)Math.Floor(rnd() * 6);
            long number = 1000 + (long)Math.Floor(rnd() * 9000);
            return $"{dong.ToString(CultureInfo.InvariantCulture)}동 이웃 {number.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>The body to post for a fixed phrase (p: a key of <see cref="Phrases"/>).</summary>
        public static string PhraseBody(string who, string p) =>
            new JObject { ["v"] = 1, ["who"] = who, ["p"] = p }.ToString(Formatting.None);

        /// <summary>The body to post for a finished run: its ending id and how many challenges it earned.</summary>
        public static string RunBody(string who, string ending, long n) =>
            new JObject { ["v"] = 1, ["who"] = who, ["run"] = new JObject { ["e"] = ending, ["c"] = n } }.ToString(Formatting.None);

        public static string RunBody(string who, RunRecord run) => RunBody(who, run.Ending, run.N);

        /// <summary>One relay message ({ id, time, event, message }) -> what to show, or null when anything is off.</summary>
        public static LoungeMessage ReadMessage(JToken m)
        {
            if (m is not JObject o || !JsSem.Is(o["event"], "message") || JsSem.Str(o["message"]) is not string body
                || JsSem.Str(o["id"]) is not string id || !JsSem.IsInt(o["time"]) || Math.Abs(JsSem.Number(o["time"])) >= 9e15) return null;
            if (JsSem.Parse(body) is not JObject b || !JsSem.IsNumber(b["v"]) || JsSem.Number(b["v"]) != 1 || JsSem.Str(b["who"]) is not string who || !IsName(who)) return null;
            long time = (long)JsSem.Number(o["time"]);
            if (JsSem.Str(b["p"]) is string p && Phrases.TryGetValue(p, out var phrase))
                return new LoungeMessage { Id = id, Time = time, Who = who, Text = phrase, Run = false };
            var run = b["run"] as JObject;
            var e = Endings.ById(JsSem.Str(run?["e"]));
            var c = run?["c"];
            if (e != null && JsSem.IsInt(c) && JsSem.Number(c) >= 0 && JsSem.Number(c) <= Challenges.All.Count)
                return new LoungeMessage
                {
                    Id = id, Time = time, Who = who, Run = true,
                    Text = $"엔딩 {e.No.ToString(CultureInfo.InvariantCulture)}에 닿았어요 · 도전 과제 {JsSem.NumText(JsSem.Number(c))}/{Challenges.All.Count.ToString(CultureInfo.InvariantCulture)}",
                };
            return null;
        }

        /// <summary>The relay's poll answer is one JSON object per line; lines that are not messages are skipped.</summary>
        public static List<LoungeMessage> ReadHistory(string text) =>
            (text ?? "").Split('\n').Select(l => ReadMessage(JsSem.Parse(l))).Where(x => x != null).ToList();
    }
}
