using System.Collections.Generic;
using UnityEngine;

namespace Operator.Cassette;

internal static class CassetteIconLibrary
{
    private static readonly Dictionary<string, Sprite?> _cache = new();

    internal static Sprite? GetIcon(string cassetteId, int value)
    {
        string key = CassetteShellColor.GetVariantKey(cassetteId, value);
        if (_cache.TryGetValue(key, out Sprite? cached)) return cached;

        Sprite? sprite = AssetsLoader.LoadOptionalAsset<Sprite>(Constants.Cassette.IconAssetPrefix + key);
        if (sprite == null)
        {
            Plugin.Log.LogDebug($"Cassette icon '{Constants.Cassette.IconAssetPrefix}{key}' missing from bundle.");
        }

        _cache[key] = sprite;
        return sprite;
    }
}
