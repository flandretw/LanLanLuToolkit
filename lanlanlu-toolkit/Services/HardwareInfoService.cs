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
                        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                        using var collection = searcher.Get();
                        var cpuList = new List<string>();
                        foreach (var obj in collection)
                        {
                            string? name = obj["Name"]?.ToString()?.Split('@')[0].Trim();
                            if (!string.IsNullOrEmpty(name) && !cpuList.Contains(name)) cpuList.Add(name);
                        }
                        cpu = cpuList.Count > 0 ? string.Join("\n", cpuList) : cpu;
                    }
                    catch { }

                    // RAM
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
                        using var collection = searcher.Get();
                        ulong totalBytes = 0;
                        uint speed = 0;
                        int count = 0;
                        foreach (var obj in collection)
                        {
                            totalBytes += Convert.ToUInt64(obj["Capacity"]);
                            if (speed == 0) speed = Convert.ToUInt32(obj["Speed"]);
                            count++;
                        }
                        double gb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                        ram = $"{gb:F0} GB ({count}/{count}) @ {speed} MHz";
                    }
                    catch { }

                    // GPU
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                        using var collection = searcher.Get();
                        var gpuList = new List<string>();
                        foreach (var obj in collection)
                        {
                            string? name = obj["Name"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(name) && !gpuList.Contains(name)) gpuList.Add(name);
                        }
                        gpu = gpuList.Count > 0 ? string.Join("\n", gpuList) : gpu;
                    }
                    catch { }

                    // OS
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem");
                        using var collection = searcher.Get();
                        foreach (var obj in collection)
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

                    // Motherboard
                    string mb = "Unknown Motherboard";
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                        using var collection = searcher.Get();
                        foreach (var obj in collection)
                        {
                            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                            string product = obj["Product"]?.ToString() ?? "";
                            mb = $"{manufacturer} {product}".Trim();
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
                        foreach (var obj in collection)
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
    }
}
