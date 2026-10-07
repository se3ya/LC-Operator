using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private void UpdateCameraBob()
    {
        if (!_cameraRestCaptured) return;
        if (!Constants.Visual.BobEnabled)
        {
            ResetCameraBob();
            return;
        }

        var player = playerHeldBy;
        if (player == null || StartOfRound.Instance?.localPlayerController != player)
        {
            ResetCameraBob();
            return;
        }

        Vector3 vel = player.thisController != null ? player.thisController.velocity : Vector3.zero;
        float horizSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
        bool grounded = player.thisController != null && player.thisController.isGrounded;

        float walkFreq = Constants.Visual.BobWalkFrequency;
        float runFreq = Constants.Visual.BobRunFrequency;
        float walkPitchDeg = Constants.Visual.BobWalkPitch;
        float runPitchDeg = Constants.Visual.BobRunPitch;
        float walkRollDeg = Constants.Visual.BobWalkRoll;
        float runRollDeg = Constants.Visual.BobRunRoll;

        float rawSpeedFactor = Mathf.Clamp01(horizSpeed / 7f);
        _smoothSpeed = Mathf.Lerp(_smoothSpeed, rawSpeedFactor, 5f * Time.deltaTime);
        float speedFactor = _smoothSpeed;

        float freq = Mathf.Lerp(walkFreq, runFreq, speedFactor);
        float intensity = Mathf.Max(0f, VideoCameraConfig.BobIntensity.Value);
        float pitchDeg = Mathf.Lerp(walkPitchDeg, runPitchDeg, speedFactor) * intensity;
        float rollDeg = Mathf.Lerp(walkRollDeg, runRollDeg, speedFactor) * intensity;

        float moveGate = Mathf.SmoothStep(0f, 0.2f, _smoothSpeed);
        pitchDeg *= moveGate;
        rollDeg *= moveGate;

        if (grounded)
        {
            _bobPhase += freq * Time.deltaTime;
        }
        else
        {
            _bobPhase += freq * 0.3f * Time.deltaTime;
            pitchDeg *= 0.25f;
            rollDeg *= 0.25f;
        }

        float bobPitch = Mathf.Sin(_bobPhase * 2f * Mathf.PI) * pitchDeg;
        float bobRoll = Mathf.Sin(_bobPhase * Mathf.PI + 0.5f) * rollDeg;

        if (grounded && !_wasGroundedLastFrame)
        {
            _jumpPitchKick = -4.5f * intensity;
        }
        else if (!grounded && _wasGroundedLastFrame)
        {
            _jumpPitchKick = +3.5f * intensity;
        }
        _wasGroundedLastFrame = grounded;
        _jumpPitchKick = Mathf.MoveTowards(_jumpPitchKick, 0f, 24f * Time.deltaTime);

        float totalPitch = bobPitch + _jumpPitchKick;
        Quaternion bobR = Quaternion.Euler(totalPitch, 0f, bobRoll);

        _bobPitch = totalPitch;
        _bobRoll = bobRoll;

        if (_camera != null)
        {
            _camera.transform.localRotation = _cameraRestLocalRot * bobR;
        }
    }

    private void ResetCameraBob()
    {
        _bobPhase = 0f;
        _jumpPitchKick = 0f;
        if (!_cameraRestCaptured) return;
        if (_camera != null) _camera.transform.localRotation = _cameraRestLocalRot;
    }
}
