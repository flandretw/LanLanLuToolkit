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
            ToolTipService.SetToolTip(CopySha256Btn, LocalizationHelper.GetString("FileHashPage_CopySha256_Tooltip") ?? "Copy SHA-256");
            ToolTipService.SetToolTip(CopySha1Btn, LocalizationHelper.GetString("FileHashPage_CopySha1_Tooltip") ?? "Copy SHA-1");
            ToolTipService.SetToolTip(CopyMd5Btn, LocalizationHelper.GetString("FileHashPage_CopyMd5_Tooltip") ?? "Copy MD5");
            ToolTipService.SetToolTip(CopySha512Btn, LocalizationHelper.GetString("FileHashPage_CopySha512_Tooltip") ?? "Copy SHA-512");
            ResetHashTextBoxPlaceholders();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CancelOngoingHashCalculation();
        }

        private void ResetHashTextBoxPlaceholders()
        {
            Sha256TextBox.Text = string.Empty;
            Sha1TextBox.Text = string.Empty;
            Md5TextBox.Text = string.Empty;
            Sha512TextBox.Text = string.Empty;

            // Show description text, hide hash textboxes
            Sha256DescText.Visibility = Visibility.Visible;
            Sha1DescText.Visibility = Visibility.Visible;
            Md5DescText.Visibility = Visibility.Visible;
            Sha512DescText.Visibility = Visibility.Visible;

            Sha256TextBox.Visibility = Visibility.Collapsed;
            Sha1TextBox.Visibility = Visibility.Collapsed;
            Md5TextBox.Visibility = Visibility.Collapsed;
            Sha512TextBox.Visibility = Visibility.Collapsed;

            Sha256MatchBadge.Visibility = Visibility.Collapsed;
            Sha1MatchBadge.Visibility = Visibility.Collapsed;
            Md5MatchBadge.Visibility = Visibility.Collapsed;
            Sha512MatchBadge.Visibility = Visibility.Collapsed;

            CopySha256Btn.Visibility = Visibility.Collapsed;
            CopySha1Btn.Visibility = Visibility.Collapsed;
            CopyMd5Btn.Visibility = Visibility.Collapsed;
            CopySha512Btn.Visibility = Visibility.Collapsed;

            CopySha256Btn.IsEnabled = false;
            CopySha1Btn.IsEnabled = false;
            CopyMd5Btn.IsEnabled = false;
            CopySha512Btn.IsEnabled = false;

            RecalculateBtn.IsEnabled = false;
            CopyAllBtn.IsEnabled = false;
            CancelHashBtn.Visibility = Visibility.Collapsed;
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
            PerformHashVerification();
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

            // Populate File Metadata Card (Predictable Compact 2-Line Layout)
            var fileInfo = new FileInfo(filePath);
            FileNameText.Text = fileInfo.Name;
            FilePathText.Text = fileInfo.FullName;
            FileSizeText.Text = $"({FormatFileSize((ulong)fileInfo.Length)})";
            ToolTipService.SetToolTip(FileNameText, fileInfo.Name);
            ToolTipService.SetToolTip(FilePathText, fileInfo.FullName);

            FileDropZoneCard.Visibility = Visibility.Collapsed;
            FileDetailsCard.Visibility = Visibility.Visible;

            ResetHashTextBoxPlaceholders();
            PerformHashVerification();

            // Trigger parallel multi-algorithm hash calculation
            await RunHashCalculation();
        }

        private async void RecalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunHashCalculation();
        }

        private void CancelHashBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelOngoingHashCalculation();
        }

        private void CancelOngoingHashCalculation()
        {
            try
            {
                if (_hashCts != null && !_hashCts.IsCancellationRequested)
                {
                    _hashCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { }
        }

        private async Task RunHashCalculation()
        {
            if (string.IsNullOrEmpty(_selectedFilePath)) return;

            CancelOngoingHashCalculation();
            var currentCts = new CancellationTokenSource();
            _hashCts = currentCts;
            var token = currentCts.Token;

            // UI State: Starting calculation
            HashProgressRing.IsActive = true;
            HashProgressRing.Visibility = Visibility.Visible;
            HashProgressBar.Visibility = Visibility.Visible;
            HashProgressBar.Value = 0;
            HashProgressPercentText.Visibility = Visibility.Visible;
            HashProgressPercentText.Text = "0%";

            CancelHashBtn.Visibility = Visibility.Visible;
            RecalculateBtn.IsEnabled = false;

            CopyAllBtn.IsEnabled = false;
            CopySha256Btn.IsEnabled = false;
            CopySha1Btn.IsEnabled = false;
            CopyMd5Btn.IsEnabled = false;
            CopySha512Btn.IsEnabled = false;

            // Switch from description to calculating indicator
            Sha256DescText.Visibility = Visibility.Collapsed;
            Sha1DescText.Visibility = Visibility.Collapsed;
            Md5DescText.Visibility = Visibility.Collapsed;
            Sha512DescText.Visibility = Visibility.Collapsed;

            string calcText = LocalizationHelper.GetString("FileHashPage_Status_Calculating") ?? "計算中...";
            Sha256TextBox.Text = calcText;
            Sha1TextBox.Text = calcText;
            Md5TextBox.Text = calcText;
            Sha512TextBox.Text = calcText;

            Sha256TextBox.Visibility = Visibility.Visible;
            Sha1TextBox.Visibility = Visibility.Visible;
            Md5TextBox.Visibility = Visibility.Visible;
            Sha512TextBox.Visibility = Visibility.Visible;

            var progress = new Progress<int>(percent =>
            {
                if (ReferenceEquals(_hashCts, currentCts))
                {
                    HashProgressBar.Value = percent;
                    HashProgressPercentText.Text = $"{percent}%";
                }
            });

            try
            {
                var results = await CalculateHashesAsync(_selectedFilePath, progress, token);

                // If a newer calculation was started, discard results of this obsolete run
                if (!ReferenceEquals(_hashCts, currentCts))
                {
                    return;
                }

                if (results == null || token.IsCancellationRequested)
                {
                    // Calculation was cancelled
                    string cancelledText = LocalizationHelper.GetString("FileHashPage_CalculationCancelled") ?? "Calculation cancelled.";
                    Sha256TextBox.Text = cancelledText;
                    Sha1TextBox.Text = cancelledText;
                    Md5TextBox.Text = cancelledText;
                    Sha512TextBox.Text = cancelledText;

                    Sha256TextBox.Visibility = Visibility.Visible;
                    Sha1TextBox.Visibility = Visibility.Visible;
                    Md5TextBox.Visibility = Visibility.Visible;
                    Sha512TextBox.Visibility = Visibility.Visible;
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

                Sha256TextBox.Visibility = Visibility.Visible;
                Sha1TextBox.Visibility = Visibility.Visible;
                Md5TextBox.Visibility = Visibility.Visible;
                Sha512TextBox.Visibility = Visibility.Visible;

                CopyAllBtn.IsEnabled = true;
                CopySha256Btn.IsEnabled = true;
                CopySha1Btn.IsEnabled = true;
                CopyMd5Btn.IsEnabled = true;
                CopySha512Btn.IsEnabled = true;

                CopySha256Btn.Visibility = Visibility.Visible;
                CopySha1Btn.Visibility = Visibility.Visible;
                CopyMd5Btn.Visibility = Visibility.Visible;
                CopySha512Btn.Visibility = Visibility.Visible;

                PerformHashVerification();
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(_hashCts, currentCts))
                {
                    string cancelledText = LocalizationHelper.GetString("FileHashPage_CalculationCancelled") ?? "Calculation cancelled.";
                    Sha256TextBox.Text = cancelledText;
                    Sha1TextBox.Text = cancelledText;
                    Md5TextBox.Text = cancelledText;
                    Sha512TextBox.Text = cancelledText;
                }
            }
            catch (IOException ex)
            {
                if (ReferenceEquals(_hashCts, currentCts))
                {
                    string msgKey = ex.Message.Contains("modified", StringComparison.OrdinalIgnoreCase)
                        ? "FileHashPage_Error_FileModified"
                        : "FileHashPage_Error_FileInUse";
                    string msg = LocalizationHelper.GetString(msgKey) ?? ex.Message;
                    ShowErrorNotification(msg);
                    ResetHashTextBoxPlaceholders();
                }
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_hashCts, currentCts))
                {
                    ShowErrorNotification(ex.Message);
                    ResetHashTextBoxPlaceholders();
                }
            }
            finally
            {
                // Only dispose this run's own CancellationTokenSource
                try
                {
                    currentCts.Dispose();
                }
                catch { }

                // Only clean up state and UI if this run is still the active one
                if (ReferenceEquals(_hashCts, currentCts))
                {
                    _hashCts = null;

                    CancelHashBtn.Visibility = Visibility.Collapsed;

                    HashProgressRing.IsActive = false;
                    HashProgressRing.Visibility = Visibility.Collapsed;
                    HashProgressBar.Visibility = Visibility.Collapsed;
                    HashProgressPercentText.Visibility = Visibility.Collapsed;

                    RecalculateIcon.Glyph = "\uE72C"; // Recalculate icon
                    RecalculateBtnText.Text = LocalizationHelper.GetString("FileHashPage_CalculateButton.Text") ?? "重新計算";
                    RecalculateBtn.IsEnabled = !string.IsNullOrEmpty(_selectedFilePath);
                }
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
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException(LocalizationHelper.GetString("FileHashPage_Error_FileNotFound") ?? "File not found.", filePath);
                }

                long initialLength = fileInfo.Length;
                DateTime initialWriteTime = fileInfo.LastWriteTimeUtc;

                // Open with FileShare.Read to prevent concurrent write modifications during calculation
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, FileOptions.SequentialScan);
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
                    long totalRead = 0;

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
                        if (initialLength > 0 && progress != null)
                        {
                            int currentPercent = (int)((double)totalRead * 100 / initialLength);
                            if (currentPercent != lastReportedPercent)
                            {
                                lastReportedPercent = currentPercent;
                                progress.Report(currentPercent);
                            }
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    // Verify the file was not modified while computing
                    fileInfo.Refresh();
                    if (fileInfo.Length != initialLength || fileInfo.LastWriteTimeUtc != initialWriteTime)
                    {
                        throw new IOException(LocalizationHelper.GetString("FileHashPage_Error_FileModified") ?? "The file was modified during hash calculation.");
                    }

                    for (int i = 0; i < hashers.Length; i++)
                    {
                        hashers[i].TransformFinalBlock(buffer, 0, 0);
                    }

                    static string ToHexString(byte[]? bytes) => bytes != null ? Convert.ToHexString(bytes).ToLowerInvariant() : string.Empty;

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

        private async void CopySha256Btn_Click(object sender, RoutedEventArgs e)
        {
            await CopySingleHashAsync(_calculatedHashes.GetValueOrDefault("SHA256", string.Empty), CopySha256Icon);
        }

        private async void CopySha1Btn_Click(object sender, RoutedEventArgs e)
        {
            await CopySingleHashAsync(_calculatedHashes.GetValueOrDefault("SHA1", string.Empty), CopySha1Icon);
        }

        private async void CopyMd5Btn_Click(object sender, RoutedEventArgs e)
        {
            await CopySingleHashAsync(_calculatedHashes.GetValueOrDefault("MD5", string.Empty), CopyMd5Icon);
        }

        private async void CopySha512Btn_Click(object sender, RoutedEventArgs e)
        {
            await CopySingleHashAsync(_calculatedHashes.GetValueOrDefault("SHA512", string.Empty), CopySha512Icon);
        }

        private async Task CopySingleHashAsync(string hash, FontIcon icon)
        {
            if (string.IsNullOrEmpty(hash)) return;
            await ClipboardService.CopyTextAsync(hash, icon, showNotification: false);
        }

        private async void CopyAllBtn_Click(object sender, RoutedEventArgs e)
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

            string successMsg = LocalizationHelper.GetString("FileHashPage_CopyAll_Success") ?? "All hashes copied to clipboard.";
            await ClipboardService.CopyTextAsync(sb.ToString(), CopyAllBtn, showNotification: true, successMsg);
        }

        private void CompareTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PerformHashVerification();
        }

        private async void PasteCompareBtn_Click(object sender, RoutedEventArgs e)
        {
            string? textToPaste = await ClipboardService.GetTextAsync();
            if (!string.IsNullOrEmpty(textToPaste))
            {
                CompareTextBox.Text = textToPaste.Trim();
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

            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                // Hash entered, but no file selected yet
                VerifyStatusCard.Visibility = Visibility.Visible;
                VerifyStatusCard.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                VerifyStatusCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
                VerifyStatusIcon.Glyph = "\uE897"; // Info question mark
                VerifyStatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

                VerifyStatusMessage.Text = LocalizationHelper.GetString("FileHashPage_MatchPending") ?? "請輸入或貼上雜湊值以進行即時比對。";
                VerifyStatusMessage.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                return;
            }

            if (_calculatedHashes.Count == 0)
            {
                // File selected, but calculation is still pending or ongoing
                VerifyStatusCard.Visibility = Visibility.Visible;
                VerifyStatusCard.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                VerifyStatusCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
                VerifyStatusIcon.Glyph = "\uE895"; // Sync
                VerifyStatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

                VerifyStatusMessage.Text = LocalizationHelper.GetString("FileHashPage_Calculating") ?? "正在計算雜湊值，請稍候……";
                VerifyStatusMessage.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
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
