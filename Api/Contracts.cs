// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;

namespace STS2RitsuMetrics.Api
{
    public enum MetricValueKind
    {
        Amount,
        Count,
    }

    public enum AnalyticsEntityKind
    {
        Unknown,
        Player,
        Monster,
        Summon,
    }

    public enum AnalyticsSourceKind
    {
        Unknown,
        Card,
        Power,
        Potion,
        Orb,
        Relic,
        Enchantment,
        Affliction,
        Modifier,
        Character,
        Creature,
        System,
    }

    public sealed record MetricDefinition(
        string Id,
        string NameLocalizationKey,
        string FallbackName,
        MetricValueKind ValueKind,
        string Category,
        bool HigherIsBetter = true);

    public sealed record EntityDescriptor(
        string Key,
        AnalyticsEntityKind Kind,
        ulong? PlayerNetId,
        string ModelId,
        string DisplayName,
        string CharacterId = "");

    public sealed record SourceDescriptor(
        string Key,
        AnalyticsSourceKind Kind,
        string ModelId,
        string DisplayName);

    public sealed record MetricObservation(
        long Sequence,
        string RunId,
        string CombatId,
        int ActIndex,
        int Floor,
        int Round,
        DateTimeOffset OccurredAtUtc,
        string MetricId,
        decimal Value,
        EntityDescriptor Subject,
        EntityDescriptor? Target,
        SourceDescriptor Source,
        IReadOnlyDictionary<string, string> Tags)
    {
        public static IReadOnlyDictionary<string, string> EmptyTags { get; } =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public static class ObservationTagIds
    {
        public const string ActorKey = "ritsumetrics.actor.key";
        public const string ActorKind = "ritsumetrics.actor.kind";
        public const string ActorModelId = "ritsumetrics.actor.model_id";
        public const string ActorDisplayName = "ritsumetrics.actor.display_name";
        public const string ActorOwnerKey = "ritsumetrics.actor.owner_key";
        public const string ContributionComponent = "ritsumetrics.contribution.component";
        public const string AttributionConfidence = "ritsumetrics.attribution.confidence";
    }

    public static class ContributionComponentIds
    {
        public const string BaseDamage = "base_damage";
        public const string DamageAmplification = "damage_amplification";
        public const string DamageMitigation = "damage_mitigation";
        public const string Block = "block";
        public const string Healing = "healing";
        public const string Execution = "execution";
    }

    public sealed record SourceMetricSnapshot(
        string SourceKey,
        AnalyticsSourceKind SourceKind,
        string ModelId,
        string DisplayName,
        decimal Value,
        int Occurrences);

    public sealed record PlayerMetricSnapshot(
        string PlayerKey,
        ulong? PlayerNetId,
        string DisplayName,
        string CharacterId,
        IReadOnlyDictionary<string, decimal> Totals,
        IReadOnlyDictionary<string, IReadOnlyList<SourceMetricSnapshot>> Sources);

    public sealed record CombatSnapshot(
        string RunId,
        string CombatId,
        int ActIndex,
        int Floor,
        string EncounterId,
        string EncounterName,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        bool Completed,
        int RoundCount,
        IReadOnlyList<PlayerMetricSnapshot> Players,
        IReadOnlyList<MetricObservation> Events,
        IReadOnlyList<CombatTimelineEvent>? Timeline = null);

    public sealed record RunPlayerIdentity(
        ulong PlayerNetId,
        string CharacterId);

    public sealed record RunIdentitySnapshot(
        long? StartedAtUnixSeconds,
        string Seed,
        int GameMode,
        int AscensionLevel,
        long? DailyTimeUnixSeconds,
        IReadOnlyList<RunPlayerIdentity> Players,
        IReadOnlyList<string> ModifierIds);

    public sealed record RunSnapshot(
        string RunId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        bool IsMultiplayer,
        bool IsDaily,
        bool? IsVictory,
        bool? IsAbandoned,
        IReadOnlyList<CombatSnapshot> Combats)
    {
        public RunIdentitySnapshot? Identity { get; init; }
    }

    public sealed class MetricsQuery
    {
        public string? RunId { get; init; }
        public string? CombatId { get; init; }
        public ulong? PlayerNetId { get; init; }
        public int? ActIndex { get; init; }
        public int? MinimumFloor { get; init; }
        public int? MaximumFloor { get; init; }
        public DateTimeOffset? FromUtc { get; init; }
        public DateTimeOffset? ToUtc { get; init; }
        public IReadOnlyCollection<string>? MetricIds { get; init; }
        public bool IncludeEvents { get; init; }
        public bool IncludeTimeline { get; init; } = true;
        public int Limit { get; init; } = 100;
    }

    public sealed record MetricsQueryResult(
        IReadOnlyList<CombatSnapshot> Combats,
        IReadOnlyDictionary<string, decimal> Totals,
        int TotalMatches,
        bool WasTruncated);

    public enum MetricsExportFormat
    {
        Json,
        Csv,
    }

    public sealed class MetricsExportRequest
    {
        public MetricsQuery Query { get; init; } = new() { IncludeEvents = true, IncludeTimeline = true };
        public MetricsExportFormat Format { get; init; } = MetricsExportFormat.Json;
        public string? DestinationPath { get; init; }
        public bool IndentedJson { get; init; } = true;
    }

    public sealed record MetricsExportResult(
        bool Success,
        string Path,
        int CombatCount,
        int EventCount,
        string? Error = null);

    public interface IMetricCollector
    {
        string Id { get; }
        void OnObservation(MetricObservation observation);
        void OnCombatCompleted(CombatSnapshot combat);
    }

    public interface ITimelineCollector
    {
        string Id { get; }
        void OnTimelineEvent(CombatTimelineEvent timelineEvent);
        void OnCombatCompleted(CombatSnapshot combat);
    }

    public interface IRitsuMetricsApi
    {
        int Version { get; }
        IReadOnlyCollection<MetricDefinition> MetricDefinitions { get; }
        IReadOnlyCollection<DashboardDefinition> DashboardDefinitions { get; }
        IReadOnlyCollection<DashboardStyleDefinition> DashboardStyles { get; }
        IReadOnlyCollection<DashboardWindowInfo> DashboardWindows { get; }
        event Action<MetricObservation>? ObservationPublished;
        event Action<CombatTimelineEvent>? TimelineEventPublished;
        event Action? SnapshotChanged;
        bool RegisterMetric(MetricDefinition definition, bool replaceExisting = false);
        IDisposable RegisterCollector(IMetricCollector collector);
        IDisposable RegisterTimelineCollector(ITimelineCollector collector);
        IDisposable RegisterDashboard(IDashboardProvider provider, bool replaceExisting = false);
        IDisposable RegisterDashboardStyle(DashboardStyleDefinition style, bool replaceExisting = false);
        string? OpenDashboard(string dashboardId, DashboardWindowOptions? options = null);
        bool CloseDashboard(string instanceId);
        bool PublishCustomObservation(string ownerModId, MetricObservation observation);
        CombatSnapshot? GetLiveCombat();
        RunSnapshot? GetLiveRun();
        MetricsQueryResult Query(MetricsQuery query);
        TimelineQueryResult QueryTimeline(TimelineQuery query);
        MetricsExportResult Export(MetricsExportRequest request);
        string ResolveSourceDisplayName(AnalyticsSourceKind kind, string modelId, string fallback = "");
    }
}
