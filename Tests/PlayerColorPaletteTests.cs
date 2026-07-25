// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Ui;

namespace STS2RitsuMetrics.Tests
{
    public sealed class PlayerColorPaletteTests
    {
        private static readonly DashboardStyleDefinition Style = new()
        {
            Id = "test",
            Name = "Test",
        };

        [Fact]
        public void ColorsFollowPlayerIdentityWhenRankingOrderChanges()
        {
            PlayerColorIdentity[] original =
            [
                new("player-a", 10, "IRONCLAD"),
                new("player-b", 20, "SILENT"),
            ];
            PlayerColorIdentity[] reordered = [original[1], original[0]];

            var first = Resolve(original);
            var second = Resolve(reordered);

            Assert.Equal(first["player-a"], second["player-a"]);
            Assert.Equal(first["player-b"], second["player-b"]);
        }

        [Fact]
        public void DuplicateCharactersReceiveDistinctStableVariants()
        {
            PlayerColorIdentity[] players =
            [
                new("player-c", 30, "DEFECT"),
                new("player-a", 10, "DEFECT"),
                new("player-b", 20, "DEFECT"),
            ];

            var first = Resolve(players);
            var second = Resolve(players.Reverse());

            Assert.Equal(players.Length, first.Values.Distinct(StringComparer.Ordinal).Count());
            foreach (var player in players)
                Assert.Equal(first[player.PlayerKey], second[player.PlayerKey]);
        }

        [Fact]
        public void CharacterAccentRemainsReadableOnDashboardSurface()
        {
            var colors = Resolve([new("player", 1, "SILENT")]);

            Assert.True(PlayerColorPalette.ContrastRatio(colors["player"], Style.SurfaceColor) >= 3d);
        }

        [Fact]
        public void WhiteTextReceivesDarkHighContrastOutline()
        {
            var outline = PlayerColorPalette.ReadableTextOutline("FFFFFFFF");

            Assert.True(PlayerColorPalette.ContrastRatio("FFFFFFFF", outline) >= 7d);
        }

        [Fact]
        public void TextBackdropKeepsWhiteTextReadableOnWhiteFill()
        {
            Assert.True(PlayerColorPalette.TextBackdropContrastRatio("FFFFFFFF", "FFFFFFFF") >= 4.5d);
        }

        [Fact]
        public void PureCharacterGreenIsSoftened()
        {
            var colors = Resolve([new("player", 1, "SILENT")]);
            var accent = new Color(colors["player"]);

            Assert.InRange(accent.S, 0.5f, 0.72f);
            Assert.InRange(accent.V, 0.7f, 0.88f);
        }

        [Fact]
        public void WhiteCharacterColorStaysNeutralAndReadable()
        {
            var colors = PlayerColorPalette.Resolve(
                [new("player", 1, "COLORLESS")],
                Style,
                _ => new("FFFFFFFF"));
            var accent = new Color(colors["player"]);

            Assert.InRange(MathF.Abs(accent.R - accent.G), 0f, 0.01f);
            Assert.InRange(MathF.Abs(accent.G - accent.B), 0f, 0.01f);
            Assert.True(PlayerColorPalette.ContrastRatio(colors["player"], Style.SurfaceColor) >= 3d);
        }

        [Fact]
        public void DuplicateCharacterVariantsStayNearTheOriginalHue()
        {
            PlayerColorIdentity[] players =
            [
                new("player-a", 10, "IRONCLAD"),
                new("player-b", 20, "IRONCLAD"),
                new("player-c", 30, "IRONCLAD"),
                new("player-d", 40, "IRONCLAD"),
            ];
            var colors = Resolve(players).Values.Select(value => new Color(value)).ToArray();
            var originalHue = new Color("FF5555").H;

            Assert.Equal(colors.Length, colors.Select(color => color.ToHtml()).Distinct().Count());
            Assert.All(colors, color => Assert.InRange(HueDistance(color.H, originalHue), 0f, 0.04f));
        }

        [Fact]
        public void PaletteCacheReusesColorsAcrossRankingChanges()
        {
            var cache = new PlayerColorPaletteCache();
            PlayerColorIdentity[] original =
            [
                new("player-a", 10, "IRONCLAD"),
                new("player-b", 20, "SILENT"),
            ];
            PlayerColorIdentity[] reordered = [original[1], original[0]];

            var first = cache.Resolve(original, Style, ResolveCharacterColor);
            var second = cache.Resolve(reordered, Style, ResolveCharacterColor);

            Assert.Same(first, second);
        }

        [Fact]
        public void PersistedIdentityColorOverridesLiveCharacterColor()
        {
            var color = PlayerColorPalette.Resolve(
                [new("player", 1, "SILENT", "E05050FF")],
                Style,
                _ => new("7FFF00FF"))["player"];

            Assert.InRange(HueDistance(new Color(color).H, new Color("E05050FF").H), 0f, 0.01f);
        }

        [Fact]
        public void PersistedDuplicateVariantsAreNotVariedAgain()
        {
            PlayerColorIdentity first = new("player-a", 10, "IRONCLAD", "FF5555FF");
            PlayerColorIdentity second = new("player-b", 20, "IRONCLAD", "E64E5AFF");

            var together = Resolve([first, second]);
            var firstOnly = Resolve([first]);
            var secondOnly = Resolve([second]);

            Assert.Equal(firstOnly[first.PlayerKey], together[first.PlayerKey]);
            Assert.Equal(secondOnly[second.PlayerKey], together[second.PlayerKey]);
        }

        [Fact]
        public void PaletteCacheInvalidatesWhenPersistedColorChanges()
        {
            var cache = new PlayerColorPaletteCache();
            var first = cache.Resolve(
                [new("player", 1, "IRONCLAD", "FF5555FF")],
                Style,
                ResolveCharacterColor);
            var second = cache.Resolve(
                [new("player", 1, "IRONCLAD", "55AAFFFF")],
                Style,
                ResolveCharacterColor);

            Assert.NotSame(first, second);
            Assert.NotEqual(first["player"], second["player"]);
        }

        private static IReadOnlyDictionary<string, string> Resolve(IEnumerable<PlayerColorIdentity> players)
        {
            return PlayerColorPalette.Resolve(players, Style, ResolveCharacterColor);
        }

        private static Color? ResolveCharacterColor(string characterId)
        {
            return characterId switch
            {
                "IRONCLAD" => new("FF5555"),
                "SILENT" => new("7FFF00"),
                "DEFECT" => new("87CEEB"),
                _ => null,
            };
        }

        private static float HueDistance(float first, float second)
        {
            var difference = MathF.Abs(first - second);
            return Math.Min(difference, 1f - difference);
        }
    }
}
