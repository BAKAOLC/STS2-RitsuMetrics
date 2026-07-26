// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Localization
{
    internal sealed class LocalizationLanguageState
    {
        private readonly Lock _gate = new();
        private string? _language;

        internal void Record(string language)
        {
            lock (_gate)
            {
                _language = language;
            }
        }

        internal bool SwitchTo(string language)
        {
            lock (_gate)
            {
                if (string.Equals(_language, language, StringComparison.OrdinalIgnoreCase))
                    return false;
                _language = language;
                return true;
            }
        }
    }
}
