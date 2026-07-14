// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class AnalysisCenter
    {
        private HBoxContainer _header = null!;

        private void BuildUi()
        {
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeStyleboxOverride("panel", ShellStyle());
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            root.AddThemeConstantOverride("separation", 0);
            AddChild(root);

            BuildHeader(root);
            var content = new MarginContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            content.AddThemeConstantOverride("margin_left", 12);
            content.AddThemeConstantOverride("margin_top", 8);
            content.AddThemeConstantOverride("margin_right", 12);
            content.AddThemeConstantOverride("margin_bottom", 9);
            root.AddChild(content);
            var body = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            body.AddThemeConstantOverride("separation", 7);
            content.AddChild(body);
            BuildSelectors(body);
            BuildMainArea(body);

            _dialogs = new();
            AddChild(_dialogs);
        }

        private void BuildHeader(VBoxContainer root)
        {
            var headerSurface = new PanelContainer();
            headerSurface.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new("101824FA"),
                BorderColor = new("4B607AE0"),
                BorderWidthBottom = 1,
                ContentMarginLeft = 13f,
                ContentMarginTop = 8f,
                ContentMarginRight = 9f,
                ContentMarginBottom = 8f,
            });
            root.AddChild(headerSurface);
            _header = new()
            {
                CustomMinimumSize = new(0, 60),
                MouseFilter = MouseFilterEnum.Pass,
            };
            _header.AddThemeConstantOverride("separation", 7);
            headerSurface.AddChild(_header);
            var accent = new ColorRect
            {
                Color = new("42A8E8FF"),
                CustomMinimumSize = new(4, 0),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _header.AddChild(accent);
            var titles = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
                ClipContents = true,
            };
            titles.AddThemeConstantOverride("separation", -3);
            var title = new Label
            {
                Text = ModLocalization.Get("analysis.title", "Analytics center"),
                MouseFilter = MouseFilterEnum.Ignore,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SectionTitleFontSize);
            title.AddThemeColorOverride("font_color", new("F1F5FBFF"));
            titles.AddChild(title);
            var subtitle = new Label
            {
                Text = ModLocalization.Get("analysis.subtitle", "Runs, combats, attribution and causal timelines"),
                MouseFilter = MouseFilterEnum.Ignore,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            subtitle.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            subtitle.AddThemeColorOverride("font_color", new("94A3B8FF"));
            titles.AddChild(subtitle);
            _header.AddChild(titles);
            var refresh = HeaderButton("↻", ModLocalization.Get("analysis.refresh", "Refresh history"),
                DashboardButtonKind.Subtle);
            refresh.Pressed += ReloadRuns;
            _header.AddChild(refresh);
            var exportJson = HeaderButton("JSON",
                ModLocalization.Get("analysis.exportJson", "Export selection as JSON"), DashboardButtonKind.Standard,
                54f);
            exportJson.Pressed += () => ExportSelection(MetricsExportFormat.Json);
            _header.AddChild(exportJson);
            var exportCsv = HeaderButton("CSV", ModLocalization.Get("analysis.exportCsv", "Export selection as CSV"),
                DashboardButtonKind.Standard, 48f);
            exportCsv.Pressed += () => ExportSelection(MetricsExportFormat.Csv);
            _header.AddChild(exportCsv);
            _close = HeaderButton("×", ModLocalization.Get("overlay.close", "Close"),
                DashboardButtonKind.Danger);
            _close.Pressed += Hide;
            _header.AddChild(_close);
        }

        private void BuildSelectors(VBoxContainer body)
        {
            var selectors = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            selectors.AddThemeConstantOverride("separation", 8);
            body.AddChild(selectors);
            _dashboard = Selector(ModLocalization.Get("analysis.page", "Analysis page"));
            _dashboard.ItemSelected += _ =>
            {
                ReplaceRenderer();
                UpdateMetricVisibility();
                MarkDirty();
            };
            selectors.AddChild(SelectorField(ModLocalization.Get("analysis.page", "Analysis page"), _dashboard,
                1.45f));
            _scope = Selector(ModLocalization.Get("analysis.range", "Selected combat or whole run"));
            _scope.AddItem(ModLocalization.Get("analysis.selectedCombat", "Selected combat"));
            _scope.AddItem(ModLocalization.Get("analysis.wholeRun", "Whole run"));
            _scope.ItemSelected += _ =>
            {
                _historyHash = 0;
                MarkDirty();
            };
            selectors.AddChild(SelectorField(ModLocalization.Get("analysis.range", "Selected combat or whole run"),
                _scope));
            _metric = Selector(ModLocalization.Get("dashboard.metric", "Metric"));
            _metric.ItemSelected += _ => MarkDirty();
            _metricField = SelectorField(ModLocalization.Get("dashboard.metric", "Metric"), _metric);
            selectors.AddChild(_metricField);
        }

        private static VBoxContainer SelectorField(string title, DashboardDropdown option, float stretch = 1f)
        {
            var content = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = stretch,
            };
            content.AddThemeConstantOverride("separation", 3);
            var label = new Label { Text = title };
            DashboardControlTheme.ApplyFieldLabel(label);
            content.AddChild(label);
            content.AddChild(option);
            return content;
        }

        private void BuildMainArea(VBoxContainer body)
        {
            var split = new HSplitContainer
            {
                SplitOffset = 500,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            body.AddChild(split);
            var historyPanel = new PanelContainer { CustomMinimumSize = new(250, 0) };
            historyPanel.AddThemeStyleboxOverride("panel", CardStyle("111A27F4", "32445BE0", 8));
            split.AddChild(historyPanel);
            var historyRoot = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            historyRoot.AddThemeConstantOverride("separation", 7);
            historyPanel.AddChild(historyRoot);
            var historyHeading = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            historyHeading.AddThemeConstantOverride("separation", 8);
            var historyTitle = new Label
            {
                Text = ModLocalization.Get("analysis.history", "Run history"),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            historyTitle.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            historyTitle.AddThemeColorOverride("font_color", new("DCE6F4FF"));
            historyHeading.AddChild(historyTitle);
            _historySummary = new()
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = new("8294AAFF"),
            };
            _historySummary.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            historyHeading.AddChild(_historySummary);
            historyRoot.AddChild(historyHeading);
            _search = new()
            {
                PlaceholderText = ModLocalization.Get("analysis.search",
                    "Search run, encounter, floor, player or mode"),
                ClearButtonEnabled = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            DashboardControlTheme.ApplySearch(_search);
            _search.TextChanged += _ => _searchDelay = SearchDelaySeconds;
            historyRoot.AddChild(_search);
            var historyScroll = new DashboardScrollContainer();
            historyRoot.AddChild(historyScroll);
            _historyRows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _historyRows.AddThemeConstantOverride("separation", 4);
            historyScroll.SetContent(_historyRows);

            var detail = new VBoxContainer
            {
                CustomMinimumSize = new(540, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            detail.AddThemeConstantOverride("separation", 7);
            split.AddChild(detail);
            var selectionHeader = new HBoxContainer { ClipContents = true };
            selectionHeader.AddThemeConstantOverride("separation", 10);
            var selectionIdentity = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            selectionIdentity.AddThemeConstantOverride("separation", -2);
            _selectionTitle = new()
            {
                Text = ModLocalization.Get("analysis.noData", "No analytics data yet"),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _selectionTitle.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SectionTitleFontSize);
            _selectionTitle.AddThemeColorOverride("font_color", new("F3F6FBFF"));
            selectionIdentity.AddChild(_selectionTitle);
            _selectionMeta = new()
            {
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _selectionMeta.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            _selectionMeta.AddThemeColorOverride("font_color", new("93A4BAFF"));
            selectionIdentity.AddChild(_selectionMeta);
            selectionHeader.AddChild(selectionIdentity);
            _deleteRun = new()
            {
                Text = ModLocalization.Get("analysis.deleteRun", "Delete run"),
                CustomMinimumSize = new(104f, 44f),
                FocusMode = FocusModeEnum.None,
                Visible = false,
            };
            DashboardControlTheme.ApplyButton(_deleteRun, DashboardButtonKind.Danger);
            _deleteRun.Pressed += RequestDeleteSelectedRun;
            selectionHeader.AddChild(_deleteRun);
            detail.AddChild(selectionHeader);
            _rendererHost = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            _rendererHost.AddThemeConstantOverride("margin_top", 2);
            detail.AddChild(_rendererHost);
            _status = new()
            {
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _status.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            _status.AddThemeColorOverride("font_color", new("7F91A9FF"));
            detail.AddChild(_status);
        }

        private static PanelContainer HistoryRunGroup(bool selected)
        {
            var group = new PanelContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            group.AddThemeStyleboxOverride("panel", CardStyle(selected ? "102438F2" : "0C141FDC",
                selected ? "4389B8F0" : "26394FC8", 2));
            return group;
        }

        private static Button HistoryRunButton(RunSnapshot run, bool expanded, bool selected, bool active)
        {
            var button = new Button
            {
                CustomMinimumSize = new(0, 50),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None,
                TooltipText = ModLocalization.Format("analysis.runTooltip",
                    "{0:g} · {1} · {2} combats", run.StartedAtUtc.ToLocalTime(), RunPlayers(run),
                    run.Combats.Count),
            };
            DashboardControlTheme.ApplyButton(button,
                selected ? DashboardButtonKind.Primary : DashboardButtonKind.Subtle, true);
            ClearButtonThemePadding(button);
            var margin = ButtonContent(button, 7, 2, 7, 2);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            margin.AddChild(row);
            var chevron = new Label
            {
                Text = expanded ? "▾" : "▸",
                CustomMinimumSize = new(18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chevron.AddThemeFontSizeOverride("font_size", DashboardControlTheme.WindowTitleFontSize);
            chevron.AddThemeColorOverride("font_color", new(selected ? "70C6F4FF" : "8DA1B8FF"));
            row.AddChild(chevron);
            var identity = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            identity.AddThemeConstantOverride("separation", -4);
            var title = new Label
            {
                Text = ModLocalization.Format("analysis.runTitle", "{0:yyyy-MM-dd HH:mm} · {1}",
                    run.StartedAtUtc.ToLocalTime(), RunPlayers(run)),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            title.AddThemeColorOverride("font_color", new("E6EDF7FF"));
            identity.AddChild(title);
            var highestFloor = run.Combats.Count == 0 ? 0 : run.Combats.Max(combat => combat.Floor);
            var summary = new Label
            {
                Text = ModLocalization.Format("analysis.runSummary", "{0} · {1} · {2} combats · floor {3}",
                    RunStatus(run, active), RunMode(run), run.Combats.Count, highestFloor),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            summary.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            summary.AddThemeColorOverride("font_color", RunStatusColor(run, active));
            identity.AddChild(summary);
            row.AddChild(identity);
            return button;
        }

        private static Button HistoryCombatButton(CombatSnapshot combat, int number, bool selected)
        {
            var totalDamage = combat.Players.Sum(player =>
                player.Totals.GetValueOrDefault(MetricIds.DamageDealt));
            var encounter = string.IsNullOrWhiteSpace(combat.EncounterName)
                ? combat.EncounterId
                : combat.EncounterName;
            var button = new Button
            {
                CustomMinimumSize = new(0, 46),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None,
                TooltipText = ModLocalization.Format("analysis.combatTooltip",
                    "Combat {0} · Act {1}, floor {2} · {3} rounds · {4} damage", number,
                    combat.ActIndex + 1, combat.Floor, combat.RoundCount, Format(totalDamage)),
            };
            DashboardControlTheme.ApplyButton(button,
                selected ? DashboardButtonKind.Primary : DashboardButtonKind.Subtle, true);
            ClearButtonThemePadding(button);
            var margin = ButtonContent(button, 7, 1, 7, 1);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            margin.AddChild(row);
            var branch = new ColorRect
            {
                Color = new(selected ? "55B7ECFF" : "334A62D8"),
                CustomMinimumSize = new(3, 0),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(branch);
            var index = new Label
            {
                Text = $"#{number}",
                CustomMinimumSize = new(39, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            index.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
            index.AddThemeColorOverride("font_color", new(selected ? "75C8F2FF" : "8EA3BAFF"));
            row.AddChild(index);
            var identity = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            identity.AddThemeConstantOverride("separation", -4);
            var title = new Label
            {
                Text = encounter,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            title.AddThemeColorOverride("font_color", new("E3EBF5FF"));
            identity.AddChild(title);
            var meta = new Label
            {
                Text = ModLocalization.Format("analysis.combatSummary", "Act {0} · Floor {1} · {2} rounds",
                    combat.ActIndex + 1, combat.Floor, combat.RoundCount),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            meta.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            meta.AddThemeColorOverride("font_color", new("8295ACFF"));
            identity.AddChild(meta);
            row.AddChild(identity);
            var amount = new VBoxContainer
            {
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            amount.AddThemeConstantOverride("separation", -5);
            var value = new Label
            {
                Text = Format(totalDamage),
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            value.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            value.AddThemeColorOverride("font_color", new("F07878FF"));
            amount.AddChild(value);
            var unit = new Label
            {
                Text = ModLocalization.Get("analysis.damageUnit", "damage"),
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            unit.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            unit.AddThemeColorOverride("font_color", new("8193A9FF"));
            amount.AddChild(unit);
            row.AddChild(amount);
            return button;
        }

        private static MarginContainer ButtonContent(Button button, int left, int top, int right, int bottom)
        {
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            button.AddChild(margin);
            margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", left);
            margin.AddThemeConstantOverride("margin_top", top);
            margin.AddThemeConstantOverride("margin_right", right);
            margin.AddThemeConstantOverride("margin_bottom", bottom);
            return margin;
        }

        private static void ClearButtonThemePadding(Button button)
        {
            ClearStyleContentMargins(button, "normal");
            ClearStyleContentMargins(button, "hover");
            ClearStyleContentMargins(button, "pressed");
            ClearStyleContentMargins(button, "focus");
            ClearStyleContentMargins(button, "disabled");
        }

        private static void ClearStyleContentMargins(Button button, string state)
        {
            if (button.GetThemeStylebox(state) is not StyleBoxFlat style)
                return;
            style.ContentMarginLeft = 0f;
            style.ContentMarginTop = 0f;
            style.ContentMarginRight = 0f;
            style.ContentMarginBottom = 0f;
        }

        private static Color RunStatusColor(RunSnapshot run, bool active)
        {
            if (active)
                return new("69BFF0FF");
            if (run.IsVictory == true)
                return new("6FD39AFF");
            if (run.IsVictory == false || run.IsAbandoned == true)
                return new("DA8490FF");
            return new("94A5B9FF");
        }

        private static Button HistoryDeleteButton(bool disabled)
        {
            var button = new Button
            {
                Text = "×",
                CustomMinimumSize = new(42, 44),
                FocusMode = FocusModeEnum.None,
                Disabled = disabled,
                TooltipText = disabled
                    ? ModLocalization.Get("analysis.deleteRun.active",
                        "The current run cannot be deleted while active.")
                    : ModLocalization.Get("analysis.deleteRun", "Delete run"),
            };
            DashboardControlTheme.ApplyIconButton(button, DashboardButtonKind.Danger, compact: true);
            return button;
        }

        private static DashboardDropdown Selector(string tooltip)
        {
            var option = new DashboardDropdown
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new(0, 44),
                TooltipText = tooltip,
            };
            option.ApplyStyle();
            return option;
        }

        private static Button HeaderButton(string text, string tooltip, DashboardButtonKind kind, float width = 38f)
        {
            var button = new Button
            {
                Text = text,
                TooltipText = tooltip,
                CustomMinimumSize = new(width, 38),
                FocusMode = FocusModeEnum.None,
            };
            if (text.Length == 1)
                DashboardControlTheme.ApplyIconButton(button, kind);
            else
                DashboardControlTheme.ApplyButton(button, kind);
            return button;
        }

        private static StyleBoxFlat ShellStyle()
        {
            return new()
            {
                BgColor = new("090E16FC"),
                BorderColor = new("60728AEC"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 7,
                CornerRadiusTopRight = 7,
                CornerRadiusBottomLeft = 7,
                CornerRadiusBottomRight = 7,
                ShadowColor = new(0f, 0f, 0f, 0.72f),
                ShadowSize = 10,
            };
        }

        private static StyleBoxFlat CardStyle(string background, string border, int padding)
        {
            return new()
            {
                BgColor = new(background),
                BorderColor = new(border),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = padding,
                ContentMarginTop = padding,
                ContentMarginRight = padding,
                ContentMarginBottom = padding,
            };
        }

        private void OnViewportSizeChanged()
        {
            if (!Visible)
                return;
            Callable.From(ApplyFullscreenGeometry).CallDeferred();
        }

        private void ApplyFullscreenGeometry()
        {
            if (!IsInsideTree())
                return;
            var viewport = GetViewport().GetVisibleRect();
            Position = viewport.Position;
            Size = viewport.Size;
        }
    }
}
