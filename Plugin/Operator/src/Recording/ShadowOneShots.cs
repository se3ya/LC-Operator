using System.Collections.Generic;
using UnityEngine;

namespace Operator.Recording;

internal static class ShadowOneShotLog
{
    private sealed class Entry
    {
        public AudioSource Source = null!;
        public AudioClip Clip = null!;
        public float VolumeScale;
        public float StartedAt;
    }

    private static readonly List<Entry> _recent = new();

    internal static void Add(AudioSource host, AudioClip? clip, float volumeScale)
    {
        if (host == null || clip == null) return;
        if (clip.length < Constants.Audio.ShadowMinResumeLength) return;

        Prune();
        _recent.Add(new Entry
        {
            Source = host,
            Clip = clip,
            VolumeScale = volumeScale,
            StartedAt = Time.realtimeSinceStartup
        });
    }

    internal static void ResumeInto(ShadowOneShotMirror mirror)
    {
        Prune();

        for (int i = 0; i < _recent.Count; i++)
        {
            Entry entry = _recent[i];
            if (entry.Source == null) continue;

            float elapsed = Time.realtimeSinceStartup - entry.StartedAt;
            if (elapsed <= 0f || elapsed >= entry.Clip.length) continue;

            mirror.Resume(entry.Source, entry.Clip, entry.VolumeScale, elapsed);
        }
    }

    private static void Prune()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = _recent.Count - 1; i >= 0; i--)
        {
            Entry entry = _recent[i];
            if (entry.Source == null || entry.Clip == null || now - entry.StartedAt >= entry.Clip.length)
            {
                _recent.RemoveAt(i);
            }
        }
    }
}

internal sealed class ShadowOneShotMirror
{
    private sealed class Mirrored
    {
        public long Handle;
        public AudioSource? Origin;
        public Transform? OriginTransform;
        public Vector3 FallbackPosition;
        public float VolumeScale = 1f;
        public int Channels = 1;
        public bool FollowsObserver;
        public OccludeAudio? Occluder;
        public bool Occluded;
        public float OccludeCheckAt;
        public float Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
        public float ExpiresAt;
    }

    private static readonly List<ShadowOneShotMirror> _attached = new();

    private readonly List<Mirrored> _live = new();
    private readonly ShadowVoicePool _pool;

    private Vector3 _cameraPosition;
    private Vector3 _cameraRight;
    private Vector3 _cameraUp;
    private Vector3 _cameraForward;
    private Vector3 _observerPosition;
    private bool _ready;
    private int _mirrored;
    private int _outOfRange;

    internal ShadowOneShotMirror(ShadowVoicePool pool) => _pool = pool;

    internal static void MirrorAll(AudioSource host, AudioClip? clip, float volumeScale)
    {
        ShadowOneShotLog.Add(host, clip, volumeScale);

        for (int i = 0; i < _attached.Count; i++)
        {
            _attached[i].Mirror(host, clip, volumeScale, 0f);
        }
    }

    internal void Attach()
    {
        if (_attached.Contains(this)) return;

        _attached.Add(this);
        ShadowOneShotLog.ResumeInto(this);
    }

    internal void Resume(AudioSource host, AudioClip clip, float volumeScale, float elapsed)
        => Mirror(host, clip, volumeScale, elapsed);

    internal void Detach() => _attached.Remove(this);

    internal void SetContext(Vector3 cameraPosition, Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward, Vector3 observerPosition)
    {
        _cameraPosition = cameraPosition;
        _cameraRight = cameraRight;
        _cameraUp = cameraUp;
        _cameraForward = cameraForward;
        _observerPosition = observerPosition;
        _ready = true;
    }

    internal int TakeMirrored()
    {
        int value = _mirrored;
        _mirrored = 0;
        return value;
    }

    internal int TakeOutOfRange()
    {
        int value = _outOfRange;
        _outOfRange = 0;
        return value;
    }

    internal void Clear()
    {
        _ready = false;
        Detach();

        for (int i = 0; i < _live.Count; i++)
        {
            _pool.Release(_live[i].Handle);
        }

        _live.Clear();
    }

    private void Mirror(AudioSource host, AudioClip? clip, float volumeScale, float elapsed)
    {
        if (!_ready || !_pool.Active || host == null || clip == null) return;

        if (ShadowAudio.IsNonDiegetic(host))
        {
            ShadowDebug.LogOneShot(host.name, clip.name, "skipped, you are the only one who hears it");
            return;
        }

        bool followsObserver = ShadowAudio.FollowsObserver(host);
        float hostDistance = Vector3.Distance(host.transform.position, _cameraPosition);
        bool spatial = ShadowAudio.SpatialBlendAt(host, hostDistance) > 0f;

        if (!spatial && !followsObserver)
        {
            ShadowDebug.LogOneShot(host.name, clip.name, "skipped, flat sound with no place in the world");
            return;
        }

        if (!followsObserver && spatial)
        {
            float check = hostDistance;
            if (check > Constants.Audio.ShadowRadius || check > host.maxDistance)
            {
                ShadowDebug.LogOneShot(host.name, clip.name, "skipped, out of the camera's range");
                _outOfRange++;
                return;
            }
        }

        ShadowClipCache.Pcm? pcm = ShadowClipCache.Get(clip);
        if (pcm == null)
        {
            ShadowDebug.LogOneShot(host.name, clip.name, $"skipped, {ShadowClipCache.LastReason}");
            return;
        }

        float pitch = Mathf.Abs(host.pitch) > 0.01f ? Mathf.Abs(host.pitch) : 1f;
        double startFrame = Mathf.Clamp(elapsed, 0f, clip.length) * pcm.Frequency * pitch;

        long handle = _pool.Claim(pcm, false, startFrame, ShadowAudio.RateFor(host, pcm));
        if (handle < 0)
        {
            ShadowDebug.LogOneShot(host.name, clip.name, "skipped, no free voice");
            return;
        }

        ShadowDebug.LogOneShot(host.name, clip.name, elapsed > 0f ? "recorded, picked up mid clip" : "recorded");

        var mirrored = new Mirrored
        {
            Handle = handle,
            Origin = host,
            OriginTransform = host.transform,
            FallbackPosition = host.transform.position,
            VolumeScale = volumeScale,
            Channels = pcm.Channels,
            FollowsObserver = followsObserver,
            Occluder = host.GetComponent<OccludeAudio>(),
            OccludeCheckAt = Time.realtimeSinceStartup + (Random.value * Constants.Audio.ShadowOccludeCheckInterval),
            ExpiresAt = Time.realtimeSinceStartup + ((clip.length - Mathf.Clamp(elapsed, 0f, clip.length)) / pitch) + 1f
        };

        _live.Add(mirrored);
        ApplyOne(mirrored);
        _mirrored++;
    }

    private float CutoffFor(Mirrored mirrored, AudioSource origin, Vector3 sourcePosition)
    {
        OccludeAudio? occluder = mirrored.Occluder;
        if (occluder == null) return Constants.Audio.ShadowOccludeBypassCutoff;
        if (occluder.overridingLowPass) return occluder.lowPassOverride;

        float now = Time.realtimeSinceStartup;
        if (now >= mirrored.OccludeCheckAt)
        {
            mirrored.OccludeCheckAt = now + Constants.Audio.ShadowOccludeCheckInterval;
            mirrored.Occluded = Physics.Linecast(sourcePosition, _cameraPosition, 256, QueryTriggerInteraction.Ignore);
        }

        float target = Constants.Audio.ShadowOccludeOpenCutoff;
        if (mirrored.Occluded)
        {
            float max = Mathf.Max(1f, origin.maxDistance);
            float distance = Mathf.Max(0.01f, Vector3.Distance(sourcePosition, _cameraPosition));
            target = Mathf.Clamp(
                Constants.Audio.ShadowOccludeScale / (distance / (max * 0.5f)),
                Constants.Audio.ShadowOccludeMinCutoff,
                Constants.Audio.ShadowOccludeMaxCutoff);
        }

        mirrored.Cutoff = mirrored.Cutoff >= Constants.Audio.ShadowOccludeBypassCutoff
            ? target
            : Mathf.Lerp(mirrored.Cutoff, target, Time.deltaTime * Constants.Audio.ShadowOccludeGlide);

        return mirrored.Cutoff;
    }

    internal void ApplyGains()
    {
        float now = Time.realtimeSinceStartup;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Mirrored mirrored = _live[i];

            if (now >= mirrored.ExpiresAt || !_pool.IsLive(mirrored.Handle))
            {
                _pool.Release(mirrored.Handle);
                _live.RemoveAt(i);
                continue;
            }

            ApplyOne(mirrored);
        }
    }

    private void ApplyOne(Mirrored mirrored)
    {
        AudioSource? origin = mirrored.Origin;
        if (origin == null)
        {
            _pool.SetGain(mirrored.Handle, 0f, 0f, 0f);
            return;
        }

        Vector3 sourcePosition = mirrored.OriginTransform != null
            ? mirrored.OriginTransform.position
            : mirrored.FallbackPosition;

        Vector3 reference = mirrored.FollowsObserver ? _observerPosition : _cameraPosition;
        ShadowAudio.GetGains(origin, sourcePosition, reference, _cameraRight, _cameraForward, origin.volume, mirrored.Channels, -1f, out float flat, out float left, out float right);

        float scale = mirrored.VolumeScale;
        _pool.SetGain(mirrored.Handle, flat * scale, left * scale, right * scale);
        _pool.SetCutoff(mirrored.Handle, CutoffFor(mirrored, origin, sourcePosition));
    }
}
