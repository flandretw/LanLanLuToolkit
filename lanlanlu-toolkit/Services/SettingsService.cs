using System;
using System.IO;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace lanlanlu_toolkit.Services
{
    public static class SettingsService
    {
        private const string AppFolderName = "lanlanlu_toolkit";
        private const string LanguageFileName = "language.txt";
        private const string ThemeFileName = "theme.txt";

        private static string GetAppFolder()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(localAppData, AppFolderName);
            
            if (!Directory.Exists(appFolder))
            {
                try
                {
                    Directory.CreateDirectory(appFolder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to create app folder: {ex.Message}");
                }
            }
            return appFolder;
        }

        public static string GetLanguage()
        {
            try
            {
                string filePath = Path.Combine(GetAppFolder(), LanguageFileName);
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read language setting: {ex.Message}");
            }
            return ApplicationLanguages.PrimaryLanguageOverride ?? "zh-TW";
        }

        public static void SaveLanguage(string language)
        {
            try
            {
                string filePath = Path.Combine(GetAppFolder(), LanguageFileName);
                File.WriteAllText(filePath, language);
                ApplicationLanguages.PrimaryLanguageOverride = language;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save language setting: {ex.Message}");
            }
        }

        public static string GetTheme()
        {
            try
            {
                string filePath = Path.Combine(GetAppFolder(), ThemeFileName);
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read theme setting: {ex.Message}");
            }
            return "Default";
        }

        public static void SaveTheme(string theme)
        {
            try
            {
                string filePath = Path.Combine(GetAppFolder(), ThemeFileName);
                File.WriteAllText(filePath, theme);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save theme setting: {ex.Message}");
            }
        }

        public static ElementTheme ToElementTheme(string themeName)
        {
            return themeName switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }
}
