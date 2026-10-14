using UnityEngine;

namespace Operator.Recording;

internal sealed class UnderwaterFilter
{
    private LowPass _one;
    private LowPass _two;
    private float _cutoff = 20000f;
    private float _at;

    public void Reset(bool submerged) => _cutoff = submerged ? Constants.Audio.UnderwaterCutoff : 20000f;

    public void Apply(float[] buffer, int frames, int channels, int rate, bool submerged)
    {
        float target = submerged ? Constants.Audio.UnderwaterCutoff : 20000f;
        float step = (float)frames / rate * Constants.Audio.UnderwaterGlideRate;
        _cutoff = Mathf.Lerp(_cutoff, target, Mathf.Clamp01(step));

        if (_cutoff >= 19000f)
        {
            if (_at != 0f)
            {
                _one.Reset();
                _two.Reset();
                _at = 0f;
            }

            return;
        }

        if (_at != _cutoff)
        {
            _one.Set(_cutoff, rate, 0.54119610f);
            _two.Set(_cutoff, rate, 1.30656296f);
            _at = _cutoff;
        }

        if (channels >= 2)
        {
            for (int f = 0, i = 0; f < frames; f++, i += channels)
            {
                buffer[i] = _two.Left(_one.Left(buffer[i]));
                buffer[i + 1] = _two.Right(_one.Right(buffer[i + 1]));
            }

            return;
        }

        for (int f = 0; f < frames; f++)
        {
            buffer[f] = _two.Left(_one.Left(buffer[f]));
        }
    }
}
