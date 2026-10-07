using System;
using System.Runtime.InteropServices;

namespace Operator.Recording;

internal static class Wine
{
    private static readonly Lazy<string?> Found = new(Detect);

    public static bool IsRunning => Found.Value != null;

    public static string Version => Found.Value ?? "";

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VersionCall();

    private static string? Detect()
    {
        IntPtr call;
        try
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll == IntPtr.Zero) return null;
            call = GetProcAddress(ntdll, "wine_get_version");
            if (call == IntPtr.Zero) return null;
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            VersionCall version = Marshal.GetDelegateForFunctionPointer<VersionCall>(call);
            return Marshal.PtrToStringAnsi(version()) ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
