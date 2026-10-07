using System;
using Operator.Compat;
using UnityEngine;

namespace Operator.Cassette;

internal static class VanillaMonitor
{
    private const string MonitorPath = "Environment/HangarShip/ShipModels2b/MonitorWall/Cube.001";
    private const int ScreenMaterialIndex = 2;

    private static MeshRenderer? _renderer;
    private static Material? _restoreTo;
    private static Material? _clone;
    private static bool _missingLogged;

    internal static bool Available => FindRenderer() != null;

    internal static int MonitorCount => Available ? 1 : 0;

    internal static Transform? GetMonitorTransform(int index1Based)
    {
        MeshRenderer? mr = FindRenderer();
        return mr != null ? mr.transform : null;
    }

    internal static bool TryGetScreenCenter(int index1Based, out Vector3 world)
    {
        world = Vector3.zero;
        MeshRenderer? mr = FindRenderer();
        if (mr == null) return false;
        world = mr.transform.TransformPoint(GetScreenBounds(mr).center);
        return true;
    }

    private static Bounds FallbackBounds =>
        new Bounds(Constants.Monitor.VanillaScreenLocalCenter, Constants.Monitor.VanillaScreenLocalExtents * 2f);

    private static Bounds GetScreenBounds(MeshRenderer mr)
    {
        MeshFilter? filter = mr.GetComponent<MeshFilter>();
        Mesh? mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || ScreenMaterialIndex >= mesh.subMeshCount) return FallbackBounds;

        Bounds local;
        try
        {
            local = mesh.GetSubMesh(ScreenMaterialIndex).bounds;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Ship monitor submesh bounds are unreadable, using baked bounds. {ex.Message}");
            return FallbackBounds;
        }

        if (local.extents.x <= 0f || local.extents.y <= 0f || local.extents.z <= 0f) return FallbackBounds;
        return local;
    }

    private static MeshRenderer? FindRenderer()
    {
        if (_renderer != null) return _renderer;

        GameObject? monitor = GameObject.Find(MonitorPath);
        MeshRenderer? mr = monitor != null ? monitor.GetComponent<MeshRenderer>() : null;
        Material[]? mats = mr != null ? mr.sharedMaterials : null;

        if (mats == null || mats.Length <= ScreenMaterialIndex)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning("Could not find ships large monitor, 'transfer' wont work to play.");
            }
            return null;
        }

        _missingLogged = false;
        _renderer = mr;
        return _renderer;
    }

    internal static bool IsBound(int index1Based)
    {
        if (_clone == null) return false;

        MeshRenderer? mr = FindRenderer();
        if (mr == null) return false;

        try
        {
            Material[] mats = mr.sharedMaterials;
            if (mats == null || ScreenMaterialIndex >= mats.Length) return false;
            return mats[ScreenMaterialIndex] == _clone;
        }
        catch { return false; }
    }

    internal static bool Reapply(int index1Based)
    {
        if (_clone == null) return false;

        MeshRenderer? mr = FindRenderer();
        if (mr == null) return false;

        try
        {
            Material[] mats = mr.sharedMaterials;
            if (mats == null || ScreenMaterialIndex >= mats.Length) return false;
            if (mats[ScreenMaterialIndex] == _clone) return false;

            ScreenMaterial.SetOn(mr, ScreenMaterialIndex, _clone);
            return true;
        }
        catch { return false; }
    }

    internal static bool TryBind(int index1Based, RenderTexture rt)
    {
        MeshRenderer? mr = FindRenderer();
        if (mr == null) return false;

        try
        {
            _restoreTo ??= mr.sharedMaterials[ScreenMaterialIndex];

            Material screen = ScreenMaterial.CreateScreen(_restoreTo, rt);
            if (_clone != null) UnityEngine.Object.Destroy(_clone);
            _clone = screen;

            ScreenMaterial.SetOn(mr, ScreenMaterialIndex, screen);
            OpenBodyCamsCompat.ClaimScreen(mr);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to bind ship monitor: {ex.Message}");
            return false;
        }
    }

    internal static void Restore(int index1Based)
    {
        try
        {
            MeshRenderer? mr = FindRenderer();
            if (mr != null && _restoreTo != null)
            {
                ScreenMaterial.SetOn(mr, ScreenMaterialIndex, _restoreTo);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to restore ship monitor screen: {ex.Message}");
        }

        if (_clone != null)
        {
            UnityEngine.Object.Destroy(_clone);
            _clone = null;
        }
        _restoreTo = null;
    }
}
