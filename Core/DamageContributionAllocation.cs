// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed record DamageSettlementSources(SourceDescriptor Floor, SourceDescriptor Block,
        SourceDescriptor Quantization, SourceDescriptor Overkill, SourceDescriptor Unknown);

    internal static class DamageContributionAllocation
    {
        // v(S) = (base + sum(additive in S)) * product(factors in S).
        // The base is fixed; each interaction is divided among its participating effects.
        // Caps and HP-loss hooks are boundaries: their counterfactuals are unknown.
        internal static void Apply(List<DamageContribution> contributions, decimal effectiveDamage)
        {
            for (var i = 0; i < contributions.Count; i++)
                contributions[i] = contributions[i] with
                {
                    AllocatedContribution = contributions[i].RawContribution,
                    StandaloneContribution = contributions[i].RawContribution,
                    UsesAverageMarginal = false,
                };
            var start = 0;
            while (start < contributions.Count)
            {
                if (!Eligible(contributions[start])) { start++; continue; }
                var end = start + 1;
                while (end < contributions.Count && Eligible(contributions[end])) end++;
                AllocateGroup(contributions, start, end);
                start = end;
            }
            var positive = Enumerable.Range(0, contributions.Count)
                .Where(i => DamageContributionSemantics.GetRole(contributions[i]) != DamageContributionRole.Settlement &&
                            contributions[i].AllocatedContribution > 0m)
                .OrderBy(i => contributions[i].Source.Key, StringComparer.Ordinal).ThenBy(i => contributions[i].Stage)
                .ToArray();
            var amounts = Scale(positive.Select(i => contributions[i].AllocatedContribution!.Value).ToArray(), effectiveDamage);
            for (var i = 0; i < contributions.Count; i++)
                contributions[i] = contributions[i] with { EffectiveContribution = 0m };
            for (var i = 0; i < positive.Length; i++)
                contributions[positive[i]] = contributions[positive[i]] with { EffectiveContribution = amounts[i] };
        }

        internal static decimal[] Scale(IReadOnlyList<decimal> weights, decimal amount)
        {
            var result = new decimal[weights.Count];
            var total = weights.Sum();
            if (total <= 0m) return result;
            if (total == amount) return weights.ToArray();
            var remaining = amount;
            for (var i = 0; i < weights.Count; i++)
            {
                result[i] = i == weights.Count - 1 ? remaining : Math.Clamp(decimal.Round(weights[i] / total * amount, 12), 0m, remaining);
                remaining -= result[i];
            }
            return result;
        }

        private static bool Eligible(DamageContribution item) =>
            item.Confidence == AttributionConfidence.Exact &&
            (item.Stage == DamageContributionStage.Additive ||
             item.Stage == DamageContributionStage.Multiplicative && item.Factor >= 0m);

        private static void AllocateGroup(List<DamageContribution> items, int start, int end)
        {
            var ordered = Enumerable.Range(start, end - start)
                .OrderBy(i => items[i].Source.Key, StringComparer.Ordinal).ThenBy(i => items[i].Stage)
                .ThenBy(i => items[i].Factor).ThenBy(i => items[i].RawContribution).ToArray();
            var factors = ordered.Where(i => items[i].Stage == DamageContributionStage.Multiplicative).ToArray();
            var additives = ordered.Where(i => items[i].Stage == DamageContributionStage.Additive).ToArray();
            var baseline = items[start].InputValue;
            // Do not reinterpret additive hooks after multiplication or discontinuous host operations.
            var multiplied = false;
            var expected = baseline;
            for (var i = start; i < end; i++)
            {
                if (items[i].InputValue != expected || multiplied && items[i].Stage == DamageContributionStage.Additive ||
                    items[i].Stage == DamageContributionStage.Multiplicative &&
                    items[i].InputValue * items[i].Factor != items[i].OutputValue)
                    return;
                multiplied |= items[i].Stage == DamageContributionStage.Multiplicative;
                expected = items[i].OutputValue;
            }
            var additive = additives.Sum(i => items[i].RawContribution);
            foreach (var i in additives)
                items[i] = items[i] with
                {
                    AllocatedContribution = items[i].RawContribution * Integral(factors.Select(j => items[j].Factor!.Value), false),
                };
            foreach (var i in factors)
            {
                var others = factors.Where(j => i != j).Select(j => items[j].Factor!.Value).ToArray();
                items[i] = items[i] with
                {
                    StandaloneContribution = baseline * (items[i].Factor!.Value - 1m),
                    AllocatedContribution = (items[i].Factor!.Value - 1m) *
                        (baseline * Integral(others, false) + additive * Integral(others, true)),
                };
            }
            foreach (var i in ordered)
                items[i] = items[i] with
                {
                    AllocatedContribution = decimal.Round(items[i].AllocatedContribution!.Value, 12),
                    UsesAverageMarginal = true,
                };
            var residue = expected - baseline - ordered.Sum(i => items[i].AllocatedContribution!.Value);
            if (ordered.Length > 0)
                items[ordered[^1]] = items[ordered[^1]] with
                {
                    AllocatedContribution = items[ordered[^1]].AllocatedContribution + residue,
                };
        }

        // Integrate product(1-t+t*f), optionally multiplied by t, in the Bernstein basis.
        // Averaged subset products avoid factorial enumeration and alternating-coefficient cancellation.
        private static decimal Integral(IEnumerable<decimal> factors, bool timesT)
        {
            var coefficients = new List<decimal> { 1m };
            foreach (var factor in factors)
            {
                var degree = coefficients.Count;
                coefficients.Add(0m);
                for (var k = degree; k >= 0; k--)
                    coefficients[k] = (k < degree ? coefficients[k] * ((decimal)(degree - k) / degree) : 0m) +
                                      (k > 0 ? coefficients[k - 1] * factor * ((decimal)k / degree) : 0m);
            }
            var count = coefficients.Count;
            return coefficients.Select((value, k) => timesT
                ? value / count * ((decimal)(k + 1) / (count + 1))
                : value / count).Sum();
        }
    }
}
