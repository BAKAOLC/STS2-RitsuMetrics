// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class MetricAvailability
    {
        internal static readonly string[] BuiltIns = typeof(MetricIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();

        internal static IEnumerable<string> Known(PlayerMetricSnapshot player)
        {
            return player.AvailableMetrics ?? player.Totals.Keys;
        }

        internal static decimal? Value(PlayerMetricSnapshot player, string metricId)
        {
            return Known(player).Contains(metricId, StringComparer.Ordinal)
                ? player.Totals.GetValueOrDefault(metricId)
                : null;
        }

        internal static decimal? Total(IEnumerable<decimal?> values)
        {
            decimal total = 0m;
            var any = false;
            foreach (var value in values)
            {
                if (value == null)
                    return null;
                total += value.Value;
                any = true;
            }
            return any ? total : null;
        }
    }
}
