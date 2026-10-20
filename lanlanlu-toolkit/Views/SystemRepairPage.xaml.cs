using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        private readonly StringBuilder _logBuilder = new();

        public static bool IsAnyProcessRunning { get; private set; } = false;

        private enum StepStatus
        {
            Idle,
            Pending,
            Running,
            Success,
            Repaired,
            Corrupted,
            Unknown,
            Failed
        }

        private enum ImageHealthAssessment
        {
            NotApplicable,
            Healthy,
            Repairable,
            NonRepairable,
            Unknown
        }

        public SystemRepairPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            this.Loaded += SystemRepairPage_Loaded;
            this.Unloaded += SystemRepairPage_Unloaded;
        }

        private void SystemRepairPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isProcessRunning)
            {
                ResetToIdleState();
            }
        }

        private void SystemRepairPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isProcessRunning)
            {
                _timer?.Stop();
            }
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
                UpdateStepVisual(i, StepStatus.Idle);
            }
        }

        private static string GetStepDefaultGlyph(int step) => step switch
        {
            1 => "\uE721", // CheckHealth
            2 => "\uE8FE", // ScanHealth (Scan)
            3 => "\uEBD3", // RestoreHealth (CloudDownload)
            4 => "\uEA18", // Sfc Scannow (Shield)
            _ => "\uE823"
        };

        private void UpdateStepVisual(int step, StepStatus status, double percent = -1)
        {
            if (step >= 1 && step <= 4) { _stepStatuses[step] = status; }
            var (icon, ring, badge, statusText, pbar) = GetStepControls(step);
            if (icon == null || ring == null || badge == null || statusText == null) return;

            switch (status)
            {
                case StepStatus.Idle:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = GetStepDefaultGlyph(step);
                    icon.ClearValue(FontIcon.ForegroundProperty);
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    badge.Visibility = Visibility.Collapsed;
                    if (pbar != null) { pbar.Visibility = Visibility.Collapsed; pbar.Value = 0; }
                    break;

                case StepStatus.Pending:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE823"; // Clock during execution queue
                    icon.ClearValue(FontIcon.ForegroundProperty);
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    badge.Visibility = Visibility.Visible;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Pending");
                    statusText.ClearValue(TextBlock.ForegroundProperty);
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) { pbar.Visibility = Visibility.Collapsed; pbar.Value = 0; }
                    break;

                case StepStatus.Running:
                    icon.Visibility = Visibility.Collapsed;
                    ring.Visibility = Visibility.Visible;
                    ring.IsActive = true;
                    badge.Visibility = Visibility.Visible;
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
                    badge.Visibility = Visibility.Visible;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Success");
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;

                case StepStatus.Corrupted:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE7BA"; // Warning shield
                    icon.Foreground = GetBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 216, 59, 1));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    badge.Visibility = Visibility.Visible;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Corrupted") ?? "發現損毀";
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;

                case StepStatus.Repaired:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE73E"; // Checkmark
                    icon.Foreground = GetBrush("SystemFillColorSuccessBrush", Color.FromArgb(255, 16, 124, 65));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    badge.Visibility = Visibility.Visible;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Repaired");
                    statusText.Foreground = icon.Foreground;
                    badge.ClearValue(Border.BackgroundProperty);
                    if (pbar != null) pbar.Visibility = Visibility.Collapsed;
                    break;

                case StepStatus.Unknown:
                    icon.Visibility = Visibility.Visible;
                    icon.Glyph = "\uE9CE"; // Help / Unknown circle
                    icon.Foreground = GetBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 216, 59, 1));
                    ring.Visibility = Visibility.Collapsed;
                    ring.IsActive = false;
                    badge.Visibility = Visibility.Visible;
                    statusText.Text = LocalizationHelper.GetString("SystemRepairPage_Step_Unknown") ?? "狀態不明";
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
                    badge.Visibility = Visibility.Visible;
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

        private static string FormatStepStatus(int currentStep, int totalSteps, string stepTitle)
        {
            var format = LocalizationHelper.GetString("SystemRepairPage_Step_Format") ?? "步驟 {0}/{1}：正在{2}";
            return string.Format(format, currentStep, totalSteps, stepTitle);
        }

        #endregion

        #region Execution & Progress Parsing

        private static StepStatus DetermineStepStatus(int step, CommandResult res)
        {
            // Failure ALWAYS takes precedence
            if (!res.IsSuccess)
            {
                return StepStatus.Failed;
            }

            if (step == 1 || step == 2) // Inspection steps (CheckHealth, ScanHealth)
            {
                return res.HealthAssessment switch
                {
                    ImageHealthAssessment.Healthy => StepStatus.Success,
                    ImageHealthAssessment.Repairable => StepStatus.Corrupted,
                    ImageHealthAssessment.NonRepairable => StepStatus.Failed,
                    _ => StepStatus.Unknown
                };
            }
            else // Repair steps (RestoreHealth, SFC)
            {
                return res.HasRepaired ? StepStatus.Repaired : StepStatus.Success;
            }
        }

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

            var stepResults = new List<(string Title, CommandResult Result)>();

            try
            {
                if (AutoModeComboBox.SelectedIndex == 0) // Full Repair (4 steps)
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairStart"));

                    // Queue steps 2, 3, 4 as Pending
                    UpdateStepVisual(2, StepStatus.Pending);
                    UpdateStepVisual(3, StepStatus.Pending);
                    UpdateStepVisual(4, StepStatus.Pending);

                    // Step 1: CheckHealth
                    string t1 = LocalizationHelper.GetString("SystemRepairPage_CheckHealth_Title.Text") ?? "CheckHealth";
                    UpdateStepVisual(1, StepStatus.Running);
                    HeroStepStatusText.Text = FormatStepStatus(1, 4, t1);
                    var res1 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /CheckHealth", currentStep: 1, totalSteps: 4);
                    UpdateStepVisual(1, DetermineStepStatus(1, res1));
                    stepResults.Add((t1, res1));

                    // Step 2: ScanHealth
                    string t2 = LocalizationHelper.GetString("SystemRepairPage_ScanHealth_Title.Text") ?? "ScanHealth";
                    UpdateStepVisual(2, StepStatus.Running);
                    HeroStepStatusText.Text = FormatStepStatus(2, 4, t2);
                    var res2 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /ScanHealth", currentStep: 2, totalSteps: 4);
                    UpdateStepVisual(2, DetermineStepStatus(2, res2));
                    stepResults.Add((t2, res2));

                    // Step 3: RestoreHealth
                    string t3 = LocalizationHelper.GetString("SystemRepairPage_RestoreHealth_Title.Text") ?? "RestoreHealth";
                    UpdateStepVisual(3, StepStatus.Running);
                    HeroStepStatusText.Text = FormatStepStatus(3, 4, t3);
                    var res3 = await RunCommandInternalAsync("Dism", "/Online /Cleanup-Image /RestoreHealth", currentStep: 3, totalSteps: 4);
                    UpdateStepVisual(3, DetermineStepStatus(3, res3));
                    stepResults.Add((t3, res3));

                    // Step 4: Sfc Scannow
                    string t4 = LocalizationHelper.GetString("SystemRepairPage_SfcScannow_Title.Text") ?? "sfc /scannow";
                    UpdateStepVisual(4, StepStatus.Running);
                    HeroStepStatusText.Text = FormatStepStatus(4, 4, t4);
                    var res4 = await RunCommandInternalAsync("sfc", "/scannow", currentStep: 4, totalSteps: 4);
                    UpdateStepVisual(4, DetermineStepStatus(4, res4));
                    stepResults.Add((t4, res4));

                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_FullRepairEnd"));
                }
                else // SFC Only
                {
                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcStart"));

                    string tSfc = LocalizationHelper.GetString("SystemRepairPage_SfcScannow_Title.Text") ?? "sfc /scannow";
                    UpdateStepVisual(4, StepStatus.Running);
                    HeroStepStatusText.Text = FormatStepStatus(1, 1, tSfc);
                    var res = await RunCommandInternalAsync("sfc", "/scannow", currentStep: 1, totalSteps: 1);
                    UpdateStepVisual(4, DetermineStepStatus(4, res));
                    stepResults.Add((tSfc, res));

                    AppendLog("\n" + LocalizationHelper.GetString("SystemRepairPage_Log_SfcEnd"));
                }

                // Aggregate and evaluate results across all steps
                StopTimer(isCompleted: true);
                ApplyAggregatedResults(stepResults);
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
                var finalStatus = DetermineStepStatus(step, res);
                UpdateStepVisual(step, finalStatus);

                StopTimer(isCompleted: true);

                if (!res.IsSuccess)
                {
                    MainProgressBar.Value = 0;
                    HeroPercentageText.Text = "0%";
                    HeroStepStatusText.Text = string.Format(LocalizationHelper.GetString("SystemRepairPage_Status_SingleFailed"), title);

                    NotificationService.Show(
                        LocalizationHelper.GetString("SystemRepairPage_Notification_Failed_Title"),
                        string.Format(LocalizationHelper.GetString("SystemRepairPage_Notification_SingleFailed_Message"), title),
                        InfoBarSeverity.Error
                    );
                    return;
                }

                MainProgressBar.Value = 100;
                HeroPercentageText.Text = "100%";

                if (step == 1 || step == 2)
                {
                    if (finalStatus == StepStatus.Corrupted)
                    {
                        HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_CorruptionDetected");
                        NotificationService.Show(
                            LocalizationHelper.GetString("SystemRepairPage_Notification_CheckDetected_Title"),
                            LocalizationHelper.GetString("SystemRepairPage_Notification_CheckDetected_Message"),
                            InfoBarSeverity.Warning
                        );
                    }
                    else if (finalStatus == StepStatus.Unknown)
                    {
                        HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_Unknown");
                        NotificationService.Show(
                            LocalizationHelper.GetString("SystemRepairPage_Notification_Unknown_Title"),
                            LocalizationHelper.GetString("SystemRepairPage_Notification_Unknown_Message"),
                            InfoBarSeverity.Warning
                        );
                    }
                    else
                    {
                        HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllHealthy");
                        NotificationService.Show(
                            LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar.Title"),
                            LocalizationHelper.GetString("SystemRepairPage_Notification_AllHealthy"),
                            InfoBarSeverity.Success
                        );
                    }
                }
                else
                {
                    if (res.HasRepaired)
                    {
                        HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllRepaired");
                        NotificationService.Show(
                            LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar.Title"),
                            LocalizationHelper.GetString("SystemRepairPage_Notification_AllRepaired"),
                            InfoBarSeverity.Success
                        );
                    }
                    else
                    {
                        HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllHealthy");
                        NotificationService.Show(
                            LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar.Title"),
                            LocalizationHelper.GetString("SystemRepairPage_Notification_AllHealthy"),
                            InfoBarSeverity.Success
                        );
                    }
                }
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
            public ImageHealthAssessment HealthAssessment { get; set; } = ImageHealthAssessment.NotApplicable;
            public bool HasRepaired { get; set; } = false;
        }

        private async Task<CommandResult> RunCommandInternalAsync(string fileName, string arguments, int currentStep, int totalSteps, int? targetStepIndex = null)
        {
            _isProcessRunning = true;
            IsAnyProcessRunning = true;
            _allowNavigation = false;

            int activeVisualStep = targetStepIndex ?? currentStep;
            var result = new CommandResult();
            if (activeVisualStep == 1 || activeVisualStep == 2)
            {
                result.HealthAssessment = ImageHealthAssessment.Unknown;
            }
            var resultLock = new object();

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

                void ParseLineStatus(string line)
                {
                    if (string.IsNullOrWhiteSpace(line)) return;

                    // Multilingual keywords for Repair results (RestoreHealth / SFC)
                    bool repaired = line.Contains("發現損毀檔案並已成功修復", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("发现损坏文件并已成功修复", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("已修復元件存放區損毀", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("已修复组件存储损坏", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("corruption was repaired", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("正常に修復されました", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("erfolgreich repariert", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("réparé avec succès", StringComparison.OrdinalIgnoreCase) ||
                                    line.Contains("reparó correctamente", StringComparison.OrdinalIgnoreCase);

                    // Multilingual keywords for Corruption / Repairable states (CheckHealth / ScanHealth)
                    bool repairable = line.Contains("元件存放區可以修復", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("可修復的", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("组件存储可以修复", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("可修复的", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("is repairable", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("修復可能です", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("kann repariert werden", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("est réparable", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("se puede reparar", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("è ripristinabile", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("é reparável", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("подлежит восстановлению", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("복구할 수 있습니다", StringComparison.OrdinalIgnoreCase);

                    // Multilingual keywords for Healthy states (CheckHealth / ScanHealth)
                    bool healthy = line.Contains("未偵測到元件存放區損毀", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("未检测到组件存储损坏", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("破損は検出されませんでした", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("keine Komponentenspeicherbeschädigung", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("Aucun endommagement du magasin de composants", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("No se detectaron daños en el almacén de componentes", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("Nessun danneggiamento dell'archivio dei componenti", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("Nenhum dano ao repositório de componentes", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("Повреждение хранилища компонентов не обнаружено", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("손상이 검색되지 않았습니다", StringComparison.OrdinalIgnoreCase);

                    // Multilingual keywords for NonRepairable / Failure states
                    bool failed = line.Contains("元件存放區無法修復", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("無法修復", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("组件存储无法修复", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("无法修复", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("cannot be repaired", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("non-repairable", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("unable to fix", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("could not be repaired", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("修復できません", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("kann nicht repariert werden", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("ne peut pas être réparé", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("no se puede reparar", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("non è ripristinabile", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("não pode ser reparado", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("не подлежит восстановлению", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("복구할 수 없습니다", StringComparison.OrdinalIgnoreCase);

                    lock (resultLock)
                    {
                        if (repaired) result.HasRepaired = true;

                        if (repairable)
                        {
                            result.HealthAssessment = ImageHealthAssessment.Repairable;
                        }
                        else if (healthy && result.HealthAssessment != ImageHealthAssessment.Repairable)
                        {
                            result.HealthAssessment = ImageHealthAssessment.Healthy;
                        }

                        if (failed)
                        {
                            result.HealthAssessment = ImageHealthAssessment.NonRepairable;
                            result.IsSuccess = false;
                        }
                    }
                }

                process.Start();

                var stdoutTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                        {
                            ParseLineStatus(line);

                            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                            {
                                AppendLog(line);
                                ParseLineProgress(line, currentStep, totalSteps, activeVisualStep);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"RepairTool stdout reader: {ex.Message}");
                    }
                });

                var stderrTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                        {
                            ParseLineStatus(line);

                            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                            {
                                string errorPrefix = LocalizationHelper.GetString("SystemRepairPage_Log_ErrorPrefix");
                                AppendLog($"{errorPrefix} " + line);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"RepairTool stderr reader: {ex.Message}");
                    }
                });

                // Wait for both the process exit AND the output/error streams to reach EOF
                await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);

                LoggingService.Log($"RepairTool: {fileName} execution finished with exit code {process.ExitCode}.");

                bool isSfc = fileName.Equals("sfc", StringComparison.OrdinalIgnoreCase);
                if (isSfc)
                {
                    if (process.ExitCode == 1)
                    {
                        lock (resultLock)
                        {
                            result.HasRepaired = true;
                        }
                    }
                    else if (process.ExitCode != 0)
                    {
                        lock (resultLock)
                        {
                            result.IsSuccess = false;
                        }
                    }
                }
                else
                {
                    if (process.ExitCode != 0)
                    {
                        lock (resultLock)
                        {
                            result.IsSuccess = false;
                        }
                    }
                }

                if (result.IsSuccess)
                {
                    AppendLog(LocalizationHelper.GetString("SystemRepairPage_Completed"));
                }
                else
                {
                    AppendLog(LocalizationHelper.GetString("SystemRepairPage_Step_Failed") ?? "執行失敗");
                }
            }
            catch (Exception ex)
            {
                lock (resultLock)
                {
                    result.IsSuccess = false;
                }
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

        private void ParseLineProgress(string line, int currentStep, int totalSteps, int activeVisualStep)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // Regex 1: Match DISM bracketed or generic percentage e.g. [=== 64.0% ===] or 64.0%
            var match = ProgressPercentRegex().Match(line);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p))
            {
                UpdateStepProgress(activeVisualStep, p);

                double stepBase = (currentStep - 1) * 100.0;
                double overall = Math.Clamp((stepBase + p) / totalSteps, 0, 100);
                MainProgressBar.Value = overall;
                HeroPercentageText.Text = $"{overall:0}%";
            }
        }

        [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
        private static partial Regex ProgressPercentRegex();

        #endregion

        #region Helpers & Actions

        private void ApplyAggregatedResults(IReadOnlyList<(string Title, CommandResult Result)> stepResults)
        {
            if (stepResults == null || stepResults.Count == 0) return;

            int total = stepResults.Count;
            int successCount = stepResults.Count(r => r.Result.IsSuccess);
            int failCount = stepResults.Count(r => !r.Result.IsSuccess);
            bool anyRepaired = stepResults.Any(r => r.Result.IsSuccess && r.Result.HasRepaired);
            bool anyUnknown = stepResults.Any(r => r.Result.IsSuccess && r.Result.HealthAssessment == ImageHealthAssessment.Unknown);

            if (failCount == 0)
            {
                // All steps succeeded
                MainProgressBar.Value = 100;
                HeroPercentageText.Text = "100%";

                if (anyRepaired)
                {
                    HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllRepaired");
                    NotificationService.Show(
                        LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar.Title"),
                        LocalizationHelper.GetString("SystemRepairPage_Notification_AllRepaired"),
                        InfoBarSeverity.Success
                    );
                }
                else if (anyUnknown)
                {
                    HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AggregatedUnknown");
                    NotificationService.Show(
                        LocalizationHelper.GetString("SystemRepairPage_Notification_Unknown_Title"),
                        LocalizationHelper.GetString("SystemRepairPage_Notification_AggregatedUnknown_Message"),
                        InfoBarSeverity.Warning
                    );
                }
                else
                {
                    HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllHealthy");
                    NotificationService.Show(
                        LocalizationHelper.GetString("SystemRepairPage_CompletionInfoBar.Title"),
                        LocalizationHelper.GetString("SystemRepairPage_Notification_AllHealthy"),
                        InfoBarSeverity.Success
                    );
                }
            }
            else if (successCount > 0)
            {
                // Partially completed: some succeeded, some failed
                double actualPercent = Math.Round((double)successCount / total * 100);
                MainProgressBar.Value = actualPercent;
                HeroPercentageText.Text = $"{actualPercent:0}%";
                HeroStepStatusText.Text = string.Format(LocalizationHelper.GetString("SystemRepairPage_Status_PartialFailed"), successCount, total);

                NotificationService.Show(
                    LocalizationHelper.GetString("SystemRepairPage_Notification_PartialFailed_Title"),
                    string.Format(LocalizationHelper.GetString("SystemRepairPage_Notification_PartialFailed_Message"), failCount),
                    InfoBarSeverity.Warning
                );
            }
            else
            {
                // All steps failed
                MainProgressBar.Value = 0;
                HeroPercentageText.Text = "0%";
                HeroStepStatusText.Text = LocalizationHelper.GetString("SystemRepairPage_Status_AllFailed");

                NotificationService.Show(
                    LocalizationHelper.GetString("SystemRepairPage_Notification_Failed_Title"),
                    LocalizationHelper.GetString("SystemRepairPage_Notification_Failed_Message"),
                    InfoBarSeverity.Error
                );
            }
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
            _logBuilder.Clear();
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
            if (LogOutput.Text == placeholder || _logBuilder.Length == 0)
            {
                _logBuilder.Clear();
            }

            _logBuilder.AppendLine(text);
            LogOutput.Text = _logBuilder.ToString();

            LogScrollViewer.ChangeView(null, double.MaxValue, null, true);

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