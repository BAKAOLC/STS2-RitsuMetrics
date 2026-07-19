// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Api;
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
        protected override void Render(DashboardRenderContext context)
        {
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
            var players = snapshot.Players.OrderByDescending(player => Metric(player, MetricIds.DamageDealt)).ToArray();
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var damage = Metric(player, MetricIds.DamageDealt);
                var rounds = Math.Max(1, snapshot.RoundCount);
                var energy = Metric(player, MetricIds.EnergySpent);
                var cards = Metric(player, MetricIds.CardsPlayed);
                var survival = SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var accent = Accent(context.Style, index);
                var content = new VBoxContainer();
                content.AddThemeConstantOverride("separation", 6);
                content.AddChild(PlayerHeader(player, index + 1, damage, totalDamage, accent, context.Style,
                    DashboardPresentation.SingleLine(context.Parameters)));
                content.AddChild(MetricGrid(context.Style,
                [
                    Stat("analysis.damagePerTurn", "Damage / turn", damage / rounds, context.Style.NegativeColor),
                    Stat("analysis.damageTaken", "Damage taken", survival.PlayerHpLost,
                        context.Style.WarningColor),
                    Stat("analysis.deaths", "Deaths", survival.PlayerDeaths, context.Style.NegativeColor),
                    Stat("analysis.summonHpLost", "Summon HP lost", survival.SummonHpLost,
                        context.Style.WarningColor),
                    Stat("analysis.summonDeaths", "Summon deaths", survival.SummonDeaths,
                        context.Style.WarningColor),
                    Stat("analysis.blocked", "Damage blocked", Metric(player, MetricIds.DamageBlocked),
                        context.Style.PositiveColor),
                    Stat("analysis.blockGained", "Block gained", Metric(player, MetricIds.BlockGained), accent),
                    Stat("analysis.damageAmplified", "Damage enabled", Metric(player, MetricIds.DamageAmplified),
                        Accent(context.Style, 4)),
                    Stat("analysis.damageMitigated", "Damage mitigated", Metric(player, MetricIds.DamageMitigated),
                        context.Style.PositiveColor),
                    Stat("analysis.cardsPerTurn", "Cards / turn", cards / rounds, Accent(context.Style, 3)),
                    Stat("analysis.damagePerEnergy", "Damage / energy", energy > 0m ? damage / energy : 0m,
                        Accent(context.Style, 1)),
                    Stat("analysis.healing", "Healing", Metric(player, MetricIds.HealingReceived),
                        context.Style.PositiveColor),
                ]));
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
                Empty(context);
                return;
            }

            var sources = AggregateSources(snapshot.Players).OrderByDescending(SourceScore).Take(250).ToArray();
            if (sources.Length == 0)
            {
                Empty(context, ModLocalization.Get("analysis.noSources", "No source data"));
                return;
            }

            var maximum = Math.Max(1m, sources.Max(SourceScore));
            Rows.AddChild(BarChartPanel(ModLocalization.Get("overview.topSources", "Top sources"),
                sources.Take(12).Select(source => new DashboardBarDatum(source.Name, SourceScore(source),
                    SourceColor(source.Kind, context.Style), Format(SourceScore(source)))), context.Style));
            AddSection(ModLocalization.Get("analysis.sourceDetails", "Source details"), Accent(context.Style, 1),
                context.Style, true);
            foreach (var source in sources)
            {
                var color = SourceColor(source.Kind, context.Style);
                var content = new VBoxContainer();
                content.AddThemeConstantOverride("separation", 4);
                content.AddChild(SourceHeader(source, color, context.Style));
                var metrics = source.Totals.Where(pair => pair.Value != 0m)
                    .OrderByDescending(pair => Math.Abs(pair.Value)).Take(6).ToArray();
                foreach (var (metricId, value) in metrics)
                    content.AddChild(Meter(MetricName(metricId), $"{Format(value)}  ·  ×{source.Occurrences[metricId]}",
                        Math.Abs(value), maximum, MetricColor(metricId, context.Style), context.Style,
                        Math.Max(23, context.Style.RowHeight - 7)));
                Rows.AddChild(Surface(content, context.Style, color));
            }

            Status.Text = ModLocalization.Format("analysis.sourceSummary", "{0} sources across {1} players",
                sources.Length, snapshot.Players.Count);
        }

        private void RenderDefense(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                Empty(context);
                return;
            }

            foreach (var (player, index) in snapshot.Players
                         .OrderByDescending(player => SnapshotStatistics.Survival(snapshot, player.PlayerNetId)
                             .PlayerHpLost)
                         .Select((player, index) => (player, index)))
            {
                var (taken, deaths, summonHpLost, summonDeaths) =
                    SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var blocked = Metric(player, MetricIds.DamageBlocked);
                var gained = Metric(player, MetricIds.BlockGained);
                var energy = Metric(player, MetricIds.EnergySpent);
                var cards = Metric(player, MetricIds.CardsPlayed);
                var accent = Accent(context.Style, index);
                var content = new VBoxContainer();
                content.AddThemeConstantOverride("separation", 5);
                content.AddChild(PlayerHeader(player, 0, taken, taken + blocked, accent, context.Style,
                    DashboardPresentation.SingleLine(context.Parameters)));
                content.AddChild(SegmentedMeter(
                    $"{ModLocalization.Get("analysis.hpLost", "HP lost")} {Format(taken)}  ·  " +
                    $"{ModLocalization.Get("analysis.blocked", "blocked")} {Format(blocked)}",
                    taken, blocked, context.Style.NegativeColor, context.Style.PositiveColor, context.Style));
                content.AddChild(MetricGrid(context.Style,
                [
                    Stat("analysis.blockGained", "Block gained", gained, context.Style.PositiveColor),
                    Stat("analysis.blockEfficiency", "Block efficiency", gained > 0m ? blocked / gained * 100m : 0m,
                        accent, "%"),
                    Stat("analysis.healing", "Healing", Metric(player, MetricIds.HealingReceived),
                        context.Style.PositiveColor),
                    Stat("analysis.energySpent", "Energy spent", energy, Accent(context.Style, 3)),
                    Stat("analysis.cardsPlayed", "Cards played", cards, Accent(context.Style, 1)),
                    Stat("analysis.cardsPerEnergy", "Cards / energy", energy > 0m ? cards / energy : 0m,
                        Accent(context.Style, 4)),
                    Stat("analysis.cardsDrawn", "Cards drawn", Metric(player, MetricIds.CardsDrawn), accent),
                    Stat("analysis.cardsDiscarded", "Cards discarded", Metric(player, MetricIds.CardsDiscarded),
                        context.Style.WarningColor),
                    Stat("analysis.cardsExhausted", "Cards exhausted", Metric(player, MetricIds.CardsExhausted),
                        context.Style.NegativeColor),
                    Stat("analysis.deaths", "Deaths", deaths, context.Style.NegativeColor),
                    Stat("analysis.summonHpLost", "Summon HP lost", summonHpLost,
                        context.Style.WarningColor),
                    Stat("analysis.summonDeaths", "Summon deaths", summonDeaths,
                        context.Style.WarningColor),
                ]));
                AddIncomingSources(content, snapshot, player, context.Style);
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
                Empty(context);
                return;
            }

            foreach (var player in snapshot.Players)
            {
                AddSection(player.DisplayName, Accent(context.Style, Rows.GetChildCount()), context.Style);
                var sourceRows = AggregatePlayerSources(player).ToArray();
                var cards = sourceRows.Where(source => source.Kind == AnalyticsSourceKind.Card)
                    .OrderByDescending(SourceScore).ToArray();
                foreach (var card in cards)
                    Rows.AddChild(CardEffectRow(card, true, context.Style));
                var effects = sourceRows.Where(source => source.Kind != AnalyticsSourceKind.Card)
                    .Where(source => SourceScore(source) > 0m)
                    .OrderByDescending(SourceScore).Take(40).ToArray();
                if (effects.Length > 0)
                    AddSection(ModLocalization.Get("analysis.nonCardEffects", "Powers, relics and effects"),
                        Accent(context.Style, 4), context.Style, true);
                foreach (var effect in effects)
                    Rows.AddChild(CardEffectRow(effect, false, context.Style));
            }

            Status.Text = ModLocalization.Format("analysis.cardEffectSummary",
                "Card and effect drill-down · {0} players",
                snapshot.Players.Count);
        }

        private void RenderContributions(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                Empty(context);
                return;
            }

            var damageEvents = Timeline(snapshot).Where(IsPlayerOffenseEvent).ToArray();
            var contributions = new Dictionary<string, ContributionRollup>(StringComparer.Ordinal);
            foreach (var timelineEvent in damageEvents)
            foreach (var contribution in timelineEvent.Damage!.Contributions)
            {
                if (DamageContributionSemantics.GetRole(contribution) == DamageContributionRole.Settlement)
                    continue;
                if (!contributions.TryGetValue(contribution.Source.Key, out var rollup))
                {
                    rollup = new(contribution.Source);
                    contributions.Add(contribution.Source.Key, rollup);
                }

                rollup.Events++;
                rollup.Raw += contribution.RawContribution;
                if (contribution.Stage == DamageContributionStage.Base)
                    rollup.Direct += Math.Max(0m, contribution.EffectiveContribution);
                else if (contribution.EffectiveContribution > 0m)
                    rollup.Enabled += contribution.EffectiveContribution;
                else
                    rollup.Prevented += -contribution.EffectiveContribution;
                if (contribution.Stage == DamageContributionStage.Execution)
                    rollup.Execution += Math.Max(0m, contribution.EffectiveContribution);
                if (contribution.Confidence > rollup.Confidence)
                    rollup.Confidence = contribution.Confidence;
            }

            var ranked = contributions.Values.OrderByDescending(item => item.Total).Take(200).ToArray();
            Rows.AddChild(BarChartPanel(ModLocalization.Get("dashboard.contributionAnalysis",
                "Contribution analysis"), ranked.Take(12).Select(rollup => new DashboardBarDatum(
                rollup.Source.DisplayName, rollup.Total, SourceColor(rollup.Source.Kind, context.Style),
                Format(rollup.Total))), context.Style));
            AddSection(ModLocalization.Get("analysis.contributionDetails", "Contribution details"),
                Accent(context.Style, 4), context.Style, true);
            foreach (var rollup in ranked)
            {
                var color = SourceColor(rollup.Source.Kind, context.Style);
                var box = new VBoxContainer();
                box.AddThemeConstantOverride("separation", 4);
                var header = new HBoxContainer();
                header.AddChild(Badge(rollup.Source.Kind.ToString().ToUpperInvariant(), color, context.Style, 78));
                header.AddChild(TruncatedLabel(rollup.Source.DisplayName, context.Style, false,
                    context.Style.FontSize + 1));
                header.AddChild(Label($"×{rollup.Events}", context.Style, true));
                box.AddChild(header);
                box.AddChild(MetricGrid(context.Style,
                [
                    Stat("analysis.directContribution", "Direct", rollup.Direct, context.Style.NegativeColor),
                    Stat("analysis.enabledContribution", "Enabled", rollup.Enabled, Accent(context.Style, 4)),
                    Stat("analysis.preventedContribution", "Prevented", rollup.Prevented,
                        context.Style.PositiveColor),
                    Stat("analysis.executionContribution", "Execution", rollup.Execution,
                        context.Style.WarningColor),
                    new(ModLocalization.Get("analysis.rawDelta", "Raw delta"), Format(rollup.Raw),
                        Math.Abs(rollup.Raw), Accent(context.Style, 1)),
                    new(ModLocalization.Get("analysis.confidence", "Confidence"),
                        DashboardLocalization.AttributionConfidence(rollup.Confidence), 0m,
                        color),
                ]));
                Rows.AddChild(Surface(box, context.Style, color));
            }

            AddAttributionSummary(snapshot, context.Style);
            Status.Text = ModLocalization.Format("analysis.contributionSummary",
                "{0} damage events · {1} contributing sources", damageEvents.Length, contributions.Count);
        }

        private void RenderTurns(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                Empty(context);
                return;
            }

            var turns = Timeline(snapshot).GroupBy(item => item.TurnIndex).OrderBy(group => group.Key)
                .Select(SummarizeTurn).ToArray();
            var charts = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            charts.AddThemeConstantOverride("h_separation", 8);
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damageDealt", "Damage"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn.Index), turn.Damage)),
                context.Style.NegativeColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damageTaken", "Damage taken"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn.Index), turn.Incoming)),
                context.Style.WarningColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.blockGained", "Block"),
                turns.Select(turn => new DashboardLineDatum(TurnLabel(turn.Index), turn.Block)),
                context.Style.PositiveColor, context.Style));
            Rows.AddChild(charts);
            AddSection(ModLocalization.Get("analysis.turnDetails", "Turn details"), Accent(context.Style, 1),
                context.Style, true);
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
                    DashboardLocalization.TurnSide(turn.Side) +
                    (turn.Extra ? $" · {ModLocalization.Get("analysis.extraTurn", "Extra turn")}" : ""),
                    context.Style, false, context.Style.FontSize + 1));
                header.AddChild(Label(ModLocalization.Format("analysis.eventCount", "{0} events",
                    turn.EventCount), context.Style, true));
                box.AddChild(header);
                box.AddChild(MetricGrid(context.Style,
                [
                    Stat("analysis.damageDealt", "Damage", turn.Damage, context.Style.NegativeColor),
                    Stat("analysis.damageTaken", "Damage taken", turn.Incoming, context.Style.WarningColor),
                    Stat("analysis.blockGained", "Block", turn.Block, context.Style.PositiveColor),
                    new(ModLocalization.Get("analysis.cardsPlayed", "Cards played"),
                        turn.Cards.ToString(CultureInfo.CurrentCulture), turn.Cards,
                        Accent(context.Style, 3)),
                    new(ModLocalization.Get("analysis.cardsDrawn", "Cards drawn"),
                        turn.Draws.ToString(CultureInfo.CurrentCulture), turn.Draws,
                        Accent(context.Style, 1)),
                    Stat("analysis.energySpent", "Energy spent", turn.Energy, Accent(context.Style, 4)),
                    new(ModLocalization.Get("analysis.modifiers", "Modifiers"),
                        turn.Modifiers.ToString(CultureInfo.CurrentCulture), turn.Modifiers,
                        context.Style.WarningColor),
                ]));
                Rows.AddChild(Surface(box, context.Style, color));
            }

            Status.Text = ModLocalization.Format("analysis.turnSummary", "{0} timeline turns · {1} rounds",
                turns.Length, snapshot.RoundCount);
        }

        private void RenderRunTrends(DashboardRenderContext context)
        {
            var combats = context.Run?.Combats ?? (context.Snapshot == null ? [] : [context.Snapshot]);
            if (combats.Count == 0)
            {
                Empty(context);
                return;
            }

            var ordered = combats.OrderBy(item => item.StartedAtUtc).ToArray();
            var charts = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            charts.AddThemeConstantOverride("h_separation", 8);
            charts.AddThemeConstantOverride("v_separation", 8);
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damageDealt", "Damage"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}", TotalDamage(combat))),
                context.Style.NegativeColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damageTaken", "Damage taken"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}",
                    TotalSurvival(combat).PlayerHpLost)),
                context.Style.WarningColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.summonHpLost", "Summon HP lost"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}",
                    TotalSurvival(combat).SummonHpLost)), Accent(context.Style, 4), context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.summonDeaths", "Summon deaths"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}",
                    TotalSurvival(combat).SummonDeaths)), context.Style.NegativeColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.blocked", "Blocked"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}",
                    combat.Players.Sum(player => Metric(player, MetricIds.DamageBlocked)))),
                context.Style.PositiveColor, context.Style));
            charts.AddChild(TrendChart(ModLocalization.Get("analysis.damagePerTurn", "Damage / turn"),
                ordered.Select(combat => new DashboardLineDatum($"F{combat.Floor}",
                    TotalDamage(combat) / Math.Max(1, combat.RoundCount))), Accent(context.Style, 1), context.Style));
            Rows.AddChild(charts);
            AddSection(ModLocalization.Get("analysis.combatDetails", "Combat details"), Accent(context.Style, 1),
                context.Style, true);
            foreach (var combat in ordered)
            {
                var damage = TotalDamage(combat);
                var (taken, _, summonHpLost, summonDeaths) = TotalSurvival(combat);
                var blocked = combat.Players.Sum(player => Metric(player, MetricIds.DamageBlocked));
                var cards = combat.Players.Sum(player => Metric(player, MetricIds.CardsPlayed));
                var rounds = Math.Max(1, combat.RoundCount);
                var color = Accent(context.Style, Math.Max(0, combat.ActIndex));
                var row = new HBoxContainer { CustomMinimumSize = new(0f, context.Style.RowHeight + 5f) };
                row.AddThemeConstantOverride("separation", 9);
                row.AddChild(Badge(ModLocalization.Format("analysis.floorShort", "F{0}", combat.Floor), color,
                    context.Style));
                row.AddChild(TruncatedLabel(combat.EncounterName, context.Style, false,
                    context.Style.FontSize + 1));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.damageDealt", "Damage"), damage,
                    context.Style.NegativeColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.damagePerTurn", "Damage / turn"),
                    damage / rounds, color, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.damageTaken", "Damage taken"), taken,
                    context.Style.WarningColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.blocked", "Blocked"), blocked,
                    context.Style.PositiveColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.summonHpLost", "Summon HP lost"),
                    summonHpLost, context.Style.WarningColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.summonDeaths", "Summon deaths"),
                    summonDeaths, context.Style.NegativeColor, context.Style));
                row.AddChild(CompactMetric(ModLocalization.Get("analysis.cardsPerTurn", "Cards / turn"),
                    cards / rounds, Accent(context.Style, 3), context.Style));
                Rows.AddChild(Surface(row, context.Style, padding: 5));
            }

            Status.Text = ModLocalization.Format("analysis.runTrendSummary", "{0} combats · {1} total damage",
                combats.Count, Format(combats.Sum(TotalDamage)));
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
            AddMaximum(records, "analysis.recordHighestHit", "Highest effective hit", damageEvents,
                item => DamageOutput(item.Damage!), DamageEventLabel);
            AddMaximum(records, "analysis.recordLargestRequest", "Largest requested hit", damageEvents,
                item => item.Damage!.RequestedAmount, DamageEventLabel);
            var turnDamage = damageEvents.GroupBy(item => (item.CombatId, item.TurnIndex))
                .Select(group => new NamedValue($"T{group.Key.TurnIndex}",
                    group.Sum(item => DamageOutput(item.Damage!))))
                .ToArray();
            AddMaximum(records, "analysis.recordBestTurn", "Best damage turn", turnDamage, item => item.Value,
                item => item.Name);
            var playerCombats = combats.SelectMany(combat => combat.Players.Select(player => new NamedValue(
                $"{player.DisplayName} · {combat.EncounterName}", Metric(player, MetricIds.DamageDealt)))).ToArray();
            AddMaximum(records, "analysis.recordBestCombat", "Best player combat", playerCombats,
                item => item.Value, item => item.Name);
            var cardSources = combats.SelectMany(combat => combat.Players)
                .SelectMany(AggregatePlayerSources)
                .Where(source => source.Kind == AnalyticsSourceKind.Card)
                .GroupBy(source => source.Name, StringComparer.CurrentCulture)
                .Select(group => new NamedValue(group.Key,
                    group.Sum(source => source.Totals.GetValueOrDefault(MetricIds.DamageDealt)))).ToArray();
            AddMaximum(records, "analysis.recordTopCard", "Top damage card", cardSources, item => item.Value,
                item => item.Name);
            var contributions = damageEvents.SelectMany(item => item.Damage!.Contributions).ToArray();
            AddMaximum(records, "analysis.recordAmplifier", "Largest amplification", contributions
                .Where(item => item.EffectiveContribution > 0m &&
                               DamageContributionSemantics.GetRole(item) == DamageContributionRole.Modifier)
                .ToArray(), item => item.EffectiveContribution, item => item.Source.DisplayName);
            AddMaximum(records, "analysis.recordMitigator", "Largest mitigation", contributions
                    .Where(item => item.EffectiveContribution < 0m &&
                                   DamageContributionSemantics.GetRole(item) == DamageContributionRole.Modifier)
                    .ToArray(), item => -item.EffectiveContribution,
                item => item.Source.DisplayName);
            var blocks = combats.SelectMany(combat => combat.Players.Select(player => new NamedValue(
                player.DisplayName, Metric(player, MetricIds.BlockGained)))).ToArray();
            AddMaximum(records, "analysis.recordBlock", "Most block gained", blocks, item => item.Value,
                item => item.Name);

            foreach (var (record, index) in records.OrderByDescending(item => item.Value)
                         .Select((record, index) => (record, index)))
            {
                var color = Accent(context.Style, index);
                var row = new HBoxContainer { CustomMinimumSize = new(0, context.Style.RowHeight + 12) };
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Badge((index + 1).ToString(CultureInfo.CurrentCulture), color, context.Style, 36));
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

        private static void AddIncomingSources(VBoxContainer content, CombatSnapshot snapshot,
            PlayerMetricSnapshot player,
            DashboardStyleDefinition style)
        {
            var timelineSources = Timeline(snapshot)
                .Where(timelineEvent => timelineEvent.Target is
                {
                    Kind: AnalyticsEntityKind.Player,
                    PlayerNetId: not null,
                } && timelineEvent.Target.PlayerNetId == player.PlayerNetId)
                .Select(timelineEvent => new
                {
                    timelineEvent.Source,
                    Value = SnapshotStatistics.EffectiveHpLost(timelineEvent),
                })
                .Where(item => item.Source != null && item.Value > 0m)
                .GroupBy(item => item.Source!.Key, StringComparer.Ordinal)
                .Select(group => new IncomingSource(group.First().Source!.DisplayName,
                    group.Sum(item => item.Value), group.Count()))
                .OrderByDescending(source => source.Value)
                .ToArray();
            var sources = timelineSources.Length > 0
                ? timelineSources
                : player.Sources.GetValueOrDefault(MetricIds.DamageTaken, [])
                    .Select(source => new IncomingSource(source.DisplayName, source.Value, source.Occurrences))
                    .ToArray();
            if (sources.Length == 0)
                return;
            content.AddChild(Label(ModLocalization.Get("analysis.incomingSources", "Top incoming sources"), style,
                true));
            var maximum = Math.Max(1m, sources.Max(source => source.Value));
            foreach (var source in sources.Take(5))
                content.AddChild(Meter(source.Name, $"{Format(source.Value)} · ×{source.Occurrences}",
                    source.Value, maximum, style.NegativeColor, style, Math.Max(22, style.RowHeight - 8)));
        }

        private void AddAttributionSummary(CombatSnapshot snapshot, DashboardStyleDefinition style)
        {
            var shares = Timeline(snapshot).Where(IsPlayerOffenseEvent)
                .SelectMany(item => item.Damage!.AttributionShares!
                    .Where(share => share.Contributor.Kind == AnalyticsEntityKind.Player))
                .GroupBy(item => item.Contributor.Key)
                .Select(group => new
                {
                    group.First().Contributor,
                    Value = group.Sum(item => item.EffectiveContribution),
                    Sources = group.Select(item => item.Source.Key).Distinct(StringComparer.Ordinal).Count(),
                }).OrderByDescending(item => item.Value).ToArray();
            if (shares.Length == 0)
                return;
            AddSection(ModLocalization.Get("analysis.attributionShares", "Effective attribution shares"),
                style.WarningColor, style, true);
            var maximum = Math.Max(1m, shares.Max(item => item.Value));
            foreach (var share in shares)
                Rows.AddChild(Meter(share.Contributor.DisplayName,
                    ModLocalization.Format("analysis.sourceValue", "{0} · {1} sources", Format(share.Value),
                        share.Sources), share.Value, maximum,
                    style.WarningColor, style));
        }

        private static bool IsPlayerOffenseEvent(CombatTimelineEvent timelineEvent)
        {
            return timelineEvent is
                   {
                       Damage.AttributionShares.Count: > 0,
                       Target.Kind: not (AnalyticsEntityKind.Player or AnalyticsEntityKind.Summon),
                   } &&
                   timelineEvent.Damage.AttributionShares.Any(share =>
                       share.Contributor.Kind == AnalyticsEntityKind.Player);
        }

        private static Control CardEffectRow(SourceRollup source, bool card, DashboardStyleDefinition style)
        {
            var color = SourceColor(source.Kind, style);
            var damage = source.Totals.GetValueOrDefault(MetricIds.DamageDealt);
            var block = source.Totals.GetValueOrDefault(MetricIds.BlockGained);
            var plays = source.Totals.GetValueOrDefault(MetricIds.CardsPlayed);
            var energy = source.Totals.GetValueOrDefault(MetricIds.EnergySpent);
            var enabled = source.Totals.GetValueOrDefault(MetricIds.DamageAmplified);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            var header = new HBoxContainer();
            header.AddChild(Badge(card
                ? ModLocalization.Get("overview.sourceKind.card", "Cards")
                : DashboardLocalization.SourceKind(source.Kind), color, style, 76));
            header.AddChild(TruncatedLabel(source.Name, style, false, style.FontSize + 1));
            header.AddChild(Label($"×{source.TotalOccurrences}", style, true));
            box.AddChild(header);
            box.AddChild(MetricGrid(style,
            [
                Stat("analysis.damageDealt", "Damage", damage, style.NegativeColor),
                Stat("analysis.blockGained", "Block", block, style.PositiveColor),
                Stat("analysis.cardsPlayed", "Plays", plays, Accent(style, 3)),
                Stat("analysis.energySpent", "Energy", energy, Accent(style, 1)),
                Stat("analysis.damagePerPlay", "Damage / play", plays > 0m ? damage / plays : 0m, color),
                Stat("analysis.damagePerEnergy", "Damage / energy", energy > 0m ? damage / energy : 0m, color),
                Stat("analysis.damageAmplified", "Damage enabled", enabled, Accent(style, 4)),
                Stat("analysis.debuffs", "Debuffs", source.Totals.GetValueOrDefault(MetricIds.DebuffsApplied),
                    style.WarningColor),
            ]));
            return Surface(box, style, color);
        }

        private static HBoxContainer SourceHeader(SourceRollup source, string color,
            DashboardStyleDefinition style)
        {
            var header = new HBoxContainer { CustomMinimumSize = new(0, style.RowHeight) };
            header.AddThemeConstantOverride("separation", 7);
            header.AddChild(Badge(DashboardLocalization.SourceKind(source.Kind), color, style, 78));
            header.AddChild(TruncatedLabel(source.Name, style, false, style.FontSize + 1));
            header.AddChild(Label(ModLocalization.Format("analysis.sourcePlayers", "{0} players", source.Players.Count),
                style, true));
            return header;
        }

        private void AddSection(string title, string color, DashboardStyleDefinition style, bool compact = false)
        {
            var row = new HBoxContainer { CustomMinimumSize = new(0, compact ? 24 : 31) };
            row.AddThemeConstantOverride("separation", 7);
            row.AddChild(new ColorRect { Color = ColorOf(color), CustomMinimumSize = new(4, compact ? 18 : 25) });
            var label = TruncatedLabel(title.ToUpperInvariant(), style, false,
                compact ? Math.Max(10, style.FontSize - 1) : style.FontSize + 1);
            label.Modulate = ColorOf(color);
            row.AddChild(label);
            Rows.AddChild(row);
        }

        private static GridContainer MetricGrid(DashboardStyleDefinition style, IReadOnlyList<StatCell> cells)
        {
            var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 9);
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
                box.AddChild(value);
                box.AddChild(TruncatedLabel(cell.Caption, style, true, Math.Max(9, style.FontSize - 2)));
                DashboardTooltip.SetValue(box, cell.Caption, cell.Value, detail: cell.ValueText);
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
            chart.SetData(data, color, Math.Max(10, style.FontSize - 2));
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
            DashboardStyleDefinition style)
        {
            var metric = new VBoxContainer { CustomMinimumSize = new(82f, 0f) };
            DashboardTooltip.SetValue(metric, caption, value);
            metric.AddThemeConstantOverride("separation", -3);
            var amount = Label(Format(value), style, false, style.FontSize + 1);
            amount.Modulate = ColorOf(color);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            metric.AddChild(amount);
            var label = TruncatedLabel(caption, style, true, Math.Max(9, style.FontSize - 3));
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.TooltipText = caption;
            metric.AddChild(label);
            return metric;
        }

        private static TurnSummary SummarizeTurn(IGrouping<int, CombatTimelineEvent> turn)
        {
            var events = turn.ToArray();
            return new(turn.Key,
                events.Where(item => item.Damage != null && item.Target?.Kind != AnalyticsEntityKind.Player)
                    .Sum(item => DamageOutput(item.Damage!)),
                events.Where(item => item is { Damage: not null, Target.Kind: AnalyticsEntityKind.Player })
                    .Sum(item => item.Damage!.HpLost),
                events.Where(item => item is
                        { Kind: CombatTimelineKind.Block, Actor.Kind: AnalyticsEntityKind.Player })
                    .Sum(item => item.Value ?? 0m),
                events.Count(item => item is
                    { Kind: CombatTimelineKind.CardPlay, Phase: TimelineEventPhase.Started }),
                events.Count(item => item.Kind == CombatTimelineKind.CardDraw),
                events.Where(item => item is { Kind: CombatTimelineKind.Energy, ActionId: "energy.spend" })
                    .Sum(item => item.Value ?? 0m),
                events.Count(item => item.Kind == CombatTimelineKind.DamageModifier),
                events.Any(item => item.IsExtraTurn),
                events.FirstOrDefault(item => item.Kind == CombatTimelineKind.Turn)?.Side ?? TimelineTurnSide.None,
                events.Length);
        }

        private static decimal DamageOutput(DamageBreakdown damage)
        {
            return damage.HpLost + damage.BlockedAmount;
        }

        private static string TurnLabel(int turn)
        {
            return turn <= 0 ? ModLocalization.Get("dashboard.setupShort", "SETUP") : $"T{turn}";
        }

        private static StatCell Stat(string key, string fallback, decimal value, string color, string suffix = "")
        {
            return new(ModLocalization.Get(key, fallback), Format(value) + suffix, value, color);
        }

        private static Dictionary<string, SourceRollup>.ValueCollection AggregatePlayerSources(
            PlayerMetricSnapshot player)
        {
            var rollups = new Dictionary<string, SourceRollup>(StringComparer.Ordinal);
            foreach (var (metricId, sources) in player.Sources)
            foreach (var source in sources)
            {
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
            IReadOnlyList<PlayerMetricSnapshot> players)
        {
            var rollups = new Dictionary<string, SourceRollup>(StringComparer.Ordinal);
            foreach (var player in players)
            foreach (var source in AggregatePlayerSources(player))
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
            return source.Totals.GetValueOrDefault(MetricIds.DamageDealt) +
                   source.Totals.GetValueOrDefault(MetricIds.DamageAmplified) +
                   source.Totals.GetValueOrDefault(MetricIds.DamageMitigated) +
                   source.Totals.GetValueOrDefault(MetricIds.BlockGained) +
                   source.Totals.GetValueOrDefault(MetricIds.HealingReceived) +
                   source.Totals.Values.Where(value => value > 0m).Sum() * 0.001m;
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
                MetricIds.DamageDealt or MetricIds.DamageTaken or MetricIds.Overkill => style.NegativeColor,
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

        private static decimal TotalDamage(CombatSnapshot combat)
        {
            return combat.Players.Sum(player => Metric(player, MetricIds.DamageDealt));
        }

        private static SurvivalStatistics TotalSurvival(CombatSnapshot combat)
        {
            return combat.Players.Aggregate(default(SurvivalStatistics),
                (total, player) => total + SnapshotStatistics.Survival(combat, player.PlayerNetId));
        }

        private static string DamageEventLabel(CombatTimelineEvent timelineEvent)
        {
            return $"{timelineEvent.Source?.DisplayName ?? timelineEvent.DisplayText} → " +
                   $"{timelineEvent.Target?.DisplayName}";
        }

        private static void AddMaximum<T>(List<RecordRow> records, string key, string fallback,
            IReadOnlyCollection<T> values, Func<T, decimal> value, Func<T, string> label)
        {
            if (values.Count == 0)
                return;
            var maximum = values.MaxBy(value)!;
            var amount = value(maximum);
            if (amount > 0m)
                records.Add(new(key, fallback, label(maximum), amount));
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
            internal int TotalOccurrences => Occurrences.Values.Sum();
        }

        private sealed class ContributionRollup(SourceDescriptor source)
        {
            internal SourceDescriptor Source { get; } = source;
            internal int Events { get; set; }
            internal decimal Direct { get; set; }
            internal decimal Enabled { get; set; }
            internal decimal Prevented { get; set; }
            internal decimal Execution { get; set; }
            internal decimal Raw { get; set; }
            internal AttributionConfidence Confidence { get; set; } = AttributionConfidence.Exact;
            internal decimal Total => Direct + Enabled + Prevented + Execution;
        }

        private sealed record StatCell(string Caption, string ValueText, decimal Value, string Color);

        private sealed record NamedValue(string Name, decimal Value);

        private sealed record IncomingSource(string Name, decimal Value, int Occurrences);

        private sealed record RecordRow(string LocalizationKey, string Fallback, string Detail, decimal Value);

        private sealed record TurnSummary(
            int Index,
            decimal Damage,
            decimal Incoming,
            decimal Block,
            int Cards,
            int Draws,
            decimal Energy,
            int Modifiers,
            bool Extra,
            TimelineTurnSide Side,
            int EventCount);
    }
}
