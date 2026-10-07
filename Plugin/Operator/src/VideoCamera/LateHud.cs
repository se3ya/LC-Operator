using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Operator.Compat;

namespace Operator;

internal static class LateHud
{
    private static Camera[] _cameras = new Camera[16];
    private static Camera? _camera;
    private static HDAdditionalCameraData? _data;
    private static bool _skipped;
    private static bool _vrChecked;
    private static bool _vr;

    internal static void Request()
    {
        if (_data == null) Hold();
    }

    internal static void Render()
    {
        if (_data == null) return;

        Camera? ui = _camera;
        bool skipped = _skipped;
        Release();

        if (ui != null && skipped) ui.Render();
    }

    internal static void Release()
    {
        if (_data != null) _data.customRender -= Skip;
        _data = null;
        _camera = null;
        _skipped = false;
    }

    private static void Hold()
    {
        if (!_vrChecked)
        {
            _vrChecked = true;
            _vr = SoftDependency.IsLoaded(ModGUIDs.Lcvr);
        }
        if (_vr) return;

        HUDManager hud = HUDManager.Instance;
        if (hud == null) return;

        Camera ui = hud.UICamera;
        if (ui == null || !ui.isActiveAndEnabled || ui.targetTexture != null) return;
        if (IsCovered(ui)) return;

        var data = ui.GetComponent<HDAdditionalCameraData>();
        if (data == null || data.hasCustomRender) return;

        data.customRender += Skip;
        _data = data;
        _camera = ui;
    }

    private static void Skip(ScriptableRenderContext context, HDCamera camera) => _skipped = true;

    private static bool IsCovered(Camera ui)
    {
        int total = Camera.allCamerasCount;
        if (_cameras.Length < total) _cameras = new Camera[total * 2];

        int count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam != ui && cam != null && cam.targetTexture == null && cam.depth >= ui.depth) return true;
        }
        return false;
    }
}
