using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace lanlanlu_toolkit.Services
{
    public enum CrashType
    {
        Bsod,
        AppCrash,
        AppHang,
        DotNetCrash
    }

    public class CrashReportItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CrashType Type { get; set; }
        public DateTime Timestamp { get; set; }
        
        private string _title = string.Empty;
        public string Title 
        { 
            get => _title; 
            set => _title = CleanTitle(value); 
        }

        public string SourceOrApp { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorDescription { get; set; } = string.Empty;
        public string FaultingModule { get; set; } = string.Empty;
        public string FilePathOrDump { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
        public string RawDetails { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public bool HasDumpFile => !string.IsNullOrEmpty(FilePathOrDump) && File.Exists(FilePathOrDump);

        public string DisplayIcon => Type switch
        {
            CrashType.Bsod => "\uE7BA", // Warning Triangle for Kernel Crash / BSOD
            CrashType.AppCrash => "\uE783", // Circle Exclamation for Application Crash
            CrashType.AppHang => "\uE823", // Clock / History for Application Hang / Frozen UI
            CrashType.DotNetCrash => "\uE774", // Globe / Web for .NET Runtime Exceptions
            _ => "\uE783"
        };

        public string DisplayBadge => Type switch
        {
            CrashType.Bsod => LocalizationHelper.GetString("CrashReportPage_Badge_KernelCrash"),
            CrashType.AppCrash => LocalizationHelper.GetString("CrashReportPage_Badge_AppCrash"),
            CrashType.AppHang => LocalizationHelper.GetString("CrashReportPage_Badge_AppHang"),
            CrashType.DotNetCrash => LocalizationHelper.GetString("CrashReportPage_Badge_DotNetCrash"),
            _ => "Event"
        };

        public string FormattedTime => Timestamp.ToString("MM/dd HH:mm");

        public string BadgeBackgroundKey => Type switch
        {
            CrashType.Bsod => "#D13438", // Critical Red
            CrashType.AppCrash => "#EA385B", // Red-Orange
            CrashType.AppHang => "#D83B01", // Orange
            CrashType.DotNetCrash => "#881798", // Purple
            _ => "#605E5C"
        };

        private static string CleanTitle(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string trimmed = input.Trim();
            while (trimmed.EndsWith('-') || trimmed.EndsWith('–') || trimmed.EndsWith('—') || trimmed.EndsWith(' '))
            {
                trimmed = trimmed.TrimEnd('-', '–', '—', ' ').Trim();
            }
            return trimmed;
        }
    }

    public class CrashReportSummary
    {
        public int TotalCrashes { get; set; }
        public int BsodCount { get; set; }
        public int AppCrashCount { get; set; }
        public DateTime? LastCrashTime { get; set; }
    }

    /// <summary>
    /// Service responsible for scanning, parsing, and diagnosing Windows kernel minidumps and Application crash events.
    /// All localized diagnostic texts and recommendations are dynamically resolved from Resources.resw.
    /// </summary>
    public static class CrashReportService
    {
        private static readonly Dictionary<uint, string> KnownBugChecks = new()
        {
            [0x0000000A] = "IRQL_NOT_LESS_OR_EQUAL",
            [0x0000001E] = "KMODE_EXCEPTION_NOT_HANDLED",
            [0x0000003B] = "SYSTEM_SERVICE_EXCEPTION",
            [0x00000050] = "PAGE_FAULT_IN_NONPAGED_AREA",
            [0x0000007E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
            [0x0000009F] = "DRIVER_POWER_STATE_FAILURE",
            [0x000000A0] = "INTERNAL_POWER_ERROR",
            [0x000000D1] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
            [0x000000EF] = "CRITICAL_PROCESS_DIED",
            [0x00000101] = "CLOCK_WATCHDOG_TIMEOUT",
            [0x00000116] = "VIDEO_TDR_FAILURE",
            [0x00000124] = "WHEA_UNCORRECTABLE_ERROR",
            [0x00000133] = "DPC_WATCHDOG_VIOLATION",
            [0x00000139] = "KERNEL_SECURITY_CHECK_FAILURE",
            [0x00000154] = "UNEXPECTED_STORE_EXCEPTION",
            [0x0000001A] = "MEMORY_MANAGEMENT"
        };

        private static readonly Dictionary<uint, string> KnownAppExceptions = new()
        {
            [0xC0000005] = "STATUS_ACCESS_VIOLATION",
            [0xC0000409] = "STATUS_STACK_BUFFER_OVERRUN",
            [0xC0000374] = "STATUS_HEAP_CORRUPTION",
            [0xC00000FD] = "STATUS_STACK_OVERFLOW",
            [0xE0434352] = "CLR_EXCEPTION"
        };

        /// <summary>
        /// Asynchronously scans both System and Application Event Logs as well as Minidump directory.
        /// </summary>
        public static async Task<List<CrashReportItem>> GetCrashReportsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<CrashReportItem>();

                // 1. Scan System Event Log (BugCheck Event 1001 & 41)
                try
                {
                    var systemQuery = "*[System[(EventID=1001 or EventID=41) and TimeCreated[timediff(@SystemTime) <= 7776000000]]]"; // 90 days
                    var query = new EventLogQuery("System", PathType.LogName, systemQuery) { ReverseDirection = true };
                    using var reader = new EventLogReader(query);

                    EventRecord? record;
                    int count = 0;
                    while ((record = reader.ReadEvent()) != null && count < 50)
                    {
                        using (record)
                        {
                            var item = ParseSystemEvent(record);
                            if (item != null)
                            {
                                list.Add(item);
                                count++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportService] Failed to read System Event Log: {ex.Message}");
                }

                // 2. Scan Application Event Log specifically targeting Application Error, Application Hang, and .NET Runtime
                try
                {
                    var appQuery = "*[System[((EventID=1000 and Provider[@Name='Application Error']) or " +
                                   "(EventID=1002 and Provider[@Name='Application Hang']) or " +
                                   "(EventID=1026 and Provider[@Name='.NET Runtime'])) and " +
                                   "TimeCreated[timediff(@SystemTime) <= 7776000000]]]"; // 90 days
                    var query = new EventLogQuery("Application", PathType.LogName, appQuery) { ReverseDirection = true };
                    using var reader = new EventLogReader(query);

                    EventRecord? record;
                    int count = 0;
                    while ((record = reader.ReadEvent()) != null && count < 100)
                    {
                        using (record)
                        {
                            var item = ParseAppEvent(record);
                            if (item != null)
                            {
                                list.Add(item);
                                count++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportService] Failed to read Application Event Log: {ex.Message}");
                }

                // 3. Scan Minidump files from C:\Windows\Minidump and enhance items
                try
                {
                    string minidumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                    if (Directory.Exists(minidumpDir))
                    {
                        var dumpFiles = Directory.GetFiles(minidumpDir, "*.dmp");
                        foreach (var dumpPath in dumpFiles)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(dumpPath);
                                // Check if we already correlated this dump
                                var matched = list.FirstOrDefault(x => x.Type == CrashType.Bsod &&
                                    (!string.IsNullOrEmpty(x.FilePathOrDump) && x.FilePathOrDump.Equals(dumpPath, StringComparison.OrdinalIgnoreCase) ||
                                     Math.Abs((x.Timestamp - fileInfo.LastWriteTime).TotalMinutes) < 2));

                                if (matched != null)
                                {
                                    matched.FilePathOrDump = dumpPath;
                                    // Try reading binary details
                                    EnrichFromMinidumpBinary(matched, dumpPath);
                                }
                                else
                                {
                                    // Add standalone dump file entry
                                    var dumpItem = CreateItemFromDumpFile(dumpPath, fileInfo);
                                    if (dumpItem != null) list.Add(dumpItem);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[CrashReportService] Failed to scan Minidump directory: {ex.Message}");
                }

                // Sort by Timestamp Descending
                return list.OrderByDescending(x => x.Timestamp).ToList();
            });
        }

        private static CrashReportItem? ParseSystemEvent(EventRecord record)
        {
            try
            {
                string kernelBadge = LocalizationHelper.GetString("CrashReportPage_Badge_KernelCrash");
                string kernelSource = LocalizationHelper.GetString("CrashReportPage_Source_Kernel");

                if (record.Id == 1001) // BugCheck / WER System Error
                {
                    var msg = record.FormatDescription() ?? "";
                    string bugcheckCode = "";
                    string parameters = "";
                    string dumpPath = "";

                    // Extract from properties if available
                    if (record.Properties.Count > 0)
                    {
                        var prop0 = record.Properties[0]?.Value?.ToString() ?? "";
                        var match = Regex.Match(prop0, @"(0x[0-9a-fA-F]+)\s*\(([^)]*)\)");
                        if (match.Success)
                        {
                            bugcheckCode = NormalizeHexCode(match.Groups[1].Value);
                            parameters = NormalizeHexParameters(match.Groups[2].Value);
                        }
                        else
                        {
                            bugcheckCode = NormalizeHexCode(prop0);
                        }

                        if (record.Properties.Count > 1)
                        {
                            dumpPath = record.Properties[1]?.Value?.ToString() ?? "";
                        }
                    }

                    if (string.IsNullOrEmpty(bugcheckCode))
                    {
                        var match = Regex.Match(msg, @"(0x[0-9a-fA-F]{4,8})");
                        if (match.Success) bugcheckCode = NormalizeHexCode(match.Groups[1].Value);
                    }

                    uint codeNum = 0;
                    if (!string.IsNullOrEmpty(bugcheckCode))
                    {
                        try 
                        { 
                            string clean = bugcheckCode.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
                                ? bugcheckCode.Substring(2) 
                                : bugcheckCode;
                            codeNum = Convert.ToUInt32(clean, 16); 
                        } 
                        catch { }
                    }

                    var (name, desc, rec) = GetBugCheckInfo(codeNum);
                    string title = !string.IsNullOrEmpty(name)
                        ? $"{name} ({bugcheckCode})"
                        : (!string.IsNullOrEmpty(bugcheckCode) ? $"{kernelBadge} ({bugcheckCode})" : kernelBadge);

                    return new CrashReportItem
                    {
                        Type = CrashType.Bsod,
                        Timestamp = record.TimeCreated ?? DateTime.Now,
                        Title = title,
                        SourceOrApp = kernelSource,
                        ErrorCode = bugcheckCode,
                        ErrorDescription = desc,
                        FaultingModule = "ntoskrnl.exe",
                        FilePathOrDump = dumpPath,
                        Parameters = parameters,
                        RawDetails = msg,
                        Recommendation = rec
                    };
                }
                else if (record.Id == 41) // Kernel-Power unexpected shutdown
                {
                    string bugcheckStr = "";
                    string paramsStr = "";
                    uint bugcheckCode = 0;

                    if (record.Properties.Count > 0)
                    {
                        var bcVal = record.Properties[0]?.Value;
                        if (bcVal != null)
                        {
                            try
                            {
                                bugcheckCode = Convert.ToUInt32(bcVal);
                                if (bugcheckCode != 0)
                                {
                                    bugcheckStr = $"0x{bugcheckCode:X8}";
                                }
                            }
                            catch { }
                        }
                    }

                    // If BugCheckCode was 0, it's a direct power loss without BugCheck
                    if (bugcheckCode == 0) return null;

                    var (name, desc, rec) = GetBugCheckInfo(bugcheckCode);
                    string title = !string.IsNullOrEmpty(name)
                        ? $"{name} ({bugcheckStr})"
                        : $"{kernelBadge} ({bugcheckStr})";

                    return new CrashReportItem
                    {
                        Type = CrashType.Bsod,
                        Timestamp = record.TimeCreated ?? DateTime.Now,
                        Title = title,
                        SourceOrApp = kernelSource,
                        ErrorCode = bugcheckStr,
                        ErrorDescription = desc,
                        FaultingModule = "ntoskrnl.exe",
                        Parameters = paramsStr,
                        RawDetails = record.FormatDescription() ?? "",
                        Recommendation = rec
                    };
                }
            }
            catch { }
            return null;
        }

        private static CrashReportItem? ParseAppEvent(EventRecord record)
        {
            try
            {
                var msg = record.FormatDescription() ?? "";
                string unknownApp = LocalizationHelper.GetString("CrashReportPage_Source_Unknown");
                string appCrashBadge = LocalizationHelper.GetString("CrashReportPage_Badge_AppCrash");
                string appHangBadge = LocalizationHelper.GetString("CrashReportPage_Badge_AppHang");
                string dotnetBadge = LocalizationHelper.GetString("CrashReportPage_Badge_DotNetCrash");

                if (record.Id == 1000) // Application Error
                {
                    string appName = record.Properties.Count > 0 ? record.Properties[0]?.Value?.ToString() ?? unknownApp : unknownApp;
                    string faultModule = record.Properties.Count > 3 ? record.Properties[3]?.Value?.ToString() ?? "" : "";
                    string exceptionCode = record.Properties.Count > 6 ? NormalizeHexCode(record.Properties[6]?.Value?.ToString()) : "";
                    string appPath = record.Properties.Count > 10 ? record.Properties[10]?.Value?.ToString() ?? "" : "";

                    uint codeNum = 0;
                    if (!string.IsNullOrEmpty(exceptionCode))
                    {
                        try 
                        { 
                            string clean = exceptionCode.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
                                ? exceptionCode.Substring(2) 
                                : exceptionCode;
                            codeNum = Convert.ToUInt32(clean, 16); 
                        } 
                        catch { }
                    }
                    var (name, desc, rec) = GetAppExceptionInfo(codeNum);

                    string title;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(exceptionCode))
                    {
                        title = $"{appName} - {name} ({exceptionCode})";
                    }
                    else if (!string.IsNullOrEmpty(name))
                    {
                        title = $"{appName} - {name}";
                    }
                    else if (!string.IsNullOrEmpty(exceptionCode))
                    {
                        title = $"{appName} ({exceptionCode})";
                    }
                    else
                    {
                        title = $"{appName} - {appCrashBadge}";
                    }

                    return new CrashReportItem
                    {
                        Type = CrashType.AppCrash,
                        Timestamp = record.TimeCreated ?? DateTime.Now,
                        Title = title,
                        SourceOrApp = appName,
                        ErrorCode = exceptionCode,
                        ErrorDescription = desc,
                        FaultingModule = faultModule,
                        FilePathOrDump = appPath,
                        RawDetails = msg,
                        Recommendation = rec
                    };
                }
                else if (record.Id == 1002) // Application Hang
                {
                    string appName = record.Properties.Count > 0 ? record.Properties[0]?.Value?.ToString() ?? unknownApp : unknownApp;
                    string desc = LocalizationHelper.GetString("AppException_Hang_Desc");
                    string rec = LocalizationHelper.GetString("AppException_Hang_Rec");

                    return new CrashReportItem
                    {
                        Type = CrashType.AppHang,
                        Timestamp = record.TimeCreated ?? DateTime.Now,
                        Title = $"{appName} - {appHangBadge}",
                        SourceOrApp = appName,
                        ErrorCode = "AppHangB1",
                        ErrorDescription = desc,
                        FaultingModule = appName,
                        RawDetails = msg,
                        Recommendation = rec
                    };
                }
                else if (record.Id == 1026) // .NET Runtime
                {
                    string appName = unknownApp;
                    var appMatch = Regex.Match(msg, @"Application:\s*([^\r\n]+)");
                    if (appMatch.Success)
                    {
                        appName = Path.GetFileName(appMatch.Groups[1].Value.Trim());
                    }

                    string desc = LocalizationHelper.GetString("AppException_DotNet_Desc");
                    string rec = LocalizationHelper.GetString("AppException_DotNet_Rec");

                    var excMatch = Regex.Match(msg, @"Exception Info:\s*([^\r\n]+)");
                    string excType = excMatch.Success ? excMatch.Groups[1].Value.Trim() : "";

                    string title = !string.IsNullOrEmpty(excType)
                        ? $"{appName} - {excType}"
                        : $"{appName} - {dotnetBadge}";

                    return new CrashReportItem
                    {
                        Type = CrashType.DotNetCrash,
                        Timestamp = record.TimeCreated ?? DateTime.Now,
                        Title = title,
                        SourceOrApp = appName,
                        ErrorCode = "0xE0434352",
                        ErrorDescription = desc,
                        RawDetails = msg,
                        Recommendation = rec
                    };
                }
            }
            catch { }
            return null;
        }

        private static CrashReportItem? CreateItemFromDumpFile(string dumpPath, FileInfo fileInfo)
        {
            try
            {
                var item = new CrashReportItem
                {
                    Type = CrashType.Bsod,
                    Timestamp = fileInfo.LastWriteTime,
                    Title = Path.GetFileName(dumpPath),
                    SourceOrApp = LocalizationHelper.GetString("CrashReportPage_Source_Kernel"),
                    FilePathOrDump = dumpPath,
                    RawDetails = $"Minidump File: {dumpPath}\nSize: {fileInfo.Length / 1024.0:F1} KB\nCreated: {fileInfo.CreationTime}"
                };

                EnrichFromMinidumpBinary(item, dumpPath);
                return item;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads basic headers and stream info from a minidump file if binary access is permitted.
        /// </summary>
        private static void EnrichFromMinidumpBinary(CrashReportItem item, string dumpPath)
        {
            try
            {
                using var fs = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var br = new BinaryReader(fs);

                if (fs.Length < 32) return;

                uint signature = br.ReadUInt32();
                if (signature == 0x504D444D) // 'MDMP' Standard Minidump
                {
                    uint version = br.ReadUInt32();
                    uint numberOfStreams = br.ReadUInt32();
                    uint streamDirectoryRva = br.ReadUInt32();
                    uint checkSum = br.ReadUInt32();
                    uint timeDateStamp = br.ReadUInt32();

                    if (timeDateStamp > 0)
                    {
                        var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        item.Timestamp = unixEpoch.AddSeconds(timeDateStamp).ToLocalTime();
                    }

                    if (streamDirectoryRva > 0 && streamDirectoryRva < fs.Length)
                    {
                        fs.Seek(streamDirectoryRva, SeekOrigin.Begin);
                        var directories = new List<(uint Type, uint Size, uint Rva)>();
                        for (int i = 0; i < numberOfStreams && fs.Position + 12 <= fs.Length; i++)
                        {
                            uint type = br.ReadUInt32();
                            uint size = br.ReadUInt32();
                            uint rva = br.ReadUInt32();
                            directories.Add((type, size, rva));
                        }

                        // Check ExceptionStream (Type 6)
                        var excStream = directories.FirstOrDefault(d => d.Type == 6);
                        ulong crashAddress = 0;
                        if (excStream.Size >= 24 && excStream.Rva < fs.Length)
                        {
                            fs.Seek(excStream.Rva, SeekOrigin.Begin);
                            uint threadId = br.ReadUInt32();
                            uint alignment = br.ReadUInt32();
                            uint exceptionCode = br.ReadUInt32();
                            uint exceptionFlags = br.ReadUInt32();
                            ulong exceptionRecord = br.ReadUInt64();
                            crashAddress = br.ReadUInt64();
                            uint numberParameters = br.ReadUInt32();
                            uint unused = br.ReadUInt32();

                            if (string.IsNullOrEmpty(item.ErrorCode) || item.ErrorCode == "0x0")
                            {
                                item.ErrorCode = $"0x{exceptionCode:X8}";
                                var (name, desc, rec) = GetBugCheckInfo(exceptionCode);
                                string kernelBadge = LocalizationHelper.GetString("CrashReportPage_Badge_KernelCrash");
                                string defaultTitle = $"{kernelBadge} ({item.ErrorCode})";
                                item.Title = string.IsNullOrEmpty(name) ? defaultTitle : $"{name} ({item.ErrorCode})";
                                item.ErrorDescription = desc;
                                item.Recommendation = rec;
                            }

                            if (numberParameters > 0 && numberParameters <= 15)
                            {
                                var pList = new List<string>();
                                for (int p = 0; p < Math.Min(4, numberParameters); p++)
                                {
                                    pList.Add($"0x{br.ReadUInt64():X}");
                                }
                                if (string.IsNullOrEmpty(item.Parameters))
                                {
                                    item.Parameters = string.Join(", ", pList);
                                }
                            }
                        }

                        // Check ModuleListStream (Type 4) to find faulting module by crashAddress
                        var modStream = directories.FirstOrDefault(d => d.Type == 4);
                        if (modStream.Size >= 4 && modStream.Rva < fs.Length && crashAddress > 0)
                        {
                            fs.Seek(modStream.Rva, SeekOrigin.Begin);
                            uint numberOfModules = br.ReadUInt32();
                            for (int m = 0; m < numberOfModules && fs.Position + 108 <= fs.Length; m++)
                            {
                                long modPos = fs.Position;
                                ulong baseOfImage = br.ReadUInt64();
                                uint sizeOfImage = br.ReadUInt32();
                                uint modCheckSum = br.ReadUInt32();
                                uint modTimeDateStamp = br.ReadUInt32();
                                uint moduleNameRva = br.ReadUInt32();

                                if (crashAddress >= baseOfImage && crashAddress < baseOfImage + sizeOfImage && moduleNameRva < fs.Length)
                                {
                                    fs.Seek(moduleNameRva, SeekOrigin.Begin);
                                    uint strLen = br.ReadUInt32();
                                    byte[] strBytes = br.ReadBytes((int)Math.Min(strLen, 512));
                                    string fullModPath = Encoding.Unicode.GetString(strBytes).TrimEnd('\0');
                                    string modFileName = Path.GetFileName(fullModPath);
                                    if (!string.IsNullOrEmpty(modFileName))
                                    {
                                        item.FaultingModule = modFileName;
                                    }
                                    break;
                                }

                                fs.Seek(modPos + 108, SeekOrigin.Begin);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static (string Name, string Description, string Recommendation) GetBugCheckInfo(uint code)
        {
            string name = KnownBugChecks.TryGetValue(code, out var knownName) ? knownName : string.Empty;
            string descKey = $"BugCheck_0x{code:X8}_Desc";
            string recKey = $"BugCheck_0x{code:X8}_Rec";

            string desc = LocalizationHelper.GetString(descKey);
            if (string.IsNullOrEmpty(desc) || desc == descKey)
            {
                desc = LocalizationHelper.GetString("BugCheck_Unknown_Desc");
            }

            string rec = LocalizationHelper.GetString(recKey);
            if (string.IsNullOrEmpty(rec) || rec == recKey)
            {
                rec = LocalizationHelper.GetString("BugCheck_Unknown_Rec");
            }

            return (name, desc, rec);
        }

        private static (string Name, string Description, string Recommendation) GetAppExceptionInfo(uint code)
        {
            string name = KnownAppExceptions.TryGetValue(code, out var knownName) ? knownName : string.Empty;
            string descKey = $"AppException_0x{code:X8}_Desc";
            string recKey = $"AppException_0x{code:X8}_Rec";

            string desc = LocalizationHelper.GetString(descKey);
            if (string.IsNullOrEmpty(desc) || desc == descKey)
            {
                desc = LocalizationHelper.GetString("AppException_Unknown_Desc");
            }

            string rec = LocalizationHelper.GetString(recKey);
            if (string.IsNullOrEmpty(rec) || rec == recKey)
            {
                rec = LocalizationHelper.GetString("AppException_Unknown_Rec");
            }

            return (name, desc, rec);
        }

        public static CrashReportSummary GenerateSummary(List<CrashReportItem> items)
        {
            return new CrashReportSummary
            {
                TotalCrashes = items.Count,
                BsodCount = items.Count(x => x.Type == CrashType.Bsod),
                AppCrashCount = items.Count(x => x.Type != CrashType.Bsod),
                LastCrashTime = items.Count > 0 ? items.Max(x => (DateTime?)x.Timestamp) : null
            };
        }

        public static string ExportToMarkdown(List<CrashReportItem> items)
        {
            var sb = new StringBuilder();
            
            string titleTemplate = LocalizationHelper.GetString("CrashReport_Export_Title");
            string timeTemplate = LocalizationHelper.GetString("CrashReport_Export_GeneratedTime");
            string summaryTemplate = LocalizationHelper.GetString("CrashReport_Export_Summary");

            string lblTime = LocalizationHelper.GetString("CrashReport_Export_Field_Timestamp");
            string lblCode = LocalizationHelper.GetString("CrashReport_Export_Field_ErrorCode");
            string lblSource = LocalizationHelper.GetString("CrashReport_Export_Field_Source");
            string lblModule = LocalizationHelper.GetString("CrashReport_Export_Field_Module");
            string lblParams = LocalizationHelper.GetString("CrashReport_Export_Field_Parameters");
            string lblDump = LocalizationHelper.GetString("CrashReport_Export_Field_DumpPath");
            string lblDiag = LocalizationHelper.GetString("CrashReport_Export_Field_Diagnosis");
            string lblRec = LocalizationHelper.GetString("CrashReport_Export_Field_Recommendation");

            sb.AppendLine(titleTemplate);
            sb.AppendLine(string.Format(timeTemplate, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
            sb.AppendLine(string.Format(summaryTemplate, items.Count, items.Count(x => x.Type == CrashType.Bsod), items.Count(x => x.Type != CrashType.Bsod)));
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var item in items)
            {
                sb.AppendLine($"## [{item.DisplayBadge}] {item.Title}");
                sb.AppendLine($"- **{lblTime}**: {item.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"- **{lblCode}**: {item.ErrorCode}");
                sb.AppendLine($"- **{lblSource}**: {item.SourceOrApp}");
                sb.AppendLine($"- **{lblModule}**: {item.FaultingModule}");
                if (!string.IsNullOrEmpty(item.Parameters)) sb.AppendLine($"- **{lblParams}**: {item.Parameters}");
                if (!string.IsNullOrEmpty(item.FilePathOrDump)) sb.AppendLine($"- **{lblDump}**: {item.FilePathOrDump}");
                sb.AppendLine($"- **{lblDiag}**: {item.ErrorDescription}");
                sb.AppendLine($"- **{lblRec}**: {item.Recommendation}");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(item.RawDetails))
                {
                    sb.AppendLine("```text");
                    sb.AppendLine(item.RawDetails);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string NormalizeHexCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            string cleanHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? raw.Substring(2)
                : raw;

            if (uint.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out uint val))
            {
                return $"0x{val:X8}";
            }

            if (ulong.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out ulong uval))
            {
                return $"0x{uval:X}";
            }

            return "0x" + cleanHex.ToUpperInvariant();
        }

        private static string NormalizeHexParameters(string? parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters)) return "";
            return Regex.Replace(parameters, @"(?i)\b0x([0-9a-f]+)\b", m => "0x" + m.Groups[1].Value.ToUpperInvariant());
        }
    }
}
