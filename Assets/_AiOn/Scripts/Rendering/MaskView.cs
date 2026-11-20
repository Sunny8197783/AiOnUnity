using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AiOn.Rendering
{
    /// <summary>
    /// The view through something worn over the face: a wet towel held over nose and mouth (cloth at the lower edge,
    /// breath misting the bottom of the view) or a respirator (twin lens rims, misting on the out-breath).
    /// </summary>
    [Serializable, VolumeComponentMenu("AiOn/Mask View")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class MaskView : VolumeComponent
    {
        [Tooltip("Overall strength (0 = off).")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        [Tooltip("0 = cloth over nose and mouth, 1 = respirator with lenses (blends between them).")]
        public ClampedFloatParameter respirator = new(0f, 0f, 1f);
        [Tooltip("How far breath mist reaches up the view.")]
        public ClampedFloatParameter mist = new(0.5f, 0f, 1f);
        [Tooltip("Breaths per minute.")]
        public ClampedFloatParameter breathRate = new(16f, 6f, 40f);
        [Tooltip("Colour of the cloth / rubber rim.")]
        public ColorParameter rimColor = new(new Color(0.012f, 0.013f, 0.015f), false, false, true);

        public bool IsActive() => intensity.value > 0.001f;
    }
}
