using System.Collections.Generic;
using UnityEngine;

namespace Operator.Recording;

internal static class ShadowClipCache
{
    internal sealed class Pcm
    {
        public float[] Samples = null!;
        public int Channels;
        public int Frequency;
        public int Frames;
        public float LastUsed;
        public bool LiveFilled;
        public float RefreshAt;
        public float[]? Spare;
    }

    private static readonly Dictionary<AudioClip, Pcm> _entries = new();
    private static readonly Dictionary<AudioClip, int> _attempts = new();
    private static readonly HashSet<AudioClip> _unusable = new();
    private static long _bytes;
    private static bool _stillLoading;

    internal static string LastReason { get; private set; } = string.Empty;

    internal static long Bytes => _bytes;

    internal static int Count => _entries.Count;

    internal static Pcm? Get(AudioClip? clip)
    {
        LastReason = string.Empty;

        if (clip == null)
        {
            LastReason = "no clip";
            return null;
        }

        if (_entries.TryGetValue(clip, out Pcm cached))
        {
            cached.LastUsed = Time.realtimeSinceStartup;
            return cached;
        }

        if (_unusable.Contains(clip))
        {
            LastReason = "the clip cannot be copied";
            return null;
        }

        Pcm? built = Build(clip);
        if (built != null)
        {
            built.LastUsed = Time.realtimeSinceStartup;
            _entries[clip] = built;
            _attempts.Remove(clip);
            _bytes += (long)built.Samples.Length * sizeof(float);
            return built;
        }

        if (_stillLoading) return null;

        _attempts.TryGetValue(clip, out int attempts);
        _attempts[clip] = attempts + 1;
        if (attempts + 1 >= Constants.Audio.ShadowClipReadAttempts)
        {
            _unusable.Add(clip);
        }

        return null;
    }

    internal static void MarkLiveFilled(Pcm pcm)
    {
        if (pcm.LiveFilled) return;
        pcm.LiveFilled = true;
        pcm.RefreshAt = 0f;
    }

    internal static void RefreshLive(AudioClip? clip, Pcm? pcm)
    {
        if (clip == null || pcm == null || !pcm.LiveFilled) return;

        float now = Time.realtimeSinceStartup;
        if (now < pcm.RefreshAt) return;
        pcm.RefreshAt = now + Constants.Audio.ShadowLiveRefreshInterval;

        float[] target = pcm.Spare ??= new float[pcm.Samples.Length];
        if (target.Length != pcm.Samples.Length) return;

        try
        {
            if (!clip.GetData(target, 0)) return;
        }
        catch
        {
            pcm.LiveFilled = false;
            return;
        }

        pcm.Spare = pcm.Samples;
        pcm.Samples = target;
    }

    private static Pcm? Build(AudioClip clip)
    {
        _stillLoading = false;

        if (clip.loadType == AudioClipLoadType.Streaming || clip.loadType == AudioClipLoadType.CompressedInMemory)
        {
            LastReason = "this kind of clip cannot be copied";
            _unusable.Add(clip);
            return null;
        }

        if (clip.loadState == AudioDataLoadState.Loading)
        {
            LastReason = "clip data is still loading";
            _stillLoading = true;
            return null;
        }

        int frames = clip.samples;
        int channels = Mathf.Max(1, clip.channels);
        if (frames <= 0)
        {
            LastReason = "the clip is empty";
            return null;
        }

        long needed = (long)frames * channels * sizeof(float);
        if (!MakeRoomFor(needed))
        {
            LastReason = "the clip is too large to copy";
            return null;
        }

        var samples = new float[frames * channels];
        if (clip.GetData(samples, 0))
        {
            return new Pcm
            {
                Samples = samples,
                Channels = channels,
                Frequency = clip.frequency > 0 ? clip.frequency : AudioSettings.outputSampleRate,
                Frames = frames
            };
        }

        if (clip.loadState == AudioDataLoadState.Unloaded)
        {
            clip.LoadAudioData();
            LastReason = "clip data is still loading";
            _stillLoading = true;
            return null;
        }

        LastReason = "the game refused to hand over the clip";
        return null;
    }

    private static bool MakeRoomFor(long needed)
    {
        long budget = Constants.Audio.ShadowClipCacheSize;
        if (needed > budget) return false;

        while (_bytes + needed > budget && _entries.Count > 0)
        {
            AudioClip? oldest = null;
            float oldestUse = float.MaxValue;

            foreach (KeyValuePair<AudioClip, Pcm> entry in _entries)
            {
                if (entry.Value.LastUsed >= oldestUse) continue;
                oldestUse = entry.Value.LastUsed;
                oldest = entry.Key;
            }

            if (oldest == null) return false;
            Evict(oldest);
        }

        return _bytes + needed <= budget;
    }

    private static void Evict(AudioClip clip)
    {
        if (!_entries.TryGetValue(clip, out Pcm pcm)) return;

        _bytes -= (long)pcm.Samples.Length * sizeof(float);
        pcm.Spare = null;
        _entries.Remove(clip);
        if (_bytes < 0) _bytes = 0;
    }

    internal static void Prune()
    {
        var dead = new List<AudioClip>();
        foreach (KeyValuePair<AudioClip, Pcm> entry in _entries)
        {
            if (entry.Key == null) dead.Add(entry.Key!);
        }

        for (int i = 0; i < dead.Count; i++)
        {
            Evict(dead[i]);
        }

        _attempts.Clear();
        _unusable.Clear();
    }
}
