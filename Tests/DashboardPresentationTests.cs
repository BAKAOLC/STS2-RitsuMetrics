// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DashboardPresentationTests
    {
        [Fact]
        public void FloatingWindowKeepsItsHistoryLimit()
        {
            Assert.Equal(200, DashboardPresentation.HistoryItemLimit(
                new Dictionary<string, string>(StringComparer.Ordinal),
                200));
        }

        [Fact]
        public void AnalysisCenterDoesNotTruncateRecordedHistory()
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DashboardPresentation.HostParameter] = DashboardPresentation.AnalysisCenterHost,
            };

            Assert.Equal(int.MaxValue, DashboardPresentation.HistoryItemLimit(parameters, 200));
        }

        [Fact]
        public void MinimumGridWidthIncludesColumnsGapsAndContainerPadding()
        {
            Assert.Equal(481f, DashboardPresentation.MinimumGridWidth(4, 112f, 5f, 9f));
        }

        [Fact]
        public void ReceivedDamageHistoryOnlyIncludesSettledHpLossOrBlock()
        {
            var target = new EntityDescriptor("player", AnalyticsEntityKind.Player, 1, "character", "Player");
            var death = TimelineEvent(CombatTimelineKind.Death, "death.end", target);
            var administrativeRemoval = TimelineEvent(CombatTimelineKind.HpLoss, "hp.removed_on_death", target);
            var blocked = TimelineEvent(CombatTimelineKind.Damage, "damage", target,
                new(7m, 7m, 7m, 0m, 0m, 7m, string.Empty, []));

            Assert.False(ReceivedDamageRenderer.IsReceivedDamageEvent(death));
            Assert.False(ReceivedDamageRenderer.IsReceivedDamageEvent(administrativeRemoval));
            Assert.True(ReceivedDamageRenderer.IsReceivedDamageEvent(blocked));
        }

        private static CombatTimelineEvent TimelineEvent(
            CombatTimelineKind kind,
            string actionId,
            EntityDescriptor target,
            DamageBreakdown? damage = null)
        {
            return new(
                1,
                "event",
                null,
                "run",
                "combat",
                DateTimeOffset.UnixEpoch,
                1,
                1,
                TimelineTurnSide.Player,
                false,
                kind,
                TimelineEventPhase.Instant,
                actionId,
                string.Empty,
                null,
                target,
                null,
                null,
                MetricObservation.EmptyTags,
                damage);
        }
    }
}
