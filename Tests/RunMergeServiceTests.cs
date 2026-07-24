// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class RunMergeServiceTests
    {
        [Fact]
        public void MatchingRunSegmentsAreMergedAndReboundToTarget()
        {
            var target = Run("target", Identity("seed"), Combat("target", "combat-1", 1));
            var source = Run("source", Identity("seed"), Combat("source", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.True(result.Success);
            Assert.Equal(1, result.AddedCombats);
            Assert.Equal(["combat-1", "combat-2"], result.MergedRun!.Combats.Select(combat => combat.CombatId));
            Assert.All(result.MergedRun.Combats, combat => Assert.Equal("target", combat.RunId));
        }

        [Fact]
        public void OverlappingBattlesAreDeduplicated()
        {
            var target = Run("run", Identity("seed"), Combat("run", "combat-1", 1));
            var source = Run("run", Identity("seed"),
                Combat("run", "combat-1", 1),
                Combat("run", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.True(result.Success);
            Assert.Equal(1, result.AddedCombats);
            Assert.Equal(1, result.OverlappingCombats);
            Assert.Equal(2, result.MergedRun!.Combats.Count);
        }

        [Fact]
        public void DifferentSeedsAreRejected()
        {
            var target = Run("run-a", Identity("seed-a"), Combat("run-a", "combat-1", 1));
            var source = Run("run-b", Identity("seed-b"), Combat("run-b", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.Equal(RunMergeFailure.SeedMismatch, result.Failure);
        }

        [Fact]
        public void DifferentAscensionLevelsAreRejected()
        {
            var target = Run("run-a", Identity("seed", 10), Combat("run-a", "combat-1", 1));
            var source = Run("run-b", Identity("seed", 20), Combat("run-b", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.Equal(RunMergeFailure.IdentityMismatch, result.Failure);
        }

        [Fact]
        public void LegacyRunsRequireAnExactRunId()
        {
            var target = Run("legacy-a", null, Combat("legacy-a", "combat-1", 1));
            var source = Run("legacy-b", null, Combat("legacy-b", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.Equal(RunMergeFailure.UnverifiableLegacyIdentity, result.Failure);
        }

        [Fact]
        public void LegacyRunsWithTheSameRunIdCanBeMerged()
        {
            var target = Run("legacy", null, Combat("legacy", "combat-1", 1));
            var source = Run("legacy", null, Combat("legacy", "combat-2", 2));

            var result = RunMergeService.Analyze(target, source);

            Assert.True(result.Success);
            Assert.Equal(2, result.MergedRun!.Combats.Count);
        }

        [Fact]
        public void ConflictingBattlesOnTheSameFloorAreRejected()
        {
            var target = Run("run", Identity("seed"), Combat("run", "combat-a", 1));
            var source = Run("run", Identity("seed"), Combat("run", "combat-b", 1));

            var result = RunMergeService.Analyze(target, source);

            Assert.Equal(RunMergeFailure.CombatConflict, result.Failure);
        }

        [Fact]
        public void TransferPackageRoundTripsACompleteRun()
        {
            var original = Run("run", Identity("seed"), Combat("run", "combat-1", 1));

            var payload = RunTransferService.Serialize(original);
            var success = RunTransferService.TryDeserialize(payload, out var restored, out var error);

            Assert.True(success, error);
            Assert.Equal(original.RunId, restored!.RunId);
            Assert.Equal(original.Identity!.Seed, restored.Identity!.Seed);
            Assert.Equal(original.Identity.StartedAtUnixSeconds, restored.Identity.StartedAtUnixSeconds);
            Assert.Equal(original.Identity.Players, restored.Identity.Players);
            Assert.Equal(original.Identity.ModifierIds, restored.Identity.ModifierIds);
            Assert.Equal(original.Combats.Single().CombatId, restored.Combats.Single().CombatId);
        }

        private static RunSnapshot Run(
            string runId,
            RunIdentitySnapshot? identity,
            params CombatSnapshot[] combats)
        {
            var startedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
            return new(
                runId,
                startedAt,
                null,
                false,
                false,
                null,
                null,
                combats)
            {
                Identity = identity,
            };
        }

        private static RunIdentitySnapshot Identity(string seed, int ascension = 10)
        {
            return new(
                1_700_000_000,
                seed,
                0,
                ascension,
                null,
                [new(1, "ironclad")],
                []);
        }

        private static CombatSnapshot Combat(string runId, string combatId, int floor)
        {
            var startedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + floor * 60);
            return new(
                runId,
                combatId,
                0,
                floor,
                $"encounter-{floor}",
                $"Encounter {floor}",
                startedAt,
                startedAt.AddMinutes(1),
                true,
                1,
                [
                    new(
                        "player:1",
                        1,
                        "Player",
                        "ironclad",
                        new Dictionary<string, decimal>(),
                        new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>()),
                ],
                [],
                []);
        }
    }
}
