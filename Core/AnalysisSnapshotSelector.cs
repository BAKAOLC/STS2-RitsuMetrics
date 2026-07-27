// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class AnalysisSnapshotSelector
    {
        internal static IReadOnlySet<string> SummaryMetricIds { get; } =
            new HashSet<string>([MetricIds.DamageDealt], StringComparer.Ordinal);

        internal static RunSnapshot Select(
            RunSnapshot run,
            DashboardDataScope scope,
            DashboardDataComponents components,
            string? combatId)
        {
            if (scope == DashboardDataScope.CurrentRun ||
                components.HasFlag(DashboardDataComponents.RunCombats))
                return run;
            var combat = run.Combats.FirstOrDefault(candidate => candidate.CombatId == combatId);
            return run with
            {
                Combats = combat == null ? [] : [combat],
            };
        }

        internal static RunSnapshot Summarize(RunSnapshot run)
        {
            return run with
            {
                Combats = run.Combats.Select(Summarize).ToArray(),
            };
        }

        private static CombatSnapshot Summarize(CombatSnapshot combat)
        {
            return combat with
            {
                Players = combat.Players.Select(Summarize).ToArray(),
                Events = [],
                Timeline = [],
            };
        }

        private static PlayerMetricSnapshot Summarize(PlayerMetricSnapshot player)
        {
            var damage = player.Totals.GetValueOrDefault(MetricIds.DamageDealt);
            IReadOnlyDictionary<string, decimal> totals = damage == 0m
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [MetricIds.DamageDealt] = damage,
                };
            return player with
            {
                Totals = totals,
                Sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal),
            };
        }
    }
}
