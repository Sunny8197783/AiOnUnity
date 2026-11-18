// The contract between the story (chapters ported from the original story/*.js) and whatever runs it: the Unity
// scene (UnityStoryHost, written later) or a scripted fake in tests (FakeStoryHost). Chapters only ever talk to
// StoryContext; StoryContext only ever talks to these interfaces. Coordinates are the ORIGINAL frame (three.js:
// x east, y up, z south, metres; yaw in radians, 0 faces north, positive turns left), so numbers in the chapters
// stay exactly the original's. The host converts to Unity space.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>A soft failure of a chapter (original story/runtime.js Fail): the director shows a fail card and retries.</summary>
    public class StoryFail : Exception
    {
        public string Type { get; }
        public JObject Detail { get; }
        public StoryFail(string type, JObject detail = null) : base($"fail:{type}") { Type = type; Detail = detail ?? new JObject(); }
    }

    /// <summary>An interaction point of the current level (original level.points entry).</summary>
    public sealed class StoryPoint
    {
        public string Id, Label, Kind;
        public double X, Y, Z;
        /// <summary>Own reach radius (0 = default) and explicit reach override (0 = none).</summary>
        public double R, Reach;
        public string Host;
        /// <summary>Setting this hides or shows the E prompt (the host applies it to the scene).</summary>
        public bool Enabled = true;
        /// <summary>Optional description shown by the common handler (point.desc in the original).</summary>
        public string Desc;
    }

    /// <summary>A choice in a dialog: id is what Talk returns; a disabled one shows why.</summary>
    public sealed class TalkChoice
    {
        public string Id, Label;
        public bool Disabled;
        public string Why;
        /// <summary>Port-only: a disabled choice the player already took (drawn "✓", not locked "⊘").</summary>
        public bool Taken;
        public TalkChoice() { }
        public TalkChoice(string id, string label, bool disabled = false, string why = null) { Id = id; Label = label; Disabled = disabled; Why = why; }
    }

    /// <summary>ui.talk({ speaker, role, portrait?, lines, choices? }).</summary>
    public sealed class TalkOptions
    {
        public string Speaker = "", Role = "", Portrait;
        public List<string> Lines = new();
        public List<TalkChoice> Choices;
        /// <summary>Port-only: "normal", or "miss" (the explanation after a wrong pick: the name in amber with a △).</summary>
        public string Tone = "normal";
        /// <summary>Port-only: where this talk sits in a run of them ("신호 2/5"), shown top right; the role stays verbatim.</summary>
        public string Progress;
    }

    public sealed class CardAction
    {
        public string Id, Label;
        public bool Primary;
        /// <summary>Port-only: a short second line under the label (a decision's consequence, an experience mode's subtitle).</summary>
        public string Hint;
        public CardAction() { }
        public CardAction(string id, string label, bool primary = false) { Id = id; Label = label; Primary = primary; }
    }

    /// <summary>
    /// ui.card({ kind, title, meta, body, actions, ... }): a full-screen story or notice card. Kind is the original's
    /// alert / news / notice / info / sms, plus the port's "decision": a two-way choice drawn with a larger title, a
    /// clear gap before the actions and equal-width buttons (each with its Hint under the label).
    /// </summary>
    public sealed class CardOptions
    {
        public string Kind = "story", Title = "", Meta = "";
        public List<string> Body = new();
        public List<CardAction> Actions;
        /// <summary>Anything else the original passes (image, source, list...), as the original's JSON.</summary>
        public JObject Extra;
    }

    /// <summary>An objective step on the HUD ({ text, done }).</summary>
    public sealed class ObjectiveStep
    {
        public string Text;
        public bool Done;
        /// <summary>Port-only: one of several exclusive options (drawn ◇ under "다음 중 하나를 고르세요"), not a to-do.</summary>
        public bool Choice;
        public ObjectiveStep() { }
        public ObjectiveStep(string text, bool done = false) { Text = text; Done = done; }
    }

    /// <summary>A named character in the scene (original level.npcs.* / engine.people.createPerson).</summary>
    public interface IStoryPerson
    {
        string Name { get; }
        bool Visible { get; set; }
        double X { get; }
        double Z { get; }
        void SetPosition(double x, double y, double z, double? yaw = null);
        /// <summary>Original animation names: idle, walk, brisk, run, carry, walker, sit, sitChair, talk, phone, wave, point, crouch, stairsUp, stairsDown, hurt, down, air.</summary>
        void SetAnim(string anim, double? speed = null);
        /// <summary>One-shot clips: punch, throw, pickup, vault, climb, land, hurt.</summary>
        void Play(string clip);
        /// <summary>Turn the head toward (x, z); null clears.</summary>
        void LookAt(double? x, double? z);
        /// <summary>Props: backpack, waterJug, walker, flashlight, phone, baton, basket, cart.</summary>
        void SetProp(string prop, bool on);
        /// <summary>Walk along points at speed m/s; completes on arrival.</summary>
        Task WalkTo(IReadOnlyList<(double x, double z)> path, double speed = 1.3);
    }

    /// <summary>A camera placement for cutscenes (original engine.setCameraOverride).</summary>
    public sealed class CameraShot
    {
        public double X, Y, Z, TargetX, TargetY, TargetZ;
        public double Fov = 50;
    }

    /// <summary>The world: levels, player, camera, light and the frame/interact events (original engine).</summary>
    public interface IStoryEngine
    {
        /// <summary>Current level id: apartment, mart, store, office, town, shelter (null before the first Enter).</summary>
        string LevelId { get; }
        /// <summary>The current level's interaction points (the same objects for the life of the level).</summary>
        IReadOnlyList<StoryPoint> Points { get; }
        /// <summary>Named spots of the current level (level.anchors / spawns), original frame.</summary>
        bool TryGetSpawn(string spawn, out double x, out double y, out double z, out double yaw);

        /// <summary>Loads a level and puts the player on a spawn. prepare runs after the level exists and before it is shown.</summary>
        Task Enter(string levelId, string spawn, Action prepare = null);
        /// <summary>level.setState(key, value) on the current level (power, soldOut, closures, hallFire...).</summary>
        void ApplyLevelState(string key, JToken value);
        /// <summary>The level's own default for a state key, or null.</summary>
        JToken LevelDefault(string key);

        void SetEnv(string preset, double seconds = 0);
        string Env { get; }
        void SetPower(bool on);

        double PlayerX { get; }
        double PlayerY { get; }
        double PlayerZ { get; }
        double PlayerYaw { get; }
        void Teleport(string spawn);
        void TeleportTo(double x, double y, double z, double yaw);
        double SpeedMul { set; }
        double StaminaUse { set; }
        bool Crouching { get; }
        /// <summary>What the player carries in hand ('water' or null): changes the walk animation.</summary>
        string Carrying { get; set; }
        void SetFlashlight(bool on);
        void SetPlayerPreset(string preset);
        void SetModelHidden(bool hidden);

        bool ControlsEnabled { get; }
        void SetControls(bool on);
        /// <summary>'first' or 'third'.</summary>
        void SetView(string view);
        string View { get; }
        void Shake(double strength, double seconds);
        void SetCameraOverride(CameraShot shot);
        Task FadeTo(double black, double seconds);
        /// <summary>A temporary colour grade on top of the preset (saturation, exposure, tint...), null clears.</summary>
        void SetGrade(JObject grade);
        double TimeScale { get; set; }
        bool ReduceMotion { get; }

        /// <summary>A named character of the current level (null if the level has none by that name).</summary>
        IStoryPerson Npc(string name);
        IStoryPerson SpawnPerson(string preset, double x, double y, double z, double yaw);
        void RemovePerson(IStoryPerson person);

        /// <summary>Every frame while the game runs (dt in seconds, already scaled by TimeScale).</summary>
        event Action<double> Frame;
        /// <summary>E on a point.</summary>
        event Action<StoryPoint> Interact;
        /// <summary>The point in reach changed (null = none).</summary>
        event Action<StoryPoint> Nearest;
        /// <summary>A UI key the director handles (M, Tab, I, P, H, Escape, F, V...).</summary>
        event Action<string> Key;
        /// <summary>The player landed from a fall of this many metres.</summary>
        event Action<double> Land;
        /// <summary>The player was stopped (e.g. { kind: 'stairs' } for someone who cannot take stairs).</summary>
        event Action<JObject> Blocked;
    }

    /// <summary>The overlay (original ui/hud.js).</summary>
    public interface IStoryHud
    {
        void SetClock(string text);
        void SetStats(Stats stats);
        void SetBattery(double percent);
        /// <summary>null hides the bag meter.</summary>
        void SetBag(double? weightKg, double limitKg);
        void SetTension(double amount01);
        void SetStamina(double amount01);
        void Prompt(string label);
        /// <summary>label: official | site | neighbor | rumor | self (colour and icon).</summary>
        void Toast(string text, string label, int ms);
        /// <summary>A subtitle line; completes when it has been shown for ms (or a length-based time).</summary>
        Task Caption(string text, string speaker = null, int? ms = null);
        void ClearCaption();
        void SetObjective(string title, string body, IReadOnlyList<ObjectiveStep> steps);
        void SetChapter(string title, string subtitle);
        void SetCinematic(bool on);
        void Hurt(double amount01);
        void Show();
        void Hide();
        void SetMap(JObject map);
        void SetCompass(double? yaw);
    }

    /// <summary>
    /// Screens (original ui/*.js). Talk, Card and TimeSkip are typed; the rest take the original's option object and
    /// return the original's result, both as JSON, so each screen can be ported with its own contract unchanged:
    /// bag, shop, radio, planner, phone, emergencyAlert, extinguisher, docs, routemap, notebook, inventory,
    /// chapterCard, failCard, ending, compose.
    /// </summary>
    public interface IStoryUi
    {
        /// <summary>Lines one at a time, then the choices; returns the picked choice id (null when there were none).</summary>
        Task<string> Talk(TalkOptions options);
        /// <summary>Returns the action id pressed (null when there were none).</summary>
        Task<string> Card(CardOptions options);
        Task TimeSkip(string from, string to, IReadOnlyList<string> text);
        Task<JToken> Open(string screen, JObject options);
    }

    /// <summary>Sound (original audio.js). Cue names: fire, hit, boom, siren, phoneAlert, radio, footstep, door, click.</summary>
    public interface IStoryAudio
    {
        /// <summary>Ambience beds: indoor, city, night, blackout, office, mart, shelter, none.</summary>
        void Ambience(string name, double fadeSeconds = 1.2);
        void Cue(string name, JObject args = null);
        /// <summary>A looping cue (siren, fire, radio static) on or off.</summary>
        void Loop(string name, bool on, JObject args = null);
    }

    /// <summary>Saving (original app.js persist): a checkpoint is a retry point inside a chapter.</summary>
    public interface IStoryPersist
    {
        void Checkpoint(string key);
        void Save();
    }

    /// <summary>Player settings the story reads (original settings object).</summary>
    public sealed class StorySettings
    {
        public bool Captions = true;
        /// <summary>Experience profile: '' (standard), 'deaf', 'low-vision', 'mobility', 'slow'.</summary>
        public string Profile = "";
        public bool MemoryCue = true;
        public bool ReduceMotion;
        public string Mode = "guided";
    }

    /// <summary>Everything a roam needs (original ctx.roam options).</summary>
    public sealed class RoamOptions
    {
        /// <summary>Point id (or "level:point", or "*") -> handler. A handler's result ends the roam unless it is null
        /// (= JS undefined: keep roaming) or StoryContext.Handled (handled, keep roaming).</summary>
        public Dictionary<string, Func<StoryPoint, Task<JToken>>> On = new();
        /// <summary>Follow the default doors between levels.</summary>
        public bool Doors = true;
        public RoamDeadline Deadline;
        /// <summary>Game seconds per real second while roaming; null keeps the context's rate.</summary>
        public double? Rate;
        /// <summary>Called every frame; a non-null result ends the roam with it.</summary>
        public Func<double, JToken> Tick;
        /// <summary>Handler for shelter-kind points in town.</summary>
        public Func<StoryPoint, Task<JToken>> Shelter;
    }

    /// <summary>Fail when the clock passes day/minute. TypeOf (if set) picks the fail type at that moment.</summary>
    public sealed class RoamDeadline
    {
        public int Day, Minute;
        public string Type;
        public Func<string> TypeOf;
    }
}
