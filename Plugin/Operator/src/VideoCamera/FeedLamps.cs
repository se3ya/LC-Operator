using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Operator;

internal sealed class FeedLamps
{
    private const int Slots = 4;
    private static readonly int DirId = Shader.PropertyToID("_LampDir");
    private static readonly int ColorId = Shader.PropertyToID("_LampColor");
    private static readonly int CountId = Shader.PropertyToID("_LampCount");

    private struct Entry
    {
        public Light? Light;
        public HDAdditionalLightData? Data;
    }

    private static readonly List<Entry> Cache = new();
    private static float _cacheAt = -100f;

    private readonly Entry[] _picks = new Entry[Slots];
    private readonly float[] _weight = new float[Slots];
    private readonly bool[] _visible = new bool[Slots];
    private readonly Vector4[] _dirs = new Vector4[Slots];
    private readonly Vector4[] _colors = new Vector4[Slots];
    private readonly List<(float score, Entry entry)> _ranked = new();
    private float _rankAt = -100f;
    private int _cursor;
    private int _count;
    private bool _outside;

    public void Update(Camera cam, Transform item, Transform? holder, bool outside)
    {
        float now = Time.time;
        if (now - _cacheAt > Constants.Cracks.LampRescan) RefreshCache(now);

        Transform view = cam.transform;
        Vector3 pos = view.position;
        int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
        _outside = outside;

        if (now - _rankAt > Constants.Cracks.LampRank) Rank(pos, item, view, holder, mask, now);
        CheckSight(pos, mask);
        Publish(view, pos);
    }

    public void Apply(Material mat)
    {
        mat.SetVectorArray(DirId, _dirs);
        mat.SetVectorArray(ColorId, _colors);
        mat.SetFloat(CountId, _count);
    }

    public void Clear(Material mat)
    {
        _count = 0;
        mat.SetFloat(CountId, 0f);
    }

    private static void RefreshCache(float now)
    {
        _cacheAt = now;
        Cache.Clear();
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Point && light.type != LightType.Spot && light.type != LightType.Directional) continue;
            string name = light.name;
            if (name.StartsWith("VC_") || name.StartsWith("OperatorIr")) continue;
            Cache.Add(new Entry { Light = light, Data = light.GetComponent<HDAdditionalLightData>() });
        }
    }

    private static bool IsOwn(Light light, Transform item, Transform view, Transform? holder)
    {
        Transform t = light.transform;
        return t.IsChildOf(item) || t.IsChildOf(view) || (holder != null && t.IsChildOf(holder));
    }

    private void Rank(Vector3 pos, Transform item, Transform view, Transform? holder, int mask, float now)
    {
        _rankAt = now;
        _ranked.Clear();
        Light? sun = TimeOfDay.Instance.sunDirect;
        foreach (Entry entry in Cache)
        {
            Light? light = entry.Light;
            if (light == null) continue;
            if (light.type == LightType.Directional && light != sun) continue;
            if (!Eval(entry, pos, _outside, out _, out _, out Vector3 color)) continue;
            if (IsOwn(light, item, view, holder)) continue;
            _ranked.Add(((color.x * 0.2126f) + (color.y * 0.7152f) + (color.z * 0.0722f), entry));
        }
        _ranked.Sort((a, b) => b.score.CompareTo(a.score));

        var old = (Entry[])_picks.Clone();
        var oldWeight = (float[])_weight.Clone();
        var oldVisible = (bool[])_visible.Clone();
        for (int i = 0; i < Slots; i++)
        {
            _picks[i] = i < _ranked.Count ? _ranked[i].entry : default;
            _weight[i] = 0f;
            _visible[i] = false;
            if (_picks[i].Light == null) continue;
            Light pick = _picks[i].Light!;
            int was = System.Array.FindIndex(old, o => o.Light == pick);
            if (was >= 0)
            {
                _weight[i] = oldWeight[was];
                _visible[i] = oldVisible[was];
            }
            else
            {
                Sight(i, pos, mask);
            }
        }
    }

    private void CheckSight(Vector3 pos, int mask)
    {
        for (int step = 0; step < Slots; step++)
        {
            _cursor = (_cursor + 1) % Slots;
            if (_picks[_cursor].Light == null) continue;
            Sight(_cursor, pos, mask);
            return;
        }
    }

    private void Sight(int slot, Vector3 pos, int mask)
    {
        Entry entry = _picks[slot];
        Light? light = entry.Light;
        if (light == null || !Eval(entry, pos, _outside, out Vector3 dir, out _, out _))
        {
            _visible[slot] = false;
            return;
        }

        if (light.type == LightType.Directional)
        {
            _visible[slot] = !Physics.Raycast(pos, dir, 500f, mask, QueryTriggerInteraction.Ignore);
            return;
        }

        Vector3 end = light.transform.position - (dir * 0.3f);
        _visible[slot] = !Physics.Linecast(pos, end, mask, QueryTriggerInteraction.Ignore);
    }

    private void Publish(Transform view, Vector3 pos)
    {
        int k = 0;
        float step = Time.deltaTime * Constants.Cracks.LampFade;
        float cap = Constants.Cracks.LampSoftCap;
        for (int i = 0; i < Slots; i++)
        {
            _dirs[i] = Vector4.zero;
            _colors[i] = Vector4.zero;
        }

        for (int i = 0; i < Slots; i++)
        {
            Entry entry = _picks[i];
            if (entry.Light == null) continue;
            if (!Eval(entry, pos, _outside, out Vector3 dir, out float radius, out Vector3 color))
            {
                _weight[i] = Mathf.MoveTowards(_weight[i], 0f, step);
                continue;
            }

            _weight[i] = Mathf.MoveTowards(_weight[i], _visible[i] ? 1f : 0f, step);
            if (_weight[i] <= 0.001f) continue;

            color *= _weight[i];
            float peak = Mathf.Max(color.x, Mathf.Max(color.y, color.z));
            if (peak > 0f) color *= cap * (1f - Mathf.Exp(-peak / cap)) / peak;

            Vector3 local = view.InverseTransformDirection(dir);
            _dirs[k] = new Vector4(local.x, local.y, local.z, radius);
            _colors[k] = new Vector4(color.x, color.y, color.z, 0f);
            k++;
        }
        _count = k;
    }

    private static bool Eval(Entry entry, Vector3 pos, bool outside, out Vector3 dir, out float radius, out Vector3 color)
    {
        dir = Vector3.forward;
        radius = 0f;
        color = Vector3.zero;
        Light? light = entry.Light;
        if (light == null || !light.isActiveAndEnabled) return false;

        float dimmer = entry.Data != null ? entry.Data.lightDimmer : 1f;
        float level;
        if (light.type == LightType.Directional)
        {
            if (!outside) return false;
            dir = -light.transform.forward;
            radius = Constants.Cracks.SunRadius * Mathf.Deg2Rad;
            level = (1f - Mathf.Exp(-light.intensity * dimmer / Constants.Cracks.SunRef)) * Constants.Cracks.SunGain;
        }
        else
        {
            Vector3 to = light.transform.position - pos;
            float dist = to.magnitude;
            if (dist < Constants.Cracks.LampNear || dist >= light.range) return false;
            dir = to / dist;
            if (light.type == LightType.Spot)
            {
                float cone = Mathf.Cos(((light.spotAngle * 0.5f) + 5f) * Mathf.Deg2Rad);
                if (Vector3.Dot(light.transform.forward, -dir) < cone) return false;
            }

            float x = dist / light.range;
            x *= x;
            x *= x;
            float fall = (1f - x) * (1f - x) / Mathf.Max(dist * dist, 1f);
            level = (1f - Mathf.Exp(-light.intensity * dimmer / Constants.Cracks.LampRef)) * fall * Constants.Cracks.LampGain;
            radius = Mathf.Clamp(Mathf.Atan(Constants.Cracks.LampSize / dist),
                Constants.Cracks.LampMinRadius * Mathf.Deg2Rad, Constants.Cracks.LampMaxRadius * Mathf.Deg2Rad);
        }

        if (level < Constants.Cracks.LampFloor) return false;
        Color tint = light.color.linear;
        if (light.useColorTemperature) tint *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
        color = new Vector3(tint.r, tint.g, tint.b) * level;
        return true;
    }
}
