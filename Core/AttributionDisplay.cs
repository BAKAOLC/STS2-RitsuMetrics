// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class AttributionDisplay
    {
        internal const string DirectPrefix = "view.direct.";
        internal const string ChildrenPrefix = "view.children.";
        internal const string ModeParameter = "linked_attribution";

        internal static bool Includes(IReadOnlySet<string> metrics, string key) => metrics.Contains(key) ||
            key.StartsWith(DirectPrefix, StringComparison.Ordinal) && metrics.Contains(key[DirectPrefix.Length..]) ||
            key.StartsWith(ChildrenPrefix, StringComparison.Ordinal) && metrics.Contains(key[ChildrenPrefix.Length..]);

        internal static CombatSnapshot Project(CombatSnapshot snapshot, bool linked) => snapshot with
        {
            Players = snapshot.Players.Select(player => Project(player, linked)).ToArray(),
            Events = linked ? snapshot.Events : snapshot.Events.Select(observation =>
            {
                if (!observation.Tags.TryGetValue("view.direct.key", out var key)) return observation;
                Enum.TryParse<AnalyticsSourceKind>(observation.Tags.GetValueOrDefault("view.direct.kind"), out var kind);
                return observation with
                {
                    Source = new(key, kind, observation.Tags.GetValueOrDefault("view.direct.model", string.Empty),
                        observation.Tags.GetValueOrDefault("view.direct.name", key)),
                };
            }).ToArray(),
        };

        internal static IReadOnlyDictionary<string, string> Tags(IReadOnlyDictionary<string, string> tags,
            SourceDescriptor direct, SourceDescriptor effect, string? detail = null)
        {
            var result = new Dictionary<string, string>(tags, StringComparer.Ordinal)
            {
                ["view.direct.key"] = direct.Key,
                ["view.direct.kind"] = direct.Kind.ToString(),
                ["view.direct.model"] = direct.ModelId,
                ["view.direct.name"] = direct.DisplayName,
                ["view.effect.key"] = effect.Key + (detail == null ? "" : "\u001f" + detail),
                ["view.effect.name"] = detail == null ? effect.DisplayName : $"{effect.DisplayName} → {detail}",
            };
            return result;
        }

        internal static PlayerMetricSnapshot Project(PlayerMetricSnapshot player, bool linked)
        {
            var sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(player.Sources, StringComparer.Ordinal);
            if (linked)
            {
                var totals = new Dictionary<string, decimal>(player.Totals, StringComparer.Ordinal);
                foreach (var (metric, contribution) in new[]
                         {
                             (MetricIds.DamageDealt, MetricIds.DamageContribution),
                             (MetricIds.EffectiveHpDamageDealt, MetricIds.EffectiveHpDamageContribution),
                         })
                    if (player.Sources.TryGetValue(contribution, out var credited))
                    {
                        sources[metric] = credited;
                        if (player.Totals.TryGetValue(contribution, out var total)) totals[metric] = total;
                        if (player.Sources.TryGetValue(ChildrenPrefix + contribution, out var children))
                            sources[ChildrenPrefix + metric] = children;
                    }
                return player with { Sources = sources, Totals = totals };
            }
            foreach (var (key, value) in player.Sources)
                if (key.StartsWith(DirectPrefix, StringComparison.Ordinal))
                    sources[key[DirectPrefix.Length..]] = value;
            return player with { Sources = sources };
        }

        internal static IReadOnlyList<SourceMetricSnapshot> Children(PlayerMetricSnapshot player, string metric,
            string source) => (player.Sources.GetValueOrDefault(ChildrenPrefix + metric) ?? [])
            .Where(child => child.SourceKey.StartsWith(source + "\u001f", StringComparison.Ordinal))
            .OrderByDescending(child => child.Value).ToArray();

        internal static bool Supports(string metric) => metric is MetricIds.DamageDealt or MetricIds.DamageContribution or
            MetricIds.EffectiveHpDamageDealt or MetricIds.EffectiveHpDamageContribution or MetricIds.DamageAmplified or
            MetricIds.BlockGained or MetricIds.BlockProvided or MetricIds.DefenseContribution or MetricIds.DamagePrevented or
            MetricIds.DamageMitigated or MetricIds.HealingContribution;
    }
}
