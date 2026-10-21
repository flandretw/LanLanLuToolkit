using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class FileAssociationPage : Page
    {
        public FileAssociationPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
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

        private static readonly Regex ValidExtensionRegex = new Regex(@"^[a-zA-Z0-9_\-\+][a-zA-Z0-9_\-\+\.]*$", RegexOptions.Compiled);

        private static bool IsValidExtension(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 64)
            {
                return false;
            }

            // Must not contain invalid filename characters or whitespace
            if (ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ext.Any(char.IsWhiteSpace))
            {
                return false;
            }

            // Must not contain quotes, path separators, command separators, or traversal dots
            if (ext.Contains('"') || ext.Contains('\'') || ext.Contains('\\') || ext.Contains('/') ||
                ext.Contains("..") || ext.EndsWith('.'))
            {
                return false;
            }

            return ValidExtensionRegex.IsMatch(ext);
        }

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

            if (!IsValidExtension(ext))
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

        private async void ResetAssociationBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            if (!IsValidExtension(ext))
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_InvalidExtTitle"), LocalizationHelper.GetString("FileAssociationPage_Warning_InvalidExt"), InfoBarSeverity.Warning);
                return;
            }

            if (Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExtTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExt"), ext), InfoBarSeverity.Warning);
                return;
            }

            ResetAssociationBtn.IsEnabled = false;

            try
            {
                await Task.Run(() =>
                {
                    string subKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";

                    // Check if key exists. If it doesn't, association override is already gone.
                    using (var initialCheck = Registry.CurrentUser.OpenSubKey(subKeyPath))
                    {
                        if (initialCheck == null)
                        {
                            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                            return;
                        }
                    }

                    // 1. Delete user override subkey tree via reg.exe (handles subtree permissions smoothly)
                    // Use ArgumentList for safe parameter escaping to prevent argument injection
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true
                    };
                    psi.ArgumentList.Add("delete");
                    psi.ArgumentList.Add($@"HKCU\{subKeyPath}");
                    psi.ArgumentList.Add("/f");

                    bool processExited = false;
                    int exitCode = -1;
                    string errorMsg = string.Empty;

                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            processExited = proc.WaitForExit(3000);
                            if (processExited)
                            {
                                exitCode = proc.ExitCode;
                                errorMsg = proc.StandardError.ReadToEnd();
                            }
                            else
                            {
                                try { proc.Kill(); } catch { }
                                string timeoutMsg = LocalizationHelper.GetString("FileAssociationPage_Error_Timeout");
                                throw new TimeoutException(string.IsNullOrWhiteSpace(timeoutMsg) ? "The registry operation timed out." : timeoutMsg);
                            }
                        }
                    }

                    // 2. Also ensure C# registry cleanup in case of any edge cases
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, false);
                    }
                    catch (ArgumentException)
                    {
                        // Subkey already does not exist (success)
                    }

                    // 3. Strict verification: target registry key MUST NOT exist
                    using (var checkKey = Registry.CurrentUser.OpenSubKey(subKeyPath))
                    {
                        if (checkKey != null)
                        {
                            string detail = !string.IsNullOrWhiteSpace(errorMsg)
                                ? errorMsg.Trim()
                                : (exitCode != 0
                                    ? $"reg.exe code: {exitCode}"
                                    : (LocalizationHelper.GetString("FileAssociationPage_Error_KeyStillExists") ?? "Registry key still exists."));

                            string failedFormat = LocalizationHelper.GetString("FileAssociationPage_Error_DeleteFailed") ?? "Failed to delete association key: {0}";
                            throw new InvalidOperationException(string.Format(failedFormat, detail));
                        }
                    }

                    // 4. Notify Windows Explorer immediately to refresh icons and association cache
                    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                });

                // 5. Show success feedback
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Success_ResetTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Success_ResetCompleted"), ext), InfoBarSeverity.Success);
                LoggingService.Log($"Successfully cleared user association override for: .{ext}");
            }
            catch (Exception ex)
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Error_Title"), ex.Message, InfoBarSeverity.Error);
                LoggingService.Log($"Error resetting association for .{ext}: {ex.Message}");
            }
            finally
            {
                ResetAssociationBtn.IsEnabled = true;
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

        private async void RegistryNavBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            if (!IsValidExtension(ext))
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_InvalidExtTitle"), LocalizationHelper.GetString("FileAssociationPage_Warning_InvalidExt"), InfoBarSeverity.Warning);
                return;
            }

            if (Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExtTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExt"), ext), InfoBarSeverity.Warning);
                return;
            }

            string subKeyPath = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";
            string rootPrefix = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "電腦" : "Computer";

            RegistryNavBtn.IsEnabled = false;

            try
            {
                // 1. Copy the target path to clipboard as a reliable backup
                _ = ClipboardService.CopyTextAsync(subKeyPath);

                // 2. Check if regedit is already running
                Process[] existing = Process.GetProcessesByName("regedit");
                bool shouldRestart = false;

                if (existing.Length > 0)
                {
                    if (this.XamlRoot != null)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = LocalizationHelper.GetString("FileAssociationPage_RegRunning_Title") ?? "登錄編輯程式正在執行",
                            Content = string.Format(
                                LocalizationHelper.GetString("FileAssociationPage_RegRunning_Content") ?? "偵測到登錄編輯程式已開啟。若要由本工具自動定位至 .{0} 機碼，需要重新啟動登錄編輯程式。\n\n您也可以保留目前視窗，目標路徑已複製到您的剪貼簿，可直接貼上至網址列。",
                                ext),
                            PrimaryButtonText = LocalizationHelper.GetString("FileAssociationPage_RegRunning_Restart") ?? "重新啟動並定位",
                            CloseButtonText = LocalizationHelper.GetString("FileAssociationPage_RegRunning_Keep") ?? "保留目前視窗 (已複製路徑)",
                            XamlRoot = this.XamlRoot,
                            DefaultButton = ContentDialogButton.Close
                        };

                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            shouldRestart = true;
                        }
                        else
                        {
                            // User opted to keep existing windows without interruption
                            AdvancedModeExpander.IsExpanded = true;
                            NotificationService.Show(
                                LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopyTitle"), 
                                LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopy"), 
                                InfoBarSeverity.Informational);
                            return;
                        }
                    }
                    else
                    {
                        // Fallback: do not interrupt user's running processes
                        AdvancedModeExpander.IsExpanded = true;
                        NotificationService.Show(
                            LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopyTitle"), 
                            LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopy"), 
                            InfoBarSeverity.Informational);
                        return;
                    }
                }

                // 3. Set LastKey and optionally restart regedit
                string regeditFullPath = await Task.Run(() =>
                {
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

                    string fullPath = $"{rootPrefix}\\{subKeyPath}";

                    // Set LastKey in registry with the localized root prefix
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", "LastKey", fullPath);

                    // If user explicitly confirmed restart, gracefully close existing processes
                    if (shouldRestart)
                    {
                        Process[] toClose = Process.GetProcessesByName("regedit");
                        foreach (var p in toClose)
                        {
                            try
                            {
                                if (!p.CloseMainWindow())
                                {
                                    p.Kill();
                                }
                                p.WaitForExit(1000);
                            }
                            catch { }
                        }
                    }

                    return fullPath;
                });

                // 4. Start fresh regedit process
                Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    UseShellExecute = true
                });

                // 5. Automatically expand guidance card
                AdvancedModeExpander.IsExpanded = true;

                NotificationService.Show(LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigatedTitle"), string.Format(LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigated"), ext), InfoBarSeverity.Success);
                LoggingService.Log($"Launched Registry Editor with LastKey: {regeditFullPath}");
            }
            catch (Exception ex)
            {
                // Fallback copy to clipboard
                _ = ClipboardService.CopyTextAsync(
                    subKeyPath,
                    showNotification: true,
                    customNotificationTitle: LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopyTitle"),
                    customNotificationMessage: LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopy"));

                LoggingService.Log($"Error launching Registry Editor navigation: {ex.Message}");
            }
            finally
            {
                RegistryNavBtn.IsEnabled = true;
            }
        }
    }
}
