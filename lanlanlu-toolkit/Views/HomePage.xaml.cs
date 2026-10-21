using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        public HomePage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            UpdateGreeting();
            this.Loaded += HomePage_Loaded;
            this.Unloaded += HomePage_Unloaded;
            LoadFromCache();
            _ = HardwareProvider.ScanSystemInfoAsync();
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            HardwareProvider.HardwareInfoUpdated += HardwareProvider_HardwareInfoUpdated;
            LoadFromCache();
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            HardwareProvider.HardwareInfoUpdated -= HardwareProvider_HardwareInfoUpdated;
        }

        private void HardwareProvider_HardwareInfoUpdated()
        {
            DispatcherQueue?.TryEnqueue(LoadFromCache);
        }

        private void LoadFromCache()
        {
            if (HardwareProvider.Cache.IsPopulated)
            {
                CpuName = HardwareProvider.Cache.CpuName ?? CpuName;
                GpuName = HardwareProvider.Cache.GpuName ?? GpuName;
                RamSize = HardwareProvider.Cache.RamSize ?? RamSize;
                MotherboardModel = HardwareProvider.Cache.MotherboardModel ?? MotherboardModel;
                OsVersion = HardwareProvider.Cache.OsVersion ?? OsVersion;
                StorageInfo = HardwareProvider.Cache.StorageInfo ?? StorageInfo;
            }
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

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            string greetingKey;
            string iconGlyph;

            switch (hour)
            {
                case >= 0 and < 5:
                    greetingKey = "Greeting_LateNight";
                    iconGlyph = "\uE708"; // Moon / star
                    break;
                case >= 5 and < 12:
                    greetingKey = "Greeting_Morning";
                    iconGlyph = "\uE706"; // Bright sun
                    break;
                case >= 12 and < 18:
                    greetingKey = "Greeting_Afternoon";
                    iconGlyph = "\uE707"; // Sun
                    break;
                default:
                    greetingKey = "Greeting_Evening";
                    iconGlyph = "\uE708"; // Moon
                    break;
            }

            GreetingText.Text = LocalizationHelper.GetString(greetingKey);
            GreetingIcon.Glyph = iconGlyph;
        }

        private void FeatureTaskMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch { }
        }
        private void FeatureInput_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(InputTesterPage));
        private void FeatureRepair_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SystemRepairPage));
        private void FeatureCrash_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(CrashReportPage));
        private void FeatureHash_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FileHashPage));
        private void FeatureAssoc_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FileAssociationPage));
        private void GoToSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                string name = element.Name.Replace("Card", "");
                if (this.FindName(name + "CopyIcon") is FontIcon icon) icon.Opacity = 1.0;
            }
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                string name = element.Name.Replace("Card", "");
                if (this.FindName(name + "CopyIcon") is FontIcon icon) icon.Opacity = 0.4;
            }
        }

        private async void CpuCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(CpuName, CpuCopyIcon);
        private async void GpuCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(GpuName, GpuCopyIcon);
        private async void RamCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(RamSize, RamCopyIcon);
        private async void OsCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(OsVersion, OsCopyIcon);
        private async void StorageCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(StorageInfo, StorageCopyIcon);
        private async void MotherboardCard_Tapped(object sender, RoutedEventArgs e) => await CopyToClipboardAsync(MotherboardModel, MotherboardCopyIcon);

        private async Task CopyToClipboardAsync(string text, FontIcon icon)
        {
            if (string.IsNullOrEmpty(text) || text == "--") return;
            await ClipboardService.CopyTextAsync(
                text,
                icon,
                showNotification: true,
                customNotificationMessage: text,
                customNotificationTitle: LocalizationHelper.GetString("Notification_Copied"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
