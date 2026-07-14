// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Ui
{
    internal static class DashboardPresentation
    {
        private const int MinimumFontSize = 12;
        private const int MaximumFontSize = 24;

        internal static int FontSize(
            IReadOnlyDictionary<string, string> parameters,
            int fallback)
        {
            return parameters.TryGetValue(DashboardParameterIds.FontSize, out var value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fontSize)
                ? Math.Clamp(fontSize, MinimumFontSize, MaximumFontSize)
                : Math.Clamp(fallback, MinimumFontSize, MaximumFontSize);
        }

        internal static DashboardControlDensity ControlDensity(IReadOnlyDictionary<string, string> parameters)
        {
            return Layout(parameters) switch
            {
                DashboardParameterValues.Standard => DashboardControlDensity.Comfortable,
                _ => DashboardControlDensity.Compact,
            };
        }

        internal static bool SingleLine(IReadOnlyDictionary<string, string> parameters)
        {
            return Layout(parameters) == DashboardParameterValues.SingleLine;
        }

        internal static bool SplitSummons(IReadOnlyDictionary<string, string> parameters)
        {
            return parameters.GetValueOrDefault(DashboardParameterIds.SummonDisplay) ==
                   DashboardParameterValues.SplitSummons;
        }

        internal static float WindowOpacity(
            IReadOnlyDictionary<string, string> parameters,
            int fallback)
        {
            return Percentage(parameters, DashboardParameterIds.WindowOpacity, fallback, 20) / 100f;
        }

        internal static float BackgroundOpacity(
            IReadOnlyDictionary<string, string> parameters,
            int fallback)
        {
            return Percentage(parameters, DashboardParameterIds.BackgroundOpacity, fallback, 0) / 100f;
        }

        internal static bool FullOpacityOnHover(IReadOnlyDictionary<string, string> parameters)
        {
            return !parameters.TryGetValue(DashboardParameterIds.FullOpacityOnHover, out var value) ||
                   !bool.TryParse(value, out var enabled) || enabled;
        }

        internal static DashboardStyleDefinition ResolveStyle(
            DashboardStyleDefinition style,
            IReadOnlyDictionary<string, string> parameters,
            int backgroundOpacityFallback = 100,
            float? backgroundOpacityOverride = null)
        {
            var fontSize = FontSize(parameters, style.FontSize);
            var backgroundOpacity = Math.Clamp(
                backgroundOpacityOverride ?? BackgroundOpacity(parameters, backgroundOpacityFallback), 0f, 1f);
            var rowHeight = Layout(parameters) switch
            {
                DashboardParameterValues.SingleLine => Math.Max(28, fontSize + 14),
                _ => Math.Max(40, fontSize + 23),
            };
            return new()
            {
                Id = style.Id,
                Name = style.Name,
                BackgroundColor = ScaleAlpha(style.BackgroundColor, backgroundOpacity),
                HeaderColor = ScaleAlpha(style.HeaderColor, backgroundOpacity),
                SurfaceColor = ScaleAlpha(style.SurfaceColor, backgroundOpacity),
                TrackColor = ScaleAlpha(style.TrackColor, backgroundOpacity),
                BorderColor = style.BorderColor,
                TextColor = style.TextColor,
                SecondaryTextColor = style.SecondaryTextColor,
                PositiveColor = style.PositiveColor,
                NegativeColor = style.NegativeColor,
                WarningColor = style.WarningColor,
                AccentColors = style.AccentColors,
                RowHeight = rowHeight,
                FontSize = fontSize,
            };
        }

        private static int Percentage(
            IReadOnlyDictionary<string, string> parameters,
            string key,
            int fallback,
            int minimum)
        {
            return parameters.TryGetValue(key, out var value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percentage)
                ? Math.Clamp(percentage, minimum, 100)
                : Math.Clamp(fallback, minimum, 100);
        }

        private static string ScaleAlpha(string value, float factor)
        {
            try
            {
                var color = new Color(value);
                color.A *= factor;
                return color.ToHtml();
            }
            catch
            {
                return value;
            }
        }

        private static string Layout(IReadOnlyDictionary<string, string> parameters)
        {
            return NormalizeLayout(parameters.GetValueOrDefault(DashboardParameterIds.Layout));
        }

        internal static string NormalizeLayout(string? value)
        {
            return value == DashboardParameterValues.SingleLine
                ? DashboardParameterValues.SingleLine
                : DashboardParameterValues.Standard;
        }

        internal static Dictionary<string, string> MergeSharedParameters(
            IReadOnlyDictionary<string, string> current,
            IReadOnlyDictionary<string, string> dashboard)
        {
            var result = new Dictionary<string, string>(dashboard, StringComparer.Ordinal);
            Copy(DashboardParameterIds.FontSize);
            Copy(DashboardParameterIds.Layout);
            Copy(DashboardParameterIds.SummonDisplay);
            Copy(DashboardParameterIds.WindowOpacity);
            Copy(DashboardParameterIds.BackgroundOpacity);
            Copy(DashboardParameterIds.FullOpacityOnHover);
            return result;

            void Copy(string key)
            {
                if (current.TryGetValue(key, out var value))
                    result[key] = value;
            }
        }
    }
}
