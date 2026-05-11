using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitialized = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadCurrentLanguage();
            LoadCurrentTheme();
            LoadCurrentTemperatureUnit();
            LoadNotificationSound();
            InitializeAboutInfo();
            _isInitialized = true;
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

        private void LoadCurrentTemperatureUnit()
        {
            string currentUnit = SettingsService.GetTemperatureUnit();

            foreach (ComboBoxItem item in TemperatureUnitComboBox.Items)
            {
                if (item.Tag?.ToString() == currentUnit)
                {
                    TemperatureUnitComboBox.SelectedItem = item;
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

                ContentDialog dialog = new ContentDialog
                {
                    Title             = LocalizationHelper.GetString("LanguageChangeDialog_Title")   ?? "Restart Required",
                    Content           = LocalizationHelper.GetString("LanguageChangeDialog_Content") ?? "Changing the language requires a restart to take effect. Save changes and exit?",
                    PrimaryButtonText = LocalizationHelper.GetString("LanguageChangeDialog_Confirm") ?? "Confirm",
                    CloseButtonText   = LocalizationHelper.GetString("LanguageChangeDialog_Cancel")  ?? "Cancel",
                    XamlRoot          = this.XamlRoot
                };

                // Primary button: Save settings and exit application
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    SettingsService.SaveLanguage(selectedLang);
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };

                // Close button: Restore previous selection
                dialog.CloseButtonClick += (_, _) =>
                {
                    _isInitialized = false;
                    LoadCurrentLanguage();
                    _isInitialized = true;
                };

                // Show dialog
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

        private void TemperatureUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (TemperatureUnitComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedUnit = selectedItem.Tag?.ToString() ?? "Celsius";
                SettingsService.SaveTemperatureUnit(selectedUnit);
            }
        }

        private void InitializeAboutInfo()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                string appName = LocalizationHelper.GetString("SettingsPage_AppVersion/Text");
                string copyright = LocalizationHelper.GetString("SettingsPage_Copyright/Text");

                AppNameTextBlock.Text = appName;
                AppCopyrightTextBlock.Text = copyright;

                if (version != null)
                {
                    AppVersionTextBlock.Text = $"{version.Major}.{version.Minor}.{version.Build}";
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
