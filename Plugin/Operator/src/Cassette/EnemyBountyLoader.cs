using System;
using BepInEx.Configuration;
using Dawn;

namespace Operator.Cassette;

internal static class EnemyBountyLoader
{
    private static bool _hooked;
    private static bool _reported;

    internal static bool Loaded { get; private set; }

    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;

        try
        {
            if (LethalContent.Enemies.IsFrozen)
            {
                LoadFromDawnLib();
            }
            else
            {
                LethalContent.Enemies.OnFreeze += LoadFromDawnLib;
            }
        }
        catch (Exception ex)
        {
            _reported = true;
            Plugin.Log.LogError($"Failed to hook DawnLib enemy registry: {ex}");
        }
    }

    internal static void ReportNotLoaded()
    {
        if (Loaded || _reported) return;
        _reported = true;
        Plugin.Log.LogError("No enemy bounty values loaded.");
    }

    private static void LoadFromDawnLib()
    {
        ConfigFile? config = Plugin.Instance is { } plugin ? plugin.Config : null;
        if (config == null)
        {
            _reported = true;
            Plugin.Log.LogError("Config was not ready. No enemy bounty values were bound.");
            return;
        }

        try
        {
            int bound = 0;
            int failed = 0;
            string firstFailure = string.Empty;

            bool priorSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                foreach (DawnEnemyInfo info in LethalContent.Enemies.Values)
                {
                    if (info?.EnemyType == null) continue;
                    try
                    {
                        if (CassetteBountyConfig.PrepareEntry(info.EnemyType)) bound++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (firstFailure.Length == 0)
                        {
                            firstFailure = $"'{info.EnemyType.enemyName}': {ex.Message}";
                        }
                    }
                }
            }
            finally
            {
                config.SaveOnConfigSet = priorSave;
                config.Save();
            }

            if (failed > 0)
            {
                _reported = true;
                Plugin.Log.LogError($"Failed to bind {failed} bounty values, starting with {firstFailure}");
            }

            Loaded = bound > 0 && failed == 0;
            if (bound > 0)
            {
                Plugin.Log.LogInfo($"Bound bounty values for {bound} enemies.");
            }
        }
        catch (Exception ex)
        {
            _reported = true;
            Plugin.Log.LogError($"Failed to read enemy bounties from DawnLib: {ex}");
        }
    }
}
