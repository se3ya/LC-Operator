using HarmonyLib;
using Operator.Cassette;

namespace Operator.Patches;

[HarmonyPatch(typeof(TVScript), "Update")]
internal static class TvUpdatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(TVScript __instance)
    {
        return !TelevisionPlayback.IsSuppressing(__instance);
    }
}

[HarmonyPatch(typeof(TVScript), "OnEnable")]
internal static class DeckAttachPatch
{
    [HarmonyPostfix]
    private static void Postfix(TVScript __instance)
    {
        try
        {
            CassetteTakerManager.NotifyTvReady(__instance);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to attach cassette deck to tge TV: {ex.Message}");
        }
    }
}
