using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Windows.Globalization;
using Windows.Storage;

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
            // 在 InitializeComponent 之前讀取並套用語言設定
            try
            {
                string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                string settingsFile = System.IO.Path.Combine(localAppData, "lanlanlu_toolkit", "language.txt");
                if (System.IO.File.Exists(settingsFile))
                {
                    string savedLang = System.IO.File.ReadAllText(settingsFile).Trim();
                    ApplicationLanguages.PrimaryLanguageOverride = savedLang;
                }
            }
            catch
            {
                // 忽略錯誤，使用系統預設
            }

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            // 套用佈景主題
            try
            {
                string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                string themeFile = System.IO.Path.Combine(localAppData, "lanlanlu_toolkit", "theme.txt");
                if (System.IO.File.Exists(themeFile))
                {
                    string savedTheme = System.IO.File.ReadAllText(themeFile).Trim();
                    if (MainWindow.Content is FrameworkElement rootElement)
                    {
                        rootElement.RequestedTheme = savedTheme switch
                        {
                            "Light" => ElementTheme.Light,
                            "Dark" => ElementTheme.Dark,
                            _ => ElementTheme.Default
                        };
                    }
                }
            }
            catch { }

            MainWindow.Activate();
        }
    }
}
