using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class CrashReportPage : Page
    {
        private List<CrashReportItem> _allReports = new();
        private List<CrashReportItem> _filteredReports = new();
        private CrashReportItem? _selectedItem;

        public CrashReportPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            this.Loaded += CrashReportPage_Loaded;
        }

        private async void CrashReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (FilterAllItem != null && string.IsNullOrEmpty(FilterAllItem.Text))
                FilterAllItem.Text = LocalizationHelper.GetString("CrashReportPage_Filter_All.Text") ?? "全部事件";
            if (FilterBsodItem != null && string.IsNullOrEmpty(FilterBsodItem.Text))
                FilterBsodItem.Text = LocalizationHelper.GetString("CrashReportPage_Filter_BSOD.Text") ?? "系統黑色畫面";
            if (FilterAppItem != null && string.IsNullOrEmpty(FilterAppItem.Text))
                FilterAppItem.Text = LocalizationHelper.GetString("CrashReportPage_Filter_App.Text") ?? "應用程式";

            if (TypeSelectorBar != null && TypeSelectorBar.SelectedItem == null && FilterAllItem != null)
            {
                TypeSelectorBar.SelectedItem = FilterAllItem;
            }

            if (_allReports.Count == 0)
            {
                await LoadReportsAsync();
            }
        }

        private async Task LoadReportsAsync()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
            CrashListView.Visibility = Visibility.Collapsed;
            NoSelectionPrompt.Visibility = Visibility.Visible;
            DetailScrollViewer.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;

            try
            {
                _allReports = await CrashReportService.GetCrashReportsAsync();
                LoadingPanel.Visibility = Visibility.Collapsed;
                
                // Update summary statistics
                var summary = CrashReportService.GenerateSummary(_allReports);
                TotalCountText.Text = summary.TotalCrashes.ToString();
                BsodCountText.Text = summary.BsodCount.ToString();
                AppCrashCountText.Text = summary.AppCrashCount.ToString();
                LastCrashTimeText.Text = summary.LastCrashTime.HasValue 
                    ? summary.LastCrashTime.Value.ToString("yyyy/MM/dd HH:mm") 
                    : LocalizationHelper.GetString("CrashReportPage_None");

                if (TypeSelectorBar != null && TypeSelectorBar.SelectedItem == null && FilterAllItem != null)
                {
                    TypeSelectorBar.SelectedItem = FilterAllItem;
                }

                ApplyFilters();
                ExportBtn.IsEnabled = _allReports.Count > 0;
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[CrashReportPage] Error loading crash reports: {ex.Message}");
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                RefreshBtn.IsEnabled = true;
            }
        }

        private void ApplyFilters()
        {
            var filterType = (TypeSelectorBar?.SelectedItem == FilterBsodItem)
                ? (Func<CrashReportItem, bool>)(x => x.Type == CrashType.Bsod)
                : (TypeSelectorBar?.SelectedItem == FilterAppItem)
                    ? (Func<CrashReportItem, bool>)(x => x.Type != CrashType.Bsod)
                    : (Func<CrashReportItem, bool>)(x => true);

            string search = CrashSearchBox.Text?.Trim() ?? "";

            _filteredReports = _allReports
                .Where(filterType)
                .Where(x => string.IsNullOrEmpty(search) ||
                            x.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            x.SourceOrApp.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            x.ErrorCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            x.FaultingModule.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            x.RawDetails.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            CrashListView.ItemsSource = _filteredReports;
            ShowingCountText.Text = $"{_filteredReports.Count} / {_allReports.Count}";

            if (_filteredReports.Count == 0)
            {
                CrashListView.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = Visibility.Visible;
                NoSelectionPrompt.Visibility = Visibility.Visible;
                DetailScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
                CrashListView.Visibility = Visibility.Visible;

                // Auto-select first item if available (enqueue to UI dispatcher queue to ensure list renders first before detail view)
                if (CrashListView.SelectedItem == null || !_filteredReports.Contains((CrashReportItem)CrashListView.SelectedItem))
                {
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        if (_filteredReports.Count > 0 && (CrashListView.SelectedItem == null || !_filteredReports.Contains((CrashReportItem)CrashListView.SelectedItem)))
                        {
                            CrashListView.SelectedIndex = 0;
                        }
                    });
                }
            }
        }

        private void CrashListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CrashListView.SelectedItem is CrashReportItem item)
            {
                _selectedItem = item;
                DisplayItemDetails(item);
            }
            else
            {
                _selectedItem = null;
                NoSelectionPrompt.Visibility = Visibility.Visible;
                DetailScrollViewer.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayItemDetails(CrashReportItem item)
        {
            NoSelectionPrompt.Visibility = Visibility.Collapsed;
            DetailScrollViewer.Visibility = Visibility.Visible;
            if (RawLogExpander != null) RawLogExpander.IsExpanded = false;
            try { FadeInDetailStoryboard.Begin(); } catch { }

            DetailTypeBadgeText.Text = item.DisplayBadge;
            DetailTimestampText.Text = item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            DetailTitleText.Text = item.Title;

            // Details
            DetailDescriptionText.Text = string.IsNullOrEmpty(item.ErrorDescription) ? "N/A" : item.ErrorDescription;
            DetailRecommendationText.Text = string.IsNullOrEmpty(item.Recommendation) ? "N/A" : item.Recommendation;

            DetailErrorCodeText.Text = string.IsNullOrEmpty(item.ErrorCode) ? "N/A" : item.ErrorCode;
            DetailFaultModuleText.Text = string.IsNullOrEmpty(item.FaultingModule) ? "N/A" : item.FaultingModule;
            DetailSourceAppText.Text = string.IsNullOrEmpty(item.SourceOrApp) ? "N/A" : item.SourceOrApp;
            DetailParamsText.Text = string.IsNullOrEmpty(item.Parameters) ? "N/A" : item.Parameters;
            DetailDumpPathText.Text = string.IsNullOrEmpty(item.FilePathOrDump) ? "N/A" : item.FilePathOrDump;

            DetailRawLogText.Text = string.IsNullOrEmpty(item.RawDetails) 
                ? LocalizationHelper.GetString("CrashReportPage_RawLog_None") 
                : item.RawDetails;

            // Wire up copy tags
            CopyRawLogBtn.Tag = item.RawDetails;
            CopyRawLogBtn.Visibility = !string.IsNullOrEmpty(item.RawDetails) ? Visibility.Visible : Visibility.Collapsed;

            // Show or hide locate button
            LocateDumpBtn.Visibility = (!string.IsNullOrEmpty(item.FilePathOrDump) && File.Exists(item.FilePathOrDump))
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void TypeSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (ShowingCountText != null) ApplyFilters();
        }

        private void CrashSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportsAsync();
        }

        private void OpenDumpFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string minidumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (Directory.Exists(minidumpDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = minidumpDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    NotificationService.Show(
                        LocalizationHelper.GetString("CrashReportPage_OpenDumpFolderBtn/Text"),
                        LocalizationHelper.GetString("CrashReportPage_DumpFolder_NotFound"),
                        InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[CrashReportPage] Failed to open minidump folder: {ex.Message}");
            }
        }

        private void LocateDumpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null && !string.IsNullOrEmpty(_selectedItem.FilePathOrDump) && File.Exists(_selectedItem.FilePathOrDump))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{_selectedItem.FilePathOrDump}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportPage] Failed to locate file: {ex.Message}");
                }
            }
        }

        private async void CopyField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string value && !string.IsNullOrEmpty(value) && value != "N/A")
            {
                try
                {
                    var data = new DataPackage();
                    data.SetText(value);
                    Clipboard.SetContent(data);

                    NotificationService.Show(
                        LocalizationHelper.GetString("Notification_Success"),
                        LocalizationHelper.GetString("CrashReportPage_Detail_Copied"),
                        InfoBarSeverity.Success);

                    if (btn.Content is FontIcon icon)
                    {
                        string oldGlyph = icon.Glyph;
                        icon.Glyph = "\uE8FB"; // Checkmark
                        await Task.Delay(1500);
                        icon.Glyph = oldGlyph;
                    }
                    else if (btn.Content is StackPanel sp)
                    {
                        var fontIcon = sp.Children.OfType<FontIcon>().FirstOrDefault();
                        if (fontIcon != null)
                        {
                            string oldGlyph = fontIcon.Glyph;
                            fontIcon.Glyph = "\uE8FB"; // Checkmark
                            await Task.Delay(1500);
                            fontIcon.Glyph = oldGlyph;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportPage] Failed to copy single field: {ex.Message}");
                }
            }
        }

        private async void CopyDetailBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            try
            {
                string lblTime = LocalizationHelper.GetString("CrashReport_Export_Field_Timestamp");
                string lblCode = LocalizationHelper.GetString("CrashReportPage_Detail_ErrorCode/Text");
                string lblModule = LocalizationHelper.GetString("CrashReportPage_Detail_FaultModule/Text");
                string lblSource = LocalizationHelper.GetString("CrashReportPage_Detail_SourceApp/Text");
                string lblParams = LocalizationHelper.GetString("CrashReportPage_Detail_Params/Text");
                string lblDump = LocalizationHelper.GetString("CrashReportPage_Detail_DumpPath/Text");
                string lblDiag = LocalizationHelper.GetString("CrashReportPage_Detail_DiagnosisHeader/Text");
                string lblRec = LocalizationHelper.GetString("CrashReportPage_Detail_RecommendationHeader/Text");
                string lblRaw = LocalizationHelper.GetString("CrashReportPage_Detail_RawLogHeader/Text");

                string reportText = $"[{_selectedItem.DisplayBadge}] {_selectedItem.Title}\n" +
                                    $"{lblTime}: {_selectedItem.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                                    $"{lblCode}: {_selectedItem.ErrorCode}\n" +
                                    $"{lblModule}: {_selectedItem.FaultingModule}\n" +
                                    $"{lblSource}: {_selectedItem.SourceOrApp}\n" +
                                    (!string.IsNullOrEmpty(_selectedItem.Parameters) ? $"{lblParams}: {_selectedItem.Parameters}\n" : "") +
                                    (!string.IsNullOrEmpty(_selectedItem.FilePathOrDump) ? $"{lblDump}: {_selectedItem.FilePathOrDump}\n" : "") +
                                    $"\n{lblDiag}:\n{_selectedItem.ErrorDescription}\n" +
                                    $"\n{lblRec}:\n{_selectedItem.Recommendation}\n" +
                                    (!string.IsNullOrEmpty(_selectedItem.RawDetails) ? $"\n--- {lblRaw} ---\n{_selectedItem.RawDetails}" : "");

                var data = new DataPackage();
                data.SetText(reportText);
                Clipboard.SetContent(data);

                NotificationService.Show(
                    LocalizationHelper.GetString("Notification_Success"),
                    LocalizationHelper.GetString("CrashReportPage_Detail_Copied"),
                    InfoBarSeverity.Success);

                CopyDetailIcon.Glyph = "\uE8FB"; // Checkmark
                CopyDetailText.Text = LocalizationHelper.GetString("CrashReportPage_Detail_Copied");

                await Task.Delay(2000);
                CopyDetailIcon.Glyph = "\uE8C8"; // Copy icon
                CopyDetailText.Text = LocalizationHelper.GetString("CrashReportPage_Detail_CopyBtn/Text");
            }
            catch { }
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_allReports.Count == 0) return;

            try
            {
                string defaultFileName = $"Crash_Report_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                IntPtr hwnd = App.MainWindow != null ? WindowNative.GetWindowHandle(App.MainWindow) : IntPtr.Zero;
                string filter = "Markdown File (*.md)\0*.md\0Text File (*.txt)\0*.txt\0All Files (*.*)\0*.*\0\0";
                string title = LocalizationHelper.GetString("CrashReportPage_ExportBtn/Text");

                string? targetFilePath = Win32FilePicker.ShowSaveDialog(hwnd, title, defaultFileName, filter, "md");

                if (!string.IsNullOrEmpty(targetFilePath))
                {
                    string content = CrashReportService.ExportToMarkdown(_allReports);
                    await File.WriteAllTextAsync(targetFilePath, content, System.Text.Encoding.UTF8);

                    NotificationService.Show(
                        LocalizationHelper.GetString("CrashReportPage_ExportSuccess_Title"),
                        $"{LocalizationHelper.GetString("CrashReportPage_ExportSuccess_Msg")} ({targetFilePath})",
                        InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[CrashReportPage] Export failed: {ex.Message}");
            }
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MasterListColumn != null)
            {
                MasterListColumn.Width = e.NewSize.Width < 960 ? new GridLength(280) : new GridLength(360);
            }
        }

        private async void CheckDumpConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int dumpType = -1;
                string dumpFolder = @"%SystemRoot%\Minidump";
                bool autoReboot = false;

                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
                    if (key != null)
                    {
                        dumpType = Convert.ToInt32(key.GetValue("CrashDumpEnabled", -1));
                        dumpFolder = key.GetValue("MinidumpDir", @"%SystemRoot%\Minidump")?.ToString() ?? @"%SystemRoot%\Minidump";
                        autoReboot = Convert.ToInt32(key.GetValue("AutoReboot", 0)) == 1;
                    }
                }
                catch { }

                string dumpDesc = dumpType switch
                {
                    0 => LocalizationHelper.GetString("CrashDump_Disabled") ?? "已停用 (不會產生當機傾印記錄檔)",
                    1 => LocalizationHelper.GetString("CrashDump_Complete") ?? "完整記憶體傾印 (Complete Dump)",
                    2 => LocalizationHelper.GetString("CrashDump_Kernel") ?? "核心記憶體傾印 (Kernel Dump)",
                    3 => LocalizationHelper.GetString("CrashDump_Small") ?? "小型記憶體傾印 (Minidump 256KB，推薦)",
                    7 => LocalizationHelper.GetString("CrashDump_Automatic") ?? "自動記憶體傾印 (Automatic Dump)",
                    _ => LocalizationHelper.GetString("CrashDump_Unknown") ?? "未設定或由系統管理"
                };

                var expFolder = Environment.ExpandEnvironmentVariables(dumpFolder);
                bool folderExists = Directory.Exists(expFolder);

                var sb = new StringBuilder();
                sb.AppendLine($"{LocalizationHelper.GetString("CrashReport_DumpTypeLabel") ?? "當機轉儲類型"}：{dumpDesc}");
                sb.AppendLine($"{LocalizationHelper.GetString("CrashReport_DumpFolderLabel") ?? "轉儲儲存路徑"}：{expFolder} ({(folderExists ? (LocalizationHelper.GetString("Folder_Exists") ?? "目錄存在") : (LocalizationHelper.GetString("Folder_NotExists") ?? "目錄尚未建立"))})");
                sb.AppendLine($"{LocalizationHelper.GetString("CrashReport_AutoRebootLabel") ?? "當機自動重新啟動"}：{(autoReboot ? (LocalizationHelper.GetString("Common_Enabled") ?? "已啟用") : (LocalizationHelper.GetString("Common_Disabled") ?? "已停用"))}");
                sb.AppendLine();
                sb.AppendLine(LocalizationHelper.GetString("CrashReport_DumpHint") ?? "若系統遭遇 BSOD 藍屏卻未產生傾印檔案，請確保此處設為「小型記憶體傾印 (256KB)」，且分頁檔 (pagefile.sys) 位於系統磁碟 C: 上。");

                var dialog = new ContentDialog
                {
                    Title = LocalizationHelper.GetString("CrashReport_DumpConfigTitle") ?? "系統當機轉儲 (Minidump) 設定檢查",
                    Content = sb.ToString(),
                    PrimaryButtonText = LocalizationHelper.GetString("CrashReport_OpenSystemSettings") ?? "開啟 Windows 啟動及修復設定",
                    CloseButtonText = LocalizationHelper.GetString("Common_Close") ?? "關閉",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "sysdm.cpl",
                        Arguments = ",3",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[CrashReportPage] CheckDumpConfig failed: {ex.Message}");
            }
        }
    }
}
