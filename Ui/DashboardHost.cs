// SPDX-License-Identifier: MPL-2.0

using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Ui.Shell.Theme;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Data.Models;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class DashboardHost : CanvasLayer
    {
        private const int FloatingWindowLayer = 120;
        private const int ControlSurfaceLayer = FloatingWindowLayer + 1;
        private const int BehindCapstoneLayer = -1;
        private const double DashboardDataRefreshInterval = 0.12d;
        private readonly Lock _dashboardDataGate = new();
        private readonly RunAggregateCache _runAggregateCache = new();

        private readonly Dictionary<string, DashboardWindow> _windows = new(StringComparer.Ordinal);
        private AnalysisCenter? _analysisCenter;
        private CombatSnapshot? _cachedCombatSnapshot;
        private DashboardDataComponents _cachedComponents;
        private string _cachedMetricSelectionKey = string.Empty;
        private bool _cachedNeedsRunAggregate;
        private RunSnapshot? _cachedRun;
        private CombatSnapshot? _cachedRunAggregate;
        private long _cachedSnapshotRevision = -1;
        private NCapstoneContainer? _capstoneContainer;
        private bool _capstoneInUse;
        private bool _dashboardRefreshRequested;
        private bool _dashboardRefreshRunning;
        private bool _dashboardRefreshScheduled;
        private bool _localizationRefreshPending;
        private CombatSnapshot? _localizedCombatSnapshot;
        private RunSnapshot? _localizedRun;
        private CombatSnapshot? _localizedRunSnapshot;
        private DashboardManagerPanel? _manager;
        private MetricsChange _pendingChange = MetricsChange.All;
        private DashboardRegistry _registry = null!;
        private int _settingsHash;
        private bool _settingsUpdateScheduled;
        private long _snapshotRevision;
        private Theme? _typographyTheme;
        private bool _visibilityUpdateScheduled;
        private CanvasLayer _windowLayer = null!;

        internal IReadOnlyCollection<DashboardWindowInfo> WindowInfos => _windows.Values
            .Select(window => window.Info).ToArray();

        internal bool ContainsWindow(string instanceId)
        {
            return _windows.ContainsKey(instanceId);
        }

        internal bool IsTopmostWindowAt(DashboardWindow candidate, Vector2 point)
        {
            if (NCapstoneContainer.Instance?.InUse == true)
                return false;
            if (_analysisCenter is { Visible: true } analysisCenter && analysisCenter.ContainsScreenPoint(point))
                return false;
            if (_manager is { Visible: true } manager && manager.ContainsScreenPoint(point))
                return false;
            return ReferenceEquals(_windowLayer.GetChildren().OfType<DashboardWindow>().Reverse()
                .FirstOrDefault(window => window.ContainsScreenPoint(point)), candidate);
        }

        internal void Initialize(DashboardRegistry registry)
        {
            _registry = registry;
        }

        public override void _Ready()
        {
            ModLocalization.SynchronizeCurrentLanguage();
            Layer = ControlSurfaceLayer;
            _windowLayer = new() { Layer = FloatingWindowLayer };
            AddChild(_windowLayer);
            _typographyTheme = DashboardControlTheme.CreateTypographyTheme();
            _registry.Changed += OnRegistryChanged;
            _registry.OpenRequested += DrainOpenRequests;
            _registry.CloseRequested += CloseWindow;
            Main.Collectors.DataChanged += MarkDirty;
            RitsuShellThemeRuntime.ThemeChanged += OnShellThemeChanged;
            ModLocalization.Changed += OnLocalizationChanged;
            ModData.HistoryReady += OnHistoryReady;
            ModData.SettingsChanged += OnSettingsChanged;
            LoadWindows();
            DrainOpenRequests();
            if (_windows.Count == 0 && ModData.Settings.OverlayEnabled)
                OpenWindow(BuiltInDashboardIds.DamageContribution, new());
            CreateControlSurfaces();
            ApplyTypographyTheme();
            SetProcessUnhandledInput(true);
            SetProcessInput(true);
            ApplySettings(true);
            BindCapstoneContainer();
            UpdateCapstoneState();
            ScheduleDashboardDataRefresh(true);
            Main.Logger.Info($"Dashboard host ready with {_windows.Count} window(s).");
        }

        public override void _ExitTree()
        {
            _registry.Changed -= OnRegistryChanged;
            _registry.OpenRequested -= DrainOpenRequests;
            _registry.CloseRequested -= CloseWindow;
            Main.Collectors.DataChanged -= MarkDirty;
            RitsuShellThemeRuntime.ThemeChanged -= OnShellThemeChanged;
            ModLocalization.Changed -= OnLocalizationChanged;
            ModData.HistoryReady -= OnHistoryReady;
            ModData.SettingsChanged -= OnSettingsChanged;
            if (_capstoneContainer != null)
                _capstoneContainer.Changed -= OnCapstoneChanged;
            foreach (var window in _windows.Values)
                window.DisposeRenderer();
            _analysisCenter?.DisposeRenderer();
            _windows.Clear();
            if (ReferenceEquals(Main.DashboardHost, this))
                Main.DashboardHost = null;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is not InputEventKey { Pressed: true, Echo: false } key)
                return;
            if (key.Keycode == Key.Escape && _analysisCenter is { Visible: true } analysisCenter)
            {
                analysisCenter.Hide();
                DashboardConsumersChanged();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (!MatchesBinding(key, ModData.Settings.ToggleKey))
                return;
            var enabled = !ModData.Settings.OverlayEnabled;
            ModData.ModifySettings(settings => settings.OverlayEnabled = enabled);
            if (enabled && _windows.Count == 0)
                OpenWindow(BuiltInDashboardIds.DamageContribution, new());
            ApplySettings();
            GetViewport().SetInputAsHandled();
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is not InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Left, Pressed: true,
                } mouse)
                return;
            if (_analysisCenter is { Visible: true } analysisCenter &&
                analysisCenter.ContainsScreenPoint(mouse.GlobalPosition))
            {
                analysisCenter.MoveToFront();
                return;
            }

            if (_manager is { Visible: true } manager && manager.ContainsScreenPoint(mouse.GlobalPosition))
            {
                manager.MoveToFront();
                return;
            }

            if (NCapstoneContainer.Instance?.InUse == true)
                return;

            var target = _windowLayer.GetChildren().OfType<DashboardWindow>().Reverse()
                .FirstOrDefault(window => window.ContainsScreenPoint(mouse.GlobalPosition));
            if (target == null)
                return;
            target.FocusWindow();
            if (_manager is { Visible: true } visibleManager)
                visibleManager.MoveToFront();
        }

        internal void ApplySettings(bool forceLayout = false)
        {
            _settingsHash = SettingsHash();
            UpdateVisibility();
            foreach (var window in _windows.Values)
                window.ApplyGlobalSettings(forceLayout);
        }

        internal void ToggleManager()
        {
            if (_manager is not { } manager || !IsInstanceValid(manager))
                return;
            if (!ModData.Settings.OverlayEnabled)
            {
                ModData.ModifySettings(settings => settings.OverlayEnabled = true);
                ApplySettings();
            }

            manager.Toggle();
        }

        internal void ToggleAnalysisCenter()
        {
            if (_analysisCenter is not { } analysisCenter || !IsInstanceValid(analysisCenter))
                return;
            analysisCenter.Toggle();
            DashboardConsumersChanged();
        }

        internal void OpenCurrentRunOverview()
        {
            if (_analysisCenter is not { } analysisCenter || !IsInstanceValid(analysisCenter))
                return;
            analysisCenter.OpenCurrentRunOverview();
            DashboardConsumersChanged();
        }

        internal void FocusWindow(string instanceId)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.FocusWindow();
            if (_manager is { Visible: true } manager)
                manager.MoveToFront();
        }

        internal void ToggleWindowLock(string instanceId)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.ToggleLock();
            _manager?.RefreshWindows();
        }

        internal void ResetWindowGeometry(string instanceId)
        {
            if (!_windows.TryGetValue(instanceId, out var window))
                return;
            window.ResetGeometry();
            FocusWindow(instanceId);
            _manager?.RefreshWindows();
        }

        internal void ConfigureWindow(
            string instanceId,
            DashboardDataScope scope,
            string styleId,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.Configure(scope, IsBuiltIn(window.DashboardId) ? "ritsumetrics.compact" : styleId,
                    parameters);
            DashboardConsumersChanged();
            _manager?.RefreshWindows();
        }

        internal void PreviewWindowParameters(
            string instanceId,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.PreviewParameters(parameters);
            DashboardConsumersChanged();
        }

        internal void RestoreWindowParameters(string instanceId)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.RestorePreviewParameters();
            DashboardConsumersChanged();
        }

        internal bool SwitchWindowDashboard(
            string instanceId,
            string dashboardId,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (!_windows.TryGetValue(instanceId, out var window) ||
                !_registry.TryGetProvider(dashboardId, out var provider))
                return false;
            if (!provider.Definition.AllowMultipleInstances && _windows.Values.Any(candidate =>
                    candidate.InstanceId != instanceId && candidate.DashboardId == dashboardId))
                return false;
            var styleId = IsBuiltIn(dashboardId)
                ? "ritsumetrics.compact"
                : provider.Definition.DefaultStyleId;
            if (!window.SwitchDashboard(provider, styleId, parameters))
                return false;
            DashboardConsumersChanged();
            _manager?.RefreshWindows();
            return true;
        }

        internal string? OpenWindow(string dashboardId, DashboardWindowOptions options)
        {
            if (!_registry.TryGetProvider(dashboardId, out var provider))
                return null;
            if (!provider.Definition.AllowMultipleInstances &&
                _windows.Values.Any(window => window.DashboardId == dashboardId))
                return _windows.Values.First(window => window.DashboardId == dashboardId).InstanceId;

            if (!ModData.Settings.OverlayEnabled)
                ModData.ModifySettings(settings => settings.OverlayEnabled = true);

            var cascade = _windows.Count % 10;
            var parameters = options.Parameters == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(options.Parameters, StringComparer.Ordinal);
            parameters.TryAdd(DashboardParameterIds.Layout,
                DashboardPresentation.NormalizeLayout(ModData.Settings.DefaultDashboardLayout));
            var state = new DashboardWindowSettings
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                DashboardId = dashboardId,
                Scope = options.Scope,
                StyleId = IsBuiltIn(dashboardId)
                    ? "ritsumetrics.compact"
                    : options.StyleId ?? provider.Definition.DefaultStyleId,
                PositionX = options.PositionX ?? 0f,
                PositionY = options.PositionY ?? 92f + cascade * 26f,
                Width = options.Width ?? provider.Definition.DefaultWidth,
                Height = options.Height ?? provider.Definition.DefaultHeight,
                HasCustomPosition = options.PositionX != null || options.PositionY != null,
                Parameters = parameters,
            };
            AddPersistedWindow(state, provider);
            _manager?.RefreshWindows();
            return state.InstanceId;
        }

        internal void CloseWindow(string instanceId)
        {
            if (!_windows.Remove(instanceId, out var window))
                return;
            window.DisposeRenderer();
            _windowLayer.RemoveChild(window);
            window.QueueFree();
            ModData.ModifySettings(settings =>
                settings.DashboardWindows.RemoveAll(item => item.InstanceId == instanceId));
            DashboardConsumersChanged();
            _manager?.RefreshWindows();
        }

        internal static void SaveWindow(DashboardWindowSettings state)
        {
            ModData.ModifySettings(settings =>
            {
                var index = settings.DashboardWindows.FindIndex(item => item.InstanceId == state.InstanceId);
                var copy = Clone(state);
                if (index < 0)
                    settings.DashboardWindows.Add(copy);
                else
                    settings.DashboardWindows[index] = copy;
            });
        }

        internal (CombatSnapshot? Snapshot, RunSnapshot? Run) ResolveDashboardData(
            DashboardDataScope scope,
            DashboardDataRequirements requirements)
        {
            EnsureDashboardDataCache();
            lock (_dashboardDataGate)
            {
                if (requirements.Components.HasFlag(DashboardDataComponents.RunCombats))
                    _localizedRun ??= _cachedRun is null ? null : LocalizedSnapshotResolver.Resolve(_cachedRun);
                if (scope == DashboardDataScope.CurrentRun)
                    _localizedRunSnapshot ??= ResolveLocalizedRunSnapshot();
                else
                    _localizedCombatSnapshot ??= ResolveLocalizedCombatSnapshot();
                return (scope == DashboardDataScope.CurrentRun ? _localizedRunSnapshot : _localizedCombatSnapshot,
                    requirements.Components.HasFlag(DashboardDataComponents.RunCombats) ? _localizedRun : null);
            }
        }

        private CombatSnapshot? ResolveLocalizedCombatSnapshot()
        {
            return _cachedCombatSnapshot == null
                ? null
                : LocalizedSnapshotResolver.Resolve(_cachedCombatSnapshot,
                    _cachedRun is { IsMultiplayer: false });
        }

        private CombatSnapshot? ResolveLocalizedRunSnapshot()
        {
            return _cachedRunAggregate == null
                ? null
                : LocalizedSnapshotResolver.Resolve(_cachedRunAggregate, _cachedRun is { IsMultiplayer: false });
        }

        internal void RestoreDefaultLayout()
        {
            foreach (var window in _windows.Values.ToArray())
            {
                window.DisposeRenderer();
                _windowLayer.RemoveChild(window);
                window.QueueFree();
            }

            _windows.Clear();
            ModData.ModifySettings(settings =>
            {
                settings.OverlayEnabled = true;
                settings.DashboardWindows.Clear();
            });
            OpenWindow(BuiltInDashboardIds.DamageContribution, new()
            {
                Width = 400f,
                Height = 360f,
            });
            ApplySettings(true);
        }

        private void LoadWindows()
        {
            foreach (var state in ModData.Settings.DashboardWindows.Select(Clone))
                if (_registry.TryGetProvider(state.DashboardId, out var provider))
                {
                    if (IsBuiltIn(state.DashboardId))
                        state.StyleId = "ritsumetrics.compact";
                    AddWindow(state, provider);
                }
        }

        private void AddPersistedWindow(DashboardWindowSettings state, IDashboardProvider provider)
        {
            ModData.ModifySettings(settings => settings.DashboardWindows.Add(Clone(state)));
            AddWindow(state, provider);
        }

        private void AddWindow(DashboardWindowSettings state, IDashboardProvider provider)
        {
            if (_windows.ContainsKey(state.InstanceId))
                return;
            var renderer = provider.CreateRenderer();
            var window = new DashboardWindow
            {
                Theme = _typographyTheme ??= DashboardControlTheme.CreateTypographyTheme(),
            };
            window.Initialize(this, _registry, provider.Definition, renderer, state);
            _windows.Add(state.InstanceId, window);
            _windowLayer.AddChild(window);
            UpdateVisibility();
            DashboardConsumersChanged();
            _manager?.RefreshWindows();
            if (_manager is { Visible: true } manager)
                manager.MoveToFront();
        }

        private void OnShellThemeChanged()
        {
            if (!IsInsideTree())
                return;
            Callable.From(ApplyTypographyTheme).CallDeferred();
        }

        private void OnLocalizationChanged()
        {
            if (!IsInsideTree())
                return;
            LocalizedSnapshotResolver.ClearCaches();
            lock (_dashboardDataGate)
            {
                _localizedRun = null;
                _localizedCombatSnapshot = null;
                _localizedRunSnapshot = null;
            }

            if (_localizationRefreshPending)
                return;
            _localizationRefreshPending = true;
            Callable.From(RebuildLocalizedUi).CallDeferred();
        }

        private void OnHistoryReady()
        {
            _analysisCenter?.HistoryLoaded();
        }

        private void OnSettingsChanged()
        {
            if (_settingsUpdateScheduled || !IsInsideTree())
                return;
            _settingsUpdateScheduled = true;
            Callable.From(() =>
            {
                _settingsUpdateScheduled = false;
                if (IsInsideTree() && SettingsHash() != _settingsHash)
                    ApplySettings();
            }).CallDeferred();
        }

        private void BindCapstoneContainer()
        {
            var current = NCapstoneContainer.Instance;
            if (ReferenceEquals(_capstoneContainer, current))
                return;
            if (_capstoneContainer != null)
                _capstoneContainer.Changed -= OnCapstoneChanged;
            _capstoneContainer = current;
            if (_capstoneContainer != null)
                _capstoneContainer.Changed += OnCapstoneChanged;
        }

        private void OnCapstoneChanged()
        {
            UpdateCapstoneState();
        }

        private void UpdateCapstoneState()
        {
            var capstoneInUse = _capstoneContainer?.InUse == true;
            if (capstoneInUse && !_capstoneInUse)
                _manager?.HideForSystemMenu();
            _capstoneInUse = capstoneInUse;
            var windowLayer = capstoneInUse
                ? BehindCapstoneLayer
                : FloatingWindowLayer;
            if (_windowLayer.Layer != windowLayer)
                _windowLayer.Layer = windowLayer;
        }

        private void RebuildLocalizedUi()
        {
            _localizationRefreshPending = false;
            if (!IsInsideTree())
                return;

            _manager?.HideForSystemMenu();

            foreach (var window in _windows.Values.ToArray())
            {
                window.DisposeRenderer();
                DetachAndFree(window);
            }

            _windows.Clear();
            if (_manager is { } currentManager && IsInstanceValid(currentManager))
                DetachAndFree(currentManager);
            if (_analysisCenter is { } analysisCenter && IsInstanceValid(analysisCenter))
                DetachAndFree(analysisCenter);
            _manager = null;
            _analysisCenter = null;

            LoadWindows();
            CreateControlSurfaces();
            ApplyTypographyTheme();
            ApplySettings(true);
            MarkAllDirty();
        }

        private void CreateControlSurfaces()
        {
            _manager = new() { Visible = false, Theme = _typographyTheme };
            _manager.Initialize(this, _registry);
            AddChild(_manager);
            _analysisCenter = new() { Theme = _typographyTheme };
            _analysisCenter.Initialize(_registry);
            _analysisCenter.VisibilityChanged += DashboardConsumersChanged;
            AddChild(_analysisCenter);
        }

        private static void DetachAndFree(Node node)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }

        private void ApplyTypographyTheme()
        {
            if (!IsInsideTree())
                return;
            _typographyTheme = DashboardControlTheme.CreateTypographyTheme();
            foreach (var window in _windows.Values)
                DashboardControlTheme.ApplyTypography(window, _typographyTheme);
            foreach (var control in GetChildren().OfType<Control>())
                DashboardControlTheme.ApplyTypography(control, _typographyTheme);
        }

        private void DrainOpenRequests()
        {
            foreach (var (instanceId, dashboardId, options) in _registry.DrainOpenRequests())
            {
                if (!_registry.TryGetProvider(dashboardId, out var provider))
                    continue;
                var cascade = _windows.Count % 10;
                var state = new DashboardWindowSettings
                {
                    InstanceId = instanceId,
                    DashboardId = dashboardId,
                    Scope = options.Scope,
                    StyleId = IsBuiltIn(dashboardId)
                        ? "ritsumetrics.compact"
                        : options.StyleId ?? provider.Definition.DefaultStyleId,
                    PositionX = options.PositionX ?? 0f,
                    PositionY = options.PositionY ?? 92f + cascade * 26f,
                    Width = options.Width ?? provider.Definition.DefaultWidth,
                    Height = options.Height ?? provider.Definition.DefaultHeight,
                    HasCustomPosition = options.PositionX != null || options.PositionY != null,
                    Parameters = options.Parameters == null
                        ? new(StringComparer.Ordinal)
                        : new Dictionary<string, string>(options.Parameters, StringComparer.Ordinal),
                };
                AddPersistedWindow(state, provider);
            }
        }

        private void OnRegistryChanged()
        {
            foreach (var window in _windows.Values)
                window.RebuildMenus();
            _manager?.RebuildOptions();
            _analysisCenter?.RebuildOptions();
            foreach (var state in ModData.Settings.DashboardWindows.Select(Clone))
                if (!_windows.ContainsKey(state.InstanceId) &&
                    _registry.TryGetProvider(state.DashboardId, out var provider))
                    AddWindow(state, provider);
        }

        private void MarkAllDirty()
        {
            MarkDirty(MetricsChange.All);
        }

        private void MarkDirty(MetricsChange change)
        {
            if (change.Kind.HasFlag(MetricsChangeKind.RunStructure))
                ScheduleVisibilityUpdate();
            if (!RequiredDataPlan().IsAffectedBy(change))
                return;

            lock (_dashboardDataGate)
            {
                _snapshotRevision++;
                _pendingChange = _pendingChange.Merge(change);
            }

            ScheduleDashboardDataRefresh();
        }

        private void ScheduleVisibilityUpdate()
        {
            if (_visibilityUpdateScheduled || !IsInsideTree())
                return;
            _visibilityUpdateScheduled = true;
            Callable.From(() =>
            {
                _visibilityUpdateScheduled = false;
                if (!IsInsideTree())
                    return;
                UpdateVisibility();
            }).CallDeferred();
        }

        internal void DashboardConsumersChanged()
        {
            lock (_dashboardDataGate)
            {
                _snapshotRevision++;
                _pendingChange = _pendingChange.Merge(MetricsChange.All);
            }

            ScheduleDashboardDataRefresh(true);
        }

        private void ScheduleDashboardDataRefresh(bool immediate = false)
        {
            if (!IsInsideTree() || !HasVisibleDataConsumer())
                return;
            if (_dashboardRefreshRunning)
            {
                _dashboardRefreshRequested = true;
                return;
            }

            if (_dashboardRefreshScheduled)
                return;
            _dashboardRefreshScheduled = true;
            RunScheduledDashboardRefresh(immediate);
        }

        private async void RunScheduledDashboardRefresh(bool immediate)
        {
            try
            {
                if (!immediate)
                    await ToSignal(GetTree().CreateTimer(DashboardDataRefreshInterval),
                        SceneTreeTimer.SignalName.Timeout);
                if (!IsInsideTree())
                    return;
                _dashboardRefreshScheduled = false;
                if (!TryCreateCaptureRequest(out var request))
                    return;

                _dashboardRefreshRunning = true;
                var data = await Task.Run(() => CaptureDashboardData(
                    Main.Repository,
                    request.Revision,
                    request.Components,
                    request.NeedsRunAggregate,
                    request.MetricIds,
                    request.MetricSelectionKey,
                    request.Change,
                    _runAggregateCache));
                if (!IsInsideTree())
                    return;
                ApplyDashboardDataRefresh(data);
            }
            catch (Exception exception)
            {
                lock (_dashboardDataGate)
                {
                    _pendingChange = _pendingChange.Merge(MetricsChange.All);
                }

                Main.Logger.Error($"Asynchronous dashboard snapshot refresh failed: {exception}");
                _dashboardRefreshRequested = true;
            }
            finally
            {
                _dashboardRefreshRunning = false;
                if (IsInsideTree())
                {
                    var refreshRequested = _dashboardRefreshRequested;
                    _dashboardRefreshRequested = false;
                    if (refreshRequested || HasPendingDashboardDataRefresh())
                        ScheduleDashboardDataRefresh();
                }
            }
        }

        private bool TryCreateCaptureRequest(out DashboardCaptureRequest request)
        {
            lock (_dashboardDataGate)
            {
                var plan = RequiredDataPlan();
                var shapeMatches = _cachedComponents == plan.Components &&
                                   _cachedNeedsRunAggregate == plan.NeedsRunAggregate &&
                                   _cachedMetricSelectionKey == plan.MetricSelectionKey;
                if (_cachedSnapshotRevision == _snapshotRevision && shapeMatches)
                {
                    request = default;
                    return false;
                }

                if (_cachedSnapshotRevision >= 0 && shapeMatches && !plan.IsAffectedBy(_pendingChange))
                {
                    _cachedSnapshotRevision = _snapshotRevision;
                    _pendingChange = default;
                    request = default;
                    return false;
                }

                request = new(
                    _snapshotRevision,
                    plan.Components,
                    plan.NeedsRunAggregate,
                    plan.MetricIds,
                    plan.MetricSelectionKey,
                    _pendingChange);
                _pendingChange = default;
                return true;
            }
        }

        private void EnsureDashboardDataCache()
        {
            lock (_dashboardDataGate)
            {
                if (_cachedSnapshotRevision >= 0)
                    return;
            }

            ScheduleDashboardDataRefresh(true);
        }

        private bool HasPendingDashboardDataRefresh()
        {
            lock (_dashboardDataGate)
            {
                var plan = RequiredDataPlan();
                return _cachedSnapshotRevision != _snapshotRevision ||
                       _cachedComponents != plan.Components ||
                       _cachedNeedsRunAggregate != plan.NeedsRunAggregate ||
                       _cachedMetricSelectionKey != plan.MetricSelectionKey;
            }
        }

        private void ApplyDashboardDataRefresh(DashboardDataCache data)
        {
            bool shapeChanged;
            lock (_dashboardDataGate)
            {
                if (data.Revision < _cachedSnapshotRevision)
                    return;
                shapeChanged = _cachedComponents != data.Components ||
                               _cachedNeedsRunAggregate != data.NeedsRunAggregate ||
                               _cachedMetricSelectionKey != data.MetricSelectionKey;
                _cachedRun = data.Run;
                _cachedCombatSnapshot = data.Combat;
                _cachedRunAggregate = data.RunAggregate;
                _cachedComponents = data.Components;
                _cachedNeedsRunAggregate = data.NeedsRunAggregate;
                _cachedMetricSelectionKey = data.MetricSelectionKey;
                _localizedRun = null;
                _localizedCombatSnapshot = null;
                _localizedRunSnapshot = null;
                _cachedSnapshotRevision = data.Revision;
            }

            foreach (var window in _windows.Values)
                if (shapeChanged)
                    window.MarkDirty();
                else
                    window.MarkDirty(data.Change);
            if (data.Change.Kind != MetricsChangeKind.None)
                _analysisCenter?.MarkDirty();
        }

        private static DashboardDataCache CaptureDashboardData(
            MetricsRepository repository,
            long revision,
            DashboardDataComponents components,
            bool needsRunAggregate,
            IReadOnlySet<string>? metricIds,
            string metricSelectionKey,
            MetricsChange change,
            RunAggregateCache runAggregateCache)
        {
            var includeEvents = components.HasFlag(DashboardDataComponents.Events);
            var includeTimeline = components.HasFlag(DashboardDataComponents.Timeline);
            var includeCompletedCombats = components.HasFlag(DashboardDataComponents.RunCombats);
            var projectCompletedCombats = includeCompletedCombats;
            var run = repository.GetLiveRunForDashboard(includeEvents, includeTimeline, includeCompletedCombats,
                projectCompletedCombats, metricIds);
            if (needsRunAggregate && !includeCompletedCombats && run != null &&
                runAggregateCache.RequiresCompletedCombats(
                    run,
                    components,
                    metricSelectionKey,
                    change.Kind.HasFlag(MetricsChangeKind.RunStructure)))
            {
                includeCompletedCombats = true;
                run = repository.GetLiveRunForDashboard(includeEvents, includeTimeline, true, false, metricIds);
            }

            if (projectCompletedCombats)
                run = DashboardSnapshotProjector.Project(run, metricIds);
            var combat = run is { Combats.Count: > 0 }
                ? DashboardSnapshotProjector.Project(run.Combats[^1], metricIds)
                : DashboardSnapshotProjector.Project(repository.GetLiveCombat(includeEvents), metricIds);
            if (!includeTimeline && combat?.Timeline is { Count: > 0 })
                combat = combat with { Timeline = [] };
            var runAggregate = needsRunAggregate && run != null
                ? runAggregateCache.Combine(
                    run,
                    components,
                    metricIds,
                    metricSelectionKey,
                    includeCompletedCombats)
                : null;
            return new(revision, components, needsRunAggregate, metricSelectionKey, change, run, combat,
                runAggregate);
        }

        private DashboardDataPlan RequiredDataPlan()
        {
            var consumers = _windows.Values
                .Where(window => window.ConsumesDashboardData)
                .Select(window => (window.DataScope, window.DataRequirements));
            return DashboardDataPlan.Create(consumers, _analysisCenter?.Visible == true);
        }

        private bool HasVisibleDataConsumer()
        {
            return _analysisCenter?.Visible == true || _windows.Values.Any(window => window.ConsumesDashboardData);
        }

        private void UpdateVisibility()
        {
            BindCapstoneContainer();
            UpdateCapstoneState();
            var settings = ModData.Settings;
            var runManager = RunManager.Instance;
            var hasLiveCombat = Main.Repository.HasLiveCombat;
            var isRunCompletionView = runManager.IsInProgress &&
                                      (runManager.IsGameOver ||
                                       runManager.DebugOnlyGetState()?.CurrentRoom?.IsVictoryRoom == true);
            var hasCompletedCombat = isRunCompletionView && Main.Repository.HasLiveRunCombat;
            var showFloatingDashboards = settings.OverlayEnabled && runManager.IsInProgress &&
                                         (hasLiveCombat || hasCompletedCombat);
            var becameVisible = false;
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var window in _windows.Values)
            {
                if (window.Visible == showFloatingDashboards)
                    continue;
                window.Visible = showFloatingDashboards;
                if (!showFloatingDashboards)
                    continue;
                becameVisible = true;
                window.MarkDirty();
            }

            if (becameVisible)
                DashboardConsumersChanged();
        }

        private static DashboardWindowSettings Clone(DashboardWindowSettings state)
        {
            return new()
            {
                InstanceId = state.InstanceId,
                DashboardId = state.DashboardId,
                Scope = state.Scope,
                StyleId = state.StyleId,
                PositionX = state.PositionX,
                PositionY = state.PositionY,
                Width = state.Width,
                Height = state.Height,
                HasCustomPosition = state.HasCustomPosition,
                IsCollapsed = state.IsCollapsed,
                IsLocked = state.IsLocked,
                Parameters = new(state.Parameters, StringComparer.Ordinal),
            };
        }

        private static bool IsBuiltIn(string dashboardId)
        {
            return dashboardId.StartsWith("ritsumetrics.", StringComparison.Ordinal);
        }

        private static bool MatchesBinding(InputEventKey key, string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
                return false;
            var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var keyToken = parts.LastOrDefault();
            if (!Enum.TryParse<Key>(keyToken, true, out var configuredKey) || key.Keycode != configuredKey)
                return false;
            var tokens = new HashSet<string>(parts.Take(parts.Length - 1), StringComparer.OrdinalIgnoreCase);
            return key.CtrlPressed == tokens.Any(token => token.Contains("ctrl", StringComparison.OrdinalIgnoreCase) ||
                                                          token.Contains("control",
                                                              StringComparison.OrdinalIgnoreCase)) &&
                   key.AltPressed == tokens.Any(token => token.Contains("alt", StringComparison.OrdinalIgnoreCase)) &&
                   key.ShiftPressed ==
                   tokens.Any(token => token.Contains("shift", StringComparison.OrdinalIgnoreCase)) &&
                   key.MetaPressed == tokens.Any(token => token.Contains("meta", StringComparison.OrdinalIgnoreCase) ||
                                                          token.Contains("cmd", StringComparison.OrdinalIgnoreCase));
        }

        private static int SettingsHash()
        {
            var settings = ModData.Settings;
            var displaySettings = HashCode.Combine(
                settings.OverlayEnabled,
                settings.HideOutsideCombat,
                settings.ShowPercentages,
                settings.ScalePercent,
                settings.WindowOpacityPercent,
                settings.OpacityPercent,
                settings.ToggleKey,
                RunManager.Instance.IsInProgress);
            return HashCode.Combine(displaySettings, RunManager.Instance.IsGameOver,
                RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.IsVictoryRoom == true);
        }

        private readonly record struct DashboardDataCache(
            long Revision,
            DashboardDataComponents Components,
            bool NeedsRunAggregate,
            string MetricSelectionKey,
            MetricsChange Change,
            RunSnapshot? Run,
            CombatSnapshot? Combat,
            CombatSnapshot? RunAggregate);

        private readonly record struct DashboardCaptureRequest(
            long Revision,
            DashboardDataComponents Components,
            bool NeedsRunAggregate,
            IReadOnlySet<string>? MetricIds,
            string MetricSelectionKey,
            MetricsChange Change);
    }
}
