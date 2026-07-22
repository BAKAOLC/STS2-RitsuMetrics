// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Models;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Core
{
    internal static class LocalizedModelNameResolver
    {
        internal static string Resolve(AnalyticsSourceKind kind, string modelId, string fallback)
        {
            return TryResolve(kind, modelId, out var value) ? value : fallback;
        }

        internal static string ResolveEncounter(string modelId, string fallback)
        {
            return TryResolveEncounter(modelId, out var value) ? value : fallback;
        }

        internal static bool TryResolve(AnalyticsSourceKind kind, string modelId, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(modelId))
                return false;
            try
            {
                var resolved = kind switch
                {
                    AnalyticsSourceKind.Card => ResolveModel<CardModel>(modelId),
                    AnalyticsSourceKind.Power => ResolveModel<PowerModel>(modelId),
                    AnalyticsSourceKind.Potion => ResolveModel<PotionModel>(modelId),
                    AnalyticsSourceKind.Orb => ResolveModel<OrbModel>(modelId),
                    AnalyticsSourceKind.Relic => ResolveModel<RelicModel>(modelId),
                    AnalyticsSourceKind.Enchantment => ResolveModel<EnchantmentModel>(modelId),
                    AnalyticsSourceKind.Affliction => ResolveModel<AfflictionModel>(modelId),
                    AnalyticsSourceKind.Modifier => ResolveModel<ModifierModel>(modelId),
                    AnalyticsSourceKind.Character => ResolveModel<CharacterModel>(modelId),
                    AnalyticsSourceKind.Creature => ResolveModel<MonsterModel>(modelId),
                    AnalyticsSourceKind.System or AnalyticsSourceKind.Unknown => ResolveSystem(modelId),
                    _ => null,
                };
                if (string.IsNullOrWhiteSpace(resolved))
                    return false;
                value = resolved;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryResolveEncounter(string modelId, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(modelId))
                return false;
            try
            {
                var resolved = ResolveModel<EncounterModel>(modelId);
                if (string.IsNullOrWhiteSpace(resolved))
                    return false;
                value = resolved;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? ResolveModel<T>(string id) where T : AbstractModel
        {
            var model = ModelDb.GetByIdOrNull<T>(Id<T>(id));
            var value = model == null ? null : GameDescriptorFactory.ResolveModelTitle(model, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static ModelId Id<T>(string entry) where T : AbstractModel
        {
            return new(ModelDb.GetCategory(typeof(T)), entry);
        }

        private static string? ResolveSystem(string id)
        {
            return id switch
            {
                "environment" => ModLocalization.Get("source.environment", "Environment"),
                "damage-floor" => ModLocalization.Get("source.damageFloor", "Damage floor"),
                "block-resolution" => ModLocalization.Get("source.blockResolution", "Block absorption"),
                "damage-quantization" =>
                    ModLocalization.Get("source.damageQuantization", "Integer HP settlement"),
                "overkill-resolution" => ModLocalization.Get("source.overkillResolution", "HP limit"),
                "unknown" => ModLocalization.Get("source.unknown", "Unknown source"),
                _ => null,
            };
        }
    }
}
