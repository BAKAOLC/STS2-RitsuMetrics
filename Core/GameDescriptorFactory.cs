// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Models;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Core
{
    internal static class GameDescriptorFactory
    {
        private static readonly ConditionalWeakTable<Player, EntityDescriptor> PlayerDescriptors = new();
        private static readonly ConditionalWeakTable<Creature, EntityDescriptor> CreatureDescriptors = new();
        private static readonly ConditionalWeakTable<Creature, SourceDescriptor> CreatureSources = new();
        private static readonly ConditionalWeakTable<AbstractModel, SourceDescriptor> ModelSources = new();
        private static readonly ConcurrentDictionary<Type, AnalyticsSourceKind> ModelKinds = new();

        private static readonly EntityDescriptor UnknownCreature =
            new("creature:unknown", AnalyticsEntityKind.Unknown, null, "unknown", "unknown");

        private static SourceDescriptor? _environmentSource;
        private static SourceDescriptor? _damageFloorSource;
        private static SourceDescriptor? _blockResolutionSource;
        private static SourceDescriptor? _damageQuantizationSource;
        private static SourceDescriptor? _overkillResolutionSource;
        private static SourceDescriptor? _unknownSource;

        static GameDescriptorFactory()
        {
            ModLocalization.Changed += ClearLocalizedSourceCaches;
        }

        internal static EntityDescriptor? Player(Creature? creature)
        {
            var player = creature?.Player ?? creature?.PetOwner;
            return player == null ? null : Player(player);
        }

        internal static EntityDescriptor? PlayerBody(Creature? creature)
        {
            return creature?.Player is { } player ? Player(player) : null;
        }

        internal static EntityDescriptor Player(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);
            return PlayerDescriptors.GetValue(player, static value => CreatePlayer(value));
        }

        internal static EntityDescriptor Creature(Creature? creature)
        {
            if (creature == null)
                return UnknownCreature;
            return creature.Player is { } player
                ? Player(player)
                : CreatureDescriptors.GetValue(creature, static value => CreateCreature(value));
        }

        internal static EntityDescriptor? CreatureOrNull(Creature? creature)
        {
            return creature == null ? null : Creature(creature);
        }

        internal static SourceDescriptor Card(CardModel card)
        {
            return Model(card);
        }

        internal static SourceDescriptor Power(PowerModel power)
        {
            return Model(power);
        }

        internal static SourceDescriptor Potion(PotionModel potion)
        {
            return Model(potion);
        }

        internal static SourceDescriptor Orb(OrbModel orb)
        {
            return Model(orb);
        }

        internal static SourceDescriptor Model(AbstractModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            return ModelSources.GetValue(model, static value => CreateModelSource(value));
        }

        internal static EntityDescriptor? ModelOwner(AbstractModel? model)
        {
            return model switch
            {
                CardModel card => Player(card.Owner),
                PowerModel power => Creature(power.Owner),
                PotionModel potion => Player(potion.Owner),
                OrbModel orb => Player(orb.Owner),
                RelicModel relic => Player(relic.Owner),
                _ => null,
            };
        }

        internal static SourceDescriptor CreatureSource(Creature? creature)
        {
            if (creature == null)
                return Environment();
            return CreatureSources.GetValue(creature, static value =>
            {
                var entity = Creature(value);
                return new($"creature:{entity.Key}", AnalyticsSourceKind.Creature, entity.ModelId,
                    entity.DisplayName);
            });
        }

        internal static SourceDescriptor Environment()
        {
            return _environmentSource ??= new("system:environment", AnalyticsSourceKind.System,
                "environment",
                ModLocalization.Get("source.environment", "Environment"));
        }

        internal static SourceDescriptor DamageFloor()
        {
            return _damageFloorSource ??= new("system:damage-floor", AnalyticsSourceKind.System,
                "damage-floor", ModLocalization.Get("source.damageFloor", "Damage floor"));
        }

        internal static SourceDescriptor BlockResolution()
        {
            return _blockResolutionSource ??= new("system:block-resolution", AnalyticsSourceKind.System,
                "block-resolution", ModLocalization.Get("source.blockResolution", "Block absorption"));
        }

        internal static SourceDescriptor DamageQuantization()
        {
            return _damageQuantizationSource ??= new("system:damage-quantization", AnalyticsSourceKind.System,
                "damage-quantization", ModLocalization.Get("source.damageQuantization", "Integer HP settlement"));
        }

        internal static SourceDescriptor OverkillResolution()
        {
            return _overkillResolutionSource ??= new("system:overkill-resolution", AnalyticsSourceKind.System,
                "overkill-resolution", ModLocalization.Get("source.overkillResolution", "HP limit"));
        }

        internal static SourceDescriptor Unknown()
        {
            return _unknownSource ??= new("system:unknown", AnalyticsSourceKind.Unknown, "unknown",
                ModLocalization.Get("source.unknown", "Unknown source"));
        }

        internal static string ResolveModelTitle(AbstractModel model, string fallback)
        {
            try
            {
                if (!model.TryResolveTitle(out var title) || !title.Exists())
                    return fallback;
                var text = title.GetFormattedText();
                return string.IsNullOrWhiteSpace(text) ? fallback : text;
            }
            catch
            {
                return fallback;
            }
        }

        private static EntityDescriptor CreatePlayer(Player player)
        {
            var characterId = Safe(() => player.Character.Id.Entry, string.Empty);
            var characterName = Safe(() => ResolveModelTitle(player.Character, characterId), characterId);
            var displayName = ResolvePlayerDisplayName(player, characterName);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = ModLocalization.Get("entity.unknownPlayer", "Unknown player");
            return new($"player:{player.NetId}", AnalyticsEntityKind.Player, player.NetId, characterId,
                displayName,
                characterId);
        }

        private static string ResolvePlayerDisplayName(Player player, string characterName)
        {
            if (!Safe(() => RunManager.Instance.NetService.Type.IsMultiplayer(), false))
                return characterName;

            var platformName = Safe(
                () => PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, player.NetId),
                string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(platformName) || string.Equals(platformName,
                    player.NetId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return characterName;
            return platformName;
        }

        private static EntityDescriptor CreateCreature(Creature creature)
        {
            if (creature.Monster != null)
            {
                var modelId = Safe(() => creature.Monster.Id.Entry, string.Empty);
                var name = Safe(() => ResolveModelTitle(creature.Monster, modelId), modelId);
                if (creature.PetOwner is { } owner)
                    return new($"summon:{owner.NetId}:{modelId}:{RuntimeHelpers.GetHashCode(creature)}",
                        AnalyticsEntityKind.Summon, owner.NetId, modelId, name);
                return new($"monster:{modelId}:{RuntimeHelpers.GetHashCode(creature)}",
                    AnalyticsEntityKind.Monster,
                    null, modelId, name);
            }

            var fallback = Safe(() => creature.ModelId.Entry, creature.GetType().Name);
            return new($"creature:{fallback}:{RuntimeHelpers.GetHashCode(creature)}",
                AnalyticsEntityKind.Unknown, null,
                fallback, fallback);
        }

        private static SourceDescriptor CreateModelSource(AbstractModel model)
        {
            var type = model.GetType();
            var id = Safe(() => model.Id.Entry, type.Name);
            var kind = ModelKinds.GetOrAdd(type, static value => ResolveModelKind(value));
            var name = ResolveModelTitle(model, id);
            return new($"{kind.ToString().ToLowerInvariant()}:{id}", kind, id, name);
        }

        private static AnalyticsSourceKind ResolveModelKind(Type type)
        {
            if (typeof(CardModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Card;
            if (typeof(PowerModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Power;
            if (typeof(PotionModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Potion;
            if (typeof(OrbModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Orb;
            if (typeof(RelicModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Relic;
            if (typeof(EnchantmentModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Enchantment;
            if (typeof(AfflictionModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Affliction;
            if (typeof(ModifierModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Modifier;
            if (typeof(CharacterModel).IsAssignableFrom(type))
                return AnalyticsSourceKind.Character;
            return typeof(MonsterModel).IsAssignableFrom(type)
                ? AnalyticsSourceKind.Creature
                : AnalyticsSourceKind.Unknown;
        }

        private static void ClearLocalizedSourceCaches()
        {
            _environmentSource = null;
            _damageFloorSource = null;
            _blockResolutionSource = null;
            _damageQuantizationSource = null;
            _overkillResolutionSource = null;
            _unknownSource = null;
            PlayerDescriptors.Clear();
            CreatureDescriptors.Clear();
            CreatureSources.Clear();
            ModelSources.Clear();
        }

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
