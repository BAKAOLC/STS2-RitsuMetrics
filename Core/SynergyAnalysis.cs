// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed record SynergySummary(EntityDescriptor Player, SourceDescriptor Source,
        string EffectKey, string EffectName, string Kind, decimal Amount, int Count, AttributionConfidence Confidence);

    internal static class SynergyAnalysis
    {
        internal static SynergySummary[] Summarize(IEnumerable<CombatTimelineEvent> timeline,
            bool amplification, bool triggers)
        {
            return timeline.Where(evt => evt.Source != null && evt.Actor?.Kind == AnalyticsEntityKind.Player &&
                                         evt.Value > 0m &&
                                         (amplification && evt.ActionId == "synergy.amplification" ||
                                          triggers && evt.ActionId is "synergy.damage" or "synergy.block"))
                .DistinctBy(evt => (evt.CombatId, evt.EventId))
                .GroupBy(evt => (evt.Actor!.Key, evt.Source!.Key, evt.ActionId,
                    Effect: evt.Details.GetValueOrDefault("effect_key", string.Empty)))
                .Select(group =>
                {
                    var first = group.First();
                    var name = first.Details.GetValueOrDefault("effect_name", group.Key.Effect);
                    if (Enum.TryParse<AnalyticsSourceKind>(first.Details.GetValueOrDefault("effect_kind"), out var kind))
                        name = LocalizedModelNameResolver.Resolve(kind,
                            first.Details.GetValueOrDefault("effect_id", string.Empty), name);
                    var confidence = group.Select(evt =>
                        Enum.TryParse<AttributionConfidence>(evt.Details.GetValueOrDefault("confidence"), out var parsed)
                            ? parsed : AttributionConfidence.Unknown).Max();
                    return new SynergySummary(first.Actor!, first.Source!, group.Key.Effect, name, first.ActionId,
                        group.Sum(evt => evt.Value ?? 0m), group.Count(), confidence);
                })
                .OrderByDescending(row => row.Amount).ThenBy(row => row.Source.Key, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
