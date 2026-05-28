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
                string title = LocalizationHelper.GetString("SettingsPage_DebugReport_SelectFolderTitle") ?? "選擇偵錯報告儲存資料夾";
                string? result = dialog.Show(hwnd, initialPath, title);

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

    // A wrapper for the modern Win32 COM IFileOpenDialog configured for folders
    internal class Win32FolderPicker
    {
        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [System.Runtime.InteropServices.PreserveSig] 
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszName);
            void GetFileName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid bhid, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;

        public string? Show(IntPtr hwnd, string? initialPath = null, string? title = null)
        {
            try
            {
                var dialog = (IFileOpenDialog)new FileOpenDialog();
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
                {
                    Guid shellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                    if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, shellItemGuid, out IShellItem initialFolderItem) == 0)
                    {
                        dialog.SetFolder(initialFolderItem);
                    }
                }

                if (dialog.Show(hwnd) == 0) // S_OK
                {
                    dialog.GetResult(out IShellItem resultItem);
                    resultItem.GetDisplayName(0x80058000, out string path); // SIGDN_FILESYSPATH = 0x80058000
                    return path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win32 FolderPicker Error: {ex.Message}");
            }
            return null;
        }
    }
}
