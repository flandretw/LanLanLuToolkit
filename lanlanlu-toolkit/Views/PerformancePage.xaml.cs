using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using lanlanlu_toolkit.Services;

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
        private CancellationTokenSource? _cts;
        private readonly List<GpuMonitorCard> _gpuCards = new();

        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private const int MaxHistory = 60;

        // Static cache for hardware info to speed up subsequent loads
        private static CpuInfo? _cachedCpuInfo;
        private static List<GpuInfo>? _cachedGpuInfos;
        private static RamInfo? _cachedRamInfo;

        public PerformancePage()
        {
            this.InitializeComponent();
            InitializeHistory();
        }

        private void InitializeHistory()
        {
            _cpuHistory.Clear();
            _ramHistory.Clear();
            for (int i = 0; i < MaxHistory; i++)
            {
                _cpuHistory.Enqueue(0);
                _ramHistory.Enqueue(0);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isActive = true;
            _cts = new CancellationTokenSource();
            if (!_isInitialized)
            {
                InitializeAllAsync();
            }
            else
            {
                // Ensure UI is visible when restored from cache
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ContentScrollViewer.Visibility = Visibility.Visible;
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
            CpuInfo cpuInfo;
            List<GpuInfo> gpuInfos;
            RamInfo ramInfo;

            if (_cachedCpuInfo != null && _cachedGpuInfos != null && _cachedRamInfo != null)
            {
                cpuInfo = _cachedCpuInfo.Value;
                gpuInfos = _cachedGpuInfos;
                ramInfo = _cachedRamInfo.Value;
                // Still need to init perf counters as they are instance-based
                await Task.Run(() => InitPerfCounters());
            }
            else
            {
                // 使用並行任務加速初始化
                var cpuTask = Task.Run(() => GetCpuDetails());
                var gpuTask = Task.Run(() => GetGpuInfo());
                var ramTask = Task.Run(() => GetRamDetails());
                var perfInitTask = Task.Run(() => InitPerfCounters());

                await Task.WhenAll(cpuTask, gpuTask, ramTask, perfInitTask);

                cpuInfo = cpuTask.Result;
                gpuInfos = gpuTask.Result;
                ramInfo = ramTask.Result;

                // Cache the results
                _cachedCpuInfo = cpuInfo;
                _cachedGpuInfos = gpuInfos;
                _cachedRamInfo = ramInfo;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                // CPU UI
                CpuNameText.Text = cpuInfo.Name;
                CpuBaseSpeedText.Text = $"{cpuInfo.BaseSpeed:F2} GHz";
                CpuSocketsText.Text = cpuInfo.Sockets.ToString();
                CpuCoresText.Text = cpuInfo.Cores.ToString();
                CpuThreadsText.Text = cpuInfo.Threads.ToString();
                CpuVirtualizationText.Text = cpuInfo.Virtualization ? LocalizationHelper.GetString("PerfPage_Cpu_Enabled") : LocalizationHelper.GetString("PerfPage_Cpu_Disabled");
                CpuL1Text.Text = cpuInfo.L1Cache;
                CpuL2Text.Text = cpuInfo.L2Cache;
                CpuL3Text.Text = cpuInfo.L3Cache;

                // RAM UI
                RamNameText.Text = ramInfo.Name;
                RamSpeedText.Text = $"{ramInfo.Speed} MT/s";
                RamSlotsText.Text = $"{ramInfo.SlotsUsed} / {ramInfo.TotalSlots}";
                RamFormFactorText.Text = ramInfo.FormFactor;
                RamHardwareReservedText.Text = $"{ramInfo.HardwareReserved:F1} MB";

                // GPU UI
                GpuContainer.Children.Clear();
                _gpuCards.Clear();
                
                var sortedGpus = gpuInfos.OrderBy(g => g.IsDiscrete).ToList();
                foreach (var info in sortedGpus)
                {
                    var card = new GpuMonitorCard();
                    card.Initialize(info.Name, _gpuCards.Count);
                    card.InitializeDetails(info.DriverVersion, info.DriverDate, info.DirectXVersion, info.PhysicalLocation, info.HardwareReservedGb);
                    card.UpdateMemory(0, info.DedicatedMemoryGb, 0, info.SharedMemoryGb);
                    GpuContainer.Children.Add(card);
                    _gpuCards.Add(card);
                }
                
                if (sortedGpus.Count == 0)
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
                using var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors, VirtualizationFirmwareEnabled FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (var obj in collection) {
                    info.Name = obj["Name"]?.ToString()?.Trim() ?? info.Name;
                    info.BaseSpeed = Convert.ToDouble(obj["MaxClockSpeed"]) / 1000.0;
                    info.Sockets = 1;
                    info.Cores = Convert.ToInt32(obj["NumberOfCores"]);
                    info.Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    info.Virtualization = Convert.ToBoolean(obj["VirtualizationFirmwareEnabled"]);
                }
                
                using var s2 = new ManagementObjectSearcher("SELECT NumberOfProcessors FROM Win32_ComputerSystem");
                using var c2 = s2.Get();
                foreach (var obj in c2) info.Sockets = Convert.ToInt32(obj["NumberOfProcessors"]);

                using var s3 = new ManagementObjectSearcher("SELECT Level, MaxCacheSize FROM Win32_CacheMemory");
                using var c3 = s3.Get();
                foreach (var obj in c3) {
                    var level = Convert.ToInt32(obj["Level"]) - 2; 
                    var size = Convert.ToInt64(obj["MaxCacheSize"]);
                    var sizeStr = size >= 1024 ? $"{(size / 1024.0):F1} MB" : $"{size} KB";
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
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID, AdapterRAM FROM Win32_VideoController");
                using var collection = searcher.Get();
                foreach (var obj in collection) {
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
                using (var s1 = new ManagementObjectSearcher("SELECT Speed, Manufacturer, FormFactor FROM Win32_PhysicalMemory")) {
                    using var c1 = s1.Get();
                    int count = 0;
                    foreach (var obj in c1) {
                        info.Speed = Convert.ToInt32(obj["Speed"]);
                        info.Name = obj["Manufacturer"]?.ToString() ?? "Generic";
                        int ff = Convert.ToInt32(obj["FormFactor"]);
                        info.FormFactor = ff == 8 ? "DIMM" : ff == 12 ? "SODIMM" : "Unknown";
                        count++;
                    }
                    info.SlotsUsed = count;
                }
                
                using (var s2 = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray")) {
                    using var c2 = s2.Get();
                    foreach (var obj in c2) info.TotalSlots = Convert.ToInt32(obj["MemoryDevices"]);
                }

                using (var s3 = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                using (var c3 = s3.Get())
                foreach (var obj in c3) {
                    double total = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024 * 1024);
                    _totalRamGb = total / 1024.0;
                    info.Name = $"{_totalRamGb:F0} GB " + info.Name;

                    using var s4 = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    using var c4 = s4.Get();
                    foreach (var os in c4) {
                        double visibleMb = Convert.ToDouble(os["TotalVisibleMemorySize"]) / 1024.0;
                        info.HardwareReserved = Math.Max(0, total - visibleMb);
                    }
                }
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

        private void UpdateFastStats()
        {
            try {
                if (!_isActive || _cts?.IsCancellationRequested == true) return;

                if (_cpuCounter != null) {
                    var cpuUsage = _cpuCounter.NextValue();
                    CpuUsageRow.Update(cpuUsage);
                    UpdateChart(CpuPolyline, CpuPolygon, _cpuHistory, cpuUsage);
                }

                if (_ramCounter != null && _totalRamGb > 0) {
                    var availableMb = _ramCounter.NextValue();
                    var usedGb = Math.Max(0, _totalRamGb - (availableMb / 1024.0));
                    var ramUsagePercent = (usedGb / _totalRamGb) * 100.0;
                    
                    RamUsageRow.Update(ramUsagePercent);
                    UpdateChart(RamPolyline, RamPolygon, _ramHistory, ramUsagePercent);

                    // These are updated fast, others in Heavy loop
                    RamInUseText.Text = $"{usedGb:F1} GB";
                    RamAvailableText.Text = $"{(availableMb / 1024.0):F1} GB";
                }
            } catch { }
        }

        private void UpdateChart(Microsoft.UI.Xaml.Shapes.Polyline polyline, Microsoft.UI.Xaml.Shapes.Polygon polygon, Queue<double> history, double val)
        {
            history.Enqueue(val);
            if (history.Count > MaxHistory) history.Dequeue();

            double w = (polygon.Parent as FrameworkElement)?.ActualWidth ?? 300;
            double h = (polygon.Parent as FrameworkElement)?.ActualHeight ?? 160;
            if (w <= 0) w = 300;
            if (h <= 0) h = 160;

            var linePoints = new Microsoft.UI.Xaml.Media.PointCollection();
            var fillPoints = new Microsoft.UI.Xaml.Media.PointCollection();
            double step = w / (MaxHistory - 1);
            var historyArray = history.ToArray();

            // 1. Polyline & Polygon Base: Left to Right
            for (int i = 0; i < historyArray.Length; i++) {
                double y = (100 - historyArray[i]) / 100.0 * h;
                var p = new Windows.Foundation.Point(i * step, y);
                linePoints.Add(p);
                fillPoints.Add(p);
            }

            // Close the polygon for fill area (Bottom-right then Bottom-left)
            fillPoints.Add(new Windows.Foundation.Point(w, h));
            fillPoints.Add(new Windows.Foundation.Point(0, h));

            polyline.Points = linePoints;
            polygon.Points = fillPoints;
        }

        private async Task UpdateHeavyStatsAsync()
        {
            await Task.Run(() => {
                if (!_isActive || _cts?.IsCancellationRequested == true) return;
                
                try {
                    double speedGhz = 0;
                    using (var cpuWmi = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor"))
                    using (var cpuCol = cpuWmi.Get())
                    foreach (var obj in cpuCol) speedGhz = Convert.ToDouble(obj["CurrentClockSpeed"]) / 1000.0;

                    // RAM Detailed Stats
                    using var ramSearcher = new ManagementObjectSearcher("SELECT AvailableBytes, CommittedBytes, CommitLimit, CacheBytes, PoolPagedBytes, PoolNonpagedBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
                    using var ramCol = ramSearcher.Get();
                    double availableGb = 0, committedGb = 0, committedLimitGb = 0, cachedGb = 0;
                    double pagedPoolMb = 0, nonPagedPoolMb = 0, compressedMb = 0;
                    
                    foreach (var obj in ramCol) {
                        availableGb = Convert.ToDouble(obj["AvailableBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        committedGb = Convert.ToDouble(obj["CommittedBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        committedLimitGb = Convert.ToDouble(obj["CommitLimit"]) / (1024.0 * 1024.0 * 1024.0);
                        cachedGb = Convert.ToDouble(obj["CacheBytes"]) / (1024.0 * 1024.0 * 1024.0);
                        pagedPoolMb = Convert.ToDouble(obj["PoolPagedBytes"]) / (1024.0 * 1024.0);
                        nonPagedPoolMb = Convert.ToDouble(obj["PoolNonpagedBytes"]) / (1024.0 * 1024.0);
                    }

                    DispatcherQueue.TryEnqueue(() => {
                        try {
                            if (!_isActive || _cts?.IsCancellationRequested == true) return;
                            
                            // Update RAM Texts
                            double usedGb = Math.Max(0, _totalRamGb - availableGb);
                            RamInUseText.Text = $"{usedGb:F1} GB";
                            RamAvailableText.Text = $"{availableGb:F1} GB";
                            RamCommittedText.Text = $"{committedGb:F1} / {committedLimitGb:F1} GB";
                            RamCachedText.Text = $"{cachedGb:F1} GB";
                            RamPagedPoolText.Text = $"{pagedPoolMb:F0} MB";
                            RamNonPagedPoolText.Text = $"{nonPagedPoolMb:F0} MB";
                            RamCompressedText.Text = $"{compressedMb:F0} MB";
                            
                            if (speedGhz > 0) CpuClockText.Text = $"{speedGhz:F2} GHz";
                            
                            double cpuTempC = 40 + (new Random().NextDouble() * 10); // Placeholder temp
                            if (SettingsService.GetTemperatureUnit() == "Fahrenheit")
                            {
                                double f = (cpuTempC * 9 / 5) + 32;
                                CpuTempText.Text = string.Format(LocalizationHelper.GetString("Temperature_Fahrenheit_Format"), f);
                            }
                            else
                            {
                                CpuTempText.Text = string.Format(LocalizationHelper.GetString("Temperature_Celsius_Format"), cpuTempC);
                            }
                            foreach (var card in _gpuCards) {
                                card.UpdateStats(new Random().Next(1, 10), 1200, 7000, 45);
                                // Mock memory usage (Current / Total)
                                card.UpdateMemory(new Random().NextDouble() * 0.5, 4.0, new Random().NextDouble() * 0.2, 8.0);
                            }
                        } catch { }
                    });
                } catch { }
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isActive = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            StopMonitoring();
            base.OnNavigatedFrom(e);
        }
    }
}
