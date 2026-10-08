using System;

namespace Operator.Recording.Voice;

internal sealed class MicResampler
{
    private double _pos;
    private float _prev;

    public int Process(float[] src, int offset, int count, int srcRate, int dstRate, float[] dst)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (srcRate == dstRate)
        {
            Array.Copy(src, offset, dst, 0, count);
            _prev = src[offset + count - 1];
            _pos = 0;
            return count;
        }

        double step = (double)srcRate / dstRate;
        int outN = 0;

        while (true)
        {
            int i0 = (int)Math.Floor(_pos);
            int i1 = i0 + 1;
            if (i1 > count)
            {
                break;
            }

            float s0 = i0 == 0 ? _prev : src[offset + i0 - 1];
            float s1 = i1 == 0 ? _prev : src[offset + i1 - 1];
            float frac = (float)(_pos - i0);
            if (outN >= dst.Length)
            {
                break;
            }
            dst[outN++] = s0 + ((s1 - s0) * frac);
            _pos += step;
        }

        _pos -= count;
        _prev = src[offset + count - 1];
        return outN;
    }
}
