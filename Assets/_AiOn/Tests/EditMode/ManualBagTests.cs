using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>The hand-packed bag (직접 놓기): grid, model, score and the mart's 사재기 rule. BagPacker stays pinned by its own tests.</summary>
    public class ManualBagTests
    {
        static BagItemView V(string id, int home, int bag = 0, double kg = 0.1, bool ready = true) =>
            new() { Id = id, Name = id, Home = home, Bag = bag, WeightKg = kg, VolumeL = 0.1, Ready = ready, Tags = new List<string>() };

        static readonly Dictionary<string, (int, int)> Sizes = new()
        {
            ["stove"] = (4, 3), ["water_2l"] = (3, 2), ["radio"] = (2, 1), ["batteries_aa"] = (1, 1), ["bars"] = (1, 1),
            ["id_card"] = (1, 1), ["flashlight"] = (1, 1), ["tissue"] = (1, 1), ["masks"] = (1, 1), ["meds"] = (1, 1), ["water_500"] = (2, 1),
        };

        static ManualBagModel M(params BagItemView[] items) =>
            new(items, id => Sizes.TryGetValue(id, out var s) ? s : ((int, int)?)null, 10, 5);

        [Test]
        public void Units_land_at_the_first_free_spot_and_stay_where_they_were_put()
        {
            var m = M(V("radio", 2), V("bars", 3));
            var a = m.PutIn("radio");
            Assert.AreEqual((0, 0, 2, 1), (a.X, a.Y, a.W, a.H));
            var b = m.PutAt("bars", 5, 3, false);
            Assert.IsNotNull(b);
            Assert.AreEqual((5, 3), (b.X, b.Y));
            Assert.IsNull(m.PutAt("bars", 1, 0, false), "a cell taken by the radio");
            Assert.AreEqual("spot", m.LastFail);
            Assert.AreEqual(2, m.HomeOf("bars"));
            Assert.IsTrue(m.Move(a, 7, 4, false));
            Assert.AreEqual((7, 4), (a.X, a.Y));
            Assert.IsFalse(m.Move(a, 9, 4, false), "it would stick out of the bag");
            Assert.AreEqual((7, 4), (a.X, a.Y), "a move that does not fit changes nothing");
        }

        [Test]
        public void R_turns_a_unit_and_a_turned_unit_needs_its_turned_space()
        {
            var m = M(V("radio", 1));
            var p = m.PutAt("radio", 9, 0, true);
            Assert.IsNotNull(p, "turned, the 2x1 radio is 1x2 and fits in the last column");
            Assert.IsTrue(p.Rotated);
            Assert.AreEqual((1, 2), (p.W, p.H));
            Assert.IsFalse(m.Move(p, 9, 0, false), "upright it would stick out");
            Assert.IsTrue(m.Move(p, 8, 0, false));
            Assert.IsFalse(p.Rotated);
        }

        [Test]
        public void A_scattered_bag_has_no_spot_but_tidy_makes_room_and_full_means_full()
        {
            var m = M(V("bars", 10), V("water_2l", 2), V("stove", 3));
            // bars spread so that no 3x2 or 2x3 spot is left anywhere
            foreach (var (x, y) in new[] { (1, 1), (4, 1), (7, 1), (1, 3), (4, 3), (7, 3), (2, 2), (5, 2), (8, 2) }) Assert.IsNotNull(m.PutAt("bars", x, y, false));
            Assert.IsNull(m.PutIn("water_2l"));
            Assert.AreEqual("room", m.LastFail, "it would fit if the bag were tidied");
            Assert.IsTrue(m.Tidy());
            Assert.IsNotNull(m.PutIn("water_2l"), "after 정리 the bars sit in one row and the bottle fits");
            Assert.AreEqual(9, m.BagOf("bars"), "tidying moves, never drops");

            var full = M(V("stove", 3));
            Assert.AreEqual(2, full.PutIn("stove", 3));
            Assert.AreEqual("full", full.LastFail, "a third stove does not fit even tidied");
        }

        [Test]
        public void Take_out_returns_units_home_and_the_result_counts_the_grid()
        {
            var m = M(V("bars", 1, bag: 2), V("radio", 0, bag: 1));
            Assert.AreEqual(2, m.BagOf("bars"), "what the game had in the bag starts in the grid");
            Assert.AreEqual(1, m.TakeOut("bars"));
            Assert.AreEqual(2, m.HomeOf("bars"));
            m.Empty();
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["bars"] = 0, ["radio"] = 0 }, m.Result());
            Assert.AreEqual(3, m.HomeOf("bars"));
        }

        [Test]
        public void The_layout_is_remembered_when_it_still_matches_else_tidied()
        {
            var m = M(V("radio", 1), V("bars", 2));
            m.PutAt("radio", 8, 4, false);
            m.PutAt("bars", 0, 4, false);
            var text = m.Grid.Encode();
            Assert.AreEqual("radio:8,4;bars:0,4", text);
            var again = new ManualBagModel(new[] { V("radio", 0, 1), V("bars", 1, 1) }, id => Sizes[id], 10, 5, layout: text);
            Assert.AreEqual(text, again.Grid.Encode(), "same things in the bag: the same places");
            var changed = new ManualBagModel(new[] { V("radio", 0, 1), V("bars", 0, 2) }, id => Sizes[id], 10, 5, layout: text);
            Assert.AreEqual(3, changed.Grid.Pieces.Count);
            Assert.AreEqual((0, 0), (changed.Grid.Pieces[0].X, changed.Grid.Pieces[0].Y), "a stale layout falls back to biggest-first");
            Assert.IsNull(BagGrid.Decode("radio:x,1"));
            Assert.IsNull(BagGrid.Decode("radio:1,1,q"));
            CollectionAssert.AreEqual(new[] { ("radio", 1, 2, true) }, BagGrid.Decode("radio:1,2,r"));
        }

        [Test]
        public void Tidy_is_biggest_first_like_the_original_packer()
        {
            var list = new List<BagLine> { new("bars", 3), new("water_2l", 2), new("gas_stove", 1), new("radio", 1) };
            var items = list.Select(l => V(l.Id, l.N)).ToList();
            var m = new ManualBagModel(items, Db);
            foreach (var l in list) m.PutIn(l.Id, l.N);
            Assert.IsTrue(m.Tidy());
            var packer = BagPacker.Pack(list, Db);
            CollectionAssert.AreEquivalent(packer.Placed.Select(p => (p.Id, p.X, p.Y, p.W, p.H)),
                m.Grid.Pieces.Select(p => (p.Id, p.X, p.Y, p.W, p.H)), "정리 lays the bag out as BagPacker does");
        }

        [Test]
        public void Neighbours_and_right_above_are_about_sides_not_corners()
        {
            var g = new BagGrid(10, 5);
            var radio = g.AddAt("radio", 2, 1, 0, 0, false);
            var batt = g.AddAt("b", 1, 1, 2, 0, false);
            var corner = g.AddAt("c", 1, 1, 2, 1, false);
            var below = g.AddAt("d", 1, 1, 1, 1, false);
            Assert.IsTrue(BagGrid.Touching(radio, batt));
            Assert.IsFalse(BagGrid.Touching(radio, corner), "a corner is not a side");
            Assert.IsTrue(BagGrid.RightAbove(radio, below));
            Assert.IsFalse(BagGrid.RightAbove(below, radio));
            Assert.IsFalse(BagGrid.RightAbove(radio, corner));
        }

        [Test]
        public void The_score_teaches_the_official_list_then_how_things_lie()
        {
            var m = M(V("water_2l", 1, kg: 2.05), V("bars", 1), V("meds", 1), V("radio", 1), V("batteries_aa", 1), V("flashlight", 1),
                V("tissue", 1), V("id_card", 1));
            var empty = m.Score(8);
            Assert.AreEqual(0, empty.Total);
            Assert.AreEqual(7 * BagRules.PriorityPts + BagRules.NextToPts + BagRules.DryPts + BagRules.WeightPts, empty.Max);
            Assert.IsFalse(empty.Line("weight").Ok, "an empty bag earns nothing for being light");

            m.PutAt("water_2l", 0, 0, false);
            m.PutAt("id_card", 1, 2, false); // right under the bottle
            m.PutAt("radio", 5, 0, false);
            m.PutAt("batteries_aa", 9, 4, false); // far from the radio
            foreach (var each in new[] { "bars", "meds", "flashlight", "tissue" }) m.PutIn(each);
            var s = m.Score(8);
            Assert.IsTrue(BagRules.Priorities.All(p => s.Line(p.Id).Ok), "all seven tags of the official list");
            Assert.AreEqual(1, s.Wet);
            Assert.AreEqual(-BagRules.DryPts, s.Line("dry").Points, "papers under water lose a point");
            Assert.IsFalse(s.Line("nextTo").Ok);
            Assert.IsNull(s.Line("nextTo").Fact, "a placement rule with no official source is a game hint");
            Assert.IsTrue(s.Line("nextTo").Hint);
            Assert.AreEqual("bag_list", s.Line("radio").Fact);
            Assert.AreEqual("docs_take", s.Line("id").Fact);
            Assert.AreEqual("무게 8kg 이하", s.Line("weight").Label);

            var id = m.Grid.Pieces.First(p => p.Id == "id_card");
            Assert.IsTrue(m.Move(id, 9, 0, false));
            var b = m.Grid.Pieces.First(p => p.Id == "batteries_aa");
            Assert.IsTrue(m.Move(b, 5, 1, false), "right under the radio");
            var t = m.Score(8);
            Assert.AreEqual(0, t.Wet);
            Assert.IsTrue(t.Line("dry").Ok);
            Assert.IsTrue(t.Line("nextTo").Ok);
            Assert.AreEqual(t.Max, t.Total);
        }

        [Test]
        public void Unready_gear_and_spare_batteries_alone_do_not_tick_a_tag()
        {
            var m = M(V("flashlight", 1, ready: false), V("radio", 1), V("batteries_aa", 0));
            m.PutIn("flashlight");
            m.PutIn("radio");
            var s = m.Score(8);
            Assert.IsFalse(s.Line("light").Ok, "a torch without batteries is not a light (like the goals)");
            Assert.IsFalse(s.Line("radio").Ok, "the radio needs its spare batteries too: 라디오와 건전지");
        }

        [Test]
        public void The_list_mask_is_a_gas_mask_so_ordinary_masks_tick_no_official_tag()
        {
            // 서울특별시 목록의 마스크는 '화생방 방독면 마스크': the tag that stands for the list is 화장지와 물티슈 instead
            Assert.IsNull(BagRules.PriorityOf("masks"));
            Assert.AreEqual("tissue", BagRules.PriorityOf("wipes").Id);
            Assert.AreEqual("tissue", BagRules.PriorityOf("tissue").Id);
        }

        [Test]
        public void An_unready_radio_earns_nothing_for_lying_next_to_the_batteries()
        {
            var m = M(V("radio", 1, ready: false), V("batteries_aa", 1));
            m.PutAt("radio", 0, 0, false);
            m.PutAt("batteries_aa", 2, 0, false);
            var s = m.Score(8);
            Assert.IsFalse(s.Line("radio").Ok);
            Assert.IsFalse(s.Line("nextTo").Ok, "the chip and the line agree: an unchecked radio counts for neither");
            Assert.That(s.Line("nextTo").Detail, Does.Contain("작동 확인"));

            var ready = M(V("radio", 1), V("batteries_aa", 1));
            ready.PutAt("radio", 0, 0, false);
            ready.PutAt("batteries_aa", 2, 0, false);
            Assert.IsTrue(ready.Score(8).Line("nextTo").Ok);
        }

        [Test]
        public void The_limit_reads_as_people_say_it()
        {
            Assert.AreEqual("8kg", BagRules.KgShort(8));
            Assert.AreEqual("7.5kg", BagRules.KgShort(7.5));
        }

        [Test]
        public void Packed_aids_weigh_in_the_games_bag_too_so_the_screen_and_the_story_agree()
        {
            var g = Rules.CreateGame(1);
            double before = Rules.BagLoad(g).WeightKg;
            Assert.AreEqual((0.0, 0.0), BagHand.AidLoad(g), "the original's runs have no aids");
            g.SetKnow(BagHand.AidsKey, new JArray("aid_chair_batt", "not_an_aid"));
            Assert.AreEqual(BagAids.Get("aid_chair_batt").Kg, BagHand.AidLoad(g).kg, 1e-9);
            Assert.AreEqual(before + BagAids.Get("aid_chair_batt").Kg, Rules.BagLoad(g).WeightKg, 1e-9);
        }

        [Test]
        public void The_modes_own_things_come_first_in_the_home_list()
        {
            var m = new ManualBagModel(new[] { V("bars", 1), V("radio", 1) }, id => Sizes[id], 10, 5, BagAids.For("deaf"));
            CollectionAssert.AreEqual(new[] { "aid_hearing_batt", "aid_memo", "bars", "radio" }, m.AtHome.Select(i => i.Id).ToArray());
        }

        [Test]
        public void Over_the_weight_limit_scores_nothing_for_weight_and_says_why()
        {
            var m = M(V("water_2l", 5, kg: 2.05));
            m.PutIn("water_2l", 4);
            var s = m.Score(8);
            Assert.IsTrue(s.Over);
            Assert.AreEqual(8.2, s.WeightKg, 1e-9);
            Assert.IsFalse(s.Line("weight").Ok);
            Assert.That(s.Line("weight").Detail, Does.Contain("남의 짐"));
            Assert.AreEqual((bool)s.ToJson()["over"], true);
        }

        [Test]
        public void Each_experience_mode_gets_its_own_things_and_they_stay_out_of_the_items()
        {
            Assert.AreEqual(new[] { "aid_chair_batt" }, BagAids.For("wheelchair").Select(a => a.Id).ToArray());
            CollectionAssert.AreEquivalent(new[] { "aid_hearing_batt", "aid_memo" }, BagAids.For("deaf").Select(a => a.Id));
            Assert.AreEqual(new[] { "aid_glasses" }, BagAids.For("lowvision").Select(a => a.Id).ToArray());
            Assert.IsEmpty(BagAids.For(""));
            Assert.IsTrue(BagAids.All.All(a => a.Fact != null), "every personal slot has a sourced reason");

            var m = new ManualBagModel(new[] { V("bars", 1) }, id => Sizes[id], 10, 5, BagAids.For("deaf"), new[] { "aid_memo" });
            Assert.AreEqual(1, m.BagOf("aid_memo"), "packed before: it starts in the bag");
            Assert.AreEqual(1, m.HomeOf("aid_hearing_batt"));
            Assert.IsNotNull(m.PutIn("aid_hearing_batt"));
            var s = m.Score(8);
            Assert.IsTrue(s.Line("aid_memo").Ok);
            Assert.AreEqual("aid_comm", s.Line("aid_memo").Fact);
            Assert.IsFalse(m.Result().ContainsKey("aid_memo"), "aids are not catalogue items: the game's items never see them");
            CollectionAssert.AreEquivalent(new[] { "aid_hearing_batt", "aid_memo" }, m.AidsPacked());
            Assert.AreEqual(BagRules.Papers.Contains("aid_memo"), true, "a paper notebook gets wet too");
        }

        [Test]
        public void Real_items_from_a_new_game_pack_and_score()
        {
            var g = Rules.CreateGame(1);
            var m = new ManualBagModel(BagDrill.BagItems(g, Db, _ => ""), Db);
            Assert.IsNotNull(m.PutIn("water_2l"));
            Assert.AreEqual(m.Result()["water_2l"], 1);
            Assert.IsTrue(m.Score(Db.bagLimitKg).Line("water").Ok);
            Assert.AreEqual(0, g.Items["water_2l"].Bag, "nothing is applied to the game until the screen closes");
        }

        static JObject ScreenAnswer(string layout, string[] aids) => new()
        {
            ["bag"] = new JObject { ["water_2l"] = 1 }, ["layout"] = layout, ["aids"] = new JArray(aids),
            ["score"] = new JObject { ["total"] = 5, ["max"] = 18, ["wet"] = 0, ["over"] = false, ["missed"] = new JArray() },
            ["tidied"] = 0, ["elapsed"] = 90.0, ["timedOut"] = true,
        };

        [Test]
        public void The_story_keeps_the_layout_and_the_score_only_when_the_screen_sent_them()
        {
            var g = Rules.CreateGame(1);
            int events = g.Events.Count;
            Assert.IsFalse(BagHand.Remember(g, JObject.Parse("{ bag: { water_2l: 1 } }"), "alert"), "the original's answer: nothing new");
            Assert.AreEqual(events, g.Events.Count);
            Assert.IsFalse(g.Know.ContainsKey(BagHand.LayoutKey));

            Assert.IsTrue(BagHand.Remember(g, ScreenAnswer("water_2l:0,0", new[] { "aid_memo" }), "alert"));
            Assert.AreEqual("water_2l:0,0", (string)g.Know[BagHand.LayoutKey]);
            CollectionAssert.AreEqual(new[] { "aid_memo" }, BagHand.AidsFrom(g.Know[BagHand.AidsKey]));
            var e = g.Events.Last();
            Assert.AreEqual("bag_score", e.Type);
            Assert.AreEqual(5, (int)e.Details["total"]);
            Assert.IsTrue((bool)e.Details["timedOut"]);

            var o = BagHand.AddOptions(new JObject(), g, "deaf", true);
            Assert.AreEqual("water_2l:0,0", (string)o["layout"]);
            Assert.AreEqual("deaf", (string)o["profile"]);
            Assert.IsTrue((bool)o["reduceMotion"]);
            CollectionAssert.AreEqual(new[] { "aid_memo" }, BagHand.AidsFrom(o["aids"]));
        }

        [Test]
        public void The_rush_after_the_siren_costs_what_it_took_never_more_than_the_original_four_minutes()
        {
            Assert.AreEqual(4, BagHand.RushMinutes(JObject.Parse("{ bag: {} }")), "the original screen: always 4");
            Assert.AreEqual(4, BagHand.RushMinutes(null));
            Assert.AreEqual(1, BagHand.RushMinutes(JObject.Parse("{ elapsed: 12 }")), "quick: one minute");
            Assert.AreEqual(2, BagHand.RushMinutes(JObject.Parse("{ elapsed: 40 }")));
            Assert.AreEqual(4, BagHand.RushMinutes(JObject.Parse("{ elapsed: 135 }")), "a slow screen (longer timer) still stops at 4");
            Assert.AreEqual(BagHand.AlertTimer, BagHand.TimerFor(""));
            Assert.AreEqual(BagHand.AlertTimer, BagHand.TimerFor("none"));
            Assert.Greater(BagHand.TimerFor("wheelchair"), BagHand.AlertTimer, "an experience mode gets more time, not a slower clock");
            Assert.IsTrue(BagHand.TimedOut(JObject.Parse("{ timedOut: true }")));
            Assert.IsFalse(BagHand.TimedOut(JObject.Parse("{ }")));
        }

        [Test]
        public void After_the_siren_a_tidy_is_charged_once_as_countdown_seconds_not_again_as_minutes()
        {
            Assert.AreEqual(2, BagHand.AlertTidyMinutes(JObject.Parse("{ tidied: 2 }")), "the original screen: a minute each");
            Assert.AreEqual(0, BagHand.AlertTidyMinutes(JObject.Parse("{ tidied: 2, elapsed: 50 }")), "the timed screen: already in elapsed");
            Assert.AreEqual(0, BagHand.AlertTidyMinutes(null));
            Assert.AreEqual(0, BagHand.AlertTidyMinutes(JObject.Parse("{ bag: {} }")));
        }

        [Test]
        public void Hoarding_is_more_than_eight_of_one_thing_over_the_whole_trip()
        {
            Assert.IsFalse(BagRules.Hoarding(new Dictionary<string, int> { ["bars"] = 8, ["canned"] = 8 }));
            Assert.IsTrue(BagRules.Hoarding(new Dictionary<string, int> { ["bars"] = 9 }), "two carts of 5 are still 10");
            Assert.IsFalse(BagRules.Hoarding(new Dictionary<string, int> { ["cash"] = 12 }), "cash from the ATM is not stock");
            Assert.IsFalse(BagRules.Hoarding(null));
        }
    }
}
