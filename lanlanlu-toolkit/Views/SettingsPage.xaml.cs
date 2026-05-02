using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Globalization;

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
            InitializeAboutInfo();
            _isInitialized = true;
        }

        private string GetThemeFilePath()
        {
            string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string appFolder = System.IO.Path.Combine(localAppData, "lanlanlu_toolkit");
            if (!System.IO.Directory.Exists(appFolder))
            {
                System.IO.Directory.CreateDirectory(appFolder);
            }
            return System.IO.Path.Combine(appFolder, "theme.txt");
        }

        private string GetSettingsFilePath()
        {
            string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string appFolder = System.IO.Path.Combine(localAppData, "lanlanlu_toolkit");
            if (!System.IO.Directory.Exists(appFolder))
            {
                System.IO.Directory.CreateDirectory(appFolder);
            }
            return System.IO.Path.Combine(appFolder, "language.txt");
        }

        private void LoadCurrentLanguage()
        {
            string currentLang = "zh-TW";
            try
            {
                string settingsFile = GetSettingsFilePath();
                if (System.IO.File.Exists(settingsFile))
                {
                    currentLang = System.IO.File.ReadAllText(settingsFile).Trim();
                }
                else if (!string.IsNullOrEmpty(ApplicationLanguages.PrimaryLanguageOverride))
                {
                    currentLang = ApplicationLanguages.PrimaryLanguageOverride;
                }
            }
            catch { }


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
            string currentTheme = "Default";
            try
            {
                string settingsFile = GetThemeFilePath();
                if (System.IO.File.Exists(settingsFile))
                {
                    currentTheme = System.IO.File.ReadAllText(settingsFile).Trim();
                }
            }
            catch { }

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

                // 讀取本地化字串（使用 Windows App SDK 的 ResourceLoader，支援打包與非打包模式）
                Microsoft.Windows.ApplicationModel.Resources.ResourceLoader loader;
                try
                {
                    loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                }
                catch
                {
                    loader = null!;
                }

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
                    try
                    {
                        System.IO.File.WriteAllText(GetSettingsFilePath(), selectedLang);
                    }
                    catch { }

                    ApplicationLanguages.PrimaryLanguageOverride = selectedLang;

                    // 立即關閉應用程式
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };

                // 按「取消」：恢復原本的選擇
                dialog.CloseButtonClick += (_, _) =>
                {
                    _isInitialized = false;
                    LoadCurrentLanguage();
                    _isInitialized = true;
                };

                // 顯示對話框（不需要 await）
                _ = dialog.ShowAsync();
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedTheme = selectedItem.Tag?.ToString() ?? "Default";

                try
                {
                    System.IO.File.WriteAllText(GetThemeFilePath(), selectedTheme);
                }
                catch { }

                if (App.MainWindow != null && App.MainWindow.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = selectedTheme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }

        private void InitializeAboutInfo()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                
                Microsoft.Windows.ApplicationModel.Resources.ResourceLoader loader;
                try { loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(); } catch { return; }

                string appName = loader.GetString("SettingsPage_AppVersion/Text");
                string copyright = loader.GetString("SettingsPage_Copyright/Text");

                if (version != null)
                {
                    AboutInfoTextBlock.Text = $"{appName} {version.Major}.{version.Minor}.{version.Build}\n{copyright}";
                }
            }
            catch { }
        }
    }
}
