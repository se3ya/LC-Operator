using System;
using System.Collections.Generic;
using Dawn;

namespace Operator.Cassette;

internal static class FirstFootage
{
    private static readonly NamespacedKey FilmedKey = NamespacedKey.From(Constants.PluginMetadata.ContentNamespace, "filmed_enemy_types");

    internal static bool IsNew(string typeName)
    {
        HashSet<string>? filmed = Load(out _);
        return filmed != null && !filmed.Contains(typeName);
    }

    internal static void Mark(IEnumerable<string> typeNames)
    {
        HashSet<string>? filmed = Load(out PersistentDataContainer? save);
        if (filmed == null || save == null) return;

        bool changed = false;
        foreach (string name in typeNames)
        {
            changed |= filmed.Add(name);
        }
        if (changed) save.Set(FilmedKey, string.Join("\n", filmed));
    }

    private static HashSet<string>? Load(out PersistentDataContainer? save)
    {
        save = null;
        try
        {
            if (GameNetworkManager.Instance == null) return null;
            save = DawnLib.GetCurrentSave();
            if (save == null) return null;

            var filmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (save.TryGet(FilmedKey, out string? stored) && !string.IsNullOrEmpty(stored))
            {
                foreach (string name in stored!.Split('\n'))
                {
                    if (name.Length > 0) filmed.Add(name);
                }
            }
            return filmed;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not read filmed enemy types from the save: {ex.Message}");
            save = null;
            return null;
        }
    }
}
