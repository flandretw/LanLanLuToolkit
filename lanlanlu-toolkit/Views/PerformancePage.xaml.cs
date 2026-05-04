using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class PerformancePage : Page
    {
        private DispatcherTimer? _timer;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private double _totalRamGb;
        private bool _isUpdating = false;
        private bool _isInitialized = false;
        private bool _isActive = false;
        private readonly List<GpuMonitorCard> _gpuCards = new();

        public PerformancePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isActive = true;
            if (!_isInitialized)
            {
                InitializeAllAsync();
            }
            else
            {
                StartMonitoring();
            }
        }

        private void StartMonitoring()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += Timer_Tick;
            }
            
            if (!_timer.IsEnabled) _timer.Start();
        }

        private void StopMonitoring()
        {
            _timer?.Stop();
        }

        private void DisposeCounters()
        {
            try
            {
                _cpuCounter?.Dispose();
                _cpuCounter = null;
                _ramCounter?.Dispose();
                _ramCounter = null;
            }
            catch { }
        }

        private async void InitializeAllAsync()
        {
            // 使用並行任務加速初始化
            var cpuTask = Task.Run(() => GetCpuDetails());
            var gpuTask = Task.Run(() => GetGpuInfo());
            var ramTask = Task.Run(() => GetRamDetails());
            var perfInitTask = Task.Run(() => InitPerfCounters());

            await Task.WhenAll(cpuTask, gpuTask, ramTask, perfInitTask);

            var cpuInfo = cpuTask.Result;
            var gpuInfos = gpuTask.Result;
            var ramInfo = ramTask.Result;

            DispatcherQueue.TryEnqueue(() =>
            {
                // CPU UI
                CpuNameText.Text = cpuInfo.Name;
                CpuBaseSpeedText.Text = $"{cpuInfo.BaseSpeed:F2} GHz";
                CpuSocketsText.Text = cpuInfo.Sockets.ToString();
                CpuCoresText.Text = cpuInfo.Cores.ToString();
                CpuThreadsText.Text = cpuInfo.Threads.ToString();
                var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                CpuVirtualizationText.Text = cpuInfo.Virtualization ? resourceLoader.GetString("PerfPage_Cpu_Enabled") : resourceLoader.GetString("PerfPage_Cpu_Disabled");
                CpuL1Text.Text = cpuInfo.L1Cache;
                CpuL2Text.Text = cpuInfo.L2Cache;
                CpuL3Text.Text = cpuInfo.L3Cache;

                // RAM UI
                RamNameText.Text = ramInfo.Name;
                RamSpeedText.Text = $"{ramInfo.Speed} MT/s";
                RamSlotsText.Text = $"{ramInfo.SlotsUsed} / {ramInfo.TotalSlots}";
                RamFormFactorText.Text = ramInfo.FormFactor;
                RamHardwareReservedText.Text = $"{ramInfo.HardwareReserved:F1} MB";

                GpuContainer.Children.Clear();
                _gpuCards.Clear();
                var gpus = GetGpuInfo().OrderBy(g => g.IsDiscrete).ToList();
                foreach (var info in gpus)
                {
                    var card = new GpuMonitorCard();
                    card.Initialize(info.Name, _gpuCards.Count);
                    card.InitializeDetails(info.DriverVersion, info.DriverDate, info.DirectXVersion, info.PhysicalLocation, info.HardwareReservedGb);
                    card.UpdateMemory(0, info.DedicatedMemoryGb, 0, info.SharedMemoryGb);
                    GpuContainer.Children.Add(card);
                    _gpuCards.Add(card);
                }
                
                if (gpuInfos.Count == 0)
                {
                    var emptyCard = new GpuMonitorCard();
                    emptyCard.Initialize("Unknown GPU", 0);
                    GpuContainer.Children.Add(emptyCard);
                    _gpuCards.Add(emptyCard);
                }
                
                _isInitialized = true;
                UpdateFastStats();
                _ = UpdateHeavyStatsAsync();
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ContentScrollViewer.Visibility = Visibility.Visible;
                StartMonitoring();
            });
        }

        private struct CpuInfo { 
            public string Name; public double BaseSpeed; public int Sockets; 
            public int Cores; public int Threads; public bool Virtualization;
            public string L1Cache; public string L2Cache; public string L3Cache;
        }

        private CpuInfo GetCpuDetails()
        {
            var info = new CpuInfo { Name = "Unknown CPU", L1Cache = "--", L2Cache = "--", L3Cache = "--" };
            try {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (var obj in searcher.Get()) {
                    info.Name = obj["Name"]?.ToString()?.Trim() ?? info.Name;
                    info.BaseSpeed = Convert.ToDouble(obj["MaxClockSpeed"]) / 1000.0;
                    info.Sockets = Convert.ToInt32(obj["SocketDesignation"] != null ? 1 : 1); // Simplification
                    info.Cores = Convert.ToInt32(obj["NumberOfCores"]);
                    info.Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    info.Virtualization = Convert.ToBoolean(obj["VirtualizationFirmwareEnabled"]);
                }
                
                // Sockets correction
                using var s2 = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (var obj in s2.Get()) info.Sockets = Convert.ToInt32(obj["NumberOfProcessors"]);

                // Cache
                using var s3 = new ManagementObjectSearcher("SELECT * FROM Win32_CacheMemory");
                foreach (var obj in s3.Get()) {
                    int level = Convert.ToInt32(obj["Level"]) - 2; // Level 3 in WMI is L1
                    long size = Convert.ToInt64(obj["MaxCacheSize"]);
                    string sizeStr = size >= 1024 ? $"{(size / 1024.0):F1} MB" : $"{size} KB";
                    if (level == 1) info.L1Cache = sizeStr;
                    else if (level == 2) info.L2Cache = sizeStr;
                    else if (level == 3) info.L3Cache = sizeStr;
                }
            } catch { }
            return info;
        }

        private struct GpuInfo {
            public string Name; public string DriverVersion; public string DriverDate;
            public string DirectXVersion; public string PhysicalLocation;
            public double DedicatedMemoryGb; public double SharedMemoryGb; public double TotalMemoryGb;
            public double HardwareReservedGb; public bool IsDiscrete;
        }

        private List<GpuInfo> GetGpuInfo()
        {
            var infos = new List<GpuInfo>();
            try {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (var obj in searcher.Get()) {
                    string? name = obj["Name"]?.ToString();
                    if (name?.Contains("Basic Render") == true) continue;

                    var info = new GpuInfo {
                        Name = name ?? "Unknown GPU",
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "--",
                        DirectXVersion = "12 (FL 12.1)", 
                        PhysicalLocation = "PCI bus --, device --, function --"
                    };

                    // Try to get location from PNPDeviceID
                    string? pnpId = obj["PNPDeviceID"]?.ToString();
                    if (pnpId != null && pnpId.Contains("BUS_")) {
                        // Very rough parsing example
                        info.PhysicalLocation = "PCI Slot " + (pnpId.Contains("DEV_") ? "Detected" : "Unknown");
                    }

                    // Driver Date
                    string? dDate = obj["DriverDate"]?.ToString();
                    if (dDate != null && dDate.Length >= 8) {
                        try {
                            info.DriverDate = $"{dDate.Substring(0, 4)}/{dDate.Substring(4, 2)}/{dDate.Substring(6, 2)}";
                        } catch { info.DriverDate = dDate; }
                    } else {
                        info.DriverDate = "--";
                    }

                    // Memory
                    try {
                        long ramVal = Convert.ToInt64(obj["AdapterRAM"]);
                        if (ramVal < 0) ramVal = (long)uint.MaxValue + ramVal + 1;
                        info.DedicatedMemoryGb = ramVal / (1024.0 * 1024.0 * 1024.0);
                    } catch { info.DedicatedMemoryGb = 0; }
                    
                    info.SharedMemoryGb = _totalRamGb * 0.5;
                    info.TotalMemoryGb = info.DedicatedMemoryGb + info.SharedMemoryGb;
                    
                    // Improved Discrete GPU detection
                    info.IsDiscrete = info.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || 
                                     info.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                     info.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                     info.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                                     info.Name.Contains("RX ", StringComparison.OrdinalIgnoreCase) ||
                                     info.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                    
                    info.HardwareReservedGb = info.IsDiscrete ? 0.1 : 0.0;

                    infos.Add(info);
                }
            } catch { }
            return infos;
        }

        private struct RamInfo {
            public string Name; public int Speed; public int SlotsUsed; 
            public int TotalSlots; public string FormFactor; public double HardwareReserved;
        }

        private RamInfo GetRamDetails()
        {
            var info = new RamInfo { Name = "Unknown RAM", FormFactor = "Unknown" };
            try {
                using (var s1 = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory")) {
                    int count = 0;
                    foreach (var obj in s1.Get()) {
                        info.Speed = Convert.ToInt32(obj["Speed"]);
                        info.Name = obj["Manufacturer"]?.ToString() ?? "Generic";
                        int ff = Convert.ToInt32(obj["FormFactor"]);
                        info.FormFactor = ff == 8 ? "DIMM" : ff == 12 ? "SODIMM" : "Unknown";
                        count++;
                    }
                    info.SlotsUsed = count;
                }
                
                using (var s2 = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemoryArray")) {
                    foreach (var obj in s2.Get()) info.TotalSlots = Convert.ToInt32(obj["MemoryDevices"]);
                }

                using (var s3 = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                foreach (var obj in s3.Get()) {
                    double total = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024 * 1024);
                    _totalRamGb = total / 1024.0;
                    info.Name = $"{_totalRamGb:F0} GB " + info.Name;

                    // Calculate Hardware Reserved: Physical RAM - Visible RAM
                    using var s4 = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (var os in s4.Get()) {
                        double visibleMb = Convert.ToDouble(os["TotalVisibleMemorySize"]) / 1024.0;
                        info.HardwareReserved = Math.Max(0, total - visibleMb);
                    }
                }
                
                // Hardware Reserved (Rough estimate: OS Visible vs Total)
                // We'll calculate this later if needed, for now use a dummy or constant
            } catch { }
            return info;
        }

        private PerformanceCounter? _commitCounter;
        private PerformanceCounter? _cacheCounter;

        private void InitPerfCounters()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); } catch { }
            try { _ramCounter = new PerformanceCounter("Memory", "Available MBytes"); } catch { }
            try { _commitCounter = new PerformanceCounter("Memory", "Committed Bytes"); } catch { }
            try { _cacheCounter = new PerformanceCounter("Memory", "Cache Bytes"); } catch { }
        }

        private async void Timer_Tick(object? sender, object e)
        {
            if (_isUpdating || !_isInitialized || !_isActive) return;
            _isUpdating = true;
            try { UpdateFastStats(); await UpdateHeavyStatsAsync(); } finally { _isUpdating = false; }
        }

        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private const int MaxHistory = 60;

        private void UpdateFastStats()
        {
            try {
                if (_cpuCounter != null) {
                    double cpuUsage = _cpuCounter.NextValue();
                    CpuUsageRow.Update(cpuUsage);
                    UpdateChart(CpuPolyline, _cpuHistory, cpuUsage);
                }

                if (_ramCounter != null && _totalRamGb > 0) {
                    double availableMb = _ramCounter.NextValue();
                    double usedGb = Math.Max(0, _totalRamGb - (availableMb / 1024.0));
                    double ramUsagePercent = (usedGb / _totalRamGb) * 100.0;
                    
                    RamUsageRow.Update(ramUsagePercent);
                    UpdateChart(RamPolyline, _ramHistory, ramUsagePercent);

                    RamInUseText.Text = $"{usedGb:F1} GB";
                    RamAvailableText.Text = $"{(availableMb / 1024.0):F1} GB";
                    
                    if (_commitCounter != null) {
                        double commitGb = _commitCounter.NextValue() / (1024.0 * 1024.0 * 1024.0);
                        RamCommittedText.Text = $"{commitGb:F1} / {_totalRamGb*1.2:F1} GB";
                    }
                    if (_cacheCounter != null) {
                        double cacheGb = _cacheCounter.NextValue() / (1024.0 * 1024.0 * 1024.0);
                        RamCachedText.Text = $"{cacheGb:F1} GB";
                    }
                }
            } catch { }
        }

        private void UpdateChart(Microsoft.UI.Xaml.Shapes.Polyline polyline, Queue<double> history, double val)
        {
            history.Enqueue(val);
            if (history.Count > MaxHistory) history.Dequeue();

            var points = new Microsoft.UI.Xaml.Media.PointCollection();
            int i = 0;
            foreach (var h in history) {
                points.Add(new Windows.Foundation.Point(i, 100 - h));
                i++;
            }
            polyline.Points = points;
        }

        private async Task UpdateHeavyStatsAsync()
        {
            await Task.Run(() => {
                try {
                    double speedGhz = 0;
                    using (var cpuWmi = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor"))
                    foreach (var obj in cpuWmi.Get()) speedGhz = Convert.ToDouble(obj["CurrentClockSpeed"]) / 1000.0;

                    // RAM Detailed Stats
                    using var ramSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_PerfOS_Memory");
                    double availableGb = 0, committedGb = 0, committedLimitGb = 0, cachedGb = 0;
                    double pagedPoolMb = 0, nonPagedPoolMb = 0, compressedMb = 0;
                    
                    foreach (var obj in ramSearcher.Get()) {
                        availableGb = Convert.ToDouble(obj["AvailableBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        committedGb = Convert.ToDouble(obj["CommittedBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        committedLimitGb = Convert.ToDouble(obj["CommitLimit"]) / (1024.0 * 1024.0 * 1024.0);
                        cachedGb = Convert.ToDouble(obj["CacheBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        pagedPoolMb = Convert.ToDouble(obj["PoolPagedBytes"]) / (1024.0 * 1024.0);
                        nonPagedPoolMb = Convert.ToDouble(obj["PoolNonpagedBytes"]) / (1024.0 * 1024.0);
                    }

                    DispatcherQueue.TryEnqueue(() => {
                        if (!_isActive) return;
                        
                        // Update RAM Texts
                        double usedGb = _totalRamGb - availableGb;
                        RamInUseText.Text = $"{usedGb:F1} GB";
                        RamAvailableText.Text = $"{availableGb:F1} GB";
                        RamCommittedText.Text = $"{committedGb:F1} / {committedLimitGb:F1} GB";
                        RamCachedText.Text = $"{cachedGb:F1} GB";
                        RamPagedPoolText.Text = $"{pagedPoolMb:F0} MB";
                        RamNonPagedPoolText.Text = $"{nonPagedPoolMb:F0} MB";
                        RamCompressedText.Text = $"{compressedMb:F0} MB";
                        
                        if (speedGhz > 0) CpuClockText.Text = $"{speedGhz:F2} GHz";
                        CpuTempText.Text = $"{(40 + (new Random().NextDouble() * 10)):F1} °C"; // Placeholder temp
                        foreach (var card in _gpuCards) {
                            card.UpdateStats(new Random().Next(1, 10), 1200, 7000, 45);
                            // Mock memory usage (Current / Total)
                            // In a real implementation, we would use PerformanceCounters matched by LUID
                            card.UpdateMemory(new Random().NextDouble() * 0.5, 4.0, new Random().NextDouble() * 0.2, 8.0);
                        }
                    });
                } catch { }
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isActive = false;
            StopMonitoring();
            base.OnNavigatedFrom(e);
        }
    }
}
