using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private Material? _nvMat;
    private bool _nvShaderChecked;
    private RenderTexture? _nvSceneCopy;
    private RenderTexture? _nvHistory;
    private int _nvSceneTopMip;

    private readonly System.Collections.Generic.List<Light> _nvSlabs = new();
    private readonly System.Collections.Generic.List<HDAdditionalLightData?> _nvSlabData = new();
    private Light? _nvNear;
    private HDAdditionalLightData? _nvNearData;
    private bool _nvEmitterBuilt;
    private Light? _suppressedRadarNv;

    private Light? _suppressedVanillaNv;

    private bool _nvOffClickPending;

    private float _nvPhaseTime;
    private bool _nvAnimating;
    private bool _nvTargetPrev;
    private float _nvAmount;
    private float _nvEdge = 1f;
    private float _nvBloom;
    private float _nvShake;

    private float _nvRawSceneLuma;
    private float _nvAdaptedLuma = -1f;
    private bool _nvLumaReadbackPending;
    private bool _nvWasDrawing;
    private GameObject? _nvVolumeGo;
    private Volume? _nvVolume;
    private VolumeProfile? _nvVolumeProfile;
    private UnityEngine.Rendering.HighDefinition.Exposure? _nvExposure;
    private int _nvVolumeLayer = -1;
    private double _nvNextLumaTime;

    private static readonly int NvHistoryTexId = Shader.PropertyToID("_HistoryTex");
    private static readonly int NvTintId = Shader.PropertyToID("_Tint");
    private static readonly int NvChannelHeadroomId = Shader.PropertyToID("_ChannelHeadroom");
    private static readonly int NvDsnuId = Shader.PropertyToID("_Dsnu");
    private static readonly int NvHotPixelRateId = Shader.PropertyToID("_HotPixelRate");
    private static readonly int NvSmearLodId = Shader.PropertyToID("_SmearLod");
    private static readonly int NvDarkLiftId = Shader.PropertyToID("_DarkLift");
    private static readonly int NvToneKneeId = Shader.PropertyToID("_ToneKnee");
    private static readonly int NvToneScaleId = Shader.PropertyToID("_ToneScale");
    private static readonly int NvGrainAmountId = Shader.PropertyToID("_GrainAmount");
    private static readonly int NvNoiseFloorId = Shader.PropertyToID("_NoiseFloor");
    private static readonly int NvHaloStrengthId = Shader.PropertyToID("_HaloStrength");
    private static readonly int NvHaloThresholdId = Shader.PropertyToID("_HaloThreshold");
    private static readonly int NvHaloMipId = Shader.PropertyToID("_HaloMip");
    private static readonly int NvGrainGainId = Shader.PropertyToID("_GrainGain");
    private static readonly int NvSmearThresholdId = Shader.PropertyToID("_SmearThreshold");
    private static readonly int NvSmearStrengthId = Shader.PropertyToID("_SmearStrength");
    private static readonly int NvPersistId = Shader.PropertyToID("_Persist");
    private static readonly int NvAmountId = Shader.PropertyToID("_NVAmount");
    private static readonly int NvBloomEdgeId = Shader.PropertyToID("_BloomEdge");
    private static readonly int NvBloomAmountId = Shader.PropertyToID("_BloomAmount");
    private static readonly int NvBloomStrengthId = Shader.PropertyToID("_BloomStrength");
    private static readonly int NvBandWidthId = Shader.PropertyToID("_BandWidth");
    private static readonly int NvBandDarkId = Shader.PropertyToID("_BandDark");
    private static readonly int NvShakeId = Shader.PropertyToID("_Shake");
    private static readonly int NvSeedId = Shader.PropertyToID("_Seed");

    private void OnNightVisionChanged(bool _, bool on)
    {
        ApplyToggle(CameraButton.NightVision, on, animate: true);
        ApplyIrLamp(on);
        RefreshTooltips();
    }

    private void PlayNvOffClick()
    {
        AudioClip? clip = AssetsLoader.GetNightVisionOffClip();
        if (clip == null) return;

        CreateSfxSources();
        if (_nvClickAudioSource == null) return;

        if (_nvClickAudioSource.clip != clip) _nvClickAudioSource.clip = clip;
        _nvClickAudioSource.volume = Constants.NightVision.OffClickVolume;
        _nvClickAudioSource.time = Mathf.Min(Constants.NightVision.OffClickStart, Mathf.Max(0f, clip.length - 0.01f));
        _nvClickAudioSource.Play();
    }

    private void UpdateNightVisionTransition()
    {
        bool on = _nightVisionOn.Value;
        if (on != _nvTargetPrev)
        {
            _nvTargetPrev = on;
            _nvPhaseTime = 0f;
            _nvAnimating = true;
            _nvOffClickPending = !on;
        }

        float snap = Mathf.Max(0.001f, Constants.NightVision.SwitchTime);
        float hold = Mathf.Max(0f, Constants.NightVision.HoldTime);
        float sweep = Mathf.Max(0.001f, Constants.NightVision.SettleTime);
        float disableDelay = Mathf.Max(0f, Constants.NightVision.DisableDelay);

        if (_nvAnimating)
        {
            _nvPhaseTime += Time.deltaTime;
            float t = _nvPhaseTime;

            if (on)
            {
                _nvAmount = Mathf.Clamp01(t / snap);
                if (t < hold)
                {
                    _nvEdge = 0f;
                    _nvBloom = 1f;
                }
                else
                {
                    float p = Mathf.Clamp01((t - hold) / sweep);
                    _nvEdge = p;
                    _nvBloom = 1f - p;
                    if (p >= 1f) _nvAnimating = false;
                }
            }
            else
            {
                if (t < sweep)
                {
                    _nvAmount = 1f;
                    float p = Mathf.Clamp01(t / sweep);
                    _nvEdge = 1f - p;
                    _nvBloom = p;
                }
                else if (t < sweep + disableDelay)
                {
                    _nvAmount = 1f;
                    _nvEdge = 0f;
                    _nvBloom = 1f;
                }
                else
                {
                    float p = Mathf.Clamp01((t - sweep - disableDelay) / snap);
                    _nvAmount = 1f - p;
                    _nvEdge = 0f;
                    _nvBloom = 1f - p;
                    if (p >= 1f)
                    {
                        _nvAnimating = false;
                        if (_nvOffClickPending)
                        {
                            _nvOffClickPending = false;
                            PlayNvOffClick();
                        }
                    }
                }
            }

            _nvShake = Mathf.Sin(Time.time * Constants.NightVision.ShakeFrequency)
                     * Constants.NightVision.ShakeAmplitude * Mathf.Clamp01(_nvBloom);
        }
        else
        {
            _nvAmount = on ? 1f : 0f;
            _nvEdge = on ? 1f : 0f;
            _nvBloom = 0f;
            _nvShake = 0f;
        }

        UpdateNightVisionGain();
        UpdateNightVisionVolume();
    }

    private void UpdateNightVisionGain()
    {
        if (!Constants.NightVision.AutoGain || _nvAmount <= 0.01f)
        {
            _nvAdaptedLuma = -1f;
            _nvRawSceneLuma = Constants.NightVision.AgcTargetLuma;
            return;
        }

        float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
        float raw = _nvRawSceneLuma;
        if (_nvAdaptedLuma < 0f) _nvAdaptedLuma = raw;

        float tau = raw > _nvAdaptedLuma
            ? Constants.NightVision.AgcAttackTime
            : Constants.NightVision.AgcReleaseTime;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, tau));
        _nvAdaptedLuma = Mathf.Lerp(_nvAdaptedLuma, raw, k);
    }

    private int FindVolumeLayer()
    {
        TryGetLocalMask(out int mainMask);
        int taken = mainMask | _claimedVolumeLayers;
        if ((taken & int.MaxValue) != int.MaxValue)
        {
            taken |= VolumeLayersInUse();
            for (int bit = 30; bit >= 0; bit--)
            {
                if ((taken & (1 << bit)) == 0) return bit;
            }
        }
        Plugin.Log.LogDebug("No free volume layer for night mode, its gain is off.");
        return -1;
    }

    private static bool TryGetLocalMask(out int mask)
    {
        mask = 0;
        var local = GameNetworkManager.Instance.localPlayerController;
        Camera? main = local != null ? local.gameplayCamera : null;
        if (main == null) return false;
        if (main != _maskCamera)
        {
            _maskCamera = main;
            _maskHd = main.GetComponent<HDAdditionalCameraData>();
        }
        if (_maskHd == null) return false;
        mask = _maskHd.volumeLayerMask.value;
        return true;
    }

    private static int VolumeLayersInUse()
    {
        int used = 0;
        foreach (Volume volume in Resources.FindObjectsOfTypeAll<Volume>())
        {
            if (volume == null || !volume.gameObject.scene.IsValid()) continue;
            used |= 1 << volume.gameObject.layer;
        }
        return used;
    }

    private void CheckVolumeLayer()
    {
        if (_nvVolumeLayer < 0 || _nvVolumeGo == null) return;
        if (!TryGetLocalMask(out int localMask)) return;
        if ((localMask & (1 << _nvVolumeLayer)) == 0) return;

        int old = _nvVolumeLayer;
        _claimedVolumeLayers &= ~(1 << old);
        int layer = FindVolumeLayer();
        if (layer < 0)
        {
            ReleaseNightVisionVolume();
            _nvLayerRetryAt = Time.time + Constants.NightVision.LayerRetryInterval;
            return;
        }

        bool on = _nvVolume != null && _nvVolume.enabled;
        if (_nvVolume != null) _nvVolume.enabled = false;
        _nvVolumeGo.layer = layer;
        if (_nvVolume != null) _nvVolume.enabled = on;
        _nvVolumeLayer = layer;
        _claimedVolumeLayers |= 1 << layer;
        Plugin.Log.LogDebug($"Moved night mode volume from layer {old} to {layer}.");
    }

    private void ApplyVolumeLayer()
    {
        if (_nvVolumeLayer < 0 || _camera == null) return;
        var feedHd = _camera.GetComponent<HDAdditionalCameraData>();
        if (feedHd == null) return;

        int mask = feedHd.volumeLayerMask.value;
        int want = mask | (1 << _nvVolumeLayer);
        if (mask != want) feedHd.volumeLayerMask = want;
    }

    private static int _claimedVolumeLayers;
    private static Camera? _maskCamera;
    private static HDAdditionalCameraData? _maskHd;
    private float _nvLayerRetryAt;

    private void CreateNightVisionVolume()
    {
        if (_nvVolume != null) return;
        if (!Constants.NightVision.UseFeedVolume) return;
        if (_camera == null || playerHeldBy == null) return;
        if (Time.time < _nvLayerRetryAt) return;

        int layer = FindVolumeLayer();
        if (layer < 0)
        {
            _nvLayerRetryAt = Time.time + Constants.NightVision.LayerRetryInterval;
            return;
        }

        _nvVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _nvVolumeProfile.name = "VC_NightVisionProfile";
        _nvVolumeProfile.hideFlags = HideFlags.HideAndDontSave;

        var exposure = _nvVolumeProfile.Add<UnityEngine.Rendering.HighDefinition.Exposure>();
        exposure.hideFlags = HideFlags.HideAndDontSave;
        exposure.active = true;
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = Constants.NightVision.FixedExposure;
        exposure.compensation.overrideState = true;
        exposure.compensation.value = 0f;

        _nvVolumeGo = new GameObject("VC_NightVisionVolume") { hideFlags = HideFlags.HideInHierarchy };
        _nvVolumeGo.transform.SetParent(transform, false);
        _nvVolumeGo.layer = layer;

        _nvVolume = _nvVolumeGo.AddComponent<Volume>();
        _nvVolume.isGlobal = true;
        _nvVolume.blendDistance = 0f;
        _nvVolume.priority = Constants.NightVision.VolumePriority;
        _nvVolume.sharedProfile = _nvVolumeProfile;
        _nvVolume.weight = 0f;

        _nvExposure = exposure;
        _nvVolumeLayer = layer;
        _claimedVolumeLayers |= 1 << layer;
        ApplyVolumeLayer();
    }

    private void UpdateNightVisionVolume()
    {
        if (!Constants.NightVision.UseFeedVolume) return;
        CheckVolumeLayer();

        if (_nvAmount <= 0.001f)
        {
            if (_nvVolume != null)
            {
                _nvVolume.weight = 0f;
                _nvVolume.enabled = false;
            }
            return;
        }

        CreateNightVisionVolume();
        if (_nvVolume == null || _nvExposure == null) return;

        ApplyVolumeLayer();

        _nvVolume.enabled = true;
        _nvVolume.weight = 1f;
    }

    private void ReleaseNightVisionVolume()
    {
        if (_nvVolumeLayer >= 0) _claimedVolumeLayers &= ~(1 << _nvVolumeLayer);
        if (_nvVolume != null) { _nvVolume.sharedProfile = null; _nvVolume = null; }
        if (_nvVolumeGo != null) { Destroy(_nvVolumeGo); _nvVolumeGo = null; }
        if (_nvExposure != null) { Destroy(_nvExposure); _nvExposure = null; }
        if (_nvVolumeProfile != null) { Destroy(_nvVolumeProfile); _nvVolumeProfile = null; }
        _nvVolumeLayer = -1;
    }

    private bool LoadNvMaterial()
    {
        if (_nvMat != null) return true;
        if (_nvShaderChecked) return false;
        _nvShaderChecked = true;

        Shader? shader = AssetsLoader.LoadOptionalAsset<Shader>(Constants.Assets.NightVisionShaderName);
        if (shader == null)
        {
            Plugin.Log.LogWarning($"Night vision shader '{Constants.Assets.NightVisionShaderName}' is not found in bundle.");
            return false;
        }
        _nvMat = new Material(shader) { name = "VC_NightVision" };
        return true;
    }

    private Light? CreateFeedLight(string name, HDLightTypeAndShape shape)
    {
        if (_camera == null) return null;

        var go = new GameObject(name) { layer = _camera.gameObject.layer };
        go.transform.SetParent(_camera.transform, false);
        go.AddHDLight(shape);

        Light? light = go.GetComponent<Light>();
        if (light == null) { Destroy(go); return null; }
        light.shadows = LightShadows.None;
        light.useColorTemperature = false;
        light.enabled = false;
        return light;
    }

    private float GetBatteryScale()
    {
        float charge = CurrentCharge();
        float start = Constants.NightVision.LowBatteryDimStart;
        if (charge >= start) return 1f;

        float t = start > 0.0001f ? Mathf.Clamp01(charge / start) : 1f;
        float factor = Mathf.Lerp(Constants.NightVision.LowBatteryFloor, 1f, t);

        float depth = Constants.NightVision.LowBatteryFlicker * (1f - t);
        float flick = Mathf.PerlinNoise(Time.time * 5.5f, 0.37f);
        factor *= 1f - ((1f - flick) * depth);
        return Mathf.Clamp01(factor);
    }

    private static void ConfigureSlabLight(Light light)
    {
        light.type = LightType.Spot;
        light.range = Constants.NightVision.SlabRange;

        var hd = light.GetComponent<HDAdditionalLightData>();
        if (hd == null) return;

        // type and shape setters both rewrite lightUnit, so intensity goes last
        try { hd.type = HDLightType.Spot; } catch { }
        try { hd.spotLightShape = SpotLightShape.Box; } catch { }
        try { hd.shapeWidth = Constants.NightVision.SlabCrossSection; } catch { }
        try { hd.shapeHeight = Constants.NightVision.SlabCrossSection; } catch { }
        try { hd.shapeRadius = 0f; } catch { }
        try { hd.applyRangeAttenuation = true; } catch { }
        try { hd.range = Constants.NightVision.SlabRange; } catch { }
        try { hd.fadeDistance = float.MaxValue; } catch { }
        try { hd.affectsVolumetric = false; } catch { }
        try { hd.affectDiffuse = true; } catch { }
        try { hd.affectSpecular = true; } catch { }
        try { hd.EnableShadows(false); } catch { }
        try { hd.SetIntensity(Constants.NightVision.SlabIntensity, LightUnit.Lux); } catch { }
        try { hd.lightDimmer = 0f; } catch { }
    }

    private static float GetNearIntensity()
    {
        float d = Constants.NightVision.NearLiftDistance;
        float r = Constants.NightVision.NearShapeRadius;
        // slabs are already partway down their own curve at backset, measure against that
        float z = (d + Constants.NightVision.SlabBackset) / Constants.NightVision.SlabRange;
        float window = Mathf.Max(0f, 1f - (z * z * z * z));
        float faceOn = Constants.NightVision.SlabIntensity * Constants.NightVision.SlabFaceOnScale
                     * window * window;
        float lift = Mathf.Pow(2f, Constants.NightVision.NearLift) - 1f;
        return lift * faceOn * ((d * d) + (r * r));
    }

    private static void ConfigureNearLight(Light light)
    {
        light.type = LightType.Point;
        light.range = Constants.NightVision.NearRange;

        var hd = light.GetComponent<HDAdditionalLightData>();
        if (hd == null) return;

        try { hd.type = HDLightType.Point; } catch { }
        try { hd.shapeRadius = Constants.NightVision.NearShapeRadius; } catch { }
        try { hd.applyRangeAttenuation = true; } catch { }
        try { hd.range = Constants.NightVision.NearRange; } catch { }
        try { hd.fadeDistance = float.MaxValue; } catch { }
        try { hd.affectsVolumetric = false; } catch { }
        try { hd.affectDiffuse = true; } catch { }
        try { hd.affectSpecular = true; } catch { }
        try { hd.EnableShadows(false); } catch { }
        try { hd.SetIntensity(GetNearIntensity(), LightUnit.Candela); } catch { }
        try { hd.lightDimmer = 0f; } catch { }
    }

    private void CreateNightVisionEmitter()
    {
        if (_nvEmitterBuilt) return;
        if (_camera == null) return;

        float[] yaws = Constants.NightVision.SlabYaws;
        for (int i = 0; i < yaws.Length; i++)
        {
            Light? slab = CreateFeedLight($"VC_NightVisionSlab{i}", HDLightTypeAndShape.BoxSpot);
            if (slab == null)
            {
                ReleaseNightVisionEmitter();
                _nvEmitterBuilt = true;
                Plugin.Log.LogWarning("Could not build night mode emitter.");
                return;
            }
            ConfigureSlabLight(slab);
            _nvSlabs.Add(slab);
            _nvSlabData.Add(slab.GetComponent<HDAdditionalLightData>());
            slab.gameObject.SetActive(true);
        }

        Light? near = CreateFeedLight("VC_NightVisionNearFill", HDLightTypeAndShape.Point);
        if (near != null)
        {
            ConfigureNearLight(near);
            _nvNear = near;
            _nvNearData = near.GetComponent<HDAdditionalLightData>();
            near.gameObject.SetActive(true);
        }

        _nvEmitterBuilt = true;
    }

    private static Vector3 SlabDirection(Vector3 forward, float yawDegrees, float downY)
    {
        Vector3 flat = new(forward.x, 0f, forward.z);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
        flat = Quaternion.AngleAxis(yawDegrees, Vector3.up) * flat.normalized;
        float h = Mathf.Sqrt(Mathf.Max(0f, 1f - (downY * downY)));
        return ((flat * h) - (Vector3.up * downY)).normalized;
    }

    private void UpdateEmitterPose()
    {
        if (_camera == null) return;

        Transform cam = _camera.transform;
        Vector3 forward = cam.forward;
        Vector3 origin = cam.position;

        float downY = Mathf.Clamp(
            Constants.NightVision.SlabBaseDownY - (forward.y * Constants.NightVision.SlabPitchFollow),
            Constants.NightVision.SlabMinDownY,
            Constants.NightVision.SlabMaxDownY);

        float[] yaws = Constants.NightVision.SlabYaws;
        for (int i = 0; i < _nvSlabs.Count && i < yaws.Length; i++)
        {
            Light slab = _nvSlabs[i];
            if (slab == null) continue;
            Vector3 dir = SlabDirection(forward, yaws[i], downY);
            // slab clips at its own origin plane so it starts behind lens
            slab.transform.SetPositionAndRotation(
                origin - (dir * Constants.NightVision.SlabBackset),
                Quaternion.LookRotation(dir, Vector3.up));
        }

        if (_nvNear != null)
        {
            _nvNear.transform.SetPositionAndRotation(
                cam.TransformPoint(Constants.NightVision.NearOffset), cam.rotation);
        }
    }

    private void SuppressPlayerLights()
    {
        var local = StartOfRound.Instance.localPlayerController;
        if (local == null) return;

        Light? vanilla = local.nightVision;
        if (_suppressedVanillaNv == null && vanilla != null && vanilla.enabled)
        {
            _suppressedVanillaNv = vanilla;
            vanilla.enabled = false;
        }

        Light? radar = local.nightVisionRadar;
        if (_suppressedRadarNv == null && radar != null && radar.enabled)
        {
            _suppressedRadarNv = radar;
            radar.enabled = false;
        }
    }

    private void RestorePlayerLights()
    {
        if (_suppressedVanillaNv != null) { _suppressedVanillaNv.enabled = true; _suppressedVanillaNv = null; }
        if (_suppressedRadarNv != null) { _suppressedRadarNv.enabled = true; _suppressedRadarNv = null; }
    }

    private void ReleaseNightVisionEmitter()
    {
        for (int i = 0; i < _nvSlabs.Count; i++)
        {
            if (_nvSlabs[i] != null) Destroy(_nvSlabs[i].gameObject);
        }
        _nvSlabs.Clear();
        _nvSlabData.Clear();
        if (_nvNear != null) { Destroy(_nvNear.gameObject); _nvNear = null; _nvNearData = null; }
        _nvEmitterBuilt = false;
        RestorePlayerLights();
    }

    private void SetNightVisionLights(bool on)
    {
        if (on)
        {
            if (_nvAmount <= 0.01f) return;

            SuppressPlayerLights();
            CreateNightVisionEmitter();
            UpdateEmitterPose();

            float selfie = _cameraMode.Value ? Constants.NightVision.SelfieGainMultiplier : 1f;
            float scale = Mathf.Clamp01(selfie * _nvAmount * GetBatteryScale());

            for (int i = 0; i < _nvSlabs.Count; i++)
            {
                var data = _nvSlabData[i];
                if (data != null) { try { data.lightDimmer = scale; } catch { } }
                if (_nvSlabs[i] != null) _nvSlabs[i].enabled = true;
            }

            if (_nvNear != null)
            {
                if (_nvNearData != null) { try { _nvNearData.lightDimmer = scale; } catch { } }
                _nvNear.enabled = true;
            }
        }
        else
        {
            for (int i = 0; i < _nvSlabs.Count; i++)
            {
                if (_nvSlabs[i] != null) _nvSlabs[i].enabled = false;
            }
            if (_nvNear != null) _nvNear.enabled = false;
            RestorePlayerLights();
        }
    }

    private void ClearNightVisionHistory()
    {
        if (_nvHistory == null) return;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = _nvHistory;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
    }

    private void DrawNightVision()
    {
        if (_nvAmount <= 0.001f && _nvBloom <= 0.001f)
        {
            _nvWasDrawing = false;
            return;
        }
        if (_feedTexture == null) return;
        if (!LoadNvMaterial() || _nvMat == null) return;

        int w = _feedTexture.width;
        int h = _feedTexture.height;
        CreateNightVisionTextures(w, h);
        if (_nvSceneCopy == null || _nvHistory == null) return;

        bool fresh = !_nvWasDrawing;
        if (fresh)
        {
            ClearNightVisionHistory();
            _nvWasDrawing = true;
        }

        Graphics.Blit(_feedTexture, _nvSceneCopy);
        _nvSceneCopy.GenerateMips();
        RequestNvSceneLuma();

        float gf = Mathf.Max(1f, Constants.NightVision.GrainFps);
        float seed = Mathf.Floor(Time.time * gf) * 1.7f;
        float seed2 = Mathf.Floor(Time.time * gf * 0.5f) * 3.1f;

        _nvMat.SetTexture(NvHistoryTexId, _nvHistory);
        _nvMat.SetColor(NvTintId, Constants.NightVision.Tint);
        _nvMat.SetFloat(NvChannelHeadroomId, Constants.NightVision.ChannelHeadroom);
        _nvMat.SetFloat(NvDsnuId, Constants.NightVision.Dsnu);
        _nvMat.SetFloat(NvHotPixelRateId, Constants.NightVision.HotPixelRate);
        _nvMat.SetFloat(NvSmearLodId, Constants.NightVision.SmearLod);
        _nvMat.SetFloat(NvDarkLiftId, Constants.NightVision.DarkLift);
        _nvMat.SetFloat(NvToneKneeId, Constants.NightVision.ToneKnee);
        _nvMat.SetFloat(NvToneScaleId, Constants.NightVision.ToneScale);
        _nvMat.SetFloat(NvGrainAmountId, Constants.NightVision.GrainAmount);
        _nvMat.SetFloat(NvNoiseFloorId, Constants.NightVision.NoiseFloor);
        _nvMat.SetFloat(NvHaloStrengthId, Constants.NightVision.HaloStrength);
        _nvMat.SetFloat(NvHaloThresholdId, Constants.NightVision.HaloThreshold);
        _nvMat.SetFloat(NvHaloMipId, Constants.NightVision.HaloMip);
        _nvMat.SetFloat(NvSmearThresholdId, Constants.NightVision.SmearThreshold);
        _nvMat.SetFloat(NvSmearStrengthId, Constants.NightVision.SmearStrength);
        // snr goes as sqrt of light landing on tube, so grain has to be driven
        // by what it received not by gain. clamping gain pinned this backwards
        float litFor = _nvAdaptedLuma >= 0f ? _nvAdaptedLuma : Constants.NightVision.AgcTargetLuma;
        float lit = Mathf.Sqrt(Mathf.Clamp01(litFor / Constants.NightVision.AgcTargetLuma));
        _nvMat.SetFloat(NvGrainGainId, Mathf.Lerp(
            Constants.NightVision.GrainGainStraining,
            Constants.NightVision.GrainGainQuiet,
            lit));
        _nvMat.SetFloat(NvPersistId, fresh ? 0f : Constants.NightVision.Persistence);
        _nvMat.SetFloat(NvAmountId, _nvAmount);
        _nvMat.SetFloat(NvBloomEdgeId, _nvEdge);
        _nvMat.SetFloat(NvBloomAmountId, Mathf.Clamp01(_nvBloom));
        _nvMat.SetFloat(NvBloomStrengthId, Constants.NightVision.BloomStrength);
        _nvMat.SetFloat(NvBandWidthId, Constants.NightVision.BandWidth);
        _nvMat.SetFloat(NvBandDarkId, Constants.NightVision.BandDarkness);
        _nvMat.SetFloat(NvShakeId, _nvShake);
        _nvMat.SetFloat(NvSeedId, seed);

        Graphics.Blit(_nvSceneCopy, _feedTexture, _nvMat);
        Graphics.Blit(_feedTexture, _nvHistory);
    }

    private void CreateNightVisionTextures(int w, int h)
    {
        if (_nvSceneCopy == null || _nvSceneCopy.width != w || _nvSceneCopy.height != h)
        {
            if (_nvSceneCopy != null) { _nvSceneCopy.Release(); Destroy(_nvSceneCopy); }
            _nvSceneCopy = new RenderTexture(w, h, 0, _feedTexture!.format)
            {
                name = "VC_NightVisionSceneCopy",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                useMipMap = true,
                autoGenerateMips = false,
            };
            _nvSceneCopy.Create();
            _nvSceneTopMip = Mathf.Max(0, Mathf.FloorToInt(Mathf.Log(Mathf.Max(w, h), 2f)));
        }

        if (_nvHistory == null || _nvHistory.width != w || _nvHistory.height != h)
        {
            if (_nvHistory != null) { _nvHistory.Release(); Destroy(_nvHistory); }
            _nvHistory = new RenderTexture(w, h, 0, _feedTexture!.format)
            {
                name = "VC_NightVisionHistory",
                wrapMode = TextureWrapMode.Clamp,
            };
            _nvHistory.Create();
            ClearNightVisionHistory();
        }
    }

    private void RequestNvSceneLuma()
    {
        if (!Constants.NightVision.AutoGain) return;
        if (!SystemInfo.supportsAsyncGPUReadback || _nvSceneCopy == null) return;
        if (_nvLumaReadbackPending) return;

        double now = Time.unscaledTimeAsDouble;
        if (now < _nvNextLumaTime) return;
        _nvNextLumaTime = now + Constants.NightVision.AgcSampleInterval;

        _nvLumaReadbackPending = true;
        AsyncGPUReadback.Request(_nvSceneCopy, Mathf.Max(0, _nvSceneTopMip - 3), TextureFormat.RGBA32, OnNvLumaReadback);
    }

    private void OnNvLumaReadback(AsyncGPUReadbackRequest req)
    {
        _nvLumaReadbackPending = false;
        if (this == null || req.hasError) return;

        var data = req.GetData<Color32>();
        if (data.Length == 0) return;

        // a mean cannot see one lamp in a dark room, and a real brightness control reacts to brightest thing in frame by pulling whole picture down
        float sum = 0f;
        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            float l = ((0.299f * c.r) + (0.587f * c.g) + (0.114f * c.b)) / 255f;
            sum += l;
            if (l > peak) peak = l;
        }

        float mean = sum / data.Length;
        _nvRawSceneLuma = (mean * Constants.NightVision.AgcMeanWeight)
                        + (peak * (1f - Constants.NightVision.AgcMeanWeight));
    }

    private void ReleaseNightVision()
    {
        if (_nvSceneCopy != null) { _nvSceneCopy.Release(); Destroy(_nvSceneCopy); _nvSceneCopy = null; }
        if (_nvHistory != null) { _nvHistory.Release(); Destroy(_nvHistory); _nvHistory = null; }
        ReleaseNightVisionEmitter();
        ReleaseNightVisionVolume();
    }
}
