using System;
using System.Runtime.InteropServices;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Centralized wrapper for classic Win32 Open and Save file dialogs (comdlg32.dll).
    /// </summary>
    public static class Win32FilePicker
    {
        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

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

        public static string? ShowOpenDialog(IntPtr hwnd, string title, string? filter = null)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;

            if (!string.IsNullOrEmpty(filter))
            {
                ofn.lpstrFilter = filter;
            }
            else
            {
                string allFilesText = LocalizationHelper.GetString("System_AllFiles");
                ofn.lpstrFilter = $"{allFilesText} (*.*)\0*.*\0\0";
            }

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

        public static string? ShowSaveDialog(IntPtr hwnd, string title, string defaultFileName, string filter, string defaultExt)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrFilter = filter;
            ofn.lpstrFile = defaultFileName.PadRight(1024, '\0');
            ofn.nMaxFile = 1024;
            ofn.lpstrDefExt = defaultExt;
            // OFN_OVERWRITEPROMPT = 0x00000002, OFN_PATHMUSTEXIST = 0x00000800
            ofn.Flags = 0x00000002 | 0x00000800;
            ofn.lpstrTitle = title;

            if (GetSaveFileName(ref ofn))
            {
                return ofn.lpstrFile.TrimEnd('\0').Trim();
            }
            return null;
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;

        /// <summary>
        /// Displays the modern Win32 COM folder picker dialog (IFileOpenDialog with FOS_PICKFOLDERS).
        /// </summary>
        public static string? ShowFolderDialog(IntPtr hwnd, string? initialPath = null, string? title = null)
        {
            IFileOpenDialog? dialog = null;
            IShellItem? initialFolderItem = null;
            IShellItem? resultItem = null;

            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
                {
                    Guid shellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                    if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, shellItemGuid, out initialFolderItem) == 0 && initialFolderItem != null)
                    {
                        dialog.SetFolder(initialFolderItem);
                    }
                }

                if (dialog.Show(hwnd) == 0) // S_OK
                {
                    dialog.GetResult(out resultItem);
                    resultItem.GetDisplayName(0x80058000, out string path); // SIGDN_FILESYSPATH = 0x80058000
                    return path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win32 FolderPicker Error: {ex.Message}");
            }
            finally
            {
                if (resultItem != null) Marshal.ReleaseComObject(resultItem);
                if (initialFolderItem != null) Marshal.ReleaseComObject(initialFolderItem);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
            return null;
        }
    }
}
