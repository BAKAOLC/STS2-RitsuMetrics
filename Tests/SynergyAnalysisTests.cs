// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Capture;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    [Collection(CaptureBridgeTestCollection.Name)]
    public sealed class SynergyAnalysisTests
    {
        private static readonly EntityDescriptor Player = new("player:1", AnalyticsEntityKind.Player, 1, "player", "Player");
        private static readonly SourceDescriptor Strength = new("power:strength", AnalyticsSourceKind.Power, "strength", "Strength");
        private static readonly SourceDescriptor Relic = new("relic:vajra", AnalyticsSourceKind.Relic, "vajra", "Vajra");
        private static readonly SourceDescriptor Card = new("card:shatter", AnalyticsSourceKind.Card, "shatter", "Shatter");

        [Fact]
        public void MixedStrengthCreditsSplitAmplificationWithoutChangingAppliedSource()
        {
            var credits = new CombatAnalyticsService.PowerAttribution(Strength);
            credits.Add(Player, 1m, "relic", Relic);
            credits.Add(Player, 2m, "card", Card);
            var shares = credits.Shares(6m, AttributionConfidence.Derived, true);
            Assert.Equal(2m, shares.Single(share => share.Source == Relic).EffectiveContribution);
            Assert.Equal(4m, shares.Single(share => share.Source == Card).EffectiveContribution);
            Assert.All(credits.Shares(6m, AttributionConfidence.Derived), share => Assert.Equal(Strength, share.Source));
            Assert.Equal(6m, shares.Sum(share => share.EffectiveContribution));
        }

        [Fact]
        public void ReductionPreservesProportionsAndRemovalClearsOldSources()
        {
            var credits = new CombatAnalyticsService.PowerAttribution(Strength);
            credits.Add(Player, 2m, null, Relic);
            credits.Add(Player, 2m, null, Card);
            credits.Remove(2m);
            Assert.All(credits.Shares(2m, AttributionConfidence.Derived, true),
                share => Assert.Equal(1m, share.EffectiveContribution));
            credits.Remove(10m);
            Assert.Empty(credits.Shares(5m, AttributionConfidence.Derived, true));
            credits.Add(Player, 1m, null, Card);
            Assert.Equal(Card, Assert.Single(credits.Shares(5m, AttributionConfidence.Derived, true)).Source);
        }

        [Fact]
        public void MissingOriginKeepsPowerSourceInsteadOfInventingCardCredit()
        {
            var credits = new CombatAnalyticsService.PowerAttribution(Strength);
            credits.Add(Player, 1m, null);
            Assert.Equal(Strength, Assert.Single(credits.Shares(3m, AttributionConfidence.Derived, true)).Source);
        }

        [Fact]
        public void ShatterReceivesOrbDamageOnceInsteadOfEveryAncestorReceivingIt()
        {
            var orb = new SourceDescriptor("orb:lightning", AnalyticsSourceKind.Orb, "lightning", "Lightning");
            var cause = new CausalScopeSnapshot("orb", "card", null, orb, "Evoke", "relic", null, Relic)
            {
                Ancestors = [new(null, Card, "card"), new(null, Relic, "relic")],
            };
            var providers = CombatAnalyticsService.SelectEffectProviders(cause, orb, 8m,
                ancestor => [new(Player, ancestor.Source, 1m, 8m, AttributionConfidence.Exact)]);
            var share = Assert.Single(providers);
            Assert.Equal(Card, share.Source);
            Assert.Equal(8m, share.EffectiveContribution);
        }

        [Fact]
        public void LightningRodPowerPassesOrbDamageToItsOriginalCard()
        {
            var orb = new SourceDescriptor("orb:lightning", AnalyticsSourceKind.Orb, "lightning", "Lightning");
            var power = new SourceDescriptor("power:rod", AnalyticsSourceKind.Power, "rod", "Lightning Rod");
            var credits = new CombatAnalyticsService.PowerAttribution(power);
            credits.Add(Player, 2m, "card", Card);
            var cause = new CausalScopeSnapshot("orb", "power", null, orb, "Evoke", "power", null, power)
            {
                Ancestors = [new(null, power, "power")],
            };
            var providers = CombatAnalyticsService.SelectEffectProviders(cause, orb, 8m,
                _ => credits.Shares(8m, AttributionConfidence.Derived, true));
            Assert.Equal(Card, Assert.Single(providers).Source);
            Assert.Equal(8m, providers.Sum(share => share.EffectiveContribution));
        }

        [Fact]
        public void PassiveOrbWithoutTriggerKeepsItsOwnAttribution()
        {
            var orb = new SourceDescriptor("orb:lightning", AnalyticsSourceKind.Orb, "lightning", "Lightning");
            var cause = new CausalScopeSnapshot("orb", null, null, orb, "Passive", "orb", null, orb);
            Assert.Empty(CombatAnalyticsService.SelectEffectProviders(cause, orb, 3m,
                _ => throw new InvalidOperationException("No provider should be queried")));
        }

        [Fact]
        public void OrbScopeRetainsItsCardAncestorAndRestoresCleanly()
        {
            var card = CausalScopeRuntime.EnterExplicit("card", null, Card, "card.play");
            var orb = CausalScopeRuntime.EnterExplicit("orb", null,
                new("orb:lightning", AnalyticsSourceKind.Orb, "lightning", "Lightning"), "Evoke");
            try
            {
                var cause = CausalScopeRuntime.Snapshot()!;
                Assert.Equal(Card, Assert.Single(cause.Ancestors).Source);
                Assert.Equal(AnalyticsSourceKind.Orb, cause.Source!.Kind);
            }
            finally
            {
                CausalScopeRuntime.Restore(orb);
                CausalScopeRuntime.Restore(card);
            }
            Assert.Null(CausalScopeRuntime.Snapshot());
        }

        [Fact]
        public void SummarySeparatesEffectsAndDeduplicatesRepeatedSnapshots()
        {
            var damage = Event("1", "synergy.damage", 8m);
            var block = Event("2", "synergy.block", 5m);
            var amplified = Event("3", "synergy.amplification", 2m);
            var rows = SynergyAnalysis.Summarize([damage, damage, block, amplified], true, true);
            Assert.Equal(3, rows.Length);
            Assert.All(rows, row => Assert.Equal(1, row.Count));
            Assert.Equal(8m, rows.Single(row => row.Kind == "synergy.damage").Amount);
            Assert.Single(SynergyAnalysis.Summarize([damage, block, amplified], true, false));
            Assert.Equal(2, SynergyAnalysis.Summarize([damage, block, amplified], false, true).Length);
            Assert.Empty(SynergyAnalysis.Summarize([damage, block, amplified], false, false));
        }

        [Fact]
        public void OldHistoryAndOrdinaryDamageDoNotInventSynergies()
        {
            Assert.Empty(SynergyAnalysis.Summarize([Event("1", "damage", 50m)], true, true));
        }

        [Fact]
        public void NewDetailsSurviveJsonRoundTripAndCombatIdsRemainDistinct()
        {
            var first = Event("1", "synergy.damage", 8m);
            var serialized = JsonSerializer.Serialize(first);
            var restored = JsonSerializer.Deserialize<CombatTimelineEvent>(serialized)!;
            var row = Assert.Single(SynergyAnalysis.Summarize([restored, first with { CombatId = "other" }], true, true));
            Assert.Equal(16m, row.Amount);
            Assert.Equal(2, row.Count);
        }

        private static CombatTimelineEvent Event(string id, string action, decimal value)
        {
            return new(1, id, null, "run", "combat", DateTimeOffset.UtcNow, 1, 1,
                TimelineTurnSide.Player, false, CombatTimelineKind.Custom, TimelineEventPhase.Instant,
                action, "effect", Player, null, Card, value, new Dictionary<string, string>
                {
                    ["effect_key"] = "orb:test",
                    ["effect_name"] = "Orb",
                    ["confidence"] = "Exact",
                });
        }
    }
}
