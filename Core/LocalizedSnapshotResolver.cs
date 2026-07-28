// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class LocalizedSnapshotResolver
    {
        internal static RunSnapshot Resolve(RunSnapshot run)
        {
            var localizePlayers = !run.IsMultiplayer;
            return run with
            {
                Combats = run.Combats.Select(combat => Resolve(combat, localizePlayers)).ToArray(),
            };
        }

        internal static CombatSnapshot Resolve(CombatSnapshot combat, bool localizePlayers)
        {
            var encounterName = LocalizedModelNameResolver.ResolveEncounter(combat.EncounterId,
                combat.EncounterName);
            return combat with
            {
                EncounterName = encounterName,
                Players = combat.Players.Select(player => ResolvePlayer(player, localizePlayers)).ToArray(),
                Events = combat.Events.Select(observation => ResolveObservation(observation, localizePlayers))
                    .ToArray(),
                Timeline = ResolveTimeline(combat.Timeline, localizePlayers, encounterName),
            };
        }

        private static PlayerMetricSnapshot ResolvePlayer(PlayerMetricSnapshot player, bool localizePlayer)
        {
            var sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(player.Sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in player.Sources)
                sources.Add(metricId, values.Select(ResolveSourceMetric).ToArray());

            if (!localizePlayer)
                return player with { Sources = sources };

            var characterId = string.IsNullOrWhiteSpace(player.CharacterId)
                ? player.PlayerKey
                : player.CharacterId;
            var displayName = LocalizedModelNameResolver.Resolve(AnalyticsSourceKind.Character, characterId,
                player.DisplayName);

            return player with { DisplayName = displayName, Sources = sources };
        }

        private static SourceMetricSnapshot ResolveSourceMetric(SourceMetricSnapshot source)
        {
            return source with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(source.SourceKind, source.ModelId,
                    source.DisplayName),
            };
        }

        private static MetricObservation ResolveObservation(MetricObservation observation, bool localizePlayers)
        {
            return observation with
            {
                Subject = ResolveEntity(observation.Subject, localizePlayers),
                Target = observation.Target is null ? null : ResolveEntity(observation.Target, localizePlayers),
                Source = ResolveSource(observation.Source),
                Tags = ResolveTags(observation.Tags, localizePlayers),
            };
        }

        private static CombatTimelineEvent ResolveTimelineEvent(
            CombatTimelineEvent timelineEvent,
            bool localizePlayers,
            string encounterName)
        {
            var actor = timelineEvent.Actor is null ? null : ResolveEntity(timelineEvent.Actor, localizePlayers);
            var target = timelineEvent.Target is null ? null : ResolveEntity(timelineEvent.Target, localizePlayers);
            var source = timelineEvent.Source is null ? null : ResolveSource(timelineEvent.Source);
            var displayText = timelineEvent.DisplayText;
            if (timelineEvent.ActionId is "combat.start" or "combat.resume" or "combat.end")
                displayText = encounterName;
            else if (Matches(displayText, timelineEvent.Source))
                displayText = source!.DisplayName;
            else if (Matches(displayText, timelineEvent.Target))
                displayText = target!.DisplayName;
            else if (Matches(displayText, timelineEvent.Actor))
                displayText = actor!.DisplayName;

            return timelineEvent with
            {
                DisplayText = displayText,
                Actor = actor,
                Target = target,
                Source = source,
                Damage = timelineEvent.Damage is null
                    ? null
                    : ResolveDamage(timelineEvent.Damage, localizePlayers),
            };
        }

        private static bool Matches(string displayText, SourceDescriptor? source)
        {
            return source != null &&
                   (string.Equals(displayText, source.DisplayName, StringComparison.Ordinal) ||
                    string.Equals(displayText, source.ModelId, StringComparison.Ordinal));
        }

        private static bool Matches(string displayText, EntityDescriptor? entity)
        {
            return entity != null &&
                   (string.Equals(displayText, entity.DisplayName, StringComparison.Ordinal) ||
                    string.Equals(displayText, entity.ModelId, StringComparison.Ordinal));
        }

        private static DamageBreakdown ResolveDamage(DamageBreakdown damage, bool localizePlayers)
        {
            return damage with
            {
                Contributions = damage.Contributions.Select(contribution => contribution with
                {
                    Source = ResolveSource(contribution.Source),
                }).ToArray(),
                AttributionShares = damage.AttributionShares?.Select(share => share with
                {
                    Contributor = ResolveEntity(share.Contributor, localizePlayers),
                    Source = ResolveSource(share.Source),
                }).ToArray(),
            };
        }

        private static EntityDescriptor ResolveEntity(EntityDescriptor entity, bool localizePlayers)
        {
            var sourceKind = entity.Kind switch
            {
                AnalyticsEntityKind.Monster or AnalyticsEntityKind.Summon => AnalyticsSourceKind.Creature,
                AnalyticsEntityKind.Player when localizePlayers => AnalyticsSourceKind.Character,
                _ => AnalyticsSourceKind.Unknown,
            };
            var modelId = entity.Kind == AnalyticsEntityKind.Player && !string.IsNullOrWhiteSpace(entity.CharacterId)
                ? entity.CharacterId
                : entity.ModelId;
            return entity with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(sourceKind, modelId, entity.DisplayName),
            };
        }

        private static SourceDescriptor ResolveSource(SourceDescriptor source)
        {
            return source with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(source.Kind, source.ModelId, source.DisplayName),
            };
        }

        private static IReadOnlyDictionary<string, string> ResolveTags(
            IReadOnlyDictionary<string, string> tags,
            bool localizePlayers)
        {
            if (!tags.TryGetValue(ObservationTagIds.ActorKind, out var kindText) ||
                !Enum.TryParse<AnalyticsEntityKind>(kindText, out var kind) ||
                !tags.TryGetValue(ObservationTagIds.ActorModelId, out var modelId) ||
                !tags.ContainsKey(ObservationTagIds.ActorDisplayName))
                return tags;

            var sourceKind = kind switch
            {
                AnalyticsEntityKind.Monster or AnalyticsEntityKind.Summon => AnalyticsSourceKind.Creature,
                AnalyticsEntityKind.Player when localizePlayers => AnalyticsSourceKind.Character,
                _ => AnalyticsSourceKind.Unknown,
            };
            if (!LocalizedModelNameResolver.TryResolve(sourceKind, modelId, out var resolved))
                return tags;

            var copy = new Dictionary<string, string>(tags, StringComparer.Ordinal)
            {
                [ObservationTagIds.ActorDisplayName] = resolved,
            };
            return copy;
        }

        private static CombatTimelineEvent[]? ResolveTimeline(
            IReadOnlyList<CombatTimelineEvent>? timeline,
            bool localizePlayers,
            string encounterName)
        {
            if (timeline is null)
                return null;

            var resolved = timeline.Select(timelineEvent =>
                    ResolveTimelineEvent(timelineEvent, localizePlayers, encounterName))
                .ToArray();
            var byId = new Dictionary<string, CombatTimelineEvent>(resolved.Length, StringComparer.Ordinal);
            foreach (var timelineEvent in resolved)
                byId.TryAdd(timelineEvent.EventId, timelineEvent);

            for (var index = 0; index < resolved.Length; index++)
            {
                var timelineEvent = resolved[index];
                if (!timelineEvent.Details.TryGetValue("origin_event_id", out var originEventId) ||
                    !timelineEvent.Details.ContainsKey("cause_source_name") ||
                    !byId.TryGetValue(originEventId, out var origin) ||
                    string.IsNullOrWhiteSpace(origin.Source?.DisplayName))
                    continue;

                var details = new Dictionary<string, string>(timelineEvent.Details, StringComparer.Ordinal)
                {
                    ["cause_source_name"] = origin.Source.DisplayName,
                };
                resolved[index] = timelineEvent with { Details = details };
            }

            return resolved;
        }
    }
}
