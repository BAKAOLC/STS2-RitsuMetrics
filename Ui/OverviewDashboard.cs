// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
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
            Rows.AddChild(SectionTitle(ModLocalization.Get("overview.playerSummary", "Player summary"),
                ModLocalization.Format("overview.playerSummary.meta", "{0} players · {1} rounds · {2} damage",
                    players.Length, snapshot.RoundCount, Format(totalDamage)), context.Style,
                Accent(context.Style, 0)));
            Rows.AddChild(BuildPlayerSummary(players, snapshot, snapshotAnalysis, totalDamage, context.Style,
                DashboardPresentation.SingleLine(context.Parameters)));

            Rows.AddChild(SectionTitle(ModLocalization.Get("overview.keyMetrics", "Key metrics"),
                ModLocalization.Get("overview.keyMetrics.meta", "Damage · survival · actions · support"),
                context.Style, Accent(context.Style, 1)));
            var metricSections = ResponsiveGrid(2, 330f);
            foreach (var definition in Sections)
                metricSections.AddChild(BuildMetricSummary(players, snapshot, definition, context.Style));
            Rows.AddChild(metricSections);

            Rows.AddChild(SectionTitle(ModLocalization.Get("overview.combatFlow", "Combat flow"),
                ModLocalization.Get("overview.combatFlow.meta", "Damage sources, composition and turn trends"),
                context.Style, Accent(context.Style, 3)));
            var flow = ResponsiveGrid(2, 280f);
            flow.AddChild(BuildTopSources(players, MetricIds.DamageDealt, "overview.topSources.ad",
                "Top AD sources", context.Style, 0));
            flow.AddChild(BuildTopSources(players, MetricIds.DamageContribution, "overview.topSources.rd",
                "Top RD sources", context.Style, 4));
            flow.AddChild(BuildTrend(context, Sections[(int)OverviewSection.Offense]));
            flow.AddChild(BuildTrend(context, Sections[(int)OverviewSection.Defense],
                ModLocalization.Get("overview.incomingDamage", "Incoming damage")));
            Rows.AddChild(flow);

            Rows.AddChild(SectionTitle(ModLocalization.Get("overview.combatAnalysis", "Combat analysis"),
                ModLocalization.Get("overview.combatAnalysis.meta", "Outcome quality and resource efficiency"),
                context.Style,
                Accent(context.Style, 4)));
            var analysisGrid = ResponsiveGrid(2, 280f);
            analysisGrid.AddChild(BuildCombatEfficiency(snapshot, snapshotAnalysis, context.Style));
            analysisGrid.AddChild(BuildDamageOutcome(snapshotAnalysis, context.Style));
            Rows.AddChild(analysisGrid);

            AccentColor = Accent(context.Style, 0);
            Status.Text = ModLocalization.Format("overview.status", "{0} · {1} players · {2} rounds",
                snapshot.EncounterName, players.Length, snapshot.RoundCount);
        }

        private static GridContainer BuildPlayerSummary(
            PlayerMetricSnapshot[] players,
            CombatSnapshot snapshot,
            SnapshotAnalysis snapshotAnalysis,
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
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var accent = playerAccents[player.PlayerKey];
                var damage = Metric(player, MetricIds.DamageDealt);
                var energy = Metric(player, MetricIds.EnergySpent);
                var survival = SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var incoming = snapshotAnalysis.Incoming(player);
                var body = new VBoxContainer();
                body.AddThemeConstantOverride("separation", 6);
                body.AddChild(PlayerHeader(player, index + 1, damage, totalDamage, accent, style, singleLine));
                var kpis = ResponsiveGrid(
                    PlayerKpiColumns,
                    PlayerKpiMinimumColumnWidth,
                    PlayerKpiHorizontalSeparation,
                    PlayerKpiHorizontalSeparation);
                kpis.AddChild(Kpi("overview.appliedDamage", "Applied damage (AD)", damage, accent, style));
                kpis.AddChild(Kpi("analysis.blockGained", "Block gained",
                    Metric(player, MetricIds.BlockGained), style.PositiveColor, style));
                kpis.AddChild(Kpi("overview.responsibilityDamage", "Responsibility damage (RD)",
                    MetricForDisplay(player, MetricIds.DamageContribution), Accent(style, 4), style));
                kpis.AddChild(Kpi("overview.damageShare", "Damage share",
                    totalDamage > 0m ? damage / totalDamage * 100m : 0m, accent, style, suffix: "%"));
                kpis.AddChild(Kpi("analysis.damagePerTurn", "Damage / turn",
                    damage / Math.Max(1, snapshot.RoundCount), Accent(style, 1), style));
                kpis.AddChild(Kpi("analysis.effectiveBlock", "Effective block", incoming.EffectiveBlock,
                    style.PositiveColor, style));
                kpis.AddChild(Kpi("analysis.deaths", "Deaths", survival.PlayerDeaths,
                    style.NegativeColor, style));
                kpis.AddChild(Kpi("analysis.hpLossRatio", "HP loss ratio", incoming.HpLossRatio * 100m,
                    style.WarningColor, style, suffix: "%"));
                kpis.AddChild(Kpi("analysis.healing", "Healing", Metric(player, MetricIds.HealingReceived),
                    style.PositiveColor, style));
                kpis.AddChild(Kpi("analysis.cardsPlayed", "Cards played", Metric(player, MetricIds.CardsPlayed),
                    Accent(style, 3), style));
                kpis.AddChild(Kpi("analysis.damagePerEnergy", "Damage / energy",
                    energy > 0m ? damage / energy : 0m, accent, style));
                kpis.AddChild(Kpi("analysis.overkill", "Overkill", Metric(player, MetricIds.Overkill),
                    style.WarningColor, style));
                body.AddChild(kpis);
                grid.AddChild(Surface(body, style, accent, PlayerCardPadding));
            }

            return grid;
        }

        private static Control BuildMetricSummary(
            PlayerMetricSnapshot[] players,
            CombatSnapshot snapshot,
            OverviewSectionDefinition definition,
            DashboardStyleDefinition style)
        {
            var title = ModLocalization.Get(definition.LocalizationKey, definition.FallbackName);
            var playerAccents = PlayerAccents(players, style);
            var body = ChartBody(title, style, Accent(style, definition.AccentIndex));
            var table = new GridContainer
            {
                Columns = definition.Metrics.Count + 1,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            table.AddThemeConstantOverride("h_separation", 8);
            table.AddThemeConstantOverride("v_separation", 7);
            table.AddChild(Label(string.Empty, style, true, Math.Max(10, style.FontSize - 2)));
            foreach (var metricId in definition.Metrics)
            {
                var heading = TruncatedLabel(MetricName(metricId), style, true, Math.Max(10, style.FontSize - 2));
                heading.CustomMinimumSize = new(72f, 0f);
                heading.HorizontalAlignment = HorizontalAlignment.Right;
                heading.TooltipText = heading.Text;
                table.AddChild(heading);
            }

            foreach (var player in players)
                AddMetricRow(player.DisplayName, metricId => Value(player, metricId), false,
                    playerAccents[player.PlayerKey]);
            if (players.Length > 1)
                AddMetricRow(ModLocalization.Get("overview.teamTotal", "Team total"),
                    metricId => players.Sum(player => Value(player, metricId)), true, null);
            body.AddChild(table);
            return Surface(body, style, Accent(style, definition.AccentIndex));

            void AddMetricRow(string name, Func<string, decimal> value, bool total, string? identityColor)
            {
                var playerName = TruncatedLabel(name, style, !total, style.FontSize);
                playerName.TooltipText = name;
                if (identityColor != null)
                    ApplyIdentityColor(playerName, identityColor);
                table.AddChild(playerName);
                foreach (var metricId in definition.Metrics)
                {
                    var amount = Label(Format(value(metricId)), style, false,
                        total ? style.FontSize + 1 : style.FontSize);
                    amount.HorizontalAlignment = HorizontalAlignment.Right;
                    DashboardTooltip.SetValue(amount, $"{name} · {MetricName(metricId)}", value(metricId));
                    if (total)
                        amount.Modulate = ColorOf(Accent(style, definition.AccentIndex));
                    table.AddChild(amount);
                }
            }

            decimal Value(PlayerMetricSnapshot player, string metricId)
            {
                return metricId == MetricIds.DamageTaken
                    ? SnapshotStatistics.Survival(snapshot, player.PlayerNetId).PlayerHpLost
                    : MetricForDisplay(player, metricId);
            }
        }

        private static Control BuildTopSources(
            IReadOnlyList<PlayerMetricSnapshot> players,
            string metricId,
            string titleKey,
            string titleFallback,
            DashboardStyleDefinition style,
            int accentIndex)
        {
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
            decimal value,
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
            DashboardTooltip.SetValue(content, ModLocalization.Get(localizationKey, fallback), value);
            content.AddThemeConstantOverride("separation", -2);
            var name = TruncatedLabel(ModLocalization.Get(localizationKey, fallback), style, true,
                Math.Max(10, style.FontSize - (emphasized ? 1 : 2)));
            name.TooltipText = name.Text;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(name);
            var amount = Label(Format(value) + suffix, style, false, style.FontSize + (emphasized ? 5 : 3));
            amount.Modulate = ColorOf(color);
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
