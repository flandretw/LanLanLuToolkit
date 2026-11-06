using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class CrashReportPage : Page
    {
        private const int DefaultDisplayLimit = 15;
        private bool _isLoading = false;
        private bool _showAllAppCrashes = false;

        private List<CrashReportItem> _allReports = new();
        private List<CrashReportItem> _filteredBsod = new();
        private List<CrashReportItem> _filteredAppCrash = new();
        private List<CrashReportItem> _filteredAppHang = new();

        public CrashReportPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            this.Loaded += CrashReportPage_Loaded;
        }

        private async void CrashReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_allReports.Count == 0 && !_isLoading)
            {
                await LoadReportsAsync();
            }
        }

        private async Task LoadReportsAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            LoadingPanel.Visibility = Visibility.Visible;
            CategoriesPanel.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;

            try
            {
                _allReports = await CrashReportService.GetCrashReportsAsync();
                _showAllAppCrashes = false;
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
                CategoriesPanel.Visibility = Visibility.Visible;
                RefreshBtn.IsEnabled = true;
                _isLoading = false;
            }
        }

        private static Brush? GetThemeBrush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out var res) && res is Brush b)
            {
                return b;
            }
            return null;
        }

        private static void SetEmptyPanelState(
            Grid panel,
            FontIcon icon,
            TextBlock titleText,
            TextBlock descText,
            bool isSearchEmpty,
            string healthyTitleKey,
            string healthyDescKey)
        {
            panel.Visibility = Visibility.Visible;
            if (isSearchEmpty)
            {
                icon.Glyph = "\uE721"; // Search glyph
                var brush = GetThemeBrush("TextFillColorSecondaryBrush");
                if (brush != null) icon.Foreground = brush;
                titleText.Text = LocalizationHelper.GetString("CrashReportPage_NoSearchMatch_Title");
                descText.Text = LocalizationHelper.GetString("CrashReportPage_NoSearchMatch_Desc");
            }
            else
            {
                icon.Glyph = "\uE73E"; // Checkmark glyph
                var brush = GetThemeBrush("SystemFillColorSuccessBrush");
                if (brush != null) icon.Foreground = brush;
                titleText.Text = LocalizationHelper.GetString(healthyTitleKey);
                descText.Text = LocalizationHelper.GetString(healthyDescKey);
            }
        }

        private void ApplyFilters()
        {
            string search = CrashSearchBox.Text?.Trim() ?? "";
            bool isSearching = !string.IsNullOrWhiteSpace(search);

            bool MatchSearch(CrashReportItem item)
            {
                if (string.IsNullOrEmpty(search)) return true;
                return item.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.SourceOrApp.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.ErrorCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.FaultingModule.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.RawDetails.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.ErrorDescription.Contains(search, StringComparison.OrdinalIgnoreCase);
            }

            _filteredBsod = _allReports
                .Where(x => x.Type == CrashType.Bsod)
                .Where(MatchSearch)
                .ToList();

            _filteredAppCrash = _allReports
                .Where(x => x.Type == CrashType.AppCrash || x.Type == CrashType.DotNetCrash)
                .Where(MatchSearch)
                .ToList();

            _filteredAppHang = _allReports
                .Where(x => x.Type == CrashType.AppHang)
                .Where(MatchSearch)
                .ToList();

            string summaryFormat = LocalizationHelper.GetString("CrashReportPage_Subtitle_SummaryFormat");
            string searchEmptyFormat = LocalizationHelper.GetString("CrashReportPage_NoSearchMatch_Subtitle");

            // 1. Kernel Crashes (BSOD) Category
            string bsodCatTitle = LocalizationHelper.GetString("CrashReportPage_Category_BSOD");
            BsodExpanderTitle.Text = $"{bsodCatTitle} ({_filteredBsod.Count})";

            if (_filteredBsod.Count == 0)
            {
                bool isSearchEmpty = isSearching;
                SetEmptyPanelState(
                    BsodHealthyPanel,
                    BsodEmptyIcon,
                    BsodEmptyTitleText,
                    BsodEmptyDescText,
                    isSearchEmpty,
                    "CrashReportPage_Healthy_BSOD_Title",
                    "CrashReportPage_Healthy_BSOD_Desc");

                BsodItemsControl.Visibility = Visibility.Collapsed;

                if (isSearchEmpty)
                {
                    BsodSuccessIcon.Visibility = Visibility.Collapsed;
                    BsodWarningIcon.Visibility = Visibility.Collapsed;
                    BsodNeutralIcon.Visibility = Visibility.Visible;
                    BsodSubtitleText.Text = string.Format(searchEmptyFormat, search);
                }
                else
                {
                    BsodSuccessIcon.Visibility = Visibility.Visible;
                    BsodWarningIcon.Visibility = Visibility.Collapsed;
                    BsodNeutralIcon.Visibility = Visibility.Collapsed;
                    BsodSubtitleText.Text = LocalizationHelper.GetString("CrashReportPage_Healthy_BSOD_Desc");
                }
                BsodExpander.IsExpanded = false; // Quietly collapsed when empty
            }
            else
            {
                BsodHealthyPanel.Visibility = Visibility.Collapsed;
                BsodItemsControl.Visibility = Visibility.Visible;
                BsodItemsControl.ItemsSource = _filteredBsod;
                BsodSuccessIcon.Visibility = Visibility.Collapsed;
                BsodNeutralIcon.Visibility = Visibility.Collapsed;
                BsodWarningIcon.Visibility = Visibility.Visible;
                var latestBsod = _filteredBsod.OrderByDescending(x => x.Timestamp).First();
                BsodSubtitleText.Text = string.Format(summaryFormat, _filteredBsod.Count, $"{latestBsod.FormattedTime} ({latestBsod.ErrorCode})");
                BsodExpander.IsExpanded = true;
            }

            // 2. Application Crashes Category (Smart expanded)
            string appCrashCatTitle = LocalizationHelper.GetString("CrashReportPage_Category_AppCrash");
            AppCrashExpanderTitle.Text = $"{appCrashCatTitle} ({_filteredAppCrash.Count})";

            if (_filteredAppCrash.Count == 0)
            {
                bool isSearchEmpty = isSearching;
                SetEmptyPanelState(
                    AppCrashHealthyPanel,
                    AppCrashEmptyIcon,
                    AppCrashEmptyTitleText,
                    AppCrashEmptyDescText,
                    isSearchEmpty,
                    "CrashReportPage_Healthy_AppCrash_Title",
                    "CrashReportPage_Healthy_AppCrash_Desc");

                AppCrashItemsControl.Visibility = Visibility.Collapsed;
                AppCrashShowMoreBtn.Visibility = Visibility.Collapsed;

                if (isSearchEmpty)
                {
                    AppCrashWarningIcon.Visibility = Visibility.Collapsed;
                    AppCrashSuccessIcon.Visibility = Visibility.Collapsed;
                    AppCrashNeutralIcon.Visibility = Visibility.Visible;
                    AppCrashSubtitleText.Text = string.Format(searchEmptyFormat, search);
                }
                else
                {
                    AppCrashWarningIcon.Visibility = Visibility.Collapsed;
                    AppCrashSuccessIcon.Visibility = Visibility.Visible;
                    AppCrashNeutralIcon.Visibility = Visibility.Collapsed;
                    AppCrashSubtitleText.Text = LocalizationHelper.GetString("CrashReportPage_Healthy_AppCrash_Desc");
                }
                AppCrashExpander.IsExpanded = false;
            }
            else
            {
                AppCrashHealthyPanel.Visibility = Visibility.Collapsed;
                AppCrashItemsControl.Visibility = Visibility.Visible;
                AppCrashWarningIcon.Visibility = Visibility.Visible;
                AppCrashSuccessIcon.Visibility = Visibility.Collapsed;
                AppCrashNeutralIcon.Visibility = Visibility.Collapsed;

                var latestApp = _filteredAppCrash.OrderByDescending(x => x.Timestamp).First();
                string appName = string.IsNullOrEmpty(latestApp.SourceOrApp) ? latestApp.Title : latestApp.SourceOrApp;
                AppCrashSubtitleText.Text = string.Format(summaryFormat, _filteredAppCrash.Count, $"{appName} ({latestApp.FormattedTime})");

                // Render top N items or all items
                if (!_showAllAppCrashes && _filteredAppCrash.Count > DefaultDisplayLimit)
                {
                    AppCrashItemsControl.ItemsSource = _filteredAppCrash.Take(DefaultDisplayLimit).ToList();
                    int remaining = _filteredAppCrash.Count - DefaultDisplayLimit;
                    string showMoreFormat = LocalizationHelper.GetString("CrashReportPage_ShowMore");
                    AppCrashShowMoreText.Text = string.Format(showMoreFormat, remaining);
                    AppCrashShowMoreBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    AppCrashItemsControl.ItemsSource = _filteredAppCrash;
                    AppCrashShowMoreBtn.Visibility = Visibility.Collapsed;
                }

                AppCrashExpander.IsExpanded = true; // Smart expanded by default
            }

            // 3. Application Hangs Category
            string appHangCatTitle = LocalizationHelper.GetString("CrashReportPage_Category_AppHang");
            AppHangExpanderTitle.Text = $"{appHangCatTitle} ({_filteredAppHang.Count})";

            if (_filteredAppHang.Count == 0)
            {
                bool isSearchEmpty = isSearching;
                SetEmptyPanelState(
                    AppHangHealthyPanel,
                    AppHangEmptyIcon,
                    AppHangEmptyTitleText,
                    AppHangEmptyDescText,
                    isSearchEmpty,
                    "CrashReportPage_Healthy_AppHang_Title",
                    "CrashReportPage_Healthy_AppHang_Desc");

                AppHangItemsControl.Visibility = Visibility.Collapsed;

                if (isSearchEmpty)
                {
                    AppHangWarningIcon.Visibility = Visibility.Collapsed;
                    AppHangSuccessIcon.Visibility = Visibility.Collapsed;
                    AppHangNeutralIcon.Visibility = Visibility.Visible;
                    AppHangSubtitleText.Text = string.Format(searchEmptyFormat, search);
                }
                else
                {
                    AppHangWarningIcon.Visibility = Visibility.Collapsed;
                    AppHangSuccessIcon.Visibility = Visibility.Visible;
                    AppHangNeutralIcon.Visibility = Visibility.Collapsed;
                    AppHangSubtitleText.Text = LocalizationHelper.GetString("CrashReportPage_Healthy_AppHang_Desc");
                }
                AppHangExpander.IsExpanded = false;
            }
            else
            {
                AppHangHealthyPanel.Visibility = Visibility.Collapsed;
                AppHangItemsControl.Visibility = Visibility.Visible;
                AppHangWarningIcon.Visibility = Visibility.Visible;
                AppHangSuccessIcon.Visibility = Visibility.Collapsed;
                AppHangNeutralIcon.Visibility = Visibility.Collapsed;
                AppHangItemsControl.ItemsSource = _filteredAppHang;
                var latestHang = _filteredAppHang.OrderByDescending(x => x.Timestamp).First();
                string hangApp = string.IsNullOrEmpty(latestHang.SourceOrApp) ? latestHang.Title : latestHang.SourceOrApp;
                AppHangSubtitleText.Text = string.Format(summaryFormat, _filteredAppHang.Count, $"{hangApp} ({latestHang.FormattedTime})");
                // Only expand AppHang by default if there are no major AppCrashes, keeping focal hierarchy clean
                AppHangExpander.IsExpanded = (_filteredAppCrash.Count == 0);
            }

            // Update Total Count indicator in search toolbar
            int totalShown = _filteredBsod.Count + _filteredAppCrash.Count + _filteredAppHang.Count;
            ShowingCountText.Text = string.IsNullOrEmpty(search)
                ? string.Format(LocalizationHelper.GetString("CrashReportPage_Count_Total"), totalShown)
                : string.Format(LocalizationHelper.GetString("CrashReportPage_Count_Filtered"), totalShown, _allReports.Count);
        }

        private void AppCrashShowMoreBtn_Click(object sender, RoutedEventArgs e)
        {
            _showAllAppCrashes = true;
            AppCrashItemsControl.ItemsSource = _filteredAppCrash;
            AppCrashShowMoreBtn.Visibility = Visibility.Collapsed;
        }

        private void CrashSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _showAllAppCrashes = false;
            ApplyFilters();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportsAsync();
        }

        private void ItemLearnMore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CrashReportItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        private void SearchOnline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CrashReportItem item)
            {
                try
                {
                    string query = item.SearchQuery;
                    if (string.IsNullOrEmpty(query)) query = item.Title;

                    string url = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportPage] Search online failed: {ex.Message}");
                }
            }
        }

        private void LocateDump_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CrashReportItem item)
            {
                if (!string.IsNullOrEmpty(item.FilePathOrDump) && File.Exists(item.FilePathOrDump))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{item.FilePathOrDump}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"[CrashReportPage] Failed to locate file: {ex.Message}");
                    }
                }
                else
                {
                    NotificationService.Show(
                        LocalizationHelper.GetString("CrashReportPage_Detail_LocateBtn/Text"),
                        LocalizationHelper.GetString("CrashReportPage_DumpFolder_NotFound"),
                        InfoBarSeverity.Informational);
                }
            }
        }

        private async void CopyReport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CrashReportItem item)
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

                string reportText = $"[{item.DisplayBadge}] {item.Title}\n" +
                                    $"{lblTime}: {item.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                                    $"{lblCode}: {item.ErrorCode}\n" +
                                    $"{lblModule}: {item.FaultingModule}\n" +
                                    $"{lblSource}: {item.SourceOrApp}\n" +
                                    (!string.IsNullOrEmpty(item.Parameters) ? $"{lblParams}: {item.Parameters}\n" : "") +
                                    (!string.IsNullOrEmpty(item.FilePathOrDump) ? $"{lblDump}: {item.FilePathOrDump}\n" : "") +
                                    $"\n{lblDiag}:\n{item.ErrorDescription}\n" +
                                    $"\n{lblRec}:\n{item.Recommendation}\n" +
                                    (!string.IsNullOrEmpty(item.RawDetails) ? $"\n--- {lblRaw} ---\n{item.RawDetails}" : "");

                string successMsg = LocalizationHelper.GetString("CrashReportPage_Detail_Copied");
                await ClipboardService.CopyTextAsync(reportText, btn, showNotification: true, successMsg);
            }
        }

        private async void ShowRawLogDialog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CrashReportItem item)
            {
                string rawText = string.IsNullOrEmpty(item.RawDetails)
                    ? LocalizationHelper.GetString("CrashReportPage_RawLog_None")
                    : item.RawDetails;

                var textBlock = new TextBlock
                {
                    Text = rawText,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
                    FontSize = 12,
                    LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                };

                var scrollViewer = new ScrollViewer
                {
                    Content = textBlock,
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var dialog = new ContentDialog
                {
                    Title = $"{item.Title} - {LocalizationHelper.GetString("CrashReportPage_Detail_RawLogHeader/Text")}",
                    Content = scrollViewer,
                    CloseButtonText = LocalizationHelper.GetString("Common_Close"),
                    PrimaryButtonText = LocalizationHelper.GetString("CrashReportPage_Detail_CopyBtn/Text"),
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await ClipboardService.CopyTextAsync(rawText, btn, showNotification: true);
                }
            }
        }

        private void OpenDumpFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetDir = string.Empty;

                // 1. Check registry-configured MinidumpDir
                string configuredDir = CrashReportService.GetConfiguredDumpDirectory();
                if (Directory.Exists(configuredDir))
                {
                    targetDir = configuredDir;
                }

                // 2. If configured dir does not exist, check if any BSOD item has an existing dump file/directory
                if (string.IsNullOrEmpty(targetDir))
                {
                    var existingDumpItem = _allReports?.FirstOrDefault(x => x.Type == CrashType.Bsod &&
                        !string.IsNullOrWhiteSpace(x.FilePathOrDump) &&
                        File.Exists(x.FilePathOrDump));

                    if (existingDumpItem != null && !string.IsNullOrWhiteSpace(existingDumpItem.FilePathOrDump))
                    {
                        string? parent = Path.GetDirectoryName(existingDumpItem.FilePathOrDump);
                        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                        {
                            targetDir = parent;
                        }
                    }
                }

                // 3. Fallback to default %WINDIR%\Minidump
                if (string.IsNullOrEmpty(targetDir))
                {
                    string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                    if (Directory.Exists(defaultDir))
                    {
                        targetDir = defaultDir;
                    }
                }

                // 4. Open folder or notify user
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    string safeDir = targetDir.Trim().Trim('\"');
                    if (safeDir.EndsWith('\\') && !safeDir.EndsWith(@":\"))
                    {
                        safeDir = safeDir.TrimEnd('\\');
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{safeDir}\"",
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
