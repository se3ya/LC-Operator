using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Operator.Recording;

internal sealed class Recorder
{
    private VideoPipe? _videoPipe;
    private AudioPipe? _audioPipe;
    private AudioPipe? _micPipe;
    private FrameCapture? _frameCapture;
    private AudioCapture? _audioCapture;
    private DissonanceMicSubscriber? _micSubscriber;
    private Operator.Recording.Voice.VoiceChain? _voiceChain;
    private Task? _ffmpegTask;
    private volatile int _ffmpegExitCode = -1;
    private string? _outputPath;
    private Stopwatch? _wallClock;
    private bool _audioEnabled;
    private bool _micEnabled;
    private bool _started;
    private bool _stopped;
    private volatile bool _markedHandoff;
    private int _faultRaised;
    private int _stopRequested;
    private ClipBroadcaster? _broadcaster;
    private CancellationTokenSource? _watchdogCts;
    private CancellationTokenSource? _ffmpegCts;
    private string _videoCodec = "libx264";
    private int _readbackWidth;
    private int _readbackHeight;
    private Stopwatch? _activeClock;
    private volatile bool _captureEnabled;
    private Stopwatch? _finaleClock;
    private volatile bool _deadManStaticHeld;
    private volatile bool _forceHiss;

    public enum FinalePhase { None, Static, Dead, DeadManStatic }
    public enum DeadManMode { None, Static, Live }

    private const double FinaleStaticTime = 1.5;
    private const double FinaleDeadTime = 1.0;

    public FinalePhase CurrentFinalePhase
    {
        get
        {
            if (_deadManStaticHeld) return FinalePhase.DeadManStatic;
            Stopwatch? clock = _finaleClock;
            if (clock == null) return FinalePhase.None;
            return clock.Elapsed.TotalSeconds < FinaleStaticTime ? FinalePhase.Static : FinalePhase.Dead;
        }
    }

    private readonly SynchronizationContext? _mainThread = SynchronizationContext.Current;

    private static AudioListener? _cachedAudioListener;
    private static int _stderrEchoed;

    private static readonly HashSet<string> _writing = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _writingLock = new();

    private volatile string? _lastProgress;
    private readonly List<string> _encoderErrors = new();
    private int _pipesOpen;
    private long _pipesClosedAt;

    public bool IsActive => _started && !_stopped;
    public bool IsCaptureEnabled => _captureEnabled;
    internal FrameCapture? Capture => _frameCapture;
    public string OutputFileName => _outputPath != null ? Path.GetFileName(_outputPath) : string.Empty;
    public double ActiveTime => _activeClock?.Elapsed.TotalSeconds ?? 0.0;

    internal static bool IsWriting(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        lock (_writingLock)
        {
            return _writing.Contains(fileName);
        }
    }

    private void MarkWriting(bool writing)
    {
        string name = OutputFileName;
        if (name.Length == 0) return;
        lock (_writingLock)
        {
            if (writing) _writing.Add(name);
            else _writing.Remove(name);
        }
    }
    internal float SegmentStartRealtime { get; set; }
    public event System.Action<Recorder>? OnStoppedByWatchdog;
    public event System.Action<Recorder>? OnEncoderFaulted;

    internal ShadowVoicePool? ShadowPool { get; set; }
    internal float InitialDuckGain { get; set; } = 1f;
    internal float InitialShadowWeight { get; set; }
    internal bool InitialRoom { get; set; }
    internal bool InitialSubmerged { get; set; }

    public event Action<RecordingResult>? OnRecordingFinalized;

    public readonly struct RecordingResult
    {
        public readonly string? OutputPath;
        public readonly bool FileOk;
        public readonly double Duration;
        public readonly bool EndedByHandoff;
        public readonly float SegmentStartRealtime;

        public RecordingResult(string? outputPath, bool fileOk, double duration, bool endedByHandoff, float segmentStartRealtime)
        {
            OutputPath = outputPath;
            FileOk = fileOk;
            Duration = duration;
            EndedByHandoff = endedByHandoff;
            SegmentStartRealtime = segmentStartRealtime;
        }
    }

    public void MarkHandoff() => _markedHandoff = true;

    private void NudgeAudio()
    {
        _audioPipe?.Nudge();
        _micPipe?.Nudge();
    }

    public void SetAudioDuckGain(float gain) => _audioCapture?.SetDuckGain(gain);

    public void SetAudioShadowWeight(float weight) => _audioCapture?.SetShadowWeight(weight);

    public void SetAudioRoom(bool inside)
    {
        _audioCapture?.SetRoom(inside);
        _voiceChain?.SetRoom(inside);
    }

    public void SetAudioSubmerged(bool submerged)
    {
        _audioCapture?.SetSubmerged(submerged);
        _voiceChain?.SetSubmerged(submerged);
    }

    public void SetAudioHeld(bool held) => _voiceChain?.SetHeld(held);

    public void UpdateMic() => _micSubscriber?.Update();

    private static AudioListener? GetAudioListener()
    {
        if (_cachedAudioListener != null) return _cachedAudioListener;
        _cachedAudioListener = UnityEngine.Object.FindObjectOfType<AudioListener>();
        return _cachedAudioListener;
    }

    public bool Start(Camera handheldCamera, ClipBroadcaster? broadcaster, DeadManMode deadMan = DeadManMode.None)
    {
        if (_started)
        {
            return false;
        }
        _started = true;
        _captureEnabled = true;
        _broadcaster = broadcaster;

        Stopwatch startTimer = Stopwatch.StartNew();
        long micElapsedMs = 0;

        try
        {
            if (handheldCamera == null || handheldCamera.targetTexture == null)
            {
                Plugin.Log.LogError("Cannot start recording, handheld camera or its targetTexture is null.");
                return false;
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Plugin.Log.LogError("Cannot start recording, this GPU does not support async GPU readback.");
                return false;
            }

            int width = handheldCamera.targetTexture.width;
            int height = handheldCamera.targetTexture.height;
            int fps = VideoCameraConfig.GetRecordingFps();
            int crf = Mathf.Clamp(VideoCameraConfig.RecordingCrf.Value, 0, 51);
            int audioBitrateKbps = VideoCameraConfig.GetAudioBitrate();
            int maxQueued = Mathf.Max(2, VideoCameraConfig.MaxQueuedVideoFrames.Value);

            _audioEnabled = VideoCameraConfig.RecordAudio.Value;
            _micEnabled = true;

            if (deadMan != DeadManMode.None)
            {
                _audioEnabled = true;
                _micEnabled = false;
                _forceHiss = true;
                _deadManStaticHeld = deadMan == DeadManMode.Static;
            }

            _outputPath = BuildOutputPath();
            MarkWriting(true);
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);

            (_readbackWidth, _readbackHeight) = VideoCameraConfig.GetReadbackSize(width, height);

            _videoPipe = new VideoPipe(_readbackWidth, _readbackHeight, fps);
            VideoPipe.KeepPoolFor(maxQueued);

            AudioListener? listener = _audioEnabled ? GetAudioListener() : null;
            if (_audioEnabled && listener == null)
            {
                Plugin.Log.LogWarning("RecordAudio is enabled but no AudioListener was found, recording without game audio.");
                _audioEnabled = false;
            }

            int gameChannels = AudioSettings.speakerMode switch
            {
                AudioSpeakerMode.Mono => 1,
                AudioSpeakerMode.Stereo => 2,
                AudioSpeakerMode.Quad => 4,
                AudioSpeakerMode.Surround => 5,
                AudioSpeakerMode.Mode5point1 => 6,
                AudioSpeakerMode.Mode7point1 => 8,
                _ => 2
            };
            int gameSampleRate = AudioSettings.outputSampleRate;
            if (_audioEnabled)
            {
                _audioPipe = new AudioPipe(
                    gameSampleRate, gameChannels,
                    () => _activeClock?.Elapsed.TotalSeconds ?? 0);
            }

            _frameCapture = handheldCamera.gameObject.AddComponent<FrameCapture>();
            _frameCapture.Initialize(handheldCamera, fps, maxQueued, _readbackWidth, _readbackHeight, _videoPipe, () => _captureEnabled, () => CurrentFinalePhase, () => _activeClock?.Elapsed.TotalSeconds ?? 0.0, NudgeAudio);

            if (_audioEnabled && listener != null && _audioPipe != null)
            {
                _audioCapture = listener.gameObject.AddComponent<AudioCapture>();
                _audioCapture.Initialize(_audioPipe, ShadowPool, () => _captureEnabled, () => CurrentFinalePhase, () => _forceHiss,
                    InitialDuckGain, InitialShadowWeight, InitialRoom, InitialSubmerged);
            }

            long micStartedAtMs = startTimer.ElapsedMilliseconds;
            if (_micEnabled)
            {
                _micSubscriber = new DissonanceMicSubscriber();
                bool effectsOn = VideoCameraConfig.MicEnvironmentEffects.Value;
                Action? onOpen = effectsOn ? null : () => _micPipe?.ClearDebt();
                DissonanceMicSubscriber subscriber = _micSubscriber;
                var micResampler = new Operator.Recording.Voice.MicResampler();
                float[] micBuf = new float[4096];
                bool micOk = _micSubscriber.TryStart(samples =>
                {
                    if (samples.Array == null)
                    {
                        return;
                    }
                    int micRate = subscriber.SampleRate;
                    if (_voiceChain != null)
                    {
                        _voiceChain.EnqueueMic(samples, micRate);
                    }
                    else if (_micPipe != null)
                    {
                        int needed = (int)((long)samples.Count * gameSampleRate / Math.Max(1, micRate)) + 8;
                        if (micBuf.Length < needed)
                        {
                            micBuf = new float[needed];
                        }
                        int n = micResampler.Process(samples.Array, samples.Offset, samples.Count, micRate, gameSampleRate, micBuf);
                        _micPipe.EnqueueCopy(micBuf, 0, n);
                    }
                }, () => _captureEnabled, onOpen);

                if (!micOk)
                {
                    _micSubscriber = null;
                    _micEnabled = false;
                }
                else if (effectsOn)
                {
                    _micPipe = new AudioPipe(
                        gameSampleRate, gameChannels,
                        () => _activeClock?.Elapsed.TotalSeconds ?? 0);
                    _voiceChain = new Operator.Recording.Voice.VoiceChain(
                        gameSampleRate, gameChannels,
                        handheldCamera.transform,
                        (buf, _) =>
                        {
                            if (_captureEnabled)
                            {
                                _micPipe?.EnqueueCopy(buf, 0, buf.Length);
                            }
                        });
                }
                else
                {
                    _micPipe = new AudioPipe(
                        gameSampleRate,
                        _micSubscriber.Channels,
                        () => _activeClock?.Elapsed.TotalSeconds ?? 0);
                }
            }

            micElapsedMs = startTimer.ElapsedMilliseconds - micStartedAtMs;

            _videoCodec = VideoEncoder.PreferredVideoCodec;
            (int outW, int outH) = VideoCameraConfig.GetRecordingSize();
            string videoArgs = VideoEncoder.ExtraArgs(_videoCodec, crf, outW, outH, fps);

            int bitrate = audioBitrateKbps;
            (List<FfmpegPipeInput> inputs, string outputArgs) = BuildFfmpegArgs(videoArgs, bitrate, outW, outH);
            string? ffmpegExe = FfmpegLocator.ExePath;
            string outputPath = _outputPath!;
            _ffmpegCts = new CancellationTokenSource();
            CancellationToken ffmpegToken = _ffmpegCts.Token;
            _ffmpegTask = Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(ffmpegExe) || !File.Exists(ffmpegExe))
                    {
                        Plugin.Log.LogError("Cannot record, ffmpeg is not ready.");
                        RaiseEncoderFault();
                        return;
                    }

                    int exitCode = await FfmpegProcess.RunWithPipesAsync(
                        ffmpegExe!, inputs, outputArgs, outputPath, OnEncoderLine, OnPipesConnected, ffmpegToken,
                        () => Volatile.Read(ref _pipesClosedAt)).ConfigureAwait(false);
                    _ffmpegExitCode = exitCode;
                    if (exitCode != 0)
                    {
                        Plugin.Log.LogWarning($"FFmpeg exited with code {exitCode}.");
                        RaiseEncoderFault();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"FFmpeg pipeline failed: {ex}");
                    RaiseEncoderFault();
                }
            });
            _ = WatchPipesAsync(ffmpegToken);
            _wallClock = Stopwatch.StartNew();
            _activeClock = Stopwatch.StartNew();

            int maxMinutes = Mathf.Max(1, VideoCameraConfig.MaxMinutes.Value);
            _watchdogCts = new CancellationTokenSource();
            _ = WatchdogAsync(TimeSpan.FromMinutes(maxMinutes), _watchdogCts.Token);

            Plugin.Log.LogInfo($"Recording started, '{_outputPath}' at {outW}x{outH} and {fps} fps.");
            Plugin.Log.LogDebug($"Recording start took {startTimer.ElapsedMilliseconds} ms on main thread, {micElapsedMs} ms of it subscribing mic, capture {width}x{height}, readback {_readbackWidth}x{_readbackHeight}, queue cap {maxQueued} frames.");
            VideoPipe? videoPipe = _videoPipe;
            AudioPipe? gameAudio = _audioPipe;
            AudioPipe? micAudio = _micPipe;
            RecordingStats.SetProbes(
                () => _activeClock?.Elapsed.TotalSeconds ?? 0.0,
                videoPipe != null ? () => videoPipe.TimeWritten : null,
                gameAudio != null ? () => gameAudio.TimeWritten : null,
                micAudio != null ? () => micAudio.TimeWritten : null);
            RecordingStats.Begin();
            DiskSpace.ClearReport();
            _stderrEchoed = 0;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to start recording: {ex}");
            CleanupOnFailure();
            return false;
        }
    }

    private async Task WatchPipesAsync(CancellationToken token)
    {
        int limit = Wine.IsRunning ? Constants.Recording.PipeConnectTimeoutWine : Constants.Recording.PipeConnectTimeout;
        try
        {
            await Task.Delay(limit, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (Volatile.Read(ref _pipesOpen) != 0 || _stopped) return;
        Plugin.Log.LogError($"FFmpeg did not open its pipes in {limit / 1000} s, this take cannot be recorded.");
        RaiseEncoderFault();
    }

    private void OnPipesConnected()
    {
        Volatile.Write(ref _pipesOpen, 1);
        Plugin.Log.LogDebug($"FFmpeg opened its video pipe {_wallClock?.ElapsedMilliseconds ?? 0} ms after recording started.");
    }

    public void SetCaptureEnabled(bool enabled)
    {
        if (!_started || _stopped)
        {
            return;
        }

        bool changed = _captureEnabled != enabled;
        if (changed)
        {
            _micSubscriber?.Flush();
        }
        _captureEnabled = enabled;
        if (!changed || _activeClock == null)
        {
            return;
        }

        if (enabled)
        {
            _activeClock.Start();
        }
        else
        {
            _activeClock.Stop();
        }
    }

    public void BeginEndCap()
    {
        if (!_started || _stopped || _finaleClock != null)
        {
            return;
        }

        _deadManStaticHeld = false;
        _captureEnabled = true;
        _activeClock?.Start();
        _finaleClock = Stopwatch.StartNew();
        Plugin.Log.LogDebug("Recording finale started.");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(FinaleStaticTime + FinaleDeadTime)).ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to stop recording after finale: {ex.Message}");
            }
        });
    }

    private (List<FfmpegPipeInput> Inputs, string OutputArgs) BuildFfmpegArgs(string videoArgs, int audioBitrateKbps, int width, int height)
    {
        string videoFilter = _readbackWidth == width && _readbackHeight == height
            ? "-vf vflip"
            : $"-vf scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,vflip";
        string outputSync = Constants.Recording.OutputSyncArg;
        string videoOut = $"-c:v {_videoCodec} -movflags +faststart {videoFilter} {videoArgs} {outputSync}";
        string shortest = VideoEncoder.HasShortestBuffer
            ? Constants.Recording.ShortestArg
            : Constants.Recording.ShortestPlainArg;

        var inputs = new List<FfmpegPipeInput>
        {
            new FfmpegPipeInput(_videoPipe!.GetStreamArguments(), _videoPipe.WriteAsync),
        };

        float micReverbAmount = VideoCameraConfig.MicReverbAmount.Value;
        string? micReverb = micReverbAmount > 0.001f ? BuildMicReverbFilter(micReverbAmount) : null;

        if (_audioEnabled && _micEnabled && _audioPipe != null && _micPipe != null)
        {
            inputs.Add(new FfmpegPipeInput(_audioPipe.GetStreamArguments(), _audioPipe.WriteAsync));
            inputs.Add(new FfmpegPipeInput(_micPipe.GetStreamArguments(), _micPipe.WriteAsync));
            const string mixTail = "amix=inputs=2:normalize=0:duration=longest:dropout_transition=2,"
                                   + MicLimiter + "[aout]";
            string micChain = micReverb != null
                ? $"[2:a]{micReverb}[mic];[1:a][mic]{mixTail}"
                : $"[1:a][2:a]{mixTail}";
            return (inputs, $"{videoOut} -filter_complex \"{micChain}\" -map 0:v -map [aout] -c:a aac -b:a {audioBitrateKbps}k {shortest}");
        }

        if (_audioEnabled && _audioPipe != null)
        {
            inputs.Add(new FfmpegPipeInput(_audioPipe.GetStreamArguments(), _audioPipe.WriteAsync));
            return (inputs, $"{videoOut} -c:a aac -b:a {audioBitrateKbps}k {shortest}");
        }

        if (_micEnabled && _micPipe != null)
        {
            inputs.Add(new FfmpegPipeInput(_micPipe.GetStreamArguments(), _micPipe.WriteAsync));
            string af = micReverb != null ? $"{micReverb},{MicLimiter}" : MicLimiter;
            return (inputs, $"{videoOut} -c:a aac -b:a {audioBitrateKbps}k -af \"{af}\" {shortest}");
        }

        return (inputs, videoOut);
    }

    private void OnEncoderLine(string line)
    {
        if (line.IndexOf("frame=", StringComparison.Ordinal) >= 0
            && line.IndexOf("bitrate=", StringComparison.Ordinal) >= 0)
        {
            _lastProgress = line;
        }
        KeepEncoderError(line);
        OnFfmpegStderr(line);
    }

    private void KeepEncoderError(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("amf", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("qsv", StringComparison.OrdinalIgnoreCase) < 0
            && line.IndexOf("encoder", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }
        lock (_encoderErrors)
        {
            if (_encoderErrors.Count < Constants.Recording.EncoderErrorLines) _encoderErrors.Add(line);
        }
    }

    private List<string> GetEncoderErrors()
    {
        lock (_encoderErrors)
        {
            return new List<string>(_encoderErrors);
        }
    }

    private static void OnFfmpegStderr(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        bool isDemuxerNoise =
            line.IndexOf("Error during demuxing: Invalid argument", StringComparison.OrdinalIgnoreCase) >= 0
         || line.IndexOf("Error retrieving a packet from demuxer: Invalid argument", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isDemuxerNoise)
        {
            return;
        }
        if (line.IndexOf("No space left on device", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            DiskSpace.ReportEncoderOutOfSpace();
            return;
        }
        if (TryReadEncoderProgress(line))
        {
            return;
        }
        if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("no such file", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (_stderrEchoed >= Constants.Recording.StderrEchoLimit) return;
            _stderrEchoed++;
            Plugin.Log.LogWarning($"ffmpeg: {line}");
        }
    }

    private static bool TryReadEncoderProgress(string line)
    {
        if (line.IndexOf("bitrate=", StringComparison.Ordinal) < 0
            || line.IndexOf("frame=", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        long frames = ReadLongAfter(line, "frame=");
        double seconds = ReadTimeAfter(line, "time=");
        RecordingStats.OnEncoderProgress(frames, seconds);
        return true;
    }

    private static long ReadLongAfter(string line, string key)
    {
        int at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return 0;
        int i = at + key.Length;
        while (i < line.Length && line[i] == ' ') i++;
        long value = 0;
        while (i < line.Length && line[i] >= '0' && line[i] <= '9')
        {
            value = (value * 10) + (line[i] - '0');
            i++;
        }
        return value;
    }

    private static double ReadTimeAfter(string line, string key)
    {
        int at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return 0.0;
        int i = at + key.Length;
        while (i < line.Length && line[i] == ' ') i++;
        int start = i;
        while (i < line.Length && line[i] != ' ') i++;

        string[] parts = line.Substring(start, i - start).Split(':');
        if (parts.Length != 3) return 0.0;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture, out double h)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out double m)
            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, culture, out double s))
        {
            return 0.0;
        }
        return (h * 3600.0) + (m * 60.0) + s;
    }

    private const string MicLimiter = "alimiter=limit=0.98:level=disabled";

    private static string BuildMicReverbFilter(float amount)
    {
        amount = Mathf.Clamp01(amount);
        float headroom = Constants.Audio.MicEchoHeadroom;
        float decay1 = (0.25f + (0.35f * amount)) / 0.8f / headroom;
        float decay2 = (0.15f + (0.30f * amount)) / 0.8f / headroom;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "aecho={0:0.0000}:1:60|100:{1:0.0000}|{2:0.0000},volume={3:0.##}",
            1f / headroom, decay1, decay2, headroom);
    }

    private async Task WatchdogAsync(TimeSpan limit, CancellationToken token)
    {
        const int PollDelay = 250;
        double remaining = Math.Max(1.0, limit.TotalSeconds);

        while (!_stopped && remaining > 0d)
        {
            try
            {
                await Task.Delay(PollDelay, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (_captureEnabled)
            {
                remaining -= PollDelay / 1000.0;
            }
        }

        if (_stopped)
        {
            return;
        }

        Plugin.Log.LogInfo($"Recording hit the MaxMinutes limit of {limit.TotalMinutes:F0} min, stopping.");
        await RunOnMainThread(() =>
        {
            try
            {
                OnStoppedByWatchdog?.Invoke(this);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Recording watchdog stop handler failed, {ex.Message}");
            }
        }).ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
    }

    private void RaiseEncoderFault()
    {
        if (_stopped || Volatile.Read(ref _stopRequested) != 0) return;
        if (Interlocked.Exchange(ref _faultRaised, 1) == 1) return;

        _ = RunOnMainThread(() =>
        {
            try
            {
                OnEncoderFaulted?.Invoke(this);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Recording encoder fault handler failed, {ex.Message}");
            }
        });
    }

    private Task RunOnMainThread(Action work)
    {
        SynchronizationContext? context = _mainThread;
        if (context == null || SynchronizationContext.Current == context)
        {
            work();
            return Task.CompletedTask;
        }

        var posted = new TaskCompletionSource<bool>();
        context.Post(_ =>
        {
            try
            {
                work();
                posted.TrySetResult(true);
            }
            catch (Exception error)
            {
                posted.TrySetException(error);
            }
        }, null);

        return posted.Task;
    }

    private async Task<bool> WaitForEncoder(Task encoder)
    {
        double deadline = Constants.Recording.EncoderFinalizeTimeout;
        double stallLimit = Constants.Recording.EncoderStallTimeout;

        var clock = Stopwatch.StartNew();
        long lastSize = CurrentOutputBytes();
        double lastGrowth = 0d;

        while (clock.Elapsed.TotalSeconds < deadline)
        {
            Task step = Task.Delay(500);
            if (await Task.WhenAny(encoder, step).ConfigureAwait(false) == encoder) return true;

            long size = CurrentOutputBytes();
            if (size != lastSize)
            {
                lastSize = size;
                lastGrowth = clock.Elapsed.TotalSeconds;
                continue;
            }

            if (clock.Elapsed.TotalSeconds - lastGrowth >= stallLimit)
            {
                Plugin.Log.LogWarning($"FFmpeg stopped writing {stallLimit:0}s ago, giving up on file.");
                return false;
            }
        }

        Plugin.Log.LogWarning($"FFmpeg was still finalizing after {deadline:0}s, file may be incomplete.");
        return false;
    }

    private long CurrentOutputBytes()
    {
        try
        {
            return _outputPath != null && File.Exists(_outputPath) ? new FileInfo(_outputPath).Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    public async Task StopAsync()
    {
        if (!_started || Interlocked.Exchange(ref _stopRequested, 1) == 1)
        {
            return;
        }

        double minSeconds = Constants.Recording.MinDuration;
        double waitCapSeconds = minSeconds + Constants.Recording.MinDurationGrace;
        while (_captureEnabled
            && (_activeClock?.Elapsed.TotalSeconds ?? minSeconds) < minSeconds
            && (_wallClock?.Elapsed.TotalSeconds ?? waitCapSeconds) < waitCapSeconds)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        _stopped = true;
        _captureEnabled = false;
        _watchdogCts?.Cancel();
        _activeClock?.Stop();

        try
        {
            await RunOnMainThread(() =>
            {
                RecordingStats.End();
                _frameCapture?.Stop();
                _audioCapture?.Stop();
                _micSubscriber?.Stop();
                _micSubscriber = null;
                _voiceChain?.Stop();

                if (_frameCapture != null)
                {
                    UnityEngine.Object.Destroy(_frameCapture);
                    _frameCapture = null;
                }
                if (_audioCapture != null)
                {
                    UnityEngine.Object.Destroy(_audioCapture);
                    _audioCapture = null;
                }
            }).ConfigureAwait(false);

            double active = _activeClock?.Elapsed.TotalSeconds ?? 0.0;
            _videoPipe?.Complete(active);
            _audioPipe?.Complete(active);
            _micPipe?.Complete(active);
            Volatile.Write(ref _pipesClosedAt, Stopwatch.GetTimestamp());
            var drainClock = Stopwatch.StartNew();

            bool ffmpegOk = false;
            if (_ffmpegTask != null)
            {
                ffmpegOk = await WaitForEncoder(_ffmpegTask).ConfigureAwait(false);
            }

            Plugin.Log.LogDebug($"FFmpeg finished {drainClock.ElapsedMilliseconds} ms after pipes closed, exit code {_ffmpegExitCode}.");
            string? lastProgress = _lastProgress;
            if (!string.IsNullOrEmpty(lastProgress))
            {
                Plugin.Log.LogDebug($"Last encoder progress: {lastProgress}");
            }

            _ffmpegCts?.Cancel();

            long outputBytes = _outputPath != null && File.Exists(_outputPath) ? new FileInfo(_outputPath).Length : 0;
            bool fileOk = outputBytes >= Constants.Recording.MinOutputSize;
            bool encodeOk = _ffmpegExitCode == 0;
            bool recordingOk = fileOk && encodeOk;
            double finalSeconds = _activeClock?.Elapsed.TotalSeconds ?? _wallClock?.Elapsed.TotalSeconds ?? 0;
            bool endedByHandoff = _markedHandoff;
            if (recordingOk)
            {
                long bytes = new FileInfo(_outputPath!).Length;
                Plugin.Log.LogInfo($"Recording finalized, '{Path.GetFileName(_outputPath)}' ({bytes / (1024.0 * 1024.0):F1} MB, {finalSeconds:F1} s active).");
            }
            else if (!fileOk)
            {
                Plugin.Log.LogError($"Recording stopped but expected file '{_outputPath}' is missing or too small at {outputBytes} bytes.");
                VideoEncoder.Strike(_videoCodec, GetEncoderErrors());
            }
            else
            {
                Plugin.Log.LogError($"Recording discarded, ffmpeg exited with code {_ffmpegExitCode} so '{Path.GetFileName(_outputPath)}' is likely unplayable.");
                VideoEncoder.Strike(_videoCodec, GetEncoderErrors());
            }

            bool doBroadcast = ffmpegOk && recordingOk
                && VideoCameraConfig.BroadcastRecordingsToClients.Value
                && _broadcaster != null
                && _outputPath != null;
            string? outputPath = _outputPath;
            ClipBroadcaster? broadcaster = _broadcaster;

            void FinalizeOnMainThread()
            {
                MarkWriting(false);

                if (doBroadcast && broadcaster != null && outputPath != null)
                {
                    try { broadcaster.BeginBroadcast(outputPath); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"Failed to send recording to other players: {ex}"); }
                }

                try
                {
                    OnRecordingFinalized?.Invoke(new RecordingResult(outputPath, recordingOk, finalSeconds, endedByHandoff, SegmentStartRealtime));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to hand finished recording to camera: {ex}");
                }

                _voiceChain?.Dispose();
                _voiceChain = null;
            }

            if (_mainThread != null)
            {
                _mainThread.Post(_ => FinalizeOnMainThread(), null);
            }
            else
            {
                FinalizeOnMainThread();
            }
        }
        catch (Exception ex)
        {
            _videoPipe?.Complete();
            _audioPipe?.Complete();
            _micPipe?.Complete();
            _ffmpegCts?.CancelAfter(TimeSpan.FromSeconds(Constants.Recording.EncoderFinalizeTimeout));
            MarkWriting(false);
            Plugin.Log.LogError($"Error while stopping recording: {ex}");
        }
    }

    private void CleanupOnFailure()
    {
        MarkWriting(false);
        if (_frameCapture != null) UnityEngine.Object.Destroy(_frameCapture);
        if (_audioCapture != null) UnityEngine.Object.Destroy(_audioCapture);
        _micSubscriber?.Stop();
        _voiceChain?.Stop();
        _voiceChain?.Dispose();
        _voiceChain = null;
        _frameCapture = null;
        _audioCapture = null;
        _micSubscriber = null;
        _videoPipe?.Complete();
        _audioPipe?.Complete();
        _micPipe?.Complete();
        _videoPipe = null;
        _audioPipe = null;
        _micPipe = null;
        _watchdogCts?.Cancel();
        _ffmpegCts?.Cancel();
    }

    private static string BuildOutputPath()
    {
        string name = $"VideoCamera_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_c{GetLocalClientId()}.mp4";
        return Path.Combine(GetActiveRecordingDirectory(), name);
    }

    private static ulong GetLocalClientId()
    {
        var net = Unity.Netcode.NetworkManager.Singleton;
        return net.IsListening ? net.LocalClientId : 0UL;
    }

    public static string GetOutputDirectory()
    {
        string overrideDir = VideoCameraConfig.OutputDirectoryOverride.Value;
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Wine.IsRunning && overrideDir.StartsWith('/')
                ? "Z:" + overrideDir.Replace('/', '\\')
                : overrideDir;
        }

        return Path.Combine(
            Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "ZeekerssRBLX", "Lethal Company", "Recordings");
    }

    public static string GetUnsavedOutputDirectory()
    {
        return Path.Combine(GetOutputDirectory(), Constants.Recording.UnsavedFolderName);
    }

    public static string GetLobbyOutputDirectory()
    {
        string save = Operator.Cassette.LobbySaveKey.Current;
        if (string.IsNullOrWhiteSpace(save))
        {
            return GetOutputDirectory();
        }
        return Path.Combine(GetOutputDirectory(), "Lobbies", save);
    }

    public static string? RetainForLobby(string fileName, string? lobbyDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        string lobbyDir = string.IsNullOrWhiteSpace(lobbyDirectory) ? GetLobbyOutputDirectory() : lobbyDirectory!;
        string dest = Path.Combine(lobbyDir, fileName);
        if (File.Exists(dest)) return dest;

        string unsaved = Path.Combine(GetUnsavedOutputDirectory(), fileName);
        string persistent = Path.Combine(GetOutputDirectory(), fileName);

        try
        {
            if (File.Exists(unsaved))
            {
                Directory.CreateDirectory(lobbyDir);
                File.Move(unsaved, dest);
                return dest;
            }
            if (File.Exists(persistent))
            {
                return persistent;
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"Could not keep '{fileName}' for this lobby: {ex.Message}");
        }
        return null;
    }

    public static string GetActiveRecordingDirectory()
    {
        return VideoCameraConfig.KeepUnsavedPerLobby.Value
            ? GetUnsavedOutputDirectory()
            : GetOutputDirectory();
    }
}
