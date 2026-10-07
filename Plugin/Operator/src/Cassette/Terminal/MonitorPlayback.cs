using System;
using UnityEngine;
using UnityEngine.Video;
using Operator.Compat;

namespace Operator.Cassette;

internal static class MonitorPlayback
{
    private const int RtWidth = 640;
    private const int RtHeight = 360;
    private const string HostName = "OperatorMonitorPlayback";
    private const string AudioName = "OperatorMonitorPlaybackAudio";

    private static GameObject? _host;
    private static VideoPlayer? _player;
    private static RenderTexture? _rt;
    private static AudioSource? _audio;

    private static bool _active;
    private static bool _monitorBound;
    private static double _nextReapplyTime;
    private static int _reapplyCount;
    private static double _nextReapplyLog;
    private static bool _stateReported;
    private static double _bindTime;
    private static int _monitorIndex = -1;
    private static string _fileName = string.Empty;

    internal static bool IsActive => _active;
    internal static int ActiveMonitorIndex => _monitorIndex;
    internal static string ActiveFileName => _fileName;

    internal static event Action? LocalPlaybackFinished;
    internal static event Action? LocalPlaybackFailed;

    internal static bool Play(string fileName, int monitorIndex)
    {
        if (!ShipMonitors.Available)
        {
            Plugin.Log.LogWarning("There is no ship monitor to transfer onto.");
            return false;
        }
        if (string.IsNullOrEmpty(fileName)) return false;

        string? filePath = TerminalCassettePlayback.FindLocalPath(fileName);
        if (string.IsNullOrEmpty(filePath))
        {
            Plugin.Log.LogWarning($"'{fileName}' not on disk locally, cannot show on monitor {monitorIndex}.");
            return false;
        }

        if (_active && _monitorIndex != monitorIndex)
        {
            RestoreCurrentMonitor();
        }

        SetupHost(monitorIndex);
        if (_player == null || _rt == null)
        {
            Plugin.Log.LogWarning($"Could not build video host for monitor {monitorIndex}.");
            return false;
        }

        _monitorIndex = monitorIndex;
        _fileName = fileName;

        if (!ShipMonitors.TryBind(monitorIndex, _rt))
        {
            Plugin.Log.LogWarning($"Failed to bind RenderTexture to monitor {monitorIndex}.");
            Stop();
            return false;
        }
        _monitorBound = true;
        MonitorScreenLight.Bind(_rt, monitorIndex);
        _nextReapplyTime = 0.0;
        _reapplyCount = 0;
        _nextReapplyLog = 0.0;
        _stateReported = false;
        _bindTime = Time.unscaledTimeAsDouble;

        if (OpenBodyCamsCompat.Available && !OpenBodyCamsCompat.TookBodyCamScreen)
        {
            OpenBodyCamsCompat.TryBind(_rt);
        }

        _active = true;

        _player.Stop();
        _player.source = VideoSource.Url;
        _player.url = "file://" + filePath.Replace('\\', '/');
        _player.playOnAwake = false;
        _player.isLooping = false;
        _player.skipOnDrop = true;
        _player.playbackSpeed = 1f;

        _player.prepareCompleted -= OnPrepared;
        _player.prepareCompleted += OnPrepared;
        _player.loopPointReached -= OnLoopPoint;
        _player.loopPointReached += OnLoopPoint;
        _player.errorReceived -= OnError;
        _player.errorReceived += OnError;

        _player.Prepare();
        Plugin.Log.LogDebug($"Preparing '{fileName}' for monitor {monitorIndex}.");
        return true;
    }

    internal static void Tick()
    {
        if (!_active || !_monitorBound) return;

        MonitorScreenLight.Tick();

        double now = Time.unscaledTimeAsDouble;
        if (now < _nextReapplyTime) return;
        _nextReapplyTime = now + Constants.Monitor.ReapplyInterval;

        ReportState(now);
        if (!_active || !_monitorBound) return;
        if (PrepareTimedOut(now))
        {
            Plugin.Log.LogError($"Monitor {_monitorIndex} never finished reading recording.");
            AbandonPlayback();
            return;
        }

        if (!ShipMonitors.Reapply(_monitorIndex)) return;

        _reapplyCount++;
        if (now >= _nextReapplyLog)
        {
            _nextReapplyLog = now + Constants.Monitor.ReapplyLogCooldown;
            Plugin.Log.LogDebug($"Reapplied the cassette feed to monitor {_monitorIndex} {_reapplyCount} times.");
        }
    }

    private static void ReportState(double now)
    {
        if (_stateReported || _player == null) return;
        if (now < _bindTime + Constants.Monitor.StateReportDelay) return;
        _stateReported = true;
        bool overdue = PrepareTimedOut(now);

        Plugin.Log.LogDebug(
            $"Monitor {_monitorIndex} state, prepared {_player.isPrepared}, playing {_player.isPlaying}, " +
            $"frame {_player.frame} of {_player.frameCount}, target {(_player.targetTexture == null ? "none" : _player.targetTexture.name)}, " +
            $"screen still ours {ShipMonitors.IsBound(_monitorIndex)}.");

        if (overdue)
        {
            Plugin.Log.LogError($"Monitor {_monitorIndex} never finished reading recording.");
            AbandonPlayback();
        }
    }

    private static bool PrepareTimedOut(double now)
    {
        return _player != null && !_player.isPrepared
            && now >= _bindTime + Constants.Monitor.PrepareTimeout;
    }

    private static void OnPrepared(VideoPlayer vp)
    {
        try
        {
            vp.time = 0.0;
            vp.Play();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to start recording on the monitor: {ex.Message}");
        }
    }

    private static void OnLoopPoint(VideoPlayer vp)
    {
        LocalPlaybackFinished?.Invoke();
    }

    private static void OnError(VideoPlayer vp, string message)
    {
        Plugin.Log.LogError($"Monitor playback failed: {message}");
        AbandonPlayback();
    }

    private static void AbandonPlayback()
    {
        if (!_active) return;
        Stop();
        LocalPlaybackFailed?.Invoke();
    }

    internal static void SetSpeed(float speed)
    {
        if (_player == null) return;
        try { _player.playbackSpeed = Mathf.Clamp(speed, 0.1f, 10f); } catch { }
    }

    internal static void Pause()
    {
        if (_player == null) return;
        try { if (_player.isPlaying) _player.Pause(); } catch { }
        if (_audio != null)
        {
            try { _audio.Pause(); } catch { }
        }
    }

    internal static void Resume()
    {
        if (_player == null) return;
        try { if (!_player.isPlaying) _player.Play(); } catch { }
        if (_audio != null)
        {
            try { _audio.UnPause(); } catch { }
        }
    }

    internal static void SetVolume(float volume)
    {
        if (_audio == null) return;
        try { _audio.volume = Mathf.Clamp01(volume); } catch { }
    }

    internal static float CurrentSeconds()
    {
        if (_player == null || !_player.isPrepared) return -1f;
        try { return (float)_player.time; } catch { return -1f; }
    }

    internal static void SeekTo(float seconds)
    {
        if (_player == null || seconds < 0f) return;
        try { if (_player.isPrepared) _player.time = seconds; } catch { }
    }

    internal static void Stop()
    {
        if (_player != null)
        {
            try { _player.Stop(); } catch { }
        }
        if (_audio != null)
        {
            try { _audio.Stop(); } catch { }
        }

        RestoreCurrentMonitor();

        _active = false;
        _monitorBound = false;
        _reapplyCount = 0;
        _nextReapplyLog = 0.0;
        _monitorIndex = -1;
        _fileName = string.Empty;

        DestroyHost();
    }

    private static void RestoreCurrentMonitor()
    {
        if (!_monitorBound) return;
        _monitorBound = false;
        MonitorScreenLight.Release();
        ShipMonitors.Restore(_monitorIndex);
        OpenBodyCamsCompat.Restore();
        OpenBodyCamsCompat.ReleaseScreen();
    }

    private static void SetupHost(int monitorIndex)
    {
        if (_rt == null || !_rt.IsCreated())
        {
            _rt = new RenderTexture(RtWidth, RtHeight, 0, RenderTextureFormat.ARGB32) { name = "OperatorMonitorRT" };
            _rt.Create();
            ClearBlack(_rt);
        }

        Transform? monitorTransform = ShipMonitors.GetMonitorTransform(monitorIndex);

        if (_host == null)
        {
            _host = new GameObject(HostName);
            _player = _host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _rt;
            _player.aspectRatio = VideoAspectRatio.Stretch;

            var audioObject = new GameObject(AudioName);
            _audio = audioObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;

            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.controlledAudioTrackCount = 1;
            _player.SetTargetAudioSource(0, _audio);
        }

        _player!.targetTexture = _rt;
        PlaceAudio(monitorTransform);
    }

    private static void PlaceAudio(Transform? monitorTransform)
    {
        if (_audio == null) return;

        AudioSource? speaker = GetShipSpeaker();
        if (speaker != null)
        {
            _audio.transform.SetParent(speaker.transform, false);
            _audio.transform.localPosition = Vector3.zero;
            MatchSpeaker(speaker, _audio);
            return;
        }

        ApplyAudioSettings(_audio);
        if (monitorTransform != null)
        {
            _audio.transform.SetParent(monitorTransform, false);
            _audio.transform.localPosition = Vector3.zero;
        }
    }

    private static AudioSource? GetShipSpeaker()
    {
        if (VideoCameraConfig.TransferAudioFromSpeaker == null
            || !VideoCameraConfig.TransferAudioFromSpeaker.Value)
        {
            return null;
        }
        return StartOfRound.Instance != null ? StartOfRound.Instance.speakerAudioSource : null;
    }

    private static void MatchSpeaker(AudioSource speaker, AudioSource target)
    {
        target.outputAudioMixerGroup = speaker.outputAudioMixerGroup;
        target.spatialBlend = 1f;
        target.rolloffMode = speaker.rolloffMode;
        target.minDistance = speaker.minDistance;
        target.maxDistance = speaker.maxDistance;
        target.spread = speaker.spread;
        target.dopplerLevel = speaker.dopplerLevel;
        target.spatialize = speaker.spatialize;
        if (speaker.rolloffMode == AudioRolloffMode.Custom)
        {
            target.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                speaker.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }

        MirrorOcclusion(speaker, target);
    }

    private static void MirrorOcclusion(AudioSource speaker, AudioSource target)
    {
        var source = speaker.GetComponent<OccludeAudio>();
        if (source == null)
        {
            StripOcclusion(target);
            var sourceLowPass = speaker.GetComponent<AudioLowPassFilter>();
            if (sourceLowPass == null) return;

            var lowPass = target.GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = target.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = sourceLowPass.cutoffFrequency;
            lowPass.lowpassResonanceQ = sourceLowPass.lowpassResonanceQ;
            return;
        }

        var occlude = target.GetComponent<OccludeAudio>();
        if (occlude == null) occlude = target.gameObject.AddComponent<OccludeAudio>();
        occlude.useReverb = source.useReverb;
        occlude.overridingLowPass = source.overridingLowPass;
        occlude.lowPassOverride = source.lowPassOverride;
    }

    private static void StripOcclusion(AudioSource target)
    {
        var occlude = target.GetComponent<OccludeAudio>();
        if (occlude != null) UnityEngine.Object.Destroy(occlude);

        var reverb = target.GetComponent<AudioReverbFilter>();
        if (reverb != null) UnityEngine.Object.Destroy(reverb);

        var lowPass = target.GetComponent<AudioLowPassFilter>();
        if (lowPass != null) UnityEngine.Object.Destroy(lowPass);
    }

    private static void ApplyAudioSettings(AudioSource target)
    {
        StripOcclusion(target);
        target.outputAudioMixerGroup = null;
        target.spatialBlend = 1f;
        target.rolloffMode = AudioRolloffMode.Linear;
        target.minDistance = 1f;
        target.maxDistance = Constants.Terminal.PlaybackAudioMaxDistance;
        target.dopplerLevel = 0f;
        target.spread = 0f;
    }

    private static void ClearBlack(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
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
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
        }
        catch (Exception)
        {
        }
        _player = null;
        _audio = null;
        _host = null;
        _rt = null;
    }
}
