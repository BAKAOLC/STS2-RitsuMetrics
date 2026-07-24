// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardInvalidationTests
    {
        [Fact]
        public void MetricChangeOnlyInvalidatesInterestedMetric()
        {
            var change = new MetricsChange(MetricsChangeKind.Metrics, "ritsumetrics.damage");
            var matching = new DashboardDataRequirements(DashboardDataComponents.Metrics,
                ["ritsumetrics.damage"]);
            var unrelated = new DashboardDataRequirements(DashboardDataComponents.Metrics,
                ["ritsumetrics.block"]);

            Assert.True(change.Affects(matching));
            Assert.False(change.Affects(unrelated));
        }

        [Fact]
        public void CoalescedMetricChangesKeepTheirExactMetricSet()
        {
            var change = new MetricsChange(MetricsChangeKind.Metrics, "ritsumetrics.damage")
                .Merge(new(MetricsChangeKind.Metrics, "ritsumetrics.block"));

            Assert.True(change.Affects(Requirements("ritsumetrics.damage")));
            Assert.True(change.Affects(Requirements("ritsumetrics.block")));
            Assert.False(change.Affects(Requirements("ritsumetrics.cards")));
        }

        [Fact]
        public void TimelineChangeDoesNotInvalidateMetricOnlyDashboard()
        {
            var change = new MetricsChange(MetricsChangeKind.Timeline);

            Assert.False(change.Affects(Requirements("ritsumetrics.damage")));
            Assert.True(change.Affects(new(DashboardDataComponents.Timeline)));
        }

        [Fact]
        public void ObservationEventOnlyInvalidatesMatchingSplitMetric()
        {
            var change = new MetricsChange(MetricsChangeKind.Metrics | MetricsChangeKind.Events, "damage");
            var damage = new DashboardDataRequirements(
                DashboardDataComponents.Metrics | DashboardDataComponents.Events, ["damage"]);
            var block = new DashboardDataRequirements(
                DashboardDataComponents.Metrics | DashboardDataComponents.Events, ["block"]);

            Assert.True(change.Affects(damage));
            Assert.False(change.Affects(block));
        }

        [Fact]
        public void RunStructureChangeInvalidatesEveryDashboardShape()
        {
            var change = new MetricsChange(MetricsChangeKind.RunStructure);

            Assert.True(change.Affects(new(DashboardDataComponents.Metrics)));
            Assert.True(change.Affects(new(DashboardDataComponents.Timeline)));
        }

        private static DashboardDataRequirements Requirements(string metricId)
        {
            return new(DashboardDataComponents.Metrics, [metricId]);
        }
    }
}
