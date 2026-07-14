// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Data.Models;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class DashboardWindow : PanelContainer
    {
        private const float DefaultHeaderHeight = 38f;
        private const int DashboardPopupMaxHeight = 420;
        private const float HeaderVerticalPadding = 2f;
        private const float OpacityTransitionResponse = 18f;
        private const float OpacityTransitionSnapDistance = 0.002f;
        private const double OpacityTransitionStyleInterval = 0.05d;
        private const double PointerExitGraceSeconds = 0.08d;
        private const double TouchRevealHoldSeconds = 1.5d;
        private const float TitleSelectorHeight = 34f;

        private readonly HashSet<int> _activeTouchIndices = [];
        private readonly List<Control> _resizeHandles = [];
        private ColorRect _accent = null!;
        private MarginContainer _body = null!;
        private DashboardSelection[] _dashboardSelections = [];
        private DashboardDefinition _definition = null!;
        private bool _dirty = true;
        private bool _dragging;
        private Vector2 _dragOffset;
        private Label _error = null!;
        private HBoxContainer _header = null!;
        private float _headerHeight = DefaultHeaderHeight;
        private DashboardHost _host = null!;
        private float _opacityReveal;
        private StyleBoxFlat? _panelStyle;
        private Dictionary<string, string>? _parametersBeforePreview;
        private double _pointerExitGraceRemaining;
        private bool _pointerInside;
        private double _refreshDelay;
        private DashboardRegistry _registry = null!;
        private IDashboardRenderer _renderer = null!;
        private bool _rendererDisposed;
        private string? _rendererError;
        private ResizeEdge _resizeEdge;
        private Control _resizeLayer = null!;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartPosition;
        private Vector2 _resizeStartSize;
        private DashboardWindowSettings _state = null!;
        private DashboardDropdown _title = null!;
        private bool _touchPointerMode;
        private double _touchRevealRemaining;

        internal string InstanceId => _state.InstanceId;
        internal string DashboardId => _state.DashboardId;

        internal DashboardWindowInfo Info => new(_state.InstanceId, _state.DashboardId, _state.Scope,
            _state.StyleId, _state.IsCollapsed, _state.IsLocked)
        {
            Parameters = new Dictionary<string, string>(_state.Parameters, StringComparer.Ordinal),
        };

        private IDashboardRendererPresentation? Presentation => _renderer as IDashboardRendererPresentation;

        internal void Initialize(
            DashboardHost host,
            DashboardRegistry registry,
            DashboardDefinition definition,
            IDashboardRenderer renderer,
            DashboardWindowSettings state)
        {
            _host = host;
            _registry = registry;
            _definition = definition;
            _renderer = renderer;
            _state = state;
            Name = $"Dashboard_{state.InstanceId}";
            MouseFilter = MouseFilterEnum.Stop;
            BuildUi();
        }

        public override void _Ready()
        {
            ApplyGlobalSettings(true);
            GetViewport().SizeChanged += OnViewportSizeChanged;
            SetProcessInput(true);
            SetProcess(true);
            Callable.From(RestoreGeometryAfterLayout).CallDeferred();
        }

        public override void _ExitTree()
        {
            GetViewport().SizeChanged -= OnViewportSizeChanged;
        }

        public override void _Input(InputEvent input)
        {
            UpdateTouchInteraction(input);
            if (!_dragging && _resizeEdge == ResizeEdge.None)
                return;
            switch (input)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    FinishPointerInteraction();
                    break;
                case InputEventMouseMotion motion when _dragging:
                    Position = ClampPosition(motion.GlobalPosition - _dragOffset);
                    break;
                case InputEventMouseMotion motion when _resizeEdge != ResizeEdge.None:
                    ResizeTo(motion.GlobalPosition);
                    break;
            }
        }

        public override void _Process(double delta)
        {
            _touchRevealRemaining = Math.Max(0d, _touchRevealRemaining - delta);
            _pointerExitGraceRemaining = Math.Max(0d, _pointerExitGraceRemaining - delta);
            UpdatePointerInside();
            UpdateOpacityTransition(delta);
            if ((_dragging || _resizeEdge != ResizeEdge.None) &&
                !Input.IsMouseButtonPressed(MouseButton.Left))
                FinishPointerInteraction();
            if (_rendererDisposed)
                return;
            _refreshDelay -= delta;
            if (!_dirty || _refreshDelay > 0d || _state.IsCollapsed)
                return;
            _refreshDelay = 0.15d;
            _dirty = false;
            var style = ResolveStyle();
            try
            {
                _renderer.Refresh(new(
                    DashboardHost.ResolveSnapshot(_state.Scope),
                    DashboardHost.ResolveRun(),
                    _state.Scope,
                    style,
                    _state.Parameters,
                    ModData.Settings.ShowPercentages,
                    SetParameter));
                ApplyButtonStyle(_renderer.View, style,
                    DashboardPresentation.ControlDensity(_state.Parameters));
                _renderer.View.Visible = true;
                _error.Visible = false;
                _rendererError = null;
                UpdatePresentation(style);
            }
            catch (Exception exception)
            {
                _renderer.View.Visible = false;
                _error.Text = ModLocalization.Format("analysis.rendererFailed", "Renderer failed: {0}",
                    exception.Message);
                _error.Visible = true;
                _refreshDelay = 1d;
                if (_rendererError == exception.Message)
                    return;
                _rendererError = exception.Message;
                Main.Logger.Error($"Dashboard '{_definition.Id}' renderer failed: {exception}");
            }
        }

        internal void ApplyGlobalSettings(bool forceLayout)
        {
            var settings = ModData.Settings;
            Scale = Vector2.One * Math.Clamp(settings.ScalePercent / 100f, 0.65f, 1.75f);
            _state.Width = Math.Clamp(_state.Width, Math.Max(300f, _definition.MinimumWidth), 1100f);
            _state.Height = Math.Clamp(_state.Height, Math.Max(180f, _definition.MinimumHeight), 900f);
            ApplyStyle();
            if (forceLayout || (!_dragging && _resizeEdge == ResizeEdge.None))
                ApplyStoredGeometry();
            _body.Visible = !_state.IsCollapsed;
            _header.MouseDefaultCursorShape = _state.IsLocked ? CursorShape.Arrow : CursorShape.Move;
            foreach (var handle in _resizeHandles)
                handle.MouseFilter = _state is { IsLocked: false, IsCollapsed: false }
                    ? MouseFilterEnum.Stop
                    : MouseFilterEnum.Ignore;
            MarkDirty();
        }

        internal void RebuildMenus()
        {
            RebuildDashboardSelector();
            ApplyStyle();
        }

        internal void MarkDirty()
        {
            _dirty = true;
        }

        internal void DisposeRenderer()
        {
            if (_rendererDisposed)
                return;
            _rendererDisposed = true;
            SetProcess(false);
            _renderer.Dispose();
        }

        internal bool SwitchDashboard(
            IDashboardProvider provider,
            string styleId,
            IReadOnlyDictionary<string, string> parameters)
        {
            IDashboardRenderer nextRenderer;
            try
            {
                nextRenderer = provider.CreateRenderer();
            }
            catch (Exception exception)
            {
                Main.Logger.Error($"Could not create dashboard '{provider.Definition.Id}': {exception}");
                return false;
            }

            var previousRenderer = _renderer;
            var previousView = previousRenderer.View;
            _renderer = nextRenderer;
            _definition = provider.Definition;
            _state.DashboardId = provider.Definition.Id;
            _state.StyleId = styleId;
            _state.Parameters = new(parameters, StringComparer.Ordinal);
            _rendererDisposed = false;
            _rendererError = null;
            _refreshDelay = 0d;
            _error.Visible = false;
            _body.RemoveChild(previousView);
            previousRenderer.Dispose();
            if (IsInstanceValid(previousView))
                previousView.QueueFree();
            _body.AddChild(nextRenderer.View);
            _body.MoveChild(nextRenderer.View, 0);
            Save();
            RebuildDashboardSelector();
            ApplyGlobalSettings(false);
            SetProcess(true);
            MarkDirty();
            return true;
        }

        internal void FocusWindow()
        {
            MoveToFront();
        }

        internal void Configure(
            DashboardDataScope scope,
            string styleId,
            IReadOnlyDictionary<string, string> parameters)
        {
            _parametersBeforePreview = null;
            _state.Scope = scope;
            _state.StyleId = styleId;
            _state.Parameters = new(parameters, StringComparer.Ordinal);
            Save();
            RebuildDashboardSelector();
            ApplyGlobalSettings(false);
        }

        internal void PreviewParameters(IReadOnlyDictionary<string, string> parameters)
        {
            _parametersBeforePreview ??= new(_state.Parameters, StringComparer.Ordinal);
            _state.Parameters = new(parameters, StringComparer.Ordinal);
            ApplyStyle();
            MarkDirty();
        }

        internal void RestorePreviewParameters()
        {
            if (_parametersBeforePreview == null)
                return;
            _state.Parameters = _parametersBeforePreview;
            _parametersBeforePreview = null;
            ApplyStyle();
            MarkDirty();
        }

        internal bool ContainsScreenPoint(Vector2 point)
        {
            return Visible && GetGlobalRect().HasPoint(point);
        }

        private void BuildUi()
        {
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            root.AddThemeConstantOverride("separation", 0);
            AddChild(root);

            _header = new()
            {
                CustomMinimumSize = new(0, DefaultHeaderHeight),
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.Move,
            };
            _header.AddThemeConstantOverride("separation", 5);
            _header.GuiInput += OnHeaderInput;
            root.AddChild(_header);
            _accent = new() { CustomMinimumSize = new(4, 0), MouseFilter = MouseFilterEnum.Ignore };
            _header.AddChild(_accent);
            var grip = new Label
            {
                Text = "⋮⋮",
                CustomMinimumSize = new(18, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _header.AddChild(grip);
            _title = new()
            {
                CustomMinimumSize = new(0, TitleSelectorHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                FocusMode = FocusModeEnum.None,
                TooltipText = ModLocalization.Get("dashboard.switchContent", "Switch dashboard content"),
                MaxPopupHeight = DashboardPopupMaxHeight,
                MinimumPopupWidth = 320,
            };
            _title.ApplyStyle(density: DashboardControlDensity.Compact);
            _title.CustomMinimumSize = new(0, TitleSelectorHeight);
            _title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.WindowTitleFontSize);
            _title.ItemSelected += OnDashboardSelected;
            _header.AddChild(_title);
            RebuildDashboardSelector();
            _header.AddChild(HeaderButton("＋",
                ModLocalization.Get("dashboard.manage", "Manage dashboards"), _host.ToggleManager));
            _header.AddChild(HeaderButton("—", ModLocalization.Get("overlay.collapse", "Collapse"),
                ToggleCollapsed));
            _header.AddChild(HeaderButton("×", ModLocalization.Get("dialog.close", "Close"),
                () => _host.CloseWindow(_state.InstanceId)));

            _body = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            _body.AddThemeConstantOverride("margin_left", 9);
            _body.AddThemeConstantOverride("margin_top", 7);
            _body.AddThemeConstantOverride("margin_right", 9);
            _body.AddThemeConstantOverride("margin_bottom", 9);
            _body.AddChild(_renderer.View);
            _error = new()
            {
                Visible = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new("F39AA8FF"),
            };
            _error.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            _body.AddChild(_error);
            root.AddChild(_body);
            _resizeLayer = new()
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(_resizeLayer);
            AddResizeHandles();
        }

        private void AddResizeHandles()
        {
            AddResizeHandle(ResizeEdge.Top, 0f, 0f, 1f, 0f, 10f, 6f, CursorShape.Vsize);
            AddResizeHandle(ResizeEdge.Bottom, 0f, 1f, 1f, 1f, 10f, 6f, CursorShape.Vsize);
            AddResizeHandle(ResizeEdge.Left, 0f, 0f, 0f, 1f, 6f, 10f, CursorShape.Hsize);
            AddResizeHandle(ResizeEdge.Right, 1f, 0f, 1f, 1f, 6f, 10f, CursorShape.Hsize);
            AddResizeHandle(ResizeEdge.Top | ResizeEdge.Left, 0f, 0f, 0f, 0f, 13f, 13f, CursorShape.Fdiagsize);
            AddResizeHandle(ResizeEdge.Top | ResizeEdge.Right, 1f, 0f, 1f, 0f, 13f, 13f,
                CursorShape.Bdiagsize);
            AddResizeHandle(ResizeEdge.Bottom | ResizeEdge.Left, 0f, 1f, 0f, 1f, 13f, 13f,
                CursorShape.Bdiagsize);
            AddResizeHandle(ResizeEdge.Bottom | ResizeEdge.Right, 1f, 1f, 1f, 1f, 13f, 13f,
                CursorShape.Fdiagsize);
        }

        private void AddResizeHandle(
            ResizeEdge edge,
            float anchorLeft,
            float anchorTop,
            float anchorRight,
            float anchorBottom,
            float thicknessX,
            float thicknessY,
            CursorShape cursor)
        {
            var handle = new Control
            {
                LayoutMode = 0,
                AnchorLeft = anchorLeft,
                AnchorTop = anchorTop,
                AnchorRight = anchorRight,
                AnchorBottom = anchorBottom,
                MouseDefaultCursorShape = cursor,
            };
            var leftSide = edge.HasFlag(ResizeEdge.Left);
            var rightSide = edge.HasFlag(ResizeEdge.Right);
            var topSide = edge.HasFlag(ResizeEdge.Top);
            var bottomSide = edge.HasFlag(ResizeEdge.Bottom);
            handle.OffsetLeft = leftSide ? 0f : rightSide ? -thicknessX : thicknessX;
            handle.OffsetRight = leftSide ? thicknessX : rightSide ? 0f : -thicknessX;
            handle.OffsetTop = topSide ? 0f : bottomSide ? -thicknessY : thicknessY;
            handle.OffsetBottom = topSide ? thicknessY : bottomSide ? 0f : -thicknessY;
            handle.GuiInput += input => OnResizeInput(input, edge);
            _resizeLayer.AddChild(handle);
            _resizeHandles.Add(handle);
        }

        private void ApplyStyle()
        {
            var style = ResolveStyle();
            var density = DashboardPresentation.ControlDensity(_state.Parameters);
            if (_panelStyle == null)
            {
                _panelStyle = new()
                {
                    BorderWidthLeft = 1,
                    BorderWidthTop = 1,
                    BorderWidthRight = 1,
                    BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 5,
                    CornerRadiusTopRight = 5,
                    CornerRadiusBottomLeft = 5,
                    CornerRadiusBottomRight = 5,
                    ShadowSize = 5,
                };
                AddThemeStyleboxOverride("panel", _panelStyle);
            }

            _panelStyle.BorderColor = DashboardRendererBase.ColorOf(style.BorderColor) with { A = 0.72f };
            ApplyOpacityVisuals(DashboardRendererBase.ColorOf(style.BackgroundColor));
            _title.ApplyStyle(style, DashboardControlDensity.Compact);
            var singleLine = DashboardPresentation.SingleLine(_state.Parameters);
            var titleFontSize = style.FontSize;
            var titleHeight = Math.Max(30f, titleFontSize + 8f);
            var titleControlHeight = Math.Max(TitleSelectorHeight, titleHeight);
            _title.CustomMinimumSize = new(0, titleControlHeight);
            _title.AddThemeFontSizeOverride("font_size", titleFontSize);
            _headerHeight = titleControlHeight + HeaderVerticalPadding * 2f;
            _header.CustomMinimumSize = new(0, _headerHeight);
            _accent.Color = DashboardRendererBase.ColorOf(Presentation?.AccentColor ?? AccentForDashboard(style));
            _body.AddThemeConstantOverride("margin_left", singleLine ? 6 : 9);
            _body.AddThemeConstantOverride("margin_top", singleLine ? 4 : 7);
            _body.AddThemeConstantOverride("margin_right", singleLine ? 6 : 9);
            _body.AddThemeConstantOverride("margin_bottom", singleLine ? 6 : 9);
            ApplyButtonStyle(_renderer.View, style, density);
        }

        private DashboardStyleDefinition ResolveStyle()
        {
            return DashboardPresentation.ResolveStyle(
                _registry.ResolveStyle(_state.StyleId, _definition.DefaultStyleId), _state.Parameters,
                ModData.Settings.OpacityPercent, EffectiveBackgroundOpacity());
        }

        private void UpdatePointerInside()
        {
            if (_activeTouchIndices.Count > 0 || _touchRevealRemaining > 0d)
            {
                _pointerInside = true;
                return;
            }

            if (_touchPointerMode)
            {
                _pointerInside = false;
                return;
            }

            var pointerPosition = GetViewport().GetMousePosition();
            if (_host.IsTopmostWindowAt(this, pointerPosition))
            {
                _pointerExitGraceRemaining = PointerExitGraceSeconds;
                _pointerInside = true;
                return;
            }

            var hoveredControl = GetViewport().GuiGetHoveredControl();
            if (hoveredControl != null &&
                (ReferenceEquals(hoveredControl, this) || IsAncestorOf(hoveredControl)))
            {
                _pointerExitGraceRemaining = PointerExitGraceSeconds;
                _pointerInside = true;
                return;
            }

            _pointerInside = _pointerExitGraceRemaining > 0d;
        }

        private void UpdateTouchInteraction(InputEvent input)
        {
            switch (input)
            {
                case InputEventScreenTouch touch:
                    _touchPointerMode = true;
                    if (touch.Pressed)
                    {
                        if (_host.IsTopmostWindowAt(this, touch.Position))
                        {
                            _activeTouchIndices.Add(touch.Index);
                            _touchRevealRemaining = TouchRevealHoldSeconds;
                        }
                    }
                    else if (_activeTouchIndices.Remove(touch.Index))
                    {
                        _touchRevealRemaining = TouchRevealHoldSeconds;
                    }

                    break;
                case InputEventScreenDrag drag:
                    _touchPointerMode = true;
                    if (_activeTouchIndices.Contains(drag.Index) || _host.IsTopmostWindowAt(this, drag.Position))
                    {
                        _activeTouchIndices.Add(drag.Index);
                        _touchRevealRemaining = TouchRevealHoldSeconds;
                    }

                    break;
                case InputEventMouse { Device: >= 0 }:
                    _touchPointerMode = false;
                    break;
            }
        }

        private void UpdateOpacityTransition(double delta)
        {
            var target = DashboardPresentation.FullOpacityOnHover(_state.Parameters) && _pointerInside ? 1f : 0f;
            if (Mathf.IsEqualApprox(_opacityReveal, target))
                return;
            var blend = 1f - MathF.Exp(-OpacityTransitionResponse * (float)Math.Max(0d, delta));
            _opacityReveal = Mathf.Lerp(_opacityReveal, target, blend);
            if (MathF.Abs(_opacityReveal - target) <= OpacityTransitionSnapDistance)
                _opacityReveal = target;
            ApplyOpacityVisuals();
            MarkDirty();
            _refreshDelay = Math.Min(_refreshDelay, OpacityTransitionStyleInterval);
        }

        private void ApplyOpacityVisuals()
        {
            var style = _registry.ResolveStyle(_state.StyleId, _definition.DefaultStyleId);
            var background = DashboardRendererBase.ColorOf(style.BackgroundColor);
            background.A *= EffectiveBackgroundOpacity();
            ApplyOpacityVisuals(background);
        }

        private void ApplyOpacityVisuals(Color background)
        {
            Modulate = Colors.White with { A = EffectiveWindowOpacity() };
            if (_panelStyle == null)
                return;
            _panelStyle.BgColor = background;
            _panelStyle.ShadowColor = new(0f, 0f, 0f, 0.5f * EffectiveBackgroundOpacity());
        }

        private float EffectiveWindowOpacity()
        {
            var configured = DashboardPresentation.WindowOpacity(_state.Parameters,
                ModData.Settings.WindowOpacityPercent);
            return Mathf.Lerp(configured, 1f, _opacityReveal);
        }

        private float EffectiveBackgroundOpacity()
        {
            var configured = DashboardPresentation.BackgroundOpacity(_state.Parameters,
                ModData.Settings.OpacityPercent);
            return Mathf.Lerp(configured, 1f, _opacityReveal);
        }

        private void UpdatePresentation(DashboardStyleDefinition style)
        {
            var title = Presentation?.Title ??
                        ModLocalization.Get(_definition.TitleLocalizationKey, _definition.FallbackTitle);
            var subtitle = Presentation?.Subtitle ?? ScopeName(_state.Scope);
            _title.Text = title;
            if (_renderer is IDashboardRendererFooterPresentation footer)
                footer.SetFooterContext(subtitle);
            _accent.Color = DashboardRendererBase.ColorOf(Presentation?.AccentColor ?? AccentForDashboard(style));
        }

        private string AccentForDashboard(DashboardStyleDefinition style)
        {
            return _state.DashboardId switch
            {
                BuiltInDashboardIds.Meter or BuiltInDashboardIds.DamageBreakdown => style.NegativeColor,
                BuiltInDashboardIds.ReceivedDamage => style.WarningColor,
                BuiltInDashboardIds.Timeline => DashboardRendererBase.AccentAt(style, 1),
                _ => DashboardRendererBase.AccentAt(style, 3),
            };
        }

        private static string ScopeName(DashboardDataScope scope)
        {
            return scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRun", "Current run")
                : ModLocalization.Get("overlay.currentCombat", "Current combat");
        }

        private void ToggleCollapsed()
        {
            _state.IsCollapsed = !_state.IsCollapsed;
            Save();
            ApplyGlobalSettings(false);
        }

        internal void ToggleLock()
        {
            _state.IsLocked = !_state.IsLocked;
            Save();
            ApplyGlobalSettings(false);
        }

        internal void ResetGeometry()
        {
            _dragging = false;
            _resizeEdge = ResizeEdge.None;
            _state.PositionX = 0f;
            _state.PositionY = 92f;
            _state.Width = _definition.DefaultWidth;
            _state.Height = _definition.DefaultHeight;
            _state.HasCustomPosition = false;
            Save();
            ApplyGlobalSettings(true);
        }

        private void SetParameter(string key, string? value)
        {
            if (value == null)
                _state.Parameters.Remove(key);
            else
                _state.Parameters[key] = value;
            Save();
            if (key == "metric_id")
                RebuildDashboardSelector();
            MarkDirty();
        }

        private void RebuildDashboardSelector()
        {
            var definitions = _registry.Definitions
                .OrderBy(definition => DashboardOrder(definition.Id))
                .ThenBy(definition => ModLocalization.Get(definition.TitleLocalizationKey,
                    definition.FallbackTitle), StringComparer.CurrentCulture)
                .ToArray();
            var selections = new List<DashboardSelection>();
            foreach (var definition in definitions)
            {
                if (definition.Id == BuiltInDashboardIds.Meter)
                {
                    selections.AddRange(Main.Api.MetricDefinitions
                        .OrderBy(metric => metric.Category, StringComparer.Ordinal)
                        .ThenBy(metric => ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName),
                            StringComparer.CurrentCulture)
                        .Select(metric => new DashboardSelection(
                            definition.Id,
                            ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName),
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [DashboardParameterIds.MetricId] = metric.Id,
                            })));
                    continue;
                }

                selections.Add(new(definition.Id,
                    ModLocalization.Get(definition.TitleLocalizationKey, definition.FallbackTitle),
                    new Dictionary<string, string>(StringComparer.Ordinal)));
            }

            _dashboardSelections = selections.ToArray();
            _title.Clear();
            var selected = -1;
            for (var index = 0; index < _dashboardSelections.Length; index++)
            {
                var selection = _dashboardSelections[index];
                _title.AddItem(selection.Title);
                if (MatchesCurrentSelection(selection))
                    selected = index;
            }

            if (selected >= 0)
                _title.Select(selected);
        }

        private void OnDashboardSelected(long index)
        {
            if (index < 0 || index >= _dashboardSelections.Length)
                return;
            var selection = _dashboardSelections[(int)index];
            if (MatchesCurrentSelection(selection))
                return;
            if (selection.DashboardId == _state.DashboardId)
            {
                _state.Parameters = DashboardPresentation.MergeSharedParameters(_state.Parameters,
                    selection.Parameters);
                Save();
                RebuildDashboardSelector();
                MarkDirty();
                return;
            }

            var parameters = DashboardPresentation.MergeSharedParameters(_state.Parameters, selection.Parameters);
            if (!_host.SwitchWindowDashboard(_state.InstanceId, selection.DashboardId, parameters))
                RebuildDashboardSelector();
        }

        private bool MatchesCurrentSelection(DashboardSelection selection)
        {
            if (selection.DashboardId != _state.DashboardId)
                return false;
            if (selection.DashboardId != BuiltInDashboardIds.Meter)
                return true;
            var selectedMetric = selection.Parameters.GetValueOrDefault(DashboardParameterIds.MetricId,
                MetricIds.DamageDealt);
            return _state.Parameters.GetValueOrDefault(DashboardParameterIds.MetricId, MetricIds.DamageDealt) ==
                   selectedMetric;
        }

        private static int DashboardOrder(string dashboardId)
        {
            return dashboardId switch
            {
                BuiltInDashboardIds.Overview => 0,
                BuiltInDashboardIds.Meter => 1,
                BuiltInDashboardIds.DamageBreakdown => 2,
                BuiltInDashboardIds.PlayerPerformance => 3,
                BuiltInDashboardIds.SourceAnalysis => 4,
                BuiltInDashboardIds.ContributionAnalysis => 5,
                BuiltInDashboardIds.DefenseResources => 6,
                BuiltInDashboardIds.CardsAndEffects => 7,
                BuiltInDashboardIds.TurnAnalysis => 8,
                BuiltInDashboardIds.Timeline => 9,
                BuiltInDashboardIds.CardLog => 10,
                BuiltInDashboardIds.ReceivedDamage => 11,
                BuiltInDashboardIds.RunTrends => 12,
                BuiltInDashboardIds.CombatRecords => 13,
                _ => 100,
            };
        }

        private void OnHeaderInput(InputEvent input)
        {
            if (_state.IsLocked)
                return;
            switch (input)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse:
                    DetachFromDefaultAnchor();
                    _dragging = true;
                    _dragOffset = mouse.GlobalPosition - Position;
                    MoveToFront();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    FinishPointerInteraction();
                    break;
                case InputEventMouseMotion motion when _dragging:
                    Position = ClampPosition(motion.GlobalPosition - _dragOffset);
                    break;
            }
        }

        private void OnResizeInput(InputEvent input, ResizeEdge edge)
        {
            if (_state.IsLocked)
                return;
            switch (input)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse:
                    DetachFromDefaultAnchor();
                    _resizeEdge = edge;
                    _resizeStartMouse = mouse.GlobalPosition;
                    _resizeStartSize = Size;
                    _resizeStartPosition = Position;
                    MoveToFront();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    FinishPointerInteraction();
                    break;
                case InputEventMouseMotion motion when _resizeEdge != ResizeEdge.None:
                    ResizeTo(motion.GlobalPosition);
                    break;
            }
        }

        private void ResizeTo(Vector2 mousePosition)
        {
            var pixelDelta = mousePosition - _resizeStartMouse;
            var localDelta = pixelDelta / Scale.X;
            var minimumWidth = Math.Max(300f, _definition.MinimumWidth);
            var minimumHeight = Math.Max(180f, _definition.MinimumHeight);
            var width = _resizeStartSize.X;
            var height = _resizeStartSize.Y;
            var position = _resizeStartPosition;
            if (_resizeEdge.HasFlag(ResizeEdge.Right))
                width = Math.Clamp(_resizeStartSize.X + localDelta.X, minimumWidth, 1100f);
            if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
                height = Math.Clamp(_resizeStartSize.Y + localDelta.Y, minimumHeight, 900f);
            if (_resizeEdge.HasFlag(ResizeEdge.Left))
            {
                width = Math.Clamp(_resizeStartSize.X - localDelta.X, minimumWidth, 1100f);
                position.X = _resizeStartPosition.X + (_resizeStartSize.X - width) * Scale.X;
            }

            if (_resizeEdge.HasFlag(ResizeEdge.Top))
            {
                height = Math.Clamp(_resizeStartSize.Y - localDelta.Y, minimumHeight, 900f);
                position.Y = _resizeStartPosition.Y + (_resizeStartSize.Y - height) * Scale.Y;
            }

            Size = new(width, height);
            Position = ClampPosition(position);
        }

        private void SaveGeometry()
        {
            _state.PositionX = Position.X;
            _state.PositionY = Position.Y;
            _state.Width = Size.X;
            if (!_state.IsCollapsed)
                _state.Height = Size.Y;
            _state.HasCustomPosition = true;
            Save();
        }

        private void FinishPointerInteraction()
        {
            if (!_dragging && _resizeEdge == ResizeEdge.None)
                return;
            _dragging = false;
            _resizeEdge = ResizeEdge.None;
            SaveGeometry();
        }

        private void OnViewportSizeChanged()
        {
            Callable.From(RestoreGeometryAfterLayout).CallDeferred();
        }

        private void RestoreGeometryAfterLayout()
        {
            if (!IsInsideTree() || _dragging || _resizeEdge != ResizeEdge.None)
                return;
            ApplyStoredGeometry();
        }

        private void Save()
        {
            DashboardHost.SaveWindow(_state);
        }

        private void ApplyStoredGeometry()
        {
            if (_state.HasCustomPosition)
            {
                SetTopLeftAnchors();
                Size = new(_state.Width, _state.IsCollapsed ? _headerHeight : _state.Height);
                Position = ClampPosition(new(_state.PositionX, _state.PositionY));
                return;
            }

            AnchorLeft = 1f;
            AnchorTop = 0f;
            AnchorRight = 1f;
            AnchorBottom = 0f;
            OffsetLeft = -_state.Width - 24f;
            OffsetTop = _state.PositionY;
            OffsetRight = -24f;
            OffsetBottom = _state.PositionY + (_state.IsCollapsed ? _headerHeight : _state.Height);
        }

        private void DetachFromDefaultAnchor()
        {
            if (_state.HasCustomPosition)
                return;
            var position = Position;
            var size = Size;
            SetTopLeftAnchors();
            Position = position;
            Size = size;
            _state.HasCustomPosition = true;
        }

        private void SetTopLeftAnchors()
        {
            AnchorLeft = 0f;
            AnchorTop = 0f;
            AnchorRight = 0f;
            AnchorBottom = 0f;
        }

        private Vector2 ClampPosition(Vector2 position)
        {
            var viewport = GetViewport().GetVisibleRect().Size;
            var scaled = Size * Scale;
            return new(
                Math.Clamp(position.X, 0f, Math.Max(0f, viewport.X - Math.Min(84f, scaled.X))),
                Math.Clamp(position.Y, 0f, Math.Max(0f, viewport.Y - Math.Min(_headerHeight, scaled.Y))));
        }

        private static Button HeaderButton(string text, string tooltip, Action action)
        {
            var button = new Button
            {
                Text = text,
                TooltipText = tooltip,
                CustomMinimumSize = new(34, 34),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                FocusMode = FocusModeEnum.None,
            };
            DashboardControlTheme.ApplyIconButton(button,
                text == "×" ? DashboardButtonKind.Danger : DashboardButtonKind.Subtle, compact: true);
            button.CustomMinimumSize = new(34, 34);
            button.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
            button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            button.Pressed += action;
            return button;
        }

        private static void ApplyButtonStyle(
            Node node,
            DashboardStyleDefinition style,
            DashboardControlDensity density)
        {
            foreach (var child in node.GetChildren())
            {
                switch (child)
                {
                    case DashboardDropdown dropdown:
                        dropdown.ApplyStyle(style, density);
                        dropdown.AddThemeFontSizeOverride("font_size", style.FontSize);
                        continue;
                    case Button button:
                        DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Standard, density, style);
                        button.AddThemeFontSizeOverride("font_size", style.FontSize);
                        break;
                    case LineEdit lineEdit:
                        DashboardControlTheme.ApplySearch(lineEdit, style, density);
                        lineEdit.AddThemeFontSizeOverride("font_size", style.FontSize);
                        break;
                }

                ApplyButtonStyle(child, style, density);
            }
        }

        [Flags]
        private enum ResizeEdge
        {
            None = 0,
            Top = 1,
            Right = 2,
            Bottom = 4,
            Left = 8,
        }

        private sealed record DashboardSelection(
            string DashboardId,
            string Title,
            IReadOnlyDictionary<string, string> Parameters);
    }
}
