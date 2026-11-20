using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AiOn.Rendering
{
    /// <summary>
    /// A weak analogue TV/radio signal over the view (after keijiro/KinoGlitch's analogue glitch, rewritten for URP 17
    /// Render Graph): CRT scanlines, line jitter, a vertical roll, a horizontal shake, colour drift and static. Drawn by
    /// <see cref="ScreenFxFeature"/>; blended like any volume override, so a profile can carry it at any weight.
    /// </summary>
    [Serializable, VolumeComponentMenu("AiOn/Analog Signal")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class AnalogSignal : VolumeComponent
    {
        [Tooltip("Overall strength of the signal look (0 = off).")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        [Tooltip("Dark CRT scan lines.")]
        public ClampedFloatParameter scanlines = new(0.5f, 0f, 1f);
        [Tooltip("Scan lines per screen height.")]
        public ClampedFloatParameter scanlineCount = new(360f, 120f, 1080f);
        [Tooltip("Rows that slip sideways (Kino scan line jitter).")]
        public ClampedFloatParameter jitter = new(0.2f, 0f, 1f);
        [Tooltip("The picture rolling vertically (Kino vertical jump).")]
        public ClampedFloatParameter verticalJump = new(0f, 0f, 1f);
        [Tooltip("The whole picture shaking sideways.")]
        public ClampedFloatParameter horizontalShake = new(0f, 0f, 1f);
        [Tooltip("Red and blue drifting apart.")]
        public ClampedFloatParameter colorDrift = new(0.2f, 0f, 1f);
        [Tooltip("Static snow.")]
        public ClampedFloatParameter noise = new(0.15f, 0f, 1f);

        public bool IsActive() => intensity.value > 0.001f;
    }
}
