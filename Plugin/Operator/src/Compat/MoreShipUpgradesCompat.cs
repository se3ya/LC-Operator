using System;
using System.Reflection;

namespace Operator.Compat;

internal static class MoreShipUpgradesCompat
{
    private const string Guid = ModGUIDs.MoreShipUpgrades;
    private const string LithiumBatteriesType = "MoreShipUpgrades.UpgradeComponents.TierUpgrades.Items.LithiumBatteries";

    private static bool _initialized;
    private static MethodInfo? _chargeRate;

    internal static float ApplyLithiumBatteriesRate(float drain)
    {
        Init();
        if (_chargeRate == null) return drain;

        try
        {
            return (float)_chargeRate.Invoke(null, new object[] { drain });
        }
        catch (Exception)
        {
            return drain;
        }
    }

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Type? type = SoftDependency.FindType(Guid, LithiumBatteriesType);
            _chargeRate = type?.GetMethod("GetChargeRateMultiplier", BindingFlags.Public | BindingFlags.Static);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to hook MoreShipUpgrades Lithium Batteries upgrade: {ex.Message}");
        }
    }
}
