// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class AnalysisCenter : PanelContainer
    {
        private const double SearchDelaySeconds = 0.18d;
        private const int SearchResultLimit = 120;

        private readonly Dictionary<(string RunId, string CombatId), string> _combatSearchText = [];
        private readonly HashSet<string> _expandedRunIds = new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _parameters = new(StringComparer.Ordinal)
        {
            ["metric_id"] = MetricIds.DamageContribution,
        };

        private string? _activeRunId;
        private string[] _appliedSearchTerms = [];
        private string _appliedSearchText = string.Empty;

        private Button _close = null!;
        private DashboardDropdown _dashboard = null!;
        private string[] _dashboardIds = [];
        private Button _deleteRun = null!;
        private DashboardDialogController _dialogs = null!;
        private bool _dirty = true;
        private int _historyHash;
        private int _historyRevision;
        private VBoxContainer _historyRows = null!;
        private Label _historySummary = null!;
        private int _liveHistorySignature;
        private DashboardDropdown _metric = null!;
        private Control _metricField = null!;
        private string[] _metricIds = [];
        private double _refreshDelay;
        private DashboardRegistry _registry = null!;
        private IDashboardRenderer? _renderer;
        private DashboardDefinition? _rendererDefinition;
        private MarginContainer _rendererHost = null!;
        private RunSnapshot[] _runs = [];
        private DashboardDropdown _scope = null!;
        private LineEdit _search = null!;
        private double _searchDelay = -1d;
        private string? _selectedCombatId;
        private RunSnapshot? _selectedRunData;
        private string? _selectedRunId;
        private Label _selectionMeta = null!;
        private Label _selectionTitle = null!;
        private Label _status = null!;

        internal void Initialize(DashboardRegistry registry)
        {
            _registry = registry;
        }

        public override void _Ready()
        {
            BuildUi();
            RebuildOptions();
            ApplyFullscreenGeometry();
            GetViewport().SizeChanged += OnViewportSizeChanged;
            SetProcess(true);
            Hide();
        }

        public override void _ExitTree()
        {
            GetViewport().SizeChanged -= OnViewportSizeChanged;
            DisposeRenderer();
        }

        public override void _Process(double delta)
        {
            if (!Visible)
                return;
            ProcessPendingSearch(delta);
            _refreshDelay -= delta;
            if (!_dirty || _refreshDelay > 0d)
                return;
            _refreshDelay = 0.3d;
            _dirty = false;
            MergeLiveRun();
            RefreshHistoryIfNeeded();
            RefreshRenderer();
        }

        internal void Toggle()
        {
            if (Visible)
            {
                Hide();
                return;
            }

            ReloadRuns();
            ApplyFullscreenGeometry();
            Show();
            MoveToFront();
            MarkDirty();
        }

        internal void OpenCurrentRunOverview()
        {
            ReloadRuns();
            var liveRun = Main.Repository.GetLiveRun(true);
            var currentRun = liveRun ?? (_runs.Length > 0 ? _runs[0] : null);
            if (currentRun != null)
            {
                _selectedRunId = currentRun.RunId;
                _selectedRunData = liveRun;
                _selectedCombatId = currentRun.Combats.Count == 0
                    ? null
                    : currentRun.Combats[^1].CombatId;
                _expandedRunIds.Add(currentRun.RunId);
                _scope.Select(1);
                _ = SelectedRun();
            }

            Select(_dashboard, _dashboardIds, BuiltInDashboardIds.Overview);
            ReplaceRenderer();
            UpdateMetricVisibility();
            _historyHash = 0;
            ApplyFullscreenGeometry();
            Show();
            MoveToFront();
            MarkDirty();
        }

        internal bool ContainsScreenPoint(Vector2 point)
        {
            return Visible && GetGlobalRect().HasPoint(point);
        }

        internal void MarkDirty()
        {
            _dirty = true;
        }

        internal void DisposeRenderer()
        {
            if (_renderer == null)
                return;
            _renderer.Dispose();
            if (IsInstanceValid(_renderer.View))
            {
                if (ReferenceEquals(_renderer.View.GetParent(), _rendererHost))
                    _rendererHost.RemoveChild(_renderer.View);
                _renderer.View.QueueFree();
            }

            _renderer = null;
            _rendererDefinition = null;
        }

        internal void RebuildOptions()
        {
            if (!IsInstanceValid(_dashboard))
                return;
            var selectedDashboard = Selected(_dashboard, _dashboardIds) ?? BuiltInDashboardIds.Overview;
            var order = new[]
            {
                BuiltInDashboardIds.Overview,
                BuiltInDashboardIds.PlayerPerformance,
                BuiltInDashboardIds.DamageContribution,
                BuiltInDashboardIds.EffectiveHpDamageContribution,
                BuiltInDashboardIds.DefenseContribution,
                BuiltInDashboardIds.Meter,
                BuiltInDashboardIds.SourceAnalysis,
                BuiltInDashboardIds.ReceivedDamage,
                BuiltInDashboardIds.DefenseResources,
                BuiltInDashboardIds.CardsAndEffects,
                BuiltInDashboardIds.ContributionAnalysis,
                BuiltInDashboardIds.TurnAnalysis,
                BuiltInDashboardIds.RunTrends,
                BuiltInDashboardIds.CombatRecords,
                BuiltInDashboardIds.CardLog,
                BuiltInDashboardIds.Timeline,
                BuiltInDashboardIds.DamageBreakdown,
            };
            var definitions = _registry.Definitions.OrderBy(definition =>
            {
                var index = Array.IndexOf(order, definition.Id);
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
            _dashboardIds = definitions.Select(definition => definition.Id).ToArray();
            _dashboard.Clear();
            foreach (var definition in definitions)
                _dashboard.AddItem(ModLocalization.Get(definition.TitleLocalizationKey, definition.FallbackTitle));
            Select(_dashboard, _dashboardIds, selectedDashboard);

            var selectedMetric = Selected(_metric, _metricIds) ?? MetricIds.DamageContribution;
            var metrics = Main.Api.MetricDefinitions.OrderBy(metric => DashboardPresentation.MetricOrder(metric.Id))
                .ThenBy(metric => metric.Category, StringComparer.Ordinal)
                .ThenBy(metric => ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName),
                    StringComparer.CurrentCulture)
                .ToArray();
            _metricIds = metrics.Select(metric => metric.Id).ToArray();
            _metric.Clear();
            foreach (var metric in metrics)
                _metric.AddItem(ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName));
            Select(_metric, _metricIds, selectedMetric);

            _historyRevision++;
            RebuildSearchIndex();
            ReplaceRenderer();
            UpdateMetricVisibility();
            MarkDirty();
        }

        private void ReloadRuns()
        {
            var live = Main.Repository.GetLiveRunSummary();
            var runs = MetricsRepository.GetSavedRuns(false, false).ToList();
            if (live != null)
            {
                var index = runs.FindIndex(run => run.RunId == live.RunId);
                if (index < 0)
                    runs.Add(live);
                else
                    runs[index] = live;
            }

            _runs = runs.OrderByDescending(run => run.StartedAtUtc).ToArray();
            _activeRunId = live?.RunId;
            _liveHistorySignature = live == null ? 0 : HistoryStructureSignature(live);
            _historyRevision++;
            RebuildSearchIndex();
            EnsureSelection();
            _selectedRunData = null;
            _ = SelectedRun();
            _historyHash = 0;
            RefreshHistoryIfNeeded();
            MarkDirty();
        }

        private void MergeLiveRun()
        {
            var live = Main.Repository.GetLiveRunSummary();
            if (live == null)
            {
                if (_activeRunId == null)
                    return;
                _activeRunId = null;
                _historyRevision++;
                RebuildSearchIndex();
                return;
            }

            _activeRunId = live.RunId;
            if (_selectedRunId == live.RunId)
                _selectedRunData = Main.Repository.GetLiveRun(true) ?? live;
            var runs = _runs;
            var index = Array.FindIndex(runs, run => run.RunId == live.RunId);
            if (index < 0)
            {
                _runs = [live, .. runs];
                _liveHistorySignature = HistoryStructureSignature(live);
                _historyRevision++;
                RebuildSearchIndex();
            }
            else
            {
                runs[index] = live;
                _runs = runs;
                var signature = HistoryStructureSignature(live);
                if (signature != _liveHistorySignature)
                {
                    _liveHistorySignature = signature;
                    _historyRevision++;
                    RebuildSearchIndex();
                }
            }

            EnsureSelection();
        }

        private void EnsureSelection()
        {
            if (_runs.Length == 0)
            {
                _selectedRunId = null;
                _selectedCombatId = null;
                return;
            }

            var run = _runs.FirstOrDefault(candidate => candidate.RunId == _selectedRunId) ?? _runs[0];
            _selectedRunId = run.RunId;
            _expandedRunIds.Add(run.RunId);
            if (run.Combats.Count == 0)
            {
                _selectedCombatId = null;
                _scope.Select(1);
                return;
            }

            if (run.Combats.All(combat => combat.CombatId != _selectedCombatId))
                _selectedCombatId = run.Combats.MaxBy(combat => combat.StartedAtUtc)?.CombatId;
        }

        private void RefreshHistoryIfNeeded()
        {
            var hash = HistoryHash();
            if (hash == _historyHash)
                return;
            _historyHash = hash;
            RebuildHistoryRows();
        }

        private int HistoryHash()
        {
            var hash = new HashCode();
            hash.Add(_selectedRunId, StringComparer.Ordinal);
            hash.Add(_selectedCombatId, StringComparer.Ordinal);
            hash.Add(_scope.Selected);
            hash.Add(_appliedSearchText, StringComparer.OrdinalIgnoreCase);
            hash.Add(_historyRevision);
            foreach (var runId in _expandedRunIds.Order(StringComparer.Ordinal))
                hash.Add(runId, StringComparer.Ordinal);

            return hash.ToHashCode();
        }

        private void RebuildHistoryRows()
        {
            Clear(_historyRows);
            var search = _appliedSearchText;
            var visibleCombats = 0;
            var matchedRuns = 0;
            var truncated = false;
            foreach (var run in _runs)
            {
                var orderedCombats = run.Combats.OrderByDescending(combat => combat.StartedAtUtc).ToArray();
                var matchingCombats = search.Length == 0
                    ? orderedCombats
                    : orderedCombats.Where(combat => MatchesSearch(combat, search)).ToArray();
                if (matchingCombats.Length == 0 && search.Length > 0)
                    continue;
                matchedRuns++;
                var expanded = search.Length > 0 || _expandedRunIds.Contains(run.RunId);
                var selectedRun = run.RunId == _selectedRunId;
                var group = HistoryRunGroup(selectedRun);
                var groupRows = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ShrinkBegin,
                };
                groupRows.AddThemeConstantOverride("separation", 2);
                group.AddChild(groupRows);
                var header = new HBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ShrinkBegin,
                };
                header.AddThemeConstantOverride("separation", 4);
                var runButton = HistoryRunButton(run, expanded, selectedRun && _scope.Selected == 1,
                    run.RunId == _activeRunId);
                runButton.Pressed += () => SelectRun(run.RunId, true);
                header.AddChild(runButton);
                var delete = HistoryDeleteButton(run.RunId == _activeRunId);
                delete.Pressed += () => RequestDeleteRun(run);
                header.AddChild(delete);
                groupRows.AddChild(header);
                _historyRows.AddChild(group);
                if (!expanded)
                    continue;

                var chronological = run.Combats.OrderBy(combat => combat.StartedAtUtc)
                    .Select((combat, index) => (combat.CombatId, Number: index + 1))
                    .ToDictionary(item => item.CombatId, item => item.Number, StringComparer.Ordinal);
                foreach (var combat in matchingCombats)
                {
                    if (search.Length > 0 && visibleCombats >= SearchResultLimit)
                    {
                        truncated = true;
                        break;
                    }

                    var combatButton = HistoryCombatButton(combat, chronological[combat.CombatId],
                        selectedRun && combat.CombatId == _selectedCombatId && _scope.Selected == 0);
                    combatButton.Pressed += () => SelectCombat(run.RunId, combat.CombatId);
                    groupRows.AddChild(combatButton);
                    visibleCombats++;
                }

                if (truncated)
                    break;
            }

            if (_historyRows.GetChildCount() == 0)
                _historyRows.AddChild(new Label
                {
                    Text = ModLocalization.Get("analysis.noHistory", "No matching combat history"),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    Modulate = new("9FAABEFF"),
                });
            _historySummary.Text = search.Length == 0
                ? ModLocalization.Format("analysis.historySummary", "{0} runs · {1} combats", _runs.Length,
                    _runs.Sum(run => run.Combats.Count))
                : truncated
                    ? ModLocalization.Format("analysis.searchLimited",
                        "{0}+ matches · refine the search to show more", visibleCombats)
                    : ModLocalization.Format("analysis.searchSummary", "{0} runs · {1} matching combats",
                        matchedRuns, visibleCombats);
        }

        private bool MatchesSearch(CombatSnapshot combat, string search)
        {
            if (!_combatSearchText.TryGetValue((combat.RunId, combat.CombatId), out var text))
                return false;
            return _appliedSearchTerms.Length == 0
                ? text.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                : _appliedSearchTerms.All(term =>
                    text.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        private void ProcessPendingSearch(double delta)
        {
            if (_searchDelay < 0d)
                return;
            _searchDelay -= delta;
            if (_searchDelay > 0d)
                return;
            _searchDelay = -1d;
            var search = _search.Text.Trim();
            if (string.Equals(search, _appliedSearchText, StringComparison.Ordinal))
                return;
            _appliedSearchText = search;
            _appliedSearchTerms = search.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _historyHash = 0;
            RefreshHistoryIfNeeded();
        }

        private void RebuildSearchIndex()
        {
            _combatSearchText.Clear();
            foreach (var run in _runs)
            {
                var players = RunPlayers(run);
                var runText = string.Join(' ', run.RunId,
                    run.StartedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    run.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                    RunStatus(run, run.RunId == _activeRunId), RunMode(run), players);
                foreach (var combat in run.Combats)
                {
                    var combatPlayers = string.Join(' ', combat.Players.Select(player =>
                        $"{player.DisplayName} {player.CharacterId}"));
                    _combatSearchText[(run.RunId, combat.CombatId)] = string.Join(' ', runText, combat.CombatId,
                        combat.EncounterId, combat.EncounterName, combat.ActIndex + 1, combat.Floor,
                        combat.RoundCount, combat.StartedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        combatPlayers);
                }
            }
        }

        private static int HistoryStructureSignature(RunSnapshot run)
        {
            var hash = new HashCode();
            hash.Add(run.RunId, StringComparer.Ordinal);
            hash.Add(run.EndedAtUtc);
            hash.Add(run.IsVictory);
            hash.Add(run.IsAbandoned);
            hash.Add(run.Combats.Count);
            foreach (var combat in run.Combats)
            {
                hash.Add(combat.CombatId, StringComparer.Ordinal);
                hash.Add(combat.Completed);
                hash.Add(combat.EncounterId, StringComparer.Ordinal);
                hash.Add(combat.EncounterName, StringComparer.CurrentCulture);
                hash.Add(combat.ActIndex);
                hash.Add(combat.Floor);
                hash.Add(combat.RoundCount);
                foreach (var player in combat.Players)
                {
                    hash.Add(player.PlayerKey, StringComparer.Ordinal);
                    hash.Add(player.DisplayName, StringComparer.CurrentCulture);
                    hash.Add(player.CharacterId, StringComparer.Ordinal);
                }
            }

            return hash.ToHashCode();
        }

        private static string RunPlayers(RunSnapshot run)
        {
            var players = run.Combats.SelectMany(combat => combat.Players)
                .GroupBy(player => player.PlayerKey, StringComparer.Ordinal)
                .Select(group => group.Last())
                .Select(player => string.IsNullOrWhiteSpace(player.DisplayName)
                    ? player.CharacterId
                    : player.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCulture)
                .ToArray();
            return players.Length == 0
                ? ModLocalization.Get("analysis.unknownPlayers", "Unknown party")
                : string.Join(" / ", players);
        }

        private static string RunStatus(RunSnapshot run, bool active)
        {
            if (active)
                return ModLocalization.Get("analysis.runStatus.active", "Active");
            if (run.IsVictory == true)
                return ModLocalization.Get("analysis.runStatus.victory", "Victory");
            if (run.IsAbandoned == true)
                return ModLocalization.Get("analysis.runStatus.abandoned", "Abandoned");
            return run.IsVictory == false
                ? ModLocalization.Get("analysis.runStatus.defeat", "Defeat")
                : ModLocalization.Get("analysis.runStatus.incomplete", "Incomplete");
        }

        private static string RunMode(RunSnapshot run)
        {
            return run.IsDaily
                ? ModLocalization.Get("analysis.runMode.daily", "Daily")
                : run.IsMultiplayer
                    ? ModLocalization.Get("analysis.runMode.multiplayer", "Multiplayer")
                    : ModLocalization.Get("analysis.runMode.standard", "Standard");
        }

        private void SelectRun(string runId, bool toggleExpansion = false)
        {
            var alreadySelected = runId == _selectedRunId && _scope.Selected == 1;
            _selectedRunId = runId;
            if (toggleExpansion && alreadySelected && _expandedRunIds.Remove(runId))
            {
                _historyHash = 0;
                MarkDirty();
                return;
            }

            _expandedRunIds.Clear();
            _expandedRunIds.Add(runId);
            var run = _runs.FirstOrDefault(candidate => candidate.RunId == runId);
            _selectedCombatId = run?.Combats.MaxBy(combat => combat.StartedAtUtc)?.CombatId;
            _scope.Select(1);
            _selectedRunData = null;
            _ = SelectedRun();
            _historyHash = 0;
            MarkDirty();
        }

        private void SelectCombat(string runId, string combatId)
        {
            _selectedRunId = runId;
            _selectedCombatId = combatId;
            _expandedRunIds.Clear();
            _expandedRunIds.Add(runId);
            _scope.Select(0);
            _selectedRunData = null;
            _ = SelectedRun();
            _historyHash = 0;
            MarkDirty();
        }

        private void RequestDeleteRun(RunSnapshot run)
        {
            if (Main.Repository.GetLiveRun(false)?.RunId == run.RunId)
                return;
            var runId = run.RunId;
            _dialogs.ShowConfirmation(
                ModLocalization.Get("analysis.deleteRun.title", "Delete this run?"),
                ModLocalization.Format("analysis.deleteRun.message",
                    "Permanently delete the run started {0:g} and all {1} combats? This cannot be undone.",
                    run.StartedAtUtc.ToLocalTime(), run.Combats.Count),
                ModLocalization.Get("analysis.deleteRun.confirm", "Delete"),
                ModLocalization.Get("dialog.cancel", "Cancel"),
                () => DeleteRun(runId));
        }

        private void RequestDeleteSelectedRun()
        {
            var run = SelectedRun();
            if (run != null)
                RequestDeleteRun(run);
        }

        private void DeleteRun(string runId)
        {
            if (!MetricsRepository.DeleteRun(runId))
            {
                _dialogs.ShowMessage(
                    ModLocalization.Get("analysis.deleteRun.failedTitle", "Could not delete run"),
                    ModLocalization.Get("analysis.deleteRun.failed", "Failed to delete the run."),
                    ModLocalization.Get("dialog.close", "Close"));
                return;
            }

            _expandedRunIds.Remove(runId);
            _selectedRunId = null;
            _selectedCombatId = null;
            _selectedRunData = null;
            ReloadRuns();
            Main.Collectors.NotifyChanged();
        }

        private RunSnapshot? SelectedRun()
        {
            if (_selectedRunId == null)
                return null;
            if (_selectedRunData?.RunId == _selectedRunId)
                return _selectedRunData;
            _selectedRunData = _selectedRunId == _activeRunId
                ? Main.Repository.GetLiveRun(true)
                : MetricsRepository.GetSavedRun(_selectedRunId);
            return _selectedRunData ?? _runs.FirstOrDefault(run => run.RunId == _selectedRunId);
        }

        private CombatSnapshot? SelectedSnapshot()
        {
            var run = SelectedRun();
            if (run == null)
                return null;
            if (_scope.Selected == 1)
                return SnapshotAggregator.Combine(run) is { } combined
                    ? combined with
                    {
                        EncounterName = ModLocalization.Get("analysis.runTotal", "Run total"),
                    }
                    : null;
            return run.Combats.FirstOrDefault(combat => combat.CombatId == _selectedCombatId);
        }

        private void ReplaceRenderer()
        {
            DisposeRenderer();
            var dashboardId = Selected(_dashboard, _dashboardIds);
            if (dashboardId == null || !_registry.TryGetProvider(dashboardId, out var provider))
                return;
            try
            {
                _rendererDefinition = provider.Definition;
                _renderer = provider.CreateRenderer();
                _rendererHost.AddChild(_renderer.View);
            }
            catch (Exception exception)
            {
                _renderer = null;
                _rendererDefinition = null;
                _status.Text = ModLocalization.Format("analysis.rendererFailed", "Renderer failed: {0}",
                    exception.Message);
                Main.Logger.Error($"Analysis center could not create renderer '{dashboardId}': {exception}");
            }
        }

        private void RefreshRenderer()
        {
            if (_renderer == null || _rendererDefinition == null)
                return;
            var run = SelectedRun();
            var snapshot = SelectedSnapshot();
            var style = _registry.ResolveStyle("ritsumetrics.compact");
            _parameters["metric_id"] = Selected(_metric, _metricIds) ?? MetricIds.DamageContribution;
            try
            {
                _renderer.Refresh(new(
                    snapshot,
                    run,
                    _scope.Selected == 1 ? DashboardDataScope.CurrentRun : DashboardDataScope.CurrentCombat,
                    style,
                    _parameters,
                    ModData.Settings.ShowPercentages,
                    SetParameter));
                UpdateSelectionText(run, snapshot);
                if (_renderer is not IDashboardRendererPresentation presentation)
                    return;
                if (!string.IsNullOrWhiteSpace(presentation.Title))
                    _selectionTitle.Text = presentation.Title;
                if (!string.IsNullOrWhiteSpace(presentation.Subtitle))
                    _selectionMeta.Text += $"  ·  {presentation.Subtitle}";
            }
            catch (Exception exception)
            {
                _status.Text = ModLocalization.Format("analysis.rendererFailed", "Renderer failed: {0}",
                    exception.Message);
                Main.Logger.Error($"Analysis center renderer '{_rendererDefinition.Id}' failed: {exception}");
            }
        }

        private void UpdateSelectionText(RunSnapshot? run, CombatSnapshot? snapshot)
        {
            if (run == null)
            {
                _deleteRun.Visible = false;
                _selectionTitle.Text = ModLocalization.Get("analysis.noData", "No analytics data yet");
                _selectionMeta.Text = ModLocalization.Get("analysis.noDataHint",
                    "Complete a combat or select a saved run when history becomes available.");
                return;
            }

            _deleteRun.Visible = true;
            _deleteRun.Disabled = Main.Repository.GetLiveRun(false)?.RunId == run.RunId;
            _deleteRun.TooltipText = _deleteRun.Disabled
                ? ModLocalization.Get("analysis.deleteRun.active", "The current run cannot be deleted while active.")
                : ModLocalization.Get("analysis.deleteRun", "Delete run");
            if (snapshot == null)
            {
                _selectionTitle.Text = ModLocalization.Get("analysis.noData", "No analytics data yet");
                _selectionMeta.Text = ModLocalization.Format("analysis.run",
                    "Run · {0:yyyy-MM-dd HH:mm} · {1} combats", run.StartedAtUtc.ToLocalTime(), run.Combats.Count);
                return;
            }

            _selectionTitle.Text = snapshot.EncounterName;
            _selectionMeta.Text = _scope.Selected == 1
                ? ModLocalization.Format("analysis.runMeta", "{0} combats · {1} rounds · started {2:g}",
                    run.Combats.Count, snapshot.RoundCount, run.StartedAtUtc.ToLocalTime())
                : ModLocalization.Format("analysis.combatMeta", "Act {0} · Floor {1} · {2} rounds · {3:g}",
                    snapshot.ActIndex + 1, snapshot.Floor, snapshot.RoundCount, snapshot.StartedAtUtc.ToLocalTime());
        }

        private void SetParameter(string key, string? value)
        {
            if (value == null)
                _parameters.Remove(key);
            else
                _parameters[key] = value;
            MarkDirty();
        }

        private void ExportSelection(MetricsExportFormat format)
        {
            var result = Main.Api.Export(new()
            {
                Format = format,
                Query = new()
                {
                    RunId = _selectedRunId,
                    CombatId = _scope.Selected == 0 ? _selectedCombatId : null,
                    IncludeEvents = true,
                    IncludeTimeline = true,
                    Limit = 5000,
                },
            });
            _status.Text = result.Success
                ? ModLocalization.Format("analysis.exported", "Exported {0} combat(s) to {1}", result.CombatCount,
                    result.Path)
                : ModLocalization.Format("overlay.exportFailed", "Export failed: {0}", result.Error ?? string.Empty);
        }

        private void UpdateMetricVisibility()
        {
            _metricField.Visible = Selected(_dashboard, _dashboardIds) == BuiltInDashboardIds.Meter;
        }

        private static string Format(decimal value)
        {
            return value == decimal.Truncate(value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N1", CultureInfo.CurrentCulture);
        }

        private static string? Selected(DashboardDropdown option, string[] ids)
        {
            return option.Selected >= 0 && option.Selected < ids.Length ? ids[option.Selected] : null;
        }

        private static void Select(DashboardDropdown option, string[] ids, string id)
        {
            var index = Array.IndexOf(ids, id);
            option.Select(index < 0 ? 0 : index);
        }

        private static void Clear(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }
    }
}
