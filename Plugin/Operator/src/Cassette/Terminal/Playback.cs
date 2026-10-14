using System;
using UnityEngine;
using UnityEngine.Video;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    private static void ConfigureVideoPlayer(Terminal terminal, string filePath)
    {
        VideoPlayer vp = GetOverlayPlayer(terminal);

        vp.Stop();
        AudioSource? playbackAudio = GetPlaybackAudio(terminal);
        playbackAudio?.Stop();

        UseTexture(Constants.Terminal.OverlayFallbackWidth, Constants.Terminal.OverlayFallbackHeight);
        FitVideo(Constants.Terminal.OverlayFallbackWidth, Constants.Terminal.OverlayFallbackHeight);

        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = _overlayTexture;
        vp.aspectRatio = VideoAspectRatio.Stretch;
        vp.isLooping = false;
        vp.playOnAwake = false;
        vp.playbackSpeed = 1.0f;

        if (playbackAudio != null)
        {
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.controlledAudioTrackCount = 1;
            vp.SetTargetAudioSource(0, playbackAudio);
            ApplyPlaybackVolume(terminal);
        }

        vp.clip = null;
        vp.source = VideoSource.Url;
        _currentPlaybackUrl = "file://" + filePath.Replace('\\', '/');
        vp.url = _currentPlaybackUrl;
        _overlayUrl = _currentPlaybackUrl;
        _userPaused = false;
        _pendingSeekSeconds = -1.0;

        vp.loopPointReached -= OnVideoFinished;
        vp.loopPointReached += OnVideoFinished;
        vp.errorReceived -= OnVideoError;
        vp.errorReceived += OnVideoError;

        vp.prepareCompleted -= OnVideoPrepared;
        vp.prepareCompleted += OnVideoPrepared;
        vp.Prepare();
    }

    internal static void OnLoadTerminalImage(Terminal terminal, TerminalNode? node)
    {
        if (!_pendingPlay) return;
        if (terminal == null || node == null) return;
        if (terminal.videoPlayer == null || terminal.terminalImage == null) return;

        string evt = node.terminalEvent ?? string.Empty;
        string nm = node.name ?? string.Empty;
        if (evt != EvtPlay && nm != "VideoCamCassettePlayNode") return;

        try
        {
            terminal.videoPlayer.enabled = true;
            terminal.terminalImage.enabled = true;
            if (!terminal.videoPlayer.isPlaying) terminal.videoPlayer.Play();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to restore terminal video image: {ex}");
        }
        _pendingPlay = false;
    }

    internal static bool ShouldSkipImage(TerminalNode? node)
    {
        if (!_isPlaying || node == null) return false;
        string evt = node.terminalEvent ?? string.Empty;
        return IsTransportEvent(evt);
    }

    private static bool IsTransportEvent(string evt)
    {
        return evt == EvtSlow || evt == EvtNormal || evt == EvtFast
            || evt == EvtTurbo || evt == EvtPause || evt == EvtResume || evt == EvtVolume;
    }

    private static void OnVideoFinished(VideoPlayer vp)
    {
        var terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
        bool joinedLate = _joinedLate;
        StopPlayback(terminal);

        if (TerminalCoWatch.Enabled && !joinedLate)
        {
            TerminalCoWatch.RequestStop();
        }
    }

    internal static void RefreshPlayback(Terminal terminal)
    {
        if (!_isPlaying) return;
        if (_overlayPlayer == null || _overlayImage == null || _overlayTexture == null) return;

        if (_overlayPlayer.source != VideoSource.Url || _overlayUrl != _currentPlaybackUrl)
        {
            _overlayPlayer.source = VideoSource.Url;
            _overlayPlayer.url = _currentPlaybackUrl;
            _overlayUrl = _currentPlaybackUrl;
            _overlayPlayer.targetTexture = _overlayTexture;
            _overlayPlayer.Prepare();
        }

        if (_pendingSeekSeconds >= 0.0 && _overlayPlayer.isPrepared)
        {
            _overlayPlayer.time = _pendingSeekSeconds;
            _pendingSeekSeconds = -1.0;
        }

        if (_userPaused)
        {
            if (_overlayPlayer.isPlaying) _overlayPlayer.Pause();
        }
        else if (_overlayPlayer.isPrepared && !_overlayPlayer.isPlaying)
        {
            _overlayPlayer.Play();
        }
    }

    private static void OnVideoError(VideoPlayer vp, string message)
    {
        Plugin.Log.LogError($"Cassette playback failed: {message}");
        if (!vp.isPlaying) ApplyStopLocally();
    }

    internal static void OnQuitTerminal(Terminal terminal)
    {
        if (TerminalCoWatch.Enabled && (_isPlaying || TerminalCoWatch.IsPreparingLocally)) return;
        StopPlayback(terminal);
    }

    public static void StopPlayback(Terminal? terminal)
    {
        if (!_isPlaying && (_overlayCanvas == null || !_overlayCanvas.gameObject.activeSelf))
        {
            _pendingPlay = false;
            return;
        }
        _isPlaying = false;
        _joinedLate = false;
        _pendingPlay = false;
        _userPaused = false;
        _pendingSeekSeconds = -1.0;

        try
        {
            if (_overlayPlayer != null)
            {
                _overlayPlayer.Stop();
            }
            AudioSource? playbackAudio = terminal != null ? FindPlaybackAudio(terminal) : null;
            playbackAudio?.Stop();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to stop cassette playback: {ex.Message}");
        }
        finally
        {
            HideOverlay();
        }
    }
}
