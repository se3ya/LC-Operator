using System.Collections.Generic;
using Dissonance.Audio.Playback;
using GameNetcodeStuff;
using Operator.Recording.Voice;
using UnityEngine;

namespace Operator.Recording;

internal sealed class ShadowVoiceChat
{
    private static bool _refreshing;
    private static int _epoch;

    private readonly List<RemoteVoiceStream> _streams = new();
    private int _seenEpoch = -1;
    private float _nextScan;
    private int _reported = -1;
    private int _others;
    private bool _cameraInside;

    internal void Begin()
    {
        _nextScan = 0f;
        _seenEpoch = _epoch;
        Rebuild();
    }

    internal static void OnVoicesRefreshed()
    {
        if (_refreshing) return;
        _epoch++;
    }

    private static void Rearm(RemoteVoiceStream stream)
    {
        stream.Generation++;
        stream.Occluded = false;
        stream.OccludeCheckAt = 0f;
        stream.Openness = 1f;
        stream.CutoffGlide = Constants.Audio.ShadowOccludeBypassCutoff;
        stream.Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
        stream.ShelfGain = 0f;
        stream.Gate = 0f;
    }

    private bool Stale()
    {
        for (int i = 0; i < _streams.Count; i++)
        {
            RemoteVoiceStream stream = _streams[i];
            PlayerControllerB? player = stream.Player;
            if (player == null) return true;
            if (stream.Source == null || stream.Playback == null) return true;
            if (stream.Source != player.currentVoiceChatAudioSource) return true;
        }

        return false;
    }

    internal void Release()
    {
        _streams.Clear();
        RemoteVoices.Clear();
        _reported = -1;
    }

    internal void Update(Transform camera, bool cameraInside)
    {
        if (camera == null)
        {
            Release();
            return;
        }

        _cameraInside = cameraInside;

        if (_seenEpoch != _epoch || Stale() || Time.realtimeSinceStartup >= _nextScan)
        {
            _seenEpoch = _epoch;
            _nextScan = Time.realtimeSinceStartup + Constants.Audio.ShadowVoiceScanInterval;
            Rebuild();
        }

        if (_streams.Count == 0) return;

        Vector3 origin = camera.position;
        Vector3 right = camera.right;
        Vector3 forward = camera.forward;

        for (int i = 0; i < _streams.Count; i++)
        {
            RemoteVoiceStream stream = _streams[i];
            AudioSource source = stream.Source;
            PlayerControllerB? speaker = stream.Player;
            if (source == null || speaker == null) continue;

            float gate = Mathf.MoveTowards(
                stream.Gate,
                VoiceFalloff.CanHear(speaker, _cameraInside) ? 1f : 0f,
                Time.deltaTime / Constants.Audio.VoiceGateTime);
            stream.Gate = gate;
            if (gate <= 0f)
            {
                stream.GainFlat = 0f;
                stream.GainLeft = 0f;
                stream.GainRight = 0f;
                stream.SendGain = 0f;
                continue;
            }

            Vector3 mouth = VoiceFalloff.MouthOf(speaker);
            float distance = Vector3.Distance(mouth, origin);
            VoiceFalloff.GetLevels(distance, _cameraInside, source, out float reach, out float room);

            ShadowAudio.GetGains(
                source,
                mouth,
                origin,
                right,
                forward,
                Constants.Audio.ShadowVoiceLevel,
                1,
                1f,
                out float flat,
                out float left,
                out float rightGain,
                reach);

            float weight = DirectivityWeight(speaker, mouth, origin);
            float facing = Mathf.Pow(10f, Constants.Audio.ShadowVoiceDirectivityLow * weight / 20f);

            stream.ShelfGain = (Constants.Audio.ShadowVoiceDirectivityHigh
                                    - Constants.Audio.ShadowVoiceDirectivityLow) * weight;

            float blocked = Mathf.Lerp(Constants.Audio.ShadowOccludeGain, 1f, stream.Openness);

            stream.GainFlat = flat * facing * blocked * gate;
            stream.GainLeft = left * facing * blocked * gate;
            stream.GainRight = rightGain * facing * blocked * gate;
            stream.SendGain = Constants.Audio.ShadowVoiceLevel
                * ShadowAudio.GroupGain(source)
                * room
                * gate;
            stream.Compensation = CompensationFor(speaker);
            stream.Cutoff = CutoffFor(stream, speaker, source, mouth, origin);

            float echoTarget = EchoFor(distance);
            if (echoTarget > 0f && stream.Echo == null)
            {
                int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                stream.Echo = new float[Mathf.Max(1, Mathf.RoundToInt(Constants.Audio.ShadowVoiceEchoDelay * rate))];
            }

            float echoSpan = echoTarget > stream.EchoMix
                ? Constants.Audio.ShadowVoiceEchoRise
                : Constants.Audio.ShadowVoiceEchoFall;
            stream.EchoMix = Mathf.MoveTowards(stream.EchoMix, echoTarget, Time.deltaTime / Mathf.Max(0.01f, echoSpan));
        }
    }

    private static float CutoffFor(RemoteVoiceStream stream, PlayerControllerB speaker, AudioSource source, Vector3 mouth, Vector3 origin)
    {
        stream.CutoffGlide = ShadowAudio.GetOcclusionCutoff(
            mouth,
            origin,
            source.maxDistance,
            stream.CutoffGlide,
            ref stream.Occluded,
            ref stream.OccludeCheckAt,
            ref stream.Openness);

        float carried = CarriedMuffle(speaker);
        return carried < stream.CutoffGlide ? carried : stream.CutoffGlide;
    }

    private static float EchoFor(float distance)
    {
        return distance > Constants.Audio.ShadowVoiceEchoDistance ? 1f : 0f;
    }

    private static float CarriedMuffle(PlayerControllerB speaker)
    {
        if (EnemyVoiceMuffle.IsFleaMuffled(speaker)) return Constants.Audio.ShadowVoiceFleaCutoff;
        if (speaker.isUnderwater) return Constants.Audio.ShadowVoiceUnderwaterCutoff;
        if (speaker.voiceMuffledByEnemy) return Constants.Audio.ShadowVoiceMaskCutoff;
        return Constants.Audio.ShadowOccludeBypassCutoff;
    }

    internal static float DirectivityWeight(PlayerControllerB speaker, Vector3 mouth, Vector3 origin)
    {
        Transform? eye = speaker.playerEye;
        Vector3 facing = eye != null ? eye.forward : speaker.transform.forward;

        Vector3 toCamera = origin - mouth;
        float length = toCamera.magnitude;
        if (length < 0.01f) return 0f;

        float cosine = Vector3.Dot(facing.normalized, toCamera / length);
        float weight = (1f - cosine) * 0.5f;
        return weight * weight;
    }

    private static float CompensationFor(PlayerControllerB speaker)
    {
        try
        {
            float applied = speaker.voicePlayerState != null ? speaker.voicePlayerState.Volume : 1f;
            return applied > 0.01f ? 1f / applied : 1f;
        }
        catch
        {
            return 1f;
        }
    }

    private void Rebuild()
    {
        StartOfRound round = StartOfRound.Instance;
        GameNetworkManager network = GameNetworkManager.Instance;
        if (round.allPlayerScripts == null)
        {
            if (_streams.Count > 0) Publish(true);
            return;
        }

        PlayerControllerB? local = network.localPlayerController;
        bool changed = false;
        int found = 0;
        int others = 0;
        bool refreshed = false;

        for (int i = 0; i < round.allPlayerScripts.Length; i++)
        {
            PlayerControllerB player = round.allPlayerScripts[i];
            if (player == null || player == local) continue;
            if (!player.isPlayerControlled) continue;
            if (player.isPlayerDead) continue;

            others++;

            AudioSource? source = player.currentVoiceChatAudioSource;
            if (source == null && !refreshed)
            {
                refreshed = true;
                _refreshing = true;
                try
                {
                    round.RefreshPlayerVoicePlaybackObjects();
                }
                finally
                {
                    _refreshing = false;
                }

                source = player.currentVoiceChatAudioSource;
            }

            if (source == null) continue;

            SamplePlaybackComponent? playback = source.GetComponent<SamplePlaybackComponent>();
            if (playback == null) continue;

            if (found < _streams.Count)
            {
                RemoteVoiceStream existing = _streams[found];
                if (existing.Playback != playback || existing.Source != source || existing.Player != player)
                {
                    existing.Playback = playback;
                    existing.Source = source;
                    existing.Player = player;
                    existing.PendingFrames = 0;
                    Rearm(existing);
                    changed = true;
                }
            }
            else
            {
                var added = new RemoteVoiceStream { Playback = playback, Source = source, Player = player };
                Rearm(added);
                _streams.Add(added);
                changed = true;
            }

            found++;
        }

        if (found < _streams.Count)
        {
            _streams.RemoveRange(found, _streams.Count - found);
            changed = true;
        }

        _others = others;
        if (changed) Publish(false);
        else if (_reported != _streams.Count) Publish(false);
    }

    private void Publish(bool empty)
    {
        if (empty)
        {
            _streams.Clear();
            RemoteVoices.Clear();
        }
        else
        {
            RemoteVoices.Publish(_streams.ToArray());
        }

        if (_streams.Count == _reported) return;

        _reported = _streams.Count;
        Plugin.Log.LogDebug(_streams.Count == 0
            ? $"No voices for the camera to record, {_others} other players in the lobby"
            : $"Recording {_streams.Count} of {_others} other voices at the camera");
    }
}
