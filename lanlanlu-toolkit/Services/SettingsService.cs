using System;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace lanlanlu_toolkit.Services
{
    public class AppSettings
    {
        public string Language { get; set; } = "en-US";
        public string Theme { get; set; } = "Default";
        public bool NotificationsEnabled { get; set; } = true;
        public bool NotificationSoundEnabled { get; set; } = true;
        public string TemperatureUnit { get; set; } = "Celsius";
        public bool DebugReportEnabled { get; set; } = false;
        public string? DebugReportPath { get; set; } = null;
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(AppSettings))]
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppSettingsContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }

    public static class SettingsService
    {
        private const string AppFolderName = "lanlanlu_toolkit";
        private const string SettingsFileName = "settings.json";
        private static AppSettings? _currentSettings;

        private static string GetAppFolder(bool createIfMissing)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                string exeFolder = !string.IsNullOrEmpty(exePath) 
                    ? Path.GetDirectoryName(Path.GetFullPath(exePath))! 
                    : AppDomain.CurrentDomain.BaseDirectory;
                
                string portableFolder = Path.Combine(exeFolder, "Data");

                if (createIfMissing)
                {
                    if (!Directory.Exists(portableFolder))
                    {
                        Directory.CreateDirectory(portableFolder);
                    }

                    // Permission test
                    string testFile = Path.Combine(portableFolder, ".write_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }

                return portableFolder;
            }
            catch (Exception ex)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string fallbackFolder = Path.Combine(localAppData, AppFolderName);
                
                if (createIfMissing && !Directory.Exists(fallbackFolder))
                {
                    Directory.CreateDirectory(fallbackFolder);
                }
                
                Debug.WriteLine($"Local folder access issue, using AppData fallback: {ex.Message}");
                return fallbackFolder;
            }
        }

        private static void LoadSettings()
        {
            if (_currentSettings != null) return;

            try
            {
                // Pass false when reading, do not proactively create folders
                string filePath = Path.Combine(GetAppFolder(false), SettingsFileName);
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    _currentSettings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            if (_currentSettings == null)
            {
                _currentSettings = new AppSettings { Language = GetDefaultLanguage() };
            }
        }

        private static void SaveSettings()
        {
            if (_currentSettings == null) return;

            try
            {
                // Only pass true when saving, this is when the folder will be created
                string filePath = Path.Combine(GetAppFolder(true), SettingsFileName);
                string json = JsonSerializer.Serialize(_currentSettings, AppSettingsContext.Default.AppSettings);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private static string GetDefaultLanguage()
        {
            string systemLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name;
            return systemLanguage.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "en-US";
        }

        public static string GetLanguage()
        {
            LoadSettings();
            return _currentSettings?.Language ?? GetDefaultLanguage();
        }

        public static void SaveLanguage(string language)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.Language = language;
                SaveSettings();
            }
            ApplicationLanguages.PrimaryLanguageOverride = language;
        }

        public static string GetTheme()
        {
            LoadSettings();
            return _currentSettings?.Theme ?? "Default";
        }

        public static void SaveTheme(string theme)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.Theme = theme;
                SaveSettings();
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

        public static bool GetNotificationsEnabled()
        {
            LoadSettings();
            return _currentSettings?.NotificationsEnabled ?? true;
        }

        public static void SaveNotificationsEnabled(bool enabled)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.NotificationsEnabled = enabled;
                SaveSettings();
            }
        }

        public static bool GetNotificationSound()
        {
            LoadSettings();
            return _currentSettings?.NotificationSoundEnabled ?? true;
        }

        public static void SaveNotificationSound(bool enabled)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.NotificationSoundEnabled = enabled;
                SaveSettings();
            }
        }

        public static string GetTemperatureUnit()
        {
            LoadSettings();
            return _currentSettings?.TemperatureUnit ?? "Celsius";
        }

        public static void SaveTemperatureUnit(string unit)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.TemperatureUnit = unit;
                SaveSettings();
            }
        }

        public static bool GetDebugReportEnabled()
        {
            LoadSettings();
            return _currentSettings?.DebugReportEnabled ?? false;
        }

        public static void SaveDebugReportEnabled(bool enabled)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.DebugReportEnabled = enabled;
                SaveSettings();
            }
        }

        public static string GetDebugReportPath()
        {
            LoadSettings();
            if (_currentSettings != null && !string.IsNullOrEmpty(_currentSettings.DebugReportPath))
            {
                return _currentSettings.DebugReportPath;
            }

            // Default path: Same directory as executable
            try
            {
                string? exePath = Environment.ProcessPath;
                return !string.IsNullOrEmpty(exePath) 
                    ? Path.GetDirectoryName(Path.GetFullPath(exePath))! 
                    : AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static void SaveDebugReportPath(string path)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.DebugReportPath = path;
                SaveSettings();
            }
        }
    }
}
