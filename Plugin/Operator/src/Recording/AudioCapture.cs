using System;
using UnityEngine;

namespace Operator.Recording;

internal sealed class AudioCapture : MonoBehaviour
{
    internal static float RecordedPeak01;

    private const float HissAmplitude = 0.15f;

    private AudioPipe? _pipe;
    private ShadowVoicePool? _pool;
    private CamcorderMic? _mic;
    private ReverbBus? _reverb;
    private Func<bool>? _shouldCapture;
    private Func<Recorder.FinalePhase>? _finalePhase;
    private Func<bool>? _forceHiss;
    private volatile bool _stopped;
    private uint _hissRng = 0x1F123BB5u;
    private float[]? _fx;
    private float _duckGain = 1f;
    private float _appliedGain = 1f;
    private float _shadowWeight;
    private float _listenerVolume = 1f;
    private volatile bool _submerged;
    private readonly UnderwaterFilter _underwater = new();
    private float _hissFadeTime;
    private long _hissFrames;
    private int _rate = 48000;

    public void Initialize(AudioPipe pipe, ShadowVoicePool? pool, Func<bool> shouldCapture, Func<Recorder.FinalePhase> finalePhase, Func<bool> forceHiss, float duckGain, float shadowWeight, bool room, bool submerged)
    {
        _pool = pool;
        _mic = BuildMic();
        _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        _hissFadeTime = Mathf.Max(0f, VideoCameraConfig.DeathStaticFadeSeconds.Value);
        _reverb = new ReverbBus(_rate, AudioSettings.GetConfiguration().dspBufferSize * 4);
        _reverb.SetRoom(room);
        _duckGain = duckGain;
        _appliedGain = duckGain;
        _shadowWeight = shadowWeight;
        _listenerVolume = AudioListener.volume;
        _submerged = submerged;
        _underwater.Reset(submerged);
        _shouldCapture = shouldCapture;
        _finalePhase = finalePhase;
        _forceHiss = forceHiss;
        _pipe = pipe;
    }

    public void Stop() => _stopped = true;

    private static CamcorderMic? BuildMic()
    {
        RecordedMicCharacter character = VideoCameraConfig.MicCharacter.Value;
        if (character == RecordedMicCharacter.Off) return null;

        float maxGain = character switch
        {
            RecordedMicCharacter.Casual => 10f,
            RecordedMicCharacter.Aggressive => 24f,
            _ => 18f
        };

        float release = character switch
        {
            RecordedMicCharacter.Casual => 13.3f,
            RecordedMicCharacter.Aggressive => 20f,
            _ => 9.09f
        };

        int rate = AudioSettings.outputSampleRate;
        return new CamcorderMic(rate > 0 ? rate : 48000, maxGain, release);
    }

    public void SetDuckGain(float gain) => _duckGain = gain;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_stopped || _pipe == null || data == null || data.Length == 0)
        {
            return;
        }

        int ch = channels > 0 ? channels : 1;
        int frames = data.Length / ch;
        float shadowScale = _shadowWeight * _listenerVolume;

        Recorder.FinalePhase phase = _finalePhase?.Invoke() ?? Recorder.FinalePhase.None;
        bool ending = phase == Recorder.FinalePhase.Static
                      || phase == Recorder.FinalePhase.Dead;

        if (ending)
        {
            WriteFinale(data.Length, channels, shadowScale, phase == Recorder.FinalePhase.Static);
            return;
        }

        bool rolling = (_forceHiss?.Invoke() ?? false)
                       || phase == Recorder.FinalePhase.DeadManStatic;

        if (_shouldCapture != null && !_shouldCapture())
        {
            _pool?.Mix(null, data.Length, ch, 0f);
            Voice.RemoteVoices.Discard();
            _reverb?.Begin(frames);
            return;
        }

        float hissGain = rolling ? TakeHissGain(frames) : 0f;

        int total = data.Length;
        if (_fx == null || _fx.Length < total)
        {
            _fx = new float[total];
        }

        float target = _duckGain;
        float start = _appliedGain;
        float delta = frames > 0 ? (target - start) / frames : 0f;

        for (int f = 0, i = 0; f < frames; f++)
        {
            float g = start + (delta * f);

            if (ch >= 2)
            {
                float mono = (data[i] + data[i + 1]) * 0.5f * g;
                _fx[i] = mono;
                _fx[i + 1] = mono;
                i += 2;

                for (int c = 2; c < ch; c++, i++)
                {
                    _fx[i] = data[i] * g;
                }

                continue;
            }

            for (int c = 0; c < ch; c++, i++)
            {
                _fx[i] = data[i] * g;
            }
        }

        _appliedGain = target;
        _pool?.Mix(_fx, frames * ch, ch, shadowScale);
        _reverb?.Begin(frames);
        Voice.RemoteVoices.Mix(_fx, frames, ch, shadowScale, _reverb);
        _reverb?.Process(_fx, frames, ch);
        if (hissGain > 0f) AddHiss(_fx, frames * ch, hissGain);
        _mic?.Process(_fx, frames * ch, ch);
        _underwater.Apply(_fx, frames, ch, _rate, _submerged);
        UpdatePeak(_fx, frames * ch);
        _pipe.EnqueueCopy(_fx, 0, frames * ch);
    }

    private void WriteFinale(int samples, int channels, float shadowScale, bool hiss)
    {
        if (_fx == null || _fx.Length < samples)
        {
            _fx = new float[samples];
        }

        if (hiss) FillHiss(_fx, samples, 1f);
        else Array.Clear(_fx, 0, samples);

        _pool?.Mix(_fx, samples, channels, shadowScale);
        Voice.RemoteVoices.Discard();
        UpdatePeak(_fx, samples);
        _pipe!.EnqueueCopy(_fx, 0, samples);
    }

    private float TakeHissGain(int frames)
    {
        if (_hissFadeTime <= 0f) return 0f;

        double elapsed = _hissFrames / (double)_rate;
        _hissFrames += frames;

        float remaining = (float)(1.0 - (elapsed / _hissFadeTime));
        return remaining > 0f ? remaining : 0f;
    }

    private void FillHiss(float[] buffer, int count, float gain)
    {
        uint s = _hissRng;
        float level = HissAmplitude * gain;
        for (int i = 0; i < count; i++)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            buffer[i] = (((s & 0xFFFF) / 32768f) - 1f) * level;
        }

        _hissRng = s;
    }

    private void AddHiss(float[] buffer, int count, float gain)
    {
        uint s = _hissRng;
        float level = HissAmplitude * gain;
        for (int i = 0; i < count; i++)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            buffer[i] += (((s & 0xFFFF) / 32768f) - 1f) * level;
        }

        _hissRng = s;
    }

    private static void UpdatePeak(float[] buffer, int count)
    {
        float peak = RecordedPeak01;
        for (int i = 0; i < count; i += 16)
        {
            float a = buffer[i] < 0f ? -buffer[i] : buffer[i];
            if (a > peak) peak = a;
        }

        RecordedPeak01 = peak;
    }

    public void SetRoom(bool inside) => _reverb?.SetRoom(inside);

    public void SetSubmerged(bool submerged) => _submerged = submerged;

    public void SetShadowWeight(float weight)
    {
        _shadowWeight = weight;
        _listenerVolume = AudioListener.volume;
    }

    private void OnDestroy() => Stop();
}
