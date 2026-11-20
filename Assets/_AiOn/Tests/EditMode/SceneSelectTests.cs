// The judge entry (competition upgrade): 장면 바로가기 and 교사용 30분 모드 (Story/SceneSelect.cs, StoryDirector
// PlayPrepared / StartTeacher). The prepared states in Resources/Demo come from the default playthrough
// (StoryPlaythroughTests.DefaultRun); DemoSaves_Write regenerates them when the story changes (run it explicitly).
// Checked here: every ready moment ships a state that loads at its checkpoint; each one plays its chapter on
// FakeStoryHost without a fail card; the player's main save, the host's mirror, the gallery and the records stay as
// they were; the teacher route plays its four moments with a sourced lesson card between them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class SceneSelectTests
    {
        static string DemoDir => Path.Combine(UnityEngine.Application.dataPath, "_AiOn", "Resources", SceneSelect.ResourceFolder);
        static List<string> ChapterIds => DirectorParts.Default().Chapters.Select(c => c.Id).ToList();
        static string ReadDemo(string id) { var p = Path.Combine(DemoDir, id + ".json"); return File.Exists(p) ? File.ReadAllText(p) : null; }
        const string MainSave = "{\"index\":3,\"G\":null,\"marker\":\"the player's own save\"}";

        // ---------------------------------------------------------------- the prepared states

        /// <summary>The default playthrough, taking a prepared state at each ready moment's checkpoint (and inside the shelter).</summary>
        static Dictionary<string, string> Capture()
        {
            var r = StoryPlaythroughTests.DefaultRun("demo-capture");
            var caught = new Dictionary<string, string>();
            r.Host.OnCheckpoint += key =>
            {
                foreach (var m in SceneSelect.Moments)
                    if (m.Ready && m.Key == key && !caught.ContainsKey(m.Id)) caught[m.Id] = r.Store.Get(StoryDirector.SaveKey);
            };
            // subscribed before any roam: this runs before the story's own handler of the first press in the shelter (reception)
            r.Host.Interact += point =>
            {
                var g = r.D.G;
                if (caught.ContainsKey("shelter") || g == null || g.Chapter != "dday" || r.Host.LevelId != "shelter" || !r.D.Ctx.Roaming || r.D.Pov != "jeongmin") return;
                // inside the shelter, just arrived: a copy of the game with the shelter as its checkpoint, and the world as it is
                var copy = SaveCodec.Load(SaveCodec.Save(g), TestSupport.Rules.Db);
                WalkedClock(copy);
                SaveCodec.MakeCheckpoint(copy, DDay.ShelterResume);
                caught["shelter"] = new JObject { ["index"] = r.D.Index, ["G"] = JObject.Parse(SaveCodec.Save(copy)), ["snap"] = r.D.Snapshot() }.ToString(Formatting.None);
            };
            // the corridor fire's checkpoint is also where 살펴서 대피 starts: the same state under its own resume key
            r.Host.OnCheckpoint += key =>
            {
                var g = r.D.G;
                if (key != "fire" || caught.ContainsKey("look") || g == null) return;
                var copy = SaveCodec.Load(SaveCodec.Save(g), TestSupport.Rules.Db);
                SaveCodec.MakeCheckpoint(copy, Endure.LookResume);
                caught["look"] = new JObject { ["index"] = r.D.Index, ["G"] = JObject.Parse(SaveCodec.Save(copy)), ["snap"] = r.D.Snapshot() }.ToString(Formatting.None);
                // and the 3D extinguisher's start (in the corridor, no look step), with the gauge read and reported in 1장
                var ext = SaveCodec.Load(SaveCodec.Save(g), TestSupport.Rules.Db);
                ext.SetKnow(ExtinguisherCheck.Know, ExtinguisherCheck.Reported);
                SaveCodec.MakeCheckpoint(ext, Endure.ExtResume);
                caught["extinguisher"] = new JObject { ["index"] = r.D.Index, ["G"] = JObject.Parse(SaveCodec.Save(ext)), ["snap"] = r.D.Snapshot() }.ToString(Formatting.None);
            };
            // seated in the shelter, the moment the grandfather collapses (the CPR screen opens): the cpr moment's state
            var policy = r.Host.OpenPolicy;
            r.Host.OpenPolicy = (screen, opts) =>
            {
                var g = r.D.G;
                if (screen == CprScene.Screen && !caught.ContainsKey("cpr") && g != null && g.Chapter == "dday")
                {
                    var copy = SaveCodec.Load(SaveCodec.Save(g), TestSupport.Rules.Db);
                    SettledClock(copy);
                    SaveCodec.MakeCheckpoint(copy, DDay.CprResume);
                    caught["cpr"] = new JObject { ["index"] = r.D.Index, ["G"] = JObject.Parse(SaveCodec.Save(copy)), ["snap"] = r.D.Snapshot() }.ToString(Formatting.None);
                }
                return policy(screen, opts);
            };
            var result = r.Play();
            Assert.That(r.Fails, Is.Empty);
            Assert.That(result, Is.EqualTo("restart"));
            return caught;
        }

        /// <summary>
        /// The walk (WalkedClock), and what happened inside after arriving (the sign-in, the seat) moved along with the
        /// arrival, plus a few minutes to sit down: the clock when the grandfather collapses.
        /// </summary>
        static void SettledClock(GameState g)
        {
            int before = g.Minute;
            var later = new HashSet<GameEvent>(g.Events.Where(e => e.Minute > DDay.Alert)); // inside, after the fake clock moved
            WalkedClock(g);
            int shift = g.Minute - DDay.Alert;
            foreach (var e in later)
            {
                e.Minute += shift;
                if (e.Details?["minute"] != null) e.Details["minute"] = e.Minute;
            }
            g.Minute = Math.Max(g.Minute, before + shift) + 3;
        }

        /// <summary>
        /// The scripted run walks to the shelter in no time (the fake host's clock does not run during a press), so the
        /// shelter state would read 20:05, the minute of the alert. Give the walk a plausible 11 minutes: out of the door
        /// at +3, the manager's window at +5, the strike at +7, at the gate and inside at +11 (well inside the story's 15).
        /// </summary>
        static void WalkedClock(GameState g)
        {
            var at = new Dictionary<string, int> { ["departure"] = 3, ["deed"] = 5, ["help"] = 5, ["strike"] = 7, ["gate_observed"] = 11, ["route_attempt"] = 11, ["arrived"] = 11, ["payoff"] = 11 };
            bool after = false; int last = 0;
            foreach (var e in g.Events)
            {
                if (e.Type == "alert") { after = true; continue; }
                if (!after || e.Minute != DDay.Alert) continue;
                if (e.Type == "payoff" && last == 0) continue; // the bag payoff as the alert sounded stays at 20:05
                last = Math.Max(last, at.TryGetValue(e.Type, out var m) ? m : last);
                e.Minute = DDay.Alert + last;
                if (e.Details?["minute"] != null) e.Details["minute"] = e.Minute;
            }
            g.Minute = DDay.Alert + Math.Max(last, 11);
        }

        [Test, Explicit("Writes Assets/_AiOn/Resources/Demo/*.json from the default playthrough")]
        public void DemoSaves_Write()
        {
            var caught = Capture();
            Directory.CreateDirectory(DemoDir);
            foreach (var m in SceneSelect.Moments.Where(x => x.Ready))
            {
                Assert.That(caught.ContainsKey(m.Id), $"no state taken for {m.Id}");
                File.WriteAllText(Path.Combine(DemoDir, m.Id + ".json"), caught[m.Id]);
            }
        }

        [Test]
        public void DemoSaves_TheDefaultRunYieldsAStateForEveryReadyMoment()
        {
            var caught = Capture();
            foreach (var m in SceneSelect.Moments.Where(x => x.Ready))
            {
                Assert.That(caught.ContainsKey(m.Id), m.Id);
                var p = SceneSelect.Prepare(m, caught[m.Id], ChapterIds, TestSupport.Rules.Db);
                Assert.That(p.Key, Is.EqualTo(m.Key), m.Id);
            }
        }

        [Test]
        public void DemoSaves_ShippedForEveryReadyMoment_LoadAtTheirCheckpoint()
        {
            var ids = ChapterIds;
            foreach (var m in SceneSelect.Moments)
            {
                var json = ReadDemo(m.Id);
                if (!m.Ready) { Assert.That(m.Key, Is.Null, $"{m.Id}: a slot has no state yet"); continue; }
                Assert.That(json, Is.Not.Null.And.Not.Empty, $"Resources/Demo/{m.Id}.json");
                var p = SceneSelect.Prepare(m, json, ids, TestSupport.Rules.Db);
                Assert.That(ids[p.Index], Is.EqualTo(m.Chapter), m.Id);
                Assert.That(p.Resume, Is.EqualTo(m.Key == m.Chapter ? null : m.Key), m.Id);
                Assert.That(StoryDirector.ReadSnap(p.Saved["snap"])["levelStates"], Is.Not.Null, m.Id);
                var g = SaveCodec.Load(p.Saved["G"].ToString(Formatting.None), TestSupport.Rules.Db);
                Assert.That(g.Checkpoint.Key, Is.EqualTo(m.Key), m.Id);
            }
        }

        [Test]
        public void Prepare_RejectsAMissingOrMismatchedState()
        {
            var ids = ChapterIds;
            var fire = SceneSelect.Get("fire");
            Assert.Throws<InvalidOperationException>(() => SceneSelect.Prepare(fire, null, ids, TestSupport.Rules.Db));
            Assert.Throws<InvalidOperationException>(() => SceneSelect.Prepare(fire, "{\"index\":3}", ids, TestSupport.Rules.Db));
            var alert = ReadDemo("alert");
            Assert.Throws<InvalidOperationException>(() => SceneSelect.Prepare(fire, alert, ids, TestSupport.Rules.Db), "the alert state is not the fire's");
            var moved = JObject.Parse(alert); moved["index"] = 4;
            Assert.Throws<InvalidOperationException>(() => SceneSelect.Prepare(SceneSelect.Get("alert"), moved.ToString(), ids, TestSupport.Rules.Db), "wrong chapter");
        }

        // ---------------------------------------------------------------- the list

        [Test]
        public void Moments_AndTheTeacherRoute_AreConsistent()
        {
            var ids = ChapterIds;
            Assert.That(SceneSelect.Moments.Select(m => m.Id), Is.Unique);
            Assert.That(SceneSelect.Moments.Count(m => m.Ready), Is.GreaterThanOrEqualTo(5));
            Assert.That(SceneSelect.Moments.Where(m => !m.Ready).Select(m => m.Id), Is.Empty, "every slot is filled (look: 살펴서 대피, 11-21)");
            foreach (var m in SceneSelect.Moments)
            {
                Assert.That(ids, Does.Contain(m.Chapter), m.Id);
                Assert.That(GoldenRuns.HasHangul(m.Title) && GoldenRuns.HasHangul(m.Line), m.Id);
                if (m.Ready) Assert.That(m.Key, Is.Not.Null, m.Id);
            }
            var facts = FakeStoryHost.LoadFacts();
            Assert.That(SceneSelect.TeacherRoute.Select(s => s.Moment), Is.EqualTo(new[] { "alert", "fire", "decision", "evac" }));
            foreach (var s in SceneSelect.TeacherRoute)
            {
                Assert.That(SceneSelect.Get(s.Moment)?.Ready, Is.True, s.Moment);
                Assert.That(facts.Text(s.Fact), Is.Not.Null.And.Not.Empty, $"{s.Fact}: a lesson is a sourced fact");
                Assert.That(facts.Source(facts.SourceId(s.Fact)), Is.Not.Null, s.Fact);
            }
            Assert.That(SceneSelect.LessonMeta(0), Is.EqualTo("교사용 30분 모드 · 1/4"));
            var opts = SceneSelect.ScreenOptions(id => id != "evac");
            Assert.That(opts["moments"].Count(), Is.EqualTo(SceneSelect.Moments.Count));
            Assert.That((bool)opts["moments"].First(x => (string)x["id"] == "evac")["ready"], Is.False, "no shipped state: not startable");
            Assert.That((bool)opts["moments"].First(x => (string)x["id"] == "look")["ready"], Is.True, "살펴서 대피 ships its state (11-21)");
            Assert.That((bool)opts["moments"].First(x => (string)x["id"] == "cpr")["ready"], Is.True);
            Assert.That((bool)opts["moments"].First(x => (string)x["id"] == "fire")["ready"], Is.True);
        }

        // ---------------------------------------------------------------- playing them

        static StoryPlaythroughTests.Rig MomentRig(string name)
        {
            var r = new StoryPlaythroughTests.Rig { Name = name };
            r.Store.Set(StoryDirector.SaveKey, MainSave);
            r.D.PreparedSave = ReadDemo;
            return r;
        }

        static void AssertNothingOfThePlayersTouched(StoryPlaythroughTests.Rig r)
        {
            Assert.That(r.Store.Get(StoryDirector.SaveKey), Is.EqualTo(MainSave), "the main save is the player's");
            Assert.That(r.Persist.Keys, Is.Empty, "the host's save mirror is not written");
            Assert.That(r.Host.Saves, Is.EqualTo(0));
            Assert.That(r.Store.Get(StoryDirector.GalleryKey), Is.Null, "no ending recorded");
            Assert.That(r.Store.Get(StoryDirector.RecordsKey), Is.Null, "no records");
            Assert.That(r.Store.Get(SceneSelect.SaveKey), Is.EqualTo(""), "the demo slot is cleared after");
            Assert.That(r.D.DemoRun, Is.False);
            Assert.That(r.D.G, Is.Null);
            Assert.That(r.Logged.Select(l => l.where + ": " + l.e), Is.Empty);
        }

        [TestCase("alert")]
        [TestCase("shelter")]
        [TestCase("cpr")]
        [TestCase("fire")]
        [TestCase("decision")]
        [TestCase("evac")]
        public void EachMoment_PlaysItsChapterFromThePreparedState_WithoutTouchingThePlayersSave(string id)
        {
            var m = SceneSelect.Get(id);
            var r = MomentRig("moment-" + id);
            var resume = m.Key == m.Chapter ? null : m.Key;
            StoryPlaythroughTests.DefaultFrom(r, m.Chapter, resume);
            int idx = ChapterIds.IndexOf(m.Chapter);
            var result = r.Host.Run(() => r.D.PlayPrepared(m, idx + 1));
            Assert.That(r.Fails, Is.Empty, "no fail card");
            Assert.That(r.Played, Is.EqualTo(new[] { m.Chapter + (resume != null ? "@" + resume : "") }));
            Assert.That(result, Is.Null, "the chapter ran to its end");
            Assert.That(r.Host.PendingSteps, Is.EqualTo(0), "every scripted step was used");
            AssertNothingOfThePlayersTouched(r);
            if (id == "cpr")
            {
                Assert.That(r.Host.Calls("enter").First().Text, Is.EqualTo("shelter:spot"), "starts seated in the shelter");
                Assert.That(r.Host.Opens.First().screen, Is.EqualTo(CprScene.Screen), "the grandfather collapses first");
                Assert.That(r.LastG.Events.Count(e => e.Type == "arrived"), Is.EqualTo(1), "the arrival is not played again");
                Assert.That(r.LastG.Events.Count(e => e.Type == "cpr"), Is.EqualTo(1));
            }
            if (id == "shelter")
            {
                Assert.That(r.Host.Calls("enter").First().Text, Is.EqualTo("shelter:stairs"), "starts inside the shelter");
                Assert.That(r.LastG.Events.Count(e => e.Type == "alert"), Is.EqualTo(1), "the alert is not played again");
            }
        }

        [Test]
        public void AMoment_LeavesNoTrailInThePlayersEchoRecord_EvenWithTheirRunBeingRecorded()
        {
            var m = SceneSelect.Get("alert");
            var r = MomentRig("moment-echo");
            // the player's own run is being recorded (a new game was begun earlier in this session)
            var echo = (IStoryEcho)typeof(StoryDirector).GetField("_echo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(r.D);
            echo.Begin();
            var before = r.Store.Get(Echo.Key);
            StoryPlaythroughTests.DefaultFrom(r, m.Chapter, null);
            r.Host.Run(() => r.D.PlayPrepared(m, ChapterIds.IndexOf(m.Chapter) + 1));
            Assert.That(r.Fails, Is.Empty);
            echo.Death("smoke"); // outside the demo the recorder still works...
            var data = Echo.ReadEcho(r.Store.Get(Echo.Key));
            Assert.That(data.Runs, Has.Count.EqualTo(1), "the player's run was saved");
            Assert.That(data.Runs[0].Deaths, Has.Count.EqualTo(1));
            Assert.That(data.Runs[0].Trail, Is.Empty, "...but nothing walked in the prepared run was kept");
            Assert.That(before, Is.Not.Null);
        }

        [Test]
        public void ScenePick_FromTheTitle_StartsTheMoment_AndASlotOrCloseStartsNothing()
        {
            var r = MomentRig("title-scenes");
            r.Host.AnswerOpen("landing", "scenes");
            r.Host.AnswerOpen("sceneSelect", "");
            Assert.That(r.Host.Run(() => r.D.TitleOnce()), Is.EqualTo("scenes"));
            r.Host.AnswerOpen("landing", "scenes");
            r.Host.AnswerOpen("sceneSelect", "nope");
            Assert.That(r.Host.Run(() => r.D.TitleOnce()), Is.EqualTo("scenes"), "an unknown moment is not startable");
            Assert.That(r.Played, Is.Empty);
            var opened = r.Host.Opens.First(o => o.screen == "sceneSelect").options;
            Assert.That(opened["moments"].Where(x => (bool)x["ready"]).Select(x => (string)x["id"]), Is.EqualTo(new[] { "alert", "shelter", "cpr", "fire", "look", "extinguisher", "decision", "evac" }));

            // a ready one plays on to the ending (the ending document's 'menu' keeps nothing)
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            r.Screens["ending"] = _ => "menu";
            r.Host.AnswerOpen("landing", "scenes");
            r.Host.AnswerOpen("sceneSelect", "evac");
            Assert.That(r.Host.Run(() => r.D.TitleOnce()), Is.EqualTo("menu"));
            Assert.That(r.Played, Is.EqualTo(new[] { "evac", "ending" }));
            Assert.That(r.LastG.Events.Any(e => e.Type == "run_recorded"), Is.False, "a prepared run is not recorded");
            AssertNothingOfThePlayersTouched(r);
        }

        [Test]
        public void TeacherMode_PlaysFourMoments_WithASourcedLessonBetween_AndKeepsThePlayersSave()
        {
            var r = MomentRig("teacher");
            // the alert moment ends seated in the shelter (no 서연 steps queued: a run past the stop would get stuck)
            StoryPlaythroughTests.DefaultFrom(r, "dday", "alert", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "endure", "fire", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "crossroads", null);
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            r.Host.AnswerOpen("landing", "teacher");
            var result = r.Host.Run(() => r.D.TitleOnce());
            Assert.That(r.Fails, Is.Empty);
            Assert.That(r.Played, Is.EqualTo(new[] { "dday@alert", "endure@fire", "crossroads", "evac", "ending" }));
            Assert.That(result, Is.EqualTo("restart"), "the ending document's pick");
            var titles = r.Host.Cards.Select(c => c.Title).ToList();
            Assert.That(titles[0], Is.EqualTo(SceneSelect.TeacherTitle));
            Assert.That(titles, Does.Contain("체험 모드"));
            var lessons = r.Host.Cards.Where(c => c.Meta != null && c.Meta.StartsWith(SceneSelect.TeacherTitle + " · ")).ToList();
            Assert.That(lessons.Select(c => c.Title), Is.EqualTo(SceneSelect.TeacherRoute.Select(s => s.LessonTitle)), "4/4 too, after the ending");
            Assert.That(lessons.Last().Meta, Does.EndWith("4/4"));
            Assert.That(r.Host.PendingSteps, Is.EqualTo(0));
            // the alert moment stopped before 서연's part and the fire moment before D+3
            Assert.That(r.Host.Log.Any(c => c.Kind == "card" && c.Text == "D+3"), Is.False);
            Assert.That(r.Host.Calls("enter").Select(c => c.Text), Has.None.EqualTo("town:shelterNotice"));
            var facts = FakeStoryHost.LoadFacts();
            for (int i = 0; i < lessons.Count; i++)
            {
                Assert.That(lessons[i].Body[0], Is.EqualTo(facts.Text(SceneSelect.TeacherRoute[i].Fact)));
                Assert.That(lessons[i].Body[1], Does.StartWith("출처: "));
            }
            var summary = r.Host.Cards.Last();
            Assert.That(summary.Title, Is.EqualTo(SceneSelect.TeacherSummaryTitle));
            Assert.That(summary.Body, Has.Count.EqualTo(4));
            // the lesson comes after its chapter's card and before the next chapter's start
            int firstLesson = r.Host.Log.FindIndex(c => c.Kind == "card" && c.Text == SceneSelect.TeacherRoute[0].LessonTitle);
            int endureStart = r.Host.Log.FindIndex(c => c.Kind == "chapter" && c.Text == "3장");
            Assert.That(firstLesson, Is.GreaterThan(0).And.LessThan(endureStart));
            AssertNothingOfThePlayersTouched(r);
        }

        [Test]
        public void TeacherRoute_LessonTitlesDoNotRepeatTheirFact_AndTheStopsAreStoryMarks()
        {
            var facts = FakeStoryHost.LoadFacts();
            foreach (var s in SceneSelect.TeacherRoute)
            {
                var text = facts.Text(s.Fact);
                Assert.That(text, Is.Not.Null.And.Not.Empty, s.Fact);
                Assert.That(text, Does.Not.StartWith(s.LessonTitle), s.Moment + ": the title is not the fact's first words");
                Assert.That(text, Does.Not.Contain("Ready.gov"), s.Moment + ": a Korean source");
            }
            Assert.That(SceneSelect.TeacherRoute.Select(s => s.StopAt), Is.EqualTo(new[] { DDay.ShelteredMark, Endure.FireDoneMark, null, null }));
            var look = SceneSelect.Get("look").Line;
            Assert.That(look, Does.Not.Contain("문틈"), "only sourced cues (fire_look, fire_door)");
        }

        [Test]
        public void TeacherMode_GivesThePlayersOwnExperienceModeBack()
        {
            var r = MomentRig("teacher-profile");
            r.D.UpdateSettings(new JObject { ["profile"] = "mobility", ["captions"] = false });
            StoryPlaythroughTests.DefaultFrom(r, "dday", "alert", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "endure", "fire", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "crossroads", null);
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            r.Host.AnswerOpen("landing", "teacher");
            r.Host.AnswerCard("start", "deaf"); // the intro, then the class picks 청각 장애
            r.Host.Run(() => r.D.TitleOnce());
            Assert.That(r.Fails, Is.Empty);
            Assert.That((string)r.D.Settings["profile"], Is.EqualTo("mobility"));
            Assert.That((bool)r.D.Settings["captions"], Is.False);
            Assert.That(JObject.Parse(r.Store.Get(StoryDirector.SettingsKey))["profile"].ToString(), Is.EqualTo("mobility"), "the stored settings too");
        }

        [Test]
        public void PauseSave_InAPreparedRun_KeepsNothing_AndSaysSo()
        {
            var r = MomentRig("demo-pause");
            var m = SceneSelect.Get("evac");
            string before = null, after = null;
            System.Threading.Tasks.Task pause = null;
            // queued ahead of the chapter's own steps: at the first roam, Esc and '저장'
            r.Host.Queue(h =>
            {
                before = r.Store.Get(SceneSelect.SaveKey);
                r.Host.AnswerOpen("pause", "save");
                pause = r.D.OpenPause();
            }, () => r.D.DemoRun && r.D.Ctx.Roaming, "pause and save");
            r.Host.Queue(h => after = r.Store.Get(SceneSelect.SaveKey), () => pause != null && pause.IsCompleted, "after the pause");
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            r.Screens["ending"] = _ => "menu";
            r.Host.Run(() => r.D.PlayPrepared(m));
            Assert.That(r.Fails, Is.Empty);
            Assert.That(r.Host.Toasts, Does.Contain(StoryDirector.DemoNoSaveText));
            Assert.That(r.Host.Toasts, Has.No.Member("마지막 체크포인트를 저장했습니다."));
            Assert.That(before, Is.Not.Null.And.Not.Empty);
            Assert.That(after, Is.EqualTo(before), "nothing written by the pause");
            AssertNothingOfThePlayersTouched(r);
        }

        [Test]
        public void ALeftoverDemoSlot_IsClearedAtTheTitle_AndAClassModeIsUndone()
        {
            var r = MomentRig("demo-leftover");
            r.Store.Set(SceneSelect.SaveKey, "{\"index\":4}");
            // a class left by closing the app: the class's experience mode is still in the settings
            r.D.UpdateSettings(new JObject { ["profile"] = "deaf", ["captions"] = true });
            r.Store.Set(SceneSelect.TeacherRestoreKey, "{\"profile\":\"mobility\",\"captions\":false}");
            r.Host.AnswerOpen("landing", "about");
            r.Host.Run(() => r.D.TitleOnce());
            Assert.That(r.Store.Get(SceneSelect.SaveKey), Is.EqualTo(""));
            Assert.That((string)r.D.Settings["profile"], Is.EqualTo("mobility"), "the player's own mode is back");
            Assert.That((bool)r.D.Settings["captions"], Is.False);
            Assert.That(r.Store.Get(SceneSelect.TeacherRestoreKey), Is.EqualTo(""), "once");
        }

        [Test]
        public void TeacherMode_Back_StartsNothing()
        {
            var r = MomentRig("teacher-back");
            r.Host.AnswerOpen("landing", "teacher");
            r.Host.AnswerCard("back");
            Assert.That(r.Host.Run(() => r.D.TitleOnce()), Is.EqualTo("teacher"));
            Assert.That(r.Played, Is.Empty);
            Assert.That(r.Store.Get(StoryDirector.SaveKey), Is.EqualTo(MainSave));
        }
    }
}
