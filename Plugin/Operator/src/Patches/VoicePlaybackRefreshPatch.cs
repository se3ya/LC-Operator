using HarmonyLib;
using Operator.Recording;

namespace Operator.Patches;

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.RefreshPlayerVoicePlaybackObjects))]
internal static class VoicePlaybackRefreshPatch
{
    [HarmonyPostfix]
    private static void NoteRefresh()
    {
        try
        {
            ShadowVoiceChat.OnVoicesRefreshed();
        }
        catch
        {
        }
    }
}
