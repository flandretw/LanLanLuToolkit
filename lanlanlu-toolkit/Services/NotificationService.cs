using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Services
{
    public static class NotificationService
    {
        private static InfoBar? _appInfoBar;
        
        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint uType);
        private const uint MB_ICONASTERISK = 0x00000040;

        /// <summary>
        /// Initialize service and bind to the MainWindow's InfoBar
        /// </summary>
        public static void Initialize(InfoBar infoBar)
        {
            _appInfoBar = infoBar;
        }

        /// <summary>
        /// Show global notification
        /// </summary>
        /// <param name="title">Title</param>
        /// <param name="message">Message content</param>
        /// <param name="severity">Severity (default Success)</param>
        public static void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success)
        {
            if (_appInfoBar == null) return;

            _appInfoBar.Title = title;
            _appInfoBar.Message = message;
            _appInfoBar.Severity = severity;
            _appInfoBar.IsOpen = true;

            // Check settings to determine whether to play sound
            if (SettingsService.GetNotificationSound())
            {
                try
                {
                    MessageBeep(MB_ICONASTERISK);
                }
                catch { /* Ignore audio errors */ }
            }
        }
    }
}
