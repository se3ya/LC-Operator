using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using LethalConfig;
using LethalConfig.ConfigItems;
using LethalConfig.ConfigItems.Options;

namespace Operator.Compat;

internal static class LethalConfigCompat
{
    private const string Guid = ModGUIDs.LethalConfig;

    private static bool? _present;

    public static bool Present => _present ??= SoftDependency.IsLoaded(Guid);

    public static void Register(ConfigFile config)
    {
        if (!Present) return;

        try
        {
            RegisterEntries(config);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register settings with LethalConfig: {ex.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void RegisterEntries(ConfigFile config)
    {
        LethalConfigManager.SetModDescription(Constants.PluginMetadata.Description);

        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
        {
            if (pair.Key.Section == Constants.Bounty.ConfigSection) continue;

            try
            {
                AddItem(pair.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Failed to add '{pair.Key}' to LethalConfig menu: {ex.Message}");
            }
        }
    }

    private static void AddItem(ConfigEntryBase entry)
    {
        switch (entry)
        {
            case ConfigEntry<bool> flag:
                LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(flag, requiresRestart: false));
                break;

            case ConfigEntry<int> number:
                LethalConfigManager.AddConfigItem(TryGetRange(number, out int intMin, out int intMax)
                    ? new IntSliderConfigItem(number, new IntSliderOptions { Min = intMin, Max = intMax, RequiresRestart = false })
                    : new IntInputFieldConfigItem(number, requiresRestart: false));
                break;

            case ConfigEntry<float> number:
                LethalConfigManager.AddConfigItem(TryGetRange(number, out float floatMin, out float floatMax)
                    ? new FloatSliderConfigItem(number, new FloatSliderOptions { Min = floatMin, Max = floatMax, RequiresRestart = false })
                    : new FloatInputFieldConfigItem(number, requiresRestart: false));
                break;

            case ConfigEntry<string> text:
                LethalConfigManager.AddConfigItem(new TextInputFieldConfigItem(text, requiresRestart: false));
                break;

            default:
                AddEnumItem(entry);
                break;
        }
    }

    private static void AddEnumItem(ConfigEntryBase entry)
    {
        if (!entry.SettingType.IsEnum) return;

        Type itemType = typeof(EnumDropDownConfigItem<>).MakeGenericType(entry.SettingType);
        Type entryType = typeof(ConfigEntry<>).MakeGenericType(entry.SettingType);
        ConstructorInfo? constructor = itemType.GetConstructor(new[] { entryType, typeof(bool) });
        if (constructor == null)
        {
            Plugin.Log.LogDebug($"LethalConfig has no enum dropdown constructor for {entry.SettingType.Name}.");
            return;
        }

        LethalConfigManager.AddConfigItem((BaseConfigItem)constructor.Invoke(new object[] { entry, false }));
    }

    private static bool TryGetRange<T>(ConfigEntry<T> entry, out T min, out T max) where T : IComparable
    {
        if (entry.Description?.AcceptableValues is AcceptableValueRange<T> range)
        {
            min = range.MinValue;
            max = range.MaxValue;
            return true;
        }

        min = default!;
        max = default!;
        return false;
    }
}
