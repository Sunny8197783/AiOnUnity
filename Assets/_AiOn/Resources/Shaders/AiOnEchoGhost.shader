// The '내 기록' echo: a soft see-through silhouette of a person (the last run walking its path again). A depth-only pass
// first, so the figure's own arms and legs do not stack into brighter patches, then a pale colour that is faint across the
// body and stronger toward the outline (a fresnel rim), with a slow vertical shimmer. Alpha is _BaseColor.a (the runtime
// fades it near the camera through a property block). Hair and eyelash cards keep their cut-out: the runtime gives that
// slot a copy with the person's _BaseMap and _Cutoff (both passes clip), the body uses the white default (nothing clipped).
// Drawn after the other transparent things (Transparent+10): its depth pass then never cuts the echo path lines or smoke
// sorted after it. Lives in Resources so it ships in builds.
Shader "AiOn/EchoGhost"
{
    Properties
    {
        _BaseColor ("Colour (alpha = opacity)", Color) = (0.435, 0.659, 1, 0.22)
        _BaseMap ("Cut-out map (alpha)", 2D) = "white" {}
        _Cutoff ("Alpha cut-off", Range(0, 1)) = 0
        _Rim ("Rim strength", Range(0, 4)) = 1.7
        _RimPower ("Rim sharpness", Range(0.5, 8)) = 2.5
        _Body ("Body fill", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float4 _BaseMap_ST; half _Cutoff; half _Rim; half _RimPower; half _Body;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff - 1e-4);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff - 1e-4);
                half3 n = normalize(i.normalWS);
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1 - saturate(abs(dot(n, view))), _RimPower);
                half shimmer = 0.85 + 0.15 * sin(i.positionWS.y * 9.0 - _Time.y * 1.6);
                half a = _BaseColor.a * saturate(_Body + rim * _Rim) * shimmer;
                half3 c = lerp(_BaseColor.rgb, half3(0.85, 0.92, 1), 0.35 + rim * 0.4); // paler than the base blue: a memory, not a hologram
                c = MixFog(c, i.fogFactor);
                return half4(c, saturate(a));
            }
            ENDHLSL
        }
    }
}
