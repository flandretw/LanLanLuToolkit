using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using lanlanlu_toolkit.Views;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit
{
    public sealed partial class MainWindow : Window
    {
        private bool _isClosingConfirmed = false;

        public MainWindow()
        {
            this.InitializeComponent();
            
            // 恢復為最簡潔的標題列擴展方式
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // 設定應用程式視窗圖示
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(iconPath);
            
            // 初始化全域通知服務
            NotificationService.Initialize(AppInfoBar);

            // 註冊視窗關閉事件以進行防呆檢查
            appWindow.Closing += AppWindow_Closing;
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
