using System;
using System.Reflection;
using UnityEngine;

namespace Operator.Compat;

internal static class LethalSpongeCompat
{
    private const string Guid = ModGUIDs.LethalSponge;

    internal static void Init()
    {
        if (!SoftDependency.IsLoaded(Guid)) return;

        try
        {
            Type? type = SoftDependency.FindType(Guid, "Scoops.service.MeshService");
            MethodInfo? decimate = type?.GetMethod("DecimateAllMeshes", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            if (decimate == null)
            {
                Plugin.Log.LogWarning("Could not find LethalSponge mesh simplifier.");
                return;
            }

            var harmony = new HarmonyLib.Harmony($"{Guid}.operatorcamera");
            harmony.Patch(decimate, prefix: new HarmonyLib.HarmonyMethod(typeof(LethalSpongeCompat), nameof(SkipCamera)));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to patch LethalSponge to skip camera model: {ex.Message}");
        }
    }

    private static bool SkipCamera(GameObject gameObject)
    {
        return gameObject == null || gameObject.GetComponent<VideoCameraItem>() == null;
    }
}
