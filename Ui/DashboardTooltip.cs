// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Godot;
using STS2RitsuMetrics.Localization;

namespace STS2RitsuMetrics.Ui
{
    internal static class DashboardTooltip
    {
        private const string FallbackLayerName = "RitsuMetricsTooltipLayer";

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

        internal static void Set(Control control, IEnumerable<string> lines)
        {
            var text = string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
            control.TooltipText = string.Empty;
            if (control.MouseFilter == Control.MouseFilterEnum.Ignore)
                control.MouseFilter = Control.MouseFilterEnum.Pass;
            control.MouseEntered += () => ShowImmediate(control, text);
            control.MouseExited += () => HideImmediate(control);
            control.TreeExiting += () => HideImmediate(control);
            control.GuiInput += input =>
            {
                if (input is not InputEventScreenTouch touch)
                    return;
                if (touch.Pressed)
                    ShowImmediate(control, text);
                else
                    HideImmediate(control);
            };
        }

        internal static void ShowImmediate(Control control, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !control.IsInsideTree())
                return;
            FindOrCreateCard(control).ShowFor(control, text);
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
    }

    internal sealed partial class DashboardTooltipCard : PanelContainer
    {
        internal const string NodeName = "RitsuMetricsImmediateTooltip";
        private const float Gap = 9f;
        private readonly Label _body;
        private readonly List<(Control Owner, string Text)> _hovered = [];
        private readonly Label _title;

        internal DashboardTooltipCard()
        {
            Visible = false;
            ZIndex = 900;
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new(210f, 0f);
            AddThemeStyleboxOverride("panel", TooltipStyle());
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            content.AddThemeConstantOverride("separation", 3);
            _title = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _title.AddThemeFontOverride("font", DashboardControlTheme.EmphasisFont);
            _title.AddThemeFontSizeOverride("font_size", DashboardControlTheme.SecondaryFontSize);
            _title.AddThemeColorOverride("font_color", new("F2F6FCFF"));
            content.AddChild(_title);
            _body = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _body.AddThemeFontOverride("font", DashboardControlTheme.BodyFont);
            _body.AddThemeFontSizeOverride("font_size", DashboardControlTheme.CaptionFontSize);
            _body.AddThemeColorOverride("font_color", new("B8C7D9FF"));
            content.AddChild(_body);
            AddChild(content);
        }

        internal void ShowFor(Control owner, string text)
        {
            _hovered.RemoveAll(item => ReferenceEquals(item.Owner, owner));
            _hovered.Add((owner, text));
            ShowCurrent();
        }

        internal void HideFor(Control owner)
        {
            _hovered.RemoveAll(item => ReferenceEquals(item.Owner, owner));
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            _hovered.RemoveAll(item => !IsInstanceValid(item.Owner) || !item.Owner.IsInsideTree() ||
                                       !item.Owner.IsVisibleInTree());
            if (_hovered.Count == 0)
            {
                Hide();
                return;
            }

            var (owner, text) = _hovered[^1];
            var lines = text.Split('\n', 2, StringSplitOptions.TrimEntries);
            _title.Text = lines[0];
            _body.Text = lines.Length > 1 ? lines[1] : string.Empty;
            _body.Visible = _body.Text.Length > 0;
            ResetSize();
            Size = GetCombinedMinimumSize();
            PositionNextTo(owner);
            Show();
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
                BorderColor = new("587594F0"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = 11f,
                ContentMarginTop = 8f,
                ContentMarginRight = 11f,
                ContentMarginBottom = 8f,
                ShadowColor = new(0f, 0f, 0f, 0.68f),
                ShadowSize = 7,
            };
        }
    }
}
