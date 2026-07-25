// SPDX-License-Identifier: MPL-2.0

using Godot;
using MegaCrit.Sts2.Core.Entities.Players;

namespace STS2RitsuMetrics.Core
{
    internal readonly record struct PlayerIdentityColorSource(
        string PlayerKey,
        ulong? PlayerNetId,
        string CharacterId,
        Color BaseColor);

    internal static class PlayerIdentityColor
    {
        private static readonly string[] FallbackColors =
        [
            "E36A6AFF",
            "68B986FF",
            "69A8D7FF",
            "C596D8FF",
            "D9A45FFF",
            "75B8B1FF",
        ];

        private static readonly float[] NeutralVariantHues = [0.46f, 0.58f, 0.1f, 0.78f];

        internal static IReadOnlyDictionary<string, string> Assign(IEnumerable<Player> players)
        {
            return Resolve(players.Select(player =>
            {
                var descriptor = GameDescriptorFactory.Player(player);
                return new PlayerIdentityColorSource(
                    descriptor.Key,
                    descriptor.PlayerNetId,
                    descriptor.CharacterId,
                    ReadBaseColor(player, descriptor.CharacterId, descriptor.Key));
            }));
        }

        internal static IReadOnlyDictionary<string, string> Resolve(
            IEnumerable<PlayerIdentityColorSource> players)
        {
            var identities = players
                .DistinctBy(player => player.PlayerKey, StringComparer.Ordinal)
                .ToArray();
            var result = new Dictionary<string, string>(identities.Length, StringComparer.Ordinal);
            foreach (var group in identities.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group
                    .OrderBy(player => player.PlayerNetId.HasValue ? 0 : 1)
                    .ThenBy(player => player.PlayerNetId)
                    .ThenBy(player => player.PlayerKey, StringComparer.Ordinal)
                    .ToArray();
                for (var index = 0; index < ordered.Length; index++)
                    result.Add(ordered[index].PlayerKey, Encode(Variant(ordered[index].BaseColor, index)));
            }

            return result;
        }

        private static string GroupKey(PlayerIdentityColorSource player)
        {
            return string.IsNullOrWhiteSpace(player.CharacterId)
                ? $"\0{player.PlayerKey}"
                : player.CharacterId.Trim();
        }

        private static Color ReadBaseColor(Player player, string characterId, string playerKey)
        {
            try
            {
                var color = player.Character.NameColor;
                color.A = 1f;
                return color;
            }
            catch
            {
                var identity = string.IsNullOrWhiteSpace(characterId) ? playerKey : characterId;
                return new(FallbackColors[(int)(StableHash(identity) % (uint)FallbackColors.Length)]);
            }
        }

        private static Color Variant(Color source, int variant)
        {
            source.A = 1f;
            if (variant == 0)
                return source;

            var magnitude = (variant + 1) / 2;
            var direction = variant % 2 == 0 ? -1f : 1f;
            var valueOffset = direction > 0f ? -magnitude * 0.07f : magnitude * 0.05f;
            if (source.S < 0.12f)
            {
                var saturation = Math.Clamp(0.04f + magnitude * 0.02f, 0.04f, 0.1f);
                var value = Math.Clamp(source.V + valueOffset, 0.52f, 1f);
                return Color.FromHsv(NeutralVariantHues[variant % NeutralVariantHues.Length],
                    saturation, value);
            }

            var hue = source.H + direction * magnitude / 60f;
            hue -= MathF.Floor(hue);
            return Color.FromHsv(
                hue,
                Math.Clamp(source.S + magnitude * 0.025f, 0f, 1f),
                Math.Clamp(source.V + valueOffset, 0.52f, 1f));
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
