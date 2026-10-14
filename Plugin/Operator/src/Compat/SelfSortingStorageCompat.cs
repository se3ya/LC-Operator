using System;
using System.Reflection;
using GameNetcodeStuff;

namespace Operator.Compat;

internal static class SelfSortingStorageCompat
{
    private const string Guid = ModGUIDs.SelfSortingStorage;

    internal static void Init()
    {
        if (!SoftDependency.IsLoaded(Guid)) return;

        try
        {
            Type? type = SoftDependency.FindType(Guid, "SelfSortingStorage.Cupboard.SmartCupboard");
            MethodInfo? store = type?.GetMethod("StoreObject", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(PlayerControllerB) }, null);
            if (store == null)
            {
                Plugin.Log.LogWarning("Could not find SSS store method.");
                return;
            }

            var harmony = new HarmonyLib.Harmony($"{Guid}.operatorcamera");
            harmony.Patch(store, prefix: new HarmonyLib.HarmonyMethod(typeof(SelfSortingStorageCompat), nameof(KeepTake)));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to patch SelfSortingStorage to keep camcorder takes: {ex.Message}");
        }
    }

    private static void KeepTake(PlayerControllerB player)
    {
        if (player == null || !player.isHoldingObject || player.isGrabbingObjectAnimation) return;
        if (player.currentlyHeldObjectServer is not VideoCameraItem camera) return;

        try
        {
            camera.KeepActiveTake();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to keep camcorder take before storing it: {ex}");
        }
    }
}
