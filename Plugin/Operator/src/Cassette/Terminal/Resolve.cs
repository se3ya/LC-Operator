using System;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{

    private static bool TryFindCassette(string id, out string fileName, out string? filePath, out string source)
    {
        fileName = string.Empty;
        filePath = null;
        source = string.Empty;

        string typedKey = CassetteIdGenerator.ToMatchKey(id);
        if (string.IsNullOrEmpty(typedKey)) return false;

        bool found = false;

        var held = GetHeldCassette();
        if (held != null && CassetteIdGenerator.ToMatchKey(held.CassetteId) == typedKey)
        {
            fileName = held.FileName;
            filePath = held.FindLocalPath();
            source = "held tape";
            found = true;
        }

        if (!found)
        {
            foreach (var kv in CassetteLibrary.All)
            {
                if (CassetteIdGenerator.ToMatchKey(kv.Value.Id) == typedKey)
                {
                    fileName = kv.Value.FileName;
                    filePath = CassetteLibrary.FindLocalPath(kv.Value);
                    source = "library";
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            var world = FindWorldCassette(typedKey);
            if (world == null) return false;
            fileName = world.FileName;
            filePath = world.FindLocalPath();
            source = "world tape";
        }

        string merged = CassetteLibrary.GetMergedName(fileName);
        if (!string.Equals(merged, fileName, StringComparison.OrdinalIgnoreCase))
        {
            fileName = merged;
            filePath = FindFilePath(merged);
        }
        return true;
    }

    private static CassetteItem? FindWorldCassette(string typedKey)
    {
        foreach (var cassette in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (cassette == null) continue;
            if (CassetteIdGenerator.ToMatchKey(cassette.CassetteId) == typedKey)
            {
                return cassette;
            }
        }
        return null;
    }

    private static CassetteItem? GetHeldCassette()
    {
        var local = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        if (local == null) return null;
        var held = local.currentlyHeldObjectServer;
        return held != null ? held.GetComponent<CassetteItem>() : null;
    }

    private static bool TryGetLibraryEntry(string typedId, out CassetteLibrary.Entry? entry)
    {
        entry = null;

        string typedKey = CassetteIdGenerator.ToMatchKey(typedId);
        if (string.IsNullOrEmpty(typedKey)) return false;

        foreach (var kv in CassetteLibrary.All)
        {
            if (CassetteIdGenerator.ToMatchKey(kv.Value.Id) != typedKey) continue;
            entry = kv.Value;
            return true;
        }

        return false;
    }

}
