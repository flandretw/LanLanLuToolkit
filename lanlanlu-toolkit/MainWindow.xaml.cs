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
            
            // Use the most concise way to extend the title bar
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // Set application window icon
            try
            {
                var hWnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
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

            // Initialize title bar colors and theme listener
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                UpdateTitleBarColors();
                if (this.Content is FrameworkElement rootElement)
                {
                    rootElement.ActualThemeChanged += (s, e) => UpdateTitleBarColors();
                }
            }

            // Register window closing event for safety check
            if (_appWindow != null)
            {
                _appWindow.Closing += AppWindow_Closing;
            }

            // Register navigation completion event to sync sidebar menu state
            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void UpdateTitleBarColors()
        {
            if (_appWindow == null || _appWindow.TitleBar == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Determine base color based on theme
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
                // Cancel the closing event first
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
                if (ContentFrame.SourcePageType != typeof(SettingsPage))
                {
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
            }
            else
            {
                var tag = args.InvokedItemContainer.Tag?.ToString();
                Type? targetType = tag switch
                {
                    "HomePage" => typeof(HomePage),
                    "PerformancePage" => typeof(PerformancePage),
                    "TestToolPage" => typeof(TestToolPage),
                    "SystemRepairPage" => typeof(SystemRepairPage),
                    _ => null
                };

                if (targetType != null && ContentFrame.SourcePageType != targetType)
                {
                    ContentFrame.Navigate(targetType);
                }
            }
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.SourcePageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = (NavigationViewItem)NavView.SettingsItem;
                return;
            }

            var tag = e.SourcePageType.Name;
            var item = FindNavigationViewItem(NavView.MenuItems, tag);
            if (item != null && (NavView.SelectedItem as NavigationViewItem) != item)
            {
                NavView.SelectedItem = item;
            }
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
