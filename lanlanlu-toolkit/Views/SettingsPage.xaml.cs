using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;
using System;


namespace lanlanlu_toolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitialized = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadCurrentLanguage();
            LoadCurrentTheme();
            LoadCurrentTemperatureUnit();
            LoadNotificationSettings();
            LoadDebugReportSettings();
            InitializeAboutInfo();
            _isInitialized = true;
        }

        private void LoadCurrentLanguage()
        {
            string currentLang = SettingsService.GetLanguage();

            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadCurrentTheme()
        {
            string currentTheme = SettingsService.GetTheme();

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadCurrentTemperatureUnit()
        {
            string currentUnit = SettingsService.GetTemperatureUnit();

            foreach (ComboBoxItem item in TemperatureUnitComboBox.Items)
            {
                if (item.Tag?.ToString() == currentUnit)
                {
                    TemperatureUnitComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedLang = selectedItem.Tag?.ToString() ?? "zh-TW";

                ContentDialog dialog = new ContentDialog
                {
                    Title             = LocalizationHelper.GetString("LanguageChangeDialog_Title")   ?? "Restart Required",
                    Content           = LocalizationHelper.GetString("LanguageChangeDialog_Content") ?? "Changing the language requires a restart to take effect. Save changes and exit?",
                    PrimaryButtonText = LocalizationHelper.GetString("LanguageChangeDialog_Confirm") ?? "Confirm",
                    CloseButtonText   = LocalizationHelper.GetString("LanguageChangeDialog_Cancel")  ?? "Cancel",
                    XamlRoot          = this.XamlRoot
                };

                // Primary button: Save settings and exit application
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    SettingsService.SaveLanguage(selectedLang);
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };

                // Close button: Restore previous selection
                dialog.CloseButtonClick += (_, _) =>
                {
                    _isInitialized = false;
                    LoadCurrentLanguage();
                    _isInitialized = true;
                };

                // Show dialog
                _ = dialog.ShowAsync();
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedTheme = selectedItem.Tag?.ToString() ?? "Default";
                SettingsService.SaveTheme(selectedTheme);

                if (App.MainWindow != null && App.MainWindow.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = SettingsService.ToElementTheme(selectedTheme);
                }
            }
        }

        private void TemperatureUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (TemperatureUnitComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedUnit = selectedItem.Tag?.ToString() ?? "Celsius";
                SettingsService.SaveTemperatureUnit(selectedUnit);
            }
        }

        private void InitializeAboutInfo()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                string appName = LocalizationHelper.GetString("SettingsPage_AppVersion/Text");
                string copyright = LocalizationHelper.GetString("SettingsPage_Copyright/Text");

                AppNameTextBlock.Text = appName;
                AppCopyrightTextBlock.Text = copyright;

                if (version != null)
                {
                    AppVersionTextBlock.Text = $"{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize about info: {ex.Message}");
            }
        }

        private void LoadNotificationSettings()
        {
            NotificationsToggle.IsOn = SettingsService.GetNotificationsEnabled();
            NotificationSoundToggle.IsOn = SettingsService.GetNotificationSound();
            NotificationSoundToggle.IsEnabled = NotificationsToggle.IsOn;
        }

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isEnabled = NotificationsToggle.IsOn;
            SettingsService.SaveNotificationsEnabled(isEnabled);
            NotificationSoundToggle.IsEnabled = isEnabled;
        }

        private void NotificationSoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            SettingsService.SaveNotificationSound(NotificationSoundToggle.IsOn);
        }

        private void LoadDebugReportSettings()
        {
            DebugReportToggle.IsOn = SettingsService.GetDebugReportEnabled();
            UpdateDebugReportPathDisplay();
        }

        private void UpdateDebugReportPathDisplay()
        {
            string path = SettingsService.GetDebugReportPath();
            string prefix = LocalizationHelper.GetString("SettingsPage_DebugReport_StoragePathPrefix/Text") ?? "Currently saved at: ";
            DebugReportPathTextBlock.Text = $"{prefix}{path}";
        }

        private void DebugReportToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isEnabled = DebugReportToggle.IsOn;
            SettingsService.SaveDebugReportEnabled(isEnabled);
            
            // Log the toggle event for debugging purposes
            if (isEnabled)
            {
                LoggingService.Log("Debug report has been enabled by user.");
            }
            else
            {
                // Optional: Log to debug output when feature is disabled
                System.Diagnostics.Debug.WriteLine("Debug report has been disabled.");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private void SelectDebugFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Using a more robust Win32 COM approach because WinRT FolderPicker is highly unstable in unpackaged/admin mode
                var dialog = new Win32FolderPicker();
                IntPtr hwnd = GetActiveWindow();
                
                string initialPath = SettingsService.GetDebugReportPath();
                string? result = dialog.Show(hwnd, initialPath);

                if (result != null)
                {
                    SettingsService.SaveDebugReportPath(result);
                    UpdateDebugReportPathDisplay();
                    LoggingService.Log($"Storage path changed to: {result}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FolderPicker error: {ex.Message}");
            }
        }

        private void ResetDebugFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset to empty string so it falls back to default in SettingsService
            SettingsService.SaveDebugReportPath(string.Empty);
            UpdateDebugReportPathDisplay();
            LoggingService.Log("Storage path reset to default.");
        }
    }

    // A wrapper for the classic Win32 SHBrowseForFolder dialog
    internal class Win32FolderPicker
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder pszPath);

        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public string pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        public string? Show(IntPtr hwnd, string title)
        {
            BROWSEINFO bi = new BROWSEINFO();
            bi.hwndOwner = hwnd;
            bi.lpszTitle = title;
            // BIF_RETURNONLYFSDIRS = 0x0001, BIF_NEWDIALOGSTYLE = 0x0040
            bi.ulFlags = 0x0001 | 0x0040;

            IntPtr pidl = SHBrowseForFolder(ref bi);
            if (pidl != IntPtr.Zero)
            {
                System.Text.StringBuilder path = new System.Text.StringBuilder(260);
                if (SHGetPathFromIDList(pidl, path))
                {
                    CoTaskMemFree(pidl);
                    return path.ToString();
                }
                CoTaskMemFree(pidl);
            }
            return null;
        }
    }
}
