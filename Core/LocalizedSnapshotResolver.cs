// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Core
{
    internal static class LocalizedSnapshotResolver
    {
        internal static RunSnapshot Resolve(RunSnapshot run)
        {
            var localizePlayers = !run.IsMultiplayer;
            var resolver = new ResolutionContext();
            return run with
            {
                Combats = run.Combats.Select(combat => Resolve(combat, localizePlayers, resolver)).ToArray(),
            };
        }

        internal static CombatSnapshot Resolve(CombatSnapshot combat, bool localizePlayers)
        {
            return Resolve(combat, localizePlayers, new());
        }

        private static CombatSnapshot Resolve(
            CombatSnapshot combat,
            bool localizePlayers,
            ResolutionContext resolver)
        {
            var encounterName = resolver.ResolveEncounter(combat.EncounterId, combat.EncounterName);
            var players = ResolvePlayers(combat.Players, localizePlayers, resolver);
            var events = ResolveObservations(combat.Events, localizePlayers, resolver);
            var timeline = ResolveTimeline(combat.Timeline, localizePlayers, encounterName, resolver);
            return string.Equals(encounterName, combat.EncounterName, StringComparison.Ordinal) &&
                   ReferenceEquals(players, combat.Players) &&
                   ReferenceEquals(events, combat.Events) &&
                   ReferenceEquals(timeline, combat.Timeline)
                ? combat
                : combat with
                {
                    EncounterName = encounterName,
                    Players = players,
                    Events = events,
                    Timeline = timeline,
                };
        }

        private static IReadOnlyList<PlayerMetricSnapshot> ResolvePlayers(
            IReadOnlyList<PlayerMetricSnapshot> players,
            bool localizePlayers,
            ResolutionContext resolver)
        {
            PlayerMetricSnapshot[]? resolved = null;
            for (var index = 0; index < players.Count; index++)
            {
                var current = ResolvePlayer(players[index], localizePlayers, resolver);
                if (ReferenceEquals(current, players[index]) && resolved == null)
                    continue;
                resolved ??= players.ToArray();
                resolved[index] = current;
            }

            return resolved ?? players;
        }

        private static IReadOnlyList<MetricObservation> ResolveObservations(
            IReadOnlyList<MetricObservation> observations,
            bool localizePlayers,
            ResolutionContext resolver)
        {
            MetricObservation[]? resolved = null;
            for (var index = 0; index < observations.Count; index++)
            {
                var current = ResolveObservation(observations[index], localizePlayers, resolver);
                if (ReferenceEquals(current, observations[index]) && resolved == null)
                    continue;
                resolved ??= observations.ToArray();
                resolved[index] = current;
            }

            return resolved ?? observations;
        }

        private static PlayerMetricSnapshot ResolvePlayer(
            PlayerMetricSnapshot player,
            bool localizePlayer,
            ResolutionContext resolver)
        {
            Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>? sources = null;
            foreach (var (metricId, values) in player.Sources)
            {
                SourceMetricSnapshot[]? resolvedValues = null;
                for (var index = 0; index < values.Count; index++)
                {
                    var current = resolver.ResolveSourceMetric(values[index]);
                    if (ReferenceEquals(current, values[index]) && resolvedValues == null)
                        continue;
                    resolvedValues ??= values.ToArray();
                    resolvedValues[index] = current;
                }

                if (resolvedValues == null)
                    continue;
                sources ??= player.Sources.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal);
                sources[metricId] = resolvedValues;
            }

            if (!localizePlayer)
                return sources == null ? player : player with { Sources = sources };

            var characterId = string.IsNullOrWhiteSpace(player.CharacterId)
                ? player.PlayerKey
                : player.CharacterId;
            var displayName = resolver.Resolve(AnalyticsSourceKind.Character, characterId, player.DisplayName);

            return sources == null && string.Equals(displayName, player.DisplayName, StringComparison.Ordinal)
                ? player
                : player with
                {
                    DisplayName = displayName,
                    Sources = sources ?? player.Sources,
                };
        }

        private static MetricObservation ResolveObservation(
            MetricObservation observation,
            bool localizePlayers,
            ResolutionContext resolver)
        {
            var subject = ResolveEntity(observation.Subject, localizePlayers, resolver);
            var target = observation.Target is null
                ? null
                : ResolveEntity(observation.Target, localizePlayers, resolver);
            var source = ResolveSource(observation.Source, resolver);
            var tags = ResolveTags(observation.Tags, localizePlayers, resolver);
            return ReferenceEquals(subject, observation.Subject) &&
                   ReferenceEquals(target, observation.Target) &&
                   ReferenceEquals(source, observation.Source) &&
                   ReferenceEquals(tags, observation.Tags)
                ? observation
                : observation with
                {
                    Subject = subject,
                    Target = target,
                    Source = source,
                    Tags = tags,
                };
        }

        private static CombatTimelineEvent ResolveTimelineEvent(
            CombatTimelineEvent timelineEvent,
            bool localizePlayers,
            string encounterName,
            ResolutionContext resolver)
        {
            var actor = timelineEvent.Actor is null
                ? null
                : ResolveEntity(timelineEvent.Actor, localizePlayers, resolver);
            var target = timelineEvent.Target is null
                ? null
                : ResolveEntity(timelineEvent.Target, localizePlayers, resolver);
            var source = timelineEvent.Source is null ? null : ResolveSource(timelineEvent.Source, resolver);
            var displayText = timelineEvent.DisplayText;
            if (timelineEvent.ActionId is "combat.start" or "combat.resume" or "combat.end")
                displayText = encounterName;
            else if (Matches(displayText, timelineEvent.Source))
                displayText = source!.DisplayName;
            else if (Matches(displayText, timelineEvent.Target))
                displayText = target!.DisplayName;
            else if (Matches(displayText, timelineEvent.Actor))
                displayText = actor!.DisplayName;

            var damage = timelineEvent.Damage is null
                ? null
                : ResolveDamage(timelineEvent.Damage, localizePlayers, resolver);
            return string.Equals(displayText, timelineEvent.DisplayText, StringComparison.Ordinal) &&
                   ReferenceEquals(actor, timelineEvent.Actor) &&
                   ReferenceEquals(target, timelineEvent.Target) &&
                   ReferenceEquals(source, timelineEvent.Source) &&
                   ReferenceEquals(damage, timelineEvent.Damage)
                ? timelineEvent
                : timelineEvent with
                {
                    DisplayText = displayText,
                    Actor = actor,
                    Target = target,
                    Source = source,
                    Damage = damage,
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

        private static DamageBreakdown ResolveDamage(
            DamageBreakdown damage,
            bool localizePlayers,
            ResolutionContext resolver)
        {
            DamageContribution[]? contributions = null;
            for (var index = 0; index < damage.Contributions.Count; index++)
            {
                var contribution = damage.Contributions[index];
                var source = ResolveSource(contribution.Source, resolver);
                if (ReferenceEquals(source, contribution.Source) && contributions == null)
                    continue;
                contributions ??= damage.Contributions.ToArray();
                contributions[index] = contribution with { Source = source };
            }

            DamageAttributionShare[]? attributionShares = null;
            // ReSharper disable once InvertIf
            if (damage.AttributionShares != null)
                for (var index = 0; index < damage.AttributionShares.Count; index++)
                {
                    var share = damage.AttributionShares[index];
                    var contributor = ResolveEntity(share.Contributor, localizePlayers, resolver);
                    var source = ResolveSource(share.Source, resolver);
                    if (ReferenceEquals(contributor, share.Contributor) &&
                        ReferenceEquals(source, share.Source) &&
                        attributionShares == null)
                        continue;
                    attributionShares ??= damage.AttributionShares.ToArray();
                    attributionShares[index] = share with
                    {
                        Contributor = contributor,
                        Source = source,
                    };
                }

            return contributions == null && attributionShares == null
                ? damage
                : damage with
                {
                    Contributions = contributions ?? damage.Contributions,
                    AttributionShares = attributionShares ?? damage.AttributionShares,
                };
        }

        private static EntityDescriptor ResolveEntity(
            EntityDescriptor entity,
            bool localizePlayers,
            ResolutionContext resolver)
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
            var displayName = resolver.Resolve(sourceKind, modelId, entity.DisplayName);
            return string.Equals(displayName, entity.DisplayName, StringComparison.Ordinal)
                ? entity
                : entity with { DisplayName = displayName };
        }

        private static SourceDescriptor ResolveSource(SourceDescriptor source, ResolutionContext resolver)
        {
            var displayName = resolver.Resolve(source.Kind, source.ModelId, source.DisplayName);
            return string.Equals(displayName, source.DisplayName, StringComparison.Ordinal)
                ? source
                : source with { DisplayName = displayName };
        }

        private static IReadOnlyDictionary<string, string> ResolveTags(
            IReadOnlyDictionary<string, string> tags,
            bool localizePlayers,
            ResolutionContext resolver)
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
            if (!resolver.TryResolve(sourceKind, modelId, out var resolved))
                return tags;

            var copy = new Dictionary<string, string>(tags, StringComparer.Ordinal)
            {
                [ObservationTagIds.ActorDisplayName] = resolved,
            };
            return copy;
        }

        private static IReadOnlyList<CombatTimelineEvent>? ResolveTimeline(
            IReadOnlyList<CombatTimelineEvent>? timeline,
            bool localizePlayers,
            string encounterName,
            ResolutionContext resolver)
        {
            if (timeline is null)
                return null;

            CombatTimelineEvent[]? resolved = null;
            for (var index = 0; index < timeline.Count; index++)
            {
                var current = ResolveTimelineEvent(timeline[index], localizePlayers, encounterName, resolver);
                if (ReferenceEquals(current, timeline[index]) && resolved == null)
                    continue;
                resolved ??= timeline.ToArray();
                resolved[index] = current;
            }

            var currentTimeline = (IReadOnlyList<CombatTimelineEvent>?)resolved ?? timeline;
            var byId = new Dictionary<string, CombatTimelineEvent>(currentTimeline.Count, StringComparer.Ordinal);
            foreach (var timelineEvent in currentTimeline)
                byId.TryAdd(timelineEvent.EventId, timelineEvent);

            for (var index = 0; index < currentTimeline.Count; index++)
            {
                var timelineEvent = currentTimeline[index];
                if (!timelineEvent.Details.TryGetValue("origin_event_id", out var originEventId) ||
                    !timelineEvent.Details.TryGetValue("cause_source_name", out var causeSourceName) ||
                    !byId.TryGetValue(originEventId, out var origin) ||
                    string.IsNullOrWhiteSpace(origin.Source?.DisplayName) ||
                    string.Equals(causeSourceName, origin.Source.DisplayName, StringComparison.Ordinal))
                    continue;

                var details = new Dictionary<string, string>(timelineEvent.Details, StringComparer.Ordinal)
                {
                    ["cause_source_name"] = origin.Source.DisplayName,
                };
                resolved ??= timeline.ToArray();
                resolved[index] = timelineEvent with { Details = details };
            }

            return resolved ?? timeline;
        }

        private sealed class ResolutionContext
        {
            private readonly Dictionary<string, string?> _encounterNames = new(StringComparer.Ordinal);
            private readonly Dictionary<(AnalyticsSourceKind Kind, string ModelId), string?> _modelNames = [];

            internal string Resolve(AnalyticsSourceKind kind, string modelId, string fallback)
            {
                return TryResolve(kind, modelId, out var resolved) ? resolved : fallback;
            }

            internal bool TryResolve(AnalyticsSourceKind kind, string modelId, out string value)
            {
                var key = (kind, modelId);
                if (!_modelNames.TryGetValue(key, out var resolved))
                {
                    resolved = LocalizedModelNameResolver.TryResolve(kind, modelId, out var current)
                        ? current
                        : null;
                    _modelNames.Add(key, resolved);
                }

                value = resolved ?? string.Empty;
                return resolved != null;
            }

            internal string ResolveEncounter(string modelId, string fallback)
            {
                // ReSharper disable once InvertIf
                if (!_encounterNames.TryGetValue(modelId, out var resolved))
                {
                    resolved = LocalizedModelNameResolver.TryResolveEncounter(modelId, out var current)
                        ? current
                        : null;
                    _encounterNames.Add(modelId, resolved);
                }

                return resolved ?? fallback;
            }

            internal SourceMetricSnapshot ResolveSourceMetric(SourceMetricSnapshot source)
            {
                var displayName = Resolve(source.SourceKind, source.ModelId, source.DisplayName);
                return string.Equals(displayName, source.DisplayName, StringComparison.Ordinal)
                    ? source
                    : source with { DisplayName = displayName };
            }
        }
    }
}
