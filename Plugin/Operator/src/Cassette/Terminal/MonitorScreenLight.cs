using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Operator.Cassette;

internal static class MonitorScreenLight
{
    private static bool _bound;

    private static GameObject? _screenLightGo;
    private static HDAdditionalLightData? _screenHd;
    private static Transform? _anchor;
    private static float _baseIntensity;
    private static float _baseVolumetric;

    private static RenderTexture? _screenRt;
    private static RenderTexture? _probeRt;
    private static bool _readbackPending;
    private static double _nextSampleTime;
    private static float _luma;
    private static float _lumaTarget;
    private static Color _ambient = Color.black;
    private static Color _ambientTarget = Color.black;
    private static Color _backlight = Color.white;
    private static float _noiseSeed;

    internal static void Bind(RenderTexture screenRt, int monitorIndex)
    {
        Release();

        _screenRt = screenRt;
        _luma = _lumaTarget = 0f;
        _ambient = _ambientTarget = Color.black;
        _noiseSeed = Random.value * 1000f;
        _nextSampleTime = 0.0;

        CreateScreenLight(monitorIndex);
        _bound = _screenHd != null;
    }

    private static void CreateScreenLight(int monitorIndex)
    {
        if (!ShipMonitors.TryGetScreenCenter(monitorIndex, out Vector3 screenCenter))
        {
            Plugin.Log.LogWarning($"Could not find screen of monitor {monitorIndex}, it gets no screen light.");
            return;
        }

        Transform? screen = ShipMonitors.GetMonitorTransform(monitorIndex);
        Transform? wall = FindAnchor(screen);
        if (wall == null)
        {
            Plugin.Log.LogWarning("Could not find the monitor wall, monitor gets no screen light.");
            return;
        }

        Vector3 world = wall.TransformPoint(Constants.Monitor.ScreenLightLocalPosition);
        if (Vector3.Distance(world, screenCenter) > Constants.Monitor.ScreenLightMaxOffset)
        {
            Plugin.Log.LogWarning($"Monitor {monitorIndex} is not where screen light was placed, it gets no glow.");
            return;
        }

        GameObject? prefab = AssetsLoader.LoadOptionalAsset<GameObject>(Constants.Monitor.ScreenLightPrefabName);
        if (prefab == null)
        {
            Plugin.Log.LogError("OperatorScreenLight prefab is missing from bundle, monitor gets no glow.");
            return;
        }

        _anchor = wall;
        _screenLightGo = Object.Instantiate(prefab);
        ApplyPose();

        HDAdditionalLightData? hd = _screenLightGo.GetComponent<HDAdditionalLightData>();
        if (hd == null)
        {
            Object.Destroy(_screenLightGo);
            _screenLightGo = null;
            _anchor = null;
            return;
        }

        _baseIntensity = hd.intensity;
        _baseVolumetric = hd.volumetricDimmer;
        hd.intensity = 0f;

        _screenHd = hd;
        _backlight = BakeLightColor(_screenLightGo.GetComponent<Light>());
    }

    private static Transform? FindAnchor(Transform? from)
    {
        for (Transform? t = from; t != null; t = t.parent)
        {
            if (t.name == Constants.Monitor.ScreenLightAnchorName) return t;
        }
        return null;
    }

    private static void ApplyPose()
    {
        if (_screenLightGo == null || _anchor == null) return;
        _screenLightGo.transform.SetPositionAndRotation(
            _anchor.TransformPoint(Constants.Monitor.ScreenLightLocalPosition),
            _anchor.rotation * Constants.Monitor.ScreenLightLocalRotation);
    }

    private static Color BakeLightColor(Light? light)
    {
        if (light == null) return Color.white;

        Color authored = light.color;
        if (light.useColorTemperature)
        {
            Color filter = Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            authored = new Color(authored.r * filter.r, authored.g * filter.g, authored.b * filter.b, 1f);
            light.useColorTemperature = false;
        }

        authored.a = 1f;
        light.color = authored;
        return authored;
    }

    private static Color ToLightTint(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float _);
        Color tint = Color.HSVToRGB(h, Mathf.Min(s, Constants.Monitor.LightMaxSaturation), 1f);
        tint.a = 1f;
        return tint;
    }

    internal static void Update()
    {
        if (!_bound || _screenRt == null) return;

        float gain = VideoCameraConfig.MonitorScreenLightIntensity.Value;
        if (gain <= 0f)
        {
            if (_screenHd != null) _screenHd.intensity = 0f;
            return;
        }

        SampleScreenLuma();

        float k = 1f - Mathf.Exp(-Constants.Monitor.LightResponseSpeed * Mathf.Max(0f, Time.deltaTime));
        _luma = Mathf.Lerp(_luma, _lumaTarget, k);
        _ambient = Color.Lerp(_ambient, _ambientTarget, k);

        float factor = Mathf.Clamp(
            Mathf.Pow(_luma / Constants.Monitor.LightMidLuma, Constants.Monitor.LightContrast),
            Constants.Monitor.LightMin,
            Constants.Monitor.LightMax) * gain;

        UpdateScreenLight(factor);
    }

    private static void UpdateScreenLight(float factor)
    {
        if (_screenHd == null || _screenLightGo == null) return;
        if (_anchor == null)
        {
            DestroyScreenLight();
            _bound = false;
            return;
        }

        ApplyPose();

        float contentWeight = Mathf.Clamp01(_luma / Constants.Monitor.LightDarkLumaThreshold);
        Color tint = Color.Lerp(_backlight, ToLightTint(_ambient), contentWeight);

        float flicker = (Mathf.PerlinNoise(_noiseSeed, Time.time * Constants.Monitor.LightFlickerSpeed) - 0.5f) * 2f;
        float intensity = _baseIntensity * factor
                          * (1f + (flicker * Constants.Monitor.LightFlickerAmount));

        _screenHd.color = tint;
        _screenHd.intensity = intensity;
        _screenHd.volumetricDimmer = Mathf.Clamp01(_baseVolumetric / Mathf.Max(0.01f, factor));
    }

    internal static void Release()
    {
        DestroyScreenLight();

        _screenRt = null;
        _bound = false;
        _luma = _lumaTarget = 0f;
        if (_readbackPending) AsyncGPUReadback.WaitAllRequests();
        _readbackPending = false;

        if (_probeRt != null)
        {
            _probeRt.Release();
            Object.Destroy(_probeRt);
            _probeRt = null;
        }
    }

    private static void DestroyScreenLight()
    {
        _screenHd = null;
        _anchor = null;
        if (_screenLightGo == null) return;
        Object.Destroy(_screenLightGo);
        _screenLightGo = null;
    }

    private static void SampleScreenLuma()
    {
        if (_screenRt == null || !SystemInfo.supportsAsyncGPUReadback) return;

        double now = Time.unscaledTimeAsDouble;
        if (now < _nextSampleTime) return;
        _nextSampleTime = now + Constants.Monitor.LightSampleInterval;

        int size = Constants.Monitor.LightProbeSize;
        if (_probeRt == null || !_probeRt.IsCreated())
        {
            _probeRt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                name = "OperatorMonitorProbe",
                useMipMap = true,
                autoGenerateMips = false,
            };
            _probeRt.Create();
        }
        if (_readbackPending) return;

        Graphics.Blit(_screenRt, _probeRt);
        _probeRt.GenerateMips();
        _readbackPending = true;
        AsyncGPUReadback.Request(_probeRt, _probeRt.mipmapCount - 1, TextureFormat.RGBA32, OnLumaReadback);
    }

    private static void OnLumaReadback(AsyncGPUReadbackRequest req)
    {
        _readbackPending = false;
        if (req.hasError) return;

        var data = req.GetData<Color32>();
        int n = data.Length;
        if (n == 0) return;

        long r = 0, g = 0, b = 0;
        for (int i = 0; i < n; i++)
        {
            r += data[i].r;
            g += data[i].g;
            b += data[i].b;
        }

        Color mean = new(r / (255f * n), g / (255f * n), b / (255f * n), 1f);
        _ambientTarget = mean;
        _lumaTarget = (0.2126f * mean.r) + (0.7152f * mean.g) + (0.0722f * mean.b);
    }
}
