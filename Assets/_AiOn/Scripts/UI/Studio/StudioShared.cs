// The minigame studios' shared, code-made objects (the procedural surface maps, the shoe print, the smoke puff, the
// gauge's dial face, the recolour material, the breathing orb): built on first use and reused by every studio, then let
// go in one place. They go when a scene unloads while no studio is open (a level change), on the title return, and at
// the start of play (with domain reload off they would outlive play mode; after a script reload the statics that held
// them are gone, so anything left with our name prefix is swept up there too). A cache that finds its object destroyed
// simply builds it again.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiOn.UI.Studio
{
    public static class StudioShared
    {
        public const string Prefix = "AiOnStudio_";

        static readonly List<Object> _all = new();
        static bool _hooked;

        /// <summary>Names <paramref name="o"/> with the prefix, keeps it out of scenes and saves, and releases it with the rest.</summary>
        public static T Keep<T>(T o, string name) where T : Object
        {
            if (!o) return o;
            o.name = Prefix + name;
            o.hideFlags = HideFlags.DontSave;
            _all.Add(o);
            if (!_hooked && Application.isPlaying) { _hooked = true; SceneManager.sceneUnloaded += OnUnloaded; }
            return o;
        }

        /// <summary>How many shared objects are alive now.</summary>
        public static int Count { get { int n = 0; foreach (var o in _all) if (o) n++; return n; } }

        /// <summary>Destroys every shared object (the caches rebuild on next use).</summary>
        public static void Release()
        {
            foreach (var o in _all)
            {
                if (!o) continue;
                if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
            }
            _all.Clear();
        }

        static void OnUnloaded(Scene s)
        {
            if (MiniStudio.Cameras.Count == 0) Release();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Release();
            Sweep(Resources.FindObjectsOfTypeAll<Sprite>());
            Sweep(Resources.FindObjectsOfTypeAll<Texture>());
            Sweep(Resources.FindObjectsOfTypeAll<Material>());
            if (_hooked) { SceneManager.sceneUnloaded -= OnUnloaded; _hooked = false; }
        }

        static void Sweep(Object[] found)
        {
            foreach (var o in found)
                if (o && (o.hideFlags & HideFlags.DontSave) != 0 && o.name.StartsWith(Prefix, System.StringComparison.Ordinal))
                    Object.DestroyImmediate(o);
        }
    }
}
