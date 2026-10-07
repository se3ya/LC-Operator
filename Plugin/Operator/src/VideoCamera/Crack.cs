using System;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static readonly int StageId = Shader.PropertyToID("_Stage");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int FlipYId = Shader.PropertyToID("_FlipY");
    private static readonly int CrushRadiusId = Shader.PropertyToID("_CrushRadius");
    private static readonly int CrushStrengthId = Shader.PropertyToID("_CrushStrength");
    private static readonly int StarSitesId = Shader.PropertyToID("_StarSites");
    private static readonly int FaceDepthId = Shader.PropertyToID("_FaceDepth");
    private static readonly int FaceOutId = Shader.PropertyToID("_FaceOut");
    private static readonly int CrackMaskId = Shader.PropertyToID("_CrackMask");
    private static readonly int CrackFaceId = Shader.PropertyToID("_CrackFace");
    private static readonly int CrackRoughId = Shader.PropertyToID("_CrackRough");
    private static readonly int FaceRightId = Shader.PropertyToID("_FaceRight");
    private static readonly int FaceUpId = Shader.PropertyToID("_FaceUp");
    private static readonly int FaceForwardId = Shader.PropertyToID("_FaceForward");
    private static readonly int FaceTanId = Shader.PropertyToID("_FaceTan");
    private static readonly int MaskTexelId = Shader.PropertyToID("_MaskTexel");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int WarpId = Shader.PropertyToID("_Warp");
    private static readonly int CaId = Shader.PropertyToID("_CA");
    private static readonly int DarkenId = Shader.PropertyToID("_Darken");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int SeedSparkId = Shader.PropertyToID("_SeedSpark");
    private static readonly int SpecLoId = Shader.PropertyToID("_SpecLo");
    private static readonly int SpecHiId = Shader.PropertyToID("_SpecHi");
    private static readonly int MilkId = Shader.PropertyToID("_Milk");
    private static readonly int RoomLumId = Shader.PropertyToID("_RoomLum");
    private static readonly int RoomColorId = Shader.PropertyToID("_RoomColor");
    private static readonly int ShaderRevId = Shader.PropertyToID("_ShaderRev");

    private Material? _crackBakeMat;
    private Material? _glassApplyMat;
    private readonly FeedLamps _lamps = new();
    private bool _glassShadersChecked;
    private bool _glassShadersAvailable;
    private const int LinePass = 0;
    private const int FacePass = 1;

    private RenderTexture? _crackMaskRt;
    private RenderTexture? _crackFaceRt;
    private RenderTexture? _crackRoughRt;
    private RenderTexture? _glassSceneCopy;
    private int _bakedStage = -1;
    private bool _bakedBack;
    private int _bakedStar = -1;
    private int _bakedWidth;
    private int _bakedHeight;

    private ulong _lastHolderId = ulong.MaxValue;
    private float _lastHeldAt = -100f;
    private float _crackHitAt = -100f;
    private bool _crackWasOnGround = true;

    private void UpdateCrackDetection()
    {
        if (!IsServer || !_networkReady) return;

        var holder = playerHeldBy;
        if (holder != null)
        {
            _lastHolderId = holder.actualClientId;
            _lastHeldAt = Time.time;
            _crackWasOnGround = true;
            return;
        }

        bool onGround = hasHitGround;
        if (onGround && !_crackWasOnGround)
        {
            float fallHeight = startFallingPosition.y - targetFloorPosition.y;
            if (fallHeight >= Constants.Cracks.MinFallHeight)
            {
                BumpCrackStage(back: false, heavy: true);
            }
        }
        _crackWasOnGround = onGround;
    }

    internal void ReportHit(int damage)
    {
        if (!_networkReady) return;
        if (IsServer) ApplyHit(damage, NetworkManager.LocalClientId);
        else CrackHitServerRpc(damage);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CrackHitServerRpc(int damage, ServerRpcParams rpcParams = default) =>
        ApplyHit(damage, rpcParams.Receive.SenderClientId);

    private void ApplyHit(int damage, ulong sender)
    {
        if (!IsServer || !IsSpawned || damage <= 0) return;

        PlayerControllerB? holder = playerHeldBy;
        bool held = holder != null && holder.actualClientId == sender;
        bool justDropped = holder == null && _lastHolderId == sender
            && Time.time - _lastHeldAt <= Constants.Cracks.DropGrace;
        if (!held && !justDropped) return;

        float now = Time.time;
        if (now - _crackHitAt < Constants.Cracks.HitCooldown) return;
        _crackHitAt = now;

        bool back = _cameraMode.Value;
        var lens = back ? _crackStageBack : _crackStageFront;
        int before = lens.Value;
        BumpCrackStage(back, damage >= Constants.Cracks.HeavyDamage);
        if (lens.Value != before)
        {
            Plugin.Log.LogDebug($"Camera {NetworkObjectId} cracked by a {damage} damage hit.");
        }
    }

    private void BumpCrackStage(bool back, bool heavy)
    {
        int max = Mathf.Clamp(VideoCameraConfig.CrackMaxStages.Value, 1, 3);
        var lens = back ? _crackStageBack : _crackStageFront;
        if (lens.Value >= max) return;
        lens.Value += 1;
        if (heavy)
        {
            _crackStarMask.Value |= 1 << lens.Value;
        }
    }

    internal static void ServerRepairAllLenses(string reason)
    {
        foreach (VideoCameraItem camera in UnityEngine.Object.FindObjectsOfType<VideoCameraItem>(true))
        {
            try
            {
                camera.ServerRepairLenses();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to repair camera lenses on {reason}: {ex.Message}");
            }
        }
    }

    internal void ServerRepairLenses()
    {
        if (!IsServer) return;
        if (_crackStageFront.Value == 0 && _crackStageBack.Value == 0) return;
        if (_crackStageFront.Value != 0) _crackStageFront.Value = 0;
        if (_crackStageBack.Value != 0) _crackStageBack.Value = 0;
        if (_crackStarMask.Value != 0) _crackStarMask.Value = 0;
        Plugin.Log.LogDebug($"Camera {NetworkObjectId} lenses repaired.");
    }

    private bool LoadGlassShaders()
    {
        if (_glassShadersChecked) return _glassShadersAvailable;
        _glassShadersChecked = true;

        Shader? bake = AssetsLoader.LoadOptionalAsset<Shader>(Constants.Cracks.BakeShaderAssetName);
        Shader? apply = AssetsLoader.LoadOptionalAsset<Shader>(Constants.Cracks.ApplyShaderAssetName);
        if (bake == null || apply == null)
        {
            Plugin.Log.LogWarning("Glass damage shaders are missing from bundle, cracked lenses are disabled.");
            return false;
        }

        _crackBakeMat = new Material(bake) { name = "VC_CrackBake" };
        _glassApplyMat = new Material(apply) { name = "VC_GlassApply" };
        LogIfShaderStale(_crackBakeMat, Constants.Cracks.BakeShaderAssetName);
        LogIfShaderStale(_glassApplyMat, Constants.Cracks.ApplyShaderAssetName);
        if (_crackBakeMat.passCount <= FacePass)
        {
            Plugin.Log.LogWarning("Glass damage bake shader in bundle has no face pass, cracked lenses are disabled.");
            return false;
        }
        _glassShadersAvailable = true;
        return true;
    }

    private float LensSeed(bool back)
    {
        float frontSeed = (float)(NetworkObjectId % 91UL) + 1f;
        return back ? frontSeed + 43f : frontSeed;
    }

    private static void LogIfShaderStale(Material mat, string label)
    {
        int rev = mat.HasProperty(ShaderRevId) ? Mathf.RoundToInt(mat.GetFloat(ShaderRevId)) : 0;
        if (rev >= Constants.Cracks.ShaderRevision) return;
        Plugin.Log.LogWarning($"Shader {label} in bundle is revision {rev} but mod needs {Constants.Cracks.ShaderRevision}.");
    }

    private void UpdateCrackMask(int stage, bool back, int w, int h)
    {
        int star = _crackStarMask.Value;
        if (_crackMaskRt != null && _crackFaceRt != null && _crackRoughRt != null && _bakedStage == stage
            && _bakedBack == back && _bakedStar == star && _bakedWidth == w && _bakedHeight == h)
        {
            return;
        }

        if (_crackMaskRt == null || _crackFaceRt == null || _crackRoughRt == null
            || _crackMaskRt.width != w || _crackMaskRt.height != h)
        {
            ReleaseCrackMaskRts();
            _crackMaskRt = CreateCrackRt("VC_CrackMask", RenderTextureFormat.ARGBHalf, w, h);
            _crackFaceRt = CreateCrackRt("VC_CrackFace", RenderTextureFormat.ARGBHalf, w, h);
            _crackRoughRt = CreateCrackRt("VC_CrackRough", RenderTextureFormat.RHalf, w, h);
        }

        _crackBakeMat!.SetFloat(StageId, stage);
        _crackBakeMat.SetFloat(SeedId, LensSeed(back));
        _crackBakeMat.SetFloat(AspectId, (float)w / Mathf.Max(1, h));
        _crackBakeMat.SetFloat(FlipYId, Constants.Cracks.BakeFlipY);
        _crackBakeMat.SetFloat(CrushRadiusId, Constants.Cracks.CrushRadius);
        _crackBakeMat.SetFloat(CrushStrengthId, Constants.Cracks.CrushStrength);
        _crackBakeMat.SetFloat(StarSitesId, star);
        _crackBakeMat.SetFloat(FaceDepthId, Constants.Cracks.FaceDepth);

        int supersample = MaskSupersampleFor(w);
        if (supersample <= 1)
        {
            DrawCrackFace(_crackMaskRt, _crackFaceRt, _crackRoughRt);
        }
        else
        {
            BakeCrackSupersampled(w, h, supersample);
        }

        _bakedStage = stage;
        _bakedBack = back;
        _bakedStar = star;
        _bakedWidth = w;
        _bakedHeight = h;

        ReadbackCrackFlow();
    }

    private static int MaskSupersampleFor(int width)
    {
        int ss = 1;
        while (ss < Constants.Cracks.MaskSupersampleMax
               && width * ss < Constants.Cracks.MaskBakeWidth)
        {
            ss *= 2;
        }
        return ss;
    }

    private static RenderTexture CreateCrackRt(string name, RenderTextureFormat format, int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, format)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }

    private static RenderTexture GetCrackTemp(RenderTextureFormat format, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, format);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }

    private void DrawCrackFace(RenderTexture mask, RenderTexture face, RenderTexture rough)
    {
        _crackBakeMat!.SetFloat(FaceOutId, 0f);
        Graphics.Blit(null, mask, _crackBakeMat, FacePass);
        _crackBakeMat.SetFloat(FaceOutId, 1f);
        Graphics.Blit(null, face, _crackBakeMat, FacePass);
        _crackBakeMat.SetFloat(FaceOutId, 2f);
        Graphics.Blit(null, rough, _crackBakeMat, FacePass);
    }

    private void BakeCrackSupersampled(int w, int h, int supersample)
    {
        int cw = w * supersample;
        int ch = h * supersample;
        RenderTexture mask = GetCrackTemp(RenderTextureFormat.ARGBHalf, cw, ch);
        RenderTexture face = GetCrackTemp(RenderTextureFormat.ARGBHalf, cw, ch);
        RenderTexture rough = GetCrackTemp(RenderTextureFormat.RHalf, cw, ch);
        DrawCrackFace(mask, face, rough);
        ShrinkCrack(mask, _crackMaskRt!);
        ShrinkCrack(face, _crackFaceRt!);
        ShrinkCrack(rough, _crackRoughRt!);
    }

    private void BakeCrackLine(RenderTexture dst)
    {
        int supersample = MaskSupersampleFor(dst.width);
        if (supersample <= 1)
        {
            Graphics.Blit(null, dst, _crackBakeMat, LinePass);
            return;
        }

        RenderTexture src = GetCrackTemp(RenderTextureFormat.ARGBHalf, dst.width * supersample, dst.height * supersample);
        Graphics.Blit(null, src, _crackBakeMat, LinePass);
        ShrinkCrack(src, dst);
    }

    private static void ShrinkCrack(RenderTexture src, RenderTexture dst)
    {
        int w = dst.width;
        int h = dst.height;
        int cw = src.width;
        int ch = src.height;
        while (cw > w)
        {
            cw = Mathf.Max(w, cw / 2);
            ch = Mathf.Max(h, ch / 2);
            if (cw == w && ch == h)
            {
                Graphics.Blit(src, dst);
                break;
            }

            RenderTexture step = GetCrackTemp(dst.format, cw, ch);
            Graphics.Blit(src, step);
            RenderTexture.ReleaseTemporary(src);
            src = step;
        }

        RenderTexture.ReleaseTemporary(src);
    }

    private void ReadbackCrackFlow()
    {
        if (_crackMaskRt == null) return;
        RenderTexture line = GetCrackTemp(RenderTextureFormat.ARGBHalf, _crackMaskRt.width, _crackMaskRt.height);
        BakeCrackLine(line);

        RenderTexture small = RenderTexture.GetTemporary(
            FeedCrackFlow.GridW, FeedCrackFlow.GridH, 0, RenderTextureFormat.R8);
        small.filterMode = FilterMode.Bilinear;
        Graphics.Blit(line, small);
        RenderTexture.ReleaseTemporary(line);

        AsyncGPUReadback.Request(small, 0, TextureFormat.R8, request =>
        {
            if (!request.hasError)
            {
                _rainSim.CrackFlow.SetGrid(request.GetData<byte>().ToArray());
            }
            RenderTexture.ReleaseTemporary(small);
        });
    }

    private void DrawGlassDamage()
    {
        if (_feedTexture == null) return;

        float rainAmount = UpdateRain();

        int stage = 0;
        if (VideoCameraConfig.CrackOnLens.Value)
        {
            int maxStages = Mathf.Clamp(VideoCameraConfig.CrackMaxStages.Value, 1, 3);
            stage = Mathf.Min(_cameraMode.Value ? _crackStageBack.Value : _crackStageFront.Value, maxStages);
        }
        if (stage <= 0 && _rainSim.CrackFlow.Active) _rainSim.CrackFlow.Clear();

        bool rainVisible = rainAmount > 0.001f || _rainSim.DropCount > 0;
        if (stage <= 0 && !rainVisible) return;
        if (!LoadGlassShaders() || _glassApplyMat == null) return;

        int w = _feedTexture.width;
        int h = _feedTexture.height;
        if (_glassSceneCopy == null || _glassSceneCopy.width != w || _glassSceneCopy.height != h)
        {
            if (_glassSceneCopy != null) { _glassSceneCopy.Release(); Destroy(_glassSceneCopy); }
            _glassSceneCopy = new RenderTexture(w, h, 0, _feedTexture.format)
            {
                name = "VC_GlassSceneCopy",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                useMipMap = true,
                autoGenerateMips = false,
            };
            _glassSceneCopy.Create();
        }

        if (stage > 0)
        {
            UpdateCrackMask(stage, _cameraMode.Value, w, h);
        }

        Graphics.Blit(_feedTexture, _glassSceneCopy);
        if (rainVisible) _glassSceneCopy.GenerateMips();

        Material mat = _glassApplyMat;
        bool cracked = stage > 0 && _crackMaskRt != null && _crackFaceRt != null && _crackRoughRt != null;
        mat.SetTexture(CrackMaskId, cracked ? _crackMaskRt : Texture2D.blackTexture);
        mat.SetTexture(CrackFaceId, cracked ? _crackFaceRt : Texture2D.blackTexture);
        mat.SetTexture(CrackRoughId, cracked ? _crackRoughRt : Texture2D.blackTexture);
        mat.SetVector(MaskTexelId, new Vector4(1f / w, 1f / h, w, h));
        mat.SetFloat(IntensityId, stage > 0 ? Constants.Cracks.EffectIntensity : 0f);
        mat.SetFloat(WarpId, Constants.Cracks.Warp);
        mat.SetFloat(CaId, Constants.Cracks.ChromaticAberration);
        mat.SetFloat(DarkenId, Constants.Cracks.Darken);
        mat.SetFloat(GlowId, Constants.Cracks.Glow * (1f + Constants.Cracks.WetGlintBoost * _rainWetness));
        mat.SetFloat(SeedSparkId, LensSeed(_cameraMode.Value) * 3f);
        mat.SetFloat(SpecLoId, Constants.Cracks.SpecLo);
        mat.SetFloat(SpecHiId, Constants.Cracks.SpecHi);
        mat.SetFloat(MilkId, Constants.Cracks.Milk);
        mat.SetFloat(AspectId, (float)w / Mathf.Max(1, h));

        Camera? cam = ActiveCamera;
        if (cam != null)
        {
            Transform view = cam.transform;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            mat.SetVector(FaceRightId, view.right);
            mat.SetVector(FaceUpId, view.up);
            mat.SetVector(FaceForwardId, view.forward);
            mat.SetVector(FaceTanId, new Vector4(tanY * w / Mathf.Max(1, h), tanY, 0f, 0f));
            if (cracked)
            {
                _lamps.Update(cam, transform, playerHeldBy != null ? playerHeldBy.transform : null, !isInFactory);
                _lamps.Apply(mat);
            }
            else
            {
                _lamps.Clear(mat);
            }
        }
        else
        {
            _lamps.Clear(mat);
        }

        float roomLum = 0.2126f * _rawAmbient.r + 0.7152f * _rawAmbient.g + 0.0722f * _rawAmbient.b;
        mat.SetFloat(RoomLumId, roomLum);
        mat.SetVector(RoomColorId, new Vector4(_rawAmbient.r, _rawAmbient.g, _rawAmbient.b, 1f));

        BindRainParams(mat, rainAmount);

        Graphics.Blit(_glassSceneCopy, _feedTexture, mat);
    }

    private void ReleaseCrackRts()
    {
        if (_glassSceneCopy != null) { _glassSceneCopy.Release(); Destroy(_glassSceneCopy); _glassSceneCopy = null; }
        ReleaseCrackMaskRts();
    }

    private void ReleaseCrackMaskRts()
    {
        if (_crackMaskRt != null) { _crackMaskRt.Release(); Destroy(_crackMaskRt); _crackMaskRt = null; }
        if (_crackFaceRt != null) { _crackFaceRt.Release(); Destroy(_crackFaceRt); _crackFaceRt = null; }
        if (_crackRoughRt != null) { _crackRoughRt.Release(); Destroy(_crackRoughRt); _crackRoughRt = null; }
        _bakedStage = -1;
    }
}
