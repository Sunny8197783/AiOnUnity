using UnityEngine;

namespace AiOn.UI
{
    /// <summary>
    /// The numbers behind the danger feedback (red edge pulse + heartbeat). Kept inside comfort limits: the pulse never
    /// passes 2.1 beats a second (well under 3 flashes a second), the red is muted and capped, and reduce-motion swaps the
    /// pulse for a steady edge.
    /// </summary>
    public static class DangerMath
    {
        /// <summary>The heartbeat starts above this danger level.</summary>
        public const float HeartFrom = 0.35f;
        /// <summary>The second beat ("dub") follows the first by this many seconds, in the sound and on screen.</summary>
        public const float DubAfter = 0.17f;

        /// <summary>0..1 from tension (0..1 already), health (0..100; counts under 45) and breathed smoke (0..1).</summary>
        public static float Level(float tension01, float health, float smoke) =>
            Mathf.Clamp01(Mathf.Max(Mathf.Clamp01(tension01), Mathf.Max(Mathf.Clamp01((45f - health) / 45f), Mathf.Clamp01(smoke) * 0.9f)));

        static float Over(float danger) => Mathf.InverseLerp(HeartFrom, 1f, danger);

        public static float Bpm(float danger) => Mathf.Lerp(64f, 126f, Over(danger));
        public static float HeartVolume(float danger) => Mathf.Lerp(0.18f, 0.55f, Over(danger));

        static float Bump(float t) => t < 0 ? 0 : t < 0.03f ? t / 0.03f : Mathf.Exp(-(t - 0.03f) / 0.09f);

        /// <summary>The pulse 0..1 this long after a beat started: lub, then a softer dub.</summary>
        public static float Beat(float sinceBeat) => Mathf.Max(Bump(sinceBeat), 0.6f * Bump(sinceBeat - DubAfter));

        /// <summary>Alpha of the red edge: the tension's steady share plus the beat (or a small steady share under reduce-motion).</summary>
        public static float EdgeAlpha(float tension01, float danger, float beat, bool reduceMotion)
        {
            float steady = Mathf.Min(Mathf.Clamp01(tension01) * 0.3f, 0.25f);
            float d = Mathf.Clamp01(danger);
            return steady + (reduceMotion ? 0.05f * d : 0.14f * d * Mathf.Clamp01(beat));
        }
    }
}
