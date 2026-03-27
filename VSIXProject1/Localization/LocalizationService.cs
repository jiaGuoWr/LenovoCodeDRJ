using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using LenovoAnalyzer;

namespace VSIXProject1.Localization
{
    /// <summary>
    /// Supported languages for the extension
    /// </summary>
    public enum SupportedLanguage
    {
        /// <summary>
        /// Auto-detect from IDE language
        /// </summary>
        Auto = -1,

        /// <summary>
        /// Simplified Chinese (zh-CN)
        /// </summary>
        ChineseSimplified = 0,

        /// <summary>
        /// English (en-US)
        /// </summary>
        English = 1
    }

    /// <summary>
    /// Core localization service for managing language settings and string resources
    /// </summary>
    public static class LocalizationService
    {
        private static SupportedLanguage _currentLanguage = SupportedLanguage.Auto;
        private static ResourceManager _resourceManager;
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Event raised when the language is changed
        /// </summary>
        public static event EventHandler LanguageChanged;

        /// <summary>
        /// Gets the current effective language (resolves Auto to actual language)
        /// </summary>
        public static SupportedLanguage CurrentLanguage
        {
            get
            {
                if (_currentLanguage == SupportedLanguage.Auto)
                {
                    return DetectIdeLanguage();
                }
                return _currentLanguage;
            }
        }

        /// <summary>
        /// Gets the CultureInfo for the current language
        /// </summary>
        public static CultureInfo CurrentCulture => GetCultureInfo(CurrentLanguage);

        /// <summary>
        /// Detects the IDE language from Visual Studio settings
        /// </summary>
        private static SupportedLanguage DetectIdeLanguage()
        {
            try
            {
                var culture = Thread.CurrentThread.CurrentUICulture;
                if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return SupportedLanguage.ChineseSimplified;
                }
                return SupportedLanguage.English;
            }
            catch
            {
                return SupportedLanguage.English;
            }
        }

        /// <summary>
        /// Initialize the localization service
        /// </summary>
        public static void Initialize(AsyncPackage package)
        {
            lock (_lock)
            {
                if (_isInitialized) return;

                try
                {
                    _resourceManager = new ResourceManager(
                        "VSIXLenovoQiraDRJ.Resources.Strings",
                        typeof(LocalizationService).Assembly);

                    _currentLanguage = SupportedLanguage.Auto;
                    UpdateCulture();
                    NotifyAnalyzer();
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LocalizationService initialization error: {ex.Message}");
                    _currentLanguage = SupportedLanguage.Auto;
                    _isInitialized = true;
                }
            }
        }

        /// <summary>
        /// Get a localized string by key
        /// </summary>
        public static string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            try
            {
                if (_resourceManager != null)
                {
                    var value = _resourceManager.GetString(key, CurrentCulture);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetString error for key '{key}': {ex.Message}");
            }

            return key;
        }

        /// <summary>
        /// Get a formatted localized string
        /// </summary>
        public static string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(CurrentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// Set the current language (only for current session, not persisted)
        /// </summary>
        public static void SetLanguage(SupportedLanguage language, AsyncPackage package = null)
        {
            var oldEffectiveLanguage = CurrentLanguage;
            _currentLanguage = language;
            var newEffectiveLanguage = CurrentLanguage;

            if (oldEffectiveLanguage != newEffectiveLanguage)
            {
                UpdateCulture();
                NotifyAnalyzer();
                LanguageChanged?.Invoke(null, EventArgs.Empty);
                TranslationProvider.Instance.Refresh();
            }
        }

        /// <summary>
        /// Notify the analyzer about language change
        /// </summary>
        private static void NotifyAnalyzer()
        {
            try
            {
                var cultureName = GetCultureInfo(CurrentLanguage).Name;
                AnalyzerLanguageSettings.SetLanguage(cultureName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotifyAnalyzer error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the CultureInfo for a supported language
        /// </summary>
        public static CultureInfo GetCultureInfo(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.English:
                    return new CultureInfo("en-US");
                case SupportedLanguage.ChineseSimplified:
                default:
                    return new CultureInfo("zh-CN");
            }
        }

        /// <summary>
        /// Get the display name for a supported language
        /// </summary>
        public static string GetLanguageDisplayName(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.Auto:
                    return GetString("Lang_Auto");
                case SupportedLanguage.English:
                    return GetString("Lang_English");
                case SupportedLanguage.ChineseSimplified:
                default:
                    return GetString("Lang_Chinese");
            }
        }

        private static void UpdateCulture()
        {
            // Do not modify Thread.CurrentThread.CurrentUICulture
            // as it affects VS internal components and can cause crashes
            // when switching to a language that VS doesn't have resources for.
            // We only use CurrentCulture in our own GetString() calls.
        }
    }
}
