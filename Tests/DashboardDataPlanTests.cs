// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardDataPlanTests
    {
        [Fact]
        public void ManyMetricWindowsStillRequestOnlyMetricData()
        {
            var consumers = Enumerable.Range(0, 50).Select(_ => (
                DashboardDataScope.CurrentCombat,
                new DashboardDataRequirements(DashboardDataComponents.Metrics)));

            var plan = DashboardDataPlan.Create(consumers);

            Assert.Equal(DashboardDataComponents.Metrics, plan.Components);
            Assert.False(plan.NeedsRunAggregate);
            Assert.Null(plan.MetricIds);
        }

        [Fact]
        public void CurrentRunRequestsOneSharedAggregateWithoutTimeline()
        {
            var plan = DashboardDataPlan.Create(
            [
                (DashboardDataScope.CurrentRun,
                    new(DashboardDataComponents.Metrics)),
                (DashboardDataScope.CurrentRun,
                    new(DashboardDataComponents.Metrics)),
            ]);

            Assert.Equal(DashboardDataComponents.Metrics, plan.Components);
            Assert.True(plan.NeedsRunAggregate);
        }

        [Fact]
        public void TimelineIsIncludedOnlyWhenAVisibleConsumerRequestsIt()
        {
            var metricOnly = DashboardDataPlan.Create(
            [
                (DashboardDataScope.CurrentCombat,
                    new(DashboardDataComponents.Metrics)),
            ]);
            var withTimeline = DashboardDataPlan.Create(
            [
                (DashboardDataScope.CurrentCombat,
                    new(DashboardDataComponents.Metrics)),
                (DashboardDataScope.CurrentCombat,
                    new(DashboardDataComponents.Timeline)),
            ]);

            Assert.False(metricOnly.Components.HasFlag(DashboardDataComponents.Timeline));
            Assert.True(withTimeline.Components.HasFlag(DashboardDataComponents.Timeline));
        }

        [Fact]
        public void MetricSelectionsAreUnionedAcrossWindows()
        {
            var plan = DashboardDataPlan.Create(
            [
                (DashboardDataScope.CurrentCombat,
                    new(DashboardDataComponents.Metrics, ["damage"])),
                (DashboardDataScope.CurrentCombat,
                    new(DashboardDataComponents.Metrics, ["block"])),
            ]);

            Assert.Equal(["block", "damage"], plan.MetricIds!.Order(StringComparer.Ordinal));
        }
    }
}
