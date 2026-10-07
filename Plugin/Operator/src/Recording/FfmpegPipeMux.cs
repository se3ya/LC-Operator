using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Operator.Recording;

internal sealed class FfmpegPipeInput
{
    public string StreamArguments { get; }
    public Func<Stream, CancellationToken, Task> WriteAsync { get; }

    public FfmpegPipeInput(string streamArguments, Func<Stream, CancellationToken, Task> writeAsync)
    {
        StreamArguments = streamArguments;
        WriteAsync = writeAsync;
    }
}

internal static class FfmpegProcess
{
    public static Task<int> RunAsync(string exePath, string arguments, Action<string>? onStderr, CancellationToken token)
        => RunCoreAsync(exePath, arguments, onStderr, null, token);

    public static async Task<int> RunWithPipesAsync(
        string exePath,
        IReadOnlyList<FfmpegPipeInput> inputs,
        string outputArgs,
        string outputPath,
        Action<string>? onStderr,
        Action? onPipesConnected,
        CancellationToken token,
        Func<long>? pipesClosedAt = null)
    {
        var pipes = new NamedPipeServerStream[inputs.Count];
        var argSb = new StringBuilder("-y -hide_banner ");
        var feeders = new Task[inputs.Count];
        try
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                string pipeName = $"operator_{Guid.NewGuid():N}";
                pipes[i] = new NamedPipeServerStream(
                    pipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    0, 1 << 20);
                argSb.Append(inputs[i].StreamArguments)
                     .Append(" -i \\\\.\\pipe\\").Append(pipeName).Append(' ');
            }
            argSb.Append(outputArgs).Append(" \"").Append(outputPath).Append('"');

            Task OnStarted(Process _)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    NamedPipeServerStream pipe = pipes[i];
                    FfmpegPipeInput input = inputs[i];
                    bool isPrimary = i == 0;
                    feeders[i] = Task.Run(async () =>
                    {
                        try
                        {
                            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                            if (isPrimary)
                            {
                                onPipesConnected?.Invoke();
                            }
                            await input.WriteAsync(pipe, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { }
                        catch (IOException) { }
                        catch (Exception ex) { Plugin.Log.LogWarning($"Failed to feed ffmpeg one of its inputs: {ex.Message}"); }
                        finally
                        {
                            try { pipe.Dispose(); } catch { }
                        }
                    });
                }
                return Task.CompletedTask;
            }

            int exit = await RunCoreAsync(exePath, argSb.ToString(), onStderr, OnStarted, token, pipesClosedAt).ConfigureAwait(false);
            try { await Task.WhenAll(feeders).ConfigureAwait(false); } catch { }
            return exit;
        }
        finally
        {
            foreach (NamedPipeServerStream? p in pipes)
            {
                try { p?.Dispose(); } catch { }
            }
        }
    }

    private static async Task<int> RunCoreAsync(string exePath, string arguments, Action<string>? onStderr, Func<Process, Task>? onStarted, CancellationToken token, Func<long>? pipesClosedAt = null)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, _) => { try { exitTcs.TrySetResult(proc.ExitCode); } catch { exitTcs.TrySetResult(-1); } };

        if (onStderr != null)
        {
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onStderr(e.Data); };
        }

        var spawnTimer = Stopwatch.StartNew();
        if (!proc.Start())
        {
            throw new InvalidOperationException("Failed to start ffmpeg.");
        }
        spawnTimer.Stop();
        if (spawnTimer.ElapsedMilliseconds >= Constants.Recording.SlowSpawnThreshold)
        {
            Plugin.Log.LogDebug($"FFmpeg took {spawnTimer.ElapsedMilliseconds} ms to launch.");
        }

        try { FfmpegJobObject.Add(proc.Handle); } catch { }

        if (onStderr != null)
        {
            proc.BeginErrorReadLine();
        }
        if (proc.HasExited)
        {
            try { exitTcs.TrySetResult(proc.ExitCode); } catch { }
        }

        if (onStarted != null)
        {
            await onStarted(proc).ConfigureAwait(false);
        }

        using (token.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch { } }))
        {
            int exitCode = await exitTcs.Task.ConfigureAwait(false);
            long exitedAt = Stopwatch.GetTimestamp();
            // exit event can beat stderr reader and caller judges merge on what it read
            try { proc.WaitForExit(); } catch { }
            LogExitTiming(pipesClosedAt, exitedAt);
            return exitCode;
        }
    }

    private static void LogExitTiming(Func<long>? pipesClosedAt, long exitedAt)
    {
        if (pipesClosedAt == null) return;
        long closedAt = pipesClosedAt();
        if (closedAt <= 0L || exitedAt < closedAt) return;

        double perTick = 1000.0 / Stopwatch.Frequency;
        long readerClosedAt = Stopwatch.GetTimestamp();
        Plugin.Log.LogDebug($"The encoder error stream closed {(readerClosedAt - exitedAt) * perTick:0} ms after FFmpeg exited.");
    }
}
