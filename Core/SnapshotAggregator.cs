// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class SnapshotAggregator
    {
        internal static CombatSnapshot? Combine(RunSnapshot? run)
        {
            if (run == null || run.Combats.Count == 0)
                return null;
            var players = new Dictionary<string, PlayerAccumulator>(StringComparer.Ordinal);
            foreach (var combat in run.Combats)
            foreach (var player in combat.Players)
            {
                if (!players.TryGetValue(player.PlayerKey, out var accumulator))
                {
                    accumulator = new(player);
                    players.Add(player.PlayerKey, accumulator);
                }

                accumulator.Add(player);
            }

            var first = run.Combats.MinBy(combat => combat.StartedAtUtc)!;
            var last = run.Combats.MaxBy(combat => combat.StartedAtUtc)!;
            return new(
                run.RunId,
                "run-total",
                last.ActIndex,
                last.Floor,
                "run-total",
                string.Empty,
                first.StartedAtUtc,
                run.EndedAtUtc,
                run.EndedAtUtc != null,
                run.Combats.Sum(combat => combat.RoundCount),
                players.Values.Select(accumulator => accumulator.Snapshot()).ToArray(),
                run.Combats.SelectMany(combat => combat.Events).ToArray(),
                run.Combats.SelectMany(combat => combat.Timeline ?? []).OrderBy(evt => evt.OccurredAtUtc)
                    .ThenBy(evt => evt.Sequence).ToArray());
        }

        private sealed class PlayerAccumulator(PlayerMetricSnapshot first)
        {
            private readonly Dictionary<string, Dictionary<string, SourceAccumulator>> _sources =
                new(StringComparer.Ordinal);

            private readonly Dictionary<string, decimal> _totals = new(StringComparer.Ordinal);

            internal void Add(PlayerMetricSnapshot player)
            {
                foreach (var total in player.Totals)
                    _totals[total.Key] = _totals.GetValueOrDefault(total.Key) + total.Value;
                AddFallbackTotal(MetricIds.DamageContribution, MetricIds.DamageDealt);
                AddFallbackTotal(MetricIds.EffectiveHpDamageDealt, MetricIds.DamageDealt);
                AddFallbackTotal(MetricIds.EffectiveHpDamageContribution,
                    player.Totals.ContainsKey(MetricIds.EffectiveHpDamageDealt)
                        ? MetricIds.EffectiveHpDamageDealt
                        : MetricIds.DamageDealt);
                foreach (var metric in player.Sources) AddSources(metric.Key, metric.Value);

                AddFallbackSources(MetricIds.DamageContribution, MetricIds.DamageDealt);
                AddFallbackSources(MetricIds.EffectiveHpDamageDealt, MetricIds.DamageDealt);
                AddFallbackSources(MetricIds.EffectiveHpDamageContribution,
                    player.Sources.ContainsKey(MetricIds.EffectiveHpDamageDealt)
                        ? MetricIds.EffectiveHpDamageDealt
                        : MetricIds.DamageDealt);

                void AddFallbackTotal(string metricId, string fallbackId)
                {
                    if (player.Totals.ContainsKey(metricId) || !player.Totals.TryGetValue(fallbackId, out var value))
                        return;
                    _totals[metricId] = _totals.GetValueOrDefault(metricId) + value;
                }

                void AddFallbackSources(string metricId, string fallbackId)
                {
                    if (player.Sources.ContainsKey(metricId) ||
                        !player.Sources.TryGetValue(fallbackId, out var fallbackSources))
                        return;
                    AddSources(metricId, fallbackSources);
                }

                void AddSources(string metricId, IReadOnlyList<SourceMetricSnapshot> metricSources)
                {
                    if (!_sources.TryGetValue(metricId, out var sources))
                    {
                        sources = new(StringComparer.Ordinal);
                        _sources.Add(metricId, sources);
                    }

                    foreach (var source in metricSources)
                    {
                        if (!sources.TryGetValue(source.SourceKey, out var accumulator))
                        {
                            accumulator = new(source);
                            sources.Add(source.SourceKey, accumulator);
                        }

                        accumulator.Value += source.Value;
                        accumulator.Occurrences += source.Occurrences;
                    }
                }
            }

            internal PlayerMetricSnapshot Snapshot()
            {
                var sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(_sources.Count,
                    StringComparer.Ordinal);
                foreach (var (metricId, values) in _sources)
                    sources.Add(metricId, values.Values
                        .Select(source => source.Snapshot())
                        .OrderByDescending(source => source.Value)
                        .ToArray());
                return first with
                {
                    Totals = new Dictionary<string, decimal>(_totals, StringComparer.Ordinal),
                    Sources = sources,
                };
            }
        }

        private sealed class SourceAccumulator(SourceMetricSnapshot first)
        {
            internal decimal Value { get; set; }
            internal int Occurrences { get; set; }

            internal SourceMetricSnapshot Snapshot()
            {
                return first with { Value = Value, Occurrences = Occurrences };
            }
        }
    }
}
