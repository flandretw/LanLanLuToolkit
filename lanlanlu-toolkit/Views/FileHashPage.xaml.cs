using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class FileHashPage : Page
    {
        private string? _selectedFilePath;
        private readonly Dictionary<string, string> _calculatedHashes = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _hashCts;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        public FileHashPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            ToolTipService.SetToolTip(ClearFileBtn, LocalizationHelper.GetString("FileHashPage_ClearFile"));
            ToolTipService.SetToolTip(ClearCompareBtn, LocalizationHelper.GetString("FileHashPage_ClearInput"));
            ResetHashTextBoxPlaceholders();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CancelOngoingHashCalculation();
        }

        private void ResetHashTextBoxPlaceholders()
        {
            string pendingText = LocalizationHelper.GetString("FileHashPage_Algorithm_Pending") ?? "Pending calculation...";
            Sha256TextBox.Text = pendingText;
            Sha1TextBox.Text = pendingText;
            Md5TextBox.Text = pendingText;
            Sha512TextBox.Text = pendingText;

            Sha256MatchBadge.Visibility = Visibility.Collapsed;
            Sha1MatchBadge.Visibility = Visibility.Collapsed;
            Md5MatchBadge.Visibility = Visibility.Collapsed;
            Sha512MatchBadge.Visibility = Visibility.Collapsed;
        }

        private void FileCard_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = LocalizationHelper.GetString("FileHashPage_DragDrop_Caption") ?? "Drop to calculate hash";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }

        private async void FileCard_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
                    {
                        await ProcessSelectedFilePath(file.Path);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"File drop error: {ex.Message}");
                    ShowErrorNotification(ex.Message);
                }
            }
        }

        private async void SelectFileBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero && App.MainWindow != null)
                {
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                }

                string? result = Win32FilePicker.ShowOpenDialog(hwnd, LocalizationHelper.GetString("FileHashPage_DialogTitle"));
                if (!string.IsNullOrEmpty(result))
                {
                    await ProcessSelectedFilePath(result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win32 Filepicker Error: {ex.Message}");
                ShowErrorNotification(ex.Message);
            }
        }

        private void ClearFileBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelOngoingHashCalculation();

            _selectedFilePath = null;
            _calculatedHashes.Clear();

            FileDetailsCard.Visibility = Visibility.Collapsed;
            FileDropZoneCard.Visibility = Visibility.Visible;

            ResetHashTextBoxPlaceholders();
            RecalculateBtn.IsEnabled = false;
            CopyAllBtn.IsEnabled = false;
            CopySha256Btn.IsEnabled = false;
            CopySha1Btn.IsEnabled = false;
            CopyMd5Btn.IsEnabled = false;
            CopySha512Btn.IsEnabled = false;

            CompareTextBox.Text = string.Empty;
            VerifyStatusCard.Visibility = Visibility.Collapsed;
        }

        private async Task ProcessSelectedFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            filePath = filePath.TrimEnd('\0').Trim();
            if (!File.Exists(filePath))
            {
                ShowErrorNotification(LocalizationHelper.GetString("FileHashPage_Error_FileNotFound"));
                return;
            }

            _selectedFilePath = filePath;
            _calculatedHashes.Clear();

            // Populate File Metadata Card
            var fileInfo = new FileInfo(filePath);
            FileNameText.Text = fileInfo.Name;
            FilePathText.Text = fileInfo.FullName;
            FileSizeText.Text = $"{LocalizationHelper.GetString("FileHashPage_FileSize")} {FormatFileSize((ulong)fileInfo.Length)} ({fileInfo.Length:N0} bytes)";

            FileDropZoneCard.Visibility = Visibility.Collapsed;
            FileDetailsCard.Visibility = Visibility.Visible;

            ResetHashTextBoxPlaceholders();
            VerifyStatusCard.Visibility = Visibility.Collapsed;

            // Trigger parallel multi-algorithm hash calculation
            await RunHashCalculation();
        }

        private async void RecalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_hashCts != null)
            {
                CancelOngoingHashCalculation();
                return;
            }

            await RunHashCalculation();
        }

        private void CancelOngoingHashCalculation()
        {
            if (_hashCts != null)
            {
                _hashCts.Cancel();
                _hashCts.Dispose();
                _hashCts = null;
            }
        }

        private async Task RunHashCalculation()
        {
            if (string.IsNullOrEmpty(_selectedFilePath)) return;

            CancelOngoingHashCalculation();
            _hashCts = new CancellationTokenSource();
            var token = _hashCts.Token;

            // UI State: Starting calculation
            HashProgressRing.IsActive = true;
            HashProgressRing.Visibility = Visibility.Visible;
            HashProgressBar.Visibility = Visibility.Visible;
            HashProgressBar.Value = 0;
            HashProgressPercentText.Visibility = Visibility.Visible;
            HashProgressPercentText.Text = "0%";

            RecalculateBtn.IsEnabled = true;
            RecalculateIcon.Glyph = "\uE711"; // Cancel icon
            RecalculateBtnText.Text = LocalizationHelper.GetString("System_Cancel") ?? "Cancel";

            CopyAllBtn.IsEnabled = false;
            CopySha256Btn.IsEnabled = false;
            CopySha1Btn.IsEnabled = false;
            CopyMd5Btn.IsEnabled = false;
            CopySha512Btn.IsEnabled = false;

            string calcText = LocalizationHelper.GetString("FileHashPage_Calculating") ?? "Calculating...";
            Sha256TextBox.Text = calcText;
            Sha1TextBox.Text = calcText;
            Md5TextBox.Text = calcText;
            Sha512TextBox.Text = calcText;

            var progress = new Progress<int>(percent =>
            {
                HashProgressBar.Value = percent;
                HashProgressPercentText.Text = $"{percent}%";
            });

            try
            {
                var results = await CalculateHashesAsync(_selectedFilePath, progress, token);

                if (results == null)
                {
                    // Calculation was cancelled
                    string cancelledText = LocalizationHelper.GetString("FileHashPage_CalculationCancelled") ?? "Calculation cancelled.";
                    Sha256TextBox.Text = cancelledText;
                    Sha1TextBox.Text = cancelledText;
                    Md5TextBox.Text = cancelledText;
                    Sha512TextBox.Text = cancelledText;
                    return;
                }

                _calculatedHashes.Clear();
                foreach (var kvp in results)
                {
                    _calculatedHashes[kvp.Key] = kvp.Value;
                }

                Sha256TextBox.Text = _calculatedHashes.GetValueOrDefault("SHA256", string.Empty);
                Sha1TextBox.Text = _calculatedHashes.GetValueOrDefault("SHA1", string.Empty);
                Md5TextBox.Text = _calculatedHashes.GetValueOrDefault("MD5", string.Empty);
                Sha512TextBox.Text = _calculatedHashes.GetValueOrDefault("SHA512", string.Empty);

                CopyAllBtn.IsEnabled = true;
                CopySha256Btn.IsEnabled = true;
                CopySha1Btn.IsEnabled = true;
                CopyMd5Btn.IsEnabled = true;
                CopySha512Btn.IsEnabled = true;

                PerformHashVerification();
            }
            catch (Exception ex)
            {
                ShowErrorNotification(ex.Message);
                ResetHashTextBoxPlaceholders();
            }
            finally
            {
                HashProgressRing.IsActive = false;
                HashProgressRing.Visibility = Visibility.Collapsed;
                HashProgressBar.Visibility = Visibility.Collapsed;
                HashProgressPercentText.Visibility = Visibility.Collapsed;

                RecalculateIcon.Glyph = "\uE72C"; // Recalculate icon
                RecalculateBtnText.Text = LocalizationHelper.GetString("FileHashPage_CalculateButton/Text") ?? "Recalculate";
                RecalculateBtn.IsEnabled = true;

                _hashCts?.Dispose();
                _hashCts = null;
            }
        }

        private static async Task<Dictionary<string, string>?> CalculateHashesAsync(
            string filePath,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var fileInfo = new FileInfo(filePath);
                long totalBytes = fileInfo.Length;
                long totalRead = 0;

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144, FileOptions.SequentialScan);
                using var sha256 = SHA256.Create();
                using var sha1 = SHA1.Create();
                using var md5 = MD5.Create();
                using var sha512 = SHA512.Create();

                var hashers = new HashAlgorithm[] { sha256, sha1, md5, sha512 };
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(262144);

                try
                {
                    int bytesRead;
                    int lastReportedPercent = -1;

                    while ((bytesRead = stream.Read(buffer, 0, 262144)) > 0)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }

                        for (int i = 0; i < hashers.Length; i++)
                        {
                            hashers[i].TransformBlock(buffer, 0, bytesRead, buffer, 0);
                        }

                        totalRead += bytesRead;
                        if (totalBytes > 0 && progress != null)
                        {
                            int currentPercent = (int)((double)totalRead * 100 / totalBytes);
                            if (currentPercent != lastReportedPercent)
                            {
                                lastReportedPercent = currentPercent;
                                progress.Report(currentPercent);
                            }
                        }
                    }

                    for (int i = 0; i < hashers.Length; i++)
                    {
                        hashers[i].TransformFinalBlock(buffer, 0, 0);
                    }

                    static string ToHexString(byte[]? bytes)
                    {
                        if (bytes == null) return string.Empty;
                        var sb = new StringBuilder(bytes.Length * 2);
                        foreach (byte b in bytes)
                        {
                            sb.Append(b.ToString("x2"));
                        }
                        return sb.ToString();
                    }

                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SHA256"] = ToHexString(sha256.Hash),
                        ["SHA1"] = ToHexString(sha1.Hash),
                        ["MD5"] = ToHexString(md5.Hash),
                        ["SHA512"] = ToHexString(sha512.Hash)
                    };
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }, cancellationToken);
        }

        private void CopySha256Btn_Click(object sender, RoutedEventArgs e)
        {
            CopySingleHash(_calculatedHashes.GetValueOrDefault("SHA256", string.Empty), CopySha256Icon);
        }

        private void CopySha1Btn_Click(object sender, RoutedEventArgs e)
        {
            CopySingleHash(_calculatedHashes.GetValueOrDefault("SHA1", string.Empty), CopySha1Icon);
        }

        private void CopyMd5Btn_Click(object sender, RoutedEventArgs e)
        {
            CopySingleHash(_calculatedHashes.GetValueOrDefault("MD5", string.Empty), CopyMd5Icon);
        }

        private void CopySha512Btn_Click(object sender, RoutedEventArgs e)
        {
            CopySingleHash(_calculatedHashes.GetValueOrDefault("SHA512", string.Empty), CopySha512Icon);
        }

        private async void CopySingleHash(string hash, FontIcon icon)
        {
            if (string.IsNullOrEmpty(hash)) return;

            var package = new DataPackage();
            package.SetText(hash);
            Clipboard.SetContent(package);

            icon.Glyph = "\uE73E"; // Checkmark icon
            icon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            await Task.Delay(1200);
            icon.Glyph = "\uE8C8"; // Copy icon
            icon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private void CopyAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_calculatedHashes.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"File: {FileNameText.Text}");
            sb.AppendLine($"Path: {FilePathText.Text}");
            sb.AppendLine($"Size: {FileSizeText.Text}");
            sb.AppendLine();
            sb.AppendLine($"SHA-256: {_calculatedHashes.GetValueOrDefault("SHA256", string.Empty)}");
            sb.AppendLine($"SHA-1:   {_calculatedHashes.GetValueOrDefault("SHA1", string.Empty)}");
            sb.AppendLine($"MD5:     {_calculatedHashes.GetValueOrDefault("MD5", string.Empty)}");
            sb.AppendLine($"SHA-512: {_calculatedHashes.GetValueOrDefault("SHA512", string.Empty)}");

            var package = new DataPackage();
            package.SetText(sb.ToString());
            Clipboard.SetContent(package);

            string successMsg = LocalizationHelper.GetString("FileHashPage_CopyAll_Success") ?? "All hashes copied to clipboard.";
            NotificationService.Show(LocalizationHelper.GetString("Notification_Success") ?? "Success", successMsg, InfoBarSeverity.Success);
        }

        private void CompareTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PerformHashVerification();
        }

        private async void PasteCompareBtn_Click(object sender, RoutedEventArgs e)
        {
            var packageView = Clipboard.GetContent();
            if (packageView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    string textToPaste = await packageView.GetTextAsync();
                    CompareTextBox.Text = textToPaste.Trim();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Verify Auto-Paste Error: {ex.Message}");
                }
            }
            PerformHashVerification();
        }

        private void ClearCompareBtn_Click(object sender, RoutedEventArgs e)
        {
            CompareTextBox.Text = string.Empty;
        }

        private void PerformHashVerification()
        {
            string expectedHash = CompareTextBox.Text.Trim();

            // Reset all match badges first
            Sha256MatchBadge.Visibility = Visibility.Collapsed;
            Sha1MatchBadge.Visibility = Visibility.Collapsed;
            Md5MatchBadge.Visibility = Visibility.Collapsed;
            Sha512MatchBadge.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(expectedHash))
            {
                VerifyStatusCard.Visibility = Visibility.Collapsed;
                return;
            }

            if (_calculatedHashes.Count == 0)
            {
                VerifyStatusCard.Visibility = Visibility.Collapsed;
                return;
            }

            string? matchedAlgorithm = null;
            if (string.Equals(_calculatedHashes.GetValueOrDefault("SHA256"), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                matchedAlgorithm = "SHA-256";
                Sha256MatchBadge.Visibility = Visibility.Visible;
            }
            else if (string.Equals(_calculatedHashes.GetValueOrDefault("SHA1"), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                matchedAlgorithm = "SHA-1";
                Sha1MatchBadge.Visibility = Visibility.Visible;
            }
            else if (string.Equals(_calculatedHashes.GetValueOrDefault("MD5"), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                matchedAlgorithm = "MD5";
                Md5MatchBadge.Visibility = Visibility.Visible;
            }
            else if (string.Equals(_calculatedHashes.GetValueOrDefault("SHA512"), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                matchedAlgorithm = "SHA-512";
                Sha512MatchBadge.Visibility = Visibility.Visible;
            }

            VerifyStatusCard.Visibility = Visibility.Visible;

            if (matchedAlgorithm != null)
            {
                VerifyStatusCard.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
                VerifyStatusCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                VerifyStatusIcon.Glyph = "\uE73E"; // Checkmark
                VerifyStatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                string format = LocalizationHelper.GetString("FileHashPage_MatchSuccess_Format") ?? "Verification successful: Exact match with {0}.";
                VerifyStatusMessage.Text = string.Format(format, matchedAlgorithm);
                VerifyStatusMessage.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else
            {
                VerifyStatusCard.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"];
                VerifyStatusCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                VerifyStatusIcon.Glyph = "\uE711"; // Cancel/Error cross
                VerifyStatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

                VerifyStatusMessage.Text = LocalizationHelper.GetString("FileHashPage_MatchFail") ?? "No match: Does not match any calculated algorithm hash.";
                VerifyStatusMessage.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
        }

        private static string FormatFileSize(ulong sizeInBytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = sizeInBytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static void ShowErrorNotification(string message)
        {
            NotificationService.Show(LocalizationHelper.GetString("Notification_Error") ?? "Error", message, InfoBarSeverity.Error);
        }
    }
}
