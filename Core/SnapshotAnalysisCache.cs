// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed record TurnAnalysisPoint(
        string CombatId,
        DateTimeOffset OccurredAtUtc,
        int Index,
        decimal Damage,
        decimal HpLost,
        decimal EffectiveBlock,
        decimal BlockGained,
        decimal Overkill,
        int Cards,
        int Draws,
        decimal Energy,
        decimal ModifierImpact,
        bool Extra,
        TimelineTurnSide Side);

    internal sealed record SnapshotAnalysis(
        IReadOnlyDictionary<string, IncomingDamageAnalysis> IncomingByPlayer,
        IReadOnlyList<TurnAnalysisPoint> Turns,
        decimal AppliedDamage,
        decimal HpDamage,
        decimal EnemyBlockedDamage,
        decimal Overkill,
        decimal HpLost,
        decimal EffectiveBlock,
        decimal BlockGained,
        decimal UnspentBlock,
        decimal SelfHpLost,
        decimal Healing,
        decimal CardsPlayed,
        decimal EnergySpent)
    {
        internal decimal DamageOutcomeTotal => HpDamage + EnemyBlockedDamage + Overkill;
        internal decimal DamageConversion => DamageOutcomeTotal > 0m ? HpDamage / DamageOutcomeTotal : 0m;
        internal decimal IncomingTotal => HpLost + EffectiveBlock;
        internal decimal HpLossRatio => IncomingTotal > 0m ? HpLost / IncomingTotal : 0m;
        internal decimal BlockEfficiency => BlockGained > 0m ? EffectiveBlock / BlockGained : 0m;

        internal IncomingDamageAnalysis Incoming(PlayerMetricSnapshot player)
        {
            return IncomingByPlayer.GetValueOrDefault(player.PlayerKey) ??
                   throw new InvalidOperationException($"Missing incoming analysis for player '{player.PlayerKey}'.");
        }
    }

    internal static class SnapshotAnalysisCache
    {
        private static readonly ConditionalWeakTable<CombatSnapshot, Lazy<SnapshotAnalysis>> Cache = new();

        internal static SnapshotAnalysis Get(CombatSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return Cache.GetValue(snapshot,
                static value => new(() => Create(value), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        internal static void Precompute(CombatSnapshot? snapshot)
        {
            if (snapshot != null)
                _ = Get(snapshot);
        }

        internal static void Precompute(IEnumerable<CombatSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots)
                Precompute(snapshot);
        }

        private static SnapshotAnalysis Create(CombatSnapshot snapshot)
        {
            var incomingByPlayer = snapshot.Players.ToDictionary(
                player => player.PlayerKey,
                player => IncomingDamageAnalysis.Create(snapshot, player),
                StringComparer.Ordinal);
            var appliedDamage = snapshot.Players.Sum(player =>
                player.Totals.GetValueOrDefault(MetricIds.DamageDealt));
            var hpDamage = snapshot.Players.Sum(player =>
                player.Totals.TryGetValue(MetricIds.EffectiveHpDamageDealt, out var value)
                    ? value
                    : player.Totals.GetValueOrDefault(MetricIds.DamageDealt));
            var overkill = snapshot.Players.Sum(player => player.Totals.GetValueOrDefault(MetricIds.Overkill));

            return new(
                incomingByPlayer,
                CreateTurns(snapshot.Timeline ?? []),
                appliedDamage,
                hpDamage,
                Math.Max(0m, appliedDamage - hpDamage),
                overkill,
                incomingByPlayer.Values.Sum(incoming => incoming.HpLost),
                incomingByPlayer.Values.Sum(incoming => incoming.EffectiveBlock),
                incomingByPlayer.Values.Sum(incoming => incoming.BlockGained),
                incomingByPlayer.Values.Sum(incoming => incoming.UnspentBlock),
                incomingByPlayer.Values.Where(incoming => incoming.HasCompleteHpTimeline)
                    .Sum(incoming => incoming.SelfHpLost),
                snapshot.Players.Sum(player => player.Totals.GetValueOrDefault(MetricIds.HealingReceived)),
                snapshot.Players.Sum(player => player.Totals.GetValueOrDefault(MetricIds.CardsPlayed)),
                snapshot.Players.Sum(player => player.Totals.GetValueOrDefault(MetricIds.EnergySpent)));
        }

        private static IReadOnlyList<TurnAnalysisPoint> CreateTurns(
            IReadOnlyList<CombatTimelineEvent> timeline)
        {
            return timeline
                .GroupBy(item => (item.CombatId, item.TurnIndex))
                .OrderBy(group => group.Min(item => item.OccurredAtUtc))
                .Select(group =>
                {
                    var events = group.ToArray();
                    return new TurnAnalysisPoint(
                        group.Key.CombatId,
                        events.Min(item => item.OccurredAtUtc),
                        group.Key.TurnIndex,
                        events.Where(item => item.Damage != null &&
                                             item.Target?.Kind == AnalyticsEntityKind.Monster)
                            .Sum(item => item.Damage!.EffectiveAmount),
                        events.Where(item => item.Target?.Kind == AnalyticsEntityKind.Player)
                            .Sum(SnapshotStatistics.EffectiveHpLost),
                        events.Where(item => item is
                        { Damage: not null, Target.Kind: AnalyticsEntityKind.Player })
                            .Sum(item => item.Damage!.BlockedAmount),
                        events.Where(item => item is
                        { Kind: CombatTimelineKind.Block, Actor.Kind: AnalyticsEntityKind.Player })
                            .Sum(item => item.Value ?? 0m),
                        events.Where(item => item.Damage != null &&
                                             item.Target?.Kind == AnalyticsEntityKind.Monster)
                            .Sum(item => item.Damage!.OverkillAmount),
                        events.Count(item => item is
                        { Kind: CombatTimelineKind.CardPlay, Phase: TimelineEventPhase.Started }),
                        events.Count(item => item.Kind == CombatTimelineKind.CardDraw),
                        events.Where(item => item is
                        { Kind: CombatTimelineKind.Energy, ActionId: "energy.spend" })
                            .Sum(item => item.Value ?? 0m),
                        events.Where(item => item.Damage != null)
                            .SelectMany(item => item.Damage!.Contributions)
                            .Where(item => DamageContributionSemantics.GetRole(item) ==
                                           DamageContributionRole.Modifier)
                            .Sum(item => Math.Abs(item.EffectiveContribution)),
                        events.Any(item => item.IsExtraTurn),
                        events.FirstOrDefault(item => item.Kind == CombatTimelineKind.Turn)?.Side ??
                        TimelineTurnSide.None);
                })
                .ToArray();
        }
    }
}
