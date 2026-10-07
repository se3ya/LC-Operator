using System;
using Dissonance.Audio.Playback;
using HarmonyLib;
using UnityEngine;

namespace Operator.Recording.Voice;

internal sealed class RemoteVoiceStream
{
    internal volatile SamplePlaybackComponent Playback = null!;
    internal AudioSource Source = null!;
    internal GameNetcodeStuff.PlayerControllerB? Player;
    internal float[] Pending = new float[Mathf.Max(4096, AudioSettings.GetConfiguration().dspBufferSize * 4)];
    internal volatile int PendingFrames;
    internal volatile float GainFlat;
    internal volatile float GainLeft;
    internal volatile float GainRight;
    internal volatile float Compensation = 1f;
    internal volatile float SendGain;
    internal volatile float Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
    internal volatile float ShelfGain;
    internal volatile float EchoMix;
    internal volatile int Generation;

    internal bool Occluded;
    internal float OccludeCheckAt;
    internal float CutoffGlide = Constants.Audio.ShadowOccludeBypassCutoff;
    internal float Openness = 1f;
    internal float Gate;

    internal float[] Filtered = new float[Mathf.Max(4096, AudioSettings.GetConfiguration().dspBufferSize * 4)];
    internal Operator.Recording.LowPass BandOne;
    internal Operator.Recording.LowPass BandTwo;
    internal Operator.Recording.HighShelf Shelf;
    internal float[]? Echo;
    internal int EchoIndex;
    internal float BandAt;
    internal bool Quiet;
    internal float ShelfAt = float.NaN;
    internal int BandGeneration = -1;

    internal void ResetFilters()
    {
        if (Quiet) return;
        Quiet = true;

        BandOne.Reset();
        BandTwo.Reset();
        Shelf.Reset();
        BandAt = 0f;
        ShelfAt = float.NaN;

        if (Echo != null) Array.Clear(Echo, 0, Echo.Length);
        EchoIndex = 0;
    }
}

internal static class RemoteVoices
{
    private static RemoteVoiceStream[] _streams = Array.Empty<RemoteVoiceStream>();
    private static float _outputRate = 48000f;

    [ThreadStatic]
    private static SamplePlaybackComponent? _speaking;

    internal static void Publish(RemoteVoiceStream[] streams)
    {
        int output = AudioSettings.outputSampleRate;
        _outputRate = output > 0 ? output : 48000f;
        _streams = streams;
    }

    internal static void Clear() => _streams = Array.Empty<RemoteVoiceStream>();

    // dissonance Filter is static so postfix on it cannot tell which speaker it belongs to
    internal static void Enter(SamplePlaybackComponent playback) => _speaking = playback;

    internal static void Leave() => _speaking = null;

    internal static void Capture(float[] temp, int frames)
    {
        SamplePlaybackComponent? owner = _speaking;
        if (owner == null || frames <= 0) return;

        RemoteVoiceStream[] streams = _streams;
        for (int i = 0; i < streams.Length; i++)
        {
            RemoteVoiceStream stream = streams[i];
            if (stream.Playback != owner) continue;
            if (frames > stream.Pending.Length)
            {
                return;
            }

            Array.Copy(temp, stream.Pending, frames);
            stream.PendingFrames = frames;
            return;
        }
    }

    private static void ApplyEcho(RemoteVoiceStream stream, float[] buffer, int count, float mix)
    {
        float[]? line = stream.Echo;
        if (line == null) return;

        int length = line.Length;

        float dry = Mathf.Lerp(1f, Constants.Audio.ShadowVoiceEchoDry, mix);
        float wet = Constants.Audio.ShadowVoiceEchoWet * mix;
        float feedback = Constants.Audio.ShadowVoiceEchoFeedback;
        int index = stream.EchoIndex;

        for (int i = 0; i < count; i++)
        {
            float delayed = line[index];
            float input = buffer[i];
            line[index] = input + delayed * feedback;
            buffer[i] = input * dry + delayed * wet;
            if (++index >= length) index = 0;
        }

        stream.EchoIndex = index;
    }

    internal static void Discard()
    {
        RemoteVoiceStream[] streams = _streams;
        for (int i = 0; i < streams.Length; i++) streams[i].PendingFrames = 0;
    }

    internal static void Mix(float[] destination, int frames, int channels, float scale, ReverbBus? bus)
    {
        RemoteVoiceStream[] streams = _streams;
        if (streams.Length == 0 || channels < 1) return;

        float rate = _outputRate;

        for (int i = 0; i < streams.Length; i++)
        {
            RemoteVoiceStream stream = streams[i];

            int generation = stream.Generation;
            if (stream.BandGeneration != generation)
            {
                stream.BandGeneration = generation;
                stream.Quiet = false;
                stream.ResetFilters();
            }

            int ready = stream.PendingFrames;
            if (ready <= 0)
            {
                stream.ResetFilters();
                continue;
            }

            stream.PendingFrames = 0;
            stream.Quiet = false;
            if (scale <= 0f) continue;

            float compensation = stream.Compensation;
            float flat = stream.GainFlat * scale * compensation;
            float left = stream.GainLeft * scale * compensation;
            float right = stream.GainRight * scale * compensation;

            int count = ready < frames ? ready : frames;
            float[] pending = stream.Pending;
            float[] filtered = stream.Filtered;
            if (filtered.Length < count) continue;

            float cutoff = stream.Cutoff;
            bool muffled = cutoff < Constants.Audio.ShadowOccludeBypassCutoff;
            if (muffled && stream.BandAt != cutoff)
            {
                stream.BandOne.Set(cutoff, rate, 0.54119610f);
                stream.BandTwo.Set(cutoff, rate, 1.30656296f);
                stream.BandAt = cutoff;
            }

            if (muffled)
            {
                for (int f = 0; f < count; f++)
                {
                    filtered[f] = stream.BandTwo.Left(stream.BandOne.Left(pending[f]));
                }
            }
            else
            {
                Array.Copy(pending, filtered, count);
            }

            bus?.Send(filtered, count, stream.SendGain * scale * compensation);

            float echoMix = stream.EchoMix;
            if (echoMix > 0.001f) ApplyEcho(stream, filtered, count, echoMix);

            if (flat <= 0f && left <= 0f && right <= 0f) continue;

            float shelf = stream.ShelfGain;
            bool turned = shelf < -0.05f;
            if (turned && stream.ShelfAt != shelf)
            {
                stream.Shelf.Set(Constants.Audio.ShadowVoiceDirectivityCorner, rate, shelf);
                stream.ShelfAt = shelf;
            }

            if (channels >= 2)
            {
                for (int f = 0, o = 0; f < count; f++, o += channels)
                {
                    float voice = turned ? stream.Shelf.Process(filtered[f]) : filtered[f];
                    destination[o] += voice * (flat + left);
                    destination[o + 1] += voice * (flat + right);
                }

                continue;
            }

            for (int f = 0; f < count; f++)
            {
                float voice = turned ? stream.Shelf.Process(filtered[f]) : filtered[f];
                destination[f] += voice * (flat + (left + right) * 0.5f);
            }
        }
    }
}

[HarmonyPatch(typeof(SamplePlaybackComponent))]
internal static class SamplePlaybackPatch
{
    [HarmonyPatch(nameof(SamplePlaybackComponent.OnAudioFilterRead))]
    [HarmonyPrefix]
    private static void MarkSpeaker(SamplePlaybackComponent __instance) => RemoteVoices.Enter(__instance);

    [HarmonyPatch(nameof(SamplePlaybackComponent.OnAudioFilterRead))]
    [HarmonyFinalizer]
    private static void ClearSpeaker() => RemoteVoices.Leave();

    [HarmonyPatch(nameof(SamplePlaybackComponent.Filter))]
    [HarmonyPostfix]
    private static void TakeVoice(float[] output, int channels, float[] temp)
    {
        if (output == null || temp == null || channels < 1) return;
        RemoteVoices.Capture(temp, output.Length / channels);
    }
}
