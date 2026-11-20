using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/endings.test.mjs and tests/r6-fix1-endings.test.mjs (their people and town cases are
    /// in FatesTests), plus parity with the original game/endings.js (Fixtures/golden-endings.json over the games of
    /// golden-runs.json; regenerate with Tools/golden/make-golden-endings.mjs).
    /// </summary>
    public class EndingsTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-endings.json");

        static Fate Pick(GameState g) => Endings.PickEnding(g, GoldenRuns.FactSource);
        static GameState Run(string path, params string[] ids) => GoldenRuns.Run(path, ids);
        static readonly (string, string)[] KnockOut = { ("violence", "{how:'punch',ko:true}") };

        /// <summary>An empty flat: only what the test lays in (tests/r6-fix1-endings.test.mjs).</summary>
        static GameState Flat(string path, params (string id, int n)[] items)
        {
            var g = Rules.CreateGame(3);
            g.SetKnow("path", path);
            foreach (var e in g.Items.Values) e.Home = e.Bag = 0;
            foreach (var (id, n) in items) g.Items[id] = new ItemEntry(n);
            return g;
        }

        [Test]
        public void There_are_11_endings_in_order_and_each_is_reached_by_a_minimal_run()
        {
            CollectionAssert.AreEqual(Enumerable.Range(1, 11), Endings.All.Select(e => e.No));
            CollectionAssert.AreEqual(GoldenRuns.Fix.Select(f => f.id), Endings.All.Select(e => e.Id));
            Assert.AreEqual(11, Endings.All.Select(e => e.Title).Distinct().Count());
            foreach (var e in Endings.All)
            {
                var f = Pick(GoldenRuns.Build(e.Id));
                Assert.AreEqual(e.Id, f.Id);
                Assert.AreEqual(e.No, f.No);
                Assert.That(f.Lines.Count >= 1 && f.Lines.All(GoldenRuns.HasHangul), e.Id);
            }
            Assert.That(Endings.Paths.All(p => Endings.Leave.Contains(p) != Endings.Town.Contains(p)));
        }

        [Test]
        public void The_same_run_always_reaches_the_same_ending_whatever_the_seed()
        {
            foreach (var e in Endings.All)
            {
                var g = GoldenRuns.Build(e.Id);
                AssertSameJson(GoldenRuns.Js(Pick(g)), GoldenRuns.Js(Pick(g)), e.Id);
                AssertSameJson(GoldenRuns.Js(Pick(SaveCodec.Clone(g))), GoldenRuns.Js(Pick(g)), e.Id);
                for (int seed = 1; seed <= 20; seed++) Assert.AreEqual(e.Id, Pick(GoldenRuns.Build(e.Id, seed)).Id, $"{e.Id} seed {seed}");
            }
        }

        [Test]
        public void Selection_shut_only_in_town_leaving_never_opens_a_world_ending_Order_breaks_ties_and_no_path_is_the_bus()
        {
            var all = GoldenRuns.Commons.Concat(new[] { "clerk_contact", "promise_store", "store_paid", "store_shuttered", "driver_text", "yoon_reply", "sort_supplies", "signals_clear", "rumor_checked" }).ToArray();
            foreach (var p in Endings.Leave)
            {
                Assert.AreEqual(p, Pick(Run(p, all)).Id);
                var f = Pick(GoldenRuns.Run(p, new[] { "hoard" }, KnockOut));
                Assert.AreEqual(p, f.Id);
                CollectionAssert.Contains(f.Lines, "몇몇 이웃은 끝내 눈을 마주치지 않았다.");
            }
            // commons and reopen both fit exactly 1: commons comes first in Order
            var tie = Run("stay", GoldenRuns.Commons.Concat(new[] { "clerk_contact", "store_paid", "store_shuttered" }).ToArray());
            Assert.AreEqual("commons", Pick(tie).Id);
            Assert.AreEqual("commons", Endings.Order[0]);
            Assert.AreEqual("reopen", Pick(Run("stay", GoldenRuns.Commons.Concat(new[] { "clerk_contact", "store_paid", "store_shuttered", "promise_store" }).ToArray())).Id, "higher fit wins");
            Assert.AreEqual("shut", Pick(GoldenRuns.Run("stay", GoldenRuns.Commons.Append("hoard"), KnockOut)).Id, "harm comes first");
            var harm3 = Pick(Run("stay", "hoard", "rumor_spread", "store_took"));
            Assert.AreEqual("stay", harm3.Id);
            CollectionAssert.Contains(harm3.Lines, "몇몇 이웃은 끝내 눈을 마주치지 않았다.");
            Assert.AreEqual("bus", Pick(Rules.CreateGame(3)).Id);
            var odd = Rules.CreateGame(3); odd.SetKnow("path", "teleport");
            Assert.AreEqual("bus", Pick(odd).Id);
        }

        [Test]
        public void Ending_lines_follow_the_run()
        {
            var walk = Run("walk", "driver_text", "card_family");
            Assert.That(Pick(walk).Lines.Any(l => l.Contains("김 기사님")) && Pick(walk).Lines.Any(l => l.Contains("대구 부모님")));
            var leg = Run("walk"); leg.Injury = new List<string> { "leg" };
            Assert.That(Pick(leg).Lines.Any(l => l.Contains("구호 트럭")));
            Assert.AreEqual(1, Pick(leg).Lines.Count(l => l == Endings.LegWalkLine || l == Endings.LegAfterLine), "the leg is told once on the walk");
            var legDriver = Run("walk", "driver_text"); legDriver.Injury = new List<string> { "leg" };
            Assert.That(Pick(legDriver).Lines.Any(l => l == Endings.LegAfterLine), "a ride from 김 기사님 does not tell the leg, so the lasting line does");
            var weak = Run("walk"); weak.Stats.Health = 30;
            Assert.That(Pick(weak).Lines.Any(l => l.Contains("실패가 아니었다")));
            Assert.That(Pick(Run("bus")).Lines.Any(l => l.Contains("우편함은 비어")));
            var letters = Pick(Run("bus", "seoyeon_contact", "stairs_asked", "water_for_seoyeon", "bus_helped"));
            Assert.That(!letters.Lines.Any(l => l.Contains("우편함은 비어")) && letters.Lines.Any(l => l.Contains("리프트")));
            CollectionAssert.AreEqual(new[] { "seoyeon" }, letters.People.Where(p => p.Letter != null).Select(p => p.Who).ToList());
            Assert.That(Pick(Run("missed", "manager_contact", "manager_detour", "rounds_with_manager")).Lines.Any(l => l.Contains("박 소장이 문을")));
            Assert.That(Pick(Run("missed")).Lines.Any(l => l.Contains("아무도 문을")));
            Assert.That(Pick(Run("seoyeon", "stairs_asked")).Lines.Any(l => l.Contains("먼저 물어볼 것")));
            CollectionAssert.Contains(Pick(Run("stay", GoldenRuns.Commons.Append("stock_to_manager").ToArray())).Lines, "첫 상자에는 '302호'라고 적혀 있었다.");
        }

        [Test]
        public void Reachability_each_world_ending_has_a_legal_in_town_run_that_meets_every_threshold()
        {
            // Choices that exclude each other in play: keep one side of each.
            var excl = new[]
            {
                new[] { "store_paid", "store_iou", "store_took" }, new[] { "handover_precise", "handover_partial", "handover_overgeneral" }, new[] { "stairs_asked", "stairs_carried" },
                new[] { "looter_gave", "looter_escaped", "looter_fought", "looter_reported", "looter_neighbors", "looter_argued" }, new[] { "rumor_checked", "rumor_spread" }, new[] { "water_for_seoyeon", "water_declined" },
                new[] { "shelter_water_given", "shelter_water_received" }, new[] { "elder_alert_knock", "elder_alert_told" },
            };
            var branch = new Dictionary<string, string[]> { ["stay"] = new[] { "rounds_with_manager", "smoke_judged" }, ["move"] = new[] { "sort_supplies", "shelter_fire_judged", "fire_report" } };
            var pathed = new Dictionary<string, string> { ["seoyeon_waited"] = "seoyeon", ["volunteered"] = "volunteer", ["stayed"] = "stay" };
            foreach (var e in Endings.All.Where(x => x.Kind == "world"))
            {
                bool ok = Endings.Town.Any(path => new[] { "stay", "move" }.Any(b => new[] { 0, 1, 2 }.Any(store =>
                {
                    var drop = new HashSet<string>(excl.SelectMany(g => g.Where((id, i) => i != (g[0] == "store_paid" ? store : 0))).Concat(branch[b == "stay" ? "move" : "stay"]));
                    if (path == "stay") drop.Add("stock_to_manager");
                    var ids = Deeds.Table.Where(d => !drop.Contains(d.Id) && (!pathed.ContainsKey(d.Id) || pathed[d.Id] == path) && d.W.Values.All(v => v >= 0)).ToList();
                    return e.Need.All(n => ids.Sum(d => d.W.TryGetValue(n.Axis, out var v) ? v : 0) >= n.Threshold);
                })));
                Assert.That(ok, e.Id);
            }
        }

        [Test]
        public void Trace_has_3_to_5_items_in_order_from_deeds_present_with_real_sources()
        {
            foreach (var id in new[] { "commons", "reopen", "relief", "board", "shut" })
            {
                var g = GoldenRuns.Build(id);
                var t = Endings.Trace(g, id, GoldenRuns.FactSource);
                var present = Deeds.Of(g).ToDictionary(d => d.Id, d => d.Seq);
                Assert.That(t.Count >= 3 && t.Count <= 5, $"{id}: {t.Count}");
                foreach (var item in t)
                {
                    Assert.That(item.Id == "path" || present.ContainsKey(item.Id), item.Id);
                    Assert.That(!string.IsNullOrEmpty(item.Text) && !string.IsNullOrEmpty(item.When), item.Id);
                    Assert.That(item.SourceIds.All(GoldenRuns.IsSource), item.Id);
                }
                var seqs = t.Select(x => x.Id == "path" ? g.Events.Last(e => e.Type == "path").Seq : present[x.Id]).ToList();
                CollectionAssert.AreEqual(seqs.OrderBy(x => x).ToList(), seqs, id);
                Assert.AreEqual(id != "shut", t.Any(x => x.Id == "path"), id);
            }
            var big = Run("stay", GoldenRuns.Commons.Concat(new[] { "card_neighbors", "door_note", "water_for_seoyeon" }).ToArray());
            Assert.AreEqual(5, Endings.Trace(big, "commons", GoldenRuns.FactSource).Count);
            foreach (var id in new[] { "bus", "walk", "stay" }) Assert.That(Endings.Trace(Run(id), id, GoldenRuns.FactSource).Count, Is.LessThanOrEqualTo(5));
            Assert.AreEqual("5장 · D+12 13:00", Endings.Trace(GoldenRuns.Build("board"), "board", GoldenRuns.FactSource).First(x => x.Id == "path").When);
            Assert.That(Endings.Trace(GoldenRuns.Build("board"), "board", null).All(x => x.SourceIds.Count == 0), "no fact lookup: no sources, no error");
        }

        [Test]
        public void Near_misses_at_most_two_never_the_ending_reached_with_a_tip()
        {
            foreach (var f in GoldenRuns.Fix)
            {
                var g = GoldenRuns.Build(f.id);
                var reached = Pick(g).Id;
                var m = Endings.NearMisses(g, reached);
                Assert.That(m.Count <= 2 && m.All(x => x.Id != reached && !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Hint) && !string.IsNullOrEmpty(x.Tip)), f.id);
                if (Endings.Leave.Contains(f.id)) Assert.That(m.All(x => x.Hint.StartsWith("연무동에 남았다면 — ", StringComparison.Ordinal)), f.id);
            }
            StringAssert.Contains(" — ", Endings.NearMisses(GoldenRuns.Build("reopen"), "reopen")[0].Tip);
        }

        // Map labels (C 경사로) and day counters (D+14) are the game's own notation; any other Latin is a leak.
        static readonly Regex LatinOk = new(@"D[+-][0-9]+|[ABC](?= ?(경사로|계단|진출입로|출입구))");
        static readonly string[] Banned = { "활성탄", "입을 벌", "무조건 높은", "지하철로", "마트로 대피", "터널", "생김새", "수상해 보이는", "날고기", "죽었", "사망" };

        [Test]
        public void Content_guard_no_stray_Latin_and_no_unverified_or_grim_wording()
        {
            var strings = new List<string>();
            foreach (var d in Deeds.Table) strings.AddRange(new[] { d.Short, d.Line, d.Cue ?? "" });
            foreach (var e in Endings.All) strings.AddRange(new[] { e.Title, e.Place, e.Hint });
            var runs = GoldenRuns.Fix.Select(f => GoldenRuns.Build(f.id)).ToList();
            runs.Add(GoldenRuns.Run("bus", Deeds.NewIds));
            runs.Add(Run("stay", "elder_candle_tip", "fire_warned", "water_declined", "store_iou", "looter_escaped", "looter_fought", "shelter_water_received", "lead_honest", "yoon_reply"));
            foreach (var g in runs)
            {
                var f = Pick(g);
                strings.Add(f.Title); strings.Add(f.Place);
                strings.AddRange(f.Lines); strings.AddRange(f.People.Select(p => p.Line)); strings.AddRange(f.Town);
                strings.AddRange(f.Trace.SelectMany(t => new[] { t.Text, t.When }));
                strings.AddRange(f.Missed.SelectMany(m => new[] { m.Title, m.Hint, m.Tip }));
            }
            foreach (var s in strings)
            {
                Assert.That(!GoldenRuns.HasLatin(LatinOk.Replace(s, "")), s);
                foreach (var b in Banned) Assert.That(!s.Contains(b), $"{b}: {s}");
            }
        }

        [Test]
        public void An_old_save_with_only_legacy_events_still_gets_an_ending()
        {
            var g = Rules.CreateGame(8);
            g.Extra["karma"] = -5;
            foreach (var (type, d) in new[]
            {
                ("help", "{who:'seoyeon',what:'계단에서 보행기를 옆에서 들어 주었다'}"), ("help", "{who:'manager',what:'마지막까지 건물을 지키며 길을 안내했다'}"),
                ("looter", "{result:'gave'}"), ("candle_left", "{}"), ("path", "{path:'stay'}"),
            }) GameRules.RecordEvent(g, type, JObject.Parse(d));
            g.SetKnow("path", "stay");
            var f = Pick(g);
            Assert.AreEqual("stay", f.Id);
            Assert.That(f.Trace.Count >= 1 && f.People.Count >= 4);
        }

        [Test]
        public void Gallery_sanitised_reads_round_trip_and_unseen_endings_stay_hidden()
        {
            foreach (var raw in new[] { null, "42", "not json", "{\"seen\":[1]}", "{\"seen\":{\"nope\":{\"at\":\"2026-10-30\",\"runs\":1,\"trace\":[]}}}", "{\"seen\":{\"bus\":{\"at\":\"yesterday\"}}}" })
                Assert.IsEmpty(Endings.ReadGallery(raw).Seen, raw);
            Assert.IsEmpty(Endings.ReadGallery((Gallery)null).Seen);
            Assert.IsEmpty(Endings.ReadGallery((JToken)null).Seen);
            GoldenRuns.AssertGolden(Endings.ReadGallery("{\"seen\":{\"bus\":{\"at\":\"2026-10-30\",\"runs\":-2,\"trace\":[\"stairs_asked\",\"evil\",7,\"path\"]}}}"),
                JToken.Parse("{seen:{bus:{at:'2026-10-30',runs:1,trace:['stairs_asked']}}}"));
            var g = Endings.AddToGallery(Endings.ReadGallery((string)null), "reopen", new[] { "path", "clerk_contact", "store_paid", "store_shuttered" }, "2026-10-30");
            AssertSameJson(GoldenRuns.Js(Endings.ReadGallery(JsonConvert.SerializeObject(g))), GoldenRuns.Js(g), "what is stored reads back the same");
            g = Endings.AddToGallery(g, "reopen", new[] { "store_paid" }, "2026-11-02");
            GoldenRuns.AssertGolden(g.Seen["reopen"], JToken.Parse("{at:'2026-10-30',runs:2,trace:['store_paid']}"));
            AssertSameJson(GoldenRuns.Js(Endings.AddToGallery(g, "nope", new string[0], "2026-11-02")), GoldenRuns.Js(g));
            var v = Endings.GalleryView(Endings.AddToGallery(g, "bus", new[] { "stairs_asked", "water_for_seoyeon", "door_note", "card_meet" }, "2026-11-03"));
            Assert.AreEqual(11, v.Count);
            CollectionAssert.AreEqual(Endings.All.Select(e => e.Id), v.Select(x => x.Id));
            GalleryCard bus = v.First(x => x.Id == "bus"), walk = v.First(x => x.Id == "walk");
            Assert.AreEqual("남쪽에서 온 편지", bus.Title); Assert.AreEqual(3, bus.Shorts.Count); Assert.AreEqual("2026-11-03", bus.At);
            Assert.IsFalse(walk.Seen); Assert.IsNull(walk.Title); Assert.IsNull(walk.Place); Assert.That(!string.IsNullOrEmpty(walk.Hint));
        }

        // Carried over from the final build's own ending tests (Round 6): the same intents, read from the deed ledger.
        [Test]
        public void Wording_the_seoyeon_and_missed_openings_and_no_blame_at_low_health()
        {
            var sy = Rules.CreateGame(3); sy.SetKnow("path", "seoyeon");
            StringAssert.DoesNotContain("휠체어", Pick(sy).Lines[0]);
            var m = Rules.CreateGame(3); m.SetKnow("path", "missed");
            StringAssert.DoesNotContain("집결지에 닿았을 때", Pick(m).Lines[0], "missed can happen anywhere");
            foreach (var path in Endings.Paths)
            {
                var g = Rules.CreateGame(3); g.SetKnow("path", path); g.Stats.Health = 20;
                var f = Pick(g);
                foreach (var l in f.Lines.Concat(f.People.Select(p => p.Line)).Concat(f.Town))
                    Assert.That(!l.Contains("탓") && !l.Contains("잘못") && !l.Contains("실수"), l);
            }
        }

        [Test]
        public void Help_pressed_again_or_kindness_received_never_opens_the_shared_kitchen_but_deeds_do()
        {
            var g = Run("stay");
            for (int i = 0; i < 4; i++) GameRules.RecordEvent(g, "help", JObject.Parse("{who:'manager',what:'같은 말'}"));
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'clerk',what:'쪽지와 현금 통',by:'other'}"));
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'seoyeon',kind:'powerbank',what:'보조배터리',by:'other'}"));
            Assert.AreEqual("stay", Pick(g).Id);
            Assert.That(!Pick(g).Lines.Any(l => l.Contains("공동 부엌")));
            Assert.That(Pick(GoldenRuns.Build("commons")).Lines.Any(l => l.Contains("공동 부엌")));
        }

        [Test]
        public void Elder_night_knock_and_day_ledger_give_people_lines_and_one_ledger_line()
        {
            // run('bus') + help elder + help stranger + two ledger days, built by the original (the ledger is not ours to run here)
            var f = Pick(GoldenRuns.Load("ledger-bus"));
            var people = f.People.Select(p => p.Line).ToList();
            Assert.That(people.Any(l => l.Contains("304호 어르신") && l.Contains("안부")));
            Assert.That(people.Any(l => l.Contains("자원봉사자")));
            Assert.AreEqual(1, f.Lines.Count(l => l.Contains("하루치")));
        }

        // ---------- tests/r6-fix1-endings.test.mjs ----------

        [Test]
        public void Stock_is_judged_in_litres_four_500_mL_bottles_are_2_L_not_enough_for_the_days()
        {
            var few = Flat("stay", ("water_500", 4), ("bars", 4));
            Assert.AreEqual(2, Endings.WaterLitres(few));
            Assert.That(Pick(few).Lines.Any(l => l.Contains("바닥났다")));
            var lots = Flat("stay", ("water_2l", 5), ("bars", 4));
            Assert.AreEqual(10, Endings.WaterLitres(lots));
            Assert.That(Endings.WaterLitres(lots) >= Endings.StockOk.Litres);
            var lines = Pick(lots).Lines;
            Assert.That(lines.Any(x => x.Contains("버틸 수 있었다")));
            Assert.That(!lines.Any(x => x.Contains("열흘")), "path-neutral: missed says \"사흘 뒤\"");
        }

        [Test]
        public void Kindness_received_is_no_deed_and_does_not_unlock_the_shared_kitchen()
        {
            var g = Flat("stay");
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'manager',what:'박 소장과 함께 남아 건물을 지키기로 했다'}"));
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'clerk',what:'쪽지와 현금 통',by:'other'}"));
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'seoyeon',what:'보조배터리',by:'other'}"));
            Assert.IsEmpty(Deeds.Of(g));
            Assert.That(!Pick(g).Lines.Any(l => l.Contains("공동 부엌")));
            GameRules.RecordEvent(g, "help", JObject.Parse("{who:'elder',what:'부축했다'}"));
            CollectionAssert.AreEqual(new[] { "elder_alert_knock" }, Deeds.Of(g).Select(d => d.Id).ToList());
        }

        [Test]
        public void Walk_a_collapse_is_not_followed_by_held_out_and_the_leg_is_told_once()
        {
            // an empty flat on the walk path, health 30, two ledger days of one meal: built by the original
            var lines = Pick(GoldenRuns.Load("ledger-walk-weak-skipped")).Lines;
            Assert.That(lines.Any(l => l.Contains("주저앉")));
            Assert.That(lines.Any(l => l.Contains("끼니를 거른 날")), "the skipped-meal line is there, in its weak form");
            Assert.That(!lines.Any(l => l.Contains("마지막까지 버틸 수 있었다")), string.Join(" / ", lines));
            var leg = Flat("walk"); leg.Injury = new List<string> { "leg" };
            Assert.AreEqual(1, Pick(leg).Lines.Count(l => l == Endings.LegWalkLine || l == Endings.LegAfterLine));
            var stay = Flat("stay"); stay.Injury = new List<string> { "leg" };
            Assert.That(Pick(stay).Lines.Any(l => l == Endings.LegAfterLine));
        }

        [Test]
        public void Elder_line_fits_paths_that_leave()
        {
            foreach (var (path, word) in new[] { ("bus", "안부"), ("stay", "302호 몫") })
            {
                var g = Flat(path);
                GameRules.RecordEvent(g, "help", JObject.Parse("{who:'elder',what:'부축했다'}"));
                Assert.That(Pick(g).People.Any(p => p.Who == "elder" && p.Line.Contains(word)), path);
            }
        }

        // ---------- parity with the original ----------

        [Test]
        public void The_endings_table_is_the_originals()
        {
            CollectionAssert.AreEqual(Golden["order"].Select(x => (string)x), Endings.Order);
            CollectionAssert.AreEqual(Golden["paths"].Select(x => (string)x), Endings.Paths);
            Assert.AreEqual((double)Golden["stockOk"]["litres"], Endings.StockOk.Litres);
            Assert.AreEqual((int)Golden["stockOk"]["food"], Endings.StockOk.Food);
            var table = (JArray)Golden["endings"];
            Assert.AreEqual(table.Count, Endings.All.Count);
            for (int i = 0; i < table.Count; i++)
            {
                var e = Endings.All[i];
                var row = (JObject)GoldenRuns.Js(e);
                row["need"] = new JArray(e.Need.Select(n => new JArray(n.Axis, n.Threshold)));
                GoldenRuns.AssertGolden(row, table[i], e.Id);
                Assert.AreSame(e, Endings.ById(e.Id));
            }
            Assert.IsNull(Endings.ById("nope"));
            Assert.IsNull(Endings.ById(null));
        }

        [Test]
        public void Every_golden_run_reaches_the_ending_the_original_reached_with_the_same_words()
        {
            var cases = (JArray)Golden["cases"];
            Assert.That(cases.Count, Is.GreaterThan(200));
            var reached = new HashSet<string>();
            int traces = 0, misses = 0;
            foreach (var c in cases)
            {
                var name = (string)c["name"];
                var g = GoldenRuns.Load(name);
                var fate = Pick(g);
                Assert.AreEqual((string)c["fate"]["id"], fate.Id, $"{name}: ending");
                GoldenRuns.AssertGolden(fate, c["fate"], $"{name}: fate");
                Assert.AreEqual((double)c["waterLitres"], Endings.WaterLitres(g), $"{name}: litres");
                reached.Add(fate.Id);
                foreach (var t in c["traces"] ?? new JArray())
                {
                    GoldenRuns.AssertGolden(Endings.Trace(g, (string)t["id"], GoldenRuns.FactSource), t["trace"], $"{name}: trace towards {t["id"]}");
                    traces++;
                }
                if (c["missedFor"] != null)
                {
                    GoldenRuns.AssertGolden(Endings.NearMisses(g, (string)c["missedFor"]["id"]), c["missedFor"]["list"], $"{name}: near misses of {c["missedFor"]["id"]}");
                    misses++;
                }
            }
            Assert.AreEqual(11, reached.Count, "every ending is reached by some run");
            Assert.That(traces > 100 && misses > 50);
        }

        [Test]
        public void The_gallery_is_read_added_to_and_shown_like_the_original()
        {
            var gal = (JObject)Golden["gallery"];
            foreach (var r in (JArray)gal["reads"])
            {
                Gallery got = (string)r["kind"] switch
                {
                    "undefined" => Endings.ReadGallery((string)null),
                    "string" => Endings.ReadGallery((string)r["raw"]),
                    _ => Endings.ReadGallery(r["raw"]),
                };
                GoldenRuns.AssertGolden(got, r["out"], $"read {r["raw"].ToString(Formatting.None)}");
            }
            var g = Endings.ReadGallery((string)null);
            var adds = (JArray)gal["adds"];
            for (int i = 0; i < adds.Count; i++)
            {
                var a = adds[i];
                var trace = a["trace"].Type == JTokenType.Null ? null : a["trace"].Select(x => (string)x).ToList();
                // stored text and live objects both go in, as in the original run
                g = i % 2 == 1
                    ? Endings.AddToGallery(JsonConvert.SerializeObject(g), (string)a["id"], trace, (string)a["at"])
                    : Endings.AddToGallery(g, (string)a["id"], trace, (string)a["at"]);
                GoldenRuns.AssertGolden(g, a["out"], $"add {i}: {a["id"]}");
                GoldenRuns.AssertGolden(Endings.GalleryView(g), a["view"], $"view after add {i}");
                GoldenRuns.AssertGolden(Endings.GalleryView(JsonConvert.SerializeObject(g)), a["view"], $"view of the stored text after add {i}");
            }
            GoldenRuns.AssertGolden(Endings.GalleryView((Gallery)null), gal["emptyView"]);
        }
    }
}
