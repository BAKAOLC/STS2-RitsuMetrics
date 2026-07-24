// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Core
{
    internal sealed class MetricsRepository
    {
        private static int _historyReadFailureLogged;
        private readonly Lock _gate = new();
        private MutableRunSession? _liveRun;
        private CombatSnapshot? _retainedCombat;

        internal MutableRunSession? LiveRun
        {
            get
            {
                lock (_gate)
                {
                    return _liveRun;
                }
            }
        }

        internal bool HasLiveCombat
        {
            get
            {
                lock (_gate)
                {
                    return _liveRun?.GetActiveCombat() != null || _retainedCombat != null;
                }
            }
        }

        internal bool HasLiveRunCombat
        {
            get
            {
                lock (_gate)
                {
                    return _liveRun?.HasAnyCombat() == true;
                }
            }
        }

        internal void SetLiveRun(MutableRunSession? run)
        {
            lock (_gate)
            {
                _liveRun = run;
            }
        }

        internal CombatSnapshot? GetLiveCombat(bool includeEvents)
        {
            lock (_gate)
            {
                var active = _liveRun?.GetActiveCombat();
                return active != null
                    ? active.Snapshot(includeEvents)
                    : _retainedCombat == null
                        ? null
                        : SnapshotCloner.Clone(_retainedCombat, includeEvents);
            }
        }

        internal void RetainCompletedCombat(CombatSnapshot combat)
        {
            lock (_gate)
            {
                _retainedCombat = SnapshotCloner.Clone(combat, true);
            }
        }

        internal bool ClearRetainedCombat()
        {
            lock (_gate)
            {
                if (_retainedCombat == null)
                    return false;
                _retainedCombat = null;
                return true;
            }
        }

        internal RunSnapshot? GetLiveRun(bool includeEvents)
        {
            lock (_gate)
            {
                return _liveRun?.Snapshot(includeEvents);
            }
        }

        internal RunSnapshot? GetLiveRunForDashboard(
            bool includeEvents,
            bool includeTimeline,
            bool includeCompletedCombats,
            IReadOnlySet<string>? metricIds)
        {
            lock (_gate)
            {
                return _liveRun?.SnapshotForLiveView(includeEvents, includeTimeline, includeCompletedCombats,
                    metricIds);
            }
        }

        internal RunSnapshot? GetLiveRunSummary()
        {
            lock (_gate)
            {
                return _liveRun?.Snapshot(false, false);
            }
        }

        internal IReadOnlyList<RunSnapshot> GetAvailableRuns(bool includeEvents)
        {
            var runs = GetSavedRuns().ToList();
            var live = GetLiveRun(includeEvents);
            // ReSharper disable once InvertIf
            if (live != null)
            {
                var index = runs.FindIndex(run => run.RunId == live.RunId);
                if (index < 0)
                    runs.Add(live);
                else
                    runs[index] = live;
            }

            return runs.Select(run => SnapshotCloner.Clone(run, includeEvents))
                .OrderByDescending(run => run.StartedAtUtc).ToArray();
        }

        internal static IReadOnlyList<RunSnapshot> GetSavedRuns(bool includeEvents = true,
            bool includeTimeline = true)
        {
            try
            {
                var runs = ModData.History.Runs.Select(run =>
                    SnapshotCloner.Clone(run, includeEvents, includeTimeline)).ToArray();
                Volatile.Write(ref _historyReadFailureLogged, 0);
                return runs;
            }
            catch (Exception exception)
            {
                LogHistoryReadFailure(exception);
                return [];
            }
        }

        internal static RunSnapshot? GetSavedRun(string runId, bool includeEvents = true)
        {
            try
            {
                var run = ModData.History.Runs.LastOrDefault(candidate => candidate.RunId == runId);
                Volatile.Write(ref _historyReadFailureLogged, 0);
                return run == null ? null : SnapshotCloner.Clone(run, includeEvents);
            }
            catch (Exception exception)
            {
                LogHistoryReadFailure(exception);
                return null;
            }
        }

        internal static void SaveRunSnapshot(RunSnapshot run)
        {
            try
            {
                var trim = default(HistoryTrimResult);
                ModData.ModifyHistory(archive =>
                {
                    var index = archive.Runs.FindIndex(existing => existing.RunId == run.RunId);
                    if (index >= 0)
                        archive.Runs[index] = run;
                    else
                        archive.Runs.Add(run);

                    trim = TrimToCombatLimit(archive.Runs, ModData.Settings.HistoryCombatLimit);
                }, operation: "run checkpoint");
                if (trim.RemovedCombats > 0)
                    Main.Logger.Info(
                        $"History retention removed {trim.RemovedCombats} old combat(s), including " +
                        $"{trim.RemovedRuns} complete run record(s), to enforce the {trim.CombatLimit}-combat limit.");
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to persist analytics history: {exception}");
            }
        }

        internal static void ClearHistory()
        {
            try
            {
                ModData.ClearHistory();
                Main.Logger.Info("Analytics history cleared.");
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to clear analytics history: {exception}");
            }
        }

        internal static bool DeleteRun(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
                return false;
            try
            {
                var removed = false;
                ModData.ModifyHistory(archive => { removed = archive.Runs.RemoveAll(run => run.RunId == runId) > 0; },
                    operation: "delete run");
                if (removed)
                    Main.Logger.Info($"Deleted analytics run '{LogId(runId)}'.");
                else
                    Main.Logger.Debug($"Analytics run '{LogId(runId)}' was not found for deletion.");
                return removed;
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to delete analytics run '{runId}': {exception}");
                return false;
            }
        }

        internal static RunMergeResult MergeSavedRun(string targetRunId, RunSnapshot source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetRunId);
            ArgumentNullException.ThrowIfNull(source);
            var result = new RunMergeResult(null, RunMergeFailure.MissingRun);
            try
            {
                ModData.ModifyHistory(archive =>
                {
                    var targetIndex = archive.Runs.FindIndex(run => run.RunId == targetRunId);
                    if (targetIndex < 0)
                        return;
                    result = RunMergeService.Analyze(archive.Runs[targetIndex], source);
                    if (!result.Success)
                        return;

                    archive.Runs[targetIndex] = result.MergedRun!;
                    if (!string.Equals(source.RunId, targetRunId, StringComparison.Ordinal))
                        archive.Runs.RemoveAll(run => run.RunId == source.RunId);
                    _ = TrimToCombatLimit(archive.Runs, ModData.Settings.HistoryCombatLimit);
                }, operation: "merge run");
                if (result.Success)
                    Main.Logger.Info(
                        $"Merged {result.AddedCombats} combat(s) into analytics run '{LogId(targetRunId)}'.");
                return result;
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to merge analytics run '{LogId(targetRunId)}': {exception}");
                return new(null, RunMergeFailure.MissingRun);
            }
        }

        internal static void ReconcileLegacyMultiplayerRuns()
        {
            try
            {
                var runs = ModData.History.Runs.ToList();
                var mergedCount = 0;
                while (TryMergeLegacyMultiplayerPair(runs, out var merged, out var leftId, out var rightId))
                {
                    runs.RemoveAll(run => run.RunId == leftId || run.RunId == rightId);
                    runs.Add(merged);
                    mergedCount++;
                }

                if (mergedCount == 0)
                    return;
                ModData.ModifyHistory(archive =>
                {
                    archive.Runs.Clear();
                    archive.Runs.AddRange(runs.OrderBy(run => run.StartedAtUtc));
                }, operation: "multiplayer run identity reconciliation");
                Main.Logger.Info($"Reconciled {mergedCount} legacy multiplayer run split(s).");
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Failed to reconcile legacy multiplayer run identities: {exception}");
            }
        }

        private static bool TryMergeLegacyMultiplayerPair(
            IReadOnlyList<RunSnapshot> runs,
            out RunSnapshot merged,
            out string leftId,
            out string rightId)
        {
            var candidates = runs.Where(run => run.IsMultiplayer &&
                                               run.RunId.StartsWith("sts2-v1-", StringComparison.Ordinal) &&
                                               run.Combats.Count > 0)
                .OrderBy(FirstActivity)
                .ToArray();
            for (var leftIndex = 0; leftIndex < candidates.Length - 1; leftIndex++)
            {
                var left = candidates[leftIndex];
                for (var rightIndex = leftIndex + 1; rightIndex < candidates.Length; rightIndex++)
                {
                    var right = candidates[rightIndex];
                    if (!AreContinuousLegacySegments(left, right))
                        continue;
                    merged = MergeRuns(left, right);
                    leftId = left.RunId;
                    rightId = right.RunId;
                    return true;
                }
            }

            merged = null!;
            leftId = string.Empty;
            rightId = string.Empty;
            return false;
        }

        private static bool AreContinuousLegacySegments(RunSnapshot left, RunSnapshot right)
        {
            if (left.IsDaily != right.IsDaily || !SamePlayerRoster(left, right))
                return false;
            var leftLast = left.Combats.MaxBy(combat => combat.EndedAtUtc ?? combat.StartedAtUtc)!;
            var leftFirstFloor = left.Combats.Min(combat => combat.Floor);
            var rightFirst = right.Combats.MinBy(combat => combat.StartedAtUtc)!;
            var gap = rightFirst.StartedAtUtc - (leftLast.EndedAtUtc ?? leftLast.StartedAtUtc);
            return gap >= TimeSpan.Zero && gap <= TimeSpan.FromMinutes(30) &&
                   rightFirst.ActIndex >= leftLast.ActIndex && rightFirst.ActIndex <= leftLast.ActIndex + 1 &&
                   rightFirst.Floor > leftFirstFloor && rightFirst.Floor <= leftLast.Floor + 10;
        }

        private static bool SamePlayerRoster(RunSnapshot left, RunSnapshot right)
        {
            var leftPlayers = RecordedPlayers(left);
            var rightPlayers = RecordedPlayers(right);
            return leftPlayers.Count >= 2 && leftPlayers.Count == rightPlayers.Count &&
                   leftPlayers.All(player => rightPlayers.TryGetValue(player.Key, out var characterId) &&
                                             string.Equals(player.Value, characterId, StringComparison.Ordinal));
        }

        private static Dictionary<ulong, string> RecordedPlayers(RunSnapshot run)
        {
            return run.Combats.SelectMany(combat => combat.Players)
                .Where(player => player.PlayerNetId.HasValue)
                .GroupBy(player => player.PlayerNetId!.Value)
                .ToDictionary(group => group.Key,
                    group => group.Select(player => player.CharacterId)
                        .FirstOrDefault(characterId => !string.IsNullOrEmpty(characterId)) ?? string.Empty);
        }

        private static RunSnapshot MergeRuns(RunSnapshot left, RunSnapshot right)
        {
            var runId = right.RunId;
            var combats = left.Combats.Concat(right.Combats)
                .GroupBy(combat => combat.CombatId, StringComparer.Ordinal)
                .Select(group => RebindRunId(group.OrderByDescending(combat => combat.Timeline?.Count ?? 0).First(),
                    runId))
                .OrderBy(combat => combat.StartedAtUtc)
                .ToArray();
            return right with
            {
                StartedAtUtc = left.StartedAtUtc < right.StartedAtUtc ? left.StartedAtUtc : right.StartedAtUtc,
                Combats = combats,
            };
        }

        private static CombatSnapshot RebindRunId(CombatSnapshot combat, string runId)
        {
            return combat with
            {
                RunId = runId,
                Events = combat.Events.Select(observation => observation with { RunId = runId }).ToArray(),
                Timeline = combat.Timeline?.Select(timelineEvent => timelineEvent with { RunId = runId }).ToArray(),
            };
        }

        private static DateTimeOffset FirstActivity(RunSnapshot run)
        {
            return run.Combats.Select(combat => combat.StartedAtUtc).DefaultIfEmpty(run.StartedAtUtc).Min();
        }

        private static HistoryTrimResult TrimToCombatLimit(List<RunSnapshot> runs, int combatLimit)
        {
            var limit = Math.Clamp(combatLimit, 10, 500);
            var originalRunCount = runs.Count;
            runs.Sort((left, right) => left.StartedAtUtc.CompareTo(right.StartedAtUtc));
            var total = runs.Sum(run => run.Combats.Count);
            var originalCombatCount = total;
            while (total > limit && runs.Count > 0)
            {
                if (runs[0].Combats.Count <= total - limit)
                {
                    total -= runs[0].Combats.Count;
                    runs.RemoveAt(0);
                    continue;
                }

                var removeCount = total - limit;
                var first = runs[0];
                runs[0] = first with { Combats = first.Combats.Skip(removeCount).ToArray() };
                total -= removeCount;
            }

            return new(originalRunCount - runs.Count, originalCombatCount - total, limit);
        }

        private static void LogHistoryReadFailure(Exception exception)
        {
            if (Interlocked.Exchange(ref _historyReadFailureLogged, 1) == 0)
                Main.Logger.Error(
                    $"Failed to read analytics history; further repeated failures are suppressed: {exception}");
        }

        private static string LogId(string id)
        {
            return id.Length <= 20 ? id : $"{id[..10]}...{id[^7..]}";
        }

        private readonly record struct HistoryTrimResult(int RemovedRuns, int RemovedCombats, int CombatLimit);
    }
}
