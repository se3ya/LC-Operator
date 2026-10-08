using UnityEngine;

namespace Operator.Cassette;

internal sealed class PlaybackGain : MonoBehaviour
{
    private float _envelope;
    private float _release;

    private void Awake()
    {
        _release = Mathf.Exp(-1f / (Constants.Television.LimiterRelease * AudioSettings.outputSampleRate));
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float gain = Constants.Television.PlaybackGain;
        float ceiling = Constants.Television.LimiterCeiling;
        for (int i = 0; i < data.Length; i += channels)
        {
            float peak = 0f;
            for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[i + c] * gain));
            _envelope = peak > _envelope ? peak : (_envelope * _release) + (peak * (1f - _release));
            float scale = _envelope > ceiling ? gain * ceiling / _envelope : gain;
            for (int c = 0; c < channels; c++) data[i + c] *= scale;
        }
    }
}
