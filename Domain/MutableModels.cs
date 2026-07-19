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

        public static MutableRunSession Restore(RunSnapshot snapshot)
        {
            var session = new MutableRunSession
            {
                RunId = snapshot.RunId,
                StartedAtUtc = snapshot.StartedAtUtc,
                IsMultiplayer = snapshot.IsMultiplayer,
                IsDaily = snapshot.IsDaily,
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

        internal RunSnapshot SnapshotForLiveView()
        {
            return Snapshot(true, true, true);
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
                    combats.AsReadOnly());
            }
        }
    }

    internal sealed class MutableCombatSession
    {
        private readonly List<MetricObservation> _events = [];
        private readonly Lock _gate = new();
        private readonly Dictionary<string, MutablePlayerMetrics> _players = new(StringComparer.Ordinal);
        private readonly List<CombatTimelineEvent> _timeline = [];
        private int _droppedEvents;
        private int _droppedTimelineEvents;

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
            session._timeline.AddRange(source.Timeline ?? []);
            foreach (var player in source.Players)
                session._players[player.PlayerKey] = MutablePlayerMetrics.Restore(player);
            return session;
        }

        public void UpdateRoundCount(int round)
        {
            lock (_gate)
            {
                RoundCount = Math.Max(RoundCount, round);
            }
        }

        public void Complete(DateTimeOffset endedAtUtc)
        {
            lock (_gate)
            {
                EndedAtUtc = endedAtUtc;
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
                if (_events.Count < maxEvents)
                    _events.Add(observation);
                else
                    _droppedEvents++;
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
            lock (_gate)
            {
                var players = _players.Values.Select(p => p.Snapshot())
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
                IReadOnlyList<MetricObservation> events = includeEvents
                    ? _events.ToList().AsReadOnly()
                    : Array.Empty<MetricObservation>();
                IReadOnlyList<CombatTimelineEvent> timeline = includeTimeline
                    ? _timeline.ToList().AsReadOnly()
                    : Array.Empty<CombatTimelineEvent>();
                return new(
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
            }
        }
    }

    internal readonly record struct CaptureBufferDiagnostics(int DroppedObservations, int DroppedTimelineEvents);

    internal sealed class MutablePlayerMetrics(EntityDescriptor player)
    {
        private readonly Dictionary<string, Dictionary<string, MutableSourceMetric>> _sources =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, decimal> _totals = new(StringComparer.Ordinal);

        public static MutablePlayerMetrics Restore(PlayerMetricSnapshot snapshot)
        {
            var metrics = new MutablePlayerMetrics(new(
                snapshot.PlayerKey,
                AnalyticsEntityKind.Player,
                snapshot.PlayerNetId,
                snapshot.CharacterId,
                snapshot.DisplayName,
                snapshot.CharacterId));
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

        public void Add(MetricObservation observation)
        {
            _totals[observation.MetricId] = _totals.GetValueOrDefault(observation.MetricId) + observation.Value;
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

            source.Value += observation.Value;
            source.Occurrences++;
        }

        public PlayerMetricSnapshot Snapshot()
        {
            var sourceValues = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(_sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in _sources)
                sourceValues.Add(metricId, values.Values
                    .Select(source => source.Snapshot())
                    .OrderByDescending(source => source.Value)
                    .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            var sources = new ReadOnlyDictionary<string, IReadOnlyList<SourceMetricSnapshot>>(sourceValues);
            return new(
                player.Key,
                player.PlayerNetId,
                player.DisplayName,
                player.CharacterId,
                new ReadOnlyDictionary<string, decimal>(
                    new Dictionary<string, decimal>(_totals, StringComparer.Ordinal)),
                sources);
        }
    }

    internal sealed class MutableSourceMetric(SourceDescriptor source)
    {
        public decimal Value { get; set; }
        public int Occurrences { get; set; }

        public static MutableSourceMetric Restore(SourceMetricSnapshot snapshot)
        {
            return new(new(snapshot.SourceKey, snapshot.SourceKind, snapshot.ModelId,
                snapshot.DisplayName))
            {
                Value = snapshot.Value,
                Occurrences = snapshot.Occurrences,
            };
        }

        public SourceMetricSnapshot Snapshot()
        {
            return new(source.Key, source.Kind, source.ModelId, source.DisplayName, Value,
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
