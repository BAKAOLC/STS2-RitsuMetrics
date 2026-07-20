// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Data.Models
{
    [JsonConverter(typeof(HistoryArchiveJsonConverter))]
    public sealed class HistoryArchive
    {
        public const int CurrentDataVersion = 1;
        internal const string CurrentStorageFormat = "combat-files-v2";

        private readonly Lock _gate = new();
        private readonly HashSet<string> _mutatedRunIds = new(StringComparer.Ordinal);
        private bool _discardPendingRuns;
        private Exception? _loadFailure;
        private long _loadRevision;
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

        [JsonIgnore] internal long LoadRevision
        {
            get
            {
                CompletePendingLoadIfReady();
                return Interlocked.Read(ref _loadRevision);
            }
        }

        internal void AttachPendingLoad(Task<HistoryArchive> pendingLoad)
        {
            _pendingLoad = pendingLoad;
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
            }
        }

        private void CompletePendingLoadIfReady()
        {
            var pending = _pendingLoad;
            if (pending is not { IsCompleted: true })
                return;
            CompletePendingLoad(false);
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
