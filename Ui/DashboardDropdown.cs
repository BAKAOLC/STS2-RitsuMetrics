// SPDX-License-Identifier: MPL-2.0

using Godot;
using STS2RitsuMetrics.Api;

namespace STS2RitsuMetrics.Ui
{
    public sealed partial class DashboardDropdown : Button
    {
        private const int PopupPadding = 4;
        private const int PopupRowSeparation = 2;

        private readonly List<string> _items = [];
        private readonly PopupPanel _popup;
        private readonly VBoxContainer _rows;
        private readonly DashboardScrollContainer _scroll;
        private DashboardStyleDefinition? _style;

        public DashboardDropdown()
        {
            Alignment = HorizontalAlignment.Left;
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            ClipText = true;
            _popup = new()
            {
                Borderless = true,
                Unresizable = true,
                TransparentBg = true,
                WrapControls = true,
            };
            var margin = new MarginContainer
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
            };
            margin.AddThemeConstantOverride("margin_left", PopupPadding);
            margin.AddThemeConstantOverride("margin_top", PopupPadding);
            margin.AddThemeConstantOverride("margin_right", PopupPadding);
            margin.AddThemeConstantOverride("margin_bottom", PopupPadding);
            _popup.AddChild(margin);
            _scroll = new()
            {
                FollowFocus = true,
            };
            margin.AddChild(_scroll);
            _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _rows.AddThemeConstantOverride("separation", PopupRowSeparation);
            _scroll.SetContent(_rows);
            AddChild(_popup);
            Pressed += TogglePopup;
            ApplyStyle();
        }

        public int Selected { get; private set; } = -1;

        public int ItemCount => _items.Count;

        public int MaxPopupHeight { get; set; } = 420;

        public int MinimumPopupWidth { get; set; } = 240;

        public int PopupRowHeight { get; set; } = 44;

        public float PopupViewportFraction { get; set; } = 0.55f;

        public event Action<long>? ItemSelected;

        public void AddItem(string text)
        {
            _items.Add(text);
            if (Selected >= 0)
                return;
            Selected = 0;
            Text = text;
        }

        public void Clear()
        {
            _popup.Hide();
            _items.Clear();
            Selected = -1;
            Text = string.Empty;
            ClearRows();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                Selected = -1;
                Text = string.Empty;
                return;
            }

            Selected = index;
            Text = _items[index];
        }

        public void ApplyStyle(DashboardStyleDefinition? style = null,
            DashboardControlDensity density = DashboardControlDensity.Comfortable)
        {
            _style = style;
            DashboardControlTheme.ApplySelector(this, style, density);
            DashboardControlTheme.ApplySelectionPopup(_popup, style);
        }

        private void TogglePopup()
        {
            if (_popup.Visible)
            {
                _popup.Hide();
                return;
            }

            if (_items.Count == 0)
                return;
            _popup.Theme = ResolveTypographyTheme() ?? DashboardControlTheme.CreateTypographyTheme();
            RebuildRows();
            var viewport = GetViewport().GetVisibleRect();
            var anchor = GetGlobalRect();
            const int outerPadding = PopupPadding * 2;
            var minimumHeight = PopupRowHeight * 2 + outerPadding + PopupRowSeparation;
            var viewportHeight = Math.Max(minimumHeight, (int)(viewport.Size.Y * PopupViewportFraction));
            var maximumHeight = Math.Max(minimumHeight, Math.Min(MaxPopupHeight, viewportHeight));
            var contentHeight = outerPadding + _items.Count * PopupRowHeight +
                                Math.Max(0, _items.Count - 1) * PopupRowSeparation;
            var height = Math.Min(maximumHeight, contentHeight);
            var maximumWidth = Math.Max(1, (int)viewport.Size.X - 12);
            var width = Math.Min(maximumWidth,
                Math.Max(MinimumPopupWidth, (int)Math.Ceiling(anchor.Size.X)));
            var below = viewport.End.Y - anchor.End.Y - 5f;
            var above = anchor.Position.Y - viewport.Position.Y - 5f;
            var openBelow = below >= Math.Min(height, minimumHeight) || below >= above;
            var availableHeight = Math.Max(PopupRowHeight + outerPadding, (int)(openBelow ? below : above));
            height = Math.Min(height, availableHeight);
            var x = Math.Clamp((int)Math.Floor(anchor.Position.X), (int)viewport.Position.X + 6,
                Math.Max((int)viewport.Position.X + 6, (int)viewport.End.X - width - 6));
            var y = openBelow
                ? (int)Math.Ceiling(anchor.End.Y + 4f)
                : (int)Math.Floor(anchor.Position.Y - height - 4f);
            var size = new Vector2I(width, height);
            _popup.MinSize = Vector2I.Zero;
            _popup.MaxSize = size;
            _popup.Popup(new Rect2I(new(x, y), size));
            if (Selected >= 0 && Selected < _rows.GetChildCount() && _rows.GetChild(Selected) is Control selectedRow)
                Callable.From(() => FocusSelectedRow(selectedRow)).CallDeferred();
        }

        private void RebuildRows()
        {
            ClearRows();
            for (var index = 0; index < _items.Count; index++)
            {
                var itemIndex = index;
                var button = new Button
                {
                    Text = _items[index],
                    TooltipText = _items[index],
                    CustomMinimumSize = new(0f, PopupRowHeight),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    FocusMode = FocusModeEnum.All,
                };
                DashboardControlTheme.ApplySelectionItem(button, index == Selected, _style);
                button.Pressed += () => SelectFromPopup(itemIndex);
                _rows.AddChild(button);
            }
        }

        private void SelectFromPopup(int index)
        {
            _popup.Hide();
            Select(index);
            ItemSelected?.Invoke(index);
        }

        private void FocusSelectedRow(Control row)
        {
            if (!_popup.Visible || !IsInstanceValid(row))
                return;
            _scroll.EnsureControlVisible(row);
            row.GrabFocus();
        }

        private Theme? ResolveTypographyTheme()
        {
            for (Node? current = this; current != null; current = current.GetParent())
                switch (current)
                {
                    case Control { Theme: not null } control:
                        return control.Theme;
                    case Window { Theme: not null } window:
                        return window.Theme;
                }

            return null;
        }

        private void ClearRows()
        {
            foreach (var child in _rows.GetChildren())
            {
                _rows.RemoveChild(child);
                child.QueueFree();
            }
        }
    }
}
