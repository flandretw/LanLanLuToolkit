using System;

namespace lanlanlu_toolkit.Services
{
    public class HardwareInfoCache
    {
        public string? CpuName { get; set; }
        public string? GpuName { get; set; }
        public string? RamSize { get; set; }
        public string? MotherboardModel { get; set; }
        public string? OsVersion { get; set; }
        public string? StorageInfo { get; set; }
        public bool IsPopulated { get; set; }
    }

    /// <summary>
    /// Provides a global hardware information cache to avoid redundant and time-consuming WMI queries.
    /// </summary>
    public static class HardwareProvider
    {
        public static HardwareInfoCache Cache { get; } = new HardwareInfoCache();
    }
}
