using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace lanlanlu_toolkit.Services
{
    /// <summary>
    /// Centralized localization helper to avoid redundant ResourceLoader instances.
    /// </summary>
    public static class LocalizationHelper
    {
        private static readonly Lazy<ResourceLoader> _loader = new(() => new ResourceLoader());

        public static ResourceLoader Loader => _loader.Value;

        public static string GetString(string key)
        {
            try
            {
                return Loader.GetString(key);
            }
            catch
            {
                return key; // Fallback to key name if resource not found
            }
        }
    }
}
