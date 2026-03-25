using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;

namespace VSIXProject1.Localization
{
    /// <summary>
    /// Provides data binding support for localized strings in WPF XAML.
    /// </summary>
    public class TranslationProvider : INotifyPropertyChanged
    {
        private static TranslationProvider _instance;

        public static TranslationProvider Instance => _instance ?? (_instance = new TranslationProvider());

        private TranslationProvider()
        {
        }

        /// <summary>
        /// Indexer to support WPF binding (e.g., {Binding [ToolWindow_Title], Source={x:Static loc:TranslationProvider.Instance}})
        /// </summary>
        public string this[string key] => LocalizationService.GetString(key);

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Call this method when the language changes to force WPF to re-evaluate all bindings using the indexer.
        /// </summary>
        public void Refresh()
        {
            // Notify that all indexer properties have changed
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }
    }
}
