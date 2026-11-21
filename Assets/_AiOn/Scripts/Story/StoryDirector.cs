// Port of app.js: the shell around the story. Boot (title screen, continue, new game), the chapter runner with
// soft-fail retries and fail cards, save / continue with checkpoints and the runtime snapshot, the danger / echo /
// cine / time-lapse wiring as hooks on StoryContext, light-source upkeep, the run record (Challenges.FinishRun),
// settings and experience profiles, and the global keys while roaming (as public methods too).
//
// What stays in the host (Unity) because the contract has no member for it: rendering (minimap walk/solids, the
// stamina bar, footsteps, the pointer-lock hint), engine/actions.js (picking up and setting down: the host calls Hurt /
// DropHeld; the competition upgrade took the fists and the throw out), audio volumes and buses, graphics quality, text scale and mouse sensitivity (ApplyHostSettings hook),
// speech for the low-vision profile (Speak hook) and reloading the scene for "menu" (MenuRequested hook).
//
// Screens open through IStoryUi.Open with the original's option object. Functions that took a positional argument get
// it wrapped: notebook(entries) -> { entries }, gallery(view) -> { view }, about(sources) -> { sources },
// ending(record, sources) -> { record, sources }; records(rec) and settings(current) take the object itself.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>Browser localStorage stand-in (app.js store): get/set strings by key; may lose data, never throws.</summary>
    public interface IStoryStore
    {
        string Get(string key);
        void Set(string key, string value);
    }

    /// <summary>An in-memory store (tests, or a host without persistence).</summary>
    public sealed class MemoryStoryStore : IStoryStore
    {
        public readonly Dictionary<string, string> Data = new();
        public string Get(string key) => key != null && Data.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) { if (key != null) Data[key] = value; }
    }

    /// <summary>The danger system (danger.js createDanger) as the director uses it.</summary>
    public interface IStoryDanger
    {
        /// <summary>The stranger the danger layer spawned (the talk facing reads it; nobody hits anyone).</summary>
        bool IsHostile(IStoryPerson person);
        /// <summary>Force an event now: 'boom' (x, z, size), 'blast', 'shell', 'collapse', 'looter'.</summary>
        void Trigger(string kind, double x, double z, double size);
        /// <summary>A retry or a new run: no looter, nothing pending, the street as built.</summary>
        void Reset();
    }

    /// <summary>The echo of earlier runs (echo.js createEcho) as the director uses it.</summary>
    public interface IStoryEcho
    {
        void Begin();
        void Resume();
        void Death(string why);
        Task Chalk();
        /// <summary>A prepared-state run (scene select, teacher mode) holds the recording: no trail of it in the player's own record.</summary>
        void Hold(bool on) { }
    }

    /// <summary>A chapter: its id (prologue, prep, dday, endure, crossroads, evac, ending) and its entry.</summary>
    public sealed class DirectorChapter
    {
        public string Id;
        /// <summary>The chapter; its result matters only for 'ending' (the ending screen's pick, e.g. 'restart').</summary>
        public Func<StoryContext, Task<JToken>> Run;
        public DirectorChapter() { }
        public DirectorChapter(string id, Func<StoryContext, Task<JToken>> run) { Id = id; Run = run; }
    }

    /// <summary>The parts the director wires together. Default() uses the story classes; tests pass stubs.</summary>
    public sealed class DirectorParts
    {
        public List<DirectorChapter> Chapters;
        /// <summary>cutscenes.js opening(ctx). Its failure never blocks the intro.</summary>
        public Func<StoryContext, Task> Opening;
        /// <summary>cutscenes.js timelapse(ctx, { from, to, text }); becomes ctx.SkipOverride (card on failure).</summary>
        public Func<StoryContext, string, string, IReadOnlyList<string>, Task> Timelapse;
        public Func<StoryContext, StoryCine> MakeCine;
        /// <summary>(ctx, hurt(n, why)) -> danger.</summary>
        public Func<StoryContext, Action<double, string>, IStoryDanger> MakeDanger;
        /// <summary>(ctx, store) -> echo.</summary>
        public Func<StoryContext, IStoryStore, IStoryEcho> MakeEcho;

        /// <summary>The original CHAPTERS list and parts, through the classes named in the story brief.</summary>
        public static DirectorParts Default(DangerFx dangerFx = null, ICineFx cineFx = null) => new DirectorParts
        {
            Chapters = new List<DirectorChapter>
            {
                new("prologue", c => StoryDirector.AsChapter(Prologue.Run(c))),
                new("prep", c => StoryDirector.AsChapter(Prep.Run(c))),
                new("dday", c => StoryDirector.AsChapter(DDay.Run(c))),
                new("endure", c => StoryDirector.AsChapter(Endure.Run(c))),
                new("crossroads", c => StoryDirector.AsChapter(Evac.Crossroads(c))),
                new("evac", c => StoryDirector.AsChapter(Evac.Evacuation(c))),
                new("ending", c => StoryDirector.AsChapter(Evac.Ending(c))),
            },
            Opening = c => Cutscenes.Opening(c),
            Timelapse = (c, from, to, text) => Cutscenes.Timelapse(c, from, to, text),
            MakeCine = c => new StoryCine(c, cineFx),
            MakeDanger = (c, hurt) =>
            {
                var d = Danger.Create(c, (n, why) => hurt(n, why), dangerFx);
                return new FnDanger(p => d.IsHostile(p), (kind, x, z, size) => d.Trigger(kind, x, z, size), () => d.Reset());
            },
            MakeEcho = (c, store) =>
            {
                var e = Echo.Create(c, store.Get, store.Set);
                return new FnEcho(() => e.Begin(), () => e.Resume(), why => e.Death(why), () => e.Chalk(), on => e.Held = on);
            },
        };
    }

    /// <summary>IStoryDanger from delegates (so the danger class need not implement the interface).</summary>
    public sealed class FnDanger : IStoryDanger
    {
        readonly Func<IStoryPerson, bool> _hostile; readonly Action<string, double, double, double> _trigger; readonly Action _reset;
        public FnDanger(Func<IStoryPerson, bool> isHostile, Action<string, double, double, double> trigger, Action reset) { _hostile = isHostile; _trigger = trigger; _reset = reset; }
        public bool IsHostile(IStoryPerson person) => _hostile?.Invoke(person) ?? false;
        public void Trigger(string kind, double x, double z, double size) => _trigger?.Invoke(kind, x, z, size);
        public void Reset() => _reset?.Invoke();
    }

    /// <summary>IStoryEcho from delegates.</summary>
    public sealed class FnEcho : IStoryEcho
    {
        readonly Action _begin, _resume; readonly Action<string> _death; readonly Func<Task> _chalk; readonly Action<bool> _hold;
        public FnEcho(Action begin, Action resume, Action<string> death, Func<Task> chalk, Action<bool> hold = null) { _begin = begin; _resume = resume; _death = death; _chalk = chalk; _hold = hold; }
        public void Begin() => _begin?.Invoke();
        public void Resume() => _resume?.Invoke();
        public void Death(string why) => _death?.Invoke(why);
        public Task Chalk() => _chalk?.Invoke() ?? Task.CompletedTask;
        public void Hold(bool on) => _hold?.Invoke(on);
    }

    /// <summary>An experience profile (체험 모드): how the body moves. Speed multiplies the story's speed.</summary>
    public sealed class ExperienceProfile
    {
        public bool Run, Jump, Stairs;
        public double Eye, Speed;
        /// <summary>The hands' keys (G pick up, Q set down): off while the player is someone else (StoryDirector.OtherPov).</summary>
        public bool Hands = true;
        public ExperienceProfile(bool run, bool jump, double eye, bool stairs, double speed) { Run = run; Jump = jump; Eye = eye; Stairs = stairs; Speed = speed; }
        public ExperienceProfile With(bool run) => new ExperienceProfile(run, Jump, Eye, Stairs, Speed);
        public ExperienceProfile With(bool run, bool jump, bool hands) => new ExperienceProfile(run, jump, Eye, Stairs, Speed) { Hands = hands };
    }

    public sealed class StoryDirector : IStoryPersist
    {
        // ------------------------------------------------------------------ constants (app.js)

        public const string SaveKey = "gyeongbo.save.v2", SettingsKey = "gyeongbo.settings.v1", GalleryKey = "gyeongbo.endings.v1",
            RecordsKey = "gyeongbo.records.v1", LoungeNameKey = "gyeongbo.lounge.v1";

        public static readonly string[] LevelIds = { "town", "apartment", "mart", "store", "office", "shelter" };

        public static readonly IReadOnlyDictionary<string, ExperienceProfile> Profiles = new Dictionary<string, ExperienceProfile>
        {
            ["none"] = new(true, true, 1.62, true, 1),
            ["mobility"] = new(false, false, 1.62, true, 0.62),   // 보행 장애: no running, slow
            ["wheelchair"] = new(false, false, 1.18, false, 0.85), // seated eye height, ramps only
            ["lowvision"] = new(true, true, 1.62, true, 0.8),      // 저시력: blurred world, spoken prompts
            ["deaf"] = new(true, true, 1.62, true, 1),             // 청각 장애: no sound, captions and cues
            ["guardian"] = new(false, false, 1.62, true, 0.72),    // 지체 장애: a guardian walks along
        };

        static readonly HashSet<string> Envs = new() { "day", "afternoon", "dusk", "night", "blackout", "interior", "interiorNight", "interiorDark", "underground" };

        /// <summary>청각 장애 mode: loud sounds also arrive as on-screen cues.</summary>
        public static readonly IReadOnlyDictionary<string, string> SoundCues = new Dictionary<string, string>
        {
            ["boom"] = "[소리] 쾅— 큰 폭발음. 바닥이 울린다.", ["phoneAlert"] = "[진동] 휴대폰이 울리며 떨린다.", ["siren"] = "[소리] 사이렌이 울리기 시작했다.",
        };

        /// <summary>Texts the host shows itself: a level swap waiting on shaders, the pointer-lock hint, a boot that failed.</summary>
        public const string LoadingText = "장면을 준비하는 중…", LockHintText = "화면을 한 번 클릭하면 마우스만 움직여 둘러볼 수 있습니다",
            BootErrorText = "문제가 생겨 게임을 계속할 수 없습니다. 새로고침해 주세요.";

        public static readonly (string key, string text)[] Keys =
        {
            ("W A S D · 방향키", "이동"), ("마우스", "둘러보기 — 화면을 한 번 클릭하면 마우스만 움직여도 시점이 돕니다 (Esc로 커서 되찾기)"),
            ("Shift", "달리기 (기력이 줄어듭니다. 가방이 무거우면 더 빨리 지칩니다)"), ("Space", "점프 · 낮은 담/차/난간 앞에서 누르면 넘거나 올라감"),
            ("G", "바닥의 물건 줍기 / 내려놓기"), ("Q", "손에 든 물건 내려놓기"), ("C", "엎드리기/웅크리기 (폭발·파편 피해가 줄어듭니다. Space로 일어서기)"), ("X", "구급함 쓰기"),
            ("B", "분필로 바닥에 적기 — 다음 회차의 내가 보게 됩니다"), ("E · Enter", "살피기 · 대화 · 사용"),
            ("F", "손전등 켜기/끄기 (정전 후)"), ("V", "1인칭 / 3인칭 전환"), ("P", "휴대폰 (문자 · FM 라디오)"), ("Tab", "수첩"), ("I", "소지품"), ("M", "지도"), ("H", "이 조작법 다시 보기"), ("Esc", "일시정지 · 설정"),
        };

        // ------------------------------------------------------------------ state

        public GameRules Rules { get; }
        public Facts Facts { get; }
        public IStoryEngine Engine { get; }
        public IStoryHud Hud { get; }
        public IStoryUi Ui { get; }
        public IStoryAudio Audio { get; }
        public IStoryStore Store { get; }
        public StoryContext Ctx { get; }
        public List<DirectorChapter> Chapters { get; }
        /// <summary>The app settings object (quality, volume, captions, view, profile, memoryCue...), as the original's JSON.</summary>
        public JObject Settings { get; private set; }
        public StorySettings StorySettings { get; }

        public GameState G;
        /// <summary>progress.index: the chapter being played.</summary>
        public int Index;

        readonly IStoryPersist _hostPersist;
        readonly Func<StoryContext, Task> _opening;
        readonly IStoryDanger _danger;
        readonly IStoryEcho _echo;

        // ------------------------------------------------------------------ host hooks

        /// <summary>Monotonic milliseconds for chapter times and the stair helper (default: the frames the engine ran).</summary>
        public Func<double> NowMs;
        /// <summary>Random in [0, 1) (the OUCH line, a new game's seed).</summary>
        public Func<double> Random;
        /// <summary>Today as YYYY-MM-DD (local) for the run record.</summary>
        public Func<string> Today = () => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        /// <summary>Settings the host owns (volume, mute, buses, text scale, quality, sensitivity) and the body (profile).</summary>
        public Action<JObject, ExperienceProfile> ApplyHostSettings;
        /// <summary>Low-vision mode: read this aloud (Korean).</summary>
        public Action<string> Speak;
        /// <summary>engine/actions.js drop(): let go of what is in hand (before a level change and on a reset).</summary>
        public Action DropHeld;
        /// <summary>groundAt(level.walk, x, z, feetY, false, 1.5): the walk height under (x, z) within reach of feetY, or null.
        /// The guardian eases to it (the player's height when null). Default: the town's walk zones (StoryCine.TownWalk), null elsewhere.</summary>
        public Func<double, double, double, double?> GroundAt;
        /// <summary>engine.level.walk: the current level's walk zones { x0, x1, z0, z1, y, ramp: { axis, y0, y1 } } for the
        /// wheelchair stair help when a Blocked event does not carry them (walk / zone).</summary>
        public Func<IReadOnlyList<JObject>> LevelWalk;
        /// <summary>The pause menu's '메인 메뉴' (the original reloads the page).</summary>
        public Action MenuRequested;
        /// <summary>Errors the original only logged (console.error).</summary>
        public Action<string, Exception> Log;
        /// <summary>A prepared save's JSON by moment id (SceneSelect; Resources/Demo/&lt;id&gt;.json), or null when there is none.</summary>
        public Func<string, string> PreparedSave;

        /// <summary>
        /// A run started from a prepared state (scene select, teacher mode): it saves under SceneSelect.SaveKey (never the
        /// main save or the host's mirror), and records no ending, challenge or echo death.
        /// </summary>
        public bool DemoRun { get; private set; }
        /// <summary>The key the running game saves under.</summary>
        public string ActiveSaveKey => DemoRun ? SceneSelect.SaveKey : SaveKey;

        double _frameMs;
        double _pausedMs;
        bool _flashOn;
        double _flashT, _litT, _mapT;
        bool _helpBusy;
        double _helpedAt = -1e9;
        IStoryPerson _buddy;
        string _buddyLevel, _buddyAnim = "";
        double _buddyY;
        double? _buddySpeed;
        readonly Random _rng = new();

        public bool FlashOn => _flashOn;
        /// <summary>Whose eyes the player sees through (SetPlayer): 정민's bag and phone chips are not shown for 서연.</summary>
        public string Pov { get; private set; } = "jeongmin";
        bool OtherPov => Pov != null && Pov != "jeongmin";
        readonly IStoryHud _rawHud;
        string _lightLevel;
        bool _lightLit;

        /// <summary>The light in hand on / off: the engine's torch and the HUD's torch glyph (phone light only).</summary>
        void SetLight(bool on)
        {
            _flashOn = on; Engine.SetFlashlight(on);
            if (_rawHud is AiOn.UI.StoryOverlay so) so.SetTorch(on && Ctx.LightSource == "phone");
        }

        /// <summary>
        /// The lights a level has, as the engine draws them (StoryContext.Goto): the level's 'power' state when the story
        /// set one, else the grid (the shelter has its own: 'grid' by default). No allocation: a JValue's string is returned
        /// as stored.
        /// </summary>
        public static string PowerOf(IReadOnlyDictionary<string, Dictionary<string, JToken>> states, string level, bool grid)
        {
            if (level == null) return null;
            JToken p = null;
            if (states != null && states.TryGetValue(level, out var st) && st != null) st.TryGetValue("power", out p);
            if (p != null && p.Type == JTokenType.String) return (string)p;
            return level == "shelter" ? "grid" : grid ? "on" : "off";
        }

        /// <summary>
        /// An indoor level with its lights on (power on, the shelter's grid or emergency lights) needs no light in hand.
        /// The town never counts: street lights do not light the way.
        /// </summary>
        public static bool LitLevel(IReadOnlyDictionary<string, Dictionary<string, JToken>> states, string level, bool grid = false)
        {
            if (level == null || level == "town") return false;
            var p = PowerOf(states, level, grid);
            return p == "on" || p == "grid" || p == "emergency";
        }

        /// <summary>The light in hand runs down only while the player is free to walk: not under a talk, a card, a menu or a cutscene.</summary>
        bool LightPaused => !Engine.ControlsEnabled || AiOn.UI.UiScreen.OpenCount > 0 || (Ctx.Cine?.Busy ?? false);

        public StoryDirector(GameRules rules, Facts facts, IStoryEngine engine, IStoryHud hud, IStoryUi ui, IStoryAudio audio, IStoryStore store,
            DirectorParts parts = null, IStoryPersist hostPersist = null, bool reduceMotion = false)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Facts = facts ?? Facts.Empty;
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Hud = new DirectorHud(hud ?? throw new ArgumentNullException(nameof(hud)), this);
            _rawHud = hud;
            Ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Audio = new DirectorAudio(audio ?? throw new ArgumentNullException(nameof(audio)), this);
            Store = store ?? new MemoryStoryStore();
            _hostPersist = hostPersist;
            parts ??= DirectorParts.Default();
            Chapters = parts.Chapters ?? throw new ArgumentException("parts.Chapters is required");
            _opening = parts.Opening;
            NowMs = () => _frameMs;
            Random = () => _rng.NextDouble();

            var saved = SafeParse(Store.Get(SettingsKey));
            Settings = DefaultSettings(reduceMotion);
            foreach (var p in saved.Properties()) Settings[p.Name] = p.Value.DeepClone();
            StorySettings = new StorySettings();

            GroundAt = (x, z, feetY) => Engine.LevelId == "town" ? TownGround(x, z, feetY) : null;
            // The original wraps ctx.goto so what is in hand is dropped (where you stand) before every level change:
            // the context's engine runs DropHeld before Enter.
            var ctxEngine = Engine is ICineFx ? new DirectorEngineFx(Engine, this) : new DirectorEngine(Engine, this);
            Ctx = new StoryContext(Rules, () => G, g => G = g, Facts, ctxEngine, Hud, Ui, Audio, this, StorySettings);
            Ctx.EventsChanged = AnnounceArchive;
            // The 서연 interlude swaps who you are; the engine keeps one controller, so swap the model it drives.
            Ctx.SetPlayer = preset => { Pov = preset; Engine.SetPlayerPreset(preset); ApplyBody(); };
            // 304호 어르신's slow steps (ElderPovScene): walking only while she is the player.
            Ctx.WalkOnly = on => { if (_walkOnly == on) return; _walkOnly = on; ApplyBody(); };
            // An optional scene (ElderPovScene) is left out of the chapter time; a pause inside it is not counted twice.
            Ctx.BeginUntimed = () =>
            {
                if (_untimed++ == 0) _untimedT0 = NowMs();
                bool ended = false;
                return () =>
                {
                    if (ended) return;
                    ended = true;
                    if (--_untimed == 0) _pausedMs += NowMs() - _untimedT0;
                };
            };

            _danger = parts.MakeDanger?.Invoke(Ctx, Hurt);
            _echo = parts.MakeEcho?.Invoke(Ctx, Store);
            // Cutscenes: the director, a harmless blast for them, and time skips shown as time passing over the town.
            Ctx.Cine = parts.MakeCine?.Invoke(Ctx);
            Ctx.Boom = (x, z, size) => _danger?.Trigger("boom", x, z, size);
            if (parts.Timelapse != null)
            {
                var tl = parts.Timelapse;
                Ctx.SkipOverride = async (from, to, text) =>
                {
                    try { await tl(Ctx, from, to, text); }
                    catch (StoryFail) { throw; }
                    catch (Exception err) { Log?.Invoke("[cine] time-lapse failed; showing the card", err); throw; }
                };
            }
            Ctx.RecordRun = () => { RecordRun(); return Task.CompletedTask; };
            Ctx.PostQuiz = OfferPostQuiz;
            Ctx.PostQuizPending = PostQuizPending;
            Ctx.Speak = SpeakIf;
            // the host's dialogue box and cards read themselves in low vision (UnityStoryHost hands talks and cards to its StoryOverlay)
            Ctx.UiReadsAloud = Ui is AiOn.UI.StoryOverlay || Ui is UnityStoryHost;
            Ctx.MindMeter = on => { if (_rawHud is AiOn.UI.StoryOverlay so) so.SetMindMode(on); };
            // settings.view after 서연's re-read: the original read ctx.settings.view; StorySettings has none, so SeoyeonRelay
            // asks through its hook, and this director answers for its own context (another context gets SeoyeonRelay's default).
            var prevView = SeoyeonRelay.ViewSetting;
            SeoyeonRelay.ViewSetting = c => ReferenceEquals(c, Ctx) ? View : prevView?.Invoke(c);

            Engine.Frame += dt => _frameMs += dt * 1000;
            Engine.Frame += LightFrame;
            Engine.Frame += MapFrame;
            Engine.Frame += GuardianFrame;
            Engine.Nearest += p => { if (p != null) SpeakIf(p.Label); };
            Engine.Land += OnLand;
            // The only Key / Blocked subscription (app.js engine.onKey has one handler): a second one on the same host made
            // F switch the light on and straight off again and X use two first-aid kits.
            Engine.Blocked += b => Observe(OnBlocked(b), "blocked");
            Engine.Key += code => Observe(OnKey(code), "key " + code);

            ApplySettings();
        }

        async void Observe(Task t, string what)
        {
            try { await t; }
            catch (Exception e) { Log?.Invoke("[app] " + what, e); }
        }

        // ------------------------------------------------------------------ settings and profiles

        public static JObject DefaultSettings(bool reduceMotion = false) => new JObject
        {
            ["quality"] = "auto", ["volume"] = 0.7, ["mute"] = false, ["captions"] = true, ["reduceMotion"] = reduceMotion, ["textScale"] = 1, ["view"] = "third",
            ["sensitivity"] = 1, ["minimap"] = true, ["volAmbience"] = 1, ["volEffects"] = 1, ["volUi"] = 1, ["profile"] = "none", ["memoryCue"] = true,
        };

        /// <summary>The experience profile in force ('none' for anything unknown).</summary>
        public string Profile => Settings["profile"]?.Type == JTokenType.String && Profiles.ContainsKey((string)Settings["profile"]) ? (string)Settings["profile"] : "none";

        bool HasLeg => G?.Injury != null && G.Injury.Contains("leg");

        /// <summary>Merges new values (e.g. the settings screen's result) and applies them.</summary>
        public void UpdateSettings(JToken values)
        {
            if (values is JObject o) foreach (var p in o.Properties()) Settings[p.Name] = p.Value.DeepClone();
            ApplySettings();
        }

        public void ApplySettings()
        {
            var p = Profiles[Profile];
            StorySettings.Captions = Truthy(Settings["captions"]);
            StorySettings.Profile = Profile == "none" ? "" : Profile;
            StorySettings.MemoryCue = Settings["memoryCue"]?.Type != JTokenType.Boolean || (bool)Settings["memoryCue"];
            StorySettings.ReduceMotion = Truthy(Settings["reduceMotion"]);
            // competition upgrade: 쉬운 한국어 and 도우미 모드 are read where text is shown (StoryOverlay, UiKit, CompanionPanel)
            AiOn.Game.EasyKorean.On = Truthy(Settings[AiOn.Game.EasyKorean.SettingKey]);
            AiOn.Game.Companion.On = Truthy(Settings[AiOn.Game.Companion.SettingKey]);
            Engine.SetView(View);
            ApplyHostSettings?.Invoke((JObject)Settings.DeepClone(), Body(p));
            AiOn.UI.UiKeys.OtherPov = OtherPov; AiOn.UI.UiKeys.WalkOnly = _walkOnly;
            Ctx.ProfileSpeed = p.Speed;
            if (G != null) Ctx.Refresh();
            Store.Set(SettingsKey, Settings.ToString(Formatting.None));
        }

        /// <summary>
        /// The frame-rate governor stepped down one tier (engine onQualityChange): keep it, and say so once, calmly.
        /// tierLabel: the tier's Korean name (TIER_LABELS[q][0]); null shows the tier id.
        /// </summary>
        public void OnQualityChange(string quality, string tierLabel, bool auto)
        {
            Settings["quality"] = quality; Store.Set(SettingsKey, Settings.ToString(Formatting.None));
            if (auto) Hud.Toast($"화면이 자주 멈춰서 그래픽을 '{tierLabel ?? quality}'(으)로 낮췄어요. 설정에서 다시 바꿀 수 있어요.", "self", 6000);
        }

        /// <summary>
        /// The body the host applies: the profile's, a broken leg ends running for the rest of the run, someone else's slow
        /// steps (WalkOnly) neither run nor jump, and 정민's hands (G, Q) are his only.
        /// </summary>
        ExperienceProfile Body(ExperienceProfile p) => p.With(p.Run && !HasLeg && !_walkOnly, p.Jump && !_walkOnly, !OtherPov);

        /// <summary>The body again after whose body or pace changed (SetPlayer, WalkOnly), with the key strip to match.</summary>
        void ApplyBody()
        {
            var p = Profiles[Profile];
            ApplyHostSettings?.Invoke((JObject)Settings.DeepClone(), Body(p));
            AiOn.UI.UiKeys.OtherPov = OtherPov; AiOn.UI.UiKeys.WalkOnly = _walkOnly;
        }

        bool _walkOnly;
        int _untimed;
        double _untimedT0;

        /// <summary>The keys that are 정민's own (his bag, phone, notebook, first aid, chalk, light): someone else's turn leaves them.</summary>
        public static readonly string[] PovHeldKeys = { "F", "Tab", "I", "X", "B", "P" };

        /// <summary>What one of 정민's own keys says while someone else is the player.</summary>
        public static string PovKeysLine(string pov) =>
            $"지금은 {(pov == "elder" ? "304호 어르신" : pov == "seoyeon" ? "서연" : "다른 사람")}의 시간이에요. 정민의 불빛·소지품·휴대폰·수첩은 정민으로 돌아가면 쓸 수 있어요.";

        string View => Settings["view"]?.Type == JTokenType.String ? (string)Settings["view"] : "third";

        void SpeakIf(string text)
        {
            if (Profile != "lowvision" || text == null) return;
            try { Speak?.Invoke(text); } catch { /* no voice */ }
        }

        // ------------------------------------------------------------------ the body: falls

        /// <summary>Health off (falls, blasts, debris, fire); why becomes the fail card's cause.</summary>
        public void Hurt(double n, string why)
        {
            if (G == null || !(n > 0)) return;
            Ctx.DeathCause = why; Ctx.Stat("health", -n); Hud.Hurt(n / 40);
        }

        void OnLand(double drop)
        {
            Audio.Cue("hit", new JObject { ["kind"] = "land" });
            if (G == null || drop < 3.2) return;
            // Port change (competition upgrade, no injury scenes): the original broke a leg on a drop over 5.5 m (no more
            // running, G.Injury "leg"). Now a hard landing only costs health and says so calmly; old saves keep their leg.
            if (drop > 5.5) Ctx.Toast(HardLandingLine, "self", 5000);
            Hurt(drop > 10 ? 200 : Num.JsRound((drop - 3) * 15), "fall");
        }

        public const string HardLandingLine = "착지가 거칠었다. 숨을 고르자. 높은 곳에서는 계단이나 사다리로 내려가자.";

        /// <summary>X: the first-aid kit (in the bag, or at home) helps you recover some health.</summary>
        public void FirstAid()
        {
            if (G == null) return;
            G.Items.TryGetValue("first_aid", out var e);
            bool atHome = Engine.LevelId == "apartment";
            if (e == null || !(e.Bag > 0 || (atHome && e.Home > 0))) { Ctx.Toast("쓸 수 있는 구급함이 없다."); return; }
            if (G.Stats.Health >= 95) { Ctx.Toast(FirstAidNotNeeded); return; }
            if (e.Bag > 0) e.Bag--; else e.Home--;
            e.Used++; Ctx.Stat("health", 35); Ctx.Event("first_aid", new JObject()); Ctx.Toast(FirstAidUsed, "self");
        }

        public const string FirstAidNotNeeded = "지금은 구급함이 필요하지 않다.", FirstAidUsed = "구급함을 열어 몸을 살피고 잠시 쉬었다. 조금 낫다.";

        // ------------------------------------------------------------------ global keys

        /// <summary>A UI key ('KeyF' or 'F', 'Tab', 'Escape'...). Menus open only during free roam.</summary>
        public async Task OnKey(string code)
        {
            if (G == null || !Engine.ControlsEnabled || code == null) return;
            var k = code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4 ? code.Substring(3) : code;
            if (Ctx.HandsFull && (k == "V" || k == "B" || k == "X")) return; // the extinguisher in both hands, first person
            if (OtherPov && Array.IndexOf(PovHeldKeys, k) >= 0)
            {
                // 서연's or 어르신's turn: 정민's light, bag, phone, notebook, first aid and chalk are not theirs to use
                if (Ctx.Roaming) { var line = PovKeysLine(Pov); Hud.Toast(line, "self", 3600); SpeakIf(line); }
                return;
            }
            if (k == "F") ToggleFlashlight();
            else if (k == "V") ToggleView();
            else if (!Ctx.Roaming) return;
            else if (k == "Tab") await OpenNotebook();
            else if (k == "I") await OpenInventory();
            else if (k == "H") await Ctx.Modal(() => ControlsCard());
            else if (k == "X") FirstAid();
            else if (k == "B") { if (_echo != null && !DemoRun) await _echo.Chalk(); } // a prepared-state run leaves no marks in the player's own record
            else if (k == "P") await Ctx.Modal(() => OpenPhone());
            else if (k == "M") await OpenMap();
            else if (k == "Escape") await OpenPause();
        }

        public void ToggleFlashlight()
        {
            if (G == null) return;
            if (!_flashOn) Relight();
            bool lit = Ctx.LightSource == "flashlight" ? Rules.Usable(G, "flashlight") : Ctx.LightSource == "phone" && G.Phone.Battery > 0;
            if (!_flashOn && !lit) { Ctx.Toast("켤 수 있는 불빛이 없다."); return; }
            SetLight(!_flashOn);
        }

        public void ToggleView()
        {
            Settings["view"] = View == "first" ? "third" : "first";
            Store.Set(SettingsKey, Settings.ToString(Formatting.None));
            Engine.SetView(View);
        }

        public Task OpenNotebook() => Ctx.Modal(() => Ui.Open("notebook", new JObject { ["entries"] = JArray.FromObject(G.Info) }));

        public Task OpenInventory() => Ctx.Modal(() => Ui.Open("inventory", InventoryOptions()));

        JObject InventoryOptions()
        {
            var items = JObject.FromObject(G.Items);
            var catalog = new JObject();
            foreach (var d in Rules.Db.items.Where(d => d != null && d.id != null))
                catalog[d.id] = new JObject
                {
                    ["name"] = d.displayName, ["unit"] = d.unit, ["price"] = d.price, ["weightKg"] = d.weightKg, ["volumeL"] = d.volumeL,
                    ["category"] = d.category.ToString().ToLowerInvariant(), ["cells"] = new JArray(d.cells.x, d.cells.y),
                };
            var load = Rules.BagLoad(G);
            var cells = Rules.BagCells(G);
            return new JObject
            {
                ["home"] = items, ["bag"] = items.DeepClone(), ["catalog"] = catalog,
                ["load"] = new JObject { ["weightKg"] = load.WeightKg, ["volumeL"] = load.VolumeL, ["over"] = new JObject { ["weight"] = load.OverWeight, ["volume"] = load.OverVolume } },
                ["cells"] = new JObject { ["used"] = cells.Used, ["total"] = cells.Total, ["fits"] = cells.Fits },
                ["limit"] = new JObject { ["weightKg"] = Rules.Db.bagLimitKg, ["volumeL"] = Rules.Db.bagLimitL },
            };
        }

        public Task OpenMap() => Ctx.Modal(() => Ui.Open("routemap", new JObject
        {
            ["mode"] = "view", ["from"] = NearestNode(), ["to"] = "shelter", ["options"] = new JArray(), ["closures"] = KnownClosures(), ["gates"] = new JObject(),
            ["known"] = new JObject { ["closures"] = new JArray() },
            ["playerAt"] = Engine.LevelId == "town" ? new JObject { ["x"] = Engine.PlayerX, ["z"] = Engine.PlayerZ } : JValue.CreateNull(),
        }));

        /// <summary>The controls card (start of a new game, and H while roaming).</summary>
        public Task<string> ControlsCard() => Ui.Card(new CardOptions
        {
            Kind = "info", Title = "조작법", Meta = "언제든 H 키로 다시 볼 수 있습니다",
            Body = Keys.Select(x => $"{x.key} — {x.text}").ToList(), Actions = new List<CardAction> { new("ok", "확인", true) },
        });

        /// <summary>The map shows closures in force now, and only once the player has learned of them this phase.</summary>
        public JArray KnownClosures()
        {
            bool knows = G.Chapter == "evac" ? G.Knows("evacClosuresKnown") : G.Knows("closureSeen") || G.Knows("closureHeard");
            if (!knows) return new JArray();
            return Ctx.LevelStates.TryGetValue("town", out var t) && t.TryGetValue("closures", out var c) && c is JArray a ? (JArray)a.DeepClone() : new JArray();
        }

        public string NearestNode()
        {
            if (Engine.LevelId != "town") return "home";
            double x = Engine.PlayerX, z = Engine.PlayerZ;
            return TownMap.Nodes.OrderBy(kv => TownMap.Dist(kv.Value.Pos.X, kv.Value.Pos.Z, x, z)).First().Key;
        }

        /// <summary>P: the phone (recent official texts, neighbourhood posts, the FM radio app).</summary>
        public async Task OpenPhone()
        {
            if (G.Phone.Battery <= 0) { Ctx.Toast("휴대폰이 꺼져 있다."); return; }
            var messages = new JArray();
            foreach (var i in G.Info.Where(i => i.Label == "official").Reverse().Take(4).Reverse())
                messages.Add(new JObject { ["from"] = "알림", ["kind"] = "safety", ["time"] = "", ["text"] = i.Text });
            // Posts from the neighbourhood chat that reached 정민 (game/rumors.js), to reread next to the official texts.
            var seen = Rumors.SeenRumors(G);
            foreach (var r in seen.Skip(Math.Max(0, seen.Count - 3)))
                messages.Add(new JObject { ["from"] = r.Claim.From, ["kind"] = "sns", ["time"] = r.Claim.Meta, ["text"] = r.Claim.Text });
            // 휴대폰 배터리 자원: in the blackout reading the texts costs a little, the chat and the FM app more (and wait at
            // low battery), and 절전 모드 can be switched here (PhoneBattery)
            bool blackout = PhoneBattery.Live && PhoneBattery.Blackout(G.Chapter, Ctx.Power);
            if (blackout) { StoryCommon.DrainBattery(G, PhoneBattery.Sip(G.Phone.Battery, PhoneBattery.Cost(G, "text"))); Ctx.Refresh(); } // never the last percent
            var actions = new JArray();
            if (!blackout) actions.Add(new JObject { ["id"] = "radio", ["label"] = "FM 라디오 앱" });
            else
            {
                if (PhoneBattery.Extras(G.Phone.Battery))
                {
                    actions.Add(new JObject { ["id"] = "sns", ["label"] = $"단톡방 보기 −{PhoneBattery.Cost(G, "sns")}%" });
                    actions.Add(new JObject { ["id"] = "radio", ["label"] = $"FM 라디오 −{PhoneBattery.Cost(G, "fm")}%" });
                }
                // 절전 모드 켜기 is the suggested press; once it is on, 끄기 comes after 닫기 so it is never the first one
                if (!PhoneBattery.Saver(G)) actions.Add(new JObject { ["id"] = "saver", ["label"] = "절전 모드 켜기", ["primary"] = true });
            }
            actions.Add(new JObject { ["id"] = "close", ["label"] = "닫기" });
            if (blackout && PhoneBattery.Saver(G)) actions.Add(new JObject { ["id"] = "saver", ["label"] = "절전 모드 끄기" });
            var pick = Str(await Ui.Open("phone", new JObject
            {
                ["battery"] = G.Phone.Battery, ["signal"] = string.IsNullOrEmpty(G.Phone.Signal) ? "ok" : G.Phone.Signal, ["messages"] = messages,
                ["actions"] = actions,
            }));
            if (pick == "radio") await StoryCommon.RadioScene(Ctx, StoryCommon.CurrentBroadcast(G), "휴대폰", "phone");
            else if (pick == "sns") await ScrollChat();
            else if (pick == "saver")
            {
                bool on = !PhoneBattery.Saver(G);
                bool first = on && !G.Events.Any(e => e.Type == PhoneBattery.Event && JsSem.Is(e.Details?["what"], "saver"));
                PhoneBattery.SetSaver(G, on);
                Ctx.Toast(on ? PhoneBattery.SaverOnLine : PhoneBattery.SaverOffLine, "self", 3600);
                if (first) await StoryCommon.FactCard(Ctx, "배터리 아끼기", new[] { PhoneBattery.FactId }, note: false); // a US source: a notice, not a 안전안내문자 in the phone
            }
        }

        /// <summary>P › 단톡방 보기 in the blackout: the day's chat, at the chat's battery cost (PhoneBattery "sns").</summary>
        async Task ScrollChat()
        {
            var cost = PhoneBattery.Use(G, "sns");
            Ctx.Refresh();
            await Ui.Talk(new TalkOptions
            {
                Speaker = "103동 단톡방", Role = $"알림 {(30 + G.Day * 17).ToString(System.Globalization.CultureInfo.InvariantCulture)}개 · 휴대폰",
                Lines = PhoneBattery.ChatLines(G.Day).ToList(),
            });
            Ctx.Toast(PhoneBattery.UseLine("sns", cost, G.Phone.Battery) + " · 새로 확인된 소식은 없다.", "self", 4200);
        }

        /// <summary>Escape: the pause menu (settings, save, lounge, back to the title). Its time is left out of chapter times.</summary>
        public async Task OpenPause()
        {
            double p0 = NowMs();
            var pick = Str(await Ctx.Modal(() => Ui.Open("pause", new JObject())));
            if (pick == "lounge") await Ctx.Modal(() => OpenLounge());
            if (_untimed == 0) _pausedMs += NowMs() - p0; // inside an untimed scene the whole scene is left out already
            if (pick == "settings") { var r = await Ctx.Modal(() => Ui.Open("settings", (JObject)Settings.DeepClone())); UpdateSettings(r); }
            else if (pick == "save")
            {
                if (DemoRun) { Ctx.Toast(DemoNoSaveText); return; } // a prepared run's slot is wiped when it ends: keeping nothing, say so
                if (G?.Checkpoint != null) SaveNow(); Ctx.Toast("마지막 체크포인트를 저장했습니다.");
            }
            else if (pick == "menu") { if (DemoRun) Store.Set(SceneSelect.SaveKey, ""); MenuRequested?.Invoke(); }
        }

        public const string DemoNoSaveText = "장면 바로가기와 교사용 모드에서는 저장되지 않습니다.";

        /// <summary>The lounge; the best run is what "내 최고 기록 알리기" posts.</summary>
        public Task<JToken> OpenLounge()
        {
            var best = Challenges.ReadRecords(Store.Get(RecordsKey)).Runs.FirstOrDefault();
            return Ui.Open("lounge", new JObject { ["name"] = LoungeName(), ["best"] = best != null ? JObject.FromObject(best) : JValue.CreateNull() });
        }

        /// <summary>The lounge name is made once per player and kept in the store.</summary>
        public string LoungeName()
        {
            var name = Store.Get(LoungeNameKey);
            if (!Lounge.IsName(name)) { name = Lounge.NewName(Random); Store.Set(LoungeNameKey, name); }
            return name;
        }

        // ------------------------------------------------------------------ light in hand

        /// <summary>
        /// Before switching the light on: a flashlight that ran down takes a spare battery pack if there is one,
        /// otherwise the phone's light stands in; a flashlight made usable later replaces the phone.
        /// </summary>
        public void Relight()
        {
            if (Ctx.LightSource == null) return; // no light in hand before the blackout
            G.Items.TryGetValue("flashlight", out var fl);
            bool has = (fl?.Home ?? 0) + (fl?.Bag ?? 0) > 0;
            if (has && !Rules.Usable(G, "flashlight") && fl.Charge == 0 && Rules.Check(G, "flashlight").Ok)
                Ctx.Toast("다 닳은 건전지를 빼고 손전등에 새 건전지를 넣었다.", "self", 4200);
            var next = StoryCommon.LightSource(Ctx);
            if (next != Ctx.LightSource && next != null)
            {
                if (Ctx.LightSource == "flashlight") Ctx.Toast("손전등 건전지가 다 닳았다. 휴대폰 불빛을 켠다.", "self", 4200);
                Ctx.LightSource = next;
            }
        }

        // The light in hand runs down: the flashlight 1 % of its charge per real minute lit (design M3), the phone 1 % per 20 s.
        void LightFrame(double dt)
        {
            // Port upgrade: walking into a lit level (power on, or the shelter's emergency lights) puts the light away.
            // Checked when the level changes and when its power does (the shelter's lights come on after the arrival).
            var lv = Engine.LevelId;
            bool lit = LitLevel(Ctx.LevelStates, lv, Ctx.Power);
            if (lv != _lightLevel || lit != _lightLit)
            {
                _lightLevel = lv; _lightLit = lit;
                if (_flashOn && G != null && lit)
                {
                    SetLight(false);
                    Ctx.Toast(Ctx.LightSource == "phone" ? "휴대폰 손전등을 껐다" : "손전등을 껐다", "self", 2400);
                }
            }
            if (!_flashOn || G == null) return;
            if (LightPaused) return;
            if (Ctx.LightSource == "flashlight")
            {
                if ((_litT += dt) < 60) return;
                _litT -= 60;
                if (!G.Items.TryGetValue("flashlight", out var fl) || !fl.Checked || Rules.DrainCharge(G, "flashlight", 1) > 0) return;
                SetLight(false);
                bool spare = new[] { "batteries_aa", "batteries_old" }.Any(id => G.Items.TryGetValue(id, out var e) && e.Home + e.Bag > 0);
                Ctx.Toast($"손전등 불빛이 점점 흐려지더니 꺼졌다. 건전지가 다 닳았다.{(spare ? " 남은 건전지가 있으니 F를 누르면 갈아 끼운다." : "")}", "self", 5600);
                return;
            }
            if (Ctx.LightSource != "phone") return;
            if ((_flashT += dt) > 20)
            {
                _flashT = 0; G.Phone.Battery = Math.Max(0, G.Phone.Battery - 1); Hud.SetBattery(G.Phone.Battery);
                if (G.Phone.Battery <= 0) { SetLight(false); Ctx.Toast("휴대폰이 꺼졌다."); }
            }
        }

        // ------------------------------------------------------------------ per frame: compass and minimap, the guardian

        void MapFrame(double dt)
        {
            if (G == null) return;
            if ((_mapT -= dt) > 0) return;
            _mapT = 0.066;
            Hud.SetCompass(-Engine.PlayerYaw * 180 / Math.PI);
            bool minimap = Settings["minimap"]?.Type != JTokenType.Boolean || (bool)Settings["minimap"];
            if (!minimap || Engine.LevelId == null) { Hud.SetMap(null); return; }
            // walk / solids (the level's floor plan) are the host's: it adds them when it draws.
            // One frame object, updated in place: 15 times a second a fresh JObject per point was steady garbage.
            var pts = Engine.Points;
            if (_mapFrame == null || _mapPts.Count != pts.Count)
            {
                _mapPts = new JArray();
                for (int i = 0; i < pts.Count; i++)
                    _mapPts.Add(new JObject { ["id"] = null, ["kind"] = null, ["label"] = null, ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0, ["enabled"] = true });
                _mapFrame = new JObject { ["level"] = null, ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0, ["yaw"] = 0.0, ["points"] = _mapPts, ["scale"] = 9 };
            }
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var o = (JObject)_mapPts[i];
                SetIn(o, "id", p.Id); SetIn(o, "kind", p.Kind); SetIn(o, "label", p.Label);
                SetIn(o, "x", p.X); SetIn(o, "y", p.Y); SetIn(o, "z", p.Z); SetIn(o, "enabled", p.Enabled);
            }
            SetIn(_mapFrame, "level", Engine.LevelId);
            SetIn(_mapFrame, "x", Engine.PlayerX); SetIn(_mapFrame, "y", Engine.PlayerY); SetIn(_mapFrame, "z", Engine.PlayerZ); SetIn(_mapFrame, "yaw", Engine.PlayerYaw);
            SetIn(_mapFrame, "scale", Engine.LevelId == "town" ? 2.0 : 9.0);
            Hud.SetMap(_mapFrame);
        }

        JObject _mapFrame;
        JArray _mapPts;

        // In-place value updates that only box when the value changed.
        static void SetIn(JObject o, string key, string v) { var j = (JValue)o[key]; if ((string)j.Value != v) j.Value = v; }
        static void SetIn(JObject o, string key, double v) { var j = (JValue)o[key]; if (!(j.Value is double d) || d != v) j.Value = v; }
        static void SetIn(JObject o, string key, bool v) { var j = (JValue)o[key]; if (!(j.Value is bool b) || b != v) j.Value = v; }

        // 지체 장애 mode: a guardian walks a step behind, to the right.
        void GuardianFrame(double dt)
        {
            // someone else's turn (서연, 304호 어르신) has its own company: the guardian waits for 정민
            bool on = G != null && Profile == "guardian" && Engine.LevelId != null && !OtherPov;
            if (!on) { if (_buddy != null) _buddy.Visible = false; return; }
            double yaw = Engine.PlayerYaw, s = Math.Sin(yaw), c = Math.Cos(yaw), py = Engine.PlayerY;
            double tx = Engine.PlayerX + s * 1.3 + c * 0.6, tz = Engine.PlayerZ + c * 1.3 - s * 0.6; // behind (+sin, +cos) and right (cos, -sin)
            bool place = false;
            if (_buddy == null || _buddyLevel != Engine.LevelId)
            {
                // The host's people belong to a level, so a new level gets its own guardian, put next to you (the original teleports its one model).
                try { _buddy = Engine.SpawnPerson("crowd", tx, py, tz, yaw); }
                catch (Exception err) { Log?.Invoke("[app] guardian model failed", err); _buddy = null; }
                if (_buddy == null) return;
                _buddyLevel = Engine.LevelId; _buddyAnim = ""; _buddySpeed = null;
                place = true;
            }
            _buddy.Visible = true;
            double bx = _buddy.X, bz = _buddy.Z, ry = double.NaN;
            double dx = tx - bx, dz = tz - bz, d = Math.Sqrt(dx * dx + dz * dz);
            bool moving = false;
            if (place || d > 8) { bx = tx; bz = tz; _buddyY = py; }
            else if (d > 0.25)
            {
                var v = Math.Min(d * 2.4, 4.2) * dt;
                bx += dx / d * v; bz += dz / d * v; ry = Math.Atan2(-dx, -dz);
                moving = true;
            }
            double? ground = null;
            try { ground = GroundAt?.Invoke(bx, bz, _buddyY + 0.3); } catch (Exception err) { Log?.Invoke("[app] guardian ground failed", err); }
            _buddyY += ((ground ?? py) - _buddyY) * Math.Min(1, dt * 12);
            if (double.IsNaN(ry)) _buddy.SetPosition(bx, _buddyY, bz); else _buddy.SetPosition(bx, _buddyY, bz, ry);
            var anim = moving ? "walk" : "idle";
            double speed = moving ? Math.Min(d * 2.4, 4.2) : 0;
            // buddy.setSpeed every frame: the host takes the same clip with a new speed as a pace change, not a restart.
            if (anim != _buddyAnim || speed != _buddySpeed) { _buddyAnim = anim; _buddySpeed = speed; _buddy.SetAnim(anim, speed); }
        }

        /// <summary>The original groundAt(walk, x, z, feetY, false, 1.5) over the town's walk zones.</summary>
        public static double? TownGround(double x, double z, double feetY)
        {
            double? best = null;
            foreach (var w in StoryCine.TownWalk)
            {
                if (x < w.X0 - 0.02 || x > w.X1 + 0.02 || z < w.Z0 - 0.02 || z > w.Z1 + 0.02) continue;
                double y = w.Y;
                if (w.Axis != null)
                {
                    double span = w.Axis == "x" ? w.X1 - w.X0 : w.Z1 - w.Z0;
                    if (span == 0 || double.IsNaN(span)) span = 1;
                    double k = Math.Min(1, Math.Max(0, (w.Axis == "x" ? x - w.X0 : z - w.Z0) / span));
                    y = w.Y0 + (w.Y1 - w.Y0) * k;
                }
                if (y > feetY + 0.45 || y < feetY - 1.5) continue;
                if (best == null || y > best) best = y;
            }
            return best;
        }

        // ------------------------------------------------------------------ wheelchair: a stair flight is a wall; ask for help

        static bool Steep(JObject w)
        {
            if (w?["ramp"] is not JObject r) return false;
            bool alongX = (string)r["axis"] == "x";
            double len = alongX ? N(w["x1"]) - N(w["x0"]) : N(w["z1"]) - N(w["z0"]);
            if (len == 0 || double.IsNaN(len)) len = 1; // (span || 1)
            return Math.Abs(N(r["y1"]) - N(r["y0"])) / len > 0.2;
        }

        /// <summary>
        /// The player was stopped. For the wheelchair profile the host sends { kind, x, z, toX, toZ, walk: [zones] } where
        /// each zone is the level's walk area { x0, x1, z0, z1, ramp: { axis, y0, y1 } } (or one zone as 'zone').
        /// </summary>
        public async Task OnBlocked(JObject b)
        {
            if (G == null || !Ctx.Roaming || _helpBusy || Profile != "wheelchair" || b == null) return;
            var zones = (b["walk"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            if (b["zone"] is JObject z1) zones.Add(z1);
            if (zones.Count == 0 && LevelWalk != null) zones.AddRange(LevelWalk() ?? Array.Empty<JObject>()); // engine.level.walk
            double toX = N(b["toX"]), toZ = N(b["toZ"]);
            var w = zones.FirstOrDefault(z => Steep(z) && toX >= N(z["x0"]) && toX <= N(z["x1"]) && toZ >= N(z["z0"]) && toZ <= N(z["z1"]));
            if (w == null) return;
            _helpBusy = true;
            try
            {
                bool recent = NowMs() - _helpedAt < 90000; // the same helper keeps going down the next flights
                if (!recent)
                {
                    var pick = await Ctx.Modal(() => Ui.Talk(new TalkOptions
                    {
                        Speaker = "계단", Role = "휠체어로는 혼자 이용할 수 없다",
                        Lines = new List<string> { "계단 앞이다. 휠체어로는 오르내릴 수 없다.", "주변 사람에게 도움을 청할까? 시간이 걸린다. 경사로나 엘리베이터가 있다면 그쪽이 낫다." },
                        Choices = new List<TalkChoice> { new("help", "도움을 청한다"), new("no", "다른 길을 찾는다") },
                    }));
                    if (pick != "help") return;
                }
                var r = (JObject)w["ramp"];
                bool alongX = (string)r["axis"] == "x";
                double lo = alongX ? N(w["x0"]) : N(w["z0"]), hi = alongX ? N(w["x1"]) : N(w["z1"]), cur = alongX ? N(b["x"]) : N(b["z"]);
                bool up = Math.Abs(cur - lo) < Math.Abs(cur - hi);
                double end = up ? hi + 0.45 : lo - 0.45, y = up ? N(r["y1"]) : N(r["y0"]), mid = alongX ? (N(w["z0"]) + N(w["z1"])) / 2 : (N(w["x0"]) + N(w["x1"])) / 2;
                if (alongX) Engine.TeleportTo(end, y, mid, Engine.PlayerYaw); else Engine.TeleportTo(mid, y, end, Engine.PlayerYaw);
                _helpedAt = NowMs(); Ctx.Pass(2);
                Ctx.Toast(recent ? "도움을 받아 다음 계단도 옮겼다." : "이웃 두 사람이 휠체어를 함께 들어 계단을 옮겨 주었다.", "neighbor");
            }
            finally { _helpBusy = false; }
        }

        // ------------------------------------------------------------------ save, checkpoint, snapshot

        /// <summary>Runtime state a checkpoint needs besides G; it is saved with G so a reload resumes the same world.</summary>
        public JObject Snapshot()
        {
            var ls = new JObject();
            foreach (var kv in Ctx.LevelStates)
            {
                var o = new JObject();
                foreach (var s in kv.Value) o[s.Key] = s.Value?.DeepClone() ?? JValue.CreateNull();
                ls[kv.Key] = o;
            }
            return new JObject
            {
                ["levelStates"] = ls, ["power"] = Ctx.Power, ["outdoorEnv"] = Ctx.OutdoorEnv, ["indoorEnv"] = Ctx.IndoorEnv,
                ["lightSource"] = Ctx.LightSource != null ? (JToken)Ctx.LightSource : JValue.CreateNull(),
                ["trail"] = new JArray((Ctx.Trail ?? new List<JObject>()).Select(t => t.DeepClone())),
                ["seoyeonAsk"] = Ctx.SeoyeonAsk != null ? (JToken)Ctx.SeoyeonAsk : JValue.CreateNull(),
            };
        }

        /// <summary>A saved snapshot comes from storage: keep only known keys with sane values.</summary>
        public static JObject ReadSnap(JToken s)
        {
            var o = s as JObject ?? new JObject();
            var levels = new JObject();
            var outp = new JObject { ["levelStates"] = levels };
            if (o["levelStates"] is JObject ls)
                foreach (var p in ls.Properties()) if (Array.IndexOf(LevelIds, p.Name) >= 0 && p.Value is JObject v) levels[p.Name] = v.DeepClone();
            if (o["power"]?.Type == JTokenType.Boolean) outp["power"] = o["power"].DeepClone();
            foreach (var k in new[] { "outdoorEnv", "indoorEnv" }) if (o[k]?.Type == JTokenType.String && Envs.Contains((string)o[k])) outp[k] = o[k].DeepClone();
            if (o["lightSource"]?.Type == JTokenType.String && ((string)o["lightSource"] == "flashlight" || (string)o["lightSource"] == "phone")) outp["lightSource"] = o["lightSource"].DeepClone();
            if (o["trail"] is JArray tr)
            {
                var ok = tr.OfType<JObject>().Where(p => Finite(p["x"]) && Finite(p["z"])).ToList();
                outp["trail"] = new JArray(ok.Skip(Math.Max(0, ok.Count - 2000)).Select(p => p.DeepClone()));
            }
            if (o["seoyeonAsk"]?.Type == JTokenType.String && ((string)o["seoyeonAsk"] == "yes" || (string)o["seoyeonAsk"] == "no")) outp["seoyeonAsk"] = o["seoyeonAsk"].DeepClone();
            return outp;
        }

        void ApplySnap(JObject s)
        {
            Ctx.LevelStates = new Dictionary<string, Dictionary<string, JToken>>();
            if (s["levelStates"] is JObject ls)
                foreach (var p in ls.Properties())
                    Ctx.LevelStates[p.Name] = ((JObject)p.Value).Properties().ToDictionary(x => x.Name, x => x.Value.DeepClone());
            if (s["power"] != null) Ctx.Power = (bool)s["power"];
            if (s["outdoorEnv"] != null) Ctx.OutdoorEnv = (string)s["outdoorEnv"];
            if (s["indoorEnv"] != null) Ctx.IndoorEnv = (string)s["indoorEnv"];
            if (s["lightSource"] != null) Ctx.LightSource = (string)s["lightSource"];
            if (s["trail"] is JArray t) Ctx.Trail = t.OfType<JObject>().Select(x => (JObject)x.DeepClone()).ToList();
            if (s["seoyeonAsk"] != null) Ctx.SeoyeonAsk = (string)s["seoyeonAsk"];
        }

        /// <summary>{ index, G, snap } under SaveKey.</summary>
        public void SaveNow()
        {
            if (G == null) return;
            Store.Set(ActiveSaveKey, new JObject { ["index"] = Index, ["G"] = JObject.Parse(SaveCodec.Save(G)), ["snap"] = Ctx.Snap?.DeepClone() ?? JValue.CreateNull() }.ToString(Formatting.None));
            KeepArchive();
        }

        // ------------------------------------------------------------------ 더 알아보기: 기록관, 번호 카드, 주민

        /// <summary>
        /// Adds what this run has unlocked so far to the stored archive (Archive.Key). Only the player's own runs: a
        /// prepared run (scene select, teacher mode) shows its unlocks while it plays (ArchiveView) but keeps none.
        /// </summary>
        void KeepArchive()
        {
            if (DemoRun || G == null) return;
            try { Store.Set(Archive.Key, Archive.Merge(Archive.Read(Store.Get(Archive.Key)), Archive.Scan(G)).ToJson()); }
            catch (Exception err) { Log?.Invoke("[archive] not saved", err); }
        }

        /// <summary>
        /// The run the archive reads: the one being played, or at the title the player's saved run (이어하기), so a save
        /// made before the archive existed still counts.
        /// </summary>
        GameState ArchiveRun()
        {
            if (G != null) return G;
            try
            {
                var sg = SafeParse(Store.Get(SaveKey))["G"];
                return sg is JObject ? SaveCodec.Load(sg.ToString(Formatting.None), Rules.Db) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The stored archive with the run being played (or the saved one) on top: what the 더 알아보기 screens show.</summary>
        public ArchiveProgress ArchiveView()
        {
            var stored = Archive.Read(Store.Get(Archive.Key));
            var run = ArchiveRun();
            return run != null ? Archive.Merge(stored, Archive.Scan(run)) : stored;
        }

        /// <summary>The player has opened the archive: what it showed is no longer "새로" (stored).</summary>
        public void ArchiveSeen(ArchiveProgress shown)
        {
            if (shown == null) return;
            var stored = Archive.Read(Store.Get(Archive.Key));
            // the player's own run keeps what it showed; a prepared run's unlocks are not the player's, so stay unmarked
            if (!DemoRun && ArchiveRun() is GameState run) stored = Archive.Merge(stored, Archive.Scan(run));
            foreach (var id in shown.Entries.Concat(shown.Cards))
                if (stored.Has(id) && !stored.Viewed.Contains(id)) stored.Viewed.Add(id);
            Store.Set(Archive.Key, stored.ToJson());
        }

        /// <summary>
        /// The chapter the hub should not spoil past: the one being played in the player's own run, or null (the title,
        /// teacher mode and scene select show every hint and the 모두 펼쳐 보기 toggle).
        /// </summary>
        public string ArchiveChapter => G != null && !DemoRun ? G.Chapter : null;

        /// <summary>The hub's options: the progress to show and the chapter not to spoil past.</summary>
        public JObject MoreOptions(ArchiveProgress view) =>
            new JObject { ["progress"] = view.ToJObject(), ["chapter"] = ArchiveChapter != null ? (JToken)ArchiveChapter : JValue.CreateNull() };

        /// <summary>
        /// 더 알아보기 (title, or the guide terminal): the hub over the archive, the map, the calendar and 우리 집 안전 준비.
        /// The hub answers MoreSeen when the player opened the 기록관 in it; only then are its "새로" marks spent.
        /// </summary>
        public async Task<JToken> OpenMore()
        {
            var view = ArchiveView();
            var r = await Ui.Open(Archive.Screen, MoreOptions(view));
            if (JsSem.Is(r, MoreSeen)) ArchiveSeen(view);
            return r;
        }

        /// <summary>The hub's answer when the 기록관 was opened in it.</summary>
        public const string MoreSeen = "seen";

        /// <summary>Unlocks already shown or announced in this run (null until the run starts).</summary>
        HashSet<string> _archiveKnown;

        /// <summary>A run (re)starts: what it and the stored archive already hold is not news.</summary>
        void ArchiveBaseline()
        {
            try { var v = ArchiveView(); _archiveKnown = new HashSet<string>(v.Entries.Concat(v.Cards)); }
            catch (Exception) { _archiveKnown = null; }
        }

        /// <summary>
        /// After the story records something (StoryContext.Refresh): a card or entry the run has just opened gets one
        /// toast, so the 'right channel, then the card' loop shows while playing (prepared runs too; they keep nothing).
        /// </summary>
        void AnnounceArchive()
        {
            if (G == null || _archiveKnown == null) return;
            try
            {
                var run = Archive.Scan(G);
                var fresh = run.Entries.Concat(run.Cards).Where(id => !_archiveKnown.Contains(id)).ToList();
                if (fresh.Count == 0) return;
                foreach (var id in fresh) _archiveKnown.Add(id);
                Ctx.Toast(Archive.Notice(fresh), "self", 5200);
            }
            catch (Exception err) { Log?.Invoke("[archive] notice", err); }
        }

        /// <summary>persist.checkpoint: a retry point (game copy + runtime snapshot), saved at once.</summary>
        public void Checkpoint(string key)
        {
            SaveCodec.MakeCheckpoint(G, key); Ctx.Snap = Snapshot(); SaveNow();
            if (!DemoRun) _hostPersist?.Checkpoint(key);
        }

        public void Save() { SaveNow(); if (!DemoRun) _hostPersist?.Save(); }

        /// <summary>The teacher route's stop for the moment playing (a StoryContext.Reached mark), or null to play the chapter out.</summary>
        string _stopAt;

        /// <summary>Called on every story mark (tests take the scene-select states at marks that are not checkpoints).</summary>
        public Action<string> MarkReached;

        /// <summary>persist.reached: a story mark. During a teacher-mode moment that ends here, the moment is over.</summary>
        public void Reached(string mark)
        {
            MarkReached?.Invoke(mark);
            if (DemoRun && _stopAt != null && mark == _stopAt) throw new MomentDone(mark);
        }

        /// <summary>Thrown by Reached at the teacher route's stop; RunChapters returns MomentResult.</summary>
        public sealed class MomentDone : Exception { public MomentDone(string mark) : base("moment:" + mark) { } }

        /// <summary>What RunChapters returns when a teacher-mode moment reached its stop.</summary>
        public const string MomentResult = "moment";

        // ------------------------------------------------------------------ resets

        /// <summary>Per-scene state that must not leak into a retry or the next run.</summary>
        public void ResetTransient()
        {
            Ctx.SpeedOverride = null; Ctx.Calm = false; Engine.Carrying = null;
            Ctx.MindMeter?.Invoke(false); // 「임시 거처의 하루」's 마음 row, if a retry or the title cut the scene short
            _litT = 0; SetLight(false);
            Ctx.SetPlayer?.Invoke("jeongmin"); Hud.SetCinematic(false); Engine.SetControls(true);
            DropHeld?.Invoke(); _danger?.Reset(); Ctx.DeathCause = null; ApplySettings();
        }

        public void ResetRun(JToken snap)
        {
            Ctx.CueSync(); // first: the HUD refresh below would toast deeds past an old cue mark
            Ctx.BatterySync(); // and the low-battery note would compare with the previous run's battery
            Ctx.LightSource = null; Ctx.SeoyeonAsk = null; Ctx.Trail = null; Ctx.Snap = null;
            Ctx.LevelStates = new Dictionary<string, Dictionary<string, JToken>>();
            Ctx.Power = true; Ctx.OutdoorEnv = "day"; Ctx.IndoorEnv = "interior"; Ctx.Rate = 0;
            if (snap != null && snap.Type != JTokenType.Null) { Ctx.Snap = ReadSnap(snap); ApplySnap(ReadSnap(Ctx.Snap)); }
            // Saves from before the snapshot existed: the light in hand follows from G.
            if (Ctx.LightSource == null && G.Events.Any(e => e.Type == "blackout")) Ctx.LightSource = StoryCommon.LightSource(Ctx);
            ResetTransient();
            ArchiveBaseline();
        }

        // ------------------------------------------------------------------ the run record

        /// <summary>
        /// The ending gallery remembers which endings were reached and the deeds that led there; the records keep
        /// challenges, best chapter times and the top runs. Evac.Ending calls it (ctx.RecordRun) once, before the document.
        /// </summary>
        public void RecordRun()
        {
            if (DemoRun) return; // a run from a prepared state is not this player's run: no gallery, records or challenges
            try
            {
                var fr = Challenges.FinishRun(G, Store.Get(GalleryKey), Store.Get(RecordsKey), Today(), Facts.SourceId);
                Store.Set(GalleryKey, JsonConvert.SerializeObject(fr.Gal));
                Store.Set(RecordsKey, JsonConvert.SerializeObject(fr.Rec));
                GameRules.RecordEvent(G, "run_recorded", JObject.FromObject(fr.News));
                KeepArchive();
            }
            catch (Exception err) { Log?.Invoke("[gallery] not saved", err); }
        }

        // ------------------------------------------------------------------ the chapter runner

        /// <summary>
        /// Plays the chapters from start (resuming inside it at resumeKey). A StoryFail shows the fail card: retry rolls
        /// back to the last checkpoint and replays the chapter (ctx.Resume = that key unless it is the chapter start);
        /// anything else returns 'menu'. Returns the ending's result (e.g. 'restart'), or null past the last chapter.
        /// </summary>
        public async Task<string> RunChapters(int start, string resumeKey, int end = -1)
        {
            int stop = end < 0 || end > Chapters.Count ? Chapters.Count : end; // teacher mode plays one chapter at a time
            AiOn.Game.Companion.RunActive = true; // 도우미 모드's chip and F1 (until the title resets it)
            for (int i = start; i < stop; i++)
            {
                var ch = Chapters[i];
                Index = i;
                Ctx.Resume = resumeKey;
                Hud.SetObjective(null, null, null);
                bool partial = resumeKey != null; // a chapter continued mid-way has no full time
                double t0 = NowMs();
                int retries = 0;
                _pausedMs = 0;
                if (resumeKey == null) Checkpoint(ch.Id);
                resumeKey = null;
                for (;;)
                {
                    StoryFail fail;
                    try
                    {
                        var r = await ch.Run(Ctx);
                        if (Array.IndexOf(Challenges.Timed, ch.Id) >= 0)
                        {
                            var d = new JObject { ["id"] = ch.Id, ["ms"] = Num.JsRound(NowMs() - t0 - _pausedMs), ["retries"] = retries };
                            if (partial) d["partial"] = true;
                            GameRules.RecordEvent(G, "chapter_time", d);
                        }
                        if (ch.Id == "ending") return Str(r);
                        break;
                    }
                    catch (StoryFail err) { fail = err; }
                    catch (MomentDone)
                    {
                        Engine.SetControls(false); Audio.Loop("siren", false); Audio.Loop("fire", false); Hud.SetCinematic(false);
                        return MomentResult;
                    }

                    if (fail.Type == "dead" && !DemoRun) _echo?.Death(JsText(fail.Detail?["why"]));
                    Engine.SetControls(false); Audio.Loop("siren", false); Audio.Loop("fire", false); Hud.SetCinematic(false);
                    var card = Debrief.FailDebrief(Rules, G, fail.Type, fail.Detail, Facts.Text, Facts.SourceId);
                    var o = JObject.FromObject(card);
                    o.Remove("sourceIds");
                    o["sources"] = new JArray((card.SourceIds ?? new List<string>()).Select(Facts.Source).Where(s => s != null).Select(s => JObject.FromObject(s)));
                    var pick = Str(await Ui.Open("failCard", o));
                    if (pick != "retry") { Engine.SetControls(true); return "menu"; }
                    retries++;
                    var key = G.Checkpoint.Key;
                    SaveCodec.Restore(G);
                    ResetRun(Ctx.Snap); // runtime fields the snapshot does not list (seoyeonAsk, lightSource) go too (F5)
                    Ctx.Resume = key == ch.Id ? null : key;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ boot

        /// <summary>Who is who, before the story starts: the opening, then the experience-mode card.</summary>
        public async Task Intro()
        {
            try { if (_opening != null) await _opening(Ctx); }
            catch (StoryFail) { throw; }
            catch (Exception err) { Log?.Invoke("[cine] opening failed", err); }
            Engine.SetControls(false);
            await ProfileCard();
            Ctx.After(1.5, () => Hud.Toast("조작법은 언제든 H 키로 볼 수 있습니다.", "self", 5000));
        }

        /// <summary>The experience-mode card (a new game's intro and the teacher mode's start).</summary>
        public async Task ProfileCard()
        {
            var pick = await Ui.Card(new CardOptions
            {
                Kind = "info", Title = "체험 모드", Meta = "설정(Esc)에서 언제든 바꿀 수 있습니다",
                // the original's opening paragraph only; each mode's own line rides on its chip (the settings subtitles)
                Body = new List<string> { "같은 재난도 몸과 감각에 따라 전혀 다른 길이 됩니다. 정민의 조건을 골라 보세요." },
                Actions = new List<CardAction>
                {
                    new("none", "기본", true), new("mobility", "보행 장애"), new("wheelchair", "휠체어"),
                    new("lowvision", "저시력"), new("deaf", "청각 장애"), new("guardian", "보호자 동반"),
                }.Select(a => { a.Hint = AiOn.UI.SettingsForm.ProfileSub(a.Id); return a; }).ToList(),
            });
            if (pick != null && Profiles.ContainsKey(pick))
            {
                Settings["profile"] = pick;
                if (pick == "deaf") Settings["captions"] = true;
                ApplySettings();
            }
        }

        /// <summary>Loads the town behind the title screen (a failure only moves the load to the opening).</summary>
        public async Task Preload()
        {
            Engine.SetControls(false); // ResetRun turns them back on when a run starts
            try { await Engine.Enter("town", "office", () => Engine.SetEnv("afternoon", 0)); }
            catch (Exception err) { Log?.Invoke("[boot] town preload failed (it loads at the opening instead)", err); }
        }

        /// <summary>The title screen loop, forever (the original boot()).</summary>
        public async Task Boot()
        {
            await Preload();
            for (;;) await TitleOnce();
        }

        /// <summary>
        /// One pass of the title loop: the landing screen, then a menu screen ('menu' back to the title) or a run (new or
        /// continued) to its end. Returns the landing pick for a menu screen, else the run's result ('menu', 'restart', null).
        /// </summary>
        public async Task<string> TitleOnce()
        {
            // 도우미 모드 forgets the last run (its goal, screens, talks) and hides its chip on the title and its menus
            AiOn.Game.Companion.Reset();
            // a prepared run left through the pause menu's reload (or a closed app) never reached its cleanup
            if (!string.IsNullOrEmpty(Store.Get(SceneSelect.SaveKey))) Store.Set(SceneSelect.SaveKey, "");
            RestoreTeacherSettings();
            var saved = SafeParse(Store.Get(SaveKey));
            var seen = Endings.GalleryView(Store.Get(GalleryKey));
            var rec = Challenges.ReadRecords(Store.Get(RecordsKey));
            var pick = Str(await Ui.Open("landing", new JObject
            {
                ["hasSave"] = saved["G"] is JObject, ["endings"] = seen.Count(e => e.Seen), ["challenges"] = rec.Done.Count,
            }));
            if (pick == "gallery") { await Ui.Open("gallery", new JObject { ["view"] = JArray.FromObject(seen) }); return pick; }
            if (pick == "records") { await Ui.Open("records", JObject.FromObject(rec)); return pick; }
            if (pick == "lounge") { await OpenLounge(); return pick; }
            if (pick == FamilyCard.Screen) { await Ui.Open(FamilyCard.Screen, new JObject()); return pick; } // 우리 집 안전 준비
            if (pick == Archive.Screen) { await OpenMore(); return pick; } // 더 알아보기 (기록관, 지도와 사람들, 훈련 달력, 우리 집 안전 준비)
            if (pick == "settings") { UpdateSettings(await Ui.Open("settings", (JObject)Settings.DeepClone())); return pick; }
            if (pick == "about") { await Ui.Open("about", new JObject { ["sources"] = JArray.FromObject(Facts.Sources) }); return pick; }
            if (pick == "scenes")
            {
                var id = Str(await Ui.Open("sceneSelect", SceneSelect.ScreenOptions(HasPrepared)));
                var m = SceneSelect.Get(id);
                if (m == null || !m.Ready) return pick;
                return await StartMoment(m);
            }
            if (pick == "teacher") return await StartTeacher();
            return await StartRun(pick == "continue", saved);
        }

        // ------------------------------------------------------------------ judge entry: scene select, teacher mode

        bool HasPrepared(string id)
        {
            try { return !string.IsNullOrEmpty(PreparedSave?.Invoke(id)); }
            catch (Exception) { return false; }
        }

        /// <summary>The prepared save of a moment, checked (SceneSelect.Prepare); null (logged) when it is missing or bad.</summary>
        PreparedStart Prepared(DemoMoment m)
        {
            try { return SceneSelect.Prepare(m, PreparedSave?.Invoke(m.Id), Chapters.Select(c => c.Id).ToList(), Rules.Db); }
            catch (Exception e) { Log?.Invoke($"[scenes] prepared save '{m?.Id}'", e); return null; }
        }

        /// <summary>
        /// Plays from a prepared state: loads it like a continued save but under the demo save key, then runs the chapters
        /// from its chapter to `end` (exclusive; -1 = to the ending). Returns the run's result, or "menu" when the state
        /// could not be loaded. The player's main save, the gallery and the records are never touched.
        /// </summary>
        public async Task<string> PlayPrepared(DemoMoment m, int end = -1, string stopAt = null)
        {
            var p = Prepared(m);
            if (p == null) { await Ui.Card(new CardOptions { Kind = "notice", Title = "장면을 열 수 없습니다", Body = { "준비된 상태를 읽지 못했습니다." } }); return "menu"; }
            DemoRun = true; _stopAt = stopAt;
            // every prepared save ships with the same seed: a moment's export gets its own anonymous id (a class keeps its own)
            if (!Ctx.TeacherRun) _exportSeed = NextExportSeed();
            _echo?.Hold(true);
            try
            {
                G = SaveCodec.Load(p.Saved["G"].ToString(Formatting.None), Rules.Db);
                SaveCodec.Restore(G);
                // a class's 사전 퀴즈 goes into each step's run, so the report (and its export) at a step's ending has it
                if (Ctx.TeacherRun && _classPre != null && QuizLogic.Find(G, QuizLogic.Pre) == null) GameRules.RecordEvent(G, QuizLogic.Event, _classPre.ToJson());
                Store.Set(SceneSelect.SaveKey, p.Saved.ToString(Formatting.None));
                ResetRun(p.Saved["snap"]);
                Engine.SetView(View);
                Hud.Show();
                string result;
                try { result = await RunChapters(p.Index, p.Resume, end); }
                finally { Hud.Hide(); }
                return result;
            }
            finally
            {
                Store.Set(SceneSelect.SaveKey, "");
                DemoRun = false; _stopAt = null;
                _echo?.Hold(false);
                LastPrepared = G;
                if (Ctx.TeacherRun && G != null) _classSteps.Add((m.Id, G)); else _exportSeed = null;
                G = null; Ctx.Rate = 0;
            }
        }

        /// <summary>장면 바로가기: one moment, played on to the ending (or until the player goes back to the title).</summary>
        public Task<string> StartMoment(DemoMoment m) => PlayPrepared(m);

        /// <summary>
        /// 교사용 30분 모드: the intro card (and the experience mode), then each moment of SceneSelect.TeacherRoute played to
        /// its stop (TeacherStep.StopAt, else the end of its chapter) with a lesson card (one sourced fact) after it; the last
        /// runs to the ending document, then its lesson and a summary card that lists the lessons. Returns the last run's
        /// result ("menu" when a step was left). The experience mode picked here is the class's: the player's own profile
        /// setting comes back afterwards.
        /// </summary>
        public async Task<string> StartTeacher()
        {
            var go = await Ui.Card(new CardOptions
            {
                Kind = "info", Title = SceneSelect.TeacherTitle, Meta = SceneSelect.TeacherMeta, Body = SceneSelect.TeacherIntro.ToList(),
                Actions = new List<CardAction> { new("start", "시작하기", true), new("back", "돌아가기") },
            });
            if (go != "start") return "teacher";
            // the player's own experience mode, kept until the class is over (also across a menu reload or a closed app)
            Store.Set(SceneSelect.TeacherRestoreKey, new JObject { ["profile"] = Settings["profile"]?.DeepClone(), ["captions"] = Settings["captions"]?.DeepClone() }.ToString(Formatting.None));
            Ctx.TeacherRun = true;
            _classPre = null; _classSteps.Clear();
            LastPrepared = null;
            uint classSeed = NextExportSeed();
            _exportSeed = classSeed; // the class's anonymous id, for every export made during it
            try
            {
                await ProfileCard();
                _classPre = await TakeQuiz(QuizLogic.Pre, classSeed, null, null);
                var ids = Chapters.Select(c => c.Id).ToList();
                var route = SceneSelect.TeacherRoute;
                string result = null;
                for (int i = 0; i < route.Count; i++)
                {
                    var m = SceneSelect.Get(route[i].Moment);
                    bool last = i == route.Count - 1;
                    int end = last ? -1 : ids.IndexOf(m.Chapter) + 1;
                    result = await PlayPrepared(m, end, last ? null : route[i].StopAt);
                    if (result == "menu" && !last) return result; // the fail card's 'menu' or a missing state: back to the title
                    await LessonCard(i);
                }
                await TeacherSummary();
                // 사후 퀴즈 after the class: the same concepts in other words, the comparison, the facts and the export
                // (the last step's report, and every step's rows)
                var basis = ExportBasis(LastPrepared, "teacher");
                if (basis != null) PlaytestExport.WithSteps(basis, _classSteps);
                await TakeQuiz(QuizLogic.Post, classSeed, _classPre, basis);
                return result;
            }
            finally { Ctx.TeacherRun = false; _classPre = null; _classSteps.Clear(); _exportSeed = null; RestoreTeacherSettings(); }
        }

        // ------------------------------------------------------------------ 사전/사후 퀴즈 (QuizLogic, QuizScreen)

        /// <summary>The state the last prepared run (a moment, a class's step) ended with; the class's export reads it.</summary>
        public GameState LastPrepared { get; private set; }

        uint ClassSeed() => (uint)Math.Floor(Random() * 4294967295.0);

        QuizAnswers _classPre;
        readonly List<(string moment, GameState g)> _classSteps = new();
        uint? _exportSeed;

        /// <summary>
        /// The seed an export's anonymous run id is made from: a class's own (drawn when it starts) or a 장면 바로가기 run's
        /// (drawn when it starts: the prepared saves all ship with one seed); null in a story run (its G.Seed).
        /// </summary>
        public uint? ExportSeed => _exportSeed;

        uint _exportRuns;

        /// <summary>A fresh seed for a moment's or a class's anonymous id: the random draw, mixed with a count of the ones
        /// drawn in this session so that two in a row never share it.</summary>
        public uint NextExportSeed() => unchecked(ClassSeed() ^ (++_exportRuns * 2654435761u));

        /// <summary>
        /// Opens the quiz screen for a phase; returns the answers, or null when it was skipped (the fake host's {} too).
        /// export: what the result page's 기록 내보내기 writes besides the quiz (PlaytestExport.Build of the run).
        /// </summary>
        async Task<QuizAnswers> TakeQuiz(string phase, uint seed, QuizAnswers pre, JObject export)
        {
            var o = new JObject { ["phase"] = phase, ["seed"] = seed, ["profile"] = Profile, ["mode"] = Ctx.TeacherRun ? "teacher" : DemoRun ? "demo" : "story" };
            if (Ctx.TeacherRun) o["concepts"] = new JArray(QuizLogic.ClassConcepts); // a class is asked what it plays
            if (_exportSeed.HasValue) o["exportSeed"] = _exportSeed.Value;
            if (pre != null) o["pre"] = pre.ToJson();
            if (export != null) o["export"] = export;
            JToken r;
            try { r = await Ui.Open(QuizLogic.Screen, o); }
            catch (StoryFail) { throw; }
            catch (Exception e) { Log?.Invoke("[quiz] screen failed; the run goes on", e); return null; }
            var a = QuizAnswers.Read(r);
            if (a == null) return null;
            a.Phase = phase; a.Seed = seed;
            return a;
        }

        JObject ExportBasis(GameState g, string mode)
        {
            try { return PlaytestExport.Build(g, mode, DateTime.UtcNow, seed: _exportSeed); }
            catch (Exception e) { Log?.Invoke("[quiz] export basis", e); return null; }
        }

        /// <summary>사전 퀴즈 before a new run (skippable): the answers stay in the run as a "quiz" event.</summary>
        async Task OfferPreQuiz()
        {
            if (G == null) return;
            var a = await TakeQuiz(QuizLogic.Pre, G.Seed, null, null);
            if (a != null) GameRules.RecordEvent(G, QuizLogic.Event, a.ToJson());
        }

        /// <summary>Whether the ending document still offers a 사후 퀴즈: once per run, not in a class (the class asks after its
        /// summary) and not in a 장면 바로가기 run that had no pre quiz.</summary>
        bool PostQuizPending()
        {
            if (G == null || Ctx.TeacherRun) return false;
            if (DemoRun && QuizLogic.Find(G, QuizLogic.Pre) == null) return false;
            return QuizLogic.Find(G, QuizLogic.Post) == null;
        }

        /// <summary>사후 퀴즈 from the ending document (Evac.EndingDocument), after the document has landed.</summary>
        async Task OfferPostQuiz()
        {
            if (!PostQuizPending()) return;
            var pre = QuizLogic.Find(G, QuizLogic.Pre);
            var a = await TakeQuiz(QuizLogic.Post, pre?.Seed ?? G.Seed, pre, ExportBasis(G, DemoRun ? "demo" : "story"));
            if (a != null) GameRules.RecordEvent(G, QuizLogic.Event, a.ToJson());
        }

        /// <summary>The class's experience mode was for the class: puts the player's own profile and captions back (once).</summary>
        void RestoreTeacherSettings()
        {
            var o = SafeParse(Store.Get(SceneSelect.TeacherRestoreKey));
            if (!o.HasValues) return;
            foreach (var k in new[] { "profile", "captions" })
            {
                if (o[k] == null || o[k].Type == JTokenType.Null) Settings.Remove(k); else Settings[k] = o[k].DeepClone();
            }
            Store.Set(SceneSelect.TeacherRestoreKey, "");
            ApplySettings();
        }

        Task<string> TeacherSummary()
        {
            var route = SceneSelect.TeacherRoute;
            return Ui.Card(new CardOptions
            {
                Kind = "info", Title = SceneSelect.TeacherSummaryTitle, Meta = SceneSelect.TeacherTitle,
                Body = route.Select(s => $"{s.LessonTitle} — {Facts.Text(s.Fact)}").ToList(),
                Actions = new List<CardAction> { new("ok", "처음 화면으로", true) },
            });
        }

        /// <summary>The card between two teacher-mode moments: one sourced line and where it comes from.</summary>
        Task<string> LessonCard(int i)
        {
            var step = SceneSelect.TeacherRoute[i];
            var src = Facts.Source(Facts.SourceId(step.Fact));
            var body = new List<string> { Facts.Text(step.Fact) };
            if (src != null) body.Add($"출처: {src.Publisher}");
            bool last = i == SceneSelect.TeacherRoute.Count - 1;
            return Ui.Card(new CardOptions
            {
                Kind = "info", Title = step.LessonTitle, Meta = SceneSelect.LessonMeta(i), Body = body,
                Actions = new List<CardAction> { new("next", last ? "정리 보기" : "다음 장면으로", true) },
            });
        }

        /// <summary>Starts a run: continue the saved one (if it loads) or a new game; plays it and clears the game after.</summary>
        public async Task<string> StartRun(bool continueSaved, JObject saved = null)
        {
            saved ??= SafeParse(Store.Get(SaveKey));
            int start = 0; string resume = null;
            if (continueSaved && saved["G"] is JObject sg)
            {
                try { G = SaveCodec.Load(sg.ToString(Formatting.None), Rules.Db); }
                catch (Exception e) { Log?.Invoke("save rejected", e); G = null; }
            }
            if (G?.Checkpoint != null)
            {
                var key = G.Checkpoint.Key;
                SaveCodec.Restore(G);
                var idx = saved["index"];
                start = idx != null && idx.Type == JTokenType.Integer && (long)idx >= 0 && (long)idx < Chapters.Count ? (int)(long)idx : 0;
                resume = Chapters[start].Id == key ? null : key;
            }
            else G = Rules.CreateGame((long)((uint)Math.Floor(Random() * 4294967296.0) >> 1));
            bool fresh = G.Checkpoint == null;
            if (fresh) _echo?.Begin(); else _echo?.Resume();
            ResetRun(G.Checkpoint != null ? saved["snap"] : null);
            Engine.SetView(View);
            if (fresh) { Engine.SetControls(false); await OfferPreQuiz(); await Intro(); Engine.SetControls(true); }
            Hud.Show();
            string result;
            try { result = await RunChapters(start, resume); }
            finally { Hud.Hide(); }
            if (result == "restart") Store.Set(SaveKey, "");
            G = null; Ctx.Rate = 0;
            return result;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A chapter entry as the runner wants it: awaits it and takes its result if it has one (Task or Task&lt;T&gt;).</summary>
        public static async Task<JToken> AsChapter(Task t)
        {
            await t;
            var prop = t.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            var v = prop?.GetValue(t);
            return v switch { null => null, JToken j => j, string s => new JValue(s), _ when prop.PropertyType.Name == "VoidTaskResult" => null, _ => JToken.FromObject(v) };
        }

        static JObject SafeParse(string s)
        {
            if (string.IsNullOrEmpty(s)) return new JObject();
            try { return JToken.Parse(s) as JObject ?? new JObject(); } catch { return new JObject(); }
        }

        /// <summary>A screen's pick as a string (null when it is not one).</summary>
        public static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static string JsText(JToken t) => t == null || t.Type == JTokenType.Null ? null : t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
        static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => false,
            JTokenType.Boolean => (bool)t,
            JTokenType.Integer or JTokenType.Float => (double)t != 0 && !double.IsNaN((double)t),
            JTokenType.String => ((string)t).Length > 0,
            _ => true,
        };
        static double N(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : double.NaN;
        static bool Finite(JToken t) { var d = N(t); return !double.IsNaN(d) && !double.IsInfinity(d); }

        // ------------------------------------------------------------------ decorators: deaf cues, spoken captions

        /// <summary>The context's engine: the host's, except that entering a level first drops what is in hand (the ctx.goto wrap).</summary>
        class DirectorEngine : IStoryEngine
        {
            protected readonly IStoryEngine E; readonly StoryDirector _d;
            public DirectorEngine(IStoryEngine e, StoryDirector d) { E = e; _d = d; }
            public Task Enter(string levelId, string spawn, Action prepare = null)
            {
                _d.DropHeld?.Invoke();
                return E.Enter(levelId, spawn, prepare);
            }
            public string LevelId => E.LevelId;
            public IReadOnlyList<StoryPoint> Points => E.Points;
            public bool TryGetSpawn(string spawn, out double x, out double y, out double z, out double yaw) => E.TryGetSpawn(spawn, out x, out y, out z, out yaw);
            public void ApplyLevelState(string key, JToken value) => E.ApplyLevelState(key, value);
            public JToken LevelDefault(string key) => E.LevelDefault(key);
            public void SetEnv(string preset, double seconds = 0) => E.SetEnv(preset, seconds);
            public string Env => E.Env;
            public void SetPower(bool on) => E.SetPower(on);
            public double PlayerX => E.PlayerX;
            public double PlayerY => E.PlayerY;
            public double PlayerZ => E.PlayerZ;
            public double PlayerYaw => E.PlayerYaw;
            public void Teleport(string spawn) => E.Teleport(spawn);
            public void TeleportTo(double x, double y, double z, double yaw) => E.TeleportTo(x, y, z, yaw);
            public double SpeedMul { set => E.SpeedMul = value; }
            public double StaminaUse { set => E.StaminaUse = value; }
            public bool Crouching => E.Crouching;
            public string Carrying { get => E.Carrying; set => E.Carrying = value; }
            public void SetFlashlight(bool on) => E.SetFlashlight(on);
            public void SetPlayerPreset(string preset) => E.SetPlayerPreset(preset);
            public void SetModelHidden(bool hidden) => E.SetModelHidden(hidden);
            public bool ControlsEnabled => E.ControlsEnabled;
            public void SetControls(bool on) => E.SetControls(on);
            public void SetView(string view) => E.SetView(view);
            public string View => E.View;
            public void Shake(double strength, double seconds) => E.Shake(strength, seconds);
            public void SetCameraOverride(CameraShot shot) => E.SetCameraOverride(shot);
            public Task FadeTo(double black, double seconds) => E.FadeTo(black, seconds);
            public void SetGrade(JObject grade) => E.SetGrade(grade);
            public double TimeScale { get => E.TimeScale; set => E.TimeScale = value; }
            public bool ReduceMotion => E.ReduceMotion;
            public IStoryPerson Npc(string name) => E.Npc(name);
            public IStoryPerson SpawnPerson(string preset, double x, double y, double z, double yaw) => E.SpawnPerson(preset, x, y, z, yaw);
            public void RemovePerson(IStoryPerson person) => E.RemovePerson(person);
            public event Action<double> Frame { add => E.Frame += value; remove => E.Frame -= value; }
            public event Action<StoryPoint> Interact { add => E.Interact += value; remove => E.Interact -= value; }
            public event Action<StoryPoint> Nearest { add => E.Nearest += value; remove => E.Nearest -= value; }
            public event Action<string> Key { add => E.Key += value; remove => E.Key -= value; }
            public event Action<double> Land { add => E.Land += value; remove => E.Land -= value; }
            public event Action<JObject> Blocked { add => E.Blocked += value; remove => E.Blocked -= value; }
        }

        /// <summary>The same, for a host whose engine also draws the cutscene effects (StoryCine looks for ICineFx on Ctx.Engine).</summary>
        sealed class DirectorEngineFx : DirectorEngine, ICineFx
        {
            ICineFx Fx => (ICineFx)E;
            public DirectorEngineFx(IStoryEngine e, StoryDirector d) : base(e, d) { }
            public void Overlay(bool on) => Fx.Overlay(on);
            public void Title(IReadOnlyList<string> lines) => Fx.Title(lines);
            public void TitleOut() => Fx.TitleOut();
            public void Fill(double x, double y, double z, double intensity, string color) => Fx.Fill(x, y, z, intensity, color);
            public void FillRange(double metres) => Fx.FillRange(metres);
            public IDisposable Missile(double[] from, double[] to, double seconds) => Fx.Missile(from, to, seconds);
        }

        sealed class DirectorAudio : IStoryAudio
        {
            readonly IStoryAudio _a; readonly StoryDirector _d;
            public DirectorAudio(IStoryAudio a, StoryDirector d) { _a = a; _d = d; }
            void Cue(string name) { if (_d.Profile == "deaf" && SoundCues.TryGetValue(name, out var t)) _d.Hud.Toast(t, "site", 4200); }
            public void Ambience(string name, double fadeSeconds = 1.2) => _a.Ambience(name, fadeSeconds);
            public void Cue(string name, JObject args = null) { _a.Cue(name, args); if (name != "siren") Cue(name); }
            public void Loop(string name, bool on, JObject args = null) { _a.Loop(name, on, args); if (name == "siren" && on) Cue(name); }
        }

        sealed class DirectorHud : IStoryHud
        {
            readonly IStoryHud _h; readonly StoryDirector _d;
            public DirectorHud(IStoryHud h, StoryDirector d) { _h = h; _d = d; }
            public void SetClock(string text) => _h.SetClock(text);
            public void SetStats(Stats stats)
            {
                // 정민's health and fatigue are not 서연's or 어르신's: the panel waits until he is the player again
                if (_d.OtherPov && _h is AiOn.UI.StoryOverlay so) { so.HideStats(); return; }
                _h.SetStats(stats);
            }
            public void SetBattery(double percent)
            {
                if (_d.OtherPov) { if (_h is AiOn.UI.StoryOverlay so) so.HideBattery(); return; }
                _h.SetBattery(percent);
            }
            public void SetBag(double? weightKg, double limitKg) => _h.SetBag(_d.OtherPov ? null : weightKg, limitKg);
            public void SetTension(double amount01) => _h.SetTension(amount01);
            public void SetStamina(double amount01) => _h.SetStamina(amount01);
            public void Prompt(string label) => _h.Prompt(label);
            public void Toast(string text, string label, int ms) => _h.Toast(text, label, ms);
            public Task Caption(string text, string speaker = null, int? ms = null) { _d.SpeakIf(text); return _h.Caption(text, speaker, ms); }
            public void ClearCaption() => _h.ClearCaption();
            public void SetObjective(string title, string body, IReadOnlyList<ObjectiveStep> steps) => _h.SetObjective(title, body, steps);
            public void SetChapter(string title, string subtitle) => _h.SetChapter(title, subtitle);
            public void SetCinematic(bool on) => _h.SetCinematic(on);
            public void Hurt(double amount01) => _h.Hurt(amount01);
            public void Show() => _h.Show();
            public void Hide() => _h.Hide();
            public void SetMap(JObject map) => _h.SetMap(map);
            public void SetCompass(double? yaw) => _h.SetCompass(yaw);
        }
    }
}
