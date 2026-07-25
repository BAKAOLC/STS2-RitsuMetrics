// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DelegatedDamageRefreshTests
    {
        [Fact]
        public void DamageContributionMeterTracksDirectDamageFallbackInMergedMode()
        {
            var metricIds = MetricMeterRenderer.RequiredMetricIds(MetricIds.DamageContribution);
            var requirements = new DashboardDataRequirements(DashboardDataComponents.Metrics, metricIds);

            Assert.Contains(MetricIds.DamageContribution, metricIds);
            Assert.Contains(MetricIds.DamageDealt, metricIds);
            Assert.True(new MetricsChange(MetricsChangeKind.Metrics, MetricIds.DamageDealt)
                .Affects(requirements));
        }

        [Fact]
        public void SingleLineMeterTextChangesAfterAttackAndReactiveDamage()
        {
            var afterAttack = MetricMeterRenderer.SingleLineValueText(10m, 10m, false);
            var afterThorns = MetricMeterRenderer.SingleLineValueText(13m, 13m, false);

            Assert.Equal("10", afterAttack);
            Assert.Equal("13", afterThorns);
            Assert.NotEqual(afterAttack, afterThorns);
        }

        [Fact]
        public void DelegatedCardDamageFallsBackToCardOwner()
        {
            var cardOwner = Player("card-owner");
            var result = CombatAnalyticsService.ResolveDirectDamagePlayer(null, cardOwner, null);

            Assert.Same(cardOwner, result);
        }

        [Fact]
        public void ActorOwnerTakesPrecedenceOverFallbackOwners()
        {
            var actorOwner = Player("actor-owner");
            var result = CombatAnalyticsService.ResolveDirectDamagePlayer(
                actorOwner,
                Player("card-owner"),
                Player("causal-owner"));

            Assert.Same(actorOwner, result);
        }

        [Fact]
        public void NonPlayerOwnersAreNotUsedForDamageCredit()
        {
            var monster = new EntityDescriptor(
                "monster",
                AnalyticsEntityKind.Monster,
                null,
                "MONSTER",
                "Monster");

            var result = CombatAnalyticsService.ResolveDirectDamagePlayer(monster, null, monster);

            Assert.Null(result);
        }

        [Fact]
        public void EmptyPowerCreditsFallBackToDirectPlayer()
        {
            var player = Player("power-owner");
            var source = new SourceDescriptor(
                "power:THORNS_POWER",
                AnalyticsSourceKind.Power,
                "THORNS_POWER",
                "Thorns");

            var result = CombatAnalyticsService.ResolvePrimaryAttributionShares([], player, source, 3m);

            var share = Assert.Single(result);
            Assert.Same(player, share.Contributor);
            Assert.Same(source, share.Source);
            Assert.Equal(3m, share.EffectiveContribution);
            Assert.Equal(AttributionConfidence.Exact, share.Confidence);
        }

        [Fact]
        public void RecordedPowerCreditsTakePrecedenceOverDirectPlayer()
        {
            var creditedPlayer = Player("power-applier");
            var source = new SourceDescriptor(
                "power:THORNS_POWER",
                AnalyticsSourceKind.Power,
                "THORNS_POWER",
                "Thorns");
            DamageAttributionShare[] tracked =
            [
                new(creditedPlayer, source, 1m, 3m, AttributionConfidence.Exact),
            ];

            var result = CombatAnalyticsService.ResolvePrimaryAttributionShares(
                tracked,
                Player("power-owner"),
                source,
                3m);

            Assert.Same(tracked, result);
        }

        private static EntityDescriptor Player(string key)
        {
            return new(
                key,
                AnalyticsEntityKind.Player,
                1,
                "CHARACTER.NECROBINDER",
                "Player",
                "CHARACTER.NECROBINDER");
        }
    }
}
