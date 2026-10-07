using UnityEngine;
using Operator.Compat;

namespace Operator.Cassette;

internal static class ShipMonitors
{
    private static bool UseGeneralImprovements =>
        GeneralImprovementsCompat.Available && GeneralImprovementsCompat.MonitorCount > 0;

    internal static bool Available => GeneralImprovementsCompat.Available || VanillaMonitor.Available;

    internal static int MonitorCount =>
        UseGeneralImprovements ? GeneralImprovementsCompat.MonitorCount : VanillaMonitor.MonitorCount;

    internal static Transform? GetMonitorTransform(int index1Based) =>
        UseGeneralImprovements
            ? GeneralImprovementsCompat.GetMonitorTransform(index1Based)
            : VanillaMonitor.GetMonitorTransform(index1Based);

    internal static bool TryGetScreenCenter(int index1Based, out Vector3 world) =>
        UseGeneralImprovements
            ? GeneralImprovementsCompat.TryGetScreenCenter(index1Based, out world)
            : VanillaMonitor.TryGetScreenCenter(index1Based, out world);

    internal static bool TryBind(int index1Based, RenderTexture rt) =>
        UseGeneralImprovements
            ? GeneralImprovementsCompat.TryBind(index1Based, rt)
            : VanillaMonitor.TryBind(index1Based, rt);

    internal static bool IsBound(int index1Based) =>
        UseGeneralImprovements
            ? GeneralImprovementsCompat.IsBound(index1Based)
            : VanillaMonitor.IsBound(index1Based);

    internal static bool Reapply(int index1Based) =>
        UseGeneralImprovements
            ? GeneralImprovementsCompat.Reapply(index1Based)
            : VanillaMonitor.Reapply(index1Based);

    internal static void Restore(int index1Based)
    {
        if (UseGeneralImprovements)
        {
            GeneralImprovementsCompat.Restore(index1Based);
            return;
        }
        VanillaMonitor.Restore(index1Based);
    }
}
