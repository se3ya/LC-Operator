using UnityEngine;

namespace Operator.Cassette;

internal static class CassetteLimits
{
    internal static int GetMaxValue()
    {
        float bounties = Mathf.Max(0, CassetteBountyConfig.SumBounties());
        float share = 1f + Constants.Bounty.FramingBonus + Constants.Bounty.KillBonus + Constants.Bounty.FirstFootageBonus + Constants.Bounty.SharedShotBonus;

        float analytic = VideoCameraConfig.FlatBaseValue.Value + (bounties * Constants.Bounty.MaxDanger * share);

        int doubled = Mathf.CeilToInt(analytic * Constants.Bounty.ValueCeilingHeadroom);
        return Mathf.Clamp(doubled, 1, Constants.Bounty.MaxValue);
    }

    internal static int ClampValue(int value, ulong sender, string what)
    {
        int ceiling = GetMaxValue();
        int capped = Mathf.Clamp(value, 1, ceiling);
        if (capped == value || value <= 0) return capped;

        if (value > 0 && value - capped <= Mathf.Max(1, capped / 100))
        {
            Plugin.Log.LogDebug($"{what} from client {sender} trimmed from {value} to {capped}.");
        }
        else
        {
            Plugin.Log.LogWarning($"{what} from client {sender} clamped from {value} to {capped}.");
        }
        return capped;
    }

    internal static int ClampStoredValue(int value)
    {
        return Mathf.Clamp(value, 0, Constants.Bounty.MaxValue);
    }

    internal static float ClampDuration(float duration, ulong sender)
    {
        float max = (Constants.ConfigRanges.MaxMinutesMax * 60f) + 4f;
        if (float.IsNaN(duration) || float.IsInfinity(duration)) return 0f;
        float capped = Mathf.Clamp(duration, 0f, max);
        if (!Mathf.Approximately(capped, duration))
        {
            Plugin.Log.LogWarning($"Cassette duration from client {sender} clamped from {duration:0}s to {capped:0}s.");
        }
        return capped;
    }

    internal static bool IsSaneSpawn(Vector3 position, Vector3 origin)
    {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return false;
        if (float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z)) return false;
        return (position - origin).sqrMagnitude <= Constants.Camera.MaxEjectDistance * Constants.Camera.MaxEjectDistance;
    }
}
