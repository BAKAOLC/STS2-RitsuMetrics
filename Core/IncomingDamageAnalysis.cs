// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed record IncomingDamageSource(
        string Key,
        string Name,
        decimal HpLost,
        decimal Share,
        int Occurrences);

    internal sealed record IncomingDamageAnalysis(
        decimal HpLost,
        decimal EffectiveBlock,
        decimal BlockGained,
        decimal UnspentBlock,
        decimal HpLossRatio,
        decimal SelfHpLost,
        decimal SelfHpLossRatio,
        bool HasCompleteHpTimeline,
        IReadOnlyList<IncomingDamageSource> Sources)
    {
        private const decimal ComparisonTolerance = 0.0001m;

        internal static IncomingDamageAnalysis Create(
            CombatSnapshot snapshot,
            PlayerMetricSnapshot player)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(player);

            var hpLost = SnapshotStatistics.Survival(snapshot, player.PlayerNetId).PlayerHpLost;
            var effectiveBlock = player.Totals.GetValueOrDefault(MetricIds.DamageBlocked);
            var metricBlockGained = player.Totals.GetValueOrDefault(MetricIds.BlockGained);
            var timeline = snapshot.Timeline ?? [];
            var hpEvents = timeline
                .Where(timelineEvent => IsPlayerBody(timelineEvent.Target, player))
                .Select(timelineEvent => new HpLossEvent(timelineEvent,
                    SnapshotStatistics.EffectiveHpLost(timelineEvent)))
                .Where(item => item.HpLost > 0m)
                .ToArray();
            var timelineHpLost = hpEvents.Sum(item => item.HpLost);
            var hasCompleteHpTimeline = ApproximatelyEqual(timelineHpLost, hpLost);
            var selfHpLost = hasCompleteHpTimeline
                ? hpEvents.Where(item => IsPlayerBody(item.Event.Actor, player)).Sum(item => item.HpLost)
                : 0m;
            var sources = timelineHpLost > 0m && hasCompleteHpTimeline
                ? TimelineSources(hpEvents, hpLost)
                : MetricSources(player, hpLost);

            var ownedBlockEvents = timeline
                .Where(timelineEvent => timelineEvent is
                {
                    Kind: CombatTimelineKind.Block,
                    ActionId: "block.gain",
                    Value: > 0m,
                } && IsOwnedBy(timelineEvent.Target, player))
                .ToArray();
            var timelineOwnedBlock = ownedBlockEvents.Sum(item => item.Value ?? 0m);
            var hasCompleteBlockTimeline = ApproximatelyEqual(timelineOwnedBlock, metricBlockGained);
            var blockGained = hasCompleteBlockTimeline
                ? ownedBlockEvents.Where(item => IsPlayerBody(item.Target, player)).Sum(item => item.Value ?? 0m)
                : metricBlockGained;
            var timelineBlocked = timeline
                .Where(timelineEvent => IsPlayerBody(timelineEvent.Target, player))
                .Sum(timelineEvent => timelineEvent.Damage?.BlockedAmount ?? 0m);
            var unspentBlock = hasCompleteBlockTimeline && ApproximatelyEqual(timelineBlocked, effectiveBlock)
                ? UnspentBlockByCombat(ownedBlockEvents, timeline, player)
                : Math.Max(0m, blockGained - effectiveBlock);
            var incoming = hpLost + effectiveBlock;

            return new(
                hpLost,
                effectiveBlock,
                blockGained,
                unspentBlock,
                incoming > 0m ? hpLost / incoming : 0m,
                selfHpLost,
                hpLost > 0m ? selfHpLost / hpLost : 0m,
                hasCompleteHpTimeline,
                sources);
        }

        private static IReadOnlyList<IncomingDamageSource> TimelineSources(
            IReadOnlyCollection<HpLossEvent> hpEvents,
            decimal totalHpLost)
        {
            return hpEvents
                .GroupBy(item => item.Event.Source?.Key ?? "unknown", StringComparer.Ordinal)
                .Select(group =>
                {
                    var hpLost = group.Sum(item => item.HpLost);
                    var first = group.First().Event;
                    return new IncomingDamageSource(
                        group.Key,
                        first.Source?.DisplayName ?? first.DisplayText,
                        hpLost,
                        totalHpLost > 0m ? hpLost / totalHpLost : 0m,
                        group.Count());
                })
                .OrderByDescending(source => source.HpLost)
                .ThenBy(source => source.Name, StringComparer.CurrentCulture)
                .ToArray();
        }

        private static IReadOnlyList<IncomingDamageSource> MetricSources(
            PlayerMetricSnapshot player,
            decimal totalHpLost)
        {
            return player.Sources.GetValueOrDefault(MetricIds.DamageTaken, [])
                .Select(source => new IncomingDamageSource(
                    source.SourceKey,
                    source.DisplayName,
                    source.Value,
                    totalHpLost > 0m ? source.Value / totalHpLost : 0m,
                    source.Occurrences))
                .OrderByDescending(source => source.HpLost)
                .ThenBy(source => source.Name, StringComparer.CurrentCulture)
                .ToArray();
        }

        private static decimal UnspentBlockByCombat(
            IReadOnlyCollection<CombatTimelineEvent> blockEvents,
            IReadOnlyCollection<CombatTimelineEvent> timeline,
            PlayerMetricSnapshot player)
        {
            var gained = blockEvents
                .Where(timelineEvent => IsPlayerBody(timelineEvent.Target, player))
                .GroupBy(timelineEvent => timelineEvent.CombatId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Value ?? 0m),
                    StringComparer.Ordinal);
            var blocked = timeline
                .Where(timelineEvent => IsPlayerBody(timelineEvent.Target, player))
                .GroupBy(timelineEvent => timelineEvent.CombatId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.Sum(item => item.Damage?.BlockedAmount ?? 0m), StringComparer.Ordinal);
            return gained.Keys.Union(blocked.Keys, StringComparer.Ordinal)
                .Sum(combatId => Math.Max(0m,
                    gained.GetValueOrDefault(combatId) - blocked.GetValueOrDefault(combatId)));
        }

        private static bool IsPlayerBody(EntityDescriptor? entity, PlayerMetricSnapshot player)
        {
            return entity?.Kind == AnalyticsEntityKind.Player && IsOwnedBy(entity, player);
        }

        private static bool IsOwnedBy(EntityDescriptor? entity, PlayerMetricSnapshot player)
        {
            if (entity == null)
                return false;
            return player.PlayerNetId is { } playerNetId
                ? entity.PlayerNetId == playerNetId
                : entity.Key == player.PlayerKey;
        }

        private static bool ApproximatelyEqual(decimal first, decimal second)
        {
            return Math.Abs(first - second) <= ComparisonTolerance;
        }

        private sealed record HpLossEvent(CombatTimelineEvent Event, decimal HpLost);
    }
}
