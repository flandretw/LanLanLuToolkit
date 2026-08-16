using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        // Disabling these (especially .exe or .lnk) will lead to bricking the OS.
        private static readonly string[] CriticalSystemExtensions = new string[]
        {
            "exe", "lnk", "bat", "cmd", "msi", "reg", "sys", "dll", "cpl", "msc", "com", "scr"
        };

        private void ExtensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim().ToLower().TrimStart('.');

            if (string.IsNullOrEmpty(ext))
            {
                SystemDialogBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                FeedbackInfoBar.IsOpen = false;
                return;
            }

            // Check if user entered a critical system extension
            bool isBlacklisted = Array.Exists(CriticalSystemExtensions, s => s.Equals(ext, StringComparison.OrdinalIgnoreCase));

            if (isBlacklisted)
            {
                SystemDialogBtn.IsEnabled = false;
                RegistryNavBtn.IsEnabled = false;
                ShowFeedback(string.Format(LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExt"), ext), InfoBarSeverity.Warning, LocalizationHelper.GetString("FileAssociationPage_Warning_CriticalExtTitle"));
            }
            else
            {
                SystemDialogBtn.IsEnabled = true;
                RegistryNavBtn.IsEnabled = true;
                FeedbackInfoBar.IsOpen = false; // Hide warning when safe
            }
        }

        private void SystemDialogBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(ext)) return;

            // Normalize: ensure it starts with a dot
            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            try
            {
                // Create a temporary file with the target extension so that the OpenWith dialog operates on it
                string tempFileName = $"temp_association_reset_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
                string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

                File.WriteAllText(tempFilePath, string.Empty);

                // Run rundll32 shell32.dll,OpenAs_RunDLL to open the system dialog
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32.dll,OpenAs_RunDLL \"{tempFilePath}\"",
                    UseShellExecute = true
                };

                Process.Start(psi);

                ShowFeedback(LocalizationHelper.GetString("FileAssociationPage_Success_DialogOpened"), InfoBarSeverity.Success, LocalizationHelper.GetString("FileAssociationPage_Success_Title"));
                LoggingService.Log($"Opened system association dialog for extension: {ext}");
            }
            catch (Exception ex)
            {
                ShowFeedback(string.Format(LocalizationHelper.GetString("FileAssociationPage_Error_DialogOpenFailed"), ex.Message), InfoBarSeverity.Error, LocalizationHelper.GetString("FileAssociationPage_Error_Title"));
                LoggingService.Log($"Error launching system association dialog: {ex.Message}");
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const uint WM_SETTEXT = 0x000C;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_RETURN = 0x0D;

        private async void RegistryNavBtn_Click(object sender, RoutedEventArgs e)
        {
            string ext = ExtensionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(ext)) return;

            ext = ext.TrimStart('.');

            // Target path in registry for address bar
            string targetPath = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{ext}";

            try
            {
                // Show visual feedback that we are launching/positioning
                ShowFeedback(LocalizationHelper.GetString("FileAssociationPage_Info_Locating"), InfoBarSeverity.Informational, LocalizationHelper.GetString("FileAssociationPage_Info_LocatingTitle"));

                Process[] processes = Process.GetProcessesByName("regedit");
                IntPtr hwndRegedit = IntPtr.Zero;

                if (processes.Length == 0)
                {
                    // Launch new Registry Editor
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "regedit.exe",
                        UseShellExecute = true
                    });

                    // Poll to locate the newly created window (up to 3 seconds)
                    for (int i = 0; i < 20; i++)
                    {
                        await System.Threading.Tasks.Task.Delay(150);
                        hwndRegedit = FindWindow("RegEdit_RegEdit", null);
                        if (hwndRegedit != IntPtr.Zero) break;
                    }
                }
                else
                {
                    hwndRegedit = FindWindow("RegEdit_RegEdit", null);
                }

                if (hwndRegedit == IntPtr.Zero)
                {
                    ShowFeedback(LocalizationHelper.GetString("FileAssociationPage_Warning_LocateFailed"), InfoBarSeverity.Warning, LocalizationHelper.GetString("FileAssociationPage_Warning_LocateFailedTitle"));
                    return;
                }

                // Bring regedit window to the foreground
                SetForegroundWindow(hwndRegedit);
                await System.Threading.Tasks.Task.Delay(300); // Wait for UI to initialize/focus

                // Locate the Address Bar: RegEdit_RegEdit -> ReBarWindow32 -> ComboBox -> Edit
                IntPtr hwndRebar = FindWindowEx(hwndRegedit, IntPtr.Zero, "ReBarWindow32", null);
                IntPtr hwndCombo = IntPtr.Zero;
                IntPtr hwndEdit = IntPtr.Zero;

                if (hwndRebar != IntPtr.Zero)
                {
                    hwndCombo = FindWindowEx(hwndRebar, IntPtr.Zero, "ComboBox", null);
                    if (hwndCombo != IntPtr.Zero)
                    {
                        hwndEdit = FindWindowEx(hwndCombo, IntPtr.Zero, "Edit", null);
                    }
                }

                if (hwndEdit == IntPtr.Zero)
                {
                    // Fallback to searching without Rebar just in case Win11 modifies the structure slightly
                    hwndCombo = FindWindowEx(hwndRegedit, IntPtr.Zero, "ComboBox", null);
                    if (hwndCombo != IntPtr.Zero)
                    {
                        hwndEdit = FindWindowEx(hwndCombo, IntPtr.Zero, "Edit", null);
                    }
                }

                if (hwndEdit != IntPtr.Zero)
                {
                    // Inject target path directly into Regedit's address bar
                    SendMessage(hwndEdit, WM_SETTEXT, IntPtr.Zero, targetPath);
                    await System.Threading.Tasks.Task.Delay(150);

                    // Press ENTER key to navigate
                    PostMessage(hwndEdit, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                    PostMessage(hwndEdit, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);

                    // Show step-by-step instructions card in UI
                    RegistryInstructionsCard.Visibility = Visibility.Visible;
                    ShowFeedback(string.Format(LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigated"), ext), InfoBarSeverity.Success, LocalizationHelper.GetString("FileAssociationPage_Success_RegNavigatedTitle"));
                    LoggingService.Log($"UI Injected Registry Editor navigation to: {targetPath}");
                }
                else
                {
                    // Fallback to copy-paste mechanism if address bar is not visible/hidden by user
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(targetPath);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

                    RegistryInstructionsCard.Visibility = Visibility.Visible;
                    ShowFeedback(LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopy"), InfoBarSeverity.Warning, LocalizationHelper.GetString("FileAssociationPage_Warning_FallbackCopyTitle"));
                    LoggingService.Log($"Registry Address bar not found. Copied path: {targetPath} to clipboard.");
                }
            }
            catch (Exception ex)
            {
                ShowFeedback(string.Format(LocalizationHelper.GetString("FileAssociationPage_Error_RegLaunchFailed"), ex.Message), InfoBarSeverity.Error, LocalizationHelper.GetString("FileAssociationPage_Error_RegLaunchFailedTitle"));
                LoggingService.Log($"Error launching Registry Editor UI navigation: {ex.Message}");
            }
        }

        private void ShowFeedback(string message, InfoBarSeverity severity, string title)
        {
            FeedbackInfoBar.Title = title;
            FeedbackInfoBar.Message = message;
            FeedbackInfoBar.Severity = severity;
            FeedbackInfoBar.IsOpen = true;
        }
    }
}
