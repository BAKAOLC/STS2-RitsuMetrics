// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Reflection;
using STS2RitsuLib.Utils;

namespace STS2RitsuMetrics.Localization
{
    internal static class ModLocalization
    {
        private static readonly Lazy<I18N> InstanceFactory = new(CreateInstance);
        private static readonly LocalizationLanguageState LanguageState = new();

        internal static I18N Instance => InstanceFactory.Value;
        private static event Action? ChangedHandlers;

        internal static event Action? Changed
        {
            add
            {
                _ = Instance;
                ChangedHandlers += value;
            }
            remove => ChangedHandlers -= value;
        }

        internal static string Get(string key, string fallback)
        {
            return Instance.Get(key, fallback);
        }

        internal static string Format(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Instance.Get(key, fallback), args);
        }

        internal static void SynchronizeCurrentLanguage()
        {
            var instance = Instance;
            if (!LanguageState.SwitchTo(I18N.ResolveCurrentLanguageCode()))
                return;
            instance.ForceReload();
        }

        private static I18N CreateInstance()
        {
            var instance = new I18N(
                "STS2-RitsuMetrics",
                resourceFolders: ["STS2RitsuMetrics.Localization"],
                resourceAssembly: Assembly.GetExecutingAssembly());
            instance.Changed += OnInstanceChanged;
            LanguageState.Record(I18N.ResolveCurrentLanguageCode());
            return instance;
        }

        private static void OnInstanceChanged()
        {
            LanguageState.Record(I18N.ResolveCurrentLanguageCode());
            ChangedHandlers?.Invoke();
        }
    }
}
