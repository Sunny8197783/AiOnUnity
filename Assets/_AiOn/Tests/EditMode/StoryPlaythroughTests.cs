// The whole story, end to end: StoryDirector over the REAL chapters (Prologue.Run ... Evac.Ending) on FakeStoryHost,
// from the title screen's "new" to the ending document. A default answer policy (first enabled choice, primary card
// action, a sensible result per screen) plus the player steps each roam needs; and an alternative run with different
// key choices (another profile, another prep week, a fail and a retry, another way out). Asserts: the chapters run in
// order, the run reaches the ending without an unexpected exception or log, and the save written at every checkpoint
// round-trips through SaveCodec (and its runtime snapshot through the director's sanitiser).
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StoryPlaythroughTests
    {
        static readonly string[] Ids = { "prologue", "prep", "dday", "endure", "crossroads", "evac", "ending" };

        /// <summary>The host's persist, checking at every checkpoint that the save just written loads back to the same game.</summary>
        internal sealed class CheckingPersist : IStoryPersist
        {
            public FakeStoryHost Host; public MemoryStoryStore Store; public StoryDirector D;
            public readonly List<string> Keys = new();
            public readonly List<string> Problems = new();
            public void Checkpoint(string key)
            {
                Keys.Add(key);
                ((IStoryPersist)Host).Checkpoint(key);
                try
                {
                    var saved = JObject.Parse(Store.Get(StoryDirector.SaveKey));
                    var back = SaveCodec.Load(saved["G"].ToString(Formatting.None), TestSupport.Rules.Db);
                    var again = JObject.Parse(SaveCodec.Save(back));
                    if (!JToken.DeepEquals(saved["G"], again)) Problems.Add($"{key}: the save does not round-trip");
                    if (!JToken.DeepEquals(JObject.Parse(SaveCodec.Save(D.G)), again)) Problems.Add($"{key}: the loaded save differs from the live game");
                    if (back.Checkpoint?.Key != key) Problems.Add($"{key}: the saved checkpoint key is {back.Checkpoint?.Key}");
                    if ((int)saved["index"] != D.Index) Problems.Add($"{key}: saved index {(int)saved["index"]} != {D.Index}");
                    // Restoring the loaded copy gives the game as it was at the checkpoint.
                    var restored = JObject.Parse(SaveCodec.Save(SaveCodec.Restore(SaveCodec.Load(saved["G"].ToString(Formatting.None), TestSupport.Rules.Db))));
                    restored.Remove("checkpoint");
                    var live = JObject.Parse(SaveCodec.Save(D.G)); live.Remove("checkpoint");
                    if (!JToken.DeepEquals(restored, live)) Problems.Add($"{key}: restoring the loaded checkpoint changes the game");
                    // The snapshot survives the director's own sanitiser unchanged (it drops keys whose value is null, i.e. unset).
                    var snap = saved["snap"] is JObject so ? (JObject)so.DeepClone() : null;
                    foreach (var p in snap?.Properties().Where(x => x.Value.Type == JTokenType.Null).ToList() ?? new List<JProperty>()) p.Remove();
                    if (snap == null) Problems.Add($"{key}: no snapshot saved");
                    else if (!JToken.DeepEquals(StoryDirector.ReadSnap(snap), snap))
                        Problems.Add($"{key}: the snapshot changes when read back: {snap.ToString(Formatting.None)} -> {StoryDirector.ReadSnap(snap).ToString(Formatting.None)}");
                }
                catch (Exception e) { Problems.Add($"{key}: {e.GetType().Name} {e.Message}"); }
            }
            public void Save() => Host.Save();
        }

        internal sealed class Rig
        {
            public FakeStoryHost Host = new FakeStoryHost { MaxIdleFrames = 30 * 60 * 3 };
            public MemoryStoryStore Store = new MemoryStoryStore();
            public StoryDirector D;
            public CheckingPersist Persist;
            public readonly List<string> Played = new();
            public readonly List<string> Fails = new();
            /// <summary>Per chapter id: answers to queue as the chapter starts (talk answers are one queue for the whole run).</summary>
            public readonly Dictionary<string, Action<FakeStoryHost>> Before = new();
            public readonly List<(string where, Exception e)> Logged = new();
            /// <summary>The game of the last chapter started (the director drops its G when the run ends).</summary>
            public GameState LastG;
            public readonly Dictionary<string, int> TalkAnswersLeftAtStart = new();
            /// <summary>Answers for screens, overriding the policy: per screen, a function of the options.</summary>
            public readonly Dictionary<string, Func<JObject, JToken>> Screens = new();
            public string Name = "run";

            readonly Dictionary<(string, string), int> _picked = new();
            int _talks;
            int Picked(string speaker, string id) => _picked.TryGetValue((speaker, id), out var n) ? n : 0;

            public Rig()
            {
                var parts = DirectorParts.Default();
                parts.Chapters = parts.Chapters.Select(ch => new DirectorChapter(ch.Id, c =>
                {
                    Played.Add(ch.Id + (c.Resume != null ? "@" + c.Resume : ""));
                    LastG = c.G;
                    TalkAnswersLeftAtStart[ch.Id] = Host.PendingTalkAnswers;
                    if (Before.TryGetValue(ch.Id, out var before)) before(Host);
                    return ch.Run(c);
                })).ToList();
                Persist = new CheckingPersist { Host = Host, Store = Store };
                D = new StoryDirector(TestSupport.Rules, FakeStoryHost.LoadFacts(), Host, Host, Host, Host, Store, parts, Persist);
                Persist.D = D;
                D.Random = () => 0.25;
                D.Today = () => "2026-11-16";
                D.Log = (w, e) => Logged.Add((w, e));
                // The default talk policy: the first enabled choice, but not the same one a fourth time in the same talk
                // (a player does not pick a dead end forever: the original's blackout menu re-offers a radio without batteries).
                Host.TalkPolicy = o =>
                {
                    if (++_talks > 4000) throw new FakeHostException("more than 4000 talks: the story loops on a choice");
                    var open = o.Choices.Where(c => !c.Disabled).ToList();
                    var pick = open.FirstOrDefault(c => Picked(o.Speaker, c.Id) < 3) ?? open.FirstOrDefault();
                    if (pick != null) _picked[(o.Speaker, pick.Id)] = Picked(o.Speaker, pick.Id) + 1;
                    return pick?.Id;
                };
                // Screens: what a player would hand back from each (the UI's result shapes); {} for the rest.
                var basePolicy = Host.OpenPolicy;
                Host.OpenPolicy = (screen, o) =>
                {
                    if (screen == "failCard") Fails.Add(o?.ToString(Formatting.None));
                    if (Screens.TryGetValue(screen, out var custom)) return custom(o);
                    switch (screen)
                    {
                        case "landing": return "new";
                        case "failCard": return "menu";
                        case "ending": return "restart";
                        case "radio": return JObject.Parse("{ official: true, noted: [] }");
                        case "rereadCaption": return "next";
                        case "extinguisher": return JObject.Parse("{ sequence: ['fight', 'shout', 'pin', 'aim', 'squeeze', 'sweep'] }");
                        case "compose": return JObject.Parse("{ chips: { what: 'gate_closed', time: true, route: true, unknowns: false, advice: 'follow_me' }, text: '', edited: false }");
                        case MountainScene.Screen: return MountainSceneTests.Sighting(o); // the report card, every line as seen, to 112
                        default: return basePolicy(screen, o);
                    }
                };
            }

            /// <summary>The chapter the steps queued next belong to: a step only runs while that chapter plays (no leaking).</summary>
            public string Scope;
            public Rig In(string chapter) { Scope = chapter; return this; }
            bool InScope(string scope) => scope == null || (Played.Count > 0 && Played.Last().Split('@')[0] == scope);

            public bool Roam(string level = null) => D.Ctx.Roaming && Host.ControlsEnabled && (level == null || Host.LevelId == level);

            static (string level, string id) Split(string pid)
            {
                int i = pid.IndexOf(':');
                return i < 0 ? (null, pid) : (pid.Substring(0, i), pid.Substring(i + 1));
            }

            /// <summary>Press E on points ("level:id") in order, each once a roam runs in that level.</summary>
            public Rig Press(params string[] ids)
            {
                foreach (var pid in ids)
                {
                    var (lv, id) = Split(pid);
                    var scope = Scope;
                    Host.Queue(h => h.Press(id), () => InScope(scope) && Roam(lv), "press " + pid);
                }
                return this;
            }

            /// <summary>Walk onto a point (a walk-through door).</summary>
            public Rig Walk(string pid)
            {
                var (lv, id) = Split(pid);
                var scope = Scope;
                Host.Queue(h => h.WalkTo(id), () => InScope(scope) && Roam(lv), "walk " + pid);
                return this;
            }

            /// <summary>EndureTests-style steps: "press level:id", "door level:id" (walk onto it), "y 5.6" (climb), "stairs1F level:x".</summary>
            public Rig Steps(params string[] steps)
            {
                foreach (var step in steps)
                {
                    var parts = step.Split(' ');
                    string verb = parts[0], target = parts[1];
                    var (level, _) = Split(target);
                    switch (verb)
                    {
                        case "press": Press(target); break;
                        case "door": Walk(target); break;
                        case "y": When(() => Roam("apartment"), x => { x.PlayerY = double.Parse(target, System.Globalization.CultureInfo.InvariantCulture); x.RunFrame(); }, "climb"); break;
                        case "stairs1F": When(() => Roam(level), x => { x.MoveTo(5, 0.9, 0); x.RunFrame(); }, "down to 1F"); break;
                        default: throw new ArgumentException(step);
                    }
                }
                return this;
            }

            public Rig When(Func<bool> when, Action<FakeStoryHost> step, string label)
            {
                var scope = Scope;
                Host.Queue(step, () => InScope(scope) && when(), label);
                return this;
            }

            /// <summary>The title screen once: "new", the intro, every chapter, the ending's pick.</summary>
            public string Play()
            {
                try { return Host.Run(() => D.TitleOnce()); }
                finally
                {
                    Console.WriteLine($"  [{Name}] played: {string.Join(", ", Played)}; checkpoints: {string.Join(", ", Persist.Keys)}; frames {Host.FrameCount}, host calls {Host.Log.Count}");
                    foreach (var f in Fails) Console.WriteLine($"  [{Name}] fail card: {f}");
                    if (Environment.GetEnvironmentVariable("PLAY_DUMP") == Name)
                    {
                        int from = Host.Log.FindLastIndex(c => c.Kind == "checkpoint" && c.Text == Environment.GetEnvironmentVariable("PLAY_FROM"));
                        foreach (var c in Host.Log.Skip(Math.Max(0, from)))
                            if (c.Kind == "talk" && c.Data is TalkOptions t && t.Choices?.Count > 0) Console.WriteLine($"    talk {t.Speaker}: {string.Join(" | ", t.Choices.Select(ch => ch.Id + (ch.Disabled ? "(x)" : "") + "=" + ch.Label))}");
                            else if (c.Kind is "answer" or "press" or "walk" or "enter" or "card" or "open") Console.WriteLine($"    {c.Kind} {c.Text}");
                    }
                }
            }

            public List<string> EventTypes => LastG.Events.Select(e => e.Type).ToList();
        }

        static void AssertWholeRun(Rig r, string result, string expectResult)
        {
            var h = r.Host;
            Assert.That(r.Played.Select(p => p.Split('@')[0]).Distinct(), Is.EqualTo(Ids), "the chapters run in order");
            Assert.That(r.Persist.Keys.First(), Is.EqualTo("prologue"));
            var starts = Ids.Select(id => r.Persist.Keys.IndexOf(id)).ToList();
            Assert.That(starts, Is.Ordered, "a checkpoint at the start of every chapter, in order");
            Assert.That(starts, Has.All.GreaterThanOrEqualTo(0), "a checkpoint at the start of every chapter");
            Assert.That(r.Persist.Problems, Is.Empty, "every checkpoint's save round-trips through SaveCodec");
            Assert.That(r.Logged.Select(l => l.where + ": " + l.e), Is.Empty, "nothing logged as failed (cine, preload, gallery)");
            Assert.That(r.TalkAnswersLeftAtStart.Values, Has.All.EqualTo(0), "no scripted talk answer leaks into a later chapter");
            Assert.That(h.PendingSteps + h.PendingTalkAnswers + h.PendingCardAnswers + h.PendingOpenAnswers, Is.EqualTo(0), "every scripted step and answer was used");

            // The ending: the run record, then the document; the director's result is the document's pick.
            var ending = h.Opens.Where(o => o.screen == "ending").ToList();
            Assert.That(ending, Has.Count.EqualTo(1), "the ending document once");
            Assert.That(ending[0].options["record"], Is.Not.Null);
            Assert.That(result, Is.EqualTo(expectResult));
            var g = r.LastG;
            var types = r.EventTypes;
            Assert.That(g.Events.Where(e => e.Type == "chapter_time").Select(e => (string)e.Details["id"]), Is.EqualTo(Challenges.Timed));
            Assert.That(types.Count(t => t == "run_recorded"), Is.EqualTo(1));
            foreach (var t in new[] { "blackout", "alert", "departure", "strike", "arrived", "smoke_choice", "sms" })
                Assert.That(types, Does.Contain(t), t);
            // In story order: the blackout and the alert (D-day), the night fire (crossroads), the evacuation notice, the record.
            Assert.That(types.IndexOf("blackout"), Is.LessThan(types.IndexOf("alert")));
            Assert.That(types.IndexOf("alert"), Is.LessThan(types.IndexOf("smoke_choice")));
            Assert.That(types.IndexOf("smoke_choice"), Is.LessThan(types.LastIndexOf("sms")));
            Assert.That(types.LastIndexOf("sms"), Is.LessThan(types.IndexOf("run_recorded")));
            Assert.That(r.Store.Get(StoryDirector.GalleryKey), Is.Not.Null.And.Not.Empty, "the gallery remembers the ending");
            Assert.That(h.Log.Where(c => c.Kind == "chapter").Select(c => c.Text).Distinct().Count(), Is.GreaterThanOrEqualTo(6), "the chapter titles");
        }

        /// <summary>
        /// The default run's rig, every step queued (the caller plays it): the first enabled choice everywhere, the primary
        /// card action, and the scripted roams. SceneSelectTests takes its prepared states (장면 바로가기) from this run.
        /// </summary>
        internal static Rig DefaultRun(string name)
        {
            var r = new Rig { Name = name };
            var h = r.Host;
            r.In("prologue");
            // Prologue: the first choice everywhere; desk, lead, window, exit.
            r.Press("office:desk", "office:coworker", "office:window", "office:officeExit");
            DefaultFrom(r, "prep", null);
            DefaultFrom(r, "dday", null);
            DefaultFrom(r, "endure", null);
            DefaultFrom(r, "crossroads", null);
            DefaultFrom(r, "evac", null);
            return r;
        }

        /// <summary>
        /// Prep, week A: equipment tonight, then the mart, the shelter, the neighbours and the bag; 「22분」 after tonight's
        /// slot answered carefully. From the 「22분」 prepared state (resume "twentytwo") tonight's slot is already done.
        /// </summary>
        static void DefaultPrep(Rig r, string resume)
        {
            var h = r.Host;
            bool fromMorning = resume == Prep.TwentyTwoResume;
            r.Before["prep"] = TwentyTwoTests.AnswerCarefully;
            foreach (var p in new[] { "equip", "mart", "shelter", "neighbors", "bag" }.Skip(fromMorning ? 1 : 0)) h.AnswerOpen("planner", p);
            h.AnswerOpen("shop", JObject.Parse("{ cart: { batteries_aa: 2, radio: 1 }, total: 33000 }"));
            h.AnswerOpen("shop", JObject.Parse("{ cart: { water_2l: 4, bars: 3 }, total: 8900 }"));
            h.AnswerOpen("routemap", JObject.Parse("{ choice: 1 }"));
            h.AnswerOpen("bag", JObject.Parse("{ bag: { water_2l: 1, bars: 3, flashlight: 1, radio: 1, meds: 1, clothes: 1 }, pocket: { meds: 1 } }"));
            h.AnswerWhen(o => o.Speaker == "현관" && o.Choices.Any(c => c.Id == "lift"), "lift", once: false);
            if (!fromMorning) r.Press("apartment:homeStock", "apartment:homeGas", "apartment:homeBreaker", "apartment:homePlanner");
            r.Press("apartment:homeDesk", "apartment:homePlanner", "apartment:homeExitDoor", "town:mart", "mart:shelfElec", "mart:shelfWater", "mart:checkout",
                "apartment:homePlanner", "apartment:homeExitDoor", "town:shelterEast", "town:shelterA", "town:shelterB", "town:shelterNotice",
                "apartment:homePlanner", "apartment:homeExitDoor", "apartment:seoyeonDoor", "apartment:elevator", "apartment:managerWindow", "apartment:lobbyExit", "town:store",
                "store:storeCounter", "store:storeExit", "town:home", "apartment:elevator", "apartment:homeDoor",
                "apartment:homePlanner", "apartment:homeBag",
                "apartment:homeSink", "apartment:homeBed");
        }

        /// <summary>
        /// The default run's steps for one chapter, from its start (resume null) or from a resume key ("shelter" and "fire"
        /// skip the steps before them): what a scene-select start of that moment needs. teacherStop: the teacher mode ends
        /// the moment at its stop (SceneSelect.TeacherRoute), so the steps after it are not queued.
        /// </summary>
        internal static void DefaultFrom(Rig r, string chapter, string resume, bool teacherStop = false)
        {
            var h = r.Host;
            r.In(chapter);
            switch (chapter)
            {
                case "prep":
                    DefaultPrep(r, resume);
                    break;
                case "dday":
                    if (resume != DDay.ShelterResume && resume != DDay.CprResume && resume != DDay.ElderResume)
                    {
                        // D-day: wait for the alert, out of the door, toward 304 (어르신 asks; the first answer: help her down),
                        // down the stairs, the manager, the lobby; shelter A after the strike.
                        r.When(() => r.D.Ctx.Roaming && r.D.G.Events.Any(e => e.Type == "alert"), _ => { }, "wait for the alert");
                        r.Press("apartment:homeExitDoor");
                        r.When(() => r.Roam("apartment") && h.PlayerY > 4, x => { x.Point("elder304").Enabled = true; x.MoveTo(12, -1, 5.6); x.RunFrame(); }, "toward 304"); // the level enables her point (elder304 'on')
                        r.When(() => r.Roam("apartment") && h.PlayerY > 4, x => { x.MoveTo(5, 0.9, 0); x.RunFrame(); }, "downstairs");
                        r.Press("apartment:managerWindow", "apartment:lobbyExit");
                        r.When(() => r.Roam("town") && r.D.G.Knows("strikeSeen"), x => x.Press("shelterA"), "shelterA after the strike");
                    }
                    if (resume != DDay.CprResume && resume != DDay.ElderResume) r.Press("shelter:reception", "shelter:spot");
                    // 「304호의 계단」 (after the seat, before the CPR): 어르신 walks down the four flights
                    if (resume != DDay.CprResume && !teacherStop) ElderPovTests.Descend(r);
                    // 서연's part (the real SeoyeonRelay inside D-day): the notice board, then the C ramp.
                    if (!teacherStop) r.Press("town:shelterNotice", "town:shelterC");
                    break;
                case "endure":
                    // Endure: the helper's day (EndureTests "helper" roams), then the night fire.
                    if (resume != "fire" && resume != Endure.MountainResume)
                        r.Steps("press apartment:homeFridge", "press apartment:homeStock", "press apartment:seoyeonDoor", "press apartment:lobbyNotice", "press apartment:homePlanner",
                            "press apartment:homeExitDoor", "door apartment:lobbyExit", "press town:water", "door town:home", "y 5.6", "press apartment:seoyeonDoor",
                            "door apartment:lobbyExit", "door town:store", "press store:storeCounter", "door store:storeExit", "door town:home", "press apartment:homeBed");
                    // 「산에서 내려온 사람들」 (D+2 at home): the report at the desk, the intercom, the window, the calendar,
                    // answered as a careful player would (the prepared saves are taken from this run)
                    r.Before["endure"] = MountainSceneTests.AnswerCarefully;
                    if (resume != "fire") r.Steps(MountainSceneTests.DaySteps);
                    r.Steps("press apartment:extinguisher");
                    break;
                case "crossroads":
                    // 「임시 거처의 하루」's scene-select start (moved, D+6): the day, then the shelter's fire
                    if (resume == Evac.ShelterLifeResume) { ShelterLifeTests.Day(r); break; }
                    // Crossroads: feel the door handle, then stay in and call 119 (after the handle, the first choice is the extinguisher run).
                    r.Before["crossroads"] = x => x.AnswerSpeaker("판단", "handle").AnswerSpeaker("판단", "stay");
                    break;
                case "evac":
                    // Evacuation: out of the door and the lobby to the assembly point (the bus).
                    r.Press("apartment:homeExitDoor", "apartment:lobbyExit", "town:assembly");
                    break;
            }
        }

        /// <summary>The default run: the first enabled choice everywhere, the primary card action, and the scripted roams.</summary>
        [Test]
        public void DefaultAnswers_NewGameToTheEndingDocument()
        {
            var r = DefaultRun("default");
            var h = r.Host;
            var result = r.Play();

            Assert.That(r.Fails, Is.Empty, "no fail card");
            Assert.That(r.Played, Is.EqualTo(Ids), "every chapter once, from its start");
            AssertWholeRun(r, result, "restart");
            var g = r.LastG;
            Assert.That(r.Persist.Keys, Is.EqualTo(new[] { "prologue", "prep", "dday", "alert", "endure", "fire", "crossroads", "fire4", "evac", "depart", "ending" }));
            Assert.That(g.Slots.Done, Is.EqualTo(new[] { "equip", "mart", "shelter", "neighbors", "bag" }));
            Assert.That((string)g.Know["path"], Is.EqualTo("bus"));
            Assert.That(r.EventTypes, Does.Contain("arrived_assembly"));
            Assert.That(r.EventTypes, Has.No.Member("stub_seoyeon_relay"), "the real SeoyeonRelay ran");
            Assert.That(g.Extra.ContainsKey("relay"), "서연's relay times");
            Assert.That((string)g.Events.Single(e => e.Type == "smoke_choice" && (string)e.Details["pick"] != "handle").Details["pick"], Is.EqualTo("stay"));
            Assert.That(h.Cards.First(c => c.Actions.Count > 0).Title, Is.EqualTo("체험 모드"));
            Assert.That(r.Store.Get(StoryDirector.SaveKey), Is.EqualTo(""), "'restart' clears the save");
            Assert.That(r.D.G, Is.Null);
        }

        /// <summary>
        /// Different key choices: the deaf profile, a prologue with every signal sorted right, prep week B (documents, home,
        /// rest), the phone first at the blackout and the elevator tried, 정민 alone through the endure days, a first attempt
        /// at the night fire with the extinguisher that fails and is retried from the checkpoint, running out instead, and the
        /// volunteer way out; the ending document returns 'menu', so the save is kept.
        /// </summary>
        [Test]
        public void AlternativeChoices_WithARetry_ReachTheEnding()
        {
            var r = new Rig { Name = "alternative" };
            var h = r.Host;
            h.AnswerCard("deaf");
            r.D.Settings["view"] = "first"; // the player plays in first person: 서연's re-read must hand the camera back that way
            r.In("prologue");
            // Prologue: every signal sorted right, text the driver, agree with the lead; out of order first.
            r.Before["prologue"] = x =>
            {
                foreach (var s in Prologue.Signals) x.AnswerSpeaker(s.From, s.Answer);
                x.AnswerSpeaker("창밖", "text");
                x.AnswerWhen(o => o.Speaker == "윤 팀장" && o.Choices != null, "ok");
            };
            r.Press("office:coworker", "office:window", "office:officeExit", "office:desk", "office:desk", "office:officeExit", "office:coworker", "office:coworker", "office:officeExit");
            r.In("prep");
            // Prep, week B.
            r.Before["prep"] = x =>
            {
                x.AnswerSpeaker("베란다 수납장", "toss").AnswerSpeaker("주방", "yes");
                x.AnswerSpeaker("오서연", "both").AnswerWhen(o => o.Speaker == "오서연" && o.Choices.Any(c => c.Id == "yes"), "yes");
                x.AnswerSpeaker("이웃", "yes").AnswerSpeaker("침대", "sleep");
            };
            foreach (var p in new JToken[] { "docs", JValue.CreateNull(), "home", "rest", "neighbors", "rest", "bag" }) h.AnswerOpen("planner", p);
            h.AnswerOpen("docs", JObject.Parse("{ card: { family: '부모님', meet: '대피소', idcopy: true }, where: 'bag' }"));
            h.AnswerOpen("bag", JObject.Parse("{ bag: { water_2l: 1, flashlight: 1 }, pocket: {} }"));
            r.Press("apartment:homeDesk",
                "apartment:homePlanner", "apartment:homePlanner", "apartment:homeWindow", "apartment:homeStock", "apartment:homeSink",
                "apartment:homePlanner",
                "apartment:homePlanner", "apartment:homeExitDoor", "apartment:seoyeonDoor", "apartment:homeDoor", "apartment:homePlanner",
                "apartment:homePlanner",
                "apartment:homePlanner", "apartment:homeBag",
                "apartment:homeSink", "apartment:homeBed");
            r.In("dday");
            // D-day: the phone first at the blackout; the elevator is tried; shelter B after the strike.
            r.Before["dday"] = x => x.AnswerSpeaker("정전", "phone");
            r.When(() => r.D.Ctx.Roaming && r.D.G.Events.Any(e => e.Type == "alert"), _ => { }, "wait for the alert");
            r.Press("apartment:homeExitDoor");
            r.When(() => r.Roam("apartment") && h.PlayerY > 4, x => { x.MoveTo(5, 0.9, 0); x.RunFrame(); }, "downstairs");
            r.Press("apartment:elevator", "apartment:lobbyExit");
            r.When(() => r.Roam("town") && r.D.G.Knows("strikeSeen"), x => x.Press("shelterB"), "shelterB after the strike");
            r.Press("shelter:spot", "shelter:reception", "shelter:spot");
            r.Press("town:shelterNotice", "town:shelterB", "town:shelterC");
            r.In("endure");
            // Endure: alone (EndureTests "alone" roams).
            r.Steps("press apartment:homeFridge", "press apartment:homeRadio", "press apartment:seoyeonDoor", "press apartment:homePlanner",
                "press apartment:homeExitDoor", "door apartment:lobbyExit", "press town:water", "door town:home", "y 5.6",
                "door apartment:lobbyExit", "door town:store", "press store:storeCounter", "door store:storeExit", "door town:home", "press apartment:homeBed");
            r.Steps(MountainSceneTests.DaySteps);
            r.Steps("press apartment:hallFire", "press apartment:hallFire", "press apartment:hallFire", "stairs1F apartment:stairs1F");
            r.Before["endure"] = x => x.AnswerSpeaker("오서연", "no").AnswerSpeaker("급수대", "go")
                .AnswerSpeaker("복도", "shout").AnswerSpeaker("복도", "water").AnswerSpeaker("복도", "retreat");
            r.Screens["extinguisher"] = o => JObject.Parse("{ sequence: ['shout', 'water', 'retreat'] }");
            r.In("crossroads");
            // Crossroads: the extinguisher first (a fail), retry, then run out down the stairs.
            r.Before["crossroads"] = x =>
            {
                if (r.Played.Count(p => p.StartsWith("crossroads")) == 1) x.AnswerSpeaker("판단", "fight");
                else x.AnswerSpeaker("판단", "run").AnswerSpeaker("박영수 소장", "rest");
            };
            r.Screens["failCard"] = o => r.Played.Last().StartsWith("crossroads") ? "retry" : "menu";
            r.In("evac");
            // Evacuation: volunteer at the community centre.
            r.Before["evac"] = x => x.AnswerSpeaker("행정복지센터", "yes");
            r.Press("apartment:homeExitDoor", "apartment:lobbyExit", "town:center");
            r.Screens["ending"] = o => "menu";

            var result = r.Play();

            Assert.That(r.Fails, Has.Count.EqualTo(1), "one fail card: the night fire");
            Assert.That(r.Played, Is.EqualTo(new[] { "prologue", "prep", "dday", "endure", "crossroads", "crossroads@fire4", "evac", "ending" }),
                "crossroads replayed from its mid-chapter checkpoint after the retry");
            Assert.That(r.Fails[0], Does.Contain("연기 속으로 불을 끄러 나갔습니다"));
            AssertWholeRun(r, result, "menu");
            var g = r.LastG;
            Assert.That(r.D.StorySettings.Profile, Is.EqualTo("deaf"));
            int reread = h.Log.FindLastIndex(c => c.Kind == "open" && c.Text == "reread");
            Assert.That(reread, Is.GreaterThan(0), "서연's re-read played");
            Assert.That(h.Log.Skip(reread).First(c => c.Kind == "view").Text, Is.EqualTo("first"), "the camera goes back to the player's view setting");
            Assert.That(g.Slots.Done, Is.EqualTo(new[] { "docs", "home", "rest", "neighbors", "rest", "bag" }));
            Assert.That((string)g.Know["path"], Is.EqualTo("volunteer"));
            Assert.That(g.Events.Where(e => e.Type == "signal_sorted").Select(e => (bool)e.Details["ok"]), Has.All.True, "every signal sorted right");
            Assert.That(r.EventTypes, Does.Contain("elevator_tried"));
            Assert.That(r.EventTypes, Does.Contain("ate_spoiled").And.Contain("fire_shout").And.Contain("fire_retreat"), "alone: ate the old food, shouted, then retreated from the hall fire");
            Assert.That(g.Events.Where(e => e.Type == "smoke_choice").Select(e => (string)e.Details["pick"]), Is.EqualTo(new[] { "run" }), "the failed attempt was rolled back");
            Assert.That(g.Events.Single(e => e.Type == "chapter_time" && (string)e.Details["id"] == "crossroads").Details.Value<int>("retries"), Is.EqualTo(1));
            // 'menu' keeps the save: the ending checkpoint, and it loads.
            var saved = JObject.Parse(r.Store.Get(StoryDirector.SaveKey));
            Assert.That((int)saved["index"], Is.EqualTo(Ids.Length - 1));
            Assert.That(SaveCodec.Load(saved["G"].ToString(Formatting.None), TestSupport.Rules.Db).Checkpoint.Key, Is.EqualTo("ending"));
        }
    }
}
