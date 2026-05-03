using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
            UpdateGreeting();
            UpdateVersion();
        }

        private void UpdateVersion()
        {
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            
            if (version != null)
            {
                // 格式化為 Major.Minor.Build
                string versionStr = $"{version.Major}.{version.Minor}.{version.Build}";
                string format = loader.GetString("HomePage_Version_Format");
                VersionText.Text = string.Format(format, versionStr);
            }
        }

        private void UpdateGreeting()
        {
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            var hour = System.DateTime.Now.Hour;
            string resourceKey;

            if (hour >= 23 || hour < 5)
                resourceKey = "Greeting_LateNight";
            else if (hour >= 5 && hour < 11)
                resourceKey = "Greeting_Morning";
            else if (hour >= 11 && hour < 17)
                resourceKey = "Greeting_Afternoon";
            else // 17 - 23
                resourceKey = "Greeting_Evening";

            GreetingText.Text = loader.GetString(resourceKey);
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
