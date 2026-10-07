using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static readonly int ExposureTintId = Shader.PropertyToID("_Color");

    private float _expAdaptedEv;
    private bool _expAdaptedValid;
    private float _expSettle;
    private float _expMultiplier = 1f;
    private Material? _exposureMaterial;
    private RenderTexture? _exposureTemp;

    private static float SrgbToLinear(float c)
    {
        return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private void UpdateExposureAdaptation()
    {
        FeedExposureMode mode = VideoCameraConfig.FeedExposure.Value;
        if (mode == FeedExposureMode.Off || _nvAmount > 0.5f)
        {
            RelaxExposure();
            return;
        }

        float dt = Mathf.Clamp(Time.deltaTime, 0f, Constants.Exposure.MaxDeltaTime);
        if (dt <= 0f) return;

        // luminance has to be weighted in linear and probe reads an sRGB byte
        if (!_rawAmbientReady) return;

        float measured = 0.2126f * SrgbToLinear(_rawAmbient.r)
            + 0.7152f * SrgbToLinear(_rawAmbient.g)
            + 0.0722f * SrgbToLinear(_rawAmbient.b);

        float sceneEv = Mathf.Log(Mathf.Max(measured, Constants.Exposure.EvFloor), 2f);

        if (!_expAdaptedValid)
        {
            _expAdaptedEv = sceneEv;
            _expAdaptedValid = true;
            _expSettle = Constants.Exposure.SettleTime;
        }
        if (_expSettle > 0f)
        {
            _expSettle -= dt;
            _expAdaptedEv = sceneEv;
        }

        bool real = mode == FeedExposureMode.Real;
        bool rising = sceneEv > _expAdaptedEv;
        float tau = real
            ? rising ? Constants.Exposure.RealTauRise : Constants.Exposure.RealTauFall
            : rising ? Constants.Exposure.CasualTauRise : Constants.Exposure.CasualTauFall;
        float deadOver = real ? Constants.Exposure.RealDeadOver : Constants.Exposure.CasualDeadOver;
        float deadUnder = real ? Constants.Exposure.RealDeadUnder : Constants.Exposure.CasualDeadUnder;
        float maxOver = real ? Constants.Exposure.RealMaxOver : Constants.Exposure.CasualMaxOver;
        float maxUnder = real ? Constants.Exposure.RealMaxUnder : Constants.Exposure.CasualMaxUnder;

        float step = (sceneEv - _expAdaptedEv) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, tau)));
        float slew = Constants.Exposure.MaxSlewRate * dt;
        _expAdaptedEv += Mathf.Clamp(step, -slew, slew);

        float errorEv = sceneEv - _expAdaptedEv;
        float e = errorEv > 0f
            ? Mathf.Max(0f, errorEv - deadOver)
            : Mathf.Min(0f, errorEv + deadUnder);

        _expMultiplier = Mathf.Pow(2f, Mathf.Clamp(e, -maxUnder, maxOver));
    }

    private void RelaxExposure()
    {
        _expMultiplier = Mathf.MoveTowards(_expMultiplier, 1f, Constants.Exposure.RelaxSpeed * Mathf.Max(0f, Time.deltaTime));
        _expAdaptedValid = false;
    }

    private void ResetExposure()
    {
        _expMultiplier = 1f;
        _expAdaptedEv = 0f;
        _expAdaptedValid = false;
    }

    private void DrawExposureAdaptation()
    {
        if (_feedTexture == null) return;

        float m = _expMultiplier;
        if (m > 1f - Constants.Exposure.ApplyEpsilon && m < 1f + Constants.Exposure.ApplyEpsilon)
        {
            return;
        }

        CreateExposureResources();
        if (_exposureMaterial == null || _exposureTemp == null)
        {
            return;
        }

        Graphics.Blit(_feedTexture, _exposureTemp);
        // SetColor gamma converts Color typed property which squared swing. Vector write lands on m exactly and an exposure multiply belongs in linear anyway
        _exposureMaterial.SetVector(ExposureTintId, new Vector4(m, m, m, 1f));
        _exposureMaterial.mainTextureOffset = Vector2.zero;
        Graphics.Blit(_exposureTemp, _feedTexture, _exposureMaterial);
    }

    private void CreateExposureResources()
    {
        if (_exposureMaterial == null)
        {
            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (sh != null) _exposureMaterial = new Material(sh) { name = "VideoCamExposure" };
        }

        if (_feedTexture == null) return;
        int w = _feedTexture.width;
        int h = _feedTexture.height;
        if (_exposureTemp != null && (_exposureTemp.width != w || _exposureTemp.height != h))
        {
            _exposureTemp.Release();
            Destroy(_exposureTemp);
            _exposureTemp = null;
        }
        if (_exposureTemp == null)
        {
            _exposureTemp = new RenderTexture(w, h, 0, _feedTexture.format) { name = "VideoCamExposureTemp", filterMode = FilterMode.Bilinear };
            _exposureTemp.Create();
        }
    }

    private void ReleaseExposureResources()
    {
        if (_exposureMaterial != null) { Destroy(_exposureMaterial); _exposureMaterial = null; }
        if (_exposureTemp != null) { _exposureTemp.Release(); Destroy(_exposureTemp); _exposureTemp = null; }
    }
}
