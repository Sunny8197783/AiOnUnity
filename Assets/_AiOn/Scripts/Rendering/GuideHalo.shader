// A soft camera-facing glow for guiding lights (exit signs, shelter entrances, supplies). Additive, no depth write;
// it thins out in fog only half as fast as the scene, so lit exits read through haze and smoke the way real ones do.
Shader "AiOn/GuideHalo"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Size ("Size (m)", Float) = 0.6
        _Core ("Core sharpness", Range(1, 8)) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size;
                float _Core;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // billboard: the quad's corners laid out in view space around the object's origin
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 centerVS = TransformWorldToView(centerWS);
                float2 corner = (v.uv - 0.5) * 2 * _Size;
                centerVS.xy += corner;
                centerVS.z += _Size * 0.5; // a little towards the camera, so a wall right behind does not cut it
                o.positionCS = TransformWViewToHClip(centerVS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2;
                float glow = saturate(1 - r);
                glow = pow(glow, _Core) + 0.35 * pow(glow, 1.5);
                half3 c = _Color.rgb * glow * _Color.a;
                // fog: half the scene's fade (the light scatters in it)
                c *= lerp(1, ComputeFogIntensity(i.fog), 0.5);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
