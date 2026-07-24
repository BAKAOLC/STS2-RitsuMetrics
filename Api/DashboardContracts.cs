// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Api
{
    public enum DashboardDataScope
    {
        CurrentCombat,
        CurrentRun,
    }

    public static class BuiltInDashboardIds
    {
        public const string Overview = "ritsumetrics.overview";
        public const string Meter = "ritsumetrics.meter";
        public const string DamageContribution = "ritsumetrics.damage-contribution";
        public const string EffectiveHpDamageContribution = "ritsumetrics.effective-hp-damage-contribution";
        public const string DefenseContribution = "ritsumetrics.defense-contribution";
        public const string CardLog = "ritsumetrics.card-log";
        public const string ReceivedDamage = "ritsumetrics.received-damage";
        public const string Timeline = "ritsumetrics.timeline";
        public const string DamageBreakdown = "ritsumetrics.damage-breakdown";
        public const string PlayerPerformance = "ritsumetrics.player-performance";
        public const string SourceAnalysis = "ritsumetrics.source-analysis";
        public const string DefenseResources = "ritsumetrics.defense-resources";
        public const string CardsAndEffects = "ritsumetrics.cards-effects";
        public const string ContributionAnalysis = "ritsumetrics.contribution-analysis";
        public const string TurnAnalysis = "ritsumetrics.turn-analysis";
        public const string RunTrends = "ritsumetrics.run-trends";
        public const string CombatRecords = "ritsumetrics.combat-records";
    }

    public static class DashboardParameterIds
    {
        public const string MetricId = "metric_id";
        public const string FontSize = "font_size";
        public const string Layout = "layout";
        public const string SummonDisplay = "summon_display";
        public const string WindowOpacity = "window_opacity";
        public const string BackgroundOpacity = "background_opacity";
        public const string FullOpacityOnHover = "full_opacity_on_hover";
    }

    public static class DashboardParameterValues
    {
        public const string SingleLine = "single_line";
        public const string Standard = "standard";
        public const string MergeSummons = "merge";
        public const string SplitSummons = "split";
    }

    public sealed class DashboardDefinition
    {
        public required string Id { get; init; }
        public required string TitleLocalizationKey { get; init; }
        public required string FallbackTitle { get; init; }
        public string DescriptionLocalizationKey { get; init; } = string.Empty;
        public string FallbackDescription { get; init; } = string.Empty;
        public string DefaultStyleId { get; init; } = "ritsumetrics.dark";
        public float DefaultWidth { get; init; } = 460f;
        public float DefaultHeight { get; init; } = 480f;
        public float MinimumWidth { get; init; } = 280f;
        public float MinimumHeight { get; init; } = 160f;
        public bool AllowMultipleInstances { get; init; } = true;
    }

    public sealed class DashboardStyleDefinition
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string BackgroundColor { get; init; } = "10141EEB";
        public string HeaderColor { get; init; } = "171726FA";
        public string SurfaceColor { get; init; } = "191D2ADB";
        public string TrackColor { get; init; } = "080B13E8";
        public string BorderColor { get; init; } = "52627DF2";
        public string TextColor { get; init; } = "EDF2FFFF";
        public string SecondaryTextColor { get; init; } = "AEBBD1FF";
        public string PositiveColor { get; init; } = "78D78AFF";
        public string NegativeColor { get; init; } = "EF817AFF";
        public string WarningColor { get; init; } = "E6BE55FF";

        public IReadOnlyList<string> AccentColors { get; init; } =
            ["E65B4AFF", "56A8E8FF", "70C878FF", "D7A84DFF", "A77BD8FF", "52C7BDFF"];

        public int RowHeight { get; init; } = 38;
        public int FontSize { get; init; } = 16;
    }

    public sealed class DashboardWindowOptions
    {
        public DashboardDataScope Scope { get; init; } = DashboardDataScope.CurrentCombat;
        public string? StyleId { get; init; }
        public float? PositionX { get; init; }
        public float? PositionY { get; init; }
        public float? Width { get; init; }
        public float? Height { get; init; }
        public IReadOnlyDictionary<string, string>? Parameters { get; init; }
    }

    public sealed record DashboardWindowInfo(
        string InstanceId,
        string DashboardId,
        DashboardDataScope Scope,
        string StyleId,
        bool IsCollapsed,
        bool IsLocked)
    {
        public IReadOnlyDictionary<string, string> Parameters { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed record DashboardRenderContext(
        CombatSnapshot? Snapshot,
        RunSnapshot? Run,
        DashboardDataScope Scope,
        DashboardStyleDefinition Style,
        IReadOnlyDictionary<string, string> Parameters,
        bool ShowPercentages,
        Action<string, string?> SetParameter);

    [Flags]
    public enum DashboardDataComponents
    {
        None = 0,
        Metrics = 1 << 0,
        Events = 1 << 1,
        Timeline = 1 << 2,
        RunCombats = 1 << 3,
        All = Metrics | Events | Timeline | RunCombats,
    }

    public sealed record DashboardDataRequirements(
        DashboardDataComponents Components,
        IReadOnlyCollection<string>? MetricIds = null)
    {
        public static DashboardDataRequirements All { get; } = new(DashboardDataComponents.All);
    }

    public interface IDashboardDataConsumer
    {
        DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters);
    }

    public interface IDashboardRenderer : IDisposable
    {
        Control View { get; }
        void Refresh(DashboardRenderContext context);
    }

    public interface IDashboardRendererPresentation
    {
        string? Title { get; }
        string? Subtitle { get; }
        string? AccentColor { get; }
    }

    public interface IDashboardRendererFooterPresentation
    {
        void SetFooterContext(string? text);
    }

    public interface IDashboardProvider
    {
        DashboardDefinition Definition { get; }
        IDashboardRenderer CreateRenderer();
    }
}
