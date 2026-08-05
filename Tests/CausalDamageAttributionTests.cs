// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Capture;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    [Collection(CaptureBridgeTestCollection.Name)]
    public sealed class CausalDamageAttributionTests
    {
        [Fact]
        public void ExplicitScopesCanFinishOutOfOrderWithoutLeavingAnotherCardBehind()
        {
            var firstSource = Source("card:FIRST", AnalyticsSourceKind.Card);
            var secondSource = Source("card:SECOND", AnalyticsSourceKind.Card);
            var first = CausalScopeRuntime.EnterExplicit("card:first", null, firstSource, "card.play");
            var second = CausalScopeRuntime.EnterExplicit("card:second", null, secondSource, "card.play");

            try
            {
                CausalScopeRuntime.Restore(first);

                var snapshot = Assert.IsType<CausalScopeSnapshot>(CausalScopeRuntime.Snapshot());
                Assert.Equal(secondSource, snapshot.Source);
                Assert.Null(snapshot.ParentEventId);

                CausalScopeRuntime.Restore(second);
                Assert.Null(CausalScopeRuntime.Snapshot());
            }
            finally
            {
                CausalScopeRuntime.Restore(second);
                CausalScopeRuntime.Restore(first);
            }
        }

        [Fact]
        public void BareCardPlayScopeIsNotUsedForDelayedDamage()
        {
            var staleCard = Snapshot("card.play", Source("card:OTHER", AnalyticsSourceKind.Card));

            var resolved = CombatAnalyticsService.ResolveDamageCause(null, staleCard);

            Assert.Null(resolved);
        }

        [Fact]
        public void PowerHookRemainsTheCauseOfDelayedDamage()
        {
            var power = Snapshot("BeforeSideTurnEnd", Source("power:THE_BOMB_POWER", AnalyticsSourceKind.Power));

            var resolved = CombatAnalyticsService.ResolveDamageCause(null, power);

            Assert.Same(power, resolved);
        }

        [Fact]
        public void HostCardSourceTakesPrecedenceOverAmbientCause()
        {
            var card = Source("card:STRIKE", AnalyticsSourceKind.Card);
            var ambient = Snapshot("BeforeSideTurnEnd", Source("power:OTHER", AnalyticsSourceKind.Power));

            var resolved = CombatAnalyticsService.ResolvePrimaryDamageSource(
                card,
                ambient,
                Source("creature:PLAYER", AnalyticsSourceKind.Creature));

            Assert.Same(card, resolved);
        }

        private static CausalScopeSnapshot Snapshot(string actionId, SourceDescriptor source)
        {
            return new("effect:1", null, null, source, actionId, "effect:1", null, source);
        }

        private static SourceDescriptor Source(string key, AnalyticsSourceKind kind)
        {
            return new(key, kind, key, key);
        }
    }
}
