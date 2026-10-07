using System.Collections.Generic;
using Dissonance.Audio.Playback;
using UnityEngine;

namespace Operator.Recording;

internal static class ShadowCurveCache
{
    internal sealed class Entry
    {
        public AnimationCurve? Rolloff;
        public AnimationCurve? Blend;
        public AnimationCurve? Spread;
        public bool IsVoicePlayback;
        public float RefreshAt;
    }

    private static readonly Dictionary<AudioSource, Entry> _entries = new();
    private static readonly List<AudioSource> _dead = new();

    internal static Entry For(AudioSource source)
    {
        float now = Time.realtimeSinceStartup;

        if (_entries.TryGetValue(source, out Entry cached))
        {
            if (now < cached.RefreshAt) return cached;

            cached.Rolloff = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            cached.Blend = source.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
            cached.Spread = source.GetCustomCurve(AudioSourceCurveType.Spread);
            cached.RefreshAt = now + NextInterval();
            return cached;
        }

        var entry = new Entry
        {
            Rolloff = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff),
            Blend = source.GetCustomCurve(AudioSourceCurveType.SpatialBlend),
            Spread = source.GetCustomCurve(AudioSourceCurveType.Spread),
            IsVoicePlayback = source.GetComponent<SamplePlaybackComponent>() != null,
            RefreshAt = now + NextInterval()
        };

        _entries[source] = entry;
        return entry;
    }

    private static float NextInterval()
        => Constants.Audio.ShadowCurveRefreshInterval * Random.Range(1f, 2f);

    internal static void Prune()
    {
        _dead.Clear();
        foreach (KeyValuePair<AudioSource, Entry> entry in _entries)
        {
            if (entry.Key == null) _dead.Add(entry.Key!);
        }

        for (int i = 0; i < _dead.Count; i++)
        {
            _entries.Remove(_dead[i]);
        }

        _dead.Clear();
    }
}
