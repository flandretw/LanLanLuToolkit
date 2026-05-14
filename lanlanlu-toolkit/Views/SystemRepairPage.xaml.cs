using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class SystemRepairPage : Page
    {
        private bool _isProcessRunning = false;
        private bool _allowNavigation = false;

        public static bool IsAnyProcessRunning { get; private set; } = false;

        public SystemRepairPage()
        {
            this.InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }


        protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            if (_isProcessRunning && !_allowNavigation)
            {
                e.Cancel = true;
                var dialog = new ContentDialog
                {
                    Title = LocalizationHelper.GetString("SystemRepairPage_QuitTitle"),
                    Content = LocalizationHelper.GetString("SystemRepairPage_QuitContent"),
                    PrimaryButtonText = LocalizationHelper.GetString("SystemRepairPage_QuitConfirm"),
                    CloseButtonText = LocalizationHelper.GetString("SystemRepairPage_QuitCancel"),
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _allowNavigation = true;
                    if (e.NavigationMode == NavigationMode.Back)
                        this.Frame.GoBack();
                    else
                        this.Frame.Navigate(e.SourcePageType, e.Parameter);
                }
            }
            base.OnNavigatingFrom(e);
        }


        private async void RunAutoRepair_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdministrator())
            {
                AppendLog(LocalizationHelper.GetString("SystemRepairPage_AdminRequired"));
                return;
            }

            SetAllButtonsEnabled(false);
            try
            {
                if (AutoModeComboBox.SelectedIndex == 0) // Full Repair
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairStart"));
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /CheckHealth");
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /ScanHealth");
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /RestoreHealth");
                    await RunCommandInternalAsync("sfc", "/scannow");
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairEnd"));
                }
                else // System File Check only
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcStart"));
                    await RunCommandInternalAsync("sfc", "/scannow");
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcEnd"));
                }
            }
            finally
            {
                SetAllButtonsEnabled(true);
                NotifyCompletion();
            }
        }

        private async void RunScanHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleCommandAsync("Dism", "/Online /Cleanup-Image /ScanHealth");
        }

        private async void RunCheckHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleCommandAsync("Dism", "/Online /Cleanup-Image /CheckHealth");
        }

        private async void RunRestoreHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleCommandAsync("Dism", "/Online /Cleanup-Image /RestoreHealth");
        }

        private async void RunSfc_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleCommandAsync("sfc", "/scannow");
        }

        private async Task RunSingleCommandAsync(string fileName, string arguments)
        {
            if (!IsAdministrator())
            {
                AppendLog(LocalizationHelper.GetString("SystemRepairPage_AdminRequired"));
                return;
            }

            SetAllButtonsEnabled(false);
            try
            {
                await RunCommandInternalAsync(fileName, arguments);
            }
            finally
            {
                SetAllButtonsEnabled(true);
                NotifyCompletion();
            }
        }

        private async Task RunCommandInternalAsync(string fileName, string arguments)
        {
            _isProcessRunning = true;
            IsAnyProcessRunning = true;
            _allowNavigation = false;
            GlobalProgress.Visibility = Visibility.Visible;
            string executingFormat = LocalizationHelper.GetString("SystemRepairPage_Log_Executing");
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {executingFormat} {fileName} {arguments}");
            
            try
            {
                Encoding oemEncoding;
                try
                {
                    // SFC forces Unicode (UTF-16) output in some Windows versions
                    if (fileName.Equals("sfc", StringComparison.OrdinalIgnoreCase))
                    {
                        oemEncoding = Encoding.Unicode;
                    }
                    else
                    {
                        int cp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                        if (System.Globalization.CultureInfo.CurrentCulture.Name.Contains("TW") && (cp == 0 || cp == 65001))
                        {
                            oemEncoding = Encoding.GetEncoding(950);
                        }
                        else
                        {
                            oemEncoding = Encoding.GetEncoding(cp);
                        }
                    }
                }
                catch
                {
                    oemEncoding = Encoding.UTF8;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = oemEncoding,
                    StandardErrorEncoding = oemEncoding
                };

                using var process = new Process { StartInfo = startInfo };
                
                process.OutputDataReceived += (s, e) => {
                    if (e.Data != null)
                    {
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
                            AppendLog(e.Data);
                        });
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (e.Data != null)
                    {
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
                            string errorPrefix = LocalizationHelper.GetString("SystemRepairPage_Log_ErrorPrefix");
                            AppendLog($"{errorPrefix} " + e.Data);
                        });
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                AppendLog(LocalizationHelper.GetString("SystemRepairPage_Completed"));
            }
            catch (Exception ex)
            {
                AppendLog(LocalizationHelper.GetString("SystemRepairPage_Error") + ": " + ex.Message);
            }
            finally
            {
                GlobalProgress.Visibility = Visibility.Collapsed;
                _isProcessRunning = false;
                IsAnyProcessRunning = false;
            }
        }

        private void NotifyCompletion()
        {
            NotificationService.Show(
                LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar/Title"),
                LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar/Message"),
                InfoBarSeverity.Success
            );
        }

        private void SetAllButtonsEnabled(bool enabled)
        {
            AutoStartBtn.IsEnabled = enabled;
            AutoModeComboBox.IsEnabled = enabled;
            ScanHealthBtn.IsEnabled = enabled;
            CheckHealthBtn.IsEnabled = enabled;
            RestoreHealthBtn.IsEnabled = enabled;
            SfcBtn.IsEnabled = enabled;
            AdvancedExpander.IsEnabled = enabled;
            ClearLogBtn.IsEnabled = enabled;
            CopyLogBtn.IsEnabled = enabled;
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogOutput.Text = string.Empty;
            ClearLogBtn.IsEnabled = false;
            CopyLogBtn.IsEnabled = false;
        }

        private async void CopyLogBtn_Click(object sender, RoutedEventArgs e)
        {
            // Execute copy
            var dataPackage = new DataPackage();
            dataPackage.SetText(LogOutput.Text);
            Clipboard.SetContent(dataPackage);

            // Visual feedback: change to "Copied" state
            var originalGlyph = CopyBtnIcon.Glyph;
            var originalText = CopyBtnText.Text;

            CopyBtnIcon.Glyph = "\uE73E"; // CheckMark
            CopyBtnText.Text = LocalizationHelper.GetString("SystemRepairPage_Copied");
            CopyLogBtn.IsEnabled = false;

            await Task.Delay(2000);

            // Restore original state
            CopyBtnIcon.Glyph = originalGlyph;
            CopyBtnText.Text = originalText;
            
            // Only re-enable if log is not empty and no process is running
            if (!string.IsNullOrEmpty(LogOutput.Text) && !_isProcessRunning)
            {
                CopyLogBtn.IsEnabled = true;
            }
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Remove Null characters (\0) which cause clipboard issues or garbled text
            text = text.Replace("\0", string.Empty);
            if (string.IsNullOrEmpty(text)) return;

            // If still showing default placeholder, replace it directly
            string placeholder = LocalizationHelper.GetString("SystemRepairPage_WaitingPlaceholder");
            if (LogOutput.Text == placeholder)
            {
                LogOutput.Text = text + "\n";
            }
            else
            {
                LogOutput.Text += text + "\n";
            }
            
            // Auto-scroll to the bottom
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
