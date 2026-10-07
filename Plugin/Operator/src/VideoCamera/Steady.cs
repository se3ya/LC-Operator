using GameNetcodeStuff;
using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private Vector2 _restHand = new(Constants.Camera.HoldRestHeight, Constants.Camera.HoldRestDepth);
    private float _steadyWeight;
    private int _steadyFrame = -1;
    private float _restTime;

    internal void SteadyArms(PlayerControllerB player)
    {
        Transform arms = player.localArmsTransform;
        Transform holder = player.localItemHolder;
        Transform rig = player.playerModelArmsMetarig;
        Transform target = player.localArmsRotationTarget;
        Camera cam = player.gameplayCamera;
        if (arms == null || holder == null || rig == null || target == null || cam == null
            || player.inSpecialInteractAnimation || player.localArmsMatchCamera || IsInspecting(player))
        {
            _steadyWeight = 0f;
            _restTime = 0f;
            return;
        }

        if (_steadyFrame != Time.frameCount - 1) _steadyWeight = 0f;
        _steadyFrame = Time.frameCount;

        bool steady = CanSteady(player);
        _steadyWeight = Mathf.MoveTowards(_steadyWeight, steady ? 1f : 0f, Time.deltaTime / Constants.Camera.HoldSteadyFade);
        _restTime = steady && _steadyWeight >= 1f && IsResting(player) ? _restTime + Time.deltaTime : 0f;
        if (_steadyWeight <= 0f) return;

        Quaternion frame = rig.rotation * Quaternion.Inverse(target.localRotation);
        Vector3 local = Quaternion.Inverse(frame) * (holder.position - cam.transform.position);
        var hand = new Vector2(local.y, local.z);
        if (_restTime >= Constants.Camera.HoldRestDelay)
        {
            _restHand = Vector2.MoveTowards(_restHand, hand, Constants.Camera.HoldRestRate * Time.deltaTime);
        }

        float reach = Constants.Camera.HoldSteadyReach;
        Vector2 offset = Vector2.ClampMagnitude(_restHand - hand, reach) * (Constants.Camera.HoldSteadiness * _steadyWeight);
        arms.position += frame * new Vector3(0f, offset.x, offset.y);
        if (parentObject == holder)
        {
            transform.rotation = holder.rotation * Quaternion.Euler(itemProperties.rotationOffset);
            transform.position = holder.position + holder.rotation * itemProperties.positionOffset;
        }
    }

    private bool CanSteady(PlayerControllerB player)
    {
        return isHeld && !isPocketed && playerHeldBy == player && !_wasSeatedLastFrame
               && !player.isPlayerDead && !player.isClimbingLadder && !player.inVehicleAnimation
               && !player.performingEmote && !player.isGrabbingObjectAnimation && !player.inTerminalMenu;
    }

    private static bool IsInspecting(PlayerControllerB player)
    {
        return player.IsInspectingItem || player.rightArmProceduralRig != null && player.rightArmProceduralRig.weight > 0.01f;
    }

    private static bool IsResting(PlayerControllerB player)
    {
        CharacterController controller = player.thisController;
        if (player.isWalking || controller == null || !controller.isGrounded) return false;
        Vector3 velocity = controller.velocity;
        float limit = Constants.Camera.HoldRestSpeed;
        return velocity.x * velocity.x + velocity.z * velocity.z < limit * limit;
    }
}
