// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Core
{
    internal enum RunMergeFailure
    {
        None,
        MissingRun,
        EmptyRun,
        ModeMismatch,
        SeedMismatch,
        IdentityMismatch,
        UnverifiableLegacyIdentity,
        PlayerMismatch,
        CombatConflict,
        NothingToMerge,
    }

    internal sealed record RunMergeResult(
        RunSnapshot? MergedRun,
        RunMergeFailure Failure,
        int AddedCombats = 0,
        int OverlappingCombats = 0)
    {
        internal bool Success => Failure == RunMergeFailure.None && MergedRun != null;
    }

    internal static class RunMergeService
    {
        internal static RunMergeResult Analyze(RunSnapshot? target, RunSnapshot? source)
        {
            if (target == null || source == null)
                return Failure(RunMergeFailure.MissingRun);
            if (target.Combats.Count == 0 || source.Combats.Count == 0)
                return Failure(RunMergeFailure.EmptyRun);
            if (target.IsMultiplayer != source.IsMultiplayer || target.IsDaily != source.IsDaily)
                return Failure(RunMergeFailure.ModeMismatch);

            var identityFailure = ValidateIdentity(target, source);
            if (identityFailure != RunMergeFailure.None)
                return Failure(identityFailure);
            if (!HaveCompatibleRecordedPlayers(target, source))
                return Failure(RunMergeFailure.PlayerMismatch);

            var targetCombats = target.Combats.ToDictionary(combat => combat.CombatId, StringComparer.Ordinal);
            var overlapping = 0;
            foreach (var sourceCombat in source.Combats)
            {
                if (!targetCombats.TryGetValue(sourceCombat.CombatId, out var targetCombat))
                    continue;
                overlapping++;
                if (!SameCombatIdentity(targetCombat, sourceCombat))
                    return Failure(RunMergeFailure.CombatConflict);
            }

            if (HasFloorConflict(target.Combats, source.Combats))
                return Failure(RunMergeFailure.CombatConflict);

            var added = source.Combats.Select(combat => combat.CombatId)
                .Distinct(StringComparer.Ordinal)
                .Count(combatId => !targetCombats.ContainsKey(combatId));
            if (added == 0)
                return Failure(RunMergeFailure.NothingToMerge);

            var canonicalRunId = target.RunId;
            var combats = target.Combats.Concat(source.Combats)
                .GroupBy(combat => combat.CombatId, StringComparer.Ordinal)
                .Select(group => RebindRunId(group.MaxBy(CombatCompleteness)!, canonicalRunId))
                .OrderBy(combat => combat.ActIndex)
                .ThenBy(combat => combat.Floor)
                .ThenBy(combat => combat.StartedAtUtc)
                .ToArray();
            var latest = LastActivity(source) > LastActivity(target) ? source : target;
            var merged = latest with
            {
                RunId = canonicalRunId,
                StartedAtUtc = target.StartedAtUtc < source.StartedAtUtc
                    ? target.StartedAtUtc
                    : source.StartedAtUtc,
                Combats = combats,
                Identity = target.Identity ?? source.Identity,
            };
            return new(merged, RunMergeFailure.None, added, overlapping);
        }

        private static RunMergeFailure ValidateIdentity(RunSnapshot target, RunSnapshot source)
        {
            if (target.Identity == null || source.Identity == null)
                return string.Equals(target.RunId, source.RunId, StringComparison.Ordinal)
                    ? RunMergeFailure.None
                    : RunMergeFailure.UnverifiableLegacyIdentity;

            var targetIdentity = target.Identity;
            var sourceIdentity = source.Identity;
            if (string.IsNullOrWhiteSpace(targetIdentity.Seed) ||
                string.IsNullOrWhiteSpace(sourceIdentity.Seed))
                return RunMergeFailure.IdentityMismatch;
            if (!string.Equals(targetIdentity.Seed, sourceIdentity.Seed, StringComparison.Ordinal))
                return RunMergeFailure.SeedMismatch;
            if (targetIdentity.StartedAtUnixSeconds != sourceIdentity.StartedAtUnixSeconds ||
                targetIdentity.GameMode != sourceIdentity.GameMode ||
                targetIdentity.AscensionLevel != sourceIdentity.AscensionLevel ||
                targetIdentity.DailyTimeUnixSeconds != sourceIdentity.DailyTimeUnixSeconds ||
                !targetIdentity.Players.SequenceEqual(sourceIdentity.Players) ||
                !targetIdentity.ModifierIds.SequenceEqual(sourceIdentity.ModifierIds, StringComparer.Ordinal))
                return RunMergeFailure.IdentityMismatch;
            return RunMergeFailure.None;
        }

        private static bool HaveCompatibleRecordedPlayers(RunSnapshot target, RunSnapshot source)
        {
            var targetPlayers = RecordedPlayers(target);
            var sourcePlayers = RecordedPlayers(source);
            return targetPlayers.Count == 0 || sourcePlayers.Count == 0 ||
                   targetPlayers.SetEquals(sourcePlayers);
        }

        private static HashSet<RecordedPlayer> RecordedPlayers(RunSnapshot run)
        {
            return run.Combats.SelectMany(combat => combat.Players)
                .Select(player => new RecordedPlayer(player.PlayerNetId, player.PlayerKey, player.CharacterId))
                .ToHashSet();
        }

        private static bool HasFloorConflict(
            IReadOnlyList<CombatSnapshot> targetCombats,
            IReadOnlyList<CombatSnapshot> sourceCombats)
        {
            var targetByFloor = targetCombats.GroupBy(combat => (combat.ActIndex, combat.Floor))
                .ToDictionary(group => group.Key,
                    group => group.Select(combat => combat.CombatId).ToHashSet(StringComparer.Ordinal));
            foreach (var sourceFloor in sourceCombats.GroupBy(combat => (combat.ActIndex, combat.Floor)))
            {
                if (!targetByFloor.TryGetValue(sourceFloor.Key, out var targetIds))
                    continue;
                var sourceIds = sourceFloor.Select(combat => combat.CombatId).ToHashSet(StringComparer.Ordinal);
                if (!targetIds.SetEquals(sourceIds))
                    return true;
            }

            return false;
        }

        private static bool SameCombatIdentity(CombatSnapshot target, CombatSnapshot source)
        {
            return target.ActIndex == source.ActIndex &&
                   target.Floor == source.Floor &&
                   string.Equals(target.EncounterId, source.EncounterId, StringComparison.Ordinal);
        }

        private static int CombatCompleteness(CombatSnapshot combat)
        {
            return (combat.Completed ? 1_000_000 : 0) +
                   combat.Events.Count * 10 +
                   (combat.Timeline?.Count ?? 0) * 10 +
                   combat.Players.Sum(player => player.Sources.Sum(source => source.Value.Count));
        }

        private static CombatSnapshot RebindRunId(CombatSnapshot combat, string runId)
        {
            return SnapshotCloner.Clone(combat, true) with
            {
                RunId = runId,
                Events = combat.Events.Select(observation => observation with { RunId = runId }).ToArray(),
                Timeline = combat.Timeline?.Select(timelineEvent => timelineEvent with { RunId = runId }).ToArray(),
            };
        }

        private static DateTimeOffset LastActivity(RunSnapshot run)
        {
            return run.Combats.Select(combat => combat.EndedAtUtc ?? combat.StartedAtUtc)
                .Append(run.EndedAtUtc ?? run.StartedAtUtc)
                .Max();
        }

        private static RunMergeResult Failure(RunMergeFailure failure)
        {
            return new(null, failure);
        }

        private readonly record struct RecordedPlayer(
            ulong? PlayerNetId,
            string PlayerKey,
            string CharacterId);
    }
}
