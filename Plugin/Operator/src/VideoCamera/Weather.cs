using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Operator.Compat;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private float _cameraCoveredAt = -1f;
    private bool _cameraCovered;

    private bool IsCovered()
    {
        if (IsInside || isInShipRoom) return true;

        Camera? cam = ActiveCamera;
        if (cam == null) return true;

        float now = Time.time;
        if (_cameraCoveredAt >= 0f && now - _cameraCoveredAt < Constants.Visual.CoverCheckInterval)
        {
            return _cameraCovered;
        }

        _cameraCoveredAt = now;

        int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
        Vector3 p = cam.transform.position;
        _cameraCovered = Physics.Raycast(
            p,
            Vector3.up,
            Constants.Visual.CoverCheckDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        return _cameraCovered;
    }

    private readonly struct WeatherAnchor
    {
        public readonly GameObject Target;
        public readonly bool CanActivate;

        public WeatherAnchor(GameObject target, bool canActivate)
        {
            Target = target;
            CanActivate = canActivate;
        }
    }

    private struct WeatherAnchorState
    {
        public GameObject Target;
        public Vector3 Position;
        public bool WasActive;
    }

    private struct IndoorFogState
    {
        public bool Valid;
        public Vector3 Position;
        public bool Enabled;
        public float MeanFreePath;
    }

    private struct MeteorSpriteState
    {
        public Transform Target;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private readonly List<WeatherAnchor> _weatherAnchors = new();
    private readonly List<WeatherAnchorState> _weatherRestore = new();
    private readonly List<int> _weatherLiveIndices = new();
    private readonly List<int> _weatherHeld = new();
    private readonly List<bool> _weatherHeldWasEnabled = new();
    private readonly List<AudioSource> _weatherHeldAudio = new();
    private readonly List<Renderer> _weatherHeldRenderers = new();
    private readonly List<bool> _weatherHeldRendererOff = new();
    private readonly List<LocalVolumetricFog> _weatherHeldFogs = new();
    private readonly List<bool> _weatherHeldFogEnabled = new();
    private readonly List<Light> _weatherHeldLights = new();
    private readonly List<bool> _weatherHeldLightEnabled = new();
    private readonly List<HDAdditionalLightData?> _weatherHeldLightData = new();
    private readonly List<Vector2> _weatherHeldLightDimmers = new();
    private static readonly Dictionary<Light, VideoCameraItem> _heldLightOwners = new();
    private readonly List<Vector3> _weatherAudioPositions = new();
    private readonly List<MeteorSpriteState> _meteorRestore = new();
    private readonly List<GameObject> _weatherClones = new();
    private readonly List<GameObject> _weatherCloneSources = new();
    private readonly List<GameObject> _attachedScratch = new();
    private readonly List<GameObject> _weatherHeldObjects = new();
    private readonly List<Volume> _weatherHeldVolumes = new();
    private readonly List<float> _weatherHeldVolumeWeights = new();
    private LevelWeatherType _scannedWeather = (LevelWeatherType)(-2);
    private float _weatherScanNextTime;
    private bool _weatherScanned;
    private bool _weatherHoldActive;
    private string _weatherHoldReport = string.Empty;
    private float _weatherHoldRetryTime;

    private void RefreshWeatherAnchors()
    {
        TimeOfDay time = TimeOfDay.Instance;
        if (time.effects == null) return;

        PlayerControllerB? reference = GetViewer();
        bool viewerOutside = reference != null && !reference.isInsideFactory;
        bool weatherChanged = time.currentLevelWeather != _scannedWeather;

        if (!weatherChanged)
        {
            if (_weatherScanned && Time.realtimeSinceStartup < _weatherScanNextTime) return;
            if (_weatherScanned && !viewerOutside) return;
        }

        if (weatherChanged && _weatherHoldActive) ReleaseWeatherHold();

        _weatherScanned = true;
        _scannedWeather = time.currentLevelWeather;
        _weatherScanNextTime = Time.realtimeSinceStartup + Constants.Visual.WeatherScanInterval;

        _weatherAnchors.Clear();
        if (viewerOutside || weatherChanged) _weatherLiveIndices.Clear();
        int current = (int)time.currentLevelWeather;

        for (int i = 0; i < time.effects.Length; i++)
        {
            WeatherEffect effect = time.effects[i];
            if (effect == null) continue;

            if ((viewerOutside || weatherChanged) && effect.effectEnabled) _weatherLiveIndices.Add(i);

            GameObject permanent = effect.effectPermanentObject;
            if (permanent != null)
            {
                if (permanent.activeInHierarchy && HasPlayerAnchor(permanent))
                {
                    _weatherAnchors.Add(new WeatherAnchor(permanent, false));
                }

                _attachedScratch.Clear();
                JllCompat.CollectAttachedTargets(permanent, _attachedScratch);
                for (int a = 0; a < _attachedScratch.Count; a++)
                {
                    GameObject attached = _attachedScratch[a];
                    if (attached == null) continue;
                    _weatherAnchors.Add(new WeatherAnchor(attached, CanHoldWeatherObject(attached, out _)));
                }
            }

            GameObject moving = effect.effectObject;
            if (moving == null) continue;
            if (!effect.effectEnabled && i != current) continue;

            if (moving.activeInHierarchy || HasPlayerAnchor(moving) || IsVisualOnly(moving))
            {
                _weatherAnchors.Add(new WeatherAnchor(moving, IsVisualOnly(moving)));
            }
        }

        if ((viewerOutside || weatherChanged) && _weatherLiveIndices.Count == 0 && current >= 0 && current < time.effects.Length)
        {
            _weatherLiveIndices.Add(current);
        }
    }

    private static bool HasPlayerAnchor(GameObject target)
    {
        return target.GetComponentInChildren<MatchLocalPlayerPosition>(true) != null;
    }

    private static bool IsVisualOnly(GameObject target)
    {
        Component[] components = target.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) return false;
            if (component is Transform) continue;
            if (component is LocalVolumetricFog) continue;
            if (component is Light) continue;
            if (component is HDAdditionalLightData) continue;
            if (component is MeshFilter) continue;
            if (component is MeshRenderer) continue;
            if (component is Volume) continue;
            if (component is Collider) continue;
            return false;
        }

        return true;
    }

    private static readonly Dictionary<System.Type, bool> _toggleSafeTypes = new();

    private static bool IsToggleSafe(System.Type type)
    {
        if (_toggleSafeTypes.TryGetValue(type, out bool cached)) return cached;

        bool safe = true;
        if (type != typeof(LocalVolumetricFog) && type != typeof(Volume) && type != typeof(HDAdditionalLightData))
        {
            const System.Reflection.BindingFlags declared =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            for (System.Type? walk = type; walk != null && walk != typeof(MonoBehaviour); walk = walk.BaseType)
            {
                try
                {
                    if (walk.GetMethod("OnEnable", declared) != null || walk.GetMethod("OnDisable", declared) != null)
                    {
                        safe = false;
                        break;
                    }
                }
                catch
                {
                    safe = false;
                    break;
                }
            }
        }

        _toggleSafeTypes[type] = safe;
        return safe;
    }

    private static bool CanHoldWeatherObject(GameObject target, out string blocker)
    {
        blocker = string.Empty;
        MonoBehaviour[] scripts = target.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++)
        {
            MonoBehaviour script = scripts[i];
            if (script == null) continue;

            System.Type type = script.GetType();
            if (IsToggleSafe(type)) continue;

            blocker = type.Name;
            return false;
        }

        return true;
    }

    private void LogWeatherHold(string message)
    {
        if (string.Equals(_weatherHoldReport, message, System.StringComparison.Ordinal)) return;
        _weatherHoldReport = message;
        Plugin.Log.LogDebug(message);
    }

    private bool ShouldHoldWeather(bool feedWanted)
    {
        if (!VideoCameraConfig.WeatherPerspective.Value) return false;

        PlayerControllerB? reference = GetViewer();
        bool viewerInside = reference != null && reference.isInsideFactory;
        if (!viewerInside) return false;

        if (!feedWanted)
        {
            LogWeatherHold("Weather hold skipped because feed is not rendering");
            return false;
        }

        if (IsInside)
        {
            LogWeatherHold("Weather hold skipped because camera reads as inside");
            return false;
        }

        if (ActiveCamera == null) return false;
        return true;
    }

    private void UpdateWeatherHold(bool feedWanted)
    {
        if (!ShouldHoldWeather(feedWanted))
        {
            ReleaseWeatherHold();
            return;
        }

        TimeOfDay time = TimeOfDay.Instance;
        Camera? active = ActiveCamera;
        if (time.effects == null || active == null) return;

        if (!_weatherHoldActive)
        {
            if (Time.realtimeSinceStartup < _weatherHoldRetryTime) return;

            _weatherHeld.Clear();
            _weatherHeldWasEnabled.Clear();
            string blocked = string.Empty;
            for (int i = 0; i < _weatherLiveIndices.Count; i++)
            {
                int index = _weatherLiveIndices[i];
                if (index < 0 || index >= time.effects.Length) continue;

                WeatherEffect effect = time.effects[index];
                if (effect == null || effect.effectObject == null) continue;

                if (!CanHoldWeatherObject(effect.effectObject, out string blocker))
                {
                    blocked = string.IsNullOrEmpty(blocked) ? blocker : $"{blocked}, {blocker}";
                    continue;
                }

                _weatherHeld.Add(index);
                _weatherHeldWasEnabled.Add(effect.effectEnabled);
            }

            if (_weatherHeld.Count == 0)
            {
                _weatherHoldRetryTime = Time.realtimeSinceStartup + Constants.Visual.WeatherRetryDelay;
                LogWeatherHold(string.IsNullOrEmpty(blocked)
                    ? "No weather effect can follow camera"
                    : $"Weather cannot follow camera because of {blocked}");
                return;
            }

            LogWeatherHold($"Weather is following camera from {_weatherHeld.Count} effects");

            _weatherHeldAudio.Clear();
            _weatherAudioPositions.Clear();
            for (int i = 0; i < _weatherHeld.Count; i++)
            {
                WeatherEffect effect = time.effects[_weatherHeld[i]];
                if (effect == null || effect.effectObject == null) continue;
                GameObject moving = effect.effectObject;

                AudioSource[] sources = moving.GetComponentsInChildren<AudioSource>(true);
                for (int s = 0; s < sources.Length; s++)
                {
                    if (sources[s] == null) continue;
                    _weatherHeldAudio.Add(sources[s]);
                    _weatherAudioPositions.Add(sources[s].transform.localPosition);
                }
            }

            CollectHeldWeatherObjects(time);
            CacheHeldWeatherVisuals(time);
            BuildWeatherClones(time);
            _weatherHoldActive = true;
        }

        Vector3 target = active.transform.position;
        for (int i = 0; i < _weatherHeld.Count; i++)
        {
            int index = _weatherHeld[i];
            if (index < 0 || index >= time.effects.Length) continue;

            WeatherEffect effect = time.effects[index];
            if (effect == null) continue;

            effect.effectEnabled = true;

            GameObject moving = effect.effectObject;
            if (moving == null) continue;
            if (!moving.activeSelf) moving.SetActive(true);
            moving.transform.position = target;
        }

        for (int i = 0; i < _weatherHeldObjects.Count; i++)
        {
            GameObject held = _weatherHeldObjects[i];
            if (held == null) continue;
            if (!held.activeSelf) held.SetActive(true);
            held.transform.position = target;
        }

        for (int i = 0; i < _weatherClones.Count; i++)
        {
            GameObject clone = _weatherClones[i];
            if (clone == null) continue;
            clone.transform.position = target;
        }

        PinWeatherAudio();
        SetHeldWeatherVisible(false);
    }

    private void PinWeatherAudio()
    {
        PlayerControllerB? listener = GetViewer();
        if (listener == null) return;

        Vector3 ears = listener.transform.position;
        for (int i = 0; i < _weatherHeldAudio.Count; i++)
        {
            AudioSource source = _weatherHeldAudio[i];
            if (source == null) continue;
            source.transform.position = ears;
        }
    }

    private void BuildWeatherClones(TimeOfDay time)
    {
        DestroyWeatherClones();

        for (int i = 0; i < _weatherHeld.Count; i++)
        {
            WeatherEffect effect = time.effects[_weatherHeld[i]];
            if (effect == null || effect.effectObject == null) continue;

            GameObject source = effect.effectObject;
            if (source.GetComponentInChildren<ParticleSystem>(true) == null) continue;
            if (AlreadyCloned(source)) continue;

            GameObject holder = new($"{source.name} (camera)");
            holder.SetActive(false);

            GameObject clone = Instantiate(source, holder.transform, false);
            clone.name = source.name;

            MonoBehaviour[] scripts = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int m = 0; m < scripts.Length; m++)
            {
                if (scripts[m] != null) Destroy(scripts[m]);
            }

            AudioSource[] sources = clone.GetComponentsInChildren<AudioSource>(true);
            for (int a = 0; a < sources.Length; a++)
            {
                if (sources[a] == null) continue;
                sources[a].playOnAwake = false;
                sources[a].mute = true;
                sources[a].enabled = false;
            }

            ApplyLayerRecursively(holder.transform, Constants.Visual.WeatherCameraLayer);
            clone.SetActive(true);
            holder.SetActive(true);
            _weatherClones.Add(holder);
            _weatherCloneSources.Add(source);
        }

        if (_weatherClones.Count > 0)
        {
            Plugin.Log.LogDebug($"Built {_weatherClones.Count} weather copies for camera.");
        }
    }

    private bool AlreadyCloned(GameObject source)
    {
        for (int i = 0; i < _weatherCloneSources.Count; i++)
        {
            if (_weatherCloneSources[i] == source) return true;
        }

        return false;
    }

    private static void ApplyLayerRecursively(Transform target, int layer)
    {
        target.gameObject.layer = layer;
        for (int i = 0; i < target.childCount; i++)
        {
            ApplyLayerRecursively(target.GetChild(i), layer);
        }
    }

    private void DestroyWeatherClones()
    {
        for (int i = 0; i < _weatherClones.Count; i++)
        {
            if (_weatherClones[i] != null) Destroy(_weatherClones[i]);
        }

        _weatherClones.Clear();
        _weatherCloneSources.Clear();
    }

    private void CollectHeldWeatherObjects(TimeOfDay time)
    {
        _weatherHeldObjects.Clear();

        for (int i = 0; i < _weatherLiveIndices.Count; i++)
        {
            int index = _weatherLiveIndices[i];
            if (index < 0 || index >= time.effects.Length) continue;

            WeatherEffect effect = time.effects[index];
            if (effect == null || effect.effectPermanentObject == null) continue;

            _attachedScratch.Clear();
            JllCompat.CollectAttachedTargets(effect.effectPermanentObject, _attachedScratch);
            for (int a = 0; a < _attachedScratch.Count; a++)
            {
                GameObject attached = _attachedScratch[a];
                if (attached == null || _weatherHeldObjects.Contains(attached)) continue;
                if (!CanHoldWeatherObject(attached, out string blocker))
                {
                    LogWeatherHold($"Weather cannot follow camera because of {blocker}");
                    continue;
                }

                _weatherHeldObjects.Add(attached);
            }
        }

        if (_weatherHeldObjects.Count > 0)
        {
            Plugin.Log.LogDebug($"Holding {_weatherHeldObjects.Count} attached weather objects for camera.");
        }
    }

    private void CacheHeldWeatherVisuals(TimeOfDay time)
    {
        _weatherHeldRenderers.Clear();
        _weatherHeldRendererOff.Clear();
        _weatherHeldFogs.Clear();
        _weatherHeldFogEnabled.Clear();
        _weatherHeldLights.Clear();
        _weatherHeldLightEnabled.Clear();
        _weatherHeldLightData.Clear();
        _weatherHeldLightDimmers.Clear();
        _weatherHeldVolumes.Clear();
        _weatherHeldVolumeWeights.Clear();

        for (int i = 0; i < _weatherHeld.Count + _weatherHeldObjects.Count; i++)
        {
            GameObject moving;
            if (i < _weatherHeld.Count)
            {
                WeatherEffect effect = time.effects[_weatherHeld[i]];
                if (effect == null || effect.effectObject == null) continue;
                moving = effect.effectObject;
            }
            else
            {
                moving = _weatherHeldObjects[i - _weatherHeld.Count];
                if (moving == null) continue;
            }

            Volume[] volumes = moving.GetComponentsInChildren<Volume>(true);
            for (int v = 0; v < volumes.Length; v++)
            {
                if (volumes[v] == null) continue;
                _weatherHeldVolumes.Add(volumes[v]);
                _weatherHeldVolumeWeights.Add(volumes[v].weight);
            }

            Renderer[] renderers = moving.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                _weatherHeldRenderers.Add(renderers[r]);
                _weatherHeldRendererOff.Add(renderers[r].forceRenderingOff);
            }

            LocalVolumetricFog[] fogs = moving.GetComponentsInChildren<LocalVolumetricFog>(true);
            for (int f = 0; f < fogs.Length; f++)
            {
                if (fogs[f] == null) continue;
                _weatherHeldFogs.Add(fogs[f]);
                _weatherHeldFogEnabled.Add(fogs[f].enabled);
            }

            Light[] lights = moving.GetComponentsInChildren<Light>(true);
            for (int l = 0; l < lights.Length; l++)
            {
                if (lights[l] == null) continue;
                if (_heldLightOwners.TryGetValue(lights[l], out VideoCameraItem owner) && owner != null && owner != this) continue;
                _heldLightOwners[lights[l]] = this;
                _weatherHeldLights.Add(lights[l]);
                _weatherHeldLightEnabled.Add(lights[l].enabled);
                HDAdditionalLightData? data = lights[l].GetComponent<HDAdditionalLightData>();
                _weatherHeldLightData.Add(data);
                _weatherHeldLightDimmers.Add(data != null ? new Vector2(data.lightDimmer, GetVolumetricDimmer(data)) : Vector2.one);
            }
        }
    }

    private void SetHeldWeatherVisible(bool visible)
    {
        for (int i = 0; i < _weatherHeldRenderers.Count; i++)
        {
            Renderer renderer = _weatherHeldRenderers[i];
            if (renderer == null) continue;
            renderer.forceRenderingOff = !visible;
        }

        for (int i = 0; i < _weatherHeldFogs.Count; i++)
        {
            LocalVolumetricFog fog = _weatherHeldFogs[i];
            if (fog == null) continue;
            fog.enabled = visible;
        }

        for (int i = 0; i < _weatherHeldLights.Count; i++)
        {
            Light light = _weatherHeldLights[i];
            if (light == null) continue;
            HDAdditionalLightData? data = i < _weatherHeldLightData.Count ? _weatherHeldLightData[i] : null;
            if (data == null)
            {
                light.enabled = visible;
                continue;
            }
            if (!light.enabled) light.enabled = true;
            Vector2 dimmers = i < _weatherHeldLightDimmers.Count ? _weatherHeldLightDimmers[i] : Vector2.one;
            data.lightDimmer = visible ? dimmers.x : 0f;
            SetVolumetricDimmer(data, visible ? dimmers.y : 0f);
        }

        for (int i = 0; i < _weatherHeldVolumes.Count; i++)
        {
            Volume volume = _weatherHeldVolumes[i];
            if (volume == null) continue;
            volume.weight = visible && i < _weatherHeldVolumeWeights.Count ? _weatherHeldVolumeWeights[i] : 0f;
        }
    }

    private void RestoreHeldWeatherVisuals()
    {
        for (int i = 0; i < _weatherHeldRenderers.Count; i++)
        {
            Renderer renderer = _weatherHeldRenderers[i];
            if (renderer == null) continue;
            renderer.forceRenderingOff = i < _weatherHeldRendererOff.Count && _weatherHeldRendererOff[i];
        }

        for (int i = 0; i < _weatherHeldFogs.Count; i++)
        {
            LocalVolumetricFog fog = _weatherHeldFogs[i];
            if (fog == null) continue;
            fog.enabled = i >= _weatherHeldFogEnabled.Count || _weatherHeldFogEnabled[i];
        }

        for (int i = 0; i < _weatherHeldLights.Count; i++)
        {
            Light light = _weatherHeldLights[i];
            if (light == null) continue;
            HDAdditionalLightData? data = i < _weatherHeldLightData.Count ? _weatherHeldLightData[i] : null;
            if (data != null && i < _weatherHeldLightDimmers.Count)
            {
                data.lightDimmer = _weatherHeldLightDimmers[i].x;
                SetVolumetricDimmer(data, _weatherHeldLightDimmers[i].y);
            }
            light.enabled = i >= _weatherHeldLightEnabled.Count || _weatherHeldLightEnabled[i];
            if (_heldLightOwners.TryGetValue(light, out VideoCameraItem owner) && (owner == null || owner == this)) _heldLightOwners.Remove(light);
        }

        _weatherHeldRenderers.Clear();
        _weatherHeldRendererOff.Clear();
        _weatherHeldFogs.Clear();
        _weatherHeldFogEnabled.Clear();
        for (int i = 0; i < _weatherHeldVolumes.Count; i++)
        {
            Volume volume = _weatherHeldVolumes[i];
            if (volume == null) continue;
            if (i < _weatherHeldVolumeWeights.Count) volume.weight = _weatherHeldVolumeWeights[i];
        }

        _weatherHeldLights.Clear();
        _weatherHeldLightEnabled.Clear();
        _weatherHeldLightData.Clear();
        _weatherHeldLightDimmers.Clear();
        _weatherHeldVolumes.Clear();
        _weatherHeldVolumeWeights.Clear();
    }

    private void ReleaseWeatherHold()
    {
        if (!_weatherHoldActive) return;
        _weatherHoldActive = false;

        TimeOfDay time = TimeOfDay.Instance;
        if (time.effects == null)
        {
            _weatherHeld.Clear();
            _weatherHeldWasEnabled.Clear();
            DestroyWeatherClones();
            RestoreHeldWeatherVisuals();
            RestoreHeldWeatherAudio();
            return;
        }

        for (int i = 0; i < _weatherHeld.Count; i++)
        {
            int index = _weatherHeld[i];
            if (index < 0 || index >= time.effects.Length) continue;

            WeatherEffect effect = time.effects[index];
            if (effect == null) continue;

            effect.effectEnabled = i < _weatherHeldWasEnabled.Count && _weatherHeldWasEnabled[i];
        }

        _weatherHeld.Clear();
        _weatherHeldWasEnabled.Clear();
        _weatherHeldObjects.Clear();
        DestroyWeatherClones();
        RestoreHeldWeatherVisuals();
        RestoreHeldWeatherAudio();
    }

    private void RestoreHeldWeatherAudio()
    {
        for (int i = 0; i < _weatherHeldAudio.Count; i++)
        {
            AudioSource source = _weatherHeldAudio[i];
            if (source == null) continue;
            if (i < _weatherAudioPositions.Count)
            {
                source.transform.localPosition = _weatherAudioPositions[i];
            }
        }

        _weatherHeldAudio.Clear();
        _weatherAudioPositions.Clear();
    }

    private bool SuppressFeedWeather()
    {
        _weatherRestore.Clear();
        if (!VideoCameraConfig.WeatherPerspective.Value) return false;

        Camera? active = ActiveCamera;
        if (active == null) return false;

        RefreshWeatherAnchors();
        if (_weatherAnchors.Count == 0) return false;

        Vector3 target = active.transform.position;

        for (int i = 0; i < _weatherAnchors.Count; i++)
        {
            WeatherAnchor anchor = _weatherAnchors[i];
            GameObject go = anchor.Target;
            if (go == null) continue;

            _weatherRestore.Add(new WeatherAnchorState
            {
                Target = go,
                Position = go.transform.position,
                WasActive = go.activeSelf
            });

            go.transform.position = target;
            if (anchor.CanActivate && !go.activeSelf)
            {
                go.SetActive(true);
            }
        }

        return _weatherRestore.Count > 0;
    }

    private void ShowHeldWeather()
    {
        if (!_weatherHoldActive) return;

        Camera? active = ActiveCamera;
        TimeOfDay time = TimeOfDay.Instance;
        if (active == null || time.effects == null) return;

        Vector3 target = active.transform.position;
        for (int i = 0; i < _weatherHeld.Count; i++)
        {
            int index = _weatherHeld[i];
            if (index < 0 || index >= time.effects.Length) continue;

            WeatherEffect effect = time.effects[index];
            if (effect == null || effect.effectObject == null) continue;
            effect.effectObject.transform.position = target;
        }

        SetHeldWeatherVisible(true);
    }

    private void HideHeldWeather()
    {
        if (!_weatherHoldActive) return;
        SetHeldWeatherVisible(false);
    }

    private void RestoreFeedWeather()
    {
        for (int i = _weatherRestore.Count - 1; i >= 0; i--)
        {
            WeatherAnchorState state = _weatherRestore[i];
            if (state.Target == null) continue;
            if (state.Target.activeSelf != state.WasActive) state.Target.SetActive(state.WasActive);
            state.Target.transform.position = state.Position;
        }

        _weatherRestore.Clear();
    }

    private IndoorFogState SuppressIndoorFog()
    {
        IndoorFogState state = default;
        if (!VideoCameraConfig.WeatherPerspective.Value) return state;

        LocalVolumetricFog fog = RoundManager.Instance.indoorFog;
        if (fog == null || !fog.gameObject.activeInHierarchy) return state;

        Camera? active = ActiveCamera;
        if (active == null) return state;

        state.Valid = true;
        state.Position = fog.transform.position;
        state.Enabled = fog.enabled;
        state.MeanFreePath = fog.parameters.meanFreePath;

        fog.transform.position = active.transform.position;
        if (IsInside)
        {
            if (!fog.enabled) fog.enabled = true;
        }
        else if (fog.enabled)
        {
            SetFogMeanFreePath(fog, Constants.Visual.HiddenFogMeanFreePath);
        }
        return state;
    }

    private void RestoreIndoorFog(IndoorFogState state)
    {
        if (!state.Valid) return;

        LocalVolumetricFog fog = RoundManager.Instance.indoorFog;
        if (fog == null) return;

        fog.transform.position = state.Position;
        if (fog.parameters.meanFreePath != state.MeanFreePath) SetFogMeanFreePath(fog, state.MeanFreePath);
        if (fog.enabled != state.Enabled) fog.enabled = state.Enabled;
    }

    private static void SetFogMeanFreePath(LocalVolumetricFog fog, float meanFreePath)
    {
        LocalVolumetricFogArtistParameters parameters = fog.parameters;
        parameters.meanFreePath = meanFreePath;
        fog.parameters = parameters;
    }

    private bool SuppressMeteorSprites()
    {
        _meteorRestore.Clear();
        if (!VideoCameraConfig.WeatherPerspective.Value) return false;

        TimeOfDay time = TimeOfDay.Instance;

        MeteorShowers meteors = time.MeteorWeather;
        if (meteors == null || meteors.meteors == null || meteors.meteors.Count == 0) return false;

        Camera? active = ActiveCamera;
        if (active == null) return false;

        Vector3 eye = active.transform.position;

        for (int i = 0; i < meteors.meteors.Count; i++)
        {
            Meteor meteor = meteors.meteors[i];
            if (meteor == null || meteor.distantSprite == null || meteor.meteorObject == null) continue;

            Transform sprite = meteor.distantSprite.transform;
            _meteorRestore.Add(new MeteorSpriteState
            {
                Target = sprite,
                Position = sprite.position,
                Rotation = sprite.rotation
            });

            Vector3 toMeteor = meteor.meteorObject.transform.position - eye;
            if (toMeteor.sqrMagnitude > 0.0001f)
            {
                sprite.position = new Ray(eye, toMeteor).GetPoint(Constants.Visual.MeteorSpriteDistance);
            }
            sprite.LookAt(eye, Vector3.up);
        }

        return _meteorRestore.Count > 0;
    }

    private void RestoreMeteorSprites()
    {
        for (int i = _meteorRestore.Count - 1; i >= 0; i--)
        {
            MeteorSpriteState state = _meteorRestore[i];
            if (state.Target == null) continue;
            state.Target.position = state.Position;
            state.Target.rotation = state.Rotation;
        }

        _meteorRestore.Clear();
    }
}
