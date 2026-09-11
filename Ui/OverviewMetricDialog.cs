// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class OverviewMetricDialog : Control
    {
        private readonly Panel _panel = new() { MouseFilter = MouseFilterEnum.Stop, ClipContents = true };
        private readonly LineEdit _search = new();
        private readonly DashboardDropdown _category = new();
        private readonly DashboardScrollContainer _scroll = new();
        private readonly VBoxContainer _rows = new();
        private readonly Label _count = new();
        private readonly Button _apply = new();
        private readonly Button _charts = new() { ToggleMode = true };
        private readonly Button _analysis = new() { ToggleMode = true };
        internal static int OpenDialogs { get; private set; }
        private readonly MetricDefinition[] _definitions;
        private readonly string[] _categories;
        private readonly List<string> _selected;
        private readonly Action<string[], bool, bool> _save;
        private bool _selectedOnly;

        internal OverviewMetricDialog(MetricDefinition[] definitions, string[] selected, bool charts, bool analysis,
            Action<string[], bool, bool> save)
        {
            _definitions = definitions;
            _selected = selected.ToList();
            _save = save;
            _categories = ["", .. definitions.Select(item => item.Category).Distinct().Order()];
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            MouseForcePassScrollEvents = false;
            var scrim = new ColorRect { Color = new("03070DCC"), MouseFilter = MouseFilterEnum.Stop };
            scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(scrim);
            scrim.GuiInput += input =>
            {
                scrim.AcceptEvent();
                if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
                    QueueFree();
            };
            AddChild(_panel);
            _panel.AddThemeStyleboxOverride("panel", DashboardControlTheme.DialogStyle());
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 10);
            _panel.AddChild(body);
            body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            body.OffsetLeft = 20;
            body.OffsetRight = -20;
            body.OffsetTop = 18;
            body.OffsetBottom = -18;
            _panel.MouseForcePassScrollEvents = false;
            var title = new Label { Text = T("overview.configure", "Displayed metrics") };
            DashboardControlTheme.ApplyDialogTitle(title);
            var header = new HBoxContainer();
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddChild(title);
            var close = new Button { TooltipText = T("dashboard.cancelEdit", "Cancel") };
            DashboardControlTheme.ApplyIconButton(close, compact: true);
            DashboardIcons.ApplyIconOnly(close, DashboardIcon.Close);
            close.Pressed += QueueFree;
            header.AddChild(close);
            body.AddChild(header);
            var hint = new Label
            {
                Text = T("overview.configureHint", "Choose up to 16 metrics. Use the arrows to reorder; changes apply to all overviews."),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            DashboardControlTheme.ApplySecondaryText(hint);
            body.AddChild(hint);
            var presets = new HFlowContainer();
            body.AddChild(presets);
            AddButton(presets, T("overview.preset.default", "Balanced"), () => Preset(OverviewMetricSelection.Defaults));
            AddButton(presets, T("overview.preset.support", "Team support"), () => Preset(OverviewMetricSelection.SupportPreset));
            var only = new Button { ToggleMode = true, Text = T("overview.selectedOnly", "Selected only") };
            DashboardControlTheme.ApplyButton(only, DashboardButtonKind.Subtle, true);
            presets.AddChild(only);
            only.Toggled += value => { _selectedOnly = value; Rebuild(); };
            var filters = new HBoxContainer();
            body.AddChild(filters);
            _search.PlaceholderText = T("overview.searchMetrics", "Search metrics");
            _search.ClearButtonEnabled = true;
            _search.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            DashboardControlTheme.ApplySearch(_search, density: DashboardControlDensity.Compact);
            filters.AddChild(_search);
            _category.CustomMinimumSize = new(130, 0);
            _category.ApplyStyle(density: DashboardControlDensity.Compact);
            foreach (var category in _categories)
                _category.AddItem(T("metric.category." + category, category.Length == 0 ? "All categories" : category));
            _category.Select(0);
            filters.AddChild(_category);
            _search.TextChanged += _ => Rebuild();
            _category.ItemSelected += _ => Rebuild();
            body.AddChild(_scroll);
            _scroll.MouseForcePassScrollEvents = false;
            _scroll.SetContent(_rows);
            DashboardControlTheme.ApplySecondaryText(_count);
            body.AddChild(_count);
            var sections = new HFlowContainer();
            body.AddChild(sections);
            _charts.ButtonPressed = charts;
            _analysis.ButtonPressed = analysis;
            ConfigureToggle(_charts, "overview.showCharts", "Show charts");
            ConfigureToggle(_analysis, "overview.showAnalysis", "Show combat analysis");
            sections.AddChild(_charts);
            sections.AddChild(_analysis);
            var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            body.AddChild(actions);
            AddButton(actions, T("dashboard.cancelEdit", "Cancel"), QueueFree);
            _apply.Text = T("overview.apply", "Apply");
            DashboardControlTheme.ApplyButton(_apply, DashboardButtonKind.Primary);
            actions.AddChild(_apply);
            _apply.Pressed += () => { _save(_selected.ToArray(), _charts.ButtonPressed, _analysis.ButtonPressed); QueueFree(); };
            Resized += FitPanel;
            Rebuild();
        }

        public override void _Ready()
        {
            OpenDialogs++;
            FitPanel();
            _search.GrabFocus();
        }

        public override void _ExitTree()
        {
            OpenDialogs = Math.Max(0, OpenDialogs - 1);
        }

        public override void _UnhandledInput(InputEvent input)
        {
            GetViewport().SetInputAsHandled();
        }

        public override void _Input(InputEvent input)
        {
            if (input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                return;
            GetViewport().SetInputAsHandled();
            QueueFree();
        }

        private void FitPanel()
        {
            var viewport = Size;
            var panelSize = new Vector2(Math.Min(760, viewport.X - 32), Math.Min(720, viewport.Y - 32));
            _panel.Size = panelSize;
            _panel.Position = (viewport - panelSize) / 2;
        }

        private void Preset(IEnumerable<string> values)
        {
            _selected.Clear();
            _selected.AddRange(values);
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (var child in _rows.GetChildren())
            {
                _rows.RemoveChild(child);
                child.QueueFree();
            }
            var search = _search.Text.Trim();
            var category = _categories[Math.Max(0, _category.Selected)];
            var definitions = _definitions.Where(item => (!_selectedOnly || _selected.Contains(item.Id)) &&
                    (category.Length == 0 || item.Category == category) &&
                    (search.Length == 0 || MetricLabel(item).Contains(search, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(item => _selected.Contains(item.Id) ? _selected.IndexOf(item.Id) : int.MaxValue)
                .ThenBy(item => item.Category).ThenBy(MetricLabel).ToArray();
            foreach (var item in definitions)
            {
                var index = _selected.IndexOf(item.Id);
                var row = new HBoxContainer();
                _rows.AddChild(row);
                var toggle = new Button
                {
                    ToggleMode = true,
                    Text = index >= 0 ? $"✓  {index + 1}. {MetricLabel(item)}" : MetricLabel(item),
                    ButtonPressed = index >= 0,
                    Disabled = index < 0 && _selected.Count >= OverviewMetricSelection.MaximumSelected,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    ClipText = true,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    TooltipText = MetricLabel(item) + "\n" + T(item.NameLocalizationKey + ".description", MetricLabel(item)),
                };
                DashboardControlTheme.ApplySelectionItem(toggle, index >= 0);
                row.AddChild(toggle);
                toggle.Toggled += enabled =>
                {
                    if (enabled) _selected.Add(item.Id);
                    else _selected.Remove(item.Id);
                    Rebuild();
                };
                if (index >= 0)
                {
                    AddMove(row, item.Id, index, -1, "↑");
                    AddMove(row, item.Id, index, 1, "↓");
                }
                else
                    row.AddChild(new Control { CustomMinimumSize = new(84, 0), MouseFilter = MouseFilterEnum.Ignore });
            }
            if (definitions.Length == 0)
                _rows.AddChild(new Label { Text = T("overview.noMetrics", "No matching metrics") });
            _count.Text = ModLocalization.Format("overview.metricCount", "{0} / {1} selected · N/A means unavailable or incomplete data",
                _selected.Count, OverviewMetricSelection.MaximumSelected);
            _apply.Disabled = _selected.Count == 0;
            _scroll.InvalidateContentSize();
        }

        private void AddMove(HBoxContainer row, string id, int index, int offset, string text)
        {
            var button = new Button
            {
                Text = text,
                Disabled = index < 0 || index + offset < 0 || index + offset >= _selected.Count,
                TooltipText = T(offset < 0 ? "overview.moveUp" : "overview.moveDown", offset < 0 ? "Move earlier" : "Move later"),
            };
            DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Subtle, true);
            row.AddChild(button);
            button.Pressed += () =>
            {
                var current = _selected.IndexOf(id);
                (_selected[current], _selected[current + offset]) = (_selected[current + offset], _selected[current]);
                Rebuild();
            };
        }

        private static void AddButton(Container parent, string text, Action action)
        {
            var button = new Button { Text = text };
            DashboardControlTheme.ApplyButton(button, DashboardButtonKind.Subtle, true);
            button.Pressed += action;
            parent.AddChild(button);
        }

        private static void ConfigureToggle(Button button, string key, string fallback)
        {
            Update();
            button.Toggled += _ => Update();
            void Update()
            {
                button.Text = T(key, fallback) + " · " + (button.ButtonPressed
                    ? T("dashboard.enabled", "On") : T("dashboard.disabled", "Off"));
                DashboardControlTheme.ApplyButton(button,
                    button.ButtonPressed ? DashboardButtonKind.Primary : DashboardButtonKind.Subtle, true);
            }
        }

        private static string MetricLabel(MetricDefinition definition) => T(definition.NameLocalizationKey, definition.FallbackName);
        private static string T(string key, string fallback) => ModLocalization.Get(key, fallback);
    }
}
