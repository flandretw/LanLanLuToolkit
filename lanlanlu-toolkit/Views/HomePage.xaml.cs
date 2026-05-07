using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Management;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private double _heroHeight = 400;
        
        public double HeroHeight
        {
            get => _heroHeight;
            set
            {
                if (_heroHeight != value)
                {
                    _heroHeight = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ContentMargin));
                    OnPropertyChanged(nameof(SystemCardsMargin));
                }
            }
        }

        public Thickness ContentMargin => new Thickness(36, HeroHeight + 40, 36, 36);

        public Thickness SystemCardsMargin => new Thickness(0, HeroHeight - 100, 0, 0);

        public HomePage()
        {
            this.InitializeComponent();
            UpdateGreeting();
            this.SizeChanged += HomePage_SizeChanged;
            _ = LoadSystemInfoAsync();
        }

        private string _cpuName = LocalizationHelper.GetString("HomePage_Detecting");
        public string CpuName { get => _cpuName; set { _cpuName = value; OnPropertyChanged(); } }

        private string _gpuName = LocalizationHelper.GetString("HomePage_Detecting");
        public string GpuName { get => _gpuName; set { _gpuName = value; OnPropertyChanged(); } }

        private string _ramSize = LocalizationHelper.GetString("HomePage_Detecting");
        public string RamSize { get => _ramSize; set { _ramSize = value; OnPropertyChanged(); } }

        private string _motherboardModel = LocalizationHelper.GetString("HomePage_Detecting");
        public string MotherboardModel { get => _motherboardModel; set { _motherboardModel = value; OnPropertyChanged(); } }

        private string _osVersion = LocalizationHelper.GetString("HomePage_Detecting");
        public string OsVersion { get => _osVersion; set { _osVersion = value; OnPropertyChanged(); } }

        private string _storageInfo = LocalizationHelper.GetString("HomePage_Detecting");
        public string StorageInfo { get => _storageInfo; set { _storageInfo = value; OnPropertyChanged(); } }

        private async Task LoadSystemInfoAsync()
        {
            // 優先從快取載入，實現秒開體驗
            if (HardwareProvider.Cache.IsPopulated)
            {
                CpuName = HardwareProvider.Cache.CpuName ?? CpuName;
                GpuName = HardwareProvider.Cache.GpuName ?? GpuName;
                RamSize = HardwareProvider.Cache.RamSize ?? RamSize;
                MotherboardModel = HardwareProvider.Cache.MotherboardModel ?? MotherboardModel;
                OsVersion = HardwareProvider.Cache.OsVersion ?? OsVersion;
                StorageInfo = HardwareProvider.Cache.StorageInfo ?? StorageInfo;
                return; // 已有資料，無需重複偵測
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
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    using (var collection = searcher.Get())
                    {
                        var cpuList = new System.Collections.Generic.List<string>();
                        foreach (var obj in collection)
                        {
                            string? name = obj["Name"]?.ToString()?.Split('@')[0].Trim();
                            if (!string.IsNullOrEmpty(name) && !cpuList.Contains(name)) cpuList.Add(name);
                        }
                        cpu = cpuList.Count > 0 ? string.Join("\n", cpuList) : cpu;
                    }

                    // RAM (Capacity + Slots + Speed)
                    using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory"))
                    using (var collection = searcher.Get())
                    {
                        ulong totalCapacity = 0;
                        uint speed = 0;
                        int stickCount = 0;
                        foreach (var obj in collection)
                        {
                            totalCapacity += Convert.ToUInt64(obj["Capacity"]);
                            uint s = Convert.ToUInt32(obj["Speed"]);
                            if (s > speed) speed = s;
                            stickCount++;
                        }

                        int totalSlots = stickCount;
                        try 
                        {
                            using (var arraySearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
                            using (var arrayCollection = arraySearcher.Get())
                            foreach (var obj in arrayCollection) totalSlots = Convert.ToInt32(obj["MemoryDevices"]);
                        } catch { }

                        double totalGB = totalCapacity / (1024.0 * 1024.0 * 1024.0);
                        string speedStr = speed > 0 ? $" @ {speed} MHz" : "";
                        ram = $"{totalGB:F0} GB ({stickCount}/{totalSlots}){speedStr}";
                    }

                    // GPU
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    using (var collection = searcher.Get())
                    {
                        var gpuList = new System.Collections.Generic.List<string>();
                        foreach (var obj in collection)
                        {
                            string? name = obj["Name"]?.ToString();
                            if (name != null && !name.Contains("Basic Render") && !gpuList.Contains(name))
                            {
                                gpuList.Add(name);
                            }
                        }
                        
                        // 排序：讓內建顯示卡優先（通常名稱包含 "Graphics" 或 "Intel"）
                        gpuList.Sort((a, b) => 
                        {
                            bool aIsIntegrated = a.Contains("Graphics") || a.Contains("Intel");
                            bool bIsIntegrated = b.Contains("Graphics") || b.Contains("Intel");
                            if (aIsIntegrated && !bIsIntegrated) return -1;
                            if (!aIsIntegrated && bIsIntegrated) return 1;
                            return 0;
                        });

                        gpu = gpuList.Count > 0 ? string.Join("\n", gpuList) : gpu;
                    }

                    // OS
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem"))
                        using (var collection = searcher.Get())
                        foreach (var obj in collection)
                        {
                            string? caption = obj["Caption"]?.ToString()?.Replace("Microsoft ", "");
                            string? build = obj["BuildNumber"]?.ToString();
                            string displayVersion = "";
                            string ubr = "";
                            
                            try 
                            {
                                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                                {
                                    if (key != null)
                                    {
                                        displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                                        ubr = key.GetValue("UBR")?.ToString() ?? "";
                                    }
                                }
                            } catch { }

                            string fullBuild = string.IsNullOrEmpty(ubr) ? build ?? "" : $"{build}.{ubr}";
                            os = $"{caption} {displayVersion} ({fullBuild})".Trim().Replace("  ", " ");
                        }
                    }
                    catch { }

                    // Motherboard
                    string mb = "Unknown Motherboard";
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                        using (var collection = searcher.Get())
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
                        using (var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive"))
                        using (var collection = searcher.Get())
                        {
                            var storageList = new System.Collections.Generic.List<string>();
                            foreach (var obj in collection)
                            {
                                string? model = obj["Model"]?.ToString();
                                ulong sizeBytes = Convert.ToUInt64(obj["Size"]);
                                double sizeGB = sizeBytes / (1024.0 * 1024.0 * 1024.0);
                                if (model != null) storageList.Add($"{model} ({sizeGB:F0} GB)");
                            }
                            storage = storageList.Count > 0 ? string.Join("\n", storageList) : storage;
                        }
                    }
                    catch { }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CpuName = cpu;
                        RamSize = ram;
                        GpuName = gpu;
                        OsVersion = os;
                        StorageInfo = storage;
                        MotherboardModel = mb;

                        // 更新快取
                        HardwareProvider.Cache.CpuName = cpu;
                        HardwareProvider.Cache.RamSize = ram;
                        HardwareProvider.Cache.GpuName = gpu;
                        HardwareProvider.Cache.OsVersion = os;
                        HardwareProvider.Cache.StorageInfo = storage;
                        HardwareProvider.Cache.MotherboardModel = mb;
                        HardwareProvider.Cache.IsPopulated = true;
                    });
                }
                catch { }
            });
        }

        private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateLayoutProportions(e.NewSize.Height);
        }

        private void UpdateLayoutProportions(double windowHeight)
        {
            // 視窗高度小於 800px 時佔一半，否則佔三分之一
            HeroHeight = windowHeight < 800 ? windowHeight * 0.5 : windowHeight * 0.33;
        }

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            string greetingKey = hour switch
            {
                >= 0 and < 5 => "Greeting_LateNight",
                >= 5 and < 12 => "Greeting_Morning",
                >= 12 and < 18 => "Greeting_Afternoon",
                _ => "Greeting_Evening"
            };
            GreetingText.Text = LocalizationHelper.GetString(greetingKey);
        }

        private void GoToPerformance_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PerformancePage));
        private void GoToSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

        private void ScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            CardsScrollViewer.ChangeView(CardsScrollViewer.HorizontalOffset - 240, null, null);
        }

        private void ScrollRight_Click(object sender, RoutedEventArgs e)
        {
            CardsScrollViewer.ChangeView(CardsScrollViewer.HorizontalOffset + 240, null, null);
        }

        private void CardsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            bool showLeft = CardsScrollViewer.HorizontalOffset > 0;
            ScrollLeftButton.Opacity = showLeft ? 1 : 0;
            ScrollLeftButton.IsHitTestVisible = showLeft;

            bool showRight = CardsScrollViewer.HorizontalOffset < CardsScrollViewer.ScrollableWidth;
            ScrollRightButton.Opacity = showRight ? 1 : 0;
            ScrollRightButton.IsHitTestVisible = showRight;
        }

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                // 注意：在 WinUI 3 中，ProtectedCursor 是受保護成員，無法從外部存取。
                // 我們透過將卡片設為具有互動性的結構來讓系統處理游標。
                string name = element.Name.Replace("Card", "");
                if (this.FindName(name + "HoverOverlay") is Border overlay) overlay.Opacity = 1;
                if (this.FindName(name + "CopyIcon") is FontIcon icon) icon.Opacity = 1;
            }
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                string name = element.Name.Replace("Card", "");
                if (this.FindName(name + "HoverOverlay") is Border overlay) overlay.Opacity = 0;
                if (this.FindName(name + "CopyIcon") is FontIcon icon) icon.Opacity = 0;
            }
        }

        private void CpuCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(CpuName, CpuCopyIcon);
        private void GpuCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(GpuName, GpuCopyIcon);
        private void RamCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(RamSize, RamCopyIcon);
        private void OsCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(OsVersion, OsCopyIcon);
        private void StorageCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(StorageInfo, StorageCopyIcon);
        private void MotherboardCard_Tapped(object sender, RoutedEventArgs e) => CopyToClipboard(MotherboardModel, MotherboardCopyIcon);

        private async void CopyToClipboard(string text, FontIcon icon)
        {
            try
            {
                var data = new DataPackage();
                data.SetText(text);
                Clipboard.SetContent(data);

                icon.Glyph = "\uE8FB"; // Check icon
                await Task.Delay(2000);
                icon.Glyph = "\uE8C8"; // Back to Copy icon
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
