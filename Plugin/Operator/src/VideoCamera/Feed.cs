using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Operator.Recording;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private bool ShouldRenderFeed(bool far)
    {
        if (ActiveCamera == null)
        {
            return false;
        }

        double now = Time.unscaledTimeAsDouble;
        if (now < _nextFeedRenderTime)
        {
            return false;
        }

        double interval = far ? 1.0 / Constants.Visual.FarScreenRate : GetFeedInterval();
        _gapAllowance = Math.Max(_lastInterval, interval);
        _lastInterval = interval;
        _nextFeedRenderTime = far || now - _nextFeedRenderTime > interval * 4d
            ? now + interval
            : _nextFeedRenderTime + interval;
        return true;
    }

    private static double GetFeedInterval() =>
        1.0 / Mathf.Clamp(VideoCameraConfig.RenderFps.Value, Constants.ConfigRanges.FeedRenderFpsMin, Constants.ConfigRanges.FeedRenderFpsMax);

    private FrameCapture? GetDrivingCapture()
    {
        FrameCapture? capture = _recorder?.Capture;
        if (capture == null || capture.Stalled) return null;
        if (_nvAmount > 0.001f || _nvBloom > 0.001f) return null;
        return capture.Interval >= GetFeedInterval() ? capture : null;
    }

    private bool ShouldRenderCapture(FrameCapture capture)
    {
        if (ActiveCamera == null)
        {
            return false;
        }

        double now = Time.unscaledTimeAsDouble;
        bool due = capture.TryDrive(Math.Min(Time.unscaledDeltaTime, Constants.Recording.MaxLookahead));
        _nextFeedRenderTime = Math.Max((due ? now : _lastFeedTime) + GetFeedInterval(), now);
        if (!due)
        {
            return false;
        }

        double interval = capture.Interval;
        _gapAllowance = Math.Max(_lastInterval, interval);
        _lastInterval = interval;
        return true;
    }

    private static readonly Plane[] ViewPlanes = new Plane[6];
    private static int _viewPlanesFrame = -1;
    private bool _screenFar;
    private double _lastInterval;
    private double _gapAllowance;

    private bool IsScreenVisible()
    {
        _screenFar = false;
        if (_screenRenderer == null || !_screenRenderer.enabled) return true;
        if (!_screenRenderer.gameObject.activeInHierarchy) return true;
        if (!_screenRenderer.isVisible) return false;

        Camera? view = StartOfRound.Instance.activeCamera;
        if (view == null) return true;

        if (_viewPlanesFrame != Time.frameCount)
        {
            GeometryUtility.CalculateFrustumPlanes(view, ViewPlanes);
            _viewPlanesFrame = Time.frameCount;
        }

        Bounds bounds = _screenRenderer.bounds;
        if (!GeometryUtility.TestPlanesAABB(ViewPlanes, bounds)) return false;

        _screenFar = IsScreenFar(view, bounds);
        return true;
    }

    private static bool IsScreenFar(Camera view, Bounds bounds)
    {
        float distance = Vector3.Distance(view.transform.position, bounds.center);
        float span = 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return span > 0f && bounds.size.magnitude / span * Screen.height < Constants.Visual.FarScreenSize;
    }

    private Renderer? _scanEffectRenderer;

    private Renderer? GetScanEffect()
    {
        if (_scanEffectRenderer != null) return _scanEffectRenderer;
        Animator? anim = HUDManager.Instance.scanEffectAnimator;
        if (anim != null)
        {
            _scanEffectRenderer = anim.GetComponent<Renderer>() ?? anim.GetComponentInChildren<Renderer>(true);
        }
        return _scanEffectRenderer;
    }

    private readonly struct HudFilterState
    {
        public readonly float Drunkness;
        public readonly float Poison;
        public readonly float Insanity;
        public readonly float Flashbang;
        public readonly float CadaverBloom;
        public readonly float Underwater;

        public HudFilterState(float drunkness, float poison, float insanity, float flashbang, float cadaverBloom, float underwater)
        {
            Drunkness = drunkness;
            Poison = poison;
            Insanity = insanity;
            Flashbang = flashbang;
            CadaverBloom = cadaverBloom;
            Underwater = underwater;
        }
    }

    private bool SuppressHudFilters(out HudFilterState snapshot)
    {
        HUDManager hud = HUDManager.Instance;

        snapshot = new HudFilterState(
            hud.drunknessFilter != null ? hud.drunknessFilter.weight : 0f,
            hud.poisonFilter != null ? hud.poisonFilter.weight : 0f,
            hud.insanityScreenFilter != null ? hud.insanityScreenFilter.weight : 0f,
            hud.flashbangScreenFilter != null ? hud.flashbangScreenFilter.weight : 0f,
            hud.CadaverBloomFilter != null ? hud.CadaverBloomFilter.weight : 0f,
            hud.underwaterScreenFilter != null ? hud.underwaterScreenFilter.weight : 0f);

        if (hud.underwaterScreenFilter != null)
        {
            hud.underwaterScreenFilter.weight = UnderwaterWeight;
        }

        if (hud.drunknessFilter != null) hud.drunknessFilter.weight = 0f;
        if (hud.poisonFilter != null) hud.poisonFilter.weight = 0f;
        if (hud.insanityScreenFilter != null) hud.insanityScreenFilter.weight = 0f;
        if (hud.flashbangScreenFilter != null) hud.flashbangScreenFilter.weight = 0f;
        if (hud.CadaverBloomFilter != null) hud.CadaverBloomFilter.weight = 0f;
        return true;
    }

    private static void RestoreHudFilters(in HudFilterState snapshot)
    {
        HUDManager hud = HUDManager.Instance;

        if (hud.drunknessFilter != null) hud.drunknessFilter.weight = snapshot.Drunkness;
        if (hud.poisonFilter != null) hud.poisonFilter.weight = snapshot.Poison;
        if (hud.insanityScreenFilter != null) hud.insanityScreenFilter.weight = snapshot.Insanity;
        if (hud.flashbangScreenFilter != null) hud.flashbangScreenFilter.weight = snapshot.Flashbang;
        if (hud.CadaverBloomFilter != null) hud.CadaverBloomFilter.weight = snapshot.CadaverBloom;
        if (hud.underwaterScreenFilter != null) hud.underwaterScreenFilter.weight = snapshot.Underwater;
    }

    private readonly struct SkyState
    {
        public readonly bool Valid;
        public readonly float BlackSky;
        public readonly bool SunDirect;
        public readonly bool SunIndirect;
        public readonly float SunDimmer;
        public readonly Vector2 DirectDimmers;
        public readonly float IndirectVolumetric;

        public SkyState(float blackSky, bool sunDirect, bool sunIndirect, float sunDimmer, Vector2 directDimmers, float indirectVolumetric)
        {
            Valid = true;
            BlackSky = blackSky;
            SunDirect = sunDirect;
            SunIndirect = sunIndirect;
            SunDimmer = sunDimmer;
            DirectDimmers = directDimmers;
            IndirectVolumetric = indirectVolumetric;
        }
    }

    private HDAdditionalLightData? _sunIndirectData;

    private HDAdditionalLightData? _sunDirectData;

    private static float GetVolumetricDimmer(HDAdditionalLightData data) =>
        data.affectsVolumetric ? data.volumetricDimmer : 1f;

    private static void SetVolumetricDimmer(HDAdditionalLightData data, float value)
    {
        if (data.affectsVolumetric) data.volumetricDimmer = value;
    }

    private HDAdditionalLightData? GetSunDirectData(Light? sunDirect)
    {
        if (sunDirect == null) return null;
        if (_sunDirectData != null && _sunDirectData.gameObject == sunDirect.gameObject)
        {
            return _sunDirectData;
        }

        _sunDirectData = sunDirect.GetComponent<HDAdditionalLightData>();
        return _sunDirectData;
    }

    private HDAdditionalLightData? GetSunIndirectData(Light? sunIndirect)
    {
        if (sunIndirect == null) return null;
        if (_sunIndirectData != null && _sunIndirectData.gameObject == sunIndirect.gameObject)
        {
            return _sunIndirectData;
        }

        _sunIndirectData = sunIndirect.GetComponent<HDAdditionalLightData>();
        return _sunIndirectData;
    }

    private bool SuppressSky(out SkyState snapshot)
    {
        snapshot = default;

        GameNetcodeStuff.PlayerControllerB? reference = GetViewer();
        if (reference == null) return false;

        bool inside = IsInside;
        if (inside == reference.isInsideFactory) return false;

        StartOfRound round = StartOfRound.Instance;
        TimeOfDay time = TimeOfDay.Instance;

        var blackSky = round.blackSkyVolume;
        Light sunDirect = time.sunDirect;
        Light sunIndirect = time.sunIndirect;
        if (blackSky == null && sunDirect == null && sunIndirect == null) return false;

        HDAdditionalLightData? directData = GetSunDirectData(sunDirect);
        HDAdditionalLightData? indirectData = GetSunIndirectData(sunIndirect);

        snapshot = new SkyState(
            blackSky != null ? blackSky.weight : 0f,
            sunDirect != null && sunDirect.enabled,
            sunIndirect != null && sunIndirect.enabled,
            indirectData != null ? indirectData.lightDimmer : -1f,
            directData != null ? new Vector2(directData.lightDimmer, GetVolumetricDimmer(directData)) : Vector2.one,
            indirectData != null ? GetVolumetricDimmer(indirectData) : 1f);

        if (blackSky != null) blackSky.weight = inside ? 1f : 0f;
        if (inside)
        {
            if (directData != null)
            {
                directData.lightDimmer = 0f;
                SetVolumetricDimmer(directData, 0f);
            }
            else if (sunDirect != null)
            {
                sunDirect.enabled = false;
            }

            if (indirectData != null)
            {
                indirectData.lightDimmer = 0f;
                SetVolumetricDimmer(indirectData, 0f);
            }
            else if (sunIndirect != null)
            {
                sunIndirect.enabled = false;
            }
        }
        else
        {
            if (sunDirect != null && !sunDirect.enabled) sunDirect.enabled = true;
            if (sunIndirect != null && !sunIndirect.enabled) sunIndirect.enabled = true;
            if (indirectData != null) indirectData.lightDimmer = 1f;
        }
        return true;
    }

    private void RestoreSky(SkyState snapshot)
    {
        if (!snapshot.Valid) return;

        StartOfRound round = StartOfRound.Instance;
        if (round.blackSkyVolume != null)
        {
            round.blackSkyVolume.weight = snapshot.BlackSky;
        }

        TimeOfDay time = TimeOfDay.Instance;
        HDAdditionalLightData? directData = GetSunDirectData(time.sunDirect);
        if (directData != null)
        {
            directData.lightDimmer = snapshot.DirectDimmers.x;
            SetVolumetricDimmer(directData, snapshot.DirectDimmers.y);
        }
        if (time.sunDirect != null && time.sunDirect.enabled != snapshot.SunDirect) time.sunDirect.enabled = snapshot.SunDirect;
        if (time.sunIndirect != null && time.sunIndirect.enabled != snapshot.SunIndirect) time.sunIndirect.enabled = snapshot.SunIndirect;

        if (snapshot.SunDimmer >= 0f)
        {
            HDAdditionalLightData? indirectData = GetSunIndirectData(time.sunIndirect);
            if (indirectData != null)
            {
                indirectData.lightDimmer = snapshot.SunDimmer;
                SetVolumetricDimmer(indirectData, snapshot.IndirectVolumetric);
            }
        }
    }

    private bool _renderFeedFailureLogged;
    private double _lastFeedTime;

    private void ResumeAfterGap(double gap)
    {
        _nvWasDrawing = false;
        _rawAmbientReady = false;
        _rainRampLastTime = -1f;
        if (gap >= Constants.RainFeed.DryTime)
        {
            _rainSim.Reset();
            _rainWetness = 0f;
        }

        Camera? cam = ActiveCamera;
        if (cam != null) HDCamera.GetOrCreate(cam).volumetricHistoryIsValid = false;
    }

    internal void RenderPendingFeed()
    {
        double now = Time.unscaledTimeAsDouble;
        double gap = now - _lastFeedTime;
        if (gap > Math.Max(_gapAllowance, Time.unscaledDeltaTime) * 4d) ResumeAfterGap(gap);
        _lastFeedTime = now;

        try
        {
            bool rendered = RenderFeed();
            FrameCapture? capture = _recorder?.Capture;
            if (rendered && capture != null) capture.OnRendered();
        }
        catch (Exception ex)
        {
            if (_renderFeedFailureLogged) return;
            _renderFeedFailureLogged = true;
            Plugin.Log.LogError($"Failed to render camera feed: {ex}");
        }
    }

    private bool RenderFeed()
    {
        Camera? active = ActiveCamera;
        if (active == null)
        {
            return false;
        }
        if (!SetupCameraTarget())
        {
            return false;
        }
        if (active.enabled) active.enabled = false;
        if (_backCamera != null && _backCamera.enabled) _backCamera.enabled = false;

        Renderer? scanFx = GetScanEffect();
        bool prevScanOff = scanFx != null && scanFx.forceRenderingOff;
        if (scanFx != null) scanFx.forceRenderingOff = true;

        bool hudSuppressed = false;
        bool weatherSuppressed = false;
        bool meteorsSuppressed = false;
        HudFilterState hudSnapshot = default;
        SkyState envSnapshot = default;
        IndoorFogState indoorFogState = default;
        ScanLightState scanLightState = default;
        SelfiePose selfiePose = default;
        Light? playerLight = null;
        try
        {
            selfiePose = MoveToHand();
            hudSuppressed = SuppressHudFilters(out hudSnapshot);
            SuppressSky(out envSnapshot);
            weatherSuppressed = SuppressFeedWeather();
            meteorsSuppressed = SuppressMeteorSprites();
            indoorFogState = SuppressIndoorFog();
            scanLightState = SuppressScanVisionLight();
            ShowHeldWeather();
            HideLensCover();
            SyncVolumeMask(active);
            playerLight = HidePlayerLight();
            active.Render();
        }
        finally
        {
            try { RestoreSky(envSnapshot); }
            finally
            {
                try { RestoreIndoorFog(indoorFogState); }
                finally
                {
                    if (playerLight != null) playerLight.enabled = true;
                    ShowLensCover();
                    RestoreSelfiePose(selfiePose);
                    SetNightVisionLights(false);
                    HideHeldWeather();
                    RestoreScanVisionLight(scanLightState);
                    if (meteorsSuppressed) RestoreMeteorSprites();
                    if (weatherSuppressed) RestoreFeedWeather();
                    if (hudSuppressed) RestoreHudFilters(hudSnapshot);
                    if (scanFx != null) scanFx.forceRenderingOff = prevScanOff;
                }
            }
        }

        DrawFeedOsd();
        _feedFresh = true;
        return true;
    }

    private struct ScanLightState
    {
        public HDAdditionalLightData? Data;
        public Light? Fallback;
        public float Dimmer;
    }

    private static Light? HidePlayerLight()
    {
        GameNetcodeStuff.PlayerControllerB? local = StartOfRound.Instance.localPlayerController;
        Light? light = local != null ? local.nightVision : null;
        if (light == null || !light.enabled) return null;
        light.enabled = false;
        return light;
    }

    private ScanLightState SuppressScanVisionLight()
    {
        ScanLightState state = default;
        Light? light = Compat.BetterScanVisionCompat.GetScanLight();
        if (light == null || !light.enabled) return state;

        var data = light.GetComponent<HDAdditionalLightData>();
        if (data != null)
        {
            float previous = data.lightDimmer;
            if (previous <= 0f) return state;
            state.Data = data;
            state.Dimmer = previous;
            data.lightDimmer = 0f;
            return state;
        }

        state.Fallback = light;
        light.enabled = false;
        return state;
    }

    private void RestoreScanVisionLight(ScanLightState state)
    {
        if (state.Data != null) state.Data.lightDimmer = state.Dimmer;
        else if (state.Fallback != null) state.Fallback.enabled = true;
    }

    private int ExcludeMask
    {
        get
        {
            if (_excludeMaskParsed)
            {
                return _excludeMask;
            }

            _excludeMaskParsed = true;
            string[] names = Constants.Visual.PreviewExcludeLayerNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
            _excludeMask = names.Length > 0 ? LayerMask.GetMask(names) : 0;
            return _excludeMask;
        }
    }

    private void MirrorPlayerCamera(bool forceAll = false)
    {
        if (this == null)
        {
            return;
        }

        Camera? playerCamera = playerHeldBy?.gameplayCamera;
        if (playerCamera == null)
        {
            return;
        }

        FeedRenderQuality desiredQuality = VideoCameraConfig.RenderQuality.Value;
        bool needsReapply = !_frameSettingsCopied || desiredQuality != _appliedFeedQuality;
        if (needsReapply && CopyFrameSettings(_camera))
        {
            CopyFrameSettings(_backCamera);
            _frameSettingsCopied = true;
            _appliedFeedQuality = desiredQuality;
        }

        if (forceAll)
        {
            _lastCullingMask = 0;
        }

        MirrorTo(_camera, playerCamera, ref _lastCullingMask);
    }

    private void MirrorTo(Camera? target, Camera playerCamera, ref int lastMask)
    {
        if (target == null) return;

        int desiredMask = playerCamera.cullingMask & ~ExcludeMask;
        if (desiredMask == 0)
        {
            desiredMask = playerCamera.cullingMask;
        }

        if (_cameraMode.Value && playerHeldBy != null && playerHeldBy.thisPlayerModel != null)
        {
            desiredMask |= 1 << playerHeldBy.thisPlayerModel.gameObject.layer;
        }

        desiredMask |= 1 << Constants.Visual.WeatherCameraLayer;

        if (desiredMask != lastMask)
        {
            target.cullingMask = desiredMask;
            lastMask = desiredMask;
        }
    }

    private bool SetupCameraTarget()
    {
        if (_camera == null)
        {
            return false;
        }

        RenderTexture target = GetFeedTexture();
        if (_camera.targetTexture != target)
        {
            _camera.targetTexture = target;
        }
        return true;
    }

    private HDAdditionalCameraData? _feedHd;

    private void SyncVolumeMask(Camera feed)
    {
        if (!TryGetLocalMask(out int localMask)) return;
        if (_feedHd == null || _feedHd.gameObject != feed.gameObject) _feedHd = feed.GetComponent<HDAdditionalCameraData>();
        if (_feedHd == null) return;

        int want = (localMask & ~_claimedVolumeLayers) | (_nvVolumeLayer >= 0 ? 1 << _nvVolumeLayer : 0);
        if (_feedHd.volumeLayerMask.value != want) _feedHd.volumeLayerMask = want;
    }

    private bool _frameSettingsCopied;
    private FeedRenderQuality _appliedFeedQuality = (FeedRenderQuality)(-1);

    private bool CopyFrameSettings(Camera? feed)
    {
        if (feed == null)
        {
            return false;
        }
        Camera? main = playerHeldBy != null ? playerHeldBy.gameplayCamera : null;
        if (main == null)
        {
            return false;
        }
        var mainHd = main.GetComponent<HDAdditionalCameraData>();
        var feedHd = feed.GetComponent<HDAdditionalCameraData>()
                     ?? feed.gameObject.AddComponent<HDAdditionalCameraData>();
        if (mainHd == null || feedHd == null)
        {
            return false;
        }

        feedHd.volumeLayerMask = mainHd.volumeLayerMask;
        feedHd.volumeAnchorOverride = mainHd.volumeAnchorOverride;
        feedHd.hasPersistentHistory = true;

        feedHd.customRenderingSettings = true;
        if (mainHd.customRenderingSettings)
        {
            feedHd.renderingPathCustomFrameSettings = mainHd.renderingPathCustomFrameSettings;
            feedHd.renderingPathCustomFrameSettingsOverrideMask = mainHd.renderingPathCustomFrameSettingsOverrideMask;
        }
        else
        {
            feedHd.renderingPathCustomFrameSettingsOverrideMask = default;
        }

        ApplyFeedRenderQuality(feedHd);

        DisableFeedFrameField(feedHd, FrameSettingsField.MotionBlur);

        feed.farClipPlane = Constants.Visual.FeedFarClipPlane;
        return true;
    }

    private static void DisableFeedFrameField(HDAdditionalCameraData data, FrameSettingsField field)
    {
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(field, false);
    }

    private static void OverrideFeedFrameField(HDAdditionalCameraData data, FrameSettingsField field)
    {
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
    }

    private static void ApplyFeedRenderQuality(HDAdditionalCameraData feedHd)
    {
        if (VideoCameraConfig.RenderQuality.Value != FeedRenderQuality.Performance)
        {
            return;
        }

        DisableFeedFrameField(feedHd, FrameSettingsField.SSR);
        DisableFeedFrameField(feedHd, FrameSettingsField.Distortion);
        DisableFeedFrameField(feedHd, FrameSettingsField.PaniniProjection);
        DisableFeedFrameField(feedHd, FrameSettingsField.SSAO);
        DisableFeedFrameField(feedHd, FrameSettingsField.ContactShadows);
        DisableFeedFrameField(feedHd, FrameSettingsField.ShadowMaps);
        DisableFeedFrameField(feedHd, FrameSettingsField.SubsurfaceScattering);
        DisableFeedFrameField(feedHd, FrameSettingsField.Volumetrics);
        DisableFeedFrameField(feedHd, FrameSettingsField.ReprojectionForVolumetrics);
        DisableFeedFrameField(feedHd, FrameSettingsField.Bloom);

        OverrideFeedFrameField(feedHd, FrameSettingsField.MaximumLODLevelMode);
        OverrideFeedFrameField(feedHd, FrameSettingsField.MaximumLODLevel);
        feedHd.renderingPathCustomFrameSettings.maximumLODLevelMode = MaximumLODLevelMode.OverrideQualitySettings;
        feedHd.renderingPathCustomFrameSettings.maximumLODLevel = 2;

        OverrideFeedFrameField(feedHd, FrameSettingsField.MaterialQualityLevel);
        feedHd.renderingPathCustomFrameSettings.materialQuality = UnityEngine.Rendering.MaterialQuality.Low;

        OverrideFeedFrameField(feedHd, FrameSettingsField.MSAAMode);
        feedHd.renderingPathCustomFrameSettings.msaaMode = MSAAMode.None;
    }

    private void AlignBackCamera()
    {
        if (_camera == null || !_cameraMode.Value || _backCameraAnchor == null)
        {
            return;
        }

        if (_camera.transform.parent != _backCameraAnchor)
        {
            return;
        }

        _cameraRestLocalRot = Quaternion.Inverse(_backCameraAnchor.rotation) * transform.rotation * Quaternion.Euler(0f, 180f, 0f);
    }

    private void ApplyCameraFlip()
    {
        if (_camera == null || !_cameraAnchorsCaptured)
        {
            return;
        }

        if (_cameraMode.Value)
        {
            if (_backCameraAnchor == null)
            {
                return;
            }

            if (_camera.transform.parent != _backCameraAnchor)
            {
                _camera.transform.SetParent(_backCameraAnchor, false);
            }

            _camera.transform.SetPositionAndRotation(
                _backCameraAnchor.position, transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            _camera.transform.localScale = _frontCameraLocalScale;
            _cameraRestLocalRot = _camera.transform.localRotation;
        }
        else
        {
            if (_camera.transform.parent != _frontCameraParent)
            {
                _camera.transform.SetParent(_frontCameraParent, false);
            }

            _camera.transform.localPosition = _frontCameraLocalPos;
            _camera.transform.localRotation = _frontCameraLocalRot;
            _camera.transform.localScale = _frontCameraLocalScale;
            _cameraRestLocalRot = _frontCameraLocalRot;
        }

        _cameraRestCaptured = true;
    }

    private void ApplyRemoteFeedViewpoint()
    {
        var local = StartOfRound.Instance.localPlayerController;
        if (_camera == null || playerHeldBy == null || _cameraMode.Value || playerHeldBy == local)
        {
            return;
        }

        Camera? eye = playerHeldBy.gameplayCamera;
        if (eye == null)
        {
            return;
        }

        Transform eyeTransform = eye.transform;
        _camera.transform.SetPositionAndRotation(GetLensPosition(eyeTransform), eyeTransform.rotation);
    }

    private Vector3 GetLensPosition(Transform eyeTransform)
    {
        Vector3 fallback = eyeTransform.position
                           + (eyeTransform.forward * Constants.Visual.RemoteLensForward)
                           + (eyeTransform.right * Constants.Visual.RemoteLensRight)
                           - (eyeTransform.up * Constants.Visual.RemoteLensDown);

        Transform? socket = playerHeldBy.localItemHolder;
        if (socket == null || itemProperties == null) return fallback;
        if (!IsHoldPoseReady(playerHeldBy)) return fallback;

        Quaternion lensRotation = socket.rotation * Quaternion.Euler(itemProperties.rotationOffset);
        Vector3 lens = socket.position
                       + (socket.rotation * itemProperties.positionOffset)
                       + (lensRotation * _frontCameraLocalPos);

        float reach = Constants.Visual.RemoteLensReach;
        return (lens - eyeTransform.position).sqrMagnitude > reach * reach ? fallback : lens;
    }

    private static bool IsHoldPoseReady(GameNetcodeStuff.PlayerControllerB holder)
    {
        Animator animator = holder.playerBodyAnimator;
        if (animator == null || !holder.isHoldingObject) return true;
        int layer = animator.GetLayerIndex(holder.twoHandedAnimation ? "HoldingItemsBothHands" : "HoldingItemsRightHand");
        return layer < 0 || animator.GetCurrentAnimatorClipInfoCount(layer) > 0;
    }

    private Camera? ActiveCamera => _camera;

    private void DisableCameras()
    {
        if (_camera != null) _camera.enabled = false;
        if (_backCamera != null) _backCamera.enabled = false;
    }

    private RenderTexture GetFeedTexture()
    {
        if (_feedTexture != null)
        {
            return _feedTexture;
        }

        (int width, int height) = VideoCameraConfig.GetRenderSize();
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);
        FilterMode filter = VideoCameraConfig.RenderFilterMode.Value switch
        {
            CameraFilterMode.Bilinear => FilterMode.Bilinear,
            _ => FilterMode.Point,
        };

        _feedTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = $"{name}_RuntimeCameraTarget",
            antiAliasing = 1,
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp,
            useDynamicScale = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        _feedTexture.Create();
        return _feedTexture;
    }

    private void ApplyScreenTexture(Texture? texture)
    {
        if (_screenRenderer == null || texture == null)
        {
            return;
        }

        if (_screenMaterial == null)
        {
            _screenMaterial = _screenRenderer.material;
        }

        Material material = _screenMaterial;
        material.mainTexture = texture;

        foreach (string colorMap in new[] { "_BaseColorMap", "_BaseMap", "_MainTex" })
        {
            if (material.HasProperty(colorMap))
            {
                material.SetTexture(colorMap, texture);
            }
        }

        foreach (string emissiveMap in new[] { "_EmissiveColorMap", "_EmissionMap" })
        {
            if (material.HasProperty(emissiveMap))
            {
                material.SetTexture(emissiveMap, texture);
            }
        }

        Vector4 crop = GetScreenCrop(texture);
        foreach (string colorMap in new[] { "_BaseColorMap_ST", "_EmissiveColorMap_ST" })
        {
            if (material.HasProperty(colorMap))
            {
                material.SetVector(colorMap, crop);
            }
        }
    }

    private static Vector4 GetScreenCrop(Texture texture)
    {
        if (texture.width <= 0 || texture.height <= 0) return new Vector4(1f, 1f, 0f, 0f);

        float ratio = (float)texture.width / texture.height / Constants.Camera.ScreenAspect;
        if (ratio > 1f)
        {
            float width = 1f / ratio;
            return new Vector4(width, 1f, (1f - width) * 0.5f, 0f);
        }

        float height = ratio;
        return new Vector4(1f, height, 0f, (1f - height) * 0.5f);
    }

    private void ApplyScreenBaseTint(Color tint)
    {
        if (_screenMaterial == null) return;

        foreach (string property in new[] { "_BaseColor", "_UnlitColor", "_Color" })
        {
            if (_screenMaterial.HasProperty(property))
            {
                _screenMaterial.SetColor(property, tint);
            }
        }
    }

    private void ApplyScreenEmission(float level)
    {
        if (_screenMaterial == null)
        {
            return;
        }

        bool on = level > 0f;
        float intensity = Mathf.Max(0f, VideoCameraConfig.ScreenEmissionIntensity.Value) * Mathf.Max(0f, level);
        Color emission = on ? Color.white * intensity : Color.black;

        if (_screenMaterial.HasProperty("_EmissiveColor"))
        {
            _screenMaterial.SetColor("_EmissiveColor", emission);
        }

        if (_screenMaterial.HasProperty("_EmissionColor"))
        {
            _screenMaterial.SetColor("_EmissionColor", emission);
        }
    }
}
