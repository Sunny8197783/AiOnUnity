using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/deeds.test.mjs, plus parity with the original game/deeds.js
    /// (Fixtures/golden-deeds.json over the games of golden-runs.json; regenerate with Tools/golden/make-golden-deeds.mjs).
    /// </summary>
    public class DeedsTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-deeds.json");

        static GameState Fresh() => Rules.CreateGame(4);
        static List<string> Ids(GameState g) => Deeds.Of(g).Select(d => d.Id).ToList();
        static GameEvent Rec(GameState g, string type, string details = "{}") => GameRules.RecordEvent(g, type, JObject.Parse(details));

        [Test]
        public void Table_has_64_deeds_in_order_with_known_chapters_axes_and_facts_and_Korean_text()
        {
            var all = Deeds.Table;
            Assert.AreEqual(64, all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                Assert.AreEqual(i + 1, d.No, d.Id);
                Assert.AreSame(d, Deeds.Get(d.Id), d.Id);
                Assert.That(Deeds.Ch.ContainsKey(d.Ch), d.Id);
                Assert.That(d.W.Keys.All(k => Deeds.AxisNames.Contains(k)), d.Id);
                foreach (var s in new[] { d.Short, d.Line })
                {
                    Assert.That(GoldenRuns.HasHangul(s), d.Id);
                    Assert.That(!GoldenRuns.HasLatin(s), d.Id);
                }
                Assert.That(d.Short.Length, Is.LessThanOrEqualTo(22), $"{d.Id}: {d.Short}");
                Assert.That(d.Facts.All(f => GoldenRuns.Fact(f) != null), d.Id);
                Assert.That(d.Cue == null || d.Cue.EndsWith("은", StringComparison.Ordinal) || d.Cue.EndsWith("는", StringComparison.Ordinal), d.Id);
                Assert.That(d.Src == "event" || d.Src == "new", d.Id);
            }
            Assert.AreEqual(27, Deeds.NewIds.Count);
            CollectionAssert.AreEqual(new[] { "seoyeon", "manager", "clerk", "elder" }, Deeds.NeighbourIds);
            CollectionAssert.AreEquivalent(Deeds.NeighbourIds, Deeds.Neighbours.Keys);
            Assert.AreEqual("서연은 이 일을 기억할 것이다.", Deeds.CueText("stairs_asked"));
            Assert.IsNull(Deeds.CueText("hoard"));
            Assert.IsNull(Deeds.CueText("nope"));
        }

        [Test]
        public void A_fresh_game_has_no_deeds_and_every_axis_at_0()
        {
            var g = Fresh();
            Assert.IsEmpty(Deeds.Of(g));
            var a = Deeds.Axes(g, final: true);
            CollectionAssert.AreEqual(Deeds.AxisNames, a.Keys);
            Assert.That(a.Values.All(v => v == 0));
        }

        [Test]
        public void Remember_once_per_id_only_new_choice_ids_rolled_back_by_restore()
        {
            var g = Fresh();
            var e = Deeds.Remember(g, "driver_text");
            Assert.AreEqual("deed", e.Type);
            Assert.AreEqual("driver_text", (string)e.Details["id"]);
            Assert.IsNull(Deeds.Remember(g, "driver_text"));
            Assert.Catch(() => Deeds.Remember(g, "nope"));
            Assert.Catch(() => Deeds.Remember(g, "hoard"));
            Assert.Catch(() => Deeds.Remember(g, null));
            Assert.AreEqual("rumor_corrected", (string)Deeds.Remember(g, "rumor_corrected", JObject.Parse("{after:'check',id:'x'}")).Details["id"]);
            SaveCodec.MakeCheckpoint(g, "c");
            Deeds.Remember(g, "mart_share");
            CollectionAssert.Contains(Ids(g), "mart_share");
            SaveCodec.Restore(g);
            CollectionAssert.AreEqual(new[] { "driver_text", "rumor_corrected" }, Ids(g));
            Assert.AreEqual("check", (string)Deeds.Of(g)[1].Details["after"]);
        }

        [Test]
        public void Every_event_derived_deed_comes_from_its_event_alone()
        {
            var eventIds = Deeds.Table.Where(d => d.Src == "event" && d.Id != "signals_clear").Select(d => d.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(eventIds, GoldenRuns.RawIds.OrderBy(x => x, StringComparer.Ordinal).ToList());
            foreach (var id in eventIds)
            {
                var g = Fresh();
                foreach (var (type, d, ch) in GoldenRuns.Raw(id)) { if (ch != null) g.Chapter = ch; GameRules.RecordEvent(g, type, d); }
                CollectionAssert.AreEqual(new[] { id }, Ids(g), id);
            }
        }

        [Test]
        public void Untagged_help_events_fall_back_to_their_old_text_and_other_helpers_do_not_count()
        {
            var g = Fresh();
            Rec(g, "help", "{who:'seoyeon',what:'어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다'}");
            Rec(g, "help", "{who:'seoyeon',what:'라디오 메모와 급수소 소식을 서로 나누기로 했다'}");
            Rec(g, "help", "{who:'seoyeon',what:'떠나기 전 서로의 연락처를 다시 확인했다'}");
            Rec(g, "help", "{who:'manager',what:'마지막까지 건물을 지키며 길을 안내했다'}");
            Rec(g, "help", "{who:'clerk',what:'가게를 닫으며 쪽지와 현금 통을 남겨 이웃이 필요한 물건을 가져가게 했다'}");
            CollectionAssert.AreEqual(new[] { "stairs_asked", "notes_exchange", "seoyeon_card_photo" }, Ids(g));
            var c = Fresh();
            Rec(c, "help", "{who:'seoyeon',what:'계단에서 보행기를 옆에서 들어 주었다'}");
            CollectionAssert.AreEqual(new[] { "stairs_carried" }, Ids(c));
            var t = Fresh(); // a tag wins over the text
            Rec(t, "help", "{who:'seoyeon',kind:'water',what:'어떻게 도울지 먼저 묻고'}");
            Assert.IsEmpty(Ids(t));
        }

        [Test]
        public void Signals_clear_needs_four_correct_sorts_and_its_seq_is_the_4th()
        {
            var g = Fresh();
            for (int i = 0; i < 3; i++) Rec(g, "signal_sorted", $"{{id:'s{i}',ok:true}}");
            Rec(g, "signal_sorted", "{id:'x',ok:false}");
            CollectionAssert.DoesNotContain(Ids(g), "signals_clear");
            var e = Rec(g, "signal_sorted", "{id:'s3',ok:true}");
            Rec(g, "signal_sorted", "{id:'s4',ok:true}");
            var d = Deeds.Of(g).First(x => x.Id == "signals_clear");
            Assert.AreEqual(e.Seq, d.Seq);
            Assert.AreEqual(5, d.N);
            Assert.AreEqual(2, Deeds.Axes(g)["info"]);
        }

        [Test]
        public void Violence_two_hits_and_a_knock_out_are_harm_5_and_self_defence_is_no_harm()
        {
            var g = Fresh();
            Rec(g, "violence", "{how:'punch',ko:false}");
            Rec(g, "violence", "{how:'kick',ko:false}");
            Rec(g, "violence", "{how:'punch',ko:true}");
            Rec(g, "looter", "{result:'fought'}");
            Assert.AreEqual(5, Deeds.Axes(g)["harm"]);
            var v = Deeds.Of(g).First(d => d.Id == "violence");
            Assert.AreEqual(3, v.N);
            Assert.AreEqual(1, v.Ko);
            Assert.IsNull(Deeds.Of(g).First(d => d.Id == "looter_fought").Ko, "only violence counts knock-outs");
        }

        [Test]
        public void Withdrawal_weighs_the_same_as_the_riskier_choice()
        {
            Dictionary<string, int> Fire(Action<GameState> fn) { var g = Fresh(); g.Chapter = "endure"; fn(g); return Deeds.Axes(g); }
            var put = Fire(g => GoldenRuns.Encounter(g, "extinguisher", JObject.Parse("{result:'out'}")));
            CollectionAssert.AreEqual(put, Fire(g => GoldenRuns.Encounter(g, "extinguisher", "retreat-ok")));
            CollectionAssert.AreEqual(put, Fire(g => Rec(g, "fire_retreat", "{size:'large'}")));
            Assert.AreEqual(1, put["manager"]);
            Assert.AreEqual(0, Fire(g => GoldenRuns.Encounter(g, "extinguisher", "escalated"))["manager"]);
            foreach (var id in new[] { "looter_escaped", "water_declined", "shelter_water_received" }) Assert.IsEmpty(Deeds.Get(id).W, id);
            int Sum(params string[] list) => list.Sum(id => Deeds.Get(id).W.Values.Sum());
            Assert.AreEqual(Sum("smoke_judged"), Sum("shelter_fire_judged", "fire_report"));
            Assert.AreEqual(Sum("rounds_with_manager"), Sum("sort_supplies"));
        }

        [Test]
        public void No_new_choice_deed_tied_to_a_safety_first_option_weighs_less_than_zero()
        {
            var temptations = new HashSet<string> { "store_took" };
            foreach (var id in Deeds.NewIds) if (!temptations.Contains(id)) Assert.That(Deeds.Get(id).W.Values.All(v => v >= 0), id);
            // harm comes only from unprovoked violence, hoarding, taking and spreading rumours
            CollectionAssert.AreEqual(new[] { "hoard", "rumor_spread", "store_took", "violence" },
                Deeds.Table.Where(d => d.W.TryGetValue("harm", out var h) && h != 0).Select(d => d.Id).OrderBy(x => x, StringComparer.Ordinal).ToList());
        }

        [Test]
        public void Promises_kept_open_until_the_run_ends_then_broken_and_the_bond_drops()
        {
            var g = Fresh();
            Deeds.Remember(g, "promise_note");
            GoldenRuns.AssertGolden(Deeds.Promises(g), JToken.Parse("[{id:'note_for_seoyeon',who:'seoyeon',state:'open'}]"));
            Assert.AreEqual("broken", Deeds.Promises(g, final: true)[0].State);
            Assert.AreEqual(1, Deeds.Axes(g)["seoyeon"]);
            Assert.AreEqual(0, Deeds.Axes(g, final: true)["seoyeon"]);
            Rec(g, "note_left");
            Assert.AreEqual("kept", Deeds.Promises(g, final: true)[0].State);
            Deeds.Remember(g, "promise_store");
            Deeds.Remember(g, "store_shuttered");
            Assert.AreEqual("kept", Deeds.Promises(g, final: true).First(p => p.Id == "store_watch").State);
        }

        [Test]
        public void NewDeeds_lists_only_what_came_after_a_seq()
        {
            var g = Fresh();
            Deeds.Remember(g, "driver_text");
            int mid = Rec(g, "blackout").Seq;
            Deeds.Remember(g, "mart_share");
            Rec(g, "hoard");
            CollectionAssert.AreEqual(new[] { "mart_share", "hoard" }, Deeds.NewDeeds(g, mid).Select(d => d.Id).ToList());
            Assert.AreEqual(3, Deeds.NewDeeds(g, 0).Count);
            Assert.IsEmpty(Deeds.NewDeeds(g, g.Events.Last().Seq));
        }

        [Test]
        public void Final_build_events_the_304_dilemma_the_night_knock_and_only_the_shelter_rumour_count()
        {
            var g = Fresh();
            Rec(g, "elder_choice", "{pick:'go'}");
            Rec(g, "knock", "{pick:'none'}");
            Rec(g, "rumor_checked", "{id:'camp_food'}");
            Rec(g, "rumor_spread", "{id:'evac_shortcut'}");
            Rec(g, "help", "{who:'manager',what:'박 소장과 함께 남아 건물을 지키기로 했다'}");
            Assert.IsEmpty(Ids(g));
            Rec(g, "help", "{who:'elder',what:'경보 속 캄캄한 계단에서 304호 어르신을 부축해 1층까지 함께 내려갔다'}");
            Rec(g, "knock", "{pick:'info'}");
            Rec(g, "rumor_checked"); // an old save: no id means the shelter rumour
            CollectionAssert.AreEqual(new[] { "elder_alert_knock", "knock_helped", "rumor_checked" }, Ids(g));
        }

        // ---------- parity with the original ----------

        [Test]
        public void The_table_is_the_originals()
        {
            CollectionAssert.AreEqual(Golden["axes"].Select(x => (string)x), Deeds.AxisNames);
            GoldenRuns.AssertGolden(Deeds.Ch, Golden["ch"]);
            GoldenRuns.AssertGolden(Deeds.Neighbours, Golden["neighbours"]);
            CollectionAssert.AreEqual(((JObject)Golden["neighbours"]).Properties().Select(p => p.Name), Deeds.NeighbourIds);
            CollectionAssert.AreEqual(Golden["newDeeds"].Select(x => (string)x), Deeds.NewIds);
            var table = (JArray)Golden["table"];
            Assert.AreEqual(table.Count, Deeds.Table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                var row = (JObject)GoldenRuns.Js(Deeds.Table[i]);
                row["cueText"] = Deeds.CueText(Deeds.Table[i].Id);
                GoldenRuns.AssertGolden(row, table[i], (string)table[i]["id"]);
            }
        }

        [Test]
        public void Every_golden_run_holds_the_deeds_axes_and_promises_the_original_found()
        {
            var cases = (JArray)Golden["cases"];
            Assert.That(cases.Count, Is.GreaterThan(200));
            CollectionAssert.AreEqual(GoldenRuns.Names, cases.Select(c => (string)c["name"]).ToList());
            foreach (var c in cases)
            {
                var name = (string)c["name"];
                var g = GoldenRuns.Load(name);
                GoldenRuns.AssertGolden(Deeds.Of(g), c["deeds"], $"{name}: deeds");
                GoldenRuns.AssertGolden(Deeds.Axes(g), c["axes"], $"{name}: axes");
                GoldenRuns.AssertGolden(Deeds.Axes(g, final: true), c["axesFinal"], $"{name}: final axes");
                GoldenRuns.AssertGolden(Deeds.Promises(g), c["promises"], $"{name}: promises");
                GoldenRuns.AssertGolden(Deeds.Promises(g, final: true), c["promisesFinal"], $"{name}: final promises");
                GoldenRuns.AssertGolden(Deeds.NewDeeds(g, (int)c["after"]), c["newDeeds"], $"{name}: new deeds after {c["after"]}");
            }
        }

        [Test]
        public void A_scripted_remember_sequence_writes_the_same_events_as_the_original()
        {
            var exp = (JObject)Golden["remember"];
            var g = Rules.CreateGame((long)exp["seed"]);
            var steps = (JArray)exp["steps"];
            var log = (JArray)exp["log"];
            for (int i = 0; i < steps.Count; i++)
            {
                string id = (string)steps[i]["id"];
                var details = (bool)steps[i]["hasDetails"] ? (JObject)steps[i]["details"] : null;
                if (log[i].Type == JTokenType.String) { Assert.Catch(() => Deeds.Remember(g, id, details), $"step {i}: {id} throws"); continue; }
                var e = Deeds.Remember(g, id, details);
                if (log[i].Type == JTokenType.Null) { Assert.IsNull(e, $"step {i}: {id} was remembered before"); continue; }
                GoldenRuns.AssertGolden(new JObject { ["seq"] = e.Seq, ["type"] = e.Type, ["details"] = e.Details }, log[i], $"step {i}: {id}");
            }
            GameRules.RecordEvent(g, "deed", new JObject { ["id"] = "promise_note" });
            AssertSameJson(SaveCodec.ToJObject(g), exp["save"], "the save after the sequence");
            GoldenRuns.AssertGolden(Deeds.Of(g), exp["deeds"]);
        }
    }
}
