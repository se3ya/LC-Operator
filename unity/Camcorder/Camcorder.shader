// Driven by Graphics.Blit(sceneCopy, feedRT, mat):
//   _MainTex     copy of the camera feed (_MainTex_TexelSize.zw = feed width,height)
//   _Aspect      feed width/height (keeps the vignette/CA field round)
//   _CamTime     Time.time (animates grain)
//   _Vignette    corner darkening strength
//   _Barrel      barrel distortion k
//   _CA          chromatic aberration amount
//   _Grain       sensor grain amount
//   _Scan        scanline darkening amount
//   _Dust        lens dust opacity
//   _DustRef     the dust opacity a slider of one would give, so darkening tracks the lift
//   _WB          per channel white balance multiply
//   _ColdDesat   cold/desaturated panic shift
//   _Glitch      tracking glitch burst intensity
//   _GlitchSeed  per burst random seed

Shader "Operator/Camcorder"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
        _Aspect ("Aspect (w/h)", Float) = 1.777
        _CamTime ("Time", Float) = 0
        _Vignette ("Vignette", Float) = 0.30
        _Barrel ("Barrel", Float) = 0.028
        _CA ("Chromatic Aberration", Float) = 0.007
        _Grain ("Grain", Float) = 0.016
        _Scan ("Scanlines", Float) = 0.05
        _Dust ("Dust", Float) = 0.06
        _DustRef ("Dust Reference", Float) = 0.06
        _WB ("White Balance", Vector) = (1,1,1,0)
        _ColdDesat ("Cold Desat", Float) = 0
        _Glitch ("Glitch", Float) = 0
        _GlitchSeed ("Glitch Seed", Float) = 0
        _Breath ("Focus Breathing (uniform zoom)", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Aspect, _CamTime, _Vignette, _Barrel, _CA, _Grain, _Scan, _Dust, _DustRef, _ColdDesat, _Glitch, _GlitchSeed, _Breath;
            float3 _WB;

            static const float C_vigInner   = 0.55;
            static const float C_vigOuter   = 1.35;
            static const float C_dustDarken = 0.15;
            static const float C_grainScale = 240.0;

            float luma3(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float hash11(float h) { return frac(sin(h * 127.1) * 43758.5453); }
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float dustVeil(float2 uv)
            {
                float v = 0.0;
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float fi = (float)i;
                    float2 c = float2(hash11(fi * 1.7 + 0.1), hash11(fi * 2.3 + 0.6));
                    float rad = lerp(0.12, 0.30, hash11(fi * 3.1 + 0.2));
                    float amp = lerp(0.4, 1.0, hash11(fi * 4.7 + 0.9));
                    float2 d = (uv - c) * float2(_Aspect, 1.0);
                    v += amp * exp(-dot(d, d) / (2.0 * rad * rad));
                }
                float sp = hash21(floor(uv * _MainTex_TexelSize.zw * 0.5));
                v += smoothstep(0.985, 1.0, sp) * 1.2;
                return saturate(v / 2.0);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                if (_Glitch > 0.0001)
                {
                    float bandId = floor(uv.y * 18.0);
                    float sel = step(1.0 - _Glitch * 0.6, hash11(bandId * 1.7 + _GlitchSeed * 3.3));
                    float sh = (hash11(bandId * 2.9 + _GlitchSeed) * 2.0 - 1.0) * 0.09 * _Glitch * sel;
                    uv.x += sh;
                }

                float2 rr = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5) * 2.0;
                float r = length(rr);

                float factor = 1.0 - _Barrel * r * r;

                factor *= (1.0 + _Breath);
                float2 off = uv - 0.5;
                float2 uvR = 0.5 + off * (factor * (1.0 + _CA * r));
                float2 uvG = 0.5 + off * factor;
                float2 uvB = 0.5 + off * (factor * (1.0 - _CA * r));
                float3 col;
                col.r = tex2D(_MainTex, uvR).r;
                col.g = tex2D(_MainTex, uvG).g;
                col.b = tex2D(_MainTex, uvB).b;

                float veil = dustVeil(uv);
                float3 lift = _Dust * veil * (0.35 + 0.65 * luma3(col));
                col = col * (1.0 - C_dustDarken * veil * saturate(_Dust / max(1e-4, _DustRef))) + lift;

                float t = saturate((r - C_vigInner) / (C_vigOuter - C_vigInner));
                float vig = 1.0 - _Vignette * (t * t * (3.0 - 2.0 * t));
                col *= vig;

                col *= _WB;
                float l = luma3(col);
                col = col * (1.0 - 0.55 * _ColdDesat) + l * (0.55 * _ColdDesat);
                col *= float3(1.0 - 0.12 * _ColdDesat, 1.0 - 0.02 * _ColdDesat, 1.0 + 0.12 * _ColdDesat);

                float2 grainCells = float2(C_grainScale * _Aspect, C_grainScale);
                float n = hash21(uv * grainCells + frac(_CamTime) * float2(37.0, 17.0));
                col += (n - 0.5) * 2.0 * _Grain * (0.35 + 0.65 * (1.0 - luma3(col)));

                float row = floor(uv.y * _MainTex_TexelSize.w);
                float sc = (fmod(row, 2.0) < 1.0) ? 1.0 : (1.0 - _Scan);
                col *= sc;

                if (_Glitch > 0.0001)
                {
                    float tearY = frac(_GlitchSeed * 0.37);
                    float tear = _Glitch * smoothstep(0.006, 0.0, abs(uv.y - tearY));
                    float barY = frac(_GlitchSeed * 0.71 + 0.3);
                    float bar = _Glitch * 0.5 * smoothstep(0.06, 0.0, abs(uv.y - barY));
                    col = col * (1.0 - bar) + tear;
                }

                return float4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
}
