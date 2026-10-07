using System;
using System.Reflection;
using UnityEngine;

namespace Operator.Compat;

internal static class CullFactoryCompat
{
    private const string Guid = ModGUIDs.CullFactory;

    private static bool _initialized;
    private static Type? _optionsType;
    private static PropertyInfo? _skipCulling;
    private static PropertyInfo? _disableCulling;

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _optionsType = SoftDependency.FindType(Guid, "CullFactory.Behaviours.API.CameraCullingOptions");
            if (_optionsType != null)
            {
                _skipCulling = _optionsType.GetProperty("SkipCulling", BindingFlags.Public | BindingFlags.Instance);
                _disableCulling = _optionsType.GetProperty("DisableCulling", BindingFlags.Public | BindingFlags.Instance);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to set up CullFactory compatibility: {ex.Message}");
        }
    }

    internal static void ApplyTo(Camera? camera)
    {
        if (camera == null) return;
        Init();
        if (_optionsType == null) return;
        try
        {
            Component options = camera.GetComponent(_optionsType);
            if (options == null)
            {
                options = camera.gameObject.AddComponent(_optionsType);
            }
            _skipCulling?.SetValue(options, false);
            _disableCulling?.SetValue(options, false);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to apply CullFactory options to camera: {ex.Message}");
        }
    }
}
