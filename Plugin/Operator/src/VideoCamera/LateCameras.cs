using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Operator.Compat;

namespace Operator;

internal static class LateCameras
{
    private static readonly List<Camera> _held = new();
    private static readonly HashSet<Camera> _skipped = new();
    private static int _heldFrame = -1;
    private static bool _wanted;
    private static bool _hooked;
    private static bool _vrChecked;
    private static bool _vr;
    private static bool _failureLogged;

    internal static void Request()
    {
        _wanted = true;
        if (_hooked) return;
        _hooked = true;
        RenderPipelineManager.beginContextRendering += OnBeginContext;
    }

    internal static void Render()
    {
        _wanted = false;
        if (_held.Count == 0) return;

        RemoveSkips();
        for (int i = 0; i < _held.Count; i++)
        {
            Camera cam = _held[i];
            if (cam == null || !_skipped.Contains(cam)) continue;
            try
            {
                cam.Render();
            }
            catch (System.Exception ex)
            {
                if (_failureLogged) continue;
                _failureLogged = true;
                Plugin.Log.LogError($"Failed to render {cam.name} after the camera feeds: {ex}");
            }
        }
        _held.Clear();
        _skipped.Clear();
    }

    internal static void Release()
    {
        _wanted = false;
        RemoveSkips();
        _held.Clear();
        _skipped.Clear();
    }

    private static void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (_held.Count > 0)
        {
            if (_heldFrame == Time.frameCount) return;
            RemoveSkips();
            _held.Clear();
            _skipped.Clear();
        }
        if (!_wanted || IsVr()) return;

        StartOfRound round = StartOfRound.Instance;
        Camera? view = round != null ? round.activeCamera : null;
        if (view == null) return;

        int start = cameras.IndexOf(view);
        if (start < 0) return;

        for (int i = start + 1; i < cameras.Count; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || cam.targetTexture == view.targetTexture) continue;
            HDAdditionalCameraData? data = GetFoglessData(cam);
            if (data == null) continue;
            data.customRender += Skip;
            _held.Add(cam);
        }
        if (_held.Count > 0) _heldFrame = Time.frameCount;
    }

    private static HDAdditionalCameraData? GetFoglessData(Camera cam)
    {
        if (cam == null || cam.cameraType != CameraType.Game || cam.targetTexture == null) return null;

        var data = cam.GetComponent<HDAdditionalCameraData>();
        if (data == null || data.hasCustomRender || data.fullscreenPassthrough) return null;

        HDCamera hd = HDCamera.GetOrCreate(cam);
        Fog? fog = hd.volumeStack?.GetComponent<Fog>();
        bool fogged = hd.frameSettings.IsEnabled(FrameSettingsField.Volumetrics) && fog != null && fog.enabled.value && fog.enableVolumetricFog.value;
        return fogged ? null : data;
    }

    private static void RemoveSkips()
    {
        for (int i = 0; i < _held.Count; i++)
        {
            Camera cam = _held[i];
            if (cam == null) continue;
            var data = cam.GetComponent<HDAdditionalCameraData>();
            if (data != null) data.customRender -= Skip;
        }
    }

    private static void Skip(ScriptableRenderContext context, HDCamera camera) => _skipped.Add(camera.camera);

    private static bool IsVr()
    {
        if (!_vrChecked)
        {
            _vrChecked = true;
            _vr = SoftDependency.IsLoaded(ModGUIDs.Lcvr);
        }
        return _vr;
    }
}
