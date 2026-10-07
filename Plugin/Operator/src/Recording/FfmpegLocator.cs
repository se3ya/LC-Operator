using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Operator.Recording;

internal static class FfmpegLocator
{
    private static readonly string DownloadUrl = Constants.Recording.FfmpegDownloadUrl;

    private static readonly object Gate = new();
    private static Task<bool>? _initTask;
    private static string? _exePath;

    public static bool IsReady => _exePath != null && File.Exists(_exePath);
    public static bool GaveUp { get; private set; }
    public static string? ExePath => _exePath;
    public static string? Version { get; private set; }

    public static Task<bool> InitAsync()
    {
        lock (Gate)
        {
            _initTask ??= Task.Run(InitCoreAsync);
            return _initTask;
        }
    }

    private static async Task<bool> InitCoreAsync()
    {
        try
        {
            string? configured = VideoCameraConfig.FfmpegExePath?.Value;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!File.Exists(configured))
                {
                    Plugin.Log.LogWarning($"Configured FFmpeg path '{configured}' does not exist, falling back.");
                }
                else if (!IsUsableFfmpeg(configured))
                {
                    Plugin.Log.LogWarning($"Configured FFmpeg at '{configured}' did not run, falling back.");
                }
                else
                {
                    _exePath = configured;
                    Plugin.Log.LogInfo($"Using user configured FFmpeg at '{configured}'.");
                    return true;
                }
            }

            string cacheDir = GetCacheDirectory();
            string cachedExe = Path.Combine(cacheDir, "ffmpeg.exe");
            if (File.Exists(cachedExe))
            {
                if (IsUsableFfmpeg(cachedExe, true))
                {
                    _exePath = cachedExe;
                    Plugin.Log.LogInfo($"Using cached FFmpeg at '{cachedExe}'.");
                    return true;
                }

                Plugin.Log.LogWarning($"Cached FFmpeg at '{cachedExe}' is not runnable, deleting it and downloading again.");
                try { File.Delete(cachedExe); } catch (Exception ex) { Plugin.Log.LogWarning($"Could not delete unusable cached FFmpeg: {ex.Message}"); }
            }

            Directory.CreateDirectory(cacheDir);
            for (int attempt = 1; attempt <= Constants.Recording.FfmpegDownloadAttempts; attempt++)
            {
                try
                {
                    if (attempt == 1)
                    {
                        Plugin.Log.LogInfo($"FFmpeg not found, downloading from {DownloadUrl} (~90 MB). Recording will be unavailable until this finishes.");
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Retrying FFmpeg download, attempt {attempt} of {Constants.Recording.FfmpegDownloadAttempts}.");
                    }

                    if (await TryInstallAsync(cacheDir, cachedExe).ConfigureAwait(false))
                    {
                        _exePath = cachedExe;
                        Plugin.Log.LogInfo($"FFmpeg ready at '{cachedExe}'.");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (DiskSpace.ReportIfOutOfSpace(ex, "FFmpeg download"))
                    {
                        return false;
                    }
                    Plugin.Log.LogWarning($"FFmpeg download attempt {attempt} failed, {ex.GetType().Name}: {Innermost(ex)}");
                }

                if (attempt < Constants.Recording.FfmpegDownloadAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 3)).ConfigureAwait(false);
                }
            }

            string? onPath = FindFfmpegOnPath();
            if (onPath != null)
            {
                _exePath = onPath;
                Plugin.Log.LogWarning($"Could not download FFmpeg, using the one on PATH at '{onPath}'.");
                return true;
            }

            Plugin.Log.LogError($"Could not get FFmpeg, recording is off until the next launch. Put ffmpeg.exe in '{cacheDir}' or set FfmpegExePath.");
            GaveUp = true;
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to set up FFmpeg: {ex}");
            GaveUp = true;
            return false;
        }
    }

    private static async Task<bool> TryInstallAsync(string cacheDir, string cachedExe)
    {
        string zipPath = Path.Combine(cacheDir, "ffmpeg-download.zip");
        string stagedExe = cachedExe + ".tmp";
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (File.Exists(stagedExe)) File.Delete(stagedExe);

            await DownloadFileAsync(DownloadUrl, zipPath).ConfigureAwait(false);
            ExtractFfmpegExe(zipPath, stagedExe);

            if (!File.Exists(stagedExe) || !IsUsableFfmpeg(stagedExe, true))
            {
                Plugin.Log.LogWarning("Downloaded FFmpeg did not run, discarding it.");
                return false;
            }

            if (File.Exists(cachedExe)) File.Delete(cachedExe);
            File.Move(stagedExe, cachedExe);
            return true;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (File.Exists(stagedExe)) File.Delete(stagedExe); } catch { }
        }
    }

    private static bool IsUsableFfmpeg(string exePath, bool acceptSlow = false)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exePath, "-hide_banner -version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return false;
            }
            string? first = null;
            proc.OutputDataReceived += (_, e) => { if (first == null && !string.IsNullOrWhiteSpace(e.Data)) first = e.Data; };
            proc.ErrorDataReceived += (_, _) => { };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            int timeout = Wine.IsRunning ? Constants.Recording.FfmpegValidateTimeoutWine : Constants.Recording.FfmpegValidateTimeout;
            if (!proc.WaitForExit(timeout))
            {
                try { proc.Kill(); } catch { }
                if (!acceptSlow) return false;
                Plugin.Log.LogWarning($"FFmpeg at '{exePath}' did not answer in {timeout / 1000} s, using it anyway.");
                return true;
            }

            proc.WaitForExit();
            if (proc.ExitCode != 0) return false;
            Version = ShortVersion(first);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FFmpeg validation of '{exePath}' failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string Innermost(Exception ex)
    {
        Exception inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    private static string? ShortVersion(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string text = line!.Trim();
        int cut = text.IndexOf(" Copyright", StringComparison.Ordinal);
        if (cut > 0) text = text.Substring(0, cut);
        return text.StartsWith("ffmpeg version ", StringComparison.Ordinal) ? text.Substring("ffmpeg version ".Length) : text;
    }

    private static string? FindFfmpegOnPath()
    {
        try
        {
            string? pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar))
            {
                return null;
            }

            foreach (string entry in pathVar.Split(Path.PathSeparator))
            {
                string trimmed = entry.Trim().Trim('"');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                string candidate;
                try { candidate = Path.Combine(trimmed, "ffmpeg.exe"); }
                catch { continue; }

                if (File.Exists(candidate) && IsUsableFfmpeg(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"PATH scan for ffmpeg failed: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    private static string GetCacheDirectory()
    {
        string pluginDir = Path.GetDirectoryName(Plugin.Instance.Info.Location)!;
        string bepinexRoot = Directory.GetParent(Directory.GetParent(pluginDir)!.FullName)!.FullName;
        return Path.Combine(bepinexRoot, "cache", "ffmpeg");
    }

    private static async Task DownloadFileAsync(string url, string destination)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1L;
        using Stream src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using FileStream dst = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long received = 0;
        long nextLog = 10L * 1024 * 1024;
        int read;
        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer, 0, read, CancellationToken.None).ConfigureAwait(false);
            received += read;
            if (received >= nextLog)
            {
                if (total > 0)
                {
                    Plugin.Log.LogInfo($"FFmpeg download: {received / (1024 * 1024)} / {total / (1024 * 1024)} MB");
                }
                else
                {
                    Plugin.Log.LogInfo($"FFmpeg download: {received / (1024 * 1024)} MB");
                }
                nextLog += 10L * 1024 * 1024;
            }
        }
    }

    private static void ExtractFfmpegExe(string zipPath, string destinationExe)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? entry = archive.Entries
            .FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            throw new InvalidOperationException($"ffmpeg.exe not found inside {zipPath}");
        }
        entry.ExtractToFile(destinationExe, overwrite: true);
    }
}
