using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace lanlanlu_toolkit.Services
{
    public static class LoggingService
    {
        private static readonly string LogFileName = "debug_report.log";
        private static readonly object _lock = new object();

        /// <summary>
        /// Logs a message to the debug report file if the feature is enabled.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            // Check if debug report is enabled in settings
            if (!SettingsService.GetDebugReportEnabled())
            {
                return;
            }

            try
            {
                string logDirectory = SettingsService.GetDebugReportPath();
                
                // Ensure the directory exists
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string logPath = Path.Combine(logDirectory, LogFileName);
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                // Use lock to ensure thread safety when writing to the file
                lock (_lock)
                {
                    File.AppendAllText(logPath, logEntry, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        /// <summary>
        /// Asynchronously logs a message to the debug report file.
        /// </summary>
        public static async Task LogAsync(string message)
        {
            await Task.Run(() => Log(message));
        }
    }
}
