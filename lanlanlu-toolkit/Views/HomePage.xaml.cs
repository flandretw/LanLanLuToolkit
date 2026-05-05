using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page
    {
        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _resources;
        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader AppResources => _resources ??= new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();

        public HomePage()
        {
            this.InitializeComponent();
            UpdateGreeting();
            UpdateVersion();
        }

        private void UpdateVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            
            if (version != null)
            {
                var versionStr = $"{version.Major}.{version.Minor}.{version.Build}";
                var format = AppResources.GetString("HomePage_Version_Format");
                VersionText.Text = string.Format(format, versionStr);
            }
        }

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            string resourceKey = hour switch
            {
                >= 23 or < 5 => "Greeting_LateNight",
                >= 5 and < 11 => "Greeting_Morning",
                >= 11 and < 17 => "Greeting_Afternoon",
                _ => "Greeting_Evening"
            };

            GreetingText.Text = AppResources.GetString(resourceKey);
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
