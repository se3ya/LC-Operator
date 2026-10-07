
// Bakes the fracture pattern into a mask RT, once per stage or lens change.
//   R = crack line intensity
//   G = ridge height, the apply pass differentiates this for refraction
//   B = crush zone, a few pixels wide at the impact point

Shader "Operator/CrackBake"
{
    Properties
    {
        _Stage ("Stage", Range(0,3)) = 0
        _Seed ("Seed", Float) = 11
        _Aspect ("Aspect", Float) = 1.7777
        _FlipY ("Flip Y", Float) = 0
        _CrushRadius ("Crush Radius", Float) = 0.012
        _CrushStrength ("Crush Strength", Float) = 0.85
        _StarSites ("Star Sites Bitmask", Float) = 0
        _FaceDepth ("Face Depth", Float) = 1
        _ShaderRev ("Shader Revision", Float) = 4
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"

        float _Stage;
        float _Seed;
        float _Aspect;
        float _FlipY;
        float _CrushRadius;
        float _CrushStrength;
        float _StarSites;
        float _FaceDepth;

            #define TAU 6.28318530718
            #define PI  3.14159265359
            #define SS(a,b,t) smoothstep(a,b,t)

            float H21(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float H11(float x) { return frac(sin(x * 78.233) * 43758.5453); }
            float VN(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = H21(i), b = H21(i + float2(1, 0)), c = H21(i + float2(0, 1)), d = H21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float FBM(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 4; k++) { v += a * VN(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            #define EC_TOP   9
            #define EC_BOT   8
            #define EC_SPINE 5
            #define EC_MID   4
            #define EC_TOTAL 26

            static const float REGION_W = 0.42;

            static const float E_WARP  = 0.010;
            static const float E_SERR  = 0.006;
            static const float E_WBASE = 0.0024;
            static const float E_WMIN  = 0.0013;
            static const float E_WLO   = 0.55;
            static const float E_WHI   = 2.80;
            static const float E_WPOW  = 1.90;
            static const float E_JBW   = 2.60;
            static const float E_NODE  = 0.62;
            static const float E_BARB  = 1.30;

            static const float PIT_X = 0.02 / 0.42;
            static const float PIT_Y = 0.12;

            float2 EdgeSite(int idx, float sd)
            {
                if (idx < EC_TOP)
                {
                    float a = H11(sd + (float)idx * 3.13 + 1.1);
                    float r = H11(sd + (float)idx * 5.7  + 2.2);
                    return float2(0.015 + 0.12 * pow(r, 1.5), 0.66 + 0.24 * a);
                }
                else if (idx < EC_TOP + EC_BOT)
                {
                    int i = idx - EC_TOP;
                    float a = H11(sd + (float)i * 4.9 + 7.7);
                    float r = H11(sd + (float)i * 6.3 + 3.3);
                    float y = 0.05 + 0.15 * a * pow(H11(sd + (float)i * 3.1 + 1.9), 0.8);
                    return float2(0.015 + 0.10 * pow(r, 1.5), y);
                }
                else if (idx < EC_TOP + EC_BOT + EC_SPINE)
                {
                    int i = idx - EC_TOP - EC_BOT;
                    float f = ((float)i + 0.5) / (float)EC_SPINE;
                    return float2(0.01 + 0.09 * H11(sd + (float)i * 8.1 + 5.0),
                                  0.12 + 0.74 * f + 0.05 * (H11(sd + (float)i * 2.3) - 0.5));
                }
                int i2 = idx - EC_TOP - EC_BOT - EC_SPINE;
                return float2(0.16 + 0.22 * H11(sd + (float)i2 * 9.7 + 4.4),
                              0.10 + 0.80 * H11(sd + (float)i2 * 5.5 + 6.6));
            }

            int EdgeMinStage(int idx)
            {
                if (idx < EC_TOP) return 1;
                if (idx < EC_TOP + EC_BOT + EC_SPINE) return 2;
                return 3;
            }

            struct CrackCell
            {
                float2 Pw;
                float f1;
                float f31;
                int i1;
                int i2;
                float2 normal;
                float distw;
                float key;
                float jb;
                float inten;
                float dn;
                float env;
                float wfac;
                float breakup;
            };

            bool FindCell(float2 P, int stage, out CrackCell c)
            {
                c = (CrackCell)0;
                float sd = _Seed;
                if (P.x > 1.25 || P.x < -0.1) return false;

                float wx = FBM(P * 3.0 + float2(sd * 0.13, 5.2)) - 0.5;
                float wy = FBM(P * 3.0 + float2(19.7, sd * 0.19)) - 0.5;
                float2 j1 = float2(VN(P * 26.0 + float2(sd * 0.37, 12.9)),
                                   VN(P * 26.0 + float2(31.3, sd * 0.41))) - 0.5;
                float2 j2 = float2(VN(P * 60.0 + float2(sd * 0.70, 4.3)),
                                   VN(P * 60.0 + float2(7.9, sd * 0.83))) - 0.5;
                float2 Pw = P + float2(wx, wy) * E_WARP + (j1 + 0.5 * j2) * E_SERR;

                float f1 = 1e9, f2 = 1e9, f3 = 1e9;
                int i1 = 0, i2 = 1;
                float2 s1 = Pw, s2 = Pw;
                [loop] for (int i = 0; i < EC_TOTAL; i++)
                {
                    if (EdgeMinStage(i) > stage) continue;
                    float2 si = EdgeSite(i, sd);
                    float d = distance(Pw, si);
                    if (d < f1)      { f3 = f2; f2 = f1; f1 = d; i2 = i1; i1 = i; s2 = s1; s1 = si; }
                    else if (d < f2) { f3 = f2; f2 = d; i2 = i; s2 = si; }
                    else if (d < f3) { f3 = d; }
                }

                float2 gvec = (Pw - s2) / max(f2, 1e-4) - (Pw - s1) / max(f1, 1e-4);
                float glen = length(gvec);
                float key = min((float)i1, (float)i2) * 37.0 + max((float)i1, (float)i2);

                float bfac = 0.12 + 0.95 * H11(key + sd * 2.63);
                float along = 0.20 + 0.90 * FBM(Pw * 13.0 + float2(sd * 1.1, sd * 0.4));
                float inten = saturate(bfac * along);
                float dn = min(distance(P, float2(0.03, 0.84)), distance(P, float2(0.03, 0.12)));
                inten = saturate(inten * (1.0 + 0.50 * (1.0 - SS(0.0, 0.28, dn))));

                float outer = lerp(0.34, 0.50, saturate((stage - 1) / 2.0));
                float hx = 1.0 - SS(outer, outer + 0.12, P.x);
                float vy = 0.30 + 0.70 * ((1.0 - SS(0.0, 0.30, abs(P.y - 0.86)))
                                        + 1.15 * (1.0 - SS(0.0, 0.34, abs(P.y - 0.10))));

                c.Pw = Pw;
                c.f1 = f1;
                c.f31 = f3 - f1;
                c.i1 = i1;
                c.i2 = i2;
                c.normal = gvec / max(glen, 1e-6);
                c.distw = (f2 - f1) / max(glen, 1e-4);
                c.key = key;
                c.jb = 1.0 - SS(0.0, 0.020, f3 - f1);
                c.inten = inten;
                c.dn = dn;
                c.env = hx * saturate(vy);
                c.wfac = E_WLO + (E_WHI - E_WLO) * pow(H11(key + sd * 1.31), E_WPOW);
                c.breakup = 0.20 + 1.15 * VN(Pw * 30.0 + float2(sd, sd * 0.7));
                return true;
            }

            void LineLook(CrackCell c, float wboost, out float lineO, out float ridgeO)
            {
                float sd = _Seed;
                float w = max(E_WMIN, E_WBASE * c.wfac * c.breakup * (1.0 + E_JBW * c.jb)) * wboost;

                float relief = w * E_BARB * (pow(VN(c.Pw * 150.0 + float2(sd * 2.3, sd * 1.1)), 3.0)
                                     + 0.5 * pow(VN(c.Pw * 340.0 + float2(sd * 0.9, sd * 3.7)), 3.0));
                float distw = max(0.0, c.distw - relief);

                float wall = 1.0 - SS(w * 0.55, w, distw);
                float halo = 1.0 - SS(0.0, w * 2.1, distw);
                float node = (1.0 - SS(0.0, 0.011, c.f31)) * (0.6 + 0.4 * c.jb);

                lineO = saturate(wall * c.inten + halo * c.inten * 0.108 + node * E_NODE) * c.env;

                float wRidge = max(w * 1.5, 0.0030);
                ridgeO = (1.0 - SS(0.0, wRidge, distw)) * (0.65 + 0.35 * c.inten) * c.env;
            }

            void ThinLook(CrackCell c, float wboost, out float lineO, out float ridgeO, out float w)
            {
                w = max(E_WMIN, E_WBASE * 0.55 * c.wfac * c.breakup * (1.0 + E_JBW * 0.5 * c.jb) * wboost);
                float wall = 1.0 - SS(w * 0.55, w, c.distw);
                float halo = 1.0 - SS(0.0, w * 2.1, c.distw);
                float node = (1.0 - SS(0.0, 0.011, c.f31)) * (0.6 + 0.4 * c.jb);
                lineO = saturate(wall * c.inten + halo * c.inten * 0.108 + node * E_NODE * 0.4) * c.env;
                float wRidge = max(w * 1.5, 0.0030);
                ridgeO = (1.0 - SS(0.0, wRidge, c.distw)) * (0.65 + 0.35 * c.inten) * c.env;
            }

            void AddPit(float2 P, inout float lineO, inout float ridgeO, inout float crush)
            {
                float R = length(float2((P.x - PIT_X) * REGION_W * _Aspect, P.y - PIT_Y));
                crush = max(crush, (1.0 - SS(_CrushRadius * 0.35, _CrushRadius, R)) * _CrushStrength);
                lineO = max(lineO, (1.0 - SS(_CrushRadius * 0.15, _CrushRadius * 0.55, R)) * 0.85);
                ridgeO = max(ridgeO, (1.0 - SS(0.0, _CrushRadius * 1.6, R)) * 0.85);
            }

            bool IsRight() { return H11(_Seed * 0.7 + 5.3) > 0.5; }

            bool IsHeavy(int stage)
            {
                int starBits = (int)(_StarSites + 0.5);
                return (starBits & ((1 << (stage + 1)) - 1)) != 0;
            }

            float2 PlateOf(float2 uv, bool right)
            {
                float fy = lerp(uv.y, 1.0 - uv.y, saturate(_FlipY));
                return float2((right ? (1.0 - uv.x) : uv.x) / REGION_W, fy);
            }

            #define FACE_BAND_FRAC 0.02208333
            #define FACE_AA 0.0009

            uint GlassMix(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float GlassHash(int2 cell, uint key)
            {
                uint inner = GlassMix((uint)cell.y * 0x85EBCA77u + GlassMix(key));
                uint h = GlassMix((uint)cell.x * 0x9E3779B1u + inner);
                return (float)(h >> 8) * (1.0 / 16777216.0);
            }

            float WallHash(int lo, int hi, uint key)
            {
                return GlassHash(int2(lo, hi), key ^ asuint(_Seed));
            }

            float Twist(float along, int lo, int hi)
            {
                float cell = floor(along);
                float t = along - cell;
                float a = GlassHash(int2((int)cell, lo * 32 + hi), 3u ^ asuint(_Seed));
                float b = GlassHash(int2((int)cell + 1, lo * 32 + hi), 3u ^ asuint(_Seed));
                return lerp(a, b, t * t * (3.0 - 2.0 * t)) * 2.0 - 1.0;
            }

            struct FaceTargets
            {
                float4 mask : SV_Target0;
                float4 face : SV_Target1;
                float rough : SV_Target2;
            };

            FaceTargets FragFace(v2f_img i)
            {
                FaceTargets o;
                o.mask = float4(0, 0, 0, 1);
                o.face = 0.0;
                o.rough = 0.0;
                int stage = (int)clamp(_Stage, 0.0, 3.0);
                if (stage <= 0) return o;

                bool right = IsRight();
                float2 P = PlateOf(i.uv, right);
                bool heavy = IsHeavy(stage);
                float wboost = heavy ? 1.35 : 1.0;

                float lineO = 0.0, ridgeO = 0.0, crush = 0.0;

                CrackCell c;
                if (FindCell(P, stage, c))
                {
                    float w;
                    ThinLook(c, wboost, lineO, ridgeO, w);

                    int lo = min(c.i1, c.i2);
                    int hi = max(c.i1, c.i2);
                    int faceCell = WallHash(lo, hi, 1u) > 0.5 ? hi : lo;
                    float h = WallHash(lo, hi, 2u);
                    float along = dot(c.Pw, float2(-c.normal.y, c.normal.x)) / 0.1;
                    float beta = radians(3.6 + 25.8 * h * h + 2.5 * Twist(along, lo, hi));
                    float sigma = radians(0.5 + 2.5 * WallHash(lo, hi, 4u) + 3.0 * (1.0 - SS(0.10, 0.40, c.dn)));

                    float2 sn = float2((right ? -1.0 : 1.0) * c.normal.x / (REGION_W * _Aspect),
                                       lerp(1.0, -1.0, saturate(_FlipY)) * c.normal.y);
                    float snl = max(length(sn), 1e-6);
                    float band = FACE_BAND_FRAC * _FaceDepth * tan(beta) * snl;
                    float presence = SS(w * 0.6, w, c.distw) * (1.0 - SS(w + band - FACE_AA, w + band + FACE_AA, c.distw))
                                   * (c.i1 == faceCell ? 1.0 : 0.0) * c.env * SS(0.03, 0.12, c.inten) * step(1e-4, band);
                    o.face = presence * float4(1.0, beta, sn / snl);
                    o.rough = presence * sigma;
                }

                if (heavy && stage >= 2) AddPit(P, lineO, ridgeO, crush);

                o.mask = float4(saturate(lineO), saturate(ridgeO), saturate(crush), 1.0);
                return o;
            }

            float _FaceOut;

            float4 FragFaceOut(v2f_img i) : SV_Target
            {
                FaceTargets o = FragFace(i);
                if (_FaceOut > 1.5) return float4(o.rough, 0.0, 0.0, 1.0);
                if (_FaceOut > 0.5) return o.face;
                return o.mask;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int stage = (int)clamp(_Stage, 0.0, 3.0);
                if (stage <= 0) return float4(0, 0, 0, 1);

                bool right = IsRight();
                float2 P = PlateOf(i.uv, right);
                bool heavy = IsHeavy(stage);
                float wboost = heavy ? 1.35 : 1.0;

                float lineO = 0.0, ridgeO = 0.0, crush = 0.0;

                CrackCell c;
                if (FindCell(P, stage, c))
                {
                    LineLook(c, wboost, lineO, ridgeO);
                }

                if (heavy && stage >= 2) AddPit(P, lineO, ridgeO, crush);

                return float4(saturate(lineO), saturate(ridgeO), saturate(crush), 1.0);
            }
        ENDCG
        Pass
        {
            Name "Line"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 5.0
            ENDCG
        }
        Pass
        {
            Name "GlassFace"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragFaceOut
            #pragma target 5.0
            ENDCG
        }
    }
}
