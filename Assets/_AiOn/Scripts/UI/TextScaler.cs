using System.Collections.Generic;
using AiOn.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// The 글자 크기 setting (the original's --text-scale on the root font size): every uGUI text is drawn at its authored
    /// size times the scale. Labels made through <see cref="UiKit.Label"/> start scaled; <see cref="Apply"/> rescales the
    /// texts already on screen once, when the setting changes (never per frame).
    /// </summary>
    public static class TextScaler
    {
        static readonly Dictionary<Text, int> Base = new();
        static readonly List<Text> Dead = new();

        public static float Scale { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Base.Clear(); Scale = 1f; _sweepAt = SweepFloor; }

        // Destroyed texts leave the registry when it has doubled since the last sweep (dialog lines, cards and screens are
        // rebuilt through UiKit all session long), so it stays about the size of the live UI.
        const int SweepFloor = 256;
        static int _sweepAt = SweepFloor;

        /// <summary>The texts the registry holds (for checks).</summary>
        public static int Count => Base.Count;

        /// <summary>A new text of authored size: remembers it and returns the size to draw now.</summary>
        public static int Register(Text t, int size)
        {
            if (t) Base[t] = size;
            if (Base.Count >= _sweepAt) { Sweep(); _sweepAt = Mathf.Max(SweepFloor, Base.Count * 2); }
            return SettingsMath.Scaled(size, Scale);
        }

        static void Sweep()
        {
            Dead.Clear();
            foreach (var kv in Base) if (!kv.Key) Dead.Add(kv.Key);
            foreach (var t in Dead) Base.Remove(t);
            Dead.Clear();
        }

        /// <summary>A text on a world-space canvas: part of the scene, not of the HUD.</summary>
        static bool InWorld(Text t)
        {
            var c = t.canvas ? t.canvas : t.GetComponentInParent<Canvas>(true);
            if (c) c = c.rootCanvas;
            return c && c.renderMode == RenderMode.WorldSpace;
        }

        /// <summary>Sets the scale and redraws every text in the loaded scenes (and the persistent ones).</summary>
        public static void Apply(float scale)
        {
            scale = SettingsMath.TextScale(scale);
            float old = Scale;
            Scale = scale;
            Sweep();
            if (Mathf.Approximately(old, scale) && Base.Count > 0) return;
            foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (InWorld(t)) continue; // signs and screens in the world (the office desk monitor) keep their size
                int b;
                if (!Base.TryGetValue(t, out b) || t.fontSize != SettingsMath.Scaled(b, old))
                    b = Base.ContainsKey(t) ? Mathf.Max(1, Mathf.RoundToInt(t.fontSize / old)) : t.fontSize; // code changed its size since: take it as the new base
                Base[t] = b;
                t.fontSize = SettingsMath.Scaled(b, scale);
            }
        }
    }
}
