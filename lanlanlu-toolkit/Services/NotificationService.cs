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
        /// 初始化服務，與 MainWindow 的 InfoBar 綁定
        /// </summary>
        public static void Initialize(InfoBar infoBar)
        {
            _appInfoBar = infoBar;
        }

        /// <summary>
        /// 顯示全域通知
        /// </summary>
        /// <param name="title">標題</param>
        /// <param name="message">訊息內容</param>
        /// <param name="severity">嚴重程度 (預設 Success)</param>
        public static void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success)
        {
            if (_appInfoBar == null) return;

            _appInfoBar.Title = title;
            _appInfoBar.Message = message;
            _appInfoBar.Severity = severity;
            _appInfoBar.IsOpen = true;

            // 檢查設定決定是否播放聲音
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
