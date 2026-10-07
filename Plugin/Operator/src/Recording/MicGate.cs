using System;
using System.Threading;

namespace Operator.Recording;

internal sealed class MicGate
{
    private volatile bool _known;
    private volatile bool _muted;
    private volatile bool _speaking;
    private volatile float _volume = 1f;
    private int _flushAsked;

    private float[][] _slots = Array.Empty<float[]>();
    private int[] _lengths = Array.Empty<int>();
    private float[] _scratch = Array.Empty<float>();
    private int _flushSeen;
    private int _slotRate;
    private int _next;
    private int _stored;
    private float _level;
    private float _gain = -1f;
    private bool _open;

    internal void SetState(bool known, bool muted, bool speaking, float volume)
    {
        _known = known;
        _muted = muted;
        _speaking = speaking;
        _volume = volume;
    }

    internal void Flush() => Interlocked.Increment(ref _flushAsked);

    internal void Process(ArraySegment<float> frame, int rate, Action<ArraySegment<float>> send, Action? onOpen)
    {
        float[]? samples = frame.Array;
        int count = frame.Count;
        if (samples == null || count <= 0 || rate <= 0) return;

        int asked = Volatile.Read(ref _flushAsked);
        if (asked != _flushSeen)
        {
            _flushSeen = asked;
            _open = false;
            _level = 0f;
            _stored = 0;
        }

        bool muted = _muted;
        bool want = !_known || (!muted && _speaking);

        if (!_open)
        {
            if (!want)
            {
                if (muted) _stored = 0;
                else Store(samples, frame.Offset, count, rate);
                return;
            }

            _open = true;
            onOpen?.Invoke();
            Replay(rate, send);
        }

        Emit(samples, frame.Offset, count, rate, want, send);
        if (!want && _level <= 0f) _open = false;
    }

    private void Store(float[] source, int offset, int count, int rate)
    {
        if (rate != _slotRate || _slots.Length == 0)
        {
            int frames = Math.Max(1, (int)Math.Round(Constants.Audio.MicPreRollTime * (double)rate / count));
            _slots = new float[frames][];
            _lengths = new int[frames];
            for (int i = 0; i < frames; i++) _slots[i] = new float[count];
            _slotRate = rate;
            _next = 0;
            _stored = 0;
        }

        float[] slot = _slots[_next];
        if (slot.Length < count)
        {
            slot = new float[count];
            _slots[_next] = slot;
        }

        Buffer.BlockCopy(source, offset * sizeof(float), slot, 0, count * sizeof(float));
        _lengths[_next] = count;
        _next = (_next + 1) % _slots.Length;
        if (_stored < _slots.Length) _stored++;
    }

    private void Replay(int rate, Action<ArraySegment<float>> send)
    {
        if (rate != _slotRate) _stored = 0;

        int size = _slots.Length;
        for (int i = 0; i < _stored; i++)
        {
            int slot = (_next - _stored + i + size) % size;
            Emit(_slots[slot], 0, _lengths[slot], rate, true, send);
        }

        _stored = 0;
    }

    private void Emit(float[] source, int offset, int count, int rate, bool open, Action<ArraySegment<float>> send)
    {
        if (_scratch.Length < count) _scratch = new float[count];

        float[] output = _scratch;
        float goal = open ? 1f : 0f;
        float step = 1f / Math.Max(1f, Constants.Audio.MicFadeTime * rate);
        float level = _level;
        float volume = _volume;
        float from = _gain < 0f ? volume : _gain;
        float slope = (volume - from) / count;

        for (int i = 0; i < count; i++)
        {
            if (level < goal) level = Math.Min(goal, level + step);
            else if (level > goal) level = Math.Max(goal, level - step);
            output[i] = source[offset + i] * level * (from + slope * (i + 1));
        }

        if (goal <= 0f && level < step) level = 0f;
        _level = level;
        _gain = volume;
        send(new ArraySegment<float>(output, 0, count));
    }
}
