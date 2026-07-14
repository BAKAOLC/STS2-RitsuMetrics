// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Reflection;
using STS2RitsuLib.Utils;

namespace STS2RitsuMetrics.Localization
{
    internal static class ModLocalization
    {
        private static readonly Lazy<I18N> InstanceFactory = new(() => new(
            "STS2-RitsuMetrics",
            resourceFolders: ["STS2RitsuMetrics.Localization"],
            resourceAssembly: Assembly.GetExecutingAssembly()));

        internal static I18N Instance => InstanceFactory.Value;

        internal static event Action? Changed
        {
            add => Instance.Changed += value;
            remove => Instance.Changed -= value;
        }

        internal static string Get(string key, string fallback)
        {
            return Instance.Get(key, fallback);
        }

        internal static string Format(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Instance.Get(key, fallback), args);
        }
    }
}
