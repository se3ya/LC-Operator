using System;
using UnityEngine;

namespace Operator.Recording.Voice;

internal sealed class MicCapture : MonoBehaviour
{
    internal static float RecordedPeak01;

    private const int MaxChannels = 8;

    private readonly HighShelf[] _shelves = new HighShelf[MaxChannels];
    private Action<float[], int>? _onProcessed;
    private volatile ReverbBus? _reverb;
    private float[] _mono = Array.Empty<float>();
    private int _rate = 48000;
    private float _shelfAt = float.NaN;
    private readonly UnderwaterFilter _underwater = new();

    private volatile float _dry = 1f;
    private volatile float _room;
    private volatile float _shelf;
    private volatile float _panLeft = 1f;
    private volatile float _panRight = 1f;
    private volatile bool _submerged;

    public void Configure(Action<float[], int> onProcessed, int sampleRate)
    {
        _onProcessed = onProcessed;
        _rate = sampleRate > 0 ? sampleRate : 48000;

        int frames = Mathf.Max(4096, AudioSettings.GetConfiguration().dspBufferSize * 4);
        _mono = new float[frames];

        var reverb = new ReverbBus(_rate, frames);
        reverb.SetRoom(false);
        _reverb = reverb;
    }

    public void SetGains(float dry, float room, float shelf, float panLeft, float panRight)
    {
        _dry = dry;
        _room = room;
        _shelf = shelf;
        _panLeft = panLeft;
        _panRight = panRight;
    }

    public void SetRoom(bool inside) => _reverb?.SetRoom(inside);

    public void SetSubmerged(bool submerged) => _submerged = submerged;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        ReverbBus? reverb = _reverb;
        if (reverb == null)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        int count = channels > 0 ? channels : 1;
        int frames = data.Length / count;
        bool fits = frames > 0 && frames <= _mono.Length;

        if (fits) FillMono(data, count, frames);
        ApplyVoice(data, count, frames);
        if (fits) AddRoom(reverb, data, count, frames);
        _underwater.Apply(data, frames, count, _rate, _submerged);

        _onProcessed?.Invoke(data, channels);
        UpdatePeak(data);
        Array.Clear(data, 0, data.Length);
    }

    private void FillMono(float[] data, int channels, int frames)
    {
        float scale = 1f / channels;
        for (int f = 0, i = 0; f < frames; f++, i += channels)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += data[i + c];
            _mono[f] = sum * scale;
        }
    }

    private void ApplyVoice(float[] data, int channels, int frames)
    {
        float shelf = _shelf;
        if (shelf != _shelfAt)
        {
            for (int c = 0; c < MaxChannels; c++)
            {
                _shelves[c].Set(Constants.Audio.ShadowVoiceDirectivityCorner, _rate, shelf);
            }

            _shelfAt = shelf;
        }

        float dry = _dry;
        for (int f = 0, i = 0; f < frames; f++, i += channels)
        {
            for (int c = 0; c < channels; c++)
            {
                float voice = c < MaxChannels ? _shelves[c].Process(data[i + c]) : data[i + c];
                data[i + c] = voice * dry;
            }
        }

        if (channels != 2) return;

        float left = _panLeft;
        float right = _panRight;
        for (int f = 0, i = 0; f < frames; f++, i += 2)
        {
            data[i] *= left;
            data[i + 1] *= right;
        }
    }

    private void AddRoom(ReverbBus reverb, float[] data, int channels, int frames)
    {
        reverb.Begin(frames);
        reverb.Send(_mono, frames, _room);
        reverb.Process(data, frames, channels);
    }

    private static void UpdatePeak(float[] data)
    {
        float peak = RecordedPeak01;
        for (int i = 0; i < data.Length; i += 16)
        {
            float a = data[i] < 0f ? -data[i] : data[i];
            if (a > peak) peak = a;
        }

        RecordedPeak01 = peak;
    }
}
