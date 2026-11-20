using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace AiOn.Rendering
{
    /// <summary>
    /// URP renderer feature for the screen effects in <see cref="AnalogSignal"/> and <see cref="MaskView"/>: one full-screen
    /// pass after post-processing (so scan lines and static are not bloomed or tone-mapped), recorded only while either
    /// volume override is active. Game cameras only; the Scene view stays clean.
    /// </summary>
    public sealed class ScreenFxFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader shader;
        [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        Material _material;
        ScreenFxPass _pass;

        public override void Create()
        {
            if (!shader) shader = Shader.Find("Hidden/AiOn/ScreenFx");
            _pass = new ScreenFxPass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            var stack = VolumeManager.instance.stack;
            var signal = stack.GetComponent<AnalogSignal>();
            var mask = stack.GetComponent<MaskView>();
            bool on = (signal != null && signal.IsActive()) || (mask != null && mask.IsActive());
            if (!on) return;
            if (!_material)
            {
                if (!shader) return;
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            _pass.Setup(_material, signal, mask);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        sealed class ScreenFxPass : ScriptableRenderPass
        {
            static readonly int SignalA = Shader.PropertyToID("_SignalA"); // intensity, scanlines, scanlineCount, jitter
            static readonly int SignalB = Shader.PropertyToID("_SignalB"); // jump, shake, drift, noise
            static readonly int MaskA = Shader.PropertyToID("_MaskA");     // intensity, respirator, mist, breathRate
            static readonly int MaskRim = Shader.PropertyToID("_MaskRim");

            Material _material;

            public ScreenFxPass() { requiresIntermediateTexture = true; }

            public void Setup(Material material, AnalogSignal s, MaskView m)
            {
                _material = material;
                if (s != null)
                {
                    material.SetVector(SignalA, new Vector4(s.intensity.value, s.scanlines.value, s.scanlineCount.value, s.jitter.value));
                    material.SetVector(SignalB, new Vector4(s.verticalJump.value, s.horizontalShake.value, s.colorDrift.value, s.noise.value));
                }
                else material.SetVector(SignalA, Vector4.zero);
                if (m != null)
                {
                    material.SetVector(MaskA, new Vector4(m.intensity.value, m.respirator.value, m.mist.value, m.breathRate.value));
                    material.SetColor(MaskRim, m.rimColor.value);
                }
                else material.SetVector(MaskA, Vector4.zero);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !_material) return;
                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_AiOnScreenFx";
                desc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0), "AiOn Screen Fx");
                resources.cameraColor = destination;
            }
        }
    }
}
