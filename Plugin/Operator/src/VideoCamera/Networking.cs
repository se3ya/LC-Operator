using System;
using Unity.Netcode;
using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void OnZoomFovChanged(float _, float current)
    {
        var local = StartOfRound.Instance.localPlayerController;
        if (playerHeldBy != null && playerHeldBy == local) return;
        ApplyFov(current);
    }

    private void ApplySyncedZoom() => ApplyFov(_zoomFov.Value);

    private void ApplyFov(float fov)
    {
        _currentFov = Mathf.Clamp(
            fov,
            Constants.ConfigRanges.CameraMinFovMin,
            Constants.ConfigRanges.CameraMaxFovMax);
        if (_camera != null) _camera.fieldOfView = _currentFov;
    }

    private void TrySetZoomFov(float fov)
    {
        if (!IsOwner || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        try { _zoomFov.Value = fov; }
        catch (Exception) { }
    }

    private void TrySetOperatorFear(float fear)
    {
        if (!IsOwner || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        try { _operatorFear.Value = fear; }
        catch (Exception) { }
    }

    private static double GetServerTime()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm.ServerTime.TickRate != 0 ? nm.ServerTime.Time : 0.0;
    }

    private double GetTapeTime()
    {
        double stamp = _tapeClockBase.Value;
        if (!_recordingState.Value) return stamp > 0.0 ? stamp : 0.0;
        double elapsed = GetServerTime() - stamp;
        return elapsed > 0.0 ? elapsed : 0.0;
    }

    private void ServerFlipTapeClock()
    {
        double now = GetServerTime();
        if (now <= 0.0) return;
        try { _tapeClockBase.Value = now - _tapeClockBase.Value; }
        catch (Exception) { }
    }

    private void CheckWorthless()
    {
        if (!IsServer || !_serverSessionWorthless) return;
        if (!_recordingState.Value) return;
        if (Operator.Cassette.CompanyMoon.IsFootageWorthless()) return;
        _serverSessionWorthless = false;
    }

    private void ServerResetTapeClock()
    {
        if (!IsServer || !IsSpawned) return;
        if (_recordingState.Value) return;
        if (_tapeClockBase.Value == 0.0) return;
        if (HasStoredTape()) return;
        try { _tapeClockBase.Value = 0.0; }
        catch (Exception) { }
    }

    private void OnCameraModeChanged(bool previous, bool back)
    {
        if (previous == back && _flipApplied)
        {
            return;
        }
        _flipApplied = true;

        CancelWebcamRaise();
        if (_shellOpen)
        {
            PlayWebcamAnimation(back);
        }
        else
        {
            ApplyWebcamPose(false);
        }
        ApplyToggle(CameraButton.Flip, back, animate: true);
        ApplyCameraFlip();
        MirrorPlayerCamera(forceAll: true);
        DisableCameras();
        ApplyLedVisuals();
    }

    private void RequestToggleCameraMode()
    {
        if (!_networkReady) return;
        if (!IsOwner) return;
        TrySetCameraMode(!_cameraMode.Value);
    }

    private void RequestToggleNightVision()
    {
        if (!_networkReady) return;
        if (!_nightVisionOn.Value && !HasUsableBattery()) return;
        if (IsServer) ToggleNightVisionServer();
        else RequestToggleNightVisionServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleNightVisionServerRpc() => ToggleNightVisionServer();

    private void ToggleNightVisionServer()
    {
        if (!IsSpawned || !_networkReady) return;
        TrySetNightVision(!_nightVisionOn.Value);
    }

    private void TrySetNightVision(bool on)
    {
        if (!IsServer || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        try { _nightVisionOn.Value = on; }
        catch (Exception) { }
    }

    private void SyncInside()
    {
        if (!IsOwner || !_networkReady || playerHeldBy == null) return;

        bool inside = playerHeldBy.isInsideFactory;
        if (_cameraInFactory.Value == inside) return;

        try { _cameraInFactory.Value = inside; }
        catch (Exception) { }
    }

    private void OnSessionOpenChanged(bool _, bool __)
    {
        RefreshTapeReels();
        ApplyLedVisuals();
        RefreshTooltips();
    }

    private void OnTapeClockChanged(double _, double __)
    {
        ApplyLedVisuals();
        RefreshTooltips();
    }

    private void ReportSessionOpen(bool open, bool handoff = false)
    {
        if (!_networkReady) return;
        if (IsServer) TrySetSessionOpen(open, handoff, NetworkManager.LocalClientId);
        else ReportSessionOpenServerRpc(open, handoff);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportSessionOpenServerRpc(bool open, bool handoff, ServerRpcParams rpcParams = default) =>
        TrySetSessionOpen(open, handoff, rpcParams.Receive.SenderClientId);

    private void TrySetSessionOpen(bool open, bool handoff = false, ulong? sender = null)
    {
        if (!IsServer || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        if (sender.HasValue)
        {
            if (open) _serverSessionOwner = sender.Value;
            else if (_sessionOpen.Value && sender.Value != _serverSessionOwner) return;
        }
        if (open)
        {
            if (!_serverHasPendingCassette) _serverTakeOpen = true;
            bool continuing = _serverHandoffTime >= 0d
                && Time.realtimeSinceStartupAsDouble - _serverHandoffTime < HandoffWindow;
            if (!continuing) _serverSessionWorthless = true;
            _serverHandoffTime = -1d;
        }
        else if (handoff)
        {
            _serverTakeEnd = Time.realtimeSinceStartup;
            _serverHandoffTime = Time.realtimeSinceStartupAsDouble;
        }
        else
        {
            _serverTakeEnd = Time.realtimeSinceStartup;
            _serverHandoffTime = -1d;
            ServerResetTapeClock();
        }
        try { _sessionOpen.Value = open; }
        catch (Exception) { }
        ServerSyncBlanks();
    }

    private void OnRecordingStateChanged(bool previous, bool current)
    {
        if (current)
        {
            _pauseOnStop = false;
            ResetRecordingBlockTip();
            TryStartRecording();
        }
        else if (_deadManActive)
        {
            _pauseOnStop = false;
            EndDeadManSession();
        }
        else if (_pauseOnStop)
        {
            _pauseOnStop = false;
            PauseRecording();
        }
        else
        {
            TryStopRecording();
        }

        ApplyRecordingVisuals(_recordingState.Value);
        ApplyRecordButton(current, animate: true);
        RefreshTapeReels();
        RefreshTooltips();
    }
}
