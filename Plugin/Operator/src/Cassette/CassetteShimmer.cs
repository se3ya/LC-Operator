using UnityEngine;

namespace Operator.Cassette;

internal sealed class CassetteShimmer : MonoBehaviour
{
    private Renderer? _renderer;
    private bool _misprint;
    private float _phase;

    internal void Init(bool misprint, float phase)
    {
        _renderer = GetComponent<Renderer>();
        _misprint = misprint;
        _phase = phase;
        enabled = true;
    }

    private void Update()
    {
        if (_renderer == null || !_renderer.isVisible) return;

        float hue = Mathf.Repeat(_phase + Time.time * Constants.Cassette.HoloShimmerSpeed, 1f);
        Color tint = Color.HSVToRGB(hue, Constants.Cassette.HoloShimmerSaturation, 1f);
        CassetteShellColor.SetBand(_renderer, CassetteTier.Holo, _misprint, tint);
    }
}
