
// Glass damage and rain composite for the camera feed.
// Driven by Graphics.Blit(sceneCopy, feedRT, mat):
//   _MainTex     copy of the camera feed
//   _CrackMask   CrackBake output, R = fracture line, G = ridge height, B = crush
//   _CrackFace   CrackBake face target, presence, presence x tilt, presence x screen normal
//   _CrackRough  CrackBake rough target, presence x roughness
//   _FaceRight/_FaceUp/_FaceForward/_FaceTan  feed camera basis and tan of half fov in x and y
//   _RainHeight  RainPrep output, r = drop height, black when dry
//   _RoomLum/_RoomColor  smoothed feed ambient from the C# probe

Shader "Operator/CrackApply"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
        _CrackMask ("Crack Mask", 2D) = "black" {}
        _CrackFace ("Crack Face", 2D) = "black" {}
        _CrackRough ("Crack Rough", 2D) = "black" {}
        _RainHeight ("Rain Height", 2D) = "black" {}
        _ShaderRev ("Shader Revision", Float) = 4
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CrackMask;
            sampler2D _CrackFace;
            sampler2D _CrackRough;
            float4 _FaceRight;
            float4 _FaceUp;
            float4 _FaceForward;
            float4 _FaceTan;
            float4 _MaskTexel;
            sampler2D _RainHeight;
            sampler2D _RainDrop;
            float4 _RainTexel;
            float2 _RainRadiusScale;
            float _RainRefractGain;
            float _RainSinContact;
            float _RainMipMax;
            float _RainRimDark;
            float _RainFresnel;

            float _Intensity;
            float _Warp;
            float _CA;
            float _Darken;
            float _Glow;
            float _SeedSpark;
            float _SpecLo;
            float _SpecHi;
            float _Milk;
            float _RoomLum;
            float3 _RoomColor;
            float _Aspect;
            float4 _LampDir[4];
            float4 _LampColor[4];
            float _LampCount;
            float _RainAmount;
            float _RainSpecular;

            #define RAIN_CENTRE_SCALE 0.10
            #define RAIN_ETA2         1.7769
            #define RAIN_ETA          1.333
            #define RAIN_TINT         float3(0.90, 0.92, 0.96)
            #define RAIN_TINT_AMT     0.40
            #define RAIN_SPEC_FLOOR   0.20
            #define RAIN_SPEC_ROOMK   1.6
            #define RAIN_SPEC_BAND    0.35
            #define RAIN_VIGNETTE     0.10

            #define GLASS_INDEX      1.52
            #define FACE_EXIT_NORMAL 0.957420
            #define CRACK_FACE_TRAP  0.5

            #define CRACK_CORE_FLOOR 0.02
            #define CRACK_CORE_ROOMK 0.16
            #define CRACK_VEIN_SHIFT 0.008
            #define CRACK_VEIN_MIX   0.70
            #define CRACK_BEVEL_DIR  float2(-0.5524, 0.8336)
            #define CRACK_BEVEL_K    0.30

            #define LAMP_LOBE         0.09
            #define LAMP_PICK_FLOOR   0.25
            #define LAMP_PICK_TIGHT   6.0
            #define LAMP_FLASH        0.05
            #define LAMP_FLASH_ROUGH  6.0
            #define LAMP_FACE_BASE    0.08
            #define LAMP_FACE_TIR     0.60

            float H21(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float VN(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = H21(i), b = H21(i + float2(1, 0)), c = H21(i + float2(0, 1)), d = H21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float3 Scene(float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).rgb; }
            float3 SceneLod(float2 uv, float lod) { return tex2Dlod(_MainTex, float4(uv, 0.0, lod)).rgb; }

            float3 LightAlong(float2 uv, float2 dir, float reach)
            {
                float3 acc = Scene(uv);
                [unroll]
                for (int i = 1; i <= 5; i++)
                {
                    float t = (float)i * reach * 0.2;
                    acc = max(acc, Scene(uv + dir * t));
                    acc = max(acc, Scene(uv - dir * t));
                }
                return acc;
            }

            float3 GlassRay(float2 uv, float3 right, float3 up, float3 forward, float2 tanXY)
            {
                float2 ndc = uv * 2.0 - 1.0;
                return normalize(forward + ndc.x * tanXY.x * right + ndc.y * tanXY.y * up);
            }

            float FaceExit(float beta)
            {
                float si = sin(2.0 * beta);
                float st = GLASS_INDEX * si;
                if (st >= 1.0) return 0.0;
                float ci = cos(2.0 * beta);
                float ct = sqrt(1.0 - st * st);
                float rs = (GLASS_INDEX * ci - ct) / (GLASS_INDEX * ci + ct);
                float rp = (GLASS_INDEX * ct - ci) / (GLASS_INDEX * ct + ci);
                return 1.0 - 0.5 * (rs * rs + rp * rp);
            }

            float3 LocalRay(float2 uv, float2 tanXY)
            {
                float2 ndc = uv * 2.0 - 1.0;
                return normalize(float3(ndc * tanXY, 1.0));
            }

            float3 LampGlint(float2 uv, float2 tangentUv, float2 tanXY)
            {
                float3 ray = LocalRay(uv, tanXY);
                float2 plane = (uv * 2.0 - 1.0) * tanXY;
                float2 tq = tangentUv * tanXY;
                float tl = length(tq);
                float3 sum = 0.0;
                int count = (int)_LampCount;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i < count)
                    {
                        float3 ld = _LampDir[i].xyz;
                        float w = LAMP_LOBE + 2.0 * _LampDir[i].w;
                        float lobe = exp(-(1.0 - dot(ray, ld)) / (w * w));
                        float pick = 1.0;
                        if (tl > 1e-5 && ld.z > 0.05)
                        {
                            float2 toward = ld.xy / ld.z - plane;
                            float tw = length(toward);
                            if (tw > 1e-5)
                            {
                                float a = dot(tq / tl, toward / tw);
                                pick = LAMP_PICK_FLOOR + (1.0 - LAMP_PICK_FLOOR) * exp(-(1.0 - a * a) * LAMP_PICK_TIGHT);
                            }
                        }
                        sum += _LampColor[i].rgb * (lobe * pick);
                    }
                }
                return sum;
            }

            float3 LampFlash(float3 mirrorDir, float rough)
            {
                float3 sum = 0.0;
                int count = (int)_LampCount;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i < count)
                    {
                        float w = (LAMP_FLASH + 2.0 * _LampDir[i].w) * (1.0 + LAMP_FLASH_ROUGH * rough);
                        sum += _LampColor[i].rgb * exp(-(1.0 - dot(mirrorDir, _LampDir[i].xyz)) / (w * w));
                    }
                }
                return sum;
            }

            float RainH(float2 uv) { return tex2Dlod(_RainHeight, float4(uv, 0.0, 0.0)).r; }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 e = (uv - 0.5) * 2.0;
                float fres = 0.30 + 0.70 * saturate(dot(e, e));

                float rainDrop = 0.0, rainOn = 0.0, rainTir = 0.0;
                float2 rainQ = 0.0;
                float3 col;

                if (_RainAmount > 0.001)
                {
                    rainOn = smoothstep(0.0, 0.10, _RainAmount);
                    rainDrop = RainH(uv);

                    float4 da = tex2Dlod(_RainDrop, float4(uv, 0.0, 0.0));
                    float cov = da.r;
                    if (cov > 0.004)
                    {
                        float2 toC = (da.gb * 2.0 - 1.0) * RAIN_CENTRE_SCALE;
                        float rUv = max(da.a * 0.125, 1e-5);
                        float2 rVec = rUv * _RainRadiusScale;
                        float2 q = -toC / max(rVec, 1e-6);
                        float ql = length(q);
                        q = ql > 1.0 ? q / ql : q;
                        rainQ = q;

                        float2 nxy = _RainSinContact * q;
                        float s2 = min(dot(nxy, nxy), 0.999);
                        float nz = sqrt(saturate(1.0 - s2));

                        float k = 1.0 - RAIN_ETA2 * s2;
                        rainTir = smoothstep(0.0, 0.08, -k);
                        float av = sqrt(max(k, 0.0)) - RAIN_ETA * nz;
                        float2 dir = (av * nxy) / max(RAIN_ETA + av * nz, 1e-3);

                        float2 centreUv = uv + toC;
                        float2 uvR = centreUv + dir * _RainRefractGain;

                        float lod = clamp(log2(_RainRefractGain / rUv), 0.0, _RainMipMax);

                        float3 through = SceneLod(clamp(uvR, 0.001, 0.999), lod);
                        float dropMix = saturate(cov * 1.6) * rainOn;
                        col = lerp(Scene(uv), through, dropMix);
                        col *= 1.0 - rainTir * _RainRimDark * dropMix;

                        float fresDrop = _RainFresnel * (0.02 + 0.98 * pow(saturate(1.0 - nz), 5.0));
                        col += fresDrop * dropMix * Lum(SceneLod(uv, min(2.0, _RainMipMax)));
                    }
                    else
                    {
                        col = Scene(uv);
                    }
                }
                else
                {
                    col = Scene(uv);
                }

                float2 baseUv = uv;

                if (_Intensity > 0.001)
                {
                    float3 mask = tex2Dlod(_CrackMask, float4(uv, 0.0, 0.0)).rgb;
                    float lineC = mask.r * _Intensity;
                    float crush = mask.b * _Intensity;

                    float2 gstep = max(float2(1.0 / 960.0, 1.0 / 540.0), _MaskTexel.xy);
                    float hxp = tex2Dlod(_CrackMask, float4(uv + float2(gstep.x, 0.0), 0.0, 0.0)).g;
                    float hxm = tex2Dlod(_CrackMask, float4(uv - float2(gstep.x, 0.0), 0.0, 0.0)).g;
                    float hyp = tex2Dlod(_CrackMask, float4(uv + float2(0.0, gstep.y), 0.0, 0.0)).g;
                    float hym = tex2Dlod(_CrackMask, float4(uv - float2(0.0, gstep.y), 0.0, 0.0)).g;
                    float2 n = float2(hxp - hxm, hyp - hym) * 7.0;

                    float2 disp = n * _Warp * 0.01 * _Intensity;
                    float2 dR = disp * (1.0 + _CA);
                    float2 dB = disp * (1.0 - _CA);
                    col.r = Scene(baseUv + dR).r;
                    col.g = Scene(baseUv + disp).g;
                    col.b = Scene(baseUv + dB).b;

                    float2 spuv = float2(uv.x * _Aspect, uv.y);

                    float roomLit = saturate(_RoomLum);
                    float3 roomTint = lerp(1.0.xxx, _RoomColor / max(_RoomLum, 1e-3), 0.30);

                    if (lineC > 0.002)
                    {
                        float lineSharp = pow(saturate(mask.r), 0.72) * _Intensity;

                        float2 gdir = (dot(n, n) > 1e-6) ? normalize(n) : float2(0.0, 1.0);
                        float sparkle = 0.55 + 0.45 * VN(spuv * 55.0 + _SeedSpark);
                        float2 tangent = (dot(n, n) > 1e-6) ? float2(-gdir.y, gdir.x) : float2(0.0, 0.0);
                        float3 glint = 1.0 - exp(-LampGlint(uv, tangent, _FaceTan.xy) * (0.8 + 0.2 * sparkle));
                        float gleam = max(glint.r, max(glint.g, glint.b)) * lineSharp
                                    * (0.55 + 0.45 * fres);

                        float3 vein = Scene(baseUv + gdir * CRACK_VEIN_SHIFT);
                        col = lerp(col, vein, lineSharp * CRACK_VEIN_MIX);

                        col *= 1.0 - lineSharp * _Darken * (1.0 - gleam);
                        col += glint * (lineSharp * _Glow * (0.55 + 0.45 * fres));

                        float core = smoothstep(0.30, 0.75, mask.r) * _Intensity;
                        float coreAmp = CRACK_CORE_FLOOR + CRACK_CORE_ROOMK * roomLit;
                        col += core * coreAmp * sparkle * roomTint;

                        float bev = clamp(dot(n, CRACK_BEVEL_DIR), -1.0, 1.0);
                        col *= 1.0 + bev * (0.5 * CRACK_BEVEL_K) * lineSharp;
                    }

                    float4 face = tex2Dlod(_CrackFace, float4(uv, 0.0, 0.0));
                    float q = face.x;
                    if (q > 0.002)
                    {
                        float beta = face.y / q;
                        float2 band = face.zw / q;
                        float2 s = dot(band, band) > 1e-12 ? normalize(band) : float2(1.0, 0.0);
                        float exitT = FaceExit(beta);
                        float3 roomCol = roomLit * roomTint;
                        float3 shown = roomCol;
                        float3 ray = GlassRay(uv, _FaceRight.xyz, _FaceUp.xyz, _FaceForward.xyz, _FaceTan.xy);
                        if (exitT > 0.0)
                        {
                            float3 toward = s.x * _FaceRight.xyz + s.y * _FaceUp.xyz;
                            toward = normalize(toward - dot(toward, ray) * ray);
                            float phi = asin(saturate(GLASS_INDEX * sin(2.0 * beta)));
                            float3 m = cos(phi) * ray + sin(phi) * toward;
                            float ahead = dot(m, _FaceForward.xyz);
                            if (ahead > 1e-4)
                            {
                                float2 fetch = 0.5 + 0.5 * float2(dot(m, _FaceRight.xyz), dot(m, _FaceUp.xyz)) / (ahead * _FaceTan.xy);
                                float2 inset = min(fetch, 1.0 - fetch);
                                float onPic = smoothstep(0.0, 0.02, min(inset.x, inset.y));
                                shown = lerp(roomCol, Scene(saturate(fetch)), onPic);
                            }
                        }
                        float3 mirror = exitT / FACE_EXIT_NORMAL * shown
                                      + (1.0 - exitT) * (1.0 - exitT) * CRACK_FACE_TRAP * roomCol;
                        col = lerp(col, mirror, saturate(q * _Intensity));

                        if (_LampCount > 0.5)
                        {
                            float3 lr = LocalRay(uv, _FaceTan.xy);
                            float3 tw = float3(s, 0.0);
                            tw = normalize(tw - dot(tw, lr) * lr);
                            float b2 = 2.0 * beta;
                            float3 mirrorDir = -cos(b2) * lr + sin(b2) * tw;
                            float rough = tex2Dlod(_CrackRough, float4(uv, 0.0, 0.0)).r / q;
                            float refl = LAMP_FACE_BASE + LAMP_FACE_TIR * (1.0 - exitT) * (1.0 - exitT);
                            float3 flash = 1.0 - exp(-LampFlash(mirrorDir, saturate(rough)));
                            col += flash * (refl * saturate(q * _Intensity) * _Glow);
                        }
                    }
                    col *= 1.0 - 0.35 * saturate(q * (1.0 - q) * 4.0) * _Intensity;

                    if (crush > 0.002)
                    {
                        float3 lightC = LightAlong(uv, float2(0.0, 1.0), 0.03);
                        float3 milk = saturate(lightC) * 0.55 + 0.35 * smoothstep(0.02, 0.30, roomLit);
                        col = lerp(col, milk, saturate(crush) * _Milk);
                    }
                }

                if (rainOn > 0.001)
                {
                    float tintMask = pow(saturate(rainDrop), 1.5) * RAIN_TINT_AMT * rainOn;
                    tintMask *= smoothstep(0.02, 0.10, Lum(col));
                    col = lerp(col, col * RAIN_TINT, tintMask);

                    float nMag = length(rainQ);
                    float upEdge = saturate(rainQ.y * 1.6) * smoothstep(0.30, 0.85, nMag) * rainOn;
                    if (_RainSpecular > 0.001 && upEdge > 0.01)
                    {
                        float2 gdir = nMag > 1e-5 ? rainQ / nMag : float2(0.0, 1.0);
                        float3 lp = LightAlong(uv, gdir, 0.04);
                        float lumR = Lum(lp);
                        float th = max(RAIN_SPEC_FLOOR, _RoomLum * RAIN_SPEC_ROOMK);
                        float g = smoothstep(th, th + RAIN_SPEC_BAND, lumR) * upEdge * _RainSpecular;
                        col += g * (0.4 + 0.6 * saturate(lp / max(lumR, 1e-3))) * rainOn;
                    }

                    float vig = smoothstep(0.55, 1.30, dot(e, e)) * RAIN_VIGNETTE * rainOn;
                    col *= 1.0 - vig;
                }

                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
