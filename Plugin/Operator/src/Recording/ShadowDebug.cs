using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Operator.Recording;

internal static class ShadowDebug
{
#if DEBUG
    private static readonly Dictionary<string, int> _oneShotDecisions = new();
    private static float _nextCensus;

    // debug only, flip from the debugger. a config bool cannot elide the argument
    // interpolations at the call sites the way Conditional does
    internal static bool Enabled = false;
#else
    internal const bool Enabled = false;
#endif

    [Conditional("DEBUG")]
    internal static void LogOneShot(string sourceName, string clipName, string decision)
    {
#if DEBUG
        if (!Enabled) return;

        string key = $"{sourceName} / {clipName} -> {decision}";
        _oneShotDecisions.TryGetValue(key, out int count);
        _oneShotDecisions[key] = count + 1;
#endif
    }

    internal static bool DueForCensus()
    {
#if DEBUG
        if (!Enabled) return false;
        if (Time.realtimeSinceStartup < _nextCensus) return false;

        _nextCensus = Time.realtimeSinceStartup + Constants.Audio.ShadowCensusInterval;
        return true;
#else
        return false;
#endif
    }

    [Conditional("DEBUG")]
    internal static void WriteCensus(AudioSource[] sources, Vector3 camera, Vector3 observer, List<string> decisions)
    {
#if DEBUG
        var report = new StringBuilder();
        report.AppendLine(
            $"sound census, {sources.Length} audio sources in the scene, "
            + $"{ShadowClipCache.Count} clips copied using {ShadowClipCache.Bytes / (1024 * 1024)} MB");

        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null || !source.isPlaying) continue;

            AudioClip? clip = Operator.Compat.SoundApiCompat.ClipOn(source);
            float toCamera = Vector3.Distance(source.transform.position, camera);
            float toObserver = Vector3.Distance(source.transform.position, observer);
            float reach = ShadowAudio.RolloffAt(source, toCamera);

            Transform? parent = source.transform.parent;
            report.Append("  ").Append(source.name)
                .Append(" under=").Append(parent != null ? parent.name : "nothing")
                .Append(" clip=").Append(clip != null ? clip.name : "none")
                .Append(clip != null ? $" {clip.length:0.00}s {clip.loadType}" : string.Empty)
                .Append(" blend=").Append(ShadowAudio.SpatialBlendAt(source, toCamera).ToString("0.00"))
                .Append(" spread=").Append(ShadowAudio.SpreadAt(source, toCamera).ToString("0"))
                .Append(" vol=").Append(source.volume.ToString("0.00"))
                .Append(source.mute ? " muted" : string.Empty)
                .Append(source.loop ? " loop" : string.Empty)
                .Append(" range=").Append(source.minDistance.ToString("0"))
                .Append("/").Append(source.maxDistance.ToString("0"))
                .Append(" ").Append(source.rolloffMode)
                .Append(" reach=").Append(reach.ToString("0.000"))
                .Append(" camera=").Append(toCamera.ToString("0.0"))
                .Append("m you=").Append(toObserver.ToString("0.0"))
                .Append("m ").Append(i < decisions.Count ? decisions[i] : "?")
                .AppendLine();
        }

        if (_oneShotDecisions.Count > 0)
        {
            report.AppendLine("  one shots since the last census");
            foreach (KeyValuePair<string, int> entry in _oneShotDecisions)
            {
                report.Append("    ").Append(entry.Value).Append("x ").AppendLine(entry.Key);
            }

            _oneShotDecisions.Clear();
        }

        Plugin.Log.LogDebug(report.ToString().TrimEnd());
#endif
    }
}
