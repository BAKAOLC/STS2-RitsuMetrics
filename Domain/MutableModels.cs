// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Domain
{
    internal sealed class MutableRunSession
    {
        private readonly List<CombatSnapshot> _completedCombats = [];
        private readonly Lock _gate = new();
        private MutableCombatSession? _activeCombat;

        public required string RunId { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset? EndedAtUtc { get; set; }
        public bool IsMultiplayer { get; init; }
        public bool IsDaily { get; init; }
        public bool? IsVictory { get; set; }
        public bool? IsAbandoned { get; set; }
        public RunIdentitySnapshot? Identity { get; init; }

        public MutableCombatSession? GetActiveCombat()
        {
            lock (_gate)
            {
                return _activeCombat;
            }
        }

        public bool HasAnyCombat()
        {
            lock (_gate)
            {
                return _activeCombat != null || _completedCombats.Count > 0;
            }
        }

        public void SetActiveCombat(MutableCombatSession combat)
        {
            ArgumentNullException.ThrowIfNull(combat);
            lock (_gate)
            {
                _activeCombat = combat;
            }
        }

        public void DiscardActiveCombat()
        {
            lock (_gate)
            {
                _activeCombat = null;
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                EndedAtUtc = null;
                IsVictory = null;
                IsAbandoned = null;
            }
        }

        public static MutableRunSession Restore(RunSnapshot snapshot, RunIdentitySnapshot? identity = null)
        {
            var session = new MutableRunSession
            {
                RunId = snapshot.RunId,
                StartedAtUtc = snapshot.StartedAtUtc,
                IsMultiplayer = snapshot.IsMultiplayer,
                IsDaily = snapshot.IsDaily,
                Identity = identity ?? snapshot.Identity,
            };
            var active = snapshot.Combats.LastOrDefault(combat => combat is
                { Completed: false, EndedAtUtc: null });
            session._completedCombats.AddRange(snapshot.Combats
                .Where(combat => !ReferenceEquals(combat, active) && combat is
                    { Completed: true } or { EndedAtUtc: not null })
                .Select(combat => SnapshotCloner.Clone(combat, true)));
            if (active != null)
                session._activeCombat = MutableCombatSession.Restore(active);
            return session;
        }

        public CombatSnapshot? CompleteActiveCombat(DateTimeOffset endedAtUtc)
        {
            lock (_gate)
            {
                if (_activeCombat == null)
                    return null;
                _activeCombat.Complete(endedAtUtc);
                var snapshot = _activeCombat.Snapshot(true);
                _completedCombats.Add(snapshot);
                _activeCombat = null;
                return snapshot;
            }
        }

        public void CompleteRun(DateTimeOffset endedAtUtc, bool? isVictory, bool? isAbandoned)
        {
            lock (_gate)
            {
                EndedAtUtc = endedAtUtc;
                IsVictory = isVictory;
                IsAbandoned = isAbandoned;
            }
        }

        public RunSnapshot Snapshot(bool includeEvents)
        {
            return Snapshot(includeEvents, true, false);
        }

        internal RunSnapshot Snapshot(bool includeEvents, bool includeTimeline)
        {
            return Snapshot(includeEvents, includeTimeline, false);
        }

        internal RunSnapshot SnapshotForLiveView(
            bool includeEvents,
            bool includeTimeline,
            bool includeCompletedCombats,
            bool projectCompletedCombats,
            IReadOnlySet<string>? metricIds,
            string? selectedCombatId = null)
        {
            lock (_gate)
            {
                var combats = new List<CombatSnapshot>(_completedCombats.Count + (_activeCombat == null ? 0 : 1));
                var completedCombats = includeCompletedCombats
                    ? _completedCombats.AsEnumerable()
                    : selectedCombatId == null
                        ? []
                        : _completedCombats.Where(combat => combat.CombatId == selectedCombatId);
                if (projectCompletedCombats)
                    combats.AddRange(completedCombats.Select(combat =>
                        Project(combat, includeEvents, includeTimeline)));
                else
                    combats.AddRange(completedCombats);

                if (_activeCombat != null &&
                    (selectedCombatId == null || _activeCombat.CombatId == selectedCombatId))
                    combats.Add(_activeCombat.Snapshot(includeEvents, includeTimeline, metricIds));
                return new(RunId, StartedAtUtc, EndedAtUtc, IsMultiplayer, IsDaily, IsVictory, IsAbandoned,
                    combats.AsReadOnly())
                {
                    Identity = Identity,
                };
            }

            static CombatSnapshot Project(CombatSnapshot combat, bool events, bool timeline)
            {
                if (events && timeline)
                    return combat;
                return combat with
                {
                    Events = events ? combat.Events : [],
                    Timeline = timeline ? combat.Timeline : [],
                };
            }
        }

        private RunSnapshot Snapshot(bool includeEvents, bool includeTimeline, bool reuseCompletedCombats)
        {
            lock (_gate)
            {
                var combats = new List<CombatSnapshot>(_completedCombats.Count + (_activeCombat == null ? 0 : 1));
                if (reuseCompletedCombats)
                    combats.AddRange(_completedCombats);
                else
                    combats.AddRange(_completedCombats.Select(combat =>
                        SnapshotCloner.Clone(combat, includeEvents, includeTimeline)));
                if (_activeCombat != null)
                    combats.Add(_activeCombat.Snapshot(includeEvents, includeTimeline));
                return new(RunId, StartedAtUtc, EndedAtUtc, IsMultiplayer, IsDaily, IsVictory, IsAbandoned,
                    combats.AsReadOnly())
                {
                    Identity = Identity,
                };
            }
        }
    }

    internal sealed class MutableCombatSession
    {
        private readonly Dictionary<string, CachedEventSnapshot> _cachedEventSnapshots =
            new(StringComparer.Ordinal);

        private readonly Dictionary<CombatSnapshotCacheKey, CachedCombatSnapshot> _cachedSnapshots = [];
        private readonly Dictionary<string, long> _eventMetricRevisions = new(StringComparer.Ordinal);

        private readonly AppendOnlySnapshotBuffer<MetricObservation> _events = new();

        private readonly Dictionary<string, AppendOnlySnapshotBuffer<MetricObservation>> _eventsByMetric =
            new(StringComparer.Ordinal);

        private readonly Lock _gate = new();
        private readonly Dictionary<string, long> _metricRevisions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MutablePlayerMetrics> _players = new(StringComparer.Ordinal);
        private readonly AppendOnlySnapshotBuffer<CombatTimelineEvent> _timeline = new();
        private int _droppedEvents;
        private int _droppedTimelineEvents;
        private long _eventRevision;
        private long _metadataRevision;
        private long _metricRevision;

        public required string RunId { get; init; }
        public required string CombatId { get; init; }
        public int ActIndex { get; init; }
        public int Floor { get; init; }
        public string EncounterId { get; set; } = string.Empty;
        public string EncounterName { get; set; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset? EndedAtUtc { get; private set; }
        public int RoundCount { get; private set; }

        public static MutableCombatSession Restore(CombatSnapshot snapshot)
        {
            var source = SnapshotCloner.Clone(snapshot, true);
            var session = new MutableCombatSession
            {
                RunId = source.RunId,
                CombatId = source.CombatId,
                ActIndex = source.ActIndex,
                Floor = source.Floor,
                EncounterId = source.EncounterId,
                EncounterName = source.EncounterName,
                StartedAtUtc = source.StartedAtUtc,
                RoundCount = source.RoundCount,
            };
            session._events.AddRange(source.Events);
            foreach (var observation in source.Events)
            {
                if (!session._eventsByMetric.TryGetValue(observation.MetricId, out var metricEvents))
                {
                    metricEvents = new();
                    session._eventsByMetric.Add(observation.MetricId, metricEvents);
                }

                metricEvents.Add(observation);
            }

            session._timeline.AddRange(source.Timeline ?? []);
            foreach (var player in source.Players)
                session._players[player.PlayerKey] = MutablePlayerMetrics.Restore(player);
            return session;
        }

        public void UpdateRoundCount(int round)
        {
            lock (_gate)
            {
                var next = Math.Max(RoundCount, round);
                if (RoundCount == next)
                    return;
                RoundCount = next;
                _metadataRevision++;
            }
        }

        public void Complete(DateTimeOffset endedAtUtc)
        {
            lock (_gate)
            {
                if (EndedAtUtc == endedAtUtc)
                    return;
                EndedAtUtc = endedAtUtc;
                _metadataRevision++;
            }
        }

        public void Add(MetricObservation observation, int maxEvents)
        {
            lock (_gate)
            {
                if (!_players.TryGetValue(observation.Subject.Key, out var player))
                {
                    player = new(observation.Subject);
                    _players.Add(observation.Subject.Key, player);
                }

                player.Add(observation);
                _metricRevision++;
                _metricRevisions[observation.MetricId] = _metricRevision;
                if (_events.Count < maxEvents)
                {
                    _events.Add(observation);
                    if (!_eventsByMetric.TryGetValue(observation.MetricId, out var metricEvents))
                    {
                        metricEvents = new();
                        _eventsByMetric.Add(observation.MetricId, metricEvents);
                    }

                    metricEvents.Add(observation);
                    _eventRevision++;
                    _eventMetricRevisions[observation.MetricId] = _eventRevision;
                }
                else
                {
                    _droppedEvents++;
                }
            }
        }

        internal void InitializePlayer(EntityDescriptor player, string identityColor)
        {
            lock (_gate)
            {
                if (_players.TryGetValue(player.Key, out var existing))
                {
                    if (existing.EnsureIdentityColor(identityColor))
                        _metadataRevision++;
                    return;
                }

                _players.Add(player.Key, new(player, identityColor));
                _metadataRevision++;
            }
        }

        public void AddTimeline(CombatTimelineEvent timelineEvent, int maxEvents)
        {
            lock (_gate)
            {
                if (_timeline.Count < maxEvents)
                    _timeline.Add(timelineEvent);
                else
                    _droppedTimelineEvents++;
            }
        }

        public CaptureBufferDiagnostics GetCaptureBufferDiagnostics()
        {
            lock (_gate)
            {
                return new(_droppedEvents, _droppedTimelineEvents);
            }
        }

        public CombatSnapshot Snapshot(bool includeEvents)
        {
            return Snapshot(includeEvents, true);
        }

        internal CombatSnapshot Snapshot(bool includeEvents, bool includeTimeline)
        {
            return Snapshot(includeEvents, includeTimeline, null);
        }

        internal CombatSnapshot Snapshot(
            bool includeEvents,
            bool includeTimeline,
            IReadOnlySet<string>? metricIds)
        {
            lock (_gate)
            {
                var metricSelectionKey = SelectionKey(metricIds);
                var cacheKey = new CombatSnapshotCacheKey(includeEvents, includeTimeline, metricSelectionKey);
                var revision = new CombatSnapshotRevision(
                    RevisionFor(_metricRevision, _metricRevisions, metricIds),
                    includeEvents
                        ? RevisionFor(_eventRevision, _eventMetricRevisions, metricIds)
                        : 0,
                    includeTimeline ? _timeline.Revision : 0,
                    _metadataRevision);
                if (_cachedSnapshots.TryGetValue(cacheKey, out var cached) && cached.Revision == revision)
                    return cached.Snapshot;

                var players = _players.Values.Select(p => p.Snapshot(metricIds))
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
                var events = includeEvents ? EventSnapshot(metricIds, metricSelectionKey) : [];
                var timeline = includeTimeline
                    ? _timeline.GetSnapshot()
                    : [];
                var snapshot = new CombatSnapshot(
                    RunId,
                    CombatId,
                    ActIndex,
                    Floor,
                    EncounterId,
                    EncounterName,
                    StartedAtUtc,
                    EndedAtUtc,
                    EndedAtUtc != null,
                    RoundCount,
                    players,
                    events,
                    timeline);
                if (_cachedSnapshots.Count >= 32)
                    _cachedSnapshots.Clear();
                _cachedSnapshots[cacheKey] = new(revision, snapshot);
                return snapshot;
            }
        }

        private IReadOnlyList<MetricObservation> EventSnapshot(
            IReadOnlySet<string>? metricIds,
            string metricSelectionKey)
        {
            if (metricIds == null)
                return _events.GetSnapshot();
            if (metricIds.Count == 1)
                return _eventsByMetric.TryGetValue(metricIds.First(), out var metricEvents)
                    ? metricEvents.GetSnapshot()
                    : [];

            var revision = RevisionFor(_eventRevision, _eventMetricRevisions, metricIds);
            if (_cachedEventSnapshots.TryGetValue(metricSelectionKey, out var cached) &&
                cached.Revision == revision)
                return cached.Events;

            var events = _events.GetSnapshot().Where(observation => metricIds.Contains(observation.MetricId))
                .ToArray();
            if (_cachedEventSnapshots.Count >= 16)
                _cachedEventSnapshots.Clear();
            _cachedEventSnapshots[metricSelectionKey] = new(revision, events);
            return events;
        }

        private static long RevisionFor(
            long allRevision,
            IReadOnlyDictionary<string, long> revisions,
            IReadOnlySet<string>? metricIds)
        {
            return metricIds == null
                ? allRevision
                : metricIds.Select(id => revisions.GetValueOrDefault(id)).DefaultIfEmpty().Max();
        }

        private static string SelectionKey(IReadOnlySet<string>? metricIds)
        {
            return metricIds == null ? "*" : string.Join('\u001f', metricIds.Order(StringComparer.Ordinal));
        }

        private readonly record struct CombatSnapshotCacheKey(
            bool IncludeEvents,
            bool IncludeTimeline,
            string MetricSelectionKey);

        private readonly record struct CombatSnapshotRevision(
            long Metrics,
            long Events,
            long Timeline,
            long Metadata);

        private readonly record struct CachedCombatSnapshot(
            CombatSnapshotRevision Revision,
            CombatSnapshot Snapshot);

        private readonly record struct CachedEventSnapshot(
            long Revision,
            IReadOnlyList<MetricObservation> Events);
    }

    internal readonly record struct CaptureBufferDiagnostics(int DroppedObservations, int DroppedTimelineEvents);

    internal sealed class MutablePlayerMetrics(EntityDescriptor player, string identityColor = "")
    {
        private readonly Dictionary<string, CachedPlayerSnapshot> _cachedSnapshots = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _metricRevisions = new(StringComparer.Ordinal);

        private readonly Dictionary<string, Dictionary<string, MutableSourceMetric>> _sources =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, decimal> _totals = new(StringComparer.Ordinal);
        private string _identityColor = identityColor;
        private long _revision;

        public static MutablePlayerMetrics Restore(PlayerMetricSnapshot snapshot)
        {
            var metrics = new MutablePlayerMetrics(new(
                snapshot.PlayerKey,
                AnalyticsEntityKind.Player,
                snapshot.PlayerNetId,
                snapshot.CharacterId,
                snapshot.DisplayName,
                snapshot.CharacterId), snapshot.IdentityColor);
            foreach (var (metricId, value) in snapshot.Totals)
                metrics._totals[metricId] = value;
            foreach (var (metricId, sources) in snapshot.Sources)
            {
                var restored = new Dictionary<string, MutableSourceMetric>(StringComparer.Ordinal);
                foreach (var source in sources)
                    restored[source.SourceKey] = MutableSourceMetric.Restore(source);
                metrics._sources[metricId] = restored;
            }

            return metrics;
        }

        internal bool EnsureIdentityColor(string value)
        {
            if (!string.IsNullOrWhiteSpace(_identityColor) || string.IsNullOrWhiteSpace(value))
                return false;
            _identityColor = value;
            _revision++;
            _cachedSnapshots.Clear();
            return true;
        }

        public void Add(MetricObservation observation)
        {
            _totals[observation.MetricId] = _totals.GetValueOrDefault(observation.MetricId) + observation.Value;
            _revision++;
            _metricRevisions[observation.MetricId] = _revision;
            if (!_sources.TryGetValue(observation.MetricId, out var bySource))
            {
                bySource = new(StringComparer.Ordinal);
                _sources.Add(observation.MetricId, bySource);
            }

            if (!bySource.TryGetValue(observation.Source.Key, out var source))
            {
                source = new(observation.Source);
                bySource.Add(observation.Source.Key, source);
            }

            source.Add(observation.Value);
        }

        public PlayerMetricSnapshot Snapshot()
        {
            return Snapshot(null);
        }

        internal PlayerMetricSnapshot Snapshot(IReadOnlySet<string>? metricIds)
        {
            var selectionKey = metricIds == null
                ? "*"
                : string.Join('\u001f', metricIds.Order(StringComparer.Ordinal));
            var revision = metricIds == null
                ? _revision
                : metricIds.Select(id => _metricRevisions.GetValueOrDefault(id)).DefaultIfEmpty().Max();
            if (_cachedSnapshots.TryGetValue(selectionKey, out var cached) && cached.Revision == revision)
                return cached.Snapshot;

            var sourceValues = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(_sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in _sources)
            {
                if (metricIds != null && !metricIds.Contains(metricId))
                    continue;
                sourceValues.Add(metricId, values.Values
                    .Select(source => source.Snapshot())
                    .OrderByDescending(source => source.Value)
                    .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            }

            var sources = new ReadOnlyDictionary<string, IReadOnlyList<SourceMetricSnapshot>>(sourceValues);
            var totals = metricIds == null
                ? new(_totals, StringComparer.Ordinal)
                : _totals.Where(item => metricIds.Contains(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var snapshot = new PlayerMetricSnapshot(
                player.Key,
                player.PlayerNetId,
                player.DisplayName,
                player.CharacterId,
                new ReadOnlyDictionary<string, decimal>(totals),
                sources,
                _identityColor);
            if (_cachedSnapshots.Count >= 16)
                _cachedSnapshots.Clear();
            _cachedSnapshots[selectionKey] = new(revision, snapshot);
            return snapshot;
        }

        private readonly record struct CachedPlayerSnapshot(long Revision, PlayerMetricSnapshot Snapshot);
    }

    internal sealed class MutableSourceMetric(SourceDescriptor source)
    {
        private SourceMetricSnapshot? _snapshot;

        public decimal Value { get; private set; }
        public int Occurrences { get; private set; }

        public static MutableSourceMetric Restore(SourceMetricSnapshot snapshot)
        {
            return new(new(snapshot.SourceKey, snapshot.SourceKind, snapshot.ModelId,
                snapshot.DisplayName))
            {
                Value = snapshot.Value,
                Occurrences = snapshot.Occurrences,
            };
        }

        internal void Add(decimal value)
        {
            Value += value;
            Occurrences++;
            _snapshot = null;
        }

        public SourceMetricSnapshot Snapshot()
        {
            return _snapshot ??= new(source.Key, source.Kind, source.ModelId, source.DisplayName, Value,
                Occurrences);
        }
    }

    internal static class SnapshotCloner
    {
        public static CombatSnapshot Clone(CombatSnapshot source, bool includeEvents)
        {
            return Clone(source, includeEvents, true);
        }

        internal static CombatSnapshot Clone(CombatSnapshot source, bool includeEvents, bool includeTimeline)
        {
            var players = source.Players.Select(Clone).ToList().AsReadOnly();
            IReadOnlyList<MetricObservation> events = includeEvents
                ? source.Events.Select(Clone).ToList().AsReadOnly()
                : Array.Empty<MetricObservation>();
            IReadOnlyList<CombatTimelineEvent> timeline = includeTimeline
                ? (source.Timeline ?? []).Select(Clone).ToList().AsReadOnly()
                : Array.Empty<CombatTimelineEvent>();
            return source with { Players = players, Events = events, Timeline = timeline };
        }

        public static RunSnapshot Clone(RunSnapshot source, bool includeEvents)
        {
            return Clone(source, includeEvents, true);
        }

        internal static RunSnapshot Clone(RunSnapshot source, bool includeEvents, bool includeTimeline)
        {
            return source with
            {
                Combats = source.Combats.Select(combat => Clone(combat, includeEvents, includeTimeline)).ToList()
                    .AsReadOnly(),
            };
        }

        private static PlayerMetricSnapshot Clone(PlayerMetricSnapshot source)
        {
            var totals = new ReadOnlyDictionary<string, decimal>(
                new Dictionary<string, decimal>(source.Totals, StringComparer.Ordinal));
            var sourceValues = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(source.Sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in source.Sources)
                sourceValues.Add(metricId, values.ToArray());
            var sources = new ReadOnlyDictionary<string, IReadOnlyList<SourceMetricSnapshot>>(sourceValues);
            return source with { Totals = totals, Sources = sources };
        }

        private static MetricObservation Clone(MetricObservation source)
        {
            var tags = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(source.Tags, StringComparer.Ordinal));
            return source with { Tags = tags };
        }

        private static CombatTimelineEvent Clone(CombatTimelineEvent source)
        {
            var details = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(source.Details, StringComparer.Ordinal));
            var damage = source.Damage == null
                ? null
                : source.Damage with
                {
                    Contributions = source.Damage.Contributions.ToList().AsReadOnly(),
                    AttributionShares = source.Damage.AttributionShares?.ToList().AsReadOnly(),
                };
            return source with { Details = details, Damage = damage };
        }
    }
}
