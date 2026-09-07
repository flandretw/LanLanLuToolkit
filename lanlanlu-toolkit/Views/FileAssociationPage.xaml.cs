using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class FileAssociationPage : Page
    {
        public FileAssociationPage()
        {
            this.InitializeComponent();
        }

        // A safety guardrail: Blacklist of critical system file extensions that must NEVER be modified or reset.
        // Disabling or altering these will lead to serious system damage or inability to launch executables.
        private static readonly string[] CriticalSystemExtensions = new string[]
        {
            // Executables and scripts
            "exe", "com", "bat", "cmd", "msi", "scr", "vbs", "vbe", "js", "jse", "wsf", "wsh", "ps1",
            // System drivers and libraries
            "dll", "sys", "drv", "ocx", "cpl",
            // System consoles, shortcuts and critical configs
            "lnk", "msc", "reg", "pif", "diagcab", "theme"
        };

        #region Win32 API Definitions

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        #endregion

        private void ExtensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');

            if (string.IsNullOrEmpty(ext))
            {
                ResetAssociationBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                return;
            }

            // Check if user entered a critical system extension
            bool isBlacklisted = Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase));

            if (isBlacklisted)
            {
                ResetAssociationBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;

                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExtTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExt"), ext), InfoBarSeverity.Warning);
            }
            else
            {
                ResetAssociationBtn.IsEnabled = true;
                RegistryNavBtn.IsEnabled = true;
            }
        }

        private void ResetAssociationBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            if (Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExtTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExt"), ext), InfoBarSeverity.Warning);
                return;
            }

            try
            {
                // 1. Delete user override subkey tree via reg.exe (handles subtree permissions smoothly)
                string regArgs = $"delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.{ext}\" /f";
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = regArgs,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(3000);
                }

                // 2. Also ensure C# registry cleanup in case of any edge cases
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}", false);
                }
                catch { }

                // 3. Notify Windows Explorer immediately to refresh icons and association cache
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

                // 4. Show success feedback
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Success_ResetTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Success_ResetCompleted"), ext), InfoBarSeverity.Success);
                LoggingService.Log($"Successfully cleared user association override for: .{ext}");
            }
            catch (Exception ex)
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Error_Title"), ex.Message, InfoBarSeverity.Error);
                LoggingService.Log($"Error resetting association for .{ext}: {ex.Message}");
            }
        }

        private void OpenSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:defaultapps",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Error_Title"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private void RegistryNavBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            string subKeyPath = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";
            string rootPrefix = "電腦";

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", false))
                {
                    string? last = key?.GetValue("LastKey") as string;
                    if (!string.IsNullOrEmpty(last))
                    {
                        int idx = last.IndexOf('\\');
                        rootPrefix = (idx > 0) ? last.Substring(0, idx) : last;
                    }
                }
            }
            catch { }

            string regeditFullPath = $"{rootPrefix}\\{subKeyPath}";

            try
            {
                // 1. Set LastKey in registry with the localized root prefix
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", "LastKey", regeditFullPath);

                // 2. If regedit is already running, terminate it so it re-reads LastKey upon startup
                Process[] existing = Process.GetProcessesByName("regedit");
                foreach (var p in existing)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                    catch { }
                }

                // 3. Start fresh regedit process
                Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    UseShellExecute = true
                });

                // 4. Also copy the target path to clipboard as a reliable backup
                try
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(subKeyPath);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                }
                catch { }

                // 5. Automatically expand guidance card
                AdvancedModeExpander.IsExpanded = true;

                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigatedTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigated"), ext), InfoBarSeverity.Success);
                LoggingService.Log($"Launched Registry Editor with LastKey: {regeditFullPath}");
            }
            catch (Exception ex)
            {
                // Fallback copy to clipboard
                try
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(subKeyPath);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopyTitle"), LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopy"), InfoBarSeverity.Warning);
                }
                catch { }

                LoggingService.Log($"Error launching Registry Editor navigation: {ex.Message}");
            }
        }
    }
}
