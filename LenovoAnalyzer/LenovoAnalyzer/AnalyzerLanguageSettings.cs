using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace LenovoAnalyzer
{
    /// <summary>
    /// Provides language settings for the analyzer that can be controlled by VSIX.
    /// Uses file-based storage to support cross-process communication (OOP analyzers).
    /// </summary>
    public static class AnalyzerLanguageSettings
    {
        private static readonly string SettingsDirectory = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                         "LenovoAnalyzer");
        
        private static readonly string SettingsFilePath = 
            Path.Combine(SettingsDirectory, "language.txt");

        private static volatile CultureInfo _cachedCulture = null;
        private static DateTime _lastFileCheckTime = DateTime.MinValue;
        private static DateTime _lastFileWriteTime = DateTime.MinValue;
        private static readonly object _lock = new object();
        
        private const int CacheExpirationSeconds = 2;

        /// <summary>
        /// Sets the language for analyzer diagnostics.
        /// Writes to file for cross-process access.
        /// </summary>
        /// <param name="cultureName">Culture name (e.g., "zh-CN", "en-US"), or null/empty to follow IDE language</param>
        public static void SetLanguage(string cultureName)
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                File.WriteAllText(SettingsFilePath, cultureName ?? "");
                
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(cultureName))
                    {
                        _cachedCulture = null;
                        Resources.Culture = null;
                    }
                    else
                    {
                        _cachedCulture = new CultureInfo(cultureName);
                        Resources.Culture = _cachedCulture;
                    }
                    _lastFileWriteTime = File.GetLastWriteTimeUtc(SettingsFilePath);
                    _lastFileCheckTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnalyzerLanguageSettings.SetLanguage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current culture for resource lookup.
        /// Reads from file with caching for performance.
        /// </summary>
        public static CultureInfo GetCulture()
        {
            try
            {
                var now = DateTime.UtcNow;
                
                lock (_lock)
                {
                    if ((now - _lastFileCheckTime).TotalSeconds < CacheExpirationSeconds)
                    {
                        return _cachedCulture ?? Thread.CurrentThread.CurrentUICulture;
                    }
                }

                if (!File.Exists(SettingsFilePath))
                {
                    lock (_lock)
                    {
                        _cachedCulture = null;
                        _lastFileCheckTime = now;
                    }
                    return Thread.CurrentThread.CurrentUICulture;
                }

                var currentFileWriteTime = File.GetLastWriteTimeUtc(SettingsFilePath);
                
                lock (_lock)
                {
                    if (currentFileWriteTime == _lastFileWriteTime && _cachedCulture != null)
                    {
                        _lastFileCheckTime = now;
                        return _cachedCulture;
                    }

                    var cultureName = File.ReadAllText(SettingsFilePath).Trim();
                    _lastFileWriteTime = currentFileWriteTime;
                    _lastFileCheckTime = now;

                    if (string.IsNullOrEmpty(cultureName))
                    {
                        _cachedCulture = null;
                        Resources.Culture = null;
                        return Thread.CurrentThread.CurrentUICulture;
                    }

                    _cachedCulture = new CultureInfo(cultureName);
                    Resources.Culture = _cachedCulture;
                    return _cachedCulture;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnalyzerLanguageSettings.GetCulture error: {ex.Message}");
                return Thread.CurrentThread.CurrentUICulture;
            }
        }

        /// <summary>
        /// Gets the localized string for a resource key
        /// </summary>
        public static string GetString(string key)
        {
            return Resources.ResourceManager.GetString(key, GetCulture()) ?? key;
        }

        /// <summary>
        /// Gets a formatted localized string
        /// </summary>
        public static string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }
    }
}
