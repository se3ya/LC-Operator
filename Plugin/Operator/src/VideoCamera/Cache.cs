using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Operator.Compat;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static readonly string[] ButtonObjectNames =
    {
        Constants.Assets.RecordButtonObjectName,
        Constants.Assets.FlipButtonObjectName,
        Constants.Assets.NightVisionButtonObjectName,
        Constants.Assets.ZoomInButtonObjectName,
        Constants.Assets.ZoomOutButtonObjectName,
    };

    private void CacheVisualReferences()
    {
        if (_camera == null)
        {
            Transform? cameraTransform = FindDeepChild(transform, "Camera");
            if (cameraTransform != null)
            {
                _camera = cameraTransform.GetComponent<Camera>();
            }
            _camera ??= GetComponentInChildren<Camera>(true);

            if (_camera != null)
            {
                CullFactoryCompat.ApplyTo(_camera);
            }
        }

        if (!_cameraAnchorsCaptured && _camera != null)
        {
            _frontCameraParent = _camera.transform.parent;
            _frontCameraLocalPos = _camera.transform.localPosition;
            _frontCameraLocalRot = _camera.transform.localRotation;
            _frontCameraLocalScale = _camera.transform.localScale;
            _cameraAnchorsCaptured = true;
            _cameraRestLocalRot = _frontCameraLocalRot;
            _cameraRestCaptured = true;
        }

        if (_screenRenderer == null)
        {
            Transform? screenRendererTransform = FindDeepChild(transform, Constants.Assets.ScreenRendererObjectName);
            if (screenRendererTransform != null)
            {
                _screenRenderer = screenRendererTransform.GetComponent<Renderer>();
            }
            if (_screenRenderer == null)
            {
                foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
                {
                    if (candidate != null && candidate.gameObject != gameObject)
                    {
                        _screenRenderer = candidate;
                        break;
                    }
                }
            }
        }

        if (_walletAnimation == null)
        {
            Transform? wallet = FindDeepChild(transform, Constants.Assets.PocketObjectName);
            if (wallet != null)
            {
                _walletAnimation = wallet.GetComponent<Animation>();
            }
        }

        FindWalletDecor();

        if (_circlesAnimation == null)
        {
            Transform? circles = FindDeepChild(transform, Constants.Assets.FilmObjectName);
            if (circles != null)
            {
                _circlesAnimation = circles.GetComponent<Animation>();
            }
        }

        if (_shellAnimation == null)
        {
            Transform? shell = FindDeepChild(transform, Constants.Assets.ScreenShellObjectName);
            if (shell != null)
            {
                _shellAnimation = shell.GetComponent<Animation>();
            }
        }

        if (_screenHinge == null && _shellAnimation != null)
        {
            _screenHinge = FindDeepChild(_shellAnimation.transform, Constants.Assets.ScreenHingeObjectName);
        }

        for (int i = 0; i < ButtonCount; i++)
        {
            if (_buttonAnimations[i] != null) continue;
            Transform? button = FindDeepChild(transform, ButtonObjectNames[i]);
            if (button != null)
            {
                _buttonAnimations[i] = button.GetComponent<Animation>();
            }
        }

        if (_redLight == null)
        {
            Transform? light = FindDeepChild(transform, Constants.Assets.RedLightObjectName);
            if (light != null)
            {
                _redLight = light.GetComponent<Light>() ?? light.GetComponentInChildren<Light>(true);
            }
        }

        if (!_redLightCaptured && _redLight != null)
        {
            SetupLampLight(_redLight);
            _redLightColor = _redLight.color;
            _redLightUseTemperature = _redLight.useColorTemperature;
            _redLightTemperature = _redLight.colorTemperature;
            _redLightCaptured = true;
        }

        if (_backCameraAnchor == null)
        {
            Transform? webcam = FindDeepChild(transform, Constants.Assets.WebcamObjectName);
            if (webcam != null)
            {
                _webcamAnimation = webcam.GetComponent<Animation>();
                _backCameraAnchor = FindDeepChild(webcam, Constants.Assets.WebcamGlassObjectName) ?? webcam;

                _backCamera = webcam.GetComponent<Camera>() ?? webcam.GetComponentInChildren<Camera>(true);
                if (_backCamera != null)
                {
                    _backCamera.allowHDR = false;
                    _backCamera.allowMSAA = false;
                    _backCamera.allowDynamicResolution = false;
                    _backCamera.useOcclusionCulling = true;
                    _backCamera.enabled = false;
                    CullFactoryCompat.ApplyTo(_backCamera);
                }
            }
        }

        if (_redLightRenderer == null)
        {
            Transform? led1 = FindDeepChild(transform, Constants.Assets.RecordLampObjectName);
            if (led1 != null)
            {
                _redLightRenderer = led1.GetComponent<Renderer>() ?? led1.GetComponentInChildren<Renderer>(true);
            }
        }

        if (_redLight2Renderer == null)
        {
            Transform? led2 = FindDeepChild(transform, Constants.Assets.WebcamLampObjectName);
            if (led2 != null)
            {
                _redLight2Renderer = led2.GetComponent<Renderer>() ?? led2.GetComponentInChildren<Renderer>(true);

                if (_redLight2 == null)
                {
                    Transform? rl2 = FindDeepChild(led2, Constants.Assets.RedLight2ObjectName);
                    if (rl2 != null)
                    {
                        _redLight2 = rl2.GetComponent<Light>() ?? rl2.GetComponentInChildren<Light>(true);
                        if (_redLight2 != null) SetupLampLight(_redLight2);
                    }
                }
            }
        }

        if (_tallyRenderer == null)
        {
            Transform? tally = FindDeepChild(transform, Constants.Assets.TallyLampObjectName);
            if (tally != null)
            {
                _tallyRenderer = tally.GetComponent<Renderer>() ?? tally.GetComponentInChildren<Renderer>(true);
            }
        }

        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>(true);
        }

        SetupScreenGlow();

        ApplyCameraFlip();
    }

    private void SetupScreenGlow()
    {
        if (_screenGlowLight != null)
        {
            return;
        }

        Transform? lightT = FindDeepChild(transform, Constants.Assets.ScreenLightObjectName);
        Light? light = lightT != null ? lightT.GetComponent<Light>() : null;
        if (lightT == null || light == null)
        {
            if (!_screenGlowMissLogged)
            {
                _screenGlowMissLogged = true;
                Plugin.Log.LogDebug($"Screen glow object '{Constants.Assets.ScreenLightObjectName}' is missing, screen bias light is off.");
            }
            return;
        }

        Transform anchor = _screenRenderer != null ? _screenRenderer.transform : transform;
        lightT.SetParent(anchor, false);
        lightT.localPosition = Constants.Visual.ScreenGlowOffset;
        lightT.localRotation = Quaternion.Euler(Constants.Visual.ScreenGlowTurn);
        lightT.localScale = Vector3.one;

        light.type = LightType.Spot;
        light.spotAngle = Constants.Visual.ScreenGlowSpotAngle;
        light.color = Color.white;
        light.useColorTemperature = false;
        light.range = Constants.Visual.ScreenGlowRange;
        light.shadows = LightShadows.None;

        var hd = lightT.GetComponent<HDAdditionalLightData>();
        if (hd != null)
        {
            hd.affectsVolumetric = false;
            hd.range = Constants.Visual.ScreenGlowRange;
            hd.SetSpotAngle(Constants.Visual.ScreenGlowSpotAngle, Constants.Visual.ScreenGlowInnerSpot * 100f);
            hd.enableSpotReflector = true;
            hd.SetIntensity(Constants.Visual.ScreenGlowFloor, LightUnit.Lumen);
        }
        _screenGlowHdLight = hd;

        lightT.gameObject.SetActive(true);
        light.enabled = false;
        _screenGlowLight = light;
    }

    private static void SetupLampLight(Light lamp)
    {
        lamp.useColorTemperature = false;
        lamp.color = Constants.Visual.LampColor;
        lamp.range = Constants.Visual.LampRange;

        var hd = lamp.GetComponent<HDAdditionalLightData>();
        if (hd != null)
        {
            hd.color = Constants.Visual.LampColor;
            hd.range = Constants.Visual.LampRange;
            hd.SetIntensity(Constants.Visual.LampIntensity, LightUnit.Lumen);
        }
        else
        {
            lamp.intensity = Constants.Visual.LampIntensity;
        }
    }

    private void UpdateScreenGlow()
    {
        if (_screenGlowLight == null)
        {
            return;
        }

        SampleFeedAmbient();

        float k = 1f - Mathf.Exp(-Constants.Visual.ScreenGlowResponseSpeed * Mathf.Max(0f, Time.deltaTime));
        _feedAmbient = Color.Lerp(_feedAmbient, _feedAmbientTarget, k);

        float peak = Mathf.Max(_feedAmbient.r, Mathf.Max(_feedAmbient.g, _feedAmbient.b));
        Color tint = Color.white;
        float lumens = Constants.Visual.ScreenGlowFloor;
        if (peak >= Constants.Visual.ScreenGlowDarkPeak)
        {
            tint = new Color(
                Mathf.LinearToGammaSpace(_feedAmbient.r / peak),
                Mathf.LinearToGammaSpace(_feedAmbient.g / peak),
                Mathf.LinearToGammaSpace(_feedAmbient.b / peak),
                1f);
            lumens = Mathf.Max(Constants.Visual.ScreenGlowWhite * peak, Constants.Visual.ScreenGlowFloor);
        }
        if (_cameraMode.Value)
        {
            lumens = Mathf.Min(lumens * Constants.Visual.ScreenGlowSelfieScale, Constants.Visual.ScreenGlowSelfieMax);
        }

        float start = Constants.Visual.ScreenGlowOpenStart;
        float open = Mathf.Clamp01((ScreenOpenAmount - start) / (1f - start));
        lumens *= open * open * (3f - 2f * open);

        _screenGlowLight.color = tint;
        if (_screenGlowHdLight != null)
        {
            _screenGlowHdLight.color = tint;
            _screenGlowHdLight.SetIntensity(lumens, LightUnit.Lumen);
        }
        else
        {
            _screenGlowLight.intensity = lumens;
        }
    }

    private void SampleFeedAmbient()
    {
        if (!_feedFresh || _feedTexture == null || !SystemInfo.supportsAsyncGPUReadback)
        {
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        if (now < _nextAmbientSampleTime)
        {
            return;
        }

        CreateAmbientProbe();
        if (_ambientProbeRt == null || _ambientReadbackPending)
        {
            return;
        }
        _nextAmbientSampleTime = now + Constants.Visual.ScreenGlowSampleInterval;
        _feedFresh = false;

        FilterMode filter = _feedTexture.filterMode;
        _feedTexture.filterMode = FilterMode.Bilinear;
        try
        {
            Graphics.Blit(_feedTexture, _ambientProbeRt);
        }
        finally
        {
            _feedTexture.filterMode = filter;
        }
        _ambientProbeRt.GenerateMips();

        _ambientReadbackPending = true;
        _ambientCallback ??= OnAmbientReadback;
        UnityEngine.Rendering.AsyncGPUReadback.Request(
            _ambientProbeRt, _ambientProbeTopMip, TextureFormat.RGBAFloat, _ambientCallback);
    }

    private void OnAmbientReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
    {
        _ambientReadbackPending = false;
        if (this == null || req.hasError)
        {
            return;
        }

        var data = req.GetData<Color>();
        if (data.Length == 0)
        {
            return;
        }

        _feedAmbientTarget = data[0];
    }

    private void SampleRawFeedAmbient()
    {
        if (_feedTexture == null) return;
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            if (!_readbackUnsupportedLogged)
            {
                _readbackUnsupportedLogged = true;
                Plugin.Log.LogWarning("This GPU does not support async readback. Exposure, focus hunting and glass tint fall back to fixed levels.");
            }
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        float elapsed = _rawAmbientTime > 0d ? (float)(now - _rawAmbientTime) : 0f;
        _rawAmbientTime = now;
        float k = 1f - Mathf.Exp(-Constants.Visual.RawResponseSpeed * Mathf.Max(0f, elapsed));
        _rawAmbient = Color.Lerp(_rawAmbient, _rawAmbientTarget, k);

        if (now < _nextRawSampleTime) return;

        CreateRawProbe();
        if (_rawProbeRt == null || _rawReadbackPending) return;
        _nextRawSampleTime = now + Constants.Visual.RawSampleInterval;

        Graphics.Blit(_feedTexture, _rawProbeRt);
        _rawProbeRt.GenerateMips();

        _rawReadbackPending = true;
        UnityEngine.Rendering.AsyncGPUReadback.Request(
            _rawProbeRt, _rawProbeTopMip, TextureFormat.RGBA32, OnRawReadback);
    }

    private void OnRawReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
    {
        _rawReadbackPending = false;
        if (this == null || req.hasError) return;

        var data = req.GetData<Color32>();
        if (data.Length == 0) return;

        Color32 c = data[0];
        _rawAmbientTarget = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
        if (!_rawAmbientReady)
        {
            _rawAmbient = _rawAmbientTarget;
            _rawAmbientReady = true;
        }
    }

    private void CreateRawProbe()
    {
        if (_rawProbeRt != null) return;

        int size = Mathf.Max(2, Constants.Visual.RawProbeSize);
        _rawProbeRt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = $"{name}_RawProbe",
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _rawProbeRt.Create();
        _rawProbeTopMip = Mathf.RoundToInt(Mathf.Log(size, 2f));
    }

    private void CreateAmbientProbe()
    {
        if (_ambientProbeRt != null)
        {
            return;
        }

        int size = Mathf.Max(2, Constants.Visual.ScreenGlowProbeSize);
        _ambientProbeRt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = $"{name}_ScreenGlowProbe",
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _ambientProbeRt.Create();
        _ambientProbeTopMip = Mathf.RoundToInt(Mathf.Log(size, 2f));
    }
}
