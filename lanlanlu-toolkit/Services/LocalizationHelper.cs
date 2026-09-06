using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Centralized localization helper to avoid redundant ResourceLoader instances.
    /// Handles dot/slash property normalization and label fallbacks automatically.
    /// </summary>
    public static class LocalizationHelper
    {
        private static readonly Lazy<ResourceLoader> _loader = new(() => new ResourceLoader());

        public static ResourceLoader Loader => _loader.Value;

        public static string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 1. Direct query
            try
            {
                string val = Loader.GetString(key);
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            // 2. Cross-lookup between dot and slash (MRT Core ResourceLoader conventions)
            if (key.Contains('.'))
            {
                try
                {
                    string slashKey = key.Replace('.', '/');
                    string val = Loader.GetString(slashKey);
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }
            else if (key.Contains('/'))
            {
                try
                {
                    string dotKey = key.Replace('/', '.');
                    string val = Loader.GetString(dotKey);
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }

            // 3. Fallback for .Text / /Text suffix -> try base key or baseKey + _Label
            if (key.EndsWith(".Text", StringComparison.OrdinalIgnoreCase) || key.EndsWith("/Text", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string baseKey = key.Substring(0, key.Length - 5);
                    string val = Loader.GetString(baseKey);
                    if (!string.IsNullOrEmpty(val)) return val;

                    val = Loader.GetString(baseKey + "_Label");
                    if (!string.IsNullOrEmpty(val))
                    {
                        // Strip trailing colon from label if present
                        return val.TrimEnd('：', ':', ' ');
                    }
                }
                catch { }
            }

            // 4. Fallback for _Label -> try /Text or base key
            if (key.EndsWith("_Label", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string baseKey = key.Substring(0, key.Length - 6);
                    string val = Loader.GetString(baseKey + "/Text");
                    if (!string.IsNullOrEmpty(val)) return val;

                    val = Loader.GetString(baseKey);
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }

            // 5. Fallback for baseKey -> try /Text
            try
            {
                string val = Loader.GetString(key + "/Text");
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            return key; // Fallback to key name if resource not found
        }
    }
}
