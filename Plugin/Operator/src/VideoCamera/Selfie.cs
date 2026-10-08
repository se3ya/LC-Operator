using UnityEngine;
using UnityEngine.Rendering;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void OnBeginCameraRendering(ScriptableRenderContext _, Camera cam)
    {
        if (cam == null) return;
        if (cam != _camera) return;

        SetNightVisionLights(true);
    }

    private void OnEndCameraRendering(ScriptableRenderContext _, Camera cam)
    {
        if (cam == null) return;
        if (cam != _camera) return;

        SetNightVisionLights(false);

        if (cam != ActiveCamera) return;

        SampleRawFeedAmbient();

        DrawAutoFocusBlur();
        DrawExposureAdaptation();
        DrawNightVision();

        if (_occlusionProximity > 0.001f)
        {
            DrawOcclusionOverlay(_occlusionProximity);
            if (_nvWasDrawing && _occlusionProximity >= 0.99f) ClearNightVisionHistory();
        }

        DrawVeilingGlare();
        DrawCamcorderLook();
        DrawGlassDamage();
    }

    private struct SelfiePose
    {
        public bool Moved;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private bool IsLocalWebcam()
    {
        StartOfRound sor = StartOfRound.Instance;
        return _cameraMode.Value && playerHeldBy != null && sor != null && playerHeldBy == sor.localPlayerController;
    }

    private SelfiePose MoveToHand()
    {
        if (!IsLocalWebcam()) return default;
        Transform? hand = playerHeldBy.serverItemHolder;
        if (hand == null || itemProperties == null || !IsHoldPoseReady(playerHeldBy)) return default;

        Vector3 position = hand.position + (hand.rotation * itemProperties.positionOffset);
        float reach = Constants.Visual.RemoteLensReach;
        if ((position - transform.position).sqrMagnitude > reach * reach) return default;

        var pose = new SelfiePose { Moved = true, Position = transform.position, Rotation = transform.rotation };
        transform.SetPositionAndRotation(position, hand.rotation * Quaternion.Euler(itemProperties.rotationOffset));
        return pose;
    }

    private void RestoreSelfiePose(SelfiePose pose)
    {
        if (pose.Moved) transform.SetPositionAndRotation(pose.Position, pose.Rotation);
    }
}
