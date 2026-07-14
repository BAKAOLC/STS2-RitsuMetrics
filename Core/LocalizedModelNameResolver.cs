// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Models;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class LocalizedModelNameResolver
    {
        internal static string Resolve(AnalyticsSourceKind kind, string modelId, string fallback)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return fallback;
            try
            {
                var value = kind switch
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
                    _ => null,
                };
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        internal static string ResolveEncounter(string modelId, string fallback)
        {
            try
            {
                return ResolveModel<EncounterModel>(modelId) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string? ResolveModel<T>(string id) where T : AbstractModel
        {
            var model = ModelDb.GetByIdOrNull<T>(Id<T>(id));
            return model == null ? null : GameDescriptorFactory.ResolveModelTitle(model, string.Empty);
        }

        private static ModelId Id<T>(string entry) where T : AbstractModel
        {
            return new(ModelDb.GetCategory(typeof(T)), entry);
        }
    }
}
