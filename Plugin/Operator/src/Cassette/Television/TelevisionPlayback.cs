using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Video;

namespace Operator.Cassette;

internal struct CassetteData
{
    public string Id;
    public string FileName;
    public string Timestamp;
    public float Duration;
    public int Value;
    public string Breakdown;

    public bool IsEmpty => string.IsNullOrEmpty(FileName);

    public static CassetteData Empty => new()
    {
        Id = string.Empty,
        FileName = string.Empty,
        Timestamp = string.Empty,
        Breakdown = string.Empty,
    };
}

internal static class TelevisionPlayback
{
    private const string HostName = "OperatorTelevisionPlayback";
    private const string AudioName = "OperatorTelevisionPlaybackAudio";

    private static string _loadedFile = string.Empty;
    private static CassetteData _loadedData = CassetteData.Empty;
    private static bool _playRequested;
    private static bool _paused;
    private static float _speed = 1f;
    private static float _volume = 1f;
    private static double _clockBase;
    private static double _clockStamp;
    private static bool _clockRunning;
    private static bool _tvOn;
    private static bool _endRaised;

    private static bool _takingOver;
    private static TVScript? _tv;
    private static RenderTexture? _screenRt;
    private static Material? _origScreenMat;
    private static Material? _screenMat;
    private static string _preparedFile = string.Empty;

    private static GameObject? _host;
    private static VideoPlayer? _player;
    private static AudioSource? _audio;
    private static bool _pendingSeekOnPrepare;

    private static Light? _tvLight;
    private static HDAdditionalLightData? _tvLightHd;
    private static float _tvLightBaseIntensity;
    private static Color _tvLightBaseColor = Color.white;
    private static RenderTexture? _probeRt;
    private static Color _ambient = Color.black;
    private static Color _ambientTarget = Color.black;
    private static float _ambientLuma;
    private static float _ambientLumaTarget;
    private static bool _readbackPending;
    private static double _nextSampleTime;
    private static float _noiseSeed = -1f;
    private static bool _missingFileLogged;
    private static float _nextTakeoverAttempt;

    internal static bool HasLoaded => !string.IsNullOrEmpty(_loadedFile);
    internal static string LoadedFileName => _loadedFile;
    internal static CassetteData LoadedData => _loadedData;
    internal static bool IsPlayRequested => _playRequested;
    internal static bool IsPaused => _paused;
    internal static float CurrentSpeed => _speed;
    internal static bool IsAdvancing => _clockRunning && (_loadedData.Duration <= 0f || Position < _loadedData.Duration);

    internal static double Position
    {
        get
        {
            double position = _clockRunning ? _clockBase + ((Now - _clockStamp) * _speed) : _clockBase;
            double end = _loadedData.Duration > 0f ? _loadedData.Duration : double.MaxValue;
            return Math.Min(Math.Max(0.0, position), end);
        }
    }

    private static double Now =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.ServerTime.Time
            : Time.realtimeSinceStartupAsDouble;

    internal static void SetClock(double position) => Retime(position);

    private static void Retime(double position)
    {
        TVScript? tv = CassetteTakerManager.ActiveTv != null ? CassetteTakerManager.ActiveTv : UnityEngine.Object.FindObjectOfType<TVScript>();
        _endRaised = false;
        _tvOn = tv != null && tv.tvOn;
        _clockBase = position;
        _clockStamp = Now;
        _clockRunning = _playRequested && !_paused && _tvOn && HasLoaded;
    }

    internal static event Action? LocalPlaybackFinished;

    internal static bool IsSuppressing(TVScript tv) => _takingOver && _tv != null && ReferenceEquals(_tv, tv);

    internal static void SetLoaded(CassetteData data)
    {
        _loadedData = data;
        _loadedFile = data.FileName ?? string.Empty;
        _playRequested = false;
        _paused = false;
        _speed = 1f;
        Retime(0.0);
        _preparedFile = string.Empty;
        _missingFileLogged = false;
        _nextTakeoverAttempt = 0f;
        if (_takingOver) EndTakeover();
    }

    internal static void SetPlaying(bool play)
    {
        double position = Position;
        _playRequested = play;
        _paused = false;
        Retime(play ? position : 0.0);
        _nextTakeoverAttempt = 0f;
    }

    internal static void SetPaused(bool paused)
    {
        double position = Position;
        _paused = paused;
        Retime(position);
        if (_takingOver && _player != null)
        {
            try { if (paused) _player.Pause(); else _player.Play(); } catch { }
        }
    }

    internal static void SetSpeed(float speed)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed)) return;
        double position = Position;
        _speed = Mathf.Clamp(speed, 0.25f, 4f);
        Retime(position);
        if (_player != null)
        {
            try { _player.playbackSpeed = _speed; } catch { }
        }
    }

    internal static float LocalVolume => _volume;

    internal static void SetLocalVolume(float volume)
    {
        _volume = Mathf.Clamp(volume, Constants.Television.VolumeMin, 1f);
        if (_audio != null) _audio.volume = _volume;
    }

    internal static void ClearAll()
    {
        if (_takingOver) EndTakeover();
        _loadedData = CassetteData.Empty;
        _loadedFile = string.Empty;
        _playRequested = false;
        _paused = false;
        _speed = 1f;
        _clockBase = 0.0;
        _clockRunning = false;
        _preparedFile = string.Empty;
        DestroyHost();
    }

    internal static void Update()
    {
        TVScript? tv = CassetteTakerManager.ActiveTv;
        if (tv != null && tv.tvOn != _tvOn) Retime(Position);
        if (_clockRunning && !_endRaised && _loadedData.Duration > 0f
            && _clockBase + ((Now - _clockStamp) * _speed) >= _loadedData.Duration + Constants.Television.EndGrace)
        {
            _endRaised = true;
            LocalPlaybackFinished?.Invoke();
        }
        bool want = tv != null && tv.tvOn && _playRequested && HasLoaded;

        if (want && !_takingOver && Time.time >= _nextTakeoverAttempt)
        {
            BeginTakeover(tv!);
        }
        else if (!want && _takingOver)
        {
            EndTakeover();
        }

        if (_takingOver) UpdateLight();
    }

    private static void BeginTakeover(TVScript tv)
    {
        if (tv.tvMesh == null || tv.video == null) return;

        string? filePath = TerminalCassettePlayback.FindLocalPath(_loadedFile);
        if (string.IsNullOrEmpty(filePath))
        {
            _nextTakeoverAttempt = Time.time + 1f;
            if (!_missingFileLogged)
            {
                _missingFileLogged = true;
                Plugin.Log.LogWarning($"'{_loadedFile}' not on disk locally, recording cant be shown.");
            }
            return;
        }

        _tv = tv;
        _screenRt = tv.video.targetTexture;

        try { if (tv.video != null) tv.video.Stop(); } catch { }
        try { if (tv.tvSFX != null) tv.tvSFX.Stop(); } catch { }

        TakeOverScreenMaterial(tv);
        SetupLight(tv);
        if (_screenRt != null) ClearBlack(_screenRt);

        _takingOver = true;

        SetupHost();
        if (_player == null || _screenRt == null)
        {
            _nextTakeoverAttempt = Time.time + 1f;
            EndTakeover();
            return;
        }

        _player.targetTexture = _screenRt;

        if (_preparedFile != _loadedFile)
        {
            _player.Stop();
            _player.source = VideoSource.Url;
            _player.url = "file://" + filePath.Replace('\\', '/');
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.playbackSpeed = 1f;
            _preparedFile = _loadedFile;
            _pendingSeekOnPrepare = true;
            _player.Prepare();
            Plugin.Log.LogDebug($"Preparing '{_loadedFile}' on the Television.");
        }
        else
        {
            try { _player.time = Position; } catch { }
            _player.Play();
        }
    }

    private static void EndTakeover()
    {
        if (_player != null)
        {
            try { _player.Stop(); } catch { }
            _player.targetTexture = null;
        }
        _preparedFile = string.Empty;
        if (_audio != null)
        {
            try { _audio.Stop(); } catch { }
        }

        if (_tv != null)
        {
            MeshRenderer? mesh = _tv.tvMesh;
            if (mesh != null)
            {
                Material target = _tv.tvOn ? _tv.tvOnMaterial : _tv.tvOffMaterial;
                if (target == null) target = _origScreenMat!;
                if (target != null)
                {
                    Material[] mats = mesh.sharedMaterials;
                    int idx = Constants.Television.ScreenMaterialIndex;
                    if (idx >= 0 && idx < mats.Length)
                    {
                        mats[idx] = target;
                        mesh.sharedMaterials = mats;
                    }
                }
            }
            _tv.wasTvOnLastFrame = !_tv.tvOn;
            RestoreLight();
            if (_tvLight != null) _tvLight.enabled = _tv.tvOn;
            RestoreVanillaAudio(_tv);
        }

        if (_screenMat != null)
        {
            UnityEngine.Object.Destroy(_screenMat);
            _screenMat = null;
        }

        _takingOver = false;
        _tv = null;
        _screenRt = null;
        _origScreenMat = null;
        _tvLight = null;
        _tvLightHd = null;
    }

    private static void RestoreVanillaAudio(TVScript tv)
    {
        if (!tv.tvOn || tv.tvSFX == null || tv.tvAudioClips == null) return;

        int clip = tv.currentClip;
        if (clip < 0 || clip >= tv.tvAudioClips.Length) return;

        AudioClip? audio = tv.tvAudioClips[clip];
        if (audio == null) return;

        tv.tvSFX.clip = audio;
        tv.tvSFX.time = Mathf.Clamp(tv.currentClipTime, 0f, Mathf.Max(0f, audio.length - 0.1f));
        tv.tvSFX.Play();
    }

    private static void OnPrepared(VideoPlayer vp)
    {
        if (!_pendingSeekOnPrepare) return;
        _pendingSeekOnPrepare = false;
        try
        {
            vp.time = Position;
            if (_takingOver)
            {
                vp.Play();
                ApplyTransport();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to start recording on Television: {ex.Message}");
        }
    }

    private static void ApplyTransport()
    {
        if (_player == null) return;
        try
        {
            _player.playbackSpeed = _speed;
            if (_paused) _player.Pause();
        }
        catch { }
    }

    private static void OnLoopPoint(VideoPlayer vp)
    {
        LocalPlaybackFinished?.Invoke();
    }

    private static void OnError(VideoPlayer vp, string message)
    {
        Plugin.Log.LogError($"Television playback failed: {message}");
    }

    private static void TakeOverScreenMaterial(TVScript tv)
    {
        MeshRenderer mesh = tv.tvMesh;
        Material[] mats = mesh.sharedMaterials;
        int idx = Constants.Television.ScreenMaterialIndex;
        if (idx >= 0 && idx < mats.Length)
        {
            _origScreenMat = mats[idx];
            if (tv.tvOnMaterial != null)
            {
                mats[idx] = CreateScreenMaterial(tv.tvOnMaterial);
                mesh.sharedMaterials = mats;
            }
        }
    }

    private static Material CreateScreenMaterial(Material vanilla)
    {
        if (_screenMat != null) UnityEngine.Object.Destroy(_screenMat);
        _screenMat = new Material(vanilla) { name = vanilla.name + " (cassette)" };
        if (_screenMat.HasProperty(Constants.Television.ScreenBaseColor))
        {
            _screenMat.SetColor(Constants.Television.ScreenBaseColor, Color.black);
        }
        if (_screenMat.HasProperty(Constants.Television.ScreenEmissiveColor))
        {
            _screenMat.SetColor(Constants.Television.ScreenEmissiveColor,
                vanilla.GetColor(Constants.Television.ScreenEmissiveColor) * Constants.Television.ScreenGlow);
        }
        return _screenMat;
    }

    private static void SetupLight(TVScript tv)
    {
        _tvLight = tv.tvLight;
        if (_tvLight == null) return;

        _tvLightHd = _tvLight.GetComponent<HDAdditionalLightData>();
        _tvLightBaseIntensity = _tvLightHd != null ? _tvLightHd.intensity : _tvLight.intensity;
        _tvLightBaseColor = _tvLight.color;

        if (_noiseSeed < 0f) _noiseSeed = UnityEngine.Random.Range(0f, 1000f);
        _ambient = _ambientTarget = Constants.Television.BacklightColor;
        _ambientLuma = _ambientLumaTarget = 0f;
        _tvLight.enabled = true;
    }

    private static void RestoreLight()
    {
        if (_tvLight == null) return;

        _tvLight.color = _tvLightBaseColor;
        if (_tvLightHd != null)
        {
            _tvLightHd.color = _tvLightBaseColor;
            _tvLightHd.intensity = _tvLightBaseIntensity;
        }
        else
        {
            _tvLight.intensity = _tvLightBaseIntensity;
        }
    }

    private static void UpdateLight()
    {
        if (!_takingOver || _tvLight == null || _screenRt == null) return;

        float gain = VideoCameraConfig.TelevisionScreenLightIntensity.Value;
        if (gain <= 0f)
        {
            RestoreLight();
            return;
        }

        SampleScreenAmbient();

        float k = 1f - Mathf.Exp(-Constants.Television.LightResponseSpeed * Mathf.Max(0f, Time.deltaTime));
        _ambient = Color.Lerp(_ambient, _ambientTarget, k);
        _ambientLuma = Mathf.Lerp(_ambientLuma, _ambientLumaTarget, k);

        Color contentColor = ToLightTint(_ambient);
        float contentWeight = Mathf.Clamp01(_ambientLuma / Constants.Television.LightDarkLumaThreshold);
        Color tint = Color.Lerp(Constants.Television.BacklightColor, contentColor, contentWeight);

        float factor = Mathf.Clamp(
            Mathf.Pow(_ambientLuma / Constants.Television.LightMidLuma, Constants.Television.LightContrast),
            Constants.Television.LightMin,
            Constants.Television.LightMax);

        float flicker = (Mathf.PerlinNoise(_noiseSeed, Time.time * Constants.Television.LightFlickerSpeed) - 0.5f) * 2f;
        float intensity = _tvLightBaseIntensity * gain * factor * (1f + (flicker * Constants.Television.LightFlickerAmount));

        _tvLight.color = tint;
        if (_tvLightHd != null)
        {
            _tvLightHd.color = tint;
            _tvLightHd.intensity = intensity;
        }
        else
        {
            _tvLight.intensity = intensity;
        }
    }

    private static void SampleScreenAmbient()
    {
        if (_screenRt == null || !SystemInfo.supportsAsyncGPUReadback) return;

        double now = Time.unscaledTimeAsDouble;
        if (now < _nextSampleTime) return;
        _nextSampleTime = now + Constants.Television.LightSampleInterval;

        int size = Constants.Television.LightProbeSize;
        if (_probeRt == null || !_probeRt.IsCreated())
        {
            _probeRt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32) { name = "OperatorTvProbe" };
            _probeRt.Create();
        }
        if (_readbackPending) return;

        Graphics.Blit(_screenRt, _probeRt);
        _readbackPending = true;
        AsyncGPUReadback.Request(_probeRt, 0, TextureFormat.RGBA32, OnAmbientReadback);
    }

    private static void OnAmbientReadback(AsyncGPUReadbackRequest req)
    {
        _readbackPending = false;
        if (req.hasError) return;

        var data = req.GetData<Color32>();
        int n = data.Length;
        if (n == 0) return;

        long r = 0, g = 0, b = 0;
        for (int i = 0; i < n; i++)
        {
            r += data[i].r;
            g += data[i].g;
            b += data[i].b;
        }

        Color mean = new(r / (255f * n), g / (255f * n), b / (255f * n), 1f);
        _ambientTarget = mean;
        _ambientLumaTarget = (0.2126f * mean.r) + (0.7152f * mean.g) + (0.0722f * mean.b);
    }

    private static Color ToLightTint(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float _);
        Color o = Color.HSVToRGB(h, Mathf.Min(s, Constants.Television.LightMaxSaturation), 1f);
        o.a = 1f;
        return o;
    }

    private static void ClearBlack(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    private static void SetupHost()
    {
        if (_host != null)
        {
            if (_audio == null && _player != null)
            {
                CreateAudio();
                _player.SetTargetAudioSource(0, _audio);
            }
            RepositionAudio();
            return;
        }

        _host = new GameObject(HostName);
        UnityEngine.Object.DontDestroyOnLoad(_host);
        _player = _host.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.aspectRatio = VideoAspectRatio.Stretch;

        CreateAudio();

        _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _player.controlledAudioTrackCount = 1;
        _player.EnableAudioTrack(0, true);
        _player.SetTargetAudioSource(0, _audio);

        _player.prepareCompleted += OnPrepared;
        _player.loopPointReached += OnLoopPoint;
        _player.errorReceived += OnError;

        RepositionAudio();
    }

    private static void CreateAudio()
    {
        var audioObject = new GameObject(AudioName);
        _audio = audioObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.spatialBlend = 1f;
        _audio.rolloffMode = AudioRolloffMode.Linear;
        _audio.minDistance = 1f;
        _audio.maxDistance = Constants.Television.PlaybackAudioMaxDistance;
        _audio.dopplerLevel = 0f;
        _audio.spread = 0f;
        _audio.volume = _volume;
        audioObject.AddComponent<PlaybackGain>();
    }

    private static void RepositionAudio()
    {
        if (_audio == null || _tv == null || _tv.tvMesh == null) return;
        _audio.transform.SetParent(_tv.tvMesh.transform, false);
        _audio.transform.localPosition = Vector3.zero;
        if (_tv.tvSFX != null) _audio.outputAudioMixerGroup = _tv.tvSFX.outputAudioMixerGroup;
    }

    private static void DestroyHost()
    {
        try
        {
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepared;
                _player.loopPointReached -= OnLoopPoint;
                _player.errorReceived -= OnError;
            }
            if (_audio != null) UnityEngine.Object.Destroy(_audio.gameObject);
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_probeRt != null)
            {
                _probeRt.Release();
                UnityEngine.Object.Destroy(_probeRt);
            }
        }
        catch (Exception)
        {
        }
        _player = null;
        _audio = null;
        _host = null;
        _probeRt = null;
        _readbackPending = false;
        _preparedFile = string.Empty;
    }
}
