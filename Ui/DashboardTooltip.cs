// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Runtime.CompilerServices;
using Godot;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal static class DashboardTooltip
    {
        private const string FallbackLayerName = "RitsuMetricsTooltipLayer";
        private static readonly ConditionalWeakTable<Control, TooltipState> States = new();

        internal static void SetValue(
            Control control,
            string title,
            decimal value,
            decimal? total = null,
            string? detail = null)
        {
            var lines = new List<string>(4)
            {
                title,
                $"{ModLocalization.Get("dashboard.tooltip.value", "Value")}: {Format(value)}",
            };
            if (total > 0m)
                lines.Add($"{ModLocalization.Get("dashboard.tooltip.share", "Share")}: {value / total:P1}");
            if (!string.IsNullOrWhiteSpace(detail))
                lines.Add(detail);
            Set(control, lines);
        }

        internal static void Set(Control control, IEnumerable<string> lines, string? badge = null)
        {
            var text = string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
            control.TooltipText = string.Empty;
            if (control.MouseFilter == Control.MouseFilterEnum.Ignore)
                control.MouseFilter = Control.MouseFilterEnum.Pass;
            var state = States.GetOrCreateValue(control);
            state.Text = text;
            state.Badge = badge;
            if (control.IsInsideTree()) FindCard(control)?.UpdateFor(control, text, badge);
            if (state.Bound)
                return;
            state.Bound = true;
            control.MouseEntered += () => ShowImmediate(control, state.Text, state.Badge);
            control.MouseExited += () => HideImmediate(control);
            control.TreeExiting += () => HideImmediate(control);
            control.GuiInput += input =>
            {
                if (input is not InputEventScreenTouch touch)
                    return;
                if (touch.Pressed)
                    ShowImmediate(control, state.Text, state.Badge);
                else
                    HideImmediate(control);
            };
        }

        internal static void SetRow(Control control, IEnumerable<string> lines, string? badge = null)
        {
            foreach (var child in control.GetChildren().OfType<Control>())
                ClearChild(child);
            Set(control, lines, badge);

            static void ClearChild(Control child)
            {
                child.TooltipText = string.Empty;
                if (States.TryGetValue(child, out var state)) state.Text = string.Empty;
                if (child.IsInsideTree()) HideImmediate(child);
                child.MouseFilter = Control.MouseFilterEnum.Ignore;
                foreach (var descendant in child.GetChildren().OfType<Control>()) ClearChild(descendant);
            }
        }

        internal static void ShowImmediate(Control control, string text, string? badge = null)
        {
            if (string.IsNullOrWhiteSpace(text) || !control.IsInsideTree())
                return;
            FindOrCreateCard(control).ShowFor(control, text, badge);
        }

        internal static void HideImmediate(Control control)
        {
            FindCard(control)?.HideFor(control);
        }

        internal static string Value(string title, decimal value, decimal? total = null, string? detail = null)
        {
            var lines = new List<string>(4)
            {
                title,
                $"{ModLocalization.Get("dashboard.tooltip.value", "Value")}: {Format(value)}",
            };
            if (total > 0m)
                lines.Add($"{ModLocalization.Get("dashboard.tooltip.share", "Share")}: {value / total:P1}");
            if (!string.IsNullOrWhiteSpace(detail))
                lines.Add(detail);
            return string.Join('\n', lines);
        }

        private static string Format(decimal value)
        {
            return value == decimal.Truncate(value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N1", CultureInfo.CurrentCulture);
        }

        private static DashboardTooltipCard FindOrCreateCard(Control control)
        {
            var layer = FindCanvasLayer(control) ?? FindOrCreateFallbackLayer(control);
            if (layer.GetNodeOrNull<DashboardTooltipCard>(DashboardTooltipCard.NodeName) is { } existing)
                return existing;
            var card = new DashboardTooltipCard { Name = DashboardTooltipCard.NodeName };
            layer.AddChild(card);
            return card;
        }

        private static DashboardTooltipCard? FindCard(Control control)
        {
            var layer = FindCanvasLayer(control) ??
                        control.GetTree().Root.GetNodeOrNull<CanvasLayer>(FallbackLayerName);
            return layer?.GetNodeOrNull<DashboardTooltipCard>(DashboardTooltipCard.NodeName);
        }

        private static CanvasLayer? FindCanvasLayer(Node node)
        {
            for (var current = node.GetParent(); current != null; current = current.GetParent())
                if (current is CanvasLayer layer)
                    return layer;

            return null;
        }

        private static CanvasLayer FindOrCreateFallbackLayer(Node node)
        {
            if (node.GetTree().Root.GetNodeOrNull<CanvasLayer>(FallbackLayerName) is { } existing)
                return existing;
            var fallback = new CanvasLayer { Name = FallbackLayerName, Layer = 250 };
            node.GetTree().Root.AddChild(fallback);
            return fallback;
        }

        private sealed class TooltipState
        {
            internal bool Bound;
            internal string Text = string.Empty;
            internal string? Badge;
        }
    }

    internal sealed partial class DashboardTooltipCard : PanelContainer
    {
        internal const string NodeName = "RitsuMetricsImmediateTooltip";
        private const float Gap = 9f;
        private const float HorizontalContentMargin = 11f;
        private const float TooltipWidth = 280f;
        private readonly VBoxContainer _body;
        private readonly List<(Control Owner, string Text, string? Badge)> _hovered = [];
        private readonly Label _title;
        private readonly Label _badge;
        private readonly ScrollContainer _scroll;
        private double _leaveElapsed;
        private float _currentWidth = TooltipWidth;
        private int _showRevision;
        private Control? _displayedOwner;
        private float _bodyHeight;
        private string? _displayedText;
        private string? _displayedBadge;

        internal DashboardTooltipCard()
        {
            Visible = false;
            ZIndex = 900;
            MouseFilter = MouseFilterEnum.Stop;
            SetProcess(false);
            CustomMinimumSize = new(TooltipWidth, 0f);
            Size = new(TooltipWidth, 1f);
            AddThemeStyleboxOverride("panel", TooltipStyle());
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            content.AddThemeConstantOverride("separation", 10);
            var heading = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            heading.AddThemeConstantOverride("separation", 12);
            _title = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _title.AddThemeFontOverride("font", DashboardControlTheme.EmphasisFont);
            _title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
            _title.AddThemeColorOverride("font_color", new("BBC9DAFF"));
            heading.AddChild(_title);
            _badge = TextLabel(string.Empty, false);
            _badge.AutowrapMode = TextServer.AutowrapMode.Off;
            _badge.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            _badge.AddThemeColorOverride("font_color", new("9DB0C5FF"));
            heading.AddChild(_badge);
            content.AddChild(heading);
            _body = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _body.AddThemeConstantOverride("separation", 8);
            _scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            DashboardControlTheme.ApplyScrollContainer(_scroll);
            _scroll.AddChild(_body);
            content.AddChild(_scroll);
            AddChild(content);
        }

        internal void ShowFor(Control owner, string text, string? badge = null)
        {
            _leaveElapsed = 0d;
            _hovered.RemoveAll(item => ReferenceEquals(item.Owner, owner));
            _hovered.Add((owner, text, badge));
            ShowCurrent();
        }

        internal void HideFor(Control owner)
        {
            if (_hovered.RemoveAll(item => ReferenceEquals(item.Owner, owner)) == 0) return;
            ShowCurrent();
        }

        internal void UpdateFor(Control owner, string text, string? badge = null)
        {
            var index = _hovered.FindIndex(item => ReferenceEquals(item.Owner, owner));
            if (index < 0)
            {
                if (Visible && ReferenceEquals(_displayedOwner, owner)) ShowContent(owner, text, badge);
                return;
            }
            if (_hovered[index].Text == text && _hovered[index].Badge == badge) return;
            _hovered[index] = (owner, text, badge);
            if (index == _hovered.Count - 1) ShowCurrent();
        }

        private void ShowCurrent()
        {
            _hovered.RemoveAll(item => !IsInstanceValid(item.Owner) || !item.Owner.IsInsideTree() ||
                                       !item.Owner.IsVisibleInTree());
            if (_hovered.Count == 0)
            {
                _leaveElapsed = 0d;
                return;
            }

            var (owner, text, badge) = _hovered[^1];
            ShowContent(owner, text, badge);
        }

        public override void _Process(double delta)
        {
            if (!Visible) return;
            if (_displayedOwner == null || !IsInstanceValid(_displayedOwner) ||
                !_displayedOwner.IsInsideTree() || !_displayedOwner.IsVisibleInTree())
            {
                Close();
                return;
            }
            // Rect hit testing includes the scroll area and grabber, unlike parent MouseExited signals.
            var pointer = GetGlobalMousePosition();
            if (GetGlobalRect().HasPoint(pointer) ||
                _hovered.Any(item => IsInstanceValid(item.Owner) && item.Owner.IsVisibleInTree() && item.Owner.GetGlobalRect().HasPoint(pointer)))
            {
                _leaveElapsed = 0d;
                return;
            }
            _leaveElapsed += delta;
            if (_leaveElapsed >= 0.4d) Close();
        }

        private void Close()
        {
            _showRevision++;
            _displayedOwner = null;
            _hovered.Clear();
            Hide();
            SetProcess(false);
        }

        private void ShowContent(Control owner, string text, string? badge)
        {
            if (Visible && ReferenceEquals(_displayedOwner, owner) &&
                _displayedText == text && _displayedBadge == badge) return;
            var scrollOffset = ReferenceEquals(_displayedOwner, owner) ? _scroll.ScrollVertical : 0;
            var lines = text.Split('\n', 2, StringSplitOptions.TrimEntries);
            _title.Text = lines[0];
            _badge.Text = badge ?? string.Empty;
            _badge.Visible = !string.IsNullOrEmpty(badge);
            var bodyText = lines.Length > 1 ? lines[1] : string.Empty;
            var viewport = owner.GetViewportRect().Size;
            var naturalWidth = bodyText.Split('\n').Select(line => MeasureWidth(line.Replace("：", ": "), false))
                .DefaultIfEmpty(0f).Max() + HorizontalContentMargin * 2 + 32f;
            _currentWidth = Math.Min(Math.Max(TooltipWidth, naturalWidth), Math.Min(560f, viewport.X - Gap * 2));
            BuildBody(bodyText);
            _body.Visible = _body.GetChildCount() > 0;
            _body.CustomMinimumSize = new(_currentWidth - HorizontalContentMargin * 2 - 16f, 0f);
            if (!ReferenceEquals(_displayedOwner, owner)) _scroll.ScrollVertical = 0;
            Size = new(_currentWidth, Visible ? Size.Y : 1f);
            _title.UpdateMinimumSize();
            _body.UpdateMinimumSize();
            UpdateMinimumSize();
            var revision = ++_showRevision;
            Callable.From(() => FinishShow(owner, text, revision, scrollOffset)).CallDeferred();
        }

        private void FinishShow(Control owner, string text, int revision, int scrollOffset)
        {
            if (revision != _showRevision ||
                !IsInstanceValid(owner) || !owner.IsInsideTree() || !owner.IsVisibleInTree())
                return;
            Size = new(_currentWidth, Math.Min(Math.Min(520f, owner.GetViewportRect().Size.Y * 0.65f),
                Math.Max(1f, _bodyHeight + _title.GetCombinedMinimumSize().Y + 34f)));
            if (!ReferenceEquals(_displayedOwner, owner)) PositionNextTo(owner);
            else Position = ClampToViewport(Position, owner.GetViewportRect().Size);
            _scroll.ScrollVertical = scrollOffset;
            _displayedOwner = owner;
            _displayedText = text;
            _displayedBadge = _badge.Visible ? _badge.Text : null;
            GetParent().MoveChild(this, -1);
            Show();
            SetProcess(true);
        }

        private void BuildBody(string text)
        {
            foreach (var child in _body.GetChildren())
            {
                _body.RemoveChild(child);
                child.QueueFree();
            }
            _bodyHeight = 0f;
            var width = _currentWidth - HorizontalContentMargin * 2 - 16f;
            var index = 0;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOfAny([':', '：']);
                if (separator < 0)
                {
                    var section = TextLabel(line, false);
                    section.AddThemeColorOverride("font_color", new("78BCECFF"));
                    Add(section, SizeLabel(section, width));
                    continue;
                }
                var name = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                var fields = value.Split(" · ");
                if (fields.Length > 2)
                {
                    var heading = TextLabel(name, false);
                    Add(heading, SizeLabel(heading, width));
                    for (var i = 0; i < fields.Length; i += 2)
                        AddPair(fields[i], i + 1 < fields.Length ? fields[i + 1] : string.Empty, false);
                }
                else
                {
                    AddPair(name, value, index == 0);
                }
                index++;
            }

            void Add(Control control, float height)
            {
                if (_body.GetChildCount() > 0) _bodyHeight += 8f;
                _bodyHeight += height;
                _body.AddChild(control);
            }

            void AddPair(string name, string value, bool emphasized)
            {
                var available = Math.Max(1f, width - 16f);
                var leftNatural = MeasureWidth(name, false);
                var rightNatural = MeasureWidth(value, emphasized);
                var (leftWidth, rightWidth) = TooltipColumnWidths(available, leftNatural, rightNatural);
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 16);
                var label = TextLabel(name, false);
                var amount = TextLabel(value, emphasized);
                amount.HorizontalAlignment = HorizontalAlignment.Right;
                var height = Math.Max(SizeLabel(label, leftWidth), SizeLabel(amount, rightWidth));
                row.AddChild(label);
                row.AddChild(amount);
                Add(row, height);
            }
        }

        internal static (float Left, float Right) TooltipColumnWidths(float available, float left, float right)
        {
            var rightWidth = left + right <= available ? right : available * right / Math.Max(1f, left + right);
            rightWidth = Math.Clamp(MathF.Ceiling(rightWidth), 1f, Math.Max(1f, available - 1f));
            return (available - rightWidth, rightWidth);
        }

        private static float MeasureWidth(string text, bool emphasized)
        {
            var font = emphasized ? DashboardControlTheme.EmphasisFont : DashboardControlTheme.BodyFont;
            var size = emphasized ? DashboardControlTheme.SecondaryFontSize + 3 : DashboardControlTheme.CaptionFontSize;
            return MathF.Ceiling(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X) + 2f;
        }

        private static float SizeLabel(Label label, float width)
        {
            var font = label.GetThemeFont("font");
            var size = label.GetThemeFontSize("font_size");
            var height = MathF.Ceiling(font.GetMultilineStringSize(label.Text, HorizontalAlignment.Left, width, size).Y);
            label.SizeFlagsHorizontal = SizeFlags.Fill;
            label.CustomMinimumSize = new(width, height);
            return height;
        }

        private static Label TextLabel(string text, bool emphasized)
        {
            var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontOverride("font", emphasized ? DashboardControlTheme.EmphasisFont : DashboardControlTheme.BodyFont);
            label.AddThemeFontSizeOverride("font_size", emphasized ? DashboardControlTheme.SecondaryFontSize + 3 : DashboardControlTheme.CaptionFontSize);
            label.AddThemeColorOverride("font_color", new(emphasized ? "F1F5FAFF" : "9DB0C5FF"));
            return label;
        }

        private void PositionNextTo(Control owner)
        {
            var anchor = owner.GetGlobalRect();
            var viewport = owner.GetViewportRect().Size;
            var placeRight = anchor.GetCenter().X <= viewport.X / 2f;
            var alignedY = anchor.Position.Y;
            var primary = ClampToViewport(new(
                placeRight ? anchor.End.X + Gap : anchor.Position.X - Size.X - Gap, alignedY), viewport);
            var secondary = ClampToViewport(new(
                placeRight ? anchor.Position.X - Size.X - Gap : anchor.End.X + Gap, alignedY), viewport);
            Vector2 selected;
            if (OverlapArea(new(primary, Size), anchor) <= 0f)
                selected = primary;
            else if (OverlapArea(new(secondary, Size), anchor) <= 0f)
                selected = secondary;
            else
                selected = ClampToViewport(new(anchor.GetCenter().X - Size.X / 2f,
                    anchor.GetCenter().Y <= viewport.Y / 2f
                        ? anchor.End.Y + Gap
                        : anchor.Position.Y - Size.Y - Gap), viewport);
            Position = new(MathF.Round(selected.X), MathF.Round(selected.Y));
        }

        private Vector2 ClampToViewport(Vector2 position, Vector2 viewport)
        {
            var maximumX = Math.Max(Gap, viewport.X - Size.X - Gap);
            var maximumY = Math.Max(Gap, viewport.Y - Size.Y - Gap);
            return new(Math.Clamp(position.X, Gap, maximumX), Math.Clamp(position.Y, Gap, maximumY));
        }

        private static float OverlapArea(Rect2 first, Rect2 second)
        {
            var width = Math.Max(0f, Math.Min(first.End.X, second.End.X) -
                                     Math.Max(first.Position.X, second.Position.X));
            var height = Math.Max(0f, Math.Min(first.End.Y, second.End.Y) -
                                      Math.Max(first.Position.Y, second.Position.Y));
            return width * height;
        }

        private static StyleBoxFlat TooltipStyle()
        {
            return new()
            {
                BgColor = new("0A111BF8"),
                BorderColor = new("56A8E8E6"),
                BorderWidthTop = 2,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = HorizontalContentMargin,
                ContentMarginTop = 8f,
                ContentMarginRight = HorizontalContentMargin,
                ContentMarginBottom = 8f,
                ShadowColor = new(0f, 0f, 0f, 0.68f),
                ShadowSize = 7,
            };
        }
    }
}
