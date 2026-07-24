// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    [Flags]
    internal enum MetricsChangeKind
    {
        None = 0,
        Metrics = 1 << 0,
        Events = 1 << 1,
        Timeline = 1 << 2,
        RunStructure = 1 << 3,
        Definitions = 1 << 4,
        All = Metrics | Events | Timeline | RunStructure | Definitions,
    }

    internal readonly record struct MetricsChange(
        MetricsChangeKind Kind,
        IReadOnlySet<string>? MetricIds = null)
    {
        internal MetricsChange(MetricsChangeKind kind, string metricId)
            : this(kind, new HashSet<string>([metricId], StringComparer.Ordinal))
        {
        }

        internal static MetricsChange All { get; } = new(MetricsChangeKind.All);

        internal MetricsChange Merge(MetricsChange other)
        {
            var thisHasMetrics = (Kind & MetricsChangeKind.Metrics) != 0;
            var otherHasMetrics = (other.Kind & MetricsChangeKind.Metrics) != 0;
            IReadOnlySet<string>? metricIds;
            if (!thisHasMetrics)
                metricIds = other.MetricIds;
            else if (!otherHasMetrics)
                metricIds = MetricIds;
            else if (MetricIds == null || other.MetricIds == null)
                metricIds = null;
            else
                metricIds = new HashSet<string>(MetricIds.Concat(other.MetricIds), StringComparer.Ordinal);
            return new(Kind | other.Kind, metricIds);
        }

        internal bool Affects(DashboardDataRequirements requirements)
        {
            if ((Kind & (MetricsChangeKind.RunStructure | MetricsChangeKind.Definitions)) != 0)
                return true;
            if ((Kind & MetricsChangeKind.Metrics) != 0 &&
                requirements.Components.HasFlag(DashboardDataComponents.Metrics) &&
                MatchesMetricSelection(requirements))
                return true;
            if ((Kind & MetricsChangeKind.Events) != 0 &&
                requirements.Components.HasFlag(DashboardDataComponents.Events) &&
                MatchesMetricSelection(requirements))
                return true;
            return (Kind & MetricsChangeKind.Timeline) != 0 &&
                   requirements.Components.HasFlag(DashboardDataComponents.Timeline);
        }

        private bool MatchesMetricSelection(DashboardDataRequirements requirements)
        {
            return MetricIds == null || requirements.MetricIds is not { Count: > 0 } metricIds ||
                   metricIds.Any(MetricIds.Contains);
        }
    }
}
