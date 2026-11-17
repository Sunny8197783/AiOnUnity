// SeoyeonRelay (story/seoyeon.js, "같은 문장, 다른 사람"): scripted runs on FakeStoryHost compared with the ORIGINAL
// scene run under node on the same scripts (Tools/golden/make-golden-story-seoyeon.mjs ->
// Fixtures/story-seoyeon-golden.json), where ui/compose.js and notecheck.js were scripted stand-ins:
//   chips     contact, a chip message (gate + time + route + follow me), every re-read caption, the notice -> C, C.
//   nocontact no number: no message; east is closed, A -> ask the guide to walk to C, C.
//   edited    a dying phone saved by the powerbank, a free-text message, Esc on the second caption, notice -> A, B -> C, C.
//   deadphone battery 0 and no powerbank: no message; straight to C.
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
    public class SeoyeonRelayTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(TestSupport.Fixture("story-seoyeon-golden.json"));

        sealed class Script
        {
            public bool Contact, Powerbank;
            public double Battery;
            public JObject Compose;
            public string[] Rereads = Array.Empty<string>(), Card = Array.Empty<string>(), Talk = Array.Empty<string>(), Steps = Array.Empty<string>();
        }

        static JObject Chips(string what, bool time, bool route, bool unknowns, string advice) => new()
        {
            ["what"] = what, ["time"] = time, ["route"] = route, ["unknowns"] = unknowns, ["advice"] = advice == null ? JValue.CreateNull() : advice,
        };

        // The same scripts as make-golden-story-seoyeon.mjs.
        static readonly Dictionary<string, Script> Scripts = new()
        {
            ["chips"] = new Script
            {
                Contact = true, Battery = 40,
                Compose = new JObject { ["chips"] = Chips("gate_closed", true, true, false, "follow_me"), ["text"] = "", ["edited"] = false },
                Rereads = new[] { "next", "next", "next", "next", "next", "next" },
                Card = new[] { "ok", "access", "ok" }, Steps = new[] { "shelterNotice", "shelterC" },
            },
            ["nocontact"] = new Script { Contact = false, Battery = 40, Card = new[] { "ok", "ok", "ok", "ok" }, Talk = new[] { "escort" }, Steps = new[] { "shelterEast", "shelterA", "shelterC" } },
            ["edited"] = new Script
            {
                Contact = true, Battery = 2, Powerbank = true,
                Compose = new JObject { ["chips"] = Chips("all_closed", false, false, false, null), ["text"] = "다 막혔어. 나 따라 계단으로 와", ["edited"] = true },
                Rereads = new[] { "next", "skip", "next" },
                Card = new[] { "ok", "stairs", "ok" }, Talk = new[] { "self" }, Steps = new[] { "shelterNotice", "shelterB", "shelterC" },
            },
            ["deadphone"] = new Script { Contact = true, Battery = 0, Card = new[] { "ok", "ok", "ok", "ok" }, Steps = new[] { "shelterC" } },
        };

        static (FakeStoryHost host, StoryContext ctx) Play(string name)
        {
            var s = Scripts[name];
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            var g = host.G;
            g.Chapter = "dday"; g.Day = 0; g.Minute = 20 * 60 + 9;
            GameRules.RecordEvent(g, "gate_observed", new JObject { ["who"] = "jeongmin", ["minute"] = 20 * 60 + 9, ["via"] = "sign" });
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "jeongmin", ["route"] = "stairs", ["minute"] = 20 * 60 + 11 });
            g.Minute = 20 * 60 + 12;
            if (s.Contact) g.Rel["seoyeon"].Contact = "phone";
            g.Phone.Battery = s.Battery;
            if (s.Powerbank) g.Items["powerbank"] = new ItemEntry { Bag = 1, Bought = 1, Checked = true };
            var players = new List<string>();
            ctx.SetPlayer = who => players.Add(who);
            if (s.Compose != null) host.AnswerOpen("compose", s.Compose);
            if (s.Rereads.Length > 0) { host.AnswerOpen("reread", null); host.AnswerOpen("reread", null); }
            foreach (var r in s.Rereads) host.AnswerOpen("rereadCaption", r);
            host.AnswerCard(s.Card);
            if (s.Talk.Length > 0) host.Answer(s.Talk);
            foreach (var st in s.Steps) host.QueuePress("town:" + st);
            host.Run(async () => { await ctx.Goto("shelter", "stairs"); await SeoyeonRelay.Run(ctx); });
            Assert.That(host.PendingSteps + host.PendingTalkAnswers + host.PendingCardAnswers + host.PendingOpenAnswers, Is.EqualTo(0), "every scripted step and answer was used");
            Assert.That(players, Is.EqualTo(new[] { "seoyeon", "jeongmin" }));
            return (host, ctx);
        }

        static bool Same(JToken a, JToken b)
        {
            bool Nullish(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;
            if (Nullish(a) || Nullish(b)) return Nullish(a) && Nullish(b);
            bool Num(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
            if (Num(a) && Num(b)) return Math.Abs((double)a - (double)b) < 1e-9;
            if (a is JObject oa && b is JObject ob)
            {
                var keys = oa.Properties().Select(x => x.Name).Union(ob.Properties().Select(x => x.Name));
                return keys.All(k => Same(oa[k], ob[k]));
            }
            if (a is JArray aa && b is JArray ab) return aa.Count == ab.Count && aa.Zip(ab, Same).All(x => x);
            return JToken.DeepEquals(a, b);
        }

        static void AssertSame(JToken want, JToken got, string what) =>
            Assert.That(Same(want, got), Is.True, $"{what}\n  want: {want?.ToString(Newtonsoft.Json.Formatting.None)}\n  got:  {got?.ToString(Newtonsoft.Json.Formatting.None)}");

        /// <summary>The note panel changes, as the original's showNote(mode) / hideNote() calls.</summary>
        static JArray Notes(FakeStoryHost host)
        {
            var a = new JArray();
            foreach (var (screen, o) in host.Opens.Where(x => x.screen == "rereadCaption" || x.screen == "reread"))
                if (o.TryGetValue("note", out var n)) a.Add(n is JObject no ? (string)no["mode"] : "hide");
            return a;
        }

        /// <summary>The 3D calls (createReread), flattened like the stand-in records them.</summary>
        static JArray Fx(FakeStoryHost host)
        {
            var a = new JArray();
            foreach (var (screen, o) in host.Opens.Where(x => x.screen == "rereadCaption" || x.screen == "reread"))
                foreach (JObject f in (JArray)o["fx"])
                    switch ((string)f["op"])
                    {
                        case "start": a.Add(new JArray("start", f["start"]["x"], f["start"]["z"])); break;
                        case "eye": a.Add(new JArray("eye", f["pitch"])); break;
                        case "crane": a.Add(new JArray("crane")); break;
                        case "path": a.Add(new JArray("path", ((JArray)f["trail"]).Count)); break;
                        case "pin": a.Add(new JArray("pin", f["label"], f["at"]["x"], f["at"]["z"], f["was"], f["now"], f["scale"] ?? JValue.CreateNull())); break;
                        case "link": a.Add(new JArray("link", f["from"]["x"], f["from"]["z"], f["to"]["x"], f["to"]["z"])); break;
                        case "dispose": a.Add(new JArray("dispose")); break;
                    }
            return a;
        }

        [TestCase("chips")]
        [TestCase("nocontact")]
        [TestCase("edited")]
        [TestCase("deadphone")]
        public void MatchesTheOriginal(string name)
        {
            var w = (JObject)Golden[name];
            var (host, ctx) = Play(name);
            var g = host.G;
            Assert.That(host.EventTypes(), Is.EqualTo(w["events"].Select(e => (string)e[0]).ToList()), "event types");
            for (int i = 0; i < g.Events.Count; i++) AssertSame(w["events"][i][1], g.Events[i].Details, $"event #{i} {g.Events[i].Type}");
            AssertSame(w["relay"], g.Extra["relay"], "G.relay");
            var hand = g.Extra.TryGetValue("handover", out var h) ? h : null;
            Assert.That(Same(w["handover"], hand) || (w["handover"].Type == JTokenType.Object && new[] { "text", "kind", "edited", "actualRoute", "observedVia", "gateSeenAt", "sentAt", "accessOpenAt", "receivedAt" }
                .All(k => Same(w["handover"][k], hand?[k]))), Is.True, $"G.handover\n  want: {w["handover"]}\n  got:  {hand}");
            Assert.That(g.Minute, Is.EqualTo((int)w["minute"]), "minute");
            Assert.That(g.Phone.Battery, Is.EqualTo((double)w["battery"]), "battery");
            if (w["powerbank"].Type == JTokenType.Object)
                Assert.That(new[] { g.Items["powerbank"].Home, g.Items["powerbank"].Bag, g.Items["powerbank"].Used }, Is.EqualTo(new[] { (int)w["powerbank"]["home"], (int)w["powerbank"]["bag"], (int)w["powerbank"]["used"] }), "powerbank");
            Assert.That(g.Info.Select(i => new[] { i.Text, i.Label }).ToList(), Is.EqualTo(w["info"].Select(i => new[] { (string)i[0], (string)i[1] }).ToList()), "info notes");
            Assert.That(host.Toasts, Is.EqualTo(w["toasts"].Select(x => (string)x).ToList()), "toasts");
            AssertSame(w["talks"], new JArray(host.Talks.Select(t => new JArray(t.Speaker, t.Role ?? "", new JArray(t.Lines)))), "talks");
            AssertSame(w["cards"], new JArray(host.Cards.Select(c => new JArray(c.Title, c.Meta, new JArray(c.Body)))), "cards");
            AssertSame(w["chapters"], new JArray(host.Calls("chapter").Select(c => new JArray(c.Text, (string)c.Data))), "chapters");
            AssertSame(w["views"], new JArray(host.Calls("view").Select(c => c.Text)), "views");
            AssertSame(w["cinematic"], new JArray(host.Calls("cinematic").Select(c => c.Text == "on")), "cinematic");
            AssertSame(w["composeOpened"], host.Opens.Where(o => o.screen == "compose").Select(o => o.options).FirstOrDefault(), "compose options");
            var caps = new JArray(host.Opens.Where(o => o.screen == "rereadCaption").Select(o =>
                new JArray(o.options["text"], o.options["pin"] is JObject p ? new JArray(p["factor"], p["anchor"], p["was"], p["now"]) : JValue.CreateNull(), o.options["last"])));
            AssertSame(w["captions"], caps, "re-read captions");
            AssertSame(w["notes"], Notes(host), "note panel");
            AssertSame(w["fx"], Fx(host), "3D re-read");
            Assert.That(ctx.Calm, Is.EqualTo((bool)w["calm"]), "calm");
            Assert.That(ctx.SpeedOverride, Is.Null);
        }

        [Test]
        public void Chips_KeyState()
        {
            var (host, ctx) = Play("chips");
            var g = host.G;
            var h = Relay.StoredHandover(g);
            Assert.That(h.Kind, Is.EqualTo("partial"));
            Assert.That(h.SentAt, Is.EqualTo("20:14")); Assert.That(h.ReceivedAt, Is.EqualTo("20:18"));
            Assert.That(host.Level("town").State["gates"]["access"].ToString(), Is.EqualTo("open"));
            Assert.That(host.Level("town").State["crowd"].ToString(), Is.EqualTo("evacuating"));
            Assert.That(host.Chapter, Is.EqualTo(("2장", "정전과 경보")), "back to 정민's chapter title");
            Assert.That(host.Cinematic, Is.False);
            Assert.That(host.ControlsEnabled, Is.True);
            Assert.That(host.Toasts, Has.Member("C 경사로 · 20:17에 열림 · 정민이 문자를 보낸 20:14 뒤의 일"));
            Assert.That(host.Cards.First(c => c.Title == "시점 전환 · 오서연").Body.Skip(1), Is.EqualTo(PrepActions.SeoyeonCard.Select(l => $"· {l}")));
            Assert.That(host.Calls("enter").Select(c => c.Text), Has.Member("town:cafe"));
            Assert.That(g.Events.Last().Type, Is.EqualTo("route_attempt"));
        }

        [Test]
        public void CalmHoldsWhileSheWalks_AndTheSpeedIsHers()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules);
            host.G.Minute = 20 * 60 + 12;
            host.AnswerCard("ok", "ok", "ok", "ok");
            bool calm = false; double? speed = null;
            host.Queue(h => { calm = ctx.Calm; speed = ctx.SpeedOverride; h.Press("shelterC"); }, () => ctx.Roaming && host.LevelId == "town", "C");
            host.Run(() => SeoyeonRelay.Run(ctx));
            Assert.That(calm, Is.True); Assert.That(speed, Is.EqualTo(0.55));
            Assert.That(ctx.Calm, Is.False); Assert.That(ctx.SpeedOverride, Is.Null);
            Assert.That(host.Cards[0].Title, Is.EqualTo("서연에게 연락할 방법이 없다"));
            Assert.That(host.PlayerX, Is.EqualTo(-118)); // still at the cafe: walking was not needed to press
        }
    }
}
