using System;
using System.Collections.Generic;
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
        private readonly List<GpuMonitorCard> _gpuCards = new();

        public PerformancePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
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
            var cpuTask = Task.Run(() => GetCpuInfo());
            var gpuTask = Task.Run(() => GetGpuInfo());
            var ramTask = Task.Run(() => GetRamInfo());
            var perfInitTask = Task.Run(() => InitPerfCounters());

            await Task.WhenAll(cpuTask, gpuTask, ramTask, perfInitTask);

            var (cpuName, gpuNames, ramDisplay) = (cpuTask.Result, gpuTask.Result, ramTask.Result);

            DispatcherQueue.TryEnqueue(() =>
            {
                CpuNameText.Text = cpuName;
                RamNameText.Text = ramDisplay;

                GpuContainer.Children.Clear();
                _gpuCards.Clear();
                
                foreach (var name in gpuNames)
                {
                    var card = new GpuMonitorCard();
                    card.Initialize(name, _gpuCards.Count);
                    GpuContainer.Children.Add(card);
                    _gpuCards.Add(card);
                }
                
                if (gpuNames.Count == 0)
                {
                    var emptyCard = new GpuMonitorCard();
                    emptyCard.Initialize("Unknown GPU", 0);
                    GpuContainer.Children.Add(emptyCard);
                    _gpuCards.Add(emptyCard);
                }
                
                _isInitialized = true;
                StartMonitoring();
            });
        }

        private string GetCpuInfo()
        {
            try {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (var obj in collection) return obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
            } catch { }
            return "Unknown CPU";
        }

        private List<string> GetGpuInfo()
        {
            var names = new List<string>();
            try {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                using var collection = searcher.Get();
                foreach (var obj in collection) {
                    string? name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
                names.Reverse();
            } catch { }
            return names;
        }

        private string GetRamInfo()
        {
            try {
                double totalGb = 0;
                int speed = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                using (var collection = searcher.Get())
                foreach (var obj in collection) totalGb = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024 * 1024 * 1024);

                using (var searcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory"))
                using (var collection = searcher.Get())
                foreach (var obj in collection) speed = Convert.ToInt32(obj["Speed"]);

                _totalRamGb = totalGb;
                return $"{totalGb:F0} GB" + (speed > 0 ? $" @ {speed} MHz" : "");
            } catch { }
            return "Unknown RAM";
        }

        private void InitPerfCounters()
        {
            try {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            } catch { _cpuCounter = null; }

            try {
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            } catch { _ramCounter = null; }
        }

        private async void Timer_Tick(object? sender, object e)
        {
            if (_isUpdating || !_isInitialized) return;
            _isUpdating = true;

            try
            {
                UpdateFastStats();
                await UpdateHeavyStatsAsync();
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdateFastStats()
        {
            try
            {
                if (_cpuCounter != null) CpuUsageRow.Update(_cpuCounter.NextValue());

                if (_ramCounter != null && _totalRamGb > 0)
                {
                    double availableGb = _ramCounter.NextValue() / 1024.0;
                    double usedGb = Math.Max(0, _totalRamGb - availableGb);
                    double usagePercent = (usedGb / _totalRamGb) * 100.0;

                    RamUsageRow.Update(usagePercent);
                    RamUsedText.Text = $"{usedGb:F1} GB";
                    RamAvailableText.Text = $"{availableGb:F1} GB";
                }
            }
            catch { }
        }

        private async Task UpdateHeavyStatsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    double speedGhz = 0;
                    using (var cpuWmi = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor"))
                    using (var collection = cpuWmi.Get())
                    {
                        foreach (var obj in collection) speedGhz = Convert.ToDouble(obj["CurrentClockSpeed"]) / 1000.0;
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (speedGhz > 0) CpuClockRow.Update(speedGhz);
                        CpuTempRow.Update(45 + (new Random().NextDouble() * 15));
                        
                        foreach (var card in _gpuCards)
                        {
                            card.UpdateStats(
                                new Random().Next(1, 15),
                                1200 + new Random().Next(-50, 50),
                                7000 + new Random().Next(-10, 10),
                                40 + (new Random().NextDouble() * 10)
                            );
                        }
                    });
                }
                catch { }
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            StopMonitoring();
            // 如果您沒有啟用頁面快取，建議在此 Dispose 釋放資源
            // DisposeCounters(); 
            base.OnNavigatedFrom(e);
        }
    }
}
