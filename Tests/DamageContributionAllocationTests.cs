// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Capture;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Tests
{
    public sealed class DamageContributionAllocationTests
    {
        [Fact]
        public void VulnerableAndKnockdownShareInteractionEquallyInEitherOrder()
        {
            foreach (var effects in new[] { new[] { ("v", 1.5m), ("k", 2m) }, new[] { ("k", 2m), ("v", 1.5m) } })
            {
                var items = Capture(10m, [], effects);
                DamageContributionAllocation.Apply(items, 30m);
                Assert.Equal(10m, items[0].EffectiveContribution);
                Assert.Equal(7.5m, items.Single(item => item.Source.Key == "v").EffectiveContribution);
                Assert.Equal(12.5m, items.Single(item => item.Source.Key == "k").EffectiveContribution);
                Assert.All(items.Skip(1), item => Assert.Equal(2.5m, item.AllocatedContribution - item.StandaloneContribution));
            }
        }

        [Theory]
        [InlineData(1.5, 2, 0.75, 2)]
        [InlineData(2, 0, 1.5, 3)]
        [InlineData(0.5, 0.75, 0.8, -2)]
        public void MatchesAllPermutationMarginalsWithAdditiveAndMultiplicativeEffects(
            double first, double second, double third, int additive)
        {
            var factors = new[] { ("a", (decimal)first), ("b", (decimal)second), ("c", (decimal)third) };
            var items = Capture(10m, [("strength", additive)], factors);
            var damage = items[^1].OutputValue;
            DamageContributionAllocation.Apply(items, damage);
            var effects = new[] { ((decimal)additive, 1m), (0m, (decimal)first), (0m, (decimal)second), (0m, (decimal)third) };
            var expected = new decimal[effects.Length];
            var count = 0;
            foreach (var order in Permutations([0, 1, 2, 3]))
            {
                decimal sum = 10m, product = 1m;
                foreach (var index in order)
                {
                    var before = sum * product;
                    sum += effects[index].Item1;
                    product *= effects[index].Item2;
                    expected[index] += sum * product - before;
                }
                count++;
            }
            for (var i = 0; i < expected.Length; i++)
                Assert.InRange(Math.Abs(items[i + 1].AllocatedContribution!.Value - expected[i] / count), 0m, 0.000000000002m);
            Assert.Equal(damage, items.Sum(item => item.EffectiveContribution));
            Assert.Equal(damage, items.Sum(item => item.AllocatedContribution!.Value));
            var reversed = Capture(10m, [("strength", additive)], factors.Reverse().ToArray());
            DamageContributionAllocation.Apply(reversed, damage);
            foreach (var item in items)
                Assert.Equal(item.AllocatedContribution, reversed.Single(other => other.Source == item.Source).AllocatedContribution);
        }

        [Fact]
        public void SettlementIsPreservedAndRealizedContributionsConserveActualDamage()
        {
            var items = Capture(9m, [("strength", 2m)], [("v", 1.5m), ("weak", 0.75m)]);
            items.Add(Item("block", DamageContributionStage.Block, 12.375m, 8.375m));
            items.Add(Item("rounding", DamageContributionStage.Quantization, 8.375m, 8m));
            items.Add(Item("overkill", DamageContributionStage.Overkill, 8m, 3m));
            var raw = items.Select(item => item.RawContribution).ToArray();
            DamageContributionAllocation.Apply(items, 7m);
            Assert.Equal(raw, items.Select(item => item.RawContribution));
            Assert.Equal(7m, items.Sum(item => item.EffectiveContribution));
            Assert.All(items.TakeLast(3), item => Assert.Equal(0m, item.EffectiveContribution));
            Assert.Equal(3m, DamageContributionAllocation.Scale(items.Select(item => item.EffectiveContribution).ToArray(), 3m).Sum());
            var restored = JsonSerializer.Deserialize<List<DamageContribution>>(JsonSerializer.Serialize(items))!;
            Assert.Equal(items, restored);
        }

        [Fact]
        public void CapturedBreakdownKeepsSettlementAndUsesSharedContribution()
        {
            var effects = Capture(10m, [], [("v", 1.5m), ("k", 2m)]);
            var calculation = new DamageCalculationCapture(null, null, null, 10m, ValueProp.Unpowered, null);
            foreach (var effect in effects.Skip(1))
                calculation.Modifiers.Add(new(effect.Source, effect.Stage, effect.InputValue, effect.OutputValue,
                    effect.RawContribution, effect.Factor, null!, "ModifyDamageMultiplicative"));
            var sources = new DamageSettlementSources(System("damage-floor"), System("block-resolution"),
                System("damage-quantization"), System("overkill-resolution"), System("unknown"));
            var result = CombatAnalyticsService.BuildContributions(calculation, effects[0].Source, 10m, 30m, 5m, 22m, 8m, sources);
            Assert.Equal(5m, result.Single(item => item.Source.Key == "v").RawContribution);
            Assert.Equal(7.5m, result.Single(item => item.Source.Key == "v").AllocatedContribution);
            Assert.Equal(22m, result.Sum(item => item.EffectiveContribution));
            Assert.Equal(17m, result.Sum(item => item.RawContribution));
            Assert.Equal(17m, result.Sum(item => item.AllocatedContribution!.Value));

            static SourceDescriptor System(string id) => new("system:" + id, AnalyticsSourceKind.System, id, id);
        }

        [Fact]
        public void PowerProvidersConserveDamageWithFractionalShares()
        {
            var power = new SourceDescriptor("strength", AnalyticsSourceKind.Power, "strength", "Strength");
            var credits = new CombatAnalyticsService.PowerAttribution(power);
            for (var i = 0; i < 3; i++)
                credits.Add(new($"p{i}", AnalyticsEntityKind.Player, (ulong)i, "p", "Player"), 1m, null,
                    new($"r{i}", AnalyticsSourceKind.Relic, "r", "Relic"));
            var shares = credits.Shares(1m, AttributionConfidence.Derived, true);
            Assert.Equal(1m, shares.Sum(item => item.EffectiveContribution));
            var contribution = Item("strength", DamageContributionStage.Additive, 10m, 11m) with { Providers = shares };
            var restored = JsonSerializer.Deserialize<DamageContribution>(JsonSerializer.Serialize(contribution))!;
            Assert.Equal(shares, restored.Providers);
        }

        [Fact]
        public void ManyEffectsRemainOrderIndependentWithoutEnumeratingPermutations()
        {
            var factors = Enumerable.Range(0, 24).Select(i => ($"m{i:00}", i % 2 == 0 ? 1.1m : 0.9m)).ToArray();
            var forward = Capture(10m, [], factors);
            var reverse = Capture(10m, [], factors.Reverse().ToArray());
            DamageContributionAllocation.Apply(forward, 2m);
            DamageContributionAllocation.Apply(reverse, 2m);
            Assert.Equal(2m, forward.Sum(item => item.EffectiveContribution));
            foreach (var item in forward)
                Assert.InRange(Math.Abs(item.AllocatedContribution!.Value - reverse.Single(other => other.Source == item.Source).AllocatedContribution!.Value), 0m, 0.000000000002m);
        }

        [Fact]
        public void CapsAndHpLossAreObservedBoundariesAndZeroDamageHasNoPositiveCredit()
        {
            var items = Capture(10m, [], [("v", 1.5m), ("k", 2m)]);
            items.Add(Item("cap", DamageContributionStage.Cap, 30m, 1m));
            DamageContributionAllocation.Apply(items, 1m);
            Assert.Equal(-29m, items[^1].AllocatedContribution);
            Assert.Equal(1m, items.Sum(item => item.EffectiveContribution));
            DamageContributionAllocation.Apply(items, 0m);
            Assert.All(items, item => Assert.Equal(0m, item.EffectiveContribution));
            Assert.False(new ModSettings().AttributeTriggeredEffects);
        }

        private static List<DamageContribution> Capture(decimal baseline,
            (string Key, decimal Value)[] additives, (string Key, decimal Value)[] factors)
        {
            var items = new List<DamageContribution> { Item("base", DamageContributionStage.Base, 0m, baseline) };
            var value = baseline;
            foreach (var (key, amount) in additives)
            {
                items.Add(Item(key, DamageContributionStage.Additive, value, value + amount));
                value += amount;
            }
            foreach (var (key, factor) in factors)
            {
                items.Add(Item(key, DamageContributionStage.Multiplicative, value, value * factor, factor));
                value *= factor;
            }
            return items;
        }

        private static DamageContribution Item(string key, DamageContributionStage stage, decimal input, decimal output, decimal? factor = null) =>
            new(new(key, AnalyticsSourceKind.Power, key, key), stage, input, output, output - input,
                output - input, factor, AttributionConfidence.Exact);

        private static IEnumerable<int[]> Permutations(int[] values)
        {
            if (values.Length == 0) { yield return []; yield break; }
            foreach (var first in values)
                foreach (var rest in Permutations(values.Where(value => value != first).ToArray()))
                    yield return [first, ..rest];
        }
    }
}
