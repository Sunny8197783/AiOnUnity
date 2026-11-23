// AiOn/Surface: the level models' glTF materials with real-world surface detail ("그래픽 사실화").
//
// The levels came from the original's three.js builders: flat colours and small canvas pictures (tiles, planks, bricks)
// with no normal or roughness maps. This shader keeps each material exactly as glTFast drew it (same property names, so
// LevelSurfaces swaps the shader in place and LevelMaterials keeps writing colours, emission and pictures into it) and
// lays a world-space triplanar PBR detail over it: a CC0 normal map, roughness variation and albedo/AO variation measured
// in metres (Resources/Surfaces), plus a slow macro variation that breaks the tiling. URP forward(+) lighting with
// shadows, SSAO, decals (DBuffer), reflection probes, fog; ShadowCaster, DepthOnly and DepthNormals passes.
Shader "AiOn/Surface"
{
    Properties
    {
        baseColorFactor ("Base Color", Color) = (1,1,1,1)
        baseColorTexture ("Base Map", 2D) = "white" {}
        baseColorTexture_Rotation ("Base Map Rotation", Float) = 0
        roughnessFactor ("Roughness", Range(0,1)) = 1
        metallicFactor ("Metallic", Range(0,1)) = 0
        [HDR] emissiveFactor ("Emission", Color) = (0,0,0,1)
        emissiveTexture ("Emission Map", 2D) = "white" {}
        alphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        normalTexture ("Normal Map (the model's own)", 2D) = "bump" {}
        normalTexture_scale ("Normal Scale", Float) = 1
        [HideInInspector] _HasNormalMap ("Has normal map", Float) = 0

        [NoScaleOffset] _DetailN ("Detail normal XY + roughness (B)", 2D) = "bump" {}
        [NoScaleOffset] _DetailA ("Detail albedo x AO (0.5 = neutral)", 2D) = "grey" {}
        _DetailScale ("Detail tiles per metre", Float) = 0.5
        _DetailNormal ("Detail normal strength", Range(0,2)) = 0.8
        _DetailAlbedo ("Detail albedo strength", Range(0,1)) = 0.5
        _DetailRough ("Detail roughness variation", Range(0,8)) = 2
        _DetailRoughMean ("Detail roughness mean", Range(0,1)) = 0.7
        _RoughOffset ("Roughness offset", Range(-1,1)) = 0
        _Macro ("Macro variation", Range(0,1)) = 0.25

        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _Surface ("Surface", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 baseColorFactor;
        float4 baseColorTexture_ST;
        float baseColorTexture_Rotation;
        float roughnessFactor;
        float metallicFactor;
        float4 emissiveFactor;
        float4 emissiveTexture_ST;
        float alphaCutoff;
        float4 normalTexture_ST;
        float normalTexture_scale, _HasNormalMap;
        float _DetailScale, _DetailNormal, _DetailAlbedo, _DetailRough, _DetailRoughMean, _RoughOffset, _Macro;
        float _Cull, _Surface;
    CBUFFER_END

    TEXTURE2D(baseColorTexture); SAMPLER(samplerbaseColorTexture);
    TEXTURE2D(emissiveTexture); SAMPLER(sampleremissiveTexture);
    TEXTURE2D(_DetailN); SAMPLER(sampler_DetailN);
    TEXTURE2D(normalTexture); SAMPLER(samplernormalTexture);
    TEXTURE2D(_DetailA); SAMPLER(sampler_DetailA);

    float2 BaseUV(float2 uv)
    {
        return uv * baseColorTexture_ST.xy + baseColorTexture_ST.zw;
    }

    // Triplanar blend weights (sharp, so straight walls take one projection).
    float3 TriWeights(float3 n)
    {
        float3 w = pow(abs(n), 6.0);
        return w / max(1e-5, w.x + w.y + w.z);
    }

    // Detail at a world position and geometric normal: the blended world normal, the albedo multiplier, the roughness delta.
    void SurfaceDetail(float3 posWS, float3 nWS, out float3 normalWS, out half albedoMul, out half roughDelta)
    {
        float3 w = TriWeights(nWS);
        float3 p = posWS * _DetailScale;
        float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;

        half4 nX = SAMPLE_TEXTURE2D(_DetailN, sampler_DetailN, uvX);
        half4 nY = SAMPLE_TEXTURE2D(_DetailN, sampler_DetailN, uvY);
        half4 nZ = SAMPLE_TEXTURE2D(_DetailN, sampler_DetailN, uvZ);
        half aX = SAMPLE_TEXTURE2D(_DetailA, sampler_DetailA, uvX).r;
        half aY = SAMPLE_TEXTURE2D(_DetailA, sampler_DetailA, uvY).r;
        half aZ = SAMPLE_TEXTURE2D(_DetailA, sampler_DetailA, uvZ).r;

        half2 tX = (nX.rg * 2 - 1) * _DetailNormal;
        half2 tY = (nY.rg * 2 - 1) * _DetailNormal;
        half2 tZ = (nZ.rg * 2 - 1) * _DetailNormal;
        // whiteout blend (Ben Golus, "Normal Mapping for a Triplanar Shader"); the tangent x flips on the faces that look
        // down an axis so the detail is not mirrored, and the surface's own sign stays in z (ceilings keep facing down)
        float3 sgn = sign(nWS) + (nWS == 0);
        tX.x *= sgn.x; tY.x *= sgn.y; tZ.x *= -sgn.z;
        float3 bX = float3(tX + nWS.zy, nWS.x);
        float3 bY = float3(tY + nWS.xz, nWS.y);
        float3 bZ = float3(tZ + nWS.xy, nWS.z);
        normalWS = normalize(bX.zyx * w.x + bY.xzy * w.y + bZ.xyz * w.z);

        half a = aX * w.x + aY * w.y + aZ * w.z;
        half r = nX.b * w.x + nY.b * w.y + nZ.b * w.z;

        // macro variation: the same map at about a ninth of the frequency along the dominant plane (breaks the tiling)
        float2 uvM = w.y > max(w.x, w.z) ? uvY : (w.x > w.z ? uvX : uvZ);
        half m = SAMPLE_TEXTURE2D(_DetailA, sampler_DetailA, uvM * 0.113 + 0.37).r;

        albedoMul = lerp(1.0h, a * 2.0h, (half)_DetailAlbedo) * lerp(1.0h, m * 2.0h, (half)_Macro);
        roughDelta = (r - (half)_DetailRoughMean) * (half)_DetailRough + (half)_RoughOffset + (0.5h - m) * (half)_Macro * 0.3h;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half3 vertexSH : TEXCOORD4;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs vp = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionWS = vp.positionWS;
                o.positionCS = vp.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(vp.positionCS.z);
                o.vertexSH = SampleSHVertex(o.normalWS);
                return o;
            }

            void Frag(Varyings i, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 nGeo = normalize(i.normalWS);
                float2 uv = BaseUV(i.uv);
                if (_HasNormalMap > 0.5)
                {
                    // the model's own normal map (a few floors and walls had one), on a cotangent frame from the screen
                    // derivatives (the level meshes carry no tangents); glTFast flips v on import, so the bitangent flips
                    float2 nuv = i.uv * normalTexture_ST.xy + normalTexture_ST.zw;
                    half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(normalTexture, samplernormalTexture, nuv), normalTexture_scale);
                    float3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                    float2 duv1 = ddx(nuv), duv2 = ddy(nuv);
                    float3 dp2perp = cross(dp2, nGeo), dp1perp = cross(nGeo, dp1);
                    float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
                    float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
                    float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
                    nGeo = normalize(tn.x * T * invmax - tn.y * B * invmax + tn.z * nGeo);
                }
                float3 nWS; half albedoMul, roughDelta;
                SurfaceDetail(i.positionWS, nGeo, nWS, albedoMul, roughDelta);

                half4 baseTex = SAMPLE_TEXTURE2D(baseColorTexture, samplerbaseColorTexture, uv);
                half3 albedo = baseTex.rgb * baseColorFactor.rgb * albedoMul;

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = metallicFactor;
                s.smoothness = saturate(1.0h - saturate((half)roughnessFactor + roughDelta));
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);
                s.emission = emissiveFactor.rgb * SAMPLE_TEXTURE2D(emissiveTexture, sampleremissiveTexture, i.uv * emissiveTexture_ST.xy + emissiveTexture_ST.zw).rgb;
                s.specular = 0;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = NormalizeNormalPerPixel(nWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #else
                    d.shadowCoord = float4(0, 0, 0, 0);
                #endif
                d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.bakedGI = SampleSHPixel(i.vertexSH, d.normalWS);
                d.shadowMask = half4(1, 1, 1, 1);

                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(i.positionCS, s, d);
                #endif

                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                color.a = 1;
                outColor = color;
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V ShadowVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                return o;
            }
            half4 ShadowFrag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            V DepthVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half DepthFrag(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormVert
            #pragma fragment NormFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            V NormVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            void NormFrag(V i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
