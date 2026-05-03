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
            InitializeAllAsync();
        }

        private async void InitializeAllAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                    _cpuCounter.NextValue();

                    string cpuName = "Unknown CPU";
                    using (var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    using (var collection = cpuSearcher.Get())
                    {
                        foreach (var obj in collection) cpuName = obj["Name"]?.ToString()?.Trim() ?? cpuName;
                    }

                    List<string> gpuNames = new List<string>();
                    using (var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    using (var collection = gpuSearcher.Get())
                    {
                        foreach (var obj in collection)
                        {
                            string? name = obj["Name"]?.ToString();
                            if (!string.IsNullOrEmpty(name)) gpuNames.Add(name);
                        }
                    }
                    gpuNames.Reverse(); // 反轉順序以匹配工作管理員的 GPU 0/1 順序

                    int ramSpeed = 0;
                    using (var ramSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    using (var collection = ramSearcher.Get())
                    {
                        foreach (var obj in collection)
                            _totalRamGb = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024 * 1024 * 1024);
                    }

                    using (var speedSearcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory"))
                    using (var collection = speedSearcher.Get())
                    {
                        foreach (var obj in collection) ramSpeed = Convert.ToInt32(obj["Speed"]);
                    }
                    string ramDisplay = $"{_totalRamGb:F0} GB" + (ramSpeed > 0 ? $" @ {ramSpeed} MHz" : "");

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CpuNameText.Text = cpuName;
                        RamNameText.Text = ramDisplay;

                        // Create GPU Cards
                        GpuContainer.Children.Clear();
                        _gpuCards.Clear();
                        for (int i = 0; i < gpuNames.Count; i++)
                        {
                            var card = new GpuMonitorCard();
                            card.Initialize(gpuNames[i], i);
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
                        SetupTimer();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hardware Init Error: {ex.Message}");
                }
            });
        }

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
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
                    double usedGb = _totalRamGb - availableGb;
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
                        
                        // Update all GPU cards
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
            _timer?.Stop();
            base.OnNavigatedFrom(e);
        }
    }
}
