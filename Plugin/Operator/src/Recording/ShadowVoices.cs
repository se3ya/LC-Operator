using UnityEngine;

namespace Operator.Recording;

internal sealed class ShadowVoicePool
{
    private sealed class Voice
    {
        public volatile bool Active;
        public volatile float GainFlat;
        public volatile float GainLeft;
        public volatile float GainRight;
        public volatile float Rate;
        public volatile int SyncFrame = -1;
        public volatile int SyncWrites;
        public volatile int Generation;
        public volatile bool Loop;
        public volatile float Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
        public ShadowClipCache.Pcm Pcm = null!;
        public double Position;
        public LowPass BandOne;
        public LowPass BandTwo;
        public float BandAt;
        public int SyncOwner;
        public int SeenSync = -1;
        public int SeenWrites;
        public float SyncAge;
        public bool Frozen;
    }

    private Voice[]? _pool;
    private int _starved;
    private float _outputRate = 48000f;

    internal bool Active { get; private set; }

    internal int TakeStarved()
    {
        int value = _starved;
        _starved = 0;
        return value;
    }

    internal void Open()
    {
        int output = AudioSettings.outputSampleRate;
        _outputRate = output > 0 ? output : 48000f;
        CreatePool();
        Active = true;
    }

    internal void Close()
    {
        Active = false;
        Voice[]? pool = _pool;
        if (pool == null) return;

        for (int i = 0; i < pool.Length; i++)
        {
            pool[i].Active = false;
            pool[i].Pcm = null!;
        }
    }

    private void CreatePool()
    {
        if (_pool != null) return;

        int size = Constants.Audio.ShadowVoiceCount;
        var pool = new Voice[size];
        for (int i = 0; i < size; i++) pool[i] = new Voice();
        _pool = pool;
    }

    internal long Claim(ShadowClipCache.Pcm pcm, bool loop, double startFrame, float rate)
    {
        if (!Active || pcm == null) return -1;
        CreatePool();

        Voice[] pool = _pool!;
        for (int i = 0; i < pool.Length; i++)
        {
            Voice voice = pool[i];
            if (voice.Active) continue;

            voice.Pcm = pcm;
            voice.Loop = loop;
            voice.Position = startFrame;
            voice.Rate = rate;
            voice.SyncFrame = -1;
            voice.GainFlat = 0f;
            voice.GainLeft = 0f;
            voice.GainRight = 0f;
            voice.Cutoff = Constants.Audio.ShadowOccludeBypassCutoff;
            voice.BandAt = 0f;
            voice.BandOne.Reset();
            voice.BandTwo.Reset();
            voice.Generation++;
            voice.Active = true;
            return MakeHandle(i, voice.Generation);
        }

        _starved++;
        return -1;
    }

    internal void Release(long handle)
    {
        Voice? voice = GetVoice(handle);
        if (voice == null) return;
        voice.Active = false;
    }

    internal bool IsLive(long handle) => GetVoice(handle) != null;

    internal void SetGain(long handle, float flat, float left, float right)
    {
        Voice? voice = GetVoice(handle);
        if (voice == null) return;
        voice.GainFlat = flat;
        voice.GainLeft = left;
        voice.GainRight = right;
    }

    internal void SetCutoff(long handle, float hertz)
    {
        Voice? voice = GetVoice(handle);
        if (voice == null) return;
        voice.Cutoff = hertz;
    }

    internal void SetRate(long handle, float rate)
    {
        Voice? voice = GetVoice(handle);
        if (voice == null) return;
        voice.Rate = rate;
    }

    internal void SetSyncFrame(long handle, int frame)
    {
        Voice? voice = GetVoice(handle);
        if (voice == null) return;
        voice.SyncFrame = frame;
        voice.SyncWrites++;
    }

    private static long MakeHandle(int index, int generation) => ((long)generation << 32) | (uint)index;

    private Voice? GetVoice(long handle)
    {
        if (handle < 0) return null;
        Voice[]? pool = _pool;
        if (pool == null) return null;

        int index = (int)(handle & 0xFFFFFFFFL);
        if (index < 0 || index >= pool.Length) return null;

        Voice voice = pool[index];
        if (!voice.Active || voice.Generation != (int)(handle >> 32)) return null;
        return voice;
    }

    internal void Mix(float[]? destination, int count, int channels, float scale)
    {
        Voice[]? pool = _pool;
        if (pool == null || count <= 0) return;

        int outChannels = channels > 0 ? channels : 1;
        int frames = count / outChannels;
        if (frames <= 0) return;

        bool audible = destination != null && scale > 0f;

        for (int i = 0; i < pool.Length; i++)
        {
            Voice voice = pool[i];
            if (!voice.Active) continue;

            if (audible) MixVoice(voice, destination!, frames, outChannels, scale, _outputRate);
            else Advance(voice, frames, _outputRate);
        }
    }

    private static void Advance(Voice voice, int frames, float outputRate)
    {
        int generation = voice.Generation;
        ShadowClipCache.Pcm pcm = voice.Pcm;
        if (pcm == null)
        {
            Retire(voice, generation);
            return;
        }

        double position = voice.Position;
        if (Follow(voice, generation, pcm.Frames, frames / outputRate, ref position)) position += (double)voice.Rate * frames;
        Step(voice, generation, pcm.Frames, position);
    }

    private static void Step(Voice voice, int generation, int sourceFrames, double position)
    {
        if (sourceFrames > 0)
        {
            if (voice.Loop)
            {
                position %= sourceFrames;
            }
            else if (position >= sourceFrames)
            {
                Retire(voice, generation);
                return;
            }
        }

        Commit(voice, generation, position);
    }

    private static void Commit(Voice voice, int generation, double position)
    {
        if (voice.Generation != generation || !voice.Active) return;
        voice.Position = position;
    }

    private static void Retire(Voice voice, int generation)
    {
        if (voice.Generation != generation || !voice.Active) return;
        voice.Position = 0d;
        voice.Active = false;
    }

    private static bool Follow(Voice voice, int generation, int sourceFrames, float elapsed, ref double position)
    {
        int writes = voice.SyncWrites;
        int sync = voice.SyncFrame;
        if (voice.SyncOwner != generation)
        {
            voice.SyncOwner = generation;
            voice.SeenSync = -1;
            voice.SeenWrites = writes;
            voice.SyncAge = 0f;
            voice.Frozen = false;
        }

        bool valid = sync >= 0 && sync < sourceFrames;
        bool changed = sync != voice.SeenSync;
        if (changed)
        {
            if (voice.Frozen && valid) position = sync;
            voice.SeenSync = sync;
            voice.SyncAge = 0f;
            voice.Frozen = false;
        }
        else if (valid && writes != voice.SeenWrites && voice.SyncAge >= Constants.Audio.ShadowFreezeDelay)
        {
            voice.Frozen = true;
        }

        voice.SeenWrites = writes;
        voice.SyncAge += elapsed;

        if (voice.Frozen)
        {
            position = sync;
            return false;
        }

        if (changed && valid)
        {
            double drift = position - sync;
            if (voice.Loop) drift -= sourceFrames * System.Math.Round(drift / sourceFrames);
            if (drift < 0d) drift = -drift;
            if (drift > Constants.Audio.ShadowResyncFrames) position = sync;
        }

        return true;
    }

    private static void MixVoice(Voice voice, float[] destination, int frames, int outChannels, float scale, float outputRate)
    {
        int generation = voice.Generation;
        ShadowClipCache.Pcm pcm = voice.Pcm;
        if (pcm == null)
        {
            Retire(voice, generation);
            return;
        }

        int sourceFrames = pcm.Frames;
        if (sourceFrames <= 0)
        {
            Retire(voice, generation);
            return;
        }

        bool loop = voice.Loop;
        float gainFlat = voice.GainFlat * scale;
        float gainLeft = voice.GainLeft * scale;
        float gainRight = voice.GainRight * scale;
        double rate = voice.Rate;
        double position = voice.Position;
        int sourceChannels = pcm.Channels;
        float[] samples = pcm.Samples;

        float cutoff = voice.Cutoff;
        bool muffled = cutoff < Constants.Audio.ShadowOccludeBypassCutoff;
        if (muffled && voice.BandAt != cutoff)
        {
            voice.BandOne.Set(cutoff, outputRate, 0.54119610f);
            voice.BandTwo.Set(cutoff, outputRate, 1.30656296f);
            voice.BandAt = cutoff;
        }

        if (!Follow(voice, generation, sourceFrames, frames / outputRate, ref position))
        {
            Commit(voice, generation, position);
            return;
        }

        if (gainFlat <= 0f && gainLeft <= 0f && gainRight <= 0f)
        {
            Step(voice, generation, sourceFrames, position + (rate * frames));
            return;
        }

        for (int f = 0; f < frames; f++)
        {
            if (position >= sourceFrames)
            {
                if (!loop)
                {
                    Retire(voice, generation);
                    return;
                }

                position %= sourceFrames;
            }

            int i0 = (int)position;
            if (i0 < 0) i0 = 0;
            int i1 = i0 + 1;
            if (i1 >= sourceFrames) i1 = loop ? 0 : i0;
            float frac = (float)(position - i0);

            float left;
            float right;
            float summed;
            if (sourceChannels >= 2)
            {
                int b0 = i0 * sourceChannels;
                int b1 = i1 * sourceChannels;
                left = samples[b0] + ((samples[b1] - samples[b0]) * frac);
                right = samples[b0 + 1] + ((samples[b1 + 1] - samples[b0 + 1]) * frac);
                summed = left + right;
            }
            else
            {
                float mono = samples[i0] + ((samples[i1] - samples[i0]) * frac);
                left = mono;
                right = mono;
                summed = mono;
            }

            int outIndex = f * outChannels;
            float outLeft = (left * gainFlat) + (summed * gainLeft);
            float outRight = (right * gainFlat) + (summed * gainRight);

            if (muffled)
            {
                outLeft = voice.BandTwo.Left(voice.BandOne.Left(outLeft));
                outRight = voice.BandTwo.Right(voice.BandOne.Right(outRight));
            }
            if (outChannels >= 2)
            {
                destination[outIndex] += outLeft;
                destination[outIndex + 1] += outRight;
            }
            else
            {
                destination[outIndex] += (outLeft + outRight) * 0.5f;
            }

            position += rate;
        }

        Commit(voice, generation, position);
    }
}
