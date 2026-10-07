using System;
using Dawn;
using UnityEngine;

namespace Operator.Cassette;

internal static class CompanyMoon
{
    private static SelectableLevel? _lastLevel;
    private static bool _isCompany;

    internal static bool IsFootageWorthless()
    {
        try
        {
            StartOfRound round = StartOfRound.Instance;
            if (round == null) return false;
            if (round.inShipPhase && VideoCameraConfig.WorthlessOrbitFootage.Value) return true;

            SelectableLevel? level = round.currentLevel;
            if (level == null) return false;

            if (level != _lastLevel)
            {
                _lastLevel = level;
                _isCompany = Classify(level);
                Plugin.Log.LogDebug(_isCompany
                    ? $"{level.PlanetName} is company moon so footage from it is worth nothing"
                    : $"{level.PlanetName} is an ordinary moon");
            }

            return _isCompany;
        }
        catch
        {
            return false;
        }
    }

    private static bool Classify(SelectableLevel level)
    {
        if (Matches(level, VideoCameraConfig.WorthlessMoonsExcluded.Value)) return false;
        if (Matches(level, VideoCameraConfig.WorthlessMoonsForced.Value)) return true;
        if (string.Equals(level.sceneName, "CompanyBuilding", StringComparison.Ordinal)) return true;

        if (!level.spawnEnemiesAndScrap) return true;

        try
        {
            DawnMoonInfo? info = level.GetDawnInfo();
            if (info != null && info.HasTag(Tags.Company)) return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool Matches(SelectableLevel level, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string planet = level.PlanetName ?? string.Empty;
        int space = planet.IndexOf(' ');
        string shortName = space >= 0 ? planet.Substring(space + 1) : planet;

        foreach (string part in raw.Split(','))
        {
            string name = part.Trim();
            if (name.Length == 0) continue;

            if (string.Equals(name, level.sceneName, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, planet, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, shortName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    internal static void Forget()
    {
        _lastLevel = null;
        _isCompany = false;
    }
}
