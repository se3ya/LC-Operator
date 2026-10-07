using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Operator.Cassette;

internal static class CassetteBountyConfig
{
    private static readonly Dictionary<string, int> _cache = new();

    internal static int SumBounties()
    {
        int total = 0;
        foreach (int value in _cache.Values) total += value;
        return total;
    }

    private static readonly Dictionary<string, float> VanillaRates = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Manticoil"] = 0f,
        ["Docile Locust Bees"] = 0f,
        ["Tulip Snake"] = 0.15f,
        ["Cadaver Growths"] = 0.3f,
        ["Cadaver Bloom"] = 0.4f,
        ["Puffer"] = 0.4f,
        ["Hoarding bug"] = 0.5f,
        ["Blob"] = 0.6f,
        ["Baboon hawk"] = 0.7f,
        ["Red Locust Bees"] = 0.8f,
        ["Butler Bees"] = 0.8f,
        ["Centipede"] = 0.9f,
        ["Butler"] = 1.0f,
        ["Stingray"] = 1.0f,
        ["Bunker Spider"] = 1.2f,
        ["Nutcracker"] = 1.3f,
        ["Crawler"] = 1.4f,
        ["Clay Surgeon"] = 1.4f,
        ["Bush Wolf"] = 1.4f,
        ["MouthDog"] = 1.5f,
        ["Masked"] = 1.5f,
        ["Feiopar"] = 1.5f,
        ["Flowerman"] = 1.6f,
        ["Maneater"] = 1.6f,
        ["Earth Leviathan"] = 1.7f,
        ["Jester"] = 1.8f,
        ["ForestGiant"] = 1.8f,
        ["RadMech"] = 1.8f,
        ["Girl"] = 2.0f,
        ["GiantKiwi"] = 2.0f,
    };

    private static readonly Dictionary<string, int> VanillaCredits = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Spring"] = 35,
    };

    private static readonly HashSet<string> Unreleased = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Red pill",
        "Lasso",
    };

    internal static bool PrepareEntry(EnemyType? type)
    {
        if (type == null) return false;
        (string key, string display) = NameOf(type);
        if (string.IsNullOrWhiteSpace(display)) return false;
        if (Unreleased.Contains(display.Trim())) return false;
        if (_cache.ContainsKey(key)) return false;
        BindAndCache(key, display, GetDefault(type));
        return true;
    }

    internal static bool TryGetBounty(string enemyName, out int bounty)
    {
        bounty = 0;
        if (string.IsNullOrWhiteSpace(enemyName)) return false;
        return _cache.TryGetValue(ToKey(enemyName), out bounty);
    }

    private static (string sanitizedKey, string displayName) NameOf(EnemyType type)
    {
        string display = string.IsNullOrWhiteSpace(type.enemyName) ? type.name : type.enemyName;
        return (ToKey(display), display);
    }

    private static readonly char[] ForbiddenKeyChars = { '=', '\n', '\r', '\t', '\\', '"', '\'', '[', ']' };

    private static string ToKey(string raw)
    {
        string s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return "Unknown";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(System.Array.IndexOf(ForbiddenKeyChars, c) >= 0 ? '_' : c);
        }
        string cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? "Unknown" : cleaned;
    }

    private static int GetDefault(EnemyType type)
    {
        string display = string.IsNullOrWhiteSpace(type.enemyName) ? type.name : type.enemyName;
        float rate;
        if (!string.IsNullOrWhiteSpace(display) && VanillaCredits.TryGetValue(display.Trim(), out int credits))
        {
            return credits;
        }
        if (!string.IsNullOrWhiteSpace(display) && VanillaRates.TryGetValue(display.Trim(), out float curated))
        {
            rate = curated;
        }
        else if (type.isDaytimeEnemy && type.PowerLevel <= 1f)
        {
            rate = 0f;
        }
        else if (type.PowerLevel <= 1f)
        {
            return Constants.Bounty.LowPowerCredits;
        }
        else
        {
            rate = Constants.Bounty.DefaultRate * Mathf.Max(0.1f, 1f + Constants.Bounty.PowerLevelScale * (type.PowerLevel - 1f));
        }
        return Mathf.RoundToInt(rate * Constants.Bounty.RateToCredits);
    }

    private static void BindAndCache(string sanitizedKey, string displayName, int defaultValue)
    {
        int boundedDefault = Mathf.Clamp(defaultValue, Constants.Bounty.MinBounty, Constants.Bounty.MaxBounty);
        var entry = Plugin.Instance.Config.Bind(
            Constants.Bounty.ConfigSection,
            sanitizedKey,
            boundedDefault,
            new ConfigDescription(
                $"Credits a tape earns once for {displayName} on camera, before danger pay and bonuses. 0 means it pays nothing.",
                new AcceptableValueRange<int>(Constants.Bounty.MinBounty, Constants.Bounty.MaxBounty)));
        _cache[sanitizedKey] = entry.Value;
    }
}
