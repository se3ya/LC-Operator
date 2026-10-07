using System;
using System.Reflection;
using UnityEngine;
using Operator.Cassette;

namespace Operator.Compat;

internal static class GeneralImprovementsCompat
{
    private const string Guid = ModGUIDs.GeneralImprovements;

    private static bool _initialized;
    private static bool _available;

    private static Type? _monitorsApiType;
    private static PropertyInfo? _numMonitorsActiveProp;
    private static MethodInfo? _getMonitorAtIndex;

    private static PropertyInfo? _meshRendererProp;
    private static PropertyInfo? _screenMaterialIndexProp;
    private static PropertyInfo? _assignedMaterialProp;
    private static PropertyInfo? _overwrittenMaterialProp;
    private static MethodInfo? _queueRender;

    private static readonly System.Collections.Generic.Dictionary<int, Material> _clones = new();
    private static readonly System.Collections.Generic.Dictionary<int, Material> _restoreTo = new();

    internal static bool Available
    {
        get
        {
            Init();
            return _available;
        }
    }

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _monitorsApiType = SoftDependency.FindType(Guid, "GeneralImprovements.API.MonitorsAPI");
            if (_monitorsApiType == null) return;

            _numMonitorsActiveProp = _monitorsApiType.GetProperty("NumMonitorsActive", BindingFlags.Public | BindingFlags.Static);
            _getMonitorAtIndex = _monitorsApiType.GetMethod("GetMonitorAtIndex", BindingFlags.Public | BindingFlags.Static);

            Type? monitorInfoType = _monitorsApiType.GetNestedType("MonitorInfo", BindingFlags.Public);
            if (monitorInfoType == null) return;

            _meshRendererProp = monitorInfoType.GetProperty("MeshRenderer", BindingFlags.Public | BindingFlags.Instance);
            _screenMaterialIndexProp = monitorInfoType.GetProperty("ScreenMaterialIndex", BindingFlags.Public | BindingFlags.Instance);
            _assignedMaterialProp = monitorInfoType.GetProperty("AssignedMaterial", BindingFlags.Public | BindingFlags.Instance);
            _overwrittenMaterialProp = monitorInfoType.GetProperty("OverwrittenMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
            _queueRender = monitorInfoType.GetMethod("QueueRender", BindingFlags.Public | BindingFlags.Instance);

            _available = _getMonitorAtIndex != null && _meshRendererProp != null && _screenMaterialIndexProp != null;
            if (_available)
            {
                Plugin.Log.LogInfo("GeneralImprovements found.");
            }
        }
        catch (Exception ex)
        {
            _available = false;
            Plugin.Log.LogWarning($"Failed to hook GeneralImprovements monitors: {ex.Message}");
        }
    }

    internal static int MonitorCount
    {
        get
        {
            if (!Available || _numMonitorsActiveProp == null) return 0;
            try { return (int)_numMonitorsActiveProp.GetValue(null); }
            catch { return 0; }
        }
    }

    private static object? GetMonitor(int index1Based)
    {
        if (!Available || _getMonitorAtIndex == null) return null;
        int index = index1Based <= 0 ? LastMonitorIndex() : index1Based - 1;
        if (index < 0) return null;
        try { return _getMonitorAtIndex.Invoke(null, new object[] { index }); }
        catch { return null; }
    }

    private static int LastMonitorIndex()
    {
        if (_getMonitorAtIndex == null) return -1;
        int last = -1;
        for (int i = 0; i < Constants.Monitor.ScanLimit; i++)
        {
            object? monitor;
            try { monitor = _getMonitorAtIndex.Invoke(null, new object[] { i }); }
            catch { break; }
            if (monitor == null) break;
            last = i;
        }
        return last;
    }

    internal static Transform? GetMonitorTransform(int index1Based)
    {
        object? monitor = GetMonitor(index1Based);
        if (monitor == null || _meshRendererProp == null) return null;
        try
        {
            var mr = _meshRendererProp.GetValue(monitor) as MeshRenderer;
            return mr != null ? mr.transform : null;
        }
        catch { return null; }
    }

    internal static bool TryGetScreenCenter(int index1Based, out Vector3 world)
    {
        world = Vector3.zero;
        object? monitor = GetMonitor(index1Based);
        if (monitor == null || _meshRendererProp == null) return false;
        try
        {
            var mr = _meshRendererProp.GetValue(monitor) as MeshRenderer;
            if (mr == null) return false;
            world = mr.transform.TransformPoint(mr.localBounds.center);
            return true;
        }
        catch { return false; }
    }

    internal static bool TryBind(int index1Based, RenderTexture rt)
    {
        object? monitor = GetMonitor(index1Based);
        if (monitor == null || _meshRendererProp == null || _screenMaterialIndexProp == null) return false;

        try
        {
            var mr = _meshRendererProp.GetValue(monitor) as MeshRenderer;
            if (mr == null) return false;

            int matIdx = (int)_screenMaterialIndexProp.GetValue(monitor);
            Material[] mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            matIdx = Mathf.Clamp(matIdx, 0, mats.Length - 1);

            Material? restoreTo = _assignedMaterialProp?.GetValue(monitor) as Material;
            _restoreTo[index1Based] = restoreTo != null ? restoreTo : mats[matIdx];

            Material baseMat = mats[matIdx];
            Material clone = ScreenMaterial.CreateScreen(baseMat, rt);

            if (_clones.TryGetValue(index1Based, out Material? oldClone) && oldClone != null)
            {
                UnityEngine.Object.Destroy(oldClone);
            }
            _clones[index1Based] = clone;

            ScreenMaterial.SetOn(mr, matIdx, clone);
            OpenBodyCamsCompat.ClaimScreen(mr);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to show recording on GeneralImprovements monitor {index1Based}: {ex.Message}");
            return false;
        }
    }

    internal static bool IsBound(int index1Based)
    {
        if (!_clones.TryGetValue(index1Based, out Material? clone) || clone == null) return false;

        object? monitor = GetMonitor(index1Based);
        if (monitor == null || _meshRendererProp == null || _screenMaterialIndexProp == null) return false;

        try
        {
            var mr = _meshRendererProp.GetValue(monitor) as MeshRenderer;
            if (mr == null) return false;

            int matIdx = (int)_screenMaterialIndexProp.GetValue(monitor);
            Material[] mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            matIdx = Mathf.Clamp(matIdx, 0, mats.Length - 1);
            return mats[matIdx] == clone;
        }
        catch { return false; }
    }

    internal static bool Reapply(int index1Based)
    {
        if (!_clones.TryGetValue(index1Based, out Material? clone) || clone == null) return false;

        object? monitor = GetMonitor(index1Based);
        if (monitor == null || _meshRendererProp == null || _screenMaterialIndexProp == null) return false;

        try
        {
            var mr = _meshRendererProp.GetValue(monitor) as MeshRenderer;
            if (mr == null) return false;

            int matIdx = (int)_screenMaterialIndexProp.GetValue(monitor);
            Material[] mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            matIdx = Mathf.Clamp(matIdx, 0, mats.Length - 1);

            if (mats[matIdx] == clone) return false;

            ScreenMaterial.SetOn(mr, matIdx, clone);
            return true;
        }
        catch { return false; }
    }

    internal static void Restore(int index1Based)
    {
        object? monitor = GetMonitor(index1Based);

        try
        {
            if (monitor != null)
            {
                _overwrittenMaterialProp?.SetValue(monitor, null);

                var mr = _meshRendererProp?.GetValue(monitor) as MeshRenderer;
                if (mr != null && _screenMaterialIndexProp != null)
                {
                    int matIdx = (int)_screenMaterialIndexProp.GetValue(monitor);
                    Material[] mats = mr.sharedMaterials;
                    if (mats != null && mats.Length > 0)
                    {
                        matIdx = Mathf.Clamp(matIdx, 0, mats.Length - 1);
                        Material? assigned = _assignedMaterialProp?.GetValue(monitor) as Material;
                        Material? back = assigned != null
                            ? assigned
                            : _restoreTo.TryGetValue(index1Based, out Material? stored) ? stored : null;
                        if (back != null)
                        {
                            mats[matIdx] = back;
                            mr.sharedMaterials = mats;
                        }
                    }
                }

                try { _queueRender?.Invoke(monitor, new object[] { string.Empty }); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to restore GeneralImprovements monitor {index1Based}: {ex.Message}");
        }
        finally
        {
            if (_clones.TryGetValue(index1Based, out Material? clone) && clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }
            _clones.Remove(index1Based);
            _restoreTo.Remove(index1Based);
        }
    }
}
