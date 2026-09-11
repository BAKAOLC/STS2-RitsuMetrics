// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Domain;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class MetricAvailabilityTests
    {
        [Fact]
        public void DefaultOverviewPreservesOriginalTwelveMetricsAndOrder()
        {
            string[] expected =
            [
                MetricIds.DamageDealt, MetricIds.BlockGained, MetricIds.DamageContribution, "overview.damage_share",
                "overview.damage_per_turn", "overview.effective_block", MetricIds.Deaths, "overview.hp_loss_ratio",
                MetricIds.HealingReceived, MetricIds.CardsPlayed, "overview.damage_per_energy", MetricIds.Overkill,
            ];
            var available = MetricAvailability.BuiltIns.Concat(OverviewMetricSelection.Derived.Select(item => item.Id));
            Assert.Equal(expected, OverviewMetricSelection.Defaults);
            Assert.Equal(expected, OverviewMetricSelection.Normalize(null, available));
            Assert.Equal("%", OverviewMetricSelection.Suffix("overview.damage_share"));
            Assert.Equal("%", OverviewMetricSelection.Suffix("overview.hp_loss_ratio"));
        }

        [Fact]
        public void NewCaptureDistinguishesZeroFromLegacyMissingAndSurvivesSerialization()
        {
            var fresh = new MutablePlayerMetrics(Player("one")).Snapshot();
            Assert.Equal(0m, MetricAvailability.Value(fresh, MetricIds.AllyBlockProvided));
            var stored = JsonSerializer.Deserialize<PlayerMetricSnapshot>(JsonSerializer.Serialize(fresh))!;
            Assert.Equal(0m, MetricAvailability.Value(stored, MetricIds.AllyBlockProvided));
            Assert.Null(MetricAvailability.Value(stored with { AvailableMetrics = null }, MetricIds.AllyBlockProvided));
        }

        [Fact]
        public void RestoringOldCombatDoesNotTurnPartialNewMetricsIntoCompleteTotals()
        {
            var old = new MutablePlayerMetrics(Player("one")).Snapshot() with { AvailableMetrics = null };
            var restored = MutablePlayerMetrics.Restore(old);
            restored.Add(Observation(MetricIds.AllyBlockProvided, 60m));
            Assert.Equal(60m, restored.Snapshot().Totals[MetricIds.AllyBlockProvided]);
            Assert.Null(MetricAvailability.Value(restored.Snapshot(), MetricIds.AllyBlockProvided));
        }

        [Fact]
        public void RunTotalRemainsUnavailableWhenOnlySomeCombatsCapturedMetric()
        {
            var fresh = new MutablePlayerMetrics(Player("one"));
            fresh.Add(Observation(MetricIds.AllyBlockProvided, 60m));
            var old = fresh.Snapshot() with { AvailableMetrics = [], Totals = new Dictionary<string, decimal>() };
            var aggregate = SnapshotAggregator.Combine(Run(Combat("old", old), Combat("new", fresh.Snapshot())))!;
            Assert.Null(MetricAvailability.Value(aggregate.Players.Single(), MetricIds.AllyBlockProvided));
            Assert.Equal(60m, aggregate.Players.Single().Totals[MetricIds.AllyBlockProvided]);
        }

        [Fact]
        public void UnknownAttributionInvalidatesCachedSnapshotsAndSurvivesRestore()
        {
            var combat = new MutableCombatSession { RunId = "run", CombatId = "combat" };
            combat.InitializePlayer(Player("one"), "");
            var before = combat.Snapshot(false);
            combat.InvalidateMetrics(MetricIds.AllyBlockProvided);
            var after = combat.Snapshot(false);
            Assert.Equal(0m, MetricAvailability.Value(before.Players.Single(), MetricIds.AllyBlockProvided));
            Assert.Null(MetricAvailability.Value(after.Players.Single(), MetricIds.AllyBlockProvided));
            var restored = MutableCombatSession.Restore(after).Snapshot(false);
            Assert.Null(MetricAvailability.Value(restored.Players.Single(), MetricIds.AllyBlockProvided));
        }

        [Fact]
        public void ProjectionDoesNotAdvertiseMetricsWhoseValuesWereRemoved()
        {
            var metrics = new MutablePlayerMetrics(Player("one"));
            metrics.Add(Observation(MetricIds.AllyBlockProvided, 60m));
            var projected = DashboardSnapshotProjector.Project(Combat("one", metrics.Snapshot()),
                new HashSet<string> { MetricIds.DamageDealt })!;
            Assert.Null(MetricAvailability.Value(projected.Players.Single(), MetricIds.AllyBlockProvided));
            Assert.Equal(0m, MetricAvailability.Value(projected.Players.Single(), MetricIds.DamageDealt));
        }

        [Fact]
        public void UndefinedRatiosStayUnavailableWhileMeasuredZeroHasAValue()
        {
            var metrics = new MutablePlayerMetrics(Player("one"));
            var snapshot = Combat("one", metrics.Snapshot());
            Assert.Null(OverviewMetricSelection.Value(metrics.Snapshot(), snapshot, "overview.damage_per_energy"));
            metrics.Add(Observation(MetricIds.EnergySpent, 2m));
            Assert.Equal(0m, OverviewMetricSelection.Value(metrics.Snapshot(), snapshot, "overview.damage_per_energy"));
            Assert.Null(MetricAvailability.Total([5m, null]));
            Assert.Equal(5m, MetricAvailability.Total([5m, 0m]));
        }

        [Fact]
        public void SelectionPreservesOrderRemovesUnknownAndDuplicateEntriesAndBoundsDensity()
        {
            var metrics = MetricAvailability.BuiltIns.Concat(OverviewMetricSelection.Derived.Select(item => item.Id)).ToArray();
            var selection = OverviewMetricSelection.Normalize([MetricIds.AllyHealing, "missing", MetricIds.AllyHealing,
                MetricIds.DamageDealt], metrics);
            Assert.Equal([MetricIds.AllyHealing, MetricIds.DamageDealt], selection);
            Assert.Equal(16, OverviewMetricSelection.Normalize(metrics, metrics).Length);
            Assert.Equal(OverviewMetricSelection.Defaults, OverviewMetricSelection.Normalize([], metrics));
        }

        [Fact]
        public void TeamBlockIsSummedPerRecipientAndSelfBlockStaysSeparate()
        {
            var contributor = Player("one");
            var metrics = new MutablePlayerMetrics(contributor);
            foreach (var receiver in new[] { contributor, Player("two"), Player("three"), Player("four") })
                metrics.Add(Observation(SupportMetrics.BlockMetric(contributor, receiver), 20m));
            Assert.Equal(60m, MetricAvailability.Value(metrics.Snapshot(), MetricIds.AllyBlockProvided));
            Assert.Equal(20m, MetricAvailability.Value(metrics.Snapshot(), MetricIds.SelfBlockProvided));
            Assert.False(SupportMetrics.IsAlly(contributor, contributor with { Kind = AnalyticsEntityKind.Summon }));
        }

        private static EntityDescriptor Player(string key) => new(key, AnalyticsEntityKind.Player, null, "character", key);
        private static MetricObservation Observation(string metricId, decimal value) => new(1, "run", "combat", 0, 1, 1,
            DateTimeOffset.UnixEpoch, metricId, value, Player("one"), null,
            new("frost", AnalyticsSourceKind.Orb, "frost", "Frost"), MetricObservation.EmptyTags);
        private static CombatSnapshot Combat(string id, PlayerMetricSnapshot player) => new("run", id, 0, 1, "test", "Test",
            DateTimeOffset.UnixEpoch, null, true, 1, [player], []);
        private static RunSnapshot Run(params CombatSnapshot[] combats) => new("run", DateTimeOffset.UnixEpoch,
            null, true, false, null, null, combats);
    }
}
