// Replays the scripts of Tools/golden/make-golden-story-runtime.mjs (run on the ORIGINAL story/runtime.js and
// story/common.js under node) on StoryContext + FakeStoryHost and compares everything the original recorded:
// toasts (text, label, ms), talks (speaker, role, lines, choice ids), events, stats, items, ledger rows, notes.
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
    public class StoryRuntimeGoldenTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(TestSupport.Fixture("story-runtime-golden.json"));

        static (FakeStoryHost host, StoryContext ctx) Rig(long seed, Func<TalkOptions, string> answer)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var ctx = host.CreateContext(rules, rules.CreateGame(seed));
            host.TalkPolicy = answer; // the original rig answers every talk with choices, disabled or not
            return (host, ctx);
        }

        static JToken Num(double? d) => d.HasValue ? new JValue(d.Value) : JValue.CreateNull();

        static JObject Dump(FakeStoryHost host)
        {
            var g = host.G;
            var items = new JObject();
            foreach (var (k, v) in g.Items) items[k] = new JArray(v.Home, v.Bag, v.Used, v.Checked, Num(v.Charge));
            return new JObject
            {
                ["toasts"] = new JArray(host.Calls("toast").Select(c => { var (label, ms) = ((string, int))c.Data; return new JArray(c.Text, label, ms); })),
                ["talks"] = new JArray(host.Talks.Select(t => new JObject
                {
                    ["speaker"] = t.Speaker, ["role"] = t.Role, ["lines"] = new JArray(t.Lines),
                    ["choices"] = t.Choices == null ? JValue.CreateNull() : new JArray(t.Choices.Select(c => c.Id)),
                })),
                ["events"] = new JArray(g.Events.Select(e => new JArray(e.Type, e.Details))),
                ["stats"] = JObject.FromObject(g.Stats),
                ["items"] = items,
                ["ledger"] = new JArray(g.Ledger),
                ["battery"] = g.Phone.Battery,
                ["money"] = g.Money,
                ["info"] = new JArray(g.Info.Select(i => new JArray(i.Text, i.Label))),
            };
        }

        static void Same(string name, JObject actual, params string[] keys)
        {
            var want = (JObject)Golden[name];
            foreach (var k in keys.Length > 0 ? keys : want.Properties().Select(p => p.Name).ToArray())
            {
                var w = want[k];
                // TalkChoice ids are strings: the ledger's numeric ids (1, 2, 3) arrive as "1", "2", "3".
                if (k == "talks") { w = w.DeepClone(); foreach (var t in w) if (t["choices"] is JArray cs) t["choices"] = new JArray(cs.Select(c => c.ToString())); }
                TestSupport.AssertSameJson(actual[k] ?? JValue.CreateNull(), w, $"{name}.{k}");
            }
        }

        [TestCase("ledger_home_1", 1, "home")]
        [TestCase("ledger_home_0", 0, "home")]
        [TestCase("ledger_shelter_2", 2, "shelter")]
        public void DayLedger(string name, int pick, string place)
        {
            var (host, ctx) = Rig(11, o => o.Choices?[pick].Id);
            host.G.Day = 3;
            var lines = host.Run(() => StoryCommon.DayLedger(ctx, 3, place));
            var d = Dump(host); d["lines"] = new JArray(lines);
            Same(name, d, "lines", "toasts", "events", "stats", "items", "ledger", "battery", "money", "info");
            // talks: the power choices' notes are not in the golden (it keeps ids only), lines and roles are
            Same(name, d, "talks");
        }

        [Test]
        public void StockAndAlbum()
        {
            var (host, ctx) = Rig(11, o => o.Choices?[0].Id);
            host.Run(async () => { await StoryCommon.StockLook(ctx); await StoryCommon.LookAlbum(ctx); await StoryCommon.LookAlbum(ctx); StoryCommon.EatOldBars(ctx); });
            Same("stock_album", Dump(host));
        }

        [Test]
        public void Radio()
        {
            var (host, ctx) = Rig(11, o => o.Choices?[0].Id);
            host.OpenPolicy = (s, o) => new JObject { ["official"] = true, ["noted"] = new JArray(new JObject { ["text"] = "라디오 안내 하나", ["kind"] = "official" }) };
            var g = host.G;
            g.Items["radio"] = new ItemEntry { Home = 1, Bought = 1 };
            g.Items["batteries_aa"] = new ItemEntry { Home = 1, Bought = 1 };
            ctx.Rules.Check(g, "radio");
            var heard = new JArray();
            host.Run(async () =>
            {
                heard.Add(Arr(await StoryCommon.RadioScene(ctx, "normal")));
                g.Items["radio"].Charge = 4;
                heard.Add(Arr(await StoryCommon.RadioScene(ctx, "blackout")));
                g.Items["batteries_old"] = new ItemEntry { Home = 1 };
                g.Chapter = "evac";
                heard.Add(Arr(await StoryCommon.RadioScene(ctx, "evac")));
            });
            var d = Dump(host); d["heard"] = heard;
            Same("radio", d);
        }

        static JToken Arr(IReadOnlyList<string> l) => l == null ? JValue.CreateNull() : new JArray(l);

        [Test]
        public void PayoffAndNotes()
        {
            var (host, ctx) = Rig(7, o => null);
            var g = host.G;
            g.Day = -1; g.Minute = 600; GameRules.RecordEvent(g, "bag_packed", new JObject());
            g.Day = 0; g.Minute = 1206; ctx.Payoff("bag", "바로 나섰다"); ctx.Payoff("judgement", "판단했다");
            ctx.Stat("tension", 30); ctx.Pass(45); ctx.Note("메모 하나", "rumor");
            Same("payoff", Dump(host));
        }

        [Test]
        public void TownCommonPoints()
        {
            var (host, ctx) = Rig(7, o => o.Speaker == "ATM" ? "2" : o.Choices?[0].Id);
            var result = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var done = ctx.Roam(new RoamOptions { On = { ["aptNotice"] = _ => System.Threading.Tasks.Task.FromResult<JToken>("end") } });
                foreach (var p in new[]
                {
                    new StoryPoint { Id = "atm1", Kind = "atm", Label = "ATM" }, new StoryPoint { Id = "vending1", Kind = "vending", Label = "자판기" },
                    new StoryPoint { Id = "busStop", Kind = "busStop", Label = "정류장" }, new StoryPoint { Id = "center", Kind = "place", Label = "행정복지센터" },
                    new StoryPoint { Id = "x", Kind = "npc", Label = "누군가" }, new StoryPoint { Id = "home", Kind = "door", Label = "103동" },
                }) await host.PressAsync(p);
                await host.PressAsync(new StoryPoint { Id = "aptNotice", Kind = "notice", Label = "b" });
                return await done;
            });
            var d = Dump(host); d["result"] = result; d["level"] = host.LevelId;
            Same("town_common", d);
        }
    }
}
