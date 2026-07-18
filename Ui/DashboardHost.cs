// SPDX-License-Identifier: MPL-2.0

using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Ui.Shell.Theme;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class DashboardHost : CanvasLayer
    {
        private const int FloatingWindowLayer = 120;
        private const int ControlSurfaceLayer = FloatingWindowLayer + 1;
        private const int BehindCapstoneLayer = -1;
        private readonly Lock _dashboardDataGate = new();

        private readonly Dictionary<string, DashboardWindow> _windows = new(StringComparer.Ordinal);
        private AnalysisCenter? _analysisCenter;
        private CombatSnapshot? _cachedCombatSnapshot;
        private RunSnapshot? _cachedRun;
        private CombatSnapshot? _cachedRunSnapshot;
        private long _cachedSnapshotRevision = -1;
        private bool _capstoneInUse;
        private DashboardManagerPanel? _manager;
        private DashboardRegistry _registry = null!;
        private int _settingsHash;
        private long _snapshotRevision;
        private Theme? _typographyTheme;
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
            Layer = ControlSurfaceLayer;
            _windowLayer = new() { Layer = FloatingWindowLayer };
            AddChild(_windowLayer);
            _typographyTheme = DashboardControlTheme.CreateTypographyTheme();
            _registry.Changed += OnRegistryChanged;
            _registry.OpenRequested += DrainOpenRequests;
            _registry.CloseRequested += CloseWindow;
            Main.Collectors.SnapshotChanged += MarkAllDirty;
            RitsuShellThemeRuntime.ThemeChanged += OnShellThemeChanged;
            LoadWindows();
            DrainOpenRequests();
            if (_windows.Count == 0 && ModData.Settings.OverlayEnabled)
                OpenWindow(BuiltInDashboardIds.DamageContribution, new());
            _manager = new() { Visible = false, Theme = _typographyTheme };
            _manager.Initialize(this, _registry);
            AddChild(_manager);
            _analysisCenter = new() { Theme = _typographyTheme };
            _analysisCenter.Initialize(_registry);
            AddChild(_analysisCenter);
            ApplyTypographyTheme();
            SetProcessUnhandledInput(true);
            SetProcessInput(true);
            SetProcess(true);
            ApplySettings(true);
            Main.Logger.Info($"Dashboard host ready with {_windows.Count} window(s).");
        }

        public override void _ExitTree()
        {
            _registry.Changed -= OnRegistryChanged;
            _registry.OpenRequested -= DrainOpenRequests;
            _registry.CloseRequested -= CloseWindow;
            Main.Collectors.SnapshotChanged -= MarkAllDirty;
            RitsuShellThemeRuntime.ThemeChanged -= OnShellThemeChanged;
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

        public override void _Process(double delta)
        {
            var capstoneInUse = NCapstoneContainer.Instance?.InUse == true;
            if (capstoneInUse && !_capstoneInUse)
                _manager?.HideForSystemMenu();
            _capstoneInUse = capstoneInUse;
            var windowLayer = capstoneInUse
                ? BehindCapstoneLayer
                : FloatingWindowLayer;
            if (_windowLayer.Layer != windowLayer)
                _windowLayer.Layer = windowLayer;

            var settingsHash = SettingsHash();
            if (settingsHash != _settingsHash)
                ApplySettings();
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
        }

        internal void OpenCurrentRunOverview()
        {
            if (_analysisCenter is not { } analysisCenter || !IsInstanceValid(analysisCenter))
                return;
            analysisCenter.OpenCurrentRunOverview();
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
            _manager?.RefreshWindows();
        }

        internal void PreviewWindowParameters(
            string instanceId,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.PreviewParameters(parameters);
        }

        internal void RestoreWindowParameters(string instanceId)
        {
            if (_windows.TryGetValue(instanceId, out var window))
                window.RestorePreviewParameters();
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
                : new(options.Parameters, StringComparer.Ordinal);
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

        internal (CombatSnapshot? Snapshot, RunSnapshot? Run) ResolveDashboardData(DashboardDataScope scope)
        {
            lock (_dashboardDataGate)
            {
                if (_cachedSnapshotRevision != _snapshotRevision)
                {
                    _cachedRun = Main.Repository.GetLiveRun(true);
                    _cachedCombatSnapshot = _cachedRun is { Combats.Count: > 0 }
                        ? _cachedRun.Combats[^1]
                        : Main.Repository.GetLiveCombat(true);
                    _cachedRunSnapshot = null;
                    _cachedSnapshotRevision = _snapshotRevision;
                }

                if (scope == DashboardDataScope.CurrentRun)
                    _cachedRunSnapshot ??= SnapshotAggregator.Combine(_cachedRun);
                return (scope == DashboardDataScope.CurrentRun ? _cachedRunSnapshot : _cachedCombatSnapshot,
                    _cachedRun);
            }
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
                        : new(options.Parameters, StringComparer.Ordinal),
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
            lock (_dashboardDataGate)
            {
                _snapshotRevision++;
            }

            UpdateVisibility();
            foreach (var window in _windows.Values)
                window.MarkDirty();
            _analysisCenter?.MarkDirty();
        }

        private void UpdateVisibility()
        {
            var settings = ModData.Settings;
            var runManager = RunManager.Instance;
            var hasLiveCombat = Main.Repository.GetLiveCombat(false) != null;
            var isRunCompletionView = runManager.IsInProgress &&
                                      (runManager.IsGameOver ||
                                       runManager.DebugOnlyGetState()?.CurrentRoom?.IsVictoryRoom == true);
            var hasCompletedCombat = isRunCompletionView &&
                                     Main.Repository.GetLiveRun(false)?.Combats.Count > 0;
            var showFloatingDashboards = settings.OverlayEnabled && runManager.IsInProgress &&
                                         (hasLiveCombat || hasCompletedCombat);
            foreach (var window in _windows.Values)
                window.Visible = showFloatingDashboards;
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
    }
}
