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
    /// 提供全域硬體資訊快取，避免重複進行耗時的 WMI 查詢。
    /// </summary>
    public static class HardwareProvider
    {
        public static HardwareInfoCache Cache { get; } = new HardwareInfoCache();
    }
}
