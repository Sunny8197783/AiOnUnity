using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/lounge.test.mjs, plus parity with the original game/lounge.js
    /// (Fixtures/golden-lounge.json; regenerate with Tools/golden/make-golden-lounge.mjs).
    /// </summary>
    public class LoungeTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-lounge.json");

        static JObject Msg(string message, string ev = "message") =>
            new() { ["id"] = "a1", ["time"] = 1762050000, ["event"] = ev, ["message"] = message };

        static string Body(string json) => JObject.Parse(json).ToString(Formatting.None);

        [Test]
        public void Names_are_neighbour_labels_and_new_ones_always_pass_the_check()
        {
            foreach (var r in new[] { 0, 0.5, 0.9999 }) Assert.IsTrue(Lounge.IsName(Lounge.NewName(() => r)), $"rnd {r}");
            for (int i = 0; i < 50; i++) Assert.IsTrue(Lounge.IsName(Lounge.NewName()), "the default random source");
            foreach (var bad in new[] { "", "107동 이웃 1234", "103동 이웃 12", "103동 이웃 1234<b>", null }) Assert.IsFalse(Lounge.IsName(bad), bad);
        }

        [Test]
        public void Fixed_phrases_and_runs_round_trip_and_anything_else_is_dropped()
        {
            var m = Lounge.ReadMessage(Msg(Lounge.PhraseBody("103동 이웃 4821", "radio")));
            GoldenRuns.AssertGolden(m, new JObject { ["id"] = "a1", ["time"] = 1762050000, ["who"] = "103동 이웃 4821", ["text"] = Lounge.Phrases["radio"], ["run"] = false });
            Assert.AreEqual("엔딩 7에 닿았어요 · 도전 과제 7/13", Lounge.ReadMessage(Msg(Lounge.RunBody("101동 이웃 1000", "commons", 7))).Text);
            Assert.AreEqual("엔딩 7에 닿았어요 · 도전 과제 7/13", Lounge.ReadMessage(Msg(Lounge.RunBody("101동 이웃 1000", new RunRecord { Ending = "commons", N = 7 }))).Text);
            foreach (var body in new[]
            {
                "hi", "{}", Body("{v:1,who:'103동 이웃 4821',p:'free text'}"), Body("{v:1,who:'103동 이웃 4821',p:'toString'}"),
                Body("{v:1,who:'evil<script>',p:'radio'}"), Body("{v:2,who:'103동 이웃 4821',p:'radio'}"),
                Body("{v:1,who:'103동 이웃 4821',run:{e:'nope',c:1}}"), Body("{v:1,who:'103동 이웃 4821',run:{e:'bus',c:99}}"),
            }) Assert.IsNull(Lounge.ReadMessage(Msg(body)), body);
            Assert.IsNull(Lounge.ReadMessage(Msg(Lounge.PhraseBody("103동 이웃 4821", "radio"), "open")));
            Assert.IsNull(Lounge.ReadMessage(null));
            var lines = string.Join("\n", Msg(Lounge.PhraseBody("102동 이웃 2000", "hello")).ToString(Formatting.None), "garbage", "{\"event\":\"keepalive\"}", "");
            CollectionAssert.AreEqual(new[] { Lounge.Phrases["hello"] }, Lounge.ReadHistory(lines).Select(x => x.Text).ToList());
            Assert.IsEmpty(Lounge.ReadHistory(null));
        }

        // ---------- parity with the original ----------

        [Test]
        public void The_tables_are_the_originals()
        {
            Assert.AreEqual((string)Golden["relay"], Lounge.Relay);
            Assert.AreEqual((string)Golden["topic"], Lounge.Topic);
            GoldenRuns.AssertGolden(Lounge.Phrases, Golden["phrases"]);
            CollectionAssert.AreEqual(((JObject)Golden["phrases"]).Properties().Select(p => p.Name), Lounge.Phrases.Keys);
        }

        [Test]
        public void Names_are_drawn_and_checked_like_the_original()
        {
            foreach (var n in (JArray)Golden["names"])
            {
                var draws = new Queue<double>(n["draws"].Select(x => (double)x));
                Assert.AreEqual((string)n["name"], Lounge.NewName(draws.Dequeue), $"draws {n["draws"].ToString(Formatting.None)}");
                Assert.IsEmpty(draws, "two draws per name");
            }
            foreach (var c in (JArray)Golden["nameChecks"])
                // only a string can be a name: every other value is turned away before the check
                Assert.AreEqual((bool)c["ok"], c["v"].Type == JTokenType.String && Lounge.IsName((string)c["v"]), c["v"].ToString(Formatting.None));
        }

        [Test]
        public void Bodies_are_the_same_text_the_original_posts()
        {
            foreach (var b in (JArray)Golden["bodies"])
            {
                string who = (string)b["who"];
                string body = (string)b["fn"] == "phrase" ? Lounge.PhraseBody(who, (string)b["p"]) : Lounge.RunBody(who, (string)b["ending"], (long)b["n"]);
                Assert.AreEqual((string)b["body"], body);
            }
        }

        [Test]
        public void Every_golden_message_is_let_through_or_dropped_like_the_original()
        {
            var messages = (JArray)Golden["messages"];
            Assert.That(messages.Count, Is.GreaterThan(60));
            int through = 0;
            foreach (var x in messages)
            {
                var got = Lounge.ReadMessage(x["m"]);
                if (x["out"].Type == JTokenType.Null) Assert.IsNull(got, x["m"].ToString(Formatting.None));
                else { GoldenRuns.AssertGolden(got, x["out"], x["m"].ToString(Formatting.None)); through++; }
            }
            Assert.That(through, Is.GreaterThan(20));
            foreach (var h in (JArray)Golden["histories"])
                GoldenRuns.AssertGolden(Lounge.ReadHistory((string)h["text"]), h["out"], "history");
        }
    }
}
