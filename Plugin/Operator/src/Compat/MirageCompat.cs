using System;
using System.Reflection;
using GameNetcodeStuff;
using UnityEngine;

namespace Operator.Compat;

internal static class MirageCompat
{
    private const string PluginGuid = "qwbarch.Mirage";
    private const string StreamTypeName = "Mirage.Unity.AudioStream+AudioStream";

    private static readonly string[] SettingTypeNames =
    {
        "Mirage.Domain.Setting",
        "Mirage.Domain.SettingModule"
    };

    private static bool _initialized;
    private static Type? _streamType;
    private static MethodInfo? _getSettings;
    private static PropertyInfo? _muteVoiceMimic;

    internal static bool IsStreaming(AudioSource source)
    {
        if (source == null) return false;
        if (!_initialized) Init();
        if (_streamType == null) return false;

        try
        {
            return source.GetComponentInParent(_streamType) != null;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not tell whether mimic is still writing its voice. {error.Message}");
            _streamType = null;
            return false;
        }
    }

    internal static bool ShouldHearMimic(AudioSource source, bool cameraInside)
    {
        if (source == null) return false;
        if (!_initialized) Init();
        if (_streamType == null) return false;

        try
        {
            if (source.GetComponentInParent(_streamType) == null) return false;

            EnemyAI? enemy = source.GetComponentInParent<EnemyAI>();
            if (enemy == null || enemy.isEnemyDead) return false;
            if (enemy is DressGirlAI) return false;

            PlayerControllerB? local = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;
            if (local == null) return false;

            if (local.isInsideFactory != enemy.isOutside) return false;
            if (cameraInside == enemy.isOutside) return false;

            return !IsMimicryOff();
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not work out whether camera should hear mimic. {error.Message}");
            _streamType = null;
            return false;
        }
    }

    private static bool IsMimicryOff()
    {
        if (_getSettings == null || _muteVoiceMimic == null) return true;

        object? settings = _getSettings.Invoke(null, null);
        if (settings == null) return true;

        return _muteVoiceMimic.GetValue(settings) is bool muted && muted;
    }

    private static void Init()
    {
        _initialized = true;
        if (!SoftDependency.IsLoaded(PluginGuid)) return;

        _streamType = SoftDependency.FindType(PluginGuid, StreamTypeName);
        if (_streamType == null) return;

        Plugin.Log.LogInfo("Mirage found.");

        foreach (string name in SettingTypeNames)
        {
            Type? settingType = SoftDependency.FindType(PluginGuid, name);
            _getSettings = settingType?.GetMethod("getSettings", BindingFlags.Public | BindingFlags.Static);
            if (_getSettings == null) continue;

            _muteVoiceMimic = _getSettings.ReturnType.GetProperty("muteVoiceMimic");
            if (_muteVoiceMimic != null) return;

            _getSettings = null;
        }

        Plugin.Log.LogWarning("Could not read Mirage mimic voice setting.");
    }
}
