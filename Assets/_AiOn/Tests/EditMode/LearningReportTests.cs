using System.Linq;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class LearningReportTests
    {
        static GameState Run(params (string type, JObject details)[] events)
        {
            var g = new GameState();
            int seq = 0;
            foreach (var (type, details) in events) g.Events.Add(new GameEvent { Seq = seq++, Type = type, Details = details ?? new JObject() });
            return g;
        }

        static Facts LoadFacts()
        {
            var path = System.IO.Path.Combine(Application.dataPath, "_AiOn/Data/Source/facts.json");
            return Facts.Load(System.IO.File.ReadAllText(path));
        }

        /// <summary>facts.json + facts-ext.json, as the game's session reads them.</summary>
        static Facts LoadMergedFacts()
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "_AiOn/Data/Source");
            return Facts.Load(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "facts.json")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "facts-ext.json")));
        }

        [Test]
        public void EmptyRun_JudgesNothing()
        {
            var r = LearningReport.Build(new GameState());
            Assert.That(r.Total, Is.EqualTo(0));
            Assert.That(r.Rate, Is.EqualTo(0f));
            Assert.That(r.GoldenMinutes, Is.Null);
            Assert.That(LearningReport.Build(null).Rows, Is.Empty);
        }

        [Test]
        public void GoldenTime_IsAlertToArrival()
        {
            var r = LearningReport.Build(Run(("alert", new JObject { ["minute"] = 100 }), ("arrived", new JObject { ["minute"] = 112, ["late"] = false })));
            Assert.That(r.GoldenMinutes, Is.EqualTo(12));
            Assert.That(r.GoldenOk, Is.True);
            Assert.That(r.Rows.Single(x => x.Id == "golden").Ok, Is.True);
            Assert.That(r.Rows.Single(x => x.Id == "stairs").Ok, Is.True, "no elevator attempt recorded");

            var late = LearningReport.Build(Run(("alert", new JObject { ["minute"] = 100 }), ("arrived", new JObject { ["minute"] = 121, ["late"] = true }), ("elevator_tried", new JObject())));
            Assert.That(late.GoldenOk, Is.False);
            Assert.That(late.Rows.Single(x => x.Id == "stairs").Ok, Is.False);
            Assert.That(late.Rows.Single(x => x.Id == "stairs").FactId, Is.EqualTo("alert_stairs"));
        }

        [Test]
        public void Prep_IsJudgedOnlyOnceItWasPutToTheTest()
        {
            Assert.That(LearningReport.Build(Run(("tub_filled", null))).Rows.Any(x => x.Axis == "prep"), Is.False);
            var r = LearningReport.Build(Run(("tub_filled", null), ("blackout", null)));
            Assert.That(r.Rows.Single(x => x.Id == "tub").Ok, Is.True);
            Assert.That(r.Rows.Single(x => x.Id == "bag").Ok, Is.False);
            Assert.That(r.Axes.Single(a => a.Id == "prep").Total, Is.EqualTo(3));
            Assert.That(r.Axes.Single(a => a.Id == "prep").Ok, Is.EqualTo(1));
        }

        [Test]
        public void Smoke_ReadsTowelCrouchAndTheLift()
        {
            var r = LearningReport.Build(Run(("smoke_escape", new JObject { ["towel"] = true, ["crouch"] = false, ["exposure"] = 3.2 }), ("elevator_tried", new JObject { ["fire"] = true })));
            Assert.That(r.Rows.Single(x => x.Id == "towel").Ok, Is.True);
            Assert.That(r.Rows.Single(x => x.Id == "crouch").Ok, Is.False);
            Assert.That(r.Rows.Single(x => x.Id == "fireStairs").Ok, Is.False);
            Assert.That(r.Rate, Is.EqualTo(1f / 3f).Within(1e-5f));
            // an escape that ended without its result judges nothing it does not know
            Assert.That(LearningReport.Build(Run(("smoke_escape", new JObject()))).Rows.Select(x => x.Id), Is.EquivalentTo(new[] { "fireStairs" }));
        }

        [Test]
        public void Rumours_OnlyFalseOnesBelievedCount()
        {
            Assert.That(LearningReport.Build(Run(("rumor_believed", new JObject { ["id"] = "a", ["truth"] = true }))).Rows, Is.Empty);
            var r = LearningReport.Build(Run(("rumor_believed", new JObject { ["id"] = "a", ["truth"] = false }), ("rumor_checked", new JObject { ["id"] = "b" })));
            Assert.That(r.Ok, Is.EqualTo(1));
            Assert.That(r.Total, Is.EqualTo(2));
        }

        [Test]
        public void EveryRowPointsAtARealFact()
        {
            var facts = LoadMergedFacts(); // the 응급 처치 rows point at facts-ext.json, as the game reads it
            var all = LearningReport.Build(Run(
                ("blackout", null), ("alert", new JObject { ["minute"] = 0 }), ("arrived", new JObject { ["minute"] = 30, ["late"] = true }),
                ("radio_blocked", null), ("expiry_checked", null), ("departure", new JObject { ["bag"] = "late" }), ("candle_lit", null),
                ("smoke_escape", new JObject { ["towel"] = false, ["crouch"] = false }), ("fire_shout", null),
                ("rumor_checked", null), ("rumor_believed", new JObject { ["truth"] = false }), ("rumor_followed", null), ("rumor_spread", null),
                ("rumor_corrected", null), ("note_left", null), ("ate_spoiled", null), ("tossed_food", null), ("water_fetched", null),
                // enough own presses for the swap row (with few presses and no swap only the rhythm row speaks: CprLogic.Evaluate)
                ("cpr", new JObject { ["played"] = true, ["vague"] = 1, ["named"] = true, ["breathSec"] = 12, ["compressions"] = 40, ["firstSwapAt"] = -1, ["aedTouches"] = 1, ["resumeDelay"] = 1 })));
            Assert.That(all.Axes.Count, Is.EqualTo(LearningReport.AxisDefs.Length));
            foreach (var row in all.Rows)
            {
                Assert.That(facts.Fact(row.FactId), Is.Not.Null, row.Id + " -> " + row.FactId);
                Assert.That(row.Label, Is.Not.Null.And.Not.Empty, row.Id);
            }
            // The 15 minutes are the story's own rule; no checked source sets an arrival time, so it is never called a standard.
            Assert.That(all.Rows.Where(x => x.Axis == "care").Select(x => x.Id), Is.EqualTo(new[] { "cprPoint", "cprBreath", "cprRhythm", "cprSwap", "cprAed" }));
            Assert.That(all.Rows.Where(x => x.Axis == "care").All(x => !x.Ok), "a vague call, a slow check, no swap, a touch");
            var golden = all.Rows.Single(x => x.Id == "golden").Label;
            Assert.That(golden, Does.Contain("이야기 속 제한").And.Not.Contain("기준"));
        }

        [Test]
        public void Guide_UsesMergedFactsWordForWord_AndFillsEveryCard()
        {
            var facts = LoadMergedFacts();
            var cards = GuideLogic.Cards(facts);
            Assert.That(cards.Select(c => c.Id), Is.EqualTo(new[] { "airraid", "cpr", "outage", "shelter", "fire", "info" }));
            foreach (var c in cards)
            {
                Assert.That(c.Lines.Count, Is.EqualTo(GuideLogic.MaxLines), c.Id + ": a judge-facing card is never empty");
                foreach (var l in c.Lines)
                {
                    Assert.That(l.Text, Is.EqualTo(facts.Text(l.FactId)), l.FactId);
                    Assert.That(l.Source, Is.Not.Null.And.Not.Empty, l.FactId + " names its publisher");
                }
            }
            // The extension's lessons reach the screen: alert levels, bystander CPR, 살펴서 대피, 1338.
            var shown = cards.SelectMany(c => c.Lines).Select(l => l.FactId).ToList();
            Assert.That(shown, Does.Contain("alert_levels").And.Contain("cpr_ratio").And.Contain("fire_look").And.Contain("report_object"));
            Assert.That(cards.Single(c => c.Id == "airraid").Lines[0].FactId, Is.EqualTo("alert_levels"), "which alert has a siren comes first");
            // Without the extension the original-only cards still fill what they can and never invent lines.
            var only = GuideLogic.Cards(LoadFacts());
            Assert.That(only.Count, Is.EqualTo(6));
            Assert.That(only.Single(c => c.Id == "cpr").Lines, Is.Empty, "the CPR facts live in facts-ext.json only");
            Assert.That(GuideLogic.Cards(null).All(c => c.Lines.Count == 0), Is.True);
        }

        [Test]
        public void Terminal_MotionAndChartMath()
        {
            Assert.That(TerminalRadar.AxisDir(0, 5).y, Is.EqualTo(1f).Within(1e-5f), "the first axis points up");
            Assert.That(TerminalRadar.AxisDir(1, 4).x, Is.EqualTo(1f).Within(1e-5f), "then clockwise");
            Assert.That(UiAppear.Progress(0.05f, 0.1f, 0.2f), Is.EqualTo(0f));
            Assert.That(UiAppear.Progress(0.2f, 0.1f, 0.2f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ReportScreen.Fill(0.1f, false), Is.EqualTo(0f));
            Assert.That(ReportScreen.Fill(5f, false), Is.EqualTo(1f));
            Assert.That(ReportScreen.Fill(0f, true), Is.EqualTo(1f), "reduce-motion shows the result at once");
            Assert.That(DecisionMoment.ScaleAt(1f, 0f), Is.EqualTo(1f));
            Assert.That(DecisionMoment.ScaleAt(1f, 1f), Is.EqualTo(DecisionMoment.SlowScale).Within(1e-5f));
            Assert.That(StoryOverlay.IsJudgement("판단"), Is.True);
            Assert.That(StoryOverlay.IsJudgement("오서연"), Is.False);
        }
    }
}
