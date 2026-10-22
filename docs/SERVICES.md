# LanLanLu Toolkit - 服務層架構規格書與目錄清冊

本文件為 LanLanLu Toolkit 服務層（`lanlanlu-toolkit/Services`）的中心技術清冊，提供人類開發者與 AI 代理作為架構維護、功能擴充與問題疑難排解的單一事實來源。

---

## 1. 架構原則與設計哲學

1. **免安裝純綠色軟體架構**  
   應用程式必須能在未封裝環境（Unpackaged）下獨立執行。不隨意向 Windows 登錄檔寫入非必要資料，軟體設定檔優先存放於執行檔同層的 `Data/settings.json`；若所在目錄具唯讀屬性（例如唯讀隨身碟或受保護的系統資料夾），則安全降級至本機應用程式資料區。
2. **零外部肥大套件依賴**  
   為了維持便攜發布體積與啟動速度，服務層嚴禁隨意引入大型 NuGet 套件。對於系統規格查詢、當機診斷、視窗控制或底層功能，優先使用 .NET 8 內建型別、Windows App SDK 原生 API 或 Win32 P/Invoke（例如 `comdlg32.dll`、`user32.dll`）。
3. **優雅降級機制**  
   對於硬體驅動程式或系統特定元件（如特定廠牌顯示卡、Windows 事件檢視器記錄、記憶體傾印檔案），若環境不支援或無存取權限，所有服務必須能安全捕捉例外並提供合理預設值，嚴禁因硬體缺失導致主程式當機。
4. **資源釋放與執行緒安全**  
   服務層若使用計時器、非同步工作或底層控制代碼，必須確保生命週期與資源管理明確；涉及 UI 呈現時，必須透過 `DispatcherQueue` 進行執行緒封送。

---

## 2. 服務目錄速查表

| 服務名稱 | 檔案路徑 | 主要職責 | 底層相依與機制 | 主要呼叫端 |
| :--- | :--- | :--- | :--- | :--- |
| `HardwareInfoService` | [`HardwareInfoService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/HardwareInfoService.cs) | 快取與非同步掃描處理器、記憶體、主機板、磁碟與作業系統規格 | WMI 查詢（`System.Management`）、登錄檔讀取 | `HomePage.xaml.cs`、`SettingsPage.xaml.cs` |
| `CrashReportService` | [`CrashReportService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/CrashReportService.cs) | 解析系統 Minidump 記憶體傾印檔案與事件檢視器重大當機記錄 | 檔案二進位讀取、Windows 事件記錄讀取器 | `CrashReportPage.xaml.cs` |
| `InputTesterService` | [`InputTesterService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/InputTesterService.cs) | 定義外接輸入診斷資料模型與動態生成三種尺寸鍵盤配置佈局 | 資料模型與佈局座標計算 | `InputTesterPage.xaml.cs` |
| `SettingsService` | [`SettingsService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/SettingsService.cs) | 本機組態儲存與雙路徑容錯持久化，支援免安裝模式與本機資料夾自動切換 | `System.Text.Json` 來源產生器、檔案讀寫 | 全域設定與各視圖 |
| `Win32FilePicker` | [`Win32FilePicker.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/Win32FilePicker.cs) | 提供未封裝 WinUI 3 應用程式穩定可靠的檔案開啟與儲存對話方塊 | Win32 COM `comdlg32.dll` | `FileHashPage.xaml.cs`、`CrashReportPage.xaml.cs` |
| `LoggingService` | [`LoggingService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/LoggingService.cs) | 執行緒安全之本機診斷記錄檔寫入器 | 檔案資料流鎖定寫入 | 全域例外處理 |
| `NotificationService` | [`NotificationService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/NotificationService.cs) | 集中管理主視窗資訊列訊息呈現、定時關閉與音效提示 | WinUI 3 `InfoBar`、Win32 `MessageBeep` | 全域通知 |
| `ClipboardService` | [`ClipboardService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/ClipboardService.cs) | 安全存取系統剪貼簿，提供控制項圖示切換動畫與訊息聯動 | WinUI 3 資料傳輸 API、`ConditionalWeakTable` | 各功能視圖 |
| `LocalizationHelper` | [`LocalizationHelper.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/LocalizationHelper.cs) | MRT Core 資源字串集中讀取與鍵值多重容錯對應 | MRT Core `ResourceLoader` | 全域多語言綁定 |

---

## 3. 各項服務詳細技術規格

### 3.1 HardwareInfoService (HardwareProvider & HardwareInfoCache)

- **檔案位置**：[`lanlanlu-toolkit/Services/HardwareInfoService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/HardwareInfoService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class HardwareProvider` / `public class HardwareInfoCache`
- **職責說明**：  
  作為系統全域硬體規格資訊的快取中樞。WMI 查詢通常需耗費數百毫秒至數秒時間，本服務透過背景非同步執行緒整合查詢處理器型號與核心/執行緒數、實體記憶體容量/插槽與 DDR 世代、顯示卡名稱（優先排序獨顯並清理商標贅字）、主機板型號與 BIOS 版本、磁碟型號容量以及 Windows 組建版本，避免各頁面重複發起 WMI 查詢造成介面凍結。
- **核心公開介面**：
  - `public static HardwareInfoCache Cache { get; }`：取得目前記憶體中的全域硬體資訊快取。
  - `public static event Action? HardwareInfoUpdated`：當硬體規格掃描完成並更新快取時觸發。
  - `public static Task<HardwareInfoCache> ScanSystemInfoAsync(bool forceRefresh = false)`：非同步執行硬體規格掃描，預設若快取已有資料則直接回傳。
- **呼叫範例**：
  ```csharp
  var hardwareInfo = await HardwareProvider.ScanSystemInfoAsync();
  string cpuName = hardwareInfo.CpuName ?? "Unknown";
  string ramInfo = hardwareInfo.RamSize ?? "Unknown";
  ```
- **注意事項**：
  - 讀取 Windows 版本時，會同時讀取登錄檔 `CurrentVersion\DisplayVersion` 與 `UBR`（組建修訂號），以拼裝出完整的組建字串。
  - 記憶體插槽總數會透過 `Win32_PhysicalMemoryArray` 進行驗證，以確保呈現精確的已插槽位比率。

---

### 3.2 CrashReportService

- **檔案位置**：[`lanlanlu-toolkit/Services/CrashReportService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/CrashReportService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static partial class CrashReportService`
- **職責說明**：  
  負責診斷與排查 Windows 核心停止錯誤（藍色當機畫面）以及應用程式異常。自動讀取系統目錄下 `%SystemRoot%\Minidump` 的記憶體傾印檔案二進位標頭，解析其 BugCheck 代碼、發生時間與引發當機的驅動程式模組名稱（`.sys`）。同時讀取 Windows 事件檢視器的重大錯誤記錄，依據內建的故障代碼知識庫，透過多語言資源檔提供繁體中文與英文的疑難排解指引與修復建議。
- **核心公開介面**：
  - `public static Task<List<CrashReportItem>> GetCrashReportsAsync()`：非同步掃描傾印檔案與事件記錄，並依時間由新至舊排序。
  - `public static CrashReportSummary GenerateSummary(List<CrashReportItem> items)`：計算總當機次數、藍色當機畫面次數、應用程式錯誤次數與最後發生時間。
  - `public static string ExportToMarkdown(List<CrashReportItem> items)`：將目前的當機診斷記錄輸出為標準 Markdown 格式報告，便於複製與分享。
- **呼叫範例**：
  ```csharp
  List<CrashReportItem> reports = await CrashReportService.GetCrashReportsAsync();
  CrashReportSummary summary = CrashReportService.GenerateSummary(reports);
  string reportMarkdown = CrashReportService.ExportToMarkdown(reports);
  ```
- **注意事項**：
  - 存取 `%SystemRoot%\Minidump` 目錄可能受 Windows 檔案權限限制；若無系統管理員權限，服務會捕捉 `UnauthorizedAccessException` 並優雅回退，繼續讀取事件檢視器記錄。
  - 內建包含 `PAGE_FAULT_IN_NONPAGED_AREA`、`SYSTEM_SERVICE_EXCEPTION` 等數十種常見 BugCheck 代碼的繁中在地化建議。

---

### 3.3 InputTesterService (KeyboardLayoutProvider & KeyModel)

- **檔案位置**：[`lanlanlu-toolkit/Services/InputTesterService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/InputTesterService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class KeyboardLayoutProvider` / `public class KeyModel`
- **職責說明**：  
  為外設診斷頁面（`InputTesterPage`）提供標準化的鍵盤資料模型與動態矩陣佈局生成演算法。支援三種鍵盤物理配置（104 鍵全尺寸、87 鍵無數字鍵區、61 鍵精簡配置），精確計算按鍵寬度、邊界留白與對齊單元，並提供按鍵即時視覺狀態管理（預設、按下、已測試、抖動警告）。
- **核心公開介面**：
  - `public static KeyboardClusters GenerateClusters(KeyboardLayoutType layoutType)`：依指定佈局型別產生主鍵區、導航編輯區與數字鍵盤區的按鍵模型集合。
  - `KeyModel`：實作 `INotifyPropertyChanged`，提供按鍵虛擬鍵碼、掃描碼、按擊次數與視覺狀態綁定。
- **呼叫範例**：
  ```csharp
  KeyboardClusters clusters = KeyboardLayoutProvider.GenerateClusters(KeyboardLayoutType.Full104);
  var mainRows = clusters.MainClusterRows;
  ```
- **注意事項**：
  - 按鍵寬度以標準單位計算（1.0U = 42px，間距 6px），能精確適應介面的縮放畫布。

---

### 3.4 SettingsService

- **檔案位置**：[`lanlanlu-toolkit/Services/SettingsService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/SettingsService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class SettingsService`
- **職責說明**：  
  管理軟體設定的讀取、儲存與路徑決策。為了維持純綠色免安裝軟體的便攜性，本服務採用雙重路徑容錯策略：
  1. 優先嘗試於執行檔同層的 `Data/settings.json` 寫入。
  2. 若執行檔位於唯讀裝置（如唯讀光碟、鎖定之隨身碟或無寫入權限的系統目錄），自動降級切換至 `%LocalAppData%\lanlanlu_toolkit\settings.json`。
  3. 若兩者皆存在，依據最後修改時間自動讀取較新的版本。
  採用 `System.Text.Json` 來源產生器（`AppSettingsContext`），具備 Native AOT 與剪裁安全性。
- **核心公開介面**：
  - `public static string GetLanguage()` / `public static void SaveLanguage(string language)`
  - `public static string GetTheme()` / `public static void SaveTheme(string theme)`
  - `public static ElementTheme ToElementTheme(string themeName)`
  - `public static bool GetNotificationsEnabled()` / `public static void SaveNotificationsEnabled(bool enabled)`
  - `public static bool GetNotificationSound()` / `public static void SaveNotificationSound(bool enabled)`
  - `public static bool GetSoundEffectsEnabled()` / `public static void SaveSoundEffectsEnabled(bool enabled)`
  - `public static string GetTemperatureUnit()` / `public static void SaveTemperatureUnit(string unit)`
  - `public static bool GetDebugReportEnabled()` / `public static void SaveDebugReportEnabled(bool enabled)`
  - `public static string GetDebugReportPath()` / `public static void SaveDebugReportPath(string path)`
- **呼叫範例**：
  ```csharp
  string currentLang = SettingsService.GetLanguage();
  SettingsService.SaveTheme("Dark");
  ```
- **注意事項**：
  - 嚴格禁止向 Windows 登錄檔寫入設定值，確保免安裝解除時不留系統殘渣。

---

### 3.5 Win32FilePicker

- **檔案位置**：[`lanlanlu-toolkit/Services/Win32FilePicker.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/Win32FilePicker.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class Win32FilePicker`
- **職責說明**：  
  封裝 Win32 原生檔案對話方塊（`comdlg32.dll`）。WinUI 3 在未封裝獨立發布模式下，若使用 UWP 的 `FileOpenPicker`，需要繁瑣且易出錯的視窗控制代碼 (HWND) 介面初始化（`IInitializeWithWindow`），甚至在特定版本會出現停止運作或無法顯示的狀況。本服務提供穩定且跨 Windows 版本相容的原生對話方塊。
- **核心公開介面**：
  - `public static string? ShowOpenDialog(IntPtr hwnd, string title, string? filter = null)`：彈出檔案開啟視窗，回傳選取之完整路徑；若取消則回傳 `null`。
  - `public static string? ShowSaveDialog(IntPtr hwnd, string title, string defaultFileName, string filter, string defaultExt)`：彈出檔案儲存視窗，回傳欲儲存之完整路徑。
- **呼叫範例**：
  ```csharp
  IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
  string? selectedFile = Win32FilePicker.ShowOpenDialog(
      hwnd, 
      "選取傾印檔案", 
      "Minidump (*.dmp)\0*.dmp\0所有檔案 (*.*)\0*.*\0\0"
  );
  ```
- **注意事項**：
  - 呼叫時必須傳入所屬視窗代碼（`hwnd`），確保對話方塊正確成為模態子視窗並置頂於應用程式前端。

---

### 3.6 LoggingService

- **檔案位置**：[`lanlanlu-toolkit/Services/LoggingService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/LoggingService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class LoggingService`
- **職責說明**：  
  提供執行緒安全的本機文字日誌記錄器。當使用者在設定中啟用「除錯報告」時，會將記錄資訊即時附加寫入至指定路徑的 `debug_report.log` 檔案中，用於未攔截例外除錯與系統診斷流程追蹤。
- **核心公開介面**：
  - `public static void Log(string message)`：同步寫入記錄項目，內部以鎖定機制確保多執行緒安全。
  - `public static Task LogAsync(string message)`：非同步包裝寫入方法。
- **呼叫範例**：
  ```csharp
  LoggingService.Log($"[HardwareInfo] Telemetry scan initiated at {DateTime.Now}");
  ```
- **注意事項**：
  - 內部會自動檢驗 `SettingsService.GetDebugReportEnabled()`，若使用者未開啟除錯開關，呼叫會直接返回，不產生額外的檔案 I/O 開銷。

---

### 3.7 NotificationService

- **檔案位置**：[`lanlanlu-toolkit/Services/NotificationService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/NotificationService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class NotificationService`
- **職責說明**：  
  全域通知分派服務。負責將應用程式內部的操作成功、警告或錯誤訊息展示於 `MainWindow` 頂端的 `InfoBar` 控制項中。具備自動倒數計時關閉（預設 4 秒）、執行緒存取檢查（跨執行緒呼叫自動封送至 UI 執行緒）以及選擇性呼叫 Win32 `MessageBeep` 播放系統提示音。
- **核心公開介面**：
  - `public static void Initialize(InfoBar infoBar)`：在應用程式主視窗啟動時綁定目標 `InfoBar` 控制項。
  - `public static void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success)`：觸發全域通知。
- **呼叫範例**：
  ```csharp
  NotificationService.Show("修復完成", "系統組件掃描已順利執行完畢。", InfoBarSeverity.Success);
  ```
- **注意事項**：
  - 支援在背景執行緒中直接呼叫 `Show()`，內部會自動利用 `DispatcherQueue.TryEnqueue` 派送至 UI 執行緒，無需呼叫端手動處理封送。

---

### 3.8 ClipboardService

- **檔案位置**：[`lanlanlu-toolkit/Services/ClipboardService.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/ClipboardService.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class ClipboardService`
- **職責說明**：  
  提供一致且具備動態視覺回饋的剪貼簿存取服務。將文字寫入系統剪貼簿時，可自動將按鈕上的圖示暫時替換為勾選圖示（持續 1.5 秒後平滑復原），並可選擇性聯動觸發全域通知。透過 `ConditionalWeakTable` 追蹤圖示動畫狀態，可防範快速重複點擊時的動畫狀態衝突與取消代碼洩漏。
- **核心公開介面**：
  - `public static Task<bool> CopyTextAsync(...)`：複製指定文字至剪貼簿，可傳入視覺錨點控制項與通知參數。
  - `public static Task<string?> GetTextAsync()`：非同步讀取系統剪貼簿中的純文字。
- **呼叫範例**：
  ```csharp
  await ClipboardService.CopyTextAsync(hashString, copyButton, showNotification: true);
  ```
- **注意事項**：
  - 複製失敗時會捕捉例外並記錄於偵錯輸出，不會拋出未處理例外導致視窗中斷。

---

### 3.9 LocalizationHelper

- **檔案位置**：[`lanlanlu-toolkit/Services/LocalizationHelper.cs`](file:///d:/GitHub/lanlanlu-toolkit/lanlanlu-toolkit/Services/LocalizationHelper.cs)
- **命名空間**：`lanlanlu_toolkit.Services`
- **類別屬性**：`public static class LocalizationHelper`
- **職責說明**：  
  WinUI 3 多語言資源檔（`Resources.resw`）的核心讀取中樞。單例管理 `ResourceLoader` 執行個體，避免反覆具現化消耗記憶體。具備智慧容錯查表功能：
  1. 自動容錯轉換點號（`.`）與斜線（`/`）鍵值格式。
  2. 支援屬性後綴回退（若查詢 `Key.Text` 失敗，自動嘗試回退至 `Key` 或 `Key_Label`）。
  3. 若找不到鍵值，回傳原始鍵名作為最後回退，確保介面不因未攔截例外而中斷當機。
- **核心公開介面**：
  - `public static ResourceLoader Loader { get; }`：取得全域資源載入器。
  - `public static string GetString(string key)`：讀取在地化字串。
- **呼叫範例**：
  ```csharp
  string buttonTitle = LocalizationHelper.GetString("SystemRepairPage_DismScan");
  ```
- **注意事項**：
  - 任何新增或修改之鍵值，必須確保在 `Strings/zh-TW/Resources.resw` 與 `Strings/en-US/Resources.resw` 兩者同步存在。

---

## 4. 維護與新增服務檢核清單

未來若因新功能需要擴充現有服務或建立新服務時，AI 代理與開發者必須依循下列檢核流程：

1. **查閱優先**：在動手編寫新程式碼前，先行檢視本文檔，確認現有服務是否已有可複用的 API。
2. **避免重複造輪子**：若涉及對話方塊，使用 `Win32FilePicker`；涉及多語言文字，使用 `LocalizationHelper`；涉及硬體遙測，使用 `HardwareInfoService`。
3. **綠色相依審查**：新服務不可隨意引入外部大型 NuGet 套件或寫入系統登錄檔。
4. **規格書即時更新**：一旦新增服務或變更公開方法簽名，**必須同步更新本文件**，確保後續接手之代理與人類維護者擁有最新的架構視野。
