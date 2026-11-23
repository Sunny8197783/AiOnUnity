// Recolours one band of a texture (Graphics.Blit): pixels near _FromHue (any hue when _HueWidth >= 0.5) inside the
// saturation and value windows take _ToHue, their saturation scaled (plus _SatAdd for greys) and value scaled. The CPR
// studio makes the neighbour's blue jumper from a dark leather jacket this way (skin, of another hue, saturation or value,
// stays as it is), and the volunteer's safety vest from a waistcoat: with _UseRects only pixels inside map region A, or
// inside region B and darker than _MaxValB, change, and silver bands (reflective tape) cross the changed pixels at up to
// four v positions (_BandV, _BandHalf wide each way). Values are the linear ones the blit reads.
Shader "Hidden/AiOn/HueSwap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FromHue ("From hue (0..1)", Float) = 0
        _HueWidth ("Hue half width", Float) = 0.06
        _MinSat ("Min saturation", Float) = 0.45
        _MaxSat ("Max saturation", Float) = 2
        _MinVal ("Min value", Float) = -1
        _MaxVal ("Max value", Float) = 2
        _ToHue ("To hue (0..1)", Float) = 0.6
        _SatAdd ("Saturation added", Float) = 0
        _SatScale ("Saturation scale", Float) = 1
        _ValScale ("Value scale", Float) = 1
        _ValAdd ("Value added", Float) = 0
        _UseRects ("Only inside the map regions", Float) = 0
        _RectA ("Region A (u0 v0 u1 v1)", Vector) = (0, 0, 0, 0)
        _RectB ("Region B (u0 v0 u1 v1)", Vector) = (0, 0, 0, 0)
        _MaxValB ("Region B max value", Float) = 2
        _BandV ("Band v positions", Vector) = (-1, -1, -1, -1)
        _BandHalf ("Band half width (v)", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FromHue, _HueWidth, _MinSat, _MaxSat, _MinVal, _MaxVal, _ToHue, _SatScale, _ValScale, _SatAdd, _ValAdd;
            float _UseRects, _MaxValB, _BandHalf;
            float4 _RectA, _RectB, _BandV;

            float Inside(float2 uv, float4 r) { return step(r.x, uv.x) * step(uv.x, r.z) * step(r.y, uv.y) * step(uv.y, r.w); }

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return c.z * lerp(float3(1, 1, 1), saturate(p - 1.0), c.y);
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);
                float3 hsv = rgb2hsv(col.rgb);
                float dh = abs(frac(hsv.x - _FromHue + 0.5) - 0.5);
                // a soft edge in hue and saturation so seams and stitching blend
                float w = (_HueWidth >= 0.5 ? 1 : saturate((_HueWidth - dh) / (_HueWidth * 0.35)))
                        * saturate((hsv.y - _MinSat) / 0.12 + (_MinSat <= 0 ? 1 : 0)) * saturate((_MaxSat - hsv.y) / 0.06)
                        * saturate((hsv.z - _MinVal) / 0.03) * saturate((_MaxVal - hsv.z) / 0.03);
                if (_UseRects > 0.5)
                    w *= saturate(Inside(i.uv, _RectA) + Inside(i.uv, _RectB) * saturate((_MaxValB - hsv.z) / 0.012));
                float3 to = hsv2rgb(float3(_ToHue, saturate(hsv.y * _SatScale + _SatAdd), saturate(hsv.z * _ValScale + _ValAdd)));
                if (_BandHalf > 0)
                {
                    float4 d = abs(i.uv.yyyy - _BandV);
                    float band = saturate((_BandHalf - min(min(d.x, d.y), min(d.z, d.w))) / 0.0015);
                    to = lerp(to, float3(0.55, 0.56, 0.56) * (0.85 + 2.0 * hsv.z), band); // the tape's silver, the cloth's folds kept
                }
                col.rgb = lerp(col.rgb, to, w);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
