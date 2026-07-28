// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal sealed class QueryService(MetricsRepository repository)
    {
        internal MetricsQueryResult Query(MetricsQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            var metricFilter = query.MetricIds is { Count: > 0 }
                ? new HashSet<string>(query.MetricIds, StringComparer.Ordinal)
                : null;
            var live = repository.GetLiveRun(true);
            var candidates = MetricsRepository.GetSavedRunSummaries()
                .Where(run => live == null || run.RunId != live.RunId)
                .Where(run => query.RunId == null || run.RunId == query.RunId)
                .SelectMany(run => run.Combats.Select(combat => new QueryCandidate(run.RunId, combat, false)))
                .ToList();
            if (live != null && (query.RunId == null || live.RunId == query.RunId))
                candidates.AddRange(live.Combats.Select(combat => new QueryCandidate(live.RunId, combat, true)));

            var filteredCandidates = candidates
                .Where(candidate => query.CombatId == null || candidate.Combat.CombatId == query.CombatId)
                .Where(candidate => query.ActIndex == null || candidate.Combat.ActIndex == query.ActIndex)
                .Where(candidate => query.MinimumFloor == null || candidate.Combat.Floor >= query.MinimumFloor)
                .Where(candidate => query.MaximumFloor == null || candidate.Combat.Floor <= query.MaximumFloor)
                .Where(candidate => query.FromUtc == null || candidate.Combat.StartedAtUtc >= query.FromUtc)
                .Where(candidate => query.ToUtc == null || candidate.Combat.StartedAtUtc <= query.ToUtc)
                .OrderByDescending(candidate => candidate.Combat.StartedAtUtc)
                .ToArray();
            var limit = Math.Clamp(query.Limit, 1, 5000);
            var selected = new List<CombatSnapshot>(Math.Min(limit, filteredCandidates.Length));
            var totalMatches = 0;
            foreach (var candidate in filteredCandidates)
            {
                if (query.PlayerNetId == null)
                {
                    totalMatches++;
                    if (selected.Count < limit && Load(candidate) is { } combat)
                        selected.Add(combat);
                    continue;
                }

                var materialized = Load(candidate);
                if (materialized == null ||
                    materialized.Players.All(player => player.PlayerNetId != query.PlayerNetId))
                    continue;
                totalMatches++;
                if (selected.Count < limit)
                    selected.Add(materialized);
            }

            var result = selected
                .Select(combat => FilterCombat(combat, query.PlayerNetId, metricFilter, query.IncludeEvents,
                    query.IncludeTimeline))
                .ToArray();
            var totals = result.SelectMany(combat => combat.Players)
                .SelectMany(player => player.Totals)
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value), StringComparer.Ordinal);
            return new(result, totals, totalMatches, totalMatches > result.Length);

            CombatSnapshot? Load(QueryCandidate candidate)
            {
                if (candidate.IsLive)
                    return candidate.Combat;
                return MetricsRepository.GetSavedRun(
                        candidate.RunId,
                        query.IncludeEvents,
                        query.IncludeTimeline,
                        candidate.Combat.CombatId)
                    ?.Combats.SingleOrDefault();
            }
        }

        internal TimelineQueryResult QueryTimeline(TimelineQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            var combats = Query(new()
            {
                RunId = query.RunId,
                CombatId = query.CombatId,
                PlayerNetId = query.PlayerNetId,
                IncludeEvents = false,
                IncludeTimeline = true,
                Limit = 5000,
            }).Combats;
            var events = combats.SelectMany(combat => combat.Timeline ?? []).OrderBy(evt => evt.OccurredAtUtc)
                .ThenBy(evt => evt.Sequence).ToArray();
            if (!string.IsNullOrWhiteSpace(query.RootEventId))
            {
                var included = DescendantIds(events, query.RootEventId);
                events = events.Where(evt => included.Contains(evt.EventId)).ToArray();
            }

            var kinds = query.Kinds is { Count: > 0 }
                ? new HashSet<CombatTimelineKind>(query.Kinds)
                : null;
            var matches = events
                .Where(evt => query.PlayerNetId == null || evt.Actor?.PlayerNetId == query.PlayerNetId ||
                              evt.Target?.PlayerNetId == query.PlayerNetId ||
                              evt.Damage?.AttributionShares?.Any(share =>
                                  share.Contributor.PlayerNetId == query.PlayerNetId) == true)
                .Where(evt => query.MinimumRound == null || evt.Round >= query.MinimumRound)
                .Where(evt => query.MaximumRound == null || evt.Round <= query.MaximumRound)
                .Where(evt => query.Side == null || evt.Side == query.Side)
                .Where(evt => query.IsExtraTurn == null || evt.IsExtraTurn == query.IsExtraTurn)
                .Where(evt => kinds == null || kinds.Contains(evt.Kind))
                .Where(evt => query.FromUtc == null || evt.OccurredAtUtc >= query.FromUtc)
                .Where(evt => query.ToUtc == null || evt.OccurredAtUtc <= query.ToUtc)
                .ToArray();
            var limit = Math.Clamp(query.Limit, 1, 50000);
            return new(matches.Take(limit).ToArray(), matches.Length, matches.Length > limit);
        }

        private static CombatSnapshot FilterCombat(
            CombatSnapshot combat,
            ulong? playerNetId,
            HashSet<string>? metricFilter,
            bool includeEvents,
            bool includeTimeline)
        {
            var players = combat.Players
                .Where(player => playerNetId == null || player.PlayerNetId == playerNetId)
                .Select(player => FilterPlayer(player, metricFilter))
                .ToArray();
            var events = includeEvents
                ? combat.Events.Where(observation =>
                        (playerNetId == null || observation.Subject.PlayerNetId == playerNetId) &&
                        (metricFilter == null || metricFilter.Contains(observation.MetricId)))
                    .ToArray()
                : [];
            var timeline = includeTimeline
                ? (combat.Timeline ?? []).Where(evt => playerNetId == null || evt.Actor?.PlayerNetId == playerNetId ||
                                                       evt.Target?.PlayerNetId == playerNetId ||
                                                       evt.Damage?.AttributionShares?.Any(share =>
                                                           share.Contributor.PlayerNetId == playerNetId) == true)
                .ToArray()
                : [];
            return combat with { Players = players, Events = events, Timeline = timeline };
        }

        private static PlayerMetricSnapshot FilterPlayer(PlayerMetricSnapshot player, HashSet<string>? metricFilter)
        {
            if (metricFilter == null)
                return player;
            return player with
            {
                Totals = player.Totals.Where(pair => metricFilter.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Sources = player.Sources.Where(pair => metricFilter.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            };
        }

        private static HashSet<string> DescendantIds(IEnumerable<CombatTimelineEvent> events, string rootId)
        {
            var children = events.Where(evt => evt.ParentEventId != null)
                .GroupBy(evt => evt.ParentEventId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(evt => evt.EventId).ToArray(),
                    StringComparer.Ordinal);
            var result = new HashSet<string>(StringComparer.Ordinal) { rootId };
            var pending = new Stack<string>();
            pending.Push(rootId);
            while (pending.TryPop(out var parent))
            {
                if (!children.TryGetValue(parent, out var ids))
                    continue;
                foreach (var id in ids)
                    if (result.Add(id))
                        pending.Push(id);
            }

            return result;
        }

        private sealed record QueryCandidate(string RunId, CombatSnapshot Combat, bool IsLive);
    }
}
