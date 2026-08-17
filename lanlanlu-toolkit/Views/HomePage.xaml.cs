using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        public Thickness ContentMargin => new Thickness(36, HeroHeight + 86, 36, 36);

        public Thickness SystemCardsMargin => new Thickness(0, HeroHeight - 64, 0, 0);

        public HomePage()
        {
            this.InitializeComponent();
            UpdateGreeting();
            this.SizeChanged += HomePage_SizeChanged;
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

        private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutProportions(e.NewSize.Height);

        private void UpdateLayoutProportions(double windowHeight)
        {
            if (windowHeight <= 0) return;
            HeroHeight = windowHeight < 800 ? windowHeight * 0.5 : windowHeight * 0.33;
        }

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

        private void FeaturePerf_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PerformancePage));
        private void FeatureInput_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(InputTesterPage));
        private void FeatureRepair_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SystemRepairPage));
        private void FeatureCrash_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(CrashReportPage));
        private void FeatureHash_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FileHashPage));
        private void FeatureAssoc_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FileAssociationPage));
        private void GoToSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

        private void ScrollLeft_Click(object sender, RoutedEventArgs e) => CardsScrollViewer.ChangeView(CardsScrollViewer.HorizontalOffset - 240, null, null);
        private void ScrollRight_Click(object sender, RoutedEventArgs e) => CardsScrollViewer.ChangeView(CardsScrollViewer.HorizontalOffset + 240, null, null);

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
                NotificationService.Show(
                    LocalizationHelper.GetString("Notification_Copied"),
                    text,
                    InfoBarSeverity.Informational);

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
