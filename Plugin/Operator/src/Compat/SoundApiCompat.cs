using System;
using System.Reflection;
using UnityEngine;

namespace Operator.Compat;

internal static class SoundApiCompat
{
    private const string PluginGuid = "me.loaforc.soundapi";

    private static bool _initialized;
    private static MethodInfo? _getOrCreate;
    private static MethodInfo? _realClip;
    private static readonly object[] _argument = new object[1];

    internal static AudioClip? ClipOn(AudioSource source)
    {
        if (source == null) return null;
        if (!_initialized) Init();
        if (_getOrCreate == null || _realClip == null) return source.clip;

        try
        {
            _argument[0] = source;
            object? data = _getOrCreate.Invoke(null, _argument);
            if (data == null) return source.clip;
            return _realClip.Invoke(data, null) as AudioClip ?? source.clip;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not read sound replaced clip which is playing, falling back to games own. {error.Message}");
            _getOrCreate = null;
            _realClip = null;
            return source.clip;
        }
    }

    private static void Init()
    {
        _initialized = true;
        if (!SoftDependency.IsLoaded(PluginGuid)) return;

        Type? type = SoftDependency.FindType(PluginGuid, "loaforcsSoundAPI.Core.AudioSourceAdditionalData");
        if (type == null) return;

        _getOrCreate = type.GetMethod(
            "GetOrCreate",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(AudioSource) },
            null);
        _realClip = type.GetProperty("RealClip", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();

        if (_getOrCreate == null || _realClip == null)
        {
            _getOrCreate = null;
            _realClip = null;
            Plugin.Log.LogWarning("Could not read clips replaced by loaforcsSoundAPI.");
            return;
        }

        Plugin.Log.LogInfo("Reading replaced sounds through loaforcSoundAPI.");
    }
}
