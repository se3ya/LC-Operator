using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Operator.Cassette;

namespace Operator.Compat;

internal static class OpenBodyCamsCompat
{
    private const string Guid = ModGUIDs.OpenBodyCams;

    private static bool _initialized;
    private static bool _available;

    private static PropertyInfo? _mainBodyCamProp;
    private static FieldInfo? _monitorOnMaterialField;
    private static MethodInfo? _getCamera;
    private static EventInfo? _onRenderTextureCreated;

    private static FieldInfo? _monitorRendererField;
    private static MethodInfo? _setMonitorMaterial;
    private static MethodInfo? _updateScreenMaterial;
    private static bool _guardPatched;
    private static bool _patchFailed;

    internal static Renderer? SuppressedRenderer { get; private set; }

    private static object? _boundComponent;
    private static Material? _boundMaterial;
    private static Color _boundBaseColor = Color.white;
    private static readonly Dictionary<string, Color> _boundEmissiveColors = new();
    private static RenderTexture? _ourRt;
    private static Delegate? _rtCreatedHandler;

    internal static bool Available
    {
        get
        {
            Init();
            return _available;
        }
    }

    private static Renderer? _hiddenOverlay;
    private static bool _overlayWasHidden;

    private static void HideOverlay(Renderer owned)
    {
        Renderer? overlay = FindOverlay(owned);
        if (overlay == _hiddenOverlay) return;

        ShowOverlay();
        if (overlay == null) return;

        _overlayWasHidden = overlay.forceRenderingOff;
        overlay.forceRenderingOff = true;
        _hiddenOverlay = overlay;
    }

    private static void ShowOverlay()
    {
        Renderer? overlay = _hiddenOverlay;
        _hiddenOverlay = null;
        if (overlay == null) return;

        try { overlay.forceRenderingOff = _overlayWasHidden; }
        catch (Exception error) { Plugin.Log.LogDebug($"Could not restore OpenBodyCams overlay: {error.Message}"); }
    }

    private static Renderer? FindOverlay(Renderer owned)
    {
        Transform? parent = owned.transform.parent;
        if (parent == null) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Renderer? candidate = parent.GetChild(i).GetComponent<Renderer>();
            if (candidate == null || candidate == owned) continue;

            Material? material = candidate.sharedMaterial;
            bool looksRight = candidate.gameObject.name.StartsWith("BodyCamOverlayMesh", StringComparison.Ordinal)
                              || (material != null && material.name.StartsWith("BodyCamOverlayMeshMaterial", StringComparison.Ordinal));

            if (looksRight) return candidate;
        }

        return null;
    }

    internal static void ClaimScreen(Renderer? renderer)
    {
        Init();
        if (!_available || renderer == null) return;
        if (!TryPatchScreen()) return;
        SuppressedRenderer = renderer;
        HideOverlay(renderer);
    }

    internal static bool TookBodyCamScreen
    {
        get
        {
            Renderer? owned = SuppressedRenderer;
            if (owned == null || _monitorRendererField == null) return false;

            object? comp = MainBodyCam();
            if (comp == null) return false;

            try { return _monitorRendererField.GetValue(comp) as Renderer == owned; }
            catch { return false; }
        }
    }

    internal static void ReleaseScreen()
    {
        ShowOverlay();

        if (SuppressedRenderer == null) return;
        SuppressedRenderer = null;

        if (_updateScreenMaterial == null) return;
        try
        {
            object? component = _mainBodyCamProp?.GetValue(null);
            if (component != null) _updateScreenMaterial.Invoke(component, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to update OpenBodyCams screen: {ex.Message}");
        }
    }

    private static bool TryPatchScreen()
    {
        if (_guardPatched) return true;
        if (_patchFailed) return false;
        if (_setMonitorMaterial == null || _monitorRendererField == null) return false;

        try
        {
            var harmony = new HarmonyLib.Harmony($"{ModGUIDs.OpenBodyCams}.operatorscreenguard");
            harmony.Patch(
                _setMonitorMaterial,
                prefix: new HarmonyLib.HarmonyMethod(typeof(OpenBodyCamsCompat), nameof(SkipWhileOperatorOwnsScreen)));
            _guardPatched = true;
            Plugin.Log.LogInfo("Patched OpenBodyCams.");
            return true;
        }
        catch (Exception ex)
        {
            _patchFailed = true;
            Plugin.Log.LogWarning($"Failed to patch OBC: {ex.Message}");
            return false;
        }
    }

    private static bool SkipWhileOperatorOwnsScreen(object __instance)
    {
        Renderer? owned = SuppressedRenderer;
        if (owned == null || _monitorRendererField == null) return true;

        try
        {
            return _monitorRendererField.GetValue(__instance) as Renderer != owned;
        }
        catch
        {
            return true;
        }
    }

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Type? apiType = SoftDependency.FindType(Guid, "OpenBodyCams.API.BodyCam");
            Type? componentType = SoftDependency.FindType(Guid, "OpenBodyCams.BodyCamComponent");
            if (apiType == null || componentType == null) return;

            _mainBodyCamProp = apiType.GetProperty("MainBodyCam", BindingFlags.Public | BindingFlags.Static);
            _monitorOnMaterialField = componentType.GetField("MonitorOnMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
            _getCamera = componentType.GetMethod("GetCamera", BindingFlags.Public | BindingFlags.Instance);
            _onRenderTextureCreated = componentType.GetEvent("OnRenderTextureCreated", BindingFlags.Public | BindingFlags.Instance);

            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _monitorRendererField = componentType.GetField("MonitorRenderer", any);
            _setMonitorMaterial = componentType.GetMethod("SetMonitorMaterial", any, null, new[] { typeof(Material) }, null);
            _updateScreenMaterial = componentType.GetMethod("UpdateScreenMaterial", any, null, Type.EmptyTypes, null);

            _available = _mainBodyCamProp != null && _monitorOnMaterialField != null;
            if (_available)
            {
                Plugin.Log.LogInfo("OpenBodyCams found.");
            }
        }
        catch (Exception ex)
        {
            _available = false;
            Plugin.Log.LogWarning($"Failed to hook OBC monitor: {ex.Message}");
        }
    }

    private static object? MainBodyCam()
    {
        if (!Available || _mainBodyCamProp == null) return null;
        try { return _mainBodyCamProp.GetValue(null); }
        catch { return null; }
    }

    internal static bool TryBind(RenderTexture rt)
    {
        object? comp = MainBodyCam();
        if (comp == null || _monitorOnMaterialField == null) return false;

        try
        {
            var mat = _monitorOnMaterialField.GetValue(comp) as Material;
            if (mat == null) return false;

            if (_boundMaterial != null && _boundMaterial != mat)
            {
                Restore();
            }

            bool freshBind = _boundMaterial != mat;

            if (_rtCreatedHandler != null) UnhookRtCreated();

            if (freshBind) SaveOriginalColors(mat);

            _boundComponent = comp;
            _boundMaterial = mat;
            _ourRt = rt;

            ScreenMaterial.ApplyTexture(mat, rt);
            mat.color = Color.white;
            foreach (string emissive in ScreenMaterial.EmissiveColorProperties)
            {
                if (mat.HasProperty(emissive)) mat.SetColor(emissive, Color.black);
            }

            HookRtCreated(comp);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to show recording on OBC monitor: {ex.Message}");
            Restore();
            return false;
        }
    }

    internal static void Restore()
    {
        try
        {
            UnhookRtCreated();

            if (_boundMaterial != null)
            {
                _boundMaterial.color = _boundBaseColor;
                RestoreEmissiveColors(_boundMaterial);

                Texture? live = null;
                try
                {
                    if (_boundComponent != null && _getCamera != null)
                    {
                        var cam = _getCamera.Invoke(_boundComponent, null) as Camera;
                        live = cam != null ? cam.targetTexture : null;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"Could not read live body cam texture: {ex.Message}");
                }
                ScreenMaterial.RestoreTexture(_boundMaterial, live);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to restore OpenBodyCams screen: {ex.Message}");
        }
        finally
        {
            _boundComponent = null;
            _boundMaterial = null;
            _ourRt = null;
            _boundEmissiveColors.Clear();
        }
    }

    private static void SaveOriginalColors(Material mat)
    {
        _boundBaseColor = mat.color;
        _boundEmissiveColors.Clear();
        foreach (string emissiveColor in ScreenMaterial.EmissiveColorProperties)
        {
            if (mat.HasProperty(emissiveColor)) _boundEmissiveColors[emissiveColor] = mat.GetColor(emissiveColor);
        }
    }

    private static void RestoreEmissiveColors(Material mat)
    {
        foreach (KeyValuePair<string, Color> entry in _boundEmissiveColors)
        {
            if (mat.HasProperty(entry.Key)) mat.SetColor(entry.Key, entry.Value);
        }
    }

    private static void HookRtCreated(object comp)
    {
        if (_onRenderTextureCreated == null) return;
        try
        {
            _rtCreatedHandler = new Action<RenderTexture>(OnObcRtCreated);
            _onRenderTextureCreated.AddEventHandler(comp, _rtCreatedHandler);
        }
        catch (Exception ex)
        {
            _rtCreatedHandler = null;
            Plugin.Log.LogDebug($"Failed to hook OpenBodyCams render texture event: {ex.Message}");
        }
    }

    private static void UnhookRtCreated()
    {
        if (_onRenderTextureCreated == null || _rtCreatedHandler == null || _boundComponent == null) return;
        try { _onRenderTextureCreated.RemoveEventHandler(_boundComponent, _rtCreatedHandler); }
        catch { }
        _rtCreatedHandler = null;
    }

    private static void OnObcRtCreated(RenderTexture _)
    {
        if (_boundMaterial != null && _ourRt != null)
        {
            ScreenMaterial.ApplyTexture(_boundMaterial, _ourRt);
            _boundMaterial.color = Color.white;
        }
    }
}
