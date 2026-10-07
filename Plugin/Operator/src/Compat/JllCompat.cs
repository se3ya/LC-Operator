using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Operator.Compat;

internal static class JllCompat
{
    private const string Guid = ModGUIDs.Jll;

    private static bool _initialized;
    private static bool _available;

    private static Type? _attachedObjectType;
    private static FieldInfo? _targetField;

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _attachedObjectType = SoftDependency.FindType(Guid, "JLL.Components.JClientAttatchedObject");
            if (_attachedObjectType == null) return;

            _targetField = _attachedObjectType.GetField("target", BindingFlags.Public | BindingFlags.Instance);
            _available = _targetField != null;

            if (_available)
            {
                Plugin.Log.LogInfo("JLL found.");
            }
        }
        catch (Exception ex)
        {
            _available = false;
            Plugin.Log.LogWarning($"Failed to hook JLL weather effects: {ex.Message}");
        }
    }

    internal static void CollectAttachedTargets(GameObject root, List<GameObject> into)
    {
        Init();
        if (!_available || _attachedObjectType == null || _targetField == null || root == null) return;

        try
        {
            Component[] attachers = root.GetComponentsInChildren(_attachedObjectType, true);
            for (int i = 0; i < attachers.Length; i++)
            {
                if (attachers[i] == null) continue;
                if (_targetField.GetValue(attachers[i]) is not GameObject target) continue;
                if (target == null || into.Contains(target)) continue;
                into.Add(target);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to read JLL weather targets: {ex.Message}");
        }
    }
}
