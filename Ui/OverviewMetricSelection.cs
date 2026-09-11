// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Ui
{
    internal static class OverviewMetricSelection
    {
        internal const int MaximumSelected = 16;
        internal static readonly string[] Defaults =
        [
            MetricIds.DamageDealt, MetricIds.BlockGained, MetricIds.DamageContribution, "overview.damage_share",
            "overview.damage_per_turn", "overview.effective_block", MetricIds.Deaths, "overview.hp_loss_ratio",
            MetricIds.HealingReceived, MetricIds.CardsPlayed, "overview.damage_per_energy", MetricIds.Overkill,
        ];
        internal static readonly string[] SupportPreset =
        [
            MetricIds.BlockProvided, MetricIds.SelfBlockProvided, MetricIds.AllyBlockProvided, MetricIds.AllyDamageBlocked,
            MetricIds.HealingContribution, MetricIds.AllyHealing, MetricIds.DamageAmplified, MetricIds.DefenseContribution,
        ];
        internal static readonly MetricDefinition[] Derived =
        [
            new("overview.damage_share", "overview.damageShare", "Damage share", MetricValueKind.Amount, "damage"),
            new("overview.effective_block", "analysis.effectiveBlock", "Effective block", MetricValueKind.Amount, "defense"),
            new("overview.hp_loss_ratio", "analysis.hpLossRatio", "HP loss ratio", MetricValueKind.Amount, "survival"),
            new("overview.damage_per_turn", "analysis.damagePerTurn", "Damage / turn", MetricValueKind.Amount, "efficiency"),
            new("overview.damage_per_energy", "analysis.damagePerEnergy", "Damage / energy", MetricValueKind.Amount, "efficiency"),
            new("overview.damage_per_card", "analysis.damagePerCard", "Damage / card", MetricValueKind.Amount, "efficiency"),
            new("overview.block_efficiency", "analysis.blockEfficiency", "Block efficiency", MetricValueKind.Amount, "efficiency"),
        ];

        internal static string[] Normalize(IEnumerable<string>? selected, IEnumerable<string> available)
        {
            var known = available.ToHashSet(StringComparer.Ordinal);
            var result = (selected ?? Defaults).Where(known.Contains).Distinct(StringComparer.Ordinal)
                .Take(MaximumSelected).ToArray();
            return result.Length > 0 ? result : Defaults.Where(known.Contains).ToArray();
        }

        internal static decimal? Value(PlayerMetricSnapshot player, CombatSnapshot snapshot, string id)
        {
            return id switch
            {
                "overview.damage_share" => Divide(MetricAvailability.Value(player, MetricIds.DamageDealt),
                    MetricAvailability.Total(snapshot.Players.Select(item =>
                        MetricAvailability.Value(item, MetricIds.DamageDealt)))) * 100m,
                "overview.effective_block" => MetricAvailability.Value(player, MetricIds.DamageBlocked) != null
                    ? SnapshotAnalysisCache.Get(snapshot).Incoming(player).EffectiveBlock : null,
                "overview.hp_loss_ratio" => HpLossRatio(player, snapshot),
                MetricIds.Deaths => MetricAvailability.Value(player, MetricIds.Deaths) != null
                    ? SnapshotStatistics.Survival(snapshot, player.PlayerNetId).PlayerDeaths : null,
                "overview.damage_per_turn" => Divide(MetricAvailability.Value(player, MetricIds.DamageDealt), snapshot.RoundCount),
                "overview.damage_per_energy" => Divide(MetricAvailability.Value(player, MetricIds.DamageDealt),
                    MetricAvailability.Value(player, MetricIds.EnergySpent)),
                "overview.damage_per_card" => Divide(MetricAvailability.Value(player, MetricIds.DamageDealt),
                    MetricAvailability.Value(player, MetricIds.CardsPlayed)),
                "overview.block_efficiency" => Divide(MetricAvailability.Value(player, MetricIds.DamageBlocked),
                    MetricAvailability.Value(player, MetricIds.BlockGained)) * 100m,
                _ => MetricAvailability.Value(player, id),
            };
        }

        internal static string Suffix(string id)
        {
            return id is "overview.damage_share" or "overview.hp_loss_ratio" or "overview.block_efficiency" ? "%" : "";
        }

        private static decimal? HpLossRatio(PlayerMetricSnapshot player, CombatSnapshot snapshot)
        {
            if (MetricAvailability.Value(player, MetricIds.DamageTaken) == null ||
                MetricAvailability.Value(player, MetricIds.DamageBlocked) == null)
                return null;
            var incoming = SnapshotAnalysisCache.Get(snapshot).Incoming(player);
            return Divide(incoming.HpLost, incoming.HpLost + incoming.EffectiveBlock) * 100m;
        }

        private static decimal? Divide(decimal? numerator, decimal? denominator)
        {
            return denominator > 0m ? numerator / denominator : null;
        }
    }
}
