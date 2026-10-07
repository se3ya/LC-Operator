Shader "Operator/RainPrep"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    float _InBlack;
    float _InWhite;
    float _OutWhite;
    float _OutBlack;

    static const float WEIGHTS[8] = { 0.013, 0.067, 0.194, 0.226, 0.226, 0.194, 0.067, 0.013 };
    static const float OFFSETS[8] = { -6.264, -4.329, -2.403, -0.649, 0.649, 2.403, 4.329, 6.264 };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f Vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord.xy;
        return o;
    }

    float4 FragBlurH(v2f i) : SV_Target
    {
        float c = 0.0;
        [unroll]
        for (int k = 0; k < 8; k++)
        {
            c += tex2D(_MainTex, i.uv + float2(OFFSETS[k], 0.0) * _MainTex_TexelSize.xy).r * WEIGHTS[k];
        }
        return float4(c, c, c, 1.0);
    }

    float4 FragBlurV(v2f i) : SV_Target
    {
        float c = 0.0;
        [unroll]
        for (int k = 0; k < 8; k++)
        {
            c += tex2D(_MainTex, i.uv + float2(0.0, OFFSETS[k]) * _MainTex_TexelSize.xy).r * WEIGHTS[k];
        }
        return float4(c, c, c, 1.0);
    }

    float4 FragLevel(v2f i) : SV_Target
    {
        float h = tex2D(_MainTex, i.uv).r;
        h = (((h * 255.0) - _InBlack) / (_InWhite - _InBlack) * (_OutWhite - _OutBlack) + _OutBlack) / 255.0;
        h = saturate(h);
        return float4(h, h, h, 1.0);
    }
    ENDCG

    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLevel
            ENDCG
        }

        Pass
        {
            Blend One One
            BlendOp Max
            ColorMask R

            CGPROGRAM
            #pragma vertex VertStamp
            #pragma fragment FragStamp
            #pragma target 4.5

            StructuredBuffer<float4> _RainDrops;
            float4 _RainCanvasSize;

            #define STAMP_PAD 1.30

            struct sv2f
            {
                float4 pos : SV_POSITION;
                float2 local : TEXCOORD0;
                nointerpolation float2 sb : TEXCOORD1;
            };

            sv2f VertStamp(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                float4 A = _RainDrops[iid * 2 + 0];
                float4 B = _RainDrops[iid * 2 + 1];

                float2 corner = float2((vid == 1 || vid == 2 || vid == 4) ? 1.0 : -1.0,
                                       (vid == 2 || vid == 4 || vid == 5) ? 1.0 : -1.0);

                float2 off = corner * float2(A.z, A.w) * STAMP_PAD;
                float2 world = A.xy + float2(off.x * B.x - off.y * B.y,
                                             off.x * B.y + off.y * B.x);
                float2 uvPos = world * _RainCanvasSize.zw;

                sv2f o;
                o.pos = float4(uvPos * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.local = corner * STAMP_PAD;
                o.sb = float2(B.z, B.w);
                return o;
            }

            float4 FragStamp(sv2f i) : SV_Target
            {
                float seed = i.sb.x;
                float3 h1 = frac(sin(seed * float3(12.9898, 78.233, 45.164)) * 43758.5453);
                float3 h2 = frac(sin(seed * float3(93.9898, 67.345, 23.719)) * 24634.6345);

                float2 p = i.local;
                float sag = lerp(0.06, 0.22, h2.y);
                p.y *= lerp(1.0, 1.0 / (1.0 + sag), smoothstep(0.4, -0.8, p.y));

                float a2 = lerp(0.05, 0.15, h1.x);
                float a3 = lerp(0.03, 0.11, h1.y);
                float phi = atan2(p.y, p.x);
                float rmod = 1.0 + a2 * sin(2.0 * phi + h1.z * 6.2832)
                                 + a3 * sin(3.0 * phi + h2.x * 6.2832);

                float nd = length(p) / max(rmod, 0.4);
                float v = saturate(1.0 - nd * nd);
                v = pow(v, 0.85) * i.sb.y;
                return float4(v, 0.0, 0.0, 1.0);
            }
            ENDCG
        }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma vertex VertAttr
            #pragma fragment FragAttr
            #pragma target 4.5

            StructuredBuffer<float4> _RainDrops;
            float4 _RainCanvasSize;

            #define STAMP_PAD 1.30
            #define CENTRE_SCALE 0.10

            struct av2f
            {
                float4 pos : SV_POSITION;
                float2 local : TEXCOORD0;
                float2 quv : TEXCOORD1;
                nointerpolation float2 sb : TEXCOORD2;
                nointerpolation float2 centre : TEXCOORD3;
                nointerpolation float rad : TEXCOORD4;
            };

            av2f VertAttr(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                float4 A = _RainDrops[iid * 2 + 0];
                float4 B = _RainDrops[iid * 2 + 1];

                float2 corner = float2((vid == 1 || vid == 2 || vid == 4) ? 1.0 : -1.0,
                                       (vid == 2 || vid == 4 || vid == 5) ? 1.0 : -1.0);

                float2 off = corner * float2(A.z, A.w) * STAMP_PAD;
                float2 world = A.xy + float2(off.x * B.x - off.y * B.y,
                                             off.x * B.y + off.y * B.x);
                float2 uvPos = world * _RainCanvasSize.zw;

                av2f o;
                o.pos = float4(uvPos * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.quv = uvPos;
                o.local = corner * STAMP_PAD;
                o.sb = float2(B.z, B.w);
                o.centre = A.xy * _RainCanvasSize.zw;
                o.rad = (A.z + A.w) * 0.5 * _RainCanvasSize.z;
                return o;
            }

            void FragAttr(av2f i, out float4 col : SV_Target, out float dep : SV_Depth)
            {
                float seed = i.sb.x;
                float3 h1 = frac(sin(seed * float3(12.9898, 78.233, 45.164)) * 43758.5453);
                float3 h2 = frac(sin(seed * float3(93.9898, 67.345, 23.719)) * 24634.6345);

                float2 p = i.local;
                float sag = lerp(0.06, 0.22, h2.y);
                p.y *= lerp(1.0, 1.0 / (1.0 + sag), smoothstep(0.4, -0.8, p.y));

                float a2 = lerp(0.05, 0.15, h1.x);
                float a3 = lerp(0.03, 0.11, h1.y);
                float phi = atan2(p.y, p.x);
                float rmod = 1.0 + a2 * sin(2.0 * phi + h1.z * 6.2832)
                                 + a3 * sin(3.0 * phi + h2.x * 6.2832);

                float nd = length(p) / max(rmod, 0.4);
                float v = pow(saturate(1.0 - nd * nd), 0.85) * i.sb.y;
                clip(v - 0.004);

                float2 toC = i.centre - i.quv;
                col = float4(v,
                             saturate(toC.x / CENTRE_SCALE * 0.5 + 0.5),
                             saturate(toC.y / CENTRE_SCALE * 0.5 + 0.5),
                             saturate(i.rad * 8.0));
                dep = 1.0 - saturate(v);
            }
            ENDCG
        }
    }
    Fallback Off
}
