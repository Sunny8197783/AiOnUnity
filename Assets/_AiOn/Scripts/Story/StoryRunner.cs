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
            var facts = Facts.Load(session.factsJson ? session.factsJson.text : "{}");
            StoryScreens.Register(host, session);

            // The host-side visuals the story drives: blasts, fires, rubble, debris (danger layer) and cutscene letterbox, titles, missiles.
            var parts = DirectorParts.Default(dangerFx: new UnityDangerFx(host), cineFx: new UnityCineFx(host));
            Director = new StoryDirector(session.Rules, facts, host, host.overlay, host, host, new FileStoryStore(), parts: parts, reduceMotion: host.settings.ReduceMotion);
            Director.NowMs = () => Time.realtimeSinceStartupAsDouble * 1000.0;
            var rng = new System.Random();
            Director.Random = () => rng.NextDouble();
            host.getState = () => Director.G;
            host.Key += code => Run(Director.OnKey(code), "key " + code);
            host.Blocked += b => Run(Director.OnBlocked(b), "blocked");

            try { await Director.Boot(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static async void Run(System.Threading.Tasks.Task t, string what)
        {
            try { await t; }
            catch (Exception e) { Debug.LogError($"[StoryRunner] {what}: {e}"); }
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
