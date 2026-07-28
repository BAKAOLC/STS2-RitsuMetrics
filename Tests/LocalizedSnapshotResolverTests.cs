// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class LocalizedSnapshotResolverTests
    {
        [Fact]
        public void ResolveDoesNotCacheRunProjectionAndReusesUnchangedCombatData()
        {
            var combat = new CombatSnapshot(
                "run",
                "combat",
                0,
                1,
                string.Empty,
                "Encounter",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                true,
                1,
                [],
                [],
                []);
            var run = new RunSnapshot(
                "run",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                false,
                false,
                true,
                false,
                [combat]);

            var first = LocalizedSnapshotResolver.Resolve(run);
            var second = LocalizedSnapshotResolver.Resolve(run);

            Assert.NotSame(first, second);
            Assert.Same(combat, first.Combats[0]);
            Assert.Same(combat, second.Combats[0]);
        }
    }
}
