using Microsoft.UI.Xaml;
using System;
using Microsoft.Windows.Globalization;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace lanlanlu_toolkit
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                // Read and apply language settings before InitializeComponent
                string lang = Services.SettingsService.GetLanguage();
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language initialization failed: {ex.Message}");
            }

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Services.LoggingService.Log("--- Application Started ---");
            Services.LoggingService.Log($"OS: {Environment.OSVersion}");
            Services.LoggingService.Log($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            
            MainWindow = new MainWindow();

            // Apply theme
            if (MainWindow.Content is FrameworkElement rootElement)
            {
                string savedTheme = Services.SettingsService.GetTheme();
                rootElement.RequestedTheme = Services.SettingsService.ToElementTheme(savedTheme);
            }

            MainWindow.Activate();
        }
    }
}
