using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitialized = false;
        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _resourceLoader;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadCurrentLanguage();
            LoadCurrentTheme();
            LoadNotificationSound();
            InitializeAboutInfo();
            _isInitialized = true;
        }

        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader GetResourceLoader()
        {
            if (_resourceLoader == null)
            {
                try
                {
                    _resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                }
                catch
                {
                    // Fallback or handle appropriately
                    _resourceLoader = null;
                }
            }
            return _resourceLoader!;
        }

        private void LoadCurrentLanguage()
        {
            string currentLang = SettingsService.GetLanguage();

            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadCurrentTheme()
        {
            string currentTheme = SettingsService.GetTheme();

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedLang = selectedItem.Tag?.ToString() ?? "zh-TW";
                var loader = GetResourceLoader();

                ContentDialog dialog = new ContentDialog
                {
                    Title             = loader?.GetString("LanguageChangeDialog_Title")   ?? "需要重新啟動",
                    Content           = loader?.GetString("LanguageChangeDialog_Content") ?? "變更語言需要重新啟動應用程式才會生效。確定要儲存變更嗎？",
                    PrimaryButtonText = loader?.GetString("LanguageChangeDialog_Confirm") ?? "確定",
                    CloseButtonText   = loader?.GetString("LanguageChangeDialog_Cancel")  ?? "取消",
                    XamlRoot          = this.XamlRoot
                };

                // 按「確定」：儲存設定並關閉程式
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    SettingsService.SaveLanguage(selectedLang);
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };

                // 按「取消」：恢復原本的選擇
                dialog.CloseButtonClick += (_, _) =>
                {
                    _isInitialized = false;
                    LoadCurrentLanguage();
                    _isInitialized = true;
                };

                // 顯示對話框
                _ = dialog.ShowAsync();
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedTheme = selectedItem.Tag?.ToString() ?? "Default";
                SettingsService.SaveTheme(selectedTheme);

                if (App.MainWindow != null && App.MainWindow.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = SettingsService.ToElementTheme(selectedTheme);
                }
            }
        }

        private void InitializeAboutInfo()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                var loader = GetResourceLoader();
                if (loader == null) return;

                string appName = loader.GetString("SettingsPage_AppVersion/Text");
                string copyright = loader.GetString("SettingsPage_Copyright/Text");

                if (version != null)
                {
                    AboutInfoTextBlock.Text = $"{appName} {version.Major}.{version.Minor}.{version.Build}\n{copyright}";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize about info: {ex.Message}");
            }
        }

        private void LoadNotificationSound()
        {
            NotificationSoundToggle.IsOn = SettingsService.GetNotificationSound();
        }

        private void NotificationSoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            SettingsService.SaveNotificationSound(NotificationSoundToggle.IsOn);
        }
    }
}
