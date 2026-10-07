using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Operator.Recording;

internal sealed class AudioPipe
{
    private static readonly float MaxEndFill = Constants.Recording.MaxEndFill;

    private readonly ConcurrentQueue<float[]> _queue = new();
    private readonly BufferPool<float> _pool = new(maxRetained: 16);
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly Func<double>? _activeTime;
    private volatile bool _completed;
    private volatile bool _faulted;
    private byte[] _scratch = new byte[8192];
    private byte[]? _silence;
    private long _floatsWritten;
    private long _floatsPadded;
    private long _floatsPaid;
    private long _floatsFilled;
    private double _end;
    private long _debt;
    private int _padCount;
    private readonly long _minSlack;
    private readonly long _maxSlack;
    private readonly long _jitterLimit;
    private long _arrival;
    private long _gapBucket;
    private long _gap;
    private long _gapLast;
    private readonly PipeSignal _wake = new();
    private readonly long _idleWake;
    private long _wokeAt;

    public AudioPipe(int sampleRate, int channels, Func<double>? activeTime = null)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _activeTime = activeTime;
        _minSlack = (long)(Constants.Recording.MinPaybackSlack * sampleRate) * channels;
        _maxSlack = (long)(Constants.Recording.MaxPaybackSlack * sampleRate) * channels;
        _jitterLimit = (long)(Constants.Recording.JitterLimit * Stopwatch.Frequency);
        _idleWake = (long)(Constants.Recording.IdleWakeInterval * Stopwatch.Frequency);
    }

    public void EnqueueCopy(float[] source, int offset, int count)
    {
        if (count <= 0 || _faulted)
        {
            return;
        }
        UpdateGap();
        float[] buffer = _pool.Rent(count);
        Buffer.BlockCopy(source, offset * sizeof(float), buffer, 0, count * sizeof(float));
        _queue.Enqueue(buffer);
        _wake.Set();
    }

    private void UpdateGap()
    {
        long now = Stopwatch.GetTimestamp();
        long bucket = now / Stopwatch.Frequency;
        if (bucket != _gapBucket)
        {
            Volatile.Write(ref _gapLast, bucket == _gapBucket + 1 ? _gap : 0);
            Volatile.Write(ref _gap, 0);
            _gapBucket = bucket;
        }

        long gap = _arrival > 0 ? now - _arrival : 0;
        _arrival = now;
        if (gap < _jitterLimit && gap > _gap)
        {
            Volatile.Write(ref _gap, gap);
        }
    }

    public double TimeWritten
    {
        get
        {
            long perSecond = (long)_sampleRate * _channels;
            return perSecond > 0 ? (double)_floatsWritten / perSecond : 0.0;
        }
    }

    public void Complete(double activeTime = 0)
    {
        _end = activeTime;
        _completed = true;
        _wake.Set();
    }

    public void ClearDebt() => Interlocked.Exchange(ref _debt, 0L);

    public void Nudge()
    {
        if (Stopwatch.GetTimestamp() - Volatile.Read(ref _wokeAt) >= _idleWake)
        {
            _wake.Set();
        }
    }

    public string GetStreamArguments() =>
        $"{Constants.Recording.InputProbeArgs} -f f32le -ar {_sampleRate} -ac {_channels}";

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration stop = cancellationToken.Register(_wake.Set);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _wake.Reset();
                Volatile.Write(ref _wokeAt, Stopwatch.GetTimestamp());
                if (_queue.TryDequeue(out float[]? samples))
                {
                    if (samples != null && samples.Length > 0)
                    {
                        int skip = GetPayback(samples.Length);
                        int count = samples.Length - skip;
                        if (count > 0)
                        {
                            int byteCount = count * sizeof(float);
                            if (_scratch.Length < byteCount)
                            {
                                _scratch = new byte[byteCount];
                            }
                            Buffer.BlockCopy(samples, skip * sizeof(float), _scratch, 0, byteCount);
                            await outputStream.WriteAsync(_scratch, 0, byteCount, cancellationToken).ConfigureAwait(false);
                            _floatsWritten += count;
                        }
                        _pool.Return(samples);
                    }
                    continue;
                }

                if (_completed)
                {
                    await FillAsync(outputStream, cancellationToken).ConfigureAwait(false);
                    await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    double rate = Math.Max(1L, (long)_sampleRate * _channels);
                    Plugin.Log.LogDebug($"Audio pipe at {_sampleRate} Hz wrote {TimeWritten:F2}s, padded {_floatsPadded / rate:F2}s in {_padCount} gaps and paid back {_floatsPaid / rate:F2}s.");
                    if (_floatsFilled > 0)
                    {
                        Plugin.Log.LogDebug($"Filled the last {_floatsFilled / rate:F2}s of the {_sampleRate} Hz audio with silence.");
                    }
                    return;
                }

                if (await TryWriteSilenceAsync(outputStream, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                await _wake;
            }
        }
        catch (IOException)
        {
            _faulted = true;
            while (_queue.TryDequeue(out float[]? dropped))
            {
                if (dropped != null) _pool.Return(dropped);
            }
        }
    }

    private int GetPayback(int length)
    {
        if (_debt <= 0 || _activeTime == null)
        {
            return 0;
        }

        long gap = Math.Max(Volatile.Read(ref _gap), Volatile.Read(ref _gapLast));
        long jitter = 2 * (long)((double)gap / Stopwatch.Frequency * _sampleRate) * _channels;
        long expected = (long)(_activeTime() * _sampleRate * _channels);
        long ahead = _floatsWritten - expected - Math.Min(Math.Max(_minSlack, jitter), _maxSlack);
        if (ahead <= 0)
        {
            return 0;
        }

        long skip = Math.Min(Math.Min(ahead, _debt), length);
        skip -= skip % _channels;
        if (skip <= 0)
        {
            return 0;
        }

        _debt -= skip;
        _floatsPaid += skip;
        return (int)skip;
    }

    private async Task<bool> TryWriteSilenceAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        if (_activeTime == null)
        {
            return false;
        }

        double elapsed = _activeTime();
        long expected = (long)(elapsed * _sampleRate * _channels);
        long deficit = expected - _floatsWritten;

        long minChunk = Math.Max(1, _sampleRate * _channels / 100);
        if (deficit < minChunk)
        {
            return false;
        }

        byte[] silence = Silence;
        int maxFloats = silence.Length / sizeof(float);
        int floats = (int)Math.Min(deficit, maxFloats);
        floats -= floats % _channels;
        if (floats <= 0)
        {
            return false;
        }

        int bytes = floats * sizeof(float);

        await outputStream.WriteAsync(silence, 0, bytes, cancellationToken).ConfigureAwait(false);
        _floatsWritten += floats;
        _floatsPadded += floats;
        _debt += floats;
        _padCount++;
        return true;
    }

    private byte[] Silence => _silence ??= new byte[Math.Max(4096, _sampleRate * _channels * sizeof(float) / 25)];

    private async Task FillAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        long perSecond = (long)_sampleRate * _channels;
        long fill = Math.Min((long)(_end * perSecond) - _floatsWritten, (long)(MaxEndFill * perSecond));
        fill -= fill % _channels;
        byte[] silence = Silence;
        int chunk = silence.Length / sizeof(float);
        chunk -= chunk % _channels;
        while (fill > 0 && chunk > 0)
        {
            int floats = (int)Math.Min(fill, chunk);
            await outputStream.WriteAsync(silence, 0, floats * sizeof(float), cancellationToken).ConfigureAwait(false);
            _floatsWritten += floats;
            _floatsFilled += floats;
            fill -= floats;
        }
    }
}
