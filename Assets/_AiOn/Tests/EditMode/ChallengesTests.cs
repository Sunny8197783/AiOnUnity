using System;
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
    /// Port of the original tests/challenges.test.mjs, plus parity with the original game/challenges.js
    /// (Fixtures/golden-challenges.json over the games of golden-runs.json; regenerate with
    /// Tools/golden/make-golden-challenges.mjs).
    /// </summary>
    public class ChallengesTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-challenges.json");

        static GameState Run(string path, params string[] ids) => GoldenRuns.Run(path, ids);

        static GameState Timed(GameState g, int ms = 60000, int retries = 0)
        {
            foreach (var id in Challenges.Timed) GameRules.RecordEvent(g, "chapter_time", new JObject { ["id"] = id, ["ms"] = ms, ["retries"] = retries });
            return g;
        }

        static Fate Pick(GameState g) => Endings.PickEnding(g, GoldenRuns.FactSource);

        [Test]
        public void Challenges_are_unique_and_only_name_real_deeds()
        {
            Assert.AreEqual(Challenges.All.Count, Challenges.All.Select(c => c.Id).Distinct().Count());
            // The original reads the ids out of each test's source; here each test is run against a context that
            // answers yes, then no, and notes every deed id it was asked about.
            var asked = new HashSet<string>();
            foreach (var answer in new[] { true, false })
            {
                var probe = new ChallengeContext
                {
                    Has = id => { asked.Add(id); return answer; },
                    Ps = new List<DeedPromise>(), St = new RunStats(), Seen = 0,
                };
                foreach (var c in Challenges.All) c.Test(probe);
            }
            Assert.That(asked.Count, Is.GreaterThanOrEqualTo(25));
            foreach (var id in asked) Assert.IsNotNull(Deeds.Get(id), $"unknown deed {id}");
        }

        [Test]
        public void A_run_earns_the_challenges_its_ledger_shows_and_time_ones_need_every_chapter_timed()
        {
            var g = Run("stay", "blackout_radio", "rumor_checked", "rumor_corrected", "seoyeon_contact", "manager_contact", "clerk_contact");
            var done = Challenges.RunChallenges(g, 0);
            foreach (var id in new[] { "info_first", "rumor_stop", "network", "no_hoard", "hands_off" }) CollectionAssert.Contains(done, id);
            foreach (var id in new[] { "clean", "endings3", "precise", "promises" }) CollectionAssert.DoesNotContain(done, id);
            done = Challenges.RunChallenges(Timed(g), 3);
            Assert.That(done.Contains("clean") && done.Contains("endings3"));
            CollectionAssert.DoesNotContain(Challenges.RunChallenges(Run("stay", "rumor_checked", "rumor_corrected", "rumor_spread"), 0), "rumor_stop");
            CollectionAssert.DoesNotContain(Challenges.RunChallenges(Run("stay", "hoard"), 0), "no_hoard");
        }

        [Test]
        public void Info_first_needs_the_blackout_radio_and_the_shelter_rumour_checked_not_any_rumour()
        {
            // "정전되면 라디오부터 켜고, 대피소 소문은 공식 안내로 확인하기": the 뒷산 rumour (3장 D+2), the camp food one
            // (4장) and the evacuation shortcut (5장) are other rumours; checking one of them alone does not earn it.
            foreach (var other in new[] { "mountain_raid", "camp_food", "evac_shortcut" })
            {
                var g = Run("stay", "blackout_radio");
                GameRules.RecordEvent(g, "rumor_checked", new JObject { ["id"] = other });
                CollectionAssert.DoesNotContain(Challenges.RunChallenges(g, 0), "info_first", other);
                GameRules.RecordEvent(g, "rumor_checked", new JObject { ["id"] = "shelter_closed" });
                CollectionAssert.Contains(Challenges.RunChallenges(g, 0), "info_first", other + " then the shelter rumour");
            }
            var noRadio = Run("stay");
            GameRules.RecordEvent(noRadio, "rumor_checked", new JObject { ["id"] = "shelter_closed" });
            CollectionAssert.DoesNotContain(Challenges.RunChallenges(noRadio, 0), "info_first", "the radio first is half of it");
        }

        [Test]
        public void Run_stats_add_chapter_times_and_a_chapter_continued_mid_way_leaves_the_run_incomplete()
        {
            var g = Timed(Run("bus"), 90000, 1);
            var st = Challenges.RunStats(g);
            Assert.AreEqual(90000L * Challenges.Timed.Length, st.Ms);
            Assert.AreEqual(Challenges.Timed.Length, st.Retries);
            Assert.IsTrue(st.Complete);
            GameRules.RecordEvent(g, "chapter_time", JObject.Parse("{id:'dday',ms:1000,retries:0,partial:true}"));
            Assert.IsFalse(Challenges.RunStats(g).Complete);
            Assert.AreEqual("1분 05초", Challenges.FmtMs(65000));
            Assert.AreEqual("1시간 2분", Challenges.FmtMs(3725000));
        }

        [Test]
        public void Records_first_time_challenges_beaten_bests_and_a_top_5_board_ranked_by_challenges_retries_time()
        {
            foreach (var raw in new[] { null, "", "{", "[]", "7",
                "{\"done\":{\"clean\":\"x\",\"nope\":\"2026-11-02\"},\"best\":{\"dday\":-1,\"nope\":5},\"runs\":[{\"at\":\"2026-11-02\",\"ending\":\"nope\",\"n\":1,\"ms\":5,\"retries\":0}]}" })
            {
                var empty = Challenges.ReadRecords(raw);
                Assert.That(empty.Done.Count == 0 && empty.Best.Count == 0 && empty.Runs.Count == 0, raw);
            }
            var slow = Timed(Run("stay", "blackout_radio", "rumor_checked"), 120000);
            var r = Challenges.AddRun((string)null, slow, "stay", 1, "2026-11-01");
            CollectionAssert.Contains(r.News.Fresh, "info_first");
            Assert.IsEmpty(r.News.Bests);
            Assert.AreEqual(1, r.News.Rank);
            var fast = Timed(Run("stay", "blackout_radio", "rumor_checked"), 60000);
            r = Challenges.AddRun(JsonConvert.SerializeObject(r.Rec), fast, "stay", 1, "2026-11-02");
            CollectionAssert.DoesNotContain(r.News.Fresh, "info_first");
            Assert.AreEqual(Challenges.Timed.Length, r.News.Bests.Count);
            Assert.AreEqual(1, r.News.Rank);
            Assert.AreEqual(60000, r.Rec.Best["dday"]);
            var fewer = Timed(Run("bus"), 1000);
            r = Challenges.AddRun(r.Rec, fewer, "bus", 1, "2026-11-02");
            Assert.AreEqual(3, r.News.Rank); // fewer challenges ranks below, however fast
            var rec = r.Rec;
            for (int i = 0; i < 4; i++) rec = Challenges.AddRun(rec, Timed(Run("stay", "blackout_radio", "rumor_checked"), 50000 + i), "stay", 1, "2026-11-03").Rec;
            Assert.AreEqual(5, rec.Runs.Count);
            Assert.That(!rec.Runs.Any(x => x.Ending == "bus"));
            AssertSameJson(GoldenRuns.Js(Challenges.ReadRecords(JsonConvert.SerializeObject(rec))), GoldenRuns.Js(rec), "what is stored reads back the same");
            Assert.IsNull(Challenges.AddRun(rec, Run("stay"), "stay", 1, "2026-11-03").News.Rank); // untimed run: no rank
        }

        [Test]
        public void Share_text_ending_one_square_per_challenge_time_link()
        {
            var g = Timed(Run("stay", "blackout_radio", "rumor_checked"));
            var news = Challenges.AddRun((string)null, g, "stay", 1, "2026-11-02").News;
            var t = Challenges.ShareText(Pick(g), news, Challenges.RunStats(g)).Split('\n');
            StringAssert.StartsWith("〈경보 이전〉 엔딩 5 · ", t[0]);
            Assert.AreEqual(Challenges.All.Count, new System.Globalization.StringInfo(t[1].Substring(t[1].LastIndexOf(' ') + 1)).LengthInTextElements, "one square per challenge");
            Assert.AreEqual("기록 6분 00초 · 다시 하기 0번", t[2]);
            Assert.AreEqual(Challenges.PublicUrl, t[3]);
        }

        [Test]
        public void FinishRun_stores_the_ending_in_the_gallery_and_the_run_in_the_records_together()
        {
            var g = Timed(Run("stay", "blackout_radio", "rumor_checked"));
            var first = Challenges.FinishRun(g, (string)null, null, "2026-11-06", GoldenRuns.FactSource);
            CollectionAssert.AreEqual(new[] { "stay" }, first.Gal.Seen.Keys);
            Assert.AreEqual(1, first.News.Rank);
            Assert.AreEqual(1, first.Rec.Runs.Count);
            CollectionAssert.Contains(first.News.Done, "info_first");
            var again = Challenges.FinishRun(g, JsonConvert.SerializeObject(first.Gal), JsonConvert.SerializeObject(first.Rec), "2026-11-07", GoldenRuns.FactSource);
            Assert.AreEqual(2, again.Gal.Seen["stay"].Runs);
            Assert.IsEmpty(again.News.Fresh);
        }

        // ---------- parity with the original ----------

        [Test]
        public void The_challenge_list_is_the_originals()
        {
            GoldenRuns.AssertGolden(Challenges.All, Golden["challenges"]);
            CollectionAssert.AreEqual(Golden["timed"].Select(x => (string)x), Challenges.Timed);
            Assert.AreEqual((string)Golden["publicUrl"], Challenges.PublicUrl);
            foreach (var t in (JArray)Golden["times"]) Assert.AreEqual((string)t["text"], Challenges.FmtMs((double)t["ms"]), $"{t["ms"]} ms");
        }

        [Test]
        public void Every_golden_run_has_the_stats_and_earns_the_challenges_of_the_original()
        {
            var cases = (JArray)Golden["cases"];
            Assert.That(cases.Count, Is.GreaterThan(200));
            var earned = new HashSet<string>();
            foreach (var c in cases)
            {
                var name = (string)c["name"];
                var g = GoldenRuns.Load(name);
                GoldenRuns.AssertGolden(Challenges.RunStats(g), c["stats"], $"{name}: stats");
                var done0 = Challenges.RunChallenges(g);
                GoldenRuns.AssertGolden(done0, c["done0"], $"{name}: challenges");
                var done3 = Challenges.RunChallenges(g, 3);
                GoldenRuns.AssertGolden(done3, c["done3"], $"{name}: challenges with three endings seen");
                earned.UnionWith(done0); earned.UnionWith(done3);
            }
            Assert.AreEqual(Challenges.All.Count, earned.Count, "every challenge is earned by some run");
        }

        [Test]
        public void Stored_records_are_sanitised_like_the_original()
        {
            foreach (var r in (JArray)Golden["reads"])
            {
                Records got = (string)r["kind"] switch
                {
                    "undefined" => Challenges.ReadRecords((string)null),
                    "string" => Challenges.ReadRecords((string)r["raw"]),
                    _ => Challenges.ReadRecords(r["raw"]),
                };
                GoldenRuns.AssertGolden(got, r["out"], $"read {r["raw"].ToString(Formatting.None)}");
            }
        }

        [Test]
        public void The_records_board_grows_run_by_run_like_the_original()
        {
            Records rec = null;
            var board = (JArray)Golden["board"];
            Assert.That(board.Count, Is.GreaterThan(40));
            int ranked = 0;
            foreach (var step in board)
            {
                var g = GoldenRuns.Load((string)step["run"]);
                string ending = (string)step["ending"], at = (string)step["at"];
                int seen = (int)step["seen"];
                // stored text and live objects both go in, as in the original run
                var r = (bool)step["asText"] ? Challenges.AddRun(JsonConvert.SerializeObject(rec), g, ending, seen, at) : Challenges.AddRun(rec, g, ending, seen, at);
                GoldenRuns.AssertGolden(r.Rec, step["rec"], $"{step["run"]} on {at}: records");
                AssertSameJson(JToken.FromObject(r.News), step["news"], $"{step["run"]} on {at}: news");
                rec = r.Rec;
                if (r.News.Rank != null) ranked++;
            }
            Assert.That(ranked, Is.GreaterThan(10));
        }

        [Test]
        public void Finished_runs_fill_the_gallery_and_the_records_like_the_original()
        {
            Gallery gal = null;
            Records rec = null;
            foreach (var step in (JArray)Golden["finished"])
            {
                var g = GoldenRuns.Load((string)step["run"]);
                var at = (string)step["at"];
                var galIn = (bool)step["galAsText"] ? Endings.ReadGallery(JsonConvert.SerializeObject(gal)) : gal;
                var recIn = (bool)step["recAsText"] ? Challenges.ReadRecords(JsonConvert.SerializeObject(rec)) : rec;
                var r = Challenges.FinishRun(g, galIn, recIn, at, GoldenRuns.FactSource);
                GoldenRuns.AssertGolden(r.Gal, step["gal"], $"{step["run"]} on {at}: gallery");
                GoldenRuns.AssertGolden(r.Rec, step["rec"], $"{step["run"]} on {at}: records");
                AssertSameJson(JToken.FromObject(r.News), step["news"], $"{step["run"]} on {at}: news");
                // the stored texts give the same result
                var viaText = Challenges.FinishRun(g, gal == null ? null : JsonConvert.SerializeObject(gal), rec == null ? null : JsonConvert.SerializeObject(rec), at, GoldenRuns.FactSource);
                AssertSameJson(JToken.FromObject(viaText), JToken.FromObject(r), $"{step["run"]} on {at}: from stored text");
                gal = r.Gal; rec = r.Rec;
            }
        }

        [Test]
        public void Share_texts_match_the_original()
        {
            foreach (var s in (JArray)Golden["shares"])
            {
                var g = GoldenRuns.Load((string)s["run"]);
                var fate = Pick(g);
                var r = Challenges.AddRun((string)null, g, fate.Id, 1, "2026-11-02");
                AssertSameJson(JToken.FromObject(r.News), s["news"], $"{s["run"]}: news");
                Assert.AreEqual((string)s["text"], Challenges.ShareText(fate, r.News, Challenges.RunStats(g)), $"{s["run"]}: share text");
                Assert.AreEqual((string)s["text"], Challenges.ShareText(fate, (JObject)s["news"], Challenges.RunStats(g)), $"{s["run"]}: share text from the stored news");
                Assert.AreEqual((string)s["noNews"], Challenges.ShareText(fate, (RunNews)null, Challenges.RunStats(g)), $"{s["run"]}: share text without news");
            }
        }
    }
}
