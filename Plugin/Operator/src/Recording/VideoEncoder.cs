using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Operator.Recording;

internal static class VideoEncoder
{
    private const string Software = "libx264";
    private static readonly string[] AutoOrder = { "h264_nvenc", "h264_qsv", "h264_amf" };
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Strikes = new();
    private static readonly HashSet<string> Dropped = new();
    private static readonly HashSet<string> Reported = new();
    private static Task? _probe;
    private static int _generation;
    private static volatile bool _ready;
    private static string _videoCodec = Software;
    private static int _variant;
    private static bool _hasShortestBuffer;

    private enum Cause
    {
        Missing,
        OldDriver,
        Busy,
        Other,
    }

    public static string PreferredVideoCodec
    {
        get
        {
            StartProbe();
            if (_ready) return _videoCodec;
            Plugin.Log.LogDebug("The encoder probe is still running, this take records on the CPU.");
            return Software;
        }
    }

    public static bool HasShortestBuffer
    {
        get
        {
            StartProbe();
            return _ready && _hasShortestBuffer;
        }
    }

    public static Task PrewarmAsync()
    {
        StartProbe();
        lock (Gate)
        {
            return _probe ?? Task.CompletedTask;
        }
    }

    public static void Restart()
    {
        lock (Gate)
        {
            _generation++;
            _ready = false;
            _videoCodec = Software;
            _variant = 0;
            _probe = null;
            Strikes.Clear();
            Dropped.Clear();
        }
        StartProbe();
    }

    public static void Strike(string codec, IReadOnlyList<string> errors)
    {
        if (codec == Software) return;
        (Cause cause, string line) = Classify(errors);
        if (cause == Cause.Other && !HasEncoderLine(errors, codec)) return;

        bool drop;
        lock (Gate)
        {
            Strikes.TryGetValue(codec, out int count);
            Strikes[codec] = ++count;
            drop = count >= 2;
        }

        if (!drop)
        {
            Plugin.Log.LogWarning($"Recording with {codec} failed, trying it again on the next take. {line}");
            return;
        }
        Plugin.Log.LogWarning($"Recording with {codec} failed twice, switching to the next encoder. {line}");
        Drop(codec);
    }

    public static string ExtraArgs(string codec, int quality, int width, int height, int fps)
    {
        int variant = codec == _videoCodec ? _variant : 0;
        return Args(codec, variant, quality, width, height, fps);
    }

    public static Task RewarmAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                await PrewarmAsync().ConfigureAwait(false);
                string? exe = FfmpegLocator.ExePath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

                string codec = _videoCodec;
                if (!_ready || codec == Software) return;

                var timer = Stopwatch.StartNew();
                var errors = new List<string>();
                int exit = await WarmPipeEncodeAsync(exe!, codec, errors).ConfigureAwait(false);
                if (exit != 0)
                {
                    await Task.Delay(Constants.Recording.EncoderRetryDelay).ConfigureAwait(false);
                    errors.Clear();
                    exit = await WarmPipeEncodeAsync(exe!, codec, errors).ConfigureAwait(false);
                }
                if (exit != 0)
                {
                    (Cause cause, string line) = Classify(errors);
                    if (cause == Cause.Other && !HasEncoderLine(errors, codec))
                    {
                        Plugin.Log.LogDebug($"The {codec} warm-up encode exited with {exit}, not an encoder failure. {line}");
                        return;
                    }
                    Plugin.Log.LogWarning($"The {codec} warm-up encode failed twice, switching to the next encoder. {line}");
                    Drop(codec);
                    return;
                }
                Plugin.Log.LogDebug($"Warmed the recording pipeline with a {codec} encode in {timer.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Could not warm the recording pipeline: {ex.Message}");
            }
        });
    }

    private static async Task<int> WarmPipeEncodeAsync(string exePath, string codec, List<string> errors)
    {
        (int width, int height) = VideoCameraConfig.GetRecordingSize();
        int fps = VideoCameraConfig.GetRecordingFps();

        var pipe = new VideoPipe(width, height, fps);
        int frameLength = width * height * 4;
        for (int i = 0; i < Constants.Recording.WarmupFrameCount; i++)
        {
            pipe.Enqueue(new byte[frameLength], 1);
        }
        pipe.Complete();

        string outputArgs = $"-c:v {codec} -movflags +faststart -vf scale={width}:{height},vflip " +
                            $"{ExtraArgs(codec, VideoCameraConfig.RecordingCrf.Value, width, height, fps)} {Constants.Recording.OutputSyncArg}";

        var inputs = new List<FfmpegPipeInput>
        {
            new FfmpegPipeInput(pipe.GetStreamArguments(), pipe.WriteAsync),
        };

        string outputPath = Path.Combine(Path.GetTempPath(), $"operator_warmup_{Guid.NewGuid():N}.mp4");
        try
        {
            using var cts = new CancellationTokenSource(Constants.Recording.EncoderProbeTimeout);
            return await FfmpegProcess.RunWithPipesAsync(exePath, inputs, outputArgs, outputPath, line => Keep(errors, line), null, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    private static void StartProbe()
    {
        lock (Gate)
        {
            if (_probe != null) return;
            int generation = _generation;
            _probe = Task.Run(() => ProbeAsync(generation));
        }
    }

    private static void Drop(string codec)
    {
        lock (Gate)
        {
            Dropped.Add(codec);
            if (codec != _videoCodec) return;
            _generation++;
            _ready = false;
            _videoCodec = Software;
            _variant = 0;
            _probe = null;
        }
        StartProbe();
    }

    private static async Task ProbeAsync(int generation)
    {
        try
        {
            await FfmpegLocator.InitAsync().ConfigureAwait(false);
            string? exe = FfmpegLocator.ExePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Publish(generation, Software, 0, false);
                return;
            }

            if (FfmpegLocator.Version != null && FirstReport("version"))
            {
                Plugin.Log.LogInfo($"Using FFmpeg {FfmpegLocator.Version}.");
            }

            bool shortest = TestShortestBuffer(exe!);
            if (!shortest && FirstReport("shortest"))
            {
                Plugin.Log.LogInfo("This ffmpeg has no shortest buffer option, recordings will take longer to finish.");
            }

            EncoderChoice choice = VideoCameraConfig.Encoder.Value;
            if (choice == EncoderChoice.Auto && Wine.IsRunning)
            {
                if (FirstReport("wine"))
                {
                    Plugin.Log.LogInfo($"Running under Wine {Wine.Version}, recording on the CPU. Set Encoder to try a graphics card anyway.");
                }
                Publish(generation, Software, 0, shortest);
                return;
            }

            foreach (string codec in Candidates(choice))
            {
                lock (Gate)
                {
                    if (Dropped.Contains(codec)) continue;
                }

                IReadOnlyList<string> errors = Array.Empty<string>();
                int variants = codec == "h264_amf" ? 3 : 1;
                for (int variant = 0; variant < variants; variant++)
                {
                    (bool ok, List<string> lines) = TestEncode(exe!, codec, variant);
                    if (ok)
                    {
                        Publish(generation, codec, variant, shortest);
                        return;
                    }
                    errors = lines;
                    if (Classify(lines).Cause == Cause.Missing) break;
                }
                Report(codec, choice, errors);
            }

            Publish(generation, Software, 0, shortest);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Encoder probe failed, recording on the CPU. {ex.Message}");
            Publish(generation, Software, 0, false);
        }
    }

    private static IEnumerable<string> Candidates(EncoderChoice choice)
    {
        return choice switch
        {
            EncoderChoice.Nvidia => new[] { "h264_nvenc" },
            EncoderChoice.Intel => new[] { "h264_qsv" },
            EncoderChoice.Amd => new[] { "h264_amf" },
            EncoderChoice.Cpu => Array.Empty<string>(),
            _ => AutoOrder,
        };
    }

    private static void Publish(int generation, string codec, int variant, bool shortest)
    {
        lock (Gate)
        {
            if (generation != _generation) return;
            _videoCodec = codec;
            _variant = variant;
            _hasShortestBuffer = shortest;
            _ready = true;
        }
        Plugin.Log.LogInfo(codec == Software
            ? "Recording will use video encoder libx264 (software)."
            : $"Recording will use video encoder {codec}.");
    }

    private static void Report(string codec, EncoderChoice choice, IReadOnlyList<string> errors)
    {
        (Cause cause, string line) = Classify(errors);
        if (!FirstReport($"{codec}:{cause}")) return;

        string vendor = codec switch
        {
            "h264_nvenc" => "NVIDIA",
            "h264_qsv" => "Intel",
            _ => "AMD",
        };
        switch (cause)
        {
            case Cause.Missing when choice == EncoderChoice.Auto:
                Plugin.Log.LogDebug($"No {vendor} encoder here. {line}");
                break;
            case Cause.Missing:
                Plugin.Log.LogWarning($"Could not use the {vendor} encoder picked in Encoder, recording on the CPU. {line}");
                break;
            case Cause.OldDriver:
                Plugin.Log.LogInfo($"Could not use the NVIDIA encoder, the driver is too old for this ffmpeg. {line}");
                break;
            case Cause.Busy:
                Plugin.Log.LogWarning($"The {vendor} encoder is busy, other apps are using all its sessions. {line}");
                break;
            default:
                Plugin.Log.LogInfo($"Could not use the {vendor} encoder. {line}");
                break;
        }
    }

    private static (Cause Cause, string Line) Classify(IReadOnlyList<string> errors)
    {
        foreach (string line in errors)
        {
            if (line.Contains("required nvenc API version")) return (Cause.OldDriver, line);
            if (line.Contains("incompatible client key") || line.Contains("out of memory (10)")) return (Cause.Busy, line);
        }
        foreach (string line in errors)
        {
            if (line.Contains("Cannot load nvcuda") || line.Contains("Cannot load nvEncodeAPI")
                || line.Contains("Could not dynamically load CUDA") || line.Contains("No CUDA capable")
                || line.Contains("unsupported device (2)") || line.Contains("amfrt64.dll")
                || line.Contains("Error creating a MFX session"))
            {
                return (Cause.Missing, line);
            }
        }
        foreach (string line in errors)
        {
            if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return (Cause.Other, line);
            }
        }
        return (Cause.Other, errors.Count > 0 ? errors[0] : "No error output.");
    }

    private static bool HasEncoderLine(IReadOnlyList<string> errors, string codec)
    {
        string prefix = "[" + codec + " @";
        foreach (string line in errors)
        {
            if (line.IndexOf(prefix, StringComparison.Ordinal) >= 0
                || line.IndexOf("opening encoder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool FirstReport(string key)
    {
        lock (Gate)
        {
            return Reported.Add(key);
        }
    }

    private static string Args(string codec, int variant, int quality, int width, int height, int fps)
    {
        int crf = Math.Clamp(quality, Constants.ConfigRanges.CrfMin, Constants.ConfigRanges.CrfMax);
        long t = (long)(Constants.Recording.AmfBaseRate * Math.Pow(width * height / Constants.Recording.AmfBaseArea, Constants.Recording.AmfAreaPower)
            * Math.Sqrt(fps / 24.0) * Math.Pow(2, (23 - crf) / 6.0));
        int qp = Math.Min(crf + Constants.Recording.AmfQpOffset, Constants.Recording.MaxQp);
        return codec switch
        {
            "h264_nvenc" => $"-preset p4 -tune hq -rc vbr -cq {crf} -b:v 0 -pix_fmt yuv420p",
            "h264_qsv" => $"-preset veryfast -global_quality {crf} -look_ahead 0 -pix_fmt yuv420p",
            "h264_amf" when variant == 0 => $"-usage transcoding -quality quality -rc vbr_peak -b:v {t} -maxrate {t * 3 / 2} -bufsize {t * 2} -max_b_frames 0 -bf 0 -pix_fmt yuv420p",
            "h264_amf" when variant == 1 => $"-usage transcoding -quality quality -rc cbr -b:v {t} -maxrate {t} -bufsize {t * 2} -max_b_frames 0 -bf 0 -pix_fmt yuv420p",
            "h264_amf" => $"-quality speed -rc cqp -qp_i {qp} -qp_p {qp} -qp_b {qp} -pix_fmt yuv420p",
            _ => $"-preset veryfast -crf {crf} -threads 2 -pix_fmt yuv420p",
        };
    }

    private static (int Width, int Height) GetProbeSize()
    {
        (int width, int height) = VideoCameraConfig.GetRecordingSize();
        return (Math.Max(Constants.Recording.EncoderProbeMinWidth, width),
                Math.Max(Constants.Recording.EncoderProbeMinHeight, height));
    }

    private static bool TestShortestBuffer(string exePath)
    {
        string args = "-hide_banner -loglevel error -f lavfi -i anullsrc=r=48000:cl=stereo -t 0.05 " +
                      $"{Constants.Recording.ShortestArg} -f null -";
        try
        {
            return RunExitCode(exePath, args, Constants.Recording.EncoderProbeTimeout, new List<string>()) == 0;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Probe for shortest buffer option failed: {ex.Message}");
            return false;
        }
    }

    private static (bool Ok, List<string> Errors) TestEncode(string exePath, string codec, int variant)
    {
        (int width, int height) = GetProbeSize();
        int fps = VideoCameraConfig.GetRecordingFps();
        string extra = Args(codec, variant, VideoCameraConfig.RecordingCrf.Value, width, height, fps);
        string args = $"-hide_banner -loglevel error -f lavfi -i testsrc=size={width}x{height}:rate={fps},format=rgba -t 0.5 " +
                      $"-c:v {codec} -vf vflip {extra} {Constants.Recording.OutputSyncArg} -f null -";
        var errors = new List<string>();
        try
        {
            return (RunExitCode(exePath, args, Constants.Recording.EncoderProbeTimeout, errors) == 0, errors);
        }
        catch (Exception ex)
        {
            Keep(errors, ex.Message);
            return (false, errors);
        }
    }

    private static int RunExitCode(string exePath, string args, int timeoutMs, List<string> errors)
    {
        ProcessStartInfo psi = new(exePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg probe.");
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, e) => Keep(errors, e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(); } catch { }
            throw new TimeoutException($"ffmpeg probe exceeded {timeoutMs}ms.");
        }

        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static void Keep(List<string> errors, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (errors)
        {
            if (errors.Count >= Constants.Recording.EncoderErrorLines) return;
            errors.Add(line!.Length > 300 ? line.Substring(0, 300) : line);
        }
    }
}
