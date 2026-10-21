using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Centralized clipboard service for standardizing copy and paste interactions,
    /// animated visual feedback (icons/colors), and toast notifications across the application.
    /// </summary>
    public static class ClipboardService
    {
        public const string IconCopy = "\uE8C8";
        public const string IconCheckmark = "\uE8FB";
        public const string IconPaste = "\uE77F";

        private const int DefaultFeedbackDurationMs = 1500;

        private class AnimationState
        {
            public CancellationTokenSource? Cts;
            public string OriginalGlyph = "";
            public Brush? OriginalForeground;
        }

        private static readonly ConditionalWeakTable<FontIcon, AnimationState> _animationStates = new();

        /// <summary>
        /// Copies the specified text to the system clipboard and provides optional visual feedback and notifications.
        /// </summary>
        /// <param name="text">The string content to copy.</param>
        /// <param name="visualAnchor">Optional UI element (Button, FontIcon, or container) that will display checkmark animation.</param>
        /// <param name="showNotification">Whether to show a global InfoBar notification.</param>
        /// <param name="customNotificationMessage">Custom message for the notification; defaults to localized 'Copied to clipboard'.</param>
        /// <param name="customNotificationTitle">Custom title for the notification; defaults to localized 'Success'.</param>
        /// <returns>True if copy operation succeeded; otherwise false.</returns>
        public static async Task<bool> CopyTextAsync(
            string text,
            FrameworkElement? visualAnchor = null,
            bool showNotification = false,
            string? customNotificationMessage = null,
            string? customNotificationTitle = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);

                // 1. Visual Feedback on target element
                if (visualAnchor != null)
                {
                    _ = AnimateVisualFeedbackAsync(visualAnchor);
                }

                // 2. Global Notification (typically reserved for bulk/full exports)
                if (showNotification)
                {
                    string title = customNotificationTitle 
                        ?? LocalizationHelper.GetString("Notification_Success") 
                        ?? "Success";
                    string message = customNotificationMessage 
                        ?? LocalizationHelper.GetString("Notification_Copied") 
                        ?? "Copied to clipboard";

                    NotificationService.Show(title, message, InfoBarSeverity.Success);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardService] Copy failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Retrieves plain text from the clipboard if available.
        /// </summary>
        public static async Task<string?> GetTextAsync()
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    return await content.GetTextAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardService] Paste failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Animates an element (Button or FontIcon) with a checkmark and success brush for standard duration.
        /// </summary>
        public static async Task AnimateVisualFeedbackAsync(FrameworkElement anchor, int durationMs = DefaultFeedbackDurationMs)
        {
            FontIcon? fontIcon = null;

            if (anchor is FontIcon directIcon)
            {
                fontIcon = directIcon;
            }
            else if (anchor is Button btn)
            {
                if (btn.Content is FontIcon btnIcon)
                {
                    fontIcon = btnIcon;
                }
                else if (btn.Content is Panel panel)
                {
                    fontIcon = panel.Children.OfType<FontIcon>().FirstOrDefault();
                }
            }
            else if (anchor is Panel panel)
            {
                fontIcon = panel.Children.OfType<FontIcon>().FirstOrDefault();
            }

            if (fontIcon == null) return;

            AnimationState state = _animationStates.GetOrCreateValue(fontIcon);

            if (state.Cts != null)
            {
                // Cancel previous active delay so the newer animation run takes precedence
                state.Cts.Cancel();
                state.Cts.Dispose();
                state.Cts = null;
            }
            else
            {
                // First animation in a rapid sequence: capture the true baseline icon and foreground
                state.OriginalGlyph = fontIcon.Glyph == IconCheckmark ? IconCopy : fontIcon.Glyph;
                state.OriginalForeground = fontIcon.Foreground;
            }

            Brush? successBrush = Application.Current.Resources.TryGetValue("SystemFillColorSuccessBrush", out object? brushObj) 
                ? brushObj as Brush 
                : null;

            var cts = new CancellationTokenSource();
            state.Cts = cts;

            fontIcon.Glyph = IconCheckmark;
            if (successBrush != null)
            {
                fontIcon.Foreground = successBrush;
            }

            try
            {
                await Task.Delay(durationMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Overridden by a newer copy click; let the newer task handle restoration
                return;
            }
            catch
            {
                // Ignore cancellation or UI unload issues
            }

            // Restore baseline icon and foreground if this animation run was not superseded
            if (state.Cts == cts)
            {
                fontIcon.Glyph = state.OriginalGlyph;
                fontIcon.Foreground = state.OriginalForeground;
                state.Cts.Dispose();
                state.Cts = null;
            }
        }
    }
}
