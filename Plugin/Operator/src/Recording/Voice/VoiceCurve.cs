using System;
using Dissonance;
using UnityEngine;

namespace Operator.Recording.Voice;

internal static class VoiceCurve
{
    private static readonly AnimationCurve Fallback = new(
        new Keyframe(0.15789473f, 1f, 0f, 0f),
        new Keyframe(0.39042088f, 0.81336546f, -1.8420166f, -1.8420166f),
        new Keyframe(0.6216507f, 0.19900563f, -1.1875f, -1.1875f),
        new Keyframe(1f, 0f, -0.21433946f, -0.21433946f));

    private static AudioSource? _prefab;
    private static float _retryAt;

    internal static float At(AudioSource? source, float meters)
    {
        AudioSource? use = source != null ? source : GetPrefab();
        if (use != null && HasCurve(use)) return ShadowAudio.RolloffAt(use, meters);
        return Fallback.Evaluate(Mathf.Clamp01(meters / Constants.Audio.VoiceRange));
    }

    private static bool HasCurve(AudioSource source)
    {
        if (source.rolloffMode != AudioRolloffMode.Custom) return true;
        AnimationCurve? curve = ShadowCurveCache.For(source).Rolloff;
        return curve != null && curve.length > 1;
    }

    private static AudioSource? GetPrefab()
    {
        if (_prefab != null) return _prefab;

        float now = Time.realtimeSinceStartup;
        if (now < _retryAt) return null;
        _retryAt = now + Constants.Audio.ShadowCurveRefreshInterval;

        try
        {
            DissonanceComms? comms = StartOfRound.Instance.voiceChatModule;
            GameObject? prefab = comms != null ? comms.PlaybackPrefab : null;
            AudioSource? found = prefab != null ? prefab.GetComponentInChildren<AudioSource>(true) : null;
            if (found == null || !HasCurve(found)) return null;

            _prefab = found;
            return found;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
