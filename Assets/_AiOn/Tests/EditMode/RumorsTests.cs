using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/rumors.test.mjs, case for case, plus parity with game/rumors.js
    /// (Fixtures/golden-rumors.json, regenerate with Tools/golden/make-golden-rumors.mjs).
    /// </summary>
    public class RumorsTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-rumors.json"));

        GameRules R;
        Dictionary<string, string> facts;

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            facts = new Dictionary<string, string>();
            GameSession.LoadFacts(Fixture("../../../Data/Source/facts.json"), facts);
        }

        string Fact(string id) => id != null && facts.TryGetValue(id, out var t) ? t : null;

        // ---------- the original's tests ----------

        [Test]
        public void Every_rumor_set_is_complete_and_cites_real_facts()
        {
            CollectionAssert.AreEqual(new[] { "shelter_closed", "camp_food", "evac_shortcut" }, Rumors.All.Select(r => r.Id));
            var g = R.CreateGame(4);
            foreach (var r in Rumors.All)
            {
                Assert.That(r.Facts.Count, Is.GreaterThan(0));
                foreach (var f in r.Facts) Assert.IsNotNull(Fact(f), $"{r.Id}: {f}");
                Assert.AreEqual(r.Facts.Count, Rumors.RumorFacts(r.Id, Fact).Count);
                Assert.AreEqual("claim", r.Items[0].Key);
                Assert.IsFalse(r.Items[0].Truth, "the claim is false");
                foreach (var it in r.Items)
                {
                    Assert.IsTrue(!string.IsNullOrEmpty(it.From) && !string.IsNullOrEmpty(it.Meta) && Array.IndexOf(GameRules.Labels, it.Label) >= 0, $"{r.Id}.{it.Key}");
                    StringAssert.IsMatch("[가-힣]", it.Text(g));
                }
                Assert.IsTrue(r.Items.Any(it => it.Truth && it.Label != "official"), $"{r.Id} has a true non-official item");
                Assert.IsTrue(r.Items.Any(it => it.Truth && it.Label == "official"), $"{r.Id} has the official line");
            }
            Assert.Catch(() => Rumors.Rumor("nope"));
        }

        [Test]
        public void Evac_shortcut_always_names_a_road_that_is_closed_for_every_seed()
        {
            for (uint s = 1; s <= 500; s++)
            {
                var g = R.CreateGame(s);
                var c = GameConditions.For(s);
                var id = Rumors.Rumor("evac_shortcut").Road(g);
                CollectionAssert.Contains(c.Closures.Evac, id, $"seed {s}: {id}");
                var posts = Rumors.Posts(g, "evac_shortcut");
                StringAssert.Contains(TownMap.Closures[id].Label, posts[0].Text);
                StringAssert.Contains(Regex.Replace(TownMap.Closures[id].Label, " 통제$", ""), posts[1].Text, "the site line names the same road");
            }
        }

        [Test]
        public void Judge_records_the_pick_and_gives_a_ladder_result()
        {
            var g = R.CreateGame(1);
            var r = Rumors.Judge(g, "camp_food", "rumor");
            AssertJson(r.ToJObject(), "{ok:true,label:'rumor',truth:false,result:'sorted',facts:['info_scope','info_rumor']}");
            r = Rumors.Judge(g, "camp_food", "official");
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("misled", r.Result);
            Assert.IsTrue(Rumors.Judge(g, "camp_food", "neighbor", "seoyeon").Ok);
            var dq = Rumors.Judge(g, "camp_food", "rumor", "seoyeon");
            Assert.IsFalse(dq.Ok, "a true neighbor memo is not a rumor");
            Assert.AreEqual("doubted", dq.Result, "doubting a true memo is not \"following a rumor\"");
            var evs = g.Events.Where(e => e.Type == "rumor_judged").ToList();
            Assert.AreEqual(4, evs.Count);
            AssertJson(evs[0].Details, "{id:'camp_food',item:'claim',pick:'rumor',ok:true}");
            BagDrill.RecordEncounter(g, "info", r.Result);
            Assert.AreEqual(1, g.Skill["info"].Count);
            int n = g.Events.Count;
            Assert.Catch(() => Rumors.Judge(g, "camp_food", "gossip"));
            Assert.Catch(() => Rumors.Judge(g, "camp_food", "rumor", "nope"));
            Assert.AreEqual(n, g.Events.Count, "a bad pick or item records nothing");
        }

        [Test]
        public void Believing_a_false_claim_misleads_until_it_is_checked_and_following_it_leads_to_lostWay()
        {
            var g = R.CreateGame(1);
            Assert.IsNull(Rumors.Misled(g));
            Assert.IsTrue(Rumors.Believe(g, "evac_shortcut"));
            Assert.AreEqual("evac_shortcut", Rumors.Misled(g));
            Assert.IsNull(Rumors.LostByRumor(g), "not lost until the closed road is reached");
            GameRules.RecordEvent(g, "rumor_followed", new JObject { ["id"] = "evac_shortcut" });
            Assert.AreEqual("evac_shortcut", Rumors.LostByRumor(g));
            var truths = Rumors.Verify(g, "evac_shortcut");
            Assert.IsTrue(truths.Count >= 2 && truths.All(p => p.Truth));
            Assert.IsNull(Rumors.Misled(g));
            Assert.IsNull(Rumors.LostByRumor(g));
            CollectionAssert.AreEqual(new[] { "evac_shortcut" }, Rumors.SeenRumors(g).Select(s => s.Id));
            Assert.AreEqual("claim", Rumors.SeenRumors(g)[0].Claim.Key);
        }

        // ---------- parity with the original ----------

        static JArray Json(IEnumerable<RumorPost> posts) => new JArray(posts.Select(p => p.ToJObject()));

        static GameState WithEvac(GameState g, JToken evac)
        {
            if (evac != null && evac.Type != JTokenType.Null) g.Cond.Closures.Evac = evac.Select(t => (string)t).ToList();
            return g;
        }

        [Test]
        public void The_sets_are_the_original_item_for_item()
        {
            var actual = new JArray(Rumors.All.Select(r => new JObject
            {
                ["id"] = r.Id, ["chapter"] = r.Chapter, ["facts"] = new JArray(r.Facts), ["namesRoad"] = r.Road(R.CreateGame(1)) != null,
                ["items"] = new JArray(r.Items.Select(it => new JObject
                {
                    ["key"] = it.Key, ["channel"] = it.Channel, ["from"] = it.From, ["meta"] = it.Meta, ["label"] = it.Label, ["truth"] = it.Truth,
                })),
            }));
            AssertSameJson(actual, Golden["sets"]);
            foreach (var r in Rumors.All)
            {
                Assert.AreSame(r, Rumors.Rumor(r.Id));
                CollectionAssert.AreEqual(Golden["facts"][r.Id].Select(t => (string)t), Rumors.RumorFacts(r.Id, Fact), $"{r.Id}: the verified texts");
            }
            Assert.IsEmpty(Rumors.RumorFacts("camp_food", _ => null), "facts the lookup does not know are left out");
            Assert.Catch(() => Rumors.RumorFacts("nope", Fact));
            Assert.Catch(() => Rumors.Rumor(null));
        }

        [Test]
        public void Every_golden_seed_names_the_original_road_and_says_the_original_words()
        {
            var bySingleRoad = Golden["posts"].Where(p => p["evac"].Count() == 1 && p["road"].Type == JTokenType.String)
                .ToDictionary(p => (string)p["road"], p => p["posts"]["evac_shortcut"]);
            var rows = (JArray)Golden["roads"];
            Assert.That(rows.Count, Is.GreaterThan(500));
            var seen = new HashSet<string>();
            foreach (var row in rows)
            {
                long seed = (long)row[0];
                var road = (string)row[1];
                var g = R.CreateGame(seed);
                Assert.AreEqual(road, Rumors.Rumor("evac_shortcut").Road(g), $"seed {seed}");
                AssertSameJson(Json(Rumors.Posts(g, "evac_shortcut")), bySingleRoad[road], $"seed {seed}");
                seen.Add(road);
            }
            CollectionAssert.AreEquivalent(GameConditions.EvacClosureSets.Select(l => l[0]).Distinct(), seen, "every road the evacuation can name comes up");
        }

        [Test]
        public void Posts_match_the_original_for_every_road_and_fall_back_without_one()
        {
            var cases = (JArray)Golden["posts"];
            Assert.AreEqual(9, cases.Count);
            foreach (var c in cases)
            {
                var g = WithEvac(R.CreateGame(1), c["evac"]);
                string what = $"evac {c["evac"].ToString(Newtonsoft.Json.Formatting.None)}";
                var road = Rumors.Rumor("evac_shortcut").Road(g);
                if (c["road"].Type == JTokenType.Null) Assert.IsNull(road, what);
                else Assert.AreEqual((string)c["road"], road, what);
                foreach (var r in Rumors.All)
                {
                    AssertSameJson(Json(Rumors.Posts(g, r.Id)), c["posts"][r.Id], $"{r.Id}, {what}");
                    Assert.IsNull(r.Id == "evac_shortcut" ? null : r.Road(g), "only evac_shortcut names a road");
                }
            }
            Assert.Catch(() => Rumors.Posts(R.CreateGame(1), "nope"));
        }

        [Test]
        public void Scripted_runs_judge_believe_and_verify_like_the_original()
        {
            var runs = (JArray)Golden["runs"];
            Assert.AreEqual(16, runs.Count);
            var results = new HashSet<string>();
            int steps = 0, thrown = 0;
            foreach (var run in runs)
            {
                var g = WithEvac(R.CreateGame((long)run["seed"]), run["evac"]);
                int i = 0;
                foreach (var s in run["steps"])
                {
                    string what = $"seed {run["seed"]} step {i++}: {s.ToString(Newtonsoft.Json.Formatting.None)}";
                    string id = (string)s["id"];
                    bool throws = (bool?)s["throws"] ?? false;
                    int events = g.Events.Count;
                    JToken r = null;
                    TestDelegate act = () =>
                    {
                        switch ((string)s["op"])
                        {
                            case "judge": r = Rumors.Judge(g, id, (string)s["pick"], (string)s["item"]).ToJObject(); break;
                            case "believe": r = Rumors.Believe(g, id); break;
                            case "verify": r = Json(Rumors.Verify(g, id)); break;
                            case "event": GameRules.RecordEvent(g, (string)s["type"], (JObject)s["details"].DeepClone()); break;
                            case "advance": GameRules.Advance(g, (double)s["minutes"]); break;
                            case "chapter": g.Chapter = (string)s["chapter"]; break;
                            case "know": g.Know["misled"] = s["value"].DeepClone(); break;
                            default: throw new InvalidOperationException($"unknown op in the fixture: {s["op"]}");
                        }
                    };
                    if (throws)
                    {
                        Assert.Catch(act, what);
                        Assert.AreEqual(events, g.Events.Count, $"a call that throws records nothing. {what}");
                        thrown++;
                    }
                    else
                    {
                        Assert.DoesNotThrow(act, what);
                        if (s["r"] != null) AssertSameJson(r, s["r"], what);
                        if ((string)s["op"] == "judge") results.Add((string)r["result"]);
                    }

                    // What the story asks next.
                    var q = s["q"];
                    Assert.AreEqual((string)q["misled"], Rumors.Misled(g), $"misled. {what}");
                    Assert.AreEqual((string)q["lost"], Rumors.LostByRumor(g), $"lostByRumor. {what}");
                    CollectionAssert.AreEqual(q["seen"].Select(t => (string)t), Rumors.SeenRumors(g).Select(x => x.Id), $"seenRumors. {what}");
                    steps++;
                }

                var seen = new JArray(Rumors.SeenRumors(g).Select(x => new JObject { ["id"] = x.Id, ["claim"] = x.Claim.ToJObject() }));
                AssertSameJson(seen, run["seen"], $"seed {run["seed"]} seen");
                var save = SaveCodec.ToJObject(g);
                AssertSameJson(save["know"], run["know"], $"seed {run["seed"]} know");
                AssertSameJson(save["events"], run["events"], $"seed {run["seed"]} events");
            }
            Assert.That(steps, Is.GreaterThan(400));
            Assert.That(thrown, Is.GreaterThan(10));
            CollectionAssert.AreEquivalent(new[] { "sorted", "doubted", "misled" }, results, "every judgement result is covered");
        }

        [Test]
        public void Being_misled_survives_a_save_and_only_a_string_that_is_a_rumor_id_counts()
        {
            var g = R.CreateGame(9);
            Assert.IsTrue(Rumors.Believe(g, "camp_food"));
            GameRules.RecordEvent(g, "rumor_followed", new JObject { ["id"] = "camp_food" });
            var loaded = SaveCodec.Load(SaveCodec.Save(g), Db);
            Assert.AreEqual("camp_food", Rumors.Misled(loaded));
            Assert.AreEqual("camp_food", Rumors.LostByRumor(loaded));
            CollectionAssert.AreEqual(new[] { "camp_food" }, Rumors.SeenRumors(loaded).Select(s => s.Id));

            Rumors.Verify(loaded, "shelter_closed");
            Assert.AreEqual("camp_food", Rumors.Misled(loaded), "checking another claim does not clear this one");
            Rumors.Verify(loaded, "camp_food");
            Assert.AreEqual(JTokenType.Null, loaded.Know["misled"].Type, "cleared to null, like the original");
            Assert.AreEqual(SaveCodec.Save(loaded), SaveCodec.Save(SaveCodec.Load(SaveCodec.Save(loaded), Db)), "the save stays valid");

            foreach (var odd in new JToken[] { 5, true, "nope", "", JValue.CreateNull() })
            {
                loaded.Know["misled"] = odd;
                Assert.IsNull(Rumors.Misled(loaded), odd.ToString(Newtonsoft.Json.Formatting.None));
                Assert.IsNull(Rumors.LostByRumor(loaded));
            }
            loaded.Know.Remove("misled");
            Assert.IsNull(Rumors.Misled(loaded));
        }
    }
}
