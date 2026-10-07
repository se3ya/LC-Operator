// Passes:
//   0 Extract    clipped highlights only, weighted by flux not by brightness, box
//                averaged over the four by four feed block   (_Thresh,_Knee,_OverRange)
//   1 Blur       separable gaussian, direction/scale via _BlurDir
//   2 Composite  feed plus halo, a flat frame wide veil, and ccd smear

Shader "Operator/VeilGlare"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
        _GlowTex ("Glow Near", 2D) = "black" {}
        _GlowFar ("Glow Far", 2D) = "black" {}
        _VeilTex ("Veil", 2D) = "black" {}
        _SmearTex ("Smear", 2D) = "black" {}
        _Thresh ("Highlight Threshold", Float) = 0.78
        _Knee ("Highlight Knee", Float) = 0.14
        _OverRange ("Over Range", Float) = 12.0
        _BlurDir ("Blur Dir (texels)", Vector) = (1,0,0,0)
        _Halo ("Halo Amount", Float) = 0.06
        _Veil ("Veiling Amount", Float) = 0.01
        _Smear ("Smear Amount", Float) = 0.07
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _MainTex_TexelSize;
        sampler2D _GlowTex;
        sampler2D _GlowFar;
        sampler2D _VeilTex;
        sampler2D _SmearTex;
        float _Thresh, _Knee, _OverRange, _Halo, _Veil, _Smear;
        float4 _BlurDir;
        float luma3(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                float3 sum = 0.0;
                [unroll] for (int y = 0; y < 4; y++)
                {
                    [unroll] for (int x = 0; x < 4; x++)
                    {
                        float2 o = (float2(x, y) - 1.5) * t;
                        float3 c = tex2Dlod(_MainTex, float4(i.uv + o, 0.0, 0.0)).rgb;
                        float3 lin = c * c;
                        float hl = saturate((luma3(lin) - _Thresh) / max(1e-4, _Knee));
                        float flux = hl * hl * hl * (1.0 + _OverRange * hl);
                        sum += lin * flux;
                    }
                }
                return float4(sum * 0.0625, 1.0);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f_img i) : SV_Target
            {
                float2 step = _BlurDir.xy * _MainTex_TexelSize.xy;
                float3 s = tex2D(_MainTex, i.uv).rgb * 0.227027;
                float w1 = 0.194594, w2 = 0.121622, w3 = 0.054054, w4 = 0.016216;
                s += (tex2D(_MainTex, i.uv + step * 1.0).rgb + tex2D(_MainTex, i.uv - step * 1.0).rgb) * w1;
                s += (tex2D(_MainTex, i.uv + step * 2.0).rgb + tex2D(_MainTex, i.uv - step * 2.0).rgb) * w2;
                s += (tex2D(_MainTex, i.uv + step * 3.0).rgb + tex2D(_MainTex, i.uv - step * 3.0).rgb) * w3;
                s += (tex2D(_MainTex, i.uv + step * 4.0).rgb + tex2D(_MainTex, i.uv - step * 4.0).rgb) * w4;
                return float4(s, 1.0);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f_img i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb;
                float3 lin = scene * scene;

                float3 glow = tex2D(_GlowTex, i.uv).rgb * 0.55 + tex2D(_GlowFar, i.uv).rgb * 0.45;

                float3 v = tex2D(_VeilTex, float2(0.25, 0.25)).rgb
                         + tex2D(_VeilTex, float2(0.75, 0.25)).rgb
                         + tex2D(_VeilTex, float2(0.25, 0.75)).rgb
                         + tex2D(_VeilTex, float2(0.75, 0.75)).rgb;
                float veil = luma3(v) * 0.25;

                float smear = luma3(tex2D(_SmearTex, i.uv).rgb);

                float3 outLin = lin + _Halo * glow + _Veil * veil + _Smear * smear;
                return float4(saturate(sqrt(max(outLin, 0.0))), 1.0);
            }
            ENDCG
        }
    }
}
