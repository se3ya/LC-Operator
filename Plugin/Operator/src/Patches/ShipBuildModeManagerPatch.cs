using HarmonyLib;
using Operator.Cassette;

namespace Operator.Patches;

[HarmonyPatch(typeof(ShipBuildModeManager), "Update")]
internal static class BuildGhostPatch
{
    private static bool _failed;

    [HarmonyPostfix]
    private static void Postfix(ShipBuildModeManager __instance)
    {
        if (_failed) return;
        try
        {
            DeckGhost.Sync(__instance);
        }
        catch (System.Exception ex)
        {
            _failed = true;
            Plugin.Log.LogError($"Failed to update the deck build ghost: {ex}");
        }
    }
}
