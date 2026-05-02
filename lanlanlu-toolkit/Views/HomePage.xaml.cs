using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
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
