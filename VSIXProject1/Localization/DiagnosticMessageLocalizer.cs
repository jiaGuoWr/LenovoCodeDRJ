using Microsoft.CodeAnalysis;

namespace VSIXProject1.Localization
{
    /// <summary>
    /// Helper class to localize diagnostic messages from Roslyn analyzers
    /// </summary>
    public static class DiagnosticMessageLocalizer
    {
        /// <summary>
        /// Gets the localized display message for a diagnostic based on its ID.
        /// Falls back to the diagnostic's default message if a translation is not found.
        /// </summary>
        /// <param name="diagnostic">The diagnostic to localize</param>
        /// <returns>The localized message</returns>
        public static string GetDisplayMessage(Diagnostic diagnostic)
        {
            if (diagnostic == null) return string.Empty;

            // Attempt to find a localized string using the diagnostic ID as the key
            string localizedMessage = LocalizationService.GetString(diagnostic.Id);

            // If the key is returned (meaning no translation was found) or it's empty, use the original message
            if (string.IsNullOrEmpty(localizedMessage) || localizedMessage == diagnostic.Id)
            {
                return diagnostic.GetMessage();
            }

            return localizedMessage;
        }
    }
}