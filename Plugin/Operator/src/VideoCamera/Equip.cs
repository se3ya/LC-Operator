using System.Collections;
using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void OnHeldChanged(bool nowHeld)
    {
        if (nowHeld)
        {
            var newHolderLocalPlayer = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
            if (_recorder != null && _recorder.IsActive
                && playerHeldBy != null && playerHeldBy != newHolderLocalPlayer)
            {
                FinalizeHandoffSession();
            }
            _lastHolder = playerHeldBy;

            if (playerHeldBy != null && playerHeldBy == newHolderLocalPlayer
                && _recordingState.Value && _recorder == null)
            {
                TryStartRecording(continuation: true);
            }

            if (Time.unscaledTimeAsDouble >= _walletBusyUntil)
            {
                PlayWalletAnimation(opening: false);
            }
            _isHovered = false;

            ApplyCameraFlip();
            ApplySyncedZoom();
            MirrorPlayerCamera(forceAll: true);
            ScheduleShellOpen(Constants.Camera.ShellOpenDelay);
            RefreshBlanks();
            ApplyLedVisuals();
            RefreshTooltips();
        }
        else
        {
            CancelEquipDelay();
            _isHovered = false;
            ResetOcclusion();
            ResetCameraBob();
            ApplyCameraFlip();
            _wasGroundedLastFrame = true;
            _pausedByPocket = false;
            _pausedBySeat = false;
            bool holderDied = _lastHolder != null && _lastHolder.isPlayerDead;
            OnDropped(holderDied);
            if (!_recordingState.Value)
            {
                CloseShell();
            }
            RefreshBlanks();
            ApplyLedVisuals();
            RefreshTooltips();
        }
    }

    private void CloseOnPocket(bool nowPocketed)
    {
        CancelEquipDelay();
        if (nowPocketed)
        {
            if (!StillFilming())
            {
                ApplyClosedShellPose();
            }
            return;
        }

        ScheduleShellOpen(Constants.Camera.SlotOpenDelay);
    }

    private void ScheduleShellOpen(float delay)
    {
        CancelEquipDelay();
        if (delay <= 0f)
        {
            OpenShell();
            return;
        }
        _equipDelayCoroutine = StartCoroutine(OpenShellAfterDelay(delay));
    }

    private void CancelEquipDelay()
    {
        if (_equipDelayCoroutine != null)
        {
            StopCoroutine(_equipDelayCoroutine);
            _equipDelayCoroutine = null;
        }
    }

    private IEnumerator OpenShellAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        _equipDelayCoroutine = null;
        if (this != null && playerHeldBy != null)
        {
            OpenShell();
        }
    }

    private void UpdateHoverState()
    {
        var localPlayer = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        bool nowHovered = false;

        if (localPlayer != null && localPlayer.gameplayCamera != null
            && !localPlayer.isGrabbingObjectAnimation
            && !(localPlayer.quickMenuManager != null && localPlayer.quickMenuManager.isMenuOpen))
        {
            var camTransform = localPlayer.gameplayCamera.transform;
            if (Physics.Raycast(camTransform.position, camTransform.forward, out RaycastHit hit,
                    localPlayer.grabDistance, localPlayer.interactableObjectsMask, QueryTriggerInteraction.Collide)
                && hit.collider != null
                && hit.collider.CompareTag("PhysicsProp")
                && this != null
                && (hit.collider.gameObject == gameObject
                    || hit.collider.transform.IsChildOf(transform)))
            {
                nowHovered = true;
            }
        }

        bool keepOpenForPendingCassette = _walletDecorScan != null && _walletDecorScan.gameObject.activeInHierarchy;
        bool shouldOpenWallet = nowHovered || keepOpenForPendingCassette;
        if (shouldOpenWallet != _walletOpen && Time.unscaledTimeAsDouble >= _walletBusyUntil)
        {
            PlayWalletAnimation(opening: shouldOpenWallet);
        }
        _isHovered = nowHovered;
    }

    public override void EquipItem()
    {
        base.EquipItem();
        ApplySyncedZoom();
        if (playerHeldBy == null)
        {
            return;
        }

        if (!playerHeldBy.IsOwner)
        {
            return;
        }

        RefreshTooltips();
        SubscribeInput();
    }

    public override void SetControlTipsForItem()
    {
        RefreshTooltips();
        if (!IsHintHolder()) base.SetControlTipsForItem();
    }

    public override void DiscardItem()
    {
        base.DiscardItem();
        if (this == null)
        {
            return;
        }

        if (playerHeldBy == null || playerHeldBy.IsOwner)
        {
            UnsubscribeInput();
        }
    }

    public override void PocketItem()
    {
        base.PocketItem();
        UnsubscribeInput();
        Stow();
    }

    private void Stow()
    {
        ResetOcclusion();

        if (playerHeldBy != null && playerHeldBy.IsOwner
            && _nightVisionOn.Value)
        {
            RequestLightsOff();
        }
    }

}
