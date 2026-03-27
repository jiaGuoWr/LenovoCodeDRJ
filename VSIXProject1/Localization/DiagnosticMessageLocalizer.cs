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
        /// Uses VSIX resource files for localization.
        /// </summary>
        public static string GetDisplayMessage(Diagnostic diagnostic)
        {
            if (diagnostic == null) return string.Empty;

            var localizedMessage = LocalizationService.GetString("Diag_" + diagnostic.Id);
            
            if (localizedMessage != "Diag_" + diagnostic.Id)
            {
                return localizedMessage;
            }

            return diagnostic.GetMessage();
        }
    }
}