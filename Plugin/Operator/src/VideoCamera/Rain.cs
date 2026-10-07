using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static Material? _rainPrepMaterial;
    private static bool _rainPrepChecked;
    private static GameObject? _rainAmbience;
    private static bool _rainAmbienceSearched;

    private static readonly int RainDropsId = Shader.PropertyToID("_RainDrops");
    private static readonly int RainCanvasSizeId = Shader.PropertyToID("_RainCanvasSize");
    private static readonly int RainInBlackId = Shader.PropertyToID("_InBlack");
    private static readonly int RainInWhiteId = Shader.PropertyToID("_InWhite");
    private static readonly int RainOutWhiteId = Shader.PropertyToID("_OutWhite");
    private static readonly int RainOutBlackId = Shader.PropertyToID("_OutBlack");
    private static readonly int RainHeightId = Shader.PropertyToID("_RainHeight");
    private static readonly int RainAmountId = Shader.PropertyToID("_RainAmount");
    private static readonly int RainSpecularId = Shader.PropertyToID("_RainSpecular");
    private static readonly int RainDropId = Shader.PropertyToID("_RainDrop");
    private static readonly int RainRadiusScaleId = Shader.PropertyToID("_RainRadiusScale");
    private static readonly int RainRefractGainId = Shader.PropertyToID("_RainRefractGain");
    private static readonly int RainSinContactId = Shader.PropertyToID("_RainSinContact");
    private static readonly int RainMipMaxId = Shader.PropertyToID("_RainMipMax");
    private static readonly int RainRimDarkId = Shader.PropertyToID("_RainRimDark");
    private static readonly int RainFresnelId = Shader.PropertyToID("_RainFresnel");

    private readonly FeedRainSim _rainSim = new();
    private RenderTexture? _rainRtA;
    private RenderTexture? _rainRtB;
    private RenderTexture? _rainDropRt;
    private GraphicsBuffer? _rainDropBuffer;

    private float _rainCeilingTimer;
    private bool _rainOccluded;
    private float _rainWetness;
    private float _rainRampLastTime = -1f;
    private float _rainWindX;
    private float _rainFallMul = 1f;
    private Vector3 _rainPrevFwd;
    private bool _rainHasPrev;

    private static bool IsRaining()
    {
        var tod = TimeOfDay.Instance;
        if (tod != null)
        {
            switch (tod.currentLevelWeather)
            {
                case LevelWeatherType.Rainy:
                case LevelWeatherType.Stormy:
                case LevelWeatherType.Flooded:
                    return true;
            }
        }
        return RainAmbienceActive();
    }

    private static bool RainAmbienceActive()
    {
        if (!_rainAmbienceSearched || _rainAmbience == null)
        {
            _rainAmbience = GameObject.Find(
                "Systems/GameSystems/TimeAndWeather/RainParticleContainer/RainAmbience");
            _rainAmbienceSearched = true;
        }
        return _rainAmbience != null && _rainAmbience.activeInHierarchy;
    }

    private bool IsLensOccluded() => IsCovered();

    private float UpdateRain()
    {
        float rainAmount = VideoCameraConfig.GetEffect(VideoCameraConfig.RainIntensity);
        if (rainAmount <= 0f)
        {
            _rainWetness = 0f;
            _rainSim.Reset();
            _rainRampLastTime = -1f;
            return 0f;
        }

        float now = Time.time;
        if (_rainRampLastTime < 0f)
        {
            _rainOccluded = IsLensOccluded();
            _rainCeilingTimer = 0.75f;
            _rainRampLastTime = now;
        }

        float dt = Mathf.Min(now - _rainRampLastTime, 0.25f);
        if (dt > 0f)
        {
            _rainRampLastTime = now;
            _rainCeilingTimer -= dt;
            if (_rainCeilingTimer <= 0f)
            {
                _rainOccluded = IsLensOccluded();
                _rainCeilingTimer = 0.75f;
            }

            bool exposed = IsRaining() && !_rainOccluded;
            float target = exposed ? 1f : 0f;
            float rate = target > _rainWetness
                ? Constants.RainFeed.WetRampSpeed
                : 1f / Mathf.Max(Constants.RainFeed.DryTime, 0.1f);
            _rainWetness = Mathf.MoveTowards(_rainWetness, target, rate * dt);

            Camera? cam = ActiveCamera;
            float lookUp = 0.5f, downCut = 1f;
            if (cam != null)
            {
                float angle = Vector3.Angle(cam.transform.forward, Vector3.up);
                lookUp = Mathf.Clamp01((90f - angle) / 90f);
                downCut = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((150f - angle) / 60f));
                UpdateRainWind(cam.transform, dt);
            }

            float density = (exposed ? _rainWetness : 0f)
                * Mathf.Lerp(Constants.RainFeed.LookUpBaseDensity, 1f, lookUp)
                * downCut
                * Constants.RainFeed.Wetness * rainAmount;

            if (_rainWetness > 0.001f || _rainSim.DropCount > 0)
            {
                _rainSim.Update(
                    dt,
                    density,
                    !exposed,
                    Constants.RainFeed.DryTime,
                    Constants.RainFeed.DropSize,
                    Constants.RainFeed.SlideSpeed * Mathf.Lerp(Constants.RainFeed.LookUpBaseSpeed, 1f, lookUp),
                    _rainWindX,
                    _rainFallMul);
            }
        }

        return _rainWetness * Constants.RainFeed.Amount * Mathf.Min(1f, rainAmount);
    }

    private void UpdateRainWind(Transform camT, float dt)
    {
        Vector3 fwd = camT.forward;
        if (!_rainHasPrev || dt <= 1e-5f)
        {
            _rainPrevFwd = fwd;
            _rainHasPrev = true;
            return;
        }

        Vector3 prevH = new(_rainPrevFwd.x, 0f, _rainPrevFwd.z);
        Vector3 curH = new(fwd.x, 0f, fwd.z);
        float yawVel = 0f;
        if (prevH.sqrMagnitude > 1e-6f && curH.sqrMagnitude > 1e-6f)
        {
            yawVel = Vector3.SignedAngle(prevH, curH, Vector3.up) * Mathf.Deg2Rad / dt;
        }
        float pitchVel = (Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f))
            - Mathf.Asin(Mathf.Clamp(_rainPrevFwd.y, -1f, 1f))) / dt;

        float targetX = Mathf.Clamp(-yawVel * Constants.RainFeed.WindGain * Constants.RainFeed.Inertia,
            -Constants.RainFeed.WindMax, Constants.RainFeed.WindMax);
        float targetFall = 1f + Mathf.Clamp(-pitchVel * Constants.RainFeed.PitchFallGain * Constants.RainFeed.Inertia,
            -Constants.RainFeed.PitchFallClamp, Constants.RainFeed.PitchFallClamp);

        float k = 1f - Mathf.Exp(-dt / Constants.RainFeed.WindTau);
        _rainWindX = Mathf.Lerp(_rainWindX, targetX, k);
        _rainFallMul = Mathf.Lerp(_rainFallMul, targetFall, k);

        _rainPrevFwd = fwd;
    }

    private static bool LoadRainPrep()
    {
        if (_rainPrepChecked) return _rainPrepMaterial != null;
        _rainPrepChecked = true;

        Shader? shader = AssetsLoader.LoadOptionalAsset<Shader>("RainPrep");
        if (shader == null)
        {
            Plugin.Log.LogWarning("Rain prep shader 'RainPrep' is not found in bundle, feed rain is disabled.");
            return false;
        }
        _rainPrepMaterial = new Material(shader) { name = "VC_RainPrep" };
        return true;
    }

    private void RenderRainPrep(Material applyMat)
    {
        int dropCount = _rainSim.DropCount;
        if (dropCount <= 0 || !LoadRainPrep() || _rainPrepMaterial == null)
        {
            applyMat.SetTexture(RainHeightId, Texture2D.blackTexture);
            applyMat.SetTexture(RainDropId, Texture2D.blackTexture);
            return;
        }

        if (_rainRtA == null || _rainRtB == null)
        {
            _rainRtA = new RenderTexture(FeedRainSim.CanvasW, FeedRainSim.CanvasH, 0, RenderTextureFormat.R8)
            { name = "VC_RainPrepA", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _rainRtB = new RenderTexture(FeedRainSim.CanvasW, FeedRainSim.CanvasH, 0, RenderTextureFormat.R8)
            { name = "VC_RainPrepB", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        if (_rainDropRt == null)
        {
            _rainDropRt = new RenderTexture(FeedRainSim.CanvasW, FeedRainSim.CanvasH, 16, RenderTextureFormat.ARGB32)
            { name = "VC_RainDrops", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            _rainDropRt.Create();
        }

        _rainDropBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, FeedRainSim.MaxGpuDrops * 2, 16);
        _rainDropBuffer.SetData(_rainSim.GpuData, 0, 0, dropCount * 2);
        _rainPrepMaterial.SetBuffer(RainDropsId, _rainDropBuffer);
        _rainPrepMaterial.SetVector(RainCanvasSizeId, new Vector4(
            FeedRainSim.CanvasW, FeedRainSim.CanvasH, 1f / FeedRainSim.CanvasW, 1f / FeedRainSim.CanvasH));
        _rainPrepMaterial.SetFloat(RainInBlackId, Constants.RainFeed.LevelInBlack);
        _rainPrepMaterial.SetFloat(RainInWhiteId, Constants.RainFeed.LevelInWhite);
        _rainPrepMaterial.SetFloat(RainOutWhiteId, Constants.RainFeed.LevelOutWhite);
        _rainPrepMaterial.SetFloat(RainOutBlackId, Constants.RainFeed.LevelOutBlack);

        RenderTexture? prev = RenderTexture.active;
        Graphics.SetRenderTarget(_rainRtA);
        GL.Clear(false, true, Color.clear);
        if (_rainPrepMaterial.SetPass(3))
        {
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, dropCount);
        }
        for (int i = 0; i < Constants.RainFeed.FusionIterations; i++)
        {
            Graphics.Blit(_rainRtA, _rainRtB, _rainPrepMaterial, 0);
            Graphics.Blit(_rainRtB, _rainRtA, _rainPrepMaterial, 1);
        }
        Graphics.Blit(_rainRtA, _rainRtB, _rainPrepMaterial, 2);

        Graphics.SetRenderTarget(_rainDropRt);
        GL.Clear(true, true, Color.clear);
        if (_rainPrepMaterial.SetPass(4))
        {
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, dropCount);
        }
        RenderTexture.active = prev;

        applyMat.SetTexture(RainHeightId, _rainRtB);
        applyMat.SetTexture(RainDropId, _rainDropRt);
    }

    private void BindRainParams(Material applyMat, float rainAmount)
    {
        applyMat.SetFloat(RainAmountId, rainAmount);
        applyMat.SetFloat(RainSpecularId, Constants.RainFeed.Specular);
        applyMat.SetVector(RainRadiusScaleId, new Vector4(
            1f, (float)FeedRainSim.CanvasW / FeedRainSim.CanvasH, 0f, 0f));

        Camera? feed = ActiveCamera;
        float halfFov = feed != null ? feed.fieldOfView * 0.5f * Mathf.Deg2Rad : 0.5236f;
        float focal = 0.5f / Mathf.Max(0.05f, Mathf.Tan(halfFov));
        applyMat.SetFloat(RainRefractGainId, focal * Constants.RainFeed.RefractGain
                                             * Constants.RainFeed.Distortion * 2f);
        applyMat.SetFloat(RainSinContactId, Constants.RainFeed.SinContactAngle);
        applyMat.SetFloat(RainMipMaxId, Constants.RainFeed.MipMax);
        applyMat.SetFloat(RainRimDarkId, Constants.RainFeed.RimDark);
        applyMat.SetFloat(RainFresnelId, Constants.RainFeed.Fresnel);

        if (rainAmount > 0.001f)
        {
            RenderRainPrep(applyMat);
        }
        else
        {
            applyMat.SetTexture(RainHeightId, Texture2D.blackTexture);
            applyMat.SetTexture(RainDropId, Texture2D.blackTexture);
        }
    }

    private void ReleaseRainRts()
    {
        if (_rainRtA != null) { _rainRtA.Release(); Destroy(_rainRtA); _rainRtA = null; }
        if (_rainRtB != null) { _rainRtB.Release(); Destroy(_rainRtB); _rainRtB = null; }
        if (_rainDropRt != null) { _rainDropRt.Release(); Destroy(_rainDropRt); _rainDropRt = null; }
        if (_rainDropBuffer != null) { _rainDropBuffer.Dispose(); _rainDropBuffer = null; }
        _rainSim.Reset();
    }
}
