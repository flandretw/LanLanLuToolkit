using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using WinRT.Interop;
using lanlanlu_toolkit.Views;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private const uint WM_GETMINMAXINFO = 0x0024;
        private const uint WM_NCDESTROY = 0x0082;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private bool _isClosingConfirmed = false;
        private AppWindow? _appWindow;
        private readonly List<ToolItem> _searchableTools = new();
        private readonly SubclassProc _subclassProc;
        private IntPtr _hWnd;

        public MainWindow()
        {
            this.InitializeComponent();

            _hWnd = WindowNative.GetWindowHandle(this);
            _subclassProc = new SubclassProc(WindowSubclassHandler);
            SetWindowSubclass(_hWnd, _subclassProc, UIntPtr.Zero, IntPtr.Zero);
            
            // Use the most concise way to extend the title bar
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // Set application window icon
            try
            {
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);

                // Try to load from local file (for development/debugging)
                var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }
                else
                {
                    // Single file mode: Extract the 0th icon from the EXE internal resources (most stable)
                    string? exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var hIcon = ExtractIcon(GetModuleHandle(null), exePath, 0);
                        if (hIcon != IntPtr.Zero && hIcon != (IntPtr)1) // 1 indicates not found
                        {
                            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                            _appWindow.SetIcon(iconId);
                        }
                    }
                }
            }
            catch { }
            
            // Initialize global notification service
            NotificationService.Initialize(AppInfoBar);

            // Initialize title bar colors, insets and theme listener
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                UpdateTitleBarColors();
                UpdateTitleBarInsets();
                if (this.Content is FrameworkElement rootElement)
                {
                    rootElement.ActualThemeChanged += (s, e) => UpdateTitleBarColors();
                    rootElement.Loaded += (s, e) =>
                    {
                        UpdateTitleBarInsets();
                        UpdateTitleBarPassthroughRegions();
                        if (rootElement.XamlRoot != null)
                        {
                            rootElement.XamlRoot.Changed -= OnXamlRootChanged;
                            rootElement.XamlRoot.Changed += OnXamlRootChanged;
                        }
                    };
                }
            }

            // Register window closing and changed event for safety check and memory trimming
            if (_appWindow != null)
            {
                _appWindow.Closing += AppWindow_Closing;
                _appWindow.Changed += AppWindow_Changed;
            }

            // Register navigation completion event to sync sidebar menu state
            ContentFrame.Navigated += ContentFrame_Navigated;

            // Initialize searchable tools index
            InitializeSearchableTools();

            // Set up passthrough region for TitleBar search box so cursor becomes I-Beam and input is client-handled
            AppTitleBar.SizeChanged += (s, e) =>
            {
                UpdateTitleBarInsets();
                UpdateTitleBarPassthroughRegions();
            };
            NavSearchBox.SizeChanged += (s, e) => UpdateTitleBarPassthroughRegions();
            NavSearchBox.Loaded += (s, e) =>
            {
                UpdateTitleBarInsets();
                UpdateTitleBarPassthroughRegions();
                if (NavSearchBox.XamlRoot != null)
                {
                    NavSearchBox.XamlRoot.Changed -= OnXamlRootChanged;
                    NavSearchBox.XamlRoot.Changed += OnXamlRootChanged;
                }
            };
        }

        private IntPtr WindowSubclassHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                uint dpi = GetDpiForWindow(hWnd);
                float scalingFactor = (dpi > 0 ? dpi : 96f) / 96f;

                const int minWidthDip = 840;
                const int minHeightDip = 560;

                mmi.ptMinTrackSize.x = Math.Max(mmi.ptMinTrackSize.x, (int)(minWidthDip * scalingFactor));
                mmi.ptMinTrackSize.y = Math.Max(mmi.ptMinTrackSize.y, (int)(minHeightDip * scalingFactor));

                Marshal.StructureToPtr(mmi, lParam, true);
            }
            else if (uMsg == WM_NCDESTROY)
            {
                RemoveWindowSubclass(hWnd, _subclassProc, uIdSubclass);
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            UpdateTitleBarInsets();
            UpdateTitleBarPassthroughRegions();
        }

        private void UpdateTitleBarInsets()
        {
            if (_appWindow == null || TitleBarRightSpacer == null) return;

            double scale = 1.0;
            if (this.Content?.XamlRoot != null)
            {
                scale = this.Content.XamlRoot.RasterizationScale;
            }
            else if (_hWnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(_hWnd);
                scale = (dpi > 0 ? dpi : 96.0) / 96.0;
            }
            if (scale <= 0) scale = 1.0;

            if (AppWindowTitleBar.IsCustomizationSupported() && _appWindow.TitleBar != null)
            {
                int rightInset = _appWindow.TitleBar.RightInset;
                TitleBarRightSpacer.Width = rightInset > 0 ? (rightInset / scale) : 144;

                int leftInset = _appWindow.TitleBar.LeftInset;
                if (TitleBarLeftSpacer != null)
                {
                    TitleBarLeftSpacer.Width = leftInset > 0 ? (leftInset / scale) : 0;
                }
            }
        }

        private void UpdateTitleBarPassthroughRegions()
        {
            try
            {
                if (_appWindow == null || NavSearchBox == null || NavSearchBox.XamlRoot == null) return;
                if (NavSearchBox.ActualWidth <= 0 || NavSearchBox.ActualHeight <= 0) return;

                double scale = NavSearchBox.XamlRoot.RasterizationScale;
                var transform = NavSearchBox.TransformToVisual(null);
                var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, NavSearchBox.ActualWidth, NavSearchBox.ActualHeight));

                var regionRect = new Windows.Graphics.RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale)
                );

                var nonClientInputSrc = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
                nonClientInputSrc.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, new Windows.Graphics.RectInt32[] { regionRect });
            }
            catch { }
        }

        private void UpdateTitleBarColors()
        {
            if (_appWindow == null || _appWindow.TitleBar == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

            var titleBar = _appWindow.TitleBar;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Determine base color based on theme
            var isDark = (this.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            var baseColor = isDark ? Colors.White : Colors.Black;

            titleBar.ButtonForegroundColor = baseColor;
            titleBar.ButtonHoverForegroundColor = baseColor;
            titleBar.ButtonPressedForegroundColor = baseColor;
            titleBar.ButtonInactiveForegroundColor = isDark ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0x00, 0x00, 0x00);

            // Let Windows 11 handle standard hover animations and red close button highlighting
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
        }

        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (SystemRepairPage.IsAnyProcessRunning && !_isClosingConfirmed)
            {
                // Cancel the closing event first
                args.Cancel = true;

                var dialog = new ContentDialog
                {
                    Title = LocalizationHelper.GetString("SystemRepairPage_QuitTitle"),
                    Content = LocalizationHelper.GetString("SystemRepairPage_QuitContent"),
                    PrimaryButtonText = LocalizationHelper.GetString("SystemRepairPage_QuitConfirm"),
                    CloseButtonText = LocalizationHelper.GetString("SystemRepairPage_QuitCancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _isClosingConfirmed = true;
                    this.Close();
                }
            }
        }


        private void InitializeSearchableTools()
        {
            _searchableTools.Clear();

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_HomePage/Content"),
                Description = LocalizationHelper.GetString("Nav_HomePage/Content"),
                Icon = "\uE80F",
                Tag = "HomePage",
                Category = LocalizationHelper.GetString("Category_System"),
                Keywords = new() { "首頁", "home", "硬體資訊", "規格", "硬體", "cpu", "gpu", "ram", "主機板" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_InputTesterPage/Content"),
                Description = LocalizationHelper.GetString("InputTesterPage_Subtitle/Text"),
                Icon = "\uE765",
                Tag = "InputTesterPage",
                Category = LocalizationHelper.GetString("Nav_TestToolPage/Content"),
                Keywords = new() { "鍵盤", "滑鼠", "連點", "微動", "按鍵", "防鬼鍵", "無衝", "NKRO", "回報率", "Hz", "滾輪", "跳幀", "keyboard", "mouse", "input", "tester" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_CrashReportPage/Content"),
                Description = LocalizationHelper.GetString("CrashReportPage_Subtitle/Text"),
                Icon = "\uE7BA",
                Tag = "CrashReportPage",
                Category = LocalizationHelper.GetString("Nav_TestToolPage/Content"),
                Keywords = new() { "當機", "藍屏", "藍當", "BSOD", "崩潰", "死機", "dump", "minidump", "crash", "bugcheck", "傾印", "當機分析", "藍色畫面", "核心損毀" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_SystemRepairPage/Content"),
                Description = LocalizationHelper.GetString("SystemRepairPage_Desc/Text"),
                Icon = "\uE762",
                Tag = "SystemRepairPage",
                Category = LocalizationHelper.GetString("Nav_TestToolPage/Content"),
                Keywords = new() { "修復", "系統修復", "sfc", "dism", "scannow", "元件存放庫", "系統損毀", "系統還原", "完整性", "repair", "windows修復" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_FileHashPage/Content"),
                Description = LocalizationHelper.GetString("FileHashPage_Desc/Text"),
                Icon = "\uEC19",
                Tag = "FileHashPage",
                Category = LocalizationHelper.GetString("Nav_UtilityPage/Content"),
                Keywords = new() { "雜湊", "hash", "sha256", "sha-256", "md5", "sha1", "sha512", "校驗", "比對", "檢查碼", "checksum", "檔案完整性" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_ResetFileTypeDefaultsPage/Content"),
                Description = LocalizationHelper.GetString("ResetFileTypeDefaultsPage_Desc/Text"),
                Icon = "\uE7AC",
                Tag = "ResetFileTypeDefaultsPage",
                Category = LocalizationHelper.GetString("Nav_UtilityPage/Content"),
                Keywords = new() { "預設應用程式", "副檔名", "開啟程式", "預設值", "重設", "解除關聯", "default apps", "file type", "extension", "reset", "association", "開啟方式" }
            });

            _searchableTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("HomePage_AppSettings/Text"),
                Description = LocalizationHelper.GetString("HomePage_AppSettings/Text"),
                Icon = "\uE713",
                Tag = "SettingsPage",
                Category = LocalizationHelper.GetString("Category_Settings"),
                Keywords = new() { "設定", "主題", "深色", "淺色", "語言", "開機", "系統匣", "托盤", "settings", "theme", "language" }
            });
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            var firstItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (firstItem != null)
            {
                NavView.SelectedItem = firstItem;
            }
            ContentFrame.Navigate(typeof(HomePage));
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToTool("SettingsPage");
            }
            else if (args.InvokedItemContainer?.Tag is string tag)
            {
                NavigateToTool(tag);
            }
        }

        private void SearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            NavSearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        private void NavSearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                NavSearchBox.Text = string.Empty;
                ContentFrame.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();
                if (string.IsNullOrEmpty(query))
                {
                    sender.ItemsSource = null;
                }
                else
                {
                    var matches = _searchableTools.Where(t => t.Matches(query)).ToList();
                    sender.ItemsSource = matches;
                }
            }
        }

        private void NavSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is ToolItem selectedTool)
            {
                sender.Text = selectedTool.Title;
            }
        }

        private void NavSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is ToolItem tool)
            {
                NavigateToTool(tool.Tag);
                sender.Text = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                var match = _searchableTools.FirstOrDefault(t => t.Matches(args.QueryText));
                if (match != null)
                {
                    NavigateToTool(match.Tag);
                    sender.Text = string.Empty;
                }
            }
        }

        public void NavigateToTool(string tag)
        {
            if (tag == "SettingsPage")
            {
                if (ContentFrame.SourcePageType != typeof(SettingsPage))
                {
                    LoggingService.Log("Navigating to SettingsPage");
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
                NavView.SelectedItem = (NavigationViewItem)NavView.SettingsItem;
                return;
            }

            Type? targetType = tag switch
            {
                "HomePage" => typeof(HomePage),
                "InputTesterPage" => typeof(InputTesterPage),
                "CrashReportPage" => typeof(CrashReportPage),
                "SystemRepairPage" => typeof(SystemRepairPage),
                "FileHashPage" => typeof(FileHashPage),
                "ResetFileTypeDefaultsPage" => typeof(ResetFileTypeDefaultsPage),
                _ => null
            };

            if (targetType != null)
            {
                if (ContentFrame.SourcePageType != targetType)
                {
                    LoggingService.Log($"Navigating to {targetType.Name}");
                    ContentFrame.Navigate(targetType);
                }
                var item = FindNavigationViewItem(NavView.MenuItems, tag);
                if (item != null)
                {
                    NavView.SelectedItem = item;
                }
            }
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.SourcePageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = (NavigationViewItem)NavView.SettingsItem;
            }
            else
            {
                var tag = e.SourcePageType.Name;
                var item = FindNavigationViewItem(NavView.MenuItems, tag);
                if (item != null && (NavView.SelectedItem as NavigationViewItem) != item)
                {
                    NavView.SelectedItem = item;
                }
            }
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange || args.DidPositionChange || args.DidPresenterChange)
            {
                UpdateTitleBarInsets();
            }

            if (args.DidPresenterChange)
            {
                if (sender.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        GC.Collect(2, GCCollectionMode.Optimized, false, false);
                        TrimWorkingSet();
                    });
                }
            }
        }

        public static void TrimWorkingSet()
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetCurrentProcess();
                SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        private NavigationViewItem? FindNavigationViewItem(System.Collections.Generic.IList<object> items, string tag)
        {
            foreach (var item in items)
            {
                if (item is NavigationViewItem navItem)
                {
                    if (navItem.Tag?.ToString() == tag) return navItem;
                    if (navItem.MenuItems.Count > 0)
                    {
                        var childItem = FindNavigationViewItem(navItem.MenuItems, tag);
                        if (childItem != null)
                        {
                            navItem.IsExpanded = true;
                            return childItem;
                        }
                    }
                }
            }
            return null;
        }
    }
}
