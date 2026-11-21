// The port's two endings (competition upgrade): 12 「짧고 정확한 한 줄」 (report: accurate reports and checking first)
// and 13 「돌봄 순번표」 (care). They read the port's axes (Deeds.PortAxisNames, AllAxes) and open only for a run that
// played their own moments (EndingDef.Gate), so every original golden run reaches and misses what the original did
// (EndingsTests checks that part unchanged).
using System.Linq;
using AiOn.Game;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PortEndingsTests
    {
        static Fate Pick(GameState g) => Endings.PickEnding(g, GoldenRuns.FactSource);

        [Test]
        public void PortAxes_AreKeptApartFromTheOriginalAxes()
        {
            var g = GoldenRuns.Build("care");
            var a = Deeds.Axes(g, true);
            var all = Deeds.AllAxes(g, true);
            CollectionAssert.AreEqual(Deeds.AxisNames, a.Keys, "Axes() is the original's");
            CollectionAssert.AreEqual(Deeds.AxisNames.Concat(Deeds.PortAxisNames), all.Keys);
            foreach (var k in Deeds.AxisNames) Assert.AreEqual(a[k], all[k], k);
            Assert.That(all["care"], Is.GreaterThanOrEqualTo(10));
            foreach (var d in Deeds.Table)
            {
                foreach (var k in Deeds.AxisNames) Assert.AreEqual(d.W.TryGetValue(k, out var v) ? v : 0, Deeds.Weight(d.Id, k), d.Id);
                foreach (var (axis, w) in Deeds.PortWeights(d.Id))
                {
                    Assert.That(Deeds.PortAxisNames, Does.Contain(axis), d.Id);
                    Assert.That(w, Is.GreaterThan(0), d.Id);
                }
            }
            Assert.AreEqual(0, Deeds.Weight("nope", "care"));
            Assert.That(Deeds.PortWeights("hoard"), Is.Empty);
            // every gate deed exists and weighs on its ending's axis
            foreach (var id in Endings.PortAdded)
            {
                var e = Endings.ById(id);
                Assert.That(e.Kind, Is.EqualTo("world"));
                Assert.That(e.Gate, Is.Not.Empty, id);
                foreach (var gate in e.Gate) Assert.That(Deeds.Weight(gate, e.Need[0].Axis), Is.GreaterThan(0), $"{id}: {gate}");
            }
        }

        [Test]
        public void PortEndings_NeedTheirOwnMoments_AnOriginalOnlyRunNeverReachesOrMissesThem()
        {
            // plenty of care, all of it the original's deeds: the original's endings only
            var ids = new[] { "elder_alert_knock", "queue_carry_elder", "shelter_water_given", "register_helped", "stairs_asked", "water_for_seoyeon", "knock_helped", "mart_share", "fire_warned", "elder_visit", "bus_helped" };
            var g = GoldenRuns.Run("stay", ids);
            Assert.That(Deeds.AllAxes(g, true)["care"], Is.GreaterThanOrEqualTo(12), "closer to 돌봄 순번표 than to any original world ending");
            var f = Pick(g);
            Assert.AreNotEqual("care", f.Id);
            Assert.That(f.Missed.Select(m => m.Id), Has.None.EqualTo("care").And.None.EqualTo("report"), "an original-only run misses what the original's did");
            // the same run, having stayed beside the grandfather in the shelter: the gate is open
            var g2 = GoldenRuns.Run("stay", ids, new[] { ("cpr", "{played:true,swaps:1}"), ("elder_pov", "{played:true}") });
            Assert.That(Deeds.AllAxes(g2, true)["care"], Is.GreaterThanOrEqualTo(Endings.ById("care").Need[0].Threshold));
            Assert.AreEqual("care", Pick(g2).Id);
            Assert.AreEqual("돌봄 순번표", Pick(g2).Title);
            // the gate open, the threshold not met yet: a near miss
            Assert.That(Pick(GoldenRuns.Run("stay", ids, new[] { ("elder_pov", "{played:true}") })).Missed.Select(m => m.Id), Has.Member("care"));
        }

        [Test]
        public void PortEndings_LeavingPathsNeverOpenThem_ButShowThemAsNearMisses()
        {
            var fix = GoldenRuns.Fix.First(x => x.id == "care");
            var g = GoldenRuns.Run("bus", fix.ids, fix.extra);
            var f = Pick(g);
            Assert.AreEqual("bus", f.Id);
            var care = f.Missed.FirstOrDefault(x => x.Id == "care");
            Assert.That(care, Is.Not.Null, "the closest world ending");
            Assert.That(care.Hint, Does.StartWith("연무동에 남았다면 — "));
            Assert.That(care.Title, Is.EqualTo("돌봄 순번표"));
        }

        [Test]
        public void PortEnding_Lines_FollowTheRun()
        {
            var report = Pick(GoldenRuns.Build("report"));
            Assert.AreEqual(("report", 12, "world"), (report.Id, report.No, report.Kind));
            Assert.That(report.Lines, Has.Some.Contains("1338"));
            Assert.That(report.Lines, Has.Some.Contains("동·호수"));
            Assert.That(report.Lines, Has.Some.Contains("방송부터"));
            Assert.That(report.Lines, Has.Some.Contains("'모름'"), "an honest card with gaps, no guess");
            // the ending's facts are sourced, and the 1338 it prints is backed by the verified line (facts-ext report_object)
            var facts = AiOn.Story.Testing.FakeStoryHost.LoadFacts();
            var cited = Endings.PickEnding(GoldenRuns.Build("report"), facts.SourceId);
            Assert.That(cited.SourceIds, Does.Contain(facts.SourceId("report_object")));
            Assert.That(cited.SourceIds, Does.Contain(facts.SourceId("report_112_text")));
            Assert.That(facts.Text("report_object"), Does.Contain("1338"));
            Assert.That(report.Lines, Has.Some.Contains("군부대(1338)"), "the child's line follows the verified wording");
            var care = Pick(GoldenRuns.Build("care"));
            Assert.AreEqual(("care", 13), (care.Id, care.No));
            Assert.That(care.Lines, Has.Some.Contains("304호 칸"));
            Assert.That(care.Lines, Has.Some.Contains("교대로"));
            Assert.That(care.Lines, Has.Some.Contains("지우"));
            Assert.That(care.People.Select(p => p.Line), Has.Some.Contains("지우가 엄마 손을 잡고"));
            foreach (var f in new[] { report, care })
                foreach (var l in f.Lines.Concat(f.People.Select(p => p.Line)).Concat(f.Town))
                    Assert.That(!l.Contains("탓") && !l.Contains("잘못") && !l.Contains("실수"), l);
        }

        [Test]
        public void TheReportEnding_IsNotTheBoardEnding_AgainInOtherWords()
        {
            var board = Endings.ById("board");
            var report = Endings.ById("report");
            Assert.That(report.Facts.Intersect(board.Facts), Is.Empty, "the two cards cite different facts");
            Assert.That(report.Facts, Does.Not.Contain("info_suspect"), "no explosive-object warning on every report ending");
            foreach (var n in new[] { "112", "1338", "119" }) Assert.That(report.Hint, Does.Contain(n), "the hint names the act of reporting");
            Assert.That(report.Hint, Does.Not.Contain("들은 말"), "checking what was heard is the board ending's hint");
        }

        [Test]
        public void ShelterChildCare_WatchingAndFindingTheChildcare_WeighTheSame()
        {
            Assert.AreEqual(Deeds.Weight("shelter_child_care", "care"), Deeds.Weight("shelter_child_referred", "care"));
            Assert.AreEqual(Deeds.Weight("shelter_child_care", "town"), Deeds.Weight("shelter_child_referred", "town"));
            var g = GoldenRuns.Build("care");
            var ev = g.Events.Last(e => e.Type == ShelterLifeLogic.Event);
            ev.Details["child"] = "staff";
            var care = Pick(g);
            Assert.That(care.Id, Is.EqualTo("care"));
            Assert.That(care.Lines, Has.Some.Contains("아이 돌봄 자원봉사 자리를 본떠"));
            Assert.That(care.People.Select(p => p.Line), Has.Some.Contains("아이 돌봄 자리의 자원봉사자"));
        }

        [Test]
        public void PortEnding_Traces_HaveThreeToFiveItems_FromTheDeedsOnItsAxis()
        {
            foreach (var id in Endings.PortAdded)
            {
                var g = GoldenRuns.Build(id);
                var axis = Endings.ById(id).Need[0].Axis;
                var t = Endings.Trace(g, id, GoldenRuns.FactSource);
                Assert.That(t.Count, Is.InRange(3, 5), id);
                Assert.That(t.Any(x => x.Id == "path"), id);
                var held = Deeds.Of(g).Select(d => d.Id).ToList();
                foreach (var x in t.Where(x => x.Id != "path"))
                {
                    Assert.That(held, Does.Contain(x.Id), id);
                    Assert.That(Deeds.Weight(x.Id, axis), Is.GreaterThan(0), $"{id}: {x.Id}");
                }
                Assert.That(t.SelectMany(x => x.SourceIds).All(GoldenRuns.IsSource), id);
            }
        }

        [Test]
        public void Gallery_ShowsThirteenCards_TheNewOnesHiddenUntilSeen()
        {
            var v = Endings.GalleryView(Endings.AddToGallery((string)null, "care", new[] { "cpr_helped", "shelter_child_care" }, "2026-11-21"));
            Assert.AreEqual(13, v.Count);
            var care = v.Last();
            Assert.That(care.Seen, Is.True);
            Assert.AreEqual("돌봄 순번표", care.Title);
            Assert.That(care.Shorts, Has.Count.EqualTo(2));
            Assert.That(v[11].Id, Is.EqualTo("report"));
            Assert.That(v[11].Seen, Is.False);
            Assert.That(v[11].Title, Is.Null);
            Assert.That(v[11].Hint, Is.Not.Empty);
            Assert.AreEqual("열세", Endings.CountWord(13));
            Assert.AreEqual("열한", Endings.CountWord(11));
        }
    }
}
