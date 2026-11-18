using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.UI;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>The pure logic behind the menu / full-screen polish: stat words, time-skip timing, map label layout,
    /// payoff grouping, the generated texture ramps and the bag's soft confirm.</summary>
    public class MenuPolishTests
    {
        // ---------- one stat word for the HUD, the cards and the debrief ----------

        [TestCase("health", 100, "좋음")]
        [TestCase("health", 67, "좋음")]
        [TestCase("health", 66, "보통")]
        [TestCase("health", 34, "보통")]
        [TestCase("health", 33, "나쁨")]
        [TestCase("tension", 0, "좋음")]
        [TestCase("tension", 27, "좋음")]
        [TestCase("tension", 33, "좋음")]
        [TestCase("tension", 34, "보통")]
        [TestCase("tension", 66, "보통")]
        [TestCase("tension", 67, "나쁨")]
        [TestCase("fatigue", 6, "좋음")]
        [TestCase("thirst", 90, "나쁨")]
        public void Stat_word_bands(string kind, double value, string word) => Assert.AreEqual(word, GameRules.StatWord(kind, value));

        // ---------- time skip ----------

        [Test]
        public void Time_skip_waits_for_reading_time_between_2_5_and_7_5_s()
        {
            Assert.AreEqual(2500, StoryScreens.TimeSkipMs(null));
            Assert.AreEqual(2500, StoryScreens.TimeSkipMs("짧다"));
            Assert.AreEqual(1400 + 30 * 55, StoryScreens.TimeSkipMs(new string('가', 30)));
            Assert.AreEqual(7500, StoryScreens.TimeSkipMs(new string('가', 400)));
        }

        // ---------- map labels ----------

        static bool Overlap((float x, float y, float w, float h) a, (float x, float y, float w, float h) b) =>
            System.Math.Abs(a.x - b.x) * 2 < a.w + b.w && System.Math.Abs(a.y - b.y) * 2 < a.h + b.h;

        [Test]
        public void Nudge_pushes_overlapping_boxes_apart_along_y_only()
        {
            var boxes = new List<(float, float, float, float)> { (100, 100, 60, 16), (110, 104, 60, 16), (400, 100, 60, 16) };
            var y = RoutemapLogic.NudgeApart(boxes, 2);
            Assert.Less(y[0], 100); Assert.Greater(y[1], 104);
            Assert.GreaterOrEqual(y[1] - y[0], 16 + 2 - 1e-3);
            Assert.AreEqual(100, y[2]); // nothing near it
        }

        [Test]
        public void Nudge_leaves_pinned_boxes_and_settles_chains()
        {
            var boxes = new List<(float, float, float, float)> { (0, 0, 50, 10), (0, 2, 50, 10), (0, 4, 50, 10) };
            var y = RoutemapLogic.NudgeApart(boxes, 0, new[] { true, false, false });
            Assert.AreEqual(0, y[0]);
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    Assert.GreaterOrEqual(System.Math.Abs(y[i] - y[j]), 10 - 1e-3, $"{i} {j}");
        }

        [Test]
        public void Map_labels_do_not_overlap_and_entrances_sit_outside_the_centre()
        {
            var p = MapProjector.Fit(874, 740);
            var labels = RoutemapLogic.Labels(p, 1.3f);
            var texts = labels.Where(s => s.Kind == MapShapeKind.Text).ToList();
            var boxes = texts.Select(RoutemapLogic.LabelBox).ToList();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    Assert.IsFalse(Overlap(boxes[i], boxes[j]), $"'{texts[i].Text}' overlaps '{texts[j].Text}'");
            // road names carry a pill and sit off the centre line
            var road = texts.First(t => t.Text == "연무로");
            Assert.Greater(road.Bg.A, 0.8f);
            // the centre's name above the diamond (the shelter node), '대피소(지하)' under it, neither covering the diamond
            var (sx, sy) = p.P(-90, 31);
            var name = texts.First(t => t.Text == "연무 생활문화센터");
            var shelter = texts.First(t => t.Text == "대피소(지하)");
            Assert.Less(name.Y + RoutemapLogic.LabelBox(name).h / 2, sy - 9 * 1.3f + 4);
            Assert.Greater(shelter.Y - RoutemapLogic.LabelBox(shelter).h / 2, sy + 9 * 1.3f - 4);
            // each entrance label has a leader line
            Assert.GreaterOrEqual(labels.Count(s => s.Kind == MapShapeKind.Line && s.Width == 1), 4);
        }

        // ---------- cards ----------

        [Test]
        public void Payoffs_group_under_one_heading_per_preparation()
        {
            var v = new ChapterCardView();
            v.Payoffs.Add(new DebriefLine("비상가방", "a"));
            v.Payoffs.Add(new DebriefLine("라디오", "b"));
            v.Payoffs.Add(new DebriefLine("비상가방", "c"));
            var g = v.PayoffGroups();
            CollectionAssert.AreEqual(new[] { "비상가방", "라디오" }, g.Select(x => x.head).ToArray());
            CollectionAssert.AreEqual(new[] { "a", "c" }, g[0].items.Select(x => x.Text).ToArray());
        }

        [Test]
        public void Experience_chips_reuse_the_settings_subtitles()
        {
            Assert.IsNull(SettingsForm.ProfileSub("none"));
            Assert.AreEqual("앉은 눈높이 · 계단 불가", SettingsForm.ProfileSub("wheelchair"));
            var form = new SettingsForm(null);
            Assert.AreSame(SettingsForm.ProfileOptions, form.LeftRadios.First(r => r.Name == "profile").Options);
        }

        // ---------- bag: not-ready note and the soft confirm ----------

        static List<BagGoal> BagGoals() => new()
        {
            new BagGoal { Id = "water", Label = "마실 물 2L 이상", Items = new[] { "water_2l" }, Min = 2, Per = new Dictionary<string, double> { ["water_2l"] = 2 }, Facts = new string[0] },
            new BagGoal { Id = "light", Label = "켜지는 손전등", Items = new[] { "flashlight" }, Facts = new string[0] },
            new BagGoal { Id = "docs", Label = "신분증·중요 서류 사본", Items = new[] { "id_card", "doc_copies" }, Facts = new string[0] },
            new BagGoal { Id = "cash", Label = "현금", Items = new[] { "cash" }, Facts = new string[0] },
        };

        [Test]
        public void Packed_but_unready_item_explains_the_open_goal()
        {
            var items = new List<BagItemView> { new() { Id = "flashlight", Ready = false }, new() { Id = "water_2l", Ready = true } };
            var bag = new Dictionary<string, int> { ["flashlight"] = 1 };
            int BagOf(string id) => bag.TryGetValue(id, out var n) ? n : 0;
            var light = BagGoals()[1];
            Assert.AreEqual(BagCheck.NotReady, BagCheck.GoalNote(light, BagOf, items));
            items[0].Ready = true;
            Assert.IsNull(BagCheck.GoalNote(light, BagOf, items));       // met now
            Assert.IsNull(BagCheck.GoalNote(BagGoals()[3], BagOf, items)); // open, but nothing packed: no excuse to give
        }

        [Test]
        public void Soft_confirm_asks_about_id_first_then_water_and_not_in_free_mode()
        {
            var items = new List<BagItemView> { new() { Id = "water_2l", Ready = true }, new() { Id = "id_card", Ready = true } };
            var bag = new Dictionary<string, int>();
            int BagOf(string id) => bag.TryGetValue(id, out var n) ? n : 0;
            var goals = BagGoals();
            var g = BagCheck.CriticalOpen(goals, "guided", BagOf, items);
            Assert.AreEqual("docs", g.Id);
            Assert.AreEqual("신분증 없이 나갈까요?", BagCheck.Question(g));
            Assert.IsNull(BagCheck.CriticalOpen(goals, "free", BagOf, items));
            bag["id_card"] = 1;
            Assert.AreEqual("water", BagCheck.CriticalOpen(goals, "hint", BagOf, items).Id);
            bag["water_2l"] = 1;
            Assert.IsNull(BagCheck.CriticalOpen(goals, "guided", BagOf, items));
            CollectionAssert.AreEqual(new[] { "켜지는 손전등", "현금" }, BagCheck.OpenLabels(goals, BagOf, items));
            Assert.AreEqual("아직 ○ : a, b 외 3개", BagCheck.OpenSummary(new[] { "a", "b", "c", "d", "e" }));
            Assert.AreEqual("아직 ○ : a", BagCheck.OpenSummary(new[] { "a" }));
        }

        // ---------- generated textures ----------

        [Test]
        public void Landing_shade_ramp_and_soft_disc()
        {
            var ts = new[] { 0f, 0.46f, 1f }; var a = new[] { 0.92f, 0.62f, 0.12f };
            Assert.AreEqual(0.92f, DecorMath.Ramp(0, ts, a), 1e-5);
            Assert.AreEqual(0.62f, DecorMath.Ramp(0.46f, ts, a), 1e-5);
            Assert.AreEqual(0.12f, DecorMath.Ramp(1, ts, a), 1e-5);
            Assert.AreEqual(0.37f, DecorMath.Ramp(0.73f, ts, a), 1e-3);
            Assert.AreEqual(1, DecorMath.Radial(0, 0.5f));
            Assert.AreEqual(0, DecorMath.Radial(1, 0.5f));
            Assert.AreEqual(0.5f, DecorMath.Radial(0.75f, 0.5f), 1e-5);
        }

        [Test]
        public void Light_fills_premix_opaque_like_css()
        {
            var c = DecorMath.Over(new UnityEngine.Color(0.1f, 0.1f, 0.1f, 0.97f), UnityEngine.Color.white, 0.1f);
            Assert.AreEqual(0.19f, c.r, 1e-4);
            Assert.AreEqual(1f, c.a);
        }

        // ---------- map labels against the routes and roads ----------

        [Test]
        public void Shelter_and_entrance_names_draw_over_the_routes_road_names_under()
        {
            var p = MapProjector.Fit(874, 740);
            var (under, over) = RoutemapLogic.LabelLayers(p, 1.3f);
            var overTexts = over.Where(s => s.Kind == MapShapeKind.Text).Select(s => s.Text).ToList();
            CollectionAssert.Contains(overTexts, "동쪽 출입구");
            CollectionAssert.Contains(overTexts, "대피소(지하)");
            CollectionAssert.Contains(overTexts, "연무 생활문화센터");
            Assert.IsTrue(under.Any(s => s.Text == "연무로"));
            Assert.IsFalse(under.Any(s => s.Text == "동쪽 출입구"));
            Assert.IsTrue(over.Any(s => s.Kind == MapShapeKind.Line)); // the leaders go with their names
        }

        [Test]
        public void North_south_road_names_clear_the_centre_line_and_entrances_stay_off_roads()
        {
            var p = MapProjector.Fit(874, 740);
            var labels = RoutemapLogic.Labels(p, 1.3f).Where(s => s.Kind == MapShapeKind.Text).ToList();
            foreach (var r in TownMap.Roads.Where(r => !r.EastWest))
            {
                var t = labels.First(l => l.Text == r.Name);
                var (cx, _) = p.P((r.Rect.X0 + r.Rect.X1) / 2, 0);
                var b = RoutemapLogic.LabelBox(t);
                Assert.Greater(b.x - b.w / 2, cx + 10, r.Name); // the pill's near edge clears the dashed line
            }
            var east = labels.First(l => l.Text == "동쪽 출입구");
            var eb = RoutemapLogic.LabelBox(east);
            foreach (var r in TownMap.Roads.Where(r => !r.EastWest))
            {
                var (ax, ay) = p.P(r.Rect.X0 - TownMap.Sidewalk, r.Rect.Z0); var (bx, by) = p.P(r.Rect.X1 + TownMap.Sidewalk, r.Rect.Z1);
                float x0 = System.Math.Min(ax, bx), x1 = System.Math.Max(ax, bx), y0 = System.Math.Min(ay, by), y1 = System.Math.Max(ay, by);
                bool hit = eb.x + eb.w / 2 > x0 && eb.x - eb.w / 2 < x1 && eb.y + eb.h / 2 > y0 && eb.y - eb.h / 2 < y1;
                Assert.IsFalse(hit, "동쪽 출입구 on " + r.Name);
            }
        }

        // ---------- cards ----------

        [Test]
        public void Decision_focus_starts_empty_and_lands_by_side()
        {
            Assert.AreEqual(0, CardFocus.Step(-1, 2, -1, false));  // left arrow: the left button
            Assert.AreEqual(1, CardFocus.Step(-1, 2, 1, false));   // right arrow: the right button
            Assert.AreEqual(0, CardFocus.Step(-1, 2, 1, true));    // Tab: the first
            Assert.AreEqual(1, CardFocus.Step(-1, 2, -1, true));   // Shift+Tab: the last
            Assert.AreEqual(1, CardFocus.Step(1, 2, 1, false));    // arrows stop at the ends
            Assert.AreEqual(0, CardFocus.Step(1, 2, 1, true));     // Tab wraps
            Assert.AreEqual(-1, CardFocus.Step(-1, 0, 1, true));
            Assert.Greater(CardFocus.ArmSeconds, 0.2f);
        }

        [Test]
        public void Time_skip_group_is_centred_with_the_bar_under_the_text()
        {
            var y = StoryScreens.SkipLayout(48, 40, 3, 42);
            float total = 48 + 28 + 40 + 36 + 3 + 22 + 42;
            Assert.AreEqual(total / 2, y[0], 1e-4);
            Assert.AreEqual(y[0] - 48 - 28, y[1], 1e-4);
            Assert.AreEqual(y[1] - 40 - 36, y[2], 1e-4);        // the track 36 px under the text
            Assert.AreEqual(-total / 2, y[3] - 42, 1e-4);       // the group ends as far below the centre as it starts above
        }

        // ---------- bag confirm ----------

        [Test]
        public void Soft_confirm_names_both_gaps_and_keeps_the_summary_short()
        {
            var goals = BagGoals();
            var docs = goals.First(g => g.Id == "docs"); var water = goals.First(g => g.Id == "water");
            Assert.AreEqual("신분증과 마실 물 없이 나갈까요?", BagCheck.Question(new List<BagGoal> { docs, water }));
            Assert.AreEqual("마실 물 없이 나갈까요?", BagCheck.Question(new List<BagGoal> { water }));
            var items = new List<BagItemView> { new() { Id = "water_2l", Ready = true }, new() { Id = "id_card", Ready = true } };
            int None(string id) => 0;
            CollectionAssert.AreEqual(new[] { "docs", "water" }, BagCheck.CriticalOpenAll(goals, "guided", None, items).Select(g => g.Id));
            Assert.IsEmpty(BagCheck.CriticalOpenAll(goals, "free", None, items));
            // long names: fewer named, the rest counted, so '외 N개' never wraps onto its own line
            Assert.AreEqual("아직 ○ : 켜지는 손전등 외 2개", BagCheck.OpenSummary(new[] { "켜지는 손전등", "휴대용 라디오와 건전지", "현금" }));
        }
    }
}
