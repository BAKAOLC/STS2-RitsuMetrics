// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Data.Models
{
    [JsonConverter(typeof(HistoryArchiveJsonConverter))]
    public sealed class HistoryArchive
    {
        public const int CurrentDataVersion = 1;
        internal const string CurrentStorageFormat = "combat-files-v2";

        private readonly Lock _gate = new();
        private readonly HashSet<string> _mutatedRunIds = new(StringComparer.Ordinal);
        private Func<CombatSnapshot, CombatSnapshot>? _combatLoader;
        private Func<CombatSnapshot, CombatSnapshot>? _combatSummaryLoader;
        private bool _discardPendingRuns;
        private Action? _loadCompleted;
        private Exception? _loadFailure;
        private long _loadRevision;
        private Task<HistoryArchive>? _observedPendingLoad;
        private Task<HistoryArchive>? _pendingLoad;
        private List<RunSnapshot> _runs = [];

        public int DataVersion { get; set; } = CurrentDataVersion;

        public List<RunSnapshot> Runs
        {
            get
            {
                CompletePendingLoadIfReady();
                return _runs;
            }
            set => _runs = value;
        }

        [JsonIgnore] internal bool RequiresStorageRewrite { get; set; }

        [JsonIgnore]
        internal long LoadRevision
        {
            get
            {
                CompletePendingLoadIfReady();
                return Interlocked.Read(ref _loadRevision);
            }
        }

        [JsonIgnore]
        internal bool IsLoadReady
        {
            get
            {
                CompletePendingLoadIfReady();
                lock (_gate)
                {
                    return _pendingLoad == null && _loadFailure == null;
                }
            }
        }

        internal void AttachPendingLoad(Task<HistoryArchive> pendingLoad)
        {
            Action? callback;
            lock (_gate)
            {
                _pendingLoad = pendingLoad;
                callback = _loadCompleted;
            }

            ObservePendingLoad(pendingLoad, callback);
        }

        internal void AttachCombatLoader(
            Func<CombatSnapshot, CombatSnapshot> combatLoader,
            Func<CombatSnapshot, CombatSnapshot>? combatSummaryLoader = null)
        {
            lock (_gate)
            {
                _combatLoader = combatLoader;
                _combatSummaryLoader = combatSummaryLoader;
            }
        }

        internal RunSnapshot? MaterializeRun(
            string runId,
            bool includeEvents,
            bool includeTimeline,
            string? combatId = null,
            CancellationToken cancellationToken = default)
        {
            CompletePendingLoadIfReady();
            RunSnapshot? run;
            Func<CombatSnapshot, CombatSnapshot>? loader;
            lock (_gate)
            {
                run = _runs.LastOrDefault(candidate => candidate.RunId == runId);
                loader = _combatLoader;
            }

            if (run == null)
                return null;

            var combats = new List<CombatSnapshot>(combatId == null ? run.Combats.Count : 1);
            foreach (var combat in run.Combats)
            {
                if (combatId != null && combat.CombatId != combatId)
                    continue;
                cancellationToken.ThrowIfCancellationRequested();
                combats.Add(Project(loader?.Invoke(combat) ?? combat, includeEvents, includeTimeline));
            }

            return run with { Combats = combats };
        }

        internal RunSnapshot? MaterializeRunSummary(
            string runId,
            CancellationToken cancellationToken = default)
        {
            CompletePendingLoadIfReady();
            RunSnapshot? run;
            Func<CombatSnapshot, CombatSnapshot>? summaryLoader;
            lock (_gate)
            {
                run = _runs.LastOrDefault(candidate => candidate.RunId == runId);
                summaryLoader = _combatSummaryLoader ?? _combatLoader;
            }

            if (run == null)
                return null;

            var combats = new List<CombatSnapshot>(run.Combats.Count);
            foreach (var combat in run.Combats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var materialized = combat.Players.Count > 0
                    ? combat
                    : summaryLoader?.Invoke(combat) ?? combat;
                combats.Add(AnalysisSnapshotSelector.SummarizeCombat(materialized));
            }

            return run with { Combats = combats };
        }

        internal void SetLoadCompletionCallback(Action callback)
        {
            Task<HistoryArchive>? pending;
            lock (_gate)
            {
                _loadCompleted = callback;
                pending = _pendingLoad;
            }

            if (pending != null)
                ObservePendingLoad(pending, callback);
        }

        internal void ApplyMutation(Action<HistoryArchive> modifier)
        {
            CompletePendingLoadIfReady();
            lock (_gate)
            {
                var before = _runs.ToDictionary(run => run.RunId, StringComparer.Ordinal);
                modifier(this);
                var after = _runs.ToDictionary(run => run.RunId, StringComparer.Ordinal);
                foreach (var runId in before.Keys.Union(after.Keys))
                    if (!before.TryGetValue(runId, out var previous)
                        || !after.TryGetValue(runId, out var current)
                        || !ReferenceEquals(previous, current))
                        _mutatedRunIds.Add(runId);
            }
        }

        internal HistoryArchive CreatePersistenceSnapshot()
        {
            CompletePendingLoad(true);
            lock (_gate)
            {
                return new()
                {
                    DataVersion = DataVersion,
                    Runs = [.. _runs],
                };
            }
        }

        internal void ClearForMutation()
        {
            CompletePendingLoadIfReady();
            lock (_gate)
            {
                _discardPendingRuns = _pendingLoad != null;
                foreach (var run in _runs)
                    _mutatedRunIds.Add(run.RunId);
                _runs.Clear();
                _combatLoader = null;
                _combatSummaryLoader = null;
            }
        }

        private static CombatSnapshot Project(
            CombatSnapshot combat,
            bool includeEvents,
            bool includeTimeline)
        {
            if (includeEvents && includeTimeline)
                return combat;
            return combat with
            {
                Events = includeEvents ? combat.Events : [],
                Timeline = includeTimeline ? combat.Timeline : [],
            };
        }

        private void CompletePendingLoadIfReady()
        {
            var pending = _pendingLoad;
            if (pending is not { IsCompleted: true })
                return;
            CompletePendingLoad(false);
        }

        private void ObservePendingLoad(Task<HistoryArchive> pending, Action? callback)
        {
            if (callback == null)
                return;
            lock (_gate)
            {
                if (ReferenceEquals(_observedPendingLoad, pending))
                    return;
                _observedPendingLoad = pending;
            }

            _ = pending.ContinueWith(
                static (_, state) => ((Action)state!).Invoke(),
                callback,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void CompletePendingLoad(bool wait)
        {
            Task<HistoryArchive>? pending;
            lock (_gate)
            {
                pending = _pendingLoad;
                if (pending == null)
                {
                    if (wait && _loadFailure != null)
                        throw new InvalidOperationException("Analytics history could not be loaded.", _loadFailure);
                    return;
                }

                if (!wait && !pending.IsCompleted)
                    return;
            }

            HistoryArchive loaded;
            try
            {
                loaded = pending.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    if (!ReferenceEquals(_pendingLoad, pending))
                        return;
                    _pendingLoad = null;
                    _loadFailure = exception;
                }

                Main.Logger.Error($"Asynchronous analytics history load failed: {exception}");
                if (wait)
                    throw new InvalidOperationException("Analytics history could not be loaded.", exception);
                return;
            }

            lock (_gate)
            {
                if (!ReferenceEquals(_pendingLoad, pending))
                    return;

                var currentRuns = _runs.ToDictionary(run => run.RunId, StringComparer.Ordinal);
                var mergedRuns = _discardPendingRuns
                    ? []
                    : loaded._runs.Where(run => !_mutatedRunIds.Contains(run.RunId)).ToList();
                foreach (var runId in _mutatedRunIds)
                    if (currentRuns.TryGetValue(runId, out var current))
                        mergedRuns.Add(current);

                DataVersion = loaded.DataVersion;
                _runs = mergedRuns.OrderBy(run => run.StartedAtUtc).ToList();
                _combatLoader = loaded._combatLoader;
                _combatSummaryLoader = loaded._combatSummaryLoader;
                RequiresStorageRewrite |= loaded.RequiresStorageRewrite;
                _pendingLoad = null;
                _loadFailure = null;
                _discardPendingRuns = false;
                _mutatedRunIds.Clear();
                Interlocked.Increment(ref _loadRevision);
            }
        }
    }
}
