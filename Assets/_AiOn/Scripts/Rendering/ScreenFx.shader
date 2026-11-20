// AiOn screen effects for ScreenFxFeature (URP 17, Render Graph full-screen blit).
// Analog signal: keijiro/KinoGlitch's analogue glitch (scan line jitter, vertical jump, horizontal shake, colour drift)
// with CRT scan lines and static added. Mask view: a wet towel over nose and mouth or a respirator's twin lenses, with
// breath misting the view on each out-breath.
Shader "Hidden/AiOn/ScreenFx"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "AiOnScreenFx"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _SignalA; // intensity, scanlines, scanlineCount, jitter
            float4 _SignalB; // verticalJump, horizontalShake, colorDrift, noise
            float4 _MaskA;   // intensity, respirator, mist, breathRate (per minute)
            float4 _MaskRim;

            float nrand(float x, float y) { return frac(sin(dot(float2(x, y), float2(12.9898, 78.233))) * 43758.5453); }

            float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }

            float3 Signal(float2 uv, float3 orig)
            {
                float k = _SignalA.x;
                if (k <= 0.001) return orig;
                float t = _Time.y;
                // Kino: jitter displacement / threshold, jump time, drift time
                float jit = _SignalA.w * k;
                float jDisp = 0.002 + pow(jit, 3) * 0.05;
                float jThresh = saturate(1 - jit * 1.2);
                float j = nrand(uv.y, floor(t * 30) * 0.01) * 2 - 1;
                j *= step(jThresh, abs(j)) * jDisp;
                float jumpAmt = _SignalB.x * k;
                float v = lerp(uv.y, frac(uv.y + frac(t * jumpAmt * 11.3)), jumpAmt);
                float shake = (nrand(floor(t * 30), 2) - 0.5) * _SignalB.y * k * 0.2;
                float drift = sin(v + t * 606.11) * _SignalB.z * k * 0.04;
                float u = uv.x + j + shake;
                float3 a = Src(float2(frac(u), v));
                float3 b = Src(float2(frac(u + drift), v));
                float3 c = float3(a.r, b.g, a.b);

                // CRT scan lines (in screen-height lines, so they hold their size at any resolution)
                float s = 0.5 + 0.5 * sin(uv.y * _SignalA.z * 6.2831853);
                c *= lerp(1, 0.72 + 0.28 * s, _SignalA.y * k);
                // static snow and the odd bright interference band rolling down
                float n = nrand(uv.x * _ScreenParams.x + t * 7.0, uv.y * _ScreenParams.y + t * 13.0);
                float band = smoothstep(0.985, 1.0, sin((uv.y - t * 0.35) * 9.0)) * 0.35;
                c = lerp(c, n.xxx, _SignalB.w * k * 0.35) + band * _SignalB.w * k;
                return lerp(orig, c, saturate(k * 1.25));
            }

            float3 Mask(float2 uv, float3 col)
            {
                float k = _MaskA.x;
                if (k <= 0.001) return col;
                float t = _Time.y;
                // out-breath: a smooth hump once per breath, misting the view
                float phase = frac(t * _MaskA.w / 60.0);
                float breath = smoothstep(0.0, 0.25, phase) * (1 - smoothstep(0.35, 0.8, phase));
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = (uv - 0.5) * float2(aspect, 1);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                // mist scatters the scene's own light: brighter where the view is bright, a grey veil (not a white wall) at night
                float3 misted = lerp(col, lum * 0.7 + 0.06 + 0.12 * saturate(lum * 4), 0.7);

                // cloth: a soft, uneven fold at the bottom edge and mist rising from it
                float edge = 0.07 + 0.025 * sin(uv.x * 9.0 + 1.3) + 0.012 * sin(uv.x * 23.0);
                float cloth = 1 - smoothstep(edge, edge + 0.09, uv.y);
                float clothMist = (1 - smoothstep(0.0, 0.12 + _MaskA.z * (0.25 + 0.35 * breath), uv.y)) * (0.35 + 0.65 * breath);
                float3 cA = lerp(col, misted, clothMist * 0.8);
                cA = lerp(cA, _MaskRim.rgb * (0.6 + 0.4 * uv.x) + lum * 0.12, cloth); // the towel catches a little of the light

                // respirator: twin oval lenses, the rubber face piece around them, mist from below the lenses
                float2 r = float2(0.46, 0.40);
                float dl = length((p - float2(-0.40, 0.03)) / r), dr = length((p - float2(0.40, 0.03)) / r);
                float d = min(dl, dr);
                float lens = 1 - smoothstep(0.93, 1.0, d);
                float ring = smoothstep(0.8, 0.97, d) * lens;
                float lensMist = (1 - smoothstep(-0.45, -0.05 + _MaskA.z * 0.35 * breath, p.y)) * (0.25 + 0.75 * breath) * lens;
                float3 cB = lerp(col, misted, lensMist * 0.75);
                cB *= 1 - ring * 0.45;
                cB = lerp(_MaskRim.rgb * (0.7 + 0.3 * saturate(1.3 - d)), cB, lens);

                float3 masked = lerp(cA, cB, _MaskA.y);
                return lerp(col, masked, saturate(k));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 col = Src(uv);
                col = Signal(uv, col);
                col = Mask(uv, col);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
