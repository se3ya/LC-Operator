using System;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using UnityEngine;

namespace Operator.Recording;

internal sealed class DissonanceMicSubscriber : IMicrophoneSubscriber
{
    internal static float MicPeak01;

    private static DissonanceComms? _cachedComms;

    private readonly MicGate _gate = new();
    private DissonanceComms? _comms;
    private VoicePlayerState? _local;
    private Action<ArraySegment<float>>? _onSamples;
    private Action? _onOpen;
    private Func<bool>? _shouldCapture;
    public int SampleRate { get; private set; } = 48000;
    public int Channels => 1;

    public bool TryStart(Action<ArraySegment<float>> onSamples, Func<bool>? shouldCapture = null, Action? onOpen = null)
    {
        if (_cachedComms == null)
        {
            _cachedComms = UnityEngine.Object.FindObjectOfType<DissonanceComms>();
        }
        _comms = _cachedComms;
        if (_comms == null)
        {
            return false;
        }

        _onSamples = onSamples;
        _shouldCapture = shouldCapture;
        _onOpen = onOpen;
        Update();
        _comms.SubscribeToRecordedAudio(this);
        Plugin.Log.LogDebug("Mic capture subscribed via Dissonance.");
        return true;
    }

    public void Stop()
    {
        if (_comms != null)
        {
            try { _comms.UnsubscribeFromRecordedAudio(this); }
            catch (Exception) { }
        }
        _comms = null;
        _onSamples = null;
        _onOpen = null;
        _shouldCapture = null;
    }

    internal void Update()
    {
        float volume = VideoCameraConfig.MicVolume.Value;
        DissonanceComms? comms = _comms;
        if (comms == null)
        {
            _gate.SetState(false, false, false, volume);
            return;
        }

        VoicePlayerState? local = GetLocal(comms);
        _gate.SetState(local != null, comms.IsMuted, local != null && local.IsSpeaking, volume);
    }

    internal void Flush() => _gate.Flush();

    private VoicePlayerState? GetLocal(DissonanceComms comms)
    {
        string name = comms.LocalPlayerName;
        if (string.IsNullOrEmpty(name))
        {
            _local = null;
            return null;
        }

        if (_local == null || _local.Name != name)
        {
            _local = comms.FindPlayer(name);
        }
        return _local;
    }

    public void ReceiveMicrophoneData(ArraySegment<float> buffer, WaveFormat format)
    {
        if (_shouldCapture != null && !_shouldCapture())
        {
            return;
        }

        Action<ArraySegment<float>>? cb = _onSamples;
        if (cb == null || buffer.Count == 0 || buffer.Array == null)
        {
            return;
        }

        if (SampleRate != format.SampleRate)
        {
            SampleRate = format.SampleRate;
        }

        float peak = MicPeak01;
        int end = buffer.Offset + buffer.Count;
        for (int i = buffer.Offset; i < end; i += 16)
        {
            float a = buffer.Array![i] < 0f ? -buffer.Array[i] : buffer.Array[i];
            if (a > peak) peak = a;
        }
        MicPeak01 = peak;

        _gate.Process(buffer, format.SampleRate, cb, _onOpen);
    }

    public void Reset()
    {
        _gate.Flush();
    }
}
