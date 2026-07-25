// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed class RunAggregateCache
    {
        private CombatSnapshot? _completedAggregate;
        private string[] _completedCombatIds = [];
        private DashboardDataComponents _components;
        private string _metricSelectionKey = string.Empty;
        private string _runId = string.Empty;

        internal bool RequiresCompletedCombats(
            RunSnapshot run,
            DashboardDataComponents components,
            string metricSelectionKey,
            bool forceRefresh)
        {
            return forceRefresh ||
                   !string.Equals(_runId, run.RunId, StringComparison.Ordinal) ||
                   _components != components ||
                   !string.Equals(_metricSelectionKey, metricSelectionKey, StringComparison.Ordinal);
        }

        internal CombatSnapshot? Combine(
            RunSnapshot run,
            DashboardDataComponents components,
            IReadOnlySet<string>? metricIds,
            string metricSelectionKey,
            bool completedCombatsIncluded)
        {
            if (completedCombatsIncluded)
            {
                var completed = run.Combats.Where(combat => combat.Completed).ToArray();
                var completedIds = completed.Select(combat => combat.CombatId).ToArray();
                if (!Matches(run.RunId, components, metricSelectionKey, completedIds))
                {
                    _runId = run.RunId;
                    _components = components;
                    _metricSelectionKey = metricSelectionKey;
                    _completedCombatIds = completedIds;
                    var projectedCompleted = metricIds == null
                        ? completed
                        : completed.Select(combat => DashboardSnapshotProjector.Project(combat, metricIds)!).ToArray();
                    if (!components.HasFlag(DashboardDataComponents.Events) ||
                        !components.HasFlag(DashboardDataComponents.Timeline))
                        projectedCompleted = projectedCompleted.Select(combat => combat with
                        {
                            Events = components.HasFlag(DashboardDataComponents.Events) ? combat.Events : [],
                            Timeline = components.HasFlag(DashboardDataComponents.Timeline) ? combat.Timeline : [],
                        }).ToArray();
                    _completedAggregate = SnapshotAggregator.Combine(run with { Combats = projectedCompleted });
                }
            }

            var active = run.Combats.Where(combat => !combat.Completed).ToArray();
            if (active.Length == 0)
                return ApplyRunCompletion(_completedAggregate, run);
            var metricCombats = _completedAggregate == null
                ? active
                : [_completedAggregate, .. active];
            var aggregate = SnapshotAggregator.Combine(run with
            {
                Combats = metricCombats,
            }, false, false);
            if (aggregate == null)
                return null;

            var events = components.HasFlag(DashboardDataComponents.Events)
                ? CompositeReadOnlyList<MetricObservation>.Create(
                    new[] { _completedAggregate?.Events }.Concat(active.Select(combat => combat.Events)))
                : [];
            var timeline = components.HasFlag(DashboardDataComponents.Timeline)
                ? CompositeReadOnlyList<CombatTimelineEvent>.Create(
                    new[] { _completedAggregate?.Timeline }.Concat(active.Select(combat => combat.Timeline)))
                : [];
            return aggregate with
            {
                Events = events,
                Timeline = timeline,
            };
        }

        private bool Matches(
            string runId,
            DashboardDataComponents components,
            string metricSelectionKey,
            IReadOnlyList<string> completedCombatIds)
        {
            return string.Equals(_runId, runId, StringComparison.Ordinal) &&
                   _components == components &&
                   string.Equals(_metricSelectionKey, metricSelectionKey, StringComparison.Ordinal) &&
                   _completedCombatIds.SequenceEqual(completedCombatIds, StringComparer.Ordinal);
        }

        private static CombatSnapshot? ApplyRunCompletion(CombatSnapshot? aggregate, RunSnapshot run)
        {
            return aggregate is null
                ? null
                : aggregate with
                {
                    EndedAtUtc = run.EndedAtUtc,
                    Completed = run.EndedAtUtc != null,
                };
        }
    }
}
