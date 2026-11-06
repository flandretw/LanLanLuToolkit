using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitialized = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            LoadCurrentLanguage();
            LoadCurrentTheme();
            LoadNotificationSettings();
            LoadSoundEffectsSettings();
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

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedLang = selectedItem.Tag?.ToString() ?? "zh-TW";

                ContentDialog dialog = new ContentDialog
                {
                    Title             = LocalizationHelper.GetString("LanguageChangeDialog_Title"),
                    Content           = LocalizationHelper.GetString("LanguageChangeDialog_Content"),
                    PrimaryButtonText = LocalizationHelper.GetString("LanguageChangeDialog_Confirm"),
                    CloseButtonText   = LocalizationHelper.GetString("LanguageChangeDialog_Cancel"),
                    XamlRoot          = this.XamlRoot
                };

                // Primary button: Save settings and exit application
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    SettingsService.SaveLanguage(selectedLang);
                    SystemRepairPage.CancelActiveRepair();
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
            UpdateNotificationsToggleText();
            UpdateNotificationSoundToggleText();
        }

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateNotificationsToggleText();
            if (!_isInitialized) return;
            bool isEnabled = NotificationsToggle.IsOn;
            SettingsService.SaveNotificationsEnabled(isEnabled);
            NotificationSoundToggle.IsEnabled = isEnabled;
        }

        private void NotificationSoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateNotificationSoundToggleText();
            if (!_isInitialized) return;
            SettingsService.SaveNotificationSound(NotificationSoundToggle.IsOn);
        }

        private void UpdateNotificationsToggleText()
        {
            if (NotificationsToggle == null || NotificationsToggleStateText == null) return;
            NotificationsToggleStateText.Text = NotificationsToggle.IsOn
                ? LocalizationHelper.GetString("Common_Toggle_On")
                : LocalizationHelper.GetString("Common_Toggle_Off");
        }

        private void UpdateNotificationSoundToggleText()
        {
            if (NotificationSoundToggle == null || NotificationSoundToggleStateText == null) return;
            NotificationSoundToggleStateText.Text = NotificationSoundToggle.IsOn
                ? LocalizationHelper.GetString("Common_Toggle_On")
                : LocalizationHelper.GetString("Common_Toggle_Off");
        }

        private void LoadSoundEffectsSettings()
        {
            SoundEffectsToggle.IsOn = SettingsService.GetSoundEffectsEnabled();
            UpdateSoundEffectsToggleText();
        }

        private void SoundEffectsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateSoundEffectsToggleText();
            if (!_isInitialized) return;
            SettingsService.SaveSoundEffectsEnabled(SoundEffectsToggle.IsOn);
        }

        private void UpdateSoundEffectsToggleText()
        {
            if (SoundEffectsToggle == null || SoundEffectsToggleStateText == null) return;
            SoundEffectsToggleStateText.Text = SoundEffectsToggle.IsOn
                ? LocalizationHelper.GetString("Common_Toggle_On")
                : LocalizationHelper.GetString("Common_Toggle_Off");
        }

        private void LoadDebugReportSettings()
        {
            DebugReportToggle.IsOn = SettingsService.GetDebugReportEnabled();
            UpdateDebugReportToggleText();
            UpdateDebugReportPathDisplay();
        }

        private void UpdateDebugReportPathDisplay()
        {
            string path = SettingsService.GetDebugReportPath();
            string prefix = LocalizationHelper.GetString("SettingsPage_DebugReport_StoragePathPrefix/Text");
            DebugReportPathTextBlock.Text = $"{prefix}{path}";
        }

        private void UpdateDebugReportToggleText()
        {
            if (DebugReportToggle == null || DebugReportToggleStateText == null) return;
            DebugReportToggleStateText.Text = DebugReportToggle.IsOn
                ? LocalizationHelper.GetString("Common_Toggle_On")
                : LocalizationHelper.GetString("Common_Toggle_Off");
        }

        private void DebugReportToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateDebugReportToggleText();
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private void SelectDebugFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero && App.MainWindow != null)
                {
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                }

                string initialPath = SettingsService.GetDebugReportPath();
                string title = LocalizationHelper.GetString("SettingsPage_DebugReport_SelectFolderTitle");
                string? result = Win32FilePicker.ShowFolderDialog(hwnd, initialPath, title);

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

        private async void RescanHardwareButton_Click(object sender, RoutedEventArgs e)
        {
            RescanHardwareButton.IsEnabled = false;
            try
            {
                await HardwareProvider.ScanSystemInfoAsync(forceRefresh: true);
                NotificationService.Show(
                    LocalizationHelper.GetString("Notification_Refreshed_Title"),
                    LocalizationHelper.GetString("Notification_Refreshed_Msg"),
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[SettingsPage] Hardware rescan error: {ex.Message}");
            }
            finally
            {
                RescanHardwareButton.IsEnabled = true;
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
}
