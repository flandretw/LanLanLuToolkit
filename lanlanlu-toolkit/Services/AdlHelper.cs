using System;
using System.IO;
using System.Runtime.InteropServices;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Lightweight P/Invoke wrapper for AMD Display Library (atiadlxx.dll).
    /// Provides zero-dependency, sub-millisecond AMD GPU & APU temperature telemetry.
    /// </summary>
    public static class AdlHelper
    {
        private static IntPtr _adlModule = IntPtr.Zero;
        private static IntPtr _adlContext = IntPtr.Zero;
        private static bool _isInitialized = false;
        private static bool _initAttempted = false;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ADL_MAIN_MALLOC_CALLBACK(int iSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ADL2_Main_Control_Create_delegate(ADL_MAIN_MALLOC_CALLBACK callback, int iEnumConnectedAdapters, out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ADL2_Main_Control_Destroy_delegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ADL2_OverdriveN_Temperature_Get_delegate(IntPtr context, int iAdapterIndex, int iTemperatureType, out int iTemperature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ADL_Overdrive6_Temperature_Get_delegate(int iAdapterIndex, out int iTemperature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ADL2_Adapter_NumberOfAdapters_Get_delegate(IntPtr context, out int numAdapters);

        private static ADL_MAIN_MALLOC_CALLBACK? _mallocCallback;
        private static ADL2_Main_Control_Create_delegate? _adl2Create;
        private static ADL2_Main_Control_Destroy_delegate? _adl2Destroy;
        private static ADL2_OverdriveN_Temperature_Get_delegate? _adl2OdNTemp;
        private static ADL_Overdrive6_Temperature_Get_delegate? _adlOd6Temp;
        private static ADL2_Adapter_NumberOfAdapters_Get_delegate? _adl2NumAdapters;

        private static IntPtr MallocCallbackImpl(int size)
        {
            return Marshal.AllocHGlobal(size);
        }

        public static bool Initialize()
        {
            if (_initAttempted) return _isInitialized;
            _initAttempted = true;

            try
            {
                string[] possiblePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "atiadlxx.dll"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "atiadlxy.dll"),
                    "atiadlxx.dll"
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path) || path == "atiadlxx.dll")
                    {
                        _adlModule = LoadLibrary(path);
                        if (_adlModule != IntPtr.Zero) break;
                    }
                }

                if (_adlModule == IntPtr.Zero) return false;

                _mallocCallback = new ADL_MAIN_MALLOC_CALLBACK(MallocCallbackImpl);
                _adl2Create = GetDelegate<ADL2_Main_Control_Create_delegate>("ADL2_Main_Control_Create");
                _adl2Destroy = GetDelegate<ADL2_Main_Control_Destroy_delegate>("ADL2_Main_Control_Destroy");
                _adl2OdNTemp = GetDelegate<ADL2_OverdriveN_Temperature_Get_delegate>("ADL2_OverdriveN_Temperature_Get");
                _adlOd6Temp = GetDelegate<ADL_Overdrive6_Temperature_Get_delegate>("ADL_Overdrive6_Temperature_Get");
                _adl2NumAdapters = GetDelegate<ADL2_Adapter_NumberOfAdapters_Get_delegate>("ADL2_Adapter_NumberOfAdapters_Get");

                if (_adl2Create != null && _adl2Create(_mallocCallback, 1, out _adlContext) == 0 && _adlContext != IntPtr.Zero)
                {
                    _isInitialized = true;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static T? GetDelegate<T>(string procName) where T : Delegate
        {
            if (_adlModule == IntPtr.Zero) return null;
            IntPtr p = GetProcAddress(_adlModule, procName);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        /// <summary>
        /// Retrieves AMD GPU / APU Temperature in Celsius.
        /// </summary>
        public static double GetTemperature(int adapterIndex = 0)
        {
            if (!Initialize() || _adlContext == IntPtr.Zero) return 0;

            try
            {
                // Try OverdriveN (Type 1 = Edge, Type 7 = Hotspot)
                if (_adl2OdNTemp != null)
                {
                    for (int a = 0; a < 6; a++)
                    {
                        if (_adl2OdNTemp(_adlContext, a, 1, out int tempMilliC) == 0 && tempMilliC > 0)
                        {
                            double c = tempMilliC / 1000.0;
                            if (c > 10 && c < 125) return c;
                        }
                    }
                }

                // Fallback to Overdrive6
                if (_adlOd6Temp != null)
                {
                    for (int a = 0; a < 6; a++)
                    {
                        if (_adlOd6Temp(a, out int tempMilliC) == 0 && tempMilliC > 0)
                        {
                            double c = tempMilliC / 1000.0;
                            if (c > 10 && c < 125) return c;
                        }
                    }
                }
            }
            catch { }

            return 0;
        }
    }
}
