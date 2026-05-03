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
        private readonly ResourceLoader _resourceLoader = new();
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
                    Title = _resourceLoader.GetString("SystemRepairPage_QuitTitle"),
                    Content = _resourceLoader.GetString("SystemRepairPage_QuitContent"),
                    PrimaryButtonText = _resourceLoader.GetString("SystemRepairPage_QuitConfirm"),
                    CloseButtonText = _resourceLoader.GetString("SystemRepairPage_QuitCancel"),
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

        private void AdvancedModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AdvancedToolsPanel != null)
            {
                AdvancedToolsPanel.Visibility = AdvancedModeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void RunAutoRepair_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdministrator())
            {
                AppendLog(_resourceLoader.GetString("SystemRepairPage_AdminRequired"));
                return;
            }

            SetAllButtonsEnabled(false);
            try
            {
                if (AutoModeComboBox.SelectedIndex == 0) // 完整修復
                {
                    AppendLog("\n=== 開始自動完整修復流程 ===");
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /CheckHealth");
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /ScanHealth");
                    await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /RestoreHealth");
                    await RunCommandInternalAsync("sfc", "/scannow");
                    AppendLog("\n=== 自動完整修復流程結束 ===");
                }
                else // 僅系統檔案檢查
                {
                    AppendLog("\n=== 開始系統檔案檢查流程 ===");
                    await RunCommandInternalAsync("sfc", "/scannow");
                    AppendLog("\n=== 系統檔案檢查流程結束 ===");
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
                AppendLog(_resourceLoader.GetString("SystemRepairPage_AdminRequired"));
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
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 執行中: {fileName} {arguments}");
            
            try
            {
                var oemEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

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
                        DispatcherQueue.TryEnqueue(() => {
                            AppendLog(e.Data);
                        });
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (e.Data != null)
                    {
                        DispatcherQueue.TryEnqueue(() => {
                            AppendLog("[錯誤] " + e.Data);
                        });
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                AppendLog(_resourceLoader.GetString("SystemRepairPage_Completed"));
            }
            catch (Exception ex)
            {
                AppendLog(_resourceLoader.GetString("SystemRepairPage_Error") + ": " + ex.Message);
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
                _resourceLoader.GetString("SystemRepairPage_CompletionInfoBar/Title"),
                _resourceLoader.GetString("SystemRepairPage_CompletionInfoBar/Message"),
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
            AdvancedModeToggle.IsEnabled = enabled;
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
            // 執行複製
            var dataPackage = new DataPackage();
            dataPackage.SetText(LogOutput.Text);
            Clipboard.SetContent(dataPackage);

            // 視覺回饋：變更為「已複製」狀態
            var originalGlyph = CopyBtnIcon.Glyph;
            var originalText = CopyBtnText.Text;

            CopyBtnIcon.Glyph = "\uE73E"; // CheckMark
            CopyBtnText.Text = "已複製";
            CopyLogBtn.IsEnabled = false;

            await Task.Delay(2000);

            // 恢復原始狀態
            CopyBtnIcon.Glyph = originalGlyph;
            CopyBtnText.Text = originalText;
            
            // 只有在日誌不為空的情況下才恢復啟用
            if (!string.IsNullOrEmpty(LogOutput.Text) && !_isProcessRunning)
            {
                CopyLogBtn.IsEnabled = true;
            }
        }

        private void AppendLog(string text)
        {
            // 如果目前還是預設提示文字，則直接取代
            if (LogOutput.Text == "等待指令執行...")
            {
                LogOutput.Text = text + "\n";
            }
            else
            {
                LogOutput.Text += text + "\n";
            }
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
