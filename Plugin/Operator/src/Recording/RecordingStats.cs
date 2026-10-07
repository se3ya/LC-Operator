using System;
using UnityEngine;

namespace Operator.Recording;

internal static class RecordingStats
{
    private static bool _active;
    private static float _lastFrameTime;
    private static float _windowStart;
    private static int _frames;
    private static float _worstFrameTime;
    private static int _stallFrames;
    private static int _rented;
    private static int _allocated;
    private static int _peakQueueDepth;
    private static double _readbackTime;
    private static int _readbacks;

    private static int _paddedFrames;
    private static int _queueFullEvents;
    private static float _freezeStart;
    private static float _longestFreeze;
    private static float _windowLongestFreeze;

    private static long _ffmpegFrames;
    private static double _ffmpegTime;
    private static int _encoderReports;

    private static int _totalPaddedFrames;
    private static int _totalQueueFullEvents;
    private static long _totalEmittedFrames;
    private static int _totalReadbacks;
    private static int _failedReadbacks;

    private static Func<double>? _activeTime;
    private static Func<double>? _videoTime;
    private static Func<double>? _gameAudioTime;
    private static Func<double>? _micTime;

    internal static void SetProbes(Func<double>? activeTime, Func<double>? videoTime, Func<double>? gameAudioTime, Func<double>? micTime)
    {
        _activeTime = activeTime;
        _videoTime = videoTime;
        _gameAudioTime = gameAudioTime;
        _micTime = micTime;
    }

    internal static void Begin()
    {
        _lastFrameTime = Time.realtimeSinceStartup;
        _windowStart = _lastFrameTime;
        _freezeStart = 0f;
        _longestFreeze = 0f;
        _ffmpegFrames = 0;
        _ffmpegTime = 0.0;
        _encoderReports = 0;
        _totalPaddedFrames = 0;
        _totalQueueFullEvents = 0;
        _totalEmittedFrames = 0;
        _totalReadbacks = 0;
        _failedReadbacks = 0;
        ResetWindow();
        _active = true;
    }

    internal static void End()
    {
        if (!_active) return;
        _active = false;
        OnQueueDrained();
        Report("stopped");
        ReportSummary();
        _activeTime = null;
        _videoTime = null;
        _gameAudioTime = null;
        _micTime = null;
    }

    internal static void OnFrame(int queueDepth)
    {
        if (!_active) return;

        float now = Time.realtimeSinceStartup;
        float frameMs = (now - _lastFrameTime) * 1000f;
        _lastFrameTime = now;

        _frames++;
        if (frameMs > _worstFrameTime) _worstFrameTime = frameMs;
        if (frameMs >= Constants.Recording.DiagnosticsStallThreshold) _stallFrames++;
        if (queueDepth > _peakQueueDepth) _peakQueueDepth = queueDepth;

        if (now - _windowStart >= Constants.Recording.DiagnosticsWindow) Report("running");
    }

    internal static void OnFramesEmitted(int repeats)
    {
        if (!_active) return;
        _totalEmittedFrames += repeats;
        if (repeats > 1)
        {
            _paddedFrames += repeats - 1;
            _totalPaddedFrames += repeats - 1;
        }
    }

    internal static void OnQueueFull()
    {
        if (!_active) return;
        _queueFullEvents++;
        _totalQueueFullEvents++;
        if (_freezeStart <= 0f) _freezeStart = Time.realtimeSinceStartup;
    }

    internal static void OnQueueDrained()
    {
        if (!_active || _freezeStart <= 0f) return;
        float held = Time.realtimeSinceStartup - _freezeStart;
        _freezeStart = 0f;
        if (held > _longestFreeze) _longestFreeze = held;
        if (held > _windowLongestFreeze) _windowLongestFreeze = held;
    }

    internal static void OnEncoderProgress(long frames, double outputSeconds)
    {
        if (!_active) return;
        _ffmpegFrames = frames;
        _ffmpegTime = outputSeconds;
        _encoderReports++;
    }

    internal static void OnBufferRented(bool allocated)
    {
        if (!_active) return;
        _rented++;
        if (allocated) _allocated++;
    }

    internal static void OnReadback(float milliseconds)
    {
        if (!_active) return;
        _readbacks++;
        _totalReadbacks++;
        _readbackTime += milliseconds;
    }

    internal static void OnReadbackFailed()
    {
        if (!_active) return;
        _failedReadbacks++;
    }

    private static void Report(string phase)
    {
        float now = Time.realtimeSinceStartup;
        float window = Mathf.Max(0.001f, now - _windowStart);
        float fps = _frames / window;
        double readbackMs = _readbacks > 0 ? _readbackTime / _readbacks : 0.0;

        Plugin.Log.LogDebug(
            $"Recording {phase}, {fps:F0} fps over {window:F1}s, worst frame {_worstFrameTime:F0} ms, " +
            $"{_stallFrames} frames over {Constants.Recording.DiagnosticsStallThreshold:F0} ms, peak queue {_peakQueueDepth}, " +
            $"{_allocated} of {_rented} frame buffers allocated, readback {readbackMs:F1} ms.");

        double active = _activeTime?.Invoke() ?? 0.0;
        float ongoingFreeze = _freezeStart > 0f ? now - _freezeStart : 0f;
        Plugin.Log.LogDebug(
            $"Recording pipeline, {active:F1}s active, {_paddedFrames} padded frames, " +
            $"{_queueFullEvents} queue stalls, longest {Mathf.Max(_windowLongestFreeze, ongoingFreeze):F1}s, " +
            $"{DescribePipes()}, encoder at {_ffmpegTime:F1}s on frame {_ffmpegFrames} from {_encoderReports} reports, " +
            $"behind {Math.Max(0.0, active - _ffmpegTime):F1}s.");

        _windowStart = now;
        ResetWindow();
    }

    private static void ReportSummary()
    {
        double active = _activeTime?.Invoke() ?? 0.0;
        double padShare = _totalEmittedFrames > 0 ? 100.0 * _totalPaddedFrames / _totalEmittedFrames : 0.0;

        Plugin.Log.LogDebug(
            $"Recording summary, {active:F1}s active, {_totalEmittedFrames} frames emitted, " +
            $"{_totalPaddedFrames} padded at {padShare:F0} percent, {_totalQueueFullEvents} queue stalls, " +
            $"longest freeze {_longestFreeze:F1}s, {DescribePipes()}, " +
            $"encoder at stop {_ffmpegTime:F1}s on frame {_ffmpegFrames} from {_encoderReports} reports, " +
            $"behind {Math.Max(0.0, active - _ffmpegTime):F1}s.");

        if (_failedReadbacks == 0) return;
        if (_failedReadbacks >= _totalReadbacks)
        {
            Plugin.Log.LogError($"Every GPU readback failed in this take ({_failedReadbacks}), it has no picture.");
            return;
        }
        Plugin.Log.LogWarning($"{_failedReadbacks} of {_totalReadbacks} GPU readbacks failed in this take, those frames repeat the last picture.");
    }

    private static string DescribePipes()
    {
        string video = _videoTime != null ? $"{_videoTime():F1}s" : "off";
        string game = _gameAudioTime != null ? $"{_gameAudioTime():F1}s" : "off";
        string mic = _micTime != null ? $"{_micTime():F1}s" : "off";
        return $"pipes video {video}, game audio {game}, mic {mic}";
    }

    private static void ResetWindow()
    {
        _frames = 0;
        _worstFrameTime = 0f;
        _stallFrames = 0;
        _rented = 0;
        _allocated = 0;
        _peakQueueDepth = 0;
        _readbackTime = 0.0;
        _readbacks = 0;
        _paddedFrames = 0;
        _queueFullEvents = 0;
        _windowLongestFreeze = 0f;
    }
}
