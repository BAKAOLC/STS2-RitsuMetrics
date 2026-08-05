// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal enum AdvancedDashboardMode
    {
        PlayerPerformance,
        SourceAnalysis,
        DefenseResources,
        CardsAndEffects,
        ContributionAnalysis,
        TurnAnalysis,
        RunTrends,
        CombatRecords,
    }

    internal sealed class AdvancedDashboardRenderer(AdvancedDashboardMode mode) : DashboardRendererBase
    {
        private DashboardRenderContext? _lastContext;
        private bool _defenseActionDetailsExpanded;
        private bool _runCombatDetailsExpanded;
        private bool _turnDetailsExpanded;

        protected override bool ReconcileRowsOnRefresh => mode is AdvancedDashboardMode.SourceAnalysis or
            AdvancedDashboardMode.CardsAndEffects or AdvancedDashboardMode.ContributionAnalysis;

        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (mode == AdvancedDashboardMode.SourceAnalysis)
                return new(DashboardDataComponents.Metrics,
                [
                    MetricIds.DamageDealt,
                    MetricIds.DamageContribution,
                    MetricIds.DamageAmplified,
                    MetricIds.Overkill,
                ]);

            var components = mode switch
            {
                AdvancedDashboardMode.ContributionAnalysis => DashboardDataComponents.Metrics |
                                                              DashboardDataComponents.Events,
                AdvancedDashboardMode.DefenseResources => DashboardDataComponents.Metrics |
                                                          DashboardDataComponents.Timeline,
                AdvancedDashboardMode.TurnAnalysis => DashboardDataComponents.Timeline,
                AdvancedDashboardMode.RunTrends => DashboardDataComponents.Metrics |
                                                   DashboardDataComponents.RunCombats,
                AdvancedDashboardMode.CombatRecords => DashboardDataComponents.Metrics |
                                                       DashboardDataComponents.Timeline |
                                                       DashboardDataComponents.RunCombats,
                _ => DashboardDataComponents.Metrics,
            };
            return new(components);
        }

        protected override void Render(DashboardRenderContext context)
        {
            _lastContext = context;
            Title = ModLocalization.Get(TitleKey(mode), TitleFallback(mode));
            Subtitle = context.Scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRun", "Current run")
                : ModLocalization.Get("overlay.currentCombat", "Current combat");
            AccentColor = Accent(context.Style, (int)mode + 1);
            switch (mode)
            {
                case AdvancedDashboardMode.PlayerPerformance:
                    RenderPlayers(context);
                    break;
                case AdvancedDashboardMode.SourceAnalysis:
                    RenderSources(context);
                    break;
                case AdvancedDashboardMode.DefenseResources:
                    RenderDefense(context);
                    break;
                case AdvancedDashboardMode.CardsAndEffects:
                    RenderCardsAndEffects(context);
                    break;
                case AdvancedDashboardMode.ContributionAnalysis:
                    RenderContributions(context);
                    break;
                case AdvancedDashboardMode.TurnAnalysis:
                    RenderTurns(context);
                    break;
                case AdvancedDashboardMode.RunTrends:
                    RenderRunTrends(context);
                    break;
                case AdvancedDashboardMode.CombatRecords:
                    RenderRecords(context);
                    break;
            }
        }

        private void RenderPlayers(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                Empty(context);
                return;
            }

            var totalDamage = snapshot.Players.Sum(player => Metric(player, MetricIds.DamageDealt));
            var snapshotAnalysis = SnapshotAnalysisCache.Get(snapshot);
            var players = snapshot.Players.OrderByDescending(player => Metric(player, MetricIds.DamageDealt)).ToArray();
            var playerAccents = PlayerAccents(players, context.Style);
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var damage = Metric(player, MetricIds.DamageDealt);
                var hpDamage = MetricForDisplay(player, MetricIds.EffectiveHpDamageDealt);
                var overkill = Metric(player, MetricIds.Overkill);
                var damageOutcome = hpDamage + Math.Max(0m, damage - hpDamage) + overkill;
                var rounds = Math.Max(1, snapshot.RoundCount);
                var energy = Metric(player, MetricIds.EnergySpent);
                var cards = Metric(player, MetricIds.CardsPlayed);
                var survival = SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var incoming = snapshotAnalysis.Incoming(player);
                var accent = playerAccents[player.PlayerKey];
                var content = new VBoxContainer();
                content.AddThemeConstantOverride("separation", 6);
                content.AddChild(PlayerHeader(player, index + 1, damage, totalDamage, accent, context.Style,
                    DashboardPresentation.SingleLine(context.Parameters)));
                content.AddChild(SegmentedMeter(
                    $"{ModLocalization.Get("overview.damageConversion", "HP conversion")} " +
                    $"{(damageOutcome > 0m ? hpDamage / damageOutcome : 0m):P1}",
                    hpDamage,
                    Math.Max(0m, damage - hpDamage) + overkill,
                    context.Style.NegativeColor,
                    context.Style.WarningColor,
                    context.Style,
                    ModLocalization.Get("metric.effectiveHpDamageDealt", "HP damage"),
                    ModLocalization.Get("overview.nonHpDamage", "Blocked + overkill"),
                    ModLocalization.Get("analysis.hpConversion.hint",
                        "HP conversion is HP damage divided by HP damage plus enemy block and overkill. Higher " +
                        "means more outgoing damage reduced enemy HP.")));
                content.AddChild(SegmentedMeter(
                    $"{ModLocalization.Get("analysis.hpLossRatio", "HP loss ratio")} {incoming.HpLossRatio:P1}",
                    incoming.HpLost,
                    incoming.EffectiveBlock,
                    context.Style.NegativeColor,
                    context.Style.PositiveColor,
                    context.Style,
                    ModLocalization.Get("analysis.hpLost", "HP lost"),
                    ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                    ModLocalization.Get("analysis.hpLossRatio.hint",
                        "HP loss ratio is HP lost divided by HP lost plus effective block. Lower means more " +
                        "incoming damage was absorbed.")));
                var statistics = new List<StatCell>
                {
                    Stat("analysis.damagePerTurn", "Damage / turn", damage / rounds, context.Style.NegativeColor),
                    Stat("overview.damageShare", "Damage share",
                        totalDamage > 0m ? damage / totalDamage * 100m : 0m, accent, "%"),
                    Stat("analysis.damagePerEnergy", "Damage / energy", energy > 0m ? damage / energy : 0m,
                        Accent(context.Style, 1)),
                    Stat("analysis.blockGained", "Block gained", incoming.BlockGained,
                        context.Style.PositiveColor),
                    Stat("analysis.blockEfficiency", "Block efficiency", incoming.BlockGained > 0m
                        ? incoming.EffectiveBlock / incoming.BlockGained * 100m
                        : 0m, context.Style.PositiveColor, "%"),
                    Stat("analysis.deaths", "Deaths", survival.PlayerDeaths, context.Style.NegativeColor),
                    Stat("analysis.damageAmplified", "Damage enabled", Metric(player, MetricIds.DamageAmplified),
                        Accent(context.Style, 4)),
                    Stat("analysis.damageMitigated", "Damage reduced",
                        Metric(player, MetricIds.DamageMitigated), context.Style.PositiveColor),
                    Stat("metric.defenseContribution", "Defense contribution",
                        Metric(player, MetricIds.DefenseContribution),
                        context.Style.PositiveColor),
                    Stat("analysis.cardsPerTurn", "Cards / turn", cards / rounds, Accent(context.Style, 3)),
                    Stat("analysis.healing", "Healing", Metric(player, MetricIds.HealingReceived),
                        context.Style.PositiveColor),
                    Stat("analysis.unspentBlock", "Unspent block", incoming.UnspentBlock,
                        context.Style.WarningColor),
                };

                content.AddChild(MetricGrid(context.Style, statistics));
                Rows.AddChild(Surface(content, context.Style, accent));
            }

            Status.Text = ModLocalization.Format("analysis.playerSummary",
                "{0} players · {1} rounds · {2} total damage", players.Length, snapshot.RoundCount,
                Format(totalDamage));
        }

        private void RenderSources(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                ReconcileAdvancedEmpty(context);
                return;
            }

            var allSources = AggregateSources(snapshot.Players, true)
                .Where(source => SourceAnalysisMetrics(source).Length > 0)
                .OrderByDescending(source => SourceMetric(source, MetricIds.DamageContribution))
                .ThenByDescending(source => SourceMetric(source, MetricIds.DamageDealt))
                .ThenByDescending(source => SourceMetric(source, MetricIds.DamageAmplified))
                .ToArray();
            if (allSources.Length == 0)
            {
                ReconcileAdvancedEmpty(context, ModLocalization.Get("analysis.noSources", "No source data"));
                return;
            }

            var sources = PageItems(allSources, context, 40);
            var adTitle = ModLocalization.Get("overview.topSources.ad", "Top applied sources (AD)");
            var rdTitle = ModLocalization.Get("overview.topSources.rd", "Top responsibility sources (RD)");
            var adChartData = allSources.Where(source => SourceMetric(source, MetricIds.DamageDealt) > 0m)
                .OrderByDescending(source => SourceMetric(source, MetricIds.DamageDealt)).Take(12)
                .Select(source => new DashboardBarDatum(source.Name,
                    SourceMetric(source, MetricIds.DamageDealt), SourceColor(source.Kind, context.Style),
                    Format(SourceMetric(source, MetricIds.DamageDealt)))).ToArray();
            var rdChartData = allSources
                .Where(source => SourceMetric(source, MetricIds.DamageContribution) > 0m)
                .OrderByDescending(source => SourceMetric(source, MetricIds.DamageContribution)).Take(12)
                .Select(source => new DashboardBarDatum(source.Name,
                    SourceMetric(source, MetricIds.DamageContribution),
                    SourceColor(source.Kind, context.Style),
                    Format(SourceMetric(source, MetricIds.DamageContribution)))).ToArray();
            var rows = new List<VariableReconciledRow>(sources.Count + 2);
            var chartFingerprint = string.Join('\u001e',
                VisualStyleFingerprint(context.Style),
                adTitle,
                rdTitle,
                string.Join('\u001d', adChartData),
                string.Join('\u001d', rdChartData));
            rows.Add(new(new("__source_charts", chartFingerprint, () =>
            {
                var charts = ResponsiveGrid(2, 280f);
                charts.AddChild(BarChartPanel(adTitle, adChartData, context.Style));
                charts.AddChild(BarChartPanel(rdTitle, rdChartData, context.Style));
                return charts;
            }), 680f));
            var maximum = Math.Max(1m, allSources.SelectMany(SourceAnalysisMetrics)
                .Select(metric => Math.Abs(metric.Value)).DefaultIfEmpty().Max());
            var sectionTitle = ModLocalization.Get("analysis.sourceDetails", "Source details");
            var sectionColor = Accent(context.Style, 1);
            rows.Add(new(new("__source_details", string.Join('\u001e',
                    VisualStyleFingerprint(context.Style), sectionTitle, sectionColor),
                () => CreateSection(sectionTitle, sectionColor, context.Style, true)), 24f));
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var source in sources)
            {
                var color = SourceColor(source.Kind, context.Style);
                var metrics = SourceAnalysisMetrics(source);
                var fingerprint = string.Join('\u001e',
                    VisualStyleFingerprint(context.Style),
                    source.Key,
                    source.Kind,
                    source.Name,
                    source.Players.Count,
                    source.TotalOccurrences,
                    color,
                    maximum,
                    string.Join('\u001d', metrics.Select(metric =>
                        $"{metric.MetricId}\u001c{MetricName(metric.MetricId)}\u001c{metric.Value}")));
                rows.Add(new(new($"source:{source.Key}", fingerprint, () =>
                {
                    var content = new VBoxContainer();
                    content.AddThemeConstantOverride("separation", 4);
                    content.AddChild(SourceHeader(source, color, context.Style));
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var (metricId, value) in metrics)
                        content.AddChild(Meter(MetricName(metricId),
                            Format(value),
                            Math.Abs(value), maximum, MetricColor(metricId, context.Style), context.Style,
                            Math.Max(23, context.Style.RowHeight - 7)));
                    return Surface(content, context.Style, color);
                }), 46f + metrics.Length * (Math.Max(23, context.Style.RowHeight - 7) + 4f)));
            }

            ReconcileRows(rows.Select(row => row.Row));
            Status.Text = ModLocalization.Format("analysis.sourceSummary",
                "{0} sources · {1} players · AD {2} · RD {3}", allSources.Length, snapshot.Players.Count,
                Format(snapshot.Players.Sum(player => MetricForDisplay(player, MetricIds.DamageDealt))),
                Format(snapshot.Players.Sum(player => MetricForDisplay(player, MetricIds.DamageContribution))));
        }

        private void ReconcileAdvancedEmpty(DashboardRenderContext context, string text = "No combat data")
        {
            ReconcileRows(
            [
                new("__empty", EmptyRowFingerprint(context, text), () => CreateEmptyRow(context, text)),
            ]);
            Status.Text = string.Empty;
        }

        private void RenderDefense(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                Empty(context);
                return;
            }

            var playerAccents = PlayerAccents(snapshot.Players, context.Style);
            foreach (var player in snapshot.Players
                         .OrderByDescending(player => SnapshotStatistics.Survival(snapshot, player.PlayerNetId)
                             .PlayerHpLost))
            {
                var (taken, deaths, _, _) = SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var incoming = SnapshotAnalysisCache.Get(snapshot).Incoming(player);
                var blocked = incoming.EffectiveBlock;
                var gained = incoming.BlockGained;
                var energy = Metric(player, MetricIds.EnergySpent);
                var cards = Metric(player, MetricIds.CardsPlayed);
                var accent = playerAccents[player.PlayerKey];
                var content = new VBoxContainer();
                content.AddThemeConstantOverride("separation", 5);
                content.AddChild(PlayerHeader(player, 0, taken, taken + blocked, accent, context.Style,
                    DashboardPresentation.SingleLine(context.Parameters)));
                content.AddChild(SegmentedMeter(
                    $"{ModLocalization.Get("analysis.hpLost", "HP lost")} {Format(taken)}  ·  " +
                    $"{ModLocalization.Get("analysis.blocked", "blocked")} {Format(blocked)}",
                    taken, blocked, context.Style.NegativeColor, context.Style.PositiveColor, context.Style,
                    ModLocalization.Get("analysis.hpLost", "HP lost"),
                    ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                    ModLocalization.Get("analysis.hpLossRatio.hint",
                        "HP loss ratio is HP lost divided by HP lost plus effective block. Lower means more " +
                        "incoming damage was absorbed.")));
                var defenseRatioHint = ModLocalization.Get("analysis.defenseRatios.hint",
                    "HP loss ratio = HP lost / (HP lost + effective block). Unspent block becomes final waste " +
                    "only after combat ends.");
                var statistics = new List<StatCell>
                {
                    Stat("analysis.effectiveBlock", "Effective block", incoming.EffectiveBlock,
                        context.Style.PositiveColor),
                    Stat("analysis.blockGained", "Block gained", gained, context.Style.PositiveColor),
                    Stat("analysis.blockEfficiency", "Block efficiency", gained > 0m ? blocked / gained * 100m : 0m,
                        accent, "%"),
                    Stat("analysis.unspentBlock", "Unspent block", incoming.UnspentBlock,
                        context.Style.WarningColor, detail: defenseRatioHint),
                    Stat("analysis.hpLossRatio", "HP loss ratio", incoming.HpLossRatio * 100m,
                        context.Style.NegativeColor, "%", defenseRatioHint),
                    Stat("analysis.healing", "Healing", Metric(player, MetricIds.HealingReceived),
                        context.Style.PositiveColor),
                    Stat("analysis.deaths", "Deaths", deaths, context.Style.NegativeColor),
                };
                if (incoming.HasCompleteHpTimeline)
                {
                    statistics.Insert(5, Stat("analysis.selfHpLost", "Self-inflicted HP loss",
                        incoming.SelfHpLost, context.Style.WarningColor));
                    statistics.Insert(6, Stat("analysis.selfHpLossRatio", "Self-inflicted share",
                        incoming.SelfHpLossRatio * 100m, context.Style.WarningColor, "%"));
                }
                content.AddChild(MetricGrid(context.Style, statistics));
                AddIncomingSources(content, incoming, context.Style);
                content.AddChild(DetailToggle(
                    ModLocalization.Get("analysis.actionDetails", "Card and energy details"),
                    _defenseActionDetailsExpanded,
                    Accent(context.Style, 3),
                    context.Style,
                    () => ToggleDetails(ref _defenseActionDetailsExpanded)));
                if (_defenseActionDetailsExpanded)
                    content.AddChild(MetricGrid(context.Style,
                    [
                        Stat("analysis.energySpent", "Energy spent", energy, Accent(context.Style, 3)),
                        Stat("analysis.cardsPlayed", "Cards played", cards, Accent(context.Style, 1)),
                        Stat("analysis.cardsPerEnergy", "Cards / energy", energy > 0m ? cards / energy : 0m,
                            Accent(context.Style, 4)),
                        Stat("analysis.cardsDrawn", "Cards drawn", Metric(player, MetricIds.CardsDrawn), accent),
                        Stat("analysis.cardsDiscarded", "Cards discarded",
                            Metric(player, MetricIds.CardsDiscarded), context.Style.WarningColor),
                        Stat("analysis.cardsExhausted", "Cards exhausted",
                            Metric(player, MetricIds.CardsExhausted), context.Style.NegativeColor),
                    ]));
                Rows.AddChild(Surface(content, context.Style, accent));
            }

            Status.Text = ModLocalization.Format("analysis.defenseSummary", "{0} defensive profiles",
                snapshot.Players.Count);
        }

        private void RenderCardsAndEffects(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                ReconcileAdvancedEmpty(context);
                return;
            }

            var playerAccents = PlayerAccents(snapshot.Players, context.Style);
            var rows = new List<VariableReconciledRow>();
            foreach (var player in snapshot.Players)
            {
                var playerColor = playerAccents[player.PlayerKey];
                rows.Add(new(new($"player:{player.PlayerKey}", string.Join('\u001e',
                        VisualStyleFingerprint(context.Style), player.DisplayName, playerColor),
                    () => CreateSection(player.DisplayName, playerColor, context.Style)), 31f));
                var sourceRows = AggregatePlayerSources(player).ToArray();
                var cards = sourceRows.Where(source => source.Kind == AnalyticsSourceKind.Card)
                    .OrderByDescending(SourceScore).ToArray();
                rows.AddRange(cards.Select(card =>
                    CardEffectVirtualRow(player.PlayerKey, card, true, context.Style)));
                var effects = sourceRows.Where(source => source.Kind != AnalyticsSourceKind.Card)
                    .Where(source => SourceScore(source) > 0m)
                    .OrderByDescending(SourceScore).ToArray();
                if (effects.Length > 0)
                {
                    var sectionTitle = ModLocalization.Get("analysis.nonCardEffects", "Powers, relics and effects");
                    var sectionColor = Accent(context.Style, 4);
                    rows.Add(new(new($"effects:{player.PlayerKey}", string.Join('\u001e',
                            VisualStyleFingerprint(context.Style), sectionTitle, sectionColor),
                        () => CreateSection(sectionTitle, sectionColor, context.Style, true)), 24f));
                }

                rows.AddRange(effects.Select(effect =>
                    CardEffectVirtualRow(player.PlayerKey, effect, false, context.Style)));
            }

            var visibleRows = PageItems(rows, context, 42);
            ReconcileRows(visibleRows.Select(row => row.Row));
            Status.Text = ModLocalization.Format("analysis.cardEffectSummary",
                "Card and effect drill-down · {0} players",
                snapshot.Players.Count);
        }

        private void RenderContributions(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                ReconcileAdvancedEmpty(context);
                return;
            }

            var observations = snapshot.Events.Where(observation =>
                    observation.MetricId == MetricIds.DamageContribution &&
                    observation.Tags.TryGetValue(ObservationTagIds.ContributionComponent, out var component) &&
                    component is ContributionComponentIds.DamageAmplification or ContributionComponentIds.Execution)
                .ToArray();
            var contributions = new Dictionary<string, ContributionRollup>(StringComparer.Ordinal);
            foreach (var observation in observations)
            {
                var key = $"{observation.Subject.Key}\0{observation.Source.Key}";
                if (!contributions.TryGetValue(key, out var rollup))
                {
                    rollup = new(observation.Subject, observation.Source);
                    contributions.Add(key, rollup);
                }

                rollup.Events++;
                var component = observation.Tags[ObservationTagIds.ContributionComponent];
                if (component == ContributionComponentIds.Execution)
                    rollup.Execution += observation.Value;
                else
                    rollup.Enabled += observation.Value;
                if (observation.Tags.TryGetValue(ObservationTagIds.AttributionConfidence, out var confidence) &&
                    Enum.TryParse<AttributionConfidence>(confidence, out var parsed) && parsed > rollup.Confidence)
                    rollup.Confidence = parsed;
            }

            var ranked = contributions.Values.OrderByDescending(item => item.Total).ToArray();
            if (ranked.Length == 0)
            {
                ReconcileAdvancedEmpty(context, ModLocalization.Get("analysis.noModifierContributions",
                    "No attributed RD amplification or execution data"));
                return;
            }

            var chartTitle = ModLocalization.Get("dashboard.contributionAnalysis", "Contribution analysis");
            var chartData = ranked.Take(12).Select(rollup => new DashboardBarDatum(
                $"{rollup.Contributor.DisplayName} · {rollup.Source.DisplayName}", rollup.Total,
                SourceColor(rollup.Source.Kind, context.Style), Format(rollup.Total))).ToArray();
            var rows = new List<VariableReconciledRow>(22)
            {
                new(new("__contribution_chart", string.Join('\u001e',
                        VisualStyleFingerprint(context.Style), chartTitle, string.Join('\u001d', chartData)),
                    () => BarChartPanel(chartTitle, chartData, context.Style)), 340f),
            };
            var sectionTitle = ModLocalization.Get("analysis.contributionDetails", "Contribution details");
            var sectionColor = Accent(context.Style, 4);
            rows.Add(new(new("__contribution_details", string.Join('\u001e',
                    VisualStyleFingerprint(context.Style), sectionTitle, sectionColor),
                () => CreateSection(sectionTitle, sectionColor, context.Style, true)), 24f));
            var detailRows = ranked.Select(rollup => ContributionVirtualRow(rollup, context.Style)).ToArray();
            rows.AddRange(PageItems(detailRows, context, 40));

            ReconcileRows(rows.Select(row => row.Row));
            Status.Text = ModLocalization.Format("analysis.contributionSummary",
                "{0} RD modifier observations · {1} attributed sources", observations.Length, contributions.Count);
        }

        private void RenderTurns(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                Empty(context);
                return;
            }

            var combatLabels = context.Run?.Combats.ToDictionary(
                combat => combat.CombatId,
                combat => (Short: ModLocalization.Format("analysis.floorShort", "F{0}", combat.Floor),
                    Full: combat.EncounterName),
                StringComparer.Ordinal) ?? new Dictionary<string, (string Short, string Full)>(StringComparer.Ordinal);
            var turns = SnapshotAnalysisCache.Get(snapshot).Turns
                .Select(turn =>
                {
                    var label = combatLabels.GetValueOrDefault(turn.CombatId);
                    return new TurnSummary(
                        label.Short,
                        label.Full,
                        turn.Index,
                        turn.Damage,
                        turn.HpLost,
                        turn.EffectiveBlock,
                        turn.BlockGained,
                        turn.Overkill,
                        turn.Cards,
                        turn.Draws,
                        turn.Energy,
                        turn.Extra,
                        turn.Side);
                })
                .ToArray();
            var charts = ResponsiveGrid(2, 280f);
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damageDealt", "Damage"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn), turn.Damage)),
                context.Style.NegativeColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.hpLost", "HP lost"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn), turn.Incoming)),
                context.Style.WarningColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.blockGained", "Block"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn), turn.Block)),
                context.Style.PositiveColor, context.Style));
            Rows.AddChild(charts);
            Rows.AddChild(DetailToggle(
                ModLocalization.Format("analysis.turnDetailsCount", "Turn details ({0})", turns.Length),
                _turnDetailsExpanded,
                Accent(context.Style, 1),
                context.Style,
                () => ToggleDetails(ref _turnDetailsExpanded)));
            if (!_turnDetailsExpanded)
            {
                Status.Text = ModLocalization.Format("analysis.turnSummary", "{0} timeline turns · {1} rounds",
                    turns.Length, snapshot.RoundCount);
                return;
            }

            foreach (var turn in turns)
            {
                var color = turn.Extra
                    ? context.Style.WarningColor
                    : turn.Side == TimelineTurnSide.Enemy
                        ? context.Style.NegativeColor
                        : Accent(context.Style, 1);
                var box = new VBoxContainer();
                box.AddThemeConstantOverride("separation", 5);
                var header = new HBoxContainer();
                header.AddChild(Badge(turn.Index <= 0
                    ? ModLocalization.Get("dashboard.setupShort", "SETUP")
                    : $"T{turn.Index}", color, context.Style));
                header.AddChild(TruncatedLabel(
                    (string.IsNullOrWhiteSpace(turn.EncounterName) ? string.Empty : $"{turn.EncounterName} · ") +
                    DashboardLocalization.TurnSide(turn.Side) +
                    (turn.Extra ? $" · {ModLocalization.Get("analysis.extraTurn", "Extra turn")}" : ""),
                    context.Style, false, context.Style.FontSize + 1));
                box.AddChild(header);
                box.AddChild(MetricGrid(context.Style,
                [
                    Stat("analysis.damageDealt", "Damage", turn.Damage, context.Style.NegativeColor),
                    Stat("analysis.overkill", "Overkill", turn.Overkill, context.Style.WarningColor),
                    Stat("analysis.hpLost", "HP lost", turn.Incoming, context.Style.WarningColor),
                    Stat("analysis.effectiveBlock", "Effective block", turn.Blocked,
                        context.Style.PositiveColor),
                    Stat("analysis.blockGained", "Block gained", turn.Block, context.Style.PositiveColor),
                    new(ModLocalization.Get("analysis.cardsPlayed", "Cards played"),
                        turn.Cards.ToString(CultureInfo.CurrentCulture), turn.Cards,
                        Accent(context.Style, 3)),
                    new(ModLocalization.Get("analysis.cardsDrawn", "Cards drawn"),
                        turn.Draws.ToString(CultureInfo.CurrentCulture), turn.Draws,
                        Accent(context.Style, 1)),
                    Stat("analysis.energySpent", "Energy spent", turn.Energy, Accent(context.Style, 4)),
                ]));
                Rows.AddChild(Surface(box, context.Style, color));
            }

            Status.Text = ModLocalization.Format("analysis.turnSummary", "{0} timeline turns · {1} rounds",
                turns.Length, snapshot.RoundCount);
        }

        private void RenderRunTrends(DashboardRenderContext context)
        {
            var combats = context.Scope == DashboardDataScope.CurrentRun
                ? context.Run?.Combats ?? []
                : context.Snapshot == null
                    ? []
                    : [context.Snapshot];
            if (combats.Count == 0)
            {
                Empty(context);
                return;
            }

            var ordered = combats.OrderBy(item => item.StartedAtUtc).ToArray();
            var charts = ResponsiveGrid(2, 280f);
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damagePerTurn", "Damage / turn"),
                CombatTrend(ordered, combat => SnapshotAnalysisCache.Get(combat).AppliedDamage /
                                               Math.Max(1, combat.RoundCount)),
                context.Style.NegativeColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.hpLossRatio", "HP loss ratio"),
                CombatTrend(ordered, combat => SnapshotAnalysisCache.Get(combat).HpLossRatio * 100m),
                context.Style.WarningColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.blockEfficiency", "Block efficiency"),
                CombatTrend(ordered, combat => SnapshotAnalysisCache.Get(combat).BlockEfficiency * 100m),
                context.Style.PositiveColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damagePerEnergy", "Damage / energy"),
                CombatTrend(ordered, combat =>
                {
                    var analysis = SnapshotAnalysisCache.Get(combat);
                    return analysis.EnergySpent > 0m ? analysis.AppliedDamage / analysis.EnergySpent : 0m;
                }),
                Accent(context.Style, 1), context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.unspentBlock", "Unspent block"),
                CombatTrend(ordered, combat => SnapshotAnalysisCache.Get(combat).UnspentBlock),
                context.Style.WarningColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.cardsPerTurn", "Cards / turn"),
                CombatTrend(ordered, combat => SnapshotAnalysisCache.Get(combat).CardsPlayed /
                                               Math.Max(1, combat.RoundCount)),
                Accent(context.Style, 3), context.Style));
            Rows.AddChild(charts);
            Rows.AddChild(DetailToggle(
                ModLocalization.Format("analysis.combatDetailsCount", "Combat details ({0})", ordered.Length),
                _runCombatDetailsExpanded,
                Accent(context.Style, 1),
                context.Style,
                () => ToggleDetails(ref _runCombatDetailsExpanded)));
            if (!_runCombatDetailsExpanded)
            {
                Status.Text = ModLocalization.Format("analysis.runTrendSummary",
                    "{0} combats · {1} total damage",
                    combats.Count, Format(combats.Sum(combat => SnapshotAnalysisCache.Get(combat).AppliedDamage)));
                return;
            }

            foreach (var combat in ordered)
            {
                var analysis = SnapshotAnalysisCache.Get(combat);
                var rounds = Math.Max(1, combat.RoundCount);
                var color = Accent(context.Style, Math.Max(0, combat.ActIndex));
                var row = new HBoxContainer { CustomMinimumSize = new(0f, context.Style.RowHeight + 5f) };
                row.AddThemeConstantOverride("separation", 9);
                row.AddChild(Badge(ModLocalization.Format("analysis.floorShort", "F{0}", combat.Floor), color,
                    context.Style));
                row.AddChild(TruncatedLabel(combat.EncounterName, context.Style, false,
                    context.Style.FontSize + 1));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.damageDealt", "Applied damage"),
                    analysis.AppliedDamage,
                    context.Style.NegativeColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.damagePerTurn", "Damage / turn"),
                    analysis.AppliedDamage / rounds, color, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.hpLost", "HP lost"), analysis.HpLost,
                    context.Style.WarningColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.hpLossRatio", "HP loss ratio"),
                    analysis.HpLossRatio * 100m,
                    context.Style.WarningColor, context.Style, "%"));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.blockEfficiency", "Block efficiency"),
                    analysis.BlockEfficiency * 100m,
                    context.Style.PositiveColor, context.Style, "%"));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.unspentBlock", "Unspent block"),
                    analysis.UnspentBlock, context.Style.WarningColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.cardsPerTurn", "Cards / turn"),
                    analysis.CardsPlayed / rounds, Accent(context.Style, 3), context.Style));
                Rows.AddChild(Surface(row, context.Style, padding: 5));
            }

            Status.Text = ModLocalization.Format("analysis.runTrendSummary", "{0} combats · {1} total damage",
                combats.Count, Format(combats.Sum(combat => SnapshotAnalysisCache.Get(combat).AppliedDamage)));
        }

        private static IEnumerable<DashboardLineDatum> CombatTrend(
            IReadOnlyList<CombatSnapshot> combats,
            Func<CombatSnapshot, decimal> value)
        {
            return combats.Select((combat, index) => new DashboardLineDatum(
                ModLocalization.Format("analysis.floorShort", "F{0}", combat.Floor),
                value(combat),
                ModLocalization.Format("analysis.runTrendPoint", "#{0} · Act {1} · {2} · {3} rounds",
                    index + 1, combat.ActIndex + 1, combat.EncounterName, combat.RoundCount)));
        }

        private void RenderRecords(DashboardRenderContext context)
        {
            var combats = context.Run?.Combats ?? (context.Snapshot == null ? [] : [context.Snapshot]);
            if (combats.Count == 0)
            {
                Empty(context);
                return;
            }

            var timeline = combats.SelectMany(Timeline).ToArray();
            var damageEvents = timeline.Where(item => item.Damage?.AttributionShares?.Any(share =>
                share.Contributor.Kind == AnalyticsEntityKind.Player) == true).ToArray();
            var records = new List<RecordRow>();
            AddMaximum(records, RecordGroup.Offense, "analysis.recordHighestHit", "Highest applied hit",
                damageEvents, item => item.Damage!.EffectiveAmount, DamageEventLabel);
            AddMaximum(records, RecordGroup.Efficiency, "analysis.recordHighestOverkill", "Highest overkill",
                damageEvents, item => item.Damage!.OverkillAmount, DamageEventLabel);
            var turnDamage = damageEvents.GroupBy(item => (item.CombatId, item.TurnIndex))
                .Select(group => new NamedValue($"T{group.Key.TurnIndex}",
                    group.Sum(item => item.Damage!.EffectiveAmount)))
                .ToArray();
            AddMaximum(records, RecordGroup.Offense, "analysis.recordBestTurn", "Best damage turn", turnDamage,
                item => item.Value, item => item.Name);
            var playerCombats = combats.SelectMany(combat => combat.Players.Select(player => new NamedValue(
                $"{player.DisplayName} · {combat.EncounterName}",
                MetricForDisplay(player, MetricIds.DamageDealt)))).ToArray();
            AddMaximum(records, RecordGroup.Offense, "analysis.recordBestCombat", "Best player damage combat",
                playerCombats, item => item.Value, item => item.Name);
            var cardSources = combats.SelectMany(combat => combat.Players)
                .SelectMany(player => AggregatePlayerSources(player, true))
                .Where(source => source.Kind == AnalyticsSourceKind.Card)
                .GroupBy(source => source.Name, StringComparer.CurrentCulture)
                .Select(group => new NamedValue(group.Key,
                    group.Sum(source => source.Totals.GetValueOrDefault(MetricIds.DamageDealt))))
                .ToArray();
            AddMaximum(records, RecordGroup.Offense, "analysis.recordTopCard", "Top damage card", cardSources,
                item => item.Value, item => item.Name);
            var contributions = damageEvents.SelectMany(item => item.Damage!.Contributions).ToArray();
            AddMaximum(records, RecordGroup.Offense, "analysis.recordAmplifier", "Largest amplification", contributions
                .Where(item => item.EffectiveContribution > 0m &&
                               DamageContributionSemantics.GetRole(item) == DamageContributionRole.Modifier)
                .ToArray(), item => item.EffectiveContribution, item => item.Source.DisplayName);
            AddMaximum(records, RecordGroup.Defense, "analysis.recordMitigator", "Largest mitigation", contributions
                    .Where(item => item.EffectiveContribution < 0m &&
                                   DamageContributionSemantics.GetRole(item) == DamageContributionRole.Modifier)
                    .ToArray(), item => -item.EffectiveContribution,
                item => item.Source.DisplayName);
            var blocks = combats.SelectMany(combat => combat.Players.Select(player => new NamedValue(
                player.DisplayName, Metric(player, MetricIds.BlockGained)))).ToArray();
            AddMaximum(records, RecordGroup.Defense, "analysis.recordBlock", "Most block gained", blocks,
                item => item.Value,
                item => item.Name);
            var incomingDamage = timeline.Where(item => item.Target?.Kind == AnalyticsEntityKind.Player &&
                                                        SnapshotStatistics.EffectiveHpLost(item) > 0m).ToArray();
            AddMaximum(records, RecordGroup.Defense, "analysis.recordLargestHpLoss", "Largest HP loss",
                incomingDamage, SnapshotStatistics.EffectiveHpLost, DamageEventLabel);
            var unspentBlock = combats.Select(combat => new NamedValue(combat.EncounterName,
                SnapshotAnalysisCache.Get(combat).UnspentBlock)).ToArray();
            AddMaximum(records, RecordGroup.Efficiency, "analysis.recordUnspentBlock", "Most unspent block",
                unspentBlock, item => item.Value, item => item.Name);

            foreach (var record in records)
            {
                var color = record.Group switch
                {
                    RecordGroup.Offense => context.Style.NegativeColor,
                    RecordGroup.Defense => context.Style.PositiveColor,
                    _ => context.Style.WarningColor,
                };
                var row = new HBoxContainer { CustomMinimumSize = new(0, context.Style.RowHeight + 12) };
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Badge(record.Group switch
                {
                    RecordGroup.Offense => ModLocalization.Get("analysis.record.offenseShort", "DMG"),
                    RecordGroup.Defense => ModLocalization.Get("analysis.record.defenseShort", "DEF"),
                    _ => ModLocalization.Get("analysis.record.efficiencyShort", "EFF"),
                }, color, context.Style, 48));
                var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                text.AddThemeConstantOverride("separation", -2);
                text.AddChild(TruncatedLabel(ModLocalization.Get(record.LocalizationKey, record.Fallback),
                    context.Style, true));
                text.AddChild(TruncatedLabel(record.Detail, context.Style, false, context.Style.FontSize + 1));
                row.AddChild(text);
                var value = Label(Format(record.Value), context.Style, false, context.Style.FontSize + 4);
                value.Modulate = ColorOf(color);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                row.AddChild(value);
                Rows.AddChild(Surface(row, context.Style, color));
            }

            Status.Text = ModLocalization.Format("analysis.recordSummary", "{0} records from {1} combats",
                records.Count, combats.Count);
        }

        private static void AddIncomingSources(VBoxContainer content, IncomingDamageAnalysis incoming,
            DashboardStyleDefinition style)
        {
            if (incoming.Sources.Count == 0)
                return;
            content.AddChild(Label(ModLocalization.Get("analysis.incomingHpSources", "Top HP-loss sources"),
                style, true));
            var maximum = Math.Max(1m, incoming.Sources.Max(source => source.HpLost));
            foreach (var source in incoming.Sources.Take(5))
                content.AddChild(Meter(source.Name,
                    $"{Format(source.HpLost)} · {source.Share:P1} · ×{source.Occurrences}",
                    source.HpLost, maximum, style.NegativeColor, style, Math.Max(22, style.RowHeight - 8)));
        }

        private static VariableReconciledRow CardEffectVirtualRow(
            string playerKey,
            SourceRollup source,
            bool card,
            DashboardStyleDefinition style)
        {
            var fingerprint = string.Join('\u001e',
                VisualStyleFingerprint(style),
                playerKey,
                source.Key,
                source.Kind,
                source.Name,
                card,
                string.Join('\u001d', source.Totals.OrderBy(item => item.Key, StringComparer.Ordinal)),
                string.Join('\u001d', source.Occurrences.OrderBy(item => item.Key, StringComparer.Ordinal)));
            return new(new($"{(card ? "card" : "effect")}:{playerKey}:{source.Key}", fingerprint,
                () => CardEffectRow(source, card, style)), 142f);
        }

        private static Control CardEffectRow(SourceRollup source, bool card, DashboardStyleDefinition style)
        {
            var color = SourceColor(source.Kind, style);
            var damage = source.Totals.GetValueOrDefault(MetricIds.DamageDealt);
            var plays = source.Totals.GetValueOrDefault(MetricIds.CardsPlayed);
            var energy = source.Totals.GetValueOrDefault(MetricIds.EnergySpent);
            var enabled = source.Totals.GetValueOrDefault(MetricIds.DamageAmplified);
            var defense = source.Totals.GetValueOrDefault(MetricIds.DefenseContribution);
            var healing = source.Totals.GetValueOrDefault(MetricIds.HealingContribution);
            var totalImpact = damage + enabled + defense + healing;
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            var header = new HBoxContainer();
            header.AddChild(Badge(card
                ? ModLocalization.Get("overview.sourceKind.card", "Cards")
                : DashboardLocalization.SourceKind(source.Kind), color, style, 76));
            header.AddChild(TruncatedLabel(source.Name, style, false, style.FontSize + 1));
            header.AddChild(Label($"×{source.TotalOccurrences}", style, true));
            box.AddChild(header);
            box.AddChild(MetricGrid(style, card
                ?
                [
                    Stat("analysis.damageDealt", "Applied damage", damage, style.NegativeColor),
                    Stat("analysis.cardsPlayed", "Plays", plays, Accent(style, 3)),
                    Stat("analysis.energySpent", "Energy", energy, Accent(style, 1)),
                    Stat("analysis.damagePerPlay", "Damage / play", plays > 0m ? damage / plays : 0m,
                        color),
                    Stat("analysis.damagePerEnergy", "Damage / energy", energy > 0m ? damage / energy : 0m,
                        color),
                    Stat("metric.defenseContribution", "Defense contribution", defense, style.PositiveColor),
                    Stat("metric.healingContribution", "Effective healing", healing, style.PositiveColor),
                    Stat("analysis.overkill", "Overkill", source.Totals.GetValueOrDefault(MetricIds.Overkill),
                        style.WarningColor),
                ]
                :
                [
                    Stat("analysis.damageDealt", "Applied damage", damage, style.NegativeColor),
                    Stat("analysis.damageAmplified", "Damage enabled", enabled, Accent(style, 4)),
                    Stat("metric.damagePrevented", "Damage prevented",
                        source.Totals.GetValueOrDefault(MetricIds.DamagePrevented), style.PositiveColor),
                    Stat("metric.healingContribution", "Effective healing", healing, style.PositiveColor),
                    Stat("metric.defenseContribution", "Defense contribution", defense, style.PositiveColor),
                    Stat("analysis.impactPerTrigger", "Impact / trigger",
                        source.TotalOccurrences > 0 ? totalImpact / source.TotalOccurrences : 0m, color),
                    Stat("analysis.overkill", "Overkill", source.Totals.GetValueOrDefault(MetricIds.Overkill),
                        style.WarningColor),
                    Stat("analysis.debuffs", "Debuffs", source.Totals.GetValueOrDefault(MetricIds.DebuffsApplied),
                        style.WarningColor),
                ]));
            return Surface(box, style, color);
        }

        private static Control ContributionRow(
            ContributionRollup rollup,
            string color,
            DashboardStyleDefinition style)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            var header = new HBoxContainer();
            header.AddChild(Badge(rollup.Source.Kind.ToString().ToUpperInvariant(), color, style, 78));
            header.AddChild(TruncatedLabel(rollup.Source.DisplayName, style, false, style.FontSize + 1));
            header.AddChild(Label(ModLocalization.Format("analysis.attributedPlayer", "→ {0}",
                rollup.Contributor.DisplayName), style, true));
            header.AddChild(Label($"×{rollup.Events}", style, true));
            box.AddChild(header);
            box.AddChild(MetricGrid(style,
            [
                Stat("analysis.enabledContribution", "Enabled", rollup.Enabled, Accent(style, 4)),
                Stat("analysis.executionContribution", "Execution", rollup.Execution, style.WarningColor),
                new(ModLocalization.Get("analysis.confidence", "Confidence"),
                    DashboardLocalization.AttributionConfidence(rollup.Confidence), 0m,
                    color),
            ]));
            return Surface(box, style, color);
        }

        private static VariableReconciledRow ContributionVirtualRow(
            ContributionRollup rollup,
            DashboardStyleDefinition style)
        {
            var color = SourceColor(rollup.Source.Kind, style);
            var fingerprint = string.Join('\u001e',
                VisualStyleFingerprint(style),
                rollup.Contributor.Key,
                rollup.Contributor.DisplayName,
                rollup.Source.Key,
                rollup.Source.DisplayName,
                rollup.Source.Kind,
                rollup.Events,
                rollup.Enabled,
                rollup.Execution,
                rollup.Confidence,
                color);
            return new(new($"contribution:{rollup.Contributor.Key}:{rollup.Source.Key}", fingerprint,
                () => ContributionRow(rollup, color, style)), 118f);
        }

        private static HBoxContainer SourceHeader(SourceRollup source, string color,
            DashboardStyleDefinition style)
        {
            var header = new HBoxContainer { CustomMinimumSize = new(0, style.RowHeight) };
            header.AddThemeConstantOverride("separation", 7);
            header.AddChild(Badge(DashboardLocalization.SourceKind(source.Kind), color, style, 78));
            header.AddChild(TruncatedLabel(source.Name, style, false, style.FontSize + 1));
            header.AddChild(Label($"×{source.TotalOccurrences}", style, true));
            header.AddChild(Label(ModLocalization.Format("analysis.sourcePlayers", "{0} players", source.Players.Count),
                style, true));
            return header;
        }

        private void AddSection(string title, string color, DashboardStyleDefinition style, bool compact = false)
        {
            Rows.AddChild(CreateSection(title, color, style, compact));
        }

        private static HBoxContainer CreateSection(
            string title,
            string color,
            DashboardStyleDefinition style,
            bool compact = false)
        {
            var row = new HBoxContainer { CustomMinimumSize = new(0, compact ? 24 : 31) };
            row.AddThemeConstantOverride("separation", 7);
            row.AddChild(new ColorRect { Color = ColorOf(color), CustomMinimumSize = new(4, compact ? 18 : 25) });
            var label = TruncatedLabel(title.ToUpperInvariant(), style, false,
                compact ? Math.Max(10, style.FontSize - 1) : style.FontSize + 1);
            label.Modulate = ColorOf(color);
            row.AddChild(label);
            return row;
        }

        private static GridContainer MetricGrid(DashboardStyleDefinition style, IReadOnlyList<StatCell> cells)
        {
            var grid = ResponsiveGrid(4, 112f, 12, 9);
            foreach (var cell in cells)
            {
                var box = new VBoxContainer
                {
                    CustomMinimumSize = new(112, 44),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                };
                box.AddThemeConstantOverride("separation", -2);
                var value = Label(cell.ValueText, style, false, style.FontSize + 3);
                value.Modulate = ColorOf(cell.Color);
                value.MouseFilter = Control.MouseFilterEnum.Ignore;
                box.AddChild(value);
                var caption = TruncatedLabel(cell.Caption, style, true, Math.Max(9, style.FontSize - 2));
                caption.MouseFilter = Control.MouseFilterEnum.Ignore;
                box.AddChild(caption);
                DashboardTooltip.SetValue(box, cell.Caption, cell.Value, detail: cell.Detail ?? cell.ValueText);
                grid.AddChild(box);
            }

            return grid;
        }

        private static Control TrendChart(
            string title,
            IEnumerable<DashboardLineDatum> data,
            string color,
            DashboardStyleDefinition style)
        {
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 5);
            var heading = new HBoxContainer { CustomMinimumSize = new(0f, 24f) };
            heading.AddThemeConstantOverride("separation", 7);
            heading.AddChild(new ColorRect
            {
                Color = ColorOf(color),
                CustomMinimumSize = new(3f, 16f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            heading.AddChild(TruncatedLabel(title, style, false, style.FontSize + 1));
            body.AddChild(heading);
            var chart = new DashboardLineChart();
            chart.SetData(data, color, Math.Max(10, style.FontSize - 2), DashboardLineSeriesKind.Combat);
            body.AddChild(chart);
            return Surface(body, style, padding: 7);
        }

        private static Control BarChartPanel(
            string title,
            IEnumerable<DashboardBarDatum> data,
            DashboardStyleDefinition style)
        {
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 6);
            var heading = TruncatedLabel(title, style, false, style.FontSize + 1);
            body.AddChild(heading);
            var chart = new DashboardBarChart();
            chart.SetData(data, Math.Max(10, style.FontSize - 2));
            body.AddChild(chart);
            return Surface(body, style, padding: 7);
        }

        private static VBoxContainer CompactMetric(
            string caption,
            decimal value,
            string color,
            DashboardStyleDefinition style,
            string suffix = "")
        {
            var metric = new VBoxContainer { CustomMinimumSize = new(82f, 0f) };
            DashboardTooltip.SetValue(metric, caption, value);
            metric.AddThemeConstantOverride("separation", -3);
            var amount = Label(Format(value) + suffix, style, false, style.FontSize + 1);
            amount.Modulate = ColorOf(color);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            metric.AddChild(amount);
            var label = TruncatedLabel(caption, style, true, Math.Max(9, style.FontSize - 3));
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.TooltipText = caption;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            metric.AddChild(label);
            return metric;
        }

        private static string TurnLabel(TurnSummary turn)
        {
            var turnLabel = turn.Index <= 0 ? ModLocalization.Get("dashboard.setupShort", "SETUP") : $"T{turn.Index}";
            return string.IsNullOrWhiteSpace(turn.CombatLabel) ? turnLabel : $"{turn.CombatLabel}/{turnLabel}";
        }

        private static Button DetailToggle(
            string title,
            bool expanded,
            string color,
            DashboardStyleDefinition style,
            Action toggle)
        {
            var button = new Button
            {
                Text = title,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new(0f, style.RowHeight + 3f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.None,
                Modulate = ColorOf(color),
                TooltipText = ModLocalization.Get(expanded ? "overlay.collapse" : "overlay.expand",
                    expanded ? "Collapse" : "Expand"),
            };
            DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Subtle, true, style);
            DashboardIcons.Apply(button, expanded ? DashboardIcon.Collapse : DashboardIcon.Expand, 17);
            button.Pressed += toggle;
            return button;
        }

        private void ToggleDetails(ref bool expanded)
        {
            expanded = !expanded;
            if (_lastContext == null)
                return;
            var scrollPosition = Scroll.ScrollVertical;
            Refresh(_lastContext);
            Callable.From(() => Scroll.ScrollVertical = scrollPosition).CallDeferred();
        }

        private static StatCell Stat(
            string key,
            string fallback,
            decimal value,
            string color,
            string suffix = "",
            string? detail = null)
        {
            return new(ModLocalization.Get(key, fallback), Format(value) + suffix, value, color, detail);
        }

        private static Dictionary<string, SourceRollup>.ValueCollection AggregatePlayerSources(
            PlayerMetricSnapshot player,
            bool displayFallback = false)
        {
            var rollups = new Dictionary<string, SourceRollup>(StringComparer.Ordinal);
            var metrics = displayFallback
                ? new[]
                {
                    MetricIds.DamageDealt,
                    MetricIds.DamageContribution,
                    MetricIds.DamageAmplified,
                    MetricIds.Overkill,
                }.Select(metricId => (MetricId: metricId,
                    Sources: MetricSourcesForDisplay(player, metricId)))
                : player.Sources.Select(metric => (MetricId: metric.Key, Sources: metric.Value));
            foreach (var (metricId, sources) in metrics)
                foreach (var rawSource in sources)
                {
                    var source = displayFallback ? PresentSource(player, rawSource) : rawSource;
                    if (!rollups.TryGetValue(source.SourceKey, out var rollup))
                    {
                        rollup = new(source.SourceKey, source.SourceKind, source.DisplayName);
                        rollups.Add(source.SourceKey, rollup);
                    }

                    rollup.Totals[metricId] = rollup.Totals.GetValueOrDefault(metricId) + source.Value;
                    rollup.Occurrences[metricId] = rollup.Occurrences.GetValueOrDefault(metricId) + source.Occurrences;
                    rollup.Players.Add(player.PlayerKey);
                }

            return rollups.Values;
        }

        private static Dictionary<string, SourceRollup>.ValueCollection AggregateSources(
            IReadOnlyList<PlayerMetricSnapshot> players,
            bool displayFallback = false)
        {
            var rollups = new Dictionary<string, SourceRollup>(StringComparer.Ordinal);
            foreach (var player in players)
                foreach (var source in AggregatePlayerSources(player, displayFallback))
                {
                    if (!rollups.TryGetValue(source.Key, out var rollup))
                    {
                        rollup = new(source.Key, source.Kind, source.Name);
                        rollups.Add(source.Key, rollup);
                    }

                    foreach (var (metricId, value) in source.Totals)
                        rollup.Totals[metricId] = rollup.Totals.GetValueOrDefault(metricId) + value;
                    foreach (var (metricId, occurrences) in source.Occurrences)
                        rollup.Occurrences[metricId] = rollup.Occurrences.GetValueOrDefault(metricId) + occurrences;
                    rollup.Players.UnionWith(source.Players);
                }

            return rollups.Values;
        }

        private static decimal SourceScore(SourceRollup source)
        {
            return new[]
            {
                MetricIds.DamageContribution,
                MetricIds.DamageDealt,
                MetricIds.BlockGained,
                MetricIds.DamageMitigated,
                MetricIds.HealingReceived,
            }.Max(metricId => SourceMetric(source, metricId));
        }

        private static decimal SourceMetric(SourceRollup source, string metricId)
        {
            return source.Totals.GetValueOrDefault(metricId);
        }

        private static (string MetricId, decimal Value)[] SourceAnalysisMetrics(SourceRollup source)
        {
            var applied = SourceMetric(source, MetricIds.DamageDealt);
            var responsibility = SourceMetric(source, MetricIds.DamageContribution);
            var amplified = SourceMetric(source, MetricIds.DamageAmplified);
            var metrics = new List<(string MetricId, decimal Value)>(4);
            if (applied != 0m)
                metrics.Add((MetricIds.DamageDealt, applied));
            if (responsibility != 0m && (applied != 0m || amplified == 0m || responsibility != amplified))
                metrics.Add((MetricIds.DamageContribution, responsibility));
            if (amplified != 0m)
                metrics.Add((MetricIds.DamageAmplified, amplified));
            var overkill = SourceMetric(source, MetricIds.Overkill);
            if (overkill != 0m)
                metrics.Add((MetricIds.Overkill, overkill));
            return metrics.ToArray();
        }

        private static string MetricName(string metricId)
        {
            var definition = Main.Api.MetricDefinitions.FirstOrDefault(item => item.Id == metricId);
            return definition == null
                ? metricId
                : ModLocalization.Get(definition.NameLocalizationKey, definition.FallbackName);
        }

        private static string MetricColor(string metricId, DashboardStyleDefinition style)
        {
            return metricId switch
            {
                MetricIds.DamageDealt or MetricIds.DamageContribution or MetricIds.DamageTaken or
                    MetricIds.Overkill =>
                    style.NegativeColor,
                MetricIds.DamageBlocked or MetricIds.BlockGained or MetricIds.HealingReceived or
                    MetricIds.DamageMitigated => style.PositiveColor,
                MetricIds.DamageAmplified => Accent(style, 4),
                MetricIds.CardsPlayed or MetricIds.CardsDrawn => Accent(style, 3),
                _ => Accent(style, 1),
            };
        }

        private static string SourceColor(AnalyticsSourceKind kind, DashboardStyleDefinition style)
        {
            return kind switch
            {
                AnalyticsSourceKind.Card => Accent(style, 1),
                AnalyticsSourceKind.Power => Accent(style, 4),
                AnalyticsSourceKind.Potion => Accent(style, 3),
                AnalyticsSourceKind.Orb => Accent(style, 5),
                AnalyticsSourceKind.Relic => style.WarningColor,
                AnalyticsSourceKind.Modifier => style.PositiveColor,
                AnalyticsSourceKind.Creature => style.NegativeColor,
                _ => style.SecondaryTextColor,
            };
        }

        private static string DamageEventLabel(CombatTimelineEvent timelineEvent)
        {
            return $"{timelineEvent.Source?.DisplayName ?? timelineEvent.DisplayText} → " +
                   $"{timelineEvent.Target?.DisplayName}";
        }

        private static void AddMaximum<T>(List<RecordRow> records, RecordGroup group, string key, string fallback,
            IReadOnlyCollection<T> values, Func<T, decimal> value, Func<T, string> label)
        {
            if (values.Count == 0)
                return;
            var maximum = values.MaxBy(value)!;
            var amount = value(maximum);
            if (amount > 0m)
                records.Add(new(group, key, fallback, label(maximum), amount));
        }

        private static string TitleKey(AdvancedDashboardMode value)
        {
            return value switch
            {
                AdvancedDashboardMode.PlayerPerformance => "dashboard.playerPerformance",
                AdvancedDashboardMode.SourceAnalysis => "dashboard.sourceAnalysis",
                AdvancedDashboardMode.DefenseResources => "dashboard.defenseResources",
                AdvancedDashboardMode.CardsAndEffects => "dashboard.cardsEffects",
                AdvancedDashboardMode.ContributionAnalysis => "dashboard.contributionAnalysis",
                AdvancedDashboardMode.TurnAnalysis => "dashboard.turnAnalysis",
                AdvancedDashboardMode.RunTrends => "dashboard.runTrends",
                AdvancedDashboardMode.CombatRecords => "dashboard.combatRecords",
                _ => "dashboard.overview",
            };
        }

        private static string TitleFallback(AdvancedDashboardMode value)
        {
            return value switch
            {
                AdvancedDashboardMode.PlayerPerformance => "Player performance",
                AdvancedDashboardMode.SourceAnalysis => "Source analysis",
                AdvancedDashboardMode.DefenseResources => "Defense and resources",
                AdvancedDashboardMode.CardsAndEffects => "Cards and effects",
                AdvancedDashboardMode.ContributionAnalysis => "Contribution analysis",
                AdvancedDashboardMode.TurnAnalysis => "Turn performance",
                AdvancedDashboardMode.RunTrends => "Run trends",
                AdvancedDashboardMode.CombatRecords => "Combat records",
                _ => "Combat overview",
            };
        }

        private sealed class SourceRollup(string key, AnalyticsSourceKind kind, string name)
        {
            internal string Key { get; } = key;
            internal AnalyticsSourceKind Kind { get; } = kind;
            internal string Name { get; } = name;
            internal Dictionary<string, decimal> Totals { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, int> Occurrences { get; } = new(StringComparer.Ordinal);
            internal HashSet<string> Players { get; } = new(StringComparer.Ordinal);
            internal int TotalOccurrences => Occurrences.Values.DefaultIfEmpty().Max();
        }

        private sealed class ContributionRollup(EntityDescriptor contributor, SourceDescriptor source)
        {
            internal EntityDescriptor Contributor { get; } = contributor;
            internal SourceDescriptor Source { get; } = source;
            internal int Events { get; set; }
            internal decimal Enabled { get; set; }
            internal decimal Execution { get; set; }
            internal AttributionConfidence Confidence { get; set; } = AttributionConfidence.Exact;
            internal decimal Total => Enabled + Execution;
        }

        private sealed record StatCell(
            string Caption,
            string ValueText,
            decimal Value,
            string Color,
            string? Detail = null);

        private sealed record NamedValue(string Name, decimal Value);

        private enum RecordGroup
        {
            Offense,
            Defense,
            Efficiency,
        }

        private sealed record RecordRow(
            RecordGroup Group,
            string LocalizationKey,
            string Fallback,
            string Detail,
            decimal Value);

        private sealed record TurnSummary(
            string CombatLabel,
            string EncounterName,
            int Index,
            decimal Damage,
            decimal Incoming,
            decimal Blocked,
            decimal Block,
            decimal Overkill,
            int Cards,
            int Draws,
            decimal Energy,
            bool Extra,
            TimelineTurnSide Side);
    }
}
