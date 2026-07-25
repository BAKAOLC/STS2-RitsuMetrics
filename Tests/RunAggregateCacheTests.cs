// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Tests
{
    public sealed class RunAggregateCacheTests
    {
        [Fact]
        public void ActiveCombatChangesAreCombinedWithCachedCompletedCombats()
        {
            var cache = new RunAggregateCache();
            var completed = Combat("completed", true, 10m, 2);
            var firstRun = Run(completed, Combat("active", false, 3m, 1));
            var updatedRun = Run(Combat("active", false, 8m, 3));

            var first = cache.Combine(firstRun, DashboardDataComponents.Metrics, null, "damage", true);
            var updated = cache.Combine(updatedRun, DashboardDataComponents.Metrics, null, "damage", false);

            Assert.Equal(13m, Total(first));
            Assert.Equal(18m, Total(updated));
            Assert.Equal(5, updated!.RoundCount);
            Assert.False(cache.RequiresCompletedCombats(updatedRun, DashboardDataComponents.Metrics, "damage",
                false));
        }

        [Fact]
        public void NewlyCompletedCombatIsIncludedInFollowingActAggregate()
        {
            var cache = new RunAggregateCache();
            var firstCombat = Combat("first", true, 10m, 2);
            _ = cache.Combine(Run(firstCombat, Combat("second", false, 3m, 1)),
                DashboardDataComponents.Metrics, null, "damage", true);

            var nextAct = cache.Combine(
                Run(firstCombat, Combat("second", true, 3m, 1), Combat("third", false, 7m, 2)),
                DashboardDataComponents.Metrics, null, "damage", true);

            Assert.Equal(20m, Total(nextAct));
            Assert.Equal(5, nextAct!.RoundCount);
        }

        [Fact]
        public void CompletedCombatCacheProjectsOnlyRequestedMetrics()
        {
            var cache = new RunAggregateCache();
            var combat = Combat("completed", true, 10m, 2);
            var player = combat.Players.Single();
            combat = combat with
            {
                Players =
                [
                    player with
                    {
                        Totals = new Dictionary<string, decimal>(player.Totals, StringComparer.Ordinal)
                        {
                            ["block"] = 99m,
                        },
                    },
                ],
            };

            var result = cache.Combine(Run(combat), DashboardDataComponents.Metrics,
                new HashSet<string>(["damage"], StringComparer.Ordinal), "damage", true);

            Assert.Equal(10m, Total(result));
            Assert.False(result!.Players.Single().Totals.ContainsKey("block"));
        }

        [Fact]
        public void AggregateSnapshotPathReusesCompletedCombatSnapshots()
        {
            var session = MutableRunSession.Restore(Run(Combat("completed", true, 10m, 2)));

            var first = session.SnapshotForLiveView(false, false, true, false, null);
            var second = session.SnapshotForLiveView(false, false, true, false, null);
            var projected = session.SnapshotForLiveView(false, false, true, true, null);

            Assert.Same(first.Combats.Single(), second.Combats.Single());
            Assert.NotSame(first.Combats.Single(), projected.Combats.Single());
        }

        [Fact]
        public void AggregateHistoryUsesSegmentedViewsInsteadOfCopyingCompletedHistory()
        {
            var cache = new RunAggregateCache();
            var completedEvent = Observation(1, "completed");
            var activeEvent = Observation(2, "active");
            var completed = Combat("completed", true, 10m, 2) with { Events = [completedEvent] };
            var active = Combat("active", false, 3m, 1) with { Events = [activeEvent] };

            var result = cache.Combine(Run(completed, active),
                DashboardDataComponents.Metrics | DashboardDataComponents.Events,
                null, "*", true);

            Assert.IsType<CompositeReadOnlyList<MetricObservation>>(result!.Events);
            Assert.Equal([completedEvent, activeEvent], result.Events);
        }

        private static decimal Total(CombatSnapshot? snapshot)
        {
            return snapshot!.Players.Single().Totals["damage"];
        }

        private static RunSnapshot Run(params CombatSnapshot[] combats)
        {
            return new("run", DateTimeOffset.UnixEpoch, null, true, false, null, null, combats);
        }

        private static CombatSnapshot Combat(string id, bool completed, decimal damage, int rounds)
        {
            var startedAt = DateTimeOffset.UnixEpoch.AddMinutes(id.Length);
            return new(
                "run",
                id,
                0,
                1,
                id,
                id,
                startedAt,
                completed ? startedAt.AddMinutes(1) : null,
                completed,
                rounds,
                [
                    new("player", 1, "Player", "CHARACTER.IRONCLAD",
                        new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["damage"] = damage,
                        },
                        new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal)),
                ],
                []);
        }

        private static MetricObservation Observation(long sequence, string sourceKey)
        {
            var player = new EntityDescriptor("player", AnalyticsEntityKind.Player, 1, "character", "Player",
                "character");
            return new(sequence, "run", "combat", 0, 1, 1, DateTimeOffset.UnixEpoch, "damage", sequence,
                player, null, new(sourceKey, AnalyticsSourceKind.Card, sourceKey, sourceKey),
                MetricObservation.EmptyTags);
        }
    }
}
