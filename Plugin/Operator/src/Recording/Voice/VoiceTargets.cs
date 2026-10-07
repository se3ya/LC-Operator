using GameNetcodeStuff;
using UnityEngine;

namespace Operator.Recording.Voice;

internal struct VoiceTargets
{
    private const float WalkieLowpass = 4000f;
    private const float WalkieLowpassQ = 3f;


    public float LowpassCutoff;
    public float LowpassQ;
    public bool HighpassOn;
    public bool ReverbOn;
    public float PitchRatio;

    public static VoiceTargets Clear => new()
    {
        LowpassCutoff = 20000f,
        LowpassQ = 1f,
        HighpassOn = false,
        ReverbOn = false,
        PitchRatio = 1f,
    };

    public static VoiceTargets Get(PlayerControllerB? local, bool inside)
    {
        VoiceTargets t = Clear;
        if (local == null)
        {
            return t;
        }

        if (EnemyVoiceMuffle.IsLocalFleaMuffled(local))
        {
            t.LowpassCutoff = 500f;
            t.LowpassQ = 5f;
            AudioSource? src = local.currentVoiceChatAudioSource;
            if (src != null)
            {
                OccludeAudio? occ = src.GetComponent<OccludeAudio>();
                if (occ != null && occ.overridingLowPass)
                {
                    t.LowpassCutoff = occ.lowPassOverride;
                }
                AudioLowPassFilter? lp = src.GetComponent<AudioLowPassFilter>();
                if (lp != null)
                {
                    t.LowpassQ = lp.lowpassResonanceQ;
                }
            }
        }
        else if (local.isUnderwater)
        {
            t.LowpassCutoff = 600f;
            t.LowpassQ = 1f;
        }

        if (local.speakingToWalkieTalkie)
        {
            t.HighpassOn = true;
            t.LowpassCutoff = Mathf.Min(t.LowpassCutoff, WalkieLowpass);
            t.LowpassQ = WalkieLowpassQ;
        }

        t.ReverbOn = inside;
        t.PitchRatio = GetPitch(local);
        return t;
    }

    private static float GetPitch(PlayerControllerB local)
    {
        SoundManager sm = SoundManager.Instance;
        if (sm == null || sm.playerVoicePitches == null)
        {
            return 1f;
        }
        int idx = (int)local.playerClientId;
        if (idx < 0 || idx >= sm.playerVoicePitches.Length)
        {
            return 1f;
        }
        return Mathf.Clamp(sm.playerVoicePitches[idx], 0.5f, 2f);
    }
}
