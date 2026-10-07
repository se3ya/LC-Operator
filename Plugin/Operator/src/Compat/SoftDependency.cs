using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

namespace Operator.Compat;

internal static class SoftDependency
{
    public static bool IsLoaded(string pluginGuid) => Chainloader.PluginInfos.ContainsKey(pluginGuid);

    public static Type? FindType(string pluginGuid, string typeName)
    {
        var timer = Stopwatch.StartNew();

        Type? type = null;
        if (Chainloader.PluginInfos.TryGetValue(pluginGuid, out PluginInfo? info))
        {
            type = SafeGetType(info?.Instance?.GetType().Assembly, typeName);
        }

        if (type == null)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = SafeGetType(assembly, typeName);
                if (type != null) break;
            }
        }

        if (type == null)
        {
            return null;
        }

        return type;
    }

    private static Type? SafeGetType(Assembly? assembly, string typeName)
    {
        if (assembly == null) return null;
        try { return assembly.GetType(typeName); }
        catch { return null; }
    }
}
