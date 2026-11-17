// Additive textured glow (the town cars' headlight pools on the road): _BaseMap.a x _BaseColor.a scales _BaseColor,
// added on top (three.js MeshBasicMaterial with AdditiveBlending, depthWrite false). Fog fades the glow to nothing.
// Lives in Resources so it ships in builds (URP's Particles/Unlit is stripped unless a shipped material uses it).
Shader "AiOn/AdditiveGlow"
{
    Properties
    {
        _BaseMap ("Mask (alpha)", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                half3 c = _BaseColor.rgb * a;
                c = MixFogColor(c, half3(0, 0, 0), i.fogFactor);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
