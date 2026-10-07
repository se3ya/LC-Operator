using System;
using GameNetcodeStuff;
using UnityEngine;

namespace Operator.Recording.Voice;

internal sealed class VoiceChain : IDisposable
{
    private const float WalkieHighpass = 1131f;
    private const float WalkieHighpassQ = 7f;

    private readonly int _dspRate;
    private readonly MicResampler _resampler = new();
    private readonly Action<float[], int> _onProcessed;
    private readonly Transform? _cameraTransform;
    private float[] _resampleBuf = new float[4096];
    private GameObject? _go;
    private MicInjector? _injector;
    private VoiceChainController? _controller;
    private volatile bool _stopped;

    public VoiceChain(int dspSampleRate, int dspChannels, Transform? cameraTransform, Action<float[], int> onProcessed)
    {
        _dspRate = dspSampleRate;
        _cameraTransform = cameraTransform;
        _onProcessed = onProcessed;
        Build(dspSampleRate);
    }

    public void Stop() => _stopped = true;

    public void SetRoom(bool inside) => _controller?.SetRoom(inside);

    public void SetSubmerged(bool submerged) => _controller?.SetSubmerged(submerged);

    public void SetHeld(bool held) => _controller?.SetHeld(held);

    public void EnqueueMic(ArraySegment<float> mono, int micRate)
    {
        if (mono.Array == null || mono.Count == 0)
        {
            return;
        }
        int needed = (int)((long)mono.Count * _dspRate / Math.Max(1, micRate)) + 8;
        if (_resampleBuf.Length < needed)
        {
            _resampleBuf = new float[needed];
        }
        int n = _resampler.Process(mono.Array, mono.Offset, mono.Count, micRate, _dspRate, _resampleBuf);
        _injector?.Add(_resampleBuf, n);
    }

    private void Build(int dspRate)
    {
        _go = new GameObject("OperatorVoiceChain") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(_go);

        AudioSource src = _go.AddComponent<AudioSource>();
        src.clip = MakeSilentClip(dspRate);
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 1f;
        src.priority = 0;
        src.playOnAwake = false;
        src.Play();

        _injector = _go.AddComponent<MicInjector>();

        AudioLowPassFilter lp = _go.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = 20000f;
        lp.lowpassResonanceQ = 1f;

        AudioHighPassFilter hp = _go.AddComponent<AudioHighPassFilter>();
        hp.cutoffFrequency = WalkieHighpass;
        hp.highpassResonanceQ = WalkieHighpassQ;
        hp.enabled = false;

        AudioReverbFilter rv = _go.AddComponent<AudioReverbFilter>();
        ConfigureReverb(rv);
        rv.enabled = false;

        PitchFilter pitch = _go.AddComponent<PitchFilter>();
        pitch.Configure();

        MicCapture capture = _go.AddComponent<MicCapture>();
        capture.Configure((buf, ch) => { if (!_stopped) _onProcessed(buf, ch); }, dspRate);

        _controller = _go.AddComponent<VoiceChainController>();
        _controller.Configure(lp, hp, rv, pitch, capture, _cameraTransform);
    }

    private static void ConfigureReverb(AudioReverbFilter rv)
    {
        rv.reverbPreset = AudioReverbPreset.User;
        rv.dryLevel = -1f;
        rv.decayTime = 0.8f;
        rv.room = -2300f;
    }

    private static AudioClip MakeSilentClip(int sampleRate)
    {
        int len = Mathf.Max(256, sampleRate / 10);
        AudioClip clip = AudioClip.Create("OperatorVoiceSilent", len, 1, sampleRate, false);
        clip.SetData(new float[len], 0);
        return clip;
    }

    public void Dispose()
    {
        if (_go == null)
        {
            return;
        }
        _injector = null;
        _controller = null;
        AudioSource? src = _go.GetComponent<AudioSource>();
        if (src != null)
        {
            src.Stop();
        }
        UnityEngine.Object.Destroy(_go);
        _go = null;
    }
}

internal sealed class PitchFilter : MonoBehaviour
{
    private PitchShifter? _shifter;
    private float _ratio = 1f;

    public void Configure() => _shifter = new PitchShifter();

    public void SetRatio(float ratio) => _ratio = ratio;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        _shifter?.Process(data, channels, _ratio);
    }
}

internal sealed class VoiceChainController : MonoBehaviour
{
    private AudioLowPassFilter? _lp;
    private AudioHighPassFilter? _hp;
    private AudioReverbFilter? _rv;
    private PitchFilter? _pitch;
    private MicCapture? _capture;
    private Transform? _camera;
    private float _cutoff = 20000f;
    private float _q = 1f;
    private float _dry = 1f;
    private float _room;
    private float _shelf;
    private float _panLeft = 1f;
    private float _panRight = 1f;
    private float _occludeCutoff = Constants.Audio.ShadowOccludeBypassCutoff;
    private float _occludeCheckAt;
    private float _openness = 1f;
    private bool _occluded;
    private bool _inside;
    private bool _submerged;
    private bool _held;

    public void Configure(AudioLowPassFilter lp, AudioHighPassFilter hp, AudioReverbFilter rv, PitchFilter pitch, MicCapture capture, Transform? camera)
    {
        _lp = lp;
        _hp = hp;
        _rv = rv;
        _pitch = pitch;
        _capture = capture;
        _camera = camera;
    }

    public void SetRoom(bool inside) => _inside = inside;

    public void SetSubmerged(bool submerged) => _submerged = submerged;

    public void SetHeld(bool held) => _held = held;

    private void ResetOcclusion()
    {
        _occluded = false;
        _occludeCheckAt = 0f;
        _openness = 1f;
        _occludeCutoff = Constants.Audio.ShadowOccludeBypassCutoff;
    }

    private void Update()
    {
        PlayerControllerB? local = GameNetworkManager.Instance != null
            ? GameNetworkManager.Instance.localPlayerController
            : null;
        bool inside = _held && local != null ? local.isInsideFactory : _inside;
        VoiceTargets t = VoiceTargets.Get(local, inside);

        float dry = 0f;
        float room = 0f;
        float shelf = 0f;
        float left = 1f;
        float right = 1f;
        float cutoff = t.LowpassCutoff;

        if (local != null && _camera != null && VoiceFalloff.CanHear(local, inside))
        {
            Vector3 mouth = VoiceFalloff.MouthOf(local);
            Vector3 lens = _camera.position;
            float distance = Vector3.Distance(mouth, lens);

            float weight = _held ? 0f : ShadowVoiceChat.DirectivityWeight(local, mouth, lens);
            float facing = Mathf.Pow(10f, Constants.Audio.ShadowVoiceDirectivityLow * weight / 20f);
            shelf = (Constants.Audio.ShadowVoiceDirectivityHigh - Constants.Audio.ShadowVoiceDirectivityLow) * weight;

            float blocked = 1f;
            if (_held)
            {
                ResetOcclusion();
            }
            else
            {
                _occludeCutoff = ShadowAudio.GetOcclusionCutoff(
                    mouth,
                    lens,
                    Constants.Audio.VoiceRange,
                    _occludeCutoff,
                    ref _occluded,
                    ref _occludeCheckAt,
                    ref _openness);
                blocked = Mathf.Lerp(Constants.Audio.ShadowOccludeGain, 1f, _openness);
                VoiceFalloff.GetPan(mouth, lens, _camera.right, _camera.forward, out left, out right);
            }

            cutoff = Mathf.Min(cutoff, _occludeCutoff);
            if (_held)
            {
                dry = 1f;
                room = VoiceFalloff.HeldRoom(inside);
            }
            else
            {
                VoiceFalloff.GetLevels(distance, inside, null, out float direct, out float tail);
                dry = direct * facing * blocked;
                room = tail;
            }
        }

        float k = Mathf.Clamp01(Time.deltaTime * 8f);
        _cutoff = Mathf.Lerp(_cutoff, cutoff, k);
        _q = Mathf.Lerp(_q, t.LowpassQ, k);

        if (_lp != null)
        {
            _lp.cutoffFrequency = _cutoff;
            _lp.lowpassResonanceQ = _q;
        }
        if (_hp != null) _hp.enabled = t.HighpassOn;
        if (_rv != null) _rv.enabled = t.ReverbOn;
        _pitch?.SetRatio(t.PitchRatio);

        _dry = Mathf.Lerp(_dry, dry, k);
        _room = Mathf.Lerp(_room, room, k);
        _shelf = Mathf.Lerp(_shelf, shelf, k);
        _panLeft = Mathf.Lerp(_panLeft, left, k);
        _panRight = Mathf.Lerp(_panRight, right, k);

        if (_capture != null)
        {
            _capture.SetGains(_dry, _room, _shelf, _panLeft, _panRight);
            _capture.SetRoom(inside);
            _capture.SetSubmerged(_submerged);
        }
    }
}
