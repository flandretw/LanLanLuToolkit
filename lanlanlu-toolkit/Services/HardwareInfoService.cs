using System.Linq;
using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

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
    /// Provides a global hardware information cache and unified WMI scanner to avoid redundant queries.
    /// </summary>
    public static class HardwareProvider
    {
        public static HardwareInfoCache Cache { get; } = new HardwareInfoCache();

        public static event Action? HardwareInfoUpdated;

        /// <summary>
        /// Asynchronously queries WMI and system registry for all hardware specs.
        /// </summary>
        public static async Task<HardwareInfoCache> ScanSystemInfoAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && Cache.IsPopulated)
            {
                return Cache;
            }

            await Task.Run(() =>
            {
                try
                {
                    string cpu = "Unknown CPU";
                    string ram = "Unknown RAM";
                    string gpu = "Unknown GPU";
                    string os = "Unknown OS";

                    // CPU
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                        using var collection = searcher.Get();
                        var cpuList = new List<string>();
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            string name = CleanCpuName(obj["Name"]?.ToString() ?? "");
                            if (!string.IsNullOrEmpty(name))
                            {
                                uint cores = 0, threads = 0;
                                try { cores = Convert.ToUInt32(obj["NumberOfCores"]); } catch { }
                                try { threads = Convert.ToUInt32(obj["NumberOfLogicalProcessors"]); } catch { }

                                string spec = (cores > 0 && threads > 0) ? $"{name} ({cores}C / {threads}T)" : name;
                                if (!cpuList.Contains(spec)) cpuList.Add(spec);
                            }
                        }
                        cpu = cpuList.Count > 0 ? string.Join("\n", cpuList) : cpu;
                    }
                    catch { }

                    // RAM
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
                        using var collection = searcher.Get();
                        ulong totalBytes = 0;
                        uint speed = 0;
                        uint smbiosType = 0;
                        int count = 0;
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            totalBytes += Convert.ToUInt64(obj["Capacity"]);
                            if (speed == 0) speed = Convert.ToUInt32(obj["Speed"]);
                            if (smbiosType == 0)
                            {
                                try { smbiosType = Convert.ToUInt32(obj["SMBIOSMemoryType"]); } catch { }
                            }
                            count++;
                        }

                        int totalSlots = count;
                        try
                        {
                            using var arraySearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                            using var arrayCol = arraySearcher.Get();
                            int slotsSum = 0;
                            foreach (ManagementObject arr in arrayCol)
                            using (arr)
                            {
                                slotsSum += Convert.ToInt32(arr["MemoryDevices"]);
                            }
                            if (slotsSum > 0) totalSlots = slotsSum;
                        }
                        catch { }

                        string memTypeStr = smbiosType switch
                        {
                            24 => "DDR3",
                            26 => "DDR4",
                            34 => "DDR5",
                            30 => "LPDDR4",
                            35 => "LPDDR5",
                            _ => ""
                        };

                        double gb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                        string speedStr = speed > 0 ? $" @ {speed} MHz" : "";
                        string memPart = !string.IsNullOrEmpty(memTypeStr) ? $" {memTypeStr}" : "";
                        ram = $"{gb:F0} GB ({count}/{Math.Max(count, totalSlots)}){memPart}{speedStr}".Trim();
                    }
                    catch { }

                    // GPU
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                        using var collection = searcher.Get();
                        var gpuList = new List<string>();
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            string name = CleanGpuName(obj["Name"]?.ToString() ?? "");
                            if (!string.IsNullOrEmpty(name) && !gpuList.Contains(name)) gpuList.Add(name);
                        }
                        // Order discrete GPUs first (e.g. NVIDIA, AMD RX, Intel Arc) before integrated graphics
                        gpuList = gpuList.OrderBy(g => IsIntegratedGpu(g) ? 1 : 0).ToList();
                        gpu = gpuList.Count > 0 ? string.Join("\n", gpuList) : gpu;
                    }
                    catch { }

                    // OS
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem");
                        using var collection = searcher.Get();
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            string caption = obj["Caption"]?.ToString() ?? "Windows";
                            caption = caption.Replace("Microsoft ", "");
                            string build = obj["Version"]?.ToString() ?? "";
                            
                            string displayVersion = "";
                            string ubr = "";
                            try
                            {
                                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                                if (key != null)
                                {
                                    displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                                    ubr = key.GetValue("UBR")?.ToString() ?? "";
                                }
                            }
                            catch { }

                            string fullBuild = string.IsNullOrEmpty(ubr) ? build ?? "" : $"{build}.{ubr}";
                            os = $"{caption} {displayVersion} ({fullBuild})".Trim().Replace("  ", " ");
                        }
                    }
                    catch { }

                    // Motherboard & BIOS
                    string mb = "Unknown Motherboard";
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                        using var collection = searcher.Get();
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                            string product = obj["Product"]?.ToString() ?? "";
                            mb = $"{manufacturer} {product}".Trim();
                            break;
                        }
                    }
                    catch { }

                    try
                    {
                        using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
                        using var biosCol = biosSearcher.Get();
                        foreach (ManagementObject b in biosCol)
                        using (b)
                        {
                            string biosVer = b["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(biosVer))
                            {
                                mb = $"{mb} (BIOS: {biosVer})".Trim();
                            }
                            break;
                        }
                    }
                    catch { }

                    // Storage (Disk Drives)
                    string storage = "Unknown Storage";
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
                        using var collection = searcher.Get();
                        var storageList = new List<string>();
                        foreach (ManagementObject obj in collection)
                        using (obj)
                        {
                            string? model = obj["Model"]?.ToString();
                            ulong sizeBytes = Convert.ToUInt64(obj["Size"]);
                            double sizeGB = sizeBytes / (1024.0 * 1024.0 * 1024.0);
                            if (model != null) storageList.Add($"{model} ({sizeGB:F0} GB)");
                        }
                        storage = storageList.Count > 0 ? string.Join("\n", storageList) : storage;
                    }
                    catch { }

                    Cache.CpuName = cpu;
                    Cache.RamSize = ram;
                    Cache.GpuName = gpu;
                    Cache.OsVersion = os;
                    Cache.StorageInfo = storage;
                    Cache.MotherboardModel = mb;
                    Cache.IsPopulated = true;
                }
                catch { }
            });

            HardwareInfoUpdated?.Invoke();
            return Cache;
        }

        private static string CleanCpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            string cleaned = name.Split('@')[0].Trim();
            cleaned = cleaned.Replace("(R)", "").Replace("(TM)", "").Replace("CPU", "").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+with\s+Radeon(?:\s+Vega)?\s+Graphics", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            return cleaned;
        }

        private static string CleanGpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            string cleaned = name.Replace("(R)", "").Replace("(TM)", "").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            return cleaned;
        }

        private static bool IsIntegratedGpu(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Iris", StringComparison.OrdinalIgnoreCase);
        }
    }
}
