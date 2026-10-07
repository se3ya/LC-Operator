using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Operator.Recording;

internal sealed class VideoPipe
{
    private static readonly BufferPool<byte> SharedPool = new(maxRetained: Constants.Recording.RetainedFrameBuffers);
    private static readonly float MaxEndFill = Constants.Recording.MaxEndFill;

    public static void PrewarmPool(int width, int height, int count) =>
        SharedPool.Prewarm(count, width * height * 4);

    public static void KeepPoolFor(int queuedFrames) =>
        SharedPool.RaiseMaxRetained(queuedFrames);

    private readonly ConcurrentQueue<(byte[] Buffer, int Repeats)> _queue = new();
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private int _queuedSlots;
    private long _framesWritten;
    private long _target;
    private byte[]? _last;
    private volatile bool _completed;
    private readonly PipeSignal _wake = new();

    public VideoPipe(int width, int height, int fps)
    {
        _width = width;
        _height = height;
        _fps = fps;
    }

    public int Count => Volatile.Read(ref _queuedSlots);

    public double TimeWritten => _fps > 0 ? (double)Interlocked.Read(ref _framesWritten) / _fps : 0.0;

    public byte[] RentFrameBuffer(int length)
    {
        byte[] buffer = SharedPool.Rent(length, out bool allocated);
        RecordingStats.OnBufferRented(allocated);
        return buffer;
    }

    public void Enqueue(byte[] frame, int repeats)
    {
        _queue.Enqueue((frame, repeats));
        Interlocked.Add(ref _queuedSlots, repeats);
        _wake.Set();
    }

    public void Complete(double activeTime = 0)
    {
        Interlocked.Exchange(ref _target, (long)(activeTime * _fps));
        _completed = true;
        _wake.Set();
    }

    public string GetStreamArguments() =>
        $"{Constants.Recording.InputProbeArgs} -f rawvideo -pix_fmt rgba -s {_width}x{_height} -framerate {_fps}";

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration stop = cancellationToken.Register(_wake.Set);
        while (!cancellationToken.IsCancellationRequested)
        {
            _wake.Reset();
            if (_queue.TryDequeue(out (byte[] Buffer, int Repeats) frame))
            {
                for (int i = 0; i < frame.Repeats; i++)
                {
                    await outputStream.WriteAsync(frame.Buffer, 0, frame.Buffer.Length, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _framesWritten);
                    Interlocked.Decrement(ref _queuedSlots);
                }
                if (_last != null) SharedPool.Return(_last);
                _last = frame.Buffer;
                continue;
            }

            if (_completed)
            {
                await FillAsync(outputStream, cancellationToken).ConfigureAwait(false);
                await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (_last != null) SharedPool.Return(_last);
                _last = null;
                return;
            }

            await _wake;
        }
    }

    private async Task FillAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        byte[]? last = _last;
        long missing = Interlocked.Read(ref _target) - Interlocked.Read(ref _framesWritten);
        if (last == null || missing <= 0)
        {
            return;
        }

        long fill = Math.Min(missing, (long)(MaxEndFill * _fps));
        for (long i = 0; i < fill; i++)
        {
            await outputStream.WriteAsync(last, 0, last.Length, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _framesWritten);
        }

        if (fill < missing)
        {
            Plugin.Log.LogWarning($"The take was missing {(double)missing / _fps:F1} s of frames at the end, only {MaxEndFill:F0} s were filled with the last picture.");
            return;
        }
        Plugin.Log.LogDebug($"Filled the last {fill} missing frames of the take with the last picture.");
    }
}
