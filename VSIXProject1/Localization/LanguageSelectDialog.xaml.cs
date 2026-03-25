using System.Collections.Generic;
using System.Windows;

namespace VSIXProject1.Localization
{
    public partial class LanguageSelectDialog : Window
    {
        private sealed class LanguageOption
        {
            public SupportedLanguage Language { get; set; }
            public string DisplayName { get; set; }
        }

        public SupportedLanguage SelectedLanguage { get; private set; }

        public LanguageSelectDialog(SupportedLanguage currentLanguage)
        {
            InitializeComponent();
            SelectedLanguage = currentLanguage;

            var options = new List<LanguageOption>
            {
                new LanguageOption
                {
                    Language = SupportedLanguage.ChineseSimplified,
                    DisplayName = LocalizationService.GetLanguageDisplayName(SupportedLanguage.ChineseSimplified)
                },
                new LanguageOption
                {
                    Language = SupportedLanguage.English,
                    DisplayName = LocalizationService.GetLanguageDisplayName(SupportedLanguage.English)
                }
            };

            LanguageComboBox.ItemsSource = options;
            LanguageComboBox.SelectedValue = currentLanguage;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (LanguageComboBox.SelectedValue is SupportedLanguage language)
            {
                SelectedLanguage = language;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
