// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Domain;

namespace STS2RitsuMetrics.Tests
{
    public sealed class PlayerIdentityColorHistoryTests
    {
        [Fact]
        public void MutableCombatPersistsIdentityColorWithoutMetrics()
        {
            var combat = Combat();
            combat.InitializePlayer(
                new("player:42", AnalyticsEntityKind.Player, 42, "SILENT", "Player", "SILENT"),
                "7FFF00FF");

            var snapshot = combat.Snapshot(false);
            var restored = MutableCombatSession.Restore(snapshot).Snapshot(false);

            Assert.Single(snapshot.Players);
            Assert.Equal("7FFF00FF", snapshot.Players[0].IdentityColor);
            Assert.Equal("7FFF00FF", restored.Players[0].IdentityColor);
        }

        [Fact]
        public void SnapshotJsonRoundTripPreservesIdentityColor()
        {
            var snapshot = Player("E05050FF");

            var json = JsonSerializer.Serialize(snapshot);
            var restored = JsonSerializer.Deserialize<PlayerMetricSnapshot>(json);

            Assert.NotNull(restored);
            Assert.Equal(snapshot.IdentityColor, restored.IdentityColor);
        }

        [Fact]
        public void LegacySnapshotWithoutIdentityColorRemainsReadable()
        {
            var json = JsonSerializer.Serialize(Player("E05050FF"));
            json = json.Replace(",\"IdentityColor\":\"E05050FF\"", string.Empty, StringComparison.Ordinal);

            var restored = JsonSerializer.Deserialize<PlayerMetricSnapshot>(json);

            Assert.NotNull(restored);
            Assert.True(string.IsNullOrEmpty(restored.IdentityColor));
        }

        private static MutableCombatSession Combat()
        {
            return new()
            {
                RunId = "run",
                CombatId = "combat",
                StartedAtUtc = DateTimeOffset.UnixEpoch,
            };
        }

        private static PlayerMetricSnapshot Player(string identityColor)
        {
            return new(
                "player:42",
                42,
                "Player",
                "SILENT",
                new Dictionary<string, decimal>(),
                new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(),
                identityColor);
        }
    }
}
