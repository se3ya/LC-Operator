using HarmonyLib;
using Operator.Recording;
using UnityEngine;

namespace Operator.Patches;

[HarmonyPatch(typeof(AudioSource))]
internal static class AudioSourceOneShotPatch
{
    [HarmonyPatch(nameof(AudioSource.PlayOneShot), typeof(AudioClip), typeof(float))]
    [HarmonyPostfix]
    private static void MirrorScaled(AudioSource __instance, AudioClip clip, float volumeScale, bool __runOriginal)
    {
        if (!__runOriginal) return;
        ShadowOneShotMirror.MirrorAll(__instance, clip, volumeScale);
    }
}
