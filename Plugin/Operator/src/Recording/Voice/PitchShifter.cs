using System;

namespace Operator.Recording.Voice;

internal sealed class PitchShifter
{
    private const int RingSize = 2048;
    private const float HalfN = RingSize / 2f;

    private float[][] _rings = Array.Empty<float[]>();
    private int _channels;
    private int _write;
    private float _delay = HalfN;

    public void Process(float[] buffer, int channels, float pitchRatio)
    {
        if (channels <= 0 || buffer.Length < channels)
        {
            return;
        }

        if (Math.Abs(pitchRatio - 1f) < 1e-4f)
        {
            WriteThrough(buffer, channels);
            return;
        }

        InitChannels(channels);
        int frames = buffer.Length / channels;

        for (int i = 0; i < frames; i++)
        {
            int baseIdx = i * channels;
            for (int c = 0; c < channels; c++)
            {
                _rings[c][_write] = buffer[baseIdx + c];
            }

            float g1 = MathF.Sin(MathF.PI * (_delay / RingSize));
            float d2 = _delay + HalfN;
            if (d2 >= RingSize) d2 -= RingSize;
            float g2 = MathF.Sin(MathF.PI * (d2 / RingSize));

            float rp1 = _write - _delay;
            float rp2 = rp1 - HalfN;

            for (int c = 0; c < channels; c++)
            {
                buffer[baseIdx + c] = (Tap(_rings[c], rp1) * g1) + (Tap(_rings[c], rp2) * g2);
            }

            _delay += 1f - pitchRatio;
            if (_delay >= RingSize) _delay -= RingSize;
            if (_delay < 0f) _delay += RingSize;

            _write++;
            if (_write >= RingSize) _write = 0;
        }
    }

    private void WriteThrough(float[] buffer, int channels)
    {
        InitChannels(channels);
        int frames = buffer.Length / channels;
        for (int i = 0; i < frames; i++)
        {
            int baseIdx = i * channels;
            for (int c = 0; c < channels; c++)
            {
                _rings[c][_write] = buffer[baseIdx + c];
            }
            _write++;
            if (_write >= RingSize) _write = 0;
        }
        _delay = HalfN;
    }

    private static float Tap(float[] ring, float readPos)
    {
        while (readPos < 0f) readPos += RingSize;
        while (readPos >= RingSize) readPos -= RingSize;
        int i0 = (int)readPos;
        int i1 = i0 + 1;
        if (i1 >= RingSize) i1 = 0;
        float f = readPos - i0;
        return ring[i0] + ((ring[i1] - ring[i0]) * f);
    }

    private void InitChannels(int channels)
    {
        if (_channels == channels && _rings.Length == channels)
        {
            return;
        }
        _rings = new float[channels][];
        for (int c = 0; c < channels; c++)
        {
            _rings[c] = new float[RingSize];
        }
        _channels = channels;
        _write = 0;
        _delay = HalfN;
    }
}
