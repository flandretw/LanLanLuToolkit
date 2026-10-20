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
        public bool SoundEffectsEnabled { get; set; } = true;
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
        private static string? _activeSettingsFilePath;

        private static string GetPortableSettingsPath()
        {
            string? exePath = Environment.ProcessPath;
            string exeFolder = !string.IsNullOrEmpty(exePath) 
                ? Path.GetDirectoryName(Path.GetFullPath(exePath))! 
                : AppDomain.CurrentDomain.BaseDirectory;

            return Path.Combine(exeFolder, "Data", SettingsFileName);
        }

        private static string GetLocalAppDataSettingsPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, AppFolderName, SettingsFileName);
        }

        private static bool CanWriteToDirectory(string dirPath)
        {
            try
            {
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                string testFile = Path.Combine(dirPath, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveSettingsFilePath(bool forWriting = false)
        {
            if (_activeSettingsFilePath != null)
            {
                if (forWriting)
                {
                    string dir = Path.GetDirectoryName(_activeSettingsFilePath)!;
                    if (CanWriteToDirectory(dir))
                    {
                        return _activeSettingsFilePath;
                    }

                    // Active path became unwritable; fall back to LocalAppData
                    _activeSettingsFilePath = GetLocalAppDataSettingsPath();
                    return _activeSettingsFilePath;
                }
                return _activeSettingsFilePath;
            }

            string portablePath = GetPortableSettingsPath();
            string portableDir = Path.GetDirectoryName(portablePath)!;
            string localAppDataPath = GetLocalAppDataSettingsPath();

            bool portableExists = File.Exists(portablePath);
            bool localAppDataExists = File.Exists(localAppDataPath);

            if (portableExists && localAppDataExists)
            {
                // Both exist: if portable directory is not writable, LocalAppData must be the active one
                if (!CanWriteToDirectory(portableDir))
                {
                    _activeSettingsFilePath = localAppDataPath;
                    return _activeSettingsFilePath;
                }

                // Both are available; choose the one with the latest modification time
                DateTime portableTime = File.GetLastWriteTimeUtc(portablePath);
                DateTime localTime = File.GetLastWriteTimeUtc(localAppDataPath);

                _activeSettingsFilePath = (localTime > portableTime) ? localAppDataPath : portablePath;
                return _activeSettingsFilePath;
            }

            if (portableExists)
            {
                if (!forWriting || CanWriteToDirectory(portableDir))
                {
                    _activeSettingsFilePath = portablePath;
                    return _activeSettingsFilePath;
                }

                _activeSettingsFilePath = localAppDataPath;
                return _activeSettingsFilePath;
            }

            if (localAppDataExists)
            {
                _activeSettingsFilePath = localAppDataPath;
                return _activeSettingsFilePath;
            }

            // Neither file exists yet. Prefer portable if directory is writable, otherwise LocalAppData
            if (CanWriteToDirectory(portableDir))
            {
                _activeSettingsFilePath = portablePath;
            }
            else
            {
                _activeSettingsFilePath = localAppDataPath;
            }

            return _activeSettingsFilePath;
        }

        private static void LoadSettings()
        {
            if (_currentSettings != null) return;

            try
            {
                string filePath = ResolveSettingsFilePath(forWriting: false);
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    _currentSettings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);
                }
                else
                {
                    // Fallback check: if primary path didn't exist, check LocalAppData as well
                    string fallbackPath = GetLocalAppDataSettingsPath();
                    if (File.Exists(fallbackPath))
                    {
                        string json = File.ReadAllText(fallbackPath);
                        _currentSettings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);
                        _activeSettingsFilePath = fallbackPath;
                    }
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
                string filePath = ResolveSettingsFilePath(forWriting: true);
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(_currentSettings, AppSettingsContext.Default.AppSettings);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");

                // If writing to the primary path failed, fall back to LocalAppData
                try
                {
                    string fallbackPath = GetLocalAppDataSettingsPath();
                    string? fallbackDir = Path.GetDirectoryName(fallbackPath);
                    if (!string.IsNullOrEmpty(fallbackDir) && !Directory.Exists(fallbackDir))
                    {
                        Directory.CreateDirectory(fallbackDir);
                    }

                    string json = JsonSerializer.Serialize(_currentSettings, AppSettingsContext.Default.AppSettings);
                    File.WriteAllText(fallbackPath, json);
                    _activeSettingsFilePath = fallbackPath;
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Failed to save settings to fallback: {fallbackEx.Message}");
                }
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

        public static bool GetSoundEffectsEnabled()
        {
            LoadSettings();
            return _currentSettings?.SoundEffectsEnabled ?? true;
        }

        public static void SaveSoundEffectsEnabled(bool enabled)
        {
            LoadSettings();
            if (_currentSettings != null)
            {
                _currentSettings.SoundEffectsEnabled = enabled;
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

            // Default path: Prefer executable directory if writable; otherwise LocalAppData
            try
            {
                string? exePath = Environment.ProcessPath;
                string exeFolder = !string.IsNullOrEmpty(exePath) 
                    ? Path.GetDirectoryName(Path.GetFullPath(exePath))! 
                    : AppDomain.CurrentDomain.BaseDirectory;

                if (CanWriteToDirectory(exeFolder))
                {
                    return exeFolder;
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string fallbackFolder = Path.Combine(localAppData, AppFolderName);
                if (!Directory.Exists(fallbackFolder))
                {
                    Directory.CreateDirectory(fallbackFolder);
                }
                return fallbackFolder;
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
