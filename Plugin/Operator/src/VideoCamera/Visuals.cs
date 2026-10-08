using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void ApplyRecordingVisuals(bool recording)
    {
        CacheVisualReferences();
        SetupCameraTarget();

        ApplyLedVisuals();

        if (recording)
        {
            PlayCirclesAnimation(true);
        }
        else
        {
            PlayCirclesAnimation(false);
            if (this == null || playerHeldBy == null)
            {
                if (_shellOpen) CloseShell();
            }
        }
    }

    private bool IsPaused()
    {
        return !_recordingState.Value
            && (_sessionOpen.Value || (_recorder != null && _recorder.IsActive) || _tapeClockBase.Value > 0.0);
    }

    private bool CanEject()
    {
        if (_recorder != null && _recorder.IsActive)
        {
            return true;
        }

        return _hasPendingCassette.Value;
    }

    private static Color GetPauseColor(Material? pauseMaterial)
    {
        if (pauseMaterial != null)
        {
            if (pauseMaterial.HasProperty(PauseMaterialBaseColorId))
            {
                return pauseMaterial.GetColor(PauseMaterialBaseColorId);
            }
            if (pauseMaterial.HasProperty(PauseMaterialColorId))
            {
                return pauseMaterial.GetColor(PauseMaterialColorId);
            }
        }
        return DefaultPauseColor;
    }

    private void ApplyLedVisuals()
    {
        if (isPocketed)
        {
            if (_redLight != null) _redLight.enabled = false;
            if (_redLight2 != null) _redLight2.enabled = false;
            Material? blackOff = AssetsLoader.LedBlackLightMaterial;
            if (blackOff != null)
            {
                ApplyLedMaterial(_redLightRenderer, blackOff);
                ApplyLedMaterial(_redLight2Renderer, blackOff);
                ApplyLedMaterial(_tallyRenderer, blackOff);
            }
            return;
        }

        bool recording = _recordingState.Value;
        bool back = _cameraMode.Value;
        bool paused = IsPaused() || (!recording && _hasPendingCassette.Value);
        Material? pauseMaterial = AssetsLoader.LedPauseLightMaterial;
        bool usePauseMaterialLight = paused && pauseMaterial != null;
        Color pauseLightColor = GetPauseColor(pauseMaterial);

        if (_redLight != null)
        {
            _redLight.enabled = recording || usePauseMaterialLight;
            if (_redLightCaptured)
            {
                if (usePauseMaterialLight)
                {
                    _redLight.useColorTemperature = false;
                    _redLight.color = pauseLightColor;
                }
                else
                {
                    _redLight.useColorTemperature = _redLightUseTemperature;
                    _redLight.colorTemperature = _redLightTemperature;
                    _redLight.color = _redLightColor;
                }
            }
        }
        if (_redLight2 != null) _redLight2.enabled = recording && back;

        Material? red = AssetsLoader.LedRedLightMaterial;
        Material? black = AssetsLoader.LedBlackLightMaterial;
        if (red == null || black == null)
        {
            return;
        }

        if (paused)
        {
            Material pause = pauseMaterial ?? black;
            ApplyLedMaterial(_redLightRenderer, pause);
            ApplyLedMaterial(_redLight2Renderer, black);
            ApplyLedMaterial(_tallyRenderer, black);
            return;
        }

        ApplyLedMaterial(_redLightRenderer, recording ? red : black);
        ApplyLedMaterial(_redLight2Renderer, (recording && back) ? red : black);
        ApplyLedMaterial(_tallyRenderer, recording ? red : black);
    }

    private static void ApplyLedMaterial(Renderer? renderer, Material desired)
    {
        if (renderer == null || desired == null) return;
        if (renderer.sharedMaterial == desired) return;
        renderer.material = desired;
    }

    private void RefreshTooltips()
    {
        if (itemProperties == null)
        {
            return;
        }

        bool dead = !HasUsableBattery();
        string[] tooltips = BuildHints(dead, eject: CanEject());
        itemProperties.toolTips = tooltips;

        if (!IsHintHolder() || isPocketed || IsHintBlocked(playerHeldBy)) return;
        HUDManager.Instance?.ChangeControlTipMultiple(tooltips, holdingItem: true, itemProperties);
    }

    private string[] BuildHints(bool dead, bool eject)
    {
        string ejectKey = BindLabel(Plugin.Inputs?.EjectCassette);
        if (dead)
        {
            return new[] { GetRecordHint(dead), eject ? $"Eject : [{ejectKey}]" : string.Empty, string.Empty };
        }

        string nightVisionKey = BindLabel(Plugin.Inputs?.ToggleNightVision);
        return new[]
        {
            GetRecordHint(dead),
            $"Zoom [{BindLabel(Plugin.Inputs?.ZoomOut)}/{BindLabel(Plugin.Inputs?.ZoomIn)}]  Flip [{BindLabel(Plugin.Inputs?.FlipCamera)}]",
            eject ? $"Night Vision [{nightVisionKey}]  Eject [{ejectKey}]" : $"Night Vision : [{nightVisionKey}]",
        };
    }

    private string GetRecordHint(bool dead)
    {
        string key = BindLabel(Plugin.Inputs?.ToggleRecording);
        if (_recordingState.Value) return $"Pause Recording : [{key}]";
        if (dead) return "Battery empty";
        if (_blanksRemaining.Value <= 0 && !_hasPendingCassette.Value) return "No blank cassettes";
        RecordingBlock block = GetRecordingBlock(_camera);
        if (block == RecordingBlock.Disabled || block == RecordingBlock.Unavailable) return "Recorder offline";
        return IsPaused() ? $"Resume Recording : [{key}]" : $"Start Recording : [{key}]";
    }

    private void HideHints()
    {
        HUDManager hud = HUDManager.Instance;
        if (hud == null || itemProperties == null || hud.controlTipLines.Length == 0) return;
        if (hud.controlTipLines[0].text == $"Drop {itemProperties.itemName} : [G]") hud.ClearControlTips();
    }

    private bool IsHintHolder()
    {
        var local = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        return playerHeldBy != null && local != null && playerHeldBy == local;
    }

    private static string BindLabel(InputAction? action)
    {
        if (action == null) return "[unbound]";
        try
        {
            string raw = action.GetBindingDisplayString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return "[unbound]";
            return raw.Split(' ')[0];
        }
        catch (Exception)
        {
            return "[unbound]";
        }
    }

    internal float ScreenOpenAmount =>
        _screenHinge == null
            ? (_shellOpen ? 1f : 0f)
            : Mathf.Clamp01(Quaternion.Angle(Quaternion.identity, _screenHinge.localRotation) / Constants.Camera.ScreenOpenAngle);

    private void OpenShell()
    {
        if (_shellOpen)
        {
            return;
        }
        _shellOpen = true;
        StowWebcam();
        PlayShellAnimation(true);
        ApplyLensCover(true, animate: true);
        RaiseWebcamWithShell();
    }

    private void CloseShell()
    {
        if (!_shellOpen)
        {
            return;
        }
        _shellOpen = false;
        StowWebcam();
        _shellCloseCutTime = Time.unscaledTimeAsDouble
                             + (GetClipLength(_shellAnimation, Constants.Assets.ScreenShellCloseAnimationName)
                             * Constants.Camera.ShellCloseCut);
        PlayShellAnimation(false);
        ApplyLensCover(false, animate: true);
    }

    private void ApplyClosedShellPose()
    {
        _shellOpen = false;
        _shellCloseCutTime = 0.0;
        StowWebcam();
        SampleClipEnd(_shellAnimation, Constants.Assets.ScreenShellCloseAnimationName);
        ApplyLensCover(false, animate: false);

        if (_redLight != null)
        {
            _redLight.enabled = false;
        }
    }

    private void ApplyOpenShellPose()
    {
        _shellOpen = true;
        _shellCloseCutTime = 0.0;
        CancelWebcamRaise();
        SampleClipEnd(_shellAnimation, Constants.Assets.ScreenShellAnimationName);
        ApplyLensCover(true, animate: false);
        ApplyWebcamPose(_cameraMode.Value);
    }

    private void PlayShellAnimation(bool opening)
    {
        PlayClip(_shellAnimation, opening
            ? Constants.Assets.ScreenShellAnimationName
            : Constants.Assets.ScreenShellCloseAnimationName, reverseOfCurrent: true);
    }

    private void PlayCirclesAnimation(bool playing)
    {
        AnimationState? state = _circlesAnimation != null ? _circlesAnimation[Constants.Assets.FilmSpinAnimationName] : null;
        if (state == null) return;

        if (!playing)
        {
            state.speed = 0f;
            return;
        }

        state.speed = 1f;
        if (state.enabled) return;
        state.wrapMode = WrapMode.Loop;
        state.weight = 1f;
        _circlesAnimation!.Play(state.name);
    }

    private void PlayWalletAnimation(bool opening, bool eject = false)
    {
        if (_walletOpen == opening && !eject) return;
        _walletOpen = opening;
        if (opening) _walletOpenedAt = Time.unscaledTimeAsDouble;

        string clipName = opening ? Constants.Assets.PocketOpenAnimationName
            : eject ? Constants.Assets.PocketEjectAnimationName
            : Constants.Assets.PocketCloseAnimationName;
        if (eject)
        {
            CrossFadeClip(_walletAnimation, clipName, Constants.Camera.EjectCrossfade);
            _walletBusyUntil = Time.unscaledTimeAsDouble + GetClipLength(_walletAnimation, clipName);
            StartDecorLift();
            return;
        }
        PlayClip(_walletAnimation, clipName);
    }

    private void StartDecorLift()
    {
        FindWalletDecor();
        if (_walletDecor == null) return;
        _liftStart = Time.unscaledTimeAsDouble;
        _walletDecor.transform.localPosition = _decorBase;
        if (_decorHidden)
        {
            _decorHidden = false;
            UpdateWalletDecorVisibility();
        }
    }

    private void UpdateDecorLift()
    {
        if (_liftStart < 0.0 || _walletDecor == null) return;

        Transform decor = _walletDecor.transform;
        double now = Time.unscaledTimeAsDouble;
        if (now >= _walletBusyUntil)
        {
            _liftStart = -1.0;
            decor.localPosition = _decorBase;
            if (_decorHidden)
            {
                _decorHidden = false;
                UpdateWalletDecorVisibility();
            }
            return;
        }

        float progress = Mathf.Clamp01((float)((now - _liftStart) / Constants.Camera.EjectLiftTime));
        float lift = Constants.Camera.EjectLift * (1f - ((1f - progress) * (1f - progress)));
        Transform? parent = decor.parent;
        Vector3 offset = parent != null ? parent.InverseTransformVector(parent.up * lift) : Vector3.up * lift;
        decor.localPosition = _decorBase + offset;
    }

    internal bool TryGetEjectPose(out Vector3 position, out Quaternion rotation)
    {
        FindWalletDecor();
        if (_walletDecor == null)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        Transform decor = _walletDecor.transform;
        position = decor.position;
        rotation = decor.rotation * Quaternion.Euler(Constants.Camera.DecorTurn);
        return true;
    }

    internal void HideEjectDecor()
    {
        if (_liftStart < 0.0 || _decorHidden) return;
        _decorHidden = true;
        UpdateWalletDecorVisibility();
    }

    private static void CrossFadeClip(Animation? animation, string clipName, float fade)
    {
        AnimationState? state = animation != null ? animation[clipName] : null;
        if (state == null) return;

        state.wrapMode = WrapMode.ClampForever;
        state.speed = 1f;
        state.time = 0f;
        animation!.CrossFade(clipName, fade);
    }

    private void PlayWebcamAnimation(bool back)
    {
        PlayClip(_webcamAnimation, back
            ? Constants.Assets.WebcamOpenAnimationName
            : Constants.Assets.WebcamCloseAnimationName, reverseOfCurrent: true);
    }

    private void ApplyWebcamPose(bool back)
    {
        SampleClipEnd(_webcamAnimation, back
            ? Constants.Assets.WebcamOpenAnimationName
            : Constants.Assets.WebcamCloseAnimationName);
    }

    private void StowWebcam()
    {
        CancelWebcamRaise();
        ApplyWebcamPose(false);
    }

    private void CancelWebcamRaise()
    {
        if (_webcamRaiseCoroutine != null)
        {
            StopCoroutine(_webcamRaiseCoroutine);
            _webcamRaiseCoroutine = null;
        }
    }

    private void RaiseWebcamWithShell()
    {
        CancelWebcamRaise();
        if (!_cameraMode.Value) return;
        _webcamRaiseCoroutine = StartCoroutine(RaiseWebcamAfterShell());
    }

    private IEnumerator RaiseWebcamAfterShell()
    {
        yield return new WaitForSecondsRealtime(
            GetClipLength(_shellAnimation, Constants.Assets.ScreenShellAnimationName));
        _webcamRaiseCoroutine = null;
        if (this != null && _shellOpen && _cameraMode.Value)
        {
            PlayWebcamAnimation(true);
        }
    }

    private void PressButton(CameraButton button)
    {
        if (HasStateClips(button)) return;
        PlayButtonClip(button, Constants.Assets.ButtonPressClipSuffix);
    }

    private void UpdateZoomButtons(int previous, int current)
    {
        if (previous < 0) PlayButtonClip(CameraButton.ZoomIn, Constants.Assets.ButtonUpClipSuffix);
        else if (previous > 0) PlayButtonClip(CameraButton.ZoomOut, Constants.Assets.ButtonUpClipSuffix);

        if (current < 0) PlayButtonClip(CameraButton.ZoomIn, Constants.Assets.ButtonDownClipSuffix);
        else if (current > 0) PlayButtonClip(CameraButton.ZoomOut, Constants.Assets.ButtonDownClipSuffix);
    }

    private void PlayButtonClip(CameraButton button, string suffix)
    {
        Animation? animation = _buttonAnimations[(int)button];
        string? clipName = FindClip(animation, suffix);
        if (clipName != null) PlayClip(animation, clipName);
    }

    private static string? FindClip(Animation? animation, string suffix)
    {
        if (animation == null) return null;
        foreach (AnimationState state in animation)
        {
            if (state.name.EndsWith(suffix, StringComparison.Ordinal)) return state.name;
        }
        return null;
    }

    private static void PlayClip(Animation? animation, string clipName, bool reverseOfCurrent = false)
    {
        AnimationState? state = animation != null ? animation[clipName] : null;
        if (state == null) return;

        Transform driven = animation!.transform;
        bool resume = false;
        Vector3 pose = default;
        Quaternion twist = default;
        Transform[]? parts = null;
        Vector3[]? poses = null;
        Quaternion[]? twists = null;

        if (reverseOfCurrent && state.length > 0f)
        {
            foreach (AnimationState playing in animation)
            {
                if (playing.length <= 0f || !animation.IsPlaying(playing.name)) continue;
                if (playing.time >= playing.length) continue;

                resume = true;
                break;
            }

            if (resume)
            {
                pose = driven.localPosition;
                twist = driven.localRotation;
                parts = animation.GetComponentsInChildren<Transform>(true);
                poses = new Vector3[parts.Length];
                twists = new Quaternion[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    poses[i] = parts[i].localPosition;
                    twists[i] = parts[i].localRotation;
                }
            }
        }

        animation.Stop();
        state.wrapMode = WrapMode.ClampForever;
        state.speed = 1f;
        state.weight = 1f;
        state.time = 0f;
        animation.Play(clipName);
        state.time = resume && parts != null ? MatchClipTime(animation, state, parts, poses!, twists!) : 0f;
    }

    private static float MatchClipTime(Animation animation, AnimationState state, Transform[] parts, Vector3[] poses, Quaternion[] twists)
    {
        state.enabled = true;
        state.weight = 1f;

        float best = 0f;
        float bestError = float.MaxValue;
        float low = 0f;
        float high = state.length;

        for (int pass = 0; pass < Constants.Camera.ClipMatchPasses; pass++)
        {
            for (int i = 0; i <= Constants.Camera.ClipMatchSteps; i++)
            {
                float time = Mathf.Lerp(low, high, (float)i / Constants.Camera.ClipMatchSteps);
                state.time = time;
                animation.Sample();

                float error = 0f;
                for (int p = 0; p < parts.Length; p++)
                {
                    error += (parts[p].localPosition - poses[p]).sqrMagnitude
                             + (Quaternion.Angle(parts[p].localRotation, twists[p]) * Constants.Camera.ClipMatchAngleWeight);
                }
                if (error < bestError)
                {
                    bestError = error;
                    best = time;
                }
            }

            float span = (high - low) / Constants.Camera.ClipMatchSteps;
            low = Mathf.Max(0f, best - span);
            high = Mathf.Min(state.length, best + span);
        }

        state.time = best;
        return best;
    }

    private static void SampleClipEnd(Animation? animation, string clipName)
    {
        AnimationState? state = animation != null ? animation[clipName] : null;
        if (state == null) return;

        animation!.Stop();
        state.wrapMode = WrapMode.ClampForever;
        state.time = state.length;
        state.speed = 0f;
        state.weight = 1f;
        state.enabled = true;
        animation.Sample();
    }

    private static float GetClipLength(Animation? animation, string clipName)
    {
        AnimationState? state = animation != null ? animation[clipName] : null;
        return state != null ? state.length : 0f;
    }

    private ScreenPhase GetScreenPhase()
    {
        InitScreen();

        bool rolling = _recordingState.Value
                       || (_recorder != null && _recorder.IsActive);
        if (rolling) return ScreenPhase.Live;

        return IsPowered && IsPanelLit ? ScreenPhase.Live : ScreenPhase.Off;
    }

    private bool IsPanelLit => _shellOpen || Time.unscaledTimeAsDouble < _shellCloseCutTime;

    private void InitScreen()
    {
        if (_screenInitialized) return;
        if (playerHeldBy == null) return;
        _screenInitialized = true;

        ApplyOpenShellPose();
        _wasHeldLastFrame = true;
        _wasPocketedLastFrame = isPocketed;
    }

    private void ApplyScreenPhase(ScreenPhase phase)
    {
        ScreenPhase previous = _appliedScreenPhase;
        _screenPhase = phase;
        if (phase == previous) return;

        _appliedScreenPhase = phase;

        if (phase == ScreenPhase.Live)
        {
            _feedAmbient = Color.black;
            _feedAmbientTarget = Color.black;
            _feedFresh = false;
            _nextFeedRenderTime = 0.0;
            ResetExposure();
            ShowLiveScreen();
            return;
        }

        ShowClosedScreen();
    }

    private static Texture2D? _darkPanelTexture;

    private static Texture2D DarkPanelTexture
    {
        get
        {
            if (_darkPanelTexture != null) return _darkPanelTexture;
            _darkPanelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "OperatorDarkPanel",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _darkPanelTexture.SetPixel(0, 0, Constants.Camera.ScreenOffAlbedo.gamma);
            _darkPanelTexture.Apply(false, false);
            return _darkPanelTexture;
        }
    }

    private void ShowClosedScreen()
    {
        if (_camera != null) _camera.enabled = false;
        if (_backCamera != null) _backCamera.enabled = false;
        ApplyScreenTexture(DarkPanelTexture);
        ApplyScreenBaseTint(Color.white);
        ApplyScreenEmission(0f);
    }

    private void ShowLiveScreen()
    {
        DisableCameras();

        RenderTexture target = GetFeedTexture();
        if (_camera != null) _camera.targetTexture = target;
        ApplyScreenTexture(target);
        ApplyScreenBaseTint(Constants.Camera.ScreenLitBaseTint);
        ApplyScreenEmission(1f);
    }

    private static Transform? FindDeepChild(Transform parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform? nested = FindDeepChild(child, name);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
