// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class DashboardSnapshotProjector
    {
        internal static RunSnapshot? Project(RunSnapshot? run, IReadOnlySet<string>? metricIds)
        {
            if (run == null || metricIds == null)
                return run;
            return run with
            {
                Combats = run.Combats.Select(combat => Project(combat, metricIds)!).ToArray(),
            };
        }

        internal static CombatSnapshot? Project(CombatSnapshot? combat, IReadOnlySet<string>? metricIds)
        {
            if (combat == null || metricIds == null)
                return combat;
            return combat with
            {
                Players = combat.Players.Select(player => Project(player, metricIds)).ToArray(),
                Events = combat.Events.Where(observation => metricIds.Contains(observation.MetricId)).ToArray(),
            };
        }

        private static PlayerMetricSnapshot Project(
            PlayerMetricSnapshot player,
            IReadOnlySet<string> metricIds)
        {
            return player with
            {
                Totals = player.Totals
                    .Where(item => metricIds.Contains(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                Sources = player.Sources
                    .Where(item => metricIds.Contains(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            };
        }
    }
}
