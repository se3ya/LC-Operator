using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;


namespace Operator;

internal sealed partial class VideoCameraItem
{

    private void SubscribeInput()
    {
        if (_inputSubscribed || Plugin.Inputs?.ToggleRecording == null)
        {
            return;
        }

        Plugin.Inputs.ToggleRecording.performed += OnToggleRecordingInput;
        Plugin.Inputs.FlipCamera.performed += OnFlipCameraInput;
        Plugin.Inputs.ToggleNightVision.performed += OnToggleNightVisionInput;
        _inputSubscribed = true;
    }

    private void UnsubscribeInput()
    {
        if (!_inputSubscribed || Plugin.Inputs?.ToggleRecording == null)
        {
            _inputSubscribed = false;
            return;
        }

        Plugin.Inputs.ToggleRecording.performed -= OnToggleRecordingInput;
        Plugin.Inputs.FlipCamera.performed -= OnFlipCameraInput;
        Plugin.Inputs.ToggleNightVision.performed -= OnToggleNightVisionInput;
        _inputSubscribed = false;
    }

    private void SubscribeEjectInput()
    {
        if (_ejectInputSubscribed || Plugin.Inputs?.EjectCassette == null) return;
        Plugin.Inputs.EjectCassette.performed += OnEjectCassetteInput;
        _ejectInputSubscribed = true;
    }

    private void UnsubscribeEjectInput()
    {
        if (!_ejectInputSubscribed || Plugin.Inputs?.EjectCassette == null)
        {
            _ejectInputSubscribed = false;
            return;
        }
        Plugin.Inputs.EjectCassette.performed -= OnEjectCassetteInput;
        _ejectInputSubscribed = false;
    }

    private bool CanUse()
    {
        var sor = StartOfRound.Instance;
        var local = sor != null ? sor.localPlayerController : null;
        if (local == null) return false;
        if (this == null || playerHeldBy != local) return false;
        if (local.isPlayerDead) return false;
        if (local.inTerminalMenu) return false;
        if (local.isTypingChat) return false;
        if (local.inSpecialInteractAnimation) return false;
        if (local.quickMenuManager != null && local.quickMenuManager.isMenuOpen) return false;
        if (local.currentlyHeldObjectServer != this) return false;
        return true;
    }

    private static bool IsHintBlocked(GameNetcodeStuff.PlayerControllerB player)
    {
        return player.inTerminalMenu || player.inSpecialInteractAnimation;
    }

    private void UpdateHints()
    {
        var sor = StartOfRound.Instance;
        if (sor == null || playerHeldBy == null || playerHeldBy != sor.localPlayerController) return;
        bool blocked = IsHintBlocked(playerHeldBy);
        if (blocked == _hintsBlocked) return;
        _hintsBlocked = blocked;
        if (blocked) HideHints();
        else RefreshTooltips();
    }

    private static bool IsInCruiser(GameNetcodeStuff.PlayerControllerB player)
    {
        if (!player.inVehicleAnimation) return false;
        InteractTrigger? seat = player.currentTriggerInAnimationWith;
        return seat != null && seat.GetComponentInParent<VehicleController>() != null;
    }

    private void OnToggleRecordingInput(InputAction.CallbackContext _)
    {
        if (!CanUse()) return;
        PulseButtonPress(CameraButton.Record);
        RequestToggleRecording();
    }

    private void OnFlipCameraInput(InputAction.CallbackContext _)
    {
        if (!CanUse()) return;
        PulseButtonPress(CameraButton.Flip);
        if (!HasUsableBattery()) return;
        RequestToggleCameraMode();
    }

    private void OnToggleNightVisionInput(InputAction.CallbackContext _)
    {
        if (!CanUse()) return;
        PulseButtonPress(CameraButton.NightVision);
        RequestToggleNightVision();
    }

    private void UpdateZoom()
    {
        if (Plugin.Inputs == null) { StopZoomSfx(); return; }
        if (!CanUse() || !HasUsableBattery()) { StopZoomSfx(); return; }

        var active = ActiveCamera;
        if (active == null) { StopZoomSfx(); return; }

        bool zoomingIn = Plugin.Inputs.ZoomIn.IsPressed();
        bool zoomingOut = Plugin.Inputs.ZoomOut.IsPressed();

        float min = VideoCameraConfig.MinFov.Value;
        float max = VideoCameraConfig.MaxFov.Value;
        float speed = VideoCameraConfig.ZoomSpeed.Value;
        if (min > max)
        {
            (min, max) = (max, min);
        }

        int dir = 0;
        if (zoomingIn && !zoomingOut && active.fieldOfView > min + 0.001f) dir = -1;
        else if (zoomingOut && !zoomingIn && active.fieldOfView < max - 0.001f) dir = 1;

        SetZoomSfxDir(dir);
        if (dir == 0) return;

        float delta = dir * speed * Time.deltaTime;
        _currentFov = Mathf.Clamp(active.fieldOfView + delta, min, max);
        active.fieldOfView = _currentFov;
        if (_camera != null) _camera.fieldOfView = _currentFov;
        TrySetZoomFov(_currentFov);
    }

    private void RequestToggleRecording()
    {
        if (!_networkReady)
        {
            return;
        }

        if (!_recordingState.Value && !HasUsableBattery())
        {
            return;
        }

        if (!_recordingState.Value && _blanksRemaining.Value <= 0 && !_hasPendingCassette.Value)
        {
            HUDManager.Instance?.DisplayTip("No cassettes", "You are out of blank cassettes. More arrive next orbit.");
            return;
        }

        if (!_recordingState.Value)
        {
            RecordingBlock block = GetRecordingBlock(_camera);
            if (block != RecordingBlock.None)
            {
                ShowRecordingBlockTip(block);
                return;
            }

            ResetRecordingBlockTip();
        }

        if (_recordingState.Value && _recorder != null && _recorder.IsActive)
        {
            _pauseOnStop = true;
        }

        if (IsServer)
        {
            ToggleRecordingServer();
        }
        else
        {
            RequestToggleRecordingServerRpc();
        }
    }

    private void RequestStopRecording()
    {
        if (!_networkReady)
        {
            return;
        }

        if (IsServer)
        {
            if (_recordingState.Value)
            {
                TrySetRecordingState(false);
            }
        }
        else
        {
            RequestStopRecordingServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleRecordingServerRpc()
    {
        ToggleRecordingServer();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStopRecordingServerRpc()
    {
        if (_recordingState.Value)
        {
            TrySetRecordingState(false);
        }
    }

    private void ToggleRecordingServer()
    {
        if (!IsSpawned || !_networkReady)
        {
            return;
        }

        bool next = !_recordingState.Value;
        if (next && !HasUsableBattery())
        {
            return;
        }
        ServerSyncBlanks();
        if (next && _blanksRemaining.Value <= 0 && !_serverHasPendingCassette)
        {
            return;
        }

        TrySetRecordingState(next);
    }

    private void TrySetRecordingState(bool value)
    {
        if (!IsServer || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        if (_recordingState.Value == value) return;
        ServerFlipTapeClock();
        if (value && !_serverHasPendingCassette) _serverTakeOpen = true;
        try
        {
            _recordingState.Value = value;
        }
        catch (Exception) { }
        ServerSyncBlanks();
    }

    private void TrySetCameraMode(bool back)
    {
        if (!IsOwner || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        try { _cameraMode.Value = back; }
        catch (Exception) { }
    }

}
