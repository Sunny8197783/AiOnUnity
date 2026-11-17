using System;
using System.Linq;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class LoungeLogicTests
    {
        static readonly TimeZoneInfo Seoul = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");

        [Test]
        public void TimeText_KoreanTwoDigitHours()
        {
            // 2026-01-01 06:05:00 UTC = 15:05 KST
            long t = new DateTimeOffset(2026, 1, 1, 6, 5, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            Assert.That(LoungeLogic.TimeText(t, Seoul), Is.EqualTo("오후 03:05"));
            Assert.That(LoungeLogic.TimeText(t - 15 * 3600, Seoul), Is.EqualTo("오전 12:05"));
            Assert.That(LoungeLogic.TimeText(t - 3 * 3600, Seoul), Is.EqualTo("오후 12:05"));
            Assert.That(LoungeLogic.TimeText(t - 6 * 3600, Seoul), Is.EqualTo("오전 09:05"));
        }

        [Test]
        public void Parse_ReadsNameBestTopic()
        {
            var a = LoungeLogic.Parse(JObject.Parse(@"{ ""name"": ""103동 이웃 4821"", ""best"": { ""ending"": ""reopen"", ""n"": 4, ""ms"": 1 } }"));
            Assert.That(a.Name, Is.EqualTo("103동 이웃 4821"));
            Assert.That(a.Best.Ending, Is.EqualTo("reopen"));
            Assert.That(a.Best.N, Is.EqualTo(4));
            Assert.That(a.Topic, Is.EqualTo(Lounge.Topic));
            var b = LoungeLogic.Parse(JObject.Parse(@"{ ""best"": null, ""topic"": ""t2"" }"));
            Assert.That(b.Best, Is.Null);
            Assert.That(b.Topic, Is.EqualTo("t2"));
            Assert.That(LoungeLogic.Parse(null).Name, Is.Null);
        }

        [Test]
        public void EnsureName_KeepsValidMakesNew()
        {
            Assert.That(LoungeLogic.EnsureName("101동 이웃 1234", null, out var c1), Is.EqualTo("101동 이웃 1234"));
            Assert.That(c1, Is.False);
            var n = LoungeLogic.EnsureName("bad", () => 0.5, out var c2);
            Assert.That(c2, Is.True);
            Assert.That(Lounge.IsName(n), Is.True);
            Assert.That(n, Is.EqualTo("104동 이웃 5500"));
        }

        [Test]
        public void Urls()
        {
            Assert.That(LoungeLogic.TopicUrl("t"), Is.EqualTo("https://ntfy.sh/t"));
            Assert.That(LoungeLogic.PollUrl("t"), Is.EqualTo("https://ntfy.sh/t/json?poll=1&since=12h"));
            Assert.That(LoungeLogic.PollUrl("t", "abc"), Is.EqualTo("https://ntfy.sh/t/json?poll=1&since=abc"));
        }

        static LoungeMessage M(string id, string who = "101동 이웃 1111", bool run = false) =>
            new LoungeMessage { Id = id, Who = who, Text = "x", Time = 0, Run = run };

        [Test]
        public void Feed_DedupesCapsAndMarks()
        {
            var feed = new LoungeFeed("102동 이웃 2222", Seoul);
            Assert.That(feed.Add(null), Is.False);
            Assert.That(feed.Add(M("a")), Is.True);
            Assert.That(feed.Add(M("a")), Is.False);
            feed.Add(M("b", "102동 이웃 2222"));
            feed.Add(M("c", "102동 이웃 2222", run: true));
            feed.Add(M("d", run: true));
            Assert.That(feed.Rows.Select(r => r.Class), Is.EqualTo(new[] { "", "mine", "run mine", "run" }));
            Assert.That(feed.Rows[0].Time, Is.EqualTo("오전 09:00"));
            for (int i = 0; i < 70; i++) feed.Add(M("m" + i));
            Assert.That(feed.Rows.Count, Is.EqualTo(LoungeFeed.Max));
            Assert.That(feed.Rows.Last().Id, Is.EqualTo("m69"));
            Assert.That(feed.LastId, Is.EqualTo("m69"));
            Assert.That(feed.Add(M("a")), Is.False); // still remembered after it scrolled out
        }

        [Test]
        public void Feed_FromRelayHistory()
        {
            var body = Lounge.PhraseBody("101동 이웃 1111", "hello");
            var line = new JObject { ["id"] = "q1", ["time"] = 1700000000, ["event"] = "message", ["message"] = body }.ToString(Newtonsoft.Json.Formatting.None);
            var feed = new LoungeFeed("x", Seoul);
            foreach (var m in Lounge.ReadHistory(line + "\n{\"event\":\"open\"}\n" + line)) feed.Add(m);
            Assert.That(feed.Rows.Count, Is.EqualTo(1));
            Assert.That(feed.Rows[0].Text, Is.EqualTo("안녕하세요, 연무동 이웃입니다."));
        }

        [Test]
        public void Cooldown()
        {
            Assert.That(LoungeLogic.CanSend(0, double.NegativeInfinity), Is.True);
            Assert.That(LoungeLogic.CanSend(10, 6), Is.False);
            Assert.That(LoungeLogic.CanSend(11, 6), Is.True);
            Assert.That(LoungeLogic.NameLine("a"), Is.EqualTo("내 이름: a · 정해진 문구만 보낼 수 있습니다."));
        }
    }
}
