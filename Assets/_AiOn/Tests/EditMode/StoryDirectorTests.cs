// StoryDirector (app.js) with FakeStoryHost and stub chapters: the run order, soft-fail retries from the chapter start and
// from a mid-chapter checkpoint, the fail card's 'menu', save / continue with the runtime snapshot, the title screens,
// the run record, settings and profiles, and the global keys / body hooks. No original test covers app.js; these
// assert what its code does for the same inputs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StoryDirectorTests
    {
        static readonly string[] Ids = { "prologue", "prep", "dday", "endure", "crossroads", "evac", "ending" };

        sealed class Rig
        {
            public FakeStoryHost Host;
            public StoryDirector D;
            public MemoryStoryStore Store;
            public readonly List<string> Played = new();
            public readonly List<string> Calls = new(); // danger / echo / timelapse calls
            public GameState LastG;
            /// <summary>Per chapter id: what the stub does (attempt number starts at 1). Null: just records an event.</summary>
            public readonly Dictionary<string, Func<StoryContext, int, Task<JToken>>> Body = new();
            readonly Dictionary<string, int> _attempts = new();
            public int Attempts(string id) => _attempts.TryGetValue(id, out var n) ? n : 0;

            public Rig(MemoryStoryStore store = null, Func<StoryContext, string, string, IReadOnlyList<string>, Task> timelapse = null)
            {
                Host = new FakeStoryHost();
                Store = store ?? new MemoryStoryStore();
                var chapters = Ids.Select(id => new DirectorChapter(id, async c =>
                {
                    LastG = c.G;
                    _attempts[id] = Attempts(id) + 1;
                    Played.Add($"{id}#{Attempts(id)}{(c.Resume != null ? "@" + c.Resume : "")}");
                    c.Event("stub", new JObject { ["id"] = id });
                    if (Body.TryGetValue(id, out var b)) return await b(c, Attempts(id));
                    return id == "ending" ? new JValue("restart") : null;
                })).ToList();
                var parts = new DirectorParts
                {
                    Chapters = chapters,
                    Opening = c => { Calls.Add("opening"); return Task.CompletedTask; },
                    Timelapse = timelapse,
                    MakeDanger = (c, hurt) => new FnDanger(p => p?.Name == "looter", (k, x, z, s) => Calls.Add($"danger.{k} {x} {z} {s}"), () => Calls.Add("danger.reset")),
                    MakeEcho = (c, s) => new FnEcho(() => Calls.Add("echo.begin"), () => Calls.Add("echo.resume"), why => Calls.Add("echo.death " + why), () => { Calls.Add("echo.chalk"); return Task.CompletedTask; }),
                };
                D = new StoryDirector(TestSupport.Rules, FakeStoryHost.LoadFacts(), Host, Host, Host, Host, Store, parts, Host);
                D.Random = () => 0.5;
                D.Today = () => "2026-11-16";
            }

            public string Title(params (string screen, JToken answer)[] opens)
            {
                foreach (var (s, a) in opens) Host.AnswerOpen(s, a);
                return Host.Run(() => D.TitleOnce());
            }

            public List<string> EventTypes(string type) => LastG.Events.Where(e => e.Type == type).Select(e => (string)e.Details["id"]).ToList();
        }

        static JObject Saved(MemoryStoryStore s) => JObject.Parse(s.Get(StoryDirector.SaveKey));

        // ------------------------------------------------------------------ the whole run

        [Test]
        public void NewGameRunsEveryChapterInOrderWithACheckpointEachAndTimes()
        {
            var r = new Rig();
            r.Body["ending"] = async (c, n) => { await c.Wait(2); return new JValue("restart"); }; // frames pass: the controls toast fires
            var result = r.Title(("landing", "new"));
            Assert.AreEqual("restart", result);
            CollectionAssert.AreEqual(Ids.Select(i => i + "#1"), r.Played);
            CollectionAssert.AreEqual(Ids, r.Host.Checkpoints, "a checkpoint at every chapter start");
            Assert.AreEqual(new[] { "opening" }, r.Calls.Where(x => x == "opening").ToArray());
            Assert.Contains("echo.begin", r.Calls);
            // intro: the experience-mode card (default: 기본), then the controls toast 1.5 s later
            var card = r.Host.Cards.Single(c => c.Title == "체험 모드");
            CollectionAssert.AreEqual(new[] { "none", "mobility", "wheelchair", "lowvision", "deaf", "guardian" }, card.Actions.Select(a => a.Id));
            Assert.Contains("조작법은 언제든 H 키로 볼 수 있습니다.", r.Host.Toasts);
            // chapter_time for the six timed chapters (not the ending), no retries, not partial
            var times = r.LastG.Events.Where(e => e.Type == "chapter_time").ToList();
            CollectionAssert.AreEqual(Challenges.Timed, times.Select(e => (string)e.Details["id"]));
            Assert.That(times.All(e => (int)e.Details["retries"] == 0 && e.Details["partial"] == null && (double)e.Details["ms"] >= 0));
            // 'restart' clears the save; the game is dropped
            Assert.AreEqual("", r.Store.Get(StoryDirector.SaveKey));
            Assert.IsNull(r.D.G);
            Assert.IsFalse(r.Host.HudShown);
            // the event order of a whole run: stub, chapter_time per chapter (ending has no time)
            var order = r.LastG.Events.Select(e => e.Type).Where(t => t == "stub" || t == "chapter_time").ToList();
            CollectionAssert.AreEqual(Ids.Take(6).SelectMany(_ => new[] { "stub", "chapter_time" }).Append("stub"), order);
        }

        [Test]
        public void AFinishedRunWithoutRestartKeepsTheSave()
        {
            var r = new Rig();
            r.Body["ending"] = (c, n) => Task.FromResult<JToken>(new JValue("menu"));
            Assert.AreEqual("menu", r.Title(("landing", "new")));
            Assert.AreEqual(6, (int)Saved(r.Store)["index"]);
            Assert.AreEqual("ending", (string)Saved(r.Store)["G"]["checkpoint"]["key"]);
        }

        [Test]
        public void IntroProfilePickAppliesTheProfile()
        {
            var r = new Rig();
            r.Host.AnswerCard("deaf");
            r.D.Settings["captions"] = false;
            r.Title(("landing", "new"));
            Assert.AreEqual("deaf", (string)r.D.Settings["profile"]);
            Assert.IsTrue((bool)r.D.Settings["captions"], "deaf turns captions on");
            Assert.AreEqual("deaf", r.D.StorySettings.Profile);
            Assert.AreEqual("deaf", (string)JObject.Parse(r.Store.Get(StoryDirector.SettingsKey))["profile"], "settings are stored");
        }

        // ------------------------------------------------------------------ soft fails

        [Test]
        public void AFailAtTheChapterStartRetriesFromTheStartWithTheGameRolledBack()
        {
            var r = new Rig();
            long money0 = 0;
            r.Body["prep"] = (c, n) =>
            {
                if (n == 1)
                {
                    money0 = c.G.Money;
                    c.G.Money -= 5000;
                    c.SetLevelState("town", "strike", "hit");
                    c.Power = false; c.OutdoorEnv = "night"; c.LightSource = "phone"; c.SeoyeonAsk = "yes";
                    c.Fail("late");
                }
                Assert.AreEqual(money0, c.G.Money, "rolled back to the chapter start");
                Assert.IsFalse(c.LevelStates.ContainsKey("town"));
                Assert.IsTrue(c.Power); Assert.AreEqual("day", c.OutdoorEnv);
                Assert.IsNull(c.LightSource); Assert.IsNull(c.SeoyeonAsk);
                Assert.IsNull(c.Resume, "retry of the chapter start");
                return Task.FromResult<JToken>(null);
            };
            r.Host.AnswerOpen("failCard", "retry");
            r.Title(("landing", "new"));
            CollectionAssert.AreEqual(new[] { "prologue#1", "prep#1", "prep#2", "dday#1", "endure#1", "crossroads#1", "evac#1", "ending#1" }, r.Played);
            var fail = r.Host.Opens.Single(o => o.screen == "failCard").options;
            var expect = Debrief.FailDebrief(TestSupport.Rules, r.LastG, "late", new JObject(), FakeStoryHost.LoadFacts().Text, FakeStoryHost.LoadFacts().SourceId);
            Assert.AreEqual(expect.Title, (string)fail["title"]);
            Assert.AreEqual(expect.SourceIds.Count, ((JArray)fail["sources"]).Count);
            Assert.IsNull(fail["sourceIds"]);
            Assert.That(((JArray)fail["sources"]).All(s => s["publisher"] != null && s["id"] != null), "sources are the source objects");
            var t = r.LastG.Events.Single(e => e.Type == "chapter_time" && (string)e.Details["id"] == "prep");
            Assert.AreEqual(1, (int)t.Details["retries"]);
            Assert.AreEqual(1, r.Host.Checkpoints.Count(k => k == "prep"), "a retry does not make a new chapter checkpoint");
            Assert.That(r.Calls.Count(x => x == "danger.reset") >= 2, "reset at the run start and at the retry");
        }

        [Test]
        public void AFailAfterAMidChapterCheckpointRetriesFromIt()
        {
            var r = new Rig();
            r.Body["dday"] = async (c, n) =>
            {
                if (c.Resume != "alert")
                {
                    c.SetLevelState("town", "closures", new JArray("sijang"));
                    c.Power = false; c.OutdoorEnv = "blackout"; c.LightSource = "flashlight"; c.SeoyeonAsk = "no";
                    c.Trail = new List<JObject> { new JObject { ["x"] = 1.5, ["z"] = 2 } };
                    c.G.Money = 777;
                    c.Checkpoint("alert");
                }
                if (n == 1)
                {
                    c.G.Money = 1; c.SetLevelState("town", "strike", "hit"); c.SeoyeonAsk = "yes";
                    c.Audio.Loop("siren", true); c.Engine.SetControls(false);
                    c.DeathCause = "smoke";
                    throw new StoryFail("dead", new JObject { ["why"] = "smoke" });
                }
                Assert.AreEqual("alert", c.Resume);
                Assert.AreEqual(777, c.G.Money);
                CollectionAssert.AreEqual(new[] { "closures" }, c.LevelStates["town"].Keys);
                Assert.IsFalse(c.Power); Assert.AreEqual("blackout", c.OutdoorEnv);
                Assert.AreEqual("flashlight", c.LightSource); Assert.AreEqual("no", c.SeoyeonAsk);
                Assert.AreEqual(1, c.Trail.Count);
                Assert.IsNull(c.DeathCause);
                Assert.IsTrue(c.Engine.ControlsEnabled);
                await Task.Yield();
                return null;
            };
            r.Host.AnswerOpen("failCard", "retry");
            r.Title(("landing", "new"));
            Assert.Contains("dday#2@alert", r.Played);
            Assert.Contains("echo.death smoke", r.Calls);
            Assert.IsFalse(r.Host.Loops.Contains("siren"), "the fail turns the siren off");
            var fail = r.Host.Opens.Single(o => o.screen == "failCard").options;
            Assert.AreEqual(Debrief.FailDebrief(TestSupport.Rules, r.LastG, "dead", new JObject { ["why"] = "smoke" }, FakeStoryHost.LoadFacts().Text, FakeStoryHost.LoadFacts().SourceId).Title, (string)fail["title"]);
            var t = r.LastG.Events.Single(e => e.Type == "chapter_time" && (string)e.Details["id"] == "dday");
            Assert.AreEqual(1, (int)t.Details["retries"]);
        }

        [Test]
        public void TheFailCardsOtherChoiceEndsTheRunForTheMenu()
        {
            var r = new Rig();
            r.Body["endure"] = (c, n) => { c.Fail("fire", new JObject { ["scene"] = "hall" }); return Task.FromResult<JToken>(null); };
            r.Host.AnswerOpen("failCard", "menu");
            Assert.AreEqual("menu", r.Title(("landing", "new")));
            CollectionAssert.AreEqual(new[] { "prologue#1", "prep#1", "dday#1", "endure#1" }, r.Played);
            Assert.IsTrue(r.Host.ControlsEnabled);
            Assert.AreEqual(3, (int)Saved(r.Store)["index"], "the save keeps the failed chapter");
            Assert.IsFalse(r.Calls.Any(x => x.StartsWith("echo.death")), "not a death");
        }

        [Test]
        public void AnErrorThatIsNotAStoryFailIsNotCaught()
        {
            var r = new Rig();
            r.Body["prep"] = (c, n) => throw new InvalidOperationException("bug");
            r.Host.AnswerOpen("landing", "new");
            Assert.Throws<InvalidOperationException>(() => r.Host.Run(() => r.D.TitleOnce()));
            Assert.AreEqual(0, r.Host.Opens.Count(o => o.screen == "failCard"));
        }

        // ------------------------------------------------------------------ save and continue

        [Test]
        public void ContinueResumesTheSavedChapterAtItsCheckpointWithTheSnapshot()
        {
            var store = new MemoryStoryStore();
            var r1 = new Rig(store);
            r1.Body["endure"] = (c, n) =>
            {
                c.SetLevelState("town", "closures", new JArray("bridge"));
                c.SetLevelState("shelter", "phase", "night");
                c.Power = false; c.OutdoorEnv = "night"; c.IndoorEnv = "interiorNight"; c.LightSource = "flashlight";
                c.G.SetKnow("fireSeen", true);
                c.Checkpoint("fire");
                c.G.SetKnow("afterFire", true); // not in the save
                throw new InvalidOperationException("the player closed the game");
            };
            r1.Host.AnswerOpen("landing", "new");
            Assert.Throws<InvalidOperationException>(() => r1.Host.Run(() => r1.D.TitleOnce()));
            Assert.AreEqual(3, (int)Saved(store)["index"]);
            Assert.AreEqual("fire", (string)Saved(store)["G"]["checkpoint"]["key"]);
            Assert.AreEqual("bridge", (string)Saved(store)["snap"]["levelStates"]["town"]["closures"][0]);

            var r2 = new Rig(store);
            r2.Body["endure"] = (c, n) =>
            {
                Assert.AreEqual("fire", c.Resume);
                Assert.IsTrue(c.G.Knows("fireSeen")); Assert.IsFalse(c.G.Knows("afterFire"));
                Assert.AreEqual("bridge", (string)((JArray)c.LevelStates["town"]["closures"])[0]);
                Assert.AreEqual("night", (string)c.LevelStates["shelter"]["phase"]);
                Assert.IsFalse(c.Power); Assert.AreEqual("night", c.OutdoorEnv); Assert.AreEqual("interiorNight", c.IndoorEnv);
                Assert.AreEqual("flashlight", c.LightSource);
                return Task.FromResult<JToken>(null);
            };
            Assert.AreEqual("restart", r2.Title(("landing", "continue")));
            Assert.IsTrue((bool)r2.Host.Opens.First(o => o.screen == "landing").options["hasSave"]);
            CollectionAssert.AreEqual(new[] { "endure#1@fire", "crossroads#1", "evac#1", "ending#1" }, r2.Played);
            Assert.IsFalse(r2.Host.Cards.Any(c => c.Title == "체험 모드"), "no intro when continuing");
            Assert.Contains("echo.resume", r2.Calls);
            Assert.AreEqual("crossroads", r2.Host.Checkpoints.First(), "no new checkpoint for the resumed chapter");
            var t = r2.LastG.Events.Single(e => e.Type == "chapter_time" && (string)e.Details["id"] == "endure");
            Assert.IsTrue((bool)t.Details["partial"]);
            Assert.IsNull(r2.LastG.Events.Single(e => e.Type == "chapter_time" && (string)e.Details["id"] == "crossroads").Details["partial"]);
        }

        [Test]
        public void ContinueAtAChapterStartCheckpointReplaysTheChapterAndRemakesItsCheckpoint()
        {
            var store = new MemoryStoryStore();
            var r1 = new Rig(store);
            r1.Body["dday"] = (c, n) => throw new InvalidOperationException("closed");
            r1.Host.AnswerOpen("landing", "new");
            Assert.Throws<InvalidOperationException>(() => r1.Host.Run(() => r1.D.TitleOnce()));
            var r2 = new Rig(store);
            r2.Title(("landing", "continue"));
            Assert.AreEqual("dday#1", r2.Played[0]);
            Assert.AreEqual("dday", r2.Host.Checkpoints[0]);
        }

        [Test]
        public void ABadSaveStartsANewGame()
        {
            var store = new MemoryStoryStore();
            store.Set(StoryDirector.SaveKey, "{\"index\":2,\"G\":{\"v\":1}}");
            var r = new Rig(store);
            r.Title(("landing", "continue"));
            Assert.IsTrue((bool)r.Host.Opens.First(o => o.screen == "landing").options["hasSave"]);
            Assert.AreEqual("prologue#1", r.Played[0]);
            Assert.IsTrue(r.Host.Cards.Any(c => c.Title == "체험 모드"));

            var r2 = new Rig(new MemoryStoryStore());
            r2.Store.Set(StoryDirector.SaveKey, "not json");
            r2.Title(("landing", "continue"));
            Assert.IsFalse((bool)r2.Host.Opens.First(o => o.screen == "landing").options["hasSave"]);
            Assert.AreEqual("prologue#1", r2.Played[0]);
        }

        [Test]
        public void ReadSnapKeepsOnlyKnownKeysAndSaneValues()
        {
            var raw = JObject.Parse(@"{ levelStates: { town: { strike: 'hit' }, moon: { a: 1 }, mart: 3, shelter: {} }, power: 'yes', outdoorEnv: 'dusk', indoorEnv: 'disco',
                lightSource: 'candle', trail: [ { x: 1, z: 2 }, { x: 'a', z: 1 }, { z: 3 }, 5 ], seoyeonAsk: 'maybe', extra: 1 }");
            var s = StoryDirector.ReadSnap(raw);
            TestSupport.AssertJson(s, "{ levelStates: { town: { strike: 'hit' }, shelter: {} }, outdoorEnv: 'dusk', trail: [ { x: 1, z: 2 } ] }");
            var trail = new JArray(Enumerable.Range(0, 2100).Select(i => new JObject { ["x"] = i, ["z"] = 0 }));
            var s2 = StoryDirector.ReadSnap(new JObject { ["trail"] = trail, ["power"] = false, ["lightSource"] = "phone", ["seoyeonAsk"] = "yes" });
            Assert.AreEqual(2000, ((JArray)s2["trail"]).Count);
            Assert.AreEqual(100, (int)s2["trail"][0]["x"], "the last 2000");
            Assert.IsFalse((bool)s2["power"]); Assert.AreEqual("phone", (string)s2["lightSource"]); Assert.AreEqual("yes", (string)s2["seoyeonAsk"]);
            TestSupport.AssertJson(StoryDirector.ReadSnap(null), "{ levelStates: {} }");
            TestSupport.AssertJson(StoryDirector.ReadSnap("x"), "{ levelStates: {} }");
        }

        [Test]
        public void AnOldSaveWithoutALightSourceTakesItFromTheGame()
        {
            var r = new Rig();
            r.Host.Run(async () =>
            {
                r.D.G = TestSupport.Rules.CreateGame(7);
                GameRules.RecordEvent(r.D.G, "blackout", new JObject());
                r.D.ResetRun(new JObject { ["power"] = false });
                Assert.AreEqual("phone", r.D.Ctx.LightSource, "battery left, no checked flashlight");
                Assert.IsFalse(r.D.Ctx.Power);
                await Task.Yield();
            });
        }

        // ------------------------------------------------------------------ title screens

        [Test]
        public void TitleScreensOpenWithTheOriginalOptionsAndReturnToTheTitle()
        {
            var r = new Rig();
            Assert.AreEqual("gallery", r.Title(("landing", "gallery")));
            var g = r.Host.Opens.Last().options;
            Assert.AreEqual(11, ((JArray)g["view"]).Count);
            Assert.AreEqual("records", r.Title(("landing", "records")));
            TestSupport.AssertJson(r.Host.Opens.Last().options, "{ done: {}, best: {}, runs: [] }");
            Assert.AreEqual("about", r.Title(("landing", "about")));
            Assert.AreEqual(FakeStoryHost.LoadFacts().Sources.Count, ((JArray)r.Host.Opens.Last().options["sources"]).Count);
            Assert.AreEqual("lounge", r.Title(("landing", "lounge")));
            var name = (string)r.Host.Opens.Last().options["name"];
            Assert.IsTrue(Lounge.IsName(name));
            Assert.AreEqual(name, r.Store.Get(StoryDirector.LoungeNameKey));
            Assert.AreEqual(JTokenType.Null, r.Host.Opens.Last().options["best"].Type);
            Assert.AreEqual("lounge", r.Title(("landing", "lounge")));
            Assert.AreEqual(name, (string)r.Host.Opens.Last().options["name"], "the name is kept");
            Assert.AreEqual("settings", r.Title(("landing", "settings"), ("settings", JObject.Parse("{ view: 'first', profile: 'wheelchair', memoryCue: false }"))));
            Assert.AreEqual("first", r.Host.View);
            Assert.AreEqual("wheelchair", r.D.StorySettings.Profile);
            Assert.IsFalse(r.D.StorySettings.MemoryCue);
            Assert.AreEqual("third", (string)r.Host.Opens.Last(o => o.screen == "settings").options["view"], "the screen gets the current settings");
            Assert.IsEmpty(r.Played);
        }

        [Test]
        public void LandingCountsSeenEndingsAndChallengesAfterARecordedRun()
        {
            var r = new Rig();
            r.Body["ending"] = async (c, n) =>
            {
                await c.RecordRun();
                return await c.Modal(() => c.Ui.Open("ending", new JObject()));
            };
            foreach (var id in Challenges.Timed) r.Body[id] = async (c, n) => { await c.Wait(1); return null; }; // a time > 0 counts
            r.Host.AnswerOpen("ending", "restart");
            Assert.AreEqual("restart", r.Title(("landing", "new")));
            Assert.That(r.LastG.Events.Where(e => e.Type == "chapter_time").All(e => (double)e.Details["ms"] >= 1000));
            Assert.Contains("run_recorded", r.LastG.Events.Select(e => e.Type).ToList());
            Assert.IsNotNull(r.Store.Get(StoryDirector.GalleryKey));
            Assert.IsNotNull(r.Store.Get(StoryDirector.RecordsKey));
            var rec = Challenges.ReadRecords(r.Store.Get(StoryDirector.RecordsKey));
            Assert.AreEqual(1, rec.Runs.Count);
            Assert.AreEqual("2026-11-16", rec.Runs[0].At);
            r.Title(("landing", "lounge"));
            var landing = r.Host.Opens.Last(o => o.screen == "landing").options;
            Assert.AreEqual(1, (int)landing["endings"]);
            Assert.AreEqual(rec.Done.Count, (int)landing["challenges"]);
            Assert.AreEqual(rec.Runs[0].Ending, (string)r.Host.Opens.Last().options["best"]["ending"]);
        }

        // ------------------------------------------------------------------ time-lapse, boom

        [Test]
        public void TheTimeLapseReplacesTheSkipCardAndTheCardStandsInWhenItFails()
        {
            int ok = 0;
            var r = new Rig(timelapse: (c, from, to, text) => { if (from == "bad") throw new Exception("no town"); ok++; return Task.CompletedTask; });
            r.Body["prologue"] = async (c, n) =>
            {
                await c.Skip("D-3", "D-2", new[] { "a" });
                await c.Skip("bad", "D-1", new[] { "b" });
                c.Boom(3, 4, 2);
                return null;
            };
            r.Title(("landing", "new"));
            Assert.AreEqual(1, ok);
            Assert.AreEqual(1, r.Host.TimeSkips.Count);
            Assert.AreEqual("bad", r.Host.TimeSkips[0].from);
            Assert.Contains("danger.boom 3 4 2", r.Calls);
        }

        // ------------------------------------------------------------------ keys while roaming

        Rig Roaming(Func<Rig, Task> script)
        {
            var r = new Rig();
            r.Body["prologue"] = async (c, n) =>
            {
                await c.Goto("town", "home");
                var stop = false;
                var roam = c.Roam(new RoamOptions { Doors = false, Tick = _ => stop ? new JValue("done") : null });
                await r.Host.Settle();
                await script(r);
                stop = true;
                await roam;
                throw new InvalidOperationException("stop here");
            };
            r.Host.AnswerOpen("landing", "new");
            Assert.Throws<InvalidOperationException>(() => r.Host.Run(() => r.D.TitleOnce()));
            return r;
        }

        [Test]
        public void MenuKeysOpenScreensOnlyWhileRoaming()
        {
            Rig rig = null;
            rig = Roaming(async r =>
            {
                r.Host.PressKey("Tab"); await r.Host.Settle();
                r.Host.PressKey("KeyI"); await r.Host.Settle();
                r.Host.PressKey("KeyM"); await r.Host.Settle();
                r.Host.PressKey("KeyH"); await r.Host.Settle();
                r.Host.PressKey("KeyB"); await r.Host.Settle();
                Assert.IsTrue(r.Host.ControlsEnabled, "controls back after each modal");
            });
            var screens = rig.Host.Opens.Select(o => o.screen).ToList();
            CollectionAssert.IsSubsetOf(new[] { "notebook", "inventory", "routemap" }, screens);
            var inv = rig.Host.Opens.Single(o => o.screen == "inventory").options;
            Assert.AreEqual(8, (double)inv["limit"]["weightKg"]);
            Assert.AreEqual("생수 2L", (string)inv["catalog"]["water_2l"]["name"]);
            Assert.AreEqual(50, (int)inv["cells"]["total"]);
            var map = rig.Host.Opens.Single(o => o.screen == "routemap").options;
            Assert.AreEqual("view", (string)map["mode"]); Assert.AreEqual("shelter", (string)map["to"]);
            Assert.AreEqual(rig.D.NearestNode(), (string)map["from"]);
            Assert.AreEqual("town", rig.Host.LevelId);
            Assert.AreEqual(rig.Host.PlayerX, (double)map["playerAt"]["x"]);
            Assert.AreEqual(0, ((JArray)map["closures"]).Count);
            Assert.IsNotNull(rig.Host.Cards.SingleOrDefault(c => c.Title == "조작법"));
            Assert.AreEqual(18, rig.Host.Cards.Single(c => c.Title == "조작법").Body.Count);
            Assert.Contains("echo.chalk", rig.Calls);

            // not roaming: nothing opens
            var r2 = new Rig();
            r2.Body["prologue"] = async (c, n) => { r2.Host.PressKey("Tab"); await r2.Host.Settle(); return null; };
            r2.Title(("landing", "new"));
            Assert.IsFalse(r2.Host.Opens.Any(o => o.screen == "notebook"));
        }

        [Test]
        public void TheMapShowsClosuresOnlyOnceKnown()
        {
            var rig = Roaming(async r =>
            {
                r.D.Ctx.SetLevelState("town", "closures", new JArray("sijang"));
                r.Host.PressKey("KeyM"); await r.Host.Settle();
                r.D.G.SetKnow("closureHeard", true);
                r.Host.PressKey("KeyM"); await r.Host.Settle();
                r.D.G.Chapter = "evac";
                r.Host.PressKey("KeyM"); await r.Host.Settle();
            });
            var maps = rig.Host.Opens.Where(o => o.screen == "routemap").Select(o => ((JArray)o.options["closures"]).Count).ToList();
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, maps);
        }

        [Test]
        public void ThePhoneShowsRecentOfficialTextsAndOpensTheRadio()
        {
            var rig = Roaming(async r =>
            {
                for (int i = 0; i < 6; i++) GameRules.Note(r.D.G, "공지 " + i, "official", "town");
                GameRules.Note(r.D.G, "소문", "rumor", "town");
                r.Host.AnswerOpen("phone", "radio");
                r.Host.AnswerOpen("radio", JObject.Parse("{ official: true, noted: [] }"));
                r.Host.PressKey("KeyP"); await r.Host.Settle();
                r.D.G.Phone.Battery = 0;
                r.Host.PressKey("KeyP"); await r.Host.Settle();
            });
            var phone = rig.Host.Opens.Single(o => o.screen == "phone").options;
            CollectionAssert.AreEqual(new[] { "공지 2", "공지 3", "공지 4", "공지 5" }, ((JArray)phone["messages"]).Where(m => (string)m["kind"] == "safety").Select(m => (string)m["text"]));
            Assert.AreEqual("radio", (string)phone["actions"][0]["id"]);
            var radio = rig.Host.Opens.Single(o => o.screen == "radio").options;
            Assert.AreEqual("phone", (string)radio["device"]);
            Assert.Contains("휴대폰이 꺼져 있다.", rig.Host.Toasts);
        }

        [Test]
        public void PauseSaveSettingsAndMenu()
        {
            var menu = 0;
            var rig = Roaming(async r =>
            {
                r.D.MenuRequested = () => menu++;
                r.Host.AnswerOpen("pause", "save");
                r.Host.PressKey("Escape"); await r.Host.Settle();
                r.Host.AnswerOpen("pause", "settings");
                r.Host.AnswerOpen("settings", JObject.Parse("{ minimap: false, view: 'first' }"));
                r.Host.PressKey("Escape"); await r.Host.Settle();
                r.Host.AnswerOpen("pause", "lounge");
                r.Host.PressKey("Escape"); await r.Host.Settle();
                r.Host.AnswerOpen("pause", "menu");
                r.Host.PressKey("Escape"); await r.Host.Settle();
            });
            Assert.Contains("마지막 체크포인트를 저장했습니다.", rig.Host.Toasts);
            Assert.AreEqual(1, menu);
            Assert.IsFalse((bool)rig.D.Settings["minimap"]);
            Assert.AreEqual("first", rig.Host.View);
            Assert.IsTrue(rig.Host.Opens.Any(o => o.screen == "lounge"));
        }

        [Test]
        public void FlashlightKeyUsesTheLightInHandAndThePhoneRunsDown()
        {
            var rig = Roaming(async r =>
            {
                r.Host.PressKey("KeyF"); await r.Host.Settle();
                Assert.Contains("켤 수 있는 불빛이 없다.", r.Host.Toasts);
                Assert.IsFalse(r.D.FlashOn);
                r.D.Ctx.LightSource = "phone";
                r.D.G.Phone.Battery = 2;
                r.Host.PressKey("KeyF"); await r.Host.Settle();
                Assert.IsTrue(r.D.FlashOn); Assert.IsTrue(r.Host.Flashlight);
                r.Host.RunFrames(20 * 30 + 5);
                Assert.AreEqual(1, r.D.G.Phone.Battery);
                r.Host.RunFrames(20 * 30 + 5);
                Assert.AreEqual(0, r.D.G.Phone.Battery);
                Assert.IsFalse(r.D.FlashOn); Assert.IsFalse(r.Host.Flashlight);
                Assert.Contains("휴대폰이 꺼졌다.", r.Host.Toasts);
                r.Host.PressKey("KeyV"); await r.Host.Settle();
                Assert.AreEqual("first", r.Host.View);
            });
            Assert.AreEqual("first", (string)JObject.Parse(rig.Store.Get(StoryDirector.SettingsKey))["view"]);
        }

        [Test]
        public void AFlashlightRunsDownOnePercentPerLitMinuteAndTakesASpareBattery()
        {
            Roaming(async r =>
            {
                var g = r.D.G;
                g.Items["flashlight"] = new ItemEntry(1);
                g.Items["batteries_aa"] = new ItemEntry(1);
                Assert.IsTrue(TestSupport.Rules.Check(g, "flashlight").Ok);
                g.Items["flashlight"].Charge = 1;
                r.D.Ctx.LightSource = "flashlight";
                g.Items["batteries_aa"] = new ItemEntry(1);
                r.Host.PressKey("KeyF"); await r.Host.Settle();
                Assert.IsTrue(r.D.FlashOn);
                r.Host.RunFrames(60 * 30 + 3);
                Assert.IsFalse(r.D.FlashOn, "charge 1 -> 0 after a lit minute");
                Assert.AreEqual(0, g.Items["flashlight"].Charge);
                Assert.That(r.Host.Toasts.Last(), Does.Contain("남은 건전지가 있으니 F를 누르면 갈아 끼운다."));
                r.Host.PressKey("KeyF"); await r.Host.Settle();
                Assert.Contains("다 닳은 건전지를 빼고 손전등에 새 건전지를 넣었다.", r.Host.Toasts);
                Assert.IsTrue(r.D.FlashOn);
                Assert.AreEqual("flashlight", r.D.Ctx.LightSource);
            });
        }

        [Test]
        public void FirstAidKey()
        {
            Roaming(async r =>
            {
                var g = r.D.G;
                g.Items.Remove("first_aid");
                r.Host.PressKey("KeyX"); await r.Host.Settle();
                Assert.AreEqual("쓸 수 있는 구급함이 없다.", r.Host.Toasts.Last());
                g.Items["first_aid"] = new ItemEntry(0) { Bag = 1 };
                r.Host.PressKey("KeyX"); await r.Host.Settle();
                Assert.AreEqual(StoryDirector.FirstAidNotNeeded, r.Host.Toasts.Last());
                g.Stats.Health = 50;
                r.Host.PressKey("KeyX"); await r.Host.Settle();
                Assert.AreEqual(85, g.Stats.Health);
                Assert.AreEqual(0, g.Items["first_aid"].Bag); Assert.AreEqual(1, g.Items["first_aid"].Used);
                Assert.AreEqual("first_aid", g.Events.Last().Type);
            });
        }

        // ------------------------------------------------------------------ the body

        [Test]
        // Port change (competition upgrade, no injury scenes): the original broke a leg on a drop over 5.5 m.
        public void FallsCostHealth_AndAHardLandingBreaksNothing()
        {
            Roaming(async r =>
            {
                ExperienceProfile body = null;
                r.D.ApplyHostSettings = (s, p) => body = p;
                var g = r.D.G;
                r.Host.LandFrom(3); Assert.AreEqual(100, g.Stats.Health);
                r.Host.LandFrom(4); Assert.AreEqual(85, g.Stats.Health); Assert.AreEqual("fall", r.D.Ctx.DeathCause);
                r.Host.LandFrom(6);
                Assert.AreEqual(40, g.Stats.Health);
                Assert.That(g.Injury == null || g.Injury.Count == 0, "no injury");
                Assert.That(g.Events.Any(e => e.Type == "injury"), Is.False);
                Assert.That(body == null || body.Run, "running is not taken away");
                Assert.Contains(StoryDirector.HardLandingLine, r.Host.Toasts);
                Assert.That(r.Host.Toasts.Any(t => t.Contains("접질") || t.Contains("부러") || t.Contains("달릴 수 없")), Is.False, "no leg injury, nothing broken");
                r.Host.LandFrom(5.6); Assert.AreEqual(1, g.Stats.Health);
                await Task.Yield();
            });
        }

        [Test]
        public void DeafModeShowsLoudSoundsAndLowVisionSpeaks()
        {
            var spoken = new List<string>();
            Roaming(async r =>
            {
                r.D.Speak = spoken.Add;
                r.D.UpdateSettings(new JObject { ["profile"] = "deaf" });
                r.D.Ctx.Audio.Cue("boom");
                r.D.Ctx.Audio.Loop("siren", true);
                r.D.Ctx.Audio.Loop("siren", false);
                r.D.Ctx.Audio.Cue("door");
                CollectionAssert.AreEqual(new[] { "[소리] 쾅— 큰 폭발음. 바닥이 울린다.", "[소리] 사이렌이 울리기 시작했다." }, r.Host.Toasts.Where(t => t.StartsWith("[")).ToArray());
                r.D.UpdateSettings(new JObject { ["profile"] = "lowvision" });
                await r.D.Ctx.Caption("말");
                r.Host.SetNearest(new StoryPoint { Id = "p", Label = "게시판" });
                Assert.AreEqual(0.8, r.D.Ctx.ProfileSpeed);
            });
            CollectionAssert.AreEqual(new[] { "말", "게시판" }, spoken);
        }

        [Test]
        public void AWheelchairIsCarriedAcrossStairsWithHelp()
        {
            Roaming(async r =>
            {
                r.D.UpdateSettings(new JObject { ["profile"] = "wheelchair" });
                var zone = JObject.Parse("{ x0: 0, x1: 4, z0: -1, z1: 1, ramp: { axis: 'x', y0: 0, y1: 3 } }");
                var flat = JObject.Parse("{ x0: 10, x1: 14, z0: -1, z1: 1, ramp: { axis: 'x', y0: 0, y1: 0.2 } }");
                var min0 = r.D.G.Minute;
                r.Host.Answer("no");
                r.Host.Block(new JObject { ["kind"] = "stairs", ["x"] = -0.2, ["z"] = 0, ["toX"] = 0.1, ["toZ"] = 0, ["walk"] = new JArray(zone, flat) });
                await r.Host.Settle();
                Assert.AreEqual(min0, r.D.G.Minute);
                r.Host.Block(new JObject { ["kind"] = "stairs", ["x"] = -0.2, ["z"] = 0, ["toX"] = 0.1, ["toZ"] = 0, ["walk"] = new JArray(zone) });
                await r.Host.Settle();
                Assert.AreEqual(4.45, r.Host.PlayerX, 1e-9); Assert.AreEqual(3, r.Host.PlayerY); Assert.AreEqual(0, r.Host.PlayerZ);
                Assert.AreEqual(min0 + 2, r.D.G.Minute);
                Assert.AreEqual("이웃 두 사람이 휠체어를 함께 들어 계단을 옮겨 주었다.", r.Host.Toasts.Last());
                var talks = r.Host.Talks.Count;
                r.Host.Block(new JObject { ["x"] = 4.2, ["z"] = 0, ["toX"] = 3.9, ["toZ"] = 0, ["zone"] = zone });
                await r.Host.Settle();
                Assert.AreEqual(talks, r.Host.Talks.Count, "the same helper, no question");
                Assert.AreEqual(-0.45, r.Host.PlayerX, 1e-9); Assert.AreEqual(0, r.Host.PlayerY);
                Assert.AreEqual("도움을 받아 다음 계단도 옮겼다.", r.Host.Toasts.Last());
                r.Host.Block(new JObject { ["toX"] = 12, ["toZ"] = 0, ["zone"] = flat });
                await r.Host.Settle();
                Assert.AreEqual(-0.45, r.Host.PlayerX, 1e-9, "a gentle ramp is not stairs");
            });
        }

        [Test]
        public void TheGuardianFollowsBehindAndTheCompassTurns()
        {
            Roaming(async r =>
            {
                r.D.UpdateSettings(new JObject { ["profile"] = "guardian" });
                r.Host.MoveTo(0, 0); r.Host.PlayerYaw = 0;
                r.Host.RunFrames(3);
                var buddy = r.Host.Level(r.Host.LevelId).Spawned.Single();
                Assert.AreEqual(0.6, buddy.X, 1e-9); Assert.AreEqual(1.3, buddy.Z, 1e-9);
                r.Host.MoveTo(0, -3); r.Host.RunFrames(90);
                Assert.AreEqual(-1.7, buddy.Z, 0.26);
                Assert.AreEqual("idle", buddy.Anim);
                Assert.AreEqual(0.72, r.D.Ctx.ProfileSpeed);
                r.Host.PlayerYaw = Math.PI / 2; r.Host.RunFrames(3);
                Assert.AreEqual(-90, r.Host.Compass.Value, 1e-9);
                Assert.AreEqual(r.Host.LevelId, (string)r.Host.Map["level"]);
                r.D.UpdateSettings(new JObject { ["profile"] = "none", ["minimap"] = false });
                r.Host.RunFrames(3);
                Assert.IsFalse(buddy.Visible);
                Assert.IsNull(r.Host.Map);
                await Task.Yield();
            });
        }

        [Test]
        public void TheGuardianEasesToTheGroundAndWalksAtItsPace()
        {
            Roaming(async r =>
            {
                r.D.UpdateSettings(new JObject { ["profile"] = "guardian" });
                r.Host.MoveTo(0, 0, 0); r.Host.PlayerYaw = 0;
                r.Host.RunFrames(3);
                var buddy = r.Host.Level(r.Host.LevelId).Spawned.Single();
                Assert.AreEqual(0, buddy.Y, 1e-9);
                Assert.AreEqual("idle", buddy.Anim); Assert.AreEqual(0, buddy.AnimSpeed);
                // Up a step: no snap to the player's height; it eases min(1, dt*12) of the way per frame and walks at min(d*2.4, 4.2).
                r.D.GroundAt = (x, z, feetY) => null;
                r.Host.MoveTo(0, -3, 2);
                r.Host.RunFrame(0.05);
                Assert.AreEqual(1.2, buddy.Y, 1e-9);
                Assert.AreEqual("walk", buddy.Anim); Assert.AreEqual(4.2, buddy.AnimSpeed.Value, 1e-9);
                r.Host.RunFrame(0.05);
                Assert.AreEqual(1.68, buddy.Y, 1e-9);
                r.Host.RunFrames(120, 0.05);
                Assert.AreEqual(2, buddy.Y, 1e-6);
                Assert.AreEqual("idle", buddy.Anim); Assert.AreEqual(0, buddy.AnimSpeed);
                // The ground under it wins over the player's height (groundAt(L.walk, ...) ?? p.y).
                r.D.GroundAt = (x, z, feetY) => 2.5;
                r.Host.RunFrames(60, 0.05);
                Assert.AreEqual(2.5, buddy.Y, 1e-6);
                await Task.Yield();
            });
        }

        [Test]
        public void TownGroundFollowsTheOriginalGroundAt()
        {
            Assert.AreEqual(0, StoryDirector.TownGround(0, 0, 0));
            Assert.IsNull(StoryDirector.TownGround(0, 0, 5), "more than 1.5 below the feet is not ground");
            Assert.IsNull(StoryDirector.TownGround(0, 0, -1), "more than 0.45 above the feet is not ground");
        }

        [Test]
        public void GoingToAnotherLevelDropsWhatIsInHandFirst()
        {
            var order = new List<string>();
            Roaming(async r =>
            {
                r.D.DropHeld = () => order.Add("drop@" + r.Host.LevelId);
                await r.D.Ctx.Goto("apartment", "home");
                Assert.AreEqual("apartment", r.Host.LevelId);
            });
            CollectionAssert.AreEqual(new[] { "drop@town" }, order);
        }

        [Test]
        public void TheStairHelpReadsTheLevelWalkWhenTheEventCarriesNone()
        {
            Roaming(async r =>
            {
                r.D.UpdateSettings(new JObject { ["profile"] = "wheelchair" });
                var zone = JObject.Parse("{ x0: 0, x1: 4, z0: -1, z1: 1, ramp: { axis: 'x', y0: 0, y1: 3 } }");
                r.D.LevelWalk = () => new[] { zone };
                r.Host.Answer("help");
                r.Host.Block(new JObject { ["kind"] = "stairs", ["x"] = -0.2, ["z"] = 0, ["toX"] = 0.1, ["toZ"] = 0 });
                await r.Host.Settle();
                Assert.AreEqual("계단", r.Host.Talks.Last().Speaker);
                Assert.AreEqual(4.45, r.Host.PlayerX, 1e-9); Assert.AreEqual(3, r.Host.PlayerY);
            });
        }

        [Test]
        public void QualityStepDownIsKeptAndToldOnce()
        {
            var r = new Rig();
            r.D.OnQualityChange("low", "낮음", true);
            r.D.OnQualityChange("medium", null, false);
            Assert.AreEqual("medium", (string)JObject.Parse(r.Store.Get(StoryDirector.SettingsKey))["quality"]);
            CollectionAssert.AreEqual(new[] { "화면이 자주 멈춰서 그래픽을 '낮음'(으)로 낮췄어요. 설정에서 다시 바꿀 수 있어요." }, r.Host.Toasts);
        }

        [Test]
        public void AsChapterTakesTheResultOfAnyTask()
        {
            var h = new FakeStoryHost();
            h.Run(async () =>
            {
                Assert.IsNull(await StoryDirector.AsChapter(Task.CompletedTask));
                Assert.IsNull(await StoryDirector.AsChapter(Run()));
                Assert.AreEqual("restart", (string)await StoryDirector.AsChapter(Task.FromResult<JToken>("restart")));
                Assert.AreEqual("menu", (string)await StoryDirector.AsChapter(Task.FromResult("menu")));
                Assert.IsNull(await StoryDirector.AsChapter(Task.FromResult<string>(null)));
            });
            static async Task Run() => await Task.Yield();
        }

        [Test]
        public void TheDefaultPartsNameTheSevenChaptersInOrder()
        {
            var p = DirectorParts.Default();
            CollectionAssert.AreEqual(Ids, p.Chapters.Select(c => c.Id));
            Assert.IsNotNull(p.Opening); Assert.IsNotNull(p.Timelapse); Assert.IsNotNull(p.MakeCine); Assert.IsNotNull(p.MakeDanger); Assert.IsNotNull(p.MakeEcho);
        }
    }
}
