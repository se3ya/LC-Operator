using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Operator.Recording;

internal sealed class PipeSignal : ICriticalNotifyCompletion
{
    private static readonly Action Fired = () => { };
    private static readonly WaitCallback RunLater = state => ((Action)state!).Invoke();

    private Action? _next;

    public void Set()
    {
        Action? next = Interlocked.Exchange(ref _next, Fired);
        if (next != null && !ReferenceEquals(next, Fired))
        {
            ThreadPool.UnsafeQueueUserWorkItem(RunLater, next);
        }
    }

    public void Reset() => Interlocked.CompareExchange(ref _next, null, Fired);

    public PipeSignal GetAwaiter() => this;

    public bool IsCompleted => ReferenceEquals(Volatile.Read(ref _next), Fired);

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _next, continuation, null), Fired))
        {
            ThreadPool.UnsafeQueueUserWorkItem(RunLater, continuation);
        }
    }
}
