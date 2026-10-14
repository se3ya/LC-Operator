using System;
using UnityEngine;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    private static string _playingFileName = string.Empty;

    internal static void OnRunTerminalEvent(Terminal terminal, TerminalNode? node)
    {
        if (terminal == null || node == null) return;
        string evt = node.terminalEvent ?? string.Empty;
        if (!_ourNodes.ContainsKey(evt)) return;

        try
        {
            if (evt == EvtPlay)
            {
                ExecPlay(terminal);
            }
            else if (evt == EvtStop)
            {
                if (TerminalMonitorTransfer.IsActive) TerminalMonitorTransfer.RequestStop();
                else if (TerminalCoWatch.Enabled) TerminalCoWatch.RequestStop();
                else
                {
                    _pendingReceiveFileName = string.Empty;
                    _pendingWait.Clear();
                    StopPlayback(terminal);
                    VideoCameraItem.RetrySegmentCleanup();
                }
            }
            else if (evt == EvtSave)
            {
                ExecSave();
            }
            else if (evt == EvtDelete)
            {
                ExecDelete();
            }
            else if (evt == EvtList)
            {
            }
            else if (evt == EvtHelp)
            {
            }
            else if (evt == EvtSlow)
            {
                BroadcastTransport(TerminalCoWatch.TransportSpeed, 0.5f);
            }
            else if (evt == EvtNormal)
            {
                BroadcastTransport(TerminalCoWatch.TransportSpeed, 1.0f);
            }
            else if (evt == EvtFast)
            {
                BroadcastTransport(TerminalCoWatch.TransportSpeed, 2.0f);
            }
            else if (evt == EvtTurbo)
            {
                BroadcastTransport(TerminalCoWatch.TransportSpeed, 4.0f);
            }
            else if (evt == EvtPause)
            {
                BroadcastTransport(TerminalCoWatch.TransportPause, 0f);
            }
            else if (evt == EvtResume)
            {
                BroadcastTransport(TerminalCoWatch.TransportResume, 0f);
            }
            else if (evt == EvtVolume)
            {
                ExecVolume(terminal);
            }
            else if (evt == EvtTransfer)
            {
                ExecTransfer();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Cassette terminal command '{evt}' failed: {ex}");
        }
    }

    private static void ExecPlay(Terminal terminal)
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id)) return;
        if (!TryFindCassette(id, out string fileName, out string? filePath, out _)) return;

        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();

        if (TerminalMonitorTransfer.IsActive)
        {
            TerminalMonitorTransfer.RequestStop();
        }

        if (TerminalCoWatch.Enabled && !string.IsNullOrEmpty(fileName))
        {
            TerminalCoWatch.RequestPlay(fileName);
        }
        else
        {
            if (string.IsNullOrEmpty(filePath))
            {
                if (CassetteLibrary.IsMergePending(fileName) || CassetteItem.IsEncodingFile(fileName)) WaitForFile(terminal, fileName);
                return;
            }
            _playingFileName = fileName;
            StartPlaybackLocal(terminal, filePath!);
        }
    }

    private static void StartPlaybackLocal(Terminal terminal, string filePath)
    {
        ConfigureVideoPlayer(terminal, filePath);
        _isPlaying = true;
        _joinedLate = false;
        _pendingPlay = false;
        SetupHandler(terminal);
        HidePrepareStatus();
        ShowOverlay();
    }

    private static void ExecTransfer()
    {
        if (!TerminalMonitorTransfer.Enabled) return;

        const int monitorIndex = Constants.Monitor.TransferIndex;

        string fileName = PlayingFileName;
        if (!IsPlaying || string.IsNullOrEmpty(fileName))
        {
            if (!TerminalMonitorTransfer.IsActive) return;
            fileName = MonitorPlayback.ActiveFileName;
            if (string.IsNullOrEmpty(fileName)) return;
            if (monitorIndex == MonitorPlayback.ActiveMonitorIndex) return;
        }

        TerminalMonitorTransfer.RequestPlay(fileName, monitorIndex);
    }

    internal static void ApplyPlayLocally(string fileName)
    {
        Terminal? terminal = GetActiveTerminal();
        if (terminal == null)
        {
            return;
        }

        string? filePath = FindFilePath(fileName);
        if (string.IsNullOrEmpty(filePath))
        {
            WaitForFile(terminal, fileName);
            return;
        }

        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();
        _playingFileName = fileName;
        StartPlaybackLocal(terminal, filePath!);
    }

    internal static bool ApplyJoinLocally(string fileName, double position, float speed, bool paused)
    {
        Terminal? terminal = GetActiveTerminal();
        if (terminal == null) return false;

        string? filePath = FindFilePath(fileName);
        if (string.IsNullOrEmpty(filePath)) return false;

        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();
        _playingFileName = fileName;
        StartPlaybackLocal(terminal, filePath!);
        _joinedLate = true;
        _pendingSeekSeconds = position;
        _userPaused = paused;
        SetSpeed(terminal, speed);
        return true;
    }

    internal static void ApplyStopLocally()
    {
        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();
        HidePrepareStatus();
        Terminal? terminal = GetActiveTerminal();
        StopPlayback(terminal);

        string justPlayed = _playingFileName;
        _playingFileName = string.Empty;
        if (!string.IsNullOrEmpty(justPlayed))
        {
            CassetteLibrary.PruneReceivedClip(justPlayed);
        }
        VideoCameraItem.RetrySegmentCleanup();
    }

    internal static string PlayingFileName => _playingFileName;

    internal static bool IsFileInUse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return string.Equals(_playingFileName, fileName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(_pendingReceiveFileName, fileName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(MonitorPlayback.ActiveFileName, fileName, StringComparison.OrdinalIgnoreCase)
               || TerminalCoWatch.IsPreparingFile(fileName);
    }

    internal static string StopForHandoff()
    {
        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();
        HidePrepareStatus();
        TerminalCoWatch.TrackStop();
        Terminal? terminal = GetActiveTerminal();
        StopPlayback(terminal);

        string justPlayed = _playingFileName;
        _playingFileName = string.Empty;
        return justPlayed;
    }

    internal static bool LocalHasFile(string fileName)
    {
        return !string.IsNullOrEmpty(FindFilePath(fileName));
    }

    internal static void ApplyPrepareLocally(string fileName)
    {
        Terminal? terminal = GetActiveTerminal();
        if (terminal == null) return;
        if (_isPlaying && TerminalCoWatch.Enabled) StopPlayback(terminal);
        SetupHandler(terminal);
        _lastStatusText = string.Empty;
        UpdatePrepareStatus(LocalHasFile(fileName), 0);
    }

    internal static void UpdatePrepareStatus(bool hasFile, int waiting)
    {
        string text = PrepareText.For(hasFile, waiting);
        if (text == _lastStatusText) return;

        Terminal? terminal = GetActiveTerminal();
        if (terminal == null) return;

        ShowPrepareStatus(terminal, text);
    }

    internal static void ApplyAbortLocally()
    {
        HidePrepareStatus();
        if (!_isPlaying)
        {
            HideOverlay();
        }
    }

    private static void WaitForFile(Terminal terminal, string fileName)
    {
        _pendingReceiveFileName = fileName;
        _pendingWait.Start();
        SetupHandler(terminal);
        Plugin.Log.LogDebug($"'{fileName}' hasnt finished transferring to this machine yet, waiting for it.");
    }

    internal static void UpdatePendingReceive(Terminal terminal)
    {
        if (!IsWaitingForFile)
        {
            return;
        }

        string fileName = _pendingReceiveFileName;
        string? filePath = FindFilePath(fileName);
        if (!string.IsNullOrEmpty(filePath))
        {
            _pendingReceiveFileName = string.Empty;
            _pendingWait.Clear();
            Plugin.Log.LogDebug($"'{fileName}' finished transferring, starting playback.");
            _playingFileName = fileName;
            StartPlaybackLocal(terminal, filePath!);
            return;
        }

        bool stalled = _pendingWait.IsStalled();
        if (!stalled && !_pendingWait.IsNothingComing()) return;

        if (stalled) Plugin.Log.LogWarning($"'{fileName}' stopped arriving, so it will not play here.");
        else Plugin.Log.LogInfo($"'{fileName}' is not coming to this machine, so it will not play here.");
        _pendingReceiveFileName = string.Empty;
        _pendingWait.Clear();
        if (!_isPlaying) HideOverlay();
    }

    internal static void ApplyTransportLocally(byte kind, float value, float positionSeconds = -1f)
    {
        if (MonitorPlayback.IsActive)
        {
            MonitorPlayback.SeekTo(positionSeconds);
            switch (kind)
            {
                case TerminalCoWatch.TransportPause: MonitorPlayback.Pause(); break;
                case TerminalCoWatch.TransportResume: MonitorPlayback.Resume(); break;
                case TerminalCoWatch.TransportSpeed: MonitorPlayback.SetSpeed(value); break;
                case TerminalCoWatch.TransportVolume:
                    _sessionPlaybackVolume = UnityEngine.Mathf.Clamp01(value);
                    MonitorPlayback.SetVolume(_sessionPlaybackVolume);
                    break;
            }

            return;
        }

        Terminal? terminal = GetActiveTerminal();
        if (terminal == null)
        {
            if (kind == TerminalCoWatch.TransportVolume) _sessionPlaybackVolume = Mathf.Clamp01(value);
            return;
        }

        if (!_isPlaying && kind != TerminalCoWatch.TransportVolume) return;

        switch (kind)
        {
            case TerminalCoWatch.TransportPause: SeekTo(terminal, positionSeconds); Pause(terminal); break;
            case TerminalCoWatch.TransportResume: SeekTo(terminal, positionSeconds); Resume(terminal); break;
            case TerminalCoWatch.TransportSpeed: SeekTo(terminal, positionSeconds); SetSpeed(terminal, value); break;
            case TerminalCoWatch.TransportVolume:
                _sessionPlaybackVolume = UnityEngine.Mathf.Clamp01(value);
                ApplyPlaybackVolume(terminal);
                break;
        }
    }

    private static void SeekTo(Terminal terminal, float positionSeconds)
    {
        if (positionSeconds < 0f || _overlayPlayer == null) return;
        if (_overlayPlayer.isPrepared)
        {
            _overlayPlayer.time = positionSeconds;
            _pendingSeekSeconds = -1.0;
        }
        else
        {
            _pendingSeekSeconds = positionSeconds;
        }
    }

    private static float CurrentPlaybackSeconds()
    {
        if (MonitorPlayback.IsActive) return MonitorPlayback.CurrentSeconds();
        if (_overlayPlayer == null || !_overlayPlayer.isPrepared) return -1f;
        return (float)_overlayPlayer.time;
    }

    private static void BroadcastTransport(byte kind, float value)
    {
        float positionSeconds = CurrentPlaybackSeconds();
        if (TerminalCoWatch.Enabled) TerminalCoWatch.RequestTransport(kind, value, positionSeconds);
        else ApplyTransportLocally(kind, value, positionSeconds);
    }

    private static string? FindFilePath(string fileName)
    {
        string effective = CassetteLibrary.GetMergedName(fileName);
        foreach (string path in CassetteLibrary.GetCandidatePaths(effective))
        {
            if (System.IO.File.Exists(path)) return path;
        }
        return null;
    }

    internal static string? FindLocalPath(string fileName) => FindFilePath(fileName);

    private static Terminal? GetActiveTerminal()
    {
        if (_activeTerminal != null) return _activeTerminal;
        return UnityEngine.Object.FindObjectOfType<Terminal>();
    }

    private static void ExecSave()
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id)) return;
        string typedKey = CassetteIdGenerator.ToMatchKey(id);
        if (string.IsNullOrEmpty(typedKey)) return;

        var cassette = FindWorldCassette(typedKey);
        if (cassette == null || cassette.IsEncoding) return;

        CassetteLibrary.TrySave(new CassetteLibrary.Entry
        {
            Id = cassette.CassetteId,
            FileName = cassette.FileName,
            Timestamp = cassette.Timestamp,
            Duration = cassette.Duration,
            ScrapValue = cassette.scrapValue,
            Breakdown = cassette.BreakdownRaw,
            SavedAt = DateTime.UtcNow,
        });

        if (VideoCameraConfig.KeepUnsavedPerLobby.Value
            && !string.IsNullOrWhiteSpace(cassette.FileName))
        {
            try
            {
                bool promoted = CassetteLibrary.TryMoveToSaved(cassette.FileName);
                if (!promoted)
                {
                    Plugin.Log.LogWarning($"Recording '{cassette.FileName}' for cassette '{cassette.CassetteId}' was not in '{Constants.Recording.UnsavedFolderName}'.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to save cassette '{cassette.CassetteId}': {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"Saved cassette '{cassette.CassetteId}' to library, recording '{cassette.FileName}', worth ${cassette.scrapValue}.");
    }

    private static void ExecDelete()
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id)) return;

        if (!TryGetLibraryEntry(id, out CassetteLibrary.Entry? entry) || entry == null)
        {
            return;
        }

        if (!CassetteLibrary.TryRemove(entry.Id))
        {
            return;
        }

        int deletedFiles = 0;
        if (VideoCameraConfig.KeepUnsavedPerLobby.Value
            && !string.IsNullOrWhiteSpace(entry.FileName)
            && !CassetteLibrary.HasEntryFor(entry.FileName))
        {
            deletedFiles = CassetteLibrary.DeleteRecordingFiles(entry.FileName);
        }

        Plugin.Log.LogInfo($"Deleted cassette '{entry.Id}' from library." +
                           (deletedFiles > 0 ? $" Removed {deletedFiles} recording files." : string.Empty));
    }

    private static void SetSpeed(Terminal terminal, float speed)
    {
        if (_overlayPlayer == null) return;
        _overlayPlayer.playbackSpeed = Mathf.Clamp(speed, 0.1f, 10f);
    }

    private static void Pause(Terminal terminal)
    {
        _userPaused = true;
        if (_overlayPlayer != null && _overlayPlayer.isPlaying)
        {
            _overlayPlayer.Pause();
        }
    }

    private static void Resume(Terminal terminal)
    {
        _userPaused = false;
        if (_overlayPlayer != null && _overlayPlayer.enabled && _overlayPlayer.isPrepared && !_overlayPlayer.isPlaying)
        {
            _overlayPlayer.Play();
        }
    }

    private static void ExecVolume(Terminal terminal)
    {
        string? rawArg = ArgAfterVerb();
        if (string.IsNullOrEmpty(rawArg)) return;
        if (!TryParseVolumePercent(rawArg, out int parsedPercent)) return;

        BroadcastTransport(TerminalCoWatch.TransportVolume, parsedPercent / 100f);
    }

    private static bool TryParseVolumePercent(string raw, out int percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string cleaned = raw.Trim();
        if (cleaned.EndsWith('%'))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 1);
        }

        if (!int.TryParse(cleaned, out int parsed)) return false;
        if (parsed < 0 || parsed > 100) return false;

        percent = parsed;
        return true;
    }

    internal static float Volume
    {
        get => _sessionPlaybackVolume;
        set
        {
            _sessionPlaybackVolume = Mathf.Clamp01(value);
            MonitorPlayback.SetVolume(_sessionPlaybackVolume);

            Terminal? terminal = GetActiveTerminal();
            AudioSource? playbackAudio = terminal != null ? FindPlaybackAudio(terminal) : null;
            if (playbackAudio != null) playbackAudio.volume = _sessionPlaybackVolume;
        }
    }

    private static int GetVolumePercent()
    {
        return Mathf.RoundToInt(Mathf.Clamp01(_sessionPlaybackVolume) * 100f);
    }

    private static void ApplyPlaybackVolume(Terminal terminal)
    {
        AudioSource? playbackAudio = GetPlaybackAudio(terminal);
        if (playbackAudio == null) return;
        playbackAudio.volume = Mathf.Clamp01(_sessionPlaybackVolume);
    }

    private static AudioSource? GetPlaybackAudio(Terminal terminal)
    {
        if (terminal == null) return null;

        if (_playbackAudioSource != null && _playbackAudioSource.transform.parent == terminal.transform)
        {
            ApplyAudioSettings(_playbackAudioSource, terminal.terminalAudio);
            return _playbackAudioSource;
        }

        Transform host = GetPlaybackHost(terminal, create: true)!;
        _playbackAudioSource = host.GetComponent<AudioSource>();
        if (_playbackAudioSource == null) _playbackAudioSource = host.gameObject.AddComponent<AudioSource>();

        ApplyAudioSettings(_playbackAudioSource, terminal.terminalAudio);
        return _playbackAudioSource;
    }

    private static Transform? GetPlaybackHost(Terminal terminal, bool create)
    {
        Transform? host = terminal.transform.Find(AudioName);
        if (host != null || !create) return host;

        var created = new GameObject(AudioName);
        created.transform.SetParent(terminal.transform, false);
        return created.transform;
    }

    private static AudioSource? FindPlaybackAudio(Terminal terminal)
    {
        if (terminal == null) return null;
        if (_playbackAudioSource != null && _playbackAudioSource.transform.parent == terminal.transform)
        {
            return _playbackAudioSource;
        }

        Transform? host = GetPlaybackHost(terminal, create: false);
        if (host == null) return null;

        _playbackAudioSource = host.GetComponent<AudioSource>();
        return _playbackAudioSource;
    }

    private static void ApplyAudioSettings(AudioSource target, AudioSource? template)
    {
        if (target == null) return;

        target.playOnAwake = false;
        target.loop = false;
        target.mute = false;
        target.spatialize = false;

        if (template == null) return;

        target.outputAudioMixerGroup = template.outputAudioMixerGroup;
        target.priority = template.priority;
        target.panStereo = template.panStereo;
        target.spatialBlend = template.spatialBlend;
        target.reverbZoneMix = template.reverbZoneMix;
        target.dopplerLevel = template.dopplerLevel;
        target.spread = template.spread;
        target.rolloffMode = template.rolloffMode;
        target.minDistance = template.minDistance;
        target.maxDistance = template.maxDistance;
        target.bypassEffects = template.bypassEffects;
        target.bypassListenerEffects = template.bypassListenerEffects;
        target.bypassReverbZones = template.bypassReverbZones;

        if (TerminalCoWatch.Enabled)
        {
            target.spatialBlend = 1f;
            target.rolloffMode = AudioRolloffMode.Linear;
            target.minDistance = 1f;
            target.maxDistance = Constants.Terminal.PlaybackAudioMaxDistance;
            target.dopplerLevel = 0f;
            target.spread = 0f;
        }
    }
}
