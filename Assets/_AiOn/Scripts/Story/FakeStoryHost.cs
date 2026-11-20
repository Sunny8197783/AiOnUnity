// FakeStoryHost: an in-memory IStoryEngine / IStoryHud / IStoryUi / IStoryAudio / IStoryPersist for EditMode tests of
// the story. Test-only code, kept in Scripts/Story so every chapter's tests share it. No UnityEngine types.
//
// QUICK START
//   var host = new FakeStoryHost();                       // levels load from Data/Source/<level>-level.json on Enter
//   var ctx  = host.CreateContext(TestSupport.Rules);     // new game (seed 7) unless you pass one; host.G is the game
//   host.Answer("go", "wait").AnswerCard("ok");          // scripted answers, consumed in order
//   host.QueuePress("apartment:homeBag");                 // player steps, run when the story waits on the player
//   host.Run(() => Prologue.Run(ctx));                    // runs the story to the end on one thread, pumping frames
//   Assert.That(host.EventTypes(), Is.EqualTo(new[] { ... }));
//
// ASYNC MODEL
//   Run() installs a single-threaded SynchronizationContext, starts the story and pumps it: queued continuations
//   first; when nothing is queued it releases Settle() waiters; when the story still waits (on the player, i.e. a
//   roam or a timer) it runs the next ready queued player step, else advances one frame (Dt seconds) — up to
//   MaxIdleFrames, then throws FakeHostException with the recent call log ("stuck"). Never call .Result/.Wait or use
//   ConfigureAwait(false) in story code.
//   Inside Run, a test may drive things by hand too:  var done = ctx.Roam(...); await host.PressAsync("x"); ... await done;
//   (PressAsync = Press + Settle: returns once the handler and everything it queued has run.)
//
// ANSWERS (Talk with choices / Card with actions / Open)
//   1. AnswerWhen(match, id) rules, in the order added (once by default);
//   2. the FIFO queues: Answer(ids) for talks, AnswerCard(ids) for cards, AnswerOpen(screen, result) per screen;
//   3. Strict = true: anything else throws FakeHostException("unscripted ...");
//   4. otherwise the default policies: TalkPolicy (first enabled choice), CardPolicy (primary action, else the first),
//      OpenPolicy ({}).
//   A scripted id that is not offered, or is disabled, always throws. Answer(null) returns null (the original's undefined).
//   Talks without choices and cards without actions return null and consume nothing.
//
// RECORDS
//   Log (every call in order: toast, caption, objective, chapter, talk, card, open, timeSkip, enter, state, env, power,
//   ambience, cue, loop, checkpoint, save, fade, camera, shake, grade, view, flashlight, preset, step, answer...),
//   and typed views: Toasts, Captions, Talks, Cards, Opens, Objectives, TimeSkips, Checkpoints, Cues.
//   Last HUD values: Clock, Stats, Battery, BagKg, Tension, Stamina, Prompt. Level state: Level(id).State / State(key).
//   Warnings: a state key or value the level json does not list (kept, not thrown).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story.Testing
{
    /// <summary>A failure of the fake itself: an unscripted decision (strict), a bad scripted answer, a stuck story.</summary>
    public sealed class FakeHostException : Exception
    {
        public FakeHostException(string message) : base(message) { }
    }

    /// <summary>One recorded host call.</summary>
    public sealed class HostCall
    {
        public int Seq;
        /// <summary>toast, caption, objective, talk, card, open, enter, state, ...</summary>
        public string Kind;
        /// <summary>The main text (toast text, caption, objective title, talk speaker, card title, screen, level id...).</summary>
        public string Text;
        /// <summary>The call's argument object (TalkOptions, CardOptions, JObject...) when there is one.</summary>
        public object Data;
        public override string ToString() => $"#{Seq} {Kind}: {Text}";
    }

    /// <summary>A person in a fake level (json npcs or SpawnPerson).</summary>
    public sealed class FakePerson : IStoryPerson
    {
        public string Name { get; }
        public string Preset;
        public bool Visible { get; set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double Yaw { get; private set; }
        public string Anim;
        public double? AnimSpeed;
        public readonly List<string> Clips = new();
        public readonly HashSet<string> Props = new();
        public double? LookX, LookZ;
        public readonly List<(double x, double z)> Walked = new();

        public FakePerson(string name, string preset, double x, double y, double z, double yaw, bool visible)
        { Name = name; Preset = preset; X = x; Y = y; Z = z; Yaw = yaw; Visible = visible; }

        public void SetPosition(double x, double y, double z, double? yaw = null) { X = x; Y = y; Z = z; if (yaw.HasValue) Yaw = yaw.Value; }
        public void SetAnim(string anim, double? speed = null) { Anim = anim; AnimSpeed = speed; }
        public void Play(string clip) => Clips.Add(clip);
        public void LookAt(double? x, double? z) { LookX = x; LookZ = z; }
        public void SetProp(string prop, bool on) { if (on) Props.Add(prop); else Props.Remove(prop); }
        /// <summary>Arrives at once at the last point of the path.</summary>
        public Task WalkTo(IReadOnlyList<(double x, double z)> path, double speed = 1.3)
        {
            if (path != null && path.Count > 0) { Walked.AddRange(path); X = path[^1].x; Z = path[^1].z; }
            return Task.CompletedTask;
        }
    }

    /// <summary>A level as the fake sees it: the json's points, spawns, anchors, accepted states and npcs, plus the live state.</summary>
    public sealed class FakeLevel
    {
        public string Id;
        public bool FromJson;
        public List<StoryPoint> Points = new();
        public Dictionary<string, (double x, double y, double z, double yaw)> Spawns = new();
        public Dictionary<string, (double x, double y, double z, double yaw)> Anchors = new();
        /// <summary>json states: key -> accepted values.</summary>
        public Dictionary<string, JArray> Accepts = new();
        /// <summary>Level-published defaults (the original's level.defaults); empty unless a test fills it.</summary>
        public Dictionary<string, JToken> Defaults = new();
        /// <summary>What ApplyLevelState set on this level (kept across enters, like the original's cached levels).</summary>
        public Dictionary<string, JToken> State = new();
        public Dictionary<string, FakePerson> Npcs = new();
        public List<FakePerson> Spawned = new();

        public StoryPoint Point(string id) => Points.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>A single-threaded SynchronizationContext: posted continuations wait in a queue the host drains.</summary>
    public sealed class FakeSyncContext : SynchronizationContext
    {
        readonly Queue<(SendOrPostCallback cb, object state)> _q = new();
        public override void Post(SendOrPostCallback d, object state) { lock (_q) _q.Enqueue((d, state)); }
        public override void Send(SendOrPostCallback d, object state) => d(state);
        public override SynchronizationContext CreateCopy() => this;
        public int Pending { get { lock (_q) return _q.Count; } }
        public bool RunOne()
        {
            (SendOrPostCallback cb, object state) item;
            lock (_q) { if (_q.Count == 0) return false; item = _q.Dequeue(); }
            item.cb(item.state);
            return true;
        }
    }

    public sealed class FakeStoryHost : IStoryEngine, IStoryHud, IStoryUi, IStoryAudio, IStoryPersist
    {
        // ================================================================== setup

        /// <summary>Folder with the level json files and facts.json. Null: see ResolveDataDir.</summary>
        public static string DataDir;

        /// <summary>
        /// DataDir if set; else $AION_ASSETS/Data/Source; else Assets/_AiOn/Data/Source under the working directory
        /// (Unity's EditMode tests); else the same under Desktop/AiOnUnityProject (the scratch .NET harnesses).
        /// </summary>
        public static string ResolveDataDir()
        {
            var tries = new List<string>();
            if (!string.IsNullOrEmpty(DataDir)) tries.Add(DataDir);
            var env = Environment.GetEnvironmentVariable("AION_ASSETS");
            if (!string.IsNullOrEmpty(env)) tries.Add(Path.Combine(env, "Data", "Source"));
            tries.Add(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "_AiOn", "Data", "Source"));
            tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AiOnUnityProject", "Assets", "_AiOn", "Data", "Source"));
            foreach (var t in tries) if (Directory.Exists(t)) return t;
            throw new FakeHostException("level data not found; set FakeStoryHost.DataDir or AION_ASSETS. Tried: " + string.Join("; ", tries));
        }

        static readonly Dictionary<string, Facts> FactsCache = new();
        /// <summary>facts.json from the data folder with facts-ext.json merged in when it is there, as the game reads them (cached per folder).</summary>
        public static Facts LoadFacts(string dataDir = null)
        {
            var dir = dataDir ?? ResolveDataDir();
            lock (FactsCache)
            {
                if (!FactsCache.TryGetValue(dir, out var f))
                {
                    var ext = Path.Combine(dir, "facts-ext.json");
                    FactsCache[dir] = f = Facts.Load(File.ReadAllText(Path.Combine(dir, "facts.json")), File.Exists(ext) ? File.ReadAllText(ext) : null);
                }
                return f;
            }
        }

        readonly string _dataDir;
        readonly FakeSyncContext _sync = new();
        readonly Dictionary<string, FakeLevel> _levels = new();

        public FakeStoryHost(string dataDir = null) { _dataDir = dataDir; }

        /// <summary>The context CreateContext made (null until then).</summary>
        public StoryContext Ctx { get; private set; }
        /// <summary>The game the context reads (StoryContext.Load replaces it).</summary>
        public GameState G { get; set; }
        public StorySettings Settings { get; private set; }

        /// <summary>A StoryContext wired to this host. g: a new game with seed 7 when null. facts: facts.json when null.</summary>
        public StoryContext CreateContext(GameRules rules, GameState g = null, Facts facts = null, StorySettings settings = null)
        {
            G = g ?? rules.CreateGame(7);
            Settings = settings ?? new StorySettings();
            Ctx = new StoryContext(rules, () => G, x => G = x, facts ?? LoadFacts(_dataDir), this, this, this, this, this, Settings);
            return Ctx;
        }

        // ================================================================== run / pump

        /// <summary>Seconds per frame for every frame the host pumps.</summary>
        public double Dt = 1 / 30.0;
        /// <summary>When the story waits and no player step is ready, advance a frame (else throw "stuck" at once).</summary>
        public bool AutoFrame = true;
        /// <summary>Frames without progress (no step run) before Run gives up with FakeHostException.</summary>
        public int MaxIdleFrames = 36000;
        /// <summary>Unscripted Talk-with-choices / Card-with-actions / Open throws instead of using the default policy.</summary>
        public bool Strict;
        /// <summary>Total frames pumped and game-time seconds they covered.</summary>
        public int FrameCount { get; private set; }
        public double Seconds { get; private set; }

        bool _running;
        readonly List<TaskCompletionSource<bool>> _settle = new();

        /// <summary>Runs the story to the end (rethrows what it threw, e.g. StoryFail).</summary>
        public void Run(Func<Task> story) => Run<bool>(async () => { await story(); return true; });

        public T Run<T>(Func<Task<T>> story)
        {
            if (_running) throw new FakeHostException("Run is not re-entrant");
            var prev = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_sync);
            _running = true;
            try
            {
                Task<T> t;
                try { t = story(); }
                catch (Exception e) { ExceptionDispatchInfo.Capture(e).Throw(); throw; }
                PumpUntil(t);
                return t.GetAwaiter().GetResult();
            }
            finally
            {
                _running = false;
                foreach (var s in _settle) s.TrySetCanceled();
                _settle.Clear();
                SynchronizationContext.SetSynchronizationContext(prev);
            }
        }

        /// <summary>Runs the story and returns the StoryFail it ends with (throws FakeHostException if it ends without one).</summary>
        public StoryFail RunExpectFail(Func<Task> story)
        {
            try { Run(story); }
            catch (StoryFail f) { return f; }
            throw new FakeHostException("expected a StoryFail, the story finished");
        }

        /// <summary>Inside Run: completes once nothing else is queued (the original tests' await tick()).</summary>
        public Task Settle()
        {
            if (!_running) throw new FakeHostException("Settle only works inside Run");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _settle.Add(tcs);
            return tcs.Task;
        }

        void PumpUntil(Task t)
        {
            int idle = 0;
            while (!t.IsCompleted)
            {
                if (_sync.RunOne()) continue;
                if (_settle.Count > 0)
                {
                    var ws = _settle.ToList(); _settle.Clear();
                    foreach (var w in ws) w.TrySetResult(true);
                    continue;
                }
                if (_steps.Count > 0)
                {
                    var s = _steps.Peek();
                    if (s.When == null || s.When())
                    {
                        _steps.Dequeue();
                        Record("step", s.Label);
                        s.Action(this);
                        idle = 0;
                        continue;
                    }
                }
                if (!AutoFrame) throw Stuck("the story waits and AutoFrame is off");
                if (++idle > MaxIdleFrames) throw Stuck($"no progress after {MaxIdleFrames} frames");
                RunFrame();
            }
        }

        FakeHostException Stuck(string why)
        {
            var next = _steps.Count > 0 ? $" next step: '{_steps.Peek().Label}' (not ready)." : " no player steps queued.";
            return new FakeHostException($"story stuck: {why}. level={LevelId ?? "-"} roaming={Ctx?.Roaming} controls={ControlsEnabled} " +
                $"player=({PlayerX:0.##}, {PlayerZ:0.##}) clock={(G != null ? GameRules.FormatClock(G) : "-")}.{next}\nrecent calls:\n  " + LogText(20));
        }

        /// <summary>One frame of dt seconds (default Dt), scaled by TimeScale like the engine's.</summary>
        public void RunFrame(double? dt = null)
        {
            var d = (dt ?? Dt) * TimeScale;
            FrameCount++; Seconds += d;
            Frame?.Invoke(d);
        }

        public void RunFrames(int n, double? dt = null) { for (int i = 0; i < n; i++) RunFrame(dt); }

        // ================================================================== player scripting

        sealed class Step { public string Label; public Func<bool> When; public Action<FakeStoryHost> Action; }
        readonly Queue<Step> _steps = new();
        /// <summary>Player steps not run yet.</summary>
        public int PendingSteps => _steps.Count;

        /// <summary>A step run when the story waits on the player and <paramref name="when"/> (if any) holds; frames pass until it does.</summary>
        public FakeStoryHost Queue(Action<FakeStoryHost> step, Func<bool> when = null, string label = "step")
        {
            _steps.Enqueue(new Step { Label = label, When = when, Action = step });
            return this;
        }

        (string level, string id) Split(string pointId)
        {
            int i = pointId.IndexOf(':');
            return i < 0 ? (null, pointId) : (pointId.Substring(0, i), pointId.Substring(i + 1));
        }

        bool RoamReady(string level) => Ctx != null && Ctx.Roaming && ControlsEnabled && (level == null || LevelId == level);

        /// <summary>Press E on a point ("id" or "level:id") once a roam runs (in that level).</summary>
        public FakeStoryHost QueuePress(string pointId)
        {
            var (lv, id) = Split(pointId);
            return Queue(h => h.Press(id), () => RoamReady(lv), "press " + pointId);
        }

        /// <summary>Walk onto a point ("id" or "level:id") once a roam runs (walk-through doors fire after a frame).</summary>
        public FakeStoryHost QueueWalk(string pointId)
        {
            var (lv, id) = Split(pointId);
            return Queue(h => h.WalkTo(id), () => RoamReady(lv), "walk " + pointId);
        }

        /// <summary>Several presses in order.</summary>
        public FakeStoryHost QueuePresses(params string[] pointIds) { foreach (var p in pointIds) QueuePress(p); return this; }

        /// <summary>A point of the current level by id (throws if the level has none).</summary>
        public StoryPoint Point(string id)
        {
            var p = Current?.Point(id);
            if (p == null) throw new FakeHostException($"no point '{id}' in level {LevelId ?? "-"} (points: {string.Join(", ", Points.Select(x => x.Id))})");
            return p;
        }

        /// <summary>Fires Interact (E) on a point of the current level.</summary>
        public void Press(string pointId) => Press(Point(pointId));

        /// <summary>Fires Interact on any point, also one made up by the test.</summary>
        public void Press(StoryPoint point)
        {
            Record("press", point?.Id, point);
            Interact?.Invoke(point);
        }

        /// <summary>Inside Run: Press, then Settle.</summary>
        public Task PressAsync(string pointId) { Press(pointId); return Settle(); }
        public Task PressAsync(StoryPoint point) { Press(point); return Settle(); }

        /// <summary>Stands on the point (and reports it as nearest) without a frame.</summary>
        public void MoveTo(double x, double z, double? y = null)
        {
            PlayerX = x; PlayerZ = z; if (y.HasValue) PlayerY = y.Value;
        }

        /// <summary>Walks onto a point: away from it first (so a door arms), onto it, a frame each, then Nearest.</summary>
        public void WalkTo(string pointId)
        {
            var p = Point(pointId);
            Record("walk", pointId);
            MoveTo(p.X + 2, p.Z); RunFrame();
            MoveTo(p.X, p.Z, p.Y); RunFrame();
            Nearest?.Invoke(p);
        }

        public void PressKey(string key) { Record("key", key); Key?.Invoke(key); }
        public void LandFrom(double metres) { Record("land", metres.ToString(System.Globalization.CultureInfo.InvariantCulture)); Land?.Invoke(metres); }
        public void Block(JObject what) { Record("blocked", what?.ToString(Newtonsoft.Json.Formatting.None), what); Blocked?.Invoke(what); }
        public void SetNearest(StoryPoint p) => Nearest?.Invoke(p);

        // ================================================================== answers

        readonly Queue<string> _talkAnswers = new();
        readonly Queue<string> _cardAnswers = new();
        readonly Dictionary<string, Queue<JToken>> _openAnswers = new();
        readonly List<(Func<TalkOptions, bool> match, string id, bool once)> _talkRules = new();

        /// <summary>Default for unscripted talks with choices (non-strict): the first enabled choice.</summary>
        public Func<TalkOptions, string> TalkPolicy = o => o.Choices.FirstOrDefault(c => !c.Disabled)?.Id;
        /// <summary>Default for unscripted cards with actions: the primary action, else the first.</summary>
        public Func<CardOptions, string> CardPolicy = o => (o.Actions.FirstOrDefault(a => a.Primary) ?? o.Actions[0]).Id;
        /// <summary>Default for unscripted screens: {}.</summary>
        public Func<string, JObject, JToken> OpenPolicy = (screen, opts) => new JObject();

        /// <summary>Talk answers (choice ids), in order. Only talks that offer choices take one.</summary>
        public FakeStoryHost Answer(params string[] ids) { foreach (var id in ids ?? new string[] { null }) _talkAnswers.Enqueue(id); return this; }
        /// <summary>Answers a matching talk (checked before the queue); once = removed after first use.</summary>
        public FakeStoryHost AnswerWhen(Func<TalkOptions, bool> match, string id, bool once = true) { _talkRules.Add((match, id, once)); return this; }
        /// <summary>Answers talks by this speaker (once by default).</summary>
        public FakeStoryHost AnswerSpeaker(string speaker, string id, bool once = true) => AnswerWhen(o => o.Speaker == speaker, id, once);
        /// <summary>Card answers (action ids), in order. Only cards with actions take one.</summary>
        public FakeStoryHost AnswerCard(params string[] ids) { foreach (var id in ids ?? new string[] { null }) _cardAnswers.Enqueue(id); return this; }
        /// <summary>The result the next Open(screen) returns.</summary>
        public FakeStoryHost AnswerOpen(string screen, JToken result)
        {
            if (!_openAnswers.TryGetValue(screen, out var q)) _openAnswers[screen] = q = new Queue<JToken>();
            q.Enqueue(result);
            return this;
        }
        public int PendingTalkAnswers => _talkAnswers.Count;
        public int PendingCardAnswers => _cardAnswers.Count;
        public int PendingOpenAnswers => _openAnswers.Values.Sum(q => q.Count);

        // ================================================================== records

        public readonly List<HostCall> Log = new();
        public readonly List<string> Warnings = new();
        int _seq;

        HostCall Record(string kind, string text, object data = null)
        {
            var c = new HostCall { Seq = ++_seq, Kind = kind, Text = text, Data = data };
            Log.Add(c);
            return c;
        }

        public IEnumerable<HostCall> Calls(string kind) => Log.Where(c => c.Kind == kind);
        public List<string> Toasts => Calls("toast").Select(c => c.Text).ToList();
        public List<string> Captions => Calls("caption").Select(c => c.Text).ToList();
        public List<TalkOptions> Talks => Calls("talk").Select(c => (TalkOptions)c.Data).ToList();
        public List<CardOptions> Cards => Calls("card").Select(c => (CardOptions)c.Data).ToList();
        public List<(string screen, JObject options)> Opens => Calls("open").Select(c => (c.Text, (JObject)c.Data)).ToList();
        /// <summary>(title, body, steps) of every SetObjective.</summary>
        public List<(string title, string body, IReadOnlyList<ObjectiveStep> steps)> Objectives =>
            Calls("objective").Select(c => ((string, string, IReadOnlyList<ObjectiveStep>))c.Data).ToList();
        public List<(string from, string to, IReadOnlyList<string> text)> TimeSkips =>
            Calls("timeSkip").Select(c => ((string, string, IReadOnlyList<string>))c.Data).ToList();
        public List<string> Checkpoints => Calls("checkpoint").Select(c => c.Text).ToList();
        public List<string> Cues => Calls("cue").Select(c => c.Text).ToList();
        /// <summary>G.Events types in order.</summary>
        public List<string> EventTypes() => G.Events.Select(e => e.Type).ToList();
        public string LogText(int last = 40) => string.Join("\n  ", Log.Skip(Math.Max(0, Log.Count - last)));

        // ================================================================== IStoryEngine

        public FakeLevel Current { get; private set; }
        public string LevelId => Current?.Id;
        public IReadOnlyList<StoryPoint> Points => Current?.Points ?? (IReadOnlyList<StoryPoint>)Array.Empty<StoryPoint>();

        /// <summary>The level (loaded from json on first use, or empty when there is no json), cached like the original.</summary>
        public FakeLevel Level(string id)
        {
            if (_levels.TryGetValue(id, out var l)) return l;
            l = LoadLevel(id);
            _levels[id] = l;
            return l;
        }

        /// <summary>The current level's state value for key (null when never set).</summary>
        public JToken State(string key) => Current != null && Current.State.TryGetValue(key, out var v) ? v : null;

        FakeLevel LoadLevel(string id)
        {
            var l = new FakeLevel { Id = id };
            string path = null;
            try { path = Path.Combine(_dataDir ?? ResolveDataDir(), id + "-level.json"); } catch (FakeHostException) { }
            if (path == null || !File.Exists(path)) return l;
            l.FromJson = true;
            var j = JObject.Parse(File.ReadAllText(path));
            double N(JToken t, double d = 0) => t == null || t.Type == JTokenType.Null ? d : (double)t;
            foreach (JObject p in (j["points"] as JArray) ?? new JArray())
            {
                var desc = p["desc"];
                l.Points.Add(new StoryPoint
                {
                    Id = (string)p["id"], Label = (string)p["label"], Kind = (string)p["kind"],
                    X = N(p["x"]), Y = N(p["y"]), Z = N(p["z"]), R = N(p["r"]), Reach = N(p["reach"]), Host = (string)p["host"],
                    Enabled = p["enabled"] == null || (bool)p["enabled"],
                    Desc = desc is JArray da ? string.Join("\n", da.Select(x => (string)x)) : (string)desc,
                });
            }
            foreach (var (src, dst) in new[] { ("spawns", l.Spawns), ("anchors", l.Anchors) })
                if (j[src] is JObject o)
                    foreach (var kv in o.Properties())
                        dst[kv.Name] = (N(kv.Value["x"]), N(kv.Value["y"]), N(kv.Value["z"]), N(kv.Value["yaw"]));
            if (j["states"] is JObject st) foreach (var kv in st.Properties()) if (kv.Value is JArray a) l.Accepts[kv.Name] = a;
            if (j["defaults"] is JObject df) foreach (var kv in df.Properties()) l.Defaults[kv.Name] = kv.Value;
            if (j["npcs"] is JObject npcs)
                foreach (var kv in npcs.Properties())
                    l.Npcs[kv.Name] = new FakePerson(kv.Name, (string)kv.Value["preset"], N(kv.Value["x"]), N(kv.Value["y"]), N(kv.Value["z"]), N(kv.Value["yaw"]),
                        kv.Value["visible"] == null || (bool)kv.Value["visible"]);
            return l;
        }

        public bool TryGetSpawn(string spawn, out double x, out double y, out double z, out double yaw)
        {
            x = y = z = yaw = 0;
            if (Current == null || spawn == null) return false;
            if (!Current.Spawns.TryGetValue(spawn, out var s) && !Current.Anchors.TryGetValue(spawn, out s)) return false;
            (x, y, z, yaw) = s;
            return true;
        }

        /// <summary>Switches the level (LevelId, Points, Npc) and puts the player on the spawn, then runs prepare.</summary>
        public Task Enter(string levelId, string spawn, Action prepare = null)
        {
            Current = Level(levelId);
            Record("enter", spawn == null ? levelId : $"{levelId}:{spawn}");
            if (spawn != null)
            {
                if (TryGetSpawn(spawn, out var x, out var y, out var z, out var yaw)) TeleportTo(x, y, z, yaw);
                else if (Current.FromJson) Warnings.Add($"enter {levelId}: no spawn '{spawn}'");
            }
            prepare?.Invoke();
            Nearest?.Invoke(null);
            return Task.CompletedTask;
        }

        public void ApplyLevelState(string key, JToken value)
        {
            if (Current == null) throw new FakeHostException($"ApplyLevelState({key}) before any level was entered");
            Current.State[key] = value;
            Record("state", $"{Current.Id}.{key}={value?.ToString(Newtonsoft.Json.Formatting.None)}", value);
            if (Current.FromJson)
            {
                if (!Current.Accepts.TryGetValue(key, out var ok)) Warnings.Add($"{Current.Id}: state '{key}' is not in the level json");
                else if (value is JValue && !ok.Any(a => JToken.DeepEquals(a, value))) Warnings.Add($"{Current.Id}: {key}={value} is not one of {ok.ToString(Newtonsoft.Json.Formatting.None)}");
            }
        }

        public JToken LevelDefault(string key) => Current != null && Current.Defaults.TryGetValue(key, out var v) ? v : null;

        public string Env { get; private set; }
        public void SetEnv(string preset, double seconds = 0) { Env = preset; Record("env", preset); }
        public bool PowerOn { get; private set; } = true;
        public void SetPower(bool on) { PowerOn = on; Record("power", on ? "on" : "off"); }

        public double PlayerX { get; set; }
        public double PlayerY { get; set; }
        public double PlayerZ { get; set; }
        public double PlayerYaw { get; set; }
        public void Teleport(string spawn)
        {
            if (TryGetSpawn(spawn, out var x, out var y, out var z, out var yaw)) TeleportTo(x, y, z, yaw);
            else Warnings.Add($"teleport: no spawn '{spawn}' in {LevelId}");
            Record("teleport", spawn);
        }
        public void TeleportTo(double x, double y, double z, double yaw) { PlayerX = x; PlayerY = y; PlayerZ = z; PlayerYaw = yaw; }
        public double SpeedMul { get; set; } = 1;
        public double StaminaUse { get; set; } = 1;
        public bool Crouching { get; set; }
        public string Carrying { get; set; }
        public bool Flashlight { get; private set; }
        public void SetFlashlight(bool on) { Flashlight = on; Record("flashlight", on ? "on" : "off"); }
        public string PlayerPreset { get; private set; }
        public void SetPlayerPreset(string preset) { PlayerPreset = preset; Record("preset", preset); }
        public bool ModelHidden { get; private set; }
        public void SetModelHidden(bool hidden) => ModelHidden = hidden;

        public bool ControlsEnabled { get; private set; } = true;
        public void SetControls(bool on) => ControlsEnabled = on;
        public string View { get; private set; } = "third";
        public void SetView(string view) { View = view; Record("view", view); }
        public void Shake(double strength, double seconds) => Record("shake", strength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public CameraShot Camera { get; private set; }
        public void SetCameraOverride(CameraShot shot) { Camera = shot; Record("camera", shot == null ? "clear" : "set", shot); }
        public double Black { get; private set; }
        public Task FadeTo(double black, double seconds) { Black = black; Record("fade", black.ToString(System.Globalization.CultureInfo.InvariantCulture)); return Task.CompletedTask; }
        public JObject Grade { get; private set; }
        public void SetGrade(JObject grade) { Grade = grade; Record("grade", grade?.ToString(Newtonsoft.Json.Formatting.None), grade); }
        public double TimeScale { get; set; } = 1;
        public bool ReduceMotion { get; set; }

        public IStoryPerson Npc(string name) => Current != null && name != null && Current.Npcs.TryGetValue(name, out var p) ? p : null;
        public FakePerson Person(string name) => (FakePerson)Npc(name);
        public IStoryPerson SpawnPerson(string preset, double x, double y, double z, double yaw)
        {
            var p = new FakePerson(preset, preset, x, y, z, yaw, true);
            Current?.Spawned.Add(p);
            Record("spawnPerson", preset, p);
            return p;
        }
        public void RemovePerson(IStoryPerson person) { if (person is FakePerson f) Current?.Spawned.Remove(f); Record("removePerson", person?.Name); }

        public event Action<double> Frame;
        public event Action<StoryPoint> Interact;
        public event Action<StoryPoint> Nearest;
        public event Action<string> Key;
        public event Action<double> Land;
        public event Action<JObject> Blocked;

        // ================================================================== IStoryHud

        public string Clock { get; private set; }
        public Stats Stats { get; private set; }
        public double Battery { get; private set; }
        public double? BagKg { get; private set; }
        public double Tension { get; private set; }
        public double Stamina { get; private set; }
        public string Prompt { get; private set; }
        public bool Cinematic { get; private set; }
        public bool HudShown { get; private set; } = true;
        public JObject Map { get; private set; }
        public double? Compass { get; private set; }
        public (string title, string subtitle) Chapter { get; private set; }

        public void SetClock(string text) => Clock = text;
        public void SetStats(Stats stats) => Stats = stats;
        public void SetBattery(double percent) => Battery = percent;
        public void SetBag(double? weightKg, double limitKg) => BagKg = weightKg;
        public void SetTension(double amount01) => Tension = amount01;
        public void SetStamina(double amount01) => Stamina = amount01;
        void IStoryHud.Prompt(string label) => Prompt = label;
        public void Toast(string text, string label, int ms) => Record("toast", text, (label, ms));
        public Task Caption(string text, string speaker = null, int? ms = null) { Record("caption", text, speaker); return Task.CompletedTask; }
        public void ClearCaption() => Record("clearCaption", null);
        public void SetObjective(string title, string body, IReadOnlyList<ObjectiveStep> steps) => Record("objective", title, (title, body, steps));
        public void SetChapter(string title, string subtitle) { Chapter = (title, subtitle); Record("chapter", title, subtitle); }
        public void SetCinematic(bool on) { Cinematic = on; Record("cinematic", on ? "on" : "off"); }
        public void Hurt(double amount01) => Record("hurt", amount01.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void Show() => HudShown = true;
        public void Hide() => HudShown = false;
        public void SetMap(JObject map) => Map = map;
        public void SetCompass(double? yaw) => Compass = yaw;

        /// <summary>Labels of toasts in order (official, site, neighbor, rumor, self).</summary>
        public List<string> ToastLabels => Calls("toast").Select(c => (((string label, int ms))c.Data).label).ToList();

        // ================================================================== IStoryUi

        public Task<string> Talk(TalkOptions options)
        {
            Record("talk", options.Speaker, options);
            if (options.Choices == null || options.Choices.Count == 0) return Task.FromResult<string>(null);
            string id; string how;
            int rule = _talkRules.FindIndex(r => r.match(options));
            if (rule >= 0) { id = _talkRules[rule].id; how = "rule"; if (_talkRules[rule].once) _talkRules.RemoveAt(rule); }
            else if (_talkAnswers.Count > 0) { id = _talkAnswers.Dequeue(); how = "queue"; }
            else if (Strict) throw new FakeHostException($"unscripted talk (strict): {options.Speaker} / {options.Lines.FirstOrDefault()} / choices: {string.Join(", ", options.Choices.Select(c => c.Id + (c.Disabled ? "(disabled)" : "")))}");
            else { id = TalkPolicy(options); how = "policy"; }
            if (id != null)
            {
                var c = options.Choices.FirstOrDefault(x => x.Id == id);
                if (c == null) throw new FakeHostException($"talk answer '{id}' ({how}) not offered by {options.Speaker}: {string.Join(", ", options.Choices.Select(x => x.Id))}");
                if (c.Disabled) throw new FakeHostException($"talk answer '{id}' ({how}) is disabled for {options.Speaker}: {c.Why}");
            }
            Record("answer", id, how);
            return Task.FromResult(id);
        }

        public Task<string> Card(CardOptions options)
        {
            Record("card", options.Title, options);
            if (options.Actions == null || options.Actions.Count == 0) return Task.FromResult<string>(null);
            string id; string how;
            if (_cardAnswers.Count > 0) { id = _cardAnswers.Dequeue(); how = "queue"; }
            else if (Strict) throw new FakeHostException($"unscripted card (strict): {options.Title} / actions: {string.Join(", ", options.Actions.Select(a => a.Id))}");
            else { id = CardPolicy(options); how = "policy"; }
            if (id != null && options.Actions.All(a => a.Id != id))
                throw new FakeHostException($"card answer '{id}' ({how}) not offered by '{options.Title}': {string.Join(", ", options.Actions.Select(a => a.Id))}");
            Record("answer", id, how);
            return Task.FromResult(id);
        }

        public Task TimeSkip(string from, string to, IReadOnlyList<string> text)
        {
            Record("timeSkip", $"{from} -> {to}", (from, to, text));
            return Task.CompletedTask;
        }

        public Task<JToken> Open(string screen, JObject options)
        {
            Record("open", screen, options);
            JToken r;
            if (_openAnswers.TryGetValue(screen, out var q) && q.Count > 0) r = q.Dequeue();
            else if (Strict) throw new FakeHostException($"unscripted screen (strict): {screen} {options?.ToString(Newtonsoft.Json.Formatting.None)}");
            else r = OpenPolicy(screen, options);
            Record("answer", r?.ToString(Newtonsoft.Json.Formatting.None), "open");
            return Task.FromResult(r);
        }

        // ================================================================== IStoryAudio / IStoryPersist

        public string AmbienceName { get; private set; }
        public void Ambience(string name, double fadeSeconds = 1.2) { AmbienceName = name; Record("ambience", name); }
        public void Cue(string name, JObject args = null) => Record("cue", name, args);
        public readonly HashSet<string> Loops = new();
        public void Loop(string name, bool on, JObject args = null) { if (on) Loops.Add(name); else Loops.Remove(name); Record("loop", $"{name} {(on ? "on" : "off")}", args); }

        /// <summary>Called on every checkpoint (e.g. to snapshot the game like the director does).</summary>
        public Action<string> OnCheckpoint;
        public int Saves { get; private set; }
        void IStoryPersist.Checkpoint(string key) { Record("checkpoint", key); OnCheckpoint?.Invoke(key); }
        public void Save() { Saves++; Record("save", null); }
    }
}
