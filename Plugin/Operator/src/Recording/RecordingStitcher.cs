using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Operator.Recording;

internal static class RecordingStitcher
{
    private readonly struct SegmentProfile : IEquatable<SegmentProfile>
    {
        public readonly string VideoCodec;
        public readonly string VideoProfile;
        public readonly string Encoder;
        public readonly int Width;
        public readonly int Height;
        public readonly string Fps;
        public readonly string AudioCodec;
        public readonly string AudioRate;
        public readonly string AudioLayout;

        public SegmentProfile(string videoCodec, string videoProfile, string encoder, int width, int height, string fps,
                              string audioCodec, string audioRate, string audioLayout)
        {
            VideoCodec = videoCodec;
            VideoProfile = videoProfile;
            Encoder = encoder;
            Width = width;
            Height = height;
            Fps = fps;
            AudioCodec = audioCodec;
            AudioRate = audioRate;
            AudioLayout = audioLayout;
        }

        public bool Equals(SegmentProfile other) =>
            VideoCodec == other.VideoCodec && VideoProfile == other.VideoProfile && Encoder == other.Encoder &&
            Width == other.Width && Height == other.Height && Fps == other.Fps &&
            AudioCodec == other.AudioCodec && AudioRate == other.AudioRate && AudioLayout == other.AudioLayout;

        public override bool Equals(object obj) => obj is SegmentProfile other && Equals(other);

        public override int GetHashCode() => (VideoCodec, VideoProfile, Encoder, Width, Height, Fps, AudioCodec, AudioRate, AudioLayout).GetHashCode();

        public override string ToString() =>
            $"{VideoCodec}/{VideoProfile} {Encoder} {Width}x{Height}@{Fps} + {AudioCodec} {AudioRate} {AudioLayout}";
    }

    private static bool TryProbeProfile(string path, out SegmentProfile profile)
    {
        profile = default;
        try
        {
            RunSync($"-hide_banner -i \"{path}\"", out string stderr);

            Match video = Regex.Match(stderr, @"Stream #\d+:\d+.*?: Video:\s*([A-Za-z0-9_]+)(?:\s*\(([^)]*)\))?.*?\b(\d{2,5})x(\d{2,5})\b.*?([\d.]+)\s+fps");
            if (!video.Success)
            {
                return false;
            }

            Match audio = Regex.Match(stderr, @"Stream #\d+:\d+.*?: Audio:\s*([A-Za-z0-9_]+).*?(\d+)\s*Hz,\s*([A-Za-z0-9.() ]+?),");
            if (!audio.Success)
            {
                return false;
            }

            Match encoder = Regex.Match(stderr, @"encoder\s*:\s*Lavc[\d.]+\s+(\S+)");

            profile = new SegmentProfile(
                video.Groups[1].Value,
                video.Groups[2].Success ? video.Groups[2].Value : string.Empty,
                encoder.Success ? encoder.Groups[1].Value : string.Empty,
                int.Parse(video.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(video.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture),
                video.Groups[5].Value,
                audio.Groups[1].Value,
                audio.Groups[2].Value,
                audio.Groups[3].Value.Trim());
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not read the profile of '{Path.GetFileName(path)}': {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryStreamCopyAsync(IReadOnlyList<string> orderedPaths, string outputPath, StitchLog log)
    {
        var first = default(SegmentProfile);
        for (int i = 0; i < orderedPaths.Count; i++)
        {
            if (!TryProbeProfile(orderedPaths[i], out SegmentProfile profile))
            {
                Plugin.Log.LogDebug("Could not read every segment profile, reencoding film.");
                return false;
            }

            if (i == 0)
            {
                first = profile;
            }
            else if (!profile.Equals(first))
            {
                Plugin.Log.LogDebug($"Segment profiles differ ({first} vs {profile}), reencoding film.");
                return false;
            }
        }

        for (int i = 0; i < orderedPaths.Count; i++)
        {
            if (SegmentHasAudio(orderedPaths[i])) continue;
            Plugin.Log.LogDebug("Segment carries no audio, reencoding film.");
            return false;
        }

        string listPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"concat_{Guid.NewGuid():N}.txt");
        try
        {
            var list = new StringBuilder();
            foreach (string path in orderedPaths)
            {
                list.Append("file '").Append(path.Replace("'", @"'\''")).Append("'\n");
            }
            File.WriteAllText(listPath, list.ToString());

            // -c copy cannot trim aac priming and flush padding, so every join splices a
            // decaying tail against silent frame. measured 11 to 128 ms of near silence
            // per seam, which reads as recording pausing. video still copies
            var audioIn = new StringBuilder();
            var fade = new StringBuilder();
            string seam = Constants.Recording.SeamFade;
            const string normalise = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo";
            for (int i = 0; i < orderedPaths.Count; i++)
            {
                audioIn.Append("-i \"").Append(orderedPaths[i]).Append("\" ");
                fade.Append($"[{i + 1}:a]").Append(normalise);
                if (i > 0)
                {
                    fade.Append($",adelay={Constants.Recording.SeamDelay}:all=1")
                        .Append($",afade=t=in:st={seam}:d={seam}");
                }
                fade.Append($"[s{i}];");
            }
            for (int i = 1; i < orderedPaths.Count; i++)
            {
                string left = i == 1 ? "[s0]" : $"[x{i - 1}]";
                string label = i == orderedPaths.Count - 1 ? "[a]" : $"[x{i}]";
                if (i > 1) fade.Append(';');
                fade.Append(left).Append($"[s{i}]")
                    .Append("acrossfade=d=").Append(seam)
                    .Append(':').Append("c1=tri:c2=tri").Append(label);
            }

            int audioKbps = VideoCameraConfig.GetAudioBitrate();
            string args = $"-y -hide_banner -f concat -safe 0 -i \"{listPath}\" {audioIn}"
                        + $"-filter_complex \"{fade}\" -map 0:v -map \"[a]\" "
                        + $"-c:v copy -c:a aac -b:a {audioKbps}k -movflags +faststart "
                        + $"{Constants.Recording.OutputFormatArg} \"{outputPath}\"";
            int exitCode = await FfmpegProcess.RunAsync(
                FfmpegLocator.ExePath!, args, log.OnLine, CancellationToken.None).ConfigureAwait(false);

            bool fileOk = File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            if (exitCode == 0 && fileOk && !log.InputFailed)
            {
                Plugin.Log.LogDebug($"Segments share one profile ({first}), copied video and crossfaded seams.");
                return true;
            }

            if (log.OutOfSpace)
            {
                return false;
            }
            if (log.InputFailed)
            {
                Plugin.Log.LogWarning("Segment could not be read during copy, re-encoding film instead.");
            }
            else
            {
                Plugin.Log.LogWarning($"Stream copy failed with exit code {exitCode}, reencoding instead.");
            }
            return false;
        }
        catch (Exception ex)
        {
            if (DiskSpace.ReportIfOutOfSpace(ex, "the merged recording"))
            {
                log.MarkOutOfSpace();
                return false;
            }
            Plugin.Log.LogWarning($"Stream copy failed ({ex.Message}), reencoding instead.");
            return false;
        }
        finally
        {
            try { if (File.Exists(listPath)) File.Delete(listPath); } catch { }
        }
    }

    // ffmpeg complains about corrupt packet and still writes whole film so length decides whether footage was actually lost
    private static bool IsShortMerge(string path, double[] durations)
    {
        double expected = 0;
        foreach (double seconds in durations)
        {
            if (seconds <= 0) return false;
            expected += seconds;
        }

        double actual = ProbeDuration(path);
        if (actual <= 0) return true;
        return actual < expected - Constants.Recording.MergeShortfall;
    }

    private static bool SegmentHasAudio(string path)
    {
        try
        {
            int exit = RunSync($"-hide_banner -loglevel error -i \"{path}\" -map 0:a -c copy -f null -", out _);
            return exit == 0;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not probe audio of '{Path.GetFileName(path)}': {ex.Message}");
            return false;
        }
    }

    private static double ProbeDuration(string path)
    {
        try
        {
            RunSync($"-hide_banner -i \"{path}\"", out string stderr);
            int marker = stderr.IndexOf("Duration:", StringComparison.Ordinal);
            if (marker < 0)
            {
                return 0;
            }

            string value = stderr.Substring(marker + 9).TrimStart();
            int comma = value.IndexOf(',');
            if (comma > 0)
            {
                value = value.Substring(0, comma);
            }
            return TimeSpan.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture, out TimeSpan parsed)
                ? parsed.TotalSeconds
                : 0;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not read duration of '{Path.GetFileName(path)}': {ex.Message}");
            return 0;
        }
    }

    private static int RunSync(string arguments, out string stderr)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(FfmpegLocator.ExePath!, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg probe.");
        var sb = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, _) => { };
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        if (!proc.WaitForExit(Constants.Recording.StitchProbeTimeout))
        {
            try { proc.Kill(); } catch { }
            throw new TimeoutException("ffmpeg probe timed out.");
        }

        proc.WaitForExit();
        stderr = sb.ToString();
        return proc.ExitCode;
    }

    private static void TryDeleteStaged(string stagedPath)
    {
        try { if (File.Exists(stagedPath)) File.Delete(stagedPath); } catch { }
    }

    private static async Task<bool> TryPublishAsync(string stagedPath, string finalPath)
    {
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(stagedPath, finalPath);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await Task.Delay(250).ConfigureAwait(false);
        }

        Plugin.Log.LogError($"Could not publish the merged film to '{Path.GetFileName(finalPath)}'.");
        TryDeleteStaged(stagedPath);
        return false;
    }

    public static Task<StitchResult> ConcatAsync(IReadOnlyList<string> orderedPaths, string outputPath, bool allowStreamCopy)
        => Task.Run(() => ConcatCoreAsync(orderedPaths, outputPath, allowStreamCopy));

    private static async Task<StitchResult> ConcatCoreAsync(IReadOnlyList<string> orderedPaths, string outputPath, bool allowStreamCopy)
    {
        if (orderedPaths == null || orderedPaths.Count < 2)
        {
            return StitchResult.Failed;
        }

        if (!FfmpegLocator.IsReady)
        {
            Plugin.Log.LogWarning("FFmpeg is not ready, so the segments were not merged.");
            return StitchResult.Failed;
        }

        (int w, int h) = VideoCameraConfig.GetRecordingSize();
        int fps = VideoCameraConfig.GetRecordingFps();
        int crf = Mathf.Clamp(VideoCameraConfig.RecordingCrf.Value, 0, 51);
        int audioKbps = VideoCameraConfig.GetAudioBitrate();
        string codec = VideoEncoder.PreferredVideoCodec;
        string videoArgs = VideoEncoder.ExtraArgs(codec, crf, w, h, fps);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        string stagedPath = outputPath + ".part";
        TryDeleteStaged(stagedPath);

        var copyLog = new StitchLog();
        if (allowStreamCopy && await TryStreamCopyAsync(orderedPaths, stagedPath, copyLog).ConfigureAwait(false))
        {
            return await TryPublishAsync(stagedPath, outputPath).ConfigureAwait(false)
                ? StitchResult.Ok
                : StitchResult.Failed;
        }

        if (copyLog.OutOfSpace)
        {
            TryDeleteStaged(stagedPath);
            return StitchResult.NoSpace;
        }

        if (!allowStreamCopy)
        {
            Plugin.Log.LogDebug("The segments came from different machines, re-encoding the film.");
        }

        TryDeleteStaged(stagedPath);

        int n = orderedPaths.Count;

        var durations = new double[n];
        for (int i = 0; i < n; i++)
        {
            durations[i] = ProbeDuration(orderedPaths[i]);
        }

        var silenceForSegment = new int[n];
        int nextExtraInput = n;
        var silenceInputs = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (SegmentHasAudio(orderedPaths[i]))
            {
                silenceForSegment[i] = -1;
                continue;
            }

            double seconds = durations[i];
            if (seconds <= 0)
            {
                Plugin.Log.LogError($"Skipped the merge, '{Path.GetFileName(orderedPaths[i])}' has no audio and no readable duration.");
                return StitchResult.Failed;
            }

            silenceForSegment[i] = nextExtraInput++;
            silenceInputs.Add($"-f lavfi -t {seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} -i anullsrc=r=48000:cl=stereo ");
            Plugin.Log.LogDebug($"'{Path.GetFileName(orderedPaths[i])}' carries no audio track, substituting silence.");
        }

        var filter = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            filter.Append($"[{i}:v]scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,")
                  .Append($"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},format=yuv420p[v{i}];");
            int audioInput = silenceForSegment[i] < 0 ? i : silenceForSegment[i];
            filter.Append($"[{audioInput}:a]aresample=async=1,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo");
            if (i > 0)
            {
                filter.Append($",afade=t=in:st=0:d={Constants.Recording.SeamFade}");
            }
            if (i < n - 1 && durations[i] > 0.05)
            {
                double fadeOutStart = System.Math.Max(0.0, durations[i] - 0.035);
                filter.Append($",afade=t=out:st={fadeOutStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}:d={Constants.Recording.SeamFade}");
            }
            filter.Append($"[a{i}];");
        }
        for (int i = 0; i < n; i++)
        {
            filter.Append($"[v{i}][a{i}]");
        }
        filter.Append($"concat=n={n}:v=1:a=1[v][a]");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var inputArgs = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                inputArgs.Append("-i \"").Append(orderedPaths[i]).Append("\" ");
            }
            foreach (string silence in silenceInputs)
            {
                inputArgs.Append(silence);
            }

            string args = $"-y -hide_banner {inputArgs}-c:v {codec} -movflags +faststart " +
                          $"-filter_complex \"{filter}\" -map [v] -map [a] {videoArgs} " +
                          $"-c:a aac -b:a {audioKbps}k {Constants.Recording.OutputSyncArg} " +
                          $"{Constants.Recording.OutputFormatArg} \"{stagedPath}\"";

            var log = new StitchLog(copyLog.Echoed);
            int exitCode = await FfmpegProcess.RunAsync(
                FfmpegLocator.ExePath!, args, log.OnLine, CancellationToken.None).ConfigureAwait(false);

            if (log.OutOfSpace)
            {
                TryDeleteStaged(stagedPath);
                return StitchResult.NoSpace;
            }

            bool fileOk = File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0;
            bool shortFilm = fileOk && log.InputFailed && IsShortMerge(stagedPath, durations);
            if (exitCode != 0 || !fileOk || shortFilm)
            {
                Plugin.Log.LogError(shortFilm
                    ? $"A segment could not be read, so '{Path.GetFileName(outputPath)}' was not merged."
                    : $"The merge produced no usable file for '{Path.GetFileName(outputPath)}'.");
                TryDeleteStaged(stagedPath);
                return shortFilm ? StitchResult.Damaged : StitchResult.Failed;
            }

            if (log.InputFailed)
            {
                Plugin.Log.LogWarning($"'{Path.GetFileName(outputPath)}' is full length but a segment decoded badly, so the picture may glitch.");
            }

            return await TryPublishAsync(stagedPath, outputPath).ConfigureAwait(false)
                ? StitchResult.Ok
                : StitchResult.Failed;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to merge the recording into '{Path.GetFileName(outputPath)}': {ex}");
            TryDeleteStaged(stagedPath);
            return StitchResult.Failed;
        }
    }

    internal enum StitchResult
    {
        Ok,
        Failed,
        NoSpace,
        Damaged,
    }

    // a concat that could not read one of its inputs still exits 0 and writes a short film,
    // so the merge has to be judged on what ffmpeg said as well as on the exit code
    private static readonly string[] InputFailures =
    {
        "impossible to open",
        "error during demuxing",
        "error opening input",
        "invalid data found when processing input",
        "error while decoding",
        "error submitting packet to decoder",
        "decoding error",
    };

    private static readonly string[] Complaints =
    {
        "error", "failed", "invalid", "could not", "cannot", "no such file",
    };

    private sealed class StitchLog
    {
        private int _echoed;

        internal StitchLog(int alreadyEchoed = 0) => _echoed = alreadyEchoed;

        internal int Echoed => _echoed;
        private volatile bool _inputFailed;
        private volatile bool _outOfSpace;

        internal bool InputFailed => _inputFailed;
        internal bool OutOfSpace => _outOfSpace;

        internal void MarkOutOfSpace() => _outOfSpace = true;

        internal void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (line.IndexOf("No space left on device", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _outOfSpace = true;
                DiskSpace.ReportEncoderOutOfSpace();
                return;
            }

            bool broken = Contains(line, InputFailures);
            if (broken) _inputFailed = true;
            else if (!Contains(line, Complaints)) return;

            if (_echoed >= Constants.Recording.StderrEchoLimit) return;
            _echoed++;
            Plugin.Log.LogWarning($"ffmpeg (merge): {line}");
        }

        private static bool Contains(string line, string[] needles)
        {
            foreach (string needle in needles)
            {
                if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
