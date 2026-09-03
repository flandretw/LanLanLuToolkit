using System;
using System.IO;
using System.Runtime.InteropServices;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Lightweight P/Invoke wrapper for NVIDIA Management Library (nvml.dll).
    /// Provides zero-dependency, sub-millisecond GPU telemetry (Temperature, Clocks, Usage, VRAM, Reserved Memory).
    /// </summary>
    public static class NvmlHelper
    {
        private static IntPtr _nvmlModule = IntPtr.Zero;
        private static bool _isInitialized = false;
        private static bool _initAttempted = false;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        // Function delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlInit_v2_delegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlShutdown_delegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetHandleByIndex_v2_delegate(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetTemperature_delegate(IntPtr device, int sensorType, out uint temp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetClockInfo_delegate(IntPtr device, int clockType, out uint clockMhz);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetUtilizationRates_delegate(IntPtr device, out nvmlUtilization_t utilization);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_v2_t
        {
            public uint version;
            public uint padding;
            public ulong total;
            public ulong reserved;
            public ulong free;
            public ulong used;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetMemoryInfo_delegate(IntPtr device, out nvmlMemory_t memory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvmlDeviceGetMemoryInfo_v2_delegate(IntPtr device, ref nvmlMemory_v2_t memory);

        private static nvmlInit_v2_delegate? _nvmlInit;
        private static nvmlShutdown_delegate? _nvmlShutdown;
        private static nvmlDeviceGetHandleByIndex_v2_delegate? _nvmlDeviceGetHandleByIndex;
        private static nvmlDeviceGetTemperature_delegate? _nvmlDeviceGetTemperature;
        private static nvmlDeviceGetClockInfo_delegate? _nvmlDeviceGetClockInfo;
        private static nvmlDeviceGetUtilizationRates_delegate? _nvmlDeviceGetUtilizationRates;
        private static nvmlDeviceGetMemoryInfo_delegate? _nvmlDeviceGetMemoryInfo;
        private static nvmlDeviceGetMemoryInfo_v2_delegate? _nvmlDeviceGetMemoryInfo_v2;

        public static bool Initialize()
        {
            if (_initAttempted) return _isInitialized;
            _initAttempted = true;

            try
            {
                string[] possiblePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll"),
                    "nvml.dll"
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path) || path == "nvml.dll")
                    {
                        _nvmlModule = LoadLibrary(path);
                        if (_nvmlModule != IntPtr.Zero) break;
                    }
                }

                if (_nvmlModule == IntPtr.Zero) return false;

                _nvmlInit = GetDelegate<nvmlInit_v2_delegate>("nvmlInit_v2") ?? GetDelegate<nvmlInit_v2_delegate>("nvmlInit");
                _nvmlShutdown = GetDelegate<nvmlShutdown_delegate>("nvmlShutdown");
                _nvmlDeviceGetHandleByIndex = GetDelegate<nvmlDeviceGetHandleByIndex_v2_delegate>("nvmlDeviceGetHandleByIndex_v2") ?? GetDelegate<nvmlDeviceGetHandleByIndex_v2_delegate>("nvmlDeviceGetHandleByIndex");
                _nvmlDeviceGetTemperature = GetDelegate<nvmlDeviceGetTemperature_delegate>("nvmlDeviceGetTemperature");
                _nvmlDeviceGetClockInfo = GetDelegate<nvmlDeviceGetClockInfo_delegate>("nvmlDeviceGetClockInfo");
                _nvmlDeviceGetUtilizationRates = GetDelegate<nvmlDeviceGetUtilizationRates_delegate>("nvmlDeviceGetUtilizationRates");
                _nvmlDeviceGetMemoryInfo_v2 = GetDelegate<nvmlDeviceGetMemoryInfo_v2_delegate>("nvmlDeviceGetMemoryInfo_v2");
                _nvmlDeviceGetMemoryInfo = GetDelegate<nvmlDeviceGetMemoryInfo_delegate>("nvmlDeviceGetMemoryInfo");

                if (_nvmlInit != null && _nvmlInit() == 0)
                {
                    _isInitialized = true;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static T? GetDelegate<T>(string procName) where T : Delegate
        {
            if (_nvmlModule == IntPtr.Zero) return null;
            IntPtr p = GetProcAddress(_nvmlModule, procName);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public struct GpuTelemetry
        {
            public bool IsValid;
            public uint TemperatureCelsius;
            public uint CoreClockMhz;
            public uint MemoryClockMhz;
            public uint GpuUsagePercent;
            public uint MemoryUsagePercent;
            public double UsedVramGb;
            public double TotalVramGb;
            public double ReservedVramMb;
        }

        public static GpuTelemetry GetTelemetry(uint deviceIndex = 0)
        {
            var result = new GpuTelemetry { IsValid = false };
            if (!Initialize() || _nvmlDeviceGetHandleByIndex == null) return result;

            try
            {
                if (_nvmlDeviceGetHandleByIndex(deviceIndex, out IntPtr device) == 0 && device != IntPtr.Zero)
                {
                    result.IsValid = true;

                    // Temperature (Sensor 0 = GPU)
                    if (_nvmlDeviceGetTemperature != null && _nvmlDeviceGetTemperature(device, 0, out uint temp) == 0)
                    {
                        result.TemperatureCelsius = temp;
                    }

                    // Graphics Core Clock (0 = Graphics)
                    if (_nvmlDeviceGetClockInfo != null && _nvmlDeviceGetClockInfo(device, 0, out uint coreClock) == 0)
                    {
                        result.CoreClockMhz = coreClock;
                    }

                    // Memory Clock (2 = Memory)
                    if (_nvmlDeviceGetClockInfo != null && _nvmlDeviceGetClockInfo(device, 2, out uint memClock) == 0)
                    {
                        result.MemoryClockMhz = memClock;
                    }

                    // Utilization
                    if (_nvmlDeviceGetUtilizationRates != null && _nvmlDeviceGetUtilizationRates(device, out nvmlUtilization_t util) == 0)
                    {
                        result.GpuUsagePercent = util.gpu;
                        result.MemoryUsagePercent = util.memory;
                    }

                    // Memory Info (v2 with reserved support, fallback to v1)
                    bool memQueried = false;
                    if (_nvmlDeviceGetMemoryInfo_v2 != null)
                    {
                        // NVML_STRUCT_VERSION(memory, 2) = sizeof(nvmlMemory_v2_t) | (2 << 24) = 40 | (2 << 24) = 0x02000028
                        var memV2 = new nvmlMemory_v2_t { version = 0x02000028 };
                        if (_nvmlDeviceGetMemoryInfo_v2(device, ref memV2) == 0)
                        {
                            result.UsedVramGb = memV2.used / (1024.0 * 1024.0 * 1024.0);
                            result.TotalVramGb = memV2.total / (1024.0 * 1024.0 * 1024.0);
                            result.ReservedVramMb = memV2.reserved / (1024.0 * 1024.0);
                            memQueried = true;
                        }
                    }

                    if (!memQueried && _nvmlDeviceGetMemoryInfo != null && _nvmlDeviceGetMemoryInfo(device, out nvmlMemory_t mem) == 0)
                    {
                        result.UsedVramGb = mem.used / (1024.0 * 1024.0 * 1024.0);
                        result.TotalVramGb = mem.total / (1024.0 * 1024.0 * 1024.0);
                    }
                }
            }
            catch { }

            return result;
        }
    }
}
