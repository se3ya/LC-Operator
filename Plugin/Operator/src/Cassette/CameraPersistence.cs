using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Dawn;
using UnityEngine;

namespace Operator.Cassette;

internal static class CameraPersistence
{
    private const string Ns = Constants.PluginMetadata.ContentNamespace;
    private const string KeyPrefix = "camera";

    private static StartOfRound? _stampRound;
    private static int _stamp;

    public static bool IsAvailable => TryGetSave(out _);

    public static int GetLobbyStamp()
    {
        if (_stampRound != StartOfRound.Instance)
        {
            _stampRound = StartOfRound.Instance;
            _stamp = UnityEngine.Random.Range(1, int.MaxValue);
        }
        return _stamp;
    }

    public static bool IsDawnItemSaving()
    {
        try
        {
            ConfigFile config = Chainloader.PluginInfos[DawnLib.PLUGIN_GUID].Instance.Config;
            return !config.TryGetEntry("Dawn Save System", "Item Saving", out ConfigEntry<bool> disabled) || !disabled.Value;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not read DawnLibs item saving setting, assuming it is on: {ex.Message}");
            return true;
        }
    }

    public static void Store(int handle, CameraSaveData data)
    {
        if (handle <= 0 || data == null) return;
        if (!TryGetSave(out PersistentDataContainer save)) return;
        NamespacedKey key = EntryKey(handle);
        string text = JsonUtility.ToJson(data);
        if (TryReadText(save, key, out string stored) && stored == text) return;
        save.Set(key, text);
    }

    public static bool TryLoad(int handle, out CameraSaveData data)
    {
        data = null!;
        if (handle <= 0) return false;
        if (!TryGetSave(out PersistentDataContainer save)) return false;
        if (!save.TryGet(EntryKey(handle), out string? text) || text == null) return false;
        data = Parse(text).Normalized();
        return true;
    }

    public static void AddFiles(HashSet<string> into)
    {
        if (!GameNetworkManager.Instance.isHostingGame) return;
        if (!TryGetSave(out PersistentDataContainer save)) return;
        var records = new List<CameraSaveData>();
        foreach (NamespacedKey key in FindKeys(save))
        {
            if (TryReadText(save, key, out string text) && TryParse(text, out CameraSaveData record)) records.Add(record);
        }
        CameraSaveData.AddFiles(into, records);
    }

    public static void ClearCracks()
    {
        if (!TryGetSave(out PersistentDataContainer save)) return;
        int cleared = 0;
        foreach (NamespacedKey key in FindKeys(save))
        {
            if (!TryReadText(save, key, out string text) || !TryParse(text, out CameraSaveData record)) continue;
            if (!record.ClearCracks()) continue;
            save.Set(key, JsonUtility.ToJson(record.Normalized()));
            cleared++;
        }
        if (cleared > 0) Plugin.Log.LogDebug($"Stored cameras with repaired lenses: {cleared}.");
    }

    private static bool TryGetSave(out PersistentDataContainer save)
    {
        save = null!;
        try
        {
            PersistentDataContainer? current = DawnLib.GetCurrentContract();
            if (current == null) return false;
            save = current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadText(PersistentDataContainer save, NamespacedKey key, out string text)
    {
        text = string.Empty;
        try
        {
            if (!save.TryGet(key, out string? loaded) || loaded == null) return false;
            text = loaded;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CameraSaveData Parse(string text)
    {
        var record = new CameraSaveData();
        JsonUtility.FromJsonOverwrite(text, record);
        return record;
    }

    private static bool TryParse(string text, out CameraSaveData record)
    {
        try
        {
            record = Parse(text);
            return true;
        }
        catch (Exception)
        {
            record = null!;
            return false;
        }
    }

    private static List<NamespacedKey> FindKeys(PersistentDataContainer save)
    {
        var found = new List<NamespacedKey>();
        foreach (NamespacedKey key in save.Keys)
        {
            if (key.Namespace == Ns && key.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)) found.Add(key);
        }
        return found;
    }

    private static NamespacedKey EntryKey(int handle) => NamespacedKey.From(Ns, $"{KeyPrefix}.{handle}");
}
