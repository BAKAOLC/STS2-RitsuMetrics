// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuLib.Settings;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class DashboardManagerPanel : PanelContainer
    {
        private static readonly Vector2 ManagerSize = new(600f, 560f);
        private DashboardConfigurationDialog _configuration = null!;
        private Label _count = null!;
        private bool _dragging;
        private Vector2 _dragOffset;
        private DashboardHost _host = null!;
        private DashboardRegistry _registry = null!;
        private VBoxContainer _windows = null!;

        internal void Initialize(DashboardHost host, DashboardRegistry registry)
        {
            _host = host;
            _registry = registry;
        }

        public override void _Ready()
        {
            Hide();
            LayerSetup();
            BuildUi();
            RebuildOptions();
            RefreshWindows();
        }

        internal void Toggle()
        {
            if (Visible)
            {
                _configuration.Dismiss();
                Hide();
                return;
            }

            RebuildOptions();
            RefreshWindows();
            CenterInViewport();
            Show();
            MoveToFront();
        }

        internal bool ContainsScreenPoint(Vector2 point)
        {
            return Visible && GetGlobalRect().HasPoint(point);
        }

        internal void HideForSystemMenu()
        {
            if (!Visible)
                return;
            _configuration.Dismiss();
            Hide();
        }

        internal void RebuildOptions()
        {
            if (IsInstanceValid(_configuration))
                _configuration.RebuildOptions();
        }

        internal void RefreshWindows()
        {
            if (!IsInstanceValid(_windows))
                return;
            _count.Text = _host.WindowInfos.Count.ToString(CultureInfo.InvariantCulture);
            Clear(_windows);
            if (_host.WindowInfos.Count == 0)
            {
                var empty = new Label
                {
                    Text = ModLocalization.Get("dashboard.noOpenWindows", "No floating dashboards are open"),
                    CustomMinimumSize = new(0f, 90f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Modulate = new("91A1B6FF"),
                };
                empty.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
                _windows.AddChild(empty);
                return;
            }

            foreach (var info in _host.WindowInfos)
                _windows.AddChild(WindowCard(info));
        }

        private void LayerSetup()
        {
            CustomMinimumSize = ManagerSize;
            Size = ManagerSize;
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new("0C121CFB"),
                BorderColor = new("59718CEB"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 7,
                CornerRadiusTopRight = 7,
                CornerRadiusBottomLeft = 7,
                CornerRadiusBottomRight = 7,
                ContentMarginLeft = 14f,
                ContentMarginTop = 11f,
                ContentMarginRight = 14f,
                ContentMarginBottom = 14f,
                ShadowColor = new(0f, 0f, 0f, 0.65f),
                ShadowSize = 10,
            });
        }

        private void BuildUi()
        {
            _configuration = new();
            _configuration.Initialize(_registry, ApplyConfiguration, _host.PreviewWindowParameters,
                _host.RestoreWindowParameters);

            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            root.AddThemeConstantOverride("separation", 9);
            AddChild(root);

            var header = new HBoxContainer
            {
                CustomMinimumSize = new(0f, 42f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            header.GuiInput += OnHeaderInput;
            var title = new Label
            {
                Text = ModLocalization.Get("dashboard.manager", "Dashboard manager"),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SectionTitleFontSize);
            header.AddChild(title);
            var settings = SmallButton(DashboardIcon.Settings,
                ModLocalization.Get("dashboard.openSettings", "Open RitsuMetrics settings"));
            settings.Pressed += OpenSettings;
            header.AddChild(settings);
            var close = SmallButton(DashboardIcon.Close, ModLocalization.Get("dialog.close", "Close"),
                DashboardButtonKind.Danger);
            close.Pressed += () =>
            {
                _configuration.Dismiss();
                Hide();
            };
            header.AddChild(close);
            root.AddChild(header);
            root.AddChild(DashboardControlTheme.Separator());

            var analysis = new Button
            {
                Text = ModLocalization.Get("analysis.open", "Open analytics center"),
                CustomMinimumSize = new(0f, 44f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            DashboardControlTheme.ApplyButton(analysis, DashboardButtonKind.Primary);
            analysis.Pressed += _host.ToggleAnalysisCenter;
            root.AddChild(analysis);

            var listHeader = new HBoxContainer();
            var listTitle = new Label
            {
                Text = ModLocalization.Get("dashboard.currentWindows", "Current floating dashboards"),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            listTitle.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            listHeader.AddChild(listTitle);
            _count = new()
            {
                Text = _host.WindowInfos.Count.ToString(CultureInfo.InvariantCulture),
                Modulate = new("91A1B6FF"),
            };
            _count.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            listHeader.AddChild(_count);
            root.AddChild(listHeader);

            var scroll = new DashboardScrollContainer();
            _windows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _windows.AddThemeConstantOverride("separation", 7);
            scroll.SetContent(_windows);
            root.AddChild(scroll);
            root.AddChild(DashboardControlTheme.Separator());

            var create = new Button
            {
                Text = ModLocalization.Get("dashboard.new", "New floating dashboard"),
                CustomMinimumSize = new(0f, 44f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            DashboardControlTheme.ApplyButton(create, DashboardButtonKind.Primary);
            create.Pressed += _configuration.OpenCreate;
            root.AddChild(create);

            AddChild(_configuration);
        }

        private void OpenSettings()
        {
            var result = ModSettingsNavigator.RequestOpenByIds(ModConstants.ModId, null, null, null);
            if (result.Success)
                HideForSystemMenu();
            else
                Main.Logger.Warn($"Could not open RitsuMetrics settings: {result.Code}: {result.Message}");
        }

        private PanelContainer WindowCard(DashboardWindowInfo info)
        {
            var definition = _registry.Definitions.FirstOrDefault(item => item.Id == info.DashboardId);
            var panel = new PanelContainer { CustomMinimumSize = new(0f, 70f) };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new("141D2AEF"),
                BorderColor = new("344A63D8"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = 10f,
                ContentMarginTop = 7f,
                ContentMarginRight = 8f,
                ContentMarginBottom = 7f,
            });
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 5);
            panel.AddChild(row);
            var identity = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipContents = true,
            };
            identity.AddThemeConstantOverride("separation", -2);
            var name = new Label
            {
                Text = WindowName(info, definition),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeFontSizeOverride("font_size", DashboardControlTheme.BodyFontSize);
            identity.AddChild(name);
            var status = new Label
            {
                Text = WindowStatus(info),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                Modulate = new("95A7BDFF"),
            };
            status.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            identity.AddChild(status);
            row.AddChild(identity);

            var focus = SmallButton(DashboardIcon.Focus, ModLocalization.Get("dashboard.focus", "Focus"));
            focus.Pressed += () => _host.FocusWindow(info.InstanceId);
            row.AddChild(focus);
            var configure = SmallButton(DashboardIcon.Configure,
                ModLocalization.Get("dashboard.configure", "Configure"));
            configure.Pressed += () => _configuration.OpenEdit(info);
            row.AddChild(configure);
            var reset = SmallButton(DashboardIcon.ResetGeometry,
                ModLocalization.Get("dashboard.resetWindowGeometry", "Reset position and size"));
            reset.Pressed += () => _host.ResetWindowGeometry(info.InstanceId);
            row.AddChild(reset);
            var lockButton = SmallButton(info.IsLocked ? DashboardIcon.Unlock : DashboardIcon.Lock,
                info.IsLocked
                    ? ModLocalization.Get("overlay.unlock", "Unlock position and size")
                    : ModLocalization.Get("overlay.lock", "Lock position and size"));
            lockButton.Pressed += () => _host.ToggleWindowLock(info.InstanceId);
            row.AddChild(lockButton);
            var duplicate = SmallButton(DashboardIcon.Duplicate,
                ModLocalization.Get("dashboard.duplicate", "Duplicate"));
            duplicate.Pressed += () =>
            {
                var instanceId = _host.OpenWindow(info.DashboardId, new()
                {
                    Scope = info.Scope,
                    StyleId = info.StyleId,
                    Parameters = info.Parameters,
                });
                if (instanceId != null)
                    _host.FocusWindow(instanceId);
            };
            row.AddChild(duplicate);
            var close = SmallButton(DashboardIcon.Close, ModLocalization.Get("dialog.close", "Close"),
                DashboardButtonKind.Danger);
            close.Pressed += () => _host.CloseWindow(info.InstanceId);
            row.AddChild(close);
            return panel;
        }

        private void ApplyConfiguration(DashboardConfiguration configuration)
        {
            if (configuration.InstanceId != null)
            {
                _host.ConfigureWindow(configuration.InstanceId, configuration.Scope, configuration.StyleId,
                    configuration.Parameters);
                _host.FocusWindow(configuration.InstanceId);
            }
            else
            {
                var instanceId = _host.OpenWindow(configuration.DashboardId, new()
                {
                    Scope = configuration.Scope,
                    StyleId = configuration.StyleId,
                    Parameters = configuration.Parameters,
                });
                if (instanceId != null)
                    _host.FocusWindow(instanceId);
            }

            RefreshWindows();
            MoveToFront();
        }

        private static string WindowName(DashboardWindowInfo info, DashboardDefinition? definition)
        {
            if (info.DashboardId != BuiltInDashboardIds.Meter ||
                !info.Parameters.TryGetValue("metric_id", out var metricId) ||
                Main.Api.MetricDefinitions.FirstOrDefault(item => item.Id == metricId) is not { } metric)
                return definition == null
                    ? info.DashboardId
                    : ModLocalization.Get(definition.TitleLocalizationKey, definition.FallbackTitle);
            return ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName);
        }

        private static string WindowStatus(DashboardWindowInfo info)
        {
            var scope = info.Scope == DashboardDataScope.CurrentRun
                ? ModLocalization.Get("overlay.currentRun", "Current run")
                : ModLocalization.Get("overlay.currentCombat", "Current combat");
            return info.IsLocked
                ? $"{scope} · {ModLocalization.Get("dashboard.locked", "Locked")}"
                : scope;
        }

        private void CenterInViewport()
        {
            Size = ManagerSize;
            var viewport = GetViewport().GetVisibleRect().Size;
            Position = new(Math.Max(12f, (viewport.X - ManagerSize.X) / 2f),
                Math.Max(12f, (viewport.Y - ManagerSize.Y) / 2f));
        }

        private void OnHeaderInput(InputEvent input)
        {
            switch (input)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse:
                    _dragging = true;
                    _dragOffset = mouse.GlobalPosition - Position;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    _dragging = false;
                    break;
                case InputEventMouseMotion motion when _dragging:
                    var viewport = GetViewport().GetVisibleRect().Size;
                    var next = motion.GlobalPosition - _dragOffset;
                    Position = new(Math.Clamp(next.X, 0f, Math.Max(0f, viewport.X - Size.X)),
                        Math.Clamp(next.Y, 0f, Math.Max(0f, viewport.Y - 36f)));
                    break;
            }
        }

        private static Button SmallButton(DashboardIcon icon, string tooltip,
            DashboardButtonKind kind = DashboardButtonKind.Subtle)
        {
            var button = new Button
            {
                TooltipText = tooltip,
                CustomMinimumSize = new(38f, 38f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                FocusMode = FocusModeEnum.None,
            };
            DashboardControlTheme.ApplyIconButton(button, kind, compact: true);
            DashboardIcons.ApplyIconOnly(button, icon);
            return button;
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
