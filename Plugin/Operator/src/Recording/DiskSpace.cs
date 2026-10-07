using System;
using System.IO;

namespace Operator.Recording;

internal static class DiskSpace
{
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    private static bool _reported;

    internal static bool IsOutOfSpace(Exception exception) =>
        exception is IOException
        && (exception.HResult == ErrorDiskFull || exception.HResult == ErrorHandleDiskFull);

    internal static bool ReportIfOutOfSpace(Exception exception, string what)
    {
        if (!IsOutOfSpace(exception)) return false;

        if (!_reported)
        {
            _reported = true;
            Plugin.Log.LogError($"Drive is full, so {what} could not be written. Free up space to keep recording.");
        }
        else
        {
            Plugin.Log.LogDebug($"Drive is still full, {what} could not be written.");
        }

        return true;
    }

    internal static void ReportEncoderOutOfSpace()
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        Plugin.Log.LogError("Drive is full, so recording could not be written. Free up space to keep recording.");
    }

    internal static void ClearReport() => _reported = false;
}
