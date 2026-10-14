using System;

namespace Operator.Cassette;

internal static class Letterbox
{
    internal static (float Width, float Height) Fit(float videoWidth, float videoHeight, float boxWidth, float boxHeight)
    {
        if (videoWidth <= 0f || videoHeight <= 0f) return (boxWidth, boxHeight);

        float scale = Math.Min(boxWidth / videoWidth, boxHeight / videoHeight);
        return (videoWidth * scale, videoHeight * scale);
    }

    internal static (int Width, int Height) PickSize(int videoWidth, int videoHeight, int maxWidth, int maxHeight, int fallbackWidth, int fallbackHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0) return (fallbackWidth, fallbackHeight);

        float scale = Math.Min(1f, Math.Min((float)maxWidth / videoWidth, (float)maxHeight / videoHeight));
        return (RoundEven(videoWidth * scale), RoundEven(videoHeight * scale));
    }

    private static int RoundEven(float value)
    {
        return Math.Max(2, (int)Math.Round(value / 2f) * 2);
    }
}
