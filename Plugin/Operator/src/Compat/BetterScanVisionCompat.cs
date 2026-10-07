using System;
using System.Reflection;
using UnityEngine;

namespace Operator.Compat;

internal static class BetterScanVisionCompat
{
    private static bool _initialized;
    private static FieldInfo? _scanLightField;

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        if (!SoftDependency.IsLoaded(ModGUIDs.BetterScanVision)) return;
        try
        {
            Type? type = SoftDependency.FindType(ModGUIDs.BetterScanVision, "BetterScanVision.PlayerControllerBPatches");
            if (type == null) return;
            _scanLightField = type.GetField("nightVision", BindingFlags.Static | BindingFlags.NonPublic)
                           ?? type.GetField("nightVision", BindingFlags.Static | BindingFlags.Public);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to find BetterScanVision scan light: {ex.Message}");
        }
    }

    internal static Light? GetScanLight()
    {
        Init();
        if (_scanLightField == null) return null;
        try { return _scanLightField.GetValue(null) as Light; }
        catch { return null; }
    }
}
