using System;

namespace Operator.Cassette;

internal static class CardLayout
{
    internal const int GlyphPitch = 6;
    internal const int LinePitch = 8;

    internal static int FitScale(string[] lines, int targetWidth, int targetHeight, float widthShare, float heightShare)
    {
        int longest = 0;
        foreach (string line in lines) longest = Math.Max(longest, line.Length);
        if (longest == 0) return 1;

        float byWidth = targetWidth * widthShare / ((longest * GlyphPitch) - 1);
        float byHeight = targetHeight * heightShare / (lines.Length * LinePitch);
        return Math.Max(1, (int)Math.Floor(Math.Min(byWidth, byHeight)));
    }

    internal static float LineWidth(string line, float scale)
    {
        return line.Length > 0 ? ((line.Length * GlyphPitch) - 1f) * scale : 0f;
    }

    internal static (float X, float Y) LineOrigin(string line, int index, int lineCount, float scale, float targetWidth, float targetHeight)
    {
        float x = (targetWidth - LineWidth(line, scale)) / 2f;
        float y = ((targetHeight - (lineCount * LinePitch * scale)) / 2f) + (index * LinePitch * scale);
        return (x, y);
    }
}
