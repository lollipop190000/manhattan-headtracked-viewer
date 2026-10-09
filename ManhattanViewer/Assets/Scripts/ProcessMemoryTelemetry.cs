using System;
using System.Runtime.InteropServices;

namespace Manhattan.Viewer
{
    /// <summary>OS working-set bytes, independent of Unity's allocation estimate.</summary>
    public static class ProcessMemoryTelemetry
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // SIZE_T is pointer-sized; this layout supports both Windows architectures.
        // https://learn.microsoft.com/windows/win32/api/psapi/ns-psapi-process_memory_counters
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryCounters
        {
            public uint size, pageFaultCount;
            public UIntPtr peakWorkingSet, workingSet, peakPagedPool, pagedPool;
            public UIntPtr peakNonPagedPool, nonPagedPool, pagefile, peakPagefile;
        }
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
#endif
        public static bool TryWorkingSetBytes(out long bytes)
        {
            bytes = 0;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var counters = new MemoryCounters { size = (uint)Marshal.SizeOf<MemoryCounters>() };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.size)) return false;
            ulong value = counters.workingSet.ToUInt64();
            if (value == 0 || value > long.MaxValue) return false;
            bytes = (long)value;
#else
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                process.Refresh(); bytes = process.WorkingSet64;
            }
#endif
            return bytes > 0;
        }
    }
}
