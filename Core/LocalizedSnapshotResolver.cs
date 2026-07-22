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
                Players = combat.Players.Select(player => Resolve(player, localizePlayers)).ToArray(),
                Events = combat.Events.Select(observation => Resolve(observation, localizePlayers)).ToArray(),
                Timeline = ResolveTimeline(combat.Timeline, localizePlayers, encounterName),
            };
        }

        private static PlayerMetricSnapshot Resolve(PlayerMetricSnapshot player, bool localizePlayer)
        {
            var sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(player.Sources.Count,
                StringComparer.Ordinal);
            foreach (var (metricId, values) in player.Sources)
                sources.Add(metricId, values.Select(Resolve).ToArray());

            if (!localizePlayer)
                return player with { Sources = sources };

            var characterId = string.IsNullOrWhiteSpace(player.CharacterId)
                ? player.PlayerKey
                : player.CharacterId;
            var displayName = LocalizedModelNameResolver.Resolve(AnalyticsSourceKind.Character, characterId,
                player.DisplayName);

            return player with { DisplayName = displayName, Sources = sources };
        }

        private static SourceMetricSnapshot Resolve(SourceMetricSnapshot source)
        {
            return source with
            {
                DisplayName = LocalizedModelNameResolver.Resolve(source.SourceKind, source.ModelId,
                    source.DisplayName),
            };
        }

        private static MetricObservation Resolve(MetricObservation observation, bool localizePlayers)
        {
            return observation with
            {
                Subject = Resolve(observation.Subject, localizePlayers),
                Target = observation.Target is null ? null : Resolve(observation.Target, localizePlayers),
                Source = Resolve(observation.Source),
                Tags = ResolveTags(observation.Tags, localizePlayers),
            };
        }

        private static CombatTimelineEvent[]? ResolveTimeline(
            IReadOnlyList<CombatTimelineEvent>? timeline,
            bool localizePlayers,
            string encounterName)
        {
            if (timeline == null)
                return null;
            var resolved = timeline.Select(timelineEvent => Resolve(timelineEvent, localizePlayers, encounterName))
                .ToArray();
            var byId = new Dictionary<string, CombatTimelineEvent>(resolved.Length, StringComparer.Ordinal);
            foreach (var timelineEvent in resolved)
                byId.TryAdd(timelineEvent.EventId, timelineEvent);
            for (var index = 0; index < resolved.Length; index++)
            {
                var timelineEvent = resolved[index];
                if (!timelineEvent.Details.TryGetValue("origin_event_id", out var originEventId) ||
                    !byId.TryGetValue(originEventId, out var origin) ||
                    string.IsNullOrWhiteSpace(origin.Source?.DisplayName) ||
                    !timelineEvent.Details.ContainsKey("cause_source_name"))
                    continue;
                var details = new Dictionary<string, string>(timelineEvent.Details, StringComparer.Ordinal)
                {
                    ["cause_source_name"] = origin.Source.DisplayName,
                };
                resolved[index] = timelineEvent with { Details = details };
            }

            return resolved;
        }

        private static CombatTimelineEvent Resolve(
            CombatTimelineEvent timelineEvent,
            bool localizePlayers,
            string encounterName)
        {
            var actor = timelineEvent.Actor is null ? null : Resolve(timelineEvent.Actor, localizePlayers);
            var target = timelineEvent.Target is null ? null : Resolve(timelineEvent.Target, localizePlayers);
            var source = timelineEvent.Source is null ? null : Resolve(timelineEvent.Source);
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
                    : Resolve(timelineEvent.Damage, localizePlayers),
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

        private static DamageBreakdown Resolve(DamageBreakdown damage, bool localizePlayers)
        {
            return damage with
            {
                Contributions = damage.Contributions.Select(contribution => contribution with
                {
                    Source = Resolve(contribution.Source),
                }).ToArray(),
                AttributionShares = damage.AttributionShares?.Select(share => share with
                {
                    Contributor = Resolve(share.Contributor, localizePlayers),
                    Source = Resolve(share.Source),
                }).ToArray(),
            };
        }

        private static EntityDescriptor Resolve(EntityDescriptor entity, bool localizePlayers)
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

        private static SourceDescriptor Resolve(SourceDescriptor source)
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
    }
}
