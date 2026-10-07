using System.Collections.Generic;
using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static readonly List<QuicksandTrigger> _waterVolumes = new();
    private static float _waterVolumesAt = -1f;

    private bool _cameraSubmerged;
    private float _underwaterWeight;

    internal bool IsUnderwater => _cameraSubmerged;
    internal float UnderwaterWeight => _underwaterWeight;

    private void UpdateUnderwater()
    {
        bool submerged = _camera != null && PointIsUnderwater(_camera.transform.position);
        if (submerged && playerHeldBy != null)
        {
            Camera? eye = playerHeldBy.gameplayCamera;
            submerged = eye != null && PointIsUnderwater(eye.transform.position);
        }
        _cameraSubmerged = submerged;

        if (submerged)
        {
            _underwaterWeight = 1f;
        }
        else if (_underwaterWeight < Constants.Visual.UnderwaterFadeFloor)
        {
            _underwaterWeight = 0f;
        }
        else
        {
            _underwaterWeight = Mathf.Lerp(_underwaterWeight, 0f, Constants.Visual.UnderwaterFade * Time.deltaTime);
        }
    }

    private static bool PointIsUnderwater(Vector3 point)
    {
        RefreshWaterVolumes();

        for (int i = 0; i < _waterVolumes.Count; i++)
        {
            QuicksandTrigger water = _waterVolumes[i];
            if (water == null || !water.isWater || !water.gameObject.activeInHierarchy) continue;

            Collider? volume = water.GetComponent<Collider>();
            if (volume == null || !volume.enabled) continue;
            if (!volume.bounds.Contains(point)) continue;
            if (volume.ClosestPoint(point) != point) continue;

            return true;
        }

        return false;
    }

    private static void RefreshWaterVolumes()
    {
        float now = Time.realtimeSinceStartup;
        if (_waterVolumesAt >= 0f && now - _waterVolumesAt < Constants.Visual.WaterRescanInterval) return;

        _waterVolumesAt = now;
        _waterVolumes.Clear();
        _waterVolumes.AddRange(Object.FindObjectsByType<QuicksandTrigger>(FindObjectsSortMode.None));
    }
}
