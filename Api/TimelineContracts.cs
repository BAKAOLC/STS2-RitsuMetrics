// SPDX-License-Identifier: MPL-2.0

namespace STS2RitsuMetrics.Api
{
    public enum CombatTimelineKind
    {
        Combat,
        Turn,
        Phase,
        HandDraw,
        CardDraw,
        CardPlay,
        CardMove,
        Attack,
        Damage,
        DamageModifier,
        Block,
        Healing,
        HpLoss,
        Power,
        Potion,
        Energy,
        Orb,
        Summon,
        Shuffle,
        Death,
        Execution,
        Effect,
        System,
        Custom,
        DamageSettlement,
    }

    public enum TimelineEventPhase
    {
        Instant,
        Started,
        Completed,
    }

    public enum TimelineTurnSide
    {
        None,
        Player,
        Enemy,
    }

    public enum DamageContributionStage
    {
        Base,
        Additive,
        Multiplicative,
        Cap,
        HpLoss,
        Block,
        Overkill,
        Execution,
        Clamp,
        Quantization,
    }

    public enum DamageContributionRole
    {
        Base,
        Modifier,
        Settlement,
    }

    public enum DamageSettlementKind
    {
        None,
        LowerBound,
        Block,
        Quantization,
        Overkill,
    }

    public enum AttributionConfidence
    {
        Exact,
        Derived,
        Heuristic,
        Unknown,
    }

    public sealed record DamageContribution(
        SourceDescriptor Source,
        DamageContributionStage Stage,
        decimal InputValue,
        decimal OutputValue,
        decimal RawContribution,
        decimal EffectiveContribution,
        decimal? Factor,
        AttributionConfidence Confidence,
        string Note = "");

    public static class DamageContributionSemantics
    {
        public static DamageContributionRole GetRole(DamageContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            if (GetSettlementKind(contribution) != DamageSettlementKind.None)
                return DamageContributionRole.Settlement;
            return contribution.Stage is DamageContributionStage.Base or DamageContributionStage.Execution
                ? DamageContributionRole.Base
                : DamageContributionRole.Modifier;
        }

        public static DamageSettlementKind GetSettlementKind(DamageContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            return contribution.Stage switch
            {
                DamageContributionStage.Clamp => DamageSettlementKind.LowerBound,
                DamageContributionStage.Block => DamageSettlementKind.Block,
                DamageContributionStage.Quantization => DamageSettlementKind.Quantization,
                DamageContributionStage.Overkill => DamageSettlementKind.Overkill,
                DamageContributionStage.Additive when IsLegacyLowerBound(contribution) =>
                    DamageSettlementKind.LowerBound,
                _ => DamageSettlementKind.None,
            };
        }

        private static bool IsLegacyLowerBound(DamageContribution contribution)
        {
            return contribution is
                   {
                       Source.Key: "system:environment", Confidence: AttributionConfidence.Derived,
                       InputValue: < 0m, OutputValue: 0m,
                   } &&
                   contribution.RawContribution == -contribution.InputValue;
        }
    }

    public sealed record DamageAttributionShare(
        EntityDescriptor Contributor,
        SourceDescriptor Source,
        decimal Weight,
        decimal EffectiveContribution,
        AttributionConfidence Confidence,
        string? OriginEventId = null);

    public sealed record DamageBreakdown(
        decimal RequestedAmount,
        decimal ModifiedAmount,
        decimal BlockedAmount,
        decimal HpLost,
        decimal OverkillAmount,
        decimal EffectiveAmount,
        string ValueProperties,
        IReadOnlyList<DamageContribution> Contributions,
        IReadOnlyList<DamageAttributionShare>? AttributionShares = null);

    public sealed record CombatTimelineEvent(
        long Sequence,
        string EventId,
        string? ParentEventId,
        string RunId,
        string CombatId,
        DateTimeOffset OccurredAtUtc,
        int Round,
        int TurnIndex,
        TimelineTurnSide Side,
        bool IsExtraTurn,
        CombatTimelineKind Kind,
        TimelineEventPhase Phase,
        string ActionId,
        string DisplayText,
        EntityDescriptor? Actor,
        EntityDescriptor? Target,
        SourceDescriptor? Source,
        decimal? Value,
        IReadOnlyDictionary<string, string> Details,
        DamageBreakdown? Damage = null);

    public sealed class TimelineQuery
    {
        public string? RunId { get; init; }
        public string? CombatId { get; init; }
        public string? RootEventId { get; init; }
        public ulong? PlayerNetId { get; init; }
        public int? MinimumRound { get; init; }
        public int? MaximumRound { get; init; }
        public TimelineTurnSide? Side { get; init; }
        public bool? IsExtraTurn { get; init; }
        public IReadOnlyCollection<CombatTimelineKind>? Kinds { get; init; }
        public DateTimeOffset? FromUtc { get; init; }
        public DateTimeOffset? ToUtc { get; init; }
        public int Limit { get; init; } = 5000;
    }

    public sealed record TimelineQueryResult(
        IReadOnlyList<CombatTimelineEvent> Events,
        int TotalMatches,
        bool WasTruncated);
}
