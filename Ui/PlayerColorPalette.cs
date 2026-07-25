// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Ui
{
    internal readonly record struct PlayerColorIdentity(
        string PlayerKey,
        ulong? PlayerNetId,
        string CharacterId);

    internal sealed class PlayerColorPaletteCache
    {
        private readonly Dictionary<string, PlayerColorIdentity> _players = new(StringComparer.Ordinal);
        private string[] _accentColors = [];
        private IReadOnlyDictionary<string, string>? _colors;
        private string? _positiveColor;
        private string? _surfaceColor;

        internal IReadOnlyDictionary<string, string> Resolve(
            IEnumerable<PlayerColorIdentity> players,
            DashboardStyleDefinition style,
            Func<string, Color?> characterColor)
        {
            var identities = players
                .DistinctBy(player => player.PlayerKey, StringComparer.Ordinal)
                .ToArray();
            if (_colors != null && MatchesPlayers(identities) && MatchesStyle(style))
                return _colors;

            _players.Clear();
            foreach (var player in identities)
                _players.Add(player.PlayerKey, player);
            _surfaceColor = style.SurfaceColor;
            _positiveColor = style.PositiveColor;
            _accentColors = style.AccentColors.ToArray();
            _colors = PlayerColorPalette.Resolve(identities, style, characterColor);
            return _colors;
        }

        private bool MatchesPlayers(PlayerColorIdentity[] players)
        {
            if (_players.Count != players.Length)
                return false;
            foreach (var player in players)
                if (!_players.TryGetValue(player.PlayerKey, out var previous) || previous != player)
                    return false;
            return true;
        }

        private bool MatchesStyle(DashboardStyleDefinition style)
        {
            return _surfaceColor != null &&
                   string.Equals(_surfaceColor, style.SurfaceColor, StringComparison.Ordinal) &&
                   string.Equals(_positiveColor, style.PositiveColor, StringComparison.Ordinal) &&
                   _accentColors.SequenceEqual(style.AccentColors, StringComparer.Ordinal);
        }
    }

    internal static class PlayerColorPalette
    {
        private const double AccentContrast = 3d;
        private static readonly float[] NeutralVariantHues = [0.46f, 0.58f, 0.1f, 0.78f];

        internal static IReadOnlyDictionary<string, string> Resolve(
            IEnumerable<PlayerColorIdentity> players,
            DashboardStyleDefinition style,
            Func<string, Color?> characterColor)
        {
            var identities = players
                .DistinctBy(player => player.PlayerKey, StringComparer.Ordinal)
                .ToArray();
            var resolved = new Dictionary<string, string>(identities.Length, StringComparer.Ordinal);
            foreach (var group in identities.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group
                    .OrderBy(player => player.PlayerNetId.HasValue ? 0 : 1)
                    .ThenBy(player => player.PlayerNetId)
                    .ThenBy(player => player.PlayerKey, StringComparer.Ordinal)
                    .ToArray();
                var baseColor = ResolveBaseColor(ordered[0], style, characterColor);
                for (var index = 0; index < ordered.Length; index++)
                    resolved.Add(ordered[index].PlayerKey,
                        Encode(ToneAccent(baseColor, Parse(style.SurfaceColor, Colors.Black), index)));
            }

            return resolved;
        }

        internal static string ReadableTextOutline(string textColor)
        {
            var text = Parse(textColor, Colors.White);
            text.A = 1f;
            return RelativeLuminance(text) >= 0.35d ? "080A0EF2" : "F7F9FCF2";
        }

        internal static string ReadableTextBackdrop(string textColor)
        {
            var text = Parse(textColor, Colors.White);
            text.A = 1f;
            return RelativeLuminance(text) >= 0.35d ? "05070A8F" : "F8FAFC8F";
        }

        internal static double ContrastRatio(string first, string second)
        {
            return ContrastRatio(Parse(first, Colors.Black), Parse(second, Colors.Black));
        }

        internal static double TextBackdropContrastRatio(string fillColor, string textColor)
        {
            var fill = Parse(fillColor, Colors.White);
            var text = Parse(textColor, Colors.White);
            var backdrop = Parse(ReadableTextBackdrop(textColor), Colors.Black);
            var effective = new Color(
                backdrop.R * backdrop.A + fill.R * (1f - backdrop.A),
                backdrop.G * backdrop.A + fill.G * (1f - backdrop.A),
                backdrop.B * backdrop.A + fill.B * (1f - backdrop.A));
            return ContrastRatio(text, effective);
        }

        private static string GroupKey(PlayerColorIdentity player)
        {
            return string.IsNullOrWhiteSpace(player.CharacterId)
                ? $"\0{player.PlayerKey}"
                : player.CharacterId.Trim();
        }

        private static Color ResolveBaseColor(
            PlayerColorIdentity player,
            DashboardStyleDefinition style,
            Func<string, Color?> characterColor)
        {
            if (!string.IsNullOrWhiteSpace(player.CharacterId))
                try
                {
                    if (characterColor(player.CharacterId) is { } resolved)
                        return resolved;
                }
                catch
                {
                    // Fall back to the configured palette when a third-party character cannot be resolved.
                }

            if (style.AccentColors is not { Count: > 0 })
                return Parse(style.PositiveColor, Colors.White);
            var identity = string.IsNullOrWhiteSpace(player.CharacterId) ? player.PlayerKey : player.CharacterId;
            return Parse(style.AccentColors[(int)(StableHash(identity) % (uint)style.AccentColors.Count)],
                Colors.White);
        }

        private static Color ToneAccent(Color source, Color surface, int variant)
        {
            source.A = 1f;
            surface.A = 1f;
            var magnitude = (variant + 1) / 2;
            var direction = variant % 2 == 0 ? -1f : 1f;
            var valueOffset = variant switch
            {
                0 => 0f,
                _ when direction > 0f => -magnitude * 0.07f,
                _ => magnitude * 0.05f,
            };
            var neutral = source.S < 0.12f;
            var hue = neutral
                ? NeutralVariantHue(variant)
                : source.H + (variant == 0 ? 0f : direction * magnitude / 60f);
            hue -= MathF.Floor(hue);
            var saturation = neutral
                ? variant == 0 ? 0f : Math.Clamp(0.04f + magnitude * 0.02f, 0.04f, 0.1f)
                : Math.Clamp(source.S * 0.62f + (variant == 0 ? 0f : magnitude * 0.025f), 0.2f, 0.72f);
            var value = Math.Clamp(source.V * (neutral ? 0.9f : 0.82f) + valueOffset, 0.52f, 0.88f);
            var toned = Color.FromHsv(hue, saturation, value);
            var target = RelativeLuminance(surface) < 0.5d ? Colors.White : Colors.Black;
            for (var step = 0; step < 24; step++)
            {
                if (ContrastRatio(toned, surface) >= AccentContrast)
                    break;
                toned = toned.Lerp(target, 0.06f);
            }

            return toned;
        }

        private static float NeutralVariantHue(int variant)
        {
            return NeutralVariantHues[variant % NeutralVariantHues.Length];
        }

        private static double ContrastRatio(Color first, Color second)
        {
            var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
            var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
            return (lighter + 0.05d) / (darker + 0.05d);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126d * Linear(color.R) + 0.7152d * Linear(color.G) + 0.0722d * Linear(color.B);

            static double Linear(float channel)
            {
                return channel <= 0.04045f
                    ? channel / 12.92d
                    : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
            }
        }

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var character in value)
            {
                hash ^= char.ToUpperInvariant(character);
                hash *= prime;
            }

            return hash;
        }

        private static Color Parse(string value, Color fallback)
        {
            try
            {
                return new(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static string Encode(Color color)
        {
            return $"{Channel(color.R):X2}{Channel(color.G):X2}{Channel(color.B):X2}FF";

            static byte Channel(float value)
            {
                return (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
            }
        }
    }
}
