using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
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
    // A wrapper for the classic Win32 OpenFileName dialog
    internal class Win32FilePicker
    {
        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        public string? Show(IntPtr hwnd, string title)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            string allFilesText = lanlanlu_toolkit.Services.LocalizationHelper.GetString("System_AllFiles") ?? "所有檔案";
            ofn.lpstrFilter = $"{allFilesText} (*.*)\0*.*\0\0";
            
            ofn.lpstrFile = new string(new char[1024]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            
            ofn.lpstrFileTitle = new string(new char[512]);
            ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
            
            ofn.lpstrTitle = title;
            // OFN_PATHMUSTEXIST = 0x00000800, OFN_FILEMUSTEXIST = 0x00001000, OFN_NOCHANGEDIR = 0x00000008
            ofn.Flags = 0x00000800 | 0x00001000 | 0x00000008;

            if (GetOpenFileName(ref ofn))
            {
                return ofn.lpstrFile.TrimEnd('\0').Trim();
            }
            return null;
        }
    }

    public sealed partial class FileHashPage : Page
    {
        private string? _selectedFilePath;
        private string _calculatedHash = string.Empty;
        private CancellationTokenSource? _hashCancellationTokenSource;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        public FileHashPage()
        {
            this.InitializeComponent();
            InitializeLocalization();
        }

        private void InitializeLocalization()
        {
            PageHeaderTitleTextBlock.Text = LocalizationHelper.GetString("FileHashPage_Title/Text") ?? "計算檔案雜湊碼";
            PageHeaderDescTextBlock.Text = LocalizationHelper.GetString("FileHashPage_Desc/Text") ?? "快速計算與驗證檔案的 SHA-256、SHA-1 或 MD5 雜湊值。";
            
            SectionSelectFileTextBlock.Text = LocalizationHelper.GetString("FileHashPage_Section_SelectFile") ?? "選擇檔案";
            DragDropPromptTextBlock.Text = LocalizationHelper.GetString("FileHashPage_DragDropPrompt/Text") ?? "選擇要計算的檔案";
            SelectFileCardDescTextBlock.Text = LocalizationHelper.GetString("FileHashPage_SelectFileCardDesc/Text") ?? "從您的電腦中選取一個檔案，系統會即時為您計算並比對對應的雜湊值。";
            SelectFileBtn.Content = LocalizationHelper.GetString("FileHashPage_SelectFile/Content") ?? "選擇檔案";
            
            SectionCalculateTextBlock.Text = LocalizationHelper.GetString("FileHashPage_Section_Calculate") ?? "計算雜湊值";
            AlgorithmLabel.Text = LocalizationHelper.GetString("FileHashPage_Algorithm/Text") ?? "雜湊演算法";
            AlgorithmDescLabel.Text = LocalizationHelper.GetString("FileHashPage_Algorithm_Desc/Text") ?? "SHA-256（高度安全，建議使用）、SHA-1（舊版相容）、MD5（快速驗證）、SHA-512（極高強度安全）。";
            CalculateBtn.Content = LocalizationHelper.GetString("FileHashPage_CalculateButton/Content") ?? "重新計算";
            ProgressStatusText.Text = LocalizationHelper.GetString("FileHashPage_Calculating") ?? "正在計算雜湊值，請稍候……";
            
            ResultTextBox.Header = LocalizationHelper.GetString("FileHashPage_Result/Header") ?? "雜湊結果";
            ResultTextBox.PlaceholderText = LocalizationHelper.GetString("FileHashPage_Result/PlaceholderText") ?? "選擇檔案並計算後，雜湊結果會在此處顯示……";
            CopyBtnText.Text = LocalizationHelper.GetString("FileHashPage_CopyButton/Text") ?? "複製結果";
            
            SectionVerifyTextBlock.Text = LocalizationHelper.GetString("FileHashPage_Section_Verify") ?? "比對驗證";
            CompareLabel.Text = LocalizationHelper.GetString("FileHashPage_Compare/Text") ?? "對比驗證雜湊值（不區分大小寫）";
            CompareDescLabel.Text = LocalizationHelper.GetString("FileHashPage_Compare_Desc/Text") ?? "在此貼上您預期比對的雜湊值，系統會即時進行無大小寫差異的比對驗證。";
            CompareTextBox.PlaceholderText = LocalizationHelper.GetString("FileHashPage_CompareInput/PlaceholderText") ?? "輸入或貼上預期的雜湊值以進行比對……";
            VerifyBtnText.Text = LocalizationHelper.GetString("FileHashPage_CompareButton/Text") ?? "比對";

            // Context Menu Localization
            if (ResultCopyMenu != null) ResultCopyMenu.Text = LocalizationHelper.GetString("System_Copy") ?? "複製";
            if (CompareCutMenu != null) CompareCutMenu.Text = LocalizationHelper.GetString("System_Cut") ?? "剪下";
            if (CompareCopyMenu != null) CompareCopyMenu.Text = LocalizationHelper.GetString("System_Copy") ?? "複製";
            if (ComparePasteMenu != null) ComparePasteMenu.Text = LocalizationHelper.GetString("System_Paste") ?? "貼上";
        }

        private async void SelectFileBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Using robust Win32 COM File Dialog since WinRT FileOpenPicker is unstable in admin/unpackaged mode
                var dialog = new Win32FilePicker();
                IntPtr hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero && App.MainWindow != null)
                {
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                }

                string? result = dialog.Show(hwnd, LocalizationHelper.GetString("FileHashPage_DialogTitle") ?? "選擇要計算雜湊值的檔案");
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
        

        private async Task ProcessSelectedFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            filePath = filePath.TrimEnd('\0').Trim();
            if (!File.Exists(filePath))
            {
                ShowErrorNotification(LocalizationHelper.GetString("FileHashPage_Error_FileNotFound") ?? "檔案不存在或無法存取！");
                return;
            }

            _selectedFilePath = filePath;
            _calculatedHash = string.Empty;
            ResultTextBox.Text = string.Empty;
            CompareTextBox.Text = string.Empty;
            VerifyResultInfoBar.IsOpen = false;

            // Get File Properties using standard System.IO
            var fileInfo = new FileInfo(filePath);
            
            // Display File Details
            FileNameText.Text = fileInfo.Name;
            FilePathText.Text = fileInfo.FullName;
            FileSizeText.Text = $"{LocalizationHelper.GetString("FileHashPage_FileSize")}{FormatFileSize((ulong)fileInfo.Length)} ({fileInfo.Length:N0} bytes)";
            FileDetailsCard.Visibility = Visibility.Visible;

            // Enable action buttons
            CalculateBtn.IsEnabled = true;
            VerifyBtn.IsEnabled = false;
            CopyBtn.IsEnabled = false;

            // Trigger Hash Calculation Automatically
            await RunHashCalculation();
        }

        private async void CalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_hashCancellationTokenSource != null)
            {
                // Calculation is in progress, request cancellation!
                _hashCancellationTokenSource.Cancel();
                return;
            }

            await RunHashCalculation();
        }

        private async Task RunHashCalculation()
        {
            if (string.IsNullOrEmpty(_selectedFilePath)) return;

            string algorithm = "SHA256";
            if (AlgorithmComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                algorithm = selectedItem.Tag.ToString()!;
            }

            // Create cancellation token
            _hashCancellationTokenSource = new CancellationTokenSource();
            var token = _hashCancellationTokenSource.Token;

            // UI feedback
            ProgressGrid.Visibility = Visibility.Visible;
            CalculateBtn.Content = LocalizationHelper.GetString("System_Cancel") ?? "取消";
            CalculateBtn.IsEnabled = true; // Keep enabled for cancellation!
            SelectFileBtn.IsEnabled = false;
            AlgorithmComboBox.IsEnabled = false;
            ResultTextBox.Text = LocalizationHelper.GetString("FileHashPage_Calculating") ?? "正在計算雜湊值，請稍候……";

            try
            {
                _calculatedHash = await CalculateHashAsync(_selectedFilePath, algorithm, token);

                if (string.IsNullOrEmpty(_calculatedHash))
                {
                    // Hashing was cancelled!
                    ResultTextBox.Text = LocalizationHelper.GetString("FileHashPage_CalculationCancelled") ?? "計算已取消。";
                    CopyBtn.IsEnabled = false;
                    VerifyBtn.IsEnabled = false;
                    VerifyResultInfoBar.IsOpen = false;
                    return;
                }
                
                ResultTextBox.Text = _calculatedHash;
                CopyBtn.IsEnabled = true;
                VerifyBtn.IsEnabled = true;

                // Perform verification if user has already entered verification text
                PerformHashVerification();
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = $"{LocalizationHelper.GetString("Notification_Error") ?? "Error"}: {ex.Message}";
                ShowErrorNotification(ex.Message);
            }
            finally
            {
                ProgressGrid.Visibility = Visibility.Collapsed;
                CalculateBtn.Content = LocalizationHelper.GetString("FileHashPage_CalculateButton/Content") ?? "重新計算";
                SelectFileBtn.IsEnabled = true;
                AlgorithmComboBox.IsEnabled = true;
                _hashCancellationTokenSource?.Dispose();
                _hashCancellationTokenSource = null;
            }
        }

        private async Task<string> CalculateHashAsync(string filePath, string algorithm, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (HashAlgorithm hasher = algorithm.ToUpper() switch
                    {
                        "SHA256" => SHA256.Create(),
                        "SHA1" => SHA1.Create(),
                        "MD5" => MD5.Create(),
                        "SHA512" => SHA512.Create(),
                        _ => throw new ArgumentException("Unsupported algorithm")
                    })
                    {
                        byte[] buffer = new byte[81920]; // 80KB buffer
                        int bytesRead;

                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return string.Empty; // Return empty directly on cancellation (exception-less)
                            }

                            hasher.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                        }

                        // Finalize the hashing process
                        hasher.TransformFinalBlock(buffer, 0, 0);

                        byte[] hashBytes = hasher.Hash ?? throw new InvalidOperationException("Hash computation failed");
                        var sb = new StringBuilder(hashBytes.Length * 2);
                        foreach (byte b in hashBytes)
                        {
                            sb.Append(b.ToString("x2"));
                        }
                        return sb.ToString();
                    }
                }
            }, cancellationToken);
        }

        private async void AlgorithmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedFilePath))
            {
                await RunHashCalculation();
            }
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_calculatedHash)) return;

            var package = new DataPackage();
            package.SetText(_calculatedHash);
            Clipboard.SetContent(package);

            NotificationService.Show(LocalizationHelper.GetString("Notification_Success") ?? "Success", LocalizationHelper.GetString("SystemRepairPage_Copied") ?? "已複製到剪貼簿", InfoBarSeverity.Success);
        }

        private void CompareTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PerformHashVerification();
        }

        private async void VerifyBtn_Click(object sender, RoutedEventArgs e)
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

        private void PerformHashVerification()
        {
            string expectedHash = CompareTextBox.Text.Trim();
            if (string.IsNullOrEmpty(expectedHash) || string.IsNullOrEmpty(_calculatedHash))
            {
                VerifyResultInfoBar.IsOpen = false;
                return;
            }

            if (string.Equals(_calculatedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                VerifyResultInfoBar.Severity = InfoBarSeverity.Success;
                VerifyResultInfoBar.Title = LocalizationHelper.GetString("FileHashPage_VerifySuccess_Title") ?? "驗證成功";
                VerifyResultInfoBar.Message = LocalizationHelper.GetString("FileHashPage_VerifySuccess");
                VerifyResultInfoBar.IsOpen = true;
            }
            else
            {
                VerifyResultInfoBar.Severity = InfoBarSeverity.Error;
                VerifyResultInfoBar.Title = LocalizationHelper.GetString("FileHashPage_VerifyFail_Title") ?? "驗證失敗";
                VerifyResultInfoBar.Message = LocalizationHelper.GetString("FileHashPage_VerifyFail");
                VerifyResultInfoBar.IsOpen = true;
            }
        }

        private string FormatFileSize(ulong sizeInBytes)
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

        private void ShowErrorNotification(string message)
        {
            NotificationService.Show(LocalizationHelper.GetString("Notification_Error") ?? "Error", message, InfoBarSeverity.Error);
        }

        private void ResultCopyMenu_Click(object sender, RoutedEventArgs e)
        {
            CopyTextBoxContent(ResultTextBox);
        }

        private void CompareCutMenu_Click(object sender, RoutedEventArgs e)
        {
            CutTextBoxContent(CompareTextBox);
        }

        private void CompareCopyMenu_Click(object sender, RoutedEventArgs e)
        {
            CopyTextBoxContent(CompareTextBox);
        }

        private async void ComparePasteMenu_Click(object sender, RoutedEventArgs e)
        {
            await PasteTextBoxContentAsync(CompareTextBox);
        }

        private void CopyTextBoxContent(TextBox textBox)
        {
            string textToCopy = textBox.SelectedText;
            if (string.IsNullOrEmpty(textToCopy))
            {
                textToCopy = textBox.Text;
            }
            if (string.IsNullOrEmpty(textToCopy)) return;

            var package = new DataPackage();
            package.SetText(textToCopy);
            Clipboard.SetContent(package);
        }

        private void CutTextBoxContent(TextBox textBox)
        {
            string textToCut = textBox.SelectedText;
            if (string.IsNullOrEmpty(textToCut)) return;

            var package = new DataPackage();
            package.SetText(textToCut);
            Clipboard.SetContent(package);

            int selectionStart = textBox.SelectionStart;
            textBox.Text = textBox.Text.Remove(selectionStart, textBox.SelectionLength);
            textBox.SelectionStart = selectionStart;
        }

        private async Task PasteTextBoxContentAsync(TextBox textBox)
        {
            var packageView = Clipboard.GetContent();
            if (packageView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    string textToPaste = await packageView.GetTextAsync();
                    int selectionStart = textBox.SelectionStart;
                    int selectionLength = textBox.SelectionLength;

                    string currentText = textBox.Text ?? string.Empty;
                    if (selectionLength > 0)
                    {
                        currentText = currentText.Remove(selectionStart, selectionLength);
                    }
                    textBox.Text = currentText.Insert(selectionStart, textToPaste);
                    textBox.SelectionStart = selectionStart + textToPaste.Length;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Paste Error: {ex.Message}");
                }
            }
        }
    }
}
