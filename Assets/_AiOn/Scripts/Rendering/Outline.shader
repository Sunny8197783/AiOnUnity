// Outline for the thing the player can use: the mesh drawn again, back faces only, pushed out by a fixed number of
// pixels (inverted hull). The push mixes the vertex normal with the direction from the mesh's centre, so the flat-shaded
// low-poly models (split normals at every edge) keep a closed line at their corners.
Shader "AiOn/Outline"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.24, 0.78, 0.71, 1)
        _Width ("Width (px)", Range(0.5, 8)) = 2.5
        _Center ("Centre (object space)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float4 _Center;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 fromCenter = v.positionOS.xyz - _Center.xyz;
                float3 dirOS = normalize(v.normalOS + normalize(fromCenter + 1e-5) * 0.9);
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 dirWS = TransformObjectToWorldDir(dirOS);
                float2 dirCS = normalize(mul((float3x3)UNITY_MATRIX_VP, dirWS).xy + 1e-6);
                // a constant width in pixels whatever the distance
                cs.xy += dirCS * (_Width * 2.0 / _ScreenParams.xy) * cs.w;
                o.positionCS = cs;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
