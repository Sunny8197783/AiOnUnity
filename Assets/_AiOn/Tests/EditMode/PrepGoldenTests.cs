// Replays the playthroughs of Tools/golden/make-golden-story-prep.mjs (run on the ORIGINAL story/prologue.js and
// story/prep.js under node) on Prologue/Prep + FakeStoryHost, with the script stored in the fixture, and compares
// everything the original recorded: events with details, toasts, talks, cards, time skips, objectives, screens,
// clock, stats, money, items, know flags, relations, skills, notes and level states.
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
    public class PrepGoldenTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(TestSupport.Fixture("story-prep-golden.json"));

        static JToken Num(double? d) => d.HasValue ? new JValue(d.Value) : JValue.CreateNull();
        static JToken Num(int? d) => d.HasValue ? new JValue(d.Value) : JValue.CreateNull();

        /// <summary>Plays one fixture case on the port and returns the host.</summary>
        static FakeStoryHost Play(JObject c)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var g = rules.CreateGame((long)c["seed"]);
            var setup = c["setup"] as JObject;
            if (setup != null)
            {
                if (setup["toured"] != null && (bool)setup["toured"]) g.SetKnow("toured", true);
                if (setup["soldOutEarly"] is JArray se) g.Cond.SoldOut.Early = se.ToObject<List<string>>();
                if (setup["soldOutLate"] is JArray sl) g.Cond.SoldOut.Late = sl.ToObject<List<string>>();
                if (setup["clock"] is JArray ck) { g.Day = (int)ck[0]; g.Minute = (int)ck[1]; }
            }
            var ctx = host.CreateContext(rules, g);
            foreach (JObject r in (c["talk"] as JArray) ?? new JArray())
            {
                string speaker = (string)r["speaker"], offers = (string)r["offers"], answer = (string)r["answer"];
                host.AnswerWhen(o => o.Speaker == speaker && (offers == null || o.Choices.Any(x => x.Id == offers)), answer);
            }
            foreach (var kv in (c["open"] as JObject ?? new JObject()).Properties())
                foreach (var a in (JArray)kv.Value) host.AnswerOpen(kv.Name, a.DeepClone());
            foreach (var s in ((JArray)c["steps"]).Select(x => (string)x))
            {
                if (s == "frame") host.Queue(h => h.RunFrame(), () => ctx.Roaming && host.ControlsEnabled, "frame");
                else host.QueuePress(s);
            }
            if ((string)c["chapter"] == "prologue") host.Run(() => Prologue.Run(ctx)); else host.Run(() => Prep.Run(ctx));
            Assert.That(host.PendingSteps, Is.EqualTo(0), "steps left");
            Assert.That(host.PendingOpenAnswers, Is.EqualTo(0), "screen answers left");
            return host;
        }

        /// <summary>JSON equality where numbers compare by value (JS has one number type: 0 and 0.0 are the same).</summary>
        static bool Eq(JToken a, JToken b)
        {
            bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;
            if (IsNull(a) || IsNull(b)) return IsNull(a) && IsNull(b);
            bool IsNum(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
            if (IsNum(a) && IsNum(b)) return (double)a == (double)b;
            if (a is JArray aa && b is JArray ba) return aa.Count == ba.Count && aa.Zip(ba, Eq).All(x => x);
            if (a is JObject ao && b is JObject bo)
                return ao.Count == bo.Count && ao.Properties().All(p => bo.TryGetValue(p.Name, out var v) && Eq(p.Value, v));
            return JToken.DeepEquals(a, b);
        }

        static void Same(string what, JToken want, JToken got)
        {
            if (Eq(want, got)) return;
            // Point at the first difference of a list.
            if (want is JArray wa && got is JArray ga)
            {
                for (int i = 0; i < Math.Min(wa.Count, ga.Count); i++)
                    if (!Eq(wa[i], ga[i]))
                        Assert.Fail($"{what}[{i}] differs\n  want {wa[i].ToString(Newtonsoft.Json.Formatting.None)}\n  got  {ga[i].ToString(Newtonsoft.Json.Formatting.None)}");
                Assert.Fail($"{what}: {wa.Count} wanted, {ga.Count} got; extra: {(wa.Count > ga.Count ? wa[ga.Count] : ga[wa.Count]).ToString(Newtonsoft.Json.Formatting.None)}");
            }
            Assert.Fail($"{what} differs\n  want {want?.ToString(Newtonsoft.Json.Formatting.None)}\n  got  {got?.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        static void Compare(string name)
        {
            var want = (JObject)Golden[name];
            var host = Play((JObject)want["script"]);
            var g = host.G;

            Same("events", want["events"], new JArray(g.Events.Select(e => new JArray(e.Type, e.Details))));
            Same("toasts", want["toasts"], new JArray(host.Calls("toast").Select(c => { var (label, ms) = ((string, int))c.Data; return new JArray(c.Text, label, ms); })));
            Same("talks", want["talks"], new JArray(host.Talks.Select(t => new JObject
            {
                ["speaker"] = t.Speaker, ["role"] = t.Role, ["lines"] = new JArray(t.Lines),
                ["choices"] = t.Choices == null ? JValue.CreateNull() : new JArray(t.Choices.Select(x => x.Id)),
            })));
            Same("cards", want["cards"], new JArray(host.Cards.Select(x => x.Title)));
            Same("skips", want["skips"], new JArray(host.TimeSkips.Select(s => new JArray(s.from, s.to, s.text.Count == 1 && name.StartsWith("prep") ? (JToken)s.text[0] : new JArray(s.text)))));
            Same("objectives", want["objectives"], new JArray(host.Objectives.Select(o => o.title)));
            Same("opens", want["opens"], new JArray(host.Opens.Select(o => new JArray(o.screen, o.screen == "planner"
                ? new JObject
                {
                    ["day"] = o.options["day"], ["slots"] = o.options["slots"], ["hint"] = o.options["hint"],
                    ["actions"] = new JArray(((JArray)o.options["actions"]).Select(a => new JArray(a["id"], a["done"], a["disabled"], a["reason"]))),
                }
                : (JToken)o.screen))));
            Same("clock", want["clock"], new JArray(g.Day, g.Minute));
            Assert.That(g.Chapter, Is.EqualTo((string)want["chapter"]));
            Same("stats", want["stats"], JObject.FromObject(g.Stats));
            Assert.That(g.Money, Is.EqualTo((long)want["money"]), "money");
            Assert.That(g.Phone.Battery, Is.EqualTo((double)want["battery"]), "battery");
            Same("slots", want["slots"], JObject.FromObject(g.Slots));
            var items = new JObject();
            foreach (var (k, v) in g.Items) items[k] = new JArray(v.Home, v.Bag, v.Used, v.Checked, Num(v.Charge), Num(v.Pocket), v.Bought);
            Same("items", want["items"], items);
            foreach (var p in ((JObject)want["know"]).Properties())
                Same($"know.{p.Name}", p.Value, g.Know.TryGetValue(p.Name, out var kv2) ? kv2 : null);
            Same("know keys", new JArray(((JObject)want["know"]).Properties().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)),
                new JArray(g.Know.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            Same("rel", want["rel"], JObject.FromObject(g.Rel));
            Same("skill", want["skill"], JObject.FromObject(g.Skill));
            Same("info", want["info"], new JArray(g.Info.Select(i => new JArray(i.Text, i.Label, i.Place))));
            foreach (var lv in ((JObject)want["levels"]).Properties())
                foreach (var st in ((JObject)lv.Value).Properties())
                {
                    if (st.Name == "dashboard") continue; // a canvas in the original, data here (PrologueTests checks it)
                    Same($"level {lv.Name}.{st.Name}", st.Value, host.Level(lv.Name).State.TryGetValue(st.Name, out var v) ? v : null);
                }
        }

        [Test] public void Prologue_AllRight() => Compare("prologue_right");
        [Test] public void Prologue_AllFacts() => Compare("prologue_facts");
        [Test] public void Prep_EquipMartShelterNeighborsBag() => Compare("prep_a");
        [Test] public void Prep_DocsHomeRestSeoyeonEmptyBag() => Compare("prep_b");
    }
}
