using System;
using System.IO;

namespace Operator.Recording;

internal static class ClipName
{
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static bool IsSafe(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length > 160) return false;
        if (fileName != Path.GetFileName(fileName)) return false;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (fileName.TrimStart('.').Length == 0) return false;
        if (!fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return false;

        return !IsDeviceName(Path.GetFileNameWithoutExtension(fileName));
    }

    internal static bool IsDeviceName(string stem)
    {
        for (int i = 0; i < ReservedDeviceNames.Length; i++)
        {
            if (string.Equals(stem, ReservedDeviceNames[i], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
