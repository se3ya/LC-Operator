using UnityEngine;

namespace Operator.Recording;

internal sealed class CamcorderMic
{
    private sealed class Biquad
    {
        private float _b0;
        private float _b1;
        private float _b2;
        private float _a1;
        private float _a2;
        private float _leftOne;
        private float _leftTwo;
        private float _rightOne;
        private float _rightTwo;

        internal void Set(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        internal float Left(float x)
        {
            float y = (_b0 * x) + _leftOne;
            _leftOne = (_b1 * x) - (_a1 * y) + _leftTwo;
            _leftTwo = (_b2 * x) - (_a2 * y);
            return y;
        }

        internal float Right(float x)
        {
            float y = (_b0 * x) + _rightOne;
            _rightOne = (_b1 * x) - (_a1 * y) + _rightTwo;
            _rightTwo = (_b2 * x) - (_a2 * y);
            return y;
        }
    }

    private readonly Biquad _highPass = new();
    private readonly Biquad _presence = new();
    private readonly Biquad _bandOne = new();
    private readonly Biquad _bandTwo = new();

    private readonly float _sampleRate;
    private readonly float _attackStep;
    private readonly float _releaseStep;
    private readonly int _holdSamples;
    private readonly float _maxGain;
    private readonly float _limitAttack;
    private readonly float _limitRelease;
    private readonly int _limitHoldSamples;
    private readonly float _sideStep;

    private uint _noiseRng = 0x9E3779B9u;
    private float _gain = 1f;
    private bool _seeded;
    private int _hold;
    private float _limit = 1f;
    private int _limitHold;
    private float _sideHistory;

    internal CamcorderMic(int sampleRate, float maxGainDecibels, float releaseDecibelsPerSecond)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 48000f;

        SetHighPass(_highPass, Constants.Audio.MicHighPass, 0.70710678f);
        SetPeaking(_presence, Constants.Audio.MicPresence, 1.1f, Constants.Audio.MicPresenceGain);
        SetLowPass(_bandOne, Constants.Audio.MicLowPass, 0.54119610f);
        SetLowPass(_bandTwo, Constants.Audio.MicLowPass, 1.30656296f);

        _maxGain = Decibels(maxGainDecibels);
        _attackStep = 1f - Mathf.Exp(-1f / (Constants.Audio.MicAttackTime * _sampleRate));
        _releaseStep = Mathf.Pow(10f, releaseDecibelsPerSecond / (20f * _sampleRate));
        _holdSamples = (int)(Constants.Audio.MicHoldTime * _sampleRate);
        _limitAttack = 1f - Mathf.Exp(-1f / (Constants.Audio.MicLimitAttackTime * _sampleRate));
        _limitRelease = 1f - Mathf.Exp(-1f / (Constants.Audio.MicLimitReleaseTime * _sampleRate));
        _limitHoldSamples = (int)(Constants.Audio.MicLimitHoldTime * _sampleRate);
        _sideStep = 1f - Mathf.Exp(-2f * Mathf.PI * Constants.Audio.MicSideHighPass / _sampleRate);
    }

    internal void Process(float[] buffer, int count, int channels)
    {
        if (buffer == null || count <= 0) return;

        int outChannels = channels > 0 ? channels : 1;
        int frames = count / outChannels;
        if (frames <= 0) return;

        float nominal = Constants.Audio.MicNominalLevel;
        float threshold = Constants.Audio.MicLimitThreshold;

        if (!_seeded)
        {
            _seeded = true;
            float openingPeak = 0f;
            for (int p = 0; p < count; p++)
            {
                float magnitude = buffer[p] < 0f ? -buffer[p] : buffer[p];
                if (magnitude > openingPeak) openingPeak = magnitude;
            }
            _gain = Mathf.Clamp(openingPeak > 1e-7f ? nominal / openingPeak : _maxGain, 1f, _maxGain);
        }
        float floor = Constants.Audio.MicNoiseFloorLevel;
        float amplitude = Constants.Audio.MicNoiseAmplitude;

        for (int f = 0, i = 0; f < frames; f++, i += outChannels)
        {
            float left = buffer[i];
            float right = outChannels >= 2 ? buffer[i + 1] : left;

            float noiseOne = NextNoise() * amplitude;
            float noiseTwo = NextNoise() * amplitude;
            left += noiseOne;
            right += (0.8f * noiseOne) + (0.6f * noiseTwo);

            left = _highPass.Left(left);
            right = _highPass.Right(right);
            left = _presence.Left(left);
            right = _presence.Right(right);
            left = _bandTwo.Left(_bandOne.Left(left));
            right = _bandTwo.Right(_bandOne.Right(right));

            float peak = Mathf.Max(Mathf.Abs(left), Mathf.Abs(right));
            float target = peak > 1e-7f ? Mathf.Min(_maxGain, nominal / peak) : _maxGain;

            if (target < _gain)
            {
                _gain += (target - _gain) * _attackStep;
                _hold = _holdSamples;
            }
            else if (_hold > 0)
            {
                _hold--;
            }
            else if (peak > floor)
            {
                _gain = Mathf.Min(Mathf.Min(_gain * _releaseStep, target), _maxGain);
            }

            left *= _gain;
            right *= _gain;

            float loud = Mathf.Max(Mathf.Abs(left), Mathf.Abs(right));
            float limitTarget = loud > threshold ? threshold / loud : 1f;

            if (limitTarget < _limit)
            {
                _limit += (limitTarget - _limit) * _limitAttack;
                _limitHold = _limitHoldSamples;
            }
            else if (_limitHold > 0)
            {
                _limitHold--;
            }
            else
            {
                _limit += (1f - _limit) * _limitRelease;
            }

            left *= _limit;
            right *= _limit;

            if (outChannels >= 2)
            {
                float mid = (left + right) * 0.5f;
                float side = (left - right) * 0.5f;
                _sideHistory += _sideStep * (side - _sideHistory);
                float shaped = (side - _sideHistory) * Constants.Audio.MicStereoWidth;
                left = mid + shaped;
                right = mid - shaped;
            }

            buffer[i] = SoftClip(left);
            if (outChannels >= 2) buffer[i + 1] = SoftClip(right);

            for (int c = 2; c < outChannels; c++)
            {
                buffer[i + c] = SoftClip(buffer[i + c]);
            }
        }
    }

    private static float SoftClip(float x)
    {
        float v = Mathf.Clamp(x * Constants.Audio.MicClipDrive, -3f, 3f);
        return v * (27f + (v * v)) / (27f + (9f * v * v));
    }

    private float NextNoise()
    {
        _noiseRng ^= _noiseRng << 13;
        _noiseRng ^= _noiseRng >> 17;
        _noiseRng ^= _noiseRng << 5;
        return ((_noiseRng & 0xFFFF) / 32768f) - 1f;
    }

    private static float Decibels(float value) => Mathf.Pow(10f, value / 20f);

    private void SetHighPass(Biquad filter, float frequency, float q)
    {
        float w = 2f * Mathf.PI * frequency / _sampleRate;
        float cos = Mathf.Cos(w);
        float alpha = Mathf.Sin(w) / (2f * q);
        float b0 = (1f + cos) * 0.5f;
        filter.Set(b0, -(1f + cos), b0, 1f + alpha, -2f * cos, 1f - alpha);
    }

    private void SetLowPass(Biquad filter, float frequency, float q)
    {
        float w = 2f * Mathf.PI * frequency / _sampleRate;
        float cos = Mathf.Cos(w);
        float alpha = Mathf.Sin(w) / (2f * q);
        float b0 = (1f - cos) * 0.5f;
        filter.Set(b0, 1f - cos, b0, 1f + alpha, -2f * cos, 1f - alpha);
    }

    private void SetPeaking(Biquad filter, float frequency, float q, float gainDecibels)
    {
        float a = Mathf.Pow(10f, gainDecibels / 40f);
        float w = 2f * Mathf.PI * frequency / _sampleRate;
        float cos = Mathf.Cos(w);
        float alpha = Mathf.Sin(w) / (2f * q);
        filter.Set(1f + (alpha * a), -2f * cos, 1f - (alpha * a), 1f + (alpha / a), -2f * cos, 1f - (alpha / a));
    }
}
