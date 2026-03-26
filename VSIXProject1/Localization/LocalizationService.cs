using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace VSIXProject1.Localization
{
    /// <summary>
    /// Supported languages for the extension
    /// </summary>
    public enum SupportedLanguage
    {
        /// <summary>
        /// Simplified Chinese (zh-CN) - Default
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
        private const string CollectionPath = "LenovoDirenjie";
        private const string LanguageProperty = "Language";

        private static SupportedLanguage _currentLanguage = SupportedLanguage.ChineseSimplified;
        private static ResourceManager _resourceManager;
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Event raised when the language is changed
        /// </summary>
        public static event EventHandler LanguageChanged;

        /// <summary>
        /// Gets the current language setting
        /// </summary>
        public static SupportedLanguage CurrentLanguage
        {
            get => _currentLanguage;
            private set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    UpdateCulture();
                    LanguageChanged?.Invoke(null, EventArgs.Empty);
                    TranslationProvider.Instance.Refresh();
                }
            }
        }

        /// <summary>
        /// Gets the CultureInfo for the current language
        /// </summary>
        public static CultureInfo CurrentCulture => GetCultureInfo(CurrentLanguage);

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

                    LoadFromSettings(package);
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LocalizationService initialization error: {ex.Message}");
                    _currentLanguage = SupportedLanguage.ChineseSimplified;
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
        /// Set the current language
        /// </summary>
        public static void SetLanguage(SupportedLanguage language, AsyncPackage package = null)
        {
            CurrentLanguage = language;
            if (package != null)
            {
                SaveToSettings(package);
            }
        }

        /// <summary>
        /// Load language setting from VS settings store
        /// </summary>
        public static void LoadFromSettings(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var settingsManager = new ShellSettingsManager(package);
                var userSettingsStore = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                if (userSettingsStore.CollectionExists(CollectionPath))
                {
                    if (userSettingsStore.PropertyExists(CollectionPath, LanguageProperty))
                    {
                        int languageValue = userSettingsStore.GetInt32(CollectionPath, LanguageProperty);
                        if (Enum.IsDefined(typeof(SupportedLanguage), languageValue))
                        {
                            _currentLanguage = (SupportedLanguage)languageValue;
                            UpdateCulture();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFromSettings error: {ex.Message}");
            }
        }

        /// <summary>
        /// Save language setting to VS settings store
        /// </summary>
        public static void SaveToSettings(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var settingsManager = new ShellSettingsManager(package);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    userSettingsStore.CreateCollection(CollectionPath);
                }

                userSettingsStore.SetInt32(CollectionPath, LanguageProperty, (int)_currentLanguage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveToSettings error: {ex.Message}");
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
                case SupportedLanguage.English:
                    return GetString("Lang_English");
                case SupportedLanguage.ChineseSimplified:
                default:
                    return GetString("Lang_Chinese");
            }
        }

        private static void UpdateCulture()
        {
            var culture = GetCultureInfo(_currentLanguage);
            Thread.CurrentThread.CurrentUICulture = culture;
        }
    }
}
