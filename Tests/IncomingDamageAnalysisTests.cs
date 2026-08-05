// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class IncomingDamageAnalysisTests
    {
        private static readonly EntityDescriptor Player =
            new("player:1", AnalyticsEntityKind.Player, 1, "character", "Player", "character");

        [Fact]
        public void SummarizesHpLossBlockSelfDamageAndSources()
        {
            var enemy = new EntityDescriptor("monster", AnalyticsEntityKind.Monster, null, "monster", "Enemy");
            var slash = new SourceDescriptor("move:slash", AnalyticsSourceKind.Creature, "slash", "Slash");
            var bloodPrice = new SourceDescriptor("card:blood-price", AnalyticsSourceKind.Card, "blood-price",
                "Blood Price");
            var player = PlayerSnapshot(10m, 15m, 20m);
            var snapshot = Combat(player,
                BlockEvent(1, "combat", 20m),
                DamageEvent(2, "combat", enemy, slash, 7m, 15m),
                DamageEvent(3, "combat", Player, bloodPrice, 3m, 0m));

            var analysis = IncomingDamageAnalysis.Create(snapshot, player);

            Assert.Equal(10m, analysis.HpLost);
            Assert.Equal(15m, analysis.EffectiveBlock);
            Assert.Equal(20m, analysis.BlockGained);
            Assert.Equal(5m, analysis.UnspentBlock);
            Assert.Equal(0.4m, analysis.HpLossRatio);
            Assert.Equal(3m, analysis.SelfHpLost);
            Assert.Equal(0.3m, analysis.SelfHpLossRatio);
            Assert.True(analysis.HasCompleteHpTimeline);
            Assert.Collection(analysis.Sources,
                source =>
                {
                    Assert.Equal("Slash", source.Name);
                    Assert.Equal(7m, source.HpLost);
                    Assert.Equal(0.7m, source.Share);
                    Assert.Equal(1, source.Occurrences);
                },
                source =>
                {
                    Assert.Equal("Blood Price", source.Name);
                    Assert.Equal(3m, source.HpLost);
                    Assert.Equal(0.3m, source.Share);
                    Assert.Equal(1, source.Occurrences);
                });
        }

        [Fact]
        public void FallsBackToAggregateSourcesWhenTimelineIsUnavailable()
        {
            var player = PlayerSnapshot(10m, 4m, 9m) with
            {
                Sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal)
                {
                    [MetricIds.DamageTaken] =
                    [
                        new("power:burn", AnalyticsSourceKind.Power, "burn", "Burn", 6m, 2),
                        new("monster", AnalyticsSourceKind.Creature, "monster", "Enemy", 4m, 1),
                    ],
                },
            };

            var analysis = IncomingDamageAnalysis.Create(Combat(player), player);

            Assert.False(analysis.HasCompleteHpTimeline);
            Assert.Equal(5m, analysis.UnspentBlock);
            Assert.Equal(0m, analysis.SelfHpLost);
            Assert.Collection(analysis.Sources,
                source =>
                {
                    Assert.Equal("Burn", source.Name);
                    Assert.Equal(0.6m, source.Share);
                    Assert.Equal(2, source.Occurrences);
                },
                source =>
                {
                    Assert.Equal("Enemy", source.Name);
                    Assert.Equal(0.4m, source.Share);
                    Assert.Equal(1, source.Occurrences);
                });
        }

        [Fact]
        public void OwnedSummonDamageIsNotClassifiedAsSelfDamage()
        {
            var summon = new EntityDescriptor("summon", AnalyticsEntityKind.Summon, 1, "summon", "Summon");
            var source = new SourceDescriptor("creature:summon", AnalyticsSourceKind.Creature, "summon", "Summon");
            var player = PlayerSnapshot(4m, 0m, 0m);
            var snapshot = Combat(player, DamageEvent(1, "combat", summon, source, 4m, 0m));

            var analysis = IncomingDamageAnalysis.Create(snapshot, player);

            Assert.True(analysis.HasCompleteHpTimeline);
            Assert.Equal(0m, analysis.SelfHpLost);
        }

        [Fact]
        public void CachedSnapshotAnalysisReusesIncomingAndSummarizesOutcomeQuality()
        {
            var totals = PlayerSnapshot(10m, 15m, 20m).Totals.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal);
            totals[MetricIds.DamageDealt] = 100m;
            totals[MetricIds.EffectiveHpDamageDealt] = 80m;
            totals[MetricIds.Overkill] = 10m;
            totals[MetricIds.HealingReceived] = 5m;
            totals[MetricIds.CardsPlayed] = 6m;
            totals[MetricIds.EnergySpent] = 3m;
            var player = PlayerSnapshot(10m, 15m, 20m) with { Totals = totals };
            var snapshot = Combat(player, BlockEvent(1, "combat", 20m),
                DamageEvent(2, "combat",
                    new("monster", AnalyticsEntityKind.Monster, null, "monster", "Enemy"),
                    new("move:slash", AnalyticsSourceKind.Creature, "slash", "Slash"), 10m, 15m));

            var first = SnapshotAnalysisCache.Get(snapshot);
            var second = SnapshotAnalysisCache.Get(snapshot);

            Assert.Same(first, second);
            Assert.Same(first.Incoming(player), second.Incoming(player));
            Assert.Equal(20m, first.EnemyBlockedDamage);
            Assert.Equal(110m, first.DamageOutcomeTotal);
            Assert.Equal(80m / 110m, first.DamageConversion);
            Assert.Equal(0.4m, first.HpLossRatio);
            Assert.Equal(0.75m, first.BlockEfficiency);
            Assert.Equal(5m, first.UnspentBlock);
            Assert.Equal(5m, first.Healing);
            Assert.Equal(6m, first.CardsPlayed);
            Assert.Equal(3m, first.EnergySpent);
        }

        [Fact]
        public void RunAggregatePreservesDetailedDataDropCounts()
        {
            var player = PlayerSnapshot(0m, 0m, 0m);
            var first = Combat(player) with
            {
                CombatId = "combat:1",
                DroppedObservationCount = 3,
                DroppedTimelineEventCount = 5,
            };
            var second = Combat(player) with
            {
                CombatId = "combat:2",
                DroppedObservationCount = 7,
                DroppedTimelineEventCount = 11,
            };
            var run = new RunSnapshot("run", DateTimeOffset.UnixEpoch, null, false, false, null, null,
                [first, second]);

            var aggregate = SnapshotAggregator.Combine(run);

            Assert.NotNull(aggregate);
            Assert.True(aggregate.HasIncompleteDetails);
            Assert.Equal(10, aggregate.DroppedObservationCount);
            Assert.Equal(16, aggregate.DroppedTimelineEventCount);
        }

        [Fact]
        public void CachedTurnsKeepEqualTurnIndexesFromSeparateCombatsDistinct()
        {
            var player = PlayerSnapshot(0m, 0m, 1m);
            var first = Combat(player, BlockEvent(1, "combat:1", 1m)) with { CombatId = "combat:1" };
            var second = Combat(player, BlockEvent(2, "combat:2", 1m)) with { CombatId = "combat:2" };
            var run = new RunSnapshot("run", DateTimeOffset.UnixEpoch, null, false, false, null, null,
                [first, second]);
            var aggregate = SnapshotAggregator.Combine(run);

            var turns = SnapshotAnalysisCache.Get(Assert.IsType<CombatSnapshot>(aggregate)).Turns;

            Assert.Equal(2, turns.Count);
            Assert.Equal(["combat:1", "combat:2"], turns.Select(turn => turn.CombatId));
            Assert.All(turns, turn => Assert.Equal(1, turn.Index));
        }

        private static PlayerMetricSnapshot PlayerSnapshot(decimal hpLost, decimal blocked, decimal blockGained)
        {
            return new(
                Player.Key,
                Player.PlayerNetId,
                Player.DisplayName,
                Player.CharacterId,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [MetricIds.DamageTaken] = hpLost,
                    [MetricIds.DamageBlocked] = blocked,
                    [MetricIds.BlockGained] = blockGained,
                },
                new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal));
        }

        private static CombatSnapshot Combat(
            PlayerMetricSnapshot player,
            params CombatTimelineEvent[] timeline)
        {
            return new(
                "run",
                "combat",
                0,
                1,
                "encounter",
                "Encounter",
                DateTimeOffset.UnixEpoch,
                null,
                false,
                1,
                [player],
                [],
                timeline);
        }

        private static CombatTimelineEvent BlockEvent(long sequence, string combatId, decimal amount)
        {
            return TimelineEvent(sequence, combatId, CombatTimelineKind.Block, "block.gain", Player,
                new("card:defend", AnalyticsSourceKind.Card, "defend", "Defend"), amount);
        }

        private static CombatTimelineEvent DamageEvent(
            long sequence,
            string combatId,
            EntityDescriptor actor,
            SourceDescriptor source,
            decimal hpLost,
            decimal blocked)
        {
            var damage = new DamageBreakdown(
                hpLost + blocked,
                hpLost + blocked,
                blocked,
                hpLost,
                0m,
                hpLost + blocked,
                string.Empty,
                []);
            return TimelineEvent(sequence, combatId, CombatTimelineKind.Damage, "damage", actor, source,
                hpLost + blocked, damage);
        }

        private static CombatTimelineEvent TimelineEvent(
            long sequence,
            string combatId,
            CombatTimelineKind kind,
            string actionId,
            EntityDescriptor actor,
            SourceDescriptor source,
            decimal value,
            DamageBreakdown? damage = null)
        {
            return new(
                sequence,
                $"event:{sequence}",
                null,
                "run",
                combatId,
                DateTimeOffset.UnixEpoch,
                1,
                1,
                TimelineTurnSide.Player,
                false,
                kind,
                TimelineEventPhase.Instant,
                actionId,
                source.DisplayName,
                actor,
                Player,
                source,
                value,
                MetricObservation.EmptyTags,
                damage);
        }
    }
}
