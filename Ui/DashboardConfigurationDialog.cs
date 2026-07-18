// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;
using STS2RitsuMetrics.Data;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed record DashboardConfiguration(
        string? InstanceId,
        string DashboardId,
        DashboardDataScope Scope,
        string StyleId,
        IReadOnlyDictionary<string, string> Parameters);

    internal sealed partial class DashboardConfigurationDialog : Control
    {
        private readonly DashboardPercentageSlider _backgroundOpacity;

        private readonly DashboardDropdown _dashboard;

        private readonly Label _description;
        private readonly DashboardDropdown _fontSize;

        private readonly string[] _fontSizes = Enumerable.Range(12, 13)
            .Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();

        private readonly Button _fullOpacityOnHover;

        private readonly DashboardDropdown _layout;

        private readonly string[] _layoutValues =
            [DashboardParameterValues.Standard, DashboardParameterValues.SingleLine];

        private readonly DashboardDropdown _metric;
        private readonly Control _metricField;
        private readonly DashboardDropdown _scope;
        private readonly Button _submit;
        private readonly Control _summonField;
        private readonly DashboardDropdown _summons;

        private readonly string[] _summonValues =
            [DashboardParameterValues.MergeSummons, DashboardParameterValues.SplitSummons];

        private readonly Label _title;
        private readonly DashboardPercentageSlider _windowOpacity;

        private string[] _dashboardIds = [];
        private string? _editingInstanceId;
        private Dictionary<string, string> _editingParameters = new(StringComparer.Ordinal);
        private string? _editingStyleId;
        private string[] _metricIds = [];
        private Action<string, IReadOnlyDictionary<string, string>>? _previewOpacity;
        private DashboardRegistry _registry = null!;
        private Action<string>? _restorePreview;
        private Action<DashboardConfiguration>? _submitted;

        internal DashboardConfigurationDialog()
        {
            Name = "DashboardConfigurationDialog";
            LayoutMode = 1;
            AnchorsPreset = (int)LayoutPreset.FullRect;
            MouseFilter = MouseFilterEnum.Stop;
            ZIndex = 400;
            Hide();

            var scrim = new ColorRect
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                Color = new("03070DCC"),
                MouseFilter = MouseFilterEnum.Stop,
            };
            scrim.GuiInput += OnScrimInput;
            AddChild(scrim);

            var center = new CenterContainer
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(center);
            var panel = new PanelContainer
            {
                CustomMinimumSize = new(640f, 520f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            panel.AddThemeStyleboxOverride("panel", DashboardControlTheme.DialogStyle());
            center.AddChild(panel);
            var content = new VBoxContainer();
            content.AddThemeConstantOverride("separation", 10);
            panel.AddChild(content);

            _title = new();
            DashboardControlTheme.ApplyDialogTitle(_title);
            content.AddChild(_title);
            content.AddChild(DashboardControlTheme.Separator());

            _dashboard = AddField(content, ModLocalization.Get("dashboard.content", "Content"), out _);
            _dashboard.ItemSelected += _ => UpdateSelectedDashboard();
            _description = new()
            {
                CustomMinimumSize = new(0f, 42f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            DashboardControlTheme.ApplySecondaryText(_description);
            content.AddChild(_description);
            var fields = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            fields.AddThemeConstantOverride("h_separation", 12);
            fields.AddThemeConstantOverride("v_separation", 8);
            content.AddChild(fields);
            _metric = AddFieldCell(fields, ModLocalization.Get("dashboard.metric", "Metric"), out _metricField);
            _scope = AddFieldCell(fields, ModLocalization.Get("dashboard.range", "Range"), out _);
            _scope.AddItem(ModLocalization.Get("overlay.currentCombat", "Current combat"));
            _scope.AddItem(ModLocalization.Get("overlay.currentRun", "Current run"));
            _fontSize = AddFieldCell(fields, ModLocalization.Get("dashboard.fontSize", "Font size"), out _);
            foreach (var value in _fontSizes)
                _fontSize.AddItem(value);
            _layout = AddFieldCell(fields, ModLocalization.Get("dashboard.layout", "Layout"), out _);
            _layout.AddItem(ModLocalization.Get("dashboard.layout.standard", "Standard"));
            _layout.AddItem(ModLocalization.Get("dashboard.layout.singleLine", "Single line"));
            _summons = AddFieldCell(fields,
                ModLocalization.Get("dashboard.summonDisplay", "Summon display"), out _summonField);
            _summons.AddItem(ModLocalization.Get("dashboard.summonDisplay.merge", "Merge into owner"));
            _summons.AddItem(ModLocalization.Get("dashboard.summonDisplay.split", "Show separately"));
            _windowOpacity = AddSliderFieldCell(fields,
                ModLocalization.Get("dashboard.windowOpacity", "Window opacity"), out _);
            _windowOpacity.Configure(20, 100, 1);
            _windowOpacity.ValueChanged += _ => PreviewOpacity();
            _backgroundOpacity = AddSliderFieldCell(fields,
                ModLocalization.Get("dashboard.backgroundOpacity", "Background opacity"), out _);
            _backgroundOpacity.Configure(0, 100, 1);
            _backgroundOpacity.ValueChanged += _ => PreviewOpacity();
            _fullOpacityOnHover = AddToggleFieldCell(fields,
                ModLocalization.Get("dashboard.fullOpacityOnHover", "Full opacity on hover"), out _);
            _fullOpacityOnHover.Toggled += enabled =>
            {
                UpdateFullOpacityOnHoverText(enabled);
                PreviewOpacity();
            };

            var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            actions.AddThemeConstantOverride("separation", 10);
            content.AddChild(actions);
            var defaults = new Button
            {
                Text = ModLocalization.Get("dashboard.restoreDefaults", "Restore defaults"),
                CustomMinimumSize = new(130f, 44f),
            };
            DashboardControlTheme.ApplyButton(defaults, DashboardButtonKind.Subtle);
            defaults.Pressed += RestoreDefaults;
            actions.AddChild(defaults);
            var cancel = new Button
            {
                Text = ModLocalization.Get("dashboard.cancelEdit", "Cancel"),
                CustomMinimumSize = new(110f, 44f),
            };
            DashboardControlTheme.ApplyButton(cancel);
            cancel.Pressed += Dismiss;
            actions.AddChild(cancel);
            _submit = new() { CustomMinimumSize = new(150f, 44f) };
            DashboardControlTheme.ApplyButton(_submit, DashboardButtonKind.Primary);
            _submit.Pressed += Submit;
            actions.AddChild(_submit);
        }

        internal void Initialize(
            DashboardRegistry registry,
            Action<DashboardConfiguration> submitted,
            Action<string, IReadOnlyDictionary<string, string>> previewOpacity,
            Action<string> restorePreview)
        {
            _registry = registry;
            _submitted = submitted;
            _previewOpacity = previewOpacity;
            _restorePreview = restorePreview;
            RebuildOptions();
        }

        internal void RebuildOptions()
        {
            if (!IsInstanceValid(_dashboard))
                return;
            var selectedDashboard = Selected(_dashboard, _dashboardIds);
            var definitions = _registry.Definitions.ToArray();
            _dashboardIds = definitions.Select(item => item.Id).ToArray();
            _dashboard.Clear();
            foreach (var definition in definitions)
                _dashboard.AddItem(ModLocalization.Get(definition.TitleLocalizationKey, definition.FallbackTitle));
            Select(_dashboard, _dashboardIds, selectedDashboard ?? BuiltInDashboardIds.Meter);

            var selectedMetric = Selected(_metric, _metricIds);
            var metrics = Main.Api.MetricDefinitions.OrderBy(item => DashboardPresentation.MetricOrder(item.Id))
                .ThenBy(item => item.Category, StringComparer.Ordinal)
                .ThenBy(item => ModLocalization.Get(item.NameLocalizationKey, item.FallbackName),
                    StringComparer.CurrentCulture)
                .ToArray();
            _metricIds = metrics.Select(item => item.Id).ToArray();
            _metric.Clear();
            foreach (var metric in metrics)
                _metric.AddItem(ModLocalization.Get(metric.NameLocalizationKey, metric.FallbackName));
            Select(_metric, _metricIds, selectedMetric ?? MetricIds.DamageContribution);
            UpdateSelectedDashboard();
        }

        internal void OpenCreate()
        {
            _editingInstanceId = null;
            _editingParameters.Clear();
            _editingStyleId = null;
            _dashboard.Disabled = false;
            Select(_dashboard, _dashboardIds, BuiltInDashboardIds.DamageContribution);
            Select(_metric, _metricIds, MetricIds.DamageContribution);
            _scope.Select(0);
            Select(_fontSize, _fontSizes, "15");
            Select(_layout, _layoutValues,
                DashboardPresentation.NormalizeLayout(ModData.Settings.DefaultDashboardLayout));
            Select(_summons, _summonValues, DashboardParameterValues.MergeSummons);
            _windowOpacity.SetValue(ModData.Settings.WindowOpacityPercent);
            _backgroundOpacity.SetValue(ModData.Settings.OpacityPercent);
            SetFullOpacityOnHover(true);
            _title.Text = ModLocalization.Get("dashboard.createTitle", "New floating dashboard");
            _submit.Text = ModLocalization.Get("dashboard.createConfirm", "Create dashboard");
            UpdateSelectedDashboard();
            Show();
            MoveToFront();
            _dashboard.GrabFocus();
        }

        internal void OpenEdit(DashboardWindowInfo info)
        {
            _editingInstanceId = info.InstanceId;
            _editingParameters = new(info.Parameters, StringComparer.Ordinal);
            _editingStyleId = info.StyleId;
            Select(_dashboard, _dashboardIds, info.DashboardId);
            _dashboard.Disabled = true;
            if (info.Parameters.TryGetValue(DashboardParameterIds.MetricId, out var metricId))
                Select(_metric, _metricIds, metricId);
            _scope.Select(info.Scope == DashboardDataScope.CurrentRun ? 1 : 0);
            var definition = _registry.Definitions.FirstOrDefault(item => item.Id == info.DashboardId);
            var fallbackFontSize = _registry.ResolveStyle(info.StyleId, definition?.DefaultStyleId).FontSize
                .ToString(CultureInfo.InvariantCulture);
            Select(_fontSize, _fontSizes,
                info.Parameters.GetValueOrDefault(DashboardParameterIds.FontSize, fallbackFontSize));
            Select(_layout, _layoutValues,
                info.Parameters.GetValueOrDefault(DashboardParameterIds.Layout,
                    DashboardParameterValues.Standard));
            Select(_summons, _summonValues,
                info.Parameters.GetValueOrDefault(DashboardParameterIds.SummonDisplay,
                    DashboardParameterValues.MergeSummons));
            _windowOpacity.SetValue(Percentage(info.Parameters, DashboardParameterIds.WindowOpacity,
                ModData.Settings.WindowOpacityPercent));
            _backgroundOpacity.SetValue(Percentage(info.Parameters, DashboardParameterIds.BackgroundOpacity,
                ModData.Settings.OpacityPercent));
            SetFullOpacityOnHover(DashboardPresentation.FullOpacityOnHover(info.Parameters));
            _title.Text = ModLocalization.Get("dashboard.editTitle", "Configure dashboard");
            _submit.Text = ModLocalization.Get("dashboard.applyChanges", "Apply changes");
            UpdateSelectedDashboard();
            Show();
            MoveToFront();
            _scope.GrabFocus();
        }

        internal void Dismiss()
        {
            if (_editingInstanceId != null)
                _restorePreview?.Invoke(_editingInstanceId);
            Close();
        }

        private void Close()
        {
            _editingInstanceId = null;
            _editingParameters.Clear();
            _editingStyleId = null;
            Hide();
        }

        public override void _UnhandledKeyInput(InputEvent input)
        {
            if (!Visible || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                return;
            Dismiss();
            GetViewport().SetInputAsHandled();
        }

        private void Submit()
        {
            var dashboardId = Selected(_dashboard, _dashboardIds);
            if (dashboardId == null)
                return;
            var metricId = Selected(_metric, _metricIds) ?? MetricIds.DamageContribution;
            var scope = _scope.Selected == 1 ? DashboardDataScope.CurrentRun : DashboardDataScope.CurrentCombat;
            var definition = _registry.Definitions.FirstOrDefault(item => item.Id == dashboardId);
            var styleId = _editingStyleId ?? definition?.DefaultStyleId ?? "ritsumetrics.compact";
            var parameters = new Dictionary<string, string>(_editingParameters, StringComparer.Ordinal)
            {
                [DashboardParameterIds.MetricId] = metricId,
                [DashboardParameterIds.FontSize] = Selected(_fontSize, _fontSizes) ?? "15",
                [DashboardParameterIds.Layout] = Selected(_layout, _layoutValues) ??
                                                 DashboardParameterValues.Standard,
                [DashboardParameterIds.SummonDisplay] = Selected(_summons, _summonValues) ??
                                                        DashboardParameterValues.MergeSummons,
                [DashboardParameterIds.WindowOpacity] =
                    _windowOpacity.Value.ToString(CultureInfo.InvariantCulture),
                [DashboardParameterIds.BackgroundOpacity] =
                    _backgroundOpacity.Value.ToString(CultureInfo.InvariantCulture),
                [DashboardParameterIds.FullOpacityOnHover] = _fullOpacityOnHover.ButtonPressed ? "true" : "false",
            };
            var configuration = new DashboardConfiguration(
                _editingInstanceId,
                dashboardId,
                scope,
                styleId,
                parameters);
            Close();
            _submitted?.Invoke(configuration);
        }

        private void UpdateSelectedDashboard()
        {
            var dashboardId = Selected(_dashboard, _dashboardIds);
            var definition = _registry.Definitions.FirstOrDefault(item => item.Id == dashboardId);
            _description.Text = definition == null
                ? string.Empty
                : ModLocalization.Get(definition.DescriptionLocalizationKey, definition.FallbackDescription);
            var needsMetric = dashboardId == BuiltInDashboardIds.Meter;
            var supportsSummons = dashboardId is BuiltInDashboardIds.Meter or
                BuiltInDashboardIds.DamageContribution or BuiltInDashboardIds.EffectiveHpDamageContribution or
                BuiltInDashboardIds.DefenseContribution;
            _metricField.Visible = needsMetric;
            _summonField.Visible = supportsSummons;
        }

        private void OnScrimInput(InputEvent input)
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                return;
            Dismiss();
            AcceptEvent();
        }

        private static DashboardDropdown AddField(VBoxContainer root, string text, out Label label)
        {
            label = new() { Text = text };
            DashboardControlTheme.ApplyFieldLabel(label);
            root.AddChild(label);
            var option = new DashboardDropdown { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            option.ApplyStyle();
            root.AddChild(option);
            return option;
        }

        private static DashboardDropdown AddFieldCell(GridContainer root, string text, out Control field)
        {
            var container = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new(260f, 0f),
            };
            container.AddThemeConstantOverride("separation", 3);
            var label = new Label { Text = text };
            DashboardControlTheme.ApplyFieldLabel(label);
            container.AddChild(label);
            var option = new DashboardDropdown { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            option.ApplyStyle(density: DashboardControlDensity.Compact);
            container.AddChild(option);
            root.AddChild(container);
            field = container;
            return option;
        }

        private static DashboardPercentageSlider AddSliderFieldCell(
            GridContainer root,
            string text,
            out Control field)
        {
            var container = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new(260f, 0f),
            };
            container.AddThemeConstantOverride("separation", 3);
            var label = new Label { Text = text };
            DashboardControlTheme.ApplyFieldLabel(label);
            container.AddChild(label);
            var slider = new DashboardPercentageSlider();
            container.AddChild(slider);
            root.AddChild(container);
            field = container;
            return slider;
        }

        private static Button AddToggleFieldCell(GridContainer root, string text, out Control field)
        {
            var container = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new(260f, 0f),
            };
            container.AddThemeConstantOverride("separation", 3);
            var label = new Label { Text = text };
            DashboardControlTheme.ApplyFieldLabel(label);
            container.AddChild(label);
            var toggle = new Button
            {
                ToggleMode = true,
                CustomMinimumSize = new(0f, 42f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            DashboardControlTheme.ApplyButton(toggle, DashboardButtonKind.Subtle);
            container.AddChild(toggle);
            root.AddChild(container);
            field = container;
            return toggle;
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

        private static int Percentage(
            IReadOnlyDictionary<string, string> parameters,
            string key,
            int fallback)
        {
            return parameters.TryGetValue(key, out var value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percentage)
                ? Math.Clamp(percentage, 0, 100)
                : Math.Clamp(fallback, 0, 100);
        }

        private void PreviewOpacity()
        {
            if (_editingInstanceId == null)
                return;
            var parameters = new Dictionary<string, string>(_editingParameters, StringComparer.Ordinal)
            {
                [DashboardParameterIds.WindowOpacity] =
                    _windowOpacity.Value.ToString(CultureInfo.InvariantCulture),
                [DashboardParameterIds.BackgroundOpacity] =
                    _backgroundOpacity.Value.ToString(CultureInfo.InvariantCulture),
                [DashboardParameterIds.FullOpacityOnHover] = _fullOpacityOnHover.ButtonPressed ? "true" : "false",
            };
            _previewOpacity?.Invoke(_editingInstanceId, parameters);
        }

        private void RestoreDefaults()
        {
            Select(_metric, _metricIds, MetricIds.DamageContribution);
            _scope.Select(0);
            var dashboardId = Selected(_dashboard, _dashboardIds);
            var definition = _registry.Definitions.FirstOrDefault(item => item.Id == dashboardId);
            var styleId = _editingStyleId ?? definition?.DefaultStyleId ?? "ritsumetrics.compact";
            var fontSize = _registry.ResolveStyle(styleId, definition?.DefaultStyleId).FontSize
                .ToString(CultureInfo.InvariantCulture);
            Select(_fontSize, _fontSizes, fontSize);
            Select(_layout, _layoutValues,
                DashboardPresentation.NormalizeLayout(ModData.Settings.DefaultDashboardLayout));
            Select(_summons, _summonValues, DashboardParameterValues.MergeSummons);
            _windowOpacity.SetValue(ModData.Settings.WindowOpacityPercent);
            _backgroundOpacity.SetValue(ModData.Settings.OpacityPercent);
            SetFullOpacityOnHover(true);
            PreviewOpacity();
        }

        private void SetFullOpacityOnHover(bool enabled)
        {
            _fullOpacityOnHover.ButtonPressed = enabled;
            UpdateFullOpacityOnHoverText(enabled);
        }

        private void UpdateFullOpacityOnHoverText(bool enabled)
        {
            _fullOpacityOnHover.Text = enabled
                ? ModLocalization.Get("dashboard.enabled", "Enabled")
                : ModLocalization.Get("dashboard.disabled", "Disabled");
            _fullOpacityOnHover.TooltipText = ModLocalization.Get("dashboard.fullOpacityOnHover.description",
                "Uses the configured opacity while idle and restores full opacity while the pointer is over this dashboard.");
        }
    }
}
