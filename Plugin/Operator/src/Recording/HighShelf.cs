using UnityEngine;

namespace Operator.Recording;

internal struct HighShelf
{
    private float _b0;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _one;
    private float _two;

    // robert bristow johnsons shelf
    internal void Set(float frequency, float rate, float decibels)
    {
        float a = Mathf.Pow(10f, decibels / 40f);
        float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 20f, rate * 0.45f) / rate;
        float cos = Mathf.Cos(w);
        float alpha = Mathf.Sin(w) * 0.70710678f;
        float shaped = 2f * Mathf.Sqrt(a) * alpha;

        float a0 = a + 1f - ((a - 1f) * cos) + shaped;
        _b0 = a * (a + 1f + ((a - 1f) * cos) + shaped) / a0;
        _b1 = -2f * a * (a - 1f + ((a + 1f) * cos)) / a0;
        _b2 = a * (a + 1f + ((a - 1f) * cos) - shaped) / a0;
        _a1 = 2f * (a - 1f - ((a + 1f) * cos)) / a0;
        _a2 = (a + 1f - ((a - 1f) * cos) - shaped) / a0;
    }

    internal void Reset()
    {
        _one = 0f;
        _two = 0f;
    }

    internal float Process(float x)
    {
        float y = (_b0 * x) + _one;
        _one = (_b1 * x) - (_a1 * y) + _two;
        _two = (_b2 * x) - (_a2 * y);
        return y;
    }
}
