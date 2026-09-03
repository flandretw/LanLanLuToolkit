using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class PerformancePage : Page
    {
        private const int MaxHistory = 60;
        private string _tempUnit = "Celsius";
        private bool _isActive;
        private CancellationTokenSource? _cts;

        // Static Process-Level Cache (Guarantees 0ms Instant Loading on Every Visit)
        private static CpuInfo? s_cachedCpuInfo;
        private static RamInfo? s_cachedRamInfo;
        private static List<DiskDriveInfo>? s_cachedDiskInfos;
        private static List<GpuInfo>? s_cachedGpuInfos;

        // CPU Telemetry State
        private readonly Queue<double> _cpuHistory = new();
        private long _prevCpuIdleTime;
        private long _prevCpuKernelTime;
        private long _prevCpuUserTime;

        // RAM Telemetry State
        private readonly Queue<double> _ramHistory = new();

        // Native Fast Win32 Disk Telemetry State
        private List<DiskDriveInfo> _currentDiskList = new();
        private readonly Dictionary<string, Queue<double>> _diskHistories = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DiskUiBinding> _diskUiBindings = new();
        private readonly Dictionary<string, DiskSample> _prevDiskSamples = new(StringComparer.OrdinalIgnoreCase);

        // GPU Telemetry State
        private List<GpuInfo> _currentGpuList = new();
        private readonly Dictionary<int, Queue<double>> _gpuHistories = new();
        private readonly List<GpuUiBinding> _gpuUiBindings = new();

        // Active Navigation
        private string _selectedNavTag = "CPU";
        private int _selectedGpuIndex = 0;
        private string _selectedDiskLetter = "C";

        private class DiskUiBinding
        {
            public string DriveLetter { get; set; } = "";
            public DiskDriveInfo Info { get; set; } = new();
            public FrameworkElement? RootElement { get; set; }
            public Border? CardBg { get; set; }
            public Border? Pill { get; set; }
            public TextBlock? UsageText { get; set; }
            public TextBlock? Subtext { get; set; }
            public Microsoft.UI.Xaml.Shapes.Polygon? Polygon { get; set; }
            public Microsoft.UI.Xaml.Shapes.Polyline? Polyline { get; set; }
        }

        private class GpuUiBinding
        {
            public int Index { get; set; }
            public GpuInfo Gpu { get; set; } = new();
            public FrameworkElement? RootElement { get; set; }
            public Border? CardBg { get; set; }
            public Border? Pill { get; set; }
            public TextBlock? UsageText { get; set; }
            public TextBlock? Subtext { get; set; }
            public Microsoft.UI.Xaml.Shapes.Polygon? Polygon { get; set; }
            public Microsoft.UI.Xaml.Shapes.Polyline? Polyline { get; set; }
        }

        public PerformancePage()
        {
            this.InitializeComponent();

            for (int i = 0; i < MaxHistory; i++)
            {
                _cpuHistory.Enqueue(0);
                _ramHistory.Enqueue(0);
            }

            InitializeLocalizedLabels();

            this.ActualThemeChanged += (s, e) =>
            {
                RebuildDiskNavCards();
                RebuildGpuNavCards();
                RefreshNavCardSelectionVisuals();
            };
        }

        private void InitializeLocalizedLabels()
        {
            try
            {
                if (CopyAllCpuBtn != null) ToolTipService.SetToolTip(CopyAllCpuBtn, LocalizationHelper.GetString("PerformancePage_CopyAll_Cpu_Tooltip"));
                if (CopyAllRamBtn != null) ToolTipService.SetToolTip(CopyAllRamBtn, LocalizationHelper.GetString("PerformancePage_CopyAll_Ram_Tooltip"));
                if (CopyAllDiskBtn != null) ToolTipService.SetToolTip(CopyAllDiskBtn, LocalizationHelper.GetString("PerformancePage_CopyAll_Disk_Tooltip"));
                if (CopyAllGpuBtn != null) ToolTipService.SetToolTip(CopyAllGpuBtn, LocalizationHelper.GetString("PerformancePage_CopyAll_Gpu_Tooltip"));
            }
            catch { }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isActive = true;
            _cts = new CancellationTokenSource();

            _tempUnit = SettingsService.GetTemperatureUnit();

            InitializeLocalizedLabels();

            // 1. Instant 0ms Synchronous Navigation Card Generation
            InitInstantNavCards();
            RefreshNavCardSelectionVisuals();

            // 2. Initialize Win32 sub-millisecond CPU times
            InitCpuTimes();

            // 3. Start high-frequency background performance loop (0% UI blocking)
            _ = StartPerformanceLoopAsync(_cts.Token);

            // 4. Parallel background WMI spec hydration (runs fully non-blocking in background)
            _ = HydrateHardwareSpecsAsync();
        }

        private void InitInstantNavCards()
        {
            // Only construct if not already built in visual tree
            if (_diskUiBindings.Count == 0)
            {
                _currentDiskList = s_cachedDiskInfos ?? ProbeDiskFast();
                RebuildDiskNavCards();
            }

            if (_gpuUiBindings.Count == 0)
            {
                _currentGpuList = s_cachedGpuInfos ?? ProbeGpuFast();
                RebuildGpuNavCards();
            }

            // Apply cached CPU/RAM specs immediately if available
            if (s_cachedCpuInfo.HasValue) ApplyCpuSpecs(s_cachedCpuInfo.Value);
            if (s_cachedRamInfo.HasValue) ApplyRamSpecs(s_cachedRamInfo.Value);
        }

        private static List<DiskDriveInfo> ProbeDiskFast()
        {
            var list = new List<DiskDriveInfo>();
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .OrderBy(d => d.Name)
                    .ToList();

                string defaultLocalDisk = LocalizationHelper.GetString("PerformancePage_LocalDisk");

                foreach (var d in drives)
                {
                    string letter = d.Name.TrimEnd('\\', ':');
                    double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? defaultLocalDisk : d.VolumeLabel;

                    list.Add(new DiskDriveInfo
                    {
                        DriveLetter = letter,
                        RootPath = d.Name,
                        VolumeLabel = label,
                        FileSystem = d.DriveFormat,
                        DriveType = d.DriveType,
                        TotalSizeGb = totalGb,
                        FreeSpaceGb = freeGb,
                        ModelName = $"{label} ({d.DriveFormat})"
                    });
                }
            }
            catch { }

            if (list.Count == 0)
            {
                string defaultLocalDisk = LocalizationHelper.GetString("PerformancePage_LocalDisk");
                list.Add(new DiskDriveInfo { DriveLetter = "C", VolumeLabel = defaultLocalDisk, TotalSizeGb = 500, FreeSpaceGb = 250 });
            }
            return list;
        }

        private static List<GpuInfo> ProbeGpuFast()
        {
            var list = new List<GpuInfo>();
            try
            {
                // 1. Check AMD / Integrated GPU first via ADL (< 1ms)
                double amdTemp = AdlHelper.GetTemperature(0);
                if (amdTemp > 0)
                {
                    list.Add(new GpuInfo
                    {
                        Name = "AMD Radeon Graphics",
                        DedicatedMemoryGb = 0.5,
                        IsDiscrete = false
                    });
                }

                // 2. Check NVIDIA discrete GPU via NVML (< 1ms)
                var nvTelemetry = NvmlHelper.GetTelemetry(0);
                if (nvTelemetry.IsValid)
                {
                    list.Add(new GpuInfo
                    {
                        Name = "NVIDIA GeForce GPU",
                        DedicatedMemoryGb = nvTelemetry.TotalVramGb > 0 ? nvTelemetry.TotalVramGb : 4.0,
                        HardwareReservedGb = nvTelemetry.ReservedVramMb > 0 ? nvTelemetry.ReservedVramMb / 1024.0 : 0,
                        IsDiscrete = true
                    });
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new GpuInfo { Name = "GPU" });
            }

            // Order so that Integrated GPU is GPU 0 and Discrete GPU is GPU 1 (matches Windows Task Manager)
            return list.OrderBy(g => g.IsDiscrete ? 1 : 0).ToList();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _isActive = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        #region Native Win32 Sub-Millisecond APIs

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        private const int IDC_ARROW = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
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

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISK_PERFORMANCE
        {
            public long BytesRead;
            public long BytesWritten;
            public long ReadTime;
            public long WriteTime;
            public long IdleTime;
            public uint ReadCount;
            public uint WriteCount;
            public uint QueueDepth;
            public uint SplitCount;
            public long QueryTime;
            public uint StorageDeviceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string StorageManagerName;
        }

        private struct DiskSample
        {
            public long BytesRead;
            public long BytesWritten;
            public long ReadTime;
            public long WriteTime;
            public long IdleTime;
            public uint ReadCount;
            public uint WriteCount;
            public long QueryTime;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            ref DISK_PERFORMANCE lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        private void InitCpuTimes()
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                _prevCpuIdleTime = ((long)idle.dwHighDateTime << 32) | idle.dwLowDateTime;
                _prevCpuKernelTime = ((long)kernel.dwHighDateTime << 32) | kernel.dwLowDateTime;
                _prevCpuUserTime = ((long)user.dwHighDateTime << 32) | user.dwLowDateTime;
            }
        }

        private double GetCpuUsagePercent()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

            long idleTime = ((long)idle.dwHighDateTime << 32) | idle.dwLowDateTime;
            long kernelTime = ((long)kernel.dwHighDateTime << 32) | kernel.dwLowDateTime;
            long userTime = ((long)user.dwHighDateTime << 32) | user.dwLowDateTime;

            long usr = userTime - _prevCpuUserTime;
            long ker = kernelTime - _prevCpuKernelTime;
            long idl = idleTime - _prevCpuIdleTime;

            long sys = ker + usr;
            if (sys <= 0) return 0;

            double cpuPercent = (sys - idl) * 100.0 / sys;
            if (cpuPercent < 0) cpuPercent = 0;
            if (cpuPercent > 100) cpuPercent = 100;

            _prevCpuIdleTime = idleTime;
            _prevCpuKernelTime = kernelTime;
            _prevCpuUserTime = userTime;

            return cpuPercent;
        }

        private static bool QueryNativeDiskPerformance(string driveLetter, out DiskSample sample)
        {
            sample = default;
            string devicePath = $@"\\.\{driveLetter}:";
            IntPtr hDevice = CreateFileW(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1)) return false;

            try
            {
                var perf = new DISK_PERFORMANCE();
                bool ok = DeviceIoControl(hDevice, IOCTL_DISK_PERFORMANCE, IntPtr.Zero, 0, ref perf, (uint)Marshal.SizeOf(typeof(DISK_PERFORMANCE)), out _, IntPtr.Zero);
                if (ok)
                {
                    sample = new DiskSample
                    {
                        BytesRead = perf.BytesRead,
                        BytesWritten = perf.BytesWritten,
                        ReadTime = perf.ReadTime,
                        WriteTime = perf.WriteTime,
                        IdleTime = perf.IdleTime,
                        ReadCount = perf.ReadCount,
                        WriteCount = perf.WriteCount,
                        QueryTime = perf.QueryTime
                    };
                    return true;
                }
            }
            finally
            {
                CloseHandle(hDevice);
            }
            return false;
        }

        #endregion

        #region Fast Polling Loop

        private async Task StartPerformanceLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
            while (_isActive && !token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    PollMetrics();
                }
                catch { }
            }
        }

        private void PollMetrics()
        {
            // 1. CPU (Win32 GetSystemTimes: 0.001 ms)
            double cpuUsage = GetCpuUsagePercent();
            _cpuHistory.Enqueue(cpuUsage);
            if (_cpuHistory.Count > MaxHistory) _cpuHistory.Dequeue();

            // 2. RAM (Win32 GlobalMemoryStatusEx: 0.001 ms)
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            double ramUsedGb = 0, ramTotalGb = 0, ramAvailGb = 0, ramPercent = 0;
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                ramTotalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                ramAvailGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                ramUsedGb = Math.Max(0, ramTotalGb - ramAvailGb);
                ramPercent = memStatus.dwMemoryLoad;
            }
            _ramHistory.Enqueue(ramPercent);
            if (_ramHistory.Count > MaxHistory) _ramHistory.Dequeue();

            // Performance Info
            var perfInfo = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION)) };
            bool hasPerfInfo = GetPerformanceInfo(out perfInfo, perfInfo.cb);

            // 3. Disks Telemetry (Native IOCTL_DISK_PERFORMANCE: 0.005 ms)
            var diskSamples = new Dictionary<string, (double activeTime, double readSpeed, double writeSpeed, double respTime)>(StringComparer.OrdinalIgnoreCase);
            foreach (var disk in _currentDiskList)
            {
                string letter = disk.DriveLetter;
                double activeTime = 0, readSpeed = 0, writeSpeed = 0, respTime = 0;

                if (QueryNativeDiskPerformance(letter, out var currentSample))
                {
                    if (_prevDiskSamples.TryGetValue(letter, out var prevSample))
                    {
                        long dQuery = currentSample.QueryTime - prevSample.QueryTime;
                        double dt = dQuery / 10_000_000.0;
                        if (dt > 0)
                        {
                            long dRead = Math.Max(0, currentSample.BytesRead - prevSample.BytesRead);
                            long dWrite = Math.Max(0, currentSample.BytesWritten - prevSample.BytesWritten);
                            long dIdle = Math.Max(0, currentSample.IdleTime - prevSample.IdleTime);
                            long dRTime = Math.Max(0, currentSample.ReadTime - prevSample.ReadTime);
                            long dWTime = Math.Max(0, currentSample.WriteTime - prevSample.WriteTime);
                            long dOps = Math.Max(0, (currentSample.ReadCount - prevSample.ReadCount) + (currentSample.WriteCount - prevSample.WriteCount));

                            readSpeed = dRead / dt;
                            writeSpeed = dWrite / dt;
                            if (dQuery > 0)
                            {
                                activeTime = Math.Clamp(100.0 - ((double)dIdle / dQuery * 100.0), 0, 100);
                            }
                            if (dOps > 0)
                            {
                                respTime = ((dRTime + dWTime) / 10000.0) / dOps;
                            }
                        }
                    }
                    _prevDiskSamples[letter] = currentSample;
                }

                diskSamples[letter] = (activeTime, readSpeed, writeSpeed, respTime);

                if (_diskHistories.TryGetValue(letter, out var history))
                {
                    history.Enqueue(activeTime);
                    if (history.Count > MaxHistory) history.Dequeue();
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isActive) return;

                // CPU UI Updates
                CpuNavUsageText.Text = $"{cpuUsage:F0}%";
                if (s_cachedCpuInfo.HasValue)
                {
                    CpuNavSubtext.Text = $"{s_cachedCpuInfo.Value.BaseSpeed:F2} GHz";
                    CpuClockText.Text = $"{s_cachedCpuInfo.Value.BaseSpeed:F2} GHz";
                    CpuClockBigText.Text = $"{s_cachedCpuInfo.Value.BaseSpeed:F2} GHz";
                }
                CpuUsageBigText.Text = $"{cpuUsage:F0}%";
                RenderWaveform(CpuNavPolyline, CpuNavPolygon, _cpuHistory);

                if (hasPerfInfo)
                {
                    CpuProcessesText.Text = perfInfo.ProcessCount.ToString("N0");
                    CpuSystemThreadsText.Text = perfInfo.ThreadCount.ToString("N0");

                    double pageSizeKb = perfInfo.PageSize.ToInt64() / 1024.0;
                    double commitTotalGb = (perfInfo.CommitTotal.ToInt64() * pageSizeKb) / (1024.0 * 1024.0);
                    double commitLimitGb = (perfInfo.CommitLimit.ToInt64() * pageSizeKb) / (1024.0 * 1024.0);
                    RamCommittedText.Text = $"{commitTotalGb:F1} / {commitLimitGb:F1} GB";

                    double cacheGb = (perfInfo.SystemCache.ToInt64() * pageSizeKb) / (1024.0 * 1024.0);
                    RamCachedText.Text = $"{cacheGb:F1} GB";

                    double pagedMb = (perfInfo.KernelPaged.ToInt64() * pageSizeKb) / 1024.0;
                    RamPagedPoolText.Text = $"{pagedMb:F0} MB";

                    double nonPagedMb = (perfInfo.KernelNonpaged.ToInt64() * pageSizeKb) / 1024.0;
                    RamNonPagedPoolText.Text = $"{nonPagedMb:F0} MB";
                }

                // RAM UI Updates
                RamNavUsageText.Text = $"{ramPercent:F0}%";
                RamNavSubtext.Text = $"{ramUsedGb:F1} / {ramTotalGb:F1} GB ({ramPercent:F0}%)";
                RamInUseBigText.Text = $"{ramUsedGb:F1} GB";
                RamAvailBigText.Text = $"{ramAvailGb:F1} GB";
                RamInUseText.Text = $"{ramUsedGb:F1} GB";
                RamAvailableText.Text = $"{ramAvailGb:F1} GB";
                RenderWaveform(RamNavPolyline, RamNavPolygon, _ramHistory);

                // Right Panel Waveforms
                if (_selectedNavTag == "CPU")
                {
                    RenderWaveform(CpuPolyline, CpuPolygon, _cpuHistory);
                }
                else if (_selectedNavTag == "RAM")
                {
                    RenderWaveform(RamPolyline, RamPolygon, _ramHistory);
                }
                else if (_selectedNavTag.StartsWith("DISK_"))
                {
                    string selLetter = _selectedNavTag.Substring(5);
                    if (_diskHistories.TryGetValue(selLetter, out var hist))
                    {
                        RenderWaveform(DiskPolyline, DiskPolygon, hist);
                    }
                }

                // Update Disk Dynamic Cards & Selected Detail
                foreach (var binding in _diskUiBindings)
                {
                    if (diskSamples.TryGetValue(binding.DriveLetter, out var sample))
                    {
                        if (binding.UsageText != null) binding.UsageText.Text = $"{sample.activeTime:F0}%";
                        if (binding.Subtext != null)
                        {
                            binding.Subtext.Text = $"{binding.Info.UsedSpaceGb:F1} / {binding.Info.TotalSizeGb:F1} GB ({binding.Info.UsagePercent:F0}%)";
                        }
                        if (_diskHistories.TryGetValue(binding.DriveLetter, out var hist) && binding.Polyline != null && binding.Polygon != null)
                        {
                            RenderWaveform(binding.Polyline, binding.Polygon, hist);
                        }

                        if (_selectedNavTag == $"DISK_{binding.DriveLetter}")
                        {
                            DiskActiveTimeBigText.Text = $"{sample.activeTime:F0}%";
                            string formattedRespTime = string.Format(LocalizationHelper.GetString("PerformancePage_Ms"), sample.respTime);
                            DiskResponseTimeBigText.Text = formattedRespTime;

                            DiskReadSpeedText.Text = FormatDataRate(sample.readSpeed);
                            DiskWriteSpeedText.Text = FormatDataRate(sample.writeSpeed);
                            DiskActiveTimeText.Text = $"{sample.activeTime:F0}%";
                            DiskResponseTimeText.Text = formattedRespTime;
                        }
                    }
                }

                // Update GPUs
                UpdateGpuTelemetry();
            });
        }

        private static string FormatDataRate(double bytesPerSec)
        {
            if (bytesPerSec >= 1024.0 * 1024.0 * 1024.0)
                return $"{(bytesPerSec / (1024.0 * 1024.0 * 1024.0)):F2} GB/s";
            if (bytesPerSec >= 1024.0 * 1024.0)
                return $"{(bytesPerSec / (1024.0 * 1024.0)):F1} MB/s";
            return $"{(bytesPerSec / 1024.0):F0} KB/s";
        }

        private void UpdateGpuTelemetry()
        {
            for (int i = 0; i < _gpuUiBindings.Count; i++)
            {
                var binding = _gpuUiBindings[i];
                var gpu = binding.Gpu;

                double gpuUsageVal = 0;
                double gpuTempVal = 0;
                uint coreClock = 0;
                uint memClock = 0;
                double usedVramGb = 0;
                double totalVramGb = gpu.DedicatedMemoryGb;

                if (gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    var telemetry = NvmlHelper.GetTelemetry(0);
                    if (telemetry.IsValid)
                    {
                        gpuUsageVal = telemetry.GpuUsagePercent;
                        gpuTempVal = telemetry.TemperatureCelsius;
                        coreClock = telemetry.CoreClockMhz;
                        memClock = telemetry.MemoryClockMhz;
                        usedVramGb = telemetry.UsedVramGb;
                        if (telemetry.TotalVramGb > 0) totalVramGb = telemetry.TotalVramGb;
                    }
                }
                else if (gpu.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || gpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    gpuTempVal = AdlHelper.GetTemperature(i);
                }

                if (_gpuHistories.TryGetValue(binding.Index, out var history))
                {
                    history.Enqueue(gpuUsageVal);
                    if (history.Count > MaxHistory) history.Dequeue();

                    if (binding.UsageText != null) binding.UsageText.Text = $"{gpuUsageVal:F0}%";
                    if (totalVramGb > 0 && binding.Subtext != null && usedVramGb > 0)
                    {
                        binding.Subtext.Text = $"{usedVramGb:F1} / {totalVramGb:F1} GB";
                    }

                    if (binding.Polyline != null && binding.Polygon != null)
                    {
                        RenderWaveform(binding.Polyline, binding.Polygon, history);
                    }

                    if (_selectedNavTag == $"GPU_{binding.Index}")
                    {
                        GpuUsageBigText.Text = $"{gpuUsageVal:F0}%";
                        GpuTempBigText.Text = FormatTemperature(gpuTempVal);
                        GpuTempText.Text = FormatTemperature(gpuTempVal);
                        if (coreClock > 0) GpuCoreClockText.Text = $"{coreClock} MHz";
                        if (memClock > 0) GpuMemClockText.Text = $"{memClock} MHz";
                        if (totalVramGb > 0 && usedVramGb > 0)
                        {
                            GpuDedicatedMemText.Text = $"{usedVramGb:F1} / {totalVramGb:F1} GB";
                        }
                        if (gpu.HardwareReservedGb > 0)
                        {
                            double mb = gpu.HardwareReservedGb * 1024.0;
                            GpuHardwareReservedText.Text = mb >= 1024 ? $"{gpu.HardwareReservedGb:F1} GB" : $"{mb:F0} MB";
                        }
                        else
                        {
                            GpuHardwareReservedText.Text = "--";
                        }
                        RenderWaveform(GpuPolyline, GpuPolygon, history);
                    }
                }
            }
        }

        private void RenderWaveform(Microsoft.UI.Xaml.Shapes.Polyline polyline, Microsoft.UI.Xaml.Shapes.Polygon polygon, Queue<double> history)
        {
            if (polyline == null || polygon == null || history.Count == 0) return;

            var parent = polygon.Parent as FrameworkElement;
            double w = parent?.ActualWidth ?? 300;
            double h = parent?.ActualHeight ?? 160;
            if (w <= 0) w = 300;
            if (h <= 0) h = 160;

            polyline.HorizontalAlignment = HorizontalAlignment.Left;
            polyline.VerticalAlignment = VerticalAlignment.Top;
            polygon.HorizontalAlignment = HorizontalAlignment.Left;
            polygon.VerticalAlignment = VerticalAlignment.Top;

            var points = history.ToArray();
            int n = points.Length;
            double step = n > 1 ? w / (n - 1) : w;

            var linePoints = new PointCollection();
            var fillPoints = new PointCollection();

            fillPoints.Add(new Point(0, h));

            for (int i = 0; i < n; i++)
            {
                double val = Math.Clamp(points[i], 0, 100);
                double x = i * step;
                double y = h - (val / 100.0 * h);
                var pt = new Point(x, y);
                linePoints.Add(pt);
                fillPoints.Add(pt);
            }

            fillPoints.Add(new Point(w, h));

            polyline.Points = linePoints;
            polygon.Points = fillPoints;
        }

        #endregion

        #region Parallel Background Hardware Specs Hydration

        private async Task HydrateHardwareSpecsAsync()
        {
            if (s_cachedCpuInfo.HasValue && s_cachedRamInfo.HasValue && s_cachedDiskInfos != null && s_cachedGpuInfos != null) return;

            var tasks = new List<Task>();

            // 1. CPU Specs Task
            if (!s_cachedCpuInfo.HasValue)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (!_isActive || _cts?.IsCancellationRequested == true) return;
                    var cpu = ProbeCpuSpecs();
                    s_cachedCpuInfo = cpu;
                    DispatcherQueue.TryEnqueue(() => ApplyCpuSpecs(cpu));
                }));
            }

            // 2. RAM Specs Task
            if (!s_cachedRamInfo.HasValue)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (!_isActive || _cts?.IsCancellationRequested == true) return;
                    var ram = ProbeRamSpecs();
                    s_cachedRamInfo = ram;
                    DispatcherQueue.TryEnqueue(() => ApplyRamSpecs(ram));
                }));
            }

            // 3. Storage / Disk Detailed WMI Task
            if (s_cachedDiskInfos == null)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (!_isActive || _cts?.IsCancellationRequested == true) return;
                    var disks = ProbeDiskSpecs();
                    s_cachedDiskInfos = disks;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _currentDiskList = disks;
                        for (int i = 0; i < _diskUiBindings.Count && i < disks.Count; i++)
                        {
                            _diskUiBindings[i].Info = disks[i];
                        }
                    });
                }));
            }

            // 4. GPU Detailed WMI Task
            if (s_cachedGpuInfos == null)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (!_isActive || _cts?.IsCancellationRequested == true) return;
                    var gpus = ProbeGpuSpecs();
                    s_cachedGpuInfos = gpus;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _currentGpuList = gpus;
                        RebuildGpuNavCards();
                    });
                }));
            }

            await Task.WhenAll(tasks);
        }

        private struct CpuInfo
        {
            public string Name;
            public double BaseSpeed;
            public int Sockets;
            public int Cores;
            public int Threads;
            public bool Virtualization;
            public string L1Cache;
            public string L2Cache;
            public string L3Cache;
        }

        private CpuInfo ProbeCpuSpecs()
        {
            var info = new CpuInfo { Name = "Processor", L1Cache = "--", L2Cache = "--", L3Cache = "--" };
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors, VirtualizationFirmwareEnabled FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (var obj in collection)
                {
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
                foreach (var obj in c3)
                {
                    int level = Convert.ToInt32(obj["Level"]) - 2;
                    long size = Convert.ToInt64(obj["MaxCacheSize"]);
                    string sizeStr = size >= 1024 ? $"{(size / 1024.0):F1} MB" : $"{size} KB";
                    if (level == 1) info.L1Cache = sizeStr;
                    else if (level == 2) info.L2Cache = sizeStr;
                    else if (level == 3) info.L3Cache = sizeStr;
                }
            }
            catch { }
            return info;
        }

        private void ApplyCpuSpecs(CpuInfo info)
        {
            CpuNameText.Text = info.Name;
            CpuBaseSpeedText.Text = $"{info.BaseSpeed:F2} GHz";
            CpuSocketsText.Text = info.Sockets.ToString();
            CpuCoresText.Text = info.Cores.ToString();
            CpuLogicalCoresText.Text = info.Threads.ToString();
            CpuVirtualizationText.Text = info.Virtualization ? LocalizationHelper.GetString("PerfPage_Cpu_Enabled") : LocalizationHelper.GetString("PerfPage_Cpu_Disabled");
            CpuL1Text.Text = info.L1Cache;
            CpuL2Text.Text = info.L2Cache;
            CpuL3Text.Text = info.L3Cache;
        }

        private struct RamInfo
        {
            public string Name;
            public uint Speed;
            public int SlotsUsed;
            public int TotalSlots;
            public string FormFactor;
            public double HardwareReservedMb;
        }

        private RamInfo ProbeRamSpecs()
        {
            var info = new RamInfo { Name = "DDR4", Speed = 3200, SlotsUsed = 2, TotalSlots = 2, FormFactor = "SODIMM", HardwareReservedMb = 0 };
            try
            {
                using var s1 = new ManagementObjectSearcher("SELECT Speed, FormFactor, MemoryType FROM Win32_PhysicalMemory");
                using var c1 = s1.Get();
                info.SlotsUsed = 0;
                foreach (var obj in c1)
                {
                    info.SlotsUsed++;
                    uint spd = Convert.ToUInt32(obj["Speed"]);
                    if (spd > 0) info.Speed = spd;
                    ushort ff = Convert.ToUInt16(obj["FormFactor"]);
                    info.FormFactor = ff switch { 8 => "DIMM", 12 => "SODIMM", _ => "DIMM" };
                }

                using var s2 = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                using var c2 = s2.Get();
                foreach (var obj in c2) info.TotalSlots = Convert.ToInt32(obj["MemoryDevices"]);

                using var s3 = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                using (var c3 = s3.Get())
                foreach (var obj in c3)
                {
                    double total = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024.0 * 1024.0);
                    using var s4 = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    using var c4 = s4.Get();
                    foreach (var os in c4)
                    {
                        double visibleMb = Convert.ToDouble(os["TotalVisibleMemorySize"]) / 1024.0;
                        info.HardwareReservedMb = Math.Max(0, total - visibleMb);
                    }
                }
            }
            catch { }
            return info;
        }

        private void ApplyRamSpecs(RamInfo info)
        {
            RamNameText.Text = info.Name;
            RamSpeedText.Text = $"{info.Speed} MT/s";
            RamSlotsText.Text = $"{info.SlotsUsed} / {Math.Max(info.SlotsUsed, info.TotalSlots)}";
            RamFormFactorText.Text = info.FormFactor;
            RamHardwareReservedText.Text = $"{info.HardwareReservedMb:F0} MB";
        }

        #endregion

        #region Storage / Disk Specs & Dynamic Cards

        public class DiskDriveInfo
        {
            public string DriveLetter { get; set; } = "C";
            public string RootPath { get; set; } = @"C:\";
            public string VolumeLabel { get; set; } = "";
            public string FileSystem { get; set; } = "NTFS";
            public DriveType DriveType { get; set; } = DriveType.Fixed;
            public double TotalSizeGb { get; set; }
            public double FreeSpaceGb { get; set; }
            public double UsedSpaceGb => Math.Max(0, TotalSizeGb - FreeSpaceGb);
            public double UsagePercent => TotalSizeGb > 0 ? (UsedSpaceGb / TotalSizeGb) * 100.0 : 0;
            public string ModelName { get; set; } = "";
        }

        private List<DiskDriveInfo> ProbeDiskSpecs()
        {
            var list = new List<DiskDriveInfo>();
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .OrderBy(d => d.Name)
                    .ToList();

                var modelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT DeviceId, Model FROM Win32_DiskDrive");
                    using var collection = searcher.Get();
                    int dIdx = 0;
                    foreach (var obj in collection)
                    {
                        string model = obj["Model"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(model)) modelMap[dIdx.ToString()] = model;
                        dIdx++;
                    }
                }
                catch { }

                string defaultLocalDisk = LocalizationHelper.GetString("PerformancePage_LocalDisk");

                foreach (var d in drives)
                {
                    string letter = d.Name.TrimEnd('\\', ':');
                    double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);

                    string modelName = !string.IsNullOrEmpty(d.VolumeLabel)
                        ? $"{d.VolumeLabel} ({d.DriveFormat})"
                        : $"{defaultLocalDisk} ({d.DriveFormat})";

                    if (modelMap.Count > 0)
                    {
                        var firstModel = modelMap.Values.FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstModel)) modelName = $"{firstModel} ({d.DriveFormat})";
                    }

                    list.Add(new DiskDriveInfo
                    {
                        DriveLetter = letter,
                        RootPath = d.Name,
                        VolumeLabel = string.IsNullOrEmpty(d.VolumeLabel) ? defaultLocalDisk : d.VolumeLabel,
                        FileSystem = d.DriveFormat,
                        DriveType = d.DriveType,
                        TotalSizeGb = totalGb,
                        FreeSpaceGb = freeGb,
                        ModelName = modelName
                    });
                }
            }
            catch { }

            if (list.Count == 0)
            {
                string defaultLocalDisk = LocalizationHelper.GetString("PerformancePage_LocalDisk");
                list.Add(new DiskDriveInfo { DriveLetter = "C", VolumeLabel = defaultLocalDisk, TotalSizeGb = 500, FreeSpaceGb = 250, ModelName = defaultLocalDisk });
            }
            return list;
        }

        private void RebuildDiskNavCards()
        {
            DiskNavCardContainer.Children.Clear();
            _diskHistories.Clear();
            _diskUiBindings.Clear();

            string diskTitleFormat = LocalizationHelper.GetString("PerformancePage_Disk_Title_Format");

            for (int i = 0; i < _currentDiskList.Count; i++)
            {
                var disk = _currentDiskList[i];
                string letter = disk.DriveLetter;
                bool isSelected = _selectedNavTag == $"DISK_{letter}";

                var hist = new Queue<double>();
                for (int h = 0; h < MaxHistory; h++) hist.Enqueue(0);
                _diskHistories[letter] = hist;

                var rootGrid = new Grid
                {
                    Tag = $"DISK_{letter}",
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };

                // Layer 0: Background Card (Dynamic ThemeResource via NavCardBackgroundStyle)
                var cardBg = new Border
                {
                    Style = (Style)Resources["NavCardBackgroundStyle"],
                    Opacity = isSelected ? 1.0 : 0.0
                };
                rootGrid.Children.Add(cardBg);

                // Layer 1: Left Selection Indicator Pill (Dynamic ThemeResource via NavPillStyle)
                var pill = new Border
                {
                    Style = (Style)Resources["NavPillStyle"],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = isSelected ? 1.0 : 0.0
                };
                rootGrid.Children.Add(pill);

                // Layer 2: Content
                var contentGrid = new Grid { Margin = new Thickness(11, 10, 8, 10), ColumnSpacing = 10 };
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Prominent Left Icon (Disk / Storage: \uEDA2)
                var icon = new FontIcon
                {
                    Glyph = "\uEDA2",
                    Style = (Style)Resources["NavIconStyle"]
                };
                contentGrid.Children.Add(icon);

                var stack = new StackPanel { Spacing = 2 };
                Grid.SetColumn(stack, 1);

                var headerGrid = new Grid();
                var titleText = new TextBlock
                {
                    Text = string.Format(diskTitleFormat, letter),
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                };
                var usageText = new TextBlock
                {
                    Name = $"DiskNavUsageText_{letter}",
                    Text = "0%",
                    Style = (Style)Resources["NavUsageTextStyle"]
                };
                headerGrid.Children.Add(titleText);
                headerGrid.Children.Add(usageText);
                stack.Children.Add(headerGrid);

                var subtext = new TextBlock
                {
                    Text = $"{disk.UsedSpaceGb:F1} / {disk.TotalSizeGb:F1} GB ({disk.UsagePercent:F0}%)",
                    Style = (Style)Resources["NavSubtextStyle"]
                };
                stack.Children.Add(subtext);

                // Mini Sparkline
                var chartGrid = new Grid
                {
                    Height = 28,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var polygon = new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Style = (Style)Resources["NavPolygonStyle"]
                };
                var polyline = new Microsoft.UI.Xaml.Shapes.Polyline
                {
                    Style = (Style)Resources["NavPolylineStyle"]
                };
                chartGrid.Children.Add(polygon);
                chartGrid.Children.Add(polyline);
                stack.Children.Add(chartGrid);

                contentGrid.Children.Add(stack);
                rootGrid.Children.Add(contentGrid);

                rootGrid.PointerPressed += NavCard_PointerPressed;
                rootGrid.PointerEntered += NavCard_PointerEntered;
                rootGrid.PointerExited += NavCard_PointerExited;

                var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
                flyout.Opening += MenuFlyout_Opening;
                flyout.Opened += MenuFlyout_Opened;
                var copyItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                {
                    Text = LocalizationHelper.GetString("PerformancePage_CopyAllMenu/Text"),
                    Icon = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\uE8C8" },
                    Tag = letter,
                };
                copyItem.Click += CopyAllDisk_Click;
                flyout.Items.Add(copyItem);
                rootGrid.ContextFlyout = flyout;
                DiskNavCardContainer.Children.Add(rootGrid);

                _diskUiBindings.Add(new DiskUiBinding
                {
                    DriveLetter = letter,
                    Info = disk,
                    RootElement = rootGrid,
                    CardBg = cardBg,
                    Pill = pill,
                    UsageText = usageText,
                    Subtext = subtext,
                    Polygon = polygon,
                    Polyline = polyline
                });
            }
            RefreshNavCardSelectionVisuals();
        }

        #endregion

        #region GPU Specs & Dynamic Cards

        public class GpuInfo
        {
            public string Name { get; set; } = "GPU";
            public string DriverVersion { get; set; } = "--";
            public string DriverDate { get; set; } = "--";
            public string DirectXVersion { get; set; } = "12 (FL 12.1)";
            public string PhysicalLocation { get; set; } = "PCI Express";
            public double DedicatedMemoryGb { get; set; }
            public double SharedMemoryGb { get; set; }
            public double TotalMemoryGb { get; set; }
            public double HardwareReservedGb { get; set; }
            public bool IsDiscrete { get; set; }
        }

        private List<GpuInfo> ProbeGpuSpecs()
        {
            var list = new List<GpuInfo>();
            double defaultSharedGb = 0;
            try
            {
                var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    double totalPhysGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    defaultSharedGb = Math.Round(totalPhysGb / 2.0, 1);
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID, AdapterRAM FROM Win32_VideoController");
                using var collection = searcher.Get();
                foreach (var obj in collection)
                {
                    string? name = obj["Name"]?.ToString();
                    if (string.IsNullOrEmpty(name) || name.Contains("Basic Render") || name.Contains("Remote")) continue;

                    var g = new GpuInfo
                    {
                        Name = name,
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "--",
                        DirectXVersion = "12 (FL 12.1)",
                        PhysicalLocation = "PCI Express",
                        SharedMemoryGb = defaultSharedGb
                    };

                    string? dDate = obj["DriverDate"]?.ToString();
                    if (dDate != null && dDate.Length >= 8)
                    {
                        try { g.DriverDate = $"{dDate.Substring(0, 4)}/{dDate.Substring(4, 2)}/{dDate.Substring(6, 2)}"; } catch { g.DriverDate = dDate; }
                    }

                    try
                    {
                        long ramVal = Convert.ToInt64(obj["AdapterRAM"]);
                        if (ramVal < 0) ramVal = (long)uint.MaxValue + ramVal + 1;
                        g.DedicatedMemoryGb = ramVal / (1024.0 * 1024.0 * 1024.0);
                    }
                    catch { g.DedicatedMemoryGb = 0; }

                    g.IsDiscrete = g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                   g.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                   g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                   g.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                                   g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

                    // If NVIDIA discrete GPU, use exact NVML VRAM & Reserved metrics
                    if (g.IsDiscrete && g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        var nv = NvmlHelper.GetTelemetry(0);
                        if (nv.IsValid)
                        {
                            if (nv.TotalVramGb > 0) g.DedicatedMemoryGb = nv.TotalVramGb;
                            if (nv.ReservedVramMb > 0) g.HardwareReservedGb = nv.ReservedVramMb / 1024.0;
                        }
                    }

                    list.Add(g);
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new GpuInfo { Name = "Generic Graphics Controller" });
            }

            // Order so that Integrated GPU is GPU 0 and Discrete GPU is GPU 1 (matches Windows Task Manager)
            return list.OrderBy(g => g.IsDiscrete ? 1 : 0).ToList();
        }

        private void RebuildGpuNavCards()
        {
            GpuNavCardContainer.Children.Clear();
            _gpuHistories.Clear();
            _gpuUiBindings.Clear();

            for (int i = 0; i < _currentGpuList.Count; i++)
            {
                int gpuIndex = i;
                var gpu = _currentGpuList[i];
                bool isSelected = _selectedNavTag == $"GPU_{gpuIndex}";

                var hist = new Queue<double>();
                for (int h = 0; h < MaxHistory; h++) hist.Enqueue(0);
                _gpuHistories[gpuIndex] = hist;

                var rootGrid = new Grid
                {
                    Tag = $"GPU_{gpuIndex}",
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };

                // Layer 0: Background Card (Dynamic ThemeResource via NavCardBackgroundStyle)
                var cardBg = new Border
                {
                    Style = (Style)Resources["NavCardBackgroundStyle"],
                    Opacity = isSelected ? 1.0 : 0.0
                };
                rootGrid.Children.Add(cardBg);

                // Layer 1: Left Selection Indicator Pill (Dynamic ThemeResource via NavPillStyle)
                var pill = new Border
                {
                    Style = (Style)Resources["NavPillStyle"],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = isSelected ? 1.0 : 0.0
                };
                rootGrid.Children.Add(pill);

                // Layer 2: Content
                var contentGrid = new Grid { Margin = new Thickness(11, 10, 8, 10), ColumnSpacing = 10 };
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Prominent Left Icon (CPU Glyph \uEEA1 per user request!)
                var icon = new FontIcon
                {
                    Glyph = "\uEEA1",
                    Style = (Style)Resources["NavIconStyle"]
                };
                contentGrid.Children.Add(icon);

                var stack = new StackPanel { Spacing = 2 };
                Grid.SetColumn(stack, 1);

                var headerGrid = new Grid();
                var titleText = new TextBlock
                {
                    Text = _currentGpuList.Count > 1 ? $"GPU {gpuIndex}" : "GPU",
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                };
                var usageText = new TextBlock
                {
                    Name = $"GpuNavUsageText_{gpuIndex}",
                    Text = "0%",
                    Style = (Style)Resources["NavUsageTextStyle"]
                };
                headerGrid.Children.Add(titleText);
                headerGrid.Children.Add(usageText);
                stack.Children.Add(headerGrid);

                var subtext = new TextBlock
                {
                    Text = gpu.Name,
                    Style = (Style)Resources["NavSubtextStyle"]
                };
                stack.Children.Add(subtext);

                // Mini sparkline
                var chartGrid = new Grid
                {
                    Height = 28,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var polygon = new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Style = (Style)Resources["NavPolygonStyle"]
                };
                var polyline = new Microsoft.UI.Xaml.Shapes.Polyline
                {
                    Style = (Style)Resources["NavPolylineStyle"]
                };
                chartGrid.Children.Add(polygon);
                chartGrid.Children.Add(polyline);
                stack.Children.Add(chartGrid);

                contentGrid.Children.Add(stack);
                rootGrid.Children.Add(contentGrid);

                rootGrid.PointerPressed += NavCard_PointerPressed;
                rootGrid.PointerEntered += NavCard_PointerEntered;
                rootGrid.PointerExited += NavCard_PointerExited;

                var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
                flyout.Opening += MenuFlyout_Opening;
                flyout.Opened += MenuFlyout_Opened;
                var copyItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                {
                    Text = LocalizationHelper.GetString("PerformancePage_CopyAllMenu/Text"),
                    Icon = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\uE8C8" },
                    Tag = i,
                };
                copyItem.Click += CopyAllGpu_Click;
                flyout.Items.Add(copyItem);
                rootGrid.ContextFlyout = flyout;
                GpuNavCardContainer.Children.Add(rootGrid);

                _gpuUiBindings.Add(new GpuUiBinding
                {
                    Index = gpuIndex,
                    Gpu = gpu,
                    RootElement = rootGrid,
                    CardBg = cardBg,
                    Pill = pill,
                    UsageText = usageText,
                    Subtext = subtext,
                    Polygon = polygon,
                    Polyline = polyline
                });
            }
            RefreshNavCardSelectionVisuals();
        }

        #endregion

        #region ThemeResource Selection & Hover Styling (Identical to CrashReportPage)

        private void RefreshNavCardSelectionVisuals()
        {
            // 1. CPU
            bool isCpuSelected = _selectedNavTag == "CPU";
            if (CpuNavCardBg != null) CpuNavCardBg.Opacity = isCpuSelected ? 1.0 : 0.0;
            if (CpuNavPill != null) CpuNavPill.Opacity = isCpuSelected ? 1.0 : 0.0;

            // 2. RAM
            bool isRamSelected = _selectedNavTag == "RAM";
            if (RamNavCardBg != null) RamNavCardBg.Opacity = isRamSelected ? 1.0 : 0.0;
            if (RamNavPill != null) RamNavPill.Opacity = isRamSelected ? 1.0 : 0.0;

            // 3. Disks
            foreach (var binding in _diskUiBindings)
            {
                bool isDiskSelected = _selectedNavTag == $"DISK_{binding.DriveLetter}";
                if (binding.CardBg != null) binding.CardBg.Opacity = isDiskSelected ? 1.0 : 0.0;
                if (binding.Pill != null) binding.Pill.Opacity = isDiskSelected ? 1.0 : 0.0;
            }

            // 4. GPUs
            foreach (var binding in _gpuUiBindings)
            {
                bool isGpuSelected = _selectedNavTag == $"GPU_{binding.Index}";
                if (binding.CardBg != null) binding.CardBg.Opacity = isGpuSelected ? 1.0 : 0.0;
                if (binding.Pill != null) binding.Pill.Opacity = isGpuSelected ? 1.0 : 0.0;
            }
        }

        private void NavCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not string tag) return;
            if (tag == _selectedNavTag) return; // Keep selected item at 1.0 opacity

            if (tag == "CPU")
            {
                if (CpuNavCardBg != null) CpuNavCardBg.Opacity = 0.6;
                if (CpuNavPill != null) CpuNavPill.Opacity = 0.5;
            }
            else if (tag == "RAM")
            {
                if (RamNavCardBg != null) RamNavCardBg.Opacity = 0.6;
                if (RamNavPill != null) RamNavPill.Opacity = 0.5;
            }
            else if (tag.StartsWith("DISK_"))
            {
                var b = _diskUiBindings.FirstOrDefault(x => x.RootElement == el);
                if (b?.CardBg != null) b.CardBg.Opacity = 0.6;
                if (b?.Pill != null) b.Pill.Opacity = 0.5;
            }
            else if (tag.StartsWith("GPU_"))
            {
                var b = _gpuUiBindings.FirstOrDefault(x => x.RootElement == el);
                if (b?.CardBg != null) b.CardBg.Opacity = 0.6;
                if (b?.Pill != null) b.Pill.Opacity = 0.5;
            }
        }

        private void NavCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not string tag) return;
            if (tag == _selectedNavTag) return; // Keep selected item at 1.0 opacity

            if (tag == "CPU")
            {
                if (CpuNavCardBg != null) CpuNavCardBg.Opacity = 0.0;
                if (CpuNavPill != null) CpuNavPill.Opacity = 0.0;
            }
            else if (tag == "RAM")
            {
                if (RamNavCardBg != null) RamNavCardBg.Opacity = 0.0;
                if (RamNavPill != null) RamNavPill.Opacity = 0.0;
            }
            else if (tag.StartsWith("DISK_"))
            {
                var b = _diskUiBindings.FirstOrDefault(x => x.RootElement == el);
                if (b?.CardBg != null) b.CardBg.Opacity = 0.0;
                if (b?.Pill != null) b.Pill.Opacity = 0.0;
            }
            else if (tag.StartsWith("GPU_"))
            {
                var b = _gpuUiBindings.FirstOrDefault(x => x.RootElement == el);
                if (b?.CardBg != null) b.CardBg.Opacity = 0.0;
                if (b?.Pill != null) b.Pill.Opacity = 0.0;
            }
        }

        #endregion

        #region Navigation & Tab Selection

        private void MenuFlyout_Opening(object? sender, object e)
        {
            try { SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW)); } catch { }
        }

        private void MenuFlyout_Opened(object? sender, object e)
        {
            try { SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW)); } catch { }
        }

        private void NavCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(sender as UIElement);
            if (!ptr.Properties.IsLeftButtonPressed)
            {
                try { SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW)); } catch { }
                return;
            }
            if (sender is not FrameworkElement clickedCard || clickedCard.Tag is not string tag) return;

            _selectedNavTag = tag;
            RefreshNavCardSelectionVisuals();

            // Toggle right detail panels
            CpuDetailPanel.Visibility = Visibility.Collapsed;
            RamDetailPanel.Visibility = Visibility.Collapsed;
            DiskDetailPanel.Visibility = Visibility.Collapsed;
            GpuDetailPanel.Visibility = Visibility.Collapsed;

            if (tag == "CPU")
            {
                CpuDetailPanel.Visibility = Visibility.Visible;
                RenderWaveform(CpuPolyline, CpuPolygon, _cpuHistory);
            }
            else if (tag == "RAM")
            {
                RamDetailPanel.Visibility = Visibility.Visible;
                RenderWaveform(RamPolyline, RamPolygon, _ramHistory);
            }
            else if (tag.StartsWith("DISK_"))
            {
                DiskDetailPanel.Visibility = Visibility.Visible;
                string letter = tag.Substring(5);
                _selectedDiskLetter = letter;

                var disk = _currentDiskList.FirstOrDefault(d => string.Equals(d.DriveLetter, letter, StringComparison.OrdinalIgnoreCase));
                if (disk != null)
                {
                    DiskTitleText.Text = string.Format(LocalizationHelper.GetString("PerformancePage_Disk_Title_Format"), letter);
                    DiskModelText.Text = disk.ModelName;
                    DiskDriveLetterText.Text = disk.RootPath;
                    DiskVolumeLabelText.Text = disk.VolumeLabel;
                    DiskFileSystemText.Text = disk.FileSystem;
                    DiskDriveTypeText.Text = disk.DriveType == DriveType.Fixed ? LocalizationHelper.GetString("PerformancePage_FixedDisk") : LocalizationHelper.GetString("PerformancePage_RemovableDisk");

                    DiskUsedSpaceText.Text = $"{disk.UsedSpaceGb:F1} GB";
                    DiskFreeSpaceText.Text = $"{disk.FreeSpaceGb:F1} GB";
                    DiskTotalCapacityText.Text = $"{disk.TotalSizeGb:F1} GB";
                    DiskUsagePercentText.Text = $"{disk.UsagePercent:F1}%";
                }

                if (_diskHistories.TryGetValue(letter, out var hist))
                {
                    RenderWaveform(DiskPolyline, DiskPolygon, hist);
                }
            }
            else if (tag.StartsWith("GPU_"))
            {
                GpuDetailPanel.Visibility = Visibility.Visible;
                if (int.TryParse(tag.Substring(4), out int gpuIdx) && gpuIdx < _currentGpuList.Count)
                {
                    _selectedGpuIndex = gpuIdx;
                    var gpu = _currentGpuList[gpuIdx];
                    GpuTitleText.Text = _currentGpuList.Count > 1 ? $"GPU {gpuIdx}" : "GPU";
                    GpuNameText.Text = gpu.Name;
                    GpuDedicatedMemText.Text = $"{gpu.DedicatedMemoryGb:F1} GB";
                    GpuSharedMemText.Text = $"{gpu.SharedMemoryGb:F1} GB";
                    GpuTotalMemText.Text = $"{(gpu.DedicatedMemoryGb + gpu.SharedMemoryGb):F1} GB";
                    if (gpu.HardwareReservedGb > 0)
                    {
                        double mb = gpu.HardwareReservedGb * 1024.0;
                        GpuHardwareReservedText.Text = mb >= 1024 ? $"{gpu.HardwareReservedGb:F1} GB" : $"{mb:F0} MB";
                    }
                    else
                    {
                        GpuHardwareReservedText.Text = "--";
                    }
                    GpuDriverVerText.Text = gpu.DriverVersion;
                    GpuDriverDateText.Text = gpu.DriverDate;
                    GpuDirectXVerText.Text = gpu.DirectXVersion;
                    GpuLocationText.Text = gpu.PhysicalLocation;

                    // Update live metrics immediately
                    double currentTemp = 0;
                    if (gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        var telemetry = NvmlHelper.GetTelemetry(0);
                        if (telemetry.IsValid)
                        {
                            currentTemp = telemetry.TemperatureCelsius;
                            GpuCoreClockText.Text = $"{telemetry.CoreClockMhz} MHz";
                            GpuMemClockText.Text = $"{telemetry.MemoryClockMhz} MHz";
                            GpuUsageBigText.Text = $"{telemetry.GpuUsagePercent}%";
                        }
                    }
                    else if (gpu.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || gpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                    {
                        currentTemp = AdlHelper.GetTemperature(gpuIdx);
                    }
                    GpuTempBigText.Text = FormatTemperature(currentTemp);
                    GpuTempText.Text = FormatTemperature(currentTemp);

                    if (_gpuHistories.TryGetValue(gpuIdx, out var history))
                    {
                        RenderWaveform(GpuPolyline, GpuPolygon, history);
                    }
                }
            }
        }

        #endregion

        #region Direct Copy Buttons & Global Notification Service

        private void CopyToClipboard(string text, Button? btn = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var dp = new DataPackage();
                dp.SetText(text);
                Clipboard.SetContent(dp);

                NotificationService.Show(
                    LocalizationHelper.GetString("Notification_Copied"),
                    text,
                    InfoBarSeverity.Informational);

                if (btn != null)
                {
                    AnimateButtonCopied(btn);
                }
            }
            catch { }
        }

        private async void AnimateButtonCopied(Button btn)
        {
            if (btn.Content is FontIcon icon)
            {
                string prevGlyph = icon.Glyph;
                icon.Glyph = "\uE8FB"; // Accept / Checkmark icon
                await Task.Delay(2000);
                icon.Glyph = prevGlyph;
            }
            else if (btn.Content is StackPanel sp)
            {
                var fontIcon = sp.Children.OfType<FontIcon>().FirstOrDefault();
                if (fontIcon != null)
                {
                    string prevGlyph = fontIcon.Glyph;
                    fontIcon.Glyph = "\uE8FB";
                    await Task.Delay(2000);
                    fontIcon.Glyph = prevGlyph;
                }
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            string textToCopy = "";
            if (btn.Tag is string targetName && !string.IsNullOrEmpty(targetName))
            {
                var target = this.FindName(targetName);
                if (target is TextBlock tb) textToCopy = tb.Text?.Trim() ?? "";
                else if (target is Microsoft.UI.Xaml.Documents.Run run) textToCopy = run.Text?.Trim() ?? "";
                else textToCopy = targetName;
            }

            if (!string.IsNullOrEmpty(textToCopy) && textToCopy != "--")
            {
                CopyToClipboard(textToCopy, btn);
            }
        }

        private static string GetColon() => LocalizationHelper.GetString("Common_Colon");

        private void CopyAllCpu_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            string c = GetColon();
            sb.AppendLine(LocalizationHelper.GetString("PerformancePage_Copy_CpuHeader"));
            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Model"), CpuNameText.Text));
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_Usage.Text")}{c}{CpuUsageBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_Speed.Text")}{c}{CpuClockBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_Processes.Text")}{c}{CpuProcessesText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_Threads.Text")}{c}{CpuSystemThreadsText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_BaseSpeed.Text")}{c}{CpuBaseSpeedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_Sockets.Text")}{c}{CpuSocketsText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_Cores.Text")}{c}{CpuCoresText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_LogicalCores.Text")}{c}{CpuLogicalCoresText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_Virtualization.Text")}{c}{CpuVirtualizationText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_L1.Text")}{c}{CpuL1Text.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_L2.Text")}{c}{CpuL2Text.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Cpu_L3.Text")}{c}{CpuL3Text.Text}");

            CopyToClipboard(sb.ToString(), sender as Button);
        }

        private void CopyAllRam_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            string c = GetColon();
            sb.AppendLine(LocalizationHelper.GetString("PerformancePage_Copy_RamHeader"));
            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Specs"), RamNameText.Text));
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_InUse.Text")}{c}{RamInUseBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_Available.Text")}{c}{RamAvailBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_Committed.Text")}{c}{RamCommittedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_Cached.Text")}{c}{RamCachedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_PagedPool.Text")}{c}{RamPagedPoolText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_NonPagedPool.Text")}{c}{RamNonPagedPoolText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_Compressed.Text")}{c}{RamCompressedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_HardwareReserved.Text")}{c}{RamHardwareReservedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_Speed.Text")}{c}{RamSpeedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_Slots.Text")}{c}{RamSlotsText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Ram_FormFactor.Text")}{c}{RamFormFactorText.Text}");

            CopyToClipboard(sb.ToString(), sender as Button);
        }

        private void CopyAllDisk_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            string c = GetColon();
            string? targetLetter = (sender as Microsoft.UI.Xaml.Controls.MenuFlyoutItem)?.Tag as string;
            var targetDisk = !string.IsNullOrEmpty(targetLetter)
                ? _currentDiskList.FirstOrDefault(d => d.DriveLetter == targetLetter)
                : null;

            if (targetDisk != null && _selectedNavTag != $"DISK_{targetDisk.DriveLetter}")
            {
                string diskTitleFormat = LocalizationHelper.GetString("PerformancePage_Disk_Title_Format");
                string title = string.Format(diskTitleFormat, targetDisk.DriveLetter);
                sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_DiskHeader_Format"), title));
                sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Model"), targetDisk.ModelName));
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_UsedSpace.Text")}{c}{targetDisk.UsedSpaceGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_FreeSpace.Text")}{c}{targetDisk.FreeSpaceGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_TotalCapacity.Text")}{c}{targetDisk.TotalSizeGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_UsagePercent.Text")}{c}{targetDisk.UsagePercent:F0}%");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_DriveLetter.Text")}{c}{targetDisk.DriveLetter}:");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_VolumeLabel.Text")}{c}{targetDisk.VolumeLabel}");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_FileSystem.Text")}{c}{targetDisk.FileSystem}");
                string driveTypeStr = targetDisk.DriveType == DriveType.Fixed ? LocalizationHelper.GetString("PerformancePage_FixedDisk") : LocalizationHelper.GetString("PerformancePage_RemovableDisk");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_DriveType.Text")}{c}{driveTypeStr}");

                CopyToClipboard(sb.ToString(), sender as Button);
                return;
            }

            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_DiskHeader_Format"), DiskTitleText.Text));
            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Model"), DiskModelText.Text));
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_ActiveTime.Text")}{c}{DiskActiveTimeBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_ResponseTime.Text")}{c}{DiskResponseTimeBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_ReadSpeed.Text")}{c}{DiskReadSpeedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_WriteSpeed.Text")}{c}{DiskWriteSpeedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_UsedSpace.Text")}{c}{DiskUsedSpaceText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_FreeSpace.Text")}{c}{DiskFreeSpaceText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_TotalCapacity.Text")}{c}{DiskTotalCapacityText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_UsagePercent.Text")}{c}{DiskUsagePercentText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_DriveLetter.Text")}{c}{DiskDriveLetterText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_VolumeLabel.Text")}{c}{DiskVolumeLabelText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_FileSystem.Text")}{c}{DiskFileSystemText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Disk_DriveType.Text")}{c}{DiskDriveTypeText.Text}");

            CopyToClipboard(sb.ToString(), sender as Button);
        }

        private void CopyAllGpu_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            string c = GetColon();
            int? targetIndex = (sender as Microsoft.UI.Xaml.Controls.MenuFlyoutItem)?.Tag as int?;
            var targetGpu = (targetIndex.HasValue && targetIndex.Value >= 0 && targetIndex.Value < _currentGpuList.Count)
                ? _currentGpuList[targetIndex.Value]
                : null;

            if (targetGpu != null && _selectedNavTag != $"GPU_{targetIndex!.Value}")
            {
                string title = _currentGpuList.Count > 1 ? $"GPU {targetIndex!.Value}" : "GPU";
                sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_GpuHeader_Format"), title));
                sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Model"), targetGpu.Name));
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DedicatedMem.Text")}{c}{targetGpu.DedicatedMemoryGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_SharedMem.Text")}{c}{targetGpu.SharedMemoryGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_TotalMem.Text")}{c}{targetGpu.TotalMemoryGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_ReservedMem.Text")}{c}{targetGpu.HardwareReservedGb:F1} GB");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DriverVersion.Text")}{c}{targetGpu.DriverVersion}");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DriverDate.Text")}{c}{targetGpu.DriverDate}");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DirectX.Text")}{c}{targetGpu.DirectXVersion}");
                sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_Location.Text")}{c}{targetGpu.PhysicalLocation}");

                CopyToClipboard(sb.ToString(), sender as Button);
                return;
            }

            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_GpuHeader_Format"), GpuTitleText.Text));
            sb.AppendLine(string.Format(LocalizationHelper.GetString("PerformancePage_Copy_Model"), GpuNameText.Text));
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_Usage.Text")}{c}{GpuUsageBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Stat_Temperature.Text")}{c}{GpuTempBigText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_CoreClock.Text")}{c}{GpuCoreClockText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_MemoryClock.Text")}{c}{GpuMemClockText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DedicatedMem.Text")}{c}{GpuDedicatedMemText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_SharedMem.Text")}{c}{GpuSharedMemText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_TotalMem.Text")}{c}{GpuTotalMemText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_ReservedMem.Text")}{c}{GpuHardwareReservedText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DriverVersion.Text")}{c}{GpuDriverVerText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DriverDate.Text")}{c}{GpuDriverDateText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_DirectX.Text")}{c}{GpuDirectXVerText.Text}");
            sb.AppendLine($"{LocalizationHelper.GetString("PerformancePage_Gpu_Location.Text")}{c}{GpuLocationText.Text}");

            CopyToClipboard(sb.ToString(), sender as Button);
        }



        #endregion

        private string FormatTemperature(double celsius)
        {
            if (celsius <= 0) return "-- °C";
            if (_tempUnit == "Fahrenheit")
            {
                double f = (celsius * 9.0 / 5.0) + 32.0;
                return $"{f:F0} °F";
            }
            return $"{celsius:F0} °C";
        }
    }
}
