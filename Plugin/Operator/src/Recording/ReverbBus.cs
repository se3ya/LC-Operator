using UnityEngine;

namespace Operator.Recording;

internal sealed class ReverbBus
{
    private static readonly int[] CombTuning = { 1215, 1293, 1390, 1476, 1548, 1623, 1695, 1760 };
    private static readonly int[] AllpassTuning = { 605, 480, 371, 245 };
    private const int StereoSpread = 25;

    private sealed class Comb
    {
        private readonly float[] _line;
        private int _index;
        private float _store;

        internal Comb(int size) => _line = new float[size];

        internal float Process(float input, float feedback, float damping)
        {
            float output = _line[_index];
            _store = (output * (1f - damping)) + (_store * damping);
            _line[_index] = input + (_store * feedback);
            if (++_index >= _line.Length) _index = 0;
            return output;
        }
    }

    private sealed class Allpass
    {
        private readonly float[] _line;
        private int _index;

        internal Allpass(int size) => _line = new float[size];

        internal float Process(float input)
        {
            float buffered = _line[_index];
            _line[_index] = input + (buffered * 0.5f);
            if (++_index >= _line.Length) _index = 0;
            return buffered - input;
        }
    }

    private readonly Comb[] _combsLeft = new Comb[CombTuning.Length];
    private readonly Comb[] _combsRight = new Comb[CombTuning.Length];
    private readonly Allpass[] _allpassLeft = new Allpass[AllpassTuning.Length];
    private readonly Allpass[] _allpassRight = new Allpass[AllpassTuning.Length];
    private readonly float[] _feedback = new float[CombTuning.Length];
    private readonly float[] _preDelay;
    private readonly float[] _input;

    private int _preIndex;
    private int _rate;
    private float _decayAt = -1f;

    private volatile float _decayTime = Constants.Audio.ShadowReverbDecayInside;
    private volatile float _damping = Constants.Audio.ShadowReverbDamping;
    private volatile float _outputGain = Constants.Audio.ShadowReverbOutputGain;

    internal ReverbBus(int sampleRate, int maxFrames)
    {
        _rate = sampleRate > 0 ? sampleRate : 48000;

        for (int i = 0; i < CombTuning.Length; i++)
        {
            _combsLeft[i] = new Comb(Scale(CombTuning[i]));
            _combsRight[i] = new Comb(Scale(CombTuning[i] + StereoSpread));
        }

        for (int i = 0; i < AllpassTuning.Length; i++)
        {
            _allpassLeft[i] = new Allpass(Scale(AllpassTuning[i]));
            _allpassRight[i] = new Allpass(Scale(AllpassTuning[i] + StereoSpread));
        }

        _preDelay = new float[Mathf.Max(1, (int)(Constants.Audio.ShadowReverbPreDelay * _rate))];
        _input = new float[Mathf.Max(1024, maxFrames)];
    }

    private int Scale(int reference) => Mathf.Max(1, Mathf.RoundToInt(reference * (_rate / 48000f)));

    internal void SetRoom(bool inside)
    {
        _decayTime = inside
            ? Constants.Audio.EchoIndoorDecay
            : Constants.Audio.EchoOutdoorDecay;
        _damping = Constants.Audio.ShadowReverbDamping;
        _outputGain = Constants.Audio.ShadowReverbOutputGain
                      * Mathf.Max(0f, VideoCameraConfig.EchoStrength.Value);
    }

    internal void Begin(int frames)
    {
        if (frames > _input.Length) frames = _input.Length;
        System.Array.Clear(_input, 0, frames);
    }

    internal void Send(float[] mono, int count, float gain)
    {
        if (gain <= 0f || mono == null) return;

        int limit = count < _input.Length ? count : _input.Length;
        for (int i = 0; i < limit; i++)
        {
            _input[i] += mono[i] * gain;
        }
    }

    internal void Process(float[] destination, int frames, int channels)
    {
        if (destination == null || channels < 1) return;

        int limit = frames < _input.Length ? frames : _input.Length;
        if (limit <= 0) return;

        float decay = _decayTime;
        float damping = _damping;
        float gain = _outputGain;
        if (_decayAt != decay)
        {
            _decayAt = decay;
            for (int i = 0; i < CombTuning.Length; i++)
            {
                float length = Scale(CombTuning[i]);
                _feedback[i] = Mathf.Clamp(Mathf.Pow(10f, -3f * length / (_rate * Mathf.Max(0.05f, decay))), 0f, 0.98f);
            }
        }

        for (int f = 0; f < limit; f++)
        {
            float dry = _preDelay[_preIndex];
            _preDelay[_preIndex] = _input[f];
            if (++_preIndex >= _preDelay.Length) _preIndex = 0;

            float left = 0f;
            float right = 0f;
            for (int c = 0; c < CombTuning.Length; c++)
            {
                left += _combsLeft[c].Process(dry, _feedback[c], damping);
                right += _combsRight[c].Process(dry, _feedback[c], damping);
            }

            for (int a = 0; a < AllpassTuning.Length; a++)
            {
                left = _allpassLeft[a].Process(left);
                right = _allpassRight[a].Process(right);
            }

            left *= gain;
            right *= gain;

            int index = f * channels;
            if (channels >= 2)
            {
                destination[index] += left;
                destination[index + 1] += right;
                continue;
            }

            destination[index] += (left + right) * 0.5f;
        }
    }
}
