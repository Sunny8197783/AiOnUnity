// Pure parts of the 대피소 라운지 panel (original ui/lounge.js): option parsing, the lounge name, relay URLs, the feed
// (dedupe, 60-row cap, mine/run classes), the ko-KR time label, the 5 s send cooldown and the status lines.
// No Unity here, so the .NET harness tests it. Rules about messages themselves stay in AiOn.Game.Lounge.
using System;
using System.Collections.Generic;
using System.Globalization;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>lounge({ name, best, topic }) options.</summary>
    public sealed class LoungeArgs
    {
        public string Name;
        /// <summary>The top run of the records ({ ending, n }) or null.</summary>
        public RunRecord Best;
        public string Topic = Lounge.Topic;
    }

    /// <summary>A feed row as drawn.</summary>
    public sealed class LoungeRow
    {
        public string Id, Who, Text, Time, Class;
    }

    /// <summary>The feed: messages in arrival order, each id once, at most 60 rows.</summary>
    public sealed class LoungeFeed
    {
        public const int Max = 60;
        readonly HashSet<string> _seen = new();
        readonly string _me;
        readonly TimeZoneInfo _zone;
        public readonly List<LoungeRow> Rows = new();
        /// <summary>The id of the newest message added (the next poll asks for messages after it).</summary>
        public string LastId { get; private set; }

        public LoungeFeed(string me, TimeZoneInfo zone = null) { _me = me; _zone = zone ?? TimeZoneInfo.Local; }

        /// <summary>Adds a message; false when it is null or already shown.</summary>
        public bool Add(LoungeMessage msg)
        {
            if (msg == null || msg.Id == null || _seen.Contains(msg.Id)) return false;
            _seen.Add(msg.Id);
            var cls = string.Join(" ", new[] { msg.Run ? "run" : "", msg.Who == _me ? "mine" : "" }).Trim();
            Rows.Add(new LoungeRow { Id = msg.Id, Who = msg.Who, Text = msg.Text, Time = LoungeLogic.TimeText(msg.Time, _zone), Class = cls });
            while (Rows.Count > Max) Rows.RemoveAt(0);
            LastId = msg.Id;
            return true;
        }
    }

    public static class LoungeLogic
    {
        public const string NameKey = "gyeongbo.lounge.v1";
        /// <summary>One message per 5 s from this panel.</summary>
        public const double SendCooldown = 5;
        /// <summary>No EventSource in Unity: the panel polls the relay this often instead of the live stream.</summary>
        public const double PollEvery = 4;

        public const string Connecting = "라운지에 연결하는 중…";
        public const string Connected = "연결됨 · 정해진 문구로만 이야기합니다.";
        public const string Offline = "라운지에 연결할 수 없습니다. 인터넷 연결을 확인해 주세요. 게임은 그대로 할 수 있습니다.";
        public const string SendFailed = "보내지 못했습니다. 잠시 뒤 다시 해 주세요.";
        public const string Note = "대화는 공개 중계 서버(ntfy.sh)를 거쳐 누구나 볼 수 있고, 12시간 뒤 사라집니다. 이름과 문구 말고는 아무것도 보내지 않습니다.";

        public static string NameLine(string name) => $"내 이름: {name} · 정해진 문구만 보낼 수 있습니다.";

        /// <summary>loungeName(store): the stored name if valid, else a new one (save it under NameKey).</summary>
        public static string EnsureName(string stored, Func<double> rnd, out bool created)
        {
            created = !Lounge.IsName(stored);
            return created ? Lounge.NewName(rnd) : stored;
        }

        public static LoungeArgs Parse(JObject o)
        {
            o ??= new JObject();
            var a = new LoungeArgs { Name = JsSem.Str(o["name"]) };
            if (JsSem.Str(o["topic"]) is string t && t.Length > 0) a.Topic = t;
            if (o["best"] is JObject b && JsSem.Str(b["ending"]) is string e)
                a.Best = new RunRecord { Ending = e, N = JsSem.IsNumber(b["n"]) ? (long)JsSem.Number(b["n"]) : 0 };
            return a;
        }

        public static string TopicUrl(string topic) => $"{Lounge.Relay}/{topic}";

        /// <summary>The history poll (last 12 hours), or the messages after <paramref name="sinceId"/>.</summary>
        public static string PollUrl(string topic, string sinceId = null) =>
            $"{TopicUrl(topic)}/json?poll=1&since={(string.IsNullOrEmpty(sinceId) ? "12h" : Uri.EscapeDataString(sinceId))}";

        /// <summary>new Date(sec * 1000).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' }) -> "오후 03:05".</summary>
        public static string TimeText(long unixSeconds, TimeZoneInfo zone = null)
        {
            var t = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), zone ?? TimeZoneInfo.Local);
            int h = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
            return $"{(t.Hour < 12 ? "오전" : "오후")} {h.ToString("00", CultureInfo.InvariantCulture)}:{t.Minute.ToString("00", CultureInfo.InvariantCulture)}";
        }

        /// <summary>Buttons are disabled from a send until SendCooldown seconds later.</summary>
        public static bool CanSend(double now, double lastSend) => double.IsNegativeInfinity(lastSend) || now - lastSend >= SendCooldown;
    }
}
