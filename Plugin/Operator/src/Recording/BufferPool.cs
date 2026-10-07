using System.Collections.Concurrent;
using System.Threading;

namespace Operator.Recording;

internal sealed class BufferPool<T>
{
    private readonly ConcurrentStack<T[]> _buffers = new();
    private int _maxRetained;
    private int _retained;

    public BufferPool(int maxRetained)
    {
        _maxRetained = maxRetained;
    }

    public void RaiseMaxRetained(int maxRetained)
    {
        int current = Volatile.Read(ref _maxRetained);
        while (maxRetained > current)
        {
            int seen = Interlocked.CompareExchange(ref _maxRetained, maxRetained, current);
            if (seen == current) return;
            current = seen;
        }
    }

    public void Prewarm(int count, int length)
    {
        for (int i = 0; i < count; i++)
        {
            if (Interlocked.Increment(ref _retained) <= _maxRetained)
            {
                _buffers.Push(new T[length]);
            }
            else
            {
                Interlocked.Decrement(ref _retained);
                return;
            }
        }
    }

    public T[] Rent(int length) => Rent(length, out _);

    public T[] Rent(int length, out bool allocated)
    {
        while (_buffers.TryPop(out T[]? buffer))
        {
            Interlocked.Decrement(ref _retained);
            if (buffer.Length == length)
            {
                allocated = false;
                return buffer;
            }
        }
        allocated = true;
        return new T[length];
    }

    public void Return(T[] buffer)
    {
        if (Interlocked.Increment(ref _retained) <= _maxRetained)
        {
            _buffers.Push(buffer);
        }
        else
        {
            Interlocked.Decrement(ref _retained);
        }
    }
}
