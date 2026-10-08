using System;
using GameNetcodeStuff;
using UnityEngine;
using Operator.Cassette;
using Operator.Recording;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void TryStartRecording(bool continuation = false)
    {
        if (playerHeldBy == null || !playerHeldBy.IsOwner)
        {
            return;
        }

        if (!VideoCameraConfig.RecordingEnabled.Value)
        {
            ShowRecordingBlockTip(RecordingBlock.Disabled);
            AbandonFailedStart(true);
            return;
        }

        if (_camera == null || _camera.targetTexture == null)
        {
            ShowRecordingBlockTip(RecordingBlock.NoCamera);
            AbandonFailedStart(true);
            return;
        }

        if (!FfmpegLocator.IsReady)
        {
            Plugin.Log.LogWarning("Cannot start recording, ffmpeg is not ready.");
            ShowRecordingBlockTip(FfmpegLocator.GaveUp ? RecordingBlock.Unavailable : RecordingBlock.Preparing);
            AbandonFailedStart(true);
            return;
        }

        if (_recorder != null && _recorder.IsActive)
        {
            _recorder.SetCaptureEnabled(true);
            ReportSessionOpen(true);
            PlayRecordingCueClip(Constants.Assets.RecordingResumeClipName);
            if (!_serverHasPendingCassette)
            {
                ApplyWalletDecor(false, string.Empty, 0);
            }
            return;
        }

        if (_broadcaster == null)
        {
            _broadcaster = ClipBroadcaster.Instance;
        }

        _shadowAudio.Begin();
        _shadowVoices.Begin();
        _recorder = new Recorder { ShadowPool = _shadowAudio.Pool };
        _recorder.SegmentStartRealtime = Time.realtimeSinceStartup;
        _audioDuckGain = GetBleedGain();
        _recorder.InitialDuckGain = _audioDuckGain;
        _recorder.InitialShadowWeight = 1f;
        _recorder.InitialRoom = IsInside;
        _recorder.InitialSubmerged = IsUnderwater;
        _recorder.OnRecordingFinalized += OnRecordingFinalized;
        _recorder.OnStoppedByWatchdog += OnWatchdogStop;
        _recorder.OnEncoderFaulted += OnEncoderFault;
        if (!_recorder.Start(_camera, _broadcaster))
        {
            _recorder.OnRecordingFinalized -= OnRecordingFinalized;
            _recorder.OnStoppedByWatchdog -= OnWatchdogStop;
            _recorder.OnEncoderFaulted -= OnEncoderFault;
            _recorder = null;
            ReleaseShadowAudio();
            ShowRecordingBlockTip(RecordingBlock.NoCamera);
            AbandonFailedStart(true);
            return;
        }

        ResetRecordingBlockTip();
        ReportSessionOpen(true);
        if (!continuation)
        {
            PlayRecordingCueClip(Constants.Assets.RecordingStartClipName);
        }
        _bountyTracker ??= new EnemyBountyTracker(_camera);
        _recordingStartTime = DateTime.Now;
    }

    private void AbandonFailedStart(bool reportFailure)
    {
        if (reportFailure && _recorder == null)
        {
            ReportRecordingFailure(false);
        }
        RequestStopRecording();
        ReportSessionOpen(false);
    }

    private void StartDeadManSession(DeathRecordingBehavior mode)
    {
        if (_recorder != null && _recorder.IsActive) return;

        if (!VideoCameraConfig.RecordingEnabled.Value)
        {
            AbandonFailedStart(false);
            return;
        }

        if (_camera == null || _camera.targetTexture == null)
        {
            AbandonFailedStart(false);
            return;
        }

        if (!FfmpegLocator.IsReady)
        {
            AbandonFailedStart(false);
            return;
        }

        _broadcaster ??= ClipBroadcaster.Instance;
        Recorder.DeadManMode deadManMode = mode == DeathRecordingBehavior.DeadManRoll
            ? Recorder.DeadManMode.Static
            : Recorder.DeadManMode.Live;

        _shadowAudio.Begin();
        _shadowVoices.Begin();
        _recorder = new Recorder { ShadowPool = _shadowAudio.Pool };
        _recorder.SegmentStartRealtime = Time.realtimeSinceStartup;
        _audioDuckGain = GetBleedGain();
        _recorder.InitialDuckGain = _audioDuckGain;
        _recorder.InitialShadowWeight = 1f;
        _recorder.InitialRoom = IsInside;
        _recorder.InitialSubmerged = IsUnderwater;
        _recorder.OnRecordingFinalized += OnRecordingFinalized;
        _recorder.OnStoppedByWatchdog += OnWatchdogStop;
        _recorder.OnEncoderFaulted += OnEncoderFault;
        if (!_recorder.Start(_camera, _broadcaster, deadManMode))
        {
            _recorder.OnRecordingFinalized -= OnRecordingFinalized;
            _recorder.OnStoppedByWatchdog -= OnWatchdogStop;
            _recorder.OnEncoderFaulted -= OnEncoderFault;
            _recorder = null;
            ReleaseShadowAudio();
            AbandonFailedStart(false);
            return;
        }

        _deadManActive = true;
        ReportSessionOpen(true);
        _bountyTracker ??= new EnemyBountyTracker(_camera);
    }

    private void PauseRecording()
    {
        if (_recorder == null || !_recorder.IsActive)
        {
            return;
        }

        _recorder.SetCaptureEnabled(false);
        PlayRecordingCueClip(Constants.Assets.RecordingPauseClipName);
        if (!_serverHasPendingCassette)
        {
            ApplyWalletDecor(true, string.Empty, 0);
        }
    }

    private void PlayRecordingCueClip(string clipName)
    {
        AudioClip? clip = AssetsLoader.GetDialogueBleepClip(clipName);
        if (_audioSource == null || clip == null)
        {
            return;
        }

        _audioSource.PlayOneShot(clip);
        try { WalkieTalkie.TransmitOneShotAudio(_audioSource, clip, 1f); }
        catch (Exception ex) { Plugin.Log.LogDebug($"Failed to transmit cue over walkie talkie: {ex.Message}"); }
    }

    private void PauseOnPocket(bool pocketedNow) => PauseWhileStowed(pocketedNow, ref _pausedByPocket);

    private void PauseOnSeat(bool seatedNow) => PauseWhileStowed(seatedNow, ref _pausedBySeat);

    private void PauseWhileStowed(bool stowed, ref bool pausedHere)
    {
        if (playerHeldBy == null || !playerHeldBy.IsOwner)
        {
            return;
        }

        if (stowed)
        {
            if (_recordingState.Value)
            {
                pausedHere = true;
                _pauseOnStop = true;
                RequestStopRecording();
            }
        }
        else if (pausedHere)
        {
            pausedHere = false;
            if (!_recordingState.Value)
            {
                RequestToggleRecording();
            }
        }
    }

    private float _audioDuckGain = 1f;

    private readonly Operator.Recording.ShadowAudio _shadowAudio = new();
    private readonly Operator.Recording.ShadowVoiceChat _shadowVoices = new();

    private float GetBleedGain()
    {
        PlayerControllerB? listener = GetViewer();
        bool together = listener != null && listener.isInsideFactory == IsInside;
        return together ? Constants.Audio.BleedGain : 0f;
    }

    private void UpdateAudioDuckGain()
    {
        if (_recorder == null || _camera == null) return;

        float bleed = GetBleedGain();

        float smoothing = Mathf.Clamp01(Time.deltaTime * Constants.Audio.DuckSmoothing);
        _audioDuckGain = Mathf.Lerp(_audioDuckGain, bleed, smoothing);
        _recorder.SetAudioDuckGain(_audioDuckGain);

        UpdateShadowAudio();
    }

    private void UpdateShadowAudio()
    {
        if (_recorder == null) return;

        if (_camera == null) return;

        const float weight = 1f;
        _recorder.SetAudioShadowWeight(weight);

        PlayerControllerB? observer = GetViewer();
        Vector3 observerPos = observer != null ? observer.transform.position : _camera.transform.position;

        _shadowAudio.SetContext(
            _camera.transform.position,
            _camera.transform.right,
            _camera.transform.up,
            _camera.transform.forward,
            observerPos);
        _shadowAudio.OwnRoot = transform;
        _shadowVoices.Update(_camera.transform, IsInside);
        _recorder.SetAudioRoom(IsInside);
        _recorder.SetAudioSubmerged(IsUnderwater);
        _recorder.SetAudioHeld(IsHeldLocally());
        _recorder.UpdateMic();
        _shadowAudio.Update(
            _camera.transform,
            observerPos,
            weight,
            IsInside,
            observer != null && observer.isInsideFactory);
    }

    private void ReleaseShadowAudio()
    {
        _shadowAudio.Release();
        _shadowVoices.Release();
        _recorder?.SetAudioShadowWeight(0f);
    }

    private PlayerControllerB? GetViewer()
    {
        PlayerControllerB? local = GameNetworkManager.Instance != null
            ? GameNetworkManager.Instance.localPlayerController
            : null;
        if (local == null) return null;
        return local.isPlayerDead ? local.spectatedPlayerScript : local;
    }

    private bool IsHeldLocally()
    {
        PlayerControllerB? local = GameNetworkManager.Instance != null
            ? GameNetworkManager.Instance.localPlayerController
            : null;
        return playerHeldBy != null && playerHeldBy == local;
    }

    private void OnDropped(bool holderDied)
    {
        if (_recorder == null || !_recorder.IsActive)
        {
            return;
        }

        if (holderDied)
        {
            _recorder.MarkHandoff();
            TryStopRecording(handoff: true);

            if (!HasUsableBattery())
            {
                if (_recordingState.Value) RequestStopRecording();
                return;
            }

            StartDeadManSession(VideoCameraConfig.DeathBehavior.Value);
            return;
        }
    }

    private void FinalizeHandoffSession()
    {
        if (_recorder == null || !_recorder.IsActive)
        {
            return;
        }

        _deadManActive = false;
        _recorder.MarkHandoff();
        TryStopRecording(handoff: true);
    }

    private void FinalizeSession(string reason)
    {
        if (_recorder == null || !_recorder.IsActive)
        {
            return;
        }

        if (_recordingState.Value)
        {
            RequestStopRecording();
        }
        else
        {
            TryStopRecording();
        }
    }

    internal void FinalizeOnDayEnd()
    {
        if (_deadManActive)
        {
            if (_recordingState.Value)
            {
                RequestStopRecording();
            }
            else
            {
                EndDeadManSession();
            }
            return;
        }

        if (_recorder != null && _recorder.IsActive && playerHeldBy == null)
        {
            if (SurvivesEndOfRound())
            {
                return;
            }

            _recorder.MarkHandoff();
            FinalizeSession("day-end");
        }
    }

    private bool SurvivesEndOfRound()
    {
        if (deactivated) return false;

        StartOfRound round = StartOfRound.Instance;
        if (round != null && round.allPlayersDead && itemProperties != null && itemProperties.isScrap)
        {
            return false;
        }

        return isHeld || isInShipRoom;
    }

    private void EndDeadManSession()
    {
        _deadManActive = false;

        if (_recorder == null || !_recorder.IsActive)
        {
            return;
        }

        var recorder = _recorder;
        StashTracker(recorder);
        ReleaseShadowAudio();
        _recorder = null;
        ReportSessionOpen(false);
        ApplyLedVisuals();
        RefreshTooltips();
        recorder.BeginEndCap();
    }

    private void OnWatchdogStop(Recorder recorder)
    {
        if (_recorder != recorder) return;
        _deadManActive = false;
        FinalizeSession("watchdog");
    }

    private void OnEncoderFault(Recorder recorder)
    {
        if (_recorder != recorder) return;
        _deadManActive = false;
        Plugin.Log.LogError("Encoder faulted so recording is being stopped.");
        FinalizeSession("encoder-fault");
    }

    private void StashTracker(Recorder recorder)
    {
        if (_bountyTracker == null) return;

        string fileName = recorder.OutputFileName;
        if (fileName.Length > 0)
        {
            _stoppedTrackers[fileName] = _bountyTracker;
        }
        _bountyTracker = null;
    }

    private void TryStopRecording(bool handoff = false)
    {
        if (_recorder == null)
        {
            return;
        }

        var recorder = _recorder;
        StashTracker(recorder);
        ReleaseShadowAudio();
        _recorder = null;
        ReportSessionOpen(false, handoff);
        ResetRecordingBlockTip();

        ApplyLedVisuals();
        RefreshTooltips();

        _ = recorder.StopAsync();
    }
}
