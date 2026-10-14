using GameNetcodeStuff;
using HarmonyLib;
using Operator.Recording.Voice;

namespace Operator.Patches;

[HarmonyPatch(typeof(CentipedeAI))]
internal static class CentipedeMufflePatch
{
    [HarmonyPatch("ClingToPlayer")]
    [HarmonyPostfix]
    private static void OnClingToPlayer(CentipedeAI __instance, PlayerControllerB playerScript)
    {
        PlayerControllerB? local = GameNetworkManager.Instance.localPlayerController;
        if (local != null && playerScript == local)
        {
            EnemyVoiceMuffle.SetFlea(__instance);
        }
    }

    [HarmonyPatch("StopClingingToPlayer")]
    [HarmonyPrefix]
    private static void OnStopClingingToPlayer(CentipedeAI __instance)
    {
        EnemyVoiceMuffle.ClearFlea(__instance);
    }
}
