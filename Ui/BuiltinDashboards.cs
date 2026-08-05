// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal static class BuiltinDashboardCatalog
    {
        internal static void Register(DashboardRegistry registry)
        {
            registry.RegisterStyle(new()
            {
                Id = "ritsumetrics.dark",
                Name = "Slate",
                BackgroundColor = "0A0E15EE",
                HeaderColor = "111722F5",
                SurfaceColor = "17202DE6",
                TrackColor = "05080DED",
                BorderColor = "657187E0",
                TextColor = "F0F3F8FF",
                SecondaryTextColor = "9FAABEFF",
                PositiveColor = "5BD27FFF",
                NegativeColor = "FF5968FF",
                WarningColor = "E8C65AFF",
                AccentColors =
                    ["E25364FF", "42A8E8FF", "62C878FF", "E3AC45FF", "A977DEFF", "42C9C2FF"],
            }, true);
            registry.RegisterStyle(new()
            {
                Id = "ritsumetrics.compact",
                Name = "Compact",
                BackgroundColor = "0B0E14F2",
                HeaderColor = "101923FA",
                SurfaceColor = "121B24E8",
                TrackColor = "070A0FF0",
                BorderColor = "568794F2",
                RowHeight = 34,
                FontSize = 15,
            }, true);
            registry.RegisterStyle(new()
            {
                Id = "ritsumetrics.glass",
                Name = "Glass",
                BackgroundColor = "101725B8",
                HeaderColor = "19243DD9",
                SurfaceColor = "17243BB8",
                TrackColor = "08101EC7",
                BorderColor = "79A7D8D9",
                SecondaryTextColor = "C2D2E8FF",
            }, true);

            Register(registry, BuiltInDashboardIds.Overview, "dashboard.overview", "Multidimensional overview",
                "Player KPIs, offense, defense, resources, attribution and turn trends", 720f, 680f,
                () => new OverviewRenderer());
            Register(registry, BuiltInDashboardIds.Meter, "dashboard.meter", "Metric meter",
                "Per-player meter with source breakdown", 400f, 360f, () => new MetricMeterRenderer());
            Register(registry, BuiltInDashboardIds.DamageContribution, "dashboard.damageContribution",
                "Responsibility damage (RD)",
                "Applied HP + Block damage redistributed among base damage and positive modifiers", 400f,
                360f, () => new MetricMeterRenderer(MetricIds.DamageContribution));
            Register(registry, BuiltInDashboardIds.EffectiveHpDamageContribution,
                "dashboard.effectiveHpDamageContribution", "HP attribution (HP-RD)",
                "Applied HP damage redistributed among base damage and positive modifiers", 400f, 360f,
                () => new MetricMeterRenderer(MetricIds.EffectiveHpDamageContribution));
            Register(registry, BuiltInDashboardIds.DefenseContribution, "dashboard.defenseContribution",
                "Defense contribution", "Effective mitigation, consumed block and healing by contributor", 400f,
                360f, () => new MetricMeterRenderer(MetricIds.DefenseContribution));
            Register(registry, BuiltInDashboardIds.CardLog, "dashboard.cardLog", "Card and effect log",
                "Cards, damage, block, powers and executions", 620f, 620f, () => new CardLogRenderer());
            Register(registry, BuiltInDashboardIds.ReceivedDamage, "dashboard.received", "Received damage",
                "Damage, block, deaths and incoming event log", 500f, 560f, () => new ReceivedDamageRenderer());
            Register(registry, BuiltInDashboardIds.Timeline, "dashboard.timeline", "Causal timeline",
                "Detailed turn and causal event reconstruction", 720f, 680f, () => new TimelineRenderer());
            Register(registry, BuiltInDashboardIds.DamageBreakdown, "dashboard.damageBreakdown", "Damage analysis",
                "Modifier waterfall and effective attribution", 650f, 640f, () => new DamageBreakdownRenderer());
            Register(registry, BuiltInDashboardIds.PlayerPerformance, "dashboard.playerPerformance",
                "Player performance", "Detailed player output, survival and action efficiency", 720f, 640f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.PlayerPerformance));
            Register(registry, BuiltInDashboardIds.SourceAnalysis, "dashboard.sourceAnalysis", "Source analysis",
                "Cross-metric drill-down by card, power, relic and effect", 720f, 680f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.SourceAnalysis));
            Register(registry, BuiltInDashboardIds.DefenseResources, "dashboard.defenseResources",
                "Defense and resources", "Damage intake, prevention, healing, energy and card flow", 720f, 660f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.DefenseResources));
            Register(registry, BuiltInDashboardIds.CardsAndEffects, "dashboard.cardsEffects", "Cards and effects",
                "Card efficiency and non-card effect contribution", 760f, 700f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.CardsAndEffects));
            Register(registry, BuiltInDashboardIds.ContributionAnalysis, "dashboard.contributionAnalysis",
                "Contribution analysis", "Direct, assisted, amplified, mitigated and execution contribution", 760f,
                700f, () => new AdvancedDashboardRenderer(AdvancedDashboardMode.ContributionAnalysis));
            Register(registry, BuiltInDashboardIds.TurnAnalysis, "dashboard.turnAnalysis", "Turn performance",
                "Per-turn output, defense, actions and extra-turn analysis", 760f, 700f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.TurnAnalysis));
            Register(registry, BuiltInDashboardIds.RunTrends, "dashboard.runTrends", "Run trends",
                "Combat-by-combat performance and efficiency trends", 760f, 680f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.RunTrends));
            Register(registry, BuiltInDashboardIds.CombatRecords, "dashboard.combatRecords", "Combat records",
                "Peak hits, turns, combats, modifiers and card records", 700f, 620f,
                () => new AdvancedDashboardRenderer(AdvancedDashboardMode.CombatRecords));
        }

        private static void Register(
            DashboardRegistry registry,
            string id,
            string localizationKey,
            string title,
            string description,
            float width,
            float height,
            Func<IDashboardRenderer> factory)
        {
            registry.RegisterDashboard(new BuiltinProvider(new()
            {
                Id = id,
                TitleLocalizationKey = localizationKey,
                FallbackTitle = title,
                DescriptionLocalizationKey = localizationKey + ".description",
                FallbackDescription = description,
                DefaultStyleId = "ritsumetrics.compact",
                DefaultWidth = width,
                DefaultHeight = height,
            }, factory), true);
        }

        private sealed class BuiltinProvider(
            DashboardDefinition definition,
            Func<IDashboardRenderer> factory) : IDashboardProvider
        {
            public DashboardDefinition Definition { get; } = definition;

            public IDashboardRenderer CreateRenderer()
            {
                return factory();
            }
        }
    }

    public abstract class DashboardRendererBase : IDashboardRenderer, IDashboardRendererPresentation,
        IDashboardRendererFooterPresentation, IDashboardDataConsumer
    {
        private const float FooterSeparation = 8f;
        private const float MinimumOverflowContextWidth = 48f;
        private readonly HBoxContainer _footer;
        private readonly Label _footerContext;
        private readonly Button _pageNext = new() { Text = "›" };
        private readonly Button _pagePrevious = new() { Text = "‹" };
        private readonly Label _pageStatus = new();
        private readonly Dictionary<string, ReconciledRowState> _reconciledRows = new(StringComparer.Ordinal);
        private readonly VariableRowHeightCache _variableRowHeights = new();
        private RendererChromeState? _chromeState;
        private string _compactFooterContext = string.Empty;
        private string _fullFooterContext = string.Empty;
        private int _pageIndex;
        private string _pageKey = string.Empty;
        private Action? _pageRefresh;
        private bool _pagingControlsAdded;
        private Action? _scopeToggle;
        private bool _variableMeasurementPending;
        private VariableReconciledRow[]? _variableVirtualRows;
        private float _virtualRowExtent;
        private ReconciledRow[]? _virtualRows;
        private string[] _visibleVariableRowKeys = [];

        protected DashboardRendererBase()
        {
            View = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ClipContents = true,
            };
            View.AddThemeConstantOverride("separation", 6);
            Toolbar = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Toolbar.AddThemeConstantOverride("separation", 6);
            View.AddChild(Toolbar);
            Scroll = new();
            Scroll.VisibleRangeChanged += ApplyVirtualizedRows;
            Rows = new()
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            Rows.AddThemeConstantOverride("separation", 4);
            Scroll.SetContent(Rows);
            View.AddChild(Scroll);
            _footer = new() { ClipContents = true };
            _footer.AddThemeConstantOverride("separation", (int)FooterSeparation);
            _footer.Resized += UpdateFooterLayout;
            Status = new()
            {
                AutowrapMode = TextServer.AutowrapMode.Off,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _footer.AddChild(Status);
            _footerContext = new()
            {
                Visible = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Right,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            _footerContext.GuiInput += input =>
            {
                if (input is not InputEventMouseButton
                    {
                        Pressed: true,
                        ButtonIndex: MouseButton.Left or MouseButton.Right,
                    })
                    return;
                _scopeToggle?.Invoke();
                _footerContext.AcceptEvent();
            };
            _footer.AddChild(_footerContext);
            View.AddChild(_footer);
        }

        protected HBoxContainer Toolbar { get; }
        protected DashboardScrollContainer Scroll { get; }
        protected VBoxContainer Rows { get; }
        protected Label Status { get; }
        public string? CompactSubtitle { get; protected set; }

        protected virtual bool ReconcileRowsOnRefresh => false;

        public virtual DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            return DashboardDataRequirements.All;
        }

        public Control View { get; }

        public void Refresh(DashboardRenderContext context)
        {
            if (!ReconcileRowsOnRefresh)
                Clear(Rows);
            var singleLine = DashboardPresentation.SingleLine(context.Parameters);
            var chromeState = new RendererChromeState(
                singleLine,
                Math.Max(11, context.Style.FontSize - 1),
                ColorOf(context.Style.SecondaryTextColor));
            if (_chromeState != chromeState)
            {
                View.AddThemeConstantOverride("separation", singleLine ? 3 : 6);
                Toolbar.AddThemeConstantOverride("separation", singleLine ? 3 : 6);
                Rows.AddThemeConstantOverride("separation", singleLine ? 2 : 4);
                Status.AddThemeFontSizeOverride("font_size", chromeState.FooterFontSize);
                Status.Modulate = chromeState.SecondaryTextColor;
                _footerContext.AddThemeFontSizeOverride("font_size", chromeState.FooterFontSize);
                _footerContext.Modulate = chromeState.SecondaryTextColor;
                _chromeState = chromeState;
            }

            CompactSubtitle = null;
            Render(context);
            var components = GetDataRequirements(context.Scope, context.Parameters).Components;
            var droppedObservations = components.HasFlag(DashboardDataComponents.Events)
                ? context.Snapshot?.DroppedObservationCount ?? 0
                : 0;
            var droppedTimelineEvents = components.HasFlag(DashboardDataComponents.Timeline)
                ? context.Snapshot?.DroppedTimelineEventCount ?? 0
                : 0;
            if (droppedObservations > 0 || droppedTimelineEvents > 0)
            {
                var notice = ModLocalization.Get("analysis.incompleteDetails", "Detailed data is incomplete");
                Status.Text = string.IsNullOrWhiteSpace(Status.Text) ? notice : $"{Status.Text} · {notice}";
                Status.TooltipText = ModLocalization.Format("analysis.incompleteDetails.hint",
                    "Capture limits omitted {0} metric observations and {1} timeline events. Aggregate totals remain complete.",
                    droppedObservations, droppedTimelineEvents);
                Status.Modulate = ColorOf(context.Style.WarningColor);
            }
            else
            {
                Status.TooltipText = string.Empty;
                Status.Modulate = ColorOf(context.Style.SecondaryTextColor);
            }
            Rows.CustomMinimumSize = new(Rows.CustomMinimumSize.X, 0f);
            Scroll.InvalidateContentSize();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void SetFooterContext(string? text)
        {
            SetFooterContext(text, null);
        }

        public string? Title { get; protected set; }
        public string? Subtitle { get; protected set; }
        public string? AccentColor { get; protected set; }

        protected void ReconcileRows(IEnumerable<ReconciledRow> rows)
        {
            _virtualRows = null;
            _variableVirtualRows = null;
            ReconcileRowsCore(rows);
        }

        protected void ReconcileVirtualRows(IEnumerable<ReconciledRow> rows, float rowExtent)
        {
            _virtualRows = rows.ToArray();
            _variableVirtualRows = null;
            _virtualRowExtent = Math.Max(1f, rowExtent);
            ApplyVirtualRows();
        }

        protected void ReconcileVariableVirtualRows(IEnumerable<VariableReconciledRow> rows)
        {
            _virtualRows = null;
            _variableVirtualRows = rows.ToArray();
            var current = _variableVirtualRows.Select(item => (item.Row.Key, item.Row.Fingerprint)).ToHashSet();
            _variableRowHeights.Retain(current);
            ApplyVariableVirtualRows();
        }

        protected IReadOnlyList<T> PageItems<T>(
            IReadOnlyList<T> items,
            DashboardRenderContext context,
            int floatingWindowLimit,
            int pageSize = 20)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(floatingWindowLimit);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
            if (!DashboardPresentation.ShowsCompleteHistory(context.Parameters))
            {
                HidePagingControls();
                return items.Take(floatingWindowLimit).ToArray();
            }

            var pageKey = string.Join('\u001f',
                context.Scope,
                context.Snapshot?.RunId,
                context.Snapshot?.CombatId);
            if (!string.Equals(_pageKey, pageKey, StringComparison.Ordinal))
            {
                _pageKey = pageKey;
                _pageIndex = 0;
            }

            var pageCount = Math.Max(1, (items.Count + pageSize - 1) / pageSize);
            _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
            if (pageCount <= 1)
            {
                HidePagingControls();
                return items;
            }

            EnsurePagingControls(context.Style);
            _pageRefresh = () => Refresh(context);
            _pagePrevious.Disabled = _pageIndex == 0;
            _pageNext.Disabled = _pageIndex == pageCount - 1;
            _pageStatus.Text = $"{_pageIndex + 1}/{pageCount} · {items.Count}";
            _pagePrevious.Visible = true;
            _pageStatus.Visible = true;
            _pageNext.Visible = true;
            return items.Skip(_pageIndex * pageSize).Take(pageSize).ToArray();
        }

        private void EnsurePagingControls(DashboardStyleDefinition style)
        {
            if (_pagingControlsAdded)
                return;
            _pagingControlsAdded = true;
            _pagePrevious.CustomMinimumSize = new(38f, 0f);
            _pageNext.CustomMinimumSize = new(38f, 0f);
            DashboardControlTheme.ApplyButton(_pagePrevious, DashboardButtonKind.Subtle, true, style);
            DashboardControlTheme.ApplyButton(_pageNext, DashboardButtonKind.Subtle, true, style);
            _pageStatus.CustomMinimumSize = new(82f, 0f);
            _pageStatus.HorizontalAlignment = HorizontalAlignment.Center;
            _pageStatus.VerticalAlignment = VerticalAlignment.Center;
            _pageStatus.Modulate = ColorOf(style.SecondaryTextColor);
            _pagePrevious.Pressed += () =>
            {
                if (_pageIndex <= 0)
                    return;
                _pageIndex--;
                _pageRefresh?.Invoke();
            };
            _pageNext.Pressed += () =>
            {
                _pageIndex++;
                _pageRefresh?.Invoke();
            };
            Toolbar.AddChild(_pagePrevious);
            Toolbar.AddChild(_pageStatus);
            Toolbar.AddChild(_pageNext);
        }

        protected void HidePagingControls()
        {
            _pageRefresh = null;
            if (!_pagingControlsAdded)
                return;
            _pagePrevious.Visible = false;
            _pageStatus.Visible = false;
            _pageNext.Visible = false;
        }

        private void ApplyVirtualizedRows()
        {
            ApplyVirtualRows();
            ApplyVariableVirtualRows();
        }

        private void ApplyVirtualRows()
        {
            if (_virtualRows == null)
                return;
            var range = VirtualListRange.Calculate(
                _virtualRows.Length,
                Scroll.ScrollVertical,
                Scroll.Size.Y,
                _virtualRowExtent);
            var separation = _chromeState?.SingleLine == true ? 2f : 4f;
            var topHeight = Math.Max(0f, range.Start * _virtualRowExtent - separation);
            var bottomHeight = Math.Max(0f,
                (_virtualRows.Length - range.End) * _virtualRowExtent - separation);
            var visible = new List<ReconciledRow>(range.Count + 2)
            {
                Spacer("__virtual_top", topHeight),
            };
            for (var index = range.Start; index < range.End; index++)
                visible.Add(_virtualRows[index]);
            visible.Add(Spacer("__virtual_bottom", bottomHeight));
            ReconcileRowsCore(visible);
            return;

            static ReconciledRow Spacer(string key, float height)
            {
                return new(key, "spacer", () => new()
                {
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                },
                    control =>
                    {
                        var visible = height > 0.5f;
                        if (control.Visible != visible)
                            control.Visible = visible;
                        var size = new Vector2(0f, visible ? height : 0f);
                        if (control.CustomMinimumSize != size)
                            control.CustomMinimumSize = size;
                    });
            }
        }

        private void ApplyVariableVirtualRows()
        {
            if (_variableVirtualRows == null)
                return;
            var separation = _chromeState?.SingleLine == true ? 2f : 4f;
            var widthBucket = VariableRowHeightCache.WidthBucket(Rows.Size.X);
            var offsets = new float[_variableVirtualRows.Length + 1];
            for (var index = 0; index < _variableVirtualRows.Length; index++)
            {
                var descriptor = _variableVirtualRows[index];
                var height = _variableRowHeights.Resolve(
                    descriptor.Row.Key,
                    descriptor.Row.Fingerprint,
                    widthBucket,
                    descriptor.EstimatedHeight);
                offsets[index + 1] = offsets[index] + Math.Max(1f, height) + separation;
            }

            var range = VariableRange(offsets, Scroll.ScrollVertical, Scroll.Size.Y, 240f);
            var visible = new List<ReconciledRow>(range.Count + 2)
            {
                VariableSpacer("__virtual_top", Math.Max(0f, offsets[range.Start] - separation)),
            };
            for (var index = range.Start; index < range.End; index++)
                visible.Add(_variableVirtualRows[index].Row);
            visible.Add(VariableSpacer("__virtual_bottom",
                Math.Max(0f, offsets[^1] - offsets[range.End] - separation)));
            _visibleVariableRowKeys = _variableVirtualRows[range.Start..range.End]
                .Select(item => item.Row.Key).ToArray();
            ReconcileRowsCore(visible);
            ScheduleVariableRowMeasurement();
        }

        private void ScheduleVariableRowMeasurement()
        {
            if (_variableMeasurementPending || !View.IsInsideTree())
                return;
            var widthBucket = VariableRowHeightCache.WidthBucket(Rows.Size.X);
            var descriptors = _variableVirtualRows?.ToDictionary(item => item.Row.Key, StringComparer.Ordinal);
            if (descriptors == null || !_visibleVariableRowKeys.Any(key =>
                    descriptors.TryGetValue(key, out var descriptor) &&
                    _variableRowHeights.NeedsMeasurement(
                        key,
                        descriptor.Row.Fingerprint,
                        widthBucket)))
                return;
            _variableMeasurementPending = true;
            Callable.From(() => { Callable.From(() => MeasureVariableRows(widthBucket)).CallDeferred(); })
                .CallDeferred();
        }

        private void MeasureVariableRows(int widthBucket)
        {
            _variableMeasurementPending = false;
            if (_variableVirtualRows == null || !View.IsInsideTree())
                return;
            var changed = false;
            var measuredAny = false;
            var descriptors = _variableVirtualRows.ToDictionary(item => item.Row.Key, StringComparer.Ordinal);
            foreach (var key in _visibleVariableRowKeys)
            {
                if (!descriptors.TryGetValue(key, out var descriptor) ||
                    !_reconciledRows.TryGetValue(key, out var state) ||
                    !GodotObject.IsInstanceValid(state.Control) ||
                    !_variableRowHeights.NeedsMeasurement(key, descriptor.Row.Fingerprint, widthBucket))
                    continue;
                var measured = Math.Max(1f, state.Control.GetCombinedMinimumSize().Y);
                measuredAny = true;
                changed |= _variableRowHeights.Record(key, descriptor.Row.Fingerprint, widthBucket, measured);
            }

            if (changed)
                ApplyVariableVirtualRows();
            else if (measuredAny)
                ScheduleVariableRowMeasurement();
        }

        private static VirtualListRange VariableRange(
            float[] offsets,
            float scrollOffset,
            float viewportHeight,
            float overscan)
        {
            var itemCount = offsets.Length - 1;
            if (itemCount <= 0)
                return new(0, 0);
            var viewport = viewportHeight > 0f ? viewportHeight : 480f;
            var startOffset = Math.Max(0f, scrollOffset - overscan);
            var endOffset = Math.Max(startOffset, scrollOffset + viewport + overscan);
            var start = Math.Max(0, UpperBound(offsets, startOffset) - 1);
            var end = Math.Clamp(UpperBound(offsets, endOffset), start + 1, itemCount);
            return new(start, end);
        }

        private static int UpperBound(float[] values, float target)
        {
            var low = 0;
            var high = values.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (values[middle] <= target)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private static ReconciledRow VariableSpacer(string key, float height)
        {
            return new(key, "spacer", () => new()
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
                control =>
                {
                    var visible = height > 0.5f;
                    if (control.Visible != visible)
                        control.Visible = visible;
                    var size = new Vector2(0f, visible ? height : 0f);
                    if (control.CustomMinimumSize != size)
                        control.CustomMinimumSize = size;
                });
        }

        private void ReconcileRowsCore(IEnumerable<ReconciledRow> rows)
        {
            var requested = rows.ToArray();
            var plan = KeyedReconciliation.Plan(
                _reconciledRows.ToDictionary(item => item.Key, item => item.Value.Fingerprint,
                    StringComparer.Ordinal),
                requested.Select(row => new ReconciliationItem(row.Key, row.Fingerprint)).ToArray());
            var requestedByKey = requested.ToDictionary(row => row.Key, StringComparer.Ordinal);
            var next = new Dictionary<string, ReconciledRowState>(StringComparer.Ordinal);
            foreach (var decision in plan.Decisions)
            {
                var row = requestedByKey[decision.Key];
                var hasPrevious = _reconciledRows.TryGetValue(row.Key, out var previous);
                Control control;
                if (decision.Reuse && hasPrevious &&
                    GodotObject.IsInstanceValid(previous.Control))
                {
                    control = previous.Control;
                }
                else
                {
                    if (hasPrevious && GodotObject.IsInstanceValid(previous.Control))
                    {
                        Rows.RemoveChild(previous.Control);
                        previous.Control.QueueFree();
                    }

                    control = row.Create();
                    Rows.AddChild(control);
                }

                if (control.GetIndex() != decision.Index)
                    Rows.MoveChild(control, decision.Index);
                row.Update?.Invoke(control);
                next.Add(row.Key, new(row.Fingerprint, control));
            }

            foreach (var key in plan.RemovedKeys)
            {
                var stale = _reconciledRows[key];
                if (!GodotObject.IsInstanceValid(stale.Control))
                    continue;
                Rows.RemoveChild(stale.Control);
                stale.Control.QueueFree();
            }

            _reconciledRows.Clear();
            foreach (var (key, state) in next)
                _reconciledRows.Add(key, state);
        }

        internal void SetScopeToggle(Action toggle)
        {
            _scopeToggle = toggle;
            _footerContext.TooltipText = ModLocalization.Get("dashboard.toggleScope",
                "Left- or right-click to switch between current combat and current run");
        }

        public void SetFooterContext(string? text, string? compactText)
        {
            var full = text ?? string.Empty;
            var compact = compactText ?? string.Empty;
            if (string.Equals(_fullFooterContext, full, StringComparison.Ordinal) &&
                string.Equals(_compactFooterContext, compact, StringComparison.Ordinal))
                return;

            _fullFooterContext = full;
            _compactFooterContext = compact;
            _footerContext.Visible = !string.IsNullOrWhiteSpace(_fullFooterContext);
            UpdateFooterLayout();
        }

        private void UpdateFooterLayout()
        {
            if (!_footerContext.Visible)
            {
                Status.SizeFlagsStretchRatio = 1f;
                return;
            }

            var availableWidth = Math.Max(0f, _footer.Size.X - FooterSeparation);
            var statusWidth = TextWidth(Status, Status.Text);
            var fullContextWidth = TextWidth(_footerContext, _fullFooterContext);
            var useCompact = !string.IsNullOrWhiteSpace(_compactFooterContext) &&
                             statusWidth + fullContextWidth > availableWidth;
            _footerContext.Text = useCompact ? _compactFooterContext : _fullFooterContext;
            var contextWidth = TextWidth(_footerContext, _footerContext.Text);
            if (statusWidth + contextWidth <= availableWidth)
            {
                Status.SizeFlagsStretchRatio = Math.Max(1f, statusWidth);
                _footerContext.SizeFlagsStretchRatio = Math.Max(1f, contextWidth);
                return;
            }

            var contextAllocation = Math.Min(contextWidth,
                Math.Max(MinimumOverflowContextWidth, availableWidth - statusWidth));
            contextAllocation = Math.Min(contextAllocation, availableWidth);
            Status.SizeFlagsStretchRatio = Math.Max(1f, availableWidth - contextAllocation);
            _footerContext.SizeFlagsStretchRatio = Math.Max(1f, contextAllocation);
        }

        private static float TextWidth(Label label, string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            return label.GetThemeFont("font").GetStringSize(text,
                fontSize: label.GetThemeFontSize("font_size")).X;
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        protected abstract void Render(DashboardRenderContext context);

        protected static Label Label(
            string text,
            DashboardStyleDefinition style,
            bool secondary = false,
            int? fontSize = null)
        {
            var label = new Label
            {
                Text = text,
                Modulate = ColorOf(secondary ? style.SecondaryTextColor : style.TextColor),
                AutowrapMode = TextServer.AutowrapMode.Off,
            };
            label.AddThemeFontSizeOverride("font_size",
                Math.Max(10, fontSize ?? style.FontSize));
            return label;
        }

        protected static Label TruncatedLabel(
            string text,
            DashboardStyleDefinition style,
            bool secondary = false,
            int? fontSize = null)
        {
            var label = Label(text, style, secondary, fontSize);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.ClipText = true;
            label.TooltipText = text;
            return label;
        }

        protected static Label WrappedLabel(
            string text,
            DashboardStyleDefinition style,
            bool secondary = false,
            int? fontSize = null)
        {
            var label = new Label
            {
                Text = text,
                Modulate = ColorOf(secondary ? style.SecondaryTextColor : style.TextColor),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            label.AddThemeFontSizeOverride("font_size",
                Math.Max(10, fontSize ?? style.FontSize));
            return label;
        }

        protected static string Format(decimal value)
        {
            return value == decimal.Truncate(value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N1", CultureInfo.CurrentCulture);
        }

        public static Color ColorOf(string value)
        {
            try
            {
                return new(value);
            }
            catch
            {
                return Colors.White;
            }
        }

        protected static string VisualStyleFingerprint(DashboardStyleDefinition style)
        {
            return string.Join(':',
                style.Id,
                style.FontSize,
                style.RowHeight,
                style.TextColor,
                style.SecondaryTextColor,
                style.SurfaceColor,
                style.TrackColor,
                style.BorderColor,
                style.PositiveColor,
                style.NegativeColor,
                style.WarningColor,
                string.Join(',', style.AccentColors));
        }

        protected static string EmptyRowFingerprint(DashboardRenderContext context, string text = "No combat data")
        {
            return string.Join(':',
                VisualStyleFingerprint(context.Style),
                ModLocalization.Get("dashboard.noData", text),
                ModLocalization.Get("dashboard.waiting", "Live data will appear when combat begins"));
        }

        protected static void Clear(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }

        protected static GridContainer ResponsiveGrid(
            int maximumColumns,
            float minimumColumnWidth,
            int horizontalSeparation = 8,
            int verticalSeparation = 8)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumColumns, 1);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumColumnWidth, 0f);
            var grid = new GridContainer
            {
                Columns = 1,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            grid.AddThemeConstantOverride("h_separation", horizontalSeparation);
            grid.AddThemeConstantOverride("v_separation", verticalSeparation);
            grid.Resized += UpdateColumns;
            grid.Ready += UpdateColumns;
            return grid;

            void UpdateColumns()
            {
                if (!GodotObject.IsInstanceValid(grid) || grid.Size.X <= 0f)
                    return;
                var availableColumns = (int)Math.Floor(
                    (grid.Size.X + horizontalSeparation) / (minimumColumnWidth + horizontalSeparation));
                var childLimit = Math.Max(1, grid.GetChildCount());
                var columns = Math.Clamp(availableColumns, 1, Math.Min(maximumColumns, childLimit));
                if (grid.Columns != columns)
                    grid.Columns = columns;
            }
        }

        protected static IReadOnlyList<CombatTimelineEvent> Timeline(CombatSnapshot? snapshot)
        {
            return snapshot?.Timeline ?? [];
        }

        protected static decimal Metric(PlayerMetricSnapshot player, string id)
        {
            return player.Totals.GetValueOrDefault(id);
        }

        protected static decimal MetricForDisplay(PlayerMetricSnapshot player, string id)
        {
            if (player.Totals.TryGetValue(id, out var value))
                return value;
            return id switch
            {
                MetricIds.DamageContribution => Metric(player, MetricIds.DamageDealt),
                MetricIds.EffectiveHpDamageDealt => Metric(player, MetricIds.DamageDealt),
                MetricIds.EffectiveHpDamageContribution => player.Totals.TryGetValue(
                    MetricIds.EffectiveHpDamageDealt, out var hpDamage)
                    ? hpDamage
                    : Metric(player, MetricIds.DamageDealt),
                _ => 0m,
            };
        }

        protected static IReadOnlyDictionary<string, string> PlayerAccents(
            IEnumerable<PlayerMetricSnapshot> players,
            DashboardStyleDefinition style)
        {
            return PlayerColorPalette.Resolve(
                players.Select(player => new PlayerColorIdentity(
                    player.PlayerKey,
                    player.PlayerNetId,
                    player.CharacterId,
                    player.IdentityColor)),
                style,
                CharacterPortraitCache.GetNameColor);
        }

        protected static void ApplyIdentityColor(Label label, string color)
        {
            label.AddThemeColorOverride("font_color", ColorOf(color));
            label.AddThemeConstantOverride("outline_size", 0);
        }

        protected static IReadOnlyList<SourceMetricSnapshot> MetricSourcesForDisplay(
            PlayerMetricSnapshot player,
            string id)
        {
            if (player.Sources.TryGetValue(id, out var sources))
                return sources;
            var fallbackId = id switch
            {
                MetricIds.DamageContribution => MetricIds.DamageDealt,
                MetricIds.EffectiveHpDamageDealt => MetricIds.DamageDealt,
                MetricIds.EffectiveHpDamageContribution when player.Sources.ContainsKey(
                    MetricIds.EffectiveHpDamageDealt) => MetricIds.EffectiveHpDamageDealt,
                MetricIds.EffectiveHpDamageContribution => MetricIds.DamageDealt,
                _ => string.Empty,
            };
            return string.IsNullOrEmpty(fallbackId)
                ? []
                : player.Sources.GetValueOrDefault(fallbackId) ?? [];
        }

        protected static SourceMetricSnapshot PresentSource(
            PlayerMetricSnapshot player,
            SourceMetricSnapshot source)
        {
            if (source.SourceKind != AnalyticsSourceKind.Creature ||
                !string.Equals(player.CharacterId, "DEFECT", StringComparison.OrdinalIgnoreCase) ||
                (!string.Equals(source.ModelId, player.CharacterId, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(source.DisplayName, player.DisplayName, StringComparison.CurrentCulture)))
                return source;
            return source with
            {
                SourceKey = $"legacy-orb:{player.PlayerKey}",
                SourceKind = AnalyticsSourceKind.Orb,
                ModelId = "LEGACY_UNKNOWN_ORB",
                DisplayName = ModLocalization.Get("source.legacyUnknownOrb", "Unidentified orb (legacy record)"),
            };
        }

        protected static string Accent(DashboardStyleDefinition style, int index)
        {
            return style.AccentColors is not { Count: > 0 }
                ? style.PositiveColor
                : style.AccentColors[Math.Abs(index) % style.AccentColors.Count];
        }

        public static string AccentAt(DashboardStyleDefinition style, int index)
        {
            return Accent(style, index);
        }

        protected static ProgressBar Meter(
            string text,
            decimal value,
            decimal maximum,
            string color,
            DashboardStyleDefinition style,
            int? height = null)
        {
            var bar = new ProgressBar
            {
                CustomMinimumSize = new(0, Math.Max(24, height ?? style.RowHeight)),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ShowPercentage = false,
                MinValue = 0d,
                MaxValue = Math.Max(1d, (double)maximum),
                Value = (double)Math.Max(0m, value),
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
            {
                BgColor = ColorOf(color) with { A = 0.94f },
                BorderColor = ColorOf(color),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
            });
            bar.AddThemeStyleboxOverride("background", new StyleBoxFlat
            {
                BgColor = ColorOf(style.TrackColor),
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
            });
            bar.AddChild(MeterTextBackdrop());
            if (!string.IsNullOrEmpty(text))
            {
                var label = Label(text, style);
                ApplyMeterTextReadability(label);
                label.MouseFilter = Control.MouseFilterEnum.Ignore;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.ClipText = true;
                label.LayoutMode = 1;
                label.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
                label.OffsetLeft = 8f;
                label.OffsetRight = -8f;
                label.VerticalAlignment = VerticalAlignment.Center;
                bar.AddChild(label);
            }

            DashboardTooltip.SetValue(bar, text, value, maximum);
            return bar;
        }

        private static ColorRect MeterTextBackdrop()
        {
            return new()
            {
                Color = ColorOf(PlayerColorPalette.MeterTextBackdropColor),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                LayoutMode = 1,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            };
        }

        protected static Control Meter(
            string labelText,
            string valueText,
            decimal value,
            decimal maximum,
            string color,
            DashboardStyleDefinition style,
            int? height = null)
        {
            return CreateLabeledMeter(labelText, valueText, value, maximum, color, style, height).Root;
        }

        private static LabeledMeter CreateLabeledMeter(
            string labelText,
            string valueText,
            decimal value,
            decimal maximum,
            string color,
            DashboardStyleDefinition style,
            int? height = null)
        {
            var bar = Meter(string.Empty, value, maximum, color, style, height);
            var labels = new HBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                LayoutMode = 1,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
                OffsetLeft = 9f,
                OffsetRight = -9f,
            };
            var name = TruncatedLabel(labelText, style);
            ApplyMeterTextReadability(name);
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            labels.AddChild(name);
            var amount = Label(valueText, style, false, style.FontSize + 1);
            ApplyMeterTextReadability(amount);
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            amount.CustomMinimumSize = new(72, 0);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            labels.AddChild(amount);
            bar.AddChild(labels);
            DashboardTooltip.SetValue(bar, labelText, value, maximum, valueText);
            return new(bar, name, amount);
        }

        private static void ApplyMeterTextReadability(Label label)
        {
            const string textColor = "FFFFFFFF";
            label.Modulate = Colors.White;
            label.AddThemeColorOverride("font_color", ColorOf(textColor));
            label.AddThemeColorOverride("font_outline_color",
                ColorOf(PlayerColorPalette.ReadableTextOutline(textColor)));
            label.AddThemeConstantOverride("outline_size", 1);
        }

        protected static Control Surface(
            Control content,
            DashboardStyleDefinition style,
            string? accent = null,
            int padding = 8)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var background = ColorOf(style.SurfaceColor);
            background.A = Math.Min(background.A, 0.72f);
            var styleBox = new StyleBoxFlat
            {
                BgColor = background,
                BorderColor = ColorOf(accent ?? style.BorderColor) with { A = accent == null ? 0.22f : 0.42f },
                BorderWidthLeft = accent == null ? 0 : 2,
                BorderWidthBottom = accent == null ? 1 : 0,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                ContentMarginLeft = padding,
                ContentMarginTop = padding,
                ContentMarginRight = padding,
                ContentMarginBottom = padding,
            };
            panel.AddThemeStyleboxOverride("panel", styleBox);
            panel.AddChild(content);
            return panel;
        }

        protected static Control Badge(
            string text,
            string color,
            DashboardStyleDefinition style,
            int width = 50,
            bool constrainWidth = false)
        {
            var badge = new PanelContainer
            {
                CustomMinimumSize = new(width, 0),
                SizeFlagsHorizontal = constrainWidth
                    ? Control.SizeFlags.ShrinkBegin
                    : Control.SizeFlags.Fill,
            };
            badge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = ColorOf(color) with { A = 0.22f },
                BorderColor = ColorOf(color) with { A = 0.75f },
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
            });
            var label = Label(text, style, false, Math.Max(9, style.FontSize - 2));
            if (constrainWidth)
            {
                label.ClipText = true;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.TooltipText = text;
            }

            label.Modulate = ColorOf(color);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            badge.AddChild(label);
            return badge;
        }

        protected static Control PlayerHeader(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            string accent,
            DashboardStyleDefinition style)
        {
            return PlayerHeader(player, rank, value, total, accent, style, false);
        }

        protected static Control PlayerHeader(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            string accent,
            DashboardStyleDefinition style,
            bool singleLine)
        {
            if (singleLine)
                return SingleLinePlayerHeader(player, rank, value, total, accent, style);

            var row = new HBoxContainer { CustomMinimumSize = new(0, 48) };
            DashboardTooltip.SetValue(row, player.DisplayName, value, total, player.CharacterId);
            row.AddThemeConstantOverride("separation", 7);
            if (rank > 0)
            {
                var rankLabel = Label(rank.ToString(CultureInfo.CurrentCulture), style, false, style.FontSize + 2);
                rankLabel.CustomMinimumSize = new(24, 0);
                rankLabel.Modulate = ColorOf(style.WarningColor);
                rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
                rankLabel.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(rankLabel);
            }

            row.AddChild(PlayerPortrait(player, 42, style.FontSize + 3, accent, style));

            var identity = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipContents = true,
            };
            identity.AddThemeConstantOverride("separation", -2);
            var playerName = TruncatedLabel(player.DisplayName, style, false, style.FontSize + 2);
            ApplyIdentityColor(playerName, accent);
            identity.AddChild(playerName);
            identity.AddChild(TruncatedLabel(player.CharacterId, style, true, Math.Max(9, style.FontSize - 2)));
            row.AddChild(identity);
            var valueStack = new VBoxContainer { CustomMinimumSize = new(74, 0) };
            valueStack.AddThemeConstantOverride("separation", -3);
            var amount = Label(Format(value), style, false, style.FontSize + 5);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            valueStack.AddChild(amount);
            var share = Label(total > 0m ? $"{value / total:P1}" : "—", style, true, style.FontSize - 1);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            share.Modulate = ColorOf(accent);
            valueStack.AddChild(share);
            row.AddChild(valueStack);
            return row;
        }

        private static HBoxContainer SingleLinePlayerHeader(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            string accent,
            DashboardStyleDefinition style)
        {
            var rowHeight = Math.Max(28, style.RowHeight);
            var row = new HBoxContainer { CustomMinimumSize = new(0, rowHeight) };
            DashboardTooltip.SetValue(row, player.DisplayName, value, total, player.CharacterId);
            row.AddThemeConstantOverride("separation", 4);
            if (rank > 0)
            {
                var rankLabel = Label(rank.ToString(CultureInfo.CurrentCulture), style, true, style.FontSize);
                rankLabel.CustomMinimumSize = new(20, 0);
                rankLabel.Modulate = ColorOf(style.WarningColor);
                rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
                rankLabel.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(rankLabel);
            }

            var portraitSize = Math.Clamp(rowHeight - 4, 24, 32);
            row.AddChild(PlayerPortrait(player, portraitSize, style.FontSize + 1, accent, style));
            var share = total > 0m ? $"{value / total:P1}" : "—";
            row.AddChild(Meter(player.DisplayName, $"{Format(value)}  ·  {share}", value,
                Math.Max(1m, total), accent, style, rowHeight));
            return row;
        }

        protected static Control PlayerMeterRow(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            decimal maximum,
            string accent,
            DashboardStyleDefinition style,
            bool showPercentages)
        {
            return PlayerMeterRow(player, rank, value, total, maximum, accent, style, showPercentages, false);
        }

        protected static Control PlayerMeterRow(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            decimal maximum,
            string accent,
            DashboardStyleDefinition style,
            bool showPercentages,
            bool singleLine)
        {
            if (singleLine)
                return SingleLinePlayerMeterRow(player, rank, value, total, maximum, accent, style,
                    showPercentages);

            var root = new VBoxContainer { CustomMinimumSize = new(0, 42) };
            DashboardTooltip.SetValue(root, player.DisplayName, value, total, player.CharacterId);
            root.AddThemeConstantOverride("separation", 3);
            var header = new HBoxContainer { CustomMinimumSize = new(0, 31) };
            header.AddThemeConstantOverride("separation", 6);

            var rankLabel = Label(rank.ToString(CultureInfo.CurrentCulture), style, true, style.FontSize);
            rankLabel.Name = "MetricMeterRank";
            rankLabel.CustomMinimumSize = new(20, 0);
            rankLabel.Modulate = ColorOf(style.WarningColor);
            rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
            rankLabel.VerticalAlignment = VerticalAlignment.Center;
            header.AddChild(rankLabel);

            header.AddChild(PlayerPortrait(player, 29, style.FontSize + 2, accent, style));

            var name = TruncatedLabel(player.DisplayName, style, false, style.FontSize + 1);
            name.VerticalAlignment = VerticalAlignment.Center;
            ApplyIdentityColor(name, accent);
            header.AddChild(name);

            var amount = Label(Format(value), style, false, style.FontSize + 3);
            amount.Name = "MetricMeterValue";
            amount.CustomMinimumSize = new(64, 0);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.VerticalAlignment = VerticalAlignment.Center;
            header.AddChild(amount);

            var share = Label(showPercentages ? total > 0m ? $"{value / total:P1}" : "—" : string.Empty,
                style, true, style.FontSize - 1);
            share.Name = "MetricMeterShare";
            share.CustomMinimumSize = new(48, 0);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            share.VerticalAlignment = VerticalAlignment.Center;
            share.Modulate = ColorOf(accent);
            share.Visible = showPercentages;
            header.AddChild(share);
            root.AddChild(header);

            var bar = new ProgressBar
            {
                Name = "MetricMeterProgress",
                CustomMinimumSize = new(0, 6),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ShowPercentage = false,
                MinValue = 0d,
                MaxValue = Math.Max(1d, (double)maximum),
                Value = (double)Math.Max(0m, value),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
            {
                BgColor = ColorOf(accent),
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
            });
            bar.AddThemeStyleboxOverride("background", new StyleBoxFlat
            {
                BgColor = ColorOf(style.TrackColor),
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
            });
            root.AddChild(bar);
            return root;
        }

        private static HBoxContainer SingleLinePlayerMeterRow(
            PlayerMetricSnapshot player,
            int rank,
            decimal value,
            decimal total,
            decimal maximum,
            string accent,
            DashboardStyleDefinition style,
            bool showPercentages)
        {
            var rowHeight = Math.Max(28, style.RowHeight);
            var row = new HBoxContainer { CustomMinimumSize = new(0, rowHeight) };
            DashboardTooltip.SetValue(row, player.DisplayName, value, total, player.CharacterId);
            row.AddThemeConstantOverride("separation", 4);

            var rankLabel = Label(rank.ToString(CultureInfo.CurrentCulture), style, true, style.FontSize);
            rankLabel.Name = "MetricMeterRank";
            rankLabel.CustomMinimumSize = new(20, 0);
            rankLabel.Modulate = ColorOf(style.WarningColor);
            rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
            rankLabel.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(rankLabel);

            var portraitSize = Math.Clamp(rowHeight - 4, 24, 30);
            row.AddChild(PlayerPortrait(player, portraitSize, style.FontSize + 1, accent, style));
            var valueText = SingleLineValueText(value, total, showPercentages);
            var meter = CreateLabeledMeter(
                player.DisplayName,
                valueText,
                value,
                maximum,
                accent,
                style,
                rowHeight);
            meter.Root.Name = "MetricMeterProgress";
            meter.Name.Name = "MetricMeterName";
            meter.Value.Name = "MetricMeterValue";
            row.AddChild(meter.Root);
            return row;
        }

        protected static void UpdatePlayerMeterRow(
            Control content,
            int rank,
            decimal value,
            decimal total,
            decimal maximum,
            bool showPercentages,
            bool singleLine)
        {
            if (content.FindChild("MetricMeterRank", true, false) is Label rankLabel)
            {
                var text = rank.ToString(CultureInfo.CurrentCulture);
                if (!string.Equals(rankLabel.Text, text, StringComparison.Ordinal))
                    rankLabel.Text = text;
            }

            if (content.FindChild("MetricMeterProgress", true, false) is ProgressBar bar)
            {
                var maxValue = Math.Max(1d, (double)maximum);
                var currentValue = (double)Math.Max(0m, value);
                if (!Mathf.IsEqualApprox(bar.MaxValue, maxValue))
                    bar.MaxValue = maxValue;
                if (!Mathf.IsEqualApprox(bar.Value, currentValue))
                    bar.Value = currentValue;
            }

            if (content.FindChild("MetricMeterValue", true, false) is Label amount)
            {
                var text = singleLine
                    ? SingleLineValueText(value, total, showPercentages)
                    : Format(value);
                if (!string.Equals(amount.Text, text, StringComparison.Ordinal))
                    amount.Text = text;
            }

            if (content.FindChild("MetricMeterShare", true, false) is not Label share)
                return;
            var shareText = showPercentages
                ? total > 0m ? $"{value / total:P1}" : "—"
                : string.Empty;
            if (!string.Equals(share.Text, shareText, StringComparison.Ordinal))
                share.Text = shareText;
            if (share.Visible != showPercentages)
                share.Visible = showPercentages;
        }

        internal static string SingleLineValueText(decimal value, decimal total, bool showPercentages)
        {
            return showPercentages && total > 0m
                ? $"{Format(value)}  ·  {value / total:P1}"
                : Format(value);
        }

        private static string PlayerMonogram(string displayName)
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? "?"
                : displayName.Trim()[..1].ToUpper(CultureInfo.CurrentCulture);
        }

        private static Control PlayerPortrait(PlayerMetricSnapshot player, int size, int fontSize, string accent,
            DashboardStyleDefinition style)
        {
            try
            {
                var portrait = CharacterPortraitCache.Get(player.CharacterId);
                if (portrait != null)
                    return new TextureRect
                    {
                        Texture = portrait,
                        CustomMinimumSize = new(size, size),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
            }
            catch (ObjectDisposedException)
            {
                CharacterPortraitCache.Invalidate(player.CharacterId);
            }

            var monogram = Label(PlayerMonogram(player.DisplayName), style, false, fontSize);
            monogram.CustomMinimumSize = new(size, size);
            monogram.HorizontalAlignment = HorizontalAlignment.Center;
            monogram.VerticalAlignment = VerticalAlignment.Center;
            monogram.Modulate = ColorOf(accent);
            return monogram;
        }

        protected static Control SegmentedMeter(
            string text,
            decimal first,
            decimal second,
            string firstColor,
            string secondColor,
            DashboardStyleDefinition style,
            string firstLabel,
            string secondLabel,
            string explanation)
        {
            var root = new VBoxContainer();
            DashboardTooltip.Set(root,
            [
                text,
                $"{firstLabel}: {Format(first)}",
                $"{secondLabel}: {Format(second)}",
                explanation,
            ]);
            root.AddThemeConstantOverride("separation", 2);
            var title = WrappedLabel(text, style);
            title.MouseFilter = Control.MouseFilterEnum.Ignore;
            root.AddChild(title);
            var segments = new HBoxContainer
            {
                CustomMinimumSize = new(0, Math.Max(10, style.RowHeight / 2)),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            segments.AddThemeConstantOverride("separation", 1);
            var total = Math.Max(1m, first + second);
            var firstSegment = new ColorRect
            {
                Color = ColorOf(firstColor),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = (float)(first / total),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var secondSegment = new ColorRect
            {
                Color = ColorOf(secondColor),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = (float)(second / total),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            segments.AddChild(firstSegment);
            segments.AddChild(secondSegment);
            root.AddChild(segments);
            return root;
        }

        protected void Empty(DashboardRenderContext context, string text = "No combat data")
        {
            if (ReconcileRowsOnRefresh)
                ReconcileRows(
                [
                    new("__empty", EmptyRowFingerprint(context, text), () => CreateEmptyRow(context, text)),
                ]);
            else
                Rows.AddChild(CreateEmptyRow(context, text));
            Status.Text = string.Empty;
        }

        protected static Control CreateEmptyRow(DashboardRenderContext context, string text = "No combat data")
        {
            var empty = new VBoxContainer { CustomMinimumSize = new(0, 112) };
            empty.AddThemeConstantOverride("separation", 4);
            var glyph = DashboardIcons.View(DashboardIcon.NoData, context.Style.FontSize + 16,
                ColorOf(context.Style.WarningColor));
            glyph.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            empty.AddChild(glyph);
            var title = Label(ModLocalization.Get("dashboard.noData", text), context.Style, false,
                context.Style.FontSize + 2);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddChild(title);
            var hint = WrappedLabel(
                ModLocalization.Get("dashboard.waiting", "Live data will appear when combat begins"),
                context.Style, true,
                Math.Max(9, context.Style.FontSize - 1));
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddChild(hint);
            return Surface(empty, context.Style, context.Style.WarningColor);
        }

        protected sealed record ReconciledRow(
            string Key,
            string Fingerprint,
            Func<Control> Create,
            Action<Control>? Update = null);

        protected sealed record VariableReconciledRow(ReconciledRow Row, float EstimatedHeight);

        private sealed record LabeledMeter(ProgressBar Root, Label Name, Label Value);

        private readonly record struct ReconciledRowState(string Fingerprint, Control Control);

        private readonly record struct RendererChromeState(
            bool SingleLine,
            int FooterFontSize,
            Color SecondaryTextColor);
    }

    internal sealed partial class OverviewRenderer : DashboardRendererBase
    {
        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            return new(DashboardDataComponents.Metrics | DashboardDataComponents.Timeline);
        }

        protected override void Render(DashboardRenderContext context)
        {
            RenderOverview(context);
        }
    }

    internal sealed class MetricMeterRenderer(string? fixedMetricId = null) : DashboardRendererBase
    {
        private readonly PlayerColorPaletteCache _playerColorCache = new();
        private DashboardRenderContext? _lastContext;
        private string? _selectedPlayerKey;

        protected override bool ReconcileRowsOnRefresh => true;

        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            var metricId = fixedMetricId ?? parameters.GetValueOrDefault(DashboardParameterIds.MetricId,
                MetricIds.DamageContribution);
            var components = DashboardDataComponents.Metrics;
            if (DashboardPresentation.SplitSummons(parameters))
                components |= DashboardDataComponents.Events;
            return new(components, RequiredMetricIds(metricId));
        }

        internal static IReadOnlyCollection<string> RequiredMetricIds(string metricId)
        {
            return metricId switch
            {
                MetricIds.DamageContribution =>
                    [MetricIds.DamageContribution, MetricIds.DamageDealt],
                MetricIds.EffectiveHpDamageDealt =>
                    [MetricIds.EffectiveHpDamageDealt, MetricIds.DamageDealt],
                MetricIds.EffectiveHpDamageContribution =>
                    [MetricIds.EffectiveHpDamageContribution, MetricIds.DamageDealt],
                MetricIds.DefenseContribution =>
                [
                    MetricIds.DefenseContribution,
                    MetricIds.DamageMitigated,
                    MetricIds.DamageBlocked,
                    MetricIds.HealingReceived,
                ],
                _ => [metricId],
            };
        }

        protected override void Render(DashboardRenderContext context)
        {
            _lastContext = context;
            var metricId = fixedMetricId ?? context.Parameters.GetValueOrDefault(DashboardParameterIds.MetricId,
                MetricIds.DamageContribution);
            var definition = Main.Api.MetricDefinitions.FirstOrDefault(item => item.Id == metricId);
            Title = definition == null
                ? metricId
                : ModLocalization.Get(definition.NameLocalizationKey, definition.FallbackName);
            AccentColor = MetricAccent(metricId, context.Style);
            var snapshot = context.Snapshot;
            Subtitle = ScopeName(context.Scope);
            CompactSubtitle = CompactScopeName(context.Scope);
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                ReconcileRows(
                [
                    new("__empty", EmptyRowFingerprint(context),
                        () => CreateEmptyRow(context)),
                ]);
                Status.Text = string.Empty;
                return;
            }

            var values = BuildMeterEntries(snapshot, metricId, DashboardPresentation.SplitSummons(context.Parameters));
            var playerAccents = _playerColorCache.Resolve(
                values.Select(entry => new PlayerColorIdentity(
                    entry.Player.PlayerKey,
                    entry.Player.PlayerNetId,
                    entry.Player.CharacterId,
                    entry.Player.IdentityColor)),
                context.Style,
                CharacterPortraitCache.GetNameColor);
            var total = values.Sum(item => item.Value);
            Subtitle = $"{ScopeName(context.Scope)}  ·  " +
                       $"{ModLocalization.Format("overlay.rounds", "{0} rounds", snapshot.RoundCount)}  ·  " +
                       $"{ModLocalization.Get("overlay.total", "Total")} {Format(total)}";
            CompactSubtitle = $"{CompactScopeName(context.Scope)} · " +
                              $"{ModLocalization.Format("overlay.roundsCompact", "{0}R", snapshot.RoundCount)} · " +
                              $"{ModLocalization.Format("overlay.totalCompact", "Σ{0}", Format(total))}";
            var selected = _selectedPlayerKey == null
                ? null
                : values.FirstOrDefault(item => item.Player.PlayerKey == _selectedPlayerKey);
            if (selected != null)
            {
                RenderPlayerDetail(context, metricId, selected.Player, selected.Value, total,
                    playerAccents[selected.Player.PlayerKey]);
                return;
            }

            _selectedPlayerKey = null;
            RenderRanking(context, values, total, playerAccents);
            Status.Text = snapshot.EncounterName;
        }

        private void RenderRanking(
            DashboardRenderContext context,
            MeterEntry[] values,
            decimal total,
            IReadOnlyDictionary<string, string> playerAccents)
        {
            var maximum = Math.Max(1m, values.Select(item => item.Value).DefaultIfEmpty().Max());
            var singleLine = DashboardPresentation.SingleLine(context.Parameters);
            ReconcileRows(values.Select((entry, index) =>
            {
                var (player, value) = entry;
                var accent = playerAccents[player.PlayerKey];
                var fingerprint = string.Join("\u001e", MeterStyleFingerprint(context), player.DisplayName,
                    player.CharacterId, accent);
                return new ReconciledRow(
                    $"player:{player.PlayerKey}",
                    fingerprint,
                    () =>
                    {
                        var content = PlayerMeterRow(player, index + 1, value, total, maximum, accent,
                            context.Style, context.ShowPercentages, singleLine);
                        var row = InteractiveRow(content, context.Style, accent, singleLine);
                        row.GuiInput += input =>
                        {
                            if (input is not InputEventMouseButton
                                {
                                    ButtonIndex: MouseButton.Left, Pressed: true,
                                })
                                return;
                            _selectedPlayerKey = player.PlayerKey;
                            RefreshLast();
                        };
                        return row;
                    },
                    row =>
                    {
                        UpdatePlayerMeterRow(row, index + 1, value, total, maximum, context.ShowPercentages,
                            singleLine);
                        DashboardTooltip.Set(row,
                        [
                            player.DisplayName,
                            $"{ModLocalization.Get("dashboard.tooltip.value", "Value")}: {Format(value)}",
                            $"{ModLocalization.Get("dashboard.tooltip.share", "Share")}: " +
                            (total > 0m ? $"{value / total:P1}" : "—"),
                            ModLocalization.Get("dashboard.openPlayerDetail", "Open source details"),
                        ]);
                    });
            }));
        }

        private void RenderPlayerDetail(
            DashboardRenderContext context,
            string metricId,
            PlayerMetricSnapshot player,
            decimal value,
            decimal total,
            string accent)
        {
            var singleLine = DashboardPresentation.SingleLine(context.Parameters);
            var rows = new List<ReconciledRow>
            {
                new("detail:header",
                    string.Join("\u001e", MeterStyleFingerprint(context), player.PlayerKey, player.DisplayName, value,
                        total, singleLine, accent),
                    () =>
                    {
                        var backText = ModLocalization.Get("dashboard.backToRanking", "Team ranking");
                        var back = new Button
                        {
                            Text = singleLine ? string.Empty : backText,
                            TooltipText = backText,
                            Alignment = singleLine ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                            FocusMode = Control.FocusModeEnum.None,
                        };
                        if (singleLine)
                            DashboardControlTheme.ApplyIconButton(back, compact: true);
                        else
                            DashboardControlTheme.ApplyButton(back, DashboardButtonKind.Subtle);
                        DashboardIcons.Apply(back, DashboardIcon.Back);
                        back.Pressed += () =>
                        {
                            _selectedPlayerKey = null;
                            RefreshLast();
                        };
                        var playerHeader = PlayerHeader(player, 0, value, total, accent,
                            context.Style, singleLine);
                        if (!singleLine)
                        {
                            var stack = new VBoxContainer();
                            stack.AddChild(back);
                            stack.AddChild(playerHeader);
                            return stack;
                        }

                        var detailHeader =
                            new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                        detailHeader.AddThemeConstantOverride("separation", 4);
                        detailHeader.AddChild(back);
                        playerHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                        detailHeader.AddChild(playerHeader);
                        return detailHeader;
                    }),
            };
            if (!player.Sources.TryGetValue(metricId, out var sources) || sources.Count == 0)
            {
                rows.Add(new("detail:no-source", MeterStyleFingerprint(context),
                    () => Label(ModLocalization.Get("overlay.noSource", "No source breakdown"),
                        context.Style, true)));
                ReconcileRows(rows);
                Status.Text = string.Empty;
                return;
            }

            var sourceMaximum = Math.Max(1m, sources.Max(item => item.Value));
            rows.AddRange(sources.Select(source =>
            {
                var percent = value > 0m ? $"{source.Value / value:P1}" : "—";
                return new ReconciledRow($"source:{source.SourceKey}",
                    string.Join("\u001e", MeterStyleFingerprint(context), source.DisplayName, source.Value,
                        source.Occurrences, value, sourceMaximum),
                    () => Meter(source.DisplayName,
                        $"{Format(source.Value)}  ·  {percent}  ·  ×{source.Occurrences}",
                        source.Value, sourceMaximum, accent, context.Style,
                        singleLine ? context.Style.RowHeight : Math.Max(24, context.Style.RowHeight - 5)));
            }));

            ReconcileRows(rows);
            Status.Text = ModLocalization.Format("dashboard.sourceCount", "{0} · {1} sources",
                player.DisplayName, sources.Count);
        }

        private static string MeterStyleFingerprint(DashboardRenderContext context)
        {
            return string.Join(':',
                VisualStyleFingerprint(context.Style),
                context.ShowPercentages,
                DashboardPresentation.SingleLine(context.Parameters));
        }

        private static PanelContainer InteractiveRow(
            Control content,
            DashboardStyleDefinition style,
            string accent,
            bool singleLine)
        {
            var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
            var normal = RowStyle(style.SurfaceColor, style.BorderColor, 0.16f, singleLine);
            var hover = RowStyle(style.SurfaceColor, accent, 0.7f, singleLine);
            row.AddThemeStyleboxOverride("panel", normal);
            row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hover);
            row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normal);
            content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var layout = new HBoxContainer();
            layout.AddChild(content);
            var chevron = Label("›", style, true, style.FontSize + 5);
            chevron.CustomMinimumSize = new(14, 0);
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.MouseFilter = Control.MouseFilterEnum.Ignore;
            layout.AddChild(chevron);
            row.AddChild(layout);
            return row;
        }

        private static StyleBoxFlat RowStyle(
            string background,
            string border,
            float borderAlpha,
            bool singleLine)
        {
            var backgroundColor = ColorOf(background);
            backgroundColor.A *= 0.42f;
            return new()
            {
                BgColor = backgroundColor,
                BorderColor = ColorOf(border) with { A = borderAlpha },
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                ContentMarginLeft = singleLine ? 3f : 6f,
                ContentMarginTop = singleLine ? 1f : 4f,
                ContentMarginRight = singleLine ? 3f : 6f,
                ContentMarginBottom = singleLine ? 1f : 5f,
            };
        }

        private void RefreshLast()
        {
            if (_lastContext != null)
                Refresh(_lastContext);
        }

        private static MeterEntry[] BuildMeterEntries(
            CombatSnapshot snapshot,
            string metricId,
            bool splitSummons)
        {
            var useLegacyFallback = ShouldUseLegacyFallback(snapshot, metricId);
            if (!splitSummons)
                return snapshot.Players
                    .Select(player => ResolveMeterEntry(player, metricId, useLegacyFallback))
                    .OrderByDescending(item => item.Value)
                    .ToArray();

            var entries = new List<MeterEntry>();
            foreach (var player in snapshot.Players)
            {
                var observations = snapshot.Events.Where(observation =>
                    observation.MetricId == metricId && observation.Subject.Key == player.PlayerKey).ToArray();
                var summonGroups = observations
                    .Where(IsSummonObservation)
                    .GroupBy(observation => (
                        ModelId: observation.Tags.GetValueOrDefault(ObservationTagIds.ActorModelId),
                        DisplayName: observation.Tags.GetValueOrDefault(ObservationTagIds.ActorDisplayName)))
                    .Where(group => !string.IsNullOrWhiteSpace(group.Key.ModelId) ||
                                    !string.IsNullOrWhiteSpace(group.Key.DisplayName))
                    .ToArray();
                if (summonGroups.Length == 0)
                {
                    entries.Add(ResolveMeterEntry(player, metricId, useLegacyFallback));
                    continue;
                }

                var summonValue = summonGroups.Sum(group => group.Sum(observation => observation.Value));
                var directValue = Math.Max(0m, Metric(player, metricId) - summonValue);
                if (directValue > 0m)
                {
                    var directObservations = observations.Where(observation => !IsSummonObservation(observation));
                    entries.Add(new(CreateEntrySnapshot(player, player.PlayerKey, player.DisplayName,
                        player.CharacterId, metricId, directValue, directObservations), directValue));
                }

                entries.AddRange(summonGroups.Select(group =>
                {
                    var value = group.Sum(observation => observation.Value);
                    var summonName = !string.IsNullOrWhiteSpace(group.Key.DisplayName)
                        ? group.Key.DisplayName
                        : !string.IsNullOrWhiteSpace(group.Key.ModelId)
                            ? group.Key.ModelId
                            : ModLocalization.Get("source.unknown", "Unknown source");
                    var displayName = ModLocalization.Format("dashboard.summonEntry", "{0} · {1}",
                        summonName, player.DisplayName);
                    var key = $"summon:{player.PlayerKey}:{group.Key.ModelId}:{group.Key.DisplayName}";
                    return new MeterEntry(CreateEntrySnapshot(player, key, displayName, string.Empty, metricId,
                        value, group), value);
                }));
            }

            return entries.OrderByDescending(item => item.Value).ToArray();
        }

        private static bool IsSummonObservation(MetricObservation observation)
        {
            if (observation.Tags.GetValueOrDefault(ObservationTagIds.ActorKind) !=
                nameof(AnalyticsEntityKind.Summon))
                return false;
            if (observation.Tags.GetValueOrDefault(ObservationTagIds.ActorOwnerKey) != observation.Subject.Key)
                return false;
            var component = observation.Tags.GetValueOrDefault(ObservationTagIds.ContributionComponent);
            return string.IsNullOrEmpty(component) || component is ContributionComponentIds.BaseDamage or
                ContributionComponentIds.Execution;
        }

        private static bool ShouldUseLegacyFallback(CombatSnapshot snapshot, string metricId)
        {
            if (snapshot.Players.Any(player => player.Totals.ContainsKey(metricId)))
                return false;
            if (metricId is MetricIds.DamageContribution or MetricIds.DefenseContribution)
                return true;
            if (metricId is not (MetricIds.EffectiveHpDamageDealt or
                MetricIds.EffectiveHpDamageContribution))
                return false;

            var hasLegacyDamage = snapshot.Players.Any(player => player.Totals.ContainsKey(MetricIds.DamageDealt));
            var hasOutputDamageSemantics = (snapshot.Timeline ?? []).Any(item => item.Damage is
            { BlockedAmount: > 0m } damage && damage.EffectiveAmount > damage.HpLost);
            return hasLegacyDamage && !hasOutputDamageSemantics;
        }

        private static MeterEntry ResolveMeterEntry(
            PlayerMetricSnapshot player,
            string metricId,
            bool useLegacyFallback)
        {
            if (!useLegacyFallback)
                return new(player, Metric(player, metricId));
            var sourceMetricIds = metricId switch
            {
                MetricIds.DamageContribution or MetricIds.EffectiveHpDamageDealt or
                    MetricIds.EffectiveHpDamageContribution => [MetricIds.DamageDealt],
                _ => new[] { MetricIds.DamageMitigated, MetricIds.DamageBlocked, MetricIds.HealingReceived },
            };
            var value = sourceMetricIds.Sum(sourceMetricId => Metric(player, sourceMetricId));
            var sources = sourceMetricIds.SelectMany(sourceMetricId =>
                    player.Sources.GetValueOrDefault(sourceMetricId) ?? [])
                .GroupBy(source => source.SourceKey, StringComparer.Ordinal)
                .Select(group => group.First() with
                {
                    Value = group.Sum(source => source.Value),
                    Occurrences = group.Sum(source => source.Occurrences),
                })
                .OrderByDescending(source => source.Value)
                .ToArray();
            var snapshot = player with
            {
                Totals = new Dictionary<string, decimal>(player.Totals, StringComparer.Ordinal)
                {
                    [metricId] = value,
                },
                Sources = new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(player.Sources,
                    StringComparer.Ordinal)
                {
                    [metricId] = sources,
                },
            };
            return new(snapshot, value);
        }

        private static PlayerMetricSnapshot CreateEntrySnapshot(
            PlayerMetricSnapshot owner,
            string key,
            string displayName,
            string characterId,
            string metricId,
            decimal value,
            IEnumerable<MetricObservation> observations)
        {
            var sources = observations
                .GroupBy(observation => observation.Source.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First().Source;
                    return new SourceMetricSnapshot(first.Key, first.Kind, first.ModelId, first.DisplayName,
                        group.Sum(observation => observation.Value), group.Count());
                })
                .OrderByDescending(source => source.Value)
                .ToArray();
            return new(key, owner.PlayerNetId, displayName, characterId,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { [metricId] = value },
                new Dictionary<string, IReadOnlyList<SourceMetricSnapshot>>(StringComparer.Ordinal)
                {
                    [metricId] = sources,
                });
        }

        private static string MetricAccent(string metricId, DashboardStyleDefinition style)
        {
            return metricId switch
            {
                MetricIds.DamageDealt or MetricIds.DamageContribution or MetricIds.EffectiveHpDamageDealt or
                    MetricIds.EffectiveHpDamageContribution or MetricIds.DamageTaken or
                    MetricIds.Overkill => style.NegativeColor,
                MetricIds.DamagePrevented or MetricIds.DefenseContribution or MetricIds.HealingContribution =>
                    style.PositiveColor,
                MetricIds.BlockGained or MetricIds.DamageBlocked or MetricIds.HealingReceived =>
                    style.PositiveColor,
                _ => Accent(style, 1),
            };
        }

        private static string ScopeName(DashboardDataScope scope)
        {
            return scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRun", "Current run")
                : ModLocalization.Get("overlay.currentCombat", "Current combat");
        }

        private static string CompactScopeName(DashboardDataScope scope)
        {
            return scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRunCompact", "Run")
                : ModLocalization.Get("overlay.currentCombatCompact", "Combat");
        }

        private sealed record MeterEntry(PlayerMetricSnapshot Player, decimal Value);
    }

    internal sealed class CardLogRenderer : FilteredTimelineRenderer
    {
        private Dictionary<string, CombatTimelineEvent> _eventById = new(StringComparer.Ordinal);
        private int _kindColumnFontSize = -1;
        private float _kindColumnWidth = 76f;

        protected override bool CollapseSemanticMoves => true;

        protected override void Render(DashboardRenderContext context)
        {
            UpdateKindColumnWidth(context.Style);
            _eventById = context.Snapshot == null
                ? new(StringComparer.Ordinal)
                : Timeline(context.Snapshot).ToDictionary(item => item.EventId, StringComparer.Ordinal);
            base.Render(context);
        }

        protected override bool Include(CombatTimelineEvent timelineEvent)
        {
            if (timelineEvent.Kind == CombatTimelineKind.Attack)
                return timelineEvent.Phase == TimelineEventPhase.Completed;
            return IsIncludedKind(timelineEvent.Kind);
        }

        protected override string RowText(CombatTimelineEvent timelineEvent)
        {
            var origin = CausalOrigin(timelineEvent);
            return $"[T{timelineEvent.TurnIndex}] " +
                   DashboardLocalization.TimelineDescription(timelineEvent, origin.Source, origin.Actor);
        }

        protected override Control CreateRow(CombatTimelineEvent timelineEvent, DashboardStyleDefinition style)
        {
            var row = new HBoxContainer { CustomMinimumSize = new(0, style.RowHeight) };
            row.AddThemeConstantOverride("separation", 6);
            var badgeText = DashboardLocalization.TimelineKind(timelineEvent.Kind);
            var badgeColor = timelineEvent.Kind switch
            {
                CombatTimelineKind.Damage or CombatTimelineKind.Execution => style.NegativeColor,
                CombatTimelineKind.Block => style.PositiveColor,
                CombatTimelineKind.Turn or CombatTimelineKind.Phase or CombatTimelineKind.Combat => Accent(style, 1),
                CombatTimelineKind.CardPlay or CombatTimelineKind.CardDraw or CombatTimelineKind.CardMove =>
                    Accent(style, 3),
                _ => Accent(style, 4),
            };
            row.AddChild(Badge(badgeText, badgeColor, style, (int)Math.Ceiling(_kindColumnWidth), true));
            var label = TruncatedLabel(RowText(timelineEvent), style);
            row.AddChild(label);
            var surface = Surface(row, style, badgeColor, 4);
            _eventById.TryGetValue(timelineEvent.ParentEventId ?? string.Empty, out var parent);
            var origin = CausalOrigin(timelineEvent);
            DashboardTooltip.Set(surface,
                DashboardLocalization.TimelineTooltip(timelineEvent, parent, origin.Source, origin.Actor));
            return surface;
        }

        protected override float VirtualRowHeight(DashboardStyleDefinition style)
        {
            return style.RowHeight + 8f;
        }

        protected override string RowFingerprint(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style)
        {
            return string.Join("\u001e",
                base.RowFingerprint(timelineEvent, style),
                _kindColumnWidth);
        }

        private static bool IsIncludedKind(CombatTimelineKind kind)
        {
            return kind is CombatTimelineKind.Combat or CombatTimelineKind.Turn or
                CombatTimelineKind.Phase or CombatTimelineKind.HandDraw or CombatTimelineKind.CardPlay or
                CombatTimelineKind.CardDraw or CombatTimelineKind.CardMove or CombatTimelineKind.Damage or
                CombatTimelineKind.Block or CombatTimelineKind.Healing or CombatTimelineKind.HpLoss or
                CombatTimelineKind.Power or CombatTimelineKind.Potion or CombatTimelineKind.Energy or
                CombatTimelineKind.Orb or CombatTimelineKind.Summon or CombatTimelineKind.Shuffle or
                CombatTimelineKind.Death or CombatTimelineKind.Execution;
        }

        private void UpdateKindColumnWidth(DashboardStyleDefinition style)
        {
            var fontSize = Math.Max(9, style.FontSize - 2);
            if (_kindColumnFontSize == fontSize)
                return;
            _kindColumnFontSize = fontSize;
            var font = DashboardControlTheme.BodyFont;
            _kindColumnWidth = Math.Clamp(
                Enum.GetValues<CombatTimelineKind>()
                    .Where(kind => kind == CombatTimelineKind.Attack || IsIncludedKind(kind))
                    .Select(DashboardLocalization.TimelineKind)
                    .Distinct(StringComparer.CurrentCulture)
                    .Select(text => font.GetStringSize(text, fontSize: fontSize).X + 18f)
                    .DefaultIfEmpty(76f)
                    .Max(),
                76f,
                160f);
        }

        private (EntityDescriptor? Actor, SourceDescriptor? Source) CausalOrigin(
            CombatTimelineEvent timelineEvent)
        {
            EntityDescriptor? actor = null;
            var parentId = timelineEvent.ParentEventId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (parentId != null && visited.Add(parentId) && _eventById.TryGetValue(parentId, out var parent))
            {
                actor ??= parent.Actor;
                if (parent.Source != null && parent.Source.Key != timelineEvent.Source?.Key)
                    return (actor, parent.Source);
                parentId = parent.ParentEventId;
            }

            return (actor, null);
        }
    }

    internal sealed class ReceivedDamageRenderer : DashboardRendererBase
    {
        private bool _historyExpanded;
        private string? _historySnapshotKey;
        private DashboardRenderContext? _lastContext;

        protected override bool ReconcileRowsOnRefresh => true;

        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            return new(DashboardDataComponents.Metrics | DashboardDataComponents.Timeline,
            [
                MetricIds.DamageBlocked,
                MetricIds.BlockGained,
                MetricIds.DamageTaken,
                MetricIds.Deaths,
            ]);
        }

        protected override void Render(DashboardRenderContext context)
        {
            var snapshot = context.Snapshot;
            if (snapshot == null || snapshot.Players.Count == 0)
            {
                Empty(context);
                return;
            }

            _lastContext = context;
            var historySnapshotKey = $"{context.Scope}\u001f{snapshot.RunId}\u001f{snapshot.CombatId}";
            if (!string.Equals(_historySnapshotKey, historySnapshotKey, StringComparison.Ordinal))
            {
                _historySnapshotKey = historySnapshotKey;
                _historyExpanded = false;
            }

            var totalTaken = snapshot.Players.Sum(item => SnapshotStatistics.Survival(snapshot, item.PlayerNetId)
                .PlayerHpLost);
            var ranked = snapshot.Players.OrderByDescending(item =>
                SnapshotStatistics.Survival(snapshot, item.PlayerNetId).PlayerHpLost).ToArray();
            var playerAccents = PlayerAccents(ranked, context.Style);
            var rows = new List<VariableReconciledRow>(ranked.Length + 201);
            for (var index = 0; index < ranked.Length; index++)
            {
                var player = ranked[index];
                var survival = SnapshotStatistics.Survival(snapshot, player.PlayerNetId);
                var taken = survival.PlayerHpLost;
                var incoming = SnapshotAnalysisCache.Get(snapshot).Incoming(player);
                var blocked = incoming.EffectiveBlock;
                var accent = playerAccents[player.PlayerKey];
                var rank = index + 1;
                var fingerprint = string.Join('\u001e', VisualStyleFingerprint(context.Style), player.PlayerKey,
                    rank, taken, totalTaken, blocked, survival, incoming.HpLossRatio, incoming.SelfHpLost,
                    incoming.SelfHpLossRatio, incoming.BlockGained, incoming.UnspentBlock,
                    string.Join('\u001d', incoming.Sources.Select(source =>
                        $"{source.Key}\u001c{source.HpLost}\u001c{source.Occurrences}")), accent,
                    DashboardPresentation.SingleLine(context.Parameters));
                rows.Add(new(new($"player:{player.PlayerKey}", fingerprint, () =>
                {
                    var card = new VBoxContainer();
                    card.AddThemeConstantOverride("separation", 5);
                    card.AddChild(PlayerHeader(player, rank, taken, totalTaken, accent, context.Style,
                        DashboardPresentation.SingleLine(context.Parameters)));
                    card.AddChild(SegmentedMeter(ModLocalization.Format("dashboard.received.summary",
                            "{0} HP lost · {1} blocked · {2} deaths", Format(taken), Format(blocked),
                            survival.PlayerDeaths),
                        taken,
                        blocked,
                        context.Style.NegativeColor,
                        Accent(context.Style, 1),
                        context.Style,
                        ModLocalization.Get("analysis.hpLost", "HP lost"),
                        ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                        ModLocalization.Get("analysis.hpLossRatio.hint",
                            "HP loss ratio is HP lost divided by HP lost plus effective block. Lower means " +
                            "more incoming damage was absorbed.")));
                    var defenseRatioHint = ModLocalization.Get("analysis.defenseRatios.hint",
                        "HP loss ratio = HP lost / (HP lost + effective block). Unspent block becomes final " +
                        "waste only after combat ends.");
                    var statistics = ResponsiveGrid(2, 150f, 8, 6);
                    statistics.AddChild(IncomingStat(
                        ModLocalization.Get("analysis.effectiveBlock", "Effective block"),
                        Format(incoming.EffectiveBlock), context.Style.PositiveColor, context.Style));
                    statistics.AddChild(IncomingStat(
                        ModLocalization.Get("analysis.unspentBlock", "Unspent block"),
                        Format(incoming.UnspentBlock), context.Style.WarningColor, context.Style,
                        defenseRatioHint));
                    statistics.AddChild(IncomingStat(
                        ModLocalization.Get("analysis.hpLossRatio", "HP loss ratio"),
                        $"{incoming.HpLossRatio:P1}", context.Style.NegativeColor, context.Style,
                        defenseRatioHint));
                    if (incoming.HasCompleteHpTimeline)
                        statistics.AddChild(IncomingStat(
                            ModLocalization.Get("analysis.selfHpLost", "Self-inflicted HP loss"),
                            $"{Format(incoming.SelfHpLost)} · {incoming.SelfHpLossRatio:P1}",
                            context.Style.WarningColor, context.Style));
                    card.AddChild(statistics);
                    if (incoming.Sources.Count > 0)
                    {
                        card.AddChild(Label(ModLocalization.Get("analysis.incomingHpSources",
                            "Top HP-loss sources"), context.Style, true));
                        var maximum = Math.Max(1m, incoming.Sources.Max(source => source.HpLost));
                        foreach (var source in incoming.Sources.Take(5))
                            card.AddChild(Meter(source.Name,
                                $"{Format(source.HpLost)} · {source.Share:P1} · ×{source.Occurrences}",
                                source.HpLost, maximum, context.Style.NegativeColor, context.Style,
                                Math.Max(22, context.Style.RowHeight - 8)));
                    }

                    return Surface(card, context.Style, accent);
                }), 205f + incoming.Sources.Take(5).Count() * Math.Max(26, context.Style.RowHeight - 4)));
            }

            var timeline = Timeline(snapshot);
            var events = new List<CombatTimelineEvent>(Math.Min(200, timeline.Count));
            for (var index = timeline.Count - 1; index >= 0; index--)
            {
                var timelineEvent = timeline[index];
                if (!IsReceivedDamageEvent(timelineEvent))
                    continue;
                events.Add(timelineEvent);
            }

            var historyTitle = ModLocalization.Format("dashboard.incomingHistory.count",
                "Incoming history ({0})", events.Count);
            rows.Add(new(new("__history_title",
                string.Join('\u001e', VisualStyleFingerprint(context.Style), historyTitle, _historyExpanded),
                () => IncomingHistoryHeader(historyTitle, _historyExpanded, context.Style, ToggleHistory)),
                context.Style.RowHeight + 7f));
            IReadOnlyList<CombatTimelineEvent> visibleEvents;
            if (_historyExpanded)
                visibleEvents = PageItems(events, context, 200, 50);
            else
            {
                HidePagingControls();
                visibleEvents = [];
            }

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var timelineEvent in visibleEvents)
            {
                var fingerprint = string.Join('\u001e', VisualStyleFingerprint(context.Style),
                    timelineEvent.TurnIndex, timelineEvent.Target?.DisplayName,
                    timelineEvent.Source?.DisplayName, timelineEvent.DisplayText, timelineEvent.Value,
                    timelineEvent.Damage?.HpLost, timelineEvent.Damage?.BlockedAmount);
                rows.Add(new(new($"history:{timelineEvent.EventId}", fingerprint, () =>
                {
                    var hpLost = SnapshotStatistics.EffectiveHpLost(timelineEvent);
                    var blockedAmount = timelineEvent.Damage?.BlockedAmount ?? 0m;
                    var value = ReceivedHistoryValue(hpLost, blockedAmount);
                    var source = timelineEvent.Source?.DisplayName ?? timelineEvent.DisplayText;
                    var target = timelineEvent.Target?.DisplayName ?? string.Empty;
                    var descriptionText = ModLocalization.Format("dashboard.received.historyDirection",
                        "{0} → {1}", source, target);
                    var historyRow =
                        new HBoxContainer { CustomMinimumSize = new(0, context.Style.RowHeight - 3) };
                    historyRow.AddChild(Label($"T{timelineEvent.TurnIndex}", context.Style, true));
                    var description = TruncatedLabel(descriptionText, context.Style);
                    historyRow.AddChild(description);
                    var amountLabel = Label(value, context.Style, false, context.Style.FontSize + 1);
                    amountLabel.Modulate = ColorOf(hpLost > 0m
                        ? context.Style.NegativeColor
                        : context.Style.PositiveColor);
                    amountLabel.HorizontalAlignment = HorizontalAlignment.Right;
                    historyRow.AddChild(amountLabel);
                    return Surface(historyRow, context.Style, hpLost > 0m
                        ? context.Style.NegativeColor
                        : context.Style.PositiveColor, 5);
                }), context.Style.RowHeight + 7f));
            }

            ReconcileRows(rows.Select(row => row.Row));
            Status.Text = ModLocalization.Format("dashboard.received.status", "{0} · incoming damage history",
                snapshot.EncounterName);
        }

        private static Button IncomingHistoryHeader(
            string title,
            bool expanded,
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
                TooltipText = ModLocalization.Get(expanded ? "overlay.collapse" : "overlay.expand",
                    expanded ? "Collapse" : "Expand"),
            };
            DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Subtle, true, style);
            DashboardIcons.Apply(button, expanded ? DashboardIcon.Collapse : DashboardIcon.Expand, 17);
            button.Pressed += toggle;
            return button;
        }

        private void ToggleHistory()
        {
            _historyExpanded = !_historyExpanded;
            if (_lastContext == null)
                return;
            var scrollPosition = Scroll.ScrollVertical;
            Refresh(_lastContext);
            Callable.From(() => Scroll.ScrollVertical = scrollPosition).CallDeferred();
        }

        internal static bool IsReceivedDamageEvent(CombatTimelineEvent timelineEvent)
        {
            ArgumentNullException.ThrowIfNull(timelineEvent);
            if (timelineEvent.Kind is not (CombatTimelineKind.Damage or CombatTimelineKind.HpLoss or
                    CombatTimelineKind.Execution) ||
                timelineEvent.Target?.Kind is not (AnalyticsEntityKind.Player or AnalyticsEntityKind.Summon))
                return false;
            return SnapshotStatistics.EffectiveHpLost(timelineEvent) > 0m ||
                   timelineEvent.Damage?.BlockedAmount > 0m;
        }

        private static Control IncomingStat(
            string caption,
            string value,
            string color,
            DashboardStyleDefinition style,
            string? tooltip = null)
        {
            var box = new VBoxContainer
            {
                CustomMinimumSize = new(150f, 42f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? caption,
            };
            box.AddThemeConstantOverride("separation", -2);
            var valueLabel = TruncatedLabel(value, style, false, style.FontSize + 2);
            valueLabel.Modulate = ColorOf(color);
            if (tooltip != null)
                valueLabel.TooltipText = tooltip;
            box.AddChild(valueLabel);
            var captionLabel = TruncatedLabel(caption, style, true, Math.Max(9, style.FontSize - 2));
            if (tooltip != null)
                captionLabel.TooltipText = tooltip;
            box.AddChild(captionLabel);
            return box;
        }

        private static string ReceivedHistoryValue(decimal hpLost, decimal blocked)
        {
            if (hpLost > 0m && blocked > 0m)
                return ModLocalization.Format("dashboard.received.historyValue",
                    "Lost {0} HP · Blocked {1}", Format(hpLost), Format(blocked));
            if (hpLost > 0m)
                return ModLocalization.Format("dashboard.received.historyHpLost", "Lost {0} HP", Format(hpLost));
            return ModLocalization.Format("dashboard.received.historyBlocked", "Blocked {0}", Format(blocked));
        }
    }

    internal abstract class FilteredTimelineRenderer : DashboardRendererBase
    {
        private readonly HashSet<int> _knownTurnIndexes = [];
        private readonly DashboardDropdown _player = new();

        private readonly LineEdit _search = new()
        {
            PlaceholderText = ModLocalization.Get("dashboard.search", "Search events"),
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        private readonly DashboardDropdown _turn = new();
        private string? _filterCombatId;
        private int _indexedTimelineCount;
        private CombatTimelineEvent? _indexedTimelineLast;
        private DashboardRenderContext? _lastContext;
        private string[] _playerKeys = [];
        private int[] _turnIndexes = [];
        private bool _updatingFilters;

        protected FilteredTimelineRenderer()
        {
            _turn.CustomMinimumSize = new(112, 0);
            _player.CustomMinimumSize = new(132, 0);
            _turn.ApplyStyle(density: DashboardControlDensity.Compact);
            _player.ApplyStyle(density: DashboardControlDensity.Compact);
            DashboardControlTheme.ApplySearch(_search, density: DashboardControlDensity.Compact);
            Toolbar.AddChild(_turn);
            Toolbar.AddChild(_player);
            Toolbar.AddChild(_search);
            _turn.ItemSelected += _ => RefreshFromFilter();
            _player.ItemSelected += _ => RefreshFromFilter();
            _search.TextChanged += _ => RefreshFromFilter();
        }

        protected override bool ReconcileRowsOnRefresh => true;

        protected virtual bool CollapseSemanticMoves => false;

        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            return new(DashboardDataComponents.Timeline);
        }

        protected override void Render(DashboardRenderContext context)
        {
            _lastContext = context;
            var snapshot = context.Snapshot;
            if (snapshot == null || Timeline(snapshot).Count == 0)
            {
                RebuildFilters(null);
                ReconcileRows(
                [
                    new("__empty", EmptyRowFingerprint(context),
                        () => CreateEmptyRow(context)),
                ]);
                Status.Text = string.Empty;
                return;
            }

            RebuildFilters(snapshot);
            var search = _search.Text.Trim();
            var turn = SelectedTurn();
            var playerKey = SelectedPlayerKey();
            var selected = SelectMatchingEvents(
                Timeline(snapshot),
                turn,
                playerKey,
                search,
                DashboardPresentation.HistoryItemLimit(context.Parameters, 1500));
            var separation = DashboardPresentation.SingleLine(context.Parameters) ? 2f : 4f;
            ReconcileVirtualRows(selected.Select(timelineEvent => new ReconciledRow(
                    timelineEvent.EventId,
                    RowFingerprint(timelineEvent, context.Style),
                    () => CreateRow(timelineEvent, context.Style))),
                VirtualRowHeight(context.Style) + separation);
            Status.Text = ModLocalization.Format("dashboard.timeline.status", "{0} events · {1} rounds",
                selected.Count, snapshot.RoundCount);
        }

        protected abstract bool Include(CombatTimelineEvent timelineEvent);
        protected abstract string RowText(CombatTimelineEvent timelineEvent);

        protected virtual IReadOnlyList<CombatTimelineEvent> FinalizeSelectedEvents(
            IReadOnlyList<CombatTimelineEvent> timelineEvents)
        {
            return timelineEvents;
        }

        protected virtual Control CreateRow(CombatTimelineEvent timelineEvent, DashboardStyleDefinition style)
        {
            return WrappedLabel(RowText(timelineEvent), style,
                timelineEvent.Kind == CombatTimelineKind.DamageModifier);
        }

        protected virtual float VirtualRowHeight(DashboardStyleDefinition style)
        {
            return style.RowHeight;
        }

        protected virtual string RowFingerprint(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style)
        {
            var details = string.Join('\u001f', timelineEvent.Details.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value}"));
            return string.Join("\u001e",
                timelineEvent.Sequence,
                timelineEvent.ParentEventId,
                timelineEvent.TurnIndex,
                timelineEvent.Round,
                timelineEvent.Kind,
                timelineEvent.Phase,
                timelineEvent.ActionId,
                timelineEvent.DisplayText,
                timelineEvent.Actor?.DisplayName,
                timelineEvent.Target?.DisplayName,
                timelineEvent.Source?.DisplayName,
                timelineEvent.Value,
                timelineEvent.Damage?.RequestedAmount,
                timelineEvent.Damage?.ModifiedAmount,
                timelineEvent.Damage?.BlockedAmount,
                timelineEvent.Damage?.HpLost,
                timelineEvent.Damage?.Contributions.Count,
                details,
                VisualStyleFingerprint(style));
        }

        private static string SearchText(CombatTimelineEvent timelineEvent)
        {
            return string.Join(' ', timelineEvent.ActionId, timelineEvent.DisplayText,
                timelineEvent.Actor?.DisplayName, timelineEvent.Target?.DisplayName,
                timelineEvent.Source?.DisplayName, timelineEvent.Source?.ModelId,
                string.Join(' ', timelineEvent.Details.Select(detail => $"{detail.Key} {detail.Value}")));
        }

        private IReadOnlyList<CombatTimelineEvent> SelectMatchingEvents(
            IReadOnlyList<CombatTimelineEvent> timelineEvents,
            int? turn,
            string? playerKey,
            string search,
            int limit)
        {
            var selected = new List<CombatTimelineEvent>(Math.Min(limit, timelineEvents.Count));
            for (var index = timelineEvents.Count - 1; index >= 0 && selected.Count < limit; index--)
            {
                var timelineEvent = timelineEvents[index];
                if (!Include(timelineEvent) ||
                    (CollapseSemanticMoves && timelineEvent.ActionId == "card.move" &&
                     HasSemanticCompanion(timelineEvents, index, timelineEvent)) ||
                    (turn != null && timelineEvent.TurnIndex != turn) ||
                    (playerKey != null && !IsPlayerEvent(timelineEvent, playerKey)) ||
                    (search.Length > 0 &&
                     !SearchText(timelineEvent).Contains(search, StringComparison.OrdinalIgnoreCase)))
                    continue;
                selected.Add(timelineEvent);
            }

            selected.Reverse();
            return FinalizeSelectedEvents(selected);
        }

        private static bool HasSemanticCompanion(
            IReadOnlyList<CombatTimelineEvent> events,
            int index,
            CombatTimelineEvent cardMove)
        {
            var previous = cardMove.Details.GetValueOrDefault("previous_pile");
            var current = cardMove.Details.GetValueOrDefault("current_pile");
            var semanticAction = (previous, current) switch
            {
                ("Draw", "Hand") => "card.draw",
                ("Hand", "Discard") => "card.discard",
                (_, "Exhaust") => "card.exhaust",
                _ => null,
            };
            if (semanticAction == null)
                return false;
            for (var candidateIndex = index + 1;
                 candidateIndex < events.Count && candidateIndex <= index + 2;
                 candidateIndex++)
            {
                var candidate = events[candidateIndex];
                if (candidate.ActionId == semanticAction && candidate.Actor?.Key == cardMove.Actor?.Key &&
                    candidate.Source?.Key == cardMove.Source?.Key)
                    return true;
            }

            return false;
        }

        private void RebuildFilters(CombatSnapshot? snapshot)
        {
            var timeline = snapshot == null ? [] : Timeline(snapshot);
            if (snapshot == null ||
                !string.Equals(_filterCombatId, snapshot.CombatId, StringComparison.Ordinal) ||
                (_indexedTimelineCount > 0 &&
                 (timeline.Count < _indexedTimelineCount ||
                  !ReferenceEquals(timeline[_indexedTimelineCount - 1], _indexedTimelineLast))))
            {
                _filterCombatId = snapshot?.CombatId;
                _indexedTimelineCount = 0;
                _indexedTimelineLast = null;
                _knownTurnIndexes.Clear();
            }

            for (var index = _indexedTimelineCount; index < timeline.Count; index++)
                _knownTurnIndexes.Add(timeline[index].TurnIndex);
            _indexedTimelineCount = timeline.Count;
            _indexedTimelineLast = timeline.Count == 0 ? null : timeline[^1];
            var turns = _knownTurnIndexes.Order().ToArray();
            var players =
                snapshot?.Players.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture).ToArray() ?? [];
            var playerKeys = players.Select(item => item.PlayerKey).ToArray();
            var selectedTurn = SelectedTurn();
            var selectedPlayer = SelectedPlayerKey();
            _updatingFilters = true;
            if (_turn.ItemCount == 0 || !_turnIndexes.SequenceEqual(turns))
            {
                _turn.Clear();
                _turn.AddLocalizedItem("dashboard.allTurns", "All turns");
                foreach (var turn in turns)
                    if (turn <= 0)
                        _turn.AddLocalizedItem("dashboard.setup", "Setup");
                    else
                        _turn.AddLocalizedItem("dashboard.turn", "Turn {0}", turn);
                _turnIndexes = turns;
            }

            if (_player.ItemCount == 0 || !_playerKeys.SequenceEqual(playerKeys))
            {
                _player.Clear();
                _player.AddLocalizedItem("overlay.allPlayers", "All players");
                foreach (var player in players)
                    _player.AddItem(player.DisplayName);
                _playerKeys = playerKeys;
            }

            if (_turn.ItemCount > 0)
                _turn.Select(selectedTurn == null
                    ? 0
                    : Math.Max(0, Array.IndexOf(_turnIndexes, selectedTurn.Value) + 1));
            if (_player.ItemCount > 0)
                _player.Select(selectedPlayer == null
                    ? 0
                    : Math.Max(0, Array.IndexOf(_playerKeys, selectedPlayer) + 1));
            _updatingFilters = false;
        }

        private int? SelectedTurn()
        {
            var index = _turn.Selected - 1;
            return index >= 0 && index < _turnIndexes.Length ? _turnIndexes[index] : null;
        }

        private string? SelectedPlayerKey()
        {
            var index = _player.Selected - 1;
            return index >= 0 && index < _playerKeys.Length ? _playerKeys[index] : null;
        }

        private void RefreshFromFilter()
        {
            if (!_updatingFilters && _lastContext != null)
                Refresh(_lastContext);
        }

        private static bool IsPlayerEvent(CombatTimelineEvent timelineEvent, string playerKey)
        {
            return timelineEvent.Actor?.Key == playerKey || timelineEvent.Target?.Key == playerKey ||
                   timelineEvent.Damage?.AttributionShares?.Any(share => share.Contributor.Key == playerKey) == true;
        }
    }

    internal sealed class TimelineRenderer : FilteredTimelineRenderer
    {
        private const int MaxVisibleDepth = 6;
        private readonly HashSet<string> _autoCollapseDecided = new(StringComparer.Ordinal);
        private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
        private Dictionary<string, string[]> _ancestorsByEventId = new(StringComparer.Ordinal);
        private Dictionary<string, CombatTimelineEvent[]> _childrenByParentId = new(StringComparer.Ordinal);
        private Dictionary<string, int> _descendantsByEventId = new(StringComparer.Ordinal);
        private Dictionary<string, CombatTimelineEvent> _eventById = new(StringComparer.Ordinal);
        private float _kindColumnWidth = 86f;
        private DashboardRenderContext? _lastContext;
        private string? _snapshotId;

        protected override bool CollapseSemanticMoves => true;

        protected override void Render(DashboardRenderContext context)
        {
            var snapshotId = context.Snapshot?.CombatId;
            if (!string.Equals(snapshotId, _snapshotId, StringComparison.Ordinal))
            {
                _snapshotId = snapshotId;
                _collapsed.Clear();
                _autoCollapseDecided.Clear();
            }

            _lastContext = context;
            base.Render(context);
        }

        protected override bool Include(CombatTimelineEvent timelineEvent)
        {
            return true;
        }

        protected override string RowText(CombatTimelineEvent timelineEvent)
        {
            var extra = timelineEvent.IsExtraTurn
                ? $" [{ModLocalization.Get("analysis.extraTurn", "Extra turn")}]"
                : string.Empty;
            var origin = CausalOrigin(timelineEvent);
            return $"{DashboardLocalization.TimelineDescription(timelineEvent, origin.Source, origin.Actor)}{extra}";
        }

        protected override IReadOnlyList<CombatTimelineEvent> FinalizeSelectedEvents(
            IReadOnlyList<CombatTimelineEvent> timelineEvents)
        {
            var window = timelineEvents.ToArray();
            BuildTreeState(window);
            return OrderCausally(window).Where(IsVisible).ToArray();
        }

        protected override Control CreateRow(CombatTimelineEvent timelineEvent, DashboardStyleDefinition style)
        {
            var row = new HBoxContainer { CustomMinimumSize = new(0, style.RowHeight + 2) };
            row.AddThemeConstantOverride("separation", 7);

            var color = timelineEvent.Kind switch
            {
                CombatTimelineKind.Damage or CombatTimelineKind.Execution => style.NegativeColor,
                CombatTimelineKind.Block or CombatTimelineKind.Healing => style.PositiveColor,
                CombatTimelineKind.Turn or CombatTimelineKind.Phase => Accent(style, 1),
                CombatTimelineKind.CardPlay or CombatTimelineKind.CardDraw => Accent(style, 3),
                _ => style.SecondaryTextColor,
            };
            row.AddChild(new ColorRect
            {
                Color = ColorOf(color) with { A = 0.78f },
                CustomMinimumSize = new(3, style.RowHeight - 8),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            var position = Label($"R{timelineEvent.Round}/T{timelineEvent.TurnIndex}", style, true,
                Math.Max(9, style.FontSize - 2));
            position.CustomMinimumSize = new(68f, 0f);
            position.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(position);
            var kind = Label(DashboardLocalization.TimelineKind(timelineEvent.Kind), style, false,
                Math.Max(10, style.FontSize - 1));
            kind.CustomMinimumSize = new(_kindColumnWidth, 0f);
            kind.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            kind.ClipText = true;
            kind.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            kind.TooltipText = kind.Text;
            kind.Modulate = ColorOf(color);
            kind.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(kind);

            var ancestors = _ancestorsByEventId.GetValueOrDefault(timelineEvent.EventId) ?? [];
            if (ancestors.Length > 0)
                row.AddChild(new TimelineHierarchyGuide(ancestors.Select(ancestor =>
                    ColorOf(Accent(style, StableColorIndex(ancestor)))).ToArray(), MaxVisibleDepth));
            if (ancestors.Length > MaxVisibleDepth)
            {
                var depth = Label($"L{ancestors.Length}", style, true, Math.Max(9, style.FontSize - 2));
                depth.CustomMinimumSize = new(30f, 0f);
                depth.HorizontalAlignment = HorizontalAlignment.Center;
                depth.VerticalAlignment = VerticalAlignment.Center;
                depth.Modulate = ColorOf(style.SecondaryTextColor);
                row.AddChild(depth);
            }

            var descendantCount = _descendantsByEventId.GetValueOrDefault(timelineEvent.EventId);
            var hasChildren = descendantCount > 0;
            if (hasChildren)
            {
                var collapsed = _collapsed.Contains(timelineEvent.EventId);
                var toggle = new Button
                {
                    TooltipText = ModLocalization.Format(collapsed
                            ? "timeline.branch.expand"
                            : "timeline.branch.collapse",
                        collapsed ? "Expand {0} linked events" : "Collapse {0} linked events", descendantCount),
                };
                DashboardControlTheme.ApplyIconButton(toggle, DashboardButtonKind.Subtle, style, true);
                DashboardIcons.ApplyIconOnly(toggle,
                    collapsed ? DashboardIcon.Expand : DashboardIcon.Collapse, 17);
                toggle.Pressed += () => ToggleBranch(timelineEvent.EventId);
                row.AddChild(toggle);
            }
            else
            {
                row.AddChild(new Control
                { CustomMinimumSize = new(40f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore });
            }

            var label = TruncatedLabel(RowText(timelineEvent), style,
                timelineEvent.Kind == CombatTimelineKind.DamageModifier);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);
            if (_collapsed.Contains(timelineEvent.EventId))
            {
                var hidden = Label($"+{descendantCount}", style, true, Math.Max(9, style.FontSize - 2));
                hidden.CustomMinimumSize = new(40f, 0f);
                hidden.HorizontalAlignment = HorizontalAlignment.Right;
                hidden.VerticalAlignment = VerticalAlignment.Center;
                hidden.Modulate = ColorOf(style.SecondaryTextColor);
                row.AddChild(hidden);
            }

            _eventById.TryGetValue(timelineEvent.ParentEventId ?? string.Empty, out var parent);
            var origin = CausalOrigin(timelineEvent);
            DashboardTooltip.Set(row,
                DashboardLocalization.TimelineTooltip(timelineEvent, parent, origin.Source, origin.Actor));
            return row;
        }

        protected override float VirtualRowHeight(DashboardStyleDefinition style)
        {
            return style.RowHeight + 2f;
        }

        protected override string RowFingerprint(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style)
        {
            return string.Join("\u001e",
                base.RowFingerprint(timelineEvent, style),
                _collapsed.Contains(timelineEvent.EventId),
                _descendantsByEventId.GetValueOrDefault(timelineEvent.EventId),
                _kindColumnWidth,
                string.Join('\u001f', _ancestorsByEventId.GetValueOrDefault(timelineEvent.EventId) ?? []));
        }

        private (EntityDescriptor? Actor, SourceDescriptor? Source) CausalOrigin(
            CombatTimelineEvent timelineEvent)
        {
            EntityDescriptor? actor = null;
            var parentId = timelineEvent.ParentEventId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (parentId != null && visited.Add(parentId) && _eventById.TryGetValue(parentId, out var parent))
            {
                actor ??= parent.Actor;
                if (parent.Source != null && parent.Source.Key != timelineEvent.Source?.Key)
                    return (actor, parent.Source);
                parentId = parent.ParentEventId;
            }

            return (actor, null);
        }

        private void BuildTreeState(CombatTimelineEvent[] timelineEvents)
        {
            var style = _lastContext?.Style;
            var fontSize = Math.Max(10, (style?.FontSize ?? 15) - 1);
            var font = DashboardControlTheme.BodyFont;
            _kindColumnWidth = Math.Clamp(
                timelineEvents.Select(item => DashboardLocalization.TimelineKind(item.Kind))
                    .Distinct(StringComparer.CurrentCulture)
                    .Select(text => font.GetStringSize(text, fontSize: fontSize).X + 12f)
                    .DefaultIfEmpty(86f)
                    .Max(),
                86f,
                160f);
            _eventById = timelineEvents.ToDictionary(item => item.EventId, StringComparer.Ordinal);
            _childrenByParentId = timelineEvents
                .Where(item => item.ParentEventId != null && _eventById.ContainsKey(item.ParentEventId))
                .GroupBy(item => item.ParentEventId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderBy(item => item.Sequence).ToArray(),
                    StringComparer.Ordinal);
            _ancestorsByEventId = new(timelineEvents.Length, StringComparer.Ordinal);
            _descendantsByEventId = new(timelineEvents.Length, StringComparer.Ordinal);
            foreach (var timelineEvent in timelineEvents)
            {
                ResolveAncestors(timelineEvent, []);
                CountDescendants(timelineEvent.EventId, []);
            }

            foreach (var timelineEvent in timelineEvents.Where(item => item.Kind == CombatTimelineKind.Damage &&
                                                                       _childrenByParentId.TryGetValue(item.EventId,
                                                                           out var children) &&
                                                                       children.All(child =>
                                                                           child.Kind == CombatTimelineKind
                                                                               .DamageModifier)))
                if (_autoCollapseDecided.Add(timelineEvent.EventId))
                    _collapsed.Add(timelineEvent.EventId);
        }

        private string[] ResolveAncestors(CombatTimelineEvent timelineEvent, HashSet<string> visiting)
        {
            if (_ancestorsByEventId.TryGetValue(timelineEvent.EventId, out var cached))
                return cached;
            if (!visiting.Add(timelineEvent.EventId) || timelineEvent.ParentEventId == null ||
                !_eventById.TryGetValue(timelineEvent.ParentEventId, out var parent))
                return _ancestorsByEventId[timelineEvent.EventId] = [];
            var ancestors = ResolveAncestors(parent, visiting).Append(parent.EventId).ToArray();
            visiting.Remove(timelineEvent.EventId);
            return _ancestorsByEventId[timelineEvent.EventId] = ancestors;
        }

        private int CountDescendants(string eventId, HashSet<string> visiting)
        {
            if (_descendantsByEventId.TryGetValue(eventId, out var cached))
                return cached;
            if (!visiting.Add(eventId) || !_childrenByParentId.TryGetValue(eventId, out var children))
                return _descendantsByEventId[eventId] = 0;
            var count = children.Sum(child => 1 + CountDescendants(child.EventId, visiting));
            visiting.Remove(eventId);
            return _descendantsByEventId[eventId] = count;
        }

        private bool IsVisible(CombatTimelineEvent timelineEvent)
        {
            return !_ancestorsByEventId.GetValueOrDefault(timelineEvent.EventId, [])
                .Any(_collapsed.Contains);
        }

        private List<CombatTimelineEvent> OrderCausally(
            CombatTimelineEvent[] timelineEvents)
        {
            var earliestByEventId = new Dictionary<string, long>(timelineEvents.Length, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<CombatTimelineEvent>(timelineEvents.Length);
            var roots = timelineEvents
                .Where(item => item.ParentEventId == null || !_eventById.ContainsKey(item.ParentEventId))
                .OrderBy(item => EarliestSequence(item.EventId, earliestByEventId, []))
                .ThenBy(item => item.Sequence);
            foreach (var root in roots)
                AppendBranch(root, ordered, emitted, earliestByEventId);
            foreach (var remaining in timelineEvents.Where(item => !emitted.Contains(item.EventId))
                         .OrderBy(item => item.Sequence))
                AppendBranch(remaining, ordered, emitted, earliestByEventId);
            return ordered;
        }

        private long EarliestSequence(
            string eventId,
            Dictionary<string, long> cache,
            HashSet<string> visiting)
        {
            if (cache.TryGetValue(eventId, out var cached))
                return cached;
            if (!_eventById.TryGetValue(eventId, out var timelineEvent) || !visiting.Add(eventId))
                return long.MaxValue;
            var earliest = timelineEvent.Sequence;
            if (_childrenByParentId.TryGetValue(eventId, out var children))
                earliest = children.Aggregate(earliest,
                    (current, child) => Math.Min(current, EarliestSequence(child.EventId, cache, visiting)));
            visiting.Remove(eventId);
            return cache[eventId] = earliest;
        }

        private void AppendBranch(
            CombatTimelineEvent timelineEvent,
            List<CombatTimelineEvent> ordered,
            HashSet<string> emitted,
            Dictionary<string, long> earliestByEventId)
        {
            if (!emitted.Add(timelineEvent.EventId))
                return;
            ordered.Add(timelineEvent);
            if (!_childrenByParentId.TryGetValue(timelineEvent.EventId, out var children))
                return;
            foreach (var child in children.OrderBy(item =>
                         EarliestSequence(item.EventId, earliestByEventId, [])).ThenBy(item => item.Sequence))
                AppendBranch(child, ordered, emitted, earliestByEventId);
        }

        private void ToggleBranch(string eventId)
        {
            if (!_collapsed.Add(eventId))
                _collapsed.Remove(eventId);
            if (_lastContext == null)
                return;
            var scrollPosition = Scroll.ScrollVertical;
            Refresh(_lastContext);
            Callable.From(() => Scroll.ScrollVertical = scrollPosition).CallDeferred();
        }

        private static int StableColorIndex(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var character in value)
                hash = (hash ^ character) * prime;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    internal sealed class DamageBreakdownRenderer : DashboardRendererBase
    {
        private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

        private readonly LineEdit _search = new()
        {
            PlaceholderText = ModLocalization.Get("dashboard.damage.search", "Search source, target or modifier"),
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        private DashboardRenderContext? _lastContext;

        internal DamageBreakdownRenderer()
        {
            DashboardControlTheme.ApplySearch(_search, density: DashboardControlDensity.Compact);
            Toolbar.AddChild(_search);
            _search.TextChanged += _ => RefreshLastPreservingScroll();
        }

        protected override bool ReconcileRowsOnRefresh => true;

        public override DashboardDataRequirements GetDataRequirements(
            DashboardDataScope scope,
            IReadOnlyDictionary<string, string> parameters)
        {
            return new(DashboardDataComponents.Timeline);
        }

        protected override void Render(DashboardRenderContext context)
        {
            _lastContext = context;
            var snapshot = context.Snapshot;
            if (snapshot == null)
            {
                ReconcileRows(
                [
                    new("__empty", EmptyRowFingerprint(context),
                        () => CreateEmptyRow(context)),
                ]);
                Status.Text = string.Empty;
                return;
            }

            var search = _search.Text.Trim();
            var timeline = Timeline(snapshot);
            var historyLimit = DashboardPresentation.HistoryItemLimit(context.Parameters, 500);
            var selectedEvents = new List<CombatTimelineEvent>(Math.Min(historyLimit, timeline.Count));
            for (var index = timeline.Count - 1; index >= 0 && selectedEvents.Count < historyLimit; index--)
            {
                var timelineEvent = timeline[index];
                if (timelineEvent.Damage == null ||
                    (search.Length > 0 && !DamageSearchText(timelineEvent)
                        .Contains(search, StringComparison.OrdinalIgnoreCase)))
                    continue;
                selectedEvents.Add(timelineEvent);
            }

            selectedEvents.Reverse();
            var events = selectedEvents.ToArray();
            ReconcileVariableVirtualRows(events.Select(timelineEvent =>
            {
                var expanded = _expanded.Contains(timelineEvent.EventId);
                var row = new ReconciledRow(timelineEvent.EventId,
                    DamageRowFingerprint(timelineEvent, context.Style, expanded), () =>
                    {
                        var group = new VBoxContainer();
                        group.AddThemeConstantOverride("separation", 5);
                        group.AddChild(DamageEventSummary(timelineEvent, context.Style, expanded, () =>
                        {
                            if (!_expanded.Add(timelineEvent.EventId))
                                _expanded.Remove(timelineEvent.EventId);
                            RefreshLastPreservingScroll();
                        }));
                        if (expanded)
                            group.AddChild(DamageEventDetails(timelineEvent, context.Style));
                        return group;
                    });
                var contributionCount = timelineEvent.Damage!.Contributions.Count(item =>
                    item.RawContribution != 0m);
                var estimatedHeight = expanded
                    ? context.Style.RowHeight + 210f + contributionCount * 24f
                    : context.Style.RowHeight + 7f;
                return new VariableReconciledRow(row, estimatedHeight);
            }));

            Status.Text = ModLocalization.Format("dashboard.damage.status",
                "{0} damage events with causal analysis", events.Length);
        }

        private static string DamageRowFingerprint(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style,
            bool expanded)
        {
            var damage = timelineEvent.Damage!;
            var contributions = string.Join('\u001f', damage.Contributions.Select(item =>
                $"{item.Source.DisplayName}:{item.RawContribution}:{item.EffectiveContribution}"));
            return string.Join("\u001e",
                timelineEvent.Sequence,
                timelineEvent.DisplayText,
                timelineEvent.Actor?.DisplayName,
                timelineEvent.Target?.DisplayName,
                timelineEvent.Source?.DisplayName,
                timelineEvent.Value,
                damage.RequestedAmount,
                damage.ModifiedAmount,
                damage.BlockedAmount,
                damage.HpLost,
                expanded,
                contributions,
                VisualStyleFingerprint(style));
        }

        private static PanelContainer DamageEventSummary(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style,
            bool expanded,
            Action toggle)
        {
            var panel = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            var normal = DamageEventStyle(style, false);
            var hover = DamageEventStyle(style, true);
            panel.AddThemeStyleboxOverride("panel", normal);
            panel.MouseEntered += () => panel.AddThemeStyleboxOverride("panel", hover);
            panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", normal);
            panel.GuiInput += input =>
            {
                if (input is InputEventMouseButton
                    {
                        ButtonIndex: MouseButton.Left, Pressed: true,
                    })
                    toggle();
            };
            var damage = timelineEvent.Damage!;
            var row = new HBoxContainer { CustomMinimumSize = new(0f, style.RowHeight + 3f) };
            row.AddThemeConstantOverride("separation", 8);
            var chevron = DashboardIcons.View(expanded ? DashboardIcon.Collapse : DashboardIcon.Expand, 18f,
                ColorOf(style.SecondaryTextColor));
            row.AddChild(chevron);
            var turn = Label($"T{timelineEvent.TurnIndex}", style, true, Math.Max(10, style.FontSize - 1));
            turn.CustomMinimumSize = new(38f, 0f);
            turn.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(turn);
            var source = timelineEvent.Source?.DisplayName ?? timelineEvent.Actor?.DisplayName ??
                timelineEvent.DisplayText;
            var target = timelineEvent.Target?.DisplayName ?? "—";
            var identity = TruncatedLabel($"{source}  →  {target}", style, false, style.FontSize + 1);
            identity.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(identity);
            var flow = Label($"{Format(damage.RequestedAmount)} → {Format(damage.ModifiedAmount)}", style, true,
                Math.Max(10, style.FontSize - 1));
            flow.Text = ModLocalization.Format("dashboard.damage.flow", "Requested {0} → modified {1}",
                Format(damage.RequestedAmount), Format(damage.ModifiedAmount));
            flow.CustomMinimumSize = new(170f, 0f);
            flow.HorizontalAlignment = HorizontalAlignment.Right;
            flow.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(flow);
            var result = Label(ModLocalization.Format("dashboard.damage.actual", "Actual {0}",
                Format(damage.HpLost + damage.BlockedAmount)), style, false, style.FontSize + 1);
            result.CustomMinimumSize = new(90f, 0f);
            result.HorizontalAlignment = HorizontalAlignment.Right;
            result.VerticalAlignment = VerticalAlignment.Center;
            result.Modulate = ColorOf(style.NegativeColor);
            row.AddChild(result);
            var modifierCount = damage.Contributions.Count(item =>
                DamageContributionSemantics.GetRole(item) == DamageContributionRole.Modifier &&
                item.RawContribution != 0m);
            var count = Label(ModLocalization.Format("dashboard.damage.modifierCount", "{0} modifiers", modifierCount),
                style, true, Math.Max(9, style.FontSize - 2));
            count.CustomMinimumSize = new(78f, 0f);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(count);
            panel.AddChild(row);
            DashboardTooltip.Set(panel, DashboardLocalization.TimelineTooltip(timelineEvent));
            return panel;
        }

        private static Control DamageEventDetails(
            CombatTimelineEvent timelineEvent,
            DashboardStyleDefinition style)
        {
            var damage = timelineEvent.Damage!;
            var details = new VBoxContainer();
            details.AddThemeConstantOverride("separation", 8);
            details.AddChild(WrappedLabel(ModLocalization.Get("dashboard.damage.stageHint",
                    "Requested is the initial amount; modified is after effects; actual is HP lost plus Block absorbed."),
                style, true, Math.Max(10, style.FontSize - 1)));
            var stages = new DashboardBarChart();
            stages.SetData([
                new(ModLocalization.Get("dashboard.damage.requestedLabel", "Requested"),
                    damage.RequestedAmount,
                    Accent(style, 1), Format(damage.RequestedAmount)),
                new(ModLocalization.Get("dashboard.damage.modifiedLabel", "Modified"),
                    damage.ModifiedAmount,
                    Accent(style, 3), Format(damage.ModifiedAmount)),
                new(ModLocalization.Get("dashboard.damage.hpLostLabel", "HP lost"), damage.HpLost,
                    style.NegativeColor, Format(damage.HpLost)),
                new(ModLocalization.Get("analysis.blocked", "Blocked"), damage.BlockedAmount,
                    style.PositiveColor, Format(damage.BlockedAmount)),
            ], Math.Max(11, style.FontSize - 1));
            details.AddChild(stages);
            var components = damage.Contributions.Where(item =>
                DamageContributionSemantics.GetRole(item) != DamageContributionRole.Settlement &&
                item.RawContribution != 0m).ToArray();
            if (components.Length > 0)
            {
                var heading = Label(ModLocalization.Get("dashboard.damage.components", "Damage components"), style,
                    true, Math.Max(10, style.FontSize - 1));
                details.AddChild(heading);
                var chart = new DashboardBarChart();
                chart.SetData(components.Select(contribution => new DashboardBarDatum(
                    $"{DashboardLocalization.ContributionStage(contribution.Stage)} · " +
                    DashboardLocalization.ContributionSource(contribution),
                    Math.Abs(contribution.RawContribution),
                    contribution.RawContribution > 0m ? style.PositiveColor : style.NegativeColor,
                    contribution.RawContribution > 0m
                        ? $"+{Format(contribution.RawContribution)}"
                        : Format(contribution.RawContribution))), Math.Max(10, style.FontSize - 2));
                details.AddChild(chart);
            }

            var settlements = damage.Contributions.Where(item =>
                DamageContributionSemantics.GetRole(item) == DamageContributionRole.Settlement &&
                item.RawContribution != 0m).ToArray();
            if (settlements.Length > 0)
            {
                details.AddChild(Label(ModLocalization.Get("dashboard.damage.settlements", "Outcome settlement"),
                    style, true, Math.Max(10, style.FontSize - 1)));
                var chart = new DashboardBarChart();
                chart.SetData(settlements.Select(contribution => new DashboardBarDatum(
                    DashboardLocalization.ContributionSource(contribution),
                    Math.Abs(contribution.RawContribution), style.SecondaryTextColor,
                    Format(contribution.RawContribution))), Math.Max(10, style.FontSize - 2));
                details.AddChild(chart);
            }

            if (damage.AttributionShares is not { Count: > 0 })
                return Surface(details, style, padding: 8);

            details.AddChild(Label(ModLocalization.Get("dashboard.damage.attribution", "Attribution allocation"),
                style, true, Math.Max(10, style.FontSize - 1)));
            var attribution = new HBoxContainer();
            attribution.AddThemeConstantOverride("separation", 12);
            foreach (var share in damage.AttributionShares)
                attribution.AddChild(CompactAttribution(share, style));
            details.AddChild(attribution);

            return Surface(details, style, padding: 8);
        }

        private static VBoxContainer CompactAttribution(
            DamageAttributionShare share,
            DashboardStyleDefinition style)
        {
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddThemeConstantOverride("separation", -2);
            box.AddChild(TruncatedLabel(share.Contributor.DisplayName, style, false, style.FontSize));
            box.AddChild(TruncatedLabel(ModLocalization.Format("dashboard.damage.attributionValue",
                    "Contribution {0} · weight {1:P1} · source {2}", Format(share.EffectiveContribution), share.Weight,
                    share.Source.DisplayName),
                style, true, Math.Max(9, style.FontSize - 2)));
            return box;
        }

        private static string DamageSearchText(CombatTimelineEvent timelineEvent)
        {
            var contributions = string.Join(' ', timelineEvent.Damage?.Contributions.Select(contribution =>
                $"{DashboardLocalization.ContributionStage(contribution.Stage)} " +
                $"{DashboardLocalization.ContributionSource(contribution)} " +
                $"{contribution.Source.DisplayName} {contribution.Source.ModelId}") ?? []);
            return string.Join(' ', DashboardLocalization.TimelineDescription(timelineEvent),
                timelineEvent.Actor?.DisplayName, timelineEvent.Target?.DisplayName,
                timelineEvent.Source?.DisplayName, timelineEvent.Source?.ModelId,
                contributions);
        }

        private void RefreshLastPreservingScroll()
        {
            if (_lastContext == null)
                return;
            var scrollPosition = Scroll.ScrollVertical;
            Refresh(_lastContext);
            Callable.From(() => Scroll.ScrollVertical = scrollPosition).CallDeferred();
        }

        private static StyleBoxFlat DamageEventStyle(DashboardStyleDefinition style, bool hover)
        {
            var background = ColorOf(hover ? style.HeaderColor : style.SurfaceColor);
            background.A *= hover ? 0.82f : 0.58f;
            return new()
            {
                BgColor = background,
                BorderColor = ColorOf(style.BorderColor) with { A = hover ? 0.48f : 0.18f },
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                ContentMarginLeft = 7f,
                ContentMarginTop = 2f,
                ContentMarginRight = 7f,
                ContentMarginBottom = 2f,
            };
        }
    }

    internal static class CharacterPortraitCache
    {
        private static readonly Dictionary<string, CharacterModel?> Models =
            new(StringComparer.OrdinalIgnoreCase);

        internal static Texture2D? Get(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;
            if (!Models.TryGetValue(characterId, out var model))
            {
                model = Resolve(characterId);
                Models[characterId] = model;
            }

            if (model == null)
                return null;
            var texture = model.IconTexture;
            if (GodotObject.IsInstanceValid(texture))
                return texture;
            var fallback = model.CharacterSelectIcon;
            return GodotObject.IsInstanceValid(fallback) ? fallback : null;
        }

        internal static Color? GetNameColor(string characterId)
        {
            return GetModel(characterId)?.NameColor;
        }

        internal static void Invalidate(string characterId)
        {
            Models.Remove(characterId);
        }

        private static CharacterModel? Resolve(string characterId)
        {
            try
            {
                var id = new ModelId(ModelDb.GetCategory(typeof(CharacterModel)), characterId);
                return ModelDb.GetByIdOrNull<CharacterModel>(id);
            }
            catch (Exception exception)
            {
                Main.Logger.Warn($"Could not resolve character portrait '{characterId}': {exception.Message}");
                return null;
            }
        }

        private static CharacterModel? GetModel(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;
            if (Models.TryGetValue(characterId, out var model))
                return model;
            model = Resolve(characterId);
            Models[characterId] = model;
            return model;
        }
    }
}
