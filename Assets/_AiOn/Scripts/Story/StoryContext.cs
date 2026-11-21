// Port of story/runtime.js createRuntime: the glue chapters use to roam, talk, keep the clock, change levels and
// fail softly. Chapters talk only to this class; it talks only to the host interfaces in StoryContract.cs.
//
// async model: every method is plain async Task. Continuations of a roam resume through the current
// SynchronizationContext (Unity's main thread in the game, FakeStoryHost's queue in tests), like the original's
// promise callbacks, so nothing here ever blocks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public sealed class StoryContext
    {
        // ------------------------------------------------------------------ static tables (runtime.js exports)

        /// <summary>Default door wiring between levels. "level:point" -> (level, spawn).</summary>
        public static readonly IReadOnlyDictionary<string, (string Level, string Spawn)> Doors = new Dictionary<string, (string, string)>
        {
            ["town:home"] = ("apartment", "lobby"), ["apartment:lobbyExit"] = ("town", "home"),
            ["town:mart"] = ("mart", "entry"), ["mart:martExit"] = ("town", "mart"),
            ["town:store"] = ("store", "entry"), ["store:storeExit"] = ("town", "store"),
            ["town:office"] = ("office", "entry"), ["office:officeExit"] = ("town", "office"),
            ["shelter:shelterExit"] = ("town", "shelter"),
        };

        /// <summary>Which levels use the outdoor env for the current story time unless a chapter overrides it.</summary>
        static readonly HashSet<string> Outdoor = new() { "town" };

        static IReadOnlyDictionary<string, JToken> D(string json)
        {
            var o = JObject.Parse(json);
            return o.Properties().ToDictionary(p => p.Name, p => p.Value);
        }

        /// <summary>
        /// LEVEL_DEFAULTS: what each level looks like when it is first built. A key a chapter set and a retry or a new run
        /// no longer lists goes back to this value on the next enter. The engine's own LevelDefault(key) wins.
        /// The values are shared: Goto applies clones.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, JToken>> LevelDefaults = new Dictionary<string, IReadOnlyDictionary<string, JToken>>
        {
            ["apartment"] = D("{ wallet: 'home', power: 'on', stock: {}, bag: 'empty', radio: 'none', hallFire: 'none', smoke: 0, haze: 'follow', seoyeonDoor: 'closed', candles: 'none', windowView: 'day', elder304: 'off', curtain: 'none' }"),
            ["town"] = D("{ traffic: 'normal', crowd: 'normal', closures: [], gates: { east: 'open', stairs: 'open', ramp: 'open', access: 'closed' }, waterPoint: 'off', buses: 'off', notices: 'none', strike: 'none' }"),
            ["shelter"] = D("{ crowd: 'settled', phase: 'calm', power: 'grid', fire: 'none', ways: ['stairs', 'ramp', 'access', 'east'], elder: 'off', seoyeon: 'hidden' }"),
            ["store"] = D("{ soldOut: [], power: 'on', clerk: 'in' }"),
            ["mart"] = D("{ crowd: 'normal', soldOut: [], power: 'on' }"),
            ["office"] = D("{ alerts: 0 }"),
        };

        /// <summary>staleKeys: the keys to put back to their default before a level's remembered states are applied.</summary>
        public static List<string> StaleKeys(IEnumerable<string> applied, IEnumerable<string> want, IEnumerable<string> defaults)
        {
            var w = new HashSet<string>(want ?? Enumerable.Empty<string>());
            var d = new HashSet<string>(defaults ?? Enumerable.Empty<string>());
            return (applied ?? Enumerable.Empty<string>()).Where(k => !w.Contains(k) && d.Contains(k)).ToList();
        }

        /// <summary>A prompt with no handler in this chapter still answers, so E never silently does nothing.</summary>
        public static string IdleLine(StoryPoint point) =>
            point.Kind == "door" ? "지금은 이 문으로 나갈 때가 아니다." : point.Kind == "shelter" ? "지금은 대피소에 들어갈 일이 없다." : $"{point.Label}: 지금은 할 일이 없다.";

        /// <summary>An On['*'] (or any) roam handler returns this when it handled the point but the roam goes on.</summary>
        public static readonly JToken Handled = new JValue("\u0001handled");

        /// <summary>
        /// The original's undefined: C# null, and also the JValue a null string converts to (so a handler or Tick written
        /// as <c>cond ? "go" : null</c> keeps roaming instead of ending with an empty value). JValue.CreateNull() (JSON
        /// null, type Null) is a value: it ends the roam with null, like the original's null.
        /// </summary>
        public static bool IsUndefined(JToken v) => v == null || (v is JValue jv && jv.Type == JTokenType.String && jv.Value == null);

        // ------------------------------------------------------------------ state

        readonly Func<GameState> _getG;
        readonly Action<GameState> _setG;

        public GameState G => _getG();
        public GameRules Rules { get; }
        public Facts Facts { get; }
        public IStoryEngine Engine { get; }
        public IStoryHud Hud { get; }
        public IStoryUi Ui { get; }
        public IStoryAudio Audio { get; }
        public IStoryPersist Persist { get; }
        public StorySettings Settings { get; }

        /// <summary>Checkpoint key a retry resumes from.</summary>
        public string Resume;
        /// <summary>Game seconds per real second while roaming (0 = clock stopped).</summary>
        public double Rate;
        public string OutdoorEnv = "day", IndoorEnv = "interior";
        /// <summary>levelId -> { key: value } re-applied on every enter.</summary>
        public Dictionary<string, Dictionary<string, JToken>> LevelStates = new();
        /// <summary>True while a Roam runs.</summary>
        public bool Roaming;
        /// <summary>Both hands on the 3D extinguisher (ExtinguisherScene.Run3D): the view, chalk and first-aid keys wait.</summary>
        public bool HandsFull;

        bool _power = true;
        /// <summary>Grid power. The shelter runs on its own generator, so the engine sees power there either way.</summary>
        public bool Power
        {
            get => _power;
            set { _power = value; Engine.SetPower(_power || Engine.LevelId == "shelter"); }
        }

        // Fields the chapters and the director set (ad-hoc ctx props in the original).
        public string LightSource;
        public bool Calm;
        public JObject Snap;
        public List<JObject> Trail;
        public double? SpeedOverride;
        public double? ProfileSpeed;
        public string DeathCause;
        public string SeoyeonAsk;
        /// <summary>
        /// The teacher route (교사용 30분 모드) is playing: optional long scenes keep to the route's time (Evac.Crossroads plays
        /// the original's one-line skip instead of 「임시 거처의 하루」 when the class picks 'move'). Set by the director.
        /// </summary>
        public bool TeacherRun;

        // Hooks the director fills; chapters call them with ?.
        public Action<string> SetPlayer;
        /// <summary>Someone else's slow body (ElderPovScene): true = walking only, no running, jumping or vaulting, whatever the profile.</summary>
        public Action<bool> WalkOnly;
        /// <summary>An optional scene left out of the chapter time: call at its start, call the returned action at its end.</summary>
        public Func<Action> BeginUntimed;
        /// <summary>(x, z, size)</summary>
        public Action<double, double, double> Boom;
        public Func<Task> RecordRun;
        /// <summary>Read aloud (the director passes it to the host's voice only in the low-vision profile).</summary>
        public Action<string> Speak;
        /// <summary>
        /// 「임시 거처의 하루」 (ShelterLifeScene): true shows the HUD's 긴장 row as 마음 (차분 · 조금 불안 · 불안), false puts it back.
        /// The same tension stat, read in calm words; no new meter.
        /// </summary>
        public Action<bool> MindMeter;
        public StoryCine Cine;
        /// <summary>The time-lapse cutscene that replaces the time-skip card; if it throws, Skip shows the card.</summary>
        public Func<string, string, IReadOnlyList<string>, Task> SkipOverride;

        double _acc;
        int _lastSeq; // the remembered-action cue toasts deeds recorded after this event seq
        readonly Dictionary<string, HashSet<string>> _applied = new(); // levelId -> keys set on the live level
        readonly List<(double left, Action fire)> _timers = new();

        public StoryContext(GameRules rules, Func<GameState> getG, Action<GameState> setG, Facts facts, IStoryEngine engine, IStoryHud hud,
            IStoryUi ui, IStoryAudio audio, IStoryPersist persist, StorySettings settings)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _getG = getG ?? throw new ArgumentNullException(nameof(getG));
            _setG = setG;
            Facts = facts ?? Facts.Empty;
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Hud = hud ?? throw new ArgumentNullException(nameof(hud));
            Ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Audio = audio ?? throw new ArgumentNullException(nameof(audio));
            Persist = persist ?? throw new ArgumentNullException(nameof(persist));
            Settings = settings ?? new StorySettings();

            Engine.Nearest += p => Hud.Prompt(p?.Label);
            Engine.Frame += OnFrame;
        }

        void OnFrame(double dt)
        {
            RunTimers(dt);
            if (Rate == 0 || !Engine.ControlsEnabled) return;
            _acc += dt * Rate;
            if (_acc >= 60)
            {
                var m = Math.Floor(_acc / 60);
                _acc -= m * 60;
                GameRules.Advance(G, m);
                Refresh();
            }
        }

        // ------------------------------------------------------------------ timers (setTimeout stand-ins, frame driven)

        /// <summary>Runs <paramref name="fire"/> after this many seconds of frames (the original's setTimeout).</summary>
        public void After(double seconds, Action fire)
        {
            if (fire == null) return;
            _timers.Add((Math.Max(0, seconds), fire));
        }

        /// <summary>Completes after this many seconds of frames (the original's await new Promise(r => setTimeout(r, ms))).</summary>
        public Task Wait(double seconds)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            After(seconds, () => tcs.TrySetResult(true));
            return tcs.Task;
        }

        void RunTimers(double dt)
        {
            if (_timers.Count == 0) return;
            var due = new List<Action>();
            for (int i = 0; i < _timers.Count; i++)
            {
                var (left, fire) = _timers[i];
                left -= dt;
                if (left <= 0) { due.Add(fire); _timers.RemoveAt(i); i--; }
                else _timers[i] = (left, fire);
            }
            foreach (var f in due) f();
        }

        // ------------------------------------------------------------------ HUD sync

        public void Refresh()
        {
            var g = G;
            Hud.SetClock(GameRules.FormatClock(g));
            Hud.SetStats(g.Stats);
            Hud.SetBattery(g.Phone.Battery);
            var load = Rules.BagLoad(g);
            bool bagOn = g.Knows("bagOn");
            Hud.SetBag(bagOn ? load.WeightKg : (double?)null, Rules.Db.bagLimitKg);
            // Experience mode (Settings.Profile) slows on top of the story's speed; a heavy bag on the back tires faster.
            Engine.SpeedMul = (SpeedOverride ?? Rules.SpeedMul(g)) * (ProfileSpeed ?? 1);
            Engine.StaminaUse = 1 + (bagOn ? load.WeightKg / Rules.Db.bagLimitKg : 0);
            Hud.SetTension(Math.Max(0, (g.Stats.Tension - 40) / 60));
            int seq = g.Events.Count > 0 ? g.Events[^1].Seq : 0;
            if (seq > _lastSeq)
            {
                var id = Settings.MemoryCue ? Deeds.NewDeeds(g, _lastSeq).FirstOrDefault(d => Deeds.CueText(d.Id) != null)?.Id : null;
                if (id != null) Toast(Deeds.CueText(id), "neighbor", 3600);
                _lastSeq = seq;
            }
        }

        /// <summary>Marks every event so far as already cued (no "will remember" toast for them).</summary>
        public void CueSync() { var g = G; _lastSeq = g.Events.Count > 0 ? g.Events[^1].Seq : 0; }

        /// <summary>Deeds.Remember: the deed event as JSON, or null when it was already remembered.</summary>
        public JObject Remember(string id, JObject details = null)
        {
            var e = Deeds.Remember(G, id, details);
            return e == null ? null : JObject.FromObject(e);
        }

        // ------------------------------------------------------------------ levels

        void ApplyState(string levelId, string key, JToken value)
        {
            // States the runtime keeps for a level that has no state key for them (VIRTUAL in the original).
            if (levelId == "shelter" && key == "seoyeon")
            {
                var n = Engine.Npc("seoyeon");
                if (n != null) n.Visible = value?.Type == JTokenType.String && (string)value == "shown";
                return;
            }
            Engine.ApplyLevelState(key, value);
        }

        HashSet<string> Applied(string levelId)
        {
            if (!_applied.TryGetValue(levelId, out var s)) _applied[levelId] = s = new HashSet<string>();
            return s;
        }

        public void SetLevelState(string levelId, string key, JToken value)
        {
            if (!LevelStates.TryGetValue(levelId, out var want)) LevelStates[levelId] = want = new Dictionary<string, JToken>();
            want[key] = value;
            if (Engine.LevelId == levelId) { ApplyState(levelId, key, value); Applied(levelId).Add(key); }
        }

        /// <summary>
        /// Enter a level and re-apply remembered states and the right lighting. The prepare step runs inside the engine's
        /// Enter (after the level exists, before it is shown), so the host must have switched LevelId/Points/Npc first.
        /// </summary>
        public async Task Goto(string levelId, string spawn = null, string env = null)
        {
            bool prepared = false;
            void Prepare()
            {
                prepared = true;
                Engine.SetPower(Power || levelId == "shelter");
                var want = LevelStates.TryGetValue(levelId, out var w) ? w : new Dictionary<string, JToken>();
                var seen = Applied(levelId);
                LevelDefaults.TryGetValue(levelId, out var table);
                JToken DefaultOf(string k) => Engine.LevelDefault(k) ?? (table != null && table.TryGetValue(k, out var v) ? v : null);
                var defaultKeys = seen.Where(k => DefaultOf(k) != null);
                foreach (var k in StaleKeys(seen.ToList(), want.Keys, defaultKeys.ToList())) ApplyState(levelId, k, DefaultOf(k).DeepClone());
                seen.Clear();
                foreach (var kv in want.ToList()) { ApplyState(levelId, kv.Key, kv.Value); seen.Add(kv.Key); }
                Engine.SetEnv(env ?? (Outdoor.Contains(levelId) ? OutdoorEnv : levelId == "shelter" ? "underground" : Power ? IndoorEnv : "interiorDark"), 0);
            }
            await Engine.Enter(levelId, spawn, Prepare);
            if (!prepared) Prepare();
            Audio.Ambience(AmbienceFor(levelId), 1.2);
        }

        string AmbienceFor(string id)
        {
            if (id == "town") return OutdoorEnv == "blackout" ? "blackout" : OutdoorEnv == "night" || OutdoorEnv == "dusk" ? "night" : "city";
            return id switch { "office" => "office", "mart" => "mart", "store" => "mart", "shelter" => "shelter", _ => "indoor" };
        }

        // ------------------------------------------------------------------ HUD and records

        public void Objective(string title, string body = "", IReadOnlyList<ObjectiveStep> steps = null) =>
            Hud.SetObjective(title, body ?? "", steps ?? Array.Empty<ObjectiveStep>());

        public void Toast(string text, string label = "self", int ms = 3800) => Hud.Toast(text, label ?? "self", ms);

        /// <summary>Captions off: same timing, nothing drawn, so story pacing does not change.</summary>
        public Task Caption(string text, string speaker = null, int? ms = null)
        {
            if (!Settings.Captions && Settings.Profile != "deaf")
                return Wait((ms ?? Math.Min(7000, 1500 + (text ?? "").Length * 60)) / 1000.0);
            return Hud.Caption(text, speaker, ms);
        }

        public GameEvent Event(string type, JObject details = null) => GameRules.RecordEvent(G, type, details ?? new JObject());

        /// <summary>A labelled note in the info log (place defaults to the current level) and a "메모:" toast.</summary>
        public void Note(string text, string label = "self", string place = null)
        {
            GameRules.Note(G, text, label, place ?? Engine.LevelId);
            Toast($"메모: {text}", label);
        }

        /// <summary>The toast names the earlier moment that set it up (Ripples), e.g. "(D-1 오전 · 비상가방을 꾸려 현관에 두었다)".</summary>
        public void Payoff(string prep, string moment)
        {
            var ev = GameRules.RecordEvent(G, "payoff", new JObject { ["prep"] = prep, ["moment"] = moment });
            var c = Ripples.CauseOf(G, prep, ev?.Seq);
            Toast($"준비가 도움이 됐습니다 · {moment}{(c != null ? $" ({c.When} · {c.Text})" : "")}", "self", c != null ? 6000 : 4600);
        }

        public void Stat(string stat, double delta) { GameRules.Adjust(G, stat, delta); Refresh(); }
        public void Pass(double minutes) { GameRules.Advance(G, minutes); Refresh(); }

        public string FactLine(string id)
        {
            var c = Facts.Cite(id);
            return c != null ? $"{c.Text}\n— {c.Source}" : "";
        }

        // ------------------------------------------------------------------ modal UI: controls off while a UI is open

        public async Task<T> Modal<T>(Func<Task<T>> fn)
        {
            Engine.SetControls(false);
            try { return await fn(); }
            finally { Engine.SetControls(true); Refresh(); }
        }

        public async Task Modal(Func<Task> fn)
        {
            Engine.SetControls(false);
            try { await fn(); }
            finally { Engine.SetControls(true); Refresh(); }
        }

        public Task<string> Say(string speaker, IEnumerable<string> lines, List<TalkChoice> choices = null, string role = null) =>
            Modal(() => Ui.Talk(new TalkOptions { Speaker = speaker ?? "", Role = role ?? "", Lines = (lines ?? Enumerable.Empty<string>()).ToList(), Choices = choices }));

        public Task<string> Say(string speaker, string line, List<TalkChoice> choices = null, string role = null) =>
            Say(speaker, new[] { line }, choices, role);

        public Task<string> Card(CardOptions options) => Modal(() => Ui.Card(options));

        /// <summary>The time skip: the director's time-lapse (SkipOverride) if set, else (or if it throws) the card.</summary>
        public async Task Skip(string from, string to, IReadOnlyList<string> text)
        {
            if (SkipOverride != null)
            {
                bool ok;
                try { await SkipOverride(from, to, text); ok = true; }
                catch (StoryFail) { throw; }
                catch (Exception) { ok = false; } // the original logs '[cine] time-lapse failed; showing the card'
                if (ok) return;
            }
            Engine.SetControls(false);
            await Ui.TimeSkip(from, to, text);
            Engine.SetControls(true);
        }

        public void Checkpoint(string key) => Persist.Checkpoint(key);

        /// <summary>A story mark (not a retry point): the teacher mode may end its moment here (StoryDirector.Reached).</summary>
        public void Reached(string mark) => Persist.Reached(mark);

        /// <summary>Throws StoryFail: the director shows the fail card and retries.</summary>
        public void Fail(string type, JObject detail = null) => throw new StoryFail(type, detail);

        // ------------------------------------------------------------------ roam

        /// <summary>
        /// Free roam until a handler returns something other than null (null = keep roaming; Handled = handled, keep
        /// roaming). Doors: follow Doors unless a handler covers the point. Deadline: StoryFail when the clock passes it.
        /// Health 0: StoryFail('dead', { why: DeathCause ?? 'hurt' }). Tick: called every frame, non-null ends the roam.
        /// A menu or dialog (controls off) pauses walk-through doors, Tick, deadlines and the health check.
        /// </summary>
        public Task<JToken> Roam(RoamOptions options = null)
        {
            var o = options ?? new RoamOptions();
            var on = o.On ?? new Dictionary<string, Func<StoryPoint, Task<JToken>>>();
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            double prevRate = Rate;
            Rate = o.Rate ?? Rate;
            Roaming = true;
            bool busy = false, done = false;
            var armed = new HashSet<string>();
            string lastL = null;
            Action<StoryPoint> onInteract = null;
            Action<double> onFrame = null;

            void Finish(JToken value, Exception err)
            {
                if (done) return;
                done = true;
                Engine.Interact -= onInteract;
                Engine.Frame -= onFrame;
                Rate = prevRate;
                Roaming = false;
                if (err != null) tcs.TrySetException(err); else tcs.TrySetResult(value);
            }

            Func<StoryPoint, Task<JToken>> Handler(string id) => id != null && on.TryGetValue(id, out var h) ? h : null;

            // Walk-through doors: stepping into a plain door (no handler of the chapter's own) goes through it, no key.
            // A door arms only once you have been more than 1.6 m from it in the current level, so arriving next to one
            // never bounces you back (armed resets on every level change: E on a door, or a handler's own Goto).
            void WalkDoor()
            {
                var lv = Engine.LevelId;
                if (!o.Doors || busy || lv == null) return;
                if (lv != lastL) { lastL = lv; armed = new HashSet<string>(); }
                foreach (var p in Engine.Points ?? Array.Empty<StoryPoint>())
                {
                    var key = $"{lv}:{p.Id}";
                    if (!Doors.ContainsKey(key) || Handler(p.Id) != null || Handler(key) != null || !p.Enabled) continue;
                    var d = Math.Sqrt((Engine.PlayerX - p.X) * (Engine.PlayerX - p.X) + (Engine.PlayerZ - p.Z) * (Engine.PlayerZ - p.Z));
                    if (d > 1.6) armed.Add(key);
                    else if (d < 0.75 && armed.Contains(key)) { armed = new HashSet<string>(); _ = Interact(p); return; }
                }
            }

            async Task Interact(StoryPoint point)
            {
                if (busy || done || point == null) return;
                busy = true;
                try
                {
                    var key = $"{Engine.LevelId}:{point.Id}";
                    JToken value = null;
                    Func<StoryPoint, Task<JToken>> h;
                    if ((h = Handler(point.Id)) != null || (h = Handler(key)) != null) value = await Modal(() => h(point));
                    else if (o.Shelter != null && point.Kind == "shelter" && Engine.LevelId == "town") value = await Modal(() => o.Shelter(point));
                    else if (o.Doors && Doors.TryGetValue(key, out var door)) { armed = new HashSet<string>(); await Goto(door.Level, door.Spawn); }
                    // The chapter's '*' sees every other point first; null means "not mine" and the common text runs.
                    else if ((h = Handler("*")) != null && !IsUndefined(value = await Modal(() => h(point)))) { /* handled */ }
                    else if (World.IsCommon(this, point)) await Modal(() => World.Common(this, point));
                    else Toast(IdleLine(point), "self", 2200);
                    if (ReferenceEquals(value, Handled)) value = null;
                    if (!IsUndefined(value)) Finish(value, null);
                }
                catch (Exception err) { Finish(null, err); }
                finally { busy = false; }
            }

            onInteract = p => { _ = Interact(p); };
            onFrame = dt =>
            {
                if (done || !Engine.ControlsEnabled) return;
                try
                {
                    WalkDoor();
                    if (o.Tick != null) { var v = o.Tick(dt); if (!IsUndefined(v)) Finish(v, null); }
                    var g = G;
                    if (g.Stats.Health <= 0) { Finish(null, new StoryFail("dead", new JObject { ["why"] = DeathCause ?? "hurt" })); return; }
                    var dl = o.Deadline;
                    if (dl != null && (g.Day > dl.Day || (g.Day == dl.Day && g.Minute >= dl.Minute)))
                        Finish(null, new StoryFail(dl.TypeOf != null ? dl.TypeOf() : dl.Type));
                }
                catch (Exception err) { Finish(null, err); }
            };
            Engine.Interact += onInteract;
            Engine.Frame += onFrame;
            return tcs.Task;
        }

        /// <summary>Walk-to helper for Tick: true when the player is within r metres of (x, z) in the given level, else null.</summary>
        public Func<double, JToken> Near(string levelId, double x, double z, double r = 3) => _ =>
        {
            if (Engine.LevelId != levelId) return null;
            double dx = Engine.PlayerX - x, dz = Engine.PlayerZ - z;
            return Math.Sqrt(dx * dx + dz * dz) < r ? new JValue(true) : null;
        };

        public TownPlace Place(string id) => TownMap.Places[id];
        public TownSpawn Spawn(string id) => TownMap.Spawns[id];

        /// <summary>Replaces the game with a saved one (SaveCodec.Load) and refreshes the HUD.</summary>
        public void Load(string json)
        {
            if (_setG == null) throw new InvalidOperationException("this context has no setG");
            _setG(SaveCodec.Load(json, Rules.Db));
            Refresh();
        }
    }
}
