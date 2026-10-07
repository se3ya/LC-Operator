using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Operator.Recording.Voice;

internal sealed class MicInjector : MonoBehaviour
{
    private readonly ConcurrentQueue<float> _queue = new();
    private int _queued;

    public void Add(float[] samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _queue.Enqueue(samples[i]);
        }

        Interlocked.Add(ref _queued, count);
        if (Volatile.Read(ref _queued) <= Constants.Audio.MicQueueLimit) return;

        int low = Constants.Audio.MicQueueLimit / 4;
        while (Volatile.Read(ref _queued) > low && _queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queued);
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (channels <= 0)
        {
            return;
        }

        int frames = data.Length / channels;
        int taken = 0;
        for (int i = 0; i < frames; i++)
        {
            float s = 0f;
            if (_queue.TryDequeue(out float v))
            {
                s = v;
                taken++;
            }

            int baseIdx = i * channels;
            for (int c = 0; c < channels; c++)
            {
                data[baseIdx + c] = s;
            }
        }

        if (taken > 0)
        {
            Interlocked.Add(ref _queued, -taken);
        }
    }
}
