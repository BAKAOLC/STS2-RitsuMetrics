// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class AnalysisSnapshotSelectorTests
    {
        [Fact]
        public void CurrentCombatDropsUnselectedCombatPayloads()
        {
            var run = Run(Combat("first"), Combat("selected"), Combat("last"));

            var selected = AnalysisSnapshotSelector.Select(
                run,
                DashboardDataScope.CurrentCombat,
                DashboardDataComponents.Metrics | DashboardDataComponents.Timeline,
                "selected");

            Assert.Equal("selected", Assert.Single(selected.Combats).CombatId);
        }

        [Fact]
        public void RunCombatDashboardKeepsAllCombats()
        {
            var run = Run(Combat("first"), Combat("selected"));

            var selected = AnalysisSnapshotSelector.Select(
                run,
                DashboardDataScope.CurrentCombat,
                DashboardDataComponents.Metrics | DashboardDataComponents.RunCombats,
                "selected");

            Assert.Same(run, selected);
        }

        [Fact]
        public void WholeRunKeepsAllCombats()
        {
            var run = Run(Combat("first"), Combat("last"));

            var selected = AnalysisSnapshotSelector.Select(
                run,
                DashboardDataScope.CurrentRun,
                DashboardDataComponents.Metrics,
                "last");

            Assert.Same(run, selected);
        }

        [Fact]
        public void SummaryDropsHeavyPayloadsAndUnneededMetrics()
        {
            var player = new PlayerMetricSnapshot(
                "player",
                1,
                "Player",
                "character",
                new Dictionary<string, decimal>
                {
                    [MetricIds.DamageDealt] = 12m,
                    [MetricIds.DamageBlocked] = 7m,
                },
                new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>
                {
                    [MetricIds.DamageDealt] =
                    [
                        new("source", AnalyticsSourceKind.Card, "card", "Card", 12m, 1),
                    ],
                });
            var combat = Combat("combat") with
            {
                Players = [player],
                Events =
                [
                    new(
                        1,
                        "run",
                        "combat",
                        0,
                        1,
                        1,
                        DateTimeOffset.UnixEpoch,
                        MetricIds.DamageDealt,
                        12m,
                        new("player", AnalyticsEntityKind.Player, 1, "character", "Player", "character"),
                        null,
                        new("source", AnalyticsSourceKind.Card, "card", "Card"),
                        MetricObservation.EmptyTags),
                ],
                Timeline =
                [
                    new(
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
                        CombatTimelineKind.Damage,
                        TimelineEventPhase.Completed,
                        "damage",
                        "Damage",
                        null,
                        null,
                        null,
                        null,
                        MetricObservation.EmptyTags),
                ],
            };

            var summary = AnalysisSnapshotSelector.Summarize(Run(combat));
            var summarizedCombat = Assert.Single(summary.Combats);
            var summarizedPlayer = Assert.Single(summarizedCombat.Players);

            Assert.Empty(summarizedCombat.Events);
            Assert.Empty(summarizedCombat.Timeline!);
            Assert.Equal(12m, Assert.Single(summarizedPlayer.Totals).Value);
            Assert.Empty(summarizedPlayer.Sources);
        }

        private static RunSnapshot Run(params CombatSnapshot[] combats)
        {
            return new("run", DateTimeOffset.UnixEpoch, null, false, false, null, null, combats);
        }

        private static CombatSnapshot Combat(string id)
        {
            return new(
                "run",
                id,
                0,
                1,
                "encounter",
                "Encounter",
                DateTimeOffset.UnixEpoch,
                null,
                false,
                1,
                [],
                [],
                []);
        }
    }
}
