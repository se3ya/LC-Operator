using System;
using UnityEngine;
using Operator.Cassette;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void CreateSfxSources()
    {
        if (_handlingAudioSource == null)
        {
            _handlingAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_handlingAudioSource, loop: true);
        }
        if (_zoomAudioSource == null)
        {
            _zoomAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_zoomAudioSource, loop: false);
        }
        if (_recordingRunAudioSource == null)
        {
            _recordingRunAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_recordingRunAudioSource, loop: false);
        }
        if (_buttonPressAudioSource == null)
        {
            _buttonPressAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_buttonPressAudioSource, loop: false);
        }
        if (_buttonHoldAudioSource == null)
        {
            _buttonHoldAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_buttonHoldAudioSource, loop: false);
        }
        if (_cassetteEjectAudioSource == null)
        {
            _cassetteEjectAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_cassetteEjectAudioSource, loop: false);
        }
        if (_crackAudioSource == null)
        {
            _crackAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_crackAudioSource, loop: false);
        }
        if (_nvClickAudioSource == null)
        {
            _nvClickAudioSource = gameObject.AddComponent<AudioSource>();
            ConfigureSfxSource(_nvClickAudioSource, loop: false);
        }
    }

    private void ConfigureSfxSource(AudioSource src, bool loop)
    {
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 1f;
        src.maxDistance = 18f;
        if (_audioSource != null)
        {
            src.outputAudioMixerGroup = _audioSource.outputAudioMixerGroup;
        }
    }

    private float GetSfxVolume(float ownVolume, float bystanderVolume)
    {
        var sor = StartOfRound.Instance;
        var local = sor != null ? sor.localPlayerController : null;
        bool isLocalHolder = local != null && playerHeldBy == local;
        return isLocalHolder ? ownVolume : bystanderVolume;
    }

    private void UpdateHandlingSfx(bool isEquipped)
    {
        if (!isEquipped)
        {
            if (_handlingAudioSource != null && _handlingAudioSource.isPlaying) _handlingAudioSource.Pause();
            _lastHandlingPosValid = false;
            return;
        }

        CreateSfxSources();
        if (_handlingAudioSource == null) return;

        AudioClip? clip = AssetsLoader.GetCameraHandlingClip();
        if (clip == null) return;

        Vector3 pos = transform.position;
        float speed = 0f;
        if (_lastHandlingPosValid && Time.deltaTime > 0f)
        {
            speed = (pos - _lastHandlingPos).magnitude / Time.deltaTime;
        }
        _lastHandlingPos = pos;
        _lastHandlingPosValid = true;

        bool moving = speed > Constants.Sfx.HandlingMoveSpeedThreshold;
        if (moving)
        {
            if (_handlingAudioSource.clip != clip) _handlingAudioSource.clip = clip;
            _handlingAudioSource.volume = GetSfxVolume(
                Constants.Sfx.HandlingOwnVolume,
                Constants.Sfx.HandlingBystanderVolume);
            if (!_handlingAudioSource.isPlaying) _handlingAudioSource.Play();
        }
        else if (_handlingAudioSource.isPlaying)
        {
            _handlingAudioSource.Pause();
        }
    }

    private void SetZoomSfxDir(int dir)
    {
        if (!IsOwner || !IsSpawned) return;
        if (_zoomDir.Value == dir) return;
        try { _zoomDir.Value = dir; }
        catch (Exception) { }
    }

    private void OnZoomDirChanged(int previous, int current)
    {
        UpdateZoomSfx(current);
        UpdateButtonHoldSfx(previous, current);
        UpdateZoomButtons(previous, current);
    }

    private void UpdateZoomSfx(int desiredDir)
    {
        if (desiredDir == _zoomSfxDir) return;
        _zoomSfxDir = desiredDir;

        CreateSfxSources();
        if (_zoomAudioSource == null) return;

        _zoomAudioSource.Stop();
        if (desiredDir == 0) return;

        AudioClip? clip = desiredDir < 0 ? AssetsLoader.GetZoomClip() : AssetsLoader.GetZoomReversedClip();
        if (clip == null) return;

        _zoomAudioSource.clip = clip;
        _zoomAudioSource.volume = GetSfxVolume(
            Constants.Sfx.ZoomOwnVolume,
            Constants.Sfx.ZoomBystanderVolume);
        _zoomAudioSource.Play();
    }

    private void StopZoomSfx() => SetZoomSfxDir(0);

    private void PlaySpinDown()
    {
        if (_batterySpinDown) return;
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetRecordingRunClip();
        if (_recordingRunAudioSource == null || clip == null) return;

        _recordingRunAudioSource.clip = clip;
        _recordingRunAudioSource.volume = GetSfxVolume(
            Constants.Sfx.RecordingRunOwnVolume,
            Constants.Sfx.RecordingRunBystanderVolume);
        _recordingRunAudioSource.time = Constants.Sfx.RecordingRunStart;
        _recordingRunAudioSource.pitch = 1f;
        if (!_recordingRunAudioSource.isPlaying) _recordingRunAudioSource.Play();

        _batterySpinDown = true;
        StartCoroutine(SpinDownRecordingRun());
    }

    private System.Collections.IEnumerator SpinDownRecordingRun()
    {
        float span = Mathf.Max(0.05f, Constants.Sfx.BatteryDeathSpinTime);
        float startVolume = _recordingRunAudioSource != null ? _recordingRunAudioSource.volume : 0f;
        float elapsed = 0f;

        while (elapsed < span && _recordingRunAudioSource != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / span);
            _recordingRunAudioSource.pitch = Mathf.Lerp(1f, Constants.Sfx.BatteryDeathEndPitch, k);
            _recordingRunAudioSource.volume = startVolume * (1f - k);
            yield return null;
        }

        if (_recordingRunAudioSource != null)
        {
            _recordingRunAudioSource.Stop();
            _recordingRunAudioSource.pitch = 1f;
            _recordingRunAudioSource.volume = startVolume;
        }

        _batterySpinDown = false;
    }

    private void UpdateRecordingRunSfx()
    {
        if (_batterySpinDown) return;

        bool shouldPlay = _networkReady && _recordingState.Value;
        if (!shouldPlay)
        {
            if (_recordingRunAudioSource != null && _recordingRunAudioSource.isPlaying)
            {
                _recordingRunAudioSource.Stop();
            }
            return;
        }

        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetRecordingRunClip();
        if (_recordingRunAudioSource == null || clip == null) return;

        if (_recordingRunAudioSource.clip != clip) _recordingRunAudioSource.clip = clip;
        _recordingRunAudioSource.pitch = 1f;
        _recordingRunAudioSource.volume = GetSfxVolume(
            Constants.Sfx.RecordingRunOwnVolume,
            Constants.Sfx.RecordingRunBystanderVolume);

        if (!_recordingRunAudioSource.isPlaying)
        {
            _recordingRunAudioSource.time = Constants.Sfx.RecordingRunStart;
            _recordingRunAudioSource.Play();
        }
        else if (_recordingRunAudioSource.time >= Constants.Sfx.RecordingRunEnd)
        {
            _recordingRunAudioSource.time = Constants.Sfx.RecordingRunStart;
        }
    }

    private void OnButtonPressPulseChanged(int _, int pulse)
    {
        var button = (CameraButton)(Mathf.Abs(pulse) % ButtonCount);
        PressButton(button);
        if (button is CameraButton.Record or CameraButton.Flip) PlayButtonPressSfx();
    }

    private void PulseButtonPress(CameraButton button)
    {
        if (!IsOwner || !IsSpawned || NetworkObject == null || !NetworkObject.IsSpawned) return;
        try { _buttonPressPulse.Value = (_buttonPressPulse.Value / ButtonCount + 1) * ButtonCount + (int)button; }
        catch (Exception) { }
    }

    private void PlayButtonPressSfx()
    {
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetButtonPressClip();
        if (_buttonPressAudioSource == null || clip == null) return;

        if (_buttonPressAudioSource.clip != clip) _buttonPressAudioSource.clip = clip;
        _buttonPressAudioSource.volume = GetSfxVolume(
            Constants.Sfx.ButtonPressOwnVolume,
            Constants.Sfx.ButtonPressBystanderVolume);
        _buttonPressAudioSource.time = 0f;
        _buttonPressAudioSource.Play();
    }

    private void UpdateButtonHoldSfx(int previous, int current)
    {
        if (previous == 0 && current != 0) TriggerButtonHoldPress();
        else if (previous != 0 && current == 0) TriggerButtonHoldRelease();
    }

    private void TriggerButtonHoldPress()
    {
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetButtonHoldClip();
        if (_buttonHoldAudioSource == null || clip == null) return;

        if (_buttonHoldAudioSource.clip != clip) _buttonHoldAudioSource.clip = clip;
        _buttonHoldAudioSource.volume = GetSfxVolume(
            Constants.Sfx.ButtonHoldOwnVolume,
            Constants.Sfx.ButtonHoldBystanderVolume);
        _buttonHoldAudioSource.time = 0f;
        _buttonHoldAudioSource.Play();
        _buttonHoldPhase = ButtonHoldPhase.Press;
    }

    private void TriggerButtonHoldRelease()
    {
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetButtonHoldClip();
        if (_buttonHoldAudioSource == null || clip == null) return;

        if (_buttonHoldAudioSource.clip != clip) _buttonHoldAudioSource.clip = clip;
        _buttonHoldAudioSource.volume = GetSfxVolume(
            Constants.Sfx.ButtonHoldOwnVolume,
            Constants.Sfx.ButtonHoldBystanderVolume);
        _buttonHoldAudioSource.time = Constants.Sfx.ButtonHoldPressEnd;
        _buttonHoldAudioSource.Play();
        _buttonHoldPhase = ButtonHoldPhase.Release;
    }

    private void PlayCassetteEjectSfx()
    {
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetCassetteEjectClip();
        if (clip != null && _cassetteEjectAudioSource != null)
        {
            if (_cassetteEjectAudioSource.clip != clip) _cassetteEjectAudioSource.clip = clip;
            _cassetteEjectAudioSource.volume = GetSfxVolume(
                Constants.Sfx.CassetteEjectOwnVolume,
                Constants.Sfx.CassetteEjectBystanderVolume);
            _cassetteEjectAudioSource.time = 0f;
            _cassetteEjectAudioSource.Play();
            return;
        }

        AudioClip? fallback = CassetteRegistrar.CassetteItemSO != null ? CassetteRegistrar.CassetteItemSO.dropSFX : null;
        if (fallback == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(fallback);
        else AudioSource.PlayClipAtPoint(fallback, transform.position);
    }

    private void OnCrackStageChanged(int previous, int current)
    {
        if (current > previous) PlayLensBreakSfx();
    }

    private void PlayLensBreakSfx()
    {
        CreateSfxSources();
        AudioClip? clip = AssetsLoader.GetLensBreakClip();
        if (_crackAudioSource == null || clip == null) return;

        _crackAudioSource.volume = GetSfxVolume(
            Constants.Sfx.LensBreakOwnVolume,
            Constants.Sfx.LensBreakBystanderVolume);
        _crackAudioSource.PlayOneShot(clip);
    }

    private void TickSfx()
    {
        if (_buttonPressAudioSource != null && _buttonPressAudioSource.isPlaying
            && _buttonPressAudioSource.time >= Constants.Sfx.ButtonPressEnd)
        {
            _buttonPressAudioSource.Stop();
        }

        if (_cassetteEjectAudioSource != null && _cassetteEjectAudioSource.isPlaying
            && _cassetteEjectAudioSource.time >= Constants.Sfx.CassetteEjectEnd)
        {
            _cassetteEjectAudioSource.Stop();
        }

        if (_buttonHoldAudioSource != null)
        {
            if (_buttonHoldPhase == ButtonHoldPhase.Press
                && _buttonHoldAudioSource.time >= Constants.Sfx.ButtonHoldPressEnd)
            {
                _buttonHoldAudioSource.Pause();
                _buttonHoldPhase = ButtonHoldPhase.HeldPaused;
            }
            else if (_buttonHoldPhase == ButtonHoldPhase.Release
                && _buttonHoldAudioSource.time >= Constants.Sfx.ButtonHoldReleaseEnd)
            {
                _buttonHoldAudioSource.Stop();
                _buttonHoldPhase = ButtonHoldPhase.Idle;
            }
        }
    }
}
