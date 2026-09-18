using System.Runtime.InteropServices;

namespace GoatClient.Core.Helpers;

/// <summary>Detects the physical system memory via Win32.</summary>
internal static class SystemMemoryInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>Total physical memory in MB, or null if it cannot be determined.</summary>
    public static long? GetTotalPhysicalMemoryMb()
    {
        try
        {
            if (GetPhysicallyInstalledSystemMemory(out var kb) && kb > 0)
            {
                return (long)(kb / 1024);
            }

            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.ullTotalPhys > 0)
            {
                return (long)(status.ullTotalPhys / 1024 / 1024);
            }
        }
        catch (Exception)
        {
            // Fall through to the managed fallback below.
        }

        var gcBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return gcBytes > 0 ? gcBytes / 1024 / 1024 : null;
    }
}
