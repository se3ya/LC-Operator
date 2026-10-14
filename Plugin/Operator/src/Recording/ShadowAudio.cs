using System.Collections.Generic;
using Operator.Compat;
using UnityEngine;
using UnityEngine.Audio;

namespace Operator.Recording;

internal sealed class ShadowAudio
{
    private sealed class Shadow
    {
        public AudioSource Origin = null!;
        public AudioClip Clip = null!;
        public long Handle;
        public bool FollowsObserver;
        public bool IsWeather;
        public bool IsOperatorEnvironment;
        public float PeakVolume;
        public string Name = string.Empty;
        public bool Loops;
        public bool Orphaned;
        public float OrphanUntil;
        public float HeldFlat;
        public float HeldLeft;
        public float HeldRight;
        public bool IsOwnSound;
        public OccludeAudio? Occluder;
        public bool Occluded;
        public float OccludeCheckAt;
        public float Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
        public float Openness = 1f;
        public int SyncFrame;
        public ShadowClipCache.Pcm Pcm = null!;
    }

    private readonly List<Shadow> _shadows = new();
    private readonly List<AudioSource> _candidates = new();
    private readonly List<string> _decisions = new();
    private readonly ShadowOneShotMirror _mirror;
    private float _nextScan;
    private float _nextReport;
    private bool _cameraInside;
    private bool _observerInside;
    private Vector3 _observerPosition;
    private int _reportedCount = -1;

    internal ShadowVoicePool Pool { get; } = new();

    internal Transform? OwnRoot { get; set; }

    internal ShadowAudio() => _mirror = new ShadowOneShotMirror(Pool);

    internal void Begin()
    {
        Pool.Open();
        _mirror.Attach();
    }

    internal void SetContext(Vector3 cameraPosition, Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward, Vector3 observerPosition)
        => _mirror.SetContext(cameraPosition, cameraRight, cameraUp, cameraForward, observerPosition);

    internal void Update(Transform camera, Vector3 observer, float weight, bool cameraInside, bool observerInside)
    {
        _cameraInside = cameraInside;
        _observerInside = observerInside;
        _observerPosition = observer;

        if (camera == null)
        {
            Clear();
            return;
        }

        PruneDeadShadows();

        bool idle = weight <= 0f;
        if (!idle && Time.realtimeSinceStartup >= _nextScan)
        {
            _nextScan = Time.realtimeSinceStartup + Constants.Audio.ShadowScanInterval;
            RebuildShadows(camera);
        }

        ApplyGains(camera, observer);
        _mirror.ApplyGains();
        Report(weight);
    }

    private void Report(float weight)
    {
        if (_shadows.Count == _reportedCount && Time.realtimeSinceStartup < _nextReport) return;

        _reportedCount = _shadows.Count;
        _nextReport = Time.realtimeSinceStartup + Constants.Audio.ShadowReportInterval;

        int mirrored = _mirror.TakeMirrored();
        int outOfRange = _mirror.TakeOutOfRange();
        int starved = Pool.TakeStarved();

        if (_shadows.Count == 0)
        {
            Plugin.Log.LogDebug(
                $"No sounds near the camera to record, weight {weight:0.00}, "
                + $"{mirrored} one shots mirrored, {outOfRange} out of range, {starved} past the voice limit.");
            return;
        }

        var names = new System.Text.StringBuilder();
        for (int i = 0; i < _shadows.Count; i++)
        {
            if (names.Length > 0) names.Append(", ");
            names.Append(_shadows[i].Name);
            if (_shadows[i].Orphaned) names.Append(" (held)");
        }

        string where = _cameraInside == _observerInside
            ? _cameraInside ? "both inside" : "both outside"
            : _cameraInside ? "camera inside, you outside" : "camera outside, you inside";

        Plugin.Log.LogDebug(
            $"Recording {_shadows.Count} sounds around the camera, {where}, at weight {weight:0.00}, "
            + $"{mirrored} one shots mirrored, {outOfRange} out of range, {starved} past the voice limit: {names}.");
    }

    internal void Clear()
    {
        for (int i = 0; i < _shadows.Count; i++)
        {
            Pool.Release(_shadows[i].Handle);
        }

        _shadows.Clear();
        _nextScan = 0f;
    }

    internal void Release()
    {
        Clear();
        _mirror.Clear();
        Pool.Close();
        ShadowClipCache.Prune();
        ShadowCurveCache.Prune();
    }

    private void PruneDeadShadows()
    {
        for (int i = _shadows.Count - 1; i >= 0; i--)
        {
            Shadow shadow = _shadows[i];

            if (shadow.Orphaned)
            {
                AudioSource? held = shadow.Origin;
                bool restored = held != null && held.isPlaying && SoundApiCompat.ClipOn(held) == shadow.Clip;
                bool expired = !StillHeldFor(shadow) && Time.realtimeSinceStartup >= shadow.OrphanUntil;
                if (restored || expired)
                {
                    Pool.Release(shadow.Handle);
                    _shadows.RemoveAt(i);
                }

                continue;
            }

            AudioSource? origin = shadow.Origin;
            bool lost = origin == null;
            bool dead = lost
                        || !origin!.isPlaying
                        || SoundApiCompat.ClipOn(origin) != shadow.Clip
                        || !Pool.IsLive(shadow.Handle);

            if (!dead) continue;

            if (shadow.Loops && Pool.IsLive(shadow.Handle) && StillHeldFor(shadow))
            {
                shadow.Orphaned = true;
                shadow.OrphanUntil = Time.realtimeSinceStartup + Constants.Audio.ShadowOrphanTime;
                Pool.SetSyncFrame(shadow.Handle, -1);
                Pool.SetGain(shadow.Handle, shadow.HeldFlat, shadow.HeldLeft, shadow.HeldRight);
                Plugin.Log.LogDebug($"{shadow.Name} is held on the camera because the game switched it off");
                continue;
            }

            Plugin.Log.LogDebug(lost
                ? $"{shadow.Name} left the camera mix because the game removed it"
                : origin!.isPlaying
                    ? $"{shadow.Name} left the camera mix because its clip changed"
                    : $"{shadow.Name} left the camera mix because the game stopped it");

            Pool.Release(shadow.Handle);
            _shadows.RemoveAt(i);
        }
    }

    private bool IsOwnCamera(Transform target)
    {
        Transform? root = OwnRoot;
        if (root == null) return false;

        for (Transform? walk = target; walk != null; walk = walk.parent)
        {
            if (walk == root) return true;
        }

        return false;
    }

    private bool StillHeldFor(Shadow shadow)
        => shadow.IsWeather ? !_cameraInside : _cameraInside != _observerInside;

    private bool AlreadyHeld(AudioSource source)
    {
        for (int i = 0; i < _shadows.Count; i++)
        {
            if (_shadows[i].Orphaned && _shadows[i].Clip == SoundApiCompat.ClipOn(source)) return true;
        }

        return false;
    }

    private bool AlreadyShadowed(AudioSource source)
    {
        for (int i = 0; i < _shadows.Count; i++)
        {
            if (_shadows[i].Origin == source) return true;
        }

        return false;
    }

    private void RebuildShadows(Transform camera)
    {
        CollectCandidates(camera, _observerPosition);

        int admitted = 0;
        for (int i = 0; i < _candidates.Count && _shadows.Count < Constants.Audio.ShadowMaxSources; i++)
        {
            if (admitted >= Constants.Audio.ShadowAdmitPerScan) break;

            AudioSource source = _candidates[i];
            if (AlreadyShadowed(source) || AlreadyHeld(source)) continue;
            if (CreateShadow(source)) admitted++;
        }
    }

    private void CollectCandidates(Transform camera, Vector3 observer)
    {
        _candidates.Clear();
        _decisions.Clear();
        Vector3 origin = camera.position;
        float radius = Constants.Audio.ShadowRadius;
#if DEBUG
        bool census = ShadowDebug.DueForCensus();
#endif

        AudioSource[] all = Object.FindObjectsOfType<AudioSource>();
        for (int i = 0; i < all.Length; i++)
        {
            AudioSource source = all[i];
            string decision = Classify(source, origin, radius, out bool admit);
#if DEBUG
            if (census) _decisions.Add(decision);
#endif
            if (admit) _candidates.Add(source);
        }

#if DEBUG
        if (census) ShadowDebug.WriteCensus(all, origin, observer, _decisions);
#endif

        _candidates.Sort((a, b) =>
            Vector3.SqrMagnitude(a.transform.position - origin)
                .CompareTo(Vector3.SqrMagnitude(b.transform.position - origin)));
    }

    private string Classify(AudioSource source, Vector3 origin, float radius, out bool admit)
    {
        admit = false;
        if (source == null) return "gone";
        if (!source.isPlaying) return "not playing";
        if (source.mute && !Operator.Compat.MirageCompat.ShouldHearMimic(source, _cameraInside))
        {
            return "muted by the game";
        }

        if (IsNonDiegetic(source)) return "skipped, you are the only one who hears it";
        if (ShadowCurveCache.For(source).IsVoicePlayback) return "skipped, a voice is recorded on its own";
        if (IsOperatorEnvironment(source) && _cameraInside != _observerInside)
        {
            return "skipped, it is the room you are standing in and not the camera's";
        }

        AudioClip? clip = SoundApiCompat.ClipOn(source);
        if (clip == null) return "no clip, only reachable as a one shot";
        if (clip.loadType == AudioClipLoadType.Streaming) return "skipped, streamed clip";
        if (AlreadyShadowed(source)) { admit = true; return "already recorded"; }
        if (!source.loop && clip.length < Constants.Audio.ShadowMinClipLength) return "skipped, too short to catch";
        if (source.timeSamples <= 0) return "skipped, the clip on it is not the one playing";

        bool follows = FollowsObserver(source);
        float distance = Vector3.Distance(origin, source.transform.position);
        bool flat = SpatialBlendAt(source, distance) <= 0f;

        if (flat)
        {
            if (!source.loop && !follows) return "skipped, flat sound with no place in the world";
            admit = true;
            return follows ? "recorded, follows you" : "recorded, flat world sound";
        }

        if (!follows)
        {
            if (distance > radius) return "skipped, past the camera radius";
            if (RolloffAt(source, distance) <= 0.0005f) return "skipped, too far to carry";
        }

        admit = true;
        return follows ? "recorded, follows you" : "recorded";
    }

    internal static bool IsOperatorEnvironment(AudioSource source)
        => source.transform.parent == null
           && source.loop
           && source.name.StartsWith("Chameleon_", System.StringComparison.Ordinal);

    internal static bool IsNonDiegetic(AudioSource source)
    {
        SoundManager sound = SoundManager.Instance;

        return source == sound.ambienceAudioNonDiagetic
               || source == sound.musicSource
               || source == sound.highAction1
               || source == sound.highAction2
               || source == sound.lowAction
               || source == sound.heartbeatSFX
               || source == sound.poisonAudio
               || source == sound.ringingEarsAudio
               || source == sound.ringingEarsAudio2;
    }

    internal static bool FollowsObserver(AudioSource source) => IsUnderWeatherEffect(source.transform);

    internal static bool IsUnderWeatherEffect(Transform target)
    {
        TimeOfDay time = TimeOfDay.Instance;
        if (time.effects == null) return false;

        for (Transform? walk = target; walk != null; walk = walk.parent)
        {
            GameObject go = walk.gameObject;
            for (int i = 0; i < time.effects.Length; i++)
            {
                WeatherEffect effect = time.effects[i];
                if (effect == null) continue;
                if (effect.effectObject == go) return true;
            }
        }

        return false;
    }

    private bool CreateShadow(AudioSource source)
    {
        AudioClip? clip = SoundApiCompat.ClipOn(source);
        if (clip == null) return false;

        ShadowClipCache.Pcm? pcm = ShadowClipCache.Get(clip);
        if (pcm == null)
        {
            ShadowDebug.LogOneShot(source.name, clip.name, $"not copied, {ShadowClipCache.LastReason}");
            return false;
        }

        if (Operator.Compat.MirageCompat.IsStreaming(source)) ShadowClipCache.MarkLiveFilled(pcm);

        int frame = source.timeSamples;
        long handle = Pool.Claim(pcm, source.loop, frame, RateFor(source, pcm));
        if (handle < 0) return false;

        _shadows.Add(new Shadow
        {
            Origin = source,
            Clip = clip,
            Handle = handle,
            Pcm = pcm,
            Name = source.name,
            Loops = source.loop,
            FollowsObserver = FollowsObserver(source),
            IsWeather = IsUnderWeatherEffect(source.transform),
            IsOperatorEnvironment = IsOperatorEnvironment(source),
            IsOwnSound = IsOwnCamera(source.transform),
            Occluder = source.GetComponent<OccludeAudio>(),
            OccludeCheckAt = Time.realtimeSinceStartup + (Random.value * Constants.Audio.ShadowOccludeCheckInterval),
            PeakVolume = source.volume,
            SyncFrame = frame
        });

        return true;
    }

    internal static float RateFor(AudioSource source, ShadowClipCache.Pcm pcm)
    {
        int output = AudioSettings.outputSampleRate;
        if (output <= 0) output = 48000;

        float pitch = Mathf.Abs(source.pitch);
        if (pitch < 0.01f) pitch = 1f;

        return (float)pcm.Frequency / output * pitch;
    }

    private void ApplyGains(Transform camera, Vector3 observer)
    {
        Vector3 origin = camera.position;
        Vector3 right = camera.right;
        Vector3 forward = camera.forward;

        for (int i = 0; i < _shadows.Count; i++)
        {
            Shadow shadow = _shadows[i];
            if (shadow.Orphaned) continue;

            AudioSource source = shadow.Origin;
            if (source == null) continue;

            if (shadow.IsOperatorEnvironment && _cameraInside != _observerInside)
            {
                shadow.HeldFlat = 0f;
                shadow.HeldLeft = 0f;
                shadow.HeldRight = 0f;
                Pool.SetGain(shadow.Handle, 0f, 0f, 0f);
                continue;
            }

            Vector3 reference = shadow.FollowsObserver ? observer : origin;
            Vector3 emitter = shadow.IsWeather ? reference : source.transform.position;

            shadow.PeakVolume = source.volume > shadow.PeakVolume
                ? source.volume
                : Mathf.Lerp(shadow.PeakVolume, source.volume, Time.deltaTime * Constants.Audio.ShadowPeakDecay);
            float volume = _cameraInside != _observerInside
                ? Mathf.Max(source.volume, shadow.PeakVolume)
                : source.volume;

            GetGains(source, emitter, reference, right, forward, volume, shadow.Pcm.Channels, -1f, out float flat, out float left, out float rightGain);

            if (shadow.IsOwnSound)
            {
                flat *= Constants.Audio.ShadowOwnCameraGain;
                left *= Constants.Audio.ShadowOwnCameraGain;
                rightGain *= Constants.Audio.ShadowOwnCameraGain;
            }

            if (shadow.IsWeather && _cameraInside)
            {
                flat *= Constants.Audio.ShadowWeatherIndoorGain;
                left *= Constants.Audio.ShadowWeatherIndoorGain;
                rightGain *= Constants.Audio.ShadowWeatherIndoorGain;
            }

            shadow.HeldFlat = flat;
            shadow.HeldLeft = left;
            shadow.HeldRight = rightGain;

            float blocked = Mathf.Lerp(Constants.Audio.ShadowOccludeGain, 1f, shadow.Openness);
            flat *= blocked;
            left *= blocked;
            rightGain *= blocked;

            ShadowClipCache.RefreshLive(shadow.Clip, shadow.Pcm);

            Pool.SetGain(shadow.Handle, flat, left, rightGain);
            Pool.SetRate(shadow.Handle, RateFor(source, shadow.Pcm));
            int frame = source.timeSamples;
            if (frame > 0) shadow.SyncFrame = frame;
            Pool.SetSyncFrame(shadow.Handle, shadow.SyncFrame);
            Pool.SetCutoff(shadow.Handle, UpdateCutoff(shadow, source, origin));
        }
    }

    private static float UpdateCutoff(Shadow shadow, AudioSource source, Vector3 camera)
    {
        OccludeAudio? occluder = shadow.Occluder;
        if (occluder == null) return Constants.Audio.ShadowOccludeBypassCutoff;

        if (occluder.overridingLowPass)
        {
            shadow.Cutoff = occluder.lowPassOverride;
            return shadow.Cutoff;
        }

        shadow.Cutoff = GetOcclusionCutoff(
            source.transform.position,
            camera,
            source.maxDistance,
            shadow.Cutoff,
            ref shadow.Occluded,
            ref shadow.OccludeCheckAt,
            ref shadow.Openness);

        return shadow.Cutoff;
    }

    private static readonly Collider[] _selfColliders = new Collider[8];
    private static readonly RaycastHit[] _blockers = new RaycastHit[16];

    internal static bool LineOccluded(Vector3 emitter, Vector3 camera)
    {
        Vector3 toCamera = camera - emitter;
        float distance = toCamera.magnitude;
        if (distance < 0.001f) return false;

        int own = Physics.OverlapSphereNonAlloc(
            emitter, Constants.Audio.ShadowOccludeSelfRadius, _selfColliders, 256, QueryTriggerInteraction.Ignore);

        int hits = Physics.RaycastNonAlloc(
            new Ray(emitter, toCamera / distance), _blockers, distance, 256, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            Collider blocker = _blockers[i].collider;
            if (blocker == null) continue;

            bool isOwn = false;
            for (int o = 0; o < own; o++)
            {
                if (_selfColliders[o] != blocker) continue;
                isOwn = true;
                break;
            }

            if (!isOwn) return true;
        }

        return false;
    }

    internal static float GetOcclusionCutoff(Vector3 emitter, Vector3 camera, float maxDistance, float cutoff, ref bool occluded, ref float checkAt, ref float openness)
    {
        float max = Mathf.Max(1f, maxDistance);
        float now = Time.realtimeSinceStartup;

        if (now >= checkAt)
        {
            checkAt = now + Constants.Audio.ShadowOccludeCheckInterval;
            if ((emitter - camera).sqrMagnitude < Mathf.Min(max * max, Constants.Audio.ShadowOccludeRangeSqr))
            {
                occluded = LineOccluded(emitter, camera);
            }
        }

        float span = occluded
            ? Constants.Audio.ShadowOccludeCloseTime
            : Constants.Audio.ShadowOccludeClearTime;
        openness = Mathf.MoveTowards(openness, occluded ? 0f : 1f, Time.deltaTime / Mathf.Max(0.01f, span));

        float target = Constants.Audio.ShadowOccludeOpenCutoff;
        if (occluded)
        {
            float distance = Mathf.Max(0.01f, Vector3.Distance(emitter, camera));
            target = Mathf.Clamp(
                Constants.Audio.ShadowOccludeScale / (distance / (max * 0.5f)),
                Constants.Audio.ShadowOccludeMinCutoff,
                Constants.Audio.ShadowOccludeMaxCutoff);
        }

        return Mathf.Lerp(
            cutoff >= Constants.Audio.ShadowOccludeBypassCutoff ? target : cutoff,
            target,
            Time.deltaTime * Constants.Audio.ShadowOccludeGlide);
    }

    internal static void GetGains(AudioSource source, Vector3 sourcePosition, Vector3 reference, Vector3 right, Vector3 forward, float volume, int channels, float spatialOverride, out float flat, out float left, out float rightGain, float rolloffOverride = -1f)
    {
        flat = 0f;
        left = 0f;
        rightGain = 0f;

        Vector3 toSource = sourcePosition - reference;
        float distance = toSource.magnitude;
        float spatial = spatialOverride >= 0f ? spatialOverride : SpatialBlendAt(source, distance);
        float level = volume * GroupGain(source);
        if (level <= 0.0001f) return;

        float spread = SpreadAt(source, distance);

        if (spatial < 1f)
        {
            float flatChannel = channels >= 2
                ? 1f
                : spread > 0f
                    ? Constants.Audio.ShadowSpreadFlatGain
                    : Constants.Audio.ShadowCentreGain;
            flat = level * (1f - spatial) * flatChannel;
        }

        if (spatial <= 0f) return;

        float reach = rolloffOverride >= 0f ? rolloffOverride : RolloffAt(source, distance);
        float directional = level * spatial * reach;
        if (directional <= 0.0001f) return;

        float panLeft;
        float panRight;
        if (distance > 0.0001f)
        {
            Vector3 unit = toSource / distance;
            float x = Vector3.Dot(unit, right);
            float z = Vector3.Dot(unit, forward);
            float horizontal = Mathf.Sqrt((x * x) + (z * z));
            float azimuth = Mathf.Atan2(x, z);
            float half = spread * Mathf.Deg2Rad * 0.5f;

            float a = horizontal * Mathf.Sin(azimuth - half);
            float b = horizontal * Mathf.Sin(azimuth + half);

            panLeft = 0.5f * (PanLeft(a) + PanLeft(b));
            panRight = 0.5f * (PanRight(a) + PanRight(b));
        }
        else
        {
            panLeft = Constants.Audio.ShadowCentreGain;
            panRight = Constants.Audio.ShadowCentreGain;
        }

        left = directional * panLeft;
        rightGain = directional * panRight;
    }

    private static float PanLeft(float pan) => (Mathf.Sqrt(2f - (pan * pan)) - pan) * 0.5f;

    private static float PanRight(float pan) => (Mathf.Sqrt(2f - (pan * pan)) + pan) * 0.5f;

    internal static float SpreadAt(AudioSource source, float distance)
    {
        AnimationCurve? curve = ShadowCurveCache.For(source).Spread;
        if (curve != null && curve.length > 1)
        {
            float max = source.maxDistance;
            float t = max > 0f ? Mathf.Clamp01(distance / max) : 0f;
            return Mathf.Clamp(curve.Evaluate(t) * 360f, 0f, 360f);
        }

        return Mathf.Clamp(source.spread, 0f, 360f);
    }

    internal static float GroupGain(AudioSource source)
    {
        AudioMixerGroup group = source.outputAudioMixerGroup;
        if (group == null) return 1f;

        string name = group.name;
        if (string.Equals(name, "SFX", System.StringComparison.Ordinal)) return Constants.Audio.ShadowSfxGroupGain;
        if (name.StartsWith("VoicePlayer", System.StringComparison.Ordinal)) return Constants.Audio.ShadowVoiceGroupGain;
        return 1f;
    }

    internal static float SpatialBlendAt(AudioSource source, float distance)
    {
        AnimationCurve? curve = ShadowCurveCache.For(source).Blend;
        if (curve != null && curve.length > 1)
        {
            float max = source.maxDistance;
            float t = max > 0f ? Mathf.Clamp01(distance / max) : 0f;
            return Mathf.Clamp01(curve.Evaluate(t));
        }

        return Mathf.Clamp01(source.spatialBlend);
    }

    internal static float RolloffAt(AudioSource source, float distance)
    {
        float max = source.maxDistance;

        if (source.rolloffMode == AudioRolloffMode.Custom)
        {
            AnimationCurve? curve = ShadowCurveCache.For(source).Rolloff;
            if (curve == null) return 0f;
            float t = max > 0f ? Mathf.Clamp01(distance / max) : 0f;
            return Mathf.Clamp01(curve.Evaluate(t));
        }

        float min = Mathf.Max(0.0001f, source.minDistance);
        if (distance <= min) return 1f;

        if (source.rolloffMode == AudioRolloffMode.Linear)
        {
            return distance >= max ? 0f : Mathf.Clamp01(Mathf.InverseLerp(max, min, distance));
        }

        return Mathf.Clamp01(min / (min + (Constants.Audio.ShadowRolloffScale * (distance - min))));
    }
}
