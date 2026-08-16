using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
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
            this.Loaded += CrashReportPage_Loaded;
        }

        private async void CrashReportPage_Loaded(object sender, RoutedEventArgs e)
        {
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
            RefreshBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;

            try
            {
                _allReports = await CrashReportService.GetCrashReportsAsync();
                
                // Update summary statistics
                var summary = CrashReportService.GenerateSummary(_allReports);
                TotalCountText.Text = summary.TotalCrashes.ToString();
                BsodCountText.Text = summary.BsodCount.ToString();
                AppCrashCountText.Text = summary.AppCrashCount.ToString();
                LastCrashTimeText.Text = summary.LastCrashTime.HasValue 
                    ? summary.LastCrashTime.Value.ToString("yyyy/MM/dd HH:mm") 
                    : LocalizationHelper.GetString("CrashReportPage_None");

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
            var filterType = TypeFilterComboBox.SelectedIndex switch
            {
                1 => (Func<CrashReportItem, bool>)(x => x.Type == CrashType.Bsod),
                2 => (Func<CrashReportItem, bool>)(x => x.Type != CrashType.Bsod),
                _ => (Func<CrashReportItem, bool>)(x => true)
            };

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

                // Auto-select first item if available
                if (CrashListView.SelectedItem == null || !_filteredReports.Contains((CrashReportItem)CrashListView.SelectedItem))
                {
                    CrashListView.SelectedIndex = 0;
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

            // Show or hide locate button
            LocateDumpBtn.Visibility = (!string.IsNullOrEmpty(item.FilePathOrDump) && File.Exists(item.FilePathOrDump))
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void TypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
    }
}
