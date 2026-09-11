// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class OverviewRenderer
    {
        private const int PlayerSummaryMaximumColumns = 2;
        private const int PlayerKpiColumns = 4;
        private const float PlayerKpiMinimumColumnWidth = 112f;
        private const int PlayerKpiHorizontalSeparation = 5;
        private const int PlayerCardPadding = 9;

        private static readonly OverviewSectionDefinition[] Sections =
        [
            new(OverviewSection.Offense, "overview.offense", "Damage", MetricIds.DamageDealt,
            [
                MetricIds.DamageDealt, MetricIds.EffectiveHpDamageDealt, MetricIds.DamageContribution,
                MetricIds.Overkill,
            ], 0),
            new(OverviewSection.Defense, "overview.defense", "Survival", MetricIds.DamageTaken,
                [MetricIds.DamageTaken, MetricIds.DamageBlocked, MetricIds.BlockGained, MetricIds.HealingReceived], 2),
            new(OverviewSection.Resources, "overview.resources", "Actions", MetricIds.CardsPlayed,
                [MetricIds.EnergySpent, MetricIds.CardsPlayed, MetricIds.CardsDrawn, MetricIds.PotionsUsed], 3),
            new(OverviewSection.Analysis, "overview.analysis", "Support", MetricIds.DefenseContribution,
                [
                    MetricIds.DamageAmplified, MetricIds.DamageMitigated, MetricIds.HealingContribution,
                    MetricIds.DefenseContribution,
                ], 4),
        ];

        private void RenderOverview(DashboardRenderContext context)
        {
            _lastOverviewContext = context;
            Title = ModLocalization.Get("dashboard.overview", "Multidimensional overview");
            Subtitle = OverviewScopeName(context.Scope);
            var snapshot = context.Snapshot;
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                Empty(context);
                return;
            }

            var players = snapshot.Players.OrderByDescending(player => Metric(player, MetricIds.DamageDealt))
                .ToArray();
            var snapshotAnalysis = SnapshotAnalysisCache.Get(snapshot);
            var totalDamage = players.Sum(player => Metric(player, MetricIds.DamageDealt));
            var knownTotalDamage = MetricAvailability.Total(players.Select(player =>
                MetricAvailability.Value(player, MetricIds.DamageDealt)));
            var summaryHeader = SectionTitle(ModLocalization.Get("overview.playerSummary", "Player summary"),
                ModLocalization.Format("overview.playerSummary.meta", "{0} players · {1} rounds · {2} damage",
                    players.Length, snapshot.RoundCount, knownTotalDamage is { } known ? Format(known) : "N/A"), context.Style,
                Accent(context.Style, 0));
            var configure = new Button { TooltipText = ModLocalization.Get("overview.configure", "Displayed metrics") };
            DashboardControlTheme.ApplyIconButton(configure, dashboardStyle: context.Style, compact: true);
            DashboardIcons.ApplyIconOnly(configure, DashboardIcon.Configure, 17);
            configure.Pressed += OpenMetricConfiguration;
            summaryHeader.AddChild(configure);
            Rows.AddChild(summaryHeader);
            Rows.AddChild(BuildPlayerSummary(players, snapshot, totalDamage, context.Style,
                DashboardPresentation.SingleLine(context.Parameters)));

            if (ModData.Settings.OverviewShowCharts)
            {
                Rows.AddChild(SectionTitle(ModLocalization.Get("overview.combatFlow", "Combat flow"),
                    ModLocalization.Get("overview.combatFlow.meta", "Damage sources, composition and turn trends"),
                    context.Style, Accent(context.Style, 3)));
                var flow = ResponsiveGrid(2, 280f);
                flow.AddChild(BuildTopSources(players, MetricIds.DamageDealt, "overview.topSources.ad",
                    "Top AD sources", context.Style, 0));
                flow.AddChild(BuildTopSources(players, MetricIds.DamageContribution, "overview.topSources.rd",
                    "Top RD sources", context.Style, 4));
                if (!snapshot.HasIncompleteDetails && Timeline(snapshot).Count > 0)
                {
                    flow.AddChild(BuildTrend(context, Sections[(int)OverviewSection.Offense]));
                    flow.AddChild(BuildTrend(context, Sections[(int)OverviewSection.Defense],
                        ModLocalization.Get("overview.incomingDamage", "Incoming damage")));
                }
                else
                    flow.AddChild(Unavailable(context.Style));
                Rows.AddChild(flow);

            }

            if (ModData.Settings.OverviewShowAnalysis)
            {
                Rows.AddChild(SectionTitle(ModLocalization.Get("overview.combatAnalysis", "Combat analysis"),
                    ModLocalization.Get("overview.combatAnalysis.meta", "Outcome quality and resource efficiency"),
                    context.Style,
                    Accent(context.Style, 4)));
                var analysisGrid = ResponsiveGrid(2, 280f);
                var analysisMetrics = new[] { MetricIds.DamageDealt, MetricIds.EffectiveHpDamageDealt,
                    MetricIds.DamageTaken, MetricIds.DamageBlocked, MetricIds.BlockGained, MetricIds.Overkill };
                if (players.All(player => analysisMetrics.All(id => MetricAvailability.Value(player, id) != null)) &&
                    !snapshot.HasIncompleteDetails && Timeline(snapshot).Count > 0)
                {
                    analysisGrid.AddChild(BuildCombatEfficiency(snapshot, snapshotAnalysis, context.Style));
                    analysisGrid.AddChild(BuildDamageOutcome(snapshotAnalysis, context.Style));
                }
                else
                    analysisGrid.AddChild(Unavailable(context.Style));
                Rows.AddChild(analysisGrid);

            }

            AccentColor = Accent(context.Style, 0);
            Status.Text = ModLocalization.Format("overview.status", "{0} · {1} players · {2} rounds",
                snapshot.EncounterName, players.Length, snapshot.RoundCount);
        }

        private static GridContainer BuildPlayerSummary(
            PlayerMetricSnapshot[] players,
            CombatSnapshot snapshot,
            decimal totalDamage,
            DashboardStyleDefinition style,
            bool singleLine)
        {
            var minimumCardWidth = DashboardPresentation.MinimumGridWidth(
                PlayerKpiColumns,
                PlayerKpiMinimumColumnWidth,
                PlayerKpiHorizontalSeparation,
                PlayerCardPadding);
            var grid = ResponsiveGrid(PlayerSummaryMaximumColumns, minimumCardWidth);
            var playerAccents = PlayerAccents(players, style);
            var metrics = SelectedMetrics();
            var hasDamage = players.All(player => MetricAvailability.Value(player, MetricIds.DamageDealt) != null);
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var accent = playerAccents[player.PlayerKey];
                var damage = Metric(player, MetricIds.DamageDealt);
                var body = new VBoxContainer();
                body.AddThemeConstantOverride("separation", 6);
                body.AddChild(hasDamage
                    ? PlayerHeader(player, index + 1, damage, totalDamage, accent, style, singleLine)
                    : Label(player.DisplayName, style));
                var kpis = ResponsiveGrid(
                    PlayerKpiColumns,
                    PlayerKpiMinimumColumnWidth,
                    PlayerKpiHorizontalSeparation,
                    PlayerKpiHorizontalSeparation);
                foreach (var metric in metrics)
                {
                    var value = OverviewMetricSelection.Value(player, snapshot, metric.Id);
                    var color = metric.Id switch
                    {
                        MetricIds.DamageContribution => Accent(style, 4),
                        "overview.damage_per_turn" => Accent(style, 1),
                        MetricIds.Deaths => style.NegativeColor,
                        "overview.hp_loss_ratio" or MetricIds.Overkill => style.WarningColor,
                        MetricIds.CardsPlayed => Accent(style, 3),
                        _ => metric.Category is "defense" or "contribution" ? style.PositiveColor : accent,
                    };
                    var kpi = Kpi(metric.NameLocalizationKey, metric.FallbackName, value, color, style,
                        suffix: OverviewMetricSelection.Suffix(metric.Id));
                    if (value != null)
                        DashboardTooltip.Set(kpi,
                        [
                            ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName),
                            ModLocalization.Get(metric.NameLocalizationKey + ".description", metric.FallbackName),
                            Format(value.Value),
                        ]);
                    kpis.AddChild(kpi);
                }
                body.AddChild(kpis);
                grid.AddChild(Surface(body, style, accent, PlayerCardPadding));
            }

            return grid;
        }

        private static Control BuildTopSources(
            IReadOnlyList<PlayerMetricSnapshot> players,
            string metricId,
            string titleKey,
            string titleFallback,
            DashboardStyleDefinition style,
            int accentIndex)
        {
            if (players.Any(player => MetricAvailability.Value(player, metricId) == null))
                return Unavailable(style);
            var body = ChartBody(ModLocalization.Get(titleKey, titleFallback), style, Accent(style, accentIndex));
            var sources = AggregateSources(players, metricId)
                .OrderByDescending(source => source.Value).Take(8).ToArray();
            if (sources.Length == 0)
            {
                body.AddChild(WrappedLabel(ModLocalization.Get("overlay.noSource", "No source breakdown"), style,
                    true));
                return Surface(body, style);
            }

            var chart = new DashboardBarChart();
            chart.SetData(sources.Select((source, index) => new DashboardBarDatum(source.Name, source.Value,
                SourceColor(source.Kind, style, index), Format(source.Value))), Math.Max(11, style.FontSize - 1));
            body.AddChild(chart);

            return Surface(body, style, Accent(style, accentIndex));
        }

        private static Control Unavailable(DashboardStyleDefinition style)
        {
            return Surface(WrappedLabel(ModLocalization.Get("overview.unavailable",
                "N/A: not captured, incomplete attribution, or an undefined ratio."), style, true), style);
        }

        private static Control BuildTrend(
            DashboardRenderContext context,
            OverviewSectionDefinition definition,
            string? title = null)
        {
            var style = context.Style;
            var sectionName = title ?? ModLocalization.Get(definition.LocalizationKey, definition.FallbackName);
            if (context is
                {
                    Scope: DashboardDataScope.CurrentRun,
                    Run: { Combats.Count: > 0 } run,
                })
                return BuildCombatTrend(run, definition, sectionName, style);

            var snapshot = context.Snapshot!;
            var body = ChartBody(ModLocalization.Format("overview.turnTrend.section", "{0} turn trend", sectionName),
                style, Accent(style, definition.AccentIndex));
            var turns = SnapshotAnalysisCache.Get(snapshot).Turns
                .Where(turn => turn.Index > 0)
                .Select(turn => new TurnPoint(turn.OccurredAtUtc, turn.Index,
                    TurnValue(turn, definition.Section)))
                .Where(item => item.Value > 0m)
                .OrderBy(item => item.OccurredAtUtc).TakeLast(16).ToArray();

            if (turns.Length == 0)
            {
                body.AddChild(WrappedLabel(ModLocalization.Get("overview.noTurnData", "No turn data"), style, true));
            }
            else
            {
                var chart = new DashboardLineChart();
                chart.SetData(turns.Select(turn => new DashboardLineDatum($"T{turn.TurnIndex}", turn.Value)),
                    Accent(style, definition.AccentIndex), Math.Max(11, style.FontSize - 1));
                body.AddChild(chart);
            }

            return Surface(body, style, Accent(style, definition.AccentIndex));
        }

        private static Control BuildCombatTrend(
            RunSnapshot run,
            OverviewSectionDefinition definition,
            string sectionName,
            DashboardStyleDefinition style)
        {
            var body = ChartBody(ModLocalization.Format("overview.combatTrend.section", "{0} by combat", sectionName),
                style, Accent(style, definition.AccentIndex));
            var combats = run.Combats.OrderBy(combat => combat.StartedAtUtc).ToArray();
            var points = combats.Select((combat, index) => new DashboardLineDatum(
                ModLocalization.Format("analysis.floorShort", "F{0}", combat.Floor),
                CombatTrendValue(combat, definition),
                ModLocalization.Format("analysis.runTrendPoint", "#{0} · Act {1} · {2} · {3} rounds",
                    index + 1, combat.ActIndex + 1, combat.EncounterName, combat.RoundCount))).ToArray();
            if (points.All(point => point.Value <= 0m))
            {
                body.AddChild(WrappedLabel(ModLocalization.Get("overview.noTurnData", "No trend data"), style, true));
            }
            else
            {
                var chart = new DashboardLineChart();
                chart.SetData(points, Accent(style, definition.AccentIndex), Math.Max(11, style.FontSize - 1),
                    DashboardLineSeriesKind.Combat);
                body.AddChild(chart);
            }

            return Surface(body, style, Accent(style, definition.AccentIndex));
        }

        private static decimal CombatTrendValue(
            CombatSnapshot combat,
            OverviewSectionDefinition definition)
        {
            return definition.Section switch
            {
                OverviewSection.Offense => combat.Players.Sum(player =>
                    MetricForDisplay(player, MetricIds.DamageDealt)),
                OverviewSection.Defense => combat.Players.Sum(player =>
                    SnapshotStatistics.Survival(combat, player.PlayerNetId).PlayerHpLost +
                    MetricForDisplay(player, MetricIds.DamageBlocked)),
                _ => combat.Players.Sum(player => MetricForDisplay(player, definition.PrimaryMetric)),
            };
        }

        private static Control BuildCombatEfficiency(
            CombatSnapshot snapshot,
            SnapshotAnalysis analysis,
            DashboardStyleDefinition style)
        {
            var body = ChartBody(ModLocalization.Get("overview.efficiency", "Combat efficiency"), style,
                style.WarningColor);
            body.AddChild(SegmentedMeter(
                $"{ModLocalization.Get("overview.damageConversion", "HP conversion")} " +
                $"{analysis.DamageConversion:P1}",
                analysis.HpDamage,
                analysis.EnemyBlockedDamage + analysis.Overkill,
                style.NegativeColor,
                style.WarningColor,
                style,
                ModLocalization.Get("metric.effectiveHpDamageDealt", "HP damage"),
                ModLocalization.Get("overview.nonHpDamage", "Blocked + overkill"),
                ModLocalization.Get("analysis.hpConversion.hint",
                    "HP conversion is HP damage divided by HP damage plus enemy block and overkill. Higher " +
                    "means more outgoing damage reduced enemy HP.")));
            body.AddChild(SegmentedMeter(
                $"{ModLocalization.Get("analysis.hpLossRatio", "HP loss ratio")} {analysis.HpLossRatio:P1}",
                analysis.HpLost,
                analysis.EffectiveBlock,
                style.NegativeColor,
                style.PositiveColor,
                style,
                ModLocalization.Get("analysis.hpLost", "HP lost"),
                ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                ModLocalization.Get("analysis.hpLossRatio.hint",
                    "HP loss ratio is HP lost divided by HP lost plus effective block. Lower means more " +
                    "incoming damage was absorbed.")));
            body.AddChild(SegmentedMeter(
                $"{ModLocalization.Get("analysis.blockEfficiency", "Block efficiency")} " +
                $"{analysis.BlockEfficiency:P1}",
                analysis.EffectiveBlock,
                analysis.UnspentBlock,
                style.PositiveColor,
                style.WarningColor,
                style,
                ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                ModLocalization.Get("analysis.unspentBlock", "Unspent block"),
                ModLocalization.Get("analysis.blockEfficiency.hint",
                    "Block efficiency is effective block divided by block gained. Unspent block may still be " +
                    "usable until combat ends.")));

            var rounds = Math.Max(1, snapshot.RoundCount);
            var metrics = ResponsiveGrid(3, 110f, 10);
            AddRecord("analysis.damagePerTurn", "Damage / turn", analysis.AppliedDamage / rounds,
                Accent(style, 1));
            AddRecord("analysis.damagePerEnergy", "Damage / energy",
                analysis.EnergySpent > 0m ? analysis.AppliedDamage / analysis.EnergySpent : 0m, Accent(style, 4));
            AddRecord("analysis.cardsPerTurn", "Cards / turn", analysis.CardsPlayed / rounds, Accent(style, 3));
            AddRecord("analysis.healing", "Healing", analysis.Healing, style.PositiveColor);
            AddRecord("analysis.selfHpLost", "Self-inflicted HP loss", analysis.SelfHpLost, style.WarningColor);
            AddRecord("analysis.unspentBlock", "Unspent block", analysis.UnspentBlock, style.WarningColor);
            body.AddChild(metrics);
            return Surface(body, style, padding: 9);

            void AddRecord(string localizationKey, string fallback, decimal value, string color)
            {
                var record = Kpi(localizationKey, fallback, value, color, style, true);
                record.CustomMinimumSize = new(0f, Math.Max(54f, style.FontSize + 36f));
                metrics.AddChild(record);
            }
        }

        private static Control BuildDamageOutcome(SnapshotAnalysis analysis, DashboardStyleDefinition style)
        {
            var body = ChartBody(ModLocalization.Get("overview.damageOutcome", "Damage outcome"), style,
                Accent(style, 4));
            var outcomes = new[]
                {
                    new DashboardDonutDatum(ModLocalization.Get("metric.effectiveHpDamageDealt", "HP damage"),
                        analysis.HpDamage, style.NegativeColor),
                    new DashboardDonutDatum(ModLocalization.Get("overview.enemyBlocked", "Blocked by enemies"),
                        analysis.EnemyBlockedDamage, style.PositiveColor),
                    new DashboardDonutDatum(ModLocalization.Get("analysis.overkill", "Overkill"),
                        analysis.Overkill, style.WarningColor),
                }
                .Where(item => item.Value > 0m)
                .ToArray();
            if (outcomes.Length == 0)
            {
                body.AddChild(WrappedLabel(ModLocalization.Get("overview.noDamageData", "No damage data"), style,
                    true));
                return Surface(body, style, padding: 9);
            }

            var chart = new DashboardDonutChart();
            chart.SetData(outcomes, Math.Max(11, style.FontSize - 1));
            body.AddChild(chart);
            return Surface(body, style, padding: 9);
        }

        private static VBoxContainer ChartBody(string title, DashboardStyleDefinition style, string accent)
        {
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 8);
            var header = new HBoxContainer { CustomMinimumSize = new(0f, 26f) };
            header.AddThemeConstantOverride("separation", 7);
            header.AddChild(new ColorRect
            {
                Color = ColorOf(accent),
                CustomMinimumSize = new(3f, 17f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            var heading = TruncatedLabel(title, style, false, style.FontSize + 2);
            heading.VerticalAlignment = VerticalAlignment.Center;
            header.AddChild(heading);
            body.AddChild(header);
            return body;
        }

        private static HBoxContainer SectionTitle(
            string title,
            string meta,
            DashboardStyleDefinition style,
            string accent)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var heading = TruncatedLabel(title, style, false, style.FontSize + 3);
            heading.Modulate = ColorOf(accent);
            row.AddChild(heading);
            var details = Label(meta, style, true, Math.Max(10, style.FontSize - 1));
            details.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(details);
            return row;
        }

        private static VBoxContainer Kpi(
            string localizationKey,
            string fallback,
            decimal? value,
            string color,
            DashboardStyleDefinition style,
            bool emphasized = false,
            string suffix = "")
        {
            var content = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            if (value is { } known)
                DashboardTooltip.SetValue(content, ModLocalization.Get(localizationKey, fallback), known);
            else
                DashboardTooltip.Set(content, [ModLocalization.Get(localizationKey, fallback),
                    ModLocalization.Get("overview.unavailable", "N/A: not captured, incomplete attribution, or an undefined ratio.")]);
            content.AddThemeConstantOverride("separation", -2);
            var name = TruncatedLabel(ModLocalization.Get(localizationKey, fallback), style, true,
                Math.Max(10, style.FontSize - (emphasized ? 1 : 2)));
            name.TooltipText = name.Text;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(name);
            var amount = Label(value is { } number ? Format(number) + suffix : "N/A", style, value == null, style.FontSize + (emphasized ? 5 : 3));
            amount.Modulate = ColorOf(value == null ? style.SecondaryTextColor : color);
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(amount);
            return content;
        }

        private static decimal TurnValue(TurnAnalysisPoint turn, OverviewSection section)
        {
            return section switch
            {
                OverviewSection.Offense => turn.Damage,
                OverviewSection.Defense => turn.HpLost + turn.EffectiveBlock,
                OverviewSection.Resources => turn.Cards,
                _ => turn.ModifierImpact,
            };
        }

        private static OverviewSource[] AggregateSources(
            IReadOnlyList<PlayerMetricSnapshot> players,
            string metricId)
        {
            var values = new Dictionary<string, OverviewSource>(StringComparer.Ordinal);
            foreach (var player in players)
                foreach (var rawSource in MetricSourcesForDisplay(player, metricId))
                {
                    var source = PresentSource(player, rawSource);
                    if (!values.TryGetValue(source.SourceKey, out var value))
                    {
                        value = new(source.SourceKind, source.DisplayName, 0m);
                        values.Add(source.SourceKey, value);
                    }

                    values[source.SourceKey] = value with { Value = value.Value + source.Value };
                }

            return values.Values.ToArray();
        }

        private static string MetricName(string metricId)
        {
            var definition = Main.Api.MetricDefinitions.FirstOrDefault(item => item.Id == metricId);
            return definition == null
                ? metricId
                : ModLocalization.Get(definition.NameLocalizationKey, definition.FallbackName);
        }

        private static string SourceColor(AnalyticsSourceKind kind, DashboardStyleDefinition style, int index)
        {
            return kind switch
            {
                AnalyticsSourceKind.Card => Accent(style, 1),
                AnalyticsSourceKind.Power => Accent(style, 4),
                AnalyticsSourceKind.Potion => Accent(style, 3),
                AnalyticsSourceKind.Orb => Accent(style, 5),
                AnalyticsSourceKind.Relic => style.WarningColor,
                AnalyticsSourceKind.Creature => style.NegativeColor,
                AnalyticsSourceKind.Modifier => style.PositiveColor,
                _ => Accent(style, index),
            };
        }

        private static string OverviewScopeName(DashboardDataScope scope)
        {
            return scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRun", "Current run")
                : ModLocalization.Get("overlay.currentCombat", "Current combat");
        }

        private enum OverviewSection
        {
            Offense,
            Defense,
            Resources,
            Analysis,
        }

        private sealed record OverviewSectionDefinition(
            OverviewSection Section,
            string LocalizationKey,
            string FallbackName,
            string PrimaryMetric,
            IReadOnlyList<string> Metrics,
            int AccentIndex);

        private sealed record OverviewSource(
            AnalyticsSourceKind Kind,
            string Name,
            decimal Value);

        private sealed record TurnPoint(DateTimeOffset OccurredAtUtc, int TurnIndex, decimal Value);
    }
}
