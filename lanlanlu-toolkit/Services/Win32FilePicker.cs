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
    }
}
