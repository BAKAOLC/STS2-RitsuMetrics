// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Tests
{
    public sealed class SnapshotStructuralSharingTests
    {
        [Fact]
        public void AppendOnlyBufferCopiesOnlyOpenChunkAndKeepsSnapshotsImmutable()
        {
            var buffer = new AppendOnlySnapshotBuffer<int>();
            buffer.AddRange(Enumerable.Range(0, 300));
            var first = buffer.GetSnapshot();

            buffer.Add(300);
            var second = buffer.GetSnapshot();

            Assert.Equal(300, first.Count);
            Assert.Equal(Enumerable.Range(0, 300), first);
            Assert.Equal(301, second.Count);
            Assert.Equal(300, second[^1]);
            Assert.Same(second, buffer.GetSnapshot());
        }

        [Fact]
        public void AppendingToLargeBufferHasBoundedSnapshotAllocation()
        {
            var buffer = new AppendOnlySnapshotBuffer<int>();
            buffer.AddRange(Enumerable.Range(0, 100_000));
            _ = buffer.GetSnapshot();
            buffer.Add(100_000);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var snapshot = buffer.GetSnapshot();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(100_001, snapshot.Count);
            Assert.InRange(allocated, 0, 32_768);
        }

        [Fact]
        public void MetricSliceIgnoresUnrelatedMetricAndTimelineChanges()
        {
            var combat = Combat();
            combat.Add(Observation("damage", 10m), 1000);
            var damage = new HashSet<string>(["damage"], StringComparer.Ordinal);
            var first = combat.Snapshot(false, false, damage);

            combat.Add(Observation("block", 7m), 1000);
            combat.AddTimeline(Timeline(), 1000);
            var unchanged = combat.Snapshot(false, false, damage);

            Assert.Same(first, unchanged);

            combat.Add(Observation("damage", 3m), 1000);
            var changed = combat.Snapshot(false, false, damage);

            Assert.NotSame(first, changed);
            Assert.Equal(13m, changed.Players.Single().Totals["damage"]);
        }

        [Fact]
        public void TimelineSliceReusesUnchangedPlayerMetricObjects()
        {
            var combat = Combat();
            combat.Add(Observation("damage", 10m), 1000);
            var damage = new HashSet<string>(["damage"], StringComparer.Ordinal);
            var first = combat.Snapshot(false, true, damage);

            combat.AddTimeline(Timeline(), 1000);
            var changed = combat.Snapshot(false, true, damage);

            Assert.NotSame(first, changed);
            Assert.Same(first.Players.Single(), changed.Players.Single());
            Assert.Single(changed.Timeline!);
        }

        [Fact]
        public void LiveViewCanSelectOneCompletedCombatWithoutIncludingTheRest()
        {
            var first = Combat("first");
            var selected = Combat("selected");
            var active = Combat("active");
            var run = new MutableRunSession
            {
                RunId = "run",
                StartedAtUtc = DateTimeOffset.UnixEpoch,
            };
            run.SetActiveCombat(first);
            _ = run.CompleteActiveCombat(DateTimeOffset.UnixEpoch.AddMinutes(1));
            run.SetActiveCombat(selected);
            _ = run.CompleteActiveCombat(DateTimeOffset.UnixEpoch.AddMinutes(2));
            run.SetActiveCombat(active);

            var snapshot = run.SnapshotForLiveView(
                false,
                false,
                false,
                true,
                null,
                "selected");

            Assert.Equal("selected", Assert.Single(snapshot.Combats).CombatId);
        }

        private static MutableCombatSession Combat()
        {
            return Combat("combat");
        }

        private static MutableCombatSession Combat(string combatId)
        {
            return new()
            {
                RunId = "run",
                CombatId = combatId,
                ActIndex = 0,
                Floor = 1,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
            };
        }

        private static MetricObservation Observation(string metricId, decimal value)
        {
            var player = new EntityDescriptor(
                "player",
                AnalyticsEntityKind.Player,
                1,
                "CHARACTER.IRONCLAD",
                "Player",
                "CHARACTER.IRONCLAD");
            return new(
                1,
                "run",
                "combat",
                0,
                1,
                1,
                DateTimeOffset.UnixEpoch,
                metricId,
                value,
                player,
                null,
                new($"source:{metricId}", AnalyticsSourceKind.Card, metricId, metricId),
                MetricObservation.EmptyTags);
        }

        private static CombatTimelineEvent Timeline()
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
                CombatTimelineKind.CardPlay,
                TimelineEventPhase.Completed,
                "card.play",
                "Card",
                null,
                null,
                null,
                null,
                MetricObservation.EmptyTags);
        }
    }
}
