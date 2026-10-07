using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dusk;
using UnityEngine;

namespace Operator;

internal static class AssetsLoader
{
    internal class KnownAssets(AssetBundle bundle) : AssetBundleLoader<KnownAssets>(bundle)
    {
        [LoadFromBundle(Constants.Assets.ItemAssetName)]
        public Item CameraItem { get; private set; } = null!;
    }

    private static AssetBundle? _bundle;
    private static KnownAssets? _known;

    private static Material? _ledRedLightMaterial;
    private static Material? _ledBlackLightMaterial;
    private static Material? _ledPauseLightMaterial;
    private static Material? _ledIrLightMaterial;
    private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    private static readonly Dictionary<string, AudioClip?> _dialogueBleeps = new();

    private static AudioClip? _cameraHandlingClip;
    private static bool _cameraHandlingAttempted;
    private static AudioClip? _zoomClip;
    private static bool _zoomAttempted;
    private static AudioClip? _zoomReversedClip;
    private static AudioClip? _recordingRunClip;
    private static bool _recordingRunAttempted;
    private static AudioClip? _buttonPressClip;
    private static bool _buttonPressAttempted;
    private static AudioClip? _buttonHoldClip;
    private static bool _buttonHoldAttempted;
    private static AudioClip? _cassetteEjectClip;
    private static bool _cassetteEjectAttempted;
    private static AudioClip? _nightVisionOffClip;
    private static bool _nightVisionOffAttempted;
    private static AudioClip[]? _lensBreakClips;
    private static bool _lensBreakAttempted;
    private static AnimationClip? _holdClip;
    private static bool _holdClipAttempted;
    private static AnimationClip? _holdLeftClip;
    private static bool _holdLeftClipAttempted;

    private static readonly System.Random StaticNoiseRng = new();

    public static Material? LedRedLightMaterial
    {
        get
        {
            if (_ledRedLightMaterial == null)
            {
                _ledRedLightMaterial = LoadOptionalAsset<Material>(Constants.Assets.MaterialRedLightName);
            }
            return _ledRedLightMaterial;
        }
    }

    public static Material? LedBlackLightMaterial
    {
        get
        {
            if (_ledBlackLightMaterial == null)
            {
                _ledBlackLightMaterial = LoadOptionalAsset<Material>(Constants.Assets.MaterialBlackLightName);
            }
            return _ledBlackLightMaterial;
        }
    }

    public static Material? LedPauseLightMaterial
    {
        get
        {
            if (_ledPauseLightMaterial == null)
            {
                _ledPauseLightMaterial = LoadOptionalAsset<Material>(Constants.Assets.MaterialPauseLightName);
            }
            return _ledPauseLightMaterial;
        }
    }

    public static Material? LedIrLightMaterial
    {
        get
        {
            if (_ledIrLightMaterial != null) return _ledIrLightMaterial;

            Material? black = LedBlackLightMaterial;
            if (black == null) return null;

            _ledIrLightMaterial = new Material(black)
            {
                name = "OperatorIrLight",
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (_ledIrLightMaterial.HasProperty(EmissiveColorId))
            {
                _ledIrLightMaterial.SetColor(EmissiveColorId, Constants.Parts.IrLampColor);
            }
            return _ledIrLightMaterial;
        }
    }

    public static AnimationClip? HoldClip
    {
        get
        {
            if (_holdClip != null || _holdClipAttempted) return _holdClip;
            _holdClipAttempted = true;
            _holdClip = LoadOptionalAsset<AnimationClip>(Constants.Assets.HoldClipName);
            if (_holdClip == null)
            {
                Plugin.Log.LogWarning("Could not find camera hold animation.");
            }
            return _holdClip;
        }
    }

    public static AnimationClip? HoldLeftClip
    {
        get
        {
            if (_holdLeftClip != null || _holdLeftClipAttempted) return _holdLeftClip;
            _holdLeftClipAttempted = true;
            _holdLeftClip = LoadOptionalAsset<AnimationClip>(Constants.Assets.HoldLeftClipName);
            if (_holdLeftClip == null)
            {
                Plugin.Log.LogWarning("Could not find left hand hold animation. The left hand stays off the screen.");
            }
            return _holdLeftClip;
        }
    }

    public static AudioClip? GetDialogueBleepClip(string clipName)
    {
        if (_dialogueBleeps.TryGetValue(clipName, out AudioClip? cached))
        {
            return cached;
        }

        AudioClip? found = FindDialogueBleep(HUDManager.Instance, clipName);
        if (found == null)
        {
            foreach (var hud in Resources.FindObjectsOfTypeAll<HUDManager>())
            {
                found = FindDialogueBleep(hud, clipName);
                if (found != null) break;
            }
        }

        _dialogueBleeps[clipName] = found;
        return found;
    }

    private static AudioClip? FindDialogueBleep(HUDManager? hud, string clipName)
    {
        if (hud == null || hud.dialogueBleeps == null) return null;
        foreach (var clip in hud.dialogueBleeps)
        {
            if (clip != null && clip.name == clipName)
            {
                return clip;
            }
        }
        return null;
    }

    public static AudioClip? GetCameraHandlingClip()
    {
        if (_cameraHandlingClip != null) return _cameraHandlingClip;
        if (_cameraHandlingAttempted) return null;
        _cameraHandlingAttempted = true;
        _cameraHandlingClip = LoadOptionalAsset<AudioClip>(Constants.Assets.CameraHandlingClipName);
        return _cameraHandlingClip;
    }

    public static AudioClip? GetZoomClip()
    {
        if (_zoomClip != null) return _zoomClip;
        if (_zoomAttempted) return null;
        _zoomAttempted = true;
        _zoomClip = LoadOptionalAsset<AudioClip>(Constants.Assets.ZoomClipName);
        return _zoomClip;
    }

    public static void WarmAudioClips()
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        WarmClip("recording run", GetRecordingRunClip);
        WarmClip("button press", GetButtonPressClip);
        WarmClip("button hold", GetButtonHoldClip);
        WarmClip("camera handling", GetCameraHandlingClip);
        WarmClip("zoom", GetZoomClip);
        WarmClip("cassette eject", GetCassetteEjectClip);
        WarmClip("night vision off", GetNightVisionOffClip);
    }

    private static void WarmClip(string name, Func<AudioClip?> load)
    {
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AudioClip? clip = load();
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not warm {name} clip, {ex.Message}");
        }
    }

    public static AudioClip? GetRecordingRunClip()
    {
        if (_recordingRunClip != null) return _recordingRunClip;
        if (_recordingRunAttempted) return null;
        _recordingRunAttempted = true;
        _recordingRunClip = LoadOptionalAsset<AudioClip>(Constants.Assets.RecordingRunClipName);
        return _recordingRunClip;
    }

    public static AudioClip? GetButtonPressClip()
    {
        if (_buttonPressClip != null) return _buttonPressClip;
        if (_buttonPressAttempted) return null;
        _buttonPressAttempted = true;
        _buttonPressClip = LoadOptionalAsset<AudioClip>(Constants.Assets.ButtonPressClipName);
        return _buttonPressClip;
    }

    public static AudioClip? GetButtonHoldClip()
    {
        if (_buttonHoldClip != null) return _buttonHoldClip;
        if (_buttonHoldAttempted) return null;
        _buttonHoldAttempted = true;
        _buttonHoldClip = LoadOptionalAsset<AudioClip>(Constants.Assets.ButtonHoldClipName);
        return _buttonHoldClip;
    }

    public static AudioClip? GetCassetteEjectClip()
    {
        if (_cassetteEjectClip != null) return _cassetteEjectClip;
        if (_cassetteEjectAttempted) return null;
        _cassetteEjectAttempted = true;
        _cassetteEjectClip = LoadOptionalAsset<AudioClip>(Constants.Assets.CassetteEjectClipName);
        return _cassetteEjectClip;
    }

    public static AudioClip? GetNightVisionOffClip()
    {
        if (_nightVisionOffClip != null) return _nightVisionOffClip;
        if (_nightVisionOffAttempted) return null;
        _nightVisionOffAttempted = true;
        _nightVisionOffClip = LoadOptionalAsset<AudioClip>(Constants.Assets.NightVisionOffClipName);
        return _nightVisionOffClip;
    }

    public static AudioClip? GetLensBreakClip()
    {
        if (!_lensBreakAttempted)
        {
            _lensBreakAttempted = true;
            var list = new List<AudioClip>();
            foreach (string name in Constants.Assets.LensBreakClipNames)
            {
                AudioClip? c = LoadOptionalAsset<AudioClip>(name);
                if (c != null) list.Add(c);
            }
            _lensBreakClips = list.ToArray();
        }
        if (_lensBreakClips == null || _lensBreakClips.Length == 0) return null;
        if (_lensBreakClips.Length == 1) return _lensBreakClips[0];
        return _lensBreakClips[StaticNoiseRng.Next(_lensBreakClips.Length)];
    }

    public static AudioClip? GetZoomReversedClip()
    {
        if (_zoomReversedClip != null) return _zoomReversedClip;
        AudioClip? src = GetZoomClip();
        if (src == null) return null;
        _zoomReversedClip = CreateReversedClip(src, src.name + "_reversed");
        return _zoomReversedClip;
    }

    private static AudioClip? CreateReversedClip(AudioClip src, string name)
    {
        try
        {
            int frames = src.samples;
            int channels = src.channels;
            if (frames <= 0 || channels <= 0) return null;

            if (src.loadState != AudioDataLoadState.Loaded)
            {
                src.LoadAudioData();
            }

            float[] data = new float[frames * channels];
            if (!src.GetData(data, 0)) return null;

            for (int i = 0, j = frames - 1; i < j; i++, j--)
            {
                for (int c = 0; c < channels; c++)
                {
                    int a = i * channels + c;
                    int b = j * channels + c;
                    (data[a], data[b]) = (data[b], data[a]);
                }
            }

            AudioClip rev = AudioClip.Create(name, frames, channels, src.frequency, stream: false);
            rev.SetData(data, 0);
            return rev;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not reverse '{name}' clip: {ex.Message}");
            return null;
        }
    }

    public static bool TryLoadBundle()
    {
        if (_bundle != null)
        {
            return true;
        }

        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string bundlePath = Path.Combine(assemblyDirectory, Constants.Assets.BundleFileName);

        if (!File.Exists(bundlePath))
        {
            Plugin.Log.LogError($"Could not find asset bundle at '{bundlePath}'. Camera is disabled.");
            return false;
        }

        try
        {
            _bundle = AssetBundle.LoadFromFile(bundlePath);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to load asset bundle: {ex.Message}");
            return false;
        }

        if (_bundle == null)
        {
            Plugin.Log.LogError($"Failed to load asset bundle. Camera is disabled.");
            return false;
        }

        _known = new KnownAssets(_bundle);
        Plugin.Log.LogInfo($"Loaded asset bundle.");
        return true;
    }

    public static Item? CameraItem => _known?.CameraItem;

    public static T? LoadOptionalAsset<T>(string assetName) where T : UnityEngine.Object
    {
        if (_bundle == null || string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        return _bundle.LoadAsset<T>(assetName);
    }
}
