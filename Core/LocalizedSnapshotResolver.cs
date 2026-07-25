// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Core
{
    internal static class LocalizedSnapshotResolver
    {
        private const int MaximumIncrementalCombatCaches = 128;
        private static readonly Lock Gate = new();

        private static readonly Dictionary<CombatCacheKey, ProjectionState<MetricObservation, MetricObservation>>
            EventStates = [];

        private static readonly Queue<CombatCacheKey> EventStateOrder = [];
        private static readonly Dictionary<CombatCacheKey, TimelineProjectionState> TimelineStates = [];
        private static readonly Queue<CombatCacheKey> TimelineStateOrder = [];
        private static ConditionalWeakTable<RunSnapshot, LocalizedPair<RunSnapshot>> _runs = new();
        private static ConditionalWeakTable<CombatSnapshot, LocalizedPair<CombatSnapshot>> _combats = new();

        private static ConditionalWeakTable<PlayerMetricSnapshot, LocalizedPair<PlayerMetricSnapshot>> _players =
            new();

        private static ConditionalWeakTable<SourceMetricSnapshot, Box<SourceMetricSnapshot>> _sourceMetrics = new();

        private static ConditionalWeakTable<MetricObservation, LocalizedPair<MetricObservation>> _observations =
            new();

        private static ConditionalWeakTable<EntityDescriptor, LocalizedPair<EntityDescriptor>> _entities = new();
        private static ConditionalWeakTable<SourceDescriptor, Box<SourceDescriptor>> _sources = new();

        internal static void ClearCaches()
        {
            lock (Gate)
            {
                _runs = new();
                _combats = new();
                _players = new();
                _sourceMetrics = new();
                _observations = new();
                _entities = new();
                _sources = new();
                EventStates.Clear();
                EventStateOrder.Clear();
                TimelineStates.Clear();
                TimelineStateOrder.Clear();
            }
        }

        internal static RunSnapshot Resolve(RunSnapshot run)
        {
            lock (Gate)
            {
                var pair = _runs.GetOrCreateValue(run);
                var localizePlayers = !run.IsMultiplayer;
                ref var cached = ref pair.For(localizePlayers);
                return cached ??= run with
                {
                    Combats = run.Combats.Select(combat => ResolveCore(combat, localizePlayers)).ToArray(),
                };
            }
        }

        internal static CombatSnapshot Resolve(CombatSnapshot combat, bool localizePlayers)
        {
            lock (Gate)
            {
                return ResolveCore(combat, localizePlayers);
            }
        }

        private static CombatSnapshot ResolveCore(CombatSnapshot combat, bool localizePlayers)
        {
            var pair = _combats.GetOrCreateValue(combat);
            ref var cached = ref pair.For(localizePlayers);
            if (cached != null)
                return cached;

            var encounterName = LocalizedModelNameResolver.ResolveEncounter(combat.EncounterId,
                combat.EncounterName);
            var key = new CombatCacheKey(combat.RunId, combat.CombatId, localizePlayers);
            var eventState = GetProjectionState(EventStates, EventStateOrder, key);
            var timelineState = GetTimelineState(key);
            return cached = combat with
            {
                EncounterName = encounterName,
                Players = combat.Players.Select(player => ResolvePlayer(player, localizePlayers)).ToArray(),
                Events = eventState.Project(combat.Events,
                    observation => ResolveObservation(observation, localizePlayers)),
                Timeline = combat.Timeline == null
                    ? null
                    : timelineState.Project(combat.Timeline, localizePlayers, encounterName),
            };
        }

        private static PlayerMetricSnapshot ResolvePlayer(PlayerMetricSnapshot player, bool localizePlayer)
        {
            var pair = _players.GetOrCreateValue(player);
            ref var cached = ref pair.For(localizePlayer);
            if (cached != null)
                return cached;

            var sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(player.Sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in player.Sources)
                sources.Add(metricId, values.Select(ResolveSourceMetric).ToArray());

            if (!localizePlayer)
                return cached = player with { Sources = sources };

            var characterId = string.IsNullOrWhiteSpace(player.CharacterId)
                ? player.PlayerKey
                : player.CharacterId;
            var displayName = LocalizedModelNameResolver.Resolve(AnalyticsSourceKind.Character, characterId,
                player.DisplayName);

            return cached = player with { DisplayName = displayName, Sources = sources };
        }

        private static SourceMetricSnapshot ResolveSourceMetric(SourceMetricSnapshot source)
        {
            return _sourceMetrics.GetValue(source, value => new(value with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(value.SourceKind, value.ModelId,
                    value.DisplayName),
            })).Value;
        }

        private static MetricObservation ResolveObservation(MetricObservation observation, bool localizePlayers)
        {
            var pair = _observations.GetOrCreateValue(observation);
            ref var cached = ref pair.For(localizePlayers);
            return cached ??= observation with
            {
                Subject = ResolveEntity(observation.Subject, localizePlayers),
                Target = observation.Target is null ? null : ResolveEntity(observation.Target, localizePlayers),
                Source = ResolveSource(observation.Source),
                Tags = ResolveTags(observation.Tags, localizePlayers),
            };
        }

        private static CombatTimelineEvent ResolveTimelineEvent(
            CombatTimelineEvent timelineEvent,
            bool localizePlayers,
            string encounterName)
        {
            var actor = timelineEvent.Actor is null ? null : ResolveEntity(timelineEvent.Actor, localizePlayers);
            var target = timelineEvent.Target is null ? null : ResolveEntity(timelineEvent.Target, localizePlayers);
            var source = timelineEvent.Source is null ? null : ResolveSource(timelineEvent.Source);
            var displayText = timelineEvent.DisplayText;
            if (timelineEvent.ActionId is "combat.start" or "combat.resume" or "combat.end")
                displayText = encounterName;
            else if (Matches(displayText, timelineEvent.Source))
                displayText = source!.DisplayName;
            else if (Matches(displayText, timelineEvent.Target))
                displayText = target!.DisplayName;
            else if (Matches(displayText, timelineEvent.Actor))
                displayText = actor!.DisplayName;

            return timelineEvent with
            {
                DisplayText = displayText,
                Actor = actor,
                Target = target,
                Source = source,
                Damage = timelineEvent.Damage is null
                    ? null
                    : ResolveDamage(timelineEvent.Damage, localizePlayers),
            };
        }

        private static bool Matches(string displayText, SourceDescriptor? source)
        {
            return source != null &&
                   (string.Equals(displayText, source.DisplayName, StringComparison.Ordinal) ||
                    string.Equals(displayText, source.ModelId, StringComparison.Ordinal));
        }

        private static bool Matches(string displayText, EntityDescriptor? entity)
        {
            return entity != null &&
                   (string.Equals(displayText, entity.DisplayName, StringComparison.Ordinal) ||
                    string.Equals(displayText, entity.ModelId, StringComparison.Ordinal));
        }

        private static DamageBreakdown ResolveDamage(DamageBreakdown damage, bool localizePlayers)
        {
            return damage with
            {
                Contributions = damage.Contributions.Select(contribution => contribution with
                {
                    Source = ResolveSource(contribution.Source),
                }).ToArray(),
                AttributionShares = damage.AttributionShares?.Select(share => share with
                {
                    Contributor = ResolveEntity(share.Contributor, localizePlayers),
                    Source = ResolveSource(share.Source),
                }).ToArray(),
            };
        }

        private static EntityDescriptor ResolveEntity(EntityDescriptor entity, bool localizePlayers)
        {
            var pair = _entities.GetOrCreateValue(entity);
            ref var cached = ref pair.For(localizePlayers);
            if (cached != null)
                return cached;
            var sourceKind = entity.Kind switch
            {
                AnalyticsEntityKind.Monster or AnalyticsEntityKind.Summon => AnalyticsSourceKind.Creature,
                AnalyticsEntityKind.Player when localizePlayers => AnalyticsSourceKind.Character,
                _ => AnalyticsSourceKind.Unknown,
            };
            var modelId = entity.Kind == AnalyticsEntityKind.Player && !string.IsNullOrWhiteSpace(entity.CharacterId)
                ? entity.CharacterId
                : entity.ModelId;
            return cached = entity with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(sourceKind, modelId, entity.DisplayName),
            };
        }

        private static SourceDescriptor ResolveSource(SourceDescriptor source)
        {
            return _sources.GetValue(source, value => new(value with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(value.Kind, value.ModelId, value.DisplayName),
            })).Value;
        }

        private static IReadOnlyDictionary<string, string> ResolveTags(
            IReadOnlyDictionary<string, string> tags,
            bool localizePlayers)
        {
            if (!tags.TryGetValue(ObservationTagIds.ActorKind, out var kindText) ||
                !Enum.TryParse<AnalyticsEntityKind>(kindText, out var kind) ||
                !tags.TryGetValue(ObservationTagIds.ActorModelId, out var modelId) ||
                !tags.ContainsKey(ObservationTagIds.ActorDisplayName))
                return tags;

            var sourceKind = kind switch
            {
                AnalyticsEntityKind.Monster or AnalyticsEntityKind.Summon => AnalyticsSourceKind.Creature,
                AnalyticsEntityKind.Player when localizePlayers => AnalyticsSourceKind.Character,
                _ => AnalyticsSourceKind.Unknown,
            };
            if (!LocalizedModelNameResolver.TryResolve(sourceKind, modelId, out var resolved))
                return tags;

            var copy = new Dictionary<string, string>(tags, StringComparer.Ordinal)
            {
                [ObservationTagIds.ActorDisplayName] = resolved,
            };
            return copy;
        }

        private static ProjectionState<TSource, TTarget> GetProjectionState<TSource, TTarget>(
            Dictionary<CombatCacheKey, ProjectionState<TSource, TTarget>> states,
            Queue<CombatCacheKey> order,
            CombatCacheKey key)
            where TSource : class
        {
            if (states.TryGetValue(key, out var state))
                return state;
            Trim(states, order);
            state = new();
            states.Add(key, state);
            order.Enqueue(key);
            return state;
        }

        private static TimelineProjectionState GetTimelineState(CombatCacheKey key)
        {
            if (TimelineStates.TryGetValue(key, out var state))
                return state;
            Trim(TimelineStates, TimelineStateOrder);
            state = new();
            TimelineStates.Add(key, state);
            TimelineStateOrder.Enqueue(key);
            return state;
        }

        private static void Trim<T>(Dictionary<CombatCacheKey, T> states, Queue<CombatCacheKey> order)
        {
            while (states.Count >= MaximumIncrementalCombatCaches && order.TryDequeue(out var oldest))
                states.Remove(oldest);
        }

        private sealed class TimelineProjectionState
        {
            private readonly Dictionary<string, CombatTimelineEvent> _byId = new(StringComparer.Ordinal);
            private readonly HashSet<string> _missingOriginIds = new(StringComparer.Ordinal);
            private CombatTimelineEvent? _lastSource;
            private AppendOnlySnapshotBuffer<CombatTimelineEvent> _output = new();
            private int _projectedCount;

            internal IReadOnlyList<CombatTimelineEvent> Project(
                IReadOnlyList<CombatTimelineEvent> source,
                bool localizePlayers,
                string encounterName)
            {
                if (!CanAppend(source) || ContainsPreviouslyMissingOrigin(source))
                    Reset();
                if (_projectedCount == source.Count)
                    return _output.GetSnapshot();

                var pending = new CombatTimelineEvent[source.Count - _projectedCount];
                for (var index = _projectedCount; index < source.Count; index++)
                {
                    var resolved = ResolveTimelineEvent(source[index], localizePlayers, encounterName);
                    pending[index - _projectedCount] = resolved;
                    _byId.TryAdd(resolved.EventId, resolved);
                }

                foreach (var timelineEvent in pending)
                {
                    var resolved = ResolveCausalSource(timelineEvent);
                    _output.Add(resolved);
                    _byId[timelineEvent.EventId] = resolved;
                }

                _projectedCount = source.Count;
                _lastSource = source.Count == 0 ? null : source[^1];
                return _output.GetSnapshot();
            }

            private bool CanAppend(IReadOnlyList<CombatTimelineEvent> source)
            {
                return _projectedCount == 0 ||
                       (source.Count >= _projectedCount &&
                        ReferenceEquals(source[_projectedCount - 1], _lastSource));
            }

            private bool ContainsPreviouslyMissingOrigin(IReadOnlyList<CombatTimelineEvent> source)
            {
                if (_missingOriginIds.Count == 0)
                    return false;
                for (var index = _projectedCount; index < source.Count; index++)
                    if (_missingOriginIds.Contains(source[index].EventId))
                        return true;
                return false;
            }

            private CombatTimelineEvent ResolveCausalSource(CombatTimelineEvent timelineEvent)
            {
                if (!timelineEvent.Details.TryGetValue("origin_event_id", out var originEventId) ||
                    !timelineEvent.Details.ContainsKey("cause_source_name"))
                    return timelineEvent;
                if (!_byId.TryGetValue(originEventId, out var origin))
                {
                    _missingOriginIds.Add(originEventId);
                    return timelineEvent;
                }

                if (string.IsNullOrWhiteSpace(origin.Source?.DisplayName))
                    return timelineEvent;
                var details = new Dictionary<string, string>(timelineEvent.Details, StringComparer.Ordinal)
                {
                    ["cause_source_name"] = origin.Source.DisplayName,
                };
                return timelineEvent with { Details = details };
            }

            private void Reset()
            {
                _byId.Clear();
                _missingOriginIds.Clear();
                _output = new();
                _projectedCount = 0;
                _lastSource = null;
            }
        }

        private sealed class ProjectionState<TSource, TTarget> where TSource : class
        {
            private TSource? _lastSource;
            private AppendOnlySnapshotBuffer<TTarget> _output = new();
            private int _projectedCount;

            internal IReadOnlyList<TTarget> Project(
                IReadOnlyList<TSource> source,
                Func<TSource, TTarget> projector)
            {
                if (_projectedCount > 0 &&
                    (source.Count < _projectedCount ||
                     !ReferenceEquals(source[_projectedCount - 1], _lastSource)))
                {
                    _output = new();
                    _projectedCount = 0;
                    _lastSource = null;
                }

                for (var index = _projectedCount; index < source.Count; index++)
                    _output.Add(projector(source[index]));
                _projectedCount = source.Count;
                _lastSource = source.Count == 0 ? null : source[^1];
                return _output.GetSnapshot();
            }
        }

        private sealed class LocalizedPair<T> where T : class
        {
            private T? _localizedPlayers;
            private T? _preservedPlayers;

            internal ref T? For(bool localizePlayers)
            {
                if (localizePlayers)
                    return ref _localizedPlayers;
                return ref _preservedPlayers;
            }
        }

        private sealed class Box<T>(T value)
        {
            internal T Value { get; } = value;
        }

        private readonly record struct CombatCacheKey(string RunId, string CombatId, bool LocalizePlayers);
    }
}
