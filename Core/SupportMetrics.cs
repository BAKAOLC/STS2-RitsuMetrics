// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class SupportMetrics
    {
        internal static bool IsAlly(EntityDescriptor contributor, EntityDescriptor receiver)
        {
            return contributor.Kind == AnalyticsEntityKind.Player && receiver.Kind == AnalyticsEntityKind.Player &&
                   !string.Equals(contributor.Key, receiver.Key, StringComparison.Ordinal);
        }

        internal static string BlockMetric(EntityDescriptor contributor, EntityDescriptor receiver)
        {
            return IsAlly(contributor, receiver) ? MetricIds.AllyBlockProvided : MetricIds.SelfBlockProvided;
        }
    }
}
