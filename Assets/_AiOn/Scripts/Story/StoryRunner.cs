using System;
using System.Collections.Generic;
using System.IO;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// Starts the game on the Boot scene: the StoryDirector (the original app.js: title screen, chapters with retries,
    /// saves, keys) running the ported chapters on the UnityStoryHost. Saves, the gallery and the run records are kept
    /// in one JSON file under the persistent data folder (the original kept them in localStorage).
    /// </summary>
    [RequireComponent(typeof(UnityStoryHost))]
    public class StoryRunner : MonoBehaviour
    {
        public StoryDirector Director { get; private set; }

        async void Start()
        {
            var host = GetComponent<UnityStoryHost>();
            var session = FindFirstObjectByType<GameSession>();
            if (!session || !host.overlay) { Debug.LogError("[StoryRunner] the Boot scene needs a GameSession and a StoryOverlay"); return; }
            var facts = session.FactsData; // facts.json + facts-ext.json (Facts.Empty when the session has none)
            StoryScreens.Register(host, session);

            // The host-side visuals the story drives: blasts, fires, rubble, debris (danger layer) and cutscene letterbox, titles, missiles.
            var dangerFx = new UnityDangerFx(host);
            var parts = DirectorParts.Default(dangerFx: dangerFx, cineFx: new UnityCineFx(host));
            Director = new StoryDirector(session.Rules, facts, host, host.overlay, host, host, new FileStoryStore(), parts: parts, reduceMotion: host.settings.ReduceMotion);
            Director.NowMs = () => Time.realtimeSinceStartupAsDouble * 1000.0;
            var rng = new System.Random();
            Director.Random = () => rng.NextDouble();
            host.getState = () => Director.G;
            // The Director itself listens to host.Key / host.Blocked (its engine is this host); subscribing here as well ran every key twice.
            Director.Log = (what, e) => Debug.LogError($"[StoryRunner] {what}: {e}");
            // 장면 바로가기 / 교사용 30분 모드: the prepared states shipped in Resources/Demo/<id>.json
            Director.PreparedSave = id =>
            {
                var asset = Resources.Load<TextAsset>(SceneSelect.ResourceFolder + "/" + id);
                return asset != null ? asset.text : null;
            };
            // The hands (engine/actions.js): picking up and setting down loose things (no punch, nothing thrown at people).
            if (!TryGetComponent(out PlayerHands hands)) hands = gameObject.AddComponent<PlayerHands>(); // not ??: a missing component is a fake null in the Editor
            hands.host = host; hands.dangerFx = dangerFx;
            hands.Random = () => rng.NextDouble();
            Director.DropHeld = hands.Drop;
            dangerFx.SpawnLoose = hands.Spawn; // blast bricks and rocks become loose things (actions.spawn)

            // The settings the host owns (app.js applySettings): sound, sensitivity, graphics tier and its governor, text
            // scale, reduced motion, the experience profile's body and senses; and the pause menu's way back to the title.
            host.settings = Director.StorySettings; // one settings object: the camera shake and the menus read what the Director wrote
            if (!TryGetComponent(out AiOn.Settings.HostSettings hostSettings)) hostSettings = gameObject.AddComponent<AiOn.Settings.HostSettings>();
            var savedQ = AiOn.Settings.QualityTiers.Saved(Director.Settings["quality"]?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)Director.Settings["quality"] : null,
                Director.Settings["qualityAuto"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)Director.Settings["qualityAuto"]);
            Director.Settings["quality"] = savedQ.quality ?? AiOn.Settings.HostSettings.DetectTier(); // 'auto': detected once and remembered
            Director.Settings["qualityAuto"] = savedQ.auto;
            Director.ApplyHostSettings = hostSettings.Apply;
            Director.Speak = hostSettings.Speak;
            hostSettings.Stepped = (q, label) => Director.OnQualityChange(q, label, true);
            Director.MenuRequested = AiOn.Settings.TitleReturn.ToTitle;
            Director.ApplySettings();

            try { await Director.Boot(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>The original's localStorage as a JSON file (save, gallery, records, settings).</summary>
    public sealed class FileStoryStore : IStoryStore
    {
        readonly string _path = Path.Combine(Application.persistentDataPath, "aion-store.json");
        Dictionary<string, string> _data;

        Dictionary<string, string> Data
        {
            get
            {
                if (_data != null) return _data;
                try { _data = File.Exists(_path) ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_path)) : null; }
                catch (Exception e) { Debug.LogWarning($"[Store] unreadable {_path}: {e.Message}"); }
                return _data ??= new Dictionary<string, string>();
            }
        }

        public string Get(string key) => Data.TryGetValue(key, out var v) ? v : null;

        public void Set(string key, string value)
        {
            if (value == null) Data.Remove(key); else Data[key] = value;
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Data)); // write then swap: a crash never leaves half a file
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
    }
}
