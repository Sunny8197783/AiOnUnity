// DDay against the ORIGINAL story/dday.js: Tools/golden/make-golden-story-dday.mjs played the same two scripts on
// the original (with the same stubs for seoyeon/elder/cutscenes) and wrote Fixtures/story-dday-golden.json.
// Here the scripts are replayed on DDay.Run + FakeStoryHost and every recorded event (type and details), the
// payoffs, deeds, know flags, item counts, clock, trust and screens opened must match. 서연's part is the same stand-in
// on both sides (DDayPlaythroughTests.StandInSeoyeon).
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DDayGoldenTests
    {
        static JObject Golden => JObject.Parse(TestSupport.Fixture("story-dday-golden.json"));

        static bool Ready(FakeStoryHost h, StoryContext c, string level) => c.Roaming && h.ControlsEnabled && h.LevelId == level;

        static (FakeStoryHost, StoryContext) Rig(Action<GameRules, GameState> setup)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var g = rules.CreateGame(7);
            g.Chapter = "prep"; g.Day = -1;
            setup(rules, g);
            var ctx = host.CreateContext(rules, g);
            ctx.Cine = new StoryCine(ctx);
            return (host, ctx);
        }

        static void Steps(FakeStoryHost h, StoryContext c, params (string level, string id)[] presses)
        {
            foreach (var (level, id) in presses)
            {
                if (id == "@downstairs") h.Queue(x => { x.MoveTo(5, 0.9, 0); x.RunFrame(); }, () => Ready(h, c, "apartment") && h.PlayerY > 4, "downstairs");
                else if (id.StartsWith("@strike:")) h.Queue(x => x.Press(id.Substring(8)), () => Ready(h, c, "town") && h.G.Knows("strikeSeen"), id);
                else h.Queue(x => x.Press(id), () => Ready(h, c, level), $"press {level}:{id}");
            }
        }

        static void Compare(JObject want, FakeStoryHost host)
        {
            var g = host.G;
            var got = g.Events.Select(e => new JArray(e.Type, e.Details)).ToList();
            var w = (JArray)want["events"];
            Assert.That(got.Select(e => (string)e[0]), Is.EqualTo(w.Select(e => (string)e[0])), "event types");
            for (int i = 0; i < w.Count; i++)
                Assert.That(JToken.DeepEquals(got[i][1], w[i][1]), Is.True, $"event {i} {w[i][0]}: want {w[i][1].ToString(Newtonsoft.Json.Formatting.None)} got {got[i][1].ToString(Newtonsoft.Json.Formatting.None)}");
            Assert.That(g.Events.Where(e => e.Type == "payoff").Select(e => (string)e.Details["prep"]), Is.EqualTo(want["payoffs"].Select(x => (string)x)));
            Assert.That(g.Events.Where(e => e.Type == "deed").Select(e => (string)e.Details["id"]), Is.EqualTo(want["deeds"].Select(x => (string)x)));
            foreach (var p in ((JObject)want["know"]).Properties())
            {
                bool wv = p.Value.Type == JTokenType.Boolean ? (bool)p.Value : p.Value.Type != JTokenType.Null;
                Assert.That(g.Knows(p.Name), Is.EqualTo(wv), $"know.{p.Name}");
            }
            foreach (var p in ((JObject)want["items"]).Properties())
            {
                Assert.That(g.Items.ContainsKey(p.Name), Is.True, p.Name);
                var e = g.Items[p.Name];
                Assert.That(new[] { e.Home, e.Bag, e.Used }, Is.EqualTo(p.Value.Select(x => (int)x)), $"items.{p.Name}");
            }
            Assert.That(g.Items.Keys.OrderBy(k => k), Is.EqualTo(((JObject)want["items"]).Properties().Select(p => p.Name).OrderBy(k => k)));
            Assert.That(g.Minute, Is.EqualTo((int)want["minute"]));
            Assert.That(g.Rel["seoyeon"].Trust, Is.EqualTo((double)want["trust"]));
            Assert.That(host.Opens.Select(o => o.screen), Is.EqualTo(want["opens"].Select(x => (string)x)));
        }

        [Test]
        public void Prepared_MatchesTheOriginal()
        {
            var (host, ctx) = Rig((rules, g) =>
            {
                g.Rel["seoyeon"].Contact = true; g.Rel["manager"].Contact = true;
                g.Items["flashlight"].Checked = true;
                g.Items["radio"] = new ItemEntry(1) { Checked = true, Charge = 100 };
                foreach (var k in new[] { "radioChecked", "routeChecked", "idTaken", "bagPacked" }) g.SetKnow(k, true);
                foreach (var (id, home, bag) in new[] { ("water_500", 3, 2), ("contact_card", 1, 1), ("blanket", 1, 1) }) { g.Items[id] = new ItemEntry(home); rules.Pack(g, id, bag); }
            });
            host.AnswerSpeaker("정전", "hall"); host.AnswerSpeaker("정전", "radio"); host.AnswerSpeaker("3층 복도", "tip");
            host.AnswerOpen("radio", JObject.Parse("{ official: true, noted: [] }"));
            host.AnswerSpeaker("접수대", "ask"); host.AnswerSpeaker("옆자리", "give"); host.AnswerSpeaker("오서연", "ask");
            host.Queue(_ => { }, () => ctx.Roaming && host.G.Events.Any(e => e.Type == "alert"), "alert");
            Steps(host, ctx, ("apartment", "homeExitDoor"), ("apartment", "@downstairs"), ("apartment", "managerWindow"), ("apartment", "lobbyExit"),
                ("town", "@strike:shelterA"), ("shelter", "reception"), ("shelter", "spot"));
            DDayPlaythroughTests.Play(host, ctx);
            Compare((JObject)Golden["prepared"], host);
        }

        [Test]
        public void Unprepared_MatchesTheOriginal()
        {
            var (host, ctx) = Rig((rules, g) =>
            {
                g.Items["candles"] = new ItemEntry(1); g.Items["water_500"] = new ItemEntry(1); g.Items.Remove("radio");
            });
            host.AnswerSpeaker("정전", "candle"); host.AnswerSpeaker("정전", "phone"); host.AnswerSpeaker("현관", "pack");
            host.AnswerOpen("bag", JObject.Parse("{ bag: { water_500: 1, water_2l: 1, flashlight: 1 }, pocket: {} }"));
            host.AnswerSpeaker("거실", "leave"); host.AnswerSpeaker("접수대", "go"); host.AnswerSpeaker("옆자리", "keep"); host.AnswerSpeaker("오서연", "go");
            host.Queue(_ => { }, () => ctx.Roaming && host.G.Events.Any(e => e.Type == "alert"), "alert");
            Steps(host, ctx, ("apartment", "homeBag"), ("apartment", "homeExitDoor"), ("apartment", "@downstairs"), ("apartment", "elevator"), ("apartment", "managerWindow"),
                ("apartment", "lobbyExit"), ("town", "closure_yeonmuCenter"), ("town", "aptNotice"), ("town", "shelterEast"), ("town", "shelterC"),
                ("town", "@strike:shelterB"), ("shelter", "spot"), ("shelter", "reception"), ("shelter", "spot"));
            DDayPlaythroughTests.Play(host, ctx);
            Compare((JObject)Golden["unprepared"], host);
        }
    }
}
