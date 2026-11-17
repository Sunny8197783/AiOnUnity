using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>Story sound names (ambience_indoor, door, footstep, phoneAlert, radio, click...) mapped to clips.</summary>
    [CreateAssetMenu(menuName = "AiOn/Audio Library", fileName = "AudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string name;
            public AudioClip clip;
        }

        public List<Entry> entries = new();

        Dictionary<string, AudioClip> _map;

        public AudioClip Get(string name)
        {
            if (_map == null || _map.Count != entries.Count)
            {
                _map = new Dictionary<string, AudioClip>();
                foreach (var e in entries) if (!string.IsNullOrEmpty(e.name) && e.clip) _map[e.name] = e.clip;
            }
            return name != null && _map.TryGetValue(name, out var c) ? c : null;
        }

        void OnValidate() => _map = null;
    }
}
