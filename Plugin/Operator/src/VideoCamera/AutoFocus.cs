using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private float _afLensDiopter = 1f / 8f;
    private float _afTargetDiopter = 1f / 8f;
    private float _afReactTimer;
    private float _afHuntTime = -1f;
    private float _afHuntAmp;
    private float _afNextIdleHunt = -1f;
    private float _afLastFov = -1f;
    private float _afZoomStableTimer;
    private bool _afZoomDirty;
    private float _afBlur;
    private float _afBreath;
    private float _afLastDiopter;
    private float _afSampledDistance = 60f;
    private readonly float[] _afRayHistory = Filled(15, 60f);
    private readonly float[] _afRays = Filled(5, 60f);
    private readonly float[] _afSorted = new float[5];
    private float _afAreaError;
    private int _afRecentIndex;
    private Material? _focusBlend;
    private bool _focusBlendChecked;

    private void UpdateAutoFocus(bool sampleScene)
    {
        CameraFocusMode focusMode = VideoCameraConfig.FocusMode.Value;
        if (focusMode == CameraFocusMode.Off) { _afBlur = 0f; _afAreaError = 0f; _afBreath = 0f; return; }
        Camera? cam = ActiveCamera;
        if (cam == null) { _afBlur = 0f; _afAreaError = 0f; return; }

        float now = Time.time;
        float dt = Time.deltaTime;

        if (sampleScene)
        {
            _afSampledDistance = SampleSubjectDistance(cam);
        }

        float dist = _afSampledDistance;
        float ideal = 1f / dist;

        float fov = Mathf.Max(1f, cam.fieldOfView);
        float zoom = Mathf.Max(1f, Constants.Camera.DefaultFov / fov);
        float speedCfg = Constants.Focus.AutoFocusSpeed / 6f;

        float huntOffset = 0f;
        if (focusMode == CameraFocusMode.Auto)
        {
            float luma = 0.2126f * _rawAmbient.r + 0.7152f * _rawAmbient.g + 0.0722f * _rawAmbient.b;
            float dark = 1f - Mathf.Clamp01(luma / Constants.Focus.DarkLumaThreshold);
            if (_nvAmount > 0.5f) dark = 0f;

            if (_afLastFov > 0f && Mathf.Abs(fov - _afLastFov) > 0.01f)
            {
                _afZoomDirty = true;
                _afZoomStableTimer = 0f;
            }
            else if (_afZoomDirty)
            {
                _afZoomStableTimer += dt;
                if (_afZoomStableTimer >= Constants.Focus.ZoomSettleTime)
                {
                    _afZoomDirty = false;
                    StartFocusHunt(Constants.Focus.HuntAmplitude * Mathf.Sqrt(zoom));
                }
            }

            if (_afNextIdleHunt < 0f) ScheduleIdleHunt(now, dark);
            if (now >= _afNextIdleHunt)
            {
                StartFocusHunt(Constants.Focus.HuntAmplitude * (0.6f + 0.8f * dark));
                ScheduleIdleHunt(now, dark);
            }

            if (Mathf.Abs(ideal - _afTargetDiopter) > Constants.Focus.RetargetThreshold)
            {
                _afReactTimer += dt;
                if (_afReactTimer >= Constants.Focus.ReactionDelay)
                {
                    _afTargetDiopter = ideal;
                    _afReactTimer = 0f;
                }
            }
            else
            {
                _afTargetDiopter = Mathf.MoveTowards(_afTargetDiopter, ideal, Constants.Focus.DriftSpeed * dt);
                _afReactTimer = 0f;
            }

            float rack = Constants.Focus.RackSpeed * speedCfg
                       * Mathf.Lerp(1f, Constants.Focus.DarkRackMultiplier, dark);
            _afLensDiopter = Mathf.MoveTowards(_afLensDiopter, _afTargetDiopter, rack * dt);

            if (_afHuntTime >= 0f)
            {
                _afHuntTime += dt;
                float duration = Constants.Focus.HuntDuration;
                if (_afHuntTime >= duration)
                {
                    _afHuntTime = -1f;
                }
                else
                {
                    float p = _afHuntTime / duration;
                    float decay = (1f - p) * (1f - p);
                    huntOffset = _afHuntAmp * Mathf.Sin(p * Mathf.PI * 2f * Constants.Focus.HuntCycles) * decay;
                }
            }
        }
        else
        {
            _afTargetDiopter = ideal;
            _afHuntTime = -1f;
            _afReactTimer = 0f;
            _afZoomDirty = false;
            _afNextIdleHunt = -1f;

            if (Plugin.Inputs != null && CanUse())
            {
                float rackDir = 0f;
                if (Plugin.Inputs.FocusNear.IsPressed()) rackDir += 1f;
                if (Plugin.Inputs.FocusFar.IsPressed()) rackDir -= 1f;
                if (rackDir != 0f)
                {
                    _afLensDiopter = Mathf.Clamp(
                        _afLensDiopter + rackDir * Constants.Focus.ManualRackSpeed * speedCfg * dt,
                        1f / Constants.Focus.ManualFarLimit,
                        1f / Constants.Focus.ManualNearLimit);
                }
            }
            else
            {
                _afLensDiopter = ideal;
            }
        }
        _afLastFov = fov;

        float error;
        if (focusMode == CameraFocusMode.Auto)
        {
            float area = 0f;
            for (int i = 0; i < _afRays.Length; i++)
            {
                area += Mathf.Max(0f, Mathf.Abs(_afLensDiopter - 1f / _afRays[i]) - Constants.Focus.RetargetThreshold);
            }
            area /= _afRays.Length;
            float tau = Mathf.Abs(_afLensDiopter - _afTargetDiopter) > 0.001f
                ? Constants.Focus.AreaRackSmoothing
                : Constants.Focus.AreaSmoothing;
            _afAreaError = Mathf.Lerp(_afAreaError, area, 1f - Mathf.Exp(-dt / tau));
            error = _afAreaError + Mathf.Abs(huntOffset);
        }
        else
        {
            _afAreaError = 0f;
            error = Mathf.Abs(_afLensDiopter + huntOffset - _afTargetDiopter);
        }
        float zBlur = Mathf.Min(zoom, Constants.Focus.MaxZoomBlur);
        float blur = Mathf.Clamp01(error * Constants.Focus.BlurPerDiopter * Mathf.Pow(zBlur, Constants.Focus.ZoomBlurPower));
        _afBlur = Mathf.MoveTowards(_afBlur, blur, dt / 0.1f);

        float effDiopter = _afLensDiopter + huntOffset;
        if (dt > 1e-4f)
        {
            float rackVel = (effDiopter - _afLastDiopter) / dt;
            float target = Mathf.Clamp(rackVel * Constants.Focus.BreathScale,
                                       -Constants.Focus.BreathMax, Constants.Focus.BreathMax);
            _afBreath = Mathf.Lerp(_afBreath, target, 1f - Mathf.Exp(-dt * Constants.Focus.BreathResponse));
        }
        _afLastDiopter = effDiopter;
    }

    private float SampleSubjectDistance(Camera cam)
    {
        float o = Constants.Focus.AreaOffset;
        int slot = _afRecentIndex * 5;
        _afRayHistory[slot] = CastSubject(cam, 0.5f, 0.5f);
        _afRayHistory[slot + 1] = CastSubject(cam, 0.5f - o, 0.5f - o);
        _afRayHistory[slot + 2] = CastSubject(cam, 0.5f + o, 0.5f - o);
        _afRayHistory[slot + 3] = CastSubject(cam, 0.5f - o, 0.5f + o);
        _afRayHistory[slot + 4] = CastSubject(cam, 0.5f + o, 0.5f + o);
        _afRecentIndex = (_afRecentIndex + 1) % 3;
        for (int i = 0; i < 5; i++)
        {
            _afRays[i] = Median(_afRayHistory[i], _afRayHistory[5 + i], _afRayHistory[10 + i]);
            _afSorted[i] = _afRays[i];
        }
        System.Array.Sort(_afSorted);
        return _afSorted[2];
    }

    private static float[] Filled(int count, float value)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = value;
        return values;
    }

    private static float CastSubject(Camera cam, float x, float y)
    {
        Vector3 dir = cam.ViewportPointToRay(new Vector3(x, y, 0f)).direction;
        return Physics.Raycast(cam.transform.position, dir, out RaycastHit hit, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0.3f, hit.distance)
            : 60f;
    }

    private static float Median(float a, float b, float c)
    {
        return Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
    }

    private void StartFocusHunt(float amplitude)
    {
        _afHuntTime = 0f;
        _afHuntAmp = amplitude * (Random.value > 0.5f ? 1f : -1f);
    }

    private void ScheduleIdleHunt(float now, float dark)
    {
        float scale = Mathf.Lerp(1f, Constants.Focus.DarkHuntTimeScale, dark);
        _afNextIdleHunt = now + Random.Range(Constants.Focus.MinIdleHuntInterval, Constants.Focus.MaxIdleHuntInterval) * scale;
    }

    private void DrawAutoFocusBlur()
    {
        if (_afBlur <= 0.002f) return;
        RenderTexture? rt = _feedTexture;
        if (rt == null) return;
        if (!LoadVeilMaterial() || _veilMat == null) return;

        CreateFocusBlend();
        int hw = Mathf.Max(2, rt.width / 2), hh = Mathf.Max(2, rt.height / 2);
        RenderTextureFormat format = _focusBlend != null ? RenderTextureFormat.RGB111110Float : rt.format;
        RenderTexture a = FocusTemp(hw, hh, format);
        RenderTexture b = FocusTemp(hw, hh, format);

        float step = _afBlur * Constants.Focus.BlurStepMax;

        Graphics.Blit(rt, a);
        _veilMat.SetVector("_BlurDir", new Vector4(step, 0f, 0f, 0f));
        Graphics.Blit(a, b, _veilMat, 1);
        _veilMat.SetVector("_BlurDir", new Vector4(0f, step, 0f, 0f));
        Graphics.Blit(b, a, _veilMat, 1);
        float fade = Mathf.Clamp01(_afBlur / Constants.Focus.BlurFade);
        if (_focusBlend != null && fade < 1f)
        {
            _focusBlend.color = new Color(1f, 1f, 1f, fade);
            Graphics.Blit(a, rt, _focusBlend);
        }
        else
        {
            Graphics.Blit(a, rt);
        }

        RenderTexture.ReleaseTemporary(a);
        RenderTexture.ReleaseTemporary(b);
    }

    private void CreateFocusBlend()
    {
        if (_focusBlendChecked) return;
        _focusBlendChecked = true;
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)) return;
        Shader? shader = Shader.Find("Sprites/Default");
        if (shader == null) return;
        _focusBlend = new Material(shader) { name = "VideoCamFocusBlend" };
    }

    private static RenderTexture FocusTemp(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, format);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }
}
