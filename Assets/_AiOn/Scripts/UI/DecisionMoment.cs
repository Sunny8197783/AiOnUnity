using System;
using UnityEngine;

namespace AiOn.UI
{
    /// <summary>
    /// A moment of judgement (a "판단" choice, a decision card): the world slows to a fifth and falls out of focus behind
    /// the choices, then comes back when one is taken. The UI runs on unscaled time, so the cards move at full speed.
    /// Counted, so nested moments end together; a run torn down mid-moment gets its time back (ResetStatics, End).
    /// </summary>
    public sealed class DecisionMoment : MonoBehaviour
    {
        public const float SlowScale = 0.2f, EaseSeconds = 0.35f;

        static DecisionMoment s_inst;
        static int s_count;
        float _k;          // 0 normal .. 1 slowed
        float _normal = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_inst = null; s_count = 0; }

        /// <summary>Time scale at progress k (0 = the scale before, 1 = slowed), eased (pure; tested).</summary>
        public static float ScaleAt(float normal, float k) => Mathf.Lerp(normal, Mathf.Min(normal, SlowScale), HudMotion.OutCubic(k));

        /// <summary>True while a moment is on.</summary>
        public static bool Active => s_count > 0;

        /// <summary>Starts a moment; dispose the result to end it. Does nothing outside play mode.</summary>
        public static IDisposable Begin()
        {
            if (!Application.isPlaying) return null;
            if (!s_inst)
            {
                var go = new GameObject("DecisionMoment");
                DontDestroyOnLoad(go);
                s_inst = go.AddComponent<DecisionMoment>();
            }
            if (s_count++ == 0)
            {
                s_inst._normal = Time.timeScale > 0.01f ? Time.timeScale : 1f;
                AiOn.Rendering.AtmosphereDirector.Focus(true);
                UiSound.Play("radio");
            }
            return new Handle();
        }

        static void End()
        {
            if (s_count <= 0) return;
            if (--s_count == 0) AiOn.Rendering.AtmosphereDirector.Focus(false);
        }

        /// <summary>Ends every moment at once (the overlay torn down mid-choice: its awaits never resume).</summary>
        public static void Reset() { while (s_count > 0) End(); }

        void Update()
        {
            float want = s_count > 0 ? 1f : 0f;
            if (Mathf.Approximately(_k, want)) return;
            _k = Mathf.MoveTowards(_k, want, Time.unscaledDeltaTime / EaseSeconds);
            // a pause (scale 0) set by someone else in the meantime is theirs: leave it
            if (Time.timeScale > 0.001f) Time.timeScale = _k <= 0f ? _normal : ScaleAt(_normal, _k);
        }

        void OnDestroy()
        {
            if (s_inst != this) return;
            if (_k > 0f && Time.timeScale > 0.001f) Time.timeScale = _normal;
            s_inst = null; s_count = 0;
        }

        sealed class Handle : IDisposable
        {
            bool _done;
            public void Dispose() { if (_done) return; _done = true; End(); }
        }
    }
}
