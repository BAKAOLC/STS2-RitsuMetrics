// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class PlayerIdentityColorTests
    {
        [Fact]
        public void AssignmentIsStableAcrossRosterOrder()
        {
            PlayerIdentityColorSource[] players =
            [
                new("player-c", 30, "SILENT", new("7FFF00FF")),
                new("player-a", 10, "SILENT", new("7FFF00FF")),
                new("player-b", 20, "SILENT", new("7FFF00FF")),
            ];

            var first = PlayerIdentityColor.Resolve(players);
            var second = PlayerIdentityColor.Resolve(players.Reverse());

            foreach (var player in players)
                Assert.Equal(first[player.PlayerKey], second[player.PlayerKey]);
        }

        [Fact]
        public void DuplicateCharactersReceiveNearbyUniqueCanonicalVariants()
        {
            PlayerIdentityColorSource[] players =
            [
                new("player-a", 10, "IRONCLAD", new("FF5555FF")),
                new("player-b", 20, "IRONCLAD", new("FF5555FF")),
                new("player-c", 30, "IRONCLAD", new("FF5555FF")),
                new("player-d", 40, "IRONCLAD", new("FF5555FF")),
            ];

            var colors = PlayerIdentityColor.Resolve(players).Values.Select(value => new Color(value)).ToArray();
            var baseHue = new Color("FF5555FF").H;

            Assert.Equal(players.Length, colors.Select(color => color.ToHtml()).Distinct().Count());
            Assert.All(colors, color => Assert.InRange(HueDistance(color.H, baseHue), 0f, 0.04f));
        }

        [Fact]
        public void FirstCharacterKeepsOfficialCanonicalColor()
        {
            var colors = PlayerIdentityColor.Resolve(
            [
                new("player", 1, "DEFECT", new("87CEEBFF")),
            ]);

            Assert.Equal("87CEEBFF", colors["player"]);
        }

        private static float HueDistance(float first, float second)
        {
            var difference = MathF.Abs(first - second);
            return Math.Min(difference, 1f - difference);
        }
    }
}
