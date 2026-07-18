// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;

namespace STS2RitsuMetrics.Api
{
    public readonly record struct SurvivalStatistics(
        decimal PlayerHpLost,
        int PlayerDeaths,
        decimal SummonHpLost,
        int SummonDeaths)
    {
        public static SurvivalStatistics operator +(SurvivalStatistics left, SurvivalStatistics right)
        {
            return new(left.PlayerHpLost + right.PlayerHpLost,
                left.PlayerDeaths + right.PlayerDeaths,
                left.SummonHpLost + right.SummonHpLost,
                left.SummonDeaths + right.SummonDeaths);
        }
    }

    public static class SnapshotStatistics
    {
        private static readonly ConditionalWeakTable<CombatSnapshot, SurvivalIndex> SurvivalIndexes = new();

        public static SurvivalStatistics Survival(CombatSnapshot combat, ulong? playerNetId)
        {
            ArgumentNullException.ThrowIfNull(combat);
            return playerNetId == null
                ? default
                : SurvivalIndexes.GetValue(combat, static snapshot => BuildSurvivalIndex(snapshot))
                    .ByPlayerNetId.GetValueOrDefault(playerNetId.Value);
        }

        public static SurvivalStatistics Survival(RunSnapshot run, ulong? playerNetId)
        {
            ArgumentNullException.ThrowIfNull(run);
            return run.Combats.Aggregate(default(SurvivalStatistics),
                (total, combat) => total + Survival(combat, playerNetId));
        }

        public static decimal EffectiveHpLost(CombatTimelineEvent timelineEvent)
        {
            ArgumentNullException.ThrowIfNull(timelineEvent);
            return timelineEvent.ActionId switch
            {
                "damage" => timelineEvent.Damage?.HpLost ?? 0m,
                "hp.loss" or "execution" => timelineEvent.Damage?.HpLost ?? timelineEvent.Value ?? 0m,
                _ => 0m,
            };
        }

        private static SurvivalIndex BuildSurvivalIndex(CombatSnapshot combat)
        {
            var statistics = new Dictionary<ulong, MutableSurvivalStatistics>();
            var timeline = combat.Timeline ?? [];
            var hasTimeline = timeline.Count > 0;
            if (hasTimeline)
                foreach (var timelineEvent in timeline)
                {
                    if (timelineEvent.Target?.PlayerNetId is not { } playerNetId ||
                        timelineEvent.Target.Kind is not (AnalyticsEntityKind.Player or AnalyticsEntityKind.Summon))
                        continue;
                    if (!statistics.TryGetValue(playerNetId, out var player))
                    {
                        player = new();
                        statistics.Add(playerNetId, player);
                    }

                    var hpLost = EffectiveHpLost(timelineEvent);
                    if (timelineEvent.Target.Kind == AnalyticsEntityKind.Player)
                    {
                        player.PlayerHpLost += hpLost;
                        if (IsCompletedDeath(timelineEvent))
                            player.PlayerDeaths++;
                    }
                    else
                    {
                        player.SummonHpLost += hpLost;
                        if (IsCompletedDeath(timelineEvent))
                            player.SummonDeaths++;
                    }
                }

            foreach (var playerSnapshot in combat.Players.Where(player => player.PlayerNetId is not null))
            {
                var playerNetId = playerSnapshot.PlayerNetId!.Value;
                if (!statistics.TryGetValue(playerNetId, out var player))
                {
                    player = new();
                    statistics.Add(playerNetId, player);
                }

                var hasSeparateMetrics = playerSnapshot.Totals.ContainsKey(MetricIds.Deaths) ||
                                         playerSnapshot.Totals.ContainsKey(MetricIds.SummonDamageTaken) ||
                                         playerSnapshot.Totals.ContainsKey(MetricIds.SummonDeaths);
                if (hasSeparateMetrics || !hasTimeline)
                {
                    player.PlayerHpLost = playerSnapshot.Totals.GetValueOrDefault(MetricIds.DamageTaken);
                    player.PlayerDeaths = decimal.ToInt32(
                        playerSnapshot.Totals.GetValueOrDefault(MetricIds.Deaths));
                    player.SummonHpLost = playerSnapshot.Totals.GetValueOrDefault(MetricIds.SummonDamageTaken);
                    player.SummonDeaths = decimal.ToInt32(
                        playerSnapshot.Totals.GetValueOrDefault(MetricIds.SummonDeaths));
                }
                else if (player.SummonHpLost == 0m)
                {
                    player.PlayerHpLost = playerSnapshot.Totals.GetValueOrDefault(MetricIds.DamageTaken);
                }
            }

            return new(statistics.ToDictionary(pair => pair.Key,
                pair => new SurvivalStatistics(pair.Value.PlayerHpLost, pair.Value.PlayerDeaths,
                    pair.Value.SummonHpLost, pair.Value.SummonDeaths)));
        }

        private static bool IsCompletedDeath(CombatTimelineEvent timelineEvent)
        {
            return timelineEvent is
            {
                Kind: CombatTimelineKind.Death,
                Phase: TimelineEventPhase.Completed,
            };
        }

        private sealed record SurvivalIndex(IReadOnlyDictionary<ulong, SurvivalStatistics> ByPlayerNetId);

        private sealed class MutableSurvivalStatistics
        {
            internal decimal PlayerHpLost { get; set; }
            internal int PlayerDeaths { get; set; }
            internal decimal SummonHpLost { get; set; }
            internal int SummonDeaths { get; set; }
        }
    }
}
