using System;
using System.Runtime.InteropServices;

namespace Operator.Recording;

internal static class FfmpegJobObject
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private static readonly object Gate = new();
    private static bool _initialized;
    private static IntPtr _handle = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    public static void Add(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr job = GetJob();
            if (job == IntPtr.Zero)
            {
                return;
            }

            if (!AssignProcessToJobObject(job, processHandle))
            {
                Plugin.Log.LogDebug($"Could not assign ffmpeg to job object (win32 {Marshal.GetLastWin32Error()}).");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Job object assignment skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IntPtr GetJob()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return _handle;
            }
            _initialized = true;

            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    Plugin.Log.LogDebug($"CreateJobObject failed (win32 {Marshal.GetLastWin32Error()}).");
                    return IntPtr.Zero;
                }

                var info = new JobObjectExtendedLimitInformationData();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

                int size = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformationData));
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
                    {
                        Plugin.Log.LogDebug($"SetInformationJobObject failed (win32 {Marshal.GetLastWin32Error()}).");
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                _handle = job;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Job object unavailable: {ex.GetType().Name}: {ex.Message}");
                _handle = IntPtr.Zero;
            }

            return _handle;
        }
    }
}
