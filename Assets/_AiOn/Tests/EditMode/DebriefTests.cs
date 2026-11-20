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
    /// Port of the original tests/debrief.test.mjs and tests/r6-fix3-debrief.test.mjs (and the debrief case of
    /// r6-fix4-ledger.test.mjs), plus parity with the original game/debrief.js (Fixtures/golden-debrief.json over the
    /// games of golden-runs.json; regenerate with Tools/golden/make-golden-debrief.mjs).
    /// Games that need the day ledger played out are loaded from golden-runs.json, where the original built them.
    /// </summary>
    public class DebriefTests
    {
        static JObject _golden;
        static JObject Golden => _golden ??= GoldenRuns.ParseFixture("golden-debrief.json");

        GameRules R;
        Dictionary<string, int> prices;

        [SetUp]
        public void SetUp()
        {
            R = Rules;
            prices = Db.items.ToDictionary(i => i.id, i => i.price);
        }

        GameState Game(params (string id, int n)[] cart)
        {
            var g = R.CreateGame(5);
            g.Money = 10_000_000;
            R.Buy(g, Cart(cart), prices);
            return g;
        }

        FailCard Fail(GameState g, string type, string detail = null) =>
            Debrief.FailDebrief(R, g, type, detail == null ? null : JObject.Parse(detail), GoldenRuns.Fact, GoldenRuns.FactSource);
        ChapterSummary Summary(GameState g, string chapter) => Debrief.ChapterSummary(R, g, chapter);
        EndingRecord Record(GameState g) => Debrief.EndingRecord(R, g, GoldenRuns.Fact, GoldenRuns.FactSource);
        static GameEvent Rec(GameState g, string type, string details = "{}") => GameRules.RecordEvent(g, type, JObject.Parse(details));
        static List<string> Src(params string[] factIds) => factIds.Select(GoldenRuns.FactSource).Distinct().ToList();

        static readonly string[] Blame = { "잘못", "실수", "탓", "게으", "무책임", "undefined", "null", "NaN", "[object" };

        GameState AlertReadyGame()
        {
            var g = Game(("water_500", 1));
            foreach (var k in new[] { "shelterLocated", "entrancesChecked", "routeChecked", "extinguisherLocated", "gateClosedSeen" }) g.SetKnow(k, true);
            R.Pack(g, "water_500");
            return g;
        }

        [Test]
        public void FailDebrief_gives_three_calm_Korean_lines_and_real_sources_for_every_type()
        {
            CollectionAssert.AreEqual(new[] { "dead", "late", "exhausted", "fire", "lostWay" }, Debrief.FailTypes);
            var fresh = R.CreateGame(9);
            foreach (var g in new[] { fresh, AlertReadyGame() })
                foreach (var type in Debrief.FailTypes)
                {
                    var d = Fail(g, type);
                    foreach (var (k, text) in new[] { ("title", d.Title), ("seen", d.Seen), ("missed", d.Missed), ("next", d.Next) })
                    {
                        Assert.That(!string.IsNullOrWhiteSpace(text) && GoldenRuns.HasHangul(text), $"{type}.{k}");
                        foreach (var b in Blame) Assert.That(!text.Contains(b), $"{type}.{k}: {text}");
                    }
                    Assert.IsNotEmpty(d.SourceIds);
                    Assert.AreEqual(d.SourceIds.Count, d.SourceIds.Distinct().Count());
                    foreach (var s in d.SourceIds) Assert.That(GoldenRuns.IsSource(s), s);
                }
            Assert.Catch(() => Fail(fresh, "boom"));
            Assert.Catch(() => Fail(fresh, null));
        }

        [Test]
        public void Late_lines_follow_what_was_checked()
        {
            var g = R.CreateGame(1);
            var d = Fail(g, "late");
            StringAssert.Contains("확인 전", d.Seen);
            StringAssert.Contains("대피소·경로 확인", d.Next);
            g.SetKnow("shelterLocated", true);
            StringAssert.Contains("생활문화센터", Fail(g, "late").Seen);
            foreach (var k in new[] { "entrancesChecked", "routeChecked", "gateClosedSeen" }) g.SetKnow(k, true);
            d = Fail(g, "late");
            StringAssert.Contains("동쪽 출입구", d.Seen);
            StringAssert.Contains("비상가방 꾸리기", d.Next);
        }

        [Test]
        public void Exhausted_overweight_bag_no_water_or_no_rest()
        {
            var g = Game(("water_2l", 2));
            R.Pack(g, "water_2l", 4);
            var d = Fail(g, "exhausted");
            Assert.That(d.Missed.Contains("8.2kg") && d.Missed.Contains("8kg"), d.Missed);
            CollectionAssert.Contains(d.SourceIds, GoldenRuns.FactSource("bag_foil"));
            var dry = R.CreateGame(2);
            StringAssert.Contains("마실 물", Fail(dry, "exhausted").Missed);
            StringAssert.Contains("휴식", Fail(AlertReadyGame(), "exhausted").Next);
            StringAssert.Contains("갈증은 좋음", Fail(dry, "exhausted").Seen);
            GameRules.Adjust(dry, "thirst", 80);
            StringAssert.Contains("갈증은 나쁨", Fail(dry, "exhausted").Seen);
        }

        [Test]
        public void Fire_the_last_extinguisher_encounter_picks_the_fact_line()
        {
            var g = R.CreateGame(3);
            Assert.AreEqual(GoldenRuns.Fact("fire_shout"), Fail(g, "fire").Missed);
            StringAssert.Contains("확인하기 전", Fail(g, "fire").Seen);
            GoldenRuns.Encounter(g, "extinguisher", "escalated"); // what the evacuation chapter passes
            Assert.AreEqual(GoldenRuns.Fact("fire_big"), Fail(g, "fire").Missed);
            // evalExtinguisher(['shout', 'aim', 'pin', 'squeeze', 'sweep'], { fire: 'small' }) in the original
            GoldenRuns.Encounter(g, "extinguisher", JObject.Parse("{result:'incomplete',correct:false,firstError:'pin'}"));
            var d = Fail(g, "fire");
            Assert.AreEqual(GoldenRuns.Fact("ext_1"), d.Missed, "first error was the pin");
            CollectionAssert.Contains(d.SourceIds, "mois_extinguisher");
            g.SetKnow("extinguisherLocated", true);
            StringAssert.Contains("알고 있었습니다", Fail(g, "fire").Seen);
        }

        [Test]
        public void LostWay_the_map_app_fails_from_the_blackout_not_a_seeded_clock()
        {
            var l = R.CreateGame(11);
            var d = Fail(l, "lostWay");
            StringAssert.Contains("정전 뒤부터", d.Seen);
            Assert.AreEqual(GoldenRuns.Fact("shelter_signs"), d.Missed);
            l.SetKnow("routeChecked", true);
            StringAssert.Contains("두 번째 경로", Fail(l, "lostWay").Next);
        }

        [Test]
        public void ChapterSummary_counts_only_that_chapter()
        {
            var g = Game();
            g.Chapter = "prep";
            GameRules.SpendSlot(g, "mart", 2);
            GameRules.SpendSlot(g, "bag", 1);
            R.Buy(g, Cart(("water_500", 3), ("batteries_aa", 1)), prices);
            R.Check(g, "flashlight");
            GoldenRuns.Encounter(g, "bag", "partial");
            GoldenRuns.Encounter(g, "extinguisher", JObject.Parse("{result:'retreat-ok',correct:true,firstError:null}")); // evalExtinguisher(['retreat'], { fire: 'small' })
            GameRules.Note(g, "라디오 89.1MHz 재난방송", "official");
            GameRules.Note(g, "내일 물이 끊긴대", "rumor");
            Rec(g, "help", "{who:'seoyeon',what:'연락 방법을 정했다'}");
            Rec(g, "payoff", "{prep:'bag',moment:'현관에 둔 배낭을 메고 바로 나섰다'}");
            g.Chapter = "dday";
            Rec(g, "payoff", "{prep:'radio',moment:'안내를 바로 들었다'}");

            var s = Summary(g, "prep");
            CollectionAssert.AreEqual(new[]
            {
                "준비에 쓴 시간: 3칸",
                "산 물건: 생수 500mL 3병, AA 건전지 4개 1팩",
                "점검해서 쓸 수 있게 된 것: 손전등(LED)",
                "비상가방: 몇 가지가 빠졌습니다",
                "소화기: 대피하며 알렸습니다",
                "수첩에 적은 정보 2개 (공식 안내 1, 소문·미확인 1)",
                "오서연: 연락 방법을 정했다",
                "지금 상태: 체력 좋음, 갈증 좋음, 피로 좋음, 긴장 좋음",
            }, s.Lines);
            GoldenRuns.AssertGolden(s.Payoffs, JToken.Parse("[{prep:'비상가방',moment:'현관에 둔 배낭을 메고 바로 나섰다',id:'bag'}]"));
            var d = Summary(g, "dday");
            Assert.AreEqual(1, d.Lines.Count);
            CollectionAssert.AreEqual(new[] { "radio" }, d.Payoffs.Select(p => p.Id).ToList());
            GoldenRuns.AssertGolden(Summary(R.CreateGame(1), "nothing"), JToken.Parse("{lines:['지금 상태: 체력 좋음, 갈증 좋음, 피로 좋음, 긴장 좋음'],payoffs:[]}"));
            GameRules.Adjust(g, "thirst", 80);
            GameRules.Adjust(g, "fatigue", 30);
            GameRules.Adjust(g, "health", -75);
            Assert.AreEqual("지금 상태: 체력 나쁨, 갈증 나쁨, 피로 보통, 긴장 좋음", Summary(g, "x").Lines[0]);
        }

        [Test]
        public void No_raw_result_id_or_prep_id_reaches_the_player()
        {
            // Every result, tool, prep id, person and contact the debrief can name, with two ledger days: built by the original.
            var g = GoldenRuns.Load("db-every-word");
            var s = Summary(g, "c");
            Assert.That(s.Lines.Count, Is.GreaterThan(30));
            string[] fine = { "AA", "LED", "mL", "L", "undefined" }; // units and item names (2L, 500mL, AA, LED); 'undefined' is the original's print of a missing who / what
            foreach (var line in s.Lines)
            {
                var rest = fine.Aggregate(line, (t, w) => t.Replace(w, ""));
                if (line.Contains("something-new") || line.StartsWith("passerby", StringComparison.Ordinal)) continue; // an id the tables do not know is shown as it is
                Assert.That(!GoldenRuns.HasLatin(rest), line);
            }
            var rec = Record(g);
            foreach (var p in s.Payoffs.Concat(rec.Helped).Where(p => p.Id != null && p.Id != "mystery")) Assert.AreNotEqual(p.Prep, p.Id, $"label for payoff '{p.Id}'");
            foreach (var c in new[] { "sms", "door", "both", "office", "store" })
            {
                g.Rel["seoyeon"].Contact = c;
                foreach (var p in Record(g).People.Where(p => p.Who == "오서연")) Assert.That(!GoldenRuns.HasLatin(p.Who + p.What), p.What);
            }
            foreach (var x in rec.Ripples) Assert.That(!GoldenRuns.HasLatin(x.Label + x.Cause.Text));
        }

        [Test]
        public void EndingRecord_on_a_fresh_game()
        {
            var r = Record(R.CreateGame(1));
            Assert.IsEmpty(r.Helped);
            GoldenRuns.AssertGolden(r.Unused, new JArray(new JObject { ["item"] = Db.Get("flashlight").displayName, ["why"] = "건전지를 넣지 않아 켜 볼 수 없었습니다." }));
            Assert.AreEqual(9, r.Unchecked.Count);
            Assert.IsEmpty(r.People);
            Assert.AreEqual(GoldenRuns.Fact("shelter_find"), r.Practice.Text);
            Assert.AreEqual("kr_shelters", r.Practice.SourceId);
            Assert.IsNull(r.Run);
        }

        [Test]
        public void EndingRecord_follows_payoffs_unused_items_know_flags_and_people()
        {
            var g = Game(("radio", 1), ("batteries_aa", 1), ("powerbank", 1), ("tape", 1), ("water_500", 2));
            R.Check(g, "radio");
            R.Consume(g, "water_500", 1);
            g.Items["bogus"] = new ItemEntry(1) { Bought = 1 }; // dropped from the catalogue
            Rec(g, "payoff", "{prep:'flashlight',moment:'정전 직후 손전등을 바로 켰다'}");
            Rec(g, "help", "{who:'seoyeon',what:'계단에서 보행기를 옆에서 들어 주었다'}");
            g.Rel["manager"].Contact = "both";
            g.Rel["seoyeon"].Contact = "sms";
            g.SetKnow("shelterLocated", true);
            var r = Record(g);
            GoldenRuns.AssertGolden(r.Helped, JToken.Parse("[{prep:'손전등 건전지',moment:'정전 직후 손전등을 바로 켰다',id:'flashlight'}]"));
            var why = r.Unused.ToDictionary(u => u.Item, u => u.Why);
            string Name(string id) => Db.Get(id).displayName;
            CollectionAssert.AreEquivalent(new[] { "flashlight", "radio", "powerbank", "tape" }.Select(Name).ToList(), why.Keys);
            Assert.AreEqual("충전해 두지 않아 쓸 수 없었습니다.", why[Name("powerbank")]);
            Assert.AreEqual("사 두었지만 쓸 일이 없었습니다.", why[Name("tape")]);
            Assert.That(!why.ContainsKey(Name("water_500")) && !why.ContainsKey(Name("batteries_aa")), "used items are not unused");
            CollectionAssert.DoesNotContain(r.Unchecked, "가까운 대피소 위치");
            GoldenRuns.AssertGolden(r.People, JToken.Parse(@"[
                {who:'오서연',what:'계단에서 보행기를 옆에서 들어 주었다 · 비상 연락 방법을 정했습니다 (문자)'},
                {who:'박영수 소장',what:'비상 연락 방법을 정했습니다 (문자·문에 쪽지)'}]"));
            Assert.AreEqual(GoldenRuns.Fact("shelter_signs"), r.Practice.Text, "next unchecked flag");
            Assert.AreEqual("law_sign", r.Practice.SourceId);

            foreach (var k in new[] { "entrancesChecked", "routeChecked", "extinguisherLocated", "radioChecked", "contactsCard", "docsCopied", "waterPoint" }) g.SetKnow(k, true);
            g.SetKnow("meetPoint", "놀이터");
            var all = Record(g);
            Assert.IsEmpty(all.Unchecked);
            Assert.AreEqual(GoldenRuns.Fact("shelter_know"), all.Practice.Text);
            Assert.AreEqual("mois_four", all.Practice.SourceId);
            Assert.That(GoldenRuns.IsSource(all.Practice.SourceId));
        }

        [Test]
        public void Fire_fail_card_follows_the_scene_it_came_from()
        {
            var g = R.CreateGame(1);
            FailCard up = Fail(g, "fire", "{scene:'upstairs'}"), sh = Fail(g, "fire", "{scene:'shelter'}"), co = Fail(g, "fire");
            Assert.That(up.Missed.Contains("창문을 닫고") || up.Missed.Contains("대기"));
            Assert.AreNotEqual(co.Title, up.Title); Assert.AreNotEqual(co.Title, sh.Title);
            foreach (var d in new[] { up, sh }) Assert.That(d.SourceIds.Count > 0 && !string.IsNullOrEmpty(d.Next));
        }

        [Test]
        public void LostWay_via_rumor_three_calm_lines_and_the_info_sources()
        {
            var g = R.CreateGame(2);
            var d = Fail(g, "lostWay", "{via:'rumor'}");
            Assert.AreEqual("소문을 따라가느라 길을 잃었습니다", d.Title);
            Assert.AreEqual(GoldenRuns.Fact("info_rumor"), d.Missed);
            StringAssert.Contains("한 번 더 확인", d.Next);
            foreach (var text in new[] { d.Seen, d.Missed, d.Next }) foreach (var b in Blame) Assert.That(!text.Contains(b), text);
            CollectionAssert.AreEqual(Src("info_rumor", "info_harm", "evac_note"), d.SourceIds);
            Assert.AreNotEqual(d.Title, Fail(g, "lostWay").Title, "the plain lostWay card stays");
        }

        [Test]
        public void Chapter_summary_lists_trades_the_day_ledger_and_flat_batteries_in_Korean()
        {
            // bars x3 and a checked radio; in 'endure' a trade with the 102동 주민, two ledger days on the powerbank, the radio run flat: built by the original
            var g = GoldenRuns.Load("db-trade-ledger-flat");
            var lines = Summary(g, "endure").Lines;
            CollectionAssert.Contains(lines, "물물교환(102동 주민): 건빵·에너지바 3봉 → AA 건전지(쓰던 것) 1팩");
            CollectionAssert.Contains(lines, "하루 배분: 물 2L · 두 끼 · 보조배터리");
            CollectionAssert.Contains(lines, "건전지가 다 닳은 것: 휴대용 라디오(건전지식)");
            var why = Record(g).Unused.ToDictionary(u => u.Item, u => u.Why);
            Assert.AreEqual("건전지가 다 닳아 더 켤 수 없었습니다.", why["휴대용 라디오(건전지식)"]);
        }

        [Test]
        public void EndingRecord_one_people_row_per_person_new_neighbours_named_ripples_curtain_counted()
        {
            var g = Game(("blackout_curtain", 1));
            for (int i = 0; i < 3; i++) Rec(g, "help", "{who:'manager',what:'마지막까지 건물을 지키며 길을 안내했다'}");
            Rec(g, "help", "{who:'seoyeon',what:'계단에서 보행기를 옆에서 들어 주었다'}");
            Rec(g, "help", "{who:'seoyeon',what:'급수소에서 물 한 통을 받아 3층까지 날라 주었다'}");
            Rec(g, "help", "{who:'elder',what:'304호 어르신을 부축해 함께 내려갔다'}");
            Rec(g, "help", "{who:'stranger',what:'정보'}");
            var r = Record(g);
            GoldenRuns.AssertGolden(r.People, JToken.Parse(@"[
                {who:'박영수 소장',what:'마지막까지 건물을 지키며 길을 안내했다'},
                {who:'오서연',what:'계단에서 보행기를 옆에서 들어 주었다 · 급수소에서 물 한 통을 받아 3층까지 날라 주었다'},
                {who:'304호 어르신',what:'304호 어르신을 부축해 함께 내려갔다'},
                {who:'문을 두드린 이웃',what:'정보'}]"));
            Assert.IsEmpty(r.Ripples);
            Assert.That(r.Unused.Any(u => u.Item == "암막 커튼"), "bought and never hung");
            g.SetKnow("curtain", true);
            Assert.That(!Record(g).Unused.Any(u => u.Item == "암막 커튼"), "hung: used");
            g.Chapter = "prep"; Rec(g, "tub_filled"); g.Day = 1; Rec(g, "payoff", "{prep:'tub',moment:'세수했다'}");
            Assert.AreEqual(1, Record(g).Ripples.Count);
        }

        [Test]
        public void ChapterSummary_names_up_to_three_remembered_deeds_of_that_chapter()
        {
            var g = R.CreateGame(1);
            g.Chapter = "dday";
            Rec(g, "help", "{who:'seoyeon',kind:'ask',what:'어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다'}");
            Deeds.Remember(g, "hall_checked"); // no cue
            Rec(g, "elder_choice", "{pick:'help'}"); // the 304 dilemma
            foreach (var id in new[] { "manager_detour", "register_helped" }) Deeds.Remember(g, id);
            var s = Summary(g, "dday").Lines;
            Assert.AreEqual("기억된 일: 서연 — 계단에서 먼저 물었다 · 304호 어르신 — 경보 때 함께 계단을 내려갔다 · 박 소장 — 관리사무소에 들러 연락을 정했다",
                s.First(l => l.StartsWith("기억된 일", StringComparison.Ordinal)));
            Assert.That(!Summary(g, "prep").Lines.Any(l => l.StartsWith("기억된 일", StringComparison.Ordinal)));
        }

        [Test]
        public void EndingRecord_carries_the_full_fate()
        {
            var g = R.CreateGame(1);
            g.SetKnow("path", "stay");
            Rec(g, "path", "{path:'stay'}");
            var fate = Record(g).Fate;
            Assert.AreEqual("stay", fate.Id);
            Assert.That(fate.No == 5 && fate.Title != null && fate.Place != null && fate.Kind == "base" && fate.Path == "stay");
            Assert.That(fate.Lines != null && fate.People != null && fate.Town != null && fate.Trace != null && fate.Missed != null);
        }

        // ---------- tests/r6-fix3-debrief.test.mjs ----------

        [Test]
        public void The_stay_branch_smoke_judgement_is_a_fire_judgement_not_an_extinguisher_use()
        {
            var g = R.CreateGame(4);
            g.Chapter = "evac";
            Rec(g, "smoke_choice", "{pick:'stay'}");
            GoldenRuns.Encounter(g, "extinguisher", "retreat-ok");
            var lines = Summary(g, "evac").Lines;
            CollectionAssert.Contains(lines, "화재 판단: 문을 닫고 신고한 뒤 안내를 기다렸습니다");
            Assert.That(!lines.Any(l => l.StartsWith("소화기", StringComparison.Ordinal)));
            var r = R.CreateGame(4);
            r.Chapter = "evac";
            Rec(r, "smoke_choice", "{pick:'run'}");
            GoldenRuns.Encounter(r, "extinguisher", "risky");
            CollectionAssert.Contains(Summary(r, "evac").Lines, "화재 판단: 연기 속으로 바로 나섰습니다");
        }

        [Test]
        public void Dead_by_smoke_indoor_smoke_card_with_fire_big_and_fire_lift_and_a_towel_aware_missed_line()
        {
            var g = R.CreateGame(4);
            var d = Fail(g, "dead", "{why:'smoke'}");
            Assert.AreEqual("연기를 너무 많이 마셨습니다", d.Title);
            Assert.AreEqual(GoldenRuns.Fact("fire_big"), d.Next);
            StringAssert.Contains("젖은 수건", d.Missed);
            CollectionAssert.AreEqual(Src("fire_big", "fire_lift"), d.SourceIds);
            Rec(g, "towel_wet", "{with:'tub'}");
            StringAssert.Contains("몸을 낮추지", Fail(g, "dead", "{why:'smoke'}").Missed);
            StringAssert.DoesNotContain("바람이 불어오는", Fail(g, "dead", "{why:'fire'}").Next);
        }

        [Test]
        public void Dead_and_exhausted_cards_cite_related_sources()
        {
            var g = R.CreateGame(4);
            CollectionAssert.AreEqual(Src("alert_stairs"), Fail(g, "dead", "{why:'fall'}").SourceIds);
            CollectionAssert.AreEqual(Src("people_order"), Fail(g, "dead", "{why:'fight'}").SourceIds);
            CollectionAssert.AreEqual(Src("home_stock"), Fail(g, "dead", "{why:'hurt'}").SourceIds);
            CollectionAssert.AreEqual(Src("home_stock"), Fail(g, "dead").SourceIds);
            CollectionAssert.AreEqual(Src("water_point", "water_sphere"), Fail(g, "exhausted", "{why:'carry'}").SourceIds);
        }

        [Test]
        public void Late_a_packed_bag_by_the_door_is_not_told_to_pack_again()
        {
            var g = R.CreateGame(4);
            foreach (var k in new[] { "shelterLocated", "entrancesChecked", "routeChecked", "bagPacked" }) g.SetKnow(k, true);
            var d = Fail(g, "late"); // never left home
            Assert.AreEqual("경보가 울린 뒤에도 집을 나서지 않았습니다.", d.Missed);
            Assert.AreEqual(GoldenRuns.Fact("bag_take"), d.Next);
            Rec(g, "departure", "{bag:'packed'}");
            d = Fail(g, "late");
            StringAssert.DoesNotContain("비상가방 꾸리기", d.Next);
            Rec(g, "departure", "{bag:'late',delay:4}");
            StringAssert.Contains("비상가방 꾸리기", Fail(g, "late").Next);
        }

        // ---------- tests/r6-fix4-ledger.test.mjs (S3) ----------

        [Test]
        public void A_ledger_that_cooked_and_fetched_marks_the_stove_and_jug_used()
        {
            // stove, butane, jug and ramen bought, then five ledger days of three meals: built by the original
            var names = Record(GoldenRuns.Load("db-ledger-tools-used")).Unused.Select(u => u.Item).ToList();
            CollectionAssert.DoesNotContain(names, Db.Get("gas_stove").displayName);
            CollectionAssert.DoesNotContain(names, Db.Get("water_jug").displayName);
        }

        // ---------- parity with the original ----------

        [Test]
        public void The_tables_are_the_originals()
        {
            CollectionAssert.AreEqual(Golden["failTypes"].Select(x => (string)x), Debrief.FailTypes);
            GoldenRuns.AssertGolden(Debrief.People, Golden["people"]);
            // the competition upgrade's prep ids (Ripples.PortAdded, e.g. the balcony partition) come after the original's
            GoldenRuns.AssertGolden(Debrief.Prep.Where(kv => !Ripples.PortAdded.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), Golden["prep"]);
        }

        /// <summary>
        /// Port deviation: the debrief's stat words use the HUD's bands (GameRules.StatWord, 67 / 34) instead of the
        /// original debrief's 60 / 30, so a card never contradicts the HUD beside it. The goldens are compared with the
        /// words masked; the words themselves are covered by MenuPolishTests.Stat_word_bands and the tests above.
        /// </summary>
        static JToken NoStatWords(JToken t)
        {
            var c = t.DeepClone();
            foreach (var v in c.SelectTokens("$..*").OfType<JValue>().Where(v => v.Type == JTokenType.String).ToList())
                v.Value = System.Text.RegularExpressions.Regex.Replace((string)v.Value, "(체력|갈증|피로|긴장)(은|는)? (좋음|보통|나쁨)", "$1$2 _");
            if (c is JValue jv && jv.Type == JTokenType.String)
                jv.Value = System.Text.RegularExpressions.Regex.Replace((string)jv.Value, "(체력|갈증|피로|긴장)(은|는)? (좋음|보통|나쁨)", "$1$2 _");
            return c;
        }

        [Test]
        public void Every_golden_run_gets_the_fail_cards_summaries_and_record_of_the_original()
        {
            var cases = (JArray)Golden["cases"];
            Assert.That(cases.Count, Is.GreaterThan(200));
            int cards = 0, summaries = 0, records = 0;
            foreach (var c in cases)
            {
                var name = (string)c["name"];
                var g = GoldenRuns.Load(name);
                foreach (var p in ((JObject)c["chapters"]).Properties())
                {
                    GoldenRuns.AssertGolden(NoStatWords(GoldenRuns.Js(Summary(g, p.Name))), NoStatWords(p.Value), $"{name}: summary of '{p.Name}'");
                    summaries++;
                }
                foreach (var f in c["fails"] ?? new JArray())
                {
                    var detail = f["detail"].Type == JTokenType.Null ? null : (JObject)f["detail"];
                    var card = Debrief.FailDebrief(R, g, (string)f["type"], detail, GoldenRuns.Fact, GoldenRuns.FactSource);
                    GoldenRuns.AssertGolden(NoStatWords(GoldenRuns.Js(card)), NoStatWords(f["out"]), $"{name}: fail card {f["type"]} {f["detail"].ToString(Formatting.None)}");
                    cards++;
                }
                if (c["record"] is JObject exp)
                {
                    var rec = Record(g);
                    Assert.AreEqual((string)exp["fateId"], rec.Fate.Id, $"{name}: the record's ending");
                    AssertSameJson(GoldenRuns.Js(rec.Fate), GoldenRuns.Js(Endings.PickEnding(g, GoldenRuns.FactSource)), $"{name}: the record carries the whole fate");
                    var got = (JObject)GoldenRuns.Js(rec);
                    got.Remove("fate");
                    got["fateId"] = rec.Fate.Id;
                    GoldenRuns.AssertGolden(got, exp, $"{name}: record");
                    records++;
                }
            }
            Assert.That(cards > 400 && summaries > 300 && records > 150, $"{cards} cards, {summaries} summaries, {records} records");
        }
    }
}
