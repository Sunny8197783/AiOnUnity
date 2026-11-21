// Replays the scripts of Tools/golden/make-golden-story-evac.mjs (the ORIGINAL story/evac.js run under node) on
// Evac + FakeStoryHost and compares what happened: G.events (type, day, minute, details), toasts, captions, the
// talks with the answer given, the last objective, the path and the fail.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class EvacGoldenTests
    {
        // 「임시 거처의 하루」 (a port addition after the move decision) is left out here: these runs are the original
        // chapter's (ShelterLifeTests plays the day itself).
        Func<StoryContext, System.Threading.Tasks.Task> _shelterLife;
        [SetUp] public void LeaveOutTheShelterDay() { _shelterLife = Evac.ShelterLifePart; Evac.ShelterLifePart = null; }
        [TearDown] public void PutTheShelterDayBack() => Evac.ShelterLifePart = _shelterLife;

        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(TestSupport.Fixture("story-evac-golden.json"));

        sealed class Script
        {
            public string Labels;
            public Dictionary<string, string[]> Speakers = new();
            public string[] Cards = new string[0];
            public Dictionary<string, JToken[]> Opens = new();
            public string[] Presses = new string[0];
            public bool At1400;
        }

        static void Fresh(GameState g)
        {
            g.Chapter = "endure"; g.Day = 3; g.Minute = 18 * 60;
            g.Items["water_2l"] = new ItemEntry(6);
            g.Items["canned"] = new ItemEntry(4);
            g.Items["rice_instant"] = new ItemEntry(2);
        }

        static void Kit(GameState g)
        {
            Fresh(g);
            g.Chapter = "crossroads"; g.Day = 7; g.Minute = 23 * 60;
            g.SetKnow("stayMove", "stay"); g.SetKnow("bagPacked", true);
            foreach (var id in new[] { "id_card", "contact_card", "doc_copies", "water_500", "bars", "flashlight", "meds" }) g.Items[id] = new ItemEntry(2);
        }

        static JObject FullBag => JObject.Parse("{ bag: { id_card: 1, contact_card: 1, doc_copies: 1, water_500: 2, bars: 2, flashlight: 1, meds: 1 }, pocket: { contact_card: 1 } }");

        static bool IsLabels(TalkOptions o) => o.Choices != null && o.Choices.Any(c => c.Id == "neighbor") && o.Choices.Any(c => c.Id == "official");

        static JObject Play(int seed, Action<GameState> setup, string[] chapters, Script s)
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(seed));
            setup(host.G);
            var speakers = s.Speakers.ToDictionary(kv => kv.Key, kv => new Queue<string>(kv.Value));
            host.TalkPolicy = o =>
            {
                if (IsLabels(o)) return s.Labels;
                if (speakers.TryGetValue(o.Speaker, out var q) && q.Count > 0) return q.Dequeue();
                return o.Choices.FirstOrDefault(c => !c.Disabled)?.Id;
            };
            host.AnswerCard(s.Cards);
            foreach (var kv in s.Opens) foreach (var r in kv.Value) host.AnswerOpen(kv.Key, r);
            foreach (var id in s.Presses)
            {
                if (id == "closure_ROAD") host.Queue(h => h.Press($"closure_{Rumors.Rumor("evac_shortcut").Road(h.G)}"), () => host.Ctx.Roaming && host.ControlsEnabled, "press closure");
                else host.QueuePress(id);
            }
            if (s.At1400) host.Queue(h => { h.G.Minute = 14 * 60; h.RunFrame(); }, () => host.Ctx.Roaming && host.ControlsEnabled, "14:00");
            JToken fail = null;
            try
            {
                host.Run(async () =>
                {
                    foreach (var c in chapters)
                        if (c == "crossroads") await Evac.Crossroads(ctx); else await Evac.Evacuation(ctx);
                });
            }
            catch (StoryFail f) { fail = new JObject { ["type"] = f.Type, ["detail"] = f.Detail }; }
            var talks = new JArray();
            for (int i = 0; i < host.Log.Count; i++)
            {
                if (host.Log[i].Kind != "talk") continue;
                var o = (TalkOptions)host.Log[i].Data;
                string ans = null;
                if (o.Choices != null && o.Choices.Count > 0) ans = host.Log.Skip(i + 1).First(c => c.Kind == "answer").Text;
                talks.Add(new JArray(o.Speaker, ans));
            }
            var obj = host.Objectives.LastOrDefault();
            return new JObject
            {
                ["fail"] = fail,
                ["path"] = host.G.Know.TryGetValue("path", out var p) ? p : JValue.CreateNull(),
                ["events"] = new JArray(host.G.Events.Select(e => new JArray(e.Type, e.Day, e.Minute, e.Details))),
                ["toasts"] = new JArray(host.Toasts),
                ["captions"] = new JArray(host.Captions),
                ["talks"] = talks,
                ["objectives"] = obj.title == null ? new JArray() : new JArray { new JArray(obj.title, obj.body, new JArray(obj.steps.Select(x => new JArray(x.Text, x.Done)))) },
            };
        }

        static JToken Norm(JToken t)
        {
            // numbers compare by value (JS 8 vs C# 8.0); ids answered as numbers in JS are strings here
            if (t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) return new JValue(((double)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (t is JArray a) return new JArray(a.Select(Norm));
            if (t is JObject o) return new JObject(o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Norm(p.Value))));
            return t?.DeepClone() ?? JValue.CreateNull();
        }

        static void Compare(string name, JObject got)
        {
            var want = (JObject)Golden[name];
            // Fact-card notices are timed (one every 2.6 s; the first after a 0 ms timeout in the original), so their order
            // against other toasts and whether the late ones fired before the script ended differ by host: compare the
            // other toasts in order, and require every notice the original showed.
            var facts = FakeStoryHost.LoadFacts().All.Select(f => f.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            bool Notice(string t) => facts.Any(f => t == f || t.EndsWith(" — " + f));
            var wantNotices = want["toasts"].Select(t => (string)t).Where(Notice).ToList();
            var gotNotices = got["toasts"].Select(t => (string)t).Where(Notice).ToList();
            Assert.That(gotNotices, Is.SupersetOf(wantNotices), $"{name}: fact notices");
            want = (JObject)want.DeepClone(); got = (JObject)got.DeepClone();
            want["toasts"] = new JArray(want["toasts"].Select(t => (string)t).Where(t => !Notice(t)));
            got["toasts"] = new JArray(got["toasts"].Select(t => (string)t).Where(t => !Notice(t)));
            foreach (var key in new[] { "fail", "path", "events", "toasts", "captions", "talks", "objectives" })
            {
                var w = Norm(want[key]); var g = Norm(got[key]);
                if (w is JArray wa && g is JArray ga)
                {
                    for (int i = 0; i < Math.Min(wa.Count, ga.Count); i++)
                        Assert.That(JToken.DeepEquals(wa[i], ga[i]), $"{name}.{key}[{i}]\n want {wa[i].ToString(Formatting.None)}\n  got {ga[i].ToString(Formatting.None)}");
                    Assert.That(ga.Count, Is.EqualTo(wa.Count), $"{name}.{key} count\n want {wa.ToString(Formatting.None)}\n  got {ga.ToString(Formatting.None)}");
                }
                else Assert.That(JToken.DeepEquals(w, g), $"{name}.{key}\n want {w.ToString(Formatting.None)}\n  got {g.ToString(Formatting.None)}");
            }
        }

        [Test]
        public void CrossroadsStay() => Compare("crossroads_stay", Play(7, Fresh, new[] { "crossroads" }, new Script
        {
            Labels = "rumor", Speakers = { ["정민"] = new[] { "go" }, ["판단"] = new[] { "handle", "stay" }, ["윤 팀장"] = new[] { "yes" }, ["박영수 소장"] = new[] { "go" } },
            Cards = new[] { "add", "stay" },
        }));

        [Test]
        public void CrossroadsMove() => Compare("crossroads_move", Play(7, Fresh, new[] { "crossroads" }, new Script
        {
            Labels = "neighbor", Speakers = { ["정민"] = new[] { "go" }, ["대피소 관리자"] = new[] { "precise", "help" } },
            Cards = new[] { "add", "move" }, Opens = { ["extinguisher"] = new JToken[] { JObject.Parse("{ sequence: ['shout', 'retreat'] }") } },
        }));

        [Test]
        public void CrossroadsFight() => Compare("crossroads_fight", Play(7, Fresh, new[] { "crossroads" }, new Script
        {
            Labels = "rumor", Speakers = { ["정민"] = new[] { "go" }, ["판단"] = new[] { "fight" } }, Cards = new[] { "read", "stay" },
        }));

        [Test]
        public void EvacBus() => Compare("evac_bus", Play(7, Kit, new[] { "evacuation" }, new Script
        {
            Labels = "rumor", Speakers = { ["정민"] = new[] { "go" }, ["라디오"] = new[] { "no" }, ["현관"] = new[] { "note" }, ["집결지"] = new[] { "ask" } },
            Opens = { ["bag"] = new JToken[] { FullBag } }, Presses = new[] { "homeExitDoor", "lobbyExit", "assembly" },
        }));

        [Test]
        public void EvacSeoyeon() => Compare("evac_seoyeon", Play(7, Kit, new[] { "evacuation" }, new Script
        {
            Labels = "rumor", Speakers = { ["정민"] = new[] { "go" }, ["라디오"] = new[] { "no" }, ["오서연"] = new[] { "card", "wait" } },
            Opens = { ["bag"] = new JToken[] { FullBag } }, Presses = new[] { "homeExitDoor", "seoyeonDoor", "seoyeonDoor" },
        }));

        [Test]
        public void EvacShortcutLost() => Compare("evac_shortcut_lost", Play(7, Kit, new[] { "evacuation" }, new Script
        {
            Labels = "neighbor", Speakers = { ["정민"] = new[] { "go" }, ["라디오"] = new[] { "no" } },
            Opens = { ["bag"] = new JToken[] { FullBag } }, Presses = new[] { "homeExitDoor", "lobbyExit", "closure_ROAD" }, At1400 = true,
        }));
    }
}
