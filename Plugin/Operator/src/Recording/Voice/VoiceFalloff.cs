using GameNetcodeStuff;
using UnityEngine;

namespace Operator.Recording.Voice;

internal static class VoiceFalloff
{
    internal static void GetLevels(float distance, bool inside, AudioSource? source, out float direct, out float room)
    {
        float near = Mathf.Max(0.01f, distance);
        float echo = inside
            ? Constants.Audio.EchoIndoorDistance
            : Constants.Audio.EchoOutdoorDistance;
        float range = Mathf.Max(1f, VideoCameraConfig.VoiceRange.Value);
        float level = VoiceCurve.At(source, near * Constants.Audio.VoiceRange / range);
        float norm = Mathf.Sqrt(1f / (near * near) + 1f / (echo * echo));
        float nearFactor = Mathf.Max(
            Constants.Audio.NearFloor,
            Mathf.Pow(Constants.Audio.NearReference / Mathf.Max(near, Constants.Audio.NearReference), Constants.Audio.NearExponent));
        direct = level / near / norm * nearFactor;
        room = level / echo / norm * nearFactor;
    }

    internal static float HeldRoom(bool inside)
    {
        float echo = inside
            ? Constants.Audio.EchoIndoorDistance
            : Constants.Audio.EchoOutdoorDistance;
        return Constants.Audio.HeldRoomDistance / echo;
    }

    internal static bool CanHear(PlayerControllerB speaker, bool cameraInside)
    {
        return !speaker.isPlayerDead && speaker.isInsideFactory == cameraInside;
    }

    internal static Vector3 MouthOf(PlayerControllerB speaker)
    {
        Transform? eye = speaker.playerEye;
        if (eye != null) return eye.position;

        Camera? head = speaker.gameplayCamera;
        return head != null ? head.transform.position : speaker.transform.position;
    }

    internal static void GetPan(Vector3 mouth, Vector3 origin, Vector3 right, Vector3 forward, out float left, out float rightGain)
    {
        Vector3 toMouth = mouth - origin;
        float distance = toMouth.magnitude;
        if (distance <= 0.0001f)
        {
            left = 1f;
            rightGain = 1f;
            return;
        }

        float half = Constants.Audio.VoiceSpread * Mathf.Deg2Rad * 0.5f;
        float side = Mathf.Sin(half);
        float centre = 0.5f * Mathf.Sqrt(2f - side * side);

        Vector3 unit = toMouth / distance;
        float x = Vector3.Dot(unit, right);
        float z = Vector3.Dot(unit, forward);
        float horizontal = Mathf.Sqrt(x * x + z * z);
        float azimuth = Mathf.Atan2(x, z);

        float a = horizontal * Mathf.Sin(azimuth - half);
        float b = horizontal * Mathf.Sin(azimuth + half);

        left = 0.5f * (PanLeft(a) + PanLeft(b)) / centre;
        rightGain = 0.5f * (PanRight(a) + PanRight(b)) / centre;

        float loudest = Mathf.Max(left, rightGain);
        if (loudest > 1f)
        {
            left /= loudest;
            rightGain /= loudest;
        }
    }

    private static float PanLeft(float pan) => (Mathf.Sqrt(2f - pan * pan) - pan) * 0.5f;

    private static float PanRight(float pan) => (Mathf.Sqrt(2f - pan * pan) + pan) * 0.5f;
}
