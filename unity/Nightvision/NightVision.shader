Shader "Operator/NightVision"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _HistoryTex ("Integration history", 2D) = "black" {}

        _Tint ("Channel gains", Color) = (0.70, 1.0, 0.44, 1)
        _ChannelHeadroom ("Channel headroom", Float) = 1.9

        _DarkLift ("Shadow lift (gamma)", Range(0.1,1)) = 0.78
        _ToneKnee ("Tone soft-clip knee", Float) = 0.35
        _ToneScale ("Tone rescale", Float) = 1.12

        _GrainAmount ("Shot noise", Range(0,1)) = 0.17
        _NoiseFloor ("Read noise", Range(0,0.5)) = 0.03
        _Dsnu ("Dark signal non uniformity", Range(0,1)) = 0.22
        _HotPixelRate ("Hot pixel rate", Range(0,0.01)) = 0.0006
        _Seed ("Grain seed", Float) = 0
        _GrainGain ("Grain gain", Float) = 1.0

        _HaloStrength ("IR diffusion strength", Float) = 0.35
        _HaloThreshold ("IR diffusion threshold", Range(0,1)) = 0.62
        _HaloMip ("IR diffusion radius (mip)", Float) = 2.0

        _Persist ("Frame integration", Range(0,1)) = 0.35

        _SmearThreshold ("Smear threshold", Float) = 0.97
        _SmearStrength ("Smear strength", Float) = 1.2
        _SmearLod ("Smear tap lod", Float) = 4.0

        _NVAmount ("NV master blend", Range(0,1)) = 1
        _BloomEdge ("Settle band Y", Range(0,1)) = 1
        _BloomAmount ("Settle bloom amount", Range(0,1)) = 0
        _BloomStrength ("Bloom strength", Float) = 1.0
        _BandWidth ("Band softness", Range(0,0.5)) = 0.13
        _BandDark ("Band dark line", Range(0,1)) = 0.55
        _Shake ("Micro shake", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _HistoryTex;

            fixed4 _Tint;
            float _ChannelHeadroom;
            float _DarkLift, _ToneKnee, _ToneScale;
            float _GrainAmount, _NoiseFloor, _Dsnu, _HotPixelRate, _Seed, _GrainGain;
            float _HaloStrength, _HaloThreshold, _HaloMip;
            float _Persist;
            float _SmearThreshold, _SmearStrength, _SmearLod;
            float _NVAmount, _BloomEdge, _BloomAmount, _BloomStrength, _BandWidth, _BandDark, _Shake;

            static const float3 kIr = float3(0.33333, 0.33333, 0.33333);

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash (float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 screenUv = i.uv;
                float2 uv = screenUv;
                uv.y += _Shake;

                fixed3 srcCol = tex2D(_MainTex, uv).rgb;
                float lum0 = dot(srcCol, kIr);

                float smear = 0.0;
                [unroll] for (int sm = 0; sm < 16; sm++)
                {
                    float sy = (sm + 0.5) / 16.0;
                    float3 sc = tex2Dlod(_MainTex, float4(uv.x, sy, 0.0, _SmearLod)).rgb;
                    smear += smoothstep(_SmearThreshold, 1.0, dot(sc, kIr));
                }
                lum0 = saturate(lum0 + (smear * 0.0625) * _SmearStrength);

                float lum = pow(saturate(lum0), _DarkLift);
                lum = saturate((lum / (1.0 + lum * _ToneKnee)) * _ToneScale);

                float dsnu = 1.0 + (hash(floor(screenUv * _MainTex_TexelSize.zw) * 0.317) - 0.5) * _Dsnu;
                lum *= dsnu;

                float gFine = hash(uv * _MainTex_TexelSize.zw + _Seed) - 0.5;
                lum = saturate(lum
                             + gFine * _GrainAmount * _GrainGain * sqrt(saturate(lum))
                             + gFine * _NoiseFloor * _GrainGain);

                float hp = hash(floor(screenUv * _MainTex_TexelSize.zw) * 0.731 + 4.13);
                lum = max(lum, step(1.0 - _HotPixelRate * _GrainGain, hp));

                fixed3 nv = saturate(_Tint.rgb * lum * _ChannelHeadroom);

                fixed3 blur = tex2Dlod(_MainTex, float4(uv, 0.0, _HaloMip)).rgb;
                float haloLum = max(0.0, dot(blur, kIr) - _HaloThreshold);
                nv += _Tint.rgb * haloLum * _HaloStrength;

                fixed3 img = lerp(srcCol, nv, saturate(_NVAmount));

                float aboveMask = smoothstep(_BloomEdge - _BandWidth, _BloomEdge + _BandWidth, uv.y);
                img *= (1.0 + _BloomAmount * aboveMask * _BloomStrength);
                float lineB = smoothstep(_BandWidth, 0.0, abs(uv.y - _BloomEdge)) * _BloomAmount;
                img *= lerp(1.0, _BandDark, lineB);

                fixed3 hist = tex2D(_HistoryTex, screenUv).rgb;
                img = lerp(img, hist, _Persist * saturate(_NVAmount));

                return fixed4(img, 1.0);
            }
            ENDCG
        }
    }
}
