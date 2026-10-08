using UnityEngine;

namespace Operator.Recording;

internal struct LowPass
{
    private float _b0;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _leftOne;
    private float _leftTwo;
    private float _rightOne;
    private float _rightTwo;

    // two of these in series with q 0.5412 then 1.3066 is a fourth order
    // two numbers are 1/(2cos(pi/8)) and 1/(2cos(3pi/8)) and no you cannot just use 0.707 twice
    internal void Set(float frequency, float rate, float q)
    {
        float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 20f, rate * 0.45f) / rate;
        float cos = Mathf.Cos(w);
        float alpha = Mathf.Sin(w) / (2f * q);
        float a0 = 1f + alpha;
        float b0 = (1f - cos) * 0.5f;
        _b0 = b0 / a0;
        _b1 = (1f - cos) / a0;
        _b2 = b0 / a0;
        _a1 = (-2f * cos) / a0;
        _a2 = (1f - alpha) / a0;
    }

    internal void Reset()
    {
        _leftOne = 0f;
        _leftTwo = 0f;
        _rightOne = 0f;
        _rightTwo = 0f;
    }

    internal float Left(float x)
    {
        float y = (_b0 * x) + _leftOne;
        _leftOne = (_b1 * x) - (_a1 * y) + _leftTwo;
        _leftTwo = (_b2 * x) - (_a2 * y);
        return y;
    }

    internal float Right(float x)
    {
        float y = (_b0 * x) + _rightOne;
        _rightOne = (_b1 * x) - (_a1 * y) + _rightTwo;
        _rightTwo = (_b2 * x) - (_a2 * y);
        return y;
    }
}
