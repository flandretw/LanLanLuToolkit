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
using Windows.ApplicationModel.DataTransfer;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class ResetFileTypeDefaultsPage : Page
    {
        public ResetFileTypeDefaultsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        // Safety guardrail: Blacklist of critical system file extensions that must NEVER be modified or reset.
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

            if (ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ext.Any(char.IsWhiteSpace))
            {
                return false;
            }

            if (ext.Contains('"') || ext.Contains('\'') || ext.Contains('\\') || ext.Contains('/') ||
                ext.Contains("..") || ext.EndsWith('.'))
            {
                return false;
            }

            return ValidExtensionRegex.IsMatch(ext);
        }

        #region Win32 API Definitions

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint AssocQueryString(
            uint flags,
            uint str,
            string pszAssoc,
            string? pszExtra,
            [Out] StringBuilder? pszOut,
            ref uint pcchOut);

        private const uint ASSOCSTR_EXECUTABLE = 2;
        private const uint ASSOCSTR_FRIENDLYAPPNAME = 4;

        private static string GetAssocString(string dotExt, uint strType)
        {
            try
            {
                uint pcchOut = 0;
                AssocQueryString(0, strType, dotExt, null, null, ref pcchOut);
                if (pcchOut == 0) return string.Empty;

                StringBuilder sb = new StringBuilder((int)pcchOut);
                if (AssocQueryString(0, strType, dotExt, null, sb, ref pcchOut) == 0)
                {
                    return sb.ToString();
                }
            }
            catch { }
            return string.Empty;
        }

        #endregion

        #region File Picker & Quick Tag Handlers

        private void SelectFileBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero && App.MainWindow != null)
                {
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                }

                string? result = Win32FilePicker.ShowOpenDialog(
                    hwnd,
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_SelectFileDialogTitle") ?? "選取欲重設預設開啟方式的檔案");

                if (!string.IsNullOrEmpty(result))
                {
                    string ext = Path.GetExtension(result).TrimStart('.');
                    if (!string.IsNullOrWhiteSpace(ext))
                    {
                        ExtensionTextBox.Text = ext;
                    }
                    else
                    {
                        NotificationService.Show(
                            LocalizationHelper.GetString("ResetFileTypeDefaultsPage_NoExtTitle") ?? "無法識別副檔名",
                            LocalizationHelper.GetString("ResetFileTypeDefaultsPage_NoExtDesc") ?? "所選檔案沒有副檔名，無法進行預設關聯處理。",
                            InfoBarSeverity.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Select file error in ResetFileTypeDefaultsPage: {ex.Message}");
            }
        }

        private void QuickTagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ExtensionTextBox.Text = tag;
            }
        }

        #endregion

        #region Association Query & Status UI

        private void ExtensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');

            if (string.IsNullOrEmpty(ext))
            {
                EmptyStatusCard.Visibility = Visibility.Visible;
                ActiveStatusCard.Visibility = Visibility.Collapsed;
                ResetAssociationBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                return;
            }

            if (!IsValidExtension(ext))
            {
                EmptyStatusCard.Visibility = Visibility.Visible;
                ActiveStatusCard.Visibility = Visibility.Collapsed;
                ResetAssociationBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                return;
            }

            UpdateAssociationDisplay(ext);
        }

        private void UpdateAssociationDisplay(string ext)
        {
            EmptyStatusCard.Visibility = Visibility.Collapsed;
            ActiveStatusCard.Visibility = Visibility.Visible;

            TargetExtBadgeTextBlock.Text = "." + ext.ToUpperInvariant();

            bool isBlacklisted = Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase));
            if (isBlacklisted)
            {
                StatusHeaderTextBlock.Text = $".{ext} " + (LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Title") ?? "預設應用程式重設");
                StatusBadgeTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_Protected") ?? "系統核心保護";
                StatusAppNameTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExtTitle") ?? "系統核心保護中";
                StatusDetailTextBlock.Text = string.Format(LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExt") ?? "副檔名 .{0} 屬於關鍵系統元件，已鎖定保護。", ext);

                ResetAssociationBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                return;
            }

            RegistryNavBtn.IsEnabled = true;

            string dotExt = "." + ext;
            string subKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{dotExt}";

            bool hasCustomOverride = false;
            bool hasUserChoice = false;
            string progId = string.Empty;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(subKeyPath))
                {
                    if (key != null)
                    {
                        hasCustomOverride = true;
                        using (var ucKey = key.OpenSubKey("UserChoice"))
                        {
                            if (ucKey != null)
                            {
                                hasUserChoice = true;
                                progId = ucKey.GetValue("ProgId") as string ?? string.Empty;
                            }
                        }
                    }
                }
            }
            catch { }

            string friendly = GetAssocString(dotExt, ASSOCSTR_FRIENDLYAPPNAME);
            string exe = GetAssocString(dotExt, ASSOCSTR_EXECUTABLE);
            bool isGenericOpenWith = !string.IsNullOrEmpty(exe) && exe.EndsWith("openwith.exe", StringComparison.OrdinalIgnoreCase);

            StatusHeaderTextBlock.Text = $".{ext} " + (LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Title") ?? "預設應用程式重設");

            if (hasUserChoice)
            {
                // Explicit user choice override present - prime target for resetting
                StatusBadgeTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_CustomSet") ?? "已設定自訂預設值";
                string appDisplay = !string.IsNullOrWhiteSpace(friendly)
                    ? friendly
                    : (!string.IsNullOrWhiteSpace(exe) ? Path.GetFileName(exe) : (!string.IsNullOrWhiteSpace(progId) ? progId : "未知應用程式"));

                StatusAppNameTextBlock.Text = string.Format(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_AppFormat") ?? "預設開啟程式：{0}",
                    appDisplay);

                StatusDetailTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_Detail_HasChoice")
                    ?? "偵測到已設定此副檔名的個人預設開啟程式。按一下右側按鈕即可解除覆寫設定。";

                ResetAssociationBtn.IsEnabled = true;
            }
            else if (!isGenericOpenWith && !string.IsNullOrEmpty(exe))
            {
                // System or global default
                StatusBadgeTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_SystemDefault") ?? "系統全域預設";
                string appDisplay = !string.IsNullOrWhiteSpace(friendly) ? friendly : Path.GetFileName(exe);

                StatusAppNameTextBlock.Text = string.Format(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_AppFormat") ?? "預設開啟程式：{0}",
                    appDisplay);

                StatusDetailTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_Detail_System")
                    ?? "目前由 Windows 系統全域定義預設程式，未偵測到使用者個人自訂鎖定機碼。";

                // Allow reset if HKCU key exists, else keep disabled since nothing to clean in HKCU
                ResetAssociationBtn.IsEnabled = hasCustomOverride;
            }
            else
            {
                // Completely unassociated / clean state
                StatusBadgeTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_Unassociated") ?? "未指定";
                StatusAppNameTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_NoApp") ?? "未指定開啟程式";
                StatusDetailTextBlock.Text = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Status_Detail_NoChoice")
                    ?? "此副檔名尚未指定自訂預設應用程式。開啟時 Windows 將提示選擇應用程式。";

                ResetAssociationBtn.IsEnabled = hasCustomOverride;
            }
        }

        #endregion

        #region Actions: Reset & Settings & Regedit

        private async void ResetAssociationBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            if (!IsValidExtension(ext))
            {
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_InvalidExtTitle") ?? "副檔名格式無效",
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_InvalidExt") ?? "請輸入有效的副檔名。",
                    InfoBarSeverity.Warning);
                return;
            }

            if (Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExtTitle") ?? "系統核心保護中",
                    string.Format(LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExt") ?? "副檔名 .{0} 屬於關鍵系統元件，禁止清除。", ext),
                    InfoBarSeverity.Warning);
                return;
            }

            ResetAssociationBtn.IsEnabled = false;

            try
            {
                await Task.Run(() =>
                {
                    string subKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";

                    // 1. Delete user override subkey tree via reg.exe (handles subtree permissions smoothly)
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

                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            bool exited = proc.WaitForExit(3000);
                            if (!exited)
                            {
                                try { proc.Kill(); } catch { }
                                string timeoutMsg = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Error_Timeout") ?? "登錄機碼操作逾時。";
                                throw new TimeoutException(timeoutMsg);
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

                    // 3. Clean up any user classes override in HKCU\Software\Classes\.ext if present
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\.{ext}", false);
                    }
                    catch { }

                    // 4. Notify Windows Explorer immediately to refresh icons and association cache
                    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                });

                // 5. Update UI state to show newly unassociated status
                UpdateAssociationDisplay(ext);

                // 6. Show success feedback
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Success_ResetTitle") ?? "解除成功",
                    string.Format(LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Success_ResetCompleted") ?? "已解除副檔名 .{0} 的自訂預設應用程式，並還原為未指定狀態。", ext),
                    InfoBarSeverity.Success);

                LoggingService.Log($"Successfully cleared user association override for: .{ext}");
            }
            catch (Exception ex)
            {
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Error_Title") ?? "錯誤",
                    ex.Message,
                    InfoBarSeverity.Error);
                LoggingService.Log($"Error resetting association for .{ext}: {ex.Message}");
            }
            finally
            {
                UpdateAssociationDisplay(ext);
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
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Error_Title") ?? "錯誤",
                    ex.Message,
                    InfoBarSeverity.Error);
            }
        }

        private async void RegistryNavBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return;

            if (!IsValidExtension(ext))
            {
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_InvalidExtTitle") ?? "副檔名格式無效",
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_InvalidExt") ?? "請輸入有效的副檔名。",
                    InfoBarSeverity.Warning);
                return;
            }

            if (Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExtTitle") ?? "系統核心保護中",
                    string.Format(LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_CriticalExt") ?? "副檔名 .{0} 屬於關鍵系統元件，禁止操作。", ext),
                    InfoBarSeverity.Warning);
                return;
            }

            string subKeyPath = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";
            string rootPrefix = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "電腦" : "Computer";

            RegistryNavBtn.IsEnabled = false;

            try
            {
                _ = ClipboardService.CopyTextAsync(subKeyPath);

                Process[] existing = Process.GetProcessesByName("regedit");
                bool isRunning = existing.Length > 0;
                foreach (var p in existing) { p.Dispose(); }
                bool shouldRestart = false;

                if (isRunning)
                {
                    if (this.XamlRoot != null)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_RegRunning_Title") ?? "登錄編輯程式正在執行",
                            Content = string.Format(
                                LocalizationHelper.GetString("ResetFileTypeDefaultsPage_RegRunning_Content") ?? "偵測到登錄編輯程式已開啟。若要由本工具自動定位至 .{0} 機碼，需要重新啟動登錄編輯程式。\n\n您也可以保留目前視窗，目標路徑已複製到您的剪貼簿，可直接貼上至網址列。",
                                ext),
                            PrimaryButtonText = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_RegRunning_Restart") ?? "重新啟動並定位",
                            CloseButtonText = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_RegRunning_Keep") ?? "保留目前視窗",
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
                            NotificationService.Show(
                                LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopyTitle") ?? "已複製路徑",
                                LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopy") ?? "目標路徑已複製到剪貼簿，可在登錄編輯程式網址列貼上並按 Enter 即可定位。",
                                InfoBarSeverity.Informational);
                            return;
                        }
                    }
                    else
                    {
                        NotificationService.Show(
                            LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopyTitle") ?? "已複製路徑",
                            LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopy") ?? "目標路徑已複製到剪貼簿，可在登錄編輯程式網址列貼上並按 Enter 即可定位。",
                            InfoBarSeverity.Informational);
                        return;
                    }
                }

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
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", "LastKey", fullPath);

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
                            finally
                            {
                                p.Dispose();
                            }
                        }
                    }

                    return fullPath;
                });

                Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    UseShellExecute = true
                });

                NotificationService.Show(
                    LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Success_RegNavigatedTitle") ?? "定位成功",
                    string.Format(LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Success_RegNavigated") ?? "登錄編輯程式已成功巡覽至 .{0}。", ext),
                    InfoBarSeverity.Success);

                LoggingService.Log($"Launched Registry Editor with LastKey: {regeditFullPath}");
            }
            catch (Exception ex)
            {
                _ = ClipboardService.CopyTextAsync(
                    subKeyPath,
                    showNotification: true,
                    customNotificationTitle: LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopyTitle"),
                    customNotificationMessage: LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Warning_FallbackCopy"));

                LoggingService.Log($"Error launching Registry Editor navigation: {ex.Message}");
            }
            finally
            {
                RegistryNavBtn.IsEnabled = true;
            }
        }

        #endregion
    }
}
