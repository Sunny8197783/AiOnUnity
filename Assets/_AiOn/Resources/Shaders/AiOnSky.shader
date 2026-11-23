// AiOn sky: the original's sky rig (engine/sky.js) as one skybox shader for RenderSettings.skybox.
// Layers, in the original's render order: Preetham sky (day/dusk) -> night dome (by _Night) -> stars -> moon -> clouds,
// then a fade to the fog colour at and below the horizon so the sky meets fogged geometry without a seam.
//
// The Preetham part is ported from three.js examples/jsm/objects/Sky.js (MIT licence, (c) 2010-2024 three.js authors;
// based on "A Practical Analytic Model for Daylight", Preetham et al., and the implementations by Simon Wallner and
// Martin Upitis). DOME_FS and CLOUD_FS are ported from engine/sky.js of the original game.
//
// Frame: Unity world (x east, y up, z north). Every direction uniform is a Unity-space unit vector set from C# (SkyRig).
// Colours are linear RGB set with SetVector (no gamma conversion), like the original's THREE.Color linear arrays.
// Output is linear HDR and goes through the same post exposure + ACES as the scene (see SkyRig for _SkyExposure).
Shader "AiOn/Sky"
{
    Properties
    {
        [Header(Preetham)]
        _SunDir ("Sun direction (unit, world)", Vector) = (0, 0.77, -0.62, 0)
        _Turbidity ("Turbidity", Float) = 2
        _Rayleigh ("Rayleigh", Float) = 1
        _MieCoefficient ("Mie coefficient", Float) = 0.005
        _MieDirectionalG ("Mie directional G", Float) = 0.8
        _SkyGain ("Sky gain (preset sky.gain)", Float) = 1
        _SkyExposure ("Sky exposure (whole sky multiplier)", Float) = 1

        [Header(Night dome)]
        _Night ("Night amount", Range(0, 1)) = 0
        _NightZenith ("Zenith (linear)", Vector) = (0.003, 0.007, 0.026, 0)
        _NightHorizon ("Horizon (linear)", Vector) = (0.023, 0.036, 0.081, 0)
        _GlowColor ("City glow colour (linear)", Vector) = (1, 0.33, 0.09, 0)
        _Glow ("City glow", Float) = 0

        [Header(Stars and moon)]
        _Stars ("Star opacity", Range(0, 1)) = 0
        _StarScale ("Star cell scale", Float) = 120
        _StarDensity ("Star density (fraction of cells)", Range(0, 1)) = 0.027
        _MoonDir ("Moon direction (unit, world)", Vector) = (0.533, 0.559, -0.635, 0)
        _Moon ("Moon opacity", Range(0, 1)) = 0

        [Header(Clouds)]
        _CloudCover ("Cover", Range(0, 1)) = 0.3
        _CloudOpacity ("Opacity", Range(0, 1)) = 1
        _CloudLit ("Lit colour (linear, x gain)", Vector) = (0.9, 0.9, 0.9, 0)
        _CloudShade ("Shade colour (linear, x gain)", Vector) = (0.35, 0.4, 0.5, 0)
        _CloudSunDir ("Cloud light direction (unit, world)", Vector) = (0, 1, 0, 0)
        _CloudTime ("Cloud time (s, from C#)", Float) = 0

        [Header(Photographed sky)]
        [NoScaleOffset] _HdriTex ("Sky photo A (equirect, LDR, sun painted out)", 2D) = "black" {}
        [NoScaleOffset] _HdriTex2 ("Sky photo B (cross-fade target)", 2D) = "black" {}
        _Hdri ("Photo weight", Range(0, 1)) = 0
        _HdriMix ("A -> B", Range(0, 1)) = 0
        _HdriGain ("Photo gain A (linear)", Float) = 1
        _HdriGain2 ("Photo gain B (linear)", Float) = 1
        _HdriYaw ("Photo yaw A (turns)", Float) = 0
        _HdriYaw2 ("Photo yaw B (turns)", Float) = 0
        _PhotoSun ("Sun disc colour over the photo (linear)", Vector) = (1, 0.95, 0.88, 0)

        [Header(Horizon)]
        _FogColor ("Fog colour (linear)", Vector) = (0.5, 0.55, 0.6, 0)
        _HorizonHaze ("Haze above the horizon (0..1)", Range(0, 1)) = 0
        _HazeFalloff ("Haze falloff (1 / band height in dir.y)", Float) = 5
        _BelowFade ("Below-horizon fade depth (dir.y)", Float) = 0.04
        _SkyVis ("Sky visible (0 = fog colour only)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define AION_PI 3.141592653589793
            #define CLOUD_OCT 4

            float4 _SunDir;
            float _Turbidity, _Rayleigh, _MieCoefficient, _MieDirectionalG, _SkyGain, _SkyExposure;
            float _Night, _Glow;
            float4 _NightZenith, _NightHorizon, _GlowColor;
            float _Stars, _StarScale, _StarDensity, _Moon;
            float4 _MoonDir;
            float _CloudCover, _CloudOpacity, _CloudTime;
            float4 _CloudLit, _CloudShade, _CloudSunDir;
            float4 _FogColor;
            sampler2D _HdriTex, _HdriTex2;
            float _Hdri, _HdriMix, _HdriGain, _HdriGain2, _HdriYaw, _HdriYaw2;
            float4 _PhotoSun;

            // a photographed sky (Poly Haven, CC0) as an equirect: azimuth from north (+z) toward east (+x), turned by yaw so
            // its sun side faces the preset's sun; sampled at mip 0 (no seam where the azimuth wraps)
            float3 Photo(sampler2D tex, float yaw, float gain, float3 d)
            {
                float u = frac(atan2(d.x, d.z) * 0.15915494 + yaw);
                float v = 0.5 + asin(clamp(d.y, -1.0, 1.0)) * 0.31830989;
                return tex2Dlod(tex, float4(u, v, 0, 0)).rgb * gain;
            }
            float _HorizonHaze, _HazeFalloff, _BelowFade, _SkyVis;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float3 betaR : TEXCOORD1;
                float3 betaM : TEXCOORD2;
                float2 sun : TEXCOORD3; // x: sunE, y: sunfade
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------------- Preetham (three.js Sky.js vertex part) ----------------
            static const float3 totalRayleigh = float3(5.804542996261093E-6, 1.3562911419845635E-5, 3.0265902468824876E-5);
            static const float3 MieConst = float3(1.8399918514433978E14, 2.7798023919660528E14, 4.0790479543861094E14);
            static const float cutoffAngle = 1.6110731556870734;
            static const float steepness = 1.5;
            static const float EE = 1000.0;

            float sunIntensity(float zenithAngleCos)
            {
                zenithAngleCos = clamp(zenithAngleCos, -1.0, 1.0);
                return EE * max(0.0, 1.0 - exp(-((cutoffAngle - acos(zenithAngleCos)) / steepness)));
            }

            float3 totalMie(float T)
            {
                float c = (0.2 * T) * 10E-18;
                return 0.434 * c * MieConst;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);

                float3 sunDir = normalize(_SunDir.xyz);
                float sunE = sunIntensity(sunDir.y);
                // three.js passes a unit sunPosition here too, so the fade is ~1 (kept for parity).
                float sunfade = 1.0 - saturate(1.0 - exp(sunDir.y / 450000.0));
                float rayleighCoefficient = _Rayleigh - (1.0 * (1.0 - sunfade));
                o.betaR = totalRayleigh * rayleighCoefficient;
                o.betaM = totalMie(_Turbidity) * _MieCoefficient;
                o.sun = float2(sunE, sunfade);
                return o;
            }

            // ---------------- Preetham (three.js Sky.js fragment part) ----------------
            static const float rayleighZenithLength = 8.4E3;
            static const float mieZenithLength = 1.25E3;
            static const float sunAngularDiameterCos = 0.9999566769464484;
            static const float THREE_OVER_SIXTEENPI = 0.05968310365946075;
            static const float ONE_OVER_FOURPI = 0.07957747154594767;

            float rayleighPhase(float cosTheta) { return THREE_OVER_SIXTEENPI * (1.0 + cosTheta * cosTheta); }
            float hgPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                float inverse = 1.0 / pow(max(1e-6, 1.0 - 2.0 * g * cosTheta + g2), 1.5);
                return ONE_OVER_FOURPI * ((1.0 - g2) * inverse);
            }

            float3 Preetham(float3 direction, v2f i)
            {
                float3 sunDir = normalize(_SunDir.xyz);
                float sunE = i.sun.x, sunfade = i.sun.y;
                float zenithAngle = acos(max(0.0, direction.y));
                float inverse = 1.0 / (cos(zenithAngle) + 0.15 * pow(93.885 - ((zenithAngle * 180.0) / AION_PI), -1.253));
                float sR = rayleighZenithLength * inverse;
                float sM = mieZenithLength * inverse;
                float3 Fex = exp(-(i.betaR * sR + i.betaM * sM));

                float cosTheta = dot(direction, sunDir);
                float rPhase = rayleighPhase(cosTheta * 0.5 + 0.5);
                float3 betaRTheta = i.betaR * rPhase;
                float mPhase = hgPhase(cosTheta, _MieDirectionalG);
                float3 betaMTheta = i.betaM * mPhase;

                float3 scatter = sunE * ((betaRTheta + betaMTheta) / (i.betaR + i.betaM));
                float3 Lin = pow(max(0.0, scatter * (1.0 - Fex)), 1.5);
                Lin *= lerp(float3(1, 1, 1), pow(max(0.0, scatter * Fex), 0.5), saturate(pow(1.0 - sunDir.y, 5.0)));

                float3 L0 = 0.1 * Fex;
                float sundisk = smoothstep(sunAngularDiameterCos, sunAngularDiameterCos + 0.00002, cosTheta);
                L0 += (sunE * 19000.0 * Fex) * sundisk;

                float3 texColor = (Lin + L0) * 0.04 + float3(0.0, 0.0003, 0.00075);
                float3 retColor = pow(max(0.0, texColor), 1.0 / (1.2 + (1.2 * sunfade)));
                return retColor * _SkyGain;
            }

            // ---------------- Night dome (sky.js DOME_FS) ----------------
            float3 NightDome(float3 d)
            {
                float h = max(d.y, 0.0);
                float3 c = lerp(_NightHorizon.rgb, _NightZenith.rgb, pow(max(h, 1e-5), 0.45)) + _GlowColor.rgb * _Glow * exp(-h * 9.0) * 0.08;
                if (d.y < 0.0) c = _NightHorizon.rgb * 0.6 + _GlowColor.rgb * _Glow * 0.05;
                return c;
            }

            // ---------------- Stars: one candidate per cell of a 3D grid, owned by the cell its sphere point lies in ----------------
            float3 hash33(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            // r: star radius, computed in frag outside flow control (fwidth inside the branch would flatten it, X4121)
            float3 StarField(float3 d, float r)
            {
                float scale = max(_StarScale, 1.0);
                float3 base = floor(d * scale);
                float3 acc = 0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 cell = base + float3(x, y, z);
                    float3 h = hash33(cell);
                    if (h.x >= _StarDensity) continue;
                    float3 j = hash33(cell + 71.37);
                    float3 s = normalize((cell + j) / scale);
                    if (any(floor(s * scale) != cell)) continue; // the cell does not own this point of the sphere: no duplicates
                    float dist = length(d - s);
                    float b = 0.25 + 1.8 * pow(h.y, 7.0);
                    float t = h.z;
                    float3 tint = t < 0.15 ? float3(1.0, 0.85, 0.7) : (t < 0.35 ? float3(0.8, 0.9, 1.0) : float3(1, 1, 1));
                    acc += tint * b * exp(-(dist * dist) / (r * r));
                }
                return acc;
            }

            // ---------------- Clouds (sky.js CLOUD_FS) ----------------
            float h2(float2 p)
            {
                float3 q = frac(p.xyx * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(h2(i), h2(i + float2(1, 0)), f.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < CLOUD_OCT; i++) { s += a * vn(p); p = p * 2.07 + float2(1.7, 9.2); a *= 0.5; }
                return s;
            }

            // ---------------- Moon: disc (#e8ecf2 x2.5, a few craters) and halo, additive like the original sprite ----------------
            float3 Moon(float3 d)
            {
                float3 m = normalize(_MoonDir.xyz);
                float3 right = normalize(cross(float3(0, 1, 0), m));
                float3 up2 = cross(m, right);
                float2 q = float2(dot(d, right), dot(d, up2));
                float ahead = dot(d, m);
                if (ahead <= 0.0) return 0;
                float dist = length(q);
                // sprite 145 m at 1500 m, disc r = 30/256 of it, halo from 24/128 to 128/128 of the half size
                const float discR = 0.0113, haloR0 = 0.0091, haloR1 = 0.0483;
                float k = saturate((dist - haloR0) / (haloR1 - haloR0));
                float3 haloA = float3(0.578, 0.679, 1.0) * 0.35;   // rgba(200,215,255,0.35)
                float3 haloB = float3(0.352, 0.456, 0.791) * 0.08; // rgba(160,180,230,0.08)
                float3 halo = k < 0.35 ? lerp(haloA, haloB, k / 0.35) : lerp(haloB, float3(0, 0, 0), (k - 0.35) / 0.65);
                float2 lp = q / discR;
                float crater = smoothstep(0.55, 0.78, vn(lp * 2.6 + 3.7)) * 0.3 + smoothstep(0.6, 0.85, vn(lp * 5.3 + 11.1)) * 0.2;
                float3 disc = lerp(float3(0.807, 0.838, 0.888), float3(0.158, 0.181, 0.223), crater);
                float inDisc = 1.0 - smoothstep(0.96, 1.0, dist / discR);
                return lerp(halo, disc, inDisc) * 2.5;
            }

            // rgb: colour, a: coverage
            float4 Clouds(float3 d)
            {
                if (d.y <= 0.0) return 0;
                // the original's xz is three.js (z south) = Unity (x, -z)
                float2 dxz = float2(d.x, -d.z);
                float2 uv = dxz / (d.y + 0.1) * 1.6 + float2(_CloudTime * 0.012, _CloudTime * 0.004);
                float n = fbm(uv), edge = 1.0 - _CloudCover;
                float a = smoothstep(edge, edge + 0.22, n), thick = smoothstep(edge, edge + 0.5, n);
                float sun = pow(max(dot(d, normalize(_CloudSunDir.xyz)), 0.0), 8.0);
                float3 c = lerp(_CloudLit.rgb, _CloudShade.rgb, thick * 0.75) + _CloudLit.rgb * sun * (1.0 - thick) * 0.8;
                return float4(c, a * smoothstep(0.0, 0.2, d.y) * _CloudOpacity);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // star size: about 1-1.5 px wide like the original's 2.2 px points, whatever the resolution
                float starR = clamp(length(fwidth(d)) * 0.8, 0.0004, 0.004);

                float3 col = Preetham(d, i);
                if (_Hdri > 0.002)
                {
                    // the photo over the analytic sky; the sun disc and its glow stay the preset's own (painted out of the photo)
                    float3 sunDir = normalize(_SunDir.xyz);
                    float cs = dot(d, sunDir);
                    float3 photo = Photo(_HdriTex, _HdriYaw, _HdriGain, d);
                    if (_HdriMix > 0.002) photo = lerp(photo, Photo(_HdriTex2, _HdriYaw2, _HdriGain2, d), _HdriMix);
                    // a photographed clear zenith is a deep blue, darker than the analytic sky the scene's light matches (a
                    // navy sky over a sunlit street): lift it toward the analytic brightness, never darken it, so clouds
                    // brighter than the analytic sky keep their contrast
                    const float3 lumW = float3(0.2126, 0.7152, 0.0722);
                    float lp = max(dot(photo, lumW), 1e-4), la = max(dot(col, lumW), 1e-4);
                    photo *= clamp(pow(la / lp, 0.8), 1.0, 3.0);
                    float3 sunCol = _PhotoSun.rgb;
                    float sun = smoothstep(0.99996, 0.99999, cs) * 60.0 + pow(max(cs, 0.0), 900.0) * 3.0 + pow(max(cs, 0.0), 24.0) * 0.25;
                    photo += sunCol * sun * step(-0.03, sunDir.y);
                    col = lerp(col, photo, _Hdri * smoothstep(-0.02, 0.04, d.y));
                }
                if (_Night > 0.002) col = lerp(col, NightDome(d), _Night);
                float above = smoothstep(0.0, 0.08, d.y);
                if (_Stars > 0.005) col += StarField(d, starR) * (_Stars * above);
                if (_Moon > 0.005) col += Moon(d) * _Moon;
                if (_CloudOpacity > 0.01) { float4 c = Clouds(d); col = lerp(col, c.rgb, c.a); }
                col = min(col * _SkyExposure, 64.0); // the sun disc is ~1e2: keep it finite for bloom

                // Horizon: haze above, the fog colour itself below, so fogged geometry meets the sky without a seam.
                float3 fogC = _FogColor.rgb;
                // a Gaussian band: flat at the horizon (where the fogged distant skyline stands), gone a few tens of degrees up
                float hy = max(d.y, 0.0) * _HazeFalloff;
                float haze = _HorizonHaze * exp(-hy * hy);
                float below = saturate(-d.y / max(_BelowFade, 1e-4));
                below = below * below * (3.0 - 2.0 * below);
                col = lerp(col, fogC, saturate(max(haze, below)));
                col = lerp(fogC, col, _SkyVis);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
