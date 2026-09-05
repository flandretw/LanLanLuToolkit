using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class SystemRepairPage : Page
    {
        private bool _isProcessRunning = false;
        private bool _allowNavigation = false;
        private DispatcherTimer? _timer;
        private readonly Stopwatch _stopwatch = new();
        private readonly StepStatus[] _stepStatuses = new StepStatus[5];

        public static bool IsAnyProcessRunning { get; private set; } = false;

        private enum StepStatus
        {
            Pending,
            Running,
            Success,
            Repaired,
            Failed
        }

        public SystemRepairPage()
        {
            this.InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            this.Loaded += SystemRepairPage_Loaded;
        }

        private void SystemRepairPage_Loaded(object sender, RoutedEventArgs e)
        {
            ResetToIdleState();
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

        #region State & Visual Management

        private void ResetToIdleState()
        {
            HeroActionControls.Visibility = Visibility.Visible;
            HeroTimerContainer.Visibility = Visibility.Collapsed;
            HeroProgressContainer.Visibility = Visibility.Collapsed;
            MainProgressBar.Value = 0;
            HeroPercentageText.Text = "0%";
            HeroStepStatusText.Text = string.Empty;

            ResetAllStepVisuals();
        }

        private void ResetAllStepVisuals()
        {
            for (int i = 1; i <= 4; i++)
            {
                UpdateStepVisual(i, StepStatus.Pending);
            }
        }

        private void UpdateStepVisual(int step, StepStatus status, double percent = -1)
        {
            if (step >= 1 && step <= 4) { _stepStatuses[step] = status; }
            var (icon, ring, badge, statusText, pbar) = GetStepControls(step);
            if (icon == null || ring == null || badge == null || statusText == null) return;

            switch (status)
            {
                case StepStatus.Pending:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE823"; // Clock
                    icon.ClearValue(FontIcon.ForegroundProperty);
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Pending");
                    statusText.ClearValue(TextBlock.ForegroundProperty);
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) { pbar.Visibility = Visibility.Collapsed; pbar.Value = 0; }
                    break;

                case StepStatus.Running:
                    icon.Visibility = Visibility.Collapsed;
                    ring.Visibility = Visibility.Visible;
                    ring.IsActive = true;
                    statusText.Text = percent >= 0 ? $"{percent:0}%" : LocalizationHelper.GetString("SystemRepairPage_Step_Running");
                    statusText.ClearValue(TextBlock.ForegroundProperty);
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null)
                    {
                        pbar.Visibility = Visibility.Visible;
                        if (percent >= 0) pbar.Value = percent;
                    }
                    break;

                case StepStatus.Success:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE73E"; // Checkmark
                    icon.Foreground = GetBrush("SystemFillColorSuccessBrush", Color.FromArgb(255, 16, 124, 65));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Success");
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;

                case StepStatus.Repaired:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE7BA"; // Shield / Caution
                    icon.Foreground = GetBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 216, 59, 1));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Repaired");
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;

                case StepStatus.Failed:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uEB90"; // Error badge
                    icon.Foreground = GetBrush("SystemFillColorCriticalBrush", Color.FromArgb(255, 196, 43, 28));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Failed");
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void UpdateStepProgress(int step, double percent)
        {
            if (step < 1 || step > 4 || _stepStatuses[step] != StepStatus.Running) return;
            var (_, _, _, statusText, pbar) = GetStepControls(step);
            if (statusText != null)
            {
                statusText.Text = $"{percent:0}%";
            }
            if (pbar != null)
            {
                pbar.Visibility = Visibility.Visible;
                pbar.Value = percent;
            }
        }

        private (FontIcon? icon, ProgressRing? ring, Border? badge, TextBlock? statusText, ProgressBar? pbar) GetStepControls(int step)
        {
            return step switch
            {
                1 => (Step1Icon, Step1Ring, Step1Badge, Step1StatusText, null),
                2 => (Step2Icon, Step2Ring, Step2Badge, Step2StatusText, Step2ProgressBar),
                3 => (Step3Icon, Step3Ring, Step3Badge, Step3StatusText, Step3ProgressBar),
                4 => (Step4Icon, Step4Ring, Step4Badge, Step4StatusText, Step4ProgressBar),
                _ => (null, null, null, null, null)
            };
        }

        private static Brush GetBrush(string resourceKey, Color fallbackColor)
        {
            if (Application.Current.Resources.TryGetValue(resourceKey, out var res) && res is Brush b)
            {
                return b;
            }
            return new SolidColorBrush(fallbackColor);
        }

        #endregion

        #region Timer Management

        private void StartTimer()
        {
            _stopwatch.Restart();
            UpdateTimerDisplay(isCompleted: false);
            HeroTimerContainer.Visibility = Visibility.Visible;

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                UpdateTimerDisplay(isCompleted: false);
            };
            _timer.Start();
        }

        private void StopTimer(bool isCompleted = true)
        {
            _timer?.Stop();
            _stopwatch.Stop();
            if (isCompleted)
            {
                UpdateTimerDisplay(isCompleted: true);
                HeroTimerContainer.Visibility = Visibility.Visible;
            }
        }

        private void UpdateTimerDisplay(bool isCompleted)
        {
            string key = isCompleted ? "SystemRepairPage_Hero_TotalTimeFormat" : "SystemRepairPage_Hero_ElapsedTimeFormat";
            string format = LocalizationHelper.GetString(key);
            HeroTimerText.Text = string.Format(format, _stopwatch.Elapsed.ToString(@"mm\:ss"));
        }

        #endregion

        #region Execution & Progress Parsing

        private async void RunAutoRepair_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdministrator())
            {
                AppendLog(LocalizationHelper.GetString("SystemRepairPage_AdminRequired"));
                return;
            }

            SetAllButtonsEnabled(false);
            ResetAllStepVisuals();

            // HeroActionControls stays visible and disabled
            HeroProgressContainer.Visibility = Visibility.Visible;
            MainProgressBar.Value = 0;
            HeroPercentageText.Text = "0%";
            StartTimer();

            try
            {
                if (AutoModeComboBox.SelectedIndex == 0) // Full Repair (4 steps)
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairStart"));

                    // Step 1: CheckHealth
                    UpdateStepVisual(1, StepStatus.Running);
                    HeroStepStatusText.Text = $"{LocalizationHelper.GetString("SystemRepairPage_CheckHealth_Title/Text")} (1/4)";
                    var res1 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /CheckHealth", currentStep: 1, totalSteps: 4);
                    UpdateStepVisual(1, res1.HasCorruption ? StepStatus.Repaired : (res1.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                    // Step 2: ScanHealth
                    UpdateStepVisual(2, StepStatus.Running);
                    HeroStepStatusText.Text = $"{LocalizationHelper.GetString("SystemRepairPage_ScanHealth_Title/Text")} (2/4)";
                    var res2 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /ScanHealth", currentStep: 2, totalSteps: 4);
                    UpdateStepVisual(2, res2.HasCorruption ? StepStatus.Repaired : (res2.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                    // Step 3: RestoreHealth
                    UpdateStepVisual(3, StepStatus.Running);
                    HeroStepStatusText.Text = $"{LocalizationHelper.GetString("SystemRepairPage_RestoreHealth_Title/Text")} (3/4)";
                    var res3 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /RestoreHealth", currentStep: 3, totalSteps: 4);
                    UpdateStepVisual(3, res3.HasCorruption ? StepStatus.Repaired : (res3.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                    // Step 4: Sfc Scannow
                    UpdateStepVisual(4, StepStatus.Running);
                    HeroStepStatusText.Text = $"{LocalizationHelper.GetString("SystemRepairPage_SfcScannow_Title/Text")} (4/4)";
                    var res4 = await RunCommandInternalAsync("sfc", "/scannow", currentStep: 4, totalSteps: 4);
                    UpdateStepVisual(4, res4.HasCorruption ? StepStatus.Repaired : (res4.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairEnd"));
                }
                else // SFC Only
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcStart"));

                    UpdateStepVisual(4, StepStatus.Running);
                    HeroStepStatusText.Text = $"{LocalizationHelper.GetString("SystemRepairPage_SfcScannow_Title/Text")} (1/1)";
                    var res = await RunCommandInternalAsync("sfc", "/scannow", currentStep: 1, totalSteps: 1);
                    UpdateStepVisual(4, res.HasCorruption ? StepStatus.Repaired : (res.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcEnd"));
                }

                // Final Completed State
                MainProgressBar.Value = 100;
                HeroPercentageText.Text = "100%";
                HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Completed");
                StopTimer(isCompleted: true);
                NotifyCompletion();
            }
            finally
            {
                Step2ProgressBar.Visibility = Visibility.Collapsed;
                Step3ProgressBar.Visibility = Visibility.Collapsed;
                Step4ProgressBar.Visibility = Visibility.Collapsed;
                HeroActionControls.Visibility = Visibility.Visible;
                SetAllButtonsEnabled(true);
            }
        }

        private async void RunCheckHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleStepAsync(1, "Dism", "/Online /Cleanup-Image /CheckHealth", LocalizationHelper.GetString("SystemRepairPage_CheckHealth_Title/Text"));
        }

        private async void RunScanHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleStepAsync(2, "Dism", "/Online /Cleanup-Image /ScanHealth", LocalizationHelper.GetString("SystemRepairPage_ScanHealth_Title/Text"));
        }

        private async void RunRestoreHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleStepAsync(3, "Dism", "/Online /Cleanup-Image /RestoreHealth", LocalizationHelper.GetString("SystemRepairPage_RestoreHealth_Title/Text"));
        }

        private async void RunSfc_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleStepAsync(4, "sfc", "/scannow", LocalizationHelper.GetString("SystemRepairPage_SfcScannow_Title/Text"));
        }

        private async Task RunSingleStepAsync(int step, string fileName, string arguments, string title)
        {
            if (!IsAdministrator())
            {
                AppendLog(LocalizationHelper.GetString("SystemRepairPage_AdminRequired"));
                return;
            }

            SetAllButtonsEnabled(false);
            // HeroActionControls stays visible and disabled
            HeroProgressContainer.Visibility = Visibility.Visible;
            MainProgressBar.Value = 0;
            HeroPercentageText.Text = "0%";
            HeroStepStatusText.Text = title;
            StartTimer();

            UpdateStepVisual(step, StepStatus.Running);

            try
            {
                var res = await RunCommandInternalAsync(fileName, arguments, currentStep: 1, totalSteps: 1, targetStepIndex: step);
                UpdateStepVisual(step, res.HasCorruption ? StepStatus.Repaired : (res.IsSuccess ? StepStatus.Success : StepStatus.Failed));

                MainProgressBar.Value = 100;
                HeroPercentageText.Text = "100%";
                HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Completed");
                StopTimer(isCompleted: true);
                NotifyCompletion();
            }
            finally
            {
                Step2ProgressBar.Visibility = Visibility.Collapsed;
                Step3ProgressBar.Visibility = Visibility.Collapsed;
                Step4ProgressBar.Visibility = Visibility.Collapsed;
                HeroActionControls.Visibility = Visibility.Visible;
                SetAllButtonsEnabled(true);
            }
        }

        private class CommandResult
        {
            public bool IsSuccess { get; set; } = true;
            public bool HasCorruption { get; set; } = false;
        }

        private async Task<CommandResult> RunCommandInternalAsync(string fileName, string arguments, int currentStep, int totalSteps, int? targetStepIndex = null)
        {
            _isProcessRunning = true;
            IsAnyProcessRunning = true;
            _allowNavigation = false;

            int activeVisualStep = targetStepIndex ?? currentStep;
            var result = new CommandResult();

            string executingFormat = LocalizationHelper.GetString("SystemRepairPage_Log_Executing");
            string logMessage = $"{executingFormat} {fileName} {arguments}";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {logMessage}");
            LoggingService.Log($"RepairTool: {logMessage}");

            try
            {
                Encoding oemEncoding;
                try
                {
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

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        string line = e.Data;
                        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                        {
                            AppendLog(line);
                            ParseLineProgress(line, currentStep, totalSteps, activeVisualStep, result);
                        });
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        string line = e.Data;
                        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                        {
                            string errorPrefix = LocalizationHelper.GetString("SystemRepairPage_Log_ErrorPrefix");
                            AppendLog($"{errorPrefix} " + line);
                        });
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                LoggingService.Log($"RepairTool: {fileName} execution finished with exit code {process.ExitCode}.");

                if (process.ExitCode != 0 && process.ExitCode != 1)
                {
                    result.IsSuccess = false;
                }

                AppendLog(LocalizationHelper.GetString("SystemRepairPage_Completed"));
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                string errorFormat = LocalizationHelper.GetString("SystemRepairPage_ErrorFormat");
                string errorMsg = string.Format(errorFormat, ex.Message);
                AppendLog(errorMsg);
                LoggingService.Log($"RepairTool ERROR: {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                _isProcessRunning = false;
                IsAnyProcessRunning = false;
            }

            return result;
        }

        private void ParseLineProgress(string line, int currentStep, int totalSteps, int activeVisualStep, CommandResult result)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // Check for keywords indicating corruption or repair
            if (line.Contains("發現損毀檔案並已成功修復", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("可修復的", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("is repairable", StringComparison.OrdinalIgnoreCase))
            {
                result.HasCorruption = true;
            }

            // Regex 1: Match DISM bracketed or generic percentage e.g. [=== 64.0% ===] or 64.0%
            var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p))
            {
                UpdateStepProgress(activeVisualStep, p);

                double stepBase = (currentStep - 1) * 100.0;
                double overall = Math.Clamp((stepBase + p) / totalSteps, 0, 100);
                MainProgressBar.Value = overall;
                HeroPercentageText.Text = $"{overall:0}%";
            }
        }

        #endregion

        #region Helpers & Actions

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
            var dataPackage = new DataPackage();
            dataPackage.SetText(LogOutput.Text);
            Clipboard.SetContent(dataPackage);

            var originalGlyph = CopyBtnIcon.Glyph;
            var originalText = CopyBtnText.Text;

            CopyBtnIcon.Glyph = "\uE73E"; // Checkmark
            CopyBtnText.Text = LocalizationHelper.GetString("SystemRepairPage_Copied");
            CopyLogBtn.IsEnabled = false;

            await Task.Delay(2000);

            CopyBtnIcon.Glyph = originalGlyph;
            CopyBtnText.Text = originalText;

            if (!string.IsNullOrEmpty(LogOutput.Text) && !_isProcessRunning)
            {
                CopyLogBtn.IsEnabled = true;
            }
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            text = text.Replace("\0", string.Empty);
            if (string.IsNullOrEmpty(text)) return;

            string placeholder = LocalizationHelper.GetString("SystemRepairPage_WaitingPlaceholder");
            if (LogOutput.Text == placeholder)
            {
                LogOutput.Text = text + "\n";
            }
            else
            {
                LogOutput.Text += text + "\n";
            }

            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);

            ClearLogBtn.IsEnabled = true;
            CopyLogBtn.IsEnabled = true;
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        #endregion
    }
}