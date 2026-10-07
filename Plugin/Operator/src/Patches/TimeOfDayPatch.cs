using HarmonyLib;

namespace Operator.Patches;

[HarmonyPatch(typeof(TimeOfDay), nameof(TimeOfDay.SetNewProfitQuota))]
internal static class QuotaRepairPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (StartOfRound.Instance == null || !StartOfRound.Instance.IsServer) return;
        if (VideoCameraConfig.LensRepair.Value == LensRepair.Never) return;
        VideoCameraItem.ServerRepairAllLenses("new quota");
    }
}
