using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
            UpdateVersion();
            UpdateGreeting();
        }

        private void UpdateVersion()
        {
            string versionStr = "1.0.0";
            try
            {
                // 嘗試讀取封裝版本 (僅在 MSIX 封裝模式有效)
                var version = Windows.ApplicationModel.Package.Current.Id.Version;
                versionStr = $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                // 非封裝模式 (免安裝版)：讀取組件版本
                var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (assemblyVersion != null)
                {
                    versionStr = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
                }
            }

            var format = LocalizationHelper.GetString("HomePage_Version_Format");
            VersionText.Text = string.Format(format, versionStr);
        }

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            var resourceKey = hour switch
            {
                >= 5 and < 12 => "Greeting_Morning",
                >= 12 and < 18 => "Greeting_Afternoon",
                _ => "Greeting_Evening"
            };

            GreetingText.Text = LocalizationHelper.GetString(resourceKey);
        }

        private void GoToPerformance_Click(object sender, RoutedEventArgs e)
        {
            // Simple navigation logic if needed, but usually handled by Shell/MainWindow
            // Since we don't have a direct reference to NavView here, we can use the window's frame
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(PerformancePage));
            }
        }

        private void GoToSettings_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(SettingsPage));
            }
        }
    }
}
