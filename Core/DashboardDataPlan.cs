// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal readonly record struct DashboardDataPlan(
        DashboardDataComponents Components,
        bool NeedsRunAggregate,
        IReadOnlySet<string>? MetricIds)
    {
        internal string MetricSelectionKey => MetricIds == null
            ? "*"
            : string.Join('\u001f', MetricIds.Order(StringComparer.Ordinal));

        internal static DashboardDataPlan Create(
            IEnumerable<(DashboardDataScope Scope, DashboardDataRequirements Requirements)> consumers,
            bool includeAnalysisCenter = false)
        {
            var components = includeAnalysisCenter
                ? DashboardDataComponents.All
                : DashboardDataComponents.None;
            var needsRunAggregate = false;
            var allMetrics = includeAnalysisCenter;
            var metricIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (scope, requirements) in consumers)
            {
                components |= requirements.Components;
                needsRunAggregate |= scope == DashboardDataScope.CurrentRun;
                if (!requirements.Components.HasFlag(DashboardDataComponents.Metrics))
                    continue;
                if (requirements.MetricIds is not { Count: > 0 } requestedMetricIds)
                    allMetrics = true;
                else if (!allMetrics)
                    metricIds.UnionWith(requestedMetricIds);
            }

            return new(components, needsRunAggregate, allMetrics ? null : metricIds);
        }
    }
}
