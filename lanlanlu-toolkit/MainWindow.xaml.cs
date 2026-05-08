using System;
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
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        private bool _isClosingConfirmed = false;
        private AppWindow? _appWindow;

        public MainWindow()
        {
            this.InitializeComponent();
            
            // 恢復為最簡潔的標題列擴展方式
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // 設定應用程式視窗圖示
            try
            {
                var hWnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);

                // 嘗試從本地檔案載入 (開發偵錯用)
                var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }
                else
                {
                    // 單一檔案模式：從 EXE 內部的資源提取第 0 個圖示 (最穩定)
                    string? exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var hIcon = ExtractIcon(GetModuleHandle(null), exePath, 0);
                        if (hIcon != IntPtr.Zero && hIcon != (IntPtr)1) // 1 表示找不到
                        {
                            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                            _appWindow.SetIcon(iconId);
                        }
                    }
                }
            }
            catch { }
            
            // 初始化全域通知服務
            NotificationService.Initialize(AppInfoBar);

            // 初始化標題列顏色與主題監聽
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                UpdateTitleBarColors();
                if (this.Content is FrameworkElement rootElement)
                {
                    rootElement.ActualThemeChanged += (s, e) => UpdateTitleBarColors();
                }
            }

            // 註冊視窗關閉事件以進行防呆檢查
            if (_appWindow != null)
            {
                _appWindow.Closing += AppWindow_Closing;
            }
        }

        private void UpdateTitleBarColors()
        {
            if (_appWindow == null || _appWindow.TitleBar == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // 根據主題決定基礎顏色
            var isDark = (this.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            var baseColor = isDark ? Colors.White : Colors.Black;
            byte overlay = (byte)(isDark ? 0xFF : 0x00);

            titleBar.ButtonForegroundColor = titleBar.ButtonHoverForegroundColor = titleBar.ButtonPressedForegroundColor = baseColor;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x33, overlay, overlay, overlay);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x66, overlay, overlay, overlay);
        }

        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (SystemRepairPage.IsAnyProcessRunning && !_isClosingConfirmed)
            {
                // 先取消關閉事件
                args.Cancel = true;

                var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                var dialog = new ContentDialog
                {
                    Title = resourceLoader.GetString("SystemRepairPage_QuitTitle"),
                    Content = resourceLoader.GetString("SystemRepairPage_QuitContent"),
                    PrimaryButtonText = resourceLoader.GetString("SystemRepairPage_QuitConfirm"),
                    CloseButtonText = resourceLoader.GetString("SystemRepairPage_QuitCancel"),
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


        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                var tag = args.InvokedItemContainer.Tag?.ToString();
                
                switch (tag)
                {
                    case "HomePage":
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                    case "PerformancePage":
                        ContentFrame.Navigate(typeof(PerformancePage));
                        break;
                    case "TestToolPage":
                        ContentFrame.Navigate(typeof(TestToolPage));
                        break;
                    case "SystemRepairPage":
                        ContentFrame.Navigate(typeof(SystemRepairPage));
                        break;
                }
            }
        }
    }
}
