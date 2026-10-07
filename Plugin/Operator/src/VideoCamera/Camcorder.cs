using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private Material? _camcorderMat;
    private bool _camcorderChecked;
    private RenderTexture? _camcorderSceneCopy;

    private float _fearSmoothed;
    private float _fear;
    private float _fearPrev;
    private float _glitchIntensity;
    private float _glitchSeed;
    private int _camcorderIdleFrames;

    private static readonly int CamAspectId = Shader.PropertyToID("_Aspect");
    private static readonly int CamTimeId = Shader.PropertyToID("_CamTime");
    private static readonly int CamVignetteId = Shader.PropertyToID("_Vignette");
    private static readonly int CamBarrelId = Shader.PropertyToID("_Barrel");
    private static readonly int CamCaId = Shader.PropertyToID("_CA");
    private static readonly int CamGrainId = Shader.PropertyToID("_Grain");
    private static readonly int CamScanId = Shader.PropertyToID("_Scan");
    private static readonly int CamDustId = Shader.PropertyToID("_Dust");
    private static readonly int CamDustRefId = Shader.PropertyToID("_DustRef");
    private static readonly int CamWbId = Shader.PropertyToID("_WB");
    private static readonly int CamColdDesatId = Shader.PropertyToID("_ColdDesat");
    private static readonly int CamGlitchId = Shader.PropertyToID("_Glitch");
    private static readonly int CamGlitchSeedId = Shader.PropertyToID("_GlitchSeed");
    private static readonly int CamBreathId = Shader.PropertyToID("_Breath");

    private bool LoadCamcorderMaterial()
    {
        if (_camcorderChecked) return _camcorderMat != null;
        _camcorderChecked = true;

        Shader? shader = AssetsLoader.LoadOptionalAsset<Shader>("Camcorder");
        if (shader == null) return false;

        _camcorderMat = new Material(shader) { name = "VC_Camcorder" };
        return true;
    }

    private void TickCamcorderFear()
    {
        float dt = Time.deltaTime;
        float response = Mathf.Max(0f, VideoCameraConfig.FearResponse.Value);

        var local = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        bool holdingLocally = playerHeldBy != null && playerHeldBy == local;
        bool rollingLocally = playerHeldBy == null && _recorder != null && _recorder.IsActive;
        float fearRaw;
        if (holdingLocally || rollingLocally)
        {
            fearRaw = StartOfRound.Instance != null ? Mathf.Clamp01(StartOfRound.Instance.fearLevel) : 0f;
            TrySetOperatorFear(fearRaw);
        }
        else
        {
            fearRaw = Mathf.Clamp01(_operatorFear.Value);
        }
        float fearDriver = fearRaw * response;

        _fearSmoothed = Mathf.MoveTowards(_fearSmoothed, fearRaw, dt * 3f);
        _fear = Mathf.Clamp01(_fearSmoothed * response);

        float glitch = VideoCameraConfig.GetEffect(VideoCameraConfig.TrackingGlitch);
        if (glitch > 0f)
        {
            float sensitivity = Mathf.Max(0.25f, glitch);
            if (fearDriver - _fearPrev > 0.08f / sensitivity && _glitchIntensity < 0.4f * glitch)
            {
                _glitchIntensity = Mathf.Min(1f, glitch);
                _glitchSeed = Random.value * 1000f;
            }
            _glitchIntensity = Mathf.MoveTowards(_glitchIntensity, 0f, dt * sensitivity / 0.18f);
        }
        else
        {
            _glitchIntensity = 0f;
        }

        _fearPrev = fearDriver;
    }

    private void DrawCamcorderLook()
    {
        if (_feedTexture == null) return;
        if (!LoadCamcorderMaterial() || _camcorderMat == null) return;

        float fear = _fear;
        float L(float a, float b) => Mathf.Lerp(a, b, fear);

        bool nvActive = _nvAmount > 0.5f;

        float vigBase = L(0.30f, 0.48f);
        float vig = vigBase * VideoCameraConfig.GetEffect(VideoCameraConfig.Vignette);
        float barrel = Mathf.Min(
            L(0.060f, 0.090f) * VideoCameraConfig.GetEffect(VideoCameraConfig.BarrelDistortion),
            Constants.Visual.BarrelMax);
        float ca = L(0.0012f, 0.0030f) * VideoCameraConfig.GetEffect(VideoCameraConfig.ChromaticAberration);
        float grain = L(0.006f, 0.0225f) * VideoCameraConfig.GetEffect(VideoCameraConfig.Grain);
        float scan = Mathf.Clamp01(L(0.05f, 0.12f) * VideoCameraConfig.GetEffect(VideoCameraConfig.Scanlines));

        float dustRef = L(0.06f, 0.10f);
        float dust = dustRef * VideoCameraConfig.GetEffect(VideoCameraConfig.LensDust);

        if (grain > 0f && _nightVisionOn.Value) grain *= 0.45f;

        if (nvActive)
        {
            vig *= Constants.NightVision.VignetteMultiplier;
            dust *= 0.30f;
            dustRef *= 0.30f;
        }

        Vector3 wb = Vector3.one;
        if (!nvActive)
        {
            float wbAmount = VideoCameraConfig.GetEffect(VideoCameraConfig.WhiteBalanceDrift);
            if (wbAmount > 0f)
            {
                Vector3 drift = new Vector3(1.045f - 0.045f * fear, 1f, 0.96f + 0.02f * fear);
                wb = Vector3.one + (drift - Vector3.one) * wbAmount;
            }

            float pumpAmount = VideoCameraConfig.GetEffect(VideoCameraConfig.ExposurePump);
            if (pumpAmount > 0f && fear > 0.05f)
            {
                float pump = 1f + fear * 0.14f * (Mathf.PerlinNoise(Time.time * 1.7f, _glitchSeed) - 0.5f) * 2f;
                wb *= 1f + (pump - 1f) * pumpAmount;
            }
        }

        float coldDesat = 0f;
        float coldAmount = VideoCameraConfig.GetEffect(VideoCameraConfig.ColdDesaturation);
        if (coldAmount > 0f && fear > 0.4f && !nvActive)
        {
            coldDesat = Mathf.Clamp01((fear - 0.4f) / 0.6f * coldAmount);
        }

        float breath = 0f;
        float breathAmount = VideoCameraConfig.GetEffect(VideoCameraConfig.FocusBreathing);
        if (breathAmount > 0f && VideoCameraConfig.FocusMode.Value != CameraFocusMode.Off)
        {
            breath = _afBreath * breathAmount;
        }

        bool anyEffect = vig != 0f || barrel != 0f || ca != 0f || grain != 0f || scan != 0f
            || dust != 0f || coldDesat != 0f || _glitchIntensity != 0f || breath != 0f
            || wb != Vector3.one;
        if (!anyEffect)
        {
            if (++_camcorderIdleFrames > Constants.Visual.CamcorderIdleReleaseFrames)
            {
                ReleaseCamcorderRts();
            }
            return;
        }

        _camcorderIdleFrames = 0;

        int w = _feedTexture.width;
        int h = _feedTexture.height;
        if (_camcorderSceneCopy == null || _camcorderSceneCopy.width != w || _camcorderSceneCopy.height != h)
        {
            ReleaseCamcorderRts();
            _camcorderSceneCopy = new RenderTexture(w, h, 0, _feedTexture.format)
            {
                name = "VC_CamcorderSceneCopy",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _camcorderSceneCopy.Create();
        }
        if (_camcorderSceneCopy == null) return;

        _camcorderMat.SetFloat(CamAspectId, (float)w / Mathf.Max(1, h));
        _camcorderMat.SetFloat(CamTimeId, Time.time);
        _camcorderMat.SetFloat(CamVignetteId, vig);
        _camcorderMat.SetFloat(CamBarrelId, barrel);
        _camcorderMat.SetFloat(CamCaId, ca);
        _camcorderMat.SetFloat(CamGrainId, grain);
        _camcorderMat.SetFloat(CamScanId, scan);
        _camcorderMat.SetFloat(CamDustId, dust);
        _camcorderMat.SetFloat(CamDustRefId, Mathf.Max(1e-4f, dustRef));
        _camcorderMat.SetVector(CamWbId, wb);
        _camcorderMat.SetFloat(CamColdDesatId, coldDesat);
        _camcorderMat.SetFloat(CamGlitchId, _glitchIntensity);
        _camcorderMat.SetFloat(CamGlitchSeedId, _glitchSeed);
        _camcorderMat.SetFloat(CamBreathId, breath);

        Graphics.Blit(_feedTexture, _camcorderSceneCopy);
        Graphics.Blit(_camcorderSceneCopy, _feedTexture, _camcorderMat);
    }

    private void ReleaseCamcorderRts()
    {
        if (_camcorderSceneCopy != null)
        {
            _camcorderSceneCopy.Release();
            Destroy(_camcorderSceneCopy);
            _camcorderSceneCopy = null;
        }
    }
}
